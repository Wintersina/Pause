using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

// After the final world: the KEEP FLYING / LOOP BACK choice, loops, and the
// speed score multiplier (FinalChoicePanel, LoopRules, RunLoop, ScoreRules).
//
//   1  the choice appears after the Ember boss and freezes time, scripted
//      (no pause spent); Back picks nothing; the countdown picks the encore
//      (ONE MORE EMBER, THEN LOOP)
//   2  KEEP FLYING stays in Ember: no portal, no boss, escalation, capped
//   3  LOOP BACK portals to the run's start world, score kept, loop + 1, and
//      the next boss / portal sequence works (also a start world of Ember)
//   4  per-loop difficulty and bonus scaling apply and stay capped
//   5  speed multiplier tiers, stacking with the chain, within the cap
//   6  HUD loop and multiplier badges fit at 9:16 .. 9:24 and Fold
//   7  the death panel shows LOOPS and the highest multiplier
//   8  the tutorial scores nothing; developer runs never save
//   9  the developer FINAL trigger (BOSS RUSH FINAL)
//  10  the simulated-run breakdown (logged as [LOOP] SIM lines)
//  11  a timed-out choice: Ember once more as a loop pass, its boss, then
//      LOOP BACK with no second prompt; score carries over
//  12  loops and KEEP FLYING raise the spawner's density by LoopRules'
//      factor, and the SpawnLane gap guard holds at max density
public static class LoopTest
{
    static int fails;
    static void Check(string what, bool ok)
    {
        Debug.Log((ok ? "[LOOP] PASS  " : "[LOOP] FAIL  ") + what);
        if (!ok) fails++;
    }

    public static void Run()
    {
        TestHarness.Exit(Execute());
    }

    const BindingFlags Inst = BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance;
    const int Ember = 3;

    public static int Execute()
    {
        fails = 0;
        using var sandbox = new TestHarness.Sandbox();
        int rush = PlayerPrefs.GetInt(BossDev.RushKey, -1);
        try
        {
            ChoiceAppearsAndFreezes();
            KeepFlyingEscalatesInEmber();
            LoopBackGoesToTheStartWorld();
            LoopBackFromAnEmberStart();
            LoopScalingAppliesAndCaps();
            SpeedMultiplierTiersAndCap();
            HudBadgesFit();
            DeathPanelShowsLoops();
            TutorialAndDeveloperRuns();
            DeveloperFinalTrigger();
            SimulatedRun();
            TimeoutPlaysEmberOnceMoreThenLoops();
            TimeoutFromAnEmberStart();
            LoopDensityRaisesSpawnRate();
            if (TestHarness.Slow("Loop: 240s max-density spawner runs")) GapGuardHoldsAtMaxDensity();
        }
        finally
        {
            RunLoop.Reset();
            BossEncounter.ResetRun();
            if (FinalChoicePanel.Instance != null) Object.DestroyImmediate(FinalChoicePanel.Instance.gameObject);
            BackNavigator.ResetHooks();
            buttonClicks.playerDied = false;
            if (rush < 0) PlayerPrefs.DeleteKey(BossDev.RushKey);
            else PlayerPrefs.SetInt(BossDev.RushKey, rush);
        }
        Debug.Log("[LOOP] failures: " + fails);
        return fails;
    }

    // ---- fixtures -----------------------------------------------------------

    static void FreshScene(int world, int startWorld)
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        BossEncounter.ResetRun();
        var camGo = new GameObject("Main Camera", typeof(Camera));
        camGo.tag = "MainCamera";
        var cam = camGo.GetComponent<Camera>();
        cam.orthographic = true;
        cam.orthographicSize = 5f;
        camGo.transform.position = new Vector3(0f, 0f, -10f);
        buttonClicks.playerDied = false;
        startMenu.youAreInTutorial = false;
        score.pauseCounter = 0;   // the world runs without a touch in batch mode
        moveBackGround.speed = .37f;
        ShipStartSpeed.EquippedHudOverride = () => ShipStartSpeed.StockHud;   // a stock start, whatever is equipped
        Time.timeScale = 1f;
        PlayerPrefs.SetString("HasDoneTut", "true");
        PlayerPrefs.SetInt(DeveloperUnlocks.EnabledKey, 0);
        PlayerPrefs.SetInt(WorldManager.PrefsCurrentWorld, world);
        PlayerPrefs.DeleteKey(RunScore.BestScoreKey);
        RunScore.EndRun(RunScore.RunId);
        RunScore.BeginRun(true, true);
        RunLoop.StartWorld = startWorld;   // WorldManager.Start would set this
    }

    // `distance`: what is left to fly (-1 = the level is over).
    static WorldManager World(float distance)
    {
        var wm = new GameObject("~WorldManager").AddComponent<WorldManager>();
        wm.SendMessage("Awake");
        typeof(WorldManager).GetField("distanceLeft", Inst).SetValue(wm, distance);
        return wm;
    }

    static (moveBackGround bg, enmiesOnBoard enemies) Board()
    {
        var bg = new GameObject("bg").AddComponent<moveBackGround>();
        var enemies = new GameObject("spawner").AddComponent<enmiesOnBoard>();
        return (bg, enemies);
    }

    static void RunWhile(BossEncounter e, BossEncounter.Phase phase, float dt = .1f, int budget = 3000)
    {
        for (int i = 0; i < budget && e.State == phase; i++) e.Step(dt, 1f);
    }

    // Level clock out -> the boss, all the way to Done.
    static void PlayBoss(WorldManager wm)
    {
        // Fly the rest of the level in one step, at whatever speed it is.
        wm.Tick((wm.DistanceLeft + 1f) / Mathf.Max(moveBackGround.speed, .01f));
        var e = BossEncounter.Instance;
        if (e == null || !BossEncounter.Running) return;
        e.Step(.1f, 1f);
        RunWhile(e, BossEncounter.Phase.Intro);
        RunWhile(e, BossEncounter.Phase.Fight);
        RunWhile(e, BossEncounter.Phase.Outro);
    }

    static void Advance(WorldManager wm)
    {
        // The rest of Advance is presentation (painter, music, backdrop):
        // the state it changes is set before any of it can throw.
        try { wm.Advance(); }
        catch (System.Exception ex) { Debug.LogWarning("[LOOP] Advance threw (presentation only): " + ex.Message); }
    }

    // ---- 1. the choice -------------------------------------------------------

    static void ChoiceAppearsAndFreezes()
    {
        FreshScene(Ember, 0);
        var (bg, _) = Board();
        var wm = World(-1f);
        int pauses = score.pauseCounter = 3;
        wm.Tick(.1f);
        Check("Ember's level end starts its boss", BossEncounter.Running && BossEncounter.Instance.Boss.artKey == "Ember");
        Check("no choice during the fight", !FinalChoicePanel.IsUp);
        var e = BossEncounter.Instance;
        e.Step(.1f, 1f);
        RunWhile(e, BossEncounter.Phase.Intro);
        RunWhile(e, BossEncounter.Phase.Fight);
        RunWhile(e, BossEncounter.Phase.Outro);
        Check("encounter done", e.State == BossEncounter.Phase.Done);
        Check("the choice panel is up after the Ember boss", FinalChoicePanel.IsUp && wm.Route == WorldManager.FinalRoute.Choosing);
        Check("... and no portal opened", !wm.PortalIsOpen && Object.FindFirstObjectByType<Portal>() == null);

        Time.timeScale = 1f;
        bg.SendMessage("Update");
        Check("the choice freezes time (timeScale 0) as a scripted freeze", BossEncounter.ScriptedFreeze && Time.timeScale == 0f);
        Check("a press on the panel is free (no pause spent)", BossEncounter.FreePress && score.pauseCounter == pauses);
        float left = wm.SecondsLeftInWorld;
        wm.Tick(5f);
        Check("the level clock stays stopped while choosing", wm.SecondsLeftInWorld == left && wm.Route == WorldManager.FinalRoute.Choosing);

        var panel = FinalChoicePanel.Instance;
        Check("the panel offers KEEP FLYING and LOOP BACK", panel.KeepButton != null && panel.LoopButton != null);
        Check("each choice says what it means in one short line ('" + panel.KeepLine.text + "' / '" + panel.LoopLine.text + "')",
              panel.KeepLine.text == FinalChoicePanel.KeepLineFor("Ember") &&
              panel.LoopLine.text == FinalChoicePanel.LoopLineFor("Space", 2));
        Check("the buttons have the cartoon press", panel.KeepButton.GetComponent<CelPress>() != null &&
              panel.LoopButton.GetComponent<CelPress>() != null);

        // Back / Escape: picks nothing and never leaves the run.
        string loaded = null;
        BackNavigator.LoadScene = s => loaded = s;
        BackNavigator.Back();
        Check("Back picks nothing and doesn't quit", FinalChoicePanel.IsUp && loaded == null &&
              wm.Route == WorldManager.FinalRoute.Choosing);
        BackNavigator.ResetHooks();

        // Layout: every line fits its card, the cards fit the panel.
        foreach (var t in panel.Panel.GetComponentsInChildren<Text>(true))
        {
            var r = t.rectTransform.rect;
            bool fits = t.horizontalOverflow == HorizontalWrapMode.Wrap
                ? t.preferredHeight <= r.height + 1f
                : t.preferredWidth <= r.width + 1f;
            Check("choice text '" + t.text + "' fits its rect", fits);
        }
        foreach (var screen in new[] { new Vector2(800, 1422), new Vector2(800, 1733), new Vector2(800, 2133),
                                       new Vector2(800, 935), new Vector2(800, 2050), new Vector2(800, 1066) })
        {
            float s = FinalChoicePanel.FitScale(screen);
            Check("the choice panel fits an 800x" + screen.y + " canvas (scale " + s.ToString("F2") + ")",
                  (FinalChoicePanel.Width + 40f) * s <= screen.x && (FinalChoicePanel.Height + 40f) * s <= screen.y && s >= .5f);
        }

        // The countdown says what it will do, then does it by itself.
        Check("a visible countdown that says what happens ('" + panel.Countdown.text + "')",
              panel.Countdown.text == "AUTO IN 10: ONE MORE EMBER, THEN LOOP" &&
              panel.Countdown.text == FinalChoicePanel.CountdownLabel("Ember", 10f));
        Check("... and fits its line (" + panel.Countdown.preferredWidth.ToString("F0") + " <= " + FinalChoicePanel.CountdownWidth + ")",
              panel.Countdown.preferredWidth <= FinalChoicePanel.CountdownWidth + 1f);
        panel.Step(4.2f);
        Check("it counts down ('" + panel.Countdown.text + "')", panel.Countdown.text == "AUTO IN 6: ONE MORE EMBER, THEN LOOP");
        for (int i = 0; i < 70 && FinalChoicePanel.IsUp; i++) panel.Step(.1f);
        Check("after ~10s it picks the encore (one more Ember, then loop)",
              !FinalChoicePanel.IsUp && wm.Route == WorldManager.FinalRoute.Encore);
        Check("the first press after the panel is still free", BossEncounter.FreePress);
        Check("no pause was spent by the choice", score.pauseCounter == pauses);
        Check("score.cs treats it like the boss intro (FreePress)",
              File.ReadAllText("Assets/Scripts/Core/score.cs").Contains("if (BossEncounter.FreePress && TouchInput.IsPressed) pauseCounterBool = true;"));
        Check("movePlayer ignores presses on the panel (no teleport onto a button)",
              File.ReadAllText("Assets/Scripts/Ship/movePlayer.cs").Contains("if (FinalChoicePanel.IsUp) return;"));
        Time.timeScale = 1f;
    }

    // ---- 2. KEEP FLYING -------------------------------------------------------

    static void KeepFlyingEscalatesInEmber()
    {
        FreshScene(Ember, 0);
        var (bg, enemies) = Board();
        var wm = World(-1f);
        PlayBoss(wm);
        Check("choosing", FinalChoicePanel.IsUp);
        FinalChoicePanel.Instance.KeepButton.onClick.Invoke();
        Check("KEEP FLYING: the panel goes and the world runs", !FinalChoicePanel.IsUp && !BossEncounter.ScriptedFreeze &&
              wm.Route == WorldManager.FinalRoute.KeepFlying);
        var theme = WorldManager.Worlds[Ember];

        wm.Tick(1f);
        float max0 = bg.maxSpeed, dens0 = LoopDifficulty.DensityScale;
        for (int i = 0; i < 60; i++) wm.Tick(1f);
        Check("still Ember, no portal, no boss after a minute", WorldManager.CurrentIndex == Ember && !wm.PortalIsOpen &&
              !BossEncounter.Running && Object.FindFirstObjectByType<Portal>() == null);
        Check("speed cap creeps past Ember's (" + theme.maxSpeed + " -> " + bg.maxSpeed.ToString("F3") + ")",
              bg.maxSpeed > theme.maxSpeed && bg.maxSpeed > max0);
        Check("spawn density keeps climbing (" + dens0.ToString("F2") + " -> " + LoopDifficulty.DensityScale.ToString("F2") + ")",
              LoopDifficulty.DensityScale > dens0);
        float slow = bg.maxSpeed - theme.maxSpeed;
        Check("slowly: at most +3 HUD speed in the first minute (+" + (slow * 100f).ToString("F1") + ")", slow <= .03f);
        for (int i = 0; i < 1200; i++) wm.Tick(1f);
        Check("and capped: +" + (LoopRules.EndlessSpeedCap * 100f) + " HUD at most, never past HUD " +
              Mathf.RoundToInt(LoopRules.AbsoluteMaxSpeed * 100f) + " (" + bg.maxSpeed.ToString("F3") + ")",
              bg.maxSpeed <= theme.maxSpeed + LoopRules.EndlessSpeedCap + 1e-4f && bg.maxSpeed <= LoopRules.AbsoluteMaxSpeed + 1e-4f &&
              bg.maxSpeed >= theme.maxSpeed + LoopRules.EndlessSpeedCap - 1e-4f);
        Check("density capped at x" + LoopRules.EndlessDensityCap + " (" + LoopDifficulty.DensityScale.ToString("F2") + ")",
              Mathf.Abs(LoopDifficulty.DensityScale - LoopRules.EndlessDensityCap) < 1e-3f);
        Check("twenty minutes on: still Ember, still no portal", WorldManager.CurrentIndex == Ember && !wm.PortalIsOpen);
        Check("Ember's full song keeps looping (it has a loop-out point)",
              WorldMusic.LoopOutSeconds(WorldMusic.EmberTrack) > 0f && theme.musicResource == WorldMusic.EmberTrack && !theme.progressiveMusic);
        Check("the loop index never moved", RunLoop.Index == 0);
        Check("an explicit KEEP FLYING never switches to LOOP BACK by itself",
              wm.Route == WorldManager.FinalRoute.KeepFlying && !RunLoop.EncorePass);
    }

    // ---- 3. LOOP BACK -----------------------------------------------------------

    static void LoopBackGoesToTheStartWorld()
    {
        FreshScene(Ember, 0);
        Board();
        var wm = World(-1f);
        RunScore.Tick(30f, .4f);
        PlayBoss(wm);
        var panel = FinalChoicePanel.Instance;
        Check("choosing", FinalChoicePanel.IsUp);
        long before = RunScore.Total;
        panel.LoopButton.onClick.Invoke();
        Check("LOOP BACK: a portal opens", wm.Route == WorldManager.FinalRoute.LoopBack && wm.PortalIsOpen &&
              Object.FindFirstObjectByType<Portal>() != null);
        Check("the panel is gone and the world runs", !FinalChoicePanel.IsUp && !BossEncounter.ScriptedFreeze);
        Check("choosing changed no score", RunScore.Total == before);

        // A missed loop portal comes back.
        typeof(WorldManager).GetMethod("OnPortalMissed", Inst).Invoke(wm, null);
        foreach (var p in Object.FindObjectsByType<Portal>(FindObjectsSortMode.None)) Object.DestroyImmediate(p.gameObject);
        Check("missed: the clock waits about " + LoopRules.LoopPortalRetrySeconds + "s (" + wm.SecondsLeftInWorld.ToString("F2") + "s)",
              !wm.PortalIsOpen && wm.SecondsLeftInWorld > LoopRules.LoopPortalRetrySeconds - 1f &&
              wm.SecondsLeftInWorld <= LoopRules.LoopPortalRetrySeconds + 1e-3f &&
              Mathf.Approximately(wm.DistanceLeft, moveBackGround.speed * LoopRules.LoopPortalRetrySeconds));
        wm.Tick(LoopRules.LoopPortalRetrySeconds + .1f);
        Check("... and the loop portal opens again", wm.PortalIsOpen && !BossEncounter.Running);

        // Fly through it.
        before = RunScore.Total;
        Advance(wm);
        foreach (var p in Object.FindObjectsByType<Portal>(FindObjectsSortMode.None)) Object.DestroyImmediate(p.gameObject);
        Check("arrives in the run's start world (Space)", WorldManager.CurrentIndex == 0);
        Check("loopIndex 0 -> 1", RunLoop.Index == 1 && RunScore.Parts.loops == 1);
        Check("the score carries over: + Ember's world bonus only (" + before + " -> " + RunScore.Total + ")",
              RunScore.Total == before + ScoreRules.WorldClearedPoints(Ember, 0));
        Check("arrival speed is the loop's (HUD " + Mathf.RoundToInt(moveBackGround.speed * 100f) + ")",
              Mathf.Approximately(moveBackGround.speed, LoopRules.ArrivalSpeed(1)) && moveBackGround.speed > 0f);
        Check("the level distance restarted", Mathf.Approximately(wm.DistanceLeft, wm.WorldDistance) && !wm.PortalIsOpen);

        // Space's boss again, then its portal, then Frost.
        PlayBoss(wm);
        Check("Space's boss came round again on the loop", BossEncounter.DoneInWorld(0));
        Check("... and its portal opened", wm.PortalIsOpen);
        long b2 = RunScore.Total;
        Advance(wm);
        foreach (var p in Object.FindObjectsByType<Portal>(FindObjectsSortMode.None)) Object.DestroyImmediate(p.gameObject);
        Check("Space -> Frost on loop 2, world bonus x1.5 (" + (RunScore.Total - b2) + ")",
              WorldManager.CurrentIndex == 1 && RunScore.Total - b2 == ScoreRules.WorldClearedPoints(0, 1) &&
              ScoreRules.WorldClearedPoints(0, 1) == 75);
        Check("still loop 2", RunLoop.Index == 1);
    }

    static void LoopBackFromAnEmberStart()
    {
        // A run that began in Ember (furthest unlocked, or the developer's pick)
        // loops back to Ember itself -- and meets its boss again.
        FreshScene(Ember, Ember);
        Board();
        var wm = World(-1f);
        PlayBoss(wm);
        wm.Choose(true);
        Advance(wm);
        foreach (var p in Object.FindObjectsByType<Portal>(FindObjectsSortMode.None)) Object.DestroyImmediate(p.gameObject);
        Check("Ember start: LOOP BACK lands in Ember, loop 2", WorldManager.CurrentIndex == Ember && RunLoop.Index == 1);
        long before = RunScore.Total;
        PlayBoss(wm);
        Check("Ember's boss again on the loop, paying x1.5 (" + (RunScore.Total - before) + ")",
              BossEncounter.DoneInWorld(Ember) && RunScore.Total - before >= ScoreRules.BossPoints(false, 0f, false, 1) &&
              ScoreRules.BossPoints(false, 0f, false, 1) == 225);
        Check("... then the choice again", FinalChoicePanel.IsUp && wm.Route == WorldManager.FinalRoute.Choosing);
        FinalChoicePanel.Instance.Pick(false);
    }

    // ---- 4. per-loop scaling ---------------------------------------------------

    static void LoopScalingAppliesAndCaps()
    {
        Check("loop 0 is the game as it was", LoopRules.ArrivalSpeed(0) == 0f && LoopRules.RampScale(0) == 1f &&
              LoopRules.MaxSpeedBonus(0) == 0f && LoopRules.PhaseRampScale(0) == 1f && LoopRules.DensityScale(0) == 1f &&
              LoopRules.BossCooldownScale(0) == 1f && LoopRules.BossHeadStart(0) == 0f && LoopRules.BonusScale(0) == 1f);
        bool monotonic = true;
        for (int l = 1; l <= 12; l++)
        {
            monotonic &= LoopRules.ArrivalSpeed(l) >= LoopRules.ArrivalSpeed(l - 1) &&
                         LoopRules.RampScale(l) >= LoopRules.RampScale(l - 1) &&
                         LoopRules.PhaseRampScale(l) >= LoopRules.PhaseRampScale(l - 1) &&
                         LoopRules.DensityScale(l) >= LoopRules.DensityScale(l - 1) &&
                         LoopRules.BossCooldownScale(l) <= LoopRules.BossCooldownScale(l - 1) &&
                         LoopRules.BonusScale(l) >= LoopRules.BonusScale(l - 1);
        }
        Check("every loop is at least as hard as the last", monotonic);
        Check("each loop starts harder: loop 2 > loop 1", LoopRules.ArrivalSpeed(1) > 0f && LoopRules.RampScale(1) > 1f &&
              LoopRules.PhaseRampScale(1) > 1f && LoopRules.DensityScale(1) > 1f && LoopRules.BossCooldownScale(1) < 1f);
        int cap = LoopRules.MaxScaledLoops;
        Check("difficulty stops growing after " + cap + " loops", LoopRules.ArrivalSpeed(50) == LoopRules.ArrivalSpeed(cap) &&
              LoopRules.RampScale(50) == LoopRules.RampScale(cap) && LoopRules.DensityScale(50) == LoopRules.DensityScale(cap) &&
              LoopRules.PhaseRampScale(50) == LoopRules.PhaseRampScale(cap) && LoopRules.BossCooldownScale(50) == LoopRules.BossCooldownScale(cap));
        Check("sane caps: arrival <= HUD 12, ramp <= x1.3, density <= x1.3, boss cooldowns >= x0.7",
              LoopRules.ArrivalSpeed(50) <= .12f + 1e-4f && LoopRules.RampScale(50) <= 1.3f + 1e-4f &&
              LoopRules.DensityScale(50) <= 1.3f + 1e-4f && LoopRules.BossCooldownScale(50) >= .7f - 1e-4f);
        foreach (var w in WorldManager.Worlds)
            Check(w.displayName + ": no loop or endless time pushes the cap past HUD 72",
                  LoopRules.MaxSpeed(w.maxSpeed, 50, 1e6f) <= LoopRules.AbsoluteMaxSpeed + 1e-5f);
        Check("bonuses scale gently: x1, x1.5, x2 ... capped at x3",
              LoopRules.BonusScale(1) == 1.5f && LoopRules.BonusScale(2) == 2f && LoopRules.BonusScale(99) == 3f);
        Check("boss 300/150 -> 450/225 on loop 2, world bonus 50 x n x 1.5",
              ScoreRules.BossPoints(true, 0f, false, 1) == 450 && ScoreRules.BossPoints(false, 0f, false, 1) == 225 &&
              ScoreRules.WorldClearedPoints(3, 1) == 300 && ScoreRules.BossPoints(true, 0f, false, 0) == 300);
        Check("small numbers even at the bonus cap (boss <= 900, world <= 600)",
              ScoreRules.BossPoints(true, 0f, false, 99) <= 900 && ScoreRules.WorldClearedPoints(3, 99) <= 600);

        // Applied: a world reached on loop 2 ramps faster, starts faster, spawns denser.
        FreshScene(2, 0);
        var (bg, enemies) = Board();
        var wm = World(-1f);
        RunScore.OnLoop(RunLoop.Advance());   // loop 2
        typeof(WorldManager).GetField("portalOpen", Inst).SetValue(wm, true);
        Advance(wm);   // Verdant -> Ember on loop 2
        var ember = WorldManager.Worlds[Ember];
        Check("loop 2 world: ramp x" + LoopRules.RampScale(1) + " (" + bg.speedRampPerSecond.ToString("F5") + ")",
              Mathf.Approximately(bg.speedRampPerSecond, ember.speedRampPerSecond * LoopRules.RampScale(1)));
        Check("loop 2 world: cap +" + (LoopRules.MaxSpeedBonus(1) * 100f) + " HUD",
              Mathf.Approximately(bg.maxSpeed, ember.maxSpeed + LoopRules.MaxSpeedBonus(1)));
        Check("loop 2 world: phases x" + LoopRules.PhaseRampScale(1) + ", density x" + LoopRules.DensityScale(1),
              Mathf.Approximately(enemies.phaseRampScale, ember.enemyRampScale * LoopRules.PhaseRampScale(1)) &&
              Mathf.Approximately(LoopDifficulty.DensityScale, LoopRules.DensityScale(1)));

        // A boss on loop 2: shorter cooldowns, the second pattern from the start.
        BossEncounter.ResetRun();
        BossEncounter.Begin(Ember, null);
        var e = BossEncounter.Instance;
        e.Step(.1f, 1f);
        RunWhile(e, BossEncounter.Phase.Intro);
        Check("loop 2 boss: cooldowns x" + LoopRules.BossCooldownScale(1) + ", one more pattern from the start",
              e.Actor != null && Mathf.Approximately(e.Actor.CooldownScale, LoopRules.BossCooldownScale(1)) &&
              BossCatalog.UnlockedAttacks(e.Boss, e.Actor.PatternHeadStart) == Mathf.Min(2, e.Boss.attacks.Length) &&
              BossCatalog.UnlockedAttacks(e.Boss, 0f) == 1);
        BossEncounter.ResetRun();
        RunLoop.Reset();
        BossEncounter.Begin(Ember, null);
        e = BossEncounter.Instance;
        e.Step(.1f, 1f);
        RunWhile(e, BossEncounter.Phase.Intro);
        Check("first-pass boss unchanged", e.Actor != null && e.Actor.CooldownScale == 1f && e.Actor.PatternHeadStart == 0f);
        BossEncounter.ResetRun();
    }

    // ---- 5. speed multiplier ----------------------------------------------------

    static void SpeedMultiplierTiersAndCap()
    {
        var expect = new (int hud, float m)[]
        {
            (0, 1f), (19, 1f), (20, 1.25f), (34, 1.25f), (35, 1.5f), (49, 1.5f), (50, 2f), (64, 2f), (65, 2.5f), (72, 2.5f), (99, 2.5f),
        };
        foreach (var (hud, m) in expect)
            Check("HUD " + hud + " -> x" + m, Mathf.Approximately(ScoreRules.SpeedMultiplierFor(hud / 100f), m));
        Check("x1.0 / x1.25 / x1.5 / x2 / x2.5 labels",
              ScoreRules.MultiplierLabel(1.25f) == "x1.25" && ScoreRules.MultiplierLabel(1.5f) == "x1.5" &&
              ScoreRules.MultiplierLabel(2f) == "x2" && ScoreRules.MultiplierLabel(2.5f) == "x2.5");
        Check("stacks with the chain: x2 chain at x1.5 = x3", Mathf.Approximately(ScoreRules.Combined(2, 1.5f), 3f));
        Check("overall cap x" + ScoreRules.MaxTotalMultiplier + ": x4 chain at x2.5 is x8, not x10",
              Mathf.Approximately(ScoreRules.Combined(4, 2.5f), ScoreRules.MaxTotalMultiplier) && ScoreRules.MaxTotalMultiplier == 8f);

        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        PlayerPrefs.SetString("HasDoneTut", "true");
        buttonClicks.playerDied = false;
        RunScore.EndRun(RunScore.RunId);
        RunScore.BeginRun(true, true);
        var rock = EnemyRoster.One(0, EnemyRole.Rock);
        var heavy = EnemyRoster.One(0, EnemyRole.Big);

        moveBackGround.speed = .10f;
        Check("slow: a rock pays 5", Kill(Enemy(rock)) == 5);
        RunScore.Tick(ScoreRules.ComboWindowSeconds + .1f, 0f);
        moveBackGround.speed = .36f;
        Check("HUD 36 (x1.5): a rock pays 8 (5 x 1.5, rounded)", Kill(Enemy(rock)) == 8);
        Check("the HUD reads the live tier", Mathf.Approximately(RunScore.SpeedMultiplier, 1.5f));
        RunScore.Tick(ScoreRules.ComboWindowSeconds + .1f, 0f);
        moveBackGround.speed = .66f;
        long sum = 0, last = 0;
        for (int i = 0; i < 10; i++) { last = Kill(Enemy(rock)); sum += last; }
        Check("HUD 66, x4 chain: the 10th rock pays 40 (5 x 8 cap, got " + last + ")", last == 40);
        Check("a heavy at the cap pays 320 -- the most any single kill can", Kill(Enemy(heavy)) == 40 * 8);
        Check("the best multiplier is recorded (x8)", Mathf.Approximately(RunScore.Parts.bestMultiplier, 8f));
        long t = RunScore.Total;
        RunScore.Tick(10f, .5f);
        Check("flight at HUD 50 pays 25 x2 = 50 over 10s (" + (RunScore.Total - t) + ")",
              RunScore.Total - t == 50 || RunScore.Total - t == 49);
        t = RunScore.Total;
        RunScore.OnDust(true);
        RunScore.OnBoss(true, 0f, false, Vector3.zero);
        Check("pickups and bosses are not speed-multiplied", RunScore.Total - t == 5 + 300);
        Check("the breakdown still sums to the total", RunScore.Parts.Total == RunScore.Total);
        moveBackGround.speed = 0f;
    }

    static GameObject Enemy(EnemyDef def)
    {
        var go = new GameObject(def.ObjectName);
        go.tag = def.Tag;
        go.AddComponent<EnemyIdentity>().Set(def);
        return go;
    }

    static long Kill(GameObject go)
    {
        long before = RunScore.Total;
        collisionDetection.AwardDestroyedTarget(go);
        Object.DestroyImmediate(go);
        return RunScore.Total - before;
    }

    // ---- 6. HUD badges ------------------------------------------------------------

    static readonly (string name, Vector2 size, Rect safe)[] Screens =
    {
        ("9:16 1080x1920",          new Vector2(1080, 1920), new Rect(0, 0, 1080, 1920)),
        ("9:19.5 1170x2532 notch",  new Vector2(1170, 2532), new Rect(0, 102, 1170, 2532 - 102 - 141)),
        ("9:20 1080x2400 cutout",   new Vector2(1080, 2400), new Rect(0, 0, 1080, 2400 - 118)),
        ("9:22 1080x2640",          new Vector2(1080, 2640), new Rect(0, 0, 1080, 2640 - 96)),
        ("9:24 1080x2880",          new Vector2(1080, 2880), new Rect(0, 0, 1080, 2880 - 120)),
        ("Fold cover 904x2316",     new Vector2(904, 2316),  new Rect(0, 0, 904, 2316 - 100)),
        ("Fold open 1812x2176",     new Vector2(1812, 2176), new Rect(0, 0, 1812, 2176 - 90)),
    };

    static void HudBadgesFit()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/gameS1.unity", OpenSceneMode.Single);
        PlayerPrefs.SetString("HasDoneTut", "true");
        startMenu.youAreInTutorial = false;
        buttonClicks.playerDied = false;
        RunScore.EndRun(RunScore.RunId);
        RunScore.BeginRun(true, true);
        moveBackGround.speed = .1f;
        var go = new GameObject("~HudLoopTest");
        var styler = go.AddComponent<HudStyler>();
        styler.SendMessage("Start");
        var hud = go.GetComponent<ScoreHud>();
        Check("the HUD has the speed and loop badges", hud != null && hud.SpeedBadge != null && hud.LoopBadge != null);
        if (hud == null || hud.SpeedBadge == null || hud.LoopBadge == null) { Object.DestroyImmediate(go); return; }
        Step(hud, .05f);
        Check("x1: no SPD badge; first pass: no LOOP badge", hud.SpeedBadge.text == "" && hud.LoopBadge.text == "");

        moveBackGround.speed = .36f;
        Step(hud, .01f);
        Check("HUD 36: 'SPD x1.5' ('" + hud.SpeedBadge.text + "')", hud.SpeedBadge.text == "SPD x1.5");
        Check("... and it pops as it steps up", hud.SpeedBadge.rectTransform.localScale.x > 1.05f);
        RunLoop.Advance();
        Step(hud, .01f);
        Check("looping: 'LOOP 2' ('" + hud.LoopBadge.text + "'), popping", hud.LoopBadge.text == "LOOP 2" &&
              hud.LoopBadge.rectTransform.localScale.x > 1.05f);
        Check("the speed badge sits on the SPEED row, the loop badge on the PAUSES row",
              hud.SpeedBadge.transform.parent.name == "SpeedText" && hud.LoopBadge.transform.parent.name == ScoreHud.LoopRowName &&
              ScoreHud.LoopRowName == "PauseCounter");
        Check("the LOOP badge is shown and drawn over the pause bar",
              hud.LoopBadge.gameObject.activeInHierarchy && hud.LoopBadge.enabled &&
              hud.LoopBadge.transform.GetSiblingIndex() == hud.LoopBadge.transform.parent.childCount - 1);
        Check("no star dust row for it to sit on", SceneUtil.FindAny("CurrecnyGatheredText") == null &&
              !CloakShieldTest.HudShowsDust(styler.HudRoot));

        // Widest content: the rows' own text plus the badge, never touching.
        var rows = (RectTransform)hud.ScoreText.transform.parent;
        UnityEngine.UI.LayoutRebuilder.ForceRebuildLayoutImmediate(rows);
        var speedRow = hud.SpeedBadge.transform.parent.GetComponent<Text>();
        var pauseRow = hud.LoopBadge.transform.parent.GetComponent<Text>();
        speedRow.text = "SPEED  99";
        pauseRow.text = "PAUSES  15";
        hud.SpeedBadge.text = "SPD x2.5";
        hud.LoopBadge.text = "LOOP 99";
        float width = rows.rect.width;
        float speedFit = speedRow.preferredWidth + 12f + hud.SpeedBadge.preferredWidth;
        float loopFit = pauseRow.preferredWidth + 12f + hud.LoopBadge.preferredWidth;
        Check("SPEED 99 + SPD x2.5 fit across the row (" + speedFit.ToString("F0") + " <= " + width + ")", speedFit <= width);
        Check("PAUSES 15 + LOOP 99 fit across the row (" + loopFit.ToString("F0") + " <= " + width + ")", loopFit <= width);
        Check("the badges fit their own rects", hud.SpeedBadge.preferredWidth <= ScoreHud.BadgeWidth &&
              hud.LoopBadge.preferredWidth <= ScoreHud.BadgeWidth);
        Check("the badges don't grow the read-out (131)", Mathf.Approximately(styler.HudRoot.rect.size.y, 131f));
        // At rest: the one-tick pop (edit mode's unscaled clock never moves
        // on, so it would hold here) may poke past the bottom row's edge, as
        // the PAUSES figure's own punch does.
        hud.SpeedBadge.rectTransform.localScale = hud.LoopBadge.rectTransform.localScale = Vector3.one;
        Check("the badges stay inside the read-out's rows",
              Inside(rows, hud.SpeedBadge.rectTransform) && Inside(rows, hud.LoopBadge.rectTransform));

        var canvas = styler.HudRoot.parent.GetComponent<Canvas>();
        var scaler = canvas.GetComponent<CanvasScaler>();
        Vector2 hudSize = styler.HudRoot.rect.size;
        foreach (var s in Screens)
        {
            float scale = HudStyler.HudCanvasScale(canvas, scaler, s.size);
            Rect r = HudStyler.HudScreenRect(s.safe, s.size, scale, hudSize);
            Rect actions = PauseQuickActions.ScreenRectFor(s.safe, s.size);
            Check(s.name + ": HUD with its badges inside the safe area, clear of the quick actions",
                  s.safe.Contains(r.min) && s.safe.Contains(r.max) && !r.Overlaps(actions) && r.yMin > s.size.y * .7f);
        }
        moveBackGround.speed = 0f;
        Object.DestroyImmediate(go);
    }

    static bool Inside(RectTransform outer, RectTransform inner)
    {
        var corners = new Vector3[4];
        inner.GetWorldCorners(corners);
        foreach (var c in corners)
        {
            Vector2 local = outer.InverseTransformPoint(c);
            if (!outer.rect.Contains(local, true) &&
                !(local.x >= outer.rect.xMin - .5f && local.x <= outer.rect.xMax + .5f &&
                  local.y >= outer.rect.yMin - .5f && local.y <= outer.rect.yMax + .5f)) return false;
        }
        return true;
    }

    static void Step(ScoreHud hud, float dt)
    {
        typeof(ScoreHud).GetMethod("TickDisplay", Inst).Invoke(hud, new object[] { dt });
    }

    // ---- 7. death panel -------------------------------------------------------------

    static void DeathPanelShowsLoops()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/gameS1.unity", OpenSceneMode.Single);
        var canvas = SceneUtil.FindAny("PopUpCanvas");
        var best = SceneUtil.FindAny("playerDeadHighestSpeed").GetComponent<Text>();
        var run = SceneUtil.FindAny("deathSpeedReachedThisRoundText").GetComponent<Text>();
        var dust = SceneUtil.FindAny("playerDeadHighScore").GetComponent<Text>();
        var parts = new RunScore.Breakdown
        {
            distance = 999999, kills = 999999, dust = 99999, atoms = 9999, teleports = 9999, bosses = 99999, worlds = 99999,
            killCount = 999, dustCount = 999, atomCount = 99, teleportCount = 99, bossCount = 9, worldCount = 9,
            loops = 12, bestMultiplier = 7.5f,
        };
        var view = DeathPanelView.Build(canvas.transform, best, run, dust,
            SceneUtil.FindAny("Replay").GetComponent<Button>(), SceneUtil.FindAny("MainMenu").GetComponent<Button>(),
            new DeathPanelView.Results { score = 9999999, bestScore = 9999999, newBest = true, ranked = true, parts = parts,
                                         bestSpeed = 99, runSpeed = 99, dustAtStart = 1f, dustWon = 1f });
        view.Skip();
        Canvas.ForceUpdateCanvases();
        var row = view.Panel.Find("Card1/" + DeathPanelView.LoopsRowName);
        Check("the breakdown has a LOOPS line", row != null);
        if (row == null) return;
        var label = row.Find("Label").GetComponent<Text>();
        var count = row.Find("Count").GetComponent<Text>();
        var value = row.Find("Points").GetComponent<Text>();
        Check("LOOPS 12 and the highest multiplier (MAX x7.5) ('" + label.text + "' '" + count.text + "' '" + value.text + "')",
              label.text == "LOOPS" && count.text == "12" && value.text == "MAX x7.5");
        var card = (RectTransform)view.Panel.Find("Card1");
        Check("the LOOPS line sits inside the breakdown card", Inside(card, (RectTransform)row));
        var last = (RectTransform)view.Panel.Find("Card1/Row6");
        Check("... below the last source row, not over it",
              ((RectTransform)row).anchoredPosition.y + DeathPanelView.BreakdownRowHeight * .5f <=
              last.anchoredPosition.y - DeathPanelView.BreakdownRowHeight * .5f + .5f);
        Check("label / count / value keep apart", label.preferredWidth <= label.rectTransform.rect.width &&
              value.preferredWidth <= value.rectTransform.rect.width && count.preferredWidth <= count.rectTransform.rect.width);
        var first = new RunScore.Breakdown();
        Check("a run that never looped: LOOPS 0, MAX x1", DeathPanelView.LoopsCount(first) == "0" &&
              DeathPanelView.BestMultiplierLabel(first) == "MAX x1");
        Object.DestroyImmediate(view.gameObject);
    }

    // ---- 8. tutorial and developer runs ---------------------------------------------

    static void TutorialAndDeveloperRuns()
    {
        // Tutorial / practice: nothing scores, multipliers included.
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        buttonClicks.playerDied = false;
        RunScore.EndRun(RunScore.RunId);
        RunScore.BeginRun(false, false);
        moveBackGround.speed = .66f;
        RunScore.Tick(10f, .66f);
        Kill(Enemy(EnemyRoster.One(0, EnemyRole.Big)));
        RunScore.OnLoop(1);
        RunScore.OnBoss(true, 0f, false, Vector3.zero);
        Check("tutorial: no score at any speed or loop", RunScore.Total == 0 && RunScore.Parts.loops == 0);
        Check("tutorial: the HUD's speed tier reads x1", RunScore.SpeedMultiplier == 1f);
        Check("the tutorial scene has no world manager (so no final choice)",
              !File.ReadAllText("Assets/Scenes/" + score.TutorialScene + ".unity").Contains("WorldManager") &&
              File.ReadAllText("Assets/Scripts/Worlds/WorldManager.cs").Contains("if (scene.name != \"gameS1\") return;"));
        moveBackGround.speed = 0f;

        // Developer: the run loops and scores, but nothing is saved.
        FreshScene(Ember, 0);
        PlayerPrefs.SetInt(DeveloperUnlocks.EnabledKey, 1);
        RunScore.EndRun(RunScore.RunId);
        RunScore.BeginRun(true, !DeveloperUnlocks.Enabled);
        RunLoop.StartWorld = 0;
        int highest = PlayerPrefs.GetInt(WorldManager.PrefsHighestWorld, 0);
        Board();
        var wm = World(-1f);
        PlayBoss(wm);
        wm.Choose(true);
        Advance(wm);
        foreach (var p in Object.FindObjectsByType<Portal>(FindObjectsSortMode.None)) Object.DestroyImmediate(p.gameObject);
        Check("developer run loops and scores", RunLoop.Index == 1 && RunScore.Total > 0 && RunScore.Scoring);
        RunScore.EndRun(RunScore.RunId);
        Check("developer run: BestScore never written", !PlayerPrefs.HasKey(RunScore.BestScoreKey));
        Check("looping back never lowers the furthest world reached", PlayerPrefs.GetInt(WorldManager.PrefsHighestWorld, 0) == highest);
        foreach (var f in new[] { "Assets/Scripts/Worlds/LoopRules.cs", "Assets/Scripts/Worlds/FinalChoicePanel.cs" })
            Check(Path.GetFileName(f) + " never writes PlayerPrefs (the loop is in-memory only)",
                  !File.ReadAllText(f).Contains("PlayerPrefs"));
        PlayerPrefs.SetInt(DeveloperUnlocks.EnabledKey, 0);
        Check("a new run starts back on loop 1", RunScore.BeginRun(true, true) > 0 && RunLoop.Index == 0);
    }

    // ---- 9. developer FINAL trigger ------------------------------------------------------

    static void DeveloperFinalTrigger()
    {
        FreshScene(0, 0);
        Board();
        var wm = World(150f);
        Check("FINAL needs developer mode", !BossDev.TriggerFinal() && WorldManager.CurrentIndex == 0);
        PlayerPrefs.SetInt(DeveloperUnlocks.EnabledKey, 1);
        Check("FINAL jumps to Ember and starts its boss", BossDev.TriggerFinal() && WorldManager.CurrentIndex == Ember &&
              BossEncounter.Running && BossEncounter.Instance.Boss.artKey == "Ember");
        var e = BossEncounter.Instance;
        e.Step(.1f, 1f);
        RunWhile(e, BossEncounter.Phase.Intro);
        Check("... with a short fight (" + e.Remaining.ToString("F1") + "s)", e.Remaining <= BossEncounter.DevShortFightSeconds + .01f);
        RunWhile(e, BossEncounter.Phase.Fight);
        RunWhile(e, BossEncounter.Phase.Outro);
        Check("... straight to the choice", FinalChoicePanel.IsUp);
        FinalChoicePanel.Instance.LoopButton.onClick.Invoke();
        Advance(wm);
        foreach (var p in Object.FindObjectsByType<Portal>(FindObjectsSortMode.None)) Object.DestroyImmediate(p.gameObject);
        Check("LOOP BACK still goes to where the run began (Space)", WorldManager.CurrentIndex == 0 && RunLoop.Index == 1);

        // The Options switch: OFF -> ON -> FINAL -> OFF, and the rush itself.
        Check("BOSS RUSH cycles OFF -> ON -> FINAL -> OFF",
              BossDev.NextRushMode(BossDev.RushOff) == BossDev.RushOn && BossDev.NextRushMode(BossDev.RushOn) == BossDev.RushFinal &&
              BossDev.NextRushMode(BossDev.RushFinal) == BossDev.RushOff && BossDev.RushLabel(BossDev.RushFinal) == "BOSS RUSH  FINAL");
        Check("Options cycles the switch", File.ReadAllText("Assets/Scripts/Menu/DeveloperOptions.cs")
              .Contains("BossDev.SetRushMode(BossDev.NextRushMode(BossDev.RushMode))"));

        FreshScene(0, 0);
        PlayerPrefs.SetInt(DeveloperUnlocks.EnabledKey, 1);
        Board();
        World(150f);
        BossDev.SetRushMode(BossDev.RushFinal);
        Check("BOSS RUSH FINAL is on", BossDev.FinalRushEnabled && BossDev.RushEnabled);
        var enc = BossEncounter.Ensure();
        for (int i = 0; i < 60 && !BossEncounter.Running; i++) enc.Step(.1f, 1f);
        Check("BOSS RUSH FINAL: a few seconds in, the Ember boss", BossEncounter.Running && WorldManager.CurrentIndex == Ember);
        BossDev.SetRushMode(BossDev.RushOff);
        PlayerPrefs.SetInt(DeveloperUnlocks.EnabledKey, 0);
        Check("FINAL needs developer mode", !BossDev.FinalRushEnabled);
        BossEncounter.ResetRun();
    }

    // ---- 10. simulated run ----------------------------------------------------------------
    //
    // A modelled run, to judge the balance (logged, plus a few sanity checks):
    // each world flown for its distance (120s at the baseline pace) from its arrival speed at its
    // (loop-scaled) ramp to its cap; a kill every 4s worth 8 base (a mix of
    // rocks 5, fighters 5-20, aliens 15), chained in threes (x1, x1, x2);
    // 25 small + 5 large star dust, 4 atoms, 8 full blinks; the 36s boss fight
    // at HUD 20 with 10 shots downed, the boss destroyed; the world bonus on
    // leaving. Old = before the speed multiplier and loop scaling.

    struct Sim { public double distance, kills, other, bosses, worlds; public double Total => distance + kills + other + bosses + worlds; }

    static Sim SimWorld(int world, int loop, bool speedMultiplier, bool loopScaling, bool leaves)
    {
        var theme = WorldManager.Worlds[world];
        int L = loopScaling ? loop : 0;
        float speed = LoopRules.ArrivalSpeed(L);
        float rate = theme.speedRampPerSecond * LoopRules.RampScale(L);
        float max = LoopRules.MaxSpeed(theme.maxSpeed, L, 0f);
        var s = new Sim();
        const float dt = .05f;
        float nextKill = 4f;
        int killNo = 0;
        float flown = 0f, length = WorldManager.WorldDistanceFor(world);
        for (float t = 0f; flown < length; t += dt)
        {
            speed = Mathf.Min(max, speed + rate * dt);
            flown += speed * dt;
            float sm = speedMultiplier ? ScoreRules.SpeedMultiplierFor(speed) : 1f;
            s.distance += ScoreRules.DistancePoints(speed, dt) * sm;
            if (t >= nextKill)
            {
                nextKill += 4f;
                int chain = (killNo++ % 3) == 2 ? 2 : 1;
                s.kills += Mathf.RoundToInt(8 * (speedMultiplier ? ScoreRules.Combined(chain, sm) : chain));
            }
        }
        // the boss fight: 36s at HUD 20
        float bm = speedMultiplier ? ScoreRules.SpeedMultiplierFor(BossConfig.FightSpeed) : 1f;
        s.distance += ScoreRules.DistancePoints(BossConfig.FightSpeed, BossConfig.FightSeconds) * bm;
        s.kills += 10 * ScoreRules.BossShot;
        s.bosses += ScoreRules.BossPoints(true, 0f, false, loopScaling ? loop : 0);
        s.other += 25 * ScoreRules.SmallDust + 5 * ScoreRules.LargeDust + 4 * ScoreRules.HealAtom + 8 * ScoreRules.Teleport;
        if (leaves) s.worlds += ScoreRules.WorldClearedPoints(world, loopScaling ? loop : 0);
        return s;
    }

    static Sim Add(Sim a, Sim b)
    {
        return new Sim { distance = a.distance + b.distance, kills = a.kills + b.kills, other = a.other + b.other,
                         bosses = a.bosses + b.bosses, worlds = a.worlds + b.worlds };
    }

    static string Line(string name, Sim s)
    {
        return string.Format("{0,-26} distance {1,6:0}  kills {2,6:0}  pickups {3,5:0}  bosses {4,5:0}  worlds {5,5:0}  = {6,7:0}",
                             name, s.distance, s.kills, s.other, s.bosses, s.worlds, s.Total);
    }

    static void SimulatedRun()
    {
        var pass = new Sim[2];
        var oldPass = new Sim();
        for (int loop = 0; loop < 2; loop++)
        {
            for (int w = 0; w < WorldManager.Worlds.Length; w++)
            {
                var ws = SimWorld(w, loop, true, true, true);
                Debug.Log("[LOOP] SIM " + Line("loop " + (loop + 1) + " " + WorldManager.Worlds[w].displayName, ws));
                pass[loop] = Add(pass[loop], ws);
                if (loop == 0) oldPass = Add(oldPass, SimWorld(w, 0, false, false, true));
            }
            Debug.Log("[LOOP] SIM " + Line("LOOP " + (loop + 1) + " TOTAL", pass[loop]));
        }
        var space = SimWorld(0, 0, true, true, true);
        var spaceOld = SimWorld(0, 0, false, false, true);
        Debug.Log("[LOOP] SIM " + Line("old rules, first pass", oldPass));
        Debug.Log("[LOOP] SIM " + Line("old rules, Space alone", spaceOld));
        Debug.Log("[LOOP] SIM " + Line("new rules, Space alone", space));
        var run = Add(pass[0], pass[1]);
        Debug.Log("[LOOP] SIM " + Line("first pass + one loop", run));

        // KEEP FLYING instead: three more minutes in Ember after the first pass.
        var ember = WorldManager.Worlds[Ember];
        float speed = BossConfig.FightSpeed, rate = ember.speedRampPerSecond;
        double dist = 0, kills = 0;
        int killNo = 0;
        float nextKill = 4f;
        for (float t = 0f; t < 180f; t += .05f)
        {
            float max = LoopRules.MaxSpeed(ember.maxSpeed, 0, t);
            speed = Mathf.Min(max, speed + rate * .05f);
            float sm = ScoreRules.SpeedMultiplierFor(speed);
            dist += ScoreRules.DistancePoints(speed, .05f) * sm;
            if (t >= nextKill) { nextKill += 4f; int chain = (killNo++ % 3) == 2 ? 2 : 1; kills += Mathf.RoundToInt(8 * ScoreRules.Combined(chain, sm)); }
        }
        var keep = pass[0];
        keep.worlds -= ScoreRules.WorldClearedPoints(Ember, 0);   // no portal out of Ember
        keep.distance += dist;
        keep.kills += kills;
        Debug.Log("[LOOP] SIM " + Line("first pass + 3 min KEEP FLYING", keep) +
                  "  (Ember speed after 3 min: HUD " + Mathf.RoundToInt(speed * 100f) + ")");

        Check("simulated Space level + boss stays in the low thousands (" + space.Total.ToString("0") + ")",
              space.Total > 800 && space.Total < 2000);
        Check("the speed multiplier raises a pass by a fair amount, not a landslide (" +
              (pass[0].Total / oldPass.Total).ToString("F2") + "x)",
              pass[0].Total > oldPass.Total && pass[0].Total < oldPass.Total * 1.8);
        Check("a loop is worth more than the first pass (" + pass[1].Total.ToString("0") + " > " + pass[0].Total.ToString("0") + ")",
              pass[1].Total > pass[0].Total);
    }

    // ---- 11. the choice timed out ---------------------------------------------------------

    static void TimeOutChoice()
    {
        var panel = FinalChoicePanel.Instance;
        for (int i = 0; i < 200 && FinalChoicePanel.IsUp; i++) panel.Step(.1f);
    }

    static void TimeoutPlaysEmberOnceMoreThenLoops()
    {
        FreshScene(Ember, 0);
        var (bg, enemies) = Board();
        var wm = World(-1f);
        RunScore.Tick(30f, .4f);
        PlayBoss(wm);
        Check("timeout: choosing after the Ember boss", FinalChoicePanel.IsUp && wm.Route == WorldManager.FinalRoute.Choosing);
        long before = RunScore.Total;
        TimeOutChoice();
        var ember = WorldManager.Worlds[Ember];
        Check("timeout: the panel goes, the world runs, no portal",
              !FinalChoicePanel.IsUp && !BossEncounter.ScriptedFreeze && !wm.PortalIsOpen &&
              Object.FindFirstObjectByType<Portal>() == null && wm.Route == WorldManager.FinalRoute.Encore);
        Check("timeout: still in Ember, a full level to fly (" + wm.DistanceLeft + ")",
              WorldManager.CurrentIndex == Ember && Mathf.Approximately(wm.DistanceLeft, wm.WorldDistance));
        Check("timeout: the loop index has not moved yet (it moves with the portal)", RunLoop.Index == 0 && RunLoop.EncorePass);
        Check("timeout: scaled as a loop pass (ramp x" + LoopRules.RampScale(1) + ", cap +" + LoopRules.MaxSpeedBonus(1) * 100f +
              ", phases x" + LoopRules.PhaseRampScale(1) + ", density x" + LoopRules.DensityScale(1) + ")",
              Mathf.Approximately(bg.speedRampPerSecond, ember.speedRampPerSecond * LoopRules.RampScale(1)) &&
              Mathf.Approximately(bg.maxSpeed, ember.maxSpeed + LoopRules.MaxSpeedBonus(1)) &&
              Mathf.Approximately(enemies.phaseRampScale, ember.enemyRampScale * LoopRules.PhaseRampScale(1)) &&
              Mathf.Approximately(LoopDifficulty.DensityScale, LoopRules.DensityScale(1)));
        Check("timeout: the choice itself changed no score", RunScore.Total == before);

        for (int i = 0; i < 30; i++) wm.Tick(1f);
        Check("30s in: still Ember, no boss, no portal", WorldManager.CurrentIndex == Ember && !BossEncounter.Running &&
              !wm.PortalIsOpen && Mathf.Abs(wm.DistanceLeft - (wm.WorldDistance - 30f * moveBackGround.speed)) < 1e-3f);
        Check("... and no endless escalation (that is KEEP FLYING's)", Mathf.Approximately(LoopDifficulty.DensityScale, LoopRules.DensityScale(1)));

        // The level ends: the Ember boss again, at the next loop's difficulty.
        wm.Tick(wm.WorldLength);
        var e = BossEncounter.Instance;
        Check("timeout: the level ends in the Ember boss again", BossEncounter.Running && e.Boss.artKey == "Ember");
        e.Step(.1f, 1f);
        RunWhile(e, BossEncounter.Phase.Intro);
        Check("timeout: the encore boss fights at loop scaling (cooldowns x" + LoopRules.BossCooldownScale(1) + ")",
              e.Actor != null && Mathf.Approximately(e.Actor.CooldownScale, LoopRules.BossCooldownScale(1)) &&
              e.Actor.PatternHeadStart == LoopRules.BossHeadStart(1));
        long beforeBoss = RunScore.Total;
        RunWhile(e, BossEncounter.Phase.Fight);
        RunWhile(e, BossEncounter.Phase.Outro);
        Check("timeout: the encore boss pays as before (survived, this loop's bonus: " + (RunScore.Total - beforeBoss) + ")",
              RunScore.Total - beforeBoss == ScoreRules.BossPoints(false, 0f, false, 0));

        Check("timeout: no second prompt after the encore boss", !FinalChoicePanel.IsUp);
        Check("timeout: straight into LOOP BACK -- the portal to the start world opens",
              wm.Route == WorldManager.FinalRoute.LoopBack && wm.PortalIsOpen && Object.FindFirstObjectByType<Portal>() != null);

        // Missed: it comes back, as LOOP BACK's does.
        typeof(WorldManager).GetMethod("OnPortalMissed", Inst).Invoke(wm, null);
        foreach (var p in Object.FindObjectsByType<Portal>(FindObjectsSortMode.None)) Object.DestroyImmediate(p.gameObject);
        wm.Tick(LoopRules.LoopPortalRetrySeconds + .1f);
        Check("timeout: a missed loop portal opens again", wm.PortalIsOpen && !BossEncounter.Running && !FinalChoicePanel.IsUp);

        long b2 = RunScore.Total;
        Advance(wm);
        foreach (var p in Object.FindObjectsByType<Portal>(FindObjectsSortMode.None)) Object.DestroyImmediate(p.gameObject);
        Check("timeout: arrives in the run's start world (Space), loop 2", WorldManager.CurrentIndex == 0 &&
              RunLoop.Index == 1 && RunScore.Parts.loops == 1 && !RunLoop.EncorePass && wm.Route == WorldManager.FinalRoute.None);
        Check("timeout: the score carries over + Ember's world bonus once (" + b2 + " -> " + RunScore.Total + ")",
              RunScore.Total == b2 + ScoreRules.WorldClearedPoints(Ember, 0) && RunScore.Total > before);
        Check("timeout: loop 2 is scaled as loop 2, not loop 3 (density x" + LoopDifficulty.DensityScale.ToString("F2") + ")",
              Mathf.Approximately(LoopDifficulty.DensityScale, LoopRules.DensityScale(1)) &&
              Mathf.Approximately(moveBackGround.speed, LoopRules.ArrivalSpeed(1)));
    }

    static void TimeoutFromAnEmberStart()
    {
        // Started in Ember: the timeout's LOOP BACK lands in Ember, and the
        // next Ember boss (a real loop end) asks again.
        FreshScene(Ember, Ember);
        Board();
        var wm = World(-1f);
        PlayBoss(wm);
        TimeOutChoice();
        Check("Ember start, timeout: the encore", wm.Route == WorldManager.FinalRoute.Encore);
        PlayBoss(wm);
        Check("Ember start: after the encore boss, the loop portal (no prompt)", wm.Route == WorldManager.FinalRoute.LoopBack &&
              wm.PortalIsOpen && !FinalChoicePanel.IsUp);
        Advance(wm);
        foreach (var p in Object.FindObjectsByType<Portal>(FindObjectsSortMode.None)) Object.DestroyImmediate(p.gameObject);
        Check("Ember start: LOOP 2 in Ember", WorldManager.CurrentIndex == Ember && RunLoop.Index == 1);
        PlayBoss(wm);
        Check("Ember start: the end of loop 2 asks again", FinalChoicePanel.IsUp && wm.Route == WorldManager.FinalRoute.Choosing);
        FinalChoicePanel.Instance.Pick(true);
        Check("... and an explicit LOOP BACK is unchanged", wm.Route == WorldManager.FinalRoute.LoopBack && wm.PortalIsOpen);
        foreach (var p in Object.FindObjectsByType<Portal>(FindObjectsSortMode.None)) Object.DestroyImmediate(p.gameObject);
    }

    // ---- 12. loop density ------------------------------------------------------------------

    static float RollAt(enmiesOnBoard board, Vector2 range)
    {
        Random.InitState(4242);
        return (float)typeof(enmiesOnBoard).GetMethod("Roll", Inst).Invoke(board, new object[] { range });
    }

    static void LoopDensityRaisesSpawnRate()
    {
        FreshScene(2, 0);
        var (_, enemies) = Board();
        var wm = World(-1f);
        var range = new Vector2(2.5f, 5f);
        Check("first pass: density x1", LoopDifficulty.DensityScale == 1f);
        float baseDelay = RollAt(enemies, range);

        // A loop: Verdant -> Ember on loop 2.
        RunScore.OnLoop(RunLoop.Advance());
        typeof(WorldManager).GetField("portalOpen", Inst).SetValue(wm, true);
        Advance(wm);
        float loopDelay = RollAt(enemies, range);
        Check("loop 2: spawn delays shrink by x" + LoopRules.DensityScale(1) + " (" + baseDelay.ToString("F3") + "s -> " +
              loopDelay.ToString("F3") + "s)", Mathf.Abs(baseDelay / loopDelay - LoopRules.DensityScale(1)) < 1e-3f);
        for (int l = 2; l <= LoopRules.MaxScaledLoops + 1; l++)
        {
            LoopDifficulty.DensityScale = LoopRules.Density(l, 0f);
            Check("loop " + (l + 1) + ": x" + LoopRules.DensityScale(l).ToString("F2"),
                  Mathf.Abs(baseDelay / RollAt(enemies, range) - LoopRules.DensityScale(l)) < 1e-3f);
        }

        // KEEP FLYING: its endless climb on top of the loop's own.
        FreshScene(Ember, 0);
        (_, enemies) = Board();
        wm = World(-1f);
        PlayBoss(wm);
        wm.Choose(false);
        for (int i = 0; i < 61; i++) wm.Tick(1f);
        float want = LoopRules.Density(0, wm.EndlessSeconds);
        Check("KEEP FLYING a minute in: delays shrink by x" + want.ToString("F2") + " (" + (baseDelay / RollAt(enemies, range)).ToString("F2") + ")",
              want > 1.15f && Mathf.Abs(baseDelay / RollAt(enemies, range) - want) < 1e-3f);
        for (int i = 0; i < 600; i++) wm.Tick(1f);
        Check("KEEP FLYING capped: x" + LoopRules.EndlessDensityCap + " (" + (baseDelay / RollAt(enemies, range)).ToString("F2") + ")",
              Mathf.Abs(baseDelay / RollAt(enemies, range) - LoopRules.EndlessDensityCap) < 1e-3f);
        Check("the spawner reads it (enmiesOnBoard.Roll)", File.ReadAllText("Assets/Scripts/Gameplay/enmiesOnBoard.cs")
              .Contains("/ Mathf.Max(0.1f, DensityMultiplier() * LoopDifficulty.DensityScale)"));
        RunLoop.Reset();
        Check("a new run is back to x1", LoopDifficulty.DensityScale == 1f);
    }

    static void SetWorldManager(WorldManager wm)
    {
        typeof(WorldManager).GetField("<Instance>k__BackingField", BindingFlags.NonPublic | BindingFlags.Static).SetValue(null, wm);
    }

    static void ScrollHazards(float dy)
    {
        foreach (var id in Object.FindObjectsByType<EnemyIdentity>(FindObjectsSortMode.None))
        {
            if (id.Def != null && id.Def.role == EnemyRole.Chaser) continue;
            id.transform.position += Vector3.down * dy;
            if (id.transform.position.y < -14f) Object.DestroyImmediate(id.gameObject);
        }
    }

    // The heaviest density the loops can reach (loop 4+, KEEP FLYING capped)
    // on top of the spawner's own x3 final stretch, over a 240s run in every
    // world: no row of the board is ever closed (SpawnLane.ShipGap).
    static void GapGuardHoldsAtMaxDensity()
    {
        EditorSceneLoader.Open("gameS1", OpenSceneMode.Single);
        var wmGo = new GameObject("~LoopDensityWorlds");
        SetWorldManager(wmGo.AddComponent<WorldManager>());
        var spawn = typeof(enmiesOnBoard).GetMethod("spawn", Inst, null, new[] { typeof(float) }, null);
        var select = typeof(enmiesOnBoard).GetMethod("SelectPhase", Inst);
        var elapsed = typeof(enmiesOnBoard).GetField("elapsedFlightSeconds", Inst);
        float max = LoopRules.Density(LoopRules.MaxScaledLoops, 1e6f);
        const float dt = .1f, scroll = 3f, runSeconds = 240f;
        float gap = SpawnLane.ShipGap;
        try
        {
            for (int w = 0; w < WorldManager.Worlds.Length; w++)
            {
                PlayerPrefs.SetInt(WorldManager.PrefsCurrentWorld, w);
                int[] counts = new int[2];
                int closed = 0;
                float tightest = 99f;
                for (int pass = 0; pass < 2; pass++)
                {
                    LoopDifficulty.DensityScale = pass == 0 ? 1f : max;
                    Random.InitState(5100 + w);
                    var board = new GameObject("~LoopDensityBoard").AddComponent<enmiesOnBoard>();
                    board.SendMessage("Start");
                    var seen = new HashSet<EnemyIdentity>();
                    for (float t = 0f; t < runSeconds; t += dt)
                    {
                        elapsed.SetValue(board, t);
                        select.Invoke(board, null);
                        spawn.Invoke(board, new object[] { dt });
                        // the board as it stands this step (nothing spawns or
                        // moves until ScrollHazards): looked up once
                        var live = Object.FindObjectsByType<EnemyIdentity>(FindObjectsSortMode.None);
                        foreach (var id in live)
                            if (seen.Add(id) && id.Def != null) counts[pass]++;
                        if (pass == 1)
                            for (float y = -3f; y <= 1.5f; y += .25f)
                            {
                                float widest = SpawnLane.WidestGap(SpawnLane.RowSpans(y, y + gap, live));
                                tightest = Mathf.Min(tightest, widest);
                                if (widest < gap - 1e-3f) closed++;
                            }
                        ScrollHazards(scroll * dt);
                    }
                    foreach (var id in Object.FindObjectsByType<EnemyIdentity>(FindObjectsSortMode.None)) Object.DestroyImmediate(id.gameObject);
                    foreach (var r in Object.FindObjectsByType<RailLaneScroller>(FindObjectsSortMode.None)) Object.DestroyImmediate(r.gameObject);
                    Object.DestroyImmediate(board.gameObject);
                }
                string name = WorldManager.Worlds[w].displayName;
                Check(string.Format("{0} at max loop density x{1:F2}: {2:F0}s, every row keeps a ship-width gap ({3} closed, tightest {4:F2} u >= {5:F2} u)",
                                    name, max, runSeconds, closed, tightest, gap), closed == 0);
                Check(string.Format("{0}: max density fields more hazards than a first pass ({1} vs {2})", name, counts[1], counts[0]),
                      counts[1] > counts[0]);
            }
        }
        finally
        {
            LoopDifficulty.Reset();
            SetWorldManager(null);
            Object.DestroyImmediate(wmGo);
        }
    }
}
