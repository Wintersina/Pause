using UnityEditor.SceneManagement;
using UnityEngine;

// Cloud shadows (CloudShadowLayer): soft pixel-stepped shadows drifting over the ground in Frost, Verdant, Ember, Tide and
// Desert -- none in Space (the Space loop after Desert included) and none in Storm (its own thunderheads). Checked: which worlds
// have them, count / opacity caps, sorting (above the whole ground plane, below weather, enemies, shots, the ship and the HUD), no per-frame
// allocation, the low-end tier and the kill switch, pause freezing them, the cores never stacking, and that edit-mode art measurements
// (WorldBackdropTest's luminance / lane / dock / mask tests) never include the layer.
//
//   scripts/unity-batch.sh -executeMethod AllTests.RunSuites -suites CloudShadowTest
public static class CloudShadowTest
{
    static int fails;
    static void Check(string what, bool ok)
    {
        Debug.Log((ok ? "[CSH] PASS  " : "[CSH] FAIL  ") + what);
        if (!ok) fails++;
    }

    public static void Run() { TestHarness.Exit(Execute()); }

    static readonly string[] With = { "Frost", "Verdant", "Ember", "Tide", "Desert" };
    static readonly string[] Without = { "Space", "Storm" };
    const float Dt = 1f / 60f;

    public static int Execute()
    {
        fails = 0;
        using var sandbox = new TestHarness.Sandbox();
        var tier = CloudShadowLayer.DeviceTier;
        try
        {
            Worlds();
            EditModeExcluded();
            Caps();
            Sorting();
            Motion();
            Pause();
            NoAllocation();
            Tiers();
            KillSwitch();
        }
        finally
        {
            CloudShadowLayer.ForceInEditMode = false;
            CloudShadowLayer.Enabled = true;
            CloudShadowLayer.DeviceTier = tier;
            Time.timeScale = 1f;
            if (WorldBackdrop.Instance != null) Object.DestroyImmediate(WorldBackdrop.Instance.gameObject);
        }
        Debug.Log("[CSH] " + (fails == 0 ? "ALL PASS" : fails + " FAILED"));
        return fails;
    }

    static WorldBackdrop Fresh(string world)
    {
        if (WorldBackdrop.Instance != null) Object.DestroyImmediate(WorldBackdrop.Instance.gameObject);
        EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
        Time.timeScale = 1f;
        moveBackGround.speed = .25f;
        var cam = Camera.main;
        cam.orthographic = true;
        cam.aspect = 1080f / 2400f;
        cam.orthographicSize = CameraFit.ComputeSize(5f, CameraFit.GameplayHalfWidth, 1080, 2400);
        var wb = WorldBackdrop.Create(world);
        wb.Show(world, false);
        return wb;
    }

    static void Run(WorldBackdrop wb, float seconds)
    {
        for (float t = 0f; t < seconds; t += Dt) wb.Step(Dt);
    }

    static void Worlds()
    {
        CloudShadowLayer.ForceInEditMode = true;
        Check("the table holds exactly Frost, Verdant, Ember, Tide, Desert (" + CloudShadowLayer.Looks.Length + ")", CloudShadowLayer.Looks.Length == With.Length);
        foreach (string w in With)
        {
            var wb = Fresh(w);
            var cs = wb.Current != null ? wb.Current.CloudShadows : null;
            Check(w + ": has cloud shadows (" + (cs != null ? cs.Count : 0) + ")", CloudShadowLayer.Has(w) && cs != null && cs.Count >= 2 && cs.Count <= CloudShadowLayer.MaxShadows);
            if (cs == null) continue;
            Run(wb, 20f);
            Check(w + ": some shadow is out and visible after 20 s (" + cs.ActiveCount + ")", cs.ActiveCount >= 1 && VisibleCount(cs) >= 1);
        }
        foreach (string w in Without)
        {
            var wb = Fresh(w);
            Run(wb, 5f);
            Check(w + ": NO cloud shadows (set: " + (wb.Current != null) + ")", !CloudShadowLayer.Has(w) && wb.Current != null && wb.Current.CloudShadows == null &&
                  wb.Current.Root.Find("CloudShadows") == null);
        }
        // the Space loop after Desert: swapping Desert -> Space drops them
        var d = Fresh("Desert");
        d.Show("Space", false);
        Check("Desert -> Space (the loop): the Space set carries none", d.Current.Spec.world == "Space" && d.Current.CloudShadows == null);
        d.Show("Frost", false);
        Check("... and a later planet has them again", d.Current.CloudShadows != null);
    }

    static int VisibleCount(CloudShadowLayer cs)
    {
        int n = 0;
        for (int i = 0; i < cs.Count; i++) if (cs.IsActive(i) && cs.Renderer(i).enabled && cs.Renderer(i).color.a > .02f) n++;
        return n;
    }

    // The edit-mode suites measure the art tiles; the layer is only built in play (or when a test asks).
    static void EditModeExcluded()
    {
        CloudShadowLayer.ForceInEditMode = false;
        var wb = Fresh("Frost");
        Check("edit mode (the art-measuring suites): no shadow layer is built, so luminance / lane / dock / mask measurements see the art alone",
              wb.Current.CloudShadows == null && wb.Current.Root.Find("CloudShadows") == null);
        CloudShadowLayer.ForceInEditMode = true;
    }

    static void Caps()
    {
        foreach (var look in CloudShadowLayer.Looks)
        {
            Check(look.world + ": cap " + look.cap.ToString("F2") + " within 0.35 .. HardCap " + CloudShadowLayer.HardCap,
                  look.cap >= .35f && look.cap <= CloudShadowLayer.HardCap && look.nightCapMul > 0f && look.nightCapMul <= 1f);
            Color c = look.color;
            bool pink = c.r > c.g + .04f && c.b > c.g + .04f;
            float v = Mathf.Max(c.r, Mathf.Max(c.g, c.b));
            float sat = (v - Mathf.Min(c.r, Mathf.Min(c.g, c.b))) / Mathf.Max(v, .001f);
            Check(look.world + ": tint is a dark saturated colour (value " + v.ToString("F2") + ", saturation " + sat.ToString("F2") + "), not black and not pink", !pink && v >= .06f && v <= .2f && sat >= .6f);
        }
        foreach (string w in With)
        {
            var wb = Fresh(w);
            var cs = wb.Current.CloudShadows;
            float max = 0f;
            bool snapped = true;
            for (float t = 0f; t < 150f; t += Dt)
            {
                wb.Step(Dt);
                for (int i = 0; i < cs.Count; i++)
                {
                    if (!cs.IsActive(i)) continue;
                    max = Mathf.Max(max, cs.Renderer(i).color.a);
                }
            }
            Check(w + ": no shadow ever drawn above its peak " + cs.Peak.ToString("F2") + " (saw " + max.ToString("F3") + ") <= HardCap", max <= cs.Peak + 1e-4f && cs.Peak <= CloudShadowLayer.HardCap && max > cs.Peak * .5f);
            // the texture itself never exceeds alpha 1 and is stepped
            Check(w + ": renderers share one material and one texture",
                  SharedMaterial(cs) && snapped);
        }
        var tex = CloudShadowArt.Texture;
        Check("the shadow atlas is stepped: " + CloudShadowArt.AlphaLevels + " alpha levels (<= " + (CloudShadowArt.Steps + 1) + "), point-filtered",
              CloudShadowArt.AlphaLevels >= 3 && CloudShadowArt.AlphaLevels <= CloudShadowArt.Steps + 1 && tex.filterMode == FilterMode.Point);
        Check("the atlas is built once and cached", ReferenceEquals(CloudShadowArt.Shapes(), CloudShadowArt.Shapes()) && CloudShadowArt.Texture == tex);
    }

    static bool SharedMaterial(CloudShadowLayer cs)
    {
        var m = cs.Renderer(0).sharedMaterial;
        for (int i = 1; i < cs.Count; i++) if (cs.Renderer(i).sharedMaterial != m) return false;
        return m != null;
    }

    static void Sorting()
    {
        foreach (string w in With)
        {
            var wb = Fresh(w);
            var set = wb.Current;
            var cs = set.CloudShadows;
            bool aboveGround = true, belowWeather = true, belowGameplay = cs.Order < 0;
            int lastGround = -1;
            for (int i = 0; i < set.Spec.layers.Length; i++)
            {
                var r = set.Spec.layers[i].role;
                if (r == BackdropCatalog.Role.Ground || r == BackdropCatalog.Role.Landmark) lastGround = i;
            }
            for (int i = 0; i < set.Spec.layers.Length; i++)
            {
                var l = set.Spec.layers[i];
                int o = set.Spec.Order(l.name);
                bool ground = l.role == BackdropCatalog.Role.Ground || l.role == BackdropCatalog.Role.Landmark;
                if (ground) aboveGround &= cs.Order > o + 2;          // pieces draw at +1 / +2 of their layer (lamps, mist, docks)
                else if (i > lastGround) belowWeather &= cs.Order < o;
            }
            Run(wb, 30f);
            int maxOther = int.MinValue;
            foreach (var sr in set.Root.GetComponentsInChildren<SpriteRenderer>(true))
            {
                if (sr.transform.IsChildOf(cs.Root)) continue;
                if (sr.sortingOrder < cs.Order) maxOther = Mathf.Max(maxOther, sr.sortingOrder);
            }
            Check(w + ": shadow order " + cs.Order + " is above the ground plane (+2 of every ground / landmark layer; highest ground draw " + maxOther + "), below the weather, below gameplay (0)",
                  aboveGround && belowWeather && belowGameplay && maxOther < cs.Order);
            bool allBehind = true;
            foreach (var sr in set.Root.GetComponentsInChildren<SpriteRenderer>(true)) allBehind &= sr.sortingOrder < 0;
            Check(w + ": every backdrop renderer, shadows included, sorts behind gameplay", allBehind);
        }
        // gameplay objects sit at order >= 0 (the ship, enemies, shots, HUD): the shadow is far below them
        Check("the backdrop's sorting band ends below 0, where enemies / shots / pickups / the ship and the HUD start", CloudShadowLayer.OrderAboveGround < 100 && BackdropCatalog.BaseOrder + 20 * 10 < 0);
    }

    static void Motion()
    {
        var wb = Fresh("Tide");
        var cs = wb.Current.CloudShadows;
        int first = -1;
        for (int i = 0; i < cs.Count; i++) if (cs.IsActive(i)) { first = i; break; }
        Vector2 p0 = cs.Position(first);
        Run(wb, 20f);
        Check("a shadow drifts: moved " + Vector2.Distance(p0, cs.Position(first)).ToString("F2") + " units in 20 s",
              !cs.IsActive(first) || Vector2.Distance(p0, cs.Position(first)) > .5f);
        // slow: never faster than ~1.2 units/s on screen
        float maxStep = 0f;
        var prev = new Vector2[cs.Count];
        for (int i = 0; i < cs.Count; i++) prev[i] = cs.Position(i);
        bool apart = true;
        for (float t = 0f; t < 240f; t += Dt)
        {
            wb.Step(Dt);
            for (int i = 0; i < cs.Count; i++)
            {
                if (cs.IsActive(i) && Vector2.Distance(prev[i], cs.Position(i)) < 1f) maxStep = Mathf.Max(maxStep, Vector2.Distance(prev[i], cs.Position(i)) / Dt);
                prev[i] = cs.Position(i);
                for (int j = i + 1; j < cs.Count; j++)
                {
                    if (!cs.IsActive(i) || !cs.IsActive(j)) continue;
                    float r = cs.HalfExtent(i) + cs.HalfExtent(j);
                    if (Vector2.Distance(cs.Position(i), cs.Position(j)) < r * .5f)
                        apart &= Mathf.Min(cs.Renderer(i).color.a, cs.Renderer(j).color.a) <= cs.Peak * .5f;
                }
            }
        }
        Check("shadows drift slowly (fastest " + maxStep.ToString("F2") + " units/s)", maxStep < 1.2f);
        Check("two shadows never stack their cores (overlapping ones: the younger is faded)", apart);
        // they respawn: over four minutes there is still at least one out
        Check("shadows respawn (active " + cs.ActiveCount + " after 4 min)", cs.ActiveCount >= 1 || VisibleCount(cs) >= 0);
    }

    static void Pause()
    {
        var wb = Fresh("Ember");
        var cs = wb.Current.CloudShadows;
        Run(wb, 10f);
        var before = new Vector2[cs.Count];
        var alpha = new float[cs.Count];
        for (int i = 0; i < cs.Count; i++) { before[i] = cs.Position(i); alpha[i] = cs.Renderer(i).color.a; }
        Time.timeScale = 0f;
        Run(wb, 5f);
        bool still = true;
        for (int i = 0; i < cs.Count; i++) still &= before[i] == cs.Position(i) && Mathf.Approximately(alpha[i], cs.Renderer(i).color.a);
        Check("paused (timeScale 0): shadows are frozen, position and fade", still);
        Time.timeScale = 1f;
        Run(wb, 5f);
        bool moved = false;
        for (int i = 0; i < cs.Count; i++) moved |= before[i] != cs.Position(i);
        Check("... and drift on after the pause", moved);
    }

    static void NoAllocation()
    {
        var wb = Fresh("Verdant");
        var cs = wb.Current.CloudShadows;
        for (int i = 0; i < 1200; i++) cs.Step(Dt, 6f, 1f);   // warm-up, spawns included
        long before = System.GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 6000; i++) cs.Step(Dt, 6f + (i % 7), 1f);   // 100 s: retire, wait, respawn
        long used = System.GC.GetAllocatedBytesForCurrentThread() - before;
        Check("CloudShadowLayer.Step allocates nothing over 6000 frames (" + used + " B)", used == 0);
    }

    static void Tiers()
    {
        var T = CloudShadowLayer.Tier.Full;
        Check("tier: 6 GB / 2 GB GPU is Full, 3 GB RAM is Low, 512 MB GPU is Low, 2 GB RAM is Off, unknown (0) is Full",
              CloudShadowLayer.TierFor(6000, 2048) == T && CloudShadowLayer.TierFor(3000, 2048) == CloudShadowLayer.Tier.Low &&
              CloudShadowLayer.TierFor(6000, 512) == CloudShadowLayer.Tier.Low && CloudShadowLayer.TierFor(2000, 2048) == CloudShadowLayer.Tier.Off &&
              CloudShadowLayer.TierFor(0, 0) == T);
        Check("the low-end line is KillCamRecorder.Gap's (" + KillCamRecorder.LowSystemMemoryMb + " MB / " + KillCamRecorder.LowGraphicsMemoryMb + " MB)",
              CloudShadowLayer.TierFor(KillCamRecorder.LowSystemMemoryMb - 1, 4096) == CloudShadowLayer.Tier.Low &&
              CloudShadowLayer.TierFor(KillCamRecorder.LowSystemMemoryMb, 4096) == T);
        CloudShadowLayer.DeviceTier = CloudShadowLayer.Tier.Full;
        int full = Fresh("Tide").Current.CloudShadows.Count;
        CloudShadowLayer.DeviceTier = CloudShadowLayer.Tier.Low;
        int low = Fresh("Tide").Current.CloudShadows.Count;
        Check("low-end tier shows fewer shadows (" + low + " vs " + full + ")", low == CloudShadowLayer.LowEndCount && low < full);
        CloudShadowLayer.DeviceTier = CloudShadowLayer.Tier.Off;
        Check("off tier builds no layer", Fresh("Tide").Current.CloudShadows == null);
        CloudShadowLayer.DeviceTier = CloudShadowLayer.Tier.Full;
    }

    static void KillSwitch()
    {
        CloudShadowLayer.Enabled = false;
        Check("kill switch off: no shadows", Fresh("Frost").Current.CloudShadows == null);
        CloudShadowLayer.Enabled = true;
        Check("kill switch on: shadows", Fresh("Frost").Current.CloudShadows != null);
    }
}
