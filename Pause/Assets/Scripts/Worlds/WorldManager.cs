using UnityEngine;
using UnityEngine.SceneManagement;

// Drives planet progression.
//
// You start on the space world. After flying far enough a portal opens;
// flying through it moves you to the next planet.
//
// A world is a fixed DISTANCE (speed x seconds of running flight), sized so
// the baseline flight -- a regular ship in its stock colour, starting at
// gameS1's speed 0 on the world's normal ramp -- reaches the boss after
// BaselineWorldSeconds (120s). Faster flight gets there sooner: a high-end
// ship or a pricier colour starts faster (ShipStartSpeed), and so do later
// loops. Paused / frozen time flies no distance, so it never counts.
//
// On transition: speed resets to zero, pauses and star dust carry over.
// Progress is remembered, so a later run starts on the furthest planet reached.
//
// After the final world (Ember) there is no next planet. Its boss ends in a
// choice (FinalChoicePanel; every number in LoopRules):
//   KEEP FLYING  stay in Ember: no portal, endless escalation to game over;
//   LOOP BACK    a portal back to the world the run started in -- the score
//                carries on and RunLoop.Index goes up; every loop is harder.
//   no pick      the countdown ran out (FinalRoute.Encore): Ember once more as
//                a loop pass -- full level, the next loop's difficulty, its
//                boss again -- then straight into LOOP BACK, no second prompt.
public class WorldManager : MonoBehaviour
{
    public const string PrefsCurrentWorld = "currentWorld";
    public const string PrefsHighestWorld = "highestWorld";

    // Seconds of unpaused flight a world takes at the baseline pace (see
    // above); each world's distance is what that flight covers.
    public const float BaselineWorldSeconds = 120f;

    // A missed portal's retry is timed at no less than this speed.
    const float RetryMinSpeed = .05f;

    [Tooltip("How long the portal stays on screen before drifting off. Missing " +
             "it is not fatal -- another opens after the same interval.")]
    public float portalLifetime = 14f;

    [Tooltip("On: a run begins at the furthest planet reached, so a planet you " +
             "have unlocked stays unlocked. Off: every run starts at Space and " +
             "the planets are a journey flown outward.")]
    public bool startAtHighestUnlocked = true;

    public static WorldManager Instance { get; private set; }

    // Space first; the rest are generated planets. Index is the world number.
    //
    // maxSpeed was lowered ~20% uniformly across every world (0.58/0.64/0.70/
    // 0.78 -> 0.46/0.51/0.56/0.62): at the old values the world scrolled fast
    // enough near the end of a level that oncoming enemies were unreadable,
    // not just hard. Difficulty past that point now comes from enemyRampScale
    // instead -- enmiesOnBoard's phases are keyed on elapsed flight time, not
    // speed, so density keeps escalating for the rest of the level even after
    // the (now lower, still per-world-distinct) speed cap is reached.
    //
    // speedRampPerSecond is the real per-second ramp: SpeedRamp ticks it once
    // per frame. The ramp used to run once per wall (both walls carry
    // moveBackGround), and the second wall added the scene's 0.002/s on top,
    // so the pace players knew was rate + 0.002. These values bake that pace
    // in (0.00115/0.00130/0.00145/0.00165 + 0.002): HUD speed 15 at about
    // 46/44/42/40s, and each world reaches its maxSpeed in 146-170s -- past
    // the 120s baseline level, so a stock start meets the boss still
    // ramping; faster starts (ShipStartSpeed) and loops get nearer the cap.
    public static readonly WorldTheme[] Worlds =
    {
        new WorldTheme {
            displayName = "Space", resourceFolder = "",
            portalColor = new Color(0.55f, 0.85f, 1f),
            speedRampPerSecond = 0.00315f, maxSpeed = 0.46f, enemyRampScale = 1.00f,
        },
        new WorldTheme {
            displayName = "Frost", resourceFolder = "Frost",
            musicResource = "WorldMusic/Frost_Main", progressiveMusic = false,
            portalColor = new Color(0.62f, 0.92f, 1f),
            speedRampPerSecond = 0.00330f, maxSpeed = 0.51f, enemyRampScale = 1.10f,
        },
        new WorldTheme {
            displayName = "Verdant", resourceFolder = "Verdant",
            musicResource = "WorldMusic/Verdant",
            portalColor = new Color(0.60f, 1f, 0.62f),
            speedRampPerSecond = 0.00345f, maxSpeed = 0.56f, enemyRampScale = 1.20f,
        },
        new WorldTheme {
            displayName = "Ember", resourceFolder = "Ember",
            musicResource = "WorldMusic/Ember_Main", progressiveMusic = false,
            portalColor = new Color(1f, 0.62f, 0.35f),
            speedRampPerSecond = 0.00365f, maxSpeed = 0.62f, enemyRampScale = 1.35f,
        },
    };

    public static int CurrentIndex
    {
        get { return Mathf.Clamp(PlayerPrefs.GetInt(PrefsCurrentWorld, 0), 0, Worlds.Length - 1); }
        set
        {
            int v = Mathf.Clamp(value, 0, Worlds.Length - 1);
            PlayerPrefs.SetInt(PrefsCurrentWorld, v);
            if (v > PlayerPrefs.GetInt(PrefsHighestWorld, 0))
                PlayerPrefs.SetInt(PrefsHighestWorld, v);
            PlayerPrefs.Save();
        }
    }

    public static WorldTheme Current
    {
        get { return Worlds[CurrentIndex]; }
    }

    public static bool HasNext
    {
        get { return CurrentIndex < Worlds.Length - 1; }
    }

    // Distance still to fly before the level ends (moveBackGround.speed x
    // seconds). <= 0: the level is over (boss, then portal).
    float distanceLeft;
    bool portalOpen;
    // Seconds of level flight on this visit (the clock that eats distanceLeft).
    float levelSeconds;
    // A level has been begun (Start / arrival / encore) or flown: until then
    // (a bare component in a headless test) there is no level clock.
    bool levelBegun;

    // The ramp and cap last given to the walls (ApplyScaledDifficulty), for
    // turning the distance left into an estimate in seconds.
    static float appliedRate = Worlds[0].speedRampPerSecond, appliedMax = Worlds[0].maxSpeed;

    // What happens after the final world's boss.
    // Encore: the choice timed out -- one more pass of the final world, then
    // LOOP BACK by itself when its boss is over.
    public enum FinalRoute { None, Choosing, KeepFlying, LoopBack, Encore }
    FinalRoute route = FinalRoute.None;
    float endlessSeconds, endlessStep;
    FinalChoicePanel choice;

    public FinalRoute Route { get { return route; } }
    // Seconds flown in KEEP FLYING (the endless escalation clock).
    public float EndlessSeconds { get { return endlessSeconds; } }

    // Roughly how long until this planet's boss, at the current speed and
    // ramp. Other systems pace themselves against it -- the blue-atom budget
    // saves one for the end, the spawner's final stretch.
    public float SecondsLeftInWorld
    {
        get
        {
            if (distanceLeft <= 0f) return 0f;
            return SpeedRamp.SecondsToCover(moveBackGround.speed, appliedRate, appliedMax, distanceLeft);
        }
    }
    // The baseline length of a world in seconds (see BaselineWorldSeconds).
    public float WorldLength { get { return BaselineWorldSeconds; } }
    public float DistanceLeft { get { return Mathf.Max(0f, distanceLeft); } }
    public float WorldDistance { get { return WorldDistanceFor(CurrentIndex); } }
    // 0 on arrival, 1 when the level is flown.
    public float Progress01
    {
        get
        {
            float d = WorldDistance;
            return d <= 0f ? 1f : Mathf.Clamp01(1f - distanceLeft / d);
        }
    }
    public bool PortalIsOpen { get { return portalOpen; } }

    // How far through this visit's level the flight is, on the baseline
    // world's clock: 0 on arrival, BaselineWorldSeconds (120) at the boss.
    // It is the share of this visit's flight time already flown (flown /
    // (flown + SecondsLeftInWorld)), so a stock start reads its real seconds
    // and a faster start (ShipStartSpeed, loops) runs through the same
    // stretch proportionally sooner. enmiesOnBoard keys its phases on it.
    // Past the level (boss, portal, final choice, KEEP FLYING, a missed
    // portal's retry) it holds at the end. Only meaningful with HasLevelClock.
    public bool HasLevelClock { get { return levelBegun; } }
    public float LevelClockSeconds
    {
        get
        {
            if (distanceLeft <= 0f || BossEncounter.DoneInWorld(CurrentIndex)) return BaselineWorldSeconds;
            float total = levelSeconds + SecondsLeftInWorld;
            if (levelSeconds <= 0f || float.IsInfinity(total) || float.IsNaN(total) || total <= 0f) return 0f;
            return Mathf.Clamp(BaselineWorldSeconds * levelSeconds / total, 0f, BaselineWorldSeconds);
        }
    }

    // A world's distance: what the baseline flight (ShipStartSpeed.StockHud
    // start, the world's own first-pass ramp and cap) covers in
    // BaselineWorldSeconds.
    public static float WorldDistanceFor(int world)
    {
        var theme = Worlds[Mathf.Clamp(world, 0, Worlds.Length - 1)];
        return SpeedRamp.DistanceOver(ShipStartSpeed.StockHud / 100f, theme.speedRampPerSecond, theme.maxSpeed,
                                      BaselineWorldSeconds);
    }

    // The speed a run starts at: the equipped ship and colour's
    // (ShipStartSpeed), never below the scene's own start, never past the
    // world's cap. moveBackGround.Start uses it in gameS1.
    public static float RunStartSpeed(float sceneStart)
    {
        float s = Mathf.Max(sceneStart, ShipStartSpeed.EquippedSpeed());
        return Mathf.Min(s, LoopRules.MaxSpeed(Current.maxSpeed, RunLoop.DifficultyIndex, 0f));
    }

    void Awake()
    {
        Instance = this;
    }

    void Start()
    {
        // Unlocks are permanent: once a planet has been reached, later runs
        // start there rather than replaying the earlier worlds. Developer mode
        // can pin a start world from Options instead.
        CurrentIndex = DeveloperUnlocks.StartWorld(startAtHighestUnlocked);
        // LOOP BACK returns here (usually Space, or the developer's pick).
        RunLoop.StartWorld = CurrentIndex;

        distanceLeft = WorldDistance;
        levelSeconds = 0f;
        levelBegun = true;
        WorldPainter.Apply(Current);
        WorldMusic.Apply(Current);
        WorldBackdrop.Apply(Current, false);
        ApplyDifficulty(Current);
        // The walls' Start may run before or after this one; both set it.
        moveBackGround.speed = RunStartSpeed(moveBackGround.speed);
        WorldBanner.Show(Current.displayName);
        Codex.Discover(Codex.WorldId(CurrentIndex));
    }

    void Update()
    {
        // Only count time the player is actually flying, matching how the rest
        // of the game measures progress.
        if (Flying) Tick(Time.deltaTime);
    }

    // The world is running (not paused / frozen, pilot alive). Frozen time
    // flies no distance, so it never brings the boss nearer.
    public static bool Flying
    {
        get { return !buttonClicks.playerDied && (TouchInput.IsPressed || score.pauseCounter <= 0); }
    }

    // One running frame of the level clock (`dt` of flight at the current
    // moveBackGround.speed). Public so edit-mode tests can step it
    // (Time.deltaTime is 0 there).
    public void Tick(float dt)
    {
        // The level clock stops for the boss and the final choice; Ember (no
        // portal) still has a boss at the end of its level, then the choice.
        if (portalOpen || BossEncounter.Running || route == FinalRoute.Choosing) return;
        if (route == FinalRoute.KeepFlying)
        {
            TickEndless(dt);
            return;
        }
        if (!HasNext && BossEncounter.DoneInWorld(CurrentIndex) && route != FinalRoute.LoopBack &&
            route != FinalRoute.Encore) return;

        distanceLeft -= dt * Mathf.Max(0f, moveBackGround.speed);
        levelSeconds += dt;
        levelBegun = true;
        WorldMusic.TryEscalate(this);
        if (distanceLeft <= 0f) EndLevel();
    }

    // The level clock ran out (or developer mode skipped it): the world's
    // boss first, and the portal once the encounter is over. A boss already
    // dealt with on this visit (a missed portal coming round again) goes
    // straight to the portal.
    public void EndLevel()
    {
        distanceLeft = 0f;
        if (portalOpen) return;
        if (BossEncounter.Begin(CurrentIndex, OnBossOver)) return;
        if ((HasNext || route == FinalRoute.LoopBack) && !BossEncounter.Running) OpenPortal();
    }

    void OnBossOver()
    {
        if (HasNext)
        {
            if (!portalOpen) OpenPortal();
            return;
        }
        // The encore's boss: no second prompt, straight into LOOP BACK.
        if (route == FinalRoute.Encore)
        {
            if (buttonClicks.playerDied) return;
            route = FinalRoute.LoopBack;
            distanceLeft = 0f;
            WorldBanner.Show("LOOP BACK");
            if (!portalOpen) OpenPortal();
            return;
        }
        OfferFinalChoice();
    }

    // ---- after the final world ----------------------------------------------

    // The final world's boss is over (destroyed or survived): freeze and ask.
    public void OfferFinalChoice()
    {
        if (route == FinalRoute.Choosing || buttonClicks.playerDied) return;
        route = FinalRoute.Choosing;
        distanceLeft = 0f;
        choice = FinalChoicePanel.Show(CurrentIndex, RunLoop.StartWorld, RunLoop.Index, Choose);
    }

    // A button's answer: LOOP BACK (true) or KEEP FLYING (false).
    public void Choose(bool loopBack)
    {
        Choose(loopBack ? FinalPick.LoopBack : FinalPick.KeepFlying);
    }

    // The panel's answer (a button, or FinalPick.Encore when its countdown ends).
    public void Choose(FinalPick pick)
    {
        if (route != FinalRoute.Choosing) return;
        if (choice != null) choice.Close();
        choice = null;
        switch (pick)
        {
            case FinalPick.LoopBack:
                route = FinalRoute.LoopBack;
                OpenPortal();
                break;
            case FinalPick.Encore:
                BeginEncore();
                break;
            default:
                route = FinalRoute.KeepFlying;
                endlessSeconds = 0f;
                endlessStep = 0f;
                WorldBanner.Show("KEEP FLYING");
                break;
        }
    }

    // No pick in time: fly the final world once more as a loop pass. The
    // full level clock, the next loop's ramp / caps / phases / density, and
    // the boss again at the end (OnBossOver then loops back by itself). The
    // flight carries straight on -- no portal, so no arrival speed reset and
    // no world bonus yet; RunLoop.Index and the score bonuses move on only
    // with the LOOP BACK portal, as before.
    void BeginEncore()
    {
        route = FinalRoute.Encore;
        RunLoop.EncorePass = true;
        BossEncounter.ForgetDone();
        portalOpen = false;
        distanceLeft = WorldDistance;
        levelSeconds = 0f;
        levelBegun = true;
        ApplyDifficulty(Current);
        WorldBanner.Show(Current.displayName.ToUpperInvariant() + "  ONE MORE");
    }

    // KEEP FLYING: no portal, no boss. The speed cap creeps up past the
    // world's own and spawns get denser, until game over (LoopRules).
    void TickEndless(float dt)
    {
        if (dt <= 0f) return;
        endlessSeconds += dt;
        endlessStep -= dt;
        if (endlessStep > 0f) return;
        endlessStep = LoopRules.EndlessStepSeconds;
        ApplyScaledDifficulty(Current, RunLoop.Index, endlessSeconds);
    }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
    // Waiting five minutes to test a portal is impractical. Never compiled
    // into a release build.
    void LateUpdate()
    {
        if (Input.GetKeyDown(KeyCode.P) && !portalOpen && HasNext)
        {
            distanceLeft = 0f;
            OpenPortal();
        }
    }
#endif

    void OpenPortal()
    {
        portalOpen = true;
        // The loop portal wears the colour of the world it leads back to.
        Color color = route == FinalRoute.LoopBack ? Worlds[RunLoop.StartWorld].portalColor : Current.portalColor;
        Portal.Spawn(color, portalLifetime, OnPortalMissed);
    }

    void OnPortalMissed()
    {
        // Give the player another shot rather than stranding them.
        portalOpen = false;
        distanceLeft = route == FinalRoute.LoopBack
            ? Mathf.Max(moveBackGround.speed, RetryMinSpeed) * LoopRules.LoopPortalRetrySeconds
            : WorldDistance * 0.25f;
    }

    // Called by Portal when the player flies through.
    public void Advance()
    {
        bool loop = !HasNext;
        if (loop && route != FinalRoute.LoopBack) return;

        // Points for the world just cleared; the run score carries on.
        RunScore.OnWorldCleared(CurrentIndex);
        string banner;
        if (loop)
        {
            // Back to where the run began, one loop on: every boss again.
            RunScore.OnLoop(RunLoop.Advance());
            BossEncounter.ForgetDone();
            route = FinalRoute.None;
            CurrentIndex = RunLoop.StartWorld;
            banner = Current.displayName + "  LOOP " + RunLoop.DisplayNumber;
        }
        else
        {
            CurrentIndex = CurrentIndex + 1;
            banner = Current.displayName;
        }

        // Speed resets on arrival (a little higher on each loop, never below
        // the ship and colour's start speed); pauses and star dust
        // deliberately carry over.
        moveBackGround.speed = Mathf.Max(LoopRules.ArrivalSpeed(RunLoop.Index), ShipStartSpeed.EquippedSpeed());
        portalOpen = false;
        distanceLeft = WorldDistance;
        levelSeconds = 0f;
        levelBegun = true;

        var theme = Current;
        ApplyDifficulty(theme);
        WorldPainter.Apply(theme);
        WorldMusic.Apply(theme);
        WorldBackdrop.Apply(theme, true);
        WorldBanner.Show(banner);
        Codex.Discover(Codex.WorldId(CurrentIndex));
    }

    // RunLoop.DifficultyIndex: the encore plays at the next loop's.
    static void ApplyDifficulty(WorldTheme theme)
    {
        ApplyScaledDifficulty(theme, RunLoop.DifficultyIndex, 0f);
    }

    // The world's ramp and caps, scaled for the loop and (KEEP FLYING) the
    // endless clock. LoopRules has every number; loop 0 is the world as-is.
    static void ApplyScaledDifficulty(WorldTheme theme, int loop, float endlessSeconds)
    {
        // Every wall, not just the first: SpeedRamp takes its rate and cap
        // from whichever instance ticks first in a frame.
        float rate = theme.speedRampPerSecond * LoopRules.RampScale(loop);
        float max = LoopRules.MaxSpeed(theme.maxSpeed, loop, endlessSeconds);
        appliedRate = rate;
        appliedMax = max;
        foreach (var bg in Object.FindObjectsByType<moveBackGround>(FindObjectsSortMode.None))
        {
            bg.speedRampPerSecond = rate;
            bg.maxSpeed = max;
        }

        var enemies = Object.FindFirstObjectByType<enmiesOnBoard>();
        if (enemies != null) enemies.phaseRampScale = theme.enemyRampScale * LoopRules.PhaseRampScale(loop);
        // Read by the spawner (enmiesOnBoard.Roll).
        LoopDifficulty.DensityScale = LoopRules.Density(loop, endlessSeconds);
    }

    // ---- developer ----------------------------------------------------------

    // BOSS RUSH FINAL (BossDev.TriggerFinal): straight to the final world,
    // no points for the worlds skipped. The run's start world is unchanged,
    // so LOOP BACK still goes where the run began.
    public void DevJumpToFinal()
    {
        int last = Worlds.Length - 1;
        portalOpen = false;
        route = FinalRoute.None;
        RunLoop.EncorePass = false;
        foreach (var p in Object.FindObjectsByType<Portal>(FindObjectsSortMode.None)) BossUtil.Kill(p.gameObject);
        if (CurrentIndex == last) return;
        CurrentIndex = last;
        var theme = Current;
        WorldPainter.Apply(theme);
        WorldMusic.Apply(theme);
        WorldBackdrop.Apply(theme, true);
        ApplyDifficulty(theme);
        WorldBanner.Show(theme.displayName);
    }

    // Reset to the first planet -- used when starting a brand new game.
    public static void ResetProgress()
    {
        PlayerPrefs.SetInt(PrefsCurrentWorld, 0);
        PlayerPrefs.Save();
    }
}

// Attaches the manager when the game scene loads, so no scene wiring is needed.
public static class WorldBootstrap
{
    [RuntimeInitializeOnLoadMethod]
    static void Init()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (scene.name != "gameS1") return;
        if (Object.FindFirstObjectByType<WorldManager>() != null) return;
        new GameObject("~WorldManager").AddComponent<WorldManager>();
    }
}
