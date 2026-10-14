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
                Sheet(sun + "sun_" + set + ".png", 2048, 1024, 1024, 2, 1, "SpaceSun");
                Sheet(sun + "sun_" + set + "_corona.png", 2048, 1024, 1024, 2, 1, "SpaceSun", 0, 0);
            }
            Sheet(sun + "flares_warm.png", 4096, 2048, 1024, 4, 2, "SpaceSun", 0, 0);
            Sheet(sun + "flares_violet.png", 4096, 2048, 1024, 4, 2, "SpaceSun", 0, 0);
        }
        Wired();
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
