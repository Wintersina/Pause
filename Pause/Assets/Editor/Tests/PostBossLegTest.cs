using UnityEngine;

// The way to the next planet after a boss (PostBossLeg): the rock slots come back very light, and normal again in the next world.
//   * outside the leg every rock slot is admitted, whatever is in play
//   * a boss being over begins it; the first rock waits, then at most one is admitted per gap, never more than MaxOnBoard in play
//   * over a long stretch the leg admits a small share of what the four rock slots ask for, far under the ordinary rate
//   * flying the portal (or closing it, or a reset) ends it: the next world admits every rock again
//
//   scripts/unity-batch.sh -executeMethod AllTests.RunSuites -suites PostBossLegTest
public static class PostBossLegTest
{
    static int fails;
    static void Check(string what, bool ok)
    {
        Debug.Log((ok ? "[PBL] PASS  " : "[PBL] FAIL  ") + what);
        if (!ok) fails++;
    }

    public static void Run() { TestHarness.Exit(Execute()); }

    public static int Execute()
    {
        fails = 0;
        using var sandbox = new TestHarness.Sandbox();
        try
        {
            NormalBeforeTheBoss();
            LightAfterTheBoss();
            SparseOverTime();
            NormalInTheNextWorld();
        }
        finally { PostBossLeg.End(); PortalPressure.Reset(); }
        Debug.Log("[PBL] failures: " + fails);
        return fails;
    }

    // One rock slot asking every 0.4 s (about what the four slots do together in a dense phase) for `seconds`.
    static int Admitted(float seconds, int rocksInPlay)
    {
        int n = 0;
        for (int i = 0, steps = Mathf.RoundToInt(seconds / .4f); i < steps; i++)
        {
            PostBossLeg.Step(.4f);
            if (PostBossLeg.AdmitRock(rocksInPlay)) n++;
        }
        return n;
    }

    static void NormalBeforeTheBoss()
    {
        PostBossLeg.End();
        Check("no leg before a boss is over", !PostBossLeg.Active);
        Check("outside the leg every rock is admitted", PostBossLeg.AdmitRock(0) && PostBossLeg.AdmitRock(5));
        Check("outside the leg all 150 asks in 60 s are admitted", Admitted(60f, 3) == 150);
    }

    static void LightAfterTheBoss()
    {
        PostBossLeg.Begin();
        Check("a boss being over begins the leg", PostBossLeg.Active);
        Check("the first rock waits a breath", !PostBossLeg.AdmitRock(0));
        PostBossLeg.Step(PostBossLeg.MinGap + .1f);
        Check("a rock is admitted once the gap has passed", PostBossLeg.AdmitRock(0));
        PostBossLeg.Step(PostBossLeg.MaxGap + .1f);
        Check("none while a rock is already in play", !PostBossLeg.AdmitRock(PostBossLeg.MaxOnBoard));
        Check("the cap is one or two on screen", PostBossLeg.MaxOnBoard >= 1 && PostBossLeg.MaxOnBoard <= 2);
        Check("a gap of at least five seconds", PostBossLeg.MinGap >= 5f && PostBossLeg.MaxGap >= PostBossLeg.MinGap);
    }

    static void SparseOverTime()
    {
        PostBossLeg.Begin();
        int light = Admitted(120f, 0);
        // ordinary: 300 asks admitted; the leg: one per 5..8 s, 15 to 24 in two minutes
        Check("the leg admits few rocks over two minutes (" + light + ")", light >= 12 && light <= 24);
        Check("under a quarter of the ordinary rate", light <= 300 / 4);
    }

    static void NormalInTheNextWorld()
    {
        PostBossLeg.Begin();
        PortalPressure.Open(1);
        Check("the leg holds while the portal waits", PostBossLeg.Active);
        PortalPressure.Close(true);
        Check("flying the portal ends the leg", !PostBossLeg.Active);
        Check("the next world admits every rock again", Admitted(60f, 3) == 150);
        PostBossLeg.Begin();
        PortalPressure.Reset();
        Check("a reset ends the leg too", !PostBossLeg.Active);
    }
}
