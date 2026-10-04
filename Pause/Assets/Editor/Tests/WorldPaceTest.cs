using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor.SceneManagement;
using UnityEngine;

// Start speed by ship and colour (ShipStartSpeed) and world length by
// distance (WorldManager.BaselineWorldSeconds):
//
//   1  the start-speed table: regular ships 0/5/10/15/20, high-end ships
//      (the four priciest) 10/15/20/25/30, by colour 1..5
//   2  a run starts at the equipped colour's speed, clamped to the world cap;
//      a portal arrival never drops below it
//   3  each world reaches its boss after ~120s of flight at the baseline
//      (stock start), and strictly sooner at starts 5/10/20/30 (logged)
//   4  paused / frozen time flies no distance
//   5  the dock popup shows the colour's START SPD
public static class WorldPaceTest
{
    static int fails;
    static void Check(string what, bool ok)
    {
        Debug.Log((ok ? "[PACE] PASS  " : "[PACE] FAIL  ") + what);
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
        try
        {
            Table();
            RunStart();
            TimeToBoss();
            PausedFliesNothing();
            DockLine();
        }
        finally
        {
            ShipStartSpeed.EquippedHudOverride = null;
            SpeedRamp.FrameOverride = null;
            SpeedRamp.DeltaOverride = null;
            SpeedRamp.ResetFrameGuard();
            ResumeSlowMo.ClockOverride = null;
            ResumeSlowMo.ResetRun();
            BossEncounter.ResetRun();
            RunLoop.Reset();
            buttonClicks.playerDied = false;
        }
        Debug.Log("[PACE] failures: " + fails);
        return fails;
    }

    // ---- 1. the table ---------------------------------------------------------

    static void Table()
    {
        var byPrice = ShipId.All.OrderByDescending(id => shopingShips.CostFor(id)).Take(4).OrderBy(id => id).ToArray();
        var highEnd = ShipStartSpeed.HighEndShips.OrderBy(id => id).ToArray();
        Check("high-end ships are the four priciest (" + string.Join(",", byPrice) + ")", byPrice.SequenceEqual(highEnd));
        Check("high-end = Ion Lancer, Jade Phantom, Gold Warden, Turtle",
              highEnd.Select(ShipId.KeyOf).SequenceEqual(new[] { "IonLancer", "JadePhantom", "GoldWarden", "Turtle" }));

        int[] regular = { 0, 5, 10, 15, 20 }, high = { 10, 15, 20, 25, 30 };
        bool all = true;
        var lines = new List<string>();
        foreach (int id in ShipId.All)
        {
            int[] want = ShipStartSpeed.IsHighEnd(id) ? high : regular;
            var got = new int[ShipSkins.CountFor(id)];
            for (int skin = 0; skin < got.Length; skin++) got[skin] = ShipStartSpeed.HudFor(id, skin);
            all &= got.Length == 5 && got.SequenceEqual(want);
            lines.Add(ShipId.KeyOf(id) + " " + string.Join("/", got));
        }
        Debug.Log("[PACE] start speeds by colour: " + string.Join("; ", lines));
        Check("every ship: colour 1..5 = 0/5/10/15/20, high-end 10/15/20/25/30", all);
        Check("regular colour 1 is gameS1's own start (0)", ShipStartSpeed.HudFor(1, 0) == ShipStartSpeed.StockHud && ShipStartSpeed.StockHud == 0);
        Check("an unknown skin falls back to colour 1", ShipStartSpeed.HudFor(7, 99) == 10 && ShipStartSpeed.HudFor(2, -1) == 0);
        Check("no ship: the stock start", ShipStartSpeed.HudFor(0, 3) == 0);
        Check("speed = HUD / 100", Mathf.Approximately(ShipStartSpeed.SpeedFor(15, 4), .30f));
        Check("label", ShipStartSpeed.Label(15) == "START SPD 15");
        float lowestCap = WorldManager.Worlds.Min(w => w.maxSpeed);
        Check("the fastest start (HUD 30) is under every world's cap (lowest " + lowestCap + ")",
              ShipStartSpeed.SpeedFor(7, 4) < lowestCap);
    }

    // ---- 2. a run's start ---------------------------------------------------------

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
        frame = 1000;
        clock = 10f;
        SpeedRamp.FrameOverride = () => frame;
        SpeedRamp.DeltaOverride = () => Dt;
        SpeedRamp.ResetFrameGuard();
        ResumeSlowMo.ClockOverride = () => clock;
        ResumeSlowMo.ResetRun();
    }

    static WorldManager World()
    {
        var wm = new GameObject("~WorldManager").AddComponent<WorldManager>();
        wm.SendMessage("Awake");
        typeof(WorldManager).GetField("distanceLeft", Inst).SetValue(wm, wm.WorldDistance);
        var apply = typeof(WorldManager).GetMethod("ApplyDifficulty", BindingFlags.NonPublic | BindingFlags.Static);
        apply.Invoke(null, new object[] { WorldManager.Current });
        return wm;
    }

    static moveBackGround[] Walls(int n)
    {
        var walls = new moveBackGround[n];
        for (int i = 0; i < n; i++) walls[i] = new GameObject("~wall" + i).AddComponent<moveBackGround>();
        return walls;
    }

    static void RunStart()
    {
        FreshScene(0);
        var wm = World();
        ShipStartSpeed.EquippedHudOverride = () => 0;
        Check("stock start: speed 0", WorldManager.RunStartSpeed(0f) == 0f);
        ShipStartSpeed.EquippedHudOverride = () => 15;
        Check("colour start HUD 15 -> 0.15", Mathf.Approximately(WorldManager.RunStartSpeed(0f), .15f));
        ShipStartSpeed.EquippedHudOverride = () => 99;
        Check("a start past the cap is clamped to the world's (" + WorldManager.Current.maxSpeed + ")",
              Mathf.Approximately(WorldManager.RunStartSpeed(0f), WorldManager.Current.maxSpeed));

        // The real equipped ship/colour, through ShipId and ShipSkins.
        ShipStartSpeed.EquippedHudOverride = null;
        PlayerPrefs.SetString(ShipId.OwnedKey(7), "True");
        ShipId.Equip(7);
        PlayerPrefs.SetInt(ShipSkins.OwnedKey(7, 2), 1);
        ShipSkins.Equip(7, 2);
        Check("Gold Warden in colour 3 starts at 20 (" + ShipStartSpeed.EquippedHud() + ")", ShipStartSpeed.EquippedHud() == 20);
        Check("... and the run does too", Mathf.Approximately(WorldManager.RunStartSpeed(0f), .20f));
        ShipId.Equip(1);
        ShipSkins.Equip(1, 0);
        Check("Neon Comet stock starts at 0", ShipStartSpeed.EquippedHud() == 0);

        // A portal arrival: the loop's arrival speed or the ship's, the higher.
        ShipStartSpeed.EquippedHudOverride = () => 10;
        moveBackGround.speed = .4f;
        try { wm.Advance(); } catch (System.Exception e) { Debug.Log("[PACE] Advance side effect threw (ignored): " + e.Message); }
        Check("arrival in Frost at the ship's start, 10 (" + moveBackGround.speed + ")",
              WorldManager.CurrentIndex == 1 && Mathf.Approximately(moveBackGround.speed, .10f));
        Check("... with Frost's full distance to fly", Mathf.Approximately(wm.DistanceLeft, WorldManager.WorldDistanceFor(1)));
        Object.DestroyImmediate(wm.gameObject);
    }

    // ---- 3. time to the boss ---------------------------------------------------------

    // Seconds of flight from `startHud` until the world's boss begins.
    static float FlyToBoss(int world, int startHud)
    {
        FreshScene(world);
        var wm = World();
        var walls = Walls(2);
        foreach (var w in walls)
        {
            w.speedRampPerSecond = WorldManager.Worlds[world].speedRampPerSecond;
            w.maxSpeed = WorldManager.Worlds[world].maxSpeed;
        }
        moveBackGround.speed = startHud / 100f;
        float t = 0f;
        for (int i = 0; i < 60 * 400 && !BossEncounter.Running; i++)
        {
            frame++;
            clock += Dt;
            foreach (var w in walls) w.SendMessage("Update");
            if (WorldManager.Flying) wm.Tick(Dt);
            t += Dt;
        }
        bool reached = BossEncounter.Running;
        BossEncounter.ResetRun();
        Object.DestroyImmediate(wm.gameObject);
        return reached ? t : float.PositiveInfinity;
    }

    static void TimeToBoss()
    {
        int[] starts = { 0, 5, 10, 20, 30 };
        for (int w = 0; w < WorldManager.Worlds.Length; w++)
        {
            var times = starts.Select(s => FlyToBoss(w, s)).ToArray();
            string name = WorldManager.Worlds[w].displayName;
            Debug.Log("[PACE] " + name + " seconds to boss at start 0/5/10/20/30: " +
                      string.Join(" / ", times.Select(x => x.ToString("F1"))));
            Check(name + ": the baseline (start 0) meets the boss at ~" + WorldManager.BaselineWorldSeconds + "s (" + times[0].ToString("F1") + "s)",
                  Mathf.Abs(times[0] - WorldManager.BaselineWorldSeconds) <= 1f);
            bool shorter = true;
            for (int i = 1; i < times.Length; i++) shorter &= times[i] < times[i - 1] - 1f;
            Check(name + ": every faster start reaches the boss strictly sooner", shorter);
            float predicted = SpeedRamp.SecondsToCover(.30f, WorldManager.Worlds[w].speedRampPerSecond,
                                                       WorldManager.Worlds[w].maxSpeed, WorldManager.WorldDistanceFor(w));
            Check(name + ": the estimate matches the flight at start 30 (" + predicted.ToString("F1") + "s)",
                  Mathf.Abs(predicted - times[4]) <= 1f);
        }
    }

    // ---- 4. paused time ---------------------------------------------------------

    static void PausedFliesNothing()
    {
        FreshScene(0);
        var wm = World();
        var walls = Walls(2);
        moveBackGround.speed = .2f;
        float before = wm.DistanceLeft;

        score.pauseCounter = 3;   // no touch in batch mode: frozen
        Check("paused: not flying", !WorldManager.Flying);
        for (int i = 0; i < 600; i++)
        {
            frame++;
            foreach (var w in walls) w.SendMessage("Update");
            if (WorldManager.Flying) wm.Tick(Dt);
        }
        Check("paused 10s: no distance flown, speed held", wm.DistanceLeft == before && Mathf.Approximately(moveBackGround.speed, .2f));
        Check("a frozen frame (dt 0) flies nothing", Tick0(wm, before));

        score.pauseCounter = 0;
        buttonClicks.playerDied = true;
        Check("dead: not flying", !WorldManager.Flying);
        buttonClicks.playerDied = false;
        Check("running: flying", WorldManager.Flying);
        wm.Tick(1f);
        Check("a second at 0.2 flies 0.2", Mathf.Abs(before - wm.DistanceLeft - .2f) < 1e-4f);
        moveBackGround.speed = 0f;
        float d = wm.DistanceLeft;
        wm.Tick(1f);
        Check("standing still flies nothing", wm.DistanceLeft == d);
        Object.DestroyImmediate(wm.gameObject);
    }

    static bool Tick0(WorldManager wm, float before)
    {
        wm.Tick(0f);
        return wm.DistanceLeft == before;
    }

    // ---- 5. the dock popup ---------------------------------------------------------

    static void DockLine()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var cam = new GameObject("cam", typeof(Camera)).GetComponent<Camera>();
        var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        var popup = DockPopup.Create(new GameObject("dock").transform, font, cam);
        var ship = new GameObject("ship7").transform;
        PlayerPrefs.SetString(ShipId.OwnedKey(7), "True");
        popup.Show(7, ship, .3f, true, false, 0f, 0f);
        Check("no line before the skin row", popup.StartSpeedText == "");
        popup.ShowSkins(7, 0, 0f);
        Check("Gold Warden colour 1: START SPD 10 (" + popup.StartSpeedText + ")", popup.StartSpeedText == "START SPD 10");
        popup.ShowSkins(7, 4, 0f);
        Check("colour 5 (previewed, not owned): START SPD 30", popup.StartSpeedText == "START SPD 30");
        popup.Show(2, ship, .3f, false, false, 600f, 0f);
        Check("a locked ship's BUY popup has no line", popup.StartSpeedText == "");
        PlayerPrefs.SetString(ShipId.OwnedKey(2), "True");
        popup.Show(2, ship, .3f, true, false, 0f, 0f);
        popup.ShowSkins(2, 3, 0f);
        Check("Volt Viper colour 4: START SPD 15", popup.StartSpeedText == "START SPD 15");
        Check("the popup grows by the line's height", Mathf.Approximately(popup.CurrentHeight,
              DockPopup.Height + DockPopup.SkinRowHeight + DockPopup.StartSpeedLineHeight));
    }
}
