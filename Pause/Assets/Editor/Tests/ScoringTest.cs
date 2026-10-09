using System.Reflection;
using System.Text.RegularExpressions;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

// The run score (RunScore / ScoreRules): every event pays its configured
// points, the kill chain multiplies and decays, a frozen world earns nothing,
// the score carries across a portal and a boss, the tutorial scores nothing,
// developer runs never save or submit, BestScore persists and cloud-merges as
// a max, every run end banks exactly once, and the HUD row, the death panel
// and the leaderboard registry show it.
public static class ScoringTest
{
    static int fails;
    static void Check(string what, bool ok)
    {
        Debug.Log((ok ? "[SCORE] PASS  " : "[SCORE] FAIL  ") + what);
        if (!ok) fails++;
    }

    public static void Run()
    {
        TestHarness.Exit(Execute());
    }

    const BindingFlags Inst = BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance;

    public static int Execute()
    {
        fails = 0;
        using var sandbox = new TestHarness.Sandbox();
        try
        {
            EventsPayTheirPoints();
            KillChainMultipliesAndDecays();
            FrozenWorldEarnsNothing();
            CarriesAcrossAWorldAndABoss();
            TutorialScoresNothing();
            DeveloperRunNeverSavesOrSubmits();
            BestScorePersistsAndMergesAsMax();
            EveryRunEndBanksOnce();
            HudShowsScoreAndFits();
            DeathPanelFitsEveryAspect();
            LeaderboardRegistry();
            SpeedTiersDustAndMilestones();
            LoopScoreScale();
        }
        finally
        {
            BossEncounter.ResetRun();
            buttonClicks.playerDied = false;
        }
        Debug.Log("[SCORE] failures: " + fails);
        return fails;
    }

    // ---- fixtures -----------------------------------------------------------

    static void RealContext()
    {
        RunScore.EndRun(RunScore.RunId);   // bank whatever an earlier check left open
        PlayerPrefs.SetString("HasDoneTut", "true");
        PlayerPrefs.SetInt(DeveloperUnlocks.EnabledKey, 0);
        PlayerPrefs.DeleteKey(RunScore.BestScoreKey);
        startMenu.youAreInTutorial = false;
        buttonClicks.playerDied = false;
        // Below HUD 20: kills pay their base points (no speed multiplier).
        moveBackGround.speed = 0f;
    }

    static GameObject Enemy(EnemyDef def)
    {
        var go = new GameObject(def.ObjectName);
        go.tag = def.Tag;
        go.AddComponent<EnemyIdentity>().Set(def);
        return go;
    }

    static GameObject Named(string name, string tag)
    {
        var go = new GameObject(name);
        go.tag = tag;
        return go;
    }

    // Kills through the game's one kill hook and returns what it paid.
    static long Kill(GameObject go)
    {
        long before = RunScore.Total;
        collisionDetection.AwardDestroyedTarget(go);
        Object.DestroyImmediate(go);
        return RunScore.Total - before;
    }

    // Lets any chain lapse (on the world's clock) without earning distance.
    static void BreakChain()
    {
        RunScore.Tick(ScoreRules.ComboWindowSeconds + .1f, 0f);
    }

    static score MakeScore()
    {
        var go = new GameObject("~ScoreHud");
        var s = go.AddComponent<score>();
        s.speedValue = new GameObject("s").AddComponent<Text>();
        s.pauseCounterText = new GameObject("p").AddComponent<Text>();
        return s;
    }

    static void Call(Object o, string method)
    {
        o.GetType().GetMethod(method, Inst).Invoke(o, null);
    }

    // ---- 1. every event pays its configured points ------------------------------

    static void EventsPayTheirPoints()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        RealContext();
        RunScore.BeginRun(true, true);
        Check("a new run starts at 0", RunScore.Total == 0);

        foreach (EnemyRole role in System.Enum.GetValues(typeof(EnemyRole)))
        {
            BreakChain();
            var def = EnemyRoster.One(0, role);
            if (def == null) { Check(role + ": roster has one", false); continue; }
            long paid = Kill(Enemy(def));
            Check(role + " kill pays " + ScoreRules.KillPoints(role, def.tier) + " (got " + paid + ")",
                  paid == ScoreRules.KillPoints(role, def.tier));
        }
        for (int tier = 1; tier <= 4; tier++)
        {
            BreakChain();
            var def = EnemyRoster.Fighter(0, tier);
            long paid = def != null ? Kill(Enemy(def)) : -1;
            Check("tier-" + tier + " fighter pays 5 x tier (" + paid + ")", paid == ScoreRules.FighterPerTier * tier);
        }
        Check("defaults are small: rock 5, fighter 5 x tier, alien 15, chaser 20, heavy 40, mine 10",
              ScoreRules.Rock == 5 && ScoreRules.FighterPerTier == 5 && ScoreRules.Alien == 15 &&
              ScoreRules.Chaser == 20 && ScoreRules.Heavy == 40 && ScoreRules.Mine == 10);

        // Legacy prefabs with no roster identity.
        BreakChain();
        Check("legacy asteroid (tag Astr) pays a rock", Kill(Named("aestroid_1", "Astr")) == ScoreRules.Rock);
        BreakChain();
        Check("legacy alien1 pays an alien", Kill(Named("alien1", "Enimey")) == ScoreRules.Alien);
        BreakChain();
        Check("legacy mine pays a mine", Kill(Named("mine", "Enimey")) == ScoreRules.Mine);

        // With the alien1.prefab fallback gone, every alien is a roster
        // alien: built exactly as the spawner builds it, in every world, it
        // pays an alien and keeps collisionDetection's "alien1" achievement key.
        for (int world = 0; world < EnemyRoster.WorldKeys.Length; world++)
        {
            BreakChain();
            var def = EnemyRoster.One(world, EnemyRole.Alien);
            var go = def != null ? EnemyFactory.Create(def, Vector3.zero, Quaternion.identity) : null;
            if (go != null) go.name += "(Clone)";
            bool key = go != null && PrefabName.Is(go, "alien1");
            long paid = go != null ? Kill(go) : -1;
            Check(EnemyRoster.WorldKeys[world] + " roster alien pays an alien (" + paid + ") and counts for the alien achievement",
                  paid == ScoreRules.Alien && key);
        }

        // Boss parts: no farming the respawning body hitbox.
        BreakChain();
        Check("the boss body hitbox pays nothing", Kill(Named("BossBody", "Enimey")) == 0);
        Check("a boss laser pays nothing", Kill(Named("BossBeamHit", "Enimey")) == 0);
        Check("a shot-down boss projectile pays a little", Kill(Named("BossShotHit", "Enimey")) == ScoreRules.BossShot);
        Check("... and never starts a chain", RunScore.Chain == 0);
        Check("non-hazards are not kills", Kill(Named("smStar1", "pickUp")) == 0);

        long t = RunScore.Total;
        RunScore.OnDust(false);
        Check("small star dust pays 2", RunScore.Total - t == 2);
        t = RunScore.Total;
        RunScore.OnDust(true);
        Check("large star dust pays 5", RunScore.Total - t == 5);
        foreach (RunScore.Atom a in System.Enum.GetValues(typeof(RunScore.Atom)))
        {
            t = RunScore.Total;
            RunScore.OnAtom(a);
            Check(a + " atom pays 10", RunScore.Total - t == 10);
        }

        // Teleports pay for actual repositioning, capped per world.
        t = RunScore.Total;
        RunScore.OnTeleport(Vector3.zero, new Vector3(.5f, 0f));
        Check("a blink in place (< min distance) pays nothing", RunScore.Total == t);
        RunScore.OnTeleport(Vector3.zero, new Vector3(1f, 0f));
        Check("a 1-unit blink pays about half (2 of 3)", RunScore.Total - t == 2);
        t = RunScore.Total;
        RunScore.OnTeleport(Vector3.zero, new Vector3(-2.4f, 2f));
        Check("a full-width blink pays the full 3", RunScore.Total - t == 3);
        for (int i = 0; i < 40; i++) RunScore.OnTeleport(Vector3.zero, new Vector3(2.4f, 0f));
        Check("teleports stop paying after " + ScoreRules.TeleportsScoredPerWorld + " in a world",
              RunScore.Parts.teleportCount == ScoreRules.TeleportsScoredPerWorld);
        t = RunScore.Total;
        RunScore.OnWorldCleared(0);
        Check("clearing Space pays 50", RunScore.Total - t == 50);
        t = RunScore.Total;
        RunScore.OnTeleport(Vector3.zero, new Vector3(2.4f, 0f));
        Check("... and a new world pays teleports again", RunScore.Total - t == 3);
        t = RunScore.Total;
        RunScore.OnWorldCleared(2);
        Check("clearing Verdant (world 3) pays 150", RunScore.Total - t == 150);

        t = RunScore.Total;
        RunScore.OnBoss(true, 0f, false, Vector3.zero);
        Check("a destroyed boss pays 300", RunScore.Total - t == 300);
        t = RunScore.Total;
        RunScore.OnBoss(false, 0f, false, Vector3.zero);
        Check("a survived (retreating) boss pays 150", RunScore.Total - t == 150);
        t = RunScore.Total;
        RunScore.OnBoss(true, 10f, true, Vector3.zero);
        Check("HitPoints rule: 10s left adds 50", RunScore.Total - t == 350);

        t = RunScore.Total;
        RunScore.Tick(10f, .35f);
        // (2026-10, second pass: speed is capped at HUD 35 -- SpeedRamp.Cap --
        // and the cap itself is the x2 tier; tiers 20 / 30 / 35)
        Check("ten seconds at HUD speed 35 (the cap) pay 17.5 distance points (speed / 20 a second) x2 speed tier = 35",
              RunScore.Total - t == 35 || RunScore.Total - t == 36);
        t = RunScore.Total;
        RunScore.Tick(10f, .46f);
        // above the cap only a boost gets there: the limit break, x2.5
        Check("ten seconds at HUD 46 (a limit break) pay 23 distance points x2.5 = 57",
              RunScore.Total - t == 57 || RunScore.Total - t == 58);
        t = RunScore.Total;
        RunScore.Tick(10f, .19f);
        Check("below HUD 20 distance is unmultiplied (10s at 19 = 9)", RunScore.Total - t == 9 || RunScore.Total - t == 10);
        t = RunScore.Total;
        for (int i = 0; i < 600; i++) RunScore.Tick(1f / 60f, .2f);
        long d = RunScore.Total - t;
        Check("distance is framerate independent (600 x 1/60s at speed 20 = 10 x1.25 = 12.5, got " + d + ")", d == 12 || d == 13);

        var parts = RunScore.Parts;
        Check("the breakdown sums to the total", parts.Total == RunScore.Total);
        Check("source hooks: pickups and kills feed RunScore",
              Regex.Matches(System.IO.File.ReadAllText("Assets/Scripts/Ship/collisionDetection.cs"), @"RunScore\.On(Dust|Atom|Kill)\(").Count == 7);   // + the violet capacitor atom
        Check("source hooks: teleport, world, boss",
              System.IO.File.ReadAllText("Assets/Scripts/Ship/movePlayer.cs").Contains("RunScore.OnTeleport(before, transform.position)") &&
              System.IO.File.ReadAllText("Assets/Scripts/Worlds/WorldManager.cs").Contains("RunScore.OnWorldCleared(CurrentIndex)") &&
              System.IO.File.ReadAllText("Assets/Scripts/Bosses/BossEncounter.cs").Contains("RunScore.OnBoss(explode"));
    }

    // ---- 2. kill chain ------------------------------------------------------------

    static void KillChainMultipliesAndDecays()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        RealContext();
        RunScore.BeginRun(true, true);
        var rock = EnemyRoster.One(0, EnemyRole.Rock);

        Check("multiplier table: 1,1,2,2,2,3,3,3,3,4 for chains 1..10",
              ScoreRules.MultiplierFor(1) == 1 && ScoreRules.MultiplierFor(2) == 1 && ScoreRules.MultiplierFor(3) == 2 &&
              ScoreRules.MultiplierFor(5) == 2 && ScoreRules.MultiplierFor(6) == 3 && ScoreRules.MultiplierFor(9) == 3 &&
              ScoreRules.MultiplierFor(10) == 4 && ScoreRules.MultiplierFor(40) == 4);

        long a = Kill(Enemy(rock));
        RunScore.Tick(.5f, 0f);
        long b = Kill(Enemy(rock));
        RunScore.Tick(.5f, 0f);
        long c = Kill(Enemy(rock));
        Check("chain: kills 1-2 at x1, the 3rd at x2 (" + a + ", " + b + ", " + c + ")",
              a == 5 && b == 5 && c == 10 && RunScore.Multiplier == 2);
        for (int i = 0; i < 7; i++) Kill(Enemy(rock));
        Check("ten chained kills reach x4", RunScore.Multiplier == 4 && RunScore.Chain == 10);
        Check("x4 is the cap", Kill(Enemy(rock)) == 20);

        RunScore.Tick(0f, 0f);
        Check("a frozen frame never runs the chain down", RunScore.Multiplier == 4);
        RunScore.Tick(ScoreRules.ComboWindowSeconds * .5f, 0f);
        Check("still alive inside the window", RunScore.Multiplier == 4 && RunScore.ChainLeft01 > 0f);
        RunScore.Tick(ScoreRules.ComboWindowSeconds * .6f, 0f);
        Check("decays to x1 after ~2s without a kill", RunScore.Multiplier == 1 && RunScore.Chain == 0);
        Check("the next kill starts over at x1", Kill(Enemy(rock)) == 5);
        Check("the best chain is recorded", RunScore.Parts.bestChain == 11);
        Check("dust and atoms are never multiplied", RunScore.OnDust(true) == ScoreRules.LargeDust);

        bool was = ScoreRules.ComboEnabled;
        ScoreRules.ComboEnabled = false;
        try
        {
            BreakChain();
            long sum = 0;
            for (int i = 0; i < 12; i++) sum += Kill(Enemy(rock));
            Check("with the chain switched off every kill is x1", sum == 12 * 5 && RunScore.Multiplier == 1);
        }
        finally { ScoreRules.ComboEnabled = was; }
    }

    // ---- 3. frozen world ----------------------------------------------------------

    static void FrozenWorldEarnsNothing()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        RealContext();
        RunScore.BeginRun(true, true);
        long t = RunScore.Total;
        for (int i = 0; i < 100; i++) RunScore.Tick(0f, .46f);   // timeScale 0: scaled dt is 0
        Check("no distance points while frozen", RunScore.Total == t);

        string src = System.IO.File.ReadAllText("Assets/Scripts/Core/score.cs");
        Check("score.cs ticks the score on the world's scaled clock, only on running frames",
              Regex.Matches(src, @"RunScore\.Tick\(Time\.deltaTime, moveBackGround\.speed\)").Count == 2 &&
              !src.Contains("RunScore.Tick(Time.unscaledDeltaTime"));
        Check("the boss intro freezes the world (timeScale 0), so it earns nothing",
              System.IO.File.ReadAllText("Assets/Scripts/Gameplay/moveBackGround.cs").Contains("BossEncounter.ScriptedFreeze"));
    }

    // ---- 4. carried across a world and a boss ---------------------------------------

    static void CarriesAcrossAWorldAndABoss()
    {
        BossEncounter.ResetRun();
        EditorSceneManager.OpenScene("Assets/Scenes/gameS1.unity", OpenSceneMode.Single);
        RealContext();
        PlayerPrefs.SetInt(WorldManager.PrefsCurrentWorld, 0);
        score.pauseCounter = 0;
        moveBackGround.speed = .3f;
        Time.timeScale = 1f;
        RunScore.BeginRun(true, true);
        RunScore.Tick(10f, .3f);
        RunScore.OnDust(true);
        long beforePortal = RunScore.Total;

        var wm = new GameObject("~WorldManager").AddComponent<WorldManager>();
        wm.SendMessage("Awake");
        try { wm.Advance(); }
        catch (System.Exception e) { Debug.LogWarning("[SCORE] Advance threw (presentation only): " + e.Message); }
        Check("flying through the portal adds the world bonus and keeps the score (" + beforePortal + " -> " + RunScore.Total + ")",
              RunScore.Total == beforePortal + ScoreRules.WorldClearedPoints(0) && WorldManager.CurrentIndex == 1);

        long beforeBoss = RunScore.Total;
        BossEncounter.Begin(1, null);
        var e2 = BossEncounter.Instance;
        e2.Step(.1f, 1f);
        for (int i = 0; i < 400 && e2.State == BossEncounter.Phase.Intro; i++) e2.Step(.1f, 1f);
        long duringIntro = RunScore.Total;
        // Destroyed: its hit points run out before the clock (BossEndRule).
        for (int i = 0; i < BossConfig.HitPoints; i++) e2.OnUltimateHit();
        for (int i = 0; i < 2000 && e2.State == BossEncounter.Phase.Fight; i++) e2.Step(.1f, 1f);
        Check("a destroyed boss adds 300 on top (" + beforeBoss + " -> " + RunScore.Total + ")",
              duringIntro == beforeBoss && RunScore.Total == beforeBoss + ScoreRules.BossDestroyed);
        RunScore.Tick(1f, .2f);
        Check("and the score keeps climbing in the next world", RunScore.Total > beforeBoss + ScoreRules.BossDestroyed);
        BossEncounter.ResetRun();
        Object.DestroyImmediate(wm.gameObject);
    }

    // ---- 5. tutorial ------------------------------------------------------------------

    static void TutorialScoresNothing()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/" + score.TutorialScene + ".unity", OpenSceneMode.Single);
        RealContext();
        var s = MakeScore();
        Call(s, "Awake");
        Call(s, "Start");
        Check("tutorial scene: the run does not score", !RunScore.Scoring);
        RunScore.OnDust(true);
        RunScore.OnAtom(RunScore.Atom.Heal);
        RunScore.OnWorldCleared(0);
        RunScore.Tick(5f, .4f);
        Kill(Enemy(EnemyRoster.One(0, EnemyRole.Alien)));
        Check("tutorial: nothing adds to the score", RunScore.Total == 0);
        Call(s, "OnDestroy");
        Check("tutorial: BestScore is never written", !PlayerPrefs.HasKey(RunScore.BestScoreKey));
        Object.DestroyImmediate(s.gameObject);

        // A run before the tutorial was ever completed is practice too.
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        PlayerPrefs.DeleteKey("HasDoneTut");
        s = MakeScore();
        Call(s, "Awake");
        Check("practice run (tutorial not done): no score", !RunScore.Scoring);
        Object.DestroyImmediate(s.gameObject);

        // The tutorial's read-out has no SCORE row.
        EditorSceneManager.OpenScene("Assets/Scenes/" + score.TutorialScene + ".unity", OpenSceneMode.Single);
        var go = new GameObject("~HudTutTest");
        var styler = go.AddComponent<HudStyler>();
        styler.SendMessage("Start");
        Check("tutorial HUD: no SCORE row", SceneUtil.FindAny(ScoreHud.RowName) == null && go.GetComponent<ScoreHud>() == null);
        Check("tutorial complete panel has no score", !System.IO.File.ReadAllText("Assets/Scripts/Tutorial/TutorialCompletePanel.cs").Contains("RunScore"));
        Object.DestroyImmediate(go);
    }

    // ---- 6. developer mode -------------------------------------------------------------

    static void DeveloperRunNeverSavesOrSubmits()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        RealContext();
        PlayerPrefs.SetInt(RunScore.BestScoreKey, 100);
        PlayerPrefs.SetInt(DeveloperUnlocks.EnabledKey, 1);
        var s = MakeScore();
        Call(s, "Awake");
        Call(s, "Start");
        Check("developer run: scores on the HUD", RunScore.Scoring);
        Check("developer run: may not save a best", !RunScore.SavesBest);
        RunScore.OnBoss(true, 0f, false, Vector3.zero);
        Check("developer run: never a NEW BEST", !RunScore.IsNewBest);
        RunScore.Stage();
        buttonClicks.playerDied = true;
        Call(s, "Update");
        Call(s, "OnDestroy");
        Check("developer run: BestScore untouched (100, though it scored 300)", PlayerPrefs.GetInt(RunScore.BestScoreKey) == 100);

        var fake = new FakeLeaderboards();
        var service = new LeaderboardService(fake, () => 0f) { Ios = false };
        var prev = LeaderboardService.Instance;
        LeaderboardService.Instance = service;
        using (EnableTopScoreForTest())
        {
            Check("developer run: submission blocked", LeaderboardService.SubmissionBlocked());
            Check("developer run: SubmitRun queues nothing",
                  service.SubmitRun(new LeaderboardRunStats { score = 99999, worldIndex = 3 }) == 0 && fake.Submissions.Count == 0);
        }
        LeaderboardService.Instance = prev;
        Object.DestroyImmediate(s.gameObject);
        buttonClicks.playerDied = false;
        PlayerPrefs.SetInt(DeveloperUnlocks.EnabledKey, 0);
    }

    // ---- 7. BestScore persistence and cloud merge -------------------------------------------

    static void BestScorePersistsAndMergesAsMax()
    {
        RealContext();
        int run = RunScore.BeginRun(true, true);
        RunScore.OnBoss(true, 0f, false, Vector3.zero);
        Check("a better run is a new best", RunScore.IsNewBest);
        RunScore.EndRun(run);
        Check("BestScore saved at run end (300)", PlayerPrefs.GetInt(RunScore.BestScoreKey) == 300);
        run = RunScore.BeginRun(true, true);
        Check("the next run knows the best to beat", RunScore.BestAtStart == 300);
        RunScore.OnDust(false);
        RunScore.EndRun(run);
        Check("a worse run never lowers it", PlayerPrefs.GetInt(RunScore.BestScoreKey) == 300);
        // (HighestSpeed is a legacy cloud-save field now: the game no longer
        // reads or writes it, it only round-trips for older builds.)
        Check("BestScore is its own key, apart from the legacy HighestSpeed field",
              ProgressSnapshot.HighestSpeedKey == "HighestSpeed" && RunScore.BestScoreKey == "BestScore");

        var snap = ProgressSnapshot.Capture(1);
        Check("cloud snapshot captures BestScore", snap.bestScore == 300);
        PlayerPrefs.DeleteKey(RunScore.BestScoreKey);
        ProgressSnapshot parsed;
        Check("snapshot JSON round-trips", ProgressSnapshot.TryParse(snap.ToJson(), out parsed) == ProgressSnapshot.ParseResult.Ok);
        parsed.Apply();
        Check("applying the snapshot restores BestScore", PlayerPrefs.GetInt(RunScore.BestScoreKey) == 300);
        ProgressSnapshot old;
        ProgressSnapshot.TryParse("{\"schemaVersion\":1,\"savedAtUtc\":5,\"currency\":2}", out old);
        Check("an older save without BestScore reads as 0", old != null && old.bestScore == 0);

        var local = new ProgressSnapshot { savedAtUtc = 10, bestScore = 12000 };
        var cloud = new ProgressSnapshot { savedAtUtc = 20, bestScore = 8000 };
        Check("merge: BestScore is the max (local higher, cloud newer)", ProgressMerge.Merge(local, cloud).bestScore == 12000);
        Check("merge: BestScore is the max (cloud higher)",
              ProgressMerge.Merge(new ProgressSnapshot { savedAtUtc = 30, bestScore = 1 }, cloud).bestScore == 8000);
        Check("the test sandbox restores BestScore", Regex.IsMatch(
            System.IO.File.ReadAllText("Assets/Editor/Tests/TestHarness.cs"), @"RunScore\.BestScoreKey"));
    }

    // ---- 8. every run end banks exactly once ----------------------------------------------

    static void EveryRunEndBanksOnce()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        RealContext();

        // Death: score.Update banks; the death screen and the scene teardown don't again.
        var s = MakeScore();
        Call(s, "Awake");
        Call(s, "Start");
        int banks = RunScore.BankCount;
        RunScore.OnBoss(false, 0f, false, Vector3.zero);
        buttonClicks.playerDied = true;
        Call(s, "Update");
        Check("death banks the run", RunScore.BankCount == banks + 1 && PlayerPrefs.GetInt(RunScore.BestScoreKey) == 150);
        RunScore.OnDust(true);
        Check("nothing scores after death", RunScore.Total == 150);
        Call(s, "Update");
        Call(s, "OnDestroy");
        Check("death + teardown bank it once", RunScore.BankCount == banks + 1);
        Object.DestroyImmediate(s.gameObject);
        buttonClicks.playerDied = false;

        // Menu / Back: the scene's teardown banks it.
        PlayerPrefs.DeleteKey(RunScore.BestScoreKey);
        s = MakeScore();
        Call(s, "Awake");
        banks = RunScore.BankCount;
        RunScore.OnDust(true);
        Call(s, "OnDestroy");
        Call(s, "OnDestroy");
        Check("leaving mid-run (Menu/Back) banks once", RunScore.BankCount == banks + 1 && PlayerPrefs.GetInt(RunScore.BestScoreKey) == 5);
        Object.DestroyImmediate(s.gameObject);

        // Replay: the next scene's Awake can beat the old OnDestroy.
        PlayerPrefs.DeleteKey(RunScore.BestScoreKey);
        var old = MakeScore();
        Call(old, "Awake");
        banks = RunScore.BankCount;
        RunScore.OnAtom(RunScore.Atom.Heal);
        var next = MakeScore();
        Call(next, "Awake");
        Check("Replay: the new run banks the old one first", RunScore.BankCount == banks + 1 &&
              PlayerPrefs.GetInt(RunScore.BestScoreKey) == 10 && RunScore.Total == 0 && RunScore.Scoring);
        Call(old, "OnDestroy");
        Check("Replay: the old scene's late teardown neither banks again nor ends the new run",
              RunScore.BankCount == banks + 1 && !RunScore.Ended);
        Call(next, "OnDestroy");
        Check("Replay: the new run banks on its own end", RunScore.BankCount == banks + 2);
        Object.DestroyImmediate(old.gameObject);
        Object.DestroyImmediate(next.gameObject);

        // Backgrounding: the best so far is written, the run stays open.
        PlayerPrefs.DeleteKey(RunScore.BestScoreKey);
        int run = RunScore.BeginRun(true, true);
        banks = RunScore.BankCount;
        RunScore.OnDust(true);
        RunScore.Stage();
        Check("backgrounding writes the best so far", PlayerPrefs.GetInt(RunScore.BestScoreKey) == 5 && !RunScore.Ended);
        RunScore.OnDust(false);
        RunScore.Stage();
        RunScore.EndRun(run);
        RunScore.EndRun(run);
        Check("resume + end: banked once, best is the final total (7)",
              RunScore.BankCount == banks + 1 && PlayerPrefs.GetInt(RunScore.BestScoreKey) == 7);
        Check("PrefsSaverRunner stages the score with the dust",
              Regex.Matches(System.IO.File.ReadAllText("Assets/Scripts/Core/PrefsSaverRunner.cs"), @"RunScore\.Stage\(\)").Count == 2);
    }

    // ---- 9. HUD -------------------------------------------------------------------------------

    static readonly (string name, Vector2 size, Rect safe)[] Screens =
    {
        ("9:16 1080x1920",          new Vector2(1080, 1920), new Rect(0, 0, 1080, 1920)),
        ("9:19.5 1170x2532 notch",  new Vector2(1170, 2532), new Rect(0, 102, 1170, 2532 - 102 - 141)),
        ("9:20 1080x2400 cutout",   new Vector2(1080, 2400), new Rect(0, 0, 1080, 2400 - 118)),
        ("9:22 1080x2640",          new Vector2(1080, 2640), new Rect(0, 0, 1080, 2640 - 96)),
        ("9:24 1080x2880",          new Vector2(1080, 2880), new Rect(0, 0, 1080, 2880 - 120)),
        ("iPad 1536x2048",          new Vector2(1536, 2048), new Rect(0, 0, 1536, 2048)),
    };

    static void HudShowsScoreAndFits()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/gameS1.unity", OpenSceneMode.Single);
        RealContext();
        RunScore.BeginRun(true, true);
        var go = new GameObject("~HudScoreTest");
        var styler = go.AddComponent<HudStyler>();
        styler.SendMessage("Start");
        var hud = go.GetComponent<ScoreHud>();
        Check("gameS1 HUD gets a ScoreHud", hud != null && hud.ScoreText != null);
        if (hud == null) { Object.DestroyImmediate(go); return; }
        styler.SendMessage("Update");
        hud.SendMessage("Update");
        var rows = hud.ScoreText.transform.parent;
        Check("SCORE is the top line of the read-out", rows.GetChild(0) == hud.ScoreText.transform);
        Check("it reads SCORE  0 at the start ('" + hud.ScoreText.text + "')", hud.ScoreText.text == "SCORE  0");

        RunScore.OnBoss(true, 0f, false, Vector3.zero);
        RunScore.OnDust(true);
        for (int i = 0; i < 400; i++) Step(hud, .05f);
        Check("it ticks up to the exact total ('" + hud.ScoreText.text + "')", hud.ScoreText.text == "SCORE  305");

        // Star dust is still earned in the run but only shown when it ends:
        // the read-out is SCORE / SPEED / PAUSES with no dust figure.
        System.Func<float> runDust = () => score.paysRealDust ? score.totalCurrency : score.tutorialCurrency;
        float dustBefore = runDust();
        score.AwardStarDust(2f);
        styler.SendMessage("Update");
        Check("a dust pickup still pays the run's dust", Mathf.Approximately(runDust(), dustBefore + 2f));
        Check("... but the in-run HUD shows no star dust", !CloakShieldTest.HudShowsDust(styler.HudRoot));
        var dustPop = hud.ShowPopup(ScoreRules.SmallDust, new Vector3(1f, 2f, 0f), RunScore.Source.Dust);
        Check("a dust pickup still pops its score ('" + (dustPop != null ? dustPop.text : "null") + "')",
              dustPop != null && dustPop.gameObject.activeSelf && dustPop.text == "+" + ScoreRules.SmallDust);

        var rock = EnemyRoster.One(0, EnemyRole.Rock);
        for (int i = 0; i < 3; i++) Kill(Enemy(rock));
        hud.SendMessage("Update");
        Check("the chain badge shows x2 on a 3-kill chain ('" + hud.ChainText.text + "')", hud.ChainText.text == "x2");
        BreakChain();
        hud.SendMessage("Update");
        Check("... and clears when the chain lapses", hud.ChainText.text == "");

        var pop = hud.ShowPopup(20, new Vector3(1f, 2f, 0f), RunScore.Source.Kill);
        Check("a +N popup appears at the source", pop != null && pop.gameObject.activeSelf && pop.text == "+20");
        var bossPop = hud.ShowPopup(300, new Vector3(float.NaN, float.NaN, 0f), RunScore.Source.Boss);
        Check("boss popups are bigger and say BOSS", bossPop != null && bossPop.fontSize > pop.fontSize && bossPop.text == "+300  BOSS");
        var colours = new System.Collections.Generic.HashSet<Color>();
        bool longer = true;
        foreach (RunScore.Source src in new[] { RunScore.Source.Kill, RunScore.Source.Dust, RunScore.Source.Atom,
                                                RunScore.Source.Boss, RunScore.Source.World })
        {
            colours.Add(ScoreHud.StyleFor(src, false).colour);
            if (src == RunScore.Source.Boss || src == RunScore.Source.World)
                longer &= ScoreHud.StyleFor(src, false).seconds > ScoreHud.StyleFor(RunScore.Source.Kill, false).seconds &&
                          ScoreHud.StyleFor(src, false).size > ScoreHud.StyleFor(RunScore.Source.Kill, true).size;
        }
        Check("each event kind (kill, dust, atom, boss, world) has its own popup colour", colours.Count == 5);
        Check("boss and world popups are bigger and stay up longer", longer);
        Check("a chained kill pops in Kaneda red", ScoreHud.StyleFor(RunScore.Source.Kill, true).colour == AkiraPalette.RedHi);

        // Frozen world: popups hold still; running, they pop, rise and go.
        int live = hud.LivePopups;
        Vector2 at0 = pop.rectTransform.anchoredPosition;
        hud.StepPopups(.2f);
        Vector2 at1 = pop.rectTransform.anchoredPosition;
        for (int i = 0; i < 30; i++) hud.StepPopups(0f);
        Check("popups freeze at timeScale 0", pop.rectTransform.anchoredPosition == at1 && hud.LivePopups == live);
        Check("... and rise while the world runs", at1.y > at0.y);
        hud.StepPopups(1f);
        Check("a kill popup is gone after its short life, the boss popup still up",
              !pop.gameObject.activeSelf && bossPop.gameObject.activeSelf);
        hud.StepPopups(1f);
        Check("the boss popup goes too", !bossPop.gameObject.activeSelf && hud.LivePopups == 0);
        int pooled = 0;
        for (int i = 0; i < 40; i++) hud.ShowPopup(5, Vector3.zero, RunScore.Source.Dust);
        foreach (Transform t in hud.ScoreText.canvas.rootCanvas.transform) if (t.name == "ScorePopup") pooled++;
        Check("popups are pooled (" + pooled + " objects for 40 popups)", pooled == ScoreHud.PopupPool);
        hud.StepPopups(-1f);
        Check("the run ending clears them", hud.LivePopups == 0);
        Check("pickups report where they were caught (dust / atom popups)",
              Regex.Matches(System.IO.File.ReadAllText("Assets/Scripts/Ship/collisionDetection.cs"),
                            @"RunScore\.On(Dust|Atom)\([^;]*hit\.transform\.position\)").Count == 6);   // + the violet capacitor atom

        // Every row still fits; the score row is as wide as it can get.
        hud.ScoreText.text = ScoreHud.Label(9999999);
        hud.ChainText.text = "x4";
        var rowsRt = (RectTransform)rows;
        LayoutRebuilder.ForceRebuildLayoutImmediate(rowsRt);
        bool fits = true;
        for (int i = 0; i < rowsRt.childCount; i++)
        {
            var row = (RectTransform)rowsRt.GetChild(i);
            var text = row.GetComponent<Text>();
            if (row.rect.height + .5f < text.preferredHeight) fits = false;
        }
        Check("every read-out row fits its text", fits);
        float scoreWidth = hud.ScoreText.preferredWidth + 8f + hud.ChainText.preferredWidth;
        Check("SCORE 9,999,999 and x4 fit across the row (" + scoreWidth.ToString("F0") + " <= " + rowsRt.rect.width + ")",
              scoreWidth <= rowsRt.rect.width);

        var root = styler.HudRoot;
        var canvas = root.parent.GetComponent<Canvas>();
        var scaler = canvas.GetComponent<CanvasScaler>();
        Vector2 hudSize = root.rect.size;
        Check("the panel stays compact (SCORE / SPEED / PAUSES: " + hudSize.y + ")", Mathf.Approximately(hudSize.y, 131f));
        foreach (var s in Screens)
        {
            float scale = HudStyler.HudCanvasScale(canvas, scaler, s.size);
            Rect r = HudStyler.HudScreenRect(s.safe, s.size, scale, hudSize);
            Rect actions = PauseQuickActions.ScreenRectFor(s.safe, s.size);
            Check(s.name + ": HUD inside the safe area, clear of the quick actions, in the top quarter",
                  s.safe.Contains(r.min) && s.safe.Contains(r.max) && !r.Overlaps(actions) && r.yMin > s.size.y * .75f);
        }
        Object.DestroyImmediate(go);
    }

    static void Step(ScoreHud hud, float dt)
    {
        // Edit mode has no unscaled clock: drive the tick-up directly.
        typeof(ScoreHud).GetMethod("TickDisplay", Inst).Invoke(hud, new object[] { dt });
    }

    // ---- 10. death panel ----------------------------------------------------------------------

    static void DeathPanelFitsEveryAspect()
    {
        // Canvas units (PopUpCanvas is 800 wide, match width): full-screen
        // aspects from 9:16 to 9:24, and an iPad.
        foreach (var (name, h) in new[] { ("9:16", 1422f), ("9:19.5", 1733f), ("9:20", 1778f), ("9:22", 1956f), ("9:24", 2133f), ("iPad 3:4", 1066f) })
        {
            var safe = new Rect(-400f, -h * .5f, 800f, h);
            // The top band: quick actions + the HUD (131 tall, no star dust
            // row) under a notch-sized inset.
            var band = new Rect(-400f, h * .5f - 60f - 131f - 16f, 800f, 131f + 16f);
            DeathPanelView.ComputeFit(safe, band, out var centre, out var scale);
            float w = (DeathPanelView.Width + 2f * DeathPanelView.GlowMargin) * scale;
            float ph = (DeathPanelView.Height + 2f * DeathPanelView.GlowMargin) * scale;
            var visual = new Rect(centre.x - w * .5f, centre.y - ph * .5f, w, ph);
            Check(name + ": death panel fits the screen and clears the HUD band (scale " + scale.ToString("F2") + ")",
                  safe.Contains(visual.min) && safe.Contains(visual.max) && !visual.Overlaps(band) && scale >= .5f);
            if (h >= 1400f) Check(name + ": phones keep the panel at full size", Mathf.Approximately(scale, 1f));
        }

        // Built from the real scene: shows the score, NEW BEST, the breakdown.
        EditorSceneManager.OpenScene("Assets/Scenes/gameS1.unity", OpenSceneMode.Single);
        var canvas = SceneUtil.FindAny("PopUpCanvas");
        var best = SceneUtil.FindAny("playerDeadHighestSpeed").GetComponent<Text>();
        var run = SceneUtil.FindAny("deathSpeedReachedThisRoundText").GetComponent<Text>();
        var dust = SceneUtil.FindAny("playerDeadHighScore").GetComponent<Text>();
        var parts = new RunScore.Breakdown { distance = 1274, kills = 1375, killCount = 131, dust = 100, dustCount = 38, worlds = 1050, worldCount = 3 };
        var r = new DeathPanelView.Results
        {
            score = parts.Total, bestScore = 3799, newBest = false, ranked = true, parts = parts,
            dustAtStart = 10f, dustWon = 2f,
        };
        var view = DeathPanelView.Build(canvas.transform, best, run, dust,
            SceneUtil.FindAny("Replay").GetComponent<Button>(), SceneUtil.FindAny("MainMenu").GetComponent<Button>(), r);
        view.ApplyAt(.3f);
        long early = long.Parse(best.text.Replace(",", ""));
        var lastRow = view.Panel.Find("Card1/Row6/Points").GetComponent<Text>();
        var firstRow = view.Panel.Find("Card1/Row0/Points").GetComponent<Text>();
        Check("mid-intro the score is still counting up", early < parts.Total);
        view.ApplyAt(.5f);
        Check("rows count up staggered (first row ahead of the last)",
              long.Parse(firstRow.text.TrimStart('+').Replace(",", "")) > 0 && lastRow.text == "+0");
        view.Skip();
        Check("settled: SCORE 3,799", best.text == "3,799");
        Check("not a record: the sub-label shows the best to beat",
              view.Panel.Find("Card0/Sub") != null && view.Panel.Find("Card0/Sub").GetComponent<Text>().text == "BEST  3,799" &&
              view.Panel.Find("Card0/NewBest") == null);
        Check("the last row settles on its points", lastRow.text == "+1,050");
        // Speed is capped: it is not a result, so the panel shows none.
        bool noSpeed = true;
        foreach (var t in view.Panel.GetComponentsInChildren<Text>(false))
            noSpeed &= !t.text.ToUpperInvariant().Contains("SPEED");
        Check("no speed line anywhere on the panel (the scene's old speed Text is switched off)",
              noSpeed && !run.gameObject.activeSelf);
        Check("a source with no points shows 0", view.Panel.Find("Card1/Row5/Points").GetComponent<Text>().text == "0");
        Check("kills row: count and points", view.Panel.Find("Card1/Row1/Count").GetComponent<Text>().text == "131" &&
              view.Panel.Find("Card1/Row1/Points").GetComponent<Text>().text == "+1,375");
        Check("the Flight Complete star dust card keeps earned / total", dust.text == "+2.00" &&
              dust.transform.parent.Find("Total").GetComponent<Text>().text == "TOTAL  12.00");

        r.ranked = false;
        Check("developer run: the sub-label says it is not saved", DeathPanelView.ScoreSubLabel(r) == "DEV RUN - NOT SAVED");
        r.practice = true;
        Check("practice run: labelled practice", DeathPanelView.ScoreSubLabel(r) == "PRACTICE RUN");
    }

    // ---- 11. leaderboard registry ------------------------------------------------------------

    // Fills Top Score's empty Play Console id for the scope, as pasting the
    // real id would.
    sealed class TopScoreEnabled : System.IDisposable
    {
        readonly FieldInfo field = typeof(LeaderboardBoard).GetField("androidId");
        readonly LeaderboardBoard board = LeaderboardBoards.Get(LeaderboardBoards.TopScore);
        readonly string was;
        public TopScoreEnabled() { was = (string)field.GetValue(board); field.SetValue(board, "CgkI_test_top_score"); }
        public void Dispose() { field.SetValue(board, was); }
    }

    static System.IDisposable EnableTopScoreForTest() { return new TopScoreEnabled(); }

    static void LeaderboardRegistry()
    {
        var all = LeaderboardBoards.All;
        var top = LeaderboardBoards.Get(LeaderboardBoards.TopScore);
        Check("top_score is the first (primary) board", all.Length > 0 && all[0].id == LeaderboardBoards.TopScore);
        Check("Top Score: iOS id me.sinaserati.Pause.top_score", top != null && top.iosId == "me.sinaserati.Pause.top_score");
        Check("Top Score: no Play Console id yet, so disabled", top != null && top.androidId == "" && !top.Enabled &&
              top.PlatformId(true) == null && top.PlatformId(false) == null);
        Check("Top Score never reuses the retired speed board's ids",
              !top.iosId.EndsWith("highest_speed") && top.androidId != "CgkI3eXNjrQcEAIQAA");
        Check("the speed board is retired: not in the table, no board enabled until Top Score's id is in",
              LeaderboardBoards.Get(LeaderboardBoards.RetiredSpeedBoard) == null && LeaderboardBoards.Enabled().Count == 0);
        Check("Top Score: higher is better, measures the run score, formats 1,234,567",
              top.sort == LeaderboardSort.HigherIsBetter && top.Measure(new LeaderboardRunStats { score = 1234567 }) == 1234567 &&
              top.Format(1234567) == "1,234,567");

        RealContext();
        PlayerPrefs.DeleteKey(LeaderboardService.PendingKey);
        PlayerPrefs.DeleteKey(LeaderboardService.SubmittedKey);
        var fake = new FakeLeaderboards();
        var service = new LeaderboardService(fake, () => 0f) { Ios = false };
        var prev = LeaderboardService.Instance;
        LeaderboardService.Instance = service;
        Check("disabled: no tab and nothing queued for Top Score",
              !service.UsableBoards().Exists(b => b.id == LeaderboardBoards.TopScore) &&
              !service.Offer(LeaderboardBoards.TopScore, 5000));
        using (EnableTopScoreForTest())
        {
            Check("once its Play Console id is in, Top Score is the first (and only) tab",
                  service.UsableBoards().Count == 1 && service.UsableBoards()[0].id == LeaderboardBoards.TopScore);
            int accepted = service.SubmitRun(new LeaderboardRunStats { score = 15451, starDust = 3f, worldIndex = 2 });
            long v;
            Check("a run end queues the score, and nothing for speed", accepted == 1 &&
                  service.HasPending(LeaderboardBoards.TopScore, out v) && v == 15451 &&
                  !service.HasPending(LeaderboardBoards.RetiredSpeedBoard, out v));
            Check("improvement only: a lower score is dropped", !service.Offer(LeaderboardBoards.TopScore, 9000));
            startMenu.youAreInTutorial = true;
            Check("never from the tutorial", service.SubmitRun(new LeaderboardRunStats { score = 99999 }) == 0);
            startMenu.youAreInTutorial = false;
        }
        LeaderboardService.Instance = prev;
        PlayerPrefs.DeleteKey(LeaderboardService.PendingKey);
    }

    // ---- 12. speed tiers at the cap, the flight dust, the speed achievements --------------------

    static void SpeedTiersDustAndMilestones()
    {
        // (2026-10, second pass: was 20 / 30 / 40 / 46 -> x1.25 / 1.5 / 2 / 2.5.
        // Speed is capped at HUD 35; above it is only ever a limit break.)
        Check("speed tiers: 20 / 30 / 35 -> x1.25 / 1.5 / 2",
              string.Join(",", ScoreRules.SpeedTierHud) == "20,30,35" &&
              ScoreRules.SpeedTierMultiplier.Length == 3 && ScoreRules.SpeedTierMultiplier[0] == 1.25f &&
              ScoreRules.SpeedTierMultiplier[1] == 1.5f && ScoreRules.SpeedTierMultiplier[2] == 2f);
        Check("the top tier is the cap itself", ScoreRules.SpeedTierHud[ScoreRules.SpeedTierHud.Length - 1] == SpeedRamp.CapHud &&
              SpeedRamp.CapHud == 35);
        Check("tier table: 19 x1, 20 x1.25, 29 x1.25, 30 x1.5, 34 x1.5, 35 x2",
              ScoreRules.SpeedMultiplierFor(.19f) == 1f && ScoreRules.SpeedMultiplierFor(.20f) == 1.25f &&
              ScoreRules.SpeedMultiplierFor(.29f) == 1.25f && ScoreRules.SpeedMultiplierFor(.30f) == 1.5f &&
              ScoreRules.SpeedMultiplierFor(.34f) == 1.5f && ScoreRules.SpeedMultiplierFor(.35f) == 2f);
        Check("limit break: anything above HUD 35 is x" + ScoreRules.LimitBreakMultiplier + " (36, 40, 45)",
              ScoreRules.LimitBreakMultiplier == 2.5f &&
              ScoreRules.SpeedMultiplierFor(.36f) == ScoreRules.LimitBreakMultiplier &&
              ScoreRules.SpeedMultiplierFor(.40f) == ScoreRules.LimitBreakMultiplier &&
              ScoreRules.SpeedMultiplierFor(.45f) == ScoreRules.LimitBreakMultiplier &&
              ScoreRules.IsLimitBreak(.36f) && !ScoreRules.IsLimitBreak(.35f));

        // The flight trickle: the old 0.05 a second at "top speed" 0.6, in
        // proportion below it, is exactly 1/12 per unit of distance at every
        // speed the game reaches. Same income, named for what it is.
        bool same = true;
        float worst = 0f;
        for (int i = 0; i <= 45; i++)
        {
            float speed = i / 100f;
            foreach (float dt in new[] { 1f / 60f, 1f / 120f, .5f })
            {
                float old = ScoreRules.DustRewardScale * .05f * Mathf.Min(1f, speed / .6f) * dt;   // the old trickle, paid at the reward scale
                float now = ScoreRules.FlightDust(speed, dt);
                float err = Mathf.Abs(now - old);
                worst = Mathf.Max(worst, err);
                same &= err <= 1e-6f * Mathf.Max(1f, old);
            }
        }
        Check("flight dust per distance equals the old top-speed trickle x DustRewardScale at every speed 0-45 (worst error " + worst + ")", same);
        Check("flight dust: nothing frozen or stopped", ScoreRules.FlightDust(.35f, 0f) == 0f && ScoreRules.FlightDust(0f, 1f) == 0f);
        Check("score has no top-speed fields any more",
              typeof(score).GetField("topSpeed") == null && typeof(score).GetField("dustPerSecondAtTopSpeed") == null);
        Check("source: the trickle is ScoreRules.FlightDust",
              System.IO.File.ReadAllText("Assets/Scripts/Core/score.cs").Contains("ScoreRules.FlightDust(moveBackGround.speed, Time.deltaTime)"));

        // Flash / Speedster / Super Sonic mark the cap and the limit break.
        float cap = SpeedRamp.Cap, max = SpeedRamp.MaxBoost;
        Check("milestones: below the cap, none (34, or 25 on a boost)",
              SpeedMilestones.Reached(cap - .01f, 0f) == 0 &&
              SpeedMilestones.Reached(.20f, .05f) == 0);
        Check("milestones: natural 35 without a boost is Flash (1)", SpeedMilestones.Reached(cap, 0f) == 1);
        Check("milestones: boosted past the cap is Speedster (2)",
              SpeedMilestones.Reached(cap, SpeedRamp.BoostPerAtom) == 2);
        Check("milestones: the full boost at the cap is Super Sonic (3)", SpeedMilestones.Reached(cap, max) == 3);
    }

    // ---- 13. loops pay more for flight and kills ------------------------------------------------

    static void LoopScoreScale()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        RealContext();
        RunScore.BeginRun(true, true);
        var rock = EnemyRoster.One(0, EnemyRole.Rock);
        try
        {
            Check("loop score scale: x1 / 1.15 / 1.30 / 1.45, x1.6 from loop 4",
                  LoopRules.ScoreScale(0) == 1f && Mathf.Approximately(LoopRules.ScoreScale(1), 1.15f) &&
                  Mathf.Approximately(LoopRules.ScoreScale(3), 1.45f) && Mathf.Approximately(LoopRules.ScoreScale(4), 1.6f) &&
                  Mathf.Approximately(LoopRules.ScoreScale(9), 1.6f));

            long t = RunScore.Total;
            RunScore.Tick(100f, .19f);
            long first = RunScore.Total - t;
            BreakChain();
            long k0 = Kill(Enemy(rock));
            BreakChain();

            RunLoop.Advance();   // loop 2 (Index 1)
            t = RunScore.Total;
            RunScore.Tick(100f, .19f);
            long second = RunScore.Total - t;
            BreakChain();
            long k1 = Kill(Enemy(rock));
            BreakChain();
            Check("flight: 100 s at HUD 19 pay " + first + " on the first pass and " + second + " on loop 2 (x1.15)",
                  Mathf.Abs(first - 95f) <= 1f && Mathf.Abs(second - 95f * LoopRules.ScoreScale(1)) <= 1f);

            for (int i = 0; i < 3; i++) RunLoop.Advance();   // Index 4: the cap
            long k4 = Kill(Enemy(rock));
            Check("kills: a rock pays " + k0 + " / " + k1 + " / " + k4 + " on loops 1 / 2 / 5 (5 x1, x1.15, x1.6)",
                  k0 == ScoreRules.Rock && k1 == Mathf.RoundToInt(ScoreRules.Rock * LoopRules.ScoreScale(1)) &&
                  k4 == Mathf.RoundToInt(ScoreRules.Rock * LoopRules.ScoreScale(4)));
        }
        finally
        {
            RunLoop.Reset();
        }
    }
}
