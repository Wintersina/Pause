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

    static bool Lift(int step) { return TutorialSafety.WantsLift(TutorialScript.Steps[step]); }

    static int Run(ref TutorialSafety.HoldReminder r, float seconds, int step, bool pressed, bool everPressed, bool over)
    {
        const float dt = 1f / 60f;
        int says = 0;
        bool lift = step >= 0 && Lift(step);
        for (float t = 0f; t < seconds; t += dt)
            if (r.Tick(dt, step, lift, pressed, everPressed, over) == TutorialSafety.HoldReminderAction.Say) says++;
        return says;
    }

    static void HoldReminderChecks()
    {
        var steps = TutorialScript.Steps;
        Check("the lift steps are freeze, jump and strike only",
              Lift(3) && Lift(4) && Lift(5) && steps[3].id == "freeze" && steps[4].id == "jump" && steps[5].id == "strike"
              && !Lift(0) && !Lift(1) && !Lift(2) && !Lift(6) && !Lift(7) && !Lift(8));
        Check("the reminder line fits the bubble and speaks", TutorialSafety.KeepFingerLine.Length <= TutorialScript.MaxLineLength
              && TutorialScript.ToPlainText(TutorialSafety.KeepFingerLine) == "Keep your finger on the screen!"
              && TutorialScript.Speak(TutorialSafety.KeepFingerLine).SyllableCount > 0);

        // lifted on every step that does not ask for it, before or after the pause lesson: shown, repeating
        for (int step = 0; step < steps.Length; step++)
        {
            var r = new TutorialSafety.HoldReminder();
            int says = Run(ref r, 10f, step, false, true, false);
            if (Lift(step)) Check("step " + step + " (" + steps[step].id + "): the requested lift never nags", says == 0);
            else Check("step " + step + " (" + steps[step].id + "): a lifted finger gets the reminder, repeating (" + says + " in 10 s)", says >= 3 && says <= 4);
        }
        var rr = new TutorialSafety.HoldReminder();
        Check("nothing before the grace period", Run(ref rr, TutorialSafety.HoldReminderGrace * .5f, 0, false, true, false) == 0);
        rr = new TutorialSafety.HoldReminder();
        var first = TutorialSafety.HoldReminderAction.None;
        float at = 0f;
        for (float t = 0f; t < 1f && first == TutorialSafety.HoldReminderAction.None; t += 1f / 60f) { first = rr.Tick(1f / 60f, 0, false, false, true, false); at = t; }
        Check("the first reminder comes just after the grace, not seconds later", first == TutorialSafety.HoldReminderAction.Say && at < TutorialSafety.HoldReminderGrace + .05f);
        rr = new TutorialSafety.HoldReminder();
        Check("repeat interval is about 2.8 s", Run(ref rr, TutorialSafety.HoldReminderGrace + TutorialSafety.HoldReminderRepeatSeconds * 2f + .1f, 1, false, true, false) == 3);
        Check("the interval suits the existing bubble pacing", TutorialSafety.HoldReminderRepeatSeconds >= 2.5f && TutorialSafety.HoldReminderRepeatSeconds <= 3f);

        // finger back down: stops, the step's own line is restored once (also after the pause lesson)
        foreach (int step in new[] { 1, 6 })
        {
            rr = new TutorialSafety.HoldReminder();
            Run(ref rr, 4f, step, false, true, false);
            Check("step " + step + ": saying while up", rr.Saying);
            Check("step " + step + ": finger down restores the step line once", rr.Tick(.016f, step, false, true, true, false) == TutorialSafety.HoldReminderAction.Restore);
            Check("step " + step + ": finger down, no more reminder", !rr.Saying && Run(ref rr, 10f, step, true, true, false) == 0);
        }
        Check("a short lift under the grace says nothing and restores nothing",
              Run(ref rr, .1f, 1, false, true, false) == 0 && rr.Tick(.016f, 1, false, true, true, false) == TutorialSafety.HoldReminderAction.None);

        // after the requested lift is done, the next step wants the finger back: nag again
        rr = new TutorialSafety.HoldReminder();
        Check("lift on the freeze step: quiet", Run(ref rr, 5f, 3, false, true, false) == 0);
        Check("then still up on the next step that wants it down: nags", Run(ref rr, 5f, 6, false, true, false) >= 1);
        // a nag already up when a lift step begins is dropped, with no restore
        rr = new TutorialSafety.HoldReminder();
        Run(ref rr, 4f, 2, false, true, false);
        Check("reaching a lift step ends the reminder", rr.Tick(.016f, 3, true, false, true, false) == TutorialSafety.HoldReminderAction.None && !rr.Saying);

        // opening: finger never down yet
        rr = new TutorialSafety.HoldReminder();
        Check("finger not yet down once: no reminder", Run(ref rr, 10f, 0, false, false, false) == 0);

        // finished
        rr = new TutorialSafety.HoldReminder();
        Check("tutorial complete (ending / done): no reminder on any step", Run(ref rr, 10f, 8, false, true, true) == 0);
        rr = new TutorialSafety.HoldReminder();
        Run(ref rr, 4f, 8, false, true, false);
        Check("completing tears the reminder down with no restore", rr.Tick(.016f, 8, false, false, true, true) == TutorialSafety.HoldReminderAction.None && !rr.Saying);
        rr = new TutorialSafety.HoldReminder();
        Check("before the first step (-1): no reminder", Run(ref rr, 10f, -1, false, true, false) == 0);

        // never advances a step: it is only a speech decision, the step table stays unmet
        bool advances = false;
        foreach (var step in steps)
            if (TutorialScript.CanAdvance(step, new TutorialSignals(), new TutorialSignals { pressed = false }, true, 99f, 0f)) advances = true;
        Check("a lifted finger with no action done advances no step", !advances);
        rr = new TutorialSafety.HoldReminder();
        bool quiet = true;
        foreach (float d in new[] { float.NaN, -1f, float.PositiveInfinity, 0f })
            if (rr.Tick(d, 1, false, false, true, false) != TutorialSafety.HoldReminderAction.None) quiet = false;
        Check("odd frame times say nothing", quiet);
    }
}
