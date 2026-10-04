using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;
using Random = UnityEngine.Random;

// The violet capacitor atom (cooldownAtom): spawned by spawnGoodStuff on the
// PickupRules budget/delays (plain clock, a fresh allowance every planet),
// collected by collisionDetection: counts, scores ScoreRules.CooldownAtom as
// an atom (with its popup), cuts exactly min(12 s, what's left) off the
// weapon charge without firing it, says "-12s CHARGE" or "WEAPON CHARGED",
// pulses the charge indicator violet and bursts in its own frames. It drifts
// down through AtomWander and leaves, never appears in the tutorial, and the
// pickup's own work allocates nothing.
//
//   Unity -batchmode -quit -projectPath Pause -executeMethod CooldownAtomTest.Run
public static class CooldownAtomTest
{
    static int fails;
    static void Check(string what, bool ok)
    {
        Debug.Log((ok ? "[CA] PASS  " : "[CA] FAIL  ") + what);
        if (!ok) fails++;
    }

    public static void Run()
    {
        TestHarness.Exit(Execute());
    }

    const BindingFlags Inst = BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance;
    const BindingFlags Stat = BindingFlags.NonPublic | BindingFlags.Static;
    const float Dt = .1f;
    const string PrefabPath = "Assets/Resources/prefabs/cooldownAtom.prefab";

    public static int Execute()
    {
        fails = 0;
        using var sandbox = new TestHarness.Sandbox();
        try
        {
            Rules();
            Prefab();
            SpawnsOnBudgetAndDelays();
            Pickup();
            Drifts();
            NotInTutorial();
            NoAllocations();
        }
        finally
        {
            RunScore.EndRun(RunScore.RunId);
            BossEncounter.ResetRun();
            AttackPool.StopAll();
            WorldTimeFx.Reset();
            Time.timeScale = 1f;
            buttonClicks.playerDied = false;
            moveBackGround.speed = 0f;
        }
        Debug.Log("[CA] failures: " + fails);
        return fails;
    }

    // ---- rules -----------------------------------------------------------

    static void Rules()
    {
        Check("PickupRules: 2 capacitors per world, first 35-65 s, then 75-115 s, plain clock",
              PickupRules.CooldownAtomsPerWorld == 2 &&
              PickupRules.CooldownAtomFirstDelayMin == 35f && PickupRules.CooldownAtomFirstDelayMax == 65f &&
              PickupRules.CooldownAtomRepeatDelayMin == 75f && PickupRules.CooldownAtomRepeatDelayMax == 115f &&
              PickupRules.CooldownAtomRate == 1f);
        Check("spawnGoodStuff reads the capacitor rules from PickupRules (no inline numbers or inspector budget)",
              typeof(spawnGoodStuff).GetField("cooldownAtomsPerWorld", Inst) == null &&
              Source("Assets/Scripts/Gameplay/spawnGoodStuff.cs").Contains("PickupRules.CooldownAtomFirstDelay()") &&
              Source("Assets/Scripts/Gameplay/spawnGoodStuff.cs").Contains("PickupRules.CooldownAtomRepeatDelay()"));
        Check("ScoreRules.CooldownAtom is an atom's worth (" + ScoreRules.CooldownAtom + ")",
              ScoreRules.CooldownAtom == ScoreRules.PauseAtom && ScoreRules.CooldownAtom > 0);
        int old = ScoreRules.CooldownAtom;
        ScoreRules.CooldownAtom = 77;
        bool own = RunScore.AtomPoints(RunScore.Atom.Cooldown) == 77 &&
                   RunScore.AtomPoints(RunScore.Atom.Pause) == ScoreRules.PauseAtom;
        ScoreRules.CooldownAtom = old;
        Check("RunScore scores the capacitor by its own ScoreRules value, not the pause atom's", own);
        Check("the charge labels: partial and full",
              ShipPowerController.CooldownAtomLabel(12f, false) == "-12s CHARGE" &&
              ShipPowerController.CooldownAtomLabel(7.4f, false) == "-7s CHARGE" &&
              ShipPowerController.CooldownAtomLabel(3f, true) == "WEAPON CHARGED");
    }

    static string Source(string path) => File.ReadAllText(path);

    static void Prefab()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        Check("cooldownAtom prefab loads (and from Resources, as the spawner does)",
              prefab != null && Resources.Load<GameObject>("prefabs/cooldownAtom") != null);
        if (prefab == null) return;
        var col = prefab.GetComponent<Collider2D>();
        var red = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Resources/prefabs/pauseAtom.prefab");
        var redCol = red != null ? red.GetComponent<Collider2D>() : null;
        PickupKind kind;
        Check("it is a pickUp of kind Cooldown with a collider set up like the red atom's",
              prefab.CompareTag("pickUp") && col != null && redCol != null && col.isTrigger == redCol.isTrigger &&
              PickupArt.TryKindOf(prefab, out kind) && kind == PickupKind.Cooldown);
    }

    // ---- spawning ---------------------------------------------------------

    static GameObject Template(string name)
    {
        var go = new GameObject(name);
        go.SetActive(false);
        return go;
    }

    // One spawner over `worlds` planets of `seconds` each; returns the time
    // (since the world began) of every capacitor spawned, per world.
    static List<List<float>> Simulate(int seed, int worlds, float seconds, bool boss, out int clones)
    {
        BossEncounter.ResetRun();
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        buttonClicks.playerDied = false;
        score.pauseCounter = 0;
        if (boss) BossEncounter.Begin(0, null);
        Random.InitState(seed);
        var go = new GameObject("~spawnGoodStuffTest");
        go.transform.position = new Vector3(0f, 6.5f, 0f);
        var s = go.AddComponent<spawnGoodStuff>();
        s.smStar = Template("smStar1");
        s.midStar = Template("LargeStar1");
        s.Atom = Template("atom3a");
        s.redAtom = Template("pauseAtom");
        s.cooldownAtom = Template("cooldownAtom");
        typeof(spawnGoodStuff).GetMethod("Start", Inst).Invoke(s, null);
        var step = typeof(spawnGoodStuff).GetMethod("spawn", Inst, null, new[] { typeof(float) }, null);
        var spawned = typeof(spawnGoodStuff).GetField("cooldownSpawned", Inst);
        var lastWorld = typeof(spawnGoodStuff).GetField("lastWorld", Inst);
        var args = new object[] { Dt };
        var result = new List<List<float>>();
        int steps = Mathf.RoundToInt(seconds / Dt);
        for (int w = 0; w < worlds; w++)
        {
            // a new planet: the spawner sees the world index change
            if (w > 0) lastWorld.SetValue(s, 100 + w);
            var times = new List<float>();
            int before = -1;
            for (int i = 0; i < steps; i++)
            {
                step.Invoke(s, args);
                int now = (int)spawned.GetValue(s);
                if (i == 0) before = 0;   // the first step resets the allowance
                if (now > before) times.Add((i + 1) * Dt);
                before = now;
            }
            result.Add(times);
        }
        clones = 0;
        foreach (var t in Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (t != null && t.name == "cooldownAtom(Clone)") clones++;
        Object.DestroyImmediate(go);
        BossEncounter.ResetRun();
        return result;
    }

    static void SpawnsOnBudgetAndDelays()
    {
        const int seeds = 30, worlds = 3;
        const float worldSeconds = 400f;
        int overBudget = 0, notTwo = 0, firstOut = 0, gapOut = 0, cloneMismatch = 0, bossFirstDiffers = 0, bossGapOut = 0;
        float firstMin = float.MaxValue, firstMax = 0f, gapMin = float.MaxValue, gapMax = 0f;
        for (int seed = 1; seed <= seeds; seed++)
        {
            int clones;
            var run = Simulate(seed, worlds, worldSeconds, false, out clones);
            int total = 0;
            foreach (var times in run)
            {
                total += times.Count;
                if (times.Count > PickupRules.CooldownAtomsPerWorld) overBudget++;
                if (times.Count != PickupRules.CooldownAtomsPerWorld) notTwo++;   // 400 s is room for both
                if (times.Count > 0)
                {
                    float f = times[0];
                    firstMin = Mathf.Min(firstMin, f); firstMax = Mathf.Max(firstMax, f);
                    if (f < PickupRules.CooldownAtomFirstDelayMin || f > PickupRules.CooldownAtomFirstDelayMax + Dt * 2f) firstOut++;
                }
                for (int i = 1; i < times.Count; i++)
                {
                    float g = times[i] - times[i - 1];
                    gapMin = Mathf.Min(gapMin, g); gapMax = Mathf.Max(gapMax, g);
                    if (g < PickupRules.CooldownAtomRepeatDelayMin - Dt || g > PickupRules.CooldownAtomRepeatDelayMax + Dt * 2f) gapOut++;
                }
            }
            if (clones != total) cloneMismatch++;

            // a boss fight doesn't speed the capacitor's clock
            int bossClones;
            var bossRun = Simulate(seed, 1, worldSeconds, true, out bossClones);
            var b = bossRun[0];
            if (b.Count == 0 || run[0].Count == 0 || Mathf.Abs(b[0] - run[0][0]) > 1e-3f) bossFirstDiffers++;
            for (int i = 1; i < b.Count; i++)
            {
                float g = b[i] - b[i - 1];
                if (g < PickupRules.CooldownAtomRepeatDelayMin - Dt || g > PickupRules.CooldownAtomRepeatDelayMax + Dt * 2f) bossGapOut++;
            }
            if (b.Count > PickupRules.CooldownAtomsPerWorld) overBudget++;
        }
        Check("never more than " + PickupRules.CooldownAtomsPerWorld + " capacitors in a world (" + overBudget + " over)",
              overBudget == 0);
        Check("every world (fresh allowance each planet) hands out both (" + notTwo + " short of " + seeds * worlds + ")",
              notTwo == 0);
        Check("first capacitor after 35-65 s (seen " + firstMin.ToString("F1") + "-" + firstMax.ToString("F1") + ")",
              firstOut == 0 && firstMax > firstMin);
        Check("next one 75-115 s later (seen " + gapMin.ToString("F1") + "-" + gapMax.ToString("F1") + ")",
              gapOut == 0 && gapMax > gapMin);
        Check("each spawn instantiates one cooldownAtom (" + cloneMismatch + " mismatched runs)", cloneMismatch == 0);
        Check("a boss fight leaves the capacitor's clock alone (" + bossFirstDiffers + " first spawns moved, " +
              bossGapOut + " gaps out)", bossFirstDiffers == 0 && bossGapOut == 0);
    }

    // ---- pickup -----------------------------------------------------------

    class Rig
    {
        public ShipPowerController c;
        public collisionDetection cd;
        public Action<Collider2D> trigger;
    }

    static void FreshScene()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var camGo = new GameObject("Main Camera", typeof(Camera));
        camGo.tag = "MainCamera";
        var cam = camGo.GetComponent<Camera>();
        cam.orthographic = true;
        cam.orthographicSize = 5f;
        cam.aspect = .5625f;
        camGo.transform.position = new Vector3(0f, 0f, -10f);
        buttonClicks.playerDied = false;
        score.pauseCounter = 0;
        collisionDetection.cloakTimer = 0f;
        collisionDetection.atomCheck = false;
        collisionDetection.lifeCounter = 0;
        moveBackGround.speed = .2f;
        Time.timeScale = 1f;
        WorldTimeFx.Reset();
    }

    static Rig Ship(int id)
    {
        PlayerPrefs.SetInt("spawnShip", id);
        var go = new GameObject("ship" + id, typeof(SpriteRenderer));
        go.transform.position = new Vector3(0f, -3f, 0f);
        var r = new Rig();
        r.cd = go.AddComponent<collisionDetection>();
        r.cd.explosionAnimation = new GameObject("~TestExplosion");
        r.cd.boostSound = go.AddComponent<AudioSource>();
        r.cd.boostText = new GameObject("~boostText", typeof(RectTransform)).AddComponent<Text>();
        r.cd.hypeText = new GameObject("~hypeText", typeof(RectTransform)).AddComponent<Text>();
        r.cd.boost = new GameObject("~boost");
        r.cd.boost.SetActive(false);
        r.trigger = (Action<Collider2D>)Delegate.CreateDelegate(typeof(Action<Collider2D>), r.cd,
            typeof(collisionDetection).GetMethod("OnTriggerEnter2D", Inst));
        r.c = go.AddComponent<ShipPowerController>();
        r.c.SendMessage("Awake");
        r.c.SendMessage("Start");
        var gun = go.GetComponentInChildren<UltimateGun>();
        if (gun != null) gun.SendMessage("Awake");
        return r;
    }

    static void Teardown(Rig r)
    {
        if (r == null || r.c == null) return;
        var c = r.c;
        c.SendMessage("OnDestroy");
        if (c.Runner != null) c.Runner.SendMessage("OnDestroy");
        if (c.Secret != null) c.Secret.SendMessage("OnDestroy");
        Object.DestroyImmediate(c.gameObject);
        AttackPool.StopAll();
        collisionDetection.cloakTimer = 0f;
        collisionDetection.lifeCounter = 0;
        typeof(ShipPowerController).GetMethod("FinishCinematic", Stat).Invoke(null, null);
    }

    static void SetTimer(ShipPowerController c, float seconds) =>
        typeof(ShipPowerController).GetField("timer", Inst).SetValue(c, seconds);

    static float Cooldown(ShipPowerController c) =>
        (float)typeof(ShipPowerController).GetField("cooldown", Inst).GetValue(c);

    static GameObject NewAtom(Vector3 at)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        var go = Object.Instantiate(prefab, at, Quaternion.identity);
        go.name = "cooldownAtom(Clone)";
        return go;
    }

    struct Outcome
    {
        public string text;
        public string burstSprite;
        public int pickups, scored, atomPoints, atomCount, scoredEvents;
        public RunScore.Source source;
    }

    static Outcome PickUp(Rig r, float timer)
    {
        SetTimer(r.c, timer);
        r.cd.hypeText.text = "";
        var atom = NewAtom(new Vector3(.4f, -2.6f, 0f));
        var o = new Outcome();
        int pickups = collisionDetection.cooldownAtomPickups;
        var parts = RunScore.Parts;
        Action<int, Vector3, RunScore.Source> spy = (p, w, s) => { o.scored = p; o.source = s; o.scoredEvents++; };
        RunScore.Scored += spy;
        try { r.trigger(atom.GetComponent<Collider2D>()); }
        finally { RunScore.Scored -= spy; }
        o.pickups = collisionDetection.cooldownAtomPickups - pickups;
        o.atomPoints = (int)(RunScore.Parts.atoms - parts.atoms);
        o.atomCount = RunScore.Parts.atomCount - parts.atomCount;
        o.text = r.cd.hypeText.text;
        var burst = PickupBurst.LastPlayed;
        if (burst != null)
        {
            var sr = burst.GetComponent<SpriteRenderer>();
            o.burstSprite = sr != null && sr.sprite != null && burst.gameObject.activeSelf ? sr.sprite.name : null;
            burst.Finish();
        }
        if (atom != null) Object.DestroyImmediate(atom);
        return o;
    }

    static void Pickup()
    {
        FreshScene();
        RunScore.EndRun(RunScore.RunId);
        PlayerPrefs.SetString("HasDoneTut", "true");
        startMenu.youAreInTutorial = false;
        moveBackGround.speed = 0f;
        RunScore.BeginRun(true, true);
        var r = Ship(4);
        var c = r.c;
        float cooldown = Cooldown(c);
        long total = RunScore.Total;

        // partial: 30 s left -> 18 s
        var o = PickUp(r, 30f);
        Check("pickup counts a capacitor (" + o.pickups + ")", o.pickups == 1);
        Check("scores ScoreRules.CooldownAtom as an atom (" + o.atomPoints + ", count +" + o.atomCount + ")",
              o.atomPoints == ScoreRules.CooldownAtom && o.atomCount == 1 && RunScore.Total == total + ScoreRules.CooldownAtom);
        Check("... with its HUD popup (" + o.scored + " " + o.source + ")",
              o.scoredEvents == 1 && o.scored == ScoreRules.CooldownAtom && o.source == RunScore.Source.Atom);
        Check("30 s left: cut by exactly 12 s (" + c.SecondsLeft + ")", Mathf.Approximately(c.SecondsLeft, 18f));
        Check("partial cut reads \"-12s CHARGE\" (" + o.text + ")", o.text == "-12s CHARGE");
        Check("the weapon didn't fire, the cooldown wasn't rerolled, no free shot",
              c.Runner.FireCount == 0 && c.Indicator.ReleaseCount == 0 && c.FreeShotsFired == 0 &&
              c.PendingFreeShots == 0 && Mathf.Approximately(Cooldown(c), cooldown) &&
              !ShipPowerController.CinematicClearActive);
        Check("the charge indicator pulses violet (not the red free-shot flash)",
              c.Indicator.ChargeFlashCount == 1 && c.Indicator.ChargeFlashing && !c.Indicator.FreeFlashing &&
              c.Indicator.FreeFlashCount == 0);
        c.Indicator.Step(.02f, ChargeIndicator.ChargeFlashSeconds + .05f);
        Check("... briefly", !c.Indicator.ChargeFlashing);
        Check("the burst plays the cooldown frames (" + o.burstSprite + ")",
              o.burstSprite != null && o.burstSprite.StartsWith("cooldown_burst"));

        // 12.4 s left: still a full 12 s cut, not charged
        o = PickUp(r, 12.4f);
        Check("12.4 s left: cut 12 s, \"-12s CHARGE\" (" + c.SecondsLeft + ", " + o.text + ")",
              Mathf.Abs(c.SecondsLeft - .4f) < 1e-4f && o.text == "-12s CHARGE");

        // full: 5 s left -> 0, cut only 5
        SetTimer(c, 5f);
        float cut = c.ReduceWeaponCooldown();
        Check("5 s left: the cut is min(12, 5) = " + cut, Mathf.Approximately(cut, 5f) && c.SecondsLeft == 0f);
        o = PickUp(r, 5f);
        Check("5 s left: emptied to 0, \"WEAPON CHARGED\" (" + c.SecondsLeft + ", " + o.text + ")",
              c.SecondsLeft == 0f && o.text == ShipPowerController.WeaponChargedLabel);
        Check("... and still nothing fired on the pickup", c.Runner.FireCount == 0 && c.Indicator.ReleaseCount == 0);

        o = PickUp(r, 12f);
        Check("exactly 12 s left: \"WEAPON CHARGED\" (" + o.text + ")",
              c.SecondsLeft == 0f && o.text == ShipPowerController.WeaponChargedLabel);

        Check("each capacitor scored once into the atoms (count " + RunScore.Parts.atomCount + ")",
              RunScore.Parts.atomCount == 4 && RunScore.Parts.atoms == 4L * ScoreRules.CooldownAtom);

        RunScore.EndRun(RunScore.RunId);
        Teardown(r);
    }

    // ---- flight -----------------------------------------------------------

    static void Drifts()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        if (prefab == null) { Check("prefab for flight", false); return; }
        const float dt = 1f / 60f;
        foreach (float speed in new[] { 0f, .15f, LoopRules.AbsoluteMaxSpeed })
        {
            Random.InitState(7 + (int)(speed * 100f));
            var go = AtomSpin.AddTo(Object.Instantiate(prefab, new Vector3(0f, 5.5f, 0f), Quaternion.identity));
            var spin = go.GetComponent<AtomSpin>();
            var scroller = go.GetComponent<moveItemEnmInStrightLine>();
            string who = "capacitor HUD " + Mathf.RoundToInt(speed * 100f);
            Check(who + ": spins and rides the world scroller", spin != null && scroller != null);
            if (scroller == null) { Object.DestroyImmediate(go); continue; }
            float t = 0f, ceiling = AtomWander.Ceiling(5f), maxY = float.MinValue;
            bool entered = false, gone = false, wanders = false;
            while (t < 20f)
            {
                go.transform.Rotate(0f, 0f, spin.degreesPerSecond * dt);
                if (!scroller.Step(dt, speed, 5f, -5f)) { gone = true; break; }
                wanders |= scroller.Wander != null;
                t += dt;
                float y = go.transform.position.y;
                if (y <= ceiling) entered = true;
                if (entered) maxY = Mathf.Max(maxY, y);
            }
            Check(who + ": drifts on AtomWander, never back over the ceiling (max " + maxY.ToString("F2") + ")",
                  wanders && maxY <= ceiling + 1e-4f);
            Check(who + ": falls out the bottom and is removed (" + t.ToString("F1") + " s)", gone && go == null);
            if (go != null) Object.DestroyImmediate(go);
        }
    }

    // ---- tutorial ---------------------------------------------------------

    static void NotInTutorial()
    {
        Check("the tutorial spawner knows nothing of the capacitor",
              !Source("Assets/Scripts/Tutorial/spawnGoodStuffTut.cs").Contains("cooldown"));
        string guid = AssetDatabase.AssetPathToGUID("Assets/Scripts/Gameplay/spawnGoodStuff.cs");
        string tut = Source("Assets/Scenes/tutorialS5.unity");
        Check("tutorialS5 has no real-game spawner and no capacitor",
              !string.IsNullOrEmpty(guid) && !tut.Contains(guid) &&
              !tut.Contains(AssetDatabase.AssetPathToGUID(PrefabPath)));
        RunScore.EndRun(RunScore.RunId);
        RunScore.BeginRun(false, false);   // the tutorial's run never scores
        Check("a capacitor in a non-scoring (tutorial) run pays nothing",
              RunScore.OnAtom(RunScore.Atom.Cooldown, Vector3.zero) == 0);
        RunScore.EndRun(RunScore.RunId);
    }

    // ---- allocations ------------------------------------------------------

    // The capacitor branch's own work: score + popup event, the charge cut,
    // the violet pulse, the word, and the pooled burst. (The trigger's name
    // read and an edit-mode Destroy aren't the branch's.)
    static void NoAllocations()
    {
        FreshScene();
        RunScore.EndRun(RunScore.RunId);
        PlayerPrefs.SetString("HasDoneTut", "true");
        startMenu.youAreInTutorial = false;
        RunScore.BeginRun(true, true);
        PickupBurst.Prewarm();
        var r = Ship(4);
        var c = r.c;
        var atoms = new GameObject[24];
        for (int i = 0; i < atoms.Length; i++) atoms[i] = NewAtom(new Vector3(0f, -2f, 0f));
        Action<int, Vector3, RunScore.Source> sink = (p, w, s) => { };
        RunScore.Scored += sink;
        string word = null;
        try
        {
            // warm up: label cache, burst frames, pool
            for (int i = 0; i < 4; i++)
            {
                SetTimer(c, i % 2 == 0 ? 30f : 4f);
                RunScore.OnAtom(RunScore.Atom.Cooldown, atoms[i].transform.position);
                word = c.CollectCooldownAtom();
                var b = PickupBurst.Play(atoms[i]);
                if (b != null) PickupBurst.LastPlayed.Finish();
                c.Indicator.Step(.02f, .02f);
            }
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 4; i < atoms.Length; i++)
            {
                SetTimer(c, i % 2 == 0 ? 30f : 4f);
                RunScore.OnAtom(RunScore.Atom.Cooldown, atoms[i].transform.position);
                word = c.CollectCooldownAtom();
                var b = PickupBurst.Play(atoms[i]);
                if (b != null) PickupBurst.LastPlayed.Finish();
                c.Indicator.Step(.02f, .02f);
            }
            long bytes = GC.GetAllocatedBytesForCurrentThread() - before;
            Check("a capacitor pickup allocates nothing (" + bytes + " B over 20)", bytes == 0 && word != null);
        }
        finally
        {
            RunScore.Scored -= sink;
            foreach (var a in atoms) if (a != null) Object.DestroyImmediate(a);
            RunScore.EndRun(RunScore.RunId);
            Teardown(r);
        }
    }
}
