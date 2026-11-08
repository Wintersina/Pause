using System.Collections.Generic;
using UnityEditor.SceneManagement;
using System;
using System.Reflection;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

// The star-dust boosters (Boosters, HazardClear, BoosterHud / BoosterHudSource):
//   1  the pure stacking rule (booster + rewarded ad capped at 3x)
//   2  PRICES: simulated typical dust in 10 minutes of play; the multiplier's extra dust over one purchase (5 minutes) stays under 70% of its price
//   3  buying: pay once, extension, limits, restart safety, no buying inside a run
//   4  the clock: ticks only on unfrozen real-run frames; practice runs never tick or multiply
//   5  what it multiplies (pickups, rewards, flight) and what it must not (claims, purchases, score, leaderboard dust)
//   6  hazard clear: what it removes, what it leaves, and that it pays no score, kills, dust or achievements
//   6b the AUTOMATIC save: a hit that would cost a heart spends a charge instead (collisionDetection), never when nothing was lost
//   7  cloud save: capture / apply round trip and the merge rules
//   8  the active-booster column (not a control): its slots on every ScreenFit device never meet the HUD, the Shop icons
//      drawn crisp, countdowns frozen with the world, icons come and go as boosters start / run out / are switched off
//   9  homing atoms: buying (1,000, 5 minutes, the hour cap), the clock, the cloud counters, and the flight itself --
//      a still ship is reached, a late side-step or a teleport makes it miss, a miss is final, star dust never homes
//   10 the Shop's ON/OFF switch: off freezes the time left and stops every effect, survives a restart, on resumes, buying switches on;
//      Hazard Clear off never auto-fires
//   11 legacy 10-minute purchases keep their 10 minutes next to the new 5-minute ones (prefs and cloud)
//   12 the read-only API the HUD reads (IsTimed / Owns / IsOn / IsActive / SecondsLeft / ChargesLeft / Changed)
public static class BoostersTest
{
    static int fails;
    static void Check(string what, bool ok)
    {
        Debug.Log((ok ? "[BOOSTERS] PASS  " : "[BOOSTERS] FAIL  ") + what);
        if (!ok) fails++;
    }

    public static void Run() { TestHarness.Exit(Execute()); }

    static readonly string[] Keys = Boosters.AllKeys;

    static void Reset(float dust)
    {
        foreach (string k in Keys) PlayerPrefs.DeleteKey(k);
        PlayerPrefs.SetFloat(StarDustLedger.CurrencyKey, dust);
        PlayerPrefs.SetInt(DeveloperUnlocks.EnabledKey, 0);
        Boosters.AdDoubled = false;
        Boosters.TestDelta = null;
        Boosters.Reload();
        Boosters.ResetRun();
    }

    public static int Execute()
    {
        fails = 0;
        using var sandbox = new TestHarness.Sandbox();
        try
        {
            Stacking();
            Prices();
            Buying();
            Clock();
            Multiplies();
            Clearing();
            AutoSave();
            Cloud();
            Control();
            Homing();
            Switch();
            Legacy();
            ReadApi();
        }
        finally
        {
            TouchInput.PressedOverride = null;
            Boosters.TestDelta = null;
            Boosters.AdDoubled = false;
            moveItemEnmInStrightLine.HomingTargetOverride = null;
            startMenu.youAreInTutorial = false;
            buttonClicks.playerDied = false;
            RunScore.EndRun(RunScore.RunId);
            StarDustLedger.EndRun(StarDustLedger.RunId);
            foreach (string k in Keys) PlayerPrefs.DeleteKey(k);
            Boosters.Reload();
        }
        Debug.Log("[BOOSTERS] failures: " + fails);
        return fails;
    }

    // ---- 1 ----
    static void Stacking()
    {
        Check("multiplier: none 1x, booster 2x, ad 2x, both 3x (never 4x)",
              Boosters.DustMultiplier(false, false) == 1f && Boosters.DustMultiplier(true, false) == 2f &&
              Boosters.DustMultiplier(false, true) == 2f && Boosters.DustMultiplier(true, true) == 3f);
        Check("the cap is 3", Boosters.MaxDustMultiplier == 3f);
    }

    // ---- 2 ----
    // Same model as DustEconomyTest.Run: levels of 120 s at 60 fps, speed .10 -> .33, seeded pickup / kill / elite counts.
    static float SimulatedDust(int seed, int worlds)
    {
        var rng = new System.Random(seed);
        float total = 0f;
        const float dt = 1f / 60f;
        for (int w = 0; w < worlds; w++)
        {
            for (int f = 0; f < 120 * 60; f++) total += ScoreRules.FlightDust(.10f + .23f * f / (120f * 60f), dt);
            int small = rng.Next(45, 80), large = rng.Next(8, 18), blue = rng.Next(0, 3), kills = rng.Next(25, 55), elites = rng.Next(0, 2);
            total += small * .5f + large * 1f + blue * 2f;                       // pickups: full value
            total += kills * ScoreRules.RewardDust(.12f);                       // kill crumbs
            total += elites * ScoreRules.RewardDust(ScoreRules.EliteDownDust);  // elites
        }
        return total;   // the end-of-run score bonus is excluded: the booster does not multiply it
    }

    static void Prices()
    {
        float sum = 0f;
        const int seeds = 12;
        for (int s = 1; s <= seeds; s++) sum += SimulatedDust(s * 7919 + 5, 5);   // 5 levels x 120 s = 10 minutes
        float per10 = sum / seeds;
        float extra = per10 * (Boosters.DustBoosterSeconds / 600f) * (Boosters.DustMultiplier(true, false) - 1f);   // what the doubling adds over one purchase
        float flawless = extra * 1.35f;                                                // adaptive difficulty's best case (docs/adaptive-difficulty.md)
        float cap = Boosters.MaxPaybackShare * Boosters.DustBoosterPrice;
        Debug.Log(string.Format("[BOOSTERS] 10 min of play: {0:F0} dust; one purchase of x2 adds {1:F0} ({2:P0} of the {3:F0} price); flawless {4:F0} ({5:P0})",
            per10, extra, extra / Boosters.DustBoosterPrice, Boosters.DustBoosterPrice, flawless, flawless / Boosters.DustBoosterPrice));
        Check("the dust multiplier pays back less than 70% of its price over one purchase (typical)", extra < cap);
        Check("... and also for a flawless run (x1.35 income)", flawless < cap);
        Check("the multiplier is a net cost: the extra never reaches the price", flawless < Boosters.DustBoosterPrice);

        float run = 0f;
        for (int s = 1; s <= seeds; s++) run += SimulatedDust(s * 104729 + 3, 4);
        run /= seeds;
        Debug.Log("[BOOSTERS] a typical 4-level run earns " + run.ToString("F0") + " dust; a hazard clear costs " + Boosters.HazardClearPrice);
        Check("a hazard clear costs about one typical run's earnings (0.5x to 1.5x)", Boosters.HazardClearPrice > .5f * run && Boosters.HazardClearPrice < 1.5f * run);
        Check("prices sit far under a ship (Gold Warden 12,000)", Boosters.DustBoosterPrice * 10f < 12000f);
        Check("5 minutes per purchase (Dust x2 and Homing Atoms)", Boosters.DustBoosterSeconds == 300f && Boosters.HomingSeconds == 300f);
    }

    // ---- 3 ----
    static void Buying()
    {
        Reset(1000f);
        var r = Boosters.BuyDustBooster();
        Check("buying the multiplier takes the price once and adds 5 minutes",
              r == Boosters.BuyResult.Bought && Mathf.Approximately(PlayerPrefs.GetFloat(StarDustLedger.CurrencyKey), 1000f - Boosters.DustBoosterPrice) &&
              Boosters.BoughtDust == 1 && Mathf.Approximately(Boosters.RemainingSeconds, 300f) && Boosters.DustActive && Boosters.AnyActive);
        Boosters.Reload();   // a restart right after the purchase
        Check("a restart keeps it: same balance, same time, no second charge",
              Mathf.Approximately(PlayerPrefs.GetFloat(StarDustLedger.CurrencyKey), 400f) && Boosters.BoughtDust == 1 && Mathf.Approximately(Boosters.RemainingSeconds, 300f));
        Check("not enough dust: nothing changes", Boosters.BuyDustBooster() == Boosters.BuyResult.NotEnoughDust &&
              PlayerPrefs.GetFloat(StarDustLedger.CurrencyKey) == 400f && Boosters.BoughtDust == 1 && !Boosters.CanBuy(Boosters.Kind.DustMultiplier));

        Reset(10000f);
        Boosters.BuyDustBooster();
        Boosters.TestDelta = 100f;
        PlayerPrefs.SetInt(Boosters.DustUsedMsKey, 100000);   // 100 s spent
        Boosters.Reload();
        Boosters.BuyDustBooster();
        Check("buying while active stacks: 200 s left + 300 s = 500 s", Mathf.Approximately(Boosters.RemainingSeconds, 500f) && Boosters.BoughtDust == 2);
        Reset(100000f);
        int ok = 0;
        while (Boosters.BuyDustBooster() == Boosters.BuyResult.Bought && ok < 20) ok++;
        Check("stored time stops at an hour (12 purchases)", ok == 12 && Mathf.Approximately(Boosters.RemainingSeconds, Boosters.MaxStoredSeconds));

        Reset(5000f);
        Check("a hazard charge costs 200 and is held", Boosters.BuyHazardClear() == Boosters.BuyResult.Bought && Boosters.ClearCharges == 1 &&
              Mathf.Approximately(PlayerPrefs.GetFloat(StarDustLedger.CurrencyKey), 5000f - Boosters.HazardClearPrice));
        Check("holding charges does NOT count as an active booster (ads stay on)", !Boosters.AnyActive);
        Boosters.Reload();
        Check("charges survive a restart", Boosters.ClearCharges == 1);
        Reset(100000f);
        int bought = 0;
        while (Boosters.BuyHazardClear() == Boosters.BuyResult.Bought && bought < 30) bought++;
        Check("at most 9 charges are held", bought == Boosters.MaxClearCharges && Boosters.ClearCharges == 9);

        Reset(5000f);
        StarDustLedger.BeginRun(true);
        Check("no buying inside a run", Boosters.BuyDustBooster() == Boosters.BuyResult.RunInProgress && Boosters.BuyHazardClear() == Boosters.BuyResult.RunInProgress);
        StarDustLedger.EndRun(StarDustLedger.RunId);

        Reset(5000f);
        Check("shop API: names, text and prices", Boosters.Name(Boosters.Kind.DustMultiplier).Length > 0 && Boosters.Description(Boosters.Kind.HazardClear).Length > 0 &&
              Boosters.Price(Boosters.Kind.DustMultiplier) == Boosters.DustBoosterPrice && Boosters.Status(Boosters.Kind.DustMultiplier) == "" &&
              Boosters.Buy(Boosters.Kind.DustMultiplier) == Boosters.BuyResult.Bought && Boosters.Status(Boosters.Kind.DustMultiplier) == "5:00 left");
    }

    static score MakeScore()
    {
        var go = new GameObject("~BoostersScore");
        var s = go.AddComponent<score>();
        s.speedValue = new GameObject("s").AddComponent<Text>();
        s.pauseCounterText = new GameObject("p").AddComponent<Text>();
        return s;
    }

    static score BeginRealRun(float dust)
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        PlayerPrefs.SetString("HasDoneTut", "true");
        startMenu.youAreInTutorial = false;
        buttonClicks.playerDied = false;
        PlayerPrefs.SetFloat(StarDustLedger.CurrencyKey, dust);
        var s = MakeScore();
        s.SendMessage("Awake");
        s.SendMessage("Start");
        return s;
    }

    static void EndRun(score s)
    {
        Object.DestroyImmediate(s.gameObject);
        StarDustLedger.EndRun(StarDustLedger.RunId);
    }

    // ---- 4 ----
    static void Clock()
    {
        Reset(5000f);
        Boosters.BuyDustBooster();
        var s = BeginRealRun(5000f);
        Boosters.TestDelta = 1f;
        score.pauseCounter = 5;
        TouchInput.PressedOverride = () => false;   // the finger is up: the world is frozen
        for (int i = 0; i < 5; i++) s.SendMessage("Update");
        Check("a frozen run (finger up, pauses left) does not spend the clock", Mathf.Approximately(Boosters.RemainingSeconds, 300f));
        TouchInput.PressedOverride = () => true;
        buttonClicks.playerDied = true;
        s.SendMessage("Update");
        buttonClicks.playerDied = false;
        Check("a dead player does not spend it", Mathf.Approximately(Boosters.RemainingSeconds, 300f));
        s.SendMessage("Update");
        s.SendMessage("Update");
        Check("running frames spend it (1 s each in this test): 298 s left", Mathf.Approximately(Boosters.RemainingSeconds, 298f));
        Boosters.TestDelta = 10000f;
        s.SendMessage("Update");
        Check("it never goes below zero and then the booster is off", Boosters.RemainingSeconds == 0f && !Boosters.DustActive && !Boosters.AnyActive);
        Check("what it spent is in the prefs (restart-proof)", PlayerPrefs.GetInt(Boosters.DustUsedMsKey) == 300000);
        TouchInput.PressedOverride = null;
        EndRun(s);

        // the menu: no run, no ticking
        Reset(5000f);
        Boosters.BuyDustBooster();
        Boosters.TestDelta = 1f;
        Check("no run, no ticking (Tick is only called by a running real run)", Mathf.Approximately(Boosters.RemainingSeconds, 300f));

        // practice run: never ticks, never multiplies
        Reset(5000f);
        Boosters.BuyDustBooster();
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        PlayerPrefs.SetString("HasDoneTut", "false");
        startMenu.youAreInTutorial = false;
        s = MakeScore();
        s.SendMessage("Awake");
        s.SendMessage("Start");
        Boosters.TestDelta = 1f;
        TouchInput.PressedOverride = () => true;
        s.SendMessage("Update");
        score.AwardStarDust(1f);
        Check("a practice (tutorial) run neither ticks nor multiplies", Mathf.Approximately(Boosters.RemainingSeconds, 300f) &&
              score.tutorialCurrency >= 1f && score.tutorialCurrency < 1.5f);
        Check("... the multiplier reads 1 there", Boosters.RunDustMultiplier == 1f);
        TouchInput.PressedOverride = null;
        EndRun(s);
        PlayerPrefs.SetString("HasDoneTut", "true");
    }

    // ---- 5 ----
    static void Multiplies()
    {
        Reset(5000f);
        var s = BeginRealRun(5000f);
        score.AwardStarDust(1f);
        float plain = StarDustLedger.Earned;
        Boosters.BuyDustBooster();   // refused inside a run: give the time by prefs instead
        PlayerPrefs.SetInt(Boosters.DustBoughtKey, 1);
        Boosters.Reload();
        Boosters.ResetRun();
        float before = StarDustLedger.Earned;
        score.AwardStarDust(1f);
        float rewards = StarDustLedger.Earned;
        score.AwardRewardDust(15f);
        float afterElite = StarDustLedger.Earned;
        Check("without the booster a pickup pays 1 (" + plain + ")", Mathf.Approximately(plain, 1f));
        Check("with it a pickup pays 2", Mathf.Approximately(rewards - before, 2f));
        Check("with it an elite reward pays 2 x 9", Mathf.Approximately(afterElite - rewards, 18f));
        Check("the run knows what the booster added (for the ad and the leaderboard)", Mathf.Approximately(Boosters.RunBoostExtra, 1f + 9f));
        Check("leaderboard dust excludes it", Mathf.Approximately(Boosters.RankedDust(StarDustLedger.Earned), StarDustLedger.Earned - 10f));

        float b2 = StarDustLedger.Earned;
        Boosters.AdDoubled = true;
        score.AwardStarDust(1f);
        Check("booster + rewarded ad pays 3x, not 4x", Mathf.Approximately(StarDustLedger.Earned - b2, 3f));
        Boosters.AdDoubled = false;

        float bal = StarDustLedger.Balance;
        StarDustLedger.Grant(5f);
        Check("a claim (achievement / Codex) is never multiplied", Mathf.Approximately(StarDustLedger.Balance - bal, 5f));

        // score
        int scoreBefore = (int)RunScore.Total;
        score.AwardStarDust(100f);
        Check("dust never changes the run score", (int)RunScore.Total == scoreBefore);
        string logic = System.IO.File.ReadAllText("Assets/Scripts/Core/score.cs");
        Check("the dust multiplier is applied in exactly the dust paths (pickup/reward, flight)", logic.Split(new[] { "Boosters.ApplyToAward" }, System.StringSplitOptions.None).Length - 1 == 2);
        string lb = System.IO.File.ReadAllText("Assets/Scripts/Core/Leaderboards/LeaderboardRunTracker.cs");
        Check("the leaderboard tracker reads un-boosted dust", lb.Contains("Boosters.RankedDust(StarDustLedger.Earned)"));
        EndRun(s);
        PlayerPrefs.SetInt(Boosters.DustBoughtKey, 0);
    }

    class FakeAttackTarget : MonoBehaviour, IShipAttackTarget
    {
        public void TakeShipAttack(int ship, float weight, Vector3 at) { }
    }

    static GameObject Body(string name, string tag, Vector3 at)
    {
        var go = new GameObject(name);
        go.tag = tag;
        go.transform.position = at;
        ClearTarget.Ensure(go);
        return go;
    }

    // ---- 6 ----
    static void Clearing()
    {
        Reset(5000f);
        Boosters.BuyHazardClear();
        var s = BeginRealRun(5000f);
        HazardClear.BeginRun();
        var enemy = Body("Fighter", "Enimey", new Vector3(0f, 1f, 0f));
        var mine = Body("RailMine", "Enimey", new Vector3(1f, 2f, 0f));
        var rock = Body("Rock", "Astr", new Vector3(-1f, 0f, 0f));
        var boss = Body("Boss_Body", "Enimey", new Vector3(0f, 3f, 0f));
        var armoured = Body("Armoured", "Enimey", new Vector3(2f, 3f, 0f));
        armoured.AddComponent<FakeAttackTarget>();
        var pickup = Body("Star", "pickUp", new Vector3(0f, -1f, 0f));
        Check("clearable: ordinary bodies and rocks; not a boss part, an attack target or a pickup",
              HazardClear.IsClearable(enemy.GetComponent<ClearTarget>()) && HazardClear.IsClearable(mine.GetComponent<ClearTarget>()) &&
              HazardClear.IsClearable(rock.GetComponent<ClearTarget>()) && !HazardClear.IsClearable(boss.GetComponent<ClearTarget>()) &&
              !HazardClear.IsClearable(armoured.GetComponent<ClearTarget>()) && !HazardClear.IsClearable(pickup.GetComponent<ClearTarget>()));

        int scoreBefore = (int)RunScore.Total, killsBefore = RunScore.Parts.killCount, chainBefore = RunScore.Chain;
        float earnedBefore = StarDustLedger.Earned;
        int pickupsBefore = score.dustPickups;
        Check("a charge is held and the clear is allowed", Boosters.ClearCharges == 1 && HazardClear.BlockedBy() == HazardClear.Block.None);
        bool fired = HazardClear.AutoSave(Vector3.zero);
        Check("it fires and removes the three bodies", fired && HazardClear.LastBodies == 3 && enemy == null && mine == null && rock == null);
        Check("... leaving the boss part, the attack target and the pickup", boss != null && armoured != null && pickup != null);
        Check("... and spends the charge", Boosters.ClearCharges == 0 && Boosters.UsedClear == 1 && HazardClear.UsedThisRun == 1);
        Check("NO credit: score, kills, chain, dust, pickups unchanged",
              (int)RunScore.Total == scoreBefore && RunScore.Parts.killCount == killsBefore && RunScore.Chain == chainBefore &&
              Mathf.Approximately(StarDustLedger.Earned, earnedBefore) && score.dustPickups == pickupsBefore);
        string src = System.IO.File.ReadAllText("Assets/Scripts/Boosters/HazardClear.cs");
        string code = string.Join("\n", System.Array.FindAll(src.Split('\n'), l => !l.TrimStart().StartsWith("//")));
        Check("the clear's code never calls a kill award, score, codex or achievement path",
              !code.Contains("AwardDestroyedTarget") && !code.Contains("RunScore.") && !code.Contains("Codex.") && !code.Contains("AchievementTracker") && !code.Contains("AwardStarDust"));
        Check("the run still counts (a clear changes no run flag)", RunScore.Scoring && RunScore.SavesBest);

        PlayerPrefs.SetInt(Boosters.ClearBoughtKey, 2);   // a second charge (buying is refused inside a run)
        Boosters.Reload();
        Check("no cooldown: with i-frames guarding the repeat, a held charge is never refused (Block.None)", HazardClear.BlockedBy() == HazardClear.Block.None && Boosters.ClearCharges == 1);
        Object.DestroyImmediate(boss); Object.DestroyImmediate(armoured); Object.DestroyImmediate(pickup);

        HazardClear.BeginRun();
        buttonClicks.playerDied = true;
        Check("blocked when dead", HazardClear.BlockedBy() == HazardClear.Block.Dead);
        buttonClicks.playerDied = false;
        PlayerPrefs.SetInt(Boosters.ClearUsedKey, 2);
        Boosters.Reload();
        HazardClear.BeginRun();
        Check("no charge, no clear", HazardClear.BlockedBy() == HazardClear.Block.NoCharge);
        PlayerPrefs.SetInt(DeveloperUnlocks.EnabledKey, 1);
        Check("developer mode grants nothing: no charge, no clear, nothing spent", HazardClear.BlockedBy() == HazardClear.Block.NoCharge && !Boosters.ClearAvailable && !Boosters.ConsumeClear() && Boosters.UsedClear == 2);
        PlayerPrefs.SetInt(DeveloperUnlocks.EnabledKey, 0);
        EndRun(s);

        // the tutorial / practice
        PlayerPrefs.SetString("HasDoneTut", "false");
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        s = MakeScore();
        s.SendMessage("Awake");
        s.SendMessage("Start");
        PlayerPrefs.SetInt(Boosters.ClearBoughtKey, 3);
        Boosters.Reload();
        Check("not in practice runs", HazardClear.BlockedBy() == HazardClear.Block.NotInRun);
        EndRun(s);
        PlayerPrefs.SetString("HasDoneTut", "true");
    }

    // ---- 7 ----
    static void Cloud()
    {
        Reset(900f);
        PlayerPrefs.SetInt(Boosters.DustBoughtKey, 2);
        PlayerPrefs.SetInt(Boosters.DustUsedMsKey, 123456);
        PlayerPrefs.SetInt(Boosters.ClearBoughtKey, 4);
        PlayerPrefs.SetInt(Boosters.ClearUsedKey, 1);
        Boosters.Reload();
        var snap = ProgressSnapshot.Capture(1000);
        Check("capture reads the counters (new purchases in the 5-minute field)", snap.boostDustBought5m == 2 && snap.boostDustBought == 0 && snap.boostDustUsedMs == 123456 && snap.boostClearBought == 4 && snap.boostClearUsed == 1 && snap.HasProgress());
        ProgressSnapshot parsed;
        Check("they survive the JSON", ProgressSnapshot.TryParse(snap.ToJson(), out parsed) == ProgressSnapshot.ParseResult.Ok &&
              parsed.boostDustBought5m == 2 && parsed.boostDustUsedMs == 123456 && parsed.boostClearBought == 4 && parsed.boostClearUsed == 1);
        foreach (string k in Keys) PlayerPrefs.DeleteKey(k);
        parsed.Apply();
        Check("apply writes them back and reloads the cache", Boosters.BoughtDust == 2 && Boosters.UsedMs == 123456 && Boosters.ClearCharges == 3 &&
              Mathf.Approximately(Boosters.RemainingSeconds, 600f - 123.456f));

        var a = new ProgressSnapshot { savedAtUtc = 2000, currency = 500f, boostDustBought = 2, boostDustUsedMs = 100000, boostClearBought = 3, boostClearUsed = 1 };
        var b = new ProgressSnapshot { savedAtUtc = 1000, currency = 300f, boostDustBought = 1, boostDustUsedMs = 400000, boostClearBought = 2, boostClearUsed = 2 };
        var m = ProgressMerge.Merge(a, b);
        Check("merge: each counter is the larger one (never summed, never lowered)",
              m.boostDustBought == 2 && m.boostDustUsedMs == 400000 && m.boostClearBought == 3 && m.boostClearUsed == 2);
        Check("merge: held charges = 1, not 3 + 2 - 3", m.boostClearBought - m.boostClearUsed == 1);
        var again = ProgressMerge.Merge(m, b);
        var again2 = ProgressMerge.Merge(a, m);
        Check("merging again changes nothing (no duplicates, nothing resurrected)",
              again.boostDustBought == m.boostDustBought && again.boostDustUsedMs == m.boostDustUsedMs && again.boostClearBought == m.boostClearBought &&
              again.boostClearUsed == m.boostClearUsed && again2.boostClearBought == 3 && again2.boostClearUsed == 2 && again.currency == m.currency);
        Check("merge is symmetric", ProgressMerge.Merge(b, a).boostClearBought == m.boostClearBought && ProgressMerge.Merge(b, a).boostDustUsedMs == m.boostDustUsedMs);

        // a purchase only the older side has is paid for from the newer balance
        var newer = new ProgressSnapshot { savedAtUtc = 3000, currency = 1000f, boostDustBought = 0, boostClearBought = 1 };
        var older = new ProgressSnapshot { savedAtUtc = 1000, currency = 5000f, boostDustBought = 1, boostClearBought = 3 };
        var paid = ProgressMerge.Merge(newer, older);
        float owed = Boosters.DustBoosterPrice + 2f * Boosters.HazardClearPrice;
        Check("a booster bought only on the other device is charged on merge (" + owed + ")",
              Mathf.Approximately(paid.currency, Mathf.Max(0f, 1000f - owed)) && paid.boostDustBought == 1 && paid.boostClearBought == 3);
        Check("two equal sides owe nothing", Mathf.Approximately(ProgressMerge.UnpaidBoosters(a, a), 0f));
        Reset(0f);
    }

    // ---- 8: the active-booster column (BoosterHud / BoosterHudSource) ----
    static Rect AuthoredHud(TopBand.Frame band, Vector2 screen)
    {
        float hudScale = HudStyler.ScaleWithScreenSize(screen, new Vector2(800f, 600f), CanvasScaler.ScreenMatchMode.MatchWidthOrHeight, 0f);
        return HudStyler.HudScreenRect(band, screen, hudScale, new Vector2(351f, 131f));
    }

    static void Control()
    {
        // ---- placement on every ScreenFit device (tall phones, the Flip7 cover screen, foldables, tablets), with and without rails ----
        bool allFit = true, noClash = true, topHalf = true, apart = true, crisp = true, small = true;
        string firstBad = null;
        foreach (var d in FitDevice.All)
        {
            var screen = new Vector2(d.w, d.h);
            Rect safe = d.Safe;
            foreach (float rail in new[] { 0f, 2.2f })
            {
                var band = TopBand.FrameFor(safe, screen, rail, d.Cutouts);
                Rect hud = AuthoredHud(band, screen);
                Rect actions = PauseQuickActions.ScreenRectFor(band, screen);
                var warning = BossWarningHud.ComputeLayout(safe, screen, hud, band);
                var rects = new Rect[BoosterHud.MaxSlots];
                for (int i = 0; i < rects.Length; i++)
                {
                    Rect r = rects[i] = BoosterHud.SlotRect(band, safe, screen, hud, i);
                    Rect icon = BoosterHud.IconRect(r, screen);
                    bool fit = r.xMin >= safe.xMin && r.xMax <= safe.xMax && r.yMin >= safe.yMin && r.yMax <= safe.yMax && r.xMax <= band.right;
                    float portalScale = Mathf.Min(screen.x / 800f, screen.y / 1200f);
                    bool clash = r.Overlaps(actions) || r.Overlaps(warning.chip) || r.Overlaps(warning.banner) || (hud.height > 0f && r.Overlaps(hud)) ||
                                 r.Overlaps(PortalPressureHud.ChipScreenRect(band, portalScale));
                    bool high = r.yMin >= screen.y * BoosterHud.LowestShare - .5f;
                    for (int j = 0; j < i; j++) apart &= !r.Overlaps(rects[j]);
                    allFit &= fit;
                    noClash &= !clash;
                    topHalf &= high;
                    crisp &= (icon.width % BoosterHud.ArtGrid == 0 || icon.width == BoosterHud.ArtGrid / 2) && icon.width == icon.height && icon.x == Mathf.Round(icon.x) && icon.y == Mathf.Round(icon.y)
                             && icon.xMin >= r.xMin && icon.xMax <= r.xMax && icon.yMin >= r.yMin;
                    small &= icon.width <= screen.x * BoosterHud.MaxWidthShare;
                    if ((!fit || clash || !high) && firstBad == null) firstBad = d.id + " rail " + rail + " slot " + i + " " + r;
                }
            }
        }
        Check("the booster column sits inside the safe area and the top band's lane on every device" + (firstBad != null ? " (first bad: " + firstBad + ")" : ""), allFit);
        Check("... clear of the Replay/Home pair, the boss chip and banner, PORTAL DANGER's chip and the score read-out", noClash);
        Check("... never in the bottom " + BoosterHud.LowestShare.ToString("0%") + " of the screen (the ship and the thumb), wrapping into a second column if it must", topHalf);
        Check("... slots never overlap each other", apart);
        Check("... icons drawn at a whole multiple of the art's 64 px grid (half on a very narrow screen) on whole pixels (crisp, point filtered)", crisp);
        Check("... and they are small (at most a tenth of the screen's width)", small);

        string hudSrc = System.IO.File.ReadAllText("Assets/Scripts/Boosters/BoosterHud.cs");
        string move = System.IO.File.ReadAllText("Assets/Scripts/Ship/movePlayer.cs");
        string scoreSrc = System.IO.File.ReadAllText("Assets/Scripts/Core/score.cs");
        Check("the run scene only (gameS1, never the tutorial)", hudSrc.Contains("scene.name != \"gameS1\""));
        Check("the column is not interactive: no Button, no GraphicRaycaster, every graphic raycastTarget off, canvas group blocks nothing",
              !hudSrc.Contains("typeof(Button)") && !hudSrc.Contains("typeof(GraphicRaycaster)") &&
              hudSrc.Contains("slot.icon.raycastTarget = false") && hudSrc.Contains("slot.label.raycastTarget = false") &&
              hudSrc.Contains("group.blocksRaycasts = false"));
        Check("a press on the column is an ordinary press: movePlayer and score.Update never ask about it",
              !move.Contains("BoosterHud") && !scoreSrc.Contains("BoosterHud") && move.Contains("PauseQuickActions.IsScreenPointOnAction(TouchInput.Position)"));
        Check("one system: the old Hazard Clear readout is gone", !System.IO.File.Exists("Assets/Scripts/Boosters/HazardClearReadout.cs") &&
              System.Type.GetType("HazardClearReadout, Assembly-CSharp") == null);
        var tallScreen = new Vector2(1080f, 2400f);
        var tallSafe = new Rect(0f, 72f, 1080f, 2256f);
        var tallBand = TopBand.FrameFor(tallSafe, tallScreen, 0f, new Rect[0]);
        Vector2 centre = BoosterHud.SlotRect(tallBand, tallSafe, tallScreen, AuthoredHud(tallBand, tallScreen), 0).center;
        Check("... and its centre is on no quick action (so the teleport press is not swallowed)", !PauseQuickActions.IsScreenPointOnAction(centre));

        foreach (var k in BoosterHudSource.Order)
        {
            var sprite = ShopIconArt.Get(BoosterHudSource.IconName(k));
            Check("the " + k + " icon is the Shop's own icon, point filtered", sprite != null && sprite.texture.filterMode == FilterMode.Point &&
                  sprite == ShopIconArt.Get(ShopIconArt.NameFor(new ShopRow { kind = k == Boosters.Kind.DustMultiplier ? ShopRowKind.DustBooster :
                                                                                    k == Boosters.Kind.HomingAtoms ? ShopRowKind.HomingAtoms : ShopRowKind.HazardClear })));
        }

        HudBehaviour();
    }

    static List<BoosterHudSource.Entry> HudEntries()
    {
        var list = new List<BoosterHudSource.Entry>();
        BoosterHudSource.Read(list);
        return list;
    }

    static bool Has(List<BoosterHudSource.Entry> list, Boosters.Kind k, out BoosterHudSource.Entry e)
    {
        foreach (var x in list) if (x.kind == k) { e = x; return true; }
        e = default(BoosterHudSource.Entry);
        return false;
    }

    // What the column shows, and when (the adapter and the live component).
    static void HudBehaviour()
    {
        BoosterHudSource.Entry e;
        string full = Boosters.FormatTime(Boosters.DustBoosterSeconds), fullHoming = Boosters.FormatTime(Boosters.HomingSeconds);
        string twoLess = Boosters.FormatTime(Boosters.DustBoosterSeconds - 2f);
        Reset(10000f);
        Boosters.BuyDustBooster();
        Boosters.BuyHazardClear();
        Boosters.BuyHazardClear();
        var s = BeginRealRun(PlayerPrefs.GetFloat(StarDustLedger.CurrencyKey));
        var list = HudEntries();
        Check("a real run shows Dust x2 with its countdown (" + full + ") and Hazard Clear with its count (x2), homing (not bought) does not",
              list.Count == 2 && Has(list, Boosters.Kind.DustMultiplier, out e) && e.timed && e.Label == full &&
              Has(list, Boosters.Kind.HazardClear, out e) && !e.timed && e.Label == "x2" && !Has(list, Boosters.Kind.HomingAtoms, out e));
        Check("... in order: timed first, then the held charges", list.Count == 2 && list[0].kind == Boosters.Kind.DustMultiplier && list[1].kind == Boosters.Kind.HazardClear);

        // the live column builds and shows them, with the Shop icons and the labels
        var host = new GameObject(BoosterHud.HostName);
        var hud = host.AddComponent<BoosterHud>();
        hud.SendMessage("Update");
        var images = host.GetComponentsInChildren<Image>(false);
        var texts = host.GetComponentsInChildren<Text>(false);
        bool dustIcon = false, clearIcon = false, dustLabel = false, clearLabel = false;
        foreach (var im in images)
        {
            dustIcon |= im.sprite != null && im.sprite == ShopIconArt.Get("booster_dust_x2");
            clearIcon |= im.sprite != null && im.sprite == ShopIconArt.Get("booster_hazard_clear");
        }
        foreach (var t in texts) { dustLabel |= t.text == full; clearLabel |= t.text == "x2"; }
        Check("the live column shows two icons (the Shop's Dust x2 and Hazard Clear art) with its countdown and \"x2\" under them",
              hud.VisibleCount == 2 && images.Length == 2 && dustIcon && clearIcon && dustLabel && clearLabel);
        bool inert = host.GetComponentsInChildren<GraphicRaycaster>(true).Length == 0 && host.GetComponentsInChildren<Button>(true).Length == 0;
        foreach (var g in host.GetComponentsInChildren<Graphic>(true)) inert &= !g.raycastTarget;
        Check("... and none of it takes a touch", inert);

        // the countdown is the booster clock: frozen frames hold it, running frames spend it
        Boosters.TestDelta = 1f;
        score.pauseCounter = 5;
        TouchInput.PressedOverride = () => false;   // the finger is up: the world is frozen
        for (int i = 0; i < 5; i++) { s.SendMessage("Update"); hud.SendMessage("Update"); }
        Check("a frozen world freezes the countdown (still " + full + ")", Has(HudEntries(), Boosters.Kind.DustMultiplier, out e) && e.Label == full);
        TouchInput.PressedOverride = () => true;
        buttonClicks.playerDied = true;
        s.SendMessage("Update");
        buttonClicks.playerDied = false;
        s.SendMessage("Update");
        s.SendMessage("Update");
        hud.SendMessage("Update");
        bool ticked = false;
        foreach (var t in host.GetComponentsInChildren<Text>(false)) ticked |= t.text == twoLess;
        Check("running frames count it down (" + twoLess + " after two 1 s frames), on the live label too", Has(HudEntries(), Boosters.Kind.DustMultiplier, out e) && e.Label == twoLess && ticked);

        // a booster the pilot switched off never shows
        Boosters.SetOn(Boosters.Kind.DustMultiplier, false);
        hud.SendMessage("Update");
        Check("a booster switched off in the Shop does not show", !Has(HudEntries(), Boosters.Kind.DustMultiplier, out e) && hud.VisibleCount == 1);
        Boosters.SetOn(Boosters.Kind.HazardClear, false);
        hud.SendMessage("Update");
        Check("... Hazard Clear switched off does not show either", HudEntries().Count == 0 && hud.VisibleCount == 0);
        Boosters.SetOn(Boosters.Kind.DustMultiplier, true);
        Boosters.SetOn(Boosters.Kind.HazardClear, true);
        hud.SendMessage("Update");
        Check("... and both show again when switched back on, the time left where it was", hud.VisibleCount == 2 &&
              Has(HudEntries(), Boosters.Kind.DustMultiplier, out e) && e.Label == twoLess);

        // a charge spent: the count drops; the last one spent: the icon goes
        Boosters.ConsumeClear();
        Check("a spent charge drops the count (x1)", Has(HudEntries(), Boosters.Kind.HazardClear, out e) && e.Label == "x1");
        Boosters.ConsumeClear();
        hud.SendMessage("Update");
        Check("... and the last one spent removes the icon", !Has(HudEntries(), Boosters.Kind.HazardClear, out e) && hud.VisibleCount == 1);

        // the time runs out: the icon goes
        Boosters.TestDelta = 100000f;
        s.SendMessage("Update");
        hud.SendMessage("Update");
        Check("the timer running out removes the icon (nothing left showing)", HudEntries().Count == 0 && hud.VisibleCount == 0 &&
              host.GetComponentsInChildren<Image>(false).Length == 0);

        // homing appears when bought; never for a dead pilot
        Boosters.TestDelta = null;
        TouchInput.PressedOverride = null;
        Object.DestroyImmediate(host);
        EndRun(s);
        Reset(10000f);
        Boosters.BuyHoming();
        s = BeginRealRun(PlayerPrefs.GetFloat(StarDustLedger.CurrencyKey));   // a new scene: a new column
        host = new GameObject(BoosterHud.HostName);
        hud = host.AddComponent<BoosterHud>();
        hud.SendMessage("Update");
        Check("homing atoms shows with its countdown", Has(HudEntries(), Boosters.Kind.HomingAtoms, out e) && e.timed && e.Label == fullHoming && hud.VisibleCount == 1);
        buttonClicks.playerDied = true;
        hud.SendMessage("Update");
        Check("a dead pilot sees no booster icons", HudEntries().Count == 0 && hud.VisibleCount == 0);
        buttonClicks.playerDied = false;
        startMenu.youAreInTutorial = true;
        Check("the tutorial shows none", HudEntries().Count == 0);
        startMenu.youAreInTutorial = false;
        Object.DestroyImmediate(host);
        EndRun(s);
        Reset(0f);
    }

    // ---- 6b: the automatic save in the one place a heart is lost (collisionDetection.OnTriggerEnter2D) ----
    sealed class Rig
    {
        public GameObject ship;
        public collisionDetection cd;
        public Action<Collider2D> trigger;

        public Rig(int maxLife)
        {
            ship = new GameObject(ShipId.ObjectName(1) + "(Clone)", typeof(SpriteRenderer));
            ship.AddComponent<Rigidbody2D>().bodyType = RigidbodyType2D.Kinematic;
            ship.AddComponent<BoxCollider2D>().isTrigger = true;
            cd = ship.AddComponent<collisionDetection>();
            cd.explosionAnimation = new GameObject("~TestExplosion");
            cd.boostSound = ship.AddComponent<AudioSource>();
            cd.boostText = new GameObject("~boostText", typeof(RectTransform)).AddComponent<Text>();
            cd.hypeText = new GameObject("~hypeText", typeof(RectTransform)).AddComponent<Text>();
            cd.boost = new GameObject("~boost");
            cd.boost.SetActive(false);
            trigger = (Action<Collider2D>)Delegate.CreateDelegate(typeof(Action<Collider2D>), cd,
                typeof(collisionDetection).GetMethod("OnTriggerEnter2D", BindingFlags.NonPublic | BindingFlags.Instance));
            Reset(maxLife);
        }

        public void Reset(int maxLife)
        {
            collisionDetection.MAXLIFE = maxLife;
            collisionDetection.lifeCounter = 0;
            collisionDetection.atomCheck = false;
            collisionDetection.invTimer = 0f;
            collisionDetection.cloakTimer = 0f;
            PlayerInvuln.Reset();
            buttonClicks.playerDied = false;
        }

        public GameObject Hit(string name = "Rock", string tag = "Astr")
        {
            var go = new GameObject(name, typeof(CircleCollider2D));
            go.tag = tag;
            go.transform.position = ship.transform.position + Vector3.right * .3f;
            ClearTarget.Ensure(go);
            trigger(go.GetComponent<Collider2D>());
            return go;
        }

        public void Dispose()
        {
            foreach (var go in new[] { cd.explosionAnimation, cd.boost, cd.boostText.gameObject, cd.hypeText.gameObject })
                if (go != null) Object.DestroyImmediate(go);
            if (ship != null) Object.DestroyImmediate(ship);
            foreach (var name in new[] { "~fx", "~TestExplosion(Clone)", "~PickupBurst" })
                for (var go = GameObject.Find(name); go != null; go = GameObject.Find(name))
                    Object.DestroyImmediate(go);
            Reset(3);
        }
    }

    static void Charges(int held)
    {
        PlayerPrefs.SetInt(Boosters.ClearBoughtKey, held);
        PlayerPrefs.SetInt(Boosters.ClearUsedKey, 0);
        Boosters.Reload();
    }

    static void AutoSave()
    {
        Reset(5000f);
        var s = BeginRealRun(5000f);
        HazardClear.BeginRun();
        var rig = new Rig(3);
        try
        {
            // a charge fires, the heart is kept
            Charges(2);
            int scoreBefore = (int)RunScore.Total, killsBefore = RunScore.Parts.killCount;
            float dustBefore = StarDustLedger.Earned;
            var rock = rig.Hit();
            Check("a hit with a charge held: the heart is kept, one charge spent, the hit source cleared",
                  collisionDetection.lifeCounter == 0 && Boosters.ClearCharges == 1 && HazardClear.UsedThisRun == 1 && !buttonClicks.playerDied);
            Check("... post-hit i-frames start, and no credit is paid",
                  PlayerInvuln.Active && (int)RunScore.Total == scoreBefore && RunScore.Parts.killCount == killsBefore && Mathf.Approximately(StarDustLedger.Earned, dustBefore));
            if (rock != null) Object.DestroyImmediate(rock);

            // the i-frames stop the same hazard hitting again: nothing more is spent, no heart lost
            var again = rig.Hit();
            Check("a second hit inside the i-frames spends nothing and costs nothing", collisionDetection.lifeCounter == 0 && Boosters.ClearCharges == 1);
            if (again != null) Object.DestroyImmediate(again);

            // a shielded hit spends nothing
            rig.Reset(3);
            collisionDetection.atomCheck = true;
            var shielded = rig.Hit();
            Check("a shielded hit (blue atom) spends no charge", Boosters.ClearCharges == 1 && collisionDetection.lifeCounter == 0 && HazardClear.UsedThisRun == 1);
            if (shielded != null) Object.DestroyImmediate(shielded);
            rig.Reset(3);

            // i-frames from a lost heart spend nothing
            PlayerInvuln.BeginPostHit();
            var inv = rig.Hit();
            Check("a hit during i-frames spends no charge", Boosters.ClearCharges == 1 && collisionDetection.lifeCounter == 0 && HazardClear.UsedThisRun == 1);
            if (inv != null) Object.DestroyImmediate(inv);
            rig.Reset(3);

            // no charge: the heart is lost
            Charges(0);
            var hurt = rig.Hit();
            Check("no charge: the heart is lost as before, i-frames begin", collisionDetection.lifeCounter == 1 && PlayerInvuln.Active);
            if (hurt != null) Object.DestroyImmediate(hurt);
            rig.Reset(3);

            // developer mode with no charges does not fire
            PlayerPrefs.SetInt(DeveloperUnlocks.EnabledKey, 1);
            var dev = rig.Hit();
            Check("developer mode with no charges does not save the heart and spends nothing", collisionDetection.lifeCounter == 1 && Boosters.UsedClear == 0);
            if (dev != null) Object.DestroyImmediate(dev);
            rig.Reset(3);
            Charges(1);
            var devHeld = rig.Hit();
            Check("developer mode with a real held charge fires and spends it", collisionDetection.lifeCounter == 0 && Boosters.ClearCharges == 0);
            if (devHeld != null) Object.DestroyImmediate(devHeld);
            PlayerPrefs.SetInt(DeveloperUnlocks.EnabledKey, 0);
            rig.Reset(3);

            // the fatal hit is saved
            Charges(1);
            rig.Reset(1);   // one heart: any hit is fatal
            var fatal = rig.Hit();
            Check("the hit that would be fatal is saved: alive, heart kept, charge spent", !buttonClicks.playerDied && collisionDetection.lifeCounter == 0 && Boosters.ClearCharges == 0 && !DeathCrash.Running);
            if (fatal != null) Object.DestroyImmediate(fatal);
            rig.Reset(3);

            // a boss-tagged source: the hit is absorbed, the boss is not removed
            Charges(1);
            var boss = rig.Hit("Boss_Body", "Enimey");
            Check("a boss body that hits is absorbed (heart kept, charge spent) and is not removed", collisionDetection.lifeCounter == 0 && Boosters.ClearCharges == 0 && boss != null);
            if (boss != null) Object.DestroyImmediate(boss);
            rig.Reset(3);

            // a scripted block lets the hit land
            Charges(1);
            var prop = typeof(ShipPowerController).GetProperty("CinematicClearActive");
            prop.GetSetMethod(true).Invoke(null, new object[] { true });
            var scripted = rig.Hit();
            prop.GetSetMethod(true).Invoke(null, new object[] { false });
            Check("scripted (cinematic) block: the hit lands, no charge spent", collisionDetection.lifeCounter == 1 && Boosters.ClearCharges == 1);
            if (scripted != null) Object.DestroyImmediate(scripted);
        }
        finally
        {
            rig.Dispose();
            Charges(0);
            EndRun(s);
        }

        // tutorial / practice: the hit lands, nothing spent
        PlayerPrefs.SetString("HasDoneTut", "false");
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        s = MakeScore();
        s.SendMessage("Awake");
        s.SendMessage("Start");
        Charges(3);
        var prac = new Rig(3);
        try
        {
            var h = prac.Hit();
            Check("practice run: the hit lands (no automatic save), no charge spent", collisionDetection.lifeCounter == 1 && Boosters.ClearCharges == 3);
            if (h != null) Object.DestroyImmediate(h);
        }
        finally
        {
            prac.Dispose();
            Charges(0);
            EndRun(s);
            PlayerPrefs.SetString("HasDoneTut", "true");
        }
        string cd = System.IO.File.ReadAllText("Assets/Scripts/Ship/collisionDetection.cs");
        Check("the hook sits in the one place a heart is lost (the hit branch of collisionDetection, before `fatal` is decided)",
              cd.Contains("HazardClear.AutoSave(") && cd.IndexOf("HazardClear.AutoSave(", StringComparison.Ordinal) < cd.IndexOf("bool fatal =", StringComparison.Ordinal) &&
              cd.Split(new[] { "lifeCounter += 1" }, StringSplitOptions.None).Length == 2);
    }

    // ---- 9 ----
    static void Homing()
    {
        // buying
        Reset(999f);
        Check("homing: 999 dust cannot buy it", Boosters.BuyHoming() == Boosters.BuyResult.NotEnoughDust && !Boosters.HomingActive);
        Reset(5000f);
        Check("homing: costs 1,000 and gives 5 minutes", Boosters.Buy(Boosters.Kind.HomingAtoms) == Boosters.BuyResult.Bought &&
              Mathf.Approximately(PlayerPrefs.GetFloat(StarDustLedger.CurrencyKey), 4000f) &&
              Mathf.Approximately(Boosters.HomingRemainingSeconds, 300f) && Boosters.Status(Boosters.Kind.HomingAtoms) == "5:00 left");
        Check("homing: its time does NOT switch ads off and is not the dust multiplier", !Boosters.AnyActive && !Boosters.DustActive);
        Boosters.Reload();
        Check("homing: survives a restart", Boosters.BoughtHoming == 1 && Boosters.HomingActive);
        Check("homing: buying again adds 5 minutes", Boosters.BuyHoming() == Boosters.BuyResult.Bought &&
              Mathf.Approximately(Boosters.HomingRemainingSeconds, 600f));
        Reset(100000f);
        int bought = 0;
        while (Boosters.BuyHoming() == Boosters.BuyResult.Bought && bought < 30) bought++;
        Check("homing: stored time stops at an hour", bought == 12 && Boosters.AtLimit(Boosters.Kind.HomingAtoms) && !Boosters.CanBuy(Boosters.Kind.HomingAtoms));
        Reset(5000f);
        StarDustLedger.BeginRun(true);
        Check("homing: no buying inside a run", Boosters.BuyHoming() == Boosters.BuyResult.RunInProgress);
        StarDustLedger.EndRun(StarDustLedger.RunId);

        // the clock: real-run frames spend it, alongside Dust x2 but on its own counter
        Reset(5000f);
        Boosters.BuyHoming();
        var s = BeginRealRun(5000f);
        Check("homing: runs in a real run", Boosters.HomingRunning);
        Boosters.TestDelta = 1f;
        score.pauseCounter = 5;
        TouchInput.PressedOverride = () => false;
        s.SendMessage("Update");
        Check("homing: a frozen world does not spend it", Mathf.Approximately(Boosters.HomingRemainingSeconds, 300f));
        TouchInput.PressedOverride = () => true;
        s.SendMessage("Update");
        s.SendMessage("Update");
        Check("homing: running frames spend it (298 s left) and latch the run", Mathf.Approximately(Boosters.HomingRemainingSeconds, 298f) &&
              Boosters.RunHomingUsed && !Boosters.RunDustUsed);
        Boosters.TestDelta = 10000f;
        s.SendMessage("Update");
        Check("homing: runs out at zero, saved in the prefs", !Boosters.HomingActive && !Boosters.HomingRunning &&
              PlayerPrefs.GetInt(Boosters.HomingUsedMsKey) == 300000);
        TouchInput.PressedOverride = null;
        Boosters.TestDelta = null;
        EndRun(s);

        // cloud counters
        Reset(5000f);
        PlayerPrefs.SetInt(Boosters.HomingBoughtKey, 3);
        PlayerPrefs.SetInt(Boosters.HomingUsedMsKey, 65432);
        Boosters.Reload();
        var snap = ProgressSnapshot.Capture(1000);
        ProgressSnapshot parsed;
        bool parsedOk = ProgressSnapshot.TryParse(snap.ToJson(), out parsed) == ProgressSnapshot.ParseResult.Ok;
        Check("homing: capture + JSON carry both counters", snap.boostHomingBought5m == 3 && snap.boostHomingUsedMs == 65432 && snap.HasProgress() &&
              parsedOk && parsed.boostHomingBought5m == 3 && parsed.boostHomingUsedMs == 65432);
        foreach (string k in Keys) PlayerPrefs.DeleteKey(k);
        parsed.Apply();
        Check("homing: apply writes them back", Boosters.BoughtHoming == 3 && Boosters.UsedHomingMs == 65432);
        var a = new ProgressSnapshot { savedAtUtc = 2000, currency = 3000f, boostHomingBought = 1, boostHomingUsedMs = 500000 };
        var b = new ProgressSnapshot { savedAtUtc = 1000, currency = 300f, boostHomingBought = 2, boostHomingUsedMs = 100000 };
        var m = ProgressMerge.Merge(a, b);
        Check("homing: merge keeps the larger of each counter", m.boostHomingBought == 2 && m.boostHomingUsedMs == 500000);
        Check("homing: a purchase only the older side has is paid for", Mathf.Approximately(ProgressMerge.UnpaidBoosters(a, b), Boosters.HomingPrice));

        // ---- the flight (pure) ----
        const float dt = 1f / 60f;
        Vector2 still = new Vector2(0f, -4f);
        var h = new AtomHoming();
        Vector3 p = new Vector3(0f, 5f);
        float t = 0f;
        while (t < 4f && Vector2.Distance(p, still) > .01f) { p = h.Advance(p, dt, .1f, still); t += dt; }
        Check("flight: a still ship is reached (" + t.ToString("0.00") + " s)", !h.Missed && Vector2.Distance(p, still) <= .01f && t < 2.5f);

        h = new AtomHoming();
        p = new Vector3(0f, 5f);
        Vector2 ship = still;
        bool moved = false;
        float closest = 99f;
        for (t = 0f; t < 4f && !h.Missed; t += dt)
        {
            if (!moved && Vector2.Distance(p, ship) < 1.5f) { ship = new Vector2(2f, -4f); moved = true; }
            p = h.Advance(p, dt, .1f, ship);
            closest = Mathf.Min(closest, Vector2.Distance(p, ship));
        }
        Check("flight: a 2-unit side-step 1.5 units out makes it miss (closest " + closest.ToString("0.00") + ")", moved && h.Missed && closest > .8f);
        Vector3 at = p;
        Check("flight: a miss is final (it no longer moves)", h.Advance(p, dt, .1f, ship) == at);

        h = new AtomHoming();
        p = h.Advance(new Vector3(0f, 5f), dt, .1f, still);
        AtomHoming.OnTeleport();
        at = p;
        p = h.Advance(p, dt, .1f, still);
        Check("flight: a teleport breaks the lock", h.Missed && p == at);
        Check("flight: a new lock after the teleport homes again", !new AtomHoming().Advance(at, dt, .1f, still).Equals(at));

        h = new AtomHoming { elapsed = AtomHoming.MaxSeconds };
        h.Advance(new Vector3(0f, 5f), dt, .1f, still);
        Check("flight: it gives up after MaxSeconds", h.Missed);
        Check("flight: never slower than the atom's own fall in a fast world", AtomHoming.SpeedFor(1f) >= AtomWander.Descent(1f));

        // ---- the mover: atoms lock on, star dust does not ----
        Reset(5000f);
        Boosters.BuyHoming();
        s = BeginRealRun(5000f);
        var target = new GameObject("~homingShip").transform;
        target.position = still;
        moveItemEnmInStrightLine.HomingTargetOverride = target;
        var atom = new GameObject("~homingAtom");
        atom.AddComponent<AtomSpin>();
        var atomMover = atom.AddComponent<moveItemEnmInStrightLine>();
        atom.transform.position = new Vector3(0f, 7f);
        atomMover.Step(dt, .1f, 6f, -6f);
        Check("mover: an atom above the view does not lock yet", atomMover.Homing == null);
        atom.transform.position = new Vector3(0f, 5f);
        atomMover.Step(dt, .1f, 6f, -6f);
        Check("mover: an atom on screen locks onto the ship and flies at it", atomMover.Homing != null && !atomMover.Homing.Missed &&
              atom.transform.position.y < 5f);
        var dust = new GameObject("~homingDust");
        var dustMover = dust.AddComponent<moveItemEnmInStrightLine>();
        dust.transform.position = new Vector3(1f, 5f);
        dustMover.Step(dt, .1f, 6f, -6f);
        Check("mover: star dust (no AtomSpin) never homes", dustMover.Homing == null && Mathf.Approximately(dust.transform.position.x, 1f));
        AtomHoming.OnTeleport();
        atomMover.Step(dt, .1f, 6f, -6f);
        Check("mover: after a miss the atom wanders on", atomMover.Homing.Missed && atomMover.Wander != null);
        var homingBefore = atomMover.Homing;
        atomMover.Step(dt, .1f, 6f, -6f);
        Check("mover: one lock per atom (never re-locks)", atomMover.Homing == homingBefore && atomMover.Homing.Missed);
        Object.DestroyImmediate(atom);
        Object.DestroyImmediate(dust);
        Object.DestroyImmediate(target.gameObject);
        moveItemEnmInStrightLine.HomingTargetOverride = null;
        EndRun(s);
    }
    // ---- 10 ----
    static void Switch()
    {
        int changed = 0;
        System.Action onChanged = () => changed++;
        Boosters.Changed += onChanged;
        try
        {
            Reset(5000f);
            Check("switch: nothing held -> switching off is refused and it stays on",
                  !Boosters.SetOn(Boosters.Kind.DustMultiplier, false) && Boosters.IsOn(Boosters.Kind.DustMultiplier) &&
                  !Boosters.Owns(Boosters.Kind.DustMultiplier) && !Boosters.IsActive(Boosters.Kind.DustMultiplier));
            changed = 0;
            Boosters.BuyDustBooster();
            Check("switch: a purchase raises Changed", changed > 0);
            Check("switch: bought -> on and active", Boosters.IsOn(Boosters.Kind.DustMultiplier) && Boosters.IsActive(Boosters.Kind.DustMultiplier) && Boosters.DustActive);
            changed = 0;
            Check("switch: off goes through", Boosters.SetOn(Boosters.Kind.DustMultiplier, false) && !Boosters.IsOn(Boosters.Kind.DustMultiplier));
            Check("switch: ... raises Changed and is saved at once", changed > 0 && PlayerPrefs.GetInt(Boosters.DustOffKey, 0) == 1);
            Check("switch: off -> still owned, time kept, but not active: no x2, ads back on",
                  Boosters.Owns(Boosters.Kind.DustMultiplier) && Mathf.Approximately(Boosters.RemainingSeconds, 300f) &&
                  !Boosters.IsActive(Boosters.Kind.DustMultiplier) && !Boosters.DustActive && !Boosters.AnyActive);

            var s = BeginRealRun(5000f);
            Boosters.TestDelta = 1f;
            TouchInput.PressedOverride = () => true;
            s.SendMessage("Update");
            s.SendMessage("Update");
            Check("switch: off in a real run -> the clock is frozen (300 s), nothing multiplies, the run is not powered-up",
                  Mathf.Approximately(Boosters.RemainingSeconds, 300f) && Boosters.RunDustMultiplier == 1f && !Boosters.RunDustUsed);
            Boosters.Reload();   // a restart
            Check("switch: off and the frozen time survive a restart", !Boosters.IsOn(Boosters.Kind.DustMultiplier) && Mathf.Approximately(Boosters.RemainingSeconds, 300f));
            Boosters.SetOn(Boosters.Kind.DustMultiplier, true);
            s.SendMessage("Update");
            s.SendMessage("Update");
            Check("switch: on again resumes the countdown (298 s) and doubles again",
                  Mathf.Approximately(Boosters.RemainingSeconds, 298f) && Boosters.RunDustMultiplier == 2f && Boosters.RunDustUsed);
            Boosters.SetOn(Boosters.Kind.DustMultiplier, false);
            Check("switch: switching off writes the clock with the flag", PlayerPrefs.GetInt(Boosters.DustUsedMsKey) == 2000 && PlayerPrefs.GetInt(Boosters.DustOffKey) == 1);
            Boosters.Reload();
            Check("switch: ... so a restart reads 298 s, off", Mathf.Approximately(Boosters.RemainingSeconds, 298f) && !Boosters.IsOn(Boosters.Kind.DustMultiplier));
            TouchInput.PressedOverride = null;
            Boosters.TestDelta = null;
            EndRun(s);

            Reset(5000f);
            Boosters.BuyDustBooster();
            Boosters.SetOn(Boosters.Kind.DustMultiplier, false);
            Check("switch: buying more stacks the time and switches it back on",
                  Boosters.BuyDustBooster() == Boosters.BuyResult.Bought && Boosters.IsOn(Boosters.Kind.DustMultiplier) &&
                  Mathf.Approximately(Boosters.RemainingSeconds, 600f) && Boosters.DustActive);

            // homing atoms off: atoms do not home, the clock does not run
            Reset(5000f);
            Boosters.BuyHoming();
            Boosters.SetOn(Boosters.Kind.HomingAtoms, false);
            s = BeginRealRun(5000f);
            Boosters.TestDelta = 1f;
            TouchInput.PressedOverride = () => true;
            s.SendMessage("Update");
            Check("switch: homing off -> no homing in a real run, 300 s kept, not latched",
                  !Boosters.HomingRunning && !Boosters.HomingActive && Mathf.Approximately(Boosters.HomingRemainingSeconds, 300f) && !Boosters.RunHomingUsed);
            Boosters.SetOn(Boosters.Kind.HomingAtoms, true);
            s.SendMessage("Update");
            Check("switch: homing on -> homes and ticks (299 s)", Boosters.HomingRunning && Mathf.Approximately(Boosters.HomingRemainingSeconds, 299f));
            TouchInput.PressedOverride = null;
            Boosters.TestDelta = null;
            EndRun(s);

            // hazard clear off: charges kept, never auto-fires
            Reset(5000f);
            Boosters.BuyHazardClear();
            Boosters.BuyHazardClear();
            s = BeginRealRun(5000f);
            Check("switch: hazard clear on in a real run -> allowed", HazardClear.BlockedBy() == HazardClear.Block.None);
            Boosters.SetOn(Boosters.Kind.HazardClear, false);
            Check("switch: hazard clear off -> SwitchedOff, not available, nothing spent",
                  HazardClear.BlockedBy() == HazardClear.Block.SwitchedOff && !Boosters.ClearAvailable && !HazardClear.AutoSave(Vector3.zero) &&
                  !Boosters.ConsumeClear() && Boosters.ClearCharges == 2 && Boosters.Owns(Boosters.Kind.HazardClear));
            Boosters.Reload();
            Check("switch: hazard clear off survives a restart", !Boosters.IsOn(Boosters.Kind.HazardClear) && Boosters.ClearCharges == 2);
            Boosters.SetOn(Boosters.Kind.HazardClear, true);
            Check("switch: hazard clear on -> allowed again", HazardClear.BlockedBy() == HazardClear.Block.None && Boosters.ClearAvailable);
            EndRun(s);
        }
        finally
        {
            Boosters.Changed -= onChanged;
            TouchInput.PressedOverride = null;
            Boosters.TestDelta = null;
        }
        Reset(0f);
    }

    // ---- 11 ----
    static void Legacy()
    {
        Reset(5000f);
        PlayerPrefs.SetInt(Boosters.DustBoughtLegacyKey, 1);   // bought before the change: 10 minutes
        PlayerPrefs.SetInt(Boosters.HomingBoughtLegacyKey, 2);
        Boosters.Reload();
        Check("legacy: an old 10-minute purchase keeps its 10 minutes", Mathf.Approximately(Boosters.RemainingSeconds, 600f) && Boosters.BoughtDust == 1 &&
              Mathf.Approximately(Boosters.HomingRemainingSeconds, 1200f));
        Boosters.BuyDustBooster();
        Check("legacy: a new purchase stacks 5 minutes onto it and only counts in the new key",
              Mathf.Approximately(Boosters.RemainingSeconds, 900f) && Boosters.BoughtDust == 2 &&
              PlayerPrefs.GetInt(Boosters.DustBoughtLegacyKey) == 1 && PlayerPrefs.GetInt(Boosters.DustBoughtKey) == 1);
        var snap = ProgressSnapshot.Capture(1000);
        Check("legacy: the cloud snapshot carries both (old field = old purchases)",
              snap.boostDustBought == 1 && snap.boostDustBought5m == 1 && snap.boostHomingBought == 2 && snap.boostHomingBought5m == 0);
        foreach (string k in Keys) PlayerPrefs.DeleteKey(k);
        snap.Apply();
        Check("legacy: apply restores 15 minutes of Dust x2 and 20 of homing", Mathf.Approximately(Boosters.RemainingSeconds, 900f) &&
              Mathf.Approximately(Boosters.HomingRemainingSeconds, 1200f));
        ProgressSnapshot old;
        Check("legacy: an older blob (no 5-minute fields) parses with none",
              ProgressSnapshot.TryParse("{\"schemaVersion\":1,\"boostDustBought\":2}", out old) == ProgressSnapshot.ParseResult.Ok &&
              old.boostDustBought == 2 && old.boostDustBought5m == 0 && old.boostHomingBought5m == 0);
        var a = new ProgressSnapshot { savedAtUtc = 2000, currency = 5000f, boostDustBought5m = 3, boostHomingBought5m = 1 };
        var b = new ProgressSnapshot { savedAtUtc = 1000, currency = 300f, boostDustBought5m = 1, boostHomingBought5m = 2 };
        var m = ProgressMerge.Merge(a, b);
        Check("legacy: the 5-minute counters merge by max", m.boostDustBought5m == 3 && m.boostHomingBought5m == 2);
        Check("legacy: a 5-minute purchase only the other side has is charged on merge",
              Mathf.Approximately(ProgressMerge.UnpaidBoosters(a, b), Boosters.HomingPrice) &&
              Mathf.Approximately(ProgressMerge.UnpaidBoosters(b, a), 2f * Boosters.DustBoosterPrice));
        Check("legacy: a snapshot with only new purchases has progress", new ProgressSnapshot { boostDustBought5m = 1 }.HasProgress());
        Reset(0f);
    }

    // ---- 12 ----
    static void ReadApi()
    {
        Reset(5000f);
        Check("api: timed kinds", Boosters.IsTimed(Boosters.Kind.DustMultiplier) && Boosters.IsTimed(Boosters.Kind.HomingAtoms) && !Boosters.IsTimed(Boosters.Kind.HazardClear));
        Check("api: nothing held", !Boosters.Owns(Boosters.Kind.DustMultiplier) && !Boosters.Owns(Boosters.Kind.HazardClear) &&
              Boosters.SecondsLeft(Boosters.Kind.HomingAtoms) == 0f && Boosters.ChargesLeft(Boosters.Kind.HazardClear) == 0 &&
              Boosters.Status(Boosters.Kind.DustMultiplier) == "" && Boosters.Status(Boosters.Kind.HazardClear) == "");
        Boosters.BuyHoming();
        Boosters.BuyHazardClear();
        Boosters.BuyHazardClear();
        Boosters.BuyHazardClear();
        Check("api: time and charges", Mathf.Approximately(Boosters.SecondsLeft(Boosters.Kind.HomingAtoms), 300f) &&
              Boosters.ChargesLeft(Boosters.Kind.HazardClear) == 3 && Boosters.ChargesLeft(Boosters.Kind.HomingAtoms) == 0 &&
              Boosters.SecondsLeft(Boosters.Kind.HazardClear) == 0f && Boosters.IsActive(Boosters.Kind.HomingAtoms) && Boosters.IsActive(Boosters.Kind.HazardClear));
        Check("api: the shop's words", Boosters.Status(Boosters.Kind.HomingAtoms) == "5:00 left" && Boosters.Status(Boosters.Kind.HazardClear) == "x3");
        Boosters.Toggle(Boosters.Kind.HomingAtoms);
        Check("api: off is owned but not active, time still read", Boosters.Owns(Boosters.Kind.HomingAtoms) && !Boosters.IsActive(Boosters.Kind.HomingAtoms) &&
              Mathf.Approximately(Boosters.SecondsLeft(Boosters.Kind.HomingAtoms), 300f) && Boosters.Status(Boosters.Kind.HomingAtoms) == "5:00 left");
        Reset(0f);
    }
}
