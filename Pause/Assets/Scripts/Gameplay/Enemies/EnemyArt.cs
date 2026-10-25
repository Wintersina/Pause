using System.Collections.Generic;
using UnityEngine;

// Loads an enemy's flipbook strip (Resources/Enemies/<key>.png, seven 128 u
// frames butted left to right: 0-3 idle, 4-5 tell, 6 hit flash) and slices it
// into sprites sized so one frame covers the role's FrameWorldSize in the
// world. The rail mines are the exception: their seven frames come from the
// neon pixel-art atlas (RailMineArt), one row per world.
public static class EnemyArt
{
    static readonly Dictionary<string, Sprite[]> cache = new Dictionary<string, Sprite[]>();

    public static Sprite[] Frames(EnemyDef def) { return Frames(def, 0); }

    // `variant` > 0: a re-coloured copy of the strip (ObsidianShards); anything else has none and gets its own strip.
    public static Sprite[] Frames(EnemyDef def, int variant)
    {
        if (def == null) return null;
        string path = ObsidianShards.Applies(def) ? ObsidianShards.StripPath(variant) : null;
        string id = path ?? def.key;
        Sprite[] frames;
        // Sprite.Create()d sprites die with an editor scene swap; Unity's null
        // check catches that and the strip is sliced again.
        if (cache.TryGetValue(id, out frames) && frames != null && frames[0] != null) return frames;

        if (def.role == EnemyRole.Mine)
        {
            frames = RailMineArt.Flipbook(def.world);
            if (frames == null) return null;
            cache[id] = frames;
            return frames;
        }

        var tex = Resources.Load<Texture2D>(path ?? def.StripPath);
        if (tex == null) return null;
        int n = EnemyRoster.FrameCount;
        float w = tex.width / (float)n;
        float ppu = tex.height / Mathf.Max(.01f, def.FrameWorldSize);
        frames = new Sprite[n];
        for (int i = 0; i < n; i++)
        {
            frames[i] = Sprite.Create(tex, new Rect(i * w, 0, w, tex.height), new Vector2(.5f, .5f), ppu);
            frames[i].name = id + "_" + i;
        }
        cache[id] = frames;
        return frames;
    }

    public static Sprite Frame(EnemyDef def, int index)
    {
        var frames = Frames(def);
        return frames == null ? null : frames[Mathf.Clamp(index, 0, frames.Length - 1)];
    }
}
