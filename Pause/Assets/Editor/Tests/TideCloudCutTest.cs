using System.Collections.Generic;
using System.IO;
using UnityEngine;

// TIDE CLOUDS ARE NEVER CUT, AND THE SEA TILES WRAP CLEAN. The Tide teal cloud ceiling (weather.png: cloud_bank, cloud_wisp, mist,
// gust, pall, rain) is cut from 256 px cells of a painted sheet. The sheet slicing left two defects that drew straight horizontal
// cut lines across the ceiling in game (BuildCeiling even had to push the rows off screen to hide one):
//   * a bank whose lower edge is flat, an opaque run far longer than any puff, and
//   * flat-topped slivers of the cloud ABOVE it in the sheet, left on the top of the next cell (wisps, mist).
// So: (1) per connected component of every cloud cell, no bounding-box edge holds a straight opaque run over FlatRun, and no
// small component rests on the cell margin; (2) every ground tile (sky, far, mid, flow x v1..v4) wraps: the join between its
// last row and its first is no harder than the tile's own row-to-row steps, and no row inside it is a hard horizontal cut.
//
//   Unity -batchmode -quit -projectPath <abs>/Pause -executeMethod TideCloudCutTest.Run
public static class TideCloudCutTest
{
    static int fails;
    static void Check(string what, bool ok)
    {
        Debug.Log((ok ? "[TCC] PASS  " : "[TCC] FAIL  ") + what);
        if (!ok) fails++;
    }

    public static void Run() { TestHarness.Exit(Execute()); }

    const string Dir = "Assets/Art/Backgrounds/Resources/Worlds/Tide/Backdrop3/";
    const int Border = 8, FlatRun = 20, Margin = 16, SpeckArea = 1500;
    const float StepRatio = 6f, JoinRatio = 2.5f;

    [System.Serializable] class Cell { public string n; public int x, y, w, h; }
    [System.Serializable] class Sheet { public Cell[] sprites; }

    public static int Execute()
    {
        fails = 0;
        Clouds();
        foreach (var v in new[] { "v1", "v2", "v3", "v4" })
            foreach (var l in new[] { "sky", "far", "mid", "flow" }) Tile(v, l);
        Debug.Log("[TCC] failures: " + fails);
        return fails;
    }

    static Texture2D Load(string png)
    {
        var tex = new Texture2D(2, 2);
        tex.LoadImage(File.ReadAllBytes(png));
        return tex;
    }

    // ---------------------------------------------------------------- clouds --

    static void Clouds()
    {
        string png = Dir + "weather.png";
        if (!File.Exists(png)) { Check("Tide weather: atlas exists", false); return; }
        var tex = Load(png);
        var px = tex.GetPixels32();
        int W = tex.width;
        var sheet = JsonUtility.FromJson<Sheet>(File.ReadAllText(Dir + "weather.json"));
        var bad = new List<string>();
        foreach (var c in sheet.sprites) Cut(px, W, c, bad);
        Object.DestroyImmediate(tex);
        Check("Tide weather: no cloud edge is cut flat and no sliver sits on a cell margin (" + (bad.Count == 0 ? "none" : string.Join("; ", bad)) + ")", bad.Count == 0);
    }

    // y runs down the cell; texture rows run bottom-up
    static bool Solid(Color32[] px, int W, Cell c, int x, int y) { return px[(c.y + c.h - 1 - y) * W + c.x + x].a > Border; }

    static void Cut(Color32[] px, int W, Cell c, List<string> bad)
    {
        var lab = new int[c.w * c.h];
        var stack = new Stack<int>();
        int next = 0;
        for (int s = 0; s < lab.Length; s++)
        {
            if (lab[s] != 0 || !Solid(px, W, c, s % c.w, s / c.w)) continue;
            next++;
            var pts = new List<int>();
            lab[s] = next; stack.Push(s);
            while (stack.Count > 0)
            {
                int p = stack.Pop(); pts.Add(p);
                int x = p % c.w, y = p / c.w;
                for (int dy = -1; dy <= 1; dy++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        int nx = x + dx, ny = y + dy;
                        if (nx < 0 || ny < 0 || nx >= c.w || ny >= c.h) continue;
                        int q = ny * c.w + nx;
                        if (lab[q] == 0 && Solid(px, W, c, nx, ny)) { lab[q] = next; stack.Push(q); }
                    }
            }
            int x0 = int.MaxValue, x1 = -1, y0 = int.MaxValue, y1 = -1;
            foreach (int p in pts) { int x = p % c.w, y = p / c.w; if (x < x0) x0 = x; if (x > x1) x1 = x; if (y < y0) y0 = y; if (y > y1) y1 = y; }
            if (pts.Count < 10) continue;
            int t = Run(lab, c, next, 0, y0, 1, 0), b = Run(lab, c, next, 0, y1, 1, 0), l = Run(lab, c, next, x0, 0, 0, 1), r = Run(lab, c, next, x1, 0, 0, 1);
            string at = c.n + " part " + pts.Count + "px";
            if (t > FlatRun) bad.Add(at + " top flat run " + t);
            if (b > FlatRun) bad.Add(at + " bottom flat run " + b);
            if (l > FlatRun) bad.Add(at + " left flat run " + l);
            if (r > FlatRun) bad.Add(at + " right flat run " + r);
            bool margin = y0 <= Margin;                       // the sheet's slivers hang from the top of the cell
            if (margin && pts.Count < SpeckArea) bad.Add(at + " is a sliver hanging from the cell top");
        }
    }

    // longest unbroken run of one component's pixels along the row (dx 1) or column (dy 1) through (x, y)
    static int Run(int[] lab, Cell c, int id, int x, int y, int dx, int dy)
    {
        int best = 0, cur = 0, n = dx == 1 ? c.w : c.h;
        for (int i = 0; i < n; i++)
        {
            cur = lab[(dy == 1 ? i : y) * c.w + (dx == 1 ? i : x)] == id ? cur + 1 : 0;
            if (cur > best) best = cur;
        }
        return best;
    }

    // ----------------------------------------------------------------- tiles --

    static void Tile(string v, string layer)
    {
        string png = Dir + v + "/" + layer + ".png";
        if (!File.Exists(png)) { Check("Tide " + v + "/" + layer + ": tile exists", false); return; }
        var tex = Load(png);
        var px = tex.GetPixels32();
        int W = tex.width, H = tex.height;
        var steps = new float[H];                  // steps[y] = mean |row y - row y+1| (the last entry wraps to row 0)
        for (int y = 0; y < H; y++)
        {
            int a = y * W, b = ((y + 1) % H) * W;
            float sum = 0;
            for (int x = 0; x < W; x++)
                sum += Mathf.Abs(px[a + x].r - px[b + x].r) + Mathf.Abs(px[a + x].g - px[b + x].g) + Mathf.Abs(px[a + x].b - px[b + x].b) + Mathf.Abs(px[a + x].a - px[b + x].a);
            steps[y] = sum / (4f * W);
        }
        Object.DestroyImmediate(tex);
        var sorted = (float[])steps.Clone();
        System.Array.Sort(sorted);
        float median = Mathf.Max(sorted[H / 2], 0.5f);
        float worst = 0; int at = 0;
        for (int y = 0; y < H - 1; y++) if (steps[y] > worst) { worst = steps[y]; at = y; }
        float join = steps[H - 1];
        Check("Tide " + v + "/" + layer + ": the wrap join (" + join.ToString("F1") + ") is as soft as its own steps (median " + median.ToString("F1") + ")", join <= median * JoinRatio);
        Check("Tide " + v + "/" + layer + ": no row is a hard cut (worst step " + worst.ToString("F1") + " at row " + at + ", median " + median.ToString("F1") + ")", worst <= median * StepRatio);
    }
}
