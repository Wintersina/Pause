using UnityEngine;

// Storm's five elites as mini-boss fights (Codex's flight strips, Art/Enemies/Elite/Storm), each with its own brain and attack, in the
// gas giant's own material (static fields, flares, lightning rods, tether cables, hail). The rules they share with the Space, Frost,
// Ember and Tide elites: none of them follows the pilot -- they keep their distance and throw HAZARDS; every attack is telegraphed from
// the first frame of its tell by the shared thin, faint, gently blinking drawing (EliteMarks rings, EliteTelegraph lanes, the strike
// glyphs -- Storm's cue colours, never pink) and what it will cover is fixed at the tell (the pilot is never hit by what it was not
// shown). Lightning is the existing AttackStrike (a narrow column, StrikeSpec.Standard), the columns of one cast struck at least
// MinStrikeGap apart so the weather's own limit (StormTuning: below 3 pulses a second) holds for a fight as it does for the sky.
// Desert still uses the BASE brains and attacks (interceptor / lance_dash ... siege / siege_cannon): nothing below changes them.
//
// Every fight is an authored script (the def's "script" block, EliteScript.cs, docs/elites/elite-scripts.md): the script's steps hand the
// brain MOVE TOKENS (OnScriptMove) and name an attack VARIANT; the brains and attacks below do what the token and the variant say, with
// no coin flips (stations, sides, tiers, dips come from tokens or the script's counters). All five stay inside the view: only Rod Dancer
// and Skyhook Harrier ever leave it, on their one scripted exit at the last heart.
//
//   Anvil Cloud       3  thunderhead / static_field (hangar)   a stationary caster: it HOLDS one of three stations (left, centre,
//                                                              right) and hops to the next on a token; the tell rings a diamond of
//                                                              static fields ahead of the pilot (leaning toward its own station), each
//                                                              lobbed onto its ring and lying as a pool; a fifth at the centre from
//                                                              phase 2, a wider diamond and longer pools at the last heart
//   Refinery Tender   5  airship      / flare_drop   (rigbay)   armored  a slow airship flying racetrack legs across the top, a wide
//                                                              U-turn at each rail onto another TIER (a tier lower, or back up),
//                                                              laying a staircase of fused flares down its heading (ringed); each
//                                                              bursts into an X of bolts
//   Rod Dancer       3  dancer       / rod_strikes  (towerbay) hurtHide  weaves a figure of eight across the top (direction by token);
//                                                              glyphs under every rod column at once, then the columns strike OUTSIDE
//                                                              IN (phase 1), INSIDE OUT (phase 2) or from BOTH ENDS in (last heart)
//   Skyhook Harrier   3  hooker       / cable_rake   (padring)  hurtHide  swoops in V shapes from a top corner down under the middle and
//                                                              up to the opposite corner, a hook over the top at each rail; the tell
//                                                              draws the cables it will drop under the dip, then rakes them down one
//                                                              after another
//   Static Bastion    5  citadel      / hail_wall    (hatch)    armored  crawls to a rail end (or the centre) on a token and plants;
//                                                              the tell draws the gap (thin edges) of a WALL of hail falling in
//                                                              staggered waves, the gap sliding a hand with each wave and, at the last
//                                                              heart, closing in; it is NOT where the pilot stands
//
// Nothing roams: Tender and Harrier turn round at the rails inside the view (StormRailBrain). Everything an attack leaves on the board
// rides it. See docs/enemy-behaviours.md.

// ---- shared bits ---------------------------------------------------------------------------------------------------


public static class StormFlight
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
    // Lightning columns of one cast are struck at least this far apart in time (below 3 pulses a second, StormTuning).
    public const float MinStrikeGap = .42f;
}

public static class StormHazards
{
    // Pops every orb (flare) of `owner` still on the board: it went down with them out.
    public static void PopOrbs(EliteShip owner)
    {
        var shots = EliteSystem.ShotsIfAny;
        if (shots == null) return;
        foreach (var s in shots.All)
            if (s.Active && s.Owner == owner && s.Kind == EliteShots.Kind.Orb)
            {
                EliteSystem.Fx.Sparks(s.transform.position, owner.Def.ShotColor, 6);
                s.Recycle();
            }
    }
}

// An attack that LAYS things onto ringed spots: the spots (Plan) are drawn as thin blinking rings from the first frame of the tell
// (faster once the aim is locked, if it is aimed), then each is lobbed onto its spot, Stagger apart, riding the board down (the ring
// stays put until the thing has landed on it). Subclasses plan the spots and lay one.
public abstract class StormSpotAttack : EliteAttack
{
    public const int MaxSpots = 6;
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
    protected abstract void Done();
    protected virtual float Stagger => def.shotInterval;
    protected virtual bool PopsOnDeath => true;
    // What lands on a ring is dangerous within HazardRadius of it for HazardSeconds (the dodge bot's plan, AttackBudgetScenarios).
    public virtual float HazardRadius => RingRadius;
    public virtual float HazardSeconds => def.hazardSeconds;

    public EliteMarks Marks => marks;
    public int Count => count;
    public int Laid => laid;
    public Vector2 Spot(int i) => spot[Mathf.Clamp(i, 0, MaxSpots - 1)];

    void ShowRings(bool fast)
    {
        for (int k = 0; k < count; k++) marks.Set(k, spot[k], RingRadius, def.ShotColor, fast);
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

    public override void OnDeath()
    {
        marks.HideAll();
        if (PopsOnDeath) StormHazards.PopOrbs(ship);
    }
}

// A flier on a racetrack inside the view: a straight LEG across the top, then a U-TURN arc at the rail, then the next leg back. The
// subclass lays one leg and its arc (Lay) from where the last one ended; the brain walks a carrot along those points at the pace the
// phase says and the ship follows it. The carrot stands still through an entrance and through a wind-up that holds the ship (TellK 0)
// and drifts on through one that does not. Nothing leaves the view and nothing is warped: the turn IS the stage presence.
public abstract class StormRailBrain : EliteBrain
{
    const int Cap = 48;
    public const float Lead = .7f;
    protected readonly Vector2[] pt = new Vector2[Cap];
    protected readonly float[] cum = new float[Cap];
    protected int n, legPts;
    protected float s, total, dirX = 1f;
    protected bool started;

    public int Legs { get; private set; }
    public float DirX => dirX;
    // The edge the legs and their arcs stay inside (the goal clamp's own).
    public float Rail => Mathf.Max(.8f, EliteSystem.RailEdge - def.hullRadius - .35f);
    // The top of the racetrack: under the HUD band and the view's top.
    public float TopY => Mathf.Min(EliteSystem.ViewTop - def.hullRadius - .6f, ship.ZoneTop);
    protected abstract float Pace { get; }
    // Share of the pace through a wind-up / an action.
    protected abstract float TellK { get; }
    protected virtual float ActK => 1f;
    // The first point of the first leg (heading dirX).
    protected abstract Vector2 StartPoint();
    // Fills pt[] with the straight run from `from` (setting legPts after it) and then the U-turn arc.
    protected abstract void Lay(Vector2 from);

    public float LegLength => legPts > 0 ? cum[legPts - 1] : 0f;
    // The carrot (the goal) is still on the straight run.
    public bool OnLeg => started && s + Lead < LegLength;
    public float LegEndX => legPts > 0 ? pt[legPts - 1].x : 0f;
    public Vector2 Carrot => PointAt(s + Lead);

    protected void Add(Vector2 p)
    {
        if (n >= Cap) return;
        pt[n] = p;
        cum[n] = n == 0 ? 0f : cum[n - 1] + Vector2.Distance(pt[n - 1], p);
        n++;
    }

    // A half ellipse leaving `from` outward (dirOut) by rx and coming back to from.x, dy higher (or lower) than it began.
    protected void AddArc(Vector2 from, float dirOut, float rx, float dy, int steps)
    {
        for (int i = 1; i <= steps; i++)
        {
            float a = Mathf.PI * i / steps;
            Add(new Vector2(from.x + dirOut * rx * Mathf.Sin(a), from.y + dy * (1f - Mathf.Cos(a)) * .5f));
        }
    }

    protected Vector2 PointAt(float d)
    {
        if (n == 0) return ship != null ? ship.Position : Vector2.zero;
        if (d <= 0f) return pt[0];
        if (d >= cum[n - 1]) return pt[n - 1];
        int i = 1;
        while (i < n - 1 && cum[i] < d) i++;
        float seg = cum[i] - cum[i - 1];
        return seg > 1e-4f ? Vector2.Lerp(pt[i - 1], pt[i], (d - cum[i - 1]) / seg) : pt[i];
    }

    void BeginLeg(Vector2 from)
    {
        n = 0;
        legPts = 0;
        Lay(from);
        total = n > 0 ? cum[n - 1] : 0f;
        Legs++;
    }

    // Back from hiding: the racetrack starts afresh from its first corner.
    public override void OnReturn() { started = false; }

    public override Vector2 Goal(Vector2 seen, float dt)
    {
        Clock += dt;
        if (!started)
        {
            started = true;
            dirX = ship.Script.Alternate("rail");   // (+1 first: in from the left; the same every fight)
            s = 0f;
            BeginLeg(StartPoint());
        }
        float k = 1f;
        if (ship.State == EliteState.Join) k = 0f;
        else if (ship.Telling) k = TellK;
        else if (ship.Acting) k = ActK;
        s += def.speed * Pace * ship.Script.SpeedScale * k * dt;
        for (int guard = 0; guard < 3 && total > .05f && s + Lead >= total; guard++)
        {
            s -= total;
            dirX = -dirX;
            BeginLeg(pt[n - 1]);
        }
        return PointAt(s + Lead);
    }
}

// ---- Anvil Cloud: thunderhead / static_field --------------------------------------------------------------------------

// A thunderhead. It HOLDS one of three stations along the top (left, centre, right: tokens "L" "C" "R") and flies to the next when the
// script says; it casts only once it has settled on its station. It never slides, never comes down and never follows the pilot's lane.
public class ThunderheadBrain : EliteBrain
{
    public const float Bob = .06f;
    int station = 1;
    public int Station => station;
    public float Reach => Mathf.Max(.4f, StormFlight.Edge(def, .5f) * .8f);
    public float StationX(int i) => (i - 1) * Reach;
    public float Height => ship.ZoneY(ship.ReachY(EliteSystem.ViewTop - def.topMargin));
    public bool Settled => Mathf.Abs(Pos.x - StationX(station)) < .3f;

    public ThunderheadBrain() { Id = "thunderhead"; }

    public override Vector2 JoinFrom => Vector2.up;
    public override float SpeedScale => .9f;
    public override float MinAttackAbove => 1.5f;
    public override bool WantsAttack(Vector2 seen) => Pos.y > seen.y + MinAttackAbove && Settled;
    public override float? FaceDeg(Vector2 seen) => -90f;

    public override void OnScriptMove(string token)
    {
        if (token == "L") station = 0;
        else if (token == "C") station = 1;
        else if (token == "R") station = 2;
    }

    public override Vector2 Goal(Vector2 follow, float dt)
    {
        Clock += dt;
        return new Vector2(StationX(station), Height + Mathf.Sin(Clock * 1.4f) * Bob);
    }
}

// Anvil Cloud: static fields. The tell holds it nose down: thin rings mark a DIAMOND of fields, Diamond u across (the def's laneOffset,
// so a script variant widens it), centred a lobAhead above where it sees the pilot, leaning a third of the way toward its own station
// (followed for the first aimCommit of the tell, then locked and blinking faster). The action lobs a charged cloud out of the cannon
// onto each ring, shotInterval apart; each lands as a pool that rides the board for poolSeconds and crackles on touch. From phase 2 a
// fifth field fills the middle of the diamond. Go round the diamond, or through the gaps between its corners.
public class StaticFieldAttack : StormSpotAttack
{
    public const float Ring = .45f, Lean = .35f;
    public StaticFieldAttack() { Id = "static_field"; }
    protected override float RingRadius => Ring;
    public override float HazardSeconds => def.poolSeconds;
    public override bool Aimed => true;
    public override bool HoldsDuringTell => true;
    public override bool HoldsDuringAction => true;
    public override float? FaceDeg => -90f;
    protected override bool PopsOnDeath => false;   // (pools lapse on their own)
    public float Diamond => Mathf.Clamp(def.laneOffset, 1f, 1.9f);
    public int PlanCount => ship.Script.Phase >= EliteScriptRunner.Phase2 ? 5 : 4;

    protected override void Plan(Vector2 pilot)
    {
        count = PlanCount;
        float d = Diamond;
        float edge = EliteSystem.RailEdge - .45f;
        float cx = Mathf.Clamp(Mathf.Lerp(pilot.x, ship.Position.x, Lean), -edge + d, edge - d);
        float cy = Mathf.Max(EliteSystem.ViewBottom + .8f + d, pilot.y + def.lobAhead);
        cy = Mathf.Min(cy, ship.Position.y - 1.2f - d);
        var c = new Vector2(cx, cy);
        for (int i = 0; i < 4; i++) spot[i] = c + Unit(90f + i * 90f) * d;
        if (count > 4) spot[4] = c;
    }

    protected override void Lay(int i, Vector2 target)
    {
        int m = def.muzzles.Length > 0 ? i % def.muzzles.Length : 0;
        var cloud = Fire(m, Deg(target - (Vector2)ship.MuzzleWorld(m)), 0f, EliteShots.Kind.Glob);
        if (cloud != null) cloud.Lob(target, Mathf.Max(.15f, def.lobSeconds));
    }

    protected override void Done() { }
}

// ---- Refinery Tender: airship / flare_drop -----------------------------------------------------------------------------

// A slow airship on a racetrack across the top. Each LEG runs rail to rail at one TIER (a height); at the rail it makes a wide U-turn
// onto the next tier, the script naming it ("tier:0" ... "tier:2", the top one first) -- without a token it steps down a tier and back
// up. A cast (FlareDropAttack) fits on a straight leg with room ahead; it drifts on at TellDrift while it rings the flares, so the
// whole staircase stays between the rails. Phase 2 spaces the tiers wider ("lowers faster").
public class AirshipBrain : StormRailBrain
{
    public const float TellDrift = .4f, StepNormal = .5f, StepWide = .75f, StepLast = .6f;
    public const int Tiers = 3;
    float tierStep = StepNormal;
    int tier, tierWay = 1, pending = -1;
    public int Tier => tier;
    public float TierStep => tierStep;
    public int Casts { get; private set; }

    public AirshipBrain() { Id = "airship"; }
    protected override float Pace => .85f;
    protected override float TellK => TellDrift;
    public override float TellScale => TellDrift;
    public override Vector2 JoinFrom => new Vector2(-1f, .8f);
    public float TurnRx => Mathf.Min(.7f, Rail * .4f);
    public float LegHalf => Mathf.Max(.3f, Rail - TurnRx);
    public float TierY(int t) => TopY - t * tierStep;

    public override void OnScriptMove(string token)
    {
        // "tier:N" (no string work: tokens arrive mid-fight)
        if (token != null && token.Length == 6 && token[0] == 't' && token[4] == ':' && token[5] >= '0' && token[5] <= '9')
            pending = Mathf.Clamp(token[5] - '0', 0, Tiers - 1);
    }

    public override void OnPhase(int phase) { tierStep = phase == EliteScriptRunner.Phase2 ? StepWide : phase == EliteScriptRunner.PhaseLast ? StepLast : StepNormal; }

    protected override Vector2 StartPoint() => new Vector2(-dirX * LegHalf, TierY(tier));

    // The tier of the leg after this one: the token's, else a step along the up-and-down walk.
    int NextTier()
    {
        if (pending >= 0 && pending != tier) { int t = pending; pending = -1; return t; }
        pending = -1;
        int next = tier + tierWay;
        if (next >= Tiers) { tierWay = -1; next = tier - 1; }
        else if (next < 0) { tierWay = 1; next = tier + 1; }
        return next;
    }

    protected override void Lay(Vector2 from)
    {
        float y = from.y;
        Add(from);
        var end = new Vector2(dirX * LegHalf, y);
        Add(end);
        legPts = n;
        int next = NextTier();
        AddArc(end, dirX, TurnRx, TierY(next) - y, 10);
        tier = next;
    }

    // The x the ship will be at in `seconds` (it drifts at TellDrift of its pace through the wind-up).
    public float XAfter(float seconds) => Pos.x + dirX * def.speed * Pace * ship.Script.SpeedScale * TellDrift * seconds;

    public override bool WantsAttack(Vector2 seen)
    {
        if (!OnLeg) return false;
        float edge = EliteSystem.RailEdge - FlareDropAttack.RailMargin;
        float x0 = XAfter(def.tellSeconds);
        float x1 = x0 + dirX * FlareDropAttack.MaxSpan;
        // (still on the straight run when the tell is over, and the whole staircase between the rails)
        return Mathf.Abs(Pos.x) < edge && Mathf.Abs(x0) < edge && Mathf.Abs(x1) < edge && dirX * (LegEndX - x0) > .1f;
    }

    public void CastDone() { Casts++; }
}

// Refinery Tender: a flare drop. The ship keeps flying through the wind-up: from the first frame of the tell Count thin rings (3, or 4
// from a variant) mark a STAIRCASE down its heading, Step across (the def's lobSpacing) and Drop down apiece, starting a little ahead
// of where it will be when it goes out. The action lobs a fused flare out of the hull onto each ring, shotInterval apart; a flare rides
// the board, then bursts after its fuse into an X of four short bolts. A shot defuses one. (Read the rings, slip between the Xs.)
public class FlareDropAttack : StormSpotAttack
{
    public const int Flares = 3, MaxFlares = 4;
    public const float Ring = .5f, Drop = .85f, ArmSeconds = .55f, Under = 1.5f, RailMargin = .9f, MaxSpan = 1.5f;
    public FlareDropAttack() { Id = "flare_drop"; }
    protected override float RingRadius => Ring;
    public override float HazardSeconds => def.hazardSeconds + ArmSeconds;
    public override float HazardRadius => Ring;
    public int PlanCount => Mathf.Clamp(def.shotCount, Flares, MaxFlares);
    public float Step => Mathf.Clamp(def.lobSpacing, .45f, .8f);

    protected override void Plan(Vector2 pilot)
    {
        var ab = ship.Brain as AirshipBrain;
        count = PlanCount;
        float edge = EliteSystem.RailEdge - FlareDropAttack.RailMargin;   // (a flare laid against a rail is lost to it)
        float x0 = ab != null ? ab.XAfter(TellSeconds) : ship.Position.x;
        float dir = ab != null ? ab.DirX : 1f;
        float step = Step;
        for (int k = 0; k < count; k++)
            spot[k] = new Vector2(Mathf.Clamp(x0 + dir * k * step, -edge, edge), Mathf.Max(EliteSystem.ViewBottom + 1.4f, ship.Position.y - Under - k * Drop));
    }

    protected override void Lay(int i, Vector2 target)
    {
        int m = def.muzzles.Length > 0 ? i % def.muzzles.Length : 0;
        var flare = Fire(m, Deg(target - (Vector2)ship.MuzzleWorld(m)), 0f, EliteShots.Kind.Orb);
        if (flare != null)
            flare.Mine(target, def.lobSeconds, def.lobSeconds + Mathf.Max(.5f, def.hazardSeconds), 4, def.hazardSpeed, ArmSeconds, 45f, Vector2.zero, 1);
    }

    protected override void Done()
    {
        var ab = ship.Brain as AirshipBrain;
        if (ab != null) ab.CastDone();
    }
}

// ---- Rod Dancer: dancer / rod_strikes ---------------------------------------------------------------------------------

// A lightning-rod dancer. It weaves a figure of eight across the top (about 90 % of the width, a hand of height that grows with the
// phase), pausing the weave while it casts. The script turns the weave round ("cw" / "ccw"). It never comes down and never follows
// the pilot's lane.
public class DancerBrain : EliteBrain
{
    public const float Pace = .5f, RyNormal = .3f, RyPhase2 = .38f, RyLast = .46f;
    float angle, way = 1f, ry = RyNormal;
    public float Angle => angle;
    public float Way => way;
    public float Ry => ry;
    public float Rx => Mathf.Max(.4f, StormFlight.Edge(def, .5f) * .9f);
    public float Height => ship.ZoneY(ship.ReachY(EliteSystem.ViewTop - def.topMargin)) - ry;

    public DancerBrain() { Id = "dancer"; }

    public override Vector2 JoinFrom => Vector2.up;
    public override float MinAttackAbove => 1.5f;
    public override bool WantsAttack(Vector2 seen) => Pos.y > seen.y + MinAttackAbove;
    public override float? FaceDeg(Vector2 seen) => -90f;

    public override void OnScriptMove(string token)
    {
        if (token == "cw") way = 1f;
        else if (token == "ccw") way = -1f;
    }

    public override void OnPhase(int phase) { ry = phase == EliteScriptRunner.Phase2 ? RyPhase2 : phase == EliteScriptRunner.PhaseLast ? RyLast : RyNormal; }

    public override Vector2 Goal(Vector2 follow, float dt)
    {
        Clock += dt;
        if (ship.State != EliteState.Attack && ship.State != EliteState.Join) angle += way * def.speed * Pace * ship.Script.SpeedScale / Rx * dt;
        return new Vector2(Mathf.Sin(angle) * Rx, Height + Mathf.Sin(angle * 2f) * ry);
    }
}

// Rod Dancer: rod strikes. The rod coils pulse and a lane glyph is drawn under EVERY column for the whole tell (>= .7 s): the pilot's
// lane and the others either side at least a strike's corridor apart (StrikeLanes), shotCount of them (2 to 4). Then the columns strike
// ONE AFTER ANOTHER, shotInterval (>= StormFlight.MinStrikeGap) apart, in the order the script's variant names: OUTSIDE IN (the column
// farthest from the pilot's lane first, the pilot's own lane last: the default and every variant not named below), INSIDE OUT (variants
// "in*": the pilot's lane first), or from BOTH ENDS in (variants "ends*": left-most, right-most, next left-most ...). (AttackStrike:
// .25 s live each.) Step off the glyph.
public class RodStrikesAttack : EliteAttack
{
    public const int MaxColumns = 4;
    public enum Order { OutsideIn, InsideOut, Ends }
    readonly AttackStrike[] armed = new AttackStrike[MaxColumns];
    readonly float[] lanes = new float[MaxColumns];
    readonly float[] tmp = new float[MaxColumns];
    int count;
    Order order;
    public RodStrikesAttack() { Id = "rod_strikes"; }
    public override bool HoldsDuringTell => true;
    public override bool HoldsDuringAction => true;
    public override bool Shoots => false;   // (lightning, not shots)
    public int Armed => count;
    public Order Sequence => order;
    public AttackStrike Strike(int i) => armed[i];
    public float Lane(int i) => lanes[i];
    public float Gap => Mathf.Max(StormFlight.MinStrikeGap, def.shotInterval);
    public int Want => Mathf.Clamp(def.shotCount, 2, MaxColumns);

    // The order a variant name asks for.
    public static Order OrderFor(string variant)
    {
        if (!string.IsNullOrEmpty(variant))
        {
            if (variant.Length >= 2 && variant[0] == 'i' && variant[1] == 'n') return Order.InsideOut;
            if (variant.Length >= 4 && variant[0] == 'e' && variant[1] == 'n' && variant[2] == 'd' && variant[3] == 's') return Order.Ends;
        }
        return Order.OutsideIn;
    }

    public override void BeginTell(Vector2 seen)
    {
        base.BeginTell(seen);
        var spec = StrikeSpec.Standard(Mathf.Max(0, def.WorldIndex));
        count = StrikeLanes.Pick(seen.x, Want, Mathf.Max(AttackStrike.MinLaneSpacing, def.lobSpacing), BossRails.DrawnInnerEdge, spec.hitHalf, lanes);
        order = OrderFor(ship.Script.VariantName);
        Sort(lanes[0]);
        for (int i = 0; i < count; i++) armed[i] = AttackStrike.Arm(spec, lanes[i], seen.y, TellSeconds + i * Gap, ship.gameObject);
    }

    // lanes[0] is the pilot's lane. Orders lanes[0..count) as the attack's sequence.
    void Sort(float pilotLane)
    {
        if (order == Order.Ends)
        {
            for (int i = 1; i < count; i++)
            {
                float x = lanes[i];
                int j = i - 1;
                while (j >= 0 && lanes[j] > x) { lanes[j + 1] = lanes[j]; j--; }
                lanes[j + 1] = x;
            }
            for (int i = 0; i < count; i++) tmp[i] = lanes[i];
            int lo = 0, hi = count - 1;
            for (int i = 0; i < count; i++) lanes[i] = (i % 2 == 0) ? tmp[lo++] : tmp[hi--];
            return;
        }
        // by distance from the pilot's lane: farthest first (outside in) or nearest first (inside out)
        float sign = order == Order.OutsideIn ? 1f : -1f;
        for (int i = 1; i < count; i++)
        {
            float x = lanes[i];
            float dx = Mathf.Abs(x - pilotLane);
            int j = i - 1;
            while (j >= 0 && sign * Mathf.Abs(lanes[j] - pilotLane) < sign * dx) { lanes[j + 1] = lanes[j]; j--; }
            lanes[j + 1] = x;
        }
    }

    public override void Cancel() { Drop(); }
    public override void OnDeath() { Drop(); }

    void Drop()
    {
        for (int i = 0; i < count; i++)
        {
            if (armed[i] != null && armed[i].State == AttackHazard.Phase.Tell) armed[i].Cancel();
            armed[i] = null;
        }
        count = 0;
    }

    public override void BeginAction()
    {
        base.BeginAction();
        if (count > 0)
        {
            if (armed[0] != null) armed[0].Ignite();
            Fired++;
            ship.OnFired(0, -90f);
        }
    }

    bool AllLit()
    {
        for (int i = 0; i < count; i++) if (armed[i] != null && armed[i].State == AttackHazard.Phase.Tell) return false;
        return true;
    }

    public override bool StepAction(float dt)
    {
        t += dt;
        return (t >= def.actionSeconds && AllLit()) || t >= def.actionSeconds + count * Gap + 1.5f;
    }

    public override void End() { for (int i = 0; i < armed.Length; i++) armed[i] = null; count = 0; }
}

// ---- Skyhook Harrier: hooker / cable_rake ------------------------------------------------------------------------------

// A skyhook raider. Each LEG is a V swoop: from a top corner down under the middle of the board (Dip u) and up to the opposite corner
// (a hook's rise short of the top), where it hooks over the top at the rail (HookRx out, back to the top) and swoops again the other
// way. The script sets the depth of the next V ("dip:1" shallow, "dip:2" deep). One rake per beat, cast on the way DOWN to the dip
// (where the whole rake fits between the rails). It never comes down to the pilot and never leaves the view.
public class HookerBrain : StormRailBrain
{
    public const float DipShallow = .9f, DipDeep = 1.4f, HookRise = .5f, HookRx = .5f;
    float dip = DipShallow, dipNext = DipShallow;
    public float Dip => dip;
    public int Casts { get; private set; }

    public HookerBrain() { Id = "hooker"; }
    protected override float Pace => 1f;
    protected override float TellK => 0f;   // (the hooks go in: it holds through the wind-up)
    public override float SpeedScale => 1.25f;
    public override Vector2 JoinFrom => new Vector2(-1f, .8f);
    public float CornerX => Mathf.Max(.4f, Rail - HookRx);

    public override void OnScriptMove(string token)
    {
        if (token == "dip:1") dipNext = DipShallow;
        else if (token == "dip:2") dipNext = DipDeep;
    }

    protected override Vector2 StartPoint() => new Vector2(-dirX * CornerX, TopY);

    protected override void Lay(Vector2 from)
    {
        dip = dipNext;
        float top = from.y;
        Add(from);
        Add(new Vector2(0f, top - dip));
        var end = new Vector2(dirX * CornerX, top - HookRise);
        Add(end);
        legPts = n;
        AddArc(end, dirX, HookRx, HookRise, 8);
    }

    // The height of the V at world x (for the heading it flies now): down to the dip in the first half, up in the second.
    public float PathY(float x)
    {
        float c = CornerX;
        float t = Mathf.Clamp01(Mathf.Abs(x) / c);
        float top = TopY;
        return x * dirX < 0f ? top - dip * (1f - t) : Mathf.Lerp(top - dip, top - HookRise, t);
    }

    public override bool WantsAttack(Vector2 seen)
    {
        if (!OnLeg) return false;
        float along = dirX * Pos.x;
        // on the way down to the dip, with the whole rake between the rails
        return along < -.25f && along > -CornerX - .1f;
    }

    public void CastDone() { Casts++; }
}

// Skyhook Harrier: a cable rake. The tell HOLDS it where it is (the hooks go in): from the first frame thin lines mark the cables it
// will drop (Cables to MaxCables from the def's shotCount, a variant widens the rake), lobSpacing apart, centred under the dip of its V
// and ordered along its heading, each from the V's height down to the bottom of the view. The action drops them one after another,
// shotInterval apart (the ship flying on along its V the while): a bolt falls from the top of each line at shotSpeed. Wait for a
// cable's turn to pass and slip between two lines, or stay out of the swath.
public class CableRakeAttack : EliteAttack
{
    public const int Cables = 4, MaxCables = 6;
    public const float Step = .95f, Half = .045f;
    readonly EliteTelegraph tele = new EliteTelegraph();
    readonly float[] cableX = new float[MaxCables];
    readonly float[] cableY = new float[MaxCables];
    int count = Cables;
    int fired;
    float next;
    bool acted;
    public CableRakeAttack() { Id = "cable_rake"; }
    public override bool Shoots => false;   // (the cables drop out of the sky over the arc, not out of a muzzle)
    public override bool HoldsDuringTell => true;
    public override float? FaceDeg => -90f;
    public EliteTelegraph Telegraph => tele;
    public int Dropped => fired;
    public float CableX(int i) => cableX[Mathf.Clamp(i, 0, MaxCables - 1)];
    public float CableY(int i) => cableY[Mathf.Clamp(i, 0, MaxCables - 1)];
    public int Count => count;

    public override void BeginTell(Vector2 seen)
    {
        base.BeginTell(seen);
        acted = false;
        fired = 0;
        tele.Clear();
        var hb = ship.Brain as HookerBrain;
        count = Mathf.Clamp(def.shotCount, Cables, MaxCables);
        float dir = hb != null ? hb.DirX : 1f;
        float step = Mathf.Clamp(def.lobSpacing, .6f, 1.1f);
        float edge = EliteSystem.RailEdge - .35f;
        for (int i = 0; i < count; i++)
        {
            cableX[i] = Mathf.Clamp(dir * (i - (count - 1) * .5f) * step, -edge, edge);
            cableY[i] = hb != null ? hb.PathY(cableX[i]) : ship.Position.y;
            tele.Lane(new Vector2(cableX[i], cableY[i]), new Vector2(cableX[i], EliteSystem.ViewBottom - .3f), Half);
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
    }

    public override bool StepAction(float dt)
    {
        t += dt;
        while (t >= next && fired < count)
        {
            FireFrom(new Vector2(cableX[fired], cableY[fired]), -90f, def.shotSpeed, EliteShots.KindOf(def.shotKind));
            if (fired == 0) ship.OnFired(0, -90f);
            fired++;
            next += def.shotInterval;
        }
        return fired >= count && t >= def.actionSeconds;
    }

    public override void End()
    {
        tele.End();
        if (acted)
        {
            var hb = ship.Brain as HookerBrain;
            if (hb != null) hb.CastDone();
        }
        acted = false;
    }
}

// ---- Static Bastion: citadel / hail_wall ----------------------------------------------------------------------------------

// A floating fortress. It CRAWLS along the top to the spot the script names (a rail end "L" / "R", or the middle "C") and PLANTS there
// to drop its wall; it casts only once it has arrived, and crawls to the next spot after the cast. It never comes down and never
// follows the pilot.
public class CitadelBrain : EliteBrain
{
    public const float Crawl = .55f;
    float targetX, carrotX;
    public float TargetX => targetX;
    public float LegX => Mathf.Max(.3f, StormFlight.Edge(def, .5f) * .85f);
    public float Height => ship.ZoneY(ship.ReachY(EliteSystem.ViewTop - def.topMargin));
    public bool Planted => Mathf.Abs(Pos.x - targetX) < .3f;

    public CitadelBrain() { Id = "citadel"; }

    public override Vector2 JoinFrom => Vector2.up;
    public override float SpeedScale => .8f;
    public override float MinAttackAbove => 1.5f;
    public override bool WantsAttack(Vector2 seen) => Pos.y > seen.y + MinAttackAbove && Planted;
    public override float? FaceDeg(Vector2 seen) => -90f;

    public override void OnScriptMove(string token)
    {
        if (token == "L") targetX = -LegX;
        else if (token == "R") targetX = LegX;
        else if (token == "C") targetX = 0f;
    }

    public override Vector2 Goal(Vector2 follow, float dt)
    {
        Clock += dt;
        // (the entrance lands on the middle of the top; the crawl starts from there)
        if (ship.State == EliteState.Join) carrotX = 0f;
        else carrotX = Mathf.MoveTowards(carrotX, targetX, def.speed * Crawl * ship.Script.SpeedScale * dt);
        return new Vector2(carrotX, Height);
    }
}

// Static Bastion: a hail wall. The tell holds it nose down: the edges of the GAP of each wave are drawn as thin lines the height of
// the view, from the first frame. The gap of the first wave sits HandOff to the side of where it saw the pilot at the start of the
// tell (toward the middle of the board, so the pilot has to move); each later wave slides it a hand (WaveSlide) further the same
// way. The action lets Waves walls of hail fall from the top of the view, shotInterval apart: a stone every Spacing across the board
// except inside the gap, each wave staggered half a stone from the last. (The def's shotCount waves, 2 or 3; at the last heart the
// gap CLOSES IN, a hand narrower with every wave.)
public class HailWallAttack : EliteAttack
{
    public const float GapHalf = .8f, HandOff = 1.3f, WaveSlide = .35f, Edge = .25f, Top = .25f, ClosePerWave = .15f, MinGapHalf = .45f;
    public const int MaxWaves = 3;
    readonly EliteTelegraph tele = new EliteTelegraph();
    readonly float[] gap = new float[MaxWaves];
    readonly float[] half = new float[MaxWaves];
    int waves, fired;
    float next;
    bool acted;
    public HailWallAttack() { Id = "hail_wall"; }
    public override bool Shoots => false;   // (the hail falls from the top of the view, not out of a muzzle)
    public override bool HoldsDuringTell => true;
    public override bool HoldsDuringAction => true;
    public override float? FaceDeg => -90f;
    public EliteTelegraph Telegraph => tele;
    public int Waves => waves;
    public int Sent => fired;
    public float GapCentre(int w) => gap[Mathf.Clamp(w, 0, MaxWaves - 1)];
    public float GapHalfOf(int w) => half[Mathf.Clamp(w, 0, MaxWaves - 1)];
    public int WaveCount => Mathf.Clamp(def.shotCount, 2, MaxWaves);
    public bool Closing => ship.Script.Phase >= EliteScriptRunner.PhaseLast;
    public float Spacing => Mathf.Max(.5f, def.lobSpacing);
    public float Fall => Mathf.Max(.8f, def.shotSpeed);

    // Is column x of wave w inside the wave's gap?
    public bool InGap(int w, float x) => Mathf.Abs(x - gap[w]) < half[w];

    public override void BeginTell(Vector2 seen)
    {
        base.BeginTell(seen);
        acted = false;
        fired = 0;
        waves = WaveCount;
        float limit = Mathf.Max(0f, EliteSystem.RailEdge - Edge - GapHalf);
        // toward the middle of the board; dead centre: the side with room (the script's counter alternates it per cast)
        float way = seen.x > .2f ? -1f : seen.x < -.2f ? 1f : ship.Script.Alternate("gapway", -1f);
        float g = Mathf.Clamp(seen.x + way * HandOff, -limit, limit);
        // if the clamp ate the offset (pilot at a rail), the slide goes the way there is room
        float slide = Mathf.Abs(g) >= limit - .01f ? -Mathf.Sign(g) : way;
        bool closing = Closing;
        tele.Clear();
        for (int w = 0; w < waves; w++)
        {
            half[w] = closing ? Mathf.Max(MinGapHalf, GapHalf - ClosePerWave * w) : GapHalf;
            gap[w] = Mathf.Clamp(g + slide * WaveSlide * w, -limit, limit);
            tele.Lane(new Vector2(gap[w] - half[w], EliteSystem.ViewTop), new Vector2(gap[w] - half[w], EliteSystem.ViewBottom), .04f);
            tele.Lane(new Vector2(gap[w] + half[w], EliteSystem.ViewTop), new Vector2(gap[w] + half[w], EliteSystem.ViewBottom), .04f);
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
            DropWave(fired);
            fired++;
            next += Mathf.Max(.3f, def.shotInterval);
        }
        return fired >= waves && t >= def.actionSeconds;
    }

    void DropWave(int w)
    {
        float e = EliteSystem.RailEdge - Edge;
        float s = Spacing;
        float x = -e + (w % 2 == 0 ? 0f : s * .5f);
        var kind = EliteShots.KindOf(def.shotKind);
        for (; x <= e + .001f; x += s)
            if (!InGap(w, x)) FireFrom(new Vector2(x, EliteSystem.ViewTop + Top), -90f, Fall, kind);
    }

    public override void End()
    {
        tele.End();
        acted = false;
    }
}
