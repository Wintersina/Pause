using System.Collections.Generic;
using UnityEngine;

// The one canonical ship identity.
//
// A ship's id is its roster index: 1..Count, the same number as
//   - its position in shopingShips.Roster / Prices,
//   - the dock berth that shows it (SpaceDock.bays[id]),
//   - its ownership key   "boughtship<id>" == "True",
//   - the saved selection "spawnShip" == id,
//   - its GameObject name "ship<id>" ("ship<id>(Clone)" when spawned),
//   - every per-ship table (ShipNozzles, ShipPowerTable, ...), indexed by id.
// Id 0 is "none" (the roster's unused slot 0). The numbers are what the
// released game wrote to PlayerPrefs (1-3 are the original three hulls), so
// they must never be renumbered or reordered: add new ships at the end.
//
// Code that needs "which ship is this?" should ask here rather than parse
// names or read PlayerPrefs itself:
//   ShipId.Equipped()      the ship gameS1 / the tutorial will fly
//   ShipId.Of(go)          the id a ship GameObject carries
//   ShipId.IsOwned(id)     ownership (the starter is always owned)
//   ShipId.KeyOf(id)       stable art key ("NeonComet", "Turtle", ...)
public static class ShipId
{
    public const int None = 0;
    public const int Starter = shopingShips.StarterShip;

    // PlayerPrefs keys. Strings and stored values are unchanged from the
    // released game.
    public const string SelectedKey = "spawnShip";
    public const string OwnedKeyPrefix = "boughtship";

    public static int First { get { return 1; } }
    public static int Last { get { return shopingShips.shipTotal - 1; } }
    public static int Count { get { return shopingShips.shipTotal - 1; } }

    public static bool IsValid(int id) { return id >= 1 && id < shopingShips.shipTotal; }

    public static IEnumerable<int> All
    {
        get { for (int id = First; id <= Last; id++) yield return id; }
    }

    // Stable, human-readable keys: the base name of each hull's art. Use these
    // if a table ever needs to survive a roster reorder (it must not happen,
    // but a key makes a mismatch obvious in data and tests).
    static readonly string[] keys =
    {
        null, "NeonComet", "VoltViper", "SolarFang", "CrimsonHalo",
        "IonLancer", "JadePhantom", "GoldWarden",
        "Lightning", "Ligher", "Paranoid", "Ninja", "Saboteur", "UFO",
        "Dove", "Turtle",
    };

    public static string KeyOf(int id) { return IsValid(id) && id < keys.Length ? keys[id] : null; }

    public static int FromKey(string key)
    {
        if (string.IsNullOrEmpty(key)) return None;
        for (int id = 1; id < keys.Length; id++)
            if (keys[id] == key) return id;
        return None;
    }

    public static string NameOf(int id) { return IsValid(id) ? shopingShips.NameFor(id) : null; }

    // Retro80s hulls (1-7) carry their own damage art; the originals (8+) are
    // single images.
    public static bool IsRetro(int id) { return id >= 1 && id <= 7; }

    // ---------------------------------------------------------- ownership

    public static string OwnedKey(int id) { return OwnedKeyPrefix + id; }

    public static bool IsOwned(int id)
    {
        return id == Starter || (IsValid(id) && PlayerPrefs.GetString(OwnedKey(id)) == "True");
    }

    // ---------------------------------------------------------- selection

    // The raw saved selection, unvalidated (may be 0, out of range or unowned).
    public static int SavedSelection { get { return PlayerPrefs.GetInt(SelectedKey, Starter); } }

    // The ship that will actually fly: the saved selection if it is a real,
    // owned ship, otherwise the starter. Everything that spawns or describes
    // the player's ship uses this.
    public static int Equipped()
    {
        int id = SavedSelection;
        return IsValid(id) && IsOwned(id) ? id : Starter;
    }

    // Saves the selection (callers decide when to flush to disk).
    public static void Equip(int id)
    {
        if (IsValid(id)) PlayerPrefs.SetInt(SelectedKey, id);
    }

    // ---------------------------------------------------------- objects

    public static string ObjectName(int id) { return "ship" + id; }

    // The id a ship GameObject carries in its name ("ship7", "ship7(Clone)"),
    // or None when it isn't named like a ship.
    public static int Of(GameObject ship)
    {
        if (ship == null) return None;
        string name = ship.name.Replace("(Clone)", "").Trim();
        if (!name.StartsWith("ship")) return None;
        int id;
        return int.TryParse(name.Substring(4), out id) && IsValid(id) ? id : None;
    }

    public static int Of(GameObject ship, int fallback)
    {
        int id = Of(ship);
        return id != None ? id : fallback;
    }
}
