using System.IO;
using UnityEngine;

// TIDE / STORM REGULAR SHIPS NEED REAL PAINTED POSES. Every tide_* / storm_* enemy strip except rocks (alien, chaser, big, fighter_1-4;
// mines are separate sprites) is 7 cells: 0-3 idle, 4-5 tell, 6 hit. Checked on the alpha / colour of the installed strips:
//  - nothing within 6 px of a cell edge in any cell;
//  - the body stays on one anchor: bbox centre of all 7 cells within 8 px sideways / 12 px vertically;
//  - the two tell cells and the hit cell are genuine poses, not the idle plus a glint: each differs from idle cell 0 on >= 6 % of the
//    union of its pixels (colour change > 40 or an alpha difference) after the best +-3 px registration, and the three differ from
//    each other (>= 4 %);
//  - the four idle cells are four distinct drawings (>= 1.5 % apart).
//
//   Unity -batchmode -quit -projectPath <abs>/Pause/Assets -executeMethod TideStormFramesTest.Run
public static class TideStormFramesTest
{
    static int fails;
    static void Check(string what, bool ok)
    {
        Debug.Log((ok ? "[TSFR] PASS  " : "[TSFR] FAIL  ") + what);
        if (!ok) fails++;
    }

    public static void Run() { TestHarness.Exit(Execute()); }

    const string Root = "Assets/Art/Resources/Enemies/";
    const int Margin = 6;
    static readonly string[] Kinds = { "alien", "chaser", "big", "fighter_1", "fighter_2", "fighter_3", "fighter_4" };

    public static int Execute()
    {
        fails = 0;
        int n = 0;
        foreach (var world in new[] { "tide", "storm" })
            foreach (var k in Kinds) { Strip(world + "_" + k); n++; }
        Check("Tide/Storm regular strips measured (" + n + ")", n == 14);
        Debug.Log("[TSFR] failures: " + fails);
        return fails;
    }

    static void Strip(string name)
    {
        var t = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        t.LoadImage(File.ReadAllBytes(Root + name + ".png"));
        int h = t.height, cells = t.width / h;
        Check(name + ": 7 square cells", cells == 7 && t.width == h * 7);
        var px = t.GetPixels32();
        var cx = new float[cells]; var cy = new float[cells];
        bool margins = true;
        for (int c = 0; c < cells; c++)
        {
            int x0 = int.MaxValue, y0 = int.MaxValue, x1 = -1, y1 = -1;
            for (int y = 0; y < h; y++)
                for (int x = 0; x < h; x++)
                    if (px[y * t.width + c * h + x].a > 8) { x0 = Mathf.Min(x0, x); x1 = Mathf.Max(x1, x); y0 = Mathf.Min(y0, y); y1 = Mathf.Max(y1, y); }
            if (x1 < 0) { margins = false; continue; }
            if (Mathf.Min(Mathf.Min(x0, y0), Mathf.Min(h - 1 - x1, h - 1 - y1)) < Margin) { margins = false; Debug.Log("[TSFR] " + name + " cell " + c + " touches the " + Margin + " px margin"); }
            cx[c] = (x0 + x1) * .5f; cy[c] = (y0 + y1) * .5f;
        }
        Check(name + ": nothing within " + Margin + " px of a cell edge", margins);
        Check(name + ": every cell keeps the body anchor (<= 8 / 12 px)", Ptp(cx) <= 8f && Ptp(cy) <= 12f);

        float tellA = Diff(px, t.width, h, 0, 4), tellB = Diff(px, t.width, h, 0, 5), hit = Diff(px, t.width, h, 0, 6);
        Check(name + ": tell A is a real pose (" + tellA.ToString("0.000") + " >= .06)", tellA >= .06f);
        Check(name + ": tell B is a real pose (" + tellB.ToString("0.000") + " >= .06)", tellB >= .06f);
        Check(name + ": hit is a real recoil pose (" + hit.ToString("0.000") + " >= .06)", hit >= .06f);
        Check(name + ": tell A, tell B and hit differ from each other", Diff(px, t.width, h, 4, 5) >= .04f && Diff(px, t.width, h, 4, 6) >= .04f && Diff(px, t.width, h, 5, 6) >= .04f);
        bool idle = true;
        for (int a = 0; a < 4; a++) for (int b = a + 1; b < 4; b++) if (Diff(px, t.width, h, a, b) < .015f) idle = false;
        Check(name + ": four distinct idle drawings", idle);
        Object.DestroyImmediate(t);
    }

    // Share of the pixel union that differs (alpha or colour), best of +-3 px shifts of cell b.
    static float Diff(Color32[] px, int W, int h, int a, int b)
    {
        float best = 9f;
        for (int dy = -3; dy <= 3; dy += 3)
            for (int dx = -3; dx <= 3; dx += 3)
            {
                int uni = 0, d = 0;
                for (int y = 0; y < h; y++)
                    for (int x = 0; x < h; x++)
                    {
                        int xb = x + dx, yb = y + dy;
                        var pa = px[y * W + a * h + x];
                        Color32 pb = (xb < 0 || yb < 0 || xb >= h || yb >= h) ? new Color32(0, 0, 0, 0) : px[yb * W + b * h + xb];
                        bool ma = pa.a > 40, mb = pb.a > 40;
                        if (!ma && !mb) continue;
                        uni++;
                        if (ma != mb || Mathf.Max(Mathf.Abs(pa.r - pb.r), Mathf.Max(Mathf.Abs(pa.g - pb.g), Mathf.Abs(pa.b - pb.b))) > 40) d++;
                    }
                best = Mathf.Min(best, uni == 0 ? 0f : (float)d / uni);
            }
        return best;
    }

    static float Ptp(float[] a) { float lo = a[0], hi = a[0]; foreach (var v in a) { lo = Mathf.Min(lo, v); hi = Mathf.Max(hi, v); } return hi - lo; }
}
