using UnityEngine;
using UnityEngine.SceneManagement;

// Watches a real run (gameS1) and hands its scores to LeaderboardService when
// it ends. Added at runtime when gameS1 loads, so no gameplay script or scene
// has to know about leaderboards.
//
//   death          -> SubmitRun (the service keeps only improvements)
//   left mid-run   -> SubmitRun only if the run beat the local best score OF ITS ROUTE (see below)
//   app backgrounded mid-run with a new best -> queued too (the app may be
//                     killed in the background); death later re-offers and
//                     the improvement rule drops the duplicate.
//
// ROUTING. A run that used a booster (Dust x2 active during real flight at any moment, or a Hazard Clear charge that
// fired; latched for the run) goes ONLY to the Power-Ups board; every other run goes only to the normal boards
// (LeaderboardService.SubmitRun). The two routes keep their own local bests (LeaderboardService.LocalBest), so a boosted
// score never becomes the bar a normal early quit has to beat, and the other way round. RunScore.SavedBest is NOT used for
// this: it keeps every ranked run, boosted or not (score rules are unchanged).
public class LeaderboardRunTracker : MonoBehaviour
{
    public const string RunScene = "gameS1";

    long runScore;
    long normalBestAtStart, boostedBestAtStart;
    // Boosters the run has used so far (LeaderboardRunTag.Power* bits). Only ever grows, so it cannot reset mid-run
    // and a booster that expires mid-run still counts.
    int powerUps;
    float endDustAll;
    bool savesBest;   // RunScore.SavesBest at the last sample (the next scene's BeginRun resets it)
    int worldsCleared;
    bool ended;
    // Captured when the run starts, never re-read: what it STARTED with.
    int startShip, startHearts, startSkin;
    // The hearts left at the last sample: what the run FINISHED with. Cached
    // (like runScore) because the next scene resets the static hit counter.
    // The ship never changes mid-run, so only the hearts have a finish value.
    int endHearts;
    // The pauses left at the last sample (score.pauseCounter), cached for the same reason.
    int endPauses;
    // The star dust earned at the last sample: the next scene's BeginRun resets the ledger.
    float endDust;
    bool premiumRun;

    void Start()
    {
        LeaderboardService.SeedLocalBests();
        normalBestAtStart = LeaderboardService.LocalBest(false);
        boostedBestAtStart = LeaderboardService.LocalBest(true);
        powerUps = 0;
        endDustAll = 0f;
        savesBest = false;
        runScore = 0;
        worldsCleared = 0;
        ended = false;
        startShip = ShipId.Equipped();
        startHearts = ShipLives.Max(startShip);
        startSkin = ShipSkins.Equipped(startShip);
        premiumRun = PremiumShips.IsPremium(startShip) || RunScore.Unranked;
        endHearts = startHearts;
        endPauses = score.pauseCounter;
        endDust = 0f;
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
        // Worlds cleared = portals / lift-offs flown (the world-cleared event).
        worldsCleared = Mathf.Max(worldsCleared, RunScore.Parts.worldCount);
        // A run that started on the premium ship stays premium whatever the
        // selection becomes; one that ever flies it is premium from then on.
        if (RunScore.Unranked || PremiumShips.IsPremium(ShipId.Equipped())) premiumRun = true;
        // Cached rather than read at teardown: the next scene's run may
        // already have begun by then.
        endHearts = ShipLives.Left;
        endPauses = Mathf.Max(0, score.pauseCounter);
        endDust = System.Math.Max(endDust, Boosters.RankedDust(StarDustLedger.Earned));   // only grows within a run
        endDustAll = System.Math.Max(endDustAll, StarDustLedger.Earned);
        savesBest = RunScore.SavesBest;
        // The boosters, latched. Holding a charge or booster time without using it does not count.
        if (Boosters.RunDustUsed) powerUps |= LeaderboardRunTag.PowerDust;
        if (HazardClear.UsedThisRun > 0) powerUps |= LeaderboardRunTag.PowerClear;
        if (Boosters.RunHomingUsed) powerUps |= LeaderboardRunTag.PowerHoming;
        if (RunScore.Scoring) runScore = System.Math.Max(runScore, RunScore.Total);
    }

    // The route's own best: a boosted run is measured against boosted runs only, a normal run against normal ones.
    bool BeatLocalBest { get { return runScore > (powerUps != 0 ? boostedBestAtStart : normalBestAtStart); } }

    // The run is over for good (death or left early): its score becomes its route's local best, as a saved best would.
    void RecordBest()
    {
        if (!savesBest || premiumRun || LeaderboardService.SubmissionBlocked()) return;
        LeaderboardService.RecordLocalBest(powerUps != 0, runScore);
    }

    public LeaderboardRunStats Stats()
    {
        return new LeaderboardRunStats
        {
            score = runScore,
            starDust = endDust,
            worldsCleared = worldsCleared,
            startShip = startShip,
            startHearts = startHearts,
            startSkin = startSkin,
            hasEnd = true,
            endHearts = endHearts,
            hasPauses = true,
            endPauses = endPauses,
            premiumRun = premiumRun,
            powerUps = powerUps,
            dustEarned = endDustAll,
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
        RecordBest();
    }

    // Leaving early (Menu, Replay, Back) keeps a new best, never anything else.
    void OnDestroy()
    {
        if (ended) return;
        ended = true;
        if (BeatLocalBest) LeaderboardService.Instance.SubmitRun(Stats());
        RecordBest();
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
