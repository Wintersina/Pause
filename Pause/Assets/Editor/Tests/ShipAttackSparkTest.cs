using System.Collections.Generic;
using UnityEditor.SceneManagement;
using UnityEngine;

// Volt Viper's chain lightning (ShipAttack.ChainLightning): a spark leaves the nose, homes onto an ENEMY, then the
// chain jumps between enemies. It may never chase or hop to bullets, boss shots, mines, rocks, pickups or lasers
// (ShipAttackTargets.IsEnemy). Run on CI only.
public static class ShipAttackSparkTest
{
    static int fails;
    static void Check(string what, bool ok)
    {
        Debug.Log((ok ? "[SPARK] PASS  " : "[SPARK] FAIL  ") + what);
        if (!ok) fails++;
    }

    public static void Run() { TestHarness.Exit(Execute()); }

    public static int Execute()
    {
        fails = 0;
        using var sandbox = new TestHarness.Sandbox();
        TextAndTuning();
        HomesOntoTheNearestEnemy();
        NeverTargetsNonEnemies();
        OnlyNonEnemiesFizzles();
        ChainHopsSkipProjectiles();
        NoTargetFizzles();
        RetargetsWhenTargetDies();
        FiresOncePerCharge();
        BossIsTargetedUnderItsHitWindow();
        AttackPool.StopAll();
        Debug.Log("[SPARK] failures: " + fails);
        return fails;
    }

    // ---- fixtures ---------------------------------------------------------

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
        collisionDetection.cloakTimer = 0f;
        moveBackGround.speed = .2f;
        Time.timeScale = 1f;
    }

    static ShipPowerController Viper()
    {
        PlayerPrefs.SetInt("spawnShip", 2);
        var go = new GameObject("viper", typeof(SpriteRenderer));
        go.transform.position = new Vector3(0f, -3f, 0f);
        var c = go.AddComponent<ShipPowerController>();
        c.SendMessage("Awake");
        c.SendMessage("Start");
        return c;
    }

    static void Teardown(ShipPowerController c)
    {
        if (c == null) return;
        c.SendMessage("OnDestroy");
        if (c.Runner != null) c.Runner.SendMessage("OnDestroy");
        if (c.Secret != null) c.Secret.SendMessage("OnDestroy");
        Object.DestroyImmediate(c.gameObject);
        AttackPool.StopAll();
    }

    static void Steps(ShipAttackRunner r, float seconds, float dt = .02f)
    {
        for (float t = 0f; t < seconds; t += dt) r.Step(dt);
    }

    static GameObject Make(string name, string tag, float x, float y)
    {
        var go = new GameObject(name);
        go.tag = tag;
        go.transform.position = new Vector3(x, y, 0f);
        ClearTarget.Ensure(go).SetRadius(.3f);
        return go;
    }

    static GameObject Enemy(float x, float y) => Make("enemy", "Enimey", x, y);

    // one of each kind of thing that is NOT an enemy, at (x, y)
    static List<GameObject> NonEnemies(float x, float y)
    {
        var l = new List<GameObject>();
        var bullet = Make("enemy bullet", "Enimey", x, y); bullet.AddComponent<EliteShotHitbox>(); l.Add(bullet);
        var bossHost = new GameObject("boss shot"); bossHost.AddComponent<BossProjectile>();
        var bossShot = Make("hitbox", "Enimey", x, y + .2f); bossShot.transform.SetParent(bossHost.transform, true); l.Add(bossShot); l.Add(bossHost);
        var laser = Make("mine laser", "Enimey", x, y - .2f); laser.AddComponent<RailMineLaserHitbox>(); l.Add(laser);
        var zone = Make("area hazard", "Enimey", x + .2f, y); zone.AddComponent<AttackHazardHitbox>(); l.Add(zone);
        var mine = Make("mine", "Enimey", x - .2f, y); mine.AddComponent<RailMineMount>(); l.Add(mine);
        var rock = Make("rock", "Astr", x, y + .4f); l.Add(rock);
        var pickup = Make("atom", "pickUp", x, y - .4f); l.Add(pickup);
        var scenery = Make("backdrop rock", "Enimey", x + .4f, y); ClearTarget.Ensure(scenery).Scenery = true; l.Add(scenery);
        return l;
    }

    static void Destroy(IEnumerable<GameObject> gos) { foreach (var g in gos) if (g != null) Object.DestroyImmediate(g); }

    // ---- cases -------------------------------------------------------------

    static void TextAndTuning()
    {
        var l = ShipLoadoutTable.For(2);
        Check("Volt Viper still chains lightning", l.attack == ShipAttack.ChainLightning && l.attackName == "CHAIN LIGHTNING");
        Check("its line names the spark", l.attackLine == "A spark homes onto an enemy, then chains lightning between enemies.");
        Check("chain tuning kept (5 hops, range 4.6, width 2.5)", l.maxHits == 5 && Mathf.Approximately(l.range, 4.6f) && Mathf.Approximately(l.width, 2.5f));
        Check("spark speed within 14-18 u/s", ShipAttackRunner.SparkSpeed >= 14f && ShipAttackRunner.SparkSpeed <= 18f);
    }

    static void HomesOntoTheNearestEnemy()
    {
        FreshScene();
        var c = Viper();
        var far = Enemy(.8f, 1.5f);
        var near = Enemy(-1f, 0f);
        var junk = NonEnemies(0f, -1.5f);   // nearer than either, all of it ignorable
        int kills = ShipAttackHits.Kills;
        c.Runner.Fire();
        Steps(c.Runner, .06f);
        Check("a spark is flying", c.Runner.ActiveRuns == 1 && ShipAttackHits.Kills == kills);
        Steps(c.Runner, 1.2f);
        Check("the nearest enemy was struck first", near == null);
        Check("then the chain reached the next one", far == null);
        Check("nothing that is not an enemy was hit", AllAlive(junk));
        Teardown(c);
        Destroy(junk);
    }

    static bool AllAlive(List<GameObject> l) { foreach (var g in l) if (g == null) return false; return true; }

    static void NeverTargetsNonEnemies()
    {
        FreshScene();
        var c = Viper();
        var enemy = Enemy(1.5f, 2f);
        var junk = NonEnemies(0f, -1f);   // right in front of the nose
        for (int i = 0; i < junk.Count; i++)
            if (junk[i] != null && junk[i].TryGetComponent(out ClearTarget t))
                Check("IsEnemy refuses " + junk[i].name, !ShipAttackTargets.IsEnemy(t));
        Check("IsEnemy accepts a plain enemy", ShipAttackTargets.IsEnemy(enemy.GetComponent<ClearTarget>()));
        c.Runner.Fire();
        Steps(c.Runner, 1.5f);
        Check("the spark went for the enemy, not what sat in front of it", enemy == null && AllAlive(junk));
        Teardown(c);
        Destroy(junk);
    }

    static void OnlyNonEnemiesFizzles()
    {
        FreshScene();
        var c = Viper();
        var junk = NonEnemies(0f, 0f);
        int kills = ShipAttackHits.Kills;
        c.Runner.Fire();
        Steps(c.Runner, 3f);
        Check("with only non-enemies on screen the spark fizzles", c.Runner.ActiveRuns == 0 && AllAlive(junk) && ShipAttackHits.Kills == kills);
        Teardown(c);
        Destroy(junk);
    }

    static void ChainHopsSkipProjectiles()
    {
        FreshScene();
        var c = Viper();
        var a = Enemy(0f, 0f);
        var b = Enemy(1.6f, 0f);     // within hop reach, but a bullet sits closer to `a`
        var junk = NonEnemies(.5f, .1f);
        c.Runner.Fire();
        Steps(c.Runner, 1.5f);
        Check("both enemies fell", a == null && b == null);
        Check("every hop skipped the bullets, mines and rocks", AllAlive(junk));
        Teardown(c);
        Destroy(junk);
    }

    static void NoTargetFizzles()
    {
        FreshScene();
        var c = Viper();
        int launches = AttackProjectile.LaunchCount;
        c.Runner.Fire();
        Steps(c.Runner, .1f);
        Check("it flies straight up with no enemy", c.Runner.ActiveRuns == 1);
        Steps(c.Runner, 2.5f);
        Check("and fizzles at the top, run over", c.Runner.ActiveRuns == 0 && AttackProjectile.LaunchCount == launches);
        Teardown(c);
    }

    static void RetargetsWhenTargetDies()
    {
        FreshScene();
        var c = Viper();
        var first = Enemy(0f, 3f);
        var second = Enemy(2.2f, 2f);
        c.Runner.Fire();
        Steps(c.Runner, .05f);
        Object.DestroyImmediate(first);   // killed by something else mid-flight
        Steps(c.Runner, 1.5f);
        Check("the spark retargeted and struck the other enemy", second == null);
        Teardown(c);
    }

    static void FiresOncePerCharge()
    {
        FreshScene();
        var c = Viper();
        var e = Enemy(0f, 1f);
        int before = c.Runner.FireCount;
        c.Runner.Fire();
        Steps(c.Runner, 1.5f);
        Check("one Fire = one run, one strike", c.Runner.FireCount == before + 1 && c.Runner.ActiveRuns == 0 && e == null);
        int kills = ShipAttackHits.Kills;
        Steps(c.Runner, 1f);
        Check("nothing fires again by itself", ShipAttackHits.Kills == kills && c.Runner.ActiveRuns == 0);
        Teardown(c);
    }

    // The spark counts the boss as a target (it is hit through BossTarget, which owns the 0.25 s hit window) and while a
    // boss is up it chases only the boss.
    static void BossIsTargetedUnderItsHitWindow()
    {
        FreshScene();
        var boss = Make("boss body", "Enimey", 0f, 3f);
        boss.AddComponent<BossTarget>();
        var minion = Enemy(.3f, 0f);
        var bt = boss.GetComponent<ClearTarget>();
        Check("the boss body is an enemy", ShipAttackTargets.IsEnemy(bt));
        Check("with a boss up the spark may only home on the boss", ShipAttackTargets.CanTarget(bt, true) && !ShipAttackTargets.CanTarget(minion.GetComponent<ClearTarget>(), true));
        Object.DestroyImmediate(boss);
        Object.DestroyImmediate(minion);
    }
}
