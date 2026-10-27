using System;
using System.IO;
using UnityEngine;
using UnityEngine.UI;
using Debug = UnityEngine.Debug;

// First-use hitches around the first blue-atom shield and the first fusion: what that moment used to do for
// the first time (the jet spool-up's clip load + decode, the popup pool and the font glyphs of the pickup words
// and fusion callouts, the discovery store's PlayerPrefs read and the clock's time-zone setup) is done as the run
// begins, spread over frames, idempotently and without per-frame allocation.
//   scripts/unity-batch.sh -executeMethod AllTests.RunSuites -suites FirstUseHitchTest
public static class FirstUseHitchTest
{
    static int fails;
    static void Check(string what, bool ok)
    {
        Debug.Log((ok ? "[FUH] PASS  " : "[FUH] FAIL  ") + what);
        if (!ok) fails++;
    }

    public static void Run() { TestHarness.Exit(Execute()); }

    static string Src(string rel) { return File.ReadAllText(Path.Combine(Application.dataPath, "Scripts/" + rel)); }

    // The text of method `name`'s body (from its signature to the next top-level "\n    }").
    static string Body(string src, string signature)
    {
        int i = src.IndexOf(signature, StringComparison.Ordinal);
        if (i < 0) return "";
        int end = src.IndexOf("\n    }", i, StringComparison.Ordinal);
        return end < 0 ? src.Substring(i) : src.Substring(i, end - i);
    }

    public static int Execute()
    {
        fails = 0;
        using var sandbox = new TestHarness.Sandbox();
        BoostJetIsResidentBeforeThePickup();
        DiscoveryStoreWarmsOnce();
        HudWarmsPoolAndGlyphs();
        WarmUpIsWiredIntoRunStart();
        Debug.Log("[FUH] failures: " + fails);
        return fails;
    }

    static void BoostJetIsResidentBeforeThePickup()
    {
        int plays = BoostJet.Plays;
        BoostJet.Prewarm();
        var s1 = BoostJet.Source;
        Check("BoostJet.Prewarm creates the pooled source and plays nothing", s1 != null && BoostJet.Plays == plays);
        var clip = Resources.Load<AudioClip>(BoostJet.ClipPath);
        Check("... and the clip is no longer a bare header (loaded or loading)",
              clip != null && clip.loadState != AudioDataLoadState.Unloaded);
        BoostJet.Prewarm();
        Check("BoostJet.Prewarm is idempotent (same source, still no play)", BoostJet.Source == s1 && BoostJet.Plays == plays);
        string play = Body(Src("Audio/BoostJet.cs"), "public static void Play()");
        Check("Play() creates nothing itself beyond the shared EnsureSource fallback", !play.Contains("new GameObject"));
    }

    static void DiscoveryStoreWarmsOnce()
    {
        FusionDiscoveryStore.Reload();
        FusionDiscoveryStore.Prewarm();
        int total = FusionDiscoveryStore.DiscoveredCount;
        FusionDiscoveryStore.Prewarm();
        Check("FusionDiscoveryStore.Prewarm is idempotent and changes no record", total == FusionDiscoveryStore.DiscoveredCount);
        Check("Today() is a plausible yyyymmdd after the warm-up", FusionDiscoveryStore.Today() > 20200101);
    }

    static void HudWarmsPoolAndGlyphs()
    {
        var root = new GameObject("fuh_canvas", typeof(Canvas), typeof(CanvasScaler));
        try
        {
            var rows = new GameObject("rows", typeof(RectTransform)).GetComponent<RectTransform>();
            rows.SetParent(root.transform, false);
            var tgo = new GameObject("speed", typeof(RectTransform), typeof(Text));
            tgo.transform.SetParent(rows, false);
            var speed = tgo.GetComponent<Text>();
            speed.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            var hud = ScoreHud.AttachPopupsOnly(root, speed);
            Check("attaching the HUD builds the popup pool (no first-popup construction)", hud != null && hud.PoolBuilt);

            int before = ScoreHud.GlyphRequests;
            int frames = 0;
            while (!hud.PrewarmDone && frames < 200) { hud.PrewarmStep(); frames++; }
            int made = ScoreHud.GlyphRequests - before;
            bool dynamic = speed.font != null && speed.font.dynamic;
            Check("one glyph batch per size, a size a frame (" + made + " of " + ScoreHud.PrewarmSizes.Length + ")",
                  !dynamic || made == ScoreHud.PrewarmSizes.Length);
            // the world-start window is 8 s of calm: the warm-up must be a small fraction of it at 60 fps
            Check("warm-up completes in " + frames + " frames (well inside the 8 s calm start)", hud.PrewarmDone && frames <= 30);

            hud.PrewarmStep();
            Check("a finished warm-up requests nothing more (idempotent)", ScoreHud.GlyphRequests - before == made);

            long a0 = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 1000; i++) hud.PrewarmStep();
            Check("a finished warm-up allocates nothing per frame", GC.GetAllocatedBytesForCurrentThread() - a0 == 0);

            // the chars cover the fusion callouts, so their first display adds no glyph
            if (dynamic)
            {
                var d = AtomFusions.DiscoveryWord(FusionId.Nova).Replace("\n", "");
                int px = Mathf.RoundToInt(ScoreHud.PrewarmSizes[ScoreHud.PrewarmSizes.Length - 1] * root.GetComponent<Canvas>().scaleFactor);
                bool all = true;
                foreach (char c in d)
                {
                    CharacterInfo ci;
                    all &= speed.font.GetCharacterInfo(c, out ci, px, FontStyle.Bold);
                }
                Check("the discovery callout's characters are resident at the biggest size", all);
            }
        }
        finally { UnityEngine.Object.DestroyImmediate(root); }
    }

    static void WarmUpIsWiredIntoRunStart()
    {
        string start = Src("Ship/collisionDetection.cs");
        int s = start.IndexOf("void Start ()", StringComparison.Ordinal);
        int jet = start.IndexOf("BoostJet.Prewarm()", StringComparison.Ordinal);
        int trig = start.IndexOf("void OnTriggerEnter2D", StringComparison.Ordinal);
        Check("ship Start prewarms the jet clip (before any pickup code)", s >= 0 && jet > s && jet < trig);
        Check("a run begins by warming the discovery store", Body(Src("Gameplay/Pickups/Fusions/AtomFusions.cs"), "public static void BeginRun").Contains("FusionDiscoveryStore.Prewarm()"));
        string hud = Src("UI/ScoreHud.cs");
        Check("both HUD attach paths start the glyph warm-up",
              Body(hud, "public static ScoreHud AttachPopupsOnly").Contains("StartPrewarm()") && Body(hud, "public static ScoreHud Attach(").Contains("StartPrewarm()"));
        string step = Body(hud, "public void PrewarmStep()");
        Check("the glyph step allocates nothing per call (no new, cached char set)", !step.Contains(" new ") && step.Contains("PrewarmChars()"));
        Check("the shield pickup branch does no Resources.Load of its own", !start.Substring(trig, Math.Min(20000, start.Length - trig)).Contains("Resources.Load"));
        Check("no synchronous load in the fusion fire path", !Body(Src("Gameplay/Pickups/Fusions/AtomFusions.cs"), "static void Fire(").Contains("Resources.Load"));
    }
}
