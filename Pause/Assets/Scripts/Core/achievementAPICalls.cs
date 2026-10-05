using UnityEngine;

// Achievement and leaderboard calls.
//
// Previously every method was wrapped in `if (Application.platform == Android)`
// with an empty iOS branch, so nothing reported outside Android. The platform
// gates are gone -- SocialBridge routes to whatever backend the platform has
// (Game Center on iOS out of the box), so these work everywhere.
public class achievementAPICalls : MonoBehaviour
{
    // What a real run has reached on the speed side, reported once each.
    // collisionDetection steps it on flying frames (never the tutorial or
    // developer mode).
    public static class SpeedMilestones
    {
        static int reported;   // 0 none, 1 Flash, 2 Speedster, 3 Super Sonic

        public static void Reset() { reported = 0; }

        // The milestone the current speed stands for (0-3).
        public static int Reached(float natural, float boost)
        {
            int capHud = SpeedRamp.CapHud;
            int hud = Mathf.RoundToInt((natural + boost) * 100f);
            if (boost > 0f && hud >= Mathf.RoundToInt((SpeedRamp.Cap + SpeedRamp.MaxBoost) * 100f)) return 3;
            if (boost > 0f && hud > capHud) return 2;
            return Mathf.RoundToInt(natural * 100f) >= capHud ? 1 : 0;
        }

        public static void Step()
        {
            int now = Reached(SpeedRamp.Natural, SpeedRamp.Boost);
            while (reported < now)
            {
                reported++;
                if (reported == 1) achievement_flash();
                else if (reported == 2) achievement_speedster();
                else achievement_super_sonic();
            }
        }
    }

    public static void showLeaderboard()
    {
        SocialBridge.ShowLeaderboard();
    }

    // ---- One-shot achievements ----
    //
    // The three speed achievements (none was ever unlocked by code before
    // the speed cap) mark the cap and the limit break, not a record:
    //   Flash        natural speed reached the cap (SPEED 35)
    //   Speedster    the first limit break: past the cap on the boost shield
    //   Super Sonic  a full limit break: the boost at its maximum at the cap
    // SpeedMilestones reports them, once each per run.

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
