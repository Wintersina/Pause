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
        [Tooltip("Extra enemy ships and meteors drawn from Resources/Prefabs/" +
                 "Enemies. Lets later phases field hardware the early ones never see.")]
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

    public GameObject[] astroid1 = new GameObject[5];
    public GameObject[] astroid2 = new GameObject[5];
    public GameObject[] astroid3 = new GameObject[5];
    public GameObject[] astroid4 = new GameObject[5];
    public GameObject[] astroid5 = new GameObject[5];
    public GameObject alien1;
    public GameObject rails;

    [Tooltip("Left empty, loads Resources/Prefabs/mine at startup.")]
    public GameObject mine;
    [Tooltip("Left empty, loads Resources/Prefabs/Enemies/kn_enemyRed5 at startup -- " +
             "the visual/collider base; ChaserEnemy supplies the actual behaviour.")]
    public GameObject chaser;

    [Tooltip("Left empty, these load from Resources/Prefabs/Enemies at startup.")]
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

        // Loading by folder means dropping new art in is enough -- no scene edit.
        if (extraEnemyPrefabs == null || extraEnemyPrefabs.Length == 0)
            extraEnemyPrefabs = Resources.LoadAll<GameObject>("Prefabs/Enemies");

        // Neither lives in that folder scan: mine.prefab sits one level up
        // (Resources/Prefabs, not Resources/Prefabs/Enemies), and the chaser
        // reuses an existing enemy hull rather than needing new art.
        if (mine == null) mine = Resources.Load<GameObject>("Prefabs/mine");
        if (chaser == null) chaser = Resources.Load<GameObject>("Prefabs/Enemies/kn_enemyRed5");

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
    // Thresholds are tuned against a 300s (5 minute) level at phaseRampScale
    // 1 -- Space's own scale, the baseline every other world ramps faster
    // than -- leaving the last ~80s of a run in full Chaos.
    static SpawnPhase[] DefaultPhases()
    {
        return new[]
        {
            new SpawnPhase {
                name = "Warm-up", activeAfterSeconds = 0f,
                rails = true, bigEnemy = true,
                railInterval = new Vector2(0.8f, 1.2f),
                enemyInterval = new Vector2(3.5f, 5f),
            },
            new SpawnPhase {
                name = "Debris", activeAfterSeconds = 35f,
                rails = true, mines = true, bigEnemy = true, smallEnemy = true, midAstroid = true,
                railInterval = new Vector2(0.7f, 1.1f),
                mineInterval = new Vector2(7f, 11f),
                enemyInterval = new Vector2(2.5f, 4.5f),
                astroidInterval = new Vector2(3f, 5f),
            },
            new SpawnPhase {
                name = "Asteroid field", extraEnemies = true, activeAfterSeconds = 80f,
                rails = true, mines = true, bigEnemy = true, smallEnemy = true,
                midAstroid = true, smallAstroid = true, aliens = true,
                railInterval = new Vector2(0.6f, 1f),
                mineInterval = new Vector2(6f, 10f),
                enemyInterval = new Vector2(2f, 3.5f),
                astroidInterval = new Vector2(2f, 4f),
                alienInterval = new Vector2(3f, 4.5f),
            },
            new SpawnPhase {
                name = "Swarm", extraEnemies = true, activeAfterSeconds = 150f,
                rails = true, mines = true, chasers = true, bigEnemy = true, smallEnemy = true,
                midAstroid = true, smallAstroid = true, bigAstroid = true, aliens = true,
                railInterval = new Vector2(0.5f, 0.9f),
                mineInterval = new Vector2(5f, 9f),
                chaserInterval = new Vector2(8f, 12f),
                enemyInterval = new Vector2(1.2f, 2.5f),
                astroidInterval = new Vector2(1.5f, 3f),
                alienInterval = new Vector2(2f, 3.5f),
            },
            new SpawnPhase {
                name = "Chaos", extraEnemies = true, activeAfterSeconds = 220f,
                rails = true, mines = true, chasers = true, bigEnemy = true, smallEnemy = true,
                midAstroid = true, smallAstroid = true, bigAstroid = true, aliens = true,
                railInterval = new Vector2(0.5f, 0.8f),
                mineInterval = new Vector2(4f, 7f),
                chaserInterval = new Vector2(6f, 9f),
                enemyInterval = new Vector2(0.6f, 1.4f),
                astroidInterval = new Vector2(0.8f, 1.8f),
                alienInterval = new Vector2(1.5f, 2.5f),
            },
        };
    }

    void Update () {
        bool flying = !buttonClicks.playerDied &&
                      (TouchInput.IsPressed || score.pauseCounter <= 0);
        if (flying) elapsedFlightSeconds += Time.deltaTime;

        SelectPhase();

        if (flying) spawn();
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
                // prefab arrays are sized 5; keep the index in range regardless
                // of how many phases are configured
                astroidSelector = Mathf.Clamp(i, 0, astroid1.Length - 1);
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
        GameObject spawned = Instantiate(prefab, PlaceFor(prefab, x), transform.rotation);
        if (PrefabName.Is(prefab, "mine"))
        {
            liveMines.Add(spawned.transform);
            var mount = spawned.GetComponent<RailMineMount>();
            if (mount == null) mount = spawned.AddComponent<RailMineMount>();
            mount.rail = pendingMineRail;
            mount.lockedX = spawned.transform.position.x;
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
        if (rails == null) return null;
        Vector3 pos = new Vector3(WorldRailX(!right), transform.position.y, 0f);
        GameObject spawned = Instantiate(rails, pos, transform.rotation);
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
        if (wall != null)
        {
            var filter = wall.GetComponent<MeshFilter>();
            if (filter != null && filter.sharedMesh != null)
            {
                float halfWidth = filter.sharedMesh.bounds.extents.x * Mathf.Abs(wall.transform.lossyScale.x);
                // Mine hardware sits at the centerline of the visible bar,
                // not at its inside edge.
                return wall.transform.position.x;
            }
            return wall.transform.position.x;
        }
        // Safety fallback for a stripped test scene. A normal game always
        // resolves the mesh calculation above; matches the authored pipe
        // position (+/-3.21) rather than an unrelated guessed constant.
        return left ? -3.21f : 3.21f;
    }

    static float Roll(Vector2 range)
    {
        return Random.Range(range.x, range.y);
    }

    void spawn()
    {
        railDelayTimer -= Time.deltaTime;
        smEnmDelayTimer -= Time.deltaTime;
        bigEnmDelayTimer -= Time.deltaTime;
        smallAstroidDelayTimer -= Time.deltaTime;
        midAstroidDelayTimer -= Time.deltaTime;
        bigAstroidDelayTimer -= Time.deltaTime;
        spawnAnimatedEnimeOneDelayTimer -= Time.deltaTime;
        extraEnemyDelayTimer -= Time.deltaTime;
        mineDelayTimer -= Time.deltaTime;
        chaserDelayTimer -= Time.deltaTime;

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

    // spawns small enimes through the board
    void spawnAstroid2()
    {
        GameObject prefab = astroid2[astroidSelector];
        SpawnEnemy(prefab, Random.Range(-2.2f, 2.4f));
    }

    // this spawn larg enimes though the board
    void spawnAstroid1()
    {
        GameObject prefab = astroid1[astroidSelector];
        SpawnEnemy(prefab, Random.Range(-2.2f, 2.4f));
    }

    // will create a line of animated enimies that the player is able to doge through
    void spawnAnimatedEnimeOne()
    {
        Vector3 randomEnmPosition = new Vector3(Random.Range(-2.3f, 2f), transform.position.y, transform.rotation.z);
        int max = Random.Range(1, 5);
        for (int i = 0; i < max; i++)
        {
            Vector3 newPositionForAnimatedAliean = new Vector3(randomEnmPosition.x + (i + .5f), randomEnmPosition.y, randomEnmPosition.z);
            if (newPositionForAnimatedAliean.x >= -2.4 && newPositionForAnimatedAliean.x <= 2.2)
                Instantiate(alien1, newPositionForAnimatedAliean, transform.rotation);
        }
    }

    // Next 3 functions spawn 3 different types of astroids.
    void spawnSmallAstroid()
    {
        GameObject prefab = astroid3[astroidSelector];
        SpawnEnemy(prefab, Random.Range(-2.3f, 2.3f));
    }

    void spawnMidAstroid()
    {
        GameObject prefab = astroid4[astroidSelector];
        SpawnEnemy(prefab, Random.Range(-2.3f, 2f));
    }

    void spawnLargeAstroid()
    {
        GameObject prefab = astroid5[astroidSelector];
        SpawnEnemy(prefab, Random.Range(-2.3f, 2.3f));
    }

    // Picks from the imported set, biased so later phases meet the nastier art:
    // black and blue hulls early, green and red once things get serious.
    void spawnExtraEnemy()
    {
        if (extraEnemyPrefabs == null || extraEnemyPrefabs.Length == 0) return;

        GameObject pick = ChooseExtra();
        if (pick == null) return;

        Vector3 pos = new Vector3(Random.Range(-2.2f, 2.2f), transform.position.y, transform.rotation.z);
        Instantiate(pick, pos, transform.rotation);
    }

    GameObject ChooseExtra()
    {
        string[] tiers = { "Black", "Blue", "Green", "Red" };
        string wanted = tiers[Mathf.Clamp(astroidSelector, 0, tiers.Length - 1)];

        var shortlist = new System.Collections.Generic.List<GameObject>();
        foreach (var go in extraEnemyPrefabs)
            if (go != null && go.name.Contains(wanted)) shortlist.Add(go);

        // meteors are colourless, so fold them in for the later phases
        if (astroidSelector >= 2)
            foreach (var go in extraEnemyPrefabs)
                if (go != null && go.name.Contains("meteor")) shortlist.Add(go);

        if (shortlist.Count == 0)
            return extraEnemyPrefabs[Random.Range(0, extraEnemyPrefabs.Length)];

        return shortlist[Random.Range(0, shortlist.Count)];
    }

    void spawnRails()
    {
        SpawnRail(Random.Range(1, 10) % 2 == 0);
    }

    // x is ignored here -- PlaceFor() always overrides it with whichever
    // rail the mine actually gets mounted to.
    void spawnMine()
    {
        if (mine == null) return;
        SpawnEnemy(mine, 0f);
    }

    // Enters from below the visible board (everything else scrolls in from
    // above) and closes in on the player before settling into a passive
    // drift -- see ChaserEnemy for the actual behaviour.
    void spawnChaser()
    {
        if (chaser == null) return;

        var cam = Camera.main;
        float bottomY = cam != null && cam.orthographic
            ? cam.transform.position.y - cam.orthographicSize - 1f
            : transform.position.y - 12f;
        Vector3 pos = new Vector3(Random.Range(-2.2f, 2.2f), bottomY, 0f);

        GameObject spawned = Instantiate(chaser, pos, Quaternion.identity);
        // The borrowed hull's own straight-line scroller would fight
        // ChaserEnemy for control of the transform.
        var straightLine = spawned.GetComponent<moveItemEnmInStrightLine>();
        if (straightLine != null) Destroy(straightLine);
        if (spawned.GetComponent<ChaserEnemy>() == null) spawned.AddComponent<ChaserEnemy>();
    }
}

// Keeps a rail mine locked to the centerline it was mounted on even while the
// rail scrolls. This only controls X; the existing enemy movement owns Y.
public class RailMineMount : MonoBehaviour
{
    public Transform rail;
    public float lockedX;

    void LateUpdate()
    {
        float x = rail != null ? rail.position.x : lockedX;
        transform.position = new Vector3(x, transform.position.y, transform.position.z);
    }
}
