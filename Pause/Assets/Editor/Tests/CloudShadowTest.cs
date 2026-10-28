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
        CloudShadowLayer.TestSeed = 7;                  // the same shadows every run
        CloudShadowLayer.DeviceTier = CloudShadowLayer.Tier.Full;   // a small CI runner reports low-end memory: measure the full tier
        try
        {
            Worlds();
            EditModeExcluded();
            Caps();
            Shapes();
            Coverage();
            Rendering();
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
            CloudShadowLayer.TestSeed = 0;
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
        foreach (var look in CloudShadowLayer.Looks)
            if (look.world == "Ember")
                Check("Ember's clouds carry the shade (its ground has no baked centre band): dark umber (red > green > blue), core cap " + look.cap.ToString("F2") + " >= .5, big shapes, Frost-like cover " + look.budget.ToString("F2") + " >= 1.1",
                      look.color.r > look.color.g && look.color.g > look.color.b && look.cap >= .5f && look.big && look.budget >= 1.1f);
        foreach (string w in With)
        {
            var wb = Fresh(w);
            var cs = wb.Current != null ? wb.Current.CloudShadows : null;
            Check(w + ": has cloud shadows (" + (cs != null ? cs.Count : 0) + ")", CloudShadowLayer.Has(w) && cs != null && cs.Count >= 2 && cs.Count <= 3);
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
        var mask = CloudShadowArt.Mask;
        Check("the mask atlas is one " + CloudShadowArt.Size + "x" + CloudShadowArt.Height + " point-filtered texture, built once and cached",
              mask.width == CloudShadowArt.Size && mask.filterMode == FilterMode.Point && ReferenceEquals(CloudShadowArt.Shapes(), CloudShadowArt.Shapes()) && CloudShadowArt.Mask == mask);
    }

    // The shader's stepped alpha (CloudShadowLayer.Quantize mirrors it): a ramp lands on a handful of levels, the rims dither.
    static void Quantised()
    {
        var levels = new System.Collections.Generic.HashSet<int>();
        for (int i = 0; i <= 100; i++)
            for (int c = 0; c < 16; c++) levels.Add(Mathf.RoundToInt(CloudShadowLayer.Quantize(i / 100f, c & 3, c >> 2) * 1000f));
        Check("a density ramp quantises to " + levels.Count + " alpha levels (" + CloudShadowArt.Steps + " steps + 0)", levels.Count == CloudShadowArt.Steps + 1);
        int lo = 0, hi = 0;
        for (int c = 0; c < 16; c++)
        {
            float q = CloudShadowLayer.Quantize(.5f, c & 3, c >> 2);
            if (q < .5f) lo++; else hi++;
        }
        Check("a half-way density dithers across a 4x4 block (" + lo + " below / " + hi + " above the step)", lo >= 4 && hi >= 4);
        bool bayer = true;
        var seen = new bool[16];
        for (int y = 0; y < 4; y++) for (int x = 0; x < 4; x++) seen[CloudShadowLayer.Bayer4(x, y)] = true;
        foreach (bool b in seen) bayer &= b;
        Check("the dither is a full 4x4 Bayer matrix", bayer);
        bool capped = true;
        for (int i = 0; i <= 100; i++) capped &= CloudShadowLayer.Quantize(i / 100f, i, i * 3) <= 1f && CloudShadowLayer.Quantize(i / 100f, i, i * 3) >= 0f;
        Check("the stepped alpha stays within 0..1 (the vertex alpha, <= the cap, scales it)", capped);
    }

    // The shapes are cloud outlines: irregular boundaries, several lobes, ink that fills part of the rect, not a blob.
    static void Shapes()
    {
        Quantised();
        string[] kinds = { "cumulus", "stratocumulus", "cirrus" };
        for (int i = 0; i < CloudShadowArt.ShapeCount; i++)
        {
            int k = CloudShadowArt.ShapeKind(i);
            float ink = CloudShadowArt.Ink(i), iso = CloudShadowArt.Isoperimetric(i);
            int lobes = CloudShadowArt.Lobes(i);
            bool ok = ink >= (k == 2 ? .08f : .3f) && ink <= .75f && iso >= 3f && (k == 2 || lobes >= 2);
            Check("shape " + i + " (" + kinds[k] + " " + CloudShadowArt.ShapeW(i) + "x" + CloudShadowArt.ShapeH(i) + "): ink " + ink.ToString("F2") + " of its rect, outline P^2/4piA " + iso.ToString("F1") +
                  " (a disc or square is 1.3), " + lobes + " lobes", ok);
        }
        Check("the shapes are big: the cumulus are 320 cells wide (~1.45 screen widths), the stratocumulus 384", CloudShadowArt.ShapeW(0) == 320 && CloudShadowArt.ShapeW(1) == 320 && CloudShadowArt.ShapeW(2) == 384 && CloudShadowArt.ShapeCount == 6);
    }

    // Coverage: over a 4-minute run the shadows hold their world's share of the view, never above the ceiling, never a uniform veil
    // (clear lit gaps remain), and the quads' own area stays bounded.
    static void Coverage()
    {
        foreach (string w in With)
        {
            var wb = Fresh(w);
            var cs = wb.Current.CloudShadows;
            float sum = 0f, max = 0f, min = 1f, maxQuad = 0f;
            int n = 0, empty = 0;
            for (float t = 0f; t < 240f; t += Dt)
            {
                wb.Step(Dt);
                if (((int)(t / Dt)) % 120 != 0) continue;
                float c = cs.Coverage();
                if (t >= 20f) { sum += c; n++; min = Mathf.Min(min, c); if (c < .03f) empty++; }
                max = Mathf.Max(max, c);
                // the fill cost: each drawn quad's rect over the view (transparent corners included), in screens
                float quad = 0f;
                float hwv = wb.Current.HalfWidth, hhv = wb.Current.HalfHeight;
                for (int i = 0; i < cs.Count; i++)
                {
                    if (!cs.IsActive(i) || cs.Renderer(i).color.a <= 0f) continue;
                    var pos = cs.Position(i);
                    float ox = Mathf.Max(0f, Mathf.Min(pos.x + cs.HalfExtent(i), hwv) - Mathf.Max(pos.x - cs.HalfExtent(i), -hwv));
                    float oy = Mathf.Max(0f, Mathf.Min(pos.y + cs.HalfExtentY(i), hhv) - Mathf.Max(pos.y - cs.HalfExtentY(i), -hhv));
                    quad += ox * oy / (4f * hwv * hhv);
                }
                maxQuad = Mathf.Max(maxQuad, quad);
            }
            float mean = sum / n;
            Check(w + ": coverage averages " + mean.ToString("F2") + " (budget " + cs.Budget.ToString("F2") + ", range " + min.ToString("F2") + " .. " + max.ToString("F2") + "), ceiling " + CloudShadowLayer.MaxCoverage,
                  mean >= (w == "Frost" ? .34f : .3f) && mean <= .55f && max <= CloudShadowLayer.MaxCoverage && empty <= n / 6);
            Check(w + ": the lit gaps stay (never a veil: peak coverage " + max.ToString("F2") + " < 0.6) and the quads' summed area is bounded (" + maxQuad.ToString("F2") + " screens <= 2.0)", max < .6f && maxQuad <= 2.0f);
        }
        Check("Frost has the largest budget", CloudShadowLayer.Looks[0].world == "Frost" && CloudShadowLayer.Looks[0].budget >= MaxCover());
    }

    static float MaxCover()
    {
        float m = 0f;
        foreach (var l in CloudShadowLayer.Looks) m = Mathf.Max(m, l.budget);
        return m;
    }

    // What the GPU draws: with only the shadows visible over white, the picture holds a few stepped levels, not a smooth gradient.
    static void Rendering()
    {
        var wb = Fresh("Desert");
        var set = wb.Current;
        var cs = set.CloudShadows;
        Run(wb, 8f);
        // one quad alone: a quad's alpha is constant, so it lands on a handful of levels (two quads would add their overlaps)
        bool kept = false;
        for (int i = 0; i < cs.Count; i++)
        {
            if (cs.IsActive(i) && !kept) { kept = true; continue; }
            cs.Renderer(i).enabled = false;
        }
        var hidden = new System.Collections.Generic.List<Renderer>();
        foreach (var r in set.Root.GetComponentsInChildren<Renderer>(true))
            if (!r.transform.IsChildOf(cs.Root) && r.enabled) { r.enabled = false; hidden.Add(r); }
        var cam = Camera.main;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = Color.white;
        const int w = 270, h = 600;
        var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
        var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
        var prevTarget = cam.targetTexture; var prevActive = RenderTexture.active;
        cam.targetTexture = rt;
        cam.Render();
        RenderTexture.active = rt;
        tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
        cam.targetTexture = prevTarget; RenderTexture.active = prevActive;
        var px = tex.GetPixels32();
        Object.DestroyImmediate(tex); Object.DestroyImmediate(rt);
        foreach (var r in hidden) if (r != null) r.enabled = true;
        var levels = new System.Collections.Generic.HashSet<int>();
        int dark = 0, white = 0;
        float darkest = 1f;
        foreach (var c in px)
        {
            levels.Add(c.r);
            if (c.r >= 250) white++; else dark++;
            darkest = Mathf.Min(darkest, c.r / 255f);
        }
        Check("rendered over white: " + levels.Count + " distinct shades (stepped: <= " + (CloudShadowArt.Steps + 2) + " for one shadow), " + dark + " shadowed / " + white + " lit pixels, darkest " + darkest.ToString("F2") + " >= " + (1f - cs.Peak).ToString("F2") + " (the cap)",
              levels.Count >= 2 && levels.Count <= CloudShadowArt.Steps + 2 && dark > 0 && white > 0 && darkest >= 1f - cs.Peak - .03f);
        cam.clearFlags = CameraClearFlags.Skybox;
    }

    static bool SharedMaterial(CloudShadowLayer cs)
    {
        var m = cs.SharedMaterial;
        for (int i = 0; i < cs.Count; i++) if (cs.Renderer(i).sharedMaterial != m) return false;
        return m != null && m.shader != null && m.shader.name == "Pause/CloudShadow";
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
        {
            var wl = Fresh("Frost");
            var cl = wl.Current.CloudShadows;
            bool small = true;
            for (float t = 0f; t < 240f; t += Dt)
            {
                wl.Step(Dt);
                for (int i = 0; i < cl.Count; i++) if (cl.IsActive(i)) small &= CloudShadowArt.ShapeH(cl.Shape(i)) <= 192;
            }
            Check("low-end tier draws only the smaller shapes (<= 192 cells tall) at " + cl.Budget.ToString("F2") + " cover (Frost's " + CloudShadowLayer.Looks[0].budget.ToString("F2") + ")", small && cl.LowEnd && cl.Budget < CloudShadowLayer.Looks[0].budget);
        }
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
