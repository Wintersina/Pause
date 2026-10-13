using System.Reflection;
using UnityEditor.SceneManagement;
using UnityEngine;

// "The red atom (refills pauses) is capped at 2 per boss fight."
// BossEncounter.TryReserveRedAtom allows MaxRedAtomsPerFight per encounter
// (intro through boss death / escape), from every source (the fight plan's
// free-shot atoms, spawnGoodStuff's normal red atom); resets with the next
// encounter; outside a boss nothing is capped.
public static class BossRedAtomCapTest
{
    static int fails;
    static void Check(string what, bool ok)
    {
        Debug.Log((ok ? "[BRC] PASS  " : "[BRC] FAIL  ") + what);
        if (!ok) fails++;
    }

    public static void Run() { TestHarness.Exit(Execute()); }

    const BindingFlags Inst = BindingFlags.NonPublic | BindingFlags.Instance;

    public static int Execute()
    {
        fails = 0;
        using var sandbox = new TestHarness.Sandbox();
        EnemyDensity.Disabled = true;
        try
        {
            ConstantsAndBalance();
            NormalLevelUncapped();
            EncountersCapAtTwoAndReset();
        }
        finally
        {
            BossEncounter.ResetRun();
            buttonClicks.playerDied = false;
            moveBackGround.speed = 0f;
        }
        Debug.Log("[BRC] failures: " + fails);
        return fails;
    }

    static int Reds()
    {
        int n = 0;
        foreach (var t in Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (t != null && t.name == "pauseAtom(Clone)") n++;
        return n;
    }

    static GameObject Template(string name)
    {
        var go = new GameObject(name);
        go.SetActive(false);
        return go;
    }

    static spawnGoodStuff NewSpawner()
    {
        BossEncounter.ResetRun();
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        buttonClicks.playerDied = false;
        score.pauseCounter = 0;
        Time.timeScale = 1f;
        moveBackGround.speed = .37f;
        Random.InitState(11);
        var go = new GameObject("~BrcSpawner");
        go.transform.position = new Vector3(0f, 6.5f, 0f);
        var s = go.AddComponent<spawnGoodStuff>();
        s.smStar = Template("smStar1");
        s.midStar = Template("LargeStar1");
        s.Atom = Template("atom3a");
        s.redAtom = Template("pauseAtom");
        s.cooldownAtom = Template("cooldownAtom");
        typeof(spawnGoodStuff).GetMethod("Start", Inst).Invoke(s, null);
        return s;
    }

    static void ForceRed(spawnGoodStuff s, int times)
    {
        var m = typeof(spawnGoodStuff).GetMethod("spawnRedAtom", Inst);
        for (int i = 0; i < times; i++) m.Invoke(s, null);
    }

    static void ConstantsAndBalance()
    {
        Check("cap is 2 and the plan hands out exactly that many",
              BossEncounter.MaxRedAtomsPerFight == 2 && BossFreeShotAtoms.Count == BossEncounter.MaxRedAtomsPerFight);
        // 5 hit points: the 2 charged shots are worth 3 hits (the ultimates supply the rest)
        Check("the pair of charged free shots is worth >= 3 boss hits", BossFreeShotAtoms.Count * BossFreeShotAtoms.ChargedBudget >= 3f - 1e-4f);
    }

    static void NormalLevelUncapped()
    {
        var s = NewSpawner();
        ForceRed(s, 6);
        Check("normal level: red atoms are not capped (" + Reds() + ")", Reds() == 6);
        Check("normal level: reserve always succeeds", BossEncounter.TryReserveRedAtom() && BossEncounter.TryReserveRedAtom() && BossEncounter.TryReserveRedAtom());
    }

    static void EncountersCapAtTwoAndReset()
    {
        for (int world = 0; world < 2; world++)
        {
            var s = NewSpawner();
            BossEncounter.Begin(world, null);
            var e = BossEncounter.Instance;
            e.Step(.1f, 1f);
            // forced-spawn stress: the normal spawner, 20 times
            ForceRed(s, 20);
            Check("boss " + world + ": forced spawns release exactly 2 (" + Reds() + ")", Reds() == 2 && e.RedAtomsReleased == 2);
            Check("boss " + world + ": a third reserve is refused", !BossEncounter.TryReserveRedAtom());
            // the fight plan on top of it: still 2 in total
            e.BeginFight();
            typeof(spawnGoodStuff).GetField("redAtomDelayTimer", Inst).SetValue(s, 0f);
            var step = typeof(spawnGoodStuff).GetMethod("spawn", Inst, null, new[] { typeof(float) }, null);
            for (int i = 0; i < 4000 && e.State == BossEncounter.Phase.Fight; i++)
            {
                e.Step(.05f, 1f);
                AtomSpacing.Reset();
                step.Invoke(s, new object[] { .05f });
            }
            Check("boss " + world + ": the whole fight stays at 2 red atoms (" + Reds() + ")", Reds() == 2);
            // next encounter
            BossEncounter.ResetRun();
            BossEncounter.Begin(world, null);
            Check("boss " + world + ": the next encounter resets the count",
                  BossEncounter.Instance.RedAtomsReleased == 0 && BossEncounter.TryReserveRedAtom());
            ForceRed(s, 20);
            Check("boss " + world + ": ... and gets exactly 2 more (" + BossEncounter.Instance.RedAtomsReleased + ")",
                  BossEncounter.Instance.RedAtomsReleased == 2);
        }
    }
}
