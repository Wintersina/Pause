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
             "the blue shield atom and the green heal atom count the same.")]
    public float secondsPerAtom = 7f;

    [Tooltip("Seconds shaved off the current countdown by a red pause atom -- " +
             "smaller than secondsPerAtom because the violet capacitor atom is " +
             "the dedicated charge-cutter now (the red atom also gives a free shot).")]
    public float secondsPerRedAtom = 5f;

    [Tooltip("Seconds shaved off the active weapon charge by a violet capacitor atom.")]
    public float secondsPerCooldownAtom = 12f;

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

    ShipLoadout loadout;
    int shipIndex;
    ShipAttackRunner runner;
    SecretPowerController secret;
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
    public ShipLoadout Loadout => loadout;
    public ShipAttackRunner Runner => runner;
    public SecretPowerController Secret => secret;

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
        loadout = ShipLoadoutTable.For(shipIndex);
        cooldown = RollCooldown();
        timer = cooldown;
        gun = UltimateGun.Attach(gameObject);
        indicator = ChargeIndicator.Attach(this);
        runner = ShipAttackRunner.Attach(gameObject, shipIndex);
        secret = SecretPowerController.Attach(gameObject, shipIndex);
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
            cooldown = RollCooldown();
            timer = cooldown;
        }

        // after the ultimate, so a free shot waiting on it goes right after
        ServiceFreeShots(running && Time.timeScale > 0f);
    }

    // ---- red atom free shot ---------------------------------------------
    //
    // Picking up a red atom fires the ship's main weapon once, for free: the
    // same attack at the same weapon level (skins), scoring through
    // ShipAttackHits like any firing. It never touches timer or cooldown, so
    // the charge meter keeps its progress and the ultimate still goes off
    // exactly when it would have; the gun drone and the charge indicator's
    // release are left alone too -- only a red flash on the indicator and a
    // "FREE SHOT" word say why it fired.
    //
    // It waits (queued, up to MaxPendingFreeShots) rather than break or crowd
    // anything: while the world is frozen, while the ultimate is sliding out
    // or about to fire, through the top tier's cinematic, and while an attack
    // of this ship is still mid-fire (a beam, blowtorch, orbit, burst) -- it
    // then goes the moment that one ends. Projectiles already in flight don't
    // hold it up. The top tier fires its quick, non-cinematic volley
    // (ShipAttackRunner.QuickVolley) -- no slow motion.

    public const int MaxPendingFreeShots = 3;
    public const string FreeShotLabel = "FREE SHOT";

    int pendingFree;
    public int PendingFreeShots => pendingFree;
    public int FreeShotsFired { get; private set; }

    // Why a free shot has to wait right now (world freezing aside).
    public bool FreeShotBlocked =>
        CinematicClearActive ||
        timer <= extendLeadSeconds ||
        (runner != null && runner.ActiveRuns > 0);

    // collisionDetection: a red atom was collected. A pickup only happens
    // while the world moves, so it fires straight away unless blocked.
    public void FreeShot()
    {
        if (buttonClicks.playerDied) return;
        pendingFree = Mathf.Min(pendingFree + 1, MaxPendingFreeShots);
        ServiceFreeShots(Time.timeScale > 0f);
    }

    // Fires one queued free shot if nothing is in the way. Called every
    // frame from Update; never touches timer / cooldown.
    public void ServiceFreeShots(bool worldRunning)
    {
        if (pendingFree <= 0) return;
        if (buttonClicks.playerDied) { pendingFree = 0; return; }
        if (!worldRunning || FreeShotBlocked) return;
        if (runner == null) runner = ShipAttackRunner.Attach(gameObject, shipIndex);
        if (!runner.FireFree()) return;
        pendingFree--;
        FreeShotsFired++;
        UltimateShotSound.Play(shipIndex);
        if (indicator != null) indicator.FlashFree();
        var hud = ScoreHud.Current;
        if (hud != null) hud.ShowWord(FreeShotLabel, transform.position + Vector3.up * 1.1f, AkiraPalette.RedHi);
    }

    // A fresh countdown: a random point in cooldownRange, shortened by the
    // ship's weapon level (ShipWeaponUpgrades: bought hull colours).
    float RollCooldown()
    {
        return Random.Range(cooldownRange.x, cooldownRange.y) * ShipWeaponUpgrades.CooldownScale(shipIndex);
    }

    public int WeaponLevel => ShipWeaponUpgrades.Level(shipIndex);

    // Called from collisionDetection when the player collects star dust or
    // an atom -- speeds up the current countdown rather than waiting it out.
    public void ReduceTimer(float seconds)
    {
        timer = Mathf.Max(0f, timer - seconds);
    }

    // Violet capacitor atom: a stronger, dedicated cut to the weapon charge.
    // Never fires the weapon itself -- a cut to zero leaves Update to fire it
    // on its next running frame, with the usual slide-out. Returns the
    // seconds actually cut: min(secondsPerCooldownAtom, what was left).
    public float ReduceWeaponCooldown()
    {
        float cut = Mathf.Min(Mathf.Max(0f, secondsPerCooldownAtom), Mathf.Max(0f, timer));
        ReduceTimer(secondsPerCooldownAtom);
        if (indicator != null) indicator.FlashCharge();
        return cut;
    }

    // The ship's word for a capacitor pickup: "WEAPON CHARGED" when the cut
    // emptied the countdown, otherwise "-Ns CHARGE" with the seconds cut.
    // Labels are cached, so a pickup allocates nothing.
    public const string WeaponChargedLabel = "WEAPON CHARGED";
    static string[] chargeCutLabels;

    public static string CooldownAtomLabel(float secondsCut, bool fullyCharged)
    {
        if (fullyCharged) return WeaponChargedLabel;
        int s = Mathf.Max(0, Mathf.RoundToInt(secondsCut));
        if (chargeCutLabels == null) chargeCutLabels = new string[61];
        if (s >= chargeCutLabels.Length) return "-" + s + "s CHARGE";
        return chargeCutLabels[s] ?? (chargeCutLabels[s] = "-" + s + "s CHARGE");
    }

    // collisionDetection: a violet capacitor atom was collected. Cuts the
    // charge and returns the word to show.
    public string CollectCooldownAtom()
    {
        float cut = ReduceWeaponCooldown();
        return CooldownAtomLabel(cut, timer <= 0f);
    }

    // Capacitor Dump (a secret power): the attack comes back at once -- the
    // ready tell still plays, so it never goes off unannounced.
    public void RechargeNow()
    {
        timer = Mathf.Min(timer, ChargeIndicator.ReadySeconds * .9f);
    }

    // The ship's main attack. Only the top price tier (ShipLoadoutTable)
    // gets the cinematic volley at everything on screen, with its slow
    // motion and early clear; every other ship fires its own directional or
    // limited weapon at normal speed (ShipAttackRunner).
    void Fire()
    {
        if (gun != null) gun.Fire();
        if (indicator != null) indicator.Release();
        UltimateShotSound.Play(shipIndex);
        if (!loadout.IsTopTier)
        {
            if (runner == null) runner = ShipAttackRunner.Attach(gameObject, shipIndex);
            runner.Fire();
            return;
        }
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

    // One homing shot landed. Through ShipAttackHits, like every attack: a
    // hazard is destroyed; the boss (BossTarget, an IShipAttackTarget) is
    // hit, not destroyed, and one homing shot is one full ultimate hit.
    void HitTarget(GameObject target, Color tint)
    {
        if (target == null) return;
        ShipAttackHits.Hit(target, shipIndex);
        CheckCleared();
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
        WorldTimeFx.Reset();
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
