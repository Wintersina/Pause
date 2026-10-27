using System.IO;
using UnityEngine;

// Ember smoke / steam / pall pieces must dissolve softly: no hard cut anywhere.
//
// The reported bug ("smoke clouds cut off by a straight line"): smoke_b, eruptsmoke_b and the
// weather palls were cropped by the generator, leaving a flat top row and straight-edged
// translucent "ghost" layers. This test scans every frame of the Ember smoke atlases and fails on
//   1. any pixel with alpha > 8 on the border of its atlas cell (art bleeding past the cell), and
//   2. any straight cut: a run of >= 14 pixels on one row / column whose alpha is >= 40 higher
//      than the pixel across it (a flat edge, not a feathered one). Plume feet (the chimney
//      mouth: bottom 34 rows, vertical stem sides in the lowest 80 rows) are exempt for pieces
//      that stand on something.
//
//   Unity -batchmode -quit -projectPath <abs>/Pause -executeMethod EmberSmokeCutTest.Run
public static class EmberSmokeCutTest
{
    static int fails;
    static void Check(string what, bool ok)
    {
        Debug.Log((ok ? "[SMOKECUT] PASS  " : "[SMOKECUT] FAIL  ") + what);
        if (!ok) fails++;
    }

    public static void Run() { TestHarness.Exit(Execute()); }

    const string Dir = "Assets/Art/Backgrounds/Resources/Worlds/Ember/Backdrop3/";
    const int Jump = 40, MinRun = 14, FootRows = 34, StemRows = 80, EdgeAlpha = 8;

    // atlas -> frame-name prefixes that are smoke-like
    static readonly string[][] Pieces =
    {
        new[] { "smoke", "smoke_a", "smoke_b" },
        new[] { "eruption", "eruptsmoke_a", "eruptsmoke_b" },
        new[] { "leaks", "steam_vent" },
        new[] { "weather", "cloud_wisp", "smokepall", "mist", "ashgust" },
    };
    static readonly string[] Feet = { "smoke_a", "smoke_b", "eruptsmoke", "steam_vent" };

    [System.Serializable] class R { public string n; public int x, y, w, h; }
    [System.Serializable] class M { public R[] sprites; }

    public static int Execute()
    {
        fails = 0;
        foreach (var p in Pieces) Atlas(p);
        Debug.Log("[SMOKECUT] failures: " + fails);
        return fails;
    }

    static bool StartsWithAny(string n, string[] prefixes, int from = 0)
    {
        for (int i = from; i < prefixes.Length; i++) if (n.StartsWith(prefixes[i])) return true;
        return false;
    }

    static void Atlas(string[] spec)
    {
        string png = Dir + spec[0] + ".png", json = Dir + spec[0] + ".json";
        if (!File.Exists(png) || !File.Exists(json)) { Check("Ember/" + spec[0] + " installed", false); return; }
        var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        tex.LoadImage(File.ReadAllBytes(png));
        var m = JsonUtility.FromJson<M>(File.ReadAllText(json));
        var px = tex.GetPixels32();
        int W = tex.width, frames = 0, bleed = 0, cuts = 0;
        string firstCut = "";
        foreach (var r in m.sprites)
        {
            if (!StartsWithAny(r.n, spec, 1)) continue;
            frames++;
            var a = new int[r.w * r.h];
            for (int y = 0; y < r.h; y++)
                for (int x = 0; x < r.w; x++)
                    a[y * r.w + x] = px[(r.y + (r.h - 1 - y)) * W + r.x + x].a; // json y is from the bottom
            if (Bleeds(a, r.w, r.h)) { bleed++; Debug.Log("[SMOKECUT] " + r.n + " art on the cell border"); }
            string cut = StraightCut(a, r.w, r.h, StartsWithAny(r.n, Feet));
            if (cut != "") { cuts++; if (firstCut == "") firstCut = r.n + " " + cut; Debug.Log("[SMOKECUT] " + r.n + " straight cut " + cut); }
        }
        Object.DestroyImmediate(tex);
        Check("Ember/" + spec[0] + ": " + frames + " smoke frames found", frames > 0);
        Check("Ember/" + spec[0] + ": no art on a cell border (" + bleed + " frames)", bleed == 0);
        Check("Ember/" + spec[0] + ": no straight hard cut (" + cuts + " frames" + (firstCut != "" ? ", first " + firstCut : "") + ")", cuts == 0);
    }

    static bool Bleeds(int[] a, int w, int h)
    {
        for (int x = 0; x < w; x++) if (a[x] > EdgeAlpha || a[(h - 1) * w + x] > EdgeAlpha) return true;
        for (int y = 0; y < h; y++) if (a[y * w] > EdgeAlpha || a[y * w + w - 1] > EdgeAlpha) return true;
        return false;
    }

    // First straight cut as "side@pos:start-end", or "".
    static string StraightCut(int[] a, int w, int h, bool foot)
    {
        // horizontal edges (top / bottom neighbour)
        for (int side = 0; side < 2; side++)
            for (int y = 0; y < h; y++)
            {
                if (foot && side == 1 && y >= h - FootRows) continue;
                int run = 0;
                for (int x = 0; x <= w; x++)
                {
                    bool hit = false;
                    if (x < w)
                    {
                        int ny = y + (side == 0 ? -1 : 1);
                        int nb = ny < 0 || ny >= h ? 0 : a[ny * w + x];
                        hit = a[y * w + x] >= Jump && a[y * w + x] - nb >= Jump;
                    }
                    if (hit) run++;
                    else { if (run >= MinRun) return (side == 0 ? "top" : "bottom") + "@" + y + ":" + (x - run) + "-" + (x - 1); run = 0; }
                }
            }
        // vertical edges (left / right neighbour)
        for (int side = 0; side < 2; side++)
            for (int x = 0; x < w; x++)
            {
                int run = 0;
                for (int y = 0; y <= h; y++)
                {
                    bool hit = false;
                    if (y < h)
                    {
                        int nx = x + (side == 0 ? -1 : 1);
                        int nb = nx < 0 || nx >= w ? 0 : a[y * w + nx];
                        hit = a[y * w + x] >= Jump && a[y * w + x] - nb >= Jump;
                    }
                    if (hit) run++;
                    else
                    {
                        if (run >= MinRun && !(foot && y - run >= h - StemRows)) return (side == 0 ? "left" : "right") + "@" + x + ":" + (y - run) + "-" + (y - 1);
                        run = 0;
                    }
                }
            }
        return "";
    }
}
