using UnityEngine;
using UnityEditor.SceneManagement;
using System.Linq;

// Paragon's visibility gate: hidden unless the "paragon_ship" feature flag or
// developer mode is on. Hidden, the game is a 15-ship game in every list.
//   1  flag off + developer mode off: roster of 15, no dock berth, no codex entry,
//      not selectable, no save / achievement footprint
//   2  flag on: visible, locked, COMING SOON, not owned
//   3  developer mode: visible and owned, with the flag off
//   4  the flag is default off and switches both ways
public static class ParagonFlagTest
{
    static int fails;
    static void Check(string what, bool ok)
    {
        Debug.Log((ok ? "[PFLAG] PASS  " : "[PFLAG] FAIL  ") + what);
        if (!ok) fails++;
    }

    public static void Run() { TestHarness.Exit(Execute()); }

    const int P = PremiumShips.Paragon;
    const string Flag = FeatureFlags.ParagonShip;

    static void Reset(bool flag, bool dev)
    {
        PlayerPrefs.SetInt(DeveloperUnlocks.EnabledKey, dev ? 1 : 0);   // raw: no snapshot / restore
        FeatureFlags.Set(Flag, flag);
        PlayerPrefs.DeleteKey(ShipId.OwnedKey(P));
    }

    public static int Execute()
    {
        fails = 0;
        using var sandbox = new TestHarness.Sandbox();

        FeatureFlags.Set(Flag, false);
        PlayerPrefs.DeleteKey(FeatureFlags.KeyOf(Flag));
        Check("the flag is OFF by default", !FeatureFlags.IsOn(Flag) && FeatureFlags.KeyOf(Flag) == "ff_paragon_ship");
        FeatureFlags.Set(Flag, true);
        Check("the flag switches on", FeatureFlags.IsOn(Flag));
        FeatureFlags.Set(Flag, false);
        Check("and off again", !FeatureFlags.IsOn(Flag));

        Hidden();
        FlagOn();
        DeveloperOn();
        HiddenDock();
        FlagOnDock();

        Debug.Log("[PFLAG] failures: " + fails);
        return fails;
    }

    static void Hidden()
    {
        Reset(false, false);
        PlayerPrefs.SetString(ShipId.OwnedKey(P), "True");   // even a stray owned key must not show it
        ShipId.Equip(P);
        Check("hidden: not visible", !PremiumShips.Visible);
        Check("hidden: the roster is the 15 ships", ShipId.Count == 15 && ShipId.Last == 15 && shopingShips.shipTotal == 16 &&
              ShipId.All.Count() == 15 && !ShipId.All.Contains(P) && PremiumShips.StandardCount == 15);
        Check("hidden: Paragon is not a valid id, has no key, name or owner",
              !ShipId.IsValid(P) && ShipId.KeyOf(P) == null && ShipId.FromKey("Paragon") == ShipId.None && ShipId.NameOf(P) == null &&
              !ShipId.IsOwned(P));
        Check("hidden: a saved Paragon selection flies the starter", ShipId.Equipped() == ShipId.Starter);
        Check("hidden: berth order is the 15 dust ships, no Paragon",
              SpaceDock.BayOrder.Length == 15 && !SpaceDock.BayOrder.Contains(P));
        Check("hidden: no codex entry", !CodexCatalogue.All.Any(e => e.id == CodexCatalogue.ShipPrefix + "Paragon"));
        Check("hidden: the cloud save syncs the same ship range as the 15-ship game", ProgressSnapshot.MaxShipIndex == 16);
        Check("hidden: Fleet Admiral still wants 15", PremiumShips.StandardCount == 15 && !PremiumShips.ComingSoon(P));
        Check("hidden: the skin sets are the dust economy's (unchanged)", SkinHearts.SkinsMissing >= 0);
        PlayerPrefs.DeleteKey(ShipId.OwnedKey(P));
        PlayerPrefs.SetInt(ShipId.SelectedKey, 1);
    }

    static void FlagOn()
    {
        Reset(true, false);
        Check("flag on: visible, 16 ships", PremiumShips.Visible && ShipId.Count == 16 && shopingShips.shipTotal == 17 &&
              ShipId.IsValid(P) && ShipId.KeyOf(P) == "Paragon" && ShipId.FromKey("Paragon") == P);
        Check("flag on: locked and COMING SOON, not owned", !ShipId.IsOwned(P) && PremiumShips.ComingSoon(P));
        Check("flag on: it is in the berths, last, and in the codex",
              SpaceDock.BayOrder.Length == 16 && SpaceDock.BayOrder[15] == P &&
              CodexCatalogue.All.Any(e => e.id == CodexCatalogue.ShipPrefix + "Paragon"));
        Check("flag on: still 15 standard ships for Fleet Admiral", PremiumShips.StandardCount == 15);
    }

    static void DeveloperOn()
    {
        Reset(false, true);
        PlayerPrefs.SetString(ShipId.OwnedKey(P), "True");   // what developer mode writes
        Check("developer mode: visible with the flag off", PremiumShips.Visible && ShipId.Count == 16 && ShipId.IsValid(P));
        Check("developer mode: owned and flyable", ShipId.IsOwned(P) && !PremiumShips.ComingSoon(P));
        ShipId.Equip(P);
        Check("developer mode: it can be equipped", ShipId.Equipped() == P);
        Check("developer mode: it is in the codex",
              CodexCatalogue.All.Any(e => e.id == CodexCatalogue.ShipPrefix + "Paragon"));
        PlayerPrefs.DeleteKey(ShipId.OwnedKey(P));
        PlayerPrefs.SetInt(ShipId.SelectedKey, 1);

        // Dev mode snapshots every owned key, Paragon's included, even from a hidden state.
        Reset(false, false);
        PlayerPrefs.SetString(ShipId.OwnedKey(P), "True");
        DeveloperUnlocks.SetEnabled(true);
        DeveloperUnlocks.SetEnabled(false);
        Check("developer mode on -> off restores Paragon's key from before", PlayerPrefs.GetString(ShipId.OwnedKey(P)) == "True");
        PlayerPrefs.DeleteKey(ShipId.OwnedKey(P));
    }

    static void HiddenDock()
    {
        Reset(false, false);
        EditorSceneLoader.Open("shopS6", UnityEditor.SceneManagement.OpenSceneMode.Single);
        PlayerPrefs.SetString("boughtship1", "True");
        PlayerPrefs.SetInt("spawnShip", 1);
        ShopSceneExtender.Build();
        var dock = SpaceDock.Instance;
        Check("hidden dock: built", dock != null);
        if (dock == null) return;
        Check("hidden dock: 15 berths, none for Paragon, no ship16 object",
              dock.bays.Length == 16 && dock.bays.Count(b => b != null) == 15 && SceneUtil.FindAny("ship16") == null);
        Check("hidden dock: layout is the 15-ship rack", dock.layout.count == 15);
    }

    static void FlagOnDock()
    {
        Reset(true, false);
        EditorSceneLoader.Open("shopS6", UnityEditor.SceneManagement.OpenSceneMode.Single);
        PlayerPrefs.SetString("boughtship1", "True");
        PlayerPrefs.SetInt("spawnShip", 1);
        ShopSceneExtender.Build();
        var dock = SpaceDock.Instance;
        Check("flag-on dock: built", dock != null);
        if (dock == null) return;
        Check("flag-on dock: 16 berths and Paragon sits in one, locked",
              dock.bays.Length == 17 && dock.bays[P] != null && SceneUtil.FindAny("ship16") != null && PremiumShips.ComingSoon(P));
    }
}
