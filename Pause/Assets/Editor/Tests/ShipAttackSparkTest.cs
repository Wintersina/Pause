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
        ArcDrawnBetweenEachLinkedPair();
        ArcLooksLikeLightning();
        NoEnemyLinksTheSparks();
        VolleyLinkCountIsCapped();
        VolleySwitchesBackToEnemies();
        VolleyArcHurtsACrossingEnemyOnce();
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
        var go = new GameObject("ship2", typeof(SpriteRenderer));   // the ship id is read from the object name (ShipId.Of)
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
        Check("a spark is flying (runs " + c.Runner.ActiveRuns + ", kills " + (ShipAttackHits.Kills - kills) + ")", c.Runner.ActiveRuns == 1 && ShipAttackHits.Kills == kills);
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
        Check("and fizzles at the top, run over (runs " + c.Runner.ActiveRuns + ", launches " + (AttackProjectile.LaunchCount - launches) + ", live targets " + ClearTarget.Live.Count + ")", c.Runner.ActiveRuns == 0 && AttackProjectile.LaunchCount == launches);
        Teardown(c);
    }

    static void RetargetsWhenTargetDies()
    {
        FreshScene();
        var c = Viper();
        var first = Enemy(0f, 3f);
        var second = Enemy(2.2f, 3.5f);   // farther than `first`, so it is only chased once `first` is gone
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

    // ---- arcs and the no-enemy volley -------------------------------------

    // Was an arc a -> b drawn among the newest `window` arcs?
    static bool Drew(Vector2 a, Vector2 b, int window)
    {
        for (int i = 0; i < Mathf.Min(window, VoltArcs.LogCount); i++)
        {
            Vector2 la, lb;
            VoltArcs.LogAt(i, out la, out lb);
            if ((la - a).sqrMagnitude < .0025f && (lb - b).sqrMagnitude < .0025f) return true;
        }
        return false;
    }

    static void ArcDrawnBetweenEachLinkedPair()
    {
        FreshScene();
        VoltArcs.StopAll();
        var c = Viper();
        var e1 = Enemy(0f, -.5f);
        var e2 = Enemy(1.5f, .5f);
        var e3 = Enemy(1.5f, 2.2f);
        Vector2 p1 = e1.transform.position, p2 = e2.transform.position, p3 = e3.transform.position;
        int drawn = VoltArcs.TotalDrawn;
        c.Runner.Fire();
        Vector2 nose = c.Runner.Nose;
        Steps(c.Runner, 1.2f);
        Check("all three enemies fell", e1 == null && e2 == null && e3 == null);
        Check("an arc was drawn for the ship's link and each hop (" + (VoltArcs.TotalDrawn - drawn) + ")", VoltArcs.TotalDrawn - drawn == 3);
        Check("ship -> first target", Drew(nose, p1, 3));
        Check("first target -> second", Drew(p1, p2, 3));
        Check("second target -> third", Drew(p2, p3, 3));
        Teardown(c);
    }

    static void ArcLooksLikeLightning()
    {
        FreshScene();
        VoltArcs.StopAll();
        var c = Viper();
        var e = Enemy(0f, 1f);
        c.Runner.Fire();
        Steps(c.Runner, .3f);   // the spark arrives and strikes, the arc is on screen
        var arc = VoltArcs.LiveAt(0);
        Check("an arc is up after the strike", arc != null && e == null);
        if (arc != null)
        {
            Check("jagged: a polyline of " + VoltArcs.Segments + " points", arc.core.positionCount == VoltArcs.Segments && arc.outer.positionCount == VoltArcs.Segments);
            Check("bright thin core, wider faint outer line", arc.core.startWidth < arc.outer.startWidth && arc.core.startWidth <= .08f && arc.outer.startColor.a < .5f);
            Check("a small spark at each contact point", arc.starA.positionCount > 1 && arc.starB.positionCount > 1);
            Check("it lingers 0.15-0.25 s", arc.Life >= .15f && arc.Life <= .25f);
            int jags = arc.Jags;
            Steps(c.Runner, .1f);
            Check("it re-jags while it fades (" + jags + " -> " + arc.Jags + ")", arc.Jags > jags);
            Steps(c.Runner, .3f);
            Check("then it is gone", !arc.Live);
        }
        Teardown(c);
    }

    static void NoEnemyLinksTheSparks()
    {
        FreshScene();
        VoltArcs.StopAll();
        var c = Viper();
        int drawn = VoltArcs.TotalDrawn;
        c.Runner.Fire();
        Steps(c.Runner, .08f);
        Check("one spark out: one link, ship -> shot 1 (" + c.Runner.VolleyLinkCount + ")", c.Runner.VolleyLinkCount == 1);
        Steps(c.Runner, .3f);
        Check("three sparks out: ship -> 1 -> 2 -> 3 (" + c.Runner.VolleyLinkCount + " links)", c.Runner.VolleyLinkCount == ShipAttackRunner.VolleyShots);
        Check("the arcs are live on screen", VoltArcs.LiveCount >= ShipAttackRunner.VolleyShots);
        Check("held arcs are reused, not redrawn every frame (" + (VoltArcs.TotalDrawn - drawn) + ")", VoltArcs.TotalDrawn - drawn <= ShipAttackRunner.VolleyShots + 1);
        Steps(c.Runner, 3f);
        Check("the volley ends cleanly", c.Runner.ActiveRuns == 0 && c.Runner.VolleyLinkCount == 0);
        Teardown(c);
    }

    static void VolleyLinkCountIsCapped()
    {
        FreshScene();
        VoltArcs.StopAll();
        var c = Viper();
        int maxLive = 0;
        for (int k = 0; k < 3; k++)   // free shots overlap with the main one
        {
            c.Runner.FireFree();
            for (float t = 0f; t < .3f; t += .02f)
            {
                c.Runner.Step(.02f);
                maxLive = Mathf.Max(maxLive, VoltArcs.LiveCount);
            }
        }
        Check("the arc pool never grows past " + VoltArcs.MaxArcs + " (live " + maxLive + ", pool " + VoltArcs.PoolSize + ")", maxLive <= VoltArcs.MaxArcs && VoltArcs.PoolSize <= VoltArcs.MaxArcs);
        c.Runner.StopAll();
        c.Runner.Fire();
        int one = 0;
        for (float t = 0f; t < .6f; t += .02f) { c.Runner.Step(.02f); one = Mathf.Max(one, c.Runner.VolleyLinkCount); }
        Check("one volley links at most " + ShipAttackRunner.VolleyShots + " pairs (" + one + ")", one == ShipAttackRunner.VolleyShots);
        Teardown(c);
    }

    static void VolleySwitchesBackToEnemies()
    {
        FreshScene();
        VoltArcs.StopAll();
        var c = Viper();
        c.Runner.Fire();
        Steps(c.Runner, .3f);
        Check("no enemy: the sparks are linked", c.Runner.VolleyLinkCount == ShipAttackRunner.VolleyShots);
        var e = Enemy(.2f, 1.5f);
        Vector2 p = e.transform.position;
        Vector2 nose = c.Runner.Nose;
        Steps(c.Runner, .04f);
        Check("an enemy comes into range: the volley links stop", c.Runner.VolleyLinkCount == 0);
        Steps(c.Runner, 1.5f);
        Check("the chain took the enemy", e == null);
        Check("with an arc from the ship to it", Drew(nose, p, 2));
        Teardown(c);
    }

    // The boss is up but off screen, so the sparks have nothing they may home on and the volley flies; a minion
    // crossing the ship's first arc takes a hit.
    static void VolleyArcHurtsACrossingEnemyOnce()
    {
        FreshScene();
        VoltArcs.StopAll();
        var boss = Make("boss body", "Enimey", 0f, 8f);
        boss.AddComponent<BossTarget>();
        var c = Viper();
        var minion = Enemy(0f, -1f);   // straight above the nose, on the line to shot 1
        int kills = ShipAttackHits.Kills;
        c.Runner.Fire();
        Steps(c.Runner, .3f);
        Check("a plain enemy on the arc was struck (" + (ShipAttackHits.Kills - kills) + ")", minion == null && ShipAttackHits.Kills == kills + 1);
        Teardown(c);
        Object.DestroyImmediate(boss);
    }
}
