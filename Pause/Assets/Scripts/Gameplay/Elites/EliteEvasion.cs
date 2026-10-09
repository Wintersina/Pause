using UnityEngine;

// The elites' eyes and judgement: what on the board can hurt them in the next
// moments, and where to fly to stay out of it. (docs/enemy-behaviours.md,
// "Elite evasion", has the design and the measured before / after.)
//
// SENSE, once a step for every elite together (EliteSystem.Step): one flat
// list of threats -- position, velocity, radius, when it is live -- built
// from registries that already exist, with no physics query, no lookup and
// no allocation:
//   * every hazard body (ClearTarget.Live: rocks, enemies, mines, chasers),
//     moving at the velocity it was MEASURED at since the last step, so a
//     weave, a brake, a lunge and a chaser all read true;
//   * a roster enemy's body dash once it is telegraphed (EnemyBrain's
//     windup): the stretch of board it is about to cross;
//   * every elite shot in the air, an elite's own included once it could
//     hit it, a landed resin pool, and the marked spot a lobbed glob is
//     about to land on;
//   * the pilot's own shots, only if PlayerShotAwareness is raised above 0.
// Roster enemies' shots count too (hostile fire: they cost an elite a heart).
// Other elites, the rails and the pilot are read per ship in Plan.
//
// PLAN, per elite, every ReactionSeconds (its reaction delay; EliteShip.
// Navigate): the brain says where it WANTS to be. If the way there is clear
// it flies exactly that -- its own pattern, untouched. If not, candidate
// spots round the ship (a fine comb sideways, each a little higher and a
// little lower) are each flown on paper -- the swing of its velocity at its
// real acceleration, the run to the spot, then holding there -- against
// every threat's straight-line path, in closed form, and against the rails.
// It takes the cheapest: a hit inside its commit window (CommitShare of the
// look-ahead) costs most, the sooner the worse; a hit read further off only
// makes a lane less attractive; then a tight squeeze, then distance from
// the brain's wish. So it sidesteps, climbs, drops back or holds, and goes
// back to its pattern the moment that is clean. The ship steers to the spot
// with its own arrive / accelerate / turn limits (sharper while evading,
// capped): it swerves, it does not teleport, and what arrives inside its
// reaction time still catches it.
//
// LIFT-OFF asks the same question about the air it would join (BestJoin):
// it waits on its pad for a clear moment, slides its join point as it rises
// and hovers just under the play layer -- still out of reach, as the whole
// lift-off always was -- until the spot is clear (each wait capped).
//
// THE SPAWN SHADOW (EliteShip.SpawnShadow, read by SpawnSpace.Fits): the
// spawner drops nothing new into the column an elite is flying. The board
// only guarantees one ship-wide gap a row, wherever it falls; an elite is
// wider and far slower sideways than the pilot's finger, so without this a
// fast board walls it in however well it reads it (the probe shows both).
//
// Friendly fire is unchanged: everything here is avoidance. A committed
// dash, what is behind the pilot when it dashes, a blink that loses the
// pilot, another elite's shot it could not get clear of and a crowded board
// still hurt and kill elites.
public static class EliteEvasion
{
    // ---- TUNABLES (every number of the evasion lives here) ----------------

    // Off: elites fly exactly as they did before (the old reflex only).
    public static bool Enabled = true;

    // How far ahead an elite reads the board, seconds, for the most skilled
    // (EliteDef.avoidance 1); a clumsy one (0) reads LookAheadUnskilled of it.
    public static float LookAheadSeconds = 1.15f;
    public static float LookAheadUnskilled = .7f;
    // The share of its look-ahead inside which a coming hit is a matter of
    // NOW: it takes whatever line gets it clear. A hit read further off
    // than that is only a lane to prefer not to be in -- the board will
    // have moved by then, and an elite that fled every distant maybe would
    // never fly its own pattern.
    public static float CommitShare = .45f;
    // Seconds between two reads of the board: its reaction delay. Between
    // reads it keeps flying its last decision. Clumsy ones are slower.
    public static float ReactionSeconds = .14f;
    public static float ReactionUnskilled = 1.5f;
    // While evading: x its def's accel (never past MaxEvadeAccel u/s^2), x
    // its turn rate, and x its cruise speed -- or EvadeBoardShare of the
    // board's scroll if that is more (a faster stream takes a faster hand),
    // never past MaxEvadeSpeed u/s.
    public static float MaxEvadeAccel = 13f;
    public static float EvadeAccelScale = 1.8f;
    public static float EvadeSpeedScale = 1.45f;
    public static float EvadeBoardShare = .45f;
    public static float MaxEvadeSpeed = 5.5f;
    public static float EvadeTurnScale = 2.5f;
    // The gap it likes to keep between its hull and anything it passes (u):
    // it takes a tighter line only when there is no roomier one.
    public static float SafetyMargin = .16f;
    // Extra room between two elites' chosen spots, and round the pilot when
    // it sidesteps (it never evades INTO the ship).
    public static float EliteSpacing = .45f;
    public static float PilotSpacing = .9f;
    // A body's path is read as a straight line at its measured speed; it
    // weaves, brakes and bounces, so the room it is given grows by this much
    // for every second ahead (u/s), plus WeaveSlack of the sideways pace its
    // pattern can reach (EnemyBrain.LateralPace, at most MaxWeavePace).
    // Shots fly true: none for them.
    public static float PredictionSlack = .25f;
    public static float WeaveSlack = .7f;
    public static float MaxWeavePace = 1.2f;
    // THE SPAWN SHADOW. The spawner never drops a new body into the column
    // an elite is flying (hull-wide, from the ship to where it means to
    // be), as far up the board as the scroll covers in this many seconds:
    // what arrives faster than a pilot could possibly move aside. 0: off
    // (the spawner then only keeps off the ship itself, as it always did).
    public static float SpawnShadowSeconds = 1.2f;
    public static float SpawnShadowPad = .12f;
    public static float SpawnShadowLead = 1f;    // how far towards its goal the column stretches (u)
    // How far above the view it sees hazards coming (u).
    public static float SenseAboveView = 4f;
    // Lift-off: the join spot must be clear for this long after it joins;
    // it may wait this long on the pad, and this long hovering under the
    // play layer; the join point slides at most this fast (u/s).
    public static float LiftClearSeconds = .8f;
    public static float LiftDelayMax = 2f;
    public static float LiftHoldMax = 1.5f;
    public static float LiftSlideSpeed = 3.5f;
    // A wind-up it has to abandon: seconds before it may attack again; and
    // how far it may jink while charging before it gives the attack up (u).
    public static float BreakOffCooldown = .6f;
    public static float JinkReach = 1f;
    // A blink (the skirmisher's dodge, the blink attack) lands only where
    // nothing arrives for this long; it blinks out when the best line it
    // can steer still gets it hit within the same time.
    public static float BlinkClearSeconds = .4f;
    // An attack held because a dash line is blocked or a friendly elite is
    // in the line of fire: seconds before it looks again.
    public static float HoldFireSeconds = .3f;
    // The old push-away reflex stays as a last resort, at this share.
    public static float ReflexWeight = .15f;
    // DODGING THE PILOT'S SHOTS. 0: never (the default: an elite is there to
    // be shot). Up to 1: the pilot's projectiles count as threats with this
    // weight, so it slips some of them. Flagged for play-testing.
    public static float PlayerShotAwareness = 0f;

    // ---- the threat picture ------------------------------------------------

    public enum Kind : byte { Body, Rock, Lunge, Shot, Pool, PlayerShot }

    public const int MaxThreats = 192;
    // What touches: EliteShip.Collide crashes at hull x .85 + radius x .8 and
    // a shot hits at its radius + hull x .8; a hair more, for the step size.
    const float BodyShare = .8f, HullShare = .85f, TouchSlack = .04f;
    const float Forever = 1e6f;

    static readonly Vector2[] at = new Vector2[MaxThreats];
    static readonly Vector2[] vel = new Vector2[MaxThreats];
    static readonly float[] rad = new float[MaxThreats];
    static readonly float[] from = new float[MaxThreats];      // seconds from now it becomes live
    static readonly float[] until = new float[MaxThreats];
    static readonly float[] ownFrom = new float[MaxThreats];   // ... for the elite that fired it
    static readonly float[] weight = new float[MaxThreats];
    static readonly float[] slack = new float[MaxThreats];     // u/s its path may stray from a straight line
    static readonly Kind[] kind = new Kind[MaxThreats];
    static readonly EliteShip[] owner = new EliteShip[MaxThreats];
    static int count, stamp;

    public static int ThreatCount => count;
    public static Kind ThreatKind(int i) => kind[i];
    public static Vector2 ThreatAt(int i) => at[i];
    public static Vector2 ThreatVelocity(int i) => vel[i];
    public static float ThreatRadius(int i) => rad[i];
    public static float ThreatFrom(int i) => from[i];
    // Counters (tests).
    public static int Senses, Plans;

    static void Add(Kind k, Vector2 p, Vector2 v, float r, float liveFrom, float liveUntil, EliteShip by, float byFrom, float w, float stray = 0f)
    {
        if (count >= MaxThreats) return;
        slack[count] = stray;
        at[count] = p; vel[count] = v; rad[count] = r; from[count] = liveFrom; until[count] = liveUntil;
        owner[count] = by; ownFrom[count] = byFrom; weight[count] = w; kind[count] = k;
        count++;
    }

    // True when `t`'s SensedVelocity was measured this step (not a first-sight guess).
    public static bool Measured(ClearTarget t) => t != null && t.SensedStep == stamp && t.SensedMeasured;

    public static float Skill(EliteDef def) => Mathf.Clamp01(def.avoidance);
    public static float LookAheadFor(EliteDef def) => LookAheadSeconds * Mathf.Lerp(LookAheadUnskilled, 1f, Skill(def));
    public static float ReactionFor(EliteDef def) => ReactionSeconds * Mathf.Lerp(ReactionUnskilled, 1f, Skill(def));
    public static float EvadeSpeedFor(EliteDef def) =>
        Mathf.Min(MaxEvadeSpeed, Mathf.Max(def.speed * EvadeSpeedScale, EliteSystem.Scroll * EvadeBoardShare));
    public static float EvadeAccelFor(EliteDef def) => Mathf.Min(MaxEvadeAccel, def.accel * EvadeAccelScale);

    // Builds the picture. Called once per running step, before the ships.
    public static void Sense(float dt)
    {
        for (int i = 0; i < count; i++) owner[i] = null;
        count = 0;
        if (!Enabled || dt <= 0f || EliteShip.Live.Count == 0) return;
        Senses++;
        stamp++;
        float scroll = EliteSystem.Scroll;
        float top = EliteSystem.ViewTop + SenseAboveView, bottom = EliteSystem.ViewBottom - 1f;
        Vector2 board = new Vector2(0f, -scroll);

        var live = ClearTarget.Live;
        for (int i = 0; i < live.Count; i++)
        {
            var t = live[i];
            if (t == null || !t.isActiveAndEnabled || t.Elite != null) continue;
            bool rock = t.CompareTag("Astr");
            if (!rock && !t.CompareTag("Enimey")) continue;
            if (t.IsShotHitbox) continue;   // shots are read from their pool, below
            Vector2 p = t.transform.position;
            // Its velocity is MEASURED, never inferred from the scroll: a body may ride the board,
            // weave, hold a station in the world or fly its own way. Only the one step it is first
            // seen has nothing to measure; it is read as riding the board until the next.
            Vector2 v = board;
            bool measured = t.SensedStep == stamp - 1;
            if (measured)
            {
                v = (p - t.SensedAt) / dt;
                // (a jump -- a blink, a re-seat -- is not a speed; the first measurement is taken whole)
                if (v.sqrMagnitude > 40f * 40f) { v = board; measured = false; }
                else if (t.SensedMeasured) v = Vector2.Lerp(t.SensedVelocity, v, .5f);
            }
            t.SensedMeasured = measured;
            t.SensedAt = p;
            t.SensedVelocity = v;
            t.SensedStep = stamp;
            if (p.y > top || p.y < bottom) continue;
            float r = t.Radius * BodyShare;
            var brain = t.Brain;
            float stray = PredictionSlack + (brain != null ? Mathf.Min(MaxWeavePace, brain.LateralPace) * WeaveSlack : 0f);
            // (a Glacier Tender's own drones never threaten it: ClearTarget.Mother)
            Add(rock ? Kind.Rock : Kind.Body, p, v, r, 0f, Forever, t.Mother, t.Mother != null ? Forever : 0f, 1f, stray);

            // a telegraphed body dash: the board it is about to cross
            Vector2 reach;
            float inSeconds;
            if (brain != null && brain.LungeAhead(out reach, out inSeconds) && reach.sqrMagnitude > .04f)
            {
                float live0 = Mathf.Max(0f, inSeconds - .1f), live1 = inSeconds + .8f;
                Add(Kind.Lunge, p + reach * .5f, board, r, live0, live1, null, 0f, 1f);
                Add(Kind.Lunge, p + reach, board, r, live0, live1, null, 0f, 1f);
            }
        }

        var pool = EliteSystem.ShotsIfAny;
        if (pool != null)
        {
            var shots = pool.All;
            for (int i = 0; i < shots.Count; i++)
            {
                var s = shots[i];
                if (s == null || !s.Active) continue;
                if (s.Airborne)
                {
                    // harmless in the air: the pool it lands as, on its marked spot
                    float lands = s.LobRemaining;
                    Add(Kind.Pool, s.LobTarget, board, s.PoolRadius, lands, lands + s.PoolSeconds, null, 0f, 1f);
                    continue;
                }
                Vector2 p = s.transform.position;
                if (p.y > top || p.y < bottom) continue;
                // (its own ship is never hit by it: EliteShot.Step; a roster
                // shot riding the board falls that much faster)
                Vector2 v = s.Velocity;
                v.y -= scroll * s.Ride;
                Add(s.Pooled ? Kind.Pool : Kind.Shot, p, v, s.Radius, 0f, Forever, s.Owner, Forever, 1f);
            }
        }

        if (PlayerShotAwareness > 0f)
        {
            var flying = AttackProjectile.Flying;
            for (int i = 0; i < flying.Count; i++)
            {
                var a = flying[i];
                if (a == null || !a.Active) continue;
                Add(Kind.PlayerShot, a.transform.position, a.Velocity, a.HitRadius, 0f, Forever, null, 0f, Mathf.Clamp01(PlayerShotAwareness));
            }
        }
    }

    // ---- flying a path on paper ------------------------------------------------

    public struct Outlook
    {
        public float hitIn;       // seconds until the first hit, < 0: none in the window
        public float clearance;   // the smallest gap left to any threat (u)
        public float danger;      // summed weight of what hits
        public float hitWeight;   // the weight of that first hit (1: a body, a hostile shot; less: the pilot's shots)
        public bool Hit => hitIn >= 0f;
    }

    // One threat against one straight leg of the ship's path. `r0` is the
    // threat relative to the ship at the leg's start, `rv` their relative
    // velocity, `R` the touching distance, `seconds` the leg's length.
    static void Leg(Vector2 r0, Vector2 rv, float R, float seconds, float legStart, float w, ref Outlook o)
    {
        if (seconds <= 0f) return;
        float c = r0.sqrMagnitude - R * R;
        if (c <= 0f)
        {
            if (o.hitIn < 0f || legStart < o.hitIn) { o.hitIn = legStart; o.hitWeight = w; }
            o.danger += w;
            o.clearance = Mathf.Min(o.clearance, 0f);
            return;
        }
        float a = rv.sqrMagnitude, b = Vector2.Dot(r0, rv);
        if (a < 1e-8f || b >= 0f)
        {
            // not closing: the start is the nearest they get
            o.clearance = Mathf.Min(o.clearance, Mathf.Sqrt(r0.sqrMagnitude) - R);
            return;
        }
        float tca = Mathf.Min(seconds, -b / a);
        float near = (r0 + rv * tca).magnitude;
        if (near >= R) { o.clearance = Mathf.Min(o.clearance, near - R); return; }
        float disc = b * b - a * c;
        float hit = disc > 0f ? (-b - Mathf.Sqrt(disc)) / a : tca;
        if (hit > seconds) { o.clearance = Mathf.Min(o.clearance, 0f); return; }
        hit += legStart;
        if (o.hitIn < 0f || hit < o.hitIn) { o.hitIn = hit; o.hitWeight = w; }
        o.danger += w;
        o.clearance = Mathf.Min(o.clearance, 0f);
    }

    // The ship's path: at `pos`, holding still until `wait`, then on at `v0`
    // for `lag` seconds, then straight to `to` at `speed`, then holding.
    // Judged over [t0, horizon] seconds from now.
    struct Path
    {
        public Vector2 pos, v0, to, p1, u;
        public float wait, lagEnd, arrive;
        public float xMin, xMax;   // the columns it crosses

        public Path(Vector2 pos, float wait, Vector2 v0, float lag, Vector2 to, float speed)
        {
            this.pos = pos; this.v0 = v0; this.to = to; this.wait = wait;
            lagEnd = wait + lag;
            p1 = pos + v0 * lag;
            Vector2 d = to - p1;
            float dist = d.magnitude;
            if (dist > 1e-4f && speed > 1e-3f) { u = d / dist * speed; arrive = lagEnd + dist / speed; }
            else { u = Vector2.zero; arrive = lagEnd; }
            xMin = Mathf.Min(pos.x, Mathf.Min(p1.x, to.x));
            xMax = Mathf.Max(pos.x, Mathf.Max(p1.x, to.x));
        }
    }

    // The path the ship's steering really gives: it cannot swing its
    // velocity round at once. Its reaction, then the time `accel` takes to
    // turn `v0` into the run at `to`, flown at the average of the two; then
    // the run.
    static Path Steered(Vector2 pos, Vector2 v0, Vector2 to, float speed, float accel, float react)
    {
        Vector2 d = to - pos;
        float dist = d.magnitude;
        Vector2 run = dist > .05f ? d / dist * Mathf.Min(speed, Mathf.Sqrt(2f * accel * dist)) : Vector2.zero;
        float swing = (run - v0).magnitude / Mathf.Max(.1f, accel);
        float lag = react + swing;
        Vector2 during = lag > 1e-4f ? (v0 * (react + swing * .5f) + run * (swing * .5f)) / lag : v0;
        return new Path(pos, 0f, during, lag, to, speed);
    }

    static void Against(in Path path, Vector2 p, Vector2 v, float R, float t0, float t1, float w, ref Outlook o, float slack = 0f)
    {
        if (t1 <= t0) return;
        // most of the board is in other columns for the whole window: out at once
        float room = R + slack * t1, drift = v.x * t1;
        if (p.x + Mathf.Max(0f, drift) + room < path.xMin || p.x + Mathf.Min(0f, drift) - room > path.xMax) return;
        if (slack > 0f)
        {
            // (the further ahead, the less sure: judged in two halves, each with the room of its middle)
            float mid = (t0 + t1) * .5f;
            Against(path, p, v, R + slack * (t0 + mid) * .5f, t0, mid, w, ref o);
            Against(path, p, v, R + slack * (mid + t1) * .5f, mid, t1, w, ref o);
            return;
        }
        // too far to matter inside the window, whatever both do
        float reachable = R + (v.magnitude + Mathf.Max(path.v0.magnitude, path.u.magnitude)) * t1 + .5f;
        if ((p - path.pos).sqrMagnitude > reachable * reachable) return;
        // leg 0: waiting
        float a = t0, b = Mathf.Min(t1, path.wait);
        if (b > a) Leg(p + v * a - path.pos, v, R, b - a, a, w, ref o);
        // leg 1: carrying on
        a = Mathf.Max(t0, path.wait); b = Mathf.Min(t1, path.lagEnd);
        if (b > a) Leg(p + v * a - (path.pos + path.v0 * (a - path.wait)), v - path.v0, R, b - a, a, w, ref o);
        // leg 2: to the spot
        a = Mathf.Max(t0, path.lagEnd); b = Mathf.Min(t1, path.arrive);
        if (b > a) Leg(p + v * a - (path.p1 + path.u * (a - path.lagEnd)), v - path.u, R, b - a, a, w, ref o);
        // leg 3: there
        a = Mathf.Max(t0, path.arrive);
        if (t1 > a) Leg(p + v * a - (path.arrive > path.lagEnd ? path.to : path.p1), v, R, t1 - a, a, w, ref o);
    }

    static Outlook Fly(EliteShip self, in Path path, float t0, float horizon, bool ignoreRocks, bool withElites)
    {
        var o = new Outlook { hitIn = -1f, clearance = 9f };
        var def = self.Def;
        // the rails: where the swing of its velocity carries it before it is on its line
        float rail = EliteSystem.RailEdge - def.hullRadius * .7f - .05f;
        if (Mathf.Abs(path.p1.x) > rail && path.lagEnd > t0 && path.lagEnd <= horizon && Mathf.Abs(path.p1.x) > Mathf.Abs(path.pos.x))
        {
            float room = rail - Mathf.Abs(path.pos.x), run = Mathf.Abs(path.p1.x) - Mathf.Abs(path.pos.x);
            o.hitIn = path.wait + (path.lagEnd - path.wait) * Mathf.Clamp01(room / Mathf.Max(1e-3f, run));
            o.hitWeight = 1f;
            o.danger = 1f;
            o.clearance = 0f;
        }
        float hull = def.hullRadius * HullShare + TouchSlack;
        for (int i = 0; i < count; i++)
        {
            if (ignoreRocks && kind[i] == Kind.Rock) continue;
            float a = Mathf.Max(t0, from[i]);
            if (owner[i] == self) a = Mathf.Max(a, ownFrom[i]);
            Against(path, at[i], vel[i], hull + rad[i], a, Mathf.Min(horizon, until[i]), weight[i], ref o, slack[i]);
        }
        if (!withElites) return o;
        var live = EliteShip.Live;
        for (int i = 0; i < live.Count; i++)
        {
            var e = live[i];
            if (e == null || e == self) continue;
            float r = hull + e.Def.hullRadius;
            if (e.InPlay)
            {
                // where it is and is heading for the next beat, then the spot it has chosen
                Against(path, e.Position, e.Velocity, r, t0, Mathf.Min(horizon, .45f), 1f, ref o);
                Against(path, e.Claim, Vector2.zero, r + EliteSpacing, Mathf.Max(t0, .3f), horizon, 1f, ref o);
            }
            else if (e.State == EliteState.LiftOff && e.JoinsIn < horizon)
                Against(path, e.LiftTarget, Vector2.zero, r + EliteSpacing, Mathf.Max(t0, e.JoinsIn), horizon, 1f, ref o);
        }
        return o;
    }

    // Holding still at `point` from `t0` to `t1` seconds from now.
    public static Outlook Hold(EliteShip self, Vector2 point, float t0, float t1)
    {
        var path = new Path(point, 0f, Vector2.zero, 0f, point, 0f);
        return Fly(self, path, t0, t1, self.Def.armored, true);
    }

    // Waiting `wait` seconds where it is, then a straight dash at `velocity`
    // for `seconds` (a lance dash, a claw dive, an ice ram).
    public static Outlook Dash(EliteShip self, float wait, Vector2 velocity, float seconds, bool ignoreRocks)
    {
        Vector2 pos = self.Position;
        var path = new Path(pos, wait, velocity, seconds, pos + velocity * seconds, 0f);
        return Fly(self, path, wait, wait + seconds, ignoreRocks, true);
    }

    // ---- the plan ----------------------------------------------------------------

    const float HitCost = 1000f, ExtraHitCost = 120f, FarHitCost = 60f, NearMissCost = 40f;
    const float GoalCost = 10f, EffortCost = 1.5f, PilotCost = 60f, SwitchCost = 4f;

    // Candidate spots: every FineStep sideways out to FineReach (to find
    // the middle of a ship-wide gap), every CoarseStep beyond out to
    // SideReach, each at its own height, a little higher and a little lower.
    const float FineStep = .15f, FineReach = 1.2f, CoarseStep = .3f, SideReach = 2.7f, HeightStep = .7f;
    const int FineCount = 8, CoarseCount = 5, SideCount = (FineCount + CoarseCount) * 2 + 1;

    static float SideOffset(int i)
    {
        if (i == 0) return 0f;
        int n = (i + 1) / 2;
        float d = n <= FineCount ? n * FineStep : FineReach + (n - FineCount) * CoarseStep;
        return (i & 1) == 1 ? d : -d;
    }

    static float Cost(in Outlook o, float commit)
    {
        if (o.Hit)
        {
            if (o.hitIn > commit) return FarHitCost * o.hitWeight;
            return HitCost * o.hitWeight * (1f + Mathf.Clamp01((commit - o.hitIn) / commit)) + ExtraHitCost * Mathf.Max(0f, o.danger - o.hitWeight);
        }
        return o.clearance < SafetyMargin ? NearMissCost * (1f - Mathf.Max(0f, o.clearance) / SafetyMargin) : 0f;
    }

    // Where to fly: `goal` (the brain's wish) when the way there is clear,
    // else the cheapest spot round the ship. `evading` says which; `hitIn`
    // is the first hit still coming on the chosen path (< 0: none).
    public static Vector2 Plan(EliteShip self, Vector2 goal, float goalSpeedScale, Vector2 previous, bool wasEvading,
                               out bool evading, out float hitIn)
    {
        Plans++;
        var def = self.Def;
        Vector2 pos = self.Position, v0 = self.Velocity;
        float horizon = LookAheadFor(def);
        float react = ReactionFor(def) * .5f;
        float evadeSpeed = EvadeSpeedFor(def), evadeAccel = EvadeAccelFor(def);
        bool armored = def.armored;
        Vector2 pilot = self.Seen;
        float pilotKeep = def.hullRadius + PilotSpacing;

        // the brain's own wish first: taken whenever it is clean
        var path = Steered(pos, v0, goal, def.speed * Mathf.Max(.1f, goalSpeedScale), def.accel, react);
        var o = Fly(self, path, 0f, horizon, armored, true);
        float commit = horizon * CommitShare;
        float best = Cost(o, commit);
        Vector2 choice = goal;
        evading = false;
        hitIn = o.hitIn;
        if (best <= 0f) return goal;

        for (int k = -1; k < SideCount * 3; k++)
        {
            Vector2 c;
            if (k == -1) { if (!wasEvading) continue; c = previous; }
            else c = pos + new Vector2(SideOffset(k / 3), k % 3 == 0 ? 0f : k % 3 == 1 ? HeightStep : -HeightStep);
            c = self.ClampGoal(c);
            // (already dearer than the best, before any danger: not worth flying on paper)
            float cost = GoalCost * (c - goal).magnitude + EffortCost * (c - pos).magnitude;
            if (cost >= best) continue;
            path = Steered(pos, v0, c, evadeSpeed, evadeAccel, react);
            o = Fly(self, path, 0f, horizon, armored, true);
            cost += Cost(o, commit);
            if ((c - pilot).sqrMagnitude < pilotKeep * pilotKeep) cost += PilotCost;
            if (wasEvading && (c - previous).sqrMagnitude > .3f * .3f) cost += SwitchCost;
            if (cost >= best) continue;
            best = cost;
            choice = c;
            evading = true;
            hitIn = o.hitIn;
        }
        return choice;
    }

    // ---- lift-off ------------------------------------------------------------------

    // The best spot to join the play at, `arriveIn` seconds from now: the
    // `preferred` one if it is clear for LiftClearSeconds after arriving and
    // far enough from the pilot, else the nearest spot that is. `clear`:
    // the spot returned really is clear.
    public static Vector2 BestJoin(EliteShip self, Vector2 preferred, float arriveIn, out bool clear)
    {
        var def = self.Def;
        var p = EliteSystem.Player;
        Vector2 pilot = p != null ? (Vector2)p.position : new Vector2(0f, -2.5f);
        float keep = EliteShip.MinJoinDistance;
        float t1 = arriveIn + LiftClearSeconds;
        float best = float.MaxValue;
        Vector2 choice = preferred;
        clear = false;
        // the preferred spot, a ring round it, then the director's ring round the pilot
        for (int k = 0; k < 1 + 8 + 16; k++)
        {
            Vector2 c = preferred;
            if (k >= 1 && k < 9)
            {
                float a = (k - 1) * Mathf.PI / 4f;
                c += new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * (k % 2 == 1 ? .9f : 1.6f);
            }
            else if (k >= 9)
            {
                float a = (k - 9) * Mathf.PI / 8f;
                c = pilot + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * (keep + .5f);
            }
            c = self.ClampGoal(c);
            float cost = 3f * (c - preferred).magnitude;
            float d = (c - pilot).magnitude;
            if (d < keep) cost += 500f + 100f * (keep - d);
            var o = Hold(self, c, arriveIn, t1);
            cost += Cost(o, t1);
            if (cost >= best) continue;
            best = cost;
            choice = c;
            clear = !o.Hit;
        }
        return choice;
    }
}
