using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor.SceneManagement;
using UnityEngine;

// Feature: "enemy weapon fires should also have friendly fire on and hurt
// other enemies and objects in the scene too" -- and "ALL weapons are
// friendly-fire enabled".
//
//   * every hostile weapon -- a roster enemy's shot (Shot / Ring / Cross /
//     Lob), an elite's shot of every kind (bolt, shard, slag, shell, glob
//     pool, slung, bounced), a boss shot, a boss laser and any beam through
//     FriendlyFire.HostileBeam -- destroys the rocks, enemies and mines it
//     hits and takes ONE heart off an elite (grace respected);
//   * never: its own shooter, the boss body, pickups, a target inside its
//     spawn-in protection (just come in, off screen, outside the rails);
//   * pays the pilot nothing, never sets off a DEATH COMBO;
//   * at most HostileFireMaxKillsPerFrame kills a frame; a beam hits each
//     target once per pulse;
//   * frozen while paused, off in the tutorial, no per-step allocation;
//   * a source / reflection guard: every class that makes a hostile hitbox
//     or registers a hostile shot goes through the hostile-fire path, every
//     elite attack fires through EliteShot, every roster attack through
//     EnemyVolley -> EliteShot, so a new weapon cannot skip it silently;
//   * a balance probe on the real spawner (kills a minute from hostile fire).
public static class HostileFireTest
{
    static int fails;
    static void Check(string what, bool ok)
    {
        Debug.Log((ok ? "[HOSTILEFIRE] PASS  " : "[HOSTILEFIRE] FAIL  ") + what);
        if (!ok) fails++;
    }

    public static void Run() { TestHarness.Exit(Execute()); }

    const float Dt = 1f / 60f;

    public static int Execute()
    {
        fails = 0;
        using var sandbox = new TestHarness.Sandbox();
        bool evasion = EliteEvasion.Enabled;
        try
        {
            EliteEvasion.Enabled = false;   // elites hold still for the shots
            Tuning();
            RosterShots();
            EliteShotKinds();
            EliteHearts();
            BossShotsAndBody();
            Beams();
            Protection();
            NoCreditNoCombo();
            KillCap();
            PausedAndTutorial();
            NoAllocations();
            EveryWeaponGoesThroughIt();
            if (TestHarness.Slow("hostile fire balance probe")) Balance();
        }
        finally
        {
            EliteEvasion.Enabled = evasion;
            FriendlyFire.HostileFireEnabled = true;
            FriendlyFire.ClearPending();
            DeathCombo.ForceInEditor = false;
            DeathCombo.ForceTrigger = -1f;
            EliteSystem.Clear();
            EliteSystem.PlayerOverride = null;
            BossEncounter.ResetRun();
            BossRails.Reset();
            startMenu.youAreInTutorial = false;
            FriendlyFire.OnSceneLoaded("");
            buttonClicks.playerDied = false;
            moveBackGround.speed = 0f;
            Time.timeScale = 1f;
        }
        Debug.Log("[HOSTILEFIRE] failures: " + fails);
        return fails;
    }

    // ---- fixtures -------------------------------------------------------------

    static void Fresh()
    {
        EliteSystem.Clear();
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        BossEncounter.ResetRun();
        BossRails.Reset();
        FriendlyFire.ClearPending();
        FriendlyFire.ResetCounters();
        FriendlyFire.ResetHostileCounters();
        FriendlyFire.HostileFireEnabled = true;
        FriendlyFire.OnSceneLoaded("gameS1");
        startMenu.youAreInTutorial = false;
        DeathCombo.ForceInEditor = false;
        DeathCombo.ForceTrigger = -1f;
        DeathCombo.ResetCounters();
        RunScore.EndRun(RunScore.RunId);
        PlayerPrefs.SetString("HasDoneTut", "true");
        RunScore.BeginRun(true, true);
        var camGo = new GameObject("Main Camera", typeof(Camera));
        camGo.tag = "MainCamera";
        var cam = camGo.GetComponent<Camera>();
        cam.orthographic = true;
        cam.orthographicSize = 5f;
        camGo.transform.position = new Vector3(0f, 0f, -10f);
        buttonClicks.playerDied = false;
        score.pauseCounter = 0;
        moveBackGround.speed = 0f;   // a still board: shots fly exactly where they are aimed
        Time.timeScale = 1f;
        var pilot = new GameObject("~Pilot").transform;
        pilot.position = new Vector3(0f, -4.5f, 0f);
        EliteSystem.PlayerOverride = pilot;
        Random.InitState(2468);
        HazardRuntime.Ensure().ClearAll();
    }

    static EliteDef Def(string brain)
    {
        foreach (var d in EliteCatalog.All) if (d.brain == brain) return d;
        return null;
    }

    static GameObject Hazard(EnemyRole role, Vector2 at, bool settled = true)
    {
        var go = EnemyFactory.Create(EnemyRoster.One(3, role), at, Quaternion.identity);
        ClearTarget.Ensure(go);
        if (settled) FriendlyFire.Settle(go);
        return go;
    }

    static EliteShip Elite(string brain, Vector2 at)
    {
        var e = EliteShip.CreateInPlay(Def(brain), at);
        e.AttackCooldown = 99f;
        return e;
    }

    static bool Gone(GameObject go) => go == null || !go.TryGetComponent(out ClearTarget t) || !t.enabled;

    static void StepShots(float seconds)
    {
        for (float t = 0f; t < seconds; t += Dt) EliteSystem.Shots.Step(Dt);
    }

    static EliteShot FireElite(EliteShip owner, EliteShots.Kind kind, Vector2 at, Vector2 v) =>
        EliteSystem.Shots.Fire(owner, owner != null ? owner.Def : Def("gunship"), kind, at, v);

    // ---- 0. the tuning block ----------------------------------------------------

    static void Tuning()
    {
        Check("tuning in one place: Enabled " + FriendlyFire.HostileFireEnabled + ", elites lose one heart " +
              FriendlyFire.HostileFireElitesLoseOneHeart + ", awards credit " + FriendlyFire.HostileFireAwardsPlayerCredit +
              ", max kills/frame " + FriendlyFire.HostileFireMaxKillsPerFrame + ", spawn-in protection " +
              FriendlyFire.HostileFireSpawnInProtectionSeconds + " s",
              FriendlyFire.HostileFireEnabled && FriendlyFire.HostileFireElitesLoseOneHeart && !FriendlyFire.HostileFireAwardsPlayerCredit &&
              FriendlyFire.HostileFireMaxKillsPerFrame == 2 && Mathf.Approximately(FriendlyFire.HostileFireSpawnInProtectionSeconds, 1f));
    }

    // ---- 1. roster enemies' shots (every attack that fires) -----------------------

    static void RosterShots()
    {
        foreach (var attack in new[] { EnemyAttack.Shot, EnemyAttack.Ring, EnemyAttack.Cross, EnemyAttack.Lob })
        {
            Fresh();
            EnemyDef def = null;
            foreach (var d in EnemyRoster.All)
            {
                var b = d.Behaviour;
                if (b != null && b.attack == attack) { def = d; break; }
            }
            // Cross was the rail mines' attack; they fire a laser now (RailMineLaser,
            // covered in Lasers below and RailMineLaserTest), so the roster may have none.
            if (def == null && attack == EnemyAttack.Cross) { Check("no roster enemy fires Cross now (the mines fire a laser)", true); continue; }
            if (def == null) { Check("the roster has an enemy that fires " + attack, false); continue; }
            var shooterGo = EnemyFactory.Create(def, new Vector3(def.role == EnemyRole.Mine ? 2.2f : 0f, 1.5f, 0f), Quaternion.identity);
            ClearTarget.Ensure(shooterGo);
            FriendlyFire.Settle(shooterGo);
            var brain = shooterGo.GetComponent<EnemyBrain>();
            if (brain == null) { Check(def.key + " has a brain", false); Object.DestroyImmediate(shooterGo); continue; }
            Vector2 at = shooterGo.transform.position;
            Vector2 lob = at + new Vector2(0f, -2f);
            int n = EnemyVolley.Fire(brain, def.Behaviour, at, Vector2.down, lob);
            EliteShot first = null;
            bool allMarked = n > 0;
            foreach (var s in EliteSystem.Shots.All)
            {
                if (!s.Active) continue;
                allMarked &= s.RosterShot && s.Shooter == shooterGo;
                if (first == null) first = s;
            }
            if (first == null) { Check(def.key + " (" + attack + ") fired", false); Object.DestroyImmediate(shooterGo); continue; }
            Vector2 target = attack == EnemyAttack.Lob ? lob : (Vector2)first.transform.position + first.Velocity.normalized * .9f;
            var rock = Hazard(EnemyRole.Rock, target);
            StepShots(attack == EnemyAttack.Lob ? 2f : 1f);
            Check(def.key + " (" + attack + ", " + n + " shots, roster shots of its own: " + allMarked + ") destroys a rock in its path",
                  allMarked && Gone(rock));
            Check("... and never its own shooter", !Gone(shooterGo));
            if (shooterGo != null) Object.DestroyImmediate(shooterGo);
        }

        // a line of aliens: the one behind shoots the one in front
        Fresh();
        var front = Hazard(EnemyRole.Fighter, new Vector2(0f, 0f));
        var behind = Hazard(EnemyRole.Fighter, new Vector2(0f, 1.6f));
        var s1 = FireElite(null, EliteShots.Kind.Bolt, new Vector2(0f, 1.6f), Vector2.down * 4f);
        s1.AsRosterShot(behind, 0f);
        StepShots(.8f);
        Check("an enemy's shot kills the enemy in front of it, unpaid; the shooter lives (EndReason " + s1.EndReason + ")",
              Gone(front) && !Gone(behind) && s1.EndReason == 4 && RunScore.Total == 0);
    }

    // ---- 2. elite shots of every kind --------------------------------------------

    static void EliteShotKinds()
    {
        foreach (EliteShots.Kind kind in System.Enum.GetValues(typeof(EliteShots.Kind)))
        {
            Fresh();
            var owner = Elite("gunship", new Vector2(-2f, 3.8f));
            var rock = Hazard(EnemyRole.Rock, new Vector2(0f, -1f));
            EliteShot s;
            if (kind == EliteShots.Kind.Glob)
            {
                s = FireElite(owner, kind, new Vector2(0f, 2f), Vector2.zero);
                s.Lob(new Vector2(0f, -1f), .5f);
            }
            else s = FireElite(owner, kind, new Vector2(0f, 1.5f), Vector2.down * 4f);
            StepShots(kind == EliteShots.Kind.Slag ? 3f : 1.5f);
            Check("an elite's " + kind + " destroys a rock it reaches (hostile fire)", Gone(rock));
        }

        // slung round a curve through the well (gravity_sling)
        Fresh();
        var tug = Elite("gunship", new Vector2(-2f, 3.8f));
        var r2 = Hazard(EnemyRole.Rock, new Vector2(0f, -1f));
        var sl = FireElite(tug, EliteShots.Kind.Bolt, new Vector2(-1f, 2f), Vector2.right);
        sl.Sling(new Vector2(-1.8f, .5f), new Vector2(0f, -1f), .6f);
        StepShots(1.2f);
        Check("a slung shot destroys a rock on its curve", Gone(r2));

        // bounced off a rail and back
        Fresh();
        var rime = Elite("gunship", new Vector2(-2f, 3.8f));
        float edge = EliteSystem.RailEdge;
        var bdef = rime.Def;
        int oldBounces = bdef.shotBounces;
        bdef.shotBounces = 2;
        var bnc = FireElite(rime, EliteShots.Kind.Shard, new Vector2(edge - .6f, .2f), new Vector2(3f, -1.5f));
        for (int i = 0; i < 120 && bnc.Bounced == 0 && bnc.Active; i++) EliteSystem.Shots.Step(Dt);
        bdef.shotBounces = oldBounces;
        var r3 = Hazard(EnemyRole.Rock, (Vector2)bnc.transform.position + bnc.Velocity.normalized * .9f);
        StepShots(1f);
        Check("a shard glancing off a rail still hits what it meets after (bounced " + bnc.Bounced + ")", bnc.Bounced >= 1 && Gone(r3));
    }

    // ---- 3. elites: one heart a hit, grace, never their own shots ------------------

    static void EliteHearts()
    {
        Fresh();
        var a = Elite("gunship", new Vector2(-1.5f, 3f));
        var b = Elite("gunship", new Vector2(1f, 0f));
        int hearts = b.Hearts;
        // a's own shot fired from inside its hull: never hurts it
        var own = FireElite(a, EliteShots.Kind.Bolt, a.Position, Vector2.zero);
        StepShots(.6f);
        Check("an elite's own shot never hurts it, even sitting on its hull (hearts " + a.Hearts + ")", a.Hearts == a.Def.hearts && own.Active);
        own.Recycle();

        // a shell (pierces two) straight into b: one heart, never more
        FireElite(a, EliteShots.Kind.Shell, b.Position + Vector2.up * .9f, Vector2.down * 4f);
        // and a bolt right behind it: inside b's grace
        FireElite(a, EliteShots.Kind.Bolt, b.Position + Vector2.up * 1.2f, Vector2.down * 4f);
        StepShots(.45f);
        Check("another elite's shells and bolts take exactly ONE heart in a burst (" + hearts + " -> " + b.Hearts + ")",
              b.Hearts == hearts - 1 && b.LastHitCause == EliteDamage.FriendlyFire && b.Grace > 0f);
        Check("... counted as a hostile-fire elite hit", FriendlyFire.HostileEliteHits >= 1);
        // a roster shot also costs a heart once the grace is over
        for (int i = 0; i < 60; i++) EliteSystem.Step(Dt);
        int h2 = b.Hearts, eliteHits = FriendlyFire.HostileEliteHits, kills = EliteShip.Kills;
        var rs = FireElite(null, EliteShots.Kind.Bolt, b.Position + Vector2.up * .9f, Vector2.down * 4f);
        var src = Hazard(EnemyRole.Fighter, new Vector2(-2f, 4f));
        rs.AsRosterShot(src, 0f);
        StepShots(.4f);
        bool down = b == null || b.State == EliteState.Dead;
        Check("an enemy's shot takes one heart off an elite after its grace (" + h2 + " -> " + (down ? "down" : b.Hearts.ToString()) + ")",
              FriendlyFire.HostileEliteHits == eliteHits + 1 && (down ? h2 == 1 && EliteShip.Kills == kills + 1 : b.Hearts == h2 - 1));
    }

    // ---- 4. boss shots; the body is immune ------------------------------------------

    static void BossShotsAndBody()
    {
        Fresh();
        var pool = new BossProjectilePool(16, 2);
        var boss = BossCatalog.ForWorld(3);
        try
        {
            var fighter = Hazard(EnemyRole.Fighter, new Vector2(0f, 0f));
            var mine = Hazard(EnemyRole.Mine, new Vector2(1.5f, 0f));
            pool.Fire(boss, BossShotStyle.Bolt, new Vector3(0f, 1.5f, 0f), Vector2.down * 4f);
            pool.Fire(boss, BossShotStyle.Bolt, new Vector3(1.5f, 1.5f, 0f), Vector2.down * 4f);
            for (int i = 0; i < 40; i++) pool.Step(Dt);
            Check("boss shots destroy an enemy and detonate a mine (blasts queued " + FriendlyFire.PendingBlasts + ")",
                  Gone(fighter) && Gone(mine) && (FriendlyFire.PendingBlasts >= 1 || FriendlyFire.MineBlasts >= 1));

            var bodyGo = new GameObject("BossBody");
            bodyGo.tag = "Enimey";
            bodyGo.AddComponent<BossTarget>();
            ClearTarget.Ensure(bodyGo).SetRadius(1f);
            FriendlyFire.Settle(bodyGo);
            bodyGo.transform.position = new Vector3(-1.5f, 0f, 0f);
            var shot = pool.Fire(boss, BossShotStyle.Bolt, new Vector3(-1.5f, 1.5f, 0f), Vector2.down * 4f);
            for (int i = 0; i < 40; i++) pool.Step(Dt);
            Check("the boss body is never hurt by hostile fire, its shot flies through", bodyGo != null && !Gone(bodyGo) && shot.EndReason != 4);
            var beam = pool.Beam(boss, null, -1, new Vector3(-1.5f, 3f, 0f), -90f, 0f, 0f, .5f, .4f);
            for (int i = 0; i < 20; i++) pool.Step(Dt);
            Check("... nor by a boss laser", bodyGo != null && !Gone(bodyGo) && beam != null);
        }
        finally { pool.Dispose(); }
    }

    // ---- 5. beams: everything along it, once per pulse --------------------------------

    static void Beams()
    {
        // a synthetic beam (the rail-mine laser and any future beam use HostileBeam)
        Fresh();
        var hits = new FriendlyFire.BeamHits();
        var rocks = new List<GameObject>();
        for (int i = 0; i < 4; i++) rocks.Add(Hazard(EnemyRole.Rock, new Vector2(0f, 2f - i * 1.3f)));
        var e = Elite("gunship", new Vector2(0f, -3.4f));
        int hearts = e.Hearts;
        var shooter = Hazard(EnemyRole.Mine, new Vector2(0f, 3.4f));
        var rt = HazardRuntime.Ensure();
        int landed = 0;
        for (int f = 0; f < 4; f++)
        {
            FriendlyFire.HostileStep();
            landed += FriendlyFire.HostileBeam(hits, new Vector2(0f, 3.4f), Vector2.down, 8f, .15f, shooter, "rail-mine laser");
        }
        int killed = rocks.Count(Gone);
        Check("a beam pierces: every rock along it dies (" + killed + "/4), the elite at its end loses a heart (" + hearts + " -> " + e.Hearts + ")",
              killed == 4 && e.Hearts == hearts - 1);
        Check("... and the beam's own shooter is untouched", !Gone(shooter));
        // the same pulse never hits it again, even with its grace long over
        for (int i = 0; i < 60; i++) EliteSystem.Step(Dt);
        e.transform.position = new Vector3(0f, -3.4f, 0f);   // back in the beam (its push-back moved it)
        for (int f = 0; f < 30; f++) { FriendlyFire.HostileStep(); FriendlyFire.HostileBeam(hits, new Vector2(0f, 3.4f), Vector2.down, 8f, .15f, shooter); }
        Check("... once per pulse: an elite left standing in it loses no more (" + e.Hearts + ")", e.Hearts == hearts - 1);
        hits.NewPulse();
        FriendlyFire.HostileStep();
        FriendlyFire.HostileBeam(hits, new Vector2(0f, 3.4f), Vector2.down, 8f, .15f, shooter);
        Check("... a new pulse hits it again (" + e.Hearts + ")", e.Hearts == hearts - 2 || (e != null && e.State == EliteState.Dead));

        // the real boss laser
        Fresh();
        var pool = new BossProjectilePool(4, 2);
        try
        {
            var boss = BossCatalog.ForWorld(3);
            var line = new List<GameObject>();
            for (int i = 0; i < 3; i++) line.Add(Hazard(EnemyRole.Rock, new Vector2(0f, 1.5f - i * 1.4f)));
            var beam = pool.Beam(boss, null, -1, new Vector3(0f, 3f, 0f), -90f, 0f, 0f, 1f, .3f);
            for (int i = 0; i < 40; i++) pool.Step(Dt);
            Check("a boss laser burns every rock along its length (" + line.Count(Gone) + "/3) and burns on", line.All(Gone) && beam.Live);
        }
        finally { pool.Dispose(); }
    }

    // ---- 6. spawn-in protection, off screen, outside the rails, pickups ---------------

    static void Protection()
    {
        Fresh();
        var rt = HazardRuntime.Ensure();
        var fresh = Hazard(EnemyRole.Rock, new Vector2(0f, 0f), settled: false);
        var s = FireElite(null, EliteShots.Kind.Bolt, new Vector2(0f, 1f), Vector2.down * 3f);
        for (int i = 0; i < 60; i++) { EliteSystem.Shots.Step(Dt); rt.Step(Dt); }
        Check("a rock that has only just come in is spared (shot passes through, " + fresh.GetComponent<ClearTarget>().PlayfieldSeconds.ToString("F2") + " s on the board)",
              !Gone(fresh));
        // after a second inside the playfield it is fair game
        for (int i = 0; i < 40; i++) rt.Step(Dt);
        FireElite(null, EliteShots.Kind.Bolt, new Vector2(0f, 1f), Vector2.down * 3f);
        for (int i = 0; i < 60; i++) { EliteSystem.Shots.Step(Dt); rt.Step(Dt); }
        Check("... and after HostileFireSpawnInProtectionSeconds in the playfield it is hit", Gone(fresh));

        // above the view: never accumulates protection time
        Fresh();
        rt = HazardRuntime.Ensure();
        var above = Hazard(EnemyRole.Rock, new Vector2(0f, 6.2f), settled: false);
        for (int i = 0; i < 120; i++) rt.Step(Dt);
        Check("a rock above the top of the screen stays protected (" + above.GetComponent<ClearTarget>().PlayfieldSeconds + " s)",
              FriendlyFire.SpawnProtected(above.GetComponent<ClearTarget>()));
        // outside the rails
        float edge = BossRails.DrawnInnerEdge;
        var outside = Hazard(EnemyRole.Rock, new Vector2(edge + .3f, 0f), settled: false);
        for (int i = 0; i < 120; i++) rt.Step(Dt);
        Check("a rock outside the rails (x " + (edge + .3f).ToString("F2") + ", inner edge " + edge.ToString("F2") + " from RailInset) stays protected",
              FriendlyFire.SpawnProtected(outside.GetComponent<ClearTarget>()));

        // pickups: never hit
        Fresh();
        var pick = new GameObject("~StarDust");
        pick.tag = "pickUp";
        pick.transform.position = new Vector3(0f, 0f, 0f);
        ClearTarget.Ensure(pick).SetRadius(.3f);
        FriendlyFire.Settle(pick);
        var ps = FireElite(null, EliteShots.Kind.Bolt, new Vector2(0f, 1f), Vector2.down * 3f);
        StepShots(.6f);
        Check("pickups / atoms / star dust are never hit (the shot flies on, EndReason " + ps.EndReason + ")", !Gone(pick) && ps.EndReason != 4);
    }

    // ---- 7. no credit, no DEATH COMBO ------------------------------------------------

    static void NoCreditNoCombo()
    {
        Fresh();
        DeathCombo.ForceInEditor = true;
        DeathCombo.ForceTrigger = 1f;
        long total = RunScore.Total;
        int dust = score.dustPickups;
        int awards = ShipAttackHits.Kills;
        var rocks = new List<GameObject>();
        for (int i = 0; i < 2; i++) rocks.Add(Hazard(EnemyRole.Rock, new Vector2(-1f + i * 2f, 0f)));
        for (int i = 0; i < 2; i++) FireElite(null, EliteShots.Kind.Bolt, new Vector2(-1f + i * 2f, 1f), Vector2.down * 4f);
        StepShots(.6f);
        Check("hostile-fire kills (" + FriendlyFire.HostileKills + ") pay no score, dust or kill (score " + RunScore.Total + ", dust awards " +
              (score.dustPickups - dust) + ")",
              rocks.All(Gone) && FriendlyFire.HostileKills == 2 && RunScore.Total == total && score.dustPickups == dust && ShipAttackHits.Kills == awards);
        Check("... and never set off a DEATH COMBO even at a forced 100% trigger (" + DeathCombo.Triggers + " triggers, " + DeathCombo.TriggerRolls + " rolls)",
              DeathCombo.Triggers == 0 && DeathCombo.TriggerRolls == 0);
        Check("... the reward code reads the damage source (AwardDestroyedTarget, DeathCombo.OnPlayerKill)",
              File.ReadAllText("Assets/Scripts/Ship/collisionDetection.cs").Contains("FriendlyFire.HostileKillInProgress") &&
              File.ReadAllText("Assets/Scripts/Gameplay/Weapons/DeathCombo.cs").Contains("FriendlyFire.HostileKillInProgress"));
        Check("... the source is back to the player after the kill", FriendlyFire.Source == DamageSource.Player);
        DeathCombo.ForceInEditor = false;
        DeathCombo.ForceTrigger = -1f;
    }

    // ---- 8. the frame's kill cap -----------------------------------------------------

    static void KillCap()
    {
        Fresh();
        var rocks = new List<GameObject>();
        var shots = new List<EliteShot>();
        for (int i = 0; i < 6; i++)
        {
            float x = -2f + i * .8f;
            rocks.Add(Hazard(EnemyRole.Rock, new Vector2(x, 0f)));
            shots.Add(FireElite(null, EliteShots.Kind.Bolt, new Vector2(x, 0f), Vector2.zero));   // already on top of it
        }
        EliteSystem.Shots.Step(Dt);
        int first = rocks.Count(Gone);
        int spent = shots.Count(s => !s.Active);
        Check("one frame kills at most " + FriendlyFire.HostileFireMaxKillsPerFrame + " (" + first + "), capped shots are not spent (" + spent + " spent)",
              first == FriendlyFire.HostileFireMaxKillsPerFrame && spent == first && FriendlyFire.HostileCapped >= 4);
        for (int i = 0; i < 3; i++) EliteSystem.Shots.Step(Dt);
        Check("... the rest go over the next frames (" + rocks.Count(Gone) + "/6)", rocks.All(Gone));
    }

    // ---- 9. paused; the tutorial -----------------------------------------------------

    static void PausedAndTutorial()
    {
        Fresh();
        var rt = HazardRuntime.Ensure();
        var rock = Hazard(EnemyRole.Rock, new Vector2(0f, 0f));
        var unsettled = Hazard(EnemyRole.Rock, new Vector2(1.5f, 0f), settled: false);
        var s = FireElite(null, EliteShots.Kind.Bolt, new Vector2(0f, .6f), Vector2.down * 3f);
        Time.timeScale = 0f;
        Vector3 at = s.transform.position;
        for (int i = 0; i < 60; i++) { float dt = TargetExplosion.Delta(); EliteSystem.Shots.Step(dt); rt.Step(dt); }
        Check("paused: the shot does not move or hit, and no protection time passes",
              !Gone(rock) && s.Active && s.transform.position == at && unsettled.GetComponent<ClearTarget>().PlayfieldSeconds == 0f);
        Time.timeScale = 1f;

        startMenu.youAreInTutorial = true;
        StepShots(.6f);
        Check("off in the tutorial: the shot flies through the rock", !Gone(rock));
        startMenu.youAreInTutorial = false;
        FriendlyFire.OnSceneLoaded(score.TutorialScene);
        FireElite(null, EliteShots.Kind.Bolt, new Vector2(0f, .6f), Vector2.down * 3f);
        StepShots(.6f);
        Check("... also by the tutorial scene alone (the scene loader's note)", !Gone(rock));
        FriendlyFire.OnSceneLoaded("gameS1");
        FireElite(null, EliteShots.Kind.Bolt, new Vector2(0f, .6f), Vector2.down * 3f);
        StepShots(.6f);
        Check("... and on again in a game scene", Gone(rock));

        // The tutorial's alien (TutorialEnemy: a real roster alien, brain off)
        // is never hostile fire's target there, yet stays an ordinary target
        // for the ship (a ram, a teleport strike, the weapon).
        startMenu.youAreInTutorial = true;
        FriendlyFire.OnSceneLoaded(score.TutorialScene);
        var alien = TutorialEnemy.Spawn(0f);
        Check("the tutorial alien spawns", alien != null);
        if (alien != null)
        {
            alien.transform.position = new Vector3(0f, -1f, 0f);
            var t = ClearTarget.Ensure(alien.gameObject);
            FriendlyFire.Settle(alien.gameObject);
            var hits = new FriendlyFire.BeamHits();
            int landed = FriendlyFire.HostileBeam(hits, new Vector2(-3f, -1f), Vector2.right, 6f, .3f, null, "test laser");
            Check("tutorial: a hostile beam across the alien lands nothing (" + landed + ") and the alien lives",
                  landed == 0 && !Gone(alien.gameObject) && !FriendlyFire.HostileFireCanHit(t, null));
            Check("... while the alien stays an ordinary target for the ship", FriendlyFire.CanHit(t));
            TutorialEnemy.Clear();
        }
        startMenu.youAreInTutorial = false;
        FriendlyFire.OnSceneLoaded("gameS1");
    }

    // ---- 10. allocation -----------------------------------------------------------------

    static void NoAllocations()
    {
        Fresh();
        var rt = HazardRuntime.Ensure();
        var pool = new BossProjectilePool(16, 2);
        try
        {
            var boss = BossCatalog.ForWorld(1);
            var e = Elite("gunship", new Vector2(0f, -2f));
            for (int i = 0; i < 12; i++) Hazard(EnemyRole.Rock, new Vector2(-2.2f + (i % 6) * .9f, 3.5f + (i / 6) * .6f), settled: false);
            for (int i = 0; i < 16; i++)
            {
                var s = FireElite(null, EliteShots.Kind.Bolt, new Vector2(-2f + (i % 8) * .5f, -4.6f + (i / 8) * .3f), new Vector2(0f, -.05f));
                s.AsRosterShot(e.gameObject, 0f);
            }
            for (int i = 0; i < 6; i++) pool.Fire(boss, BossShotStyle.Bolt, new Vector3(-2f + i * .8f, 1.5f, 0f), new Vector2(0f, .05f));
            var hits = new FriendlyFire.BeamHits();
            for (int i = 0; i < 5; i++)
            {
                EliteSystem.Shots.Step(Dt); pool.Step(Dt); rt.Step(Dt);
                FriendlyFire.HostileBeam(hits, new Vector2(0f, 3.5f), Vector2.down, 8f, .2f, null);
            }
            long before = System.GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 20; i++)
            {
                EliteSystem.Shots.Step(Dt);
                pool.Step(Dt);
                rt.Step(Dt);
                FriendlyFire.HostileBeam(hits, new Vector2(0f, 3.5f), Vector2.down, 8f, .2f, null);
            }
            long used = System.GC.GetAllocatedBytesForCurrentThread() - before;
            Check("steady steps with " + HostileShots.ActiveCount + " shots, a beam and " + ClearTarget.Live.Count +
                  " targets (protected and in grace) -- tracking and hostile-fire scans allocate nothing (" + used + " bytes)", used == 0);
        }
        finally { pool.Dispose(); }
    }

    // ---- 11. no weapon skips it --------------------------------------------------------

    static readonly Regex ClassStart = new Regex(@"(?m)^\s*(?:\[[^\]\n]*\]\s*)*(?:public |internal |private )?(?:sealed |abstract |static |partial )*class\s+(\w+)");

    static Dictionary<string, string> Classes()
    {
        var map = new Dictionary<string, string>();
        foreach (var path in Directory.GetFiles("Assets/Scripts", "*.cs", SearchOption.AllDirectories))
        {
            string src = File.ReadAllText(path);
            var ms = ClassStart.Matches(src);
            for (int i = 0; i < ms.Count; i++)
            {
                int start = ms[i].Index, end = i + 1 < ms.Count ? ms[i + 1].Index : src.Length;
                map[ms[i].Groups[1].Value] = src.Substring(start, end - start);
            }
        }
        return map;
    }

    static bool UsesHostileFire(string body) =>
        body.Contains("FriendlyFire.HostileFireCanHit(") || body.Contains("FriendlyFire.HostileBeam(");

    static void EveryWeaponGoesThroughIt()
    {
        var classes = Classes();
        // the bodies themselves (an elite's hull, the boss's body) are targets, not weapons
        var bodies = new HashSet<string> { "EliteShip", "BossActor", "BossHitbox" };
        var weapons = new List<string>();
        foreach (var kv in classes)
        {
            string b = kv.Value;
            bool weapon = b.Contains("HostileShots.Register(") || b.Contains("BossHitbox.Circle(") || b.Contains("BossHitbox.Box(") ||
                          Regex.IsMatch(b, @"\.tag\s*=\s*(""Enimey""|BossHitbox\.Tag)");
            if (weapon && !bodies.Contains(kv.Key)) weapons.Add(kv.Key);
        }
        var skipping = weapons.Where(w => !UsesHostileFire(classes[w])).ToList();
        Check("every class that makes a hostile hitbox or registers a hostile shot goes through hostile fire (" + string.Join(", ", weapons) +
              (skipping.Count > 0 ? "; SKIPPING: " + string.Join(", ", skipping) : "") + ")",
              skipping.Count == 0 && weapons.Contains("EliteShot") && weapons.Contains("BossProjectile") && weapons.Contains("BossBeam"));

        // reflection: every IHostileShot type
        var asm = typeof(FriendlyFire).Assembly;
        var shotTypes = asm.GetTypes().Where(t => typeof(IHostileShot).IsAssignableFrom(t) && !t.IsInterface).Select(t => t.Name).ToList();
        var shotSkips = shotTypes.Where(n => !classes.ContainsKey(n) || !UsesHostileFire(classes[n])).ToList();
        Check("every IHostileShot type (" + string.Join(", ", shotTypes) + ") runs the hostile-fire hit test",
              shotTypes.Count >= 2 && shotSkips.Count == 0);

        // every elite attack fires through EliteAttack.Fire -> EliteShot (no shots of its own)
        var attackTypes = asm.GetTypes().Where(t => typeof(EliteAttack).IsAssignableFrom(t) && !t.IsAbstract).ToList();
        var made = new HashSet<string>(EliteAttacks.Ids.Select(id => EliteAttacks.Create(id).GetType().Name));
        var rogue = attackTypes.Where(t => classes.ContainsKey(t.Name) &&
                                           (classes[t.Name].Contains("Shots.Fire(") || classes[t.Name].Contains("Instantiate(") ||
                                            classes[t.Name].Contains("BossHitbox") || classes[t.Name].Contains("FriendlyKill("))).Select(t => t.Name).ToList();
        Check("every elite attack (" + attackTypes.Count + ": " + string.Join(", ", made) + ") fires only through EliteAttack.Fire -> EliteShot" +
              (rogue.Count > 0 ? " -- ROGUE: " + string.Join(", ", rogue) : ""),
              rogue.Count == 0 && attackTypes.All(t => made.Contains(t.Name)) && classes["EliteAttack"].Contains("EliteSystem.Shots.Fire("));

        // every roster attack that fires goes through EnemyVolley -> EliteShot, marked with its shooter
        string volley = classes.ContainsKey("EnemyVolley") ? classes["EnemyVolley"] : "";
        // (Laser is a rail mine's beam, RailMineLaser, not a volley of shots: checked just below)
        var firing = System.Enum.GetValues(typeof(EnemyAttack)).Cast<EnemyAttack>()
            .Where(a => a != EnemyAttack.None && a != EnemyAttack.Lunge && a != EnemyAttack.Laser && a != EnemyAttack.Blast && a != EnemyAttack.Strike).ToList();
        bool cased = firing.All(a => volley.Contains("case EnemyAttack." + a + ":"));
        int fires = Regex.Matches(volley, @"pool\.Fire\(").Count, marks = Regex.Matches(volley, @"AsRosterShot\(source").Count;
        Check("every firing roster attack (" + string.Join(", ", firing) + ") goes through EnemyVolley's pool, each shot marked with its shooter (" +
              fires + " launches, " + marks + " marked)", cased && fires > 0 && fires == marks);

        // (Blast and Strike are the themed area hazards, AttackBlast / AttackStrike on the AttackHazard base: they burn through hostile fire the way a laser does)
        string hazard = classes.ContainsKey("AttackHazard") ? classes["AttackHazard"] : "";
        Check("the themed area hazards (EnemyAttack.Blast, Strike: AttackHazard) burn through hostile fire (FriendlyFire.HostileFireCanHit + HostileHit) and never hurt their shooter",
              hazard.Contains("FriendlyFire.HostileFireCanHit(") && hazard.Contains("FriendlyFire.HostileHit(") && hazard.Contains("shooter"));

        string laser = classes.ContainsKey("RailMineLaser") ? classes["RailMineLaser"] : "";
        Check("the rail mine laser (EnemyAttack.Laser) burns through hostile fire (FriendlyFire.HostileFireCanHit + HostileHit)",
              laser.Contains("FriendlyFire.HostileFireCanHit(") && laser.Contains("FriendlyFire.HostileHit("));

        // mine blasts hook every burst
        Check("every rail-mine burst blasts its neighbours (RailBombAnimator -> FriendlyFire.MineBlast)",
              File.ReadAllText("Assets/Scripts/Gameplay/RailBombAnimator.cs").Contains("FriendlyFire.MineBlast(mine)"));
    }

    // ---- 12. balance on the real spawner ----------------------------------------------

    static void Balance()
    {
        try
        {
            EnemyThreat.ForceShooting = true;
            EditorSceneLoader.Open("gameS1", OpenSceneMode.Single);
            FriendlyFire.OnSceneLoaded("gameS1");
            foreach (int hud in new[] { 10, 20, 30, 40 })
            {
                float onKpm, offShots, onShots, spawnsPm;
                int peak;
                Sim(hud, true, out onKpm, out onShots, out spawnsPm, out peak);
                Sim(hud, false, out _, out offShots, out _, out _);
                Debug.Log(string.Format("[HOSTILEFIRE] balance hud {0}: hostile-fire kills {1:F1}/min of {2:F1} spawns/min ({3:P0}), shots fired/min on {4:F0} off {5:F0}, peak kills in a frame {6}",
                                        hud, onKpm, spawnsPm, spawnsPm > 0f ? onKpm / spawnsPm : 0f, onShots, offShots, peak));
                Check("hud " + hud + ": hostile fire thins the board, never empties it (" + onKpm.ToString("F1") + " kills/min, " +
                      (spawnsPm > 0f ? onKpm / spawnsPm : 0f).ToString("P0") + " of spawns), at most " + FriendlyFire.HostileFireMaxKillsPerFrame + " a frame (" + peak + ")",
                      spawnsPm > 0f && onKpm / spawnsPm < .25f && peak <= FriendlyFire.HostileFireMaxKillsPerFrame);
            }
        }
        finally
        {
            EnemyThreat.ForceShooting = false;
            SpawnSpace.ClockOverride = null;
            EnemyDensityProbe.Clear();
            moveBackGround.speed = 0f;
        }
    }

    const float SimWarmup = 8f, SimWindow = 60f;

    static void Sim(int hud, bool on, out float killsPerMinute, out float shotsPerMinute, out float spawnsPerMinute, out int peak)
    {
        FriendlyFire.HostileFireEnabled = on;
        EnemyDensityProbe.Clear();
        EnemyDensityProbe.chasers.Clear();
        FriendlyFire.ClearPending();
        FriendlyFire.ResetHostileCounters();
        buttonClicks.playerDied = false;
        score.pauseCounter = 0;
        LoopDifficulty.Reset();
        Random.InitState(5100 + hud);
        float v = hud * .3f;
        moveBackGround.speed = hud / 100f;
        var board = EnemyDensityProbe.NewBoard();
        var ship = new GameObject("~FFShip").transform;
        foreach (string timer in EnemyDensityProbe.Timers)
            typeof(enmiesOnBoard).GetField(timer, EnemyDensityProbe.Inst).SetValue(board, Random.Range(0f, 1.5f));
        float level = EnemyDensityProbe.LevelSecondFor(hud);
        var view = ShipTargets.View();
        int spawnsBefore = 0, killsBefore = 0, firedBefore = 0;
        peak = 0;
        float clock = 0f;
        for (float t = 0f; t < SimWarmup + SimWindow; t += Dt)
        {
            clock += Dt;
            EnemyDensityProbe.Elapsed.SetValue(board, level);
            if (t < SimWarmup) { spawnsBefore = board.SpawnedCount; killsBefore = FriendlyFire.HostileKills; firedBefore = EnemyVolley.Fired; }
            FriendlyFire.HostileStep();
            FriendlyFire.TrackPlayfield(Dt, view);
            int k0 = FriendlyFire.HostileKills;
            EnemyDensityProbe.StepBoard(board, ship, clock, v);
            FriendlyFire.StepBlasts(Dt);
            peak = Mathf.Max(peak, FriendlyFire.HostileKills - k0);
        }
        float minutes = SimWindow / 60f;
        killsPerMinute = (FriendlyFire.HostileKills - killsBefore) / minutes;
        shotsPerMinute = (EnemyVolley.Fired - firedBefore) / minutes;
        spawnsPerMinute = (board.SpawnedCount - spawnsBefore) / minutes;
        Object.DestroyImmediate(ship.gameObject);
        EnemyDensityProbe.Clear();
        FriendlyFire.HostileFireEnabled = true;
    }
}
