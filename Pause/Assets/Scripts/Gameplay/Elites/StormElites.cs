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
//   Anvil Cloud       3  thunderhead / static_field (hangar)   a flat thunderhead drifting along the top; the tell rings four static
//                                                              fields in a diamond ahead of the pilot, each lobbed onto its ring and
//                                                              lying as a pool; a fifth at the centre once it has lost two hearts
//   Refinery Tender   5  airship      / flare_drop   (rigbay)   armored  a slow airship that crosses the whole top and leaves by the far
//                                                              side, laying a staircase of fused flares down its heading (ringed);
//                                                              each bursts into an X of bolts. Returns from the other side, lower
//   Rod Dancer        3  dancer       / rod_strikes  (towerbay) hurtHide  weaves a figure of eight across the top; glyphs under every
//                                                              rod column at once, then the columns strike OUTSIDE IN, the pilot's
//                                                              own lane last
//   Skyhook Harrier   3  hooker       / cable_rake   (padring)  hurtHide  swoops across the top on a dipping arc (off one side, back
//                                                              from the other); the tell draws the cables it will drop (thin lines),
//                                                              hooks in, then rakes them down one after another along the arc
//   Static Bastion    5  citadel      / hail_wall    (hatch)    armored  crawls along the top; the tell draws the gap (thin edges) of a
//                                                              WALL of hail falling in two staggered waves (three at <= 2 hearts), the
//                                                              gap sliding a hand with each wave; it is NOT where the pilot stands
//
// Tender and Harrier ROAM (EliteBrain.Roams): their paths leave the view, so the ship neither clamps their goal nor plans evasions,
// and they are untouchable while outside the board. Everything an attack leaves on the board rides it. See docs/enemy-behaviours.md.

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

// A roamer that makes PASSES: in from a side of the view at a height in a band near the top, along its path at a steady pace, out of
// the other side; a short pause off the board, then the next pass from the opposite side at a new height. Subclasses give the band,
// the path's shape and when a cast fits.
public abstract class StormPassBrain : EliteBrain
{
    public enum Stage { Leave, Away, Pass }
    public const float PauseSeconds = 1.2f, LeaveScale = 2.2f;
    protected Stage stage = Stage.Leave;
    protected float wait, s, length, dirX = 1f, y0, afterLeave = .4f;
    protected int casts;
    protected Vector2 start;
    public Stage Phase => stage;
    public float DirX => dirX;
    public Vector2 Start => start;
    public int Passes { get; private set; }
    public int Casts => casts;
    public abstract int CastsAllowed { get; }
    protected abstract void Band(out float lo, out float hi);
    public virtual float PassScale => 1f;
    // The path `along` u from where the pass entered.
    public abstract Vector2 PathAt(float along);
    // Along the path at world x (the path runs straight across, so this is a plain offset).
    public float AlongAt(float x) => (x - start.x) * dirX;
    // How far along the pass the ship is.
    public float Along => (Pos.x - start.x) * dirX;

    public override bool Roams => true;
    public override float TellScale => 1f;
    public override Vector2 JoinFrom => new Vector2(-1f, .8f);
    protected override void OnBind() { dirX = Random.value < .5f ? -1f : 1f; }
    public override void OnJoin() { stage = Stage.Leave; }
    public override void OnReturn() { stage = Stage.Leave; }

    void BeginPass()
    {
        Passes++;
        dirX = -dirX;
        float lo, hi;
        Band(out lo, out hi);
        y0 = Random.Range(lo, hi);
        float off = StormFlight.OffX(def);
        start = new Vector2(-dirX * off, y0);
        length = 2f * off;
        s = 0f;
        casts = 0;
        ship.Warp(start);
        stage = Stage.Pass;
    }

    public override Vector2 Goal(Vector2 seen, float dt)
    {
        Clock += dt;
        // (an attack begun outside a pass -- only a forced one -- is cast on the spot: it neither leaves nor starts a pass mid-cast)
        if (stage != Stage.Pass && ship.State == EliteState.Attack) return Pos;
        switch (stage)
        {
            case Stage.Leave:
                // just up from a dock / back from hiding / done with a pass: out of the view by the nearer side
                if (StormFlight.OffBoard(def, Pos)) { stage = Stage.Away; wait = afterLeave; afterLeave = .4f; }
                return new Vector2(Mathf.Sign(Pos.x == 0f ? 1f : Pos.x) * (StormFlight.OffX(def) + 1f), Pos.y + .3f);
            case Stage.Away:
                wait -= dt;
                if (wait <= 0f) BeginPass();
                return Pos;
            default:
                s += def.speed * PassScale * dt;
                // the carrot is past the far edge: it flies on out and waits there
                if (s >= length) { stage = Stage.Leave; afterLeave = PauseSeconds; }
                return PathAt(s + 1.8f);
        }
    }

    public override float SpeedScale => stage == Stage.Leave ? LeaveScale : PassScale;
}

// ---- Anvil Cloud: thunderhead / static_field --------------------------------------------------------------------------

// A thunderhead. It drifts side to side along the top (a slow pendulum, about 85 % of the width) and holds still while it casts. It
// never comes down and never follows the pilot's lane.
public class ThunderheadBrain : EliteBrain
{
    public const float Pace = .6f, Ry = .08f;
    float phase;
    public int Dir => Mathf.Cos(phase) >= 0f ? 1 : -1;
    public float Rx => Mathf.Max(.4f, StormFlight.Edge(def, .5f) * .85f);
    public float Height => ship.ReachY(EliteSystem.ViewTop - def.topMargin);

    public ThunderheadBrain() { Id = "thunderhead"; }

    protected override void OnBind() { phase = Random.value * 6.28f; }
    public override Vector2 JoinFrom => Vector2.up;
    public override float SpeedScale => .8f;
    public override float MinAttackAbove => 1.5f;
    public override bool WantsAttack(Vector2 seen) => Pos.y > seen.y + MinAttackAbove;
    public override float? FaceDeg(Vector2 seen) => -90f;

    public override Vector2 Goal(Vector2 follow, float dt)
    {
        Clock += dt;
        if (ship.State != EliteState.Attack) phase += def.speed * Pace / Rx * dt;
        return new Vector2(Mathf.Sin(phase) * Rx, Height + Mathf.Sin(phase * 2f) * Ry);
    }
}

// Anvil Cloud: static fields. The tell holds it nose down: thin rings mark a DIAMOND of fields, Diamond u across, centred a lobAhead
// above where it sees the pilot (followed for the first aimCommit of the tell, then locked and blinking faster). The action lobs a
// charged cloud out of the cannon onto each ring, shotInterval apart; each lands as a pool that rides the board for poolSeconds and
// crackles on touch. At <= 1 heart (two lost) a fifth field fills the middle of the diamond. Go round the diamond, or through the
// gaps between its corners.
public class StaticFieldAttack : StormSpotAttack
{
    public const float Ring = .45f, Diamond = 1.3f;
    public StaticFieldAttack() { Id = "static_field"; }
    protected override float RingRadius => Ring;
    public override float HazardSeconds => def.poolSeconds;
    public override bool Aimed => true;
    public override bool HoldsDuringTell => true;
    public override bool HoldsDuringAction => true;
    public override float? FaceDeg => -90f;
    protected override bool PopsOnDeath => false;   // (pools lapse on their own)
    public int HeartsLost => Mathf.Max(0, def.hearts - ship.Hearts);
    public int PlanCount => HeartsLost >= 2 ? 5 : 4;

    protected override void Plan(Vector2 pilot)
    {
        count = PlanCount;
        float edge = EliteSystem.RailEdge - .45f;
        float cx = Mathf.Clamp(pilot.x, -edge + Diamond, edge - Diamond);
        float cy = Mathf.Max(EliteSystem.ViewBottom + .8f + Diamond, pilot.y + def.lobAhead);
        cy = Mathf.Min(cy, ship.Position.y - 1.2f - Diamond);
        var c = new Vector2(cx, cy);
        for (int i = 0; i < 4; i++) spot[i] = c + Unit(90f + i * 90f) * Diamond;
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

// A slow airship. A PASS is: in from a side at a height in the top band, straight across at a steady pace, out of the far side; it
// casts FlareDropAttack once on the way (twice with <= 3 hearts) where the whole staircase of flares fits between the rails, then
// leaves, waits off the board and comes back from the other side.
public class AirshipBrain : StormPassBrain
{
    public AirshipBrain() { Id = "airship"; }
    // (it slows to a drift while it rings the flares, so the whole staircase stays between the rails)
    public const float TellDrift = .4f;
    public override float TellScale => TellDrift;
    public override int CastsAllowed => 1 + (ship != null && ship.Hearts <= 3 ? 1 : 0);
    protected override void Band(out float lo, out float hi)
    {
        hi = EliteSystem.ViewTop - def.hullRadius - .6f;
        lo = hi - 1.2f;
    }
    public override Vector2 PathAt(float along) => new Vector2(start.x + dirX * along, y0);

    // The x the ship will be at in `seconds` (it drifts at TellDrift of its pace through the wind-up).
    public float XAfter(float seconds) => Pos.x + dirX * def.speed * PassScale * TellDrift * seconds;

    public override bool WantsAttack(Vector2 seen)
    {
        if (stage != Stage.Pass || casts >= CastsAllowed) return false;
        float edge = EliteSystem.RailEdge - FlareDropAttack.RailMargin;
        float x0 = XAfter(def.tellSeconds);
        float x1 = x0 + dirX * (FlareDropAttack.Flares - 1) * FlareDropAttack.Step;
        return Mathf.Abs(Pos.x) < edge && Mathf.Abs(x0) < edge && Mathf.Abs(x1) < edge;
    }

    public void CastDone() { casts++; }
}

// Refinery Tender: a flare drop. The ship keeps flying through the wind-up: from the first frame of the tell Flares thin rings mark a
// STAIRCASE down its heading, Step across and Drop down apiece, starting a little ahead of where it will be when it goes out. The action
// lobs a fused flare out of the hull onto each ring, shotInterval apart; a flare rides the board, then bursts after its fuse into an X
// of four short bolts. A shot defuses one. (Read the rings, slip between the Xs.)
public class FlareDropAttack : StormSpotAttack
{
    public const int Flares = 3;
    public const float Ring = .5f, Step = .7f, Drop = .85f, ArmSeconds = .55f, Under = 1.5f, RailMargin = .9f;
    public FlareDropAttack() { Id = "flare_drop"; }
    protected override float RingRadius => Ring;
    public override float HazardSeconds => def.hazardSeconds + ArmSeconds;
    public override float HazardRadius => Ring;

    protected override void Plan(Vector2 pilot)
    {
        var ab = ship.Brain as AirshipBrain;
        count = Flares;
        float edge = EliteSystem.RailEdge - FlareDropAttack.RailMargin;   // (a flare laid against a rail is lost to it)
        float x0 = ab != null ? ab.XAfter(TellSeconds) : ship.Position.x;
        float dir = ab != null ? ab.DirX : 1f;
        for (int k = 0; k < count; k++)
            spot[k] = new Vector2(Mathf.Clamp(x0 + dir * k * Step, -edge, edge), Mathf.Max(EliteSystem.ViewBottom + 1.4f, ship.Position.y - Under - k * Drop));
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

// A lightning-rod dancer. It weaves a figure of eight across the top (about 90 % of the width, a hand of height), pausing the weave
// while it casts. It never comes down and never follows the pilot's lane.
public class DancerBrain : EliteBrain
{
    public const float Pace = .5f, Ry = .3f;
    float angle;
    public float Angle => angle;
    public float Rx => Mathf.Max(.4f, StormFlight.Edge(def, .5f) * .9f);
    public float Height => ship.ReachY(EliteSystem.ViewTop - def.topMargin) - Ry;

    public DancerBrain() { Id = "dancer"; }

    protected override void OnBind() { angle = Random.value * 6.28f; }
    public override Vector2 JoinFrom => Vector2.up;
    public override float MinAttackAbove => 1.5f;
    public override bool WantsAttack(Vector2 seen) => Pos.y > seen.y + MinAttackAbove;
    public override float? FaceDeg(Vector2 seen) => -90f;

    public override Vector2 Goal(Vector2 follow, float dt)
    {
        Clock += dt;
        if (ship.State != EliteState.Attack) angle += def.speed * Pace / Rx * dt;
        return new Vector2(Mathf.Sin(angle) * Rx, Height + Mathf.Sin(angle * 2f) * Ry);
    }
}

// Rod Dancer: rod strikes. The rod coils pulse and a lane glyph is drawn under EVERY column for the whole tell (>= .7 s): the pilot's
// lane and the others either side at least a strike's corridor apart (StrikeLanes), shotCount of them (one more at its last heart,
// four at the most). Then the columns strike ONE AFTER ANOTHER, shotInterval (>= StormFlight.MinStrikeGap) apart, OUTSIDE IN: the
// column farthest from the pilot's lane first, the pilot's own lane last. (AttackStrike: .25 s live each.) Step off the glyph.
public class RodStrikesAttack : EliteAttack
{
    public const int MaxColumns = 4;
    readonly AttackStrike[] armed = new AttackStrike[MaxColumns];
    readonly float[] lanes = new float[MaxColumns];
    int count;
    public RodStrikesAttack() { Id = "rod_strikes"; }
    public override bool HoldsDuringTell => true;
    public override bool HoldsDuringAction => true;
    public override bool Shoots => false;   // (lightning, not shots)
    public int Armed => count;
    public AttackStrike Strike(int i) => armed[i];
    public float Lane(int i) => lanes[i];
    public float Gap => Mathf.Max(StormFlight.MinStrikeGap, def.shotInterval);
    public int Want => Mathf.Clamp(def.shotCount + (ship.Hearts <= 1 ? 1 : 0), 2, MaxColumns);

    public override void BeginTell(Vector2 seen)
    {
        base.BeginTell(seen);
        var spec = StrikeSpec.Standard(Mathf.Max(0, def.WorldIndex));
        count = StrikeLanes.Pick(seen.x, Want, Mathf.Max(AttackStrike.MinLaneSpacing, def.lobSpacing), BossRails.DrawnInnerEdge, spec.hitHalf, lanes);
        // outside in: farthest from the pilot's lane (lanes[0]) first, the pilot's lane last
        float pilotLane = lanes[0];
        for (int i = 1; i < count; i++)
        {
            float x = lanes[i];
            int j = i - 1;
            while (j >= 0 && Mathf.Abs(lanes[j] - pilotLane) < Mathf.Abs(x - pilotLane)) { lanes[j + 1] = lanes[j]; j--; }
            lanes[j + 1] = x;
        }
        for (int i = 0; i < count; i++) armed[i] = AttackStrike.Arm(spec, lanes[i], seen.y, TellSeconds + i * Gap, ship.gameObject);
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

// A skyhook raider. A PASS is: in from a side at a height in the upper band, a dipping arc across the board (Dip u lower in the
// middle) at a steady pace, out of the far side; off the board, then back from the other side at a new height. One cable rake a pass
// (two at its last heart), cast where the whole rake fits between the rails. It never comes down to the pilot.
public class HookerBrain : StormPassBrain
{
    public const float Dip = .9f;
    public HookerBrain() { Id = "hooker"; }
    public override int CastsAllowed => 1 + (ship != null && ship.Hearts <= 1 ? 1 : 0);
    public override float PassScale => 1.15f;
    protected override void Band(out float lo, out float hi)
    {
        hi = EliteSystem.ViewTop - def.hullRadius - .6f;
        lo = hi - 1.0f;
    }

    // The arc `along` u from where the pass entered: a half sine dip.
    public override Vector2 PathAt(float along)
    {
        float k = length > .01f ? Mathf.Clamp01(along / length) : 0f;
        return new Vector2(start.x + dirX * along, y0 - Dip * Mathf.Sin(k * Mathf.PI));
    }

    public override bool WantsAttack(Vector2 seen)
    {
        if (stage != Stage.Pass || casts >= CastsAllowed) return false;
        float edge = EliteSystem.RailEdge - .45f;
        float x1 = Pos.x + dirX * (CableRakeAttack.Cables - 1) * CableRakeAttack.Step;
        return Mathf.Abs(Pos.x) < edge && Mathf.Abs(x1) < edge;
    }

    public void CastDone() { casts++; }
}

// Skyhook Harrier: a cable rake. The tell HOLDS it where it is (the hooks go in): from the first frame thin lines mark the Cables
// cables it will drop, Step apart along its heading from its own column, each from the arc's height down to the bottom of the view.
// The action drops them one after another, shotInterval apart (the ship flying on along its arc the while): a bolt falls from the
// top of each line at shotSpeed. Wait for a cable's turn to pass and slip between two lines, or stay out of the swath.
public class CableRakeAttack : EliteAttack
{
    public const int Cables = 4;
    public const float Step = .95f, Half = .045f;
    readonly EliteTelegraph tele = new EliteTelegraph();
    readonly float[] cableX = new float[Cables];
    readonly float[] cableY = new float[Cables];
    int fired;
    float next;
    bool acted;
    public CableRakeAttack() { Id = "cable_rake"; }
    public override bool Shoots => false;   // (the cables drop out of the sky over the arc, not out of a muzzle)
    public override bool HoldsDuringTell => true;
    public override float? FaceDeg => -90f;
    public EliteTelegraph Telegraph => tele;
    public int Dropped => fired;
    public float CableX(int i) => cableX[Mathf.Clamp(i, 0, Cables - 1)];
    public float CableY(int i) => cableY[Mathf.Clamp(i, 0, Cables - 1)];
    public int Count => Cables;

    public override void BeginTell(Vector2 seen)
    {
        base.BeginTell(seen);
        acted = false;
        fired = 0;
        tele.Clear();
        var hb = ship.Brain as HookerBrain;
        float x0 = ship.Position.x;
        float dir = hb != null ? hb.DirX : 1f;
        float step = Mathf.Max(.6f, def.lobSpacing > .5f ? def.lobSpacing : Step);
        for (int i = 0; i < Cables; i++)
        {
            cableX[i] = x0 + dir * (i + 1) * step;
            cableY[i] = hb != null ? hb.PathAt(hb.AlongAt(cableX[i])).y : ship.Position.y;
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
        while (t >= next && fired < Cables)
        {
            FireFrom(new Vector2(cableX[fired], cableY[fired]), -90f, def.shotSpeed, EliteShots.KindOf(def.shotKind));
            if (fired == 0) ship.OnFired(0, -90f);
            fired++;
            next += def.shotInterval;
        }
        return fired >= Cables && t >= def.actionSeconds;
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

// A floating fortress. It crawls along the top (a slow pendulum over about 60 % of the width) and holds still while it casts. It
// never comes down and never follows the pilot.
public class CitadelBrain : EliteBrain
{
    public const float Pace = .22f;
    public float Rx => Mathf.Max(.3f, StormFlight.Edge(def, .5f) * .6f);
    public float Height => ship.ReachY(EliteSystem.ViewTop - def.topMargin);

    public CitadelBrain() { Id = "citadel"; }

    public override Vector2 JoinFrom => Vector2.up;
    public override float SpeedScale => .7f;
    public override float MinAttackAbove => 1.5f;
    public override bool WantsAttack(Vector2 seen) => Pos.y > seen.y + MinAttackAbove;
    public override float? FaceDeg(Vector2 seen) => -90f;
    public override Vector2 Goal(Vector2 follow, float dt)
    {
        Clock += dt;
        return new Vector2(Mathf.Sin(Clock * Pace) * Rx, Height);
    }
}

// Static Bastion: a hail wall. The tell holds it nose down: the edges of the GAP of each wave are drawn as thin lines the height of
// the view, from the first frame. The gap of the first wave sits HandOff to the side of where it saw the pilot at the start of the
// tell (toward the middle of the board, so the pilot has to move); each later wave slides it a hand (WaveSlide) further the same
// way. The action lets Waves walls of hail fall from the top of the view, shotInterval apart: a stone every Spacing across the board
// except inside the gap, each wave staggered half a stone from the last. (Two waves; three at <= 2 hearts.)
public class HailWallAttack : EliteAttack
{
    public const float GapHalf = .8f, HandOff = 1.3f, WaveSlide = .35f, Edge = .25f, Top = .25f;
    public const int MaxWaves = 3;
    readonly EliteTelegraph tele = new EliteTelegraph();
    readonly float[] gap = new float[MaxWaves];
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
    public int WaveCount => Mathf.Clamp(Mathf.Max(2, def.shotCount) + (ship.Hearts <= 2 ? 1 : 0), 2, MaxWaves);
    public float Spacing => Mathf.Max(.5f, def.lobSpacing);
    public float Fall => Mathf.Max(.8f, def.shotSpeed);

    // Is column x of wave w inside the wave's gap?
    public bool InGap(int w, float x) => Mathf.Abs(x - gap[w]) < GapHalf;

    public override void BeginTell(Vector2 seen)
    {
        base.BeginTell(seen);
        acted = false;
        fired = 0;
        waves = WaveCount;
        float limit = Mathf.Max(0f, EliteSystem.RailEdge - Edge - GapHalf);
        // toward the middle of the board; dead centre: the side with room (alternates per cast)
        float way = seen.x > .2f ? -1f : seen.x < -.2f ? 1f : (Random.value < .5f ? -1f : 1f);
        float g = Mathf.Clamp(seen.x + way * HandOff, -limit, limit);
        // if the clamp ate the offset (pilot at a rail), the slide goes the way there is room
        float slide = Mathf.Abs(g) >= limit - .01f ? -Mathf.Sign(g) : way;
        tele.Clear();
        for (int w = 0; w < waves; w++)
        {
            gap[w] = Mathf.Clamp(g + slide * WaveSlide * w, -limit, limit);
            tele.Lane(new Vector2(gap[w] - GapHalf, EliteSystem.ViewTop), new Vector2(gap[w] - GapHalf, EliteSystem.ViewBottom), .04f);
            tele.Lane(new Vector2(gap[w] + GapHalf, EliteSystem.ViewTop), new Vector2(gap[w] + GapHalf, EliteSystem.ViewBottom), .04f);
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
