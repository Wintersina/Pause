using UnityEngine;

// The four hazard rock TYPES of the Space asteroid field (SpaceAsteroidField): ice, molten, iron, geode. Each has its
// own whole-rock art and its own chunk art (what a split leaves), both a 4-frame blink strip (2048x512 = 4 x 512x512:
// only the glowing accent pulses: dim, mid, bright, mid), and its own Codex entry (hazard_space_rock_<key>).
// The drift backdrop (SpaceAsteroidDrift) keeps asteroid_00..02: hazards and scenery look different.
//
// Art lives in Resources/Worlds/Space/Hazards/: space_hazard_rock_<key>_blink.png, space_hazard_rock_<key>_chunk_blink.png
// (strips) and space_hazard_rock_<key>.png (frame-0 still: the Codex icon). Sprite[]s are built once and shared.
public sealed class SpaceRockType
{
    public readonly string key, name, lore, codexId, objectName;
    public readonly float wholeFill, chunkFill;     // the alpha footprint's share of the 512 px frame (measured)
    public SpaceRockType(string key, string name, string lore, float wholeFill, float chunkFill)
    {
        this.key = key; this.name = name; this.lore = lore;
        this.wholeFill = wholeFill; this.chunkFill = chunkFill;
        codexId = SpaceRockTypes.CodexPrefix + key;
        objectName = "space_rock_" + key;
    }

    Sprite[] whole, chunk;
    Sprite still;
    public Sprite[] Whole { get { return Frames(ref whole, SpaceRockTypes.Folder + "space_hazard_rock_" + key + "_blink"); } }
    public Sprite[] Chunk { get { return Frames(ref chunk, SpaceRockTypes.Folder + "space_hazard_rock_" + key + "_chunk_blink"); } }
    public Sprite Still
    {
        get
        {
            if (still == null) still = Resources.Load<Sprite>(SpaceRockTypes.Folder + "space_hazard_rock_" + key);
            return still;
        }
    }
    public float Fill(bool isChunk) { return isChunk ? chunkFill : wholeFill; }
    public void Forget() { whole = chunk = null; still = null; }

    // Cuts the strip into BlinkFrames sprites (once; null while the art is missing).
    static Sprite[] Frames(ref Sprite[] cache, string path)
    {
        if (cache != null && cache.Length > 0 && cache[0] != null) return cache;
        var strip = Resources.Load<Sprite>(path);
        if (strip == null) return cache = null;
        var tex = strip.texture;
        int n = SpaceRockTypes.BlinkFrames;
        int w = tex.width / n;
        var frames = new Sprite[n];
        for (int i = 0; i < n; i++)
            frames[i] = Sprite.Create(tex, new Rect(i * w, 0, w, tex.height), new Vector2(.5f, .5f), strip.pixelsPerUnit);
        return cache = frames;
    }
}

public static class SpaceRockTypes
{
    public const string Folder = "Worlds/Space/Hazards/";
    public const string CodexPrefix = "hazard_space_rock_";
    public const int BlinkFrames = 4;
    public const float BlinkFps = 7f;                       // the loop's rate (6-8 fps)
    // dim, mid, bright, mid: a tint pulse in code (the strip's own glow does the rest once the real strips are in)
    public static readonly float[] PulseLevel = { .84f, .92f, 1f, .92f };

    public static readonly SpaceRockType[] All =
    {
        new SpaceRockType("ice", "Rime Rock",
            "A drifting comet core, frosted white and full of cold blue light. Warm it with a shot and it cracks into icy shards.",
            .86f, .45f),
        new SpaceRockType("molten", "Ember Rock",
            "Still-hot rock streaked with glowing orange seams that throb like a pulse. It breaks into smouldering lumps.",
            .86f, .48f),
        new SpaceRockType("iron", "Iron Rock",
            "A dense, rust-dark lump of ore with a vein of light running through it. It breaks into heavy scrap.",
            .86f, .49f),
        new SpaceRockType("geode", "Geode Rock",
            "A plain shell with a glittering crystal heart that blinks from inside. Crack it and the crystals scatter.",
            .86f, .45f),
    };

    public static int Count { get { return All.Length; } }
    public static SpaceRockType Of(int type) { return All[((type % All.Length) + All.Length) % All.Length]; }

    public static SpaceRockType ByCodexId(string id)
    {
        if (id == null) return null;
        for (int i = 0; i < All.Length; i++) if (All[i].codexId == id) return All[i];
        return null;
    }

    public static SpaceRockType ByObjectName(string name)
    {
        if (string.IsNullOrEmpty(name)) return null;
        for (int i = 0; i < All.Length; i++) if (name.StartsWith(All[i].objectName, System.StringComparison.Ordinal)) return All[i];
        return null;
    }

    public static void Forget() { for (int i = 0; i < All.Length; i++) All[i].Forget(); }
}
