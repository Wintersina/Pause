using UnityEngine;

// The cover boulders' half of the seam with the world hazards (HazardCoverSeam, docs/world-weather-hazards.md section 11):
// hazards ask this before they START (and fade out when a boulder appears); the boulder director asks WorldHazards.AnyActive
// before it spawns (CoverBoulderDirector.Blocked "hazard"). The two never share the screen.
public sealed class CoverHazardQuery : ICoverQuery
{
    public static readonly CoverHazardQuery Instance = new CoverHazardQuery();

    // A boulder is alive (solid, or still bursting / fading: the board is not clean yet).
    public bool AnyOnScreen { get { return CoverBoulders.Alive > 0; } }

    // A live boulder's bounding circle touches this world rectangle.
    public bool AnyWithin(Rect world)
    {
        foreach (var b in CoverBoulders.All)
        {
            float r = b.Bound;
            if (b.Pos.x + r < world.xMin || b.Pos.x - r > world.xMax || b.Pos.y + r < world.yMin || b.Pos.y - r > world.yMax) continue;
            return true;
        }
        return false;
    }
}
