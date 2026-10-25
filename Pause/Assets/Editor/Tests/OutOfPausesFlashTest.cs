using UnityEngine;

// The out-of-pauses screen flash: fires once on the 1 -> 0 transition of a real run.
//   scripts/unity-batch.sh -executeMethod AllTests.RunSuites -suites OutOfPausesFlashTest
public static class OutOfPausesFlashTest
{
    static int fails;
    static void Check(string what, bool ok)
    {
        Debug.Log((ok ? "[OOP] PASS  " : "[OOP] FAIL  ") + what);
        if (!ok) fails++;
    }

    public static void Run() { TestHarness.Exit(Execute()); }

    public static int Execute()
    {
        fails = 0;
        Check("1 -> 0 fires", OutOfPausesFlash.ShouldFire(1, 0, true, false, false));
        Check("2 -> 1 does not fire", !OutOfPausesFlash.ShouldFire(2, 1, true, false, false));
        Check("0 -> 0 does not fire", !OutOfPausesFlash.ShouldFire(0, 0, true, false, false));
        Check("a refill (0 -> 2) does not fire", !OutOfPausesFlash.ShouldFire(0, 2, true, false, false));
        Check("tutorial / practice does not fire", !OutOfPausesFlash.ShouldFire(1, 0, false, false, false));
        Check("dead does not fire", !OutOfPausesFlash.ShouldFire(1, 0, true, true, false));
        Check("kill cam does not fire", !OutOfPausesFlash.ShouldFire(1, 0, true, false, true));
        // Once per transition: a second 0 -> 0 spend (not possible in play) cannot re-fire.
        Check("repeat at zero does not fire", !OutOfPausesFlash.ShouldFire(0, 0, true, false, false) && OutOfPausesFlash.ShouldFire(1, 0, true, false, false));

        Check("intensity 0 at start", OutOfPausesFlash.Intensity(0f) == 0f);
        Check("intensity peaks at end of ramp", Mathf.Approximately(OutOfPausesFlash.Intensity(OutOfPausesFlash.RampUpSeconds), 1f));
        Check("intensity falls after the peak", OutOfPausesFlash.Intensity(0.2f) < 1f && OutOfPausesFlash.Intensity(0.2f) > OutOfPausesFlash.Intensity(0.35f));
        Check("intensity 0 at the end", OutOfPausesFlash.Intensity(OutOfPausesFlash.TotalSeconds) == 0f);
        Check("total in 0.35-0.5 s", OutOfPausesFlash.TotalSeconds >= 0.35f && OutOfPausesFlash.TotalSeconds <= 0.5f);
        Check("edge glow bright at edge, clear at centre", OutOfPausesFlash.EdgeAlpha(0f, 0.5f) > 0.99f && OutOfPausesFlash.EdgeAlpha(0.5f, 0.5f) == 0f);
        Check("edge glow on all four edges", OutOfPausesFlash.EdgeAlpha(1f, 0.5f) > 0.99f && OutOfPausesFlash.EdgeAlpha(0.5f, 0f) > 0.99f && OutOfPausesFlash.EdgeAlpha(0.5f, 1f) > 0.99f);

        Debug.Log("[OOP] failures: " + fails);
        return fails;
    }
}
