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
    public static readonly Color Ink = Hex(0x08071A);
    public static readonly Color Night = Hex(0x0E0C24);
    public static readonly Color Indigo = Hex(0x1C1950);
    public static readonly Color IndigoLight = Hex(0x2D2A72);
    public static readonly Color Steel = Hex(0x4A57A0);
    public static readonly Color SteelShade = Hex(0x2B3270);
    public static readonly Color SteelLight = Hex(0x8F9CE0);
    public static readonly Color Red = Hex(0xD8202A);
    public static readonly Color RedShade = Hex(0x8A0F17);
    public static readonly Color Orange = Hex(0xFF8A1E);
    public static readonly Color OrangeLight = Hex(0xFFC27A);
    public static readonly Color Teal = Hex(0x22E3CF);
    public static readonly Color TealShade = Hex(0x0E8F8C);
    public static readonly Color Magenta = Hex(0xE3297F);
    public static readonly Color Paper = Hex(0xF3EFE2);
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
