using UnityEngine;

// Carries the pilot's old progress into the new achievements (once), and
// re-derives what can be derived from owned state whenever it may have changed.
//
// There was no local "unlocked" flag before: only lifetime counters
// (achv_count_*, achv_progress_*) and what the game itself keeps (tutorial,
// highest world, owned ships and skins, codex, best score). So:
//
//   first launch of the new build (ach_schema < 1)
//     seed the counters from the legacy ones (kills <- Aliens, rocks <- Asteroids,
//     deaths, stars: the old counts are lower bounds), then Reevaluate()
//   Reevaluate() (that launch, and after every cloud restore / account switch)
//     unlock everything derivable: tutorial, worlds reached, ships, skins,
//     codex counts, best score
//
// Everything unlocked this way is UNLOCKED but not claimed, so each earned one
// is worth 25 star dust to collect. It never runs in developer mode (the keys
// it reads are overridden there).
public static class AchievementMigration
{
    public const int Schema = 1;

    public const string LegacyAliens = "achv_count_aliens", LegacyAsteroids = "achv_count_asteroids",
        LegacyDeaths = "achv_count_deaths", LegacyStars = "achv_count_stars";

    // The old per-tier progress keys ("achv_progress_" + Play id), the seed on installs
    // from before the per-category counters.
    static readonly string[][] LegacyProgressIds =
    {
        new[] { "CgkI3eXNjrQcEAIQAg", "CgkI3eXNjrQcEAIQAw", "CgkI3eXNjrQcEAIQDw", "CgkI3eXNjrQcEAIQEA", "CgkI3eXNjrQcEAIQEQ", "CgkI3eXNjrQcEAIQFQ" },   // aliens
        new[] { "CgkI3eXNjrQcEAIQBA", "CgkI3eXNjrQcEAIQBQ", "CgkI3eXNjrQcEAIQEg", "CgkI3eXNjrQcEAIQEw", "CgkI3eXNjrQcEAIQFA" },                        // asteroids
        new[] { "CgkI3eXNjrQcEAIQCw", "CgkI3eXNjrQcEAIQFg", "CgkI3eXNjrQcEAIQFw", "CgkI3eXNjrQcEAIQGA", "CgkI3eXNjrQcEAIQGQ" },                        // deaths
        new[] { "CgkI3eXNjrQcEAIQGw", "CgkI3eXNjrQcEAIQHA" },                                                                                         // stars
    };

    public const string LegacyProgressPrefix = "achv_progress_";

    // Every legacy key the cloud snapshot still carries for one release.
    public static System.Collections.Generic.List<string> LegacyKeys()
    {
        var keys = new System.Collections.Generic.List<string> { LegacyAliens, LegacyAsteroids, LegacyDeaths, LegacyStars };
        foreach (var group in LegacyProgressIds) foreach (string id in group) keys.Add(LegacyProgressPrefix + id);
        return keys;
    }

    static int Legacy(string countKey, int group)
    {
        if (PlayerPrefs.HasKey(countKey)) return PlayerPrefs.GetInt(countKey);
        int seed = 0;
        foreach (string id in LegacyProgressIds[group]) seed = Mathf.Max(seed, PlayerPrefs.GetInt(LegacyProgressPrefix + id, 0));
        return seed;
    }

    public static bool NeedsRun { get { return PlayerPrefs.GetInt(AchievementStore.SchemaKey, 0) < Schema; } }

    // Runs the one-time migration when it is due. False: nothing to do (or developer mode).
    public static bool RunIfNeeded()
    {
        if (!NeedsRun || DeveloperUnlocks.Enabled) return false;
        string[] counters = { AchievementCatalog.CKills, AchievementCatalog.CRocks, AchievementCatalog.CDeaths, AchievementCatalog.CStars };
        string[] keys = { LegacyAliens, LegacyAsteroids, LegacyDeaths, LegacyStars };
        for (int i = 0; i < counters.Length; i++)
            AchievementStore.SetCounterAtLeast(counters[i], Legacy(keys[i], i));
        Reevaluate();
        PlayerPrefs.SetInt(AchievementStore.SchemaKey, Schema);
        PrefsSaver.MarkDirty();
        return true;
    }

    // After a cloud restore or an account switch: the merged state may unlock more.
    public static void AfterCloudApply()
    {
        if (DeveloperUnlocks.Enabled) return;
        AchievementStore.RecountDerived();
        Reevaluate();
    }

    // Unlocks everything derivable from state the game already keeps.
    public static void Reevaluate()
    {
        if (DeveloperUnlocks.Enabled) return;
        var tutorial = AchievementCatalog.Find("meta_first_flight");
        if (PlayerPrefs.GetString("HasDoneTut", "") == "true") AchievementStore.Unlock(tutorial);

        int highest = PlayerPrefs.GetInt(WorldManager.PrefsHighestWorld, 0);
        string[] reached = { null, "world_frost_reached", "world_verdant_reached", "world_ember_reached", "world_tide_reached" };
        for (int w = 1; w < reached.Length; w++)
            if (highest >= w) AchievementStore.Unlock(AchievementCatalog.Find(reached[w]));

        int ships = AchievementTracker.OwnedShips();
        AchievementStore.SetCounterAtLeast(AchievementCatalog.CShips, ships);
        if (ships >= 2) AchievementStore.Unlock(AchievementCatalog.Find("ship_first"));
        AchievementTracker.EvaluateSkins();

        Codex.Reload();
        AchievementTracker.RefreshCodex();

        AchievementStore.SetCounterAtLeast(AchievementCatalog.CScore, RunScore.SavedBest);
        AchievementStore.RecountDerived();
    }
}
