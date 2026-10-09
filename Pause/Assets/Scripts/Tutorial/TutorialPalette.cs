using UnityEngine;

// Runtime colours for the tutorial's robot, bubble, guides, skip pill and end
// card. The art (Art/Resources/Tutorial) is a painted rustic steampunk kit:
// brass-framed navy panels, amber nixie displays, a red antenna lamp. Almost
// everything is baked into the sprites, so these only tint the white parts
// (voice pulse, touch guide), the text and its outline, and the scrims.
//
// Brass/amber is the hero colour, a cyan edge accent supports it, and red is
// kept for the antenna lamp and warnings. Text is paper on the bubble's
// navy panel; TutorialRobotTest pins the contrast.
public static class TutorialPalette
{
    public static readonly Color Ink = Hex(0x05060C);
    public static readonly Color Night = Hex(0x0A0B16);
    public static readonly Color Indigo = Hex(0x112338);
    public static readonly Color IndigoLight = Hex(0x1B3350);
    // The bubble's panel fill, sampled from tut_bubble.png.
    public static readonly Color Panel = Hex(0x0C1725);
    public static readonly Color Steel = Hex(0x6B7480);
    public static readonly Color SteelShade = Hex(0x2A2F3A);
    public static readonly Color SteelLight = Hex(0xB4BCC8);
    public static readonly Color Red = Hex(0xD8232C);
    public static readonly Color RedShade = Hex(0x86121F);
    // Brass-amber: highlights, the end card's dust accent, the voice pulse.
    public static readonly Color Orange = Hex(0xFFB83D);
    public static readonly Color OrangeLight = Hex(0xFDF06A);
    // Cyan edge accent of the panels.
    public static readonly Color Teal = Hex(0x1BA3C6);
    public static readonly Color TealShade = Hex(0x0F5E6A);
    public static readonly Color Magenta = Hex(0xFF2E88);
    public static readonly Color Paper = Hex(0xF4EAD4);

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
