using System.Linq;
using UnityEngine;

// Claiming: once, 25 star dust exactly, one save, never while locked or dormant,
// COLLECT ALL, and the ledger (which overwrites prefs during runs) keeps a grant.
public static class AchievementClaimTest
{
    static int fails;
    static void Check(string what, bool ok)
    {
        Debug.Log((ok ? "[ACL] PASS  " : "[ACL] FAIL  ") + what);
        if (!ok) fails++;
    }

    public static void Run() { TestHarness.Exit(Execute()); }

    public static int Execute()
    {
        fails = 0;
        using var sandbox = new TestHarness.Sandbox();
        PlayerPrefs.SetInt(DeveloperUnlocks.EnabledKey, 0);
        AchievementStore.ResetAll();
        PlayerPrefs.SetFloat(StarDustLedger.CurrencyKey, 100f);
        bool tide = WorldManager.TideEnabled;
        try
        {
            WorldManager.TideEnabled = false;
            ClaimOnce();
            ClaimAll();
            LedgerRun();
            Events();
        }
        finally
        {
            WorldManager.TideEnabled = tide;
            AchievementStore.ResetAll();
        }
        Debug.Log("[ACL] failures: " + fails);
        return fails;
    }

    static float Dust() { return PlayerPrefs.GetFloat(StarDustLedger.CurrencyKey); }

    static void ClaimOnce()
    {
        var d = AchievementCatalog.Find("loop_1");
        Check("locked: not claimable, pays nothing", !AchievementStore.IsClaimable(d) && AchievementStore.Claim(d) == 0 && Dust() == 100f);
        Check("unlock is idempotent", AchievementStore.Unlock(d) && !AchievementStore.Unlock(d));
        Check("unlocking never pays", Dust() == 100f && !AchievementStore.IsClaimed(d));
        Check("unlocked: claimable", AchievementStore.IsClaimable(d) && AchievementStore.ClaimableCount == 1);
        int saves = PrefsSaver.SaveCount;
        int paid = AchievementStore.Claim(d);
        Check("claim pays 25 and sets the flag", paid == 25 && Dust() == 125f && AchievementStore.IsClaimed(d));
        Check("claim is ONE save, flag and dust in the same write", PrefsSaver.SaveCount == saves + 1);
        saves = PrefsSaver.SaveCount;
        Check("claiming again pays nothing", AchievementStore.Claim(d) == 0 && Dust() == 125f && PrefsSaver.SaveCount == saves);
        Check("claimed: no longer claimable", !AchievementStore.IsClaimable(d) && AchievementStore.ClaimableCount == 0);
        Check("claim by id facade", Achievements.Claim("loop_1") == 0);
        Check("an unknown id claims nothing", AchievementStore.Claim(null) == 0 && Achievements.Claim("nope") == 0);
    }

    static void ClaimAll()
    {
        foreach (string id in new[] { "kills_100", "stars_150", "meta_logged_on" }) AchievementStore.Unlock(AchievementCatalog.Find(id));
        float before = Dust();
        int saves = PrefsSaver.SaveCount;
        Check("3 waiting", AchievementStore.ClaimableCount == 3);
        int paid = AchievementStore.ClaimAll();
        Check("COLLECT ALL pays N x 25 (75)", paid == 75 && Dust() == before + 75f);
        Check("... with one save", PrefsSaver.SaveCount == saves + 1);
        Check("... and a second COLLECT ALL pays nothing", AchievementStore.ClaimAll() == 0 && Dust() == before + 75f);
        Check("claimed count", AchievementStore.ClaimedCount == 4 && AchievementStore.UnlockedCount == 4);
    }

    static void LedgerRun()
    {
        // A grant made while a run is active would be overwritten by the next
        // Commit's absolute write unless it also moves the baseline.
        AchievementStore.Unlock(AchievementCatalog.Find("loop_2"));
        PlayerPrefs.SetFloat(StarDustLedger.CurrencyKey, 200f);
        int token = StarDustLedger.BeginRun(true);
        StarDustLedger.Earn(10f);
        float saved = StarDustLedger.Saved;
        int paid = AchievementStore.Claim(AchievementCatalog.Find("loop_2"));
        StarDustLedger.Commit(token);
        Check("a claim during an active ledger run survives the next Commit (" + Dust() + ")", paid == 25 && Dust() == 235f);
        StarDustLedger.EndRun(token);
        Check("... and the run's end", Dust() == 235f);
        // not in a run: plain add
        StarDustLedger.Grant(5f);
        Check("Grant adds to the saved balance", Dust() == 240f);
        StarDustLedger.Grant(-3f);
        StarDustLedger.Grant(0f);
        Check("Grant ignores zero and negative amounts", Dust() == 240f);
    }

    static void Events()
    {
        AchievementStore.ResetAll();
        int unlocked = 0, claimed = 0;
        System.Action<AchievementDef> onU = d => unlocked++;
        System.Action onC = () => claimed++;
        AchievementStore.Unlocked += onU;
        AchievementStore.Claimed += onC;
        try
        {
            AchievementStore.Unlock(AchievementCatalog.Find("loop_5"));
            AchievementStore.Unlock(AchievementCatalog.Find("loop_5"));
            AchievementStore.Claim(AchievementCatalog.Find("loop_5"));
            AchievementStore.Claim(AchievementCatalog.Find("loop_5"));
            Check("Unlocked fires once, Claimed fires once", unlocked == 1 && claimed == 1);
            // a counter unlocking everything it reaches at once
            AchievementStore.SetCounter("kills", 1200);
            Check("one counter write unlocks kills_100 and kills_1000, not kills_5000",
                  AchievementStore.IsUnlocked(AchievementCatalog.Find("kills_100")) && AchievementStore.IsUnlocked(AchievementCatalog.Find("kills_1000")) &&
                  !AchievementStore.IsUnlocked(AchievementCatalog.Find("kills_5000")) && unlocked == 3);
            Check("progress is capped at the target, percent is exact", AchievementStore.Progress(AchievementCatalog.Find("kills_5000")) == 1200 &&
                  System.Math.Abs(AchievementStore.Percent(AchievementCatalog.Find("kills_5000")) - 24.0) < 1e-9 &&
                  AchievementStore.Progress(AchievementCatalog.Find("kills_100")) == 1200);
            Check("lowering a target later cannot take an unlock away: the flag persists",
                  AchievementStore.IsUnlocked(AchievementCatalog.Find("kills_100")));
        }
        finally
        {
            AchievementStore.Unlocked -= onU;
            AchievementStore.Claimed -= onC;
        }
    }
}
