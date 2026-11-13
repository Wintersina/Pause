using UnityEngine;

// Four extra rock types for Space's decorative drift field (SpaceAsteroidDrift): ice, molten, iron, geode, drawn in
// the pixel-art house style with a 4-frame blink strip each (2048x512 = 4 x 512x512; only the glowing accent pulses:
// dim, mid, bright, mid). Pure scenery: they share the drift's grey tint, haze, size, sway and tumble with the
// old asteroid_00..02 and never have a body of any kind.
//
// Art: Resources/Worlds/Space/DriftRocks/space_hazard_rock_<key>_blink.png (mipmapped: the rocks are drawn small).
// The Sprite[]s are cut once and shared by every rock of the type.
public sealed class SpaceRockType
{
    public readonly string key;
    public SpaceRockType(string key) { this.key = key; }

    Sprite[] frames;
    public Sprite[] Frames
    {
        get
        {
            if (frames != null && frames.Length > 0 && frames[0] != null) return frames;
            var strip = Resources.Load<Sprite>(SpaceRockTypes.Folder + "space_hazard_rock_" + key + "_blink");
            if (strip == null) return null;
            var tex = strip.texture;
            int n = SpaceRockTypes.BlinkFrames, w = tex.width / n;
            frames = new Sprite[n];
            for (int i = 0; i < n; i++)
            {
                frames[i] = Sprite.Create(tex, new Rect(i * w, 0, w, tex.height), new Vector2(.5f, .5f), strip.pixelsPerUnit);
                frames[i].name = "space_rock_" + key + "_" + i;
            }
            return frames;
        }
    }
    public void Forget() { frames = null; }
}

public static class SpaceRockTypes
{
    public const string Folder = "Worlds/Space/DriftRocks/";
    public const int BlinkFrames = 4;
    public const float BlinkFps = 7f;                       // the loop's rate (6-8 fps)

    public static readonly SpaceRockType[] All =
    {
        new SpaceRockType("ice"), new SpaceRockType("molten"), new SpaceRockType("iron"), new SpaceRockType("geode"),
    };
    public static int Count { get { return All.Length; } }
    public static void Forget() { for (int i = 0; i < All.Length; i++) All[i].Forget(); }
}
