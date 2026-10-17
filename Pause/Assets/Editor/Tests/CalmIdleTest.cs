using System.IO;
using UnityEngine;

// CALM IDLE MOTION: the enemies' idle cells only shimmer. No left-and-right wobble, no
// wing open/close, no vertical stretch baked into cells 0..3 of an enemy strip, and none
// driven from code either.
//
//   art   between any two idle cells the hull centroid moves <= 1.5% of the cell width
//         and the silhouette's bounding box changes <= 3% in width and height.
//         Exempt (deliberate): every WING FLAPPER (EnemyRoster.WingFlapKeys, art and hold pattern as painted; the
//         Desert boss too), the four FLOATING rocks (their drawn bob is the point; the
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
            if (d == null || d.role == EnemyRole.Mine || EnemyRoster.FlapsWings(d)) continue;   // wing flappers keep their art
            bool floating = System.Array.IndexOf(FloatingRocks, key) >= 0;
            Strip(key, path, 7, 0, 4, 0, !floating);
        }
        // Desert boss: wing beat, kept as painted (allowlisted)
        foreach (var boss in new[] { "Ember", "Frost", "Storm", "Tide", "Verdant" })
            Strip("boss " + boss, "Assets/Art/Resources/Bosses/" + boss + ".png", 5, 0, 4, 0, true);

        foreach (var d in EnemyRoster.All)
        {
            if (d.role == EnemyRole.Mine) continue;
            var t = EnemyRoster.IdleTicks(d);
            int sum = 0, min = 99;
            foreach (var x in t) { sum += x; min = Mathf.Min(min, x); }
            if (d.key == "space_alien" || EnemyRoster.FlapsWings(d)) continue;
            Check(d.key + " idle loop is calm (" + sum + " ticks, shortest hold " + min + ")", sum >= 16 && min >= 4);
        }
        CodexPreviews();
        Check("wing-flap allowlist is not empty (" + EnemyRoster.WingFlapKeys.Length + ")", EnemyRoster.WingFlapKeys.Length > 0);
        foreach (var k in EnemyRoster.WingFlapKeys) BodyRegistered(k);
        foreach (var k in EnemyRoster.WingFlapKeys) Check(k + " (wing flapper) is a real enemy", EnemyRoster.Find(k) != null);
        Check("elite procedural squash is small (" + EliteShip.SquashMax + ")", EliteShip.SquashMax <= .04f);
        Check("elite procedural lean is small (" + EliteShip.LeanMax + ")", EliteShip.LeanMax <= 3f);
        Check("elite bank is capped (" + EliteShip.BankCap + ")", EliteShip.BankCap <= 4f);
        Debug.Log("[CALM-IDLE] failures: " + fails);
        return fails;
    }

    // The Codex screen plays the same idle cells (CodexAnimations -> CodexAnimator) and nothing procedural:
    // no EliteShip squash / lean / bank, no scale pulse, no backdrop pan on a ship. Every idle drawing is laid
    // out at ONE size (equal sprite bounds, so CodexAnimator.Place gives one scale), and the only whole-sprite
    // motion is a ROCK's spin or sway (a rotation, never a stretch; rocks are left as they are).
    static void CodexPreviews()
    {
        foreach (var d in EnemyRoster.All)
        {
            if (d.role == EnemyRole.Mine) continue;
            Preview(d.key, CodexAnimations.Enemy(d), d.role == EnemyRole.Rock);
        }
        foreach (var e in EliteCatalog.All) Preview(e.key, CodexAnimations.Elite(e), false);
        foreach (var b in BossCatalog.All) Preview("boss " + b.name, CodexAnimations.Boss(b), false);
    }

    static void Preview(string what, CodexAnimation a, bool rock)
    {
        if (a == null || !a.HasArt) return;   // no art loaded for it: nothing to animate
        bool constant = true;
        var size = a.idle[0].bounds.size;
        foreach (var sp in a.idle) constant &= sp != null && (sp.bounds.size - size).sqrMagnitude < 1e-6f;
        Check("Codex " + what + ": every idle drawing is shown at one scale", constant);
        Check("Codex " + what + ": no pan, and no spin or sway unless it is a rock (spin " + a.spinDegreesPerSecond + ", sway " + a.swayDegrees + ")",
              a.driftPeriod == 0f && (rock || (a.spinDegreesPerSecond == 0f && a.swayDegrees == 0f)));
    }

    // Wing / limb flappers keep their wings moving, but the BODY must sit identically in all four idle cells
    // (same place, same size), so only wings move and the loop seam (cell 3 -> 0) does not jump. The body is the
    // part that survives an erosion (thin wings and limbs do not); the share of it that is opaque in all four cells
    // must be high. desert_fighter_2 is a wing-only silhouette (no body to erode), so it is not measured.
    public const float MinBodyOverlap = .6f;
    static void BodyRegistered(string key)
    {
        if (key == "desert_fighter_2") return;
        var tex = new Texture2D(2, 2);
        tex.LoadImage(File.ReadAllBytes("Assets/Art/Resources/Enemies/" + key + ".png"));
        int cell = tex.width / 7, h = tex.height;
        var px = tex.GetPixels32();
        int w = tex.width;
        Object.DestroyImmediate(tex);
        var masks = new bool[4][];
        for (int k = 0; k < 4; k++)
        {
            masks[k] = new bool[cell * cell];
            for (int y = 0; y < cell; y++)
                for (int x = 0; x < cell; x++)
                    masks[k][y * cell + x] = px[(h - 1 - y) * w + k * cell + x].a > 32;
        }
        int r = Mathf.Max(3, cell / 12);
        var cores = new bool[4][]; int least = int.MaxValue;
        for (int pass = 0; pass < 2; pass++)
        {
            least = int.MaxValue;
            for (int k = 0; k < 4; k++)
            {
                cores[k] = Erode(masks[k], cell, r);
                int n = 0; foreach (bool b in cores[k]) if (b) n++;
                least = Mathf.Min(least, n);
            }
            if (least >= 30) break;
            r = 1;
        }
        int all = 0;
        for (int i = 0; i < cell * cell; i++) if (cores[0][i] && cores[1][i] && cores[2][i] && cores[3][i]) all++;
        float share = least > 0 ? all / (float)least : 0f;
        Check(key + " flapper: body sits identically in all four idle cells (stable share " + share.ToString("0.00") + ", need >= " + MinBodyOverlap + ")", share >= MinBodyOverlap);
    }

    static bool[] Erode(bool[] m, int n, int r)
    {
        var o = new bool[n * n];
        for (int y = r; y < n - r; y++)
            for (int x = r; x < n - r; x++)
            {
                bool all = true;
                for (int dy = -r; dy <= r && all; dy++)
                    for (int dx = -r; dx <= r; dx++)
                        if (!m[(y + dy) * n + x + dx]) { all = false; break; }
                o[y * n + x] = all;
            }
        return o;
    }

    // (every pair of idle cells is compared, so the loop seam 3 -> 0 is covered)
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
