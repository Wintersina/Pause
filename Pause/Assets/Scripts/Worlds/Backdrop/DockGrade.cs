using System.Collections.Generic;
using UnityEngine;

// Elite docks in their backdrop: the colour they are drawn in (so a dock hull / pad sits in its layer's light, not above it) and the
// rule that only one dock is ever on the ground at a time.
public static partial class DockGrade
{
    // The game's rules (one dock at a time, solid ground only, no floating kinds). Editor tests that exercise every dock drawing and kind
    // (art, strips, launch) switch it off to place any kind anywhere; DockAuditTest holds the strict rules.
    public static bool Strict = true;

    // The colour multiplier of dock drawing `closedSprite` of `world` on ground set `variant` (white: none known). Allocates (it builds the
    // key): directors call Tints once when they are built and keep the result.
    public static Color Tint(string world, int variant, string closedSprite)
    {
        if (closedSprite == null) return Color.white;
        int i = System.Array.IndexOf(tableKeys, world + "/" + variant + "/" + closedSprite);
        if (i < 0) i = System.Array.IndexOf(tableKeys, world + "/*/" + closedSprite);
        return i < 0 ? Color.white : new Color(tableVals[i * 3], tableVals[i * 3 + 1], tableVals[i * 3 + 2], 1f);
    }

    // The tints of every dock drawing of a world (its SiteClosed names), for a director to keep.
    public static Color[] Tints(string world, int variant, string[] closedSprites)
    {
        var c = new Color[closedSprites.Length];
        for (int i = 0; i < c.Length; i++) c[i] = Tint(world, variant, closedSprites[i]);
        return c;
    }

    // Is a dock still anywhere on the ground, not yet scrolled out under the view? (Then no new one is placed: one dock at a time.)
    public static bool AnyOnGround(BackdropPool sites, float halfH, BackdropPiece except = null)
    {
        if (!Strict) return false;
        for (int i = 0; i < sites.items.Count; i++)
        {
            var p = sites.items[i];
            if (!p.active || p == except || p.sr == null || p.sr.sprite == null) continue;
            if (p.y > -halfH - p.size * .6f) return true;
        }
        return false;
    }
}
