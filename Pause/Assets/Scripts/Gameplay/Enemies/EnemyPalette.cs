using UnityEngine;

// Runtime copies of the enemy art palette (Art/Enemies/src~/palette.env), for
// anything that has to match a world's enemies -- codex accents, effects and
// the per-world bosses (Theme). Field names are the env keys in PascalCase;
// EnemyRosterTest checks they agree. The player's red is deliberately
// absent: red means friendly.
public static class EnemyPalette
{
    public static readonly Color Ink = Hex(0x140C14);
    public static readonly Color Bone = Hex(0xF4EAD4);
    public static readonly Color Gun = Hex(0x2C2D40);
    public static readonly Color Steel = Hex(0x5A6A88);
    public static readonly Color SteelSh = Hex(0x262D44);
    public static readonly Color SteelHi = Hex(0xA3B4CC);
    public static readonly Color Bruise = Hex(0x74409A);
    public static readonly Color BruiseSh = Hex(0x3A1E52);
    public static readonly Color BruiseHi = Hex(0xA86CD0);
    public static readonly Color Bile = Hex(0x8FA84E);
    public static readonly Color BileSh = Hex(0x3E5229);
    public static readonly Color BileHi = Hex(0xD4E68E);
    public static readonly Color BileLight = Hex(0xC8FF3A);
    public static readonly Color Magenta = Hex(0xFF2E88);
    public static readonly Color MagentaSh = Hex(0x8E1450);
    public static readonly Color Cyan = Hex(0x6EF2EE);
    public static readonly Color TealSh = Hex(0x0F5E6A);
    public static readonly Color Amber = Hex(0xFFB43C);
    public static readonly Color Sodium = Hex(0xF2862B);
    public static readonly Color SodiumSh = Hex(0xA9481A);
    public static readonly Color Ice = Hex(0x6AAED0);
    public static readonly Color IceSh = Hex(0x2C557E);
    public static readonly Color IceHi = Hex(0xCDF4F8);
    public static readonly Color Moss = Hex(0x6E9A5A);
    public static readonly Color Char = Hex(0x6A5462);
    public static readonly Color CharSh = Hex(0x2A1C26);
    public static readonly Color CharHi = Hex(0xA08A9A);

    // One world's enemy look, for art that must sit in the same cast (the
    // end-of-level bosses): hull base / one shadow / one highlight, a
    // secondary accent material, the signature light and its dimmed tone.
    // The same table lives in Art/Enemies/src~/common.py (WORLD_THEMES) as
    // palette.env token names.
    public struct Theme
    {
        public string world;
        public Color hull, hullShadow, hullHighlight, accent, light, lightDim, ink, bone;
        public TargetExplosion.Kind explosion;
    }

    public static Theme ThemeFor(int world)
    {
        switch (world)
        {
            case 1: return new Theme { world = "frost", hull = Ice, hullShadow = IceSh, hullHighlight = IceHi,
                                       accent = Steel, light = Cyan, lightDim = TealSh, ink = Ink, bone = Bone,
                                       explosion = TargetExplosion.Kind.Ice };
            case 2: return new Theme { world = "verdant", hull = Bile, hullShadow = BileSh, hullHighlight = BileHi,
                                       accent = Bruise, light = BileLight, lightDim = BileSh, ink = Ink, bone = Bone,
                                       explosion = TargetExplosion.Kind.Spore };
            case 3:
            case 4: // Tide: Ember's cast until its own palette (add-world phase 12)
                return new Theme { world = "ember", hull = Char, hullShadow = CharSh, hullHighlight = CharHi,
                                       accent = Gun, light = Sodium, lightDim = SodiumSh, ink = Ink, bone = Bone,
                                       explosion = TargetExplosion.Kind.Magma };
            default: return new Theme { world = "space", hull = Steel, hullShadow = SteelSh, hullHighlight = SteelHi,
                                        accent = Bruise, light = Magenta, lightDim = MagentaSh, ink = Ink, bone = Bone,
                                        explosion = TargetExplosion.Kind.Metal };
        }
    }

    // Each world's signature enemy light.
    public static Color WorldLight(int world) => ThemeFor(world).light;

    public static string Html(Color c) => "#" + ColorUtility.ToHtmlStringRGB(c);

    static Color Hex(int rgb)
    {
        return new Color(((rgb >> 16) & 0xFF) / 255f, ((rgb >> 8) & 0xFF) / 255f, (rgb & 0xFF) / 255f);
    }
}
