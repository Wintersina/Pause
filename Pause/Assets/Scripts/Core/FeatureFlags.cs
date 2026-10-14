using UnityEngine;

// Feature flags: switchable features. A flag is a PlayerPrefs int "ff_<name>"
// (1 = on, 0 = off); a flag with no stored value takes its declared default.
// There is no remote-config service yet; a flag is switched by code
// (FeatureFlags.Set), by the developer list in Options (developer mode only,
// see FeatureFlagOptions) or, later, whatever backend writes it.
//
// THE REGISTRY: every flag is a public const string field of this class
// carrying a [FeatureFlags.Flag] attribute (label + default). All lists the
// flags by reflection, so a new flag shows up in the Options developer list
// (and the tests) without touching any UI code.
public static class FeatureFlags
{
    public const string Prefix = "ff_";

    [System.AttributeUsage(System.AttributeTargets.Field)]
    public sealed class FlagAttribute : System.Attribute
    {
        public readonly string label;
        public readonly bool defaultOn;
        public FlagAttribute(string label, bool defaultOn = false) { this.label = label; this.defaultOn = defaultOn; }
    }

    // The premium "Paragon" ship (id 16). ON by default since 2026-10-13: it
    // shows in the dock as a locked COMING SOON premium berth (real-money IAP,
    // never free; PremiumShips). Switch it OFF and the roster reads as the
    // original 15 ships everywhere. Developer mode shows (and owns) it either way.
    [Flag("PARAGON SHIP", true)]
    public const string ParagonShip = "paragon_ship";

    // World weather and hazards (docs/world-weather-hazards.md): the rare push / pull weather events (tornado, sandstorm,
    // whirlpool...) and the ambient weather particles. No damage, only pushes. Two switches so either can be turned off.
    [Flag("WORLD HAZARDS", true)]
    public const string WorldHazards = "world_hazards";

    [Flag("WORLD WEATHER", true)]
    public const string WorldWeather = "world_weather";

    public struct Info
    {
        public string name, label;
        public bool defaultOn;
    }

    static Info[] all;
    static System.Collections.Generic.Dictionary<string, bool> defaults;

    // Every registered flag, in declaration order.
    public static Info[] All
    {
        get
        {
            if (all == null) Discover();
            return all;
        }
    }

    static void Discover()
    {
        var list = new System.Collections.Generic.List<Info>();
        var map = new System.Collections.Generic.Dictionary<string, bool>();
        var fields = typeof(FeatureFlags).GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
        foreach (var f in fields)
        {
            if (!f.IsLiteral || f.FieldType != typeof(string)) continue;
            var attr = (FlagAttribute)System.Attribute.GetCustomAttribute(f, typeof(FlagAttribute));
            if (attr == null) continue;
            string name = (string)f.GetRawConstantValue();
            list.Add(new Info { name = name, label = attr.label, defaultOn = attr.defaultOn });
            map[name] = attr.defaultOn;
        }
        defaults = map;
        all = list.ToArray();
    }

    public static bool DefaultOf(string flag)
    {
        if (defaults == null) Discover();
        bool on;
        return defaults.TryGetValue(flag, out on) && on;
    }

    // Hot paths ask every frame (ShipId.IsValid -> PremiumShips.Visible), so the
    // "ff_<name>" strings are built once, never per call.
    static readonly System.Collections.Generic.Dictionary<string, string> keys =
        new System.Collections.Generic.Dictionary<string, string>();

    public static string KeyOf(string flag)
    {
        string key;
        if (!keys.TryGetValue(flag, out key)) keys[flag] = key = Prefix + flag;
        return key;
    }

    public static bool IsOn(string flag)
    {
        int v = PlayerPrefs.GetInt(KeyOf(flag), -1);
        return v < 0 ? DefaultOf(flag) : v == 1;
    }

    // Setting a flag to its default clears the stored value (so a later change
    // of the default reaches players who never chose).
    public static void Set(string flag, bool on)
    {
        if (on == DefaultOf(flag)) PlayerPrefs.DeleteKey(KeyOf(flag));
        else PlayerPrefs.SetInt(KeyOf(flag), on ? 1 : 0);
    }
}
