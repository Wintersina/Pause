using System.IO;
using UnityEngine;

// STORM ELITES MUST NEVER BE CLIPPED. Every Storm elite strip (the five flight strips and their death strips) is measured cell by cell
// on its alpha: nothing may sit within 6 px of a cell edge, and the flight frames keep their framing: across the seven cells the
// bbox's horizontal centre drifts <= 12 px (banks lean the hull), its top edge <= 12 px (the bow stays put; lift-off jets only add art at the stern) and its
// width changes <= 40 px. Death strips disperse by design: edge margin only.
//
//   Unity -batchmode -quit -projectPath <abs>/Pause -executeMethod StormFramingTest.Run
public static class StormFramingTest
{
    static int fails;
    static void Check(string what, bool ok)
    {
        Debug.Log((ok ? "[STMF] PASS  " : "[STMF] FAIL  ") + what);
        if (!ok) fails++;
    }

    public static void Run() { TestHarness.Exit(Execute()); }

    const string Root = "Assets/Art/Resources/Elites/Storm/";
    const int Margin = 6;

    public static int Execute()
    {
        fails = 0;
        int n = 0;
        foreach (var f in Directory.GetFiles(Root, "storm_elite_*.png")) { Strip(f, f.EndsWith("_death.png")); n++; }
        Check("Storm elite strips measured (" + n + ")", n == 10);
        Debug.Log("[STMF] failures: " + fails);
        return fails;
    }

    static void Strip(string path, bool dispersing)
    {
        string name = Path.GetFileNameWithoutExtension(path);
        var t = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        t.LoadImage(File.ReadAllBytes(path));
        int h = t.height, cells = t.width / h;
        var px = t.GetPixels32();
        var cx = new float[cells]; var top = new float[cells]; var w = new float[cells];
        bool margins = true, any = true;
        for (int c = 0; c < cells; c++)
        {
            int x0 = int.MaxValue, y0 = int.MaxValue, x1 = -1, y1 = -1;
            for (int y = 0; y < h; y++)
                for (int x = 0; x < h; x++)
                    if (px[y * t.width + c * h + x].a > 8) { x0 = Mathf.Min(x0, x); x1 = Mathf.Max(x1, x); y0 = Mathf.Min(y0, y); y1 = Mathf.Max(y1, y); }
            if (x1 < 0) { any = false; continue; }
            if (Mathf.Min(Mathf.Min(x0, y0), Mathf.Min(h - 1 - x1, h - 1 - y1)) < Margin) { margins = false; Debug.Log("[STMF] " + name + " cell " + c + " bbox " + x0 + "," + y0 + "-" + x1 + "," + y1 + " touches the " + Margin + " px margin"); }
            cx[c] = (x0 + x1) * .5f; top[c] = h - 1 - y1; w[c] = x1 - x0;   // Unity rows count from the bottom: the bow is the highest row
        }
        Object.DestroyImmediate(t);
        Check(name + ": every cell has art", any);
        Check(name + ": nothing within " + Margin + " px of a cell edge", margins);
        if (dispersing) return;
        Check(name + ": every cell keeps the bow and the centre (<= 12 / 12 px) and the width (<= 40 px)", Ptp(cx) <= 12f && Ptp(top) <= 12f && Ptp(w) <= 40f);
    }

    static float Ptp(float[] a) { float lo = a[0], hi = a[0]; foreach (var v in a) { lo = Mathf.Min(lo, v); hi = Mathf.Max(hi, v); } return hi - lo; }
}
