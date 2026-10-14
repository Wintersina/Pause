using UnityEditor;
using UnityEngine;

// THE SPACE SUN (SpaceSunLayer, Art/.../Space/Backdrop/sun_*, flares_*): both palettes load and import at full resolution, the
// palette is one stable pick per run (and varies across runs), the body turns smoothly and slowly with no allocation, the
// flare flipbook plays and the bigger prominence arcs come and go one at a time, it sits at the screen's edge on every phone
// shape without covering the lane, the menu shows it dimmed, and it is torn down with its set.
//
//   Unity -batchmode -quit -projectPath <abs>/Pause -executeMethod SpaceSunTest.Run
public static class SpaceSunTest
{
    static int fails;
    static void Check(string what, bool ok, string extra = "")
    {
        Debug.Log((ok ? "[SUN] PASS  " : "[SUN] FAIL  ") + what + (extra.Length > 0 ? "  [" + extra + "]" : ""));
        if (!ok) fails++;
    }

    public static void Run() { TestHarness.Exit(Execute()); }

    const float Dt = 1f / 60f;
    const string Dir = "Assets/Art/Backgrounds/Resources/Worlds/Space/Backdrop/";

    public static int Execute()
    {
        fails = 0;
        using var sandbox = new TestHarness.Sandbox();
        var root = new GameObject("~SunTestRoot").transform;
        try
        {
            Files();
            Pick();
            var set = new BackdropSet("Space", root, 3.72f, 6.6f);
            Check("the Space set is complete and has a sun", set.Complete && set.Sun != null && set.Sun.Built);
            if (set.Sun != null && set.Sun.Built)
            {
                Rotation(set);
                Flipbook(set);
                Arcs(set);
                Allocation(set);
            }
            set.Destroy();
            Check("the sun is torn down with its set", GameObject.Find("SpaceSun") == null);
            Screens(root);
            Menu(root);
            RunStability(root);
        }
        finally
        {
            Object.DestroyImmediate(root.gameObject);
        }
        Debug.Log("[SUN] " + (fails == 0 ? "ALL PASS" : fails + " FAILED"));
        return fails;
    }

    static void Files()
    {
        foreach (var p in new[] { "warm", "violet" })
        {
            var disc = Resources.Load<Texture2D>("Worlds/Space/Backdrop/sun_" + p);
            var corona = Resources.Load<Texture2D>("Worlds/Space/Backdrop/sun_" + p + "_corona");
            var flares = Resources.Load<Texture2D>("Worlds/Space/Backdrop/flares_" + p);
            Check(p + ": the three sheets load", disc != null && corona != null && flares != null);
            if (disc == null || corona == null || flares == null) continue;
            Check(p + ": sheet sizes (2048x1024, 2048x1024, 4096x2048: full resolution, nothing downscaled)",
                  disc.width == 2048 && disc.height == 1024 && corona.width == 2048 && corona.height == 1024 && flares.width == 4096 && flares.height == 2048);
            foreach (var n in new[] { "sun_" + p, "sun_" + p + "_corona", "flares_" + p })
            {
                var imp = AssetImporter.GetAtPath(Dir + n + ".png") as TextureImporter;
                var t = AssetDatabase.LoadAssetAtPath<Texture2D>(Dir + n + ".png");
                bool softLayer = n != "sun_" + p;
                Check(n + ": import is not clamped, has no mip maps", imp != null && t != null && imp.maxTextureSize >= Mathf.Max(t.width, t.height) && !imp.mipmapEnabled);
                Check(n + ": " + (softLayer ? "bilinear" : "point") + " filter", imp != null && imp.filterMode == (softLayer ? FilterMode.Bilinear : FilterMode.Point));
                var mobile = imp.GetPlatformTextureSettings("Android");
                Check(n + ": mobile " + (softLayer ? "ASTC 4x4 (soft additive layer)" : "RGBA32 (hard pixel art)"),
                      mobile.overridden && mobile.format == (softLayer ? TextureImporterFormat.ASTC_4x4 : TextureImporterFormat.RGBA32));
            }
        }
    }

    static void Pick()
    {
        int warm = 0, violet = 0, left = 0, right = 0;
        for (int s = 0; s < 64; s++)
        {
            Check("seed " + s + ": the pick is stable", SpaceSunPick.PaletteFor(s) == SpaceSunPick.PaletteFor(s) && SpaceSunPick.SideFor(s) == SpaceSunPick.SideFor(s), "") ;
            if (SpaceSunPick.PaletteFor(s) == SpaceSunPick.Warm) warm++; else violet++;
            if (SpaceSunPick.SideFor(s) < 0) left++; else right++;
        }
        Check("across seeds both palettes appear, roughly evenly", warm >= 20 && violet >= 20, warm + " warm / " + violet + " violet");
        Check("across seeds the sun sits on both sides", left >= 20 && right >= 20, left + " / " + right);
    }

    static int SeedFor(string palette)
    {
        for (int s = 1; s < 1000; s++) if (SpaceSunPick.PaletteFor(s) == palette) return s;
        return 1;
    }

    // The run's pick never changes within a run: sets built again (a world change, a menu) show the same palette.
    static void RunStability(Transform root)
    {
        SpaceSunPick.NewRun(SeedFor(SpaceSunPick.Warm));
        var a = new BackdropSet("Space", root, 3.72f, 6.6f);
        var b = new BackdropSet("Space", root, 3.72f, 6.6f);
        Check("a run showing warm: every set built in it is warm", a.Sun.Palette == SpaceSunPick.Warm && b.Sun.Palette == SpaceSunPick.Warm);
        a.Destroy(); b.Destroy();
        SpaceSunPick.NewRun(SeedFor(SpaceSunPick.Violet));
        var c = new BackdropSet("Space", root, 3.72f, 6.6f);
        Check("the next run can be the other palette", c.Sun.Palette == SpaceSunPick.Violet);
        Check("the side matches the seed's pick", Mathf.Sign(c.Sun.Root.localPosition.x) == SpaceSunPick.Side);
        c.Destroy();
    }

    static void Rotation(BackdropSet set)
    {
        var sun = set.Sun;
        float last = sun.Angle, minD = 999f, maxD = -999f, total = 0f;
        for (int i = 0; i < 600; i++)
        {
            sun.Step(Dt);
            float d = Mathf.DeltaAngle(last, sun.Angle); last = sun.Angle;
            minD = Mathf.Min(minD, d); maxD = Mathf.Max(maxD, d); total += d;
        }
        float per = 360f * Dt / SpaceSunTuning.TurnSeconds;
        Check("the body turns smoothly: every frame the same small step", maxD - minD < 1e-3f && Mathf.Abs(minD - per) < 1e-3f, minD.ToString("F4") + " .. " + maxD.ToString("F4"));
        Check("... always one way and very slowly (< .2 deg per frame, one turn in 2 - 5 minutes)", minD > 0f && maxD < .2f && SpaceSunTuning.TurnSeconds >= 120f && SpaceSunTuning.TurnSeconds <= 300f);
        float before = sun.Angle; sun.Step(0f);
        Check("a paused world holds it still (dt 0)", sun.Angle == before);
        Check("the corona and the flares turn with the body (one root)", sun.Corona.transform.parent == sun.Root && sun.Flare.transform.parent == sun.Root && sun.Disc.transform.parent == sun.Root);
        Check("it is a pure backdrop piece: no collider anywhere in it", sun.Root.GetComponentsInChildren<Collider2D>(true).Length == 0 && sun.Root.GetComponentsInChildren<Collider>(true).Length == 0);
        Check("low contrast: the disc and the layers draw under their alpha caps", sun.Disc.color.a <= SpaceSunTuning.DiscAlpha + 1e-3f && sun.Flare.color.a <= SpaceSunTuning.FlareAlpha + 1e-3f && sun.Corona.color.a <= SpaceSunTuning.CoronaAlpha + 1e-3f);
    }

    static void Flipbook(BackdropSet set)
    {
        var sun = set.Sun;
        var seen = new System.Collections.Generic.HashSet<Sprite>();
        var order = new System.Collections.Generic.List<int>();
        for (int i = 0; i < 6 * 60 / 2; i++)
        {
            sun.Step(Dt);
            seen.Add(sun.Flare.sprite);
            if (order.Count == 0 || order[order.Count - 1] != sun.FlareFrame) order.Add(sun.FlareFrame);
        }
        Check("the flare flipbook plays all 8 frames", seen.Count == 8, seen.Count + " frames");
        bool inOrder = true;
        for (int i = 1; i < order.Count; i++) inOrder &= order[i] == (order[i - 1] + 1) % 8;
        Check("... in order, looping", inOrder && order.Count >= 8);
    }

    static void Arcs(BackdropSet set)
    {
        var sun = set.Sun;
        int start = sun.ArcCount; bool overlap = false, tooBright = false; int active = 0; float prevAge = -1f; int arcs = 0;
        for (int i = 0; i < 900 * 10; i++)
        {
            sun.Step(.1f);
            if (sun.Arc.enabled) { if (sun.ArcAge < 0f) overlap = true; tooBright |= sun.Arc.color.a > SpaceSunTuning.ArcAlpha + 1e-3f; }
            if (sun.ArcAge >= 0f && prevAge < 0f) arcs++;
            prevAge = sun.ArcAge;
            if (sun.Arc.enabled) active++;
        }
        Check("bigger prominence arcs come at random intervals (" + (sun.ArcCount - start) + " in 900 s)", sun.ArcCount - start >= 10 && sun.ArcCount - start <= 40 && arcs == sun.ArcCount - start);
        Check("... one at a time, never over the alpha cap, and for part of the time only", !overlap && !tooBright && active > 0 && active < 900 * 10 / 2);
    }

    static void Allocation(BackdropSet set)
    {
        // (A game scene opened earlier makes the editor allocate a few bytes per Transform write: per-frame garbage of the
        // sun's own would be far above this ceiling.)
        var sun = set.Sun;
        long bytes = TestHarness.AllocatedBytes(() => { for (int i = 0; i < 300; i++) sun.Step(Dt); });
        Check("stepping the sun allocates no per-frame garbage", bytes <= 300 * 5 * 24, bytes + " B / 300 frames");
    }

    static void Screens(Transform root)
    {
        float[][] screens = { new[] { 1080f, 1920f }, new[] { 1080f, 2340f }, new[] { 1080f, 2520f }, new[] { 1200f, 2560f }, new[] { 1640f, 2640f }, new[] { 2000f, 2000f } };
        foreach (var sc in screens)
        {
            float hw = Mathf.Max(CameraFit.GameplayViewHalfWidth(new Vector2(sc[0], sc[1])), 3.72f);
            float hh = hw * sc[1] / sc[0];
            var set = new BackdropSet("Space", root, hw, hh);
            string tag = sc[0] + "x" + sc[1];
            float r = SpaceSunTuning.CellUnits * SpaceSunTuning.DiscRadiusShare;
            float cx = Mathf.Abs(set.Sun.Root.localPosition.x), cy = set.Sun.Root.localPosition.y;
            float inner = cx - r;
            float visible = Mathf.Clamp01((hw - inner) / (2f * r));
            Check("screen " + tag + ": the disc stays off the lane (inner edge >= 2.0 u from the centre line)", inner >= 2.0f, inner.ToString("F2"));
            Check("screen " + tag + ": an edge of it shows (" + (visible * 100f).ToString("F0") + "% of the disc)", visible > 0f && visible <= .75f);
            Check("screen " + tag + ": it sits in the upper half of the view", cy > 0f && cy < hh);
            set.Destroy();
        }
    }

    static void Menu(Transform root)
    {
        var set = new BackdropSet("Space", root, 3.72f, 6.6f, new BackdropSet.MenuOptions { variant = 0, dim = .5f });
        Check("the menu's Space backdrop shows the sun too", set.Sun != null && set.Sun.Built);
        if (set.Sun != null) { set.Sun.Step(Dt); Check("... dimmed like its tiles", set.Sun.Disc.color.a <= SpaceSunTuning.DiscAlpha * .5f + 1e-3f, set.Sun.Disc.color.a.ToString("F2")); }
        set.Destroy();
    }
}
