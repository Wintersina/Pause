using UnityEngine;

// A hazard that actually hunts the player instead of just scrolling past.
//
// Spawned from below the visible board (enmiesOnBoard.spawnChaser), it
// accelerates up toward the player's current position -- including
// following left/right dodges -- for a few seconds, then gives up and
// drifts in a loose orbit around wherever the chase left off. It stays on
// screen as a lingering hazard from there: the player can still run into
// it, and it carries the same "Enimey" tag every other hazard does, so
// ShipPowerController's ultimate (laser/missiles/shockwave/railgun) can
// target it exactly like an asteroid.
//
// It is SpawnSpace's self-steering pattern: it holds its place in the world
// while the board pours past it, so it is the one that gives way. Each frame
// it looks a little way up the board and sidesteps anything coming at it,
// and its move goes through SpawnSpace.ResolveSteer, which never lets it
// step into another enemy (worst case the scroll carries it along until it
// can slip past). It runs in LateUpdate, after the board-locked movers.
[DefaultExecutionOrder(50)]
public class ChaserEnemy : MonoBehaviour, IMovementFootprint
{
    [Tooltip("How long, in seconds, this actively closes in on the player before giving up and wandering.")]
    public float chaseSeconds = 3.5f;

    [Tooltip("World units/second while closing in -- ramps from startChaseSpeed up to this over the chase.")]
    public float chaseSpeed = 2.4f;

    [Tooltip("Chase speed at the moment it spawns, before it has had a chance to accelerate.")]
    public float startChaseSpeed = 0.9f;

    [Tooltip("World units/second while idly drifting after the chase ends.")]
    public float wanderSpeed = 1.1f;

    [Tooltip("Radius of the idle drift loop around the point the chase left off.")]
    public float wanderRadius = 0.7f;

    [Tooltip("Sideways speed (u/s) of a sidestep around an enemy coming down the board at it.")]
    public float dodgeSpeed = 4f;

    [Tooltip("How far ahead (seconds of scroll) it watches the board for something to sidestep.")]
    public float lookAheadSeconds = .45f;

    // How it hunts (EnemyBehaviours; each world's chaser has its own):
    //   Hound   steady pursuit
    //   Lancer  stops to aim (AimSeconds), then dashes along the line it
    //           locked (DashSeconds), again and again
    //   Weaver  pursues on a sideways weave
    //   Burner  the same pursuit, tuned short and hard (its numbers)
    public ChaserStyle style = ChaserStyle.Hound;
    public const float LancerAimSeconds = .55f, LancerDashSeconds = .7f;
    public const float LancerAimSpeed = .15f, LancerDashSpeed = 1.9f;
    public const float WeaveSpeed = 1.5f, WeaveHz = .8f;

    // It does not stay for ever: after lingerSeconds of orbiting (or when a
    // boss / portal is coming) it leaves, climbing out the top.
    public float lingerSeconds = 5f;
    public const float LeaveSpeed = 4.5f, LeaveMargin = 1.2f;
    public bool Leaving { get; private set; }
    public float SecondsAlive { get; private set; }
    float lingered, leaveTime;

    // Chasers in play (the spawner caps them: EnemyDensity.MaxChasers).
    static readonly System.Collections.Generic.List<ChaserEnemy> alive = new System.Collections.Generic.List<ChaserEnemy>(8);
    public static int Alive
    {
        get
        {
            for (int i = alive.Count - 1; i >= 0; i--)
                if (alive[i] == null || !alive[i].enabled) alive.RemoveAt(i);
            return alive.Count;
        }
    }

    float styleClock;
    Vector3 lockedHeading = Vector3.up;

    // True while a Lancer is stopped, aiming its next dash.
    public bool Aiming => style == ChaserStyle.Lancer && !wandering && styleClock < LancerAimSeconds;

    public void Configure(EnemyBehaviour b)
    {
        if (b == null) return;
        style = b.chaser;
        chaseSeconds = b.chaseSeconds;
        chaseSpeed = b.chaseSpeed;
        startChaseSpeed = b.chaseStart;
        wanderSpeed = b.wanderSpeed;
        wanderRadius = b.wanderRadius;
        lingerSeconds = b.lingerSeconds;
        if (!alive.Contains(this)) alive.Add(this);
    }

    Transform player;
    float chaseTimer;
    float wanderAngle;
    Vector3 wanderCenter;
    bool wandering;
    bool initialised;
    SpawnFootprint footprint;

    // True while it is still closing in (EnemyFlipbook loops its lunge then).
    public bool IsChasing => !wandering && !Leaving;

    // What it hunts (the ship; a headless simulation can set a stand-in).
    public Transform Target { get { return player; } set { player = value; } }

    void Start()
    {
        Init();
    }

    void Init()
    {
        if (initialised) return;
        initialised = true;
        if (!alive.Contains(this)) alive.Add(this);
        chaseTimer = chaseSeconds;
        wanderAngle = Random.value * Mathf.PI * 2f;
        if (player == null)
        {
            var mover = Object.FindFirstObjectByType<movePlayer>();
            if (mover != null) player = mover.transform;
        }
    }

    void LateUpdate()
    {
        bool flying = !buttonClicks.playerDied &&
                      (TouchInput.IsPressed || score.pauseCounter <= 0);
        if (!flying) return;
        Step(Time.deltaTime);

        // A safety net, not the normal exit: colliding with the player or
        // the ultimate destroying it are the expected ways this goes away.
        var cam = Camera.main;
        if (cam != null && cam.orthographic &&
            transform.position.y > cam.transform.position.y + cam.orthographicSize + 6f)
            Destroy(gameObject);
    }

    // One frame (dt explicit for headless simulations).
    public void Step(float dt)
    {
        Init();
        Vector3 from = transform.position;
        Vector3 wish = from;
        SecondsAlive += dt;

        if (!Leaving && (PilotAirspace.MustClear || (wandering && (lingered += dt) >= lingerSeconds))) Leaving = true;
        if (Leaving)
        {
            // eases off, then climbs out of the view
            leaveTime += dt;
            wish += Vector3.up * LeaveSpeed * Mathf.Clamp01(.25f + leaveTime * 1.5f) * dt;
            if (from.y > CameraFit.ViewTop + LeaveMargin)
            {
                if (Application.isPlaying) Destroy(gameObject);
                else { transform.position = new Vector3(from.x, 60f, from.z); enabled = false; }
                return;
            }
        }
        else if (!wandering)
        {
            chaseTimer -= dt;
            if (player != null)
            {
                float k = 1f - Mathf.Clamp01(chaseTimer / Mathf.Max(0.01f, chaseSeconds));
                float speed = Mathf.Lerp(startChaseSpeed, chaseSpeed, k);
                // a Flare Decoy (secret power) draws the chase off the ship
                Vector3 goal = ShipDecoy.Active ? ShipDecoy.Position : player.position;
                Vector3 toPlayer = goal - from;
                toPlayer.z = 0f;
                if (toPlayer.sqrMagnitude > 0.0001f)
                {
                    Vector3 heading = toPlayer.normalized;
                    styleClock += dt;
                    if (style == ChaserStyle.Lancer)
                    {
                        // aim (nearly still, the heading follows), then a
                        // straight dash along the heading it had locked
                        float cycle = LancerAimSeconds + LancerDashSeconds;
                        if (styleClock >= cycle) styleClock -= cycle;
                        if (styleClock < LancerAimSeconds) { lockedHeading = heading; speed *= LancerAimSpeed; }
                        else { heading = lockedHeading; speed *= LancerDashSpeed; }
                    }
                    wish += heading * speed * dt;
                    if (style == ChaserStyle.Weaver)
                        wish.x += Mathf.Cos(styleClock * WeaveHz * 2f * Mathf.PI) * WeaveSpeed * dt;
                }
            }
            else
            {
                // No player to chase (e.g. it just died) -- keep drifting up
                // rather than stalling in place.
                wish += Vector3.up * startChaseSpeed * dt;
            }

            if (chaseTimer <= 0f)
            {
                wandering = true;
                wanderCenter = from;
            }
        }
        else
        {
            wanderAngle += dt * 1.4f;
            Vector3 target = wanderCenter + new Vector3(Mathf.Cos(wanderAngle), Mathf.Sin(wanderAngle) * 0.6f, 0f) * wanderRadius;
            wish = Vector3.MoveTowards(from, target, wanderSpeed * dt);
        }

        if (footprint == null) TryGetComponent(out footprint);
        if (footprint == null || !footprint.isActiveAndEnabled)
        {
            transform.position = wish;
            return;
        }

        // Something coming down the board at it: start the sidestep early,
        // away from the threat (and off the wall).
        float scroll = SpawnSpace.ScrollSpeed;
        Rect threat;
        if (SpawnSpace.ThreatAhead(footprint, wish, footprint.half, scroll * lookAheadSeconds, out threat))
        {
            float dir = wish.x >= threat.center.x ? 1f : -1f;
            if (Mathf.Abs(wish.x + dir * (footprint.half.x + .3f)) > SpawnLane.LaneHalf) dir = -dir;
            wish.x += dir * dodgeSpeed * dt;
        }

        Vector2 p = SpawnSpace.ResolveSteer(footprint, from, wish, scroll * dt, dodgeSpeed * dt);
        // a wandering loop follows wherever it had to give way to
        if (wandering) wanderCenter += new Vector3(p.x - wish.x, p.y - wish.y, 0f);
        transform.position = new Vector3(p.x, p.y, from.z);
    }

    // IMovementFootprint: where it may be over the next moments -- it rises
    // through the board at the scroll speed (it holds its place in the
    // world) plus its own speed, and may sidestep. SpawnSpace only reserves
    // this up to SteerHorizon; past that it gets out of the way itself.
    public Rect SweptBounds(Vector2 center, Vector2 half, float from, float to)
    {
        float own = Mathf.Max(chaseSpeed, wanderSpeed) * to;
        float side = Mathf.Min(1f, (Mathf.Max(chaseSpeed, wanderSpeed) + dodgeSpeed) * to);
        return Rect.MinMaxRect(center.x - half.x - side, center.y - half.y - own,
                               center.x + half.x + side, center.y + half.y + own + SpawnSpace.ScrollSpeed * to);
    }

    public bool SelfSteering => true;
}
