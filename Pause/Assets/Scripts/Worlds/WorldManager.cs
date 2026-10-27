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
//           Frost -> Verdant, Verdant -> Ember, Ember -> Tide,
//           Tide -> Storm, Storm -> Desert)
//           shows its planet instead: the same stage and the same pressure
//           until the ship touches it, then the descent (Planetfall), which
//           calls Advance while its clouds hide the view. A world left by
//           lift-off (LiftoffCatalog: Frost, Verdant, Ember, Tide, Storm, Desert) climbs to space first (Liftoff):
//           the same stage, no pressure, nothing spawning, until its calm
//           interlude ends and it opens the gateway (OpenGateway) -- or,
//           the last live world's (Desert's, the final world), starts the
//           loop at once (StartLoop): there is no portal after the final world.
//
// Through the portal: the next planet. After the final world there is no
// portal: the ship lifts off and the run starts over in Space, the first
// world of the chain, one loop on (RunLoop, LoopRules; LoopWorld). The score
// carries on. There is no other ending than the pilot's death.
//
// On arrival: speed resets to the ship's start speed (or the loop's arrival
// speed), pauses and star dust carry over. Progress is remembered, so a later
// run starts on the furthest planet reached (or the world the player chose in
// Options, PlayerStartWorld) -- except REPLAY, which flies the same run again
// on the world the pilot died in (RunStartWorld).
//
// A run that starts from the menu begins with its entry (WorldEntry): the
// portal arrival in Space, the planet's planetfall on a planet; a replay does
// not. The level clock starts when the entry hands the ship over.
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
            musicResource = "WorldMusic/Space_Main", progressiveMusic = false, // Neon Solitude, looped end to end
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
            musicResource = "WorldMusic/Verdant_Main", progressiveMusic = false, // Jungle Night Pulse
            portalColor = new Color(0.60f, 1f, 0.62f),
            speedRampPerSecond = 0.00345f, enemyRampScale = 1.20f,
        },
        new WorldTheme {
            displayName = "Ember", resourceFolder = "Ember",
            musicResource = "WorldMusic/Ember_Main", progressiveMusic = false,
            portalColor = new Color(1f, 0.62f, 0.35f),
            speedRampPerSecond = 0.00365f, enemyRampScale = 1.35f,
        },
        // World 5, the ocean planet (stand-ins still: elites' sounds, themed attacks and
        // explosion kind, see the flags-on report).
        new WorldTheme {
            displayName = "Tide", resourceFolder = "Tide",
            musicResource = "WorldMusic/Tide_Main", progressiveMusic = false,   // Deep Sonar Ping
            portalColor = new Color(0.49f, 0.95f, 0.75f),   // bioluminescent mint #7CF2C0
            speedRampPerSecond = 0.00385f, enemyRampScale = 1.50f,
        },
        // World 6, the gas giant (see the flags-on report for the stand-ins it
        // still carries: Tide's rails / cast / boss placeholder, explosion and palette).
        new WorldTheme {
            displayName = "Storm", resourceFolder = "Storm",
            musicResource = "WorldMusic/Storm_Main", progressiveMusic = false,   // Static Storm
            portalColor = new Color(0.96f, 0.95f, 0.72f),   // storm-white with a pale lightning-yellow edge
            speedRampPerSecond = 0.00405f, enemyRampScale = 1.65f,
        },
        // World 7, the dune planet (docs/desert-world-plan.md), the final world.
        // (Known stand-ins: see the flags-on report; rails / elites' art may still
        // borrow Storm's until the remaining add-world phases land.)
        new WorldTheme {
            displayName = "Desert", resourceFolder = "Desert",
            musicResource = "WorldMusic/Desert_Main", progressiveMusic = false,   // Dune Circuit
            portalColor = new Color(0.96f, 0.90f, 0.72f),   // sun-white / pale bone gold (never amber, never red)
            speedRampPerSecond = 0.00425f, enemyRampScale = 1.80f,
        },
    };

    // The world count and the last world of the chain: every world in Worlds is
    // reachable, and past the last the next stop is the loop, not another planet.
    public static int LiveWorldCount { get { return Worlds.Length; } }
    public static int LastLiveWorld { get { return Worlds.Length - 1; } }
    // Where the loop restarts after the final world: Space, the first world of
    // the chain, whatever world the run itself began in.
    public const int LoopWorld = 0;

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

    // The space leg: from a lift-off's swap to Space's sky (Liftoff.Swap) to
    // the next world's arrival (Advance), the player is flying in the
    // interlude world (Space) while CurrentIndex still names the planet just
    // left (its score, boss, loop and portal bookkeeping hang on it). What
    // spawns follows the sky, not the bookkeeping: SpawnWorld.
    int spaceLegWorld = -1;
    public static bool InSpaceLeg { get { var run = Instance; return run != null && run.spaceLegWorld >= 0; } }

    // The roster the spawners draw from (EnemyRoster.CurrentWorld: rocks,
    // heavies, aliens, extras, rail mines, chasers; EliteDirector): the
    // planet the run is on, or the interlude world during the space leg.
    public static int SpawnWorld
    {
        get
        {
            var run = Instance;
            if (run != null && run.spaceLegWorld >= 0) return run.spaceLegWorld;
            return CurrentIndex;
        }
    }

    public static void BeginSpaceLeg(int interludeWorld)
    {
        if (Instance != null) Instance.spaceLegWorld = Mathf.Clamp(interludeWorld, 0, Worlds.Length - 1);
    }

    public static WorldTheme Current
    {
        get { return Worlds[CurrentIndex]; }
    }

    public static bool HasNext
    {
        get { return CurrentIndex < LastLiveWorld; }
    }

    // Where the way on leads: the next planet, or (after the final world)
    // Space again, one loop on (LoopWorld).
    public static int PortalDestination
    {
        get { return HasNext ? CurrentIndex + 1 : LoopWorld; }
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
    // The level's expected length and natural speed when its flight began
    // (EnemyDensity.RunLengthFactor); -1 until the first flown frame.
    float levelStartSeconds = -1f, levelStartSpeed;

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

    // The spawn-rate make-up for a short (fast-start) level; 1 outside one.
    public float SpawnRunFactor
    {
        get
        {
            if (!levelBegun || levelStartSeconds <= 0f || distanceLeft <= 0f) return 1f;
            return EnemyDensity.RunLengthFactor(levelStartSeconds, levelStartSpeed, SpeedRamp.Natural);
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
        runStartWorld = runWorld;
    }

    // The world this run began on (memory only, set at run start; 0 for a
    // hand-built manager). A loop is only counted when the pilot arrives back
    // here (LoopRules.CompletesLoop): a run that began in Frost loops when it
    // returns to Frost, not when it passes through Space.
    int runStartWorld;
    public static int RunStart { get { var run = Instance; return run != null ? run.runStartWorld : 0; } }

    void OnDestroy()
    {
        if (Instance == this) PortalPressure.Reset();
    }

    // REPLAY (buttonClicks.replay) starts a fresh run on the world the pilot
    // died in (a run begun in Space that died in Frost replays in Frost), not
    // the world the dead run began in. Menu PLAY keeps the start rule
    // (player pick / developer pick / furthest planet). Kept across the reload of gameS1 (in memory
    // only); leaving for any non-gameplay scene drops it (GameStateReset).
    static int replayWorld = -1;

    // Called by Replay while the old run is still live: the world the pilot
    // is in right now, i.e. the one they died in (CurrentIndex; after a loop
    // that is Space again). The replayed run begins there; when it has flown
    // the final world its loop goes to Space like any run's (LoopWorld).
    public static void PinReplayWorld()
    {
        replayWorld = Instance != null ? Mathf.Clamp(CurrentIndex, 0, Worlds.Length - 1) : -1;
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
        // world the pilot died in (RunStartWorld).
        string source = RunStartSource(startAtHighestUnlocked);
        spaceLegWorld = -1;
        CurrentIndex = RunStartWorld(startAtHighestUnlocked);
        runStartWorld = CurrentIndex;
        Debug.Log("[WorldManager] run start: " + Current.displayName + " (index " + CurrentIndex + ", source " + source +
                  "; saved highest " + PlayerPrefs.GetInt(PrefsHighestWorld, 0) + ", dev " + DeveloperUnlocks.Enabled +
                  ", dev pick " + (DeveloperUnlocks.HasSelectedWorld ? PlayerPrefs.GetInt(DeveloperUnlocks.SelectedWorldKey).ToString() : "none") +
                  ", player pick " + (PlayerStartWorld.HasChoice ? PlayerPrefs.GetInt(PlayerStartWorld.Key).ToString() : "none") +
                  ", boss rush " + BossDev.RushMode + ", loop " + RunLoop.Index + ")");

        distanceLeft = WorldDistance;
        levelSeconds = 0f;
        levelStartSeconds = -1f;
        levelBegun = true;
        portalOpen = false;
        PortalPressure.Reset();
        // The run's entry (WorldEntry): a planet's planetfall starts in
        // Space's sky and switches to the planet under its clouds
        // (ShowEntryWorld); Space's portal arrival and every other start
        // show the world at once. A replay (a pinned world) has none.
        var entryPlan = WorldEntry.Plan(CurrentIndex, PinnedReplayWorld >= 0);
        WorldArtPreload.RunStarted(CurrentIndex, entryPlan == WorldEntry.Kind.Planetfall);
        var shown = entryPlan == WorldEntry.Kind.Planetfall ? Worlds[0] : Current;
        WorldPainter.Apply(shown);
        WorldMusic.Apply(shown);
        WorldBackdrop.Apply(shown, false);
        ApplyDifficulty(Current);
        ArtWarm.Begin(CurrentIndex);   // the boss, elite and boulder sheets, loaded in the calm start instead of at first use
        // The walls' Start may run before or after this one; both set it.
        moveBackGround.speed = RunStartSpeed(moveBackGround.speed);
        var entryKind = WorldEntry.Begin(entryPlan, CurrentIndex);
        EntryPlayed = entryKind;
        if (shown != Current && entryKind != WorldEntry.Kind.Planetfall)
        {
            // the planetfall could not play: the planet at once
            WorldPainter.Apply(Current);
            WorldMusic.Apply(Current);
            WorldBackdrop.Apply(Current, false);
        }
        // The planetfall shows the banner after its clouds (ShowEntryWorld).
        if (entryKind != WorldEntry.Kind.Planetfall) WorldBanner.Show(Current.displayName);
        Codex.Discover(Codex.WorldId(CurrentIndex));
        AchievementTracker.OnWorldEntered(CurrentIndex);
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
        // The run has not begun while its entry plays (WorldEntry) ...
        if (WorldEntry.Active) return;
        // The level clock stops for the boss ...
        if (BossEncounter.Running) return;
        // ... and for the open portal, whose own clock runs instead: the
        // longer the pilot stays, the harder the board presses.
        if (portalOpen)
        {
            PortalPressure.Tick(dt);
            return;
        }

        if (levelStartSeconds < 0f)
        {
            levelStartSpeed = SpeedRamp.Natural;
            levelStartSeconds = SecondsLeftInWorld;
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
        PostBossLeg.Begin();
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
        // The next world's backdrop and planetfall art load now, a frame at a time, under the lift-off / boss-over beat
        WorldArtPreload.Next(CurrentIndex, PortalDestination, !HasNext, Worlds[PortalDestination].displayName);
        // Leaving a planet that lifts off (LiftoffCatalog: Frost, Verdant, Ember, Tide, Storm, Desert): the climb
        // to space and a calm interlude first, then Liftoff opens the gateway
        // itself (no pressure until then). Missing art: the gateway now (the
        // final world's: the loop, without the cinematic).
        if (Liftoff.Spawn(LiftoffCatalog.For(CurrentIndex, PortalDestination, !HasNext)) != null) return;
        OpenGateway();
    }

    // The way on from the open stage: the next planet's planetfall if it has
    // one, else the portal; either way the pressure starts. The lift-off
    // calls it when its interlude is over: Frost's ends on Verdant's planet
    // approach, Verdant's on Ember's, Ember's on Tide's, Tide's on Storm's, Storm's on Desert's. The last
    // live world (LastLiveWorld) has no gateway at all: the loop starts
    // (StartLoop, a loop is never a planetfall and never a portal).
    public void OpenGateway()
    {
        if (!portalOpen) return;
        if (!HasNext) { StartLoop(); return; }
        int destination = PortalDestination;
        PortalPressure.Open(destination);
        // A planet arrived at by planetfall shows itself instead (and falls
        // back to the portal if its art is missing).
        if (Planetfall.Spawn(PlanetfallCatalog.For(CurrentIndex, destination, false)) != null) return;
        // The portal wears its world's colour.
        Portal.Spawn(Current.portalColor);
    }

    // The last world's lift-off after its interlude (or its missing art):
    // the loop's world change straight away, no portal and no pressure --
    // Advance(true): loop count, bosses again, speed, difficulty, painter,
    // music, banner, and Space's level from its start (LoopWorld). The
    // interlude already shows Space's run sky, so the backdrop doesn't
    // change. False (and nothing changed): not the final world, or no
    // gateway open yet.
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

    // What this run's start played (WorldEntry.Kind.None: nothing, or a replay).
    public WorldEntry.Kind EntryPlayed { get; private set; }

    // The planetfall entry's clouds cover the view: the planet the run
    // began on takes the place of Space's sky (the same presentation as
    // Advance, without a world change: the run is already on this world).
    // Returns the banner for after the clouds.
    public string ShowEntryWorld()
    {
        var theme = Current;
        WorldPainter.Apply(theme);
        WorldMusic.Apply(theme);
        WorldBackdrop.Apply(theme, true);
        ArtWarm.Begin(CurrentIndex);   // under the clouds: its boss, elites and boulders load before they are wanted
        return theme.displayName;
    }

    // Called by Portal when the player flies through.
    public void Advance() { Advance(true); }

    // The world change itself. Planetfall passes false and shows the
    // returned banner once its clouds have cleared. Null: nothing changed.
    public string Advance(bool showBanner)
    {
        bool wrap = !HasNext;   // past the final world: round to Space again
        // The way round again only exists as an open portal.
        if (wrap && !portalOpen) return null;
        int destination = wrap ? LoopWorld : CurrentIndex + 1;
        bool firstArrival = destination > PlayerPrefs.GetInt(PrefsHighestWorld, 0);   // a world never reached before
        // A loop counts only on arriving back at the world the run began on.
        bool loop = LoopRules.CompletesLoop(destination, runStartWorld);
        spaceLegWorld = -1;   // the next world takes the spawners over

        // Nothing of the world left comes along (a planetfall cleared the
        // board at its commit; a portal has no such beat): the hazards and
        // elites on the board are this world's cast, not the next one's.
        Planetfall.ClearBoard(Camera.main);

        // Points for the world just cleared; the run score carries on.
        RunScore.OnWorldCleared(CurrentIndex);
        AchievementTracker.OnWorldCleared();
        PortalPressure.Close(true);
        string banner;
        if (loop)
        {
            // Back on the run's starting world, one loop on: every boss again.
            int loopIndex = RunLoop.Advance();
            RunScore.OnLoop(loopIndex);
            AchievementTracker.OnLoop(loopIndex);
            BossEncounter.ForgetDone();
            CurrentIndex = destination;
            banner = Current.displayName + "  LOOP " + RunLoop.DisplayNumber;
        }
        else
        {
            // Round to Space past the final world is not a loop unless the run began there.
            if (wrap) BossEncounter.ForgetDone();
            CurrentIndex = destination;
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
        levelStartSeconds = -1f;
        levelBegun = true;

        var theme = Current;
        ApplyDifficulty(theme);
        WorldPainter.Apply(theme);
        WorldMusic.Apply(theme);
        WorldBackdrop.Apply(theme, true);
        ArtWarm.Begin(CurrentIndex);   // the next world's lazy art, loaded now rather than at its boss's entrance
        if (showBanner) WorldBanner.Show(banner);
        Codex.Discover(Codex.WorldId(CurrentIndex));
        AchievementTracker.OnWorldEntered(CurrentIndex);
        ShowStartWorldHint(firstArrival);
        return banner;
    }

    // The first new world ever reached: a small non-blocking toast that the
    // starter world can now be changed in Options (once ever).
    static void ShowStartWorldHint(bool firstArrival)
    {
        if (!PlayerStartWorld.ShouldShowHint(firstArrival, DeveloperUnlocks.Enabled || startMenu.youAreInTutorial)) return;
        PlayerStartWorld.MarkHintShown();
        try { AccountHintToast.Show(PlayerStartWorld.HintText); }
        catch (System.Exception e) { Debug.LogException(e); }
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
    // no points for the worlds skipped. Its lift-off loops to Space.
    public void DevJumpToFinal()
    {
        int last = LastLiveWorld;
        portalOpen = false;
        PortalPressure.Close(false);
        foreach (var p in Object.FindObjectsByType<Portal>(FindObjectsSortMode.None)) BossUtil.Kill(p.gameObject);
        if (Planetfall.Live != null) BossUtil.Kill(Planetfall.Live.gameObject);
        if (Liftoff.Live != null) BossUtil.Kill(Liftoff.Live.gameObject);
        if (PortalArrival.Live != null) BossUtil.Kill(PortalArrival.Live.gameObject);
        if (CurrentIndex == last) return;
        CurrentIndex = last;
        var theme = Current;
        WorldPainter.Apply(theme);
        WorldMusic.Apply(theme);
        WorldBackdrop.Apply(theme, true);
        ArtWarm.Begin(CurrentIndex);
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
