using UnityEngine;

// Desert's six elites as mini-boss fights (Codex's flight strips, Art/Enemies/Elite/Desert), each with its own brain and attack, in the
// dunes' own material (churned sand, quicksand, glass shards, focused sun-lens beams, dust walls, rolling boulders). The rules they share
// with the Space, Frost, Verdant, Ember, Tide and Storm elites: none of them follows the pilot -- they keep their distance and throw
// HAZARDS; every attack is telegraphed from the first frame of its tell by the shared thin, faint, gently blinking drawing (EliteMarks
// rings, EliteTelegraph lanes -- Desert's pale bone-gold cue colours, never pink, never green) and what it will cover is fixed at the
// tell (the pilot is never hit by what it was not shown). Lens beams are streams of thin bolts down drawn lines: nothing flashes the screen.
// Desert was the last world on the BASE brains and attacks (interceptor / lance_dash ... bastion / ward_curtain); they stay registered.
//
//   Dune Stalker      3  burrower  / worm_churn    (wormburrow)  hurtHide  a LOW rippling band: it circles a stadium track mid-board (a
//                                                              rippling low run, a rise up the rail, the top run back, a dive down the
//                                                              other rail); the tell rings a trail of four sand bursts (twin trails
//                                                              from phase 2) aimed along the pilot's row, cast at the start of the low run
//   Caravan Hauler    5  caravan   / quicksand_row (caravanbay)  armored  plods along the top in legs with a rest at each end; the
//                                                              tell rings a ROW of quicksand pools with one slot left open beside the
//                                                              pilot (a second row from phase 2, gap mirrored; a leg shake at the end)
//   Lens Matriarch    3  glint     / glass_fan     (glasspad)    hurtHide  a zig-zag circuit across the top (a lane, a turn at the rail
//                                                              one lane lower, back); it halts mid-run and the tell draws a fan of
//                                                              glass-shard rays with the pilot's own line left out
//   Glass Cannon      3  lenspost  / lens_beams    (buriedsilo)  perches on one of three columns along the top in the script's order
//                                                              (left, centre, right, centre); the tell draws thin beam lines either
//                                                              side of the pilot's, then a stream of bolts runs down each, outside in
//   Scorpion Skiff    3  flanker   / boulder_roll  (ruindock)    hurtHide  holds a flank, then scuttles across to the other after
//                                                              every throw (a script beat); the tell draws the path of a boulder (a
//                                                              skid slab) to the far rail and down; two boulders from phase 2
//   Mirror Bastion    5  bulwark   / dust_wall     (ruindock)    armored  slides UP AND DOWN one side rail (three slots); the tell draws
//                                                              the gap of a WALL of dust sweeping from the opposite rail in waves; the
//                                                              rails swap each phase; the last phase adds the mirror wall
//
// Nobody leaves the view except for the one scripted last-heart exit (Stalker left, Matriarch top, Skiff right): the Stalker and the
// Matriarch fly closed circuits that turn round at the rails (DesertTrack), nobody warps. Every choice (perch, flank, slot, pace) is a
// script move token (EliteBrain.OnScriptMove) or a Script.Cycle counter: no coin flips. The attacks are varied by the script's variants
// (hazardCount = rays / beams / rows / waves / bursts, shotSpread, shotInterval, skidSpeed ...). Everything an attack leaves on the board
// rides it. See docs/enemy-behaviours.md ("Desert elites") and docs/elites/elite-scripts.md for the design.

// ---- shared bits ---------------------------------------------------------------------------------------------------

public static class DesertFlight
{
    // World x beyond which an elite is wholly out of the view (what an elite leaving by a side has to clear).
    public static float OffX(EliteDef def) => SpaceFlight.OffX(def);
    // World y at which an elite is wholly above the view.
    public static float AboveY(EliteDef def) => FrostFlight.AboveY(def);
    // The x range a ship's centre may keep between the rails.
    public static float Edge(EliteDef def, float margin) => FrostFlight.Edge(def, margin);
    // Is a roamer's centre outside the board (past a rail, above the top or under the bottom)?
    public static bool OffBoard(EliteDef def, Vector2 p) =>
        Mathf.Abs(p.x) > OffX(def) - .25f || p.y > AboveY(def) - .2f || p.y < FrostFlight.BelowY(def) + .2f;
    // The world y of the top of an elite's station (never above the player's reach).
    public static float Height(EliteShip ship, EliteDef def) => ship.ZoneY(ship.ReachY(EliteSystem.ViewTop - def.topMargin));
}

// An attack that LAYS things onto ringed spots: the spots (Plan) are drawn as thin blinking rings from the first frame of the tell
// (faster once the aim is locked, if it is aimed), then each is lobbed onto its spot, Stagger apart, riding the board down (the ring
// stays put until the thing has landed on it). Subclasses plan the spots and lay one.
public abstract class DesertSpotAttack : EliteAttack
{
    public const int MaxSpots = 10;
    protected EliteMarks marks;
    protected readonly Vector2[] spot = new Vector2[MaxSpots];
    protected readonly float[] landAt = new float[MaxSpots];
    protected int count, laid;
    protected float next;
    protected bool acted;

    protected override void OnBind() { marks = new EliteMarks(ship, MaxSpots); }
    protected abstract float RingRadius { get; }
    // Fills spot[0..count) for a pilot at `pilot`.
    protected abstract void Plan(Vector2 pilot);
    // Fires the thing that will land on `target` (the spot, moved up by what the board scrolls while it flies).
    protected abstract void Lay(int i, Vector2 target);
    // The cast is over (the brain moves on).
    protected virtual void Done() { }
    protected virtual float Stagger => def.shotInterval;
    // What lands on a ring is dangerous within HazardRadius of it for HazardSeconds (the dodge bot's plan, AttackBudgetScenarios).
    public virtual float HazardRadius => RingRadius;
    public virtual float HazardSeconds => def.poolSeconds;

    public EliteMarks Marks => marks;
    public int Count => count;
    public int Laid => laid;
    public Vector2 Spot(int i) => spot[Mathf.Clamp(i, 0, MaxSpots - 1)];
    public int HeartsLost => Mathf.Max(0, def.hearts - ship.Hearts);

    void ShowRings(bool fast)
    {
        for (int k = 0; k < count; k++) marks.Set(k, spot[k], RingRadius, def.ShotColor, fast);
        for (int k = count; k < MaxSpots; k++) marks.Hide(k);
    }

    public override void BeginTell(Vector2 seen)
    {
        base.BeginTell(seen);
        acted = false;
        laid = 0;
        count = 0;
        marks.HideAll();
        Plan(seen);
        ShowRings(false);
    }

    public override void StepTell(float dt)
    {
        t += dt;
        if (Aimed && !ship.AimLocked) Plan(ship.AimSeen);
        ShowRings(Aimed ? ship.AimLocked : t > TellSeconds * .6f);
    }

    public override void Cancel() { marks.HideAll(); }

    public override void BeginAction()
    {
        base.BeginAction();
        acted = true;
        laid = 0;
        next = 0f;
    }

    public override bool StepAction(float dt)
    {
        t += dt;
        while (t >= next && laid < count)
        {
            Lay(laid, spot[laid] + Vector2.up * (EliteSystem.Scroll * def.lobSeconds));
            landAt[laid] = t + def.lobSeconds;
            laid++;
            next += Stagger;
        }
        bool pending = false;
        for (int k = 0; k < count; k++)
        {
            if (k >= laid || t < landAt[k]) { marks.Set(k, spot[k], RingRadius, def.ShotColor, true); pending = true; }
            else marks.Hide(k);
        }
        return laid >= count && !pending;
    }

    public override void Passive(float dt) { marks.Step(dt); }

    public override void End()
    {
        marks.HideAll();
        if (acted) Done();
        acted = false;
    }

    // (the pools lapse on their own)
    public override void OnDeath() { marks.HideAll(); }
}

// A closed circuit a brain flies: a STADIUM -- a low straight run, a half-ellipse turn up the right rail, the high straight run back, a
// half-ellipse turn down the left rail -- so it turns round at the rails and never leaves the view. Point(u) is the position u units
// along it (u wraps); the straights carry a ripple (sine) or a zig-zag (triangle) that dies away at the turns. No allocation.
public sealed class DesertTrack
{
    public float cx, cy, half = 1f, rx = .7f, ry = 1f, amp, period = 3.4f;
    public bool triangle;
    public float Straight => Mathf.Max(.2f, 2f * half);
    public float Arc => Mathf.PI * (rx + ry) * .5f;
    public float Length => 2f * (Straight + Arc);
    public float Wrap(float u) => Mathf.Repeat(u, Length);

    float Ripple(float x, float f)
    {
        float env = Mathf.Clamp01(Mathf.Min(f, 1f - f) * 5f);
        if (triangle)
        {
            float k = Mathf.Repeat(x / Mathf.Max(.5f, period), 1f);
            return -amp * (1f - Mathf.Abs(2f * k - 1f)) * env;
        }
        return amp * Mathf.Sin(x * (Mathf.PI * 2f / Mathf.Max(.5f, period))) * env;
    }

    public Vector2 Point(float u)
    {
        u = Wrap(u);
        float s = Straight, a = Arc;
        if (u < s)
        {
            float x = -half + u;
            return new Vector2(cx + x, cy - ry + Ripple(x, u / s));
        }
        if (u < s + a)
        {
            float ang = (-90f + 180f * (u - s) / a) * Mathf.Deg2Rad;
            return new Vector2(cx + half + rx * Mathf.Cos(ang), cy + ry * Mathf.Sin(ang));
        }
        if (u < 2f * s + a)
        {
            float v = u - s - a;
            float x = half - v;
            return new Vector2(cx + x, cy + ry + Ripple(x, v / s));
        }
        float ang2 = (90f + 180f * (u - 2f * s - a) / a) * Mathf.Deg2Rad;
        return new Vector2(cx - half + rx * Mathf.Cos(ang2), cy + ry * Mathf.Sin(ang2));
    }

    // On the low run, between `from` and `to` (shares of its length).
    public bool OnLowRun(float u, float from, float to)
    {
        u = Wrap(u);
        return u >= Straight * from && u <= Straight * to;
    }

    // On either straight run, `margin` u clear of its ends (the turns are not).
    public bool OnStraight(float u, float margin)
    {
        u = Wrap(u);
        float s = Straight, a = Arc;
        return (u >= margin && u <= s - margin) || (u >= s + a + margin && u <= 2f * s + a - margin);
    }

    public float StartOfHighRun => Straight + Arc;

    // The u of the point of the circuit nearest `p` (a coarse scan: a ship that comes back re-enters the circuit where it is).
    public float Nearest(Vector2 p)
    {
        float best = 0f, bd = float.MaxValue, len = Length;
        for (int i = 0; i < 96; i++)
        {
            float u = len * i / 96f;
            float d = (Point(u) - p).sqrMagnitude;
            if (d < bd) { bd = d; best = u; }
        }
        return best;
    }
}

// A brain that flies a DesertTrack: a carrot runs ahead along the circuit at the ship's pace and the ship chases it (the carrot waits when
// the ship falls behind -- a flinch, an evasion). Script tokens "steady" / "fast" / "slow" set the pace. Subclasses lay the circuit out
// (Build) and say when a cast fits. Nothing here leaves the view.
public abstract class DesertTrackBrain : EliteBrain
{
    public const float Lead = 1.3f, MaxLag = 2.4f;
    protected readonly DesertTrack track = new DesertTrack();
    protected float u, pace = 1f;
    public int Casts { get; private set; }
    public DesertTrack Track => track;
    public float U => u;
    public virtual float PassScale => 1f;
    protected virtual float StartU => 0f;
    protected abstract void Build();

    public float PaceSpeed => def.speed * SpeedScale * (ship != null ? ship.Script.SpeedScale : 1f);
    public Vector2 PointAfter(float seconds) { Build(); return track.Point(u + PaceSpeed * seconds); }
    public void CastDone() { Casts++; }

    public override bool Roams => false;
    public override float TellScale => 1f;
    public override float SpeedScale => PassScale * pace;
    public override void OnJoin() { Build(); u = StartU; }
    public override void OnReturn() { Build(); u = track.Nearest(Pos); }

    public override void OnScriptMove(string token)
    {
        if (token == "fast") pace = 1.35f;
        else if (token == "slow") pace = .8f;
        else if (token == "steady") pace = 1f;
    }

    public override Vector2 Goal(Vector2 seen, float dt)
    {
        Clock += dt;
        Build();
        if (ship.State != EliteState.Join && ship.State != EliteState.Hide && (Pos - track.Point(u)).sqrMagnitude < MaxLag * MaxLag)
            u = track.Wrap(u + PaceSpeed * dt);
        return track.Point(u + Lead);
    }
}

// ---- Dune Stalker: burrower / worm_churn ---------------------------------------------------------------------------

// A burrower rippling under the sand: it circles a low, wide stadium mid-board -- a rippling run along the bottom (left to right), up
// the right rail, back along the top, down the left rail -- and casts a churn at the start of the low run (WantsAttack; the script says
// which laps), its tokens set the pace. It never leaves the view.
public class BurrowerBrain : DesertTrackBrain
{
    public const float Ripple = .38f, Period = 3.3f;
    public BurrowerBrain() { Id = "burrower"; }
    public override float PassScale => 1.2f;

    protected override void Build()
    {
        float edge = DesertFlight.Edge(def, .5f);
        float high = DesertFlight.Height(ship, def) - .15f;
        float low = Mathf.Max(Mathf.Lerp(EliteSystem.ViewBottom, EliteSystem.ViewTop, .47f), high - 3.0f);
        low = Mathf.Min(low, high - 2.0f);
        track.rx = Mathf.Clamp(edge * .32f, .4f, .9f);
        track.half = Mathf.Max(.3f, edge - track.rx);
        track.cx = 0f;
        track.ry = (high - low) * .5f;
        track.cy = (high + low) * .5f;
        track.amp = Ripple;
        track.period = Period;
        track.triangle = false;
    }

    public override float MinAttackAbove => 1.2f;
    // The start of the low run: the whole wind-up still lies on the run or its turn.
    public override bool WantsAttack(Vector2 seen) => track.OnLowRun(u, 0f, .6f) && Pos.y > seen.y + MinAttackAbove;
}

// Dune Stalker: a worm churn. The ship keeps flying through the wind-up: from the first frame of the tell Bursts thin rings mark a
// TRAIL of sand bursts leading from just under where it will be toward where it sees the pilot (followed for the first aimCommit of the
// tell, then locked and blinking faster); the trail is at least Bursts * Pitch long. The action lobs a grit burst onto each ring,
// shotInterval apart nearest first, so the churn travels toward the pilot like a worm surfacing; each lands as a pool that rides the
// board for poolSeconds. Read the trail and step off it.
public class WormChurnAttack : DesertSpotAttack
{
    public const int Bursts = 4;
    public const float Ring = .4f, Pitch = .85f, Under = 1.1f, TwinOffset = .6f;
    Vector2 origin;
    public WormChurnAttack() { Id = "worm_churn"; }
    protected override float RingRadius => Ring;
    public override bool Aimed => true;
    public Vector2 Origin => origin;
    // hazardCount >= 8 (the script's "twin" beat): two trails side by side, their bursts interleaved.
    public int Trails => def.hazardCount >= 8 ? 2 : 1;

    public override void BeginTell(Vector2 seen)
    {
        var bb = ship.Brain as BurrowerBrain;
        // where the ship will be when the tell ends (its circuit a pace ahead)
        Vector2 at = bb != null ? bb.PointAfter(def.tellSeconds) : (Vector2)ship.Position;
        origin = new Vector2(Mathf.Clamp(at.x, -EliteSystem.RailEdge + .45f, EliteSystem.RailEdge - .45f), at.y - Under);
        base.BeginTell(seen);
    }

    protected override void Plan(Vector2 pilot)
    {
        int trails = Trails;
        count = Bursts * trails;
        float edge = EliteSystem.RailEdge - .45f;
        // (always down the board: the trail leads toward the pilot's row, never up)
        Vector2 to = new Vector2(pilot.x, Mathf.Min(pilot.y + def.lobAhead, origin.y - 1.2f));
        Vector2 d = to - origin;
        float len = Mathf.Max(d.magnitude, Bursts * Pitch);
        Vector2 dir = d.sqrMagnitude > 1e-4f ? d.normalized : Vector2.down;
        Vector2 perp = new Vector2(-dir.y, dir.x);
        for (int k = 0; k < Bursts; k++)
        {
            for (int t = 0; t < trails; t++)
            {
                float off = trails == 1 ? 0f : (t == 0 ? -TwinOffset : TwinOffset);
                Vector2 p = origin + dir * (len * (k + 1) / Bursts) + perp * off;
                spot[k * trails + t] = new Vector2(Mathf.Clamp(p.x, -edge, edge), Mathf.Max(EliteSystem.ViewBottom + .8f, p.y));
            }
        }
    }

    protected override void Lay(int i, Vector2 target)
    {
        int m = def.muzzles.Length > 0 ? i % def.muzzles.Length : 0;
        var burst = Fire(m, Deg(target - (Vector2)ship.MuzzleWorld(m)), 0f, EliteShots.Kind.Glob);
        if (burst != null) burst.Lob(target, Mathf.Max(.15f, def.lobSeconds));
    }

    protected override void Done()
    {
        var bb = ship.Brain as BurrowerBrain;
        if (bb != null) bb.CastDone();
    }
}

// ---- Caravan Hauler: caravan / quicksand_row ------------------------------------------------------------------------

// A slow caravan. It plods along the top in LEGS, from near one rail to near the other at a crawl, and rests a beat at each end; it
// only throws while resting. It never comes down and never follows the pilot's lane.
public class CaravanBrain : EliteBrain
{
    public const float Pace = .55f, Rest = 1.6f, EndMargin = .5f;
    float side = 1f, rest, shake;
    public float Side => side;
    public bool Shaking => shake > 0f;
    public bool Resting => rest > 0f;
    public int Legs { get; private set; }
    public float Height => DesertFlight.Height(ship, def);
    public float EndX => side * DesertFlight.Edge(def, EndMargin);

    public CaravanBrain() { Id = "caravan"; }

    protected override void OnBind() { side = ship.Script.Alternate("leg"); }
    public override void OnJoin() { rest = 0f; shake = 0f; side = Pos.x >= 0f ? -1f : 1f; }
    public override Vector2 JoinFrom => Vector2.up;
    public override void OnScriptMove(string token) { if (token == "shake") shake = 2.4f; }

    public override Vector2 Goal(Vector2 seen, float dt)
    {
        Clock += dt;
        if (shake > 0f) shake -= dt;
        if (ship.State == EliteState.Attack) return new Vector2(Pos.x, Height);
        if (rest > 0f)
        {
            rest -= dt;
            // (the leg shake: a quick shudder of the whole hull while it rests)
            return new Vector2(Pos.x + (shake > 0f ? Mathf.Sin(Clock * 38f) * .12f : 0f), Height);
        }
        float endX = EndX;
        if (Mathf.Abs(Pos.x - endX) < .15f) { side = -side; Legs++; rest = Rest; return new Vector2(Pos.x, Height); }
        return new Vector2(endX, Height);
    }

    public override float SpeedScale => Pace;
    public override float MinAttackAbove => 1.5f;
    public override bool WantsAttack(Vector2 seen) => rest > 0f && Pos.y > seen.y + MinAttackAbove;
    public override float? FaceDeg(Vector2 seen) => -90f;
}

// Caravan Hauler: a quicksand row. The tell holds it nose down: thin rings mark a ROW of five slots across the board (Slots), centred
// a lobAhead above the pilot's height, with ONE slot left open: the one beside the pilot's, toward the middle of the board (so the pilot
// has to move one slot). The action lobs a sand canister out of the hold onto each ringed slot, shotInterval apart; each lands as a
// pool of quicksand that rides the board for poolSeconds. With hazardCount 2 (the script's later phases) a second row follows a hand above,
// its open slot mirrored. Go through the gap.
public class QuicksandRowAttack : DesertSpotAttack
{
    public const int Slots = 5;
    public const float Ring = .45f, RowGap = 1.2f, MaxPitch = 1.0f;
    public QuicksandRowAttack() { Id = "quicksand_row"; }
    protected override float RingRadius => Ring;
    public override bool Aimed => true;
    public override bool HoldsDuringTell => true;
    public override bool HoldsDuringAction => true;
    public override float? FaceDeg => -90f;
    public int GapSlot { get; private set; }
    public int Rows { get; private set; }

    public static float Pitch => Mathf.Min(MaxPitch, (EliteSystem.RailEdge - .45f) * 2f / (Slots - 1));
    public float SlotX(int i) => (i - (Slots - 1) * .5f) * Pitch;

    protected override void Plan(Vector2 pilot)
    {
        float pitch = Pitch;
        int mid = (Slots - 1) / 2;
        int near = Mathf.Clamp(Mathf.RoundToInt(pilot.x / Mathf.Max(.1f, pitch)) + mid, 0, Slots - 1);
        int gap = near < mid ? near + 1 : near > mid ? near - 1 : mid + 1;
        GapSlot = gap;
        float y = Mathf.Clamp(pilot.y + def.lobAhead, EliteSystem.ViewBottom + .8f, ship.Position.y - 1.4f);
        int rows = Mathf.Clamp(def.hazardCount, 1, 2) >= 2 && y + RowGap < ship.Position.y - 1.0f ? 2 : 1;
        Rows = rows;
        count = 0;
        for (int r = 0; r < rows; r++)
        {
            int g = r == 0 ? gap : Slots - 1 - gap;
            for (int i = 0; i < Slots; i++)
                if (i != g) spot[count++] = new Vector2(SlotX(i), y + r * RowGap);
        }
    }

    protected override void Lay(int i, Vector2 target)
    {
        int m = def.muzzles.Length > 0 ? i % def.muzzles.Length : 0;
        var can = Fire(m, Deg(target - (Vector2)ship.MuzzleWorld(m)), 0f, EliteShots.Kind.Glob);
        if (can != null) can.Lob(target, Mathf.Max(.15f, def.lobSeconds));
    }
}

// ---- Lens Matriarch: glint / glass_fan ---------------------------------------------------------------------------------

// A heliograph signalling across the dunes: it flies a zig-zag circuit across the top -- a lane (a triangle wave, Dip u deep), a turn
// round the rail one lane lower, the lane back -- and casts a fan where it is on a straight lane between the rails (the tell halts it
// mid-run). It never leaves the view; it starts on the high lane, flying left.
public class GlintBrain : DesertTrackBrain
{
    public const float Dip = .75f, Period = 3.4f;
    public GlintBrain() { Id = "glint"; }
    protected override float StartU => track.StartOfHighRun;

    protected override void Build()
    {
        float edge = DesertFlight.Edge(def, .5f);
        float high = DesertFlight.Height(ship, def) - .1f;
        track.rx = Mathf.Clamp(edge * .4f, .5f, .9f);
        track.half = Mathf.Max(.3f, edge - track.rx);
        track.cx = 0f;
        track.ry = .5f;
        track.cy = high - track.ry;
        track.amp = Dip;
        track.period = Period;
        track.triangle = true;
    }

    public override float MinAttackAbove => 2.2f;
    public override bool WantsAttack(Vector2 seen) =>
        track.OnStraight(u, .5f) && Mathf.Abs(Pos.x) < EliteSystem.RailEdge - .6f && Pos.y > seen.y + MinAttackAbove;
}

// Lens Matriarch: a glass fan. The tell HALTS it where it is (the panels turn): from the first frame thin rays are drawn from its
// muzzle, Want of them (hazardCount: 6 or 8) in a fan centred on the pilot's lane when the tell began, shotSpread degrees apart
// and the CENTRAL gap left open -- there is no ray on the pilot's own line. The action sends a glass shard (shotSpeed) down every ray
// at once. Hold still in the gap, or slip sideways between two rays.
public class GlassFanAttack : EliteAttack
{
    public const int Rays = 6, MaxRays = 8;
    public const float Half = .03f, Reach = 14f;
    readonly EliteTelegraph tele = new EliteTelegraph();
    readonly float[] deg = new float[MaxRays];
    int count;
    bool acted;
    public GlassFanAttack() { Id = "glass_fan"; }
    public override bool HoldsDuringTell => true;
    public override bool HoldsDuringAction => true;
    public override float? FaceDeg => -90f;
    public EliteTelegraph Telegraph => tele;
    public int Count => count;
    public float RayDeg(int i) => deg[Mathf.Clamp(i, 0, MaxRays - 1)];
    // (the script's hazardCount, in pairs: 6 rays, or 8 with hazardCount 8; without a script value, 6)
    public int Want => def.hazardCount >= 4 ? Mathf.Min(MaxRays, def.hazardCount & ~1) : Rays;
    public float Spread => Mathf.Max(8f, def.shotSpread);

    public override void BeginTell(Vector2 seen)
    {
        base.BeginTell(seen);
        acted = false;
        tele.Clear();
        Vector2 o = ship.MuzzleWorld(0);
        float centre = Mathf.Clamp(Deg(seen - o), -150f, -30f);
        count = Want;
        for (int k = 0; k < count; k++)
        {
            // -.5, +.5, -1.5, +1.5 ... of the spread: the pilot's own line (0) is never a ray
            int pair = k / 2;
            float side = k % 2 == 0 ? -1f : 1f;
            deg[k] = centre + side * (pair + .5f) * Spread;
            tele.Lane(o, o + Unit(deg[k]) * Reach, Half);
        }
        tele.Show(TellSeconds, def.ShotColor, def.WorldIndex);
    }

    public override void StepTell(float dt) { t += dt; tele.Step(dt); }
    public override void Cancel() { tele.End(); }
    public override void OnDeath() { tele.End(); }

    public override void BeginAction()
    {
        base.BeginAction();
        acted = true;
        tele.GoLive();
        for (int k = 0; k < count; k++) Fire(0, deg[k], def.shotSpeed);
    }

    public override bool StepAction(float dt)
    {
        t += dt;
        return t >= def.actionSeconds;
    }

    public override void End()
    {
        tele.End();
        if (acted)
        {
            var gb = ship.Brain as GlintBrain;
            if (gb != null) gb.CastDone();
        }
        acted = false;
    }
}

// ---- Glass Cannon: lenspost / lens_beams -----------------------------------------------------------------------------------

// A siege deck on three perches (the left, the middle or the right column of the top). The SCRIPT sends it from one to the next with
// "perch:0|1|2" tokens (left, centre, right, centre ...); it glides there during the beat's wait and casts only on a perch.
public class LenspostBrain : EliteBrain
{
    public const int Perches = 3;
    int perch = 1;
    public int Perch => perch;
    public int Moves { get; private set; }
    public float Height => DesertFlight.Height(ship, def);
    public float PerchX(int i) => (i - 1) * DesertFlight.Edge(def, .6f) * .8f;
    public bool OnPerch => Mathf.Abs(Pos.x - PerchX(perch)) < .25f;

    public LenspostBrain() { Id = "lenspost"; }

    bool seeded;
    protected override void OnBind() { perch = 1; }
    public override Vector2 JoinFrom => Vector2.up;
    public override Vector2 Goal(Vector2 seen, float dt)
    {
        Clock += dt;
        return new Vector2(PerchX(perch), Height + Mathf.Sin(Clock * .9f) * .06f);
    }

    public override float MinAttackAbove => 2.2f;
    public override bool WantsAttack(Vector2 seen) => OnPerch && Pos.y > seen.y + MinAttackAbove;
    public override float? FaceDeg(Vector2 seen) => -90f;

    // "perch:N" (no allocation: the digit is read in place). The first token only places it; later ones that change it count as moves.
    public override void OnScriptMove(string token)
    {
        if (token == null || token.Length != 7 || !token.StartsWith("perch:", System.StringComparison.Ordinal)) return;
        int n = Mathf.Clamp(token[6] - '0', 0, Perches - 1);
        if (seeded && n != perch) Moves++;
        seeded = true;
        perch = n;
    }
}

// Glass Cannon: lens beams. The tell holds it nose down while the lens focuses: thin beam lines are drawn from the lens, Beams of them
// (six at its last heart), FanDeg apart and EITHER SIDE of the pilot's own line when the tell began -- there is no beam on the pilot's
// lane. The action runs a stream of shotCount thin bolts (shotSpeed, shotInterval apart) down each line, one beam after another,
// BeamGap apart, the farthest from the pilot's line first and the nearest last. Nothing flashes. Stand between the beams.
public class LensBeamsAttack : EliteAttack
{
    public const int Beams = 4, MaxBeams = 6;
    public const float FanDeg = 11f, Half = .035f, Reach = 14f, BeamGap = .38f;
    readonly EliteTelegraph tele = new EliteTelegraph();
    readonly float[] deg = new float[MaxBeams];
    readonly int[] sent = new int[MaxBeams];
    int count;
    bool acted;
    public LensBeamsAttack() { Id = "lens_beams"; }
    public override bool HoldsDuringTell => true;
    public override bool HoldsDuringAction => true;
    public override float? FaceDeg => -90f;
    public EliteTelegraph Telegraph => tele;
    public int Count => count;
    public float BeamDeg(int i) => deg[Mathf.Clamp(i, 0, MaxBeams - 1)];
    // (the script's hazardCount, in pairs: 4 beams, 6 at most; without a script value, 4)
    public int Want => def.hazardCount >= 4 ? Mathf.Min(MaxBeams, def.hazardCount & ~1) : Beams;
    // The angle between neighbouring beams: the script's shotSpread, else FanDeg.
    public float Fan => def.shotSpread >= 6f ? def.shotSpread : FanDeg;
    public int Streak => Mathf.Max(2, def.shotCount);

    public override void BeginTell(Vector2 seen)
    {
        base.BeginTell(seen);
        acted = false;
        tele.Clear();
        Vector2 o = ship.MuzzleWorld(0);
        float centre = Mathf.Clamp(Deg(seen - o), -150f, -30f);
        count = Want;
        // outside in: the pairs are laid out from the farthest pair to the nearest
        int pairs = count / 2;
        for (int k = 0; k < count; k++)
        {
            int pair = pairs - 1 - k / 2;
            float side = k % 2 == 0 ? -1f : 1f;
            deg[k] = centre + side * (pair + 1) * Fan;
            sent[k] = 0;
            tele.Lane(o, o + Unit(deg[k]) * Reach, Half);
        }
        tele.Show(TellSeconds, def.ShotColor, def.WorldIndex);
    }

    public override void StepTell(float dt) { t += dt; tele.Step(dt); }
    public override void Cancel() { tele.End(); }
    public override void OnDeath() { tele.End(); }

    public override void BeginAction()
    {
        base.BeginAction();
        acted = true;
        tele.GoLive();
        for (int k = 0; k < MaxBeams; k++) sent[k] = 0;
    }

    public override bool StepAction(float dt)
    {
        t += dt;
        int n = Streak;
        bool all = true;
        for (int k = 0; k < count; k++)
        {
            float startAt = (k / 2) * BeamGap;
            while (sent[k] < n && t >= startAt + sent[k] * Mathf.Max(.05f, def.shotInterval))
            {
                Fire(0, deg[k], def.shotSpeed);
                sent[k]++;
            }
            all &= sent[k] >= n;
        }
        return all && t >= def.actionSeconds;
    }

    public override void End()
    {
        tele.End();
        acted = false;   // (the next perch is the script's next beat)
    }
}

// ---- Scorpion Skiff: flanker / boulder_roll ---------------------------------------------------------------------------------

// A skiff that holds a FLANK: near a rail, high on the board, bobbing; the script beat "cross" (after every throw) scuttles it across the top to the other rail.
// It never comes down and never takes the pilot's lane.
public class FlankerBrain : EliteBrain
{
    public const float CrossScale = 2.0f, Margin = .5f;
    float side = 1f;
    bool crossing;
    public float Side => side;
    public bool Crossing => crossing;
    public int Crossings { get; private set; }
    public float Height => DesertFlight.Height(ship, def) - .35f;
    public float FlankX => side * DesertFlight.Edge(def, Margin);

    public FlankerBrain() { Id = "flanker"; }

    protected override void OnBind() { side = ship.Script.Alternate("flank"); }
    public override void OnJoin() { side = Pos.x >= 0f ? 1f : -1f; crossing = false; }
    public override void OnReturn() { crossing = false; }
    public override Vector2 JoinFrom => new Vector2(side, 1f);

    public override Vector2 Goal(Vector2 seen, float dt)
    {
        Clock += dt;
        if (crossing && Mathf.Abs(Pos.x - FlankX) < .3f) crossing = false;
        return new Vector2(FlankX, Height + Mathf.Sin(Clock * 1.4f) * .18f);
    }

    public override float SpeedScale => crossing ? CrossScale : 1f;
    public override float MinAttackAbove => 1.8f;
    public override bool WantsAttack(Vector2 seen) => !crossing && Pos.y > seen.y + MinAttackAbove;
    public override float? FaceDeg(Vector2 seen) => -90f;

    // "cross": across to the other flank (not while it is already crossing).
    public override void OnScriptMove(string token)
    {
        if (token == "cross" && !crossing) Cross();
    }

    public void Cross()
    {
        side = -side;
        crossing = true;
        Crossings++;
    }
}

// Scorpion Skiff: boulder roll. The tell holds it still: the path of each boulder is drawn from the first frame as thin lanes -- out of
// its claw to a ringed bounce point on the FAR rail, then (mirrored) on down to the bottom of the view -- Boulders of them (hazardCount: two once it
// has lost a heart), the second a hand higher. The action sends a boulder (a skid slab, EliteShots.Kind.Slab) down each path,
// BoulderGap apart; it glances off the far rail exactly where the ring is and rolls on out of the bottom. A boulder soaks hazardArmour
// player hits before it breaks. Step off the lane.
public class BoulderRollAttack : EliteAttack
{
    public const int MaxBoulders = 2;
    public const float BoulderGap = .55f, LaneMargin = .1f, BounceAhead = 1.0f, HandUp = 1.3f;
    readonly EliteTelegraph lanes = new EliteTelegraph();
    readonly Vector2[] bounce = new Vector2[MaxBoulders];
    readonly Vector2[] bottom = new Vector2[MaxBoulders];
    readonly EliteShot[] rocks = new EliteShot[MaxBoulders];
    Vector2 from;
    int count, thrown;
    float next;
    bool acted;
    public BoulderRollAttack() { Id = "boulder_roll"; }
    public override bool HoldsDuringTell => true;
    public override bool HoldsDuringAction => true;
    public override float? FaceDeg => -90f;
    public EliteTelegraph Lanes => lanes;
    public int Count => count;
    public int Thrown => thrown;
    public Vector2 Bounce(int i) => bounce[Mathf.Clamp(i, 0, MaxBoulders - 1)];
    public Vector2 PathEnd(int i) => bottom[Mathf.Clamp(i, 0, MaxBoulders - 1)];
    public Vector2 From => from;
    public EliteShot Rock(int i) => rocks[Mathf.Clamp(i, 0, MaxBoulders - 1)];
    public int Want => Mathf.Clamp(def.hazardCount, 1, MaxBoulders);   // (the script's hazardCount: 1 or 2 boulders)
    public float Speed => def.skidSpeed;
    float Radius => def.hazardSize * .42f;

    public override void BeginTell(Vector2 seen)
    {
        base.BeginTell(seen);
        acted = false;
        thrown = 0;
        for (int i = 0; i < MaxBoulders; i++) rocks[i] = null;
        lanes.Clear();
        from = ship.MuzzleWorld(0);
        count = Want;
        float railX = -Mathf.Sign(from.x >= 0f ? 1f : -1f) * (EliteSystem.RailEdge - Radius - .02f);
        float half = Radius + LaneMargin;
        for (int i = 0; i < count; i++)
        {
            float y = Mathf.Clamp(seen.y + BounceAhead + i * HandUp, EliteSystem.ViewBottom + 1.5f, Mathf.Max(EliteSystem.ViewBottom + 1.6f, from.y - 1.2f));
            bounce[i] = new Vector2(railX, y);
            Vector2 d = (bounce[i] - from).normalized;
            float k = (bounce[i].y - (EliteSystem.ViewBottom - .5f)) / Mathf.Max(.1f, -d.y);
            bottom[i] = bounce[i] + new Vector2(-d.x, d.y) * k;
            lanes.Lane(from, bounce[i], half);
            lanes.Lane(bounce[i], bottom[i], half);
            lanes.Ring(bounce[i], half * 1.6f);
        }
        lanes.Show(TellSeconds, def.ShotColor, def.WorldIndex);
    }

    public override void StepTell(float dt) { t += dt; lanes.Step(dt); }
    public override void Cancel() { lanes.End(); }

    public override void BeginAction()
    {
        base.BeginAction();
        acted = true;
        lanes.GoLive();
        thrown = 0;
        next = 0f;
    }

    public override bool StepAction(float dt)
    {
        t += dt;
        while (t >= next && thrown < count)
        {
            Vector2 at = ship.MuzzleWorld(0);
            Vector2 d = bounce[thrown] - at;
            var rock = Fire(0, Deg(d), 0f, EliteShots.Kind.Slab);
            if (rock != null) rock.Skid(d.normalized * Speed);
            rocks[thrown] = rock;
            thrown++;
            next += BoulderGap;
        }
        return thrown >= count && t >= def.actionSeconds;
    }

    public override void End()
    {
        lanes.End();
        acted = false;   // (the scuttle across is the script's next beat)
    }

    // Its boulders break up with it.
    public override void OnDeath()
    {
        lanes.End();
        for (int i = 0; i < MaxBoulders; i++)
        {
            var s = rocks[i];
            rocks[i] = null;
            if (s != null && s.Active && s.Owner == ship && s.Kind == EliteShots.Kind.Slab)
            {
                s.Struck();
                if (s.Active) { EliteSystem.Fx.Sparks(s.transform.position, def.ShotColor, 6); s.Recycle(); }
            }
        }
    }
}

// ---- Mirror Bastion: bulwark / dust_wall ------------------------------------------------------------------------------------

// A glass fortress. It stands on ONE side rail and slides up and down it between three slots (the top, a hand lower, two hands lower),
// holding still while it casts; the dust wall sweeps from the opposite rail. Script tokens: "slot:0|1|2" (slide to that slot), "swap"
// (cross to the other rail); the rails also swap whenever the script enters a new phase. It never follows the pilot.
public class BulwarkBrain : EliteBrain
{
    public const int Slots = 3;
    public const float SlotStep = .9f, Margin = .3f;
    float side = 1f;
    int slot;
    public float Side => side;
    public int Slot => slot;
    public int Swaps { get; private set; }
    public float Height => DesertFlight.Height(ship, def);
    public float RailX => side * DesertFlight.Edge(def, Margin);
    public float SlotY(int i) => Height - Mathf.Clamp(i, 0, Slots - 1) * SlotStep;
    public bool AtSlot => Mathf.Abs(Pos.y - SlotY(slot)) < .3f && Mathf.Abs(Pos.x - RailX) < .3f;

    public BulwarkBrain() { Id = "bulwark"; }

    protected override void OnBind() { side = ship.Script.Alternate("rail"); }
    public override void OnJoin() { slot = 0; }
    public override Vector2 JoinFrom => Vector2.up;
    public override float SpeedScale => 1f;
    public override float MinAttackAbove => 1.2f;
    public override bool WantsAttack(Vector2 seen) => AtSlot && Pos.y > seen.y + MinAttackAbove;
    public override float? FaceDeg(Vector2 seen) => -90f;

    public override void OnScriptMove(string token)
    {
        if (token == null) return;
        if (token == "swap") { Swap(); return; }
        if (token.Length == 6 && token.StartsWith("slot:", System.StringComparison.Ordinal)) slot = Mathf.Clamp(token[5] - '0', 0, Slots - 1);
    }

    // (a new phase: the other rail)
    public override void OnPhase(int phase) { Swap(); }

    void Swap() { side = -side; Swaps++; }

    public override Vector2 Goal(Vector2 follow, float dt)
    {
        Clock += dt;
        return new Vector2(RailX, SlotY(slot) + Mathf.Sin(Clock * .8f) * .05f);
    }
}

// Mirror Bastion: a dust wall. The tell holds it nose down: the edges of the GAP of each wave are drawn as thin lines across the
// board, from the first frame. A wave is a wall of dust that sweeps SIDEWAYS across the board (shotSpeed) from the rail far from the
// pilot to the other: a grain every Spacing up the board except inside the gap, the waves WaveGap apart (each staggered half a grain). The
// first gap sits HandOff above or below the pilot's height (toward the middle of the board's height, so the pilot has to move); each
// later wave slides its gap a hand (WaveSlide) the same way. hazardCount waves (two or three).
public class DustWallAttack : EliteAttack
{
    public const float GapHalf = .75f, HandOff = 1.4f, WaveSlide = .6f, Half = .035f, WaveGap = 1.0f, Lip = .12f;
    public const int MaxWaves = 3;
    readonly EliteTelegraph tele = new EliteTelegraph();
    readonly float[] gap = new float[MaxWaves];
    int waves, fired;
    float next;
    bool acted, fromLeft, mirror;
    public DustWallAttack() { Id = "dust_wall"; }
    public override bool HoldsDuringTell => true;
    public override bool HoldsDuringAction => true;
    public override bool Shoots => false;   // (the grains come off the rails, not out of the hull's muzzles)
    public override float? FaceDeg => -90f;
    public EliteTelegraph Telegraph => tele;
    public int Waves => waves;
    public int Sent => fired;
    public float GapCentre(int w) => gap[Mathf.Clamp(w, 0, MaxWaves - 1)];
    // Every wave comes from the rail OPPOSITE the bastion's; a "mirror" beat sends every other wave from the bastion's own rail.
    public bool FromLeft(int w) => mirror && w % 2 == 1 ? !fromLeft : fromLeft;
    public bool Mirror => mirror;
    // (the script's hazardCount: two or three waves)
    public int WaveCount => Mathf.Clamp(def.hazardCount, 2, MaxWaves);
    public float Spacing => Mathf.Max(.45f, def.lobSpacing);
    public float Low => EliteSystem.ViewBottom + .35f;
    public float High => DesertFlight.Height(ship, def) - 1.0f;   // (the wall spans the same board whichever slot the bastion slides to)

    public bool InGap(int w, float y) => Mathf.Abs(y - gap[w]) < GapHalf;

    public override void BeginTell(Vector2 seen)
    {
        base.BeginTell(seen);
        acted = false;
        fired = 0;
        waves = WaveCount;
        float lo = Low + GapHalf, hi = Mathf.Max(lo + .1f, High - GapHalf);
        // the pilot's height is never inside the first gap: above it when there is room, else below
        float way = seen.y + HandOff + GapHalf <= hi ? 1f : -1f;
        float g = Mathf.Clamp(seen.y + way * HandOff, lo, hi);
        float slide = g >= hi - .01f ? -1f : g <= lo + .01f ? 1f : way;
        var bb = ship.Brain as BulwarkBrain;
        fromLeft = (bb != null ? bb.Side : ship.Position.x) >= 0f;   // (the waves come from the rail opposite the bastion: they cross the whole board)
        mirror = ship.Script.VariantName == "mirror";
        tele.Clear();
        float e = EliteSystem.RailEdge;
        for (int w = 0; w < waves; w++)
        {
            gap[w] = Mathf.Clamp(g + slide * WaveSlide * w, lo, hi);
            tele.Lane(new Vector2(-e, gap[w] - GapHalf), new Vector2(e, gap[w] - GapHalf), Half);
            tele.Lane(new Vector2(-e, gap[w] + GapHalf), new Vector2(e, gap[w] + GapHalf), Half);
        }
        tele.Show(TellSeconds, def.ShotColor, def.WorldIndex);
    }

    public override void StepTell(float dt) { t += dt; tele.Step(dt); }
    public override void Cancel() { tele.End(); }
    public override void OnDeath() { tele.End(); }

    public override void BeginAction()
    {
        base.BeginAction();
        acted = true;
        tele.GoLive();
        fired = 0;
        next = 0f;
        if (ship != null) ship.OnFired(0, -90f);
    }

    public override bool StepAction(float dt)
    {
        t += dt;
        while (t >= next && fired < waves)
        {
            SendWave(fired);
            fired++;
            next += WaveGap;
        }
        return fired >= waves && t >= def.actionSeconds;
    }

    void SendWave(int w)
    {
        bool left = FromLeft(w);
        float x = (left ? -1f : 1f) * (EliteSystem.RailEdge - Lip);
        float deg = left ? 0f : 180f;
        float s = Spacing;
        float y = Low + (w % 2 == 0 ? 0f : s * .5f);
        var kind = EliteShots.KindOf(def.shotKind);
        for (; y <= High + .001f; y += s)
            if (!InGap(w, y)) FireFrom(new Vector2(x, y), deg, def.shotSpeed, kind);
    }

    public override void End()
    {
        tele.End();
        acted = false;
    }
}
