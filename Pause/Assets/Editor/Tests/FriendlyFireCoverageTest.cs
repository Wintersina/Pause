using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor.SceneManagement;
using UnityEngine;
using static AttackTestKit;

// FULL FRIENDLY FIRE: every enemy-owned damage source hurts every enemy-like target, including a
// target that arrives AFTER the hazard is already burning (the persisting-hazard bug class).
//
//   * LATER ARRIVALS: for each hazard family the hazard is lit FIRST and the victim is created
//     afterwards (settled, or "entering": just come in, past only the short spawn-in grace):
//     rail-mine laser, every AttackHazard (blast, strike, jet, wave, lash, log), boss laser, boss
//     shot, roster / elite shot, mine blast, free-mover and elite body crashes;
//   * targets: rock, fighter, a free-standing mine, a rail mine on its rail (opposite rail too),
//     an elite (one heart), never a pickup; the boss takes only a sliver, only from ordinary
//     enemies' fire (never from its own);
//   * the "FRIENDLY FIRE" word shows for every source and is rate limited;
//   * a REGISTRY / SOURCE GUARD: every component that makes a damaging hitbox or registers a hostile
//     shot is listed in FriendlyFireSources and runs the shared hostile-fire path, so a future attack
//     cannot skip it silently.
public static class FriendlyFireCoverageTest
{
    static int fails;
    static void Check(string what, bool ok)
    {
        Debug.Log((ok ? "[FFCOV] PASS  " : "[FFCOV] FAIL  ") + what);
        if (!ok) fails++;
    }

    public static void Run() { TestHarness.Exit(Execute()); }

    static Transform ship;
    static float clock;

    public static int Execute()
    {
        fails = 0;
        using var sandbox = new TestHarness.Sandbox();
        bool evasion = EliteEvasion.Enabled;
        try
        {
            EliteEvasion.Enabled = false;
            RailMineLaserLater();
            RailMineVsRailMine();
            AttackHazardsLater();
            BossLaserLater();
            ShotsLater();
            MineBlastReaches();
            CrashesReach();
            PickupsSpared();
            BossGetsASliver();
            PopupEverywhere();
            RegistryGuard();
        }
        finally
        {
            EliteEvasion.Enabled = evasion;
            RailMineLaser.AngleOverride = null;
            RailMineLaser.HurtsOtherEnemies = true;
            AttackHazard.HurtsOtherEnemies = true;
            FriendlyFire.HostileFireEnabled = true;
            FriendlyFire.ClearPending();
            EnemyThreat.ForceShooting = false;
            SpawnSpace.ClockOverride = null;
            Cleanup();
            BossEncounter.ResetRun();
            BossRails.Reset();
            EliteSystem.PlayerOverride = null;
            FriendlyFire.OnSceneLoaded("");
            startMenu.youAreInTutorial = false;
        }
        Debug.Log("[FFCOV] failures: " + fails);
        return fails;
    }

    // ---- fixtures ----------------------------------------------------------------------

    static void FreshBoard()
    {
        Fresh();
        HazardRuntime.Ensure().ClearAll();
        EnemyThreat.ForceShooting = false;
        EliteEvasion.Enabled = false;
    }

    // one running frame of the hostile world + the playfield tracker
    static void Frame()
    {
        EliteSystem.Step(Dt);
        HazardRuntime.Ensure().Step(Dt);
    }

    static void Frames(float seconds) { for (float t = 0f; t < seconds; t += Dt) Frame(); }

    static GameObject Enemy(EnemyRole role, Vector2 at, bool settled)
    {
        var def = EnemyRoster.One(3, role);
        for (int w = 0; def == null && w < 6; w++) def = EnemyRoster.One(w, role);
        var go = EnemyFactory.Create(def, at, Quaternion.identity);
        ClearTarget.Ensure(go);
        var b = go.GetComponent<EnemyBrain>();
        if (b != null) b.enabled = false;   // held where it is put
        if (settled) FriendlyFire.Settle(go);
        return go;
    }

    static bool Gone(GameObject go) => go == null || !go.TryGetComponent(out ClearTarget t) || !t.enabled;

    static EliteDef Def(string brain)
    {
        return BaseEliteDefs.For(brain);
    }

    // A point the hazard's live footprint covers, inside the playfield (null: none).
    static Vector2? Covered(AttackHazard h)
    {
        for (float y = 4.2f; y >= -4.2f; y -= .1f)
            for (float x = -2.2f; x <= 2.2f; x += .1f)
                if (h.Footprint.Touches(new Vector2(x, y), .02f)) return new Vector2(x, y);
        return null;
    }

    // ---- 1. the rail-mine laser (a 0.4 s beam) ------------------------------------------

    static EnemyBrain Mine(int world, bool right, float y, out GameObject rail)
    {
        var def = EnemyRoster.One(world, EnemyRole.Mine);
        float x = enmiesOnBoard.WorldRailX(!right);
        rail = new GameObject("RailMineLane");
        rail.transform.position = new Vector3(x, y, 0f);
        rail.AddComponent<RailLaneScroller>();
        var go = EnemyFactory.Create(def, new Vector3(x, y, 0f), Quaternion.identity);
        var brain = go.GetComponent<EnemyBrain>();
        var mount = go.AddComponent<RailMineMount>();
        mount.MountTo(rail.transform);
        mount.brain = brain;
        brain.TargetOverride = ship;
        return brain;
    }

    static void MineStep(EnemyBrain brain)
    {
        clock += Dt;
        SpawnSpace.ClockOverride = clock;
        brain.Step(Dt);
        var mount = brain.GetComponent<RailMineMount>();
        if (mount != null) TestHarness.Send(mount, "LateUpdate");
        var fb = brain.GetComponent<EnemyFlipbook>();
        if (fb != null) fb.Advance(Dt);
        var l = brain.Laser;
        if (l != null) l.Place();
        HazardRuntime.Ensure().Step(Dt);
    }

    static RailMineLaser LiveLaser(EnemyBrain brain)
    {
        for (int i = 0; i < 600; i++)
        {
            MineStep(brain);
            var l = brain.Laser;
            if (l != null && l.State == RailMineLaser.Phase.Beam) return l;
        }
        return null;
    }

    static void LaserBoard()
    {
        FreshBoard();
        clock = 100f;
        SpawnSpace.ClockOverride = clock;
        RailMineLaser.AngleOverride = 0f;
        EnemyThreat.ForceShooting = true;
        ship = new GameObject("~LaserShip").transform;
        ship.position = new Vector3(0f, 1f, 0f);
        EliteSystem.PlayerOverride = ship;
    }

    static void RailMineLaserLater()
    {
        // the beam is lit FIRST; every victim is created after it burns
        LaserBoard();
        var brain = Mine(3, true, 1f, out var rail);
        var l = LiveLaser(brain);
        Check("a rail mine fires and its beam burns", l != null);
        if (l == null) return;
        float row = l.Y;
        var settledRock = Enemy(EnemyRole.Rock, new Vector2(-1.2f, row), true);
        MineStep(brain);
        Check("a settled rock that appears in the burning beam dies", Gone(settledRock));
        var freeMine = Enemy(EnemyRole.Mine, new Vector2(.6f, row + .02f), true);
        MineStep(brain);
        Check("a free-standing mine of the SAME type as the shooter, appearing in the burning beam, dies (only the shooter is spared)", Gone(freeMine));
        Check("... and the shooting mine itself is never hit", brain != null && !Gone(brain.gameObject));

        // an enemy that just flew in: past only the short spawn-in grace it is hit, well within the beam's life
        var fresh = Enemy(EnemyRole.Fighter, new Vector2(-2f, row), false);
        float inBeam = 0f;
        for (int i = 0; i < 60 && l.State == RailMineLaser.Phase.Beam && !Gone(fresh); i++) { MineStep(brain); inBeam += Dt; }
        Check("an enemy that has only just flown into the beam is hit before the beam ends (" + inBeam.ToString("F2") + " s of " + RailMineLaser.BeamSeconds + " s; grace " +
              FriendlyFire.HostileFireSpawnInProtectionSeconds + " s)", Gone(fresh));

        Object.DestroyImmediate(rail);
        Object.DestroyImmediate(brain.gameObject);

        // a second pulse (next shot of the ride) on a second mine hits an elite that arrived between the pulses
        LaserBoard();
        var b2 = Mine(3, false, 1f, out var rail2);
        var elite = EliteShip.CreateInPlay(Def("gunship"), new Vector2(0f, -2f));
        elite.AttackCooldown = 99f;
        var l2 = LiveLaser(b2);
        elite.transform.position = new Vector3(0f, l2 != null ? l2.Y : 1f, 0f);
        int h0 = elite.Hearts;
        for (int i = 0; i < 12; i++) MineStep(b2);
        Check("an elite that moves into a burning beam loses a heart (" + h0 + " -> " + elite.Hearts + ")", elite.Hearts == h0 - 1 || elite.State == EliteState.Dead);
        Object.DestroyImmediate(rail2);
    }

    static void RailMineVsRailMine()
    {
        LaserBoard();
        var a = Mine(3, true, 1f, out var railA);
        var b = Mine(3, false, 1f, out var railB);
        b.enabled = false;
        var bGo = b.gameObject;
        // a rail mine on its rail counts as inside the playfield: the spawn-in grace runs out
        for (int i = 0; i < 60; i++) HazardRuntime.Ensure().Step(Dt);
        var bt = bGo.GetComponent<ClearTarget>();
        float edge = BossRails.DrawnInnerEdge;
        Check("a rail mine riding its rail (x " + bGo.transform.position.x.ToString("F2") + ", inner edge " + edge.ToString("F2") + ") gets out of spawn-in grace (" +
              bt.PlayfieldSeconds.ToString("F2") + " s)", !FriendlyFire.SpawnProtected(bt));
        var l = LiveLaser(a);
        for (int i = 0; i < 8; i++) MineStep(a);
        Check("a rail mine's laser hits the rail mine on the OPPOSITE rail (laser " + (l != null ? "y " + l.Y.ToString("F2") : "none") + ", target y " +
              (bGo != null ? bGo.transform.position.y.ToString("F2") : "-") + ")", l != null && Gone(bGo));
        Check("... and never the mine that fired it", a != null && !Gone(a.gameObject));
        Object.DestroyImmediate(railA);
        Object.DestroyImmediate(railB);
    }

    // ---- 2. the themed area hazards, lit first, victim after ------------------------------

    delegate AttackHazard Maker();

    static void AttackHazardsLater()
    {
        var made = new List<(string name, Maker make)>
        {
            ("blast ring", () => AttackBlast.Arm(BlastSpec.Standard(1), new Vector2(0f, 3f), new Vector2(0f, -3f), 1f, null)),
            ("strike column", () => AttackStrike.Arm(StrikeSpec.Standard(1), 0f, -1.5f, 1f, null)),
            ("flame jet", () => AttackJet.Arm(JetSpec.Pressure(4), new Vector2(0f, 2.4f), new Vector2(0f, -1f), 1f, null)),
            ("surf wave", () => AttackWave.Arm(WaveSpec.Surf(4), new Vector2(0f, 3f), new Vector2(0f, -3f), 1f, null)),
            ("vine lash", () => AttackLash.Arm(LashSpec.Standard(2), new Vector2(0f, 3f), new Vector2(0f, -2.5f), 1f, null)),
            ("rolling log", () => AttackLog.Arm(LogSpec.Standard(2), new Vector2(-2f, 3f), new Vector2(.5f, 1f), 1f, 1.2f, null)),
        };
        foreach (var (name, make) in made)
        {
            FreshBoard();
            var h = make();
            h.Ignite();
            Frames(.15f);
            // a stream of settled rocks, each created AFTER the hazard went live, one at a time where it burns now
            bool killed = false;
            int made1 = 0;
            for (int f = 0; f < 90 && h.Live && !killed; f++)
            {
                var p = Covered(h);
                if (p.HasValue)
                {
                    var rock = Enemy(EnemyRole.Rock, p.Value, true);
                    made1++;
                    Frame();
                    if (Gone(rock)) killed = true;
                }
                else Frame();
            }
            Check("a " + name + " (lit first) kills a rock that appears in its live footprint later (" + made1 + " rocks tried)", killed);
            if (h != null) h.Cancel();
        }
    }

    // ---- 3. boss laser / boss shot ---------------------------------------------------------

    static void BossLaserLater()
    {
        FreshBoard();
        var pool = new BossProjectilePool(8, 2);
        try
        {
            var boss = BossCatalog.ForWorld(3);
            var beam = pool.Beam(boss, null, -1, new Vector3(0f, 3f, 0f), -90f, 0f, 0f, 1f, .6f);
            for (int i = 0; i < 40; i++) { pool.Step(Dt); HazardRuntime.Ensure().Step(Dt); }
            Check("the boss laser burns", beam != null && beam.Live);
            var settled = Enemy(EnemyRole.Rock, new Vector2(0f, 0f), true);
            var entering = Enemy(EnemyRole.Fighter, new Vector2(.05f, -1.5f), false);
            var mine = Enemy(EnemyRole.Mine, new Vector2(0f, -2.5f), true);
            for (int i = 0; i < 60; i++) { pool.Step(Dt); HazardRuntime.Ensure().Step(Dt); }
            Check("boss laser (already burning) kills a settled rock, a just-entered fighter and a mine that appear later (" +
                  (Gone(settled) ? "rock " : "") + (Gone(entering) ? "fighter " : "") + (Gone(mine) ? "mine" : "") + ")", Gone(settled) && Gone(entering) && Gone(mine));

            // a boss shot in flight meets an enemy created after it was fired
            var shot = pool.Fire(boss, BossShotStyle.Bolt, new Vector3(1.5f, 3f, 0f), Vector2.down * 3f);
            for (int i = 0; i < 20; i++) { pool.Step(Dt); HazardRuntime.Ensure().Step(Dt); }
            var late = Enemy(EnemyRole.Fighter, new Vector2(1.5f, -.2f), true);
            for (int i = 0; i < 60; i++) { pool.Step(Dt); HazardRuntime.Ensure().Step(Dt); }
            Check("a boss shot already in flight kills an enemy created in its path afterwards", Gone(late));
        }
        finally { pool.Dispose(); }
    }

    // ---- 4. roster / elite shots, shooter exclusion ------------------------------------------

    static void ShotsLater()
    {
        FreshBoard();
        var owner = Enemy(EnemyRole.Rock, new Vector2(1.5f, 3.2f), true);   // the shot flies straight through its own shooter
        var s = EliteSystem.Shots.Fire(null, Def("gunship"), EliteShots.Kind.Bolt, new Vector2(1.5f, 3.5f), Vector2.down * 3f);
        s.AsRosterShot(owner, 0f);
        for (int i = 0; i < 15; i++) { EliteSystem.Shots.Step(Dt); HazardRuntime.Ensure().Step(Dt); }
        var later = Enemy(EnemyRole.Fighter, new Vector2(1.5f, 0f), true);
        for (int i = 0; i < 90; i++) { EliteSystem.Shots.Step(Dt); HazardRuntime.Ensure().Step(Dt); }
        Check("a shot in flight kills an enemy that appears in its path afterwards", Gone(later));
        Check("a shot flew through its own shooter without hurting it (owner exclusion is by identity only)", !Gone(owner));

        // a shot fired through a same-type peer of its shooter: the peer dies, the shooter lives
        FreshBoard();
        var o2 = Enemy(EnemyRole.Fighter, new Vector2(0f, 4f), true);
        var p2 = Enemy(EnemyRole.Fighter, new Vector2(0f, 1f), true);
        var s2 = EliteSystem.Shots.Fire(null, Def("gunship"), EliteShots.Kind.Bolt, new Vector2(0f, 3f), Vector2.down * 4f);
        s2.AsRosterShot(o2, 0f);
        for (int i = 0; i < 60; i++) { EliteSystem.Shots.Step(Dt); HazardRuntime.Ensure().Step(Dt); }
        Check("a fighter's shot kills a fighter beside it (same type) and spares only the shooter", Gone(p2) && !Gone(o2));

        // an elite's own shot hurts another elite that comes into its path (one heart)
        FreshBoard();
        var shooterElite = EliteShip.CreateInPlay(Def("gunship"), new Vector2(-2f, 3f));
        shooterElite.AttackCooldown = 99f;
        var shot = EliteSystem.Shots.Fire(shooterElite, shooterElite.Def, EliteShots.Kind.Bolt, new Vector2(1f, 3.5f), Vector2.down * 3f);
        for (int i = 0; i < 10; i++) { EliteSystem.Shots.Step(Dt); HazardRuntime.Ensure().Step(Dt); }
        var other = EliteShip.CreateInPlay(Def("gunship"), new Vector2(1f, 1f));
        other.AttackCooldown = 99f;
        int hearts = other.Hearts;
        for (int i = 0; i < 90; i++) { EliteSystem.Shots.Step(Dt); HazardRuntime.Ensure().Step(Dt); EliteSystem.Step(Dt); }
        Check("an elite's shot costs another elite, which came into its path later, one heart (" + hearts + " -> " + other.Hearts + ")", other.Hearts == hearts - 1 || other.State == EliteState.Dead);
        Check("... and never its own elite", shooterElite != null && shooterElite.Hearts == shooterElite.Def.hearts);
    }

    // ---- 5. mine blasts ----------------------------------------------------------------------

    static void MineBlastReaches()
    {
        FreshBoard();
        var a = Enemy(EnemyRole.Mine, new Vector2(0f, 0f), true);
        var b = Enemy(EnemyRole.Mine, new Vector2(.8f, 0f), true);
        var rock = Enemy(EnemyRole.Rock, new Vector2(-.7f, .2f), false);   // even one just come in
        var far = Enemy(EnemyRole.Rock, new Vector2(0f, 3f), true);
        FriendlyFire.MineBlast(a);
        FriendlyFire.StepBlasts(FriendlyFire.MineBlastDelay + .01f);
        Check("a bursting mine blasts a mine and a rock beside it (chain), not a far rock", Gone(b) && Gone(rock) && !Gone(far));
    }

    // ---- 6. body crashes ----------------------------------------------------------------------

    static void CrashesReach()
    {
        FreshBoard();
        var chaser = Enemy(EnemyRole.Chaser, new Vector2(0f, 0f), true);
        var rock = Enemy(EnemyRole.Rock, new Vector2(.1f, 0f), true);
        Check("a chaser is a free mover", FriendlyFire.FreeMover(chaser));
        bool crashed = FriendlyFire.StepCrashes(ShipTargets.View());
        Check("a chaser that runs into a rock breaks on it, both destroyed", crashed && Gone(chaser) && Gone(rock));

        FreshBoard();
        var elite = EliteShip.CreateInPlay(Def("gunship"), new Vector2(0f, 0f));
        elite.AttackCooldown = 99f;
        var victim = Enemy(EnemyRole.Fighter, new Vector2(.15f, 0f), true);
        FriendlyFire.ResetHostileCounters();
        Frames(.1f);
        Check("an elite's body crash destroys an enemy that flies into it and shows the word", Gone(victim) && FriendlyFire.Popups >= 1);
    }

    // ---- 7. not hurt ---------------------------------------------------------------------------

    static void PickupsSpared()
    {
        LaserBoard();
        var brain = Mine(3, true, 1f, out var rail);
        var l = LiveLaser(brain);
        float row = l != null ? l.Y : 1f;
        var pick = new GameObject("~StarDust") { tag = "pickUp" };
        pick.transform.position = new Vector3(-.5f, row, 0f);
        ClearTarget.Ensure(pick).SetRadius(.3f);
        FriendlyFire.Settle(pick);
        for (int i = 0; i < 12; i++) MineStep(brain);
        Check("a pickup in the laser is never hurt", !Gone(pick));
        Object.DestroyImmediate(rail);
    }

    static void BossGetsASliver()
    {
        // a real boss fight: an ordinary enemy's shot costs the boss a sliver, the boss's own shot nothing
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        BossEncounter.ResetRun();
        BossRails.Reset();
        PlayField.Reset();
        var camGo = new GameObject("Main Camera", typeof(Camera));
        camGo.tag = "MainCamera";
        var cam = camGo.GetComponent<Camera>();
        cam.orthographic = true;
        cam.orthographicSize = 5f;
        camGo.transform.position = new Vector3(0f, 0f, -10f);
        buttonClicks.playerDied = false;
        score.pauseCounter = 0;
        moveBackGround.speed = 0f;
        FriendlyFire.OnSceneLoaded("gameS1");
        startMenu.youAreInTutorial = false;
        PlayerPrefs.SetInt(WorldManager.PrefsCurrentWorld, 3);
        BossEncounter.Begin(3, null);
        var e = BossEncounter.Instance;
        e.Step(.1f, 1f);
        for (int i = 0; i < 400 && e.State == BossEncounter.Phase.Intro; i++) e.Step(.1f, 1f);
        FriendlyFire.ResetHostileCounters();
        HazardRuntime.Ensure().ClearAll();
        var body = Object.FindObjectsByType<BossTarget>(FindObjectsSortMode.None).FirstOrDefault();
        Check("a boss fight has a body target", e.State == BossEncounter.Phase.Fight && body != null);
        if (body == null) return;
        var t = body.GetComponent<ClearTarget>();
        var shooter = Enemy(EnemyRole.Fighter, new Vector2(0f, -4f), true);
        FriendlyFire.Settle(body.gameObject);
        float d0 = e.Damage;
        Check("an ordinary enemy's shot may hurt the boss; the boss's own fire (no enemy shooter) may not",
              FriendlyFire.HostileFireCanHit(t, shooter) && !FriendlyFire.HostileFireCanHit(t, null));
        Vector3 at = body.transform.position;
        FriendlyFire.HostileHit(t, at, "enemy shot");
        Check("... it costs a sliver (" + (e.Damage - d0).ToString("F3") + " of one ship hit), far less than one hit, and counts as friendly fire",
              e.Damage > d0 && e.Damage - d0 <= .1f && FriendlyFire.HostileBossHits == 1);
        Check("... rate limited: the next one inside the gap is refused", !FriendlyFire.HostileFireCanHit(t, shooter));
        BossEncounter.ResetRun();
    }

    // ---- 8. the word ----------------------------------------------------------------------------

    static void PopupEverywhere()
    {
        // rail laser kills two enemies on one frame: one word; after the gap, another
        LaserBoard();
        FriendlyFire.ResetHostileCounters();
        var brain = Mine(3, true, 1f, out var rail);
        var l = LiveLaser(brain);
        float row = l != null ? l.Y : 1f;
        var r1 = Enemy(EnemyRole.Rock, new Vector2(-1.5f, row), true);
        var r2 = Enemy(EnemyRole.Rock, new Vector2(-.5f, row), true);
        for (int i = 0; i < 4; i++) MineStep(brain);
        Check("a laser killing two enemies shows ONE friendly-fire word (" + FriendlyFire.Popups + ")", Gone(r1) && Gone(r2) && FriendlyFire.Popups == 1);
        Frames(FriendlyFire.HostileFirePopupGap + .1f);
        var rock = Enemy(EnemyRole.Rock, new Vector2(0f, 3f), true);
        var shot = EliteSystem.Shots.Fire(null, Def("gunship"), EliteShots.Kind.Bolt, new Vector2(0f, 3.8f), Vector2.down * 2f);
        for (int i = 0; i < 40; i++) { EliteSystem.Shots.Step(Dt); HazardRuntime.Ensure().Step(Dt); }
        Check("... and a later kill, after the gap, shows another (" + FriendlyFire.Popups + ")", Gone(rock) && FriendlyFire.Popups == 2);
        Object.DestroyImmediate(rail);

        // every other source shows it too
        FreshBoard();
        FriendlyFire.ResetHostileCounters();
        var a = Enemy(EnemyRole.Mine, new Vector2(0f, 0f), true);
        var b = Enemy(EnemyRole.Rock, new Vector2(.6f, 0f), true);
        FriendlyFire.MineBlast(a);
        FriendlyFire.StepBlasts(.2f);
        Check("a mine blast kill shows the word", Gone(b) && FriendlyFire.Popups == 1);
        FreshBoard();
        FriendlyFire.ResetHostileCounters();
        var h = AttackStrike.Arm(StrikeSpec.Standard(1), 0f, -1.5f, 1f, null);
        h.Ignite();
        Frames(.1f);
        var rk = Enemy(EnemyRole.Rock, new Vector2(0f, 0f), true);
        Frames(.2f);
        Check("an area hazard kill shows the word", Gone(rk) && FriendlyFire.Popups == 1);
        h.Cancel();
    }

    // ---- 9. the registry guard ---------------------------------------------------------------------

    static void RegistryGuard()
    {
        var asm = typeof(FriendlyFire).Assembly;

        // 1. every AttackHazard subclass is covered by a family test above
        var covered = new HashSet<string> { "AttackBlast", "AttackStrike", "AttackJet", "AttackWave", "AttackLash", "AttackLog" };
        var hazards = asm.GetTypes().Where(t => typeof(AttackHazard).IsAssignableFrom(t) && !t.IsAbstract).Select(t => t.Name).ToList();
        var uncovered = hazards.Where(n => !covered.Contains(n)).ToList();
        Check("every AttackHazard subclass (" + string.Join(", ", hazards) + ") has a later-arrival friendly-fire test here" +
              (uncovered.Count > 0 ? " -- MISSING: " + string.Join(", ", uncovered) : ""), uncovered.Count == 0 && hazards.Count >= 6);

        // 2. every class that makes a damaging hitbox or registers a hostile shot / zone is in the registry
        var reg = new HashSet<string>(FriendlyFireSources.All.Select(s => s.type));
        var triggers = new Regex(@"\.tag\s*=\s*(""Enimey""|BossHitbox\.Tag)|BossHitbox\.(Circle|Box)\(|HostileShots\.Register\(|:\s*[\w, ]*\b(IHostileShot|IHostileZone)\b");
        var classRx = new Regex(@"^(?:public |internal |static |sealed |abstract |partial )*class\s+(\w+)", RegexOptions.Multiline);
        var unregistered = new List<string>();
        var weapons = new List<string>();
        foreach (var path in Directory.GetFiles("Assets/Scripts", "*.cs", SearchOption.AllDirectories))
        {
            string src = File.ReadAllText(path);
            var ms = classRx.Matches(src);
            for (int i = 0; i < ms.Count; i++)
            {
                string name = ms[i].Groups[1].Value;
                string body = src.Substring(ms[i].Index, (i + 1 < ms.Count ? ms[i + 1].Index : src.Length) - ms[i].Index);
                if (!triggers.IsMatch(body)) continue;
                if (name == "EliteShip" || name == "BossActor" || name == "BossHitbox") continue;   // bodies / the factory: targets, not weapons
                weapons.Add(name);
                if (!reg.Contains(name)) unregistered.Add(name + " (" + Path.GetFileName(path) + ")");
            }
        }
        Check("every class that makes a damaging hitbox / registers a hostile shot or zone is listed in FriendlyFireSources (" + string.Join(", ", weapons.Distinct()) + ")" +
              (unregistered.Count > 0 ? " -- NOT REGISTERED: " + string.Join(", ", unregistered) : ""), unregistered.Count == 0 && weapons.Distinct().Count() >= 5);

        // 3. every registered source exists and its file runs the shared path
        var bad = new List<string>();
        foreach (var s in FriendlyFireSources.All)
        {
            var type = asm.GetTypes().FirstOrDefault(t => t.Name == s.type);
            if (type == null) { bad.Add(s.type + " (no such type)"); continue; }
            bool ok = false;
            foreach (var path in Directory.GetFiles("Assets/Scripts", "*.cs", SearchOption.AllDirectories))
            {
                string src = File.ReadAllText(path);
                var tops = classRx.Matches(src);
                for (int i = 0; i < tops.Count && !ok; i++)
                {
                    if (tops[i].Groups[1].Value != s.type) continue;
                    string body = src.Substring(tops[i].Index, (i + 1 < tops.Count ? tops[i + 1].Index : src.Length) - tops[i].Index);
                    ok = body.Contains(s.marker);
                }
                if (ok) break;
            }
            if (!ok) bad.Add(s.type + " (does not call " + s.marker + ")");
        }
        Check("every registered source (" + FriendlyFireSources.All.Length + ") calls its shared friendly-fire entry point" +
              (bad.Count > 0 ? " -- " + string.Join("; ", bad) : ""), bad.Count == 0);

        // 4. the word and the cap live in one place
        string ff = File.ReadAllText("Assets/Scripts/Gameplay/Enemies/FriendlyFire.cs");
        Check("the FRIENDLY FIRE word is shown from FriendlyFire.Popup only",
              Directory.GetFiles("Assets/Scripts", "*.cs", SearchOption.AllDirectories)
                  .Count(p => File.ReadAllText(p).Contains("\"FRIENDLY FIRE\"")) == 1 && ff.Contains("\"FRIENDLY FIRE\""));
    }
}
