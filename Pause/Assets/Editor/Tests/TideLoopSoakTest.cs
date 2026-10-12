using System.Collections.Generic;
using System.Reflection;
using UnityEditor.SceneManagement;
using UnityEngine;

// The whole world chain, walked headless through the real transitions
// (WorldManager, Planetfall, Liftoff, Portal, PortalPressure, RunLoop), over
// three loops, and for a run that began elsewhere (a developer start).
//
//   Space -> Frost -> Verdant -> Ember -> Tide -> Storm -> Desert -> (lift-off) Space, loop + 1
//   a run started in world N goes on to Desert, then (lift-off, no portal) to SPACE, and on
//   through the chain again: the loop always restarts at Space, never at N
//
// Every arrival must be clean: the level stage, no transition in progress, no
// portal or pressure left, the arrival speed of the loop, the world's own
// roster, a calm window re-armed (arrivalKey), and the run's score, hearts and
// pauses carried. 
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
            // the whole chain, three loops: Space -> Frost -> Verdant -> Ember -> Tide -> Storm -> Desert -> (lift-off) Space, loop + 1
            Chain(0, new[] { 0, 1, 2, 3, 4, 5, 6, 0, 1, 2, 3, 4, 5, 6, 0, 1, 2, 3, 4, 5, 6, 0 });
            // a run that began elsewhere (a developer start or a replay) goes on to Desert, then to Space, then the chain again
            Chain(3, new[] { 3, 4, 5, 6, 0, 1, 2, 3, 4, 5, 6, 0 });
            Chain(4, new[] { 4, 5, 6, 0, 1, 2, 3, 4, 5, 6, 0 });
            Chain(5, new[] { 5, 6, 0, 1, 2, 3, 4, 5, 6, 0 });
            Chain(6, new[] { 6, 0, 1, 2, 3, 4, 5, 6, 0, 1 });
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

    // ---- fixtures (LiftoffTest's) ------------------------------------------------

    static void FreshScene(int startWorld)
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        BossEncounter.ResetRun();
        RunLoop.Reset();
        PortalPressure.Reset();
        // the scene's background music source (WorldMusic only plays, and marks the codex song heard, with one)
        new GameObject("MovingMusic", typeof(AudioSource));
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
    static void Chain(int start, int[] expect)
    {
        string label = "run begun in " + WorldManager.Worlds[start].displayName;
        FreshScene(start);
        RunScore.BeginRun(true, true);
        RunScore.Tick(10f, .3f);
        var ship = Ship();
        collisionDetection.lifeCounter = 1;
        var wm = World();
        var visited = new List<int> { WorldManager.CurrentIndex };
        bool tidy = true, clean = true, speedOk = true, rosterOk = true, scoreOk = true, calmOk = true, noStray = true;
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
            // the cast the spawner draws from IS that world's own (Tide: tide_*, never an Ember stand-in), in every role
            foreach (EnemyRole role in System.Enum.GetValues(typeof(EnemyRole)))
            {
                var cast = EnemyRoster.For(EnemyRoster.CurrentWorld, role);
                rosterOk &= cast.Count > 0;
                foreach (var d in cast) rosterOk &= d.world == now && d.key.StartsWith(EnemyRoster.WorldKeys[now] + "_", System.StringComparison.Ordinal);
            }
            scoreOk &= RunScore.Total >= scoreWas && collisionDetection.lifeCounter == 1;
            scoreWas = RunScore.Total;
            calmOk &= !SpaceDirector.Quiet;
            // nothing leaked across the transition: one backdrop on the arrival world's own set, no lift-off / planetfall
            // stage left behind, the clock running, the pilot's state sane
            var bd = WorldBackdrop.Instance;
            bool sky = Object.FindObjectsByType<WorldBackdrop>(FindObjectsSortMode.None).Length == 1 && bd != null && bd.Current != null &&
                       bd.Current.Spec.world == BackdropCatalog.For(WorldManager.Worlds[now].displayName).world;
            bool leftovers = GameObject.Find("~LiftoffStage") != null || GameObject.Find("~Liftoff") != null ||
                             GameObject.Find("~PlanetfallStage") != null;
            bool hooks = Codex.IsDiscovered(Codex.WorldId(now)) && Codex.IsTrackHeard(CodexMusic.WorldId(WorldManager.Worlds[now].displayName));
            if (!sky || leftovers || Time.timeScale != 1f || buttonClicks.playerDied || !hooks)
            { tidy = false; why += " [leak at " + from + "->" + now + ": sky " + sky + " leftovers " + leftovers + " timeScale " + Time.timeScale + " hooks " + hooks + "]"; }
            noStray &= Object.FindObjectsByType<Portal>(FindObjectsSortMode.None).Length == 0;
            Check("   " + label + ": " + WorldManager.Worlds[from].displayName + " -> " + WorldManager.Worlds[now].displayName +
                  " (loop " + (RunLoop.Index + 1) + ", " + (now == expect[visited.Count - 1] ? "as expected" : "EXPECTED " + WorldManager.Worlds[expect[visited.Count - 1]].displayName) + ")",
                  now == expect[visited.Count - 1]);
            if (now != expect[visited.Count - 1]) break;
        }

        Check(label + ": the order is " + Names(expect) + " (walked " + Names(visited.ToArray()) + ")", Same(visited, expect));
        Check(label + ": every arrival clean: level stage, no transition, no portal or pressure" + why, clean && noStray);
        Check(label + ": every arrival leak-free: one backdrop of the arrival world, no stage left, timeScale 1, codex marks and song heard" + why, tidy);
        Check(label + ": every arrival at the loop's arrival speed, on the right world's roster, no calm left on", speedOk && rosterOk && calmOk);
        Check(label + ": score never lost, hearts untouched through " + loopsSeen + " loop(s)", scoreOk);
        int expectLoops = 0;
        for (int i = 1; i < expect.Length; i++) if (expect[i] == WorldManager.LoopWorld && expect[i - 1] == LastOf(start)) expectLoops++;
        Check(label + ": " + expectLoops + " loop(s) counted exactly (RunLoop.Index " + RunLoop.Index + ")", RunLoop.Index == expectLoops && loopsSeen == expectLoops);
        Check(label + ": Desert is reached", visited.Contains(6));

        Cleanup();
        Object.DestroyImmediate(ship.gameObject);
        Object.DestroyImmediate(wm.gameObject);
    }

    // The world after which the chain loops (the last world, Desert).
    static int LastOf(int start) { return 6; }

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
