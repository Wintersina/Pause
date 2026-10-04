using UnityEngine;

// How each ship wears its life hearts (ShipLivesIndicator): the one table,
// keyed by ShipId.
//
// The player's thumb sits a hull-length under the ship (movePlayer: the ship
// flies 1 unit above the finger), so everything beside or below the hull's
// middle is hidden under it. The hearts gather at the tail instead -- just
// behind the hull, either side of the engine flame -- where a thumb leaves
// them in view. The spinners (Ninja, UFO) wear theirs as a shield ring round
// the spinning hull that only whips quickly past the thumb side.
public enum HeartStyle
{
    TailArc,       // a shallow arc hugging the tail, either side of the flame
    CometTrail,    // a tapering, wagging trail off one side of the tail
    ShieldRing,    // a ring orbiting the spinning hull (spinners only)
    WingTips,      // a pair of diagonals hanging off the wing tips
    PulseCluster,  // a tight honeycomb beside the flame, beating together
}

public static class ShipHeartStyles
{
    // By ship id (0 is "none").
    static readonly HeartStyle[] table =
    {
        HeartStyle.TailArc,       //  0 none
        HeartStyle.CometTrail,    //  1 Neon Comet: a comet, so a comet's tail
        HeartStyle.TailArc,       //  2 Volt Viper: arc round its twin exhaust legs
        HeartStyle.PulseCluster,  //  3 Solar Fang: a small beating sun beside its single engine
        HeartStyle.TailArc,       //  4 Crimson Halo: a halo-like arc under the tail
        HeartStyle.WingTips,      //  5 Ion Lancer: long swept wings, lights on the tips
        HeartStyle.PulseCluster,  //  6 Jade Phantom: a ghostly huddle
        HeartStyle.TailArc,       //  7 Gold Warden: five hearts as a crown under the tail
        HeartStyle.WingTips,      //  8 Lightning: wide wings, a pair off each tip
        HeartStyle.CometTrail,    //  9 Ligher: a flickering trail like a flame's
        HeartStyle.WingTips,      // 10 Paranoid: outboard engine pods, hearts off each
        HeartStyle.ShieldRing,    // 11 Ninja: spins, so its hearts orbit
        HeartStyle.PulseCluster,  // 12 Saboteur: a tight, twitchy cluster
        HeartStyle.ShieldRing,    // 13 UFO: spins, so its hearts orbit
        HeartStyle.CometTrail,    // 14 Dove: trailing like tail feathers
        HeartStyle.PulseCluster,  // 15 Turtle: a clutch tucked by the shell
    };

    public static HeartStyle For(int id)
    {
        // Only a spinning hull carries an orbit round; a spinner always orbits.
        if (ShipUiSlots.Spins(id)) return HeartStyle.ShieldRing;
        var style = id >= 0 && id < table.Length ? table[id] : HeartStyle.TailArc;
        return style == HeartStyle.ShieldRing ? HeartStyle.TailArc : style;
    }

    // Two-sided styles spread either side of the flame; one-sided ones sit
    // on one side and mirror at a screen edge.
    public static bool TwoSided(HeartStyle style)
    {
        return style == HeartStyle.TailArc || style == HeartStyle.WingTips;
    }

    // Unit honeycomb cells for PulseCluster (x outward from the flame, y
    // down), 2..5 hearts.
    static readonly Vector2[][] cluster =
    {
        new[] { new Vector2(0f, 0f) },
        new[] { new Vector2(0f, 0f), new Vector2(1f, 0f) },
        // (the second row leans outward, away from the thumb under the flame)
        new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(1.5f, .87f) },
        new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(2f, 0f), new Vector2(1.5f, .87f) },
        new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(2f, 0f), new Vector2(1.5f, .87f), new Vector2(2.5f, .87f) },
    };

    public struct Frame
    {
        public float tailY;           // hull bottom, relative to the ship
        public float flameL, flameR;  // the exhaust's sides, relative to the ship
        public float hullL, hullR;    // the hull's sides
        public float size, gap;       // a heart, and the gap kept round it
    }

    // The resting offset (from the ship's position) of heart k of n in
    // `style`, its first heart on side `side` (+1 right, -1 left).
    // `oneSided`: a two-sided style with one side blocked (a gun hovering
    // low there) runs all its hearts out on `side` instead.
    public static Vector2 Offset(HeartStyle style, int k, int n, float side, Frame f, bool oneSided = false)
    {
        float h = f.size, g = f.gap, top = f.tailY - g - h * .5f;
        switch (style)
        {
            case HeartStyle.TailArc:
            {
                // alternate sides outward from the flame, the outer ones drooping
                float s = oneSided || k % 2 == 0 ? side : -side;
                int rank = oneSided ? k : k / 2;
                float x = s > 0f ? f.flameR + g + h * .5f + rank * (h + g * .5f)
                                 : f.flameL - g - h * .5f - rank * (h + g * .5f);
                return new Vector2(x, top - rank * h * (oneSided ? .12f : .3f));
            }
            case HeartStyle.WingTips:
            {
                float s = oneSided || k % 2 == 0 ? side : -side;
                int rank = oneSided ? k : k / 2;
                // hanging off the wing tip (never into the flame), each
                // further one out and down
                float x = Mathf.Max((s > 0f ? f.hullR : -f.hullL) - h * .1f, (s > 0f ? f.flameR : -f.flameL) + g + h * .5f);
                return new Vector2(s * (x + rank * h * .7f), top - rank * h * (oneSided ? .3f : .6f));
            }
            case HeartStyle.CometTrail:
            {
                float inner = side > 0f ? f.flameR : f.flameL;
                return new Vector2(inner + side * (g + h * .5f + k * h * .8f), top - k * h * .28f);
            }
            case HeartStyle.PulseCluster:
            default:
            {
                float inner = side > 0f ? f.flameR : f.flameL;
                var cells = cluster[Mathf.Clamp(n, 1, cluster.Length) - 1];
                Vector2 c = cells[Mathf.Clamp(k, 0, cells.Length - 1)];
                float d = h * 1.02f;
                return new Vector2(inner + side * (g + h * .5f + c.x * d), top - c.y * d);
            }
        }
    }

    // A one-sided style's own side for this ship: away from its gun
    // (UltimateGun rests left on even ids, right on odd, and hovers), under
    // the secret meter, which holds still.
    public static float HomeSide(int id) { return id % 2 == 0 ? 1f : -1f; }
}
