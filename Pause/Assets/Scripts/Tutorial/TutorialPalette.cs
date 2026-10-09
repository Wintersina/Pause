using UnityEngine;

// Runtime colours for the tutorial's robot, bubble, guides, skip pill and end
// card. The art (Art/Resources/Tutorial) is a low-res cyberpunk pixel kit: a
// stern gunmetal robot with a CRT visor, dark navy HUD panels with cyan edges
// and corner brackets, cyan/magenta display pixels, a red antenna lamp. Almost
// everything is baked into the sprites, so these only tint the white parts
// (voice pulse, touch guide), the text and its outline, and the scrims.
//
// Cyan is the edge accent, amber the highlight, and red is kept for the
// antenna lamp and warnings. Hot magenta is only a pixel accent (display
// bars, the arrow): at about 5:1 on the panel it is too weak for body text.
// Text is cool paper on the bubble's navy panel; TutorialRobotTest pins the
// contrast.
public static class TutorialPalette
{
    // The kit's outline ink (05060C in the sprites).
    public static readonly Color Ink = Hex(0x05060C);
    public static readonly Color Night = Hex(0x0A0B16);
    public static readonly Color Indigo = Hex(0x112338);
    public static readonly Color IndigoLight = Hex(0x1B3350);
    // The panels' fill, sampled from tut_bubble.png (and card/button).
    public static readonly Color Panel = Hex(0x0C1725);
    // The panels' gunmetal rim.
    public static readonly Color Steel = Hex(0x34414E);
    public static readonly Color SteelShade = Hex(0x2A2F3A);
    public static readonly Color SteelLight = Hex(0xB4BCC8);
    public static readonly Color Red = Hex(0xD8232C);
    public static readonly Color RedShade = Hex(0x86121F);
    // Amber: highlights, the end card's dust accent, the touch guide.
    public static readonly Color Orange = Hex(0xFFB83D);
    public static readonly Color OrangeLight = Hex(0xFDF06A);
    // Cyan edge accent of the panels (0BD0F6 in the sprites): the voice
    // pulse and the end card's second stat.
    public static readonly Color Cyan = Hex(0x0BD0F6);
    public static readonly Color CyanShade = Hex(0x07516B);
    public static readonly Color Magenta = Hex(0xFF2E88);
    // Body text: a cool off-white.
    public static readonly Color Paper = Hex(0xE8F4FF);

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
