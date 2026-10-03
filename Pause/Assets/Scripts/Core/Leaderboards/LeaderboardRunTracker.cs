using UnityEngine;
using UnityEngine.SceneManagement;

// Watches a real run (gameS1) and hands its scores to LeaderboardService when
// it ends. Added at runtime when gameS1 loads, so no gameplay script or scene
// has to know about leaderboards.
//
//   death          -> SubmitRun (the service keeps only improvements)
//   left mid-run   -> SubmitRun only if the run beat the local best speed
//   app backgrounded mid-run with a new best -> queued too (the app may be
//                     killed in the background); death later re-offers and
//                     the improvement rule drops the duplicate.
public class LeaderboardRunTracker : MonoBehaviour
{
    public const string RunScene = "gameS1";

    long peakSpeed;
    long bestAtStart;
    int furthestWorld;
    bool ended;

    void Start()
    {
        bestAtStart = Mathf.RoundToInt(PlayerPrefs.GetFloat("HighestSpeed"));
        peakSpeed = 0;
        furthestWorld = WorldManager.CurrentIndex;
        ended = false;
    }

    void Update()
    {
        if (ended) return;
        if (buttonClicks.playerDied)
        {
            End();
            return;
        }
        Sample();
    }

    void Sample()
    {
        peakSpeed = System.Math.Max(peakSpeed, (long)Mathf.Round(moveBackGround.speed * 100f));
        furthestWorld = Mathf.Max(furthestWorld, WorldManager.CurrentIndex);
    }

    public LeaderboardRunStats Stats()
    {
        return new LeaderboardRunStats
        {
            topSpeed = peakSpeed,
            starDust = StarDustLedger.Earned,
            worldIndex = furthestWorld,
        };
    }

    void End()
    {
        // The speed at the moment of death (what the death card shows) is
        // part of the run too.
        Sample();
        ended = true;
        LeaderboardService.Instance.SubmitRun(Stats());
    }

    // Leaving early (Menu, Replay, Back) keeps a new best, never anything else.
    void OnDestroy()
    {
        if (ended) return;
        ended = true;
        if (peakSpeed > bestAtStart) LeaderboardService.Instance.SubmitRun(Stats());
    }

    void OnApplicationPause(bool paused)
    {
        if (paused && !ended && peakSpeed > bestAtStart) LeaderboardService.Instance.SubmitRun(Stats());
    }

    // ---- bootstrap ----

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Init()
    {
        LeaderboardService.Install();
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (FindFirstObjectByType<LeaderboardRunner>() == null)
        {
            var go = new GameObject("~Leaderboards");
            go.hideFlags = HideFlags.HideInHierarchy;
            DontDestroyOnLoad(go);
            go.AddComponent<LeaderboardRunner>();
        }
        if (scene.name == RunScene)
            new GameObject("~LeaderboardRunTracker").AddComponent<LeaderboardRunTracker>();
    }
}

// Drives the service's debounced flush.
public class LeaderboardRunner : MonoBehaviour
{
    void Update()
    {
        if (LeaderboardService.HasInstance) LeaderboardService.Instance.Tick();
    }
}
