using UnityEngine;
using UnityEngine.SceneManagement;

// Watches a real run (gameS1) and hands its scores to LeaderboardService when
// it ends. Added at runtime when gameS1 loads, so no gameplay script or scene
// has to know about leaderboards.
//
//   death          -> SubmitRun (the service keeps only improvements)
//   left mid-run   -> SubmitRun only if the run beat the local best score
//   app backgrounded mid-run with a new best -> queued too (the app may be
//                     killed in the background); death later re-offers and
//                     the improvement rule drops the duplicate.
public class LeaderboardRunTracker : MonoBehaviour
{
    public const string RunScene = "gameS1";

    long runScore;
    long bestScoreAtStart;
    int furthestLoop;
    bool ended;
    // Captured when the run starts, never re-read: what it STARTED with.
    int startShip, startHearts;
    // The hearts left at the last sample: what the run FINISHED with. Cached
    // (like runScore) because the next scene resets the static hit counter.
    // The ship never changes mid-run, so only the hearts have a finish value.
    int endHearts;
    bool premiumRun;

    void Start()
    {
        bestScoreAtStart = RunScore.SavedBest;
        runScore = 0;
        furthestLoop = RunLoop.DisplayNumber;
        ended = false;
        startShip = ShipId.Equipped();
        startHearts = ShipLives.Max(startShip);
        premiumRun = PremiumShips.IsPremium(startShip) || RunScore.Unranked;
        endHearts = startHearts;
    }

    void Update()
    {
        if (ended) return;
        if (buttonClicks.playerDied)
        {
            // The death crash's DEATH COMBO is part of the run's score.
            if (!DeathCrash.PanelReady) return;
            End();
            return;
        }
        Sample();
    }

    void Sample()
    {
        furthestLoop = Mathf.Max(furthestLoop, RunLoop.DisplayNumber);
        // A run that started on the premium ship stays premium whatever the
        // selection becomes; one that ever flies it is premium from then on.
        if (RunScore.Unranked || PremiumShips.IsPremium(ShipId.Equipped())) premiumRun = true;
        // Cached rather than read at teardown: the next scene's run may
        // already have begun by then.
        endHearts = ShipLives.Left;
        if (RunScore.Scoring) runScore = System.Math.Max(runScore, RunScore.Total);
    }

    bool BeatLocalBest { get { return runScore > bestScoreAtStart; } }

    public LeaderboardRunStats Stats()
    {
        return new LeaderboardRunStats
        {
            score = runScore,
            starDust = StarDustLedger.Earned,
            loop = furthestLoop,
            startShip = startShip,
            startHearts = startHearts,
            hasEnd = true,
            endHearts = endHearts,
            premiumRun = premiumRun,
        };
    }

    void End()
    {
        // The speed at the moment of death (what the death card shows) is
        // part of the run too.
        Sample();
        if (buttonClicks.playerDied) endHearts = 0;   // the fatal hit may not be counted yet
        ended = true;
        LeaderboardService.Instance.SubmitRun(Stats());
    }

    // Leaving early (Menu, Replay, Back) keeps a new best, never anything else.
    void OnDestroy()
    {
        if (ended) return;
        ended = true;
        if (BeatLocalBest) LeaderboardService.Instance.SubmitRun(Stats());
    }

    void OnApplicationPause(bool paused)
    {
        if (!paused || ended) return;
        Sample();
        if (BeatLocalBest) LeaderboardService.Instance.SubmitRun(Stats());
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

// Drives the service's debounced flush, and on every return to the app retries
// a failed silent sign-in and sends what is still queued.
public class LeaderboardRunner : MonoBehaviour
{
    void OnApplicationFocus(bool focus)
    {
        if (!focus) return;
        AccountLink.RetrySilentOnResume();
        if (LeaderboardService.HasInstance) LeaderboardService.Instance.Flush();
    }

    void Update()
    {
        if (LeaderboardService.HasInstance) LeaderboardService.Instance.Tick();
    }
}
