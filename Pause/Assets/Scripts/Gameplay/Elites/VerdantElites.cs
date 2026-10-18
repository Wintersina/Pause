using UnityEngine;

// Verdant's four new elites (Codex's flight strips, Art/Enemies/Elite/Verdant/new/manifest_new_elites.md), each its own brain and
// attack, joining the Resin Warden. They fly out of Verdant's own pads, every attack is the world's own material (a trunk, a vine,
// spores, a leaf blade) built from the attack cores already in the game (ShotMotion Roll / Burst / Slash, AttackLash), and every one
// draws its footprint from the first frame of its tell (AttackPreview: the same dotted pink-white outline the themed hazards use).
//
//   Timber Hauler  logger  / log_roll     (riverbay)   lobs trunks onto ringed spots; each lands, rolls down the board on a diagonal
//   Thornlash      thorn   / vine_lash    (towerbay)   a thorned vine whip swept across the lane (AttackLash), aimed once at the tell
//   Sporebloom     drifter / spore_burst  (podpad)     pods that burst into a ring of six spores; the burst rings are drawn first
//   Leafblade      diver   / leaf_dive    (roothangar) circles, then dives down a drawn lane and leaves a crescent across its row
//
// See docs/enemy-behaviours.md ("Verdant elites") for the design.

// ---- the telegraph -------------------------------------------------------------------------------------------------

// A dotted outline of what an attack is about to cover: rings (a landing spot, a burst), lanes (a roll path, a dive, a slash row).
// Built once per attack, shown from the first frame of its tell for the whole tell, riding the board when asked to.
public sealed class EliteTelegraph
{
    public const int RingPoints = 16;
    readonly AttackShape shape = new AttackShape();
    AttackPreview preview;
    float seconds;
    public bool Showing => preview != null && preview.Active;
    public int Loops => shape.LoopCount;
    public AttackShape Shape => shape;

    public void Clear() { End(); shape.Clear(); }

    public void Ring(Vector2 c, float r)
    {
        if (shape.LoopCount >= AttackShape.MaxLoops) return;
        shape.BeginLoop();
        for (int i = 0; i < RingPoints; i++)
        {
            float a = i * Mathf.PI * 2f / RingPoints;
            shape.LoopPoint(c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * (r * TelegraphStyle.RingScale));
        }
        shape.EndLoop();
    }

    // A lane: the rectangle `half` either side of a -> b.
    public void Lane(Vector2 a, Vector2 b, float half)
    {
        if (shape.LoopCount >= AttackShape.MaxLoops) return;
        Vector2 d = b - a;
        float m = d.magnitude;
        if (m < 1e-4f) return;
        Vector2 n = new Vector2(-d.y, d.x) / m * half;
        shape.BeginLoop();
        shape.LoopPoint(a + n); shape.LoopPoint(b + n); shape.LoopPoint(b - n); shape.LoopPoint(a - n);
        shape.EndLoop();
    }

    // `world`: whose cue colour the outline takes (Verdant's by default; Frost's elites pass their own).
    public void Show(float untilLive, Color shot, int world = 2)
    {
        End();
        seconds = untilLive;
        preview = AttackPreview.Show(shape, untilLive, HostileShotPalette.Body(shot, world), true);   // (bold: Verdant's canopy is bright)
    }

    public void Step(float dt) { if (preview != null) preview.Step(dt); }
    public void Follow(Vector2 delta) { if (preview != null) preview.Follow(delta); }
    public void GoLive() { if (preview != null) { preview.GoLive(); preview = null; } }
    public void End() { if (preview != null) { preview.End(); preview = null; } }
}

// ---- brains --------------------------------------------------------------------------------------------------------

// Timber Hauler: a lumbering log barge, a lane wall like the Kilnback but slower and a wider reach: it throws from anywhere over the pilot's lane.
public class LoggerBrain : HaulerBrain
{
    public LoggerBrain() { Id = "logger"; }
    public override float SpeedScale => .7f;
    public override bool WantsAttack(Vector2 seen) => Pos.y > seen.y + MinAttackAbove && Mathf.Abs(Pos.x - seen.x) < 1.7f;
}

// Thornlash: holds a lane beside the pilot like a gunship but lets its whip reach: any time it is within a body and a half of the pilot's height.
public class ThornBrain : GunshipBrain
{
    public ThornBrain() { Id = "thorn"; }
    public override bool WantsAttack(Vector2 seen) => Mathf.Abs(Pos.y - seen.y) < .95f && Mathf.Abs(Pos.x - Lane) < .7f;
}

// Sporebloom: a slow drifting platform high over the board, swaying side to side on top of the bastion's crawl.
public class DrifterBrain : BastionBrain
{
    public DrifterBrain() { Id = "drifter"; }
    public override Vector2 Goal(Vector2 seen, float dt)
    {
        Vector2 g = base.Goal(seen, dt);
        g.x += Mathf.Sin(Clock * .6f) * .55f;
        return g;
    }
    public override float SpeedScale => .65f;
}

// Leafblade: circles the pilot like a striker, quicker and tighter, then dives.
public class DiverBrain : StrikerBrain
{
    public DiverBrain() { Id = "diver"; }
    public override Vector2 JoinFrom => new Vector2(-1f, .3f);
}

// ---- attacks -------------------------------------------------------------------------------------------------------

// Timber Hauler: it plants itself, a row of ringed spots and the line each trunk will roll along is drawn at once (the whole
// wind-up), then it lobs shotCount trunks onto the spots, one after another out of its grabbers. A trunk lands, rolls down the
// board on a diagonal toward the nearer rail at 1.4 u/s (relative to the ground), glances off a rail once and is gone after five
// seconds. The spots are locked on screen when the tell begins (the lobs come down from above onto them as the board carries them); a pilot who stands on one or on a roll line is hit.
public class LogRollAttack : EliteAttack
{
    public const float LaneLength = 3.6f, PathHalf = .16f, SpotRing = .45f, MinSpotX = .32f;
    public const float RaftClear = 1.5f;   // a raft's gap: the free floor between the two trunks' roll lanes (the ship is .56 wide)
    readonly EliteTelegraph telegraph = new EliteTelegraph();
    Vector2 rowCentre;   // the row where the trunks LAND (on screen: the tell draws it there, locked)
    Vector2 liveRow;     // the lob's aim: that row plus the board's scroll over the flight, riding the board down onto it
    float next;
    int lobbed;
    bool fromLeft;
    bool raft;           // this cast is a RAFT (raft_roll): two trunks side by side, rolling down the same diagonal with one gap between them
    float raftGapX;      // the raft's gap: the middle of the free floor between its trunks
    float raftDir;       // +1 / -1: the way both trunks roll
    public LogRollAttack() { Id = "log_roll"; }
    // The raft_roll attack says when a cast is a raft; log_roll never is.
    protected virtual bool WantsRaft() => false;
    public bool Raft => raft;
    public float RaftGapX => raftGapX;
    public float RaftDirX => raftDir;
    // Half the width of a trunk's roll lane (what the telegraph outlines, what the gap is measured from).
    public float LaneHalf => def.shotSize * .4f + PathHalf;
    public override bool HoldsDuringTell => true;
    public Vector2 RowCentre => rowCentre;
    public int Lobbed => lobbed;
    public EliteTelegraph Telegraph => telegraph;

    public Vector2 Spot(int i) => SpotAt(rowCentre, i);
    public int SpotCount => raft ? 2 : Mathf.Max(1, def.shotCount);

    Vector2 SpotAt(Vector2 centre, int i)
    {
        if (raft)
        {
            // (the two trunks of a raft: either side of the gap, a lane's half-width plus half the gap from its middle)
            float off = RaftClear * .5f + LaneHalf;
            return new Vector2(raftGapX + (i == 0 ? -off : off), centre.y);
        }
        int n = Mathf.Max(1, def.shotCount);
        float x = centre.x + (i - (n - 1) * .5f) * def.lobSpacing;
        float edge = Mathf.Max(MinSpotX, EliteSystem.RailEdge - def.shotSize - .1f);
        x = Mathf.Clamp(x, -edge, edge);
        // (a spot on the middle line would roll either way: it is nudged to a side, so its roll can be drawn)
        if (Mathf.Abs(x) < MinSpotX) x = x >= 0f ? MinSpotX : -MinSpotX;
        return new Vector2(x, centre.y);
    }

    // The way a trunk rolls on from a spot (board frame), the same rule EliteShot uses.
    public static Vector2 RollDir(float spotX)
    {
        float dx = spotX > .25f ? 1f : -1f;
        return new Vector2(dx * ShotMotions.RollSpeed * .65f, -ShotMotions.RollSpeed * .76f).normalized;
    }

    // The roll's path from a spot: out along the diagonal, bouncing off the rail once (board frame), as the dotted lanes.
    void DrawRoll(Vector2 spot)
    {
        Vector2 p = spot, d = raft ? new Vector2(raftDir * ShotMotions.RollSpeed * .65f, -ShotMotions.RollSpeed * .76f).normalized : RollDir(spot.x);
        float left = LaneLength, edge = EliteSystem.RailEdge;
        for (int leg = 0; leg < 2 && left > .05f; leg++)
        {
            float toRail = d.x > 0f ? (edge - p.x) / d.x : (-edge - p.x) / d.x;
            float run = Mathf.Min(left, Mathf.Max(0f, toRail));
            Vector2 q = p + d * run;
            telegraph.Lane(p, q, PathHalf + def.shotSize * .4f);
            left -= run;
            p = q;
            d = new Vector2(-d.x, d.y);
        }
    }

    public override void BeginTell(Vector2 seen)
    {
        base.BeginTell(seen);
        // where the trunks will come down: lobAhead of the pilot, on screen, drawn there for the whole tell
        rowCentre = seen + Vector2.up * def.lobAhead;
        telegraph.Clear();
        raft = WantsRaft();
        if (raft) PlanRaft(seen);
        int n = SpotCount;
        for (int i = 0; i < n; i++)
        {
            Vector2 s = Spot(i);
            telegraph.Ring(s, SpotRing);
            DrawRoll(s);
        }
        telegraph.Show(TellSeconds, def.ShotColor);
    }

    // The raft's gap: beside the pilot toward the middle of the board (so the way through is a step, not a run), kept wholly between
    // the rails with both trunks; both trunks roll toward the side with the most floor.
    void PlanRaft(Vector2 seen)
    {
        float reach = RaftClear * .5f + LaneHalf * 2f + .15f;
        float lim = Mathf.Max(0f, EliteSystem.RailEdge - reach);
        raftGapX = Mathf.Clamp(seen.x + (seen.x >= 0f ? -1f : 1f) * .9f, -lim, lim);
        raftDir = raftGapX >= 0f ? -1f : 1f;
    }

    public override void StepTell(float dt)
    {
        t += dt;
        telegraph.Step(dt);
    }

    public override void Cancel() { telegraph.End(); }
    public override void OnDeath() { telegraph.End(); }

    public override void BeginAction()
    {
        base.BeginAction();
        telegraph.GoLive();
        next = 0f;
        lobbed = 0;
        fromLeft = ship.Position.x < rowCentre.x;
        liveRow = rowCentre + Vector2.up * (EliteSystem.Scroll * def.lobSeconds);
    }

    public override bool StepAction(float dt)
    {
        t += dt;
        liveRow.y -= EliteSystem.Scroll * dt;   // (the aim rides the board down onto the row)
        int n = SpotCount;
        while (t >= next && lobbed < n)
        {
            int spot = fromLeft ? lobbed : n - 1 - lobbed;
            int m = def.muzzles.Length > 0 ? lobbed % def.muzzles.Length : 0;
            Vector2 to = SpotAt(liveRow, spot);
            var shot = Fire(m, Deg(to - ship.MuzzleWorld(m)), 0f);
            if (shot != null)
            {
                shot.Lob(to, def.lobSeconds - next);   // (the later trunks come down with the first: every spot lands on time)
                if (raft) shot.RollToward(raftDir);
            }
            lobbed++;
            next += raft ? 0f : def.shotInterval;   // (a raft's two trunks leave together)
        }
        return t >= def.actionSeconds && lobbed >= n;
    }

    public override void End() { telegraph.End(); }
}

// Thornlash: it holds still while its coiled whip root swells, and the whole swept area -- the start line, the arc of the tip and the
// end line -- is drawn from the first frame of the tell (AttackLash's own preview). The sweep is aimed once, at the pilot's spot when the
// tell begins, from the root nearer to it; it sweeps across that spot in AttackLash's fair way (a corridor on the far side stays open).
public class VineLashAttack : EliteAttack
{
    AttackLash lash;
    bool lit;
    public VineLashAttack() { Id = "vine_lash"; }
    public override bool HoldsDuringTell => true;
    public override bool Shoots => false;   // (a whip, not shots)
    public AttackLash Lash => lash;
    public bool Lit => lit;

    int Root(Vector2 seen)
    {
        int best = 0;
        float bd = float.MaxValue;
        for (int m = 0; m < def.muzzles.Length; m++)
        {
            float d = ((Vector2)ship.MuzzleWorld(m) - seen).sqrMagnitude;
            if (d < bd) { bd = d; best = m; }
        }
        return best;
    }

    public override void BeginTell(Vector2 seen)
    {
        base.BeginTell(seen);
        lit = false;
        int m = Root(seen);
        var spec = LashSpec.Standard(Mathf.Max(0, def.WorldIndex));
        if (def.hazardSeconds > .01f) spec.sweepSeconds = def.hazardSeconds;
        if (def.hazardSize >= AttackLash.MinLength) spec.length = def.hazardSize;   // (the whip's reach: the pilot's range + 1.6 u when longer)
        Vector2 at = ship.MuzzleWorld(m);
        lash = AttackLash.Arm(spec, at, seen, TellSeconds, ship.gameObject);
        if (lash != null) lash.Follow(ship.transform, at - ship.Position);
        rootMuzzle = m;
    }

    int rootMuzzle;

    public override void Cancel() { Drop(); }
    public override void OnDeath() { Drop(); }

    void Drop()
    {
        if (lash != null && lash.State == AttackHazard.Phase.Tell) lash.Cancel();
        lash = null;
    }

    public override void BeginAction()
    {
        base.BeginAction();
        if (lash != null)
        {
            lash.Ignite();
            lit = true;
            Fired++;
            ship.OnFired(rootMuzzle, Deg(lash.Root - ship.Position));
        }
    }

    public override bool StepAction(float dt)
    {
        t += dt;
        return t >= def.actionSeconds;
    }

    public override void End() { lash = null; }
}

// Sporebloom: it holds while its pods swell. The burst points (a ring as wide as the spores reach) and the line each pod flies are drawn
// from the first frame of the tell. Then shotCount pods leave its side vents, one after another, each flying to its own burst point
// in ShotMotions.BurstSeconds and opening there into six spores in a ring, drifting out at 1.6 u/s. The burst points are set either
// side of the pilot's spot, so the cloud of spores (not the pods, which are slow) is what must be read.
public class SporeBurstAttack : EliteAttack
{
    public const float Reach = ShotMotions.SporeSpeed * ShotMotions.SporeSeconds;   // how far a spore flies
    protected readonly EliteTelegraph telegraph = new EliteTelegraph();
    Vector2[] burst = new Vector2[4];
    float next;
    int sent;
    public SporeBurstAttack() { Id = "spore_burst"; }
    public override bool HoldsDuringTell => true;
    public int Sent => sent;
    public EliteTelegraph Telegraph => telegraph;
    public Vector2 Burst(int i) => burst[Mathf.Clamp(i, 0, burst.Length - 1)];

    int Count => Mathf.Clamp(def.shotCount, 1, burst.Length);

    // Where pod i bursts: spread across the pilot's spot by lobSpacing, a little above it (the spores drift out and down onto it).
    Vector2 Place(Vector2 seen, int i)
    {
        int n = Count;
        float x = seen.x + (i - (n - 1) * .5f) * def.lobSpacing;
        float edge = Mathf.Max(.2f, EliteSystem.RailEdge - .35f);
        return new Vector2(Mathf.Clamp(x, -edge, edge), seen.y + def.lobAhead);
    }

    public override void BeginTell(Vector2 seen)
    {
        base.BeginTell(seen);
        telegraph.Clear();
        for (int i = 0; i < Count; i++)
        {
            burst[i] = Place(seen, i);
            Vector2 from = ship.MuzzleWorld(i % Mathf.Max(1, def.muzzles.Length));
            telegraph.Lane(from, burst[i], def.shotSize * .5f + .06f);
            telegraph.Ring(burst[i], Reach + .2f);
        }
        DrawMore(seen);
        telegraph.Show(TellSeconds, def.ShotColor);
    }

    // The spore_drift attack adds its cloud's outline here (before the telegraph goes up).
    protected virtual void DrawMore(Vector2 seen) { }

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
            int m = sent % Mathf.Max(1, def.muzzles.Length);
            Vector2 from = ship.MuzzleWorld(m);
            Vector2 d = burst[sent] - from;
            float speed = d.magnitude / ShotMotions.BurstSeconds;
            Fire(m, Deg(d), speed);
            sent++;
            next += def.shotInterval;
        }
        return t >= def.actionSeconds && sent >= Count;
    }

    public override void End() { telegraph.End(); }
}

// Leafblade: it locks the pilot's spot, holds a breath while the blades spread (the lane it will dive down and the row its crescent will
// cross are drawn at once), then dives through the spot at dashSpeed -- committed, no steering -- and, as it passes through, a crescent
// leaves its blade along the row, crossing the lane lengthwise at 4.5 u/s. A sidestep dodges the dive; the row needs a step up or down.
public class LeafDiveAttack : EliteAttack
{
    public const float DiveHalfPad = .22f, SlashHalf = .18f;
    readonly EliteTelegraph telegraph = new EliteTelegraph();
    bool slashed;
    float slashDeg;
    Vector2 diveFrom;
    public LeafDiveAttack() { Id = "leaf_dive"; }
    public override bool HoldsDuringTell => true;
    public override bool DrivesMovement => true;
    public override float? FaceDeg => Deg(dir);
    public EliteTelegraph Telegraph => telegraph;
    public bool Slashed => slashed;
    public Vector2 DiveFrom => diveFrom;
    public float SlashDeg => slashDeg;
    public float SlashRowY => aim.y;

    // (it dives at the spot it locked, from where it held)
    public override void DashLine(Vector2 seen, bool locked, out Vector2 d, out float reach)
    {
        Vector2 to = aim - ship.Position;
        reach = to.magnitude + DashPast;
        d = to.sqrMagnitude > 1e-4f ? to.normalized : Vector2.down;
    }

    // Which way the crescent crosses the lane: toward the side with the most floor (away from the nearer rail).
    public static float SlashHeading(float aimX) => aimX >= 0f ? 180f : 0f;

    public override void BeginTell(Vector2 seen)
    {
        base.BeginTell(seen);
        slashed = false;
        slashDeg = SlashHeading(aim.x);
        // where it will have come to rest by the end of the tell (it brakes from its circling speed at its own accel): the lane is drawn from there
        Vector2 v = ship.Velocity;
        Vector2 from = ship.Position + v * v.magnitude / (2f * Mathf.Max(1f, def.accel));
        diveFrom = from;
        Vector2 to = aim - from;
        Vector2 d = to.sqrMagnitude > 1e-4f ? to.normalized : Vector2.down;
        telegraph.Clear();
        telegraph.Lane(from, aim + d * (.5f + .25f), def.hullRadius + DiveHalfPad);
        // the row the crescent crosses: from the end of the dive to the far rail
        float edge = EliteSystem.RailEdge;
        Vector2 rowFrom = new Vector2(aim.x, aim.y), rowTo = new Vector2(slashDeg > 90f ? -edge : edge, aim.y);
        telegraph.Lane(rowFrom, rowTo, SlashHalf);
        telegraph.Show(TellSeconds, def.ShotColor);
    }

    public override void StepTell(float dt) { t += dt; telegraph.Step(dt); }
    public override void Cancel() { telegraph.End(); }
    public override void OnDeath() { telegraph.End(); }

    public override void BeginAction()
    {
        base.BeginAction();
        telegraph.GoLive();
        slashed = false;
        Vector2 d = aim - ship.Position;
        dir = d.sqrMagnitude > 1e-4f ? d.normalized : Vector2.down;
        ship.Drive(dir * def.dashSpeed);
    }

    float slashT;

    public override bool StepAction(float dt)
    {
        t += dt;
        if (!slashed)
        {
            ship.Drive(dir * def.dashSpeed);
            Vector2 to = aim - ship.Position;
            bool through = Vector2.Dot(to, dir) <= 0f;
            if (through || t >= def.actionSeconds)
            {
                slashed = true;
                slashT = t;
                // out of the blade on the side the crescent leaves (its muzzle 1 = left blade, 2 = right: the blade on the side it heads for, so the whole crescent starts ahead of the spot), along the row it was drawn on
                int m = def.muzzles.Length > 2 ? (slashDeg > 90f ? 1 : 2) : 0;
                var shot = Fire(m, slashDeg, def.shotSpeed);
                if (shot != null)
                {
                    // from the row it drew, not where the hull has got to
                    Vector3 p = shot.transform.position;
                    shot.transform.position = new Vector3(p.x, aim.y, p.z);
                }
            }
            return false;
        }
        // the dive ends where it was drawn to end: a quick stop just past the locked spot, never a coast down the board
        ship.Drive(dir * def.dashSpeed * .12f);
        return t >= slashT + StopSeconds;
    }

    public const float StopSeconds = .3f;

    public override void End()
    {
        telegraph.End();
        var s = ship.Brain as StrikerBrain;
        if (s != null) s.ResetLap();
    }
}

// ====================================================================================================================================
// ---- the mini-boss rework (Verdant's five, one new brain and attack each; the cores above are reused) -----------------------------
// ====================================================================================================================================
//
// The rules they share (as Space's and Frost's): none of them follows the pilot -- they keep their distance and antagonise; each throws
// HAZARDS in the jungle's own material (trunks, vines, resin, spores, leaf blades; the cue colours are Verdant's, never pink); every
// attack is drawn from the first frame of its tell (EliteTelegraph / AttackLash's own preview) and its aim LOCKS there; they launch
// from Verdant's own pads, one kind each (the backdrop brings the pad into view: VerdantDirector.BringHome).
//
//   Leafblade      raptor   / leaf_swoop    (roothangar) SWOOPING PASSES: in from a side or the top, a diagonal lane drawn, sliced along
//                                                  it and out the far side; a crescent is left across a drawn row (two once hurt)
//   Resin Warden   canopy   / canopy_mortar (hatch)      holds the top, HOPS between four canopy perches; a wall of resin pools with one
//                                                  gap: the pools linger longer and the gap narrows for every heart lost
//   Sporebloom     floater  / spore_drift   (podpad)     floats high and slow, then SINKS off its side into the canopy and comes back up
//                                                  elsewhere; pods that burst into spore rings; at two hearts a lingering spore cloud lane
//   Thornlash      anchor   / thorn_sweep   (towerbay)   latches on just outside a rail and whips the thorned vine across the lane (the
//                                                  fair corridor of AttackLash), swings across the top to the other rail and whips there;
//                                                  one more sweep per anchor for every heart lost
//   Timber Hauler  lumber   / raft_roll     (riverbay)   a lumber barge on the top rail, never chasing: trunks onto ringed spots, and at
//                                                  two hearts a log RAFT -- two trunks rolling down side by side with one gap
//
// The Leafblade, Sporebloom and Thornlash ROAM (EliteBrain.Roams): their paths leave the view and they are untouchable while outside the
// board. The first four are not armoured (3 hearts, they hide when hurt: EliteDef.hurtHide); the Hauler is (5 hearts, no hiding).

// ---- Leafblade: raptor + leaf_swoop --------------------------------------------------------------------------------------------

// A raptor making PASSES. One pass is: it comes in from a side of the board (or over the top) and takes up its start point just
// inside that edge, wholly in sight (the lane is drawn from the first frame of the tell, LeafSwoopAttack), slices along the lane at
// dashSpeed and out of the far side; then it waits PauseSeconds (a little less for every heart lost) off the view and re-enters
// from ANOTHER of the three entries. It never circles, never comes down to the pilot and never stops on the board between passes.
public class RaptorBrain : EliteBrain
{
    public enum Stage { Leave, Away, Enter, Ready, Pass }
    public const int Entries = 3;   // 0 the left edge, 1 the right edge, 2 the top
    public const float PauseSeconds = 1.5f, PausePerHeart = .3f, MinPause = .7f, EnterScale = 2.4f, ReadyWait = .1f, SettleDistance = .15f;
    Stage stage = Stage.Leave;
    int entry = -1, lastEntry = -1;
    float wait, ready;
    Vector2 readyAt;
    public Stage Phase => stage;
    public int Entry => entry;
    public int LastEntry => lastEntry;
    public Vector2 ReadyPoint => readyAt;
    public int Passes { get; private set; }
    public int PassesStarted { get; private set; }
    // Crescents a pass leaves: one, two once it has lost a heart.
    public int Crescents => ship != null && ship.Hearts < def.hearts ? 2 : 1;
    public float PauseNow => Mathf.Max(MinPause, PauseSeconds - PausePerHeart * (ship != null ? Mathf.Max(0, def.hearts - ship.Hearts) : 0));

    public RaptorBrain() { Id = "raptor"; }

    public override bool Roams => true;
    public override float TellScale => 1f;
    public override Vector2 JoinFrom => Vector2.up;
    public override void OnJoin() { stage = Stage.Leave; lastEntry = -1; }
    public override void OnReturn() { BeginEnter(); }

    // The next entry (never the last one's) and where the Leafblade starts from: a side's edge of the board at a height in the upper
    // part of the view, or the top at a column; it is placed beyond the view and flies in to it.
    void BeginEnter()
    {
        int pick = Random.Range(0, Entries);
        for (int guard = 0; pick == lastEntry && guard < 12; guard++) pick = Random.Range(0, Entries);
        if (pick == lastEntry) pick = (pick + 1) % Entries;
        entry = pick;
        Vector2 from;
        if (entry == 2)
        {
            float edge = FrostFlight.Edge(def, .5f);
            readyAt = new Vector2(Random.Range(-edge, edge), EliteSystem.ViewTop - def.hullRadius * .3f);
            from = new Vector2(readyAt.x, FrostFlight.AboveY(def));
        }
        else
        {
            float sx = entry == 0 ? -1f : 1f;
            float y = Random.Range(EliteSystem.ViewTop - 3.6f, EliteSystem.ViewTop - 1.5f);
            readyAt = new Vector2(sx * (EliteSystem.RailEdge + .05f), y);   // (a hull's width partly off the board)
            from = new Vector2(sx * SpaceFlight.OffX(def), y);
        }
        ship.Warp(from);
        stage = Stage.Enter;
        ready = 0f;
    }

    public override Vector2 Goal(Vector2 seen, float dt)
    {
        Clock += dt;
        switch (stage)
        {
            case Stage.Leave:
                // just lifted off a pad: up and out of the view, then the first pass
                if (Pos.y > FrostFlight.AboveY(def) - .2f) { stage = Stage.Away; wait = .5f; }
                return new Vector2(Pos.x, FrostFlight.AboveY(def) + .6f);
            case Stage.Away:
                wait -= dt;
                if (wait <= 0f) BeginEnter();
                return Pos;
            case Stage.Enter:
                if ((Pos - readyAt).sqrMagnitude < SettleDistance * SettleDistance) { stage = Stage.Ready; ready = 0f; }
                return readyAt;
            case Stage.Ready:
                ready += dt;
                return readyAt;
            default:
                return Pos;
        }
    }

    public override float SpeedScale => stage == Stage.Enter || stage == Stage.Leave ? EnterScale : 1f;
    public override bool WantsAttack(Vector2 seen) => stage == Stage.Ready && ready >= ReadyWait;
    // nose along the way it will go: down and in from a side, straight down from the top
    public override float? FaceDeg(Vector2 seen) => stage == Stage.Leave ? (float?)null : entry == 0 ? -35f : entry == 1 ? -145f : -90f;

    public void PassStarted() { stage = Stage.Pass; PassesStarted++; }

    public void PassDone()
    {
        Passes++;
        lastEntry = entry;
        stage = Stage.Away;
        wait = PauseNow;
    }
}

// Leafblade: a swooping pass. From where it waits (RaptorBrain.ReadyPoint: an edge of the board, wholly in sight) a DIAGONAL LANE is
// drawn through the board toward the pilot's spot -- the angle clamped to DiveMinDeg..DiveMaxDeg off the horizontal (a side entry) or
// MinFromVertical..MaxFromVertical off the vertical (the top), the lane's end at the far edge of the view -- and, across it, the row(s)
// its crescent will take: where the lane crosses the pilot's height, one crescent, and RowGap lower a second once it has lost a heart.
// All of it is locked at the tell. The action slices along the lane at dashSpeed -- committed, no steering, so a sidestep dodges it --
// and as the blade crosses a row a leaf crescent leaves it, level along the drawn row toward the side with the most floor, 4.5 u/s. It
// leaves the view by the far edge; the brain brings it back in by another entry. Rows hold to the part of the lane inside the board.
public class LeafSwoopAttack : EliteAttack
{
    public const float DiveHalfPad = .22f, SlashHalf = .18f, RowGap = 1.5f, EdgeIn = .45f, CheckAhead = 3f, MaxRunSeconds = 8f;
    public const float SideMinDeg = 15f, SideMaxDeg = 60f, TopMinDeg = 15f, TopMaxDeg = 50f;   // the lane's slope bounds
    readonly EliteTelegraph telegraph = new EliteTelegraph();
    readonly Vector2[] rowAt = new Vector2[2];
    readonly float[] rowDeg = new float[2];
    Vector2 start, end;
    int rows, fired;
    bool ran;
    public LeafSwoopAttack() { Id = "leaf_swoop"; }
    public override bool HoldsDuringTell => true;
    public override bool DrivesMovement => true;
    public override float? FaceDeg => Deg(dir);
    public EliteTelegraph Telegraph => telegraph;
    public Vector2 LaneStart => start;
    public Vector2 LaneEnd => end;
    public int Rows => rows;
    public int RowsFired => fired;
    public Vector2 RowAt(int i) => rowAt[Mathf.Clamp(i, 0, 1)];
    public float RowDeg(int i) => rowDeg[Mathf.Clamp(i, 0, 1)];

    RaptorBrain Raptor => ship.Brain as RaptorBrain;

    // The lane's direction from `s` for a pass entered by `entry` (0 left, 1 right, 2 top) at the pilot `seen`.
    public static Vector2 LaneDir(EliteDef def, Vector2 s, int entry, Vector2 seen)
    {
        float edge = FrostFlight.Edge(def, .5f);
        float ty = Mathf.Clamp(seen.y + 1f, EliteSystem.ViewBottom + 1.6f, Mathf.Max(EliteSystem.ViewBottom + 1.7f, s.y - 1.5f));
        Vector2 to = new Vector2(Mathf.Clamp(seen.x, -edge, edge), ty) - s;
        if (entry < 2)
        {
            float sg = s.x < 0f ? 1f : -1f;   // (inward from the edge it starts on)
            float th = Mathf.Clamp(Mathf.Atan2(-to.y, Mathf.Max(.01f, to.x * sg)) * Mathf.Rad2Deg, SideMinDeg, SideMaxDeg) * Mathf.Deg2Rad;
            return new Vector2(sg * Mathf.Cos(th), -Mathf.Sin(th));
        }
        float side = Mathf.Abs(to.x) > .2f ? Mathf.Sign(to.x) : (s.x > 0f ? -1f : 1f);
        float ph = Mathf.Clamp(Mathf.Atan2(Mathf.Abs(to.x), Mathf.Max(.01f, -to.y)) * Mathf.Rad2Deg, TopMinDeg, TopMaxDeg) * Mathf.Deg2Rad;
        return new Vector2(side * Mathf.Sin(ph), -Mathf.Cos(ph));
    }

    int EntryNow
    {
        get
        {
            var rb = Raptor;
            if (rb != null && rb.Entry >= 0) return rb.Entry;
            Vector2 p = ship.Position;
            return p.y > EliteSystem.ViewTop - def.hullRadius ? 2 : p.x < 0f ? 0 : 1;
        }
    }

    // (the lane is straight down from nowhere: an attack that has not planned yet checks the way it would take)
    public override void DashLine(Vector2 seen, bool locked, out Vector2 d, out float reach)
    {
        reach = CheckAhead;
        d = locked && dir.sqrMagnitude > 1e-4f ? dir : LaneDir(def, ship.Position, EntryNow, seen);
    }

    public override void BeginTell(Vector2 seen)
    {
        base.BeginTell(seen);
        ran = false;
        fired = 0;
        start = ship.Position;
        dir = LaneDir(def, start, EntryNow, seen);
        aim = start + dir * 2f;
        float rail = EliteSystem.RailEdge, edgeIn = rail - EdgeIn;
        // the lane out to the edge of the view (the far rail or the bottom)
        float tx = dir.x > .001f ? (rail + .1f - start.x) / dir.x : dir.x < -.001f ? (-rail - .1f - start.x) / dir.x : 99f;
        float ty = (EliteSystem.ViewBottom - .3f - start.y) / dir.y;
        end = start + dir * Mathf.Min(tx, ty);
        // the part of it inside the board proper (rows are drawn there), and the point of it nearest where the pilot stands
        float tIn = Mathf.Abs(start.x) > edgeIn && Mathf.Abs(dir.x) > .001f ? (Mathf.Abs(start.x) - edgeIn) / Mathf.Abs(dir.x) : 0f;
        float outX = dir.x > .001f ? (edgeIn - start.x) / dir.x : dir.x < -.001f ? (-edgeIn - start.x) / dir.x : 99f;
        float outY = (EliteSystem.ViewBottom + 1.2f - start.y) / dir.y;
        float tOut = Mathf.Min(outX, outY);
        var rb = Raptor;
        rows = rb != null ? rb.Crescents : 1;
        float step = RowGap / Mathf.Max(.2f, -dir.y);
        float tp = Vector2.Dot(seen + Vector2.up * 1f - start, dir);
        float lo = tIn + .4f, hi = Mathf.Max(lo, tOut - (rows - 1) * step - .2f);
        tp = Mathf.Clamp(tp, lo, hi);
        telegraph.Clear();
        telegraph.Lane(start, end, def.hullRadius + DiveHalfPad);
        for (int i = 0; i < rows; i++)
        {
            Vector2 p = start + dir * (tp + i * step);
            rowAt[i] = p;
            rowDeg[i] = LeafDiveAttack.SlashHeading(p.x);
            telegraph.Lane(p, new Vector2(rowDeg[i] > 90f ? -rail : rail, p.y), SlashHalf);
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
        ran = true;
        fired = 0;
        ship.Drive(dir * def.dashSpeed);
        var rb = Raptor;
        if (rb != null) rb.PassStarted();
    }

    public override bool StepAction(float dt)
    {
        t += dt;
        ship.Drive(dir * def.dashSpeed);
        // as the blade crosses a drawn row its crescent leaves, level along the row
        while (fired < rows && Vector2.Dot((Vector2)ship.Position - rowAt[fired], dir) >= 0f)
        {
            int m = def.muzzles.Length > 2 ? (rowDeg[fired] > 90f ? 1 : 2) : 0;
            var shot = Fire(m, rowDeg[fired], def.shotSpeed);
            if (shot != null)
            {
                Vector3 p = shot.transform.position;
                float lim = EliteSystem.RailEdge - .3f;   // (a crescent that starts on a rail is gone at once: it starts between them)
                shot.transform.position = new Vector3(Mathf.Clamp(p.x, -lim, lim), rowAt[fired].y, p.z);   // (on the row it drew, not where the hull has got to)
            }
            fired++;
        }
        Vector2 q = ship.Position;
        // out of the view by the far edge (or the bottom)
        return q.y < FrostFlight.BelowY(def) || Mathf.Abs(q.x) > SpaceFlight.OffX(def) || t >= MaxRunSeconds;
    }

    public override void End()
    {
        telegraph.End();
        var rb = Raptor;
        if (ran && rb != null) rb.PassDone();
        ran = false;
    }
}

// ---- Resin Warden: canopy + canopy_mortar ---------------------------------------------------------------------------------------

// A mortar on the canopy. It holds topMargin under the top of the view (never above the player's reach) on one of four perches
// spread along the top and does NOT follow the pilot: after each mortar, or when PerchMax has passed, it HOPS to another perch -- a
// short visible leap (HopScale x its speed, an arc HopArc high), preferring a perch HopAway or more from the pilot's column.
public class CanopyBrain : EliteBrain
{
    public enum Stage { Perch, Hop }
    public const float PerchWait = .55f, PerchMax = 4.5f, HopScale = 2.6f, HopMin = .45f, HopSpeed = 5.5f, HopArc = .7f, HopAway = 1f, SettleDistance = .25f;
    static readonly float[] Columns = { -.82f, -.28f, .28f, .82f };
    Stage stage = Stage.Perch;
    int perch = -1, lastPerch = -1;
    float perched, hopT, hopLen;
    Vector2 hopFrom;
    public Stage Phase => stage;
    public int Perch => perch;
    public int Hops { get; private set; }
    public int PerchCount => Columns.Length;
    public float HopSeconds => hopLen;

    public CanopyBrain() { Id = "canopy"; }

    public override Vector2 JoinFrom => Vector2.up;
    public override void OnJoin() { perch = Nearest(Pos.x); stage = Stage.Perch; perched = 0f; }
    public override void OnReturn() { perch = Nearest(Pos.x); stage = Stage.Perch; perched = 0f; }

    int Nearest(float x)
    {
        float edge = FrostFlight.Edge(def, .4f);
        int best = 0;
        for (int i = 1; i < Columns.Length; i++) if (Mathf.Abs(Columns[i] * edge - x) < Mathf.Abs(Columns[best] * edge - x)) best = i;
        return best;
    }

    // Where perch `i` is: its column, topMargin under the top (inside the player's reach) less the height of a hop's arc, so the top of a
    // leap reaches that line and the player's reach cap never flattens it.
    public Vector2 Station(int i) =>
        new Vector2(Columns[Mathf.Clamp(i, 0, Columns.Length - 1)] * FrostFlight.Edge(def, .4f), ship.ReachY(EliteSystem.ViewTop - def.topMargin) - HopArc);

    void BeginHop()
    {
        var f = ship.Follow;
        int pick = -1;
        float edge = FrostFlight.Edge(def, .4f);
        for (int tries = 0; tries < 8 && pick < 0; tries++)
        {
            int c = Random.Range(0, Columns.Length);
            if (c == perch || (c == lastPerch && tries < 5)) continue;
            if (Mathf.Abs(Columns[c] * edge - f.x) < HopAway && tries < 6) continue;
            pick = c;
        }
        if (pick < 0) pick = (perch + 1) % Columns.Length;
        lastPerch = perch;
        perch = pick;
        hopFrom = Pos;
        hopLen = Mathf.Max(HopMin, Vector2.Distance(Pos, Station(perch)) / HopSpeed);
        hopT = 0f;
        stage = Stage.Hop;
        Hops++;
    }

    public override Vector2 Goal(Vector2 follow, float dt)
    {
        Clock += dt;
        if (perch < 0) perch = Nearest(Pos.x);
        Vector2 st = Station(perch);
        if (stage == Stage.Perch)
        {
            perched += dt;
            if (perched > PerchMax) BeginHop();
            return st + new Vector2(Mathf.Sin(Clock * 1.3f) * .08f, Mathf.Sin(Clock * .9f) * .08f);
        }
        hopT += dt;
        float k = Mathf.Clamp01(hopT / hopLen);
        if (k >= 1f && ((Pos - st).sqrMagnitude < SettleDistance * SettleDistance || hopT > hopLen + 1f)) { stage = Stage.Perch; perched = 0f; return st; }
        return Vector2.Lerp(hopFrom, st, k) + Vector2.up * (Mathf.Sin(k * Mathf.PI) * HopArc);
    }

    public override float SpeedScale => stage == Stage.Hop ? HopScale : 1f;
    public override float MinAttackAbove => 1.8f;
    public override bool WantsAttack(Vector2 seen) => stage == Stage.Perch && perched > PerchWait && Pos.y > seen.y + MinAttackAbove;

    // The mortar is done: it hops.
    public void MortarDone() { if (stage == Stage.Perch) BeginHop(); }
}

// Resin Warden: a wall of resin pools with ONE gap. The tell plants it and draws a ring on every spot of the row (lobAhead in front
// of the pilot, locked on screen) -- globs from the wall's gap outward to both rails, lobSpacing apart (pools overlap: no way through
// but the gap) -- and the action lobs them one after another out of its pods; each lands as a sticky pool that rides the board.
// The gap sits beside the pilot toward the middle (GapShift away), GapBase wide between the flanking pools, and NARROWS GapPerHeart for
// every heart lost (never under GapMin: the ship is .56 wide); the pools LINGER LingerPerHeart (share of poolSeconds) longer for every
// heart lost. Shoot a glob and it is gone; the gap is always there.
public class CanopyMortarAttack : EliteAttack
{
    public const float GapBase = 1.8f, GapPerHeart = .35f, GapMin = 1.1f, GapShift = 1f, LingerPerHeart = .35f;
    public const int MaxGlobs = 8;
    readonly EliteTelegraph telegraph = new EliteTelegraph();
    readonly float[] xs = new float[MaxGlobs];
    readonly float[] left = new float[MaxGlobs / 2], right = new float[MaxGlobs / 2];
    int count;
    float gapX, gapClear, linger, next;
    int lobbed;
    bool fromLeft;
    Vector2 rowCentre, liveRow;
    public CanopyMortarAttack() { Id = "canopy_mortar"; }
    public override bool HoldsDuringTell => true;
    public EliteTelegraph Telegraph => telegraph;
    public int Count => count;
    public int Lobbed => lobbed;
    public float GapX => gapX;
    public float GapWidth => gapClear;
    public float LingerSeconds => linger;
    public float RowY => rowCentre.y;
    public Vector2 Spot(int i) => new Vector2(xs[Mathf.Clamp(i, 0, MaxGlobs - 1)], rowCentre.y);
    public float PoolRadius => def.shotSize * 2.1f * .42f;
    public static float GapFor(int lost) => Mathf.Max(GapMin, GapBase - GapPerHeart * Mathf.Max(0, lost));
    int Lost => Mathf.Max(0, def.hearts - ship.Hearts);

    public override void BeginTell(Vector2 seen)
    {
        base.BeginTell(seen);
        rowCentre = seen + Vector2.up * def.lobAhead;
        gapClear = GapFor(Lost);
        linger = def.poolSeconds * LingerPerHeart * Lost;
        float r = PoolRadius, rail = EliteSystem.RailEdge, sp = Mathf.Max(.3f, def.lobSpacing);
        float lim = Mathf.Max(0f, rail - 2f * r - gapClear * .5f - .05f);   // (both flanking pools wholly between the rails: a pool on the rail is gone)
        gapX = Mathf.Clamp(seen.x + (seen.x >= 0f ? -1f : 1f) * GapShift, -lim, lim);
        // the wall either side of the gap, innermost pool first, until the last one is up against the rail
        int nl = Flank(left, gapX - gapClear * .5f - r, -1f, sp, r, rail);
        int nr = Flank(right, gapX + gapClear * .5f + r, 1f, sp, r, rail);
        count = 0;
        for (int i = 0; i < Mathf.Max(nl, nr) && count < MaxGlobs; i++)
        {
            if (i < nl && count < MaxGlobs) xs[count++] = left[i];
            if (i < nr && count < MaxGlobs) xs[count++] = right[i];
        }
        telegraph.Clear();
        for (int i = 0; i < count; i++) telegraph.Ring(Spot(i), r + .12f);
        telegraph.Show(TellSeconds, def.ShotColor);
    }

    // Pools from `x0` out toward the rail on `sign`'s side; the last within a ship-less sliver of the rail.
    static int Flank(float[] into, float x0, float sign, float spacing, float r, float rail)
    {
        int n = 0;
        for (int k = 0; k < into.Length; k++)
        {
            float x = Mathf.Clamp(x0 + sign * k * spacing, -(rail - r - .02f), rail - r - .02f);   // (a pool is gone the moment it touches a rail)
            into[n++] = x;
            if (sign * x >= rail - r - .22f) break;
        }
        return n;
    }

    public override void StepTell(float dt) { t += dt; telegraph.Step(dt); }
    public override void Cancel() { telegraph.End(); }
    public override void OnDeath() { telegraph.End(); }

    public override void BeginAction()
    {
        base.BeginAction();
        telegraph.GoLive();
        next = 0f;
        lobbed = 0;
        fromLeft = ship.Position.x < gapX;
        liveRow = rowCentre + Vector2.up * (EliteSystem.Scroll * def.lobSeconds);
    }

    public override bool StepAction(float dt)
    {
        t += dt;
        liveRow.y -= EliteSystem.Scroll * dt;   // (the aim rides the board down onto the row)
        while (t >= next && lobbed < count)
        {
            int i = fromLeft ? lobbed : count - 1 - lobbed;
            int m = def.muzzles.Length > 0 ? lobbed % def.muzzles.Length : 0;
            Vector2 to = new Vector2(xs[i], liveRow.y);
            var shot = Fire(m, Deg(to - ship.MuzzleWorld(m)), 0f);
            if (shot != null)
            {
                shot.Lob(to, def.lobSeconds - next);
                if (linger > 0f) shot.Linger(linger);
            }
            lobbed++;
            next += def.shotInterval;
        }
        return t >= def.actionSeconds && lobbed >= count;
    }

    // the mortar is spent: it hops to another perch
    public override void End()
    {
        telegraph.End();
        var cb = ship.Brain as CanopyBrain;
        if (cb != null) cb.MortarDone();
    }
}

// ---- Sporebloom: floater + spore_drift ------------------------------------------------------------------------------------------

// A spore balloon. It floats at one of three stations high on the board (a slow sway, nothing to do with the pilot) and takes
// AttacksPerFloat bursts (or FloatMax seconds); then it SINKS off its nearer side, drifting down into the canopy, is out of play for
// UnderSeconds, and RISES again out of the other side to a different station (at least MinMove from the last).
public class FloaterBrain : EliteBrain
{
    public enum Stage { Float, Sink, Under, Rise }
    public const int AttacksPerFloat = 2;
    public const float FloatMax = 10f, UnderSeconds = 1.3f, SinkScale = 1.5f, RiseScale = 1.7f, SettleDistance = .4f, SinkDrop = 1.4f, MinMove = 1.4f, AttackWait = .6f;
    static readonly float[] Columns = { -.75f, 0f, .75f };
    Stage stage = Stage.Float;
    int col = -1, lastCol = -1, attacks, sinkSide = 1;
    float floated, under, sinkY;
    public Stage Phase => stage;
    public int Column => col;
    public int Sinks { get; private set; }
    public int SinkSide => sinkSide;
    public int ColumnCount => Columns.Length;

    public FloaterBrain() { Id = "floater"; }

    public override bool Roams => true;
    public override float TellScale => 1f;
    public override Vector2 JoinFrom => Vector2.up;
    public override void OnJoin() { col = Nearest(Pos.x); stage = Stage.Float; floated = 0f; attacks = 0; }
    public override void OnReturn() { lastCol = col; col = Pick(); stage = Stage.Float; floated = 0f; attacks = 0; }

    int Nearest(float x)
    {
        float edge = FrostFlight.Edge(def, .55f);
        int best = 0;
        for (int i = 1; i < Columns.Length; i++) if (Mathf.Abs(Columns[i] * edge - x) < Mathf.Abs(Columns[best] * edge - x)) best = i;
        return best;
    }

    public Vector2 Station(int c) =>
        new Vector2(Columns[Mathf.Clamp(c, 0, Columns.Length - 1)] * FrostFlight.Edge(def, .55f), ship.ReachY(EliteSystem.ViewTop - def.topMargin - .2f));

    // A different station, at least MinMove from where it was.
    int Pick()
    {
        float edge = FrostFlight.Edge(def, .55f);
        float from = col >= 0 ? Columns[col] * edge : 0f;
        int pick = -1;
        for (int tries = 0; tries < 8 && pick < 0; tries++)
        {
            int c = Random.Range(0, Columns.Length);
            if (c != col && Mathf.Abs(Columns[c] * edge - from) >= MinMove) pick = c;
        }
        return pick >= 0 ? pick : (col + 1) % Columns.Length;
    }

    public override Vector2 Goal(Vector2 seen, float dt)
    {
        Clock += dt;
        if (col < 0) col = Nearest(Pos.x);
        float off = SpaceFlight.OffX(def);
        switch (stage)
        {
            case Stage.Float:
            {
                floated += dt;
                if (floated > FloatMax || attacks >= AttacksPerFloat) { BeginSink(); return Pos; }
                return Station(col) + new Vector2(Mathf.Sin(Clock * .6f) * .35f, Mathf.Sin(Clock * .9f) * .12f);
            }
            case Stage.Sink:
                if (Mathf.Abs(Pos.x) > off - .25f) { stage = Stage.Under; under = 0f; Sinks++; }
                return new Vector2(sinkSide * (off + .8f), sinkY);
            case Stage.Under:
            {
                under += dt;
                if (under >= UnderSeconds)
                {
                    lastCol = col;
                    col = Pick();
                    Vector2 st = Station(col);
                    // it rises out of the side its new station is on
                    float sx = Columns[col] < -.01f ? -1f : Columns[col] > .01f ? 1f : -sinkSide;
                    ship.Warp(new Vector2(sx * off, st.y - 1f));
                    stage = Stage.Rise;
                }
                return Pos;
            }
            default:
            {
                Vector2 st = Station(col);
                if ((Pos - st).sqrMagnitude < SettleDistance * SettleDistance) { stage = Stage.Float; floated = 0f; attacks = 0; }
                return st;
            }
        }
    }

    void BeginSink()
    {
        sinkSide = Pos.x >= 0f ? 1 : -1;
        sinkY = Pos.y - SinkDrop;
        stage = Stage.Sink;
    }

    public override float SpeedScale => stage == Stage.Sink ? SinkScale : stage == Stage.Rise ? RiseScale : .65f;
    public override float MinAttackAbove => 1.5f;
    public override bool WantsAttack(Vector2 seen) =>
        stage == Stage.Float && floated > AttackWait && attacks < AttacksPerFloat && Pos.y > seen.y + MinAttackAbove && (Pos - Station(col)).sqrMagnitude < .8f * .8f;

    public void BurstDone() { attacks++; }
}

// Sporebloom: spore_burst (the pods and their rings, drawn first) and, once it is down to CloudHearts, a lingering SPORE CLOUD: the
// zone is a lane (CloudDiam wide, CloudPools pools stacked CloudStep apart) at CloudOffset beside the pilot toward the middle, outlined
// with the rest at the first frame of the tell, locked; the pods' vents then lob the pools onto it, each a cloud-sized pool that rides
// the board and lingers CloudSeconds. A cloud pool is a plain pool (no spores of its own).
public class SporeDriftAttack : SporeBurstAttack
{
    public const int CloudHearts = 2, CloudPools = 3;
    public const float CloudSeconds = 3f, CloudDiam = 1.3f, CloudStep = 1.1f, CloudOffset = 1.7f;
    bool cloud;
    float laneX, rowY, liveY, nextCloud;
    int cloudSent;
    public SporeDriftAttack() { Id = "spore_drift"; }
    public bool HasCloud => cloud;
    public float CloudLaneX => laneX;
    public float CloudRowY => rowY;
    public int CloudSent => cloudSent;

    protected override void DrawMore(Vector2 seen)
    {
        cloud = ship.Hearts <= CloudHearts;
        if (!cloud) return;
        float lim = Mathf.Max(0f, EliteSystem.RailEdge - CloudDiam * .5f - .1f);
        laneX = Mathf.Clamp(seen.x + (seen.x >= 0f ? -1f : 1f) * CloudOffset, -lim, lim);
        rowY = seen.y + .9f;
        float half = (CloudPools - 1) * .5f * CloudStep + CloudDiam * .5f;
        telegraph.Lane(new Vector2(laneX, rowY + half), new Vector2(laneX, rowY - half), CloudDiam * .5f);
    }

    public override void BeginAction()
    {
        base.BeginAction();
        cloudSent = 0;
        nextCloud = 0f;
        liveY = rowY + EliteSystem.Scroll * def.lobSeconds;
    }

    public override bool StepAction(float dt)
    {
        bool done = base.StepAction(dt);
        if (!cloud) return done;
        liveY -= EliteSystem.Scroll * dt;
        float scale = CloudDiam / Mathf.Max(.05f, def.shotSize * 2.1f);
        while (t >= nextCloud && cloudSent < CloudPools)
        {
            int m = cloudSent % Mathf.Max(1, def.muzzles.Length);
            Vector2 to = new Vector2(laneX, liveY + (cloudSent - (CloudPools - 1) * .5f) * CloudStep);
            var shot = Fire(m, Deg(to - ship.MuzzleWorld(m)), 0f, EliteShots.Kind.Glob);
            if (shot != null)
            {
                shot.Lob(to, def.lobSeconds - nextCloud);
                shot.Cloud(scale, CloudSeconds);
            }
            cloudSent++;
            nextCloud += def.shotInterval * .5f;
        }
        return done && cloudSent >= CloudPools;
    }

    public override void End()
    {
        base.End();
        var fb = ship.Brain as FloaterBrain;
        if (fb != null) fb.BurstDone();
    }
}

// ---- Thornlash: anchor + thorn_sweep --------------------------------------------------------------------------------------------

// A whip anchored to the rails. It latches onto one side just outside a rail (a hair of the hull on the board), at a height it takes
// when it arrives (Standoff above the pilot, inside the player's reach, then held: it does not slide up and down with the pilot), and
// takes 1 + (hearts lost) sweeps from there, an attack gap apart; then it SWINGS across the top (high, SwingScale x its speed, not
// attacking) to the other rail and latches on there.
public class AnchorBrain : EliteBrain
{
    public enum Stage { Anchored, Swing }
    public const float LatchWait = .8f, SwingScale = 2.2f, Standoff = 3.4f, SettleDistance = .3f, Inset = .08f;
    Stage stage = Stage.Anchored;
    int side = 1, sweepsLeft;
    float anchored, anchorY;
    bool settled, rising;
    public Stage Phase => stage;
    public int Side => side;
    public int Anchors { get; private set; }
    public int Swings { get; private set; }
    public int SweepsLeft => sweepsLeft;
    public int SweepsPerAnchor => 1 + (ship != null ? Mathf.Max(0, def.hearts - ship.Hearts) : 0);
    public float AnchorX => side * (EliteSystem.RailEdge + Inset);
    public float AnchorY => anchorY;

    public AnchorBrain() { Id = "anchor"; }

    public override bool Roams => true;
    public override float TellScale => 1f;
    public override Vector2 JoinFrom => Vector2.up;
    public override void OnJoin() { side = Pos.x >= 0f ? 1 : -1; Latch(ship.Follow); }
    public override void OnReturn() { side = -side; stage = Stage.Swing; rising = true; }

    float TopY => EliteSystem.ViewTop - def.hullRadius - .25f;

    void Latch(Vector2 follow)
    {
        stage = Stage.Anchored;
        anchorY = ship.ReachY(Mathf.Min(follow.y + Standoff, EliteSystem.ViewTop - def.hullRadius - 1.2f));
        settled = false;
        anchored = 0f;
        sweepsLeft = SweepsPerAnchor;
    }

    public override Vector2 Goal(Vector2 follow, float dt)
    {
        Clock += dt;
        if (stage == Stage.Anchored)
        {
            Vector2 st = new Vector2(AnchorX, anchorY + Mathf.Sin(Clock * 1.1f) * .1f);
            if (!settled && (Pos - new Vector2(AnchorX, anchorY)).sqrMagnitude < SettleDistance * SettleDistance) { settled = true; Anchors++; }
            if (settled) anchored += dt;
            // spent its sweeps (and not mid-attack: the brain's goal is only read between them): over the top to the other rail
            if (settled && sweepsLeft <= 0) { side = -side; stage = Stage.Swing; rising = true; Swings++; return Pos; }
            return st;
        }
        // swing: up to the top, along it, then down the other rail to its latch height
        if (rising)
        {
            if (Pos.y > TopY - .25f) rising = false;
            return new Vector2(Pos.x, TopY);
        }
        if (Mathf.Abs(Pos.x - AnchorX) < .5f) { Latch(follow); return new Vector2(AnchorX, anchorY); }
        return new Vector2(AnchorX, TopY);
    }

    public override float SpeedScale => stage == Stage.Swing ? SwingScale : .8f;
    public override float MinAttackAbove => 1.5f;
    public override bool WantsAttack(Vector2 seen) => stage == Stage.Anchored && settled && anchored >= LatchWait && sweepsLeft > 0 && Pos.y > seen.y + MinAttackAbove;

    public void SweepDone() { sweepsLeft = Mathf.Max(0, sweepsLeft - 1); }
}

// Thornlash: a thorn sweep. vine_lash (the whip's whole swept area drawn from the first frame of the tell, aimed once, AttackLash's fair
// corridor beyond the start line) from the root nearer the pilot, and each whip counts against the anchor's sweeps.
public class ThornSweepAttack : VineLashAttack
{
    public ThornSweepAttack() { Id = "thorn_sweep"; }

    public override void End()
    {
        bool lit = Lit;
        base.End();
        var ab = ship.Brain as AnchorBrain;
        if (lit && ab != null) ab.SweepDone();
    }
}

// ---- Timber Hauler: lumber + raft_roll -------------------------------------------------------------------------------------------

// A lumber barge on the top rail. It slides a slow sine along the top (topMargin under it, never above the player's reach), its
// course nothing to do with the pilot, and never comes down: it attacks from anywhere over the board with the pilot well below.
public class LumberBrain : EliteBrain
{
    public const float SlideRate = .35f, SlideAmp = .8f;
    float phase;
    public LumberBrain() { Id = "lumber"; }
    protected override void OnBind() { phase = Random.value * 6.28f; }
    public override Vector2 JoinFrom => Vector2.up;
    public override float SpeedScale => .8f;
    public override float MinAttackAbove => 1.8f;

    public override Vector2 Goal(Vector2 seen, float dt)
    {
        Clock += dt;
        float edge = FrostFlight.Edge(def, .45f);
        return new Vector2(Mathf.Sin(Clock * SlideRate + phase) * SlideAmp * edge, ship.ReachY(EliteSystem.ViewTop - def.topMargin) + Mathf.Sin(Clock * .8f) * .06f);
    }

    public override bool WantsAttack(Vector2 seen) => Pos.y > seen.y + MinAttackAbove;
}

// Timber Hauler: log_roll (trunks lobbed onto ringed spots, each rolling down a drawn diagonal), and, once it is down to RaftHearts,
// every other cast is a RAFT: two trunks lobbed together onto the two spots either side of ONE gap (RaftClear of free floor between
// their roll lanes, beside the pilot toward the middle), both rolling down the same diagonal toward the side with the most floor.
// The raft's spots, lanes and gap are all drawn from the first frame of the tell, locked.
public class RaftRollAttack : LogRollAttack
{
    public const int RaftHearts = 2;
    int sinceRaft;
    public RaftRollAttack() { Id = "raft_roll"; }

    protected override bool WantsRaft()
    {
        if (ship.Hearts > RaftHearts) { sinceRaft = 0; return false; }
        return sinceRaft++ % 2 == 0;
    }
}
