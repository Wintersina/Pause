using UnityEngine;
using UnityEngine.SceneManagement;

// Drives planet progression.
//
// A world is a fixed DISTANCE (speed x seconds of running flight), sized so
// the baseline flight -- a regular ship in its stock colour, starting at
// gameS1's speed 0 on the world's normal ramp -- reaches the boss after
// BaselineWorldSeconds (120s). Faster flight gets there sooner: a high-end
// ship or a pricier colour starts faster (ShipStartSpeed), a blue atom's
// boost covers extra distance, and later loops arrive faster. Speed is capped
// at SpeedRamp.Cap in every world, so the start speed is the lasting
// advantage. Paused / frozen time flies no distance, so it never counts.
//
// Every level goes through the same three stages (Stage):
//
//   Level   the level clock eats the world's distance
//   Boss    the world's boss (BossEncounter)
//   Portal  the portal is open and STAYS open until the ship flies through
//           it. The level clock is stopped; PortalPressure escalates the
//           board for as long as the pilot stays (docs/speed-and-loops.md).
//           A world with a planetfall (PlanetfallCatalog: Space -> Frost,
//           Frost -> Verdant)
//           shows its planet instead: the same stage and the same pressure
//           until the ship touches it, then the descent (Planetfall), which
//           calls Advance while its clouds hide the view. A world left by
//           lift-off (LiftoffCatalog: Frost, Verdant) climbs to space first (Liftoff):
//           the same stage, no pressure, nothing spawning, until its calm
//           interlude ends and it opens the gateway (OpenGateway).
//
// Through the portal: the next planet -- or, after the final world, back to
// the world the run started in, one loop on (RunLoop, LoopRules). The score
// carries on. There is no other ending than the pilot's death.
//
// On arrival: speed resets to the ship's start speed (or the loop's arrival
// speed), pauses and star dust carry over. Progress is remembered, so a later
// run starts on the furthest planet reached.
public class WorldManager : MonoBehaviour
{
    public const string PrefsCurrentWorld = "currentWorld";
    public const string PrefsHighestWorld = "highestWorld";

    // Seconds of unpaused flight a world takes at the baseline pace (see
    // above); each world's distance is what that flight covers.
    public const float BaselineWorldSeconds = 120f;

    [Tooltip("On: a run begins at the furthest planet reached, so a planet you " +
             "have unlocked stays unlocked. Off: every run starts at Space and " +
             "the planets are a journey flown outward.")]
    public bool startAtHighestUnlocked = true;

    public static WorldManager Instance { get; private set; }

    // Space first; the rest are generated planets. Index is the world number.
    //
    // Every world shares the one speed cap (SpeedRamp.Cap, HUD 35). What
    // differs is how fast it gets there (speedRampPerSecond: Ember reaches 35
    // about twenty seconds before Space), how fast the spawner's phases
    // arrive (enemyRampScale -- enmiesOnBoard's phases are keyed on the level
    // clock, not speed, so density keeps escalating after the cap is
    // reached), and the world's own roster, pilot load and boss.
    //
    // speedRampPerSecond is the real per-second ramp below SpeedRamp.EaseKnee
    // (SpeedRamp ticks it once per frame): HUD speed 15 at about
    // 46/44/42/40s.
    public static readonly WorldTheme[] Worlds =
    {
        new WorldTheme {
            displayName = "Space", resourceFolder = "",
            progressiveMusic = false, // plays the scene's own track (no stage clips)
            portalColor = new Color(0.55f, 0.85f, 1f),
            speedRampPerSecond = 0.00315f, enemyRampScale = 1.00f,
        },
        new WorldTheme {
            displayName = "Frost", resourceFolder = "Frost",
            musicResource = "WorldMusic/Frost_Main", progressiveMusic = false,
            portalColor = new Color(0.62f, 0.92f, 1f),
            speedRampPerSecond = 0.00330f, enemyRampScale = 1.10f,
        },
        new WorldTheme {
            displayName = "Verdant", resourceFolder = "Verdant",
            musicResource = "", progressiveMusic = false, // plays the scene's own track
            portalColor = new Color(0.60f, 1f, 0.62f),
            speedRampPerSecond = 0.00345f, enemyRampScale = 1.20f,
        },
        new WorldTheme {
            displayName = "Ember", resourceFolder = "Ember",
            musicResource = "WorldMusic/Ember_Main", progressiveMusic = false,
            portalColor = new Color(1f, 0.62f, 0.35f),
            speedRampPerSecond = 0.00365f, enemyRampScale = 1.35f,
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

    // Where the open portal leads: the next planet, or (after the final
    // world) the world the run started in.
    public static int PortalDestination
    {
        get { return HasNext ? CurrentIndex + 1 : Mathf.Clamp(RunLoop.StartWorld, 0, Worlds.Length - 1); }
    }

    // Distance still to fly before the level ends (moveBackGround.speed x
    // seconds). <= 0: the level is over (boss, then portal).
    float distanceLeft;
    bool portalOpen;
    // Seconds of level flight on this visit (the clock that eats distanceLeft).
    float levelSeconds;
    // A level has been begun (Start / arrival) or flown: until then (a bare
    // component in a headless test) there is no level clock.
    bool levelBegun;

    // The ramp last given to the walls (ApplyDifficulty), for turning the
    // distance left into an estimate in seconds.
    static float appliedRate = Worlds[0].speedRampPerSecond;

    // Where this visit to the world stands.
    public enum LevelStage { Level, Boss, Portal }
    public LevelStage Stage
    {
        get
        {
            if (portalOpen) return LevelStage.Portal;
            return BossEncounter.Running ? LevelStage.Boss : LevelStage.Level;
        }
    }

    // Roughly how long until this planet's boss, on the speed curve from the
    // current natural speed (a boost in progress only brings it a little
    // nearer). Other systems pace themselves against it -- the blue-atom
    // budget saves one for the end, the spawner's final stretch, the boss
    // warning.
    public float SecondsLeftInWorld
    {
        get
        {
            if (distanceLeft <= 0f) return 0f;
            return SpeedRamp.SecondsToCover(SpeedRamp.Natural, appliedRate, SpeedRamp.Cap, distanceLeft);
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
    // The portal is open and waiting (Stage == Portal).
    public bool PortalIsOpen { get { return portalOpen; } }

    // How far through this visit's level the flight is, on the baseline
    // world's clock: 0 on arrival, BaselineWorldSeconds (120) at the boss.
    // It is the share of this visit's flight time already flown (flown /
    // (flown + SecondsLeftInWorld)), so a stock start reads its real seconds
    // and a faster start (ShipStartSpeed, loops) runs through the same
    // stretch proportionally sooner. enmiesOnBoard keys its phases on it.
    // Past the level (boss, portal) it holds at the end. Only meaningful
    // with HasLevelClock.
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
    // start, the world's own first-pass ramp) covers in BaselineWorldSeconds.
    public static float WorldDistanceFor(int world)
    {
        var theme = Worlds[Mathf.Clamp(world, 0, Worlds.Length - 1)];
        return SpeedRamp.DistanceOver(ShipStartSpeed.StockHud / 100f, theme.speedRampPerSecond, SpeedRamp.Cap,
                                      BaselineWorldSeconds);
    }

    // The speed a run starts at: the equipped ship and colour's
    // (ShipStartSpeed), never below the scene's own start, never past the
    // cap. moveBackGround.Start uses it in gameS1.
    public static float RunStartSpeed(float sceneStart)
    {
        float s = Mathf.Max(sceneStart, ShipStartSpeed.EquippedSpeed());
        return Mathf.Min(s, SpeedRamp.Cap);
    }

    // The natural speed a portal arrival starts at on `loop`: the loop's
    // arrival speed or the ship and colour's start, whichever is higher.
    public static float ArrivalSpeed(int loop)
    {
        return Mathf.Min(SpeedRamp.Cap, Mathf.Max(LoopRules.ArrivalSpeed(loop), ShipStartSpeed.EquippedSpeed()));
    }

    void Awake()
    {
        Instance = this;
    }

    void OnDestroy()
    {
        if (Instance == this) PortalPressure.Reset();
    }

    void Start()
    {
        // Unlocks are permanent: once a planet has been reached, later runs
        // start there rather than replaying the earlier worlds. Developer mode
        // can pin a start world from Options instead.
        CurrentIndex = DeveloperUnlocks.StartWorld(startAtHighestUnlocked);
        // The final world's portal returns here (usually Space, or the developer's pick).
        RunLoop.StartWorld = CurrentIndex;

        distanceLeft = WorldDistance;
        levelSeconds = 0f;
        levelBegun = true;
        portalOpen = false;
        PortalPressure.Reset();
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

    // One running frame (`dt` of flight at the current moveBackGround.speed).
    // Public so edit-mode tests can step it (Time.deltaTime is 0 there).
    public void Tick(float dt)
    {
        // The level clock stops for the boss ...
        if (BossEncounter.Running) return;
        // ... and for the open portal, whose own clock runs instead: the
        // longer the pilot stays, the harder the board presses.
        if (portalOpen)
        {
            PortalPressure.Tick(dt);
            return;
        }

        distanceLeft -= dt * Mathf.Max(0f, moveBackGround.speed);
        levelSeconds += dt;
        levelBegun = true;
        WorldMusic.TryEscalate(this);
        if (distanceLeft <= 0f) EndLevel();
    }

    // The level clock ran out (or developer mode skipped it): the world's
    // boss first, and the portal once the encounter is over. A boss already
    // dealt with on this visit goes straight to the portal.
    public void EndLevel()
    {
        distanceLeft = 0f;
        if (portalOpen) return;
        if (BossEncounter.Begin(CurrentIndex, OnBossOver)) return;
        if (!BossEncounter.Running) OpenPortal();
    }

    // The encounter is over (destroyed or survived): the portal, in every
    // world. A dead pilot gets the death panel instead.
    void OnBossOver()
    {
        if (buttonClicks.playerDied) return;
        distanceLeft = 0f;
        if (!portalOpen) OpenPortal();
    }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
    // Waiting two minutes to test a portal is impractical. Never compiled
    // into a release build.
    void LateUpdate()
    {
        if (Input.GetKeyDown(KeyCode.P) && !portalOpen && !BossEncounter.Running)
        {
            distanceLeft = 0f;
            OpenPortal();
        }
    }
#endif

    void OpenPortal()
    {
        portalOpen = true;
        // Leaving a planet that lifts off (LiftoffCatalog: Frost, Verdant): the climb
        // to space and a calm interlude first, then Liftoff opens the gateway
        // itself (no pressure until then). Missing art: the gateway now.
        if (Liftoff.Spawn(LiftoffCatalog.For(CurrentIndex, PortalDestination, !HasNext)) != null) return;
        OpenGateway();
    }

    // The way on from the open stage: the next planet's planetfall if it has
    // one, else the portal; either way the pressure starts. The lift-off
    // calls it when its interlude is over: Frost's ends on Verdant's planet
    // approach, Verdant's on Ember's portal (an Ember planetfall needs only
    // its art and a PlanetfallCatalog entry).
    public void OpenGateway()
    {
        if (!portalOpen) return;
        int destination = PortalDestination;
        PortalPressure.Open(destination);
        // A planet arrived at by planetfall shows itself instead (and falls
        // back to the portal if its art is missing).
        if (Planetfall.Spawn(PlanetfallCatalog.For(CurrentIndex, destination, !HasNext)) != null) return;
        // The portal wears its world's colour; the loop portal the colour of
        // the world it leads back to.
        Color color = HasNext ? Current.portalColor : Worlds[destination].portalColor;
        Portal.Spawn(color);
    }

    // Called by Portal when the player flies through.
    public void Advance() { Advance(true); }

    // The world change itself. Planetfall passes false and shows the
    // returned banner once its clouds have cleared. Null: nothing changed.
    public string Advance(bool showBanner)
    {
        bool loop = !HasNext;
        // The way round again only exists as an open portal.
        if (loop && !portalOpen) return null;

        // Points for the world just cleared; the run score carries on.
        RunScore.OnWorldCleared(CurrentIndex);
        PortalPressure.Close(true);
        string banner;
        if (loop)
        {
            // Back to where the run began, one loop on: every boss again.
            RunScore.OnLoop(RunLoop.Advance());
            BossEncounter.ForgetDone();
            CurrentIndex = RunLoop.StartWorld;
            banner = Current.displayName + "  LOOP " + RunLoop.DisplayNumber;
        }
        else
        {
            CurrentIndex = CurrentIndex + 1;
            banner = Current.displayName;
        }

        // Speed resets on arrival (a little higher on each loop, never below
        // the ship and colour's start speed, never past the cap); pauses and
        // star dust deliberately carry over. A boost in progress rides on.
        SpeedRamp.SetNatural(ArrivalSpeed(RunLoop.Index));
        portalOpen = false;
        distanceLeft = WorldDistance;
        levelSeconds = 0f;
        levelBegun = true;

        var theme = Current;
        ApplyDifficulty(theme);
        WorldPainter.Apply(theme);
        WorldMusic.Apply(theme);
        WorldBackdrop.Apply(theme, true);
        if (showBanner) WorldBanner.Show(banner);
        Codex.Discover(Codex.WorldId(CurrentIndex));
        return banner;
    }

    // The world's ramp and the spawner's pace, scaled for the loop
    // (LoopRules has every number; loop 0 is the world as-is). The cap is
    // SpeedRamp.Cap whatever the loop.
    static void ApplyDifficulty(WorldTheme theme)
    {
        int loop = RunLoop.Index;
        // Every wall, not just the first: SpeedRamp takes its rate from
        // whichever instance ticks first in a frame.
        float rate = theme.speedRampPerSecond * LoopRules.RampScale(loop);
        appliedRate = rate;
        foreach (var bg in Object.FindObjectsByType<moveBackGround>(FindObjectsSortMode.None))
        {
            bg.speedRampPerSecond = rate;
            bg.maxSpeed = SpeedRamp.Cap;
        }

        var enemies = Object.FindFirstObjectByType<enmiesOnBoard>();
        if (enemies != null) enemies.phaseRampScale = theme.enemyRampScale * LoopRules.PhaseRampScale(loop);
        // Read by the spawner (enmiesOnBoard.Roll).
        LoopDifficulty.DensityScale = LoopRules.DensityScale(loop);
    }

    // ---- developer ----------------------------------------------------------

    // BOSS RUSH FINAL (BossDev.TriggerFinal): straight to the final world,
    // no points for the worlds skipped. The run's start world is unchanged,
    // so the final portal still leads back to where the run began.
    public void DevJumpToFinal()
    {
        int last = Worlds.Length - 1;
        portalOpen = false;
        PortalPressure.Close(false);
        foreach (var p in Object.FindObjectsByType<Portal>(FindObjectsSortMode.None)) BossUtil.Kill(p.gameObject);
        if (Planetfall.Live != null) BossUtil.Kill(Planetfall.Live.gameObject);
        if (Liftoff.Live != null) BossUtil.Kill(Liftoff.Live.gameObject);
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
