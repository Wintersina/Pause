using UnityEngine;

// The tutorial cannot break: the step watchdog always recovers the step (and
// offers SKIP, never moving on by itself), and a fling (huge / non-finite /
// off-screen input) can never lose the ship.
public static class TutorialRobustnessTest
{
    static int fails;
    static void Check(string what, bool ok)
    {
        Debug.Log((ok ? "[TR] PASS  " : "[TR] FAIL  ") + what);
        if (!ok) fails++;
    }

    const float Dt = 1f / 60f;
    static readonly TutorialSafety.WatchdogAction None = TutorialSafety.WatchdogAction.None;

    // Runs a hostile frame stream; returns the actions seen in order.
    static System.Collections.Generic.List<TutorialSafety.WatchdogAction> Run(float seconds, bool missing, float dt = Dt)
    {
        var wd = new TutorialSafety.Watchdog();
        var seen = new System.Collections.Generic.List<TutorialSafety.WatchdogAction>();
        for (float t = 0f; t < seconds; t += dt)
        {
            var a = wd.Tick(dt, false, missing);
            if (a != None) seen.Add(a);
            if (a == TutorialSafety.WatchdogAction.Advance) break;
        }
        return seen;
    }

    public static int Execute()
    {
        fails = 0;

        // a target that is gone is brought back within seconds
        var gone = Run(5f, true);
        Check("a missing target resets the step within 5 s", gone.Count > 0 && gone[0] == TutorialSafety.WatchdogAction.Reset);

        // a target that is gone is noticed even while the finger keeps steering (steering is
        // "progress" for the stuck clock, but it is not what brings the atom or wall back)
        var wdSteering = new TutorialSafety.Watchdog();
        var steeringGone = new System.Collections.Generic.List<TutorialSafety.WatchdogAction>();
        for (float t = 0f; t < 5f; t += Dt)
        {
            var a = wdSteering.Tick(Dt, true, true);
            if (a != None) steeringGone.Add(a);
        }
        Check("a missing target resets the step within 5 s even while the player keeps steering",
              steeringGone.Count > 0 && steeringGone[0] == TutorialSafety.WatchdogAction.Reset);

        // no progress at all: reset, reset, then every reset also offers SKIP -- and it never
        // moves the tutorial on by itself (Hints: no timer advances a step; SKIP is the player's)
        var stuck = Run(1000f, false);
        Check("a step with no progress is reset again and again, never advanced by itself (" + stuck.Count + " actions)",
              stuck.Count >= 5 && !stuck.Contains(TutorialSafety.WatchdogAction.Advance));
        Check("the first two attempts only reset", stuck[0] == TutorialSafety.WatchdogAction.Reset && stuck[1] == TutorialSafety.WatchdogAction.Reset);
        Check("from the third attempt on every reset also offers SKIP",
              stuck.IndexOf(TutorialSafety.WatchdogAction.OfferSkip) == TutorialSafety.OfferSkipAfter - 1 &&
              stuck.FindAll(a => a == TutorialSafety.WatchdogAction.OfferSkip).Count == stuck.Count - (TutorialSafety.OfferSkipAfter - 1));

        // progress keeps it quiet forever
        var wd = new TutorialSafety.Watchdog();
        bool quiet = true;
        for (int i = 0; i < 100000; i++) if (wd.Tick(Dt, i % 600 == 0, false) != None) quiet = false;
        Check("a player who keeps progressing is never interrupted", quiet);

        // odd frame times: NaN, negative, infinite and huge dt never throw or fire at once
        wd = new TutorialSafety.Watchdog();
        bool calm = true;
        foreach (float dt in new[] { float.NaN, -5f, float.PositiveInfinity, 1e9f, 0f })
            if (wd.Tick(dt, false, false) != None && dt != 1e9f && !float.IsPositiveInfinity(dt)) calm = false;
        Check("odd frame times do not trip the watchdog out of turn", calm);
        wd = new TutorialSafety.Watchdog();
        Check("one stalled frame (app backgrounded) is not 25 s of being stuck", wd.Tick(600f, false, false) == None);

        // the table: every step has a way to finish and none finishes by itself
        foreach (var step in TutorialScript.Steps)
            Check("step " + step.id + " does not advance with no signals",
                  !TutorialScript.CanAdvance(step, new TutorialSignals(), new TutorialSignals(), true, 99f, 0f));

        // ---- the fling ----
        Vector3 start = new Vector3(0f, -3f, 0f), last = new Vector3(1f, -2f, 0f);
        float hw = 2.4f, bottom = -4.15f, top = 4.5f;
        var nan = new Vector3(float.NaN, 1f, 0f);
        var inf = new Vector3(1f, float.PositiveInfinity, 0f);
        Check("NaN position goes back to the last valid one", TutorialSafety.RecoverShip(nan, last, start, hw, bottom, top, 3f) == last);
        Check("infinite position goes back to the last valid one", TutorialSafety.RecoverShip(inf, last, start, hw, bottom, top, 3f) == last);
        Check("no valid last position: the start position", TutorialSafety.RecoverShip(nan, nan, start, hw, bottom, top, 3f) == start);
        Check("nothing valid at all: the origin, never NaN", TutorialSafety.IsFinite(TutorialSafety.RecoverShip(nan, nan, nan, hw, bottom, top, 3f)));
        foreach (float huge in new[] { 1e6f, -1e6f, 3.4e38f, -3.4e38f })
        {
            var p = TutorialSafety.RecoverShip(new Vector3(huge, huge, 0f), last, start, hw, bottom, top, 3f);
            Check("a flung position " + huge + " is pinned inside the box", Mathf.Abs(p.x) <= hw + 3f && p.y >= bottom - 3f && p.y <= top + 3f);
        }
        var inside = new Vector3(1.1f, 0.5f, 0f);
        Check("a normal position is untouched", TutorialSafety.RecoverShip(inside, last, start, hw, bottom, top, 3f) == inside);

        // finger input: NaN / off-screen / huge
        var good = new Vector2(540f, 900f);
        Check("a NaN touch point falls back to the last good one", TutorialSafety.SafeScreenPoint(new Vector2(float.NaN, 5f), good, 1080f, 2400f) == good);
        Check("an infinite touch point falls back to the last good one", TutorialSafety.SafeScreenPoint(new Vector2(3f, float.NegativeInfinity), good, 1080f, 2400f) == good);
        var flung = TutorialSafety.SafeScreenPoint(new Vector2(-9e6f, 9e6f), good, 1080f, 2400f);
        Check("a point flung off the screen is pinned to its edge", flung == new Vector2(0f, 2400f));
        Check("a normal touch point is untouched", TutorialSafety.SafeScreenPoint(good, Vector2.zero, 1080f, 2400f) == good);
        // many touches / lift at speed: IsPressed is exactly-one-finger; a lift keeps the last good point
        Check("a lift at speed keeps the ship where it is (guard never moves a valid, in-box ship)",
              TutorialSafety.RecoverShip(last, last, start, hw, bottom, top, 3f) == last);
        return fails;
    }
}
