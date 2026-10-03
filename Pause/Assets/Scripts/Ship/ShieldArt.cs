using UnityEngine;

// Shared art for the contour shield, rendered from SVG by
// Art/Shield/src~/render.sh into Resources/Shield. Loaded once and sliced into
// flipbook frames; the static references are re-created if a scene change
// destroyed them.
public static class ShieldArt
{
    // Akira palette (docs/art-style.md section 1), flat tones.
    public static readonly Color32 Ink = new Color32(0x14, 0x0C, 0x14, 255);       // INK
    public static readonly Color32 Line = new Color32(0x6E, 0xF2, 0xEE, 255);      // CYAN
    public static readonly Color32 LineDim = new Color32(0x1F, 0xB5, 0xB9, 255);   // TEAL
    public static readonly Color32 Dash = new Color32(0xFF, 0xB4, 0x3C, 255);      // AMBER sodium kick
    public static readonly Color32 Hot = new Color32(0xF4, 0xEA, 0xD4, 255);       // BONE
    public static readonly Color32 ImpactWhite = new Color32(255, 255, 255, 255);       // 1-tick impact only
    public static readonly Color32 PlateLit = new Color32(255, 255, 255, 255);     // texture as authored
    public static readonly Color32 PlateShadow = new Color32(150, 170, 200, 255);  // side away from the light
    public static readonly Color32 Fill = new Color32(0x1F, 0xB5, 0xB9, 46);       // TEAL, flat tint
    public static readonly Color32 Clear = new Color32(0, 0, 0, 0);

    static Material material;
    static Texture2D atlas;
    static Sprite[] impact, spark, shards;

    public static Material Material
    {
        get
        {
            if (material != null) return material;
            var shader = Shader.Find("Sprites/Default");
            material = new Material(shader) { name = "~ShieldContour", hideFlags = HideFlags.DontSave };
            material.mainTexture = Atlas;
            return material;
        }
    }

    public static Texture2D Atlas
    {
        get
        {
            if (atlas != null) return atlas;
            atlas = Resources.Load<Texture2D>("Shield/shield_atlas");
            if (atlas == null) atlas = Texture2D.whiteTexture;
            return atlas;
        }
    }

    public static Sprite[] Impact { get { return Frames(ref impact, "Shield/shield_impact", 4); } }
    public static Sprite[] Spark { get { return Frames(ref spark, "Shield/shield_spark", 4); } }
    public static Sprite[] Shards { get { return Frames(ref shards, "Shield/shield_shards", 4); } }

    static Sprite[] Frames(ref Sprite[] frames, string path, int count)
    {
        if (frames != null && frames.Length == count && frames[0] != null) return frames;
        var tex = Resources.Load<Texture2D>(path);
        frames = new Sprite[count];
        if (tex == null) tex = Texture2D.whiteTexture;
        float w = tex.width / (float)count;
        for (int i = 0; i < count; i++)
            frames[i] = Sprite.Create(tex, new Rect(i * w, 0, w, tex.height), new Vector2(.5f, .5f), 100f);
        return frames;
    }
}
