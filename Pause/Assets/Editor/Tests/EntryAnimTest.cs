using System.IO;
using System.Reflection;
using UnityEditor.SceneManagement;
using UnityEngine;

// The run-start entry (WorldEntry): every run begun from the menu plays its
// entry before the pilot has control.
//
//   1  the plan: Space -> the portal arrival, a planet -> its planetfall
//      (Tide's too once a run starts there), a replay and a disabled entry ->
//      none; a planet whose planetfall can't play falls back to the portal
//   2  Space's portal arrival: the ship held, shielded and shrunk in the
//      vortex, the run waiting (level clock, score, speed ramp), the real
//      timing, and a clean hand-off (full size, at the start, no transition,
//      score 0, hearts as they were)
//   3  every planet's planetfall as the run's entry: Space's sky first, the
//      world switched in under the clouds, the same clean hand-off
//   4  every way into a run: menu PLAY at each highest world, the player's
//      START WORLD, the developer's pick, the first run after the tutorial
//      (finished and skipped), a replay (none), the tutorial scene (none)
//   5  a lifted finger freezes the entry; no per-frame allocation
//   6  the hooks are wired
public static class EntryAnimTest
{
    static int fails;
    static void Check(string what, bool ok)
    {
        Debug.Log((ok ? "[ENTRY] PASS  " : "[ENTRY] FAIL  ") + what);
        if (!ok) fails++;
    }

    public static void Run() { TestHarness.Exit(Execute()); }

    const BindingFlags Inst = BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance;
    const BindingFlags Stat = BindingFlags.NonPublic | BindingFlags.Static;
    const float Dt = 1f / 60f;
    static int frame;

    public static int Execute()
    {
        fails = 0;
        using var sandbox = new TestHarness.Sandbox();
        try
        {
            Plan();
            SpaceEntry();
            for (int world = 1; world <= 3; world++) PlanetEntry(world);
            WorldManager.TideEnabled = true;
            PlanetEntry(4);
            WorldManager.TideEnabled = false;
            Paths();
            Frozen();
            NoAllocations(0);
            NoAllocations(2);
            Wiring();
        }
        finally
        {
            Gone();
            WorldEntry.Enabled = true;
            WorldManager.TideEnabled = false;
            WorldManager.ClearReplayWorld();
            PlanetfallCatalog.Enabled = true;
            PlanetfallCatalog.Defs = PlanetfallCatalog.All;
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
            collisionDetection.lifeCounter = 0;
            moveBackGround.speed = 0f;
        }
        Debug.Log("[ENTRY] failures: " + fails);
        return fails;
    }

    // ---- fixtures -----------------------------------------------------------

    static void FreshScene(int highest)
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        BossEncounter.ResetRun();
        RunLoop.Reset();
        PortalPressure.Reset();
        WorldManager.ClearReplayWorld();
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
        PlayerPrefs.DeleteAll();
        PlayerPrefs.SetString("HasDoneTut", "true");
        PlayerPrefs.SetInt(DeveloperUnlocks.EnabledKey, 0);
        PlayerPrefs.SetInt(WorldManager.PrefsHighestWorld, highest);
        PlayerPrefs.SetInt(WorldManager.PrefsCurrentWorld, highest);
        ShipStartSpeed.EquippedHudOverride = () => ShipStartSpeed.StockHud;
        frame = 90000;
        SpeedRamp.FrameOverride = () => frame;
        SpeedRamp.DeltaOverride = () => Dt;
        SpeedRamp.ResetFrameGuard();
        SpeedRamp.ResetBoost();
        ResumeSlowMo.ClockOverride = () => frame * Dt;
        ResumeSlowMo.ResetRun();
        RunScore.EndRun(RunScore.RunId);
        RunScore.BeginRun(true, true);
        collisionDetection.lifeCounter = 1;
        moveBackGround.speed = 0f;
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
        go.transform.localScale = Vector3.one * .8f;
        return go.transform;
    }

    // A run start the way the game runs it: Awake, then Start.
    static WorldManager StartRun(bool startAtHighest = true)
    {
        var wm = new GameObject("~WorldManager").AddComponent<WorldManager>();
        wm.startAtHighestUnlocked = startAtHighest;
        wm.SendMessage("Awake");
        wm.HoldStartWorld();
        typeof(WorldManager).GetMethod("Start", Inst).Invoke(wm, null);
        return wm;
    }

    static void Gone()
    {
        if (Liftoff.Live != null) Object.DestroyImmediate(Liftoff.Live.gameObject);
        if (Planetfall.Live != null) Object.DestroyImmediate(Planetfall.Live.gameObject);
        if (PortalArrival.Live != null) Object.DestroyImmediate(PortalArrival.Live.gameObject);
        foreach (var p in Object.FindObjectsByType<Portal>(FindObjectsSortMode.None)) Object.DestroyImmediate(p.gameObject);
    }

    // One frame the way the game runs it: only while Flying.
    static void Fly(WorldManager wm)
    {
        frame++;
        if (!WorldManager.Flying) return;
        if (wm != null) wm.Tick(Dt);
        var p = Planetfall.Live;
        if (p != null && p.State != Planetfall.Stage.Done) p.Step(Dt);
        var a = PortalArrival.Live;
        if (a != null && a.State != PortalArrival.Stage.Done) a.Step(Dt);
    }

    static bool EntryHooksOff()
    {
        return !Planetfall.HoldsShip && !Planetfall.ShieldsShip && !Planetfall.SuspendsSpawning && !Planetfall.FreePress &&
               !PortalArrival.HoldsShip && !PortalArrival.ShieldsShip && !PortalArrival.SuspendsSpawning && !PortalArrival.FreePress &&
               !WorldEntry.Active && !WorldTransition.InProgress && WorldBackdrop.ScrollBoost == 1f;
    }

    static string BackdropWorld() { return WorldBackdrop.Instance != null && WorldBackdrop.Instance.Current != null ? WorldBackdrop.Instance.Current.Spec.world : null; }
    static string SpecWorld(int world) { return BackdropCatalog.For(WorldManager.Worlds[world].displayName).world; }

    // ---- 1. the plan ------------------------------------------------------------

    static void Plan()
    {
        Check("Space's run starts with the portal arrival",
              WorldEntry.Plan(0, false) == WorldEntry.Kind.Portal);
        Check("Frost's, Verdant's and Ember's with their planetfall",
              WorldEntry.Plan(1, false) == WorldEntry.Kind.Planetfall && WorldEntry.Plan(2, false) == WorldEntry.Kind.Planetfall &&
              WorldEntry.Plan(3, false) == WorldEntry.Kind.Planetfall);
        Check("Tide's too (a run only starts there with the switch on, or by the developer's pick)",
              WorldEntry.Plan(4, false) == WorldEntry.Kind.Planetfall);
        Check("a replay has none, whatever the world",
              WorldEntry.Plan(0, true) == WorldEntry.Kind.None && WorldEntry.Plan(2, true) == WorldEntry.Kind.None);
        WorldEntry.Enabled = false;
        Check("switched off: none", WorldEntry.Plan(0, false) == WorldEntry.Kind.None && WorldEntry.Plan(3, false) == WorldEntry.Kind.None);
        WorldEntry.Enabled = true;
        PlanetfallCatalog.Enabled = false;
        Check("planetfalls switched off: a planet's run starts with the portal arrival",
              WorldEntry.Plan(2, false) == WorldEntry.Kind.Portal);
        PlanetfallCatalog.Enabled = true;

        string folder = PlanetfallCatalog.Frost.folder;
        try
        {
            PlanetfallCatalog.Frost.folder = "Worlds/Nowhere/";
            FreshScene(1);
            var ship = Ship();
            var wm = StartRun();
            Check("Frost's art missing: the portal arrival plays, Frost shown at once",
                  wm.EntryPlayed == WorldEntry.Kind.Portal && PortalArrival.Active && Planetfall.Live == null &&
                  BackdropWorld() == SpecWorld(1));
            Gone();
            Object.DestroyImmediate(ship.gameObject);
            Object.DestroyImmediate(wm.gameObject);
        }
        finally { PlanetfallCatalog.Frost.folder = folder; }

        FreshScene(0);
        var wm2 = StartRun();
        Check("no ship in the scene (a bare manager): no entry, the run as before", wm2.EntryPlayed == WorldEntry.Kind.None && EntryHooksOff());
        Object.DestroyImmediate(wm2.gameObject);

        var tl = typeof(PortalArrivalTimeline);
        Check("the portal arrival's timeline: about 2.6 s, open before emerge before close before release, the ship hidden until it emerges",
              PortalArrivalTimeline.Seconds >= 2f && PortalArrivalTimeline.Seconds <= 3f &&
              PortalArrivalTimeline.OpenFrom < PortalArrivalTimeline.EmergeFrom && PortalArrivalTimeline.EmergeFrom < PortalArrivalTimeline.CloseFrom &&
              PortalArrivalTimeline.CloseTo < PortalArrivalTimeline.Seconds && PortalArrivalTimeline.PortalScale(0f) <= .001f &&
              PortalArrivalTimeline.PortalScale(PortalArrivalTimeline.Seconds) <= .001f &&
              Mathf.Abs(PortalArrivalTimeline.PortalScale((PortalArrivalTimeline.OpenTo + PortalArrivalTimeline.CloseFrom) * .5f) - 1f) < .001f &&
              PortalArrivalTimeline.ShipScale(0f) < .1f && Mathf.Abs(PortalArrivalTimeline.ShipScale(PortalArrivalTimeline.Seconds) - 1f) < .001f &&
              tl != null);
    }

    // ---- 2. Space: the portal arrival ----------------------------------------------

    static void SpaceEntry()
    {
        FreshScene(0);
        var ship = Ship();
        var hull = ship.GetComponent<SpriteRenderer>();
        var plume = ship.GetChild(0).GetComponent<SpriteRenderer>();
        Vector3 scale0 = ship.localScale;
        var wm = StartRun();
        var a = PortalArrival.Live;
        Check("Space, from the menu: the portal arrival plays", wm.EntryPlayed == WorldEntry.Kind.Portal && a != null && Planetfall.Live == null);
        Check("... the run has not begun: a transition for the weapon charge, the ship held, shielded and spawns off, a press free",
              WorldEntry.Active && WorldTransition.InProgress && PortalArrival.HoldsShip && PortalArrival.ShieldsShip &&
              PortalArrival.SuspendsSpawning && PortalArrival.FreePress);
        Check("... the world is shown at once (Space's backdrop), no banner wait, the portal in the world's colour",
              BackdropWorld() == SpecWorld(0) && a.RingRenderer.color.r == WorldManager.Worlds[0].portalColor.r);
        Check("... the ship's renderers are lifted over the portal (hull " + hull.sortingOrder + ", plume " + plume.sortingOrder + ")",
              hull.sortingOrder == PortalArrival.Raise && plume.sortingOrder == PortalArrival.Raise - 1 &&
              hull.sortingOrder > PortalArrival.RingOrder);

        float distance = wm.DistanceLeft;
        long score0 = RunScore.Total;
        float speed0 = moveBackGround.speed;
        bool opened = false, shrunk = false, emerged = false, ringSeen = false, closed = false, clockHeld = true, held = true;
        float endAt = -1f;
        Vector3 startAt = a.Centre;
        for (int i = 0; i < (int)(6f / Dt) && PortalArrival.Live != null; i++)
        {
            Fly(wm);
            if (PortalArrival.Live == null) { endAt = i * Dt; break; }
            float t = a.Seconds;
            clockHeld &= wm.DistanceLeft == distance && wm.LevelClockSeconds == 0f;
            held &= PortalArrival.HoldsShip;
            if (Mathf.Abs(t - .3f) < Dt) { shrunk = ship.localScale.x < scale0.x * .5f && a.RingRenderer.enabled; }
            if (Mathf.Abs(t - PortalArrivalTimeline.EmergeTo) < Dt) emerged = ship.localScale.x >= scale0.x * .95f;
            if (Mathf.Abs(t - (PortalArrivalTimeline.OpenTo + .3f)) < Dt) opened = a.RingRenderer.enabled && a.RingRenderer.transform.localScale.x > .9f * PortalArrival.PortalSize;
            ringSeen |= a.WaveRenderer.enabled;
            if (Mathf.Abs(t - PortalArrivalTimeline.CloseTo) < Dt) closed = !a.RingRenderer.enabled;
        }
        Check("the portal opens (full size by " + PortalArrivalTimeline.OpenTo.ToString("F2") + " s), the ship comes out small and grows, a ring leaves, the portal closes",
              opened && shrunk && emerged && ringSeen && closed);
        Check("the level clock waited (distance " + wm.DistanceLeft.ToString("F1") + " of " + distance.ToString("F1") + "), the ship held the whole time", clockHeld && held);
        Check("it hands over at " + endAt.ToString("F2") + " s (timeline " + PortalArrivalTimeline.Seconds + ")",
              Mathf.Abs(endAt - PortalArrivalTimeline.Seconds) < .1f);
        Check("a clean state: every hook off, no transition, the portal gone",
              PortalArrival.Live == null && EntryHooksOff() && GameObject.Find("~PortalArrival") == null &&
              Portal.Live == null && !PortalPressure.Active);
        Check("... the ship at its start, full size, its renderers back (hull " + hull.sortingOrder + ", plume " + plume.sortingOrder + ")",
              ship.localScale == scale0 && Mathf.Abs(ship.position.x) < 1e-3f && Mathf.Abs(ship.position.y - ShipReach.StartY) < 1e-3f &&
              hull.sortingOrder == 0 && plume.sortingOrder == -1);
        Check("... score 0, hearts as they were (" + collisionDetection.lifeCounter + "), the same speed, a full world ahead",
              RunScore.Total == score0 && RunScore.Total == 0 && collisionDetection.lifeCounter == 1 && moveBackGround.speed == speed0 &&
              wm.Stage == WorldManager.LevelStage.Level && Mathf.Approximately(wm.DistanceLeft, wm.WorldDistance));
        Fly(wm);
        Check("... and the level clock runs from the hand-off", wm.DistanceLeft < wm.WorldDistance || moveBackGround.speed <= 0f);
        Object.DestroyImmediate(ship.gameObject);
        Object.DestroyImmediate(wm.gameObject);
    }

    // ---- 3. a planet: its planetfall ----------------------------------------------

    static void PlanetEntry(int world)
    {
        string name = WorldManager.Worlds[world].displayName;
        FreshScene(world);
        var ship = Ship();
        var hull = ship.GetComponent<SpriteRenderer>();
        var plume = ship.GetChild(0).GetComponent<SpriteRenderer>();
        var wm = StartRun();
        var p = Planetfall.Live;
        Check(name + ", from the menu: its planetfall plays as the entry (no portal)",
              wm.EntryPlayed == WorldEntry.Kind.Planetfall && p != null && p.IsEntry && p.Def.world == world &&
              p.State == Planetfall.Stage.Descent && Portal.Live == null && PortalArrival.Live == null && WorldManager.CurrentIndex == world);
        Check("... the run has not begun: held, shielded, spawns off, a transition, the ship lifted over the clouds, no pressure",
              WorldEntry.Active && WorldTransition.InProgress && Planetfall.HoldsShip && Planetfall.ShieldsShip &&
              Planetfall.SuspendsSpawning && !PortalPressure.Active && hull.sortingOrder == Planetfall.Raise);
        Check("... it starts in Space's sky (not " + name + "'s), the banner waits for the clouds",
              BackdropWorld() == SpecWorld(0));

        float distance = wm.DistanceLeft;
        long score0 = RunScore.Total;
        float speed0 = moveBackGround.speed;
        string sky = null;
        bool clockHeld = true, switchedUnder = false, held = true;
        float endAt = -1f, switchedAt = -1f;
        for (int i = 0; i < (int)(10f / Dt) && Planetfall.Live != null; i++)
        {
            Fly(wm);
            if (Planetfall.Live == null) { endAt = i * Dt; break; }
            clockHeld &= wm.DistanceLeft == distance && wm.LevelClockSeconds == 0f;
            held &= Planetfall.HoldsShip;
            if (switchedAt < 0f && p.Switched)
            {
                switchedAt = p.Seconds;
                sky = BackdropWorld();
                switchedUnder = PlanetfallTimeline.Cover(p.Seconds) >= .999f;
            }
        }
        float expected = PlanetfallTimeline.Seconds / Planetfall.EntryTimeScale;
        Check("the clouds covered the view as " + name + " replaced Space (at " + switchedAt.ToString("F2") + " s of the clock)",
              switchedUnder && sky == SpecWorld(world));
        Check("the level clock waited and the ship was flown the whole way", clockHeld && held);
        Check("it hands over at " + endAt.ToString("F2") + " s (" + PlanetfallTimeline.Seconds + " s of timeline at x" + Planetfall.EntryTimeScale + ")",
              Mathf.Abs(endAt - expected) < .15f);
        Check("a clean state: every hook off, no transition, nothing left in the scene",
              Planetfall.Live == null && EntryHooksOff() && GameObject.Find("~Planetfall") == null && GameObject.Find("~PlanetfallStage") == null);
        Check("... the ship at its start, its renderers back, still " + name,
              Mathf.Abs(ship.position.x) < 1e-3f && Mathf.Abs(ship.position.y - ShipReach.StartY) < 1e-3f &&
              hull.sortingOrder == 0 && plume.sortingOrder == -1 && WorldManager.CurrentIndex == world);
        Check("... score 0, hearts as they were, the same speed, a full world ahead, no world bonus paid",
              RunScore.Total == 0 && score0 == 0 && collisionDetection.lifeCounter == 1 && moveBackGround.speed == speed0 &&
              wm.Stage == WorldManager.LevelStage.Level && Mathf.Approximately(wm.DistanceLeft, wm.WorldDistance));
        Object.DestroyImmediate(ship.gameObject);
        Object.DestroyImmediate(wm.gameObject);
    }

    // ---- 4. every way into a run ------------------------------------------------------

    static WorldEntry.Kind Started(System.Action setup)
    {
        FreshScene(0);
        setup();
        var ship = Ship();
        var wm = StartRun();
        var kind = wm.EntryPlayed;
        int world = WorldManager.CurrentIndex;
        Check("  (started on " + WorldManager.Worlds[world].displayName + ": " + kind + ")", true);
        lastWorld = world;
        Gone();
        Object.DestroyImmediate(ship.gameObject);
        Object.DestroyImmediate(wm.gameObject);
        return kind;
    }
    static int lastWorld;

    static void Paths()
    {
        // menu PLAY: the furthest planet reached
        for (int highest = 0; highest <= 3; highest++)
        {
            int h = highest;
            var kind = Started(() => PlayerPrefs.SetInt(WorldManager.PrefsHighestWorld, h));
            Check("menu PLAY, furthest world " + h + ": " + (h == 0 ? "the portal arrival" : "the planetfall") + " on " + WorldManager.Worlds[h].displayName,
                  lastWorld == h && kind == (h == 0 ? WorldEntry.Kind.Portal : WorldEntry.Kind.Planetfall));
        }
        // the player's START WORLD column
        var psw = Started(() =>
        {
            PlayerPrefs.SetInt(WorldManager.PrefsHighestWorld, 3);
            PlayerPrefs.SetInt(PlayerStartWorld.Key, 2);
        });
        Check("START WORLD = Verdant (furthest Ember): Verdant's planetfall", lastWorld == 2 && psw == WorldEntry.Kind.Planetfall);
        var pswSpace = Started(() =>
        {
            PlayerPrefs.SetInt(WorldManager.PrefsHighestWorld, 3);
            PlayerPrefs.SetInt(PlayerStartWorld.Key, 0);
        });
        Check("START WORLD = Space (furthest Ember): the portal arrival", lastWorld == 0 && pswSpace == WorldEntry.Kind.Portal);
        // the developer's world picker
        var dev = Started(() =>
        {
            PlayerPrefs.SetInt(DeveloperUnlocks.EnabledKey, 1);
            PlayerPrefs.SetInt(DeveloperUnlocks.SelectedWorldKey, 3);
        });
        Check("developer pick = Ember: Ember's planetfall", lastWorld == 3 && dev == WorldEntry.Kind.Planetfall);
        var devSpace = Started(() =>
        {
            PlayerPrefs.SetInt(DeveloperUnlocks.EnabledKey, 1);
            PlayerPrefs.SetInt(DeveloperUnlocks.SelectedWorldKey, 0);
            PlayerPrefs.SetInt(WorldManager.PrefsHighestWorld, 3);
        });
        Check("developer pick = Space: the portal arrival", lastWorld == 0 && devSpace == WorldEntry.Kind.Portal);
        var devTide = Started(() =>
        {
            PlayerPrefs.SetInt(DeveloperUnlocks.EnabledKey, 1);
            PlayerPrefs.SetInt(DeveloperUnlocks.SelectedWorldKey, 4);
        });
        Check("developer pick = Tide (switch off): Tide's planetfall", lastWorld == 4 && devTide == WorldEntry.Kind.Planetfall);

        // the first run after the tutorial: finished (its PLAY button) or skipped
        var afterSkip = Started(() =>
        {
            PlayerPrefs.DeleteAll();
            TutorialSkip.FinishBySkipping();
        });
        Check("the first real run after the tutorial (skipped): the portal arrival into Space",
              lastWorld == 0 && afterSkip == WorldEntry.Kind.Portal && PlayerPrefs.GetString("HasDoneTut") == "true");
        var afterFinish = Started(() =>
        {
            PlayerPrefs.DeleteAll();
            PlayerPrefs.SetString("HasDoneTut", "true");   // Hints marks it done before the card
            startMenu.youAreInTutorial = false;
            buttonClicks.playerDied = false;
            moveBackGround.speed = 0f;
            score.totalCurrency = 0;
        });
        Check("... and from the end card's PLAY (tutButtonClicks.replay's state): the same", lastWorld == 0 && afterFinish == WorldEntry.Kind.Portal);

        // a replay after a death: quick
        FreshScene(2);
        var ship = Ship();
        var first = StartRun();
        Check("the run's own start (Verdant): its planetfall", first.EntryPlayed == WorldEntry.Kind.Planetfall);
        Gone();
        buttonClicks.PrepareReplay();   // GameStateReset.Clear + the pin, as REPLAY does
        Check("REPLAY pins the run's world (Verdant)", WorldManager.PinnedReplayWorld == 2);
        Object.DestroyImmediate(first.gameObject);
        var again = StartRun();
        Check("... the replayed run starts at once, no entry, on Verdant",
              again.EntryPlayed == WorldEntry.Kind.None && !WorldEntry.Active && EntryHooksOff() && WorldManager.CurrentIndex == 2 &&
              BackdropWorld() == SpecWorld(2) && ship.localScale == Vector3.one * .8f);
        WorldManager.ClearReplayWorld();
        Object.DestroyImmediate(again.gameObject);
        var menuAgain = StartRun();
        Check("... and a run begun from the menu afterwards (pin dropped on leaving gameplay) plays its entry",
              menuAgain.EntryPlayed == WorldEntry.Kind.Planetfall);
        Gone();
        Object.DestroyImmediate(menuAgain.gameObject);
        Object.DestroyImmediate(ship.gameObject);

        string boot = File.ReadAllText("Assets/Scripts/Worlds/WorldManager.cs");
        Check("the tutorial scene has no WorldManager, so no entry (WorldBootstrap attaches to gameS1 only)",
              boot.Contains("if (scene.name != \"gameS1\") return;"));
    }

    // ---- 5. a lifted finger, allocations --------------------------------------------------

    static void Frozen()
    {
        foreach (int world in new[] { 0, 2 })
        {
            FreshScene(world);
            var ship = Ship();
            var wm = StartRun();
            for (int i = 0; i < 60; i++) Fly(wm);
            float t0 = world == 0 ? PortalArrival.Live.Seconds : Planetfall.Live.Seconds;
            Vector3 at = ship.position;
            score.pauseCounter = 3;
            for (int i = 0; i < 120; i++) Fly(wm);
            float t1 = world == 0 ? PortalArrival.Live.Seconds : Planetfall.Live.Seconds;
            Check(WorldManager.Worlds[world].displayName + "'s entry: a lifted finger freezes it (clock " + t0.ToString("F2") + " -> " + t1.ToString("F2") + ")",
                  t0 == t1 && ship.position == at && WorldEntry.Active);
            score.pauseCounter = 0;
            for (int i = 0; i < (int)(10f / Dt) && WorldEntry.Active; i++) Fly(wm);
            Check("... and the finger back, it plays out", !WorldEntry.Active && EntryHooksOff());
            Gone();
            Object.DestroyImmediate(ship.gameObject);
            Object.DestroyImmediate(wm.gameObject);
        }
    }

    static void NoAllocations(int world)
    {
        for (int pass = 0; pass < 2; pass++)
        {
            FreshScene(world);
            var ship = Ship();
            var wm = StartRun();
            for (int i = 0; i < 20; i++) Fly(wm);
            long bytes = 0;
            int frames = world == 0 ? 120 : 130;
            if (pass == 1) bytes = TestHarness.AllocatedBytes(() => { for (int i = 0; i < frames; i++) Fly(wm); });
            else for (int i = 0; i < frames; i++) Fly(wm);
            if (pass == 1)
            {
                long control;
                bool meter = TestHarness.AllocMeterWorks(out control);
                const long Margin = 512;
                Check("the allocation meter works (" + control + " bytes for the control)", meter);
                Check(WorldManager.Worlds[world].displayName + "'s entry: no allocation per frame (" + frames + " frames " + bytes + " B, margin " + Margin + ")",
                      meter && bytes >= 0 && bytes <= Margin);
            }
            for (int i = 0; i < 1200 && WorldEntry.Active; i++) Fly(wm);
            Gone();
            Object.DestroyImmediate(ship.gameObject);
            Object.DestroyImmediate(wm.gameObject);
        }
    }

    // ---- 6. wiring -----------------------------------------------------------------------

    static void Wiring()
    {
        string Read(string f) { return File.ReadAllText("Assets/Scripts/" + f); }
        Check("the hooks are wired: movePlayer, collisionDetection, the spawners, score",
              Read("Ship/movePlayer.cs").Contains("PortalArrival.HoldsShip") &&
              Read("Ship/collisionDetection.cs").Contains("PortalArrival.ShieldsShip") &&
              Read("Gameplay/Spawning/enmiesOnBoard.cs").Contains("PortalArrival.SuspendsSpawning") &&
              Read("Gameplay/Pickups/spawnGoodStuff.cs").Contains("PortalArrival.SuspendsSpawning") &&
              Read("Core/score.cs").Contains("PortalArrival.FreePress"));
        Check("the run waits for its entry: the level clock, the speed ramp, the score and dust, the elites",
              Read("Worlds/WorldManager.cs").Contains("if (WorldEntry.Active) return;") &&
              Read("Gameplay/moveBackGround.cs").Contains("!WorldEntry.Active") &&
              Read("Core/score.cs").Contains("if (WorldEntry.Active) return;") &&
              Read("Gameplay/Elites/EliteDirector.cs").Contains("WorldEntry.Active"));
        Check("the calm start's window waits for the entry (so the full " + enmiesOnBoard.CalmArrivalSeconds + " s is still ahead)",
              Read("Gameplay/Spawning/enmiesOnBoard.cs").Contains("calmEndsAt += Time.deltaTime"));
        Check("the entry is a world transition for the weapon charge",
              Read("Worlds/WorldTransition.cs").Contains("PortalArrival.Active"));
    }
}
