using System.Collections.Generic;
using System.Reflection;
using UnityEditor.SceneManagement;
using UnityEngine;

// Bug: moveBackGround sits on both side walls, and every instance ramped the
// shared static speed in its own Update, so the ramp ran once per wall per
// frame. WorldManager also only gave the world's ramp/cap to one of them.
// SpeedRamp now owns the ramp and ticks once per frame.
//
// These tests drive the real moveBackGround.Update with a simulated frame
// counter and fixed dt, since edit mode can't advance Time.frameCount.
public static class SpeedRampTest
{
    static int fails;
    static void Check(string what, bool ok)
    {
        Debug.Log((ok ? "[SR] PASS  " : "[SR] FAIL  ") + what);
        if (!ok) fails++;
    }

    public static void Run()
    {
        TestHarness.Exit(Execute());
    }

    const float Dt = 1f / 60f;
    static int frame;
    static float dt = Dt;
    static float clock;

    public static int Execute()
    {
        fails = 0;
        using var sandbox = new TestHarness.Sandbox();

        try
        {
            ScenesHaveTwoWalls();
            OneTickPerFrame();
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            SameRateForOneTwoThreeWalls();
            NeverExceedsCap();
            FrozenDoesNotRamp();
            WorldTransitionResetsSpeed();
        }
        finally
        {
            SpeedRamp.FrameOverride = null;
            SpeedRamp.DeltaOverride = null;
            SpeedRamp.ResetFrameGuard();
            ResumeSlowMo.ClockOverride = null;
            ResumeSlowMo.ResetRun();
        }

        Debug.Log("[SR] failures: " + fails);
        return fails;
    }

    // ---- fixtures ------------------------------------------------------

    static void Fresh()
    {
        frame = 1000;
        dt = Dt;
        clock = 10f;
        SpeedRamp.FrameOverride = () => frame;
        SpeedRamp.DeltaOverride = () => dt;
        SpeedRamp.ResetFrameGuard();
        ResumeSlowMo.ClockOverride = () => clock;
        ResumeSlowMo.ResetRun();
        buttonClicks.playerDied = false;
        startMenu.youAreInTutorial = false;
        score.pauseCounter = 0; // running without touch in batch mode
        moveBackGround.speed = 0f;
        ShipStartSpeed.EquippedHudOverride = () => ShipStartSpeed.StockHud;
    }

    static List<moveBackGround> MakeWalls(int n, WorldTheme theme)
    {
        var walls = new List<moveBackGround>();
        var mat = new Material(Shader.Find("Sprites/Default"));
        for (int i = 0; i < n; i++)
        {
            var go = new GameObject("~wall" + i, typeof(MeshRenderer));
            go.GetComponent<MeshRenderer>().sharedMaterial = mat;
            var bg = go.AddComponent<moveBackGround>();
            bg.speedRampPerSecond = theme.speedRampPerSecond;
            bg.maxSpeed = SpeedRamp.Cap;
            walls.Add(bg);
        }
        return walls;
    }

    static void Destroy(List<moveBackGround> walls)
    {
        foreach (var w in walls) Object.DestroyImmediate(w.gameObject);
    }

    // One frame: every wall runs its Update, as Unity would.
    static void Step(IList<moveBackGround> walls)
    {
        frame++;
        clock += dt;
        foreach (var w in walls) TestHarness.Send(w, "Update");
    }

    static void Simulate(IList<moveBackGround> walls, float seconds, System.Action<float> eachFrame = null)
    {
        int frames = Mathf.RoundToInt(seconds / dt);
        for (int i = 0; i < frames; i++)
        {
            Step(walls);
            eachFrame?.Invoke(moveBackGround.speed);
        }
    }

    // ---- cases -----------------------------------------------------------

    static void ScenesHaveTwoWalls()
    {
        foreach (var scene in new[] { "gameS1", "tutorialS5" })
        {
            EditorSceneLoader.Open(scene, OpenSceneMode.Single);
            var walls = Object.FindObjectsByType<moveBackGround>(FindObjectsSortMode.None);
            var names = new List<string>();
            foreach (var w in walls) names.Add(w.name);
            names.Sort();
            Debug.Log("[SR] " + scene + " moveBackGround instances: " + string.Join(", ", names));
            Check(scene + " has the two walls the fix is about (" + walls.Length + ")", walls.Length == 2);
            if (scene == "tutorialS5")
                Check("tutorialS5 walls ramp at 0.0023/s (the pre-fix felt pace)",
                      System.Array.TrueForAll(walls, w => Mathf.Approximately(w.speedRampPerSecond, 0.0023f)));
        }
    }

    static void OneTickPerFrame()
    {
        Fresh();
        SpeedRamp.Tick(1f, 10f);
        SpeedRamp.Tick(1f, 10f);
        SpeedRamp.Tick(1f, 10f);
        Check("three ticks in one frame advance once", Mathf.Approximately(moveBackGround.speed, Dt));
        frame++;
        SpeedRamp.Tick(1f, 10f);
        Check("the next frame advances again", Mathf.Approximately(moveBackGround.speed, 2f * Dt));
    }

    static void SameRateForOneTwoThreeWalls()
    {
        for (int w = 0; w < WorldManager.LiveWorldCount; w++)
        {
            var theme = WorldManager.Worlds[w];
            const float T = 60f;
            float expected = theme.speedRampPerSecond * T;
            var results = new float[3];
            for (int n = 1; n <= 3; n++)
            {
                Fresh();
                var walls = MakeWalls(n, theme);
                Simulate(walls, T);
                results[n - 1] = moveBackGround.speed;
                Destroy(walls);
            }
            Check(theme.displayName + ": speed after 60s is the same with 1/2/3 walls (" +
                  results[0].ToString("F5") + " / " + results[1].ToString("F5") + " / " + results[2].ToString("F5") + ")",
                  Mathf.Abs(results[0] - results[1]) < 1e-6f && Mathf.Abs(results[0] - results[2]) < 1e-6f);
            Check(theme.displayName + ": speed after 60s = startSpeed + rate*t (" +
                  results[1].ToString("F5") + " vs " + expected.ToString("F5") + ")",
                  Mathf.Abs(results[1] - expected) < 1e-4f);
        }
    }

    // Seconds to HUD speed 15 before the fix, when both walls ramped
    // (world rate + the scene's 0.002/s). The retuned rates keep this pace.
    static readonly float[] FeltSecondsTo15 = { 46.0f, 43.9f, 42.0f, 39.7f };
    // The ramp's own benchmark (it predates the 120s distance-based level:
    // WorldManager.BaselineWorldSeconds): every world eases into the one cap
    // (SpeedRamp.Cap, HUD 35) inside it.
    const float LevelSeconds = 180f;

    // (Was NeverExceedsWorldMax, against the per-world caps 38/40/42/44.
    // Every world now shares SpeedRamp.Cap.)
    static void NeverExceedsCap()
    {
        for (int w = 0; w < WorldManager.LiveWorldCount; w++)
        {
            var theme = WorldManager.Worlds[w];
            Fresh();
            dt = 0.05f; // coarser steps keep the 450s run quick; the cap doesn't care
            var walls = MakeWalls(2, theme);
            float peak = 0f, reachedAt = -1f, at15 = -1f, t = 0f;
            Simulate(walls, 450f, s =>
            {
                t += dt;
                peak = Mathf.Max(peak, s);
                if (reachedAt < 0f && s >= SpeedRamp.Cap) reachedAt = t;
                if (at15 < 0f && Mathf.RoundToInt(s * 100f) >= ResumeSlowMo.MinHudSpeed) at15 = t;
            });
            Debug.Log("[SR] " + theme.displayName + ": rate " + theme.speedRampPerSecond + "/s, cap " + SpeedRamp.Cap +
                      ", reaches the cap at " + reachedAt.ToString("F1") + "s, HUD 15 at " + at15.ToString("F1") +
                      "s, speed at 180s " + SpeedRamp.SpeedAfter(0f, theme.speedRampPerSecond, SpeedRamp.Cap, 180f).ToString("F3"));
            Check(theme.displayName + ": HUD 15 within 2s of the pre-fix " + FeltSecondsTo15[w] + "s (" + at15.ToString("F1") + "s)",
                  at15 >= 0f && Mathf.Abs(at15 - FeltSecondsTo15[w]) <= 2f);
            Check(theme.displayName + ": reaches the cap inside " + LevelSeconds + "s of flight (" + reachedAt.ToString("F1") + "s)",
                  reachedAt >= 0f && reachedAt <= LevelSeconds);
            Check(theme.displayName + ": speed never exceeds the cap " + SpeedRamp.Cap + " (peak " + peak.ToString("F4") + ")",
                  peak <= SpeedRamp.Cap + 1e-6f);
            Check(theme.displayName + ": speed settles exactly on the cap", Mathf.Approximately(moveBackGround.speed, SpeedRamp.Cap));
            Destroy(walls);
        }
    }

    static void FrozenDoesNotRamp()
    {
        Fresh();
        var walls = MakeWalls(2, WorldManager.Worlds[0]);
        Simulate(walls, 10f);
        float before = moveBackGround.speed;

        score.pauseCounter = 3; // no touch in batch mode: the frozen branch
        Simulate(walls, 10f);
        Check("frozen: timeScale is 0", Time.timeScale == 0f);
        Check("frozen: speed holds (" + before.ToString("F5") + " -> " + moveBackGround.speed.ToString("F5") + ")",
              moveBackGround.speed == before);

        score.pauseCounter = 0;
        buttonClicks.playerDied = true;
        Simulate(walls, 10f);
        Check("dead: speed holds", moveBackGround.speed == before);
        buttonClicks.playerDied = false;

        // timeScale 0 means deltaTime 0: a tick then changes nothing.
        dt = 0f;
        frame++;
        SpeedRamp.Tick(1f, 10f);
        Check("a zero-dt tick changes nothing", moveBackGround.speed == before);
        dt = Dt;

        Simulate(walls, 10f);
        Check("running again resumes the ramp",
              Mathf.Abs(moveBackGround.speed - (before + WorldManager.Worlds[0].speedRampPerSecond * 10f)) < 1e-4f);
        Destroy(walls);
    }

    static void WorldTransitionResetsSpeed()
    {
        EditorSceneLoader.Open("gameS1", OpenSceneMode.Single);
        Fresh();
        WorldManager.CurrentIndex = 0;

        var walls = new List<moveBackGround>(Object.FindObjectsByType<moveBackGround>(FindObjectsSortMode.None));
        var applyDifficulty = typeof(WorldManager).GetMethod("ApplyDifficulty", BindingFlags.NonPublic | BindingFlags.Static);
        applyDifficulty.Invoke(null, new object[] { WorldManager.Worlds[0] });
        bool allSpace = walls.TrueForAll(w =>
            w.speedRampPerSecond == WorldManager.Worlds[0].speedRampPerSecond && w.maxSpeed == SpeedRamp.Cap);
        Check("ApplyDifficulty gives every wall the world's ramp and the one cap", walls.Count == 2 && allSpace);

        Simulate(walls, 30f);
        Check("gameS1's two walls ramp at Space's rate",
              Mathf.Abs(moveBackGround.speed - WorldManager.Worlds[0].speedRampPerSecond * 30f) < 1e-4f);

        var wm = new GameObject("~WorldManagerTest").AddComponent<WorldManager>();
        try { wm.Advance(); }
        catch (System.Exception e) { Debug.Log("[SR] Advance side effect threw (ignored): " + e.Message); }

        Check("world transition moves to Frost", WorldManager.CurrentIndex == 1);
        Check("world transition resets speed to 0", moveBackGround.speed == 0f);
        var frost = WorldManager.Worlds[1];
        Check("every wall picks up Frost's ramp, the cap unchanged",
              walls.TrueForAll(w => w.speedRampPerSecond == frost.speedRampPerSecond && w.maxSpeed == SpeedRamp.Cap));

        Simulate(walls, 30f);
        Check("after the transition the ramp restarts from 0 at Frost's rate",
              Mathf.Abs(moveBackGround.speed - frost.speedRampPerSecond * 30f) < 1e-4f);

        Object.DestroyImmediate(wm.gameObject);
    }
}
