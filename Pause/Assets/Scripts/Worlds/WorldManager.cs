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
//           Frost -> Verdant, Verdant -> Ember, Ember -> Tide once TideEnabled)
//           shows its planet instead: the same stage and the same pressure
//           until the ship touches it, then the descent (Planetfall), which
//           calls Advance while its clouds hide the view. A world left by
//           lift-off (LiftoffCatalog: Frost, Verdant, Ember, Tide) climbs to space first (Liftoff):
//           the same stage, no pressure, nothing spawning, until its calm
//           interlude ends and it opens the gateway (OpenGateway) -- or,
//           the last live world's (Ember's, or Tide's once TideEnabled; LiftoffDef.AutoLoopNow), starts the loop
//           at once with no portal (StartLoop).
//
// Through the portal: the next planet -- or, after the final world, back to
// the world the run started in, one loop on (RunLoop, LoopRules). The score
// carries on. There is no other ending than the pilot's death.
//
// On arrival: speed resets to the ship's start speed (or the loop's arrival
// speed), pauses and star dust carry over. Progress is remembered, so a later
// run starts on the furthest planet reached (or the world the player chose in
// Options, PlayerStartWorld) -- except REPLAY, which flies the same run again
// from the world it began in (RunStartWorld).
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
        // World 5, the ocean planet. UNDER CONSTRUCTION (see TideEnabled): only its
        // planetfall / lift-off art and numbers are real. Its rails, backdrop,
        // roster, boss and explosion are Ember's stand-ins until the add-world
        // phases 5, 11, 12, 13 land (resourceFolder "Ember" = Ember's rails;
        // BackdropCatalog.For, EnemyRoster.For, BossCatalog.ForWorld and
        // TargetExplosion.KindForWorld all resolve an unknown world to Ember's).
        new WorldTheme {
            displayName = "Tide", resourceFolder = "Ember", // placeholder rails (Ember's)
            musicResource = "", progressiveMusic = false,   // plays the scene's own track
            portalColor = new Color(0.49f, 0.95f, 0.75f),   // bioluminescent mint #7CF2C0
            speedRampPerSecond = 0.00385f, enemyRampScale = 1.50f,
        },
    };

    // ======================================================================
    //  RELEASE SWITCH. Flip this one initial value to true when Tide is done
    //  (backdrops, roster, boss, elites, sounds: add-world checklist, phase 17).
    //
    //  false (production today): the loop is Space -> Frost -> Verdant -> Ember
    //  -> Space, exactly as before Tide existed. Ember is the last LIVE world
    //  (LastLiveWorld), its lift-off starts the loop, and nothing ever flies to
    //  Tide's planet. Tide still exists in Worlds[] so the developer world
    //  picker / DeveloperUnlocks / tests can start a run ON Tide (a run that
    //  begins there treats Tide as the last world, its lift-off loops).
    //
    //  true: Space -> Frost -> Verdant -> Ember -> Tide -> Space (loop + 1);
    //  Ember's lift-off opens Tide's planetfall.
    //
    //  A property so tests can flip it (and must restore it).
    // ======================================================================
    public static bool TideEnabled { get; set; } = false;

    // How many worlds the chain reaches (Worlds.Length with the switch on). The
    // tests that walk "every world" walk this many, so Tide joins them (and
    // the checks that need its real art / roster) the moment the switch flips.
    public static int LiveWorldCount { get { return LastLiveWorld + 1; } }

    // The final world of the chain the player can actually reach: the last
    // entry of Worlds[] with the release switch on, else the one before it.
    // Past it the next stop is the loop, not another planet.
    public static int LastLiveWorld
    {
        get { return TideEnabled ? Worlds.Length - 1 : Worlds.Length - 2; }
    }

    // The world the live run is on. Once this manager has set it (Start,
    // Advance) it is held here, in memory: the saved world (PlayerPrefs) is
    // progress other writers touch mid-run -- a cloud pull, developer mode's
    // restore -- and the spawners, elites, boss and music all read the world
    // through CurrentIndex, so a write there flipped them to another world
    // while the backdrop and rails stayed painted for this one (a Space cast
    // flying over Frost). -1: not set yet (the saved world is read).
    int runWorld = -1;

    public static int CurrentIndex
    {
        get
        {
            var run = Instance;
            if (run != null && run.runWorld >= 0) return run.runWorld;
            return Mathf.Clamp(PlayerPrefs.GetInt(PrefsCurrentWorld, 0), 0, Worlds.Length - 1);
        }
        set
        {
            int v = Mathf.Clamp(value, 0, Worlds.Length - 1);
            if (Instance != null) Instance.runWorld = v;
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
        get { return CurrentIndex < LastLiveWorld; }
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

    // The natural speed every new world is entered at: exactly what a fresh
    // run starts with (RunStartSpeed: the selected ship and colour's start,
    // the scene's own start as a floor, never past the cap). `loop` is
    // accepted for the callers' sake and does not matter: a loop is a new
    // world like any other and the loop's difficulty lives in the ramp rate,
    // density and enemies (LoopRules), not in a higher arrival speed.
    public static float ArrivalSpeed(int loop)
    {
        var wall = Object.FindFirstObjectByType<moveBackGround>();
        return RunStartSpeed(wall != null ? wall.startSpeed : 0f);
    }

    void Awake()
    {
        Instance = this;
        if (Application.isPlaying) HoldStartWorld();
    }

    // The manager is attached when gameS1 loads, before any Start, but it
    // picks the run's world in its own Start. A script whose Start runs first
    // would read the world the LAST run ended on (the saved current world): the
    // wrong cast, backdrop or music for a developer pick or a Replay. Hold the
    // world this run will start in from Awake on (memory only; Start saves
    // it). Edit-mode tests build managers by hand and set the saved world
    // themselves, so Awake only does this while playing.
    public void HoldStartWorld()
    {
        runWorld = Mathf.Clamp(RunStartWorld(startAtHighestUnlocked), 0, Worlds.Length - 1);
    }

    void OnDestroy()
    {
        if (Instance == this) PortalPressure.Reset();
    }

    // REPLAY (buttonClicks.replay) flies the run just played again, from the
    // world it began in -- not from the furthest planet that run reached.
    // Without this, a run started in Space that planetfell into Frost (which
    // raises highestWorld) replayed in Frost, the next one in Verdant, and so
    // on: Replay seemed to pick a world at random. Menu PLAY keeps the
    // furthest-planet rule. Kept across the reload of gameS1 (in memory
    // only); leaving for any non-gameplay scene drops it (GameStateReset).
    static int replayWorld = -1;

    // Called by Replay while the old run is still live: RunLoop.StartWorld is
    // where it began (a loop goes back there too, so it is the run's world).
    public static void PinReplayWorld()
    {
        replayWorld = Instance != null ? Mathf.Clamp(RunLoop.StartWorld, 0, Worlds.Length - 1) : -1;
    }

    public static void ClearReplayWorld() { replayWorld = -1; }

    // -1: none (the next run uses the normal start rule).
    public static int PinnedReplayWorld { get { return replayWorld; } }

    // The world a run begins on: a replay's pinned world, else the
    // progression rule (furthest planet reached, or the developer's pick).
    public static int RunStartWorld(bool startAtHighestUnlocked)
    {
        if (replayWorld >= 0) return Mathf.Clamp(replayWorld, 0, Worlds.Length - 1);
        if (UsesPlayerStartWorld(startAtHighestUnlocked)) return PlayerStartWorld.Chosen();
        return DeveloperUnlocks.StartWorld(startAtHighestUnlocked);
    }

    // The player's own START WORLD choice (Options) decides the run, below the
    // replay pin and the developer pick.
    static bool UsesPlayerStartWorld(bool startAtHighestUnlocked)
    {
        if (!startAtHighestUnlocked) return false;
        if (DeveloperUnlocks.Enabled) return false;   // developer runs start by the developer's rule
        return PlayerStartWorld.Chosen() >= 0;
    }

    // Where RunStartWorld got its answer, for the run-start log line.
    public static string RunStartSource(bool startAtHighestUnlocked)
    {
        if (replayWorld >= 0) return "replay pin";
        if (DeveloperUnlocks.Enabled && DeveloperUnlocks.HasSelectedWorld) return "developer pick";
        if (UsesPlayerStartWorld(startAtHighestUnlocked)) return "player start world";
        return startAtHighestUnlocked ? "furthest planet" : "journey (Space)";
    }

    void Start()
    {
        // Unlocks are permanent: once a planet has been reached, later runs
        // start there rather than replaying the earlier worlds. Developer mode
        // can pin a start world from Options instead. Replay restarts the
        // world the replayed run began in (RunStartWorld).
        string source = RunStartSource(startAtHighestUnlocked);
        CurrentIndex = RunStartWorld(startAtHighestUnlocked);
        Debug.Log("[WorldManager] run start: " + Current.displayName + " (index " + CurrentIndex + ", source " + source +
                  "; saved highest " + PlayerPrefs.GetInt(PrefsHighestWorld, 0) + ", dev " + DeveloperUnlocks.Enabled +
                  ", dev pick " + (DeveloperUnlocks.HasSelectedWorld ? PlayerPrefs.GetInt(DeveloperUnlocks.SelectedWorldKey).ToString() : "none") +
                  ", player pick " + (PlayerStartWorld.HasChoice ? PlayerPrefs.GetInt(PlayerStartWorld.Key).ToString() : "none") +
                  ", boss rush " + BossDev.RushMode + ", loop " + RunLoop.Index + ")");
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
        // Leaving a planet that lifts off (LiftoffCatalog: Frost, Verdant, Ember, Tide): the climb
        // to space and a calm interlude first, then Liftoff opens the gateway
        // itself (no pressure until then). Missing art: the gateway now.
        if (Liftoff.Spawn(LiftoffCatalog.For(CurrentIndex, PortalDestination, !HasNext)) != null) return;
        OpenGateway();
    }

    // The way on from the open stage: the next planet's planetfall if it has
    // one, else the portal; either way the pressure starts. The lift-off
    // calls it when its interlude is over: Frost's ends on Verdant's planet
    // approach, Verdant's on Ember's, Ember's on Tide's (TideEnabled). The last
    // live world's (LastLiveWorld) starts the
    // loop itself (StartLoop) and comes here only if that fails: the loop
    // portal back round (a loop is never a planetfall).
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

    // The last world's lift-off (LiftoffDef.autoLoop) after its interlude:
    // the loop's world change straight away, no portal and no pressure --
    // the same Advance the loop portal calls (loop count, bosses again,
    // speed, difficulty, painter, music, banner). The interlude already
    // shows Space's backdrop, so the backdrop doesn't change. False (and
    // nothing changed): the caller opens the loop portal instead, so the
    // pilot is never stranded.
    public bool StartLoop()
    {
        if (!portalOpen || HasNext) return false;
        int was = CurrentIndex, loopWas = RunLoop.Index;
        try { Advance(true); }
        catch (System.Exception e)
        {
            // Advance sets its state before any presentation can throw: if
            // the world already changed, the loop has begun regardless.
            Debug.LogException(e);
        }
        return !portalOpen && (CurrentIndex != was || RunLoop.Index != loopWas);
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

        // Nothing of the world left comes along (a planetfall cleared the
        // board at its commit; a portal has no such beat): the hazards and
        // elites on the board are this world's cast, not the next one's.
        Planetfall.ClearBoard(Camera.main);

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

        // Speed resets on arrival to the selected ship's start speed, the same
        // value a fresh run starts with (ArrivalSpeed); a limit-break boost
        // in progress does not carry over either. Score, distance, pauses and
        // star dust deliberately carry over. The ramp then climbs again
        // from there at the new world's rate.
        SpeedRamp.ResetBoost();
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
        int last = LastLiveWorld;
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
