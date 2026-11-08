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
        HoldReminderChecks();
        return fails;
    }

    // ---- finger-off reminder ----

    static int Run(ref TutorialSafety.HoldReminder r, float seconds, int step, bool pressed, bool everPressed, bool over, System.Action<TutorialSafety.HoldReminderAction> on = null)
    {
        const float dt = 1f / 60f;
        int says = 0;
        for (float t = 0f; t < seconds; t += dt)
        {
            var a = r.Tick(dt, step, TutorialSafety.PauseStepIndex(TutorialScript.Steps), pressed, everPressed, over);
            if (a == TutorialSafety.HoldReminderAction.Say) says++;
            if (on != null) on(a);
        }
        return says;
    }

    static void HoldReminderChecks()
    {
        int pause = TutorialSafety.PauseStepIndex(TutorialScript.Steps);
        Check("the pause step is the freeze step, after hold / dust / heal", pause == 3 && TutorialScript.Steps[pause].id == "freeze");
        Check("the reminder line fits the bubble and speaks", TutorialSafety.KeepFingerLine.Length <= TutorialScript.MaxLineLength
              && TutorialScript.ToPlainText(TutorialSafety.KeepFingerLine) == "Keep your finger on the screen!"
              && TutorialScript.Speak(TutorialSafety.KeepFingerLine).SyllableCount > 0);

        // lifted before the pause step: shown, then repeats
        for (int step = 0; step < pause; step++)
        {
            var r = new TutorialSafety.HoldReminder();
            int says = Run(ref r, 10f, step, false, true, false);
            Check("step " + step + ": a lifted finger gets the reminder, repeating (" + says + " in 10 s)", says >= 3 && says <= 4);
        }
        var rr = new TutorialSafety.HoldReminder();
        Check("nothing before the grace period", Run(ref rr, TutorialSafety.HoldReminderGrace * .5f, 0, false, true, false) == 0);
        rr = new TutorialSafety.HoldReminder();
        var first = TutorialSafety.HoldReminderAction.None;
        float at = 0f;
        for (float t = 0f; t < 1f && first == TutorialSafety.HoldReminderAction.None; t += 1f / 60f) { first = rr.Tick(1f / 60f, 0, pause, false, true, false); at = t; }
        Check("the first reminder comes just after the grace, not seconds later", first == TutorialSafety.HoldReminderAction.Say && at < TutorialSafety.HoldReminderGrace + .05f);
        rr = new TutorialSafety.HoldReminder();
        int repeats = Run(ref rr, TutorialSafety.HoldReminderGrace + TutorialSafety.HoldReminderRepeatSeconds * 2f + .1f, 1, false, true, false);
        Check("repeat interval is about 2.8 s", repeats == 3);
        Check("the interval suits the existing bubble pacing", TutorialSafety.HoldReminderRepeatSeconds >= 2.5f && TutorialSafety.HoldReminderRepeatSeconds <= 3f);

        // finger back down: stops, the step's own line is restored once
        rr = new TutorialSafety.HoldReminder();
        Run(ref rr, 4f, 1, false, true, false);
        Check("saying while up", rr.Saying);
        Check("finger down: the step line is restored once", rr.Tick(.016f, 1, pause, true, true, false) == TutorialSafety.HoldReminderAction.Restore);
        Check("finger down: no more reminder", !rr.Saying && Run(ref rr, 10f, 1, true, true, false) == 0);
        Check("a short lift under the grace says nothing and restores nothing",
              Run(ref rr, .1f, 1, false, true, false) == 0 && rr.Tick(.016f, 1, pause, true, true, false) == TutorialSafety.HoldReminderAction.None);

        // opening: finger never down yet
        rr = new TutorialSafety.HoldReminder();
        Check("finger not yet down once: no reminder", Run(ref rr, 10f, 0, false, false, false) == 0);

        // pause step and after: lifting is the point
        for (int step = pause; step < TutorialScript.Steps.Length; step++)
        {
            rr = new TutorialSafety.HoldReminder();
            Check("step " + step + " (" + TutorialScript.Steps[step].id + "): lifting is quiet", Run(ref rr, 10f, step, false, true, false) == 0);
        }
        // moving on into the pause step while a reminder is up cancels it without a restore
        rr = new TutorialSafety.HoldReminder();
        Run(ref rr, 4f, pause - 1, false, true, false);
        Check("reaching the pause step ends the reminder", rr.Tick(.016f, pause, pause, false, true, false) == TutorialSafety.HoldReminderAction.None && !rr.Saying);

        // finished
        rr = new TutorialSafety.HoldReminder();
        Check("tutorial over (ending / done): no reminder", Run(ref rr, 10f, 1, false, true, true) == 0);
        rr = new TutorialSafety.HoldReminder();
        Run(ref rr, 4f, 1, false, true, false);
        Check("ending tears the reminder down with no restore", rr.Tick(.016f, 1, pause, false, true, true) == TutorialSafety.HoldReminderAction.None && !rr.Saying);
        rr = new TutorialSafety.HoldReminder();
        Check("before the first step (-1): no reminder", Run(ref rr, 10f, -1, false, true, false) == 0);

        // never advances a step: the reminder is only a speech decision, and the step table stays unmet
        bool advances = false;
        foreach (var step in TutorialScript.Steps)
            if (TutorialScript.CanAdvance(step, new TutorialSignals(), new TutorialSignals { pressed = false }, true, 99f, 0f)) advances = true;
        Check("a lifted finger with no action done advances no step", !advances);
        // odd frame times
        rr = new TutorialSafety.HoldReminder();
        bool quiet = true;
        foreach (float d in new[] { float.NaN, -1f, float.PositiveInfinity, 0f })
            if (rr.Tick(d, 1, pause, false, true, false) != TutorialSafety.HoldReminderAction.None) quiet = false;
        Check("odd frame times say nothing", quiet);
    }
}
