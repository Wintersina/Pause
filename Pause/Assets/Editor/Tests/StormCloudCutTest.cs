using System.Collections.Generic;
using System.IO;
using UnityEngine;

// STORM CLOUDS ARE NEVER CUT. Storm's thunderheads (landmarks anvil_tower_*), their lightning layers (thunder) and the weather
// clouds are painted pieces cut from 256 px atlas cells. Two defects drew a thunderhead with a flat, straight edge in game:
//   * art that runs into the cell border (the sprite rect then slices it), and
//   * art cut flat BEFORE it reached the cell: build_lm_haz.py once sliced Codex's 2x2 painted sheet on the grid line and a cloud
//     whose wisps crossed the line came out with a dead-straight left/right edge.
// So: (1) no cell of the Storm landmark / thunder / lightning / weather / sites / rotors atlases has an opaque pixel on its border,
// and (2) the outermost row/column of a cloud or landmark drawing never holds a long straight opaque run (a soft edge is ragged).
//
//   Unity -batchmode -quit -projectPath <abs>/Pause -executeMethod StormCloudCutTest.Run
public static class StormCloudCutTest
{
    static int fails;
    static void Check(string what, bool ok)
    {
        Debug.Log((ok ? "[SCC] PASS  " : "[SCC] FAIL  ") + what);
        if (!ok) fails++;
    }

    public static void Run() { TestHarness.Exit(Execute()); }

    const string Dir = "Assets/Art/Backgrounds/Resources/Worlds/Storm/Backdrop3/";
    const int Border = 8, FlatRun = 20;
    static readonly string[] BorderAtlases = { "landmarks", "thunder", "lightning", "weather", "sites", "rotors" };
    static readonly string[] CutAtlases = { "landmarks", "thunder", "weather" };

    [System.Serializable] class Cell { public string n; public int x, y, w, h; }
    [System.Serializable] class Sheet { public Cell[] sprites; }

    public static int Execute()
    {
        fails = 0;
        foreach (var a in BorderAtlases) Scan(a, false);
        foreach (var a in CutAtlases) Scan(a, true);
        Debug.Log("[SCC] failures: " + fails);
        return fails;
    }

    static void Scan(string atlas, bool flat)
    {
        string png = Dir + atlas + ".png";
        if (!File.Exists(png)) { Check("Storm " + atlas + ": atlas exists", false); return; }
        var tex = new Texture2D(2, 2);
        tex.LoadImage(File.ReadAllBytes(png));
        var px = tex.GetPixels32();
        int W = tex.width;
        var sheet = JsonUtility.FromJson<Sheet>(File.ReadAllText(Dir + atlas + ".json"));
        var bad = new List<string>();
        foreach (var c in sheet.sprites)
        {
            if (flat && atlas == "weather" && !(c.n.StartsWith("cloud_") || c.n.StartsWith("pall"))) continue;
            string s = flat ? FlatEdge(px, W, c) : BorderPixel(px, W, c);
            if (s != null) bad.Add(c.n + " " + s);
        }
        Object.DestroyImmediate(tex);
        Check("Storm " + atlas + (flat ? ": no cloud / landmark edge is cut flat (" : ": no cell has art on its border (") + (bad.Count == 0 ? "none" : string.Join("; ", bad)) + ")", bad.Count == 0);
    }

    // y runs down the cell; texture rows run bottom-up
    static byte A(Color32[] px, int W, Cell c, int x, int y) { return px[(c.y + c.h - 1 - y) * W + c.x + x].a; }

    static string BorderPixel(Color32[] px, int W, Cell c)
    {
        for (int i = 0; i < c.w; i++)
            if (A(px, W, c, i, 0) > Border) return "top x" + i;
            else if (A(px, W, c, i, c.h - 1) > Border) return "bottom x" + i;
        for (int j = 0; j < c.h; j++)
            if (A(px, W, c, 0, j) > Border) return "left y" + j;
            else if (A(px, W, c, c.w - 1, j) > Border) return "right y" + j;
        return null;
    }

    static string FlatEdge(Color32[] px, int W, Cell c)
    {
        int x0 = int.MaxValue, x1 = -1, y0 = int.MaxValue, y1 = -1;
        for (int y = 0; y < c.h; y++)
            for (int x = 0; x < c.w; x++)
                if (A(px, W, c, x, y) > Border) { if (x < x0) x0 = x; if (x > x1) x1 = x; if (y < y0) y0 = y; if (y > y1) y1 = y; }
        if (x1 < 0) return null;
        int l = Run(px, W, c, x0, 0, 0, 1), r = Run(px, W, c, x1, 0, 0, 1), t = Run(px, W, c, 0, y0, 1, 0), b = Run(px, W, c, 0, y1, 1, 0);
        if (l > FlatRun) return "left edge flat run " + l;
        if (r > FlatRun) return "right edge flat run " + r;
        if (t > FlatRun) return "top edge flat run " + t;
        if (b > FlatRun) return "bottom edge flat run " + b;
        return null;
    }

    // longest unbroken run of mostly-opaque pixels along the column (dx 0, dy 1) or row (dx 1, dy 0) through (x, y)
    static int Run(Color32[] px, int W, Cell c, int x, int y, int dx, int dy)
    {
        int best = 0, cur = 0, n = dy == 1 ? c.h : c.w;
        for (int i = 0; i < n; i++)
        {
            cur = A(px, W, c, x + dx * i, y + dy * i) > 128 ? cur + 1 : 0;
            if (cur > best) best = cur;
        }
        return best;
    }
}
