using System.Collections.Generic;
using UnityEditor.SceneManagement;
using UnityEngine;

// Feature: signature upgrade effects of Jade Phantom (chill), Gold Warden (pierce, drift), Paranoid (bigger
// blast) and Saboteur (bigger blast, chain), unlocked by weapon level on top of the plain tracks
// (ShipSignatures2). Each effect is OFF below its unlock level and ON at / after it.
//
//   Unity -batchmode -quit -projectPath Pause -executeMethod ShipSignatures2Test.Run
public static class ShipSignatures2Test
{
    static int fails;
    static void Check(string what, bool ok)
    {
        Debug.Log((ok ? "[SG2] PASS  " : "[SG2] FAIL  ") + what);
        if (!ok) fails++;
    }

    public static void Run() { TestHarness.Exit(Execute()); }

    public static int Execute()
    {
        fails = 0;
        using var sandbox = new TestHarness.Sandbox();

        TablesUnlockAtTheirLevels();
        LabelsMentionTheEffectsAndKeepTheNumbers();
        JadeChillsOutsideTheSplash();
        GoldBoltsPierceAndSentriesDrift();
        GoldSentriesIgnoreScenery();
        GoldTurretsAimAllAround();
        GoldSentriesCanBeShotDown();
        ParanoidBlastGrowsWithTheLevel();
        SaboteurBlastGrowsAndChains();

        AttackPool.StopAll();
        Debug.Log("[SG2] failures: " + fails);
        return fails;
    }

    // ---- fixtures ------------------------------------------------------

    static void SetLevel(int id, int level)
    {
        PlayerPrefs.SetInt(DeveloperUnlocks.EnabledKey, 0);
        for (int n = 1; n < ShipSkins.PerShip; n++)
        {
            if (n <= level) PlayerPrefs.SetInt(ShipSkins.OwnedKey(id, n), 1);
            else PlayerPrefs.DeleteKey(ShipSkins.OwnedKey(id, n));
        }
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
        moveBackGround.speed = 0f;   // a still world: mines and bodies stay where the test put them
        Time.timeScale = 1f;
        EnemyShove.Clear();
    }

    static GameObject Hazard(float x, float y)
    {
        var go = new GameObject("hazard");
        go.tag = "Enimey";
        go.transform.position = new Vector3(x, y, 0f);
        ClearTarget.Ensure(go).SetRadius(.3f);
        return go;
    }

    static bool Alive(GameObject go) { return go != null; }

    static ShipPowerController Ship(int id, int level)
    {
        SetLevel(id, level);
        PlayerPrefs.SetInt("spawnShip", id);
        var go = new GameObject("ship" + id, typeof(SpriteRenderer));
        go.transform.position = new Vector3(0f, -3f, 0f);
        var c = go.AddComponent<ShipPowerController>();
        c.SendMessage("Awake");
        c.SendMessage("Start");
        var gun = go.GetComponentInChildren<UltimateGun>();
        if (gun != null) gun.SendMessage("Awake");
        return c;
    }

    static void Teardown(ShipPowerController c, int id)
    {
        if (c != null)
        {
            c.SendMessage("OnDestroy");
            if (c.Runner != null) c.Runner.SendMessage("OnDestroy");
            if (c.Secret != null) c.Secret.SendMessage("OnDestroy");
            Object.DestroyImmediate(c.gameObject);
        }
        AttackPool.StopAll();
        EnemyShove.Clear();
        collisionDetection.cloakTimer = 0f;
        collisionDetection.lifeCounter = 0;
        SetLevel(id, 0);
    }

    static void Steps(ShipAttackRunner r, float seconds, float dt = .02f)
    {
        for (float t = 0f; t < seconds - 1e-4f; t += dt) r.Step(dt);
    }

    static void Destroy(params GameObject[] list)
    {
        foreach (var g in list) if (g != null) Object.DestroyImmediate(g);
    }

    // ---- cases ---------------------------------------------------------

    static void TablesUnlockAtTheirLevels()
    {
        Check("chill is off at levels 0-1 and on from level 2",
              !ShipSignatures2.ChillsAt(0) && !ShipSignatures2.ChillsAt(1) && ShipSignatures2.ChillsAt(2) && ShipSignatures2.ChillsAt(4));
        Check("the chill is wider and longer at level 4 than at level 2",
              ShipSignatures2.ChillReachAt(4) > ShipSignatures2.ChillReachAt(2) && ShipSignatures2.ChillSecondsAt(4) > ShipSignatures2.ChillSecondsAt(2));
        Check("pierce: none below level 2, one extra at 2, two at 4",
              ShipSignatures2.SentryPierceAt(1) == 0 && ShipSignatures2.SentryPierceAt(2) == 1 && ShipSignatures2.SentryPierceAt(4) == 2);
        Check("sentries drift only at the top level",
              !ShipSignatures2.SentriesDriftAt(3) && ShipSignatures2.SentriesDriftAt(4));
        Check("the eye's blast is the base 1.2 until level 2, then grows to 1.8",
              Mathf.Approximately(ShipSignatures2.EyeBlastAt(0), ShipAttackRunner.EyeBlast) &&
              Mathf.Approximately(ShipSignatures2.EyeBlastAt(1), ShipAttackRunner.EyeBlast) &&
              ShipSignatures2.EyeBlastAt(2) > ShipAttackRunner.EyeBlast && Mathf.Approximately(ShipSignatures2.EyeBlastAt(4), 1.8f));
        Check("the mine blast is the base until level 2; the chain starts at level 4",
              Mathf.Approximately(ShipSignatures2.MineBlastAt(1), ShipAttackRunner.MineBlast) &&
              ShipSignatures2.MineBlastAt(2) > ShipAttackRunner.MineBlast &&
              !ShipSignatures2.MinesChainAt(3) && ShipSignatures2.MinesChainAt(4));
        bool stamped = true;
        for (int lv = 0; lv <= ShipWeaponUpgrades.MaxLevel; lv++)
            stamped &= ShipWeaponUpgrades.LoadoutAt(6, lv).weaponLevel == lv && ShipWeaponUpgrades.LoadoutAt(12, lv).weaponLevel == lv;
        Check("a loadout carries the weapon level it was built at", stamped && ShipLoadoutTable.For(6).weaponLevel == 0);
    }

    static void LabelsMentionTheEffectsAndKeepTheNumbers()
    {
        // the dock row and the codex read the step labels: the unlocking step names the effect
        Check("Jade Phantom: level 2 and 4 name the chill",
              ShipWeaponUpgrades.Step(6, 2).label.Contains("CHILL") && ShipWeaponUpgrades.Step(6, 4).label.Contains("CHILL") &&
              !ShipWeaponUpgrades.Step(6, 1).label.Contains("CHILL"));
        Check("Gold Warden: level 2 names the pierce, level 4 the drift",
              ShipWeaponUpgrades.Step(7, 2).label.Contains("PIERCE") && ShipWeaponUpgrades.Step(7, 4).label.Contains("DRIFT"));
        Check("Paranoid: level 2 and 4 name the blast",
              ShipWeaponUpgrades.Step(10, 2).label.Contains("BLAST") && ShipWeaponUpgrades.Step(10, 4).label.Contains("BLAST"));
        Check("Saboteur: level 2 names the blast, level 4 the chain",
              ShipWeaponUpgrades.Step(12, 2).label.Contains("BLAST") && ShipWeaponUpgrades.Step(12, 4).label.Contains("CHAIN"));
        bool numbers = true;
        foreach (int id in new[] { 6, 7, 10, 12 })
        {
            numbers &= Mathf.Approximately(ShipWeaponUpgrades.Step(id, 2).cooldownCut, .15f) &&
                       Mathf.Approximately(ShipWeaponUpgrades.Step(id, 4).cooldownCut, .20f) &&
                       ShipWeaponUpgrades.Step(id, 2).label.StartsWith("-15% COOLDOWN") &&
                       ShipWeaponUpgrades.Step(id, 4).label.StartsWith("-20% COOLDOWN");
        }
        Check("the cooldown steps keep their numbers (and their label's start)", numbers);
        Check("the other steps are unchanged",
              ShipWeaponUpgrades.Step(6, 1).label == "SHATTER SPLASH" && ShipWeaponUpgrades.Step(7, 3).label == "LONGER SENTRIES" &&
              ShipWeaponUpgrades.Step(10, 3).label == "+1 MARK" && ShipWeaponUpgrades.Step(12, 1).label == "+1 MINE");
    }

    // Jade Phantom: one enemy ahead freezes and shatters; a second one arrives after the ring has passed, so only
    // the shatter's chill can touch it.
    static void JadeChillsOutsideTheSplash()
    {
        foreach (var row in new[] { new { level = 1, dx = 3.0f }, new { level = 2, dx = 3.0f }, new { level = 4, dx = 3.5f } })
        {
            FreshScene();
            var jade = Ship(6, row.level);
            var a = Hazard(0f, -1f);
            jade.Runner.Fire();
            Steps(jade.Runner, 1.04f);   // the ring has covered the screen, a is frozen
            var b = Hazard(0f, -1f + row.dx);   // straight up: past the chain's reach, inside the chill's halo (dx is the distance)
            int guard = 0;
            while (jade.Runner.ShatterCount < 1 && guard++ < 40) jade.Runner.Step(.02f);
            bool chills = ShipSignatures2.ChillsAt(row.level);
            var fs = b.GetComponent<FrozenState>();
            Check("level " + row.level + ": a shatters (" + jade.Runner.ShatterCount + ") and b outside the chain's reach lives", !Alive(a) && Alive(b));
            Check("level " + row.level + ": the chill is " + (chills ? "ON: b stops under an overlay" : "OFF: b is untouched"),
                  chills ? fs != null && fs.IsFrozen && jade.Runner.ChilledNow == 1 && jade.Runner.ChillCount == 1
                         : (fs == null || !fs.IsFrozen) && jade.Runner.ChillCount == 0);
            if (chills)
            {
                float hold = ShipSignatures2.ChillSecondsAt(row.level);
                Steps(jade.Runner, hold - .3f);
                Check("level " + row.level + ": still chilled before " + hold + " s", fs.IsFrozen && Alive(b));
                Steps(jade.Runner, .5f);
                Check("level " + row.level + ": then it thaws by itself, unharmed", !fs.IsFrozen && Alive(b) && jade.Runner.ChilledNow == 0);
            }
            Destroy(b);
            Teardown(jade, 6);
        }
    }

    // Gold Warden: bolts carry on to a second enemy from level 2; the sentries follow the ship at level 4.
    static void GoldBoltsPierceAndSentriesDrift()
    {
        foreach (int level in new[] { 1, 2 })
        {
            FreshScene();
            var gold = Ship(7, level);
            var row = new[] { Hazard(-1.2f, 3.5f), Hazard(0f, 3.5f), Hazard(1.2f, 3.5f) };
            gold.Runner.Fire();
            Steps(gold.Runner, 4f);
            bool pierces = ShipSignatures2.SentryPierceAt(level) > 0;
            Check("level " + level + ": bolts " + (pierces ? "carry on to another enemy (" + gold.Runner.PierceCount + ")" : "do not pierce"),
                  pierces ? gold.Runner.PierceCount > 0 : gold.Runner.PierceCount == 0);
            Check("level " + level + ": the row is cleared either way", !Alive(row[0]) && !Alive(row[1]) && !Alive(row[2]));
            Destroy(row);
            Teardown(gold, 7);
        }

        // a boss-like target is never a pierce destination: it takes at most one hit per bolt fired
        FreshScene();
        var g = Ship(7, ShipWeaponUpgrades.MaxLevel);
        var bossGo = Hazard(0f, 3.5f);
        var fake = bossGo.AddComponent<FakeBoss>();
        var other = Hazard(1f, 3.5f);
        g.Runner.Fire();
        Steps(g.Runner, 3f);
        Check("a boss-like target takes only bolts aimed at it, never a carried-on one (" + fake.hits + " hits, " + g.Runner.PierceCount + " carried)",
              fake.hits <= g.Runner.SentryBolts);
        Destroy(bossGo, other);
        Teardown(g, 7);

        foreach (int level in new[] { 2, 4 })
        {
            FreshScene();
            var gold = Ship(7, level);
            gold.Runner.Fire();
            Steps(gold.Runner, 1.5f);
            float x0 = gold.Runner.FirstSentryX;
            gold.transform.position += Vector3.right * 1.5f;
            Steps(gold.Runner, 1.2f);
            float moved = Mathf.Abs(gold.Runner.FirstSentryX - x0);
            bool drifts = ShipSignatures2.SentriesDriftAt(level);
            Check("level " + level + ": the sentries " + (drifts ? "drift along with the ship (moved " + moved.ToString("0.00") + ")" : "hold their place"),
                  drifts ? moved > 1.2f : moved < 1e-3f);
            Teardown(gold, 7);
        }
    }

    // Gold Warden: scenery nearer than an enemy is never picked as a mark and never takes a bolt. A backdrop rock is a
    // hazard-tagged body that takes hits (flagged Scenery, and an ISceneryTarget even when the flag is reset); a cover
    // boulder is a plain decorative object with no target at all.
    static void GoldSentriesIgnoreScenery()
    {
        foreach (bool flagReset in new[] { false, true })
        {
            FreshScene();
            var gold = Ship(7, 1);
            var rock = Hazard(0f, 2.6f);
            rock.tag = "Astr";
            var rockHits = rock.AddComponent<FakeRock>();
            var rockClear = rock.GetComponent<ClearTarget>();
            rockClear.Scenery = !flagReset;   // the flag reset (a pooled body): the marker still marks it
            var boulder = new GameObject("coverBoulder", typeof(SpriteRenderer));
            boulder.transform.position = new Vector3(0f, 2.9f, 0f);
            var enemy = Hazard(0f, 3.8f);
            var fake = enemy.AddComponent<FakeBoss>();
            var muzzle = new Vector3(0f, 2.2f, 0f);
            Check("scenery" + (flagReset ? " (flag reset)" : "") + ": nearest real mark is the enemy though the rock is closer",
                  ShipTargets.Nearest(muzzle, 0f, null, true) == enemy.GetComponent<ClearTarget>() && ShipTargets.IsThreat(rockClear) == false);
            gold.Runner.Fire();
            Steps(gold.Runner, 4f);
            Check("scenery" + (flagReset ? " (flag reset)" : "") + ": the sentries shot the enemy (" + fake.hits + " hits)", fake.hits > 0);
            Check("scenery" + (flagReset ? " (flag reset)" : "") + ": the backdrop rock took no hit and the boulder is untouched",
                  rockHits.hits == 0 && Alive(rock) && Alive(boulder));
            Destroy(rock, boulder, enemy);
            Teardown(gold, 7);
        }
    }

    // Gold Warden turrets: the head turns the full circle (no arc limit) toward its mark at a capped speed, the muzzle
    // follows the rotation, bolts leave from it, and a turret with nothing to shoot holds fire.
    static void GoldTurretsAimAllAround()
    {
        FreshScene();
        var t = new SentryTurret();
        t.Show(Vector3.zero, SentryTurret.SentryScaleForTests);
        Check("turret art loads: base and head drawn, head sorts above the base",
              t.BaseRenderer.sprite != null && t.HeadRenderer.sprite != null && t.HeadRenderer.sortingOrder > t.BaseRenderer.sortingOrder);
        Check("art is point filtered at 256 px a unit",
              t.HeadRenderer.sprite.texture.filterMode == FilterMode.Point && Mathf.Approximately(t.HeadRenderer.sprite.pixelsPerUnit, 256f));
        var targets = new[] { new Vector3(3f, 3f), new Vector3(-3f, 3f), new Vector3(3f, -3f), new Vector3(-3f, -3f),
                              new Vector3(0f, -4f), new Vector3(0f, 4f), new Vector3(-4f, 0f), new Vector3(4f, 0f) };
        foreach (var at in targets)
        {
            t.Show(Vector3.zero, SentryTurret.SentryScaleForTests);
            t.Unfold(1f);
            float prev = t.Angle;
            bool capped = true;
            for (int i = 0; i < 100; i++)
            {
                t.Aim(at, .02f);
                if (Mathf.Abs(Mathf.DeltaAngle(prev, t.Angle)) > SentryTurret.TurnSpeed * .02f + .01f) capped = false;
                prev = t.Angle;
            }
            Vector3 aimDir = at.normalized, muzzleDir = t.Muzzle.normalized;
            Check("head converges on " + at + " (error " + t.Error(at).ToString("0.0") + ")", t.Error(at) < .5f);
            Check("turn speed is capped for " + at, capped);
            Check("muzzle sits on the aim line for " + at + ", " + (SentryTurret.MuzzleReach * SentryTurret.SentryScaleForTests).ToString("0.000") + " from the pivot",
                  Vector3.Dot(aimDir, muzzleDir) > .999f && Mathf.Abs(t.Muzzle.magnitude - SentryTurret.MuzzleReach * SentryTurret.SentryScaleForTests) < 1e-3f);
            Check("head transform agrees with the muzzle for " + at,
                  Vector3.Distance(t.HeadTransform.TransformPoint(new Vector3(0f, SentryTurret.MuzzleReach, 0f)), t.Muzzle) < 1e-3f);
        }
        Check("pointing straight up the muzzle is straight above the pivot",
              Vector3.Distance(SentryTurret.MuzzleAt(Vector3.zero, 0f, 1f), new Vector3(0f, SentryTurret.MuzzleReach, 0f)) < 1e-5f);
        Check("pointing left the muzzle is to the left", Vector3.Distance(SentryTurret.MuzzleAt(Vector3.zero, 90f, 1f), new Vector3(-SentryTurret.MuzzleReach, 0f, 0f)) < 1e-5f);
        t.Destroy();

        // in play: an enemy below and beside the sentry, held by a fake boss so it survives to be shot at
        FreshScene();
        var gold = Ship(7, 1);
        var enemy = Hazard(0f, -1.5f);
        enemy.AddComponent<FakeBoss>();
        gold.Runner.Fire();
        Steps(gold.Runner, 1.5f);
        var tur = gold.Runner.SentryTurretOf(0);
        Check("a deployed sentry has a turret", tur != null && tur.Visible);
        if (tur != null)
        {
            Check("its head faces the enemy below it (error " + tur.Error(enemy.transform.position).ToString("0.0") + ")", tur.Error(enemy.transform.position) < 1f);
            Check("the sentries fired", gold.Runner.SentryBolts > 0);
            Vector3 from = gold.Runner.LastBoltFrom;
            // the last bolt came from whichever sentry fired last: find the one whose muzzle it left from
            bool fromAMuzzle = false;
            for (int i = 0; i < 3; i++)
            {
                var o = gold.Runner.SentryTurretOf(i);
                if (o == null || !o.Visible) continue;
                Vector3 toE = (enemy.transform.position - o.Pivot).normalized;
                if (Mathf.Abs((from - o.Pivot).magnitude - SentryTurret.MuzzleReach * ShipAttackRunner.SentryTurretSize) < .01f &&
                    Vector3.Angle(from - o.Pivot, toE) < SentryTurret.FireCone + 1f) fromAMuzzle = true;
            }
            Check("bolts leave from a muzzle ring around a pivot, along the aim", fromAMuzzle);
        }
        Destroy(enemy);
        Teardown(gold, 7);

        // nothing to shoot: no bolts, the head only idles about straight up
        FreshScene();
        var idle = Ship(7, 1);
        idle.Runner.Fire();
        Steps(idle.Runner, 2f);
        var it = idle.Runner.SentryTurretOf(0);
        Check("no target: no bolts", idle.Runner.SentryBolts == 0);
        Check("no target: the head idles within its sweep", it != null && Mathf.Abs(Mathf.DeltaAngle(0f, it.Angle)) <= SentryTurret.IdleSweep + .5f);
        Teardown(idle, 7);
    }

    // Gold Warden: enemies can shoot the deployed sentries down. An enemy shot that touches one is spent and costs it a
    // hit; an enemy body touching it costs it a hit and is not hurt; the loadout's hit count destroys it (it folds away
    // early). Nothing lands while the world is frozen; scenery, pickups and the player's own shots never hurt it; with
    // nothing touching them they still fold away on their timer.
    static void GoldSentriesCanBeShotDown()
    {
        int hits = ShipWeaponUpgrades.LoadoutFor(7).hits;
        Check("shot down: the Warden's loadout gives sentries a small hit count (" + hits + ")", hits >= 1 && hits <= 4);

        FreshScene();
        var gold = Ship(7, 1);
        gold.Runner.Fire();
        Steps(gold.Runner, 1.5f);   // pods flown and unfolded
        int deployed = gold.Runner.SentriesNow;
        Check("shot down: the sentries deployed (" + deployed + ")", deployed >= 3);
        var parked = new List<GameObject>();
        if (deployed >= 3)
        {
            Vector3 p0 = gold.Runner.SentryTurretOf(0).Pivot, p1 = gold.Runner.SentryTurretOf(1).Pivot, p2 = gold.Runner.SentryTurretOf(2).Pivot;
            Vector3 p3 = deployed > 3 ? gold.Runner.SentryTurretOf(3).Pivot : p2;

            // scenery, a pickup and nothing else on the last one: it is never hit
            var rock = Hazard(p3.x, p3.y);
            rock.tag = "Astr";
            rock.GetComponent<ClearTarget>().Scenery = true;
            rock.AddComponent<FakeRock>();
            var pickup = new GameObject("pickupOnSentry");
            pickup.tag = "pickUp";
            pickup.transform.position = p3;
            ClearTarget.Ensure(pickup).SetRadius(.3f);
            parked.Add(rock);
            parked.Add(pickup);

            // an enemy shot on sentry 0: one hit, the shot is spent
            var shot = FakeShot.At(p0);
            parked.Add(shot.gameObject);
            Steps(gold.Runner, .04f);
            Check("shot down: an enemy shot hits a sentry (hp " + gold.Runner.SentryHp(0) + " of " + hits + ")", gold.Runner.SentryHp(0) == hits - 1);
            Check("shot down: the enemy shot is spent on it", shot.popped && !shot.ShotCollidable);

            // frozen world (finger lifted): a shot on sentry 2 lands nothing until the world runs again
            var frozenShot = FakeShot.At(p2);
            parked.Add(frozenShot.gameObject);
            int before = gold.Runner.SentryHitsTaken;
            Time.timeScale = 0f;
            gold.Runner.Step(ShipAttackRunner.ScaledDelta());
            Steps(gold.Runner, .2f);
            Check("shot down: no hit while the world is frozen", gold.Runner.SentryHitsTaken == before && !frozenShot.popped && gold.Runner.SentryHp(2) == hits);
            Time.timeScale = 1f;
            Steps(gold.Runner, .04f);
            Check("shot down: the same shot hits once the world runs", frozenShot.popped && gold.Runner.SentryHp(2) == hits - 1);

            // an enemy body parked on sentry 1: it costs hits, is never hurt, and the hit count destroys the sentry
            var body = Hazard(p1.x, p1.y + .1f);
            var bodyHits = body.AddComponent<FakeBoss>();
            parked.Add(body);
            Steps(gold.Runner, .04f);
            Check("shot down: an enemy body touching a sentry hits it (hp " + gold.Runner.SentryHp(1) + ")", gold.Runner.SentryHp(1) == hits - 1);
            int downBefore = gold.Runner.SentriesShotDown;
            Steps(gold.Runner, ShipAttackRunner.SentryHitGuard * hits + .1f);
            Check("shot down: " + hits + " hits destroy it", gold.Runner.SentryDowned(1) || !(gold.Runner.SentryTurretOf(1).Visible));
            Check("shot down: exactly one death explosion for it (" + gold.Runner.SentryBursts + ")", gold.Runner.SentryBursts == 1);
            Check("shot down: counted once (" + (gold.Runner.SentriesShotDown - downBefore) + ")", gold.Runner.SentriesShotDown - downBefore == 1);
            Check("shot down: the body that touched it is not hurt", Alive(body) && body.GetComponent<ClearTarget>().isActiveAndEnabled);
            Steps(gold.Runner, ShipAttackRunner.DeploySeconds);
            Check("shot down: it has folded away", !gold.Runner.SentryTurretOf(1).Visible);
            Check("shot down: the sentries that were not destroyed fight on", gold.Runner.SentriesNow == deployed - 1);
            if (deployed > 3)
                Check("shot down: scenery and a pickup never hit a sentry", gold.Runner.SentryHp(3) == hits && rock.GetComponent<FakeRock>().hits == 0);

            // the player's own shots: an attack projectile in flight across sentry 2 is not a hit
            int beforeOwn = gold.Runner.SentryHitsTaken;
            var own = AttackPool.Projectile();
            if (own != null) own.transform.position = p2;
            Steps(gold.Runner, ShipAttackRunner.SentryHitGuard + .1f);
            Check("shot down: the player's own projectiles never hit a sentry", gold.Runner.SentryHitsTaken == beforeOwn);
        }
        foreach (var g in parked) if (g != null) { var fs = g.GetComponent<FakeShot>(); if (fs != null) HostileShots.Unregister(fs); Object.DestroyImmediate(g); }
        Time.timeScale = 1f;
        Teardown(gold, 7);

        // nothing touching them: every sentry still folds away on its own timer, none shot down
        FreshScene();
        var calm = Ship(7, 1);
        calm.Runner.Fire();
        Steps(calm.Runner, 1.5f);
        int n = calm.Runner.SentriesNow;
        Steps(calm.Runner, 2f);
        Check("timer: untouched sentries stand through their life (" + calm.Runner.SentriesNow + " of " + n + ")", n > 0 && calm.Runner.SentriesNow == n);
        float life = ShipWeaponUpgrades.LoadoutFor(7).duration;
        Steps(calm.Runner, life + 2f * ShipAttackRunner.DeploySeconds);
        Check("timer: lifetime is 2x the original 6 s (" + life + ")", Mathf.Approximately(life, 12f) && Mathf.Approximately(life, ShipLoadoutTable.WardenSentryBaseLifetime * 2f));
        Check("timer: expiring plays no death explosion (" + calm.Runner.SentryBursts + ")", calm.Runner.SentryBursts == 0);
        Check("timer: they fold away on their timer (" + calm.Runner.SentriesNow + " left), none shot down",
              calm.Runner.SentriesNow == 0 && calm.Runner.SentriesShotDown == 0);
        Teardown(calm, 7);
    }

    // A hostile shot sitting still (an enemy bolt as the shot registry sees it).
    sealed class FakeShot : MonoBehaviour, IHostileShot
    {
        public bool popped;
        public static FakeShot At(Vector3 at)
        {
            var go = new GameObject("fakeEnemyShot");
            go.transform.position = at;
            var s = go.AddComponent<FakeShot>();
            HostileShots.Register(s);
            return s;
        }
        public bool ShotCollidable => !popped;
        public Vector2 ShotPosition => transform.position;
        public float ShotRadius => .12f;
        public int ShotOwner => 4242;
        public float ShotAge => 1f;
        public int ShotMass => HostileShots.Light;
        public Color ShotTint => Color.white;
        public void ShotPop(Vector2 at) { popped = true; }
        public bool ShotShovable => false;
        public void ShotShove(Vector2 by) { }
    }

    // Paranoid: a neighbour 1.5 from the mark dies from level 2 on, one 1.8 away only at level 4. The neighbour
    // sits level with the eye so the gaze never marks it itself.
    static void ParanoidBlastGrowsWithTheLevel()
    {
        foreach (var row in new[] { new { level = 1, d = 1.5f, dies = false }, new { level = 2, d = 1.5f, dies = true },
                                    new { level = 2, d = 1.8f, dies = false }, new { level = 4, d = 1.8f, dies = true } })
        {
            FreshScene();
            var c = Ship(10, row.level);
            float eyeY = c.Runner.Nose.y + ShipAttackRunner.EyeAhead;
            var mark = Hazard(0f, eyeY + .3f);
            float dx = Mathf.Sqrt(row.d * row.d - .4f * .4f);
            var near = Hazard(dx, eyeY - .1f);   // behind the eye: never marked
            c.Runner.Fire();
            Steps(c.Runner, 2f);
            Check("level " + row.level + ": the mark detonates", !Alive(mark));
            Check("level " + row.level + ": a neighbour " + row.d + " away " + (row.dies ? "is caught in the bigger blast" : "is out of reach"),
                  Alive(near) != row.dies);
            Destroy(near);
            Teardown(c, 10);
        }
    }

    // Saboteur: a hazard 1.4 from a mine's spot dies from level 2 (blast 1.35 + its radius); from level 4 a
    // blowing mine sets off the next one.
    static void SaboteurBlastGrowsAndChains()
    {
        foreach (int level in new[] { 1, 2 })
        {
            FreshScene();
            var c = Ship(12, level);
            Vector3 nose = c.Runner.Nose;
            Vector3 spot = nose + Quaternion.Euler(0f, 0f, 35f) * Vector3.up * 2.2f;   // the first mine lands here
            var trip = Hazard(spot.x, spot.y);
            var edge = Hazard(spot.x - 1.4f, spot.y);
            c.Runner.Fire();
            Steps(c.Runner, .9f);
            bool bigger = ShipSignatures2.MineBlastAt(level) > ShipAttackRunner.MineBlast;
            Check("level " + level + ": the mine trips", !Alive(trip));
            Check("level " + level + ": a hazard 1.4 away " + (bigger ? "is caught in the bigger blast" : "is out of reach"), Alive(edge) != bigger);
            Destroy(edge);
            Teardown(c, 12);
        }

        foreach (int level in new[] { 3, 4 })
        {
            FreshScene();
            var c = Ship(12, level);
            Vector3 nose = c.Runner.Nose;
            Vector3 spot = nose + Quaternion.Euler(0f, 0f, 35f) * Vector3.up * 2.2f;
            var trip = Hazard(spot.x, spot.y);
            c.Runner.Fire();
            int launched = c.Runner.LiveMines;
            Steps(c.Runner, 1.2f);
            bool chains = ShipSignatures2.MinesChainAt(level);
            Check("level " + level + ": one mine trips and the others " + (chains ? "go with it (" + c.Runner.LiveMines + " of " + launched + " left)" : "stay"),
                  !Alive(trip) && (chains ? c.Runner.LiveMines < launched - 1 : c.Runner.LiveMines == launched - 1));
            Teardown(c, 12);
        }
    }

    sealed class FakeRock : MonoBehaviour, IShipAttackTargetUncapped, ISceneryTarget
    {
        public int hits;
        public void TakeShipAttack(int ship, float w, Vector3 at) { hits++; }
    }

    sealed class FakeBoss : MonoBehaviour, IShipAttackTargetUncapped
    {
        public int hits;
        public float weight;
        public void TakeShipAttack(int ship, float w, Vector3 at) { hits++; weight += w; }
    }
}
