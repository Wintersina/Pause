using UnityEngine;
using UnityEngine.UI;

// The hyped finger-lift pause (PauseSlam): "PAUSED!!" slams in at the ship and settles into
// the pause icon; a "-1" floats by the PAUSES counter and the number ticks down live, but only
// when the resume will really spend a pause. Touch cuts it; spam reuses one set of objects.
//   scripts/unity-batch.sh -executeMethod AllTests.RunSuites -suites PauseSlamTest
public static class PauseSlamTest
{
    static int fails;
    static void Check(string what, bool ok)
    {
        Debug.Log((ok ? "[PSLAM] PASS  " : "[PSLAM] FAIL  ") + what);
        if (!ok) fails++;
    }

    public static void Run() { TestHarness.Exit(Execute()); }

    public static int Execute()
    {
        fails = 0;
        using var sandbox = new TestHarness.Sandbox();

        // ---- pure rules ----
        Check("slams on a started run with pauses left", PauseSlam.ShouldSlam(true, 3, false, false));
        Check("no slam before the first touch", !PauseSlam.ShouldSlam(false, 3, false, false));
        Check("no slam with no pauses left", !PauseSlam.ShouldSlam(true, 0, false, false));
        Check("no slam when dead / in the kill cam", !PauseSlam.ShouldSlam(true, 3, true, false) && !PauseSlam.ShouldSlam(true, 3, false, true));
        Check("a normal resume spends", PauseSlam.WillSpend(true, false, false));
        Check("a scripted free press does not spend", !PauseSlam.WillSpend(true, true, false));
        Check("a free STASIS freeze does not spend", !PauseSlam.WillSpend(true, false, true));
        Check("an unstarted run does not spend", !PauseSlam.WillSpend(false, false, false));

        // ---- timeline ----
        Check("starts huge, crashes below 1 on impact", PauseSlam.BodyScale(0f) > 2f && PauseSlam.BodyScale(PauseSlam.SlamIn) < 1f);
        Check("overshoots then settles at 1", PauseSlam.BodyScale(0.17f) > 1.05f && Mathf.Approximately(PauseSlam.BodyScale(PauseSlam.Settled), 1f));
        Check("shrinks into the icon at the end", PauseSlam.BodyScale(PauseSlam.Total - 0.001f) < 0.3f);
        Check("fully faded by the end", Mathf.Approximately(PauseSlam.Alpha(PauseSlam.Total), 0f) && PauseSlam.Alpha(PauseSlam.HoldEnd) == 1f);
        Check("total is 0.5-0.8 s", PauseSlam.Total >= 0.5f && PauseSlam.Total <= 0.8f);
        Check("squash on impact only", PauseSlam.Squash(PauseSlam.SlamIn).x > 1.1f && PauseSlam.Squash(PauseSlam.Settled) == Vector2.one);
        PauseSlam.Minus(0f, out float r0, out _, out float a0);
        PauseSlam.Minus(PauseSlam.MinusSeconds, out float r1, out _, out float a1);
        Check("the -1 rises and fades", r1 > r0 && a0 == 1f && a1 <= 0.001f);

        // ---- sizing / placement at every aspect ----
        (int w, int h, float dpi)[] screens = { (1080, 2400, 420f), (968, 2376, 420f), (1080, 1080, 160f), (1920, 1080, 96f), (3440, 1440, 96f), (800, 480, 160f) };
        foreach (var s in screens)
        {
            float px = PauseSlam.FontPxFor(new Vector2(s.w, s.h), Mathf.Max(1f, s.dpi / 160f * 2f));
            float widest = px * 6.4f * 1.12f * 1.1f;
            Check("PAUSED!! fits " + s.w + "x" + s.h, widest <= s.w * 0.95f || px <= 24.01f);
            var safe = new Rect(0, 0, s.w, s.h);
            var half = new Vector2(widest * .5f, px * .8f);
            var c = PauseSlam.Clamp(new Vector2(-500, s.h + 500), half, safe);
            Check("clamped inside the screen " + s.w + "x" + s.h, c.x - half.x >= -0.01f && c.x + half.x <= s.w + 0.01f && c.y - half.y >= -0.01f && c.y + half.y <= s.h + 0.01f);
        }

        // ---- behaviour ----
        var counterGo = new GameObject("PauseCounter", typeof(RectTransform), typeof(Text));
        var counter = counterGo.GetComponent<Text>();
        counter.fontSize = 19;
        counter.text = "PAUSES  3";
        try
        {
            buttonClicks.playerDied = false;
            score.RunHasStarted = true;
            score.pauseCounter = 3;

            PauseSlam.OnPaused(null);
            Check("a lift slams", PauseSlam.Active && PauseSlam.HoldsIcon);
            Check("the counter still reads the real 3 before the tick", PauseSlam.ShownPauses(3) == 3);
            PauseSlam.Advance(PauseSlam.MinusDelay + 0.02f);
            Check("the -1 shows beside the counter", PauseSlam.MinusVisible && PauseSlam.MinusLabel == "-1");
            PauseSlam.Advance(PauseSlam.TickDelay);
            Check("the counter ticks down live to 2", PauseSlam.ShownPauses(3) == 2);
            PauseSlam.Advance(PauseSlam.IconAt);
            Check("the icon is let through once it settles", !PauseSlam.HoldsIcon);
            PauseSlam.Advance(PauseSlam.Total);
            Check("the slam ends by itself", !PauseSlam.Active);

            // resume: the real spend lands, the HUD hands back its own number
            PauseSlam.OnResumed();
            score.pauseCounter = 2;
            PauseSlam.Advance(0.016f);
            Check("after the resume it ends on the right value", !PauseSlam.Pending && PauseSlam.ShownPauses(score.pauseCounter) == 2);

            // resume during the pop cuts it cleanly and still lands on the right value
            PauseSlam.OnPaused(null);
            PauseSlam.Advance(0.05f);
            PauseSlam.OnResumed();
            Check("touch during the pop cuts it at once", !PauseSlam.Active && !PauseSlam.HoldsIcon);
            Check("an early resume shows the new number straight away", PauseSlam.ShownPauses(2) == 1);
            score.pauseCounter = 1;
            PauseSlam.Advance(0.016f);
            Check("... and hands back after the spend", !PauseSlam.Pending && PauseSlam.ShownPauses(1) == 1);

            // spam: no stacking, no new objects
            int before = Object.FindObjectsByType<PauseSlam>(FindObjectsSortMode.None).Length;
            int graphics = Object.FindObjectsByType<Text>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length;
            score.pauseCounter = 9;
            for (int i = 0; i < 40; i++)
            {
                PauseSlam.OnPaused(null);
                PauseSlam.Advance(0.01f);
                PauseSlam.OnResumed();
                score.pauseCounter--;
                PauseSlam.Advance(0.008f);
            }
            Check("spam keeps one slam object", Object.FindObjectsByType<PauseSlam>(FindObjectsSortMode.None).Length == before);
            Check("spam creates no new labels", Object.FindObjectsByType<Text>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length == graphics);
            Check("spam leaves nothing stuck", !PauseSlam.Active && !PauseSlam.Pending);

            // free (not consumed): slams, but no -1 and no early number
            counter.text = "PAUSES  5";
            score.pauseCounter = 5;
            PauseSlam.Advance(PauseSlam.MinusSeconds + 0.1f);
            Check("the previous -1 has gone", !PauseSlam.MinusVisible);
            PauseSlam.TestForceFree = true;
            PauseSlam.OnPaused(null);
            PauseSlam.Advance(PauseSlam.TickDelay + 0.2f);
            Check("a free lift still slams", PauseSlam.Active);
            Check("... with no -1 and no early number", !PauseSlam.MinusVisible && !PauseSlam.Pending && PauseSlam.ShownPauses(5) == 5);
            PauseSlam.OnResumed();
            PauseSlam.TestForceFree = false;

            // not started / dead / out of pauses: no slam
            score.RunHasStarted = false;
            PauseSlam.OnPaused(null);
            Check("no slam before the first touch (live)", !PauseSlam.Active);
            score.RunHasStarted = true;
            score.pauseCounter = 0;
            PauseSlam.OnPaused(null);
            Check("no slam with no pauses left (live)", !PauseSlam.Active && !PauseSlam.Pending);
            score.pauseCounter = 4;
            buttonClicks.playerDied = true;
            PauseSlam.OnPaused(null);
            Check("no slam after death (live)", !PauseSlam.Active);
            buttonClicks.playerDied = false;

            // death mid-pop ends it
            PauseSlam.OnPaused(null);
            buttonClicks.playerDied = true;
            PauseSlam.Advance(0.016f);
            Check("death mid-pop cuts it", !PauseSlam.Active && !PauseSlam.Pending);
            buttonClicks.playerDied = false;

            // no per-frame allocations while it plays
            PauseSlam.OnPaused(null);
            PauseSlam.Advance(0.016f);
            long bytes = TestHarness.AllocatedBytes(() => { PauseSlam.Advance(0.016f); PauseSlam.Advance(0.016f); PauseSlam.Advance(0.016f); });
            Check("the animation allocates nothing per frame", bytes <= 0);
        }
        finally
        {
            if (PauseSlam.Live != null) Object.DestroyImmediate(PauseSlam.Live.gameObject);
            Object.DestroyImmediate(counterGo);
        }
        return fails;
    }
}
