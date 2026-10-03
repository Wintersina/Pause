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
// second or so before it goes off, and ChargeIndicator -- a per-ship
// animated piece of that weapon in front of the hull -- shows the charge
// building (see WeaponStyleTable). Collecting star dust or an atom shaves
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

    [Header("Cinematic clear")]
    [Tooltip("Real seconds the world is held slowed after the last homing shot " +
             "launches -- the normal end of the ultimate's slow motion.")]
    public float cinematicHoldSeconds = 2.5f;

    [Tooltip("Real seconds to ease from the cinematic slow motion back up to " +
             "full speed when it ends, instead of snapping.")]
    public float cinematicEaseOutSeconds = 0.2f;

    public static ShipPowerController Instance { get; private set; }
    // True from the moment the ultimate fires until the world is fully back
    // to normal speed -- including the short ease-out at the end.
    public static bool CinematicClearActive { get; private set; }
    // True only during that closing ease-out.
    public static bool CinematicExiting => CinematicClearActive && exiting;
    // Set when the slow motion ended early because every on-screen target
    // was gone, rather than by the hold timer running out.
    public static bool LastCinematicEndedEarly { get; private set; }

    // Deliberately dramatic: threats crawl while the homing shots remain
    // readable, giving every target impact its own moment on screen.
    public const float CinematicSlowScale = 0.06f;
    public static float CinematicTimeScale
    {
        get
        {
            if (!CinematicClearActive) return 1f;
            if (!exiting) return CinematicSlowScale;
            float t = Mathf.Clamp01((Time.unscaledTime - exitStartedAt) / Mathf.Max(0.0001f, exitSeconds));
            return Mathf.Lerp(CinematicSlowScale, 1f, t * t * (3f - 2f * t));
        }
    }

    static bool exiting;
    static float exitStartedAt; // unscaled
    static float exitSeconds;

    ShipPower power;
    int shipIndex;
    float timer;
    float cooldown;
    UltimateGun gun;
    ChargeIndicator indicator;
    // Cinematic clear bookkeeping (see BeginCinematic / TickCinematic).
    int onScreenAtStart;
    bool launchesDone;
    float holdUntil;
    public float Charge01 => cooldown <= 0f ? 1f : Mathf.Clamp01(1f - timer / cooldown);
    // Seconds of running world time until the ultimate fires.
    public float SecondsLeft => timer;
    public int ShipIndex => shipIndex;
    public ChargeIndicator Indicator => indicator;

    void Awake()
    {
        Instance = this;
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
        FinishCinematic();
    }

    void Start()
    {
        // Both from the ship actually flying, so an unowned saved selection
        // (which flies the starter) can't hand the starter another's power.
        shipIndex = ShipId.Of(gameObject, ShipId.Equipped());
        power = ShipPowerTable.For(shipIndex);
        cooldown = Random.Range(cooldownRange.x, cooldownRange.y);
        timer = cooldown;
        gun = UltimateGun.Attach(gameObject);
        indicator = ChargeIndicator.Attach(this);
    }

    void Update()
    {
        TickCinematic();

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
        if (indicator != null) indicator.Release();
        UltimateShotSound.Play(shipIndex);
        if (!BeginCinematic()) return;
        StartCoroutine(CinematicClear(SnapshotTargets()));
    }

    // ---- cinematic clear -------------------------------------------------
    //
    // The ultimate slows the world to CinematicSlowScale (moveBackGround
    // applies CinematicTimeScale while CinematicClearActive), sends a homing
    // shot at every hazard, and holds the slow motion until cinematicHoldSeconds
    // after the last launch. It also ends the moment every hazard that was on
    // screen is gone -- shot down, scrolled off, or destroyed some other way --
    // so the player isn't left waiting in slow motion over an empty screen.
    // Both endings go through BeginExit(), which eases back to full speed.

    bool BeginCinematic()
    {
        if (CinematicClearActive) return false;
        CinematicClearActive = true;
        LastCinematicEndedEarly = false;
        exiting = false;
        launchesDone = false;
        holdUntil = float.MaxValue;
        onScreenAtStart = 0; // counted by SnapshotTargets
        return true;
    }

    List<GameObject> SnapshotTargets()
    {
        var targets = new List<GameObject>();
        foreach (var target in Targets())
        {
            ClearTarget.Ensure(target);
            targets.Add(target);
        }
        targets.Sort((a, b) => b.transform.position.y.CompareTo(a.transform.position.y));
        // Counted once every snapshotted hazard is registered. Nothing on
        // screen when it fired means there is nothing to "clear": keep the
        // ordinary hold so the shot still gets its moment, rather than ending
        // the slow motion on the very first frame.
        onScreenAtStart = ClearTarget.CountOnScreen(Camera.main);
        return targets;
    }

    // Per frame, on unscaled time. Ends the state on death, when the hold
    // timer runs out, or when the screen has been cleared.
    void TickCinematic()
    {
        if (!CinematicClearActive) return;

        if (buttonClicks.playerDied) { FinishCinematic(); return; }

        if (CinematicExiting)
        {
            // Lifting the finger mid-ease means the world is about to freeze
            // anyway; finishing now hands timeScale straight back to
            // moveBackGround rather than ramping up only to snap to 0.
            bool worldRuns = TouchInput.IsPressed || score.pauseCounter <= 0;
            if (!worldRuns || Time.unscaledTime - exitStartedAt >= exitSeconds) FinishCinematic();
            return;
        }

        if (launchesDone && Time.unscaledTime >= holdUntil) { BeginExit(early: false); return; }
        CheckCleared();
    }

    // Called every frame and straight from each hit, so the slow motion
    // starts lifting on the same frame the last target is destroyed.
    void CheckCleared()
    {
        if (!CinematicClearActive || CinematicExiting) return;
        if (onScreenAtStart <= 0) return;
        if (ClearTarget.CountOnScreen(Camera.main) > 0) return;
        PowerFx.Ring(transform.position, 2.2f, ShipExhaust.TintFor(shipIndex), .3f);
        BeginExit(early: true);
    }

    // The one way the slow motion ends while the player is alive.
    void BeginExit(bool early)
    {
        if (!CinematicClearActive || CinematicExiting) return;
        LastCinematicEndedEarly = early;
        exitSeconds = Mathf.Max(0f, cinematicEaseOutSeconds);
        exitStartedAt = Time.unscaledTime;
        exiting = true;
        if (exitSeconds <= 0f) FinishCinematic();
    }

    static void FinishCinematic()
    {
        CinematicClearActive = false;
        exiting = false;
    }

    // The ultimate is intentionally input-independent once it has begun:
    // lifting a finger cannot cancel shots already hunting the screen's
    // hazards. Unscaled timing keeps the sequence smooth while the world is
    // slowed to make every impact readable. Shots keep launching even if the
    // slow motion has already lifted (the screen cleared, but hazards above
    // it were snapshotted too); a shot whose target is gone fizzles in
    // PowerFx.HomeTo rather than retargeting.
    IEnumerator CinematicClear(List<GameObject> targets)
    {
        Color tint = ShipExhaust.TintFor(shipIndex);
        for (int i = 0; i < targets.Count; i++)
        {
            var target = targets[i];
            if (target == null) continue;
            Vector3 from = gun != null ? gun.MuzzlePosition : transform.position + Vector3.up;
            PowerFx.HomingProjectile(from, target.transform, tint, shipIndex, 2.4f, () => HitTarget(target, tint));
            yield return new WaitForSecondsRealtime(.11f);
        }

        // Let the final dart land before returning the normal simulation rate.
        // HomeTo has a 2.4s unscaled safety limit; keep the world slowed until
        // even the last, farthest arc has had time to connect -- unless the
        // screen clears first (TickCinematic).
        launchesDone = true;
        holdUntil = Time.unscaledTime + cinematicHoldSeconds;
    }

    void HitTarget(GameObject target, Color tint)
    {
        if (target == null) return;
        // The boss is hit, not destroyed: it shortens the fight (BossTarget).
        if (BossTarget.Intercept(target, shipIndex)) { CheckCleared(); return; }
        TargetExplosion.Spawn(target, shipIndex);
        collisionDetection.PlayExplosion();
        collisionDetection.AwardDestroyedTarget(target);
        ClearTarget.Release(target);
        Destroy(target);
        CheckCleared();
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

    // Phase Cloak: cloakSeconds of real invulnerability with its own lavender
    // phase look (ring + pulsing aura), not the blue-atom shield.
    //
    // It used to only bump collisionDetection.invTimer, but hazards are gated
    // on atomCheck, which only a blue atom sets -- so Cloak never protected
    // the ship (and, during a shield, it silently stretched the shield's own
    // timer instead). It now runs collisionDetection's separate cloak clock;
    // hazards check Invulnerable (shield or Cloak), so the two overlap
    // cleanly and each ends on its own schedule.
    public static readonly Color CloakTint = new Color(0.75f, 0.6f, 1f, 0.9f);

    public void DoCloak()
    {
        collisionDetection.BeginCloak(cloakSeconds);
        PowerFx.Ring(transform.position, 2.2f, CloakTint);
        PowerFx.CloakAura(new Color(0.75f, 0.6f, 1f, 0.7f), cloakSeconds);
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
