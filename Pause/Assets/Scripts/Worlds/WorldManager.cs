using UnityEngine;
using UnityEngine.SceneManagement;

// Drives planet progression.
//
// You start on the space world. After enough active flight time a portal opens;
// flying through it moves you to the next planet.
//
// On transition: speed resets to zero, pauses and star dust carry over.
// Progress is remembered, so a later run starts on the furthest planet reached.
public class WorldManager : MonoBehaviour
{
    public const string PrefsCurrentWorld = "currentWorld";
    public const string PrefsHighestWorld = "highestWorld";

    [Tooltip("Seconds of active flight before the portal opens.")]
    public float secondsPerWorld = 480f;   // 8 minutes

    [Tooltip("How long the portal stays on screen before drifting off. Missing " +
             "it is not fatal -- another opens after the same interval.")]
    public float portalLifetime = 14f;

    [Tooltip("Off: every run starts at Space and the planets are a journey you " +
             "fly out through. On: runs resume at the furthest planet reached, " +
             "treating worlds as unlocked shortcuts.")]
    public bool startAtHighestUnlocked = false;

    public static WorldManager Instance { get; private set; }

    // Space first; the rest are generated planets. Index is the world number.
    public static readonly WorldTheme[] Worlds =
    {
        new WorldTheme {
            displayName = "Space", resourceFolder = "",
            portalColor = new Color(0.55f, 0.85f, 1f),
            speedRampPerSecond = 0.002f, maxSpeed = 0.60f,
        },
        new WorldTheme {
            displayName = "Frost", resourceFolder = "Frost",
            musicResource = "WorldMusic/Frost",
            portalColor = new Color(0.62f, 0.92f, 1f),
            speedRampPerSecond = 0.0024f, maxSpeed = 0.66f,
        },
        new WorldTheme {
            displayName = "Verdant", resourceFolder = "Verdant",
            musicResource = "WorldMusic/Verdant",
            portalColor = new Color(0.60f, 1f, 0.62f),
            speedRampPerSecond = 0.0028f, maxSpeed = 0.72f,
        },
        new WorldTheme {
            displayName = "Ember", resourceFolder = "Ember",
            musicResource = "WorldMusic/Ember",
            portalColor = new Color(1f, 0.62f, 0.35f),
            speedRampPerSecond = 0.0032f, maxSpeed = 0.80f,
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

    void Awake()
    {
        Instance = this;
    }

    void Start()
    {
        // Each run is a journey outward by default: begin at Space regardless of
        // how far a previous run got. The furthest planet reached is still
        // recorded, so progress is never lost -- it just is not a shortcut.
        CurrentIndex = startAtHighestUnlocked
            ? PlayerPrefs.GetInt(PrefsHighestWorld, 0)
            : 0;

        timer = secondsPerWorld;
        WorldPainter.Apply(Current);
        WorldMusic.Apply(Current);
        ApplyDifficulty(Current);
        WorldBanner.Show(Current.displayName);
    }

    void Update()
    {
        if (portalOpen || !HasNext) return;

        // Only count time the player is actually flying, matching how the rest
        // of the game measures progress.
        bool running = !buttonClicks.playerDied &&
                       (TouchInput.IsPressed || score.pauseCounter <= 0);
        if (!running) return;

        timer -= Time.deltaTime;
        if (timer <= 0f) OpenPortal();
    }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
    // Waiting eight minutes to test a portal is impractical. Never compiled
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
        Portal.Spawn(Current.portalColor, portalLifetime, OnPortalMissed);
    }

    void OnPortalMissed()
    {
        // Give the player another shot rather than stranding them.
        portalOpen = false;
        timer = secondsPerWorld * 0.25f;
    }

    // Called by Portal when the player flies through.
    public void Advance()
    {
        if (!HasNext) return;

        CurrentIndex = CurrentIndex + 1;

        // Speed resets on arrival; pauses and star dust deliberately carry over.
        moveBackGround.speed = 0f;

        var theme = Current;
        WorldPainter.Apply(theme);
        WorldMusic.Apply(theme);
        ApplyDifficulty(theme);
        WorldBanner.Show(theme.displayName);

        portalOpen = false;
        timer = secondsPerWorld;
    }

    static void ApplyDifficulty(WorldTheme theme)
    {
        var bg = Object.FindFirstObjectByType<moveBackGround>();
        if (bg == null) return;
        bg.speedRampPerSecond = theme.speedRampPerSecond;
        bg.maxSpeed = theme.maxSpeed;
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
