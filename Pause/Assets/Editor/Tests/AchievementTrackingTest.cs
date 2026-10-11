using System.Collections.Generic;
using System.Linq;
using UnityEditor.SceneManagement;
using UnityEngine;

// Every tracking hook family, driven by a simulated event: kills (enemy / rock /
// mine / blink), elites, bosses, worlds and loops, pickups, deaths, pauses, the
// shop, the codex, score and chain, speed, the blink-dodge probe, the toast --
// and the guards (tutorial, developer mode, not a real run) that give no progress.
public static class AchievementTrackingTest
{
    static int fails;
    static void Check(string what, bool ok)
    {
        Debug.Log((ok ? "[ATR] PASS  " : "[ATR] FAIL  ") + what);
        if (!ok) fails++;
    }

    public static void Run() { TestHarness.Exit(Execute()); }

    static bool U(string id) { return AchievementStore.IsUnlocked(AchievementCatalog.Find(id)); }
    static int C(string counter) { return AchievementStore.Counter(counter); }

    static GameObject Obj(string name, string tag)
    {
        var go = new GameObject(name);
        go.tag = tag;
        return go;
    }

    public static int Execute()
    {
        fails = 0;
        using var sandbox = new TestHarness.Sandbox();
        PlayerPrefs.SetInt(DeveloperUnlocks.EnabledKey, 0);
        // a clean profile: no ships, worlds or codex from this machine's editor prefs
        PlayerPrefs.DeleteKey(WorldManager.PrefsHighestWorld);
        for (int i = 1; i <= shopingShips.shipTotal; i++)
        {
            PlayerPrefs.DeleteKey("boughtship" + i);
            for (int n = 1; n < ShipSkins.PerShip; n++) PlayerPrefs.DeleteKey(ShipSkins.OwnedKey(i, n));
        }
        PlayerPrefs.SetString(Codex.PrefsKey, "");
        Codex.Reload();
        bool tide = WorldManager.TideEnabled;
        var objs = new List<GameObject>();
        try
        {
            WorldManager.TideEnabled = false;
            AchievementStores.Current = new FakeAchievementStore { available = false };
            Fresh();
            Achievements.ForceReal = true;
            Kills(objs);
            Elites();
            Bosses();
            Worlds();
            Pickups();
            Pauses();
            Score();
            Shop();
            CodexHooks();
            Dodge(objs);
            Guards(objs);
            Meta();
            Toast();
            Calls();
        }
        finally
        {
            foreach (var o in objs) if (o != null) Object.DestroyImmediate(o);
            AchievementTracker.Disable();
            AchievementTracker.Clock = () => Time.time;
            Achievements.ForceReal = null;
            AchievementStores.Current = null;
            WorldManager.TideEnabled = tide;
            AchievementStore.ResetAll();
        }
        Debug.Log("[ATR] failures: " + fails);
        return fails;
    }

    static void Fresh()
    {
        AchievementStore.ResetAll();
        AchievementTracker.ResetRun();
    }

    static void Kills(List<GameObject> objs)
    {
        Fresh();
        var enemy = Obj("alien1(Clone)", "Enimey"); objs.Add(enemy);
        var rock = Obj("aestroid_brown(Clone)", "Astr"); objs.Add(rock);
        var mine = Obj("mine(Clone)", "Enimey"); objs.Add(mine);
        var bossShot = Obj("BossShot", "Enimey"); objs.Add(bossShot);
        AchievementTracker.OnKill(enemy, 0);
        Check("an enemy kill counts (kills 1)", C("kills") == 1 && C("rocks") == 0 && C("mines") == 0);
        AchievementTracker.OnKill(rock, 0);
        Check("a rock counts as a rock, not a kill", C("kills") == 1 && C("rocks") == 1);
        AchievementTracker.OnKill(mine, 0);
        Check("a rail mine is a kill AND a mine", C("kills") == 2 && C("mines") == 1);
        AchievementTracker.OnKill(bossShot, 0);
        Check("a boss part is neither", C("kills") == 2);
        Check("no blink kill yet", !U("pause_blink_kill"));
        AchievementTracker.OnKill(enemy, ScoreRules.TeleportKillBonus);
        Check("a kill with the blink bonus unlocks Correct Pause", U("pause_blink_kill") && C("kills") == 3);

        AchievementStore.SetCounter("kills", 99);
        AchievementTracker.OnKill(enemy, 0);
        Check("the 100th kill unlocks Gunner", U("kills_100") && !U("kills_1000"));
        AchievementStore.SetCounter("rocks", 499);
        AchievementTracker.OnKill(rock, 0);
        AchievementStore.SetCounter("mines", 24);
        AchievementTracker.OnKill(mine, 0);
        Check("500 rocks / 25 mines unlock Rock Breaker / Minesweeper", U("rocks_500") && U("mines_25"));
        // through the game's own hook
        int kills = C("kills");
        collisionDetection.RecordKillAchievement(enemy);
        Check("collisionDetection.RecordKillAchievement feeds the tracker", C("kills") == kills + 1);

        Fresh();
        AchievementTracker.OnChain(9);
        Check("a 9-chain is not enough", !U("chain_10") && C("best_chain") == 9);
        AchievementTracker.OnChain(10);
        Check("a 10-chain unlocks Chain Reaction", U("chain_10"));
        Fresh();
        AchievementTracker.OnMegaDomino(Vector3.zero);
        Check("a MEGA DOMINO unlocks the hidden achievement", U("mega_domino"));
    }

    static void Elites()
    {
        Fresh();
        var defs = EliteCatalog.All;
        var space = defs.Where(d => d.WorldIndex == 0).ToArray();
        AchievementTracker.OnEliteKilled(space[0], EliteDamage.PlayerWeapon);
        Check("an elite kill: Elite Hunter, one Space elite", U("elite_first") && C("elites") == 1 && C("elite_w0") == 1);
        AchievementTracker.OnEliteKilled(space[0], EliteDamage.PlayerWeapon);
        Check("the same elite again counts as a kill but not a new Space elite", C("elites") == 2 && C("elite_w0") == 1);
        AchievementTracker.OnEliteKilled(space[0], EliteDamage.Crash);
        AchievementTracker.OnEliteKilled(space[0], EliteDamage.Rail);
        AchievementTracker.OnEliteKilled(space[0], EliteDamage.FriendlyFire);
        Check("crash / rail / friendly fire are not the pilot's kills", C("elites") == 2);
        for (int i = 1; i < space.Length; i++) AchievementTracker.OnEliteKilled(space[i], EliteDamage.ShieldRam);
        Check("every Space elite unlocks Space Elites (" + space.Length + ")", U("elite_space_all") && !U("elite_frost_all"));
        Check("no blink strike yet", !U("elite_blink"));
        AchievementTracker.OnEliteKilled(space[0], EliteDamage.Teleport);
        Check("an elite killed by a blink: Blink Strike (+ Correct Pause)", U("elite_blink") && U("pause_blink_kill"));
        AchievementStore.SetCounter("elites", 9);
        AchievementTracker.OnEliteKilled(space[0], EliteDamage.PlayerContact);
        Check("10 elites: Veteran Hunter", U("elite_10") && !U("elite_50"));
        var verdant = defs.Where(d => d.WorldIndex == 2).ToArray();
        foreach (var d in verdant) AchievementTracker.OnEliteKilled(d, EliteDamage.PlayerWeapon);
        Check("the Verdant elite completes Verdant Elites", U("elite_verdant_all"));
        foreach (var d in defs.Where(d => d.WorldIndex == 3)) AchievementTracker.OnEliteKilled(d, EliteDamage.PlayerWeapon);
        foreach (var d in defs.Where(d => d.WorldIndex == 1)) AchievementTracker.OnEliteKilled(d, EliteDamage.PlayerWeapon);
        Check("Frost and Ember sets complete too", U("elite_frost_all") && U("elite_ember_all"));
    }

    static void Bosses()
    {
        Fresh();
        AchievementTracker.OnBossFightStart();
        AchievementTracker.OnBossOutro(0, false);
        Check("a boss that warped away is no kill", !U("boss_space"));
        AchievementTracker.OnBossOutro(0, true);
        Check("destroying the Space boss: Archon Down + Untouchable", U("boss_space") && U("boss_no_hit") && C("bosses") == 1);
        AchievementStore.ResetAll();
        AchievementTracker.OnBossFightStart();
        AchievementTracker.OnPlayerHurt();
        AchievementTracker.OnBossOutro(1, true);
        Check("losing a heart in the fight: the boss falls, but no Untouchable", U("boss_frost") && !U("boss_no_hit"));
        AchievementTracker.OnBossFightStart();
        AchievementTracker.OnBossOutro(1, true);
        Check("a new fight starts clean: Untouchable", U("boss_no_hit"));
        AchievementTracker.OnBossOutro(1, true);
        Check("the same boss twice is one boss for Boss Collector", C("bosses") == 1);
        AchievementTracker.OnBossOutro(0, true);
        AchievementTracker.OnBossOutro(2, true);
        AchievementTracker.OnBossOutro(3, true);
        Check("all four live bosses: Boss Collector", U("boss_verdant") && U("boss_ember") && C("bosses") == 4 && U("boss_all"));
        AchievementTracker.OnBossOutro(4, true);
        Check("the Tide boss is dormant while Tide is off", !U("boss_tide"));
        WorldManager.TideEnabled = true;
        AchievementTracker.OnBossOutro(4, true);
        Check("... and counts once it is on", U("boss_tide"));
        WorldManager.TideEnabled = false;
    }

    static void Worlds()
    {
        Fresh();
        AchievementTracker.OnWorldEntered(0);
        Check("Space is the start, no achievement", AchievementStore.UnlockedCount == 0);
        AchievementTracker.OnWorldEntered(1); AchievementTracker.OnWorldEntered(2); AchievementTracker.OnWorldEntered(3);
        Check("Frost, Verdant, Ember reached", U("world_frost_reached") && U("world_verdant_reached") && U("world_ember_reached"));
        AchievementTracker.OnWorldEntered(4);
        Check("Tide reached stays dormant", !U("world_tide_reached"));
        AchievementTracker.OnLoop(1);
        Check("one loop: Full Circle", U("loop_1") && !U("loop_2"));
        AchievementTracker.OnLoop(2);
        AchievementTracker.OnLoop(1);
        Check("two loops: Second Lap (a lower loop never lowers the best)", U("loop_2") && !U("loop_5") && C("best_loop") == 2);
        AchievementTracker.OnLoop(5);
        Check("five loops: Orbit Lifer", U("loop_5"));
    }

    static void Pickups()
    {
        Fresh();
        for (int i = 0; i < 149; i++) AchievementTracker.OnStar();
        Check("149 stars: not yet", !U("stars_150"));
        AchievementTracker.OnStar();
        Check("150 stars: Dust Gatherer", U("stars_150") && C("stars") == 150);
        for (int i = 0; i < 9; i++) AchievementTracker.OnDeath();
        Check("9 crashes: not yet", !U("deaths_10"));
        AchievementTracker.OnDeath();
        Check("10 crashes: Crash Course", U("deaths_10") && !U("deaths_100"));
        AchievementTracker.OnSecretPower();
        Check("a secret power: Secret Power", U("secret_power_first"));
        AchievementTracker.OnSpeedMilestone(1);
        Check("Flash", U("speed_flash") && !U("speed_speedster"));
        AchievementTracker.OnSpeedMilestone(2); AchievementTracker.OnSpeedMilestone(3);
        Check("Speedster and Super Sonic", U("speed_speedster") && U("speed_super_sonic"));
    }

    static void Pauses()
    {
        Fresh();
        AchievementTracker.OnWorldCleared();
        Check("clearing a world with no pause spent: Hold Your Breath", U("pause_no_pause_world"));
        AchievementStore.ResetAll();
        AchievementTracker.OnWorldEntered(1);
        AchievementTracker.OnPauseSpent();
        Check("the first pause spent: Paused", U("pause_first"));
        AchievementTracker.OnWorldCleared();
        Check("... but that world was not cleared without a pause", !U("pause_no_pause_world"));
        AchievementTracker.OnWorldEntered(2);
        AchievementTracker.OnWorldCleared();
        Check("the next world starts clean: it counts", U("pause_no_pause_world"));
        AchievementTracker.OnPauseCount(14);
        Check("14 pauses held: not yet", !U("pause_hoarder"));
        AchievementTracker.OnPauseCount(15);
        Check("15 pauses held: Time Banker", U("pause_hoarder"));
        for (int i = 0; i < 99; i++) AchievementTracker.OnTeleport(Vector3.zero, Vector3.right);
        Check("99 blinks: not yet", !U("pause_blink_100"));
        AchievementTracker.OnTeleport(Vector3.zero, Vector3.right);
        Check("100 blinks: Blink Master", U("pause_blink_100"));
        // through score.cs's own hooks
        AchievementStore.ResetAll();
        score.pauseCounter = 13;
        score.incromentPause();
        Check("score.incromentPause reaches the tracker (13 + 2 = 15)", U("pause_hoarder"));
    }

    static void Score()
    {
        Fresh();
        AchievementTracker.OnScore(4999);
        Check("4,999: not yet", !U("score_10k"));
        AchievementTracker.OnScore(5000);
        Check("5,000: Rookie Score", U("score_10k") && !U("score_50k"));
        AchievementTracker.OnScore(20000);
        Check("20,000: Ace Score", U("score_50k") && !U("score_150k"));
        AchievementTracker.OnScore(80000);
        Check("80,000: Legend Score", U("score_150k"));
        // the real event path: RunScore raises Scored while a scoring run is live
        AchievementStore.ResetAll();
        AchievementTracker.Enable();
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        RunScore.EndRun(RunScore.RunId);
        RunScore.BeginRun(true, true);
        var e = Obj("alien1", "Enimey");
        try
        {
            for (int i = 0; i < 30; i++) RunScore.OnKill(e, 0);
            Check("RunScore.Scored drives the score counter (" + C("best_score") + " of " + RunScore.Total + ")",
                  C("best_score") > 0 && C("best_score") <= RunScore.Total && C("best_score") >= RunScore.Total - 50);
            Check("RunScore.OnKill feeds the chain counter", C("best_chain") == RunScore.Parts.bestChain && C("best_chain") >= 10 && U("chain_10"));
        }
        finally { Object.DestroyImmediate(e); RunScore.EndRun(RunScore.RunId); AchievementTracker.Disable(); }
    }

    static void Shop()
    {
        Fresh();
        PlayerPrefs.SetFloat(StarDustLedger.CurrencyKey, 5000f);
        PlayerPrefs.SetString("boughtship1", "True");
        bool bought = shopingShips.TryPurchase(2, shopingShips.CostFor(2));
        Check("buying a ship: Hangar Debut, dust spent counted, ships counted (bought " + bought + ", spent " + C("spent") + ", ships " + C("ships") + ")",
              bought && U("ship_first") && C("spent") == 600 && C("ships") == 2 && !U("ship_half"));
        for (int i = 3; i <= 8; i++) shopingShips.TryPurchase(i, 0f);
        Check("8 ships owned: Squadron (" + C("ships") + ")", C("ships") == 8 && U("ship_half") && !U("ship_all"));
        for (int i = 9; i <= 15; i++) shopingShips.TryPurchase(i, 0f);
        Check("every ship owned: Fleet Admiral", C("ships") == ShipId.Count && U("ship_all"));
        Check("a failed purchase changes nothing", !shopingShips.TryPurchase(2, 99999f) && C("spent") == 600);

        var res = ShipSkins.TryPurchase(2, 1);
        Check("buying a skin: Fresh Paint, price spent counted", res == ShipSkins.PurchaseResult.Bought && U("skin_first") && C("spent") == 600 + 300 && !U("skin_special"));
        ShipSkins.TryPurchase(2, ShipSkins.Special);
        Check("buying a Special skin: Special Edition", U("skin_special") && C("spent") == 600 + 300 + 750);
        Check("not a full wardrobe yet", !U("skin_full_set"));
        ShipSkins.TryPurchase(2, 2); ShipSkins.TryPurchase(2, 3);
        Check("all skins of one ship: Full Wardrobe", U("skin_full_set"));
        AchievementStore.SetCounter("spent", 9900);
        PlayerPrefs.SetFloat(StarDustLedger.CurrencyKey, 5000f);
        ShipSkins.TryPurchase(3, 1);
        Check("10,000 spent: Big Spender", U("dust_spent_10000"));
        Fresh();
        AchievementStore.SetCounter("spent", 5);
        Check("an owned skin is not re-bought", ShipSkins.TryPurchase(2, 1) == ShipSkins.PurchaseResult.AlreadyOwned && C("spent") == 5);
    }

    static void CodexHooks()
    {
        Fresh();
        PlayerPrefs.SetString(Codex.PrefsKey, "");
        Codex.Reload();
        AchievementTracker.RefreshCodex();
        Check("a fresh codex: the free entries count, nothing unlocked (" + Codex.DiscoveredCount + " free, codex_10 " + U("codex_10") + ")", C("codex") == Codex.DiscoveredCount && U("codex_10") == (Codex.DiscoveredCount >= 10) && !U("codex_50"));
        // Field Guide: everything non-boss, non-elite of ONE world
        var world0 = Codex.Entries.Where(e => (e.category == CodexCategory.Enemies || e.category == CodexCategory.Hazards) &&
                                              BossCatalog.Find(e.id) == null && EliteCatalog.FindByCodexId(e.id) == null)
                                  .Where(e => { var d = EnemyRoster.FindByCodexId(e.id); return d != null && d.world == 0; })
                                  .Select(e => e.id).ToArray();
        Check("Space has a field guide to complete (" + world0.Length + ")", world0.Length >= 4);
        PlayerPrefs.SetString(Codex.PrefsKey, string.Join(",", world0.Take(world0.Length - 1)));
        Codex.Reload();
        AchievementTracker.RefreshCodex();
        Check("one entry short: no Field Guide", !U("codex_field_guide"));
        PlayerPrefs.SetString(Codex.PrefsKey, string.Join(",", world0));
        Codex.Reload();
        AchievementTracker.RefreshCodex();
        Check("a world fully catalogued: Field Guide", U("codex_field_guide"));

        // 50 entries
        PlayerPrefs.SetString(Codex.PrefsKey, string.Join(",", Codex.Entries.Where(e => !e.secret).Take(60).Select(e => e.id)));
        Codex.Reload();
        AchievementTracker.RefreshCodex();
        Check("50+ entries: Cartographer", Codex.DiscoveredCount >= 50 && U("codex_50"));
        // every entry (developer mode shows all, but the achievement needs real discoveries)
        PlayerPrefs.SetString(Codex.PrefsKey, string.Join(",", Codex.Entries.Select(e => e.id)));
        PlayerPrefs.SetString("boughtship1", "True");
        for (int i = 2; i <= 15; i++) PlayerPrefs.SetString("boughtship" + i, "True");
        PlayerPrefs.SetInt(WorldManager.PrefsHighestWorld, 3);
        Codex.Reload();
        AchievementTracker.RefreshCodex();
        Check("every entry discovered: Completionist (" + Codex.DiscoveredCount + "/" + Codex.Entries.Length + ")",
              Codex.DiscoveredCount == Codex.Entries.Length && U("codex_complete"));
        PlayerPrefs.SetString(Codex.PrefsKey, "");
        Codex.Reload();
    }

    static GameObject Shot(List<GameObject> objs, Vector3 at)
    {
        var go = new GameObject("TestShot");
        go.transform.position = at;
        var col = go.AddComponent<CircleCollider2D>();
        col.radius = .1f;
        col.isTrigger = true;
        go.AddComponent<EliteShotHitbox>();
        objs.Add(go);
        return go;
    }

    static void Dodge(List<GameObject> objs)
    {
        Fresh();
        float now = 10f;
        AchievementTracker.Clock = () => now;
        var shot = Shot(objs, new Vector3(2f, 3f, 0f));
        Physics2D.SyncTransforms();
        Check("no shot near: no dodge pending", !AchievementTracker.DodgePending);
        AchievementTracker.OnTeleport(new Vector3(-3f, 0f, 0f), new Vector3(1f, 1f, 0f));
        Check("a blink away from nothing is no dodge", !AchievementTracker.DodgePending);
        AchievementTracker.OnTeleport(new Vector3(2.2f, 3f, 0f), new Vector3(2.1f, 3.05f, 0f));
        Check("... nor one that lands on the shot", !AchievementTracker.DodgePending);
        AchievementTracker.OnTeleport(new Vector3(2.2f, 3f, 0f), new Vector3(-2f, -2f, 0f));
        Check("a blink out from beside a hostile shot starts the dodge", AchievementTracker.DodgePending);
        now = 10.5f; AchievementTracker.TickDodge();
        Check("half a second: not yet", !U("pause_perfect_dodge") && AchievementTracker.DodgePending);
        now = 11.1f; AchievementTracker.TickDodge();
        Check("a second survived: Perfect Pause", U("pause_perfect_dodge") && !AchievementTracker.DodgePending);

        AchievementStore.ResetAll();
        now = 20f;
        AchievementTracker.OnTeleport(new Vector3(2.2f, 3f, 0f), new Vector3(-2f, -2f, 0f));
        now = 20.4f;
        AchievementEvents.RaisePlayerHurt();
        now = 21.5f; AchievementTracker.TickDodge();
        Check("hurt within the second: no dodge", !U("pause_perfect_dodge") && !AchievementTracker.DodgePending);

        AchievementStore.ResetAll();
        AchievementTracker.DodgeProbeEnabled = false;
        now = 30f;
        AchievementTracker.OnTeleport(new Vector3(2.2f, 3f, 0f), new Vector3(-2f, -2f, 0f));
        Check("the probe can be switched off", !AchievementTracker.DodgePending);
        AchievementTracker.DodgeProbeEnabled = true;
        Object.DestroyImmediate(shot);
        AchievementTracker.Clock = () => Time.time;
    }

    static void Guards(List<GameObject> objs)
    {
        Fresh();
        var enemy = Obj("alien1", "Enimey"); objs.Add(enemy);
        Achievements.ForceReal = null;
        score.paysRealDust = false;
        AchievementTracker.OnKill(enemy, 0); AchievementTracker.OnStar(); AchievementTracker.OnDeath(); AchievementTracker.OnLoop(5);
        AchievementTracker.OnWorldEntered(3); AchievementTracker.OnPauseSpent(); AchievementTracker.OnSecretPower(); AchievementTracker.OnScore(99999);
        Check("tutorial / practice run (paysRealDust false): no progress at all", AchievementStore.UnlockedCount == 0 && C("kills") == 0 && C("stars") == 0);
        score.paysRealDust = true;
        PlayerPrefs.SetInt(DeveloperUnlocks.EnabledKey, 1);
        AchievementTracker.OnKill(enemy, 0); AchievementTracker.OnStar(); AchievementTracker.OnLoop(5); AchievementTracker.OnBossOutro(0, true);
        AchievementTracker.OnShipBought(2, 600f); AchievementTracker.RefreshCodex();
        Check("developer mode: no progress, in a run or in the shop", AchievementStore.UnlockedCount == 0 && C("kills") == 0 && C("spent") == 0);
        PlayerPrefs.SetInt(DeveloperUnlocks.EnabledKey, 0);
        AchievementTracker.OnKill(enemy, 0);
        Check("a real run counts (paysRealDust true, developer mode off)", C("kills") == 1);
        score.paysRealDust = false;
        AchievementTracker.OnShipBought(2, 600f);
        Check("menu events do not need a run: a purchase counts", C("spent") == 600);
        Achievements.ForceReal = true;
    }

    static void Meta()
    {
        Fresh();
        PlayerPrefs.SetInt(DeveloperUnlocks.EnabledKey, 1);
        AchievementTracker.OnTutorialDone();
        Check("tutorial done in developer mode: nothing", !U("meta_first_flight"));
        PlayerPrefs.SetInt(DeveloperUnlocks.EnabledKey, 0);
        Achievements.ForceReal = null;
        score.paysRealDust = false;   // the tutorial scene is not a real run
        AchievementTracker.OnTutorialDone();
        Check("the tutorial completion counts even though the tutorial is not a real run", U("meta_first_flight"));
        AchievementTracker.OnSignedIn();
        Check("signing in: Logged On", U("meta_logged_on"));
        PlayerPrefs.SetInt(DeveloperUnlocks.EnabledKey, 1);
        AchievementStore.ResetAll();
        AchievementTracker.OnSignedIn();
        PlayerPrefs.SetInt(DeveloperUnlocks.EnabledKey, 0);
        Check("sign-in is not blocked by developer mode", U("meta_logged_on"));
        Achievements.ForceReal = true;
    }

    static void Toast()
    {
        Fresh();
        AchievementTracker.Enable();
        try
        {
            AchievementStore.Unlock(AchievementCatalog.Find("loop_1"));   // wired via AchievementStore.Unlocked; not gameS1: silent
            Check("an unlock outside a run raises no toast and does not throw", CodexToast.Current == null || !CodexToast.Current.Showing);
            Check("the toast heading", AchievementTracker.ToastHeading == "ACHIEVEMENT UNLOCKED");
            Check("the toast icon is the badge (or its placeholder)", AchievementArt.For("loop_1") != null);
        }
        finally { AchievementTracker.Disable(); }
    }

    static void Calls()
    {
        // the call sites exist where the plan puts them
        string root = System.IO.Path.GetFullPath("Assets/Scripts");
        System.Func<string, string> read = p => System.IO.File.ReadAllText(System.IO.Path.Combine(root, p));
        Check("hook: WorldManager.Advance reports worlds, loops and cleared worlds",
              read("Worlds/WorldManager.cs").Contains("AchievementTracker.OnWorldEntered") && read("Worlds/WorldManager.cs").Contains("AchievementTracker.OnLoop") &&
              read("Worlds/WorldManager.cs").Contains("AchievementTracker.OnWorldCleared"));
        Check("hook: BossEncounter fight start and outro", read("Bosses/BossEncounter.cs").Contains("AchievementTracker.OnBossFightStart") &&
              read("Bosses/BossEncounter.cs").Contains("AchievementTracker.OnBossOutro"));
        Check("hook: collisionDetection raises PlayerHurt, death, stars", read("Ship/collisionDetection.cs").Contains("AchievementEvents.RaisePlayerHurt") &&
              read("Ship/collisionDetection.cs").Contains("AchievementTracker.OnDeath") && read("Ship/collisionDetection.cs").Contains("AchievementTracker.OnStar"));
        Check("hook: shop, skins, secret power, pauses, blinks",
              read("Shop/shopingShips.cs").Contains("AchievementTracker.OnShipBought") && read("Ship/ShipSkins.cs").Contains("AchievementTracker.OnSkinBought") &&
              read("Gameplay/Weapons/SecretPowers.cs").Contains("AchievementTracker.OnSecretPower") && read("Core/score.cs").Contains("AchievementTracker.OnPauseSpent") &&
              read("Ship/movePlayer.cs").Contains("AchievementTracker.OnTeleport"));
        Check("the old achievement code is gone", !System.IO.File.Exists(System.IO.Path.Combine(root, "Core/achievementAPICalls.cs")) &&
              !System.IO.File.Exists(System.IO.Path.Combine(root, "Core/AchievementTiers.cs")) && !System.IO.File.Exists(System.IO.Path.Combine(root, "Core/StringHolder.cs")));
    }
}
