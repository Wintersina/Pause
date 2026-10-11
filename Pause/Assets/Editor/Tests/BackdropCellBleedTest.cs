using System.Collections.Generic;
using System.IO;
using UnityEngine;

// GROUND ATLAS CELLS HOLD ONE DRAWING. Every planet world's ground pieces
// (landmarks, pipes, sites, fires) are cut from 256 px atlas cells; a sliver
// of the NEIGHBOURING drawing left in a cell's margin draws in game as a
// fragment hanging in the air beside the piece (Frost's refineries carried a
// flat strip of a rig's floe skirt over their stacks; Desert's straight pipe
// the flange of the next one). Art/Worlds/src~/clean_cell_bleed.py removes
// them; this keeps them out: no cell may hold a small separate alpha
// component that is a thin sliver along the cell's outer band and does not
// touch the drawing. Particles, droplets, loose floes and rocks (compact, or
// away from the edges) are fine.
//
// And SPANS (bridges, viaducts, causeways, lone pipe bridges) declare ends
// (PieceRule.WithEnds): every world's span drawing has an ends rule, and its
// ends lie on the drawing (within its opaque bounds), so the planner checks
// the ground where the drawing really meets it.
//
//   Unity -batchmode -quit -projectPath <abs>/Pause -executeMethod BackdropCellBleedTest.Run
public static class BackdropCellBleedTest
{
    static int fails;
    static void Check(string what, bool ok)
    {
        Debug.Log((ok ? "[BCB] PASS  " : "[BCB] FAIL  ") + what);
        if (!ok) fails++;
    }

    public static void Run() { TestHarness.Exit(Execute()); }

    const int Band = 40, Thin = 18;
    static readonly string[] Worlds = { "Frost", "Verdant", "Ember", "Tide", "Desert" };
    static readonly string[] Atlases = { "landmarks", "pipes", "sites", "fires" };

    public static int Execute()
    {
        fails = 0;
        Bleed();
        Spans();
        Debug.Log("[BCB] failures: " + fails);
        return fails;
    }

    [System.Serializable] class Cell { public string n; public int x, y, w, h; }
    [System.Serializable] class Sheet { public Cell[] sprites; }

    static void Bleed()
    {
        foreach (var w in Worlds)
            foreach (var a in Atlases)
            {
                string png = "Assets/Art/Backgrounds/Resources/Worlds/" + w + "/Backdrop3/" + a + ".png";
                if (!File.Exists(png)) continue;
                var tex = new Texture2D(2, 2);
                tex.LoadImage(File.ReadAllBytes(png));
                var px = tex.GetPixels32();
                int W = tex.width;
                var sheet = JsonUtility.FromJson<Sheet>(File.ReadAllText(png.Substring(0, png.Length - 4) + ".json"));
                var bad = new List<string>();
                foreach (var c in sheet.sprites)
                {
                    string s = Slivers(px, W, c);
                    if (s != null) bad.Add(c.n + " " + s);
                }
                Object.DestroyImmediate(tex);
                Check(w + " " + a + ": no cell carries a sliver of its neighbour (" + (bad.Count == 0 ? "none" : string.Join("; ", bad)) + ")", bad.Count == 0);
            }
    }

    // The first sliver found in cell `c` ("x0..x1,y0..y1", y down the cell), or null.
    static string Slivers(Color32[] px, int W, Cell c)
    {
        int cw = c.w, ch = c.h;
        var lab = new int[cw * ch];
        var sizes = new List<int> { 0 };
        var minX = new List<int> { 0 }; var maxX = new List<int> { 0 }; var minY = new List<int> { 0 }; var maxY = new List<int> { 0 };
        var stack = new Stack<int>();
        // image rows run bottom-up in Unity: cell row yy (from the top) is texture row c.y + ch - 1 - yy
        System.Func<int, int, bool> on = (xx, yy) => px[(c.y + ch - 1 - yy) * W + c.x + xx].a > 8;
        for (int y0 = 0; y0 < ch; y0++)
            for (int x0 = 0; x0 < cw; x0++)
            {
                if (lab[y0 * cw + x0] != 0 || !on(x0, y0)) continue;
                int id = sizes.Count, n = 0, ax = x0, bx = x0, ay = y0, by = y0;
                lab[y0 * cw + x0] = id;
                stack.Push(y0 * cw + x0);
                while (stack.Count > 0)
                {
                    int i = stack.Pop(), x = i % cw, y = i / cw;
                    n++;
                    if (x < ax) ax = x; if (x > bx) bx = x; if (y < ay) ay = y; if (y > by) by = y;
                    for (int dy = -1; dy <= 1; dy++)
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            int xx = x + dx, yy = y + dy;
                            if (xx < 0 || yy < 0 || xx >= cw || yy >= ch || lab[yy * cw + xx] != 0 || !on(xx, yy)) continue;
                            lab[yy * cw + xx] = id;
                            stack.Push(yy * cw + xx);
                        }
                }
                sizes.Add(n); minX.Add(ax); maxX.Add(bx); minY.Add(ay); maxY.Add(by);
            }
        if (sizes.Count <= 1) return null;
        int main = 1;
        for (int k = 2; k < sizes.Count; k++) if (sizes[k] > sizes[main]) main = k;
        for (int k = 1; k < sizes.Count; k++)
        {
            if (k == main || sizes[k] <= 30 || sizes[k] >= sizes[main] * .08f) continue;
            int x0 = minX[k], x1 = maxX[k], y0 = minY[k], y1 = maxY[k], w = x1 - x0 + 1, h = y1 - y0 + 1;
            bool lr = (x0 <= Band && x1 <= Band + Thin || x1 >= cw - 1 - Band && x0 >= cw - 1 - Band - Thin) && w <= Thin && h >= 2 * w;
            bool tb = (y0 <= Band && y1 <= Band + Thin || y1 >= ch - 1 - Band && y0 >= ch - 1 - Band - Thin) && h <= Thin && w >= 2 * h;
            if (!lr && !tb) continue;
            bool touches = false;
            for (int y = Mathf.Max(0, y0 - 3); y <= Mathf.Min(ch - 1, y1 + 3) && !touches; y++)
                for (int x = Mathf.Max(0, x0 - 3); x <= Mathf.Min(cw - 1, x1 + 3); x++)
                    if (lab[y * cw + x] == main) { touches = true; break; }
            if (!touches) return x0 + ".." + x1 + "," + y0 + ".." + y1;
        }
        return null;
    }

    static void Spans()
    {
        var spans = new (string world, string atlas, string piece, PieceRule rule)[]
        {
            ("Frost", "landmarks", "causeway_00", FrostTuning.Rule("causeway_00", 1)),
            ("Verdant", "pipes", "pipebridge_00", VerdantTuning.Rule("pipebridge_00")),
            ("Ember", "landmarks", "viaduct_00", EmberTuning.Rule("viaduct_00")),
            ("Ember", "pipes", "pipebridge_00", EmberTuning.Rule("pipebridge_00")),
            ("Ember", "pipes", "pipebridge_01", EmberTuning.Rule("pipebridge_01")),
            ("Tide", "landmarks", "bridge_section_00", TideTuning.Rule("bridge_section_00")),
            ("Tide", "pipes", "pipebridge_00", TideTuning.Rule("pipebridge_00")),
            ("Tide", "pipes", "pipebridge_01", TideTuning.Rule("pipebridge_01")),
            ("Desert", "pipes", "pipe_bridge_00", DesertTuning.Rule("pipe_bridge_00")),
            ("Desert", "pipes", "pipe_bridge_01", DesertTuning.Rule("pipe_bridge_01")),
        };
        foreach (var s in spans)
        {
            var atlas = new BackdropAtlas(Resources.Load<Texture2D>("Worlds/" + s.world + "/Backdrop3/" + s.atlas),
                                          Resources.Load<TextAsset>("Worlds/" + s.world + "/Backdrop3/" + s.atlas));
            var sp = atlas.Get(s.piece);
            bool ends = s.rule.HasEnds, inside = false;
            if (sp != null && ends)
            {
                // the drawn bounds as a share of the drawn width from the centre (y up)
                var b = sp.bounds;
                float w = b.size.x;
                System.Func<Vector2, bool> within = e => Mathf.Abs(e.x) <= .5f && e.y * w >= b.min.y - .02f && e.y * w <= b.max.y + .02f;
                inside = within(s.rule.endA) && within(s.rule.endB) && s.rule.endA.x < 0f && s.rule.endB.x > 0f;
            }
            Check(s.world + " " + s.piece + ": a span with two ends on the drawing (" + (sp != null) + ", ends " + ends + ", inside " + inside + ")",
                  sp != null && ends && inside);
        }
    }
}
