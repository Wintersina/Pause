using System.Reflection;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

// REPLAY (buttonClicks.replay): death -> Replay -> a fresh run.
//
//   1  Replay flies the replayed run's own start world, not the furthest
//      planet that run reached: after a Space -> Frost planetfall (Space
//      again), after a portal (Verdant -> Ember: Verdant again), after a loop
//      (Ember start: Ember, loop 1 again), twice in a row, and with the
//      developer's start-world pick
//   2  leaving for the menu drops the pin: menu PLAY keeps the furthest-planet
//      rule
//   3  the replayed run is clean: clock running, pilot alive, no portal /
//      planetfall / lift-off, no transition (the charge freeze is off), no
//      pressure, no boss, loop 0
//   4  the top-right replay / home quick actions run their action once per
//      tap (no persistent onClick copied from the scene buttons), so a tap
//      loads the scene once
//
// The reload itself can't run in edit mode; it is simulated in the order
// Unity runs it: the old scene goes (NewScene), the new scene's score.Awake
// begins the run (RunScore.BeginRun, which resets RunLoop), then
// WorldManager.Start picks the world.
public static class ReplayTest
{
    static int fails;
    static void Check(string what, bool ok)
    {
        Debug.Log((ok ? "[REPLAY] PASS  " : "[REPLAY] FAIL  ") + what);
        if (!ok) fails++;
    }

    public static void Run() { TestHarness.Exit(Execute()); }

    const BindingFlags Inst = BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance;
    const BindingFlags Stat = BindingFlags.NonPublic | BindingFlags.Static;
    const int Space = 0, Frost = 1, Verdant = 2, Ember = 3;

    public static int Execute()
    {
        fails = 0;
        using var sandbox = new TestHarness.Sandbox();
        try
        {
            AfterPlanetfall();
            AfterPortal();
            AfterLoop();
            DeveloperPick();
            DeveloperMatrix();
            MenuDropsThePin();
            QuickActionsFireOnce();
        }
        finally
        {
            Gone();
            WorldManager.ClearReplayWorld();
            RunLoop.Reset();
            BossEncounter.ResetRun();
            PortalPressure.Reset();
            SpeedRamp.ResetBoost();
            ShipStartSpeed.EquippedHudOverride = null;
            buttonClicks.playerDied = false;
            moveBackGround.speed = 0f;
            Time.timeScale = 1f;
        }
        Debug.Log("[REPLAY] failures: " + fails);
        return fails;
    }

    // ---- fixtures -----------------------------------------------------------

    // A run begun from the menu: no replay pin (GameStateReset drops it).
    static void Prefs(int highest, bool developer = false, int pick = -1)
    {
        WorldManager.ClearReplayWorld();
        PlayerPrefs.SetString("HasDoneTut", "true");
        PlayerPrefs.SetInt(DeveloperUnlocks.EnabledKey, developer ? 1 : 0);
        PlayerPrefs.SetInt(WorldManager.PrefsHighestWorld, highest);
        PlayerPrefs.SetInt(WorldManager.PrefsCurrentWorld, highest);
        if (pick >= 0) PlayerPrefs.SetInt(DeveloperUnlocks.SelectedWorldKey, pick);
        else PlayerPrefs.DeleteKey(DeveloperUnlocks.SelectedWorldKey);
        PlayerPrefs.DeleteKey(RunScore.BestScoreKey);
    }

    // A run's scene loading: the old scene gone, score.Awake, then
    // WorldManager.Start. Returns the live manager.
    static WorldManager Load(bool startAtHighest = true)
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        BossEncounter.ResetRun();
        var camGo = new GameObject("Main Camera", typeof(Camera));
        camGo.tag = "MainCamera";
        var cam = camGo.GetComponent<Camera>();
        cam.orthographic = true;
        cam.aspect = 1080f / 2340f;
        cam.orthographicSize = CameraFit.ComputeSize(5f, CameraFit.GameplayHalfWidth, 1080, 2340);
        camGo.transform.position = new Vector3(0f, 0f, -10f);
        score.pauseCounter = 0;
        ShipStartSpeed.EquippedHudOverride = () => ShipStartSpeed.StockHud;

        // score.Awake: a new run (resets RunLoop, StartWorld included).
        RunScore.EndRun(RunScore.RunId);
        RunScore.BeginRun(true, true);

        var wm = new GameObject("~WorldManager").AddComponent<WorldManager>();
        wm.startAtHighestUnlocked = startAtHighest;
        wm.SendMessage("Awake");
        // Start picks the world and the run's start first, then dresses the
        // scene (backdrop, music, banner); only the former matters here.
        try { typeof(WorldManager).GetMethod("Start", Inst).Invoke(wm, null); }
        catch (System.Exception e) { Debug.Log("[REPLAY] (Start's presentation threw in edit mode: " + (e.InnerException ?? e).Message + ")"); }
        Gone();
        return wm;
    }

    static void Gone()
    {
        if (Liftoff.Live != null) Object.DestroyImmediate(Liftoff.Live.gameObject);
        if (Planetfall.Live != null) Object.DestroyImmediate(Planetfall.Live.gameObject);
        foreach (var p in Object.FindObjectsByType<Portal>(FindObjectsSortMode.None)) Object.DestroyImmediate(p.gameObject);
    }

    // The level flown and the boss dealt with: the real EndLevel path opens
    // the way on (portal, planetfall or lift-off).
    static void FinishLevel(WorldManager wm)
    {
        typeof(BossEncounter).GetField("doneWorld", Stat).SetValue(null, WorldManager.CurrentIndex);
        typeof(WorldManager).GetField("distanceLeft", Inst).SetValue(wm, 0f);
        wm.EndLevel();
    }

    // Through the way on, as the portal / the planetfall's switch do.
    static void Through(WorldManager wm)
    {
        FinishLevel(wm);
        wm.Advance(false);
        Gone();
    }

    // The pilot dies (moveBackGround parks the clock), the panel's Replay.
    static void DieAndReplay()
    {
        buttonClicks.playerDied = true;
        Time.timeScale = 0f;
        buttonClicks.PrepareReplay();
    }

    static void Clean(string label, WorldManager wm)
    {
        Check(label + ": clock running, pilot alive", Time.timeScale == 1f && !buttonClicks.playerDied && !startMenu.playerDied);
        Check(label + ": no portal / planetfall / lift-off, no transition (charge not frozen)",
              !wm.PortalIsOpen && Planetfall.Live == null && Liftoff.Live == null && Portal.Live == null &&
              !WorldTransition.InProgress);
        Check(label + ": no pressure, no boss, level stage, full distance",
              !PortalPressure.Active && !BossEncounter.Running && wm.Stage == WorldManager.LevelStage.Level &&
              Mathf.Approximately(wm.DistanceLeft, wm.WorldDistance));
        Check(label + ": loop 0, no loop difficulty", RunLoop.Index == 0 && LoopDifficulty.DensityScale == 1f);
    }

    // ---- 1 + 3 ----------------------------------------------------------------

    static void AfterPlanetfall()
    {
        Prefs(Space);
        var wm = Load();
        Check("a first run starts in Space", WorldManager.CurrentIndex == Space && RunLoop.StartWorld == Space);

        // Space's level ends in the Frost planetfall (a live transition).
        FinishLevel(wm);
        bool planetfall = Planetfall.Live != null;
        Check("Space's way on is the Frost planetfall, a world transition", planetfall && WorldTransition.InProgress);
        // The descent's switch (Planetfall.Switch -> Advance(false)).
        wm.Advance(false);
        Check("the planetfall lands in Frost and unlocks it",
              WorldManager.CurrentIndex == Frost && PlayerPrefs.GetInt(WorldManager.PrefsHighestWorld) == Frost);

        // Dies mid-descent (the planetfall still live) and replays.
        DieAndReplay();
        Check("Replay pins the run's start world (Space)", WorldManager.PinnedReplayWorld == Space);
        wm = Load();
        Check("after a Space -> Frost planetfall, Replay starts in Space again (was: Frost, the furthest planet) " +
              "[" + WorldManager.Worlds[WorldManager.CurrentIndex].displayName + "]",
              WorldManager.CurrentIndex == Space && RunLoop.StartWorld == Space);
        Check("Frost stays unlocked", PlayerPrefs.GetInt(WorldManager.PrefsHighestWorld) == Frost);
        Clean("replay after a planetfall", wm);

        // And again: the replayed run is a Space run too.
        Through(wm);
        Check("the replayed run planetfalls into Frost again", WorldManager.CurrentIndex == Frost);
        DieAndReplay();
        wm = Load();
        Check("a second Replay is Space again", WorldManager.CurrentIndex == Space);
        Clean("second replay", wm);
    }

    static void AfterPortal()
    {
        Prefs(Verdant);
        var wm = Load();
        Check("a run on the furthest planet starts in Verdant", WorldManager.CurrentIndex == Verdant);
        Through(wm);
        Check("Verdant's portal leads to Ember and unlocks it",
              WorldManager.CurrentIndex == Ember && PlayerPrefs.GetInt(WorldManager.PrefsHighestWorld) == Ember);
        DieAndReplay();
        wm = Load();
        Check("after a portal, Replay starts in Verdant again (was: Ember) [" +
              WorldManager.Worlds[WorldManager.CurrentIndex].displayName + "]",
              WorldManager.CurrentIndex == Verdant && RunLoop.StartWorld == Verdant);
        Clean("replay after a portal", wm);
    }

    static void AfterLoop()
    {
        Prefs(Ember);
        var wm = Load();
        Check("an Ember run", WorldManager.CurrentIndex == Ember && RunLoop.StartWorld == Ember);
        FinishLevel(wm);
        Check("the loop portal is open", wm.PortalIsOpen && !WorldManager.HasNext);
        wm.Advance(false);
        Gone();
        Check("looped back to Ember, loop 1", WorldManager.CurrentIndex == Ember && RunLoop.Index == 1);
        DieAndReplay();
        wm = Load();
        Check("after a loop, Replay starts in Ember on loop 0", WorldManager.CurrentIndex == Ember && RunLoop.Index == 0);
        Clean("replay after a loop", wm);
    }

    static void DeveloperPick()
    {
        Prefs(Ember, developer: true, pick: Frost);
        var wm = Load();
        Check("developer pick: the run starts in Frost", WorldManager.CurrentIndex == Frost);
        FinishLevel(wm);
        Gone();
        // Frost's lift-off then Verdant's planetfall; the switch is Advance.
        wm.Advance(false);
        Check("on to Verdant", WorldManager.CurrentIndex == Verdant);
        DieAndReplay();
        wm = Load();
        Check("developer pick: Replay is Frost again", WorldManager.CurrentIndex == Frost);
        Clean("developer replay", wm);
    }


    // ---- developer start-world pick, every world, every way of (re)starting ----

    static string Backdrop()
    {
        var wb = WorldBackdrop.Instance;
        return wb != null && wb.Current != null ? wb.Current.Spec.world : "(none)";
    }

    static string Name(int w) { return WorldManager.Worlds[w].displayName; }

    // The real sequence of calls, in the order Unity runs them, for a
    // developer pick on every world, with the furthest planet reached
    // anywhere from Space to Ember and either start rule: the first run, then
    // Replay after Replay (from the death panel / quick action / pause menu
    // they are all buttonClicks.replay -> PrepareReplay), then the menu.
    static void DeveloperMatrix()
    {
        int worlds = WorldManager.Worlds.Length;
        for (int pick = 0; pick < worlds; pick++)
        {
            for (int highest = 0; highest < worlds; highest++)
            {
                foreach (bool startAtHighest in new[] { true, false })
                {
                    string tag = "dev pick " + Name(pick) + ", furthest " + Name(highest) +
                                 (startAtHighest ? "" : ", journey") + ": ";
                    Prefs(highest, developer: true, pick: pick);
                    // Stale state from the session before (the app restart: the
                    // pin and the loop are in memory only, prefs persist).
                    var wm = Load(startAtHighest);
                    Check(tag + "first run starts in the pick (loop " + RunLoop.Index + ", backdrop " + Backdrop() + ")",
                          WorldManager.CurrentIndex == pick && RunLoop.StartWorld == pick && RunLoop.Index == 0 &&
                          (Backdrop() == "(none)" || Backdrop() == Name(pick)));

                    // Fly into the next world (planetfall / portal / lift-off) and replay.
                    for (int replay = 1; replay <= 3; replay++)
                    {
                        if (replay != 2) Through(wm);
                        DieAndReplay();
                        wm = Load(startAtHighest);
                        Check(tag + "replay " + replay + " starts in the pick, not " + Name(WorldManager.CurrentIndex) +
                              " (backdrop " + Backdrop() + ")",
                              WorldManager.CurrentIndex == pick && RunLoop.StartWorld == pick && RunLoop.Index == 0 &&
                              (Backdrop() == "(none)" || Backdrop() == Name(pick)));
                    }

                    // Out to the menu and PLAY again: the pick still rules.
                    GameStateReset.Clear();
                    wm = Load(startAtHighest);
                    Check(tag + "menu PLAY starts in the pick", WorldManager.CurrentIndex == pick && RunLoop.StartWorld == pick);
                }
            }
        }
    }

    // ---- 2 --------------------------------------------------------------------

    static void MenuDropsThePin()
    {
        Prefs(Space);
        var wm = Load();
        Through(wm);
        Check("Space -> Frost", WorldManager.CurrentIndex == Frost);
        DieAndReplay();
        // ... but the pilot leaves for the menu instead (GoHome / any
        // non-gameplay scene: GameStateReset.Clear).
        GameStateReset.Clear();
        Check("leaving for the menu drops the replay pin", WorldManager.PinnedReplayWorld < 0);
        wm = Load();
        Check("menu PLAY keeps the furthest-planet rule (Frost)", WorldManager.CurrentIndex == Frost);
        Clean("menu run", wm);
        Check("without a live run Replay pins nothing", RepinWithoutRun());
    }

    static bool RepinWithoutRun()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        WorldManager.ClearReplayWorld();
        buttonClicks.PrepareReplay();
        return WorldManager.PinnedReplayWorld < 0;
    }

    // ---- 4 --------------------------------------------------------------------

    static int RuntimeListeners(UnityEventBase e)
    {
        var calls = typeof(UnityEventBase).GetField("m_Calls", Inst)?.GetValue(e);
        var runtime = calls?.GetType().GetField("m_RuntimeCalls", Inst)?.GetValue(calls) as System.Collections.IList;
        return runtime != null ? runtime.Count : -1;
    }

    static void QuickActionsFireOnce()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/gameS1.unity", OpenSceneMode.Single);
        var source = SceneUtil.FindAny("replayWhenPausedButton");
        var sourceButton = source != null ? source.GetComponent<Button>() : null;
        Check("the scene's pause replay button calls buttonClicks.replay (persistent)",
              sourceButton != null && sourceButton.onClick.GetPersistentEventCount() == 1 &&
              sourceButton.onClick.GetPersistentMethodName(0) == "replay");

        var go = new GameObject("~PauseQuickActionsTest");
        var comp = go.AddComponent<PauseQuickActions>();
        comp.SendMessage("Start");
        foreach (var name in new[] { "replayClone", "leaveClone" })
        {
            var clone = typeof(PauseQuickActions).GetField(name, Inst).GetValue(comp) as GameObject;
            var button = clone != null ? clone.GetComponent<Button>() : null;
            if (button == null) { Check(name + " exists", false); continue; }
            int persistent = button.onClick.GetPersistentEventCount();
            int runtime = RuntimeListeners(button.onClick);
            Check(name + ": one action per tap (persistent " + persistent + " + runtime " + runtime + ")",
                  persistent == 0 && runtime == 1);
        }
        Object.DestroyImmediate(go);
    }
}
