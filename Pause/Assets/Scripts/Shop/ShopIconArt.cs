using System.Collections.Generic;
using UnityEngine;

// The Shop's pixel-art row icons (Resources/ShopIcons, 256x256, Point filter), one per star dust pack, per booster and Remove Ads.
// Source and previews live in Assets/Art/Shop~/icons.
public static class ShopIconArt
{
    static readonly Dictionary<string, Sprite> cache = new Dictionary<string, Sprite>();

    public static Sprite Get(string name)
    {
        Sprite s;
        if (cache.TryGetValue(name, out s) && s != null) return s;
        s = Resources.Load<Sprite>("ShopIcons/" + name);
        if (s != null) cache[name] = s;
        return s;
    }

    // The icon file for a row, or null for rows that keep their own art (Paragon).
    public static string NameFor(ShopRow r)
    {
        switch (r.kind)
        {
            case ShopRowKind.RemoveAds: return "remove_ads";
            case ShopRowKind.Pack:
                if (r.pack == null) return null;
                return r.pack.dust >= 5000 ? "pack_6000" : r.pack.dust >= 2000 ? "pack_2500" : "pack_850";
            case ShopRowKind.DustBooster: return "booster_dust_x2";
            case ShopRowKind.HazardClear: return "booster_hazard_clear";
            case ShopRowKind.HomingAtoms: return "booster_homing_atoms";
            default: return null;
        }
    }

    public static Sprite For(ShopRow r)
    {
        string n = NameFor(r);
        return n == null ? null : Get(n);
    }
}
