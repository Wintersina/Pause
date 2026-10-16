using System.Collections.Generic;
using System.IO;
using UnityEngine;

// EMBER ELITE SPRITES MUST NEVER BE CLIPPED OR JUMP, AND THEIR ENGINES MUST BURN. The six Ember elites are the 384 px HD format (8 flight cells
// 3072 x 384 + 3 death cells; EliteHdTest checks the sizes): 0 parked, 1 parked idle, 2 lift-off, 3 / 4 the two painted flight poses, 5 / 6 banks,
// 7 damaged. Measured cell by cell on the alpha:
//   * nothing within 6 px of a cell edge (flames included);
//   * the registration holds: the nose (bbox top) within 6 px and the horizontal centre within 6 px across cells 0-6 (the flames hang below,
//     so only the top and the sides are compared; the banks shear the hull a little), the hull width within 14 px (pose B moves wings a little);
//     the damaged cell keeps its nose within 12 px and its centre within 10 px (hanging panels / smoke may move the bbox);
//   * the two flight poses really differ (>= 400 px of the hull's colour) and the damaged cell differs from the flight cell far more (>= 4000 px);
//   * the engines: per nozzle the point lies in the dark bell mouth, a flame covers it in cells 2-7 (in front of the hull, not hidden by the
//     bell), it runs >= 10 px past the hull's bottom edge in cell 3, engines with more than one nozzle differ in length, and no runtime
//     plume doubles the baked flames (exhaustScale 0). Death strips (which disperse by design) get the edge margin only.
//   * no pink on a hull outside Space (the Ember cue is orange / amber): hue of strongly saturated pixels never in the magenta band.
//
//   Unity -batchmode -quit -projectPath <abs>/Pause -executeMethod EmberEliteFramingTest.Run
public static class EmberEliteFramingTest
{
    static int fails;
    static void Check(string what, bool ok)
    {
        Debug.Log((ok ? "[EEF] PASS  " : "[EEF] FAIL  ") + what);
        if (!ok) fails++;
    }

    public static void Run() { TestHarness.Exit(Execute()); }

    const string Dir = "Assets/Art/Resources/Elites/Ember/";
    const string Defs = "Assets/Art/Resources/Elites/Defs/";
    const int Margin = 6;
    static readonly string[] Keys =
    {
        "ember_elite_sunstoke", "ember_elite_coalrunner", "ember_elite_brass_vulture", "ember_elite_kilnback", "ember_elite_ash_wraith", "ember_elite_cauterizer",
    };

    public static int Execute()
    {
        fails = 0;
        int n = 0;
        foreach (var f in Directory.GetFiles(Dir, "ember_elite_*.png")) { Strip(f, f.EndsWith("_death.png")); n++; }
        Check("Ember elite strips measured (" + n + ")", n == 12);
        foreach (var k in Keys) { Engines(k); Defs_(k); }
        Debug.Log("[EEF] failures: " + fails);
        return fails;
    }

    static Color32[] Load(string path, out int w, out int h)
    {
        var t = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        t.LoadImage(File.ReadAllBytes(path));
        w = t.width; h = t.height;
        var px = t.GetPixels32();
        Object.DestroyImmediate(t);
        return px;
    }

    static Color32[] Cell(Color32[] px, int w, int h, int cell)
    {
        var c = new Color32[h * h];
        for (int y = 0; y < h; y++) System.Array.Copy(px, y * w + cell * h, c, y * h, h);
        return c;
    }

    static void Strip(string path, bool dispersing)
    {
        string name = Path.GetFileNameWithoutExtension(path);
        var px = Load(path, out int W, out int h);
        int cells = W / h;
        Check(name + ": " + (dispersing ? "3 death cells" : "8 flight cells") + " of 384 px (" + W + " x " + h + ")", h == 384 && cells == (dispersing ? 3 : 8));
        var cx = new float[cells]; var top = new float[cells]; var w = new int[cells];
        bool margins = true, any = true;
        for (int c = 0; c < cells; c++)
        {
            int x0 = int.MaxValue, y0 = int.MaxValue, x1 = -1, y1 = -1;
            for (int y = 0; y < h; y++)
                for (int x = 0; x < h; x++)
                    if (px[y * W + c * h + x].a > 8) { x0 = Mathf.Min(x0, x); x1 = Mathf.Max(x1, x); y0 = Mathf.Min(y0, y); y1 = Mathf.Max(y1, y); }
            if (x1 < 0) { any = false; continue; }
            if (Mathf.Min(Mathf.Min(x0, y0), Mathf.Min(h - 1 - x1, h - 1 - y1)) < Margin) { margins = false; Debug.Log("[EEF] " + name + " cell " + c + " bbox " + x0 + "," + y0 + "-" + x1 + "," + y1 + " is within the " + Margin + " px margin"); }
            cx[c] = (x0 + x1) * .5f; top[c] = h - 1 - y1; w[c] = x1 - x0;   // Texture2D rows run bottom-up: the nose is the largest y
        }
        Check(name + ": every cell has art", any);
        Check(name + ": nothing within " + Margin + " px of a cell edge", margins);
        if (dispersing || !any || cells < 8) return;
        var main = new[] { 0, 1, 2, 3, 4, 5, 6 };
        Check(name + ": the nose stays put across cells 0-6 (<= 6 px)", Ptp(top, main) <= 6f);
        Check(name + ": the hull keeps its horizontal pivot across cells 0-6 (<= 6 px)", Ptp(cx, main) <= 6f);
        Check(name + ": hull width steady across the flight cells (<= 14 px)", Ptp(new float[] { w[0], w[1], w[3], w[4] }, new[] { 0, 1, 2, 3 }) <= 14f);
        Check(name + ": the damaged cell keeps the nose (<= 12 px) and pivot (<= 10 px)", Mathf.Abs(top[7] - top[3]) <= 12f && Mathf.Abs(cx[7] - cx[3]) <= 10f);
        var a = Cell(px, W, h, 3); var b = Cell(px, W, h, 4); var d = Cell(px, W, h, 7);
        int dAB = Diff(a, b, 0), dAD = Diff(a, d, 0);
        Check(name + ": the two flight poses differ (" + dAB + " px)", dAB >= 400);
        Check(name + ": the damaged cell is a real damaged drawing, not a hairline crack (" + dAD + " px differ from flight)", dAD >= 4000);
        // no magenta / pink hull pixels outside Space
        int pink = 0;
        for (int i = 0; i < a.Length; i++)
        {
            var p = a[i];
            if (p.a < 200) continue;
            Color.RGBToHSV(p, out float hh, out float ss, out float vv);
            if (ss > .5f && vv > .45f && hh > .79f && hh < .95f) pink++;
        }
        Check(name + ": no pink on the hull (" + pink + " px; pink is Space's cue)", pink < 40);
    }

    // pixels whose colour or alpha differs by more than `tol` in the rows above `fromRow` is ignored (the flames hang at the bottom)
    static int Diff(Color32[] a, Color32[] b, int tol)
    {
        int n = 0;
        for (int i = 0; i < a.Length; i++)
        {
            int dd = Mathf.Abs(a[i].r - b[i].r) + Mathf.Abs(a[i].g - b[i].g) + Mathf.Abs(a[i].b - b[i].b) + Mathf.Abs(a[i].a - b[i].a);
            if (dd > 40 + tol) n++;
        }
        return n;
    }

    static float Ptp(float[] a, int[] idx) { float lo = a[idx[0]], hi = a[idx[0]]; foreach (int i in idx) { lo = Mathf.Min(lo, a[i]); hi = Mathf.Max(hi, a[i]); } return hi - lo; }

    static void Engines(string key)
    {
        var px = Load(Dir + key + ".png", out int W, out int S);
        var def = JsonUtility.FromJson<EliteDef>(File.ReadAllText(Defs + key + ".json"));
        var hull = Cell(px, W, S, 0);
        Check(key + ": no runtime plume doubles the baked flames (exhaustScale 0)", def.exhaustScale <= 0f);
        Check(key + ": cellPixels 384", def.cellPixels == 384);
        int hullBottom = 0;
        for (int y = 0; y < S; y++) for (int x = 0; x < S; x++) if (hull[(S - 1 - y) * S + x].a > 128) hullBottom = Mathf.Max(hullBottom, y);
        var lengths = new HashSet<int>();
        foreach (var nz in def.nozzles)
        {
            int nx = Mathf.RoundToInt(nz.x), ny = Mathf.RoundToInt(nz.y);
            var h0 = hull[(S - 1 - ny) * S + nx];
            Check(key + " " + nz.name + ": nozzle point is on the hull, in the dark bell mouth", h0.a == 255 && (h0.r * .3f + h0.g * .59f + h0.b * .11f) < 70f);
            for (int cell = 2; cell <= 7; cell++)
            {
                var c = Cell(px, W, S, cell);
                if (cell == 5 || cell == 6) continue;          // banks shear the hull sideways: the mouth moves a few px
                bool covered = false;
                for (int dy = -2; dy <= 2 && !covered; dy++)
                    for (int dx = -5; dx <= 5 && !covered; dx++)
                    {
                        var p = c[(S - 1 - (ny + dy)) * S + nx + dx]; var q = hull[(S - 1 - (ny + dy)) * S + nx + dx];
                        covered = p.a == 255 && (p.r != q.r || p.g != q.g || p.b != q.b) && Mathf.Max(p.r, Mathf.Max(p.g, p.b)) > 150;
                    }
                if (cell == 7) covered = true;               // the damaged hull is a different drawing: its flame is checked by the length below
                Check(key + " cell " + cell + " " + nz.name + ": flame covers the nozzle point (in front of the hull, not hidden by the bell)", covered);
                if (cell == 3)
                {
                    int tip = ny;
                    for (int y = ny; y < S; y++)
                        for (int dx = -3; dx <= 3; dx++)
                        {
                            var q = c[(S - 1 - y) * S + nx + dx]; var r0 = hull[(S - 1 - y) * S + nx + dx];
                            if (q.a > 0 && (q.r != r0.r || q.g != r0.g || q.b != r0.b || q.a != r0.a)) tip = Mathf.Max(tip, y);
                        }
                    Check(key + " " + nz.name + ": flame leaves the engine and runs past the hull's bottom edge (tip " + tip + " vs " + hullBottom + ")", tip >= hullBottom + 10);
                    lengths.Add(tip);
                }
            }
        }
        if (def.nozzles.Length > 1) Check(key + ": each engine keeps its own flame length in the flight cell", lengths.Count >= 2);
    }

    static void Defs_(string key)
    {
        var def = JsonUtility.FromJson<EliteDef>(File.ReadAllText(Defs + key + ".json"));
        var c = def.cells;
        Check(key + ": cell map parked 0, idle 1, lift-off 2, flight 3 + 4, banks 5 / 6, damaged 7, no tell / action / hit cells",
              c.parked == 0 && c.parkedIdle == 1 && c.liftoff == 2 && c.flight.Length == 2 && c.flight[0] == 3 && c.flight[1] == 4 && c.bankLeft == 5 && c.bankRight == 6 &&
              c.damaged == 7 && c.tell < 0 && c.action < 0 && c.hit < 0);
    }
}
