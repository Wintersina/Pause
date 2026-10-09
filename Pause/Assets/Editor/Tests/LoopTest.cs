using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

// After the final world: its portal back round, loops, and the speed score
// multiplier (WorldManager, Portal, LoopRules, RunLoop, ScoreRules).
// docs/speed-and-loops.md.
//
//   1  the Ember boss is followed by the portal, as in every world: it leads
//      to the run's start world, freezes nothing, stops the level clock and
//      never goes away (no KEEP FLYING / LOOP BACK choice any more)
//   2  waiting at it: still Ember, no boss, the pressure climbs, the natural
//      speed never passes the one cap (HUD 35)
//   3  through it: the run's start world, score kept, loop + 1, arrival speed
//      under the cap, and the next boss / portal sequence works (also a start
//      world of Ember)
//   4  per-loop difficulty (no speed: density, pilot load, threat ceiling,
//      fighter tiers, shot budget and cadence, score) and bonus scaling apply
//      and stay capped
//   5  speed multiplier tiers (x2 at the cap, x2.5 only in a limit break),
//      stacking with the chain, within the cap; loop score scale
//   6  HUD loop and multiplier badges fit at 9:16 .. 9:24 and Fold
//   7  the death panel shows LOOPS and the highest multiplier
//   8  the tutorial scores nothing; developer runs never save
//   9  the developer FINAL trigger (BOSS RUSH FINAL)
//  10  the simulated-run breakdown (logged as [LOOP] SIM lines)
//  11  every loop ends the same way: the final portal again, loop 3 scaled
//      as loop 3
//  12  loops raise the spawner's density by LoopRules' factor, a waiting
//      portal's pressure on top of it, and the SpawnLane gap guard holds at
//      max loop density
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
        // These checks read exact spawn delays to prove the loop / boss hooks;
        // the speed-keyed density cut (EnemyDensity, its own suite) multiplies
        // the same delays, so it is switched off here (the Sandbox puts it back).
        EnemyDensity.Disabled = true;
        // This suite is the loop portal's: Ember's lift-off (LiftoffTest; it
        // starts the loop with no portal, LoopStartsStraightFromTheLiftoff)
        // stands aside so the boss's end opens the portal at once.
        LiftoffCatalog.Enabled = false;
        int rush = PlayerPrefs.GetInt(BossDev.RushKey, -1);
        try
        {
            FinalPortalOpensAndWaits();
            WaitingAtTheFinalPortal();
            LoopBackGoesToTheStartWorld();
            LoopBackFromAnEmberStart();
            LoopStartsStraightFromTheLiftoff();
            LoopScalingAppliesAndCaps();
            SpeedMultiplierTiersAndCap();
            HudBadgesFit();
            DeathPanelShowsLoops();
            TutorialAndDeveloperRuns();
            DeveloperFinalTrigger();
            SimulatedRun();
            FinalPortalEveryLoop();
            LoopDensityRaisesSpawnRate();
            if (TestHarness.Slow("Loop: 240s max-density spawner runs")) GapGuardHoldsAtMaxDensity();
        }
        finally
        {
            LiftoffCatalog.Enabled = true;
            RunLoop.Reset();
            BossEncounter.ResetRun();
            PortalPressure.Reset();
            SpeedRamp.ResetBoost();
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
        moveBackGround.speed = .33f;   // (under the cap, HUD 35)
        PortalPressure.Reset();
        SpeedRamp.ResetBoost();
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

    // WorldManager.ApplyDifficulty: the walls and the spawner get the current
    // world's ramp, the cap and the loop's scaling.
    static void ApplyDifficulty()
    {
        typeof(WorldManager).GetMethod("ApplyDifficulty", BindingFlags.NonPublic | BindingFlags.Static)
            .Invoke(null, new object[] { WorldManager.Current });
    }

    static void RunWhile(BossEncounter e, BossEncounter.Phase phase, float dt = .1f, int budget = 3000)
    {
        for (int i = 0; i < budget && e.State == phase; i++) e.Step(dt, 1f);
    }

    // Level clock out -> the boss, all the way to Done.
    static void PlayBoss(WorldManager wm)
    {
        // Fly the rest of the level in one step, at whatever speed it is (a
        // stock ship arrives at 0 and would never cover the distance in one
        // flat step, so stand in for the ramp's first seconds).
        if (moveBackGround.speed < .04f) moveBackGround.speed = .04f;
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

    // ---- 1. the final portal ---------------------------------------------------

    static void FinalPortalOpensAndWaits()
    {
        FreshScene(Ember, 0);
        var (bg, _) = Board();
        var wm = World(-1f);
        int pauses = score.pauseCounter = 3;
        wm.Tick(.1f);
        Check("Ember's level end starts its boss", BossEncounter.Running && BossEncounter.Instance.Boss.artKey == "Ember");
        Check("no portal during the fight", !wm.PortalIsOpen && Portal.Live == null && wm.Stage == WorldManager.LevelStage.Boss);
        var e = BossEncounter.Instance;
        e.Step(.1f, 1f);
        RunWhile(e, BossEncounter.Phase.Intro);
        RunWhile(e, BossEncounter.Phase.Fight);
        RunWhile(e, BossEncounter.Phase.Outro);
        Check("encounter done", e.State == BossEncounter.Phase.Done);
        Check("after the Ember boss the portal opens, as in every world (stage Portal, no choice)",
              wm.PortalIsOpen && wm.Stage == WorldManager.LevelStage.Portal && Portal.Live != null &&
              Object.FindFirstObjectByType<Portal>() != null);
        Check("... leading back to the run's start world (Space), wearing its colour",
              WorldManager.PortalDestination == 0 && PortalPressure.Destination == 0 && PortalPressure.Active);

        Check("no pause was spent by the boss or the portal", score.pauseCounter == pauses);
        // (the old choice froze time as a scripted freeze; the portal freezes nothing)
        score.pauseCounter = 0;   // no finger in batch mode: let the world run
        Time.timeScale = 1f;
        bg.SendMessage("Update");
        Check("the waiting portal freezes nothing (no scripted freeze, the world runs)",
              !BossEncounter.ScriptedFreeze && Time.timeScale > 0f);
        score.pauseCounter = pauses;
        wm.Tick(5f);
        Check("the level clock stays stopped while the portal waits; its own clock runs (" +
              PortalPressure.Seconds.ToString("F1") + " s)",
              wm.SecondsLeftInWorld == 0f && wm.DistanceLeft == 0f && wm.Stage == WorldManager.LevelStage.Portal &&
              Mathf.Abs(PortalPressure.Seconds - 5f) < 1e-3f);

        // It never goes: ten minutes of flight later it is the same portal, on station.
        var portal = Portal.Live;
        for (int i = 0; i < 600; i++) { portal.Step(1f); wm.Tick(1f); }
        Check("ten minutes on: the same loop portal, still open, on station and in view",
              Portal.Live == portal && portal != null && portal.OnStation && wm.PortalIsOpen &&
              portal.transform.position.y < CameraFit.ViewTop && portal.transform.position.y > CameraFit.ViewBottom &&
              !BossEncounter.Running && WorldManager.CurrentIndex == Ember && RunLoop.Index == 0);

        Check("the choice is gone from the game (no FinalChoicePanel, no KEEP FLYING)",
              !File.Exists("Assets/Scripts/Worlds/FinalChoicePanel.cs") &&
              !File.ReadAllText("Assets/Scripts/Ship/movePlayer.cs").Contains("FinalChoicePanel") &&
              !File.ReadAllText("Assets/Scripts/Worlds/WorldManager.cs").Contains("KeepFlying") &&
              !File.ReadAllText("Assets/Scripts/Bosses/BossEncounter.cs").Contains("FinalChoicePanel"));
        Time.timeScale = 1f;
        foreach (var p in Object.FindObjectsByType<Portal>(FindObjectsSortMode.None)) Object.DestroyImmediate(p.gameObject);
        PortalPressure.Reset();
    }

    // ---- 2. waiting at the final portal -----------------------------------------------

    static void WaitingAtTheFinalPortal()
    {
        FreshScene(Ember, 0);
        var (bg, _) = Board();
        var wm = World(-1f);
        PlayBoss(wm);
        Check("waiting: the loop portal is open", wm.PortalIsOpen && wm.Stage == WorldManager.LevelStage.Portal);
        var theme = WorldManager.Worlds[Ember];
        ApplyDifficulty();
        float dens0 = PortalPressure.DensityScale, loopDensity = LoopDifficulty.DensityScale;

        // The speed cap holds: twenty minutes of the walls' ramp from the boss's 20.
        int frame = 5000;
        SpeedRamp.FrameOverride = () => frame;
        SpeedRamp.DeltaOverride = () => 1f;
        SpeedRamp.ResetFrameGuard();
        moveBackGround.speed = BossConfig.FightSpeed;
        float peak = 0f;
        try
        {
            for (int i = 0; i < 1200; i++)
            {
                frame++;
                bg.SendMessage("Update");
                wm.Tick(1f);
                peak = Mathf.Max(peak, moveBackGround.speed);
            }
        }
        finally
        {
            SpeedRamp.FrameOverride = null;
            SpeedRamp.DeltaOverride = null;
            SpeedRamp.ResetFrameGuard();
        }
        Check("the walls ramp to the one cap, HUD " + SpeedRamp.CapHud + " (peak " + (peak * 100f).ToString("F2") + ")",
              Mathf.Approximately(bg.maxSpeed, SpeedRamp.Cap) && peak <= SpeedRamp.Cap + 1e-6f &&
              Mathf.Approximately(moveBackGround.speed, SpeedRamp.Cap));
        Check("twenty minutes on: still Ember, the portal still open, no boss, the loop index unmoved",
              WorldManager.CurrentIndex == Ember && wm.PortalIsOpen && !BossEncounter.Running && RunLoop.Index == 0 &&
              Object.FindFirstObjectByType<Portal>() != null);
        Check("the pressure climbs instead (spawn rate x" + dens0.ToString("F2") + " -> x" + PortalPressure.DensityScale.ToString("F2") +
              "), the loop's own density untouched", PortalPressure.DensityScale > dens0 && PortalPressure.Pressing &&
              LoopDifficulty.DensityScale == loopDensity);
        Check("Ember's full song keeps looping while it waits (it has a loop-out point)",
              WorldMusic.LoopOutSeconds(WorldMusic.EmberTrack) > 0f && theme.musicResource == WorldMusic.EmberTrack && !theme.progressiveMusic);
        foreach (var p in Object.FindObjectsByType<Portal>(FindObjectsSortMode.None)) Object.DestroyImmediate(p.gameObject);
        PortalPressure.Reset();
        moveBackGround.speed = 0f;
    }

    // ---- 3. through the final portal -----------------------------------------------------

    static void LoopBackGoesToTheStartWorld()
    {
        FreshScene(Ember, 0);
        Board();
        var wm = World(-1f);
        RunScore.Tick(30f, .3f);
        PlayBoss(wm);
        Check("the loop portal opens by itself after the boss", wm.PortalIsOpen && Portal.Live != null &&
              WorldManager.PortalDestination == 0);

        // It never expires: a minute and a half of waiting, it is still there.
        var portal = Portal.Live;
        for (int i = 0; i < 90; i++) { portal.Step(1f); wm.Tick(1f); }
        Check("90 s later the same loop portal is still open (no missed portal, no lap)",
              Portal.Live == portal && wm.PortalIsOpen && WorldManager.CurrentIndex == Ember && !BossEncounter.Running);

        // Fly through it.
        long before = RunScore.Total;
        try { portal.Enter(); }
        catch (System.Exception ex) { Debug.LogWarning("[LOOP] Enter threw (presentation only): " + ex.Message); }
        foreach (var p in Object.FindObjectsByType<Portal>(FindObjectsSortMode.None)) Object.DestroyImmediate(p.gameObject);
        Check("arrives in the run's start world (Space)", WorldManager.CurrentIndex == 0);
        Check("loopIndex 0 -> 1", RunLoop.Index == 1 && RunScore.Parts.loops == 1);
        Check("the score carries over: + Ember's world bonus only (" + before + " -> " + RunScore.Total + ")",
              RunScore.Total == before + ScoreRules.WorldClearedPoints(Ember, 0));
        Check("the pressure is over: neutral dials again", !PortalPressure.Active && PortalPressure.DensityScale == 1f &&
              !PortalPressure.EarningsClosed && wm.Stage == WorldManager.LevelStage.Level);
        Check("arrival speed is the ship's fresh-run start (HUD " + Mathf.RoundToInt(moveBackGround.speed * 100f) + "), under the cap",
              Mathf.Approximately(moveBackGround.speed, WorldManager.ArrivalSpeed(1)) &&
              Mathf.Approximately(WorldManager.ArrivalSpeed(1), WorldManager.RunStartSpeed(0f)) &&
              moveBackGround.speed <= SpeedRamp.Cap);
        Check("the level distance restarted", Mathf.Approximately(wm.DistanceLeft, wm.WorldDistance) && !wm.PortalIsOpen);

        // Space's boss again, then its portal, then Frost.
        PlayBoss(wm);
        Check("Space's boss came round again on the loop", BossEncounter.DoneInWorld(0));
        Check("... and its portal opened, to Frost", wm.PortalIsOpen && WorldManager.PortalDestination == 1);
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
        Check("Ember start: its portal leads back to Ember", wm.PortalIsOpen && WorldManager.PortalDestination == Ember);
        Advance(wm);
        foreach (var p in Object.FindObjectsByType<Portal>(FindObjectsSortMode.None)) Object.DestroyImmediate(p.gameObject);
        Check("Ember start: the loop portal lands in Ember, loop 2", WorldManager.CurrentIndex == Ember && RunLoop.Index == 1);
        long before = RunScore.Total;
        PlayBoss(wm);
        Check("Ember's boss again on the loop, paying x1.5 (" + (RunScore.Total - before) + ")",
              BossEncounter.DoneInWorld(Ember) && RunScore.Total - before >= ScoreRules.BossPoints(false, 0f, false, 1) &&
              ScoreRules.BossPoints(false, 0f, false, 1) == 225);
        Check("... then the same loop portal again, to Ember", wm.PortalIsOpen && wm.Stage == WorldManager.LevelStage.Portal &&
              WorldManager.PortalDestination == Ember);
        foreach (var p in Object.FindObjectsByType<Portal>(FindObjectsSortMode.None)) Object.DestroyImmediate(p.gameObject);
        PortalPressure.Reset();
    }

    // Ember's lift-off (LiftoffDef.AutoLoopNow while Tide is gated off): after its interlude in Space's
    // sky the loop begins at once, no portal -- the same world change as the
    // loop portal's. A run that began in Ember loops back there through its
    // portal, as before.
    static void LoopStartsStraightFromTheLiftoff()
    {
        LiftoffCatalog.Enabled = true;
        try
        {
            FreshScene(Ember, 0);
            Board();
            var wm = World(-1f);
            RunScore.Tick(30f, .3f);
            PlayBoss(wm);
            var l = Liftoff.Live;
            Check("Ember's boss down: the lift-off, no portal", l != null && l.Def.AutoLoopNow && Portal.Live == null && wm.PortalIsOpen);
            long before = RunScore.Total;
            bool noPortal = true;
            int changes = 0, world = WorldManager.CurrentIndex;
            for (int i = 0; i < 1200 && Liftoff.Live != null; i++)
            {
                try { Liftoff.Live.Step(1f / 60f); }
                catch (System.Exception ex) { Debug.LogWarning("[LOOP] lift-off step threw: " + ex.Message); }
                noPortal &= Portal.Live == null && !PortalPressure.Active;
                if (WorldManager.CurrentIndex != world) { changes++; world = WorldManager.CurrentIndex; }
            }
            Check("the lift-off ends straight in Space's level: no portal, no pressure, one world change (" + changes + ")",
                  Liftoff.Live == null && noPortal && Object.FindFirstObjectByType<Portal>() == null && changes == 1 &&
                  WorldManager.CurrentIndex == 0 && wm.Stage == WorldManager.LevelStage.Level && !wm.PortalIsOpen);
            Check("... loopIndex 0 -> 1, exactly once", RunLoop.Index == 1 && RunScore.Parts.loops == 1);
            Check("... + Ember's world bonus only (" + before + " -> " + RunScore.Total + ")",
                  RunScore.Total == before + ScoreRules.WorldClearedPoints(Ember, 0));
            Check("... the loop's arrival speed, the level distance restarted, every boss again",
                  Mathf.Approximately(moveBackGround.speed, WorldManager.ArrivalSpeed(1)) &&
                  Mathf.Approximately(wm.DistanceLeft, wm.WorldDistance) && !BossEncounter.DoneInWorld(Ember));
            wm.Tick(5f);
            Check("... 5 s on: still no portal, still loop 2", Portal.Live == null && RunLoop.Index == 1 && WorldManager.CurrentIndex == 0);
            Object.DestroyImmediate(wm.gameObject);

            // a run begun in Ember: the loop leads back to Ember, so its portal
            FreshScene(Ember, Ember);
            Board();
            wm = World(-1f);
            PlayBoss(wm);
            for (int i = 0; i < 1200 && Liftoff.Live != null; i++)
            {
                try { Liftoff.Live.Step(1f / 60f); }
                catch (System.Exception ex) { Debug.LogWarning("[LOOP] lift-off step threw: " + ex.Message); }
            }
            Check("Ember start: the lift-off ends on the loop portal back to Ember (not straight round)",
                  Liftoff.Live == null && Portal.Live != null && WorldManager.CurrentIndex == Ember && RunLoop.Index == 0 &&
                  WorldManager.PortalDestination == Ember && PortalPressure.Active);
            foreach (var p in Object.FindObjectsByType<Portal>(FindObjectsSortMode.None)) Object.DestroyImmediate(p.gameObject);
            Object.DestroyImmediate(wm.gameObject);
            PortalPressure.Reset();
        }
        finally { LiftoffCatalog.Enabled = false; }
    }

    // ---- 4. per-loop scaling ---------------------------------------------------

    static void LoopScalingAppliesAndCaps()
    {
        Check("loop 0 is the game as it is", LoopRules.ArrivalSpeed(0) == 0f && LoopRules.RampScale(0) == 1f &&
              LoopRules.PhaseRampScale(0) == 1f && LoopRules.DensityScale(0) == 1f &&
              LoopRules.PilotLoadBonus(0) == 0f && LoopRules.ThreatBonus(0) == 0f && LoopRules.TierShift(0) == 0 &&
              LoopRules.ShotBonus(0) == 0 && LoopRules.VolleyGapScale(0) == 1f && LoopRules.ScoreScale(0) == 1f &&
              LoopRules.BossCooldownScale(0) == 1f && LoopRules.BossHeadStart(0) == 0f && LoopRules.BonusScale(0) == 1f);
        bool monotonic = true;
        for (int l = 1; l <= 12; l++)
        {
            monotonic &= LoopRules.ArrivalSpeed(l) >= LoopRules.ArrivalSpeed(l - 1) &&
                         LoopRules.RampScale(l) >= LoopRules.RampScale(l - 1) &&
                         LoopRules.PhaseRampScale(l) >= LoopRules.PhaseRampScale(l - 1) &&
                         LoopRules.DensityScale(l) >= LoopRules.DensityScale(l - 1) &&
                         LoopRules.PilotLoadBonus(l) >= LoopRules.PilotLoadBonus(l - 1) &&
                         LoopRules.ThreatBonus(l) >= LoopRules.ThreatBonus(l - 1) &&
                         LoopRules.TierShift(l) >= LoopRules.TierShift(l - 1) &&
                         LoopRules.ShotBonus(l) >= LoopRules.ShotBonus(l - 1) &&
                         LoopRules.VolleyGapScale(l) <= LoopRules.VolleyGapScale(l - 1) &&
                         LoopRules.ScoreScale(l) >= LoopRules.ScoreScale(l - 1) &&
                         LoopRules.BossCooldownScale(l) <= LoopRules.BossCooldownScale(l - 1) &&
                         LoopRules.BonusScale(l) >= LoopRules.BonusScale(l - 1);
        }
        Check("every loop is at least as hard as the last", monotonic);
        Check("each loop starts harder on every axis but speed: loop 2 > loop 1",
              LoopRules.ArrivalSpeed(1) > 0f && LoopRules.RampScale(1) > 1f &&
              LoopRules.PhaseRampScale(1) > 1f && LoopRules.DensityScale(1) > 1f && LoopRules.BossCooldownScale(1) < 1f &&
              LoopRules.PilotLoadBonus(1) > 0f && LoopRules.ThreatBonus(1) > 0f && LoopRules.TierShift(1) > 0 &&
              LoopRules.ShotBonus(1) > 0 && LoopRules.VolleyGapScale(1) < 1f && LoopRules.ScoreScale(1) > 1f);
        int cap = LoopRules.MaxScaledLoops;
        Check("difficulty stops growing after " + cap + " loops", LoopRules.ArrivalSpeed(50) == LoopRules.ArrivalSpeed(cap) &&
              LoopRules.RampScale(50) == LoopRules.RampScale(cap) && LoopRules.DensityScale(50) == LoopRules.DensityScale(cap) &&
              LoopRules.PhaseRampScale(50) == LoopRules.PhaseRampScale(cap) && LoopRules.BossCooldownScale(50) == LoopRules.BossCooldownScale(cap) &&
              LoopRules.PilotLoadBonus(50) == LoopRules.PilotLoadBonus(cap) && LoopRules.ThreatBonus(50) == LoopRules.ThreatBonus(cap) &&
              LoopRules.TierShift(50) == LoopRules.TierShift(cap) && LoopRules.ShotBonus(50) == LoopRules.ShotBonus(cap) &&
              LoopRules.VolleyGapScale(50) == LoopRules.VolleyGapScale(cap));
        Check("sane caps: arrival <= HUD 12, ramp <= x1.3, density <= x1.3, boss cooldowns >= x0.7, pilot load <= +1.5, " +
              "threats <= +3, tiers <= +2, shots <= +6, volley gap >= x0.7",
              LoopRules.ArrivalSpeed(50) <= .12f + 1e-4f && LoopRules.RampScale(50) <= 1.3f + 1e-4f &&
              LoopRules.DensityScale(50) <= 1.3f + 1e-4f && LoopRules.BossCooldownScale(50) >= .7f - 1e-4f &&
              LoopRules.PilotLoadBonus(50) <= 1.5f + 1e-4f && LoopRules.ThreatBonus(50) <= 3f + 1e-4f &&
              LoopRules.TierShift(50) <= 2 && LoopRules.ShotBonus(50) <= 6 && LoopRules.VolleyGapScale(50) >= .7f - 1e-4f);
        // (was: no loop or endless time pushes a world's cap past HUD 50)
        bool underCap = true;
        for (int l = 0; l <= 50; l++) underCap &= LoopRules.ArrivalSpeed(l) <= SpeedRamp.Cap && WorldManager.ArrivalSpeed(l) <= SpeedRamp.Cap;
        Check("no loop adds speed: every arrival is under the one cap (HUD " + SpeedRamp.CapHud + ")", underCap && SpeedRamp.CapHud == 35);
        Check("bonuses scale gently: x1, x1.5, x2 ... capped at x3",
              LoopRules.BonusScale(1) == 1.5f && LoopRules.BonusScale(2) == 2f && LoopRules.BonusScale(99) == 3f);
        Check("flight / kill points per loop: x1.15, x1.3 ... capped at x1.6",
              Mathf.Approximately(LoopRules.ScoreScale(1), 1.15f) && Mathf.Approximately(LoopRules.ScoreScale(2), 1.3f) &&
              Mathf.Approximately(LoopRules.ScoreScale(99), 1.6f));
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
        Check("loop 2 world: the cap is still HUD " + SpeedRamp.CapHud + " (" + bg.maxSpeed + ")",
              Mathf.Approximately(bg.maxSpeed, SpeedRamp.Cap));
        Check("loop 2 world: phases x" + LoopRules.PhaseRampScale(1) + ", density x" + LoopRules.DensityScale(1),
              Mathf.Approximately(enemies.phaseRampScale, ember.enemyRampScale * LoopRules.PhaseRampScale(1)) &&
              Mathf.Approximately(LoopDifficulty.DensityScale, LoopRules.DensityScale(1)));

        // The loop's other axes, live (measured again on a first pass below).
        bool wasDisabled = EnemyDensity.Disabled;
        EnemyDensity.Disabled = false;
        float pilots1 = EnemyDensity.MaxPilotLoad(35f, Ember), threats1 = EnemyDensity.MaxThreats(35f);
        int shots1 = EnemyThreat.ShotBudget;
        float gap1 = EnemyThreat.Gap;
        int maxTier1 = MaxFighterTier(Ember, 1);
        EnemyDensity.Disabled = wasDisabled;

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

        EnemyDensity.Disabled = false;
        float pilots0 = EnemyDensity.MaxPilotLoad(35f, Ember), threats0 = EnemyDensity.MaxThreats(35f);
        int shots0 = EnemyThreat.ShotBudget;
        float gap0 = EnemyThreat.Gap;
        int maxTier0 = MaxFighterTier(Ember, 1);
        EnemyDensity.Disabled = wasDisabled;
        Check("loop 2 pilot load +" + LoopRules.PilotLoadBonus(1) + " (" + pilots0 + " -> " + pilots1 + ")",
              Mathf.Abs(pilots1 - pilots0 - LoopRules.PilotLoadBonus(1)) < 1e-4f);
        Check("loop 2 threat ceiling +" + LoopRules.ThreatBonus(1) + " bodies x view (" + threats0.ToString("F2") + " -> " +
              threats1.ToString("F2") + ")", Mathf.Abs(threats1 - threats0 - LoopRules.ThreatBonus(1) * EnemyDensity.ViewScale) < 1e-3f);
        Check("loop 2 shot budget +" + LoopRules.ShotBonus(1) + " (" + shots0 + " -> " + shots1 + ")",
              shots0 == EnemyThreat.MaxEnemyShots && shots1 == shots0 + LoopRules.ShotBonus(1));
        Check("loop 2 volley gap x" + LoopRules.VolleyGapScale(1) + " (" + gap0.ToString("F2") + " -> " + gap1.ToString("F2") + " s)",
              Mathf.Approximately(gap0, EnemyThreat.VolleyGap) && Mathf.Approximately(gap1, gap0 * LoopRules.VolleyGapScale(1)));
        Check("loop 2 fighter tiers shift up (phase 1 tops out at tier " + maxTier0 + " -> " + maxTier1 + ")",
              maxTier0 == 1 && maxTier1 == 1 + LoopRules.TierShift(1));
    }

    // The highest fighter tier ChooseExtraDef fields in `phase` on the current loop.
    static int MaxFighterTier(int world, int phase)
    {
        Random.InitState(77);
        int max = 0;
        for (int i = 0; i < 400; i++)
        {
            var d = enmiesOnBoard.ChooseExtraDef(world, phase);
            if (d != null && d.role == EnemyRole.Fighter) max = Mathf.Max(max, d.tier);
        }
        return max;
    }

    // ---- 5. speed multiplier ----------------------------------------------------

    static void SpeedMultiplierTiersAndCap()
    {
        var expect = new (int hud, float m)[]
        {
            // 2026-10, second pass: 20 / 30 / 40 / 46 -> 20 / 30 / 35 (the cap), and
            // anything above the cap (only a limit break gets there) x2.5
            (0, 1f), (19, 1f), (20, 1.25f), (29, 1.25f), (30, 1.5f), (34, 1.5f), (35, 2f), (36, 2.5f), (45, 2.5f), (50, 2.5f), (99, 2.5f),
        };
        foreach (var (hud, m) in expect)
            Check("HUD " + hud + " -> x" + m, Mathf.Approximately(ScoreRules.SpeedMultiplierFor(hud / 100f), m));
        Check("the top natural tier is the cap itself; x2.5 is the limit break's",
              ScoreRules.SpeedTierHud[ScoreRules.SpeedTierHud.Length - 1] == SpeedRamp.CapHud &&
              ScoreRules.LimitBreakMultiplier == 2.5f && ScoreRules.IsLimitBreak(.36f) && !ScoreRules.IsLimitBreak(.35f));
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
        moveBackGround.speed = .32f;
        Check("HUD 32 (x1.5): a rock pays 8 (5 x 1.5, rounded)", Kill(Enemy(rock)) == 8);
        Check("the HUD reads the live tier", Mathf.Approximately(RunScore.SpeedMultiplier, 1.5f));
        RunScore.Tick(ScoreRules.ComboWindowSeconds + .1f, 0f);
        moveBackGround.speed = .45f;   // a full limit break (the cap + the most boost)
        long sum = 0, last = 0;
        for (int i = 0; i < 10; i++) { last = Kill(Enemy(rock)); sum += last; }
        Check("HUD 45 (limit break x2.5), x4 chain: the 10th rock pays 40 (5 x 8 cap, got " + last + ")", last == 40);
        Check("a heavy at the cap pays 320 -- the most any single kill can", Kill(Enemy(heavy)) == 40 * 8);
        Check("the best multiplier is recorded (x8)", Mathf.Approximately(RunScore.Parts.bestMultiplier, 8f));
        long t = RunScore.Total;
        RunScore.Tick(10f, .4f);
        // (2026-10, second pass: above the cap is the limit break, x2.5)
        Check("flight at HUD 40 (limit break) pays 20 x2.5 = 50 over 10s (" + (RunScore.Total - t) + ")",
              RunScore.Total - t == 50 || RunScore.Total - t == 49);
        t = RunScore.Total;
        RunScore.Tick(10f, .35f);
        Check("flight at the cap pays 17.5 x2 = 35 over 10s (" + (RunScore.Total - t) + ")",
              RunScore.Total - t == 35 || RunScore.Total - t == 34);
        t = RunScore.Total;
        RunScore.OnDust(true);
        RunScore.OnBoss(true, 0f, false, Vector3.zero);
        Check("pickups and bosses are not speed-multiplied", RunScore.Total - t == 5 + 300);
        Check("the breakdown still sums to the total", RunScore.Parts.Total == RunScore.Total);

        // A loop pays its flight and kill points x LoopRules.ScoreScale.
        RunScore.EndRun(RunScore.RunId);
        RunScore.BeginRun(true, true);
        t = RunScore.Total;
        RunScore.Tick(1000f, .1f);
        long first = RunScore.Total - t;
        RunScore.OnLoop(RunLoop.Advance());
        t = RunScore.Total;
        RunScore.Tick(1000f, .1f);
        long looped = RunScore.Total - t;
        Check("loop 2: flight pays x" + LoopRules.ScoreScale(1) + " (" + first + " -> " + looped + ")",
              Mathf.Abs(first - 500) <= 1 && Mathf.Abs(looped - first * LoopRules.ScoreScale(1)) <= 1f);
        moveBackGround.speed = .10f;
        RunScore.Tick(ScoreRules.ComboWindowSeconds + .1f, 0f);
        Check("loop 2: a rock at x1 pays 5 x " + LoopRules.ScoreScale(1) + " rounded (6)", Kill(Enemy(rock)) == Mathf.RoundToInt(5 * LoopRules.ScoreScale(1)));
        RunLoop.Reset();
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

        moveBackGround.speed = .32f;
        Step(hud, .01f);
        Check("HUD 32: 'SPD x1.5' ('" + hud.SpeedBadge.text + "')", hud.SpeedBadge.text == "SPD x1.5");
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
                                         dustAtStart = 1f, dustWon = 1f });
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
        Check("the tutorial scene has no world manager (so no portal and no loop)",
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
        Advance(wm);
        foreach (var p in Object.FindObjectsByType<Portal>(FindObjectsSortMode.None)) Object.DestroyImmediate(p.gameObject);
        Check("developer run loops and scores", RunLoop.Index == 1 && RunScore.Total > 0 && RunScore.Scoring);
        RunScore.EndRun(RunScore.RunId);
        Check("developer run: BestScore never written", !PlayerPrefs.HasKey(RunScore.BestScoreKey));
        Check("looping back never lowers the furthest world reached", PlayerPrefs.GetInt(WorldManager.PrefsHighestWorld, 0) == highest);
        foreach (var f in new[] { "Assets/Scripts/Worlds/LoopRules.cs", "Assets/Scripts/Worlds/PortalPressure.cs" })
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
        Check("... straight to the loop portal, back to where the run began",
              wm.PortalIsOpen && WorldManager.PortalDestination == 0);
        Advance(wm);
        foreach (var p in Object.FindObjectsByType<Portal>(FindObjectsSortMode.None)) Object.DestroyImmediate(p.gameObject);
        Check("the loop portal still goes to where the run began (Space)", WorldManager.CurrentIndex == 0 && RunLoop.Index == 1);

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
    // (loop-scaled) ramp on SpeedRamp's curve to the one cap (HUD 35); flight and kills x the
    // loop's score scale; a kill every 4s worth 8 base (a mix of
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
        var s = new Sim();
        const float dt = .05f;
        float nextKill = 4f;
        int killNo = 0;
        float flown = 0f, length = WorldManager.WorldDistanceFor(world);
        for (float t = 0f; flown < length; t += dt)
        {
            speed = Mathf.Min(SpeedRamp.Cap, SpeedRamp.Advance(speed, rate, dt));
            flown += speed * dt;
            float sm = speedMultiplier ? ScoreRules.SpeedMultiplierFor(speed) : 1f;
            float ls = loopScaling ? LoopRules.ScoreScale(L) : 1f;
            s.distance += ScoreRules.DistancePoints(speed, dt) * sm * ls;
            if (t >= nextKill)
            {
                nextKill += 4f;
                int chain = (killNo++ % 3) == 2 ? 2 : 1;
                s.kills += Mathf.RoundToInt(8 * (speedMultiplier ? ScoreRules.Combined(chain, sm) : chain) * ls);
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
            for (int w = 0; w < WorldManager.LiveWorldCount; w++)
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

        // Lingering at the final portal instead of entering it: the grace pays
        // as usual (flight and kills), after it nothing (PortalPressure).
        var ember = WorldManager.Worlds[Ember];
        float speed = BossConfig.FightSpeed, rate = ember.speedRampPerSecond;
        double lingerDist = 0, lingerKills = 0, earnedSeconds = 0;
        int killNo = 0;
        float nextKill = 4f;
        for (float t = 0f; t < 180f; t += .05f)
        {
            speed = Mathf.Min(SpeedRamp.Cap, SpeedRamp.Advance(speed, rate, .05f));
            if (t >= PortalPressure.GraceSeconds) continue;   // earnings closed
            earnedSeconds += .05f;
            float sm = ScoreRules.SpeedMultiplierFor(speed);
            lingerDist += ScoreRules.DistancePoints(speed, .05f) * sm;
            if (t >= nextKill) { nextKill += 4f; int chain = (killNo++ % 3) == 2 ? 2 : 1; lingerKills += Mathf.RoundToInt(8 * ScoreRules.Combined(chain, sm)); }
        }
        var linger = pass[0];
        linger.worlds -= ScoreRules.WorldClearedPoints(Ember, 0);   // not entered: no world bonus
        linger.distance += lingerDist;
        linger.kills += lingerKills;
        Debug.Log("[LOOP] SIM " + Line("first pass + 3 min lingering", linger) +
                  "  (earning for " + earnedSeconds.ToString("F1") + " s of it; speed after 3 min: HUD " + Mathf.RoundToInt(speed * 100f) + ")");

        Check("lingering three minutes at the portal earns less than flying on (" + linger.Total.ToString("0") + " < " +
              (pass[0].Total + SimWorld(0, 1, true, true, false).Total * .5).ToString("0") + ")",
              linger.Total < pass[0].Total + SimWorld(0, 1, true, true, false).Total * .5 &&
              lingerDist + lingerKills < SimWorld(0, 1, true, true, false).Total * .1);
        Check("simulated Space level + boss stays in the low thousands (" + space.Total.ToString("0") + ")",
              space.Total > 800 && space.Total < 2000);
        Check("the speed multiplier raises a pass by a fair amount, not a landslide (" +
              (pass[0].Total / oldPass.Total).ToString("F2") + "x)",
              pass[0].Total > oldPass.Total && pass[0].Total < oldPass.Total * 1.8);
        Check("a loop is worth more than the first pass (" + pass[1].Total.ToString("0") + " > " + pass[0].Total.ToString("0") + ")",
              pass[1].Total > pass[0].Total);
    }

    // ---- 11. every loop ends the same way ---------------------------------------------------

    static void FinalPortalEveryLoop()
    {
        // Started in Ember: its portal leads back to Ember, every loop, the same way.
        FreshScene(Ember, Ember);
        var (bg, enemies) = Board();
        var wm = World(-1f);
        RunScore.Tick(30f, .3f);
        PlayBoss(wm);
        Check("loop 1 ends in the loop portal", wm.PortalIsOpen && wm.Stage == WorldManager.LevelStage.Portal &&
              WorldManager.PortalDestination == Ember);
        Advance(wm);
        foreach (var p in Object.FindObjectsByType<Portal>(FindObjectsSortMode.None)) Object.DestroyImmediate(p.gameObject);
        Check("LOOP 2 in Ember", WorldManager.CurrentIndex == Ember && RunLoop.Index == 1);
        PlayBoss(wm);
        Check("the end of loop 2: the same loop portal, no prompt, no second lap",
              wm.PortalIsOpen && wm.Stage == WorldManager.LevelStage.Portal && WorldManager.PortalDestination == Ember &&
              PortalPressure.Active && PortalPressure.Destination == Ember);
        // waiting a while changes nothing about where it leads
        for (int i = 0; i < 120; i++) wm.Tick(1f);
        long b2 = RunScore.Total;
        Advance(wm);
        foreach (var p in Object.FindObjectsByType<Portal>(FindObjectsSortMode.None)) Object.DestroyImmediate(p.gameObject);
        var ember = WorldManager.Worlds[Ember];
        Check("LOOP 3 in Ember, + Ember's loop-2 world bonus once (" + (RunScore.Total - b2) + ")",
              WorldManager.CurrentIndex == Ember && RunLoop.Index == 2 && RunScore.Parts.loops == 2 &&
              RunScore.Total - b2 == ScoreRules.WorldClearedPoints(Ember, 1));
        Check("loop 3 is scaled as loop 3 (ramp x" + LoopRules.RampScale(2) + ", phases x" + LoopRules.PhaseRampScale(2) +
              ", density x" + LoopRules.DensityScale(2) + ", arrival HUD " + Mathf.RoundToInt(WorldManager.ArrivalSpeed(2) * 100f) + ")",
              Mathf.Approximately(bg.speedRampPerSecond, ember.speedRampPerSecond * LoopRules.RampScale(2)) &&
              Mathf.Approximately(bg.maxSpeed, SpeedRamp.Cap) &&
              Mathf.Approximately(enemies.phaseRampScale, ember.enemyRampScale * LoopRules.PhaseRampScale(2)) &&
              Mathf.Approximately(LoopDifficulty.DensityScale, LoopRules.DensityScale(2)) &&
              Mathf.Approximately(moveBackGround.speed, WorldManager.ArrivalSpeed(2)));
        Check("the pressure of the wait is gone on arrival", !PortalPressure.Active && PortalPressure.DensityScale == 1f &&
              PortalPressure.ShipGapScale == 1f);
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
            LoopDifficulty.DensityScale = LoopRules.DensityScale(l);
            Check("loop " + (l + 1) + ": x" + LoopRules.DensityScale(l).ToString("F2"),
                  Mathf.Abs(baseDelay / RollAt(enemies, range) - LoopRules.DensityScale(l)) < 1e-3f);
        }

        // A waiting portal: its pressure on top of the loop's own, without a plateau.
        FreshScene(Ember, 0);
        (_, enemies) = Board();
        wm = World(-1f);
        PlayBoss(wm);
        Check("the portal waits", wm.PortalIsOpen && PortalPressure.Active);
        wm.Tick(4f);
        Check("in the grace the board is thinner: delays x1/" + PortalPressure.GraceDensity + " (" +
              (baseDelay / RollAt(enemies, range)).ToString("F2") + ")",
              Mathf.Abs(baseDelay / RollAt(enemies, range) - PortalPressure.GraceDensity) < 1e-3f);
        wm.Tick(57f);
        float want = PortalPressure.DensityAt(61f);
        Check("a minute in: delays shrink by x" + want.ToString("F2") + " (" + (baseDelay / RollAt(enemies, range)).ToString("F2") + ")",
              want > 2f && Mathf.Abs(baseDelay / RollAt(enemies, range) - want) < 1e-3f);
        float atMinute = baseDelay / RollAt(enemies, range);
        for (int i = 0; i < 600; i++) wm.Tick(1f);
        float atEleven = baseDelay / RollAt(enemies, range);
        Check("eleven minutes in: still climbing, no plateau (x" + atMinute.ToString("F2") + " -> x" + atEleven.ToString("F2") + ")",
              atEleven > atMinute * 3f && Mathf.Abs(atEleven - PortalPressure.DensityAt(661f)) < 1e-2f);
        foreach (var p in Object.FindObjectsByType<Portal>(FindObjectsSortMode.None)) Object.DestroyImmediate(p.gameObject);
        PortalPressure.Reset();
        Check("the spawner reads the pressure too", File.ReadAllText("Assets/Scripts/Gameplay/Spawning/enmiesOnBoard.cs")
              .Contains("EnemyDensity.RateScale(EnemyDensity.Hud) * PortalPressure.DensityScale"));
        Check("the spawner reads it (enmiesOnBoard.Roll)", File.ReadAllText("Assets/Scripts/Gameplay/Spawning/enmiesOnBoard.cs")
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

    // The heaviest density the loops can reach (loop 4+) on top of the
    // spawner's own x3 final stretch, over a 240s run in every world: no row
    // of the board is ever closed (SpawnLane.ShipGap). (Only a portal kept
    // waiting deep into overdrive may shrink the gap: PortalPressure, by
    // design, tested in OpenPortalTest.)
    static void GapGuardHoldsAtMaxDensity()
    {
        EditorSceneLoader.Open("gameS1", OpenSceneMode.Single);
        var wmGo = new GameObject("~LoopDensityWorlds");
        SetWorldManager(wmGo.AddComponent<WorldManager>());
        var spawn = typeof(enmiesOnBoard).GetMethod("spawn", Inst, null, new[] { typeof(float) }, null);
        var select = typeof(enmiesOnBoard).GetMethod("SelectPhase", Inst);
        var elapsed = typeof(enmiesOnBoard).GetField("elapsedFlightSeconds", Inst);
        float max = LoopRules.DensityScale(LoopRules.MaxScaledLoops);
        PortalPressure.Reset();
        const float dt = .1f, scroll = 3f, runSeconds = 240f;
        float gap = SpawnLane.ShipGap;
        try
        {
            for (int w = 0; w < WorldManager.LiveWorldCount; w++)
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
                        foreach (var id in Object.FindObjectsByType<EnemyIdentity>(FindObjectsSortMode.None))
                            if (seen.Add(id) && id.Def != null) counts[pass]++;
                        if (pass == 1)
                            for (float y = -3f; y <= 1.5f; y += .25f)
                            {
                                float widest = SpawnLane.WidestGap(SpawnLane.RowSpans(y, y + gap));
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
