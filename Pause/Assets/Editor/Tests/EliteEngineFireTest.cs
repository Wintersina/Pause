using System.IO;
using UnityEngine;

// Engine-fire frames, all worlds (art: Art/Enemies/src~/elite_engine_fire/engine_fire.py): every elite that was
// redone has a flight loop of >= 2 cells that is ONE fixed hull (pixel-identical outside the engine zone) with only
// the flames changing from cell to cell; the flames are cold / world palette (no pink outside Space, no pink or red in
// Frost), the cells keep the 6 px framing margin, the flame leaves each bell (it covers the nozzle mouth row) and the
// engine zone is the only thing that differs. Frost idles with the engines only: no sway.
//
//   scripts/unity-batch.sh -executeMethod AllTests.RunSuites -suites EliteEngineFireTest
public static class EliteEngineFireTest
{
    static int fails;
    static void Check(string what, bool ok)
    {
        Debug.Log((ok ? "[EEF] PASS  " : "[EEF] FAIL  ") + what);
        if (!ok) fails++;
    }

    public static void Run() { TestHarness.Exit(Execute()); }

    const int S = 384, Margin = 6, Half = 48, Up = 28, FloorY = 378;   // the engine zone: per nozzle x +-Half, y from nozzle y - Up down to FloorY (bell, spill, halo, flame)
    static readonly string[] Keys =
    {
        // one line per world, added with that world's frames
        "ember_elite_ash_wraith", "ember_elite_brass_vulture", "ember_elite_cauterizer",
        "ember_elite_coalrunner", "ember_elite_kilnback", "ember_elite_sunstoke",
        "verdant_elite_leafblade", "verdant_elite_resin_warden", "verdant_elite_sporebloom",
        "verdant_elite_thornlash", "verdant_elite_timber_hauler",
        "tide_elite_bathyscaphe", "tide_elite_pearl_warden", "tide_elite_riptide",
        "tide_elite_thunderfin", "tide_elite_trawler",
        "storm_elite_anvil_cloud", "storm_elite_refinery_tender", "storm_elite_rod_dancer",
        "storm_elite_skyhook_harrier", "storm_elite_static_bastion",
        "frost_elite_cryo_siren", "frost_elite_rimebreaker", "frost_elite_floe_harrower",
        "frost_elite_glacier_tender", "frost_elite_whiteout_sentinel",
        "desert_elite_caravan_hauler", "desert_elite_dune_stalker", "desert_elite_glass_cannon",
        "desert_elite_lens_matriarch", "desert_elite_mirror_bastion", "desert_elite_scorpion_walker",
    };

    static string Dir(string key) { return "Assets/Art/Resources/Elites/" + char.ToUpper(key[0]) + key.Substring(1, key.IndexOf('_') - 1) + "/"; }

    public static int Execute()
    {
        fails = 0;
        EliteCatalog.Reload();
        foreach (string key in Keys) One(key);
        Debug.Log("[EEF] failures: " + fails);
        return fails;
    }

    static bool Engine(EliteDef def, int x, int y)
    {
        if (y >= FloorY) return false;
        foreach (var n in def.nozzles)
            if (Mathf.Abs(x - Mathf.RoundToInt(n.x)) <= Half && y >= Mathf.RoundToInt(n.y) - Up) return true;
        return false;
    }

    static void One(string key)
    {
        var def = EliteCatalog.Find(key);
        Check(key + ": def exists with a flight loop of >= 2 cells (" + (def == null ? "-" : def.cells.flight.Length.ToString()) + ")", def != null && def.cells.flight.Length >= 2);
        if (def == null || def.cells.flight.Length < 2) return;
        var t = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        t.LoadImage(File.ReadAllBytes(Dir(key) + key + ".png"));
        int W = t.width; var px = t.GetPixels32(); Object.DestroyImmediate(t);
        int[] fl = def.cells.flight;
        int max = 0; foreach (int c in fl) max = Mathf.Max(max, c);
        Check(key + ": strip holds every flight cell (" + W / S + " cells)", W >= (max + 1) * S);
        if (W < (max + 1) * S) return;
        Color32 P(int cell, int x, int y) { return px[(S - 1 - y) * W + cell * S + x]; }

        int hullDiff = 0, flameDiffCells = 0, edge = 0, hot = 0, pink = 0;
        for (int i = 1; i < fl.Length; i++)
        {
            int flameDiff = 0;
            for (int y = 0; y < S; y++)
                for (int x = 0; x < S; x++)
                {
                    var a = P(fl[0], x, y); var b = P(fl[i], x, y);
                    bool same = a.r == b.r && a.g == b.g && a.b == b.b && a.a == b.a;
                    if (same) continue;
                    if (Engine(def, x, y)) flameDiff++; else hullDiff++;
                }
            if (flameDiff > 0) flameDiffCells++;
            Check(key + ": flight cell " + fl[i] + " flames differ from cell " + fl[0] + " (" + flameDiff + " px in the engine regions)", flameDiff >= 40);
        }
        for (int i = 0; i < fl.Length; i++)
            for (int y = 0; y < S; y++)
                for (int x = 0; x < S; x++)
                {
                    var p = P(fl[i], x, y);
                    if (p.a == 0) continue;
                    if (x < Margin || y < Margin || x >= S - Margin || y >= S - Margin) edge++;
                    if (Engine(def, x, y) && p.a > 128 && p.r > p.g + 60 && p.r > p.b - 20) hot++;   // red / orange / pink fire
                    if (Engine(def, x, y) && p.a > 128 && p.r > 170 && p.b > 140 && p.g < p.r - 70 && p.g < p.b - 40) pink++;
                }
        Check(key + ": the hull is pixel-identical across the idle loop cells (" + hullDiff + " px differ outside the engine regions)", hullDiff == 0);
        Check(key + ": at least two idle cells have different flames", flameDiffCells >= 1);
        Check(key + ": idle cells keep the " + Margin + " px framing margin (" + edge + " px inside it)", edge == 0);
        Check(key + ": no pink flame outside Space (" + pink + " px)", pink == 0);
        if (key.StartsWith("frost_")) Check(key + ": Frost fire stays cold, no red / orange (" + hot + " px)", hot == 0);
    }
}
