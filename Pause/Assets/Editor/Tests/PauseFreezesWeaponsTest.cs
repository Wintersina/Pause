using UnityEngine;

// A pause (moveBackGround parks Time.timeScale at 0) freezes every weapon
// effect: the weapon clocks hand out no time while the world is frozen and
// pick up where they left off on resume.
public static class PauseFreezesWeaponsTest
{
    static int fails;
    static void Check(string what, bool ok)
    {
        Debug.Log((ok ? "[PFW] PASS  " : "[PFW] FAIL  ") + what);
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
        float savedScale = Time.timeScale;
        int frame = 0;
        PauseClock.Reset();
        PauseClock.FrameOverride = () => frame;
        PauseClock.StepOverride = () => .1f;
        try
        {
            Time.timeScale = 1f;
            frame++; float a = PauseClock.Now;
            frame++; float b = PauseClock.Now;
            Check("the pause clock runs while the world runs", b > a);

            Time.timeScale = 0f;
            float frozen = PauseClock.Now;
            for (int i = 0; i < 20; i++) { frame++; PauseClock.Now.ToString(); }
            Check("the pause clock stands still through a pause", Mathf.Approximately(PauseClock.Now, frozen));
            Check("weapon step time is zero in a pause", ShipAttackRunner.ScaledDelta() == 0f);

            Time.timeScale = 1f;
            frame++;
            Check("the pause clock resumes where it left off", Mathf.Approximately(PauseClock.Now, frozen + .1f));
        }
        finally
        {
            Time.timeScale = savedScale;
            PauseClock.FrameOverride = null;
            PauseClock.StepOverride = null;
            PauseClock.Reset();
        }
        return fails;
    }
}
