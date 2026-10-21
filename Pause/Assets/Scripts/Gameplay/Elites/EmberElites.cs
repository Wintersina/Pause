using System.Collections.Generic;
using UnityEngine;

// Ember's six elites as mini-boss fights (Codex's HD strips, Art/Enemies/Elite/Ember/), each with its own brain and attack. The rules they
// share with the Space and Frost elites: none of them follows the pilot -- they keep their distance and antagonise, and the passes and
// dives they make are COMMITTED (locked at the tell, the pilot sidesteps); each throws HAZARDS in Ember's own material (heat needles, coal,
// scrap, ember clouds, slag, molten strips, firebombs; the cue colours are Ember's, never pink); every attack is telegraphed with the shared
// thin, faint, gently blinking drawing (ShowSight, EliteTelegraph, EliteMarks -- no thick style of its own) and its aim LOCKS at the tell.
// They launch from Ember's own sites (foundry hangar, magma bay, slag pad, furnace bay, hatch) with only the small blinking launch light, and
// the backdrop brings a site of the kind into view when none is (EmberDirector.BringHome). Storm and Desert still use the BASE brains and
// attacks (interceptor / lance_dash ... siege / siege_cannon): nothing below changes them.
//
//   Sunstoke      3  stoker    / lance_pass   (foundryhangar)  hurtHide  RAMMER: works the two side rails at three heights; a thin sight line locks
//                                              the lane across the board, then it rams across and brakes at the far rail (rocks break, no heart
//                                              lost), heat needles fan back along the lane, and it arcs up the rail to the next lane for the return pass
//   Coalrunner    3  strafer   / coal_strafe  (magmabay)       hurtHide  SWEEPER: races along the top third from rail to rail, STOPS dead at each
//                                              rail and fires its locked, crossing bolt lanes standing; coal clumps (variants) bounce down the board
//   Brass Vulture 3  scavenger / scrap_dive   (slagpad)        hurtHide  DIVER: circles high inside the view; a thin ring marks a spot (slots: on the
//                                              pilot, a hand either side) and it dives through it claw first flinging scrap, then climbs back
//   Ash Wraith    3  ashling   / ember_veil   (hatch)          hurtHide  SNIPER: blinks between three high perches in the script's order (never near the
//                                              pilot); each blink leaves a drifting EMBER CLOUD (ringed first, short-lived), then fans shards
//   Kilnback      5  kiln      / kiln_dump    (furnacebay)     armored   ANCHOR: planted at the top (the script moves it a hand at a time, never a
//                                              pendulum); slag dumps down drawn lanes and LAVA spills, a drawn lane that becomes a molten strip
//   Cauterizer    5  furnace   / fire_mortar  (furnacebay, foundryhangar) armored  ARTILLERY: parks high and tracks the pilot's column; a long charge
//                                              draws a thin sight line down its lane, then one piercing shell (two lanes with a gap in phase 2);
//                                              firebombs lobbed onto ringed spots; the last heart fires both at once
//
// None of them ROAMS (EliteBrain.Roams is false for all six): every path stays inside the view, so the ship clamps their goals, plans their
// evasions and the rails crash them like any elite. Each one's fight is authored in its def json ("script": entrance, phase 1 / 2 / last
// loops, variants, exit) -- docs/elites/elite-scripts.md -- and the brains read the script's move tokens in OnScriptMove; no coin flips,
// only EliteScriptRunner.Cycle / Alternate / Offset. Everything an attack leaves on the board rides it.

// ---- shared bits ---------------------------------------------------------------------------------------------------

public static class EmberFlight
{
    // World x beyond which an elite is wholly out of the view (what an elite leaving by a side has to clear).
    public static float OffX(EliteDef def) => SpaceFlight.OffX(def);
    // World y at which an elite is wholly above / below the view.
    public static float AboveY(EliteDef def) => FrostFlight.AboveY(def);
    public static float BelowY(EliteDef def) => FrostFlight.BelowY(def);
    // The x range a ship's centre may keep between the rails.
    public static float Edge(EliteDef def, float margin) => FrostFlight.Edge(def, margin);

    // The x of a station on a side rail: the hull `margin` clear of the inner edge.
    public static float RailX(EliteShip ship, float margin) => EliteSystem.RailEdge - ship.Def.hullRadius - margin;

    // The y of the high band, `drop` under its top: just under the HUD band and no higher than the pilot's reach.
    public static float HighY(EliteShip ship, float drop) =>
        ship.ZoneY(ship.ReachY(EliteSystem.ViewTop - ship.Def.hullRadius - .6f)) - drop;

    // Is a centre outside the board (past a rail, above the top or under the bottom)?
    public static bool OffBoard(EliteDef def, Vector2 p) =>
        Mathf.Abs(p.x) > OffX(def) - .25f || p.y > AboveY(def) - .2f || p.y < BelowY(def) + .2f;
}

// A script token with a digit argument: "lane:2", "perch:0".
public static class EmberTokens
{
    public static bool Int(string token, string prefix, out int n)
    {
        n = 0;
        if (token == null || token.Length <= prefix.Length || string.CompareOrdinal(token, 0, prefix, 0, prefix.Length) != 0) return false;
        n = token[prefix.Length] - '0';
        return n >= 0 && n <= 9;
    }
}

// The lanes an attack closes across the board always leave the pilot room to live: a gap at least MinGap wide between the rails and the
// lanes (each lane blocks `half` either side of its x). Dumps, spills and the Cauterizer's double lane are all built through KeepGap.
public static class EmberLanes
{
    public const float MinGap = 1.3f;      // u: a little over the pilot's ship (~.8 u) and its dodge
    static readonly List<float> scratch = new List<float>(16);

    // The widest clear stretch between -edge .. edge that no lane covers.
    public static float Gap(IList<float> xs, float half, float edge)
    {
        scratch.Clear();
        for (int i = 0; i < xs.Count; i++) scratch.Add(xs[i]);
        scratch.Sort();
        float cursor = -edge, best = 0f;
        for (int i = 0; i < scratch.Count; i++)
        {
            best = Mathf.Max(best, scratch[i] - half - cursor);
            cursor = Mathf.Max(cursor, scratch[i] + half);
        }
        return Mathf.Max(best, edge - cursor);
    }

    // Drops the lane nearest `from` (the pilot) until a MinGap is left; the number dropped.
    public static int KeepGap(List<float> xs, float half, float edge, float from)
    {
        int dropped = 0;
        while (xs.Count > 0 && Gap(xs, half, edge) < MinGap)
        {
            int worst = 0;
            for (int i = 1; i < xs.Count; i++) if (Mathf.Abs(xs[i] - from) < Mathf.Abs(xs[worst] - from)) worst = i;
            xs.RemoveAt(worst);
            dropped++;
        }
        return dropped;
    }

    // A lane's centre kept off a rail by its half width (and nothing narrower than `half` of room).
    public static float Clamp(float x, float half, float edge)
    {
        float e = Mathf.Max(0f, edge - half);
        return Mathf.Clamp(x, -e, e);
    }

    // The path of a bouncing shot as dotted lanes: out from `from` along `v` to the first rail, glancing off, and on, `length` in all.
    public static void Bounce(EliteTelegraph t, Vector2 from, Vector2 v, float half, float length)
    {
        Vector2 d = v.sqrMagnitude > 1e-6f ? v.normalized : Vector2.down, p = from;
        float left = length, edge = EliteSystem.RailEdge - half;
        for (int leg = 0; leg < 2 && left > .05f; leg++)
        {
            float toRail = d.x > 1e-3f ? (edge - p.x) / d.x : d.x < -1e-3f ? (-edge - p.x) / d.x : 1e6f;
            float run = Mathf.Min(left, Mathf.Max(0f, toRail));
            Vector2 q = p + d * run;
            t.Lane(p, q, half);
            left -= run;
            p = q;
            d = new Vector2(-d.x, d.y);
        }
    }
}

// A second sight line for an attack that draws two lanes at once (the ship has one of its own, ShowSight): the same thin bar, stepped
// blinking, the ship's shot colour.
public sealed class EmberSight
{
    readonly SpriteRenderer sr;
    readonly EliteDef def;

    public EmberSight(EliteShip ship)
    {
        def = ship.Def;
        var go = new GameObject("Sight2");
        go.transform.SetParent(ship.transform, false);
        sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = EliteFxArt.Sight;
        sr.sortingOrder = EliteShip.PlayOrder - 1;
        sr.enabled = false;
    }

    public bool Shown => sr.enabled;

    public void Show(Vector2 from, float deg, float length, bool on, float width)
    {
        sr.enabled = on;
        if (!on) return;
        sr.transform.position = new Vector3(from.x, from.y, 0f);
        sr.transform.rotation = Quaternion.Euler(0f, 0f, deg - 90f);
        sr.transform.localScale = new Vector3(width, length, 1f);
        Color c = def.ShotColor;
        c.a = .7f;
        sr.color = c;
    }

    public void Hide() { sr.enabled = false; }
}

// ---- Sunstoke: stoker ----------------------------------------------------------------------------------------------

// A lance runner, and a RAMMER: it works the two side rails of the view. A PASS is: take a station on a rail at one of three heights (the
// script's `lane:N` token; lane 0 the highest), take the thin sight line across the board (LancePassAttack), ram the whole way across and
// stop at the far rail; then the U-TURN: an arc up (or down) the rail to the height of the next lane, and the return pass the other way.
// It never leaves the view (the only exit is the scripted one at its last heart) and never prowls: between passes it only repositions.
public class StokerBrain : EliteBrain
{
    public enum Stage { Turn, Ready, Run }
    public const float ReadyWait = .15f, TurnScale = 1.6f, LaneStep = .85f, Settle = .45f, MaxBulge = 1.1f, RailMargin = .7f;
    public const int Lanes = 3;
    Stage stage = Stage.Turn;
    float ready, side = 1f;
    int lane;
    public Stage Phase => stage;
    public float Side => side;                   // the rail it works from: -1 left, +1 right
    public int Lane => lane;
    public int Passes { get; private set; }
    public int PassesStarted { get; private set; }
    public float WaitLeft => ready;
    public Vector2 Station => new Vector2(side * EmberFlight.RailX(ship, RailMargin), EmberFlight.HighY(ship, lane * LaneStep));

    public StokerBrain() { Id = "stoker"; }

    public override Vector2 JoinFrom => Vector2.up;
    protected override void OnBind() { side = ship.Script.Alternate("side", -1f); }
    public override void OnJoin() { stage = Stage.Turn; ready = 0f; }
    public override void OnReturn() { stage = Stage.Turn; ready = 0f; }

    public override void OnScriptMove(string token)
    {
        int n;
        if (EmberTokens.Int(token, "lane:", out n)) lane = Mathf.Clamp(n, 0, Lanes - 1);
    }

    public override Vector2 Goal(Vector2 seen, float dt)
    {
        Clock += dt;
        if (stage == Stage.Run) return Pos;
        Vector2 st = Station;
        Vector2 d = st - Pos;
        if (d.sqrMagnitude < Settle * Settle && ship.Velocity.sqrMagnitude < 1f) { stage = Stage.Ready; ready += dt; return st; }
        stage = Stage.Turn;
        ready = 0f;
        // the U-turn: bulges in off the rail while the height is still to be made, and lands on the rail as it arrives
        st.x -= side * Mathf.Min(MaxBulge, Mathf.Abs(d.y) * .6f);
        return st;
    }

    public override float SpeedScale => stage == Stage.Turn ? TurnScale : 1f;
    public override bool WantsAttack(Vector2 seen) => stage == Stage.Ready && ready >= ReadyWait;
    // waiting at its rail, prow along the lane
    public override float? FaceDeg(Vector2 seen) => stage == Stage.Ready ? (side > 0f ? 180f : 0f) : (float?)null;

    // The attack's callbacks.
    public void PassStarted() { stage = Stage.Run; PassesStarted++; }

    public void PassDone()
    {
        Passes++;
        side = -side;       // it stands at the far rail now: that is the next station's rail
        stage = Stage.Turn;
        ready = 0f;
    }
}

// Sunstoke: a lance pass. The tell holds it at its rail, prow along the board: ONE thin sight line, blinking gently, runs from the lance
// across the whole board along the lane it will take (level, or sloping toward the pilot's row, at most MaxDown / MaxUp). It follows the
// pilot's row for the first aimCommit of the tell, then LOCKS (and blinks a little faster); the action rams straight down it at dashSpeed
// -- committed, no steering, nothing for the pilot to do but leave the lane -- ploughing through any rock (no heart lost), braking into the
// far rail inside the view. As it arrives a fan of shotCount heat needles is flung back along the lane (shotSpread apart, the lane
// locked: they cross the board where the ram was). The brain then makes the U-turn to the next lane (StokerBrain).
public class LancePassAttack : EliteAttack
{
    public const float SightWidth = .05f, SightLength = 22f, MaxRunSeconds = 4f, MaxDown = .7f, MaxUp = .12f, Decel = 12f, MinSpeed = 1f, NeedlesAt = 1.2f, LowestEnd = 2f, EndMargin = .45f;
    bool ran, needles;
    float way = 1f, runLen;
    Vector2 start;
    public LancePassAttack() { Id = "lance_pass"; }
    public override bool Aimed => true;
    public override bool HoldsDuringTell => true;
    public override bool DrivesMovement => true;
    public override bool Ploughs => true;
    public override bool CommitsBlind => true;
    public override float? FaceDeg => Deg(dir);
    public bool Locked => ship.AimLocked;
    public bool NeedlesFlung => needles;
    public float Way => way;
    public float RunLength => runLen;

    public override bool FriendlyInLine(Vector2 seen) =>
        EliteOnLine(ship.Position, dir.sqrMagnitude > .5f ? dir : Vector2.down, SightLength * .5f, def.hullRadius);

    // The lane toward the far rail for a pilot seen at p: level, tipped toward the pilot's row (never steeper than MaxDown).
    public Vector2 LaneDir(Vector2 from, Vector2 p)
    {
        float slope = (p.y - from.y) / Mathf.Max(2f, Mathf.Abs(p.x - from.x));
        return new Vector2(way, Mathf.Clamp(slope, -MaxDown, MaxUp)).normalized;
    }

    public override void BeginTell(Vector2 seen)
    {
        base.BeginTell(seen);
        ran = false;
        needles = false;
        way = ship.Position.x >= 0f ? -1f : 1f;
        dir = LaneDir(ship.Position, seen);
        aim = seen;
    }

    public override void StepTell(float dt)
    {
        t += dt;
        if (!ship.AimLocked)
        {
            dir = LaneDir(ship.Position, ship.AimSeen);
            aim = ship.AimSeen;
        }
        bool on = Mathf.FloorToInt(t / ((ship.AimLocked ? 2f : 4f) * EliteArt.Tick)) % 2 == 0;
        ship.ShowSight(ship.Position + dir * def.hullRadius * .8f, Deg(dir), SightLength, on, SightWidth);
    }

    public override void Cancel() { ship.ShowSight(Vector2.zero, 0f, 0f, false); }

    public override void BeginAction()
    {
        base.BeginAction();
        ran = true;
        ship.ShowSight(Vector2.zero, 0f, 0f, false);
        start = ship.Position;
        // as far as the far rail (inside the view), and not lower than the board allows
        float farX = way * EmberFlight.RailX(ship, EndMargin);
        runLen = Mathf.Max(.5f, (farX - start.x) / dir.x);
        if (dir.y < -.01f) runLen = Mathf.Min(runLen, Mathf.Max(.5f, (start.y - (EliteSystem.ViewBottom + LowestEnd)) / -dir.y));
        ship.Drive(dir * def.dashSpeed);
        var sb = ship.Brain as StokerBrain;
        if (sb != null) sb.PassStarted();
    }

    public override bool StepAction(float dt)
    {
        t += dt;
        float left = runLen - Vector2.Dot(ship.Position - start, dir);
        // the parting shot: arriving at the far rail, heat needles back along the lane it rammed
        if (!needles && left <= NeedlesAt)
        {
            needles = true;
            float back = Deg(-dir);
            int n = Mathf.Max(1, def.shotCount);
            for (int i = 0; i < n; i++) Fire(0, back + (i - (n - 1) * .5f) * def.shotSpread, def.shotSpeed);
        }
        if (left <= .1f || t >= MaxRunSeconds)
        {
            ship.Drive(Vector2.zero);
            return true;
        }
        // full speed, then braking into the rail
        float speed = Mathf.Clamp(Mathf.Sqrt(2f * Decel * left), MinSpeed, def.dashSpeed);
        ship.Drive(dir * speed);
        return false;
    }

    public override void End()
    {
        ship.ShowSight(Vector2.zero, 0f, 0f, false);
        var sb = ship.Brain as StokerBrain;
        if (ran && sb != null) sb.PassDone();
        ran = false;
    }

    public override void OnDeath() { ship.ShowSight(Vector2.zero, 0f, 0f, false); }
}

// ---- Coalrunner: strafer ---------------------------------------------------------------------------------------------

// A strafing gunship, a SWEEPER that stops to fire. It races along the top of the view from rail to rail (the script's `L` / `R` tokens
// say which rail; the height steps down a band each leg, 3 bands, from the counter, never a coin), STOPS dead at the rail and fires its
// locked lane volley there -- it never fires on the move, and it never leaves the view. Wherever it runs it stays in the top third.
public class StraferBrain : EliteBrain
{
    public enum Stage { Run, Stop }
    public const float HaltSpeed = .6f, Settle = .3f, RunScale = 1.5f, RailMargin = .7f;
    public const int Bands = 3;
    Stage stage = Stage.Run;
    float side = 1f;
    int band;
    public Stage Phase => stage;
    public float Side => side;                   // the rail it runs to / stands at
    public int BandIndex => band;
    public int Runs { get; private set; }
    public int Volleys { get; private set; }
    public Vector2 Station
    {
        get
        {
            float lo, hi;
            Band(def, out lo, out hi);
            // (held inside the pilot's reach like every goal: the station it settles on is the one the ship will fly to)
            return new Vector2(side * EmberFlight.RailX(ship, RailMargin), ship.ReachY(Mathf.Lerp(hi, lo, band / (float)Bands)));
        }
    }

    public StraferBrain() { Id = "strafer"; }

    public override Vector2 JoinFrom => new Vector2(-1f, .8f);
    protected override void OnBind() { side = ship.Script.Alternate("side", -1f); }
    public override void OnJoin() { stage = Stage.Run; }
    public override void OnReturn() { stage = Stage.Run; }

    // The top third of the view, under the very top.
    public static void Band(EliteDef def, out float lo, out float hi)
    {
        hi = Mathf.Min(EliteSystem.ViewTop - def.hullRadius - .5f, FightZone.Top(FightZone.HalfOf(def)));
        lo = Mathf.Min(hi, EliteSystem.ViewTop - (EliteSystem.ViewTop - EliteSystem.ViewBottom) / 3f + def.hullRadius);
    }

    public override void OnScriptMove(string token)
    {
        if (token == "L" || token == "R")
        {
            side = token == "L" ? -1f : 1f;
            band = ship.Script.Cycle("band", Bands);
            Runs++;
        }
    }

    public override Vector2 Goal(Vector2 seen, float dt)
    {
        Clock += dt;
        Vector2 st = Station;
        if ((st - Pos).sqrMagnitude < Settle * Settle && ship.Velocity.sqrMagnitude < HaltSpeed * HaltSpeed) stage = Stage.Stop;
        else if (stage == Stage.Stop && (st - Pos).sqrMagnitude > 1f) stage = Stage.Run;
        return st;
    }

    // Fast along the run, ordinary once it is standing.
    public override float SpeedScale => stage == Stage.Run ? RunScale : 1f;
    // Fires only when it is standing at the rail.
    public override bool WantsAttack(Vector2 seen) => stage == Stage.Stop;

    public void VolleyDone() { Volleys++; }
}

// Coalrunner: a coal strafe. The tell HOLDS it at its rail (the strafe is over: it fires standing): from the first frame the lanes of the
// volley are drawn from its side cannons, the two cannons crossing their fans of shotCount bolts (shotSpread apart) below it at CrossDeg
// either way. The angles are locked then and there. The action sends the bolts down those lanes in one volley. With hazardCount > 0 it also
// drops that many burning COAL CLUMPS (at most two, one each way): slow, big, they skid down the board and glance off the side rails
// twice before breaking, their path drawn with the volley's.
public class CoalStrafeAttack : EliteAttack
{
    public const float LaneLength = 5.5f, CrossDeg = 24f, ClumpSpeed = 1.5f, ClumpFall = 1.6f, ClumpScale = 1.9f, LaneMargin = .05f;
    public const int MaxClumps = 2, Bounces = 2;
    readonly EliteTelegraph tele = new EliteTelegraph();
    readonly float[] angle = new float[12];
    readonly int[] muzzleOf = new int[12];
    readonly Vector2[] clumpV = new Vector2[MaxClumps];
    int bolts, clumps;
    public CoalStrafeAttack() { Id = "coal_strafe"; }

    public override bool HoldsDuringTell => true;
    public override bool HoldsDuringAction => true;

    public EliteTelegraph Telegraph => tele;
    public int Bolts => bolts;
    public int Clumps => clumps;
    public float Angle(int i) => angle[i];
    public int ClumpsNow => Mathf.Clamp(def.hazardCount, 0, MaxClumps);

    static float SideOf(EliteShip s, int m) => Mathf.Cos(s.MuzzleDeg(m) * Mathf.Deg2Rad) < 0f ? -1f : 1f;

    public override bool FriendlyInLine(Vector2 seen)
    {
        for (int m = 0; m < def.muzzles.Length; m++)
            if (EliteOnLine(ship.MuzzleWorld(m), Unit(-90f - SideOf(ship, m) * CrossDeg), LaneLength, def.shotSize * .5f)) return true;
        return false;
    }

    public override void BeginTell(Vector2 seen)
    {
        base.BeginTell(seen);
        tele.Clear();
        bolts = 0;
        int per = Mathf.Max(1, def.shotCount), cannons = Mathf.Max(1, def.muzzles.Length);
        float half = def.shotSize * .5f + LaneMargin;
        for (int m = 0; m < cannons && bolts < angle.Length; m++)
        {
            float baseDeg = -90f - SideOf(ship, m) * CrossDeg;
            Vector2 o = ship.MuzzleWorld(m);
            for (int i = 0; i < per && bolts < angle.Length; i++)
            {
                float a = baseDeg + (i - (per - 1) * .5f) * def.shotSpread;
                angle[bolts] = a;
                muzzleOf[bolts] = m;
                tele.Lane(o, o + Unit(a) * LaneLength, half);
                bolts++;
            }
        }
        // the coal: out of the belly, the first inward from the rail it stands at, the second the other way
        clumps = ClumpsNow;
        Vector2 belly = ship.Position + Vector2.down * def.hullRadius * .5f;
        float way = ship.Position.x >= 0f ? -1f : 1f;
        for (int k = 0; k < clumps; k++)
        {
            clumpV[k] = new Vector2((k == 0 ? way : -way) * ClumpSpeed, -ClumpFall);
            EmberLanes.Bounce(tele, belly, clumpV[k], def.shotSize * .5f * ClumpScale + LaneMargin, 5f);
        }
        tele.Show(TellSeconds, def.ShotColor, def.WorldIndex);
    }

    public override void StepTell(float dt) { t += dt; tele.Step(dt); }
    public override void Cancel() { tele.End(); }
    public override void OnDeath() { tele.End(); }

    public override void BeginAction()
    {
        base.BeginAction();
        tele.GoLive();
        for (int i = 0; i < bolts; i++) Fire(muzzleOf[i], angle[i], def.shotSpeed);
        Vector2 belly = ship.Position + Vector2.down * def.hullRadius * .5f;
        for (int k = 0; k < clumps; k++)
        {
            var c = FireFrom(belly, Deg(clumpV[k]), 0f, EliteShots.Kind.Slag);
            if (c == null) continue;
            c.Shrink(ClumpScale);
            c.Skid(clumpV[k]);
            c.Ricochet(Bounces);
        }
    }

    public override bool StepAction(float dt)
    {
        t += dt;
        return t >= def.actionSeconds;
    }

    public override void End()
    {
        tele.End();
        var sb = ship.Brain as StraferBrain;
        if (sb != null) sb.VolleyDone();
    }
}

// ---- Brass Vulture: scavenger ------------------------------------------------------------------------------------------

// A scrap diver. It CIRCLES high INSIDE the view, on a flat ellipse under the HUD band; after MinCircle seconds on it the script's next
// beat makes a dive (ScrapDiveAttack), it pulls up to the circle again and goes on round. The script's tokens `wide` / `tight` set the
// width of the ellipse. It never leaves the view (the only exit is the scripted one at its last heart).
public class ScavengerBrain : EliteBrain
{
    public enum Stage { Climb, Circle, Dive }
    public const float WideX = 2.0f, TightX = 1.2f, Ry = .7f, MinCircle = .6f, ClimbScale = 1.6f;
    Stage stage = Stage.Climb;
    float angle, circled, rx = WideX, dir = 1f;
    public Stage Phase => stage;
    public int Dives { get; private set; }
    public float RadiusX => rx;

    public ScavengerBrain() { Id = "scavenger"; }

    public override Vector2 JoinFrom => Vector2.up;
    protected override void OnBind() { dir = ship.Script.Alternate("dir", 1f); }
    public override void OnJoin() { stage = Stage.Climb; }
    public override void OnReturn() { stage = Stage.Climb; }

    public override void OnScriptMove(string token)
    {
        if (token == "wide") rx = WideX;
        else if (token == "tight") rx = TightX;
    }

    public Vector2 Centre(Vector2 f)
    {
        float e = EmberFlight.Edge(def, .5f);
        return new Vector2(Mathf.Clamp(f.x * .5f, -e, e), EmberFlight.HighY(ship, Ry + .35f));
    }

    public Vector2 OnCircle(Vector2 c) => c + new Vector2(Mathf.Cos(angle) * rx, Mathf.Sin(angle) * Ry);

    public override Vector2 Goal(Vector2 follow, float dt)
    {
        Clock += dt;
        Vector2 c = Centre(follow);
        switch (stage)
        {
            case Stage.Climb:
                if (Pos.y > c.y - Ry - .1f)
                {
                    stage = Stage.Circle;
                    circled = 0f;
                    angle = Mathf.Atan2((Pos.y - c.y) / Ry, (Pos.x - c.x) / rx);
                }
                return c;
            case Stage.Circle:
                angle += dir * def.speed / rx * dt;
                circled += dt;
                return OnCircle(c);
            default:
                return Pos;
        }
    }

    public override float SpeedScale => stage == Stage.Climb ? ClimbScale : 1f;
    public override bool WantsAttack(Vector2 seen) => stage == Stage.Circle && circled >= MinCircle;
    public override float? FaceDeg(Vector2 seen) => stage == Stage.Circle ? FaceTowards(new Vector2(Pos.x, EliteSystem.ViewBottom)) : (float?)null;

    // The attack's callbacks.
    public void DiveStarted() { stage = Stage.Dive; Dives++; }
    public void DiveDone() { stage = Stage.Climb; }
}

// Brass Vulture: a scrap dive. The tell holds it on its circle, claw toward the board, while a thin ring marks the spot it will dive
// through: the pilot's spot for the first aimCommit of the tell, then locked (and blinking a little faster). The ring cycles through three
// SLOTS, dive after dive: on the pilot, a hand to one side, a hand to the other (the counter, never a coin). The action dives straight
// through the ring at dashSpeed -- committed, ploughing any rock -- flinging a fan of shotCount scrap shards as it crosses it, then swings
// in a tight arc away from the nearer rail and climbs back to its circle. (The shards and the ring are all the pilot has to read: stay
// off the ring.)
public class ScrapDiveAttack : EliteAttack
{
    public enum Phase { Down, Turn, Up }
    public const float Ring = .55f, OffsetX = 1.7f, Through = .3f, TurnRate = 480f, UpScale = .9f, MaxSeconds = 4f, ExitDrop = .9f, ClimbDecel = 10f, Floor = 2.6f, RailRoom = .7f;
    public const int Slots = 3;
    EliteMarks marks;
    Vector2 spot;
    float heading, turnDir, wobble;
    bool flung, ran;
    int slot;
    Phase phase;
    public ScrapDiveAttack() { Id = "scrap_dive"; }
    protected override void OnBind() { marks = new EliteMarks(ship, 1); }
    public override bool Aimed => true;
    public override bool HoldsDuringTell => true;
    public override bool DrivesMovement => true;
    public override bool Ploughs => true;
    public override bool CommitsBlind => true;
    public override float? FaceDeg => phase == Phase.Down && !ran ? Deg(spot - ship.Position) : heading;
    public Vector2 Spot => spot;
    public Phase DivePhase => phase;
    public EliteMarks Marks => marks;
    public bool Flung => flung;
    public int Slot => slot;

    // The ring of slot `s` for a pilot at `p`: slot 0 on the pilot, 1 and 2 a hand to either side of it (away from the nearer rail first).
    public Vector2 SpotFor(Vector2 p, int s)
    {
        if (s > 0) p.x += (s == 1 ? 1f : -1f) * (p.x > 0f ? -1f : 1f) * OffsetX;
        p.x += wobble;
        float edge = EliteSystem.RailEdge - Ring - RailRoom;
        p.x = Mathf.Clamp(p.x, -edge, edge);
        p.y = Mathf.Clamp(p.y, EliteSystem.ViewBottom + Floor, Mathf.Max(EliteSystem.ViewBottom + Floor + .1f, ship.Position.y - 2.5f));
        return p;
    }

    public override void BeginTell(Vector2 seen)
    {
        base.BeginTell(seen);
        ran = false;
        flung = false;
        phase = Phase.Down;
        slot = ship.Script.Cycle("slot", Slots);
        wobble = ship.Script.Offset("aim", .3f);
        spot = SpotFor(seen, slot);
        marks.HideAll();
    }

    public override void StepTell(float dt)
    {
        t += dt;
        if (!ship.AimLocked) spot = SpotFor(ship.AimSeen, slot);
        aim = spot;
        dir = (spot - ship.Position).normalized;
        marks.Set(0, spot, Ring, def.ShotColor, ship.AimLocked);
    }

    public override void Cancel() { marks.HideAll(); }

    public override void BeginAction()
    {
        base.BeginAction();
        ran = true;
        phase = Phase.Down;
        flung = false;
        Vector2 d = spot - ship.Position;
        heading = Deg(d.sqrMagnitude > 1e-4f ? d : Vector2.down);
        ship.Drive(Unit(heading) * def.dashSpeed);
        var sb = ship.Brain as ScavengerBrain;
        if (sb != null) sb.DiveStarted();
    }

    public override bool StepAction(float dt)
    {
        t += dt;
        marks.Set(0, spot, Ring, def.ShotColor, true);
        Vector2 p = ship.Position;
        switch (phase)
        {
            case Phase.Down:
            {
                ship.Drive(Unit(heading) * def.dashSpeed);
                float past = Vector2.Dot(p - spot, Unit(heading));
                if (past >= 0f && !flung)
                {
                    // through the ring: the scrap off the claw
                    flung = true;
                    int n = Mathf.Max(1, def.shotCount);
                    for (int i = 0; i < n; i++) Fire(0, heading + (i - (n - 1) * .5f) * def.shotSpread, def.shotSpeed);
                    marks.HideAll();
                }
                if (past >= Through || p.y < EliteSystem.ViewBottom + 2.0f)
                {
                    phase = Phase.Turn;
                    turnDir = p.x < 0f ? 1f : -1f;   // swing away from the nearer rail (through 0 deg for +, through 180 for -)
                }
                break;
            }
            case Phase.Turn:
                heading += turnDir * TurnRate * dt;
                ship.Drive(Unit(heading) * def.dashSpeed * UpScale);
                if (Mathf.Abs(Mathf.DeltaAngle(heading, 90f)) < 12f) { heading = 90f; phase = Phase.Up; }
                break;
            default:
            {
                // the climb brakes into its circle's height (inside the view and the pilot's reach): no overshooting the top
                float left = EmberFlight.HighY(ship, ExitDrop) - p.y;
                ship.Drive(Vector2.up * Mathf.Min(def.dashSpeed * UpScale, Mathf.Max(1.2f, Mathf.Sqrt(2f * ClimbDecel * Mathf.Max(0f, left)))));
                break;
            }
        }
        // (never into a side rail: the swing of the turn gives way at the edge)
        float lim = EliteSystem.RailEdge - def.hullRadius - .45f;
        if (Mathf.Abs(p.x) > lim && Mathf.Sign(ship.Velocity.x) == Mathf.Sign(p.x)) ship.Drive(new Vector2(0f, ship.Velocity.y));
        // back up at its circle: the climb ends there
        bool home = phase == Phase.Up && p.y >= EmberFlight.HighY(ship, ExitDrop) - .1f;
        if (home || t >= MaxSeconds)
        {
            ship.Drive(Vector2.zero);
            return true;
        }
        return false;
    }

    public override void End()
    {
        marks.HideAll();
        var sb = ship.Brain as ScavengerBrain;
        if (ran && sb != null) sb.DiveDone();
        ran = false;
    }

    public override void OnDeath() { marks.HideAll(); }
    public override void Passive(float dt) { marks.Step(dt); }
}

// ---- Ash Wraith: ashling -------------------------------------------------------------------------------------------------

// An ember ghost, a SNIPER that blinks. It sits on one of three high PERCHES (fixed columns: left, a lower middle, right) Standoff above the
// pilot, never nearer to it than MinRange; each attack blinks it to the perch the script names (`perch:N` token: the order is the
// script's, 0 left, 1 middle, 2 right) -- and it blinks away of its own accord (FleeCooldown apart) to the farthest perch if the pilot
// closes in on it. It never gives chase and never leaves the view (the only exit is the scripted one at its last heart).
public class AshlingBrain : EliteBrain
{
    public const float MinRange = 3.4f, PerchWait = .55f, FleeShare = .75f, FleeCooldown = 2.4f, SettleDistance = .45f, MinHop = 1.3f;
    static readonly float[] Columns = { -.82f, 0f, .82f };
    int perch = -1, next = -1, lastPerch = -1, scripted = -1;
    float perched, fleeCool;
    public int Perch => perch;
    public int Next => next;
    public int Scripted => scripted;
    public int Hops { get; private set; }
    public int Flees { get; private set; }
    public int PerchCount => Columns.Length;
    public bool Settled { get; private set; }

    public AshlingBrain() { Id = "ashling"; }

    public override Vector2 JoinFrom => new Vector2(.6f, 1f);
    public override void OnJoin() { perch = Nearest(Pos.x); perched = 0f; }
    public override void OnReturn() { perch = Nearest(Pos.x); perched = 0f; fleeCool = FleeCooldown; }

    public override void OnScriptMove(string token)
    {
        int n;
        if (EmberTokens.Int(token, "perch:", out n)) scripted = Mathf.Clamp(n, 0, Columns.Length - 1);
    }

    int Nearest(float x)
    {
        float edge = EmberFlight.Edge(def, .5f);
        int best = 0;
        for (int i = 1; i < Columns.Length; i++) if (Mathf.Abs(Columns[i] * edge - x) < Mathf.Abs(Columns[best] * edge - x)) best = i;
        return best;
    }

    // Where perch `i` is now: its column, keepDistance above where the pilot is (inside the player's reach and the view).
    public Vector2 Station(int i, Vector2 f)
    {
        float edge = EmberFlight.Edge(def, .5f);
        float y = Mathf.Min(f.y + def.keepDistance, EliteSystem.ViewTop - def.hullRadius - .9f);
        return new Vector2(Columns[Mathf.Clamp(i, 0, Columns.Length - 1)] * edge, ship.ZoneY(ship.ReachY(y)));
    }

    // The perch of the next blink for a pilot at `f`, locked in `Next`: the one the script names (never the one it is on, never one within
    // MinRange of the pilot); with no such token the next perch round (a counter); when `flee`, the farthest from the pilot.
    public Vector2 ChooseNext(Vector2 f, bool flee = false)
    {
        int pick = -1;
        if (!flee && scripted >= 0 && scripted != perch && (Station(scripted, f) - f).magnitude >= MinRange) pick = scripted;
        if (pick < 0 && !flee)
        {
            int c = (perch + 1 + ship.Script.Cycle("hop", Columns.Length - 1)) % Columns.Length;
            if ((Station(c, f) - f).magnitude >= MinRange) pick = c;
        }
        if (pick < 0)
        {
            float far = -1f;
            for (int c = 0; c < Columns.Length; c++)
            {
                if (c == perch) continue;
                float d = (Station(c, f) - f).magnitude;
                if (d > far) { far = d; pick = c; }
            }
            if (pick < 0) pick = (perch + 1) % Columns.Length;
        }
        next = pick;
        return Station(pick, f);
    }

    public override Vector2 Goal(Vector2 follow, float dt)
    {
        Clock += dt;
        if (perch < 0) perch = Nearest(Pos.x);
        if (fleeCool > 0f) fleeCool -= dt;
        Vector2 st = Station(perch, follow);
        Settled = (Pos - st).sqrMagnitude < SettleDistance * SettleDistance;
        if (Settled) perched += dt;
        // the pilot has come at it: a blink away to the farthest perch (not an attack, no cloud)
        if (fleeCool <= 0f && (Pos - ship.Seen).magnitude < MinRange * FleeShare && State == EliteState.Follow)
        {
            Vector2 to = ChooseNext(ship.Seen, true);
            ship.Blink(to, false);
            Blinked(true, false);
            Flees++;
            fleeCool = FleeCooldown;
            st = Station(perch, follow);
        }
        return st + new Vector2(Mathf.Sin(Clock * 1.7f) * .1f, Mathf.Sin(Clock * 1.1f) * .08f);
    }

    EliteState State => ship.State;

    public override bool WantsAttack(Vector2 seen) =>
        Settled && perched >= PerchWait && (seen - Pos).magnitude >= MinRange * 1.05f;

    public override float? FaceDeg(Vector2 seen) => FaceTowards(seen);

    // It blinked (or tried to): `moved` false keeps the perch. An attack's blink spends the script's token.
    public void Blinked(bool moved, bool attack = true)
    {
        if (moved && next >= 0) { lastPerch = perch; perch = next; Hops++; }
        if (attack) scripted = -1;
        perched = 0f;
        Settled = false;
    }
}

// Ash Wraith: an ember veil. The tell plants it on its perch: a thin ring outlines the EMBER CLOUD it will leave where it stands (blinking,
// faster once locked), a ring the perch it will blink to (as big as a cloud when one will be left there too: hazardCount 2), and one thin
// sight line runs from that perch to the pilot's spot -- all following the pilot for the first aimCommit of the tell, then locked. The
// action leaves the cloud (an orb that drifts hazardSpeed u/s with the board for hazardSeconds, hurts on touch, one shot pops it), blinks
// to the perch (when nothing is about to arrive there; otherwise it stays), leaves a second cloud there when hazardCount is 2, and fans
// shotCount shards (shotSpread apart) down the locked line.
public class EmberVeilAttack : EliteAttack
{
    public const float CloudRing = .7f, PerchRing = .3f, SightLength = 6f, SightWidth = .045f;
    EliteMarks marks;
    Vector2 from, perchAt;
    bool blinked;
    int clouds;
    float cloudSide = 1f;
    public EmberVeilAttack() { Id = "ember_veil"; }
    protected override void OnBind() { marks = new EliteMarks(ship, 2); }
    public override bool Aimed => true;
    public override bool HoldsDuringTell => true;
    public override float? FaceDeg => Deg(dir);
    public Vector2 CloudAt => from;
    public Vector2 PerchAt => perchAt;
    public EliteMarks Marks => marks;
    public bool Blinked => blinked;
    public int Clouds => clouds;
    // Clouds a cast leaves: 1 where it stood, 2 there and where it lands.
    public int CloudsPerCast => Mathf.Clamp(def.hazardCount, 1, 2);

    public override bool FriendlyInLine(Vector2 seen)
    {
        Vector2 to = seen - perchAt;
        float dist = to.magnitude;
        return dist > 1e-3f && EliteOnLine(perchAt, to / dist, SightLength, def.shotSize);
    }

    public override void BeginTell(Vector2 seen)
    {
        base.BeginTell(seen);
        var ab = ship.Brain as AshlingBrain;
        from = ship.Position;
        perchAt = ab != null ? ab.ChooseNext(seen) : from;
        blinked = false;
        Aim(seen);
        marks.HideAll();
    }

    void Aim(Vector2 p)
    {
        Vector2 d = p - perchAt;
        if (d.sqrMagnitude > 1e-4f) { dir = d.normalized; aim = p; }
    }

    public override void StepTell(float dt)
    {
        t += dt;
        if (!ship.AimLocked) Aim(ship.AimSeen);
        bool fast = ship.AimLocked;
        marks.Set(0, from, CloudRing, def.ShotColor, fast);
        marks.Set(1, perchAt, CloudsPerCast >= 2 ? CloudRing : PerchRing, def.ShotColor, fast, .5f);
        bool on = Mathf.FloorToInt(t / ((fast ? 2f : 4f) * EliteArt.Tick)) % 2 == 0;
        ship.ShowSight(perchAt + dir * def.hullRadius, Deg(dir), SightLength, on, SightWidth);
    }

    public override void Cancel()
    {
        ship.ShowSight(Vector2.zero, 0f, 0f, false);
        marks.HideAll();
    }

    void Cloud(Vector2 at)
    {
        cloudSide = -cloudSide;
        var cloud = FireFrom(at, 0f, 0f, EliteShots.Kind.Orb);
        if (cloud == null) return;
        cloud.Mine(at, 0f, Mathf.Max(.5f, def.hazardSeconds), 1, 0f, .05f, 0f, new Vector2(def.hazardSpeed * cloudSide, 0f), 1);
        clouds++;
    }

    public override void BeginAction()
    {
        base.BeginAction();
        ship.ShowSight(Vector2.zero, 0f, 0f, false);
        marks.HideAll();
        // the cloud, where it stood
        Cloud(from);
        // the blink
        blinked = !EliteEvasion.Hold(ship, perchAt, 0f, EliteEvasion.BlinkClearSeconds).Hit;
        if (blinked)
        {
            ship.Blink(perchAt, true);
            // (a second cloud where it lands)
            if (CloudsPerCast >= 2) Cloud(perchAt);
        }
        // the fan, down the locked line
        Vector2 origin = ship.Position;
        float baseDeg = Deg(aim - origin);
        int n = Mathf.Max(1, def.shotCount);
        for (int i = 0; i < n; i++) Fire(0, baseDeg + (i - (n - 1) * .5f) * def.shotSpread, def.shotSpeed);
    }

    public override bool StepAction(float dt)
    {
        t += dt;
        return t >= def.actionSeconds;
    }

    public override void Passive(float dt) { marks.Step(dt); }

    public override void End()
    {
        ship.ShowSight(Vector2.zero, 0f, 0f, false);
        marks.HideAll();
        var ab = ship.Brain as AshlingBrain;
        if (ab != null) ab.Blinked(blinked);
    }

    // Its clouds go with it.
    public override void OnDeath()
    {
        ship.ShowSight(Vector2.zero, 0f, 0f, false);
        marks.HideAll();
        EmberHazards.PopOrbs(ship);
    }
}

public static class EmberHazards
{
    // Pops every orb (cloud, firebomb) of `owner` still on the board: it went down with them out.
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

// ---- Kilnback and Cauterizer: the top-holding kilns -----------------------------------------------------------------------

// Holds the top of the view (topMargin under it, never above the player's reach). It never comes down and never prowls; where along the
// top it stands is the script's call (tokens), not a pendulum. It attacks whenever it is over the pilot.
public abstract class EmberHoldBrain : EliteBrain
{
    protected abstract float Crawl { get; }

    public virtual float Amplitude => Mathf.Max(0f, Mathf.Min(def.laneOffset, EliteSystem.RailEdge - def.hullRadius - .6f));
    public float Height => ship.ZoneY(ship.ReachY(EliteSystem.ViewTop - def.topMargin));

    public override float SpeedScale => Crawl;
    public override float MinAttackAbove => 1.5f;
    public override bool WantsAttack(Vector2 seen) => Pos.y > seen.y + MinAttackAbove;
    public override Vector2 JoinFrom => Vector2.up;
}

// Kilnback: an ANCHOR. Planted at the top, it takes the station the script gives it (`C` the centre, `L` / `R` a hand off it) and holds
// it, hanging with a tiny sway; it does not slide while it casts and never follows the pilot.
public class KilnBrain : EmberHoldBrain
{
    float station;       // -1 left, 0 centre, +1 right
    public KilnBrain() { Id = "kiln"; }
    protected override float Crawl => .75f;
    public float Station => station;
    public float StationX => station * Amplitude * .8f;

    public override void OnScriptMove(string token)
    {
        if (token == "C") station = 0f;
        else if (token == "L") station = -1f;
        else if (token == "R") station = 1f;
    }

    public override Vector2 Goal(Vector2 follow, float dt)
    {
        Clock += dt;
        return new Vector2(StationX + Mathf.Sin(Clock * .7f) * .05f, Height + Mathf.Sin(Clock * .9f) * .08f);
    }
}

// Cauterizer: ARTILLERY. Parked high, it TRACKS the pilot's column at a crawl (its cannon's lane is where the pilot stands), and the
// script's `hold` token freezes it where it is for the beat (bombs), `track` lets it follow again.
public class FurnaceBrain : EmberHoldBrain
{
    bool tracking = true;
    float heldX;
    public FurnaceBrain() { Id = "furnace"; }
    protected override float Crawl => .65f;
    public bool Tracking => tracking;
    public override float Amplitude => Mathf.Max(0f, EliteSystem.RailEdge - def.hullRadius - .6f);

    public override void OnScriptMove(string token)
    {
        if (token == "hold") { tracking = false; heldX = Pos.x; }
        else if (token == "track") tracking = true;
    }

    public override Vector2 Goal(Vector2 follow, float dt)
    {
        Clock += dt;
        float x = tracking ? Mathf.Clamp(follow.x, -Amplitude, Amplitude) : heldX;
        return new Vector2(x, Height + Mathf.Sin(Clock * .9f) * .08f);
    }

    public override float? FaceDeg(Vector2 seen) => -90f;
}

// Kilnback: a kiln dump. The script picks the cast through hazardArmour: 0 a DUMP, 1 a LAVA SPILL.
//
// DUMP: from the first frame of the tell a thin lane is drawn down the board under each blob's x (a row centred on the pilot's spot,
// lobSpacing apart, locked at the commit); then the blobs drop out of the thrusters one after another, shotInterval apart, each gliding
// out to its lane (GlideSeconds) and sinking down it as slag. THE FIGHT ESCALATES with shotCount (the script's phases raise it; at most
// MaxBlobs). Lanes are pruned (EmberLanes.KeepGap) so a MinGap-wide gap always remains between the rails.
//
// SPILL: a lane (hazardCount of them, StripSep apart, one or two) is drawn from under the hull down SpillLen steps, locked on the
// pilot's spot; then the pools are lobbed one after another down it, each landing (lobSeconds) as a molten pool that rides the board for
// poolSeconds -- a short-lived strip. Strips too keep a MinGap-wide gap.
public class KilnDumpAttack : EliteAttack
{
    public const float LaneMargin = .06f, SpillStep = .62f, SpillInterval = .07f, DumpDrop = .9f, GlideSeconds = .4f, StripSep = 2.1f, SpillTell = 1.1f;
    public const int MaxBlobs = 7, SpillLen = 6;
    readonly EliteTelegraph tele = new EliteTelegraph();
    readonly List<float> lanes = new List<float>(10);
    int fired, spillCount;
    bool spill;
    float next, laneTop;
    public KilnDumpAttack() { Id = "kiln_dump"; }
    public override bool HoldsDuringTell => true;
    public override bool HoldsDuringAction => true;
    public override bool Aimed => true;
    public override float TellSeconds => spill ? Mathf.Max(BaseTell, def.tellSeconds * SpillTell) : BaseTell;

    public bool IsSpill => spill;
    public IReadOnlyList<float> Lanes => lanes;
    public EliteTelegraph Telegraph => tele;
    public int Dumped => fired;
    public int Blobs => Mathf.Clamp(def.shotCount, 1, MaxBlobs);
    public int Strips => Mathf.Clamp(def.hazardCount, 1, 2);
    public float BlobHalf => def.shotSize * .42f + LaneMargin;
    public float PoolHalf => def.shotSize * 2.1f * .42f + LaneMargin;
    public float Edge => EliteSystem.RailEdge - .25f;
    public float LaneTop => laneTop;
    public float StripBottom => laneTop - SpillLen * SpillStep;
    public float Half => spill ? PoolHalf : BlobHalf;

    // The lanes of a cast for a pilot at x (shared with the tests: any pilot x leaves a gap).
    public void Plan(bool spillCast, float pilotX)
    {
        lanes.Clear();
        float half = spillCast ? PoolHalf : BlobHalf;
        float cx = EmberLanes.Clamp(pilotX, half, Edge);
        if (spillCast)
        {
            lanes.Add(cx);
            if (Strips == 2) lanes.Add(EmberLanes.Clamp(cx + (cx > 0f ? -StripSep : StripSep), half, Edge));
        }
        else
        {
            int n = Blobs;
            for (int i = 0; i < n; i++)
            {
                float x = EmberLanes.Clamp(cx + (i - (n - 1) * .5f) * def.lobSpacing, half, Edge);
                bool dup = false;
                for (int j = 0; j < lanes.Count; j++) dup |= Mathf.Abs(lanes[j] - x) < half * 1.6f;
                if (!dup) lanes.Add(x);
            }
        }
        EmberLanes.KeepGap(lanes, half, Edge, pilotX);
    }

    public override bool FriendlyInLine(Vector2 seen) =>
        EliteOnLine(ship.Position, Vector2.down, 8f, .6f);

    void Draw()
    {
        tele.Clear();
        laneTop = ship.Position.y - def.hullRadius * .5f - .2f;
        float bottom = spill ? StripBottom : EliteSystem.ViewBottom - .5f;
        for (int i = 0; i < lanes.Count; i++) tele.Lane(new Vector2(lanes[i], laneTop), new Vector2(lanes[i], bottom), Half);
        tele.Show(TellSeconds, def.ShotColor, def.WorldIndex);
    }

    public override void BeginTell(Vector2 seen)
    {
        base.BeginTell(seen);
        spill = def.hazardArmour == 1;
        Plan(spill, seen.x);
        Draw();
    }

    public override void StepTell(float dt)
    {
        t += dt;
        tele.Step(dt);
    }

    public override void Cancel() { tele.End(); }
    public override void OnDeath() { tele.End(); }

    public override void BeginAction()
    {
        base.BeginAction();
        tele.GoLive();
        fired = 0;
        spillCount = 0;
        next = 0f;
    }

    public override bool StepAction(float dt)
    {
        t += dt;
        if (spill) return StepSpill();
        // the dump: blob i out of the thrusters (alternating), gliding to the foot of its lane and sinking
        while (t >= next && fired < lanes.Count)
        {
            int m = def.muzzles.Length > 0 ? fired % def.muzzles.Length : 0;
            Vector2 from = ship.MuzzleWorld(m);
            var blob = Fire(m, -90f, 0f, EliteShots.Kind.Slag);
            if (blob != null) blob.Glide(new Vector2(lanes[fired], from.y - DumpDrop), GlideSeconds, 0f);
            fired++;
            next += def.shotInterval;
        }
        return t >= def.actionSeconds && fired >= lanes.Count;
    }

    // Pools down the strip, step by step, a strip after another within each step.
    bool StepSpill()
    {
        int steps = SpillLen, total = steps * lanes.Count;
        while (t >= next && spillCount < total)
        {
            int k = spillCount / Mathf.Max(1, lanes.Count), s = spillCount % Mathf.Max(1, lanes.Count);
            Vector2 spot = new Vector2(lanes[s], laneTop - (k + .5f) * SpillStep);
            int m = def.muzzles.Length > 0 ? spillCount % def.muzzles.Length : 0;
            Vector2 target = spot + Vector2.up * (EliteSystem.Scroll * def.lobSeconds);   // (the spot rides the board while the pool is in the air)
            var pool = Fire(m, Deg(target - ship.MuzzleWorld(m)), 0f, EliteShots.Kind.Glob);
            if (pool != null) pool.Lob(target, def.lobSeconds);
            spillCount++;
            next += SpillInterval;
        }
        return t >= def.actionSeconds && spillCount >= total;
    }

    public override void End() { tele.End(); }
}

// Cauterizer: a fire mortar. The script picks the cast through hazardArmour: 0 a SHELL, 1 FIREBOMBS, 2 a DOUBLE shell (two lanes), 3 a
// shell AND firebombs at once.
//
// SHELL: the long tell plants it, nose down, and a thin sight line blinks down its lane, faster for the last part; then ONE piercing shell
// (the def's shotKind) down the lane, and a recoil. DOUBLE: TWO lanes, Sep either side of its own, drawn with two sight lines; the safe
// gap between them (and beyond them) is always at least EmberLanes.MinGap.
//
// FIREBOMBS: shotCount spots (2 or 3) are ringed around the pilot's spot, ahead of it, following it for the first aimCommit of the tell
// and then locked; then the bombs are lobbed out of the cannon one after another (shotInterval apart), each gliding (lobSeconds) to its
// ring on the board and bursting after hazardSeconds more into a small ring of hazardCount short fire bolts (hazardSpeed u/s). A bomb
// takes one player shot to defuse. (hazardCount is the ring's size: it is not varied by the script.)
public class FireMortarAttack : EliteAttack
{
    public const float SightWidth = .045f, Sep = 1.55f, SpotRing = .5f, BombTell = .8f, ArmSeconds = .5f, SpotSpread = 1.5f, Recoil = 1.6f;
    public const int ModeShell = 0, ModeBombs = 1, ModeDouble = 2, ModeBoth = 3;
    EmberSight sight2;
    EliteMarks marks;
    readonly Vector2[] spot = new Vector2[3];
    readonly float[] shownUntil = new float[3];
    int dropped, spots;
    bool shell, bombs, doubled, fired;
    float laneA, laneB, next;
    public FireMortarAttack() { Id = "fire_mortar"; }
    protected override void OnBind() { marks = new EliteMarks(ship, 3); sight2 = new EmberSight(ship); }
    public override bool HoldsDuringTell => true;
    public override bool HoldsDuringAction => true;
    public override bool Aimed => true;
    public override float? FaceDeg => -90f;
    public override float TellSeconds => bombs && !shell ? Mathf.Max(EliteDef.MinAimedTell, def.tellSeconds * BombTell) : BaseTell;

    public bool Bombs => bombs;
    public bool Shell => shell;
    public bool Doubled => doubled;
    public float LaneA => laneA;
    public float LaneB => laneB;
    public int Spots => spots;
    public Vector2 Spot(int i) => spot[i];
    public EliteMarks Marks => marks;
    public EmberSight Sight2 => sight2;
    public int BombsDropped => dropped;
    public bool ShellFired => fired;
    public float Edge => EliteSystem.RailEdge - .3f;

    // Two lanes Sep either side of the cannon's, kept wholly between the rails (so the gap between them keeps its width).
    public void PlanLanes(float cannonX, bool two, out float a, out float b)
    {
        if (!two) { a = b = cannonX; return; }
        float c = Mathf.Clamp(cannonX, -(Edge - Sep), Edge - Sep);
        a = c - Sep;
        b = c + Sep;
    }

    // Bomb spot k for a pilot at p: the first ahead of it, the others spread to either side.
    Vector2 SpotFor(Vector2 p, int k)
    {
        float x = p.x + (k == 0 ? 0f : (k % 2 == 1 ? 1f : -1f) * SpotSpread * ((k + 1) / 2));
        x = Mathf.Clamp(x, -(Edge - .1f), Edge - .1f);
        float y = Mathf.Clamp(p.y + def.lobAhead + (k == 0 ? 0f : .4f), EliteSystem.ViewBottom + .9f, Mathf.Max(EliteSystem.ViewBottom + 1f, ship.Position.y - 1.5f));
        return new Vector2(x, y);
    }

    public override bool FriendlyInLine(Vector2 seen)
    {
        if (!shell) return false;
        Vector2 p = ship.MuzzleWorld(0);
        return EliteOnLine(p, Vector2.down, p.y - EliteSystem.ViewBottom, def.shotSize * .5f + (doubled ? Sep : 0f));
    }

    public override void BeginTell(Vector2 seen)
    {
        base.BeginTell(seen);
        marks.HideAll();
        sight2.Hide();
        dropped = 0;
        fired = false;
        for (int k = 0; k < shownUntil.Length; k++) shownUntil[k] = 0f;
        int mode = def.hazardArmour;
        shell = mode != ModeBombs;
        bombs = mode == ModeBombs || mode == ModeBoth;
        doubled = mode == ModeDouble;
        spots = 0;
        if (bombs)
        {
            spots = Mathf.Clamp(def.shotCount, 2, spot.Length);
            for (int k = 0; k < spots; k++) spot[k] = SpotFor(seen, k);
            ShowSpots(false);
        }
        if (shell) PlanLanes(ship.MuzzleWorld(0).x, doubled, out laneA, out laneB);
    }

    void ShowSpots(bool locked)
    {
        for (int k = 0; k < spots; k++)
            if (shownUntil[k] <= 0f) marks.Set(k, spot[k], SpotRing, def.ShotColor, locked);
    }

    public override void StepTell(float dt)
    {
        t += dt;
        if (bombs)
        {
            if (!ship.AimLocked) for (int k = 0; k < spots; k++) spot[k] = SpotFor(ship.AimSeen, k);
            ShowSpots(ship.AimLocked);
        }
        if (!shell) return;
        // the sight line(s) down the lane(s), blinking on fours of a tick and on twos for the last third
        float y = ship.MuzzleWorld(0).y, len = Mathf.Max(.5f, y - EliteSystem.ViewBottom + .5f);
        bool on = Mathf.FloorToInt(t / ((t > TellSeconds * .7f ? 2f : 4f) * EliteArt.Tick)) % 2 == 0;
        ship.ShowSight(new Vector2(laneA, y), -90f, len, on, SightWidth);
        if (doubled) sight2.Show(new Vector2(laneB, y), -90f, len, on, SightWidth);
    }

    public override void Cancel()
    {
        ship.ShowSight(Vector2.zero, 0f, 0f, false);
        sight2.Hide();
        marks.HideAll();
    }

    public override void BeginAction()
    {
        base.BeginAction();
        ship.ShowSight(Vector2.zero, 0f, 0f, false);
        sight2.Hide();
        next = 0f;
        dropped = 0;
        for (int k = 0; k < shownUntil.Length; k++) shownUntil[k] = 0f;
    }

    public override bool StepAction(float dt)
    {
        t += dt;
        bool a = !shell || StepShell();
        bool b = !bombs || StepBombs(dt);
        return a && b;
    }

    bool StepShell()
    {
        if (!fired)
        {
            fired = true;
            var kind = EliteShots.KindOf(def.shotKind);
            float y = ship.MuzzleWorld(0).y;
            if (!doubled) Fire(0, -90f, def.shotSpeed);
            else
            {
                FireFrom(new Vector2(laneA, y), -90f, def.shotSpeed, kind);
                FireFrom(new Vector2(laneB, y), -90f, def.shotSpeed, kind);
                ship.OnFired(0, -90f);
            }
            ship.Drive(Vector2.up * Recoil);
        }
        return t >= def.actionSeconds;
    }

    bool StepBombs(float dt)
    {
        while (t >= next && dropped < spots)
        {
            Vector2 to = spot[dropped];
            Vector2 target = to + Vector2.up * (EliteSystem.Scroll * def.lobSeconds);   // (rides the board down onto its ring)
            var bomb = Fire(0, Deg(target - ship.MuzzleWorld(0)), 0f, EliteShots.Kind.Orb);
            if (bomb != null)
                bomb.Mine(target, def.lobSeconds, def.lobSeconds + def.hazardSeconds, Mathf.Max(3, def.hazardCount), def.hazardSpeed, ArmSeconds, 0f, Vector2.zero, 1);
            shownUntil[dropped] = def.lobSeconds;
            dropped++;
            next += def.shotInterval;
        }
        // a ring stays until its bomb has landed on it
        for (int k = 0; k < spots; k++)
        {
            if (shownUntil[k] > 0f)
            {
                shownUntil[k] -= dt;
                if (shownUntil[k] <= 0f) { marks.Hide(k); shownUntil[k] = -1f; }
                else marks.Set(k, spot[k], SpotRing, def.ShotColor, true);
            }
            else if (k >= dropped) marks.Set(k, spot[k], SpotRing, def.ShotColor, true);
        }
        return t >= def.actionSeconds && dropped >= spots && AllHidden();
    }

    bool AllHidden()
    {
        for (int k = 0; k < spots; k++) if (marks.Shown(k)) return false;
        return true;
    }

    public override void Passive(float dt) { marks.Step(dt); }

    public override void End()
    {
        ship.ShowSight(Vector2.zero, 0f, 0f, false);
        sight2.Hide();
        marks.HideAll();
        for (int k = 0; k < shownUntil.Length; k++) shownUntil[k] = 0f;
    }

    // Its bombs go with it.
    public override void OnDeath()
    {
        ship.ShowSight(Vector2.zero, 0f, 0f, false);
        sight2.Hide();
        marks.HideAll();
        EmberHazards.PopOrbs(ship);
    }
}
