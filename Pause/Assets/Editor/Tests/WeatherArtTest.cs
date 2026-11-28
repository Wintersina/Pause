using System.IO;
using UnityEngine;

// THE STAGED WEATHER / HAZARD / SUN ART IS REAL RESOLUTION (docs/world-weather-hazards.md section 12; the user: "everything
// very high rez"). The art lives staged in Art/Weather~/<World>/ (not imported until the user vets it), so the files are read
// straight from disk. Per sheet: the exact size, every cell filled, and the PIXEL-DUPLICATION test of BackdropResolutionTest
// (a cell is a k x k block upscale when >= 97% of its opaque texels equal the first texel of their k x k block, k = 2, 3, 4, at
// the aligned phase or its diagonal neighbours): none may be. When the art is wired (Resources/Weather), the import settings
// must not clamp it below its size.
//
//   Unity -batchmode -quit -projectPath <abs>/Pause -executeMethod WeatherArtTest.Run
public static class WeatherArtTest
{
    static int fails;
    static void Check(string what, bool ok, string extra = "")
    {
        Debug.Log((ok ? "[WART] PASS  " : "[WART] FAIL  ") + what + (extra.Length > 0 ? "  [" + extra + "]" : ""));
        if (!ok) fails++;
    }

    public static void Run() { TestHarness.Exit(Execute()); }

    const float BlockFraction = .97f;
    const int MinOpaque = 400;
    static string Root => Application.dataPath + "/Art/Weather~/";

    public static int Execute()
    {
        fails = 0;
        // EVERY world's weather is approved and wired (Backdrop3/wx_* or Space's Backdrop/wx_*): full resolution, no block upscaling (the gate lane checks
        // every third world's cells plus Storm and Tide; the full lane every world), import not clamped, no mip maps, ASTC 4x4 on phones.
        for (int w = 0; w < WorldWeatherTable.Entries.Length; w++)
        {
            string name = WorldWeatherTable.Entries[w].world;
            string rel = BackdropCatalog.Folder(name);
            string wd = Application.dataPath + "/Art/Backgrounds/Resources/" + rel;
            if (TestHarness.SweepWorld(w) || name == "Storm" || name == "Tide")
            {
                Sheet(wd + "wx_hazard.png", 3072, 1536, 512, 6, 2, name);
                Sheet(wd + "wx_hazard.png", 3072, 1536, 512, 4, 1, name, 2);
                Sheet(wd + "wx_ambient.png", 2048, 512, 256, 8, 2, name);
            }
            else
            {
                foreach (var n3 in new[] { "wx_hazard", "wx_ambient" })
                {
                    var t3 = Resources.Load<Texture2D>(rel + n3);
                    Check(name + "/" + n3 + ": installed at full size", t3 != null && t3.width == (n3 == "wx_hazard" ? 3072 : 2048) && t3.height == (n3 == "wx_hazard" ? 1536 : 512));
                }
            }
            foreach (var n2 in new[] { "wx_hazard", "wx_ambient" })
            {
                string asset = "Assets/Art/Backgrounds/Resources/" + rel + n2 + ".png";
                var imp2 = UnityEditor.AssetImporter.GetAtPath(asset) as UnityEditor.TextureImporter;
                var t2 = UnityEditor.AssetDatabase.LoadAssetAtPath<Texture2D>(asset);
                Check(name + "/" + n2 + ": import is not clamped below its size, no mip maps", imp2 != null && t2 != null && imp2.maxTextureSize >= Mathf.Max(t2.width, t2.height) && !imp2.mipmapEnabled);
                var mob2 = imp2 != null ? imp2.GetPlatformTextureSettings("Android") : default(UnityEditor.TextureImporterPlatformSettings);
                Check(name + "/" + n2 + ": ASTC 4x4 on phones", imp2 != null && mob2.overridden && mob2.format == UnityEditor.TextureImporterFormat.ASTC_4x4);
            }
            Check(name + ": no staged leftovers (Art/Weather~/" + name + " holds no sheet)", !File.Exists(Root + name + "/hazard.png") && !File.Exists(Root + name + "/ambient.png"));
        }
        string sun = Application.dataPath + "/Art/Backgrounds/Resources/Worlds/Space/Backdrop/";   // the approved sun is wired
        if (Directory.Exists(sun))
        {
            foreach (var set in new[] { "warm", "violet" })
            {
                // the disc file is the drawn cell cropped to its visible box (SpaceSunTuning.DiscCrop; scripts/crop_sun_disc.py)
                var crop = SpaceSunTuning.DiscCrop(set);
                string discPath = sun + "sun_" + set + ".png";
                if (!File.Exists(discPath)) Check("SpaceSun/sun_" + set + ".png not delivered yet", true);
                else
                {
                    var disc = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                    disc.LoadImage(File.ReadAllBytes(discPath));
                    Check("SpaceSun/sun_" + set + ".png is its crop box " + crop.w + " x " + crop.h + " (the drawn cell's visible pixels, nothing downscaled)",
                          disc.width == crop.w && disc.height == crop.h, disc.width + " x " + disc.height);
                    var dpx = disc.GetPixels32();
                    int opaque = 0;
                    for (int i = 0; i < dpx.Length; i++) if (dpx[i].a > 128) opaque++;
                    Check("SpaceSun/sun_" + set + ".png holds the disc (opaque pixels)", opaque > 100000, opaque.ToString());
                    bool clearEdge = true;
                    for (int x = 0; x < disc.width && clearEdge; x++) clearEdge = dpx[x].a == 0 && dpx[(disc.height - 1) * disc.width + x].a == 0;
                    for (int y = 0; y < disc.height && clearEdge; y++) clearEdge = dpx[y * disc.width].a == 0 && dpx[y * disc.width + disc.width - 1].a == 0;
                    Check("SpaceSun/sun_" + set + ".png keeps a clear margin on every edge", clearEdge);
                    Object.DestroyImmediate(disc);
                }
                Sheet(sun + "sun_" + set + "_corona.png", 2048, 1024, 1024, 2, 1, "SpaceSun", 0, 0);
            }
            Sheet(sun + "flares_warm.png", 4096, 2048, 1024, 4, 2, "SpaceSun", 0, 0);
            Sheet(sun + "flares_violet.png", 4096, 2048, 1024, 4, 2, "SpaceSun", 0, 0);
        }
        Wired();
        FrostSoftSheets();
        Debug.Log("[WART] " + (fails == 0 ? "ALL PASS" : fails + " FAILED"));
        return fails;
    }

    // `cols` x `rows` cells of `cell` px starting at row `firstRow`.
    static void Sheet(string path, int w, int h, int cell, int cols, int rows, string label, int firstRow = 0, int minOpaqueOverride = -1, int cellH = 0)
    {
        if (cellH == 0) cellH = cell;
        string file = label + "/" + Path.GetFileName(path);
        if (!File.Exists(path)) { Check(file + " not delivered yet", true); return; }
        var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        tex.LoadImage(File.ReadAllBytes(path));
        if (firstRow == 0) Check(file + " is " + w + " x " + h + " (high resolution, not downscaled)", tex.width == w && tex.height == h, tex.width + " x " + tex.height);
        if (tex.width != w || tex.height != h) { Object.DestroyImmediate(tex); return; }
        var px = tex.GetPixels32();
        for (int r = firstRow; r < firstRow + rows; r++)
            for (int c = 0; c < cols; c++)
            {
                int opaque = 0;
                float worst = BlockUpscale(px, w, h, c * cell, (h / cellH - 1 - r) * cellH, cell, cellH, out opaque);
                int need = minOpaqueOverride >= 0 ? minOpaqueOverride : (cell <= 256 ? 100 : MinOpaque);   // a tiny particle is legitimately a few hundred texels
                if (opaque < need) { Check(file + " cell r" + r + " c" + c + " has art", false, opaque + " opaque px"); continue; }
                if (opaque < MinOpaque) { Check(file + " cell r" + r + " c" + c + " is a small particle (too sparse for the block test)", true, opaque + " opaque px"); continue; }
                if (path.Contains("Weather~") && worst >= BlockFraction)
                {
                    // STAGED art (not wired, awaiting the user's vetting / a Codex redo): reported, not a failure. Wired art must pass.
                    Debug.Log("[WART] WARN  " + file + " cell r" + r + " c" + c + " is a block upscale (staged art, redo needed before wiring)  [worst block share " + worst.ToString("F2") + "]");
                    continue;
                }
                Check(file + " cell r" + r + " c" + c + " is true resolution (no block upscale)", worst < BlockFraction, "worst block share " + worst.ToString("F2"));
            }
        Object.DestroyImmediate(tex);
    }

    // The largest share of opaque texels equal to their block's first texel over k = 2..4 (aligned phase and diagonal shifts).
    static float BlockUpscale(Color32[] px, int w, int h, int x0, int y0, int cellW, int cellH, out int opaque)
    {
        opaque = 0;
        for (int y = 0; y < cellH; y++) for (int x = 0; x < cellW; x++) if (px[(y0 + y) * w + x0 + x].a > 24) opaque++;
        if (opaque < MinOpaque) return 0f;
        float worst = 0f;
        for (int k = 2; k <= 4; k++)
            for (int ph = 0; ph < k; ph++)
            {
                int same = 0, total = 0;
                for (int y = ph; y + k <= cellH; y++)
                {
                    int by = (y - ph) % k;
                    for (int x = ph; x + k <= cellW; x++)
                    {
                        var p = px[(y0 + y) * w + x0 + x];
                        if (p.a <= 24) continue;
                        total++;
                        int bx = (x - ph) % k;
                        var f = px[(y0 + y - by) * w + x0 + x - bx];
                        if (f.r == p.r && f.g == p.g && f.b == p.b && Mathf.Abs(f.a - p.a) < 3) same++;
                    }
                }
                if (total > 0) worst = Mathf.Max(worst, same / (float)total);
            }
        return worst;
    }

    // FROST'S WEATHER IS SOFT AND NO FRAME CUTS INTO ANOTHER (2026-11, the user: "all the clouds and weathers are too ugly and pixile and its
    // frames are cutting into each other ... make sure we fix this in frost"; build_soft_weather.py). The old wx_hazard cut a pick that was
    // not drawn on its grid (each 512 cell held parts of two drawings), the old weather carried the pick's detached strays and was x4 nearest
    // neighbour blocks, the old wx_ambient's particles were flat-cut at the sheet's edges. Now, read from disk:
    //   * every sprite rect sits inside its own cell with a transparent gutter around it (wrap-tiling front strips: the side gutters hold
    //     the strip's own continuation, never another cell), and a non-tiling drawing never touches its rect's border;
    //   * the sheets are smooth (many alpha levels), not stepped pixel art;
    //   * weather.json keeps the old 256 px cell's world size (w / pixelScale == 256).
    [System.Serializable] class WxRect { public string n; public int x, y, w, h; }
    [System.Serializable] class WxSheet { public WxRect[] sprites; public float pixelScale; public int sheetW, sheetH; }
    [System.Serializable] class WxCell { public int col, row, x, y, w, h; }
    [System.Serializable] class WxCells { public WxCell[] cells; }
    [System.Serializable] class WxLayout { public int gutter; }

    static Color32[] Px(string path, out int w, out int h)
    {
        var t = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        t.LoadImage(File.ReadAllBytes(path));
        w = t.width; h = t.height;
        var px = t.GetPixels32();
        Object.DestroyImmediate(t);
        return px;
    }

    static int AlphaLevels(Color32[] px)
    {
        var seen = new bool[256]; int n = 0;
        foreach (var p in px) if (!seen[p.a]) { seen[p.a] = true; n++; }
        return n;
    }

    // max alpha on the rect's own border (inside) and on the `ring` px around it (outside, clipped to the cell)
    static void Edges(Color32[] px, int W, int x, int y, int w, int h, int ring, int cx0, int cy0, int cw, int ch, out int border, out int outside)
    {
        border = 0; outside = 0;
        for (int yy = Mathf.Max(cy0, y - ring); yy < Mathf.Min(cy0 + ch, y + h + ring); yy++)
            for (int xx = Mathf.Max(cx0, x - ring); xx < Mathf.Min(cx0 + cw, x + w + ring); xx++)
            {
                int a = px[yy * W + xx].a;
                bool inside = xx >= x && xx < x + w && yy >= y && yy < y + h;
                if (!inside) outside = Mathf.Max(outside, a);
                else if (xx == x || xx == x + w - 1 || yy == y || yy == y + h - 1) border = Mathf.Max(border, a);
            }
    }

    static void FrostSoftSheets()
    {
        string dir = Application.dataPath + "/Art/Backgrounds/Resources/" + BackdropCatalog.Folder("Frost");
        int W, H;
        // weather: the cloud ceiling, wisps, mist, blizzard sheets, snow fields
        if (File.Exists(dir + "weather.png") && File.Exists(dir + "weather.json"))
        {
            var px = Px(dir + "weather.png", out W, out H);
            var m = JsonUtility.FromJson<WxSheet>(File.ReadAllText(dir + "weather.json"));
            int bad = 0, n = 0; string first = "";
            foreach (var r in m.sprites)
            {
                n++;
                int cx0 = r.x / 512 * 512, cy0 = r.y / 512 * 512;
                bool inCell = r.x - cx0 >= 4 && r.y - cy0 >= 4 && r.x + r.w <= cx0 + 512 - 4 && r.y + r.h <= cy0 + 512 - 4;
                int border = 255, outside = 255;
                if (inCell) Edges(px, W, r.x, r.y, r.w, r.h, 16, cx0, cy0, 512, 512, out border, out outside);
                bool ok = inCell && border == 0 && outside == 0 && Mathf.Abs(r.w / Mathf.Max(.01f, m.pixelScale) - 256f) < .5f;
                if (!ok) { bad++; if (first.Length == 0) first = r.n + " (border " + border + ", gutter " + outside + ")"; }
            }
            Check("Frost/weather.png: " + n + " sprites, each inside its own 512 cell with a clear gutter, never touching its rect, at the old cell's world size",
                  W == 2048 && H == 2048 && n == 16 && bad == 0, first);
            int lv = AlphaLevels(px);
            Check("Frost/weather.png is soft (" + lv + " alpha levels, not stepped pixel art)", lv >= 32);
        }
        // wx_hazard: the blizzard front (row 0, tiles side by side), the eddy (row 1), the warnings and gust curls (row 2)
        if (File.Exists(dir + "wx_hazard.png") && File.Exists(dir + "wx_hazard.json"))
        {
            var px = Px(dir + "wx_hazard.png", out W, out H);
            int g = JsonUtility.FromJson<WxLayout>(File.ReadAllText(dir + "wx_hazard.json")).gutter;
            Check("Frost/wx_hazard.json: a gutter of " + g + " px (4..64, a multiple of 4: ASTC 4x4 blocks never straddle two cells)", g >= 4 && g <= 64 && g % 4 == 0);
            int bad = 0; string first = "";
            int side = 512 - 2 * g;
            for (int r = 0; r < 3 && g >= 4; r++)
                for (int c = 0; c < 6; c++)
                {
                    int cx0 = c * 512, cy0 = (2 - r) * 512, x = cx0 + g, y = cy0 + g;
                    bool wrap = r == 0 || (r == 2 && c < 2);      // the front strips tile sideways: their side gutters continue the strip
                    int topBottom = 0, sides = 0, wrapMismatch = 0, border = 0;
                    for (int yy = cy0; yy < cy0 + 512; yy++)
                        for (int xx = cx0; xx < cx0 + 512; xx++)
                        {
                            int a = px[yy * W + xx].a;
                            bool inY = yy >= y && yy < y + side, inX = xx >= x && xx < x + side;
                            if (!inY) topBottom = Mathf.Max(topBottom, a);
                            else if (!inX)
                            {
                                if (!wrap) sides = Mathf.Max(sides, a);
                                else
                                {
                                    int src = xx < x ? xx + side : xx - side;     // the strip's own continuation
                                    var p = px[yy * W + xx]; var q = px[yy * W + src];
                                    if (p.r != q.r || p.g != q.g || p.b != q.b || p.a != q.a) wrapMismatch++;
                                }
                            }
                            else if (!wrap && (xx == x || xx == x + side - 1 || yy == y || yy == y + side - 1)) border = Mathf.Max(border, a);
                            else if (wrap && (yy == y || yy == y + side - 1)) border = Mathf.Max(border, a);
                        }
                    if (topBottom > 0 || sides > 0 || wrapMismatch > 0 || border > 0)
                    {
                        bad++;
                        if (first.Length == 0) first = "r" + r + " c" + c + " (gutter " + Mathf.Max(topBottom, sides) + ", wrap mismatches " + wrapMismatch + ", border " + border + ")";
                    }
                }
            Check("Frost/wx_hazard.png: every cell's drawing stays inside its " + side + " px rect (clear gutters; the front strips' side gutters are their own wrap-around)",
                  W == 3072 && H == 1536 && bad == 0, first);
            int lv = AlphaLevels(px);
            Check("Frost/wx_hazard.png is soft (" + lv + " alpha levels, not stepped pixel art)", lv >= 32);
        }
        // wx_ambient: snowflakes, glints, dots
        if (File.Exists(dir + "wx_ambient.png") && File.Exists(dir + "wx_ambient.json"))
        {
            var px = Px(dir + "wx_ambient.png", out W, out H);
            var m = JsonUtility.FromJson<WxCells>(File.ReadAllText(dir + "wx_ambient.json"));
            int bad = 0; string first = "";
            foreach (var cl in m.cells)
            {
                int cx0 = cl.col * 256, cy0 = (1 - cl.row) * 256;
                bool inCell = cl.x >= cx0 + 2 && cl.y >= cy0 + 2 && cl.x + cl.w <= cx0 + 254 && cl.y + cl.h <= cy0 + 254;
                int border = 255, outside = 255;
                if (inCell) Edges(px, W, cl.x, cl.y, cl.w, cl.h, 2, cx0, cy0, 256, 256, out border, out outside);
                if (!inCell || border > 0 || outside > 0) { bad++; if (first.Length == 0) first = "cell r" + cl.row + " c" + cl.col; }
            }
            Check("Frost/wx_ambient.png: every particle whole inside its own cell (nothing flat-cut at the sheet's or a rect's edge)", m.cells.Length == 16 && bad == 0, first);
            int lv = AlphaLevels(px);
            Check("Frost/wx_ambient.png is soft (" + lv + " alpha levels, not stepped pixel art)", lv >= 32);
        }
    }

    // Once wired (Resources/Weather/...), nothing may be clamped below its own size.
    static void Wired()
    {
        string dir = Application.dataPath + "/Art/Backgrounds/Resources/Weather";
        if (!Directory.Exists(dir)) { Check("no wired weather textures yet (art awaiting the user's vetting)", true); return; }
        foreach (var f in Directory.GetFiles(dir, "*.png", SearchOption.AllDirectories))
        {
            string rel = "Assets" + f.Substring(Application.dataPath.Length).Replace('\\', '/');
            var imp = UnityEditor.AssetImporter.GetAtPath(rel) as UnityEditor.TextureImporter;
            var t = UnityEditor.AssetDatabase.LoadAssetAtPath<Texture2D>(rel);
            if (imp == null || t == null) continue;
            Check(rel + " is not clamped below its size and has no mip maps", imp.maxTextureSize >= Mathf.Max(t.width, t.height) && !imp.mipmapEnabled);
        }
    }
}
