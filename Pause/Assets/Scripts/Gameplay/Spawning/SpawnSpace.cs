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

    // Do ordinary enemies steer round, slow for or stop short of each other at run time? OFF: they follow their own paths and
    // simply overlap (FriendlyFire's crashes and beams settle it); only elites keep clear of other enemies (their own code,
    // EliteEvasion). Spawn-time placement (Fits) is unaffected. Static (not const) so a test of the avoidance rules may switch it on.
    public static bool RegularsAvoid = false;

    // See ResolveSteer: the spare room a steerer that gave way wants before it returns to its wish.
    public const float YieldBand = .15f;

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
            Physics2D.SyncTransforms();   // (auto sync is off: bounds follow the last sync)
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
        if (!RegularsAvoid) return wish;
        Vector2 half = self.half;
        // Hysteresis: one that gave way last frame only goes back to its wish once that is clear
        // with YieldBand to spare. The neighbour is carried along with the board, so the spot it
        // was carried out of is clear again a frame later; without the band it stepped back into
        // it, was carried out again, and so on: a vibration the size of one scroll step.
        if (ClearForSteerer(self, wish, self.yielded ? half + Vector2.one * YieldBand : half)) { self.yielded = false; return wish; }
        self.yielded = true;

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
        // Boxed in: every envelope-clear spot is gone. Still never into a body:
        // the nearest spot that touches none -- where it wishes, or carried
        // down with the board / holding its place, each as it is or a few
        // dodge steps sideways (away from the wish's side first).
        if (ClearOfBodies(self, wish, half)) return wish;
        if (ClearOfBodies(self, sideways, half)) return sideways;
        bool right1 = wish.x >= from.x;
        for (int s = 0; s <= 6; s++)
            for (int h = 0; h < 2; h++)
            {
                float y = h == 0 ? carried.y : from.y;
                float step = dodgeStep * s;
                Vector2 first = new Vector2(Mathf.Clamp(from.x + (right1 ? step : -step), -laneLimit, laneLimit), y);
                Vector2 second = new Vector2(Mathf.Clamp(from.x + (right1 ? -step : step), -laneLimit, laneLimit), y);
                if (ClearOfBodies(self, first, half)) return first;
                if (s > 0 && ClearOfBodies(self, second, half)) return second;
            }
        return carried;
    }

    // ---- SOFT AVOIDANCE (the way free movers now keep off each other) -----------
    //
    // ResolveSteer is a ladder of hard tests re-run every frame: the wish, the
    // wish sideways, a step left, a step right, carried, held -- each an on / off
    // envelope check. Two bodies close together flipped between rungs frame to
    // frame (and a chaser poking at the edge of a falling rock's probe, or a
    // pilot's band cut in and out as a hazard crossed its look window, did the
    // same): the shake on the dense boards (EnemyAvoidanceTest has the numbers).
    //
    // Soft avoidance replaces that with ONE continuous rule. Another body
    // within SoftReach of touching pushes the steerer sideways with a force that
    // rises smoothly (smoothstep) from nothing at the edge of the reach to
    // SoftSpeed at touching and to SoftDeepSpeed deep inside; the push is
    // low-passed (SoftTau) and its change capped (SoftAccel), so no choice can
    // flip faster than that; the sign comes from the sideways offset plus a
    // tiny stable bias (never a coin toss at dead centre). It never forbids a
    // step: a body that drives on into a neighbour (a fleeing pilot, a chaser
    // on its line) is not stopped -- FriendlyFire's crash resolves it -- and a
    // pair stacked on one spot is parted by the deep push (FriendlyFire usually
    // gets there first). One loop over the enemy list a call, no allocation.
    public static bool Soft = true;
    public const float SoftReach = .25f;        // u beyond touching where the push starts (zero there)
    public const float SoftSpeed = 1.2f;        // u/s of drift at touching
    public const float SoftDeepSpeed = 2.4f;    // u/s when bodies are half a body inside each other
    public const float SoftTau = .3f;           // seconds: the low-pass on the drift
    public const float SoftAccel = 5f;          // u/s^2: the most the drift velocity may change by
    public const float SoftEdge = .15f, SoftEdgeFade = .4f;   // u: how far past the lane edge a body may drift, and over how much room the outward push fades
    public const float SoftBias = .06f;         // u: stable tie-break (by instance id) for bodies dead in line
    public const float SoftMaxOffset = 1f;      // u: how far a pilot's line may be drifted
    public const float SoftReturn = .4f;        // u/s: a drifted pilot's way back to its line

    // The sideways drift velocity of `self` right now (u/s, + to the right),
    // low-passed and rate-capped; advances self's filter by dt. `cap` scales
    // the speeds (a nimble steerer may drift faster). `lookUp`: the body is
    // read this much taller upward (something coming down on it).
    public static float SoftDrift(SpawnFootprint self, float dt, float cap = 1f, float lookUp = 0f)
    {
        if (!RegularsAvoid) { self.softV = 0f; return 0f; }
        if (dt <= 0f) return self.softV;
        Vector2 c = self.transform.position;
        Vector2 half = self.half;
        float cy = c.y + lookUp * .5f, hy = half.y + lookUp * .5f;
        int idSelf = self.GetInstanceID();
        float push = 0f;
        for (int i = 0; i < enemies.Count; i++)
        {
            var f = enemies[i];
            if (f == null || f == self) continue;
            Vector2 o = f.transform.position;
            float dx = c.x - o.x;
            float px = half.x + f.half.x + SoftReach - Mathf.Abs(dx);
            if (px <= 0f) continue;
            float py = hy + f.half.y + SoftReach - Mathf.Abs(cy - o.y);
            if (py <= 0f) continue;
            float depth = Mathf.Min(px, py);
            float w = Mathf.Clamp01(depth / (SoftReach * 2f));
            w = w * w * (3f - 2f * w);
            float deep = Mathf.Clamp01((depth - SoftReach) / Mathf.Max(.1f, Mathf.Min(half.x, f.half.x)));
            float bias = idSelf > f.GetInstanceID() ? SoftBias : -SoftBias;
            float dir = Mathf.Clamp((dx + bias) / .3f, -1f, 1f);
            push += dir * (SoftSpeed * w + (SoftDeepSpeed - SoftSpeed) * deep);
        }
        // Towards the lane's edge the outward push fades out (smoothly) instead of running into the lane clamp,
        // which would switch it off abruptly each time the body reached it.
        if (push * c.x > 0f)
        {
            float room = SpawnLane.LaneHalf + SoftEdge - half.x - Mathf.Abs(c.x);
            push *= Mathf.Clamp01(room / SoftEdgeFade);
        }
        float max = SoftDeepSpeed * cap;
        float target = Mathf.Clamp(push * cap, -max, max);
        float k = 1f - Mathf.Exp(-dt / SoftTau);
        float next = Mathf.Lerp(self.softV, target, k);
        self.softV = Mathf.MoveTowards(self.softV, next, SoftAccel * cap * dt);
        return self.softV;
    }

    // A pilot's damped offset from its line: the drift integrated, with a slow way home.
    public static float SoftOffset(SpawnFootprint self, float dt)
    {
        if (!RegularsAvoid) { self.SoftX = 0f; return 0f; }
        float v = SoftDrift(self, dt);
        float x = self.SoftX + v * dt;
        x = Mathf.MoveTowards(x, 0f, SoftReturn * dt);
        x = Mathf.Clamp(x, -SoftMaxOffset, SoftMaxOffset);
        self.SoftX = x;
        return x;
    }

    // Forget the drift (a pilot leaving, a body re-seated): flying away is never held back.
    public static void SoftRelease(SpawnFootprint self, float dt)
    {
        self.softV = Mathf.MoveTowards(self.softV, 0f, SoftAccel * 2f * dt);
        self.SoftX = Mathf.MoveTowards(self.SoftX, 0f, SoftReturn * 3f * dt);
    }

    // For a chaser: the move it wants with the soft drift added (or ResolveSteer's hard rules when Soft is off).
    public static Vector2 Steer(SpawnFootprint self, Vector2 from, Vector2 wish, float scrollStep, float dodgeStep, float dt,
                                float cap = 1f, float lookUp = 0f)
    {
        if (!RegularsAvoid) return wish;
        if (!Soft) return ResolveSteer(self, from, wish, scrollStep, dodgeStep);
        float v = SoftDrift(self, dt, cap, lookUp);
        float limit = SpawnLane.LaneHalf + .3f - self.half.x;
        float x = wish.x + v * dt;
        // (the lane clamp only ever pulls the drift back, never the wish itself)
        if (Mathf.Abs(x) > limit) x = Mathf.Sign(x) * Mathf.Max(limit, Mathf.Abs(wish.x));
        wish.x = x;
        return wish;
    }

    // Touching no other enemy's body at this instant (a hair of margin, so
    // resting against one never reads as overlap through rounding)?
    static bool ClearOfBodies(SpawnFootprint self, Vector2 at, Vector2 half)
    {
        Rect body = BodyRect(at, half);
        for (int i = 0; i < enemies.Count; i++)
        {
            var f = enemies[i];
            if (f == null || f == self) continue;
            if (Overlaps(body, f.Body, .01f)) return false;
        }
        return true;
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

    // Clear of every other enemy's envelope, for a sweep (a rail mine's whole
    // slide, riding against the board)?
    public static bool ClearForSweep(SpawnFootprint self, Rect sweep)
    {
        if (!RegularsAvoid) return true;
        for (int i = 0; i < enemies.Count; i++)
        {
            var f = enemies[i];
            if (f == null || f == self) continue;
            if (Overlaps(sweep, f.Envelope(), Margin)) return false;
        }
        return true;
    }

    // The closest enemy envelope straight ahead of a steerer within
    // `lookUp` units above it (board-relative), or false. Used to start a
    // sidestep early instead of being carried.
    public static bool ThreatAhead(SpawnFootprint self, Vector2 at, Vector2 half, float lookUp, out Rect threat)
    {
        if (!RegularsAvoid) { threat = default; return false; }
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
