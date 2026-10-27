using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

// BACKDROP ART MUST BE REAL RESOLUTION. Desert's ground tiles and every Desert / Ember ground atlas shipped
// as half-resolution art nearest-neighbour doubled (every texel pair identical in 2x2 blocks), which draws
// as chunky blocks next to the finer worlds. Measured by the PIXEL-DUPLICATION test: an image (or, for the
// 1024 atlases, each 256 cell) is a k x k block upscale when, at some phase of the k grid, >= 97% of its
// opaque texels equal the first texel of their k x k block (k = 2, 3, 4). Real art scores <= ~0.86 there.
// Also: adjacent identical texel pairs stay under a ceiling (soft flat skies and cloud decks are far under
// it; the block upscales sit above it only in combination, so the block test is the real gate), and no
// backdrop texture is imported clamped below its own size (a max-size clamp is a hidden downscale).
//
// Tools: Art/Worlds/<World>/backdrop_v3~/src~/build_hires*.py rebuild atlases at true resolution.
//
//   Unity -batchmode -quit -projectPath <abs>/Pause -executeMethod BackdropResolutionTest.Run
public static class BackdropResolutionTest
{
    static int fails;
    static void Check(string what, bool ok)
    {
        Debug.Log((ok ? "[BRES] PASS  " : "[BRES] FAIL  ") + what);
        if (!ok) fails++;
    }

    public static void Run() { TestHarness.Exit(Execute()); }

    const string Root = "Assets/Art/Backgrounds/Resources/Worlds/";
    const float BlockFraction = 0.97f;        // fraction of opaque texels equal to their block's first texel
    const float PairCeiling = 0.85f;          // adjacent identical-texel pairs (opaque on at least one side)
    const int MinOpaque = 300;                // a cell with fewer opaque texels is too sparse to judge
    static readonly System.Text.RegularExpressions.Regex PixelGridGround = new System.Text.RegularExpressions.Regex(@"^Desert/Backdrop3/v[1234]/(sky|far|mid)\.png$");
    static readonly string[] Dirs = { "Backdrop3", "Backdrop" };
    static readonly string[] Worlds = { "Frost", "Verdant", "Ember", "Tide", "Desert", "Space", "Storm" };

    public static int Execute()
    {
        fails = 0;
        int n = 0;
        foreach (var w in Worlds)
            foreach (var d in Dirs)
            {
                string dir = Root + w + "/" + d;
                if (!Directory.Exists(dir)) continue;
                foreach (var png in Directory.GetFiles(dir, "*.png", SearchOption.AllDirectories))
                {
                    string path = png.Replace('\\', '/');
                    string rel = path.Substring(Root.Length);
                    // gate lane: variant 1 of each world (the v2..v4 folders / _2.._4 files are the other variant sets)
                    if (!TestHarness.FullLane && System.Text.RegularExpressions.Regex.IsMatch(rel, @"(/v[2-9]/|_[2-9]\.png$)")) continue;
                    Audit(path, rel);
                    n++;
                }
            }
        Check("audited at least 150 backdrop textures (" + n + ")", n >= (TestHarness.FullLane ? 150 : 60));
        Debug.Log("[BRES] failures: " + fails);
        return fails;
    }

    static void Audit(string path, string rel)
    {
        var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        tex.LoadImage(File.ReadAllBytes(path));
        int W = tex.width, H = tex.height;
        var px = tex.GetPixels32();
        Object.DestroyImmediate(tex);

        // atlases (square 1024 sheets of 256 cells) are judged cell by cell; tiles as a whole
        bool cells = W >= 1024 && H >= 1024 && W % 256 == 0 && H % 256 == 0 && !rel.Contains("worm") && !rel.Contains("anim") && !rel.Contains("reference") && !rel.Contains("station");
        int cw = cells ? 256 : W, ch = cells ? 256 : H;
        int worstK = 0; float worst = 0;
        for (int cy = 0; cy < H; cy += ch)
            for (int cx = 0; cx < W; cx += cw)
                for (int k = 2; k <= 4; k++)
                {
                    float s = BlockScore(px, W, cx, cy, cw, ch, k);
                    if (s > worst) { worst = s; worstK = k; }
                }
        string label = rel + ": " + (worst > 0 ? "block score " + worst.ToString("0.000") + " (k=" + worstK + ")" : "too sparse to judge");
        if (PixelGridGround.IsMatch(rel))
        {
            // 2026-10: the painted Desert ground tiles are deliberate PIXEL ART on a 4 px cell grid (build_tiles.py pixel pass), so the
            // 2/3/4 block test does not apply. They must really be that grid (k=4 >= .97) and have real detail AT the cell level (8 px
            // blocks are not uniform), so a plain 2x or 4x upscale of a small image still fails here.
            float g4 = BlockScore(px, W, 0, 0, W, H, 4), g8 = BlockScore(px, W, 0, 0, W, H, 8);
            Check(label + " is a deliberate 4 px pixel grid (k=4 " + g4.ToString("0.000") + " >= " + BlockFraction + ") with cell-level detail (k=8 " + g8.ToString("0.000") + " < .5)",
                  g4 >= BlockFraction && g8 < .5f);
        }
        else
            Check(label + " is not a nearest-neighbour block upscale", worst < BlockFraction);

        float pairs = PairDup(px, W, H);
        Check(rel + ": identical adjacent texel pairs " + pairs.ToString("0.00") + " <= " + PairCeiling, pairs <= PairCeiling);

        var imp = AssetImporter.GetAtPath(path) as TextureImporter;
        if (imp != null)
            Check(rel + ": import max size " + imp.maxTextureSize + " does not clamp the " + W + "x" + H + " texture by more than ~20%", imp.maxTextureSize * 1.3f >= System.Math.Max(W, H));
    }

    // Best fraction (over the k*k grid phases) of opaque texels equal to their k x k block's first texel.
    public static float BlockScore(Color32[] px, int W, int x0, int y0, int w, int h, int k)
    {
        int opaque = 0;
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                if (px[(y0 + y) * W + x0 + x].a > 8) opaque++;
        if (opaque < MinOpaque) return 0;
        float best = 0;
        for (int oy = 0; oy < k; oy++)
            for (int ox = 0; ox < k; ox++)
            {
                int eq = 0, tot = 0;
                for (int by = oy; by + k <= h; by += k)
                    for (int bx = ox; bx + k <= w; bx += k)
                    {
                        var r = px[(y0 + by) * W + x0 + bx];
                        for (int j = 0; j < k; j++)
                            for (int i = 0; i < k; i++)
                            {
                                var c = px[(y0 + by + j) * W + x0 + bx + i];
                                if (c.a <= 8) continue;
                                tot++;
                                if (c.r == r.r && c.g == r.g && c.b == r.b && c.a == r.a) eq++;
                            }
                    }
                if (tot > 0) best = Mathf.Max(best, eq / (float)tot);
            }
        return best;
    }

    public static float PairDup(Color32[] px, int W, int H)
    {
        long same = 0, tot = 0;
        for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                var a = px[y * W + x];
                if (x + 1 < W)
                {
                    var b = px[y * W + x + 1];
                    if (a.a > 8 || b.a > 8) { tot++; if (a.r == b.r && a.g == b.g && a.b == b.b && a.a == b.a) same++; }
                }
                if (y + 1 < H)
                {
                    var b = px[(y + 1) * W + x];
                    if (a.a > 8 || b.a > 8) { tot++; if (a.r == b.r && a.g == b.g && a.b == b.b && a.a == b.a) same++; }
                }
            }
        return tot == 0 ? 0 : same / (float)tot;
    }
}
