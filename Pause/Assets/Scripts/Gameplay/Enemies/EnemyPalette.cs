using UnityEngine;

// Runtime copies of the enemy art palette (Art/Enemies/src~/palette.env), for
// anything that tints to match the enemies (codex accents, effects). Field
// names are the env keys in PascalCase; EnemyRosterTest checks they agree.
// The player's red is deliberately absent: red means friendly.
public static class EnemyPalette
{
    public static readonly Color Ink = Hex(0x140C14);
    public static readonly Color Bone = Hex(0xF4EAD4);
    public static readonly Color Magenta = Hex(0xFF2E88);
    public static readonly Color BileLight = Hex(0xC8FF3A);
    public static readonly Color Cyan = Hex(0x6EF2EE);
    public static readonly Color Sodium = Hex(0xF2862B);
    public static readonly Color Ice = Hex(0x6AAED0);
    public static readonly Color Steel = Hex(0x5A6A88);
    public static readonly Color Bruise = Hex(0x74409A);

    // Each world's signature enemy light: Space magenta, Frost cyan, Verdant
    // bile, Ember sodium.
    public static Color WorldLight(int world)
    {
        switch (world)
        {
            case 1: return Cyan;
            case 2: return BileLight;
            case 3: return Sodium;
            default: return Magenta;
        }
    }

    public static string Html(Color c) => "#" + ColorUtility.ToHtmlStringRGB(c);

    static Color Hex(int rgb)
    {
        return new Color(((rgb >> 16) & 0xFF) / 255f, ((rgb >> 8) & 0xFF) / 255f, (rgb & 0xFF) / 255f);
    }
}
