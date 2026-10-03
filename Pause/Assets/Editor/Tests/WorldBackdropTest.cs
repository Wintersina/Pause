using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Profiling;

// Headless checks of the animated world backgrounds (WorldBackdrop).
public static class WorldBackdropTest
{
    static int failures;

    // Budgets / guards.
    public const long TextureBudgetBytes = 4L * 1024 * 1024;   // per world, desktop (BC) import
    const float SeamTolerance = 0.02f;          // mean |top row - bottom row|, premultiplied RGBA
    const float SkyMaxLuminance = 0.10f;        // opaque far layer
    const float TileMaxLuminance = 0.15f;       // alpha-weighted, screen-filling tile layers
    const float TileMaxChroma = 0.20f;          // alpha-weighted max(rgb) - min(rgb)
    const float AtlasMaxLuminance = 0.62f;      // set-piece art before its (dimming) runtime tint

    static void Check(string what, bool ok)
    {
        Debug.Log((ok ? "[WB] PASS  " : "[WB] FAIL  ") + what);
        if (!ok) failures++;
    }

    public static void Run()
    {
        TestHarness.Exit(Execute());
    }

    static readonly Dictionary<string, string[]> RequiredSprites = new Dictionary<string, string[]>
    {
        { "Space", new[] { "giant_00", "rocky_00", "ringback_00", "ringfront_00", "comet_00", "station_00",
                           "galaxy0", "galaxy1", "wisp0", "wisp1", "moon", "star", "dot", "streak" } },
        { "Frost", new[] { "aurora_00", "peak0", "peak1", "peak2", "geyser_00", "cloud", "dot" } },
        { "Verdant", new[] { "waterfall_00", "ruin_00", "obelisk0", "obelisk1", "dot" } },
        { "Ember", new[] { "eruption_00", "bubble_00", "volcano", "dot" } },
    };

    public static int Execute()
    {
        failures = 0;
        using var sandbox = new TestHarness.Sandbox();
        float savedSpeed = moveBackGround.speed;
        try
        {
            CheckCatalog();
            CheckArt();
            CheckRuntime();
        }
        finally
        {
            moveBackGround.speed = savedSpeed;
            Time.timeScale = 1f;
        }
        Debug.Log("[WB] failures: " + failures);
        return failures;
    }

    // Every world resolves to its own complete set; rates rise far -> near.
    static void CheckCatalog()
    {
        foreach (var theme in WorldManager.Worlds)
        {
            var spec = BackdropCatalog.For(theme.displayName);
            Check(theme.displayName + " has its own backdrop spec", spec.world == theme.displayName);
            Check(theme.displayName + " has 4-10 depth layers (" + spec.layers.Length + ")",
                  spec.layers.Length >= 4 && spec.layers.Length <= 10);

            bool increasing = true;
            for (int i = 1; i < spec.layers.Length; i++)
                if (!(spec.layers[i].rate > spec.layers[i - 1].rate)) increasing = false;
            Check(theme.displayName + " parallax rates strictly increase far -> near", increasing);
            Check(theme.displayName + " far layer is an opaque sky tile",
                  spec.layers[0].kind == BackdropCatalog.Kind.Tile && spec.layers[0].name == "sky");

            string folder = BackdropCatalog.Folder(spec.world);
            foreach (var l in spec.layers)
            {
                if (l.kind == BackdropCatalog.Kind.Pieces) continue;
                Check(spec.world + "/" + l.texture + " tile sprite resolves",
                      Resources.Load<Sprite>(folder + l.texture) != null);
            }
            foreach (string atlas in new[] { BackdropCatalog.AtlasFx, BackdropCatalog.AtlasAnim })
            {
                Check(spec.world + "/" + atlas + " atlas texture resolves", Resources.Load<Texture2D>(folder + atlas) != null);
                Check(spec.world + "/" + atlas + " manifest resolves", Resources.Load<TextAsset>(folder + atlas) != null);
            }

            var fx = new BackdropAtlas(Resources.Load<Texture2D>(folder + "fx"), Resources.Load<TextAsset>(folder + "fx"));
            var anim = new BackdropAtlas(Resources.Load<Texture2D>(folder + "anim"), Resources.Load<TextAsset>(folder + "anim"));
            foreach (string s in RequiredSprites[spec.world])
                Check(spec.world + " sprite " + s + " present", fx.Has(s) || anim.Has(s));
            fx.Destroy();
            anim.Destroy();
        }
    }

    // Pixels: seamless tiles, contrast guard, texture budget.
    static void CheckArt()
    {
        foreach (var spec in BackdropCatalog.All)
        {
            string dir = "Assets/Art/Resources/Worlds/" + spec.world + "/Backdrop/";
            long bytes = 0, astc = 0;
            foreach (string path in Directory.GetFiles(dir, "*.png"))
            {
                string asset = path.Replace('\\', '/');
                var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(asset);
                if (tex == null) { Check(asset + " imports", false); continue; }
                bytes += Profiler.GetRuntimeMemorySizeLong(tex);
                astc += ((tex.width + 5) / 6) * ((tex.height + 5) / 6) * 16L;

                var imp = (TextureImporter)AssetImporter.GetAtPath(asset);
                Check(asset + " has no mipmaps", imp != null && !imp.mipmapEnabled);
                Check(asset + " is compressed", imp != null && imp.textureCompression != TextureImporterCompression.Uncompressed);

                var px = ReadPixels(path);
                string name = Path.GetFileNameWithoutExtension(path);
                bool tile = WorldBackdropImport.IsTile(asset);
                if (tile)
                {
                    Check(asset + " wraps vertically (Repeat)", tex.wrapModeV == TextureWrapMode.Repeat);
                    float seam = SeamDifference(px);
                    Check(spec.world + "/" + name + " is vertically seamless (top vs bottom row " +
                          seam.ToString("F4") + " <= " + SeamTolerance + ")", seam <= SeamTolerance);
                }

                float lum, chroma;
                Measure(px, out lum, out chroma);
                if (name == "sky")
                    Check(spec.world + " sky stays dark (lum " + lum.ToString("F3") + ")", lum <= SkyMaxLuminance);
                if (name == "sky" || name == "far" || name == "mid")
                    Check(spec.world + "/" + name + " under gameplay contrast guard (lum " + lum.ToString("F3") +
                          ", chroma " + chroma.ToString("F3") + ")", lum <= TileMaxLuminance && chroma <= TileMaxChroma);
                if (!tile)
                    Check(spec.world + "/" + name + " atlas art under brightness ceiling (lum " + lum.ToString("F3") + ")",
                          lum <= AtlasMaxLuminance);
            }
            Debug.Log("[WB] " + spec.world + " texture memory: " + (bytes / 1024) + " KB desktop, ~" +
                      (astc / 1024) + " KB ASTC 6x6");
            Check(spec.world + " texture memory " + (bytes / 1024) + " KB <= " + (TextureBudgetBytes / 1024) + " KB",
                  bytes <= TextureBudgetBytes);
        }
    }

    static Color[] ReadPixelsCache;
    static int ReadW, ReadH;

    static Color[] ReadPixels(string path)
    {
        var t = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        t.LoadImage(File.ReadAllBytes(path));
        ReadW = t.width;
        ReadH = t.height;
        var px = t.GetPixels();
        Object.DestroyImmediate(t);
        ReadPixelsCache = px;
        return px;
    }

    static float SeamDifference(Color[] px)
    {
        int w = ReadW, h = ReadH;
        double sum = 0;
        for (int x = 0; x < w; x++)
        {
            Color a = px[x];                    // bottom row
            Color b = px[(h - 1) * w + x];      // top row
            sum += Mathf.Abs(a.r * a.a - b.r * b.a) + Mathf.Abs(a.g * a.a - b.g * b.a) +
                   Mathf.Abs(a.b * a.a - b.b * b.a) + Mathf.Abs(a.a - b.a);
        }
        return (float)(sum / (w * 4));
    }

    static void Measure(Color[] px, out float lum, out float chroma)
    {
        double wsum = 0, l = 0, c = 0;
        for (int i = 0; i < px.Length; i += 3)
        {
            Color p = px[i];
            if (p.a <= 0.01f) continue;
            float mx = Mathf.Max(p.r, Mathf.Max(p.g, p.b));
            float mn = Mathf.Min(p.r, Mathf.Min(p.g, p.b));
            wsum += p.a;
            l += p.a * (0.2126f * p.r + 0.7152f * p.g + 0.0722f * p.b);
            c += p.a * (mx - mn);
        }
        lum = wsum > 0 ? (float)(l / wsum) : 0f;
        chroma = wsum > 0 ? (float)(c / wsum) : 0f;
    }

    // Runtime: freeze, speed integration, bounded pools, portal swap.
    static void CheckRuntime()
    {
        var go = new GameObject("~WorldBackdropTest");
        var wb = go.AddComponent<WorldBackdrop>();
        try
        {
            foreach (var spec in BackdropCatalog.All)
            {
                Time.timeScale = 1f;
                moveBackGround.speed = 0.2f;
                wb.Show(spec.world, false);
                Check(spec.world + " set builds complete", wb.Current != null && wb.Current.Complete &&
                                                          wb.Current.Spec.world == spec.world);
                if (wb.Current == null) continue;
                for (int i = 0; i < 300; i++) wb.Step(1f / 60f);      // let set pieces spawn

                // Freeze: timeScale 0 must leave every renderer exactly where it was.
                var before = Snapshot(go);
                Time.timeScale = 0f;
                for (int i = 0; i < 120; i++) wb.Step(1f / 60f);
                var after = Snapshot(go);
                Check(spec.world + " background is a still frame while timeScale = 0 (" + before.Count + " renderers)",
                      Same(before, after));
                Time.timeScale = 1f;
                for (int i = 0; i < 2; i++) wb.Step(1f / 60f);
                Check(spec.world + " background moves again when time resumes", !Same(after, Snapshot(go)));

                // Speed coupling: the offset integrates speed, so a speed change alters
                // the rate, never the position.
                var mid = wb.Current.Tiles[wb.Current.Tiles.Count > 2 ? 2 : 0];
                const float dt = 1f / 60f;
                moveBackGround.speed = 0.1f;
                wb.Step(dt);
                float o0 = mid.offset;
                moveBackGround.speed = 0.55f;                         // big jump in speed
                wb.Step(dt);
                float step = Mathf.Repeat(mid.offset - o0, mid.TileHeight);
                float expected = (WorldBackdrop.ScrollVelocity(0.55f) * mid.layer.rate + mid.layer.flow) * dt;
                Check(spec.world + " speed change moves the " + mid.layer.name + " layer by one frame of the new speed (" +
                      step.ToString("F5") + " vs " + expected.ToString("F5") + ")", Mathf.Abs(step - expected) < 1e-4f);

                // Long run: pools never grow.
                int transforms = go.GetComponentsInChildren<Transform>(true).Length;
                int capacity = wb.Current.PieceCount;
                bool withinCapacity = true;
                for (int i = 0; i < 20 * 60 * 30; i++)           // 20 minutes at 30 fps
                {
                    moveBackGround.speed = Mathf.Repeat(i * 0.0002f, 0.62f);
                    wb.Step(1f / 30f);
                    if (i % 600 == 0)
                        foreach (var p in wb.Current.Director.Pools)
                            if (p.ActiveCount > p.Capacity) withinCapacity = false;
                }
                Check(spec.world + " pooled set pieces stay bounded over a 20-minute run (" + transforms + " transforms, " +
                      capacity + " pooled)", withinCapacity && wb.Current.PieceCount == capacity &&
                      go.GetComponentsInChildren<Transform>(true).Length == transforms);
                int activeNow = 0;
                foreach (var p in wb.Current.Director.Pools) activeNow += p.ActiveCount;
                Check(spec.world + " still spawning set pieces late in a run (" + activeNow + " active)", activeNow > 0);
            }

            // Portal swap cross-fades, then the old set is torn down.
            Time.timeScale = 1f;
            wb.Show("Space", false);
            wb.Show("Frost", true);
            Check("portal swap starts a cross-fade", wb.Current.Spec.world == "Frost" && wb.Current.Alpha < 0.01f);
            int roots = go.transform.childCount;
            Check("both sets exist mid-fade", roots == 2);
            for (int i = 0; i < 120; i++) wb.Step(1f / 60f);
            Check("cross-fade completes and the old set is removed",
                  Mathf.Approximately(wb.Current.Alpha, 1f) && go.transform.childCount == 1);
            Check("unknown world falls back to Space art", BackdropCatalog.For("Nowhere").world == "Space");
        }
        finally
        {
            Object.DestroyImmediate(go);
        }
    }

    struct State { public Vector3 pos; public Sprite sprite; public Color color; public bool visible; public Vector3 scale; }

    static List<State> Snapshot(GameObject root)
    {
        var list = new List<State>();
        foreach (var sr in root.GetComponentsInChildren<SpriteRenderer>(true))
            list.Add(new State { pos = sr.transform.position, sprite = sr.sprite, color = sr.color,
                                 visible = sr.enabled && sr.gameObject.activeInHierarchy,
                                 scale = sr.transform.lossyScale });
        return list;
    }

    static bool Same(List<State> a, List<State> b)
    {
        if (a.Count != b.Count) return false;
        for (int i = 0; i < a.Count; i++)
        {
            if (a[i].visible != b[i].visible || a[i].sprite != b[i].sprite) return false;
            if (!a[i].visible) continue;
            if ((a[i].pos - b[i].pos).sqrMagnitude > 1e-12f) return false;
            if ((a[i].scale - b[i].scale).sqrMagnitude > 1e-12f) return false;
            if (a[i].color != b[i].color) return false;
        }
        return true;
    }
}
