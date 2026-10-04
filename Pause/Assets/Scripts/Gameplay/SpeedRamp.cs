using System;
using UnityEngine;

// The single owner of the difficulty ramp on moveBackGround.speed.
//
// moveBackGround sits on both side walls (leftPipe and rightPipe in gameS1 and
// tutorialS5). Each instance used to add speedRampPerSecond * dt to the shared
// static speed on its own, so the ramp ran once per wall per frame: twice the
// intended rate. Worse, WorldManager only pushed the world's ramp/cap into one
// of them, so the other kept the scene's 0.002/s and 0.6 cap. Space, Frost and
// Verdant ended up climbing at their own rate + 0.002/s and topping out at 0.6
// instead of their maxSpeed.
//
// Every instance still calls Tick on its running frames, and the frame guard
// makes sure only the first call in a frame counts. The ramp therefore runs
// exactly once per frame, however many walls exist. WorldManager.ApplyDifficulty
// gives every instance the same ramp and cap, so it doesn't matter which one
// goes first.
public static class SpeedRamp
{
    // Test hooks: edit-mode tests can't advance Time.frameCount or set
    // Time.deltaTime.
    public static Func<int> FrameOverride;
    public static Func<float> DeltaOverride;

    static int lastFrame = int.MinValue;

    static int Frame => FrameOverride != null ? FrameOverride() : Time.frameCount;
    static float Delta => DeltaOverride != null ? DeltaOverride() : Time.deltaTime;

    // Advances moveBackGround.speed by ratePerSecond * dt, up to max. Returns
    // false when another caller already ticked this frame.
    public static bool Tick(float ratePerSecond, float max)
    {
        int frame = Frame;
        if (frame == lastFrame) return false;
        lastFrame = frame;

        float s = moveBackGround.speed;
        if (s >= max) return true;
        moveBackGround.speed = Mathf.Min(s + ratePerSecond * Delta, max);
        return true;
    }

    public static void ResetFrameGuard()
    {
        lastFrame = int.MinValue;
    }

    // ---- the ramp as a curve (pure; WorldManager measures worlds with it) ----
    // Speed starts at v0, climbs `rate` per second, holds at `max`. A start at
    // or above the cap holds where it is (Tick never lowers speed).

    // Distance (speed x seconds) flown in `seconds`.
    public static float DistanceOver(float v0, float rate, float max, float seconds)
    {
        if (seconds <= 0f) return 0f;
        v0 = Mathf.Max(0f, v0);
        if (v0 >= max || rate <= 0f) return v0 * seconds;
        float tc = (max - v0) / rate;
        if (seconds <= tc) return v0 * seconds + .5f * rate * seconds * seconds;
        return v0 * tc + .5f * rate * tc * tc + max * (seconds - tc);
    }

    // Seconds needed to fly `distance` (the inverse of DistanceOver).
    // Infinity when the speed is and stays zero.
    public static float SecondsToCover(float v0, float rate, float max, float distance)
    {
        if (distance <= 0f) return 0f;
        v0 = Mathf.Max(0f, v0);
        if (v0 >= max || rate <= 0f) return v0 > 0f ? distance / v0 : float.PositiveInfinity;
        float tc = (max - v0) / rate;
        float dc = DistanceOver(v0, rate, max, tc);
        if (distance <= dc) return (-v0 + Mathf.Sqrt(v0 * v0 + 2f * rate * distance)) / rate;
        return tc + (distance - dc) / max;
    }
}
