using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

// BOSS INCOMING (BossWarning / BossWarningHud):
//
//   1  the countdown's rules, on their own: fires once at the lead, holds
//      while paused, follows a moved estimate without a jump and never
//      upwards, lands on zero with the boss, shortened / skipped warnings,
//      no second warning on the same visit
//   2  a stock flight in every world: the warning starts ~30 s of flight
//      before the boss, once, in the world's accent; banner, then chip,
//      T-10, T-3, and the hand-off when the boss intro starts
//   3  pausing holds the count and every pose of the warning
//   4  a blue atom's speed boost (and its end) mid-countdown
//   5  a level entered with less than the lead left
//   6  loops and portals: never while a portal waits (any world, the loop
//      portal too), none for a boss already done, warned again on a loop
//   7  the tutorial has none
//   8  death hides it at once
//   9  layout: clear of the read-out and the quick actions, inside the
//      safe area, at 1080x2520, 1080x1920 and the other test screens
//  10  nothing allocated per frame while it runs
//  11  the boss's name stays secret until met; tunables; the calm hook is off
//
//   Unity -batchmode -quit -projectPath Pause -executeMethod BossWarningTest.Run
public static class BossWarningTest
{
    static int fails;
    static void Check(string what, bool ok)
    {
        Debug.Log((ok ? "[BW] PASS  " : "[BW] FAIL  ") + what);
        if (!ok) fails++;
    }

    public static void Run()
    {
        TestHarness.Exit(Execute());
    }

    const BindingFlags Inst = BindingFlags.NonPublic | BindingFlags.Instance;
    const float Dt = 1f / 60f;
    static int frame;
    static float clock;

    public static int Execute()
    {
        fails = 0;
        using var sandbox = new TestHarness.Sandbox();
        float lead = BossWarningConfig.LeadSeconds, calm = BossWarningConfig.CalmFinalSeconds;
        bool reveal = BossWarningConfig.RevealNameBeforeFirstSight;
        try
        {
            Rules();
            EveryWorld();
            Paused();
            SpeedChanges();
            ShortLevel();
            LoopsAndRoutes();
            Tutorial();
            Death();
            Layout();
            Allocations();
            NameAndTunables();
        }
        finally
        {
            BossWarningConfig.LeadSeconds = lead;
            BossWarningConfig.CalmFinalSeconds = calm;
            BossWarningConfig.RevealNameBeforeFirstSight = reveal;
            ShipStartSpeed.EquippedHudOverride = null;
            SpeedRamp.FrameOverride = null;
            SpeedRamp.DeltaOverride = null;
            SpeedRamp.ResetFrameGuard();
            ResumeSlowMo.ClockOverride = null;
            ResumeSlowMo.ResetRun();
            BossEncounter.ResetRun();
            RunLoop.Reset();
            buttonClicks.playerDied = false;
            startMenu.youAreInTutorial = false;
            score.pauseCounter = 0;
        }
        Debug.Log("[BW] failures: " + fails);
        return fails;
    }

    // ---- 1. the rules ---------------------------------------------------------

    static void Rules()
    {
        var beats = new List<BossWarningBeat>();
        var c = new BossCountdown();
        c.OnBeat = b => beats.Add(b);

        // far out: nothing
        for (float far = 60f; far > 30.5f; far -= Dt) c.Step(BossWarningInput.Ahead, far, Dt);
        Check("nothing before the lead (" + BossWarningConfig.LeadSeconds + " s)", !c.Active && c.Triggers == 0);
        c.Step(BossWarningInput.Ahead, float.PositiveInfinity, Dt);
        c.Step(BossWarningInput.Ahead, float.NaN, Dt);
        Check("an infinite / NaN estimate (standing still) never starts it", !c.Active);

        // a steady flight in: the estimate falls a second a second
        float t = 30f;
        c.Step(BossWarningInput.Ahead, t, Dt);
        Check("fires when the estimate reaches the lead", c.Active && c.Triggers == 1 && beats.Count == 1 && beats[0] == BossWarningBeat.Announce);
        Check("starts at 30, banner up for " + BossWarningConfig.BannerSeconds + " s, not shortened",
              Mathf.Approximately(c.Total, 30f) && c.Seconds == 30 && c.BannerUp && !c.Shortened &&
              Mathf.Approximately(c.BannerFor, BossWarningConfig.BannerSeconds) && c.Stage == BossWarningStage.Countdown);

        bool monotone = true, tracked = true;
        float closeAt = -1f, finalAt = -1f;
        int lastSeconds = c.Seconds;
        bool secondsDown = true;
        while (t > Dt)
        {
            t -= Dt;
            float before = c.Shown;
            c.Step(BossWarningInput.Ahead, t, Dt);
            monotone &= c.Shown <= before;
            tracked &= Mathf.Abs(c.Shown - t) < .05f;
            secondsDown &= c.Seconds <= lastSeconds && c.Seconds >= lastSeconds - 1;
            lastSeconds = c.Seconds;
            if (closeAt < 0f && c.Stage == BossWarningStage.Close) closeAt = t;
            if (finalAt < 0f && c.Stage == BossWarningStage.Final) finalAt = t;
        }
        Check("a steady flight: the count is the estimate, only ever falling", monotone && tracked);
        Check("the seconds read-out steps down one at a time, to 1", secondsDown && c.Seconds == 1);
        Check("steps up at T-10 and T-3 (" + closeAt.ToString("F2") + ", " + finalAt.ToString("F2") + ")",
              Mathf.Abs(closeAt - 10f) < .05f && Mathf.Abs(finalAt - 3f) < .05f);
        Check("beats: one Announce, one Close, one Final, a Second for each of 10..1",
              Count(beats, BossWarningBeat.Announce) == 1 && Count(beats, BossWarningBeat.Close) == 1 &&
              Count(beats, BossWarningBeat.Final) == 1 && Count(beats, BossWarningBeat.Second) == 10);
        Check("the banner is long gone, the count nearly zero as the boss lands (" + c.Shown.ToString("F2") + ")",
              !c.BannerUp && c.Shown < .1f);

        // the encounter waits out a cinematic, then its intro starts
        float held = c.Shown;
        for (int i = 0; i < 120; i++) c.Step(BossWarningInput.Holding, 0f, Dt);
        Check("holds while the encounter is pending", c.Active && c.Shown == held);
        c.Step(BossWarningInput.Arriving, 0f, Dt);
        Check("the boss intro ends it with an Arrive beat", !c.Active && beats[beats.Count - 1] == BossWarningBeat.Arrive);
        int n = beats.Count;
        for (int i = 0; i < 60; i++) c.Step(BossWarningInput.Arriving, 0f, Dt);
        for (int i = 0; i < 60; i++) c.Step(BossWarningInput.Ahead, 12f, Dt);
        Check("no second warning on the same visit, whatever the estimate says", !c.Active && c.Triggers == 1 && beats.Count == n);
        c.Step(BossWarningInput.None, 0f, Dt);
        c.Step(BossWarningInput.Ahead, 80f, Dt);
        c.Step(BossWarningInput.Ahead, 29.9f, Dt);
        Check("the next level's boss warns again", c.Active && c.Triggers == 2 && Mathf.Approximately(c.Total, 29.9f) && !c.Shortened);

        // paused
        float shown = c.Shown, since = c.Since;
        for (int i = 0; i < 600; i++) c.Step(BossWarningInput.Ahead, 29.9f, 0f);
        Check("paused 10 s (dt 0): the count and its clock hold", c.Shown == shown && c.Since == since);

        // the estimate moves: 5 s nearer, later 6 s further
        c.Reset();
        c.Step(BossWarningInput.Ahead, 30f, Dt);
        Follow(c, 30f, 4f, 0f);
        float jumpDown = MaxDrop(c, 26f - 5f, 3f, out bool up1, out float gap1);
        Check("the estimate jumps 5 s nearer: no jump on screen (largest step " + (jumpDown / Dt).ToString("F2") + "x), never upwards",
              jumpDown <= BossWarningConfig.MaxRate * Dt + 1e-4f && !up1);
        Check("... and it is closing: " + gap1.ToString("F2") + " s out after 3 s", gap1 >= 0f && gap1 < 1.5f);
        MaxDrop(c, 21f - 3f, 5f, out bool up1b, out float gap1b);
        Check("... and has caught up after 8 s (gap " + gap1b.ToString("F2") + " s)", !up1b && Mathf.Abs(gap1b) < .15f);
        float eta = c.Shown + 3f;
        float slow = MaxDrop(c, eta, 8f, out bool up2, out float gap2, out float minDrop);
        Check("the estimate jumps 3 s further: the count slows (to " + (minDrop / Dt).ToString("F2") + "x) but never stops or rises",
              !up2 && minDrop >= BossWarningConfig.MinRate * Dt - 1e-4f && slow <= BossWarningConfig.MaxRate * Dt + 1e-4f);
        Check("... and has caught up within 8 s (gap " + gap2.ToString("F2") + " s)", Mathf.Abs(gap2) < .25f);

        // late correction: 1.5 s out with the boss 0.5 s away
        c.Reset();
        c.Step(BossWarningInput.Ahead, 30f, Dt);
        Follow(c, 30f, 28f, 0f);
        float e2 = .5f, worst = 0f;
        while (e2 > 0f) { e2 -= Dt; float b = c.Shown; c.Step(BossWarningInput.Ahead, Mathf.Max(0f, e2), Dt); worst = Mathf.Max(worst, b - c.Shown); }
        Check("a late error still closes at a bounded rate: " + c.Shown.ToString("F2") + " s shown as the boss lands, read-out " + c.Seconds,
              worst <= BossWarningConfig.MaxRate * Dt + 1e-4f && c.Shown < 1f && c.Seconds == 1);

        // shortened warnings
        c.Reset();
        c.Step(BossWarningInput.Ahead, 12f, Dt);
        Check("12 s left at first sight: a shortened warning from 12, banner " + c.BannerFor.ToString("F1") + " s",
              c.Active && c.Shortened && Mathf.Approximately(c.Total, 12f) && c.Seconds == 12 &&
              c.BannerFor > 0f && c.BannerFor <= 12f * BossWarningConfig.BannerMaxShare + 1e-4f && c.Stage == BossWarningStage.Countdown);
        c.Reset();
        c.Step(BossWarningInput.Ahead, 8f, Dt);
        Check("8 s left: straight in at the T-10 stage", c.Active && c.Stage == BossWarningStage.Close && c.BannerUp);
        c.Reset();
        c.Step(BossWarningInput.Ahead, 3f, Dt);
        Check("3 s left: no banner, the count only, at the T-3 stage", c.Active && !c.BannerUp && c.Stage == BossWarningStage.Final && c.Seconds == 3);
        c.Reset();
        int triggers = c.Triggers;
        c.Step(BossWarningInput.Ahead, .5f, Dt);
        c.Step(BossWarningInput.Ahead, .4f, Dt);
        Check("half a second left: no warning at all", !c.Active && c.Triggers == triggers);
        c.Step(BossWarningInput.Ahead, 20f, Dt);
        Check("... and none later on that visit either", !c.Active);
    }

    static int Count(List<BossWarningBeat> beats, BossWarningBeat b)
    {
        int n = 0;
        foreach (var x in beats) if (x == b) n++;
        return n;
    }

    // Fly `seconds` with the estimate falling in step from `eta` (+ offset).
    static void Follow(BossCountdown c, float eta, float seconds, float offset)
    {
        for (float t = 0f; t < seconds; t += Dt)
        {
            eta -= Dt;
            c.Step(BossWarningInput.Ahead, Mathf.Max(0f, eta + offset), Dt);
        }
    }

    static float MaxDrop(BossCountdown c, float eta, float seconds, out bool rose, out float gap)
    {
        return MaxDrop(c, eta, seconds, out rose, out gap, out float _);
    }

    static float MaxDrop(BossCountdown c, float eta, float seconds, out bool rose, out float gap, out float minDrop)
    {
        float worst = 0f;
        minDrop = float.MaxValue;
        rose = false;
        for (float t = 0f; t < seconds; t += Dt)
        {
            eta -= Dt;
            float before = c.Shown;
            c.Step(BossWarningInput.Ahead, eta, Dt);
            float drop = before - c.Shown;
            rose |= drop < 0f;
            worst = Mathf.Max(worst, drop);
            minDrop = Mathf.Min(minDrop, drop);
        }
        gap = c.Shown - eta;
        return worst;
    }

    // ---- the flight rig (as WorldPaceTest) ---------------------------------------

    static void FreshScene(int world)
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        BossEncounter.ResetRun();
        RunLoop.Reset();
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
        PlayerPrefs.DeleteKey(Codex.PrefsKey);
        Codex.Reload();
        ShipStartSpeed.EquippedHudOverride = () => 0;
        frame = 1000;
        clock = 10f;
        SpeedRamp.FrameOverride = () => frame;
        SpeedRamp.DeltaOverride = () => Dt;
        SpeedRamp.ResetFrameGuard();
        SpeedRamp.ResetBoost();
        PortalPressure.Reset();
        ResumeSlowMo.ClockOverride = () => clock;
        ResumeSlowMo.ResetRun();
    }

    sealed class Rig
    {
        public WorldManager wm;
        public moveBackGround[] walls;
        public BossWarningHud hud;
        public float flown;

        // One frame of the game: walls (ramp), level clock, warning.
        public void Frame()
        {
            frame++;
            clock += Dt;
            foreach (var w in walls) TestHarness.Send(w, "Update");
            bool flying = WorldManager.Flying;
            if (flying) { wm.Tick(Dt); flown += Dt; }
            var input = BossWarning.Read(wm);
            hud.Step(input, input == BossWarningInput.Ahead ? wm.SecondsLeftInWorld : float.PositiveInfinity,
                     input == BossWarningInput.Ahead && flying ? Dt : 0f, Dt);
        }
    }

    static Rig Fly(int world, float startSpeed = 0f)
    {
        FreshScene(world);
        var rig = new Rig();
        rig.wm = new GameObject("~WorldManager").AddComponent<WorldManager>();
        rig.wm.SendMessage("Awake");
        rig.walls = new moveBackGround[2];
        for (int i = 0; i < 2; i++) rig.walls[i] = new GameObject("~wall" + i).AddComponent<moveBackGround>();
        typeof(WorldManager).GetField("distanceLeft", Inst).SetValue(rig.wm, rig.wm.WorldDistance);
        typeof(WorldManager).GetField("levelBegun", Inst).SetValue(rig.wm, true);
        typeof(WorldManager).GetMethod("ApplyDifficulty", BindingFlags.NonPublic | BindingFlags.Static)
            .Invoke(null, new object[] { WorldManager.Current });
        moveBackGround.speed = startSpeed;
        rig.hud = BossWarningHud.Build();
        rig.hud.ApplyLayout(BossWarningHud.ComputeLayout(new Rect(0, 0, 1080, 2520), new Vector2(1080, 2520),
                                                        new Rect(33, 2336, 474, 177)));
        return rig;
    }

    static void SetDistance(Rig rig, float seconds)
    {
        // the distance `seconds` of flight covers from here
        var theme = WorldManager.Current;
        float d = SpeedRamp.DistanceOver(moveBackGround.speed, theme.speedRampPerSecond, SpeedRamp.Cap, seconds);
        typeof(WorldManager).GetField("distanceLeft", Inst).SetValue(rig.wm, d);
    }

    static void Drop(Rig rig)
    {
        if (rig.hud != null) UnityEngine.Object.DestroyImmediate(rig.hud.gameObject);
        BossEncounter.ResetRun();
        SpeedRamp.ResetBoost();
        PortalPressure.Reset();
    }

    // ---- 2. every world ---------------------------------------------------------

    static void EveryWorld()
    {
        for (int w = 0; w < WorldManager.LiveWorldCount; w++)
        {
            string name = WorldManager.Worlds[w].displayName;
            var rig = Fly(w);
            var c = rig.hud.Countdown;
            float firedAt = -1f, bannerGone = -1f, chipAt = -1f, closeAt = -1f, finalAt = -1f;
            bool never = true, monotone = true, bannerFirst = false, finalOnlyAtEnd = true, glowQuietEarly = true;
            float glowClose = 0f, glowFinal = 0f;
            int lastSeconds = 99;
            bool secondsDown = true;
            string titleAtFire = "", subAtFire = "";
            for (int i = 0; i < 60 * 400 && !BossEncounter.Running; i++)
            {
                float before = c.Shown;
                bool wasActive = c.Active;
                rig.Frame();
                if (BossEncounter.Running) break;
                if (!wasActive && c.Active)
                {
                    firedAt = rig.flown;
                    bannerFirst = rig.hud.BannerVisible && !rig.hud.ChipVisible;
                    titleAtFire = rig.hud.TitleShown;
                    subAtFire = rig.hud.SubShown;
                }
                else if (c.Active)
                {
                    monotone &= c.Shown <= before;
                    secondsDown &= c.Seconds <= lastSeconds;
                    lastSeconds = c.Seconds;
                    if (bannerGone < 0f && !rig.hud.BannerVisible) bannerGone = rig.flown - firedAt;
                    if (chipAt < 0f && rig.hud.ChipVisible) chipAt = rig.flown - firedAt;
                    if (closeAt < 0f && c.Stage == BossWarningStage.Close) closeAt = rig.flown;
                    if (finalAt < 0f && c.Stage == BossWarningStage.Final) finalAt = rig.flown;
                    if (c.Stage == BossWarningStage.Countdown && !c.BannerUp) glowQuietEarly &= rig.hud.VignetteAlpha == 0f;
                    if (c.Stage == BossWarningStage.Close) glowClose = Mathf.Max(glowClose, rig.hud.VignetteAlpha);
                    if (c.Stage == BossWarningStage.Final) glowFinal = Mathf.Max(glowFinal, rig.hud.VignetteAlpha);
                    finalOnlyAtEnd &= rig.hud.FinalVisible == (c.Stage == BossWarningStage.Final);
                }
                else never &= !rig.hud.Visible && !rig.hud.ChipVisible && !rig.hud.BannerVisible;
            }
            float bossAt = rig.flown;
            Check(name + ": boss after ~120 s of flight (" + bossAt.ToString("F1") + ")", BossEncounter.Running && Mathf.Abs(bossAt - 120f) < 1f);
            Check(name + ": nothing on screen for the first " + firedAt.ToString("F1") + " s", never && firedAt > 60f);
            Check(name + ": the warning starts 30 s of flight before the boss (" + (bossAt - firedAt).ToString("F2") + " s), once",
                  Mathf.Abs(bossAt - firedAt - 30f) < .25f && c.Triggers == 1);
            Check(name + ": banner first (BOSS INCOMING / " + subAtFire + "), in the world's accent",
                  bannerFirst && titleAtFire == BossWarningConfig.Title && subAtFire == BossWarningConfig.UnknownLine &&
                  rig.hud.Accent == BossWarningConfig.Accent(w));
            Check(name + ": the banner collapses into the chip after " + bannerGone.ToString("F2") + " s (chip up at " + chipAt.ToString("F2") + ")",
                  Mathf.Abs(bannerGone - BossWarningConfig.BannerSeconds) < .05f && chipAt > 0f && chipAt <= bannerGone);
            Check(name + ": the count only falls, a second at a time", monotone && secondsDown);
            Check(name + ": T-10 at " + (bossAt - closeAt).ToString("F2") + " s, T-3 at " + (bossAt - finalAt).ToString("F2") + " s before the boss",
                  Mathf.Abs(bossAt - closeAt - 10f) < .25f && Mathf.Abs(bossAt - finalAt - 3f) < .25f);
            Check(name + ": no edge glow until T-10, then stronger at T-3 (" + glowClose.ToString("F2") + " -> " + glowFinal.ToString("F2") + ")",
                  glowQuietEarly && glowClose > .05f && glowFinal > glowClose);
            Check(name + ": the 3-2-1 shows for the last three seconds only", finalOnlyAtEnd);
            Check(name + ": the count reads 1 with " + c.Shown.ToString("F2") + " s left as the level ends",
                  c.Active && c.Seconds == 1 && c.Shown < .2f);

            // the encounter: pending (holds), then its intro (hand-off)
            Check(name + ": held while the encounter is pending", BossWarning.Read(rig.wm) == BossWarningInput.Holding && rig.hud.ChipVisible);
            BossEncounter.Instance.Step(Dt, 1f);
            Check(name + ": the boss intro is running", BossEncounter.Instance.State == BossEncounter.Phase.Intro &&
                  BossWarning.Read(rig.wm) == BossWarningInput.Arriving);
            rig.hud.Step(BossWarningInput.Arriving, 0f, 0f, Dt);
            Check(name + ": hand-off: the count ends, the chip reads 0 and bursts while the edges flare",
                  !c.Active && rig.hud.HandingOff && rig.hud.ChipVisible && rig.hud.DigitsShown == "0" &&
                  rig.hud.VignetteAlpha > .3f && !rig.hud.FinalVisible && !rig.hud.BannerVisible);
            for (float t = 0f; t < BossWarningConfig.HandoffSeconds + .05f; t += Dt) rig.hud.Step(BossWarningInput.Arriving, 0f, 0f, Dt);
            Check(name + ": ... and is gone " + BossWarningConfig.HandoffSeconds + " s into the intro",
                  !rig.hud.Visible && !rig.hud.ChipVisible && rig.hud.VignetteAlpha == 0f && !rig.hud.HandingOff);
            BossEncounter.Instance.BeginFight();
            for (int i = 0; i < 30; i++) rig.hud.Step(BossWarning.Read(rig.wm), 0f, 0f, Dt);
            Check(name + ": nothing during the fight", BossWarning.Read(rig.wm) == BossWarningInput.None && !rig.hud.Visible);
            Drop(rig);
        }
    }

    // Flies until the warning is up and `shown` or less is on the clock.
    static bool FlyTo(Rig rig, float shown)
    {
        for (int i = 0; i < 60 * 400 && !BossEncounter.Running; i++)
        {
            rig.Frame();
            if (rig.hud.Countdown.Active && rig.hud.Countdown.Shown <= shown) return true;
        }
        return false;
    }

    // ---- 3. paused ---------------------------------------------------------

    static void Paused()
    {
        foreach (float at in new[] { 29f, 20f, 8f, 2.5f })
        {
            var rig = Fly(0);
            var c = rig.hud.Countdown;
            bool reached = FlyTo(rig, at);
            float shown = c.Shown, since = c.Since, left = rig.wm.DistanceLeft, glow = rig.hud.VignetteAlpha;
            string digits = rig.hud.DigitsShown;
            Vector3 chipScale = rig.hud.ChipRect.localScale, bannerScale = rig.hud.BannerRect.localScale;
            Vector2 bannerAt = rig.hud.BannerRect.anchoredPosition;
            bool banner = rig.hud.BannerVisible, chip = rig.hud.ChipVisible;
            float bar = rig.hud.BarFill01;

            score.pauseCounter = 3;   // no touch in batch mode: frozen
            bool froze = !WorldManager.Flying;
            for (int i = 0; i < 600; i++) rig.Frame();
            Check("paused 10 s at T-" + at + ": no distance flown, the count holds at " + shown.ToString("F2"),
                  reached && froze && rig.wm.DistanceLeft == left && c.Shown == shown && c.Since == since && c.Triggers == 1);
            Check("paused at T-" + at + ": the warning holds its pose (no blink, pulse or slide)",
                  rig.hud.DigitsShown == digits && rig.hud.ChipRect.localScale == chipScale &&
                  rig.hud.BannerRect.localScale == bannerScale && rig.hud.BannerRect.anchoredPosition == bannerAt &&
                  rig.hud.BannerVisible == banner && rig.hud.ChipVisible == chip && rig.hud.VignetteAlpha == glow &&
                  rig.hud.BarFill01 == bar && rig.hud.Visible);
            score.pauseCounter = 0;
            rig.Frame();
            Check("resumed at T-" + at + ": it carries on from where it was", c.Shown < shown && c.Shown > shown - .1f);
            Drop(rig);
        }

        // resume slow-mo: the world (and the count) at 0.6x
        var slow = Fly(0);
        FlyTo(slow, 20f);
        var sc = slow.hud.Countdown;
        float s0 = sc.Shown, d0 = slow.wm.SecondsLeftInWorld;
        for (int i = 0; i < 60; i++)
        {
            float dt = Dt * ResumeSlowMo.SlowScale;
            slow.wm.Tick(dt);
            slow.hud.Step(BossWarningInput.Ahead, slow.wm.SecondsLeftInWorld, dt, Dt);
        }
        Check("a real second of resume slow-mo takes " + (s0 - sc.Shown).ToString("F2") + " s off the count, like the level's own clock (" +
              (d0 - slow.wm.SecondsLeftInWorld).ToString("F2") + ")",
              Mathf.Abs((s0 - sc.Shown) - ResumeSlowMo.SlowScale) < .06f && Mathf.Abs((d0 - slow.wm.SecondsLeftInWorld) - ResumeSlowMo.SlowScale) < .06f);
        Drop(slow);
    }

    // ---- 4. speed changes ---------------------------------------------------------

    static void SpeedChanges()
    {
        for (int w = 0; w < WorldManager.LiveWorldCount; w += 3)
        {
            string name = WorldManager.Worlds[w].displayName;
            var rig = Fly(w);
            var c = rig.hud.Countdown;
            FlyTo(rig, 22f);
            float etaBefore = rig.wm.SecondsLeftInWorld;
            SpeedRamp.AddBoost();                         // a blue atom (the boost comes on over ~0.2 s)
            float etaBoost = rig.wm.SecondsLeftInWorld;
            float peakBoost = 0f, etaBoostEnd = 0f;
            bool rose = false;
            float worst = 0f, least = float.MaxValue;
            float boostLeft = 5f;
            bool boosted = true;
            int lastSeconds = c.Seconds;
            bool secondsDown = true;
            float etaAfter = 0f;
            for (int i = 0; i < 60 * 60 && !BossEncounter.Running; i++)
            {
                float before = c.Shown;
                rig.Frame();
                if (BossEncounter.Running) break;
                float drop = before - c.Shown;
                rose |= drop < 0f;
                worst = Mathf.Max(worst, drop);
                if (c.Shown > 0f) least = Mathf.Min(least, drop);   // (it stops at zero)
                secondsDown &= c.Seconds <= lastSeconds;
                lastSeconds = c.Seconds;
                boostLeft -= Dt;
                peakBoost = Mathf.Max(peakBoost, SpeedRamp.Boost);
                if (boosted && boostLeft <= 0f)
                {
                    boosted = false;
                    float e = rig.wm.SecondsLeftInWorld;
                    etaBoostEnd = e;
                    SpeedRamp.EndBoost();                 // the shield ends: the boost eases off
                    etaAfter = rig.wm.SecondsLeftInWorld - e;
                }
            }
            // The estimate is taken on natural speed: the boost does not move it
            // in a jump, either way; it eats the distance faster while it lasts.
            float boostDrop = etaBefore - etaBoostEnd;
            Check(name + ": a blue atom's boost (peak +" + Mathf.RoundToInt(peakBoost * 100f) + ") never jumps the estimate (" +
                  (etaBoost - etaBefore).ToString("+0.00;-0.00") + " s on, " + etaAfter.ToString("+0.00;-0.00") + " s off) and " +
                  "brings the boss nearer while it lasts (" + boostDrop.ToString("F2") + " s in 5 s of flight)",
                  Mathf.Abs(etaBoost - etaBefore) < .01f && Mathf.Abs(etaAfter) < .01f &&
                  Mathf.Approximately(peakBoost, SpeedRamp.BoostPerAtom) && boostDrop > 5.3f && boostDrop < 7f);
            Check(name + ": the count never rises or jumps through it (" + (least / Dt).ToString("F2") + "x .. " + (worst / Dt).ToString("F2") + "x)",
                  !rose && secondsDown && worst <= BossWarningConfig.MaxRate * Dt + 1e-4f && least >= BossWarningConfig.MinRate * Dt - 1e-4f);
            Check(name + ": ... and still reads 1 (" + c.Shown.ToString("F2") + " s) as the boss arrives, one warning only",
                  BossEncounter.Running && c.Seconds == 1 && c.Shown < .3f && c.Triggers == 1);
            Drop(rig);
        }

        // a fast start (HUD 30): the same lead
        var fast = Fly(2, .30f);
        var fc = fast.hud.Countdown;
        float firedAt = -1f;
        for (int i = 0; i < 60 * 400 && !BossEncounter.Running; i++)
        {
            fast.Frame();
            if (firedAt < 0f && fc.Active) firedAt = fast.flown;
        }
        Check("a start at HUD 30 (boss after " + fast.flown.ToString("F1") + " s): still warned 30 s out (" + (fast.flown - firedAt).ToString("F2") + ")",
              fast.flown < 110f && Mathf.Abs(fast.flown - firedAt - 30f) < .25f && fc.Triggers == 1);
        Drop(fast);
    }

    // ---- 5. a short level ---------------------------------------------------------

    static void ShortLevel()
    {
        var rig = Fly(1, .3f);
        SetDistance(rig, 14f);
        rig.Frame();
        var c = rig.hud.Countdown;
        Check("14 s from the boss at the first frame: a shortened warning from " + c.Total.ToString("F1") + " s",
              c.Active && c.Shortened && Mathf.Abs(c.Total - 14f) < .2f && rig.hud.BannerVisible && c.BannerFor <= BossWarningConfig.BannerSeconds);
        Check("... its bar starts part-drained (" + rig.hud.BarFill01.ToString("F2") + " of the full 30 s)", c.Total / BossWarningConfig.LeadSeconds < .5f);
        float flown0 = rig.flown;
        while (!BossEncounter.Running && rig.flown < 60f) rig.Frame();
        Check("... and the boss comes when it says (" + (rig.flown - flown0).ToString("F1") + " s later, " + c.Shown.ToString("F2") + " s shown)",
              BossEncounter.Running && Mathf.Abs(rig.flown - flown0 - 14f) < .3f && c.Shown < .2f);
        Drop(rig);

        rig = Fly(0, .3f);
        SetDistance(rig, 2.5f);
        rig.Frame();
        Check("2.5 s from the boss: the chip and the 3-2-1 only, no banner",
              rig.hud.Countdown.Active && !rig.hud.BannerVisible && rig.hud.ChipVisible && rig.hud.FinalVisible && rig.hud.DigitsShown == "3");
        Drop(rig);

        rig = Fly(0, .3f);
        SetDistance(rig, .4f);
        for (int i = 0; i < 10; i++) rig.Frame();
        Check("0.4 s from the boss: nothing", !rig.hud.Countdown.Active && !rig.hud.Visible);
        Drop(rig);
    }

    // ---- 6. loops and portals ---------------------------------------------------------

    // The portal opens and waits (WorldManager.OpenPortal without the
    // portal's GameObject: the stage and the pressure clock are what Read sees).
    static void OpenPortal(WorldManager wm)
    {
        typeof(WorldManager).GetField("portalOpen", Inst).SetValue(wm, true);
        PortalPressure.Open(WorldManager.PortalDestination);
    }

    static void LoopsAndRoutes()
    {
        int last = WorldManager.LastLiveWorld;
        var rig = Fly(last, .2f);
        RunLoop.StartWorld = 0;
        var c = rig.hud.Countdown;
        while (!BossEncounter.Running && rig.flown < 300f) rig.Frame();
        Check("first pass, " + WorldManager.Worlds[last].displayName + ": warned once", BossEncounter.Running && c.Triggers == 1);

        // the fight is over: the portal back round opens and waits
        BossEncounter.ResetRun();
        typeof(BossEncounter).GetField("doneWorld", BindingFlags.NonPublic | BindingFlags.Static).SetValue(null, last);
        rig.hud.Step(BossWarning.Read(rig.wm), 0f, 0f, 1f);
        Check("after the boss: no boss ahead", BossWarning.Read(rig.wm) == BossWarningInput.None && !rig.hud.Visible);

        typeof(WorldManager).GetField("distanceLeft", Inst).SetValue(rig.wm, 0f);
        OpenPortal(rig.wm);
        Check("the final world's portal leads back to the start world", WorldManager.PortalDestination == 0 &&
              PortalPressure.Destination == 0 && rig.wm.Stage == WorldManager.LevelStage.Portal);
        Check("the loop portal waiting: no warning", BossWarning.Read(rig.wm) == BossWarningInput.None &&
              float.IsPositiveInfinity(BossWarning.SecondsToBoss));
        // the stage alone keeps it quiet: even with distance on the clock and
        // the boss forgotten, a waiting portal never has a boss ahead
        typeof(WorldManager).GetField("distanceLeft", Inst).SetValue(rig.wm, 5f);
        BossEncounter.ForgetDone();
        Check("a waiting portal (stage Portal) has no boss ahead whatever the clock says",
              BossWarning.Read(rig.wm) == BossWarningInput.None);
        typeof(BossEncounter).GetField("doneWorld", BindingFlags.NonPublic | BindingFlags.Static).SetValue(null, last);
        typeof(WorldManager).GetField("distanceLeft", Inst).SetValue(rig.wm, 0f);

        // a minute and a half of waiting, pressure and all: never a warning
        bool quiet = true;
        for (int i = 0; i < 60 * 90; i++)
        {
            rig.Frame();
            quiet &= !c.Active && !rig.hud.Visible && BossWarning.Read(rig.wm) == BossWarningInput.None;
        }
        Check("90 s at the waiting loop portal (pressure level " + PortalPressure.Level.ToString("F1") + "): never a warning",
              quiet && c.Triggers == 1 && rig.wm.Stage == WorldManager.LevelStage.Portal && PortalPressure.Pressing &&
              !BossEncounter.Running);

        // through the loop portal: the start world again, one loop on
        try { rig.wm.Advance(); } catch (Exception e) { Debug.Log("[BW] Advance side effect threw (ignored): " + e.Message); }
        Check("looped: back in " + WorldManager.Current.displayName + ", loop " + RunLoop.Index + ", its boss ahead again",
              WorldManager.CurrentIndex == 0 && RunLoop.Index == 1 && rig.wm.Stage == WorldManager.LevelStage.Level &&
              !PortalPressure.Active && BossWarning.Read(rig.wm) == BossWarningInput.Ahead &&
              BossWarning.SecondsToBoss > 40f);
        float start = rig.flown, firedAt = -1f;
        bool early = false;
        while (!BossEncounter.Running && rig.flown - start < 300f)
        {
            rig.Frame();
            if (firedAt < 0f && c.Triggers == 2) firedAt = rig.flown;
            if (c.Active && rig.wm.SecondsLeftInWorld > 31f) early = true;
        }
        Check("loop 1: warned again, once, 30 s before its boss (" + (rig.flown - firedAt).ToString("F2") + " s; level " + (rig.flown - start).ToString("F1") + " s)",
              BossEncounter.Running && c.Triggers == 2 && !early && Mathf.Abs(rig.flown - firedAt - 30f) < .3f &&
              rig.hud.Accent == BossWarningConfig.Accent(0));

        // a boss already done on this visit with the level clock running again
        // (nothing in the game does that now; the guard stays): no warning
        BossEncounter.ResetRun();
        typeof(BossEncounter).GetField("doneWorld", BindingFlags.NonPublic | BindingFlags.Static).SetValue(null, 0);
        typeof(WorldManager).GetField("distanceLeft", Inst).SetValue(rig.wm, rig.wm.WorldDistance);
        bool none = rig.wm.DistanceLeft > 0f && rig.wm.Stage == WorldManager.LevelStage.Level;
        for (int i = 0; i < 300; i++) { rig.Frame(); none &= !c.Active; }
        Check("a boss already done on this visit (level clock running): no warning", none && c.Triggers == 2);
        Drop(rig);

        // a mid-run portal: quiet while it waits, the next world's boss warned once
        rig = Fly(1, .3f);
        typeof(BossEncounter).GetField("doneWorld", BindingFlags.NonPublic | BindingFlags.Static).SetValue(null, 1);
        typeof(WorldManager).GetField("distanceLeft", Inst).SetValue(rig.wm, 0f);
        OpenPortal(rig.wm);
        quiet = WorldManager.PortalDestination == 2;
        for (int i = 0; i < 60 * 20; i++) { rig.Frame(); quiet &= !rig.hud.Countdown.Active && BossWarning.Read(rig.wm) == BossWarningInput.None; }
        Check("a mid-run portal waiting 20 s: no warning", quiet && rig.hud.Countdown.Triggers == 0);
        try { rig.wm.Advance(); } catch (Exception e) { Debug.Log("[BW] Advance side effect threw (ignored): " + e.Message); }
        Check("through it: " + WorldManager.Current.displayName + ", the same loop, its boss ahead",
              WorldManager.CurrentIndex == 2 && RunLoop.Index == 0 && BossWarning.Read(rig.wm) == BossWarningInput.Ahead);
        float t0 = rig.flown;
        while (!BossEncounter.Running && rig.flown - t0 < 300f) rig.Frame();
        Check("the next world: warned once before its boss", BossEncounter.Running && rig.hud.Countdown.Triggers == 1);
        Drop(rig);
    }

    // ---- 7. the tutorial ---------------------------------------------------------

    static void Tutorial()
    {
        Check("the warning is only built in gameS1", BossWarningHud.ShouldAttach("gameS1") &&
              !BossWarningHud.ShouldAttach(score.TutorialScene) && !BossWarningHud.ShouldAttach("startMenuS2"));
        var rig = Fly(0, .3f);
        SetDistance(rig, 20f);
        startMenu.youAreInTutorial = true;
        for (int i = 0; i < 60; i++) rig.Frame();
        Check("flagged as the tutorial: no boss ahead, no warning",
              BossWarning.Read(rig.wm) == BossWarningInput.None && !rig.hud.Countdown.Active && !rig.hud.Visible);
        startMenu.youAreInTutorial = false;
        Drop(rig);

        EditorSceneLoader.Open(score.TutorialScene);
        Check("the tutorial scene has no WorldManager, boss encounter or warning in it",
              UnityEngine.Object.FindFirstObjectByType<WorldManager>() == null &&
              UnityEngine.Object.FindFirstObjectByType<BossEncounter>() == null &&
              UnityEngine.Object.FindFirstObjectByType<BossWarningHud>() == null);
        Check("no WorldManager: no boss ahead", BossWarning.Read(null) == BossWarningInput.None);
    }

    // ---- 8. death ---------------------------------------------------------

    static void Death()
    {
        foreach (float at in new[] { 29.5f, 15f, 2f })
        {
            var rig = Fly(0);
            FlyTo(rig, at);
            bool up = rig.hud.Visible;
            var beats = new List<BossWarningBeat>();
            Action<BossWarningBeat> listen = b => beats.Add(b);
            BossWarning.Beat += listen;
            buttonClicks.playerDied = true;
            rig.Frame();
            BossWarning.Beat -= listen;
            Check("died at T-" + at + ": everything hidden on that frame",
                  up && !rig.hud.Visible && !rig.hud.ChipVisible && !rig.hud.BannerVisible && !rig.hud.FinalVisible &&
                  rig.hud.VignetteAlpha == 0f && !rig.hud.Countdown.Active && !BossWarning.Active &&
                  beats.Count == 1 && beats[0] == BossWarningBeat.Cancel);
            for (int i = 0; i < 120; i++) rig.Frame();
            Check("... and it stays hidden under the death panel", !rig.hud.Visible && rig.hud.Countdown.Triggers == 1);
            buttonClicks.playerDied = false;
            Drop(rig);
        }
    }

    // ---- 9. layout ---------------------------------------------------------

    static readonly (string name, Vector2 size, Rect safe)[] Screens =
    {
        ("9:21 1080x2520",          new Vector2(1080, 2520), new Rect(0, 0, 1080, 2520)),
        ("9:21 1080x2520 cutout",   new Vector2(1080, 2520), new Rect(0, 0, 1080, 2520 - 110)),
        ("9:16 1080x1920",          new Vector2(1080, 1920), new Rect(0, 0, 1080, 1920)),
        ("9:19.5 1170x2532 notch",  new Vector2(1170, 2532), new Rect(0, 102, 1170, 2532 - 102 - 141)),
        ("9:20 1080x2400 cutout",   new Vector2(1080, 2400), new Rect(0, 0, 1080, 2400 - 118)),
        ("9:24 1080x2880",          new Vector2(1080, 2880), new Rect(0, 0, 1080, 2880 - 120)),
        ("Fold cover 904x2316",     new Vector2(904, 2316),  new Rect(0, 0, 904, 2316 - 100)),
        ("Fold open 1812x2176",     new Vector2(1812, 2176), new Rect(0, 0, 1812, 2176 - 90)),
        ("narrow 720x1280",         new Vector2(720, 1280),  new Rect(0, 0, 720, 1280)),
    };

    static bool Inside(Rect outer, Rect inner)
    {
        return inner.xMin >= outer.xMin - .01f && inner.xMax <= outer.xMax + .01f &&
               inner.yMin >= outer.yMin - .01f && inner.yMax <= outer.yMax + .01f;
    }

    static string R(Rect r)
    {
        return "x " + r.xMin.ToString("F0") + ".." + r.xMax.ToString("F0") + ", y " + r.yMin.ToString("F0") + ".." + r.yMax.ToString("F0");
    }

    static void Layout()
    {
        EditorSceneLoader.Open("gameS1");
        var go = new GameObject("~HudStyler");
        var styler = go.AddComponent<HudStyler>();
        styler.SendMessage("Start");
        Canvas.ForceUpdateCanvases();
        LayoutRebuilder.ForceRebuildLayoutImmediate(styler.HudRoot);
        var canvas = styler.HudRoot.GetComponentInParent<Canvas>().rootCanvas;
        var scaler = canvas.GetComponent<CanvasScaler>();
        Vector2 hudSize = styler.HudRoot.rect.size;
        Check("the read-out the chip sits beside is the score panel (" + hudSize + ")", hudSize.x > 300f && hudSize.y > 100f);

        var hud = BossWarningHud.Build();
        hud.BindHud(styler.HudRoot);
        foreach (var s in Screens)
        {
            float scale = HudStyler.HudCanvasScale(canvas, scaler, s.size);
            Rect read = HudStyler.HudScreenRect(s.safe, s.size, scale, hudSize);
            Rect actions = PauseQuickActions.ScreenRectFor(s.safe, s.size);
            var l = BossWarningHud.ComputeLayout(s.safe, s.size, read);
            Debug.Log("[BW] " + s.name + ": read-out " + R(read) + "; actions " + R(actions) + "; chip " + R(l.chip) +
                      (l.chipInBand ? " (in the band)" : " (under the icons)") + "; banner " + R(l.banner));
            Check(s.name + ": the chip (at its largest) is clear of the score read-out and the quick actions",
                  !l.chip.Overlaps(read) && !l.chip.Overlaps(actions));
            // under the icons it is the band's second row: the top fifth on a phone, the top quarter on an unfolded screen
            Check(s.name + ": the chip is inside the safe area, in the top band (its foot " + (l.chip.yMin / s.size.y).ToString("P0") + " up)",
                  Inside(s.safe, l.chip) && l.chip.yMin > s.size.y * (s.size.x <= 1200f ? .8f : .75f));
            Check(s.name + ": the chip is big enough to read (" + (l.chip.width / BossWarningHud.ChipMaxPunch).ToString("F0") + " px wide)",
                  l.chip.width / BossWarningHud.ChipMaxPunch >= s.size.x * .1f);
            Check(s.name + ": the banner is inside the safe area, under the band, clear of all three",
                  Inside(s.safe, l.banner) && !l.banner.Overlaps(read) && !l.banner.Overlaps(actions) && !l.banner.Overlaps(l.chip));
            // the band is two rows at its right end (icons, then the chip), so the banner hangs a chip lower
            Check(s.name + ": the banner stays in the top of the screen, off the ship's lane (its foot " + (l.banner.yMin / s.size.y).ToString("P0") + " up)",
                  l.banner.yMin >= s.size.y * (s.size.x <= 1200f ? .68f : .6f));
            var band = TopBand.FrameFor(s.safe, s.size);
            Check(s.name + ": the chip is inside the top band's ends (inside the rails), like the read-out and the icons",
                  l.chip.xMin >= band.left - .01f && l.chip.xMax <= band.right + .01f &&
                  read.xMin >= band.left - .01f && actions.xMax <= band.right + .01f);

            // what is built matches what was computed
            hud.ApplyLayout(l);
            var chip = hud.ChipRect;
            var banner = hud.BannerRect;
            bool chipOk = chip.anchoredPosition == l.chipTop && Mathf.Approximately(chip.localScale.x, l.chipScale) &&
                          chip.pivot == new Vector2(.5f, 1f) && chip.sizeDelta == new Vector2(BossWarningHud.ChipW, BossWarningHud.ChipH);
            bool bannerOk = banner.anchoredPosition == l.bannerTop && Mathf.Approximately(banner.localScale.x, l.bannerScale) &&
                            banner.sizeDelta == new Vector2(BossWarningHud.BannerW, BossWarningHud.BannerH);
            Check(s.name + ": the built chip and banner sit where the layout says", chipOk && bannerOk);
        }
        // The lane between the rails holds the read-out and the icons with
        // nothing to spare, so on both target screens the chip is under the
        // icons, right-aligned with them.
        foreach (int i in new[] { 0, 2 })
        {
            Rect read = HudStyler.HudScreenRect(Screens[i].safe, Screens[i].size, HudStyler.HudCanvasScale(canvas, scaler, Screens[i].size), hudSize);
            Rect actions = PauseQuickActions.ScreenRectFor(Screens[i].safe, Screens[i].size);
            var l = BossWarningHud.ComputeLayout(Screens[i].safe, Screens[i].size, read);
            Check(Screens[i].name + ": the chip sits under the icons, flush with their right end, at full size",
                  !l.chipInBand && Mathf.Abs(l.chip.xMax - actions.xMax) < .5f && l.chip.yMax < actions.yMin &&
                  Mathf.Approximately(l.chipScale, PauseQuickActions.CanvasScaleFor(Screens[i].size)));
        }
        // with room beside the read-out (no rails, a small read-out) it is in the band
        {
            var size = Screens[0].size;
            var open = TopBand.FrameFor(Screens[0].safe, size, 0f, null);
            var l = BossWarningHud.ComputeLayout(Screens[0].safe, size, new Rect(open.left, open.top - 177f, 474f, 177f), open);
            Rect actions = PauseQuickActions.ScreenRectFor(open, size);
            Check("with room in the band the chip sits between the read-out and the icons",
                  l.chipInBand && l.chip.xMin > open.left + 474f && l.chip.xMax < actions.xMin && Mathf.Abs(l.chip.yMax - open.top) < .5f);
        }

        // a read-out so wide there is no room beside it: under the icons
        Rect act = PauseQuickActions.ScreenRectFor(Screens[0].safe, Screens[0].size);
        float bandLeft = TopBand.FrameFor(Screens[0].safe, Screens[0].size).left;
        var wideRead = new Rect(bandLeft, 2336, act.xMin - bandLeft - 4f, 177);
        var wide = BossWarningHud.ComputeLayout(Screens[0].safe, Screens[0].size, wideRead);
        Check("no room in the band: the chip drops under the icons, still clear and on screen",
              !wide.chipInBand && !wide.chip.Overlaps(act) && !wide.chip.Overlaps(wideRead) &&
              Inside(Screens[0].safe, wide.chip) && !wide.banner.Overlaps(wide.chip));

        // the pieces: nothing takes a touch, the canvas sorts under the icons
        var root = hud.GetComponent<Canvas>();
        bool noTouch = hud.GetComponent<GraphicRaycaster>() == null;
        foreach (var g in hud.GetComponentsInChildren<Graphic>(true)) noTouch &= !g.raycastTarget;
        Check("the warning can never take a touch", noTouch);
        Check("its canvas sorts over the HUD and under the quick actions (90)", root.sortingOrder == BossWarningHud.SortingOrder &&
              root.sortingOrder < 90 && root.sortingOrder > canvas.sortingOrder);
        bool red = false;
        foreach (var a in BossWarningConfig.Accents)
        {
            Color.RGBToHSV(a, out float h, out float sat, out float _);
            // the player's red sits at hue ~0 (AkiraPalette.Red 357 deg, RedHi 7 deg)
            float deg = h * 360f;
            red |= sat > .5f && (deg < 20f || deg > 345f);
        }
        Check("one accent per world, none of them the player's red", BossWarningConfig.Accents.Length == WorldManager.Worlds.Length && !red);
        UnityEngine.Object.DestroyImmediate(hud.gameObject);
        UnityEngine.Object.DestroyImmediate(go);
    }

    // ---- 10. allocations ---------------------------------------------------------

    static void Allocations()
    {
        var rig = Fly(0, .3f);
        SetDistance(rig, 31f);
        // warm up: trigger, banner, into the chip
        for (int i = 0; i < 240; i++) rig.Frame();
        var hud = rig.hud;
        var wm = rig.wm;
        Check("alloc rig: the countdown is running", hud.Countdown.Active && hud.ChipVisible);

        // (GC.GetAllocatedBytesForCurrentThread reads 0 under this Mono: the
        // profiler's GC.Alloc recorder is the meter, checked by a positive control)
        long control;
        bool meterWorks = TestHarness.AllocMeterWorks(out control);
        Check("the allocation meter passes its positive control (" + TestHarness.AllocControlCount + " small arrays read as " + control + " bytes)", meterWorks);

        // the warning's own frame, exactly as BossWarningHud.Update drives it
        int frames = 0;
        float eta = wm.SecondsLeftInWorld;
        long allocated = TestHarness.AllocatedBytes(() =>
        {
        while (hud.Countdown.Active && hud.Countdown.Shown > .2f && frames < 4000)
        {
            // (the level clock itself is WorldManager's; the estimate falls in step)
            eta -= Dt;
            var input = BossWarning.Read(wm);
            hud.Step(input, Mathf.Max(0f, eta) + 0f * wm.SecondsLeftInWorld, Dt, Dt);
            float read = BossWarning.SecondsToBoss + BossWarning.ShownSeconds + BossWarning.SpawnCalm01;
            if (read < -1f) break;
            frames++;
        }
        });
        Check("the whole countdown, T-" + (frames * Dt + .2f).ToString("F0") + " to the boss (" + frames + " frames, every digit change, T-10, T-3): " +
              allocated + " bytes allocated", meterWorks && frames > 1200 && allocated == 0);
        Drop(rig);

        // idle (no warning up) is free too
        rig = Fly(0);
        for (int i = 0; i < 30; i++) rig.Frame();
        hud = rig.hud;
        wm = rig.wm;
        var idleHud = hud;
        var idleWm = wm;
        allocated = TestHarness.AllocatedBytes(() =>
        {
            for (int i = 0; i < 600; i++) idleHud.Step(BossWarning.Read(idleWm), idleWm.SecondsLeftInWorld, Dt, Dt);
        });
        Check("600 idle frames before the warning: " + allocated + " bytes allocated", meterWorks && allocated == 0 && !hud.Visible);
        Drop(rig);
    }

    // ---- 11. the name, the tunables ---------------------------------------------------------

    static void NameAndTunables()
    {
        for (int w = 0; w < WorldManager.LiveWorldCount; w++)
        {
            var boss = BossCatalog.ForWorld(w);
            var rig = Fly(w, .3f);
            SetDistance(rig, 29f);
            rig.Frame();
            bool secret = rig.hud.SubShown == BossWarningConfig.UnknownLine;
            Drop(rig);

            rig = Fly(w, .3f);
            Codex.Discover(boss.id);
            SetDistance(rig, 29f);
            rig.Frame();
            Check(boss.name + ": a secret until met (\"" + BossWarningConfig.UnknownLine + "\"), then named (\"" + rig.hud.SubShown + "\")",
                  secret && rig.hud.SubShown == boss.name + BossWarningConfig.KnownSuffix && rig.hud.TitleShown == "BOSS INCOMING");
            Drop(rig);
        }

        var r = Fly(0, .3f);
        BossWarningConfig.RevealNameBeforeFirstSight = true;
        SetDistance(r, 29f);
        r.Frame();
        Check("RevealNameBeforeFirstSight names an unmet boss", r.hud.SubShown == "VOID ARCHON  APPROACHING");
        BossWarningConfig.RevealNameBeforeFirstSight = false;

        Check("defaults: lead 30, beats at 10 and 3, banner 2.6 s", BossWarningConfig.LeadSeconds == 30f &&
              BossWarningConfig.CloseAt == 10f && BossWarningConfig.FinalAt == 3f && BossWarningConfig.BannerSeconds == 2.6f);
        Check("the spawn-calm hook is off by default (0 with the boss " + BossWarning.SecondsToBoss.ToString("F1") + " s away)",
              BossWarningConfig.CalmFinalSeconds == 0f && BossWarning.SpawnCalm01 == 0f);
        SetDistance(r, 2.5f);
        BossWarningConfig.CalmFinalSeconds = 5f;
        Check("... and ramps over its final seconds when a tuner turns it on (" + BossWarning.SpawnCalm01.ToString("F2") + " at 2.5 of 5 s)",
              Mathf.Abs(BossWarning.SpawnCalm01 - .5f) < .05f);
        BossWarningConfig.CalmFinalSeconds = 0f;
        Drop(r);

        // a different lead moves the trigger, nothing else
        BossWarningConfig.LeadSeconds = 20f;
        var c = new BossCountdown();
        c.Step(BossWarningInput.Ahead, 25f, Dt);
        bool waits = !c.Active;
        c.Step(BossWarningInput.Ahead, 20f, Dt);
        Check("LeadSeconds is the one number for the lead (20: waits at 25, fires at 20)", waits && c.Active && c.Seconds == 20);
        BossWarningConfig.LeadSeconds = 30f;
    }
}
