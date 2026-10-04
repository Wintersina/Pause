using System.IO;
using System.Reflection;
using UnityEditor.SceneManagement;
using UnityEngine;

// Feature: "the secret bosses at the end of each level ... each boss pauses
// the user, makes the user lose all speed and drop to a constant speed of
// 20, then it shoots projectiles at the user that must be dodged."
//
// Drives BossEncounter frame by frame (Step(realDt, timeScale)) in edit mode.
public static class BossEncounterTest
{
    static int fails;
    static void Check(string what, bool ok)
    {
        Debug.Log((ok ? "[BOSS] PASS  " : "[BOSS] FAIL  ") + what);
        if (!ok) fails++;
    }

    public static void Run()
    {
        TestHarness.Exit(Execute());
    }

    const BindingFlags Inst = BindingFlags.NonPublic | BindingFlags.Instance;

    public static int Execute()
    {
        fails = 0;
        using var sandbox = new TestHarness.Sandbox();
        int rush = PlayerPrefs.GetInt(BossDev.RushKey, -1);
        try
        {
            EveryWorldHasCompleteArt();
            LevelEndStartsTheBossThenThePortal();
            EmberHasABossButNoPortal();
            IntroFreezeIsScriptedAndFree();
            SpeedIsHeldAt20ThenReleased();
            ProjectilesFreezeAtTimeScaleZero();
            SpawningIsSuspended();
            UltimateHitsSpendHitPoints();
            HitPointsOrTimerWhicheverFirst();
            ProjectilePoolIsBounded();
            CodexEntryIsSecretUntilMet();
            MusicFallsBackWithoutABossClip();
            DevTriggerWorks();
            DeathShowsTheNormalPanel();
        }
        finally
        {
            BossEncounter.ResetRun();
            if (rush < 0) PlayerPrefs.DeleteKey(BossDev.RushKey);
            else PlayerPrefs.SetInt(BossDev.RushKey, rush);
        }
        Debug.Log("[BOSS] failures: " + fails);
        return fails;
    }

    // ---- fixtures ------------------------------------------------------

    static void FreshScene(int world = 0)
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        BossEncounter.ResetRun();
        var camGo = new GameObject("Main Camera", typeof(Camera));
        camGo.tag = "MainCamera";
        var cam = camGo.GetComponent<Camera>();
        cam.orthographic = true;
        cam.orthographicSize = 5f;
        camGo.transform.position = new Vector3(0f, 0f, -10f);
        buttonClicks.playerDied = false;
        score.pauseCounter = 0; // the world runs without a touch in batch mode
        moveBackGround.speed = .37f;
        Time.timeScale = 1f;
        PlayerPrefs.SetInt(DeveloperUnlocks.EnabledKey, 0);
        PlayerPrefs.SetInt(WorldManager.PrefsCurrentWorld, world);
    }

    static WorldManager World(float timer)
    {
        var wm = new GameObject("~WorldManager").AddComponent<WorldManager>();
        wm.SendMessage("Awake");
        typeof(WorldManager).GetField("timer", Inst).SetValue(wm, timer);
        return wm;
    }

    // Runs the encounter until it leaves `phase` (or a step budget runs out).
    static void RunWhile(BossEncounter e, BossEncounter.Phase phase, float dt = .1f, int budget = 2000)
    {
        for (int i = 0; i < budget && e.State == phase; i++) e.Step(dt, 1f);
    }

    static BossEncounter StartFight(int world = 0)
    {
        BossEncounter.Begin(world, null);
        var e = BossEncounter.Instance;
        e.Step(.1f, 1f);                          // Pending -> Intro
        RunWhile(e, BossEncounter.Phase.Intro);   // Intro -> Fight
        return e;
    }

    // ---- tests ---------------------------------------------------------

    static void EveryWorldHasCompleteArt()
    {
        Check("one boss per world", BossCatalog.All.Length == WorldManager.Worlds.Length);
        var names = new System.Collections.Generic.HashSet<string>();
        for (int w = 0; w < WorldManager.Worlds.Length; w++)
        {
            var boss = BossCatalog.ForWorld(w);
            Check(boss.artKey + " belongs to " + WorldManager.Worlds[w].displayName,
                  boss.artKey == WorldManager.Worlds[w].displayName);
            Check(boss.artKey + " has a distinct name", names.Add(boss.name));
            Check(boss.artKey + " has 2-3 attack patterns", boss.attacks.Length >= 2 && boss.attacks.Length <= 3);
            bool tells = true;
            foreach (var a in boss.attacks) tells &= a.tellSeconds >= .5f;
            Check(boss.artKey + " telegraphs every attack (>= 0.5s)", tells);
            int n = 0, missing = 0;
            foreach (var s in BossArt.AllSprites(boss)) { n++; if (s == null) missing++; }
            Check(boss.artKey + " art resolves (" + (n - missing) + "/" + n + ")", missing == 0);
            var idle = BossArt.Body(boss, BossArt.Idle0);
            Check(boss.artKey + " body cells are square", idle != null && Mathf.Approximately(idle.rect.width, idle.rect.height));
            Check(boss.artKey + " idle and death differ",
                  idle != null && BossArt.Body(boss, BossArt.Death(2)) != null && idle != BossArt.Body(boss, BossArt.Death(2)));
        }
        Check("warning slab resolves", BossArt.Warning() != null);
        Check("death plays every frame once", BossArt.FrameAt(BossArt.DeathTicks, BossArt.Seconds(BossArt.DeathTicks) + .01f, false) == BossArt.DeathFrames);
        foreach (var dir in new[] { "Space", "Frost", "Verdant", "Ember" })
            Check(dir + " SVG sources exist", Directory.GetFiles("Assets/Art/Bosses/" + dir + "/src~", "*.svg").Length >= 29);
    }

    static void LevelEndStartsTheBossThenThePortal()
    {
        FreshScene(0);
        var wm = World(-1f);
        wm.SendMessage("Update");
        var e = BossEncounter.Instance;
        Check("level end starts the boss", BossEncounter.Running && e != null && e.Boss == BossCatalog.ForWorld(0));
        Check("... instead of the portal", !wm.PortalIsOpen && Object.FindFirstObjectByType<Portal>() == null);

        e.Step(.1f, 1f);
        RunWhile(e, BossEncounter.Phase.Intro);
        wm.SendMessage("Update");
        Check("level clock is stopped during the fight", !wm.PortalIsOpen);
        RunWhile(e, BossEncounter.Phase.Fight);
        Check("a survived fight ends in the outro", e.State == BossEncounter.Phase.Outro);
        RunWhile(e, BossEncounter.Phase.Outro);
        Check("encounter done", e.State == BossEncounter.Phase.Done && !BossEncounter.Running);
        Check("the portal opens after the encounter", wm.PortalIsOpen && Object.FindFirstObjectByType<Portal>() != null);
        Check("the boss and its shots are gone", Object.FindFirstObjectByType<BossActor>() == null &&
              GameObject.Find("~BossProjectiles") == null);
        Check("the same world's boss doesn't come back for a missed portal", !BossEncounter.Begin(0, null));
    }

    static void EmberHasABossButNoPortal()
    {
        FreshScene(3);
        var wm = World(-1f);
        wm.SendMessage("Update");
        Check("Ember's level end starts its boss", BossEncounter.Running && BossEncounter.Instance.Boss.artKey == "Ember");
        var e = BossEncounter.Instance;
        e.Step(.1f, 1f);
        RunWhile(e, BossEncounter.Phase.Intro);
        RunWhile(e, BossEncounter.Phase.Fight);
        RunWhile(e, BossEncounter.Phase.Outro);
        wm.SendMessage("Update");
        Check("no portal after the last world's boss", !wm.PortalIsOpen && !BossEncounter.Running);
    }

    static void IntroFreezeIsScriptedAndFree()
    {
        FreshScene();
        var bg = new GameObject("bg").AddComponent<moveBackGround>();
        int pauses = score.pauseCounter;
        BossEncounter.Begin(0, null);
        var e = BossEncounter.Instance;
        e.Step(.1f, 1f);
        Check("intro running", e.State == BossEncounter.Phase.Intro);
        Check("intro asks for a scripted freeze", BossEncounter.ScriptedFreeze);
        Time.timeScale = 1f;
        bg.SendMessage("Update"); // the world would run (no pauses left)...
        Check("... and moveBackGround freezes time anyway", Time.timeScale == 0f);
        Check("a press during the intro is free", BossEncounter.FreePress);
        RunWhile(e, BossEncounter.Phase.Intro);
        Check("intro ends in the fight after ~2s", e.State == BossEncounter.Phase.Fight);
        Check("no pause was spent by the intro", score.pauseCounter == pauses);
        Check("the first press after the intro is still free (finger was up)", BossEncounter.FreePress);
        Check("score.cs marks that press as already paid",
              File.ReadAllText("Assets/Scripts/Core/score.cs").Contains("if (BossEncounter.FreePress && TouchInput.IsPressed) pauseCounterBool = true;"));
        Check("no scripted freeze in the fight", !BossEncounter.ScriptedFreeze);
        Object.DestroyImmediate(bg.gameObject);
    }

    static void SpeedIsHeldAt20ThenReleased()
    {
        FreshScene();
        moveBackGround.speed = .41f;
        var bg = new GameObject("bg").AddComponent<moveBackGround>();
        bg.speedRampPerSecond = 50f; // any real ramp would show at once
        bg.maxSpeed = 5f;
        BossEncounter.Begin(0, null);
        var e = BossEncounter.Instance;
        e.Step(.1f, 1f);
        e.Step(BossConfig.SpeedDrainSeconds * .5f, 0f);
        Check("speed drains during the intro", moveBackGround.speed < .41f && moveBackGround.speed > 0f);
        e.Step(BossConfig.SpeedDrainSeconds, 0f);
        Check("all speed is lost", moveBackGround.speed == 0f);
        RunWhile(e, BossEncounter.Phase.Intro);
        Check("fight speed is 0.20 (HUD 20)", moveBackGround.speed == BossConfig.FightSpeed &&
              Mathf.RoundToInt(moveBackGround.speed * 100f) == 20);
        bool held = true;
        for (int i = 0; i < 40; i++)
        {
            bg.SendMessage("Update");
            moveBackGround.speed += BossEncounter.FilterSpeedChange(.05f); // the atom's boost, filtered
            e.Step(.1f, 1f);
            held &= moveBackGround.speed == BossConfig.FightSpeed;
        }
        Check("speed held at 0.20 through ramp and atom boosts", held);
        Check("atom boost filtered during the fight", BossEncounter.FilterSpeedChange(.05f) == 0f);
        string collision = File.ReadAllText("Assets/Scripts/Ship/collisionDetection.cs");
        Check("collisionDetection guards the atom's +0.05",
              collision.Contains("if (!BossEncounter.SpeedLocked) { moveBackGround.speed += .05f; atomCounter++; }"));
        RunWhile(e, BossEncounter.Phase.Fight);
        RunWhile(e, BossEncounter.Phase.Outro);
        Check("speed lock released after", !BossEncounter.SpeedLocked && BossEncounter.FilterSpeedChange(.05f) == .05f);
        moveBackGround.speed = BossConfig.FightSpeed;
        typeof(moveBackGround).GetMethod("speedUp", Inst).Invoke(bg, null);
        Check("the ramp runs again from 20", moveBackGround.speed >= BossConfig.FightSpeed &&
              (moveBackGround.speed > BossConfig.FightSpeed || Time.deltaTime == 0f));
        Object.DestroyImmediate(bg.gameObject);
    }

    static void ProjectilesFreezeAtTimeScaleZero()
    {
        FreshScene();
        var e = StartFight();
        var shot = e.Pool.Fire(e.Boss, BossShotStyle.Bolt, new Vector3(0f, 2f, 0f), new Vector2(0f, -4f));
        var lane = e.Pool.Lane(e.Boss, 0f, 1f, 2f, .5f, .5f);
        Vector3 at = shot.transform.position;
        float bossX = e.Actor.transform.position.x;
        for (int i = 0; i < 10; i++) e.Step(.1f, 0f);
        Check("a shot doesn't move while time is frozen", shot.transform.position == at && shot.Active);
        Check("a lane stays telegraphing while frozen", lane.Telegraphing);
        Check("the boss doesn't drift while frozen", e.Actor.transform.position.x == bossX);
        e.Step(.1f, 1f);
        Check("it moves again with time", shot.transform.position.y < at.y);
        e.Step(.6f, 1f);
        Check("the lane goes live after its telegraph", lane.Live && lane.Hitbox != null);
        var hit = shot.Hitbox;
        Check("shots hit through the normal enemy rules (tag + trigger)",
              hit != null && hit.CompareTag("Enimey") && hit.GetComponent<Collider2D>().isTrigger);
        Object.DestroyImmediate(hit);
        e.Step(.05f, 1f);
        Check("a shot whose hitbox was destroyed recycles", !shot.Active);
    }

    static void SpawningIsSuspended()
    {
        FreshScene();
        Check("no suspension without a boss", !BossEncounter.SuspendsSpawning);
        BossEncounter.Begin(0, null);
        Check("suspended while pending", BossEncounter.SuspendsSpawning);
        var e = BossEncounter.Instance;
        var hazard = new GameObject("rock");
        hazard.tag = "Astr";
        ClearTarget.Ensure(hazard);
        e.Step(.1f, 1f);
        RunWhile(e, BossEncounter.Phase.Intro);
        Check("on-board hazards are cleared by the boss's arrival", hazard == null);
        Check("suspended during the fight", BossEncounter.SuspendsSpawning);
        Check("enmiesOnBoard checks it", File.ReadAllText("Assets/Scripts/Gameplay/enmiesOnBoard.cs")
              .Contains("if (flying && !BossEncounter.SuspendsSpawning) spawn();"));
        RunWhile(e, BossEncounter.Phase.Fight);
        RunWhile(e, BossEncounter.Phase.Outro);
        Check("spawning resumes after", !BossEncounter.SuspendsSpawning);
    }

    static void UltimateHitsSpendHitPoints()
    {
        FreshScene();
        var e = StartFight();
        e.Step(.5f, 1f);
        float before = e.Remaining;
        var body = e.Actor.BodyHitbox;
        Check("the boss is an ultimate target (Enimey tag)", body != null && body.CompareTag("Enimey"));
        // Every ship attack (the volley included) hits through ShipAttackHits,
        // which hands a BossTarget the hit instead of destroying it.
        Check("the boss body is a registered attack target (IShipAttackTarget)",
              body.GetComponent<IShipAttackTarget>() is BossTarget && body.GetComponent<ClearTarget>() != null &&
              File.ReadAllText("Assets/Scripts/Gameplay/ShipPowerController.cs").Contains("ShipAttackHits.Hit(target, shipIndex)"));
        Check("the boss starts with BossConfig.HitPoints (" + BossConfig.HitPoints + ")", e.HitPointsLeft == BossConfig.HitPoints);
        Check("a homing shot on the boss is intercepted", BossTarget.Intercept(body, 1));
        Check("... and takes one hit point (" + e.HitPointsLeft + " left)", e.HitPointsLeft == BossConfig.HitPoints - 1 && e.Hits == 1);
        Check("... but never shaves the fight clock (it stays fixed)", Mathf.Abs(before - e.Remaining) < .001f);
        Check("... with a hit flash", e.Actor.Flashing && e.Actor.BodyFrame == BossArt.Hit);
        Check("the boss itself is not destroyed", body != null && e.Actor != null);
        float budget = 1f;
        ShipAttackHits.Hit(body, 1, .25f, ref budget);
        Check("a weaker attack contact (0.25 hit) banks, no whole point yet", body != null &&
              e.HitPointsLeft == BossConfig.HitPoints - 1 && Mathf.Abs(before - e.Remaining) < .001f);
        e.OnShipAttackHit(.5f);
        e.OnShipAttackHit(.25f);
        Check("weighted hits add up: 0.25 + 0.5 + 0.25 = one more point", e.HitPointsLeft == BossConfig.HitPoints - 2);
        var other = new GameObject("rock");
        Check("ordinary targets are not intercepted", !BossTarget.Intercept(other, 1));
        RunWhile(e, BossEncounter.Phase.Fight);
        Check("a boss with hit points left when the clock runs out retreats (SURVIVED)",
              e.Actor.State == BossActor.Mode.Retreating && !e.Destroyed);
    }

    // Whichever comes first: hit points (DESTROYED, 300) or the fight clock
    // (SURVIVED, 150). Scoring unchanged, loop scaling as before.
    static void HitPointsOrTimerWhicheverFirst()
    {
        Check("the end rule is hit points or survival, whichever first",
              BossConfig.EndRule == BossEndRule.HitPointsOrSurvival && BossConfig.HitPoints == 3);
        var remainingField = typeof(BossEncounter).GetField("remaining", Inst);

        // HP first: three full hits early in the fight end it at once.
        FreshScene();
        RunScore.EndRun(RunScore.RunId);
        RunScore.BeginRun(true, true);
        var e = StartFight();
        e.Step(1f, 1f);
        long score = RunScore.Total;
        for (int i = 0; i < BossConfig.HitPoints; i++) e.OnUltimateHit();
        float left = e.Remaining;
        e.Step(.05f, 1f);
        Check("HP first: the last hit point ends the fight with " + left.ToString("F1") + "s still on the clock",
              e.State == BossEncounter.Phase.Outro && left > BossConfig.FightSeconds * .5f);
        Check("HP first: DESTROYED -- it explodes and pays " + ScoreRules.BossDestroyed + " (" + (RunScore.Total - score) + ")",
              e.Destroyed && e.Actor.State == BossActor.Mode.Dying && RunScore.Total - score == ScoreRules.BossDestroyed);
        RunWhile(e, BossEncounter.Phase.Outro);
        Check("HP first: then Done", e.State == BossEncounter.Phase.Done);

        // HP first with weighted hits: 0.5 x 6 = 3.
        FreshScene();
        e = StartFight();
        for (int i = 0; i < BossConfig.HitPoints * 2; i++) e.OnShipAttackHit(.5f);
        e.Step(.05f, 1f);
        Check("HP first: half-weight hits add up (6 x 0.5 = 3 points)", e.State == BossEncounter.Phase.Outro && e.Destroyed);

        // Timer first: two hits, then the clock runs out.
        FreshScene();
        RunScore.EndRun(RunScore.RunId);
        RunScore.BeginRun(true, true);
        e = StartFight();
        e.OnUltimateHit();
        e.OnUltimateHit();
        score = RunScore.Total;
        float fought = 0f;
        for (int i = 0; i < 2000 && e.State == BossEncounter.Phase.Fight; i++) { e.Step(.1f, 1f); fought += .1f; }
        Check("timer first: the fight lasts the full clock (" + fought.ToString("F1") + "s of " + BossConfig.FightSeconds + ")",
              Mathf.Abs(fought - BossConfig.FightSeconds) < .25f);
        Check("timer first: SURVIVED -- one hit point left, it retreats and pays " + ScoreRules.BossSurvived +
              " (" + (RunScore.Total - score) + ")",
              e.State == BossEncounter.Phase.Outro && !e.Destroyed && e.HitPointsLeft == 1 &&
              e.Actor.State == BossActor.Mode.Retreating && RunScore.Total - score == ScoreRules.BossSurvived);

        // Simultaneous: the last point goes in on the frame the clock runs out.
        FreshScene();
        RunScore.EndRun(RunScore.RunId);
        RunScore.BeginRun(true, true);
        e = StartFight();
        remainingField.SetValue(e, .05f);
        for (int i = 0; i < BossConfig.HitPoints; i++) e.OnUltimateHit();
        score = RunScore.Total;
        e.Step(.1f, 1f);
        Check("simultaneous: hit points and clock out on one frame -> DESTROYED (the hit counts)",
              e.State == BossEncounter.Phase.Outro && e.Destroyed && e.Remaining <= 0f &&
              RunScore.Total - score == ScoreRules.BossDestroyed);

        // ... and one point short on that frame: SURVIVED.
        FreshScene();
        e = StartFight();
        remainingField.SetValue(e, .05f);
        for (int i = 0; i < BossConfig.HitPoints - 1; i++) e.OnUltimateHit();
        e.OnShipAttackHit(.99f);
        e.Step(.1f, 1f);
        Check("simultaneous edge: a hair short of the last point when the clock runs out -> SURVIVED",
              e.State == BossEncounter.Phase.Outro && !e.Destroyed && e.HitPointsLeft == 1);

        // Hits after the fight (outro) change nothing.
        e.OnUltimateHit();
        Check("hits during the outro are ignored", e.HitPointsLeft == 1 && !e.Destroyed);

        // Loop scaling unchanged: loop 2 pays x1.5 either way.
        Check("loop scaling as before: 450 / 225 on loop 2",
              ScoreRules.BossPoints(true, 0f, false, 1) == 450 && ScoreRules.BossPoints(false, 0f, false, 1) == 225);
        Check("BossEncounter scores the flat pair (no time bonus)",
              File.ReadAllText("Assets/Scripts/Bosses/BossEncounter.cs").Contains("RunScore.OnBoss(explode, remaining, false,"));
        RunScore.EndRun(RunScore.RunId);
    }

    static void ProjectilePoolIsBounded()
    {
        FreshScene();
        var boss = BossCatalog.ForWorld(1);
        var pool = new BossProjectilePool(4, 2);
        int fired = 0;
        for (int i = 0; i < 10; i++)
            if (pool.Fire(boss, BossShotStyle.Shard, Vector3.zero, Vector2.down) != null) fired++;
        Check("shots stop at the pool size", fired == 4 && pool.Created == 4 && pool.ActiveShots == 4);
        for (int i = 0; i < 5; i++) pool.Lane(boss, 0f, 1f, 2f, .5f, .5f);
        Check("lanes stop at their pool size", pool.BeamsCreated == 2);
        pool.RecycleAll();
        Check("recycled shots are reused, not re-created",
              pool.Fire(boss, BossShotStyle.Bolt, Vector3.zero, Vector2.down) != null && pool.Created == 4);
        pool.Dispose();

        var e = StartFight();
        for (int i = 0; i < 300; i++) e.Step(.1f, 1f);
        Check("a whole fight never exceeds the configured pool (" + (e.Pool != null ? e.Pool.Created : 0) + ")",
              e.Pool == null || (e.Pool.Created <= BossConfig.ProjectilePoolMax && e.Pool.BeamsCreated <= BossConfig.BeamPoolMax));
        Check("every pattern got played", e.Pool == null || e.Actor == null || e.Actor.AttacksStarted >= 6);
    }

    static void CodexEntryIsSecretUntilMet()
    {
        FreshScene();
        PlayerPrefs.SetString(Codex.PrefsKey, "");
        Codex.Reload();
        var entry = Codex.Find(BossCatalog.ForWorld(0).id);
        Check("each boss has a codex entry", entry != null && Codex.Find(BossCatalog.ForWorld(3).id) != null);
        Check("boss lore tells the pilot's story", entry != null && entry.lore.Length > 40);
        int total = Codex.Total;
        Check("hidden before the first encounter", entry != null && !Codex.IsListed(entry) && !Codex.IsDiscovered(entry));
        Check("CodexPanel skips unlisted entries",
              File.ReadAllText("Assets/Scripts/Codex/CodexPanel.cs").Contains("!Codex.IsListed(e)"));

        PlayerPrefs.SetInt(DeveloperUnlocks.EnabledKey, 1);
        Check("developer mode reveals it", Codex.IsListed(entry) && Codex.IsDiscovered(entry) && Codex.DiscoveredCount == Codex.Total);
        PlayerPrefs.SetInt(DeveloperUnlocks.EnabledKey, 0);
        Check("... without recording it", !Codex.IsListed(entry));

        BossEncounter.Begin(0, null);
        BossEncounter.Instance.Step(.1f, 1f);
        Check("meeting the boss reveals it", Codex.IsListed(entry) && Codex.IsDiscovered(entry) && Codex.Total == total + 1);
        Codex.Reload();
        Check("... and it stays revealed", Codex.IsDiscovered(entry));
    }

    static void MusicFallsBackWithoutABossClip()
    {
        FreshScene();
        foreach (var theme in WorldManager.Worlds)
            Check(theme.displayName + " boss track path is WorldMusic/Boss_" + theme.displayName,
                  WorldMusic.BossResource(theme) == "WorldMusic/Boss_" + theme.displayName);
        var worldTrack = AudioClip.Create("world", 4410, 1, 44100, false);
        var bossTrack = AudioClip.Create("boss", 4410, 1, 44100, false);
        Check("a boss clip wins when it exists", WorldMusic.ResolveBossTrack(worldTrack, bossTrack) == bossTrack);
        Check("no boss clip keeps the world track", WorldMusic.ResolveBossTrack(worldTrack, null) == worldTrack);

        var src = new GameObject("MovingMusic").AddComponent<AudioSource>();
        src.clip = worldTrack;
        var space = WorldManager.Worlds[0];
        bool hasClip = WorldMusic.BossClip(space) != null;
        WorldMusic.BeginBoss(space);
        if (!hasClip)
            Check("fallback: same track, intensified", src.clip == worldTrack && src.pitch > 1f && WorldMusic.BossFallbackActive);
        WorldMusic.EndBoss();
        Check("music restored after the boss", src.clip == worldTrack && Mathf.Approximately(src.pitch, 1f));
        Check("the TODO is marked in WorldMusic", File.ReadAllText("Assets/Scripts/Worlds/WorldMusic.cs").Contains("TODO(boss-music)"));
        Check("docs/TODO.md lists boss music", File.Exists("../docs/TODO.md") &&
              File.ReadAllText("../docs/TODO.md").Contains("Resources/WorldMusic/Boss_<World>"));
    }

    static void DevTriggerWorks()
    {
        FreshScene();
        var wm = World(150f);
        Check("no dev trigger outside developer mode", !BossDev.TriggerNow() && !BossEncounter.Running);
        PlayerPrefs.SetInt(DeveloperUnlocks.EnabledKey, 1);
        Check("dev trigger starts the current world's boss now", BossDev.TriggerNow() && BossEncounter.Running &&
              BossEncounter.Instance.Boss == BossCatalog.ForWorld(WorldManager.CurrentIndex));
        Check("... with the level clock run out", wm.SecondsLeftInWorld == 0f);

        FreshScene();
        PlayerPrefs.SetInt(DeveloperUnlocks.EnabledKey, 1);
        World(150f);
        BossDev.SetRush(true);
        Check("boss rush is on", BossDev.RushEnabled);
        var e = BossEncounter.Ensure();
        for (int i = 0; i < 50 && !BossEncounter.Running; i++) e.Step(.1f, 1f);
        Check("boss rush brings the boss a few seconds in", BossEncounter.Running);
        BossDev.SetRush(false);
        PlayerPrefs.SetInt(DeveloperUnlocks.EnabledKey, 0);
        Check("boss rush needs developer mode", !BossDev.RushEnabled);
    }

    static void DeathShowsTheNormalPanel()
    {
        BossEncounter.ResetRun();
        EditorSceneManager.OpenScene("Assets/Scenes/gameS1.unity", OpenSceneMode.Single);
        buttonClicks.playerDied = false;
        score.pauseCounter = 0;
        PlayerPrefs.SetInt(WorldManager.PrefsCurrentWorld, 1);
        var e = StartFight(1);
        e.Step(3f, 1f);
        buttonClicks.playerDied = true;   // collisionDetection's last life
        e.Step(.1f, 1f);
        Check("death aborts the encounter", e.State == BossEncounter.Phase.Aborted && !BossEncounter.Running);
        Check("no scripted freeze or free press is left", !BossEncounter.ScriptedFreeze && !BossEncounter.FreePress);
        var dead = Object.FindFirstObjectByType<playerIsDead>();
        Check("gameS1 has its death handler", dead != null);
        if (dead != null)
        {
            typeof(playerIsDead).GetMethod("Update", Inst).Invoke(dead, null); // just this component
            var panel = Object.FindFirstObjectByType<DeathPanelView>();
            Check("the normal death panel shows", panel != null && panel.Panel != null);
        }
        buttonClicks.playerDied = false;
    }
}
