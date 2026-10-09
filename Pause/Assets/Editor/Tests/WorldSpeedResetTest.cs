using System.Collections.Generic;
using System.Reflection;
using UnityEditor.SceneManagement;
using UnityEngine;

// Entering a new world resets the game speed to the selected ship's start
// speed (ShipStartSpeed via WorldManager.ArrivalSpeed / RunStartSpeed: the
// same value a fresh run starts at), then the ramp climbs again from there.
//
// Every world change is WorldManager.Advance: the portal calls Advance(),
// the planetfall Advance(false) under its clouds, and the last world's
// lift-off StartLoop() (a loop is a new world: no louder arrival speed).
// Score, distance clock, hearts carry; nothing resets mid-world or mid-boss.
public static class WorldSpeedResetTest
{
    static int fails;
    static void Check(string what, bool ok)
    {
        Debug.Log((ok ? "[WSR] PASS  " : "[WSR] FAIL  ") + what);
        if (!ok) fails++;
    }

    public static void Run() { TestHarness.Exit(Execute()); }

    const float Dt = 1f / 60f;
    static int frame;
    static WorldManager wm;
    static List<moveBackGround> walls;

    public static int Execute()
    {
        fails = 0;
        using var sandbox = new TestHarness.Sandbox();
        try
        {
            // (fast ship, slow ship, skins that change start speed)
            foreach (int hud in new[] { 0, 5, 10, 15, 20, 30 })
            {
                Transitions(hud);
            }
            RealShipsAndSkins();
            NotMidWorld();
            Boosts();
        }
        finally
        {
            ShipStartSpeed.EquippedHudOverride = null;
            SpeedRamp.FrameOverride = null;
            SpeedRamp.DeltaOverride = null;
            SpeedRamp.ResetFrameGuard();
            SpeedRamp.ResetBoost();
            BossEncounter.ResetRun();
            RunLoop.Reset();
            PortalPressure.Reset();
            buttonClicks.playerDied = false;
            moveBackGround.speed = 0f;
        }
        Debug.Log("[WSR] failures: " + fails);
        return fails;
    }

    static void Fresh(int hud)
    {
        EditorSceneLoader.Open("gameS1", OpenSceneMode.Single);
        BossEncounter.ResetRun();
        RunLoop.Reset();
        PortalPressure.Reset();
        SpeedRamp.ResetBoost();
        buttonClicks.playerDied = false;
        startMenu.youAreInTutorial = false;
        score.pauseCounter = 0;
        frame = 5000;
        SpeedRamp.FrameOverride = () => frame;
        SpeedRamp.DeltaOverride = () => Dt;
        SpeedRamp.ResetFrameGuard();
        ShipStartSpeed.EquippedHudOverride = () => hud;
        walls = new List<moveBackGround>(Object.FindObjectsByType<moveBackGround>(FindObjectsSortMode.None));
        wm = Object.FindFirstObjectByType<WorldManager>();
        if (wm == null) wm = new GameObject("~wm").AddComponent<WorldManager>();
        WorldManager.CurrentIndex = 0;
        RunLoop.StartWorld = 0;
        // a fresh run's own start
        moveBackGround.speed = WorldManager.RunStartSpeed(0f);
        foreach (var w in walls) { w.speedRampPerSecond = WorldManager.Worlds[0].speedRampPerSecond; w.maxSpeed = SpeedRamp.Cap; }
    }

    static void Fly(float seconds)
    {
        int n = Mathf.RoundToInt(seconds / Dt);
        for (int i = 0; i < n; i++)
        {
            frame++;
            foreach (var w in walls) TestHarness.Send(w, "Update");
        }
    }

    static void OpenPortal(bool open)
    {
        typeof(WorldManager).GetField("portalOpen", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(wm, open);
    }

    static void Enter(string how)
    {
        try
        {
            if (how == "portal") wm.Advance();
            else if (how == "planetfall") wm.Advance(false);
            else wm.StartLoop();
        }
        catch (System.Exception e) { Debug.Log("[WSR] " + how + " presentation threw (ignored): " + e.Message); }
    }

    static void Transitions(int hud)
    {
        float def = Mathf.Min(hud / 100f, SpeedRamp.Cap);
        foreach (string how in new[] { "portal", "planetfall", "loop" })
        {
            Fresh(hud);
            string tag = "HUD " + hud + " " + how + ": ";
            int from = 1;
            if (how == "loop") from = WorldManager.LastLiveWorld;
            WorldManager.CurrentIndex = from;
            // flown fast, a limit break on, hearts and score banked
            moveBackGround.speed = .33f;
            collisionDetection.lifeCounter = 3;
            RunScore.BeginRun(true, false);
            long score0 = RunScore.Total;
            OpenPortal(true);
            moveBackGround.speed = .33f;
            Enter(how);
            Check(tag + "world changed", WorldManager.CurrentIndex == (how == "loop" ? 0 : from + 1));
            Check(tag + "speed == the ship's fresh-run start (" + moveBackGround.speed + " vs " + def + ")",
                  Mathf.Approximately(moveBackGround.speed, def));
            Check(tag + "== WorldManager.RunStartSpeed(0)", Mathf.Approximately(moveBackGround.speed, WorldManager.RunStartSpeed(0f)));
            Check(tag + "hearts untouched (" + collisionDetection.lifeCounter + ")", collisionDetection.lifeCounter == 3);
            Check(tag + "score only gains the world bonus (" + score0 + " -> " + RunScore.Total + ")",
                  RunScore.Total == score0 + ScoreRules.WorldClearedPoints(from, 0));
            Check(tag + "distance clock restarted for the new world",
                  Mathf.Approximately(wm.DistanceLeft, wm.WorldDistance) && wm.HasLevelClock);
            float s0 = moveBackGround.speed;
            Fly(5f);
            Check(tag + "then it ramps normally (" + s0 + " -> " + moveBackGround.speed + ")",
                  def >= SpeedRamp.Cap - 1e-6f ? moveBackGround.speed <= SpeedRamp.Cap + 1e-6f
                                                : moveBackGround.speed > s0 && moveBackGround.speed <= SpeedRamp.Cap + 1e-6f);
            // deterministic: the same again gives the same number
            float a = moveBackGround.speed;
            Fresh(hud);
            WorldManager.CurrentIndex = from;
            moveBackGround.speed = .33f;
            OpenPortal(true);
            Enter(how);
            Fly(5f);
            Check(tag + "deterministic", Mathf.Approximately(a, moveBackGround.speed));
        }
    }

    // Real ship ids and colours (no override): fast ship, slow ship, skins.
    static void RealShipsAndSkins()
    {
        ShipStartSpeed.EquippedHudOverride = null;
        foreach (var (id, skin) in new[] { (1, 0), (1, 3), (2, 4), (5, 0), (5, 4), (7, 2), (15, 1) })
        {
            float want = Mathf.Min(SpeedRamp.Cap, ShipStartSpeed.SpeedFor(id, skin));
            int idc = id, skc = skin;
            Fresh(0);
            ShipStartSpeed.EquippedHudOverride = () => ShipStartSpeed.HudFor(idc, skc);
            WorldManager.CurrentIndex = 1;
            moveBackGround.speed = .30f;
            OpenPortal(true);
            Enter("portal");
            Check("ship " + id + " colour " + (skin + 1) + ": new world starts at its START SPD " + ShipStartSpeed.HudFor(id, skin),
                  Mathf.Approximately(moveBackGround.speed, want));
        }
    }

    // Nothing resets while a world is simply flown, nor during a boss.
    static void NotMidWorld()
    {
        Fresh(10);
        float prev = moveBackGround.speed;
        bool never = true;
        for (int i = 0; i < 20; i++)
        {
            Fly(2f);
            never &= moveBackGround.speed >= prev - 1e-6f;
            prev = moveBackGround.speed;
        }
        Check("flying a world: speed only ever climbs, no reset (" + prev + ")", never && prev > .10f);
        float before = moveBackGround.speed;
        wm.Tick(3f);
        Check("a level tick (clock/boss warning) does not touch the speed", Mathf.Approximately(moveBackGround.speed, before));
    }

    // A limit-break boost in progress does not ride into the next world.
    static void Boosts()
    {
        Fresh(0);
        WorldManager.CurrentIndex = 1;
        moveBackGround.speed = SpeedRamp.Cap;
        SpeedRamp.AddBoost();
        SpeedRamp.AddBoost();
        Fly(1f);
        Check("a boost is up before the transition", SpeedRamp.Boost > 0f && moveBackGround.speed > SpeedRamp.Cap);
        OpenPortal(true);
        Enter("portal");
        Check("new world: boost gone, speed at the start speed", SpeedRamp.Boost == 0f && Mathf.Approximately(moveBackGround.speed, 0f));
    }
}
