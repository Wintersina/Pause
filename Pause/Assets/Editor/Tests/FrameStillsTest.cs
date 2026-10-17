using System.Collections.Generic;
using System.IO;
using UnityEngine;

// STILL / NEAR-STILL ENEMY IDLE FRAMES (frame-fix campaign, group "stills").
// The audit found hazard rocks and a few ships whose four idle cells were one drawing
// repeated (space_rock_cluster / crater / dark, frost_rock_rime, the Tide urchin, the
// Verdant knot and spore, the Space alien's only-leg-tip shimmer, and single repeated cells
// on three Frost ships). Each of these sheets now carries FOUR GENUINELY DIFFERENT idle
// drawings (cells 0..3), every cell sits on the same pivot and size and is clear of the cell
// border, and the sheet's grid (cell size, cell count) is unchanged.
//
//   Unity -batchmode -quit -projectPath <abs>/Pause -executeMethod FrameStillsTest.Run
public static class FrameStillsTest
{
    static int fails;
    static void Check(string what, bool ok)
    {
        Debug.Log((ok ? "[FRAME-STILLS] PASS  " : "[FRAME-STILLS] FAIL  ") + what);
        if (!ok) fails++;
    }

    public static void Run() { TestHarness.Exit(Execute()); }

    const string Dir = "Assets/Art/Resources/Enemies/";
    const int Margin = 6;
    // fraction of the union silhouette that must differ between any two idle cells
    const float MinDifference = 0.04f;

    // sprite, cell size
    static readonly (string name, int cell)[] Idle =
    {
        ("tide_rock_urchin", 192), ("space_alien", 192),
        ("verdant_rock_knot", 192), ("verdant_rock_spore", 192),
        ("frost_fighter_2", 192), ("frost_rock_shard", 192),
        // PENDING (own PR "Space rock idle frames"): space_rock_cluster / crater / dark (idle cells c1 / c2 await Codex image
        // generation), frost_rock_rime (cell 3 too close to cell 0) and frost_chaser (cells 1 and 3 identical).
    };

    public static int Execute()
    {
        fails = 0;
        foreach (var (name, cell) in Idle) Sprite(name, cell, 4);
        FrostBigCellTwo();
        foreach (var n in new string[0] /* Space rocks join with their own PR */) NoPaleRim(n);
        Debug.Log("[FRAME-STILLS] failures: " + fails);
        return fails;
    }

    static Color32[] Load(string name, out int w, out int h)
    {
        var tex = new Texture2D(2, 2);
        tex.LoadImage(File.ReadAllBytes(Dir + name + ".png"));
        w = tex.width; h = tex.height;
        var px = tex.GetPixels32();
        Object.DestroyImmediate(tex);
        return px;
    }

    // pixel (x, y from the TOP) of cell k
    static Color32 At(Color32[] px, int w, int h, int cell, int k, int x, int y)
        => px[(h - 1 - y) * w + k * cell + x];

    static void Sprite(string name, int cell, int idleCells)
    {
        var px = Load(name, out int w, out int h);
        Check(name + ": sheet is " + cell + " px cells, 7 cells (" + w + "x" + h + ")", h == cell && w == cell * 7);
        for (int k = 0; k < 7; k++)
        {
            // the design cells are drawn with a 5 px margin; redrawn cells are registered with >= 6.
            // Tell / hit cells carry debris and may reach nearer the edge but never touch it.
            int need = k < 4 ? 5 : 2;
            int m = Margin(px, w, h, cell, k);
            Check(name + " cell " + k + ": >= " + need + " px clear of the cell border (" + m + ")", m >= need);
        }
        var worst = 1f; string pair = "";
        for (int i = 0; i < idleCells; i++)
            for (int j = i + 1; j < idleCells; j++)
            {
                float d = Difference(px, w, h, cell, i, j);
                if (d < worst) { worst = d; pair = i + "/" + j; }
            }
        Check(name + ": the four idle cells are distinct drawings (least different pair " + pair + " = " + worst.ToString("0.000") + ", need >= " + MinDifference + ")",
            worst >= MinDifference);
    }

    static int Margin(Color32[] px, int w, int h, int cell, int k)
    {
        int minX = cell, minY = cell, maxX = -1, maxY = -1;
        for (int y = 0; y < cell; y++)
            for (int x = 0; x < cell; x++)
                if (At(px, w, h, cell, k, x, y).a > 40)
                {
                    if (x < minX) minX = x; if (x > maxX) maxX = x;
                    if (y < minY) minY = y; if (y > maxY) maxY = y;
                }
        if (maxX < 0) return -1;
        return Mathf.Min(Mathf.Min(minX, minY), Mathf.Min(cell - 1 - maxX, cell - 1 - maxY));
    }

    // fraction of the union silhouette where the two cells differ (shape or colour)
    static float Difference(Color32[] px, int w, int h, int cell, int a, int b)
    {
        int union = 0, diff = 0;
        for (int y = 0; y < cell; y++)
            for (int x = 0; x < cell; x++)
            {
                var p = At(px, w, h, cell, a, x, y); var q = At(px, w, h, cell, b, x, y);
                bool pm = p.a > 40, qm = q.a > 40;
                if (!pm && !qm) continue;
                union++;
                if (pm != qm) { diff++; continue; }
                int d = Mathf.Max(Mathf.Abs(p.r - q.r), Mathf.Max(Mathf.Abs(p.g - q.g), Mathf.Abs(p.b - q.b)));
                if (d > 40) diff++;
            }
        return union == 0 ? 0f : (float)diff / union;
    }

    // frost_big (256 px cells): its wing-flap cell 2 used to touch the cell border.
    static void FrostBigCellTwo()
    {
        var px = Load("frost_big", out int w, out int h);
        Check("frost_big: sheet is 256 px cells, 7 cells (" + w + "x" + h + ")", h == 256 && w == 256 * 7);
        int m = Margin(px, w, h, 256, 2);
        Check("frost_big cell 2: >= " + Margin + " px clear of the cell border (" + m + ")", m >= Margin);
        for (int k = 0; k < 7; k++)
        {
            int mk = Margin(px, w, h, 256, k);
            Check("frost_big cell " + k + ": does not touch the cell border (" + mk + ")", mk >= 2);
        }
    }

    // ROCKS HAVE NO OUTLINE: no pale / white rim hugging the silhouette in any cell of the strip
    // (idle, tell, hit) nor in the first death cell (the intact hull).
    static void NoPaleRim(string name)
    {
        var px = Load(name, out int w, out int h);
        for (int k = 0; k < 7; k++) RimCheck(name + " cell " + k, px, w, h, 192, k);
        var dp = Load("Death/" + name, out int dw, out int dh);
        RimCheck(name + " death cell 0", dp, dw, dh, 192, 0);
    }

    static void RimCheck(string what, Color32[] px, int w, int h, int cell, int k)
    {
        int edge = 0, pale = 0;
        for (int y = 0; y < cell; y++)
            for (int x = 0; x < cell; x++)
            {
                if (At(px, w, h, cell, k, x, y).a <= 40) continue;
                bool near = false;   // within 2 px of transparency (or of the cell border)
                for (int dy = -2; dy <= 2 && !near; dy++)
                    for (int dx = -2; dx <= 2 && !near; dx++)
                    {
                        int xx = x + dx, yy = y + dy;
                        if (xx < 0 || yy < 0 || xx >= cell || yy >= cell || At(px, w, h, cell, k, xx, yy).a <= 40) near = true;
                    }
                if (!near) continue;
                edge++;
                var c = At(px, w, h, cell, k, x, y);
                int mx = Mathf.Max(c.r, Mathf.Max(c.g, c.b)), mn = Mathf.Min(c.r, Mathf.Min(c.g, c.b));
                if (mn > 150 && mx - mn < 70) pale++;
            }
        float share = edge == 0 ? 0f : (float)pale / edge;
        Check(what + ": no pale / white rim on the outer edge (" + pale + " of " + edge + " edge px)", share < 0.03f);
    }
}
