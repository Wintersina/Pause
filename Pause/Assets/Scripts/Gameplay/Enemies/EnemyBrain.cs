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
// PRESENCE. A hazard (rocks, mines) is run as above: an offset over its
// scrolling mover. A PILOT (fighters, heavies, aliens; EnemyBehaviour.IsPilot)
// flies in world space instead -- its mover is switched to `station` and no
// longer scrolls it -- on an engagement script (StepPilot):
//
//   Waiting   above the view, its column reserved (PilotAirspace), until the
//             hazards already in that column have gone by
//   Entering  down to its station (Drop / Swoop); an alien Descends instead,
//             marching down the screen at its own speed
//   Engaging  the same primitives around its anchor and the same attack
//             state machine, for at most engageSeconds
//   Exiting   Climb / Peel out the top, or a telegraphed Run out the bottom
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
    public enum PilotStage { None, Waiting, Entering, Engaging, Exiting, Gone }

    // ---- pilots, shared (tunables) ----
    public const float MaxWaitSeconds = 4f;       // waiting for its column to clear, at most
    public const float SwoopOvershoot = .9f;      // a Swoop dips this far past its station
    public const float RunTellSeconds = .55f;     // the tell before an attack run
    public const float LeaveMargin = 1.2f;        // gone once this far past the top / bottom of the view
    public const float EarlyLeaveShare = .5f;     // volleys spent: may leave after this share of its window
    public const float CatchUpSpeed = 12f;        // u/s back to its line after giving way
    public const float DodgeSpeed = 4f;           // u/s sideways when ResolveSteer has to give way
    public const float LungeRecoverSeconds = .55f;
    public const float ShoveReturnSpeed = 3f;     // u/s back to its line after something pushed it (a shockwave)
    public const float SidestepLookSeconds = .5f; // how far up the board (seconds of scroll) it watches for hazards beside its column
    // A pilot holds in the player's reach (ReachHoldY, HostileReach): its
    // station, but never above the reach ceiling and never closer above the
    // ship than the standoff. Only when the ship is so close under it that
    // no windup would be fair from there does it climb to its firing height
    // (HoldY) for the attack, and drop back after it.
    //
    // HoldY, the firing height: higher than its station while the ship is
    // close under it, far enough above to keep its full windup clearance
    // (MinFireDistance + HoldMargin, x view), but never higher than
    // HoldTopDepth under the top of the view nor into the HUD band -- unless
    // the band hangs lower than the shallowest authored station
    // (HoldBandFloorDepth, 14% under the top: a stacked read-out on a phone
    // small in dp), where the pilot may still hold that high, as that
    // station already does.
    public const float HoldMargin = .1f;
    public const float HoldTopDepth = 1.1f;
    public const float HoldBandFloorDepth = 1.4f;

    // THE VIEW. A pilot's script is written for a view 10 u tall (the
    // authored camera). The game's camera shows more than that and differs by
    // phone (CameraFit: 13.2 u at 1080x1920, 17.4 u at 1080x2520), so every
    // distance and speed a pilot flies by -- station depth, entry, exit and
    // run speeds, dives, the windup clearances, its shots' speed -- is
    // multiplied by ViewScale: a pilot holds the same place ON SCREEN, takes
    // the same time to arrive and leave, and its shots take the same time to
    // reach the ship, whatever the camera shows. Lateral numbers are not
    // scaled (the lane is as wide as it was).
    public const float AuthoredViewHeight = 10f;
    public static float ViewScale => Mathf.Max(.5f, (CameraFit.ViewTop - CameraFit.ViewBottom) / AuthoredViewHeight);

    // Where a pilot whose station is `stationY` holds with the ship at
    // `shipY` (pure, for tests). The ship's ceiling (ShipReach.TopFor: right under the HUD band)
    // is as high as the deepest stations (30% under the top), so a ship
    // parked high would sit on a deep pilot and deny every windup (they need
    // MinFireDistance x view, 18% of the view): instead the pilot backs up
    // to keep that clearance, up to its ceiling (HoldTopDepth under the top,
    // the HUD band's bottom, never lower than HoldBandFloorDepth under the
    // top), and returns to its station when the ship drops.
    public static float HoldY(float stationY, float shipY, float view, float viewTop, float bandBottom)
    {
        float need = FireNeed(shipY, view);
        float cap = Mathf.Max(Mathf.Min(viewTop - HoldTopDepth * view, bandBottom), viewTop - HoldBandFloorDepth * view);
        return Mathf.Max(stationY, Mathf.Min(need, cap));
    }

    // The lowest a pilot can hold straight above a ship at `shipY` and still
    // start a fair windup (MinFireDistance + HoldMargin, x view).
    public static float FireNeed(float shipY, float view) => shipY + (MinFireDistance + HoldMargin) * view;

    // Where a pilot whose station is `stationY` holds in the player's reach
    // (pure, for tests): its station, no higher than the reach ceiling
    // (HostileReach: its collider, `colliderBelow` under its centre, in
    // contact range of a ship at the top of its reach) and no closer above
    // the ship at `shipY` than the standoff (its drawing `drawnBelow` under
    // its centre clear of the hull). `up` / `down`: how far its own pattern
    // carries it above / below its anchor.
    public static float ReachHoldY(float stationY, float shipY, float shipTop, float colliderBelow, float drawnBelow, float up, float down)
    {
        float ceiling = HostileReach.CeilingFor(shipTop, colliderBelow) - up;
        float floor = shipY + HostileReach.StandoffFor(drawnBelow) + down;
        return Mathf.Max(Mathf.Min(stationY, ceiling), floor);
    }

    // How far a pilot's own pattern carries it above / below its anchor
    // (a Brake hover is its station: nothing).
    public static float PatternUp(EnemyBehaviour b) => b == null || b.vertical == EnemyVertical.Brake ? 0f : b.rise;
    public static float PatternDown(EnemyBehaviour b) => b == null || b.vertical == EnemyVertical.Brake ? 0f : b.sink;

    // Climbing to its firing height for an attack (HostileReach RISE).
    public bool Rising => rising;
    public float RisenSeconds => risenFor;

    // Pilots fly their engagement scripts (false: every enemy rides the
    // scroll as a hazard, the first pass's behaviour).
    public static bool PilotsEnabled = true;

    // A hazard squeezed in beside a pilot's column keeps a narrower lateral
    // band than its behaviour's (the spawner sets it; 1 = the whole band).
    public float BandScale { get; private set; } = 1f;
    public void SetBandScale(float scale) { BandScale = Mathf.Clamp01(scale); }
    float Band => Behaviour.bandX * BandScale * reach;

    // A rock's size (HazardSize): its pattern's amplitude (Reach: band, rise,
    // sink) and pace (speeds up, periods down). 1 for everything else.
    public float Size { get; private set; } = 1f;
    public float Reach => reach;
    public float Pace => pace;
    float reach = 1f, pace = 1f;

    public void SetSize(float size, float reachScale, float paceScale)
    {
        Size = size > 0f ? size : 1f;
        reach = reachScale > 0f ? reachScale : 1f;
        pace = paceScale > 0f ? paceScale : 1f;
        if (Def != null)
        {
            halfX = Def.ColliderSize.x * .5f * Size;
            halfY = Def.ColliderSize.y * .5f * Size;
            drawnHalfY = SpawnSpace.BodyHalf(Def, Size).y;
        }
    }

    public bool IsPilot { get; private set; }
    public PilotStage Stage { get; private set; }
    public Vector2 Anchor => anchor;
    public float ColumnHalf => columnHalf;
    public float EngagedSeconds => engaged;
    public float InViewSeconds => seen;
    public PilotExit LeftBy { get; private set; }
    // True once told to clear out (boss, portal); it then Climbs.
    public bool Ordered { get; private set; }

    // ---- fairness, shared by every enemy (tunables) ----
    public const float TellFloorSeconds = .45f;   // no windup is ever shorter
    public const float ReleaseSeconds = .2f;      // tell cell 5 held

    // The windup's length for a behaviour: its tell, never under the floor -- and an instant-hit area hazard
    // (Blast, Strike) is told at least AttackHazard.MinTellSeconds (FR1).
    public static float TellFor(EnemyBehaviour b)
    {
        float t = Mathf.Max(TellFloorSeconds, b.tell);
        return b.IsAreaHazard ? Mathf.Max(t, AttackHazard.MinTellSeconds) : t;
    }
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

    // THE anchor: what the brain's pattern is added to. For a hazard it is the
    // mover's own position (the body minus the brain's offset); for a pilot it
    // is its station / flight anchor in world space.
    //
    // EXTERNAL DISPLACEMENT (a shove: something else writes transform.position).
    // A hazard's brain only ever ADDS its offset's change each frame, so a
    // shove moves its Base with it and stays. A pilot notices that it is not
    // where it last put itself and flies back to its line at ShoveReturnSpeed
    // -- over time, never a snap -- while its anchor (its station) stays put;
    // set Base to move the station itself.
    public Vector2 Base
    {
        get { return IsPilot ? anchor : new Vector2(transform.position.x - ox - lx, transform.position.y - oy - ly); }
        set
        {
            if (IsPilot) { anchor = value; return; }
            Vector2 d = value - Base;
            transform.position += new Vector3(d.x, d.y, 0f);
        }
    }

    // True while a pilot is flying back from where something pushed it.
    public bool Displaced { get; private set; }

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
    float halfX, halfY, drawnHalfY;   // collider half-width / half-height, drawn half-height
    bool rising;
    float risenFor;
    // pilot state
    Vector2 anchor;                 // world: where its pattern is centred
    float columnHalf, stageTime, engaged, waited, peelDir, runToX, runFromX;
    bool swoopDipped, runDiving, hasLastSet;
    float viewScale = 1f;           // this frame's ViewScale (pilots; 1 for a hazard)
    Vector3 lastSet;                // where it last put itself (a different position next frame = it was pushed)
    SpawnFootprint footprint;
    moveEnimes weaverMover;
    moveItemEnmInStrightLine scrollMover;
    int reserved;                   // shots held in EnemyThreat's budget during a windup
    RailMineLaser laser;            // a mine's laser, from its windup until it has cooled (EnemyAttack.Laser)

    // The mine's laser while it aims, burns or cools (tests); null otherwise.
    // A laser mine that rides its rail beside the ship (RailMineMount.StepRide).
    public bool RidesRail => onRail && Armed && Behaviour != null && Behaviour.attack == EnemyAttack.Laser;
    // ... and has fired its volleys and cooled: time to let the board take it.
    public bool RideFinished => Volleys >= Behaviour.maxVolleys && State == Phase.Idle && laser == null;

    public RailMineLaser Laser => laser != null && laser.Owner == transform ? laser : null;

    public void Init(EnemyDef def, EnemyBehaviour behaviour)
    {
        Def = def;
        Behaviour = behaviour;
        if (behaviour == null) { enabled = false; return; }
        TryGetComponent(out flipbook);
        if (TryGetComponent(out mount)) mount.brain = this;
        // (TryGetComponent: a missed GetComponent is a fake null in the editor)
        if (TryGetComponent(out moveEnimes weaver)) { hostMover = weaver; weaverMover = weaver; }
        else if (TryGetComponent(out moveItemEnmInStrightLine scroller)) { hostMover = scroller; scrollMover = scroller; }
        else hostMover = null;
        onRail = def.role == EnemyRole.Mine;
        halfX = def.ColliderSize.x * .5f * Size;
        halfY = def.ColliderSize.y * .5f * Size;
        drawnHalfY = SpawnSpace.BodyHalf(def, Size).y;
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
        // a laser mine: its world's beam art and the laser pool, ready before
        // it ever fires (at spawn, so firing never builds anything)
        if (behaviour.attack == EnemyAttack.Laser && Armed) { MineLaserArt.For(def.world); RailMineLasers.Prewarm(); }
        // an area hazard: its pool is built at spawn too, so arming one never builds anything
        if (behaviour.attack == EnemyAttack.Blast && Armed) { var warm = AttackBlast.Pool; }
        else if (behaviour.attack == EnemyAttack.Strike && Armed) { var warm = AttackStrike.Pool; }

        IsPilot = PilotsEnabled && behaviour.IsPilot && def.role != EnemyRole.Chaser && !onRail && hostMover != null;
        Stage = PilotStage.None;
        if (!IsPilot) return;
        // its mover holds station from here on; the brain flies it
        if (weaverMover != null) weaverMover.station = true;
        if (scrollMover != null) scrollMover.station = true;
        anchor = transform.position;
        columnHalf = PilotAirspace.ColumnHalf(def, behaviour);
        peelDir = anchor.x >= 0f ? -1f : 1f;   // peels toward the open side of the lane
        Stage = PilotStage.Waiting;
        PilotAirspace.Register(this);
    }

    // Told to clear out (a boss or a portal is coming): it Climbs away.
    public void Order()
    {
        Ordered = true;
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
        if (Behaviour.attack == EnemyAttack.Cross || Behaviour.attack == EnemyAttack.Laser)
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
        if (IsPilot) { StepPilot(dt); return; }
        // the spawner clamps a mine to its rail after it is built
        if (onRail && mount == null && TryGetComponent(out mount)) mount.brain = this;

        if (onRail && mount != null) mount.StepRide(dt, Target);

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
        float band = Band;
        float total = Mathf.Clamp(ox + lx, -band, band);
        if (!onRail)
        {
            float lane = SpawnLane.LaneHalf - halfX;
            if (Mathf.Abs(baseX) <= lane) total = Mathf.Clamp(baseX + total, -lane, lane) - baseX;
        }
        lx = total - ox;
        float totalY = Mathf.Clamp(oy + ly, -Behaviour.Down * reach, Behaviour.Up * reach);
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

        // the mine's laser runs on the same running frames (frozen with the world)
        var l = Laser;
        if (l != null)
        {
            l.Step(dt);
            if (!l.Active) laser = null;
        }
        else laser = null;
    }

    // ---- movement ----------------------------------------------------------

    void StepLateral(float dt, float baseX, Transform t, bool committed)
    {
        var b = Behaviour;
        float band = Band;
        // (pace: a rock's size makes its pattern slower or livelier; 1 otherwise)
        float speed = b.lateralSpeed * pace, period = Mathf.Max(.1f, b.lateralPeriod / pace);
        switch (b.lateral)
        {
            case EnemyLateral.Drift:
                if (committed && b.Shoots) break;
                ox += dir * speed * dt;
                if (ox > band) { ox = band; dir = -1f; }
                else if (ox < -band) { ox = -band; dir = 1f; }
                break;
            case EnemyLateral.Glide:
                ox = Mathf.Clamp(ox + dir * speed * dt, -band, band);
                break;
            case EnemyLateral.Sway:
                if (committed) break;
                lat += dt;
                ox = band * Mathf.Sin(lat / period * 2f * Mathf.PI);
                break;
            case EnemyLateral.Orbit:
                lat += dt;
                float a = lat / period * 2f * Mathf.PI * dir;
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
        // (reach / pace: a rock's size; 1 for everything else)
        float period = Mathf.Max(.1f, b.verticalPeriod / pace);
        switch (b.vertical)
        {
            case EnemyVertical.Bob:
                vert += dt;
                oy = b.rise * reach * Mathf.Sin(vert / period * 2f * Mathf.PI);
                break;
            case EnemyVertical.Pulse:
            {
                // a kick up the board over the first quarter, a slow sink back
                vert += dt;
                float k = Mathf.Repeat(vert / period, 1f);
                float f = k < .25f ? Mathf.Sin(k / .25f * Mathf.PI * .5f) : .5f + .5f * Mathf.Cos((k - .25f) / .75f * Mathf.PI);
                oy = b.rise * reach * f;
                break;
            }
            case EnemyVertical.Brake:
                // sheds part of the scroll once it is in view: a hover
                if (inView && oy < b.rise)
                    oy = Mathf.Min(b.rise, oy + SpawnSpace.ScrollSpeed * Mathf.Clamp01(b.verticalSpeed) * dt);
                break;
            case EnemyVertical.Sink:
            case EnemyVertical.Creep:
                if (inView && !committed) oy = Mathf.Max(-b.sink * reach, oy - b.verticalSpeed * pace * dt);
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
                    if (!EnemyThreat.TryReserveVolley(b.ThreatCount)) { cooldown = .25f; return; }
                    reserved = Mathf.Max(1, b.ThreatCount);
                }
                BeginWindup(p, t);
                break;
            case Phase.Windup:
                PulseCharge();
                if (stateTime < TellFor(b)) return;
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
                else if (b.attack == EnemyAttack.Laser)
                {
                    // the mine holds still while its beam burns
                    var l = Laser;
                    if (l != null && l.State == RailMineLaser.Phase.Beam && stateTime < RailMineLaser.BeamSeconds) return;
                    if (stateTime < ReleaseSeconds) return;
                }
                else if (stateTime < ReleaseSeconds) return;
                Enter(Phase.Recover);
                if (flipbook != null) flipbook.Drive(EnemyFlipbook.DrivePhase.None);
                break;
            case Phase.Recover:
                if (IsPilot && b.attack == EnemyAttack.Lunge)
                {
                    // a pilot's lunge is a dive and recover: back to its station
                    float k = Mathf.Clamp01(stateTime / LungeRecoverSeconds);
                    lx = Mathf.Lerp(lungeToX, 0f, k);
                    ly = Mathf.Lerp(lungeToY, 0f, k);
                    if (k < 1f) return;
                }
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

    void OnDisable()
    {
        ReleaseReservation();
        CancelHazards();
        var l = Laser;
        if (l != null) l.Cancel();
        laser = null;
        if (IsPilot) PilotAirspace.Unregister(this);
    }

    // ---- pilots ------------------------------------------------------------

    void Go(PilotStage next)
    {
        Stage = next;
        stageTime = 0f;
    }

    void StepPilot(float dt)
    {
        if (Stage == PilotStage.Gone) return;
        var b = Behaviour;
        Vector3 p = transform.position;
        if (hasLastSet && ((Vector2)(p - lastSet)).sqrMagnitude > 1e-6f) Displaced = true;   // pushed since its last step
        float top = CameraFit.ViewTop, bottom = CameraFit.ViewBottom;
        bool inView = p.y < top - ViewInset && p.y > bottom;
        if (inView) seen += dt;
        var t = Target;
        stageTime += dt;
        if (!Ordered && PilotAirspace.MustClear) Ordered = true;
        float view = Mathf.Max(.5f, (top - bottom) / AuthoredViewHeight);
        viewScale = view;
        float stationY = top - b.stationDepth * view;
        bool descends = b.entry == PilotEntry.Descend;
        // the station it holds now: in the player's reach, climbing to its
        // firing height only for an attack while the ship is close under it
        float holdY = stationY;
        if (!descends && t != null)
        {
            float fireHold = HoldY(stationY, t.position.y, view, top, PlayField.Live.bandBottom);
            if (!HostileReach.Enabled) holdY = fireHold;
            else
            {
                float reachHold = ReachHoldY(stationY, t.position.y, ShipReach.Top, halfY, drawnHalfY, PatternUp(b), PatternDown(b));
                // too close above the ship for a fair windup straight above it: it
                // climbs toward the lowest fair height (HoldY from the reach hold,
                // capped under the HUD band as ever) -- only if a windup would be
                // fair from there (MayAttack's clearances, where it is across)
                float riseHold = HoldY(reachHold, t.position.y, view, top, PlayField.Live.bandBottom);
                float rdy = riseHold - t.position.y, rdx = p.x - t.position.x;
                bool fairUp = rdy >= MinFireAbove * view && rdx * rdx + rdy * rdy >= MinFireDistance * view * MinFireDistance * view;
                StepRise(dt, b, Stage == PilotStage.Engaging && riseHold > reachHold + HostileReach.MinRise && fairUp);
                holdY = rising ? riseHold : reachHold;
            }
        }

        switch (Stage)
        {
            case PilotStage.Waiting:
                // above the view, column reserved: in once what was already
                // coming down that column has gone by
                waited += dt;
                if (Ordered) { Depart(PilotExit.Climb); return; }
                if (waited < MaxWaitSeconds && !PilotAirspace.ColumnClear(this, descends ? top : stationY)) return;
                Go(descends ? PilotStage.Engaging : PilotStage.Entering);
                break;
            case PilotStage.Entering:
            {
                if (Ordered) { BeginExit(PilotExit.Climb); break; }
                bool swoop = b.entry == PilotEntry.Swoop && !swoopDipped;
                float goal = swoop ? holdY - SwoopOvershoot * view : holdY;
                // the dip never drops its body onto the ship (one parked at the top of its reach, or wherever it is)
                if (swoop)
                {
                    float floor = HostileReach.Enabled && t != null
                        ? t.position.y + HostileReach.StandoffFor(drawnHalfY) + PatternDown(b)
                        : ShipReach.EntryFloor + halfX;
                    goal = Mathf.Max(goal, Mathf.Min(holdY, floor));
                }
                float speed = b.entrySpeed * view * (b.entry == PilotEntry.Swoop ? (swoop ? 1.5f : .6f) : 1f);
                anchor.y = Mathf.MoveTowards(anchor.y, goal, speed * dt);
                if (Mathf.Abs(anchor.y - goal) > 1e-3f) break;
                if (swoop) swoopDipped = true;
                else Go(PilotStage.Engaging);
                break;
            }
            case PilotStage.Engaging:
                engaged += dt;
                if (descends)
                {
                    anchor.y -= b.descendSpeed * view * dt;
                    if (anchor.y < bottom - LeaveMargin) { Depart(PilotExit.Run); return; }
                }
                if (State != Phase.Idle) break;   // never leaves mid-attack (nor moves its station)
                // (not while a shove has it off its line: it flies back to the line it was pushed from)
                if (!descends && !Displaced)
                {
                    float holdSpeed = HostileReach.Enabled ? Mathf.Max(b.entrySpeed * .6f, HostileReach.PilotRiseSpeed) : b.entrySpeed * .6f;
                    anchor.y = Mathf.MoveTowards(anchor.y, holdY, holdSpeed * view * dt);
                }
                bool spent = Armed && b.maxVolleys > 0 && Volleys >= b.maxVolleys;   // nothing left to fire
                if (Ordered) BeginExit(PilotExit.Climb);
                else if (!descends && (engaged >= b.engageSeconds || (spent && engaged >= b.engageSeconds * EarlyLeaveShare)))
                    BeginExit(b.exit);
                break;
            case PilotStage.Exiting:
                StepExit(dt, b, t, top, bottom);
                if (Stage == PilotStage.Gone) return;
                break;
        }

        bool committed = State == Phase.Windup || State == Phase.Release || runDiving || (Stage == PilotStage.Exiting && LeftBy == PilotExit.Run);
        if (!(Stage == PilotStage.Exiting && LeftBy == PilotExit.Peel)) StepLateral(dt, anchor.x, t, committed);
        if (b.vertical != EnemyVertical.Brake) StepVertical(dt, inView, committed);   // (a pilot's hover is its station)
        if (Stage == PilotStage.Engaging) StepAttack(dt, p, anchor.x, inView, t);

        // where it wants to be: inside its band -- cut back on a side while a
        // hazard passes there (it sidesteps toward its own column, which no
        // hazard is ever routed down) -- and inside the lane
        float band = b.bandX;
        if (footprint == null) TryGetComponent(out footprint);
        float bandLo = -band, bandHi = band;
        if (band > 0f && footprint != null && Stage != PilotStage.Waiting)
        {
            bool diving = runDiving || (State == Phase.Release && b.attack == EnemyAttack.Lunge);
            PilotAirspace.BandLimits(this, p, footprint.half, band, SpawnSpace.ScrollSpeed * SidestepLookSeconds + .6f,
                                     diving ? 4f : .4f, out bandLo, out bandHi);
        }
        float total = Mathf.Clamp(ox + lx, bandLo, bandHi);
        float lane = SpawnLane.LaneHalf - halfX;
        float wantX = Mathf.Clamp(anchor.x + total, -lane, lane);
        float wantY = anchor.y + oy + ly;
        Vector2 from = p;
        Vector2 want = new Vector2(wantX, wantY);
        if (Displaced && (want - from).sqrMagnitude < .05f * .05f) Displaced = false;   // back on its line
        Vector2 wish = Vector2.MoveTowards(from, want, (Displaced ? ShoveReturnSpeed : CatchUpSpeed * view) * dt);

        // the chaser's rule: never step into another body (another pilot, a
        // chaser, an elite, a hazard that got into its column anyway)
        if (footprint != null && footprint.isActiveAndEnabled)
            wish = SpawnSpace.ResolveSteer(footprint, from, wish, SpawnSpace.ScrollSpeed * dt, DodgeSpeed * dt);
        transform.position = new Vector3(wish.x, wish.y, p.z);
        lastSet = transform.position;
        hasLastSet = true;
    }

    // RISE: with the ship too close under its reach hold for a fair windup,
    // an armed pilot with volleys left climbs to its firing height from
    // RiseLead before its next windup may start; it holds there through
    // the attack (the anchor never moves mid-attack) and drops back once it
    // is recovering its cooldown -- or after RiseMaxSeconds if the attack
    // never started (the shot budget), with half a cooldown to wait.
    void StepRise(float dt, EnemyBehaviour b, bool closeUnder)
    {
        if (State != Phase.Idle) return;   // mid-attack: it stays where it is
        bool wants = closeUnder && Armed && b.Attacks && b.attack != EnemyAttack.Cross && Volleys < b.maxVolleys &&
                     cooldown <= HostileReach.RiseLead && !Ordered;
        if (wants && risenFor < HostileReach.RiseMaxSeconds)
        {
            rising = true;
            risenFor += dt;
            return;
        }
        if (rising && risenFor >= HostileReach.RiseMaxSeconds) cooldown = Mathf.Max(cooldown, b.cooldown * .5f);
        rising = false;
        risenFor = 0f;
    }

    void BeginExit(PilotExit how)
    {
        LeftBy = how;
        Go(PilotStage.Exiting);
        runDiving = false;
        if (State != Phase.Idle) { Enter(Phase.Idle); ReleaseReservation(); }
        if (charge != null) charge.enabled = false;
        if (how == PilotExit.Run)
        {
            // the run is its last attack: told like any other
            if (flipbook != null) flipbook.Drive(EnemyFlipbook.DrivePhase.Windup);
        }
        else if (flipbook != null) flipbook.Drive(EnemyFlipbook.DrivePhase.None);
    }

    void StepExit(float dt, EnemyBehaviour b, Transform t, float top, float bottom)
    {
        switch (LeftBy)
        {
            case PilotExit.Run:
                if (!runDiving)
                {
                    if (stageTime < Mathf.Max(TellFloorSeconds, RunTellSeconds)) return;
                    // the line is locked as the tell ends: its own column, shifted
                    // toward the pilot as far as its band allows
                    runDiving = true;
                    runFromX = ox + lx;
                    float goal = runFromX;
                    if (b.lungeX > 0f && t != null) goal = Mathf.Lerp(runFromX, t.position.x - anchor.x, b.lungeX);
                    runToX = Mathf.Clamp(goal, -b.bandX, b.bandX);
                    stageTime = 0f;
                    if (flipbook != null) flipbook.Drive(EnemyFlipbook.DrivePhase.Release);
                }
                lx = Mathf.Lerp(runFromX, runToX, Mathf.Clamp01(stageTime / .25f)) - ox;
                anchor.y -= b.runSpeed * viewScale * Mathf.Clamp01(.4f + stageTime * 3f) * dt;
                if (anchor.y + oy + ly < bottom - LeaveMargin) Depart(PilotExit.Run);
                break;
            default:
                // retreats: eases off, then climbs away (a Peel swings to the open side)
                anchor.y += b.exitSpeed * viewScale * Mathf.Clamp01(.25f + stageTime * 1.5f) * dt;
                if (LeftBy == PilotExit.Peel)
                    ox = Mathf.MoveTowards(ox, peelDir * b.bandX - lx, 1.8f * dt);
                if (anchor.y > top + LeaveMargin) Depart(LeftBy);
                break;
        }
    }

    // Off the screen and out of play. (In the editor's headless runs the
    // object is parked far off the board for the test's own clean-up.)
    void Depart(PilotExit how)
    {
        LeftBy = how;
        Stage = PilotStage.Gone;
        ReleaseReservation();
        PilotAirspace.NoteDeparture(this);
        PilotAirspace.Unregister(this);
        if (Application.isPlaying) Destroy(gameObject);
        else
        {
            transform.position = new Vector3(transform.position.x, how == PilotExit.Run ? -60f : 60f, transform.position.z);
            enabled = false;
        }
    }

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
        if (onRail && mount != null && !mount.AttackReady) return false;   // a laser mine fires from its hold row
        if (Behaviour.attack == EnemyAttack.Lunge && EliteInLungePath(p)) return false;
        Vector3 s = t.position;
        if (Behaviour.attack == EnemyAttack.Cross || Behaviour.attack == EnemyAttack.Laser)
            return Mathf.Abs(p.y - s.y) < 6f && p.y > s.y - .5f;   // its row matters, not its height
        if (p.y - s.y < MinFireAbove * viewScale) return false;
        float clear = MinFireDistance * viewScale;
        return ((Vector2)(s - p)).sqrMagnitude >= clear * clear;
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
        float reachY = b.lungeDive * viewScale + SpawnSpace.ScrollSpeed * seconds + halfX;
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
                case EnemyLateral.Glide: return b.lateralSpeed * pace;
                case EnemyLateral.Track: return b.lateralSpeed;
                case EnemyLateral.Sway:
                case EnemyLateral.Orbit: return b.bandX * reach * 2f * Mathf.PI / Mathf.Max(.1f, b.lateralPeriod / pace);
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
        reach = new Vector2(LungeGoal(baseX, Target) - lx, -b.lungeDive * viewScale - ly);
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
        if (b.attack == EnemyAttack.Laser)
        {
            // the aim line shows the row the beam will burn, through the tell's end
            var old = Laser;
            if (old != null) old.Cancel();
            laser = RailMineLasers.Take();
            if (laser != null) laser.Arm(transform, Def.world, Mathf.Max(TellFloorSeconds, b.tell));
        }
        else if (b.IsAreaHazard) ArmHazards(p, t);
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
            lungeToY = -b.lungeDive * viewScale;
            return;
        }
        ReleaseReservation();
        if (b.attack == EnemyAttack.Laser)
        {
            var l = Laser;
            if (l != null && l.State == RailMineLaser.Phase.Aim)
            {
                l.Fire();
                ShotsFired++;
                EnemyVolley.Volleys++;
                EnemyVolley.Fired++;
            }
            return;
        }
        if (b.IsAreaHazard)
        {
            int n = IgniteHazards();
            ShotsFired += n;
            EnemyVolley.Volleys++;
            EnemyVolley.Fired += n;
            return;
        }
        ShotsFired += EnemyVolley.Fire(this, b, (Vector2)p + (Vector2)MuzzleLocal(), aim, lobTarget);
    }

    // ---- the themed area hazards (Blast, Strike): armed with the windup, ignited at the release ----

    readonly AttackHazard[] armed = new AttackHazard[4];
    static readonly float[] laneBuffer = new float[4];

    void ArmHazards(Vector3 p, Transform t)
    {
        var b = Behaviour;
        float tell = TellFor(b);
        int world = Def != null ? Def.world : 0;
        // a pilot holds its place in the world; a hazard's ground rides the board
        float ride = IsPilot ? 0f : b.ride;
        CancelHazards();
        Vector2 target = t != null ? (Vector2)t.position : (Vector2)p + Vector2.down * 3f;
        if (b.attack == EnemyAttack.Blast)
        {
            var spec = b.blast;
            spec.world = world;
            spec.ride = ride;
            Vector2 offset = MuzzleLocal();
            var blast = AttackBlast.Arm(spec, (Vector2)p + offset, target, tell, gameObject);
            if (blast != null) { blast.Follow(transform, offset); armed[0] = blast; }
            return;
        }
        var ss = b.strike;
        ss.world = world;
        ss.ride = ride;
        int lanes = StrikeLanes.Pick(target.x, Mathf.Min(armed.Length, b.strikeLanes), b.laneSpacing, BossRails.DrawnInnerEdge, ss.hitHalf > 0f ? ss.hitHalf : .18f, laneBuffer);
        for (int i = 0; i < lanes; i++)
            armed[i] = AttackStrike.Arm(ss, laneBuffer[i], target.y, tell, gameObject);
    }

    int IgniteHazards()
    {
        int n = 0;
        for (int i = 0; i < armed.Length; i++)
        {
            if (armed[i] == null) continue;
            armed[i].Ignite();
            armed[i] = null;
            n++;
        }
        return n;
    }

    // Its tell was cut short (it died, was told to leave): the hazards it armed go with it.
    void CancelHazards()
    {
        for (int i = 0; i < armed.Length; i++)
        {
            if (armed[i] != null && armed[i].State == AttackHazard.Phase.Tell) armed[i].Cancel();
            armed[i] = null;
        }
    }

    static Vector2 Rotate(Vector2 v, float deg)
    {
        float r = deg * Mathf.Deg2Rad, c = Mathf.Cos(r), s = Mathf.Sin(r);
        return new Vector2(v.x * c - v.y * s, v.x * s + v.y * c);
    }

    // ---- SpawnSpace --------------------------------------------------------

    // A body rect widened to everything the behaviour can reach around
    // `basePoint` (`reach`: a rock's size scales its pattern, HazardSize).
    public static Rect Envelope(EnemyBehaviour b, Vector2 basePoint, Vector2 half, float bandScale = 1f, float reach = 1f)
    {
        if (b == null) return SpawnSpace.BodyRect(basePoint, half);
        float band = b.bandX * bandScale * reach;
        return Rect.MinMaxRect(basePoint.x - band - half.x, basePoint.y - b.Down * reach - half.y,
                               basePoint.x + band + half.x, basePoint.y + b.Up * reach + half.y);
    }

    // A pilot's sweep (self-steering): where it may be over the next
    // moments -- it holds its place in the world, so it rises through the
    // board at the scroll speed, plus its own speed every way.
    public static Rect PilotSweep(EnemyBrain brain, Vector2 center, Vector2 half, float to)
    {
        float own = Mathf.Min(1.2f, (brain != null && brain.Behaviour != null ? brain.Behaviour.entrySpeed : 3f) * to);
        return Rect.MinMaxRect(center.x - half.x - own, center.y - half.y - own,
                               center.x + half.x + own, center.y + half.y + own + SpawnSpace.ScrollSpeed * to);
    }

    // The live enemy's sweep: its whole envelope, wherever in it the body is
    // now (the movers call this from SweptBounds).
    public static Rect Widen(EnemyBrain brain, Vector2 center, Vector2 half)
    {
        if (brain == null || brain.Behaviour == null || !brain.enabled) return SpawnSpace.BodyRect(center, half);
        Vector2 basePoint = new Vector2(center.x - brain.ox - brain.lx, center.y - brain.oy - brain.ly);
        return Envelope(brain.Behaviour, basePoint, half, brain.BandScale, brain.reach);
    }
}

// A spawn candidate's pattern: the behaviour's whole envelope around the
// spawn point (the spawner reuses one instance; no allocation per try).
public sealed class EnemyBrainPlan : IMovementFootprint
{
    public EnemyBehaviour behaviour;
    public float bandScale = 1f;
    public float reach = 1f;   // the candidate's size (HazardSize.Reach)

    // The lateral band it will sweep.
    public float Band => behaviour != null ? behaviour.bandX * reach : 0f;

    public Rect SweptBounds(Vector2 center, Vector2 half, float from, float to)
    {
        return EnemyBrain.Envelope(behaviour, center, half, bandScale, reach);
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

    // The live budget: the two numbers above, plus what later loops
    // (LoopRules) and a portal kept waiting (PortalPressure) add. Never more
    // than PortalPressure.ShotCap alive, whatever asks.
    public static int ShotBudget
    {
        get
        {
            return Mathf.Min(Mathf.Max(MaxEnemyShots, PortalPressure.ShotCap),
                             MaxEnemyShots + LoopRules.ShotBonus(RunLoop.Index) + PortalPressure.ShotBonus);
        }
    }

    public static float Gap
    {
        get { return VolleyGap * LoopRules.VolleyGapScale(RunLoop.Index) * PortalPressure.VolleyGapScale; }
    }

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
            return n + RailMineLasers.LiveBeams + AttackHazard.LiveThreat;   // a burning mine laser or area hazard counts as shots
        }
    }

    public static bool TryReserveVolley(int shots)
    {
        float now = SpawnSpace.Clock;
        if (now < lastVolley) lastVolley = float.NegativeInfinity;   // a new run / a test's clock
        if (now - lastVolley < Gap) return false;
        int n = Mathf.Max(1, shots);
        if (LiveShots + pending + n > ShotBudget) return false;
        lastVolley = now;
        pending += n;
        return true;
    }

    public static void Reset() { lastVolley = float.NegativeInfinity; pending = 0; }
}

// Fires a behaviour's projectiles through the elites' pooled shots
// (EliteSystem.Shots), marked as roster shots: riding the board, friendly
// fire on (FriendlyFire hostile fire; never the shooter itself). Returns how many left the muzzle (0 when the pool is spent).
public static class EnemyVolley
{
    public static int Fired, Volleys;   // tests, the probe

    public static int Fire(EnemyBrain brain, EnemyBehaviour b, Vector2 at, Vector2 aim, Vector2 lobTarget)
    {
        var pool = EliteSystem.Shots;
        var style = b.ShotStyle;
        var source = brain.gameObject;
        pilotVolley = brain.IsPilot;
        pilotView = brain.IsPilot ? EnemyBrain.ViewScale : 1f;
        Volleys++;
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

    // A hazard's shot rides the board with it (its speed is over the board);
    // a pilot holds its place in the world, so its shot flies in world space,
    // a little faster (PilotShotSpeed), like an elite's.
    public const float PilotShotSpeed = 1.25f;

    static int One(EliteShots pool, EliteDef style, EnemyBehaviour b, GameObject source, Vector2 at, Vector2 direction)
    {
        float speed = pilotVolley ? b.shotSpeed * PilotShotSpeed * pilotView : b.shotSpeed;
        speed *= PortalPressure.ShotSpeedScale;   // x1 unless a portal has been kept waiting into overdrive
        var s = pool.Fire(null, style, b.shotKind, at, direction * speed);
        if (s == null) return 0;
        s.AsRosterShot(source, pilotVolley ? 0f : b.ride);
        return 1;
    }

    static bool pilotVolley;
    static float pilotView = 1f;

    static Vector2 Turn(Vector2 v, float deg)
    {
        float r = deg * Mathf.Deg2Rad, c = Mathf.Cos(r), s = Mathf.Sin(r);
        return new Vector2(v.x * c - v.y * s, v.x * s + v.y * c);
    }
}
