using UnityEngine;

/*
    Spawns enemies at different intervals onto the board.

    Previously every enemy type spawned in every phase -- only the prefab art
    swapped as speed rose, so the *mix* never changed. Phases now describe which
    enemy types are in play and how often, so each phase introduces something new.

    Leave `phases` empty in the Inspector to use the defaults built in Start().
 */

public class enmiesOnBoard : MonoBehaviour {

    [System.Serializable]
    public class SpawnPhase
    {
        public string name = "Phase";

        [Tooltip("This phase activates once this many seconds of active flight have " +
                 "passed in the current level (elapsedFlightSeconds * phaseRampScale). " +
                 "The last phase is the catch-all for everything after.")]
        public float activeAfterSeconds = 0f;

        [Header("Enemy types in play")]
        public bool rails = true;
        public bool mines;
        [Tooltip("Enters from below the board and closes in on the player for a few " +
                 "seconds before settling into a passive drift. See ChaserEnemy.")]
        public bool chasers;
        [Tooltip("Extra enemy ships (and some rocks) drawn from the current world's " +
                 "EnemyRoster. Lets later phases field hardware the early ones never see.")]
        public bool extraEnemies;
        public bool bigEnemy;
        public bool smallEnemy;
        public bool smallAstroid;
        public bool midAstroid;
        public bool bigAstroid;
        public bool aliens;

        [Header("Seconds between spawns (min, max)")]
        public Vector2 railInterval = new Vector2(0.6f, 1.0f);
        public Vector2 mineInterval = new Vector2(6f, 10f);
        public Vector2 chaserInterval = new Vector2(7f, 11f);
        public Vector2 enemyInterval = new Vector2(2.5f, 5f);
        [Tooltip("The armoured heavy's own timer: it holds a column and attacks, so it comes less often than the rocks.")]
        public Vector2 heavyInterval = new Vector2(6f, 9f);
        public Vector2 astroidInterval = new Vector2(2.5f, 5f);
        public Vector2 alienInterval = new Vector2(2.5f, 4f);
        public Vector2 extraInterval = new Vector2(2.5f, 4.5f);
    }

    // Which enemies spawn comes from EnemyRoster: each world (Space, Frost,
    // Verdant, Ember) has its own cast filling the same roles, picked from
    // the *current* world at every spawn, so a portal switches the set and the
    // tutorial (no WorldManager) gets Space. The old per-phase asteroid
    // prefab arrays (astroid1-5) were retired with their aestroid_* art,
    // and the old alien1 invader prefab with its art: aliens always come
    // from the current world's roster (no art, no alien line).

    [Tooltip("Left empty (the default), the current world's rail mine is built from EnemyRoster.")]
    public GameObject mine;
    [Tooltip("Left empty (the default), the current world's chaser is built from EnemyRoster; " +
             "a prefab here overrides it (ChaserEnemy is added at spawn).")]
    public GameObject chaser;

    [Tooltip("Left empty (the default), the extras are the current world's tiered fighters " +
             "from EnemyRoster; prefabs here override that.")]
    public GameObject[] extraEnemyPrefabs;

    [Tooltip("Multiplies elapsed flight time before checking phase thresholds -- set " +
             "per world by WorldManager so later planets escalate through the phases " +
             "faster than earlier ones, independent of the speed cap.")]
    public float phaseRampScale = 1f;

    public SpawnPhase[] phases;

    private float railDelayTimer;
    private float smEnmDelayTimer;
    private float bigEnmDelayTimer;
    private float smallAstroidDelayTimer;
    private float midAstroidDelayTimer;
    private float bigAstroidDelayTimer;
    private float spawnAnimatedEnimeOneDelayTimer;
    private float extraEnemyDelayTimer;
    private float mineDelayTimer;
    private float chaserDelayTimer;

    // Drives SelectPhase() -- only accumulates while actually flying, so a
    // level's difficulty escalation can't be dodged by never letting go of
    // the (finger-down) touch, and continues climbing even after
    // moveBackGround.speed has hit its per-world cap, unlike the old speed-
    // keyed phases, which flattened out completely once speed stopped rising.
    private float elapsedFlightSeconds;

    private int astroidSelector; // level of the game
    private SpawnPhase phase;

    // Rail mines attach to these moving lane transforms so the visible wall,
    // mine art and scrolling behavior remain one coherent hazard.
    readonly System.Collections.Generic.List<Transform> liveRails =
        new System.Collections.Generic.List<Transform>();
    readonly System.Collections.Generic.List<Transform> liveMines =
        new System.Collections.Generic.List<Transform>();

    // A new world begins with a short, scenic fly-in. A ship that already
    // launches at SPEED 10+ skips that beat and reaches the encounter pace.
    //
    // The window belongs to each WORLD ARRIVAL, not to the scene: it starts
    // again when a portal brings the ship to the next world (or back round
    // on a loop), unless the ship arrives at SPEED 10+. Nothing spawns in it
    // -- hazards, pilots, chasers, the deferred queue -- and when it ends the
    // slots come in one after another (StaggerAfterCalm), never as a burst.
    public const int FastArrivalHudSpeed = 10;
    public const float CalmArrivalSeconds = 8f;
    bool openingEncounterPrimed;
    float calmEndsAt;          // elapsedFlightSeconds when this arrival's window ends
    int arrivalKey = -1;       // world + loop the current window belongs to

    public bool InCalmWindow => !openingEncounterPrimed;
    public int CalmWindows { get; private set; }   // windows begun (tests)

    static int ArrivalKey()
    {
        return EnemyRoster.CurrentWorld + 16 * RunLoop.Index;
    }

    // A new world (a portal, a loop): the calm arrival again, unless the
    // ship is already fast. Whatever was queued for the old world is dropped.
    void BeginArrival()
    {
        arrivalKey = ArrivalKey();
        deferredCount = 0;
        if (Mathf.RoundToInt(moveBackGround.speed * 100f) >= FastArrivalHudSpeed) { PrimeOpeningEncounter(); return; }
        openingEncounterPrimed = false;
        calmEndsAt = elapsedFlightSeconds + CalmArrivalSeconds;
        CalmWindows++;
    }

    // The least each slot waits after a calm window ends, so the board fills
    // one enemy at a time (the first heavy and rock: PrimeOpeningEncounter).
    void StaggerAfterCalm()
    {
        railDelayTimer = Mathf.Max(railDelayTimer, .5f);
        mineDelayTimer = Mathf.Max(mineDelayTimer, 4f);
        midAstroidDelayTimer = Mathf.Max(midAstroidDelayTimer, 5f);
        spawnAnimatedEnimeOneDelayTimer = Mathf.Max(spawnAnimatedEnimeOneDelayTimer, 6f);
        smallAstroidDelayTimer = Mathf.Max(smallAstroidDelayTimer, 7f);
        extraEnemyDelayTimer = Mathf.Max(extraEnemyDelayTimer, 8f);
        bigAstroidDelayTimer = Mathf.Max(bigAstroidDelayTimer, 9f);
        chaserDelayTimer = Mathf.Max(chaserDelayTimer, 10f);
    }

    void Start () {
        // rock sizes come from their own stream, seeded off this one without
        // consuming it (HazardSize.Seed): the spawner's draws are as they were
        HazardSize.Seed();

        if (phases == null || phases.Length == 0)
            phases = DefaultPhases();

        // The extras, chaser and mine come from EnemyRoster per world (see
        // ChooseExtraDef / spawnChaser / spawnMine); the fields stay as
        // inspector overrides. The retired mine.prefab no longer exists.
        if (mine == null) mine = Resources.Load<GameObject>("Prefabs/mine");

        phase = phases[0];
        astroidSelector = 0;
        elapsedFlightSeconds = 0f;

        // staggered so the board does not fill up the instant the run starts
        railDelayTimer = 3f;
        bigEnmDelayTimer = 3f;
        smEnmDelayTimer = 8f;
        midAstroidDelayTimer = 13f;
        smallAstroidDelayTimer = 18f;
        bigAstroidDelayTimer = 21f;
        spawnAnimatedEnimeOneDelayTimer = 6f;
        extraEnemyDelayTimer = 24f;
        mineDelayTimer = 10f;
        chaserDelayTimer = 20f;

        arrivalKey = ArrivalKey();
        calmEndsAt = CalmArrivalSeconds;
        CalmWindows = 1;
        if (ShipStartSpeed.EquippedHud() >= FastArrivalHudSpeed)
            PrimeOpeningEncounter();
    }

    // Escalating mix: each phase adds a type rather than just reskinning.
    //
    // Thresholds and intervals retuned 2026-09-07: reported as feeling way
    // too sparse on Space specifically as the run speeds up. Two compounding
    // causes -- Space's speedRampPerSecond/maxSpeed (0.00115/0.46) mean speed
    // never actually reaches its cap within a single 300s level, so the
    // player's sense of "things speeding up" builds continuously from second
    // one; but Space also has the lowest enemyRampScale (1.00, the baseline
    // every other world ramps faster than), so at the old thresholds it did
    // not reach the denser phases until 150-220s in -- half to three-quarters
    // of the entire level -- leaving speed and density badly out of step for
    // most of a run. Thresholds are now compressed (Chaos at 130s instead of
    // 220s) so density ramps in step with speed instead of trailing it, and
    // every phase's spawn intervals are tightened on top of that so the
    // board reads as busier at every stage, not just once Chaos hits. Still
    // tuned against a 300s (5 minute) level at phaseRampScale 1 (Space);
    // every other world reaches Chaos sooner still.
    //
    // 2026-10-04: worlds are a distance now (~120s at a stock start), and the
    // phases run on the world's level clock (PhaseClockSeconds), so Chaos at
    // 130s never came before Space's boss. It starts at 105s of the 120s
    // level: the last ~1/8 of Space, earlier in each later world
    // (enemyRampScale; Ember ~78s) and earlier again on loops -- always
    // before the boss, at any start speed.
    public const float ChaosStartSeconds = 105f;

    public static SpawnPhase[] DefaultPhases()
    {
        return new[]
        {
            new SpawnPhase {
                name = "Warm-up", activeAfterSeconds = 0f,
                // The first threat arrives after the quiet fly-in (unless a
                // fast ship starts the run at encounter pace).
                rails = true, mines = true, bigEnemy = true, smallEnemy = true,
                railInterval = new Vector2(0.6f, 0.9f),
                mineInterval = new Vector2(7f, 10f),
                enemyInterval = new Vector2(2.4f, 3.4f),
                heavyInterval = new Vector2(4.5f, 6.5f),
            },
            new SpawnPhase {
                name = "Debris", activeAfterSeconds = 20f,
                rails = true, mines = true, bigEnemy = true, smallEnemy = true, midAstroid = true,
                railInterval = new Vector2(0.55f, 0.85f),
                mineInterval = new Vector2(6f, 9f),
                enemyInterval = new Vector2(1.8f, 3f),
                heavyInterval = new Vector2(4.5f, 6.5f),
                astroidInterval = new Vector2(2.2f, 3.5f),
            },
            new SpawnPhase {
                name = "Asteroid field", extraEnemies = true, activeAfterSeconds = 45f,
                rails = true, mines = true, bigEnemy = true, smallEnemy = true,
                midAstroid = true, smallAstroid = true, aliens = true,
                railInterval = new Vector2(0.5f, 0.8f),
                mineInterval = new Vector2(5f, 8f),
                enemyInterval = new Vector2(1.4f, 2.4f),
                heavyInterval = new Vector2(5f, 7.5f),
                astroidInterval = new Vector2(1.5f, 2.8f),
                alienInterval = new Vector2(2.2f, 3.5f),
            },
            new SpawnPhase {
                name = "Swarm", extraEnemies = true, activeAfterSeconds = 80f,
                rails = true, mines = true, chasers = true, bigEnemy = true, smallEnemy = true,
                midAstroid = true, smallAstroid = true, bigAstroid = true, aliens = true,
                railInterval = new Vector2(0.4f, 0.7f),
                mineInterval = new Vector2(4f, 7f),
                chaserInterval = new Vector2(7f, 10f),
                enemyInterval = new Vector2(0.9f, 1.8f),
                heavyInterval = new Vector2(4.5f, 7f),
                astroidInterval = new Vector2(1.1f, 2.2f),
                alienInterval = new Vector2(1.5f, 2.7f),
            },
            new SpawnPhase {
                name = "Chaos", extraEnemies = true, activeAfterSeconds = ChaosStartSeconds,
                rails = true, mines = true, chasers = true, bigEnemy = true, smallEnemy = true,
                midAstroid = true, smallAstroid = true, bigAstroid = true, aliens = true,
                railInterval = new Vector2(0.4f, 0.65f),
                mineInterval = new Vector2(3.5f, 6f),
                chaserInterval = new Vector2(5f, 8f),
                enemyInterval = new Vector2(0.5f, 1.1f),
                heavyInterval = new Vector2(4f, 6f),
                astroidInterval = new Vector2(0.6f, 1.4f),
                alienInterval = new Vector2(1.2f, 2f),
            },
        };
    }

    void Update () {
        bool flying = !buttonClicks.playerDied &&
                      (TouchInput.IsPressed || score.pauseCounter <= 0);
        if (flying) elapsedFlightSeconds += Time.deltaTime;

        SelectPhase();

        // A boss encounter clears the board and suspends normal spawning.
        if (flying && !BossEncounter.SuspendsSpawning)
        {
            StepCalmArrival();
            if (openingEncounterPrimed) spawn();
        }
    }

    // The clock the phases run on. In gameS1 it is the world's level clock
    // (WorldManager.LevelClockSeconds): 0 on arrival, 120 at the boss, so
    // every world -- and every visit -- goes through the phases again, a
    // faster start just as a stock one, proportionally sooner. Without a
    // WorldManager (tutorial, headless tests) it is the seconds flown.
    float PhaseClockSeconds()
    {
        var world = WorldManager.Instance;
        return world != null && world.HasLevelClock ? world.LevelClockSeconds : elapsedFlightSeconds;
    }

    void SelectPhase()
    {
        float effectiveTime = PhaseClockSeconds() * Mathf.Max(0.01f, phaseRampScale);
        // Searched from the end: elapsed time only ever grows, so the
        // correct phase is the *latest* one whose threshold has been
        // reached, not the first (ascending-search made sense for the old
        // speed thresholds, which could sit still or even dip; time never
        // does).
        for (int i = phases.Length - 1; i >= 0; i--)
        {
            if (i == 0 || effectiveTime >= phases[i].activeAfterSeconds)
            {
                phase = phases[i];
                // the phase index, capped at the fifth phase as the old
                // five-slot prefab arrays capped it (ChooseExtraDef reads it)
                astroidSelector = Mathf.Clamp(i, 0, 4);
                return;
            }
        }
    }

    // ---- placement (SpawnSpace) -------------------------------------------
    //
    // Every enemy goes through SpawnSpace before it is built: its footprint
    // -- its body plus the band its movement pattern sweeps (a rock's weave)
    // -- must stay clear of every live enemy's, and of every pickup's when
    // there is a choice. A spawn that finds no clear spot (another x or
    // weave, or a short lift above the spawn line, still off screen) is
    // deferred and retried every frame for up to MaxDeferSeconds instead of
    // landing on top of something. The board scrolls past fast enough that
    // nearly all of them land within a few frames, so density stays where
    // the timers put it (SpawnSpaceTest measures it).

    public enum SlotKind { Rock, Big, Alien, Extra, Mine, Chaser }

    // (sizeDraw: a rock's size draw, kept while it waits; HazardSize)
    struct Deferred { public SlotKind kind; public float age; public float x; public float sizeDraw; }

    public const int MaxDeferred = 24;
    public const float MaxDeferSeconds = 1f;
    const int PlaceTries = 6;
    // Beside a pilot's column a hazard takes the widest of these shares of
    // its lateral band that fits a free stretch of lane (two tries each).
    static readonly float[] RoutedBandScales = { 1f, .45f, 0f };
    const int LiftSteps = 4;
    const float LiftStep = .5f;
    const int MineLiftSteps = 8;
    const int ChaserDropSteps = 3;   // stays above the Destroyer (BelowCameraDestroyer)

    readonly Deferred[] deferred = new Deferred[MaxDeferred];
    int deferredCount;

    // Running totals (tests, tuning): enemies built, spawns that had to
    // wait, and the few that waited too long and were let go.
    public int SpawnedCount { get; private set; }
    public int DeferredTotal { get; private set; }
    public int DroppedTotal { get; private set; }
    public int PendingCount => deferredCount;

    readonly WeavePlan weaveCandidate = new WeavePlan();
    // a roster enemy's behaviour envelope, as a spawn candidate's pattern
    readonly EnemyBrainPlan brainCandidate = new EnemyBrainPlan();

    // Spawns skipped because the board already held its fill of threats
    // (EnemyDensity.MaxThreats): not queued, the timer simply comes round again.
    public int SkippedForThreats { get; private set; }
    // Pilots not admitted because the airspace was full (PilotAirspace).
    public int SkippedForPilots { get; private set; }

    // `x`: where it would like to be (NaN: anywhere in its lane).
    void Spawn(SlotKind kind, float x = float.NaN)
    {
        if (!EnemyDensity.RoomFor(EnemyDensity.Hud)) { SkippedForThreats++; return; }
        sizeDraw = float.NaN;
        if (!TrySpawn(kind, x)) Defer(kind, x);
    }

    // The size draw of the rock being tried (HazardSize.Draw): a spawn that
    // has to wait keeps it, so waiting never trades a big rock for a small one.
    float sizeDraw = float.NaN;

    void Defer(SlotKind kind, float x)
    {
        DeferredTotal++;
        if (deferredCount >= MaxDeferred) { DroppedTotal++; return; }
        deferred[deferredCount++] = new Deferred { kind = kind, age = 0f, x = x, sizeDraw = sizeDraw };
    }

    void RetryDeferred(float dt)
    {
        for (int i = 0; i < deferredCount; )
        {
            deferred[i].age += dt;
            // While a portal is kept waiting (PortalPressure.Active) the
            // threat ceiling holds for a deferred spawn too: with the board
            // full it waits (and lapses like any other). Without this the
            // backlog the pressure's spawn rate builds landed all at once,
            // far past the ceiling and the body cap. (Outside the wait the
            // board is left exactly as it was tuned: there a deferred spawn
            // only overshoots by one or two.)
            sizeDraw = deferred[i].sizeDraw;
            bool done = (!PortalPressure.Active || EnemyDensity.RoomFor(EnemyDensity.Hud)) &&
                        TrySpawn(deferred[i].kind, deferred[i].x);
            deferred[i].sizeDraw = sizeDraw;
            if (!done && deferred[i].age < MaxDeferSeconds) { i++; continue; }
            if (!done) DroppedTotal++;
            deferred[i] = deferred[--deferredCount];
        }
    }

    // false = no clear spot right now (defer); true = built, or nothing to
    // build (missing roster art -- EnemyRosterTest guards it).
    bool TrySpawn(SlotKind kind, float x = float.NaN)
    {
        int world = EnemyRoster.CurrentWorld;
        switch (kind)
        {
            // "small enemy" and the three asteroid slots: one of the world's rocks
            case SlotKind.Rock: return TrySpawnDef(EnemyRoster.Pick(world, EnemyRole.Rock), float.NaN);
            // "big enemy" slot: the world's armoured heavy. At ~1.1 u it keeps
            // to the middle of the lane (SpawnLane.HeavyMaxX), clear of the
            // walls and the rail mines.
            case SlotKind.Big:
                return TrySpawnDef(EnemyRoster.Pick(world, EnemyRole.Big), Random.Range(-SpawnLane.HeavyMaxX, SpawnLane.HeavyMaxX));
            case SlotKind.Alien: return TrySpawnDef(EnemyRoster.One(world, EnemyRole.Alien), x);
            case SlotKind.Extra: return TrySpawnExtra();
            case SlotKind.Mine: return TrySpawnMine();
            case SlotKind.Chaser: return TrySpawnChaser();
        }
        return true;
    }

    static bool Weaves(EnemyDef def)
    {
        // EnemyFactory gives rocks and aliens the weaving mover (moveEnimes);
        // with a behaviour (EnemyBehaviours) it only scrolls and the brain
        // moves them, so only an entry without one still weaves
        return (def.role == EnemyRole.Rock || def.role == EnemyRole.Alien) && def.Behaviour == null;
    }

    // A clear spot on (or just above) the spawn line for a body of `half`:
    // a weaver tries weave amplitudes (its weave, not its spawn x, decides
    // where it flies), anything else x's near preferredX within +/-maxX.
    // laneDef, when set, must also leave its row a ship-width gap
    // (SpawnLane). The first pass also keeps clear of pickups; the second
    // only of enemies.
    bool TryPlace(Vector2 half, bool weaves, float preferredX, float maxX, EnemyDef laneDef,
                  out Vector3 pos, out float amplitude, IMovementFootprint plan = null, float size = 1f)
    {
        // anywhere in its lane -- but hazards are routed round the pilots: with
        // any on station, somewhere in a stretch of lane none of them holds
        // (and where the gaps are narrow it keeps a narrower band: RoutedBandScales)
        var brainPlan = plan as EnemyBrainPlan;
        float fullBand = brainPlan != null ? brainPlan.Band : 0f;
        float fullMaxX = maxX;
        if (brainPlan != null) brainPlan.bandScale = 1f;
        bool routed = !weaves && PilotAirspace.Count > 0;
        if (!routed && float.IsNaN(preferredX)) preferredX = Random.Range(-maxX, maxX);
        float clock = SpawnSpace.Clock;
        float baseY = transform.position.y;
        int passes = SpawnSpace.Live(SpawnLayer.Pickup).Count > 0 ? 2 : 1;
        for (int pass = 0; pass < passes; pass++)
            for (int lift = 0; lift < LiftSteps; lift++)
            {
                float y = baseY + lift * LiftStep;
                for (int t = 0; t < PlaceTries; t++)
                {
                    float x;
                    amplitude = 0f;
                    if (weaves)
                    {
                        amplitude = WeavePlan.Safe(Random.Range(moveEnimes.MinAmplitude, moveEnimes.MaxAmplitude));
                        x = WeavePlan.X(amplitude, clock);
                        weaveCandidate.amplitude = amplitude;
                    }
                    else if (!routed) x = t == 0 ? Mathf.Clamp(preferredX, -maxX, maxX) : Random.Range(-maxX, maxX);
                    else
                    {
                        float scale = RoutedBandScales[Mathf.Min(t / 2, RoutedBandScales.Length - 1)];
                        if (brainPlan != null) brainPlan.bandScale = scale;
                        maxX = fullMaxX + fullBand * (1f - scale);   // a narrower band may sit nearer the rails
                        if (!PilotAirspace.TryFreeX(half.x + fullBand * scale, maxX, out x)) continue;
                    }
                    var c = new SpawnCandidate(new Vector2(x, y), half, weaves ? weaveCandidate : plan);
                    if (!SpawnSpace.Fits(c)) continue;
                    // hazards are routed round the pilots: never down a reserved column
                    Rect reach = c.Sweep(0f, SpawnSpace.Lifetime);
                    if (PilotAirspace.Blocks(reach.xMin, reach.xMax)) continue;
                    if (pass == 0 && passes > 1 && !SpawnSpace.Fits(c, SpawnLayer.Pickup)) continue;
                    if (laneDef != null && !SpawnLane.Fits(laneDef, x, y, size)) continue;
                    pos = new Vector3(x, y, 0f);
                    return true;
                }
            }
        pos = Vector3.zero;
        amplitude = 0f;
        return false;
    }

    // The current world's enemy for a role (nothing spawns if its roster
    // art is missing; EnemyRosterTest guards the art).
    bool TrySpawnDef(EnemyDef def, float preferredX)
    {
        if (def == null || EnemyArt.Frames(def) == null) return true;
        bool weaves = Weaves(def);
        Vector3 pos;
        float amplitude;
        var behaviour = def.Behaviour;
        // A pilot flies itself: it needs a free column (PilotAirspace), not
        // a spot on the board, and waits above the view until it may come in.
        if (EnemyBrain.PilotsEnabled && behaviour != null && behaviour.IsPilot && def.role != EnemyRole.Chaser)
        {
            int wing = def.role == EnemyRole.Fighter && def.tier == 1 ? 2 : 1;   // scouts fly in pairs when there is room
            for (int k = 0; k < wing; k++)
            {
                float stationX;
                if (!PilotAirspace.TryAdmit(def, behaviour, k == 0 ? preferredX : float.NaN, out stationX))
                {
                    if (k == 0) SkippedForPilots++;
                    break;
                }
                EnemyFactory.Create(def, new Vector3(stationX, transform.position.y + PilotAirspace.WaitAbove, 0f), transform.rotation);
                SpawnedCount++;
            }
            return true;
        }
        // its whole pattern (the behaviour's envelope) must fit, inside the
        // lane, at the size it is drawn at (HazardSize: rocks vary)
        float size = HazardSize.Draw(def, ref sizeDraw);
        brainCandidate.behaviour = behaviour;
        brainCandidate.reach = HazardSize.Varies(def) ? HazardSize.Reach(size) : 1f;
        float maxX = Mathf.Max(0f, SpawnLane.MaxX(def, size) - brainCandidate.Band);
        if (!TryPlace(SpawnSpace.BodyHalf(def, size), weaves, preferredX, maxX, def, out pos, out amplitude,
                      behaviour != null ? brainCandidate : null, size))
            return false;
        var go = EnemyFactory.Create(def, pos, transform.rotation, size);
        sizeDraw = float.NaN;
        if (behaviour != null && brainCandidate.bandScale < 1f)
        {
            EnemyBrain squeezed;
            if (go.TryGetComponent(out squeezed)) squeezed.SetBandScale(brainCandidate.bandScale);
        }
        if (weaves)
        {
            var mover = go.GetComponent<moveEnimes>();
            if (mover != null) mover.SetWeave(amplitude);
        }
        SpawnedCount++;
        return true;
    }

    // An inspector prefab override (extraEnemyPrefabs): same placement,
    // footprint from its collider.
    bool TrySpawnPrefab(GameObject prefab, float preferredX)
    {
        var weaver = prefab.GetComponent<moveEnimes>();
        Vector2 half = SpawnSpace.BodyHalf(prefab);
        Vector3 pos;
        float amplitude;
        if (!TryPlace(half, weaver != null, preferredX, SpawnLane.LaneHalf - half.x, null, out pos, out amplitude))
            return false;
        var go = Instantiate(prefab, pos, transform.rotation);
        var mover = go.GetComponent<moveEnimes>();
        if (mover != null) mover.SetWeave(amplitude);
        SpawnFootprint.Attach(go, half);
        SpawnFootprint.Bind(go, go.GetComponent<IMovementFootprint>());
        SpawnedCount++;
        return true;
    }

    Transform NearestLiveRail(bool right)
    {
        for (int i = liveRails.Count - 1; i >= 0; i--)
            if (liveRails[i] == null) liveRails.RemoveAt(i);
        if (liveRails.Count == 0) return null;

        Transform best = null;
        float bestDistance = float.MaxValue;
        for (int i = 0; i < liveRails.Count; i++)
        {
            bool railIsRight = liveRails[i].position.x > 0f;
            if (railIsRight != right) continue;
            float distance = Mathf.Abs(liveRails[i].position.y - transform.position.y);
            if (distance < bestDistance)
            {
                best = liveRails[i];
                bestDistance = distance;
            }
        }
        return best;
    }

    Transform SpawnRail(bool right)
    {
        Vector3 pos = new Vector3(WorldRailX(!right), transform.position.y, 0f);
        GameObject spawned = new GameObject("RailMineLane");
        spawned.transform.position = pos;
        spawned.AddComponent<RailLaneScroller>();
        liveRails.Add(spawned.transform);
        return spawned.transform;
    }

    // Where a rail mine's centre rides: clamped to the DRAWN rail. The wall
    // quad is no guide (it carries the texture's transparent canvas well into
    // the lane), so the x comes from the rail art's visible inner edge
    // (WorldPainter.VisibleRailEdges) and the mine art's own clamp
    // (RailMineArt.MountX); a wall without the rail art keeps the authored
    // lane edge. Never outside what the camera shows.
    public static float WorldRailX(bool left)
    {
        GameObject wall = GameObject.Find(left ? "leftPipe" : "rightPipe");
        float wallX = wall != null ? wall.transform.position.x : (left ? -3.15f : 3.15f);
        float inner, outer;
        float railX = wall != null && WorldPainter.VisibleRailEdges(wall, out inner, out outer)
            ? RailMineArt.MountX(inner) : RailMineArt.FallbackRailX;
        var cam = Camera.main;
        float visibleLimit = cam != null && cam.orthographic
            ? cam.orthographicSize * cam.aspect - .30f : railX;
        float safeLimit = Mathf.Max(.65f, Mathf.Min(railX, visibleLimit));
        return Mathf.Sign(wallX == 0f ? (left ? -1f : 1f) : wallX) * safeLimit;
    }

    // Continuous spawn-rate multiplier, layered on top of the phase system
    // above (which still controls which enemy *types* are active). Steps
    // every 10 seconds of active flight, the same for every world/level:
    //   0-60s:  climbs from 1x to 2x (six 10s steps)
    //   60s-(level end minus 30s): keeps climbing, 2x toward 2.5x
    //   final 30s of the level: flat 3x, regardless of how long the level is
    // Applied by dividing rolled delays (Roll() below), so higher density
    // means shorter delays -- more spawns per minute, on every active type
    // at once, not just the ones a phase newly unlocks.
    //
    // Roll() also multiplies in LoopDifficulty.DensityScale: x1 on a first
    // pass, x1.1 / x1.2 / x1.3 on later loops (LoopRules.DensityScale;
    // WorldManager sets it), and PortalPressure.DensityScale: x1 except while
    // a portal is open and waiting, when it climbs without limit. However
    // dense, every spawn still goes through SpawnSpace (no enemy on top of
    // another), the threat ceiling (EnemyDensity.MaxThreats) and SpawnLane
    // (each row keeps a ship-width gap): a spawn with no room waits a few
    // frames for the board to scroll on (RetryDeferred).
    const float DensityTickSeconds = 10f;
    const float DensityFirstMinute = 60f;
    const float DensityFinalStretch = 30f;
    const float DensityFirstMinuteCeiling = 2f;
    const float DensityMidCeiling = 2.5f;
    const float DensityFinalMultiplier = 3f;

    float DensityMultiplier()
    {
        float levelLength = WorldManager.BaselineWorldSeconds;
        float finalStart = Mathf.Max(DensityFirstMinute, levelLength - DensityFinalStretch);

        if (elapsedFlightSeconds >= finalStart) return DensityFinalMultiplier;
        // A fast start (ShipStartSpeed) ends the world sooner: the final
        // stretch is then the last 30s of flight before the boss, however
        // early. (Only while the level is still being flown.)
        var world = WorldManager.Instance;
        if (world != null && world.DistanceLeft > 0f && world.SecondsLeftInWorld <= DensityFinalStretch)
            return DensityFinalMultiplier;

        int tick = Mathf.FloorToInt(elapsedFlightSeconds / DensityTickSeconds);

        if (elapsedFlightSeconds <= DensityFirstMinute)
        {
            int firstMinuteTicks = Mathf.RoundToInt(DensityFirstMinute / DensityTickSeconds); // 6
            return Mathf.Lerp(1f, DensityFirstMinuteCeiling, (float)tick / firstMinuteTicks);
        }

        int firstMinuteTickCount = Mathf.RoundToInt(DensityFirstMinute / DensityTickSeconds);
        int midTicks = Mathf.Max(1, Mathf.FloorToInt((finalStart - DensityFirstMinute) / DensityTickSeconds));
        int tickInMid = tick - firstMinuteTickCount;
        return Mathf.Lerp(DensityFirstMinuteCeiling, DensityMidCeiling, (float)tickInMid / midTicks);
    }

    float Roll(Vector2 range)
    {
        // ... and EnemyDensity.RateScale: fewer, smarter enemies, cut harder the faster the board scrolls
        return Random.Range(range.x, range.y) / Mathf.Max(0.1f, DensityMultiplier() * LoopDifficulty.DensityScale)
               / Mathf.Max(0.1f, EnemyDensity.RateScale(EnemyDensity.Hud) * PortalPressure.DensityScale);
    }

    // The calm window's clock (Update; tests step it): a new world starts a
    // new window, and a window that has run out primes the first encounter.
    void StepCalmArrival()
    {
        if (ArrivalKey() != arrivalKey) BeginArrival();
        if (!openingEncounterPrimed && elapsedFlightSeconds >= calmEndsAt)
        {
            StaggerAfterCalm();
            PrimeOpeningEncounter();
        }
    }

    void PrimeOpeningEncounter()
    {
        openingEncounterPrimed = true;
        // One readable enemy arrives before the ordinary cadence takes over.
        bigEnmDelayTimer = Mathf.Min(bigEnmDelayTimer, .75f);
        smEnmDelayTimer = Mathf.Min(smEnmDelayTimer, 1.5f);
    }

    void spawn() { spawn(Time.deltaTime); }

    // dt is explicit so a headless test can step a whole run: Time.deltaTime
    // is 0 outside Play mode.
    void spawn(float dt)
    {
        // spawns that found no clear spot get first go at the board
        RetryDeferred(dt);

        railDelayTimer -= dt;
        smEnmDelayTimer -= dt;
        bigEnmDelayTimer -= dt;
        smallAstroidDelayTimer -= dt;
        midAstroidDelayTimer -= dt;
        bigAstroidDelayTimer -= dt;
        spawnAnimatedEnimeOneDelayTimer -= dt;
        extraEnemyDelayTimer -= dt;
        mineDelayTimer -= dt;
        chaserDelayTimer -= dt;

        if (railDelayTimer <= 0)
        {
            if (phase.rails) spawnRails();
            railDelayTimer = Roll(phase.railInterval);
        }
        if (mineDelayTimer <= 0)
        {
            if (phase.mines) spawnMine();
            mineDelayTimer = Roll(phase.mineInterval);
        }
        if (chaserDelayTimer <= 0)
        {
            if (phase.chasers) spawnChaser();
            chaserDelayTimer = Roll(phase.chaserInterval);
        }
        if (smEnmDelayTimer <= 0)
        {
            if (phase.smallEnemy) spawnAstroid2();
            smEnmDelayTimer = Roll(phase.enemyInterval);
        }
        if (bigEnmDelayTimer <= 0)
        {
            if (phase.bigEnemy) spawnAstroid1();
            bigEnmDelayTimer = Roll(phase.heavyInterval);
        }
        if (smallAstroidDelayTimer <= 0)
        {
            if (phase.smallAstroid) spawnSmallAstroid();
            smallAstroidDelayTimer = Roll(phase.astroidInterval);
        }
        if (midAstroidDelayTimer <= 0)
        {
            if (phase.midAstroid) spawnMidAstroid();
            midAstroidDelayTimer = Roll(phase.astroidInterval);
        }
        if (bigAstroidDelayTimer <= 0)
        {
            if (phase.bigAstroid) spawnLargeAstroid();
            bigAstroidDelayTimer = Roll(phase.astroidInterval);
        }
        if (spawnAnimatedEnimeOneDelayTimer <= 0)
        {
            if (phase.aliens) spawnAnimatedEnimeOne();
            spawnAnimatedEnimeOneDelayTimer = Roll(phase.alienInterval);
        }
        if (extraEnemyDelayTimer <= 0)
        {
            if (phase.extraEnemies) spawnExtraEnemy();
            extraEnemyDelayTimer = Roll(phase.extraInterval);
        }
    }

    // "small enemy" slot: one of the world's rocks
    void spawnAstroid2()
    {
        Spawn(SlotKind.Rock);
    }

    // "big enemy" slot: the world's armoured heavy (SpawnLane.HeavyMaxX
    // keeps it mid-lane and leaves a ship-width gap beside it).
    void spawnAstroid1()
    {
        Spawn(SlotKind.Big);
    }

    // will create a line of animated enimies that the player is able to doge through
    // (Each alien weaves on its own amplitude from its first frame, so the
    // "line" only ever decided how many come; each one is placed -- or
    // deferred -- on its own. The old in-lane filter keeps the count.)
    void spawnAnimatedEnimeOne()
    {
        // (With behaviours the line is a real line: AlienLineSpacing apart,
        // each wiggling or marching in step inside its own narrow band.)
        float startX = Random.Range(-2.3f, 2f);
        int max = Random.Range(1, 5);
        for (int i = 0; i < max; i++)
        {
            float x = startX + (i + .5f) * AlienLineSpacing;
            if (x >= -AlienLineMaxX && x <= AlienLineMaxX) Spawn(SlotKind.Alien, x);
        }
    }

    public const float AlienLineSpacing = 1.4f, AlienLineMaxX = 1.75f;

    // Next 3 functions spawn 3 different types of astroids.
    void spawnSmallAstroid()
    {
        Spawn(SlotKind.Rock);
    }

    void spawnMidAstroid()
    {
        Spawn(SlotKind.Rock);
    }

    void spawnLargeAstroid()
    {
        Spawn(SlotKind.Rock);
    }

    // The current world's fighters, tiered so later phases meet the nastier
    // hulls (see ChooseExtraDef). Prefabs in extraEnemyPrefabs override it.
    void spawnExtraEnemy()
    {
        Spawn(SlotKind.Extra);
    }

    bool TrySpawnExtra()
    {
        float x = Random.Range(-2.2f, 2.2f);
        if (extraEnemyPrefabs != null && extraEnemyPrefabs.Length > 0)
        {
            GameObject pick = extraEnemyPrefabs[Random.Range(0, extraEnemyPrefabs.Length)];
            return pick == null || TrySpawnPrefab(pick, x);
        }
        var def = ChooseExtraDef(EnemyRoster.CurrentWorld, astroidSelector);
        if (def != null && def.role == EnemyRole.Big) x = Mathf.Clamp(x, -SpawnLane.HeavyMaxX, SpawnLane.HeavyMaxX);
        return TrySpawnDef(def, x);
    }

    // Phase index -> fighter tier window: the extras start in phase 2, which
    // fields tiers 1-2; phase 3 tiers 1-3; phase 4 tiers 2-4. From phase 2 a
    // quarter of the picks are the world's rocks or its heavy instead (where
    // the Kenney meteors used to fold in).
    // Later loops shift the window up (LoopRules.TierShift: +1, then +2), so
    // a loop meets the nastier hulls from its first fighters on.
    public static EnemyDef ChooseExtraDef(int world, int phaseIndex)
    {
        if (phaseIndex >= 2 && Random.value < .25f)
            return EnemyRoster.Pick(world, Random.value < .7f ? EnemyRole.Rock : EnemyRole.Big);
        int maxTier = Mathf.Clamp(phaseIndex + LoopRules.TierShift(RunLoop.Index), 1, 4);
        int minTier = Mathf.Max(1, maxTier - 2);
        return EnemyRoster.Fighter(world, Random.Range(minTier, maxTier + 1));
    }

    void spawnRails()
    {
        SpawnRail(Random.Range(1, 10) % 2 == 0);
    }

    // Rail mines remain hardware clamped to a moving side rail. A future
    // free-standing turret will occupy the independent stationary-hazard role.
    void spawnMine()
    {
        Spawn(SlotKind.Mine);
    }

    bool TrySpawnMine()
    {
        // The current world's themed rail mine keeps its original mounted
        // movement and arming animation.
        var def = mine == null ? EnemyRoster.One(EnemyRoster.CurrentWorld, EnemyRole.Mine) : null;
        if (mine == null && (def == null || EnemyArt.Frames(def) == null)) return true;

        bool right = Random.value < .5f;
        Transform rail = NearestLiveRail(right);
        if (rail == null) rail = SpawnRail(right);
        if (rail == null) return true;

        Vector2 half = def != null ? SpawnSpace.BodyHalf(def) : SpawnSpace.BodyHalf(mine);
        float x = rail.position.x;
        int passes = SpawnSpace.Live(SpawnLayer.Pickup).Count > 0 ? 2 : 1;
        for (int pass = 0; pass < passes; pass++)
            for (int k = 0; k < MineLiftSteps; k++)
            {
                float y = transform.position.y + k * LiftStep;
                brainCandidate.behaviour = def != null ? def.Behaviour : null;
                brainCandidate.bandScale = 1f;
                brainCandidate.reach = 1f;
                var c = new SpawnCandidate(new Vector2(x, y), half, brainCandidate.behaviour != null ? brainCandidate : null);
                if (!SpawnSpace.Fits(c)) continue;
                if (pass == 0 && passes > 1 && !SpawnSpace.Fits(c, SpawnLayer.Pickup)) continue;
                if (def != null && !SpawnLane.Fits(def, x, y)) continue;

                GameObject built;
                if (def != null)
                {
                    built = EnemyFactory.Create(def, new Vector3(x, y, 0f), Quaternion.identity);
                    built.GetComponent<SpriteRenderer>().flipX = x > 0f;
                }
                else
                {
                    built = Instantiate(mine, new Vector3(x, y, 0f), transform.rotation);
                    if (built.GetComponent<RailBombAnimator>() == null) built.AddComponent<RailBombAnimator>();
                    SpawnFootprint.Attach(built, half);
                }
                var mount = built.GetComponent<RailMineMount>();
                if (mount == null) mount = built.AddComponent<RailMineMount>();
                mount.MountTo(rail);
                mount.brain = built.GetComponent<EnemyBrain>();   // its slide's envelope (SweptBounds)
                liveMines.Add(built.transform);
                SpawnedCount++;
                return true;
            }
        return false;
    }

    // Enters from below the visible board (everything else scrolls in from
    // above) and closes in on the player before settling into a passive
    // drift -- see ChaserEnemy for the actual behaviour.
    void spawnChaser()
    {
        Spawn(SlotKind.Chaser);
    }

    bool TrySpawnChaser()
    {
        var def = chaser == null ? EnemyRoster.One(EnemyRoster.CurrentWorld, EnemyRole.Chaser) : null;
        if (chaser == null && (def == null || EnemyArt.Frames(def) == null)) return true;
        // chasers stay and hunt: only so many at once, none with a boss on the way
        if (ChaserEnemy.Alive >= EnemyDensity.MaxChasers(EnemyDensity.Hud) || PilotAirspace.AdmissionClosed)
        {
            SkippedForPilots++;
            return true;
        }

        var cam = Camera.main;
        float bottomY = cam != null && cam.orthographic
            ? cam.transform.position.y - cam.orthographicSize - 1f
            : transform.position.y - 12f;
        Vector2 half = def != null ? SpawnSpace.BodyHalf(def) : SpawnSpace.BodyHalf(chaser);

        // It steers itself from here on (SpawnSpace.ResolveSteer); it only
        // has to start clear of everything, below the board.
        int passes = SpawnSpace.Live(SpawnLayer.Pickup).Count > 0 ? 2 : 1;
        for (int pass = 0; pass < passes; pass++)
            for (int k = 0; k < ChaserDropSteps; k++)
                for (int t = 0; t < PlaceTries; t++)
                {
                    var at = new Vector2(Random.Range(-2.2f, 2.2f), bottomY - k * LiftStep);
                    var c = new SpawnCandidate(at, half);
                    if (!SpawnSpace.ClearForSteerer(null, at, half) || !SpawnSpace.Fits(c)) continue;
                    if (pass == 0 && passes > 1 && !SpawnSpace.Fits(c, SpawnLayer.Pickup)) continue;

                    Vector3 pos = new Vector3(at.x, at.y, 0f);
                    if (def != null) EnemyFactory.Create(def, pos, Quaternion.identity);
                    else
                    {
                        GameObject spawned = Instantiate(chaser, pos, Quaternion.identity);
                        // The borrowed hull's own straight-line scroller would fight
                        // ChaserEnemy for control of the transform.
                        var straightLine = spawned.GetComponent<moveItemEnmInStrightLine>();
                        if (straightLine != null) Destroy(straightLine);
                        var hunter = spawned.GetComponent<ChaserEnemy>();
                        if (hunter == null) hunter = spawned.AddComponent<ChaserEnemy>();
                        SpawnFootprint.Attach(spawned, half);
                        SpawnFootprint.Bind(spawned, hunter);
                    }
                    SpawnedCount++;
                    return true;
                }
        return false;
    }
}

// A rail mine is hardware clamped to a moving side rail, rather than an
// ordinary hazard that happens to share its X coordinate.  The mount records
// the mine's along-rail offset at spawn and reapplies the full rail-relative
// position after all ordinary Update movers have run.  That gives the mine a
// single source of travel (its own side rail), keeps its clamp seated through
// a frame of animation, and prevents a rail/mine pair from shearing apart
// when the board speed changes.
//
// For SpawnSpace it is a board-locked pattern: the rail scrolls with the
// board, so the mine's sweep is its body (MountTo binds it).
public class RailMineMount : MonoBehaviour, IMovementFootprint
{
    public Transform rail;
    public float lockedX;
    float railOffsetY;
    bool mounted;

    // How far along its rail the mine has slid from where it was clamped
    // (EnemyBrain: Patrol / Creep), and the brain whose envelope bounds it.
    [System.NonSerialized] public float Slide;
    // ... and how far a shove has slid it along the rail (EnemyShove).
    [System.NonSerialized] public float Shove;
    [System.NonSerialized] public EnemyBrain brain;

    // Kept public for the headless regression test and for quick inspection
    // while playing in the editor.
    public float AlignmentError
    {
        get { return Mathf.Abs(transform.position.x - (rail != null ? rail.position.x : lockedX)); }
    }

    public bool IsOnRail(float tolerance = 0.015f)
    {
        return AlignmentError <= tolerance;
    }

    // Use this rather than assigning rail directly for runtime spawns: it
    // captures the mine's intentional spacing from the rail's spawn point.
    // The public fields remain available for old prefabs and editor probes;
    // LateUpdate captures their offset lazily on the first frame.
    public void MountTo(Transform targetRail)
    {
        rail = targetRail;
        lockedX = targetRail != null ? targetRail.position.x : transform.position.x;
        mounted = targetRail != null;
        if (mounted) railOffsetY = transform.position.y - targetRail.position.y;

        // EnemyFactory retains the legacy straight-line mover so prefab and
        // targeting contracts stay intact.  Once mounted, it must not be the
        // mine's movement authority: this mount follows the rail instead.
        var looseScroller = GetComponent<moveItemEnmInStrightLine>();
        if (looseScroller != null) looseScroller.enabled = !mounted;
        // the rail is its mover now (a switched-off scroller would read as held)
        SpawnFootprint.Bind(gameObject, mounted ? (IMovementFootprint)this : (IMovementFootprint)looseScroller);
    }

    public Rect SweptBounds(Vector2 center, Vector2 half, float from, float to)
    {
        return EnemyBrain.Widen(brain, center, half);
    }

    public bool SelfSteering => false;

    void LateUpdate()
    {
        if (!mounted && rail != null)
        {
            mounted = true;
            lockedX = rail.position.x;
            railOffsetY = transform.position.y - rail.position.y;
            var looseScroller = GetComponent<moveItemEnmInStrightLine>();
            if (looseScroller != null) looseScroller.enabled = false;
        }

        // A lane has left the board (or was otherwise removed).  Do not let
        // its mine become a detached, invisible-wall hazard in mid-field.
        if (mounted && rail == null)
        {
            Destroy(gameObject);
            return;
        }

        if (rail != null)
            // (SnapLift: drawn on the same whole screen pixel as the rail art, under half a pixel from true)
            transform.position = new Vector3(rail.position.x, rail.position.y + railOffsetY + Slide + Shove + BoardRoll.SnapLift, transform.position.z);
        else
            transform.position = new Vector3(lockedX, transform.position.y, transform.position.z);
    }

    void OnDrawGizmosSelected()
    {
        if (rail == null) return;
        Gizmos.color = IsOnRail() ? Color.green : Color.red;
        Gizmos.DrawLine(transform.position, rail.position);
    }
}

public class RailLaneScroller : MonoBehaviour
{
    void Update()
    {
        // the same step the rail art takes this frame (BoardRoll), so a mine
        // on this lane stays registered to the art it is clamped to
        if (TouchInput.IsPressed || score.pauseCounter <= 0)
            transform.position += Vector3.down * BoardRoll.Advance(moveBackGround.speed, Time.deltaTime);
        if (transform.position.y < -12f) Destroy(gameObject);
    }
}
