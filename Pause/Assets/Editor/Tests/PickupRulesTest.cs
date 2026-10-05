using System.IO;
using System.Reflection;
using UnityEditor.SceneManagement;
using UnityEngine;

// Feature: "during boss fights release 3x more star dust so it can activate
// the powerups faster ... and during the level release 2x more blue shield
// atoms" (PickupRules).
//
// Drives the real spawnGoodStuff headlessly through spawn(dt) with seeded
// Random, counting what it instantiates from stand-in templates.
public static class PickupRulesTest
{
    static int fails;
    static void Check(string what, bool ok)
    {
        Debug.Log((ok ? "[PICKUP] PASS  " : "[PICKUP] FAIL  ") + what);
        if (!ok) fails++;
    }

    public static void Run()
    {
        TestHarness.Exit(Execute());
    }

    const BindingFlags Inst = BindingFlags.NonPublic | BindingFlags.Instance;
    const float Dt = .1f;

    public static int Execute()
    {
        fails = 0;
        using var sandbox = new TestHarness.Sandbox();
        // These checks read exact spawn delays to prove the loop / boss hooks;
        // the speed-keyed density cut (EnemyDensity, its own suite) multiplies
        // the same delays, so it is switched off here (the Sandbox puts it back).
        EnemyDensity.Disabled = true;
        try
        {
            Constants();
            BossTriplesStarDust();
            ShieldAtomsDoubleOthersUnchanged();
            DustFillsTheMeterFaster();
            DensityHookUntouched();
            TutorialUntouched();
        }
        finally
        {
            BossEncounter.ResetRun();
            LoopDifficulty.Reset();
        }
        Debug.Log("[PICKUP] failures: " + fails);
        return fails;
    }

    // ---- fixtures ------------------------------------------------------

    struct Counts { public int small, large, blue, red; }

    static GameObject Template(string name)
    {
        var go = new GameObject(name);
        go.SetActive(false); // clones stay inert
        return go;
    }

    static spawnGoodStuff NewSpawner()
    {
        var go = new GameObject("~spawnGoodStuffTest");
        go.transform.position = new Vector3(0f, 6.5f, 0f);
        var s = go.AddComponent<spawnGoodStuff>();
        s.smStar = Template("smStar1");
        s.midStar = Template("LargeStar1");
        s.Atom = Template("atom3a");
        s.redAtom = Template("pauseAtom");
        return s;
    }

    // Steps the spawner for `seconds` and counts the clones it made.
    static Counts Simulate(int seed, float seconds)
    {
        Random.InitState(seed);
        var s = NewSpawner();
        typeof(spawnGoodStuff).GetMethod("Start", Inst).Invoke(s, null);
        var step = typeof(spawnGoodStuff).GetMethod("spawn", Inst, null, new[] { typeof(float) }, null);
        var args = new object[] { Dt };
        int steps = Mathf.RoundToInt(seconds / Dt);
        for (int i = 0; i < steps; i++) step.Invoke(s, args);

        var c = new Counts();
        foreach (var t in Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            string n = t.name;
            if (!n.EndsWith("(Clone)")) continue;
            if (n.StartsWith("smStar1")) c.small++;
            else if (n.StartsWith("LargeStar1")) c.large++;
            else if (n.StartsWith("atom3a")) c.blue++;
            else if (n.StartsWith("pauseAtom")) c.red++;
            Object.DestroyImmediate(t.gameObject);
        }
        Object.DestroyImmediate(s.smStar);
        Object.DestroyImmediate(s.midStar);
        Object.DestroyImmediate(s.Atom);
        Object.DestroyImmediate(s.redAtom);
        Object.DestroyImmediate(s.gameObject);
        return c;
    }

    static void FreshScene()
    {
        BossEncounter.ResetRun();
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        buttonClicks.playerDied = false;
        score.pauseCounter = 0;
    }

    static void StartBoss()
    {
        Check("a boss encounter starts (Pending counts as running)", BossEncounter.Begin(0, null) && BossEncounter.Running);
    }

    // ---- checks ---------------------------------------------------------

    static void Constants()
    {
        Check("BossStarDustMultiplier is 3", PickupRules.BossStarDustMultiplier == 3f);
        Check("ShieldAtomRateMultiplier is 2", PickupRules.ShieldAtomRateMultiplier == 2);
        Check("star dust runs x1 without a boss, x3 with one",
              PickupRules.StarDustRate(false) == 1f && PickupRules.StarDustRate(true) == 3f);
        Check("a planet's blue allowance doubles (3-5 -> 6-10)",
              PickupRules.ShieldAtomBudget(3) == 6 && PickupRules.ShieldAtomBudget(5) == 10);
    }

    static void BossTriplesStarDust()
    {
        const float window = 300f;
        long baseSmall = 0, baseLarge = 0, bossSmall = 0, bossLarge = 0;
        for (int seed = 1; seed <= 8; seed++)
        {
            FreshScene();
            Check("no boss in the baseline run", !BossEncounter.Running);
            var b = Simulate(seed, window);
            baseSmall += b.small; baseLarge += b.large;

            FreshScene();
            StartBoss();
            var f = Simulate(seed, window);
            bossSmall += f.small; bossLarge += f.large;
        }
        float smallRatio = (float)bossSmall / Mathf.Max(1, baseSmall);
        float largeRatio = (float)bossLarge / Mathf.Max(1, baseLarge);
        float totalRatio = (float)(bossSmall + bossLarge) / Mathf.Max(1, baseSmall + baseLarge);
        Check("baseline releases star dust (" + baseSmall + " small, " + baseLarge + " large over 8 x " + window + "s)",
              baseSmall > 0 && baseLarge > 0);
        Check("boss releases ~3x the small dust (" + smallRatio.ToString("F2") + "x)", smallRatio > 2.7f && smallRatio < 3.3f);
        Check("boss releases ~3x the large dust (" + largeRatio.ToString("F2") + "x)", largeRatio > 2.7f && largeRatio < 3.3f);
        Check("boss releases ~3x all star dust (" + totalRatio.ToString("F2") + "x)", totalRatio > 2.8f && totalRatio < 3.2f);
        BossEncounter.ResetRun();
    }

    // The pre-change rules, replayed with the same RNG calls, for the
    // baseline the doubled shield rate is measured against.
    static int OldBlueCount(int seed, float seconds)
    {
        Random.InitState(seed);
        int budget = Random.Range(3, 6);
        float timer = Random.Range(20f, 45f);
        int spawned = 0;
        int steps = Mathf.RoundToInt(seconds / Dt);
        for (int i = 0; i < steps; i++)
        {
            timer -= Dt;
            if (timer <= 0f && spawned < budget - 1)
            {
                spawned++;
                timer = Random.Range(55f, 95f);
            }
        }
        return spawned;
    }

    static float OldRedMean(float seconds, int runs)
    {
        long total = 0;
        for (int seed = 1; seed <= runs; seed++)
        {
            Random.InitState(1000 + seed);
            float timer = Random.Range(15f, 40f);
            int spawned = 0;
            int steps = Mathf.RoundToInt(seconds / Dt);
            for (int i = 0; i < steps; i++)
            {
                timer -= Dt;
                if (timer <= 0f && spawned < 5) { timer = Random.Range(50f, 90f); spawned++; }
            }
            total += spawned;
        }
        return (float)total / runs;
    }

    static void ShieldAtomsDoubleOthersUnchanged()
    {
        const float window = 180f; // a long level (baseline is WorldManager.BaselineWorldSeconds = 120s)
        const int runs = 60;
        long blue = 0, red = 0, oldBlue = 0;
        for (int seed = 1; seed <= runs; seed++)
        {
            FreshScene();
            var c = Simulate(seed, window);
            blue += c.blue; red += c.red;
            oldBlue += OldBlueCount(5000 + seed, window);
        }
        float blueMean = (float)blue / runs, oldMean = (float)oldBlue / runs;
        float ratio = blueMean / Mathf.Max(.01f, oldMean);
        Check("blue shield atoms per 180s: " + oldMean.ToString("F2") + " before -> " + blueMean.ToString("F2") +
              " now (" + ratio.ToString("F2") + "x, ~2x)", ratio > 1.8f && ratio < 2.4f);

        float redMean = (float)red / runs, oldRed = OldRedMean(window, runs);
        Check("red pause atoms unchanged (" + oldRed.ToString("F2") + " before, " + redMean.ToString("F2") + " now)",
              Mathf.Abs(redMean - oldRed) < .35f);

        // the boss doesn't touch the red atom either
        FreshScene();
        StartBoss();
        long bossRed = 0;
        for (int seed = 1; seed <= 20; seed++)
        {
            if (!BossEncounter.Running) StartBoss();
            bossRed += Simulate(seed, window).red;
        }
        Check("red pause atoms unchanged in a boss fight (" + ((float)bossRed / 20).ToString("F2") + ")",
              Mathf.Abs((float)bossRed / 20 - oldRed) < .5f);
        BossEncounter.ResetRun();

        // the green heal atom lives in its own spawner, with its own rules
        var heal = new GameObject("~heal").AddComponent<HealAtomSpawner>();
        Check("heal atom spawner defaults unchanged (2 per world, 60s first, 70-130s gap, 60%)",
              heal.maxPerWorld == 2 && heal.firstChanceAfter == 60f &&
              heal.gapSeconds == new Vector2(70f, 130f) && Mathf.Approximately(heal.chance, .6f));
        Object.DestroyImmediate(heal.gameObject);
        Check("HealAtomSpawner reads no PickupRules",
              !File.ReadAllText("Assets/Scripts/Gameplay/HealAtomSpawner.cs").Contains("PickupRules"));
    }

    // Each pickup fills the secret meter by its fixed amount, so 3x the dust
    // is 3x the meter per second of fight.
    static void DustFillsTheMeterFaster()
    {
        FreshScene();
        var go = new GameObject("~secret");
        var c = go.AddComponent<SecretPowerController>();
        float per = c.perSmallDust;
        c.SetMeter(0f);
        c.Add(per * 2);
        float normal = c.Meter;
        c.SetMeter(0f);
        c.Add(per * 2 * PickupRules.BossStarDustMultiplier);
        Check("3x the dust caught is 3x the secret meter (" + normal + " -> " + c.Meter + ")",
              Mathf.Approximately(c.Meter, normal * 3f));
        Object.DestroyImmediate(go);
    }

    static void DensityHookUntouched()
    {
        FreshScene();
        var go = new GameObject("~board");
        var board = go.AddComponent<enmiesOnBoard>();
        var roll = typeof(enmiesOnBoard).GetMethod("Roll", Inst);
        var elapsed = typeof(enmiesOnBoard).GetField("elapsedFlightSeconds", Inst);
        elapsed.SetValue(board, 160f); // flat 3x final stretch (180s default level)
        LoopDifficulty.DensityScale = 1.2f;
        float d = (float)roll.Invoke(board, new object[] { new Vector2(12f, 12f) });
        Check("enemy Roll still divides by DensityMultiplier x LoopDifficulty.DensityScale (" + d.ToString("F3") + ")",
              Mathf.Approximately(d, 12f / (3f * 1.2f)));
        StartBoss();
        float b = (float)roll.Invoke(board, new object[] { new Vector2(12f, 12f) });
        Check("... and the boss's dust boost doesn't touch enemy density", Mathf.Approximately(b, d));
        LoopDifficulty.Reset();
        BossEncounter.ResetRun();
        Object.DestroyImmediate(go);
    }

    static void TutorialUntouched()
    {
        string tut = File.ReadAllText("Assets/Scripts/Tutorial/spawnGoodStuffTut.cs");
        Check("the tutorial spawner reads no PickupRules", !tut.Contains("PickupRules"));
    }
}
