using System.Collections.Generic;
using UnityEngine;

// SpawnSpace: the one place gameplay spawners ask "where may this enemy go?"
// so no two enemies ever sit on top of each other -- not at spawn, and not
// later when they move differently.
//
// ---- the model -------------------------------------------------------------
//
// BOARD SPACE. Everything that scrolls with the board (rocks, fighters,
// heavies, aliens, rail mines, pickups) moves down at the same rate
// (moveBackGround.speed x 30 u/s), so relative to each other they only move
// by what their own pattern adds on top of the scroll. Footprints are
// therefore compared in board space: world coordinates at this instant, as
// seen by an observer riding the scroll. A plain scroller's swept area is
// just its body; a weaving rock's is the band it weaves across; a chaser,
// which holds its place in the world while the board pours past it, rises
// through board space at the scroll speed.
//
// FOOTPRINTS. Every live enemy carries a SpawnFootprint (EnemyFactory adds
// it): its body half-extents (the drawn silhouette or collider, whichever is
// larger -- BodyHalf) and, optionally, its movement pattern as an
// IMovementFootprint. The planner inflates every rect by Margin.
//
// TWO KINDS OF PATTERN.
//   board-locked  (SelfSteering == false) -- the scroll plus a pattern the
//                 planner can bound ahead of time (straight, weave, rail).
//                 These are kept apart purely by placement: a spawn is only
//                 accepted where its sweep meets no live sweep in any time
//                 window (SweepWindows), so they can never meet later.
//   self-steering (SelfSteering == true) -- reacts to the player at run time
//                 (ChaserEnemy). The planner keeps spawns off its near-future
//                 path (windows starting before SteerHorizon), and the
//                 pattern resolves its own moves every frame through
//                 ResolveSteer(), which never lets it step into anyone.
//
// ---- adding a new movement / attack pattern ---------------------------------
//
//   1. Implement IMovementFootprint on the pattern (a component, or a plain
//      class the component delegates to -- see WeavePlan). SweptBounds must
//      cover every point the body may occupy, in board space, between `from`
//      and `to` seconds of flight from now. If the pattern can't predict its
//      timing (e.g. it keys off Time.time, which runs during pauses while
//      the board doesn't), return the whole envelope regardless of window.
//   2. Live: SpawnFootprint.Bind(go, pattern) once the pattern is attached.
//   3. Spawning: build a SpawnCandidate with the same pattern (a reusable
//      instance for its parameters -- no allocation per try) and ask
//      SpawnSpace.Fits() for each candidate spot; if nothing fits, defer.
//   4. A pattern that steers at run time returns SelfSteering = true and
//      routes each frame's move through ResolveSteer().
//
// Nothing here allocates per call: the registry is a static list kept by
// SpawnFootprint's OnEnable/OnDisable, and every query is a plain loop.
public interface IMovementFootprint
{
    // Board-space bounds of everything the body may cover from `from` to
    // `to` seconds of flight from now, for a body of half-extents `half`
    // centred at `center` now. Include the body; SpawnSpace adds Margin.
    Rect SweptBounds(Vector2 center, Vector2 half, float from, float to);

    // true: steers itself at run time and resolves its own moves
    // (ResolveSteer); false: board-locked, kept apart by placement alone.
    bool SelfSteering { get; }
}

// A footprint that also keeps a stretch of board clear of NEW spawns: an
// elite's column (EliteShip.SpawnShadow). Only placement reads it.
public interface ISpawnShadow
{
    bool SpawnShadow(out Rect column);
}

// One spot a spawner is considering.
public struct SpawnCandidate
{
    public Vector2 center;
    public Vector2 half;
    public IMovementFootprint plan;   // null: a plain scroller (body only)

    public SpawnCandidate(Vector2 center, Vector2 half, IMovementFootprint plan = null)
    {
        this.center = center;
        this.half = half;
        this.plan = plan;
    }

    public Rect Sweep(float from, float to)
    {
        return plan != null ? plan.SweptBounds(center, half, from, to) : SpawnSpace.BodyRect(center, half);
    }
}

public enum SpawnLayer { Enemy, Pickup }

public static class SpawnSpace
{
    // Clearance kept around every body, on each side (world units).
    public const float Margin = .06f;

    // Time windows (seconds of flight from now) the sweeps are compared in:
    // two footprints conflict only if they overlap in the same window, so a
    // future pattern with predictable timing (a dash at t = 1s) only blocks
    // the space it really uses, when it uses it. The last window runs for
    // the rest of the enemy's life on the board.
    public static readonly float[] SweepWindows = { 0f, .3f, 1f, Lifetime };
    public const float Lifetime = 1e4f;

    // Self-steering footprints are only reserved this far ahead: they get
    // themselves out of the way after that (ResolveSteer).
    public const float SteerHorizon = .3f;

    // The board scroll in world units per second (what every board-locked
    // mover applies).
    public static float ScrollSpeed => moveBackGround.speed * 30f;

    // The clock the weave patterns read (Time.time in play; a headless
    // simulation sets ClockOverride).
    public static float? ClockOverride;
    public static float Clock => ClockOverride ?? Time.time;

    // ---- registry ---------------------------------------------------------

    static readonly List<SpawnFootprint> enemies = new List<SpawnFootprint>(128);
    static readonly List<SpawnFootprint> pickups = new List<SpawnFootprint>(128);

    public static List<SpawnFootprint> Live(SpawnLayer layer) => layer == SpawnLayer.Enemy ? enemies : pickups;
    public static int EnemyCount => enemies.Count;

    internal static void Register(SpawnFootprint f)
    {
        var list = Live(f.layer);
        f.registryIndex = list.Count;
        list.Add(f);
    }

    internal static void Unregister(SpawnFootprint f)
    {
        var list = Live(f.layer);
        int i = f.registryIndex;
        if (i < 0 || i >= list.Count || list[i] != f)
        {
            i = list.IndexOf(f);
            if (i < 0) return;
        }
        int last = list.Count - 1;
        if (i != last)
        {
            list[i] = list[last];
            list[i].registryIndex = i;
        }
        list.RemoveAt(last);
        f.registryIndex = -1;
    }

    // ---- sizes ------------------------------------------------------------

    // A roster enemy's body: the larger of its drawn silhouette
    // (EnemyRoster.TargetWidth) and its collider (as SpawnLane counts it --
    // tumbling rocks at their diagonal), as a square. `scale`: the body's
    // size (HazardSize: a rock drawn small or large; 1 for everything else).
    public static Vector2 BodyHalf(EnemyDef def, float scale = 1f)
    {
        if (def == null) return Vector2.one * .3f;
        Vector2 lane = SpawnLane.HalfExtents(def, scale);
        // (two-argument Max: the three-argument one is params float[], an allocation)
        float half = Mathf.Max(Mathf.Max(EnemyRoster.TargetWidth(def.role) * .5f * scale, lane.x), lane.y);
        return new Vector2(half, half);
    }

    // Anything else (an inspector prefab override, a pickup): its collider's
    // world bounds, else its renderer's.
    public static Vector2 BodyHalf(GameObject go)
    {
        EnemyIdentity id;
        if (go.TryGetComponent(out id) && id.Def != null) return BodyHalf(id.Def, id.Scale);
        Collider2D col;
        if (go.TryGetComponent(out col) && col.enabled)
        {
            Vector2 e = col.bounds.extents;
            if (e.x > .01f || e.y > .01f) return e;
            var box = col as BoxCollider2D;
            if (box != null) return Vector2.Scale(box.size, go.transform.lossyScale) * .5f;
            var circle = col as CircleCollider2D;
            if (circle != null)
                return Vector2.one * circle.radius * Mathf.Max(go.transform.lossyScale.x, go.transform.lossyScale.y);
        }
        var r = go.GetComponentInChildren<Renderer>();
        if (r != null && r.bounds.extents.x > .01f) return r.bounds.extents;
        return Vector2.one * .3f;
    }

    public static Rect BodyRect(Vector2 center, Vector2 half)
    {
        return new Rect(center.x - half.x, center.y - half.y, half.x * 2f, half.y * 2f);
    }

    static bool Overlaps(Rect a, Rect b, float margin)
    {
        // each rect carries Margin, so two bodies keep 2 x Margin between them
        return a.xMin - margin < b.xMax + margin && b.xMin - margin < a.xMax + margin &&
               a.yMin - margin < b.yMax + margin && b.yMin - margin < a.yMax + margin;
    }

    // ---- placement --------------------------------------------------------

    // Does the candidate's sweep stay clear of every live footprint on
    // `layer` (Margin included) in every time window? `ignore` skips one
    // footprint (the mover itself, when re-checking).
    public static bool Fits(SpawnCandidate c, SpawnLayer layer = SpawnLayer.Enemy, SpawnFootprint ignore = null)
    {
        var list = Live(layer);
        bool candidatePlan = c.plan != null;
        Rect body = BodyRect(c.center, c.half);
        for (int i = 0; i < list.Count; i++)
        {
            var f = list[i];
            if (f == null || f == ignore) continue;
            IMovementFootprint plan = f.Plan;
            bool steering = plan != null && plan.SelfSteering;
            bool held = f.Held;
            if (plan is ISpawnShadow shadow && shadow.SpawnShadow(out Rect column))
            {
                Rect whole = candidatePlan ? c.plan.SweptBounds(c.center, c.half, 0f, SteerHorizon) : body;
                if (Overlaps(whole, column, Margin)) return false;
            }
            if (!candidatePlan && plan == null && !held)
            {
                // two plain scrollers: they never move relative to each other
                if (Overlaps(body, f.Body, Margin)) return false;
                continue;
            }
            for (int w = 0; w < SweepWindows.Length - 1; w++)
            {
                float from = SweepWindows[w], to = SweepWindows[w + 1];
                if ((steering || held) && from >= SteerHorizon) break;
                Rect a = candidatePlan ? c.plan.SweptBounds(c.center, c.half, from, to) : body;
                if (Overlaps(a, f.Sweep(from, to), Margin)) return false;
            }
        }
        return true;
    }

    // The same check against the other layer as well: an enemy prefers a
    // spot clear of pickups, a pickup one clear of enemies.
    public static bool FitsBoth(SpawnCandidate c)
    {
        return Fits(c, SpawnLayer.Enemy) && Fits(c, SpawnLayer.Pickup);
    }

    // A pickup at `pos` (half extents `half`, plain scroller) that stays out
    // of every enemy's footprint: tries a few x's within [minX, maxX] then a
    // short lift above the spawn line. Cheap and soft -- when the board is
    // packed it keeps the original spot (pickups are harmless to overlap).
    public static Vector3 PickupSpot(Vector3 pos, Vector2 half, float minX, float maxX)
    {
        var c = new SpawnCandidate(pos, half);
        if (Fits(c)) return pos;
        for (int lift = 0; lift < 3; lift++)
        {
            for (int k = 0; k < 6; k++)
            {
                c.center = new Vector2(Random.Range(minX, maxX), pos.y + lift * .45f);
                if (Fits(c)) return new Vector3(c.center.x, c.center.y, pos.z);
            }
        }
        return pos;
    }

    // ---- self-steering -----------------------------------------------------

    // For a self-steering pattern (ChaserEnemy): given where it stood at the
    // start of this frame and where it wants to be now, returns a position
    // whose body is clear of every other enemy's envelope -- board-locked
    // ones at their whole sweep (a weaving rock's band), other steerers at
    // their body. Call it after the board-locked movers have run (LateUpdate,
    // or in order in a simulation). Tries, in order:
    //   the wish; the wish's sideways part with the scroll carrying it
    //   (sidestepping `dodgeStep` either way); being carried with the board
    //   (always clear of every board-locked envelope if it started clear);
    //   holding still (clear of steerers that already moved).
    public static Vector2 ResolveSteer(SpawnFootprint self, Vector2 from, Vector2 wish, float scrollStep, float dodgeStep)
    {
        Vector2 half = self.half;
        if (ClearForSteerer(self, wish, half)) return wish;

        Vector2 carried = new Vector2(from.x, from.y - scrollStep);
        Vector2 sideways = new Vector2(wish.x, carried.y);
        if (ClearForSteerer(self, sideways, half)) return sideways;
        float laneLimit = SpawnLane.LaneHalf + .3f - half.x;
        for (int s = 1; s <= 2; s++)
        {
            float step = dodgeStep * s;
            Vector2 left = new Vector2(Mathf.Max(-laneLimit, carried.x - step), carried.y);
            Vector2 right = new Vector2(Mathf.Min(laneLimit, carried.x + step), carried.y);
            // sidestep away from the wish's side first
            bool preferRight = wish.x >= from.x;
            Vector2 first = preferRight ? right : left, second = preferRight ? left : right;
            if (ClearForSteerer(self, first, half)) return first;
            if (ClearForSteerer(self, second, half)) return second;
        }
        if (ClearForSteerer(self, carried, half)) return carried;
        if (ClearForSteerer(self, from, half)) return from;
        return carried;
    }

    // Clear of every other enemy's steering envelope at this instant?
    public static bool ClearForSteerer(SpawnFootprint self, Vector2 at, Vector2 half)
    {
        Rect body = BodyRect(at, half);
        for (int i = 0; i < enemies.Count; i++)
        {
            var f = enemies[i];
            if (f == null || f == self) continue;
            if (Overlaps(body, f.Envelope(), Margin)) return false;
        }
        return true;
    }

    // The closest enemy envelope straight ahead of a steerer within
    // `lookUp` units above it (board-relative), or false. Used to start a
    // sidestep early instead of being carried.
    public static bool ThreatAhead(SpawnFootprint self, Vector2 at, Vector2 half, float lookUp, out Rect threat)
    {
        Rect probe = new Rect(at.x - half.x, at.y - half.y, half.x * 2f, half.y * 2f + Mathf.Max(0f, lookUp));
        float best = float.MaxValue;
        threat = default;
        bool found = false;
        for (int i = 0; i < enemies.Count; i++)
        {
            var f = enemies[i];
            if (f == null || f == self) continue;
            Rect e = f.Envelope();
            if (!Overlaps(probe, e, Margin)) continue;
            float d = e.yMin - at.y;
            if (d < best) { best = d; threat = e; found = true; }
        }
        return found;
    }

    // ---- diagnostics (tests) ----------------------------------------------

    // Are any two live enemy bodies overlapping right now (no margin)?
    public static bool AnyBodiesOverlap(out SpawnFootprint a, out SpawnFootprint b)
    {
        for (int i = 0; i < enemies.Count; i++)
        {
            Rect ri = enemies[i].Body;
            for (int j = i + 1; j < enemies.Count; j++)
                if (Overlaps(ri, enemies[j].Body, 0f)) { a = enemies[i]; b = enemies[j]; return true; }
        }
        a = b = null;
        return false;
    }
}
