using System.Collections.Generic;
using System.Reflection;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

// The Power-Ups leaderboard (docs/leaderboards.md, "Power-Ups board"):
//   1  routing: a run that used a booster feeds ONLY the Power-Ups board, any other run never does
//   2  the latch: Dust x2 active in flight, expiring mid-run, a charge held vs fired, both
//   3  gates: Paragon, the tutorial, developer mode, a build with no store id for the board
//   4  local bests per route (early quits) and the backfill never offering a boosted score to Top Score
//   5  the score tag: round trip, the length limit at worst-case values, old tags, the normal boards' tags unchanged
//   6  the panel: columns with and without tag data, no hearts/pauses badges, font floors
public static class PowerUpsLeaderboardTest
{
    static int failures;

    static void Check(string what, bool ok)
    {
        Debug.Log((ok ? "[PU-LB] PASS  " : "[PU-LB] FAIL  ") + what);
        if (!ok) failures++;
    }

    public static void Run() { TestHarness.Exit(Execute()); }

    const string BoostedAndroid = "CgkTestBoosted";
    static float now;

    static LeaderboardBoard[] Table()
    {
        var list = new List<LeaderboardBoard>(LeaderboardTest.LiveTable());
        list.Add(new LeaderboardBoard(LeaderboardBoards.BoostedRuns, BoostedAndroid, "", "Boosted", "d", LeaderboardSort.HigherIsBetter,
                                      LeaderboardBoards.FormatScore, r => r.score, 1, true));
        return list.ToArray();
    }

    public static int Execute()
    {
        failures = 0;
        using var sandbox = new TestHarness.Sandbox();
        bool savedDied = buttonClicks.playerDied;
        int savedPauses = score.pauseCounter;
        try
        {
            RealShipTable();
            LeaderboardBoards.OverrideForTests(Table());
            Routing();
            Latch();
            Gates();
            MissingStoreId();
            LocalBests();
            Tags();
            Panel();
        }
        finally
        {
            LeaderboardBoards.OverrideForTests(null);
            buttonClicks.playerDied = savedDied;
            score.pauseCounter = savedPauses;
            Boosters.TestDelta = null;
            foreach (string k in new[] { Boosters.DustBoughtKey, Boosters.DustUsedMsKey, Boosters.ClearBoughtKey, Boosters.ClearUsedKey, Boosters.HomingBoughtKey, Boosters.HomingUsedMsKey }) PlayerPrefs.DeleteKey(k);
            Boosters.Reload();
            Boosters.ResetRun();
            HazardClear.BeginRun();
            LeaderboardPanel.Close();
        }
        Debug.Log("[PU-LB] failures: " + failures);
        return failures;
    }

    // ---- helpers ----

    static void Context()
    {
        PlayerPrefs.DeleteKey(LeaderboardService.PendingKey);
        PlayerPrefs.DeleteKey(LeaderboardService.SubmittedKey);
        PlayerPrefs.DeleteKey(LeaderboardService.NormalBestKey);
        PlayerPrefs.DeleteKey(LeaderboardService.BoostedBestKey);
        PlayerPrefs.DeleteKey(LeaderboardService.BoundPlayerKey);
        PlayerPrefs.DeleteKey(RunScore.BestScoreKey);
        PlayerPrefs.SetInt(DeveloperUnlocks.EnabledKey, 0);
        PlayerPrefs.SetString("HasDoneTut", "true");
        startMenu.youAreInTutorial = false;
        foreach (string k in new[] { Boosters.DustBoughtKey, Boosters.DustUsedMsKey, Boosters.ClearBoughtKey, Boosters.ClearUsedKey, Boosters.HomingBoughtKey, Boosters.HomingUsedMsKey }) PlayerPrefs.DeleteKey(k);
        Boosters.TestDelta = null;
        Boosters.Reload();
        Boosters.ResetRun();
        HazardClear.BeginRun();
        LeaderboardPanel.Close();
    }

    static LeaderboardService Service(FakeLeaderboards fake)
    {
        now = 0f;
        var service = new LeaderboardService(fake, () => now) { Ios = false, PlayerId = () => null };
        LeaderboardService.Instance = service;
        return service;
    }

    static string Id(string logical) { return LeaderboardBoards.Get(logical).PlatformId(false); }

    static bool Pending(LeaderboardService s, string board) { long v; return s.HasPending(board, out v); }

    static LeaderboardRunStats Run(long scoreValue, int powerUps, int ship = 7)
    {
        return new LeaderboardRunStats
        {
            score = scoreValue, starDust = 5f, dustEarned = 9.5f, worldsCleared = 3, startShip = ship, startHearts = 4,
            hasEnd = true, endHearts = 2, hasPauses = true, endPauses = 3, powerUps = powerUps,
        };
    }

    static void SetUsedThisRun(int n)
    {
        typeof(HazardClear).GetProperty("UsedThisRun").GetSetMethod(true).Invoke(null, new object[] { n });
    }

    static void Invoke(object target, string method, params object[] args)
    {
        target.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public).Invoke(target, args);
    }

    static void SetField(object target, string name, object value)
    {
        target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);
    }

    static T GetField<T>(object target, string name)
    {
        return (T)target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(target);
    }

    static void RealShipTable()
    {
        var real = LeaderboardBoards.Get(LeaderboardBoards.BoostedRuns);
        Check("real table: the Power-Ups board exists, last, powered-up only", real != null && real.poweredUpOnly
              && LeaderboardBoards.All[LeaderboardBoards.All.Length - 1] == real);
        Check("real table: its Android id is the Play Console id and its iOS id is empty, so it is on for Android only",
              real.androidId == "CgkIopqxqbAPEAIQQg" && real.iosId == "" && real.EnabledOn(false) && !real.EnabledOn(true));
        Check("real table: no other board is powered-up only", CountPoweredOnly() == 1);
        bool iosTab = false;
        foreach (var b in LeaderboardBoards.All) iosTab |= b.poweredUpOnly && b.EnabledOn(true);
        Check("real table: no iOS tab for it", !iosTab);
    }

    static int CountPoweredOnly()
    {
        int n = 0;
        foreach (var b in LeaderboardBoards.All) if (b.poweredUpOnly) n++;
        return n;
    }

    // ---- 1 routing ----

    static void Routing()
    {
        Context();
        var fake = new FakeLeaderboards();
        var service = Service(fake);
        int n = service.SubmitRun(Run(500, 0));
        Check("no booster: the normal boards take it (score + worlds)", n == 2 && Pending(service, LeaderboardBoards.TopScore)
              && Pending(service, LeaderboardBoards.FurthestWorld));
        Check("no booster: nothing for the Power-Ups board", !Pending(service, LeaderboardBoards.BoostedRuns));

        Context();
        service = Service(fake = new FakeLeaderboards());
        n = service.SubmitRun(Run(500, LeaderboardRunTag.PowerDust));
        Check("Dust x2: only the Power-Ups board is offered", n == 1 && Pending(service, LeaderboardBoards.BoostedRuns));
        Check("Dust x2: no leak into the normal boards' queue", !Pending(service, LeaderboardBoards.TopScore) && !Pending(service, LeaderboardBoards.FurthestWorld));

        foreach (int bits in new[] { LeaderboardRunTag.PowerClear, (LeaderboardRunTag.PowerDust | LeaderboardRunTag.PowerClear) })
        {
            Context();
            service = Service(fake = new FakeLeaderboards());
            n = service.SubmitRun(Run(700, bits));
            Check("power-ups " + bits + ": only the Power-Ups board", n == 1 && Pending(service, LeaderboardBoards.BoostedRuns)
                  && !Pending(service, LeaderboardBoards.TopScore) && !Pending(service, LeaderboardBoards.FurthestWorld));
        }

        // The send itself: score is the run score, the tag carries the columns.
        Context();
        service = Service(fake = new FakeLeaderboards());
        service.SubmitRun(Run(500, (LeaderboardRunTag.PowerDust | LeaderboardRunTag.PowerClear)));
        now = 60f;
        service.Tick();
        bool sent = fake.SubmissionsTo(BoostedAndroid) == 1 && fake.Submissions.Count == 1 && fake.Submissions[0].score == 500;
        Check("sent: one submission, to the Power-Ups board only, with the run score", sent);
        string tag = fake.Submissions.Count > 0 ? fake.Submissions[0].tag : "";
        Check("sent: tag is s7h4e2p3 + d950 w3 u3", tag == "s7h4e2p3d950w3u3");
        long v;
        Check("sent: confirmed and marked submitted on that board only", service.HasSubmitted(LeaderboardBoards.BoostedRuns, out v) && v == 500
              && !service.HasSubmitted(LeaderboardBoards.TopScore, out v));

        // A normal run afterwards: improves the normal boards, not the boosted one.
        service.SubmitRun(Run(450, 0));
        Check("a later normal run never touches the Power-Ups board", !Pending(service, LeaderboardBoards.BoostedRuns) && Pending(service, LeaderboardBoards.TopScore));

        // The normal boards' tag carries the run columns (d, w, r) and never a u.
        Context();
        service = Service(fake = new FakeLeaderboards());
        service.SubmitRun(Run(500, 0));
        now = 60f;
        service.Tick();
        bool plain = fake.Submissions.Count == 2;
        foreach (var s in fake.Submissions) plain &= s.tag == "s7h4e2p3d500w3r500";
        Check("normal boards: tag is s7h4e2p3 + d500 w3 r500, no power-ups field", plain);
    }

    // ---- 2 the latch ----

    static LeaderboardRunTracker NewTracker(GameObject go)
    {
        buttonClicks.playerDied = false;
        var t = go.AddComponent<LeaderboardRunTracker>();
        StarDustLedger.BeginRun(true);
        Invoke(t, "Start");
        return t;
    }

    static int Bits(LeaderboardRunTracker t) { return t.Stats().powerUps; }

    static void Latch()
    {
        var go = new GameObject("~pu-tracker");
        try
        {
            // no booster
            Context();
            Service(new FakeLeaderboards());
            var t = NewTracker(go);
            Boosters.Tick(1f);
            Invoke(t, "Sample");
            Check("latch: no booster, none used", Bits(t) == 0 && !t.Stats().PoweredUp);
            Object.DestroyImmediate(go); go = new GameObject("~pu-tracker");

            // Dust x2 owned and active, a real flight frame
            Context();
            PlayerPrefs.SetInt(Boosters.DustBoughtKey, 1);
            Boosters.Reload();
            Service(new FakeLeaderboards());
            t = NewTracker(go);
            Invoke(t, "Sample");
            Check("latch: Dust x2 owned but no flight frame yet: not used", Bits(t) == 0);
            Boosters.TestDelta = 1f;
            Boosters.Tick(1f);
            Invoke(t, "Sample");
            Check("latch: Dust x2 active during flight: used", Bits(t) == LeaderboardRunTag.PowerDust);
            // The next scene's Awake resets the booster flag; the tracker keeps its own latch.
            Boosters.ResetRun();
            Invoke(t, "Sample");
            Check("latch: stays set after the run statics reset", Bits(t) == LeaderboardRunTag.PowerDust);
            Object.DestroyImmediate(go); go = new GameObject("~pu-tracker");

            // Dust x2 expires mid-run
            Context();
            PlayerPrefs.SetInt(Boosters.DustBoughtKey, 1);
            PlayerPrefs.SetInt(Boosters.DustUsedMsKey, (int)(Boosters.DustBoosterSeconds * 1000f) - 400);
            Boosters.Reload();
            Service(new FakeLeaderboards());
            t = NewTracker(go);
            Boosters.TestDelta = 1f;
            Boosters.Tick(1f);   // the last 0.4 s: it runs out in this frame
            Check("latch: the booster really expired", !Boosters.DustActive);
            Invoke(t, "Sample");
            Check("latch: Dust x2 that expired mid-run still counts", Bits(t) == LeaderboardRunTag.PowerDust);
            Boosters.TestDelta = null;
            Object.DestroyImmediate(go); go = new GameObject("~pu-tracker");

            // charge held, unused
            Context();
            PlayerPrefs.SetInt(Boosters.ClearBoughtKey, 3);
            Boosters.Reload();
            Service(new FakeLeaderboards());
            t = NewTracker(go);
            Invoke(t, "Sample");
            Check("latch: a held Hazard Clear charge, unused: not powered-up", Boosters.ClearCharges == 3 && Bits(t) == 0);
            Object.DestroyImmediate(go); go = new GameObject("~pu-tracker");

            // charge fired
            SetUsedThisRun(1);
            t = NewTracker(go);
            Invoke(t, "Sample");
            Check("latch: a fired Hazard Clear charge counts", Bits(t) == LeaderboardRunTag.PowerClear);
            SetUsedThisRun(0);
            Invoke(t, "Sample");
            Check("latch: cannot reset mid-run", Bits(t) == LeaderboardRunTag.PowerClear);
            Object.DestroyImmediate(go); go = new GameObject("~pu-tracker");

            // both
            Context();
            PlayerPrefs.SetInt(Boosters.DustBoughtKey, 1);
            Boosters.Reload();
            Service(new FakeLeaderboards());
            t = NewTracker(go);
            Boosters.Tick(1f);
            SetUsedThisRun(2);
            Invoke(t, "Sample");
            Check("latch: both used -> both bits", Bits(t) == (LeaderboardRunTag.PowerDust | LeaderboardRunTag.PowerClear));
            SetUsedThisRun(0);
            Object.DestroyImmediate(go); go = new GameObject("~pu-tracker");

            // homing atoms: owned but not flown = nothing; flown = the HOMING bit
            Context();
            PlayerPrefs.SetInt(Boosters.HomingBoughtKey, 1);
            Boosters.Reload();
            Service(new FakeLeaderboards());
            t = NewTracker(go);
            Invoke(t, "Sample");
            Check("latch: homing time owned, not flown: not powered-up", Boosters.HomingActive && Bits(t) == 0);
            Boosters.Tick(1f);
            Invoke(t, "Sample");
            Check("latch: homing atoms flown -> the HOMING bit", Bits(t) == LeaderboardRunTag.PowerHoming);
            Object.DestroyImmediate(go); go = new GameObject("~pu-tracker");

            // a late booster: a normal snapshot at pause, the boosted end only to the Power-Ups board
            Context();
            RunScore.BeginRun(true, true, false);
            PlayerPrefs.SetInt(Boosters.ClearBoughtKey, 1);
            Boosters.Reload();
            var fake = new FakeLeaderboards();
            var service = Service(fake);
            t = NewTracker(go);
            SetField(t, "runScore", 300L);
            Invoke(t, "OnApplicationPause", true);   // backgrounded before any booster was used
            long v;
            Check("late booster: the pre-booster snapshot queued 300 on Top Score (an unboosted state)",
                  service.HasPending(LeaderboardBoards.TopScore, out v) && v == 300);
            SetUsedThisRun(1);
            SetField(t, "runScore", 900L);
            buttonClicks.playerDied = true;
            Invoke(t, "End");
            Check("late booster: the final 900 went to the Power-Ups board", service.HasPending(LeaderboardBoards.BoostedRuns, out v) && v == 900);
            Check("late booster: the normal boards still hold only the pre-booster 300",
                  service.HasPending(LeaderboardBoards.TopScore, out v) && v == 300);
            SetUsedThisRun(0);
        }
        finally
        {
            Boosters.TestDelta = null;
            buttonClicks.playerDied = false;
            Object.DestroyImmediate(go);
        }
    }

    // ---- 3 gates ----

    static void Gates()
    {
        Context();
        var fake = new FakeLeaderboards();
        var service = Service(fake);
        Check("Paragon + booster: submitted nowhere", service.SubmitRun(Run(900, LeaderboardRunTag.PowerAll, PremiumShips.Paragon)) == 0
              && !Pending(service, LeaderboardBoards.BoostedRuns) && !Pending(service, LeaderboardBoards.TopScore));
        var unranked = Run(900, LeaderboardRunTag.PowerDust);
        unranked.premiumRun = true;
        Check("unranked (premium) run + booster: submitted nowhere", service.SubmitRun(unranked) == 0 && !Pending(service, LeaderboardBoards.BoostedRuns));
        long v;
        Check("a premium-tagged offer to the Power-Ups board is refused", !service.Offer(LeaderboardBoards.BoostedRuns, 5, "s16h14d1w1u1")
              && !service.HasPending(LeaderboardBoards.BoostedRuns, out v));

        startMenu.youAreInTutorial = true;
        Check("tutorial/practice + booster: nowhere", service.SubmitRun(Run(900, LeaderboardRunTag.PowerDust)) == 0 && !Pending(service, LeaderboardBoards.BoostedRuns));
        startMenu.youAreInTutorial = false;

        PlayerPrefs.SetInt(DeveloperUnlocks.EnabledKey, 1);
        Check("developer mode + booster: nowhere", service.SubmitRun(Run(900, LeaderboardRunTag.PowerDust)) == 0);
        PlayerPrefs.SetInt(DeveloperUnlocks.EnabledKey, 0);

        // Paragon already queued would never be sent: the Flush net drops it.
        var table = new LeaderboardService.ScoreTable();
        table.Set(LeaderboardBoards.BoostedRuns, 50, "s16h14d1w1u1");
        table.Save(LeaderboardService.PendingKey);
        service.Flush();
        now = 60f;
        service.Tick();
        Check("a premium-tagged queued Power-Ups entry is dropped, not sent", fake.Submissions.Count == 0 && !service.HasPending(LeaderboardBoards.BoostedRuns, out v));
    }

    // A build whose Power-Ups board has no store id (the iOS state).
    static void MissingStoreId()
    {
        var shipped = new List<LeaderboardBoard>(LeaderboardTest.LiveTable());
        shipped.Add(new LeaderboardBoard(LeaderboardBoards.BoostedRuns, "", "", "Boosted", "d", LeaderboardSort.HigherIsBetter,
                                         LeaderboardBoards.FormatScore, r => r.score, 1, true));
        LeaderboardBoards.OverrideForTests(shipped.ToArray());
        try
        {
            Context();
            var fake = new FakeLeaderboards();
            var service = Service(fake);
            int n = service.SubmitRun(Run(800, LeaderboardRunTag.PowerDust));
            long v;
            Check("no store id: a powered-up run submits nowhere and does not fall back to the normal boards",
                  n == 0 && !Pending(service, LeaderboardBoards.TopScore) && !Pending(service, LeaderboardBoards.FurthestWorld)
                  && !Pending(service, LeaderboardBoards.BoostedRuns));
            now = 60f;
            service.Tick();
            Check("no store id: nothing reached the store", fake.Submissions.Count == 0);
            Check("no store id: no Power-Ups tab", service.UsableBoards().TrueForAll(b => !b.poweredUpOnly));
            Check("no store id: a normal run still goes to the normal boards", service.SubmitRun(Run(800, 0)) == 2);
            Check("no store id: Offer to the board is refused", !service.Offer(LeaderboardBoards.BoostedRuns, 5) && !service.HasPending(LeaderboardBoards.BoostedRuns, out v));
        }
        finally
        {
            LeaderboardBoards.OverrideForTests(Table());
        }
    }

    // ---- 4 local bests ----

    static void LocalBests()
    {
        var go = new GameObject("~pu-best");
        try
        {
            Context();
            PlayerPrefs.SetInt(RunScore.BestScoreKey, 1000);   // the saved best before any booster
            var fake = new FakeLeaderboards();
            var service = Service(fake);
            long v;

            // a boosted early quit below the saved best still ranks on its own board
            var t = NewTracker(go);
            SetField(t, "savesBest", true);
            SetField(t, "runScore", 300L);
            SetField(t, "powerUps", LeaderboardRunTag.PowerDust);
            Invoke(t, "OnDestroy");
            Check("early quit, boosted 300 under the normal best 1000: queued on the Power-Ups board", service.HasPending(LeaderboardBoards.BoostedRuns, out v) && v == 300);
            Check("early quit, boosted: nothing on the normal boards", !Pending(service, LeaderboardBoards.TopScore));
            Check("the boosted best is tracked on its own; the normal best is still 1000", LeaderboardService.LocalBest(true) == 300 && LeaderboardService.LocalBest(false) == 1000);
            Object.DestroyImmediate(go); go = new GameObject("~pu-best");

            // RunScore banked the boosted run as a saved best: the normal bar must not move
            PlayerPrefs.SetInt(RunScore.BestScoreKey, 5000);
            PlayerPrefs.DeleteKey(LeaderboardService.PendingKey);
            t = NewTracker(go);
            SetField(t, "savesBest", true);
            SetField(t, "runScore", 1200L);
            Invoke(t, "OnDestroy");
            Check("early quit, normal 1200 over the normal best (1000) but under RunScore.SavedBest (5000): still submitted",
                  service.HasPending(LeaderboardBoards.TopScore, out v) && v == 1200);
            Check("early quit, normal: nothing on the Power-Ups board", !Pending(service, LeaderboardBoards.BoostedRuns));
            Object.DestroyImmediate(go); go = new GameObject("~pu-best");

            // a normal run below the normal best is not submitted early, whatever the boosted best is
            PlayerPrefs.DeleteKey(LeaderboardService.PendingKey);
            t = NewTracker(go);
            SetField(t, "savesBest", true);
            SetField(t, "runScore", 900L);
            Invoke(t, "OnDestroy");
            Check("early quit, normal 900 under the normal best 1200: not submitted", !Pending(service, LeaderboardBoards.TopScore));
            Object.DestroyImmediate(go); go = new GameObject("~pu-best");

            // a boosted run is not blocked by the (higher) normal best, only by the boosted best
            t = NewTracker(go);
            SetField(t, "savesBest", true);
            SetField(t, "runScore", 400L);
            SetField(t, "powerUps", LeaderboardRunTag.PowerClear);
            Invoke(t, "OnDestroy");
            Check("early quit, boosted 400 over the boosted best 300 (normal best 1200): submitted", service.HasPending(LeaderboardBoards.BoostedRuns, out v) && v == 400);
            PlayerPrefs.DeleteKey(LeaderboardService.PendingKey);
            Object.DestroyImmediate(go); go = new GameObject("~pu-best");
            t = NewTracker(go);
            SetField(t, "savesBest", true);
            SetField(t, "runScore", 350L);
            SetField(t, "powerUps", LeaderboardRunTag.PowerClear);
            Invoke(t, "OnDestroy");
            Check("early quit, boosted 350 under the boosted best 400: not submitted", !Pending(service, LeaderboardBoards.BoostedRuns));
            Object.DestroyImmediate(go); go = new GameObject("~pu-best");

            // a death always offers (the service keeps only improvements) and records the route's best
            RunScore.BeginRun(true, true, false);
            t = NewTracker(go);
            SetField(t, "savesBest", true);
            SetField(t, "runScore", 2000L);
            SetField(t, "powerUps", LeaderboardRunTag.PowerDust);
            buttonClicks.playerDied = true;
            Invoke(t, "End");
            Check("death, boosted: Power-Ups board only, boosted best recorded",
                  service.HasPending(LeaderboardBoards.BoostedRuns, out v) && v == 2000 && LeaderboardService.LocalBest(true) == 2000 && LeaderboardService.LocalBest(false) == 1200);
            buttonClicks.playerDied = false;

            // a non-saving run (developer / practice) never moves a best
            Object.DestroyImmediate(go); go = new GameObject("~pu-best");
            t = NewTracker(go);
            SetField(t, "savesBest", false);
            SetField(t, "runScore", 9999L);
            Invoke(t, "OnDestroy");
            Check("a run that saves no best does not move the local bests", LeaderboardService.LocalBest(false) == 1200 && LeaderboardService.LocalBest(true) == 2000);

            // backfill: the saved best (5000, boosted history) is NOT offered to Top Score; the normal best is
            Context();
            PlayerPrefs.SetInt(RunScore.BestScoreKey, 5000);
            PlayerPrefs.SetInt(LeaderboardService.NormalBestKey, 1200);
            fake = new FakeLeaderboards();
            service = Service(fake);
            service.PlayerId = () => "gp-A";
            service.Flush();
            now = 60f;
            service.Tick();
            Check("backfill offers the normal best (1200), never RunScore.SavedBest (5000, may hold boosted runs)",
                  fake.SubmissionsTo(Id(LeaderboardBoards.TopScore)) == 1 && fake.Submissions.Exists(s => s.board == Id(LeaderboardBoards.TopScore) && s.score == 1200)
                  && fake.SubmissionsTo(BoostedAndroid) == 0);

            // before any run has seeded the key, the saved best is the normal best
            Context();
            PlayerPrefs.SetInt(RunScore.BestScoreKey, 321);
            Check("unseeded: the normal best falls back to the saved best", LeaderboardService.LocalBest(false) == 321);
            LeaderboardService.SeedLocalBests();
            PlayerPrefs.SetInt(RunScore.BestScoreKey, 999);
            Check("seeded once: later saved-best changes do not move it", LeaderboardService.LocalBest(false) == 321);
        }
        finally
        {
            buttonClicks.playerDied = false;
            Object.DestroyImmediate(go);
        }
    }

    // ---- 5 tags ----

    static void Tags()
    {
        string tag = LeaderboardRunTag.Encode(7, 4, 2, 3, 2, 123456, 12, (LeaderboardRunTag.PowerDust | LeaderboardRunTag.PowerClear));
        Check("tag: full encode", tag == "s7h4e2p3k2d123456w12u3");
        int ship, hearts, end, pauses, skin, worlds, powerUps;
        long dust;
        Check("tag: round trip", LeaderboardRunTag.TryParse(tag, out ship, out hearts, out end, out pauses, out skin, out dust, out worlds, out powerUps)
              && ship == 7 && hearts == 4 && end == 2 && pauses == 3 && skin == 2 && dust == 123456 && worlds == 12 && powerUps == 3);

        // worst case in range
        int maxShip = 1;
        for (int i = 1; i <= shopingShips.RosterTotal; i++) if (ShipId.IsValid(i) && !PremiumShips.IsPremium(i)) maxShip = i;
        string worst = LeaderboardRunTag.Encode(maxShip, ShipLives.MostHearts, ShipLives.MostHearts, LeaderboardRunTag.MostPauses, LeaderboardRunTag.MostSkin,
                                               long.MaxValue, int.MaxValue, 7);
        Check("tag: worst-case length " + worst.Length + " is within the 64-character Play Games limit", worst.Length <= 64 && worst.Length >= 20);
        Check("tag: the worst case is URL-safe (letters and digits only)", System.Text.RegularExpressions.Regex.IsMatch(worst, "^[a-z0-9]+$"));
        Check("tag: absurd dust and worlds are clamped, not dropped", worst.Contains("d" + LeaderboardRunTag.MostDust) && worst.Contains("w" + LeaderboardRunTag.MostWorlds));
        // an absolute bound: even a 4-digit ship and 4-digit hearts cannot exceed the limit
        string widest = "s9999h9999e9999p9999k9999d" + LeaderboardRunTag.MostDust + "w9999u3";
        Check("tag: the widest string the parser accepts is still under 64", widest.Length < 64);
        Check("tag: the real maximum is " + worst.Length + " characters", LeaderboardRunTag.MaxLength == 64 && worst.Length < LeaderboardRunTag.MaxLength);
        Check("tag: worst case parses back", LeaderboardRunTag.TryParse(worst, out ship, out hearts, out end, out pauses, out skin, out dust, out worlds, out powerUps)
              && dust == LeaderboardRunTag.MostDust && worlds == LeaderboardRunTag.MostWorlds && powerUps == 7);

        // old tags keep parsing and report no columns
        string[] old = { "s7h4", "s7h4e2", "s7h4e2p3", "s7h4e2p3k2" };
        bool oldOk = true;
        foreach (string o in old)
            oldOk &= LeaderboardRunTag.TryParse(o, out ship, out hearts, out end, out pauses, out skin, out dust, out worlds, out powerUps)
                     && ship == 7 && hearts == 4 && dust == -1 && worlds == -1 && powerUps == 0;
        Check("old tags still parse, with no columns", oldOk);
        Check("old tags: the 3/4/5-argument parses still work on the new tag",
              LeaderboardRunTag.TryParse(tag, out ship, out hearts) && ship == 7 && LeaderboardRunTag.TryParse(tag, out ship, out hearts, out end, out pauses, out skin) && skin == 2);
        Check("tag: no columns given writes none of the column fields", LeaderboardRunTag.Encode(7, 4, 2, 3, 2) == "s7h4e2p3k2");
        Check("tag: a power-ups value of 0 writes no u part", !LeaderboardRunTag.Encode(7, 4, 2, 3, 0, 5, 1, 0).Contains("u"));
        Check("tag: bad power-ups bits read as none", LeaderboardRunTag.TryParse("s7h4d5w1u9", out ship, out hearts, out end, out pauses, out skin, out dust, out worlds, out powerUps) && powerUps == 0 && dust == 5);
        Check("tag: Paragon is never encoded with columns either", LeaderboardRunTag.Encode(PremiumShips.Paragon, 14, 1, 1, 0, 5, 1, 3) == "");
        Check("tag: labels", LeaderboardRunTag.PowerUpLabel(1) == "DUST x2" && LeaderboardRunTag.PowerUpLabel(2) == "CLEAR"
              && LeaderboardRunTag.PowerUpLabel(3) == "DUST x2 + CLEAR" && LeaderboardRunTag.PowerUpShort(3) == "BOTH" && LeaderboardRunTag.PowerUpLabel(0) == "");
        Check("tag: homing labels", LeaderboardRunTag.PowerUpLabel(LeaderboardRunTag.PowerHoming) == "HOMING"
              && LeaderboardRunTag.PowerUpShort(LeaderboardRunTag.PowerHoming) == "HOMING"
              && LeaderboardRunTag.PowerUpLabel(5) == "DUST x2 + HOMING" && LeaderboardRunTag.PowerUpShort(6) == "BOTH"
              && LeaderboardRunTag.PowerUpLabel(7) == "DUST x2 + CLEAR + HOMING" && LeaderboardRunTag.PowerUpShort(7) == "ALL 3");
        Check("tag: the homing bit round-trips", LeaderboardRunTag.TryParse(LeaderboardRunTag.Encode(7, 4, -1, -1, 0, 5, 1, LeaderboardRunTag.PowerHoming),
              out ship, out hearts, out end, out pauses, out skin, out dust, out worlds, out powerUps) && powerUps == LeaderboardRunTag.PowerHoming);

        // the run's stats rounding: dust earned in hundredths
        var run = Run(10, LeaderboardRunTag.PowerDust);
        run.dustEarned = 12.345f;
        Check("stats: tag dust is the earned dust in hundredths, worlds included", run.Tag.Contains("d1235w3u1") || run.Tag.Contains("d1234w3u1"));
    }

    // ---- 6 panel ----

    static readonly Vector2 Phone = new Vector2(1080f, 2340f);
    static readonly Rect PhoneSafe = new Rect(0f, 63f, 1080f, 2202f);

    static void Panel()
    {
        Context();
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        string id = BoostedAndroid;
        var fake = new FakeLeaderboards();
        fake.SetBoard(id,
            FakeLeaderboards.Entry(1, "TETSUO", 9000, false, 7, 4, 2, 3, 0, 123456, 12, (LeaderboardRunTag.PowerDust | LeaderboardRunTag.PowerClear)),
            FakeLeaderboards.Entry(2, "KAI", 8000, false, 3, 3, 1, 1, 0, 5000, 4, LeaderboardRunTag.PowerDust),
            FakeLeaderboards.Entry(3, "KEI", 7000, false, 2, 3, 1, 1, 0, 100, 1, LeaderboardRunTag.PowerClear),
            FakeLeaderboards.Entry(4, "OLD", 6000),   // a score with no tag
            FakeLeaderboards.Entry(5, "KANEDA", 5000, true));
        var service = Service(fake);
        var panel = LeaderboardPanel.Open(service, null, Phone, PhoneSafe);
        Check("panel: two tabs, MAIN then Power-Ups", panel.TabBoards.Count == 2 && panel.TabBoards[0] == LeaderboardPanel.MainTab && panel.TabBoards[1] == LeaderboardBoards.BoostedRuns);
        panel.SelectBoard(LeaderboardBoards.BoostedRuns);
        Check("panel: populated", panel.CurrentState == LeaderboardPanel.State.Populated && panel.RowCount == 5);
        var rows = panel.PanelRoot.Find("List/Rows");
        Check("panel: row 0 star dust and worlds", rows.Find("Row0/Stats").GetComponent<Text>().text == "DUST 1.2K\nWORLDS 12");
        Check("panel: row 0 shows both power-ups", rows.Find("Row0/PowerUps").GetComponent<Text>().text == "BOTH");
        Check("panel: row 1 shows Dust x2", rows.Find("Row1/PowerUps").GetComponent<Text>().text == "DUST x2");
        Check("panel: row 2 shows Clear", rows.Find("Row2/PowerUps").GetComponent<Text>().text == "CLEAR");
        Check("panel: score is the value", rows.Find("Row0/Value").GetComponent<Text>().text == "9,000");
        Check("panel: ship shown, no hearts or pauses badges", rows.Find("Row0/Ship") != null && rows.Find("Row0/Hearts") == null && rows.Find("Row0/Pauses") == null);
        Check("panel: an untagged score shows its score and blank columns", rows.Find("Row3/Value") != null && rows.Find("Row3/Stats") == null
              && rows.Find("Row3/PowerUps") == null && rows.Find("Row3/Ship") == null);
        var own = panel.PanelRoot.Find("PlayerRow/Row");
        Check("panel: own row without tag data is blank in the columns, no crash", own != null && own.Find("Stats") == null && own.Find("PowerUps") == null && own.Find("Value") != null);

        // font floors: nothing on the tab or in a row under 16 units
        bool floors = true;
        foreach (var t in panel.PanelRoot.GetComponentsInChildren<Text>(true))
        {
            bool relevant = t.name == "Stats" || t.name == "PowerUps" || t.name == "Value" || (t.name == "Label" && t.transform.parent.name.StartsWith("Tab_"));
            if (!relevant) continue;
            int smallest = t.resizeTextForBestFit ? t.resizeTextMinSize : t.fontSize;
            if (smallest < 16) { floors = false; Debug.Log("[PU-LB] under 16: " + t.name + " " + smallest); }
        }
        Check("panel: type never under 16 units", floors);
        // tap targets: the tabs are still 100-unit targets
        bool taps = true;
        foreach (var b in panel.Tabs) taps &= ((RectTransform)b.transform).rect.height + 2f * LeaderboardPanel.TabTouchMargin >= LeaderboardPanel.MinTapUnits - .1f;
        Check("panel: tabs keep their 100-unit tap target", taps);
        // the name column must stay readable at the narrowest panel
        Check("panel: the name column keeps at least 80 units at the design width", panel.BoostedNameWidth >= 80f);
        Check("panel: compact counts", LeaderboardPanel.Compact(999) == "999" && LeaderboardPanel.Compact(1234) == "1.2K" && LeaderboardPanel.Compact(9999) == "9.9K" && LeaderboardPanel.Compact(12345) == "12K" && LeaderboardPanel.Compact(2500000) == "2M" && LeaderboardPanel.Compact(0) == "0");

        // a store with no tags at all (Game Center): rows blank, no crash
        var bare = new FakeLeaderboards();
        bare.SetBoard(id, FakeLeaderboards.Entry(1, "A", 100), FakeLeaderboards.Entry(2, "B", 90, true));
        service = Service(bare);
        panel = LeaderboardPanel.Open(service, null, Phone, PhoneSafe);
        panel.SelectBoard(LeaderboardBoards.BoostedRuns);
        Check("panel: a store with no tags still lists the scores", panel.CurrentState == LeaderboardPanel.State.Populated && panel.RowCount == 2);

        // own row filled from the device record when the store has no tag
        Context();
        var mine = new FakeLeaderboards();
        mine.SetBoard(id, FakeLeaderboards.Entry(1, "KANEDA", 700, true));
        var marks = new LeaderboardService.ScoreTable();
        marks.Set(LeaderboardBoards.BoostedRuns, 700, "s7h4e2p3d4200w5u2");
        marks.Save(LeaderboardService.SubmittedKey);
        service = Service(mine);
        panel = LeaderboardPanel.Open(service, null, Phone, PhoneSafe);
        panel.SelectBoard(LeaderboardBoards.BoostedRuns);
        rows = panel.PanelRoot.Find("List/Rows");
        Check("panel: own entry filled from the device record (iOS path)", rows.Find("Row0/Stats").GetComponent<Text>().text == "DUST 42\nWORLDS 5"
              && rows.Find("Row0/PowerUps").GetComponent<Text>().text == "CLEAR");
        LeaderboardPanel.Close();

        // end to end through the Fake: tag stored with the score, read back into columns
        Context();
        var e2e = new FakeLeaderboards();
        service = Service(e2e);
        service.SubmitRun(Run(640, LeaderboardRunTag.PowerDust));
        now = 60f;
        service.Tick();
        LeaderboardPage got = null;
        service.LoadBoard(LeaderboardBoards.BoostedRuns, 10, (top, me) => got = top);
        Check("end to end: the stored row carries the columns",
              got != null && got.entries.Count == 1 && got.entries[0].hasRunStats && got.entries[0].worlds == 3 && got.entries[0].starDustHundredths == 950
              && got.entries[0].powerUps == LeaderboardRunTag.PowerDust);
    }
}
