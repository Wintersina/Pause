using UnityEngine;

// What a real run has reached on the speed side, reported once each per run
// (collisionDetection steps it on flying frames, never the tutorial or
// developer mode):
//   Flash        natural speed reached the cap (SPEED 35)
//   Speedster    the first limit break: past the cap on the boost shield
//   Super Sonic  a full limit break: the boost at its maximum at the cap
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
            AchievementTracker.OnSpeedMilestone(reported);
        }
    }
}
