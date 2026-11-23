using System.Collections.Generic;
using UnityEngine;

// The small generic mark beside a shared-board name that says which platform the player is on: a robot head for
// Android, an apple for iOS. Drawn as pixel art in the game's neon pixel style (each mark has its own size and
// palette) and built once at runtime (point-filtered, no asset). The legacy Text stack (Orbitron) has no colour
// emoji, so a picture is the only way to show it. Deliberately NOT the Google, Android or Apple logos and never
// the Apple logo character.
public static class PlatformMark
{
    // On-screen box of the mark, in panel units, and the room it takes from the name column (mark plus gap). The
    // image keeps the sprite's aspect and fits inside the box.
    public const float Units = 24f, Reserve = 28f;

    static readonly Color32 Clear = new Color32(0, 0, 0, 0);
    static readonly Color32 White = new Color32(0xF4, 0xF2, 0xEA, 255);

    // Robot (12 x 8): o = ink outline, w = white antenna, f = fill, e = eye.
    static readonly string[] Robot =
    {
        "..w......w..",
        "...w....w...",
        "..oooooooo..",
        ".offffffffo.",
        ".offeffeffo.",
        ".offffffffo.",
        ".offffffffo.",
        "..oooooooo..",
    };

    static readonly Dictionary<char, Color32> RobotPalette = new Dictionary<char, Color32>
    {
        { 'o', new Color32(0x0B, 0x0A, 0x14, 255) },
        { 'w', White },
        { 'f', new Color32(0x9B, 0xE3, 0x5A, 255) },
        { 'e', new Color32(0x0B, 0x0A, 0x14, 255) },
    };

    // Apple (16 x 15): w = ink outline, r = fruit, d = shade, h = highlight, l = leaf, v = leaf vein, s = stem.
    static readonly string[] Apple =
    {
        ".........www....",
        "........wwlllw..",
        ".......wslvllw..",
        "....wwwwslllw...",
        "...wrrrrrrrrw...",
        "..wrhhrrrrrrrw..",
        ".wrrhrrrrrrddrw.",
        ".wrrrrrrrrrdddw.",
        ".wrrrrrrrrrdddw.",
        ".wrrrrrrrrddddw.",
        ".wrrrrrrrrdddrw.",
        "..wrrrrrrddddw..",
        "..wrrrrrddddrw..",
        "...wrrrddddrw...",
        "....wrrddddw....",
    };

    static readonly Dictionary<char, Color32> ApplePalette = new Dictionary<char, Color32>
    {
        { 'w', new Color32(0x0B, 0x0A, 0x14, 255) },
        { 'r', new Color32(0xE2, 0x48, 0x5A, 255) },
        { 'd', new Color32(0x9F, 0x31, 0x50, 255) },
        { 'h', new Color32(0xFF, 0xD0, 0xD8, 255) },
        { 'l', new Color32(0x5B, 0xC2, 0x4F, 255) },
        { 'v', new Color32(0x3B, 0x8F, 0x38, 255) },
        { 's', new Color32(0x9A, 0x67, 0x43, 255) },
    };

    static Sprite android, ios;

    public static bool Known(string platform) { return platform == SharedBoardLimits.Android || platform == SharedBoardLimits.Ios; }

    // Null for an empty or unknown platform (a native-board row has no mark).
    public static Sprite For(string platform)
    {
        if (platform == SharedBoardLimits.Android) return android != null ? android : (android = Build(Robot, RobotPalette));
        if (platform == SharedBoardLimits.Ios) return ios != null ? ios : (ios = Build(Apple, ApplePalette));
        return null;
    }

    static Sprite Build(string[] art, Dictionary<char, Color32> palette)
    {
        int h = art.Length, w = art[0].Length;
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.HideAndDontSave };
        var pixels = new Color32[w * h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                Color32 p;
                if (!palette.TryGetValue(art[y][x], out p)) p = Clear;
                pixels[(h - 1 - y) * w + x] = p;
            }
        tex.SetPixels32(pixels);
        tex.Apply(false, false);
        var sprite = Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(.5f, .5f), 100f, 0, SpriteMeshType.FullRect);
        sprite.hideFlags = HideFlags.HideAndDontSave;
        return sprite;
    }
}
