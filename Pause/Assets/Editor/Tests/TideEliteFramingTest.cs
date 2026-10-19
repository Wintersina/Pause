using System.IO;
using UnityEngine;

// TIDE ELITE SPRITES MUST NEVER BE CLIPPED OR JUMP. Every installed Tide elite strip (the 7-cell flight strips and their 3-cell death strips) is
// measured cell by cell on its alpha: nothing may sit within 6 px of a cell edge, and the flight strips keep their framing: the hull's nose
// (bbox top) stays within 6 px and the horizontal centre within 6 px across all seven cells (the flames hang below, so only the top and the
// sides are compared; the banks shear the hull a little), and the hull width (cells 0, 1, 2, 3, 6) varies <= 10 px. Same approach as
// EmberFramingTest. Death strips (which disperse by design) are checked for the edge margin only.
//
//   Unity -batchmode -quit -projectPath <abs>/Pause -executeMethod TideEliteFramingTest.Run
public static class TideEliteFramingTest
{
    static int fails;
    static void Check(string what, bool ok)
    {
        Debug.Log((ok ? "[TEF] PASS  " : "[TEF] FAIL  ") + what);
        if (!ok) fails++;
    }

    public static void Run() { TestHarness.Exit(Execute()); }

    const string Dir = "Assets/Art/Resources/Elites/Tide/";
    const int Margin = 6;

    public static int Execute()
    {
        fails = 0;
        int n = 0;
        // (the hull strips and their death strips; the _lights / _damaged companions are additive overlays and the burn loop, TideDockTest's)
        foreach (var f in Directory.GetFiles(Dir, "tide_elite_*.png"))
        {
            if (f.EndsWith("_lights.png") || f.EndsWith("_damaged.png") || f.EndsWith("_lights_damaged.png")) continue;
            Strip(f, f.EndsWith("_death.png")); n++;
        }
        Check("Tide elite strips measured (" + n + ")", n >= 10);
        Debug.Log("[TEF] failures: " + fails);
        return fails;
    }

    static void Strip(string path, bool dispersing)
    {
        string name = Path.GetFileNameWithoutExtension(path);
        var t = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        t.LoadImage(File.ReadAllBytes(path));
        int h = t.height, cells = t.width / h;
        var px = t.GetPixels32();
        var cx = new float[cells]; var top = new float[cells]; var w = new int[cells];
        bool margins = true, any = true;
        for (int c = 0; c < cells; c++)
        {
            int x0 = int.MaxValue, y0 = int.MaxValue, x1 = -1, y1 = -1;
            for (int y = 0; y < h; y++)
                for (int x = 0; x < h; x++)
                    if (px[y * t.width + c * h + x].a > 8) { x0 = Mathf.Min(x0, x); x1 = Mathf.Max(x1, x); y0 = Mathf.Min(y0, y); y1 = Mathf.Max(y1, y); }
            if (x1 < 0) { any = false; continue; }
            if (Mathf.Min(Mathf.Min(x0, y0), Mathf.Min(h - 1 - x1, h - 1 - y1)) < Margin) { margins = false; Debug.Log("[TEF] " + name + " cell " + c + " bbox " + x0 + "," + y0 + "-" + x1 + "," + y1 + " is within the " + Margin + " px margin"); }
            cx[c] = (x0 + x1) * .5f; top[c] = h - 1 - y1; w[c] = x1 - x0;   // Texture2D rows run bottom-up: the nose is the largest y
        }
        Object.DestroyImmediate(t);
        Check(name + ": every cell has art", any);
        Check(name + ": nothing within " + Margin + " px of a cell edge", margins);
        if (dispersing || !any) return;
        // the nose: the top of the bbox, measured from the top row
        Check(name + ": the nose stays put across the cells (<= 6 px)", Ptp(top, cells) <= 6f);
        Check(name + ": the hull keeps its horizontal pivot across the cells (<= 6 px)", Ptp(cx, cells) <= 6f);
        var hw = new float[] { w[0], w[1], w[2], w[3], w[cells - 1] };
        Check(name + ": hull width steady across the flat cells (<= 10 px)", Ptp(hw, hw.Length) <= 10f);
    }

    static float Ptp(float[] a, int n) { float lo = a[0], hi = a[0]; for (int i = 1; i < n; i++) { lo = Mathf.Min(lo, a[i]); hi = Mathf.Max(hi, a[i]); } return hi - lo; }
}
