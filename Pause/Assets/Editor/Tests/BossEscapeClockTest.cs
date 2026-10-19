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
        Check("hearts = the old " + OldHearts + " + 5 = 10 (" + BossConfig.Hearts + ")",
              BossConfig.BaseHearts == OldHearts && BossConfig.ExtraHearts == 5 && BossConfig.Hearts == 10 && BossConfig.HitPoints == 10);
        Check("the hearts still split the hit points evenly (" + BossConfig.HitPoints + " hits, " + BossConfig.HeartWeight.ToString("F3") + " a heart)",
              Mathf.Abs(BossConfig.HeartWeight * BossConfig.Hearts - BossConfig.HitPoints) < 1e-4f && BossConfig.HitPoints == BossConfig.Hearts && Mathf.Abs(BossConfig.HeartWeight - 1f) < 1e-4f);
        Check("no boss has its own hearts or clock: the catalogue holds neither", typeof(BossDef).GetField("hearts") == null && typeof(BossDef).GetField("fightSeconds") == null);
        // the free shots of the fight fit its clock
        float[] t = new float[BossFreeShotAtoms.Count];
        Check("the free-shot atoms fit the 120 s clock", BossFreeShotAtoms.Roll(BossConfig.FightSeconds, t) == BossFreeShotAtoms.Count);
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

        BossEncounter.Begin(world, null);
        var e = BossEncounter.Instance;
        e.Step(Dt, 1f);
        for (int i = 0; i < 400 && e.State == BossEncounter.Phase.Intro; i++) e.Step(Dt, 1f);
        string name = e.Boss.artKey;
        Check(name + ": fights with " + e.HeartsLeft + " hearts (5 + 5) and a " + e.Remaining.ToString("F0") + " s clock",
              e.State == BossEncounter.Phase.Fight && e.HeartsLeft == BossConfig.Hearts && e.Actor.Hearts != null && e.Actor.Hearts.ShownCount == BossConfig.Hearts &&
              Mathf.Abs(e.Remaining - 120f) < .01f);

        float fought = 0f;
        while (e.State == BossEncounter.Phase.Fight && fought < 119.5f) { e.Step(Dt, 1f); fought += Dt; }
        Check(name + ": still here at " + fought.ToString("F1") + " s (not escaped before 2 min)",
              e.State == BossEncounter.Phase.Fight && !e.Destroyed && e.Remaining > .4f);
        while (e.State == BossEncounter.Phase.Fight && fought < 130f) { e.Step(Dt, 1f); fought += Dt; }
        Check(name + ": escapes at 120 s (" + fought.ToString("F1") + " s), not destroyed",
              e.State == BossEncounter.Phase.Outro && !e.Destroyed && e.Actor.State == BossActor.Mode.Retreating &&
              Mathf.Abs(fought - 120f) < .3f);
    }
}
