using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

// NEW BEST badges compare against the shared board (NewBestCheck, NewRecordTracker, DeathPanelView):
//   sharing on   the player's own shared row (first run = new best; equal / below = not; offline = cached row)
//   sharing off  the original saved best
//   one-time reset (NewBestReset) wipes only the best-score keys
//   the flight-complete panel and the HUD tracker show the badges when flagged
//
//   Unity -batchmode -quit -projectPath Pause -executeMethod NewBestTest.Run
public static class NewBestTest
{
    static int fails;
    static void Check(string what, bool ok)
    {
        Debug.Log((ok ? "[NBT] PASS  " : "[NBT] FAIL  ") + what);
        if (!ok) fails++;
    }

    public static void Run() { TestHarness.Exit(Execute()); }

    public static int Execute()
    {
        fails = 0;
        using var sandbox = new TestHarness.Sandbox();
        var shared = SharedLeaderboardService.HasInstance ? SharedLeaderboardService.Instance : null;
        var sharedMode = NewBestCheck.SharedMode;
        var known = NewBestCheck.KnownBest;
        try
        {
            SharedMode();
            SharingOff();
            OfflineFallback();
            Tracker();
            Reset();
            Panel();
        }
        finally
        {
            SharedLeaderboardService.Instance = shared;
            NewBestCheck.SharedMode = sharedMode;
            NewBestCheck.KnownBest = known;
            ClearKeys();
        }
        Debug.Log("[NBT] failures: " + fails);
        return fails;
    }

    static void ClearKeys()
    {
        foreach (string k in NewBestReset.Keys()) PlayerPrefs.DeleteKey(k);
        PlayerPrefs.DeleteKey(NewBestReset.FlagKey);
        PlayerPrefs.DeleteKey(SharedLeaderboardService.SharingKey);
    }

    static LeaderboardRunStats Stats(long score, float dust, int worlds, int powerUps = 0)
    {
        return new LeaderboardRunStats { score = score, starDust = dust, dustEarned = dust, worldsCleared = worlds, startShip = 3, startHearts = 3, powerUps = powerUps };
    }

    // A service over a fake backend whose row (submitted store) holds the given bests.
    static void Row(SharedTable table, long score, long dustHundredths, int worlds)
    {
        var store = new SharedRun { score = score, dust = dustHundredths, worlds = worlds, ship = 3, hearts = 3, endHearts = 1, pauses = 0 };
        var s = SharedLeaderboardService.Instance;
        // Seed through the real upload path of the fake: a committed upload fills the submitted store.
        var fake = (FakeSharedBoard)s.Backend;
        var doc = new SharedDoc();
        doc.Offer(store);
        fake.Seed(table, "uid-local", doc);
        PlayerPrefs.SetString(SharedLeaderboardService.SubmittedKey, JsonSubmitted(table, doc));
    }

    static string JsonSubmitted(SharedTable table, SharedDoc doc)
    {
        // the Store type is private: build the same JSON shape ({"main":{..},"power":{..}})
        string empty = JsonUtility.ToJson(new SharedDoc());
        string d = JsonUtility.ToJson(doc);
        return "{\"main\":" + (table == SharedTable.Main ? d : empty) + ",\"power\":" + (table == SharedTable.Power ? d : empty) + "}";
    }

    static void Begin(int savedBest = -1)
    {
        RunScore.EndRun(RunScore.RunId);   // bank the previous run first so it cannot move the saved best below
        if (savedBest >= 0) PlayerPrefs.SetInt(RunScore.BestScoreKey, savedBest);
        RunScore.BeginRun(true, true, false);
        RunScore.OnWorldCleared(1);   // some points so a local-best comparison has a run to judge
        NewBestCheck.Capture();
    }

    static void SharedMode()
    {
        ClearKeys();
        PlayerPrefs.SetInt(SharedLeaderboardService.SharingKey, 1);
        var fake = new FakeSharedBoard();
        SharedLeaderboardService.Instance = new SharedLeaderboardService(fake);
        NewBestCheck.SharedMode = NewBestCheck.DefaultSharedMode;
        NewBestCheck.KnownBest = (t, c) => SharedLeaderboardService.Instance.KnownBest(t, c);

        // First ever run: no row yet -> every column with a value is a new best.
        Begin();
        var f = NewBestCheck.Evaluate(Stats(500, 2f, 1));
        Check("sharing on, no row yet: score, dust and worlds are all new bests", f.score && f.dust && f.worlds);
        var zero = NewBestCheck.Evaluate(Stats(500, 0f, 0));
        Check("a column with no value is never a new best", zero.score && !zero.dust && !zero.worlds);

        // A row exists: compared per column.
        Row(SharedTable.Main, 1000, 500, 3);
        Begin(1);   // the local best is irrelevant now
        f = NewBestCheck.Evaluate(Stats(1001, 5.01f, 4));
        Check("above the shared row in every column: all new", f.score && f.dust && f.worlds);
        f = NewBestCheck.Evaluate(Stats(1000, 5f, 3));
        Check("equal to the shared row: not a new best", !f.score && !f.dust && !f.worlds);
        f = NewBestCheck.Evaluate(Stats(999, 4.99f, 2));
        Check("below the shared row: not a new best", !f.Any);
        f = NewBestCheck.Evaluate(Stats(1500, 1f, 3));
        Check("each column is judged on its own", f.score && !f.dust && !f.worlds);

        // The finished run is already queued when the panel opens: the baseline was taken at run start.
        Begin();
        Row(SharedTable.Main, 5000, 0, 0);
        f = NewBestCheck.Evaluate(Stats(4000, 0f, 0));
        Check("baseline is captured at run start (a row written later does not change it)", f.score);

        // Power-Ups runs use the Power table's row.
        Row(SharedTable.Main, 100000, 100000, 50);
        Row(SharedTable.Power, 200, 100, 1);
        Begin();
        f = NewBestCheck.Evaluate(Stats(300, 2f, 2, LeaderboardRunTag.PowerDust));
        Check("a boosted run is judged against the Power-Ups row, not Main", f.score && f.dust && f.worlds);

        // Never ranked: premium / developer / practice runs flag nothing.
        RunScore.BeginRun(true, true, true);
        NewBestCheck.Capture();
        Check("an unranked (premium) run never flags", !NewBestCheck.Evaluate(Stats(9999, 9f, 9)).Any);
        RunScore.BeginRun(false, false, false);
        Check("a practice run never flags", !NewBestCheck.Evaluate(Stats(9999, 9f, 9)).Any);
    }

    static void SharingOff()
    {
        ClearKeys();
        PlayerPrefs.SetInt(SharedLeaderboardService.SharingKey, 0);
        SharedLeaderboardService.Instance = new SharedLeaderboardService(new FakeSharedBoard());
        NewBestCheck.SharedMode = NewBestCheck.DefaultSharedMode;
        Check("sharing off: the shared mode is off", !NewBestCheck.SharedMode());

        Begin(0);
        var f = NewBestCheck.Evaluate(Stats(1, 50f, 5));
        Check("sharing off: compares against the saved best (beaten = new best); dust / worlds never flag", f.score && !f.dust && !f.worlds);

        Begin(999999);
        f = NewBestCheck.Evaluate(Stats(1, 50f, 5));
        Check("sharing off: a run below the saved best is not a new best (the shared row is ignored)", !f.Any);

        // Backend not available == original behaviour even with the switch on.
        PlayerPrefs.SetInt(SharedLeaderboardService.SharingKey, 1);
        SharedLeaderboardService.Instance = new SharedLeaderboardService(new FakeSharedBoard { Available = false });
        Check("no shared backend: original rule", !NewBestCheck.SharedMode());
    }

    static void OfflineFallback()
    {
        ClearKeys();
        PlayerPrefs.SetInt(SharedLeaderboardService.SharingKey, 1);
        var fake = new FakeSharedBoard { Fail = SharedResult.Error, SignedIn = false };
        SharedLeaderboardService.Instance = new SharedLeaderboardService(fake);
        NewBestCheck.KnownBest = (t, c) => SharedLeaderboardService.Instance.KnownBest(t, c);
        Row(SharedTable.Main, 800, 0, 0);
        Begin();
        Check("offline: the cached shared row still decides (below)", !NewBestCheck.Evaluate(Stats(700, 0f, 0)).score);
        Check("offline: the cached shared row still decides (above)", NewBestCheck.Evaluate(Stats(900, 0f, 0)).score);
        Check("offline: the check never touched the network", fake.LoadCalls == 0 && fake.UploadCalls == 0);

        // A throwing store must not block the results.
        NewBestCheck.KnownBest = (t, c) => { throw new System.Exception("store unreadable"); };
        Begin();
        bool ok = true;
        NewBestCheck.Flags f = default;
        try { f = NewBestCheck.Evaluate(Stats(10, 0f, 0)); } catch (System.Exception) { ok = false; }
        Check("an unreadable store falls back to a first-run baseline instead of throwing", ok && f.score);
        NewBestCheck.KnownBest = (t, c) => SharedLeaderboardService.Instance.KnownBest(t, c);
    }

    static void Tracker()
    {
        ClearKeys();
        PlayerPrefs.SetInt(SharedLeaderboardService.SharingKey, 1);
        var best = new System.Collections.Generic.Dictionary<string, long>();
        long scoreBest = 100, dustBest = 0, worldsBest = 2, powerScore = 50;
        var live = new LeaderboardRunStats();
        var t = new NewRecordTracker();
        t.Eligible = () => true;
        t.SharedMode = () => true;
        t.SharedBest = (tbl, col) => tbl == SharedTable.Power ? (col == SharedColumn.Score ? powerScore : 0)
            : col == SharedColumn.Score ? scoreBest : col == SharedColumn.Dust ? dustBest : worldsBest;
        t.Live = () => live;
        t.Begin();
        Check("shared mode watches score, dust and worlds in both tables", t.Watching == 6);
        live = Stats(100, 0f, 2);
        Check("equal to the shared row: nothing fires", t.Poll().Count == 0);
        live = Stats(101, 0.5f, 3);
        var fresh = t.Poll();
        Check("beating the row fires score, dust (first value) and worlds", t.Crossed(LeaderboardBoards.TopScore)
              && t.Crossed(LeaderboardBoards.RunStarDust) && t.Crossed(LeaderboardBoards.FurthestWorld) && fresh.Count == 3);
        Check("fires once per run", t.Poll().Count == 0);

        live = Stats(60, 0f, 0, LeaderboardRunTag.PowerDust);
        t.Begin();
        fresh = t.Poll();
        Check("a boosted run chases only the Power-Ups row (score 60 > 50)", fresh.Count == 1 && fresh[0] == LeaderboardBoards.BoostedRuns);

        // Sharing off: the original watch (Top Score from the saved best) is what runs.
        PlayerPrefs.SetInt(RunScore.BestScoreKey, 0);
        var off = new NewRecordTracker { Eligible = () => true, SharedMode = () => false };
        off.Baseline = (LeaderboardBoard b, out long v) => { v = 10; return b.id == LeaderboardBoards.TopScore; };
        off.Live = () => Stats(11, 0f, 0);
        off.Begin();
        Check("sharing off: the original baseline is used", off.Watching == 1 && off.Poll().Count == 1);
    }

    static void Reset()
    {
        ClearKeys();
        // Keys that must survive.
        string[] keep = { StarDustLedger.CurrencyKey, "BoughtShip3", "HasDoneTut", "ads_runCount", SharedLeaderboardService.SharingKey, SharedLeaderboardService.BoundUidKey };
        foreach (string k in keep) PlayerPrefs.SetInt(k, 7);
        foreach (string k in NewBestReset.Keys()) PlayerPrefs.SetInt(k, 9);
        bool ran = NewBestReset.Run();
        bool gone = true;
        foreach (string k in NewBestReset.Keys()) gone &= !PlayerPrefs.HasKey(k);
        bool kept = true;
        foreach (string k in keep) kept &= PlayerPrefs.GetInt(k, 0) == 7;
        Check("reset: runs once and deletes every listed key", ran && gone);
        Check("reset: dust, purchases, progress, settings and the uid are untouched", kept);
        Check("reset: the version flag is set", PlayerPrefs.GetInt(NewBestReset.FlagKey, 0) == 1);

        PlayerPrefs.SetInt(RunScore.BestScoreKey, 123);
        Check("reset: a second launch does nothing (a new best score survives)", !NewBestReset.Run() && PlayerPrefs.GetInt(RunScore.BestScoreKey, 0) == 123);
        Check("reset: exactly the expected keys", NewBestReset.Keys().Length == 13);
        foreach (string k in keep) PlayerPrefs.DeleteKey(k);
    }

    static void Panel()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/gameS1.unity", OpenSceneMode.Single);
        var canvas = SceneUtil.FindAny("PopUpCanvas");
        var best = SceneUtil.FindAny("playerDeadHighestSpeed").GetComponent<Text>();
        var run = SceneUtil.FindAny("deathSpeedReachedThisRoundText").GetComponent<Text>();
        var dust = SceneUtil.FindAny("playerDeadHighScore").GetComponent<Text>();
        var parts = new RunScore.Breakdown { distance = 1274, kills = 1375, killCount = 131, dust = 100, dustCount = 38, worlds = 1050, worldCount = 3 };
        var all = DeathPanelView.Build(canvas.transform, best, run, dust, SceneUtil.FindAny("Replay").GetComponent<Button>(),
            SceneUtil.FindAny("MainMenu").GetComponent<Button>(),
            new DeathPanelView.Results { score = parts.Total, bestScore = parts.Total, newBest = true, newDust = true, newWorlds = true, ranked = true, parts = parts, dustAtStart = 10f, dustWon = 2f });
        all.Skip();
        Check("panel: score badge when flagged", all.Panel.Find("Card0/NewBest") != null);
        Check("panel: star dust badge when flagged", all.Panel.Find("Card2/NewBestDust") != null);
        Check("panel: worlds badge when flagged", all.Panel.Find("Card1/Row6/NewBest") != null);
        Object.DestroyImmediate(all.gameObject);

        // Build moves the scene's texts and buttons into the panel, so destroying the first panel destroyed them: reopen the scene.
        EditorSceneManager.OpenScene("Assets/Scenes/gameS1.unity", OpenSceneMode.Single);
        canvas = SceneUtil.FindAny("PopUpCanvas");
        best = SceneUtil.FindAny("playerDeadHighestSpeed").GetComponent<Text>();
        run = SceneUtil.FindAny("deathSpeedReachedThisRoundText").GetComponent<Text>();
        dust = SceneUtil.FindAny("playerDeadHighScore").GetComponent<Text>();
        var none = DeathPanelView.Build(canvas.transform, best, run, dust, SceneUtil.FindAny("Replay").GetComponent<Button>(),
            SceneUtil.FindAny("MainMenu").GetComponent<Button>(),
            new DeathPanelView.Results { score = parts.Total, bestScore = parts.Total + 5, ranked = true, parts = parts, dustAtStart = 10f, dustWon = 2f });
        none.Skip();
        Check("panel: no badges when nothing is flagged",
              none.Panel.Find("Card0/NewBest") == null && none.Panel.Find("Card2/NewBestDust") == null && none.Panel.Find("Card1/Row6/NewBest") == null);
        Object.DestroyImmediate(none.gameObject);
    }
}
