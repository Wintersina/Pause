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
public static class ShipNozzles
{
    public struct Nozzle
    {
        public float x, y;     // sprite-rect pixels
        public float scale;    // plume size relative to a single main engine
        public Nozzle(float x, float y, float scale) { this.x = x; this.y = y; this.scale = scale; }
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
