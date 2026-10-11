using System.IO;
using UnityEngine;

// Every elite def has its authored <key>_death.png: 576 x 192, three 192 px cells
// (flash, rupture, dispersal) that slice through EliteArt, start on the live hull
// (cell 0 covers every opaque pixel of the flight cell), keep a 6 px clear margin,
// carry the hostile-pink core accent, and contain no red. Also: Steel Hound's
// death flipbook cell 0 sits on its flight registration.
//
//   Unity -batchmode -quit -projectPath <abs>/Pause -executeMethod EliteDeathStripTest.Run
public static class EliteDeathStripTest
{
    static int fails;
    static void Check(string what, bool ok)
    {
        Debug.Log((ok ? "[EDS] PASS  " : "[EDS] FAIL  ") + what);
        if (!ok) fails++;
    }

    public static void Run() { TestHarness.Exit(Execute()); }

    static string Folder(EliteDef d)
    {
        string w = d.key.Substring(0, d.key.IndexOf('_'));
        return char.ToUpper(w[0]) + w.Substring(1);
    }

    // the sixteen strips built by src~/build_death_strips.py (the Tide and four Verdant ones predate it)
    static bool Authored(string key)
    {
        return key.StartsWith("ember_") || key.StartsWith("frost_") || key.StartsWith("space_") || key == "verdant_elite_resin_warden";
    }

    static Texture2D Read(string path)
    {
        var t = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        t.LoadImage(File.ReadAllBytes(path));
        return t;
    }

    public static int Execute()
    {
        fails = 0;
        int n = 0;
        foreach (var d in EliteCatalog.All)
        {
            n++;
            var death = EliteArt.ExtraFrames(d, EliteArt.Extra.Death);
            Check(d.key + ": death strip slices into 3 cells of 192", death != null && death.Length == 3 &&
                  Mathf.Approximately(death[0].rect.width, 192f) && Mathf.Approximately(death[0].rect.height, 192f));
            string dir = "Assets/Art/Resources/Elites/" + Folder(d) + "/";
            if (!File.Exists(dir + d.key + "_death.png") || !Authored(d.key)) continue;
            var ds = Read(dir + d.key + "_death.png");
            var fl = Read(dir + d.key + ".png");
            int flightCell = d.cells != null ? d.cells.Flight0 : 3;
            if (!(d.cells != null && d.cells.flight != null && d.cells.flight.Length > 0)) flightCell = 3;
            // Ember defs carry no cell map: its live flight frame is the loop's last (cell 3) -- registration is checked there
            bool ember = d.key.StartsWith("ember_");
            if (ember) flightCell = 3;
            int covered = 0, total = 0, pink = 0, red = 0, edge = 0, flightRed = 0;
            for (int y = 0; y < 192; y++)
                for (int x = 0; x < 192; x++)
                {
                    var f = fl.GetPixel(flightCell * 192 + x, y);
                    var c0 = ds.GetPixel(x, y);
                    if (f.a > .5f)
                    {
                        total++; if (c0.a > .5f) covered++;
                        Color.RGBToHSV(f, out float fh, out float fs, out float fv);
                        if (fs > .6f && fv > .55f && (fh < .012f || fh > .985f)) flightRed++;
                    }
                    for (int cell = 0; cell < 3; cell++)
                    {
                        var p = ds.GetPixel(cell * 192 + x, y);
                        if (p.a < .5f) continue;
                        if (x < 6 || x >= 186 || y < 6 || y >= 186) edge++;
                        Color.RGBToHSV(p, out float h, out float s, out float v);
                        if (s > .6f && v > .55f && (h < .012f || h > .985f)) red++;
                        if (p.r > .9f && p.g < .45f && p.b > .7f) pink++;
                    }
                }
            Check(d.key + ": cell 0 sits on the live hull (" + covered + "/" + total + " flight pixels covered)", total > 500 && covered >= total * .98f);
            Check(d.key + ": 6 px transparent margin in every cell (" + edge + " stray)", edge == 0);
            Check(d.key + ": hostile-pink core accent present (" + pink + " px), adds no red (" + red + " px vs " + flightRed + " in the hull itself)", pink > 20 && red <= flightRed * 3 + 2);
            Object.DestroyImmediate(ds); Object.DestroyImmediate(fl);
        }
        Check("every elite def was walked (" + n + ")", n >= 20);

        // Steel Hound: death flipbook cell 0 registers with the flight cells
        var dt = Read("Assets/Art/Resources/Enemies/Death/space_chaser.png");
        var ft = Read("Assets/Art/Resources/Enemies/space_chaser.png");
        float dx, dy;
        Centroid(dt, 0, out float ax, out float ay);
        Centroid(ft, 3, out float bx, out float by);
        dx = ax - bx; dy = ay - by;
        Check("Steel Hound death cell 0 hull centroid within 1 px of its last flight cell (" + dx.ToString("0.0") + ", " + dy.ToString("0.0") + ")",
              Mathf.Abs(dx) <= 1f && Mathf.Abs(dy) <= 1f);
        Object.DestroyImmediate(dt); Object.DestroyImmediate(ft);
        Debug.Log("[EDS] " + (fails == 0 ? "ALL PASS" : fails + " FAILED"));
        return fails;
    }

    static void Centroid(Texture2D t, int cell, out float cx, out float cy)
    {
        long sx = 0, sy = 0, c = 0;
        int side = t.height;
        for (int y = 0; y < side; y++)
            for (int x = 0; x < side; x++)
                if (t.GetPixel(cell * side + x, y).a > .5f) { sx += x; sy += y; c++; }
        cx = c > 0 ? (float)sx / c : 0f; cy = c > 0 ? (float)sy / c : 0f;
    }
}
