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

    public static void leaderboard_highest_speed_reached(float value)
    {
        SocialBridge.ReportScore((long)value, StringHolder.leaderboard_highest_speed_reached);
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

    public static void achievement_first_death()
    {
        SocialBridge.UnlockAchievement(StringHolder.achievement_first_death);
    }

    public static void achievement_correct_pause()
    {
        SocialBridge.UnlockAchievement(StringHolder.achievement_correct_pause);
    }

    public static void achievement_paused()
    {
        SocialBridge.UnlockAchievement(StringHolder.achievement_paused);
    }

    // ---- Incremental achievements ----

    public static void achievement_stars()
    {
        SocialBridge.IncrementAchievement(StringHolder.achievement_stars, 1);
    }

    public static void achievement_stars_2()
    {
        SocialBridge.IncrementAchievement(StringHolder.achievement_stars_2, 1);
    }

    public static void achievement_destroyer()
    {
        SocialBridge.IncrementAchievement(StringHolder.achievement_destroyer, 1);
    }

    public static void achievement_destroyer_2()
    {
        SocialBridge.IncrementAchievement(StringHolder.achievement_destroy_2, 1);
    }

    public static void achievement_destroyer_3()
    {
        SocialBridge.IncrementAchievement(StringHolder.achievement_destroyer_3, 1);
    }

    public static void achievement_destroyer_4()
    {
        SocialBridge.IncrementAchievement(StringHolder.achievement_destroyer_4, 1);
    }

    public static void achievement_destroyer_5()
    {
        SocialBridge.IncrementAchievement(StringHolder.achievement_destroyer_5, 1);
    }

    public static void achievement_death_2()
    {
        SocialBridge.IncrementAchievement(StringHolder.achievement_death_2, 1);
    }

    public static void achievement_death_3()
    {
        SocialBridge.IncrementAchievement(StringHolder.achievement_death_3, 1);
    }

    public static void achievement_death_4()
    {
        SocialBridge.IncrementAchievement(StringHolder.achievement_death_4, 1);
    }

    public static void achievement_death_5()
    {
        SocialBridge.IncrementAchievement(StringHolder.achievement_death_5, 1);
    }

    public static void achievement_aliens()
    {
        SocialBridge.IncrementAchievement(StringHolder.achievement_aliens, 1);
    }

    public static void achievement_aliens_2()
    {
        SocialBridge.IncrementAchievement(StringHolder.achievement_aliens_2, 1);
    }

    public static void achievement_aliens_3()
    {
        SocialBridge.IncrementAchievement(StringHolder.achievement_aliens_3, 1);
    }

    public static void achievement_aliens_4()
    {
        SocialBridge.IncrementAchievement(StringHolder.achievement_aliens_4, 1);
    }

    public static void achievement_aliens_5()
    {
        SocialBridge.IncrementAchievement(StringHolder.achievement_aliens_5, 1);
    }

    public static void achievement_aliens_6()
    {
        SocialBridge.IncrementAchievement(StringHolder.achievement_aliens_6, 1);
    }
}
