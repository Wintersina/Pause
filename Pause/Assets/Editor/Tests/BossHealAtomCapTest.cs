using UnityEditor.SceneManagement;
using UnityEngine;

// "During boss fights no more than 2 health atoms should appear."
// The green heal atom is capped at BossEncounter.MaxHealAtomsPerFight per
// encounter (intro through boss death), whatever releases it; the count
// resets for the next boss, and outside a boss nothing is capped.
public static class BossHealAtomCapTest
{
    static int fails;
    static void Check(string what, bool ok)
    {
        Debug.Log((ok ? "[BHC] PASS  " : "[BHC] FAIL  ") + what);
        if (!ok) fails++;
    }

    public static void Run() { TestHarness.Exit(Execute()); }

    public static int Execute()
    {
        fails = 0;
        using var sandbox = new TestHarness.Sandbox();
        try
        {
            NormalLevelUncapped();
            CapHoldsAcrossAnEncounterAndResets();
        }
        finally
        {
            BossEncounter.ResetRun();
            buttonClicks.playerDied = false;
            collisionDetection.lifeCounter = 0;
            moveBackGround.speed = 0f;
        }
        Debug.Log("[BHC] failures: " + fails);
        return fails;
    }

    static int HealAtoms()
    {
        int n = 0;
        foreach (var t in Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (t != null && t.name == HealAtom.ObjectName) n++;
        return n;
    }

    static HealAtomSpawner NewSpawner()
    {
        var sp = new GameObject("~HealSp").AddComponent<HealAtomSpawner>();
        sp.maxPerWorld = 100;
        sp.chance = 1f;
        sp.firstChanceAfter = 0f;
        sp.gapSeconds = new Vector2(.1f, .1f);
        return sp;
    }

    // Steps the spawner for `seconds`; AtomSpacing reset each frame.
    static void StepFor(HealAtomSpawner sp, float seconds)
    {
        for (float t = 0f; t < seconds; t += .1f)
        {
            AtomSpacing.Reset();
            sp.Step(.1f);
        }
    }

    static void Setup()
    {
        BossEncounter.ResetRun();
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        buttonClicks.playerDied = false;
        score.pauseCounter = 0;
        moveBackGround.speed = .37f;
        Time.timeScale = 1f;
        collisionDetection.lifeCounter = 3;
        var camGo = new GameObject("Main Camera") { tag = "MainCamera" };
        var cam = camGo.AddComponent<Camera>();
        cam.orthographic = true;
        cam.orthographicSize = 5f;
        camGo.transform.position = new Vector3(0f, 0f, -10f);
        PlayerPrefs.SetInt(DeveloperUnlocks.EnabledKey, 0);
    }

    static void NormalLevelUncapped()
    {
        Setup();
        var sp = NewSpawner();
        StepFor(sp, 2f);
        Check("normal level: heal atoms are not capped (" + HealAtoms() + " released)", HealAtoms() > BossEncounter.MaxHealAtomsPerFight);
        Check("normal level: reserve always succeeds", BossEncounter.TryReserveHealAtom());
    }

    static void CapHoldsAcrossAnEncounterAndResets()
    {
        for (int world = 0; world < 2; world++)
        {
            Setup();
            var sp = NewSpawner();
            PlayerPrefs.SetInt(WorldManager.PrefsCurrentWorld, world);
            BossEncounter.Begin(world, null);
            var e = BossEncounter.Instance;
            e.Step(.1f, 1f);
            for (int i = 0; i < 20 && HealAtoms() < 99; i++) { StepFor(sp, 1f); e.Step(.1f, 1f); }
            Check("boss " + world + ": intro/fight released at most 2 heal atoms (" + HealAtoms() + ")",
                  HealAtoms() == BossEncounter.MaxHealAtomsPerFight && e.HealAtomsReleased == 2);
            // the fight plan's heal and every later attempt are refused too
            Check("boss " + world + ": a third reserve is refused", !BossEncounter.TryReserveHealAtom());
            // the next boss gets a fresh allowance
            BossEncounter.Begin(world, null); // refused while running; same encounter
            Check("boss " + world + ": still capped while the same encounter runs", !BossEncounter.TryReserveHealAtom());
            BossEncounter.ResetRun();
            BossEncounter.Begin(world, null);
            Check("boss " + world + ": next encounter resets the count",
                  BossEncounter.Instance.HealAtomsReleased == 0 && BossEncounter.TryReserveHealAtom());
        }
    }
}
