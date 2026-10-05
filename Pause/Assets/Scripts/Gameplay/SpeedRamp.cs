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
//
// THE SOFT KNEE (2026-10). Past HUD 35 the board was a wall: speed kept
// climbing at the full rate right up to the cap. The ramp now keeps its
// early pace up to SoftKnee and climbs at SoftRampScale of it from there to
// the cap, so approaching and passing 35 is a gentle increase and the cap
// (WorldManager.Worlds, LoopRules) is an endurance plateau. Both numbers are
// here; Tick, DistanceOver and SecondsToCover all follow the same curve.
public static class SpeedRamp
{
    // ---- tunables ----
    // moveBackGround.speed where the ramp softens (HUD 30) ...
    public static float SoftKnee = .30f;
    // ... and the share of the world's ramp rate it keeps above it.
    public static float SoftRampScale = .40f;

    // The ramp rate at `speed` for a world whose base rate is `rate`.
    public static float RateAt(float speed, float rate)
    {
        return speed >= SoftKnee ? rate * SoftRampScale : rate;
    }

    // `speed` after `dt` seconds of ramp (no cap).
    public static float Advance(float speed, float rate, float dt)
    {
        if (dt <= 0f || rate <= 0f) return speed;
        if (speed >= SoftKnee) return speed + rate * SoftRampScale * dt;
        float next = speed + rate * dt;
        if (next <= SoftKnee) return next;
        // crossed the knee inside this step: the rest of it at the soft rate
        return SoftKnee + (next - SoftKnee) * SoftRampScale;
    }

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
        moveBackGround.speed = Mathf.Min(Advance(s, ratePerSecond, Delta), max);
        return true;
    }

    public static void ResetFrameGuard()
    {
        lastFrame = int.MinValue;
    }

    // ---- the ramp as a curve (pure; WorldManager measures worlds with it) ----
    // Speed starts at v0, climbs `rate` per second to SoftKnee and
    // rate x SoftRampScale from there, holds at `max`. A start at or above
    // the cap holds where it is (Tick never lowers speed).

    // The stretch of ramp from `v`: its acceleration and the speed it ends at.
    static void Stretch(float v, float rate, float max, out float accel, out float target)
    {
        bool below = v < SoftKnee && SoftKnee < max;
        accel = v < SoftKnee ? rate : rate * SoftRampScale;
        target = below ? SoftKnee : max;
    }

    // Speed after `seconds` on the curve.
    public static float SpeedAfter(float v0, float rate, float max, float seconds)
    {
        float v = Mathf.Max(0f, v0);
        for (int k = 0; k < 2 && seconds > 0f && v < max && rate > 0f; k++)
        {
            Stretch(v, rate, max, out float a, out float target);
            float tc = (target - v) / a;
            if (seconds <= tc) return v + a * seconds;
            v = target;
            seconds -= tc;
        }
        return v;
    }

    // Distance (speed x seconds) flown in `seconds`.
    public static float DistanceOver(float v0, float rate, float max, float seconds)
    {
        if (seconds <= 0f) return 0f;
        float v = Mathf.Max(0f, v0), d = 0f;
        for (int k = 0; k < 2 && v < max && rate > 0f; k++)
        {
            Stretch(v, rate, max, out float a, out float target);
            float tc = (target - v) / a;
            if (seconds <= tc) return d + v * seconds + .5f * a * seconds * seconds;
            d += v * tc + .5f * a * tc * tc;
            v = target;
            seconds -= tc;
        }
        return d + v * seconds;
    }

    // Seconds needed to fly `distance` (the inverse of DistanceOver).
    // Infinity when the speed is and stays zero.
    public static float SecondsToCover(float v0, float rate, float max, float distance)
    {
        if (distance <= 0f) return 0f;
        float v = Mathf.Max(0f, v0), t = 0f;
        for (int k = 0; k < 2 && v < max && rate > 0f; k++)
        {
            Stretch(v, rate, max, out float a, out float target);
            float tc = (target - v) / a;
            float dc = v * tc + .5f * a * tc * tc;
            if (distance <= dc) return t + (-v + Mathf.Sqrt(v * v + 2f * a * distance)) / a;
            distance -= dc;
            t += tc;
            v = target;
        }
        return v > 0f ? t + distance / v : float.PositiveInfinity;
    }
}
