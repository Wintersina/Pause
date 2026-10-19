using UnityEngine;

// Frost's five elites as mini-boss fights (Codex's flight strips, Art/Enemies/Elite/Frost/), each its own brain and attack. The rules
// they share: none of them follows the pilot -- they keep their distance and antagonise; each throws HAZARDS in Frost's own material
// (ice, cold: shards, slabs, icicles, frost mines, a cold fog; the cue colours are Frost's, never pink); every attack is telegraphed
// and its aim LOCKS at the tell. They launch from Frost's ground sites (hangar, rigbay, padring, crawlerbay, hatch), and the backdrop
// brings one of the kind into view when none is (FrostDirector.BringHome).
//
//   Rimebreaker       icebreaker / ice_run        (hangar)     pass-through RUNS: an ice-crack lane is drawn down the board from the
//                                                   top, then it ploughs the whole way through (rocks break, no heart lost) and out
//                                                   the bottom; it re-enters on another lane after a pause. After a hit: pairs of runs
//   Floe Harrower     barge / floe_barge          (crawlerbay) holds high on the far side of the pilot, sliding along the top; floe_cast
//                                                   (a slab row with a gap + a lance down it) and, between casts, single slabs that
//                                                   skid diagonally and bounce off one rail
//   Cryo Siren        hopper / frost_bloom        (rigbay)     a long-range kiter that HOPS between perches (out of the view and back,
//                                                   a blink trail marking the next one); orb -> shard ring, a beam every third; two
//                                                   orbs a bloom at its last heart
//   Glacier Tender    carrier / tender_rig        (hatch)      hangs near the top; tethered ice drones (shield while two live) that also
//                                                   drop frost mines on the pilot's lane; a cold fog cone when its shield is down
//   Whiteout Sentinel patrol / icicle_barrage     (padring)    patrols straight legs across the TOP; ice plates soak hits and spray
//                                                   shards; stripped, it calls down an ICICLE BARRAGE down marked lanes with one safe one
//
// The Rimebreaker and the Siren ROAM (EliteBrain.Roams): their paths leave the view and they are untouchable while outside the board.
// See docs/enemy-behaviours.md ("Frost elites") for the design.

// ---- shared bits ---------------------------------------------------------------------------------------------------------------

public static class FrostFlight
{
    // World y at which an elite is wholly above the view / wholly below it.
    public static float AboveY(EliteDef def) => EliteSystem.ViewTop + def.cellWorldSize * .7f;
    public static float BelowY(EliteDef def) => EliteSystem.ViewBottom - def.cellWorldSize * .8f;
    // The x range a ship's centre may keep between the rails.
    public static float Edge(EliteDef def, float margin) => Mathf.Max(0f, EliteSystem.RailEdge - def.hullRadius - margin);
}

// ---- brains --------------------------------------------------------------------------------------------------------------------

// Rimebreaker: an icebreaker making RUNS. One run is: wait off the top, appear at the top on a lane (the lane is drawn on the board
// as an ice crack from the first frame of the tell), plough straight down the whole board and out of the bottom (IceRunAttack);
// then wait PauseSeconds and come back on a DIFFERENT lane (at least MinLaneGap from the last, the lanes of a pair on opposite sides).
// After it has lost a heart (and hidden, and come back) the runs come in PAIRS: the second follows PairGap after the first.
// It never prowls, never comes down to the pilot and never stops on the board between runs.
public class IcebreakerBrain : EliteBrain
{
    public enum Stage { Leave, Away, Enter, Ready, Run }
    public const float PauseSeconds = 1.8f, PairGap = .5f, EnterScale = 2.4f, ReadyWait = .1f, SettleDistance = .15f;
    public const float MinLaneGap = 1.6f, NearPilot = 2.6f;   // a lane is within NearPilot of where it sees the pilot, when it can be
    Stage stage = Stage.Leave;
    float wait, ready, lane, lastLane;
    int pairLeft;
    public Stage Phase => stage;
    public float Lane => lane;
    public float LastLane => lastLane;
    public int Runs { get; private set; }
    public int RunsStarted { get; private set; }
    public int PairSize => ship != null && ship.Hearts < def.hearts ? 2 : 1;
    public int PairLeft => pairLeft;
    public float WaitLeft => wait;

    public IcebreakerBrain() { Id = "icebreaker"; }

    public override bool Roams => true;
    public override float TellScale => 1f;
    public override Vector2 JoinFrom => Vector2.up;

    public override void OnJoin() { stage = Stage.Leave; pairLeft = 0; }
    public override void OnReturn() { pairLeft = 0; BeginEnter(); }

    // A lane between the rails, not the last one's, and (a pair) on the other side of the board from it.
    public float PickLane(Vector2 seen, bool second)
    {
        float edge = FrostFlight.Edge(def, .5f);
        for (int tries = 0; tries < 8; tries++)
        {
            float x = Random.Range(-edge, edge);
            if (Mathf.Abs(x - lastLane) < MinLaneGap) continue;
            if (second && Mathf.Abs(lastLane) > .5f && Mathf.Sign(x) == Mathf.Sign(lastLane)) continue;
            if (tries < 5 && Mathf.Abs(x - seen.x) > NearPilot) continue;
            return x;
        }
        // (a tight board: the far side of the middle from the last lane)
        return Mathf.Clamp(-Mathf.Sign(lastLane == 0f ? 1f : lastLane) * edge * .7f, -edge, edge);
    }

    void BeginEnter()
    {
        if (pairLeft <= 0) pairLeft = PairSize;
        bool second = pairLeft < PairSize;
        lane = PickLane(ship.Follow, second);
        ship.Warp(new Vector2(lane, FrostFlight.AboveY(def)));
        stage = Stage.Enter;
        ready = 0f;
    }

    public Vector2 ReadyPoint => new Vector2(lane, EliteSystem.ViewTop - def.hullRadius - .35f);

    public override Vector2 Goal(Vector2 seen, float dt)
    {
        Clock += dt;
        switch (stage)
        {
            case Stage.Leave:
                // just lifted off a dock: up and out of the view, then the first run's lane
                if (Pos.y > FrostFlight.AboveY(def) - .2f) { stage = Stage.Away; wait = .5f; }
                return new Vector2(Pos.x, FrostFlight.AboveY(def) + .6f);
            case Stage.Away:
                wait -= dt;
                if (wait <= 0f) BeginEnter();
                return Pos;
            case Stage.Enter:
                if ((Pos - ReadyPoint).sqrMagnitude < SettleDistance * SettleDistance) { stage = Stage.Ready; ready = 0f; }
                return ReadyPoint;
            case Stage.Ready:
                ready += dt;
                return ReadyPoint;
            default:
                return Pos;
        }
    }

    public override float SpeedScale => stage == Stage.Enter || stage == Stage.Leave ? EnterScale : 1f;
    public override bool WantsAttack(Vector2 seen) => stage == Stage.Ready && ready >= ReadyWait;
    // prow first: nose down through the whole run, and at the top waiting for it
    public override float? FaceDeg(Vector2 seen) => stage == Stage.Leave ? (float?)null : -90f;

    // The attack's callbacks.
    public void RunStarted() { stage = Stage.Run; RunsStarted++; }

    public void RunDone()
    {
        Runs++;
        lastLane = lane;
        pairLeft--;
        stage = Stage.Away;
        wait = pairLeft > 0 ? PairGap : PauseSeconds;
    }
}

// Floe Harrower: an ice barge on the top rail. It holds high (topMargin under the top, never above the player's reach) on the side of the
// board AWAY from the pilot and slides a slow sine along the top, always LaneKeep clear of the pilot's lane and never coming down to it.
// It crosses to the other side (high, at CrossScale, not attacking) only when the pilot takes its side by more than FlipBand.
public class BargeBrain : EliteBrain
{
    public const float LaneKeep = 1.2f, SlideRate = .5f, SlideAmp = .3f, CrossScale = 1.7f, FlipBand = .3f;
    float side = 1f;
    bool crossing;
    public float Side => side;
    public bool Crossing => crossing;
    public int Crossings { get; private set; }
    public float Height => ship.ReachY(EliteSystem.ViewTop - def.topMargin);

    public BargeBrain() { Id = "barge"; }

    protected override void OnBind() { side = Random.value < .5f ? -1f : 1f; }
    public override void OnJoin() { var p = EliteSystem.Player; side = Pos.x >= (p != null ? p.position.x : 0f) ? 1f : -1f; crossing = false; }
    public override Vector2 JoinFrom => Vector2.up;

    public override Vector2 Goal(Vector2 seen, float dt)
    {
        Clock += dt;
        float edge = FrostFlight.Edge(def, .35f);
        // the pilot has taken its side: over to the other
        if (!crossing && Mathf.Abs(seen.x) > FlipBand && Mathf.Sign(seen.x) == side) { side = -side; crossing = true; Crossings++; }
        float x = side * Mathf.Min(def.laneOffset, edge) + Mathf.Sin(Clock * SlideRate) * SlideAmp;
        x = side > 0f ? Mathf.Max(x, seen.x + LaneKeep) : Mathf.Min(x, seen.x - LaneKeep);
        x = Mathf.Clamp(x, -edge, edge);
        if (crossing && Mathf.Abs(Pos.x - x) < .3f) crossing = false;
        return new Vector2(x, Height + Mathf.Sin(Clock * .8f) * .08f);
    }

    public override float SpeedScale => crossing ? CrossScale : .85f;
    public override float MinAttackAbove => 1.8f;
    public override bool WantsAttack(Vector2 seen) => !crossing && Pos.y > seen.y + MinAttackAbove && Mathf.Abs(Pos.x - seen.x) > LaneKeep * .8f;
}

// Cryo Siren: a long-range kiter that HOPS. A hop is: fly off the view by a side or the top, wait AwaySeconds out of sight while a
// BLINK TRAIL of rings is drawn into the next perch (where it will be), come back in from a side or the top and settle there. It
// perches on one of Perches fixed columns (never the same twice running) Standoff above the pilot (keepDistance, inside the player's
// reach), takes ONE bloom and hops again; it also hops when the pilot comes within MinRange or after PerchMax seconds. It never closes.
public class HopperBrain : EliteBrain
{
    public enum Stage { Enter, Perch, Exit, Away }
    public const float MinRange = 3.3f, PerchWait = .5f, PerchMax = 5f, AwaySeconds = .8f, EnterScale = 2f, ExitScale = 2.2f, SettleDistance = .35f;
    public const float TrailRing = .26f;
    public const int TrailRings = 4;
    static readonly float[] Columns = { -.78f, -.3f, .3f, .78f };
    Stage stage = Stage.Enter;
    int perch = -1, lastPerch = -1, entry;       // entry: 0 left side, 1 right side, 2 the top
    float perched, away;
    EliteMarks marks;
    public Stage Phase => stage;
    public int Perch => perch;
    public int Hops { get; private set; }
    public bool PeeledOff { get; private set; }
    public EliteMarks Marks => marks;
    public int ColumnCount => Columns.Length;

    public HopperBrain() { Id = "hopper"; }

    public override bool Roams => true;
    public override float TellScale => 1f;
    public override Vector2 JoinFrom => new Vector2(.5f, 1f);
    protected override void OnBind() { marks = new EliteMarks(ship, TrailRings); }

    public override void OnJoin() { perch = Nearest(Pos.x); stage = Stage.Enter; perched = 0f; }
    public override void OnReturn() { marks.HideAll(); ChooseNext(); stage = Stage.Enter; perched = 0f; }

    int Nearest(float x)
    {
        float edge = FrostFlight.Edge(def, .5f);
        int best = 0;
        for (int i = 1; i < Columns.Length; i++) if (Mathf.Abs(Columns[i] * edge - x) < Mathf.Abs(Columns[best] * edge - x)) best = i;
        return best;
    }

    // Where perch `i` is now: its column, Standoff above the pilot (kept inside the player's reach and the view).
    public Vector2 Station(int i, Vector2 f)
    {
        float edge = FrostFlight.Edge(def, .5f);
        float y = Mathf.Min(f.y + def.keepDistance, EliteSystem.ViewTop - def.hullRadius - 1f);
        return new Vector2(Columns[Mathf.Clamp(i, 0, Columns.Length - 1)] * edge, ship.ReachY(y));
    }

    // Where it comes in from for the perch it is going to (off the view).
    Vector2 EntryPoint(int i, Vector2 f)
    {
        Vector2 st = Station(i, f);
        if (entry == 2) return new Vector2(st.x, FrostFlight.AboveY(def));
        return new Vector2((entry == 0 ? -1f : 1f) * SpaceFlight.OffX(def), st.y + .4f);
    }

    // The next perch: not this one, not under the pilot's nose; and the edge it comes in by.
    void ChooseNext()
    {
        var f = ship != null ? ship.Follow : Vector2.zero;
        float edge = FrostFlight.Edge(def, .5f);
        int pick = -1;
        for (int tries = 0; tries < 8 && pick < 0; tries++)
        {
            int c = Random.Range(0, Columns.Length);
            if (c == perch || c == lastPerch && tries < 6) continue;
            if (Mathf.Abs(Columns[c] * edge - f.x) < 1f && tries < 6) continue;
            pick = c;
        }
        if (pick < 0) pick = (perch + 1) % Columns.Length;
        lastPerch = perch;
        perch = pick;
        // by the nearer side of the new perch, or over the top
        entry = Random.value < .4f ? 2 : (Columns[perch] < 0f ? 0 : 1);
    }

    public override Vector2 Goal(Vector2 follow, float dt)
    {
        Clock += dt;
        marks.Step(dt);
        float off = SpaceFlight.OffX(def);
        switch (stage)
        {
            case Stage.Enter:
            {
                Vector2 st = Station(perch, follow);
                if ((Pos - st).sqrMagnitude < SettleDistance * SettleDistance) { stage = Stage.Perch; perched = 0f; PeeledOff = false; }
                return st;
            }
            case Stage.Perch:
            {
                perched += dt;
                if ((Pos - ship.Seen).magnitude < MinRange) { PeeledOff = true; stage = Stage.Exit; }
                else if (perched > PerchMax) stage = Stage.Exit;
                return Station(perch, follow) + new Vector2(Mathf.Sin(Clock * 1.7f) * .12f, Mathf.Sin(Clock * 1.1f) * .1f);
            }
            case Stage.Exit:
            {
                // off by the side it is nearer to, or over the top when it is near the middle
                bool top = Mathf.Abs(Pos.x) < 1f;
                if (top ? Pos.y > FrostFlight.AboveY(def) - .2f : Mathf.Abs(Pos.x) > off - .25f)
                {
                    Hops++;
                    ChooseNext();
                    stage = Stage.Away;
                    away = 0f;
                }
                return top ? new Vector2(Pos.x, FrostFlight.AboveY(def) + .8f) : new Vector2(Mathf.Sign(Pos.x) * (off + 1f), Pos.y + .6f);
            }
            default:
            {
                away += dt;
                // the blink trail: rings along the way in, ending on the perch itself
                Vector2 st = Station(perch, follow), from = EntryPoint(perch, follow);
                Vector2 d = (from - st).normalized;
                for (int i = 0; i < TrailRings; i++)
                    marks.Set(i, st + d * (i * .9f), i == 0 ? TrailRing * 1.6f : TrailRing, def.ShotColor, away > AwaySeconds * .55f, 1f - i * .18f);
                if (away >= AwaySeconds)
                {
                    marks.HideAll();
                    ship.Warp(from);
                    stage = Stage.Enter;
                }
                return Pos;
            }
        }
    }

    public override float SpeedScale => stage == Stage.Enter ? EnterScale : stage == Stage.Exit ? ExitScale : 1f;
    public override float MinAttackAbove => 1.2f;

    public override bool WantsAttack(Vector2 seen) =>
        stage == Stage.Perch && perched > PerchWait && Pos.y > seen.y + MinAttackAbove && (seen - Pos).magnitude >= MinRange * 1.05f;

    // The bloom is done: it hops.
    public void BloomDone() { if (stage == Stage.Perch) stage = Stage.Exit; }
}

// Glacier Tender: a slow support tug. Hangs topMargin under the top of the view (never above the player's reach), sliding a slow sine
// either side of the middle -- it does not follow the pilot. When the pilot comes within keepDistance it scurries off sideways, fast,
// until the pilot is 1.4x that away again. Attacks (releases drones, or vents fog) while it has room for more drones.
public class CarrierBrain : EliteBrain
{
    public const float FleeScale = 1.7f, Release = 1.4f, SlideRate = .35f;
    bool fleeing, init;
    float trackX, fleeSide = 1f, phase;
    public bool Fleeing => fleeing;
    public int Flights { get; private set; }
    public float Amplitude => Mathf.Max(0f, Mathf.Min(def.laneOffset, FrostFlight.Edge(def, .4f)));
    public float Height => ship.ReachY(EliteSystem.ViewTop - def.topMargin);

    public CarrierBrain() { Id = "carrier"; }

    protected override void OnBind() { phase = Random.value * Mathf.PI * 2f; }

    public override Vector2 Goal(Vector2 seen, float dt)
    {
        Clock += dt;
        float edge = FrostFlight.Edge(def, .4f);
        if (!init) { trackX = Pos.x; init = true; }
        float dist = (Pos - seen).magnitude;
        bool was = fleeing;
        fleeing = dist < def.keepDistance * (was ? Release : 1f);
        if (fleeing && !was)
        {
            Flights++;
            // away from the pilot, mostly sideways (the top of the view is close); a rail that way: the other
            fleeSide = Pos.x >= seen.x ? 1f : -1f;
            if (edge - fleeSide * Pos.x < .8f) fleeSide = -fleeSide;
        }
        if (fleeing)
        {
            trackX = Mathf.Clamp(Pos.x + fleeSide * 1.6f, -edge, edge);
            return new Vector2(trackX, Mathf.Max(Pos.y, seen.y + def.keepDistance) + .4f);
        }
        trackX = Mathf.MoveTowards(trackX, Mathf.Sin(Clock * SlideRate + phase) * Amplitude, def.speed * .35f * dt);
        return new Vector2(trackX, Height + Mathf.Sin(Clock * .9f) * .1f);
    }

    public override float SpeedScale => fleeing ? FleeScale : .7f;
    public override float MinAttackAbove => 1.8f;

    public override bool WantsAttack(Vector2 seen)
    {
        var dd = ship.Attack as DroneDeployAttack;
        return !fleeing && Pos.y > seen.y + MinAttackAbove && (dd == null || dd.Live < dd.Cap);
    }

    public override Vector2 JoinFrom => Vector2.up;
}

// Whiteout Sentinel: a slow ironclad PATROLLING the top. Flies straight legs from one side of the board to the other along the top
// (topMargin under it, never above the player's reach), a beat's rest at each end, at a steady slow pace, never dodging anything
// (Steadfast), its prow (and its plates) turned on the pilot. It does not come down to the pilot or toward its lane.
public class PatrolBrain : EliteBrain
{
    public const float Pace = .6f, Rest = .6f, EndMargin = .5f;
    float side = 1f, rest;
    public float Side => side;
    public int Legs { get; private set; }
    public float Height => ship.ReachY(EliteSystem.ViewTop - def.topMargin);
    public float EndX => side * FrostFlight.Edge(def, EndMargin);

    public PatrolBrain() { Id = "patrol"; }

    protected override void OnBind() { side = Random.value < .5f ? -1f : 1f; }
    public override void OnJoin() { rest = 0f; side = Pos.x >= 0f ? -1f : 1f; }
    public override Vector2 JoinFrom => Vector2.up;

    public override Vector2 Goal(Vector2 seen, float dt)
    {
        Clock += dt;
        if (rest > 0f) { rest -= dt; return new Vector2(Pos.x, Height); }
        float endX = EndX;
        if (Mathf.Abs(Pos.x - endX) < .15f) { side = -side; Legs++; rest = Rest; return new Vector2(Pos.x, Height); }
        return new Vector2(endX, Height);
    }

    public override bool Steadfast => true;
    public override float SpeedScale => Pace;
    public override float MinAttackAbove => 1.5f;
    public override bool WantsAttack(Vector2 seen) => Pos.y > seen.y + MinAttackAbove;
    public override float? FaceDeg(Vector2 seen) => FaceTowards(seen);
}

// ---- attacks ---------------------------------------------------------------------------------------------------------------------

// Floe Harrower: floe cast. The tell plants it while its slab chutes charge;
// the action casts shotCount ice slabs (EliteShots.Kind.Slab) one after
// another, SlabGap apart, out of alternate chutes, each gliding out
// (hazardSeconds) onto its own spot in a staggered row across the whole
// board, led by the scroll so it is lobAhead in front of the pilot when the
// lance comes -- shotCount + 1
// slots between the rails, one left open: the gap, the slot beside the
// pilot's toward the middle. The slabs ride the board, drifting hazardSpeed
// sideways together, block shots (hazardArmour hits) and hurt on touch.
// The gap is chosen at the tell, and from SightFrom of it a sight line
// blinks from its keel down the gap's lane to the pilot's height; right
// after the last slab (LanceDelay) one fast lance (a bolt) goes down it:
// keep out of the gap's lane until the lance has passed, then slip into it
// before the row arrives (under a second at the top scroll speeds).
public class FloeCastAttack : EliteAttack
{
    public const float SightWidth = .18f, SlabGap = .06f, Stagger = .2f, SightFrom = .4f, LanceDelay = .15f, SightLength = 9f;
    public const int Keel = 2;   // muzzle: the lance; 0 / 1: the chutes
    readonly EliteShot[] slabs = new EliteShot[6];
    float next, slot, sideways, settleAt;
    int cast, slotsDone, gap;
    Vector2 row;
    bool lanced;

    public FloeCastAttack() { Id = "floe_cast"; }
    public override bool HoldsDuringTell => true;
    public override bool Aimed => true;
    public int Count => Mathf.Clamp(def.shotCount, 3, 5);
    public int Gap => gap;
    public Vector2 Row => row;
    public float SlotWidth => slot;
    public bool Lanced => lanced;
    // (the lance comes right after the last slab: at the top scroll speeds the row is on the pilot within a second)
    public float LanceAt => (Count - 1) * SlabGap + LanceDelay;
    public float SettleAt => settleAt;
    public int Cast => cast;
    public EliteShot Slab(int i) => slabs[i];

    // Spot i of the row now (the row rides the board, the slabs drift with it).
    public Vector2 Spot(int i)
    {
        float edge = EliteSystem.RailEdge;
        return new Vector2(-edge + slot * (i + .5f), row.y + (i % 2 == 0 ? Stagger : -Stagger));
    }

    // Where the gap is now: its spot, drifted with the slabs since they settled.
    public Vector2 GapNow => Spot(gap) + Vector2.right * sideways * Mathf.Max(0f, t - settleAt);

    // Where the lance goes: down the gap's lane, at the pilot's height (wherever the row is).
    public Vector2 LanceAim => new Vector2(GapNow.x, Mathf.Min(ship.AimSeen.y, ship.Position.y - 1f));

    public override bool FriendlyInLine(Vector2 seen) =>
        EliteOnLine(ship.Position, Vector2.down, Mathf.Max(1f, ship.Position.y - seen.y), 1f);

    public override void Cancel() { ship.ShowSight(Vector2.zero, 0f, 0f, false); }

    // The plan is made at the tell (the gap: the slot beside the pilot's, toward the middle) ...
    public override void BeginTell(Vector2 seen)
    {
        base.BeginTell(seen);
        float edge = EliteSystem.RailEdge;
        int n = Count;
        slot = 2f * edge / (n + 1);
        row = new Vector2(0f, seen.y + def.lobAhead);
        sideways = 0f;
        settleAt = 0f;
        int mine = Mathf.Clamp(Mathf.FloorToInt((seen.x + edge) / slot), 0, n);
        gap = Mathf.Clamp(mine + (seen.x > 0f ? -1 : 1), 0, n);
        if (gap == mine) gap = mine == 0 ? 1 : mine - 1;
    }

    // ... and shown: from SightFrom of the wind-up a sight blinks down the gap's lane
    public override void StepTell(float dt)
    {
        t += dt;
        if (t < TellSeconds * SightFrom) return;
        Vector2 keel = ship.MuzzleWorld(Keel);
        bool on = Mathf.FloorToInt(t / (4f * EliteArt.Tick)) % 2 == 0;
        ship.ShowSight(keel, Deg(LanceAim - keel), SightLength, on, SightWidth);
    }

    public override void BeginAction()
    {
        base.BeginAction();
        next = 0f;
        cast = 0;
        slotsDone = 0;
        lanced = false;
        for (int i = 0; i < slabs.Length; i++) slabs[i] = null;
        int n = Count;
        settleAt = (n - 1) * SlabGap + def.hazardSeconds;
        // the row: lobAhead in front of the pilot once the slabs are out (it rides the board meanwhile)
        row.y = Mathf.Min(ship.AimSeen.y + def.lobAhead + EliteSystem.Scroll * settleAt, EliteSystem.ViewTop - .6f);
        // they all drift the same way: toward the middle from the gap's side
        sideways = (Spot(gap).x > 0f ? -1f : 1f) * def.hazardSpeed;
    }

    public override bool StepAction(float dt)
    {
        t += dt;
        row.y -= EliteSystem.Scroll * dt;   // the row is on the board: it rides it
        int n = Count;
        while (t >= next && slotsDone <= n)
        {
            if (slotsDone != gap)
            {
                int m = cast % 2;
                Vector2 to = Spot(slotsDone);
                var s = Fire(m, Deg(to - ship.MuzzleWorld(m)), 0f, EliteShots.Kind.Slab);
                if (s != null) { s.Glide(to, def.hazardSeconds, sideways); slabs[cast] = s; }
                cast++;
                next += SlabGap;
            }
            slotsDone++;
        }
        Vector2 keel = ship.MuzzleWorld(Keel);
        float deg = Deg(LanceAim - keel);
        if (!lanced)
        {
            // the sight down the gap's lane, blinking fast now: the lance is coming
            bool on = Mathf.FloorToInt(t / (2f * EliteArt.Tick)) % 2 == 0;
            ship.ShowSight(keel, deg, SightLength, on, SightWidth);
        }
        if (!lanced && t >= LanceAt)
        {
            lanced = true;
            ship.ShowSight(Vector2.zero, 0f, 0f, false);
            Fire(Keel, deg, def.shotSpeed);
        }
        return lanced && t >= Mathf.Max(def.actionSeconds, LanceAt + .1f);
    }

    public override void End() { ship.ShowSight(Vector2.zero, 0f, 0f, false); }

    // Its slabs break up with it.
    public override void OnDeath()
    {
        for (int i = 0; i < slabs.Length; i++)
        {
            var s = slabs[i];
            slabs[i] = null;
            if (s == null || !s.Active || s.Owner != ship || s.Kind != EliteShots.Kind.Slab) continue;
            s.Struck();   // (until it breaks)
            if (s.Active) { EliteSystem.Fx.Sparks(s.transform.position, def.ShotColor, 6); s.Recycle(); }
        }
    }
}

// Cryo Siren: frost bloom. The tell charges the dish; the action sends one
// slow cryo orb (EliteShots.Kind.Orb, shotSpeed x OrbShare) at the pilot,
// its fuse ring blinking; hazardSeconds later it bursts into a ring of
// hazardCount shards (hazardSpeed). Shoot the orb first and it just pops.
// Every BeamEvery-th attack is a beam sweep instead: a longer tell
// (BeamTell) in which a sight line paints the arc it will sweep -- BeamArc
// either side of the pilot -- then locks on its start; the action sweeps a
// stream of fast bolts (shotSpeed) across that arc. Get out of the arc.
// At its last heart (below DoubleBelow) a bloom is TWO orbs, OrbSplit degrees either side of the aim.
public class FrostBloomAttack : EliteAttack
{
    public const int BeamEvery = 3, DoubleBelow = 2;
    public const float OrbSplit = 11f;
    public const float SightWidth = .2f, OrbShare = .3f, BeamArc = 16f, BeamTell = 1.6f, BeamInterval = .035f, BeamLength = 9f, LockShare = .7f;
    bool beam, acted;
    int casts, beamShots;
    float a0, a1, next;
    public bool Beam => beam;
    public float BeamFrom => a0;
    public float BeamTo => a1;
    public int BeamShots => beamShots;
    public EliteShot Orb { get; private set; }
    public EliteShot Orb2 { get; private set; }
    public int Orbs => ship != null && ship.Hearts < DoubleBelow ? 2 : 1;

    public FrostBloomAttack() { Id = "frost_bloom"; }

    public override float TellSeconds => beam ? BaseTell * BeamTell : BaseTell;
    public override bool Aimed => true;
    public override bool HoldsDuringTell => beam;
    public bool Locked => beam && t >= TellSeconds * LockShare;

    public override bool FriendlyInLine(Vector2 seen)
    {
        Vector2 to = seen - ship.Position;
        float dist = to.magnitude;
        return dist > 1e-3f && EliteOnLine(ship.Position, to / dist, dist + 1f, .45f);
    }

    public override void BeginTell(Vector2 seen)
    {
        base.BeginTell(seen);
        acted = false;
        casts++;
        beam = casts % BeamEvery == 0;
        float c = Deg(seen - ship.MuzzleWorld(0));
        float s = seen.x >= ship.Position.x ? 1f : -1f;
        // sweeps from past the pilot's outer side back across it
        a0 = c + s * BeamArc;
        a1 = c - s * BeamArc;
    }

    // Forces the next attack to be a beam (tests, previews).
    public void NextIsBeam() { casts = (casts / BeamEvery + 1) * BeamEvery - 1; }

    public override void Cancel() { ship.ShowSight(Vector2.zero, 0f, 0f, false); }

    public override void StepTell(float dt)
    {
        t += dt;
        if (!beam) return;
        // the arc painted: the sight swings across it, then holds on the start, blinking faster
        float deg = a0;
        if (!Locked)
        {
            float k = Mathf.PingPong(t * 2f / (TellSeconds * LockShare), 1f);
            deg = Mathf.Lerp(a0, a1, k);
        }
        // (painting: steady; locked: blinking fast)
        bool on = !Locked || Mathf.FloorToInt(t / (2f * EliteArt.Tick)) % 2 == 0;
        ship.ShowSight(ship.MuzzleWorld(0), deg, BeamLength, on, SightWidth);
    }

    public override void BeginAction()
    {
        base.BeginAction();
        acted = true;
        next = 0f;
        beamShots = 0;
        if (beam) return;
        ship.ShowSight(Vector2.zero, 0f, 0f, false);
        float aimDeg = Deg(ship.AimSeen - ship.MuzzleWorld(0));
        int n = Orbs;
        Orb = Orb2 = null;
        for (int i = 0; i < n; i++)
        {
            float split = n == 1 ? 0f : (i == 0 ? -OrbSplit : OrbSplit);
            var orb = Fire(0, aimDeg + split, def.shotSpeed * OrbShare, EliteShots.Kind.Orb);
            if (orb != null) orb.Fuse(def.hazardSeconds);
            if (i == 0) Orb = orb; else Orb2 = orb;
        }
    }

    public override bool StepAction(float dt)
    {
        t += dt;
        if (!beam) return t >= def.actionSeconds;
        float k = Mathf.Clamp01(t / def.actionSeconds);
        float deg = Mathf.Lerp(a0, a1, k);
        ship.ShowSight(ship.MuzzleWorld(0), deg, BeamLength, true, SightWidth);
        while (t >= next && next <= def.actionSeconds)
        {
            Fire(0, Mathf.Lerp(a0, a1, Mathf.Clamp01(next / def.actionSeconds)), def.shotSpeed);
            beamShots++;
            next += BeamInterval;
        }
        return t >= def.actionSeconds;
    }

    // The bloom is done and the Siren hops on to its next perch.
    public override void End()
    {
        ship.ShowSight(Vector2.zero, 0f, 0f, false);
        var h = ship.Brain as HopperBrain;
        if (h != null && acted) h.BloomDone();
    }

    // An orb still on its fuse dies with it (pops, no ring).
    public override void OnDeath()
    {
        Pop(Orb);
        Pop(Orb2);
        Orb = Orb2 = null;
    }

    void Pop(EliteShot o)
    {
        if (o != null && o.Active && o.Owner == ship && o.Kind == EliteShots.Kind.Orb) o.Struck();
    }
}

// Glacier Tender: drone deploy. The tell charges the drone pods; the action
// releases 2-3 ice drones (Frost's Flake fighter, hazardSize scale, its own
// movers off: EliteDrone) out of the pods, never more than Cap (hazardCount)
// alive, each on a glowing cyan tether. They fan out lobAhead below it
// (FanDeg apart) and sway there at hazardSpeed -- never lower than
// DroneFloor above the pilot -- a wall of targets and hazards. While two or
// more live, the Tender is shield-linked (a blinking cyan ring): no hit but
// a pause jump, a shielded ram or a rail costs it a heart. Kill the drones
// first. Killing the Tender scuttles every drone it still has.
public class DroneDeployAttack : EliteAttack
{
    public const string DroneKey = "frost_fighter_1";
    public const float FanDeg = 38f, DroneFloor = 1.1f, BlockFlash = .15f;
    public const int Link = 2;
    readonly GameObject[] drones = new GameObject[4];
    readonly int[] pod = new int[4];
    SpriteRenderer[] tethers;
    SpriteRenderer shield;
    float clock, flash;

    public DroneDeployAttack() { Id = "drone_deploy"; }
    public override bool Shoots => false;
    public int Cap => Mathf.Clamp(def.hazardCount, Link, drones.Length);
    public int Released { get; private set; }
    public int Blocked { get; private set; }
    public bool ShieldShown => shield != null && shield.enabled;
    public bool TetherShown(int i) => tethers != null && i < tethers.Length && tethers[i].enabled;

    public int Live
    {
        get
        {
            int n = 0;
            for (int i = 0; i < drones.Length; i++) if (Alive(drones[i])) n++;
            return n;
        }
    }

    public bool Linked => Live >= Link;
    public GameObject Drone(int i) => i >= 0 && i < drones.Length && Alive(drones[i]) ? drones[i] : null;

    static bool Alive(GameObject go) => go != null && go.activeInHierarchy;

    public override bool FriendlyInLine(Vector2 seen) =>
        EliteOnLine(ship.Position, Vector2.down, def.lobAhead + .5f, def.lobAhead * .7f);

    public override void BeginAction()
    {
        base.BeginAction();
        int want = Random.Range(2, 4);
        int pods = Mathf.Max(1, def.muzzles.Length);
        for (int i = 0; i < Cap && want > 0; i++)
        {
            if (Alive(drones[i])) continue;
            int m = Released % pods;
            var go = SpawnDrone(ship.MuzzleWorld(m), i);
            if (go == null) break;
            drones[i] = go;
            pod[i] = m;
            ship.OnFired(m, 270f);
            Fired++;
            Released++;
            want--;
        }
    }

    public override bool StepAction(float dt)
    {
        t += dt;
        return t >= def.actionSeconds;
    }

    GameObject SpawnDrone(Vector2 at, int slot)
    {
        var d = EnemyRoster.Find(DroneKey);
        if (d == null) return null;
        var go = EnemyFactory.Create(d, new Vector3(at.x, at.y, 0f), Quaternion.identity);
        if (go == null) return null;
        go.transform.SetParent(EliteSystem.Root, true);   // (gone with the elites on a clear)
        go.transform.localScale *= Mathf.Max(.2f, def.hazardSize);
        // the Tender flies it on its tether: its own movers off
        var mover = go.GetComponent<moveItemEnmInStrightLine>();
        if (mover != null) mover.enabled = false;
        var brain = go.GetComponent<EnemyBrain>();
        if (brain != null) brain.enabled = false;
        var drone = go.AddComponent<EliteDrone>();
        drone.mother = ship;
        drone.slot = slot;
        SpawnFootprint.Bind(go, drone);
        var ct = ClearTarget.Ensure(go);
        if (ct != null) ct.Mother = ship;
        return go;
    }

    // Where drone slot i hangs: fanned out below the Tender, swaying.
    public Vector2 SlotSpot(int i)
    {
        float deg = -90f + (i - (Cap - 1) * .5f) * FanDeg;
        Vector2 p = ship.Position + Unit(deg) * def.lobAhead + new Vector2(Mathf.Sin(clock * 1.9f + i * 2.1f) * .18f, Mathf.Sin(clock * 1.3f + i) * .08f);
        float edge = EliteSystem.RailEdge - .35f;
        p.x = Mathf.Clamp(p.x, -edge, edge);
        p.y = Mathf.Max(p.y, ship.Seen.y + DroneFloor);
        return p;
    }

    public override void Passive(float dt)
    {
        clock += dt;
        if (flash > 0f) flash -= dt;
        if (tethers == null) Build();
        for (int i = 0; i < drones.Length; i++)
        {
            var go = drones[i];
            if (!Alive(go))
            {
                drones[i] = null;
                if (i < tethers.Length) tethers[i].enabled = false;
                continue;
            }
            Vector2 p = go.transform.position;
            p = Vector2.MoveTowards(p, SlotSpot(i), Mathf.Max(.5f, def.hazardSpeed) * dt);
            go.transform.position = new Vector3(p.x, p.y, 0f);
            // the tether: pod to drone, glowing in hard steps
            var line = tethers[i];
            Vector2 from = ship.MuzzleWorld(pod[i]);
            Vector2 to = p - from;
            line.enabled = true;
            line.transform.position = new Vector3(from.x, from.y, 0f);
            line.transform.rotation = Quaternion.Euler(0f, 0f, Deg(to) - 90f);
            line.transform.localScale = new Vector3(.08f, to.magnitude, 1f);
            Color c = def.ShotColor;
            c.a = Mathf.FloorToInt((clock + i * .1f) / (5f * EliteArt.Tick)) % 2 == 0 ? .85f : .5f;
            line.color = c;
        }
        bool linked = Linked;
        shield.enabled = linked;
        if (linked)
        {
            Color c = flash > 0f ? def.ShotCore : def.ShotColor;
            c.a = flash > 0f ? 1f : Mathf.FloorToInt(clock / (6f * EliteArt.Tick)) % 2 == 0 ? .75f : .45f;
            shield.color = c;
        }
    }

    void Build()
    {
        tethers = new SpriteRenderer[drones.Length];
        for (int i = 0; i < tethers.Length; i++)
        {
            var go = new GameObject("Tether" + i);
            go.transform.SetParent(ship.transform, false);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = EliteFxArt.Sight;
            sr.sortingOrder = EliteShip.PlayOrder - 1;
            sr.enabled = false;
            tethers[i] = sr;
        }
        var sg = new GameObject("ShieldLink");
        sg.transform.SetParent(ship.transform, false);
        shield = sg.AddComponent<SpriteRenderer>();
        shield.sprite = EliteFxArt.Ring;
        shield.sortingOrder = EliteShip.PlayOrder + 3;
        sg.transform.localScale = Vector3.one * def.hullRadius * 2.9f / Mathf.Max(.01f, EliteFxArt.Ring.bounds.size.x);
        shield.enabled = false;
    }

    // The shield link: two drones up, only a pause jump, a shielded ram or a rail gets through.
    public override bool Absorbs(EliteDamage cause, Vector3 at)
    {
        if (!Linked || cause == EliteDamage.Teleport || cause == EliteDamage.ShieldRam || cause == EliteDamage.Domino || cause == EliteDamage.Rail) return false;
        Blocked++;
        flash = BlockFlash;
        EliteSystem.Fx.Sparks(at, def.ShotCore, 6);
        return true;
    }

    // Its drones go down with it (a blast each, nothing paid).
    public override void OnDeath()
    {
        for (int i = 0; i < drones.Length; i++)
        {
            var go = drones[i];
            drones[i] = null;
            if (Alive(go)) EliteDrone.Scuttle(go);
        }
        if (tethers != null) foreach (var l in tethers) if (l != null) l.enabled = false;
        if (shield != null) shield.enabled = false;
    }
}

// A Glacier Tender's drone: marks the Frost fighter as its mother's (it is
// flown by DroneDeployAttack.Passive) and gives SpawnSpace its footprint.
public class EliteDrone : MonoBehaviour, IMovementFootprint
{
    public EliteShip mother;
    public int slot;

    public Rect SweptBounds(Vector2 center, Vector2 half, float from, float to)
    {
        float own = 3f * to;
        return Rect.MinMaxRect(center.x - half.x - own, center.y - half.y - own, center.x + half.x + own, center.y + half.y + own);
    }

    public bool SelfSteering => true;

    // Its Tender went down: the drone goes too (a blast, nothing paid -- not a shot, a body).
    public static void Scuttle(GameObject go) { EliteShip.FriendlyKill(go); }
}

// Whiteout Sentinel's plates: armour shatter. hazardCount ice plates (rims drawn on its prow, outermost first) soak every hit but a
// pause jump, a shielded ram or a rail: each costs a plate instead of a heart (then PlateGrace of no more plate losses). The broken
// plate's ice comes back at you: two cone edges blink from its eye for SprayDelay, then shotCount shards fan out along them
// (shotSpread apart). Its own attack while plated: a shard from each side emitter at the pilot. Stripped (the damaged drawing) the
// armour has nothing left to soak with: IcicleBarrageAttack takes over (this one then just keeps shooting). There is no charge.
public class ArmourShatterAttack : EliteAttack
{
    public const float SprayDelay = .4f, PlateGrace = .3f, ConeLength = 2.4f;
    int plates;
    float plateGrace, sprayIn = -1f, clock;
    SpriteRenderer[] plateArt;
    SpriteRenderer coneL, coneR;
    public int Plates => plates;
    // Every plate gone at once (tests: the hearts underneath).
    public void Strip()
    {
        plates = 0;
        if (plateArt != null) foreach (var p in plateArt) p.enabled = false;
    }
    public bool Stripped => plates <= 0;
    public int Shattered { get; private set; }
    public int Sprays { get; private set; }
    public bool SprayTelegraphed => sprayIn >= 0f;
    public bool PlateShown(int i) => plateArt != null && i < plateArt.Length && plateArt[i].enabled;

    public ArmourShatterAttack() { Id = "armour_shatter"; }

    protected override void OnBind() { plates = Mathf.Max(1, def.hazardCount); }

    public override bool Aimed => true;
    public override bool ShowsDamaged => Stripped;

    public float ConeHalf => Mathf.Max(8f, def.shotSpread * (Mathf.Max(2, def.shotCount) - 1) * .5f);

    public override bool FriendlyInLine(Vector2 seen)
    {
        Vector2 to = seen - ship.Position;
        float dist = to.magnitude;
        return dist > 1e-3f && EliteOnLine(ship.Position, to / dist, dist + .5f, .5f);
    }

    public override void BeginAction()
    {
        base.BeginAction();
        // plated: a shard from each side emitter at the pilot
        for (int m = 1; m < def.muzzles.Length; m++)
            Fire(m, Deg(ship.AimSeen - ship.MuzzleWorld(m)), def.shotSpeed);
    }

    public override bool StepAction(float dt)
    {
        t += dt;
        return t >= def.actionSeconds * .5f;
    }

    public override bool Absorbs(EliteDamage cause, Vector3 at)
    {
        if (plates <= 0 || cause == EliteDamage.Teleport || cause == EliteDamage.ShieldRam || cause == EliteDamage.Domino || cause == EliteDamage.Rail) return false;
        if (plateGrace > 0f) return true;
        plates--;
        Shattered++;
        plateGrace = PlateGrace;
        if (sprayIn < 0f) sprayIn = SprayDelay;
        if (plateArt != null && plates < plateArt.Length)
        {
            plateArt[plates].enabled = false;
            EliteSystem.Fx.Sparks(plateArt[plates].transform.position, def.ShotCore, 10);
        }
        // stripped: it answers soon (the barrage)
        if (plates == 0 && ship.State == EliteState.Follow) ship.AttackCooldown = Mathf.Min(ship.AttackCooldown, .5f);
        return true;
    }

    public override void Passive(float dt)
    {
        clock += dt;
        if (plateArt == null) Build();
        if (plateGrace > 0f) plateGrace -= dt;
        for (int i = 0; i < plateArt.Length; i++)
        {
            if (!plateArt[i].enabled) continue;
            Color c = def.ShotCore;
            c.a = (i == plates - 1 && plateGrace > 0f) ? 1f : Mathf.FloorToInt((clock + i * .2f) / (8f * EliteArt.Tick)) % 2 == 0 ? .8f : .6f;
            plateArt[i].color = c;
        }
        if (sprayIn < 0f) return;
        sprayIn -= dt;
        Vector2 eye = ship.MuzzleWorld(0);
        float face = ship.MuzzleDeg(0);
        if (sprayIn >= 0f)
        {
            // the telegraph: the cone's two edges blink from its eye
            bool on = Mathf.FloorToInt(sprayIn / (2f * EliteArt.Tick)) % 2 == 0;
            Edge(coneL, eye, face + ConeHalf, on);
            Edge(coneR, eye, face - ConeHalf, on);
            return;
        }
        coneL.enabled = coneR.enabled = false;
        sprayIn = -1f;
        Sprays++;
        int n = Mathf.Max(2, def.shotCount);
        for (int i = 0; i < n; i++)
            Fire(0, face + (i - (n - 1) * .5f) * def.shotSpread, def.shotSpeed);
    }

    void Edge(SpriteRenderer sr, Vector2 from, float deg, bool on)
    {
        sr.enabled = on;
        if (!on) return;
        sr.transform.position = new Vector3(from.x, from.y, 0f);
        sr.transform.rotation = Quaternion.Euler(0f, 0f, deg - 90f);
        sr.transform.localScale = new Vector3(.1f, ConeLength, 1f);
        Color c = def.ShotColor;
        c.a = .8f;
        sr.color = c;
    }

    void Build()
    {
        int n = Mathf.Max(1, def.hazardCount);
        plateArt = new SpriteRenderer[n];
        // rims ahead of its prow, innermost (index 0, the last to go) to outermost
        Vector2 tip = def.muzzles.Length > 0 ? def.PixelToLocal(def.muzzles[0].x, def.muzzles[0].y - 36f) : Vector2.up * .3f;
        for (int i = 0; i < n; i++)
        {
            var go = new GameObject("Plate" + i);
            go.transform.SetParent(ship.HullTransform, false);
            go.transform.localPosition = tip + Vector2.up * (.035f + i * .07f);
            go.transform.localScale = Vector3.one * def.cellWorldSize * (.42f + i * .1f);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = EliteFxArt.Plate;
            sr.sortingOrder = EliteShip.PlayOrder + 1;
            sr.enabled = i < plates;
            plateArt[i] = sr;
        }
        coneL = Line("ConeL");
        coneR = Line("ConeR");
    }

    SpriteRenderer Line(string name)
    {
        var go = new GameObject(name);
        go.transform.SetParent(ship.transform, false);
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = EliteFxArt.Sight;
        sr.sortingOrder = EliteShip.PlayOrder - 1;
        sr.enabled = false;
        return sr;
    }

    public override void OnDeath()
    {
        sprayIn = -1f;
        if (coneL != null) coneL.enabled = coneR.enabled = false;
    }
}

// ---- the new Frost attacks ------------------------------------------------------------------------------------------------------

// Rimebreaker: an ice run. From the first frame of the tell an ICE-CRACK LANE is drawn down the board from the elite (the lane's
// outline and a zigzag crack down its middle: EliteTelegraph, the dotted telegraph the other themed hazards use), locked where it
// stands. The action ploughs straight down that lane at dashSpeed -- committed, no steering, so a sidestep is all it takes -- through
// the WHOLE board, breaking every rock in the way (no heart lost) and out of the bottom; frost shards fly off the prow at the launch
// and off the first MaxPloughShards rocks it breaks, glancing off the rails (shotBounces). Anything else in the lane (an enemy, a
// mine) is a crash and costs it a heart: lure it. The run over, the brain waits and brings it back on another lane (IcebreakerBrain).
public class IceRunAttack : EliteAttack
{
    public const int MaxPloughShards = 3;     // rocks per run that throw shards
    public const float LaneMargin = .12f, CrackHalf = .04f, CrackStep = 1.3f, CrackWiggle = .16f, CheckAhead = 3f, MaxRunSeconds = 7f;
    readonly EliteTelegraph telegraph = new EliteTelegraph();
    int smashed;
    float laneX;
    bool ran;
    public IceRunAttack() { Id = "ice_run"; }
    public override bool HoldsDuringTell => true;
    public override bool DrivesMovement => true;
    public override bool Ploughs => true;
    public override float? FaceDeg => -90f;
    public float LaneX => laneX;
    public int Smashed => smashed;
    public EliteTelegraph Telegraph => telegraph;

    // (straight down its lane; only the first stretch is checked for hazards -- the run is meant to cross the board and rocks do not stop it)
    public override void DashLine(Vector2 seen, bool locked, out Vector2 d, out float reach)
    {
        d = Vector2.down;
        reach = CheckAhead;
    }

    public override void BeginTell(Vector2 seen)
    {
        base.BeginTell(seen);
        ran = false;
        laneX = ship.Position.x;
        dir = Vector2.down;
        aim = new Vector2(laneX, EliteSystem.ViewBottom);
        // the lane and the crack down it, locked on screen for the whole tell
        float top = ship.Position.y - def.hullRadius, bottom = EliteSystem.ViewBottom - .6f;
        telegraph.Clear();
        telegraph.Lane(new Vector2(laneX, top), new Vector2(laneX, bottom), def.hullRadius + LaneMargin);
        Vector2 a = new Vector2(laneX, top);
        for (int i = 1; a.y > bottom + .1f; i++)
        {
            Vector2 b = new Vector2(laneX + (i % 2 == 0 ? -1f : 1f) * CrackWiggle, Mathf.Max(bottom, a.y - CrackStep));
            telegraph.Lane(a, b, CrackHalf);
            a = b;
        }
        telegraph.Show(TellSeconds, def.ShotColor, Mathf.Max(1, def.WorldIndex));
    }

    public override void StepTell(float dt)
    {
        t += dt;
        telegraph.Step(dt);
    }

    public override void Cancel() { telegraph.End(); }

    public override void BeginAction()
    {
        base.BeginAction();
        ran = true;
        telegraph.GoLive();
        smashed = 0;
        ship.Drive(Vector2.down * def.dashSpeed);
        var ib = ship.Brain as IcebreakerBrain;
        if (ib != null) ib.RunStarted();
        // the launch: a shard down-left and one down-right off the prow
        float spread = Mathf.Max(10f, def.shotSpread);
        Fire(0, -90f - spread, def.shotSpeed);
        Fire(0, -90f + spread, def.shotSpeed);
    }

    public override bool StepAction(float dt)
    {
        t += dt;
        ship.Drive(Vector2.down * def.dashSpeed);
        // out of the bottom: wholly below the view
        return ship.Position.y < FrostFlight.BelowY(def) || t >= MaxRunSeconds;
    }

    public override void OnPlough(Vector2 at)
    {
        if (smashed++ >= MaxPloughShards) return;
        // the broken rock's ice: two flat shards, out to both rails
        Fire(0, -90f - 70f, def.shotSpeed * .9f);
        Fire(0, -90f + 70f, def.shotSpeed * .9f);
    }

    public override void End()
    {
        telegraph.End();
        var ib = ship.Brain as IcebreakerBrain;
        if (ran && ib != null) ib.RunDone();
        ran = false;
    }

    public override void OnDeath() { telegraph.End(); }
}

// Floe Harrower: the barge. Casts alternate, CastEvery apart (every other cast at its last heart): a FLOE CAST (floe_cast: a staggered
// row of slabs across the board with one gap, then a lance down the gap), and between casts a SKID -- one big ice slab slid out of
// a chute at skidSpeed (faster for every heart lost) straight across the screen toward the far rail, where it glances off ONCE and
// slides back across and out of the bottom. Its whole path (to the rail, and from the rail to the bottom) is drawn as the dotted lane
// from the first frame of the tell, locked there: the slab can be shot to bits (hazardArmour hits) or simply stepped round.
public class FloeBargeAttack : FloeCastAttack
{
    public const int CastEvery = 3, CastEveryLast = 2;
    public const float SkidTell = .85f, LaneMargin = .1f, BounceAhead = 1.2f, SpeedPerHeart = .12f;
    readonly EliteTelegraph skidLane = new EliteTelegraph();
    int casts, chute;
    bool skid;
    Vector2 bounce, bottom, from;
    EliteShot slab;
    public FloeBargeAttack() { Id = "floe_barge"; }

    public bool Skid => skid;
    public int Chute => chute;
    public Vector2 BounceAt => bounce;
    public Vector2 PathEnd => bottom;
    public Vector2 PathFrom => from;
    public EliteShot SkidSlab => slab;
    public EliteTelegraph SkidLane => skidLane;
    public int Casts => casts;
    public float SkidSpeed => def.skidSpeed * (1f + SpeedPerHeart * Mathf.Max(0, def.hearts - ship.Hearts));
    int Every => ship.Hearts <= 1 ? CastEveryLast : CastEvery;
    public override float TellSeconds => skid ? Mathf.Max(EliteDef.MinAimedTell, def.tellSeconds * SkidTell) : base.TellSeconds;
    float SlabRadius => def.hazardSize * .42f;

    // The skid's two legs from a chute at `at`: out to the far rail and, mirrored, down to the bottom of the view.
    void Plan(Vector2 at, Vector2 seen)
    {
        float railX = -Mathf.Sign(ship.Position.x >= 0f ? 1f : -1f) * (EliteSystem.RailEdge - SlabRadius - .02f);
        float y = Mathf.Clamp(seen.y + BounceAhead, EliteSystem.ViewBottom + 1.5f, Mathf.Max(EliteSystem.ViewBottom + 1.6f, at.y - 2.5f));
        bounce = new Vector2(railX, y);
        Vector2 d = (bounce - at).normalized;
        float k = (bounce.y - (EliteSystem.ViewBottom - .5f)) / Mathf.Max(.1f, -d.y);
        bottom = bounce + new Vector2(-d.x, d.y) * k;
        from = at;
    }

    public override void BeginTell(Vector2 seen)
    {
        base.BeginTell(seen);
        skid = casts++ % Every != 0;
        slab = null;
        skidLane.Clear();
        if (!skid) return;
        chute = ship.Position.x >= 0f ? 0 : 1;   // the chute on the side it slides toward
        Plan(ship.MuzzleWorld(chute), seen);
        float half = SlabRadius + LaneMargin;
        skidLane.Lane(from, bounce, half);
        skidLane.Lane(bounce, bottom, half);
        skidLane.Ring(bounce, half * 1.6f);
        skidLane.Show(TellSeconds, def.ShotColor, Mathf.Max(1, def.WorldIndex));
    }

    public override void StepTell(float dt)
    {
        if (!skid) { base.StepTell(dt); return; }
        t += dt;
        skidLane.Step(dt);
    }

    public override void Cancel()
    {
        skidLane.End();
        base.Cancel();
    }

    public override void BeginAction()
    {
        if (!skid) { base.BeginAction(); return; }
        t = 0f;
        skidLane.GoLive();
        // straight at the bounce point it drew, from where the chute is now
        Vector2 at = ship.MuzzleWorld(chute);
        Vector2 d = (bounce - at);
        slab = Fire(chute, Deg(d), 0f, EliteShots.Kind.Slab);
        if (slab != null) slab.Skid(d.normalized * SkidSpeed);
    }

    public override bool StepAction(float dt)
    {
        if (!skid) return base.StepAction(dt);
        t += dt;
        return t >= def.actionSeconds * .6f;
    }

    public override void End()
    {
        skidLane.End();
        base.End();
    }

    // Its slabs break up with it.
    public override void OnDeath()
    {
        skidLane.End();
        base.OnDeath();
        var s = slab;
        slab = null;
        if (s != null && s.Active && s.Owner == ship && s.Kind == EliteShots.Kind.Slab)
        {
            s.Struck();
            if (s.Active) { EliteSystem.Fx.Sparks(s.transform.position, def.ShotColor, 6); s.Recycle(); }
        }
    }
}

// Glacier Tender: the carrier. Attacks alternate while its shield is down (fewer than two drones): it launches drones (drone_deploy:
// 2-3 tethered ice drones, shield-linked while two live) and VENTS A FOG -- a cold cone straight down from its belly pods (the
// themed frost jet, AttackJet: the cone's outline is drawn from the first frame of the tell, FogLength long, FogTip half-wide at the
// far end, planted for the whole tell), then it burns for FogLive and hurts anything it touches. A shielded Tender with room for more
// drones just launches. And its drones are armed: every MineEvery seconds (less for every heart lost) one of them drops a FROST MINE
// on the pilot's lane -- the spot is ringed from the moment the drone is picked (MineTell), then the mine slides out of the drone to
// it, rides the board on a fuse ring and pops into MineArms short shards. One player shot defuses it. All of it goes with the Tender.
public class TenderRigAttack : DroneDeployAttack
{
    public const float MineEvery = 4.2f, MineEveryLost = .45f, MineEveryMin = 2.4f, MineTell = .9f, MineGlide = .6f, MineFuse = 1.6f;
    public const float MineAhead = 1.5f, MineRing = .38f, MineScale = .5f, MineArmSpeed = 2.4f, MineArmSeconds = .8f, MineBelowDrone = .6f;
    public const float FogLength = 3.6f, FogBase = .3f, FogTip = 1.15f, FogLive = .55f;
    public const int MineArms = 5, MineCap = 3;
    readonly EliteShot[] mines = new EliteShot[MineCap];
    EliteMarks marks;
    AttackJet fog;
    bool venting, minePending;
    int casts, mineDrone;
    float mineClock, mineT;
    Vector2 mineSpot;

    public TenderRigAttack() { Id = "tender_rig"; }
    protected override void OnBind() { marks = new EliteMarks(ship, 1); }

    public override bool HoldsDuringTell => venting;
    public bool Venting => venting;
    public int Casts => casts;
    public AttackJet Fog => fog;
    public int Vents { get; private set; }
    public int MinesDropped { get; private set; }
    public bool MinePending => minePending;
    public Vector2 MineSpot => mineSpot;
    public EliteMarks Marks => marks;
    public EliteShot Mine(int i) => i >= 0 && i < mines.Length ? mines[i] : null;
    public float MineGap => Mathf.Max(MineEveryMin, MineEvery - MineEveryLost * Mathf.Max(0, def.hearts - ship.Hearts));

    // The cone's nozzle: between the two lower pods.
    public Vector2 FogOrigin => def.muzzles.Length >= 4 ? (ship.MuzzleWorld(2) + ship.MuzzleWorld(3)) * .5f : ship.MuzzleWorld(0);

    public static JetSpec FogSpec(int world) => new JetSpec
    {
        style = JetStyle.Frost, length = FogLength, maxLength = 0f, baseHalf = FogBase, tipHalf = FogTip, liveSeconds = FogLive, sweepDeg = 0f, centered = false, ride = 0f, world = world,
    };

    public override void BeginTell(Vector2 seen)
    {
        base.BeginTell(seen);
        // shield down: every other attack is the fog; shielded, it only launches
        venting = !Linked && casts++ % 2 == 1;
        if (!venting) return;
        Vector2 o = FogOrigin;
        fog = AttackJet.Arm(FogSpec(Mathf.Max(1, def.WorldIndex)), o, o + Vector2.down * FogLength, TellSeconds, ship.gameObject);
        if (fog != null) fog.Follow(ship.transform, o - ship.Position);
    }

    public override void Cancel()
    {
        if (fog != null) { fog.Cancel(); fog = null; }
        venting = false;
    }

    public override void BeginAction()
    {
        if (!venting) { base.BeginAction(); return; }
        t = 0f;
        Vents++;
        Fired++;
        ship.OnFired(Mathf.Min(2, def.muzzles.Length - 1), 270f);
        if (fog != null) fog.Ignite();
    }

    public override bool StepAction(float dt)
    {
        if (!venting) return base.StepAction(dt);
        t += dt;
        return t >= Mathf.Max(def.actionSeconds, FogLive + .1f);
    }

    public override void End()
    {
        fog = null;
        venting = false;
    }

    // ---- the mines ----

    public override void Passive(float dt)
    {
        base.Passive(dt);
        marks.Step(dt);
        if (ship.State == EliteState.Dead) return;
        if (Live == 0)
        {
            // no drone, no mine
            if (minePending) { minePending = false; marks.HideAll(); }
            mineClock = 0f;
            return;
        }
        if (!minePending)
        {
            mineClock += dt;
            if (mineClock >= MineGap) PlanMine();
            return;
        }
        mineT += dt;
        mineSpot.y -= EliteSystem.Scroll * dt;   // (the spot is on the board)
        marks.Set(0, mineSpot, MineRing, def.ShotColor, mineT > MineTell * .55f);
        if (mineT >= MineTell) DropMine();
    }

    bool MineSlotFree()
    {
        for (int i = 0; i < mines.Length; i++) if (mines[i] == null || !mines[i].Active || mines[i].Owner != ship) return true;
        return false;
    }

    void PlanMine()
    {
        if (!MineSlotFree()) return;
        for (int k = 0; k < 4; k++)
        {
            int i = (mineDrone + k) % 4;
            var d = Drone(i);
            if (d == null) continue;
            float edge = EliteSystem.RailEdge - .5f;
            Vector2 seen = ship.Seen;
            // the pilot's lane, a little ahead of it -- and under the drone that drops it
            mineSpot = new Vector2(Mathf.Clamp(seen.x, -edge, edge), Mathf.Max(EliteSystem.ViewBottom + .8f, Mathf.Min(seen.y + MineAhead, d.transform.position.y - MineBelowDrone)));
            mineDrone = i;
            minePending = true;
            mineT = 0f;
            mineClock = 0f;
            return;
        }
    }

    void DropMine()
    {
        minePending = false;
        marks.HideAll();
        // from the planned drone, or any other that is left
        GameObject d = Drone(mineDrone);
        for (int k = 1; d == null && k < 4; k++) d = Drone((mineDrone + k) % 4);
        if (d == null) return;
        Vector2 at = d.transform.position;
        var orb = FireFrom(at, Deg(mineSpot - at), 0f, EliteShots.Kind.Orb);
        if (orb == null) return;
        orb.Shrink(MineScale);
        orb.Mine(mineSpot, MineGlide, MineGlide + MineFuse, MineArms, MineArmSpeed, MineArmSeconds, 0f, Vector2.zero, 1);
        for (int i = 0; i < mines.Length; i++)
            if (mines[i] == null || !mines[i].Active || mines[i].Owner != ship) { mines[i] = orb; break; }
        MinesDropped++;
        mineDrone = (mineDrone + 1) % 4;
    }

    public override void OnDeath()
    {
        base.OnDeath();
        if (fog != null) { fog.Cancel(); fog = null; }
        venting = minePending = false;
        marks.HideAll();
        for (int i = 0; i < mines.Length; i++)
        {
            var m = mines[i];
            mines[i] = null;
            if (m != null && m.Active && m.Owner == ship && m.IsMine) { EliteSystem.Fx.Sparks(m.transform.position, def.ShotColor, 6); m.Recycle(); }
        }
    }
}

// Whiteout Sentinel: the barrage. While it has plates it is armour_shatter (plates soak hits and spray shards). Once STRIPPED it stops
// soaking and calls down ICICLES: the long tell (BarrageTell x tellSeconds, planted) marks the lanes with ice glyphs -- three down each
// lane -- from its first frame, locked: Lanes marked lanes (BaseLanes, one more at two hearts lost, one more at four) among Lanes + 1
// columns evenly spread between the rails; the one column left bare is the SAFE LANE, always the column beside the pilot's toward the
// middle. Then the icicles come down their lanes one after another from the top (a sweep from one end, SeqGap apart, quicker and
// faster-falling for every heart lost), the lane's glyphs going out as its icicle drops. No charge: it never moves at the pilot.
public class IcicleBarrageAttack : ArmourShatterAttack
{
    public const int BaseLanes = 4, MaxLanes = 6, Glyphs = 3;
    public const float BarrageTell = 1.7f, SeqGap = .42f, SeqGapStep = .05f, SeqGapMin = .18f, FallSpeed = 5.4f, FallStep = .35f, GlyphWidth = .22f, SkyAbove = .4f;
    readonly bool[] marked = new bool[MaxLanes + 1];
    readonly int[] order = new int[MaxLanes];
    EliteMarks glyphs;
    int columns, safe, lanes, dropped;
    float next;
    bool planned;

    public IcicleBarrageAttack() { Id = "icicle_barrage"; }
    protected override void OnBind()
    {
        base.OnBind();
        glyphs = new EliteMarks(ship, (MaxLanes + 1) * Glyphs, EliteFxArt.Icicle);
    }

    public override bool HoldsDuringTell => Stripped;
    public override float TellSeconds => Stripped ? BaseTell * BarrageTell : base.TellSeconds;
    public int HeartsLost => Mathf.Max(0, def.hearts - ship.Hearts);
    public int LaneCount => Mathf.Min(MaxLanes, BaseLanes + (HeartsLost >= 2 ? 1 : 0) + (HeartsLost >= 4 ? 1 : 0));
    public int Columns => columns;
    public int Safe => safe;
    public int Lanes => lanes;
    public int Dropped => dropped;
    public bool Planned => planned;
    public float Gap => Mathf.Max(SeqGapMin, SeqGap - SeqGapStep * HeartsLost);
    public float Fall => FallSpeed + FallStep * HeartsLost;
    public bool Marked(int column) => column >= 0 && column < columns && marked[column];
    public EliteMarks Glyph => glyphs;
    public int Barrages { get; private set; }

    // The centre of column i (columns evenly spread between the rails).
    public float ColumnX(int i)
    {
        float edge = EliteSystem.RailEdge;
        return -edge + (i + .5f) * 2f * edge / Mathf.Max(1, columns);
    }

    public float ColumnWidth => 2f * EliteSystem.RailEdge / Mathf.Max(1, columns);

    void Plan(Vector2 seen)
    {
        lanes = LaneCount;
        columns = lanes + 1;
        int mine = 0;
        float best = float.MaxValue;
        for (int i = 0; i < columns; i++) { float d = Mathf.Abs(ColumnX(i) - seen.x); if (d < best) { best = d; mine = i; } }
        // the safe lane: beside the pilot's column, toward the middle
        safe = mine + (seen.x > 0f ? -1 : 1);
        if (safe < 0 || safe >= columns) safe = mine + (safe < 0 ? 1 : -1);
        safe = Mathf.Clamp(safe, 0, columns - 1);
        for (int i = 0; i < columns; i++) marked[i] = i != safe;
        // the sweep: from one end to the other
        bool fromLeft = Random.value < .5f;
        int n = 0;
        for (int k = 0; k < columns; k++)
        {
            int c = fromLeft ? k : columns - 1 - k;
            if (c != safe) order[n++] = c;
        }
        dropped = 0;
        planned = true;
    }

    void ShowGlyphs(bool fast)
    {
        float top = EliteSystem.ViewTop - .6f, low = EliteSystem.ViewBottom + 1.1f;
        for (int c = 0; c < columns; c++)
        {
            for (int g = 0; g < Glyphs; g++)
            {
                int slot = c * Glyphs + g;
                if (!marked[c] || LaneDone(c)) { glyphs.Hide(slot); continue; }
                float y = Mathf.Lerp(top, low, g / (Glyphs - 1f));
                glyphs.Set(slot, new Vector2(ColumnX(c), y), GlyphWidth, def.ShotColor, fast);
            }
        }
    }

    bool LaneDone(int column)
    {
        for (int k = 0; k < dropped; k++) if (order[k] == column) return true;
        return false;
    }

    public override void BeginTell(Vector2 seen)
    {
        base.BeginTell(seen);
        planned = false;
        glyphs.HideAll();
        if (!Stripped) return;
        Plan(seen);
        ShowGlyphs(false);
    }

    public override void StepTell(float dt)
    {
        t += dt;
        if (Stripped && planned) ShowGlyphs(t > TellSeconds * Mathf.Min(.6f, def.aimCommit));
    }

    public override void Cancel()
    {
        glyphs.HideAll();
        planned = false;
    }

    public override void BeginAction()
    {
        if (!Stripped || !planned) { base.BeginAction(); return; }
        t = 0f;
        next = 0f;
        dropped = 0;
        Barrages++;
        ship.OnFired(0, ship.MuzzleDeg(0));
    }

    public override bool StepAction(float dt)
    {
        if (!Stripped || !planned) return base.StepAction(dt);
        t += dt;
        while (dropped < lanes && t >= next)
        {
            int c = order[dropped];
            // out of the sky over its lane
            var ic = FireFrom(new Vector2(ColumnX(c), EliteSystem.ViewTop + SkyAbove), -90f, Fall, EliteShots.Kind.Shell);
            if (ic != null) EliteSystem.Fx.Sparks(new Vector2(ColumnX(c), EliteSystem.ViewTop - .1f), def.ShotCore, 4);
            dropped++;
            next += Gap;
            ShowGlyphs(true);
        }
        if (dropped < lanes) ShowGlyphs(true);
        return dropped >= lanes && t >= def.actionSeconds;
    }

    public override void End()
    {
        glyphs.HideAll();
        planned = false;
    }

    public override void Passive(float dt)
    {
        base.Passive(dt);
        glyphs.Step(dt);
    }

    public override void OnDeath()
    {
        base.OnDeath();
        glyphs.HideAll();
        planned = false;
    }
}
