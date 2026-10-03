using UnityEngine;

// The art guide's palette (docs/art-style.md §1, docs/art-samples/src/akira.py)
// for everything tinted or coloured in code: HUD, death panel, world banner,
// menus, dock. One place, so a palette change is one edit.
public static class AkiraPalette
{
    // Night / sky
    public static readonly Color Night0 = Hex(0x070A16);
    public static readonly Color Night1 = Hex(0x0E1424);
    public static readonly Color Indigo0 = Hex(0x1A1F45);
    public static readonly Color Indigo1 = Hex(0x2A2E6B);
    public static readonly Color Dusk = Hex(0x3A2A5C);
    // Hero: Kaneda red
    public static readonly Color Red = Hex(0xD8232C);
    public static readonly Color RedShadow = Hex(0x86121F);
    public static readonly Color RedHi = Hex(0xFF5B45);
    // City glow
    public static readonly Color Sodium = Hex(0xF2862B);
    public static readonly Color Amber = Hex(0xFFB43C);
    public static readonly Color SodiumShadow = Hex(0xA9481A);
    // Neon
    public static readonly Color Teal = Hex(0x1FB5B9);
    public static readonly Color Cyan = Hex(0x6EF2EE);
    public static readonly Color TealShadow = Hex(0x0F5E6A);
    // Accent (sparingly)
    public static readonly Color Magenta = Hex(0xFF2E88);
    // Ink + highlight
    public static readonly Color Ink = Hex(0x140C14);
    public static readonly Color Bone = Hex(0xF4EAD4);
    // Card fill and muted type used by the UI samples (ui.py CARD / MUTED)
    public static readonly Color Card = Hex(0x151B30);
    public static readonly Color Muted = Hex(0x8C93B8);
    public static readonly Color Hairline = Hex(0x2E3560);  // ui.py inner panel line
    // Player metal
    public static readonly Color Gun = Hex(0x2C2D40);
    public static readonly Color GunHi = Hex(0x5A5C78);

    public static Color Hex(int rgb, float alpha = 1f)
    {
        return new Color(((rgb >> 16) & 0xFF) / 255f, ((rgb >> 8) & 0xFF) / 255f, (rgb & 0xFF) / 255f, alpha);
    }

    public static Color WithAlpha(Color c, float a) { c.a = a; return c; }
}
