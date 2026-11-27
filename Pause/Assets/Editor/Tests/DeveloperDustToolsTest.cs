using UnityEngine;

// The developer-only dust tools (DeveloperDustTools): +1000 STARDUST adds exactly 1000 per tap, and
// WIPE DUST & COLLECTION resets dust and collection while keeping real-money entitlements and
// settings. Both do nothing with developer mode off.
public static class DeveloperDustToolsTest
{
    static int failures;
    static void Check(string what, bool ok)
    {
        Debug.Log((ok ? "[DDT] PASS  " : "[DDT] FAIL  ") + what);
        if (!ok) failures++;
    }

    public static void Run() { TestHarness.Exit(Execute()); }

    static readonly string[] Extra =
    {
        AdFree.Key, UpgradeOfferStore.StreakKey, UpgradeOfferStore.DeclinedKey, UpgradeOfferStore.LastDiedKey,
        UpgradeOfferStore.EnabledKey,
    };

    public static int Execute()
    {
        failures = 0;
        using var sandbox = new TestHarness.Sandbox();
        // Int prefs the sandbox does not cover: put back as they were.
        var saved = new System.Collections.Generic.Dictionary<string, int?>();
        foreach (string k in Extra) saved[k] = PlayerPrefs.HasKey(k) ? PlayerPrefs.GetInt(k, 0) : (int?)null;
        try
        {
            AddDustTool();
            WipeKeepsAndResets();
            WipeWithDeveloperOff();
        }
        finally
        {
            DeveloperUnlocks.SetEnabled(false);
            foreach (string k in Extra)
            {
                if (saved[k].HasValue) PlayerPrefs.SetInt(k, saved[k].Value);
                else PlayerPrefs.DeleteKey(k);
            }
            PlayerPrefs.Save();
        }
        Debug.Log("[DDT] failures: " + failures);
        return failures;
    }

    static void Reset()
    {
        DeveloperUnlocks.SetEnabled(false);
        PlayerPrefs.DeleteKey(StarDustLedger.CurrencyKey);
        PlayerPrefs.DeleteKey(DeveloperUnlocks.SelectedWorldKey);
    }

    static void AddDustTool()
    {
        Reset();
        PlayerPrefs.SetFloat(StarDustLedger.CurrencyKey, 250f);
        Check("+1000 with developer mode off does nothing", DeveloperDustTools.AddDust() < 0f
              && Mathf.Approximately(PlayerPrefs.GetFloat(StarDustLedger.CurrencyKey), 250f));

        DeveloperUnlocks.SetEnabled(true);
        float a = DeveloperDustTools.AddDust();
        Check("first tap adds exactly 1000", Mathf.Approximately(a, 1250f) && Mathf.Approximately(StarDustLedger.Saved, 1250f));
        float b = DeveloperDustTools.AddDust();
        Check("second tap adds exactly 1000 more", Mathf.Approximately(b, 2250f) && Mathf.Approximately(StarDustLedger.Saved, 2250f));
        Check("the add was saved at once", !PrefsSaver.Dirty);
        Reset();
    }

    static void WipeKeepsAndResets()
    {
        Reset();
        // Real progress, laid down with developer mode off.
        PlayerPrefs.SetFloat(StarDustLedger.CurrencyKey, 98765f);
        PlayerPrefs.SetString(ShipId.OwnedKey(2), "True");
        PlayerPrefs.SetString(ShipId.OwnedKey(3), "True");
        PlayerPrefs.SetString(ShipId.OwnedKey(PremiumShips.Paragon), "True");   // the real-money ship
        PlayerPrefs.SetInt(ShipId.SelectedKey, 3);
        PlayerPrefs.SetInt(ShipSkins.OwnedKey(2, 1), 1);
        PlayerPrefs.SetInt(ShipSkins.EquippedKey(2), 1);
        PlayerPrefs.SetInt(WorldManager.PrefsHighestWorld, 3);
        PlayerPrefs.SetInt(WorldManager.PrefsCurrentWorld, 2);
        PlayerPrefs.SetString(Codex.PrefsKey, "space_alien,frost_drone");
        PlayerPrefs.SetString(Codex.PaidKey, "space_alien");
        PlayerPrefs.SetString(DamageLog.KilledKey, "space_alien:3");
        PlayerPrefs.SetInt(Boosters.DustBoughtKey, 2);
        PlayerPrefs.SetInt(UpgradeOfferStore.StreakKey, 4);
        PlayerPrefs.SetInt(AdFree.Key, 1);                                       // real-money entitlement
        PlayerPrefs.SetInt(RunScore.BestScoreKey, 4321);
        PlayerPrefs.SetInt(SoundSettings.SfxOnKey, 0);                           // a setting
        PlayerPrefs.SetInt(UpgradeOfferStore.EnabledKey, 0);                     // a setting
        PlayerPrefs.SetString("HasDoneTut", "true");
        Codex.Reload();
        Boosters.Reload();

        DeveloperUnlocks.SetEnabled(true);   // snapshots the real progress, grants every ship
        Check("developer mode on shows every ship owned", ShipId.IsOwned(9));

        Check("wipe runs with developer mode on", DeveloperDustTools.Wipe());
        Check("developer mode is still on afterwards", DeveloperUnlocks.Enabled);
        Check("wipe saved at once", !PrefsSaver.Dirty);

        // Back to real state to read what was actually kept.
        DeveloperUnlocks.SetEnabled(false);
        Check("dust is 0", Mathf.Approximately(PlayerPrefs.GetFloat(StarDustLedger.CurrencyKey), 0f));
        Check("bought ships gone", !ShipId.IsOwned(2) && !ShipId.IsOwned(3));
        Check("starter ship owned and selected", ShipId.IsOwned(ShipId.Starter) && ShipId.SavedSelection == ShipId.Starter);
        Check("Paragon (real money) kept", PlayerPrefs.GetString(ShipId.OwnedKey(PremiumShips.Paragon)) == "True");
        Check("skin ownership and equip gone", !ShipSkins.IsOwnedReal(2, 1) && ShipSkins.EquippedReal(2) == ShipSkins.Stock);
        Check("world progress gone", PlayerPrefs.GetInt(WorldManager.PrefsHighestWorld, 0) == 0 && PlayerPrefs.GetInt(WorldManager.PrefsCurrentWorld, 0) == 0);
        Check("codex discoveries gone", !PlayerPrefs.HasKey(Codex.PrefsKey) && !PlayerPrefs.HasKey(Codex.PaidKey));
        Check("kill tally gone", !PlayerPrefs.HasKey(DamageLog.KilledKey));
        Check("boosters gone", Boosters.BoughtDust == 0 && !PlayerPrefs.HasKey(Boosters.DustBoughtKey));
        Check("offer streak gone", !PlayerPrefs.HasKey(UpgradeOfferStore.StreakKey));
        Check("Remove Ads kept", AdFree.IsActive);
        Check("best score kept", PlayerPrefs.GetInt(RunScore.BestScoreKey, 0) == 4321);
        Check("audio setting kept", PlayerPrefs.GetInt(SoundSettings.SfxOnKey, 1) == 0);
        Check("offer toggle setting kept", PlayerPrefs.GetInt(UpgradeOfferStore.EnabledKey, 1) == 0);
        Check("tutorial flag kept", PlayerPrefs.GetString("HasDoneTut") == "true");

        // The wiped state is what the cloud snapshot sees too.
        var snap = ProgressSnapshot.Capture(0);
        Check("snapshot: no dust, no codex, Remove Ads kept", snap.currency == 0f && snap.codexSeen.Length == 0 && snap.adFree);
        Reset();
    }

    static void WipeWithDeveloperOff()
    {
        Reset();
        PlayerPrefs.SetFloat(StarDustLedger.CurrencyKey, 500f);
        PlayerPrefs.SetString(ShipId.OwnedKey(2), "True");
        PlayerPrefs.SetString(Codex.PrefsKey, "space_alien");
        Check("wipe with developer mode off returns false", !DeveloperDustTools.Wipe());
        Check("nothing wiped with developer mode off",
              Mathf.Approximately(PlayerPrefs.GetFloat(StarDustLedger.CurrencyKey), 500f)
              && PlayerPrefs.GetString(ShipId.OwnedKey(2)) == "True" && PlayerPrefs.HasKey(Codex.PrefsKey));

        // Two-tap confirm: the first tap arms, a second within the window confirms, a late one re-arms.
        float armed = -1f;
        Check("first tap only arms", !DeveloperDustTools.Confirm(ref armed, 10f));
        Check("second tap inside the window confirms", DeveloperDustTools.Confirm(ref armed, 11f));
        Check("confirming disarms", !DeveloperDustTools.Confirm(ref armed, 12f));
        Check("a late second tap re-arms instead", !DeveloperDustTools.Confirm(ref armed, 12f + DeveloperDustTools.ConfirmSeconds + 1f));
        Reset();
    }
}
