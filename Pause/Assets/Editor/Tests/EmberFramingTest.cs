using System.IO;
using UnityEngine;

// EMBER SPRITES MUST NEVER BE CLIPPED. Every installed Ember strip (enemies, hazards, the six elites and every death strip) is
// measured cell by cell on its alpha: nothing may sit within 4 px of a cell edge (no cut-off wing, flame or horn), and the flight
// frames must keep their framing: across the four idle cells the pivot (bbox centre) drifts <= 3 px sideways / 5 px vertically and
// the bbox size changes <= 14 x 8 px; across all seven cells the pivot stays within 6 / 9 px (tell and hit marks add sparks only).
// Tumbling rocks and death strips (which disperse by design) are checked for the edge margin only.
//
//   Unity -batchmode -quit -projectPath <abs>/Pause/Assets -executeMethod EmberFramingTest.Run
public static class EmberFramingTest
{
    static int fails;
    static void Check(string what, bool ok)
    {
        Debug.Log((ok ? "[EMBF] PASS  " : "[EMBF] FAIL  ") + what);
        if (!ok) fails++;
    }

    public static void Run() { TestHarness.Exit(Execute()); }

    const string Root = "Assets/Art/Resources/";
    const int Margin = 4;

    public static int Execute()
    {
        fails = 0;
        int n = 0;
        foreach (var f in Directory.GetFiles(Root + "Enemies", "ember_*.png")) { Strip(f, false); n++; }
        foreach (var f in Directory.GetFiles(Root + "Enemies/Death", "ember_*.png")) { Strip(f, true); n++; }
        foreach (var f in Directory.GetFiles(Root + "Elites/Ember", "ember_elite_*.png")) { Strip(f, f.EndsWith("_death.png")); n++; }
        Check("Ember strips measured (" + n + ")", n >= 34);
        Debug.Log("[EMBF] failures: " + fails);
        return fails;
    }

    static void Strip(string path, bool dispersing)
    {
        string name = Path.GetFileNameWithoutExtension(path);
        var t = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        t.LoadImage(File.ReadAllBytes(path));
        int h = t.height, cells = t.width / h;
        var px = t.GetPixels32();
        var cx = new float[cells]; var cy = new float[cells]; var w = new int[cells]; var ht = new int[cells];
        bool margins = true, any = true;
        for (int c = 0; c < cells; c++)
        {
            int x0 = int.MaxValue, y0 = int.MaxValue, x1 = -1, y1 = -1;
            for (int y = 0; y < h; y++)
                for (int x = 0; x < h; x++)
                    if (px[y * t.width + c * h + x].a > 8) { x0 = Mathf.Min(x0, x); x1 = Mathf.Max(x1, x); y0 = Mathf.Min(y0, y); y1 = Mathf.Max(y1, y); }
            if (x1 < 0) { any = false; continue; }
            if (Mathf.Min(Mathf.Min(x0, y0), Mathf.Min(h - 1 - x1, h - 1 - y1)) < Margin) { margins = false; Debug.Log("[EMBF] " + name + " cell " + c + " bbox " + x0 + "," + y0 + "-" + x1 + "," + y1 + " touches the " + Margin + " px margin"); }
            cx[c] = (x0 + x1) * .5f; cy[c] = (y0 + y1) * .5f; w[c] = x1 - x0; ht[c] = y1 - y0;
        }
        Object.DestroyImmediate(t);
        Check(name + ": every cell has art", any);
        Check(name + ": nothing within " + Margin + " px of a cell edge", margins);
        if (dispersing || name.Contains("_rock_")) return;
        Check(name + ": idle pivot steady (<= 3 / 5 px) and size steady (<= 14 x 8 px)",
              Ptp(cx, 4) <= 3f && Ptp(cy, 4) <= 5f && Ptp(w, 4) <= 14 && Ptp(ht, 4) <= 8);
        Check(name + ": every cell keeps the pivot (<= 6 / 9 px)", Ptp(cx, cells) <= 6f && Ptp(cy, cells) <= 9f);
    }

    static float Ptp(float[] a, int n) { float lo = a[0], hi = a[0]; for (int i = 1; i < n; i++) { lo = Mathf.Min(lo, a[i]); hi = Mathf.Max(hi, a[i]); } return hi - lo; }
    static float Ptp(int[] a, int n) { int lo = a[0], hi = a[0]; for (int i = 1; i < n; i++) { lo = Mathf.Min(lo, a[i]); hi = Mathf.Max(hi, a[i]); } return hi - lo; }
}
