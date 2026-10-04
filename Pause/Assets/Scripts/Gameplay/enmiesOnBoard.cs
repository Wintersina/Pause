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

    // A mine must be attached to a real rail, never to a magic screen x.
    // These are the transforms returned by Instantiate(), so a mine follows
    // the precise lane the rail was given for the current world.
    readonly System.Collections.Generic.List<Transform> liveRails =
        new System.Collections.Generic.List<Transform>();
    readonly System.Collections.Generic.List<Transform> liveMines =
        new System.Collections.Generic.List<Transform>();
    Transform pendingMineRail;

    void Start () {

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
    static SpawnPhase[] DefaultPhases()
    {
        return new[]
        {
            new SpawnPhase {
                name = "Warm-up", activeAfterSeconds = 0f,
                // A rail mine is introduced early and then keeps returning;
                // players should see this rail hazard before the board gets
                // crowded with later asteroid phases.
                rails = true, mines = true, bigEnemy = true, smallEnemy = true,
                railInterval = new Vector2(0.6f, 0.9f),
                mineInterval = new Vector2(7f, 10f),
                enemyInterval = new Vector2(2.4f, 3.4f),
            },
            new SpawnPhase {
                name = "Debris", activeAfterSeconds = 20f,
                rails = true, mines = true, bigEnemy = true, smallEnemy = true, midAstroid = true,
                railInterval = new Vector2(0.55f, 0.85f),
                mineInterval = new Vector2(6f, 9f),
                enemyInterval = new Vector2(1.8f, 3f),
                astroidInterval = new Vector2(2.2f, 3.5f),
            },
            new SpawnPhase {
                name = "Asteroid field", extraEnemies = true, activeAfterSeconds = 45f,
                rails = true, mines = true, bigEnemy = true, smallEnemy = true,
                midAstroid = true, smallAstroid = true, aliens = true,
                railInterval = new Vector2(0.5f, 0.8f),
                mineInterval = new Vector2(5f, 8f),
                enemyInterval = new Vector2(1.4f, 2.4f),
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
                astroidInterval = new Vector2(1.1f, 2.2f),
                alienInterval = new Vector2(1.5f, 2.7f),
            },
            new SpawnPhase {
                name = "Chaos", extraEnemies = true, activeAfterSeconds = 130f,
                rails = true, mines = true, chasers = true, bigEnemy = true, smallEnemy = true,
                midAstroid = true, smallAstroid = true, bigAstroid = true, aliens = true,
                railInterval = new Vector2(0.4f, 0.65f),
                mineInterval = new Vector2(3.5f, 6f),
                chaserInterval = new Vector2(5f, 8f),
                enemyInterval = new Vector2(0.5f, 1.1f),
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
        if (flying && !BossEncounter.SuspendsSpawning) spawn();
    }

    void SelectPhase()
    {
        float effectiveTime = elapsedFlightSeconds * Mathf.Max(0.01f, phaseRampScale);
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

    // Mines are rail hardware -- they belong in a rail lane, not at an
    // arbitrary x. Everything else spawns wherever it was asked to.
    //
    // A side is picked first and the rail search is filtered to that side --
    // NearestLiveRail() used to match on vertical distance alone, so with
    // both a left and a right rail on screen at once (spawnRails() alternates
    // sides freely) a mine could be hung on whichever rail was nearest in Y
    // regardless of which side it actually came from, landing it on the
    // wrong lane -- reported as mines inconsistently sticking to different
    // parts of the screen.
    Vector3 PlaceFor(GameObject prefab, float x)
    {
        pendingMineRail = null;
        if (PrefabName.Is(prefab, "mine"))
        {
            bool right = Random.value < 0.5f;
            Transform rail = NearestLiveRail(right);
            // A mine can be selected by the enemy table before a rail on
            // this side happens to be on screen. Create its mounting rail
            // first in that case.
            if (rail == null) rail = SpawnRail(right);
            if (rail != null)
            {
                pendingMineRail = rail;
                x = rail.position.x;
                // Hold enough vertical space for the mine's full circular
                // silhouette before it enters the visible board.
                float y = ReserveMineY(rail, transform.position.y);
                return new Vector3(x, y, 0f);
            }
        }
        return new Vector3(x, transform.position.y, 0f);
    }

    float ReserveMineY(Transform rail, float requestedY)
    {
        for (int i = liveMines.Count - 1; i >= 0; i--)
            if (liveMines[i] == null) liveMines.RemoveAt(i);

        float y = requestedY;
        bool moved;
        do
        {
            moved = false;
            for (int i = 0; i < liveMines.Count; i++)
            {
                if (Mathf.Abs(liveMines[i].position.x - rail.position.x) < 0.02f &&
                    Mathf.Abs(liveMines[i].position.y - y) < 1.18f)
                {
                    y += 1.22f;
                    moved = true;
                    break;
                }
            }
        } while (moved);
        return y;
    }

    GameObject SpawnEnemy(GameObject prefab, float x)
    {
        if (prefab == null) return null;
        GameObject spawned = Instantiate(prefab, PlaceFor(prefab, x), transform.rotation);
        if (PrefabName.Is(prefab, "mine"))
        {
            liveMines.Add(spawned.transform);
            if (spawned.GetComponent<RailBombAnimator>() == null)
                spawned.AddComponent<RailBombAnimator>();
            var mount = spawned.GetComponent<RailMineMount>();
            if (mount == null) mount = spawned.AddComponent<RailMineMount>();
            mount.MountTo(pendingMineRail);
        }
        return spawned;
    }

    // right: only rails on the positive-x side are considered a match, so a
    // mine can never end up mounted to the opposite lane from the one it was
    // meant for.
    Transform NearestLiveRail(bool right)
    {
        for (int i = liveRails.Count - 1; i >= 0; i--)
            if (liveRails[i] == null) liveRails.RemoveAt(i);
        if (liveRails.Count == 0) return null;

        // Prefer the rail closest in vertical travel to this spawn point,
        // among those on the requested side. Its x is nevertheless taken
        // directly from that rail's transform.
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
        // Invisible lane marker: the visible rail is the themed side wall.
        // This replaces the retired rail1/rail2/rail3 obstacle art while
        // preserving a moving transform for mine attachment.
        GameObject spawned = new GameObject("RailMineLane");
        spawned.transform.position = pos;
        spawned.AddComponent<RailLaneScroller>();
        liveRails.Add(spawned.transform);
        return spawned.transform;
    }

    // The side-wall meshes are the rails in every world. Their inner edges
    // move correctly with the authored geometry, regardless of texture/theme.
    // This calculation is deliberately based on those live meshes rather than
    // a hard-coded portrait-screen coordinate.
    static float WorldRailX(bool left)
    {
        GameObject wall = GameObject.Find(left ? "leftPipe" : "rightPipe");
        float wallX = 0f;
        if (wall != null)
        {
            wallX = wall.transform.position.x;
        }
        else wallX = left ? -3.21f : 3.21f;

        // The decorative pipe's transform is outside the portrait camera
        // (about +/-3.21). Its old centerline therefore spawned both the rail
        // and its mine beyond the visible board. Put rail hardware just
        // inside the pipe, constrained to the camera's actual visible edge.
        var cam = Camera.main;
        float visibleLimit = cam != null && cam.orthographic
            ? cam.orthographicSize * cam.aspect - .30f : 2.35f;
        float safeLimit = Mathf.Max(.65f, Mathf.Min(2.35f, visibleLimit));
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
    // pass, x1.1 / x1.2 / x1.3 on later loops, and KEEP FLYING's endless
    // climb on top (LoopRules.Density; WorldManager sets it). However dense,
    // every roster spawn still goes through SpawnLane, so each row keeps a
    // ship-width gap -- a crowded row skips the spawn and the timer rolls on.
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
        return Random.Range(range.x, range.y) / Mathf.Max(0.1f, DensityMultiplier() * LoopDifficulty.DensityScale);
    }

    void spawn() { spawn(Time.deltaTime); }

    // dt is explicit so a headless test can step a whole run: Time.deltaTime
    // is 0 outside Play mode.
    void spawn(float dt)
    {
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
            bigEnmDelayTimer = Roll(phase.enemyInterval);
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

    // The current world's enemy for a role, at x on the spawn line (nothing
    // spawns if its roster art is missing; EnemyRosterTest guards the art).
    GameObject SpawnRole(EnemyRole role, float x)
    {
        var def = EnemyRoster.Pick(EnemyRoster.CurrentWorld, role);
        if (def == null || EnemyArt.Frames(def) == null) return null;
        // Lane guard: keep a ship-width gap in this spawn row (see
        // SpawnLane). No safe x this time -> skip; the timer rolls again.
        float safeX;
        if (!SpawnLane.PickX(def, x, transform.position.y, out safeX)) return null;
        return EnemyFactory.Create(def, new Vector3(safeX, transform.position.y, 0f), transform.rotation);
    }

    // "small enemy" slot: one of the world's rocks
    void spawnAstroid2()
    {
        SpawnRole(EnemyRole.Rock, Random.Range(-2.2f, 2.4f));
    }

    // "big enemy" slot: the world's armoured heavy. At ~1.1 u it keeps to
    // the middle of the lane (SpawnLane.HeavyMaxX), clear of the walls and
    // the rail mines, and SpawnLane leaves a ship-width gap beside it.
    void spawnAstroid1()
    {
        SpawnRole(EnemyRole.Big, Random.Range(-SpawnLane.HeavyMaxX, SpawnLane.HeavyMaxX));
    }

    // will create a line of animated enimies that the player is able to doge through
    void spawnAnimatedEnimeOne()
    {
        Vector3 randomEnmPosition = new Vector3(Random.Range(-2.3f, 2f), transform.position.y, transform.rotation.z);
        var def = EnemyRoster.One(EnemyRoster.CurrentWorld, EnemyRole.Alien);
        bool roster = def != null && EnemyArt.Frames(def) != null;
        int max = Random.Range(1, 5);
        for (int i = 0; i < max; i++)
        {
            Vector3 newPositionForAnimatedAliean = new Vector3(randomEnmPosition.x + (i + .5f), randomEnmPosition.y, randomEnmPosition.z);
            if (newPositionForAnimatedAliean.x >= -2.4 && newPositionForAnimatedAliean.x <= 2.2)
            {
                // the line stops short rather than close the row (SpawnLane)
                if (!roster || !SpawnLane.Fits(def, newPositionForAnimatedAliean.x, newPositionForAnimatedAliean.y)) break;
                EnemyFactory.Create(def, newPositionForAnimatedAliean, transform.rotation);
            }
        }
    }

    // Next 3 functions spawn 3 different types of astroids.
    void spawnSmallAstroid()
    {
        SpawnRole(EnemyRole.Rock, Random.Range(-2.3f, 2.3f));
    }

    void spawnMidAstroid()
    {
        SpawnRole(EnemyRole.Rock, Random.Range(-2.3f, 2f));
    }

    void spawnLargeAstroid()
    {
        SpawnRole(EnemyRole.Rock, Random.Range(-2.3f, 2.3f));
    }

    // The current world's fighters, tiered so later phases meet the nastier
    // hulls (see ChooseExtraDef). Prefabs in extraEnemyPrefabs override it.
    void spawnExtraEnemy()
    {
        Vector3 pos = new Vector3(Random.Range(-2.2f, 2.2f), transform.position.y, transform.rotation.z);
        if (extraEnemyPrefabs != null && extraEnemyPrefabs.Length > 0)
        {
            GameObject pick = extraEnemyPrefabs[Random.Range(0, extraEnemyPrefabs.Length)];
            if (pick != null) Instantiate(pick, pos, transform.rotation);
            return;
        }
        var def = ChooseExtraDef(EnemyRoster.CurrentWorld, astroidSelector);
        if (def == null || EnemyArt.Frames(def) == null) return;
        float x = def.role == EnemyRole.Big ? Mathf.Clamp(pos.x, -SpawnLane.HeavyMaxX, SpawnLane.HeavyMaxX) : pos.x;
        float safeX;
        if (SpawnLane.PickX(def, x, pos.y, out safeX))
            EnemyFactory.Create(def, new Vector3(safeX, pos.y, 0f), transform.rotation);
    }

    // Phase index -> fighter tier window: the extras start in phase 2, which
    // fields tiers 1-2; phase 3 tiers 1-3; phase 4 tiers 2-4. From phase 2 a
    // quarter of the picks are the world's rocks or its heavy instead (where
    // the Kenney meteors used to fold in).
    public static EnemyDef ChooseExtraDef(int world, int phaseIndex)
    {
        if (phaseIndex >= 2 && Random.value < .25f)
            return EnemyRoster.Pick(world, Random.value < .7f ? EnemyRole.Rock : EnemyRole.Big);
        int maxTier = Mathf.Clamp(phaseIndex, 1, 4);
        int minTier = Mathf.Max(1, maxTier - 2);
        return EnemyRoster.Fighter(world, Random.Range(minTier, maxTier + 1));
    }

    void spawnRails()
    {
        SpawnRail(Random.Range(1, 10) % 2 == 0);
    }

    // x is ignored here -- PlaceFor() always overrides it with whichever
    // rail the mine actually gets mounted to.
    void spawnMine()
    {
        // The legacy blue mine prefab has been retired. Rail mines are now
        // built from the current world's EnemyRoster mine, so their
        // visual always matches the rail and planet they are mounted on.
        if (mine != null) { SpawnEnemy(mine, 0f); return; }

        bool right = Random.value < .5f;
        Transform rail = NearestLiveRail(right);
        if (rail == null) rail = SpawnRail(right);
        if (rail == null) return;

        var def = EnemyRoster.One(EnemyRoster.CurrentWorld, EnemyRole.Mine);
        if (def != null && EnemyArt.Frames(def) != null)
        {
            float mineY = ReserveMineY(rail, transform.position.y);
            // a mine never closes the last gap in its row (a heavy may sit
            // beside the rail lane); it waits for the next roll instead
            if (!SpawnLane.Fits(def, rail.position.x, mineY)) return;
            var built = EnemyFactory.Create(def,
                new Vector3(rail.position.x, mineY, 0f), Quaternion.identity);
            // The clamp is drawn on the left (toward a left-hand wall); a
            // right-hand rail mirrors it so it always grips its own wall.
            built.GetComponent<SpriteRenderer>().flipX = rail.position.x > 0f;
            var builtMount = built.AddComponent<RailMineMount>();
            builtMount.MountTo(rail);
            liveMines.Add(built.transform);
        }
        // Without the mine art (the neon atlas, RailMineArt) no mine spawns;
        // RailMineArtTest guards it.
    }

    // Enters from below the visible board (everything else scrolls in from
    // above) and closes in on the player before settling into a passive
    // drift -- see ChaserEnemy for the actual behaviour.
    void spawnChaser()
    {
        var def = chaser == null ? EnemyRoster.One(EnemyRoster.CurrentWorld, EnemyRole.Chaser) : null;
        if (chaser == null && (def == null || EnemyArt.Frames(def) == null)) return;

        var cam = Camera.main;
        float bottomY = cam != null && cam.orthographic
            ? cam.transform.position.y - cam.orthographicSize - 1f
            : transform.position.y - 12f;
        Vector3 pos = new Vector3(Random.Range(-2.2f, 2.2f), bottomY, 0f);

        if (def != null) { EnemyFactory.Create(def, pos, Quaternion.identity); return; }

        GameObject spawned = Instantiate(chaser, pos, Quaternion.identity);
        // The borrowed hull's own straight-line scroller would fight
        // ChaserEnemy for control of the transform.
        var straightLine = spawned.GetComponent<moveItemEnmInStrightLine>();
        if (straightLine != null) Destroy(straightLine);
        if (spawned.GetComponent<ChaserEnemy>() == null) spawned.AddComponent<ChaserEnemy>();
    }
}

// A rail mine is hardware clamped to a moving side rail, rather than an
// ordinary hazard that happens to share its X coordinate.  The mount records
// the mine's along-rail offset at spawn and reapplies the full rail-relative
// position after all ordinary Update movers have run.  That gives the mine a
// single source of travel (its own side rail), keeps its clamp seated through
// a frame of animation, and prevents a rail/mine pair from shearing apart
// when the board speed changes.
public class RailMineMount : MonoBehaviour
{
    public Transform rail;
    public float lockedX;
    float railOffsetY;
    bool mounted;

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
    }

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
            transform.position = new Vector3(rail.position.x, rail.position.y + railOffsetY, transform.position.z);
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
        if (TouchInput.IsPressed || score.pauseCounter <= 0)
            transform.position += Vector3.down * moveBackGround.speed * Time.deltaTime * 30f;
        if (transform.position.y < -12f) Destroy(gameObject);
    }
}
