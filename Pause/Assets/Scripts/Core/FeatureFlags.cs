using UnityEngine;

// Feature flags: unfinished features that ship dark. A flag is a PlayerPrefs int
// "ff_<name>" (1 = on), DEFAULT OFF. There is no remote-config service yet; the
// flag is switched on by code (FeatureFlags.Set), a developer tool or, later,
// whatever backend writes it. Developer mode (DeveloperUnlocks) may also reveal
// a specific feature -- see PremiumShips.Visible.
public static class FeatureFlags
{
    public const string Prefix = "ff_";

    // The premium "Paragon" ship (id 16): hidden everywhere unless this flag or
    // developer mode is on, so the roster reads as the original 15 ships.
    public const string ParagonShip = "paragon_ship";

    public static string KeyOf(string flag) { return Prefix + flag; }

    public static bool IsOn(string flag) { return PlayerPrefs.GetInt(KeyOf(flag), 0) == 1; }

    public static void Set(string flag, bool on)
    {
        if (on) PlayerPrefs.SetInt(KeyOf(flag), 1);
        else PlayerPrefs.DeleteKey(KeyOf(flag));
    }
}
