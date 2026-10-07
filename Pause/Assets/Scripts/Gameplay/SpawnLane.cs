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
// the lane. enmiesOnBoard routes the roster spawns through Fits (next to
// SpawnSpace, which keeps the bodies themselves apart); chasers are exempt
// (they rise from below and steer themselves). Live hazards come from
// SpawnSpace's registry (every roster enemy has a SpawnFootprint).
public static class SpawnLane
{
    // A ship is ReferenceHullSize (0.58 u) across; a gap must clear it with
    // room to spare. The damage hitbox is tighter than the art now
    // (ShipHitbox: the widest, UFO's disc, is ~0.56 u), so this still clears
    // every hull with >= 30% to spare (ShipHitboxTest).
    public static float ShipGap => shopingShips.ReferenceHullSize * 1.3f;

    // Half the lane: the rail hardware sits at +/-2.35 (enmiesOnBoard.WorldRailX),
    // just inside the walls.
    public const float LaneHalf = 2.35f;

    // Heavies keep their centre within +/-HeavyMaxX: half a ~1.1 u heavy
    // further out still clears a rail mine's inner edge (2.35 - 0.31).
    public const float HeavyMaxX = 1.45f;

    const int Candidates = 8;

    // Half extents of a hazard's collider in the world. Tumbling rocks sweep
    // their square's diagonal, so they count at their widest. `scale`: the
    // body's size (HazardSize; a rock drawn small or large).
    public static Vector2 HalfExtents(EnemyDef def, float scale = 1f)
    {
        Vector2 half = def.ColliderSize * .5f * scale;
        if (def.role == EnemyRole.Rock && !def.floating)
            half = Vector2.one * Mathf.Max(half.x, half.y) * 1.4142f;
        return half;
    }

    static Vector2 HalfExtents(EnemyIdentity id)
    {
        if (id.Def != null) return HalfExtents(id.Def, id.Scale);
        var box = id.GetComponent<BoxCollider2D>();
        return box != null ? box.size * .5f : Vector2.one * .3f;
    }

    // Does a hazard of def (at `scale`) at (x, y) still leave a ship-width
    // gap in its row? Reads SpawnSpace's live registry into a reused buffer:
    // no scene scan, no allocation.
    public static bool Fits(EnemyDef def, float x, float y, float scale = 1f)
    {
        Vector2 half = HalfExtents(def, scale);
        float band = half.y + ShipGap;
        FillRowSpans(y - band, y + band, buffer);
        buffer.Add(new Vector2(x - half.x, x + half.x));
        return WidestGap(buffer) >= GuaranteedGap;
    }

    // The gap every row is promised: a ship's width -- shrinking to nothing
    // once a portal has been kept waiting deep into overdrive
    // (PortalPressure.ShipGapScale; x1 at every other time), which is what
    // finally makes staying fatal.
    public static float GuaranteedGap => ShipGap * PortalPressure.ShipGapScale;

    static readonly List<Vector2> buffer = new List<Vector2>(64);

    // A safe x for def near preferredX, or false if the row is too full.
    public static bool PickX(EnemyDef def, float preferredX, float y, out float x)
    {
        float max = MaxX(def);
        x = Mathf.Clamp(preferredX, -max, max);
        if (Fits(def, x, y)) return true;
        for (int i = 0; i < Candidates; i++)
        {
            x = Random.Range(-max, max);
            if (Fits(def, x, y)) return true;
        }
        return false;
    }

    // How far from the centre line def's centre may spawn.
    public static float MaxX(EnemyDef def, float scale = 1f)
    {
        return def.role == EnemyRole.Big ? HeavyMaxX : LaneHalf - HalfExtents(def, scale).x;
    }

    // The x spans of every live hazard (chasers aside) overlapping [y0, y1].
    // (A fresh list, for tests; the guard itself fills a reused buffer.)
    public static List<Vector2> RowSpans(float y0, float y1)
    {
        var spans = new List<Vector2>();
        FillRowSpans(y0, y1, spans);
        return spans;
    }

    static void FillRowSpans(float y0, float y1, List<Vector2> spans)
    {
        spans.Clear();
        var live = SpawnSpace.Live(SpawnLayer.Enemy);
        for (int i = 0; i < live.Count; i++)
        {
            var f = live[i];
            if (f == null) continue;
            EnemyIdentity id;
            Vector2 half;
            if (f.TryGetComponent(out id))
            {
                if (id.Def != null && id.Def.role == EnemyRole.Chaser) continue;
                half = HalfExtents(id);
            }
            else half = f.half;
            Vector3 p = f.transform.position;
            if (p.y + half.y < y0 || p.y - half.y > y1) continue;
            spans.Add(new Vector2(p.x - half.x, p.x + half.x));
        }
    }

    // The widest free stretch of the lane [-LaneHalf, LaneHalf] between spans.
    public static float WidestGap(List<Vector2> spans)
    {
        // insertion sort by left edge (rows hold a handful; no comparer alloc)
        for (int i = 1; i < spans.Count; i++)
        {
            Vector2 v = spans[i];
            int j = i - 1;
            while (j >= 0 && spans[j].x > v.x) { spans[j + 1] = spans[j]; j--; }
            spans[j + 1] = v;
        }
        float cursor = -LaneHalf, best = 0f;
        for (int i = 0; i < spans.Count; i++)
        {
            Vector2 s = spans[i];
            if (s.x > cursor) best = Mathf.Max(best, s.x - cursor);
            cursor = Mathf.Max(cursor, s.y);
        }
        return Mathf.Max(best, LaneHalf - cursor);
    }
}
