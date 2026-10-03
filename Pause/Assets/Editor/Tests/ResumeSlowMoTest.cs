using System.IO;
using System.Reflection;
using UnityEditor.SceneManagement;
using UnityEngine;

// Feature: "after speed 15, once the user pauses and comes back from pause ...
// slow time down by 40% for about half of a second and then the game ramps up
// to current speed".
//
// ResumeSlowMo holds timeScale at 0.6 for 0.5s of real time after a resume,
// then eases back to 1 over RampSeconds. moveBackGround asks it for the scale
// on every running frame and calls Freeze() on frozen ones.
public static class ResumeSlowMoTest
{
    static int fails;
    static void Check(string what, bool ok)
    {
        Debug.Log((ok ? "[RS] PASS  " : "[RS] FAIL  ") + what);
        if (!ok) fails++;
    }

    public static void Run()
    {
        TestHarness.Exit(Execute());
    }

    const float Dt = 1f / 60f;
    static float clock;

    public static int Execute()
    {
        fails = 0;
        using var sandbox = new TestHarness.Sandbox();
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        try
        {
            CurveShape();
            ResumeAtSpeedHoldsThenRamps();
            BelowFifteenHasNoEffect();
            RefreezeCancelsAndNextResumeRestarts();
            DeathCancels();
            CinematicTakesPriority();
            LaunchCountdownIsNotAResume();
            TutorialIsExcluded();
            OutOfPausesNeverTriggers();
            DrivesTheRealMoveBackGround();
            HookIsInMoveBackGround();
        }
        finally
        {
            ResumeSlowMo.ClockOverride = null;
            SetCinematic(false);
            ResumeSlowMo.ResetRun();
        }

        Debug.Log("[RS] failures: " + fails);
        return fails;
    }

    // ---- fixtures ------------------------------------------------------

    static void Fresh(float speed)
    {
        clock = 10f;
        ResumeSlowMo.ClockOverride = () => clock;
        ResumeSlowMo.ResetRun();
        SetCinematic(false);
        buttonClicks.playerDied = false;
        startMenu.youAreInTutorial = false;
        moveBackGround.speed = speed;
    }

    // One frame of moveBackGround's decision: running or frozen.
    static float Frame(bool running)
    {
        clock += Dt;
        Time.timeScale = running ? ResumeSlowMo.Apply(1f) : ResumeSlowMo.Freeze();
        return Time.timeScale;
    }

    static void Frames(int n, bool running) { for (int i = 0; i < n; i++) Frame(running); }

    // Launch touch, past the 1s countdown, then a pause (finger up).
    static void LaunchedAndPaused()
    {
        Frames(5, false);
        Frames(90, true);
        Frames(30, false);
    }

    static void SetCinematic(bool on)
    {
        var prop = typeof(ShipPowerController).GetProperty("CinematicClearActive",
            BindingFlags.Public | BindingFlags.Static);
        prop.GetSetMethod(true).Invoke(null, new object[] { on });
        typeof(ShipPowerController).GetField("exiting", BindingFlags.NonPublic | BindingFlags.Static)
            .SetValue(null, false);
    }

    // ---- cases -----------------------------------------------------------

    static void CurveShape()
    {
        Check("slow scale is 0.6 (40% slower)", Mathf.Approximately(ResumeSlowMo.SlowScale, 0.6f));
        Check("hold is half a second", Mathf.Approximately(ResumeSlowMo.HoldSeconds, 0.5f));
        Check("ramp is 0.3-0.5s (" + ResumeSlowMo.RampSeconds + ")",
              ResumeSlowMo.RampSeconds >= 0.3f && ResumeSlowMo.RampSeconds <= 0.5f);
        Check("ramp starts at the slow scale (no step)",
              Mathf.Approximately(ResumeSlowMo.ScaleAt(ResumeSlowMo.HoldSeconds), 0.6f));
        Check("ramp ends at 1", Mathf.Approximately(ResumeSlowMo.ScaleAt(ResumeSlowMo.TotalSeconds), 1f));
        float mid = ResumeSlowMo.ScaleAt(ResumeSlowMo.HoldSeconds + ResumeSlowMo.RampSeconds * 0.5f);
        Check("ramp is ease-out (past the linear midpoint halfway through: " + mid.ToString("F3") + ")", mid > 0.8f);
    }

    static void ResumeAtSpeedHoldsThenRamps()
    {
        Fresh(0.2f); // HUD 20
        LaunchedAndPaused();
        Check("frozen while paused", Time.timeScale == 0f);

        int slowFrames = 0;
        float scale = Frame(true);
        Check("resume frame runs at 0.6 (" + scale + ")", Mathf.Approximately(scale, 0.6f));
        while (Mathf.Approximately(scale, 0.6f) && slowFrames < 1000) { slowFrames++; scale = Frame(true); }
        float holdSeconds = slowFrames * Dt;
        Check("0.6 held for 0.5s +-1 frame (" + slowFrames + " frames, " + holdSeconds.ToString("F3") + "s)",
              Mathf.Abs(holdSeconds - 0.5f) <= Dt + 1e-4f);

        bool monotonic = true, everAbove = scale > 0.6f;
        float prev = scale;
        float rampStart = clock;
        int guard = 0;
        while (scale < 1f && guard++ < 1000)
        {
            scale = Frame(true);
            if (scale < prev - 1e-6f) monotonic = false;
            prev = scale;
        }
        float rampSeconds = clock - rampStart;
        Check("ramp rises monotonically", monotonic && everAbove);
        Check("reaches 1.0 within the ramp time (" + rampSeconds.ToString("F3") + "s)",
              scale == 1f && rampSeconds <= ResumeSlowMo.RampSeconds + Dt + 1e-4f);
        Check("effect is finished afterwards", !ResumeSlowMo.IsActive && Frame(true) == 1f);

        // Two moveBackGround instances call in one frame: same answer, no double edge.
        Frames(10, false);
        clock += Dt;
        float a = ResumeSlowMo.Apply(1f), b = ResumeSlowMo.Apply(1f);
        Check("several callers in one frame agree", Mathf.Approximately(a, 0.6f) && Mathf.Approximately(b, 0.6f));

        Fresh(0.15f); // HUD exactly 15
        LaunchedAndPaused();
        Check("HUD speed 15 qualifies", Mathf.Approximately(Frame(true), 0.6f));
    }

    static void BelowFifteenHasNoEffect()
    {
        Fresh(0.14f); // HUD 14
        LaunchedAndPaused();
        float s = Frame(true);
        Check("HUD 14: resume is plain 1.0 (" + s + ")", s == 1f && !ResumeSlowMo.IsActive);
        Frames(20, true);
        Check("and stays 1.0", Time.timeScale == 1f);
        Check("HUD mapping matches score/HudStyler: round(speed*100)",
              Mathf.RoundToInt(0.14f * 100f) == 14 && !ResumeSlowMo.SpeedQualifies);
    }

    static void RefreezeCancelsAndNextResumeRestarts()
    {
        Fresh(0.3f);
        LaunchedAndPaused();
        Frame(true);
        Frames(20, true); // 1/3 s into the hold
        Check("(effect running)", ResumeSlowMo.IsActive);
        float f = Frame(false);
        Check("lifting the finger refreezes immediately (timeScale 0)", f == 0f && !ResumeSlowMo.IsActive);
        Frames(30, false);

        Frame(true);
        Frames(26, true); // 0.45s after the second resume (0.78s after the first)
        Check("next resume restarts the hold from scratch (" + Time.timeScale + ")",
              Mathf.Approximately(Time.timeScale, 0.6f));

        // Refreeze during the ramp too.
        Frames(10, true);
        Check("(in the ramp)", Time.timeScale > 0.6f && Time.timeScale < 1f);
        Check("refreeze during the ramp is 0", Frame(false) == 0f);
        Check("and the next resume starts at 0.6 again", Mathf.Approximately(Frame(true), 0.6f));
    }

    static void DeathCancels()
    {
        Fresh(0.3f);
        LaunchedAndPaused();
        Frame(true);
        Frames(5, true);
        buttonClicks.playerDied = true;
        // moveBackGround's frozen branch is what runs on death.
        Check("death freezes (0) and cancels", Frame(false) == 0f && !ResumeSlowMo.IsActive);

        Fresh(0.3f);
        LaunchedAndPaused();
        Frame(true);
        buttonClicks.playerDied = true;
        float s = ResumeSlowMo.Apply(1f);
        Check("Apply() while dead cancels too", !ResumeSlowMo.IsActive && s == 1f);

        Fresh(0.3f);
        LaunchedAndPaused();
        Frame(true);
        ResumeSlowMo.Cancel();
        Check("Cancel() ends it at once", !ResumeSlowMo.IsActive && Frame(true) == 1f);
        buttonClicks.playerDied = false;
    }

    static void CinematicTakesPriority()
    {
        Fresh(0.3f);
        LaunchedAndPaused();
        Frame(true);
        Check("(effect running)", ResumeSlowMo.IsActive);

        SetCinematic(true);
        float s = Frame(true);
        Check("cinematic scale wins over the resume slow-mo (" + s + ")",
              Mathf.Approximately(s, ShipPowerController.CinematicSlowScale));
        Check("and the resume effect is dropped", !ResumeSlowMo.IsActive);

        SetCinematic(false);
        Check("after the cinematic it doesn't come back", Frame(true) == 1f);

        // A resume while the cinematic is already running never starts it.
        Fresh(0.3f);
        LaunchedAndPaused();
        SetCinematic(true);
        ResumeSlowMo.OnResume();
        Check("no effect starts under an active cinematic", !ResumeSlowMo.IsActive);
        SetCinematic(false);

        // The real moveBackGround applies the cinematic before ever asking us.
        Fresh(0.3f);
        LaunchedAndPaused();
        Frame(true);
        SetCinematic(true);
        var bg = new GameObject("bg", typeof(MeshRenderer)).AddComponent<moveBackGround>();
        bg.SendMessage("Update");
        Check("moveBackGround: cinematic scale while both are active",
              Mathf.Approximately(Time.timeScale, ShipPowerController.CinematicSlowScale));
        SetCinematic(false);
        Object.DestroyImmediate(bg.gameObject);
    }

    static void LaunchCountdownIsNotAResume()
    {
        Fresh(0.5f); // fast enough to qualify, to isolate the launch rule
        Frames(30, false);
        float s = Frame(true);
        Check("the launch touch is not a resume (" + s + ")", s == 1f && !ResumeSlowMo.IsActive);

        Frames(20, true);        // into READY
        Frames(15, false);       // lift during the countdown
        s = Frame(true);
        Check("retouching during the 1s countdown is not a resume", s == 1f && !ResumeSlowMo.IsActive);

        Frames(60, true);        // countdown done
        Frames(10, false);
        Check("after the countdown a real resume does trigger", Mathf.Approximately(Frame(true), 0.6f));
    }

    static void TutorialIsExcluded()
    {
        Fresh(0.3f);
        startMenu.youAreInTutorial = true;
        LaunchedAndPaused();
        Check("no effect in the tutorial", Frame(true) == 1f && !ResumeSlowMo.IsActive);
        startMenu.youAreInTutorial = false;
    }

    static void OutOfPausesNeverTriggers()
    {
        // With no pauses the frozen branch never runs, so there is no edge.
        Fresh(0.3f);
        Frames(5, false);
        Frames(200, true);
        Check("a world that never freezes never slows", Time.timeScale == 1f && !ResumeSlowMo.IsActive);
    }

    static void DrivesTheRealMoveBackGround()
    {
        // pauseCounter > 0 with no touch (batch mode) is the frozen branch;
        // pauseCounter 0 is the running branch -- the same two lines a touch
        // toggles between.
        Fresh(0.3f);
        var go = new GameObject("bg", typeof(MeshRenderer));
        go.GetComponent<MeshRenderer>().sharedMaterial = new Material(Shader.Find("Sprites/Default"));
        var bg = go.AddComponent<moveBackGround>();
        void Tick(int pauses)
        {
            clock += Dt;
            score.pauseCounter = pauses;
            try { bg.SendMessage("Update"); } catch (System.Exception) { }
        }

        Tick(3);
        Check("moveBackGround frozen branch gives 0", Time.timeScale == 0f);
        for (int i = 0; i < 90; i++) Tick(0); // launch + countdown
        Check("launch runs at 1", Time.timeScale == 1f);
        for (int i = 0; i < 10; i++) Tick(3);
        moveBackGround.speed = 0.3f;
        Tick(0);
        Check("moveBackGround resume runs at 0.6", Mathf.Approximately(Time.timeScale, 0.6f));
        Tick(3);
        Check("moveBackGround refreeze is 0", Time.timeScale == 0f);

        moveBackGround.speed = 0.3f;
        Tick(0);
        buttonClicks.playerDied = true;
        Tick(0);
        Check("moveBackGround on death: 0 and cancelled", Time.timeScale == 0f && !ResumeSlowMo.IsActive);
        buttonClicks.playerDied = false;
        Object.DestroyImmediate(go);
    }

    static void HookIsInMoveBackGround()
    {
        string src = File.ReadAllText("Assets/Scripts/Gameplay/moveBackGround.cs");
        Check("moveBackGround applies ResumeSlowMo on running frames",
              src.Contains("Time.timeScale = ResumeSlowMo.Apply(1f) * WorldTimeFx.Scale;"));  // hit-stop / Time Bubble on top
        Check("moveBackGround reports freezes", src.Contains("Time.timeScale = ResumeSlowMo.Freeze();"));
        int cinematic = src.IndexOf("ShipPowerController.CinematicTimeScale");
        int apply = src.IndexOf("ResumeSlowMo.Apply");
        Check("the cinematic branch comes first", cinematic >= 0 && cinematic < apply);
    }
}
