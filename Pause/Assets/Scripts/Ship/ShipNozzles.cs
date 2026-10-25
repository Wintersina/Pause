using UnityEngine;

// Per-hull engine nozzle positions, measured off each ship's own pixel art.
//
// Exhaust used to be mounted with one shared rule: "bottom edge of the
// sprite, plus/minus a fraction of its width for the twin-engine ships".
// That only works when the engines happen to sit on the bottom edge at that
// fraction. Paranoid's two engines are outboard pods at the far left and
// right of the hull, 2px above its bottom edge -- the shared rule put its
// plumes ~5px inboard of them, under the belly. Crimson Halo, Ion Lancer and
// Jade Phantom have twin engines the rule treated as one centre nozzle, and
// Volt Viper's twin mounts landed outside its two exhaust legs.
//
// Each entry is in sprite-rect pixels: x from the rect's left edge, y from
// its bottom edge, at the nozzle's exit (where the plume should begin). The
// rects are ShipHullArt's (256 px cells of the hull sheets). The points are
// the "nozzles" of the ship's own data file (ShipArt/<Key>/<Key>_data.json),
// measured by the art pipeline (src~/build_restyle.py / build.py): it walks
// down the nozzle's own pixel column to the last painted row and sits the
// point just inside that lip. ShipNozzlesTest checks every point lies on
// painted pixels in the lower half of its hull.
//
// The point is measured on the key frame (column 0). The hull bobs and banks
// inside its sprite rect, so a nozzle moves from frame to frame: `offsets`
// holds, per hull column (0..8, see ShipHullArt), the nozzle's (dx, dy) shift
// from its key-frame point in sprite-rect pixels (x right, y up), measured off
// that column's art. Damage rows share the intact silhouette, so one set of
// columns serves all three rows. Absent = the nozzle does not move.
public static class ShipNozzles
{
    public struct Nozzle
    {
        public float x, y;     // sprite-rect pixels
        public float scale;    // plume size relative to a single main engine
        public float[] offsets; // (dx, dy) per hull column, sprite-rect pixels, y up; null = static
        public Nozzle(float x, float y, float scale, float[] offsets = null)
        {
            this.x = x; this.y = y; this.scale = scale; this.offsets = offsets;
        }
    }

    static readonly Nozzle[] fallback = { new Nozzle(-1f, -1f, 1f) };

    public static bool Has(int index)
    {
        var d = ShipData.For(index);
        return d != null && d.nozzleList.Length > 0;
    }

    public static Nozzle[] For(int index)
    {
        return Has(index) ? ShipData.For(index).nozzleList : fallback;
    }

    // The nozzle's shift from its key-frame point on the hull frame `hull`
    // (ShipHullArt names a frame "<Key>_<state>_<column>"), in the hull's local space.
    public static Vector2 FrameShift(Sprite hull, Nozzle nozzle)
    {
        if (hull == null || nozzle.offsets == null) return Vector2.zero;
        string name = hull.name;
        int cut = name.LastIndexOf('_');
        int column;
        if (cut < 0 || !int.TryParse(name.Substring(cut + 1), out column)) return Vector2.zero;
        if (column < 0 || column * 2 + 1 >= nozzle.offsets.Length) return Vector2.zero;
        return new Vector2(nozzle.offsets[column * 2], nozzle.offsets[column * 2 + 1]) / hull.pixelsPerUnit;
    }

    // Sprite-rect pixels -> the hull's local space. The fallback (negative
    // coordinates) means "centre of the bottom edge" for an unknown hull.
    public static Vector2 ToLocal(Sprite hull, Nozzle nozzle)
    {
        Rect rect = hull.rect;
        float px = nozzle.x < 0f ? rect.width * .5f : nozzle.x;
        float py = nozzle.y < 0f ? 1f : nozzle.y;
        return (new Vector2(px, py) - hull.pivot) / hull.pixelsPerUnit;
    }
}
