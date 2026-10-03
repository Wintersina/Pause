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
// same rects are used by shopingShips.SpriteFor / OriginalShipArt, so the
// numbers can be checked against the art directly; ShipNozzlesTest also
// checks every point lies inside its hull.
public static class ShipNozzles
{
    public struct Nozzle
    {
        public float x, y;     // sprite-rect pixels
        public float scale;    // plume size relative to a single main engine
        public Nozzle(float x, float y, float scale) { this.x = x; this.y = y; this.scale = scale; }
    }

    const float Twin = .72f;

    static readonly Nozzle[][] table =
    {
        null,
        /*  1 Neon Comet   32x29 */ new[] { N(16f, 1.5f) },
        /*  2 Volt Viper   28x23 */ new[] { N(9.5f, 1.5f, Twin), N(18.5f, 1.5f, Twin) },
        /*  3 Solar Fang   29x28 */ new[] { N(14.5f, 1.5f) },
        /*  4 Crimson Halo 46x57 */ new[] { N(19f, 2.5f, Twin), N(29f, 2.5f, Twin) },
        /*  5 Ion Lancer   47x55 */ new[] { N(18f, 5.5f, Twin), N(29f, 5.5f, Twin) },
        /*  6 Jade Phantom 56x55 */ new[] { N(24.5f, 4.5f, Twin), N(32f, 4.5f, Twin) },
        /*  7 Gold Warden  51x55 */ new[] { N(25f, 1.5f), N(18f, 6.5f, .5f), N(33f, 6.5f, .5f) },
        /*  8 Lightning    28x27 */ new[] { N(8f, 1.5f, Twin), N(20f, 1.5f, Twin) },
        /*  9 Ligher       20x29 */ new[] { N(10f, 1.5f) },
        /* 10 Paranoid     30x22 */ new[] { N(3f, 2.5f, Twin), N(27f, 2.5f, Twin) },
        /* 11 Ninja        30x30 */ new[] { N(15f, 1.5f) },   // spins; wind, not plumes
        /* 12 Saboteur     24x30 */ new[] { N(12f, 1.5f) },
        /* 13 UFO          26x26 */ new[] { N(13f, 1.5f) },   // spins; wind, not plumes
        /* 14 Dove         20x24 */ new[] { N(10f, 1.5f) },
        /* 15 Turtle       22x29 */ new[] { N(11f, 1f) },
    };

    static readonly Nozzle[] fallback = { N(-1f, -1f) };

    static Nozzle N(float x, float y, float scale = 1f) { return new Nozzle(x, y, scale); }

    public static bool Has(int index)
    {
        return index > 0 && index < table.Length && table[index] != null && table[index].Length > 0;
    }

    public static Nozzle[] For(int index)
    {
        return Has(index) ? table[index] : fallback;
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
