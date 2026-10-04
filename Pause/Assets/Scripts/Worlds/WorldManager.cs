using UnityEngine;
using UnityEngine.SceneManagement;

// Drives planet progression.
//
// You start on the space world. After enough active flight time a portal opens;
// flying through it moves you to the next planet.
//
// On transition: speed resets to zero, pauses and star dust carry over.
// Progress is remembered, so a later run starts on the furthest planet reached.
//
// After the final world (Ember) there is no next planet. Its boss ends in a
// choice (FinalChoicePanel; every number in LoopRules):
//   KEEP FLYING  stay in Ember: no portal, endless escalation to game over;
//   LOOP BACK    a portal back to the world the run started in -- the score
//                carries on and RunLoop.Index goes up; every loop is harder.
public class WorldManager : MonoBehaviour
{
    public const string PrefsCurrentWorld = "currentWorld";
    public const string PrefsHighestWorld = "highestWorld";

    [Tooltip("Seconds of active flight before the portal opens.")]
    public float secondsPerWorld = 180f;   // 3 minutes

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
    // 46/44/42/40s, and each world reaches its maxSpeed in 146-170s, inside
    // the 180s level.
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

    float timer;
    bool portalOpen;

    // What happens after the final world's boss.
    public enum FinalRoute { None, Choosing, KeepFlying, LoopBack }
    FinalRoute route = FinalRoute.None;
    float endlessSeconds, endlessStep;
    FinalChoicePanel choice;

    public FinalRoute Route { get { return route; } }
    // Seconds flown in KEEP FLYING (the endless escalation clock).
    public float EndlessSeconds { get { return endlessSeconds; } }

    // How long until this planet's portal opens. Other systems pace themselves
    // against the level clock -- the blue-atom budget saves one for the end.
    public float SecondsLeftInWorld { get { return Mathf.Max(0f, timer); } }
    public float WorldLength { get { return secondsPerWorld; } }
    public bool PortalIsOpen { get { return portalOpen; } }

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

        timer = secondsPerWorld;
        WorldPainter.Apply(Current);
        WorldMusic.Apply(Current);
        WorldBackdrop.Apply(Current, false);
        ApplyDifficulty(Current);
        WorldBanner.Show(Current.displayName);
        Codex.Discover(Codex.WorldId(CurrentIndex));
    }

    void Update()
    {
        // Only count time the player is actually flying, matching how the rest
        // of the game measures progress.
        bool running = !buttonClicks.playerDied &&
                       (TouchInput.IsPressed || score.pauseCounter <= 0);
        if (running) Tick(Time.deltaTime);
    }

    // One running frame of the level clock (`dt` of flight). Public so
    // edit-mode tests can step it (Time.deltaTime is 0 there).
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
        if (!HasNext && BossEncounter.DoneInWorld(CurrentIndex) && route != FinalRoute.LoopBack) return;

        timer -= dt;
        WorldMusic.TryEscalate(this);
        if (timer <= 0f) EndLevel();
    }

    // The level clock ran out (or developer mode skipped it): the world's
    // boss first, and the portal once the encounter is over. A boss already
    // dealt with on this visit (a missed portal coming round again) goes
    // straight to the portal.
    public void EndLevel()
    {
        timer = 0f;
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
        OfferFinalChoice();
    }

    // ---- after the final world ----------------------------------------------

    // The final world's boss is over (destroyed or survived): freeze and ask.
    public void OfferFinalChoice()
    {
        if (route == FinalRoute.Choosing || buttonClicks.playerDied) return;
        route = FinalRoute.Choosing;
        timer = 0f;
        choice = FinalChoicePanel.Show(CurrentIndex, RunLoop.StartWorld, RunLoop.Index, Choose);
    }

    // The panel's answer (a button, or KEEP FLYING when its countdown ends).
    public void Choose(bool loopBack)
    {
        if (route != FinalRoute.Choosing) return;
        if (choice != null) choice.Close();
        choice = null;
        if (loopBack)
        {
            route = FinalRoute.LoopBack;
            OpenPortal();
        }
        else
        {
            route = FinalRoute.KeepFlying;
            endlessSeconds = 0f;
            endlessStep = 0f;
            WorldBanner.Show("KEEP FLYING");
        }
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
            timer = 0f;
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
        timer = route == FinalRoute.LoopBack ? LoopRules.LoopPortalRetrySeconds : secondsPerWorld * 0.25f;
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

        // Speed resets on arrival (a little higher on each loop); pauses and
        // star dust deliberately carry over.
        moveBackGround.speed = LoopRules.ArrivalSpeed(RunLoop.Index);
        portalOpen = false;
        timer = secondsPerWorld;

        var theme = Current;
        ApplyDifficulty(theme);
        WorldPainter.Apply(theme);
        WorldMusic.Apply(theme);
        WorldBackdrop.Apply(theme, true);
        WorldBanner.Show(banner);
        Codex.Discover(Codex.WorldId(CurrentIndex));
    }

    static void ApplyDifficulty(WorldTheme theme)
    {
        ApplyScaledDifficulty(theme, RunLoop.Index, 0f);
    }

    // The world's ramp and caps, scaled for the loop and (KEEP FLYING) the
    // endless clock. LoopRules has every number; loop 0 is the world as-is.
    static void ApplyScaledDifficulty(WorldTheme theme, int loop, float endlessSeconds)
    {
        // Every wall, not just the first: SpeedRamp takes its rate and cap
        // from whichever instance ticks first in a frame.
        float rate = theme.speedRampPerSecond * LoopRules.RampScale(loop);
        float max = LoopRules.MaxSpeed(theme.maxSpeed, loop, endlessSeconds);
        foreach (var bg in Object.FindObjectsByType<moveBackGround>(FindObjectsSortMode.None))
        {
            bg.speedRampPerSecond = rate;
            bg.maxSpeed = max;
        }

        var enemies = Object.FindFirstObjectByType<enmiesOnBoard>();
        if (enemies != null) enemies.phaseRampScale = theme.enemyRampScale * LoopRules.PhaseRampScale(loop);
        // Read by the spawner (see LoopDifficulty for the one-line hook).
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
