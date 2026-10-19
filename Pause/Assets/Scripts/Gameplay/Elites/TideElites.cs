using UnityEngine;

// Tide's five elites as mini-boss fights (Codex's flight strips, Art/Enemies/Elite/Tide/manifest_new_elites.md), each with its own brain
// and attack, in the ocean's own material (foam, sea mines, nets, thunder, pearls). The rules they share with the Space, Frost and Ember
// elites: none of them follows the pilot -- they keep their distance and throw HAZARDS; every attack is telegraphed from the first frame
// of its tell by the shared thin, faint, gently blinking drawing (EliteMarks rings, EliteTelegraph lanes, the strike glyphs -- Tide's cue
// colours, never pink) and its aim LOCKS at the tell (the pilot is never hit by what it was not shown). A cast that lays things on the
// board (foam, mines, nets) lobs each onto a ringed spot that rides the board. Storm and Desert still use the BASE brains and attacks
// (interceptor / lance_dash ... siege / siege_cannon): nothing below changes them. The first generation of Tide's own ids (surfer /
// surf_dash, abyss / depth_charge, netter / net_cast, stormray / thunder_strike, pearler / pearl_curtain) are kept, unused by the five.
//
//   Riptide      3  breaker   / foam_wake     (trenchhatch) hurtHide  rides a wave line across the top third, in from one side and out
//                                              the other; rings on the water behind it, then foam is lobbed onto them (touch hurts, a shot
//                                              pops it) -- the wake it leaves; re-enters from the other side. Hurt: two wakes a run
//   Bathyscaphe  5  deep      / mine_chain    (rigdeck)     armored   surfaces at the top, lays a CHAIN of sea mines down a line to the
//                                              pilot's column (rings first), each bursting after a fuse into a small cross of bolts, then
//                                              dives off the top and surfaces again at another column. More mines for every two hearts lost
//   Trawler      3  trawl     / drag_net      (reefdock)              stands on one FLANK against a rail and drags a line of nets in from
//                                              it, one after another, across the pilot's row -- ringed first, the far side always left open
//                                              -- then leaves by that side and re-enters on the opposite flank
//   Thunderfin   3  tempest   / thunder_sweep (ventstack)   hurtHide  circles slowly in a small ellipse high over the board; thunder
//                                              glyphs mark every column at once and the columns then strike one after another, sweeping the
//                                              way it is flying (the pilot's own lane included)
//   Pearl Warden 5  oyster    / pearl_spiral  (wreckbay)    armored   keeps one of three stations at the top; sweeps a spiral of slow
//                                              pearls down an arc (the two edge lanes drawn), with a gap left in the direction the pilot stood
//                                              at the lock; hops to another station after every cast
//
// Riptide, Bathyscaphe and Trawler ROAM (EliteBrain.Roams): their paths leave the view, so the ship neither clamps their goal nor plans
// evasions, and they are untouchable while outside the board. Everything an attack leaves on the board rides it.

// ---- shared bits ---------------------------------------------------------------------------------------------------

public static class TideFlight
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
}

public static class TideHazards
{
    // Pops every orb (foam, mine) of `owner` still on the board: it went down with them out.
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
public abstract class TideSpotAttack : EliteAttack
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
        if (PopsOnDeath) TideHazards.PopOrbs(ship);
    }
}

// ---- Riptide: breaker ------------------------------------------------------------------------------------------------

// A wave rider. A RUN is: in from a side of the view at a height in the top third, along a gentle wave line (Amplitude, WaveLength) at a
// steady pace and out of the other side; it never comes down to the pilot. The sides alternate. One wake a run (FoamWakeAttack), two at
// its last heart, laid in the window where the whole wind-up still fits between the rails. Each heart lost makes the run LostBoost faster.
public class BreakerBrain : EliteBrain
{
    public enum Stage { Leave, Away, Run }
    public const float PauseSeconds = 1.0f, Lead = 2.2f, LostBoost = .1f, LeaveScale = 2.2f;
    public const float Amplitude = .5f, WaveLength = 3.4f, Behind = .6f, SpotStep = .8f;
    public const int Spots = 3;
    Stage stage = Stage.Leave;
    float wait, s, length, dirX = 1f, y0, afterLeave = .4f;
    int casts;
    Vector2 start;
    public Stage Phase => stage;
    public float DirX => dirX;
    public Vector2 Start => start;
    public int Runs { get; private set; }
    public int Casts => casts;
    public int CastsAllowed => 1 + (ship != null && ship.Hearts <= 1 ? 1 : 0);
    float Speedup => 1f + LostBoost * Mathf.Max(0, def.hearts - (ship != null ? ship.Hearts : def.hearts));

    public BreakerBrain() { Id = "breaker"; }

    public override bool Roams => true;
    public override float TellScale => 1f;
    public override Vector2 JoinFrom => new Vector2(-1f, .8f);
    protected override void OnBind() { dirX = Random.value < .5f ? -1f : 1f; }
    public override void OnJoin() { stage = Stage.Leave; }
    public override void OnReturn() { stage = Stage.Leave; }

    // The top third of the view, under the very top, leaving room for the wave.
    public static void Band(EliteDef def, out float lo, out float hi)
    {
        hi = EliteSystem.ViewTop - def.hullRadius - .5f - Amplitude;
        lo = Mathf.Min(hi, EliteSystem.ViewTop - (EliteSystem.ViewTop - EliteSystem.ViewBottom) / 3f + def.hullRadius + Amplitude);
    }

    // The wave line `along` u from where the run entered.
    public Vector2 PathAt(float along) =>
        new Vector2(start.x + dirX * along, y0 + Amplitude * Mathf.Sin(along * 2f * Mathf.PI / WaveLength));

    // Wake spot k of a cast whose wind-up lasts `tellSeconds`: on the line, behind where the ship will be when it goes out.
    public Vector2 SpotBehind(float tellSeconds, int k)
    {
        float now = (Pos.x - start.x) * dirX;
        return PathAt(now + def.speed * Speedup * tellSeconds - Behind - SpotStep * k);
    }

    void BeginRun()
    {
        Runs++;
        dirX = -dirX;
        float lo, hi;
        Band(def, out lo, out hi);
        y0 = Random.Range(lo, hi);
        float off = TideFlight.OffX(def);
        start = new Vector2(-dirX * off, y0);
        length = 2f * off;
        s = 0f;
        casts = 0;
        ship.Warp(start);
        stage = Stage.Run;
    }

    public override Vector2 Goal(Vector2 seen, float dt)
    {
        Clock += dt;
        switch (stage)
        {
            case Stage.Leave:
                // just up from a dock / back from hiding: out of the view by the nearer side (an attack forced in the meantime is finished first)
                if (ship.State == EliteState.Attack) return Pos;
                if (TideFlight.OffBoard(def, Pos)) { stage = Stage.Away; wait = afterLeave; afterLeave = .4f; }
                return new Vector2(Mathf.Sign(Pos.x == 0f ? 1f : Pos.x) * (TideFlight.OffX(def) + 1f), Pos.y + .3f);
            case Stage.Away:
                wait -= dt;
                if (wait <= 0f) BeginRun();
                return Pos;
            default:
                s += def.speed * Speedup * dt;
                // the carrot is past the far edge: it flies on out and waits there
                if (s >= length) { stage = Stage.Leave; afterLeave = PauseSeconds; }
                return PathAt(s + Lead);
        }
    }

    public override float SpeedScale => stage == Stage.Leave ? LeaveScale : Speedup;

    public override bool WantsAttack(Vector2 seen)
    {
        if (stage != Stage.Run || casts >= CastsAllowed) return false;
        float edge = EliteSystem.RailEdge - .45f;
        if (Mathf.Abs(Pos.x) >= edge) return false;
        // the whole cast (the wind-up, then a bank every shotInterval) must fit before it rides out of the rails
        float exitX = Pos.x + dirX * def.speed * Speedup * (def.tellSeconds + Spots * def.shotInterval);
        return Mathf.Abs(exitX) < EliteSystem.RailEdge - .15f && Mathf.Abs(SpotBehind(def.tellSeconds, 0).x) < edge && Mathf.Abs(SpotBehind(def.tellSeconds, Spots - 1).x) < edge;
    }

    public void CastDone() { casts++; }
}

// Riptide: a foam wake. The tell does NOT stop it (it keeps riding its line): from the first frame BreakerBrain.Spots thin rings mark
// spots on the water behind where it will be when it goes out, along its own line. The action lobs a foam bank out of the lance onto
// each ring, one after another (shotInterval apart); a bank rides the board for hazardSeconds, hurts on touch and takes one player shot
// to pop. (The rings and the foam are all the pilot has to read: stay off them.)
public class FoamWakeAttack : TideSpotAttack
{
    public const float Ring = .5f;
    public FoamWakeAttack() { Id = "foam_wake"; }
    protected override float RingRadius => Ring;
    public override float HazardRadius => Ring * .8f;

    protected override void Plan(Vector2 pilot)
    {
        var bb = ship.Brain as BreakerBrain;
        count = BreakerBrain.Spots;
        for (int k = 0; k < count; k++) spot[k] = bb != null ? bb.SpotBehind(TellSeconds, k) : ship.Position + Vector2.down * (1f + k * .8f);
    }

    protected override void Lay(int i, Vector2 target)
    {
        var foam = Fire(0, Deg(target - (Vector2)ship.MuzzleWorld(0)), 0f, EliteShots.Kind.Orb);
        if (foam != null) foam.Mine(target, def.lobSeconds, def.lobSeconds + Mathf.Max(.5f, def.hazardSeconds), 1, 0f, .05f, 0f, Vector2.zero, 1);
    }

    protected override void Done()
    {
        var bb = ship.Brain as BreakerBrain;
        if (bb != null) bb.CastDone();
    }
}

// ---- Bathyscaphe: deep -----------------------------------------------------------------------------------------------

// A mine layer that surfaces and dives. It RISES to a station at the top (a column, never the one it last used), holds there sliding
// a little, lays CastsPerSurface chains of mines (MineChainAttack; one more at <= 3 hearts), then SINKS off the top of the view, waits
// out of sight (AwaySeconds) and surfaces again at another column. It never comes down to the pilot.
public class DeepBrain : EliteBrain
{
    public enum Stage { Rise, Hold, Sink, Away }
    public const float AwaySeconds = 1.6f, SinkScale = 2f, RiseScale = 1.5f, HoldScale = .75f, ReadyWait = .5f, SettleDistance = .35f, Reposition = 2f, MaxHold = 9f;
    Stage stage = Stage.Rise;
    float wait, held, stationX, phase;
    int casts;
    public Stage Phase => stage;
    public float StationX => stationX;
    public int Casts => casts;
    public int Surfacings { get; private set; }
    public int CastsPerSurface => 1 + (ship != null && ship.Hearts <= 3 ? 1 : 0);
    public float Height => ship.ReachY(EliteSystem.ViewTop - def.topMargin);

    public DeepBrain() { Id = "deep"; }

    public override bool Roams => true;
    public override float TellScale => 1f;
    public override Vector2 JoinFrom => Vector2.up;
    protected override void OnBind() { phase = Random.value * 6.28f; stationX = RandomX(99f); }
    public override void OnJoin() { stage = Stage.Rise; casts = 0; }
    public override void OnReturn() { stage = Stage.Rise; casts = 0; }

    float RandomX(float avoid)
    {
        float edge = TideFlight.Edge(def, .9f);
        float x = Random.Range(-edge, edge);
        for (int tries = 0; tries < 8 && Mathf.Abs(x - avoid) < Reposition; tries++) x = Random.Range(-edge, edge);
        return x;
    }

    public override Vector2 Goal(Vector2 follow, float dt)
    {
        Clock += dt;
        switch (stage)
        {
            case Stage.Rise:
            {
                Vector2 st = new Vector2(stationX, Height);
                if ((Pos - st).sqrMagnitude < SettleDistance * SettleDistance) { stage = Stage.Hold; held = 0f; }
                return st;
            }
            case Stage.Hold:
                held += dt;
                if (held > MaxHold) stage = Stage.Sink;
                return new Vector2(stationX + Mathf.Sin(Clock * .5f + phase) * .45f, Height + Mathf.Sin(Clock * .9f) * .08f);
            case Stage.Sink:
                if (TideFlight.OffBoard(def, Pos)) { stage = Stage.Away; wait = AwaySeconds; }
                return new Vector2(Pos.x, TideFlight.AboveY(def) + 1f);
            default:
                wait -= dt;
                if (wait <= 0f)
                {
                    stationX = RandomX(stationX);
                    casts = 0;
                    Surfacings++;
                    ship.Warp(new Vector2(stationX, TideFlight.AboveY(def) + .3f));
                    stage = Stage.Rise;
                }
                return Pos;
        }
    }

    public override float SpeedScale => stage == Stage.Sink ? SinkScale : stage == Stage.Rise ? RiseScale : HoldScale;
    public override float MinAttackAbove => 1.5f;
    public override bool WantsAttack(Vector2 seen) => stage == Stage.Hold && held >= ReadyWait && casts < CastsPerSurface && Pos.y > seen.y + MinAttackAbove;
    public override float? FaceDeg(Vector2 seen) => stage == Stage.Hold ? -90f : (float?)null;

    // The attack's callback.
    public void CastDone()
    {
        casts++;
        if (casts >= CastsPerSurface) stage = Stage.Sink;
        else held = 0f;
    }
}

// Bathyscaphe: a mine chain. The tell holds it at its station, nose down: a thin ring marks every mine of a CHAIN that runs from just
// under the hull down to the pilot's column (a little ahead of it), the pilot's spot followed for the first aimCommit of the tell and
// then locked (and blinking faster). The action lays the mines one after another, shotInterval apart, out of the claws; each is lobbed
// onto its ring (lobSeconds), rides the board for hazardSeconds more and bursts into a small cross of four bolts (hazardSpeed). A mine
// takes one player shot to defuse. THE FIGHT ESCALATES: shotCount mines, one more for every two hearts lost (MaxSpots - 1 at most).
public class MineChainAttack : TideSpotAttack
{
    public const float Ring = .5f, ArmSeconds = .5f, Drop = 1.2f;
    public MineChainAttack() { Id = "mine_chain"; }
    public override float HazardSeconds => def.hazardSeconds + ArmSeconds;
    public override bool Aimed => true;
    public override bool HoldsDuringTell => true;
    public override bool HoldsDuringAction => true;
    public override float? FaceDeg => -90f;
    protected override float RingRadius => Ring;
    public int HeartsLost => Mathf.Max(0, def.hearts - ship.Hearts);
    public int PlanCount => Mathf.Clamp(Mathf.Max(2, def.shotCount) + HeartsLost / 2, 2, MaxSpots - 1);

    public override bool FriendlyInLine(Vector2 seen) =>
        EliteOnLine(ship.Position, Vector2.down, 8f, .6f);

    protected override void Plan(Vector2 pilot)
    {
        count = PlanCount;
        float edge = EliteSystem.RailEdge - .35f;
        Vector2 from = ship.Position + Vector2.down * (def.hullRadius + .5f);
        Vector2 to = new Vector2(Mathf.Clamp(pilot.x, -edge, edge), Mathf.Max(EliteSystem.ViewBottom + 1.4f, pilot.y + def.lobAhead));
        if (to.y > from.y - Drop) to.y = from.y - Drop;
        for (int i = 0; i < count; i++)
        {
            Vector2 p = Vector2.Lerp(from, to, (i + 1f) / count);
            p.x = Mathf.Clamp(p.x, -edge, edge);
            spot[i] = p;
        }
    }

    protected override void Lay(int i, Vector2 target)
    {
        int m = def.muzzles.Length > 0 ? i % def.muzzles.Length : 0;
        var mine = Fire(m, Deg(target - (Vector2)ship.MuzzleWorld(m)), 0f, EliteShots.Kind.Orb);
        if (mine != null)
            mine.Mine(target, def.lobSeconds, def.lobSeconds + Mathf.Max(.5f, def.hazardSeconds), 4, def.hazardSpeed, ArmSeconds, 45f, Vector2.zero, 1);
    }

    protected override void Done()
    {
        var db = ship.Brain as DeepBrain;
        if (db != null) db.CastDone();
    }
}

// ---- Trawler: trawl --------------------------------------------------------------------------------------------------

// A net hauler that works a FLANK. It enters on one side, takes a station against that rail at the top (bobbing a little up and down),
// drags its nets (DragNetAttack; a second cast at its last heart), then leaves by the side it stands on, waits out of sight
// (AwaySeconds) and comes in on the OPPOSITE flank. It never crosses the board and never comes down to the pilot.
public class TrawlBrain : EliteBrain
{
    public enum Stage { Enter, Flank, Leave, Away }
    public const float AwaySeconds = 1.2f, EnterScale = 1.5f, LeaveScale = 2f, ReadyWait = .6f, SettleDistance = .3f, MaxFlank = 8f;
    Stage stage = Stage.Enter;
    float wait, held;
    int side = 1, casts;
    public Stage Phase => stage;
    public int Side => side;
    public int Casts => casts;
    public int Visits { get; private set; }
    public int CastsPerVisit => 1 + (ship != null && ship.Hearts <= 1 ? 1 : 0);
    public float FlankX => side * Mathf.Max(0f, EliteSystem.RailEdge - def.hullRadius - .25f);
    float FlankY => ship.ReachY(EliteSystem.ViewTop - def.topMargin - .6f) + Mathf.Sin(Clock * .7f) * .45f;

    public TrawlBrain() { Id = "trawl"; }

    public override bool Roams => true;
    public override float TellScale => 1f;
    public override Vector2 JoinFrom => new Vector2(side, .5f);
    protected override void OnBind() { side = Random.value < .5f ? -1 : 1; }
    public override void OnJoin() { stage = Stage.Enter; casts = 0; }
    public override void OnReturn() { stage = Stage.Enter; casts = 0; }

    public override Vector2 Goal(Vector2 follow, float dt)
    {
        Clock += dt;
        switch (stage)
        {
            case Stage.Enter:
            {
                Vector2 st = new Vector2(FlankX, FlankY);
                if ((Pos - st).sqrMagnitude < SettleDistance * SettleDistance) { stage = Stage.Flank; held = 0f; }
                return st;
            }
            case Stage.Flank:
                held += dt;
                if (held > MaxFlank) stage = Stage.Leave;
                return new Vector2(FlankX, FlankY);
            case Stage.Leave:
                if (Mathf.Abs(Pos.x) > TideFlight.OffX(def) - .25f) { stage = Stage.Away; wait = AwaySeconds; }
                return new Vector2(side * (TideFlight.OffX(def) + 1f), Pos.y);
            default:
                wait -= dt;
                if (wait <= 0f)
                {
                    side = -side;
                    casts = 0;
                    Visits++;
                    ship.Warp(new Vector2(side * TideFlight.OffX(def), FlankY));
                    stage = Stage.Enter;
                }
                return Pos;
        }
    }

    public override float SpeedScale => stage == Stage.Leave ? LeaveScale : stage == Stage.Enter ? EnterScale : 1f;
    public override float MinAttackAbove => 1.2f;
    public override bool WantsAttack(Vector2 seen) => stage == Stage.Flank && held >= ReadyWait && casts < CastsPerVisit && Pos.y > seen.y + MinAttackAbove;
    public override float? FaceDeg(Vector2 seen) => stage == Stage.Flank ? -90f : (float?)null;

    // The attack's callback.
    public void CastDone()
    {
        casts++;
        if (casts >= CastsPerVisit) stage = Stage.Leave;
        else held = 0f;
    }
}

// Trawler: a net drag. The tell holds it on its flank, nose down: a thin ring marks every net spot of a ROW across the pilot's height
// (a little ahead of it, followed for the first aimCommit of the tell, then locked), starting at the flank's own rail and running in
// toward the middle, lobSpacing apart. The row is only as long as leaves a gap at least Gap wide, between its far end and the far
// rail, for the pilot to live in. The action lobs the nets out one after another, shotInterval apart, flank first; each lands as a pool
// that rides the board for poolSeconds. (Stand on the open side, or go round the row's end.)
public class DragNetAttack : TideSpotAttack
{
    public const float Ring = .42f, Gap = 1.5f;
    public DragNetAttack() { Id = "drag_net"; }
    public override float HazardSeconds => def.poolSeconds;
    public override bool Aimed => true;
    public override bool HoldsDuringTell => true;
    public override bool HoldsDuringAction => true;
    public override float? FaceDeg => -90f;
    protected override float RingRadius => Ring;
    protected override bool PopsOnDeath => false;   // (nets are pools: they lapse on their own)

    public float Edge => Mathf.Max(.3f, EliteSystem.RailEdge - def.shotSize - .1f);
    public int Side => ship.Position.x >= 0f ? 1 : -1;

    // How many nets the row can hold and still leave the pilot a gap.
    public int NetsFor()
    {
        int want = Mathf.Max(2, def.shotCount) + (ship.Hearts <= 1 ? 1 : 0);
        float room = 2f * Edge - Gap - 2f * Ring;
        int fit = 1 + Mathf.FloorToInt(Mathf.Max(0f, room) / Mathf.Max(.3f, def.lobSpacing));
        return Mathf.Clamp(Mathf.Min(want, fit), 1, MaxSpots);
    }

    protected override void Plan(Vector2 pilot)
    {
        count = NetsFor();
        int sd = Side;
        float y = Mathf.Max(EliteSystem.ViewBottom + 1.2f, Mathf.Min(pilot.y + def.lobAhead, ship.Position.y - 1.2f));
        for (int i = 0; i < count; i++) spot[i] = new Vector2(sd * (Edge - i * def.lobSpacing), y);
    }

    protected override void Lay(int i, Vector2 target)
    {
        int m = def.muzzles.Length > 0 ? i % def.muzzles.Length : 0;
        var net = Fire(m, Deg(target - (Vector2)ship.MuzzleWorld(m)), 0f);
        if (net != null) net.Lob(target, Mathf.Max(.15f, def.lobSeconds));
    }

    protected override void Done()
    {
        var tb = ship.Brain as TrawlBrain;
        if (tb != null) tb.CastDone();
    }
}

// ---- Thunderfin: tempest ---------------------------------------------------------------------------------------------

// A storm cell that loiters. It circles a small, slow ellipse high over the board (about 80 % of the width, a hair of height), pausing
// the circle while it attacks. It never comes down and never follows the pilot's lane. SweepDir is the way it is flying along the
// top (+1 right): the thunder it calls sweeps that way.
public class TempestBrain : EliteBrain
{
    public const float Ry = .12f, Pace = .55f;
    float angle;
    public int SweepDir => Mathf.Sin(angle) > 0f ? -1 : 1;   // (x = cos: it moves left while sin > 0)
    public float Angle => angle;
    public float Rx => Mathf.Max(.4f, TideFlight.Edge(def, .6f) * .8f);
    public float Height => ship.ReachY(EliteSystem.ViewTop - def.topMargin);

    public TempestBrain() { Id = "tempest"; }

    protected override void OnBind() { angle = Random.value * 6.28f; }
    public override Vector2 JoinFrom => Vector2.up;
    public override float MinAttackAbove => 1.5f;
    public override bool WantsAttack(Vector2 seen) => Pos.y > seen.y + MinAttackAbove;
    public override float? FaceDeg(Vector2 seen) => -90f;

    public override Vector2 Goal(Vector2 follow, float dt)
    {
        Clock += dt;
        if (ship.State != EliteState.Attack) angle += def.speed * Pace / Rx * dt;
        return new Vector2(Mathf.Cos(angle) * Rx, Height + Mathf.Sin(angle * 2f) * Ry);
    }
}

// Thunderfin: a thunder sweep. The lure coils pulse and a lane glyph is drawn under EVERY column for the whole tell (>= .7 s): the
// pilot's lane first and the others either side at least a strike's corridor apart (StrikeLanes), shotCount of them (one more at its
// last heart). Then the columns strike ONE AFTER ANOTHER, shotInterval apart, sweeping the way the fish is flying (TempestBrain.SweepDir)
// -- the last glyph is the last column. (AttackStrike: .25 s live each.) Step off the glyph, or wait in the gaps between the columns.
public class ThunderSweepAttack : EliteAttack
{
    readonly AttackStrike[] armed = new AttackStrike[5];
    readonly float[] lanes = new float[5];
    int count;
    public ThunderSweepAttack() { Id = "thunder_sweep"; }
    public override bool HoldsDuringTell => true;
    public override bool HoldsDuringAction => true;
    public override bool Shoots => false;   // (lightning, not shots)
    public int Armed => count;
    public AttackStrike Strike(int i) => armed[i];
    public float Lane(int i) => lanes[i];
    public int Want => Mathf.Clamp(def.shotCount + (ship.Hearts <= 1 ? 1 : 0), 2, armed.Length);

    public override void BeginTell(Vector2 seen)
    {
        base.BeginTell(seen);
        var spec = StrikeSpec.Standard(Mathf.Max(0, def.WorldIndex));
        count = StrikeLanes.Pick(seen.x, Want, Mathf.Max(AttackStrike.MinLaneSpacing, def.lobSpacing), BossRails.DrawnInnerEdge, spec.hitHalf, lanes);
        // the sweep order: along the way it is flying
        var tb = ship.Brain as TempestBrain;
        bool rightward = tb == null || tb.SweepDir > 0;
        for (int i = 1; i < count; i++)
        {
            float x = lanes[i];
            int j = i - 1;
            while (j >= 0 && (rightward ? lanes[j] > x : lanes[j] < x)) { lanes[j + 1] = lanes[j]; j--; }
            lanes[j + 1] = x;
        }
        for (int i = 0; i < count; i++) armed[i] = AttackStrike.Arm(spec, lanes[i], seen.y, TellSeconds + i * Mathf.Max(.1f, def.shotInterval), ship.gameObject);
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
        return (t >= def.actionSeconds && AllLit()) || t >= def.actionSeconds + count * Mathf.Max(.1f, def.shotInterval) + 1.5f;
    }

    public override void End() { for (int i = 0; i < armed.Length; i++) armed[i] = null; count = 0; }
}

// ---- Pearl Warden: oyster --------------------------------------------------------------------------------------------

// A shell that keeps one of three stations at the top (left, middle, right), sliding a few hairs, and never comes down to the pilot.
// After every cast it hops to ANOTHER station. It only attacks settled on its station.
public class OysterBrain : EliteBrain
{
    public const float ReadyWait = .7f, SettleDistance = .3f;
    public static readonly float[] Columns = { -.65f, 0f, .65f };
    int station = 1;
    float settled;
    public int Station => station;
    public int Hops { get; private set; }
    public bool Settled => settled > 0f;
    public float Height => ship.ReachY(EliteSystem.ViewTop - def.topMargin);

    public OysterBrain() { Id = "oyster"; }

    protected override void OnBind() { station = Random.Range(0, Columns.Length); }
    public override Vector2 JoinFrom => Vector2.up;
    public override float SpeedScale => .8f;
    public override float MinAttackAbove => 1.5f;
    public override bool WantsAttack(Vector2 seen) => settled >= ReadyWait && Pos.y > seen.y + MinAttackAbove;
    public override float? FaceDeg(Vector2 seen) => -90f;

    public Vector2 StationPos(int i) => new Vector2(Columns[Mathf.Clamp(i, 0, Columns.Length - 1)] * TideFlight.Edge(def, .6f), Height);

    public override Vector2 Goal(Vector2 follow, float dt)
    {
        Clock += dt;
        Vector2 st = StationPos(station);
        if ((Pos - st).sqrMagnitude < SettleDistance * SettleDistance) settled += dt; else settled = 0f;
        return st + new Vector2(Mathf.Sin(Clock * .45f) * .2f, Mathf.Sin(Clock * .8f) * .07f);
    }

    // The attack's callback: on to another station.
    public void CastDone()
    {
        station = (station + 1 + Random.Range(0, Columns.Length - 1)) % Columns.Length;
        Hops++;
        settled = 0f;
    }
}

// Pearl Warden: a pearl spiral. The tell holds it on its station, nose down: two thin lanes mark the edges of the ARC the spiral will
// sweep (ArcDeg either side of straight down). The pilot's spot is followed for the first aimCommit of the tell and then locked: that
// direction is left OUT of the spiral (GapDeg either side) -- the pilot who stood still at the lock is in the gap. The action sweeps
// shotCount pearls (more for every two hearts lost) from one edge of the arc to the other, shotInterval apart, out of the two shells in
// turn; the sweep runs the other way each cast.
public class PearlSpiralAttack : EliteAttack
{
    public const float ArcDeg = 58f, GapDeg = 13f, LaneLength = 5f;
    public const int MaxPearls = 13;
    readonly EliteTelegraph tele = new EliteTelegraph();
    readonly float[] angle = new float[MaxPearls];
    int total, fired, sweep = 1;
    float next, gapDeg;
    bool acted;
    public PearlSpiralAttack() { Id = "pearl_spiral"; }
    public override bool Aimed => true;
    public override bool HoldsDuringTell => true;
    public override bool HoldsDuringAction => true;
    public override float? FaceDeg => -90f;

    public EliteTelegraph Telegraph => tele;
    public int Total => total;
    public int Flung => fired;
    public int Sweep => sweep;
    public float GapAngle => gapDeg;
    public float Angle(int i) => angle[Mathf.Clamp(i, 0, MaxPearls - 1)];
    public int HeartsLost => Mathf.Max(0, def.hearts - ship.Hearts);
    public int PearlCount => Mathf.Clamp(Mathf.Max(5, def.shotCount) + (HeartsLost / 2) * 2, 5, MaxPearls);

    // The angle of pearl i of n in a sweep that runs `way` (+1 counter-clockwise, -1 clockwise).
    public static float AngleOf(int i, int n, int way) => -90f + way * (-ArcDeg + 2f * ArcDeg * i / Mathf.Max(1, n - 1));

    public override void BeginTell(Vector2 seen)
    {
        base.BeginTell(seen);
        acted = false;
        sweep = -sweep;
        tele.Clear();
        Vector2 o = ship.Position;
        float half = def.shotSize * .5f + .04f;
        tele.Lane(o, o + Unit(-90f - ArcDeg) * LaneLength, half);
        tele.Lane(o, o + Unit(-90f + ArcDeg) * LaneLength, half);
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
        gapDeg = Deg(ship.AimSeen - ship.Position);
        int n = PearlCount;
        total = 0;
        for (int i = 0; i < n; i++)
        {
            float a = AngleOf(i, n, sweep);
            if (Mathf.Abs(Mathf.DeltaAngle(a, gapDeg)) < GapDeg) continue;
            angle[total++] = a;
        }
        fired = 0;
        next = 0f;
    }

    public override bool StepAction(float dt)
    {
        t += dt;
        while (t >= next && fired < total)
        {
            int m = def.muzzles.Length > 0 ? fired % def.muzzles.Length : 0;
            Fire(m, angle[fired], def.shotSpeed);
            fired++;
            next += def.shotInterval;
        }
        return fired >= total && t >= def.actionSeconds;
    }

    public override void End()
    {
        tele.End();
        if (acted)
        {
            var ob = ship.Brain as OysterBrain;
            if (ob != null) ob.CastDone();
        }
        acted = false;
    }
}

// ---- the first generation (kept for their ids; no Tide elite flies them now) ----

// ---- brains --------------------------------------------------------------------------------------------------------

// Riptide: a lean interceptor, a touch faster than Sunstoke.
public class SurferBrain : InterceptorBrain
{
    public SurferBrain() { Id = "surfer"; }
    public override float SpeedScale => 1.1f;
}

// Bathyscaphe: a siege platform that wallows slowly side to side.
public class AbyssBrain : SiegeBrain
{
    public AbyssBrain() { Id = "abyss"; }
    public override Vector2 Goal(Vector2 seen, float dt)
    {
        Vector2 g = base.Goal(seen, dt);
        g.x += Mathf.Sin(Clock * .5f) * .5f;
        return g;
    }
    public override float SpeedScale => .8f;
}

// Trawler: a hauler that blocks the lane, slow and wide.
public class NetterBrain : HaulerBrain
{
    public NetterBrain() { Id = "netter"; }
    public override float SpeedScale => .75f;
}

// Thunderfin: a skirmisher that keeps its range and blinks clear.
public class StormrayBrain : SkirmisherBrain
{
    public StormrayBrain() { Id = "stormray"; }
}

// Pearl Warden: a bastion platform that breathes sideways.
public class PearlerBrain : BastionBrain
{
    public PearlerBrain() { Id = "pearler"; }
    public override Vector2 Goal(Vector2 seen, float dt)
    {
        Vector2 g = base.Goal(seen, dt);
        g.x += Mathf.Sin(Clock * .45f) * .4f;
        return g;
    }
    public override float SpeedScale => .7f;
}

// ---- attacks -------------------------------------------------------------------------------------------------------

// Riptide: the lance dash with its lane drawn. It re-aims through the first half of the tell, then locks and the dotted lane of the
// ram is shown for the rest of it; the dash is committed (bait it), spout needles leave the lance tip as it brakes.
public class SurfDashAttack : LanceDashAttack
{
    readonly EliteTelegraph telegraph = new EliteTelegraph();
    bool shown;
    public EliteTelegraph Telegraph => telegraph;
    public bool Shown => shown;
    public SurfDashAttack() { Id = "surf_dash"; }

    public override void BeginTell(Vector2 seen)
    {
        base.BeginTell(seen);
        shown = false;
        telegraph.Clear();
    }

    public override void StepTell(float dt)
    {
        base.StepTell(dt);
        if (!shown && t >= TellSeconds * def.aimCommit)
        {
            shown = true;
            Vector2 from = ship.Position;
            float reach = def.dashSpeed * def.actionSeconds * .9f;
            telegraph.Clear();
            telegraph.Lane(from, from + dir * reach, def.hullRadius + .12f);
            telegraph.Show(Mathf.Max(.1f, TellSeconds - t), def.ShotColor);
        }
        telegraph.Step(dt);
    }

    public override void Cancel() { telegraph.End(); shown = false; }
    public override void OnDeath() { telegraph.End(); }
    public override void BeginAction() { base.BeginAction(); telegraph.GoLive(); }
    public override void End() { telegraph.End(); }
}

// Bathyscaphe: depth charges. The burst points (a little above the pilot, either side of its column) and the lane each charge flies
// are drawn from the first frame of the tell. Then shotCount fused orbs leave the claws, one after another, each flying to its own
// point and bursting there (hazardSeconds) into a ring of hazardCount pearls at hazardSpeed. Shoot a charge and it only pops.
public class DepthChargeAttack : EliteAttack
{
    public const float BurstRing = .55f;
    readonly EliteTelegraph telegraph = new EliteTelegraph();
    Vector2[] burst = new Vector2[4];
    float next;
    int sent;
    public DepthChargeAttack() { Id = "depth_charge"; }
    public override bool HoldsDuringTell => true;
    public int Sent => sent;
    public EliteTelegraph Telegraph => telegraph;
    public Vector2 Burst(int i) => burst[Mathf.Clamp(i, 0, burst.Length - 1)];
    int Count => Mathf.Clamp(def.shotCount, 1, burst.Length);
    int Claw(int i) => def.muzzles.Length >= 2 ? i % 2 : 0;

    public override bool FriendlyInLine(Vector2 seen)
    {
        Vector2 to = seen - ship.Position;
        float dist = to.magnitude;
        return dist > 1e-3f && EliteOnLine(ship.Position, to / dist, dist + 1f, Count * def.lobSpacing * .5f);
    }

    public override void BeginTell(Vector2 seen)
    {
        base.BeginTell(seen);
        telegraph.Clear();
        float edge = Mathf.Max(.2f, EliteSystem.RailEdge - .35f);
        for (int i = 0; i < Count; i++)
        {
            float x = seen.x + (i - (Count - 1) * .5f) * def.lobSpacing;
            burst[i] = new Vector2(Mathf.Clamp(x, -edge, edge), seen.y + def.lobAhead);
            telegraph.Lane(ship.MuzzleWorld(Claw(i)), burst[i], def.shotSize * .5f + .06f);
            telegraph.Ring(burst[i], BurstRing);
        }
        telegraph.Show(TellSeconds, def.ShotColor);
    }

    public override void StepTell(float dt) { t += dt; telegraph.Step(dt); }
    public override void Cancel() { telegraph.End(); }
    public override void OnDeath() { telegraph.End(); }

    public override void BeginAction()
    {
        base.BeginAction();
        telegraph.GoLive();
        next = 0f;
        sent = 0;
    }

    public override bool StepAction(float dt)
    {
        t += dt;
        while (t >= next && sent < Count)
        {
            int m = Claw(sent);
            Vector2 d = burst[sent] - (Vector2)ship.MuzzleWorld(m);
            float seconds = Mathf.Max(.2f, def.hazardSeconds);
            var orb = Fire(m, Deg(d), d.magnitude / seconds, EliteShots.Kind.Orb);
            if (orb != null) orb.Fuse(seconds);
            sent++;
            next += def.shotInterval;
        }
        return t >= def.actionSeconds && sent >= Count;
    }

    public override void End() { telegraph.End(); }
}

// Trawler: a net cast. A row of ringed spots across the pilot's height (centred on it) is drawn from the first frame of the tell; the
// spot beside the pilot on the side toward the board's middle is left out: the hole in the net. Then the booms lob a net onto every
// other spot (each lands as a pool riding the board for poolSeconds).
public class NetCastAttack : EliteAttack
{
    public const float SpotRing = .42f;
    readonly EliteTelegraph telegraph = new EliteTelegraph();
    Vector2 row, liveRow;
    int gap, cast;
    float next;
    public NetCastAttack() { Id = "net_cast"; }
    public override bool HoldsDuringTell => true;
    public int Count => Mathf.Max(3, def.shotCount);
    public int Centre => Count / 2;
    public int Gap => gap;
    public int Cast => cast;
    public Vector2 Row => row;
    public EliteTelegraph Telegraph => telegraph;

    public Vector2 Spot(int i) => SpotAt(row, i);

    Vector2 SpotAt(Vector2 centre, int i)
    {
        float x = centre.x + (i - (Count - 1) * .5f) * def.lobSpacing;
        float edge = Mathf.Max(.3f, EliteSystem.RailEdge - def.shotSize - .1f);
        return new Vector2(Mathf.Clamp(x, -edge, edge), centre.y);
    }

    public override bool FriendlyInLine(Vector2 seen)
    {
        Vector2 to = seen - ship.Position;
        float dist = to.magnitude;
        return dist > 1e-3f && EliteOnLine(ship.Position, to / dist, dist + 1f, Count * def.lobSpacing * .5f);
    }

    public override void BeginTell(Vector2 seen)
    {
        base.BeginTell(seen);
        row = seen + new Vector2(((Count - 1) * .5f - Centre) * def.lobSpacing, def.lobAhead);
        gap = Centre + (seen.x > 0f ? -1 : 1);
        telegraph.Clear();
        for (int i = 0; i < Count; i++) if (i != gap) telegraph.Ring(Spot(i), SpotRing);
        telegraph.Show(TellSeconds, def.ShotColor);
    }

    public override void StepTell(float dt) { t += dt; telegraph.Step(dt); }
    public override void Cancel() { telegraph.End(); }
    public override void OnDeath() { telegraph.End(); }

    public override void BeginAction()
    {
        base.BeginAction();
        telegraph.GoLive();
        next = 0f;
        cast = 0;
        liveRow = row + Vector2.up * (EliteSystem.Scroll * def.lobSeconds);
    }

    public override bool StepAction(float dt)
    {
        t += dt;
        liveRow.y -= EliteSystem.Scroll * dt;
        while (t >= next && cast < Count - 1)
        {
            int slot = cast >= gap ? cast + 1 : cast;   // (the k-th cast goes to spot k, skipping the gap)
            int m = def.muzzles.Length > 0 ? cast % def.muzzles.Length : 0;
            Vector2 to = SpotAt(liveRow, slot);
            var shot = Fire(m, Deg(to - (Vector2)ship.MuzzleWorld(m)), 0f);
            if (shot != null) shot.Lob(to, Mathf.Max(.15f, def.lobSeconds - next));
            cast++;
            next += def.shotInterval;
        }
        return t >= def.actionSeconds && cast >= Count - 1;
    }

    public override void End() { telegraph.End(); }
}

// Thunderfin: thunder strike. The lure coils pulse and a lane glyph is drawn under every column for the whole tell (>= .7 s), then
// shotCount lightning columns hit at once (AttackStrike: .25 s live), on the pilot's lane and the lanes beside it. Step off the glyph.
public class ThunderStrikeAttack : EliteAttack
{
    readonly AttackStrike[] armed = new AttackStrike[4];
    readonly float[] lanes = new float[4];
    int count;
    public ThunderStrikeAttack() { Id = "thunder_strike"; }
    public override bool HoldsDuringTell => true;
    public override bool Shoots => false;   // (lightning, not shots)
    public int Armed => count;
    public AttackStrike Strike(int i) => armed[i];

    public override void BeginTell(Vector2 seen)
    {
        base.BeginTell(seen);
        var spec = StrikeSpec.Standard(Mathf.Max(0, def.WorldIndex));
        count = StrikeLanes.Pick(seen.x, Mathf.Clamp(def.shotCount, 1, armed.Length), Mathf.Max(AttackStrike.MinLaneSpacing, def.lobSpacing),
                                 BossRails.DrawnInnerEdge, spec.hitHalf, lanes);
        for (int i = 0; i < count; i++) armed[i] = AttackStrike.Arm(spec, lanes[i], seen.y, TellSeconds, ship.gameObject);
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
        for (int i = 0; i < count; i++) if (armed[i] != null) armed[i].Ignite();
        if (count > 0)
        {
            Fired++;
            ship.OnFired(0, -90f);
        }
    }

    public override bool StepAction(float dt)
    {
        t += dt;
        return t >= def.actionSeconds;
    }

    public override void End() { for (int i = 0; i < armed.Length; i++) armed[i] = null; count = 0; }
}

// Pearl Warden: the ward curtain in pearls: a row of slow pearls across the pilot's height with
// the one spot beside the pilot left out.
public class PearlCurtainAttack : WardCurtainAttack
{
    public PearlCurtainAttack() { Id = "pearl_curtain"; }
}
