using System.Linq;
using UnityEngine;

// The one-time move from the old counters / owned state to the new set: the
// mapped achievements come out UNLOCKED but not claimed (each worth 25 dust),
// running twice changes nothing, developer mode is left alone, and the cloud
// keys round-trip and merge (flags union, counters max).
public static class AchievementMigrationTest
{
    static int fails;
    static void Check(string what, bool ok)
    {
        Debug.Log((ok ? "[AMG] PASS  " : "[AMG] FAIL  ") + what);
        if (!ok) fails++;
    }

    public static void Run() { TestHarness.Exit(Execute()); }

    static bool U(string id) { return AchievementStore.IsUnlocked(AchievementCatalog.Find(id)); }

    public static int Execute()
    {
        fails = 0;
        using var sandbox = new TestHarness.Sandbox();
        PlayerPrefs.SetInt(DeveloperUnlocks.EnabledKey, 0);
        bool tide = WorldManager.TideEnabled;
        try
        {
            WorldManager.TideEnabled = false;
            Migrates();
            DeveloperModeIsSkipped();
            Cloud();
        }
        finally
        {
            WorldManager.TideEnabled = tide;
            AchievementStore.ResetAll();
        }
        Debug.Log("[AMG] failures: " + fails);
        return fails;
    }

    static void Clear()
    {
        AchievementStore.ResetAll();
        foreach (string k in AchievementMigration.LegacyKeys()) PlayerPrefs.DeleteKey(k);
        PlayerPrefs.DeleteKey("HasDoneTut");
        PlayerPrefs.DeleteKey(WorldManager.PrefsHighestWorld);
        PlayerPrefs.DeleteKey(RunScore.BestScoreKey);
        PlayerPrefs.DeleteKey(ProgressSnapshot.HighestSpeedKey);
        PlayerPrefs.SetString(Codex.PrefsKey, "");
        for (int i = 1; i <= shopingShips.shipTotal; i++)
        {
            PlayerPrefs.DeleteKey("boughtship" + i);
            for (int n = 1; n < ShipSkins.PerShip; n++) PlayerPrefs.DeleteKey(ShipSkins.OwnedKey(i, n));
        }
        Codex.Reload();
    }

    static void Migrates()
    {
        Clear();
        PlayerPrefs.SetInt(AchievementMigration.LegacyAliens, 150);
        PlayerPrefs.SetInt(AchievementMigration.LegacyAsteroids, 600);
        PlayerPrefs.SetInt(AchievementMigration.LegacyDeaths, 12);
        PlayerPrefs.SetInt(AchievementMigration.LegacyStars, 200);
        PlayerPrefs.SetString("HasDoneTut", "true");
        PlayerPrefs.SetInt(WorldManager.PrefsHighestWorld, 2);
        PlayerPrefs.SetString("boughtship1", "True");
        PlayerPrefs.SetString("boughtship2", "True");
        PlayerPrefs.SetString("boughtship3", "True");
        PlayerPrefs.SetInt(ShipSkins.OwnedKey(2, 1), 1);
        PlayerPrefs.SetInt(RunScore.BestScoreKey, 3000);
        PlayerPrefs.SetString(Codex.PrefsKey, "enemy_space_fighter_1,enemy_frost_alien,hazard_mine,hazard_ember_mine,atom_violet,world_portal");
        Codex.Reload();

        Check("a fresh install needs the migration", AchievementMigration.NeedsRun);
        bool ran = AchievementMigration.RunIfNeeded();
        Check("it runs once and stamps the schema", ran && !AchievementMigration.NeedsRun && PlayerPrefs.GetInt(AchievementStore.SchemaKey) == AchievementMigration.Schema);

        string[] expected = { "meta_first_flight", "world_frost_reached", "world_verdant_reached", "ship_first", "skin_first",
                              "kills_100", "rocks_500", "deaths_10", "stars_150", "score_10k" };
        foreach (string id in expected) Check("migrated: " + id, U(id));
        string[] not = { "world_ember_reached", "kills_1000", "deaths_100", "stars_1000", "score_50k", "ship_half", "skin_special", "loop_1", "boss_space", "meta_logged_on", "speed_flash" };
        foreach (string id in not) Check("not earned: " + id, !U(id));
        Check("counters seeded from the old ones", AchievementStore.Counter("kills") == 150 && AchievementStore.Counter("rocks") == 600 &&
              AchievementStore.Counter("deaths") == 12 && AchievementStore.Counter("stars") == 200 && AchievementStore.Counter("ships") == 3);
        Check("the codex count is current", AchievementStore.Counter("codex") == Codex.DiscoveredCount && Codex.DiscoveredCount >= 6);
        Check("every migrated achievement is UNLOCKED, none claimed",
              AchievementStore.UnlockedCount == AchievementCatalog.All.Count(d => AchievementStore.IsUnlocked(d)) &&
              AchievementStore.ClaimedCount == 0 && AchievementStore.ClaimableCount == AchievementStore.UnlockedCount);
        Check("... each one worth 25 dust", AchievementStore.ClaimableCount * AchievementCatalog.RewardDust ==
              AchievementCatalog.All.Count(d => AchievementStore.IsClaimable(d)) * 25 && AchievementStore.ClaimableCount >= 10);

        // idempotent
        string before = Snapshot();
        Check("running again does nothing", !AchievementMigration.RunIfNeeded() && Snapshot() == before);
        AchievementMigration.Reevaluate();
        Check("re-evaluating changes nothing either", Snapshot() == before);
        // claimed ones stay claimed through a re-run
        AchievementStore.Claim(AchievementCatalog.Find("kills_100"));
        AchievementMigration.Reevaluate();
        Check("a claimed achievement stays claimed", AchievementStore.IsClaimed(AchievementCatalog.Find("kills_100")));

        // a later cloud restore that brings more owned state unlocks more
        PlayerPrefs.SetString("boughtship4", "True");
        PlayerPrefs.SetInt(WorldManager.PrefsHighestWorld, 3);
        AchievementMigration.AfterCloudApply();
        Check("after a cloud restore: more is derived (Ember, 4 ships)", U("world_ember_reached") && AchievementStore.Counter("ships") == 4);

        // the old per-tier progress keys seed on installs from before the category counters
        Clear();
        PlayerPrefs.SetInt(AchievementMigration.LegacyProgressPrefix + "CgkI3eXNjrQcEAIQEA", 80);   // aliens_4's old clamped counter
        AchievementMigration.RunIfNeeded();
        Check("legacy achv_progress_* seeds the kills counter", AchievementStore.Counter("kills") == 80 && !U("kills_100"));
        Check("legacy key list covers 4 counts + 18 tier keys", AchievementMigration.LegacyKeys().Count == 4 + 18);
    }

    static string Snapshot()
    {
        return string.Join(",", AchievementCatalog.All.Select(d => d.id + ":" + (AchievementStore.IsUnlocked(d) ? 1 : 0) + (AchievementStore.IsClaimed(d) ? 1 : 0))) +
               "|" + AchievementStore.Counter("kills") + "," + AchievementStore.Counter("ships") + "," + AchievementStore.Counter("codex");
    }

    static void DeveloperModeIsSkipped()
    {
        Clear();
        PlayerPrefs.SetString("HasDoneTut", "true");
        PlayerPrefs.SetInt(DeveloperUnlocks.EnabledKey, 1);
        Check("developer mode: the migration waits", !AchievementMigration.RunIfNeeded() && AchievementMigration.NeedsRun && !U("meta_first_flight"));
        AchievementMigration.AfterCloudApply();
        Check("... and nothing is derived from its overridden keys", !U("meta_first_flight"));
        PlayerPrefs.SetInt(DeveloperUnlocks.EnabledKey, 0);
        Check("back to normal: it runs", AchievementMigration.RunIfNeeded() && U("meta_first_flight"));
    }

    static void Cloud()
    {
        Clear();
        AchievementStore.Unlock(AchievementCatalog.Find("loop_1"));
        AchievementStore.Claim(AchievementCatalog.Find("loop_1"));
        AchievementStore.SetCounter("kills", 321);
        AchievementStore.MarkBoss(0);
        AchievementStore.MarkBoss(1);
        AchievementStore.SetCounter("bosses", 1);   // counters merge by max, sets by union: recounted below
        var local = ProgressSnapshot.Capture(100);
        Check("snapshot carries the unlocked / claimed flags, counters and sets",
              local.CounterValue("ach_u_loop_1") == 1 && local.CounterValue("ach_c_loop_1") == 1 &&
              local.CounterValue("ach_n_kills") == 321 && local.CounterValue(AchievementStore.BossFlagKey(1)) == 1);
        Check("the sync keys include every achievement's flags", ProgressSnapshot.CounterKeys().Count(k => k.StartsWith("ach_u_")) == 60 &&
              ProgressSnapshot.CounterKeys().Count(k => k.StartsWith("ach_c_")) == 60);

        // another device: claimed a different one, unlocked a third, more kills, a different boss
        Clear();
        AchievementStore.Unlock(AchievementCatalog.Find("loop_2"));
        AchievementStore.Claim(AchievementCatalog.Find("loop_2"));
        AchievementStore.Unlock(AchievementCatalog.Find("loop_1"));   // earned there too, not claimed there
        AchievementStore.SetCounter("kills", 100);
        AchievementStore.MarkBoss(2);
        var other = ProgressSnapshot.Capture(200);
        var merged = ProgressMerge.Merge(local, other);
        Check("merge: unlocked and claimed flags are a union (claimed wins over unclaimed)",
              merged.CounterValue("ach_u_loop_1") == 1 && merged.CounterValue("ach_c_loop_1") == 1 &&
              merged.CounterValue("ach_u_loop_2") == 1 && merged.CounterValue("ach_c_loop_2") == 1);
        Check("merge: counters take the max, boss sets the union", merged.CounterValue("ach_n_kills") == 321 &&
              merged.CounterValue(AchievementStore.BossFlagKey(0)) == 1 && merged.CounterValue(AchievementStore.BossFlagKey(2)) == 1);
        Clear();
        merged.Apply();
        bool wasClaimed = AchievementStore.IsClaimed(AchievementCatalog.Find("loop_1"));
        string who = string.Join(",", AchievementCatalog.All.Where(d => AchievementStore.IsClaimable(d)).Select(d => d.id));
        int again = AchievementStore.ClaimAll();
        Debug.Log("[AMG] claimable after apply: " + who);
        Check("apply: restored, loop_1 stays claimed (no second payout) (claimed " + wasClaimed + ", paid " + again + ")", wasClaimed && !AchievementStore.IsClaimable(AchievementCatalog.Find("loop_1")) &&
              !who.Contains("loop_"));
        AchievementMigration.AfterCloudApply();
        Check("after apply: the boss counter is recounted from the union (3)", AchievementStore.Counter("bosses") == 3);
        string json = merged.ToJson();
        ProgressSnapshot parsed;
        Check("JSON round trip keeps the keys", ProgressSnapshot.TryParse(json, out parsed) == ProgressSnapshot.ParseResult.Ok &&
              parsed.CounterValue("ach_n_kills") == 321);
    }
}
