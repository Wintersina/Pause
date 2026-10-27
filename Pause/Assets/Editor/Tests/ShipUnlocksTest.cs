using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// The ownership gate on the last three dock berths (ShipUnlocks): the first
// two open at 4 owned ships, the last at 5. Ids are read from the gate, not
// hard-coded, so a roster price change keeps the test honest.
public static class ShipUnlocksTest
{
    static int fails;
    static void Check(string what, bool ok)
    {
        Debug.Log((ok ? "[UNL] PASS  " : "[UNL] FAIL  ") + what);
        if (!ok) fails++;
    }

    public static void Run() { TestHarness.Exit(Execute()); }

    // Owns the starter plus `n - 1` ungated ships (never a gated one).
    static void OwnExactly(int n)
    {
        for (int id = 1; id < shopingShips.RosterTotal; id++) PlayerPrefs.DeleteKey(ShipId.OwnedKey(id));
        PlayerPrefs.SetString(ShipId.OwnedKey(1), "True");
        int owned = 1;
        for (int id = 2; id < shopingShips.RosterTotal && owned < n; id++)
        {
            if (ShipUnlocks.RequiredOwned(id) > 0) continue;
            PlayerPrefs.SetString(ShipId.OwnedKey(id), "True");
            owned++;
        }
    }

    public static int Execute()
    {
        fails = 0;
        using var sandbox = new TestHarness.Sandbox();
        DeveloperUnlocks.SetEnabled(false);

        var g = ShipUnlocks.GatedIds;
        Check("three ships are gated, the dearest three in dock order, Paragon last",
              g.Length == 3 && g[2] == PremiumShips.Paragon && shopingShips.Roster[g[0]] == "Jade Phantom" &&
              shopingShips.Roster[g[1]] == "Gold Warden");
        Check("thresholds are 4, 4 and 5; other ships are ungated",
              ShipUnlocks.RequiredOwned(g[0]) == 4 && ShipUnlocks.RequiredOwned(g[1]) == 4 &&
              ShipUnlocks.RequiredOwned(g[2]) == 5 && ShipUnlocks.RequiredOwned(1) == 0 && ShipUnlocks.RequiredOwned(2) == 0);

        PlayerPrefs.SetFloat(StarDustLedger.CurrencyKey, 100000f);

        OwnExactly(3);
        Check("3 owned: OwnedCount reads the save", ShipUnlocks.OwnedCount() == 3);
        Check("3 owned: all three are locked", ShipUnlocks.IsLocked(g[0]) && ShipUnlocks.IsLocked(g[1]) && ShipUnlocks.IsLocked(g[2]));
        float dust = PlayerPrefs.GetFloat(StarDustLedger.CurrencyKey);
        Check("3 owned: a locked purchase is refused, takes no dust and grants nothing",
              !shopingShips.TryPurchase(g[0], 1f) && !shopingShips.TryPurchase(g[1], 1f) &&
              !ShipId.IsOwned(g[0]) && !ShipId.IsOwned(g[1]) &&
              Mathf.Approximately(PlayerPrefs.GetFloat(StarDustLedger.CurrencyKey), dust));
        Check("3 owned: the button reads OWN 4 SHIPS / OWN 5 SHIPS",
              ShipUnlocks.LockedButtonText(g[0]) == "OWN 4 SHIPS" && ShipUnlocks.LockedButtonText(g[2]) == "OWN 5 SHIPS" &&
              ShipUnlocks.LockedMessage(g[1]) == "OWN 4 SHIPS TO UNLOCK");

        OwnExactly(4);
        Check("4 owned: the first two open, the last stays locked",
              !ShipUnlocks.IsLocked(g[0]) && !ShipUnlocks.IsLocked(g[1]) && ShipUnlocks.IsLocked(g[2]));
        Check("4 owned: the last cannot be bought (dust path or store tap)",
              !shopingShips.TryPurchase(g[2], 1f) && !ParagonPurchase.RequestBuy() && !ShipId.IsOwned(g[2]));
        Check("4 owned: the first one can be bought",
              shopingShips.TryPurchase(g[0], 1f) && ShipId.IsOwned(g[0]));
        Check("the ship being bought is not counted: now 5 owned", ShipUnlocks.OwnedCount() == 5 && ShipUnlocks.OwnedCount(g[0]) == 4);
        Check("5 owned: the last opens", !ShipUnlocks.IsLocked(g[2]));

        OwnExactly(5);
        Check("5 owned: all three are purchasable", !ShipUnlocks.IsLocked(g[0]) && !ShipUnlocks.IsLocked(g[1]) && !ShipUnlocks.IsLocked(g[2]));
        Check("5 owned: a gated dust ship buys normally", shopingShips.TryPurchase(g[1], 1f) && ShipId.IsOwned(g[1]));

        // already owned: kept and usable whatever the count
        OwnExactly(1);
        PlayerPrefs.SetString(ShipId.OwnedKey(g[0]), "True");
        PlayerPrefs.SetString(ShipId.OwnedKey(PremiumShips.Paragon), "True");   // what a restore writes
        Check("owned gated ships are never locked and stay owned",
              !ShipUnlocks.IsLocked(g[0]) && !ShipUnlocks.IsLocked(PremiumShips.Paragon) &&
              ShipId.IsOwned(g[0]) && ShipId.IsOwned(PremiumShips.Paragon));
        ShipId.Equip(g[0]);
        Check("an owned gated ship can still be the equipped ship", ShipId.Equipped() == g[0]);
        PlayerPrefs.DeleteKey(ShipId.OwnedKey(PremiumShips.Paragon));
        PremiumShips.Grant(PremiumShips.Paragon);
        Check("a Paragon restore grants it whatever the count", ShipId.IsOwned(PremiumShips.Paragon));

        // the dock popup: locked button text, no BUY
        OwnExactly(3);
        PlayerPrefs.SetFloat(StarDustLedger.CurrencyKey, 100000f);
        EditorSceneLoader.Open("shopS6", OpenSceneMode.Single);
        ShopSceneExtender.Build();
        var dock = SpaceDock.Instance;
        if (dock == null) { Check("the dock was built", false); }
        else
        {
            dock.Select(g[0]);
            Check("locked popup: mode Locked, button says OWN 4 SHIPS",
                  dock.popup.CurrentMode == DockPopup.Mode.Locked && dock.popup.ButtonLabel.text == "OWN 4 SHIPS");
            dock.popup.Press();
            dock.Buy(g[0]);
            Check("pressing it or Buy never owns the ship or takes dust",
                  !ShipId.IsOwned(g[0]) && Mathf.Approximately(PlayerPrefs.GetFloat(StarDustLedger.CurrencyKey), 100000f));
            OwnExactly(4);
            dock.Select(g[1]);
            dock.Deselect();
            dock.Select(g[1]);
            Check("4 owned: the popup is BUY again", dock.popup.CurrentMode == DockPopup.Mode.Buy && dock.popup.ButtonLabel.text == "BUY");
        }

        DeveloperUnlocks.SetEnabled(false);
        Debug.Log("[UNL] failures: " + fails);
        return fails;
    }
}
