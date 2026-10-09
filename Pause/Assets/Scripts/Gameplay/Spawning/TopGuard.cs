using UnityEngine;

// Fair spawns for a ship parked at the top of its zone.
//
// The ship may now fly up under the score board (ShipReach.TopFor), so the
// strip between the spawn line (SpawnAboveCamera: just over the view's top)
// and its hull is only the HUD band deep -- about 2 u on a phone. Scrolling
// at HUD 35 (10.5 u/s) a hazard born over the ship would reach it in 0.2 s.
// So a scrolling hazard is not placed in a column a ship up there stands in
// unless the player would have ReactSeconds to see it coming: the spawner
// (enmiesOnBoard.TryPlace) skips that spot, tries another column and, with
// none, defers like any blocked spawn. Pilots are not affected: they hold
// their own stations and keep the standoff above the ship (HostileReach).
public static class TopGuard
{
    // Least time a hazard born at the spawn line needs to reach the hull's top (s).
    public static float ReactSeconds = .5f;
    // Extra width either side of the hull and the hazard that counts as the same column (u).
    public static float Side = .35f;
    public static bool Enabled = true;

    // Would a hazard whose swept columns span [xMin, xMax] and whose body
    // reaches `halfY` under its centre, born at `spawnY`, reach the ship's
    // top edge sooner than ReactSeconds at `scrollSpeed` (u/s)?
    public static bool Blocks(float xMin, float xMax, float spawnY, float halfY, Vector3 ship, float scrollSpeed,
                              float hullHalfWidth, float hullAbove)
    {
        if (!Enabled || scrollSpeed <= 0f) return false;
        if (ship.x < xMin - hullHalfWidth - Side || ship.x > xMax + hullHalfWidth + Side) return false;
        float gap = (spawnY - halfY) - (ship.y + hullAbove);
        return gap < ReactSeconds * scrollSpeed;
    }

    // Live: the run's ship.
    public static bool BlocksLive(float xMin, float xMax, float spawnY, float halfY)
    {
        var ship = EliteSystem.Player;
        if (ship == null) return false;
        return Blocks(xMin, xMax, spawnY, halfY, ship.position, SpawnSpace.ScrollSpeed, ShipScale.HullHalfWidth, ShipReach.HullAbove);
    }
}
