using System.Collections.Generic;
using UnityEngine;

// SENSIBLE PLACEMENT of a planet world's ground pieces (landmarks, pipes,
// wildfires, launch sites) -- shared by every planet director (Verdant first;
// Frost and Ember adopt it the same way, see the end of this comment).
//
// Three things make a piece look like it belongs to the ground under it
// rather than a sticker dropped at random:
//
//   PINNED     A piece rides the ground tile it stands on (Layer.pinTo, the
//              mid tile): GroundPlanner places it from the tile's own
//              unwrapped scroll (BackdropTile.travel), so its offset to the
//              ground never changes while it is in view -- it does not slide.
//              A piece's ground coordinate is gy = y + travel.
//   AFFINITY   Every variant's mid tile has a coarse class grid (GroundMask,
//              <variant folder>/mask.txt: Water / Built / Open / Canopy per
//              16 px cell, derived offline from the tile's colour and edge
//              statistics by the world's src~/ground_masks.py). A piece
//              declares what it may stand on (PieceRule.on) and how much of
//              its footprint must match (need); GroundPlanner only accepts a
//              spot where the mask under the footprint agrees. The tile
//              repeats vertically (period = its drawn height) and spans the
//              view's width (TileWidth), exactly as BackdropTile draws it.
//   ROOM       Accepted footprints are remembered (ground coordinates) and a
//              new one may not overlap any of them; they are forgotten once
//              they have scrolled out under the view.
//
// Directors build their CLUSTER GRAMMAR on top (VerdantDirector: a refinery
// with a pipe run to a neighbour, a wildfire with burn patches downwind ...).
//
// Adopting it (Frost, Ember): give the world's ground-piece layer
// .PinnedTo("mid", midRate), write mask.txt per variant with a
// ground_masks.py of its own (Frost: open water leads / ice sheet / coast /
// industry; Ember: lava / crust / forge / ash), declare a PieceRule per
// drawing, and replace the director's spacing-only spawn with
// planner.Find(...) + Pin(piece, x, gy) and planner.Step(piece) per frame.
// Nothing here allocates after construction.
[System.Flags]
public enum GroundClass : byte
{
    None = 0,
    Water = 1,      // rivers, channels, flooded streets / open water, lava
    Built = 2,      // ruins, roads, industry already painted in the tile
    Open = 4,       // clearings, flats, banks: smooth ground that is not water
    Canopy = 8,     // dense forest / ice sheet / crust: the textured ground
    Land = Built | Open | Canopy,
    Any = Water | Land,
}

// One variant's class grid (row 0 = the TOP of the tile image).
public class GroundMask
{
    public readonly int Cols, Rows;
    readonly GroundClass[] cells;

    public GroundMask(int cols, int rows, GroundClass[] cells) { Cols = cols; Rows = rows; this.cells = cells; }

    // mask.txt: one line per row, one character per cell (W B O C).
    public static GroundMask Parse(string text)
    {
        if (string.IsNullOrEmpty(text)) return null;
        var lines = text.Replace("\r", "").Split('\n');
        int rows = 0, cols = 0;
        foreach (var l in lines) if (l.Length > 0) { rows++; cols = Mathf.Max(cols, l.Length); }
        if (rows == 0) return null;
        var c = new GroundClass[rows * cols];
        int r = 0;
        foreach (var l in lines)
        {
            if (l.Length == 0) continue;
            for (int x = 0; x < cols; x++)
            {
                char ch = x < l.Length ? l[x] : 'C';
                c[r * cols + x] = ch == 'W' ? GroundClass.Water : ch == 'B' ? GroundClass.Built : ch == 'O' ? GroundClass.Open : GroundClass.Canopy;
            }
            r++;
        }
        return new GroundMask(cols, rows, c);
    }

    public static GroundMask Load(string world, int variant)
    {
        var t = Resources.Load<TextAsset>(BackdropCatalog.TileFolder(world, variant) + "mask");
        return t != null ? Parse(t.text) : null;
    }

    // u across the tile 0..1 (left..right), v up the tile 0..1 (bottom..top),
    // both wrapped / clamped like the drawn tile (repeats vertically only).
    public GroundClass At(float u, float v)
    {
        int x = Mathf.Clamp((int)(u * Cols), 0, Cols - 1);
        float vv = v - Mathf.Floor(v);
        int y = Mathf.Clamp((int)((1f - vv) * Rows), 0, Rows - 1);
        return cells[y * Cols + x];
    }

    public float Share(GroundClass c)
    {
        int n = 0;
        foreach (var k in cells) if ((k & c) != 0) n++;
        return n / (float)cells.Length;
    }
}

// What a drawing may stand on.
public struct PieceRule
{
    public GroundClass on;      // classes its footprint must sit on
    public float need;          // share of the footprint samples that must match (0..1)
    public GroundClass near;    // optional: at least one sample of its RING (1.6 x footprint) must be this (None: no rule)
    public float core;          // footprint half-size as a share of the drawn width (the art's core, not its margins)

    public PieceRule(GroundClass on, float need, float core = .32f, GroundClass near = GroundClass.None)
    {
        this.on = on; this.need = need; this.core = core; this.near = near;
    }
}

public class GroundPlanner
{
    readonly BackdropSet set;
    readonly BackdropTile tile;
    public readonly GroundMask Mask;

    struct Foot { public bool on; public float x, rx, ry; public double gy; }
    readonly Foot[] feet;

    public GroundPlanner(BackdropSet set, string hostLayer, GroundMask mask, int capacity = 48)
    {
        this.set = set;
        Mask = mask;
        foreach (var t in set.Tiles) if (t.layer.name == hostLayer) tile = t;
        feet = new Foot[capacity];
    }

    public bool Ready => tile != null && tile.TileHeight > 0f;
    public BackdropTile Tile => tile;
    public double Travel => tile != null ? tile.travel : 0.0;

    // Screen y (set-root local) of ground coordinate gy, and back.
    public float Y(double gy) { return (float)(gy - Travel); }
    public double GroundY(float y) { return y + Travel; }

    // Tile coordinates of a ground point: u across (0 left .. 1 right), v up
    // from a tile copy's bottom edge (wraps every TileHeight). The copies sit
    // at bottom edges -V/2 + i h - offset (BackdropTile.Tick), so a point at
    // y lies (y + V/2 + offset) / h up its copy; with offset = travel mod h
    // that is (gy + V/2) / h -- constant for a pinned piece.
    public Vector2 UV(float x, double gy)
    {
        float w = tile != null ? tile.TileWidth : 1f;
        float h = tile != null ? Mathf.Max(.01f, tile.TileHeight) : 1f;
        double v = (gy + set.HalfHeight) / h;
        return new Vector2(x / w + .5f, (float)(v - System.Math.Floor(v)));
    }

    public GroundClass ClassAt(float x, double gy)
    {
        if (Mask == null) return GroundClass.Canopy;
        var uv = UV(x, gy);
        return Mask.At(uv.x, uv.y);
    }

    // Share of a footprint (half-sizes rx, ry, an ellipse sampled on a 5 x 5
    // grid) whose ground is one of `on`.
    public float Match(float x, double gy, float rx, float ry, GroundClass on)
    {
        if (Mask == null) return 1f;
        int hit = 0, n = 0;
        for (int j = -2; j <= 2; j++)
            for (int i = -2; i <= 2; i++)
            {
                float fx = i * .5f, fy = j * .5f;
                if (fx * fx + fy * fy > 1.05f) continue;
                n++;
                if ((ClassAt(x + fx * rx, gy + fy * ry) & on) != 0) hit++;
            }
        return n > 0 ? hit / (float)n : 0f;
    }

    // Does a ring just outside the footprint touch `near` anywhere?
    public bool Near(float x, double gy, float rx, float ry, GroundClass near)
    {
        if (near == GroundClass.None || Mask == null) return true;
        for (int k = 0; k < 12; k++)
        {
            float a = k * Mathf.PI / 6f;
            if ((ClassAt(x + Mathf.Cos(a) * rx * 1.6f, gy + Mathf.Sin(a) * ry * 1.6f) & near) != 0) return true;
        }
        return false;
    }

    // A piece's footprint half-sizes (the same everywhere: Find, Accepts,
    // Claim and the tests).
    public static float HalfX(PieceRule r, float size) { return size * Mathf.Max(r.core, MinCore); }
    public static float HalfY(PieceRule r, float size) { return HalfX(r, size) * .8f; }
    public const float MinCore = .2f;

    public bool Accepts(PieceRule r, float x, double gy, float size)
    {
        float rx = HalfX(r, size), ry = HalfY(r, size);
        return Match(x, gy, rx, ry, r.on) >= r.need && Near(x, gy, rx, ry, r.near);
    }

    // Room for a footprint (half-sizes) at x, gy: no overlap with any remembered one.
    public bool Free(float x, double gy, float rx, float ry, int ignore = -1)
    {
        for (int i = 0; i < feet.Length; i++)
        {
            if (!feet[i].on || i == ignore) continue;
            if (Mathf.Abs(feet[i].x - x) < feet[i].rx + rx && System.Math.Abs(feet[i].gy - gy) < feet[i].ry + ry) return false;
        }
        return true;
    }

    // Remember a footprint; returns its slot (-1 when full: then nothing is placed).
    public int Claim(float x, double gy, float rx, float ry)
    {
        for (int i = 0; i < feet.Length; i++)
        {
            if (feet[i].on) continue;
            feet[i] = new Foot { on = true, x = x, gy = gy, rx = rx, ry = ry };
            return i;
        }
        return -1;
    }

    public void Release(int slot) { if (slot >= 0 && slot < feet.Length) feet[slot].on = false; }

    // Forget footprints that have scrolled out under the view.
    public void Prune()
    {
        double bottom = Travel - set.HalfHeight;
        for (int i = 0; i < feet.Length; i++)
            if (feet[i].on && feet[i].gy + feet[i].ry < bottom - 3.0) feet[i].on = false;
    }

    // Looks for a spot for a piece of drawn width `size` obeying `rule`, with
    // its centre in [xMin, xMax] and gy in [gyMin, gyMax]: `tries` random
    // candidates, the best-matching free one wins. False when none fits.
    public bool Find(PieceRule rule, float size, float xMin, float xMax, double gyMin, double gyMax,
                     System.Random rng, out float x, out double gy, int tries = 28)
    {
        x = 0f; gy = 0.0;
        float rx = HalfX(rule, size), ry = HalfY(rule, size);
        float best = -1f;
        for (int t = 0; t < tries; t++)
        {
            float cx = xMin + (float)rng.NextDouble() * (xMax - xMin);
            double cy = gyMin + rng.NextDouble() * (gyMax - gyMin);
            if (!Free(cx, cy, rx, ry)) continue;
            float m = Match(cx, cy, rx, ry, rule.on);
            if (m < rule.need || m <= best) continue;
            if (!Near(cx, cy, rx, ry, rule.near)) continue;
            best = m; x = cx; gy = cy;
            if (m >= .999f) break;
        }
        return best >= 0f;
    }

    public int Footprints
    {
        get { int n = 0; foreach (var f in feet) if (f.on) n++; return n; }
    }
}
