using UnityEngine;

// Achievement and leaderboard calls.
//
// Previously every method was wrapped in `if (Application.platform == Android)`
// with an empty iOS branch, so nothing reported outside Android. The platform
// gates are gone -- SocialBridge routes to whatever backend the platform has
// (Game Center on iOS out of the box), so these work everywhere.
public class achievementAPICalls : MonoBehaviour
{
    public static void showLeaderboard()
    {
        SocialBridge.ShowLeaderboard();
    }

    // Goes through LeaderboardService, which drops it in developer mode and
    // the tutorial, keeps it queued while signed out and only sends an
    // improvement. (LeaderboardRunTracker offers the same value at run end.)
    public static void leaderboard_highest_speed_reached(float value)
    {
        LeaderboardService.Instance.Offer(LeaderboardBoards.TopSpeed, (long)value);
    }

    // ---- One-shot achievements ----

    public static void achievement_buy_your_first_ship()
    {
        SocialBridge.UnlockAchievement(StringHolder.achievement_buy_your_first_ship);
    }

    public static void achievement_flash()
    {
        SocialBridge.UnlockAchievement(StringHolder.achievement_flash);
    }

    public static void achievement_buy_all_ships()
    {
        SocialBridge.UnlockAchievement(StringHolder.achievement_buy_all_ships);
    }

    public static void achievement_speedster()
    {
        SocialBridge.UnlockAchievement(StringHolder.achievement_speedster);
    }

    public static void achievement_super_sonic()
    {
        SocialBridge.UnlockAchievement(StringHolder.achievement_super_sonic);
    }

    public static void achievement_logged_on_successfully()
    {
        SocialBridge.UnlockAchievement(StringHolder.achievement_logged_on_successfully);
    }

    public static void achievement_tutorial_completed()
    {
        SocialBridge.UnlockAchievement(StringHolder.achievement_tutorial_complete);
    }

    public static void achievement_you_have_unclocked_the_secret_ship()
    {
        SocialBridge.UnlockAchievement(StringHolder.achievement_you_have_unlocked_the_secret_ship);
    }

    public static void achievement_correct_pause()
    {
        SocialBridge.UnlockAchievement(StringHolder.achievement_correct_pause);
    }

    public static void achievement_paused()
    {
        SocialBridge.UnlockAchievement(StringHolder.achievement_paused);
    }

    // ---- Tiered achievements ----
    // One call per event; AchievementTiers advances every tier of the
    // category against its own threshold.

    public static void star_collected()
    {
        AchievementTiers.Record(AchievementCategory.Stars);
    }

    public static void alien_killed()
    {
        AchievementTiers.Record(AchievementCategory.Aliens);
    }

    public static void asteroid_destroyed()
    {
        AchievementTiers.Record(AchievementCategory.Asteroids);
    }

    public static void player_died()
    {
        AchievementTiers.Record(AchievementCategory.Deaths);
    }
}
