using System.Collections.Generic;
using UnityEngine;

// Keeps every spawn row passable: a new hazard may only enter the board if,
// across the lane, there is still a ship-width gap in its row.
//
// "Row" = every live hazard whose collider overlaps the new one's height
// padded by a ship gap above and below, so no window of the board a ship
// has to slip through is ever closed off by spawns landing next to each
// other. The heavies (EnemyRole.Big, ~1.1 u since they were made properly
// big) are what made this necessary: one heavy plus two rocks could wall off
// the lane. enmiesOnBoard routes the roster spawns through PickX/Fits;
// chasers are exempt (they rise from below and steer themselves).
public static class SpawnLane
{
    // A ship is ReferenceHullSize (0.58 u) across; a gap must clear it with
    // room to spare.
    public static float ShipGap => shopingShips.ReferenceHullSize * 1.3f;

    // Half the lane: the rail hardware sits at +/-2.35 (enmiesOnBoard.WorldRailX),
    // just inside the walls.
    public const float LaneHalf = 2.35f;

    // Heavies keep their centre within +/-HeavyMaxX: half a ~1.1 u heavy
    // further out still clears a rail mine's inner edge (2.35 - 0.31).
    public const float HeavyMaxX = 1.45f;

    const int Candidates = 8;

    // Half extents of a hazard's collider in the world. Tumbling rocks sweep
    // their square's diagonal, so they count at their widest.
    public static Vector2 HalfExtents(EnemyDef def)
    {
        Vector2 half = def.ColliderSize * .5f;
        if (def.role == EnemyRole.Rock && !def.floating)
            half = Vector2.one * Mathf.Max(half.x, half.y) * 1.4142f;
        return half;
    }

    static Vector2 HalfExtents(EnemyIdentity id)
    {
        var box = id.GetComponent<BoxCollider2D>();
        if (id.Def != null) return HalfExtents(id.Def);
        return box != null ? box.size * .5f : Vector2.one * .3f;
    }

    // Does a hazard of def at (x, y) still leave a ship-width gap in its row?
    public static bool Fits(EnemyDef def, float x, float y)
    {
        Vector2 half = HalfExtents(def);
        float band = half.y + ShipGap;
        var spans = RowSpans(y - band, y + band);
        spans.Add(new Vector2(x - half.x, x + half.x));
        return WidestGap(spans) >= ShipGap;
    }

    // A safe x for def near preferredX, or false if the row is too full.
    public static bool PickX(EnemyDef def, float preferredX, float y, out float x)
    {
        float max = def.role == EnemyRole.Big ? HeavyMaxX : LaneHalf - HalfExtents(def).x;
        x = Mathf.Clamp(preferredX, -max, max);
        if (Fits(def, x, y)) return true;
        for (int i = 0; i < Candidates; i++)
        {
            x = Random.Range(-max, max);
            if (Fits(def, x, y)) return true;
        }
        return false;
    }

    // The x spans of every live hazard (chasers aside) overlapping [y0, y1].
    public static List<Vector2> RowSpans(float y0, float y1)
    {
        var spans = new List<Vector2>();
        foreach (var id in Object.FindObjectsByType<EnemyIdentity>(FindObjectsSortMode.None))
        {
            if (id == null || (id.Def != null && id.Def.role == EnemyRole.Chaser)) continue;
            Vector2 half = HalfExtents(id);
            Vector3 p = id.transform.position;
            if (p.y + half.y < y0 || p.y - half.y > y1) continue;
            spans.Add(new Vector2(p.x - half.x, p.x + half.x));
        }
        return spans;
    }

    // The widest free stretch of the lane [-LaneHalf, LaneHalf] between spans.
    public static float WidestGap(List<Vector2> spans)
    {
        spans.Sort((a, b) => a.x.CompareTo(b.x));
        float cursor = -LaneHalf, best = 0f;
        foreach (var s in spans)
        {
            if (s.x > cursor) best = Mathf.Max(best, s.x - cursor);
            cursor = Mathf.Max(cursor, s.y);
        }
        return Mathf.Max(best, LaneHalf - cursor);
    }
}
