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
    [Tooltip("Seconds shaved off the current countdown per Star Dust (small star) pickup collected.")]
    public float secondsPerDust = DustCutSeconds;

    [Tooltip("Seconds shaved off the current countdown per Bright Star (large star) pickup collected.")]
    public float secondsPerBrightStar = BrightStarCutSeconds;

    [Tooltip("Seconds shaved off the current countdown per atom collected -- " +
             "the blue shield atom and the green heal atom count the same.")]
    public float secondsPerAtom = AtomCutSeconds;

    [Tooltip("Seconds shaved off the current countdown by a red pause atom -- " +
             "smaller than secondsPerAtom because the violet capacitor atom is " +
             "the dedicated charge-cutter now (the red atom also gives a free shot).")]
    public float secondsPerRedAtom = RedAtomCutSeconds;

    [Tooltip("Seconds shaved off the active weapon charge by a violet capacitor atom.")]
    public float secondsPerCooldownAtom = CooldownAtomCutSeconds;

    // The atoms' charge cuts as shipped (the fields' defaults; the controller
    // is added at runtime, so these are the values played). The codex quotes
    // them (CodexCatalogue.*Cut).
    public const float DustCutSeconds = 0.4f;       // Star Dust (smStar1)
    public const float BrightStarCutSeconds = 0.8f; // Bright Star (LargeStar1)
    public const float AtomCutSeconds = 7f;          // blue shield, green heal
    public const float RedAtomCutSeconds = 5f;       // red pause atom
    public const float CooldownAtomCutSeconds = 12f; // violet capacitor (up to)

    [Header("Cinematic attack")]
    [Tooltip("Real seconds the top tier's attack stays active after it has " +
             "played its part -- the normal end of the cinematic state.")]
    public float cinematicHoldSeconds = 0.3f;

    [Tooltip("Real seconds the closing ease-out lasts when the cinematic state ends.")]
    public float cinematicEaseOutSeconds = 0.2f;

    // How the countdown runs. A real run is Timed. The tutorial (Hints)
    // holds the charge while it teaches the rest, then arms it so that a
    // few atoms -- and only pickups -- fill it, and the player sees the
    // power go off once.
    //   Timed        counts down on its own (running world time) and on pickups
    //   Held         frozen: no ticking, pickups don't cut it, it never fires,
    //                and red-atom free shots are dropped
    //   PickupsOnly  only pickup cuts move it; it fires at zero as usual
    // Whatever the mode, a world transition (WorldTransition.InProgress: the
    // open portal, a planetfall, a lift-off) holds it the way Held does --
    // see TransitionFrozen.
    public enum ChargeMode { Timed, Held, PickupsOnly }
    [System.NonSerialized] public ChargeMode chargeMode = ChargeMode.Timed;

    // Times the ultimate has gone off (the tutorial's power step waits on it).
    public int UltimatesFired { get; private set; }

    public static ShipPowerController Instance { get; private set; }
    // True from the moment the top tier's attack fires until it has ended --
    // including the short ease-out at the end.
    public static bool CinematicClearActive { get; private set; }
    // True only during that closing ease-out.
    public static bool CinematicExiting => CinematicClearActive && exiting;
    // Set when the attack's state ended early because every on-screen target
    // was gone, rather than by the hold timer running out.
    public static bool LastCinematicEndedEarly { get; private set; }

    static bool exiting;
    static float exitStartedAt; // PauseClock
    static float exitSeconds;

    ShipLoadout loadout;
    int shipIndex;
    ShipAttackRunner runner;
    SecretPowerController secret;
    float timer;
    float cooldown;
    bool armed;   // Arm set the countdown before Start ran: keep it
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
        if (!armed)
        {
            cooldown = RollCooldown();
            timer = cooldown;
        }
        gun = UltimateGun.Attach(gameObject);
        indicator = ChargeIndicator.Attach(this);
        runner = ShipAttackRunner.Attach(gameObject, shipIndex);
        secret = SecretPowerController.Attach(gameObject, shipIndex);
        // a premium hull's timed mini-warheads (PremiumShips tiers)
        if (PremiumShips.IsPremium(shipIndex)) ParagonMissiles.Attach(gameObject, shipIndex);
    }

    // ---- world transitions ---------------------------------------------
    //
    // While the world changes (WorldTransition.InProgress: the open portal,
    // a planetfall's approach and descent, a lift-off's climb and interlude,
    // until the new world is live and the pilot has control) the charge is
    // frozen, as ChargeMode.Held freezes it: the countdown doesn't tick,
    // pickups don't cut it, it never fires (the gun stays tucked in), a red
    // atom's free shot is dropped and one already queued waits. Whatever was
    // earned is kept and carries on the moment the transition is over. The
    // pickups themselves still pay everything else (shield, heal, pause,
    // points); only their charge is void.
    public static bool TransitionFrozen => WorldTransition.InProgress;

    // The charge can't move or fire right now (the tutorial's hold, or a
    // world transition).
    bool ChargeHeld => chargeMode == ChargeMode.Held || TransitionFrozen;

    void Update()
    {
        Step(Time.deltaTime);
    }

    // One frame. Public so edit-mode tests can step it with a real dt.
    public void Step(float dt)
    {
        TickCinematic();

        bool running = !buttonClicks.playerDied &&
                       (TouchInput.IsPressed || score.pauseCounter <= 0);
        // (a boss's entrance holds the charge too: nothing fires into its slow motion)
        bool transit = TransitionFrozen || BossEncounter.ScriptedEntrance;

        if (running && timer > 0f && chargeMode == ChargeMode.Timed && !transit) timer -= dt;

        float extendTarget = !transit && timer <= extendLeadSeconds
            ? 1f - Mathf.Clamp01(timer / Mathf.Max(0.01f, extendLeadSeconds))
            : 0f;
        if (gun != null) gun.Tick(extendTarget);

        if (running && timer <= 0f && chargeMode != ChargeMode.Held && !transit)
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
    // anything: while the world is frozen, through a world transition (one
    // picked up during it is dropped -- TransitionFrozen), while the ultimate is sliding out
    // or about to fire, through the top tier's cinematic, and while an attack
    // of this ship is still mid-fire (a beam, blowtorch, orbit, burst, sentries)
    // -- it then goes the moment that one ends. Projectiles already in flight
    // don't hold it up. The top tier fires its own verb, shortened
    // (AttackRun.quick) -- no slow motion.

    public const int MaxPendingFreeShots = 3;
    public const string FreeShotLabel = "FREE SHOT";

    int pendingFree;
    public int PendingFreeShots => pendingFree;
    public int FreeShotsFired { get; private set; }

    // Why a free shot has to wait right now (world freezing aside).
    public bool FreeShotBlocked =>
        CinematicClearActive ||
        TransitionFrozen ||
        timer <= extendLeadSeconds ||
        (runner != null && runner.ActiveRuns > 0);

    // collisionDetection: a red atom was collected. A pickup only happens
    // while the world moves, so it fires straight away unless blocked.
    public void FreeShot()
    {
        if (buttonClicks.playerDied || ChargeHeld) return;
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
        // Ignore non-positive cuts; an already-ready weapon (timer 0) stays at 0.
        if (seconds <= 0f || ChargeHeld) return;
        timer = Mathf.Max(0f, timer - seconds);
    }

    // A blue or green atom was caught (collisionDetection): its cut to the
    // countdown (ReduceTimer) is shown as a pulse of the charge read-out in
    // the atom's colour. Star dust cuts silently -- it comes in clusters.
    public void AtomCharged(Color colour)
    {
        // a held / transition-frozen charge took no cut: nothing to show
        if (indicator != null && !ChargeHeld) indicator.FlashAtom(colour);
    }

    // The tutorial's power step: a fresh, empty charge of `seconds` that
    // only pickups fill (ChargeMode.PickupsOnly), so it goes off after
    // exactly the atoms it was sized for.
    public void Arm(float seconds)
    {
        cooldown = Mathf.Max(.01f, seconds);
        timer = cooldown;
        armed = true;
        chargeMode = ChargeMode.PickupsOnly;
    }

    // Violet capacitor atom: a stronger, dedicated cut to the weapon charge.
    // Never fires the weapon itself -- a cut to zero leaves Update to fire it
    // on its next running frame, with the usual slide-out. Returns the
    // seconds actually cut: min(secondsPerCooldownAtom, what was left).
    public float ReduceWeaponCooldown()
    {
        if (TransitionFrozen) return 0f;
        float cut = Mathf.Min(Mathf.Max(0f, secondsPerCooldownAtom), Mathf.Max(0f, timer));
        ReduceTimer(secondsPerCooldownAtom);
        if (indicator != null) indicator.FlashCharge();
        return cut;
    }

    // The ship's word for a capacitor pickup: "WEAPON CHARGED" when the cut
    // emptied the countdown, otherwise "-Ns CHARGE" with the seconds cut.
    // Labels are cached, so a pickup allocates nothing.
    public const string WeaponChargedLabel = "WEAPON CHARGED";
    // ... and during a world transition, when it cuts nothing.
    public const string ChargeHeldLabel = "CHARGE HELD";
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
        if (TransitionFrozen) return ChargeHeldLabel;
        float cut = ReduceWeaponCooldown();
        return CooldownAtomLabel(cut, timer <= 0f);
    }

    // The ship's main attack. Only the top price tier (ShipLoadoutTable)
    // plays the cinematic attack -- each of the three with its own verb
    // (laser sweep, frost nova, sentries), the state ending when the
    // attack has played its part or the screen is clear; every other
    // ship fires its own directional or limited weapon at normal speed
    // (ShipAttackRunner).
    void Fire()
    {
        UltimatesFired++;
        if (gun != null) gun.Fire();
        if (indicator != null) indicator.Release();
        UltimateShotSound.Play(shipIndex);
        if (runner == null) runner = ShipAttackRunner.Attach(gameObject, shipIndex);
        if (!loadout.IsTopTier)
        {
            runner.Fire();
            return;
        }
        if (!BeginCinematic()) return;
        onScreenAtStart = ClearTarget.CountOnScreen(Camera.main);
        // nothing on screen: there is nothing to "clear", so the state keeps its ordinary hold
        // rather than ending on the very first frame (CheckCleared)
        runner.Fire();
        StartCoroutine(CinematicClear());
    }

    // ---- cinematic attack ------------------------------------------------
    //
    // The top tier's attack plays at normal speed (the runner steps on gameplay
    // time; the world is never slowed). CinematicClearActive only marks it as
    // running, so spawns, boss intros and free shots wait for it. It holds until
    // the attack has played the part that needs it plus cinematicHoldSeconds. It
    // also ends the moment every hazard that was on screen is gone -- hit by the
    // attack, scrolled off, or destroyed some other way. Both endings go through
    // BeginExit(), a short ease-out before the state clears.

    bool BeginCinematic()
    {
        if (CinematicClearActive) return false;
        CinematicClearActive = true;
        LastCinematicEndedEarly = false;
        exiting = false;
        launchesDone = false;
        holdUntil = float.MaxValue;
        onScreenAtStart = 0;
        return true;
    }

    // Per frame, on the pause-aware clock (PauseClock). Ends the state on death, when the hold
    // timer runs out, or when the screen has been cleared.
    void TickCinematic()
    {
        if (!CinematicClearActive) return;

        if (buttonClicks.playerDied) { FinishCinematic(); return; }

        if (CinematicExiting)
        {
            // Lifting the finger mid-ease means the world is about to freeze
            // anyway; finish now.
            bool worldRuns = TouchInput.IsPressed || score.pauseCounter <= 0;
            if (!worldRuns || PauseClock.Now - exitStartedAt >= exitSeconds) FinishCinematic();
            return;
        }

        if (launchesDone && PauseClock.Now >= holdUntil) { BeginExit(early: false); return; }
        CheckCleared();
    }

    // Called every frame and straight from the attack, so the state
    // starts ending on the same frame the last target is destroyed.
    void CheckCleared()
    {
        if (!CinematicClearActive || CinematicExiting) return;
        if (onScreenAtStart <= 0) return;
        if (ClearTarget.CountOnScreen(Camera.main) > 0) return;
        PowerFx.Ring(transform.position, 1.4f, ShipExhaust.TintFor(shipIndex) * new Color(1f, 1f, 1f, .55f), .3f);
        BeginExit(early: true);
    }

    // The one way the state ends while the player is alive.
    void BeginExit(bool early)
    {
        if (!CinematicClearActive || CinematicExiting) return;
        LastCinematicEndedEarly = early;
        exitSeconds = Mathf.Max(0f, cinematicEaseOutSeconds);
        exitStartedAt = PauseClock.Now;
        exiting = true;
        if (exitSeconds <= 0f) FinishCinematic();
    }

    static void FinishCinematic()
    {
        CinematicClearActive = false;
        exiting = false;
    }

    // The attack is intentionally input-independent once it has begun:
    // lifting a finger cannot cancel it. Waits (real time, with a safety
    // limit) while the runner says it still wants the state held, then
    // starts the hold.
    IEnumerator CinematicClear()
    {
        float limit = PauseClock.Now + MaxCinematicSeconds;
        while (runner != null && runner.CinematicHold && PauseClock.Now < limit)
        {
            CheckCleared();
            yield return null;
        }
        launchesDone = true;
        holdUntil = PauseClock.Now + cinematicHoldSeconds;
    }

    // The state never outlasts this, whatever an attack is doing.
    public const float MaxCinematicSeconds = 6f;
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

// The ship prefab is instantiated a frame or two after the scene loads: it
// announces itself (ShipSpawn), and this waits for that instead of scanning
// the scene every frame, giving up after a while as before. Only gameS1's
// ship (movePlayer) carries powers. Out of play (edit-mode suites, where no
// Start runs) a frame still looks it up.
public class ShipPowerAttach : MonoBehaviour
{
    float giveUp = 5f;
    bool done;
    public bool Done { get { return done; } }

    void OnEnable()
    {
        ShipSpawn.Spawned += OnShip;
        if (ShipSpawn.Live != null) OnShip(ShipSpawn.Live);
    }

    void OnDisable() { ShipSpawn.Spawned -= OnShip; }

    void OnShip(GameObject player)
    {
        if (done || player == null || player.GetComponent<movePlayer>() == null) return;
        if (player.GetComponent<ShipPowerController>() == null)
            player.AddComponent<ShipPowerController>();
        done = true;   // gone on the next frame (never destroyed from inside an event or OnEnable)
    }

    void Update()
    {
        if (!done && ShipSpawn.Live != null) OnShip(ShipSpawn.Live);
        if (!done && !Application.isPlaying)
        {
            var player = Object.FindFirstObjectByType<movePlayer>();
            if (player != null) OnShip(player.gameObject);
        }
        if (done) { if (Application.isPlaying) Destroy(gameObject); else DestroyImmediate(gameObject); return; }
        giveUp -= Time.unscaledDeltaTime;
        if (giveUp <= 0f) Destroy(gameObject);
    }
}
