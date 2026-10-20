using UnityEngine;

// Real seconds, but only the ones the world was running: it stands still while
// the game is paused (Time.timeScale 0, set by moveBackGround) and picks up
// where it left off on resume. For weapon timers that must last a fixed number
// of real seconds of play (the cinematic hold and its ease-out) yet never tick
// away during a pause. Visual effects use the scaled Time.deltaTime instead, so
// they also follow the resume slow-mo ramp.
public static class PauseClock
{
    static int frame = -1;
    static float now;

    // Tests drive time through this: null means Time.unscaledDeltaTime.
    public static System.Func<float> StepOverride;
    public static System.Func<int> FrameOverride;   // tests: the frame counter

    public static float Now
    {
        get
        {
            int f = FrameOverride != null ? FrameOverride() : Time.frameCount;
            if (frame != f)
            {
                frame = f;
                if (Time.timeScale > 0f) now += StepOverride != null ? StepOverride() : Time.unscaledDeltaTime;
            }
            return now;
        }
    }

    public static void Reset() { frame = -1; now = 0f; }
}
