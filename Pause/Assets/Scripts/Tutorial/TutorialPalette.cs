using UnityEngine;

// Runtime colours for the tutorial's robot, bubble, guides, skip pill and end
// card: 80s anime, Akira (1988) palette, flat cel style. Deep blue-black and
// indigo base, Kaneda red as the hero colour, sodium orange, teal/cyan, a
// sparse magenta.
//
// The art's colours are baked from Art/UI/Tutorial/src~/palette.env (each
// tut_*.svg uses @NAME@ placeholders that render.sh fills in). These are the
// same values for things tinted at runtime; TutorialRobotTest fails if the
// two ever drift. Field names are the env names in PascalCase.
public static class TutorialPalette
{
    public static readonly Color Ink = Hex(0x140C14);
    public static readonly Color Night = Hex(0x0E1424);
    public static readonly Color Indigo = Hex(0x1A1F45);
    public static readonly Color IndigoLight = Hex(0x2A2E6B);
    public static readonly Color Steel = Hex(0x5A6A88);
    public static readonly Color SteelShade = Hex(0x262D44);
    public static readonly Color SteelLight = Hex(0xA3B4CC);
    public static readonly Color Red = Hex(0xD8232C);
    public static readonly Color RedShade = Hex(0x86121F);
    public static readonly Color Orange = Hex(0xF2862B);
    public static readonly Color OrangeLight = Hex(0xFFB43C);
    public static readonly Color Teal = Hex(0x1FB5B9);
    public static readonly Color TealShade = Hex(0x0F5E6A);
    public static readonly Color Magenta = Hex(0xFF2E88);
    public static readonly Color Paper = Hex(0xF4EAD4);
    public static readonly Color TemplateShade = Hex(0xB4B4B4);

    // Secondary text: paper at 60%.
    public static readonly Color Muted = new Color(Paper.r, Paper.g, Paper.b, .6f);

    public static Color Hex(int rgb)
    {
        return new Color(((rgb >> 16) & 0xFF) / 255f, ((rgb >> 8) & 0xFF) / 255f, (rgb & 0xFF) / 255f, 1f);
    }

    // "#RRGGBB" for rich text.
    public static string Html(Color c)
    {
        return "#" + ColorUtility.ToHtmlStringRGB(c);
    }
}
