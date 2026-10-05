using System.Reflection;
using UnityEditor.SceneManagement;
using UnityEngine;

// Feature: "when the player rams an enemy without a shield (a non-fatal hit)
// it just disappears -- make it die properly like any other kill."
//
//   * an unshielded, non-fatal ram costs a heart and the rock / enemy blows
//     up through the shared path (TargetExplosion.Spawn), unpaid (a crash
//     is not a kill; a shielded ram still pays);
//   * over a seeded run it splits into pieces at the configured rate;
//   * a rammed rail mine bursts once and its blast catches what's near;
//   * an elite keeps its rule (a heart, knocked away, no blast); a shot's
//     hitbox is absorbed without a hazard blast;
//   * the fatal hit is still DeathCrash's (no blast, no split, no mine
//     blast); the post-hit window rams nothing;
//   * the ram blast allocates nothing beyond the split's one-off cut.
public static class RamKillTest
{
    static int fails;
    const float Dt = 1f / 60f;
    const BindingFlags Inst = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    static readonly MethodInfo Trigger = typeof(collisionDetection).GetMethod("OnTriggerEnter2D", Inst);

    static int FirstShip { get { foreach (int id in ShipId.All) return id; return 0; } }

    static void Check(string what, bool ok)
    {
        Debug.Log((ok ? "[RAM] PASS  " : "[RAM] FAIL  ") + what);
        if (!ok) fails++;
    }

    public static void Run() { TestHarness.Exit(Execute()); }

    public static int Execute()
    {
        fails = 0;
        using var sandbox = new TestHarness.Sandbox();
        EditorSceneManager.OpenScene("Assets/Scenes/gameS1.unity", OpenSceneMode.Single);
        EnemySplit.ForceInEditor = true;
        try
        {
            ExplodesUnpaid();
            SplitRate();
            MineBursts();
            EliteAndShotsUnchanged();
            FatalUnchanged();
            PostHitWindowRamsNothing();
            NoAllocations();
        }
        finally
        {
            EnemySplit.ForceInEditor = false;
            EnemySplit.ChanceOverride = -1f;
            FriendlyFire.ClearPending();
            EliteSystem.Clear();
            EliteSystem.PlayerOverride = null;
            PlayerInvuln.Reset();
            collisionDetection.lifeCounter = 0;
            collisionDetection.atomCheck = false;
            buttonClicks.playerDied = false;
        }
        Debug.Log("[RAM] failures: " + fails);
        return fails;
    }

    // ---- fixtures ------------------------------------------------------------

    static DeathCrashTest.Rig Ship()
    {
        var r = new DeathCrashTest.Rig(FirstShip);
        Fresh();
        return r;
    }

    // A fresh heart count and no post-hit window, so the next contact is a
    // plain non-fatal unshielded hit.
    static void Fresh()
    {
        collisionDetection.lifeCounter = 0;
        collisionDetection.atomCheck = false;
        collisionDetection.cloakTimer = 0f;
        PlayerInvuln.Reset();
        buttonClicks.playerDied = false;
    }

    static GameObject Hazard(EnemyRole role, Vector3 at, int world = 0)
    {
        var go = EnemyFactory.Create(EnemyRoster.One(world, role), at, Quaternion.identity);
        ClearTarget.Ensure(go);
        return go;
    }

    static void Ram(DeathCrashTest.Rig r, GameObject go)
    {
        var col = go.GetComponent<Collider2D>();
        if (col == null) col = go.AddComponent<CircleCollider2D>();
        Trigger.Invoke(r.cd, new object[] { col });
    }

    static void Kill(GameObject go) { if (go != null) Object.DestroyImmediate(go); }

    static void ClearFx()
    {
        for (var go = GameObject.Find("~WeaponFx"); go != null; go = GameObject.Find("~WeaponFx")) Object.DestroyImmediate(go);
        foreach (var name in new[] { "~TestExplosion(Clone)", "RailMineBurst" })
            for (var go = GameObject.Find(name); go != null; go = GameObject.Find(name)) Object.DestroyImmediate(go);
        HazardRuntime.Ensure().ClearAll();
    }

    // ---- 1. it explodes, unpaid ------------------------------------------------

    static void ExplodesUnpaid()
    {
        var r = Ship();
        try
        {
            RunScore.EndRun(RunScore.RunId);
            RunScore.BeginRun(true, true);
            EnemySplit.ChanceOverride = 0f;
            foreach (var role in new[] { EnemyRole.Rock, EnemyRole.Fighter, EnemyRole.Chaser })
            {
                Fresh();
                ClearFx();
                var go = Hazard(role, r.ship.transform.position + Vector3.right * .3f);
                int blasts = RamKill.Blasts, rolls = EnemySplit.Rolls;
                long score = RunScore.Total;
                int dust = score_dust();
                Ram(r, go);
                Check(role + ": an unshielded non-fatal ram costs one heart and starts the post-hit window",
                      collisionDetection.lifeCounter == 1 && PlayerInvuln.Active && !buttonClicks.playerDied);
                Check(role + ": ... and it explodes through the shared path (" + WeaponFx.ActiveFlipbooks + " blast layers, rolled for a split)",
                      RamKill.Blasts == blasts + 1 && WeaponFx.ActiveFlipbooks >= 3 && EnemySplit.Rolls == rolls + 1);
                Check(role + ": ... unpaid, as before (score " + score + " -> " + RunScore.Total + ", dust awards " + (score_dust() - dust) + ")",
                      RunScore.Total == score && score_dust() == dust);
                Kill(go);
            }

            // A shielded ram still pays (the rule the unshielded one is measured against).
            Fresh();
            collisionDetection.atomCheck = true;
            var paid = Hazard(EnemyRole.Rock, r.ship.transform.position + Vector3.right * .3f);
            long before = RunScore.Total;
            Ram(r, paid);
            Check("a shielded ram still pays kill points (" + before + " -> " + RunScore.Total + ") and costs no heart",
                  RunScore.Total > before && collisionDetection.lifeCounter == 0);
            Kill(paid);
            collisionDetection.atomCheck = false;
        }
        finally { EnemySplit.ChanceOverride = -1f; r.Dispose(); ClearFx(); }
    }

    static int score_dust() => score.dustPickups;

    // ---- 2. splits at the configured rate ---------------------------------------

    static void SplitRate()
    {
        var r = Ship();
        try
        {
            var rt = HazardRuntime.Ensure();
            foreach (var role in new[] { EnemyRole.Rock, EnemyRole.Big })
            {
                ClearFx();
                EnemySplit.Seed(20261004u);
                EnemySplit.ResetCounters();
                const int N = 300;
                TargetExplosion.Size size = TargetExplosion.Size.Small;
                int pieces = 0, rammed = 0;
                for (int i = 0; i < N; i++)
                {
                    Fresh();
                    var go = Hazard(role, r.ship.transform.position + Vector3.right * .3f);
                    size = TargetExplosion.SizeFor(go);
                    int before = rt.ActiveFragments, blasts = RamKill.Blasts;
                    Ram(r, go);
                    if (RamKill.Blasts == blasts + 1) rammed++;
                    pieces = Mathf.Max(pieces, rt.ActiveFragments - before);
                    Kill(go);
                    for (int k = 0; k < 90; k++) rt.Step(Dt);
                    if (i % 50 == 49) ClearFx();
                }
                float want = EnemySplit.ChanceFor(size);
                float rate = EnemySplit.Splits / (float)Mathf.Max(1, EnemySplit.Rolls);
                Check(role + " (" + size + "): " + rammed + " of " + N + " rams blew up, " + EnemySplit.Splits + " split = " +
                      (rate * 100f).ToString("F1") + "% (configured " + (want * 100f).ToString("F0") + "%), into up to " + pieces + " pieces",
                      rammed == N && EnemySplit.Rolls == N && Mathf.Abs(rate - want) < .06f && pieces >= 2 && pieces <= 4);
                Check(role + ": ... every piece gone after its flight", rt.ActiveFragments == 0);
            }
        }
        finally { r.Dispose(); ClearFx(); }
    }

    // ---- 3. mines burst -------------------------------------------------------------

    static void MineBursts()
    {
        var r = Ship();
        try
        {
            var rt = HazardRuntime.Ensure();
            for (int w = 0; w < EnemyRoster.WorldKeys.Length; w++)
            {
                Fresh();
                ClearFx();
                FriendlyFire.ClearPending();
                Vector3 at = r.ship.transform.position + Vector3.right * .3f;
                var mine = Hazard(EnemyRole.Mine, at, w);
                var near = Hazard(EnemyRole.Rock, at + new Vector3(.6f, .4f, 0f), w);
                var far = Hazard(EnemyRole.Rock, at + new Vector3(0f, 4f, 0f), w);
                int blasts = FriendlyFire.MineBlasts, kills = FriendlyFire.Kills;
                Ram(r, mine);
                bool burst = GameObject.Find("RailMineBurst") != null;
                Check(EnemyRoster.WorldKeys[w] + " mine rammed unshielded: one burst queued (" + FriendlyFire.PendingBlasts +
                      "), its burst frame shown, its blast spawned",
                      FriendlyFire.PendingBlasts == 1 && burst && WeaponFx.ActiveFlipbooks >= 3 && collisionDetection.lifeCounter == 1);
                Kill(mine);
                for (int i = 0; i < 30; i++) rt.Step(Dt);
                Check(EnemyRoster.WorldKeys[w] + " ... and the blast catches what's near, not what's far (friendly fire, " +
                      (FriendlyFire.MineBlasts - blasts) + " blast)",
                      near == null && far != null && FriendlyFire.MineBlasts - blasts == 1 && FriendlyFire.Kills > kills);
                Kill(near); Kill(far);
            }
        }
        finally { FriendlyFire.ClearPending(); r.Dispose(); ClearFx(); }
    }

    // ---- 4. elites and shots keep their rules ------------------------------------------

    static void EliteAndShotsUnchanged()
    {
        var r = Ship();
        var pilot = new GameObject("~Pilot").transform;
        EliteSystem.PlayerOverride = pilot;
        var pool = new BossProjectilePool(4, 1);
        try
        {
            Fresh();
            ClearFx();
            EliteDef def = null;
            foreach (var d in EliteCatalog.All) { def = d; break; }
            var e = EliteShip.CreateInPlay(def, (Vector2)r.ship.transform.position + Vector2.up * .4f);
            e.AttackCooldown = 99f;
            int hearts = e.Hearts, blasts = RamKill.Blasts, rolls = EnemySplit.Rolls;
            Vector2 was = e.Position;
            Ram(r, e.gameObject);
            Check("an elite rammed unshielded: a heart each (" + hearts + " -> " + (e != null ? e.Hearts : -1) +
                  "), knocked away, no hazard blast or split",
                  e != null && e.Hearts == hearts - 1 && e.LastHitCause == EliteDamage.PlayerContact &&
                  (e.Position - was).sqrMagnitude > .01f && RamKill.Blasts == blasts && EnemySplit.Rolls == rolls &&
                  collisionDetection.lifeCounter == 1);
            EliteSystem.Clear();

            Fresh();
            var shot = pool.Fire(BossCatalog.ForWorld(0), BossShotStyle.Bolt, r.ship.transform.position, Vector2.zero);
            blasts = RamKill.Blasts;
            Ram(r, shot.Hitbox);
            Check("a boss shot that hits costs a heart and is absorbed -- no hazard blast",
                  collisionDetection.lifeCounter == 1 && RamKill.Blasts == blasts && RamKill.NotAHazardBody(shot.Hitbox));
        }
        finally { pool.Dispose(); EliteSystem.Clear(); EliteSystem.PlayerOverride = null; Kill(pilot.gameObject); r.Dispose(); ClearFx(); }
    }

    // ---- 5. the fatal hit is still DeathCrash's -------------------------------------------

    static void FatalUnchanged()
    {
        foreach (var role in new[] { EnemyRole.Big, EnemyRole.Mine })
        {
            var r = Ship();
            try
            {
                ClearFx();
                FriendlyFire.ClearPending();
                EnemySplit.ChanceOverride = 1f;
                collisionDetection.lifeCounter = collisionDetection.MAXLIFE - 1;
                var go = Hazard(role, r.ship.transform.position + Vector3.right * .3f);
                int blasts = RamKill.Blasts, rolls = EnemySplit.Rolls, mines = FriendlyFire.PendingBlasts;
                Ram(r, go);
                Check(role + ": the last heart still starts the death crash -- no ram blast, split or mine blast",
                      DeathCrash.Running && buttonClicks.playerDied && RamKill.Blasts == blasts && EnemySplit.Rolls == rolls &&
                      FriendlyFire.PendingBlasts == mines && GameObject.Find("RailMineBurst") == null);
                Kill(go);
            }
            finally { EnemySplit.ChanceOverride = -1f; r.Dispose(); ClearFx(); }
        }
    }

    // ---- 6. post-hit i-frames ---------------------------------------------------------------

    static void PostHitWindowRamsNothing()
    {
        var r = Ship();
        try
        {
            ClearFx();
            PlayerInvuln.BeginPostHit();
            collisionDetection.lifeCounter = 1;
            int blasts = RamKill.Blasts;
            foreach (var role in new[] { EnemyRole.Rock, EnemyRole.Fighter, EnemyRole.Mine })
            {
                var go = Hazard(role, r.ship.transform.position + Vector3.right * .3f);
                Ram(r, go);
                Kill(go);
            }
            Check("inside the post-hit window nothing is rammed (no heart, no blast, no mine burst)",
                  collisionDetection.lifeCounter == 1 && RamKill.Blasts == blasts && FriendlyFire.PendingBlasts == 0 &&
                  WeaponFx.ActiveFlipbooks == 0);
        }
        finally { r.Dispose(); ClearFx(); }
    }

    // ---- 7. allocations ---------------------------------------------------------------------

    static void NoAllocations()
    {
        ClearFx();
        var rt = HazardRuntime.Ensure();
        var go = Hazard(EnemyRole.Big, new Vector3(0f, 0f, 0f));
        try
        {
            // warm: the pool, the flipbooks, the drawing's one-off cut
            EnemySplit.ChanceOverride = 1f;
            for (int i = 0; i < 3; i++) { RamKill.Blast(go, FirstShip); for (int k = 0; k < 120; k++) rt.Step(Dt); }

            EnemySplit.ChanceOverride = 0f;
            long before = System.GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 10; i++) RamKill.Blast(go, FirstShip);
            long plain = System.GC.GetAllocatedBytesForCurrentThread() - before;

            EnemySplit.ChanceOverride = 1f;
            int splits = EnemySplit.Splits;
            before = System.GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 6; i++) { RamKill.Blast(go, FirstShip); for (int k = 0; k < 120; k++) rt.Step(Dt); }
            long split = System.GC.GetAllocatedBytesForCurrentThread() - before;
            Check("ram blasts allocate nothing (" + plain + " bytes for 10), nor do warm splits (" + split + " bytes for " +
                  (EnemySplit.Splits - splits) + ")", plain == 0 && split == 0 && EnemySplit.Splits - splits >= 3);
        }
        finally { EnemySplit.ChanceOverride = -1f; Kill(go); ClearFx(); }
    }
}
