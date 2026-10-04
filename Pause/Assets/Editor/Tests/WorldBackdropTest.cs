using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

// Headless checks of the animated world backgrounds (WorldBackdrop).
public static class WorldBackdropTest
{
    static int failures;

    // Budgets / guards.
    public const long TextureBudgetBytes = 4L * 1024 * 1024;   // per world, GPU size of the imported format
    const float SeamTolerance = 0.02f;          // mean |top row - bottom row|, premultiplied RGBA
    // The guide's sky ramps (docs/art-style.md 1.3) peak at ~#123248 / #143430,
    // so the opaque sky averages up to ~0.12 relative luminance.
    const float SkyMaxLuminance = 0.13f;        // opaque far layer
    const float TileMaxLuminance = 0.17f;       // alpha-weighted, screen-filling tile layers
    const float TileMaxChroma = 0.20f;          // alpha-weighted max(rgb) - min(rgb)
    // docs/art-style.md 4.1: backdrop forms at HSV value <= 35%; point lights
    // (windows, dashes, sparks) are tiny and excepted, so the 90th percentile
    // of value is checked. Saturation is gated as chroma (above): the guide's
    // own night ramps are >60% HSV saturation at <15% value, where HSV S is
    // not a meaningful "colourfulness".
    const float TileMaxValueP90 = 0.35f;
    const float AtlasMaxLuminance = 0.66f;      // set-piece art (lights included) before its dimming runtime tint

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
        { "Space", new[] { "giant_00", "giant_11", "rocky_00", "rocky_03", "station_00", "station_03",
                           "ringstation_00", "ringstation_03", "mini_station_00", "mini_ringstation_00",
                           "mini_rocky_00", "comet_00", "comet_01", "galaxy0", "galaxy1", "wisp0", "wisp1",
                           "moon", "star", "dot", "streak" } },
        { "Frost", new[] { "aurora_00", "glacier_00", "massif0", "massif1", "geyser_00",
                           "cloud0", "cloud1", "haze", "dot" } },
        { "Verdant", new[] { "waterfall_00", "ruin_00", "obelisk0", "obelisk1", "cloud0", "cloud1", "haze", "dot",
                             "firefly_00", "spore_00" } },
        { "Ember", new[] { "volcano_00", "burst_00", "cloud0", "cloud1", "haze", "dot" } },
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
            CheckSpaceAtlas();
            CheckSpaceTiers();
            CheckVerdantPalette();
            CheckReadability();
            CheckWalls();
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
            if (spec.world != "Space") CheckDepthModel(spec);

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

    // The ship flies at atmosphere level: ground and landmarks are far below
    // on slow parallax, only atmospheric layers (air, clouds, particles) may
    // move fast, and at least one cloud/haze layer sits between ground and ship.
    static void CheckDepthModel(BackdropCatalog.Spec spec)
    {
        float maxGround = 0f;
        bool groundFar = true, nearAtmospheric = true;
        foreach (var l in spec.layers)
        {
            bool ground = l.role == BackdropCatalog.Role.Ground || l.role == BackdropCatalog.Role.Landmark;
            if (ground)
            {
                maxGround = Mathf.Max(maxGround, l.rate);
                if (l.rate > BackdropCatalog.MaxGroundRate) groundFar = false;
            }
            else if (l.rate > BackdropCatalog.MaxGroundRate &&
                     l.role != BackdropCatalog.Role.Atmosphere && l.role != BackdropCatalog.Role.Cloud)
                nearAtmospheric = false;
        }
        Check(spec.world + " ground and landmark layers use far parallax (<= " + BackdropCatalog.MaxGroundRate + ")",
              groundFar);
        Check(spec.world + " near layers are atmospheric only", nearAtmospheric);
        bool cloudBetween = false;
        foreach (var l in spec.layers)
            if (l.role == BackdropCatalog.Role.Cloud && l.rate > maxGround && l.rate < 1f) cloudBetween = true;
        Check(spec.world + " has a cloud/haze layer between the ground and the ship", cloudBetween);
        bool hasLandmark = false;
        foreach (var l in spec.layers) if (l.role == BackdropCatalog.Role.Landmark) hasLandmark = true;
        Check(spec.world + " has landmark set pieces", hasLandmark);
    }

    static float largestLandmark;

    static bool LandmarksSmall(WorldBackdrop wb)
    {
        var pd = wb.Current.Director as PlanetDirector;
        if (pd == null) return true;
        bool ok = true;
        foreach (var pool in pd.Landmarks)
            foreach (var p in pool.items)
            {
                if (!p.active) continue;
                Vector3 size = p.sr.bounds.size;
                float m = Mathf.Max(size.x, size.y);
                largestLandmark = Mathf.Max(largestLandmark, m);
                if (m > BackdropCatalog.MaxLandmarkSize * 1.15f) ok = false;   // height may exceed width a little
                if (p.rate > BackdropCatalog.MaxGroundRate) ok = false;
            }
        return ok;
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
                // GPU size of the imported format (Profiler's editor number also
                // counts transient CPU copies, so it varies between sessions).
                bytes += (long)UnityEngine.Experimental.Rendering.GraphicsFormatUtility.ComputeMipmapSize(
                    tex.width, tex.height, tex.graphicsFormat);
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
                if (name == "sky" || name == "far" || name == "mid")
                {
                    float v90 = ValuePercentile(px, 0.9f);
                    Check(spec.world + "/" + name + " forms at HSV value <= 35% (p90 " + v90.ToString("F3") + ")",
                          v90 <= TileMaxValueP90);
                }
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

    // ---------------------------------------------------- composited look --

    // The ground layers stacked the way the game draws them at rest: the
    // opaque sky, then far and mid (alpha over), then the river strip centred.
    static Color[] Composite(string world, out int w, out int h)
    {
        string dir = "Assets/Art/Resources/Worlds/" + world + "/Backdrop/";
        var outPx = (Color[])ReadPixels(dir + "sky.png").Clone();
        w = ReadW; h = ReadH;
        foreach (string layer in new[] { "far", "mid", "flow" })
        {
            string path = dir + layer + ".png";
            if (!File.Exists(path)) continue;
            var px = ReadPixels(path);
            int lw = ReadW, lh = ReadH;
            if (lh != h || lw > w) continue;
            int x0 = (w - lw) / 2;
            for (int y = 0; y < h; y++)
                for (int x = 0; x < lw; x++)
                {
                    Color s = px[y * lw + x];
                    if (s.a <= 0f) continue;
                    int i = y * w + x0 + x;
                    outPx[i] = Color.Lerp(outPx[i], new Color(s.r, s.g, s.b, 1f), s.a);
                }
        }
        return outPx;
    }

    static float Chroma(Color c) { return Mathf.Max(c.r, Mathf.Max(c.g, c.b)) - Mathf.Min(c.r, Mathf.Min(c.g, c.b)); }
    static float Value(Color c) { return Mathf.Max(c.r, Mathf.Max(c.g, c.b)); }

    // docs/art-style.md 4.2 (WCAG relative luminance of sRGB colours).
    static float Linear(float v) { return v <= 0.03928f ? v / 12.92f : Mathf.Pow((v + 0.055f) / 1.055f, 2.4f); }
    static float RelLum(Color c) { return 0.2126f * Linear(c.r) + 0.7152f * Linear(c.g) + 0.0722f * Linear(c.b); }
    static float Contrast(float a, float b) { return (Mathf.Max(a, b) + 0.05f) / (Mathf.Min(a, b) + 0.05f); }

    // Hue families used by the diversity check.
    const int FamGreen = 0, FamTeal = 1, FamIndigo = 2, FamWarm = 3, FamOther = 4, FamGrey = 5;

    static int Family(float hue, Color c)
    {
        if (Chroma(c) <= 0.03f) return FamGrey;
        if (hue >= 90f && hue < 175f) return FamGreen;
        if (hue >= 175f && hue < 200f) return FamTeal;
        if (hue >= 200f && hue < 265f) return FamIndigo;
        if (hue < 60f || hue >= 330f) return FamWarm;
        return FamOther;
    }

    static bool IsTeal(float hue, float s, float v) { return hue >= 165f && hue < 200f && s >= 0.5f && v >= 0.55f; }
    static bool IsSodium(float hue, float s, float v) { return hue >= 15f && hue < 45f && s >= 0.6f && v >= 0.75f; }

    // Verdant must read as an 80s anime night forest, not monochrome mud:
    // several hue families (indigo night + greens), several distinct
    // hue/value clusters, a real value range, and the teal neon / sodium
    // lantern accents. Before the redo master had 85% of the screen in one
    // green family, 5 clusters, a 0.17 value range, ~0.06% teal, no sodium.
    public const int VerdantMinClusters = 7;            // 30-degree hue x 0.1 value bins with >= 0.5% coverage
    public const float VerdantMinValueRange = 0.20f;    // p95 - p5 of HSV value
    public const float VerdantMaxFamilyShare = 0.80f;   // no single hue family may own the picture
    public const float VerdantMinFamilyShare = 0.10f;   // at least two families this big
    public const float VerdantMinTeal = 0.0015f;        // teal / cyan neon pixels (fraction of the screen)
    public const float VerdantMinSodium = 0.0003f;      // sodium / amber lantern pixels

    static void CheckVerdantPalette()
    {
        int w, h;
        var px = Composite("Verdant", out w, out h);
        var bins = new Dictionary<int, int>();
        var fam = new int[6];
        var values = new List<float>();
        int n = 0, teal = 0, sodium = 0;
        for (int i = 0; i < px.Length; i += 2)
        {
            Color c = px[i];
            float hh, ss, vv;
            Color.RGBToHSV(c, out hh, out ss, out vv);
            float hue = hh * 360f;
            int band = Mathf.Min((int)(vv / 0.1f), 5);
            int key = Chroma(c) > 0.03f ? ((int)(hue / 30f) % 12) * 6 + band : 100 + band;
            int k;
            bins.TryGetValue(key, out k);
            bins[key] = k + 1;
            fam[Family(hue, c)]++;
            values.Add(vv);
            if (IsTeal(hue, ss, vv)) teal++;
            if (IsSodium(hue, ss, vv)) sodium++;
            n++;
        }
        int clusters = 0;
        foreach (var kv in bins) if (kv.Value >= 0.005f * n) clusters++;
        values.Sort();
        float range = values[(int)(n * 0.95f)] - values[(int)(n * 0.05f)];
        float maxShare = 0f;
        int big = 0;
        for (int f = 0; f < FamGrey; f++)
        {
            float share = fam[f] / (float)n;
            maxShare = Mathf.Max(maxShare, share);
            if (share >= VerdantMinFamilyShare) big++;
        }
        Check("Verdant has >= " + VerdantMinClusters + " distinct hue/value clusters (" + clusters + ")",
              clusters >= VerdantMinClusters);
        Check("Verdant value range p5..p95 >= " + VerdantMinValueRange + " (" + range.ToString("F3") + ")",
              range >= VerdantMinValueRange);
        Check("Verdant is not one hue family (largest " + maxShare.ToString("F2") + " <= " + VerdantMaxFamilyShare +
              ", " + big + " families >= " + VerdantMinFamilyShare + ")", maxShare <= VerdantMaxFamilyShare && big >= 2);
        Check("Verdant greens and indigo night both present (green " + (fam[FamGreen] / (float)n).ToString("F2") +
              ", indigo " + (fam[FamIndigo] / (float)n).ToString("F2") + ")",
              fam[FamGreen] >= 0.08f * n && fam[FamIndigo] >= 0.2f * n);
        Check("Verdant teal neon present (" + (teal / (float)n).ToString("F4") + " >= " + VerdantMinTeal + ")",
              teal >= VerdantMinTeal * n);
        Check("Verdant sodium accents present (" + (sodium / (float)n).ToString("F4") + " >= " + VerdantMinSodium + ")",
              sodium >= VerdantMinSodium * n);

        // The landmarks and particles carry the neon and lantern lights too.
        foreach (string atlas in new[] { "anim", "fx" })
        {
            int t = 0, so = 0;
            foreach (var c in ReadPixels("Assets/Art/Resources/Worlds/Verdant/Backdrop/" + atlas + ".png"))
            {
                if (c.a < 0.9f) continue;
                float hh, ss, vv;
                Color.RGBToHSV(c, out hh, out ss, out vv);
                if (IsTeal(hh * 360f, ss, vv)) t++;
                if (IsSodium(hh * 360f, ss, vv)) so++;
            }
            Check("Verdant " + atlas + " atlas carries teal (" + t + " px) and sodium (" + so + " px) lights",
                  t >= 200 && so >= 50);
        }
    }

    // docs/art-style.md 4, with each world's enemies on top: the composited
    // lane stays darker and greyer than the enemy bodies, bodies reach 2.5:1
    // and the brightest tone 7:1 against the lane (its median luminance).
    static void CheckReadability()
    {
        for (int wi = 0; wi < WorldManager.Worlds.Length; wi++)
        {
            string world = BackdropCatalog.For(WorldManager.Worlds[wi].displayName).world;
            var theme = EnemyPalette.ThemeFor(wi);
            int w, h;
            var px = Composite(world, out w, out h);
            var lum = new List<float>();
            var val = new List<float>();
            double chroma = 0;
            int x0 = (int)(w * 0.2f), x1 = (int)(w * 0.8f);
            for (int y = 0; y < h; y += 2)
                for (int x = x0; x < x1; x += 2)
                {
                    Color c = px[y * w + x];
                    lum.Add(RelLum(c));
                    val.Add(Value(c));
                    chroma += Chroma(c);
                }
            lum.Sort();
            val.Sort();
            float laneLum = lum[lum.Count / 2];
            float laneV90 = val[(int)(val.Count * 0.9f)];
            float laneChroma = (float)(chroma / lum.Count);
            float body = Contrast(RelLum(theme.hull), laneLum);
            float bright = Mathf.Max(RelLum(theme.light), Mathf.Max(RelLum(theme.hullHighlight), RelLum(theme.bone)));
            float kick = Contrast(bright, laneLum);
            Check(world + " lane with enemies on top: body " + body.ToString("F2") + ":1 >= 2.5, brightest " +
                  kick.ToString("F2") + ":1 >= 7", body >= 2.5f && kick >= 7f);
            Check(world + " lane stays darker than enemy bodies (lane value p90 " + laneV90.ToString("F2") +
                  " <= " + TileMaxValueP90 + " and < hull " + Value(theme.hull).ToString("F2") + ")",
                  laneV90 <= TileMaxValueP90 && laneV90 < Value(theme.hull));
            // Ember's enemies are deliberately grey char on a warm sky, so
            // there only the guide's chroma ceiling applies; the green worlds
            // must also stay greyer than their (green) enemies.
            bool greyer = laneChroma <= TileMaxChroma && (world != "Verdant" || laneChroma < Chroma(theme.hull));
            Check(world + " lane chroma " + laneChroma.ToString("F2") + " <= " + TileMaxChroma +
                  (world == "Verdant" ? " and < enemy hull " + Chroma(theme.hull).ToString("F2") : ""), greyer);
        }
    }

    // ------------------------------------------------------------- walls --

    public const int WallWidth = 64, WallHeight = 448;
    const int WallMaxColours = 32;              // flat cels: a handful of tones plus stepped light halos
    const float WallMinMajorCover = 0.95f;      // colours with >= 0.5% coverage must cover the wall
    const float WallMaxSoftPairs = 0.01f;       // neighbours 1..6 levels apart = gradient banding

    // Space keeps the scene's own wall textures (left_1 / right_6 / right_7
    // materials); the planets' come from Resources via WorldPainter.
    public static string[] WallPaths(int world)
    {
        if (string.IsNullOrEmpty(WorldManager.Worlds[world].resourceFolder))
            return new[] { "Assets/Art/left.png", "Assets/Art/right.png" };
        string f = "Assets/Art/Resources/Worlds/" + WorldManager.Worlds[world].resourceFolder + "/";
        return new[] { f + "wallLeft.png", f + "wallRight.png" };
    }

    static void CheckWalls()
    {
        for (int wi = 0; wi < WorldManager.Worlds.Length; wi++)
        {
            string world = WorldManager.Worlds[wi].displayName;
            var paths = WallPaths(wi);
            Color32[] left = null;
            for (int side = 0; side < 2; side++)
            {
                string path = paths[side];
                if (!File.Exists(path)) { Check(path + " exists", false); continue; }
                var t = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                t.LoadImage(File.ReadAllBytes(path));
                int w = t.width, h = t.height;
                var px = t.GetPixels32();
                Object.DestroyImmediate(t);
                string tag = world + " " + (side == 0 ? "left" : "right") + " wall";
                Check(tag + " is " + WallWidth + "x" + WallHeight + " (" + w + "x" + h + ")",
                      w == WallWidth && h == WallHeight);
                if (w != WallWidth || h != WallHeight) continue;

                var counts = new Dictionary<int, int>();
                bool opaque = true;
                foreach (var c in px)
                {
                    if (c.a < 255) opaque = false;
                    int key = (c.r << 24) | (c.g << 16) | (c.b << 8) | c.a;
                    int k;
                    counts.TryGetValue(key, out k);
                    counts[key] = k + 1;
                }
                int major = 0;
                foreach (var kv in counts) if (kv.Value >= 0.005f * px.Length) major += kv.Value;

                int soft = 0, pairs = 0;
                float seam = 0f;
                for (int y = 0; y < h; y++)
                {
                    int yn = (y + 1) % h;                  // the last row wraps to the first: the seam
                    float rowDiff = 0f;
                    for (int x = 0; x < w; x++)
                    {
                        Color32 a = px[y * w + x];
                        Color32 down = px[yn * w + x];
                        Color32 right = x + 1 < w ? px[y * w + x + 1] : a;
                        int d1 = MaxDiff(a, down), d2 = MaxDiff(a, right);
                        pairs += 2;
                        if (d1 > 0 && d1 <= 6) soft++;
                        if (d2 > 0 && d2 <= 6) soft++;
                        rowDiff += (Mathf.Abs(a.r - down.r) + Mathf.Abs(a.g - down.g) + Mathf.Abs(a.b - down.b)) / (3f * 255f);
                    }
                    rowDiff /= w;
                    if (yn == 0) seam = rowDiff;
                }
                Check(tag + " is opaque", opaque);
                Check(tag + " uses flat palette colours (" + counts.Count + " unique <= " + WallMaxColours +
                      ", major tones cover " + (major / (float)px.Length).ToString("F3") + " >= " + WallMinMajorCover + ")",
                      counts.Count <= WallMaxColours && major >= WallMinMajorCover * px.Length);
                Check(tag + " has no soft gradients (" + (soft / (float)pairs).ToString("F4") + " banding pairs <= " +
                      WallMaxSoftPairs + ")", soft <= WallMaxSoftPairs * pairs);
                // Seamless: crisp art has hard panel lines, so 'top row == bottom
                // row' is the wrong test. Instead the wrap (last row -> first row)
                // must be a transition the tile already contains -- the motifs
                // repeat with a period that divides the height -- or be smooth.
                bool repeats = false;
                for (int y = 0; y + 1 < h && !repeats; y++)
                {
                    bool same = true;
                    for (int x = 0; x < w && same; x++)
                        same = MaxDiff(px[y * w + x], px[(h - 1) * w + x]) == 0 &&
                               MaxDiff(px[(y + 1) * w + x], px[x]) == 0;
                    repeats = same;
                }
                Check(tag + " is vertically seamless (wrap " + seam.ToString("F3") + ", repeats an inner row pair: " +
                      repeats + ")", repeats || seam <= SeamTolerance);
                if (side == 0) left = px;
                else if (left != null)
                {
                    bool mirror = true;
                    for (int y = 0; y < h && mirror; y++)
                        for (int x = 0; x < w; x++)
                            if (MaxDiff(left[y * w + x], px[y * w + (w - 1 - x)]) != 0) { mirror = false; break; }
                    Check(world + " right wall mirrors the left (both inner edges face the playfield)", mirror);
                }
            }
        }
    }

    // -------------------------------------------------------------- space --

    [System.Serializable] class AtlasRect { public string n; public int x, y, w, h; }
    [System.Serializable] class AtlasManifest { public AtlasRect[] sprites; }

    // Space's atlas cells are variants, cut so each sprite pivots on its art:
    // a variant never hops and a rotation turns in place. Planets, stations
    // and the rest are centred on their bounding box, galaxies on their
    // bright core and wisps on their mass (those two spin).
    static void CheckSpaceAtlas()
    {
        string dir = "Assets/Art/Resources/Worlds/Space/Backdrop/";
        var rects = new Dictionary<string, string>();
        foreach (string atlas in new[] { "anim", "fx" })
        {
            var px = ReadPixels(dir + atlas + ".png");
            int w = ReadW, h = ReadH;
            var m = JsonUtility.FromJson<AtlasManifest>(File.ReadAllText(dir + atlas + ".json"));
            float worst = 0f;
            string worstName = "";
            bool inside = true, unique = true;
            foreach (var r in m.sprites)
            {
                if (r.x < 0 || r.y < 0 || r.x + r.w > w || r.y + r.h > h) { inside = false; continue; }
                string key = atlas + ":" + r.x + "," + r.y + "," + r.w + "," + r.h;
                if (rects.ContainsKey(key)) unique = false;
                rects[key] = r.n;

                int x0 = int.MaxValue, x1 = -1, y0 = int.MaxValue, y1 = -1;
                double mass = 0, mx = 0, my = 0;
                var lum = new List<float>();
                for (int y = 0; y < r.h; y++)
                    for (int x = 0; x < r.w; x++)
                    {
                        Color c = px[(r.y + y) * w + r.x + x];
                        if (c.a <= 0f) continue;
                        x0 = Mathf.Min(x0, x); x1 = Mathf.Max(x1, x);
                        y0 = Mathf.Min(y0, y); y1 = Mathf.Max(y1, y);
                        mass += c.a; mx += c.a * (x + 0.5); my += c.a * (y + 0.5);
                        lum.Add((c.r + c.g + c.b) * c.a);
                    }
                if (x1 < 0) { Check("Space " + r.n + " has art in its rect", false); continue; }
                Vector2 centre = new Vector2((x0 + x1 + 1) * 0.5f, (y0 + y1 + 1) * 0.5f);
                if (r.n.StartsWith("wisp")) centre = new Vector2((float)(mx / mass), (float)(my / mass));
                else if (r.n.StartsWith("galaxy"))
                {
                    lum.Sort();
                    float cut = lum[Mathf.Max(0, lum.Count - 400)];
                    double n = 0, cx = 0, cy = 0;
                    for (int y = 0; y < r.h; y++)
                        for (int x = 0; x < r.w; x++)
                        {
                            Color c = px[(r.y + y) * w + r.x + x];
                            if (c.a <= 0f || (c.r + c.g + c.b) * c.a < cut) continue;
                            n++; cx += x + 0.5; cy += y + 0.5;
                        }
                    centre = new Vector2((float)(cx / n), (float)(cy / n));
                }
                float off = Mathf.Max(Mathf.Abs(centre.x - r.w * 0.5f), Mathf.Abs(centre.y - r.h * 0.5f));
                if (off > worst) { worst = off; worstName = r.n; }

                // Stars are pinpoints and streaks thin lines, not 256 px cells.
                if (r.n == "star" || r.n == "dot")
                    Check("Space " + r.n + " is a pinpoint sprite (" + r.w + "x" + r.h + " px)", r.w <= 32 && r.h <= 32);
                if (r.n == "streak")
                    Check("Space streak is a thin line (" + r.w + "x" + r.h + " px)", r.h <= 12 && r.w >= 4 * r.h);
            }
            Check("Space/" + atlas + " sprite rects lie inside the texture", inside);
            Check("Space/" + atlas + " has no two names on one rect (cells are variants, not padded flipbooks)", unique);
            Check("Space/" + atlas + " art is centred in every sprite rect (worst " + worst.ToString("F1") + " px, " +
                  worstName + ")", worst <= 2f);
        }
    }

    // The depth tiers themselves: farther = smaller, slower, dimmer, hazier
    // and sorted behind; most planets far away, near ones rare.
    static void CheckSpaceTiers()
    {
        var spec = BackdropCatalog.For("Space");
        var tiers = SpaceDirector.Tiers;
        bool mono = true;
        int total = 0;
        for (int i = 0; i < tiers.Length; i++)
        {
            total += tiers[i].weight;
            if (i == 0) continue;
            var a = tiers[i - 1];
            var b = tiers[i];
            if (!(a.scale < b.scale && a.light < b.light && a.clarity <= b.clarity &&
                  spec.Rate(a.layer) < spec.Rate(b.layer) && spec.Order(a.layer) < spec.Order(b.layer))) mono = false;
        }
        Check("Space depth tiers grow, speed up and brighten strictly far -> near", mono);
        float farShare = (tiers[0].weight + tiers[1].weight) / (float)total;
        float nearShare = tiers[tiers.Length - 1].weight / (float)total;
        Check("Space planets are mostly far away (two farthest tiers " + farShare.ToString("F2") +
              " >= 0.7, nearest " + nearShare.ToString("F2") + " <= 0.08)", farShare >= 0.7f && nearShare <= 0.08f);
        Check("Space comets pass behind every body", spec.Order("comets") < spec.Order(tiers[0].layer));
        float nearRate = spec.Rate(tiers[tiers.Length - 1].layer);
        Check("Space bodies stay far behind the ship's own depth (nearest tier rate " + nearRate + " <= 0.15)",
              nearRate <= 0.15f);
    }

    // Watched over the long run: what each set piece looked like at spawn.
    class Seen { public Sprite sprite; public float age; }
    static readonly Dictionary<BackdropPiece, Seen> spaceSeen = new Dictionary<BackdropPiece, Seen>();
    static readonly List<BackdropPiece> spaceActive = new List<BackdropPiece>();
    const int SpaceKinds = 4;
    static float[,] sizeMin, sizeMax, valueMin, valueMax;
    static int[] planetsPerTier;
    static int spriteSwaps, overlaps, tierMismatches, bodiesSeen, maxGroupsInView;

    static void SpaceWatchReset()
    {
        int t = SpaceDirector.Tiers.Length;
        spaceSeen.Clear();
        sizeMin = new float[SpaceKinds, t]; sizeMax = new float[SpaceKinds, t];
        valueMin = new float[SpaceKinds, t]; valueMax = new float[SpaceKinds, t];
        for (int k = 0; k < SpaceKinds; k++)
            for (int i = 0; i < t; i++)
            {
                sizeMin[k, i] = valueMin[k, i] = float.MaxValue;
                sizeMax[k, i] = valueMax[k, i] = -1f;
            }
        planetsPerTier = new int[t];
        spriteSwaps = overlaps = tierMismatches = bodiesSeen = maxGroupsInView = 0;
    }

    static void SpaceWatch(SpaceDirector d, BackdropSet set, bool checkOverlap)
    {
        // A set piece keeps the variant it spawned with for its whole life
        // (its age restarting marks a new life in a recycled pool slot).
        foreach (var pool in d.SetPieces)
            foreach (var p in pool.items)
            {
                if (!p.active) { spaceSeen.Remove(p); continue; }
                Seen s;
                if (!spaceSeen.TryGetValue(p, out s) || p.age < s.age)
                {
                    spaceSeen[p] = new Seen { sprite = p.sr.sprite, age = p.age };
                    if (d.Bodies.Contains(pool)) NoteBody(p, set.Spec);
                }
                else
                {
                    if (p.sr.sprite != s.sprite) spriteSwaps++;
                    s.age = p.age;
                }
            }
        if (!checkOverlap) return;

        // No two bodies overlap, unless one is the other's own moon / station.
        spaceActive.Clear();
        foreach (var pool in d.Bodies)
            foreach (var p in pool.items) if (p.active) spaceActive.Add(p);
        int groups = 0;
        for (int i = 0; i < spaceActive.Count; i++)
        {
            var a = spaceActive[i];
            if (a.parent == null && Mathf.Abs(a.y) < set.HalfHeight) groups++;
            Bounds ba = a.sr.bounds;
            for (int j = i + 1; j < spaceActive.Count; j++)
            {
                var b = spaceActive[j];
                if (SpaceDirector.Group(a) == SpaceDirector.Group(b)) continue;
                Bounds bb = b.sr.bounds;
                if (ba.min.x < bb.max.x && bb.min.x < ba.max.x && ba.min.y < bb.max.y && bb.min.y < ba.max.y) overlaps++;
            }
        }
        maxGroupsInView = Mathf.Max(maxGroupsInView, groups);
    }

    static void NoteBody(BackdropPiece p, BackdropCatalog.Spec spec)
    {
        bodiesSeen++;
        var tier = SpaceDirector.Tiers[p.tier];
        if (p.kind == SpaceDirector.Planet) planetsPerTier[p.tier]++;
        sizeMin[p.kind, p.tier] = Mathf.Min(sizeMin[p.kind, p.tier], p.size);
        sizeMax[p.kind, p.tier] = Mathf.Max(sizeMax[p.kind, p.tier], p.size);
        float v = Value(p.color);
        valueMin[p.kind, p.tier] = Mathf.Min(valueMin[p.kind, p.tier], v);
        valueMax[p.kind, p.tier] = Mathf.Max(valueMax[p.kind, p.tier], v);
        // Rate and sorting come from the tier; a companion shares its planet's.
        if (!Mathf.Approximately(p.rate, spec.Rate(tier.layer))) tierMismatches++;
        if (Mathf.Abs(p.sr.sortingOrder - spec.Order(tier.layer)) > 4) tierMismatches++;
        if (p.parent != null && (p.parent.tier != p.tier || p.parent.rate != p.rate || p.size >= p.parent.size))
            tierMismatches++;
    }

    static void SpaceWatchReport()
    {
        int tiers = SpaceDirector.Tiers.Length;
        Check("Space set pieces keep the sprite they spawned with (" + spriteSwaps + " swaps)", spriteSwaps == 0);
        Check("Space bodies never overlap over a 20-minute run (" + overlaps + " overlapping samples, at most " +
              maxGroupsInView + " in view at once)", overlaps == 0 && maxGroupsInView <= 4);
        Check("Space bodies take rate and sorting from their depth tier (" + tierMismatches + " mismatches in " +
              bodiesSeen + " bodies)", tierMismatches == 0 && bodiesSeen >= 40);

        // Within a kind, every body of a farther tier is smaller and dimmer
        // than every body of a nearer one.
        bool mono = true;
        string seen = "";
        for (int k = 0; k < SpaceKinds; k++)
        {
            int last = -1, count = 0;
            for (int t = 0; t < tiers; t++)
            {
                if (sizeMax[k, t] < 0f) continue;
                count++;
                if (last >= 0 && !(sizeMax[k, last] < sizeMin[k, t] && valueMax[k, last] < valueMin[k, t])) mono = false;
                last = t;
            }
            seen += (k > 0 ? "/" : "") + count;
        }
        Check("Space farther tier => smaller and dimmer, per kind (tiers seen: planet/station/planetoid/moon " +
              seen + ")", mono);

        int planets = 0;
        foreach (int n in planetsPerTier) planets += n;
        int far = planetsPerTier[0] + planetsPerTier[1], near = planetsPerTier[tiers - 1];
        Check("Space planets in the run are mostly far (" + far + " of " + planets + " in the two farthest tiers, " +
              near + " near)", planets >= 20 && far >= 0.65f * planets && near <= 0.12f * planets);
    }

    static int MaxDiff(Color32 a, Color32 b)
    {
        return Mathf.Max(Mathf.Abs(a.r - b.r), Mathf.Max(Mathf.Abs(a.g - b.g), Mathf.Max(Mathf.Abs(a.b - b.b), Mathf.Abs(a.a - b.a))));
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

    static float ValuePercentile(Color[] px, float q)
    {
        var hist = new int[256];
        int n = 0;
        for (int i = 0; i < px.Length; i += 3)
        {
            Color p = px[i];
            if (p.a <= 0.5f) continue;
            hist[Mathf.Clamp((int)(Mathf.Max(p.r, Mathf.Max(p.g, p.b)) * 255f + 0.5f), 0, 255)]++;
            n++;
        }
        int target = (int)(n * q), acc = 0;
        for (int v = 0; v < 256; v++)
        {
            acc += hist[v];
            if (acc >= target) return v / 255f;
        }
        return 1f;
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
                bool landmarksSmall = true;
                largestLandmark = 0f;
                var space = wb.Current.Director as SpaceDirector;
                if (space != null) SpaceWatchReset();
                for (int i = 0; i < 20 * 60 * 30; i++)           // 20 minutes at 30 fps
                {
                    moveBackGround.speed = Mathf.Repeat(i * 0.0002f, 0.62f);
                    wb.Step(1f / 30f);
                    if (i % 600 == 0)
                        foreach (var p in wb.Current.Director.Pools)
                            if (p.ActiveCount > p.Capacity) withinCapacity = false;
                    if (i % 30 == 0 && !LandmarksSmall(wb)) landmarksSmall = false;
                    if (space != null) SpaceWatch(space, wb.Current, i % 3 == 0);
                }
                if (space != null) SpaceWatchReport();
                Check(spec.world + " pooled set pieces stay bounded over a 20-minute run (" + transforms + " transforms, " +
                      capacity + " pooled)", withinCapacity && wb.Current.PieceCount == capacity &&
                      go.GetComponentsInChildren<Transform>(true).Length == transforms);
                if (spec.world != "Space")
                    Check(spec.world + " landmarks stay small and far over a long run (largest " +
                          largestLandmark.ToString("F2") + " u, limit " + BackdropCatalog.MaxLandmarkSize + ")",
                          landmarksSmall && largestLandmark > 0f);
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
