using System;
using System.Collections.Generic;
using UnityEngine;

// The player's progress as one small, versioned, JSON-serialisable record:
// everything that belongs to the player's account and nothing that belongs to
// the device (settings, developer switches, cloud bookkeeping).
//
// Capture() reads it from PlayerPrefs and Apply() writes it back, using the
// game's own key strings byte for byte ("PlayerCurrecny" is misspelt on
// purpose -- every existing install keeps its star dust under it).
//
// Developer mode (DeveloperUnlocks) overwrites HasDoneTut, highestWorld,
// currentWorld and boughtship* with an unlocked-everything state and parks the
// real values in developerBackup_* keys. Capture reads those backups instead, and Apply
// writes into them, so the cloud only ever sees the real progress and leaving
// developer mode restores the merged real progress.
[Serializable]
public class ProgressSnapshot
{
    public const int CurrentSchemaVersion = 1;

    // ---- PlayerPrefs keys (exact strings) ----
    public const string CurrencyKey = "PlayerCurrecny";
    public const string BoughtShipPrefix = "boughtship";
    public const string SpawnShipKey = "spawnShip";
    public const string HighestSpeedKey = "HighestSpeed";
    public const string CurrentWorldKey = "currentWorld";
    public const string HighestWorldKey = "highestWorld";
    public const string TutorialKey = "HasDoneTut";

    [Serializable]
    public struct Counter
    {
        public string key;
        public int value;
        public Counter(string key, int value) { this.key = key; this.value = value; }
    }

    public int schemaVersion = CurrentSchemaVersion;
    public long savedAtUtc;          // unix milliseconds of the last local change
    public float currency;
    public int[] boughtShips = new int[0];
    public int spawnShip;
    public float highestSpeed;
    public int currentWorld;
    public int highestWorld;
    public bool hasDoneTut;
    public Counter[] counters = new Counter[0];

    // Highest ship index whose ownership is synced (inclusive).
    public static int MaxShipIndex { get { return shopingShips.shipTotal; } }

    // Every lifetime counter that is synced: the per-category achievement
    // counts and the per-tier keys they migrated from.
    public static List<string> CounterKeys()
    {
        var keys = new List<string>();
        foreach (AchievementCategory category in Enum.GetValues(typeof(AchievementCategory)))
        {
            keys.Add(AchievementTiers.CounterKey(category));
            foreach (var tier in AchievementTiers.For(category))
                keys.Add(AchievementTiers.LegacyProgressKey(tier.id));
        }
        return keys;
    }

    public static ProgressSnapshot Fresh(long savedAtUtc)
    {
        return new ProgressSnapshot { savedAtUtc = savedAtUtc };
    }

    // ---- PlayerPrefs <-> snapshot ----

    public static ProgressSnapshot Capture(long savedAtUtc)
    {
        var s = new ProgressSnapshot { savedAtUtc = savedAtUtc };
        s.currency = PlayerPrefs.GetFloat(CurrencyKey, 0f);
        s.highestSpeed = PlayerPrefs.GetFloat(HighestSpeedKey, 0f);
        s.spawnShip = PlayerPrefs.GetInt(SpawnShipKey, 0);
        s.currentWorld = RealInt(CurrentWorldKey);
        s.highestWorld = RealInt(HighestWorldKey);
        s.hasDoneTut = RealString(TutorialKey) == "true";

        var ships = new List<int>();
        for (int i = 0; i <= MaxShipIndex; i++)
            if (RealString(BoughtShipPrefix + i) == "True") ships.Add(i);
        s.boughtShips = ships.ToArray();

        if (DeveloperUnlocks.Enabled)
        {
            // A developer run can move to a world, and pick a ship, that the
            // real progress hasn't unlocked.
            s.currentWorld = Mathf.Min(s.currentWorld, s.highestWorld);
            if (s.spawnShip != 0 && !ships.Contains(s.spawnShip))
                s.spawnShip = shopingShips.StarterShip;
        }

        var counters = new List<Counter>();
        foreach (AchievementCategory category in Enum.GetValues(typeof(AchievementCategory)))
        {
            // Count() seeds from the legacy per-tier keys on old installs.
            int count = AchievementTiers.Count(category);
            if (count > 0 || PlayerPrefs.HasKey(AchievementTiers.CounterKey(category)))
                counters.Add(new Counter(AchievementTiers.CounterKey(category), count));
        }
        foreach (string key in CounterKeys())
        {
            if (key.StartsWith("achv_count_")) continue;
            if (PlayerPrefs.HasKey(key)) counters.Add(new Counter(key, PlayerPrefs.GetInt(key)));
        }
        s.counters = counters.ToArray();
        return s;
    }

    // Overwrites every synced key with this snapshot. Keys the snapshot
    // doesn't have are removed, so applying a different account's (or a
    // fresh) snapshot leaves nothing of the previous one behind.
    public void Apply()
    {
        PlayerPrefs.SetFloat(CurrencyKey, currency);
        PlayerPrefs.SetFloat(HighestSpeedKey, highestSpeed);
        SetOrDelete(SpawnShipKey, spawnShip);
        WriteRealInt(CurrentWorldKey, currentWorld, currentWorld != 0);
        WriteRealInt(HighestWorldKey, highestWorld, highestWorld != 0);
        WriteRealString(TutorialKey, "true", hasDoneTut);

        var owned = new HashSet<int>(boughtShips ?? new int[0]);
        for (int i = 0; i <= MaxShipIndex; i++)
            WriteRealString(BoughtShipPrefix + i, "True", owned.Contains(i));

        var values = new Dictionary<string, int>();
        foreach (var c in counters ?? new Counter[0])
            if (!string.IsNullOrEmpty(c.key)) values[c.key] = c.value;
        foreach (string key in CounterKeys())
            if (!values.ContainsKey(key)) PlayerPrefs.DeleteKey(key);
        foreach (var pair in values) PlayerPrefs.SetInt(pair.Key, pair.Value);
    }

    static void SetOrDelete(string key, int value)
    {
        if (value != 0) PlayerPrefs.SetInt(key, value);
        else PlayerPrefs.DeleteKey(key);
    }

    // ---- Developer-mode aware access to the keys DeveloperUnlocks overrides ----

    static string BackupKey(string key) { return "developerBackup_" + key; }
    static string ExistsKey(string key) { return BackupKey(key) + "_exists"; }

    // While developer mode is on, every key it snapshotted has its real value
    // in the backup (DeveloperUnlocks leaves keys it has no snapshot of alone).
    static bool Overridden(string key)
    {
        return DeveloperUnlocks.Enabled && PlayerPrefs.HasKey(ExistsKey(key));
    }

    static string RealString(string key)
    {
        if (!Overridden(key)) return PlayerPrefs.GetString(key, "");
        return PlayerPrefs.GetInt(ExistsKey(key), 0) == 1 ? PlayerPrefs.GetString(BackupKey(key), "") : "";
    }

    static int RealInt(string key)
    {
        if (!Overridden(key)) return PlayerPrefs.GetInt(key, 0);
        return PlayerPrefs.GetInt(ExistsKey(key), 0) == 1 ? PlayerPrefs.GetInt(BackupKey(key), 0) : 0;
    }

    static void WriteRealString(string key, string value, bool present)
    {
        if (Overridden(key))
        {
            PlayerPrefs.SetInt(ExistsKey(key), present ? 1 : 0);
            if (present) PlayerPrefs.SetString(BackupKey(key), value);
            else PlayerPrefs.DeleteKey(BackupKey(key));
            return;
        }
        if (present) PlayerPrefs.SetString(key, value);
        else PlayerPrefs.DeleteKey(key);
    }

    static void WriteRealInt(string key, int value, bool present)
    {
        if (Overridden(key))
        {
            PlayerPrefs.SetInt(ExistsKey(key), present ? 1 : 0);
            if (present) PlayerPrefs.SetInt(BackupKey(key), value);
            else PlayerPrefs.DeleteKey(BackupKey(key));
            return;
        }
        if (present) PlayerPrefs.SetInt(key, value);
        else PlayerPrefs.DeleteKey(key);
    }

    // ---- JSON ----

    public string ToJson()
    {
        return JsonUtility.ToJson(this);
    }

    public enum ParseResult { Ok, Empty, Invalid, NewerSchema }

    // Empty input is "no cloud save yet". A blob written by a newer version of
    // the game is reported separately: it must not be overwritten.
    public static ParseResult TryParse(string json, out ProgressSnapshot snapshot)
    {
        snapshot = null;
        if (string.IsNullOrEmpty(json) || json.Trim().Length == 0) return ParseResult.Empty;
        try
        {
            snapshot = JsonUtility.FromJson<ProgressSnapshot>(json);
        }
        catch (Exception)
        {
            return ParseResult.Invalid;
        }
        if (snapshot == null || snapshot.schemaVersion <= 0) { snapshot = null; return ParseResult.Invalid; }
        if (snapshot.schemaVersion > CurrentSchemaVersion) { snapshot = null; return ParseResult.NewerSchema; }
        if (snapshot.boughtShips == null) snapshot.boughtShips = new int[0];
        if (snapshot.counters == null) snapshot.counters = new Counter[0];
        return ParseResult.Ok;
    }

    // The progress content without the timestamp, for change detection.
    public string ContentKey()
    {
        long at = savedAtUtc;
        savedAtUtc = 0;
        string json = ToJson();
        savedAtUtc = at;
        return json;
    }

    public int CounterValue(string key)
    {
        foreach (var c in counters ?? new Counter[0]) if (c.key == key) return c.value;
        return 0;
    }
}
