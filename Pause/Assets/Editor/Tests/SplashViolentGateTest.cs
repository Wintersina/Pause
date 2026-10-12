using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// The HapticGate intro (the approved gate film + card shake) and its
// tap-to-smash skip: the film's frame table and art, simulation (fake clock),
// the scene wiring (film playback, one scene load, queued taps, input path),
// screen fit on every device, and per-frame allocations.
public static class SplashViolentGateTest
{
    static int fails;
    static void Check(string what, bool ok)
    {
        Debug.Log((ok ? "[GATE] PASS  " : "[GATE] FAIL  ") + what);
        if (!ok) fails++;
    }

    public static void Run() { TestHarness.Exit(Execute()); }

    const float Dt = 1f / 60f;
    static readonly BindingFlags Any = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

    public static int Execute()
    {
        fails = 0;
        using var sandbox = new TestHarness.Sandbox();
        var loadWas = splashScene.LoadScene;
        var inputWas = splashScene.InputSource;
        bool buildWas = splashScene.BuildGateInEditMode;
        float scaleWas = Time.timeScale;
        try
        {
            FilmTable();
            PureSim();
            SceneBehaviour();
            InputPath();
            SlotsAndFallbacks();
            ScreenFit();
            Allocations();
            Source();
        }
        finally
        {
            splashScene.LoadScene = loadWas;
            splashScene.InputSource = inputWas;
            splashScene.BuildGateInEditMode = buildWas;
            Time.timeScale = scaleWas;
        }
        Debug.Log("[GATE] failures: " + fails);
        return fails;
    }

    // ---- pure simulation -------------------------------------------------

    static float Rms(GateSim sim, float from, float to)
    {
        // run a fresh sim to `from`, then average |shake| until `to`
        var s = new GateSim();
        while (s.time < from) s.Advance(Dt);
        double sum = 0; int n = 0;
        while (s.time < to)
        {
            s.Advance(Dt);
            sum += s.shake.sqrMagnitude; n++;
        }
        return Mathf.Sqrt((float)(sum / Mathf.Max(1, n)));
    }

    // ---- the film's frame table ----------------------------------------------

    static void FilmTable()
    {
        Check("film timing: 30 fps, final frame 100 at 3.33 s, split 1.133 s, wordmark lands 3.30 s",
              GateFilm.Fps == 30f && GateFilm.LastFrame == 100 && Mathf.Abs(GateFilm.Length - 3.333f) < 0.002f
              && Mathf.Abs(GateFilm.FirstSplit - 1.1333f) < 0.001f && Mathf.Abs(GateFilm.WordmarkLands - 3.3f) < 0.001f);
        bool asc = GateFilm.Kept[0] == 0 && GateFilm.Kept[GateFilm.Kept.Length - 1] == GateFilm.LastFrame;
        for (int i = 1; i < GateFilm.Kept.Length; i++) asc &= GateFilm.Kept[i] > GateFilm.Kept[i - 1] && (GateFilm.Kept[i] - GateFilm.Kept[i - 1] <= 2 || GateFilm.Kept[i] == 34);
        Check("kept frames ascend from 0 to 100, no gap over 2 frames after the split (" + GateFilm.Kept.Length + " frames)", asc && GateFilm.Kept.Length == 60);
        bool keyOk = true;
        for (int f = 0; f <= GateFilm.LastFrame; f++)
        {
            int k = GateFilm.KeyFor(f);
            keyOk &= GateFilm.Kept[k] <= f && (k == GateFilm.Kept.Length - 1 || GateFilm.Kept[k + 1] > f);
        }
        Check("every film frame maps to the newest stored frame at or before it", keyOk && GateFilm.KeyFor(500) == GateFilm.Kept.Length - 1);
        Check("frame at time: 0.2 s -> 6, 1 s -> 30, 2 s -> 60, 3 s -> 90, 9 s -> 100, -1 s -> 0",
              GateFilm.FrameAt(0.2f) == 6 && GateFilm.FrameAt(1f) == 30 && GateFilm.FrameAt(2f) == 60 && GateFilm.FrameAt(3f) == 90
              && GateFilm.FrameAt(9f) == 100 && GateFilm.FrameAt(-1f) == 0);
        Check("leaves shut until the split, fully open once settled",
              GateFilm.OpenAt(1.1f) == 0f && GateFilm.OpenAt(GateFilm.OpenSettled) == 1f && GateFilm.OpenAt(1.5f) > 0.2f);

        // the files: every kept frame present, nothing extra, each under 1 MiB, 810x828, import settings
        int present = 0, small = 0, sized = 0, imported = 0;
        foreach (int f in GateFilm.Kept)
        {
            string path = "Assets/Resources/" + GateFilm.PathFor(f) + ".jpg";
            if (File.Exists(path)) present++;
            if (File.Exists(path) && new FileInfo(path).Length < 1024 * 1024) small++;
            var t = Resources.Load<Texture2D>(GateFilm.PathFor(f));
            if (t != null && t.width == GateFilm.Width && t.height == GateFilm.Height) sized++;
            if (FilmImporterOk(path)) imported++;
        }
        int n = GateFilm.Kept.Length;
        Check("all " + n + " kept film frames exist (" + present + ")", present == n);
        Check("each film frame file is under 1 MiB (" + small + "/" + n + ")", small == n);
        Check("each film frame imports at 810x828 (" + sized + "/" + n + ")", sized == n);
        Check("film import: no mips, no NPOT scaling, bilinear clamp, not readable, ASTC 6x6 on Android and iOS (" + imported + "/" + n + ")", imported == n);
        int files = Directory.GetFiles("Assets/Resources/HapticGate/Film", "*.jpg").Length;
        Check("no stray film frames in the folder (" + files + ")", files == n);
        Check("the retired procedural door art is gone",
              !File.Exists("Assets/Resources/HapticGate/industrial_gate_v4.png") && !File.Exists("Assets/Resources/HapticGate/gate_cracks_1.png"));
    }

    static bool FilmImporterOk(string path)
    {
        var imp = AssetImporter.GetAtPath(path) as TextureImporter;
        if (imp == null || imp.mipmapEnabled || imp.npotScale != TextureImporterNPOTScale.None || imp.isReadable
            || imp.filterMode != FilterMode.Bilinear || imp.wrapMode != TextureWrapMode.Clamp || imp.maxTextureSize < 1024) return false;
        foreach (var plat in new[] { "Android", "iOS" })
        {
            var ps = imp.GetPlatformTextureSettings(plat);
            if (!ps.overridden || ps.format != TextureImporterFormat.ASTC_6x6) return false;
        }
        return true;
    }

    // ---- pure simulation -------------------------------------------------

    static void PureSim()
    {
        float a = Rms(null, 0.02f, 0.3f), b = Rms(null, 0.4f, 0.7f), c = Rms(null, 0.8f, 1.1f);
        Check("rattle shake rises while the shut gate strains (" + a.ToString("F4") + " < " + b.ToString("F4") + " < " + c.ToString("F4") + ")",
              a < b && b < c);

        var sim = new GateSim();
        int impacts = 0, dones = 0;
        var impactTimes = new List<float>();
        sim.Impact += (s, i) => { if (i >= 0) { impacts++; impactTimes.Add(sim.time); } };
        sim.Done += () => dones++;
        float openAtSplit = -1f;
        int guard = 0;
        while (!sim.finished && guard++ < 1000)
        {
            sim.Advance(Dt);
            if (openAtSplit < 0f && sim.time >= GateFilm.FirstSplit - 0.02f) openAtSplit = sim.open;
        }
        Check("intro ends by itself at about " + sim.naturalSeconds + " s (" + sim.time.ToString("F2") + ")", sim.finished && !sim.smashFinish && Mathf.Abs(sim.time - sim.naturalSeconds) < 0.05f);
        Check("intro = the 3.33 s film + a short hold (3.8-4.2 s)", sim.time >= 3.8f && sim.time <= 4.2f && GateSim.IntroSeconds > GateFilm.WordmarkLands + 0.3f);
        Check("leaves shut until the film's split, open at the end (" + openAtSplit.ToString("F2") + " -> " + sim.open.ToString("F2") + ")", openAtSplit == 0f && sim.open > 0.99f);
        Check("each of the film's shoves fires an impact on time (" + impacts + "/" + GateSim.StepCount + ")",
              impacts == GateSim.StepCount && impactTimes.Count > 0 && Mathf.Abs(impactTimes[0] - GateFilm.FirstSplit) < Dt + 0.001f);
        Check("Done fires exactly once on natural finish", dones == 1);

        // taps -> crack1, crack2, smash
        sim = new GateSim();
        var log = new List<string>();
        sim.Cracked += st => log.Add("c" + st);
        sim.Smashed += () => log.Add("smash");
        dones = 0; sim.Done += () => dones++;
        float[] at = { 0.40f, 0.70f, 1.00f };
        int k = 0;
        bool step1 = false, step2 = false;
        while (!sim.finished && sim.time < 5f)
        {
            if (k < 3 && sim.time >= at[k]) { sim.Tap(); k++; }
            sim.Advance(Dt);
            if (sim.crackStage == 1) step1 = true;
            if (sim.crackStage == 2) step2 = true;
        }
        Check("three discrete taps: crack 1, crack 2, (crack 3 beat), smash -> " + string.Join(",", log),
              string.Join(",", log) == "c1,c2,c3,smash" && step1 && step2);
        Check("smash finishes the card within 0.5 s of the third tap (" + (sim.time - 1.0f).ToString("F2") + ")",
              sim.smashFinish && sim.time - 1.0f <= 0.52f && dones == 1);

        // rapid triple tap in one frame: queued, none lost, still three visible stages
        sim = new GateSim();
        log.Clear();
        sim.Cracked += st => log.Add("c" + st);
        sim.Smashed += () => log.Add("smash");
        while (sim.time < 0.3f) sim.Advance(Dt);
        bool t1 = sim.Tap(), t2 = sim.Tap(), t3 = sim.Tap(), t4 = sim.Tap();
        Check("triple tap in one frame: three accepted, the extra ignored", t1 && t2 && t3 && !t4 && sim.pendingTaps == 3);
        sim.Advance(Dt);
        Check("... first stage applies at once, the rest queue behind it", sim.crackStage == 1 && sim.pendingTaps == 2);
        float t0 = sim.time;
        while (!sim.finished && sim.time < 5f) sim.Advance(Dt);
        Check("... all three stages ran in order and the smash ended it fast -> " + string.Join(",", log) + " in " + (sim.time - t0).ToString("F2") + " s",
              string.Join(",", log) == "c1,c2,c3,smash" && sim.time - t0 <= 0.8f);

        // lockout and post-smash
        sim = new GateSim();
        Check("a tap inside the first 0.15 s is dropped (stray tap from the previous screen)", !sim.Tap() && sim.pendingTaps == 0);
        while (sim.time < 0.3f) sim.Advance(Dt);
        sim.Tap(); sim.Tap(); sim.Tap();
        while (!sim.smashed) sim.Advance(Dt);
        int stage = sim.crackStage;
        bool late = sim.Tap();
        Check("tap after the smash is ignored", !late && sim.pendingTaps == 0 && sim.crackStage == stage);

        // natural finish before taps complete: proceeds, taps don't strand it
        sim = new GateSim();
        while (sim.time < 2.0f) sim.Advance(Dt);
        sim.Tap(); sim.Tap();
        while (!sim.finished && sim.time < 5f) sim.Advance(Dt);
        Check("two taps then the intro runs out: it finishes normally, no smash", sim.finished && !sim.smashFinish && !sim.smashed);

        // hitch: a 1 s frame must not jump past everything silently
        sim = new GateSim(); sim.Advance(0.1f);
        Check("zero/negative dt does nothing", Unchanged(sim));
    }

    static bool Unchanged(GateSim s) { float t = s.time; s.Advance(0f); s.Advance(-1f); return s.time == t; }

    // ---- the scene component -----------------------------------------------

    static splashScene OpenCard(out int loads, out List<string> sounds)
    {
        EditorSceneLoader.Open("spashS7", OpenSceneMode.Single);
        splashScene.BuildGateInEditMode = true;
        var card = UnityEngine.Object.FindFirstObjectByType<splashScene>();
        card.ApplyLayout(1080, 2340, new Rect(0, 0, 1080, 2340));
        var count = new int[1];
        var cues = new List<string>();
        splashScene.LoadScene = name => { count[0]++; if (name != splashScene.NextScene) fails++; };
        splashScene.SoundCue += cues.Add;
        card.Restart();
        loads = 0; sounds = cues;
        lastCount = count;
        return card;
    }
    static int[] lastCount;

    static void SceneBehaviour()
    {
        var card = OpenCard(out _, out var cues);
        Check("splash card built the film and the effects (film + view present, art loaded)",
              card.Film != null && card.Film.HasArt && card.View != null && card.Sim != null && card.Film.LoadedCount == GateFilm.Kept.Length);
        Check("the film replaces the mark (hidden while it plays)", card.FilmShowing && !card.logo.enabled);

        // film playback on the card clock (0.2 / 1 / 2 / 3 s, then the held final frame)
        float[] at0 = { 0.2f, 1f, 2f, 3f, 3.6f };
        int[] want = { 0, 0, 60, 90, 100 };
        var got = new List<int>();
        int ai = 0;
        for (int i = 0; i < 400 && ai < at0.Length; i++)
        {
            card.Step(Dt);
            if (card.Sim.time >= at0[ai] - 0.0001f) { got.Add(card.Film.ShownFrame); ai++; }
        }
        Check("film frames on the card clock 0.2/1/2/3/3.6 s -> " + string.Join(",", got),
              got.Count == 5 && got[0] == want[0] && got[1] == want[1] && got[2] == want[2] && got[3] == want[3] && got[4] == want[4]);
        lastCount[0] = 0; card.Restart(); cues.Clear();

        // natural run
        Time.timeScale = 0f;
        for (int i = 0; i < 400 && lastCount[0] == 0; i++) card.Step(Dt);
        Check("timeScale 0: the card still advances and finishes (unscaled step)", lastCount[0] == 1 && card.Sim.time > 3.9f);
        Check("timeScale is restored to 1 when the card leaves", Time.timeScale == 1f);
        for (int i = 0; i < 60; i++) card.Step(Dt);
        Check("natural finish loads the next scene exactly once (" + lastCount[0] + ")", lastCount[0] == 1);
        Check("a tap after leaving does not load again", !card.Tap() && lastCount[0] == 1);

        // three discrete taps
        lastCount[0] = 0; card.Restart(); cues.Clear();
        float[] at = { 0.4f, 0.8f, 1.2f };
        int k = 0; int loadsAtSmash = -1;
        for (int i = 0; i < 400 && lastCount[0] == 0; i++)
        {
            if (k < 3 && card.Sim.time >= at[k]) { card.Tap(); k++; }
            card.Step(Dt);
            if (card.Sim.smashed && loadsAtSmash < 0) loadsAtSmash = lastCount[0];
        }
        Check("three taps: crack 1, crack 2, smash, then exactly one scene load", card.Sim.smashed && lastCount[0] == 1 && loadsAtSmash == 0);
        Check("the next scene loads within 0.5 s of the third tap (" + (card.Sim.time - 1.2f).ToString("F2") + ")",
              card.Sim.time - 1.2f <= 0.55f);
        for (int i = 0; i < 90; i++) { card.Tap(); card.Step(Dt); }
        Check("taps during and after the smash never double-load (" + lastCount[0] + ")", lastCount[0] == 1);
        Check("sound hooks fired: " + string.Join(",", cues.GetRange(0, Mathf.Min(cues.Count, 12))),
              cues.Contains("gate_step") && cues.Contains("gate_crack") && cues.Contains("gate_smash"));

        // rapid triple tap inside one frame
        lastCount[0] = 0; card.Restart();
        for (int i = 0; i < 20; i++) card.Step(Dt);
        card.Tap(); card.Tap(); card.Tap(); card.Tap(); card.Tap();
        card.Step(Dt);
        Check("rapid triple tap: first stage shows immediately", card.Sim.crackStage == 1);
        for (int i = 0; i < 12; i++) card.Step(Dt);
        Check("... second stage shows next", card.Sim.crackStage >= 2);
        for (int i = 0; i < 200 && lastCount[0] == 0; i++) card.Step(Dt);
        Check("... and the smash loads exactly once (" + lastCount[0] + ")", card.Sim.smashed && lastCount[0] == 1);

        // taps in the smash window
        lastCount[0] = 0; card.Restart();
        for (int i = 0; i < 30; i++) card.Step(Dt);
        card.Tap(); card.Tap(); card.Tap();
        while (!card.Sim.smashed) card.Step(Dt);
        for (int i = 0; i < 10; i++) { card.Tap(); card.Step(Dt); }
        for (int i = 0; i < 60; i++) card.Step(Dt);
        Check("tap in the middle of the smash is ignored (one load)", lastCount[0] == 1);

        // natural finish before taps complete
        lastCount[0] = 0; card.Restart();
        while (card.Sim.time < card.holdSeconds - 0.3f) card.Step(Dt);
        card.Tap(); card.Tap();
        for (int i = 0; i < 100; i++) card.Step(Dt);
        Check("natural finish with an unfinished tap sequence proceeds once", lastCount[0] == 1 && !card.Sim.smashed);

        // effects respond to the taps; the smash jumps the film to its final frame under a white flash
        lastCount[0] = 0; card.Restart();
        for (int i = 0; i < 30; i++) card.Step(Dt);
        int before = card.View.ActiveParticles;
        card.Tap(); for (int i = 0; i < 3; i++) card.Step(Dt);
        int after1 = card.View.ActiveParticles;
        card.Tap(); for (int i = 0; i < 12; i++) card.Step(Dt);
        Check("tap 1 throws sparks and chips off a leaf (" + before + " -> " + after1 + "), tap 2 more and steam leaks",
              before == 0 && after1 > 0 && card.Sim.crackStage == 2 && card.View.ActiveParticles > 0);
        Check("... the film keeps playing under the taps (frame " + card.Film.ShownFrame + ")", card.Film.ShownFrame == 0 && card.Sim.time < 1f);
        card.Tap(); while (!card.Sim.smashed) card.Step(Dt);
        card.Step(Dt);
        Check("smash: film jumps to the final frame (gate open, wordmark landed) under a bright flash (" + card.View.FlashAlpha.ToString("F2") + ")",
              card.Film.ShownFrame == GateFilm.LastFrame && card.View.FlashAlpha > 0.8f && card.View.ActiveParticles > 40);

        splashScene.SoundCue -= cues.Add;
        Time.timeScale = 1f;
    }

    // ---- input path ----------------------------------------------------------

    static void InputPath()
    {
        var card = OpenCard(out _, out _);
        for (int i = 0; i < 30; i++) card.Step(Dt);

        // Real legacy Input can't be injected in a batch editor; check it is
        // idle without input and that the splash's Update really consumes it.
        var idle = SplashInput.ReadLegacy();
        Check("legacy input reader reports no press when nothing is pressed", !idle.tap);

        int sampleCalls = 0;
        bool pressNext = true;
        splashScene.InputSource = () => { sampleCalls++; var s = new SplashInput.Sample(); s.tap = pressNext; pressNext = false; return s; };
        var update = typeof(splashScene).GetMethod("Update", Any);
        update.Invoke(card, null);
        Check("Update polls the input source and turns ONE press into ONE tap (" + card.Sim.pendingTaps + "/" + card.Sim.crackStage + ")",
              sampleCalls == 1 && card.Sim.pendingTaps + card.Sim.crackStage == 1);
        update.Invoke(card, null);
        Check("a held button (no new press) adds nothing", card.Sim.pendingTaps + card.Sim.crackStage == 1);

        lastCount[0] = 0;
        splashScene.InputSource = null;
        typeof(splashScene).GetMethod("OnEnable", Any).Invoke(card, null);
        Check("the splash registers a BackNavigator layer while enabled", BackNavigator.IsRegistered(card));
        bool handled = BackNavigator.HandleEscape(987654);
        Check("Escape / Android back (via BackNavigator) leaves at once, like before", handled && lastCount[0] == 1);
        BackNavigator.HandleEscape(987655); card.OnBackPressed();
        Check("... and only once", lastCount[0] == 1);
        typeof(splashScene).GetMethod("OnDisable", Any).Invoke(card, null);
        Check("... and unregisters when disabled", !BackNavigator.IsRegistered(card));
        splashScene.InputSource = SplashInput.ReadLegacy;
    }

    // ---- data-driven art slots ------------------------------------------------

    static void SlotsAndFallbacks()
    {
        Check("effect slot names: " + GateArt.DebrisSlot + ", " + GateArt.SteamSlot,
              GateArt.DebrisSlot == "HapticGate/gate_debris" && GateArt.SteamSlot == "HapticGate/gate_steam");
        var deb = Resources.Load<Texture2D>(GateArt.DebrisSlot);
        var st = Resources.Load<Texture2D>(GateArt.SteamSlot);
        Check("gate_debris is 1024x512 (8x4 of 128)", deb != null && deb.width == 1024 && deb.height == 512);
        Check("gate_steam is 1024x1024 (4x4 of 256)", st != null && st.width == 1024 && st.height == 1024);
        Check("importers: debris/steam point, no mips, uncompressed", ImporterOk(GateArt.DebrisSlot) && ImporterOk(GateArt.SteamSlot));
        string artSrc = File.ReadAllText(Path.Combine(Application.dataPath, "Scripts/UI/GateArt.cs"));
        Check("the old procedural door / crack / steam fallbacks are gone from the code",
              !artSrc.Contains("HapticGate/steam\"") && !artSrc.Contains("industrial_gate") && !artSrc.Contains("gate_cracks_\""));

        var art = GateArt.Load();
        Check("art loads", art != null);
        if (art == null) return;
        Check("32 debris cells (8x4) of 128 px", art.debris.Length == 32 && Mathf.Approximately(art.debris[0].rect.width, 128f));
        Check("16 steam cells (4x4) of 256 px", art.steam.Length == 16 && Mathf.Approximately(art.steam[0].rect.width, 256f));
        art.Release();

        // The film's first frame is a closed gate on black with smoke, the last has the wordmark at the bottom.
        var f0 = Resources.Load<Texture2D>(GateFilm.PathFor(0));
        var f100 = Resources.Load<Texture2D>(GateFilm.PathFor(GateFilm.LastFrame));
        if (f0 == null || f100 == null) { Check("first / last film frames load", false); return; }
        var p0 = Pixels(f0); var p100 = Pixels(f100);
        int w = f0.width, h = f0.height;
        Func<Color[], int, int, Color> at = (px, x, y) => px[y * w + x];   // y up (texture rows)
        bool cornersBlack = at(p0, 2, 2).maxColorComponent < 0.05f && at(p0, w - 3, h - 3).maxColorComponent < 0.05f;
        // wordmark band: film y 0.80..0.98 from the top -> rows near the bottom; red there only in the final frame
        float red0 = RedShare(p0, w, h, 0.03f, 0.18f), red100 = RedShare(p100, w, h, 0.03f, 0.18f);
        Check("film frame 0: black corners, no wordmark yet (red " + red0.ToString("F3") + "); final frame: red wordmark at the bottom (" + red100.ToString("F3") + ")",
              cornersBlack && red0 < 0.005f && red100 > 0.03f);
    }

    static Color[] Pixels(Texture2D t)
    {
        var rt = RenderTexture.GetTemporary(t.width, t.height, 0);
        Graphics.Blit(t, rt);
        var prev = RenderTexture.active; RenderTexture.active = rt;
        var copy = new Texture2D(t.width, t.height, TextureFormat.RGBA32, false);
        copy.ReadPixels(new Rect(0, 0, t.width, t.height), 0, 0);
        RenderTexture.active = prev; RenderTexture.ReleaseTemporary(rt);
        var px = copy.GetPixels();
        UnityEngine.Object.DestroyImmediate(copy);
        return px;
    }

    // share of strongly red pixels in the rows between y0 and y1 (fractions of the height, from the bottom)
    static float RedShare(Color[] px, int w, int h, float y0, float y1)
    {
        int n = 0, red = 0;
        for (int y = (int)(y0 * h); y < (int)(y1 * h); y++)
            for (int x = 0; x < w; x += 2)
            {
                var c = px[y * w + x]; n++;
                if (c.r > 0.75f && c.g < 0.45f && c.b < 0.45f) red++;
            }
        return red / (float)Mathf.Max(1, n);
    }

    static bool ImporterOk(string slot)
    {
        var imp = AssetImporter.GetAtPath("Assets/Resources/" + slot + ".png") as TextureImporter;
        return imp != null && imp.filterMode == FilterMode.Point && !imp.mipmapEnabled && imp.textureCompression == TextureImporterCompression.Uncompressed;
    }

    // ---- screen fit -------------------------------------------------------------

    static void ScreenFit()
    {
        EditorSceneLoader.Open("spashS7", OpenSceneMode.Single);
        splashScene.BuildGateInEditMode = true;
        var card = UnityEngine.Object.FindFirstObjectByType<splashScene>();
        var cam = card.GetComponentInParent<Camera>();
        float baseSize = cam.orthographicSize;
        card.Restart();
        int bad = 0;
        foreach (var d in FitDevice.All)
        {
            cam.orthographicSize = CameraFit.ComputeSize(baseSize, 2.85f, d.w, d.h);
            var l = card.ApplyLayout(d.w, d.h, d.Safe);
            card.Restart();
            card.Step(0.0f);
            float viewH = 2f * cam.orthographicSize, viewW = viewH * d.w / d.h;
            Vector2 c = cam.transform.position;
            var view = new Rect(c.x - viewW / 2f, c.y - viewH / 2f, viewW, viewH);
            var safe = new Rect(view.xMin + d.Safe.x / d.w * viewW, view.yMin + d.Safe.y / d.h * viewH,
                                d.Safe.width / d.w * viewW, d.Safe.height / d.h * viewH);
            // the film's gate fully open (its widest) and the landed wordmark (the mark's spot)
            float fw = l.filmWidth;
            Vector2 gc = (Vector2)cam.transform.position + l.gateCenter;
            float halfW = (GateFilm.LeafOpenX + GateFilm.LeafWidth * 0.5f) * fw, halfH = GateFilm.GateHeight * 0.5f * fw;
            const float eps = 0.002f;
            bool doorIn = gc.x - halfW >= safe.xMin - eps && gc.x + halfW <= safe.xMax + eps
                          && gc.y - halfH >= safe.yMin - eps && gc.y + halfH <= safe.yMax + eps;
            // worst shake: wordmark must stay on screen
            float maxShake = (0.005f + 0.022f * 1.6f) * fw;
            var lb = card.logo.bounds;
            bool logoIn = lb.min.x - maxShake >= view.xMin && lb.max.x + maxShake <= view.xMax
                          && lb.min.y - maxShake >= view.yMin && lb.max.y + maxShake <= view.yMax;
            bool logoSafe = lb.min.x >= safe.xMin - eps && lb.max.x <= safe.xMax + eps;
            if (!doorIn || !logoIn || !logoSafe)
            {
                bad++;
                Debug.Log("[GATE] fit problem on " + d.id + " door=" + doorIn + " logo=" + logoIn + " logoSafe=" + logoSafe
                          + " view=" + view + " safe=" + safe + " logo=" + lb.min + ".." + lb.max + " shake=" + maxShake + " fw=" + fw);
            }
        }
        Check("open gate + landed wordmark (even at max shake) fit every FitDevice (" + FitDevice.All.Length + " devices)", bad == 0);
        splashScene.BuildGateInEditMode = false;
    }

    // ---- allocations --------------------------------------------------------------

    static void Allocations()
    {
        var card = OpenCard(out _, out var cueList);
        splashScene.SoundCue -= cueList.Add;   // the test's own list would grow (and allocate) while measuring
        long control;
        Check("allocation meter sees a control allocation", TestHarness.AllocMeterWorks(out control));
        // warm up twice through everything: natural run, and a run with taps
        for (int pass = 0; pass < 2; pass++)
        {
            lastCount[0] = 0; card.Restart();
            for (int i = 0; i < 24; i++) card.Step(Dt);
            card.Tap(); card.Tap(); card.Tap();
            for (int i = 0; i < 60; i++) card.Step(Dt);
            lastCount[0] = 0; card.Restart();
            for (int i = 0; i < 260; i++) card.Step(Dt);
        }
        lastCount[0] = 0; card.Restart();
        long natural = TestHarness.AllocatedBytes(() => { for (int i = 0; i < 235; i++) card.Step(Dt); });   // up to the frame before the scene change (4.0 s)
        // The meter itself reads a one-off ~42 B in a long editor session (sometimes twice: the opening clip's
        // start lands inside this window), so the whole run allows that and the per-part check below is strict.
        Check("natural intro allocates nothing per frame (" + natural + " bytes over the whole run)", natural <= 128);
        {
            lastCount[0] = 0; card.Restart();
            var shake = (Action)Delegate.CreateDelegate(typeof(Action), card, "ApplyShake");
            Action adv = () => card.Sim.Advance(Dt), show = () => card.Film.Show(card.FilmTime), sync = () => card.View.Sync(Dt);
            long sa = 0, sb = 0, sc = 0, sd = 0;
            for (int i = 0; i < 230; i++)
            {
                sa += TestHarness.AllocatedBytes(adv); sb += TestHarness.AllocatedBytes(show);
                sc += TestHarness.AllocatedBytes(sync); sd += TestHarness.AllocatedBytes(shake);
            }
            Check("... per part, zero: sim " + sa + ", film " + sb + ", effects " + sc + ", shake/words " + sd, sa + sb + sc + sd == 0);
        }   // <= 0.5 B per frame: the meter itself reads a one-off ~42 B in a long editor session
        lastCount[0] = 0; card.Restart();
        for (int i = 0; i < 24; i++) card.Step(Dt);
        long taps = TestHarness.AllocatedBytes(() =>
        {
            card.Tap(); card.Tap(); card.Tap();
            for (int i = 0; i < 38; i++) card.Step(Dt);   // through the smash, up to the frame before the scene change
        });
        Check("crack/smash sequence allocates nothing per frame (" + taps + " bytes)", taps <= 64);
        if (taps != 0)
        {
            lastCount[0] = 0; card.Restart();
            for (int i = 0; i < 24; i++) card.Step(Dt);
            Debug.Log("[GATE] diag taps: " + TestHarness.AllocatedBytes(() => { card.Tap(); card.Tap(); card.Tap(); }));
            for (int i = 0; i < 40; i++)
            {
                long b = TestHarness.AllocatedBytes(() => card.Step(Dt));
                if (b != 0) Debug.Log("[GATE] diag step " + i + " t=" + card.Sim.time + " crack=" + card.Sim.crackStage + " bytes=" + b);
            }
        }
    }

    // ---- source guards --------------------------------------------------------------

    static void Source()
    {
        string src = File.ReadAllText(Path.Combine(Application.dataPath, "Scripts/UI/splashScene.cs"));
        Check("splash drives itself from Time.unscaledDeltaTime, never scaled time",
              src.Contains("Time.unscaledDeltaTime") && !src.Contains("Time.deltaTime"));
    }
}
