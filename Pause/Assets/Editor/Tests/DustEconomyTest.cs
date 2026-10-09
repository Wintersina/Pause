using System.IO;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

// The star dust economy dial (ScoreRules.DustRewardScale = 0.60, "about 40%
// less overall except when actually picking up stardust"):
//   1  the constant and which sources it touches (pickups: no; flight trickle,
//      kill crumbs, elites, score bonus: yes) -- all through ScoreRules.RewardDust
//   2  fixed-seed simulated runs: pickup dust equals the master baseline,
//      reward dust is 0.60x the master baseline (within 2%), the combined
//      total is documented in the log; no positive reward rounds to zero
//   3  the live path: AwardStarDust (pickups) pays full, AwardRewardDust
//      pays 0.60x, both reach the ledger and the practice (tutorial) counter
public static class DustEconomyTest
{
    static int fails;
    static void Check(string what, bool ok)
    {
        Debug.Log((ok ? "[DUSTECON] PASS  " : "[DUSTECON] FAIL  ") + what);
        if (!ok) fails++;
    }

    public static void Run() { TestHarness.Exit(Execute()); }

    // Master baseline (before this change), written out here on purpose so a
    // retune of the live constants can't silently move the baseline with it.
    const float BaseSmall = .5f, BaseLarge = 1f, BaseBlue = 2f;           // pickups: unchanged
    const float BaseKill = .12f, BaseElite = 15f;                         // rewards: x0.60
    const float BaseFlightPerDistance = 1f / 12f, BaseBonusPerSqrt = .02f, BaseBonusCap = 1.5f;

    public static int Execute()
    {
        fails = 0;
        using var sandbox = new TestHarness.Sandbox();
        try
        {
            Constants();
            Simulated();
            LivePath();
        }
        finally
        {
            buttonClicks.playerDied = false;
            RunScore.EndRun(RunScore.RunId);
            StarDustLedger.EndRun(StarDustLedger.RunId);
        }
        Debug.Log("[DUSTECON] failures: " + fails);
        return fails;
    }

    static void Constants()
    {
        Check("DustRewardScale is 0.60", Mathf.Approximately(ScoreRules.DustRewardScale, .6f));
        Check("RewardDust scales and never turns a positive reward into 0",
              Mathf.Approximately(ScoreRules.RewardDust(.12f), .072f) && ScoreRules.RewardDust(.0001f) > 0f &&
              ScoreRules.RewardDust(0f) == 0f && ScoreRules.RewardDust(-3f) == 0f);
        string col = File.ReadAllText("Assets/Scripts/Ship/collisionDetection.cs");
        Check("pickup values are the master ones (star .5, large 1, blue atom 2, kill .12 base)",
              col.Contains("smallStarValue = 0.5f") && col.Contains("largeStarValue = 1f") &&
              col.Contains("blueAtomValue = 2f") && col.Contains("enemyDustValue = 0.12f"));
        Check("pickups go straight to AwardStarDust, kills through AwardRewardDust",
              col.Contains("awardDust(smallStarValue)") && col.Contains("awardDust(largeStarValue)") &&
              col.Contains("awardDust(blueAtomValue)") && col.Contains("awardKillDust(player.enemyDustValue)") &&
              !col.Contains("awardDust(player.enemyDustValue)"));
        Check("elites pay through AwardRewardDust", File.ReadAllText("Assets/Scripts/Gameplay/Elites/EliteFx.cs").Contains("score.AwardRewardDust(def.Dust)"));
        Check("base reward constants unchanged", ScoreRules.EliteDownDust == BaseElite && Mathf.Approximately(ScoreRules.DustPerDistance, BaseFlightPerDistance) &&
              ScoreRules.ScoreDustPerSqrtPoint == BaseBonusPerSqrt && ScoreRules.ScoreDustCap == BaseBonusCap);
        Check("shop prices / skin costs unchanged",
              shopingShips.Prices[2] == 600f && ShipSkins.KindPrices[1] == 300f && ShipSkins.KindPrices[2] == 750f);
    }

    struct Totals { public float pickup, flight, kills, elites, bonus; public float Reward { get { return flight + kills + elites + bonus; } } }

    // One modelled run: `worlds` levels of 120 s at 60 fps, the speed climbing
    // from 0.10 to 0.33 inside each level, with seeded pickup / kill / elite counts.
    static void Run(int seed, int worlds, out Totals oldT, out Totals newT)
    {
        var rng = new System.Random(seed);
        oldT = new Totals(); newT = new Totals();
        long scorePoints = 0;
        for (int w = 0; w < worlds; w++)
        {
            const float dt = 1f / 60f;
            for (int f = 0; f < 120 * 60; f++)
            {
                float speed = .10f + .23f * f / (120f * 60f);
                oldT.flight += BaseFlightPerDistance * speed * dt;
                newT.flight += ScoreRules.FlightDust(speed, dt);
            }
            int small = rng.Next(45, 80), large = rng.Next(8, 18), blue = rng.Next(0, 3), kills = rng.Next(25, 55), elites = rng.Next(0, 2);
            oldT.pickup += small * BaseSmall + large * BaseLarge + blue * BaseBlue;
            newT.pickup += small * .5f + large * 1f + blue * 2f;   // what collisionDetection awards, via AwardStarDust
            for (int k = 0; k < kills; k++)
            {
                oldT.kills += BaseKill;
                newT.kills += ScoreRules.RewardDust(BaseKill);
            }
            oldT.elites += elites * BaseElite;
            for (int e = 0; e < elites; e++) newT.elites += ScoreRules.RewardDust(ScoreRules.EliteDownDust);
            scorePoints += 1200 + rng.Next(0, 400);
        }
        oldT.bonus = Mathf.Round(Mathf.Min(BaseBonusCap, BaseBonusPerSqrt * Mathf.Sqrt(scorePoints)) * 100f) / 100f;
        newT.bonus = ScoreRules.ScoreDustBonus(scorePoints);
    }

    static void Simulated()
    {
        foreach (int worlds in new[] { 1, 4, 8 })
        {
            float sumOldR = 0, sumNewR = 0, sumOldP = 0, sumNewP = 0;
            for (int seed = 1; seed <= 20; seed++)
            {
                Totals o, n;
                Run(seed * 7919 + worlds, worlds, out o, out n);
                sumOldR += o.Reward; sumNewR += n.Reward; sumOldP += o.pickup; sumNewP += n.pickup;
            }
            float ratio = sumNewR / sumOldR, total = (sumNewR + sumNewP) / (sumOldR + sumOldP);
            Debug.Log(string.Format("[DUSTECON] {0} world(s), 20 seeded runs: pickups {1:F1} -> {2:F1}; rewards {3:F1} -> {4:F1} (x{5:F3}); combined {6:F1} -> {7:F1} (x{8:F3})",
                worlds, sumOldP / 20, sumNewP / 20, sumOldR / 20, sumNewR / 20, ratio, (sumOldR + sumOldP) / 20, (sumNewR + sumNewP) / 20, total));
            Check(worlds + " world(s): pickup dust identical to the master baseline", Mathf.Approximately(sumOldP, sumNewP));
            // The score bonus is tidied to 0.01 and its cap is scaled too, so the ratio has a hair of slack.
            Check(worlds + " world(s): reward dust is 0.60x the baseline (x" + ratio.ToString("F3") + ")", Mathf.Abs(ratio - .6f) <= .6f * .02f);
            Check(worlds + " world(s): combined dust is between 0.60x and 1.0x (x" + total.ToString("F3") + ")", total > .6f && total < 1f);
        }
        // many tiny awards: no per-award rounding loss
        float tiny = 0f;
        for (int i = 0; i < 10000; i++) tiny += ScoreRules.RewardDust(BaseKill);
        Check("10,000 kill crumbs pay 0.60x exactly (" + tiny.ToString("F2") + ")", Mathf.Abs(tiny - 10000 * BaseKill * .6f) < .5f);
    }

    static score MakeScore()
    {
        var go = new GameObject("~DustEcon");
        var s = go.AddComponent<score>();
        s.speedValue = new GameObject("s").AddComponent<Text>();
        s.pauseCounterText = new GameObject("p").AddComponent<Text>();
        return s;
    }

    static void LivePath()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        PlayerPrefs.SetString("HasDoneTut", "true");
        PlayerPrefs.SetInt(DeveloperUnlocks.EnabledKey, 0);
        startMenu.youAreInTutorial = false;
        buttonClicks.playerDied = false;
        PlayerPrefs.SetFloat(StarDustLedger.CurrencyKey, 100f);
        var s = MakeScore();
        s.SendMessage("Awake");
        s.SendMessage("Start");
        score.AwardStarDust(.5f);
        float paid = score.AwardRewardDust(.12f);
        score.AwardRewardDust(15f);
        Check("pickup pays full, a kill crumb pays 0.072, an elite 9 (ledger " + StarDustLedger.Earned + ")",
              Mathf.Abs(paid - .072f) < 1e-5f && Mathf.Abs(StarDustLedger.Earned - (.5f + .072f + 9f)) < 1e-4f &&
              Mathf.Abs(score.totalCurrency - (100f + StarDustLedger.Earned)) < 1e-4f);
        Object.DestroyImmediate(s.gameObject);
        StarDustLedger.EndRun(StarDustLedger.RunId);

        // the tutorial: practice dust follows the same reward scale, the saved balance is untouched
        PlayerPrefs.SetString("HasDoneTut", "false");
        PlayerPrefs.SetFloat(StarDustLedger.CurrencyKey, 100f);
        s = MakeScore();
        s.SendMessage("Awake");
        s.SendMessage("Start");
        score.AwardStarDust(3f);
        score.AwardRewardDust(15f);
        Check("tutorial: pickups full, rewards scaled, saved balance untouched",
              Mathf.Abs(score.tutorialCurrency - 12f) < 1e-4f && score.totalCurrency == 0f && PlayerPrefs.GetFloat(StarDustLedger.CurrencyKey) == 100f);
        Object.DestroyImmediate(s.gameObject);
        StarDustLedger.EndRun(StarDustLedger.RunId);
    }
}
