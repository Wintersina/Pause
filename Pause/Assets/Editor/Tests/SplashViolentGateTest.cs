using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// The violent HapticGate intro and its tap-to-smash skip: simulation (fake
// clock), the scene wiring (one scene load, queued taps, input path), screen
// fit on every device, and per-frame allocations.
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
        // run a fresh sim to `from`, then average |shake| and leaf jitter until `to`
        var s = new GateSim();
        while (s.time < from) s.Advance(Dt);
        double sum = 0; int n = 0;
        while (s.time < to)
        {
            s.Advance(Dt);
            sum += s.leftJitter.sqrMagnitude + s.shake.sqrMagnitude * 4f; n++;
        }
        return Mathf.Sqrt((float)(sum / Mathf.Max(1, n)));
    }

    static void PureSim()
    {
        float a = Rms(null, 0.02f, 0.22f), b = Rms(null, 0.25f, 0.45f), c = Rms(null, 0.5f, 0.69f);
        Check("rattle amplitude rises through the build (" + a.ToString("F4") + " < " + b.ToString("F4") + " < " + c.ToString("F4") + ")",
              a < b && b < c);

        var sim = new GateSim();
        int impacts = 0, jumps = 0, flat = 0, dones = 0, maxStepFrames = 0;
        sim.Impact += (s, i) => { if (i >= 0) impacts++; };
        sim.Done += () => dones++;
        float prev = 0f, peak = 0f; bool jitterXY = false, jitterRot = false;
        int steady = 0;
        while (!sim.finished)
        {
            sim.Advance(Dt);
            float d = sim.open - prev;
            if (d > 0.03f) jumps++;
            if (sim.time > 0.75f && sim.time < 1.7f && Mathf.Abs(d) < 0.004f) flat++;
            prev = sim.open; peak = Mathf.Max(peak, sim.open);
            if (Mathf.Abs(sim.leftJitter.x) > 0f && Mathf.Abs(sim.leftJitter.y) > 0f) jitterXY = true;
            if (Mathf.Abs(sim.leftRot) > 0.001f) jitterRot = true;
            steady++;
            if (steady > 1000) break;
        }
        Check("intro ends by itself at about " + sim.naturalSeconds + " s (" + sim.time.ToString("F2") + ")", sim.finished && !sim.smashFinish && Mathf.Abs(sim.time - sim.naturalSeconds) < 0.05f);
        Check("doors end fully open (" + sim.open.ToString("F3") + ")", sim.open > 0.97f && peak < 1.12f);
        Check("doors are forced open in jerky steps (" + jumps + " jolts, " + flat + " held frames)", jumps >= 5 && flat >= 20);
        Check("each forced step fires an impact (" + impacts + "/" + GateSim.StepCount + ")", impacts == GateSim.StepCount);
        Check("leaves jitter on x, y and rotation", jitterXY && jitterRot);
        Check("Done fires exactly once on natural finish", dones == 1);
        Check("intro length is roughly as before, slightly longer (2.0-2.6 s)", sim.time >= 2.0f && sim.time <= 2.6f);

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
        Check("splash card built the door (view present)", card.View != null && card.Sim != null);

        // natural run
        Time.timeScale = 0f;
        for (int i = 0; i < 400 && lastCount[0] == 0; i++) card.Step(Dt);
        Check("timeScale 0: the card still advances and finishes (unscaled step)", lastCount[0] == 1 && card.Sim.time > 2f);
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

        // door visuals respond
        lastCount[0] = 0; card.Restart();
        for (int i = 0; i < 30; i++) card.Step(Dt);
        card.Tap(); for (int i = 0; i < 6; i++) card.Step(Dt);
        bool crack1 = CrackShown(card, 0);
        card.Tap(); for (int i = 0; i < 12; i++) card.Step(Dt);
        bool crack2 = CrackShown(card, 1);
        Check("tap 1 shows crack overlay 1, tap 2 shows overlay 2 (and bulges the leaves)", crack1 && crack2 && card.Sim.bulge > 0f);
        card.Tap(); while (!card.Sim.smashed) card.Step(Dt);
        for (int i = 0; i < 6; i++) card.Step(Dt);
        var leaf = card.View.leftLeaf.GetComponent<SpriteRenderer>();
        Check("smash hides the door leaves", !leaf.enabled);

        splashScene.SoundCue -= cues.Add;
        Time.timeScale = 1f;
    }

    static bool CrackShown(splashScene card, int idx)
    {
        var leaf = card.View.leftLeaf;
        var overlays = leaf.GetComponentsInChildren<SpriteRenderer>();
        int on = 0; bool right = false;
        for (int i = 0; i < overlays.Length; i++)
        {
            if (overlays[i].transform == leaf) continue;
            if (overlays[i].enabled) { on++; if (overlays[i].gameObject.name.EndsWith((idx + 1).ToString())) right = true; }
        }
        return on == 1 && right;
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
        Check("slot names: " + GateArt.DoorSlot + ", " + GateArt.CrackSlotPrefix + "1..3, " + GateArt.DebrisSlot + ", " + GateArt.SteamSlot,
              GateArt.DoorSlot == "HapticGate/industrial_gate_v2" && GateArt.CrackSlotPrefix == "HapticGate/gate_cracks_"
              && GateArt.DebrisSlot == "HapticGate/gate_debris" && GateArt.SteamSlot == "HapticGate/gate_steam");
        var door = Resources.Load<Texture2D>(GateArt.DoorSlot);
        Check("door v2 is 1536x1024", door != null && door.width == 1536 && door.height == 1024);
        for (int i = 1; i <= 3; i++)
        {
            var c = Resources.Load<Texture2D>(GateArt.CrackSlotPrefix + i);
            Check("gate_cracks_" + i + " is 1536x1024 (aligned to the door sheet)", c != null && c.width == 1536 && c.height == 1024);
        }
        var deb = Resources.Load<Texture2D>(GateArt.DebrisSlot);
        var st = Resources.Load<Texture2D>(GateArt.SteamSlot);
        Check("gate_debris is 1024x512 (8x4 of 128)", deb != null && deb.width == 1024 && deb.height == 512);
        Check("gate_steam is 1024x1024 (4x4 of 256)", st != null && st.width == 1024 && st.height == 1024);
        Check("importers: point filter, no mipmaps, uncompressed", ImporterOk(GateArt.DoorSlot) && ImporterOk(GateArt.DebrisSlot)
              && ImporterOk(GateArt.SteamSlot) && ImporterOk(GateArt.CrackSlotPrefix + "1") && ImporterOk(GateArt.CrackSlotPrefix + "2")
              && ImporterOk(GateArt.CrackSlotPrefix + "3"));
        Check("the old industrial_gate / steam fallbacks are gone from the code",
              !File.ReadAllText(Path.Combine(Application.dataPath, "Scripts/UI/GateArt.cs")).Contains("HapticGate/steam\""));

        var art = GateArt.Load();
        Check("art loads", art != null);
        if (art == null) return;
        Check("32 debris cells (8x4) of 128 px", art.debris.Length == 32 && Mathf.Approximately(art.debris[0].rect.width, 128f));
        Check("16 steam cells (4x4) of 256 px", art.steam.Length == 16 && Mathf.Approximately(art.steam[0].rect.width, 256f));
        bool cracksOk = true;
        for (int i = 0; i < 3; i++)
            cracksOk &= art.leftCracks[i] != null && art.rightCracks[i] != null
                        && art.leftCracks[i].rect == art.leftLeaf.rect && art.rightCracks[i].rect == art.rightLeaf.rect;
        Check("crack overlays are cut with exactly the leaf rects (aligned), 3 stages, both leaves", cracksOk);
        Check("leaf crop contract: left x 0.055, right x 0.51, w 0.44",
              Mathf.Abs(art.leftLeaf.rect.x - 1536 * 0.055f) < 0.5f && Mathf.Abs(art.rightLeaf.rect.x - 1536 * 0.51f) < 0.5f
              && Mathf.Abs(art.leftLeaf.rect.width - 1536 * 0.44f) < 0.5f);
        // every v2 leaf must be inside its crop (nothing of the door cut off): the dark margin at the crop edges
        Check("door art inside the crops (edge columns are background)", EdgesAreBackground(door));
        art.Release();
    }

    static bool ImporterOk(string slot)
    {
        var imp = AssetImporter.GetAtPath("Assets/Resources/" + slot + ".png") as TextureImporter;
        return imp != null && imp.filterMode == FilterMode.Point && !imp.mipmapEnabled && imp.textureCompression == TextureImporterCompression.Uncompressed;
    }

    // the leaf crops' outer columns must be the dark backdrop, i.e. the door isn't cut by the crop
    static bool EdgesAreBackground(Texture2D door)
    {
        var rt = RenderTexture.GetTemporary(door.width, door.height, 0);
        Graphics.Blit(door, rt);
        var prev = RenderTexture.active; RenderTexture.active = rt;
        var copy = new Texture2D(door.width, door.height, TextureFormat.RGBA32, false);
        copy.ReadPixels(new Rect(0, 0, door.width, door.height), 0, 0);
        RenderTexture.active = prev; RenderTexture.ReleaseTemporary(rt);
        float[] xs = { 1536 * 0.055f + 2, 1536 * 0.495f - 3, 1536 * 0.51f + 2, 1536 * 0.95f - 3 };
        bool ok = true;
        foreach (float x in xs)
            for (int y = 60; y < 960; y += 30)
            {
                var c = copy.GetPixel((int)x, y);
                if (c.r + c.g + c.b > 0.7f) ok = false;
            }
        UnityEngine.Object.DestroyImmediate(copy);
        return ok;
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
            var lr = card.View.leftLeaf.GetComponent<SpriteRenderer>().bounds;
            var rr = card.View.rightLeaf.GetComponent<SpriteRenderer>().bounds;
            var both = lr; both.Encapsulate(rr.min); both.Encapsulate(rr.max);
            const float eps = 0.002f;
            bool doorIn = both.min.x >= safe.xMin - eps && both.max.x <= safe.xMax + eps
                          && both.min.y >= safe.yMin - eps && both.max.y <= safe.yMax + eps;
            // worst shake: logo must stay on screen
            float maxShake = (0.012f + 0.045f * 1.6f) * l.logoSize.x;
            var lb = card.logo.bounds;
            bool logoIn = lb.min.x - maxShake >= view.xMin && lb.max.x + maxShake <= view.xMax
                          && lb.min.y - maxShake >= view.yMin && lb.max.y + maxShake <= view.yMax;
            bool logoSafe = lb.min.x >= safe.xMin - eps && lb.max.x <= safe.xMax + eps;
            if (!doorIn || !logoIn || !logoSafe)
            {
                bad++;
                Debug.Log("[GATE] fit problem on " + d.id + " door=" + doorIn + " logo=" + logoIn + " logoSafe=" + logoSafe);
            }
        }
        Check("door shut state + logo (even at max shake) fit every FitDevice (" + FitDevice.All.Length + " devices)", bad == 0);
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
            for (int i = 0; i < 160; i++) card.Step(Dt);
        }
        lastCount[0] = 0; card.Restart();
        long natural = TestHarness.AllocatedBytes(() => { for (int i = 0; i < 135; i++) card.Step(Dt); });   // up to the frame before the scene change
        Check("natural intro allocates nothing per frame (" + natural + " bytes over the whole run)", natural <= 64);   // <= 0.5 B per frame: the meter itself reads a one-off ~42 B in a long editor session
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
