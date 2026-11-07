using UnityEngine;

// The small generic mark beside a shared-board name that says which platform the player is on: a robot head for
// Android, an apple for iOS. Drawn here as 12 x 12 pixel art in the game's neon pixel style and built once at
// runtime (point-filtered, no asset). The legacy Text stack (Orbitron) has no colour emoji, so a picture is the
// only way to show it. Deliberately NOT the Google, Android or Apple logos and never the Apple logo character.
public static class PlatformMark
{
    public const int Size = 12;
    // On-screen size of the mark, in panel units, and the room it takes from the name column (mark plus gap).
    public const float Units = 24f, Reserve = 28f;

    // o = ink outline, f = fill, e = eye / detail, l = leaf. 12 columns each.
    static readonly string[] Robot =
    {
        "..o......o..",
        "...o....o...",
        "..oooooooo..",
        ".offffffffo.",
        ".offeffeffo.",
        ".offffffffo.",
        ".offffffffo.",
        "..oooooooo..",
    };

    static readonly string[] Apple =
    {
        "......ol....",
        "..ooo.oo.oo.",
        ".offfoffffo.",
        ".offffffffo.",
        ".offffffffo.",
        ".offffffffo.",
        "..offffffo..",
        "...oo..oo...",
    };

    static Sprite android, ios;

    public static bool Known(string platform) { return platform == SharedBoardLimits.Android || platform == SharedBoardLimits.Ios; }

    // Null for an empty or unknown platform (a native-board row has no mark).
    public static Sprite For(string platform)
    {
        if (platform == SharedBoardLimits.Android) return android != null ? android : (android = Build(Robot, new Color32(0x9B, 0xE3, 0x5A, 255)));
        if (platform == SharedBoardLimits.Ios) return ios != null ? ios : (ios = Build(Apple, new Color32(0xF2, 0xF0, 0xE6, 255)));
        return null;
    }

    static Sprite Build(string[] art, Color32 fill)
    {
        var ink = new Color32(0x0B, 0x0A, 0x14, 255);
        var leaf = new Color32(0x5B, 0xC2, 0x4F, 255);
        var eye = new Color32(0x0B, 0x0A, 0x14, 255);
        int h = art.Length;
        var tex = new Texture2D(Size, h, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.HideAndDontSave };
        var pixels = new Color32[Size * h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < Size; x++)
            {
                char c = art[y][x];
                Color32 p = c == 'o' ? ink : c == 'f' ? fill : c == 'e' ? eye : c == 'l' ? leaf : new Color32(0, 0, 0, 0);
                pixels[(h - 1 - y) * Size + x] = p;
            }
        tex.SetPixels32(pixels);
        tex.Apply(false, false);
        var sprite = Sprite.Create(tex, new Rect(0, 0, Size, h), new Vector2(.5f, .5f), 100f);
        sprite.hideFlags = HideFlags.HideAndDontSave;
        return sprite;
    }
}
