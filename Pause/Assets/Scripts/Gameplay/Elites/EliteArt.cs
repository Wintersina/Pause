using System.Collections.Generic;
using UnityEngine;

// An elite's drawings: Resources/Elites/<World>/<key>.png (seven square
// cells: idle 0..3, tell, action, hit), sliced so one cell is the def's
// cellWorldSize in the world, plus the optional strips that take over from
// the procedural placeholders the moment they exist (see EliteDef):
// <key>_parked, <key>_liftoff, <key>_death -- any number of square cells.
//
// EliteFxArt below draws the placeholder FX in code (glow dots, dust puffs,
// heat streaks, shot drawings, debris sparks) in the house neon pixel style:
// hard-stepped rings, no smooth gradients.
public static class EliteArt
{
    public const int FrameCount = 7;
    public const int Idle0 = 0, IdleFrames = 4, Tell = 4, Action = 5, Hit = 6;
    public const float Tick = 1f / 24f;
    // Hold ticks per drawing (24 fps), the fighters' rhythm.
    public static readonly int[] IdleTicks = { 6, 3, 2, 3 };
    public const int HitTicks = 3;

    public enum Extra { Parked, Liftoff, Death }

    static readonly Dictionary<string, Sprite[]> cache = new Dictionary<string, Sprite[]>();
    static readonly HashSet<string> missing = new HashSet<string>();

    public static string WorldFolder(EliteDef def)
    {
        string w = def != null ? def.world : "";
        return string.IsNullOrEmpty(w) ? "" : char.ToUpperInvariant(w[0]) + w.Substring(1);
    }

    public static string StripPath(EliteDef def) => EliteCatalog.ArtFolder + "/" + WorldFolder(def) + "/" + def.key;

    public static string ExtraPath(EliteDef def, Extra e) => StripPath(def) + "_" + Suffix(e);

    public static string Suffix(Extra e) => e == Extra.Parked ? "parked" : e == Extra.Liftoff ? "liftoff" : "death";

    public static Sprite[] Frames(EliteDef def)
    {
        if (def == null) return null;
        return Load(StripPath(def), def, FrameCount);
    }

    public static Sprite Frame(EliteDef def, int i)
    {
        var f = Frames(def);
        return f == null ? null : f[Mathf.Clamp(i, 0, f.Length - 1)];
    }

    // Null until Codex delivers that strip.
    public static Sprite[] ExtraFrames(EliteDef def, Extra e)
    {
        if (def == null) return null;
        return Load(ExtraPath(def, e), def, 0);
    }

    public static bool HasExtra(EliteDef def, Extra e) => ExtraFrames(def, e) != null;

    // count 0: as many square cells as the strip holds.
    static Sprite[] Load(string path, EliteDef def, int count)
    {
        Sprite[] frames;
        if (cache.TryGetValue(path, out frames) && frames != null && frames.Length > 0 && frames[0] != null) return frames;
        if (missing.Contains(path) && Application.isPlaying) return null;
        var tex = Resources.Load<Texture2D>(path);
        if (tex == null) { missing.Add(path); return null; }
        int n = count > 0 ? count : Mathf.Max(1, tex.width / Mathf.Max(1, tex.height));
        float w = tex.width / (float)n;
        float ppu = tex.height / Mathf.Max(.01f, def.cellWorldSize);
        frames = new Sprite[n];
        for (int i = 0; i < n; i++)
        {
            frames[i] = Sprite.Create(tex, new Rect(i * w, 0, w, tex.height), new Vector2(.5f, .5f), ppu, 0, SpriteMeshType.FullRect);
            frames[i].name = def.key + "_" + i;
        }
        cache[path] = frames;
        return frames;
    }

    // Debris pieces: the idle drawing cut into a 3 x 3 grid of shards.
    public const int ShardCols = 3, ShardRows = 3;
    static readonly Dictionary<string, Sprite[]> shards = new Dictionary<string, Sprite[]>();

    public static Sprite[] Shards(EliteDef def)
    {
        if (def == null) return null;
        Sprite[] s;
        if (shards.TryGetValue(def.key, out s) && s != null && s[0] != null) return s;
        var f = Frame(def, Hit);
        if (f == null) return null;
        Rect r = f.textureRect;
        // the drawing sits inset in its cell: cut the middle 70%
        float inset = .15f;
        float x0 = r.x + r.width * inset, y0 = r.y + r.height * inset;
        float w = r.width * (1f - 2f * inset) / ShardCols, h = r.height * (1f - 2f * inset) / ShardRows;
        s = new Sprite[ShardCols * ShardRows];
        for (int row = 0; row < ShardRows; row++)
            for (int col = 0; col < ShardCols; col++)
            {
                var sub = new Rect(x0 + col * w, y0 + row * h, w, h);
                s[row * ShardCols + col] = Sprite.Create(f.texture, sub, new Vector2(.5f, .5f), f.pixelsPerUnit, 0, SpriteMeshType.FullRect);
            }
        shards[def.key] = s;
        return s;
    }

    // Where shard i sits in the drawing (world, before rotation).
    public static Vector2 ShardHome(EliteDef def, int i)
    {
        float span = def.cellWorldSize * .7f;
        int col = i % ShardCols, row = i / ShardCols;
        return new Vector2(((col + .5f) / ShardCols - .5f) * span, ((row + .5f) / ShardRows - .5f) * span);
    }
}

// The procedural placeholder drawings, built once (never per frame).
public static class EliteFxArt
{
    static Sprite glow, puff, streak, ring, bolt, slag, shell, shard, spark, sight;

    // A soft-looking dot drawn in four hard steps (white: tint it).
    public static Sprite Glow => glow != null ? glow : (glow = Disc("EliteGlow", 16, new[] { 1f, .78f, .45f, .2f }));
    // A lumpy dust puff (grey-white ramp; tinted dusty violet in use).
    public static Sprite Puff => puff != null ? puff : (puff = Blob("EliteDust", 24, 11));
    // A rising heat-shimmer streak.
    public static Sprite Streak => streak != null ? streak : (streak = StreakSprite());
    public static Sprite Ring => ring != null ? ring : (ring = RingSprite("EliteRing", 24));
    // Shots: white core, the colour comes from the tint (two layers).
    public static Sprite Bolt => bolt != null ? bolt : (bolt = Capsule("EliteBolt", 8, 16));
    public static Sprite Slag => slag != null ? slag : (slag = Blob("EliteSlag", 16, 23));
    public static Sprite Shell => shell != null ? shell : (shell = Capsule("EliteShell", 12, 24));
    public static Sprite Shard => shard != null ? shard : (shard = Diamond("EliteShard", 10, 16));
    public static Sprite Spark => spark != null ? spark : (spark = Diamond("EliteSpark", 6, 6));
    // The siege cannon's blinking sight line: a 1 x 8 bar.
    public static Sprite Sight => sight != null ? sight : (sight = Bar("EliteSight"));

    static Texture2D NewTex(int w, int h, string name)
    {
        var t = new Texture2D(w, h, TextureFormat.RGBA32, false);
        t.filterMode = FilterMode.Point;
        t.wrapMode = TextureWrapMode.Clamp;
        t.name = name;
        t.hideFlags = HideFlags.DontSave;
        return t;
    }

    static Sprite Make(Texture2D t, float ppu)
    {
        t.Apply(false, false);
        var s = Sprite.Create(t, new Rect(0, 0, t.width, t.height), new Vector2(.5f, .5f), ppu, 0, SpriteMeshType.FullRect);
        s.name = t.name;
        s.hideFlags = HideFlags.DontSave;
        return s;
    }

    // A disc of `n` px in hard alpha steps (centre .. rim). 1 world unit across.
    static Sprite Disc(string name, int n, float[] steps)
    {
        var t = NewTex(n, n, name);
        float c = (n - 1) * .5f;
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float d = Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c)) / (n * .5f);
                int band = Mathf.FloorToInt(d * steps.Length);
                float a = band < steps.Length ? steps[band] : 0f;
                t.SetPixel(x, y, new Color(1f, 1f, 1f, a));
            }
        return Make(t, n);
    }

    static Sprite RingSprite(string name, int n)
    {
        var t = NewTex(n, n, name);
        float c = (n - 1) * .5f;
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float d = Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c));
                float a = d > n * .5f - 2.5f && d <= n * .5f - .5f ? 1f : d > n * .5f - 4f && d <= n * .5f - 2.5f ? .45f : 0f;
                t.SetPixel(x, y, new Color(1f, 1f, 1f, a));
            }
        return Make(t, n);
    }

    // A lumpy cloud: three overlapping discs, a lighter top-left.
    static Sprite Blob(string name, int n, int seed)
    {
        var t = NewTex(n, n, name);
        var rng = new System.Random(seed);
        var cx = new float[3]; var cy = new float[3]; var r = new float[3];
        for (int i = 0; i < 3; i++)
        {
            cx[i] = n * (.35f + .3f * (float)rng.NextDouble());
            cy[i] = n * (.35f + .3f * (float)rng.NextDouble());
            r[i] = n * (.22f + .1f * (float)rng.NextDouble());
        }
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float best = 9f;
                for (int i = 0; i < 3; i++)
                {
                    float d = Mathf.Sqrt((x - cx[i]) * (x - cx[i]) + (y - cy[i]) * (y - cy[i])) / r[i];
                    if (d < best) best = d;
                }
                if (best > 1f) { t.SetPixel(x, y, Color.clear); continue; }
                float lit = (x + y) > n ? .75f : 1f;   // top-right lighter
                float v = best < .55f ? 1f : best < .85f ? .82f : .62f;
                t.SetPixel(x, y, new Color(v * lit, v * lit, v * lit, best < .85f ? 1f : .7f));
            }
        return Make(t, n);
    }

    static Sprite StreakSprite()
    {
        int w = 6, h = 24;
        var t = NewTex(w, h, "EliteHeat");
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float wob = Mathf.Sin(y * .7f) * 1.2f;
                float d = Mathf.Abs(x - (w - 1) * .5f - wob);
                float a = d < .8f ? .9f : d < 1.8f ? .4f : 0f;
                a *= 1f - Mathf.Abs(y - h * .5f) / (h * .5f);
                t.SetPixel(x, y, new Color(1f, 1f, 1f, a > .55f ? .8f : a > .2f ? .4f : 0f));
            }
        return Make(t, h);
    }

    // Upright capsule (points up the screen), hot white core and a body.
    static Sprite Capsule(string name, int w, int h)
    {
        var t = NewTex(w, h, name);
        float cx = (w - 1) * .5f;
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float rx = Mathf.Abs(x - cx) / (w * .5f);
                float ry = Mathf.Abs(y - (h - 1) * .5f) / (h * .5f);
                float d = Mathf.Max(rx, Mathf.Pow(ry, 3f));
                float a = d < .45f ? 1f : d < .8f ? .85f : d < 1f ? .45f : 0f;
                float v = d < .45f ? 1f : .8f;
                t.SetPixel(x, y, new Color(v, v, v, a));
            }
        return Make(t, h);
    }

    static Sprite Diamond(string name, int w, int h)
    {
        var t = NewTex(w, h, name);
        float cx = (w - 1) * .5f, cy = (h - 1) * .5f;
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float d = Mathf.Abs(x - cx) / (w * .5f) + Mathf.Abs(y - cy) / (h * .5f);
                float a = d < .5f ? 1f : d < 1f ? .7f : 0f;
                t.SetPixel(x, y, new Color(1f, 1f, 1f, a));
            }
        return Make(t, h);
    }

    static Sprite Bar(string name)
    {
        var t = NewTex(1, 8, name);
        for (int y = 0; y < 8; y++) t.SetPixel(0, y, new Color(1f, 1f, 1f, y % 4 < 2 ? 1f : 0f));
        var s = Sprite.Create(t, new Rect(0, 0, 1, 8), new Vector2(.5f, 0f), 8f, 0, SpriteMeshType.FullRect);
        t.Apply(false, false);
        s.name = name;
        s.hideFlags = HideFlags.DontSave;
        return s;
    }
}
