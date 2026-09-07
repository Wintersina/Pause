using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// Runs the active ship's power on a cooldown.
//
// Attached to the player ship at runtime by ShipPowerBootstrap, so no prefab or
// scene wiring is needed. Everything here is Inspector-tunable.
//
// Used to require a second finger on screen to spend a charged power, with a
// text readout telling the player to do that -- clunky on a one-touch game,
// and easy to miss entirely. It now fires itself the moment it is ready, no
// input at all, and UltimateGun gives the player something to watch coming:
// a small weapon that slides out of the ship's left side over the last
// second or so before it goes off. Collecting star dust or an atom shaves
// time off the current countdown (see ReduceTimer, called from
// collisionDetection's pickup handling), so playing well gets the ultimate
// back faster.
public class ShipPowerController : MonoBehaviour
{
    [Header("Timing")]
    [Tooltip("The countdown is rerolled to a random point in this range every " +
             "time the power fires, then counts down on its own -- only while " +
             "the game is actually running (finger down, not dead) -- and " +
             "fires itself the instant it reaches zero.")]
    public Vector2 cooldownRange = new Vector2(30f, 60f);

    [Tooltip("How many seconds before firing the gun starts sliding out.")]
    public float extendLeadSeconds = 1.2f;

    [Header("Pickup timer boost")]
    [Tooltip("Seconds shaved off the current countdown per star dust pickup collected.")]
    public float secondsPerDust = 0.5f;

    [Tooltip("Seconds shaved off the current countdown per atom collected -- " +
             "blue, red or the green heal atom all count the same.")]
    public float secondsPerAtom = 7f;

    [Header("Tuning")]
    public float laserWidth = 0.85f;
    public float shockwaveRadius = 3.2f;
    public int missileCount = 4;
    public float missileRadius = 6f;
    public float cloakSeconds = 4f;
    public float magnetRadius = 5f;
    public float magnetSeconds = 5f;
    public float dilationScale = 0.45f;
    public float dilationSeconds = 4f;
    public int overchargePauses = 2;

    public static ShipPowerController Instance { get; private set; }

    ShipPower power;
    float timer;
    float cooldown;
    UltimateGun gun;

    void Awake()
    {
        Instance = this;
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    void Start()
    {
        power = ShipPowerTable.For(PlayerPrefs.GetInt("spawnShip", 0));
        cooldown = Random.Range(cooldownRange.x, cooldownRange.y);
        timer = cooldown;
        gun = UltimateGun.Attach(gameObject);
    }

    void Update()
    {
        bool running = !buttonClicks.playerDied &&
                       (TouchInput.IsPressed || score.pauseCounter <= 0);

        if (running && timer > 0f) timer -= Time.deltaTime;

        float extendTarget = timer <= extendLeadSeconds
            ? 1f - Mathf.Clamp01(timer / Mathf.Max(0.01f, extendLeadSeconds))
            : 0f;
        if (gun != null) gun.Tick(extendTarget);

        if (running && timer <= 0f)
        {
            Fire();
            cooldown = Random.Range(cooldownRange.x, cooldownRange.y);
            timer = cooldown;
        }
    }

    // Called from collisionDetection when the player collects star dust or
    // an atom -- speeds up the current countdown rather than waiting it out.
    public void ReduceTimer(float seconds)
    {
        timer = Mathf.Max(0f, timer - seconds);
    }

    void Fire()
    {
        if (gun != null) gun.Fire();

        switch (power)
        {
            case ShipPower.Laser:        DoLaser();      break;
            case ShipPower.Missiles:     DoMissiles();   break;
            case ShipPower.Shockwave:    DoShockwave();  break;
            case ShipPower.Cloak:        StartCoroutine(DoCloak());   break;
            case ShipPower.Magnet:       StartCoroutine(DoMagnet());  break;
            case ShipPower.TimeDilation: StartCoroutine(DoDilation()); break;
            case ShipPower.Railgun:      DoRailgun();    break;
            case ShipPower.Overcharge:   DoOvercharge(); break;
        }
    }

    // Clears the lanes either side of the ship, leaving the centre alone.
    void DoRailgun()
    {
        float x = transform.position.x;
        var tint = new Color(1f, 0.55f, 0.4f, 0.9f);

        PowerFx.Laser(transform.position + Vector3.left * 1.1f, 0.9f, 12f, tint);
        PowerFx.Laser(transform.position + Vector3.right * 1.1f, 0.9f, 12f, tint);

        foreach (var go in Targets())
        {
            float dx = Mathf.Abs(go.transform.position.x - x);
            if (dx > 0.55f && dx < 1.75f && go.transform.position.y >= transform.position.y - 1f)
            {
                PowerFx.Burst(go.transform.position, tint, 5);
                collisionDetection.PlayExplosion();
                Destroy(go);
            }
        }
    }

    // ---- helpers -------------------------------------------------------

    static bool IsTarget(GameObject go)
    {
        return go != null && (go.CompareTag("Enimey") || go.CompareTag("Astr"));
    }

    static IEnumerable<GameObject> Targets()
    {
        foreach (var t in Object.FindObjectsByType<Transform>(FindObjectsSortMode.None))
            if (IsTarget(t.gameObject))
                yield return t.gameObject;
    }

    // ---- effects -------------------------------------------------------

    void DoLaser()
    {
        float x = transform.position.x;
        PowerFx.Laser(transform.position, laserWidth * 2f, 12f, new Color(0.6f, 0.95f, 1f, 0.9f));

        foreach (var go in Targets())
            if (Mathf.Abs(go.transform.position.x - x) <= laserWidth &&
                go.transform.position.y >= transform.position.y)
            {
                PowerFx.Burst(go.transform.position, new Color(0.7f, 0.95f, 1f), 5);
                collisionDetection.PlayExplosion();
                Destroy(go);
            }
    }

    void DoMissiles()
    {
        var pool = new List<GameObject>();
        foreach (var go in Targets())
            if (Vector2.Distance(go.transform.position, transform.position) <= missileRadius)
                pool.Add(go);

        pool.Sort((a, b) =>
            Vector2.Distance(a.transform.position, transform.position)
            .CompareTo(Vector2.Distance(b.transform.position, transform.position)));

        int fired = Mathf.Min(missileCount, pool.Count);
        var hits = new Vector3[fired];
        for (int i = 0; i < fired; i++) hits[i] = pool[i].transform.position;

        PowerFx.Missiles(transform.position, hits, new Color(1f, 0.72f, 0.35f));

        for (int i = 0; i < fired; i++)
        {
            PowerFx.Burst(hits[i], new Color(1f, 0.7f, 0.3f), 6);
            collisionDetection.PlayExplosion();
            Destroy(pool[i]);
        }
    }

    void DoShockwave()
    {
        PowerFx.Ring(transform.position, shockwaveRadius, new Color(1f, 0.85f, 0.4f, 0.95f), 0.5f);

        foreach (var go in Targets())
            if (Vector2.Distance(go.transform.position, transform.position) <= shockwaveRadius)
            {
                PowerFx.Burst(go.transform.position, new Color(1f, 0.8f, 0.4f), 5);
                collisionDetection.PlayExplosion();
                Destroy(go);
            }
    }

    IEnumerator DoCloak()
    {
        // reuse the existing invulnerability window
        collisionDetection.invTimer = Mathf.Max(collisionDetection.invTimer, cloakSeconds);
        PowerFx.Ring(transform.position, 2.2f, new Color(0.75f, 0.6f, 1f, 0.9f));
        PowerFx.Aura(transform.position, new Color(0.75f, 0.6f, 1f, 0.7f), cloakSeconds);
        yield return null;
    }

    IEnumerator DoMagnet()
    {
        PowerFx.Ring(transform.position, magnetRadius, new Color(0.5f, 1f, 0.85f, 0.85f), 0.6f);
        PowerFx.Aura(transform.position, new Color(0.5f, 1f, 0.85f, 0.55f), magnetSeconds);

        float t = magnetSeconds;
        while (t > 0f)
        {
            t -= Time.deltaTime;
            foreach (var g in GameObject.FindGameObjectsWithTag("pickUp"))
            {
                if (Vector2.Distance(g.transform.position, transform.position) > magnetRadius) continue;
                g.transform.position = Vector3.MoveTowards(
                    g.transform.position, transform.position, 6f * Time.deltaTime);
            }
            yield return null;
        }
    }

    IEnumerator DoDilation()
    {
        // moveBackGround drives timeScale every frame, so slow the world by
        // scaling speed rather than fighting it over Time.timeScale.
        PowerFx.Ring(transform.position, 3.2f, new Color(0.7f, 0.8f, 1f, 0.9f), 0.7f);
        PowerFx.Aura(transform.position, new Color(0.6f, 0.75f, 1f, 0.5f), dilationSeconds);

        float original = moveBackGround.speed;
        moveBackGround.speed = original * dilationScale;
        yield return new WaitForSeconds(dilationSeconds);
        // only restore if nothing else reset it in the meantime
        if (Mathf.Approximately(moveBackGround.speed, original * dilationScale))
            moveBackGround.speed = original;
    }

    void DoOvercharge()
    {
        PowerFx.Burst(transform.position, new Color(0.6f, 1f, 0.7f), 14);
        PowerFx.Ring(transform.position, 1.8f, new Color(0.6f, 1f, 0.7f, 0.9f));

        for (int i = 0; i < overchargePauses; i++)
            score.incromentPause();
    }
}

// Attaches the power controller to the player ship when the game scene loads.
// Finds the ship by its movePlayer component, so it works regardless of which
// ship prefab was spawned.
public static class ShipPowerBootstrap
{
    [RuntimeInitializeOnLoadMethod]
    static void Init()
    {
        UnityEngine.SceneManagement.SceneManager.sceneLoaded -= OnSceneLoaded;
        UnityEngine.SceneManagement.SceneManager.sceneLoaded += OnSceneLoaded;
    }

    static void OnSceneLoaded(UnityEngine.SceneManagement.Scene scene,
                              UnityEngine.SceneManagement.LoadSceneMode mode)
    {
        if (scene.name != "gameS1") return;
        var host = new GameObject("~ShipPowerAttach");
        host.AddComponent<ShipPowerAttach>();
    }
}

// The ship prefab is instantiated a frame or two after the scene loads, so poll
// briefly rather than assuming it already exists.
public class ShipPowerAttach : MonoBehaviour
{
    float giveUp = 5f;

    void Update()
    {
        var player = Object.FindFirstObjectByType<movePlayer>();
        if (player != null)
        {
            if (player.GetComponent<ShipPowerController>() == null)
                player.gameObject.AddComponent<ShipPowerController>();
            Destroy(gameObject);
            return;
        }

        giveUp -= Time.unscaledDeltaTime;
        if (giveUp <= 0f) Destroy(gameObject);
    }
}
