using System.Collections.Generic;
using System.Reflection;
using UnityEditor.SceneManagement;
using UnityEngine;

// DEATH COMBO (DeathCombo): a pilot kill sets off a combo ~40% of the time;
// each hazard in range of a combo death is hit ~40% of the time, one hop
// (HopDelay) later, and each such kill is a combo death of its own; the
// chain stops at MaxDepth and at gaps wider than the radius; an elite loses
// exactly one heart per combo hit (and none in its grace); the player and
// the boss are never touched; a paused world freezes the chain; the death
// crash clears it; chain victims never roll the trigger again; it works for
// weapon kills and shielded rams; a quiet step allocates nothing. And the
// death crash's chain kills carry the chain on ~40% of the time.
//
//   Unity -batchmode -quit -projectPath Pause -executeMethod DeathComboTest.Run
public static class DeathComboTest
{
    static int fails;
    const float Dt = 1f / 60f;
    const BindingFlags Inst = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    static readonly MethodInfo Trigger = typeof(collisionDetection).GetMethod("OnTriggerEnter2D", Inst);

    static void Check(string what, bool ok)
    {
        Debug.Log((ok ? "[DCB] PASS  " : "[DCB] FAIL  ") + what);
        if (!ok) fails++;
    }

    public static void Run()
    {
        TestHarness.Exit(Execute());
    }

    public static int Execute()
    {
        fails = 0;
        using var sandbox = new TestHarness.Sandbox();
        EditorSceneManager.OpenScene("Assets/Scenes/gameS1.unity", OpenSceneMode.Single);
        EliteCatalog.Reload();
        DeathCombo.ForceInEditor = true;
        try
        {
            Constants();
            TriggerRate();
            ChainRate();
            LineDomino();
            GapStops();
            DepthCap();
            Stagger();
            Elites();
            NeverThePlayerOrBoss();
            Paused();
            DeathCrashClears();
            WeaponAndRamKills();
            VictimsDontRetrigger();
            Allocations();
            DeathCrashChainRate();
            DeathCrashSpares();
        }
        finally
        {
            Clean();
            DeathCombo.ForceInEditor = false;
            DeathCombo.ForceTrigger = DeathCombo.ForceChain = DeathCombo.ForceDeathHit = DeathCombo.ForceDeathChain = -1f;
            DeathCrash.ForceMega = null;
            DeathCombo.ClearPending();
            RunScore.EndRun(RunScore.RunId);
            buttonClicks.playerDied = false;
            Time.timeScale = 1f;
        }
        Debug.Log("[DCB] failures: " + fails);
        return fails;
    }

    // ---------------------------------------------------------------- fixtures

    static readonly List<GameObject> spawned = new List<GameObject>();

    static void Clean()
    {
        foreach (var go in spawned) if (go != null) Object.DestroyImmediate(go);
        spawned.Clear();
        EliteSystem.Clear();
    }

    static void Fresh(uint seed)
    {
        Clean();
        DeathCombo.ClearPending();
        DeathCombo.ResetCounters();
        DeathCombo.Seed(seed);
        DeathCombo.ForceTrigger = DeathCombo.ForceChain = DeathCombo.ForceDeathHit = DeathCombo.ForceDeathChain = -1f;
        RunScore.EndRun(RunScore.RunId);
        RunScore.BeginRun(true, false);
        buttonClicks.playerDied = false;
        moveBackGround.speed = .3f;
        Time.timeScale = 1f;
    }

    // A real roster rock, its movers off.
    static GameObject Rock(Vector3 at)
    {
        var go = EnemyFactory.Create(EnemyRoster.One(0, EnemyRole.Rock), at, Quaternion.identity);
        var t = ClearTarget.Ensure(go);
        foreach (var b in go.GetComponents<MonoBehaviour>())
            if (!(b is EnemyIdentity) && !(b is ClearTarget)) b.enabled = false;
        // a fixed size so the spacing in these scenarios is exact
        (t != null ? t : go.GetComponent<ClearTarget>()).SetRadius(.3f);
        spawned.Add(go);
        return go;
    }

    // A vertical line of rocks up the middle of the view, `spacing` apart.
    static List<GameObject> Line(int n, float spacing, float gapAfter = -1, float gap = 0f)
    {
        var list = new List<GameObject>();
        float y = CameraFit.ViewBottom + .8f;
        for (int i = 0; i < n; i++)
        {
            list.Add(Rock(new Vector3(0f, y, 0f)));
            y += i == gapAfter ? gap : spacing;
        }
        return list;
    }

    static void Steps(float seconds, float dt = Dt)
    {
        for (float t = 0f; t < seconds; t += dt) DeathCombo.Step(dt);
    }

    // Sets off a combo at `wreck` (forced), as a pilot kill would.
    static void Ignite(GameObject wreck)
    {
        float keep = DeathCombo.ForceTrigger;
        DeathCombo.ForceTrigger = 1f;
        DeathCombo.OnPlayerKill(wreck);
        DeathCombo.ForceTrigger = keep;
        ClearTarget.Release(wreck);
        Object.DestroyImmediate(wreck);
    }

    static int Alive(List<GameObject> list)
    {
        int n = 0;
        foreach (var go in list) if (go != null) n++;
        return n;
    }

    static readonly FieldInfo GraceField = typeof(EliteShip).GetField("grace", Inst);
    static void NoGrace(EliteShip e) { if (e != null && GraceField != null) GraceField.SetValue(e, 0f); }

    static bool Near(float value, float want, float band) => Mathf.Abs(value - want) <= band;

    // ---------------------------------------------------------------- suites

    static void Constants()
    {
        Check("tuning: trigger 40%, chain 40%, death chain 40%",
              DeathCombo.TriggerChance == .40f && DeathCombo.ChainChance == .40f && DeathCombo.DeathChainChance == .40f);
        Check("tuning: radius ~1.4, depth 6, hop .08-.12 s",
              Near(DeathCombo.BlastRadius, 1.4f, .2f) && DeathCombo.MaxDepth == 6 &&
              DeathCombo.HopDelay >= .08f && DeathCombo.HopDelay <= .12f);
    }

    // Over many pilot kills, ~40% set off a combo.
    static void TriggerRate()
    {
        Fresh(101);
        var rock = Rock(new Vector3(0f, 0f, 0f));
        const int N = 4000;
        for (int i = 0; i < N; i++)
        {
            DeathCombo.OnPlayerKill(rock);
            DeathCombo.ClearPending();
        }
        float rate = DeathCombo.Triggers / (float)N;
        Check("trigger rate over " + N + " kills ~40% (" + (rate * 100f).ToString("F1") + "%)",
              DeathCombo.TriggerRolls == N && Near(rate, .40f, .03f));
    }

    // Each hazard in range of a combo death is hit ~40% of the time.
    static void ChainRate()
    {
        Fresh(202);
        DeathCombo.ForceChain = -1f;
        int rolls = 0, hits = 0;
        // one neighbour in range of each burst, depth-limited by being alone
        for (int trial = 0; trial < 600; trial++)
        {
            var wreck = Rock(new Vector3(-1f, 0f, 0f));
            var other = Rock(new Vector3(0f, 0f, 0f));
            int before = DeathCombo.ChainRolls;
            Ignite(wreck);
            Steps(.25f);
            if (DeathCombo.ChainRolls > before) rolls++;
            if (other == null) hits++;
            if (other != null) Object.DestroyImmediate(other);
            DeathCombo.ClearPending();
        }
        float rate = rolls > 0 ? hits / (float)rolls : 0f;
        Check("chain: every in-range neighbour is rolled for (" + rolls + "/600)", rolls == 600);
        Check("chain rate per in-range hazard ~40% (" + (rate * 100f).ToString("F1") + "%)", Near(rate, .40f, .05f));
        float wins = DeathCombo.ChainRolls > 0 ? DeathCombo.ChainWins / (float)DeathCombo.ChainRolls : 0f;
        Check("chain: wins / rolls over every roll ~40% (" + (wins * 100f).ToString("F1") + "%)", Near(wins, .40f, .05f));
        Clean();
    }

    // Forced to always chain, a line of rocks inside the radius goes one
    // after another to the end.
    static void LineDomino()
    {
        Fresh(303);
        DeathCombo.ForceChain = 1f;
        var line = Line(6, 1f);
        Ignite(line[0]);
        Steps(2f);
        Check("line: the domino runs down the line (" + Alive(line) + " left)", Alive(line) == 0);
        Check("line: five links, deepest hop 5 (" + DeathCombo.Kills + ", " + DeathCombo.Deepest + ")",
              DeathCombo.Kills == 5 && DeathCombo.Deepest == 5);
        Check("line: nothing pending or lingering after", DeathCombo.Pending == 0);
    }

    // A gap wider than the radius stops it.
    static void GapStops()
    {
        Fresh(404);
        DeathCombo.ForceChain = 1f;
        var line = Line(6, 1f, 2, DeathCombo.BlastRadius + 1.2f);   // rocks 0-2, then a gap, 3-5
        Ignite(line[0]);
        Steps(2f);
        Check("gap: the chain stops at the gap (" + DeathCombo.Kills + " kills, " + Alive(line) + " left)",
              DeathCombo.Kills == 2 && line[3] != null && line[4] != null && line[5] != null);
    }

    // A long line: at most MaxDepth hops from the pilot's kill.
    static void DepthCap()
    {
        Fresh(505);
        DeathCombo.ForceChain = 1f;
        var line = Line(10, .9f);
        Ignite(line[0]);
        Steps(3f);
        Check("depth: " + DeathCombo.MaxDepth + " hops at most (" + DeathCombo.Kills + " kills, deepest " + DeathCombo.Deepest + ")",
              DeathCombo.Deepest == DeathCombo.MaxDepth && DeathCombo.Kills == DeathCombo.MaxDepth);
        Check("depth: the rest of the line survives (" + Alive(line) + ")", Alive(line) == 9 - DeathCombo.MaxDepth);
    }

    // One hop every HopDelay: a wave, not a mass delete.
    static void Stagger()
    {
        Fresh(606);
        DeathCombo.ForceChain = 1f;
        var line = Line(5, 1f);
        Ignite(line[0]);
        var died = new float[line.Count];
        for (int i = 0; i < died.Length; i++) died[i] = -1f;
        float t = 0f;
        for (int f = 0; f < 120; f++)
        {
            DeathCombo.Step(Dt);
            t += Dt;
            for (int i = 1; i < line.Count; i++) if (died[i] < 0f && line[i] == null) died[i] = t;
        }
        bool ok = died[1] > 0f && Near(died[1], DeathCombo.HopDelay, Dt * 1.5f + .001f);
        string times = "";
        for (int i = 1; i < line.Count; i++)
        {
            times += died[i].ToString("F3") + " ";
            if (died[i] < 0f) { ok = false; continue; }
            if (i > 1 && !Near(died[i] - died[i - 1], DeathCombo.HopDelay, Dt + .001f)) ok = false;
        }
        Check("stagger: one link every " + DeathCombo.HopDelay + " s (" + times.Trim() + ")", ok);
    }

    static void Elites()
    {
        Fresh(707);
        DeathCombo.ForceChain = 1f;
        var defs = EliteCatalog.All;
        if (defs == null || defs.Length == 0) { Check("elites: one to test with", false); return; }
        var def = defs[0];
        foreach (var d in defs) if (d.hearts >= 2) { def = d; break; }
        var e = EliteShip.CreateInPlay(def, new Vector2(.8f, 0f));
        spawned.Add(e.gameObject);
        int hearts = e.Hearts;
        Ignite(Rock(new Vector3(0f, 0f, 0f)));
        Steps(.3f);
        Check("elite: a combo hit costs exactly one heart (" + hearts + " -> " + (e != null ? e.Hearts : -1) + ")",
              e != null && e.Hearts == hearts - 1 && e.LastHitCause == EliteDamage.Combo);
        Check("elite: it is not one-shot, and a heart lost is no death: no burst from it (" + DeathCombo.Bursts + ")",
              e != null && e.State != EliteState.Dead && DeathCombo.Bursts == 1);
        // In its grace: a second combo right away costs nothing.
        int h2 = e.Hearts;
        Ignite(Rock(new Vector3(0f, 0f, 0f)));
        Steps(.3f);
        Check("elite: no heart lost in its grace (" + h2 + " -> " + e.Hearts + ")", e.Hearts == h2);
        // Its last heart: the combo kills it, and its death is a combo death.
        while (e != null && e.Hearts > 1) { NoGrace(e); e.TakeHit(EliteDamage.PlayerWeapon, e.transform.position); }
        NoGrace(e);
        var beyond = Rock(new Vector3(1.9f, 0f, 0f));
        int bursts = DeathCombo.Bursts;
        Ignite(Rock(new Vector3(0f, 0f, 0f)));
        Steps(.5f);
        Check("elite: the last heart goes to the combo, and its death chains on (" + (DeathCombo.Bursts - bursts) + " bursts)",
              (e == null || e.State == EliteState.Dead) && DeathCombo.Bursts - bursts >= 2 && beyond == null);
        Clean();
    }

    static void NeverThePlayerOrBoss()
    {
        Fresh(808);
        DeathCombo.ForceChain = 1f;
        var rig = new DeathCrashTest.Rig(1);
        collisionDetection.lifeCounter = 0;
        rig.ship.transform.position = new Vector3(.5f, 0f, 1f);
        var boss = new GameObject("BossBody", typeof(SpriteRenderer), typeof(CircleCollider2D));
        boss.tag = "Enimey";
        boss.transform.position = new Vector3(-.6f, 0f, 0f);
        boss.AddComponent<BossTarget>();
        ClearTarget.Ensure(boss);
        spawned.Add(boss);
        Ignite(Rock(new Vector3(0f, 0f, 0f)));
        Steps(.5f);
        Check("the player is never hit (hearts lost " + collisionDetection.lifeCounter + ", died " + buttonClicks.playerDied + ")",
              collisionDetection.lifeCounter == 0 && !buttonClicks.playerDied && rig.ship != null);
        Check("the boss is never chained (rolls " + DeathCombo.ChainRolls + ")", boss != null && DeathCombo.ChainRolls == 0);
        rig.Dispose();
        Clean();
    }

    static void Paused()
    {
        Fresh(909);
        DeathCombo.ForceChain = 1f;
        var line = Line(3, 1f);
        Ignite(line[0]);
        Time.timeScale = 0f;
        var runtime = HazardRuntime.Ensure();
        for (int i = 0; i < 120; i++) runtime.Step(TargetExplosion.Delta());
        Check("paused: the combo holds (" + DeathCombo.Pending + " pending, " + Alive(line) + " alive)",
              DeathCombo.Pending == 1 && line[1] != null && line[2] != null);
        Time.timeScale = 1f;
        for (int i = 0; i < 60; i++) runtime.Step(Dt);
        Check("resumed: it plays out through HazardRuntime (" + Alive(line) + " alive)", line[1] == null && line[2] == null);
    }

    static void DeathCrashClears()
    {
        Fresh(1001);
        DeathCombo.ForceChain = 1f;
        var line = Line(3, 1f);
        Ignite(line[0]);
        buttonClicks.playerDied = true;
        Steps(.5f);
        Check("a player death clears the queue (" + DeathCombo.Pending + " pending, " + Alive(line) + " alive)",
              DeathCombo.Pending == 0 && line[1] != null);
        int rolls = DeathCombo.TriggerRolls;
        DeathCombo.ForceTrigger = 1f;
        DeathCombo.OnPlayerKill(line[1]);
        Check("no new combo while dead", DeathCombo.TriggerRolls == rolls && DeathCombo.Pending == 0);
        buttonClicks.playerDied = false;
    }

    static void WeaponAndRamKills()
    {
        Fresh(1101);
        DeathCombo.ForceTrigger = 1f;
        DeathCombo.ForceChain = 1f;
        var a = Rock(new Vector3(0f, 0f, 0f));
        var b = Rock(new Vector3(.9f, 0f, 0f));
        long before = RunScore.Total;
        ShipAttackHits.Hit(a, 1);
        Check("weapon kill: sets off a combo", DeathCombo.Triggers == 1 && DeathCombo.Pending == 1);
        Steps(.3f);
        Check("weapon kill: its neighbour goes and pays like a kill (" + before + " -> " + RunScore.Total + ")",
              b == null && RunScore.Parts.killCount == 2 && RunScore.Total > before);

        Fresh(1102);
        DeathCombo.ForceTrigger = 1f;
        DeathCombo.ForceChain = 1f;
        var rig = new DeathCrashTest.Rig(1);
        collisionDetection.lifeCounter = 0;
        collisionDetection.atomCheck = true;   // the boost shield is up
        PlayerInvuln.Reset();
        rig.ship.transform.position = new Vector3(0f, -1f, 1f);
        var rammed = Rock(new Vector3(0f, -.7f, 0f));
        if (rammed.GetComponent<Collider2D>() == null) rammed.AddComponent<CircleCollider2D>();
        var next = Rock(new Vector3(.9f, -.2f, 0f));
        Trigger.Invoke(rig.cd, new object[] { rammed.GetComponent<Collider2D>() });
        Check("shielded ram: sets off a combo (" + DeathCombo.Triggers + ")", DeathCombo.Triggers == 1);
        Steps(.3f);
        Check("shielded ram: the chain takes the neighbour, never the ship",
              next == null && collisionDetection.lifeCounter == 0 && !buttonClicks.playerDied);
        collisionDetection.atomCheck = false;
        rig.Dispose();
        Clean();
    }

    // Chain victims only spread through the chain roll.
    static void VictimsDontRetrigger()
    {
        Fresh(1201);
        DeathCombo.ForceChain = 1f;
        var line = Line(4, 1f);
        Ignite(line[0]);
        int rolls = DeathCombo.TriggerRolls;
        Steps(1f);
        Check("victims never roll the trigger (" + rolls + " -> " + DeathCombo.TriggerRolls + ", " + DeathCombo.Kills + " kills)",
              DeathCombo.TriggerRolls == rolls && DeathCombo.Kills == 3 && DeathCombo.Triggers == 1);
    }

    static void Allocations()
    {
        Fresh(1301);
        if (!TestHarness.AllocMeterWorks(out long control)) { Check("allocations: meter unavailable (skipped)", true); return; }
        // warm
        DeathCombo.Step(Dt);
        long quiet = TestHarness.AllocatedBytes(() => { for (int i = 0; i < 200; i++) DeathCombo.Step(Dt); });
        Check("a quiet step allocates nothing (" + quiet + " B)", quiet == 0);
        var far = Rock(new Vector3(3f, 3f, 0f));
        var wreck = Rock(new Vector3(-1.5f, -2f, 0f));
        DeathCombo.ForceTrigger = 1f;
        DeathCombo.OnPlayerKill(wreck);
        DeathCombo.Step(Dt);   // warm the burst path
        DeathCombo.OnPlayerKill(wreck);
        long burst = TestHarness.AllocatedBytes(() => DeathCombo.Step(Dt));
        Check("a burst with nothing in range allocates nothing (" + burst + " B)", burst == 0 && far != null);
        Check("nothing lingers (" + DeathCombo.Pending + " pending)", DeathCombo.Pending == 0);
    }

    // The death crash: the wreckage breaks what it touches ~40% of the
    // time, and each chain kill's pieces are live ~40% of the time.
    static void DeathCrashChainRate()
    {
        Fresh(1401);
        int n = 4000, live = 0, hit = 0;
        for (int i = 0; i < n; i++) if (DeathCombo.RollDeathChain()) live++;
        for (int i = 0; i < n; i++) if (DeathCombo.RollDeathHit()) hit++;
        float rate = live / (float)n, hitRate = hit / (float)n;
        Check("death crash: the wreckage breaks an enemy it touches ~40% (" + (hitRate * 100f).ToString("F1") + "%)",
              Near(hitRate, .40f, .03f) && DeathCombo.DeathHitRolls == n);
        Check("death crash: a chain kill carries the chain on ~40% (" + (rate * 100f).ToString("F1") + "%)",
              Near(rate, .40f, .03f) && DeathCombo.DeathChainRolls == n);
    }

    // A lost roll: the wreckage glances off a rock on its path and spares
    // it (rolled once, not once per frame of contact); no DEATH COMBO.
    static void DeathCrashSpares()
    {
        Fresh(1501);
        DeathCrash.ForceMega = false;
        DeathCombo.ForceDeathHit = 0f;
        var rig = new DeathCrashTest.Rig(1);
        rig.ship.transform.position = new Vector3(0f, -2.2f, 1f);
        var rock = Rock(new Vector3(0f, CameraFit.ViewTop - .5f, 0f));
        var k = DeathCrashTest.Killer("aestroid_1", "Astr", rig.ship.transform.position + new Vector3(.15f, .4f, 0f));
        rig.Hit(k);
        Object.DestroyImmediate(k);
        var crash = DeathCrash.Instance;
        bool placed = false;
        for (float t = 0f; DeathCrash.Running && t < 14f; t += Dt)
        {
            if (!placed && rock != null)
                for (int i = 0; i < crash.PieceCount; i++)
                    if (crash.IsMain(i) && crash.Flying(i))
                    {
                        rock.transform.position = crash.PathAt(i, crash.ProgressOf(i) + .15f) + Vector3.back;
                        placed = true;
                    }
            crash.Step(Dt);
        }
        Check("death crash: a lost roll spares the rock on the wreckage's path (" + crash.DominoKills + " kills, " +
              DeathCombo.DeathHitRolls + " roll)", rock != null && crash.DominoKills == 0 && DeathCombo.DeathHitRolls == 1);
        Check("death crash: no DEATH COMBO when nothing broke", RunScore.Parts.deathCombo == 0);
        rig.Dispose();
        DeathCrash.ForceMega = null;
        buttonClicks.playerDied = false;
        Clean();
    }
}
