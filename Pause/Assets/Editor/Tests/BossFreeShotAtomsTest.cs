using System.Collections.Generic;
using System.Reflection;
using UnityEditor.SceneManagement;
using UnityEngine;

// Feature: "during boss fights have 3 free shot atoms appear randomly before
// the boss escapes". Every boss fight (all worlds, the Ember loop's second
// one too) releases exactly BossFreeShotAtoms.Count red atoms at random
// moments of the fight, all before the boss's clock runs out, spaced by
// AtomSpacing, inside the lane, none once the boss is dead or gone.
//
// Drives the real BossEncounter and the real spawnGoodStuff together,
// frame by frame, in edit mode.
public static class BossFreeShotAtomsTest
{
    static int fails;
    static void Check(string what, bool ok)
    {
        Debug.Log((ok ? "[BFA] PASS  " : "[BFA] FAIL  ") + what);
        if (!ok) fails++;
    }

    public static void Run() { TestHarness.Exit(Execute()); }

    const BindingFlags Inst = BindingFlags.NonPublic | BindingFlags.Instance;
    const float Dt = .05f;

    public static int Execute()
    {
        fails = 0;
        using var sandbox = new TestHarness.Sandbox();
        EnemyDensity.Disabled = true;
        try
        {
            RollIsRandomSpacedAndInsideTheWindow();
            ShortWindowsFitFewer();
            for (int w = 0; w < WorldManager.LiveWorldCount; w++) EveryWorldGetsExactlyThree(w);
            LoopFightGetsThreeAgain();
            EarlyKillLeavesNoStray();
            DeterministicPerSeed();
            NoAllocations();
        }
        finally
        {
            BossEncounter.ResetRun();
            buttonClicks.playerDied = false;
            moveBackGround.speed = 0f;
        }
        Debug.Log("[BFA] failures: " + fails);
        return fails;
    }

    // ---- rig -----------------------------------------------------------

    class Rig
    {
        public GameObject root;
        public spawnGoodStuff s;
        public MethodInfo step;
        public object[] args = { Dt };
        public int lastReds;
    }

    static GameObject Template(string name)
    {
        var go = new GameObject(name);
        go.SetActive(false);
        return go;
    }

    static Rig NewRig(int seed)
    {
        BossEncounter.ResetRun();
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        buttonClicks.playerDied = false;
        score.pauseCounter = 0;
        Time.timeScale = 1f;
        Random.InitState(seed);
        var r = new Rig();
        var go = new GameObject("~BfaSpawner");
        go.transform.position = new Vector3(0f, 6.5f, 0f);
        r.s = go.AddComponent<spawnGoodStuff>();
        r.s.smStar = Template("smStar1");
        r.s.midStar = Template("LargeStar1");
        r.s.Atom = Template("atom3a");
        r.s.redAtom = Template("pauseAtom");
        r.s.cooldownAtom = Template("cooldownAtom");
        typeof(spawnGoodStuff).GetMethod("Start", Inst).Invoke(r.s, null);
        r.step = typeof(spawnGoodStuff).GetMethod("spawn", Inst, null, new[] { typeof(float) }, null);
        return r;
    }

    static List<GameObject> Reds()
    {
        var l = new List<GameObject>();
        foreach (var t in Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (t != null && t.name == "pauseAtom(Clone)") l.Add(t.gameObject);
        return l;
    }

    // One frame of both. Returns the red atoms released this frame.
    static int Frame(Rig r, BossEncounter e)
    {
        e.Step(Dt, 1f);
        r.step.Invoke(r.s, r.args);
        int n = Reds().Count;
        int d = n - r.lastReds;
        r.lastReds = n;
        return d;
    }

    class Result
    {
        public List<float> times = new List<float>();   // fight clock at each release
        public List<float> xs = new List<float>();
        public int afterEnd, beforeFight;
        public bool destroyed;
    }

    // Runs a whole encounter; killAt >= 0 destroys the boss that fight second.
    static Result Fight(Rig r, int world, float killAt = -1f)
    {
        var res = new Result();
        // a normal red atom is due the moment the fight begins: it must be held back
        typeof(spawnGoodStuff).GetField("redAtomDelayTimer", Inst).SetValue(r.s, 0f);
        BossEncounter.Begin(world, null);
        var e = BossEncounter.Instance;
        var seen = new HashSet<GameObject>(Reds());
        bool held = false;
        bool killed = false;
        for (int i = 0; i < 4000 && e.State != BossEncounter.Phase.Done; i++)
        {
            var phase = e.State;
            float clock = e.FightClock;
            if (phase == BossEncounter.Phase.Fight && killAt >= 0f && !killed && clock >= killAt)
            {
                killed = true;
                for (int k = 0; k < 20; k++) e.OnShipAttackHit(1f);
            }
            if (e.State == BossEncounter.Phase.Outro && !held)
            {
                held = true;   // the allowance's own red atom may come once the boss is gone: not under test
                typeof(spawnGoodStuff).GetField("redAtomDelayTimer", Inst).SetValue(r.s, 1e6f);
            }
            int d = Frame(r, e);
            if (d > 0)
            {
                foreach (var g in Reds())
                {
                    if (!seen.Add(g)) continue;
                    if (phase == BossEncounter.Phase.Fight && e.State == BossEncounter.Phase.Fight)
                    {
                        res.times.Add(e.FightClock);
                        res.xs.Add(g.transform.position.x);
                    }
                    else if (phase == BossEncounter.Phase.Outro || phase == BossEncounter.Phase.Done) res.afterEnd++;
                    else res.beforeFight++;
                }
            }
        }
        res.destroyed = e.Destroyed;
        // and a while after the boss is gone, nothing more comes
        for (int i = 0; i < 400; i++)
        {
            int d = Frame(r, e);
            if (d > 0) res.afterEnd += d;
        }
        return res;
    }

    static void Cleanup()
    {
        foreach (var t in Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (t != null && t.parent == null) Object.DestroyImmediate(t.gameObject);
        BossEncounter.ResetRun();
    }

    static void CheckFight(string label, Result res)
    {
        Check(label + ": exactly " + BossFreeShotAtoms.Count + " red atoms in the fight (got " + res.times.Count + ")",
            res.times.Count == BossFreeShotAtoms.Count);
        Check(label + ": none before the fight or after it", res.beforeFight == 0 && res.afterEnd == 0);
        bool inWin = true, spaced = true, lane = true;
        for (int i = 0; i < res.times.Count; i++)
        {
            if (res.times[i] < BossFreeShotAtoms.StartMargin - .01f ||
                res.times[i] > BossConfig.FightSeconds - BossFreeShotAtoms.EndMargin + AtomSpacing.Gap + .01f) inWin = false;
            if (i > 0 && res.times[i] - res.times[i - 1] < AtomSpacing.Gap - .001f) spaced = false;
            if (Mathf.Abs(res.xs[i]) > RailInset.PickupLaneHalf + .01f) lane = false;
        }
        Check(label + ": all before the boss escapes, after the start margin", inWin && res.times.Count > 0);
        Check(label + ": at least AtomSpacing.Gap apart", spaced);
        Check(label + ": inside the lane", lane);
    }

    // ---- tests ---------------------------------------------------------

    static void RollIsRandomSpacedAndInsideTheWindow()
    {
        Check("constants respect the atom gap", BossFreeShotAtoms.MinSpacing >= AtomSpacing.Gap && BossFreeShotAtoms.EndMargin >= AtomSpacing.Gap);
        var t = new float[BossFreeShotAtoms.Count];
        bool ok = true, spaced = true, varied = false;
        float first = -1f;
        for (int seed = 1; seed <= 200; seed++)
        {
            Random.InitState(seed);
            int n = BossFreeShotAtoms.Roll(BossConfig.FightSeconds, t);
            if (n != BossFreeShotAtoms.Count) ok = false;
            for (int i = 0; i < n; i++)
            {
                if (t[i] < BossFreeShotAtoms.StartMargin - 1e-3f || t[i] > BossConfig.FightSeconds - BossFreeShotAtoms.EndMargin + 1e-3f) ok = false;
                if (i > 0 && t[i] - t[i - 1] < BossFreeShotAtoms.MinSpacing - 1e-3f) spaced = false;
            }
            if (first < 0f) first = t[0]; else if (Mathf.Abs(t[0] - first) > .5f) varied = true;
        }
        Check("200 rolls: 3 times each inside [start margin, fight - end margin]", ok);
        Check("200 rolls: always >= MinSpacing apart", spaced);
        Check("rolls differ between seeds", varied);
    }

    static void ShortWindowsFitFewer()
    {
        var t = new float[BossFreeShotAtoms.Count];
        Check("a fight shorter than the margins gets none", BossFreeShotAtoms.Roll(BossFreeShotAtoms.StartMargin + BossFreeShotAtoms.EndMargin - 1f, t) == 0);
        Check("the dev 1.5 s fight gets none", BossFreeShotAtoms.Roll(BossEncounter.DevShortFightSeconds, t) == 0);
        int n = BossFreeShotAtoms.Roll(BossFreeShotAtoms.StartMargin + BossFreeShotAtoms.EndMargin + BossFreeShotAtoms.MinSpacing + .5f, t);
        Check("a window that fits two spaced atoms gets two", n == 2);
    }

    static void EveryWorldGetsExactlyThree(int w)
    {
        var r = NewRig(100 + w);
        var res = Fight(r, w);
        CheckFight("world " + w, res);
        Cleanup();
    }

    static void LoopFightGetsThreeAgain()
    {
        int last = WorldManager.LastLiveWorld;
        var r = NewRig(7);
        var first = Fight(r, last);
        BossEncounter.ForgetDone();
        r.lastReds = Reds().Count;
        var second = Fight(r, last);
        CheckFight("loop, first pass", first);
        CheckFight("loop, second fight", second);
        Cleanup();
    }

    static void EarlyKillLeavesNoStray()
    {
        var r = NewRig(5);
        var res = Fight(r, 0, killAt: 0.5f);
        Check("boss destroyed in the first second: no atoms at all (" + res.times.Count + "+" + res.afterEnd + ")", res.destroyed && res.times.Count == 0 && res.afterEnd == 0);
        Cleanup();

        r = NewRig(5);
        res = Fight(r, 0, killAt: 20f);
        Check("boss destroyed mid-fight: only those already due came, none after (" + res.times.Count + ")",
            res.destroyed && res.times.Count < BossFreeShotAtoms.Count && res.afterEnd == 0);
        bool early = true;
        foreach (var t in res.times) if (t > 20.5f) early = false;
        Check("... and all before the kill", early);
        Cleanup();
    }

    static void DeterministicPerSeed()
    {
        var a = NewRig(42); var ra = Fight(a, 1); Cleanup();
        var b = NewRig(42); var rb = Fight(b, 1); Cleanup();
        var c = NewRig(43); var rc = Fight(c, 1); Cleanup();
        bool same = ra.times.Count == rb.times.Count && ra.times.Count > 0;
        for (int i = 0; same && i < ra.times.Count; i++) same = Mathf.Approximately(ra.times[i], rb.times[i]) && Mathf.Approximately(ra.xs[i], rb.xs[i]);
        bool diff = ra.times.Count != rc.times.Count;
        for (int i = 0; !diff && i < ra.times.Count; i++) diff = Mathf.Abs(ra.times[i] - rc.times[i]) > .01f;
        Check("same seed, same times and x", same);
        Check("different seed, different times", diff);
    }

    static void NoAllocations()
    {
        var r = NewRig(9);
        BossEncounter.Begin(0, null);
        var e = BossEncounter.Instance;
        bool x = false;
        long before = System.GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 2000; i++) x |= BossEncounter.FreeAtomDue;
        long after = System.GC.GetAllocatedBytesForCurrentThread();
        Check("the per-frame due check allocates nothing", after - before == 0 && !x);
        Cleanup();
    }
}
