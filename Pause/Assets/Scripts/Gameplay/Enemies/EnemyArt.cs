using System.Collections.Generic;
using UnityEngine;

// Loads an enemy's flipbook strip (Resources/Enemies/<key>.png, seven 128 u
// frames butted left to right: 0-3 idle, 4-5 tell, 6 hit flash) and slices it
// into sprites sized so one frame covers the role's FrameWorldSize in the
// world. Same runtime-slicing approach as RailBombSprites.
public static class EnemyArt
{
    static readonly Dictionary<string, Sprite[]> cache = new Dictionary<string, Sprite[]>();

    public static Sprite[] Frames(EnemyDef def)
    {
        if (def == null) return null;
        Sprite[] frames;
        // Sprite.Create()d sprites die with an editor scene swap; Unity's null
        // check catches that and the strip is sliced again.
        if (cache.TryGetValue(def.key, out frames) && frames != null && frames[0] != null) return frames;

        var tex = Resources.Load<Texture2D>(def.StripPath);
        if (tex == null) return null;
        int n = EnemyRoster.FrameCount;
        float w = tex.width / (float)n;
        float ppu = tex.height / Mathf.Max(.01f, def.FrameWorldSize);
        frames = new Sprite[n];
        for (int i = 0; i < n; i++)
        {
            frames[i] = Sprite.Create(tex, new Rect(i * w, 0, w, tex.height), new Vector2(.5f, .5f), ppu);
            frames[i].name = def.key + "_" + i;
        }
        cache[def.key] = frames;
        return frames;
    }

    public static Sprite Frame(EnemyDef def, int index)
    {
        var frames = Frames(def);
        return frames == null ? null : frames[Mathf.Clamp(index, 0, frames.Length - 1)];
    }
}
