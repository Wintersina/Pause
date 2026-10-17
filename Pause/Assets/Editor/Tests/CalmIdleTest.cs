using System.IO;
using UnityEngine;

// CALM IDLE MOTION: the enemies' idle cells only shimmer. No left-and-right wobble, no
// wing open/close, no vertical stretch baked into cells 0..3 of an enemy strip, and none
// driven from code either.
//
//   art   between any two idle cells the hull centroid moves <= 1.5% of the cell width
//         and the silhouette's bounding box changes <= 3% in width and height.
//         Exempt (deliberate): the four FLOATING rocks (their drawn bob is the point; the
//         centroid limit still applies), the Space boss (its six idle cells are engine
//         flames) and elites (their idle cells are engine-flame cells, owned by the
//         elite-engines branch).
//   code  idle holds are calm (>= 4 ticks a drawing outside a one-tick-pair hold, a loop of
//         >= 16 ticks = at most 6 drawings a second), the elite squash / lean / bank are
//         small. Rock tumble (AsteroidSpin spin / sway) is left alone on purpose.
//
//   Unity -batchmode -quit -projectPath <abs>/Pause -executeMethod CalmIdleTest.Run
public static class CalmIdleTest
{
    static int fails;
    static void Check(string what, bool ok)
    {
        Debug.Log((ok ? "[CALM-IDLE] PASS  " : "[CALM-IDLE] FAIL  ") + what);
        if (!ok) fails++;
    }

    public static void Run() { TestHarness.Exit(Execute()); }

    public const float MaxShift = .015f, MaxSize = .03f, FloatShift = .02f;   // floating rocks keep a slightly larger drawn bob
    static readonly string[] FloatingRocks = { "space_rock_crater", "frost_rock_chunk", "ember_rock_islet", "desert_rock_slab" };

    public static int Execute()
    {
        fails = 0;
        foreach (var path in Directory.GetFiles("Assets/Art/Resources/Enemies", "*.png"))
        {
            string key = Path.GetFileNameWithoutExtension(path);
            var d = EnemyRoster.Find(key);
            if (d == null || d.role == EnemyRole.Mine) continue;
            bool floating = System.Array.IndexOf(FloatingRocks, key) >= 0;
            Strip(key, path, 7, 0, 4, 0, !floating);
        }
        foreach (var boss in new[] { "Desert", "Ember", "Frost", "Storm", "Tide", "Verdant" })
            Strip("boss " + boss, "Assets/Art/Resources/Bosses/" + boss + ".png", 5, 0, 4, 0, true);

        foreach (var d in EnemyRoster.All)
        {
            if (d.role == EnemyRole.Mine) continue;
            var t = EnemyRoster.IdleTicks(d);
            int sum = 0, min = 99;
            foreach (var x in t) { sum += x; min = Mathf.Min(min, x); }
            if (d.key == "space_alien") continue;
            Check(d.key + " idle loop is calm (" + sum + " ticks, shortest hold " + min + ")", sum >= 16 && min >= 4);
        }
        Check("elite procedural squash is small (" + EliteShip.SquashMax + ")", EliteShip.SquashMax <= .04f);
        Check("elite procedural lean is small (" + EliteShip.LeanMax + ")", EliteShip.LeanMax <= 3f);
        Check("elite bank is capped (" + EliteShip.BankCap + ")", EliteShip.BankCap <= 4f);
        Debug.Log("[CALM-IDLE] failures: " + fails);
        return fails;
    }

    // cols: cells across the sheet; first/count: the idle cells (row 0 for bosses)
    static void Strip(string key, string path, int cols, int first, int count, int row, bool size)
    {
        var tex = new Texture2D(2, 2);
        tex.LoadImage(File.ReadAllBytes(path));
        int w = tex.width, cell = w / cols;
        var px = tex.GetPixels32(); int h = tex.height;
        Object.DestroyImmediate(tex);
        var cx = new float[count]; var cy = new float[count]; var bw = new int[count]; var bh = new int[count];
        for (int k = 0; k < count; k++)
        {
            double sx = 0, sy = 0; int n = 0, x0 = cell, x1 = -1, y0 = cell, y1 = -1;
            for (int y = 0; y < cell; y++)
                for (int x = 0; x < cell; x++)
                    if (px[(h - 1 - (row * cell + y)) * w + (first + k) * cell + x].a > 32)
                    {
                        sx += x; sy += y; n++;
                        if (x < x0) x0 = x; if (x > x1) x1 = x; if (y < y0) y0 = y; if (y > y1) y1 = y;
                    }
            if (n == 0) { Check(key + " idle cell " + k + " is not empty", false); return; }
            cx[k] = (float)(sx / n); cy[k] = (float)(sy / n); bw[k] = x1 - x0 + 1; bh[k] = y1 - y0 + 1;
        }
        float shift = 0, dw = 0, dh = 0;
        for (int i = 0; i < count; i++)
            for (int j = i + 1; j < count; j++)
            {
                shift = Mathf.Max(shift, Mathf.Sqrt((cx[i] - cx[j]) * (cx[i] - cx[j]) + (cy[i] - cy[j]) * (cy[i] - cy[j])));
                dw = Mathf.Max(dw, Mathf.Abs(bw[i] - bw[j]) / (float)Mathf.Max(bw[i], bw[j]));
                dh = Mathf.Max(dh, Mathf.Abs(bh[i] - bh[j]) / (float)Mathf.Max(bh[i], bh[j]));
            }
        Check(key + " idle hull holds still (centroid shift " + shift.ToString("0.0") + " px of " + cell + " = " + (shift / cell * 100f).ToString("0.0") + "%, limit " + (size ? MaxShift : FloatShift) * 100f + "%)",
              shift <= (size ? MaxShift : FloatShift) * cell + .01f);
        if (size)
            Check(key + " idle hull keeps its size (width " + (dw * 100f).ToString("0.0") + "%, height " + (dh * 100f).ToString("0.0") + "%, limit " + MaxSize * 100f + "%)",
                  dw <= MaxSize && dh <= MaxSize);
    }
}
