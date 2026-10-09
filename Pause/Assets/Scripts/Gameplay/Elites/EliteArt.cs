using System.Collections.Generic;
using UnityEngine;

// An elite's drawings: Resources/Elites/<World>/<key>.png (square cells;
// which is which is the def's EliteCells map -- by default idle 0..3, tell,
// action, hit), sliced so one cell is the def's cellWorldSize in the world, plus the optional strips that take over from
// the procedural placeholders the moment they exist (see EliteDef):
// <key>_parked, <key>_liftoff, <key>_death -- any number of square cells.
//
// EliteFxArt below draws the placeholder FX in code (glow dots, dust puffs,
// heat streaks, shot drawings, debris sparks) in the house neon pixel style:
// hard-stepped rings, no smooth gradients.
public static class EliteArt
{
    public const int FrameCount = 7;
    // The default (Ember) layout; a def's `cells` may name others.
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
        return Load(StripPath(def), def, 0);
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
        var f = Frame(def, def.cells.Debris);
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
    static Sprite glow, puff, streak, ring, bolt, slag, shell, shard, spark, sight, pool, slab, orb, plate;

    // A soft-looking dot drawn in four hard steps (white: tint it).
    public static Sprite Glow => glow != null ? glow : (glow = Disc("EliteGlow", 16, new[] { 1f, .78f, .45f, .2f }));
    // A lumpy dust puff (grey-white ramp; tinted dusty violet in use).
    public static Sprite Puff => puff != null ? puff : (puff = Blob("EliteDust", 24, 11));
    // A rising heat-shimmer streak.
    public static Sprite Streak => streak != null ? streak : (streak = StreakSprite());
    public static Sprite Ring => ring != null ? ring : (ring = RingSprite("EliteRing", 24));
    // Shots: white core, the colour comes from the tint (two layers). Every
    // shot is drawn HOSTILE -- pointed, angular, hard-edged, facing its
    // flight (a dart, a finned shell, a chevron, a spiked burr) -- never a
    // round capsule, gem or nugget an atom could be mistaken for
    // (PickupGlow: atoms are the round, soft, glowing things).
    public static Sprite Bolt => bolt != null ? bolt : (bolt = Dart("EliteBolt", 10, 20, 0f));
    public static Sprite Slag => slag != null ? slag : (slag = Burr("EliteSlag", 19, 6));
    // A landed resin pool: a flat, lumpy puddle (wider than tall).
    public static Sprite Pool => pool != null ? pool : (pool = Puddle("ElitePool", 24, 14, 31));
    public static Sprite Shell => shell != null ? shell : (shell = Dart("EliteShell", 14, 24, .8f));
    public static Sprite Shard => shard != null ? shard : (shard = Chevron("EliteShard", 12, 16));
    public static Sprite Spark => spark != null ? spark : (spark = Diamond("EliteSpark", 6, 6));
    // The siege cannon's blinking sight line: a 1 x 8 bar.
    public static Sprite Sight => sight != null ? sight : (sight = Bar("EliteSight"));

    // floe_cast's ice slab: Frost's own floating chunk of ice (frost_rock_chunk's
    // first cell), the slag blob if that is missing.
    public static Sprite Slab
    {
        get
        {
            if (slab != null) return slab;
            var d = EnemyRoster.Find(SlabArtKey);
            slab = d != null ? EnemyArt.Frame(d, 0) : null;
            if (slab == null) slab = Slag;
            return slab;
        }
    }
    public const string SlabArtKey = "frost_rock_chunk";
    // frost_bloom's cryo orb: a hard-stepped disc with a bright rim.
    public static Sprite Orb => orb != null ? orb : (orb = Disc("EliteOrb", 14, new[] { 1f, 1f, .7f, .95f }));
    // armour_shatter's ice plate rim: a chevron, apex at the top (its pivot).
    public static Sprite Plate => plate != null ? plate : (plate = Chevron("ElitePlate", 72, 44));

    static Sprite Chevron(string name, int w, int h)
    {
        var t = NewTex(w, h, name);
        float cx = (w - 1) * .5f, slope = (w * .5f - 2f) / (h - 1f);
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int r = h - 1 - y;                       // rows down from the apex
                float d = Mathf.Abs(Mathf.Abs(x - cx) - r * slope);
                float a = d < 1.2f ? 1f : d < 2.4f ? .55f : 0f;
                t.SetPixel(x, y, new Color(1f, 1f, 1f, a));
            }
        t.Apply(false, false);
        var s = Sprite.Create(t, new Rect(0, 0, w, h), new Vector2(.5f, 1f), w, 0, SpriteMeshType.FullRect);
        s.name = name;
        s.hideFlags = HideFlags.DontSave;
        return s;
    }

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

    // A flat puddle: overlapping ellipses, hard-stepped rim.
    static Sprite Puddle(string name, int w, int h, int seed)
    {
        var t = NewTex(w, h, name);
        var rng = new System.Random(seed);
        var cx = new float[4]; var cy = new float[4]; var rx = new float[4]; var ry = new float[4];
        for (int i = 0; i < 4; i++)
        {
            cx[i] = w * (.3f + .4f * (float)rng.NextDouble());
            cy[i] = h * (.4f + .2f * (float)rng.NextDouble());
            rx[i] = w * (.2f + .08f * (float)rng.NextDouble());
            ry[i] = h * (.28f + .1f * (float)rng.NextDouble());
        }
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float best = 9f;
                for (int i = 0; i < 4; i++)
                {
                    float dx = (x - cx[i]) / rx[i], dy = (y - cy[i]) / ry[i];
                    best = Mathf.Min(best, Mathf.Sqrt(dx * dx + dy * dy));
                }
                if (best > 1f) { t.SetPixel(x, y, Color.clear); continue; }
                float v = best < .5f ? 1f : best < .8f ? .8f : .6f;
                t.SetPixel(x, y, new Color(v, v, v, best < .8f ? 1f : .75f));
            }
        return Make(t, w);
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

    // An arrow pointing up the texture (EliteShot.Face turns it along its
    // flight): a barbed head over a narrow shaft; `fletch` > 0 adds tail
    // fins (the siege shell). Straight edges only: nothing round, nothing
    // with two lobes (a heart is the player's life). Hard alpha.
    static Sprite Dart(string name, int w, int h, float fletch)
    {
        var t = NewTex(w, h, name);
        float cx = w * .5f;
        const float head = .5f;   // the head's base, as a share of the length
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float px = Mathf.Abs(x + .5f - cx), v = (y + .5f) / h;   // v: 0 tail .. 1 tip
                bool inside;
                if (v >= head) inside = px <= (1f - v) / (1f - head) * cx;          // the head
                else inside = px <= cx * .28f + .01f ||                               // the shaft
                              (fletch > 0f && v < .22f && px <= cx * fletch * (1f - v / .22f * .5f));   // fins
                // barbs: the head's base cut back in the middle, so its corners hook
                if (v >= head && v < head + .1f && px < cx * .5f && px > cx * .28f) inside = false;
                if (!inside) { t.SetPixel(x, y, Color.clear); continue; }
                bool spine = px <= Mathf.Max(.6f, cx * .2f);
                float g = spine ? 1f : x + .5f < cx ? .86f : .68f;   // lit left, shaded right
                t.SetPixel(x, y, new Color(g, g, g, 1f));
            }
        return Make(t, h);
    }

    // An arrowhead pointing up the texture: a solid triangle with a
    // shallow notch in its base.
    static Sprite Chevron(string name, int w, int h)
    {
        var t = NewTex(w, h, name);
        float cx = w * .5f;
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float px = Mathf.Abs(x + .5f - cx), v = (y + .5f) / h;   // 0 base .. 1 point
                bool inside = px <= (1f - v) * cx && !(v < .3f && px < (.3f - v) / .3f * cx * .55f);
                if (!inside) { t.SetPixel(x, y, Color.clear); continue; }
                float g = px < (1f - v) * cx * .35f ? 1f : (x + .5f < cx ? .86f : .68f);
                t.SetPixel(x, y, new Color(g, g, g, 1f));
            }
        return Make(t, h);
    }

    // A spiked mine: a hot core in a dark collar, `spikes` long thin points.
    static Sprite Burr(string name, int n, int spikes)
    {
        var t = NewTex(n, n, name);
        float c = n * .5f;
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float dx = x + .5f - c, dy = y + .5f - c;
                float r = Mathf.Sqrt(dx * dx + dy * dy) / c;
                float a = Mathf.Atan2(dy, dx) * spikes * .5f;
                float spike = Mathf.Pow(Mathf.Abs(Mathf.Cos(a)), 12f);
                float reach = .42f + .58f * spike;
                if (r > reach) { t.SetPixel(x, y, Color.clear); continue; }
                float g = r < .2f ? 1f : r < .36f ? .38f : (dx + dy < 0f ? .86f : .68f);
                t.SetPixel(x, y, new Color(g, g, g, 1f));
            }
        return Make(t, n);
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
