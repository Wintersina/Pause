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
    }

    public GameObject[] astroid1 = new GameObject[5];
    public GameObject[] astroid2 = new GameObject[5];
    public GameObject[] astroid3 = new GameObject[5];
    public GameObject[] astroid4 = new GameObject[5];
    public GameObject[] astroid5 = new GameObject[5];
    public GameObject alien1;
    public GameObject rails;

    public SpawnPhase[] phases;

    private float railDelayTimer;
    private float smEnmDelayTimer;
    private float bigEnmDelayTimer;
    private float smallAstroidDelayTimer;
    private float midAstroidDelayTimer;
    private float bigAstroidDelayTimer;
    private float spawnAnimatedEnimeOneDelayTimer;

    private int astroidSelector; // level of the game
    private SpawnPhase phase;

    void Start () {

        if (phases == null || phases.Length == 0)
            phases = DefaultPhases();

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
                name = "Asteroid field", speedBelow = 0.4f,
                rails = true, bigEnemy = true, smallEnemy = true,
                midAstroid = true, smallAstroid = true, aliens = true,
                railInterval = new Vector2(0.6f, 1f),
                enemyInterval = new Vector2(2f, 3.5f),
                astroidInterval = new Vector2(2f, 4f),
                alienInterval = new Vector2(3f, 4.5f),
            },
            new SpawnPhase {
                name = "Swarm", speedBelow = 0.5f,
                rails = true, bigEnemy = true, smallEnemy = true,
                midAstroid = true, smallAstroid = true, bigAstroid = true, aliens = true,
                railInterval = new Vector2(0.5f, 0.9f),
                enemyInterval = new Vector2(1.2f, 2.5f),
                astroidInterval = new Vector2(1.5f, 3f),
                alienInterval = new Vector2(2f, 3.5f),
            },
            new SpawnPhase {
                name = "Chaos", speedBelow = float.MaxValue,
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
    }

    // spawns small enimes through the board
    void spawnAstroid2()
    {
        Vector3 randomEnmPosition = new Vector3(Random.Range(-2.2f, 2.4f), transform.position.y, transform.rotation.z);
        Instantiate(astroid2[astroidSelector], randomEnmPosition, transform.rotation);
    }

    // this spawn larg enimes though the board
    void spawnAstroid1()
    {
        Vector3 randomEnmPosition = new Vector3(Random.Range(-2.2f, 2.4f), transform.position.y, transform.rotation.z);
        Instantiate(astroid1[astroidSelector], randomEnmPosition, transform.rotation);
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
        Vector3 randomEnmPosition = new Vector3(Random.Range(-2.3f, 2.3f), transform.position.y, transform.rotation.z);
        Instantiate(astroid3[astroidSelector], randomEnmPosition, transform.rotation);
    }

    void spawnMidAstroid()
    {
        Vector3 randomEnmPosition = new Vector3(Random.Range(-2.3f, 2f), transform.position.y, transform.rotation.z);
        Instantiate(astroid4[astroidSelector], randomEnmPosition, transform.rotation);
    }

    void spawnLargeAstroid()
    {
        Vector3 randomEnmPosition = new Vector3(Random.Range(-2.3f, 2.3f), transform.position.y, transform.rotation.z);
        Instantiate(astroid5[astroidSelector], randomEnmPosition, transform.rotation);
    }

    void spawnRails()
    {
        Vector3 left = new Vector3(-2.75f, transform.position.y, transform.rotation.z);
        Vector3 right = new Vector3(2.65f, transform.position.y, transform.rotation.z);

        if (Random.Range(1, 10) % 2 == 0)
            Instantiate(rails, right, transform.rotation);
        else
            Instantiate(rails, left, transform.rotation);
    }
}
