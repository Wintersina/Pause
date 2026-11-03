using System.Collections.Generic;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

// The star-dust boosters (Boosters, HazardClear, HazardClearButton):
//   1  the pure stacking rule (booster + rewarded ad capped at 3x)
//   2  PRICES: simulated typical dust in 10 minutes of play; the multiplier's extra dust stays under 70% of its price
//   3  buying: pay once, extension, limits, restart safety, no buying inside a run
//   4  the clock: ticks only on unfrozen real-run frames; practice runs never tick or multiply
//   5  what it multiplies (pickups, rewards, flight) and what it must not (claims, purchases, score, leaderboard dust)
//   6  hazard clear: what it removes, what it leaves, and that it pays no score, kills, dust or achievements
//   7  cloud save: capture / apply round trip and the merge rules
//   8  the in-run control: its rect on a spread of screens (phones, tall, tablet) never meets the HUD
public static class BoostersTest
{
    static int fails;
    static void Check(string what, bool ok)
    {
        Debug.Log((ok ? "[BOOSTERS] PASS  " : "[BOOSTERS] FAIL  ") + what);
        if (!ok) fails++;
    }

    public static void Run() { TestHarness.Exit(Execute()); }

    static readonly string[] Keys = { Boosters.DustBoughtKey, Boosters.DustUsedMsKey, Boosters.ClearBoughtKey, Boosters.ClearUsedKey };

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
            Cloud();
            Control();
        }
        finally
        {
            TouchInput.PressedOverride = null;
            Boosters.TestDelta = null;
            Boosters.AdDoubled = false;
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
        float extra = per10 * (Boosters.DustMultiplier(true, false) - 1f);            // what the doubling adds
        float flawless = extra * 1.35f;                                                // adaptive difficulty's best case (docs/adaptive-difficulty.md)
        float cap = Boosters.MaxPaybackShare * Boosters.DustBoosterPrice;
        Debug.Log(string.Format("[BOOSTERS] 10 min of play: {0:F0} dust; the x2 adds {1:F0} ({2:P0} of the {3:F0} price); flawless {4:F0} ({5:P0})",
            per10, extra, extra / Boosters.DustBoosterPrice, Boosters.DustBoosterPrice, flawless, flawless / Boosters.DustBoosterPrice));
        Check("the dust multiplier pays back less than 70% of its price in 10 minutes (typical)", extra < cap);
        Check("... and also for a flawless run (x1.35 income)", flawless < cap);
        Check("the multiplier is a net cost: the extra never reaches the price", flawless < Boosters.DustBoosterPrice);

        float run = 0f;
        for (int s = 1; s <= seeds; s++) run += SimulatedDust(s * 104729 + 3, 4);
        run /= seeds;
        Debug.Log("[BOOSTERS] a typical 4-level run earns " + run.ToString("F0") + " dust; a hazard clear costs " + Boosters.HazardClearPrice);
        Check("a hazard clear costs about one typical run's earnings (0.5x to 1.5x)", Boosters.HazardClearPrice > .5f * run && Boosters.HazardClearPrice < 1.5f * run);
        Check("prices sit far under a ship (Gold Warden 12,000)", Boosters.DustBoosterPrice * 10f < 12000f);
        Check("10 minutes per purchase", Boosters.DustBoosterSeconds == 600f);
    }

    // ---- 3 ----
    static void Buying()
    {
        Reset(1000f);
        var r = Boosters.BuyDustBooster();
        Check("buying the multiplier takes the price once and adds 10 minutes",
              r == Boosters.BuyResult.Bought && Mathf.Approximately(PlayerPrefs.GetFloat(StarDustLedger.CurrencyKey), 1000f - Boosters.DustBoosterPrice) &&
              Boosters.BoughtDust == 1 && Mathf.Approximately(Boosters.RemainingSeconds, 600f) && Boosters.DustActive && Boosters.AnyActive);
        Boosters.Reload();   // a restart right after the purchase
        Check("a restart keeps it: same balance, same time, no second charge",
              Mathf.Approximately(PlayerPrefs.GetFloat(StarDustLedger.CurrencyKey), 400f) && Boosters.BoughtDust == 1 && Mathf.Approximately(Boosters.RemainingSeconds, 600f));
        Check("not enough dust: nothing changes", Boosters.BuyDustBooster() == Boosters.BuyResult.NotEnoughDust &&
              PlayerPrefs.GetFloat(StarDustLedger.CurrencyKey) == 400f && Boosters.BoughtDust == 1 && !Boosters.CanBuy(Boosters.Kind.DustMultiplier));

        Reset(10000f);
        Boosters.BuyDustBooster();
        Boosters.TestDelta = 100f;
        PlayerPrefs.SetInt(Boosters.DustUsedMsKey, 100000);   // 100 s spent
        Boosters.Reload();
        Boosters.BuyDustBooster();
        Check("buying while active extends: 500 s left + 600 s = 1100 s", Mathf.Approximately(Boosters.RemainingSeconds, 1100f) && Boosters.BoughtDust == 2);
        Reset(100000f);
        int ok = 0;
        while (Boosters.BuyDustBooster() == Boosters.BuyResult.Bought && ok < 20) ok++;
        Check("stored time stops at an hour (6 purchases)", ok == 6 && Mathf.Approximately(Boosters.RemainingSeconds, Boosters.MaxStoredSeconds));

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
              Boosters.Buy(Boosters.Kind.DustMultiplier) == Boosters.BuyResult.Bought && Boosters.Status(Boosters.Kind.DustMultiplier) == "10:00 left");
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
        Check("a frozen run (finger up, pauses left) does not spend the clock", Mathf.Approximately(Boosters.RemainingSeconds, 600f));
        TouchInput.PressedOverride = () => true;
        buttonClicks.playerDied = true;
        s.SendMessage("Update");
        buttonClicks.playerDied = false;
        Check("a dead player does not spend it", Mathf.Approximately(Boosters.RemainingSeconds, 600f));
        s.SendMessage("Update");
        s.SendMessage("Update");
        Check("running frames spend it (1 s each in this test): 598 s left", Mathf.Approximately(Boosters.RemainingSeconds, 598f));
        Boosters.TestDelta = 10000f;
        s.SendMessage("Update");
        Check("it never goes below zero and then the booster is off", Boosters.RemainingSeconds == 0f && !Boosters.DustActive && !Boosters.AnyActive);
        Check("what it spent is in the prefs (restart-proof)", PlayerPrefs.GetInt(Boosters.DustUsedMsKey) == 600000);
        TouchInput.PressedOverride = null;
        EndRun(s);

        // the menu: no run, no ticking
        Reset(5000f);
        Boosters.BuyDustBooster();
        Boosters.TestDelta = 1f;
        Check("no run, no ticking (Tick is only called by a running real run)", Mathf.Approximately(Boosters.RemainingSeconds, 600f));

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
        Check("a practice (tutorial) run neither ticks nor multiplies", Mathf.Approximately(Boosters.RemainingSeconds, 600f) &&
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
        bool fired = HazardClear.Fire(Vector3.zero);
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

        Boosters.BuyHazardClear();   // refused in a run: set the counter
        PlayerPrefs.SetInt(Boosters.ClearBoughtKey, 2);
        Boosters.Reload();
        Check("on cooldown a second tap does nothing", HazardClear.BlockedBy() == HazardClear.Block.Cooldown && !HazardClear.Fire(Vector3.zero) && Boosters.ClearCharges == 1);
        Object.DestroyImmediate(boss); Object.DestroyImmediate(armoured); Object.DestroyImmediate(pickup);

        HazardClear.BeginRun();
        Check("an empty screen does not spend a charge", !HazardClear.Fire(Vector3.zero) && Boosters.ClearCharges == 1);
        buttonClicks.playerDied = true;
        Check("blocked when dead", HazardClear.BlockedBy() == HazardClear.Block.Dead);
        buttonClicks.playerDied = false;
        PlayerPrefs.SetInt(Boosters.ClearUsedKey, 2);
        Boosters.Reload();
        HazardClear.BeginRun();
        Check("no charge, no clear", HazardClear.BlockedBy() == HazardClear.Block.NoCharge);
        PlayerPrefs.SetInt(DeveloperUnlocks.EnabledKey, 1);
        Check("developer mode: always available, never spends", HazardClear.BlockedBy() == HazardClear.Block.None && Boosters.ConsumeClear() && Boosters.UsedClear == 2);
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
        Check("capture reads the four counters", snap.boostDustBought == 2 && snap.boostDustUsedMs == 123456 && snap.boostClearBought == 4 && snap.boostClearUsed == 1 && snap.HasProgress());
        ProgressSnapshot parsed;
        Check("they survive the JSON", ProgressSnapshot.TryParse(snap.ToJson(), out parsed) == ProgressSnapshot.ParseResult.Ok &&
              parsed.boostDustBought == 2 && parsed.boostDustUsedMs == 123456 && parsed.boostClearBought == 4 && parsed.boostClearUsed == 1);
        foreach (string k in Keys) PlayerPrefs.DeleteKey(k);
        parsed.Apply();
        Check("apply writes them back and reloads the cache", Boosters.BoughtDust == 2 && Boosters.UsedMs == 123456 && Boosters.ClearCharges == 3 &&
              Mathf.Approximately(Boosters.RemainingSeconds, 1200f - 123.456f));

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

    // ---- 8 ----
    static void Control()
    {
        var screens = new[]
        {
            new Vector2(1080f, 2400f),   // tall phone
            new Vector2(1080f, 1920f),   // 16:9 phone
            new Vector2(720f, 1280f),    // small phone
            new Vector2(1290f, 2796f),   // iPhone Pro Max
            new Vector2(2048f, 2732f),   // tablet
            new Vector2(1200f, 1920f),   // small tablet
        };
        bool allFit = true, noClash = true, bigEnough = true;
        foreach (var screen in screens)
        {
            Rect safe = new Rect(0f, screen.y * .03f, screen.x, screen.y * .94f);   // a notch and a home bar
            foreach (float rail in new[] { 0f, 2.2f })
            {
                var band = TopBand.FrameFor(safe, screen, rail, new Rect[0]);
                Rect clear = HazardClearButton.RectFor(band, screen);
                Rect pair = PauseQuickActions.ScreenRectFor(band, screen);
                float boardBottom = band.top - HudStyler.ReadoutHeightFor(screen) * HudStyler.ReadoutFitFor(band, screen);
                allFit &= clear.xMin >= safe.xMin && clear.xMax <= safe.xMax && clear.yMin >= safe.yMin && clear.yMax <= safe.yMax;
                noClash &= !clear.Overlaps(pair) && clear.yMax <= pair.yMin && clear.xMax <= band.right;
                // below the score board's bottom edge, or clear of the board horizontally
                noClash &= clear.yMax <= boardBottom + .5f;
                bigEnough &= clear.width >= 40f && Mathf.Approximately(clear.width, clear.height);
                // the lower half of the screen (the thumb's play area) is left alone
                noClash &= clear.yMin > screen.y * .5f;
            }
        }
        Check("the control sits inside the safe area on every screen", allFit);
        Check("... under the Replay/Home pair, clear of the score board, in the top half only", noClash);
        Check("... and is a square at least as big as a tap target", bigEnough);
        Check("the tutorial gets no button (gameS1 only)", System.IO.File.ReadAllText("Assets/Scripts/Boosters/HazardClearButton.cs").Contains("scene.name != \"gameS1\""));
        Check("movePlayer treats a press on it as the button's", System.IO.File.ReadAllText("Assets/Scripts/Ship/movePlayer.cs").Contains("HazardClearButton.PointerOnButton"));
        Check("a tap on it never spends a pause (score.Update)", System.IO.File.ReadAllText("Assets/Scripts/Core/score.cs").Contains("!HazardClearButton.PointerOnButton"));
    }
}
