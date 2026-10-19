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
//   Sunstoke      3  stoker    / lance_pass   (foundryhangar)  hurtHide  waits off-screen; a thin sight line locks the lane across the whole
//                                              board, then it rams through and out of the far edge (rocks break, no heart lost); heat needles
//                                              fan back along the lane; it returns from ANOTHER edge. Hurt: two passes back to back
//   Coalrunner    3  strafer   / coal_strafe  (magmabay)       hurtHide  broadside strafing runs ACROSS THE TOP THIRD, in from one side and
//                                              out the other, crossing bolt volleys down locked, drawn lanes; re-enters from the other side.
//                                              At <= 2 hearts it also drops burning coal clumps that bounce down the board off the rails
//   Brass Vulture 3  scavenger / scrap_dive   (slagpad)        hurtHide  circles high OFF-screen, then a thin ring marks a spot and it dives
//                                              through it claw first, flinging scrap, and pulls back off-screen; every heart lost adds
//                                              another, offset dive
//   Ash Wraith    3  ashling   / ember_veil   (hatch)          hurtHide  blinks between high perches at range (never near the pilot); each
//                                              blink leaves a drifting EMBER CLOUD (ringed first, short-lived, hurts on touch), then fans shards
//   Kilnback      5  kiln      / kiln_dump    (furnacebay)     armored   holds the top and slides; alternately dumps slag blobs down drawn
//                                              lanes (more per heart lost) and spills LAVA: a drawn lane that becomes a short-lived molten strip
//   Cauterizer    5  furnace   / fire_mortar  (furnacebay, foundryhangar) armored  parks high; a long charge draws a thin sight line down
//                                              its lane, then one piercing shell (two lanes with a safe gap at <= 2 hearts); between shots it
//                                              lobs FIREBOMBS onto ringed spots that burst into a small fire ring
//
// The first three ROAM (EliteBrain.Roams): their paths leave the view, so the ship neither clamps their goal nor plans evasions, and they
// are untouchable while outside the board. Everything an attack leaves on the board rides it.

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

    // Is a roamer's centre outside the board (past a rail, above the top or under the bottom)?
    public static bool OffBoard(EliteDef def, Vector2 p) =>
        Mathf.Abs(p.x) > OffX(def) - .25f || p.y > AboveY(def) - .2f || p.y < BelowY(def) + .2f;
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

// A lance runner. A PASS is: wait off the view at an edge (a side, or over the top; never the edge of the last pass), take a thin sight
// line down the lane (LancePassAttack), ram the whole way across the board and out of the far edge; then wait out of sight (PauseSeconds)
// and come back in from ANOTHER edge. Having lost a heart (and hidden, and come back) it makes PairSize passes back to back, PairGap
// apart, each from a different edge. It never prowls and never stops on the board.
public class StokerBrain : EliteBrain
{
    public enum Stage { Leave, Away, Ready, Run }
    public const float PauseSeconds = 1.6f, PairGap = .5f, ReadyWait = .15f, LeaveScale = 2.2f;
    Stage stage = Stage.Leave;
    float wait, ready;
    int edge = -1, lastEdge = -1, passesLeft;
    public Stage Phase => stage;
    public int Edge => edge;            // 0 left, 1 right, 2 over the top
    public int LastEdge => lastEdge;
    public int Passes { get; private set; }
    public int PassesStarted { get; private set; }
    public int PassesLeft => passesLeft;
    public int PairSize => ship != null && ship.Hearts < def.hearts ? 2 : 1;
    public float WaitLeft => wait;

    public StokerBrain() { Id = "stoker"; }

    public override bool Roams => true;
    public override float TellScale => 1f;
    public override Vector2 JoinFrom => Vector2.up;
    public override void OnJoin() { stage = Stage.Leave; passesLeft = 0; }
    public override void OnReturn() { stage = Stage.Leave; passesLeft = 0; }

    // Waits at a new edge: off the view, level with / above the pilot for a side, over the pilot's side of the top.
    void BeginEnter()
    {
        if (passesLeft <= 0) passesLeft = PairSize;
        int pick = Random.Range(0, 3);
        for (int tries = 0; tries < 6 && pick == lastEdge; tries++) pick = Random.Range(0, 3);
        if (pick == lastEdge) pick = (lastEdge + 1) % 3;
        edge = pick;
        Vector2 f = ship.Follow;
        Vector2 at;
        if (edge == 2) at = new Vector2(Mathf.Clamp(f.x + Random.Range(-2.4f, 2.4f), -EmberFlight.Edge(def, .5f), EmberFlight.Edge(def, .5f)), EmberFlight.AboveY(def));
        else at = new Vector2((edge == 0 ? -1f : 1f) * EmberFlight.OffX(def),
                              Mathf.Clamp(f.y + Random.Range(.8f, 3.2f), EliteSystem.ViewBottom + 2.2f, EliteSystem.ViewTop - .8f));
        ship.Warp(at);
        stage = Stage.Ready;
        ready = 0f;
    }

    public override Vector2 Goal(Vector2 seen, float dt)
    {
        Clock += dt;
        switch (stage)
        {
            case Stage.Leave:
                // just up from a dock / back from hiding: out of the view by the nearer side
                if (EmberFlight.OffBoard(def, Pos)) { stage = Stage.Away; wait = .4f; }
                return new Vector2(Mathf.Sign(Pos.x == 0f ? 1f : Pos.x) * (EmberFlight.OffX(def) + 1f), Pos.y + .3f);
            case Stage.Away:
                wait -= dt;
                if (wait <= 0f) BeginEnter();
                return Pos;
            case Stage.Ready:
                ready += dt;
                return Pos;
            default:
                return Pos;
        }
    }

    public override float SpeedScale => stage == Stage.Leave ? LeaveScale : 1f;
    public override bool WantsAttack(Vector2 seen) => stage == Stage.Ready && ready >= ReadyWait;
    // waiting at its edge, prow toward the board
    public override float? FaceDeg(Vector2 seen) => stage == Stage.Ready ? FaceTowards(Vector2.zero) : (float?)null;

    // The attack's callbacks.
    public void PassStarted() { stage = Stage.Run; PassesStarted++; }

    public void PassDone()
    {
        Passes++;
        lastEdge = edge;
        passesLeft--;
        stage = Stage.Away;
        wait = passesLeft > 0 ? PairGap : PauseSeconds;
    }
}

// Sunstoke: a lance pass. The tell holds it at its edge, prow toward the pilot: ONE thin sight line, blinking gently, runs from the lance
// across the whole board along the lane it will take. It follows the pilot's spot for the first aimCommit of the tell, then LOCKS (and
// blinks a little faster); the action rams straight down it at dashSpeed -- committed, no steering, nothing for the pilot to do but leave
// the lane -- ploughing through any rock (no heart lost), and out of the far edge. As it leaves the board a fan of shotCount heat needles
// is flung back along the lane (shotSpread apart, the lane locked: they cross the board where the ram was). The brain then waits and
// returns from another edge (StokerBrain).
public class LancePassAttack : EliteAttack
{
    public const float SightWidth = .05f, SightLength = 22f, MaxRunSeconds = 4f;
    bool ran, needles, entered;
    public LancePassAttack() { Id = "lance_pass"; }
    public override bool Aimed => true;
    public override bool HoldsDuringTell => true;
    public override bool DrivesMovement => true;
    public override bool Ploughs => true;
    public override bool CommitsBlind => true;
    public override float? FaceDeg => Deg(dir);
    public bool Locked => ship.AimLocked;
    public bool NeedlesFlung => needles;

    public override bool FriendlyInLine(Vector2 seen) =>
        EliteOnLine(ship.Position, dir.sqrMagnitude > .5f ? dir : Vector2.down, SightLength * .5f, def.hullRadius);

    public override void BeginTell(Vector2 seen)
    {
        base.BeginTell(seen);
        ran = false;
        needles = false;
        entered = false;
    }

    public override void StepTell(float dt)
    {
        t += dt;
        if (!ship.AimLocked)
        {
            Vector2 d = ship.AimSeen - ship.Position;
            if (d.sqrMagnitude > 1e-4f) { dir = d.normalized; aim = ship.AimSeen; }
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
        ship.Drive(dir * def.dashSpeed);
        var sb = ship.Brain as StokerBrain;
        if (sb != null) sb.PassStarted();
    }

    static bool Inside(Vector2 p) =>
        Mathf.Abs(p.x) < EliteSystem.RailEdge && p.y < EliteSystem.ViewTop && p.y > EliteSystem.ViewBottom;

    public override bool StepAction(float dt)
    {
        t += dt;
        ship.Drive(dir * def.dashSpeed);
        Vector2 p = ship.Position;
        bool inside = Inside(p);
        // the parting shot: as it leaves the board, heat needles back along the lane it rammed
        if (entered && !inside && !needles)
        {
            needles = true;
            float back = Deg(-dir);
            int n = Mathf.Max(1, def.shotCount);
            for (int i = 0; i < n; i++) Fire(0, back + (i - (n - 1) * .5f) * def.shotSpread, def.shotSpeed);
        }
        entered |= inside;
        // out of the far edge: wholly off the view
        return (entered && EmberFlight.OffBoard(def, p)) || t >= MaxRunSeconds;
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

// A strafing gunship. A RUN is: in from a side of the view at a height in the top third, straight across at a steady pace and out of
// the other side; it never turns on the board and never comes down to the pilot. The sides alternate. Its volleys (CoalStrafeAttack) go
// out in the window where the whole wind-up and volley still fit between the rails; MaxVolleys a run (one more at its last heart). Each
// heart lost makes the run LostBoost faster.
public class StraferBrain : EliteBrain
{
    public enum Stage { Leave, Away, Run }
    public const float PauseSeconds = 1.0f, Lead = 2.4f, LostBoost = .1f, SlopeMax = .1f, LeaveScale = 2.2f;
    public const int MaxVolleys = 2;
    Stage stage = Stage.Leave;
    float wait, s, length, dirX = 1f, afterLeave = .4f;
    Vector2 start, u = Vector2.right;
    public Stage Phase => stage;
    public float DirX => dirX;
    public Vector2 RunDir => u;
    public Vector2 Start => start;
    public int Runs { get; private set; }
    public int Volleys { get; private set; }
    public int VolleysAllowed => MaxVolleys + (ship != null && ship.Hearts <= 1 ? 1 : 0);
    float Speedup => 1f + LostBoost * Mathf.Max(0, def.hearts - (ship != null ? ship.Hearts : def.hearts));

    public StraferBrain() { Id = "strafer"; }

    public override bool Roams => true;
    public override float TellScale => 1f;
    public override Vector2 JoinFrom => new Vector2(-1f, .8f);
    protected override void OnBind() { dirX = Random.value < .5f ? -1f : 1f; }
    public override void OnJoin() { stage = Stage.Leave; }
    public override void OnReturn() { stage = Stage.Leave; }

    // The top third of the view, under the very top.
    public static void Band(EliteDef def, out float lo, out float hi)
    {
        hi = EliteSystem.ViewTop - def.hullRadius - .5f;
        lo = Mathf.Min(hi, EliteSystem.ViewTop - (EliteSystem.ViewTop - EliteSystem.ViewBottom) / 3f + def.hullRadius);
    }

    void BeginRun()
    {
        Runs++;
        dirX = -dirX;
        float lo, hi;
        Band(def, out lo, out hi);
        float y0 = Random.Range(lo, hi), off = EmberFlight.OffX(def);
        u = new Vector2(dirX, Random.Range(-SlopeMax, SlopeMax)).normalized;
        start = new Vector2(-dirX * off, y0);
        length = 2f * off / Mathf.Max(.5f, Mathf.Abs(u.x));
        s = 0f;
        Volleys = 0;
        ship.Warp(start);
        stage = Stage.Run;
    }

    public override Vector2 Goal(Vector2 seen, float dt)
    {
        Clock += dt;
        switch (stage)
        {
            case Stage.Leave:
                if (EmberFlight.OffBoard(def, Pos)) { stage = Stage.Away; wait = afterLeave; afterLeave = .4f; }
                return new Vector2(Mathf.Sign(Pos.x == 0f ? 1f : Pos.x) * (EmberFlight.OffX(def) + 1f), Pos.y + .3f);
            case Stage.Away:
                wait -= dt;
                if (wait <= 0f) BeginRun();
                return Pos;
            default:
                s += def.speed * Speedup * dt;
                // the carrot is past the far edge: it flies on out of the nearer side (it may still be on the board, lagging the carrot) and waits there
                if (s >= length) { stage = Stage.Leave; afterLeave = PauseSeconds; }
                return start + u * (s + Lead);
        }
    }

    public override float SpeedScale => stage == Stage.Leave ? LeaveScale : Speedup;

    // Where it will be when a volley started now goes out.
    public float FireX => Pos.x + dirX * def.speed * Speedup * (def.tellSeconds + .05f);

    public override bool WantsAttack(Vector2 seen)
    {
        if (stage != Stage.Run || Volleys >= VolleysAllowed) return false;
        float edge = EliteSystem.RailEdge - .45f;
        return Mathf.Abs(FireX) < edge && dirX * Pos.x < edge;
    }

    public void VolleyDone() { Volleys++; }
}

// Coalrunner: a coal strafe. The tell does NOT stop it (it keeps racing along its run): from the first frame the lanes of the volley are
// drawn from where its side cannons WILL be when it goes out (the run is straight and steady), the two cannons crossing their fans of
// shotCount bolts (shotSpread apart) below it at CrossDeg either way. The angles are locked then and there. The action sends the bolts down
// those lanes in one volley. At <= ClumpsAt hearts it also drops burning COAL CLUMPS (one at 2 hearts, two at 1, one each way): slow, big,
// they skid down the board and glance off the side rails twice before breaking, their path drawn with the volley's.
public class CoalStrafeAttack : EliteAttack
{
    public const float LaneLength = 5.5f, CrossDeg = 24f, ClumpSpeed = 1.5f, ClumpFall = 1.6f, ClumpScale = 1.9f, LaneMargin = .05f;
    public const int ClumpsAt = 2, MaxClumps = 2, Bounces = 2;
    readonly EliteTelegraph tele = new EliteTelegraph();
    readonly float[] angle = new float[12];
    readonly int[] muzzleOf = new int[12];
    readonly Vector2[] clumpV = new Vector2[MaxClumps];
    int bolts, clumps;
    public CoalStrafeAttack() { Id = "coal_strafe"; }

    public EliteTelegraph Telegraph => tele;
    public int Bolts => bolts;
    public int Clumps => clumps;
    public float Angle(int i) => angle[i];
    public int ClumpsFor(int hearts) => hearts <= ClumpsAt ? Mathf.Clamp(ClumpsAt + 1 - hearts, 1, MaxClumps) : 0;

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
        Vector2 shift = ship.Velocity * TellSeconds;
        int per = Mathf.Max(1, def.shotCount), cannons = Mathf.Max(1, def.muzzles.Length);
        float half = def.shotSize * .5f + LaneMargin;
        for (int m = 0; m < cannons && bolts < angle.Length; m++)
        {
            float baseDeg = -90f - SideOf(ship, m) * CrossDeg;
            Vector2 o = ship.MuzzleWorld(m) + shift;
            for (int i = 0; i < per && bolts < angle.Length; i++)
            {
                float a = baseDeg + (i - (per - 1) * .5f) * def.shotSpread;
                angle[bolts] = a;
                muzzleOf[bolts] = m;
                tele.Lane(o, o + Unit(a) * LaneLength, half);
                bolts++;
            }
        }
        // the coal: out of the belly, carrying on the way the run goes (the second one back)
        clumps = ClumpsFor(ship.Hearts);
        Vector2 belly = ship.Position + shift + Vector2.down * def.hullRadius * .5f;
        float way = Mathf.Sign(ship.Velocity.x == 0f ? 1f : ship.Velocity.x);
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

// A scrap diver. It CIRCLES high, wholly off the view (a ring of Radius over the top); after CircleSeconds it makes a dive
// (ScrapDiveAttack), climbs back off the top and circles again. A cycle is 1 + (hearts lost, at most ExtraDives) dives, the later ones
// after only ShortCircle seconds and each at a spot offset from the first.
public class ScavengerBrain : EliteBrain
{
    public enum Stage { Climb, Circle, Dive }
    public const float Radius = 1.9f, CircleSeconds = 1.5f, ShortCircle = .5f, ClimbScale = 2f;
    public const int ExtraDives = 2;
    Stage stage = Stage.Climb;
    float angle, circled;
    int divesLeft, diveIndex;
    public Stage Phase => stage;
    public int Dives { get; private set; }
    public int DiveIndex => diveIndex;
    public int DivesLeft => divesLeft;
    public int PlanDives => 1 + Mathf.Min(ExtraDives, ship != null ? Mathf.Max(0, def.hearts - ship.Hearts) : 0);

    public ScavengerBrain() { Id = "scavenger"; }

    public override bool Roams => true;
    public override float TellScale => 1f;
    public override Vector2 JoinFrom => Vector2.up;
    public override void OnJoin() { stage = Stage.Climb; divesLeft = 0; diveIndex = 0; }
    public override void OnReturn() { stage = Stage.Climb; divesLeft = 0; diveIndex = 0; }

    public Vector2 Centre(Vector2 f)
    {
        float e = EmberFlight.Edge(def, .5f);
        return new Vector2(Mathf.Clamp(f.x * .5f, -e, e), EmberFlight.AboveY(def) + Radius + .25f);
    }

    public override Vector2 Goal(Vector2 follow, float dt)
    {
        Clock += dt;
        Vector2 c = Centre(follow);
        switch (stage)
        {
            case Stage.Climb:
                if (Pos.y > EmberFlight.AboveY(def) + .1f)
                {
                    stage = Stage.Circle;
                    circled = 0f;
                    Vector2 r = Pos - c;
                    angle = Mathf.Atan2(r.y, r.x);
                    if (divesLeft <= 0) { divesLeft = PlanDives; diveIndex = 0; }
                }
                return new Vector2(Pos.x, EmberFlight.AboveY(def) + Radius + .9f);
            case Stage.Circle:
                angle += def.speed / Radius * dt;
                circled += dt;
                return c + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * Radius;
            default:
                return Pos;
        }
    }

    public override float SpeedScale => stage == Stage.Climb ? ClimbScale : 1f;
    public override bool WantsAttack(Vector2 seen) => stage == Stage.Circle && circled >= (diveIndex > 0 ? ShortCircle : CircleSeconds);
    public override float? FaceDeg(Vector2 seen) => stage == Stage.Circle ? FaceTowards(new Vector2(Pos.x, EliteSystem.ViewBottom)) : (float?)null;

    // The attack's callbacks.
    public void DiveStarted() { stage = Stage.Dive; Dives++; }

    public void DiveDone()
    {
        divesLeft--;
        diveIndex++;
        stage = Stage.Climb;
        if (divesLeft <= 0) diveIndex = 0;
    }
}

// Brass Vulture: a scrap dive. The tell holds it on its circle, off the view, claw toward the board, while a thin ring marks the spot it
// will dive through: the pilot's spot for the first aimCommit of the tell, then locked (and blinking a little faster); a later dive of a
// cycle rings a spot OffsetX to the side of the first (alternating). The action dives straight through the ring at dashSpeed -- committed,
// ploughing any rock -- flinging a fan of shotCount scrap shards as it crosses it, then swings in a tight arc away from the nearer rail
// and climbs back off the top of the view. (The shards and the ring are all the pilot has to read: stay off the ring.)
public class ScrapDiveAttack : EliteAttack
{
    public enum Phase { Down, Turn, Up }
    public const float Ring = .55f, OffsetX = 1.7f, Through = 1.0f, TurnRate = 300f, UpScale = .9f, MaxSeconds = 4f;
    EliteMarks marks;
    Vector2 spot;
    float heading, turnDir;
    bool flung, ran;
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

    // The ring of dive number `index` of a cycle for a pilot at `p`: the first on the pilot, the others alternately to either side of it.
    public Vector2 SpotFor(Vector2 p, int index)
    {
        if (index > 0) p.x += (index % 2 == 1 ? (p.x > 0f ? -1f : 1f) : (p.x > 0f ? 1f : -1f)) * OffsetX * ((index + 1) / 2);
        float edge = EliteSystem.RailEdge - Ring;
        p.x = Mathf.Clamp(p.x, -edge, edge);
        p.y = Mathf.Clamp(p.y, EliteSystem.ViewBottom + 1.3f, Mathf.Max(EliteSystem.ViewBottom + 1.4f, ship.Position.y - 2.5f));
        return p;
    }

    int Index { get { var sb = ship.Brain as ScavengerBrain; return sb != null ? sb.DiveIndex : 0; } }

    public override void BeginTell(Vector2 seen)
    {
        base.BeginTell(seen);
        ran = false;
        flung = false;
        phase = Phase.Down;
        spot = SpotFor(seen, Index);
        marks.HideAll();
    }

    public override void StepTell(float dt)
    {
        t += dt;
        if (!ship.AimLocked) spot = SpotFor(ship.AimSeen, Index);
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
                if (past >= Through || p.y < EliteSystem.ViewBottom + .3f)
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
                ship.Drive(Vector2.up * def.dashSpeed * UpScale);
                break;
        }
        return (phase == Phase.Up && p.y > EmberFlight.AboveY(def) - .3f) || t >= MaxSeconds;
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

// An ember ghost. It sits on one of four high PERCHES (fixed columns, alternate ones a little lower) Standoff above the pilot, never
// nearer to it than MinRange; each attack blinks it to another perch -- never the one it is on, never one within MinRange of the pilot --
// and it blinks away of its own accord (FleeCooldown apart) if the pilot closes in on it. It never gives chase.
public class AshlingBrain : EliteBrain
{
    public const float MinRange = 3.4f, PerchWait = .55f, FleeShare = .75f, FleeCooldown = 2.4f, SettleDistance = .45f, MinHop = 1.3f;
    static readonly float[] Columns = { -.82f, -.28f, .28f, .82f };
    int perch = -1, next = -1, lastPerch = -1;
    float perched, fleeCool;
    public int Perch => perch;
    public int Next => next;
    public int Hops { get; private set; }
    public int Flees { get; private set; }
    public int PerchCount => Columns.Length;
    public bool Settled { get; private set; }

    public AshlingBrain() { Id = "ashling"; }

    public override Vector2 JoinFrom => new Vector2(.6f, 1f);
    public override void OnJoin() { perch = Nearest(Pos.x); perched = 0f; }
    public override void OnReturn() { perch = Nearest(Pos.x); perched = 0f; fleeCool = FleeCooldown; }

    int Nearest(float x)
    {
        float edge = EmberFlight.Edge(def, .5f);
        int best = 0;
        for (int i = 1; i < Columns.Length; i++) if (Mathf.Abs(Columns[i] * edge - x) < Mathf.Abs(Columns[best] * edge - x)) best = i;
        return best;
    }

    // Where perch `i` is now: its column, keepDistance above where the pilot is (inside the player's reach and the view), odd ones lower.
    public Vector2 Station(int i, Vector2 f)
    {
        float edge = EmberFlight.Edge(def, .5f);
        float y = Mathf.Min(f.y + def.keepDistance, EliteSystem.ViewTop - def.hullRadius - .9f) - (i % 2 == 1 ? .7f : 0f);
        return new Vector2(Columns[Mathf.Clamp(i, 0, Columns.Length - 1)] * edge, ship.ReachY(y));
    }

    // The next perch for a pilot at `f`: not this one, not the last, none within MinRange of the pilot (else the farthest); locked in `Next`.
    public Vector2 ChooseNext(Vector2 f)
    {
        int pick = -1;
        float far = -1f;
        int farPerch = -1;
        for (int tries = 0; tries < 10 && pick < 0; tries++)
        {
            int c = Random.Range(0, Columns.Length);
            if (c == perch || (c == lastPerch && tries < 6)) continue;
            Vector2 st = Station(c, f);
            if ((st - f).magnitude < MinRange || (st - Pos).magnitude < MinHop) continue;
            pick = c;
        }
        if (pick < 0)
        {
            for (int c = 0; c < Columns.Length; c++)
            {
                if (c == perch) continue;
                float d = (Station(c, f) - f).magnitude;
                if (d > far) { far = d; farPerch = c; }
            }
            pick = farPerch >= 0 ? farPerch : (perch + 1) % Columns.Length;
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
            Vector2 to = ChooseNext(ship.Seen);
            ship.Blink(to, false);
            Blinked(true);
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

    // It blinked (or tried to): `moved` false keeps the perch.
    public void Blinked(bool moved)
    {
        if (moved && next >= 0) { lastPerch = perch; perch = next; Hops++; }
        perched = 0f;
        Settled = false;
    }
}

// Ash Wraith: an ember veil. The tell plants it on its perch: a thin ring outlines the EMBER CLOUD it will leave where it stands (blinking,
// faster once locked), a smaller faint ring the perch it will blink to, and one thin sight line runs from that perch to the pilot's
// spot -- all following the pilot for the first aimCommit of the tell, then locked. The action leaves the cloud (an orb that drifts
// hazardSpeed u/s with the board for hazardSeconds, hurts on touch, one shot pops it), blinks to the perch (when nothing is about to arrive
// there; otherwise it stays) and fans shotCount shards (shotSpread apart) down the locked line.
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
        marks.Set(1, perchAt, PerchRing, def.ShotColor, fast, .5f);
        bool on = Mathf.FloorToInt(t / ((fast ? 2f : 4f) * EliteArt.Tick)) % 2 == 0;
        ship.ShowSight(perchAt + dir * def.hullRadius, Deg(dir), SightLength, on, SightWidth);
    }

    public override void Cancel()
    {
        ship.ShowSight(Vector2.zero, 0f, 0f, false);
        marks.HideAll();
    }

    public override void BeginAction()
    {
        base.BeginAction();
        ship.ShowSight(Vector2.zero, 0f, 0f, false);
        marks.HideAll();
        // the cloud, where it stood
        cloudSide = -cloudSide;
        var cloud = FireFrom(from, 0f, 0f, EliteShots.Kind.Orb);
        if (cloud != null)
        {
            cloud.Mine(from, 0f, Mathf.Max(.5f, def.hazardSeconds), 1, 0f, .05f, 0f, new Vector2(def.hazardSpeed * cloudSide, 0f), 1);
            clouds++;
        }
        // the blink
        blinked = !EliteEvasion.Hold(ship, perchAt, 0f, EliteEvasion.BlinkClearSeconds).Hit;
        if (blinked) ship.Blink(perchAt, true);
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

// Holds the top of the view (topMargin under it, never above the player's reach) and slides a slow sine laneOffset either side of the
// middle: it never comes down and never follows the pilot's lane. It attacks whenever it is over the pilot.
public abstract class EmberHoldBrain : EliteBrain
{
    float phase;
    protected abstract float Rate { get; }
    protected abstract float Crawl { get; }

    protected override void OnBind() { phase = Random.value * Mathf.PI * 2f; }

    public float Amplitude => Mathf.Max(0f, Mathf.Min(def.laneOffset, EliteSystem.RailEdge - def.hullRadius - .6f));
    public float Height => ship.ReachY(EliteSystem.ViewTop - def.topMargin);

    public override Vector2 Goal(Vector2 follow, float dt)
    {
        Clock += dt;
        return new Vector2(Mathf.Sin(Clock * Rate + phase) * Amplitude, Height + Mathf.Sin(Clock * .9f) * .08f);
    }

    public override float SpeedScale => Crawl;
    public override float MinAttackAbove => 1.5f;
    public override bool WantsAttack(Vector2 seen) => Pos.y > seen.y + MinAttackAbove;
    public override Vector2 JoinFrom => Vector2.up;
}

public class KilnBrain : EmberHoldBrain
{
    public KilnBrain() { Id = "kiln"; }
    protected override float Rate => .45f;
    protected override float Crawl => .75f;
}

public class FurnaceBrain : EmberHoldBrain
{
    public FurnaceBrain() { Id = "furnace"; }
    protected override float Rate => .3f;
    protected override float Crawl => .65f;
    public override float? FaceDeg(Vector2 seen) => -90f;
}

// Kilnback: a kiln dump. Casts alternate: a DUMP, then a LAVA SPILL.
//
// DUMP: from the first frame of the tell a thin lane is drawn down the board under each blob's x (a row centred on the pilot's spot,
// lobSpacing apart, locked at the commit); then the blobs drop out of the thrusters one after another, shotInterval apart, each gliding
// out to its lane (GlideSeconds) and sinking down it as slag. THE FIGHT ESCALATES: shotCount blobs, and one more for every heart lost
// (MaxBlobs). Lanes are pruned (EmberLanes.KeepGap) so a MinGap-wide gap always remains between the rails.
//
// SPILL: a lane (two once it has lost SecondStripAt hearts, StripSep apart) is drawn from under the hull down SpillLen steps, locked on the
// pilot's spot; then the pools are lobbed one after another down it, each landing (lobSeconds) as a molten pool that rides the board for
// poolSeconds -- a short-lived strip. Strips too keep a MinGap-wide gap.
public class KilnDumpAttack : EliteAttack
{
    public const float LaneMargin = .06f, SpillStep = .62f, SpillInterval = .07f, DumpDrop = .9f, GlideSeconds = .4f, StripSep = 2.1f, SpillTell = 1.1f;
    public const int MaxBlobs = 7, SpillLen = 6, SecondStripAt = 3;
    readonly EliteTelegraph tele = new EliteTelegraph();
    readonly List<float> lanes = new List<float>(10);
    int casts, fired, spillCount;
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
    public int HeartsLost => Mathf.Max(0, def.hearts - ship.Hearts);
    public int Blobs => Mathf.Min(MaxBlobs, Mathf.Max(1, def.shotCount) + HeartsLost);
    public int Strips => ship.Hearts <= SecondStripAt ? 2 : 1;
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
        spill = casts++ % 2 == 1;
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

// Cauterizer: a fire mortar. Casts alternate: a SHELL, then FIREBOMBS (a shell is only due when the pilot is within LaneReach of its
// lane -- otherwise it lobs bombs again and keeps the shell for the next cast).
//
// SHELL: the long tell plants it, nose down, and a thin sight line blinks down its lane, faster for the last part; then ONE piercing shell
// (the def's shotKind) down the lane, and a recoil. At <= TwoLanesAt hearts it fires TWO lanes, Sep either side of its own, drawn with
// two sight lines; the safe gap between them (and beyond them) is always at least EmberLanes.MinGap.
//
// FIREBOMBS: shotCount spots (a third at <= TwoLanesAt hearts) are ringed around the pilot's spot, ahead of it, following it for the first
// aimCommit of the tell and then locked; then the bombs are lobbed out of the cannon one after another (shotInterval apart), each gliding
// (lobSeconds) to its ring on the board and bursting after hazardSeconds more into a small ring of hazardCount short fire bolts
// (hazardSpeed u/s). A bomb takes one player shot to defuse.
public class FireMortarAttack : EliteAttack
{
    public const float SightWidth = .045f, LaneReach = 1.25f, Sep = 1.55f, SpotRing = .5f, BombTell = .8f, ArmSeconds = .5f, SpotSpread = 1.5f, Recoil = 1.6f;
    public const int TwoLanesAt = 2;
    EmberSight sight2;
    EliteMarks marks;
    readonly Vector2[] spot = new Vector2[3];
    readonly float[] shownUntil = new float[3];
    int dropped, spots;
    bool bombs, shellNext = true, doubled, fired;
    float laneA, laneB, next;
    public FireMortarAttack() { Id = "fire_mortar"; }
    protected override void OnBind() { marks = new EliteMarks(ship, 3); sight2 = new EmberSight(ship); }
    public override bool HoldsDuringTell => true;
    public override bool HoldsDuringAction => true;
    public override bool Aimed => true;
    public override float? FaceDeg => -90f;
    public override float TellSeconds => bombs ? Mathf.Max(EliteDef.MinAimedTell, def.tellSeconds * BombTell) : BaseTell;

    public bool Bombs => bombs;
    public bool Doubled => doubled;
    public float LaneA => laneA;
    public float LaneB => laneB;
    public int Spots => spots;
    public Vector2 Spot(int i) => spot[i];
    public EliteMarks Marks => marks;
    public EmberSight Sight2 => sight2;
    public int BombsDropped => dropped;
    public bool ShellFired => fired;
    public bool ShellDue(float pilotX) => shellNext && Mathf.Abs(pilotX - ship.MuzzleWorld(0).x) < LaneReach;
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
        if (bombs) return false;
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
        bombs = !ShellDue(seen.x);
        // a bomb cast that was only a stand-in for a shell leaves the shell due; one that was its turn hands over to the shell
        if (bombs && !shellNext) shellNext = true;
        else if (!bombs) shellNext = false;
        if (bombs)
        {
            spots = Mathf.Max(2, def.shotCount) + (ship.Hearts <= TwoLanesAt ? 1 : 0);
            spots = Mathf.Min(spots, spot.Length);
            for (int k = 0; k < spots; k++) spot[k] = SpotFor(seen, k);
            ShowSpots(false);
            return;
        }
        doubled = ship.Hearts <= TwoLanesAt;
        PlanLanes(ship.MuzzleWorld(0).x, doubled, out laneA, out laneB);
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
            return;
        }
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
        return bombs ? StepBombs(dt) : StepShell();
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
