using System.Reflection;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

// Star dust collected in a run is kept however the run ends, exactly once,
// and shop purchases are saved the moment they happen.
//
// Dust used to be written only by the death screen, so Menu, Replay, Back or
// backgrounding the app lost the whole run. Replay and Menu also zero
// score.totalCurrency before the scene unloads, which is why the ledger keeps
// its own record instead of trusting that field.
public static class StarDustPersistenceTest
{
    const string Key = "PlayerCurrecny";
    static int fails;
    static void Check(string what, bool ok)
    {
        Debug.Log((ok ? "[SD] PASS  " : "[SD] FAIL  ") + what);
        if (!ok) fails++;
    }

    public static void Run()
    {
        TestHarness.Exit(Execute());
    }

    public static int Execute()
    {
        fails = 0;
        using var sandbox = new TestHarness.Sandbox();

        Check("the save key is still the original misspelt one", StarDustLedger.CurrencyKey == Key);
        CommitsAreIdempotent();
        ReplayAfterZeroingStillBanksTheRun();
        BackgroundingBanksTheRun();
        StaleRunCannotCloseTheNewOne();
        NonPayingRunNeverTouchesTheSave();
        ScoreComponentBanksOnLeaveAndOnDeathOnce();
        PurchasesSaveImmediately();

        Debug.Log("[SD] failures: " + fails);
        return fails;
    }

    static bool Near(float a, float b) { return Mathf.Abs(a - b) < 1e-4f; }

    static void CommitsAreIdempotent()
    {
        PlayerPrefs.SetFloat(Key, 10f);
        int run = StarDustLedger.BeginRun(true);
        StarDustLedger.Earn(2.5f);
        StarDustLedger.Commit(run);
        StarDustLedger.Commit(run);
        Check("committing twice credits the run once", Near(PlayerPrefs.GetFloat(Key), 12.5f));

        // What playerIsDead does on death: write score.totalCurrency, which is
        // the same baseline + earned sum.
        PlayerPrefs.SetFloat(Key, 10f + 2.5f);
        StarDustLedger.EndRun(run);
        Check("death screen + ledger + scene teardown still credit it once",
              Near(PlayerPrefs.GetFloat(Key), 12.5f));

        StarDustLedger.BeginRun(true);
        Check("the next run starts from the banked balance", Near(StarDustLedger.Balance, 12.5f));
        StarDustLedger.EndRun(StarDustLedger.BeginRun(false));
    }

    static void ReplayAfterZeroingStillBanksTheRun()
    {
        PlayerPrefs.SetFloat(Key, 4f);
        int run = StarDustLedger.BeginRun(true);
        StarDustLedger.Earn(3f);
        score.totalCurrency = 0f;          // buttonClicks.replay() / menu do this
        StarDustLedger.EndRun(run);
        Check("Replay/Menu keep the run's dust", Near(PlayerPrefs.GetFloat(Key), 7f));
        Check("and the run is closed", !StarDustLedger.IsActive);
    }

    static void BackgroundingBanksTheRun()
    {
        PlayerPrefs.SetFloat(Key, 1f);
        int run = StarDustLedger.BeginRun(true);
        StarDustLedger.Earn(.75f);
        // PrefsSaverRunner.OnApplicationPause(true) / OnApplicationQuit.
        StarDustLedger.Stage();
        PrefsSaver.SaveNow();
        Check("backgrounding writes the dust so far", Near(PlayerPrefs.GetFloat(Key), 1.75f));
        StarDustLedger.Earn(.25f);
        StarDustLedger.Stage();
        StarDustLedger.EndRun(run);
        Check("resuming and leaving later adds only the rest", Near(PlayerPrefs.GetFloat(Key), 2f));
    }

    static void StaleRunCannotCloseTheNewOne()
    {
        PlayerPrefs.SetFloat(Key, 0f);
        int oldRun = StarDustLedger.BeginRun(true);
        StarDustLedger.Earn(1f);
        int newRun = StarDustLedger.BeginRun(true);   // next scene's Awake first
        Check("beginning a run banks the one still open", Near(PlayerPrefs.GetFloat(Key), 1f));
        StarDustLedger.EndRun(oldRun);                 // old scene's OnDestroy late
        Check("the old scene's teardown leaves the new run open", StarDustLedger.IsActive);
        StarDustLedger.Earn(2f);
        StarDustLedger.EndRun(newRun);
        Check("and the new run is credited on top", Near(PlayerPrefs.GetFloat(Key), 3f));
    }

    static void NonPayingRunNeverTouchesTheSave()
    {
        PlayerPrefs.SetFloat(Key, 9f);
        int run = StarDustLedger.BeginRun(false);
        StarDustLedger.Earn(5f);
        StarDustLedger.Stage();
        StarDustLedger.EndRun(run);
        Check("a non-paying run leaves the saved dust alone", Near(PlayerPrefs.GetFloat(Key), 9f));
    }

    static score MakeHud()
    {
        var go = new GameObject("~ScoreHud");
        var hud = go.AddComponent<score>();
        hud.speedValue = new GameObject("s").AddComponent<Text>();
        hud.pauseCounterText = new GameObject("p").AddComponent<Text>();
        return hud;
    }

    static void Call(score hud, string method)
    {
        typeof(score).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
            .Invoke(hud, null);
    }

    static void ScoreComponentBanksOnLeaveAndOnDeathOnce()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        PlayerPrefs.SetString("HasDoneTut", "true");
        startMenu.youAreInTutorial = false;
        buttonClicks.playerDied = false;
        PlayerPrefs.SetFloat(Key, 10f);

        // Leave via Replay/Menu.
        var hud = MakeHud();
        Call(hud, "Awake");
        Call(hud, "Start");
        Check("speed readout is spelled \"Current\"", hud.speedValue.text == "Current Speed : 0");
        score.AwardStarDust(2f);
        score.totalCurrency = 0f;
        Call(hud, "OnDestroy");
        Check("leaving a real run mid-flight keeps its dust", Near(PlayerPrefs.GetFloat(Key), 12f));
        Object.DestroyImmediate(hud.gameObject);

        // Die, with the death screen also saving, then leave.
        hud = MakeHud();
        Call(hud, "Awake");
        Call(hud, "Start");
        score.AwardStarDust(1f);
        buttonClicks.playerDied = true;
        Call(hud, "Update");
        Check("death banks the run", Near(PlayerPrefs.GetFloat(Key), 13f));
        PlayerPrefs.SetFloat(Key, score.totalCurrency);   // playerIsDead.BeginResults
        Call(hud, "Update");
        Call(hud, "OnDestroy");
        Check("death + death screen + leaving count the run exactly once",
              Near(PlayerPrefs.GetFloat(Key), 13f));
        Object.DestroyImmediate(hud.gameObject);
        buttonClicks.playerDied = false;
    }

    static void PurchasesSaveImmediately()
    {
        PlayerPrefs.SetFloat(Key, 100f);
        PlayerPrefs.SetString("boughtship3", "False");
        int saves = PrefsSaver.SaveCount;
        Check("an affordable ship can be bought", shopingShips.TryPurchase(3, 60f));
        Check("its cost is deducted", Near(PlayerPrefs.GetFloat(Key), 40f));
        Check("it is marked owned", PlayerPrefs.GetString("boughtship3") == "True");
        Check("it becomes the selected ship", PlayerPrefs.GetInt("spawnShip") == 3);
        Check("the purchase is saved to disk right away", PrefsSaver.SaveCount == saves + 1);

        saves = PrefsSaver.SaveCount;
        PlayerPrefs.SetString("boughtship4", "False");
        Check("an unaffordable ship is refused", !shopingShips.TryPurchase(4, 60f));
        Check("and nothing changes", Near(PlayerPrefs.GetFloat(Key), 40f) &&
              PlayerPrefs.GetString("boughtship4") == "False" && PrefsSaver.SaveCount == saves);
    }
}
