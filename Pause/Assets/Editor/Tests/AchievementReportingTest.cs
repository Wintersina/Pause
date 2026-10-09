using System.Linq;
using UnityEngine;

// Store reporting: placeholders are never sent, signed-out unlocks are caught up
// at sign-in (and only once), progress is batched, the editor uses the no-op store.
public static class AchievementReportingTest
{
    static int fails;
    static void Check(string what, bool ok)
    {
        Debug.Log((ok ? "[ARP] PASS  " : "[ARP] FAIL  ") + what);
        if (!ok) fails++;
    }

    public static void Run() { TestHarness.Exit(Execute()); }

    public static int Execute()
    {
        fails = 0;
        using var sandbox = new TestHarness.Sandbox();
        PlayerPrefs.SetInt(DeveloperUnlocks.EnabledKey, 0);
        AchievementStore.ResetAll();
        bool ios = AchievementIds.IosIdsConfirmed;
        try
        {
            Stores();
            Offline();
            Progress();
            Switch();
        }
        finally
        {
            AchievementIds.IosIdsConfirmed = ios;
            AchievementStores.Current = null;
            AchievementStore.ResetAll();
        }
        Debug.Log("[ARP] failures: " + fails);
        return fails;
    }

    static void Stores()
    {
        AchievementStores.Current = null;
        Check("the editor's store is the no-op", AchievementStores.Current.Name == "none" && !AchievementStores.Current.Available);
        var legacy = AchievementCatalog.Find("meta_first_flight");    // has a real (legacy) Android id
        var fresh = AchievementCatalog.Find("loop_1");                // placeholder
        var play = new PlayGamesAchievementStore();
        var gc = new GameCenterAchievementStore();
        Check("Play Games: a real id can be reported, a placeholder cannot", play.CanReport(legacy) && !play.CanReport(fresh));
        Check("Game Center: nothing reportable until the iOS ids are confirmed", !gc.CanReport(legacy) && !gc.CanReport(fresh));
        AchievementIds.IosIdsConfirmed = true;
        Check("Game Center: reportable once confirmed", gc.CanReport(fresh));
        AchievementIds.IosIdsConfirmed = false;
        bool called = false, result = true;
        play.Report(fresh, 100.0, ok => { called = true; result = ok; });
        Check("a placeholder is never sent: the callback fails at once", called && !result);
        Check("platform id mapping: Android real id verbatim, iOS = prefix + ach_ + id",
              AchievementIds.AndroidId(legacy) == "CgkI3eXNjrQcEAIQBw" && AchievementIds.IosId(fresh) == "me.sinaserati.Pause.ach_loop_1");
        Check("PlatformPercent is exact below 100", SocialBridge.PlatformPercent(100.0) == 100.0);
    }

    static void Offline()
    {
        var fake = new FakeAchievementStore { available = false };
        AchievementStores.Current = fake;
        var tut = AchievementCatalog.Find("meta_first_flight");
        var frost = AchievementCatalog.Find("world_frost_reached");
        AchievementStore.Unlock(tut);
        AchievementStore.Unlock(frost);
        Check("signed out: unlocks are local and nothing is sent", AchievementStore.IsUnlocked(tut) && fake.reports.Count == 0 &&
              AchievementSync.Confirmed(tut) == 0);
        AchievementTracker.Enable();
        try
        {
            fake.available = true;
            AchievementTracker.OnSignedIn();   // what SocialBridge.SignedIn runs
            Check("sign-in: the missed unlocks are reported once (+ Logged On itself)",
                  fake.reports.Count(r => r.Key == "meta_first_flight" && r.Value == 100.0) == 1 &&
                  fake.reports.Count(r => r.Key == "world_frost_reached") == 1 &&
                  fake.reports.Count(r => r.Key == "meta_logged_on") == 1);
            int n = fake.reports.Count;
            AchievementTracker.OnSignedIn();
            Check("a second sign-in sends nothing twice", fake.reports.Count == n);
            AchievementStore.Unlock(AchievementCatalog.Find("world_verdant_reached"));
            Check("signed in: a new unlock is reported immediately", fake.reports.Last().Key == "world_verdant_reached" && fake.reports.Last().Value == 100.0);
            // the event path itself
            fake.reports.Clear();
            SocialBridge.NotifySignedIn();
            Check("SocialBridge.SignedIn drives the sync (nothing new to send)", fake.reports.Count == 0);
        }
        finally { AchievementTracker.Disable(); }
    }

    static void Progress()
    {
        var fake = new FakeAchievementStore();
        AchievementStores.Current = fake;
        AchievementStore.ResetAll();
        for (int i = 0; i < 5; i++) AchievementStore.AddCounter("kills", 10);
        Check("progress is batched, not sent per kill", fake.reports.Count == 0 && AchievementSync.PendingReports >= 1);
        AchievementSync.FlushReports();
        var r = fake.reports.Where(x => x.Key == "kills_100").ToList();
        Check("flush: 50% for kills_100 once, no repeat for a standard-store score",
              r.Count == 1 && r[0].Value == 50.0 && fake.reports.All(x => x.Key != "score_10k"));
        fake.reports.Clear();
        AchievementSync.FlushReports();
        Check("an empty queue sends nothing", fake.reports.Count == 0);
        AchievementStore.AddCounter("kills", 50);
        Check("completing sends 100% at once", fake.reports.Any(x => x.Key == "kills_100" && x.Value == 100.0));
        fake.reports.Clear();
        fake.available = false;
        AchievementStore.AddCounter("stars", 20);
        AchievementSync.FlushReports();
        Check("flush while signed out keeps the queue for the sign-in sync", fake.reports.Count == 0 && AchievementSync.PendingReports >= 1);
        fake.available = true;
        AchievementSync.ResyncAll();
        Check("... which reports the open counters' percent", fake.reports.Any(x => x.Key == "stars_150" && System.Math.Abs(x.Value - 20 * 100.0 / 150) < 1e-9));
        Check("non-incremental achievements (score, chain, loops) get no partial progress",
              fake.reports.All(x => AchievementCatalog.Find(x.Key).storeSteps > 0 || x.Value == 100.0));
        fake.unreportable.Add("kills_1000");
        fake.reports.Clear();
        AchievementStore.AddCounter("kills", 1);
        AchievementSync.FlushReports();
        Check("an achievement the store cannot take (placeholder id) is skipped", fake.reports.All(x => x.Key != "kills_1000"));
    }

    static void Switch()
    {
        var fake = new FakeAchievementStore();
        AchievementStores.Current = fake;
        AchievementStore.ResetAll();
        var d = AchievementCatalog.Find("loop_1");
        AchievementStore.Unlock(d);
        Check("confirmed after a successful report", AchievementSync.Confirmed(d) == 100);
        AchievementSync.ClearSyncMarks();
        Check("a different account: marks cleared, unlock still local", AchievementSync.Confirmed(d) == 0 && AchievementStore.IsUnlocked(d));
        fake.reports.Clear();
        AchievementSync.ResyncAll();
        Check("... and reported to the new account", fake.reports.Any(x => x.Key == "loop_1"));
    }
}
