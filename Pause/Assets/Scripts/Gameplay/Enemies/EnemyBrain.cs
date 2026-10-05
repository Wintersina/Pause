using UnityEngine;

// Runs one roster enemy's EnemyBehaviour (EnemyBehaviours has the table,
// docs/enemy-behaviours.md the design).
//
// The enemy's mover (moveEnimes, moveItemEnmInStrightLine, RailMineMount)
// stays the scroll authority; the brain adds a bounded OFFSET in board space
// on top of it each running frame -- sideways and along the board, or along
// the rail for a mine -- and runs the attack state machine:
//
//   Idle -> Windup (the tell: cell 4 held, a charge light) -> Release (cell
//   5, the shot or the lunge) -> Recover -> Idle
//
// PAUSE. Step(dt) is the only thing that advances it, called from LateUpdate
// on running frames only (the same rule every mover uses), with the world's
// scaled dt. A frozen world steps nothing: no timer runs, nothing fires.
// A stunned enemy (its mover switched off: HazardMovers) stops with it.
//
// SPACE. The offset never leaves the behaviour's envelope (bandX, Up, Down)
// and the lane, and the movers widen their SpawnSpace sweep by that
// envelope (Widen), so patterns are kept apart by placement alone: no
// neighbour scans. Nothing here allocates per frame.
[DefaultExecutionOrder(-20)]   // LateUpdate before RailMineMount settles the mine
[DisallowMultipleComponent]
public class EnemyBrain : MonoBehaviour
{
    public enum Phase { Idle, Windup, Release, Recover }

    // ---- fairness, shared by every enemy (tunables) ----
    public const float TellFloorSeconds = .45f;   // no windup is ever shorter
    public const float ReleaseSeconds = .2f;      // tell cell 5 held
    public const float MinFireAbove = 1.6f;       // only starts a windup this far above the pilot ...
    public const float MinFireDistance = 1.8f;    // ... and this far from it
    public const float ViewInset = .35f;          // and this far inside the top of the view
    public const float MarchHop = .14f;           // seconds a March hop takes

    public EnemyDef Def { get; private set; }
    public EnemyBehaviour Behaviour { get; private set; }
    public Phase State { get; private set; }
    public Vector2 Offset => new Vector2(ox + lx, oy + ly);
    public bool Armed { get; private set; }
    public int ShotsFired { get; private set; }
    public int Volleys { get; private set; }
    public int Windups { get; private set; }
    // Seconds of windup that ran before the last release (tests).
    public float LastTellSeconds { get; private set; }
    public float StateTime => stateTime;
    public SpriteRenderer ChargeLight => charge;

    // Where the offset is measured from: the mover's own position.
    public Vector2 Base => new Vector2(transform.position.x - ox - lx, transform.position.y - oy - ly);

    // What it attacks (the ship; tests set a stand-in).
    public Transform TargetOverride;

    UnityEngine.Behaviour hostMover;   // moveEnimes or moveItemEnmInStrightLine: the scroll authority
    RailMineMount mount;
    EnemyFlipbook flipbook;
    SpriteRenderer charge;
    bool onRail;

    float ox, oy, lx, ly;           // pattern offset, lunge offset
    float lat, vert;                // pattern clocks
    float dir = 1f;
    float marchTarget, marchTimer;
    float seen;                     // seconds inside the view
    float stateTime, cooldown;
    float lungeFromX, lungeFromY, lungeToX, lungeToY;
    Vector2 aim = Vector2.down;
    Vector2 lobTarget;
    float halfX;
    int reserved;                   // shots held in EnemyThreat's budget during a windup

    public void Init(EnemyDef def, EnemyBehaviour behaviour)
    {
        Def = def;
        Behaviour = behaviour;
        if (behaviour == null) { enabled = false; return; }
        TryGetComponent(out flipbook);
        if (TryGetComponent(out mount)) mount.brain = this;
        // (TryGetComponent: a missed GetComponent is a fake null in the editor)
        if (TryGetComponent(out moveEnimes weaver)) hostMover = weaver;
        else if (TryGetComponent(out moveItemEnmInStrightLine scroller)) hostMover = scroller;
        else hostMover = null;
        onRail = def.role == EnemyRole.Mine;
        halfX = def.ColliderSize.x * .5f;
        Armed = behaviour.Attacks && (behaviour.armedChance >= 1f || Random.value < behaviour.armedChance);
        // neighbours out of step, except the invader lines: wiggling and
        // marching in lockstep is the point
        bool lockstep = def.role == EnemyRole.Alien;
        lat = lockstep ? 0f : Random.value * Mathf.Max(.1f, behaviour.lateralPeriod);
        vert = lockstep ? 0f : Random.value * Mathf.Max(.1f, behaviour.verticalPeriod);
        dir = lockstep ? 1f : (Random.value < .5f ? -1f : 1f);
        marchTimer = behaviour.lateralPeriod;
        cooldown = behaviour.firstDelay;
        State = Phase.Idle;
        if (flipbook != null && !onRail) flipbook.SetBrainDriven(Armed);
        if (behaviour.Shoots && Armed) BuildChargeLight();
    }

    void BuildChargeLight()
    {
        var go = new GameObject("Charge");
        go.transform.SetParent(transform, false);
        go.transform.localPosition = MuzzleLocal();
        charge = go.AddComponent<SpriteRenderer>();
        charge.sprite = EliteFxArt.Glow;
        charge.sortingOrder = 13;
        charge.color = EnemyBehaviours.ShotColor(Def.world);
        charge.enabled = false;
    }

    Vector3 MuzzleLocal()
    {
        if (Behaviour.attack == EnemyAttack.Cross)
            return new Vector3(transform.position.x > 0f ? -Behaviour.muzzle : Behaviour.muzzle, 0f, 0f);
        return new Vector3(0f, -Behaviour.muzzle, 0f);
    }

    Transform Target => TargetOverride != null ? TargetOverride : EliteSystem.Player;

    static bool Flying => !buttonClicks.playerDied && (TouchInput.IsPressed || score.pauseCounter <= 0);

    void LateUpdate()
    {
        if (!Flying) return;
        float dt = Time.deltaTime;
        if (dt <= 0f) return;
        Step(dt);
    }

    // One running frame (dt explicit: tests and simulations step it by hand).
    public void Step(float dt)
    {
        if (Behaviour == null || dt <= 0f) return;
        // stunned / gripped: whatever switched its mover off holds the brain too
        if (hostMover != null && !hostMover.enabled && !onRail) return;
        // the spawner clamps a mine to its rail after it is built
        if (onRail && mount == null && TryGetComponent(out mount)) mount.brain = this;

        float beforeX = ox + lx, beforeY = oy + ly;
        Vector3 p = transform.position;
        float baseX = p.x - beforeX;
        float top = CameraFit.ViewTop, bottom = CameraFit.ViewBottom;
        bool inView = p.y < top - ViewInset && p.y > bottom;
        if (inView) seen += dt;

        bool committed = State == Phase.Windup || State == Phase.Release;
        var t = Target;

        StepLateral(dt, baseX, t, committed);
        StepVertical(dt, inView, committed);
        StepAttack(dt, p, baseX, inView, t);

        // never outside the envelope, never into a rail
        float band = Behaviour.bandX;
        float total = Mathf.Clamp(ox + lx, -band, band);
        if (!onRail)
        {
            float lane = SpawnLane.LaneHalf - halfX;
            if (Mathf.Abs(baseX) <= lane) total = Mathf.Clamp(baseX + total, -lane, lane) - baseX;
        }
        lx = total - ox;
        float totalY = Mathf.Clamp(oy + ly, -Behaviour.Down, Behaviour.Up);
        ly = totalY - oy;

        if (onRail)
        {
            if (mount != null) mount.Slide = totalY;   // the mount places it (LateUpdate)
        }
        else
        {
            float dx = total - beforeX, dy = totalY - beforeY;
            if (dx != 0f || dy != 0f) transform.position = new Vector3(p.x + dx, p.y + dy, p.z);
        }
    }

    // ---- movement ----------------------------------------------------------

    void StepLateral(float dt, float baseX, Transform t, bool committed)
    {
        var b = Behaviour;
        float band = b.bandX;
        switch (b.lateral)
        {
            case EnemyLateral.Drift:
                if (committed && b.Shoots) break;
                ox += dir * b.lateralSpeed * dt;
                if (ox > band) { ox = band; dir = -1f; }
                else if (ox < -band) { ox = -band; dir = 1f; }
                break;
            case EnemyLateral.Glide:
                ox = Mathf.Clamp(ox + dir * b.lateralSpeed * dt, -band, band);
                break;
            case EnemyLateral.Sway:
                if (committed) break;
                lat += dt;
                ox = band * Mathf.Sin(lat / Mathf.Max(.1f, b.lateralPeriod) * 2f * Mathf.PI);
                break;
            case EnemyLateral.Orbit:
                lat += dt;
                float a = lat / Mathf.Max(.1f, b.lateralPeriod) * 2f * Mathf.PI * dir;
                ox = band * Mathf.Cos(a);
                oy = band * Mathf.Sin(a);
                break;
            case EnemyLateral.Track:
                if (committed || t == null) break;
                // (lx: what a lunge already moved it; the two add up to where it is)
                float want = Mathf.Clamp(t.position.x - baseX, -band, band) - lx;
                ox = Mathf.MoveTowards(ox, want, b.lateralSpeed * dt);
                break;
            case EnemyLateral.March:
                marchTimer -= dt;
                if (marchTimer <= 0f)
                {
                    marchTimer += Mathf.Max(.1f, b.lateralPeriod);
                    float hop = band * .5f;
                    if (Mathf.Abs(marchTarget + dir * hop) > band + 1e-4f) dir = -dir;
                    marchTarget = Mathf.Clamp(marchTarget + dir * hop, -band, band);
                }
                ox = Mathf.MoveTowards(ox, marchTarget, band * .5f / MarchHop * dt);
                break;
        }
    }

    void StepVertical(float dt, bool inView, bool committed)
    {
        var b = Behaviour;
        switch (b.vertical)
        {
            case EnemyVertical.Bob:
                vert += dt;
                oy = b.rise * Mathf.Sin(vert / Mathf.Max(.1f, b.verticalPeriod) * 2f * Mathf.PI);
                break;
            case EnemyVertical.Pulse:
            {
                // a kick up the board over the first quarter, a slow sink back
                vert += dt;
                float k = Mathf.Repeat(vert / Mathf.Max(.1f, b.verticalPeriod), 1f);
                float f = k < .25f ? Mathf.Sin(k / .25f * Mathf.PI * .5f) : .5f + .5f * Mathf.Cos((k - .25f) / .75f * Mathf.PI);
                oy = b.rise * f;
                break;
            }
            case EnemyVertical.Brake:
                // sheds part of the scroll once it is in view: a hover
                if (inView && oy < b.rise)
                    oy = Mathf.Min(b.rise, oy + SpawnSpace.ScrollSpeed * Mathf.Clamp01(b.verticalSpeed) * dt);
                break;
            case EnemyVertical.Sink:
            case EnemyVertical.Creep:
                if (inView && !committed) oy = Mathf.Max(-b.sink, oy - b.verticalSpeed * dt);
                break;
            case EnemyVertical.Patrol:
                if (committed) break;   // an armed mine holds still
                vert += dt;
                oy = b.rise * Mathf.Sin(vert / Mathf.Max(.1f, b.verticalPeriod) * 2f * Mathf.PI);
                break;
        }
    }

    // ---- attack ------------------------------------------------------------

    void StepAttack(float dt, Vector3 p, float baseX, bool inView, Transform t)
    {
        var b = Behaviour;
        if (!Armed) return;
        stateTime += dt;
        switch (State)
        {
            case Phase.Idle:
                cooldown -= dt;
                if (cooldown > 0f || Volleys >= b.maxVolleys) return;
                if (!MayAttack(p, inView, t)) return;
                if (b.Shoots)
                {
                    if (!EnemyThreat.TryReserveVolley(b.shotCount)) { cooldown = .25f; return; }
                    reserved = Mathf.Max(1, b.shotCount);
                }
                BeginWindup(p, t);
                break;
            case Phase.Windup:
                PulseCharge();
                if (stateTime < Mathf.Max(TellFloorSeconds, b.tell)) return;
                LastTellSeconds = stateTime;
                Release(p, baseX, t);
                break;
            case Phase.Release:
                if (b.attack == EnemyAttack.Lunge)
                {
                    float k = Mathf.Clamp01(stateTime / Mathf.Max(.05f, b.lungeSeconds));
                    float e = 1f - (1f - k) * (1f - k);   // out fast, settling
                    lx = Mathf.Lerp(lungeFromX, lungeToX, e);
                    ly = Mathf.Lerp(lungeFromY, lungeToY, e);
                    if (k < 1f) return;
                }
                else if (stateTime < ReleaseSeconds) return;
                Enter(Phase.Recover);
                if (flipbook != null) flipbook.Drive(EnemyFlipbook.DrivePhase.None);
                break;
            case Phase.Recover:
                cooldown = b.cooldown;
                Enter(Phase.Idle);
                break;
        }
    }

    // The shots it reserved in the budget when its windup began are either
    // in the air now or will never be fired.
    void ReleaseReservation()
    {
        if (reserved <= 0) return;
        EnemyThreat.Unreserve(reserved);
        reserved = 0;
    }

    void OnDisable() { ReleaseReservation(); }

    void Enter(Phase next)
    {
        State = next;
        stateTime = 0f;
    }

    // Fair to start a tell now? On screen, above the pilot and not on top of it.
    bool MayAttack(Vector3 p, bool inView, Transform t)
    {
        if (!inView || t == null || seen < Behaviour.firstDelay) return false;
        if (Behaviour.Shoots && !EnemyThreat.ShootingAllowed) return false;
        if (Behaviour.attack == EnemyAttack.Lunge && EliteInLungePath(p)) return false;
        Vector3 s = t.position;
        if (Behaviour.attack == EnemyAttack.Cross)
            return Mathf.Abs(p.y - s.y) < 6f && p.y > s.y - .5f;   // its row matters, not its height
        if (p.y - s.y < MinFireAbove) return false;
        return ((Vector2)(s - p)).sqrMagnitude >= MinFireDistance * MinFireDistance;
    }

    // A body dash never starts through a friendly elite: one in the band it
    // may cross, as far below as the dive reaches by the time it lands (the
    // elite holds its place while the board carries this enemy down).
    bool EliteInLungePath(Vector3 p)
    {
        var live = EliteShip.Live;
        if (live.Count == 0 || !EliteEvasion.Enabled) return false;
        var b = Behaviour;
        float seconds = Mathf.Max(TellFloorSeconds, b.tell) + b.lungeSeconds;
        float reachX = (b.lungeX > 0f ? b.bandX * 2f : 0f) + halfX;
        float reachY = b.lungeDive + SpawnSpace.ScrollSpeed * seconds + halfX;
        for (int i = 0; i < live.Count; i++)
        {
            var e = live[i];
            if (e == null || !e.InPlay) continue;
            Vector2 d = e.Position - (Vector2)p;
            float r = e.Def.hullRadius + .15f;
            if (Mathf.Abs(d.x) < reachX + r && d.y < r && d.y > -reachY - r) return true;
        }
        return false;
    }

    // For the elites' threat sensor: how fast its pattern can carry it
    // sideways (u/s) -- how far off a straight-line guess of its path may be.
    public float LateralPace
    {
        get
        {
            var b = Behaviour;
            if (b == null) return 0f;
            switch (b.lateral)
            {
                case EnemyLateral.Drift:
                case EnemyLateral.Glide:
                case EnemyLateral.Track: return b.lateralSpeed;
                case EnemyLateral.Sway:
                case EnemyLateral.Orbit: return b.bandX * 2f * Mathf.PI / Mathf.Max(.1f, b.lateralPeriod);
                case EnemyLateral.March: return b.bandX * .5f / Mathf.Max(.2f, b.lateralPeriod) + .3f;
                default: return 0f;
            }
        }
    }

    // For the elites' threat sensor: a body dash that is telegraphed (the
    // windup) or under way. `reach` is from where it is now to where the dash
    // ends, in board space; `inSeconds` how long until it goes.
    public bool LungeAhead(out Vector2 reach, out float inSeconds)
    {
        reach = Vector2.zero;
        inSeconds = 0f;
        var b = Behaviour;
        if (b == null || !Armed || b.attack != EnemyAttack.Lunge) return false;
        if (State == Phase.Release)
        {
            reach = new Vector2(lungeToX - lx, lungeToY - ly);
            return true;
        }
        if (State != Phase.Windup) return false;
        float baseX = transform.position.x - ox - lx;
        reach = new Vector2(LungeGoal(baseX, Target) - lx, -b.lungeDive - ly);
        inSeconds = Mathf.Max(0f, Mathf.Max(TellFloorSeconds, b.tell) - stateTime);
        return true;
    }

    // Where a lunge released now would take the lunge offset sideways.
    float LungeGoal(float baseX, Transform t)
    {
        var b = Behaviour;
        float band = b.bandX;
        float want = lx;
        if (b.lungeX > 0f && t != null)
        {
            float here = ox + lx;                                   // where it stands in its band
            float pilot = t.position.x - baseX;                     // where the pilot is, same frame
            float goal = b.lungeDive <= 0f
                ? (pilot >= here ? band : -band)                    // a slash: right across, the pilot's side
                : Mathf.Lerp(here, pilot, b.lungeX);                // a pounce: toward the pilot's column
            want = Mathf.Clamp(goal, -band, band) - ox;
        }
        return want;
    }

    void BeginWindup(Vector3 p, Transform t)
    {
        Windups++;
        Enter(Phase.Windup);
        var b = Behaviour;
        // the aim is locked NOW, inside a cone around straight down: moving
        // after the tell starts always dodges
        aim = Vector2.down;
        if (t != null)
        {
            Vector2 to = (Vector2)t.position - ((Vector2)p + (Vector2)MuzzleLocal());
            if (b.aimCone > 0f && to.sqrMagnitude > 1e-4f)
            {
                float deg = Mathf.Clamp(Vector2.SignedAngle(Vector2.down, to), -b.aimCone, b.aimCone);
                aim = Rotate(Vector2.down, deg);
            }
            lobTarget = t.position;
        }
        if (flipbook != null) flipbook.Drive(EnemyFlipbook.DrivePhase.Windup);
        if (charge != null) { charge.enabled = true; PulseCharge(); }
    }

    void PulseCharge()
    {
        if (charge == null) return;
        float k = Mathf.Clamp01(stateTime / Mathf.Max(TellFloorSeconds, Behaviour.tell));
        // grows as it charges, blinking on 12 fps steps (limited animation)
        float blink = Mathf.FloorToInt(stateTime * 12f) % 2 == 0 ? 1f : .8f;
        float d = Mathf.Lerp(.16f, .5f, k) * blink;
        charge.transform.localScale = Vector3.one * (d / Mathf.Max(.01f, charge.sprite.bounds.size.x));
    }

    void Release(Vector3 p, float baseX, Transform t)
    {
        var b = Behaviour;
        Volleys++;
        Enter(Phase.Release);
        if (charge != null) charge.enabled = false;
        if (flipbook != null) flipbook.Drive(EnemyFlipbook.DrivePhase.Release);
        if (b.attack == EnemyAttack.Lunge)
        {
            lungeFromX = lx;
            lungeFromY = ly;
            lungeToX = LungeGoal(baseX, t);
            lungeToY = -b.lungeDive;
            return;
        }
        ReleaseReservation();
        ShotsFired += EnemyVolley.Fire(this, b, (Vector2)p + (Vector2)MuzzleLocal(), aim, lobTarget);
    }

    static Vector2 Rotate(Vector2 v, float deg)
    {
        float r = deg * Mathf.Deg2Rad, c = Mathf.Cos(r), s = Mathf.Sin(r);
        return new Vector2(v.x * c - v.y * s, v.x * s + v.y * c);
    }

    // ---- SpawnSpace --------------------------------------------------------

    // A body rect widened to everything the behaviour can reach around `basePoint`.
    public static Rect Envelope(EnemyBehaviour b, Vector2 basePoint, Vector2 half)
    {
        if (b == null) return SpawnSpace.BodyRect(basePoint, half);
        return Rect.MinMaxRect(basePoint.x - b.bandX - half.x, basePoint.y - b.Down - half.y,
                               basePoint.x + b.bandX + half.x, basePoint.y + b.Up + half.y);
    }

    // The live enemy's sweep: its whole envelope, wherever in it the body is
    // now (the movers call this from SweptBounds).
    public static Rect Widen(EnemyBrain brain, Vector2 center, Vector2 half)
    {
        if (brain == null || brain.Behaviour == null || !brain.enabled) return SpawnSpace.BodyRect(center, half);
        Vector2 basePoint = new Vector2(center.x - brain.ox - brain.lx, center.y - brain.oy - brain.ly);
        return Envelope(brain.Behaviour, basePoint, half);
    }
}

// A spawn candidate's pattern: the behaviour's whole envelope around the
// spawn point (the spawner reuses one instance; no allocation per try).
public sealed class EnemyBrainPlan : IMovementFootprint
{
    public EnemyBehaviour behaviour;

    public Rect SweptBounds(Vector2 center, Vector2 half, float from, float to)
    {
        return EnemyBrain.Envelope(behaviour, center, half);
    }

    public bool SelfSteering => false;
}

// The shared budget for roster enemies' projectiles: how many may be alive,
// and how close together two enemies' volleys may start. Checked only when
// an enemy wants to begin a windup, never per frame.
public static class EnemyThreat
{
    // ---- tunables ----
    public static int MaxEnemyShots = 12;      // roster shots alive at once
    public static float VolleyGap = .4f;       // seconds between two enemies' windups
    // A live projectile as a share of a body, for the spawner's threat cap.
    public static float ShotWeight = .5f;

    // Tests: allow firing with no EliteDirector stepping the shots.
    public static bool ForceShooting;

    static float lastVolley = float.NegativeInfinity;
    // Shots promised by windups in progress: they count against the budget
    // before they exist, or several long tells would overrun it together.
    static int pending;

    public static int PendingShots => pending;
    public static void Unreserve(int shots) { pending = Mathf.Max(0, pending - Mathf.Max(0, shots)); }

    // Roster enemies fire only where their shots are stepped (gameS1's
    // EliteDirector) and never in the tutorial.
    public static bool ShootingAllowed
    {
        get
        {
            if (ForceShooting) return true;
            if (startMenu.youAreInTutorial) return false;
            return EliteDirector.Instance != null;
        }
    }

    // Roster shots in play right now.
    public static int LiveShots
    {
        get
        {
            var all = EliteSystem.Shots.All;
            int n = 0;
            for (int i = 0; i < all.Count; i++)
                if (all[i] != null && all[i].Active && all[i].RosterShot) n++;
            return n;
        }
    }

    public static bool TryReserveVolley(int shots)
    {
        float now = SpawnSpace.Clock;
        if (now < lastVolley) lastVolley = float.NegativeInfinity;   // a new run / a test's clock
        if (now - lastVolley < VolleyGap) return false;
        int n = Mathf.Max(1, shots);
        if (LiveShots + pending + n > MaxEnemyShots) return false;
        lastVolley = now;
        pending += n;
        return true;
    }

    public static void Reset() { lastVolley = float.NegativeInfinity; pending = 0; }
}

// Fires a behaviour's projectiles through the elites' pooled shots
// (EliteSystem.Shots), marked as roster shots: no friendly fire, riding the
// board. Returns how many left the muzzle (0 when the pool is spent).
public static class EnemyVolley
{
    public static int Fired;   // tests

    public static int Fire(EnemyBrain brain, EnemyBehaviour b, Vector2 at, Vector2 aim, Vector2 lobTarget)
    {
        var pool = EliteSystem.Shots;
        var style = b.ShotStyle;
        var source = brain.gameObject;
        int n = 0;
        switch (b.attack)
        {
            case EnemyAttack.Lob:
            {
                var s = pool.Fire(null, style, EliteShots.Kind.Glob, at, Vector2.zero);
                if (s == null) break;
                s.AsRosterShot(source, 0f);
                s.Lob(lobTarget, EnemyBehaviours.LobSeconds);
                n = 1;
                break;
            }
            case EnemyAttack.Ring:
            {
                int count = Mathf.Max(1, b.shotCount);
                for (int i = 0; i < count; i++)
                {
                    float deg = 360f * (i + .5f) / count;
                    n += One(pool, style, b, source, at, Turn(Vector2.down, deg));
                }
                break;
            }
            case EnemyAttack.Cross:
                // across the lane, away from its own rail
                n = Fan(pool, style, b, source, at, at.x > 0f ? Vector2.left : Vector2.right);
                break;
            case EnemyAttack.Shot:
                n = Fan(pool, style, b, source, at, aim);
                break;
        }
        Fired += n;
        return n;
    }

    static int Fan(EliteShots pool, EliteDef style, EnemyBehaviour b, GameObject source, Vector2 at, Vector2 aim)
    {
        int count = Mathf.Max(1, b.shotCount), n = 0;
        Vector2 side = new Vector2(-aim.y, aim.x);
        for (int i = 0; i < count; i++)
        {
            float k = i - (count - 1) * .5f;
            n += One(pool, style, b, source, at + side * (k * b.shotGap), Turn(aim, k * b.shotSpread));
        }
        return n;
    }

    static int One(EliteShots pool, EliteDef style, EnemyBehaviour b, GameObject source, Vector2 at, Vector2 direction)
    {
        var s = pool.Fire(null, style, b.shotKind, at, direction * b.shotSpeed);
        if (s == null) return 0;
        s.AsRosterShot(source, b.ride);
        return 1;
    }

    static Vector2 Turn(Vector2 v, float deg)
    {
        float r = deg * Mathf.Deg2Rad, c = Mathf.Cos(r), s = Mathf.Sin(r);
        return new Vector2(v.x * c - v.y * s, v.x * s + v.y * c);
    }
}
