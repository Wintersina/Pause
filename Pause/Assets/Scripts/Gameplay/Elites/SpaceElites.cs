using System.Collections.Generic;
using UnityEngine;

// Space's four elites as mini-boss fights (Codex's burn strips, Art/Enemies/Elite/Space/), each with its own brain and its own
// engagement pattern. The rules they share: none of them follows the pilot -- they keep their distance and antagonise; each throws
// HAZARDS (a rift mine, a volley that crosses on the pilot's spot, a curtain with a narrowing gap, a flung asteroid); every attack
// is telegraphed before it can hurt and its aim LOCKS at the tell. Pink cues are Space's own (the pink-only-in-Space rule). They
// launch from Space's docks as ever (stations, planets, big asteroids; one small light blinks at the door in the launch
// second, EliteLaunchTell).
//
//   Rift Lancer         sniper   / rift_snipe    (station)   an edge sniper: perches at long range on a flank, a blinking sight line
//                                                  tracks then locks, a rail of bolts; after 1-2 shots it leaves by its own side and
//                                                  re-enters from the OPPOSITE one. Every third run it leaves a RIFT MINE on the line.
//   Orbit Reaver        swooper  / swoop_volley  (planet)    big strafing loops off the view and back round, a new side and slope each
//                                                  pass; locks the pilot's spot (a marker ring) and crosses its claw shards on it
//   Eventide Bastion    fortress / ward_barrage  (station)   holds the top and slides at a crawl; ward curtains with a gap that narrows
//                                                  as hearts drop (two staggered rows at <= 2), shield mines between them
//   Singularity Hauler  wrecker  / tow_sling     (asteroid)  parks high; tows a big asteroid into its claws, flings it down a marked
//                                                  lane; alternates with a gravity well that curves its shots in and gently tugs the pilot
//
// The lancer and reaver ROAM (EliteBrain.Roams): their paths leave the view, so the ship neither clamps their goal nor plans
// evasions, and they are untouchable while outside the board. Everything an attack leaves on the board rides it.

// ---- shared bits ----------------------------------------------------------------------------------------------------

public static class SpaceFlight
{
    // World x beyond which an elite is wholly out of the view (what an elite leaving by a side has to clear).
    public static float OffX(EliteDef def) => Mathf.Max(EliteSystem.RailEdge, EliteSystem.ViewHalfWidth) + def.cellWorldSize * .7f;
}

// A few blinking ring marks the attack owns (lock rings, mine drops, the well): stepped hard like every tell, no allocation after
// the build. World-positioned; children of the ship so they go with it.
public sealed class EliteMarks
{
    public const int Order = 6;
    readonly SpriteRenderer[] rings;
    readonly Sprite art;
    readonly bool sprite0;   // a custom mark (an icicle glyph), not the ring: drawn at full size
    float clock;

    // `sprite`: a mark other than the ring (Frost's icicle glyphs).
    public EliteMarks(EliteShip ship, int n, Sprite sprite = null)
    {
        sprite0 = sprite != null;
        art = sprite != null ? sprite : EliteFxArt.MarkRing;
        var root = new GameObject("Marks").transform;
        root.SetParent(ship.transform, false);
        rings = new SpriteRenderer[n];
        for (int i = 0; i < n; i++)
        {
            var go = new GameObject("Mark" + i);
            go.transform.SetParent(root, false);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = art;
            sr.sortingOrder = Order;
            sr.enabled = false;
            rings[i] = sr;
        }
    }

    public int Count => rings.Length;
    public bool Shown(int i) => i >= 0 && i < rings.Length && rings[i].enabled;
    public Vector2 At(int i) => rings[i].transform.position;
    public float Radius(int i) => rings[i].transform.localScale.x * art.bounds.size.x * .5f;

    public int ShownCount
    {
        get
        {
            int n = 0;
            for (int i = 0; i < rings.Length; i++) if (rings[i].enabled) n++;
            return n;
        }
    }

    public void Step(float dt) { clock += dt; }

    // Ring i at `at`, `radius` wide; blinking on fours of a tick, on twos once `fast` (locked); `alpha` dims it.
    public void Set(int i, Vector2 at, float radius, Color c, bool fast, float alpha = 1f)
    {
        if (i < 0 || i >= rings.Length) return;
        var r = rings[i];
        r.enabled = true;
        r.transform.position = new Vector3(at.x, at.y, 0f);
        r.transform.localScale = Vector3.one * radius * (sprite0 ? 1f : TelegraphStyle.RingScale) * 2f / Mathf.Max(.01f, art.bounds.size.x);
        bool on = Mathf.FloorToInt(clock / ((fast ? 2f : 4f) * EliteArt.Tick)) % 2 == 0;
        c.a = (on ? TelegraphStyle.RingAlpha : TelegraphStyle.RingAlphaLow) * alpha;
        r.color = c;
    }

    public void Hide(int i) { if (i >= 0 && i < rings.Length) rings[i].enabled = false; }
    public void HideAll() { for (int i = 0; i < rings.Length; i++) rings[i].enabled = false; }
}

// The Singularity Hauler's gravity well and what it does to the pilot: a gentle pull on the ship, as a capped finger offset
// (movePlayer adds Offset to where the finger puts the ship, as the weather does with WorldHazards.PlayerOffset). It is a fraction of
// normal steering -- Cap u at the most, the finger always wins -- and the pull dies away as the pilot nears the well (Strength is
// zero inside ring + Margin), so it can never drag the ship into the ring. It lasts the seconds the well is up and then fades.
public static class EliteWell
{
    public const float Cap = .32f;       // u: the most the ship is ever displaced from the finger
    public const float Gain = .7f;       // u/s^2 at full strength
    public const float Return = 2.2f;    // 1/s: the displacement fades back to the finger
    public const float Range = 3.4f;     // u: no pull beyond this from the well
    public const float Margin = .6f;     // u: no pull within ring + this of the well's centre
    static bool up;
    static Vector2 at;
    static float ring, left;

    public static Vector2 Offset { get; private set; }
    public static bool Up => up;
    public static Vector2 At => at;
    public static float SecondsLeft => left;

    // 0 .. 1: how hard the well pulls a ship `dist` from its centre (a ring of `ringRadius`): nothing inside the margin, then a hump
    // that fades out at Range.
    public static float Strength(float dist, float ringRadius)
    {
        float inner = ringRadius + Margin;
        if (dist <= inner || dist >= Range) return 0f;
        float k = (dist - inner) / (Range - inner);
        return Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((dist - inner) / .5f)) * (1f - k);
    }

    public static void Open(Vector2 position, float ringRadius, float seconds)
    {
        up = true;
        at = position;
        ring = ringRadius;
        left = seconds;
    }

    // The well rides the board: the attack moves it each step.
    public static void MoveTo(Vector2 position) { at = position; }
    public static void Close() { up = false; left = 0f; }
    public static void Reset() { up = false; left = 0f; Offset = Vector2.zero; }

    public static void Step(float dt)
    {
        if (dt <= 0f) return;
        Vector2 off = Offset;
        var p = up ? EliteSystem.Player : null;   // (only while a well is up: the lookup is not free)
        if (up && p != null)
        {
            left -= dt;
            if (left <= 0f) up = false;
            else
            {
                Vector2 d = at - (Vector2)p.position;
                float dist = d.magnitude;
                if (dist > 1e-3f) off += d / dist * (Gain * Strength(dist, ring) * dt);
            }
        }
        off *= Mathf.Exp(-Return * dt);
        Offset = Vector2.ClampMagnitude(off, Cap);
        if (Offset.sqrMagnitude < 1e-8f) Offset = Vector2.zero;
    }
}

// An asteroid the Singularity Hauler tows in and flings: a real Space rock (tag Astr: hurts on touch, shot apart by the pilot's
// weapons, killed by a shielded ram) taken off its scroll mover and flown by this -- held in the claws, then flung at Velocity
// down its marked lane, glancing off the side rails (MaxBounces, Restitution) like a body with weight, and gone off the bottom.
public sealed class ThrownRock : MonoBehaviour, IMovementFootprint
{
    public const int MaxBounces = 2;
    public const float Restitution = .7f, MaxAge = 14f;
    static readonly List<ThrownRock> all = new List<ThrownRock>();

    public EliteShip mother;
    public Vector2 Velocity;
    public bool Flung { get; private set; }
    public int Bounces { get; private set; }
    public float Age { get; private set; }
    public static IReadOnlyList<ThrownRock> All => all;

    public static ThrownRock Attach(GameObject rock, EliteShip mother)
    {
        var t = rock.AddComponent<ThrownRock>();
        t.mother = mother;
        all.Add(t);
        return t;
    }

    public static void StepAll(float dt)
    {
        if (dt <= 0f) return;
        for (int i = all.Count - 1; i >= 0; i--)
        {
            var r = all[i];
            if (r == null) { all.RemoveAt(i); continue; }
            r.Step(dt);
        }
    }

    public static void Clear() { all.Clear(); }

    // Its hauler went down with the rock still in its claws: a blast, nothing paid.
    public static void Scuttle(GameObject rock) { EliteShip.FriendlyKill(rock); }

    public void Fling(Vector2 velocity)
    {
        Flung = true;
        Velocity = velocity;
    }

    void Step(float dt)
    {
        Age += dt;
        if (!Flung) return;
        Vector3 p = transform.position;
        p.x += Velocity.x * dt;
        p.y += Velocity.y * dt;
        float edge = EliteSystem.RailEdge - Radius;
        if (Mathf.Abs(p.x) > edge && Mathf.Sign(Velocity.x) == Mathf.Sign(p.x) && Bounces < MaxBounces)
        {
            Bounces++;
            Velocity.x = -Velocity.x * Restitution;
            p.x = Mathf.Sign(p.x) * edge;
            EliteSystem.Fx.Sparks(new Vector2(Mathf.Sign(p.x) * EliteSystem.RailEdge, p.y), new Color(1f, .6f, .9f), 5);
        }
        transform.position = p;
        if (p.y < EliteSystem.ViewBottom - 1.5f || Age > MaxAge) Retire();
    }

    float Radius
    {
        get
        {
            var ct = GetComponent<ClearTarget>();
            return ct != null ? ct.Radius : .4f;
        }
    }

    void Retire()
    {
        all.Remove(this);
        ClearTarget.Release(gameObject);
        BossUtil.Kill(gameObject);
    }

    void OnDestroy() { all.Remove(this); }

    // IMovementFootprint (SpawnSpace): it flies itself.
    public Rect SweptBounds(Vector2 center, Vector2 half, float from, float to)
    {
        float own = Mathf.Max(Velocity.magnitude, 3f) * to;
        return Rect.MinMaxRect(center.x - half.x - own, center.y - half.y - own, center.x + half.x + own, center.y + half.y + own);
    }

    public bool SelfSteering => true;
}

// ---- Rift Lancer: sniper -------------------------------------------------------------------------------------------

// An edge sniper. A RUN is: enter from a side of the view, perch at long range on that flank (the rail side, Standoff above the
// pilot), take one or two shots (rift_snipe), leave off its own side, wait out of sight (AwaySeconds), then the next run enters from
// the OPPOSITE side. It never closes on the pilot: if the pilot comes within MinRange of it, it peels off instead. Every MineEvery-th
// run its first shot also leaves a rift mine on the rail line (RiftSnipeAttack).
public class SniperBrain : EliteBrain
{
    public enum Stage { Enter, Perch, Exit, Away }
    public const float Standoff = 4.6f;       // u above the pilot (never above the player's reach)
    public const float MinRange = 3.3f;       // u: closer to the pilot than this and it leaves
    public const float SettleDistance = .4f, PerchWait = .35f, AwaySeconds = 1.1f, EnterScale = 1.7f, ExitScale = 2f;
    public const int MineEvery = 3;
    Stage stage = Stage.Enter;
    float side = 1f, perched, away;
    int shotsLeft;
    bool mineTaken;
    public Stage Phase => stage;
    public float Side => side;
    public int Runs { get; private set; } = 1;
    public int ShotsLeft => shotsLeft;
    public int Shots { get; private set; }
    // The next shot is the first of a mine run.
    public bool MineDue => Runs % MineEvery == 0 && !mineTaken;
    public bool PeeledOff { get; private set; }

    public SniperBrain() { Id = "sniper"; }

    public override bool Roams => true;
    public override float TellScale => 1f;

    protected override void OnBind() { side = Random.value < .5f ? -1f : 1f; }
    public override void OnJoin() { side = Pos.x >= 0f ? 1f : -1f; BeginRun(false); }
    public override void OnReturn() { side = Pos.x >= 0f ? 1f : -1f; BeginRun(false); }

    void BeginRun(bool next)
    {
        if (next) Runs++;
        stage = Stage.Enter;
        shotsLeft = Random.Range(1, 3);   // 1 or 2
        mineTaken = false;
        perched = 0f;
        PeeledOff = false;
    }

    // Where it perches on its flank: the rail side, Standoff over the pilot (kept inside the player's reach).
    public Vector2 Station(Vector2 f)
    {
        float edge = EliteSystem.RailEdge - def.hullRadius - .45f;
        float y = Mathf.Min(f.y + Standoff, EliteSystem.ViewTop - def.hullRadius - 1f);   // (Station clamps it into the fighting zone)
        return new Vector2(side * edge, ship.ZoneY(ship.ReachY(y)));
    }

    public override Vector2 Goal(Vector2 follow, float dt)
    {
        Clock += dt;
        float off = SpaceFlight.OffX(def);
        Vector2 st = Station(follow);
        switch (stage)
        {
            case Stage.Enter:
                if ((Pos - st).sqrMagnitude < SettleDistance * SettleDistance) { stage = Stage.Perch; perched = 0f; }
                return st;
            case Stage.Perch:
                perched += dt;
                if ((Pos - ship.Seen).magnitude < MinRange) { PeeledOff = true; stage = Stage.Exit; }
                return st + new Vector2(0f, Mathf.Sin(Clock * 1.4f) * .15f);
            case Stage.Exit:
                if (Mathf.Abs(Pos.x) > off - .25f) { stage = Stage.Away; away = 0f; }
                return new Vector2(side * (off + 1f), Pos.y + .6f);
            default:
                away += dt;
                if (away >= AwaySeconds)
                {
                    // out of sight: back in from the opposite side
                    side = -side;
                    BeginRun(true);
                    ship.Warp(new Vector2(side * off, Station(follow).y + 1f));
                    stage = Stage.Enter;
                }
                return Pos;
        }
    }

    public override float SpeedScale => stage == Stage.Enter ? EnterScale : stage == Stage.Exit ? ExitScale : 1f;

    public override bool WantsAttack(Vector2 seen) =>
        stage == Stage.Perch && perched > PerchWait && shotsLeft > 0 && (seen - Pos).magnitude >= MinRange * 1.05f;

    public override float? FaceDeg(Vector2 seen) => stage == Stage.Perch ? FaceTowards(seen) : (float?)null;

    // The attack's callbacks.
    public void MineTaken() { mineTaken = true; }

    public void ShotDone()
    {
        Shots++;
        shotsLeft--;
        perched = 0f;
        if (shotsLeft <= 0) stage = Stage.Exit;
    }

    public override Vector2 JoinFrom => new Vector2(side, 1f);
}

// Rift Lancer: a rift snipe. The rift_rail of old -- planted, nose on the pilot, a blinking sight line from the prong tracks the
// pilot for the first TrackShare of the wind-up, then locks and blinks faster; shotCount fast bolts follow straight down the locked
// line, the first kicking it back -- and, on a mine run, a RIFT MINE: its spot (the point the sight locks on) is ringed from the
// first frame of the tell, and when the rail goes the prong sends a small pink rift gliding out along the line to it (lobSeconds),
// where it rides the board on a fuse ring (hazardSeconds more) and pops into a short cross of hazardCount bolts (hazardSpeed u/s,
// ArmSeconds of life). One player shot defuses it.
public class RiftSnipeAttack : EliteAttack
{
    public const float TrackShare = .6f, SightLength = 11f, SightWidth = .09f, Recoil = 1.8f, MineRing = .5f, ArmSeconds = .9f;
    float next;
    int fired;
    bool mineRun;
    Vector2 mineAt;
    EliteShot mine;
    EliteMarks marks;
    public RiftSnipeAttack() { Id = "rift_snipe"; }
    protected override void OnBind() { marks = new EliteMarks(ship, 1); }
    public override bool HoldsDuringTell => true;
    public override bool Aimed => true;
    public override float? FaceDeg => Deg(dir);
    public bool Locked => t >= TellSeconds * Mathf.Min(TrackShare, def.aimCommit);
    public bool MineRun => mineRun;
    public Vector2 MineSpot => mineAt;
    public EliteShot Mine => mine;
    public EliteMarks Marks => marks;
    public int Rail => fired;

    public override bool FriendlyInLine(Vector2 seen)
    {
        Vector2 to = seen - ship.Position;
        float dist = to.magnitude;
        return dist > 1e-3f && EliteOnLine(ship.Position, to / dist, SightLength, def.shotSize * .5f);
    }

    public override void BeginTell(Vector2 seen)
    {
        base.BeginTell(seen);
        fired = 0;
        mine = null;
        var sn = ship.Brain as SniperBrain;
        mineRun = sn != null && sn.MineDue;
        mineAt = ClampSpot(seen);
        marks.HideAll();
    }

    Vector2 ClampSpot(Vector2 p)
    {
        float edge = EliteSystem.RailEdge - .35f;
        p.x = Mathf.Clamp(p.x, -edge, edge);
        p.y = Mathf.Clamp(p.y, EliteSystem.ViewBottom + .8f, Mathf.Max(EliteSystem.ViewBottom + .8f, ship.Position.y - 1f));
        return p;
    }

    public override void Cancel()
    {
        ship.ShowSight(Vector2.zero, 0f, 0f, false);
        marks.HideAll();
    }

    public override void StepTell(float dt)
    {
        t += dt;
        if (!Locked)
        {
            Vector2 d = ship.Seen - ship.Position;
            if (d.sqrMagnitude > 1e-4f) { dir = d.normalized; aim = ship.Seen; }
            mineAt = ClampSpot(aim);
        }
        else mineAt.y -= EliteSystem.Scroll * dt;   // (the spot is on the board: it rides it)
        float period = Locked ? 2f : 4f;
        bool on = Mathf.FloorToInt(t / (period * EliteArt.Tick)) % 2 == 0;
        ship.ShowSight(ship.MuzzleWorld(0), Deg(dir), SightLength, on, SightWidth);
        if (mineRun) marks.Set(0, mineAt, MineRing, def.ShotColor, Locked);
    }

    public override void BeginAction()
    {
        base.BeginAction();
        next = 0f;
        fired = 0;
        ship.ShowSight(Vector2.zero, 0f, 0f, false);
        marks.HideAll();
        if (!mineRun) return;
        var m = Fire(0, Deg(mineAt - ship.MuzzleWorld(0)), 0f, EliteShots.Kind.Orb);
        if (m == null) return;
        // a small pink rift sent along the line: glides out to the spot, rides the board, fuse ring, then a short cross
        m.Mine(mineAt, def.lobSeconds, def.lobSeconds + def.hazardSeconds, Mathf.Max(2, def.hazardCount), def.hazardSpeed, ArmSeconds, 0f, Vector2.zero, 1);
        mine = m;
        var sn = ship.Brain as SniperBrain;
        if (sn != null) sn.MineTaken();
    }

    public override bool StepAction(float dt)
    {
        t += dt;
        int n = Mathf.Max(1, def.shotCount);
        while (t >= next && fired < n)
        {
            Fire(0, Deg(dir), def.shotSpeed);
            if (fired == 0) ship.Drive(-dir * Recoil);
            fired++;
            next += def.shotInterval;
        }
        return t >= def.actionSeconds && fired >= n;
    }

    public override void Passive(float dt) { marks.Step(dt); }

    public override void End()
    {
        ship.ShowSight(Vector2.zero, 0f, 0f, false);
        marks.HideAll();
        var sn = ship.Brain as SniperBrain;
        if (fired > 0 && sn != null) sn.ShotDone();
        fired = 0;
    }

    // Its rift mine goes with it (defused, no cross).
    public override void OnDeath()
    {
        var m = mine;
        mine = null;
        if (m != null && m.Active && m.Owner == ship && m.IsMine) { EliteSystem.Fx.Sparks(m.transform.position, def.ShotColor, 6); m.Recycle(); }
    }
}

// ---- Orbit Reaver: swooper -----------------------------------------------------------------------------------------

// A swooper. Flies big loops: a PASS is a straight strafing run across the board on a line Offset to one side of the pilot (so it is
// never nearer than that), entered from beyond a side of the view and left beyond the other; then it loops round out of sight and
// comes back for the next pass from the side it left by, on a new slope. It never parks. Lost a heart (and hidden, and back): its
// passes and loops are LostBoost faster for every heart gone. It fires one volley a pass (SwoopVolleyAttack), in the window
// FireFrom..FireTo along the run.
public class SwooperBrain : EliteBrain
{
    public enum Stage { Loop, Pass }
    public const float Offset = 3f;           // u: how far to the side of the pilot the pass runs
    public const float Lead = 2.4f;           // u: the point it chases ahead of itself on the line
    public const float Boost = 1.5f, LoopScale = 1.3f, LostBoost = .3f;
    public const float FireFrom = -4f, FireTo = -2.2f;   // along the run (0 = abreast of the pilot) in which it may start its wind-up
    public const float MinSlope = .2f, MaxSlope = .9f;
    Stage stage = Stage.Loop;
    float s, span0, span1, entrySide = 1f;
    Vector2 u = Vector2.right, n = Vector2.up;
    bool volleyed;
    public Stage Phase => stage;
    public int Passes { get; private set; }
    public Vector2 Dir => u;
    public Vector2 Normal => n;
    public float EntrySide => entrySide;
    public float EntrySpan => span0;
    public float ExitSpan => span1;
    public bool Volleyed => volleyed;

    public SwooperBrain() { Id = "swooper"; }

    public override bool Roams => true;
    public override float TellScale => 1f;
    public override void OnJoin() { entrySide = Pos.x >= 0f ? 1f : -1f; stage = Stage.Loop; Plan(); }
    public override void OnReturn() { entrySide = Pos.x >= 0f ? 1f : -1f; stage = Stage.Loop; Plan(); }

    float Speedup => 1f + LostBoost * Mathf.Max(0, def.hearts - (ship != null ? ship.Hearts : def.hearts));

    // The next pass: in from the side opposite the last one's, running down across the board on a new slope.
    void Plan()
    {
        Passes++;
        entrySide = -entrySide;
        float slope = Random.Range(MinSlope, MaxSlope);
        u = new Vector2(-entrySide, -slope).normalized;
        n = new Vector2(-u.y, u.x);
        if (n.y < 0f) n = -n;
        // both ends of the run wholly out of the view (where the line crosses x = +-OffX, and a little more)
        float c = OnLine(ship != null ? ship.Follow : Pos, 0f).x;
        float ux = Mathf.Max(.45f, Mathf.Abs(u.x)), off = SpaceFlight.OffX(def);
        span0 = Mathf.Max(3f, (off - entrySide * c) / ux) + 1.2f;
        span1 = Mathf.Max(3f, (off + entrySide * c) / ux) + 1.2f + Lead;
        s = -span0;
        volleyed = false;
    }

    // The point on the line `along` from abreast of the pilot (an anchor kept between the rails).
    public Vector2 OnLine(Vector2 f, float along)
    {
        float ax = Mathf.Clamp(f.x, -EliteSystem.RailEdge + .6f, EliteSystem.RailEdge - .6f);
        return new Vector2(ax, f.y) + n * Offset + u * along;
    }

    // How far along the run the ship is now (0: abreast of the pilot).
    public float Along(Vector2 f) => Vector2.Dot(Pos - OnLine(f, 0f), u);

    public override Vector2 Goal(Vector2 follow, float dt)
    {
        Clock += dt;
        if (stage == Stage.Loop)
        {
            Vector2 entry = OnLine(follow, -span0);
            if ((Pos - entry).sqrMagnitude < 1.5f * 1.5f) { stage = Stage.Pass; s = -span0; }
            return entry;
        }
        s += def.speed * SpeedScale * dt;
        if (s >= span1) { stage = Stage.Loop; Plan(); return OnLine(follow, -span0); }
        return OnLine(follow, s + Lead);
    }

    public override float SpeedScale => (stage == Stage.Pass ? Boost : LoopScale) * Speedup;

    public override bool WantsAttack(Vector2 seen)
    {
        if (stage != Stage.Pass || volleyed) return false;
        float a = Along(ship.Follow);
        return a >= FireFrom && a <= FireTo;
    }

    public void VolleyDone() { volleyed = true; }

    public override Vector2 JoinFrom => new Vector2(-1f, .6f);
}

// Orbit Reaver: a swoop volley. The tell keeps it racing along its pass (it never holds) while a marker ring tracks the pilot's spot
// for the first aimCommit of it, then LOCKS there and blinks faster; the action fires shotCount shards, the claws taking turns,
// shotInterval apart, each at that locked spot -- from wherever the pass has carried it, so they cross there from the pass angle.
// The ring stays until the last shard has had time to land. Keep moving: the shards cross on empty space.
public class SwoopVolleyAttack : EliteAttack
{
    public const float LockRing = .5f, LingerSeconds = .5f;
    float next, linger;
    int fired;
    Vector2 lockAt;
    EliteMarks marks;
    public SwoopVolleyAttack() { Id = "swoop_volley"; }
    protected override void OnBind() { marks = new EliteMarks(ship, 1); }
    public override bool Aimed => true;
    public Vector2 LockedAt => lockAt;
    public int Volley => fired;
    public EliteMarks Marks => marks;

    public override bool FriendlyInLine(Vector2 seen)
    {
        Vector2 to = seen - ship.Position;
        float dist = to.magnitude;
        return dist > 1e-3f && EliteOnLine(ship.Position, to / dist, dist + 1f, .3f);
    }

    public override void BeginTell(Vector2 seen)
    {
        base.BeginTell(seen);
        lockAt = seen;
        fired = 0;
        linger = 0f;
    }

    public override void StepTell(float dt)
    {
        t += dt;
        lockAt = ship.AimSeen;   // (live until the commit, then where the pilot WAS)
        marks.Set(0, lockAt, LockRing, def.ShotColor, ship.AimLocked);
    }

    public override void Cancel() { marks.HideAll(); }

    public override void BeginAction()
    {
        base.BeginAction();
        next = 0f;
        fired = 0;
        linger = 0f;
        lockAt = ship.AimSeen;
    }

    public override bool StepAction(float dt)
    {
        t += dt;
        int n = Mathf.Max(1, def.shotCount);
        while (t >= next && fired < n)
        {
            int m = def.muzzles.Length > 0 ? fired % def.muzzles.Length : 0;
            Fire(m, Deg(lockAt - ship.MuzzleWorld(m)), def.shotSpeed);
            fired++;
            next += def.shotInterval;
        }
        marks.Set(0, lockAt, LockRing, def.ShotColor, true);
        if (fired >= n) linger += dt;
        return t >= def.actionSeconds && fired >= n && linger >= LingerSeconds;
    }

    public override void Passive(float dt) { marks.Step(dt); }

    public override void End()
    {
        marks.HideAll();
        var sw = ship.Brain as SwooperBrain;
        if (fired > 0 && sw != null) sw.VolleyDone();
        fired = 0;
    }
}

// ---- Eventide Bastion: fortress ------------------------------------------------------------------------------------

// A fortress. Holds near the top of the view (topMargin under it, never above the player's reach) and slides sideways at a crawl, a
// slow sine laneOffset either side of the middle: it never comes down to the pilot. It attacks whenever it is over it.
public class FortressBrain : EliteBrain
{
    public const float SlideRate = .55f;   // rad/s
    float phase;

    public FortressBrain() { Id = "fortress"; }

    protected override void OnBind() { phase = Random.value * Mathf.PI * 2f; }

    public float Amplitude => Mathf.Max(0f, Mathf.Min(def.laneOffset, EliteSystem.RailEdge - def.hullRadius - .6f));
    public float Height => ship.ZoneY(ship.ReachY(EliteSystem.ViewTop - def.topMargin));

    public override Vector2 Goal(Vector2 follow, float dt)
    {
        Clock += dt;
        return new Vector2(Mathf.Sin(Clock * SlideRate + phase) * Amplitude, Height + Mathf.Sin(Clock * 1.1f) * .1f);
    }

    public override float SpeedScale => .7f;
    public override float MinAttackAbove => 1.5f;
    public override bool WantsAttack(Vector2 seen) => Pos.y > seen.y + MinAttackAbove;
    public override Vector2 JoinFrom => Vector2.up;
}

// Eventide Bastion: ward barrage. Casts alternate: a CURTAIN, then a MINE DROP.
//
// CURTAIN (the ward curtain, escalating): from the first frame of the tell a ring marks each spot of a row across the pilot's height
// (centred on it), live until the commit, then locked and blinking faster; the one spot beside the pilot toward the middle is left
// out -- the gap. Then shotCount slow bolts out of the pods, shotInterval apart, each at its spot. THE FIGHT ESCALATES with the
// hearts it has lost: the gap narrows by GapStep a heart (the two bolts beside it close in, down to MinGap -- still a ship's width
// and then some) and at <= DoubleRowAt hearts a SECOND ROW follows RowDelay after the first, its gap on the OTHER side of the
// pilot (the first row's gap now takes bolts): its spots ring up in the delay, and the delay is longer than a ship needs to cross
// between the gaps (AssumedPilotSpeed, ReactionSeconds). Without room beyond the pilot (a rail) the second gap stays where it was.
//
// MINE DROP (a shorter tell): one shield mine (two once it is down to TwoMinesAt hearts) is ringed ahead of the pilot, then glides
// out of a pod to the spot and drifts down with the board, slowly sideways toward the middle. A mine rides a fuse ring
// (MineGlide + hazardSeconds) and pops into a ring of hazardCount short bolts; it takes hazardArmour player shots to defuse.
public class WardBarrageAttack : EliteAttack
{
    public const float RowDelay = 1f, AssumedPilotSpeed = 4f, ReactionSeconds = .35f, GapStep = .1f, MinGap = 1.1f;
    public const float SpotRing = .34f, MineTell = .8f, MineGlide = .9f, MineRing = .55f, ArmSeconds = 1.1f, MineDrift = .25f;
    public const int DoubleRowAt = 2, TwoMinesAt = 3;
    readonly Vector2[] spot = new Vector2[2];
    readonly EliteShot[] live = new EliteShot[4];
    EliteMarks marks;
    int casts, slot, gap, gap2, dropped, mineCount;
    float next;
    bool mines, second;
    Vector2 row;

    public WardBarrageAttack() { Id = "ward_barrage"; }
    protected override void OnBind() { marks = new EliteMarks(ship, 8); }
    public override bool HoldsDuringTell => true;
    public override bool Aimed => true;
    public override float TellSeconds => mines ? Mathf.Max(MineTell, def.tellSeconds * .85f) : BaseTell;

    public bool MineDrop => mines;
    public int Count => Mathf.Max(3, def.shotCount);
    public int Centre => Count / 2;
    public int Gap => gap;
    public int Gap2 => gap2;
    public Vector2 Row => row;
    public int HeartsLost => Mathf.Max(0, def.hearts - ship.Hearts);
    public bool DoubleRow => ship.Hearts <= DoubleRowAt;
    public bool SecondRow => second;
    public int Mines => mineCount;
    public EliteMarks Marks => marks;
    public EliteShot Mine(int i) => i >= 0 && i < live.Length ? live[i] : null;

    // The width of the gap (between the two bolts beside it) for a row spacing and hearts lost.
    public static float GapWidthFor(float spacing, int lost) => Mathf.Max(MinGap, 2f * spacing - lost * GapStep);
    public float GapWidth => GapWidthFor(def.lobSpacing, HeartsLost);

    // The seconds a ship has to cross from one gap to the other between the rows, and the least it needs.
    public static float CrossNeeded(float spacing) => 2f * spacing / AssumedPilotSpeed + ReactionSeconds;

    // Spot i of the row with its gap at `gapSlot`: the two beside the gap pulled in to the gap's width.
    public Vector2 Spot(int i, int gapSlot)
    {
        float x = row.x + (i - (Count - 1) * .5f) * def.lobSpacing;
        float gx = row.x + (gapSlot - (Count - 1) * .5f) * def.lobSpacing;
        float w = GapWidth * .5f;
        if (i == gapSlot - 1) x = gx - w;
        else if (i == gapSlot + 1) x = gx + w;
        return new Vector2(x, row.y);
    }

    // The row and its gaps for a pilot seen at `p`.
    void PlanRow(Vector2 p)
    {
        row = p + new Vector2(((Count - 1) * .5f - Centre) * def.lobSpacing, 0f);
        int toward = p.x > 0f ? -1 : 1;
        gap = Centre + toward;
        gap2 = Centre - toward;
        // no room beyond the pilot: the second gap stays put
        if (Mathf.Abs(row.x + (gap2 - (Count - 1) * .5f) * def.lobSpacing) > EliteSystem.RailEdge - .3f) gap2 = gap;
    }

    // Where the mines of this drop go: ahead of the pilot, the first toward the middle.
    void PlanMines(Vector2 p)
    {
        mineCount = ship.Hearts <= TwoMinesAt ? 2 : 1;
        float edge = EliteSystem.RailEdge - .5f;
        float toward = p.x > 0f ? -1f : 1f;
        float y = Mathf.Min(p.y + def.lobAhead, Mathf.Max(EliteSystem.ViewBottom + 1f, ship.Position.y - 1.2f));
        for (int k = 0; k < mineCount; k++)
            spot[k] = new Vector2(Mathf.Clamp(p.x + (k == 0 ? toward : -toward) * (mineCount == 1 ? .8f : 1f), -edge, edge), y);
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
        mines = casts++ % 2 == 1;
        second = false;
        marks.HideAll();
        if (mines) PlanMines(seen); else PlanRow(seen);
        ShowMarks();
    }

    void ShowMarks()
    {
        bool fast = ship.AimLocked;
        if (mines)
        {
            for (int k = 0; k < mineCount; k++) marks.Set(k, spot[k], MineRing, def.ShotColor, fast);
            return;
        }
        for (int i = 0; i < Count; i++)
        {
            if (i == gap) { if (DoubleRow) marks.Set(i, Spot(i, gap), SpotRing, def.ShotColor, false, .35f); else marks.Hide(i); continue; }
            marks.Set(i, Spot(i, gap), SpotRing, def.ShotColor, fast);
        }
    }

    public override void StepTell(float dt)
    {
        t += dt;
        if (!ship.AimLocked)
        {
            if (mines) PlanMines(ship.AimSeen); else PlanRow(ship.AimSeen);
        }
        else if (mines)
        {
            for (int k = 0; k < mineCount; k++) spot[k].y -= EliteSystem.Scroll * dt;   // (a mine's spot is on the board)
        }
        ShowMarks();
    }

    public override void Cancel() { marks.HideAll(); }

    public override void BeginAction()
    {
        base.BeginAction();
        next = 0f;
        slot = 0;
        dropped = 0;
        second = false;
        for (int i = 0; i < live.Length; i++) live[i] = null;
    }

    public override bool StepAction(float dt)
    {
        t += dt;
        if (mines) return StepMines(dt);
        int n = Count;
        int g = second ? gap2 : gap;
        while (t >= next && slot < n)
        {
            if (slot != g)
            {
                int m = def.muzzles.Length > 0 ? slot % def.muzzles.Length : 0;
                Fire(m, Deg(Spot(slot, g) - ship.MuzzleWorld(m)), def.shotSpeed);
            }
            slot++;
            next += def.shotInterval;
        }
        if (slot >= n && !second && DoubleRow)
        {
            // the second row: its spots ring up now, and it follows RowDelay after the first
            second = true;
            slot = 0;
            next = RowDelay;
            marks.HideAll();
            for (int i = 0; i < n; i++) if (i != gap2) marks.Set(i, Spot(i, gap2), SpotRing, def.ShotColor, true);
            return false;
        }
        if (slot >= n && (!DoubleRow || second))
        {
            marks.HideAll();
            return t >= def.actionSeconds;
        }
        return false;
    }

    bool StepMines(float dt)
    {
        while (t >= next && dropped < mineCount)
        {
            int m = def.muzzles.Length > 0 ? dropped % def.muzzles.Length : 0;
            Vector2 to = spot[dropped];
            var orb = Fire(m, Deg(to - ship.MuzzleWorld(m)), 0f, EliteShots.Kind.Orb);
            if (orb != null)
            {
                float toward = to.x > 0f ? -1f : 1f;
                orb.Mine(to, MineGlide, MineGlide + def.hazardSeconds, Mathf.Max(3, def.hazardCount), def.hazardSpeed, ArmSeconds, 0f, new Vector2(toward * MineDrift, 0f), def.hazardArmour);
                live[dropped % live.Length] = orb;
            }
            marks.Hide(dropped);
            dropped++;
            next += def.shotInterval;
        }
        for (int k = dropped; k < mineCount; k++)
        {
            spot[k].y -= EliteSystem.Scroll * dt;
            marks.Set(k, spot[k], MineRing, def.ShotColor, true);
        }
        return t >= def.actionSeconds && dropped >= mineCount;
    }

    public override void Passive(float dt) { marks.Step(dt); }
    public override void End() { marks.HideAll(); }

    // Its mines are defused with it.
    public override void OnDeath()
    {
        marks.HideAll();
        for (int i = 0; i < live.Length; i++)
        {
            var m = live[i];
            live[i] = null;
            if (m != null && m.Active && m.Owner == ship && m.IsMine) { EliteSystem.Fx.Sparks(m.transform.position, def.ShotColor, 6); m.Recycle(); }
        }
    }
}

// ---- Singularity Hauler: wrecker ------------------------------------------------------------------------------------

// A wrecker. Parks high in the board (topMargin under the top, never above the player's reach), drifting a crawl either side of the
// middle; it never comes down to the pilot. It attacks from where it is.
public class WreckerBrain : EliteBrain
{
    public const float SlideRate = .4f;
    float phase;

    public WreckerBrain() { Id = "wrecker"; }

    protected override void OnBind() { phase = Random.value * Mathf.PI * 2f; }

    public float Amplitude => Mathf.Max(0f, Mathf.Min(def.laneOffset, EliteSystem.RailEdge - def.hullRadius - .6f));
    public float Height => ship.ZoneY(ship.ReachY(EliteSystem.ViewTop - def.topMargin));

    public override Vector2 Goal(Vector2 follow, float dt)
    {
        Clock += dt;
        return new Vector2(Mathf.Sin(Clock * SlideRate + phase) * Amplitude, Height + Mathf.Sin(Clock * .9f) * .1f);
    }

    public override float SpeedScale => .75f;
    public override float MinAttackAbove => 1.5f;
    public override bool WantsAttack(Vector2 seen) => Pos.y > seen.y + MinAttackAbove;
    public override Vector2 JoinFrom => Vector2.up;
}

// Singularity Hauler: tow and sling. Casts alternate: a TOW, then a SLING.
//
// TOW: the tell plants it and a blinking sight line drops from the top of the view to its tow claws -- the way the asteroid comes.
// The action tows a big Space rock (hazardSize x the roster rock) down from above the view into the claws (hazardSeconds, easing
// in), then LOCKS a lane from the claws to where the pilot is (LaneSeconds, the lane drawn as the dotted lane telegraph) and
// flings it down it at hazardSpeed. The rock is a real hazard the whole time: it hurts on touch, the pilot's shots break it up (held
// in the claws or flung), a shielded ram smashes it, and flung it glances off the rails (ThrownRock). Dropped on a hit hauler
// that dies, it is blown up unpaid.
//
// SLING: the gravity sling of old with its well marked from the tell on: the ring on the spot ahead of the pilot (lobAhead, led by
// the scroll) where shotCount shots, thrown out of both claws, curve in (lobSeconds) and cross. The well holds WellSeconds and
// tugs the pilot gently while it does (EliteWell).
public class TowSlingAttack : EliteAttack
{
    public enum Phase { Tow, Lane, Settle }
    public const string RockKey = "space_rock_cluster";
    public const float LaneSeconds = .9f, SettleSeconds = .35f, WellSeconds = 2f, WellRing = .45f, Recoil = 1.2f, SightWidth = .09f, Bulge = 1.1f;
    readonly EliteTelegraph lane = new EliteTelegraph();
    EliteMarks marks;
    int casts, fired;
    bool sling, laneShown;
    Phase phase;
    float phaseT, next, wellLeft;
    Vector2 well, towFrom, flingDir;
    GameObject rock;
    ThrownRock thrown;

    public TowSlingAttack() { Id = "tow_sling"; }
    protected override void OnBind() { marks = new EliteMarks(ship, 2); }
    public override bool HoldsDuringTell => true;
    public override bool HoldsDuringAction => true;
    public override bool Aimed => true;
    // (the tow throws a rock, not shots; the sling's are checked in SpaceEliteTest)
    public override bool Shoots => false;

    public bool SlingCast => sling;
    public Phase TowPhase => phase;
    public GameObject Rock => rock;
    public ThrownRock Thrown => thrown;
    public Vector2 Well => well;
    public Vector2 FlingDir => flingDir;
    public EliteTelegraph Lane => lane;
    public EliteMarks Marks => marks;
    public int Slung => fired;

    // The point between the tow claws, where the asteroid is held.
    public Vector2 ClawPoint
    {
        get
        {
            if (def.muzzles.Length < 2) return ship.MuzzleWorld(0);
            return (ship.MuzzleWorld(0) + ship.MuzzleWorld(1)) * .5f;
        }
    }

    public override bool FriendlyInLine(Vector2 seen)
    {
        Vector2 to = (sling ? seen + Vector2.up * def.lobAhead : seen) - ship.Position;
        float dist = to.magnitude;
        return dist > 1e-3f && EliteOnLine(ship.Position, to / dist, dist, .6f);
    }

    Vector2 WellFor(Vector2 p)
    {
        Vector2 w = p + Vector2.up * (def.lobAhead + EliteSystem.Scroll * def.lobSeconds);
        float edge = Mathf.Max(0f, EliteSystem.RailEdge - def.shotSize - .3f);
        w.x = Mathf.Clamp(w.x, -edge, edge);
        return w;
    }

    public override void BeginTell(Vector2 seen)
    {
        base.BeginTell(seen);
        sling = casts++ % 2 == 1;
        marks.HideAll();
        laneShown = false;
        if (sling) well = WellFor(seen);
    }

    public override void StepTell(float dt)
    {
        t += dt;
        bool on = Mathf.FloorToInt(t / ((t > TellSeconds * .7f ? 2f : 4f) * EliteArt.Tick)) % 2 == 0;
        if (sling)
        {
            if (!ship.AimLocked) well = WellFor(ship.AimSeen);
            marks.Set(0, well, WellRing, def.ShotColor, ship.AimLocked);
            marks.Set(1, well, WellRing * 1.9f, def.ShotColor, ship.AimLocked, .5f);
            return;
        }
        // the way the asteroid comes: a sight line from the top of the view down to the claws
        Vector2 c = ClawPoint;
        float top = EliteSystem.ViewTop + 1.5f;
        ship.ShowSight(new Vector2(c.x, top), -90f, Mathf.Max(.5f, top - c.y), on, SightWidth);
        marks.Set(0, c, WellRing * 1.1f, def.ShotColor, t > TellSeconds * .7f);
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
        phaseT = 0f;
        next = 0f;
        fired = 0;
        if (sling)
        {
            wellLeft = WellSeconds;
            EliteWell.Open(well, WellRing, WellSeconds);
            return;
        }
        phase = Phase.Tow;
        Vector2 c = ClawPoint;
        towFrom = new Vector2(c.x, EliteSystem.ViewTop + 1.8f);
        rock = SpawnRock(towFrom);
        thrown = rock != null ? rock.GetComponent<ThrownRock>() : null;
    }

    GameObject SpawnRock(Vector2 at)
    {
        var d = EnemyRoster.Find(RockKey);
        if (d == null) return null;
        var go = EnemyFactory.Create(d, new Vector3(at.x, at.y, 0f), Quaternion.identity, Mathf.Max(.5f, def.hazardSize));
        if (go == null) return null;
        go.transform.SetParent(EliteSystem.Root, true);   // (gone with the elites on a clear)
        // flown by the hauler: its own scroll mover and brain off
        var mover = go.GetComponent<moveEnimes>();
        if (mover != null) mover.enabled = false;
        var brain = go.GetComponent<EnemyBrain>();
        if (brain != null) brain.enabled = false;
        var ct = ClearTarget.Ensure(go);
        if (ct != null) ct.Mother = ship;
        var tr = ThrownRock.Attach(go, ship);
        SpawnFootprint.Bind(go, tr);
        return go;
    }

    public override bool StepAction(float dt)
    {
        t += dt;
        return sling ? StepSling(dt) : StepTow(dt);
    }

    bool StepTow(float dt)
    {
        phaseT += dt;
        Vector2 claw = ClawPoint;
        float held = rock != null ? ClawHold : 0f;
        switch (phase)
        {
            case Phase.Tow:
            {
                float seconds = Mathf.Max(.3f, def.hazardSeconds);
                float k = Mathf.Clamp01(phaseT / seconds), e = 1f - (1f - k) * (1f - k);
                if (rock != null) rock.transform.position = (Vector3)Vector2.Lerp(towFrom, claw + Vector2.up * held, e);
                if (k >= 1f)
                {
                    phase = Phase.Lane;
                    phaseT = 0f;
                    // the lane: locked now, from the claws down to where the pilot is
                    Vector2 target = new Vector2(Mathf.Clamp(ship.Seen.x, -EliteSystem.RailEdge + .4f, EliteSystem.RailEdge - .4f), ship.Seen.y);
                    flingDir = (target - claw).normalized;
                    if (flingDir.y > -.3f) flingDir = new Vector2(Mathf.Sign(flingDir.x) * .8f, -.6f).normalized;
                    ShowLane(claw);
                }
                break;
            }
            case Phase.Lane:
                if (rock != null) rock.transform.position = (Vector3)(claw + Vector2.up * held + Quiver(phaseT));
                lane.Step(dt);
                if (phaseT >= LaneSeconds)
                {
                    phase = Phase.Settle;
                    phaseT = 0f;
                    lane.GoLive();
                    laneShown = false;
                    if (thrown != null && rock != null)
                    {
                        thrown.Fling(flingDir * Mathf.Max(1f, def.hazardSpeed));
                        Fired++;
                        ship.OnFired(0, Deg(flingDir));
                    }
                    ship.Drive(-flingDir * Recoil);
                }
                break;
            default:
                return phaseT >= SettleSeconds;
        }
        return false;
    }

    // A held rock sits just above the claws (half its height).
    float ClawHold => .3f + def.hazardSize * .2f;

    // The strain of the tow: a stepped little shake (never a smooth wobble).
    static Vector2 Quiver(float clock) => new Vector2(Mathf.FloorToInt(clock * 20f) % 2 == 0 ? .03f : -.03f, 0f);

    void ShowLane(Vector2 claw)
    {
        float half = (rock != null ? RockRadius : .4f) + .1f;
        float len = (claw.y - (EliteSystem.ViewBottom - 1f)) / Mathf.Max(.2f, -flingDir.y);
        lane.Clear();
        lane.Lane(claw, claw + flingDir * len, half);
        lane.Show(LaneSeconds, def.ShotColor);
        laneShown = true;
    }

    public float RockRadius
    {
        get
        {
            var ct = rock != null ? rock.GetComponent<ClearTarget>() : null;
            return ct != null ? ct.Radius : .4f;
        }
    }

    bool StepSling(float dt)
    {
        int n = Mathf.Max(1, def.shotCount);
        while (t >= next && fired < n)
        {
            int m = def.muzzles.Length > 0 ? fired % def.muzzles.Length : 0;
            Vector2 from = ship.MuzzleWorld(m);
            float side = from.x >= ship.Position.x ? 1f : -1f;
            float edge = EliteSystem.RailEdge - def.shotSize - .25f;
            Vector2 bend = new Vector2(Mathf.Clamp(from.x + side * Bulge, -edge, edge), Mathf.Lerp(from.y, well.y, .3f));
            var shot = Fire(m, Deg(bend - from), def.shotSpeed);
            if (shot != null) shot.Sling(bend, well, def.lobSeconds);
            fired++;
            next += def.shotInterval;
        }
        return t >= def.actionSeconds && fired >= n;
    }

    // The well rides the board for as long as it is up (and keeps pulling on its own clock, EliteWell).
    public override void Passive(float dt)
    {
        marks.Step(dt);
        if (wellLeft > 0f)
        {
            wellLeft -= dt;
            well.y -= EliteSystem.Scroll * dt;
            EliteWell.MoveTo(well);
            marks.Set(0, well, WellRing, def.ShotColor, true);
            marks.Set(1, well, WellRing * (1.5f + .4f * Mathf.Abs(Mathf.Sin(wellLeft * 6f))), def.ShotColor, true, .5f);
            if (wellLeft <= 0f) marks.HideAll();
        }
    }

    public override void End()
    {
        ship.ShowSight(Vector2.zero, 0f, 0f, false);
        lane.End();
        laneShown = false;
        if (!sling) marks.HideAll();
        // let a rock still in the claws go (an attack broken off): it drops away
        if (rock != null && thrown != null && !thrown.Flung) thrown.Fling(Vector2.down * 3f);
        rock = null;
        thrown = null;
    }

    public override void OnDeath()
    {
        ship.ShowSight(Vector2.zero, 0f, 0f, false);
        lane.End();
        marks.HideAll();
        EliteWell.Close();
        wellLeft = 0f;
        if (rock != null && thrown != null && !thrown.Flung) ThrownRock.Scuttle(rock);
        rock = null;
        thrown = null;
    }
}
