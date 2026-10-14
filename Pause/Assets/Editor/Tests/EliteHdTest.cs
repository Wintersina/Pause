using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

// The HD elite rebuilds (docs/ship-scale-and-lockon-analysis.md 2.7, Elite/<World>/src~/build_*_hd.py): which elites are built on a
// 384 px cell (painted master area-averaged, never upscaled, never block-doubled) and which stay on 192 (no master: Codex redraw), and
// that every def agrees with its strips (cellPixels vs the PNG size, 7 flight + 3 death cells), the points scale with the cell,
// hull / hit radius and size tier are untouched, HD strips are ASTC on phones and the per-world texture budget holds.
//
//   Unity -batchmode -quit -projectPath <abs>/Pause -executeMethod EliteHdTest.Run
public static class EliteHdResolution
{
    // Built from a painted master by the build_*_hd.py scripts (cell 384). Everything else is 192 until its master is redrawn.
    public static readonly HashSet<string> Hd = new HashSet<string>
    {
        "tide_elite_thunderfin", "tide_elite_riptide", "tide_elite_bathyscaphe", "tide_elite_trawler", "tide_elite_pearl_warden",
        "storm_elite_skyhook_harrier", "storm_elite_refinery_tender", "storm_elite_rod_dancer", "storm_elite_anvil_cloud", "storm_elite_static_bastion",
        "desert_elite_dune_stalker", "desert_elite_caravan_hauler", "desert_elite_lens_matriarch", "desert_elite_scorpion_walker", "desert_elite_mirror_bastion",
        "frost_elite_floe_harrower", "frost_elite_glacier_tender", "frost_elite_whiteout_sentinel",
        "verdant_elite_timber_hauler", "verdant_elite_thornlash", "verdant_elite_sporebloom", "verdant_elite_leafblade",
    };

    public static int ExpectedCell(string key) { return Hd.Contains(key) ? 384 : 192; }
    public static bool CellOk(EliteDef d) { return d != null && d.cellPixels == ExpectedCell(d.key); }
}

public static class EliteHdTest
{
    static int fails;
    static void Check(string what, bool ok)
    {
        Debug.Log((ok ? "[EHD] PASS  " : "[EHD] FAIL  ") + what);
        if (!ok) fails++;
    }

    public static void Run() { TestHarness.Exit(Execute()); }

    const string Res = "Assets/Art/Resources/Elites/";

    static (int w, int h) PngSize(string path)
    {
        var b = File.ReadAllBytes(path);
        int w = (b[16] << 24) | (b[17] << 16) | (b[18] << 8) | b[19], h = (b[20] << 24) | (b[21] << 16) | (b[22] << 8) | b[23];
        return (w, h);
    }

    // Share of aligned 2 x 2 blocks of opaque pixels that are one flat colour, vs the same for blocks offset by one pixel: block-doubled
    // art is 100% / ~25%, a painted downsample is about equal.
    static void BlockShares(Texture2D t, int cellX, int cell, out float aligned, out float offset)
    {
        var px = t.GetPixels32();
        int W = t.width, a = 0, an = 0, o = 0, on = 0;
        for (int pass = 0; pass < 2; pass++)
            for (int y = pass; y + 1 < cell; y += 2)
                for (int x = cellX + pass; x + 1 < cellX + cell; x += 2)
                {
                    var p0 = px[y * W + x];
                    if (p0.a < 250) continue;
                    var p1 = px[y * W + x + 1]; var p2 = px[(y + 1) * W + x]; var p3 = px[(y + 1) * W + x + 1];
                    if (p1.a < 250 || p2.a < 250 || p3.a < 250) continue;
                    bool flat = Same(p0, p1) && Same(p0, p2) && Same(p0, p3);
                    if (pass == 0) { an++; if (flat) a++; } else { on++; if (flat) o++; }
                }
        aligned = an > 0 ? a / (float)an : 0; offset = on > 0 ? o / (float)on : 0;
    }

    static bool Same(Color32 a, Color32 b) { return a.r == b.r && a.g == b.g && a.b == b.b; }

    public static int Execute()
    {
        fails = 0;
        var worldBytes = new Dictionary<string, long>();
        int hd = 0, walked = 0;
        foreach (var d in EliteCatalog.All)
        {
            walked++;
            string folder = EliteArt.WorldFolder(d);
            string strip = Res + folder + "/" + d.key + ".png", death = Res + folder + "/" + d.key + "_death.png";
            Check(d.key + ": cellPixels " + d.cellPixels + " is the expected " + EliteHdResolution.ExpectedCell(d.key) + " (HD list in EliteHdResolution)", EliteHdResolution.CellOk(d));
            if (!File.Exists(strip)) continue;
            var (w, h) = PngSize(strip);
            Check(d.key + ": strip " + w + " x " + h + " = 7 cells of the def's " + d.cellPixels + " px", h == d.cellPixels && w == 7 * d.cellPixels);
            if (File.Exists(death))
            {
                var (dw, dh) = PngSize(death);
                Check(d.key + ": death strip " + dw + " x " + dh + " = 3 cells of " + d.cellPixels + " px", dh == d.cellPixels && dw == 3 * d.cellPixels);
            }
            foreach (var p in d.muzzles) Check(d.key + ": muzzle " + p.name + " inside the cell", p.x > 0 && p.x < d.cellPixels && p.y > 0 && p.y < d.cellPixels);
            foreach (var p in d.nozzles) Check(d.key + ": nozzle " + p.name + " inside the cell", p.x > 0 && p.x < d.cellPixels && p.y > 0 && p.y < d.cellPixels);
            if (d.cellPixels != 384) continue;
            hd++;
            // no block doubling: the hover cell and the damaged cell
            var tex = new Texture2D(2, 2);
            tex.LoadImage(File.ReadAllBytes(strip));
            foreach (int cell in new[] { 3, 6 })
            {
                BlockShares(tex, cell * 384, 384, out float al, out float off);
                Check(d.key + ": cell " + cell + " is not block-doubled (aligned 2x2 flat " + (al * 100).ToString("0") + "% vs offset " + (off * 100).ToString("0") + "%)", al < .92f && al < off * 1.6f + .12f);
            }
            Object.DestroyImmediate(tex);
            // a pixel is now 1/Z of the old one: the points, in the world, sit where they did (pixel -> local keeps the same hull)
            Check(d.key + ": cellWorldSize " + d.cellWorldSize.ToString("0.00") + " and hullRadius " + d.hullRadius.ToString("0.000") + " unchanged by the rebuild (pixel size = " + d.PixelWorld.ToString("0.0000") + " u)",
                  d.cellWorldSize >= 1.4f && d.hullRadius > .2f && d.hullRadius < d.cellWorldSize * .5f);
            var ti = AssetImporter.GetAtPath(strip) as TextureImporter;
            var an = ti != null ? ti.GetPlatformTextureSettings("Android") : default;
            Check(d.key + ": strip max size 4096 (a 2688 px strip is not downscaled) and ASTC on Android (" + (ti != null ? an.format.ToString() : "no importer") + ")",
                  ti != null && ti.maxTextureSize >= 4096 && an.overridden && AndroidTextureDiet.IsAstc(an.format));
            long px = (long)w * h + (File.Exists(death) ? (long)PngSize(death).w * PngSize(death).h : 0);
            worldBytes[folder] = (worldBytes.TryGetValue(folder, out long b) ? b : 0) + px;
        }
        foreach (var kv in worldBytes)
        {
            // phones: ASTC 6x6 = 3.56 bits per px (the Android diet); the editor / desktop keep RGBA32
            float mobile = kv.Value * 3.56f / 8f / 1048576f, desktop = kv.Value * 4f / 1048576f;
            Check(kv.Key + ": HD elite strips cost " + mobile.ToString("0.0") + " MB on phones (ASTC 6x6), " + desktop.ToString("0.0") + " MB uncompressed (budget 4 / 40 MB)", mobile <= 4f && desktop <= 40f);
        }
        Check("every HD elite on the list was walked (" + hd + " of " + EliteHdResolution.Hd.Count + ")", hd == EliteHdResolution.Hd.Count);
        Check("every elite def was walked (" + walked + ")", walked >= 36);
        return fails;
    }
}
