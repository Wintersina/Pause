using UnityEngine;

// Where the Lightning ship's Sky Javelins strike (ShipAttack.SkyJavelins).
//
// The count grows with the weapon steps (ShipWeaponUpgrades row 8): 3 javelins
// at the base, +1 on the first colour, +1 on the third, so 3 -> 4 -> 5 (the
// two cooldown steps between them leave the count alone). BaseCount and
// MaxCount below are that table's two ends.
//
// Lanes are evenly spaced about the ship (the centre of the group is the
// ship's x), launched centre-out so the middle lanes land first. The spacing
// is the loadout's range (1.2 u, authored for a phone) widened by
// LaneMetrics.Density on wide screens (up to 2x), then capped so the whole
// group fits inside the rails; if the ship is near a rail the group slides
// over as one (never squashed) so the lanes stay evenly spaced and inside.
public static class SkyJavelinLanes
{
    public const int BaseCount = 3;    // the stock ship
    public const int MaxCount = 5;     // every colour bought (ShipWeaponUpgrades)
    // Keep a javelin's centre this far inside the rail's inner edge / the screen edge.
    public const float EdgeMargin = .35f;

    // Each javelin hits for what one of the base three did (1/3 of a hit), however
    // many fly: more lanes means more cleared, never a weaker javelin. A boss is
    // still paced by its own hit window; a firing hits for at most MaxCount / BaseCount
    // hits only where a body spans several lanes.
    public const float WeightPerJavelin = 1f / BaseCount;

    // The lane limits right now: inside the rails and the view, whichever is narrower.
    public static void Limits(Rect view, out float minX, out float maxX)
    {
        float rail = BossRails.DrawnInnerEdge - EdgeMargin;
        minX = Mathf.Max(view.xMin + EdgeMargin, -rail);
        maxX = Mathf.Min(view.xMax - EdgeMargin, rail);
    }

    // The spacing between neighbouring lanes: `baseSpacing` widened with the
    // screen (`density` 1..2), at most what fits `count` lanes in minX..maxX.
    public static float Pitch(int count, float baseSpacing, float density, float minX, float maxX)
    {
        float pitch = baseSpacing * Mathf.Max(1f, density);
        if (count > 1) pitch = Mathf.Min(pitch, (maxX - minX) / (count - 1));
        return pitch;
    }

    // Lane offset (in lanes, from the group's centre) of the index-th javelin to
    // launch: centre first, then outward alternating left / right.
    //   odd count : 0, -1, +1, -2, +2
    //   even count: -.5, +.5, -1.5, +1.5
    public static float Offset(int index, int count)
    {
        if ((count & 1) == 1)
            return index == 0 ? 0f : (index % 2 == 1 ? -1f : 1f) * ((index + 1) / 2);
        return (index % 2 == 0 ? -1f : 1f) * (index / 2 + .5f);
    }

    // The x of the index-th of `count` javelins for a group centred on
    // `centreX`: slid as a whole so every lane is inside minX..maxX.
    public static float LaneX(int index, int count, float centreX, float pitch, float minX, float maxX)
    {
        float half = (count - 1) * .5f * pitch;
        float lo = minX + half, hi = maxX - half;
        float c = lo > hi ? (minX + maxX) * .5f : Mathf.Clamp(centreX, lo, hi);
        return c + Offset(index, count) * pitch;
    }
}
