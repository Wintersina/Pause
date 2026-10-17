using System.IO;
using UnityEngine;

// The three rebuilt elites (Frost Cryo Siren, Frost Rimebreaker, Verdant Resin Warden: Art/Enemies/src~/frost_verdant_elites): real painted
// frames, 10 cells of 384 px (0 parked, 1 parked idle, 2 lift-off, 3-6 flight burn cycle, 7 bank left, 8 bank right, 9 damaged) + a 3-cell death
// strip. Checks: strip size and cell map, nothing within 6 px of a cell edge, one fixed anchor (nose line and horizontal pivot hold across
// the cells), the hull width holds, every state is its own drawing (not a recolour of one frame), the flight cycle really animates the
// burn, the damaged cell is visibly wrecked (a real share of the hull pixels differ from the flight cell), the Tide-style flames leave each
// nozzle and run past the hull, no pale outline ring, no pink anywhere (Space only), the Warden is not mostly green.
//
//   Unity -batchmode -quit -projectPath <abs>/Pause -executeMethod FrostVerdantEliteFramesTest.Run
public static class FrostVerdantEliteFramesTest
{
    static int fails;
    static void Check(string what, bool ok)
    {
        Debug.Log((ok ? "[FVF] PASS  " : "[FVF] FAIL  ") + what);
        if (!ok) fails++;
    }

    public static void Run() { TestHarness.Exit(Execute()); }

    const int S = 384, Cells = 10, Margin = 6;
    static readonly string[] Keys = { "frost_elite_cryo_siren", "frost_elite_rimebreaker", "verdant_elite_resin_warden" };

    static Color32[] Load(string path, out int w, out int h)
    {
        var t = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        t.LoadImage(File.ReadAllBytes(path));
        w = t.width; h = t.height;
        var px = t.GetPixels32();
        Object.DestroyImmediate(t);
        return px;
    }

    static string Dir(string key) { return "Assets/Art/Resources/Elites/" + char.ToUpper(key[0]) + key.Substring(1, key.IndexOf('_') - 1) + "/"; }

    public static int Execute()
    {
        fails = 0;
        EliteCatalog.Reload();
        foreach (string key in Keys) One(key);
        Debug.Log("[FVF] failures: " + fails);
        return fails;
    }

    static bool Differs(Color32 a, Color32 b) { return Mathf.Abs(a.r - b.r) + Mathf.Abs(a.g - b.g) + Mathf.Abs(a.b - b.b) > 48 || (a.a > 128) != (b.a > 128); }

    static void One(string key)
    {
        var def = EliteCatalog.Find(key);
        Check(key + ": def exists", def != null);
        if (def == null) return;
        var px = Load(Dir(key) + key + ".png", out int W, out int H);
        Check(key + ": strip is " + Cells + " cells of " + S + " px (" + W + " x " + H + ")", W == Cells * S && H == S && def.cellPixels == S);
        var c = def.cells;
        Check(key + ": cell map = parked 0, idle 1, lift-off 2, flight 3-6, banks 7/8, damaged 9",
              c.parked == 0 && c.parkedIdle == 1 && c.liftoff == 2 && c.bankLeft == 7 && c.bankRight == 8 && c.damaged == 9 &&
              c.flight != null && c.flight.Length == 4 && c.flight[0] == 3 && c.flight[3] == 6 && c.tell == -1 && c.action == -1 && c.hit == -1);
        if (W != Cells * S) return;

        var top = new float[Cells]; var cx = new float[Cells]; var wid = new int[Cells]; var bottom = new int[Cells];
        bool margins = true, any = true;
        for (int cell = 0; cell < Cells; cell++)
        {
            int x0 = int.MaxValue, y0 = int.MaxValue, x1 = -1, y1 = -1;
            for (int y = 0; y < S; y++)
                for (int x = 0; x < S; x++)
                    if (px[y * W + cell * S + x].a > 8) { x0 = Mathf.Min(x0, x); x1 = Mathf.Max(x1, x); y0 = Mathf.Min(y0, y); y1 = Mathf.Max(y1, y); }
            if (x1 < 0) { any = false; continue; }
            if (Mathf.Min(Mathf.Min(x0, y0), Mathf.Min(S - 1 - x1, S - 1 - y1)) < Margin) { margins = false; Debug.Log("[FVF] " + key + " cell " + cell + " bbox " + x0 + "," + y0 + "-" + x1 + "," + y1 + " is inside the margin"); }
            top[cell] = S - 1 - y1; cx[cell] = (x0 + x1) * .5f; wid[cell] = x1 - x0;   // rows run bottom-up: the nose is the largest y
        }
        Check(key + ": every cell has art", any);
        Check(key + ": nothing within " + Margin + " px of a cell edge", margins);
        Check(key + ": the nose line holds on one anchor across the cells (<= 10 px)", Ptp(top, Cells) <= 10f);
        Check(key + ": the horizontal pivot holds across the cells (<= 18 px: banks lean, the damaged cell loses wing)", Ptp(cx, Cells) <= 18f);
        var flat = new float[] { wid[0], wid[1], wid[2], wid[3], wid[4], wid[5], wid[6] };
        Check(key + ": the hull width holds across the flat cells (<= 40 px: folded vs spread poses)", Ptp(flat, flat.Length) <= 40f);

        // distinct drawings: the hull part (above the engine mouths) differs between the painted states
        var nz = def.nozzles;
        int mouthY = Mathf.RoundToInt(nz[0].y) - 6;   // rows above this (top-down) are the hull
        int[,] pairs = { { 0, 3 }, { 2, 3 }, { 3, 4 }, { 3, 7 }, { 3, 8 }, { 7, 8 }, { 3, 9 }, { 0, 2 } };
        int[] need = { 1200, 600, 600, 1500, 1500, 1500, 3000, 400 };
        for (int p = 0; p < pairs.GetLength(0); p++)
        {
            int a = pairs[p, 0], b = pairs[p, 1], d = 0;
            if (a == 3 && b == 4) continue;   // the flight cells 3-6 share one fixed hull on purpose (EliteEngineFireTest)
            for (int y = 0; y < mouthY; y++)
                for (int x = 0; x < S; x++)
                    if (Differs(px[(S - 1 - y) * W + a * S + x], px[(S - 1 - y) * W + b * S + x])) d++;
            Check(key + ": cells " + a + " and " + b + " are different drawings (" + d + " px differ, needs " + need[p] + ")", d >= need[p]);
        }
        // the burn cycle animates: the flame area differs between the four flight cells
        for (int cell = 4; cell <= 6; cell++)
        {
            int d = 0;
            for (int y = mouthY; y < S; y++) for (int x = 0; x < S; x++) if (Differs(px[(S - 1 - y) * W + 3 * S + x], px[(S - 1 - y) * W + cell * S + x])) d++;
            Check(key + ": burn cell " + cell + " differs from cell 3 under the hull (" + d + " px)", d >= 150);
        }

        // flames: leave every nozzle, in front of the hull, past the hull's bottom edge, each engine on its own phase
        int hullBottom = 0;
        for (int y = 0; y < S; y++) for (int x = 0; x < S; x++) if (px[(S - 1 - y) * W + x].a > 128) hullBottom = Mathf.Max(hullBottom, y);
        Check(key + ": no runtime plume doubles the baked flames (exhaustScale 0)", def.exhaustScale <= 0f);
        var lengths = new System.Collections.Generic.HashSet<int>();
        foreach (var n in nz)
        {
            int nx = Mathf.RoundToInt(n.x), ny = Mathf.RoundToInt(n.y);
            var h0 = px[(S - 1 - ny) * W + nx];
            Check(key + " " + n.name + ": nozzle point is on the hull, in the dark bell mouth", h0.a == 255 && (h0.r * .3f + h0.g * .59f + h0.b * .11f) < 70f);
            for (int cell = 2; cell <= 9; cell++)
            {
                bool covered = false;
                for (int dy = -3; dy <= 3 && !covered; dy++)
                    for (int dx = -9; dx <= 9 && !covered; dx++)
                    {
                        var pp = px[(S - 1 - (ny + dy)) * W + cell * S + nx + dx]; var q = px[(S - 1 - (ny + dy)) * W + nx + dx];
                        covered = pp.a == 255 && (pp.r != q.r || pp.g != q.g || pp.b != q.b) && Mathf.Max(pp.r, Mathf.Max(pp.g, pp.b)) > 150;
                    }
                Check(key + " cell " + cell + " " + n.name + ": the flame covers the nozzle point (in front of the hull)", covered);
            }
            int tip = ny;
            for (int y = ny; y < S; y++)
                for (int dx = -3; dx <= 3; dx++)
                {
                    var q = px[(S - 1 - y) * W + 3 * S + nx + dx]; var r0 = px[(S - 1 - y) * W + nx + dx];
                    if (q.a > 0 && (q.r != r0.r || q.g != r0.g || q.b != r0.b || q.a != r0.a)) tip = Mathf.Max(tip, y);
                }
            Check(key + " " + n.name + ": the flame runs past the hull's bottom edge (tip " + tip + " vs " + hullBottom + ")", tip >= hullBottom + 10);
            lengths.Add(tip);
        }
        Check(key + ": each engine keeps its own phase (flame lengths differ in the hover cell)", lengths.Count >= 2);

        // materials: no pale outline ring, no pink, not mostly green (the Warden)
        foreach (int cell in new[] { 0, 3 })
        {
            int edge = 0, edgePale = 0, inner = 0, innerPale = 0, solid = 0, pink = 0, green = 0;
            for (int y = 3; y < hullBottom - 8; y++)
                for (int x = 3; x < S - 3; x++)
                {
                    var p = px[(S - 1 - y) * W + cell * S + x];
                    if (p.a < 255) continue;
                    solid++;
                    Color.RGBToHSV(p, out float hh, out float ss, out float vv);
                    if (ss > .35f && vv > .4f && hh > .8f && hh < .96f) pink++;
                    if (ss > .3f && vv > .25f && hh > .2f && hh < .47f) green++;
                    int ring = 99;
                    for (int r = 1; r <= 7 && ring == 99; r++)
                        if (px[(S - 1 - y) * W + cell * S + x - r].a < 255 || px[(S - 1 - y) * W + cell * S + x + r].a < 255 ||
                            px[(S - 1 - (y - r)) * W + cell * S + x].a < 255 || px[(S - 1 - (y + r)) * W + cell * S + x].a < 255) ring = r;
                    bool pale = vv > .82f && ss < .28f;
                    if (ring <= 2) { edge++; if (pale) edgePale++; }
                    else if (ring >= 4 && ring <= 7) { inner++; if (pale) innerPale++; }
                }
            float pe = edge > 0 ? edgePale / (float)edge : 0f, pi = inner > 0 ? innerPale / (float)inner : 0f;
            Check(key + " cell " + cell + ": no pale outline ring (edge " + pe.ToString("0.00") + " pale vs " + pi.ToString("0.00") + " just inside)", pe - pi < .30f);
            Check(key + " cell " + cell + ": no pink on the hull (" + pink + " px of " + solid + ")", pink <= solid * .001f);
            if (key.StartsWith("verdant_")) Check(key + " cell " + cell + ": not mostly green (" + (green * 100f / Mathf.Max(1, solid)).ToString("0") + "% green px: its own palette)", green <= solid * .22f);
        }

        // death strip
        var dpx = Load(Dir(key) + key + "_death.png", out int DW, out int DH);
        Check(key + ": death strip is 3 cells of " + S + " px", DW == 3 * S && DH == S);
        bool dm = true, dpink = false;
        int dp = 0;
        for (int y = 0; y < S; y++)
            for (int x = 0; x < DW; x++)
            {
                var p = dpx[y * DW + x];
                if (p.a < 128) continue;
                int xi = x % S;
                if (xi < Margin || xi >= S - Margin || y < Margin || y >= S - Margin) dm = false;
                if (p.r > 230 && p.g < 115 && p.b > 178) dp++;
            }
        dpink = dp > 20;
        Check(key + ": death strip keeps the 6 px margin", dm);
        Check(key + ": death strip has no hostile-pink accent (pink is Space only; " + dp + " px)", !dpink);
    }

    static float Ptp(float[] a, int n) { float lo = a[0], hi = a[0]; for (int i = 1; i < n; i++) { lo = Mathf.Min(lo, a[i]); hi = Mathf.Max(hi, a[i]); } return hi - lo; }
}
