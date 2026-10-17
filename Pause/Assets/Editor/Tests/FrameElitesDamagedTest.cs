using System.IO;
using UnityEngine;

// Frame-fix campaign: the damaged cell of the 384 px elites (Tide, Storm, Desert, Frost, Space) is a REAL painted wreck, not the flight hull
// with a hairline crack. Per elite: the damaged cell differs from the flight cell by a real margin (the hull itself, above the engines, and
// the flames), the hull silhouette still overlaps the flight hull (same ship, same anchor), engines burn unevenly (some out, some
// ragged), nothing touches the cell margin, and no pink appears outside Space.
//
//   Unity -batchmode -quit -projectPath <abs>/Pause -executeMethod FrameElitesDamagedTest.Run
public static class FrameElitesDamagedTest
{
    static int fails;
    static void Check(string what, bool ok)
    {
        Debug.Log((ok ? "[FED] PASS  " : "[FED] FAIL  ") + what);
        if (!ok) fails++;
    }

    public static void Run() { TestHarness.Exit(Execute()); }

    static readonly string[] Keys =
    {
        "tide_elite_bathyscaphe", "tide_elite_pearl_warden", "tide_elite_riptide", "tide_elite_thunderfin", "tide_elite_trawler",
        "storm_elite_anvil_cloud", "storm_elite_refinery_tender", "storm_elite_rod_dancer", "storm_elite_skyhook_harrier", "storm_elite_static_bastion",
        "desert_elite_caravan_hauler", "desert_elite_dune_stalker", "desert_elite_glass_cannon", "desert_elite_lens_matriarch", "desert_elite_mirror_bastion", "desert_elite_scorpion_walker",
        "frost_elite_floe_harrower", "frost_elite_glacier_tender", "frost_elite_whiteout_sentinel",
        "space_elite_eventide_bastion", "space_elite_orbit_reaver", "space_elite_rift_lancer", "space_elite_singularity_hauler",
    };

    const int Cell = 384, Margin = 6, Solid = 96;

    // top-left origin RGBA of one cell
    static Color32[] CellPixels(Texture2D t, int cell)
    {
        var px = t.GetPixels32();
        var o = new Color32[Cell * Cell];
        for (int y = 0; y < Cell; y++)
            for (int x = 0; x < Cell; x++)
                o[y * Cell + x] = px[(t.height - 1 - y) * t.width + cell * Cell + x];
        return o;
    }

    static bool IsPink(Color32 c)
    {
        if (c.a < Solid) return false;
        Color.RGBToHSV(c, out float h, out float s, out float v);
        return s > .35f && v > .4f && h > .78f && h < .96f;
    }

    public static int Execute()
    {
        fails = 0;
        foreach (string key in Keys)
        {
            var d = EliteCatalog.Find(key);
            Check(key + ": def exists", d != null);
            if (d == null) continue;
            string world = EliteArt.WorldFolder(d);
            string path = "Assets/Art/Resources/Elites/" + world + "/" + key + ".png";
            var t = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            t.LoadImage(File.ReadAllBytes(path));
            int fl = d.cells.Flight0, dm = d.cells.damaged;
            Check(key + ": strip is 384 px cells with a damaged cell (" + t.width + " x " + t.height + ", damaged " + dm + ")", t.height == Cell && dm >= 0 && t.width >= (dm + 1) * Cell);
            if (t.height != Cell || dm < 0 || t.width < (dm + 1) * Cell) { Object.DestroyImmediate(t); continue; }
            var f = CellPixels(t, fl);
            var g = CellPixels(t, dm);
            Object.DestroyImmediate(t);

            int nozzleTop = Cell;
            foreach (var n in d.nozzles) nozzleTop = Mathf.Min(nozzleTop, Mathf.RoundToInt(n.y));
            int hullRows = nozzleTop - 2;   // rows above the engines: the hull, flames excluded

            int hullF = 0, hullInter = 0, hullUnion = 0, hullChanged = 0, minX = Cell, minY = Cell, maxX = -1, maxY = -1, flame = 0, pinkF = 0, pinkG = 0;
            for (int y = 0; y < Cell; y++)
                for (int x = 0; x < Cell; x++)
                {
                    var a = f[y * Cell + x]; var b = g[y * Cell + x];
                    bool sa = a.a >= Solid, sb = b.a >= Solid;
                    if (sb) { if (x < minX) minX = x; if (x > maxX) maxX = x; if (y < minY) minY = y; if (y > maxY) maxY = y; }
                    if (IsPink(a)) pinkF++;
                    if (IsPink(b)) pinkG++;
                    if (y < hullRows)
                    {
                        if (sa) hullF++;
                        if (sa && sb) hullInter++;
                        if (sa || sb) hullUnion++;
                        int diff = Mathf.Max(Mathf.Abs(a.r - b.r), Mathf.Max(Mathf.Abs(a.g - b.g), Mathf.Abs(a.b - b.b)));
                        if (sa != sb || (sa && sb && diff > 35)) hullChanged++;
                    }
                    else if (y >= nozzleTop + 4 && b.a >= 200)
                    {
                        int mx = Mathf.Max(b.r, Mathf.Max(b.g, b.b)), mn = Mathf.Min(b.r, Mathf.Min(b.g, b.b));
                        if (mx - mn > 90) flame++;   // saturated pixels under the bells: the flames
                    }
                }
            float iou = hullInter / (float)Mathf.Max(1, hullUnion);
            float changed = hullChanged / (float)Mathf.Max(1, hullF);
            Check(key + ": hull overlaps the flight hull (IoU " + iou.ToString("0.000") + " >= 0.85, same anchor)", iou >= .85f);
            Check(key + ": painted damage changes " + (changed * 100).ToString("0.0") + "% of the hull (>= 4%)", changed >= .04f);
            Check(key + ": burning engines present (" + flame + " flame px >= 120)", flame >= 120);
            Check(key + ": art " + minX + "," + minY + " - " + maxX + "," + maxY + " keeps " + Margin + " px clear of the cell border",
                  minX >= Margin && minY >= Margin && maxX <= Cell - 1 - Margin && maxY <= Cell - 1 - Margin);
            if (world != "Space") Check(key + ": no pink outside Space (" + pinkG + " px, flight " + pinkF + ")", pinkG <= pinkF + 8);
        }
        return fails;
    }
}
