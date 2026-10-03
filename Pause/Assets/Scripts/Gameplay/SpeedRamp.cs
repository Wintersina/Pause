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
}
