using UnityEngine;

// The tutorial's pure decisions (TutorialSafety): the jump step's touch ring
// sits ABOVE the wall on every view height (it used to sit low on the screen,
// below the ship), and a death in the tutorial is taken back, not final.
public static class TutorialSafetyTest
{
    static int fails;
    static void Check(string what, bool ok)
    {
        Debug.Log((ok ? "[TS] PASS  " : "[TS] FAIL  ") + what);
        if (!ok) fails++;
    }

    public static int Execute()
    {
        fails = 0;
        const float pieceHalf = .4f, shipHalf = .3f, reachTop = 4.5f;
        // camera half heights: a short screen, the usual 5, tall phones (1080x2400)
        foreach (float half in new[] { 4f, 5f, 6.2f, 8f })
        {
            float hold = TutorialWall.HoldY(-half, half);
            float target = Mathf.Min(hold + TutorialWall.CrossMargin(pieceHalf, shipHalf) + .15f, reachTop);
            var p = TutorialSafety.JumpTouchPoint(.7f, target, 2.4f);
            // the wall holds in the upper part, the ring is above its row and not on/below the ship's start
            Check("view " + half + ": the ring is above the wall row", p.y > hold + pieceHalf);
            Check("view " + half + ": the ring is above the wall's hold line, never below the ship's lane", p.y > -half + .3f * 2f * half);
            Check("view " + half + ": the ring keeps the ship's lane x", Mathf.Approximately(p.x, .7f));
        }
        Check("a ring x far outside the lane is clamped", Mathf.Approximately(TutorialSafety.JumpTouchPoint(9f, 1f, 2.4f).x, 2.4f));

        Check("a death in the tutorial is recovered", TutorialSafety.ShouldRecover(true, true, false));
        Check("no death: nothing to recover", !TutorialSafety.ShouldRecover(true, false, false));
        Check("a death outside the tutorial runs the normal flow", !TutorialSafety.ShouldRecover(false, true, false));
        Check("a death after the lesson ended is not recovered", !TutorialSafety.ShouldRecover(true, true, true));
        Check("recovery before the heal keeps the ship damaged", TutorialSafety.LifeCounterAfterRecovery(false, 2) == 1);
        Check("recovery after the heal restores every heart", TutorialSafety.LifeCounterAfterRecovery(true, 2) == 0);
        Check("a one-heart hull recovers to full", TutorialSafety.LifeCounterAfterRecovery(false, 1) == 0);
        Check("the retry line is the approved one", TutorialSafety.TryAgainLine == "Whoa, whoa there! Let's try again."
              && TutorialSafety.TryAgainLine.Length <= 70);
        Check("the retry line speaks without throwing", TutorialScript.Speak(TutorialSafety.TryAgainLine).SyllableCount > 0);
        // crash lines: light first, playful after a recovery, the second crash sets back, no spam
        var R = TutorialSafety.CrashReply.None;
        Check("first crash: the light line", TutorialSafety.DecideCrash(1, 0, true, 99f, false, false) == TutorialSafety.CrashReply.Ouch);
        Check("first crash after a recovery: the playful line", TutorialSafety.DecideCrash(1, 1, true, 99f, false, false) == TutorialSafety.CrashReply.Playful);
        Check("second crash at the cap recovers", TutorialSafety.DecideCrash(2, 0, true, 0f, true, false) == TutorialSafety.CrashReply.Recover);
        Check("second crash below the cap says nothing", TutorialSafety.DecideCrash(2, 0, false, 99f, false, false) == R);
        Check("no line inside the cooldown", TutorialSafety.DecideCrash(1, 0, true, 3f, false, false) == R);
        Check("no line over another line", TutorialSafety.DecideCrash(1, 0, true, 99f, true, false) == R);
        Check("nothing during the fly-in, even a recovery", TutorialSafety.DecideCrash(2, 0, true, 99f, false, true) == R);
        Check("no crash, no reply", TutorialSafety.DecideCrash(0, 0, true, 99f, false, false) == R);
        Check("the crash lines speak and fit the box", TutorialScript.Speak(TutorialSafety.OuchLine).SyllableCount > 0
              && TutorialSafety.PlayfulLine.Length <= 70 && TutorialSafety.OuchLine.Length <= 70);
        return fails;
    }
}
