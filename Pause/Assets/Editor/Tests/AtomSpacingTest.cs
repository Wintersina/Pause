using System.Collections.Generic;
using System.Reflection;
using UnityEditor.SceneManagement;
using UnityEngine;

// Bug: "a bunch of atoms spawn now at the same time".
//
// The real game's atoms come from two independent spawners -- spawnGoodStuff
// (blue shield, red pause, violet capacitor) and HealAtomSpawner (green) --
// each on its own timers, with nothing keeping them apart. Since worlds
// became a ~120 s distance (WorldManager.BaselineWorldSeconds) the blue
// atom's end-of-level guarantee (60 s left) lands at ~60 s of flight: the
// very second the green atom's first chance comes up (firstChanceAfter 60 s),
// with the capacitor's first one (35-65 s) close by -- two or three atoms
// in one breath on most worlds.
//
// Drives both real spawners headlessly through a stock 120 s world plus a
// boss-length tail (seeded Random, a damaged pilot so the green atom is in
// play) with a real WorldManager's level clock, and checks that no two
// atoms are released within AtomSpacing.Gap of each other, while each
// world still gets its guaranteed blue and the spawners' budgets.
public static class AtomSpacingTest
{
    static int fails;
    static void Check(string what, bool ok)
    {
        Debug.Log((ok ? "[ATOMGAP] PASS  " : "[ATOMGAP] FAIL  ") + what);
        if (!ok) fails++;
    }

    public static void Run() { TestHarness.Exit(Execute()); }

    const BindingFlags Inst = BindingFlags.NonPublic | BindingFlags.Instance;
    const BindingFlags Stat = BindingFlags.NonPublic | BindingFlags.Static;
    const float Dt = .05f;
    const float WorldSeconds = 120f, TailSeconds = 40f;
    const float Speed = .15f;   // a steady world speed: the level clock is distance / speed

    public static int Execute()
    {
        fails = 0;
        using var sandbox = new TestHarness.Sandbox();
        EnemyDensity.Disabled = true;
        try
        {
            SpacedThroughAWorld();
        }
        finally
        {
            BossEncounter.ResetRun();
            SetWorldManager(null);
        }
        Debug.Log("[ATOMGAP] failures: " + fails);
        return fails;
    }

    static void SetWorldManager(WorldManager wm)
    {
        typeof(WorldManager).GetField("<Instance>k__BackingField", BindingFlags.NonPublic | BindingFlags.Static).SetValue(null, wm);
    }

    static GameObject Template(string name)
    {
        var go = new GameObject(name);
        go.SetActive(false);
        return go;
    }

    struct Release { public float t; public char kind; }

    // One world: every atom release, in order.
    static List<Release> World(int seed)
    {
        BossEncounter.ResetRun();
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        buttonClicks.playerDied = false;
        score.pauseCounter = 0;
        collisionDetection.lifeCounter = 1;   // banged up: the green atom is in play
        PlayerPrefs.SetInt(WorldManager.PrefsCurrentWorld, 0);
        moveBackGround.speed = Speed;

        // the level clock: a WorldManager whose distance runs out after WorldSeconds
        var wm = new GameObject("~AgWorlds").AddComponent<WorldManager>();
        SetWorldManager(wm);
        typeof(WorldManager).GetField("appliedRate", Stat).SetValue(null, 0f);   // steady speed: seconds = distance / speed
        var distance = typeof(WorldManager).GetField("distanceLeft", Inst);

        Random.InitState(seed);
        var go = new GameObject("~AgSpawner");
        go.transform.position = new Vector3(0f, 6.5f, 0f);
        var s = go.AddComponent<spawnGoodStuff>();
        s.smStar = Template("smStar1");
        s.midStar = Template("LargeStar1");
        s.Atom = Template("atom3a");
        s.redAtom = Template("pauseAtom");
        s.cooldownAtom = Template("cooldownAtom");
        typeof(spawnGoodStuff).GetMethod("Start", Inst).Invoke(s, null);
        var step = typeof(spawnGoodStuff).GetMethod("spawn", Inst, null, new[] { typeof(float) }, null);
        var blue = typeof(spawnGoodStuff).GetField("blueSpawned", Inst);
        var red = typeof(spawnGoodStuff).GetField("redSpawned", Inst);
        var cap = typeof(spawnGoodStuff).GetField("cooldownSpawned", Inst);

        var heal = new GameObject("~AgHeal").AddComponent<HealAtomSpawner>();
        typeof(HealAtomSpawner).GetMethod("Start", Inst).Invoke(heal, null);
        var healed = typeof(HealAtomSpawner).GetField("spawnedThisWorld", Inst);

        var releases = new List<Release>();
        int b0 = 0, r0 = 0, c0 = 0, h0 = 0;
        var args = new object[] { Dt };
        int steps = Mathf.RoundToInt((WorldSeconds + TailSeconds) / Dt);
        for (int i = 0; i < steps; i++)
        {
            float t = (i + 1) * Dt;
            distance.SetValue(wm, Mathf.Max(0f, Speed * (WorldSeconds - t)));
            step.Invoke(s, args);
            heal.Step(Dt);
            int b = (int)blue.GetValue(s), r = (int)red.GetValue(s), c = (int)cap.GetValue(s), h = (int)healed.GetValue(heal);
            // the first step resets the allowance (lastWorld -1 -> 0): counts start there
            if (i == 0) { b0 = 0; r0 = 0; c0 = 0; }
            for (; b0 < b; b0++) releases.Add(new Release { t = t, kind = 'B' });
            for (; r0 < r; r0++) releases.Add(new Release { t = t, kind = 'R' });
            for (; c0 < c; c0++) releases.Add(new Release { t = t, kind = 'C' });
            for (; h0 < h; h0++) releases.Add(new Release { t = t, kind = 'G' });
        }

        foreach (var t in Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (t != null && t.parent == null) Object.DestroyImmediate(t.gameObject);
        SetWorldManager(null);
        return releases;
    }

    static void SpacedThroughAWorld()
    {
        const int seeds = 40;
        float gap = AtomSpacing.Gap;
        int crowded = 0, sameSecond = 0, noGuarantee = 0, total = 0, greens = 0;
        float closest = float.MaxValue;
        string example = null;
        for (int seed = 1; seed <= seeds; seed++)
        {
            var rel = World(seed);
            total += rel.Count;
            bool guarantee = false;
            for (int i = 0; i < rel.Count; i++)
            {
                if (rel[i].kind == 'G') greens++;
                if (rel[i].kind == 'B' && rel[i].t >= WorldSeconds - 60f - 1f && rel[i].t <= WorldSeconds) guarantee = true;
                if (i == 0) continue;
                float d = rel[i].t - rel[i - 1].t;
                closest = Mathf.Min(closest, d);
                if (d < 1f) sameSecond++;
                if (d < gap - Dt * .5f)
                {
                    crowded++;
                    if (example == null)
                        example = "seed " + seed + ": " + rel[i - 1].kind + " at " + rel[i - 1].t.ToString("F2") + " s, " +
                                  rel[i].kind + " at " + rel[i].t.ToString("F2") + " s";
                }
            }
            if (!guarantee) noGuarantee++;
        }
        Check("no two atoms within " + gap + " s of each other over " + seeds + " worlds (" + crowded +
              " crowded pairs, " + sameSecond + " within 1 s, closest " + closest.ToString("F2") + " s" +
              (example != null ? "; e.g. " + example : "") + ")", crowded == 0);
        Check("every world still hands out a blue atom in its last minute (" + noGuarantee + " without)", noGuarantee == 0);
        Check("the spacing costs no world its atoms (" + (total / (float)seeds).ToString("F1") + " a world, " +
              greens + " green)", total / (float)seeds >= 7f && greens >= seeds / 2);
    }
}

