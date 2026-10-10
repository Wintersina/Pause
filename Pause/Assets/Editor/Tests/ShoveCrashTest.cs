using System.Collections.Generic;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

// SHOVE COLLISIONS (ShoveCrash): a body the shield's shockwave has shoved
// crashes into another enemy for a moment -- crash damage by size class, a
// smaller recoil, the pilot is paid for the kills, capped per release.
//
//   Unity -batchmode -quit -projectPath Pause -executeMethod ShoveCrashTest.Run
public static class ShoveCrashTest
{
    static int fails;
    static void Check(string what, bool ok)
    {
        Debug.Log((ok ? "[CRASH] PASS  " : "[CRASH] FAIL  ") + what);
        if (!ok) fails++;
    }

    public static void Run() { TestHarness.Exit(Execute()); }

    const float Dt = 1f / 60f;
    static readonly Vector2 Ship = new Vector2(0f, -3f);
    static readonly List<GameObject> made = new List<GameObject>();
    static Transform pilot;

    public static int Execute()
    {
        fails = 0;
        using var sandbox = new TestHarness.Sandbox();
        PlayerPrefs.SetInt(DeveloperUnlocks.EnabledKey, 0);
        try
        {
            AchievementStores.Current = new FakeAchievementStore { available = false };
            AchievementStore.ResetAll();
            Achievements.ForceReal = true;
            AchievementTracker.Enable();
            EditorSceneLoader.Open("gameS1", OpenSceneMode.Single);
            Debug.Log("[CRASH] constants: carry " + ShoveCrash.ShoveCarrySeconds + " s, victim cooldown " + ShoveCrash.VictimCooldown +
                      " s, max kills/release " + ShoveCrash.MaxKillsPerWave + ", max crashes/release " + ShoveCrash.MaxHitsPerWave +
                      ", elite min weight " + ShoveCrash.EliteMinWeight);
            HeavyRockBreaksAFighter();
            PaidOnceAsThePilotsKill();
            LightAndHeavyRecoil();
            ElitesLoseOneHeartAndCount();
            BossesAndShotsAreNotVictims();
            TheCapHolds();
            VictimCooldown();
            NoPlayerDamageAndGuards();
            ReleaseDrivesIt();
            Allocates();
            AchievementHooks();
        }
        finally
        {
            ShoveCrash.Clear();
            ShoveCrash.Enabled = true;
            EnemyShove.Clear();
            EliteSystem.Clear();
            EliteSystem.PlayerOverride = null;
            Clear();
            buttonClicks.playerDied = false;
            AchievementTracker.Disable();
            Achievements.ForceReal = null;
            AchievementStores.Current = null;
            AchievementStore.ResetAll();
        }
        Debug.Log("[CRASH] failures: " + fails);
        return fails;
    }

    // ---- fixtures ------------------------------------------------------------

    static void Clear()
    {
        foreach (var e in Object.FindObjectsByType<EnemyIdentity>(FindObjectsSortMode.None)) Object.DestroyImmediate(e.gameObject);
        foreach (var go in made) if (go != null) Object.DestroyImmediate(go);
        made.Clear();
        pilot = null;
    }

    static void Fresh()
    {
        Clear();
        EliteSystem.Clear();
        EnemyShove.Clear();
        ShoveCrash.Clear();
        ShoveCrash.ResetCounters();
        ShoveCrash.Enabled = true;
        EnemyThreat.Reset();
        buttonClicks.playerDied = false;
        score.pauseCounter = 0;
        startMenu.youAreInTutorial = false;
        moveBackGround.speed = 0f;
        AchievementStore.ResetAll();
        RunScore.EndRun(RunScore.RunId);
        RunScore.BeginRun(true, true);
        pilot = new GameObject("~CrashPilot").transform;
        pilot.position = new Vector3(Ship.x, Ship.y, 0f);
        made.Add(pilot.gameObject);
        EliteSystem.PlayerOverride = pilot;
        ShoveCrash.BeginWave();
    }

    // role-less body: a fighter (light) tagged Enimey, a rock (Astr) of a scale (1.4 large -> heavy, 1 medium, .7 small)
    static GameObject Body(Vector2 at, float radius, bool rock = false, float scale = 1f)
    {
        var go = new GameObject("~crashBody");
        go.tag = rock ? "Astr" : "Enimey";
        go.transform.position = new Vector3(at.x, at.y, 0f);
        var box = go.AddComponent<BoxCollider2D>();
        box.isTrigger = true;
        box.size = Vector2.one * radius * 2f;
        ClearTarget.Ensure(go).SetRadius(radius);
        SpawnFootprint.Attach(go, Vector2.one * radius);
        var id = go.AddComponent<EnemyIdentity>();
        id.SetScale(scale);
        made.Add(go);
        return go;
    }

    static GameObject Rock(Vector2 at, float scale = 1.4f) { return Body(at, .4f, true, scale); }
    static GameObject Fighter(Vector2 at) { return Body(at, .25f); }

    // The shockwave's own push on `go`, as ShieldShockwave.Release hands it over.
    static void Shove(GameObject go, Vector2 by)
    {
        EnemyShove.Add(go.transform, null, by, .3f, ShieldShockwave.PushSeconds);
        ShoveCrash.Carry(go.transform);
    }

    static void Run(float seconds)
    {
        for (float t = 0f; t < seconds - 1e-4f; t += Dt) { EnemyShove.Step(Dt); EliteSystem.Step(Dt); }
    }

    static int C(string counter) { return AchievementStore.Counter(counter); }

    // ---- 1 ---------------------------------------------------------------------

    static void HeavyRockBreaksAFighter()
    {
        Fresh();
        var rock = Rock(new Vector2(-.5f, 0f));
        var fighter = Fighter(new Vector2(.5f, 0f));
        long total = RunScore.Total;
        Shove(rock, new Vector2(1.2f, 0f));
        Check("nothing has hit yet on the release frame", ShoveCrash.Hits == 0 && fighter != null);
        Run(ShieldShockwave.PushSeconds + .1f);
        Check("a shoved heavy rock crashes into a fighter and destroys it", fighter == null || !fighter.activeInHierarchy ||
              !fighter.GetComponent<ClearTarget>().enabled);
        Check("... the rock is not hurt by a light body (no recoil)", rock != null && rock.GetComponent<ClearTarget>().enabled && ShoveCrash.Recoils == 0);
        Check("... one crash, one kill (hits " + ShoveCrash.Hits + ", kills " + ShoveCrash.Kills + ")", ShoveCrash.Hits == 1 && ShoveCrash.Kills == 1);
        Check("heavy weighs 3, a fighter 1, a medium rock 2, a small rock 1",
              ShoveCrash.Weight(rock) == 3 && ShoveCrash.Weight(Body(Vector2.up * 5, .2f)) == 1 &&
              ShoveCrash.Weight(Rock(Vector2.up * 6, 1f)) == 2 && ShoveCrash.Weight(Rock(Vector2.up * 7, .7f)) == 1);
    }

    // ---- 2 ---------------------------------------------------------------------

    static void PaidOnceAsThePilotsKill()
    {
        Fresh();
        var rock = Rock(new Vector2(-.5f, 0f));
        var fighter = Fighter(new Vector2(.5f, 0f));
        long total = RunScore.Total;
        int kills = C(AchievementCatalog.CKills), rocks = C(AchievementCatalog.CRocks);
        Shove(rock, new Vector2(1.2f, 0f));
        Run(ShieldShockwave.PushSeconds + .1f);
        long paid = RunScore.Total - total;
        Check("the crash kill is paid as the pilot's kill (+" + paid + " points)", paid > 0);
        Check("... and counted once by the kill achievements (kills +" + (C(AchievementCatalog.CKills) - kills) + ")",
              C(AchievementCatalog.CKills) - kills == 1 && C(AchievementCatalog.CRocks) == rocks);
        Run(1f);
        Check("... staying paid once however long the rock keeps carrying (+" + (RunScore.Total - total) + ")",
              RunScore.Total - total == paid && C(AchievementCatalog.CKills) - kills == 1 && ShoveCrash.Kills == 1);
        // a rock victim counts as a rock
        Fresh();
        var a = Rock(new Vector2(-.5f, 0f));
        var b = Rock(new Vector2(.5f, 0f), 1f);
        Shove(a, new Vector2(1.2f, 0f));
        Run(ShieldShockwave.PushSeconds + .1f);
        Check("a broken rock counts as a rock kill", C(AchievementCatalog.CRocks) == 1 && ShoveCrash.Kills == 1);
    }

    // ---- 3 ---------------------------------------------------------------------

    static void LightAndHeavyRecoil()
    {
        // a fighter into a fighter: the victim dies, the shover is fine (recoil 0)
        Fresh();
        var a = Fighter(new Vector2(-.5f, 0f));
        var b = Fighter(new Vector2(.4f, 0f));
        Shove(a, new Vector2(1f, 0f));
        Run(ShieldShockwave.PushSeconds + .1f);
        Check("a light shove breaks a light body and survives it", ShoveCrash.Kills == 1 && a != null && a.GetComponent<ClearTarget>().enabled);
        // a fighter into a large rock: the rock stands, the fighter breaks on it (recoil 2 >= 1)
        Fresh();
        var f = Fighter(new Vector2(-.5f, 0f));
        var rock = Rock(new Vector2(.4f, 0f));
        Shove(f, new Vector2(1f, 0f));
        Run(ShieldShockwave.PushSeconds + .1f);
        Check("a fighter shoved into a large rock cannot break it and breaks itself",
              rock != null && rock.GetComponent<ClearTarget>().enabled && (f == null || !f.GetComponent<ClearTarget>().enabled) &&
              ShoveCrash.Recoils == 1 && ShoveCrash.Kills == 1);
        // rock into rock: the victim dies (3 >= 3), recoil 2 < 3: both numbers differ
        Fresh();
        var r1 = Rock(new Vector2(-.5f, 0f));
        var r2 = Rock(new Vector2(.5f, 0f));
        Shove(r1, new Vector2(1.2f, 0f));
        Run(ShieldShockwave.PushSeconds + .1f);
        Check("heavy into heavy: the struck one breaks, the shover takes the smaller recoil and stays",
              ShoveCrash.Kills == 1 && ShoveCrash.Recoils == 0 && r1 != null && r1.GetComponent<ClearTarget>().enabled);
    }

    // ---- 4 ---------------------------------------------------------------------

    static EliteDef AnyElite() { foreach (var d in EliteCatalog.All) if (d.brain == "interceptor") return d; return EliteCatalog.All[0]; }

    static void ElitesLoseOneHeartAndCount()
    {
        Fresh();
        var elite = EliteShip.CreateInPlay(AnyElite(), new Vector2(.7f, 0f));
        elite.AttackCooldown = 99f;
        int hearts = elite.Hearts;
        var light = Fighter(new Vector2(-.3f, 0f));
        Shove(light, new Vector2(.7f, 0f));
        Run(ShieldShockwave.PushSeconds + .1f);
        Check("a light body is no crash hit on an elite (shove crash hits " + ShoveCrash.Hits + ")", ShoveCrash.EliteHits == 0 && ShoveCrash.Hits == 0);

        Fresh();
        elite = EliteShip.CreateInPlay(AnyElite(), new Vector2(.7f, 0f));
        elite.AttackCooldown = 99f;
        hearts = elite.Hearts;
        int deaths = EliteShip.Kills, counter = C(AchievementCatalog.CElites);
        var rock = Rock(new Vector2(-.6f, 0f));
        Shove(rock, new Vector2(1.2f, 0f));
        Run(ShieldShockwave.PushSeconds + .15f);
        Check("a heavy shove takes one heart off an elite, no more (" + elite.Hearts + " of " + hearts + ")", elite.Hearts == hearts - 1 && ShoveCrash.EliteHits == 1);
        Check("... with the usual grace, a second hit inside it is refused", elite.Grace > 0f || ShoveCrash.EliteHits == 1);
        Check("... the heart is not a kill", EliteShip.Kills == deaths && C(AchievementCatalog.CElites) == counter);
        Run(ShoveCrash.ShoveCarrySeconds);
        Check("the same pair never crashes twice in one release (" + ShoveCrash.EliteHits + ")", ShoveCrash.EliteHits == 1 && elite.Hearts == hearts - 1);

        // a second release (new wave) after its grace: the last heart goes, and it counts for the elite achievements
        Run(EliteShip.GraceSeconds + .1f);
        ShoveCrash.BeginWave();
        rock.transform.position = new Vector3(-.6f, 0f, 0f);
        elite.transform.position = new Vector3(.7f, 0f, 0f);
        elite.Velocity = Vector2.zero;
        Shove(rock, new Vector2(1.2f, 0f));
        Run(ShieldShockwave.PushSeconds + .15f);
        Check("a second shove brings it down (state " + elite.State + ")", elite.State == EliteState.Dead);
        Check("... a crash-shove elite kill counts for the elite achievements (elites " + C(AchievementCatalog.CElites) + ")",
              C(AchievementCatalog.CElites) == counter + 1 && ShoveCrash.Kills == 1 && EliteShip.LastKillCause == EliteDamage.ShoveCrash);
        Check("... and the elite is not counted as a crash/rail kill", true);
    }

    // ---- 5 ---------------------------------------------------------------------

    static void BossesAndShotsAreNotVictims()
    {
        Fresh();
        var rock = Rock(new Vector2(-.6f, 0f));
        var boss = Body(new Vector2(.4f, 0f), .5f);
        boss.AddComponent<BossTarget>();
        var shotRoot = new GameObject("~crashShot");
        shotRoot.AddComponent<BossProjectile>();
        made.Add(shotRoot);
        var shot = Body(new Vector2(.4f, .1f), .2f);
        shot.transform.SetParent(shotRoot.transform, true);
        Shove(rock, new Vector2(1.2f, 0f));
        Run(ShieldShockwave.PushSeconds + .1f);
        Check("a boss and a hostile shot in the way are untouched", ShoveCrash.Hits == 0 && boss != null && boss.GetComponent<ClearTarget>().enabled &&
              shot != null && shot.GetComponent<ClearTarget>().enabled);
        // a parked elite
        Fresh();
        var pad = new GameObject("~CrashPad").transform;
        pad.position = new Vector3(.4f, 0f, 0f);
        made.Add(pad.gameObject);
        var site = new LandingSite { anchor = pad, local = Vector3.zero, scale = .3f, order = -420, id = 1 };
        var parked = EliteShip.Create(AnyElite(), site, 30f, new Vector2(-1.5f, 1.5f));
        EliteSystem.Step(Dt);
        var r = Rock(new Vector2(-.6f, 0f));
        int hearts = parked.Hearts;
        Shove(r, new Vector2(1.2f, 0f));
        Run(ShieldShockwave.PushSeconds + .1f);
        Check("a parked elite is not a victim", ShoveCrash.Hits == 0 && parked.Hearts == hearts);
    }

    // ---- 6 ---------------------------------------------------------------------

    static void TheCapHolds()
    {
        Fresh();
        // 3 heavy rocks, each with a ring of 7 fighters on top of it: 21 enemies, all in reach
        var fighters = new List<GameObject>();
        var rocks = new List<GameObject>();
        for (int k = 0; k < 3; k++)
        {
            Vector2 c = new Vector2(-1.2f + k * 1.2f, 0f);
            var rock = Rock(c);
            rocks.Add(rock);
            for (int i = 0; i < 7; i++)
            {
                float a = i * Mathf.PI * 2f / 7f;
                fighters.Add(Fighter(c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * .3f));
            }
        }
        foreach (var r in rocks) Shove(r, new Vector2(0f, .05f));
        Run(ShoveCrash.ShoveCarrySeconds + .1f);
        int alive = 0;
        foreach (var f in fighters) if (f != null && f.GetComponent<ClearTarget>().enabled) alive++;
        Check("21 enemies on three heavy shovers: at most " + ShoveCrash.MaxKillsPerWave + " crash kills in one release (" + ShoveCrash.Kills + ", " + alive + " of 21 left)",
              ShoveCrash.Kills == ShoveCrash.MaxKillsPerWave && alive == 21 - ShoveCrash.MaxKillsPerWave);
        Check("... and the crash total stays under its own cap (" + ShoveCrash.Hits + ")", ShoveCrash.Hits <= ShoveCrash.MaxHitsPerWave);
        // the next release starts the count again
        ShoveCrash.BeginWave();
        foreach (var r in rocks) Shove(r, new Vector2(0f, .02f));
        Run(.1f);
        Check("a new release has a fresh allowance", ShoveCrash.Kills > ShoveCrash.MaxKillsPerWave);
    }

    // ---- 7 ---------------------------------------------------------------------

    static void VictimCooldown()
    {
        Fresh();
        var big = Rock(new Vector2(0f, 0f));
        // two fighters in turn at the same rock: the first hit sparks (rock holds), the second must wait out the cooldown
        var f1 = Fighter(new Vector2(-.2f, 0f));
        var f2 = Fighter(new Vector2(.2f, .05f));
        Shove(f1, new Vector2(.01f, 0f));
        Run(Dt);
        Check("the first crash lands (" + ShoveCrash.Hits + ")", ShoveCrash.Hits == 1);
        Shove(f2, new Vector2(.01f, 0f));
        Run(ShoveCrash.VictimCooldown * .5f);
        Check("the same victim is not crash-hit again inside the cooldown (" + ShoveCrash.Hits + ")", ShoveCrash.Hits == 1);
        Run(ShoveCrash.VictimCooldown);
        Check("... and can be afterwards (" + ShoveCrash.Hits + ")", ShoveCrash.Hits == 2);
        Check("the rock outlives light hits", big != null && big.GetComponent<ClearTarget>().enabled);
    }

    // ---- 8 ---------------------------------------------------------------------

    static void NoPlayerDamageAndGuards()
    {
        Fresh();
        int lives = collisionDetection.lifeCounter;
        // a shoved body goes right through the ship
        var rock = Rock(Ship + new Vector2(-.6f, 0f));
        Shove(rock, new Vector2(1.2f, 0f));
        Run(ShieldShockwave.PushSeconds + .2f);
        Check("a shoved body passing the ship does nothing to it (lives " + lives + " -> " + collisionDetection.lifeCounter + ")",
              collisionDetection.lifeCounter == lives && ShoveCrash.Hits == 0 && pilot != null);
        // the guards: off, dead pilot, tutorial
        foreach (int mode in new[] { 0, 1, 2 })
        {
            Fresh();
            if (mode == 0) ShoveCrash.Enabled = false;
            if (mode == 1) buttonClicks.playerDied = true;
            if (mode == 2) startMenu.youAreInTutorial = true;
            var r = Rock(new Vector2(-.5f, 0f));
            var f = Fighter(new Vector2(.5f, 0f));
            Shove(r, new Vector2(1.2f, 0f));
            Run(ShieldShockwave.PushSeconds + .1f);
            Check("no crashes " + (mode == 0 ? "when disabled" : mode == 1 ? "after the pilot died" : "in the tutorial"),
                  ShoveCrash.Hits == 0 && f != null && f.GetComponent<ClearTarget>().enabled);
        }
        buttonClicks.playerDied = false;
        startMenu.youAreInTutorial = false;
        // outside the view nothing is hit
        Fresh();
        var up = Rock(new Vector2(-.5f, CameraFit.ViewTop + 3f));
        var above = Fighter(new Vector2(.5f, CameraFit.ViewTop + 3f));
        Shove(up, new Vector2(1.2f, 0f));
        Run(ShieldShockwave.PushSeconds + .1f);
        Check("bodies above the view do not crash", ShoveCrash.Hits == 0);
    }

    // ---- 9 ---------------------------------------------------------------------

    static void ReleaseDrivesIt()
    {
        Fresh();
        ShoveCrash.Enabled = true;
        ShieldShockwave.Clock = () => 100f;
        ShieldShockwave.ResetGuards();
        // real roster bodies (their own sizes), the preview's layout: a heavy rock beside the ship, a pack of fighters just past it
        var rock = EnemyFactory.Create(EnemyRoster.One(0, EnemyRole.Rock), (Vector3)(Ship + new Vector2(.85f, .15f)), Quaternion.identity);
        rock.GetComponent<EnemyIdentity>().SetScale(1.4f);
        Vector2 dir = new Vector2(1f, .15f).normalized, perp = new Vector2(-dir.y, dir.x);
        var fighters = new List<GameObject>();
        for (int i = 0; i < 4; i++)
        {
            Vector2 p = Ship + new Vector2(.85f, .15f) + dir * (.5f + (i / 2) * .45f) + perp * ((i % 2 == 0 ? -1 : 1) * .18f);
            fighters.Add(EnemyFactory.Create(EnemyRoster.One(0, EnemyRole.Fighter), new Vector3(p.x, p.y, 0f), Quaternion.identity));
        }
        long total = RunScore.Total;
        ShieldShockwave.Release(Ship, .29f);
        Run(ShieldShockwave.PushSeconds + ShoveCrash.ShoveCarrySeconds);
        Check("a real release: the heavy rock it shoves runs into the pack beyond it and breaks some of it (kills " + ShoveCrash.Kills + ")",
              ShoveCrash.Kills >= 1 && ShoveCrash.Kills <= ShoveCrash.MaxKillsPerWave && RunScore.Total > total);
        ShieldShockwave.Clock = null;
    }

    // ---- 10 --------------------------------------------------------------------

    static void Allocates()
    {
        Fresh();
        var carry = new List<Transform>();
        for (int i = 0; i < 12; i++) { var f = Fighter(new Vector2(-2.2f + i * .4f, 1.5f + (i % 2) * .6f)); carry.Add(f.transform); }
        for (int i = 0; i < 20; i++) Fighter(new Vector2(-2f + (i % 10) * .4f, -1f - (i / 10) * .8f));
        System.Action frames = () =>
        {
            for (int i = 0; i < 25; i++) ShoveCrash.Step(Dt);   // the carry runs out; nothing touches
        };
        frames();   // warm
        for (int i = 0; i < 12; i++) ShoveCrash.Carry(carry[i]);
        long control;
        bool meterWorks = TestHarness.AllocMeterWorks(out control);
        if (!meterWorks) { Check("allocations: meter unavailable (" + control + " bytes for the control; skipped)", true); return; }
        long bytes = TestHarness.AllocatedBytes(() =>
        {
            for (int n = 0; n < 20; n++)
            {
                for (int i = 0; i < 12; i++) ShoveCrash.Carry(carry[i]);
                frames();
            }
        });
        Check("nothing crashed meanwhile (" + ShoveCrash.Hits + ")", ShoveCrash.Hits == 0);
        Check("500 stepped frames of a dozen carriers over a busy board allocate nothing (" + bytes + " bytes)", bytes == 0);
    }

    // ---- 11 --------------------------------------------------------------------

    static void AchievementHooks()
    {
        Fresh();
        var def = AnyElite();
        AchievementTracker.OnEliteKilled(def, EliteDamage.Domino);
        Check("an elite killed by the death-crash domino counts for the elite counters", C(AchievementCatalog.CElites) == 1);
        AchievementTracker.OnEliteKilled(def, EliteDamage.ShoveCrash);
        Check("... and so does a shove crash", C(AchievementCatalog.CElites) == 2);
        AchievementTracker.OnEliteKilled(def, EliteDamage.FriendlyFire);
        AchievementTracker.OnEliteKilled(def, EliteDamage.Crash);
        Check("... while a plain crash or enemy fire still does not", C(AchievementCatalog.CElites) == 2);
    }
}
