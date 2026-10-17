using System.IO;
using UnityEngine;

// DESERT / EMBER REGULAR SHIPS NEED REAL PAINTED POSES, not one master plus a glint. Every desert_* / ember_* flying enemy strip
// (alien, big, chaser, fighter_1..4: 7 cells, 0-3 idle, 4-5 tell, 6 hit) is measured cell by cell:
//  * nothing within 6 px of a cell edge in any cell;
//  * the idle cells are distinct drawings (>= 5% of the silhouette changes between every pair);
//  * both tell cells and the hit cell have a SILHOUETTE that differs from the idle drawing by >= 6% (a glint or a flash changes ~0-3%),
//    and the two tell cells differ from each other by >= 4% (warm-up vs peak);
//  * the pivot stays within 6 px sideways (bbox centre) / 8 px vertically (alpha centroid: flame length changes the bbox, not the hull)
//    across all seven cells (registered on one anchor).
// Rock hazards are out of scope (near-still, engine-free).
//
//   Unity -batchmode -quit -projectPath <abs>/Pause -executeMethod DesertEmberFrameTest.Run
public static class DesertEmberFrameTest
{
    static int fails;
    static void Check(string what, bool ok)
    {
        Debug.Log((ok ? "[DEFR] PASS  " : "[DEFR] FAIL  ") + what);
        if (!ok) fails++;
    }

    public static void Run() { TestHarness.Exit(Execute()); }

    const string Dir = "Assets/Art/Resources/Enemies/";
    const int Margin = 6;
    static readonly string[] Kinds = { "alien", "big", "chaser", "fighter_1", "fighter_2", "fighter_3", "fighter_4" };

    public static int Execute()
    {
        fails = 0;
        int n = 0;
        foreach (var world in new[] { "desert", "ember" })
            foreach (var k in Kinds) { Ship(world + "_" + k); n++; }
        Check("desert / ember ships measured (" + n + ")", n == 14);
        Debug.Log("[DEFR] failures: " + fails);
        return fails;
    }

    static void Ship(string name)
    {
        var t = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        t.LoadImage(File.ReadAllBytes(Dir + name + ".png"));
        int h = t.height, cells = t.width / h;
        var px = t.GetPixels32();
        bool sized = cells == 7;
        Check(name + ": seven square cells", sized);
        if (!sized) { Object.DestroyImmediate(t); return; }
        bool margins = true, any = true;
        float lox = 1e9f, hix = -1e9f, loy = 1e9f, hiy = -1e9f;
        long sy; int cnt;
        for (int c = 0; c < cells; c++)
        {
            int x0 = int.MaxValue, y0 = int.MaxValue, x1 = -1, y1 = -1; sy = 0; cnt = 0;
            for (int y = 0; y < h; y++)
                for (int x = 0; x < h; x++)
                    if (px[y * t.width + c * h + x].a > 8) { x0 = Mathf.Min(x0, x); x1 = Mathf.Max(x1, x); y0 = Mathf.Min(y0, y); y1 = Mathf.Max(y1, y); sy += y; cnt++; }
            if (x1 < 0) { any = false; continue; }
            if (Mathf.Min(Mathf.Min(x0, y0), Mathf.Min(h - 1 - x1, h - 1 - y1)) < Margin) { margins = false; Debug.Log("[DEFR] " + name + " cell " + c + " is within " + Margin + " px of the edge"); }
            lox = Mathf.Min(lox, (x0 + x1) * .5f); hix = Mathf.Max(hix, (x0 + x1) * .5f);
            float cyc = (float)sy / cnt; loy = Mathf.Min(loy, cyc); hiy = Mathf.Max(hiy, cyc);
        }
        Check(name + ": every cell has art", any);
        Check(name + ": nothing within " + Margin + " px of a cell edge", margins);
        Check(name + ": all seven cells share one pivot (<= 6 / 8 px)", hix - lox <= 6f && hiy - loy <= 8f);
        float minIdle = 9f;
        for (int a = 0; a < 4; a++) for (int b = a + 1; b < 4; b++) minIdle = Mathf.Min(minIdle, Change(px, t.width, h, a, b));
        Check(name + ": the four idle cells are distinct drawings (min change " + minIdle.ToString("0.000") + " >= 0.05)", minIdle >= .05f);
        float tell1 = Change(px, t.width, h, 0, 4, true), tell2 = Change(px, t.width, h, 0, 5, true), hit = Change(px, t.width, h, 0, 6, true), tt = Change(px, t.width, h, 4, 5, true);
        Check(name + ": tell warm-up is a real pose (" + tell1.ToString("0.000") + " >= 0.06)", tell1 >= .06f);
        Check(name + ": tell peak is a real pose (" + tell2.ToString("0.000") + " >= 0.06)", tell2 >= .06f);
        Check(name + ": hit is a real recoil pose (" + hit.ToString("0.000") + " >= 0.06)", hit >= .06f);
        Check(name + ": tell warm-up and peak differ (" + tt.ToString("0.000") + " >= 0.04)", tt >= .04f);
        Object.DestroyImmediate(t);
    }

    // fraction of the union silhouette where the alpha (and, unless silhouetteOnly, the colour) differs between two cells
    static float Change(Color32[] px, int w, int h, int a, int b, bool silhouetteOnly = false)
    {
        int union = 0, diff = 0;
        for (int y = 0; y < h; y++)
            for (int x = 0; x < h; x++)
            {
                var p = px[y * w + a * h + x]; var q = px[y * w + b * h + x];
                bool pa = p.a > 40, qa = q.a > 40;
                if (!pa && !qa) continue;
                union++;
                if (pa != qa) { diff++; continue; }
                if (silhouetteOnly) continue;
                int d = Mathf.Max(Mathf.Abs(p.r - q.r), Mathf.Max(Mathf.Abs(p.g - q.g), Mathf.Abs(p.b - q.b)));
                if (d > 40) diff++;
            }
        return union == 0 ? 0f : (float)diff / union;
    }
}
