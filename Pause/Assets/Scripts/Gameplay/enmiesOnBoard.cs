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

        [Tooltip("This phase is active while speed is below this value. The last phase is the catch-all.")]
        public float speedBelow = 0.2f;

        [Header("Enemy types in play")]
        public bool rails = true;
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

    [Tooltip("Left empty, these load from Resources/Prefabs/Enemies at startup.")]
    public GameObject[] extraEnemyPrefabs;

    public SpawnPhase[] phases;

    private float railDelayTimer;
    private float smEnmDelayTimer;
    private float bigEnmDelayTimer;
    private float smallAstroidDelayTimer;
    private float midAstroidDelayTimer;
    private float bigAstroidDelayTimer;
    private float spawnAnimatedEnimeOneDelayTimer;
    private float extraEnemyDelayTimer;

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

        phase = phases[0];
        astroidSelector = 0;

        // staggered so the board does not fill up the instant the run starts
        railDelayTimer = 3f;
        bigEnmDelayTimer = 3f;
        smEnmDelayTimer = 8f;
        midAstroidDelayTimer = 13f;
        smallAstroidDelayTimer = 18f;
        bigAstroidDelayTimer = 21f;
        spawnAnimatedEnimeOneDelayTimer = 6f;
        extraEnemyDelayTimer = 24f;
    }

    // Escalating mix: each phase adds a type rather than just reskinning.
    static SpawnPhase[] DefaultPhases()
    {
        return new[]
        {
            new SpawnPhase {
                name = "Warm-up", speedBelow = 0.2f,
                rails = true, bigEnemy = true,
                railInterval = new Vector2(0.8f, 1.2f),
                enemyInterval = new Vector2(3.5f, 5f),
            },
            new SpawnPhase {
                name = "Debris", speedBelow = 0.3f,
                rails = true, bigEnemy = true, smallEnemy = true, midAstroid = true,
                railInterval = new Vector2(0.7f, 1.1f),
                enemyInterval = new Vector2(2.5f, 4.5f),
                astroidInterval = new Vector2(3f, 5f),
            },
            new SpawnPhase {
                name = "Asteroid field", extraEnemies = true, speedBelow = 0.4f,
                rails = true, bigEnemy = true, smallEnemy = true,
                midAstroid = true, smallAstroid = true, aliens = true,
                railInterval = new Vector2(0.6f, 1f),
                enemyInterval = new Vector2(2f, 3.5f),
                astroidInterval = new Vector2(2f, 4f),
                alienInterval = new Vector2(3f, 4.5f),
            },
            new SpawnPhase {
                name = "Swarm", extraEnemies = true, speedBelow = 0.5f,
                rails = true, bigEnemy = true, smallEnemy = true,
                midAstroid = true, smallAstroid = true, bigAstroid = true, aliens = true,
                railInterval = new Vector2(0.5f, 0.9f),
                enemyInterval = new Vector2(1.2f, 2.5f),
                astroidInterval = new Vector2(1.5f, 3f),
                alienInterval = new Vector2(2f, 3.5f),
            },
            new SpawnPhase {
                name = "Chaos", extraEnemies = true, speedBelow = float.MaxValue,
                rails = true, bigEnemy = true, smallEnemy = true,
                midAstroid = true, smallAstroid = true, bigAstroid = true, aliens = true,
                railInterval = new Vector2(0.5f, 0.8f),
                enemyInterval = new Vector2(0.6f, 1.4f),
                astroidInterval = new Vector2(0.8f, 1.8f),
                alienInterval = new Vector2(1.5f, 2.5f),
            },
        };
    }

    void Update () {
        SelectPhase();

        if (TouchInput.IsPressed && !buttonClicks.playerDied)
        {
            spawn();
        }
        else if (score.pauseCounter <= 0 && !buttonClicks.playerDied)
        {
            spawn();
        }
    }

    void SelectPhase()
    {
        for (int i = 0; i < phases.Length; i++)
        {
            if (moveBackGround.speed < phases[i].speedBelow || i == phases.Length - 1)
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
    Vector3 PlaceFor(GameObject prefab, float x)
    {
        pendingMineRail = null;
        if (PrefabName.Is(prefab, "mine"))
        {
            Transform rail = NearestLiveRail();
            // A mine can be selected by the enemy table before a rail happens
            // to be on screen. Create its mounting rail first in that case.
            if (rail == null) rail = SpawnRail(Random.value < 0.5f);
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

    Transform NearestLiveRail()
    {
        for (int i = liveRails.Count - 1; i >= 0; i--)
            if (liveRails[i] == null) liveRails.RemoveAt(i);
        if (liveRails.Count == 0) return null;

        // Prefer the rail closest in vertical travel to this spawn point. Its
        // x is nevertheless taken directly from that rail's transform.
        Transform best = liveRails[0];
        float bestDistance = Mathf.Abs(best.position.y - transform.position.y);
        for (int i = 1; i < liveRails.Count; i++)
        {
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
        // resolves the mesh calculation above.
        return left ? -2.5f : 2.5f;
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

        if (railDelayTimer <= 0)
        {
            if (phase.rails) spawnRails();
            railDelayTimer = Roll(phase.railInterval);
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
