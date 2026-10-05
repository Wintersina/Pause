using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using UnityEditor.SceneManagement;
using UnityEngine;

// The speed cap and the limit break (docs/speed-and-loops.md, section 1 and 2).
//
//   1  natural speed never passes SpeedRamp.Cap (HUD 35): every world, every
//      loop, long runs, through the walls and the closed form, whatever a
//      wall's own maxSpeed says, at any start or arrival speed
//   2  the limit break: only the blue atom's boost passes the cap; it comes
//      on over a fraction of a second, the ramp runs underneath it, and when
//      the shield ends it settles smoothly back to natural (<= the cap);
//      distance flown in it counts toward the level; x2.5 while it lasts
//   3  the ramp into 35: old versus new seconds to HUD 10 / 20 / 30 / 35 per
//      world (logged), slightly slower into the cap, unchanged early
//   4  ship start speed: old versus new seconds to the boss per world and
//      start (logged); a faster start always gets there sooner
//   5  a start at the cap (35) is not broken
//   6  "top speed" is gone: no code path or UI string refers to it, old
//      saves with the legacy best-speed field still load
//   7  no per-frame allocation in the speed code
public static class SpeedCapTest
{
    static int fails;
    static void Check(string what, bool ok)
    {
        Debug.Log((ok ? "[CAP] PASS  " : "[CAP] FAIL  ") + what);
        if (!ok) fails++;
    }

    public static void Run() { TestHarness.Exit(Execute()); }

    const BindingFlags Inst = BindingFlags.NonPublic | BindingFlags.Instance;
    static int frame;
    static float dt = 1f / 60f;

    public static int Execute()
    {
        fails = 0;
        using var sandbox = new TestHarness.Sandbox();
        try
        {
            Tunables();
            CapNeverPassed();
            CapThroughTheWalls();
            LimitBreak();
            LimitBreakCountsTowardTheLevel();
            RampTable();
            StartSpeedTable();
            StartAtTheCap();
            TopSpeedGone();
            OldSavesLoad();
            NoAllocations();
        }
        finally
        {
            ShipStartSpeed.EquippedHudOverride = null;
            SpeedRamp.FrameOverride = null;
            SpeedRamp.DeltaOverride = null;
            SpeedRamp.ResetFrameGuard();
            SpeedRamp.ResetBoost();
            ResumeSlowMo.ClockOverride = null;
            ResumeSlowMo.ResetRun();
            BossEncounter.ResetRun();
            RunLoop.Reset();
            PortalPressure.Reset();
            buttonClicks.playerDied = false;
            moveBackGround.speed = 0f;
        }
        Debug.Log("[CAP] failures: " + fails);
        return fails;
    }

    // ---- fixtures -------------------------------------------------------------

    static void Clock(float step)
    {
        frame = 50000;
        dt = step;
        SpeedRamp.FrameOverride = () => frame;
        SpeedRamp.DeltaOverride = () => dt;
        SpeedRamp.ResetFrameGuard();
        SpeedRamp.ResetBoost();
    }

    static void TickFrame(float rate, float max = 10f)
    {
        frame++;
        SpeedRamp.Tick(rate, max);
    }

    // The ramp the game shipped with before this branch (11844cc4): the
    // world's rate to HUD 30, 40% of it from there to the world's own cap.
    static readonly float[] OldCaps = { .38f, .40f, .42f, .44f };
    const float OldKnee = .30f, OldSoft = .40f;

    static float OldSpeedAfter(float v, float rate, float cap, float t)
    {
        if (v >= cap) return v;
        if (v < OldKnee)
        {
            float tk = (OldKnee - v) / rate;
            if (t <= tk) return v + rate * t;
            v = OldKnee;
            t -= tk;
        }
        return Mathf.Min(cap, v + rate * OldSoft * t);
    }

    // Old seconds from v to `target` (inf when the old cap is below it).
    static float OldSecondsTo(float v, float rate, float cap, float target)
    {
        if (target > cap + 1e-6f) return float.PositiveInfinity;
        if (v >= target) return 0f;
        float t = 0f;
        if (v < OldKnee)
        {
            if (target <= OldKnee) return (target - v) / rate;
            t = (OldKnee - v) / rate;
            v = OldKnee;
        }
        return t + (target - v) / (rate * OldSoft);
    }

    // Old seconds to fly `distance` from v (numeric: small steps).
    static float OldSecondsToCover(float v, float rate, float cap, float distance)
    {
        float t = 0f, d = 0f;
        const float step = .01f;
        while (d < distance && t < 2000f)
        {
            float next = OldSpeedAfter(v, rate, cap, step);
            d += .5f * (v + next) * step;
            v = next;
            t += step;
        }
        return t;
    }

    static float OldWorldDistance(int w)
    {
        float rate = WorldManager.Worlds[w].speedRampPerSecond, d = 0f, v = 0f;
        const float step = .01f;
        for (float t = 0f; t < WorldManager.BaselineWorldSeconds - 1e-4f; t += step)
        {
            float next = OldSpeedAfter(v, rate, OldCaps[w], step);
            d += .5f * (v + next) * step;
            v = next;
        }
        return d;
    }

    // New seconds from v to `target` along the live curve (closed form).
    static float NewSecondsTo(float v, float rate, float target)
    {
        float lo = 0f, hi = 1000f;
        if (SpeedRamp.SpeedAfter(v, rate, SpeedRamp.Cap, hi) < target - 1e-5f) return float.PositiveInfinity;
        for (int i = 0; i < 60; i++)
        {
            float mid = .5f * (lo + hi);
            if (SpeedRamp.SpeedAfter(v, rate, SpeedRamp.Cap, mid) < target - 1e-5f) lo = mid; else hi = mid;
        }
        return hi;
    }

    static void FreshScene(int world)
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        BossEncounter.ResetRun();
        RunLoop.Reset();
        PortalPressure.Reset();
        var camGo = new GameObject("Main Camera", typeof(Camera));
        camGo.tag = "MainCamera";
        var cam = camGo.GetComponent<Camera>();
        cam.orthographic = true;
        cam.orthographicSize = 5f;
        camGo.transform.position = new Vector3(0f, 0f, -10f);
        buttonClicks.playerDied = false;
        startMenu.youAreInTutorial = false;
        score.pauseCounter = 0;
        Time.timeScale = 1f;
        PlayerPrefs.SetInt(DeveloperUnlocks.EnabledKey, 0);
        PlayerPrefs.SetInt(WorldManager.PrefsCurrentWorld, world);
        Clock(1f / 60f);
        ResumeSlowMo.ClockOverride = () => frame / 60f;
        ResumeSlowMo.ResetRun();
    }

    static WorldManager World()
    {
        var wm = new GameObject("~WorldManager").AddComponent<WorldManager>();
        wm.SendMessage("Awake");
        typeof(WorldManager).GetField("distanceLeft", Inst).SetValue(wm, wm.WorldDistance);
        typeof(WorldManager).GetField("levelBegun", Inst).SetValue(wm, true);
        var apply = typeof(WorldManager).GetMethod("ApplyDifficulty", BindingFlags.NonPublic | BindingFlags.Static);
        apply.Invoke(null, new object[] { WorldManager.Current });
        return wm;
    }

    static moveBackGround[] Walls(int n, float rate, float max)
    {
        var walls = new moveBackGround[n];
        for (int i = 0; i < n; i++)
        {
            walls[i] = new GameObject("~wall" + i).AddComponent<moveBackGround>();
            walls[i].speedRampPerSecond = rate;
            walls[i].maxSpeed = max;
        }
        return walls;
    }

    // ---- 0. the tunables -----------------------------------------------------

    static void Tunables()
    {
        Check("the cap is HUD 35 (SpeedRamp.Cap .35), the ease starts at HUD 25",
              Mathf.Approximately(SpeedRamp.Cap, .35f) && SpeedRamp.CapHud == 35 && Mathf.Approximately(SpeedRamp.EaseKnee, .25f));
        Check("the limit break: +5 a blue atom, at most +10, on in 0.2 s, off at 5 HUD a second",
              Mathf.Approximately(SpeedRamp.BoostPerAtom, .05f) && Mathf.Approximately(SpeedRamp.MaxBoost, .10f) &&
              Mathf.Approximately(SpeedRamp.BoostRisePerSecond, .25f) && Mathf.Approximately(SpeedRamp.BoostSettlePerSecond, .05f));
        Check("WorldTheme has no per-world speed cap any more",
              typeof(WorldTheme).GetField("maxSpeed") == null);
        Check("LoopRules has no speed cap, loop speed bonus, KEEP FLYING or absolute ceiling any more",
              typeof(LoopRules).GetMethod("MaxSpeed") == null && typeof(LoopRules).GetField("AbsoluteMaxSpeed") == null &&
              typeof(LoopRules).GetField("MaxSpeedPerLoop") == null && typeof(LoopRules).GetField("EndlessSpeedPerSecond") == null);
    }

    // ---- 1. the cap ---------------------------------------------------------------

    static void CapNeverPassed()
    {
        float worstNatural = 0f, worstUnboosted = 0f;
        bool onlyBoosted = true, settled = true;
        int runs = 0;
        Clock(1f / 30f);
        for (int w = 0; w < WorldManager.Worlds.Length; w++)
            for (int loop = 0; loop <= 5; loop++)
                foreach (float start in new[] { 0f, .20f, .35f })
                {
                    runs++;
                    SpeedRamp.ResetBoost();
                    SpeedRamp.ResetFrameGuard();
                    moveBackGround.speed = 0f;
                    SpeedRamp.SetNatural(Mathf.Max(start, LoopRules.ArrivalSpeed(loop)));
                    float rate = WorldManager.Worlds[w].speedRampPerSecond * LoopRules.RampScale(loop);
                    // 15 minutes of flight, a blue atom every 47 s (two in a
                    // row every third one), the shield up 5.8 s each
                    float shieldLeft = 0f, sinceEnd = 99f;
                    int atoms = 0;
                    for (float t = 0f; t < 900f; t += dt)
                    {
                        if (t % 47f < dt)
                        {
                            atoms++;
                            SpeedRamp.AddBoost();
                            if (atoms % 3 == 0) SpeedRamp.AddBoost();
                            shieldLeft = 5.8f;
                        }
                        if (shieldLeft > 0f && (shieldLeft -= dt) <= 0f) { SpeedRamp.EndBoost(); sinceEnd = 0f; }
                        sinceEnd += dt;
                        TickFrame(rate);
                        worstNatural = Mathf.Max(worstNatural, SpeedRamp.Natural);
                        if (SpeedRamp.Boost <= 0f) worstUnboosted = Mathf.Max(worstUnboosted, moveBackGround.speed);
                        if (moveBackGround.speed > SpeedRamp.Cap + 1e-6f) onlyBoosted &= SpeedRamp.Boost > 0f;
                        // after a shield ends the boost is gone within its settle time
                        if (shieldLeft <= 0f && sinceEnd > SpeedRamp.MaxBoost / SpeedRamp.BoostSettlePerSecond + 2f * dt)
                            settled &= SpeedRamp.Boost == 0f && moveBackGround.speed <= SpeedRamp.Cap + 1e-6f;
                    }
                }
        Check("natural speed never passes HUD 35: 4 worlds x 6 loops x 3 starts, 15 minutes each with blue atoms (" + runs +
              " runs; worst natural " + (worstNatural * 100f).ToString("F3") + ", worst unboosted " + (worstUnboosted * 100f).ToString("F3") + ")",
              worstNatural <= SpeedRamp.Cap + 1e-6f && worstUnboosted <= SpeedRamp.Cap + 1e-6f);
        Check("speed is above the cap only while the boost is on (the limit break)", onlyBoosted);
        Check("after every shield, speed is back at or under the cap within the boost's settle time", settled);

        // Every way a level's speed is set goes through the cap.
        bool arrivals = true;
        ShipStartSpeed.EquippedHudOverride = () => 99;
        for (int loop = 0; loop <= 8; loop++) arrivals &= WorldManager.ArrivalSpeed(loop) <= SpeedRamp.Cap + 1e-6f;
        Check("a portal arrival, any loop, any ship (even a start of 99), is at most the cap", arrivals);
        Check("a run start past the cap is clamped to it", Mathf.Approximately(WorldManager.RunStartSpeed(1f), SpeedRamp.Cap));
        ShipStartSpeed.EquippedHudOverride = null;
        SpeedRamp.ResetBoost();
        moveBackGround.speed = 0f;
        SpeedRamp.SetNatural(.9f);
        Check("SetNatural clamps to the cap", Mathf.Approximately(moveBackGround.speed, SpeedRamp.Cap));
        Check("the closed form never passes the cap (Ember, loop 3, an hour)",
              SpeedRamp.SpeedAfter(0f, WorldManager.Worlds[3].speedRampPerSecond * LoopRules.RampScale(3), 10f, 3600f) <= SpeedRamp.Cap + 1e-6f);
    }

    // The scenes' walls carry their own maxSpeed (0.58 in the component's
    // default, 0.6 in older scenes): the cap applies through them anyway.
    static void CapThroughTheWalls()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        Clock(1f / 30f);
        ResumeSlowMo.ClockOverride = () => frame / 30f;
        ResumeSlowMo.ResetRun();
        buttonClicks.playerDied = false;
        score.pauseCounter = 0;
        moveBackGround.speed = 0f;
        var walls = Walls(2, WorldManager.Worlds[3].speedRampPerSecond * LoopRules.RampScale(3), .6f);
        float peak = 0f;
        for (int i = 0; i < 30 * 600; i++)
        {
            frame++;
            foreach (var w in walls) TestHarness.Send(w, "Update");
            peak = Mathf.Max(peak, moveBackGround.speed);
        }
        Check("two walls told maxSpeed 0.6, ramping at Ember's third-loop rate for 10 minutes, stop at the cap (peak HUD " +
              (peak * 100f).ToString("F2") + ")", peak <= SpeedRamp.Cap + 1e-6f && Mathf.Approximately(moveBackGround.speed, SpeedRamp.Cap));
        foreach (var w in walls) Object.DestroyImmediate(w.gameObject);
    }

    // ---- 2. the limit break ---------------------------------------------------

    static void LimitBreak()
    {
        const float rate = .00315f;
        Clock(1f / 60f);
        moveBackGround.speed = SpeedRamp.Cap;
        Check("at the cap, unboosted: not a limit break, x2", !SpeedRamp.LimitBroken &&
              Mathf.Approximately(ScoreRules.SpeedMultiplierFor(moveBackGround.speed), 2f));

        SpeedRamp.AddBoost();
        int frames = 0;
        while (SpeedRamp.Boost < SpeedRamp.BoostTarget && frames < 600) { TickFrame(rate); frames++; }
        Check("one blue atom at the cap: HUD 40 in " + (frames / 60f).ToString("F2") + " s (a short rise, not a one-frame jump)",
              ScoreRules.HudSpeed(moveBackGround.speed) == 40 && frames > 1 && frames <= 15);
        Check("... the limit break: past the cap on the boost, x" + ScoreRules.SpeedMultiplierFor(moveBackGround.speed),
              SpeedRamp.LimitBroken && Mathf.Approximately(ScoreRules.SpeedMultiplierFor(moveBackGround.speed), ScoreRules.LimitBreakMultiplier));
        Check("... the natural speed under it is still the cap", Mathf.Approximately(SpeedRamp.Natural, SpeedRamp.Cap));
        Check("... the HUD callout names it", ScoreHud.SpeedCalloutLabel(2.5f, true).StartsWith(ScoreHud.LimitBreakWord));

        SpeedRamp.AddBoost();
        SpeedRamp.AddBoost();   // a third: past MaxBoost
        for (int i = 0; i < 120; i++) TickFrame(rate);
        Check("two (or three) atoms in one shield: at most HUD " + Mathf.RoundToInt((SpeedRamp.Cap + SpeedRamp.MaxBoost) * 100f) +
              " (" + ScoreRules.HudSpeed(moveBackGround.speed) + ")",
              Mathf.Approximately(moveBackGround.speed, SpeedRamp.Cap + SpeedRamp.MaxBoost));

        // the shield ends: it settles, smoothly, to the cap
        SpeedRamp.EndBoost();
        float last = moveBackGround.speed, worstDrop = 0f;
        bool monotone = true;
        frames = 0;
        while (moveBackGround.speed > SpeedRamp.Cap + 1e-6f && frames < 6000)
        {
            TickFrame(rate);
            frames++;
            monotone &= moveBackGround.speed <= last + 1e-7f;
            worstDrop = Mathf.Max(worstDrop, last - moveBackGround.speed);
            last = moveBackGround.speed;
        }
        float settle = frames / 60f, expected = SpeedRamp.MaxBoost / SpeedRamp.BoostSettlePerSecond;
        Check("the shield ends: speed settles back to the cap in " + settle.ToString("F2") + " s (" + expected + " s expected), never jumping (" +
              (worstDrop * 100f).ToString("F3") + " HUD a frame at most)",
              monotone && Mathf.Abs(settle - expected) < .05f && worstDrop <= SpeedRamp.BoostSettlePerSecond / 60f + 1e-6f);
        bool holds = true;
        for (int i = 0; i < 60 * 60; i++) { TickFrame(rate); holds &= moveBackGround.speed <= SpeedRamp.Cap + 1e-6f; }
        Check("... and stays at the cap after it (a minute)", holds && SpeedRamp.Boost == 0f && !SpeedRamp.LimitBroken);

        // below the cap a boost is a boost, and the ramp runs underneath it
        Clock(1f / 60f);
        moveBackGround.speed = .20f;
        SpeedRamp.AddBoost();
        for (int i = 0; i < 300; i++) TickFrame(rate);
        float want = SpeedRamp.SpeedAfter(.20f, rate, SpeedRamp.Cap, 5f);
        Check("boosted below the cap, the ramp keeps climbing underneath (natural " + SpeedRamp.Natural.ToString("F4") + " vs " + want.ToString("F4") + ")",
              Mathf.Abs(SpeedRamp.Natural - want) < 2e-4f && Mathf.Abs(SpeedRamp.Boost - SpeedRamp.BoostPerAtom) < 1e-6f &&
              !SpeedRamp.LimitBroken);
        SpeedRamp.EndBoost();
        for (int i = 0; i < 120; i++) TickFrame(rate);
        Check("... and when it ends the ship is where the ramp would have it (no -5 under, the old bug)",
              Mathf.Abs(moveBackGround.speed - SpeedRamp.SpeedAfter(.20f, rate, SpeedRamp.Cap, 7f)) < 2e-4f);

        // a boss takes the speed over
        moveBackGround.speed = SpeedRamp.Cap;
        SpeedRamp.AddBoost();
        for (int i = 0; i < 30; i++) TickFrame(rate);
        SpeedRamp.CancelBoost();
        Check("CancelBoost (a boss intro): the boost is gone at once, speed at natural",
              SpeedRamp.Boost == 0f && SpeedRamp.BoostTarget == 0f && Mathf.Approximately(moveBackGround.speed, SpeedRamp.Cap));

        // an arrival during a boost keeps it riding on top
        SpeedRamp.AddBoost();
        for (int i = 0; i < 30; i++) TickFrame(rate);
        SpeedRamp.SetNatural(.10f);
        Check("a portal arrival during a boost: natural to the arrival speed, the boost rides on",
              Mathf.Approximately(SpeedRamp.Natural, .10f) && Mathf.Approximately(moveBackGround.speed, .10f + SpeedRamp.BoostPerAtom));
        SpeedRamp.ResetBoost();
        Check("speed set to 0 under a boost takes the boost with it", Zeroed());
    }

    static bool Zeroed()
    {
        Clock(1f / 60f);
        moveBackGround.speed = .3f;
        SpeedRamp.AddBoost();
        for (int i = 0; i < 30; i++) TickFrame(0f);
        moveBackGround.speed = 0f;
        TickFrame(0f);
        bool ok = SpeedRamp.Boost <= SpeedRamp.BoostRisePerSecond * dt + 1e-6f && SpeedRamp.Natural == 0f;
        SpeedRamp.ResetBoost();
        return ok;
    }

    // Distance in a boost counts: the level clock integrates the effective
    // speed, so a limit break brings the boss nearer.
    static void LimitBreakCountsTowardTheLevel()
    {
        FreshScene(0);
        var wm = World();
        moveBackGround.speed = SpeedRamp.Cap;
        float before = wm.DistanceLeft;
        wm.Tick(1f);
        float plain = before - wm.DistanceLeft;
        SpeedRamp.AddBoost();
        SpeedRamp.AddBoost();
        for (int i = 0; i < 60; i++) TickFrame(0f);
        before = wm.DistanceLeft;
        float eta = wm.SecondsLeftInWorld;
        wm.Tick(1f);
        float boosted = before - wm.DistanceLeft;
        Check("a second of limit break flies " + boosted.ToString("F3") + " of the level, against " + plain.ToString("F3") + " at the cap",
              Mathf.Approximately(boosted, SpeedRamp.Cap + SpeedRamp.MaxBoost) && Mathf.Approximately(plain, SpeedRamp.Cap));
        Check("the boss estimate is taken on natural speed (the boost only eats the distance faster)",
              Mathf.Abs(eta - SpeedRamp.SecondsToCover(SpeedRamp.Cap, WorldManager.Worlds[0].speedRampPerSecond, SpeedRamp.Cap, before)) < .01f);
        SpeedRamp.ResetBoost();
        Object.DestroyImmediate(wm.gameObject);

        // a whole Space level with a blue atom early: the boss comes sooner
        float plainSeconds = FlyLevel(0, false), boostSeconds = FlyLevel(0, true);
        Check("a stock Space level with a blue atom at 30 s reaches the boss sooner (" + boostSeconds.ToString("F1") + " s vs " +
              plainSeconds.ToString("F1") + " s)", boostSeconds < plainSeconds - .3f && plainSeconds < 121f);
    }

    static float FlyLevel(int world, bool atom)
    {
        FreshScene(world);
        var wm = World();
        var walls = Walls(2, WorldManager.Worlds[world].speedRampPerSecond, SpeedRamp.Cap);
        moveBackGround.speed = 0f;
        float t = 0f;
        for (int i = 0; i < 60 * 300 && !BossEncounter.Running; i++)
        {
            if (atom && i == 60 * 30) SpeedRamp.AddBoost();
            if (atom && i == 60 * 36) SpeedRamp.EndBoost();
            frame++;
            foreach (var w in walls) TestHarness.Send(w, "Update");
            if (WorldManager.Flying) wm.Tick(dt);
            t += dt;
        }
        bool reached = BossEncounter.Running;
        BossEncounter.ResetRun();
        SpeedRamp.ResetBoost();
        Object.DestroyImmediate(wm.gameObject);
        foreach (var w in walls) Object.DestroyImmediate(w.gameObject);
        return reached ? t : float.PositiveInfinity;
    }

    // ---- 3. the ramp table ------------------------------------------------------

    static readonly int[] Marks = { 10, 20, 30, 35 };

    static void RampTable()
    {
        Debug.Log("[CAP] TABLE ramp: seconds from 0 to HUD 10 / 20 / 30 / 35, old (cap) / new");
        bool early = true, slower = true, slightly = true, reached = true, tickAgrees = true;
        for (int w = 0; w < WorldManager.Worlds.Length; w++)
        {
            var theme = WorldManager.Worlds[w];
            float rate = theme.speedRampPerSecond;
            var cells = new List<string>();
            var oldT = new float[Marks.Length];
            var newT = new float[Marks.Length];
            for (int i = 0; i < Marks.Length; i++)
            {
                float target = Marks[i] / 100f;
                oldT[i] = OldSecondsTo(0f, rate, OldCaps[w], target);
                newT[i] = NewSecondsTo(0f, rate, target);
                cells.Add(Marks[i] + ": " + oldT[i].ToString("F1") + " / " + newT[i].ToString("F1"));
            }
            float oldCapAt = OldSecondsTo(0f, rate, OldCaps[w], OldCaps[w]);
            Debug.Log(string.Format("[CAP] TABLE {0,-8} rate {1:F3} HUD/s | {2} | old cap {3:F0} at {4:F0} s",
                                    theme.displayName, rate * 100f, string.Join(" | ", cells), OldCaps[w] * 100f, oldCapAt));
            early &= Mathf.Abs(oldT[0] - newT[0]) < .2f && Mathf.Abs(oldT[1] - newT[1]) < .2f;
            slower &= newT[2] > oldT[2] && newT[3] > oldT[3];
            slightly &= newT[2] - oldT[2] < 6f && newT[3] - oldT[3] < 15f;
            reached &= newT[3] < WorldManager.BaselineWorldSeconds + 30f;

            // the real Tick reaches each mark when the closed form says
            Clock(1f / 60f);
            moveBackGround.speed = 0f;
            int mark = 0;
            for (int f = 1; f < 60 * 200 && mark < Marks.Length; f++)
            {
                TickFrame(rate);
                while (mark < Marks.Length && moveBackGround.speed >= Marks[mark] / 100f - 1e-5f)
                {
                    tickAgrees &= Mathf.Abs(f / 60f - newT[mark]) < .1f;
                    mark++;
                }
            }
            tickAgrees &= mark == Marks.Length;
        }
        Check("the first 80 s are unchanged: HUD 10 and 20 come exactly when they did", early);
        Check("the ramp into 35 is slower than before: HUD 30 and 35 both come later", slower);
        Check("... slightly: HUD 30 under 6 s later, HUD 35 under 15 s later", slightly);
        Check("... and 35 is reached in every world (inside 150 s), where it stops", reached);
        Check("frame-by-frame Tick hits every mark within 0.1 s of the closed form", tickAgrees);
    }

    // ---- 4. ship start speed --------------------------------------------------------

    static readonly int[] Starts = { 0, 5, 10, 15, 20, 25, 30 };

    static void StartSpeedTable()
    {
        Debug.Log("[CAP] TABLE time to the boss by start speed (HUD 0/5/10/15/20/25/30), seconds old / new");
        bool sooner = true, baseline = true, lasting = true;
        var passOld = new float[Starts.Length];
        var passNew = new float[Starts.Length];
        for (int w = 0; w < WorldManager.Worlds.Length; w++)
        {
            var theme = WorldManager.Worlds[w];
            float rate = theme.speedRampPerSecond, oldD = OldWorldDistance(w), newD = WorldManager.WorldDistanceFor(w);
            var cells = new List<string>();
            float prev = float.PositiveInfinity;
            for (int i = 0; i < Starts.Length; i++)
            {
                float v = Starts[i] / 100f;
                float o = OldSecondsToCover(v, rate, OldCaps[w], oldD);
                float n = SpeedRamp.SecondsToCover(v, rate, SpeedRamp.Cap, newD);
                passOld[i] += o;
                passNew[i] += n;
                cells.Add(Starts[i] + ": " + o.ToString("F0") + "/" + n.ToString("F0"));
                sooner &= n < prev - 1f;
                prev = n;
                if (i == 0) baseline &= Mathf.Abs(n - WorldManager.BaselineWorldSeconds) < .2f && Mathf.Abs(o - WorldManager.BaselineWorldSeconds) < .5f;
            }
            float n30 = SpeedRamp.SecondsToCover(.30f, rate, SpeedRamp.Cap, newD);
            lasting &= n30 <= WorldManager.BaselineWorldSeconds * .7f;
            Debug.Log(string.Format("[CAP] TABLE {0,-8} distance old {1:F1} new {2:F1} | {3}", theme.displayName, oldD, newD, string.Join(" | ", cells)));
        }
        Debug.Log("[CAP] TABLE first pass (four levels of flight, no boss fights) by start: " +
                  string.Join(" | ", Starts.Select((s, i) => s + ": " + passOld[i].ToString("F0") + "/" + passNew[i].ToString("F0"))));
        Check("a stock start (0) meets every boss at 120 s, old and new", baseline);
        Check("every faster start reaches every boss strictly sooner", sooner);
        Check("the fastest start (30) reaches every boss at least 30% sooner than a stock start: the lasting advantage", lasting);
        bool passSooner = true;
        for (int i = 1; i < Starts.Length; i++) passSooner &= passNew[i] < passNew[i - 1];
        Check("... so a faster ship flies more worlds (and loops) in the same time", passSooner);
    }

    // ---- 5. a start at the cap ---------------------------------------------------------

    static void StartAtTheCap()
    {
        ShipStartSpeed.EquippedHudOverride = () => 35;
        Check("a start of 35 runs at the cap", Mathf.Approximately(WorldManager.RunStartSpeed(0f), SpeedRamp.Cap) &&
              Mathf.Approximately(WorldManager.ArrivalSpeed(0), SpeedRamp.Cap));
        Check("... skips the calm arrival (HUD " + enmiesOnBoard.FastArrivalHudSpeed + "+)", SpeedRamp.CapHud >= enmiesOnBoard.FastArrivalHudSpeed);
        float shortest = float.PositiveInfinity;
        for (int w = 0; w < WorldManager.Worlds.Length; w++)
            shortest = Mathf.Min(shortest, SpeedRamp.SecondsToCover(SpeedRamp.Cap, WorldManager.Worlds[w].speedRampPerSecond, SpeedRamp.Cap,
                                                                    WorldManager.WorldDistanceFor(w)));
        Check("... its shortest level (" + shortest.ToString("F0") + " s) is longer than the boss warning's lead (" + BossWarningConfig.LeadSeconds + " s)",
              shortest > BossWarningConfig.LeadSeconds + 10f);
        Check("... density at arrival is the HUD 35 density (no step past it)",
              Mathf.Approximately(EnemyDensity.RateScale(35f), EnemyDensity.RateScale(SpeedRamp.Cap * 100f)));

        // flown: the boss is warned of, then comes
        FreshScene(0);
        var wm = World();
        var walls = Walls(2, WorldManager.Worlds[0].speedRampPerSecond, SpeedRamp.Cap);
        moveBackGround.speed = WorldManager.RunStartSpeed(0f);
        float t = 0f, aheadAt = -1f;
        bool capped = true;
        for (int i = 0; i < 60 * 200 && !BossEncounter.Running; i++)
        {
            frame++;
            foreach (var w in walls) TestHarness.Send(w, "Update");
            if (WorldManager.Flying) wm.Tick(dt);
            t += dt;
            capped &= moveBackGround.speed <= SpeedRamp.Cap + 1e-6f;
            if (aheadAt < 0f && BossWarning.Read(wm) == BossWarningInput.Ahead && wm.SecondsLeftInWorld <= BossWarningConfig.LeadSeconds) aheadAt = t;
        }
        Check("a start-35 Space level: the boss at " + t.ToString("F1") + " s, the warning window opens at " + aheadAt.ToString("F1") +
              " s, speed held at the cap", BossEncounter.Running && aheadAt > 0f && t - aheadAt >= BossWarningConfig.LeadSeconds - 1f && capped);
        BossEncounter.ResetRun();
        Object.DestroyImmediate(wm.gameObject);
        foreach (var w in walls) Object.DestroyImmediate(w.gameObject);
        ShipStartSpeed.EquippedHudOverride = null;
    }

    // ---- 6. "top speed" is gone ----------------------------------------------------------

    // Where the words may still appear, and why (file, a substring the line
    // must contain).
    static readonly (string file, string mustContain)[] Allowed =
    {
        ("Core/CloudSave/ProgressSnapshot.cs", ""),       // the legacy save field: old saves keep it
        ("Core/CloudSave/ProgressMerge.cs", ""),          // ... and it still merges (max)
        ("Core/StringHolder.cs", "leaderboard_highest_speed_reached"),   // generated Play Games resource ids
        ("Core/Leaderboards/LeaderboardBoards.cs", "RetiredSpeedBoard"), // the retired id, so old queues drop it
        ("Gameplay/playerIsDead.cs", "FormerlySerializedAs"),            // keeps the scene's wiring
    };

    static void TopSpeedGone()
    {
        var pattern = new Regex(@"top\s*_?speed|best\s*_?speed|highest\s*_?speed|peak\s*_?speed|speed\s*_?record|SpeedLine|Your Speed",
                                RegexOptions.IgnoreCase);
        var hits = new List<string>();
        foreach (string path in Directory.GetFiles("Assets/Scripts", "*.cs", SearchOption.AllDirectories))
        {
            string rel = path.Replace('\\', '/').Substring("Assets/Scripts/".Length);
            var lines = File.ReadAllLines(path);
            for (int i = 0; i < lines.Length; i++)
            {
                if (!pattern.IsMatch(lines[i])) continue;
                bool ok = Allowed.Any(a => a.file == rel && (a.mustContain == "" || lines[i].Contains(a.mustContain)));
                if (!ok) hits.Add(rel + ":" + (i + 1) + ": " + lines[i].Trim());
            }
        }
        foreach (var h in hits.Take(20)) Debug.Log("[CAP] top speed still referenced: " + h);
        Check("no script refers to a top / best / highest speed outside the legacy save field and the retired board id (" + hits.Count + " hits)",
              hits.Count == 0);

        string scene = File.ReadAllText("Assets/Scenes/gameS1.unity");
        Check("gameS1 has no speed text left on the death panel (\"Your Speed\")", !scene.Contains("m_Text: Your Speed"));
        Check("no speed board in the leaderboard table",
              LeaderboardBoards.All.All(b => b.id != LeaderboardBoards.RetiredSpeedBoard && b.androidId != StringHolder.leaderboard_highest_speed_reached));
        Check("the run stats carry no speed", typeof(LeaderboardRunStats).GetField("topSpeed") == null);
        Check("the death panel's results carry no speed",
              typeof(DeathPanelView.Results).GetField("bestSpeed") == null && typeof(DeathPanelView.Results).GetField("runSpeed") == null);
        Check("score has no top-speed dust tunables", typeof(score).GetField("topSpeed") == null &&
              typeof(score).GetField("dustPerSecondAtTopSpeed") == null);
        Check("the codex, tutorial and Stardock text say nothing of a top speed", NoSpeedRecordText());
        Check("the live SPEED read-out stays in the gameplay HUD", scene.Contains("m_Text: 'Speed : 0.0'"));
    }

    static bool NoSpeedRecordText()
    {
        var pattern = new Regex(@"top speed|best speed|highest speed|speed record|fastest run", RegexOptions.IgnoreCase);
        foreach (var e in CodexCatalogue.All)
            if (pattern.IsMatch(e.name + " " + e.lore)) { Debug.Log("[CAP] codex " + e.name + " mentions a speed record"); return false; }
        foreach (var dir in new[] { "Assets/Scripts/Tutorial", "Assets/Scripts/Menu", "Assets/Scripts/Shop", "Assets/Scripts/UI", "Assets/Scripts/Codex" })
        {
            if (!Directory.Exists(dir)) continue;
            foreach (string path in Directory.GetFiles(dir, "*.cs", SearchOption.AllDirectories))
                if (pattern.IsMatch(File.ReadAllText(path))) { Debug.Log("[CAP] " + path + " mentions a speed record"); return false; }
        }
        return true;
    }

    // An old save (with the best speed in it) still loads; a new save
    // without it loads too; the game no longer writes the key.
    static void OldSavesLoad()
    {
        const string old = "{\"schemaVersion\":1,\"savedAtUtc\":5,\"currency\":12.5,\"highestSpeed\":46,\"highestWorld\":2,\"boughtShips\":[1,3]}";
        var r = ProgressSnapshot.TryParse(old, out var snap);
        Check("an old save with its best speed parses (" + r + ")", r == ProgressSnapshot.ParseResult.Ok && snap.highestSpeed == 46f &&
              snap.highestWorld == 2);
        r = ProgressSnapshot.TryParse("{\"schemaVersion\":1,\"savedAtUtc\":5,\"highestWorld\":1}", out snap);
        Check("a save without the field parses (it reads 0)", r == ProgressSnapshot.ParseResult.Ok && snap.highestSpeed == 0f);
        string dead = File.ReadAllText("Assets/Scripts/Gameplay/playerIsDead.cs") + File.ReadAllText("Assets/Scripts/UI/DeathPanelView.cs") +
                      File.ReadAllText("Assets/Scripts/Core/Leaderboards/LeaderboardRunTracker.cs");
        Check("the end of a run neither reads nor writes the old best-speed key", !dead.Contains("\"HighestSpeed\""));
    }

    // ---- 7. allocations --------------------------------------------------------

    static void NoAllocations()
    {
        bool meter = TestHarness.AllocMeterWorks(out long control);
        Clock(1f / 60f);
        moveBackGround.speed = .3f;
        float sink = 0f;
        System.Action work = () =>
        {
            for (int i = 0; i < 600; i++)
            {
                if (i == 100) SpeedRamp.AddBoost();
                if (i == 400) SpeedRamp.EndBoost();
                TickFrame(.00315f);
                sink += SpeedRamp.Natural + SpeedRamp.Boost + (SpeedRamp.LimitBroken ? 1f : 0f);
                sink += SpeedRamp.SecondsToCover(SpeedRamp.Natural, .00315f, SpeedRamp.Cap, 12f);
                sink += ScoreRules.SpeedMultiplierFor(moveBackGround.speed) + ScoreRules.FlightDust(moveBackGround.speed, dt);
                sink += achievementAPICalls.SpeedMilestones.Reached(SpeedRamp.Natural, SpeedRamp.Boost);
            }
        };
        work();
        Clock(1f / 60f);
        moveBackGround.speed = .3f;
        long bytes = TestHarness.AllocatedBytes(work);
        Check("the speed code allocates nothing per frame (Tick, the boost, the estimate, the multiplier, the dust, the milestones: " +
              bytes + " bytes over 600 frames; meter control " + control + ")", meter && bytes == 0 && sink > 0f);
        SpeedRamp.ResetBoost();
    }
}
