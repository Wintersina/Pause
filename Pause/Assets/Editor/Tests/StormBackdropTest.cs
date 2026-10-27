using System.Collections.Generic;
using System.IO;
using UnityEditor.SceneManagement;
using UnityEngine;

// The Storm backdrop (StormDirector, StormLightning): a thunderstorm, never Tide's ocean.
//   * its own spec (own folder, four variant sets, v3 the night side), layers rising far -> near, the ground pieces
//     PINNED to the mid tile, every layer behind gameplay;
//   * LIGHTNING IS GENTLE (photosensitivity): over many seeded 10-minute runs no two light pulses start closer
//     than MinPulseGap, never more than 2 in any second (a flash and its faint re-strike), none in the opening
//     seconds, a long-run mean well under one a second; every glow stays under its alpha cap, a glow's alpha
//     changes by only a few percent per 60 fps frame, and it is soft (no frame jump), a bloom over the clouds is
//     fainter still; bolts are thin art that live under half a second and never re-flicker;
//   * STRIKES land on a lightning-rod mast: the bolt's lower end sits on the rod tip while it lives;
//   * the ART is a storm: the tiles carry (almost) no green / teal / mint, and far less than Tide's sea; the lightning
//     atlas's glow cells fade to nothing at their border; the rod tip / strike end are where the code expects;
//   * the cloud ceiling thick at the start (continuing the planetfall's deck) and gone by ~14 s, a light scattering
//     after; rain sheets fall; gusts; debris tumbles; launch sites work for the world's elites; no per-frame allocation.
// (The tiles' seams / brightness caps / texture budget are WorldBackdropTest's.)
//
//   Unity -batchmode -quit -projectPath <abs>/Pause -executeMethod StormBackdropTest.Run
public static class StormBackdropTest
{
    static int fails;
    static void Check(string what, bool ok)
    {
        Debug.Log((ok ? "[SBD] PASS  " : "[SBD] FAIL  ") + what);
        if (!ok) fails++;
    }

    public static void Run() { TestHarness.Exit(Execute()); }

    const float Dt = 1f / 30f;
    static BackdropVariants Sel => BackdropVariants.For("Storm");
    const string Art = "Assets/Art/Backgrounds/Resources/Worlds/Storm/Backdrop3/";

    public static int Execute()
    {
        fails = 0;
        using var sandbox = new TestHarness.Sandbox();
        float savedSpeed = moveBackGround.speed;
        try
        {
            Catalog();
            LightningSchedule();
            Lightning();
            Strikes();
            Pinned();
            EyeGone();
            Palette();
            Resolution();
            AtlasContract();
            Clouds();
            Weather();
            Sites();
            Allocation();
        }
        finally
        {
            moveBackGround.speed = savedSpeed;
            Time.timeScale = 1f;
            BackdropSet.AtlasOverride = null;
            Sel.Reset();
            StormDirector.SeedOverride = 0;
            LandingSites.Override = null;
            EliteSystem.PlayerOverride = null;
            EliteSystem.Clear();
            if (WorldBackdrop.Instance != null) Object.DestroyImmediate(WorldBackdrop.Instance.gameObject);
        }
        Debug.Log("[SBD] failures: " + fails);
        return fails;
    }

    static WorldBackdrop Fresh(int variant = 0, float speed = .25f)
    {
        EliteSystem.Clear();
        EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
        Time.timeScale = 1f;
        moveBackGround.speed = speed;
        Sel.Reset();
        Sel.Seed(11);
        Sel.Force = variant;
        var cam = Camera.main;
        cam.orthographic = true;
        cam.aspect = 1080f / 2400f;
        cam.orthographicSize = CameraFit.ComputeSize(5f, CameraFit.GameplayHalfWidth, 1080, 2400);
        var wb = WorldBackdrop.Create("Storm");
        wb.Show("Storm", false);
        return wb;
    }

    static StormDirector Director(WorldBackdrop wb) { return wb.Current != null ? wb.Current.Director as StormDirector : null; }

    static void Run(WorldBackdrop wb, float seconds, System.Action each = null, float dt = Dt)
    {
        for (float t = 0f; t < seconds; t += dt) { wb.Step(dt); if (each != null) each(); }
    }

    // ---- the layer stack -------------------------------------------------------------

    static void Catalog()
    {
        var spec = BackdropCatalog.For("Storm");
        Check("Storm has its own spec, folder and four variant sets (not Tide's stand-in)",
              spec.world == "Storm" && spec.folder == "Worlds/Storm/Backdrop3/" && spec.variantSets == StormTuning.Variants);
        string[] want = { "sky", "far", "mid", "ground", "flow", "glow", "bolts", "mist", "wisps", "ceiling", "bloom", "gusts", "debris", "rain" };
        bool all = true;
        foreach (string n in want) all &= spec.Has(n);
        Check("Storm layers: ground tiles, pinned ground pieces, flow, lightning glow + bolts, mist, wisps, ceiling, bloom, gusts, debris, rain", all);
        var g = spec.Find("ground");
        Check("the ground pieces are PINNED to the mid tile: same rate, landmark role, right after it",
              g.pinTo == "mid" && g.rate == spec.Rate("mid") && g.rate <= BackdropCatalog.MaxGroundRate &&
              g.role == BackdropCatalog.Role.Landmark && System.Array.IndexOf(spec.layers, g) == System.Array.IndexOf(spec.layers, spec.Find("mid")) + 1);
        bool rising = true;
        for (int i = 1; i < spec.layers.Length; i++)
            rising &= spec.layers[i].rate > spec.layers[i - 1].rate ||
                      (spec.layers[i].pinTo == spec.layers[i - 1].name && spec.layers[i].rate == spec.layers[i - 1].rate);
        Check("Storm rates strictly increase far -> near", rising);
        Check("Storm ships four ground sets rotating, v3 the night side (darker), v1 / v2 / v4 day",
              spec.variantSets == 4 && !spec.Night(1) && !spec.Night(2) && spec.Night(3) && !spec.Night(4) &&
              spec.VariantBrightness(3) < 1f && spec.VariantBrightness(1) >= spec.VariantBrightness(3) &&
              System.IO.File.Exists(Art + "v3/mid.png") && System.IO.File.Exists(Art + "v4/mid.png") &&
              StormTuning.ClusterWeights.Length == 5 && StormTuning.VariantBrightness.Length == 5 && StormTuning.Night.Length == 5);
        Check("the art is dark: Storm is drawn below the bright-world lift, so shots keep the thin outline", !spec.Bright);
        var set = new BackdropSet("Storm", new GameObject("~root").transform, 3f, 5f);
        Check("a Storm backdrop is built by the StormDirector (not TideDirector)", set.Director is StormDirector && !(set.Director is TideDirector));
        Check("every layer sits behind gameplay (order < 0)", spec.Order("rain") < 0 && spec.Order("sky") < spec.Order("rain"));
        set.Destroy();
        Object.DestroyImmediate(GameObject.Find("~root"));
    }

    // ---- the lightning schedule (pure logic) -----------------------------------------------

    static void LightningSchedule()
    {
        float worstGap = 999f, mean = 0f, firstAt = 999f, firstStrike = 999f;
        int worstInSecond = 0, runs = 20, kinds = 0;
        var seen = new HashSet<StormLightning.Kind>();
        for (int seed = 1; seed <= runs; seed++)
        {
            var s = new StormLightning(seed * 7919);
            var times = new List<float>();
            for (float t = 0f; t < 600f; t += 1f / 60f)
            {
                StormLightning.Kind k; float peak; bool fresh;
                if (!s.Tick(1f / 60f, true, out k, out peak, out fresh)) continue;
                times.Add(s.Clock);
                seen.Add(k);
                if (peak <= 0f || peak > 1f) fails++;
                if (fresh && k == StormLightning.Kind.Strike) firstStrike = Mathf.Min(firstStrike, s.Clock);
            }
            if (times.Count > 0) firstAt = Mathf.Min(firstAt, times[0]);
            for (int i = 1; i < times.Count; i++) worstGap = Mathf.Min(worstGap, times[i] - times[i - 1]);
            for (int i = 0; i < times.Count; i++)
            {
                int inSec = 0;
                for (int j = i; j < times.Count && times[j] < times[i] + 1f; j++) inSec++;
                worstInSecond = Mathf.Max(worstInSecond, inSec);
            }
            mean += times.Count / 600f / runs;
        }
        kinds = seen.Count;
        Check("lightning pulses never start closer than " + StormTuning.MinPulseGap + " s (closest over " + runs + " runs: " + worstGap.ToString("F2") + " s)",
              worstGap >= StormTuning.MinPulseGap - 1e-3f);
        Check("at most 2 pulses in any one second, far below the 3 per second photosensitivity line (worst " + worstInSecond + ")", worstInSecond <= 2);
        Check("the long-run mean is well under one pulse a second (" + mean.ToString("F2") + " / s)", mean > .1f && mean < .6f);
        Check("nothing flashes in the opening " + StormTuning.FirstEvent + " s (first pulse " + firstAt.ToString("F1") + " s)", firstAt >= StormTuning.FirstEvent - .05f);
        Check("no strike before " + StormTuning.StrikeFirst + " s (first " + firstStrike.ToString("F1") + " s)", firstStrike >= StormTuning.StrikeFirst - .05f);
        Check("all three lightning kinds happen (" + kinds + ")", kinds == 3);
        var none = new StormLightning(3);
        bool noStrikeWithoutMast = true;
        for (float t = 0f; t < 900f; t += 1f / 60f)
        {
            StormLightning.Kind k; float peak; bool fresh;
            if (none.Tick(1f / 60f, false, out k, out peak, out fresh) && k == StormLightning.Kind.Strike) noStrikeWithoutMast = false;
        }
        Check("without a lightning-rod mast in view there is never a strike", noStrikeWithoutMast);
        // envelopes: soft and bounded
        float maxStep = 0f, prev = 0f;
        for (float a = 0f; a <= StormLightning.GlowLife + .1f; a += 1f / 60f)
        {
            float e = StormLightning.Envelope(a);
            maxStep = Mathf.Max(maxStep, Mathf.Abs(e - prev));
            prev = e;
            if (e < 0f || e > 1f) fails++;
        }
        Check("a glow envelope changes by " + maxStep.ToString("F3") + " per 60 fps frame (<= .26: no hard step)", maxStep <= .26f);
        Check("the envelope is gone by its life", StormLightning.Envelope(StormLightning.GlowLife) == 0f);
        float bprev = 0f, bmax = 0f, peaks = 0f; bool rose = false, fell = false, reflicker = false;
        for (float a = 0f; a <= StormLightning.BoltLife + .1f; a += 1f / 240f)
        {
            float e = StormLightning.BoltEnvelope(a);
            if (e > bprev + 1e-4f) { if (fell) reflicker = true; rose = true; }
            if (e < bprev - 1e-4f) fell = true;
            bprev = e; bmax = Mathf.Max(bmax, e); peaks = e;
        }
        Check("a bolt rises once, falls once and never re-flickers; it lives under half a second (" + StormLightning.BoltLife.ToString("F2") + " s)",
              rose && fell && !reflicker && bmax <= 1f && peaks == 0f && StormLightning.BoltLife < .5f);
        Check("the light caps are low (glow " + StormTuning.GlowAlpha + ", ceiling bloom " + StormTuning.BloomAlpha + ")",
              StormTuning.GlowAlpha <= .25f && StormTuning.BloomAlpha <= StormTuning.GlowAlpha);
    }

    // ---- the lightning as drawn ---------------------------------------------------------------------

    static void Lightning()
    {
        int totalFlashes = 0;
        float worstAlpha = 0f, worstBloom = 0f, worstFrameStep = 0f, worstGlowSum = 0f, worstBloomSum = 0f;
        int worstLive = 0, boltsSeen = 0;
        for (int v = 1; v <= StormTuning.Variants; v++)
        {
            var wb = Fresh(v);
            var d = Director(wb);
            if (d == null) { Check("Storm director v" + v, false); return; }
            var lastAlpha = new Dictionary<BackdropPiece, float>();
            Run(wb, 240f, () =>
            {
                int live = 0;
                foreach (var g in d.Glow.items)
                {
                    if (!g.active) { lastAlpha.Remove(g); continue; }
                    live++;
                    float a = g.sr.color.a;
                    worstAlpha = Mathf.Max(worstAlpha, a);
                    float prev;
                    if (lastAlpha.TryGetValue(g, out prev)) worstFrameStep = Mathf.Max(worstFrameStep, Mathf.Abs(a - prev));
                    lastAlpha[g] = a;
                }
                foreach (var g in d.Bloom.items) if (g.active) { live++; worstBloom = Mathf.Max(worstBloom, g.sr.color.a); }
                worstLive = Mathf.Max(worstLive, live);
                boltsSeen = Mathf.Max(boltsSeen, d.LiveBolts);
                // the COMBINED light of every live glow / bloom (what overlapping pulses add up to)
                float gs = 0f, bs = 0f;
                foreach (var g in d.Glow.items) if (g.active) gs += g.sr.color.a;
                foreach (var g in d.Bloom.items) if (g.active) bs += g.sr.color.a;
                worstGlowSum = Mathf.Max(worstGlowSum, gs);
                worstBloomSum = Mathf.Max(worstBloomSum, bs);
            }, 1f / 60f);
            totalFlashes += d.Flashes;
            Check("v" + v + ": lightning happens in 4 minutes (" + d.Flashes + " pulses: " + d.Forks + " forks, " + d.Strikes + " strikes)", d.Flashes >= 15);
            Object.DestroyImmediate(wb.gameObject);
        }
        Check("a glow never exceeds its alpha cap (" + worstAlpha.ToString("F3") + " <= " + StormTuning.GlowAlpha + ")", worstAlpha <= StormTuning.GlowAlpha + 1e-3f);
        Check("the bloom over the clouds is fainter still (" + worstBloom.ToString("F3") + " <= " + StormTuning.BloomAlpha + ")", worstBloom <= StormTuning.BloomAlpha + 1e-3f);
        Check("overlapping pulses never add up: the combined glow alpha stays <= " + StormTuning.GlowAlpha + " (" + worstGlowSum.ToString("F3") + "), the combined bloom <= " + StormTuning.BloomAlpha + " (" + worstBloomSum.ToString("F3") + ")",
              worstGlowSum <= StormTuning.GlowAlpha + 1e-3f && worstBloomSum <= StormTuning.BloomAlpha + 1e-3f);
        Check("a glow's alpha moves by <= .06 per real 60 fps frame (" + worstFrameStep.ToString("F3") + "): soft, no strobe", worstFrameStep <= .06f);
        Check("at most 4 glows + 2 blooms are ever alive at once (" + worstLive + ") and at most 4 bolts (" + boltsSeen + ")", worstLive <= 6 && boltsSeen <= 4);
        Check("over four variants lightning drew " + totalFlashes + " pulses", totalFlashes > 80);
    }

    // A strike's bolt ends exactly on the mast's rod tip, for as long as it lives.
    static void Strikes()
    {
        int checkedStrikes = 0, bad = 0;
        float worstOff = 0f;
        for (int seed = 1; seed <= 6 && checkedStrikes < 6; seed++)
        {
            StormDirector.SeedOverride = 300 + seed;
            var wb = Fresh(2);
            var d = Director(wb);
            if (d == null) { Check("Storm director for strikes", false); return; }
            float guard = 0f;
            Run(wb, 400f, () =>
            {
                if (d.Strikes == 0) return;
                guard += Dt;
                foreach (var b in d.Bolts.items)
                {
                    if (!b.active || b.parent == null || !b.parent.active) continue;
                    // the strike cell's lower end (bottom centre, y 226 of 256) vs the mast's rod tip (y 40 of 256)
                    float end = b.root.localPosition.y - (StormTuning.StrikeEndY - 128f) / 256f * b.size;
                    float tip = b.parent.root.localPosition.y + (128f - StormTuning.RodTipY) / 256f * b.parent.size;
                    float off = Mathf.Abs(end - tip);
                    worstOff = Mathf.Max(worstOff, off);
                    if (off > .02f) bad++;
                    checkedStrikes++;
                }
            });
            Object.DestroyImmediate(wb.gameObject);
        }
        StormDirector.SeedOverride = 0;
        Check("strikes happen on the thunderhead field (" + checkedStrikes + " frames sampled)", checkedStrikes > 0);
        Check("a strike bolt's lower end sits on its mast's rod tip (worst " + worstOff.ToString("F3") + " units)", bad == 0);
    }

    // ---- pinned ground -------------------------------------------------------------------------------

    static void Pinned()
    {
        var wb = Fresh(2);
        var d = Director(wb);
        if (d == null) return;
        Run(wb, 8f);
        bool placed = d.Ground.ActiveCount > 0;
        bool same = true;
        var seenGy = new Dictionary<BackdropPiece, Vector2>();   // (gy, age) last frame; a smaller age = a recycled slot
        Run(wb, 60f, () =>
        {
            foreach (var p in d.Ground.items)
            {
                if (!p.active) { seenGy.Remove(p); continue; }
                Vector2 was;
                if (!seenGy.TryGetValue(p, out was) || p.age < was.y) { seenGy[p] = new Vector2((float)p.gy, p.age); continue; }
                seenGy[p] = new Vector2((float)p.gy, p.age);
                if (System.Math.Abs(was.x - (float)p.gy) > 1e-3 || System.Math.Abs(p.y + d.Planner.Travel - p.gy) > 1e-3 || p.rate != wb.Current.Spec.Rate("mid")) same = false;
            }
        });
        Check("pieces are placed (" + d.Ground.ActiveCount + " live) and their ground spot never changes while in view (60 s of scroll)", placed && same);
        Object.DestroyImmediate(wb.gameObject);

        // no overlap, distinct pieces, towers where the storm is thick
        int seeds = 40, bad = 0, towers = 0, spires = 0, pieces = 0;
        for (int v = 1; v <= StormTuning.Variants; v++)
            for (int s = 1; s <= seeds / 4; s++)
            {
                StormDirector.SeedOverride = 5000 + s * 13 + v;
                var w2 = Fresh(v);
                var d2 = Director(w2);
                Run(w2, 90f);
                foreach (var p in d2.Ground.items)
                {
                    if (!p.active) continue;
                    pieces++;
                    if (p.kind == StormDirector.KindTower) towers++;
                    if (p.kind == StormDirector.KindSpire) spires++;
                    foreach (var q in d2.Ground.items)
                        if (q != p && q.active && Mathf.Abs(q.x - p.x) < p.size * .22f && Mathf.Abs((float)(q.gy - p.gy)) < p.size * .18f) bad++;
                }
                Object.DestroyImmediate(w2.gameObject);
            }
        StormDirector.SeedOverride = 0;
        Check("no two pieces sit on top of each other over " + seeds + " seeded runs (" + bad + ")", bad == 0);
        Check("the storm's pieces turn up: " + towers + " thunderhead towers and " + spires + " lightning-rod masts among " + pieces, towers > 5 && spires > 3);
    }

    // ---- the removed eye-of-the-storm deck stays gone ----------------------------------------------------------

    static void EyeGone()
    {
        Check("Storm ships exactly four ground sets (v1, v2, v4 day, v3 night; the old gray eye deck stays gone)", StormTuning.Variants == 4 && StormTuning.ClusterWeights.Length == 5);
        Check("no 'eye' atlas on disk (the new v3 is the night side, not a cyclone eye)", !File.Exists(Art + "eye.png") && !File.Exists(Art + "eye.json"));
        foreach (string v in new[] { "v1", "v2", "v3", "v4" })
            foreach (string r in new[] { "sky", "far", "mid", "flow" })
                Check("Storm " + v + "/" + r + ".png is installed", File.Exists(Art + v + "/" + r + ".png"));
    }

    // ---- the art is a storm, not an ocean -----------------------------------------------------------

    struct Mix { public float teal, violet, red, pink, mean; public int n; public float sat; }

    static Mix Measure(string path, float minAlpha = .5f)
    {
        var tex = new Texture2D(2, 2);
        tex.LoadImage(File.ReadAllBytes(path));
        var px = tex.GetPixels32();
        var m = new Mix();
        double sum = 0, ssum = 0;
        int teal = 0, violet = 0, red = 0, pink = 0, n = 0;
        for (int i = 0; i < px.Length; i += 3)
        {
            var c = px[i];
            if (c.a < minAlpha * 255f) continue;
            float h, s, v;
            Color.RGBToHSV(c, out h, out s, out v);
            h *= 360f;
            n++;
            sum += v; ssum += s;
            if (v > .12f && s > .18f && h >= 95f && h <= 160f) teal++;   // green / mint (the ocean's colour); teal 160+ is storm colour now
            if (v > .12f && s > .30f && h >= 292f && h <= 350f) pink++;
            if (v > .12f && s > .30f && h >= 250f && h < 292f) violet++;
            if (s > .5f && v > .12f && (h < 15f || h > 340f)) red++;
        }
        Object.DestroyImmediate(tex);
        m.n = n;
        m.teal = n > 0 ? teal / (float)n : 0f;
        m.violet = n > 0 ? violet / (float)n : 0f;
        m.red = n > 0 ? red / (float)n : 0f;
        m.pink = n > 0 ? pink / (float)n : 0f;
        m.mean = n > 0 ? (float)(sum / n) : 0f;
        m.sat = n > 0 ? (float)(ssum / n) : 0f;
        return m;
    }

    // 2026-10: Storm ground sets are realistic cool steel / teal-grey with violet-white lightning (no amber rims since the realistic restyle). No ocean green, no
    // pink (Space only), no red (the player's); violet stays a minority (the capacitor pickup is violet).
    // Tile red cap 0.6% (same as the atlas check): the v4 gust-front crest rims measure ~0.49%; the user approved the art on 2026-10-20.
    public const float MaxTileRedShare = .006f;
    public const float MaxTealShare = .01f, MaxVioletShare = .30f, MaxRedShare = .003f, MaxPinkShare = .003f, MinMidSaturation = .22f;

    // v1 / v2 are the 1024x2048 HD sets.
    static bool Hd(int v) { return true; }
    static bool Hd(string file) { return true; }

    static void Palette()
    {
        string[] roles = { "sky", "far", "mid" };
        float midSat = 1f;
        for (int v = 1; v <= StormTuning.Variants; v++)
            foreach (string r in roles)
            {
                string path = Art + "v" + v + "/" + r + ".png";
                if (!File.Exists(path)) { Check("Storm v" + v + "/" + r + ".png exists", false); continue; }
                var m = Measure(path);
                Check("Storm v" + v + " " + r + ": no ocean green / mint (" + (m.teal * 100f).ToString("F2") + "% <= " + MaxTealShare * 100f + "%), violet a minority (" +
                      (m.violet * 100f).ToString("F1") + "% <= " + MaxVioletShare * 100f + "%), no pink (" + (m.pink * 100f).ToString("F2") + "%), no red (" + (m.red * 100f).ToString("F2") + "%)",
                      m.teal <= MaxTealShare && m.violet <= MaxVioletShare && m.pink <= MaxPinkShare && m.red <= MaxTileRedShare);
                if (r == "mid" && Hd(v))
                {
                    midSat = Mathf.Min(midSat, m.sat);
                    Check("Storm v" + v + " mid is colourful, not gray (mean saturation " + m.sat.ToString("F2") + " >= " + MinMidSaturation + ")", m.sat >= MinMidSaturation);
                }
            }
        foreach (string atlas in new[] { "landmarks", "sites", "weather", "lightning" })
        {
            string path = Art + atlas + ".png";
            if (!File.Exists(path)) { Check("Storm " + atlas + ".png exists", false); continue; }
            var m = Measure(path, .2f);
            Check("Storm " + atlas + ": no ocean green (" + (m.teal * 100f).ToString("F2") + "%), no pink (" + (m.pink * 100f).ToString("F2") + "%), no red (" + (m.red * 100f).ToString("F2") + "%)",
                  m.teal <= MaxTealShare * 2f && m.pink <= MaxPinkShare * 2f && m.red <= MaxRedShare * 2f);
        }
    }

    // ---- real resolution (no fake upscales) ----------------------------------------------------
    // Desert's first tiles were a 256x512 painting doubled to 512x1024: every texel a 2x2 block, so the world looked soft and
    // zoomed. A nearest-neighbour upscale makes (nearly) every adjacent texel pair identical; real pixel art of the
    // sharp worlds (Tide/Ember/Verdant/Frost) sits at ~5-30% for tiles. Storm measures 5-11% on tiles, 1% on the
    // atlases and 22% on the lightning sheet. The flow sheets are 1-px steep rain streaks, so their VERTICAL pairs are
    // legitimately identical (~80%); only their horizontal pairs are held to the ceiling.
    const float MaxDupPairs = .35f;

    static void Resolution()
    {
        var files = new List<string>();
        for (int v = 1; v <= StormTuning.Variants; v++) foreach (string r in new[] { "sky", "far", "mid", "flow" }) files.Add("v" + v + "/" + r + ".png");
        foreach (string a in new[] { "landmarks", "sites", "weather", "lightning" }) files.Add(a + ".png");
        foreach (string f in files)
        {
            string path = Art + f;
            if (!File.Exists(path)) { Check("Storm " + f + " exists (resolution)", false); continue; }
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            tex.LoadImage(File.ReadAllBytes(path));
            var px = tex.GetPixels32();
            int w = tex.width, h = tex.height;
            Object.DestroyImmediate(tex);
            long hSame = 0, hN = 0, vSame = 0, vN = 0;
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    var c = px[y * w + x];
                    if (c.a == 0) continue;
                    if (x + 1 < w) { var d = px[y * w + x + 1]; if (d.a != 0) { hN++; if (c.r == d.r && c.g == d.g && c.b == d.b && c.a == d.a) hSame++; } }
                    if (y + 1 < h) { var d = px[(y + 1) * w + x]; if (d.a != 0) { vN++; if (c.r == d.r && c.g == d.g && c.b == d.b && c.a == d.a) vSame++; } }
                }
            float dh = hN > 0 ? (float)hSame / hN : 0f, dv = vN > 0 ? (float)vSame / vN : 0f;
            bool flow = f.EndsWith("flow.png");
            bool size = f.StartsWith("v") ? (Hd(f) ? (w == 1024 && h == 2048) : (w == 512 && h == 1024)) : (w == 1024 && h == 1024);
            Check("Storm " + f + " is native " + w + "x" + h + " with real detail: identical texel pairs " + (dh * 100f).ToString("F0") + "% across, " +
                  (dv * 100f).ToString("F0") + "% down (<= " + MaxDupPairs * 100f + "%" + (flow ? ", down exempt: rain streaks" : "") + ")",
                  size && dh <= MaxDupPairs && (flow || dv <= MaxDupPairs));
        }
    }

    // The cells the code relies on: names, the rod tip, the strike end, glows that fade out at their border, soft weather.
    static void AtlasContract()
    {
        var wb = Fresh(2);
        var set = wb.Current;
        var land = set.Atlas("landmarks"); var sites = set.Atlas("sites"); var weather = set.Atlas("weather"); var light = set.Atlas("lightning");
        bool names = true;
        foreach (string n in StormTuning.Towers) names &= land.Has(n);
        foreach (string n in StormTuning.Spires) names &= land.Has(n);
        foreach (string n in StormTuning.Skyhooks) names &= land.Has(n);
        foreach (string n in StormTuning.Refineries) names &= land.Has(n);
        foreach (string n in StormTuning.Neighbours) names &= land.Has(n);
        foreach (string n in StormTuning.Dishes) names &= land.Has(n);
        for (int k = 0; k < StormTuning.FirstLaunchSite; k++) names &= sites.Has(StormTuning.SiteClosed[k]) && sites.Has(StormTuning.SiteOpen[k]);   // (the strip sites are in the docks atlas: StormDockTest)
        names &= sites.Has("lights_off") && sites.Has("lights_on");
        Check("every piece the director draws is in the landmarks / sites atlases", names);
        Check("weather: 4 cloud banks, wisps, mist, gusts, rain; lightning: >= 6 bolts, 2 strikes, 2 flashes, an arc, debris",
              weather.Frames("cloud_bank").Length >= 4 && weather.Frames("cloud_wisp").Length >= 3 && weather.Frames("mist").Length >= 2 &&
              weather.Frames("gust").Length >= 2 && weather.Frames("rain").Length >= 4 &&
              light.Frames("bolt").Length >= 6 && light.Frames("strike").Length >= 2 && light.Frames("flash").Length >= 2 &&
              light.Frames("arc").Length >= 1 && light.Frames("debris").Length >= 4);
        Object.DestroyImmediate(wb.gameObject);

        // pixel contracts, read straight from the PNG (JSON y counts from the bottom)
        var lt = Load(Art + "lightning.png");
        var js = JsonUtility.FromJson<Cells>(File.ReadAllText(Art + "lightning.json"));
        bool glowSoft = true, strikeEnd = true, boltsBright = true;
        string detail = "";
        foreach (var c in js.sprites)
        {
            if (c.n.StartsWith("flash_"))
            {
                float edge = MaxAlpha(lt, c, 0, 0, c.w, 6) + MaxAlpha(lt, c, 0, c.h - 6, c.w, 6) + MaxAlpha(lt, c, 0, 0, 6, c.h) + MaxAlpha(lt, c, c.w - 6, 0, 6, c.h);
                float centre = MaxAlpha(lt, c, c.w / 2 - 10, c.h / 2 - 10, 20, 20);
                if (edge > .02f || centre < .5f) glowSoft = false;
                detail += " " + c.n + " edge " + edge.ToString("F2") + " centre " + centre.ToString("F2");
            }
            if (c.n.StartsWith("strike_"))
            {
                // the bolt's lower end: opaque pixels near (128, 226) from the top
                float a = MaxAlpha(lt, c, c.w / 2 - 12, 226 - 12, 24, 24);
                if (a < .6f) strikeEnd = false;
                detail += " " + c.n + " end " + a.ToString("F2");
            }
            if (c.n.StartsWith("bolt_") && Brightest(lt, c) < .85f) boltsBright = false;
        }
        Check("glow cells fade to nothing at their border and glow in the middle" + detail, glowSoft);
        Check("a strike bolt reaches the strike end point (128, " + StormTuning.StrikeEndY + ")", strikeEnd);
        Check("bolt cores are white-hot (brightest pixel value >= .85)", boltsBright);

        var lm = Load(Art + "landmarks.png");
        var jl = JsonUtility.FromJson<Cells>(File.ReadAllText(Art + "landmarks.json"));
        bool tipOk = true; string tipDetail = "";
        foreach (var c in jl.sprites)
            if (StormTuning.IsSpire(c.n))
            {
                float a = MaxAlpha(lm, c, c.w / 2 - 14, (int)StormTuning.RodTipY - 14, 28, 28);
                float above = MaxAlpha(lm, c, 0, 0, c.w, (int)StormTuning.RodTipY - 22);
                tipDetail += " " + c.n + " tip " + a.ToString("F2") + " above " + above.ToString("F2");
                if (a < .6f || above > .35f) tipOk = false;
            }
        Check("the lightning-rod masts' rod tips are at (128, " + StormTuning.RodTipY + ")" + tipDetail, tipOk);
        Object.DestroyImmediate(lt); Object.DestroyImmediate(lm);
    }

    [System.Serializable] class Cell { public string n; public int x, y, w, h; }
    [System.Serializable] class Cells { public Cell[] sprites; }

    static Texture2D Load(string path)
    {
        var t = new Texture2D(2, 2);
        t.LoadImage(File.ReadAllBytes(path));
        return t;
    }

    // max alpha over the sub-rect (x, y from the cell's TOP-left) of a cell (JSON y counts from the bottom)
    static float MaxAlpha(Texture2D t, Cell c, int x0, int y0, int w, int h)
    {
        float best = 0f;
        for (int y = Mathf.Max(0, y0); y < Mathf.Min(c.h, y0 + h); y++)
            for (int x = Mathf.Max(0, x0); x < Mathf.Min(c.w, x0 + w); x++)
                best = Mathf.Max(best, t.GetPixel(c.x + x, c.y + (c.h - 1 - y)).a);
        return best;
    }

    static float Brightest(Texture2D t, Cell c)
    {
        float best = 0f;
        for (int y = 0; y < c.h; y += 2)
            for (int x = 0; x < c.w; x += 2)
            {
                Color p = t.GetPixel(c.x + x, c.y + y);
                if (p.a > .5f) best = Mathf.Max(best, Mathf.Min(p.r, Mathf.Min(p.g, p.b)));
            }
        return best;
    }

    // ---- clouds and weather ----------------------------------------------------------------------------

    public const float MaxCloudCover = .15f, MaxLaneSeconds = 2.5f;

    static void Clouds()
    {
        var wb = Fresh(1);
        var d = Director(wb);
        if (d == null) return;
        float at0 = d.CeilingCover;
        Run(wb, StormTuning.CeilingHold - .5f);
        float held = d.CeilingCover;
        Run(wb, 10f - (StormTuning.CeilingHold - .5f));
        float at10 = d.CeilingCover;
        Run(wb, 5f);
        float at15 = d.CeilingCover;
        Check("the storm-cloud ceiling is thick at the start (" + at0.ToString("F2") + " ... " + held.ToString("F2") + ") and thin by 10 s (" + at10.ToString("F2") + "), gone by 15 s (" + at15.ToString("F2") + ")",
              held > .35f && at10 <= .12f && at15 <= .02f);
        Object.DestroyImmediate(wb.gameObject);

        float worstMean = 0f, worstLane = 0f, sum = 0f;
        int runs = 0;
        for (int v = 1; v <= StormTuning.Variants; v++)
            for (int seed = 1; seed <= 2; seed++)
            {
                var w2 = Fresh(v);
                var d2 = Director(w2);
                CloudCoverMeter.Reseed(d2, 9100 + 17 * seed + v);
                var st = CloudCoverMeter.Run(w2, Dt);
                worstMean = Mathf.Max(worstMean, st.covered);
                worstLane = Mathf.Max(worstLane, st.laneRun);
                sum += st.covered; runs++;
                Object.DestroyImmediate(w2.gameObject);
            }
        Check("after the ceiling, clouds cover <= " + MaxCloudCover + " of the view (worst " + worstMean.ToString("F3") + ", mean " + (sum / runs).ToString("F3") + ") and never park over the centre lane (" + worstLane.ToString("F1") + " s)",
              worstMean <= MaxCloudCover && worstLane <= MaxLaneSeconds);
    }

    static void Weather()
    {
        var wb = Fresh(2);
        var d = Director(wb);
        if (d == null) return;
        Run(wb, 3f);
        Check("heavy rain: " + d.Rain.ActiveCount + " sheets fall through the view", d.Rain.ActiveCount >= 6);
        int debrisSeen = 0;
        int g0 = d.Gusts;
        Run(wb, 90f, () => debrisSeen = Mathf.Max(debrisSeen, d.Debris.ActiveCount));
        int gusts = d.Gusts - g0;
        Check("a wind-shear gust sweeps through now and then (" + gusts + " in 90 s)", gusts >= 3);
        Check("debris tumbles through (" + debrisSeen + " alive at once)", debrisSeen >= 1);
        bool falls = true;
        var y0 = new List<float>();
        foreach (var p in d.Rain.items) y0.Add(p.root.localPosition.y);
        Run(wb, .25f);
        int k = 0;
        foreach (var p in d.Rain.items) { if (p.root.localPosition.y > y0[k] + .05f && p.root.localPosition.y < y0[k] + 3f) falls = false; k++; }
        Check("rain sheets move down relative to the view (they fall faster than the ground scrolls)", falls);
        Object.DestroyImmediate(wb.gameObject);
    }

    // ---- launch sites for the world's own elites -------------------------------------------------------

    static void Sites()
    {
        var wb = Fresh(2);
        var d = Director(wb);
        if (d == null) return;
        var sites = new List<LandingSite>();
        var kinds = new HashSet<LandingKind>();
        bool shape = true;
        int samples = 0;
        Run(wb, 240f, () =>
        {
            if (LandingSites.Collect(sites) == 0) return;
            samples++;
            foreach (var s in sites)
            {
                kinds.Add(s.kind);
                Vector3 p = s.Position - wb.transform.position;
                shape &= s.emerge && s.Valid && s.order < EliteShip.PlayOrder - 2 && s.scale > .05f && s.scale < .5f &&
                         p.y > -CameraFit.ViewTop * .5f && System.Array.IndexOf(StormTuning.SiteKinds, s.kind) >= 0;
            }
        });
        Check("Storm offers ground launch sites while flying (" + samples + " samples, " + d.SitesSpawned + " sites, kinds " + string.Join(",", kinds) + ")",
              samples > 100 && kinds.Count >= 2);
        Check("... each an emerge site, behind gameplay, small, in the upper view", shape);

        var defs = new List<EliteDef>();
        EliteCatalog.ForWorld(5, defs);
        Check("Storm has its own elites (" + defs.Count + ")", defs.Count >= 4);
        if (defs.Count == 0) { Object.DestroyImmediate(wb.gameObject); return; }
        var pilot = new GameObject("~Pilot").transform;
        pilot.position = new Vector3(0f, -2.5f, 0f);
        EliteSystem.PlayerOverride = pilot;
        bool found = false;
        for (int i = 0; i < 30 * 120 && !found; i++)
        {
            wb.Step(Dt);
            LandingSites.Collect(sites);
            found = sites.Count > 0;
            if (!found && i % 300 == 299) d.SpawnSite(float.NaN, 2);
        }
        Check("a Storm launch site comes into view", found);
        if (!found) { Object.DestroyImmediate(wb.gameObject); return; }
        var dir = new GameObject("~Dir").AddComponent<EliteDirector>();
        Random.InitState(5);
        dir.SpawnGroup(5, 1);
        var e = EliteShip.Live.Count > 0 ? EliteShip.Live[0] : null;
        Check("the director docks a Storm elite in one of the world's sites", e != null && e.IsDocked);
        if (e != null)
        {
            BackdropPiece site = null;
            foreach (var p in d.Sites.items) if (p.active && p.root == e.Site.anchor) site = p;
            Check("... inside a Storm site piece", site != null);
            if (site != null)
            {
                Sprite closed = site.sr.sprite;
                bool shutWhileParked = true, opened = false;
                for (int i = 0; i < 30 * 12 && e != null && e.State == EliteState.Parked; i++)
                {
                    bool tell = e.StateTime >= e.ParkSeconds - EliteShip.EngineTellSeconds;
                    wb.Step(Dt);
                    EliteSystem.Step(Dt);
                    if (!tell && d.SiteOpen(site)) shutWhileParked = false;
                    if (tell && d.SiteOpen(site)) opened = true;
                }
                Check("the site stays shut while the elite waits, opens in the launch tell", shutWhileParked && opened && site.sr.sprite != closed);   // (a strip site has no lamp row: its doors and vapour are the cue)
                Run(wb, 8f + StormTuning.SiteCloseDelay, () => EliteSystem.Step(Dt));
                Check("... then shuts again behind it", !d.SiteOpen(site) && site.sr.sprite == closed);
            }
        }
        Object.DestroyImmediate(dir.gameObject);
        EliteSystem.Clear();
        Object.DestroyImmediate(wb.gameObject);
    }

    // ---- 60 fps hygiene ------------------------------------------------------------------------------------

    static void Allocation()
    {
        var wb = Fresh(2);
        var d = Director(wb);
        if (d == null) return;
        Run(wb, 60f);
        long control;
        bool meter = TestHarness.AllocMeterWorks(out control);
        Check("allocation meter sees a control allocation (" + control + " bytes)", meter);
        d.ForceGust();
        int flashes = d.Flashes;
        long used = TestHarness.AllocatedBytes(() => Run(wb, 120f));
        Check("Storm backdrop allocates nothing over 2 minutes of frames, clusters, gusts, rain and " + (d.Flashes - flashes) + " lightning pulses (" + used + " bytes)",
              meter && used >= 0 && used <= 256);
        int transforms = wb.GetComponentsInChildren<Transform>(true).Length;
        Run(wb, 300f);
        Check("pools never grow over a long run", wb.GetComponentsInChildren<Transform>(true).Length == transforms);
        var before = new List<Vector3>();
        foreach (var p in d.Ground.items) before.Add(p.root.localPosition);
        int pulses = d.Storm.Pulses;
        for (int i = 0; i < 300; i++) wb.Step(0f);
        bool still = d.Storm.Pulses == pulses;
        for (int i = 0; i < d.Ground.items.Count; i++) still &= d.Ground.items[i].root.localPosition == before[i];
        Check("a paused frame (dt 0) moves nothing and no lightning strikes", still);
        Object.DestroyImmediate(wb.gameObject);
    }
}
