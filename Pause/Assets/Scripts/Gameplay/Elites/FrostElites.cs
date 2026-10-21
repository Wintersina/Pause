using UnityEngine;

// Frost's five elites as mini-boss fights (Codex's flight strips, Art/Enemies/Elite/Frost/), each its own brain and attack. The rules
// they share: none of them follows the pilot -- they keep their distance and antagonise; each throws HAZARDS in Frost's own material
// (ice, cold: shards, slabs, icicles, frost mines, a cold fog; the cue colours are Frost's, never pink); every attack is telegraphed
// and its aim LOCKS at the tell. They launch from Frost's ground sites (hangar, rigbay, padring, crawlerbay, hatch), and the backdrop
// brings one of the kind into view when none is (FrostDirector.BringHome).
//
//   Rimebreaker       icebreaker / ice_run        (hangar)     rammer: an ice-crack lane is drawn down a fixed column (1, 3, 2), then it
//                                                   ploughs the whole board to the bottom of the view (rocks break, no heart lost),
//                                                   U-turns up the nearer rail and rests at the top. Hurt: runs in pairs, then triples
//   Floe Harrower     barge / floe_barge          (crawlerbay) sweeper: holds high on the far side of the pilot, working a flank per beat;
//                                                   floe_cast (a slab row with a gap + a lance down it) and, between casts, single slabs
//                                                   that skid diagonally and bounce off one rail
//   Cryo Siren        hopper / frost_bloom        (rigbay)     sniper: three fixed perches (left, right, centre), one bloom each, a short
//                                                   VISIBLE hop between them with a blink trail; orb -> shard ring, a beam on the
//                                                   centre perch; two orbs once hurt, a beam every cast at the last heart
//   Glacier Tender    carrier / tender_rig        (hatch)      summoner: hangs near the top over a scripted spot; tethered ice drones (pair,
//                                                   trio, all four) that also drop frost mines on the pilot's lane; a cold fog vent
//   Whiteout Sentinel patrol / icicle_barrage     (padring)    anchor: patrols straight legs across the TOP and fires at the ends; ice
//                                                   plates soak hits and spray shards; stripped, it calls down an ICICLE BARRAGE down
//                                                   marked lanes with one safe one (the script says how many, how fast)
//
// None of them leaves the view (no Roams; only a scripted last-heart exit may). Each plays a script (docs/elites/elite-scripts.md) from its
// def json: move tokens the brains read in OnScriptMove, attack variants the attacks read by name (VariantName).
// See docs/enemy-behaviours.md ("Frost elites") for the design.

// ---- shared bits ---------------------------------------------------------------------------------------------------------------

public static class FrostFlight
{
    // World y at which an elite is wholly above the view / wholly below it.
    public static float AboveY(EliteDef def) => EliteSystem.ViewTop + def.cellWorldSize * .7f;
    public static float BelowY(EliteDef def) => EliteSystem.ViewBottom - def.cellWorldSize * .8f;
    // Where an ice run ends: on the floor of the view (KeepInView holds a ship at ViewBottom + .6 hull).
    public static float RunEndY(EliteDef def) => EliteSystem.ViewBottom + def.hullRadius * .6f + .2f;
    // The x range a ship's centre may keep between the rails.
    public static float Edge(EliteDef def, float margin) => Mathf.Max(0f, EliteSystem.RailEdge - def.hullRadius - margin);

    // The script side of the attacks: a variant is a tag as well as a set of numbers. "Declares" is true when the elite's script has any
    // variant by that name prefix (a scripted elite picks its attack by the step; an unscripted one keeps its old counter).
    public static bool Is(string name, string prefix) => !string.IsNullOrEmpty(name) && name.StartsWith(prefix, System.StringComparison.Ordinal);

    public static bool Declares(EliteShip s, string prefix)
    {
        var v = s != null && s.Script != null ? s.Script.Data.variants : null;
        if (v == null) return false;
        for (int i = 0; i < v.Length; i++) if (Is(v[i].name, prefix)) return true;
        return false;
    }
}

// ---- brains --------------------------------------------------------------------------------------------------------------------

// Rimebreaker: an icebreaker making RUNS, always inside the view. One run is: rest at the top on a lane, tell (the lane is drawn on the
// board as an ice crack from the first frame), plough straight down the whole board to the bottom of the view (IceRunAttack), then a
// U-TURN: along the bottom to the nearer rail, up the rail and across the top to the next lane, where it rests until the next beat.
// The lanes are fixed columns (token "lane:1" left, "lane:2" centre, "lane:3" right) and the script says which one comes next
// (phase 1: 1, 3, 2). It never prowls, never comes down to the pilot between runs and never leaves the view.
public class IcebreakerBrain : EliteBrain
{
    public enum Stage { Enter, Ready, Run, Return }
    public const float EnterScale = 2.2f, ReturnScale = 2.3f, ReadyWait = .1f, SettleDistance = .15f, LaneSpread = .75f, TurnReach = .9f, RailMargin = .35f;
    public const int Lanes = 3;
    static readonly int[] Order = { 0, 2, 1 };   // the unscripted order: lanes 1, 3, 2
    Stage stage = Stage.Enter;
    int laneIdx, turn;
    float ready, lane, lastLane, railSide = 1f;
    bool tokened;
    public Stage Phase => stage;
    public float Lane => lane;
    public int LaneIndex => laneIdx;
    public float LastLane => lastLane;
    public int Runs { get; private set; }
    public int RunsStarted { get; private set; }
    public int Turn => turn;
    public float RailSide => railSide;

    public IcebreakerBrain() { Id = "icebreaker"; }

    public override float TellScale => 1f;
    public override bool Steadfast => true;
    public override Vector2 JoinFrom => Vector2.up;

    protected override void OnBind() { laneIdx = Order[0]; lane = LaneX(laneIdx); }
    public override void OnJoin() { stage = Stage.Enter; ready = 0f; }
    public override void OnReturn() { stage = Stage.Enter; ready = 0f; }

    // The centre of lane i (0 left .. 2 right).
    public float LaneX(int i) => (Mathf.Clamp(i, 0, Lanes - 1) - 1) * FrostFlight.Edge(def, .5f) * LaneSpread;

    // "lane:1" .. "lane:3"
    public override void OnScriptMove(string token)
    {
        if (token.Length != 6 || token[0] != 'l' || token[4] != ':') return;
        int i = token[5] - '1';
        if (i < 0 || i >= Lanes) return;
        tokened = true;
        laneIdx = i;
        lane = LaneX(i);
    }

    // Where it rests on its lane: the top, as flown (inside the zone and the player's reach).
    public Vector2 ReadyPoint => ship.ClampGoal(ship.ReachGoal(new Vector2(lane, EliteSystem.ViewTop - def.hullRadius - .35f)));

    Vector2 TurnPoint(int i)
    {
        float railX = railSide * FrostFlight.Edge(def, RailMargin);
        if (i == 0) return new Vector2(railX, EliteSystem.ViewBottom + def.hullRadius + .5f);
        return new Vector2(railX, ReadyPoint.y - .8f);
    }

    public override Vector2 Goal(Vector2 seen, float dt)
    {
        Clock += dt;
        switch (stage)
        {
            case Stage.Enter:
            {
                Vector2 rp = ReadyPoint;
                if ((Pos - rp).sqrMagnitude < SettleDistance * SettleDistance) { stage = Stage.Ready; ready = 0f; }
                return rp;
            }
            case Stage.Ready:
            {
                ready += dt;
                Vector2 rp = ReadyPoint;
                // (the next lane came: slide across to it)
                if (Mathf.Abs(Pos.x - rp.x) > .5f) { stage = Stage.Enter; ready = 0f; }
                return rp;
            }
            case Stage.Return:
            {
                // the U-turn: round the bottom corner, up the rail, then over to the lane
                Vector2 wp = TurnPoint(turn);
                if ((Pos - wp).sqrMagnitude < TurnReach * TurnReach)
                {
                    if (turn == 0) turn = 1;
                    else { stage = Stage.Enter; ready = 0f; }
                    wp = TurnPoint(turn);
                }
                return wp;
            }
            default:
                return Pos;
        }
    }

    public override float SpeedScale => stage == Stage.Enter ? EnterScale : stage == Stage.Return ? ReturnScale : 1f;
    public override bool WantsAttack(Vector2 seen) => stage == Stage.Ready && ready >= ReadyWait;
    // prow first: nose down through the whole run, and at the top waiting for it
    public override float? FaceDeg(Vector2 seen) => stage == Stage.Return ? (float?)null : -90f;

    // The attack's callbacks.
    public void RunStarted() { stage = Stage.Run; RunsStarted++; }

    public void RunDone()
    {
        Runs++;
        lastLane = lane;
        // up the rail nearer to where the run ended
        railSide = lane > .1f ? 1f : lane < -.1f ? -1f : ship.Script.Alternate("rail");
        if (!tokened) { laneIdx = Order[(ship.Script.Cycle("lane", Lanes) + 1) % Lanes]; lane = LaneX(laneIdx); }
        turn = 0;
        stage = Stage.Return;
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
    public float Height => ship.ZoneY(ship.ReachY(EliteSystem.ViewTop - def.topMargin));

    public BargeBrain() { Id = "barge"; }

    protected override void OnBind() { side = ship.Script.Alternate("side"); }

    // "L" / "R": the flank this beat is worked from (the pilot taking that flank still sends it across to the other).
    public override void OnScriptMove(string token)
    {
        if (token != "L" && token != "R") return;
        float s = token == "L" ? -1f : 1f;
        if (s == side) return;
        // (the pilot has that flank: it stays on the far side)
        Vector2 p = ship.Seen;
        if (Mathf.Abs(p.x) > FlipBand && Mathf.Sign(p.x) == s) return;
        side = s;
        crossing = true;
        Crossings++;
    }

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

// Cryo Siren: a long-range sniper that works three FIXED PERCHES (token "perch:1" left, "perch:2" right, "perch:3" centre), Standoff
// above the pilot (keepDistance, inside the player's reach), one bloom per perch. Between perches it HOPS in plain sight: the BLINK
// TRAIL of rings is drawn into the next perch for a beat (Mark), then it streaks there fast (Hop). It never leaves the view and
// never closes on the pilot.
public class HopperBrain : EliteBrain
{
    public enum Stage { Enter, Perch, Mark, Hop }
    public const float MinRange = 3.3f, PerchWait = .5f, MarkSeconds = .45f, EnterScale = 2f, HopScale = 3.2f, MarkScale = .25f, SettleDistance = .35f;
    public const float TrailRing = .26f, TrailStep = .9f;
    public const int TrailRings = 4;
    static readonly float[] Columns = { -.78f, .78f, 0f };
    Stage stage = Stage.Enter;
    int perch;
    bool tokened;
    float perched, markT;
    Vector2 from;
    EliteMarks marks;
    public Stage Phase => stage;
    public int Perch => perch;
    public int Hops { get; private set; }
    public EliteMarks Marks => marks;
    public int ColumnCount => Columns.Length;

    public HopperBrain() { Id = "hopper"; }

    public override float TellScale => 1f;
    public override Vector2 JoinFrom => new Vector2(.5f, 1f);
    protected override void OnBind() { marks = new EliteMarks(ship, TrailRings); }

    public override void OnJoin() { stage = Stage.Enter; perched = 0f; }
    public override void OnReturn() { marks.HideAll(); stage = Stage.Enter; perched = 0f; }

    // Where perch `i` is now: its column, Standoff above the pilot (kept inside the player's reach and the view).
    public Vector2 Station(int i, Vector2 f)
    {
        float edge = FrostFlight.Edge(def, .5f);
        float y = Mathf.Min(f.y + def.keepDistance, EliteSystem.ViewTop - def.hullRadius - 1f);
        return new Vector2(Columns[Mathf.Clamp(i, 0, Columns.Length - 1)] * edge, ship.ZoneY(ship.ReachY(y)));
    }

    // "perch:1" .. "perch:3"
    public override void OnScriptMove(string token)
    {
        if (token.Length != 7 || token[0] != 'p' || token[5] != ':') return;
        int n = token[6] - '1';
        if (n < 0 || n >= Columns.Length) return;
        tokened = true;
        GoTo(n);
    }

    void GoTo(int n)
    {
        if (n == perch) return;
        perch = n;
        // (still on its way in: it just flies to the new perch; settled or hopping: the blink trail first)
        if (stage != Stage.Enter) BeginMark();
    }

    void BeginMark()
    {
        stage = Stage.Mark;
        markT = 0f;
        from = Pos;
        Hops++;
        EliteSystem.Fx.Sparks(Pos, def.EngineColor, 6);
    }

    // The trail: rings along the way, the biggest on the perch itself.
    void Trail(Vector2 st, bool fast)
    {
        Vector2 d = from - st;
        float len = d.magnitude;
        Vector2 dir = len > .01f ? d / len : Vector2.up;
        for (int i = 0; i < TrailRings; i++)
            marks.Set(i, st + dir * Mathf.Min(len, i * TrailStep), i == 0 ? TrailRing * 1.6f : TrailRing, def.ShotColor, fast, 1f - i * .18f);
    }

    public override Vector2 Goal(Vector2 follow, float dt)
    {
        Clock += dt;
        marks.Step(dt);
        Vector2 st = Station(perch, follow);
        switch (stage)
        {
            case Stage.Enter:
                if ((Pos - st).sqrMagnitude < SettleDistance * SettleDistance) { stage = Stage.Perch; perched = 0f; }
                return st;
            case Stage.Perch:
                perched += dt;
                return st + new Vector2(Mathf.Sin(Clock * 1.7f) * .12f, Mathf.Sin(Clock * 1.1f) * .1f);
            case Stage.Mark:
                markT += dt;
                Trail(st, markT > MarkSeconds * .55f);
                if (markT >= MarkSeconds) { stage = Stage.Hop; EliteSystem.Fx.Sparks(Pos, def.ShotColor, 8); }
                return Pos;
            default:
                Trail(st, true);
                if ((Pos - st).sqrMagnitude < SettleDistance * SettleDistance)
                {
                    marks.HideAll();
                    stage = Stage.Perch;
                    perched = 0f;
                    EliteSystem.Fx.Sparks(Pos, def.ShotColor, 8);
                }
                return st;
        }
    }

    public override float SpeedScale => stage == Stage.Enter ? EnterScale : stage == Stage.Hop ? HopScale : stage == Stage.Mark ? MarkScale : 1f;
    public override float MinAttackAbove => 1.2f;

    public override bool WantsAttack(Vector2 seen) =>
        stage == Stage.Perch && perched > PerchWait && Pos.y > seen.y + MinAttackAbove && (seen - Pos).magnitude >= MinRange * 1.05f;

    // The bloom is done. A scripted Siren hops when the next step's token comes; an unscripted one walks the perches in order.
    public void BloomDone() { if (!tokened && stage == Stage.Perch) GoTo((perch + 1) % Columns.Length); }
}

// Glacier Tender: a slow support tug. Hangs topMargin under the top of the view (never above the player's reach) over the spot the
// script gives it (token "C" the middle, "L" / "R" a hand off it), a slow bob, no sliding about -- it does not follow the pilot. When
// the pilot comes within keepDistance it scurries off sideways, fast, until the pilot is 1.4x that away again. Attacks (releases
// drones, or vents fog) while it has room for more drones, or when the beat is a vent.
public class CarrierBrain : EliteBrain
{
    public const float FleeScale = 1.7f, Release = 1.4f, SlideRate = .35f;
    bool fleeing, init;
    float trackX, fleeSide = 1f, hang;
    public bool Fleeing => fleeing;
    public float Hang => hang;
    public int Flights { get; private set; }
    public float Amplitude => Mathf.Max(0f, Mathf.Min(def.laneOffset, FrostFlight.Edge(def, .4f)));
    public float Height => ship.ZoneY(ship.ReachY(EliteSystem.ViewTop - def.topMargin));

    public CarrierBrain() { Id = "carrier"; }

    // "C" / "L" / "R": where it hangs this beat.
    public override void OnScriptMove(string token)
    {
        if (token == "C") hang = 0f;
        else if (token == "L") hang = -1f;
        else if (token == "R") hang = 1f;
    }

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
        trackX = Mathf.MoveTowards(trackX, hang * Amplitude, def.speed * .35f * dt);
        return new Vector2(trackX + Mathf.Sin(Clock * SlideRate) * .12f, Height + Mathf.Sin(Clock * .9f) * .1f);
    }

    public override float SpeedScale => fleeing ? FleeScale : .7f;
    public override float MinAttackAbove => 1.8f;

    public override bool WantsAttack(Vector2 seen)
    {
        var dd = ship.Attack as DroneDeployAttack;
        // (a vent beat goes off whatever its drones are doing; a launch beat waits for room)
        bool vent = FrostFlight.Is(ship.Script.Step.attack, "vent");
        return !fleeing && Pos.y > seen.y + MinAttackAbove && (vent || dd == null || dd.Live < dd.Cap);
    }

    public override Vector2 JoinFrom => Vector2.up;
}

// Whiteout Sentinel: a slow ironclad PATROLLING the top. Flies straight legs from one side of the board to the other along the top
// (topMargin under it, never above the player's reach), a rest at each end, at a steady slow pace, never dodging anything
// (Steadfast), its prow (and its plates) turned on the pilot. It does not come down to the pilot or toward its lane. It fires at the
// ENDS of its legs (WantsAttack: resting at one, or within EndHold of it) -- the anchor planted at the rail.
public class PatrolBrain : EliteBrain
{
    public const float Pace = .6f, Rest = 1.2f, EndMargin = .5f, EndHold = .9f;
    float side = 1f, rest;
    public float Side => side;
    public int Legs { get; private set; }
    public float Height => ship.ZoneY(ship.ReachY(EliteSystem.ViewTop - def.topMargin));
    public float EndX => side * FrostFlight.Edge(def, EndMargin);

    public PatrolBrain() { Id = "patrol"; }

    protected override void OnBind() { side = ship.Script.Alternate("side", -1f); }
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
    public override bool WantsAttack(Vector2 seen) =>
        Pos.y > seen.y + MinAttackAbove && (rest > 0f || Mathf.Abs(Pos.x) > FrostFlight.Edge(def, EndMargin) - EndHold);
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
        int toward = seen.x > 0f ? -1 : 1;
        // (scripted, from phase 2: the gap moves a hand each cast -- beside the pilot's slot on one side, then the other)
        if (FrostFlight.Declares(ship, "row") && ship.Script.Phase >= EliteScriptRunner.Phase2) toward = ship.Script.Alternate("gaphand", toward) > 0f ? 1 : -1;
        gap = Mathf.Clamp(mine + toward, 0, n);
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
        row.y = Mathf.Min(ship.AimSeen.y + def.lobAhead + EliteSystem.Scroll * settleAt, Mathf.Min(EliteSystem.ViewTop - .6f, ship.ZoneTop));
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
// A scripted Siren's step names it (variant "beam" / "twin"); an unscripted one beams every BeamEvery-th attack instead: a longer tell
// (BeamTell) in which a sight line paints the arc it will sweep -- BeamArc
// either side of the pilot -- then locks on its start; the action sweeps a
// stream of fast bolts (shotSpeed) across that arc. Get out of the arc.
// A "twin" bloom (unscripted: at the last heart, below DoubleBelow) is TWO orbs, OrbSplit degrees either side of the aim.
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
    public int Orbs => ship == null ? 1 : FrostFlight.Declares(ship, "twin") ? (FrostFlight.Is(ship.Script.VariantName, "twin") ? 2 : 1) : ship.Hearts < DoubleBelow ? 2 : 1;

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
        // (scripted: the step says -- a "beam" beat; unscripted: every third)
        beam = FrostFlight.Declares(ship, "beam") ? FrostFlight.Is(ship.Script.VariantName, "beam") : casts % BeamEvery == 0;
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
        int want = Mathf.Clamp(def.shotCount, 2, 4);   // (the script's variants say how many: pair 2, trio 3, all 4)
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
            line.transform.localScale = new Vector3(TelegraphStyle.ScaleX(TelegraphStyle.LineWidth), TelegraphStyle.ScaleY(to.magnitude), 1f);
            Color c = def.ShotColor;
            c.a = Mathf.FloorToInt((clock + i * .1f) / (5f * EliteArt.Tick)) % 2 == 0 ? TelegraphStyle.LineAlpha : TelegraphStyle.LineAlphaLow;
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
        shield.sortingOrder = EliteShip.PlayOrder + 2;
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
        sr.enabled = true;
        sr.transform.position = new Vector3(from.x, from.y, 0f);
        sr.transform.rotation = Quaternion.Euler(0f, 0f, deg - 90f);
        sr.transform.localScale = new Vector3(TelegraphStyle.ScaleX(TelegraphStyle.LineWidth), TelegraphStyle.ScaleY(ConeLength), 1f);
        Color c = def.ShotColor;
        c.a = on ? TelegraphStyle.LineAlpha : TelegraphStyle.LineAlphaLow;
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
// the WHOLE board, breaking every rock in the way (no heart lost) down to the bottom of the view; frost shards fly off the prow at the launch
// and off the first MaxPloughShards rocks it breaks, glancing off the rails (shotBounces). Anything else in the lane (an enemy, a
// mine) is a crash and costs it a heart: lure it. The run over, the brain U-turns it up the nearer rail to its next lane (IcebreakerBrain).
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
        // down to the bottom of the view (it never leaves it: the brain turns it up the rail)
        return ship.Position.y <= FrostFlight.RunEndY(def) || t >= MaxRunSeconds;
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
        int c = casts++;
        // (scripted: the step says -- a "skid" beat; unscripted: cast, skid, skid)
        skid = FrostFlight.Declares(ship, "skid") ? FrostFlight.Is(ship.Script.VariantName, "skid") : c % Every != 0;
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
        // (scripted: a "vent" beat vents when the shield is down; unscripted: every other attack while it is)
        if (FrostFlight.Declares(ship, "vent")) venting = !Linked && FrostFlight.Is(ship.Script.VariantName, "vent");
        else venting = !Linked && casts++ % 2 == 1;
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
    // (a scripted barrage beat names its lanes in shotCount; otherwise the lanes grow with the hearts lost)
    public int LaneCount => ship.Script.Varied ? Mathf.Clamp(def.shotCount, 3, MaxLanes) : Mathf.Min(MaxLanes, BaseLanes + (HeartsLost >= 2 ? 1 : 0) + (HeartsLost >= 4 ? 1 : 0));
    public int Columns => columns;
    public int Safe => safe;
    public int Lanes => lanes;
    public int Dropped => dropped;
    public bool Planned => planned;
    public float Gap => ship.Script.Varied ? Mathf.Max(SeqGapMin, def.shotInterval) : Mathf.Max(SeqGapMin, SeqGap - SeqGapStep * HeartsLost);
    public float Fall => ship.Script.Varied ? Mathf.Max(1f, def.shotSpeed) : FallSpeed + FallStep * HeartsLost;
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
        bool fromLeft = ship.Script.Alternate("sweep") > 0f;
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
        float top = Mathf.Min(EliteSystem.ViewTop - .6f, ship.ZoneTop), low = EliteSystem.ViewBottom + 1.1f;
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
