using UnityEngine;
using static AttackTestKit;

// A hostile shot that reaches the ship through the REAL physics step (no hand-called OnTriggerEnter2D) costs a heart:
// the Bloom Maw's lobbed resin pool (its brain, windup to landing), every elite-shot kind in every world's skin, and every
// boss's projectile (both styles), each twice over the same pool slots, against an unshielded, non-invulnerable ship.
// (Edit-mode tests get no OnTriggerEnter2D messages: the contact is read from the physics step and handed to the ship's own handler.) Physics2D auto sync is off (perf pass): the trigger step has to see moved/enabled colliders itself.
public static class HostileHitsPlayerTest
{
    static int fails;
    static void Check(string what, bool ok)
    {
        Debug.Log((ok ? "[HOSTILEHIT] PASS  " : "[HOSTILEHIT] FAIL  ") + what);
        if (!ok) fails++;
    }

    public static void Run() { TestHarness.Exit(Execute()); }

    static EliteDef Def(float size)
    {
        var d = new EliteDef { key = "test_verdant", world = "verdant", shotSize = size, shotColor = "#FF4FD8", shotCore = "#FFFFFF", lobSeconds = EnemyBehaviours.LobSeconds, poolSeconds = 2.5f, hazardCount = 6, hazardSpeed = 1.5f };
        d.Resolve();
        return d;
    }

    static void Step(int frames)
    {
        for (int i = 0; i < frames; i++)
        {
            EliteSystem.Step(Dt);
            Physics2D.Simulate(Dt);
        }
    }

    // Steps the hostile world and the physics; when the shot's trigger first touches the hull, delivers the trigger message the
    // way Unity would (edit-mode tests get no OnTriggerEnter2D messages, so the contact is read from the physics and the
    // message handed to the ship's own handler, as every other hit test does). Returns whether the physics reported contact.
    static bool Fly(DeathCrashTest.Rig rig, EliteShot shot, int frames)
    {
        var hull = rig.ship.GetComponent<Collider2D>();
        for (int i = 0; i < frames; i++)
        {
            EliteSystem.Step(Dt);
            Physics2D.Simulate(Dt);
            if (shot == null || !shot.Active || shot.Hitbox == null) return false;
            var hb = shot.Hitbox.GetComponent<Collider2D>();
            var slash = shot.Hitbox.GetComponents<Collider2D>();
            foreach (var c in slash)
                if (c.enabled && c.IsTouching(hull)) { rig.Hit(shot.Hitbox); return true; }
        }
        return false;
    }

    // One shot of `kind` flown into the ship; true when the physics step reported contact and the ship lost a heart.
    static bool ShotHurts(DeathCrashTest.Rig rig, EliteDef d, EliteShots.Kind kind, bool lob)
    {
        var ship = rig.ship.transform;
        ship.position = new Vector3(0f, 0f, 0f);
        collisionDetection.lifeCounter = 0;
        PlayerInvuln.Reset();
        var sh = EliteSystem.Shots.Fire(null, d, kind, new Vector2(0f, lob ? 3f : 2f), lob ? Vector2.zero : new Vector2(0f, -4f), false, 0);
        if (sh == null) return false;
        if (lob) { sh.AsRosterShot(null, 0f); sh.Lob(new Vector2(0f, 0f), EnemyBehaviours.LobSeconds); }
        bool touched = Fly(rig, sh, 150);
        bool hurt = touched && collisionDetection.lifeCounter >= 1;
        if (sh.Active) sh.Recycle();
        return hurt;
    }

    // Every shot kind, in every world's skin (procedural and themed), thrown straight and (the glob) lobbed, twice over the same
    // pool slots: a reused shot hurts as the first did.
    static void EveryKindEveryWorld(DeathCrashTest.Rig rig)
    {
        string bad = "";
        int n = 0;
        for (int world = 0; world < ShotSkins.Worlds; world++)
        {
            ShotSkins.Enable(world);
            var d = new EliteDef { key = "test_" + ShotSkins.WorldKeys[world], world = ShotSkins.WorldKeys[world], shotSize = .3f, shotColor = "#FF4FD8", shotCore = "#FFFFFF", lobSeconds = EnemyBehaviours.LobSeconds, poolSeconds = 2.5f, hazardCount = 6, hazardSpeed = 1.5f, hazardSize = .5f, hazardArmour = 1 };
            d.Resolve();
            foreach (EliteShots.Kind kind in System.Enum.GetValues(typeof(EliteShots.Kind)))
                for (int pass = 0; pass < 2; pass++)
                {
                    bool lob = kind == EliteShots.Kind.Glob;
                    n++;
                    if (!ShotHurts(rig, d, kind, lob)) bad += " " + ShotSkins.WorldKeys[world] + "/" + kind + (lob ? "(lob)" : "") + "#" + pass;
                }
            ShotSkins.Enable(world, false);
        }
        Check("every shot kind in every world, twice over the pool, hurts an unshielded ship through the physics step (" + n + " tried; failed:" + (bad == "" ? " none" : bad) + ")", bad == "");
    }

    // Every boss's pooled projectile, both styles, fired twice from the same pool slot.
    static void EveryBossShot(DeathCrashTest.Rig rig)
    {
        string bad = "";
        int n = 0;
        var ship = rig.ship.transform;
        foreach (var boss in BossCatalog.All)
        {
            var pool = new BossProjectilePool(8, 2);
            try
            {
                foreach (BossShotStyle style in System.Enum.GetValues(typeof(BossShotStyle)))
                    for (int pass = 0; pass < 2; pass++)
                    {
                        n++;
                        ship.position = Vector3.zero;
                        collisionDetection.lifeCounter = 0;
                        PlayerInvuln.Reset();
                        var p = pool.Fire(boss, style, new Vector3(0f, 2f, 0f), new Vector2(0f, -4f));
                        bool touched = false;
                        var hull = rig.ship.GetComponent<Collider2D>();
                        for (int f = 0; f < 120 && p != null && !touched; f++)
                        {
                            pool.Step(Dt);
                            Physics2D.Simulate(Dt);
                            if (!p.Active) break;
                            foreach (var c in p.GetComponentsInChildren<Collider2D>())
                                if (c.enabled && c.IsTouching(hull)) { touched = true; rig.Hit(c.gameObject); break; }
                        }
                        if (!(touched && collisionDetection.lifeCounter >= 1)) bad += " " + boss.id + "/" + style + "#" + pass;
                        if (p != null && p.Active) p.Recycle();
                    }
            }
            finally { pool.Dispose(); }
        }
        Check("every boss's projectile (" + BossCatalog.All.Length + " bosses, both styles, twice over the pool) hurts an unshielded ship through the physics step (" + n + " tried; failed:" + (bad == "" ? " none" : bad) + ")", bad == "");
    }

    static void BloomMawBrain(DeathCrashTest.Rig rig)
    {
        var ship = rig.ship.transform;
        collisionDetection.lifeCounter = 0;
        PlayerInvuln.Reset();
        EliteSystem.Clear();
        EliteSystem.PlayerOverride = ship;
        ship.position = new Vector3(0f, -3f, 0f);
        EnemyThreat.Reset();
        EnemyThreat.ForceShooting = true;
        PilotAirspace.Clear();
        SpawnSpace.ClockOverride = 100f;
        var def = EnemyRoster.Find("verdant_big");
        EnemyBrain brain = null;
        for (int k = 0; k < 50 && brain == null; k++)
        {
            var go = EnemyFactory.Create(def, new Vector3(0f, 1.8f, 0f), Quaternion.identity);
            brain = go.GetComponent<EnemyBrain>();
            brain.TargetOverride = ship;
            if (brain.Armed) break;
            Object.DestroyImmediate(go);
            brain = null;
        }
        Check("a Bloom Maw can be made armed", brain != null);
        if (brain == null) return;
        int landed = 0; bool touched = false; float clock = 100f;
        var seen = new System.Collections.Generic.HashSet<EliteShot>();
        string log = "";
        for (int i = 0; i < 60 * 14; i++)
        {
            clock += Dt; SpawnSpace.ClockOverride = clock;
            brain.Step(Dt);
            if (i % 60 == 0 && i < 600) log += " [" + i / 60 + "s " + brain.Stage + "/" + brain.State + " y" + brain.transform.position.y.ToString("F1") + "]";
            EliteSystem.Step(Dt);
            Physics2D.Simulate(Dt);
            var all = EliteSystem.ShotsIfAny != null ? EliteSystem.ShotsIfAny.All : null;
            if (all != null)
                foreach (var sh in all)
                {
                    if (sh == null || !sh.Active) continue;
                    if (seen.Add(sh)) log += " fired@" + (i * Dt).ToString("F1") + " lobTo " + sh.LobTarget + " ship " + (Vector2)ship.position + ";";
                    if (!sh.Airborne && sh.Kind == EliteShots.Kind.Glob && !touched)
                    {
                        landed++;
                        foreach (var c in sh.Hitbox.GetComponents<Collider2D>())
                            if (c.enabled && c.IsTouching(rig.ship.GetComponent<Collider2D>())) { touched = true; rig.Hit(sh.Hitbox); }
                    }
                }
        }
        log += " | state " + brain.State + " stage " + brain.Stage + " windups " + brain.Windups + " pos " + brain.transform.position + " pilot " + brain.IsPilot + " behaviourAttack " + brain.Behaviour.attack;
        Check("Bloom Maw brain: volleys " + brain.ShotsFired + ", landed-frames " + landed + ", touched " + touched + ", hearts " + collisionDetection.lifeCounter + log, touched && collisionDetection.lifeCounter >= 1);
    }

    public static int Execute()
    {
        fails = 0;
        var mode = Physics2D.simulationMode;
        using (new TestHarness.Sandbox())
        {
            DeathCrashTest.Rig rig = null;
            try
            {
                Fresh();
                Physics2D.simulationMode = SimulationMode2D.Script;
                Check("the project ships Physics2D auto sync off (" + Physics2D.autoSyncTransforms + "): these hits are read with the setting that ships", !Physics2D.autoSyncTransforms);
                rig = new DeathCrashTest.Rig(0);
                collisionDetection.MAXLIFE = 5;
                var ship = rig.ship.transform;
                var box = rig.ship.GetComponent<BoxCollider2D>();   // (the rig's hull has no sprite size to fit)
                box.size = new Vector2(.8f, 1f);
                ship.localScale = Vector3.one;
                EliteSystem.PlayerOverride = ship;
                var d = Def(.3f);

                // 1. Bloom Maw: the glob lands on the ship and the pool hurts
                ship.position = new Vector3(0f, 0f, 0f);
                Physics2D.SyncTransforms();
                Physics2D.Simulate(Dt);
                collisionDetection.lifeCounter = 0;
                PlayerInvuln.Reset();
                var s = EliteSystem.Shots.Fire(null, d, EliteShots.Kind.Glob, new Vector2(0f, 3f), Vector2.zero, true, 0);
                s.AsRosterShot(null, 0f);
                s.Lob(new Vector2(ship.position.x, ship.position.y), EnemyBehaviours.LobSeconds);
                bool touched = Fly(rig, s, Mathf.RoundToInt((EnemyBehaviours.LobSeconds + .6f) / Dt));
                Check("a resin pool landed on the ship is touching it in the physics step and costs a heart (touch " + touched + ", hearts lost " + collisionDetection.lifeCounter + ", landed " + !s.Airborne + ")",
                      touched && collisionDetection.lifeCounter >= 1);
                s.Recycle();

                // 2. a plain shot flying into the ship
                collisionDetection.lifeCounter = 0;
                PlayerInvuln.Reset();
                var shot = EliteSystem.Shots.Fire(null, d, EliteShots.Kind.Shard, new Vector2(0f, 3f), new Vector2(0f, -4f));
                bool touched2 = Fly(rig, shot, 120);
                Check("a straight shot into the ship touches it in the physics step and costs a heart (touch " + touched2 + ", " + collisionDetection.lifeCounter + ")", touched2 && collisionDetection.lifeCounter >= 1);
                if (shot != null && shot.Active) shot.Recycle();

                // 3. the pool does not hurt through post-hit invulnerability or a shield (existing rules stay)
                collisionDetection.lifeCounter = 0;
                collisionDetection.atomCheck = true;
                var s3 = EliteSystem.Shots.Fire(null, d, EliteShots.Kind.Glob, new Vector2(0f, 3f), Vector2.zero, true, 0);
                s3.AsRosterShot(null, 0f);
                s3.Lob(new Vector2(ship.position.x, ship.position.y), EnemyBehaviours.LobSeconds);
                bool touched3 = Fly(rig, s3, Mathf.RoundToInt((EnemyBehaviours.LobSeconds + .3f) / Dt));
                Check("under a shield the pool touches but costs no heart (touch " + touched3 + ", " + collisionDetection.lifeCounter + ")", touched3 && collisionDetection.lifeCounter == 0);
                collisionDetection.atomCheck = false;
                BloomMawBrain(rig);
                EveryKindEveryWorld(rig);
                EveryBossShot(rig);
            }
            finally
            {
                Physics2D.simulationMode = mode;
                if (rig != null) rig.Dispose();
                collisionDetection.lifeCounter = 0;
                collisionDetection.atomCheck = false;
                PlayerInvuln.Reset();
                AttackTestKit.Cleanup();
            }
        }
        Debug.Log("[HOSTILEHIT] failures: " + fails);
        return fails;
    }
}
