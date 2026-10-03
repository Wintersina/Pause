using System.Collections.Generic;
using UnityEngine;

public enum AchievementCategory { Aliens, Asteroids, Deaths, Stars }

// Tiered "do X N times" achievements.
//
// Each category keeps one persistent lifetime count, and every tier of that
// category unlocks at its own threshold. Previously each tier kept its own
// counter clamped to 100 and reported the raw count as a percentage, while
// every kill bumped every tier at once -- so "kill 5 aliens" and "kill 3500
// aliens" both completed on the 100th kill.
public static class AchievementTiers
{
    public struct Tier
    {
        public readonly string id;
        public readonly int threshold;
        public Tier(string id, int threshold) { this.id = id; this.threshold = threshold; }
    }

    // Thresholds are the counts named in each achievement's description.
    public static readonly Tier[] Aliens =
    {
        new Tier(StringHolder.achievement_aliens, 5),
        new Tier(StringHolder.achievement_aliens_2, 25),
        new Tier(StringHolder.achievement_aliens_3, 50),
        new Tier(StringHolder.achievement_aliens_4, 150),
        new Tier(StringHolder.achievement_aliens_5, 1000),
        new Tier(StringHolder.achievement_aliens_6, 3500),
    };

    public static readonly Tier[] Asteroids =
    {
        new Tier(StringHolder.achievement_destroyer, 5),
        new Tier(StringHolder.achievement_destroy_2, 25),
        new Tier(StringHolder.achievement_destroyer_3, 50),
        new Tier(StringHolder.achievement_destroyer_4, 100),
        new Tier(StringHolder.achievement_destroyer_5, 1500),
    };

    public static readonly Tier[] Deaths =
    {
        new Tier(StringHolder.achievement_first_death, 1),
        new Tier(StringHolder.achievement_death_2, 5),
        new Tier(StringHolder.achievement_death_3, 10),
        new Tier(StringHolder.achievement_death_4, 50),
        new Tier(StringHolder.achievement_death_5, 100),
    };

    public static readonly Tier[] Stars =
    {
        new Tier(StringHolder.achievement_stars, 150),
        new Tier(StringHolder.achievement_stars_2, 1000),
    };

    public static Tier[] For(AchievementCategory category)
    {
        switch (category)
        {
            case AchievementCategory.Aliens: return Aliens;
            case AchievementCategory.Asteroids: return Asteroids;
            case AchievementCategory.Deaths: return Deaths;
            default: return Stars;
        }
    }

    public static string CounterKey(AchievementCategory category)
    {
        return "achv_count_" + category.ToString().ToLowerInvariant();
    }

    // The per-tier key the old clamped counters were stored under.
    public static string LegacyProgressKey(string achievementId)
    {
        return "achv_progress_" + achievementId;
    }

    public static double Percent(int count, int threshold)
    {
        if (threshold <= 0) return 100.0;
        return System.Math.Min(100.0, System.Math.Max(0, count) * 100.0 / threshold);
    }

    public static int Count(AchievementCategory category)
    {
        string key = CounterKey(category);
        if (PlayerPrefs.HasKey(key)) return PlayerPrefs.GetInt(key);

        // First run after the update: the old counters were all bumped
        // together, so the highest of them is the real count (up to the old
        // 100 cap). Seed from it so nobody loses progress.
        int seed = 0;
        foreach (var tier in For(category))
            seed = Mathf.Max(seed, PlayerPrefs.GetInt(LegacyProgressKey(tier.id), 0));
        return seed;
    }

    // Adds to the category's count and reports progress for every tier that
    // was still incomplete before this step. Returns what it reported, in
    // tier order. The count is written to PlayerPrefs but not flushed to disk;
    // PrefsSaver batches that.
    public static List<KeyValuePair<string, double>> Record(AchievementCategory category, int steps = 1)
    {
        var reported = new List<KeyValuePair<string, double>>();
        int before = Count(category);
        int after = Mathf.Max(0, before + steps);
        PlayerPrefs.SetInt(CounterKey(category), after);
        PrefsSaver.MarkDirty();

        foreach (var tier in For(category))
        {
            if (before >= tier.threshold) continue;   // already complete
            double percent = Percent(after, tier.threshold);
            reported.Add(new KeyValuePair<string, double>(tier.id, percent));
            SocialBridge.ReportProgress(tier.id, percent);
        }
        return reported;
    }
}
