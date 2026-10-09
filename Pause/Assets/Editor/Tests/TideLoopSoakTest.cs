using System.Collections.Generic;
using System.Reflection;
using UnityEditor.SceneManagement;
using UnityEngine;

// The whole world chain, walked headless through the real transitions
// (WorldManager, Planetfall, Liftoff, Portal, PortalPressure, RunLoop), with
// the Tide release switch (WorldManager.TideEnabled) OFF and ON, over three
// loops, and for a run that began elsewhere (a developer start in Tide).
//
//   OFF (production today)   Space -> Frost -> Verdant -> Ember -> (lift-off) Space, loop + 1, again and again
//   ON  (the release)        Space -> Frost -> Verdant -> Ember -> Tide -> (lift-off) Space, loop + 1
//   Tide start               Tide -> (lift-off) -> the loop PORTAL back to Tide (the interlude shows Space, the loop leads elsewhere)
//
// Every arrival must be clean: the level stage, no transition in progress, no
// portal or pressure left, the arrival speed of the loop, the world's own
// roster, a calm window re-armed (arrivalKey), and the run's score, hearts and
// pauses carried. One suite so that flipping the switch is one tested step.
public static class TideLoopSoakTest
{
    static int fails;
    static void Check(string what, bool ok)
    {
        Debug.Log((ok ? "[TIDESOAK] PASS  " : "[TIDESOAK] FAIL  ") + what);
        if (!ok) fails++;
    }

    public static void Run() { TestHarness.Exit(Execute()); }

    const BindingFlags Inst = BindingFlags.NonPublic | BindingFlags.Instance;
    const BindingFlags Stat = BindingFlags.NonPublic | BindingFlags.Static;
    const float Dt = 1f / 60f;
    static int frame;

    public static int Execute()
    {
        fails = 0;
        using var sandbox = new TestHarness.Sandbox();
        try
        {
            ReleaseSwitchIsOffInTheShippedCode();
            Chain(false, 0, new[] { 0, 1, 2, 3, 0, 1, 2, 3, 0, 1, 2, 3, 0 });
            Chain(true, 0, new[] { 0, 1, 2, 3, 4, 0, 1, 2, 3, 4, 0, 1, 2, 3, 4, 0 });
            // a run that began in Tide: its loop leads back to Tide, through the portal
            Chain(true, 4, new[] { 4, 4, 4, 4 });
            Chain(false, 4, new[] { 4, 4, 4 });
            // a run that began in Ember: with the switch on it goes on to Tide, then loops back to Ember by portal
            Chain(true, 3, new[] { 3, 4, 3, 4, 3 });
            Chain(false, 3, new[] { 3, 3, 3 });
        }
        finally
        {
            Cleanup();
        }
        Debug.Log("[TIDESOAK] failures: " + fails);
        return fails;
    }

    static void Cleanup()
    {
        WorldManager.TideEnabled = false;
        if (Liftoff.Live != null) Object.DestroyImmediate(Liftoff.Live.gameObject);
        if (Planetfall.Live != null) Object.DestroyImmediate(Planetfall.Live.gameObject);
        foreach (var p in Object.FindObjectsByType<Portal>(FindObjectsSortMode.None)) Object.DestroyImmediate(p.gameObject);
        if (WorldBackdrop.Instance != null) Object.DestroyImmediate(WorldBackdrop.Instance.gameObject);
        PlanetfallCatalog.Enabled = true;
        LiftoffCatalog.Enabled = true;
        PortalPressure.Reset();
        ShipStartSpeed.EquippedHudOverride = null;
        SpeedRamp.FrameOverride = null;
        SpeedRamp.DeltaOverride = null;
        SpeedRamp.ResetFrameGuard();
        SpeedRamp.ResetBoost();
        ResumeSlowMo.ClockOverride = null;
        ResumeSlowMo.ResetRun();
        BossEncounter.ResetRun();
        RunLoop.Reset();
        buttonClicks.playerDied = false;
        score.pauseCounter = 0;
        moveBackGround.speed = 0f;
    }

    static void ReleaseSwitchIsOffInTheShippedCode()
    {
        Check("the shipped code has the Tide release switch OFF (flip WorldManager.TideEnabled's initial value to release)",
              !WorldManager.TideEnabled);
    }

    // ---- fixtures (LiftoffTest's) ------------------------------------------------

    static void FreshScene(int startWorld)
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        BossEncounter.ResetRun();
        RunLoop.Reset();
        PortalPressure.Reset();
        var camGo = new GameObject("Main Camera", typeof(Camera));
        camGo.tag = "MainCamera";
        var cam = camGo.GetComponent<Camera>();
        cam.orthographic = true;
        cam.aspect = 1080f / 2340f;
        cam.orthographicSize = CameraFit.ComputeSize(5f, CameraFit.GameplayHalfWidth, 1080, 2340);
        camGo.transform.position = new Vector3(0f, 0f, -10f);
        buttonClicks.playerDied = false;
        startMenu.youAreInTutorial = false;
        score.pauseCounter = 0;
        Time.timeScale = 1f;
        PlayerPrefs.SetInt(DeveloperUnlocks.EnabledKey, 0);
        PlayerPrefs.SetInt(WorldManager.PrefsCurrentWorld, startWorld);
        ShipStartSpeed.EquippedHudOverride = () => ShipStartSpeed.StockHud;
        frame = 90000;
        SpeedRamp.FrameOverride = () => frame;
        SpeedRamp.DeltaOverride = () => Dt;
        SpeedRamp.ResetFrameGuard();
        SpeedRamp.ResetBoost();
        ResumeSlowMo.ClockOverride = () => frame * Dt;
        ResumeSlowMo.ResetRun();
        RunLoop.StartWorld = startWorld;
    }

    static WorldManager World()
    {
        var wm = new GameObject("~WorldManager").AddComponent<WorldManager>();
        wm.SendMessage("Awake");
        typeof(WorldManager).GetField("distanceLeft", Inst).SetValue(wm, wm.WorldDistance);
        typeof(WorldManager).GetField("levelBegun", Inst).SetValue(wm, true);
        return wm;
    }

    static Transform Ship()
    {
        var go = new GameObject("~Ship");
        go.AddComponent<movePlayer>();
        go.AddComponent<SpriteRenderer>().sortingOrder = 0;
        var child = new GameObject("Plume");
        child.transform.SetParent(go.transform, false);
        child.AddComponent<SpriteRenderer>().sortingOrder = -1;
        go.transform.position = new Vector3(-1f, -3f, 0f);
        return go.transform;
    }

    // The level flown and its boss dealt with: the real EndLevel path.
    static void FinishLevel(WorldManager wm)
    {
        typeof(BossEncounter).GetField("doneWorld", Stat).SetValue(null, WorldManager.CurrentIndex);
        typeof(WorldManager).GetField("distanceLeft", Inst).SetValue(wm, 0f);
        wm.EndLevel();
    }

    // One frame the way the game runs it: only while Flying.
    static void Fly(WorldManager wm)
    {
        frame++;
        if (!WorldManager.Flying) return;
        wm.Tick(Dt);
        if (Liftoff.Live != null && Liftoff.Live.State != Liftoff.Stage.Done) Liftoff.Live.Step(Dt);
        if (Planetfall.Live != null && Planetfall.Live.State != Planetfall.Stage.Done) Planetfall.Live.Step(Dt);
    }

    // ---- the walk ---------------------------------------------------------------

    // From the world the run starts in, end each level and follow whatever the
    // game opens (lift-off, planetfall, portal) to the next level; record the
    // worlds in order and check every arrival.
    static void Chain(bool tideOn, int start, int[] expect)
    {
        WorldManager.TideEnabled = tideOn;
        string label = (tideOn ? "switch ON" : "switch OFF") + ", run begun in " + WorldManager.Worlds[start].displayName;
        FreshScene(start);
        RunScore.BeginRun(true, true);
        RunLoop.StartWorld = start;   // (BeginRun resets it)
        RunScore.Tick(10f, .3f);
        var ship = Ship();
        collisionDetection.lifeCounter = 1;
        var wm = World();
        var visited = new List<int> { WorldManager.CurrentIndex };
        bool clean = true, speedOk = true, rosterOk = true, scoreOk = true, calmOk = true, noStray = true;
        string why = "";
        long scoreWas = RunScore.Total;
        int safety = 0;
        int loopsSeen = 0;

        while (visited.Count < expect.Length && safety++ < 40)
        {
            int from = WorldManager.CurrentIndex, loopBefore = RunLoop.Index;
            FinishLevel(wm);
            bool committed = false;
            for (int i = 0; i < (int)(60f / Dt); i++)
            {
                Fly(wm);
                var p = Planetfall.Live;
                if (p != null && p.OnStation && !committed) committed = p.Commit(ship);
                if (Portal.Live != null && Liftoff.Live == null && Planetfall.Live == null) { Portal.Live.Enter(); break; }
                if (Liftoff.Live == null && Planetfall.Live == null && Portal.Live == null && wm.Stage == WorldManager.LevelStage.Level) break;
            }

            int now = WorldManager.CurrentIndex;
            visited.Add(now);
            if (RunLoop.Index > loopBefore) loopsSeen++;

            // a clean arrival
            bool arrivedClean = wm.Stage == WorldManager.LevelStage.Level && !wm.PortalIsOpen && !WorldTransition.InProgress &&
                                Portal.Live == null && Planetfall.Live == null && Liftoff.Live == null && !PortalPressure.Active &&
                                !Planetfall.HoldsShip && !Liftoff.HoldsShip && !Liftoff.SuspendsSpawning && !Planetfall.SuspendsSpawning;
            if (!arrivedClean) { clean = false; why += " [unclean at " + from + "->" + now + "]"; }
            speedOk &= Mathf.Approximately(SpeedRamp.Natural, WorldManager.ArrivalSpeed(RunLoop.Index));
            rosterOk &= EnemyRoster.CurrentWorld == now && WorldManager.Current == WorldManager.Worlds[now];
            scoreOk &= RunScore.Total >= scoreWas && collisionDetection.lifeCounter == 1;
            scoreWas = RunScore.Total;
            calmOk &= !SpaceDirector.Quiet;
            noStray &= Object.FindObjectsByType<Portal>(FindObjectsSortMode.None).Length == 0;
            Check("   " + label + ": " + WorldManager.Worlds[from].displayName + " -> " + WorldManager.Worlds[now].displayName +
                  " (loop " + (RunLoop.Index + 1) + ", " + (now == expect[visited.Count - 1] ? "as expected" : "EXPECTED " + WorldManager.Worlds[expect[visited.Count - 1]].displayName) + ")",
                  now == expect[visited.Count - 1]);
            if (now != expect[visited.Count - 1]) break;
        }

        Check(label + ": the order is " + Names(expect) + " (walked " + Names(visited.ToArray()) + ")", Same(visited, expect));
        Check(label + ": every arrival clean: level stage, no transition, no portal or pressure" + why, clean && noStray);
        Check(label + ": every arrival at the loop's arrival speed, on the right world's roster, no calm left on", speedOk && rosterOk && calmOk);
        Check(label + ": score never lost, hearts untouched through " + loopsSeen + " loop(s)", scoreOk);
        int expectLoops = 0;
        for (int i = 1; i < expect.Length; i++) if (expect[i] == start && expect[i - 1] >= LastOf(tideOn, start)) expectLoops++;
        Check(label + ": " + expectLoops + " loop(s) counted exactly (RunLoop.Index " + RunLoop.Index + ")", RunLoop.Index == expectLoops && loopsSeen == expectLoops);
        if (!tideOn && start != 4)
            Check(label + ": Tide never visited with the switch off", !visited.Contains(4));

        Cleanup();
        Object.DestroyImmediate(ship.gameObject);
        Object.DestroyImmediate(wm.gameObject);
    }

    // The world after which the chain loops (the last live one) for this setup.
    static int LastOf(bool tideOn, int start)
    {
        if (start == 4) return 4;
        return tideOn ? 4 : 3;
    }

    static bool Same(List<int> a, int[] b)
    {
        if (a.Count != b.Length) return false;
        for (int i = 0; i < b.Length; i++) if (a[i] != b[i]) return false;
        return true;
    }

    static string Names(int[] worlds)
    {
        var names = new List<string>();
        foreach (int w in worlds) names.Add(WorldManager.Worlds[w].displayName);
        return string.Join(" > ", names);
    }
}
