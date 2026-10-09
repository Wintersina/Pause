using System;
using System.Collections.Generic;
using UnityEngine;

// The ONE table of store ids: internal achievement id -> Google Play Games id
// (Android) and Game Center id (iOS).
//
//   Android  Starts from the ids the project already had in the Play Console
//            (the 13 legacy achievements that mean the same thing as a new
//            one, kept so those console entries keep working); every other id
//            is a placeholder "TODO_android_<id>". The owner pastes the real
//            ids of the 60 rows they create into Resources/AchievementStoreIds.csv
//            (internal_id,CgkI...), which overrides this table at runtime; no
//            code edit needed.
//   iOS      me.sinaserati.Pause.ach_<id> (the scheme the exported Game Center
//            CSV uses). None exists in App Store Connect yet, so reporting on
//            iOS stays off until IosIdsConfirmed is switched on.
//
// A placeholder (or an unconfirmed iOS id) is never sent anywhere
// (IsPlaceholder / AchievementStores).
public static class AchievementIds
{
    public const string IosPrefix = "me.sinaserati.Pause.";
    public const string AchievementIosPrefix = IosPrefix + "ach_";
    public const string PlaceholderPrefix = "TODO_";
    public const string OverrideResource = "AchievementStoreIds";

    // Flip once the 60 Game Center achievements exist in App Store Connect with the ids above.
    public static bool IosIdsConfirmed = false;

    // Legacy Play Console achievements that map one-to-one onto a new one.
    static readonly string[] LegacyAndroid =
    {
        "meta_first_flight", "CgkI3eXNjrQcEAIQBw",   // tutorial_complete
        "meta_logged_on", "CgkI3eXNjrQcEAIQGg",      // logged_on_successfully
        "speed_flash", "CgkI3eXNjrQcEAIQCQ",
        "speed_speedster", "CgkI3eXNjrQcEAIQCA",
        "speed_super_sonic", "CgkI3eXNjrQcEAIQCg",
        "pause_first", "CgkI3eXNjrQcEAIQDA",         // paused
        "pause_blink_kill", "CgkI3eXNjrQcEAIQDQ",    // correct_pause
        "ship_first", "CgkI3eXNjrQcEAIQHQ",          // buy_your_first_ship
        "ship_all", "CgkI3eXNjrQcEAIQHg",            // buy_all_ships
        "stars_150", "CgkI3eXNjrQcEAIQGw",           // stars
        "stars_1000", "CgkI3eXNjrQcEAIQHA",          // stars_2
        "deaths_10", "CgkI3eXNjrQcEAIQFw",           // death_3
        "deaths_100", "CgkI3eXNjrQcEAIQGQ",          // death_5
    };

    static Dictionary<string, string> android;

    public static bool IsIOS
    {
        get
        {
#if UNITY_IOS
            return true;
#else
            return false;
#endif
        }
    }

    public static bool IsPlaceholder(string id)
    {
        return string.IsNullOrEmpty(id) || id.StartsWith(PlaceholderPrefix, StringComparison.Ordinal);
    }

    public static string Placeholder(string internalId) { return PlaceholderPrefix + "android_" + internalId; }

    // Forget the cached table (tests, after the override file changes).
    public static void Reload() { android = null; }

    static Dictionary<string, string> Table()
    {
        if (android != null) return android;
        var map = new Dictionary<string, string>(AchievementCatalog.All.Length);
        foreach (var d in AchievementCatalog.All) map[d.id] = Placeholder(d.id);
        for (int i = 0; i + 1 < LegacyAndroid.Length; i += 2) map[LegacyAndroid[i]] = LegacyAndroid[i + 1];
        var file = Resources.Load<TextAsset>(OverrideResource);
        if (file != null) ApplyOverrides(map, file.text);
        android = map;
        return map;
    }

    // "internal_id,android_id" lines; blank lines, # comments, a header row and
    // unknown ids are ignored; an empty or placeholder id keeps the default.
    public static void ApplyOverrides(Dictionary<string, string> map, string csv)
    {
        if (string.IsNullOrEmpty(csv)) return;
        foreach (string raw in csv.Split('\n'))
        {
            string line = raw.Trim();
            if (line.Length == 0 || line[0] == '#') continue;
            int comma = line.IndexOf(',');
            if (comma <= 0) continue;
            string id = line.Substring(0, comma).Trim();
            string value = line.Substring(comma + 1).Trim().Trim('"');
            if (!map.ContainsKey(id) || IsPlaceholder(value)) continue;
            map[id] = value;
        }
    }

    public static string AndroidId(AchievementDef def)
    {
        string id;
        return def != null && Table().TryGetValue(def.id, out id) ? id : null;
    }

    public static string IosId(AchievementDef def)
    {
        return def == null ? null : AchievementIosPrefix + def.id;
    }

    public static string IosId(string internalId) { return AchievementIosPrefix + internalId; }

    public static string ForCurrentPlatform(AchievementDef def)
    {
        return IsIOS ? IosId(def) : AndroidId(def);
    }

    // Whether a report for this achievement may be sent from this platform.
    public static bool IsReportable(AchievementDef def, bool ios)
    {
        if (def == null) return false;
        if (ios) return IosIdsConfirmed && !IsPlaceholder(IosId(def));
        return !IsPlaceholder(AndroidId(def));
    }
}
