using System.IO;
using UnityEngine;

// DESERT ELITES MUST NEVER BE CLIPPED (the EmberFramingTest approach on the 384 px cells). Every installed Desert elite strip (six flight strips,
// six death strips) is measured cell by cell on its alpha: nothing may sit within 6 px of a cell edge (no cut-off wing, claw, barrel or flame),
// and the flying cells (hover, bank left, bank right, damaged) keep their framing: the bbox centre drifts <= 14 px sideways (a bank swings the
// wings) / 8 px vertically, and the bbox size changes <= 24 x 12 px. Parked / idle / ignition cells and death strips are checked for the margin
// only (the ignition jets are long on purpose, the death strip disperses by design).
//
//   Unity -batchmode -quit -projectPath <abs>/Pause -executeMethod DesertFramingTest.Run
public static class DesertFramingTest
{
    static int fails;
    static void Check(string what, bool ok)
    {
        Debug.Log((ok ? "[DESF] PASS  " : "[DESF] FAIL  ") + what);
        if (!ok) fails++;
    }

    public static void Run() { TestHarness.Exit(Execute()); }

    const string Dir = "Assets/Art/Resources/Elites/Desert/";
    const int Margin = 6;

    public static int Execute()
    {
        fails = 0;
        int n = 0;
        foreach (var f in Directory.GetFiles(Dir, "desert_elite_*.png")) { Strip(f, f.EndsWith("_death.png")); n++; }
        Check("Desert elite strips measured (" + n + ")", n == 12);
        Debug.Log("[DESF] failures: " + fails);
        return fails;
    }

    static void Strip(string path, bool death)
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
            if (Mathf.Min(Mathf.Min(x0, y0), Mathf.Min(h - 1 - x1, h - 1 - y1)) < Margin) { margins = false; Debug.Log("[DESF] " + name + " cell " + c + " bbox " + x0 + "," + y0 + "-" + x1 + "," + y1 + " is within " + Margin + " px of the cell border"); }
            cx[c] = (x0 + x1) * .5f; cy[c] = (y0 + y1) * .5f; w[c] = x1 - x0; ht[c] = y1 - y0;
        }
        Object.DestroyImmediate(t);
        Check(name + ": every cell has art", any);
        Check(name + ": nothing within " + Margin + " px of a cell edge", margins);
        if (death || cells < 7) return;
        Check(name + ": flying cells keep the pivot (<= 14 / 8 px) and the size (<= 24 x 12 px)",
              Ptp(cx, 3, 6) <= 14f && Ptp(cy, 3, 5) <= 8f && Ptp(w, 3, 6) <= 24 && Ptp(ht, 3, 5) <= 12);
        // (the damaged cell, 6, is a wreck with sputtering flames of its own length: its vertical extent is free, its sideways pivot and width are not)
    }

    static float Ptp(float[] a, int from, int to) { float lo = a[from], hi = a[from]; for (int i = from; i <= to; i++) { lo = Mathf.Min(lo, a[i]); hi = Mathf.Max(hi, a[i]); } return hi - lo; }
    static float Ptp(int[] a, int from, int to) { int lo = a[from], hi = a[from]; for (int i = from; i <= to; i++) { lo = Mathf.Min(lo, a[i]); hi = Mathf.Max(hi, a[i]); } return hi - lo; }
}
