using System.Collections.Generic;
using UnityEngine;

// Each tier of a tiered achievement unlocks at its own count. They used to
// share a per-tier counter clamped to 100 that was reported as the percent
// directly, and every kill bumped every tier, so all of them completed
// together on the 100th kill.
public static class AchievementTiersTest
{
    static int fails;
    static void Check(string what, bool ok)
    {
        Debug.Log((ok ? "[AT] PASS  " : "[AT] FAIL  ") + what);
        if (!ok) fails++;
    }

    public static void Run()
    {
        TestHarness.Exit(Execute());
    }

    public static int Execute()
    {
        fails = 0;
        using var sandbox = new TestHarness.Sandbox();

        ThresholdMath();
        TableIsSane();
        TiersUnlockAtTheirOwnCount();
        CompletedTiersStopReporting();
        LegacyProgressSeedsTheCount();
        CountsAreBatchedNotSavedPerKill();

        Debug.Log("[AT] failures: " + fails);
        return fails;
    }

    static void ThresholdMath()
    {
        Check("0 of 5 is 0%", AchievementTiers.Percent(0, 5) == 0.0);
        Check("1 of 5 is 20%", System.Math.Abs(AchievementTiers.Percent(1, 5) - 20.0) < 1e-9);
        Check("5 of 5 is 100%", AchievementTiers.Percent(5, 5) == 100.0);
        Check("past the threshold stays at 100%", AchievementTiers.Percent(400, 5) == 100.0);
        Check("100 kills is under 3% of 3500", AchievementTiers.Percent(100, 3500) < 3.0);
        Check("1750 of 3500 is 50%", System.Math.Abs(AchievementTiers.Percent(1750, 3500) - 50.0) < 1e-9);
        Check("negative counts clamp to 0%", AchievementTiers.Percent(-3, 5) == 0.0);
    }

    static void TableIsSane()
    {
        foreach (AchievementCategory category in System.Enum.GetValues(typeof(AchievementCategory)))
        {
            var tiers = AchievementTiers.For(category);
            bool ascending = tiers.Length > 0;
            for (int i = 1; i < tiers.Length; i++)
                ascending &= tiers[i].threshold > tiers[i - 1].threshold;
            bool ids = true;
            foreach (var t in tiers) ids &= !string.IsNullOrEmpty(t.id);
            Check(category + " tiers have ids and strictly rising thresholds", ascending && ids);
        }
        Check("alien tiers are 5/25/50/150/1000/3500",
              Thresholds(AchievementTiers.Aliens) == "5,25,50,150,1000,3500");
        Check("asteroid tiers are 5/25/50/100/1500",
              Thresholds(AchievementTiers.Asteroids) == "5,25,50,100,1500");
        Check("asteroid tier 2 still uses the existing id",
              AchievementTiers.Asteroids[1].id == StringHolder.achievement_destroy_2);
    }

    static string Thresholds(AchievementTiers.Tier[] tiers)
    {
        var parts = new List<string>();
        foreach (var t in tiers) parts.Add(t.threshold.ToString());
        return string.Join(",", parts);
    }

    static Dictionary<string, double> AsMap(List<KeyValuePair<string, double>> reports)
    {
        var map = new Dictionary<string, double>();
        foreach (var r in reports) map[r.Key] = r.Value;
        return map;
    }

    static void TiersUnlockAtTheirOwnCount()
    {
        Reset(AchievementCategory.Aliens);
        List<KeyValuePair<string, double>> last = null;
        for (int i = 0; i < 100; i++) last = AchievementTiers.Record(AchievementCategory.Aliens);
        var map = AsMap(last);

        Check("lifetime alien count is 100", AchievementTiers.Count(AchievementCategory.Aliens) == 100);
        Check("100 kills: the 150 tier is 66.7%, not complete",
              map.ContainsKey(StringHolder.achievement_aliens_4) &&
              System.Math.Abs(map[StringHolder.achievement_aliens_4] - 100.0 * 100 / 150) < 1e-6);
        Check("100 kills: the 3500 tier is under 3%",
              map.ContainsKey(StringHolder.achievement_aliens_6) && map[StringHolder.achievement_aliens_6] < 3.0);
        Check("100 kills: the 1000 tier is 10%",
              map.ContainsKey(StringHolder.achievement_aliens_5) &&
              System.Math.Abs(map[StringHolder.achievement_aliens_5] - 10.0) < 1e-6);

        Reset(AchievementCategory.Aliens);
        for (int i = 0; i < 4; i++) AchievementTiers.Record(AchievementCategory.Aliens);
        map = AsMap(AchievementTiers.Record(AchievementCategory.Aliens));
        Check("the 5th kill completes the 5-kill tier",
              map.ContainsKey(StringHolder.achievement_aliens) && map[StringHolder.achievement_aliens] == 100.0);
        Check("the 5th kill puts the 25-kill tier at 20%",
              System.Math.Abs(map[StringHolder.achievement_aliens_2] - 20.0) < 1e-6);
    }

    static void CompletedTiersStopReporting()
    {
        Reset(AchievementCategory.Asteroids);
        for (int i = 0; i < 5; i++) AchievementTiers.Record(AchievementCategory.Asteroids);
        var map = AsMap(AchievementTiers.Record(AchievementCategory.Asteroids));
        Check("a tier finished on an earlier kill is not reported again",
              !map.ContainsKey(StringHolder.achievement_destroyer));
        Check("unfinished tiers are still reported", map.Count == AchievementTiers.Asteroids.Length - 1);

        Reset(AchievementCategory.Deaths);
        map = AsMap(AchievementTiers.Record(AchievementCategory.Deaths));
        Check("the first death completes the first-death achievement",
              map.ContainsKey(StringHolder.achievement_first_death) &&
              map[StringHolder.achievement_first_death] == 100.0);
        map = AsMap(AchievementTiers.Record(AchievementCategory.Deaths));
        Check("the second death no longer reports first-death",
              !map.ContainsKey(StringHolder.achievement_first_death));
    }

    static void LegacyProgressSeedsTheCount()
    {
        PlayerPrefs.DeleteKey(AchievementTiers.CounterKey(AchievementCategory.Stars));
        PlayerPrefs.SetInt(AchievementTiers.LegacyProgressKey(StringHolder.achievement_stars), 37);
        PlayerPrefs.SetInt(AchievementTiers.LegacyProgressKey(StringHolder.achievement_stars_2), 37);
        Check("an old save's clamped counter seeds the new count",
              AchievementTiers.Count(AchievementCategory.Stars) == 37);
        AchievementTiers.Record(AchievementCategory.Stars);
        Check("and counting continues from it", AchievementTiers.Count(AchievementCategory.Stars) == 38);
        PlayerPrefs.DeleteKey(AchievementTiers.LegacyProgressKey(StringHolder.achievement_stars));
        PlayerPrefs.DeleteKey(AchievementTiers.LegacyProgressKey(StringHolder.achievement_stars_2));
    }

    static void CountsAreBatchedNotSavedPerKill()
    {
        Reset(AchievementCategory.Aliens);
        PrefsSaver.SaveNow();
        int saves = PrefsSaver.SaveCount;
        for (int i = 0; i < 30; i++) AchievementTiers.Record(AchievementCategory.Aliens);
        Check("30 kills cause no PlayerPrefs.Save()", PrefsSaver.SaveCount == saves);
        Check("but leave the prefs marked dirty", PrefsSaver.Dirty);
        Check("a save is not due right after the last one",
              !PrefsSaver.SaveIfDue(Time.unscaledTime + 1f));
        Check("a save is due once the batching interval has passed",
              PrefsSaver.SaveIfDue(Time.unscaledTime + PrefsSaver.SaveInterval + 1f));
        Check("and it is a single save", PrefsSaver.SaveCount == saves + 1 && !PrefsSaver.Dirty);
    }

    static void Reset(AchievementCategory category)
    {
        PlayerPrefs.SetInt(AchievementTiers.CounterKey(category), 0);
    }
}
