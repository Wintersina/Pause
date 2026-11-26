using UnityEditor.SceneManagement;
using UnityEngine;

// Feature: "lengthen all the boss healths by 4 hearts, and the amount of time
// they take to escape ... boss should be there for 2 min before escaping, this
// rule should be for all bosses."
//
// Every boss (all worlds) wears 5 hearts + 5 (10) and stays EscapeSeconds
// (120) before it escapes: one rule, from BossConfig, not per boss.
public static class BossEscapeClockTest
{
    static int fails;
    static void Check(string what, bool ok)
    {
        Debug.Log((ok ? "[BOSSESCAPE] PASS  " : "[BOSSESCAPE] FAIL  ") + what);
        if (!ok) fails++;
    }

    public static void Run() { TestHarness.Exit(Execute()); }

    const float Dt = .1f;

    public static int Execute()
    {
        fails = 0;
        using var sandbox = new TestHarness.Sandbox();
        try
        {
            TheNumbers();
            SelectedStartIsHeld();
            for (int w = 0; w < BossCatalog.All.Length; w++) EveryBossHasTheRule(w);
        }
        finally
        {
            BossEncounter.ResetRun();
            buttonClicks.playerDied = false;
            moveBackGround.speed = 0f;
        }
        Debug.Log("[BOSSESCAPE] failures: " + fails);
        return fails;
    }

    static void TheNumbers()
    {
        const int OldHearts = 5;
        Check("the escape clock is one named constant, 120 s (2 minutes)", BossConfig.EscapeSeconds == 120f && BossConfig.FightSeconds == BossConfig.EscapeSeconds);
        Check("starting hearts = the old " + OldHearts + " + 8 = 13 (" + BossConfig.Hearts + ")",
              BossConfig.BaseHearts == OldHearts && BossConfig.ExtraHearts == 8 && BossConfig.HeartsAt(0, 0) == 13 && BossConfig.HitPoints == BossConfig.Hearts);
        Check("the hearts still split the hit points evenly (" + BossConfig.HitPoints + " hits, " + BossConfig.HeartWeight.ToString("F3") + " a heart)",
              Mathf.Abs(BossConfig.HeartWeight * BossConfig.Hearts - BossConfig.HitPoints) < 1e-4f && BossConfig.HitPoints == BossConfig.Hearts && Mathf.Abs(BossConfig.HeartWeight - 1f) < 1e-4f);
        Check("no boss has its own hearts or clock: the catalogue holds neither", typeof(BossDef).GetField("hearts") == null && typeof(BossDef).GetField("fightSeconds") == null);
        for (int start = 0; start < WorldManager.Worlds.Length; start++)
        for (int step = 0; step < WorldManager.Worlds.Length; step++)
        {
            int world = (start + step) % WorldManager.Worlds.Length;
            Check("start " + start + ", world " + world + ": health grows from the selected starting level, including wraps",
                BossConfig.HeartsAt(world, start) == 13 + step && BossConfig.HeartsAt(world, start, 1) == 20 + step);
        }
        // the free shots of the fight fit its clock
        float[] t = new float[BossFreeShotAtoms.Count];
        Check("the free-shot atoms fit the 120 s clock", BossFreeShotAtoms.Roll(BossConfig.FightSeconds, t) == BossFreeShotAtoms.Count);
    }

    static void SelectedStartIsHeld()
    {
        using var held = new TestHarness.Sandbox();
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        BossEncounter.ResetRun(); WorldManager.ClearReplayWorld(); RunLoop.Reset();
        PlayerPrefs.SetInt(DeveloperUnlocks.EnabledKey, 0);
        PlayerPrefs.SetInt(WorldManager.PrefsHighestWorld, 6); PlayerStartWorld.Select(3);
        var manager = new GameObject("~HeldHealthStart").AddComponent<WorldManager>();
        manager.SendMessage("Awake"); manager.HoldStartWorld();
        Check("dock-selected Ember starts at thirteen hearts; the next world has fourteen",
            WorldManager.RunStart == 3 && BossConfig.HeartsForWorld(3) == 13 && BossConfig.HeartsForWorld(4) == 14 && BossConfig.HeartsForWorld(0) == 17);
        PlayerStartWorld.Select(0);
        Check("changing a saved preference cannot move the active run's health curve", BossConfig.HeartsForWorld(3) == 13);
        Object.DestroyImmediate(manager.gameObject);
    }

    static void EveryBossHasTheRule(int world)
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
        score.pauseCounter = 0;
        moveBackGround.speed = .37f;
        Time.timeScale = 1f;
        PlayerPrefs.SetInt(DeveloperUnlocks.EnabledKey, 0);
        PlayerPrefs.SetInt(WorldManager.PrefsCurrentWorld, world);

        RunScore.EndRun(RunScore.RunId); RunScore.BeginRun(true, true); RunScore.OnElite(Vector3.zero, 20000);
        BossEncounter.Begin(world, null);
        var e = BossEncounter.Instance;
        e.Step(Dt, 1f);
        for (int i = 0; i < 400 && e.State == BossEncounter.Phase.Intro; i++) e.Step(Dt, 1f);
        string name = e.Boss.artKey;
        Check(name + ": fights with " + e.HeartsLeft + " hearts (5 + 5) and a " + e.Remaining.ToString("F0") + " s clock",
              e.State == BossEncounter.Phase.Fight && e.HeartsLeft == BossConfig.Hearts && e.Actor.Hearts != null && e.Actor.Hearts.ShownCount == BossConfig.Hearts &&
              Mathf.Abs(e.Remaining - 120f) < .01f);

        long scoreBeforeEscape = RunScore.Total;
        float fought = 0f;
        while (e.State == BossEncounter.Phase.Fight && fought < 119.5f) { e.Step(Dt, 1f); fought += Dt; }
        Check(name + ": still here at " + fought.ToString("F1") + " s (not escaped before 2 min)",
              e.State == BossEncounter.Phase.Fight && !e.Destroyed && e.Remaining > .4f);
        while (e.State == BossEncounter.Phase.Fight && fought < 130f) { e.Step(Dt, 1f); fought += Dt; }
        Check(name + ": escapes at 120 s (" + fought.ToString("F1") + " s), not destroyed",
              e.State == BossEncounter.Phase.Outro && !e.Destroyed && e.Actor.State == BossActor.Mode.Retreating &&
              Mathf.Abs(fought - 120f) < .3f);
        Check(name + ": an escape costs exactly 10000 points", RunScore.Total == scoreBeforeEscape - 10000);
        long afterEscape = RunScore.Total; e.Step(Dt, 1f);
        Check(name + ": the escape penalty is not charged again during the outro", RunScore.Total == afterEscape);
    }
}
