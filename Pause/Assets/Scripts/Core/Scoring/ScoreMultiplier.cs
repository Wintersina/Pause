using System;

// The max-speed streak: hold the speed cap (HUD 35) for StreakSeconds without
// losing a heart and every point scored from then on is worth Factor times as
// much, until a heart is lost or the speed drops below the cap.
//
// Every number is a named constant here (nothing else hard-codes 15 or 2).
//
// How it runs:
//   * RunScore.Tick calls Tick(dt, speed) on running frames only, with the
//     world's own dt, so paused time (the pause menu, the shield-expire
//     shockwave's freeze, boss/planetfall/lift-off scripted freezes, the run's
//     entry) never advances the streak and never breaks it. The tutorial and
//     developer-free runs that do not score never tick at all.
//   * "At the cap" is moveBackGround.speed (the effective scroll speed: the
//     natural ramp plus any limit-break boost) >= SpeedRamp.Cap - CapEpsilon.
//     A world change does not touch the streak; only a real speed change does
//     (a boss holds speed at 20, so a boss fight ends it).
//   * collisionDetection calls OnHeartLost when a hit costs a heart (the
//     killing one too). Shielded / cloaked / invulnerable hits cost nothing
//     and do not break it. Healing does not extend or restore it.
//   * RunScore.BeginRun resets everything (new run, replay).
//   * The multiplier applies to points gained while it is active, through the
//     one place RunScore adds score (RunScore.Gain). Earlier points are never
//     revalued. RunScore.Total is unchanged in meaning, so leaderboards,
//     the best score and the score achievements simply see the doubled score.
//
// No allocation: statics and value types only.
public static class ScoreMultiplier
{
    // ---- tunables ----
    // Seconds at the cap, without losing a heart, before the multiplier starts.
    public const float StreakSeconds = 15f;
    // What score gained while it is active is multiplied by.
    public const int Factor = 2;
    // Speed counts as "at the cap" this close under it (HUD units are 0.01).
    public const float CapEpsilon = .0005f;
    // Float slack so exactly StreakSeconds of dt steps always trips it.
    const double Slack = 1e-4;
    // HUD cue: the progress bar shows once the streak has run this long.
    public const float CueShowAfterSeconds = 2f;
    // HUD cue: the bar fills in steps of this many seconds (chunky, pixel).
    public const float CueStepSeconds = .5f;
    // HUD cue: how long the "just lost" feedback stays on screen.
    public const float LossCueSeconds = .6f;

    static double streak;
    static double secondsIn2x;
    static bool active;
    static int startedCount;
    static int lostCount;

    // ---- state ----
    public static bool Active { get { return active; } }
    // Seconds held at the cap (capped at StreakSeconds once active).
    public static float Streak { get { return (float)streak; } }
    // 0..1 toward the multiplier (1 while active).
    public static float Progress01 { get { return active ? 1f : (float)Math.Min(1d, streak / StreakSeconds); } }
    // The multiplier right now: Factor while active, else 1.
    public static int Current { get { return active ? Factor : 1; } }
    // This run's time spent at x2 (a stat for future achievements).
    public static float SecondsIn2x { get { return (float)secondsIn2x; } }
    // Times it came on / ended while active this run (the HUD plays its
    // one-shots when these change; a Reset never changes them).
    public static int StartedCount { get { return startedCount; } }
    public static int LostCount { get { return lostCount; } }

    public static bool AtCap(float speed) { return speed >= SpeedRamp.Cap - CapEpsilon; }

    public static void Reset()
    {
        streak = 0d;
        secondsIn2x = 0d;
        active = false;
        // a reset is silent: the one-shot counters are left alone
    }

    // `dt`: the world's running delta (0 / negative while frozen: nothing
    // happens). `speed`: moveBackGround.speed.
    public static void Tick(float dt, float speed)
    {
        if (dt <= 0f) return;
        if (!AtCap(speed)) { Break(); return; }
        if (active) { secondsIn2x += dt; return; }
        streak += dt;
        if (streak >= StreakSeconds - Slack)
        {
            streak = StreakSeconds;
            active = true;
            startedCount++;
        }
    }

    public static void OnHeartLost() { Break(); }

    static void Break()
    {
        if (active) lostCount++;
        active = false;
        streak = 0d;
    }

    // `points` as earned while the multiplier is (or is not) on. Never
    // negative, never overflows.
    public static int Gain(int points)
    {
        if (points <= 0) return 0;
        if (!active) return points;
        long v = (long)points * Factor;
        return v > int.MaxValue ? int.MaxValue : (int)v;
    }

    public static float Gain(float points)
    {
        return points <= 0f ? 0f : active ? points * Factor : points;
    }
}
