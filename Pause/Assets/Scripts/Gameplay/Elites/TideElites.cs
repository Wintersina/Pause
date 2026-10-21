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
//   Riptide      3  breaker   / foam_wake     (trenchhatch) hurtHide  a WAVE RIDER: a sine wave across the top of the view, a rolling U-turn
//                                              at each rail onto a lower lane (down, down, up, up), foam rings on the water behind it, then
//                                              foam lobbed onto them. Hurt: wakes in pairs; at the last heart one scripted exit right
//   Bathyscaphe  5  deep      / mine_chain    (rigdeck)     armored   holds a column at the top (left, right, centre, a script order) and
//                                              lays a CHAIN of sea mines down to the pilot's column; between columns it SINKS, slides low
//                                              along the zone and rises again (it never leaves the view). Longer, forked chains later
//   Trawler      3  trawl     / drag_net      (reefdock)              stands on one FLANK against a rail and drags a line of nets in from
//                                              it, one after another, across the pilot's row -- ringed first, the far side always left open
//                                              -- then sails over the top to the other flank. Later: wider rows, then nets from BOTH rails
//   Thunderfin   3  tempest   / thunder_sweep (ventstack)   hurtHide  circles slowly in an ellipse high over the board; thunder glyphs mark
//                                              every column at once and the columns then strike one after another, sweeping the way it is
//                                              flying (the direction flips each cast); later outside in and middle out, then both at once
//   Pearl Warden 5  oyster    / pearl_spiral  (wreckbay)    armored   keeps one of three stations at the top (a script order); sweeps a
//                                              spiral of slow pearls down an arc (the two edge lanes drawn), with a gap left in the direction
//                                              the pilot stood at the lock; later a wider arc, then a double spiral
//
// THE SCRIPT (def json "script", EliteScript.cs): the fixed loops, the variants and the move tokens ("pace:1.2", "col:2", "L", "R",
// "st:1") are authored there; the brains below read the tokens (OnScriptMove) and use no randomness. All five stay inside the view: a
// brain keeps to the board and turns round at the rail; only Riptide's and Thunderfin's one scripted last-heart exit leaves it.
//
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

    // A script move token "name:number" ("col:2", "pace:1.15"): true and the number. No allocation (it runs in the fight loop).
    public static bool TokenFloat(string token, string name, out float v)
    {
        v = 0f;
        if (token == null) return false;
        int n = name.Length;
        if (token.Length <= n + 1 || token[n] != ':' || !token.StartsWith(name, System.StringComparison.Ordinal)) return false;
        float whole = 0f, frac = 0f, scale = 1f;
        bool dot = false, any = false, neg = false;
        for (int i = n + 1; i < token.Length; i++)
        {
            char c = token[i];
            if (c == '-' && i == n + 1) { neg = true; continue; }
            if (c == '.') { dot = true; continue; }
            if (c < '0' || c > '9') return false;
            any = true;
            if (!dot) whole = whole * 10f + (c - '0');
            else { scale *= .1f; frac += (c - '0') * scale; }
        }
        v = (whole + frac) * (neg ? -1f : 1f);
        return any;
    }

    public static bool TokenInt(string token, string name, out int v)
    {
        float f;
        bool ok = TokenFloat(token, name, out f);
        v = Mathf.RoundToInt(f);
        return ok;
    }
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

// A wave rider that stays in the view. Its RIDE is a sine wave (Amplitude, WaveLength) along a lane across the top of the board, at a
// slow steady pace; at the end of a pass it makes a rolling U-turn (a half loop that bulges Bulge toward the rail) onto the next lane
// and comes back the other way. The lanes go down, down, up, up (LaneOrder, a counter, never a coin). The script says how fast
// ("pace:1.2") and when to lay a foam wake (FoamWakeAttack, behind it on the water it has just crossed).
public class BreakerBrain : EliteBrain
{
    public enum Stage { Ride, Turn }
    public const float Amplitude = .5f, WaveLength = 3.4f, Behind = .45f, SpotStep = .5f, Bulge = .4f, Lead = 1.1f, Cruise = .62f, LaneStep = .85f;
    public const int Spots = 3, MaxWake = 4;
    static readonly int[] LaneOrder = { 1, 2, 1, 0 };
    Stage stage = Stage.Ride;
    float dirX = 1f, passX0, laneY, nextY, fromY, k, pace = 1f, cx;
    int lane, nextLane;
    Vector2 carrot;
    bool started;
    public Stage Phase => stage;
    public float DirX => dirX;
    public int Lane => lane;
    public float Pace => pace;
    public float PassX0 => passX0;
    public int Passes { get; private set; }
    public int Turns { get; private set; }
    public int Casts { get; private set; }
    public float PassEnd => Mathf.Max(.5f, TideFlight.Edge(def, .45f) - Bulge);

    public BreakerBrain() { Id = "breaker"; }

    public override float TellScale => 1f;
    public override Vector2 JoinFrom => new Vector2(-1f, .8f);
    public override float SpeedScale => Cruise * pace;
    public override void OnJoin() { Restart(); }
    public override void OnReturn() { Restart(); }

    public override void OnScriptMove(string token)
    {
        float v;
        if (TideFlight.TokenFloat(token, "pace", out v)) pace = Mathf.Clamp(v, .5f, 1.6f);
    }

    // The top of the view, under the very top, leaving room for the wave.
    public static void Band(EliteDef def, out float lo, out float hi)
    {
        hi = Mathf.Min(EliteSystem.ViewTop - def.hullRadius - .5f - Amplitude, FightZone.Top(FightZone.HalfOf(def)));
        lo = Mathf.Min(hi, EliteSystem.ViewTop - (EliteSystem.ViewTop - EliteSystem.ViewBottom) / 3f + def.hullRadius + Amplitude);
    }

    // Lane i of the ride: the highest where the wave's crest still fits the zone, then LaneStep lower each.
    public float LaneAt(int i)
    {
        float lo, hi;
        Band(def, out lo, out hi);
        return Mathf.Min(hi, ship.ZoneTop - Amplitude - .05f) - i * LaneStep;
    }

    // The wave line of this pass at world x.
    public float WaveY(float x) => laneY + Amplitude * Mathf.Sin((x - passX0) * dirX * 2f * Mathf.PI / WaveLength);

    // Wake spot k of a cast: on the water of this pass, behind where the ship is now.
    public Vector2 WakeSpot(int spotIndex)
    {
        float along = (Pos.x - passX0) * dirX - Behind - SpotStep * spotIndex;   // (the last rings may lie back in the bulge by the rail)
        float x = passX0 + dirX * along;
        return new Vector2(x, WaveY(x));
    }

    // How many rings the coming cast lays (the step's variant is not on the def's numbers yet).
    public int PlannedSpots()
    {
        int n = def.shotCount;
        var sd = ship.Script.Data;
        string a = ship.Script.Step.attack;
        if (sd.variants != null && !string.IsNullOrEmpty(a))
            for (int i = 0; i < sd.variants.Length; i++)
            {
                if (sd.variants[i].name != a || sd.variants[i].set == null) continue;
                for (int j = 0; j < sd.variants[i].set.Length; j++)
                    if (sd.variants[i].set[j].key == "shotCount") n = Mathf.RoundToInt(sd.variants[i].set[j].value);
            }
        return Mathf.Clamp(n, 2, MaxWake);
    }

    void Restart()
    {
        started = true;
        stage = Stage.Ride;
        lane = 0;
        pace = 1f;
        dirX = Pos.x > .3f ? -1f : 1f;
        laneY = LaneAt(0);
        passX0 = -dirX * PassEnd;
        cx = passX0;
        carrot = new Vector2(cx, laneY);
    }

    public override Vector2 Goal(Vector2 seen, float dt)
    {
        Clock += dt;
        if (!started) Restart();
        float step = def.speed * SpeedScale * ship.Script.SpeedScale * .95f * dt;
        bool keepUp = (carrot - Pos).sqrMagnitude < Lead * Lead;
        float pe = PassEnd;
        if (stage == Stage.Ride)
        {
            if (keepUp) cx += dirX * step;
            if (dirX * cx >= pe)
            {
                cx = dirX * pe;
                fromY = WaveY(cx);
                nextLane = LaneOrder[ship.Script.Cycle("lane", LaneOrder.Length)];
                nextY = LaneAt(nextLane);
                k = 0f;
                stage = Stage.Turn;
                carrot = new Vector2(cx, fromY);
            }
            else carrot = new Vector2(cx, WaveY(cx));
        }
        else
        {
            float len = Mathf.Abs(nextY - fromY) + Bulge * 3f + .3f;
            if (keepUp) k += step / len;
            if (k >= 1f)
            {
                Turns++;
                Passes++;
                lane = nextLane;
                laneY = nextY;
                dirX = -dirX;
                passX0 = -dirX * pe;
                cx = passX0;
                stage = Stage.Ride;
                carrot = new Vector2(cx, laneY);
            }
            else
            {
                float e = k * k * (3f - 2f * k);
                carrot = new Vector2(dirX * (pe + Bulge * Mathf.Sin(Mathf.PI * k)), Mathf.Lerp(fromY, nextY, e));
            }
        }
        carrot.y = ship.ReachY(carrot.y);   // (inside the player's reach, so the carrot is never out of the ship's)
        return carrot;
    }

    // Wakes go on the water it has just crossed: a long enough stretch of this pass behind it.
    public override bool WantsAttack(Vector2 seen)
    {
        if (stage != Stage.Ride) return false;
        float now = (Pos.x - passX0) * dirX;
        return now >= Behind + SpotStep * (PlannedSpots() - 1) - Bulge * .8f + .1f && dirX * Pos.x < PassEnd + .2f;
    }

    public void CastDone() { Casts++; }
}

// Riptide: a foam wake. The tell does NOT stop it (it keeps riding its line): from the first frame thin rings mark spots on the water
// BEHIND it, along the wave line it has just crossed (shotCount of them, 2 to BreakerBrain.MaxWake). The action lobs a foam bank out
// of the lance onto each ring, one after another (shotInterval apart); a bank rides the board for hazardSeconds, hurts on touch and
// takes one player shot to pop. (The rings and the foam are all the pilot has to read: stay off them.)
public class FoamWakeAttack : TideSpotAttack
{
    public const float Ring = .5f;
    public FoamWakeAttack() { Id = "foam_wake"; }
    protected override float RingRadius => Ring;
    public override float HazardRadius => Ring * .8f;

    protected override void Plan(Vector2 pilot)
    {
        var bb = ship.Brain as BreakerBrain;
        count = Mathf.Clamp(def.shotCount, 2, BreakerBrain.MaxWake);
        for (int k = 0; k < count; k++) spot[k] = bb != null ? bb.WakeSpot(k) : ship.Position + Vector2.down * (1f + k * .8f);
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

// A mine layer that SUBMERGES and resurfaces without ever leaving the view. It holds a station at the top (one of three columns, a
// little sliding), lays a chain of mines (MineChainAttack), and when the script names another column ("col:0|1|2") it SINKS straight
// down (Dip), slides along the low line to that column and RISES onto the station. It never comes down to the pilot.
public class DeepBrain : EliteBrain
{
    public enum Stage { Hold, Sink, Slide, Rise }
    public const float ReadyWait = .5f, SettleDistance = .35f, Dip = 1.1f, SinkScale = 1.1f, SlideScale = 1.3f, RiseScale = 1.2f, HoldScale = .75f, Reach = .85f;
    public static readonly float[] Columns = { -1f, 0f, 1f };
    Stage stage = Stage.Rise;
    int column = 1, target = 1;
    float held;
    public Stage Phase => stage;
    public int Column => column;
    public int Target => target;
    public int Dives { get; private set; }
    public int Casts { get; private set; }
    public float Height => ship.ZoneY(ship.ReachY(EliteSystem.ViewTop - def.topMargin));
    public float StationOf(int i) => Columns[Mathf.Clamp(i, 0, Columns.Length - 1)] * TideFlight.Edge(def, .5f) * Reach;
    public float StationX => StationOf(target);

    public DeepBrain() { Id = "deep"; }

    public override float TellScale => 1f;
    public override Vector2 JoinFrom => Vector2.up;
    public override void OnJoin() { stage = Stage.Rise; held = 0f; }
    public override void OnReturn() { stage = Stage.Rise; held = 0f; }

    public override void OnScriptMove(string token)
    {
        int c;
        if (!TideFlight.TokenInt(token, "col", out c)) return;
        target = Mathf.Clamp(c, 0, Columns.Length - 1);
        if (stage != Stage.Hold) return;
        if (Mathf.Abs(Pos.x - StationOf(target)) > SettleDistance + .2f) { stage = Stage.Sink; Dives++; }
        else column = target;
    }

    public override Vector2 Goal(Vector2 follow, float dt)
    {
        Clock += dt;
        switch (stage)
        {
            case Stage.Sink:
            {
                float low = Height - Dip;
                if (Pos.y <= low + .25f) stage = Stage.Slide;
                return new Vector2(Pos.x, low);
            }
            case Stage.Slide:
            {
                float tx = StationOf(target);
                if (Mathf.Abs(Pos.x - tx) < .3f) stage = Stage.Rise;
                return new Vector2(tx, Height - Dip);
            }
            case Stage.Rise:
            {
                Vector2 st = new Vector2(StationOf(target), Height);
                if ((Pos - st).sqrMagnitude < SettleDistance * SettleDistance) { column = target; stage = Stage.Hold; held = 0f; }
                return st;
            }
            default:
                held += dt;
                return new Vector2(StationOf(column) + Mathf.Sin(Clock * .5f) * .35f, Height + Mathf.Sin(Clock * .9f) * .08f);
        }
    }

    public override float SpeedScale => stage == Stage.Sink ? SinkScale : stage == Stage.Slide ? SlideScale : stage == Stage.Rise ? RiseScale : HoldScale;
    public override float MinAttackAbove => 1.5f;
    public override bool WantsAttack(Vector2 seen) => stage == Stage.Hold && held >= ReadyWait && Pos.y > seen.y + MinAttackAbove;
    public override float? FaceDeg(Vector2 seen) => stage == Stage.Hold ? -90f : (float?)null;

    // The attack's callback.
    public void CastDone() { Casts++; }
}

// Bathyscaphe: a mine chain. The tell holds it at its station, nose down: a thin ring marks every mine of a CHAIN that runs from just
// under the hull down to the pilot's column (a little ahead of it, plus laneOffset: a fork to one side), the pilot's spot followed for
// the first aimCommit of the tell and then locked (and blinking faster). The action lays the mines one after another, shotInterval
// apart, out of the claws; each is lobbed onto its ring (lobSeconds), rides the board for hazardSeconds more and bursts into a small
// cross of four bolts (hazardSpeed). A mine takes one player shot to defuse. The script makes the chain longer (shotCount: 2 .. 5)
// and forks it (laneOffset) as the fight goes on.
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
    public int PlanCount => Mathf.Clamp(Mathf.Max(2, def.shotCount), 2, MaxSpots - 1);

    public override bool FriendlyInLine(Vector2 seen) =>
        EliteOnLine(ship.Position, Vector2.down, 8f, .6f);

    protected override void Plan(Vector2 pilot)
    {
        count = PlanCount;
        float edge = EliteSystem.RailEdge - .35f;
        Vector2 from = ship.Position + Vector2.down * (def.hullRadius + .5f);
        Vector2 to = new Vector2(Mathf.Clamp(pilot.x + def.laneOffset, -edge, edge), Mathf.Max(EliteSystem.ViewBottom + 1.4f, pilot.y + def.lobAhead));
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

// A net hauler that works a FLANK. It stands against a rail near the top (bobbing a little), drags its nets (DragNetAttack), and when
// the script says so ("L" / "R") sails over the TOP of the board in one arc to the other flank. It never leaves the view and never
// comes down to the pilot.
public class TrawlBrain : EliteBrain
{
    public enum Stage { Flank, Cross }
    public const float ReadyWait = .6f, Hang = .9f, CrossScale = 1.1f, Lead = 1.2f;
    Stage stage = Stage.Flank;
    int side = -1, to = -1;
    float held, k;
    Vector2 carrot;
    public Stage Phase => stage;
    public int Side => side;
    public int Casts { get; private set; }
    public int Crossings { get; private set; }
    public float FlankOf(int s) => s * Mathf.Max(0f, EliteSystem.RailEdge - def.hullRadius - .3f);
    public float FlankX => FlankOf(side);
    public float Height => ship.ZoneY(ship.ReachY(EliteSystem.ViewTop - def.topMargin));
    float FlankY => Height - Hang + Mathf.Sin(Clock * .7f) * .3f;

    public TrawlBrain() { Id = "trawl"; }

    public override float TellScale => 1f;
    public override Vector2 JoinFrom => new Vector2(side, .5f);
    public override void OnJoin() { stage = Stage.Flank; to = side; held = 0f; }
    public override void OnReturn() { stage = Stage.Flank; to = side; held = 0f; }

    public override void OnScriptMove(string token)
    {
        int want = token == "L" ? -1 : token == "R" ? 1 : 0;
        if (want == 0 || want == side) return;
        to = want;
        if (stage == Stage.Flank) { stage = Stage.Cross; k = 0f; carrot = Pos; }
    }

    public override Vector2 Goal(Vector2 follow, float dt)
    {
        Clock += dt;
        if (stage == Stage.Flank)
        {
            held += dt;
            return new Vector2(FlankOf(side), FlankY);
        }
        float width = Mathf.Abs(FlankOf(to) - FlankOf(side)) + Hang * 2f;
        if ((carrot - Pos).sqrMagnitude < Lead * Lead) k += def.speed * SpeedScale * ship.Script.SpeedScale * .9f * dt / Mathf.Max(1f, width);
        if (k >= 1f)
        {
            side = to;
            stage = Stage.Flank;
            held = 0f;
            Crossings++;
            return new Vector2(FlankOf(side), FlankY);
        }
        float e = k * k * (3f - 2f * k);
        carrot = new Vector2(Mathf.Lerp(FlankOf(side), FlankOf(to), e), ship.ReachY(FlankY + Hang * Mathf.Sin(Mathf.PI * k)));
        return carrot;
    }

    public override float SpeedScale => stage == Stage.Cross ? CrossScale : 1f;
    public override float MinAttackAbove => 1.2f;
    public override bool WantsAttack(Vector2 seen) => stage == Stage.Flank && held >= ReadyWait && Pos.y > seen.y + MinAttackAbove;
    public override float? FaceDeg(Vector2 seen) => stage == Stage.Flank ? -90f : (float?)null;

    // The attack's callback.
    public void CastDone() { Casts++; held = 0f; }
}

// Trawler: a net drag. The tell holds it on its flank, nose down: a thin ring marks every net spot of a ROW across the pilot's height
// (a little ahead of it, lobAhead, followed for the first aimCommit of the tell, then locked), starting at the flank's own rail and
// running in toward the middle, lobSpacing apart. The row is only as long as leaves a gap at least Gap wide, between its far end and
// the far rail, for the pilot to live in. With hazardCount >= 2 ("both rails") the nets come in from BOTH rails, shotCount / 2 each,
// and the open gap is in the middle. The action lobs the nets out one after another, shotInterval apart, flank first; each lands as
// a pool that rides the board for poolSeconds. (Stand on the open side, or go round the row's end.)
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
    public bool Both => def.hazardCount >= 2;

    // How many nets the row can hold and still leave the pilot a gap.
    public int NetsFor()
    {
        float room = 2f * Edge - Gap - 2f * Ring;
        float sp = Mathf.Max(.3f, def.lobSpacing);
        if (Both)
        {
            int per = Mathf.Max(1, def.shotCount / 2);
            int fitPer = 1 + Mathf.FloorToInt(Mathf.Max(0f, room) / (2f * sp));
            return Mathf.Clamp(Mathf.Min(per, fitPer) * 2, 2, MaxSpots);
        }
        int want = Mathf.Max(2, def.shotCount);
        int fit = 1 + Mathf.FloorToInt(Mathf.Max(0f, room) / sp);
        return Mathf.Clamp(Mathf.Min(want, fit), 1, MaxSpots);
    }

    protected override void Plan(Vector2 pilot)
    {
        count = NetsFor();
        int sd = Side;
        float y = Mathf.Max(EliteSystem.ViewBottom + 1.2f, Mathf.Min(pilot.y + def.lobAhead, ship.Position.y - 1.2f));
        for (int i = 0; i < count; i++)
        {
            if (Both)
            {
                int j = i / 2;
                spot[i] = new Vector2((i % 2 == 0 ? -1 : 1) * (Edge - j * def.lobSpacing), y);
            }
            else spot[i] = new Vector2(sd * (Edge - i * def.lobSpacing), y);
        }
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

// A storm cell that loiters. It circles a slow ellipse high over the board (about 80 % of the width, Ry tall), pausing the circle
// while it attacks. It never comes down and never follows the pilot's lane. The script flies it right or left along the top of the
// ellipse ("R" / "L"): the direction flips from cast to cast. SweepDir is the way it is flying along now (+1 right): the thunder it
// calls sweeps that way.
public class TempestBrain : EliteBrain
{
    public const float Ry = .35f, Pace = .55f;
    float angle = Mathf.PI * 1.5f;
    int dir = 1;
    public int SweepDir => Mathf.Sin(angle) * dir > 0f ? -1 : 1;   // (x = cos: it moves left while sin * dir > 0)
    public int Dir => dir;
    public float Angle => angle;
    public float Rx => Mathf.Max(.4f, TideFlight.Edge(def, .6f) * .8f);
    public float Height => ship.ZoneY(ship.ReachY(EliteSystem.ViewTop - def.topMargin));

    public TempestBrain() { Id = "tempest"; }

    public override Vector2 JoinFrom => Vector2.up;
    public override float MinAttackAbove => 1.5f;
    public override bool WantsAttack(Vector2 seen) => Pos.y > seen.y + MinAttackAbove;
    public override float? FaceDeg(Vector2 seen) => -90f;

    public override void OnScriptMove(string token)
    {
        if (token == "R") dir = 1;
        else if (token == "L") dir = -1;
    }

    public override Vector2 Goal(Vector2 follow, float dt)
    {
        Clock += dt;
        if (ship.State != EliteState.Attack) angle += dir * def.speed * Pace * ship.Script.SpeedScale / Rx * dt;
        return new Vector2(Mathf.Cos(angle) * Rx, Height - Ry - Ry * Mathf.Sin(angle));
    }
}

// Thunderfin: a thunder sweep. The lure coils pulse and a lane glyph is drawn under EVERY column for the whole tell (>= .7 s): the
// pilot's lane first and the others either side at least a strike's corridor apart (StrikeLanes), shotCount of them (3 fit the board).
// Then the columns strike ONE AFTER ANOTHER, shotInterval apart, in the ORDER hazardCount says: 0 sweeping the way the fish is flying
// (TempestBrain.SweepDir) -- the last glyph is the last column; 1 BOTH ways at once, from the two outer columns in; 2 from the middle
// out both ways. (AttackStrike: .25 s live each.) Step off the glyph, or wait in the gaps between the columns.
public class ThunderSweepAttack : EliteAttack
{
    readonly AttackStrike[] armed = new AttackStrike[5];
    readonly float[] lanes = new float[5];
    int count, first;
    public ThunderSweepAttack() { Id = "thunder_sweep"; }
    public override bool HoldsDuringTell => true;
    public override bool HoldsDuringAction => true;
    public override bool Shoots => false;   // (lightning, not shots)
    public int Armed => count;
    public AttackStrike Strike(int i) => armed[i];
    public float Lane(int i) => lanes[i];
    public int Want => Mathf.Clamp(def.shotCount, 2, armed.Length);

    // The place of column i (in the sweep's order) in the strike sequence.
    int Rank(int i, int n)
    {
        switch (def.hazardCount)
        {
            case 1: return Mathf.Min(i, n - 1 - i);
            case 2: return Mathf.FloorToInt(Mathf.Abs(i - (n - 1) * .5f));
            default: return i;
        }
    }

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
        first = 0;
        for (int i = 0; i < count; i++)
        {
            if (Rank(i, count) < Rank(first, count)) first = i;
            armed[i] = AttackStrike.Arm(spec, lanes[i], seen.y, TellSeconds + Rank(i, count) * Mathf.Max(.1f, def.shotInterval), ship.gameObject);
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
            if (armed[first] != null) armed[first].Ignite();
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
// The script sends it from station to station ("st:0|1|2"), in a fixed order; it only attacks settled on its station.
public class OysterBrain : EliteBrain
{
    public const float ReadyWait = .7f, SettleDistance = .3f;
    public static readonly float[] Columns = { -.65f, 0f, .65f };
    int station = 1;
    float settled;
    bool placed;
    public int Station => station;
    public int Hops { get; private set; }
    public int Casts { get; private set; }
    public bool Settled => settled > 0f;
    public float Height => ship.ZoneY(ship.ReachY(EliteSystem.ViewTop - def.topMargin));

    public OysterBrain() { Id = "oyster"; }

    public override Vector2 JoinFrom => Vector2.up;
    public override float SpeedScale => .8f;
    public override float MinAttackAbove => 1.5f;
    public override bool WantsAttack(Vector2 seen) => settled >= ReadyWait && Pos.y > seen.y + MinAttackAbove;
    public override float? FaceDeg(Vector2 seen) => -90f;
    public override void OnJoin() { placed = false; }

    public Vector2 StationPos(int i) => new Vector2(Columns[Mathf.Clamp(i, 0, Columns.Length - 1)] * TideFlight.Edge(def, .6f), Height);

    public override void OnScriptMove(string token)
    {
        int s;
        if (!TideFlight.TokenInt(token, "st", out s)) return;
        s = Mathf.Clamp(s, 0, Columns.Length - 1);
        if (!placed) { placed = true; station = s; return; }   // (the first station: it flies straight to it)
        if (s == station) return;
        station = s;
        Hops++;
        settled = 0f;
    }

    public override Vector2 Goal(Vector2 follow, float dt)
    {
        Clock += dt;
        Vector2 st = StationPos(station);
        if ((Pos - st).sqrMagnitude < SettleDistance * SettleDistance) settled += dt; else settled = 0f;
        return st + new Vector2(Mathf.Sin(Clock * .45f) * .2f, Mathf.Sin(Clock * .8f) * .07f);
    }

    // The attack's callback.
    public void CastDone() { Casts++; }
}

// Pearl Warden: a pearl spiral. The tell holds it on its station, nose down: two thin lanes mark the edges of the ARC the spiral will
// sweep (ArcDeg + shotSpread either side of straight down). The pilot's spot is followed for the first aimCommit of the tell and then
// locked: that direction is left OUT of the spiral (GapDeg either side) -- the pilot who stood still at the lock is in the gap. The
// action sweeps shotCount pearls from one edge of the arc to the other, shotInterval apart, out of the two shells in turn; the sweep
// runs the other way each cast. With hazardCount >= 2 it is a DOUBLE spiral: both sweeps at once, interleaved.
public class PearlSpiralAttack : EliteAttack
{
    public const float ArcDeg = 58f, GapDeg = 13f, LaneLength = 5f;
    public const int MaxPearls = 13, DoublePearls = 6;
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
    public bool Double => def.hazardCount >= 2;
    public float Arc => ArcDeg + Mathf.Max(0f, def.shotSpread);
    public int PearlCount => Mathf.Clamp(Mathf.Max(5, def.shotCount), 5, MaxPearls);

    // The angle of pearl i of n in a sweep that runs `way` (+1 counter-clockwise, -1 clockwise) over an arc of `arc` either side.
    public static float AngleOf(int i, int n, int way, float arc = ArcDeg) => -90f + way * (-arc + 2f * arc * i / Mathf.Max(1, n - 1));

    public override void BeginTell(Vector2 seen)
    {
        base.BeginTell(seen);
        acted = false;
        sweep = -sweep;
        tele.Clear();
        Vector2 o = ship.Position;
        float half = def.shotSize * .5f + .04f;
        tele.Lane(o, o + Unit(-90f - Arc) * LaneLength, half);
        tele.Lane(o, o + Unit(-90f + Arc) * LaneLength, half);
        tele.Show(TellSeconds, def.ShotColor, def.WorldIndex);
    }

    public override void StepTell(float dt) { t += dt; tele.Step(dt); }
    public override void Cancel() { tele.End(); }
    public override void OnDeath() { tele.End(); }

    void Add(float a)
    {
        if (Mathf.Abs(Mathf.DeltaAngle(a, gapDeg)) < GapDeg || total >= MaxPearls) return;
        angle[total++] = a;
    }

    public override void BeginAction()
    {
        base.BeginAction();
        acted = true;
        tele.GoLive();
        gapDeg = Deg(ship.AimSeen - ship.Position);
        int n = Double ? Mathf.Min(PearlCount, DoublePearls) : PearlCount;
        total = 0;
        float arc = Arc;
        for (int i = 0; i < n; i++)
        {
            Add(AngleOf(i, n, sweep, arc));
            if (Double) Add(AngleOf(i, n, -sweep, arc));
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
