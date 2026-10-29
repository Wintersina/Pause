using System;
using UnityEngine;

public enum ShipSkinKind { Stock, Palette, Special }

// One colourway of one hull. Colours are the hull ("hull"-tone forms) base,
// its one shadow and its one highlight; a special also has a livery pattern
// painted in a second colour (accent*). Friendly red (lights, red trim) is
// the same on every skin and is not listed.
public struct ShipSkin
{
    public string name;               // "Stock", "Night", "Kaneda", ...
    public ShipSkinKind kind;
    public string hue, hueShadow, hueHighlight;
    public string pattern;            // null, or racing / chevrons / diagonal / split
    public string accent, accentShadow, accentHighlight;

    public Color primary { get { return ShipSkins.Parse(hue); } }
    public Color shadow { get { return ShipSkins.Parse(hueShadow); } }
    public Color highlight { get { return ShipSkins.Parse(hueHighlight); } }
    // The livery colour of a special; the primary for every other skin.
    public Color accentColor { get { return ShipSkins.Parse(accent ?? hue); } }
    public string DisplayName { get { return (name ?? "").ToUpperInvariant(); } }
}

// Purchasable hull colourways ("skins"): the table, the economy and the
// saved state. The skin list is the "skins" of the ship's data file (ShipArt/<Key>/<Key>_data.json).
// The art is ShipHullArt's (one sheet per ship and skin,
// written by Art/Resources/ShipArt/src~/build_restyle.py / build_skins.py into Art/Resources/ShipArt/<Key>/).
//
// Every ship has PerShip skins: 0 is Stock (its own hue, free and always
// owned), 1-3 are palette swaps, 4 is the ship's special livery.
//
// Saved (PlayerPrefs, synced by ProgressSnapshot):
//   shipSkin<id>        the equipped skin index (absent = 0, Stock)
//   skinOwned<id>_<n>   1 when skin n (1..PerShip-1) of ship id was bought
//
// Developer mode (DeveloperUnlocks) shows every skin as owned without
// writing any of those keys, the way the codex shows every entry: ownership
// is answered at read time, and an equip while it is on goes to
// developerShipSkin<id>, which is ignored (and cleared) once it is off. So
// switching it off leaves the real choice and ownership exactly as they were.
//
// The dock can preview a skin on the hull before it is bought (Preview);
// Shown(id) is what every renderer of the ship uses.
public static class ShipSkins
{
    public const int PerShip = 5;
    public const int Stock = 0;
    public const int Special = PerShip - 1;

    public const string EquippedKeyPrefix = "shipSkin";
    public const string OwnedKeyPrefix = "skinOwned";
    public const string DeveloperEquippedPrefix = "developerShipSkin";

    // ---------------------------------------------------------- economy
    // The one config table: a standard ship's skin prices by its dock row
    // (SpaceDock.RowOf, 1 = top = cheapest hulls): { palette, special } in
    // star dust. Later ships cost more; row 5 holds Gold Warden, the anchor
    // (1000 per colour, 2500 for the Special). Stock is always free. Rows past
    // the table use its last entry; a premium ship has its own PremiumShips.SkinPrice.
    public static readonly float[][] RowPrices =
    {
        new[] { 200f, 500f },     // row 1: Neon Comet, Volt Viper, Lightning
        new[] { 400f, 1000f },    // row 2
        new[] { 600f, 1500f },    // row 3
        new[] { 800f, 2000f },    // row 4
        new[] { 1000f, 2500f },   // row 5: ... Gold Warden
    };

    // A ship's first colour is its first heart and first weapon step
    // (SkinHearts, ShipWeaponUpgrades), priced low so a new player feels the
    // progression on their first run's dust. It is skin 1, the leftmost
    // purchasable swatch (0 is the stock hull); every other skin, the ships
    // of the last dock row and the premium ship keep their normal prices.
    public const float FirstColourPrice = 50f;

    // The last dock row holding a standard (dust-priced) ship.
    static int LastStandardRow
    {
        get
        {
            int last = 0;
            foreach (int id in ShipId.All)
                if (!PremiumShips.IsPremium(id)) last = Mathf.Max(last, SpaceDock.RowOf(id));
            return last;
        }
    }

    public static bool FirstColourDiscounted(int id)
    {
        return !PremiumShips.IsPremium(id) && SpaceDock.RowOf(id) < LastStandardRow;
    }

    // The price of a skin kind for a ship in dock row `row`.
    public static float PriceOf(ShipSkinKind kind, int row)
    {
        if (kind == ShipSkinKind.Stock) return 0f;
        var t = RowPrices[Mathf.Clamp(row, 1, RowPrices.Length) - 1];
        return kind == ShipSkinKind.Special ? t[1] : t[0];
    }

    public static float PriceOf(int id, int skin)
    {
        if (!Has(id, skin) || skin == Stock) return 0f;
        // a premium ship's skins are all one (high) price
        if (PremiumShips.IsPremium(id)) return PremiumShips.SkinPrice;
        if (skin == 1 && FirstColourDiscounted(id)) return FirstColourPrice;
        return PriceOf(Get(id, skin).kind, SpaceDock.RowOf(id));
    }

    // Raised with the ship id when its equipped or previewed skin changes.
    public static event Action<int> Changed;

    // ---------------------------------------------------------- skins

    static ShipSkin S(string name, ShipSkinKind kind, string hue, string shadow, string highlight,
                      string pattern = null, string accent = null, string accentShadow = null,
                      string accentHighlight = null)
    {
        return new ShipSkin
        {
            name = name, kind = kind, hue = hue, hueShadow = shadow, hueHighlight = highlight,
            pattern = pattern, accent = accent, accentShadow = accentShadow, accentHighlight = accentHighlight,
        };
    }

    // The ship's skins, index 0 = Stock (null when it has no data file).
    static ShipSkin[] RowOf(int id)
    {
        var d = ShipData.For(id);
        return d != null ? d.skinList : null;
    }

    public static bool Has(int id, int skin)
    {
        var row = RowOf(id);
        return row != null && skin >= 0 && skin < row.Length;
    }

    public static int CountFor(int id) { var row = RowOf(id); return row != null ? row.Length : 0; }

    public static ShipSkin Get(int id, int skin)
    {
        if (Has(id, skin)) return RowOf(id)[skin];
        if (Has(id, Stock)) return RowOf(id)[Stock];
        return S("Stock", ShipSkinKind.Stock, "#FFFFFF", "#FFFFFF", "#FFFFFF");
    }

    // Resource name of a non-stock sheet: "<ShipKey>_<SkinName>".
    public static string SheetName(int id, int skin)
    {
        string key = ShipId.KeyOf(id);
        return key == null || skin == Stock || !Has(id, skin) ? key : key + "_" + RowOf(id)[skin].name;
    }

    // ---------------------------------------------------------- keys

    // The strings are built once: the hull is drawn every frame (title-screen
    // traffic, the flying ship), and a per-frame string concat is garbage.
    const int KeyCacheShips = 32;
    static readonly string[] equippedKeys = new string[KeyCacheShips];
    static readonly string[] devKeys = new string[KeyCacheShips];
    static readonly string[] ownedKeys = new string[KeyCacheShips * PerShip];

    public static string EquippedKey(int id)
    {
        if (id < 0 || id >= KeyCacheShips) return EquippedKeyPrefix + id;
        return equippedKeys[id] ?? (equippedKeys[id] = EquippedKeyPrefix + id);
    }

    public static string OwnedKey(int id, int skin)
    {
        if (id < 0 || id >= KeyCacheShips || skin < 0 || skin >= PerShip) return OwnedKeyPrefix + id + "_" + skin;
        int k = id * PerShip + skin;
        return ownedKeys[k] ?? (ownedKeys[k] = OwnedKeyPrefix + id + "_" + skin);
    }

    public static string DeveloperEquippedKey(int id)
    {
        if (id < 0 || id >= KeyCacheShips) return DeveloperEquippedPrefix + id;
        return devKeys[id] ?? (devKeys[id] = DeveloperEquippedPrefix + id);
    }

    // ---------------------------------------------------------- ownership

    // The saved ownership, whatever developer mode shows.
    public static bool IsOwnedReal(int id, int skin)
    {
        if (!Has(id, skin)) return false;
        return skin == Stock || PlayerPrefs.GetInt(OwnedKey(id, skin), 0) == 1;
    }

    public static bool IsOwned(int id, int skin)
    {
        if (!Has(id, skin)) return false;
        return DeveloperUnlocks.Enabled || IsOwnedReal(id, skin);
    }

    // ---------------------------------------------------------- equipped

    // The saved choice, ignoring developer mode (what the cloud syncs).
    public static int EquippedReal(int id)
    {
        int skin = PlayerPrefs.GetInt(EquippedKey(id), Stock);
        return IsOwnedReal(id, skin) ? skin : Stock;
    }

    // The skin the ship flies in.
    public static int Equipped(int id)
    {
        if (ShipId.TutorialFlight && id == ShipId.TutorialShip) return Stock;   // the tutorial's stock Neon Comet
        if (DeveloperUnlocks.Enabled && PlayerPrefs.HasKey(DeveloperEquippedKey(id)))
        {
            int dev = PlayerPrefs.GetInt(DeveloperEquippedKey(id), Stock);
            return Has(id, dev) ? dev : Stock;
        }
        return EquippedReal(id);
    }

    // Equips an owned skin and saves at once. False (nothing written) if the
    // skin isn't owned.
    public static bool Equip(int id, int skin)
    {
        if (!IsOwned(id, skin)) return false;
        if (DeveloperUnlocks.Enabled) PlayerPrefs.SetInt(DeveloperEquippedKey(id), skin);
        else if (skin == Stock) PlayerPrefs.DeleteKey(EquippedKey(id));
        else PlayerPrefs.SetInt(EquippedKey(id), skin);
        if (previewShip == id) { previewShip = 0; previewSkin = Stock; }
        PrefsSaver.SaveNow();
        Raise(id);
        return true;
    }

    public enum PurchaseResult { Bought, AlreadyOwned, CantAfford, Invalid }

    // Spends the dust once, marks the skin owned, equips it and saves at once.
    public static PurchaseResult TryPurchase(int id, int skin)
    {
        if (!Has(id, skin) || !ShipId.IsOwned(id)) return PurchaseResult.Invalid;
        if (IsOwned(id, skin)) return PurchaseResult.AlreadyOwned;
        float price = PriceOf(id, skin);
        float dust = PlayerPrefs.GetFloat(StarDustLedger.CurrencyKey);
        if (dust < price) return PurchaseResult.CantAfford;
        PlayerPrefs.SetFloat(StarDustLedger.CurrencyKey, dust - price);
        PlayerPrefs.SetInt(OwnedKey(id, skin), 1);
        AchievementTracker.OnSkinBought(id, skin, price);
        Equip(id, skin);   // saves
        return PurchaseResult.Bought;
    }

    // Star dust still missing to buy a skin (0 if affordable).
    public static float ShortBy(int id, int skin)
    {
        return Mathf.Max(0f, PriceOf(id, skin) - PlayerPrefs.GetFloat(StarDustLedger.CurrencyKey));
    }

    // Developer mode switched off: forget its choices (the real ones were
    // never touched).
    public static void ClearDeveloperChoices()
    {
        for (int id = 1; ShipId.ArtKeyOf(id) != null; id++)
        {
            if (!PlayerPrefs.HasKey(DeveloperEquippedKey(id))) continue;
            PlayerPrefs.DeleteKey(DeveloperEquippedKey(id));
            Raise(id);
        }
    }

    // ---------------------------------------------------------- preview

    static int previewShip, previewSkin;

    public static bool IsPreviewing(int id) { return id > 0 && previewShip == id; }
    public static int PreviewShip { get { return previewShip; } }

    public static void SetPreview(int id, int skin)
    {
        if (!Has(id, skin)) return;
        int old = previewShip;
        if (skin == Equipped(id)) { previewShip = 0; previewSkin = Stock; }
        else { previewShip = id; previewSkin = skin; }
        if (old > 0 && old != id) Raise(old);
        Raise(id);
    }

    public static void ClearPreview()
    {
        int old = previewShip;
        previewShip = 0;
        previewSkin = Stock;
        if (old > 0) Raise(old);
    }

    // The skin the ship is drawn in right now: a dock preview, else equipped.
    public static int Shown(int id)
    {
        return previewShip == id && id > 0 ? previewSkin : Equipped(id);
    }

    // The colours the ship is drawn in right now (for the exhaust and any
    // per-ship tint). ShipSkins.Current(id).primary is the hull base colour.
    public static ShipSkin Current(int id) { return Get(id, Shown(id)); }

    static void Raise(int id)
    {
        if (Changed != null) Changed(id);
    }

    public static Color Parse(string hex)
    {
        Color c;
        return hex != null && ColorUtility.TryParseHtmlString(hex, out c) ? c : Color.white;
    }
}
