using System;
using System.Collections.Generic;
using UnityEngine;

// Re-reports tiered achievement progress after sign-in.
//
// AchievementTiers.Record() reports as the counts change, but SocialBridge
// drops reports while the player is signed out -- so kills and stars earned
// offline (or before the platform finished signing in) would never reach
// Game Center / Play Games. Once per sign-in this sends every tier that has
// progress and isn't yet confirmed complete on the platform, with the
// percentage AchievementTiers would report for the current count.
//
// A tier is "confirmed" once a 100% report for it succeeded; that is stored
// per category as the highest confirmed threshold (achv_synced_<category>),
// so already-unlocked tiers aren't re-sent on every launch.
public static class AchievementSync
{
    public delegate void Reporter(string achievementId, double percent, Action<bool> done);

    public static string SyncedKey(AchievementCategory category)
    {
        return "achv_synced_" + category.ToString().ToLowerInvariant();
    }

    static void DefaultReporter(string id, double percent, Action<bool> done)
    {
        SocialBridge.ReportProgress(id, percent, ok => { if (done != null) done(ok); });
    }

    // Returns what it reported, in category/tier order.
    public static List<KeyValuePair<string, double>> ResyncAll(Reporter report = null)
    {
        if (report == null) report = DefaultReporter;
        var reported = new List<KeyValuePair<string, double>>();
        foreach (AchievementCategory category in Enum.GetValues(typeof(AchievementCategory)))
        {
            int count = AchievementTiers.Count(category);
            if (count <= 0) continue;
            int confirmed = PlayerPrefs.GetInt(SyncedKey(category), 0);
            foreach (var tier in AchievementTiers.For(category))
            {
                if (tier.threshold <= confirmed) continue;
                double percent = AchievementTiers.Percent(count, tier.threshold);
                reported.Add(new KeyValuePair<string, double>(tier.id, percent));
                var cat = category;
                int threshold = tier.threshold;
                report(tier.id, percent, ok =>
                {
                    if (!ok || percent < 100.0) return;
                    string key = SyncedKey(cat);
                    if (PlayerPrefs.GetInt(key, 0) < threshold)
                    {
                        PlayerPrefs.SetInt(key, threshold);
                        PrefsSaver.MarkDirty();
                    }
                });
            }
        }
        return reported;
    }

    // A different player signed in: nothing is confirmed for them yet.
    public static void ClearSyncMarks()
    {
        foreach (AchievementCategory category in Enum.GetValues(typeof(AchievementCategory)))
            PlayerPrefs.DeleteKey(SyncedKey(category));
    }
}
