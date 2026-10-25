using System.Linq;
using System.Collections.Generic;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

// Leaderboard foundation: board registry, submission rules (improvement only,
// debounced, offline queue flushed once on sign-in, nothing from developer
// mode or the tutorial, a send only counts once the board shows it, marks tied
// to the store player and a gated one-time backfill), and the in-game panel built from FakeLeaderboards in
// every state, at several screen shapes, opened from the real Options button.
public static class LeaderboardTest
{
    static int failures;

    static void Check(string what, bool ok)
    {
        Debug.Log((ok ? "[LB] PASS  " : "[LB] FAIL  ") + what);
        if (!ok) failures++;
    }

    public static void Run()
    {
        TestHarness.Exit(Execute());
    }

    public static int Execute()
    {
        failures = 0;
        using var sandbox = new TestHarness.Sandbox();

        // The real table (no speed board; the score board waits for its Play
        // Console id) ...
        Registry();
        RetiredSpeedBoard();
        // ... and a table with the score and world boards switched on, so the
        // submission rules and the panel have live boards to work on.
        LeaderboardBoards.OverrideForTests(LiveTable());
        try
        {
            ImprovementOnly();
            OfflineQueue();
            Blocked();
            RunTagsAndParagon();
            FurthestLoopShipHearts();
            ConfirmBeforeDone();
            AccountBinding();
            BackfillGates();
            PanelStates();
            SafeAreaFit();
            OptionsButton();
        }
        finally
        {
            LeaderboardBoards.OverrideForTests(null);
        }

        Debug.Log("[LB] failures: " + failures);
        return failures;
    }

    // ---- helpers ----

    static float now;

    static void RealRunContext()
    {
        PlayerPrefs.DeleteKey(LeaderboardService.PendingKey);
        PlayerPrefs.DeleteKey(LeaderboardService.SubmittedKey);
        PlayerPrefs.SetInt(DeveloperUnlocks.EnabledKey, 0);
        PlayerPrefs.SetString("HasDoneTut", "true");
        startMenu.youAreInTutorial = false;
        LeaderboardPanel.Close();
    }

    static LeaderboardService Service(FakeLeaderboards fake)
    {
        now = 0f;
        // No store player unless a test names one (account binding is off).
        var service = new LeaderboardService(fake, () => now) { Ios = false, PlayerId = () => null };
        LeaderboardService.Instance = service;
        return service;
    }

    // The live boards the submission and panel checks use: Top Score and
    // Furthest World with test ids on both stores, Star Dust still a
    // placeholder. (With the speed board retired, the real table has no
    // enabled board until the Play Console score id is pasted in.)
    const string TestScoreAndroid = "CgkTestTopScore", TestWorldAndroid = "CgkTestFurthestWorld";
    internal static LeaderboardBoard[] LiveTable()
    {
        return new[]
        {
            new LeaderboardBoard(LeaderboardBoards.TopScore, TestScoreAndroid, AchievementIds.IosPrefix + "top_score",
                "Top Score", "Best score in a single run.", LeaderboardSort.HigherIsBetter,
                LeaderboardBoards.FormatScore, r => r.score),
            new LeaderboardBoard(LeaderboardBoards.RunStarDust, "", "", "Star Dust",
                "Most star dust collected in a single run.", LeaderboardSort.HigherIsBetter,
                LeaderboardBoards.FormatHundredths, r => (long)System.Math.Round(r.starDust * 100.0)),
            new LeaderboardBoard(LeaderboardBoards.FurthestWorld, TestWorldAndroid, AchievementIds.IosPrefix + "furthest_world",
                "Furthest Loop", "Furthest loop reached in a single run.", LeaderboardSort.HigherIsBetter,
                LeaderboardBoards.FormatLoop, r => System.Math.Max(1, r.loop)),
        };
    }

    static string TopScoreId { get { return LeaderboardBoards.Get(LeaderboardBoards.TopScore).PlatformId(false); } }

    // ---- registry ----

    static void Registry()
    {
        var ids = new HashSet<string>();
        bool unique = true;
        foreach (var b in LeaderboardBoards.All) unique &= ids.Add(b.id);
        Check("registry: logical ids are unique", unique);

        bool both = true, named = true;
        foreach (var b in LeaderboardBoards.Enabled())
        {
            both &= !string.IsNullOrEmpty(b.PlatformId());
            named &= !string.IsNullOrEmpty(b.displayName) && !string.IsNullOrEmpty(b.description);
        }
        Check("registry: every enabled board has an id for the running platform", both);
        Check("registry: every enabled board has a name and description", named);

        // Speed is capped (SpeedRamp.Cap): there is no speed board any more.
        bool noSpeedBoard = LeaderboardBoards.Get(LeaderboardBoards.RetiredSpeedBoard) == null;
        foreach (var b in LeaderboardBoards.All)
            noSpeedBoard &= b.androidId != "CgkI3eXNjrQcEAIQAA" &&
                            !b.iosId.EndsWith("highest_speed") && !b.displayName.ToLowerInvariant().Contains("speed");
        Check("registry: no speed board (no top_speed id, no Highest Speed ids or name)", noSpeedBoard);
        Check("registry: AchievementIds has no entry for the retired speed board",
              AchievementCatalog.All.All(d => AchievementIds.AndroidId(d) != "CgkI3eXNjrQcEAIQAA"));
        var score = LeaderboardBoards.Get(LeaderboardBoards.TopScore);
        Check("Top Score is the first (primary) board", LeaderboardBoards.All.Length > 0 && LeaderboardBoards.All[0] == score);
        Check("Top Score uses me.hapticgate.pause.top_score on iOS",
              score != null && score.iosId == AchievementIds.IosPrefix + "top_score");

        var dust = LeaderboardBoards.Get(LeaderboardBoards.RunStarDust);
        var world = LeaderboardBoards.Get(LeaderboardBoards.FurthestWorld);
        Check("Android ids: Top Score / Star Dust / Furthest Loop",
              score.androidId == "CgkIopqxqbAPEAIQPg" && dust.androidId == "CgkIopqxqbAPEAIQPw" && world.androidId == "CgkIopqxqbAPEAIQQA");
        Check("a board is enabled per platform: all three on Android", score.EnabledOn(false) && dust.EnabledOn(false) && world.EnabledOn(false));
        Check("a board is enabled per platform: only Top Score on iOS until the other Game Center ids exist",
              score.EnabledOn(true) && !dust.EnabledOn(true) && !world.EnabledOn(true) &&
              dust.PlatformId(true) == null && world.PlatformId(true) == null && dust.PlatformId(false) == dust.androidId);
        Check("Furthest Loop: renamed, logical id kept",
              world.id == "furthest_world" && world.displayName == "Furthest Loop" && world.description == "Furthest loop reached in a single run.");
        Check("Furthest Loop: value is the loop number, no cap, 1 at least",
              world.Measure(new LeaderboardRunStats { loop = 1 }) == 1 && world.Measure(new LeaderboardRunStats { loop = 40 }) == 40 &&
              world.Measure(new LeaderboardRunStats()) == 1);
        Check("formatters: loop", LeaderboardBoards.FormatLoop(1) == "Loop 1" && LeaderboardBoards.FormatLoop(12) == "Loop 12");
        Check("formatters: hundredths", LeaderboardBoards.FormatHundredths(1234) == "12.34");

        var lower = new LeaderboardBoard("t", "a", "i", "T", "d", LeaderboardSort.LowerIsBetter, null, r => 0);
        Check("sort order: lower-is-better compares the other way", lower.IsBetter(5, 9) && !lower.IsBetter(9, 5));

        RealRunContext();
        var fake = new FakeLeaderboards();
        var service = Service(fake);
        Check("offer to an unknown/disabled board is skipped", !service.Offer("no_such_board", 500));
        long v;
        Check("nothing queued for a disabled board", !service.HasPending("no_such_board", out v));
        int accepted = service.SubmitRun(new LeaderboardRunStats { score = 500, starDust = 3f, loop = 2 });
        Check("a run end only queues enabled boards", accepted == LeaderboardBoards.Enabled().Count);
        Check("only enabled boards appear as panel tabs", service.UsableBoards().Count == LeaderboardBoards.Enabled().Count);
    }

    // A score an older build queued for the speed board while signed out is
    // dropped on the next flush and never sent; a run end offers score, star
    // dust and world only.
    static void RetiredSpeedBoard()
    {
        RealRunContext();
        LeaderboardBoards.OverrideForTests(LiveTable());
        try
        {
            var fake = new FakeLeaderboards { SignedIn = false };
            var service = Service(fake);
            var old = new LeaderboardService.ScoreTable();
            old.Set(LeaderboardBoards.RetiredSpeedBoard, 46);
            old.Save(LeaderboardService.PendingKey);
            long v;
            Check("retired speed board: an old build's queued value loads", service.HasPending(LeaderboardBoards.RetiredSpeedBoard, out v) && v == 46);
            fake.SignedIn = true;
            service.Flush();
            Check("retired speed board: dropped on flush, nothing sent", fake.Submissions.Count == 0 &&
                  !service.HasPending(LeaderboardBoards.RetiredSpeedBoard, out v));
            Check("retired speed board: an offer to it is refused", !service.Offer(LeaderboardBoards.RetiredSpeedBoard, 50));

            int accepted = service.SubmitRun(new LeaderboardRunStats { score = 1234, starDust = 2.5f, loop = 1 });
            now = 60f;
            service.Tick();
            bool onlyLive = true;
            foreach (var sub in fake.Submissions)
                onlyLive &= sub.board == TestScoreAndroid || sub.board == TestWorldAndroid;
            Check("a run end submits score and world (dust is a placeholder) and nothing else: " + accepted,
                  accepted == 2 && fake.Submissions.Count == 2 && onlyLive &&
                  fake.SubmissionsTo(TestScoreAndroid) == 1 && fake.SubmissionsTo(TestWorldAndroid) == 1);
        }
        finally
        {
            LeaderboardBoards.OverrideForTests(null);
        }
    }

    // ---- submission ----

    static void ImprovementOnly()
    {
        RealRunContext();
        var fake = new FakeLeaderboards();
        var service = Service(fake);

        Check("first score is accepted", service.Offer(LeaderboardBoards.TopScore, 100));
        service.Tick();
        Check("debounced: nothing sent straight away", fake.Submissions.Count == 0);
        now = LeaderboardService.DebounceSeconds + .01f;
        service.Tick();
        Check("sent after the debounce", fake.SubmissionsTo(TopScoreId) == 1 && fake.Submissions[0].score == 100);
        long v;
        Check("submitted value recorded", service.HasSubmitted(LeaderboardBoards.TopScore, out v) && v == 100);
        Check("queue empty after success", !service.HasPending(LeaderboardBoards.TopScore, out v));

        Check("a worse score is dropped", !service.Offer(LeaderboardBoards.TopScore, 90));
        Check("an equal score is dropped", !service.Offer(LeaderboardBoards.TopScore, 100));
        now += 10f;
        service.Tick();
        Check("nothing sent for non-improvements", fake.Submissions.Count == 1);

        // Two offers inside the debounce window: one submission, the better value.
        Check("improvement accepted", service.Offer(LeaderboardBoards.TopScore, 110));
        now += .5f;
        Check("further improvement accepted", service.Offer(LeaderboardBoards.TopScore, 115));
        now += .5f;
        service.Tick();
        Check("still waiting inside the window", fake.Submissions.Count == 1);
        now += LeaderboardService.DebounceSeconds;
        service.Tick();
        Check("one submission for the burst", fake.Submissions.Count == 2);
        Check("the burst sent its best value", fake.Submissions[1].score == 115);
        service.Tick();
        service.Flush();
        Check("flushing again sends nothing", fake.Submissions.Count == 2);

        // A run end goes through the same rule.
        service.SubmitRun(new LeaderboardRunStats { score = 80 });
        Check("run end below best: not queued", !service.HasPending(LeaderboardBoards.TopScore, out v));
        service.SubmitRun(new LeaderboardRunStats { score = 130 });
        Check("run end above best: queued",
              service.HasPending(LeaderboardBoards.TopScore, out v) && v == 130);
    }

    static void OfflineQueue()
    {
        RealRunContext();
        var fake = new FakeLeaderboards { SignedIn = false };
        var service = Service(fake);

        service.Offer(LeaderboardBoards.TopScore, 150);
        Check("signed out: a lower score than the pending one is dropped", !service.Offer(LeaderboardBoards.TopScore, 140));
        service.Offer(LeaderboardBoards.TopScore, 160);
        now = 60f;
        service.Tick();
        Check("signed out: nothing sent", fake.Submissions.Count == 0);

        var table = LeaderboardService.ScoreTable.Load(LeaderboardService.PendingKey);
        int forBoard = 0;
        foreach (var e in table.entries) if (e.board == LeaderboardBoards.TopScore) forBoard++;
        Check("queue persisted in PlayerPrefs under PendingKey", PlayerPrefs.HasKey(LeaderboardService.PendingKey));
        Check("queue holds one entry per board", forBoard == 1);
        long v;
        Check("queue holds the best pending value", table.TryGet(LeaderboardBoards.TopScore, out v) && v == 160);

        // Relaunch: a fresh service reads the same queue.
        var relaunched = Service(fake);
        Check("queue survives a relaunch", relaunched.HasPending(LeaderboardBoards.TopScore, out v) && v == 160);

        // A failed send keeps it queued.
        fake.SignedIn = true;
        fake.SubmitSucceeds = false;
        relaunched.Flush();
        Check("failed send stays queued", relaunched.HasPending(LeaderboardBoards.TopScore, out v) && v == 160);
        fake.SubmitSucceeds = true;
        fake.Submissions.Clear();

        // Sign-in success flushes, exactly once.
        LeaderboardService.Install();
        SocialBridge.NotifySignedIn();
        Check("sign-in flushes the queue", fake.SubmissionsTo(TopScoreId) == 1 && fake.Submissions[0].score == 160);
        SocialBridge.NotifySignedIn();
        LeaderboardService.Install();
        SocialBridge.NotifySignedIn();
        Check("a second sign-in sends nothing more", fake.Submissions.Count == 1);
        Check("queue cleared after the flush", !relaunched.HasPending(LeaderboardBoards.TopScore, out v));
        Check("pending key removed once empty", !PlayerPrefs.HasKey(LeaderboardService.PendingKey));
    }

    static void Blocked()
    {
        RealRunContext();
        var fake = new FakeLeaderboards();
        var service = Service(fake);
        var run = new LeaderboardRunStats { score = 999 };
        long v;

        PlayerPrefs.SetInt(DeveloperUnlocks.EnabledKey, 1);
        Check("developer mode: run not submitted", service.SubmitRun(run) == 0);
        Check("developer mode: offer refused", !service.Offer(LeaderboardBoards.TopScore, 999));
        service.SubmitRun(run);
        Check("developer mode: nothing queued", !service.HasPending(LeaderboardBoards.TopScore, out v));
        PlayerPrefs.SetInt(DeveloperUnlocks.EnabledKey, 0);

        startMenu.youAreInTutorial = true;
        Check("tutorial: run not submitted", service.SubmitRun(run) == 0);
        startMenu.youAreInTutorial = false;

        Check("the tutorial scene and its replay are tutorial runs",
              score.IsTutorialRun(score.TutorialScene, false) && score.IsTutorialRun("gameS1", true));
        Check("a game run is not a tutorial run", !score.IsTutorialRun("gameS1", false));

        // A skipped tutorial (it sets HasDoneTut) and one never flagged at all
        // (a lost flag, a cloud-restored or mid-way install) both still rank.
        PlayerPrefs.DeleteKey("HasDoneTut");
        TutorialSkip.FinishBySkipping();
        Check("skipped tutorial: not blocked", !LeaderboardService.SubmissionBlocked());
        Check("skipped tutorial: run submitted", service.SubmitRun(run) == LeaderboardBoards.Enabled().Count);
        PlayerPrefs.DeleteKey(LeaderboardService.PendingKey);
        PlayerPrefs.DeleteKey("HasDoneTut");
        Check("tutorial flag missing in a real run: not blocked", !LeaderboardService.SubmissionBlocked());
        PlayerPrefs.SetString("HasDoneTut", "false");
        Check("tutorial flag false in a real run: run submitted", service.SubmitRun(new LeaderboardRunStats { score = 1000 }) > 0);
        PlayerPrefs.SetString("HasDoneTut", "true");
        Check("completed tutorial: not blocked", !LeaderboardService.SubmissionBlocked());

        // Back to a clean queue for the checks below.
        PlayerPrefs.DeleteKey(LeaderboardService.PendingKey);
        PlayerPrefs.DeleteKey(LeaderboardService.SubmittedKey);
        service = Service(fake);

        now = 60f;
        service.Tick();
        service.Flush();
        Check("blocked runs never reach the store", fake.Submissions.Count == 0);
        Check("a real run is submitted (score and world)", service.SubmitRun(run) == LeaderboardBoards.Enabled().Count);
    }


    // ---- start ship + hearts, and the premium (cheat) ship ----

    static void RunTagsAndParagon()
    {
        // The tag format.
        Check("tag: encode s<ship>h<hearts>", LeaderboardRunTag.Encode(7, 4) == "s7h4");
        int ship, hearts;
        Check("tag: round trip", LeaderboardRunTag.TryParse("s7h4", out ship, out hearts) && ship == 7 && hearts == 4);
        Check("tag: Paragon is never encoded", LeaderboardRunTag.Encode(PremiumShips.Paragon, 10) == "");
        Check("tag: a Paragon tag reads as unknown", !LeaderboardRunTag.TryParse("s16h10", out ship, out hearts) && ship == 0 && hearts == 0 && LeaderboardRunTag.IsPremium("s16h10"));
        Check("tag: garbage is unknown", !LeaderboardRunTag.TryParse("hello", out ship, out hearts) && ship == 0 && hearts == 0
              && !LeaderboardRunTag.TryParse("", out ship, out hearts) && !LeaderboardRunTag.TryParse(null, out ship, out hearts)
              && !LeaderboardRunTag.TryParse("s7h", out ship, out hearts) && !LeaderboardRunTag.TryParse("s7h4x", out ship, out hearts));
        Check("tag: absurd hearts read as unknown hearts", LeaderboardRunTag.TryParse("s7h999", out ship, out hearts) && ship == 7 && hearts == 0);

        // End hearts: s<ship>h<start>e<end>, old tags still read (end unknown).
        int end;
        Check("tag: encode with end hearts", LeaderboardRunTag.Encode(7, 4, 2) == "s7h4e2" && LeaderboardRunTag.Encode(7, 4, 0) == "s7h4e0");
        Check("tag: unknown end leaves the old form", LeaderboardRunTag.Encode(7, 4) == "s7h4");
        Check("tag: end round trip", LeaderboardRunTag.TryParse("s7h4e2", out ship, out hearts, out end) && ship == 7 && hearts == 4 && end == 2);
        Check("tag: a death reads end 0", LeaderboardRunTag.TryParse("s7h4e0", out ship, out hearts, out end) && end == 0);
        Check("tag: old tag reads, end unknown", LeaderboardRunTag.TryParse("s7h4", out ship, out hearts, out end) && ship == 7 && hearts == 4 && end == -1);
        Check("tag: the 3-argument parse still reads an end tag", LeaderboardRunTag.TryParse("s7h4e2", out ship, out hearts) && ship == 7 && hearts == 4);
        Check("tag: bad end part is unknown", !LeaderboardRunTag.TryParse("s7h4e", out ship, out hearts) && !LeaderboardRunTag.TryParse("s7h4e2x", out ship, out hearts)
              && !LeaderboardRunTag.TryParse("s7h4f2", out ship, out hearts));
        Check("tag: absurd end hearts read as unknown end", LeaderboardRunTag.TryParse("s7h4e999", out ship, out hearts, out end) && hearts == 4 && end == -1);
        Check("tag: Paragon never encoded with an end either", LeaderboardRunTag.Encode(PremiumShips.Paragon, 10, 3) == ""
              && !LeaderboardRunTag.TryParse("s16h10e3", out ship, out hearts, out end) && end == -1 && LeaderboardRunTag.IsPremium("s16h10e3"));
        Check("tag: well under the 64-character limit, URL-safe", LeaderboardRunTag.Encode(15, ShipLives.MostHearts, ShipLives.MostHearts).Length < 20);

        // A normal run carries its start ship and hearts to the store.
        RealRunContext();
        var fake = new FakeLeaderboards();
        var service = Service(fake);
        var run = new LeaderboardRunStats { score = 500, loop = 1, startShip = 7, startHearts = 4, hasEnd = true, endHearts = 2 };
        Check("start ship run: accepted", service.SubmitRun(run) > 0);
        now = 60f;
        service.Tick();
        bool tagged = fake.Submissions.Count > 0;
        foreach (var sub in fake.Submissions) tagged &= sub.tag == "s7h4e2";
        Check("start ship run: every board's submission carries s7h4e2", tagged);
        Check("submitted record keeps the tag (companion for stores without tags)",
              LeadboardTag(LeaderboardService.SubmittedKey, LeaderboardBoards.TopScore) == "s7h4e2");

        // An old queue (written before tags existed) still loads and sends, tagless.
        RealRunContext();
        PlayerPrefs.SetString(LeaderboardService.PendingKey, "{\"entries\":[{\"board\":\"" + LeaderboardBoards.TopScore + "\",\"value\":77}]}");
        fake = new FakeLeaderboards();
        service = Service(fake);
        service.Flush();
        Check("old queue entry (no tag) is still sent, with an empty tag",
              fake.SubmissionsTo(TopScoreId) == 1 && fake.Submissions[0].score == 77 && fake.Submissions[0].tag == "");

        // Paragon: nothing reaches any board.
        RealRunContext();
        fake = new FakeLeaderboards();
        service = Service(fake);
        long v;
        var paragonRun = new LeaderboardRunStats { score = 9999, loop = 3, startShip = PremiumShips.Paragon, startHearts = 14 };
        Check("Paragon start: run not submitted", service.SubmitRun(paragonRun) == 0);
        var switched = new LeaderboardRunStats { score = 9999, loop = 3, startShip = 5, startHearts = 3, premiumRun = true };
        Check("started on Paragon, ship changed mid-run: not submitted (premiumRun)", service.SubmitRun(switched) == 0);
        Check("a premium tag is refused at the queue", !service.Offer(LeaderboardBoards.TopScore, 9999, "s16h14"));
        service.Flush();
        now = 120f;
        service.Tick();
        Check("Paragon runs: nothing queued, nothing sent",
              !service.HasPending(LeaderboardBoards.TopScore, out v) && fake.Submissions.Count == 0);

        // Paragon equipped (e.g. a run with a stale stats object): blocked at the single gate.
        PlayerPrefs.SetString(ShipId.OwnedKey(PremiumShips.Paragon), "True");
        PlayerPrefs.SetInt(ShipId.SelectedKey, PremiumShips.Paragon);
        Check("Paragon equipped: SubmissionBlocked", LeaderboardService.SubmissionBlocked());
        Check("Paragon equipped: even a clean-looking run is refused", service.SubmitRun(run) == 0);
        Check("Paragon equipped: Offer refused", !service.Offer(LeaderboardBoards.TopScore, 12345));
        // ... and a premium entry already in the queue is dropped, never sent.
        var table = new LeaderboardService.ScoreTable();
        table.Set(LeaderboardBoards.TopScore, 4242, "s16h10");
        table.Save(LeaderboardService.PendingKey);
        service.Flush();
        Check("a queued premium entry is dropped, not sent",
              fake.Submissions.Count == 0 && !service.HasPending(LeaderboardBoards.TopScore, out v));
        PlayerPrefs.SetInt(ShipId.SelectedKey, ShipId.Starter);
        PlayerPrefs.DeleteKey(ShipId.OwnedKey(PremiumShips.Paragon));

        // Local best and the score achievements: a Paragon run is unranked.
        PlayerPrefs.SetInt(RunScore.BestScoreKey, 100);
        int scoreCounter = AchievementStore.Counter(AchievementCatalog.CScore);
        int loopCounter = AchievementStore.Counter(AchievementCatalog.CLoop);
        Achievements.ForceReal = true;
        try
        {
            RunScore.BeginRun(true, true, true);
            Check("Paragon run: unranked, saves no best, still pays the dust score bonus",
                  RunScore.Unranked && !RunScore.SavesBest && RunScore.PaysScoreBonus);
            AchievementTracker.OnScore(999999);
            AchievementTracker.OnLoop(5);
            Check("Paragon run: no score or loop achievement progress",
                  AchievementStore.Counter(AchievementCatalog.CScore) == scoreCounter &&
                  AchievementStore.Counter(AchievementCatalog.CLoop) == loopCounter);
            RunScore.EndRun(RunScore.RunId);
            Check("Paragon run: the saved best is untouched", RunScore.SavedBest == 100);

            RunScore.BeginRun(true, true, false);
            Check("normal run: ranked", !RunScore.Unranked && RunScore.SavesBest);
            AchievementTracker.OnScore(1234);
            Check("normal run: score achievement still counts", AchievementStore.Counter(AchievementCatalog.CScore) >= 1234);
        }
        finally
        {
            Achievements.ForceReal = null;
            RunScore.BeginRun(false, false, false);
        }

        // Rows: ship icon + heart + count where known, blank where not.
        RealRunContext();
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        fake = new FakeLeaderboards();
        var mineNoTag = FakeLeaderboards.Entry(4, "KANEDA", 700, true);
        fake.SetBoard(TopScoreId,
            FakeLeaderboards.Entry(1, "TETSUO", 1000, false, 7, 4, 2),
            FakeLeaderboards.Entry(2, "KAI", 900),
            FakeLeaderboards.Entry(3, "CHEAT", 800, false, PremiumShips.Paragon, 14),
            mineNoTag);
        var companion = new LeaderboardService.ScoreTable();
        companion.Set(LeaderboardBoards.TopScore, 700, "s3h2e1");
        companion.Save(LeaderboardService.SubmittedKey);
        var panel = OpenPanel(fake);
        var rows = panel.PanelRoot.Find("List/Rows");
        Check("row with ship and hearts: both shown",
              rows.Find("Row0/Ship") != null && rows.Find("Row0/Hearts/Heart") != null &&
              rows.Find("Row0/Hearts/Count").GetComponent<Text>().text == "4");
        Check("old entry (no tag): blank, no ship and no hearts", rows.Find("Row1/Ship") == null && rows.Find("Row1/Hearts") == null);
        Check("a Paragon entry never shows the cheat ship", rows.Find("Row2/Ship") == null);
        Check("own entry: filled from the device's record of the submitted score",
              rows.Find("Row3/Ship") != null && rows.Find("Row3/Hearts/Count").GetComponent<Text>().text == "2");
        Check("an entry without end hearts shows only the start hearts", FakeLeaderboards.Entry(5, "OLD", 1, false, 3, 2).hasEnd == false);
        Check("row ships get a sprite to animate", rows.Find("Row0/Ship").GetComponent<Image>().sprite != null);
        var ownRow = panel.PanelRoot.Find("PlayerRow/Row");
        Check("own rank row also shows ship and hearts", ownRow != null && ownRow.Find("Ship") != null && ownRow.Find("Hearts") != null);

        // A score the store has that this device didn't make (different value) is not given this device's ship.
        fake.SetBoard(TopScoreId, FakeLeaderboards.Entry(1, "KANEDA", 750, true));
        panel = OpenPanel(fake);
        Check("own entry with another value: left blank", panel.PanelRoot.Find("List/Rows/Row0/Ship") == null);

        // The store did not flag the player's own top-list row as local: the player's rank finds it.
        var unflagged = FakeLeaderboards.Entry(1, "KANEDA", 700);
        unflagged.playerId = fake.LocalPlayerId;
        fake.SetBoard(TopScoreId, unflagged, FakeLeaderboards.Entry(2, "KAI", 700));
        panel = OpenPanel(fake);
        Check("own top entry not flagged local: still gets the tag by rank",
              panel.PanelRoot.Find("List/Rows/Row0/Ship") != null && panel.PanelRoot.Find("List/Rows/Row0/Hearts/Count").GetComponent<Text>().text == "2");
        Check("another player's entry with the same value: left blank", panel.PanelRoot.Find("List/Rows/Row1/Ship") == null);

        // Own rank is 2 with a different value: the other player's equal-valued row 1 stays blank.
        var other = FakeLeaderboards.Entry(1, "KAI", 700);
        var ownLower = FakeLeaderboards.Entry(2, "KANEDA", 650, true);
        fake.SetBoard(TopScoreId, other, ownLower);
        panel = OpenPanel(fake);
        Check("same value as the stored score, but not the player's rank: left blank", panel.PanelRoot.Find("List/Rows/Row0/Ship") == null);

        // Nothing kept for the submitted score: nothing to fill.
        var none = new LeaderboardService.ScoreTable();
        none.Set(LeaderboardBoards.TopScore, 700, "");
        none.Save(LeaderboardService.SubmittedKey);
        fake.SetBoard(TopScoreId, unflagged);
        panel = OpenPanel(fake);
        Check("tagless stored score: unflagged own entry stays blank", panel.PanelRoot.Find("List/Rows/Row0/Ship") == null);
    }

    // Furthest Loop rows carry the ship and hearts like every other board.
    static void FurthestLoopShipHearts()
    {
        string worldId = LeaderboardBoards.Get(LeaderboardBoards.FurthestWorld).PlatformId(false);

        // A run's loop submission carries the same tag as its score.
        RealRunContext();
        var fake = new FakeLeaderboards();
        var service = Service(fake);
        var run = new LeaderboardRunStats { score = 500, loop = 3, startShip = 7, startHearts = 4, hasEnd = true, endHearts = 1 };
        service.SubmitRun(run);
        now = 60f;
        service.Tick();
        bool sent = false; string tag = null;
        foreach (var sub in fake.Submissions) if (sub.board == worldId) { sent = true; tag = sub.tag; }
        Check("furthest loop: submitted with value 3 and the s7h4e1 tag", sent && tag == "s7h4e1" && fake.Submissions.Exists(x => x.board == worldId && x.score == 3));
        Check("furthest loop: record keeps the tag",
              LeadboardTag(LeaderboardService.SubmittedKey, LeaderboardBoards.FurthestWorld) == "s7h4e1");

        // Paragon: never encoded or sent on this board either.
        RealRunContext();
        fake = new FakeLeaderboards();
        service = Service(fake);
        var paragon = new LeaderboardRunStats { score = 9, loop = 5, startShip = PremiumShips.Paragon, startHearts = 14 };
        Check("furthest loop: Paragon run not submitted", service.SubmitRun(paragon) == 0 && fake.Submissions.Count == 0);
        long v;
        Check("furthest loop: premium tag refused", !service.Offer(LeaderboardBoards.FurthestWorld, 5, "s16h14")
              && !service.HasPending(LeaderboardBoards.FurthestWorld, out v));

        // Tying an old, tagless best now sends the tag (loops tie constantly); a tagged tie does not resend.
        RealRunContext();
        var old = new LeaderboardService.ScoreTable();
        old.Set(LeaderboardBoards.FurthestWorld, 3);
        old.Save(LeaderboardService.SubmittedKey);
        fake = new FakeLeaderboards();
        service = Service(fake);
        Check("furthest loop: a tie of a tagless best is queued with the tag", service.Offer(LeaderboardBoards.FurthestWorld, 3, "s7h4e1"));
        Check("furthest loop: a tie with no tag is still ignored", !service.Offer(LeaderboardBoards.FurthestWorld, 3));
        now = 120f;
        service.Tick();
        Check("furthest loop: the tie went out with its tag", fake.SubmissionsTo(worldId) == 1 && fake.Submissions[0].tag == "s7h4e1");
        Check("furthest loop: the tie updated the stored tag", LeadboardTag(LeaderboardService.SubmittedKey, LeaderboardBoards.FurthestWorld) == "s7h4e1");
        Check("furthest loop: once tagged, a tie is ignored", !service.Offer(LeaderboardBoards.FurthestWorld, 3, "s2h3e0"));
        Check("furthest loop: a lower loop is still ignored", !service.Offer(LeaderboardBoards.FurthestWorld, 2, "s2h3e0"));

        // Rows: the Furthest Loop tab shows ship + hearts, hides Paragon, and fills the own row.
        RealRunContext();
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        fake = new FakeLeaderboards();
        fake.SetBoard(worldId,
            FakeLeaderboards.Entry(1, "TETSUO", 5, false, 7, 4, 2),
            FakeLeaderboards.Entry(2, "KAI", 4),
            FakeLeaderboards.Entry(3, "CHEAT", 3, false, PremiumShips.Paragon, 14),
            FakeLeaderboards.Entry(4, "KANEDA", 2, true));
        var companion = new LeaderboardService.ScoreTable();
        companion.Set(LeaderboardBoards.FurthestWorld, 2, "s3h2e1");
        companion.Save(LeaderboardService.SubmittedKey);
        var panel = OpenPanel(fake);
        panel.SelectBoard(LeaderboardBoards.FurthestWorld);
        var rows = panel.PanelRoot.Find("List/Rows");
        Check("furthest loop tab: ship and hearts shown",
              rows.Find("Row0/Ship") != null && rows.Find("Row0/Hearts/Count").GetComponent<Text>().text == "4");
        Check("furthest loop tab: value formatted as a loop", rows.Find("Row0/Value").GetComponent<Text>().text == "Loop 5");
        Check("furthest loop tab: untagged entry blank", rows.Find("Row1/Ship") == null && rows.Find("Row1/Hearts") == null);
        Check("furthest loop tab: Paragon entry hidden", rows.Find("Row2/Ship") == null);
        Check("furthest loop tab: own entry filled from the device record",
              rows.Find("Row3/Ship") != null && rows.Find("Row3/Hearts/Count").GetComponent<Text>().text == "2");
    }

    static string LeadboardTag(string key, string board)
    {
        return LeaderboardService.ScoreTable.Load(key).TagOf(board);
    }

    // ---- confirming a send, account binding, backfill ----

    static void ConfirmContext()
    {
        RealRunContext();
        PlayerPrefs.DeleteKey(LeaderboardService.BoundPlayerKey);
        PlayerPrefs.DeleteKey(RunScore.BestScoreKey);
        PlayerPrefs.DeleteKey(AccountLink.DisconnectedKey);
    }

    static LeaderboardService PlayerService(FakeLeaderboards fake, string player)
    {
        var service = Service(fake);
        service.PlayerId = () => player;
        return service;
    }

    static LeaderboardPage PageWith(long value)
    {
        var page = new LeaderboardPage { status = LeaderboardStatus.Ok, hasPlayer = true };
        page.player = FakeLeaderboards.Entry(3, "ME", value, true);
        return page;
    }

    // What Play Games hands back when it stores the score but has no public rank for it.
    static LeaderboardPage Unranked(long value)
    {
        LeaderboardPage page = null;
        SocialScores.ToPage(null, new FakeScore { rank = value > 0 ? -1 : 0, value = value, userID = "ME" }, "ME", "Me", null, p => page = p);
        return page;
    }

    class FakeScore : UnityEngine.SocialPlatforms.IScore
    {
        public string leaderboardID { get; set; }
        public long value { get; set; }
        public System.DateTime date { get; set; }
        public string formattedValue { get; set; }
        public string userID { get; set; }
        public int rank { get; set; }
        public void ReportScore(System.Action<bool> callback) { }
    }

    static void ConfirmBeforeDone()
    {
        // The decision itself, no store involved.
        var high = LeaderboardBoards.Get(LeaderboardBoards.TopScore);
        var low = new LeaderboardBoard("t", "a", "i", "T", "d", LeaderboardSort.LowerIsBetter, null, r => 0);
        var none = new LeaderboardPage { status = LeaderboardStatus.Ok };
        Check("judge: board holds the value -> accepted", LeaderboardService.Judge(high, 100, PageWith(100)) == LeaderboardService.Verdict.Accepted);
        Check("judge: board holds a better value -> accepted", LeaderboardService.Judge(high, 100, PageWith(150)) == LeaderboardService.Verdict.Accepted);
        Check("judge: board holds a worse value -> rejected", LeaderboardService.Judge(high, 100, PageWith(90)) == LeaderboardService.Verdict.Rejected);
        Check("judge: no entry for the player -> rejected", LeaderboardService.Judge(high, 100, none) == LeaderboardService.Verdict.Rejected);
        Check("judge: unranked player entry (rank -1) holding the value -> accepted",
              LeaderboardService.Judge(high, 100, Unranked(100)) == LeaderboardService.Verdict.Accepted);
        Check("judge: unranked player entry with a worse value -> rejected",
              LeaderboardService.Judge(high, 100, Unranked(90)) == LeaderboardService.Verdict.Rejected);
        var empty = Unranked(0);
        Check("social scores: rank -1 with a value is the player's entry; an empty score is not",
              Unranked(100).hasPlayer && !empty.hasPlayer && LeaderboardService.Judge(high, 100, empty) == LeaderboardService.Verdict.Rejected);
        Check("judge: failed read -> unknown", LeaderboardService.Judge(high, 100, LeaderboardPage.Failed(LeaderboardStatus.Error)) == LeaderboardService.Verdict.Unknown);
        Check("judge: no page -> unknown", LeaderboardService.Judge(high, 100, null) == LeaderboardService.Verdict.Unknown);
        Check("judge: lower-is-better follows the board's order",
              LeaderboardService.Judge(low, 50, PageWith(40)) == LeaderboardService.Verdict.Accepted &&
              LeaderboardService.Judge(low, 50, PageWith(60)) == LeaderboardService.Verdict.Rejected);
        Check("backoff grows and is capped",
              LeaderboardService.BackoffSeconds(0) < LeaderboardService.BackoffSeconds(1) &&
              LeaderboardService.BackoffSeconds(1) < LeaderboardService.BackoffSeconds(3) &&
              LeaderboardService.BackoffSeconds(4) == LeaderboardService.BackoffSeconds(40) &&
              LeaderboardService.BackoffSeconds(0) >= 1f);

        // Accepted: the board holds the value, so it counts.
        ConfirmContext();
        var fake = new FakeLeaderboards();
        var service = Service(fake);
        long v;
        service.Offer(LeaderboardBoards.TopScore, 100);
        now = LeaderboardService.DebounceSeconds + .01f;
        service.Tick();
        Check("confirm: accepted send is marked submitted and leaves the queue",
              service.HasSubmitted(LeaderboardBoards.TopScore, out v) && v == 100 && !service.HasPending(LeaderboardBoards.TopScore, out v));

        // Silently refused (the plugin said true, the board never took it): stays queued,
        // is read again after a delay, then re-sent after a longer one, never in a loop.
        ConfirmContext();
        fake = new FakeLeaderboards { SilentlyRejects = true };
        service = Service(fake);
        service.Offer(LeaderboardBoards.TopScore, 200);
        now = LeaderboardService.DebounceSeconds + .01f;
        service.Tick();
        Check("refused: one send", fake.Submissions.Count == 1);
        Check("refused: not marked submitted, still queued",
              !service.HasSubmitted(LeaderboardBoards.TopScore, out v) && service.HasPending(LeaderboardBoards.TopScore, out v) && v == 200);
        now += 1f;
        service.Tick();
        service.Flush();
        Check("refused: no resend or re-read inside the first delay", fake.Submissions.Count == 1);
        now += LeaderboardService.BackoffSeconds(0);
        service.Tick();
        Check("refused: read again, then waits before re-sending", fake.Submissions.Count == 1);
        now += 5f;
        service.Tick();
        service.Flush();
        Check("refused: still waiting out the longer delay", fake.Submissions.Count == 1);
        fake.SilentlyRejects = false;   // the server takes it this time
        now += LeaderboardService.BackoffSeconds(1);
        service.Tick();
        Check("refused: re-sent after the backoff", fake.Submissions.Count == 2);
        Check("refused -> accepted on the retry: marked submitted, queue cleared",
              service.HasSubmitted(LeaderboardBoards.TopScore, out v) && v == 200 && !service.HasPending(LeaderboardBoards.TopScore, out v));
        now += 1000f;
        service.Tick();
        Check("accepted: nothing more is sent", fake.Submissions.Count == 2);

        // Read-back fails (offline): not marked, not re-sent, read again later.
        ConfirmContext();
        fake = new FakeLeaderboards { LoadsFail = true };
        service = Service(fake);
        service.Offer(LeaderboardBoards.TopScore, 300);
        now = LeaderboardService.DebounceSeconds + .01f;
        service.Tick();
        Check("unreadable: sent once, kept pending until confirmed",
              fake.Submissions.Count == 1 && !service.HasSubmitted(LeaderboardBoards.TopScore, out v) && service.HasPending(LeaderboardBoards.TopScore, out v));
        fake.LoadsFail = false;
        now += LeaderboardService.BackoffSeconds(0) + .01f;
        service.Tick();
        Check("unreadable -> readable: confirmed without a second send",
              fake.Submissions.Count == 1 && service.HasSubmitted(LeaderboardBoards.TopScore, out v) && v == 300 && !service.HasPending(LeaderboardBoards.TopScore, out v));

        // A read the store never answers times out and is retried; its late answer is ignored.
        ConfirmContext();
        fake = new FakeLeaderboards { DeferLoads = true };
        service = Service(fake);
        service.Offer(LeaderboardBoards.TopScore, 400);
        now = LeaderboardService.DebounceSeconds + .01f;
        service.Tick();
        Check("silent read: waiting, not marked", !service.HasSubmitted(LeaderboardBoards.TopScore, out v));
        now += LeaderboardService.SubmitTimeoutSeconds + 1f;
        service.Tick();
        fake.DeferLoads = false;
        fake.CompleteLoads();
        Check("silent read: the late answer is ignored", !service.HasSubmitted(LeaderboardBoards.TopScore, out v));
        now += LeaderboardService.BackoffSeconds(0) + .01f;
        service.Tick();
        Check("silent read: retried and confirmed", service.HasSubmitted(LeaderboardBoards.TopScore, out v) && v == 400 && fake.Submissions.Count == 1);
    }

    static void AccountBinding()
    {
        // Marks of unknown ownership are re-queued once for the first player seen, and sent once.
        ConfirmContext();
        long v;
        var old = new LeaderboardService.ScoreTable();
        old.Set(LeaderboardBoards.TopScore, 5000, "s7h4e1");
        old.Save(LeaderboardService.SubmittedKey);
        var fake = new FakeLeaderboards();
        var service = PlayerService(fake, "gp-A");
        service.Flush();
        Check("backfill: an old mark is sent again, with its tag",
              fake.SubmissionsTo(TopScoreId) == 1 && fake.Submissions[0].score == 5000 && fake.Submissions[0].tag == "s7h4e1");
        Check("backfill: the player is remembered", PlayerPrefs.GetString(LeaderboardService.BoundPlayerKey) == "gp-A");
        Check("backfill: confirmed, marked again", service.HasSubmitted(LeaderboardBoards.TopScore, out v) && v == 5000);
        service.Flush();
        now += 100f;
        service.Tick();
        Check("backfill: only once", fake.Submissions.Count == 1);
        var relaunch = PlayerService(new FakeLeaderboards(), "gp-A");
        relaunch.Flush();
        Check("backfill: not again on a relaunch for the same player", ((FakeLeaderboards)relaunch.Platform).Submissions.Count == 0);

        // Another player on the same device: the marks say nothing about their boards and are cleared, not re-sent.
        var other = new FakeLeaderboards();
        var switched = PlayerService(other, "gp-B");
        switched.Flush();
        Check("account switch: the old player's marks are cleared", !switched.HasSubmitted(LeaderboardBoards.TopScore, out v));
        Check("account switch: another player's score is not sent", other.Submissions.Count == 0);
        Check("account switch: bound to the new player", PlayerPrefs.GetString(LeaderboardService.BoundPlayerKey) == "gp-B");
        Check("account switch: the new player's scores queue normally", switched.Offer(LeaderboardBoards.TopScore, 400));

        // The saved best is offered once too (a best made before the board existed).
        ConfirmContext();
        PlayerPrefs.SetInt(RunScore.BestScoreKey, 777);
        fake = new FakeLeaderboards();
        service = PlayerService(fake, "gp-A");
        service.Flush();
        Check("backfill: the saved best score is sent", fake.SubmissionsTo(TopScoreId) == 1 && fake.Submissions[0].score == 777);
        service.Flush();
        Check("backfill: the saved best is sent once", fake.Submissions.Count == 1);

        // No player id yet: nothing is bound or sent.
        ConfirmContext();
        PlayerPrefs.SetInt(RunScore.BestScoreKey, 777);
        fake = new FakeLeaderboards();
        service = PlayerService(fake, null);
        service.Flush();
        Check("no player id: no backfill, nothing bound", fake.Submissions.Count == 0 && PlayerPrefs.GetString(LeaderboardService.BoundPlayerKey) == "");
    }

    static void BackfillGates()
    {
        // Developer mode: the backfill neither sends nor uses itself up.
        ConfirmContext();
        PlayerPrefs.SetInt(RunScore.BestScoreKey, 777);
        var marks = new LeaderboardService.ScoreTable();
        marks.Set(LeaderboardBoards.FurthestWorld, 9, "s7h4e1");
        marks.Save(LeaderboardService.SubmittedKey);
        PlayerPrefs.SetInt(DeveloperUnlocks.EnabledKey, 1);
        var fake = new FakeLeaderboards();
        var service = PlayerService(fake, "gp-A");
        service.Flush();
        now += 100f;
        service.Tick();
        long v;
        Check("backfill blocked in developer mode: nothing sent", fake.Submissions.Count == 0);
        Check("backfill blocked in developer mode: nothing queued or bound",
              !service.HasPending(LeaderboardBoards.TopScore, out v) && !service.HasPending(LeaderboardBoards.FurthestWorld, out v) &&
              PlayerPrefs.GetString(LeaderboardService.BoundPlayerKey) == "");
        PlayerPrefs.SetInt(DeveloperUnlocks.EnabledKey, 0);
        service.Flush();
        Check("backfill after developer mode is off: sent",
              fake.SubmissionsTo(TopScoreId) == 1 && fake.SubmissionsTo(LeaderboardBoards.Get(LeaderboardBoards.FurthestWorld).androidId) == 1);

        // Premium ship equipped: same.
        ConfirmContext();
        PlayerPrefs.SetInt(RunScore.BestScoreKey, 777);
        marks.Save(LeaderboardService.SubmittedKey);
        PlayerPrefs.SetString(ShipId.OwnedKey(PremiumShips.Paragon), "True");
        PlayerPrefs.SetInt(ShipId.SelectedKey, PremiumShips.Paragon);
        fake = new FakeLeaderboards();
        service = PlayerService(fake, "gp-A");
        service.Flush();
        now += 100f;
        service.Tick();
        Check("backfill blocked on the premium ship: nothing sent, nothing bound",
              fake.Submissions.Count == 0 && PlayerPrefs.GetString(LeaderboardService.BoundPlayerKey) == "");
        PlayerPrefs.SetInt(ShipId.SelectedKey, ShipId.Starter);
        PlayerPrefs.DeleteKey(ShipId.OwnedKey(PremiumShips.Paragon));

        // A premium-tagged mark is never re-queued, whoever flies now.
        ConfirmContext();
        var tainted = new LeaderboardService.ScoreTable();
        tainted.Set(LeaderboardBoards.TopScore, 9999, "s16h14");
        tainted.Save(LeaderboardService.SubmittedKey);
        fake = new FakeLeaderboards();
        service = PlayerService(fake, "gp-A");
        service.Flush();
        Check("backfill: a premium-tagged mark is not re-sent", fake.Submissions.Count == 0);

        // SIGN OUT in Pause: nothing is bound or sent until the player signs in again.
        ConfirmContext();
        PlayerPrefs.SetInt(RunScore.BestScoreKey, 777);
        PlayerPrefs.SetInt(AccountLink.DisconnectedKey, 1);
        fake = new FakeLeaderboards();
        service = PlayerService(fake, "gp-A");
        service.Flush();
        Check("disconnected: no backfill, nothing bound", fake.Submissions.Count == 0 && PlayerPrefs.GetString(LeaderboardService.BoundPlayerKey) == "");
        PlayerPrefs.DeleteKey(AccountLink.DisconnectedKey);
        service.Flush();
        Check("signed in again: the backfill runs", fake.SubmissionsTo(TopScoreId) == 1);
    }

    // ---- panel ----

    static readonly Vector2 Phone = new Vector2(1080f, 2340f);
    static readonly Rect PhoneSafe = new Rect(0f, 63f, 1080f, 2202f);

    static LeaderboardPanel OpenPanel(FakeLeaderboards fake)
    {
        var service = Service(fake);
        return LeaderboardPanel.Open(service, null, Phone, PhoneSafe);
    }

    static void PanelStates()
    {
        RealRunContext();
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        // Populated (player outside the top 10).
        var fake = FakeLeaderboards.Demo();
        var panel = OpenPanel(fake);
        Check("populated: state", panel.CurrentState == LeaderboardPanel.State.Populated);
        Check("populated: top 10 rows", panel.RowCount == 10);
        Check("populated: own rank row shown", panel.PlayerRowShown && panel.PlayerRankText == "#14");
        Check("populated: one tab per enabled board", panel.Tabs.Count == LeaderboardBoards.Enabled().Count);
        Check("populated: first tab selected", panel.SelectedBoard == LeaderboardBoards.TopScore);
        Check("populated: View all shown", panel.ViewAllButton.gameObject.activeSelf);
        var row0 = panel.PanelRoot.Find("List/Rows/Row0");
        var shape0 = row0 != null ? row0.GetComponent<CelShape>() : null;
        Check("populated: other players' rows are card-coloured", shape0 != null && shape0.fill == AkiraPalette.Card);
        var own = panel.PanelRoot.Find("PlayerRow/Row");
        var ownShape = own != null ? own.GetComponent<CelShape>() : null;
        Check("populated: own row is Kaneda red", ownShape != null && ownShape.fill == AkiraPalette.Red);

        // Player inside the top 10: their list row turns red too.
        var mine = new FakeLeaderboards();
        mine.SetBoard(TopScoreId, FakeLeaderboards.Entry(1, "TETSUO", 300), FakeLeaderboards.Entry(2, "KANEDA", 250, true),
                      FakeLeaderboards.Entry(3, "KAI", 200));
        panel = OpenPanel(mine);
        var row1 = panel.PanelRoot.Find("List/Rows/Row1");
        Check("top-10 player: list row highlighted red",
              row1 != null && row1.GetComponent<CelShape>().fill == AkiraPalette.Red);
        Check("top-10 player: rows match entries", panel.RowCount == 3 && panel.PlayerRankText == "#2");

        // A stored score the store gave no public rank (rank -1): shown without a rank, never "#-1".
        var unranked = new FakeLeaderboards();
        unranked.SetBoard(TopScoreId, FakeLeaderboards.Entry(-1, "KANEDA", 250, true));
        panel = OpenPanel(unranked);
        Check("unranked player: own row shows the score with a dash for the rank",
              panel.PlayerRowShown && panel.PlayerRankText == "#--");
        Check("unranked player: the private-profile note is shown, inside the player row",
              panel.UnrankedNote == LeaderboardPanel.UnrankedMessage("Fake") && panel.UnrankedNote.Contains("private") &&
              panel.PanelRoot.Find("PlayerRow/UnrankedNote") != null);
        Check("unranked note: Game Center wording differs and does not mention a private profile",
              LeaderboardPanel.UnrankedMessage("Game Center").Contains("Game Center") &&
              !LeaderboardPanel.UnrankedMessage("Game Center").Contains("private"));
        Check("ranked player: no unranked note", OpenPanel(mine).UnrankedNote == null);
        var noScore = new FakeLeaderboards();
        noScore.SetBoard(TopScoreId, FakeLeaderboards.Entry(1, "TETSUO", 300));
        panel = OpenPanel(noScore);
        Check("no player entry: no unranked note", panel.UnrankedNote == null && panel.PanelRoot.Find("PlayerRow/UnrankedNote") == null);

        // 25 entries: exactly the top 10 in rank order, plus the player's own row when they rank outside it.
        var big = new FakeLeaderboards();
        var entries = new LeaderboardEntry[25];
        for (int i = 0; i < 25; i++)
            entries[i] = i == 17 ? FakeLeaderboards.Entry(i + 1, "KANEDA", 1000 - i * 10, true)
                                 : FakeLeaderboards.Entry(i + 1, "PILOT" + (i + 1), 1000 - i * 10);
        big.SetBoard(TopScoreId, entries);
        panel = OpenPanel(big);
        bool ordered = true;
        for (int i = 0; i < 10; i++)
        {
            var r = panel.PanelRoot.Find("List/Rows/Row" + i + "/Rank");
            var t = r != null ? r.GetComponent<UnityEngine.UI.Text>() : null;
            ordered &= t != null && t.text == "#" + (i + 1);
        }
        Check("25 entries: exactly 10 rows", panel.RowCount == 10 && panel.PanelRoot.Find("List/Rows/Row9") != null &&
              panel.PanelRoot.Find("List/Rows/Row10") == null);
        Check("25 entries: rows are ranks 1 to 10 in order", ordered);
        Check("25 entries: own row #18 shown below the list", panel.PlayerRowShown && panel.PlayerRankText == "#18");
        entries[17] = FakeLeaderboards.Entry(18, "PILOT18", 820);
        entries[3] = FakeLeaderboards.Entry(4, "KANEDA", 960, true);
        big.SetBoard(TopScoreId, entries);
        panel = OpenPanel(big);
        Check("25 entries, player in the top 10: still 10 rows and own rank shown",
              panel.RowCount == 10 && panel.PlayerRankText == "#4");

        // Signed out -> one tap signs in, then loads.
        var signedOut = FakeLeaderboards.Demo();
        signedOut.SignedIn = false;
        panel = OpenPanel(signedOut);
        Check("signed out: state", panel.CurrentState == LeaderboardPanel.State.SignedOut);
        Check("signed out: message", panel.MessageText == "SIGN IN TO SEE RANKINGS");
        Check("signed out: sign-in button", panel.ActionButton != null);
        Check("signed out: no View all", !panel.ViewAllButton.gameObject.activeSelf);
        panel.ActionButton.onClick.Invoke();
        Check("signed out: tap runs one interactive sign-in", signedOut.SignInCalls == 1);
        Check("signed out: rankings load after sign-in", panel.CurrentState == LeaderboardPanel.State.Populated);

        // Sign-in refused: back to signed out.
        var refused = FakeLeaderboards.Demo();
        refused.SignedIn = false;
        refused.SignInSucceeds = false;
        panel = OpenPanel(refused);
        panel.ActionButton.onClick.Invoke();
        Check("sign-in refused: stays signed out", panel.CurrentState == LeaderboardPanel.State.SignedOut);

        // Loading.
        var slow = FakeLeaderboards.Demo();
        slow.DeferLoads = true;
        panel = OpenPanel(slow);
        Check("loading: state", panel.CurrentState == LeaderboardPanel.State.Loading);
        Check("loading: no rows", panel.RowCount == 0 && !panel.PlayerRowShown);
        slow.CompleteLoads();
        Check("loading -> populated", panel.CurrentState == LeaderboardPanel.State.Populated && panel.RowCount == 10);

        // Empty.
        var empty = new FakeLeaderboards();
        panel = OpenPanel(empty);
        Check("empty: state", panel.CurrentState == LeaderboardPanel.State.Empty);
        Check("empty: message", panel.MessageText == "NO SCORES YET");

        // Error / offline, with retry.
        var broken = FakeLeaderboards.Demo();
        broken.LoadsFail = true;
        panel = OpenPanel(broken);
        Check("error: state", panel.CurrentState == LeaderboardPanel.State.Error);
        Check("error: retry button", panel.ActionButton != null);
        broken.LoadsFail = false;
        panel.ActionButton.onClick.Invoke();
        Check("error: retry loads", panel.CurrentState == LeaderboardPanel.State.Populated);

        // No store on this device (editor, Mac).
        panel = LeaderboardPanel.Open(Service(null), null, Phone, PhoneSafe);
        Check("null platform: unavailable state", panel.CurrentState == LeaderboardPanel.State.Unavailable);

        // A stale load for a tab the player left is ignored.
        var stale = FakeLeaderboards.Demo();
        stale.DeferLoads = true;
        panel = OpenPanel(stale);                        // request 1: waits
        var saved = stale.Boards[TopScoreId];
        stale.DeferLoads = false;
        stale.SetBoard(TopScoreId);
        panel.Reload();                                  // request 2: answers at once, empty
        stale.Boards[TopScoreId] = saved;
        stale.CompleteLoads();                           // request 1 answers late, populated
        Check("a stale answer doesn't overwrite the newer one", panel.CurrentState == LeaderboardPanel.State.Empty);

        LeaderboardPanel.Close();
        Check("close removes the panel", !LeaderboardPanel.IsOpen);
    }

    static void SafeAreaFit()
    {
        RealRunContext();
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var cases = new[]
        {
            ("9:16 phone", new Vector2(1080, 1920), new Rect(0, 0, 1080, 1920)),
            ("9:19.5 notch", new Vector2(1170, 2532), new Rect(0, 102, 1170, 2361)),
            ("9:20 punch-hole", new Vector2(1080, 2400), new Rect(0, 0, 1080, 2310)),
            ("9:21 tall", new Vector2(1080, 2520), new Rect(0, 90, 1080, 2340)),
            ("9:22 tall", new Vector2(1080, 2640), new Rect(0, 90, 1080, 2460)),
            ("9:24 tall", new Vector2(1080, 2880), new Rect(0, 90, 1080, 2700)),
            ("Z Fold cover", new Vector2(968, 2376), new Rect(0, 60, 968, 2226)),
            ("3:4 tablet", new Vector2(1536, 2048), new Rect(0, 40, 1536, 1968)),
            ("16:9 landscape", new Vector2(1920, 1080), new Rect(0, 0, 1920, 1080)),
            ("landscape notch", new Vector2(2532, 1170), new Rect(141, 63, 2250, 1107)),
            ("small 2:3", new Vector2(640, 960), new Rect(0, 0, 640, 960)),
        };
        foreach (var (name, screen, safe) in cases)
        {
            var layout = LeaderboardPanel.ComputeLayout(screen, safe);
            var p = layout.panelPixels;
            bool inside = p.xMin >= safe.xMin - .5f && p.xMax <= safe.xMax + .5f &&
                          p.yMin >= safe.yMin - .5f && p.yMax <= safe.yMax + .5f;
            Check(name + ": panel inside the safe area", inside);
            bool big = p.width >= safe.width * .5f || p.height >= safe.height * .9f;
            Check(name + ": panel uses the space (not tiny)", big);
            Check(name + ": row type stays readable (>= 18 px)", 24f * layout.scale >= 18f || screen.x < 700);

            var panel = LeaderboardPanel.Open(Service(FakeLeaderboards.Demo()), null, screen, safe);
            var a = panel.SafeRoot;
            bool anchors = Mathf.Abs(a.anchorMin.x - safe.xMin / screen.x) < 1e-4f &&
                           Mathf.Abs(a.anchorMax.y - safe.yMax / screen.y) < 1e-4f &&
                           Mathf.Abs(a.anchorMin.y - safe.yMin / screen.y) < 1e-4f &&
                           Mathf.Abs(a.anchorMax.x - safe.xMax / screen.x) < 1e-4f;
            Check(name + ": built panel is anchored to the safe area", anchors);
            var scaler = panel.GetComponent<CanvasScaler>();
            Check(name + ": canvas scale applied", scaler != null && Mathf.Approximately(scaler.scaleFactor, layout.scale));
            Check(name + ": everything drawn stays inside the panel", ContentInsidePanel(panel));
        }
        LeaderboardPanel.Close();
    }

    // Every rect under the panel, in panel units, inside the panel (plus the
    // few units a hard cel shadow reaches out).
    static bool ContentInsidePanel(LeaderboardPanel panel)
    {
        var root = panel.PanelRoot;
        float hx = panel.PanelSize.x * .5f + 10f, hy = panel.PanelSize.y * .5f + 10f;
        var corners = new Vector3[4];
        foreach (var rt in root.GetComponentsInChildren<RectTransform>(true))
        {
            if (rt == root) continue;
            // rows inside the scrolling list are clipped to it (the list itself is checked)
            if (rt.parent != null && rt.GetComponentInParent<RectMask2D>() != null && rt.GetComponent<RectMask2D>() == null) continue;
            rt.GetWorldCorners(corners);
            foreach (var c in corners)
            {
                var local = root.InverseTransformPoint(c);
                if (Mathf.Abs(local.x) > hx || Mathf.Abs(local.y) > hy)
                {
                    Debug.Log("[LB] outside the panel: " + rt.name + " at " + local);
                    return false;
                }
            }
        }
        return true;
    }

    static void OptionsButton()
    {
        RealRunContext();
        // The leaderboard moved from Options to the home screen (2026-10-12).
        EditorSceneManager.OpenScene("Assets/Scenes/leaderboardS3.unity", OpenSceneMode.Single);
        Check("Options no longer has a LeaderBoard button",
              GameObject.Find("pullUpLeaderBoard") == null && GameObject.Find("LeaderboardButton") == null);
        EditorSceneManager.OpenScene("Assets/Scenes/startS4.unity", OpenSceneMode.Single);
        var fake = FakeLeaderboards.Demo();
        Service(fake);

        var go = GameObject.Find("LeaderboardButton");
        var button = go != null ? go.GetComponent<Button>() : null;
        Check("home has the Leaderboard button", button != null);
        if (button == null) return;
        Check("... in the main menu column", go.transform.parent != null && go.transform.parent.name == "UIPanel");
        int calls = button.onClick.GetPersistentEventCount();
        Object target = calls > 0 ? button.onClick.GetPersistentTarget(0) : null;
        Check("Leaderboard button calls startMenu.leaderboard",
              calls == 1 && target is startMenu && button.onClick.GetPersistentMethodName(0) == "leaderboard");
        var caption = go.GetComponentInChildren<Text>(true);
        Check("... and reads Leaderboard", caption != null && caption.text == "Leaderboard");

        // Persistent calls are runtime-only; invoke the same method directly.
        ((startMenu)target).leaderboard();
        var panel = LeaderboardPanel.Current;
        Check("LeaderBoard button opens the in-game panel", panel != null);
        if (panel == null) return;
        Check("panel is populated from the fake", panel.CurrentState == LeaderboardPanel.State.Populated);
        Check("panel uses the scene's Orbitron",
              panel.GetComponentInChildren<Text>().font != null &&
              panel.GetComponentInChildren<Text>().font.name.Contains("Orbitron"));
        Check("panel sits above the home canvas", panel.GetComponent<Canvas>().sortingOrder > GameObject.Find("MainMenuCanvas").GetComponent<Canvas>().sortingOrder);

        panel.ViewAllButton.onClick.Invoke();
        Check("View all opens the native UI for the selected board",
              fake.NativeUICalls.Count == 1 && fake.NativeUICalls[0] == TopScoreId);

        fake.SignedIn = false;
        fake.NativeUICalls.Clear();
        panel.ViewAllButton.onClick.Invoke();
        Check("View all signed out: signs in first, then opens",
              fake.SignInCalls == 1 && fake.NativeUICalls.Count == 1);

        Check("back closes the panel instead of leaving Options", LeaderboardPanel.CloseIfOpen());
        Check("panel gone", !LeaderboardPanel.IsOpen && !LeaderboardPanel.CloseIfOpen());
    }
}
