using UnityEngine;

// CLOUD SHADOWS: soft, slow shadows of clouds passing over the ground, in every planet world but Storm (Frost, Verdant, Ember,
// Tide, Desert); never in Space (the Space loop after Desert included) and never in Storm, which has its own thunderheads (no table row = no layer).
//
// WHAT IT IS  A handful of pooled SpriteRenderers (2-4, per world) under the backdrop set's root, all sharing ONE
//   procedural texture (CloudShadowArt: three pixel-art blob shapes in one 512x512 atlas, built once, cached for the
//   process) and ONE default sprite material. Each shape is a union of dithered, 5-step stepped alpha rings on a 4-art-pixel
//   cell grid (the ground tiles are 896 px wide, so a cell is mainWidth / 896 * 4 world units); the quad's position is snapped to
//   that grid, so a shadow crawls across the ground in pixel steps like the art under it.
//
// WHERE IT SORTS  Above EVERY ground-plane layer of the world (tiles, strips, landmarks, docks, pipes: everything with
//   role Ground / Landmark, plus their +1 / +2 lamps and mist), so a cloud shadow darkens the landforms and structures too;
//   below the world's weather (Atmosphere / Cloud layers: mist, wisps, ceiling, snow, grit ...) and far below gameplay
//   (order 0: enemies, elites, shots, pickups, the ship) and the HUD. Gameplay is never darkened by it.
//
// HOW DARK  Each shadow's draw alpha is  cap x fade x suppress x setAlpha,  every factor in 0..1, and the texture's own alpha is
//   <= 1, so no pixel of one shadow is darker than `Cap` (<= HardCap, 0.5); the per-world cap is applied in ONE place
//   (PeakAlpha). Shadows keep apart (the younger of two overlapping ones fades until only their soft edges meet), so two
//   never stack their cores. Night-side variants (Spec.Night) take NightCapMul of the cap so the backdrop never goes black.
//
// MOTION  Scaled time only (dt = 0 while paused: frozen). Position += own drift (heading wanders a few degrees) and
//   the ground's scroll (the set's scroll velocity x the ground rate x Parallax). A shadow is born just outside the view,
//   fades in over FadeSeconds, lives 45-80 s, fades out, and is also retired when it has left the view; a retired slot waits a few
//   seconds before it spawns a new one. Nothing allocates after the set is built.
//
// COST  2-4 quads, one batch, no extra full-screen layer; a low-end phone (the KillCamRecorder memory thresholds)
//   gets fewer shadows (LowEndCount), a very low-memory one none (OffSystemMemoryMb). Kill switch: Enabled.
//
// EDIT-MODE SUITES  measure the art tiles (luminance, lane, dock, mask): the layer is only built in play (or when a test
//   sets ForceInEditMode), so those measurements never include it.
public sealed class CloudShadowLayer
{
    public static bool Enabled = true;               // kill switch
    public static bool ForceInEditMode;              // tests / previews only
    public const float HardCap = 0.5f;               // no shadow is ever drawn darker than this (alpha)
    public const int MaxShadows = 4;
    public const int LowEndCount = 2;
    public const int OffSystemMemoryMb = 2500;       // below this much RAM: no shadows at all
    public const float FadeSeconds = 3f;
    public const float MaxScrollVelocity = 20f;      // a lift-off / planetfall rush never streaks the shadows
    public const int OrderAboveGround = 5;           // sorting order added to the last ground-plane layer (pieces use +1, +2)
    public const int CellArtPixels = 4;
    public const float ArtWidthPixels = 896f;

    public enum Tier { Full, Low, Off }

    // Same thresholds as KillCamRecorder.Gap (3600 MB RAM / 1024 MB GPU), plus the Off floor.
    public static Tier TierFor(int systemMemoryMb, int graphicsMemoryMb)
    {
        if (systemMemoryMb > 0 && systemMemoryMb < OffSystemMemoryMb) return Tier.Off;
        bool low = (systemMemoryMb > 0 && systemMemoryMb < KillCamRecorder.LowSystemMemoryMb) ||
                   (graphicsMemoryMb > 0 && graphicsMemoryMb < KillCamRecorder.LowGraphicsMemoryMb);
        return low ? Tier.Low : Tier.Full;
    }
    public static Tier DeviceTier = TierFor(SystemInfo.systemMemorySize, SystemInfo.graphicsMemorySize);

    // One world's look. dir: degrees, 0 = right, 90 = up.
    public struct Look
    {
        public string world;
        public Color color;
        public float cap;            // peak draw alpha of the shadow core (<= HardCap)
        public float nightCapMul;    // on the planet's night side
        public int count;
        public float speed;          // own drift, world units / s
        public float dir;
    }

    public static readonly Look[] Looks =
    {
        new Look { world = "Frost",   color = new Color(.01f, .04f, .12f), cap = .50f, nightCapMul = .6f, count = 3, speed = .20f, dir = 200f },
        new Look { world = "Verdant", color = new Color(.00f, .07f, .02f), cap = .48f, nightCapMul = .5f, count = 3, speed = .17f, dir = 330f },
        new Look { world = "Ember",   color = new Color(.12f, .02f, .01f), cap = .46f, nightCapMul = .6f, count = 3, speed = .26f, dir = 155f },
        new Look { world = "Tide",    color = new Color(.00f, .05f, .11f), cap = .50f, nightCapMul = .6f, count = 4, speed = .30f, dir = 25f },
        new Look { world = "Desert",  color = new Color(.11f, .045f, .01f), cap = .50f, nightCapMul = .55f, count = 3, speed = .22f, dir = 20f },
    };
    public const float Parallax = 1.0f;             // x the ground tile's scroll rate

    public static bool Has(string world) { return Find(world, out _); }

    static bool Find(string world, out Look look)
    {
        for (int i = 0; i < Looks.Length; i++) if (Looks[i].world == world) { look = Looks[i]; return true; }
        look = default;
        return false;
    }

    // ---- build ----

    struct Slot
    {
        public SpriteRenderer sr;
        public Transform tr;
        public bool active;
        public float x, y, heading, speed, phase, age, life, half, halfH, wait, suppress;
        public bool hasWait;
    }

    readonly Slot[] slots;
    readonly Transform root;
    readonly Look look;
    readonly float peak;
    readonly float groundRate;
    readonly int count;
    readonly Sprite[] shapes;
    uint rng;
    float cell = .02f, halfW = 3f, halfH = 6f;
    float scrollDown;

    public int Count { get { return count; } }
    public float Peak { get { return peak; } }
    public int Order { get; private set; }
    public Transform Root { get { return root; } }
    public int ActiveCount
    {
        get { int n = 0; for (int i = 0; i < count; i++) if (slots[i].active) n++; return n; }
    }
    public SpriteRenderer Renderer(int i) { return slots[i].sr; }
    public bool IsActive(int i) { return slots[i].active; }
    public Vector2 Position(int i) { return new Vector2(slots[i].x, slots[i].y); }
    public float HalfExtent(int i) { return slots[i].half; }

    // The layer for `set`, or null (Space / unknown world, kill switch, off tier, edit mode).
    public static CloudShadowLayer TryCreate(BackdropSet set, int seed = 0)
    {
        if (!Enabled || set == null) return null;
        if (!Application.isPlaying && !ForceInEditMode) return null;
        Look look;
        if (!Find(set.Spec.world, out look)) return null;
        if (DeviceTier == Tier.Off) return null;
        return new CloudShadowLayer(set, look, DeviceTier, seed);
    }

    CloudShadowLayer(BackdropSet set, Look look, Tier tier, int seed)
    {
        this.look = look;
        peak = Mathf.Min(HardCap, look.cap * (set.Spec.Night(set.Variant) ? look.nightCapMul : 1f));
        count = Mathf.Min(MaxShadows, tier == Tier.Low ? Mathf.Min(LowEndCount, look.count) : look.count);
        shapes = CloudShadowArt.Shapes();
        rng = (uint)(seed * 2654435761u + 0x9E3779B9u) | 1u;
        if (seed == 0) rng ^= (uint)(System.Environment.TickCount * 747796405);
        if (rng == 0) rng = 1;

        // the last ground-plane layer: everything the shadow must fall on is at or below it
        var layers = set.Spec.layers;
        int last = 0;
        float rate = 0f;
        for (int i = 0; i < layers.Length; i++)
        {
            var r = layers[i].role;
            if (r == BackdropCatalog.Role.Ground || r == BackdropCatalog.Role.Landmark) last = i;
            if (layers[i].kind == BackdropCatalog.Kind.Tile && r == BackdropCatalog.Role.Ground) rate = Mathf.Max(rate, layers[i].rate);
        }
        groundRate = rate;
        Order = set.Spec.Order(layers[last].name) + OrderAboveGround;

        root = new GameObject("CloudShadows").transform;
        root.SetParent(set.Root, false);
        root.localPosition = new Vector3(0f, 0f, 1.5f);
        slots = new Slot[MaxShadows];
        for (int i = 0; i < count; i++)
        {
            var go = new GameObject("shadow" + i);
            go.transform.SetParent(root, false);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = shapes[0];
            sr.sortingOrder = Order;
            sr.color = new Color(look.color.r, look.color.g, look.color.b, 0f);
            sr.enabled = false;
            slots[i].sr = sr;
            slots[i].tr = go.transform;
        }
        Layout(set.HalfWidth, set.HalfHeight);
        for (int i = 0; i < count; i++) Spawn(i, true);
    }

    public void Layout(float halfWidth, float halfHeight)
    {
        halfW = halfWidth;
        halfH = halfHeight;
        cell = halfWidth * 2f * 1.02f / ArtWidthPixels * CellArtPixels;
        float scale = cell * CloudShadowArt.PixelsPerUnit;
        for (int i = 0; i < count; i++)
        {
            slots[i].tr.localScale = new Vector3(scale, scale, 1f);
            if (slots[i].active) { slots[i].half = slots[i].sr.sprite.rect.width * .5f * cell; slots[i].halfH = slots[i].sr.sprite.rect.height * .5f * cell; }
        }
    }

    float Rand() { rng ^= rng << 13; rng ^= rng >> 17; rng ^= rng << 5; return (rng & 0xFFFFFF) / 16777216f; }
    float Range(float a, float b) { return a + (b - a) * Rand(); }

    // Put slot i into play. `initial`: somewhere in view, already part-way through its life (the level opens with shadows about).
    void Spawn(int i, bool initial)
    {
        ref Slot s = ref slots[i];
        int shape = (int)(Rand() * shapes.Length) % shapes.Length;
        s.sr.sprite = shapes[shape];
        s.sr.flipX = Rand() < .5f;
        s.sr.flipY = Rand() < .5f;
        s.half = shapes[shape].rect.width * .5f * cell;
        s.halfH = shapes[shape].rect.height * .5f * cell;
        s.heading = look.dir + Range(-25f, 25f);
        s.speed = look.speed * Range(.7f, 1.3f);
        s.phase = Range(0f, 6.28f);
        s.life = Range(45f, 80f);
        s.age = initial ? Range(.15f, .5f) * s.life : 0f;
        s.suppress = 0f;
        s.hasWait = false;

        // net heading over the ground (drift + the ground's scroll)
        float rad = s.heading * Mathf.Deg2Rad;
        float nx = Mathf.Cos(rad) * s.speed, ny = Mathf.Sin(rad) * s.speed - ScrollEstimate;
        float nl = Mathf.Max(.01f, Mathf.Sqrt(nx * nx + ny * ny));
        nx /= nl; ny /= nl;

        float bestD = -1f, bx = 0f, by = 0f;
        for (int tries = 0; tries < 8; tries++)
        {
            float px, py;
            if (initial)
            {
                px = Range(-halfW, halfW); py = Range(-halfH, halfH);
            }
            else
            {
                // enter from the edge the shadow is heading away from: back off along the net heading until clear of the view
                px = Range(-halfW, halfW); py = Range(-halfH, halfH);
                float back = 0f;
                float limX = halfW + s.half, limY = halfH + s.halfH;
                while (back < 60f && Mathf.Abs(px - nx * back) < limX && Mathf.Abs(py - ny * back) < limY) back += .25f;
                px -= nx * back; py -= ny * back;
            }
            float nearest = 1e9f;
            for (int j = 0; j < count; j++)
            {
                if (j == i || !slots[j].active) continue;
                float d = Mathf.Sqrt((slots[j].x - px) * (slots[j].x - px) + (slots[j].y - py) * (slots[j].y - py)) / (slots[j].half + s.half);
                nearest = Mathf.Min(nearest, d);
            }
            if (nearest > bestD) { bestD = nearest; bx = px; by = py; }
            if (nearest >= 1f) break;
        }
        s.x = bx; s.y = by;
        s.active = true;
        s.sr.enabled = true;
        Place(ref s);
        s.suppress = YieldTarget(i);
        Apply(ref s, 1f);
    }

    float ScrollEstimate { get { return scrollDown; } }

    void Place(ref Slot s)
    {
        s.tr.localPosition = new Vector3(Mathf.Round(s.x / cell) * cell, Mathf.Round(s.y / cell) * cell, 0f);
    }

    // The draw alpha: peak x fade-in x fade-out x suppress x the set's cross-fade, each in 0..1.
    void Apply(ref Slot s, float setAlpha)
    {
        float inF = Mathf.Clamp01(s.age / FadeSeconds), outF = Mathf.Clamp01((s.life - s.age) / FadeSeconds);
        inF = inF * inF * (3f - 2f * inF); outF = outF * outF * (3f - 2f * outF);
        float a = peak * inF * outF * Mathf.Clamp01(s.suppress > 0f ? 1f - s.suppress : 1f) * Mathf.Clamp01(setAlpha);
        var c = s.sr.color;
        c.a = Mathf.Min(a, peak);
        s.sr.color = c;
    }

    // dt is already scaled (0 while paused); velocity: the set's scroll velocity (world units / s).
    public void Step(float dt, float velocity, float setAlpha)
    {
        scrollDown = Mathf.Min(velocity, MaxScrollVelocity) * groundRate * Parallax;
        for (int i = 0; i < count; i++)
        {
            ref Slot s = ref slots[i];
            if (!s.active)
            {
                if (dt > 0f)
                {
                    if (!s.hasWait) { s.wait = Range(.5f, 2.5f); s.hasWait = true; }
                    s.wait -= dt;
                    if (s.wait <= 0f) Spawn(i, false);
                }
                continue;
            }
            if (dt > 0f)
            {
                s.age += dt;
                float rad = (s.heading + 12f * Mathf.Sin(s.age * .12f + s.phase)) * Mathf.Deg2Rad;
                s.x += Mathf.Cos(rad) * s.speed * dt;
                s.y += Mathf.Sin(rad) * s.speed * dt - scrollDown * dt;
                bool gone = s.age >= s.life || Mathf.Abs(s.x) > halfW + s.half + .5f || s.y < -halfH - s.halfH - .5f || s.y > halfH + s.halfH + 6f + .5f;
                if (gone && s.age > 1f)
                {
                    s.active = false; s.sr.enabled = false; s.hasWait = false;
                    continue;
                }
                Place(ref s);
            }
            float target = YieldTarget(i);
            s.suppress = Mathf.MoveTowards(s.suppress, target, dt * .5f);
            Apply(ref s, setAlpha);
        }
    }

    // 0..1: how far slot i must yield (fade) to the older shadows it overlaps -- only the younger of two yields, so
    // two cores never stack.
    float YieldTarget(int i)
    {
        ref Slot s = ref slots[i];
        float target = 0f;
        for (int j = 0; j < count; j++)
        {
            if (j == i || !slots[j].active) continue;
            bool iYounger = s.age < slots[j].age || (s.age == slots[j].age && i > j);
            if (!iYounger) continue;
            float dx = slots[j].x - s.x, dy = slots[j].y - s.y;
            float r = s.half + slots[j].half;
            float d = Mathf.Sqrt(dx * dx + dy * dy);
            target = Mathf.Max(target, 1f - Mathf.Clamp01((d - r * .5f) / (r * .4f)));
        }
        return target;
    }

    public void Teardown()
    {
        if (root != null) BackdropAtlas.Kill(root.gameObject);   // the shared sprites / texture stay cached
    }
}

// The shadow shapes: one 512x512 RGBA atlas (1 MB) built once per process (CloudShadowArt.Shapes, also called from the world-art preload so
// the first level never builds it mid-play). White with a stepped alpha: each shape is a union of soft discs, roughened by value noise,
// quantised to five alpha steps with an ordered 4x4 dither on the steps' edges, so the rim reads as pixel art, not a blur.
public static class CloudShadowArt
{
    public const int Size = 512;
    public const int Steps = 5;
    public const float PixelsPerUnit = 100f;

    static Texture2D tex;
    public static int AlphaLevels { get; private set; }   // distinct alpha values in the atlas (<= Steps + 1)
    static Sprite[] sprites;

    // (x, y, w, h) of the shapes in the atlas.
    static readonly int[] Rects = { 0, 0, 192, 144,   192, 0, 168, 192,   0, 192, 192, 192 };

    static readonly int[] Bayer = { 0, 8, 2, 10, 12, 4, 14, 6, 3, 11, 1, 9, 15, 7, 13, 5 };

    public static Sprite[] Shapes()
    {
        if (tex != null && sprites != null && sprites[0] != null) return sprites;
        Build();
        return sprites;
    }

    public static Texture2D Texture { get { Shapes(); return tex; } }

    static float Hash(int x, int y, int seed)
    {
        uint h = (uint)(x * 374761393 + y * 668265263 + seed * 1274126177);
        h = (h ^ (h >> 13)) * 1274126177u;
        return ((h ^ (h >> 16)) & 0xFFFF) / 65535f;
    }

    static float VNoise(float x, float y, int seed)
    {
        int xi = Mathf.FloorToInt(x), yi = Mathf.FloorToInt(y);
        float fx = x - xi, fy = y - yi;
        fx = fx * fx * (3f - 2f * fx); fy = fy * fy * (3f - 2f * fy);
        float a = Hash(xi, yi, seed), b = Hash(xi + 1, yi, seed), c = Hash(xi, yi + 1, seed), d = Hash(xi + 1, yi + 1, seed);
        return Mathf.Lerp(Mathf.Lerp(a, b, fx), Mathf.Lerp(c, d, fx), fy);
    }

    // The shape's coverage 0..1 at texel (x, y) of a w x h rect: the max of soft cones, roughened, faded to 0 at the rect's rim.
    public static float Coverage(int shape, int x, int y, int w, int h, float[] cones)
    {
        float f = 0f;
        for (int k = 0; k < cones.Length; k += 3)
        {
            float dx = x - cones[k], dy = y - cones[k + 1];
            f = Mathf.Max(f, 1f - Mathf.Sqrt(dx * dx + dy * dy) / cones[k + 2]);
        }
        f += (VNoise(x / 13f, y / 13f, shape + 1) - .5f) * .3f + (VNoise(x / 5f, y / 5f, shape + 11) - .5f) * .1f;
        float edge = Mathf.Min(Mathf.Min(x, w - 1 - x), Mathf.Min(y, h - 1 - y));
        f *= Mathf.Clamp01((edge - 1f) / 8f);
        return Mathf.Clamp01(f * 3f);
    }

    static void Build()
    {
        if (tex != null) Object.DestroyImmediate(tex);
        tex = new Texture2D(Size, Size, TextureFormat.RGBA32, false, false)
        {
            name = "CloudShadowAtlas", filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.HideAndDontSave,
        };
        var px = new Color32[Size * Size];
        for (int i = 0; i < px.Length; i++) px[i] = new Color32(255, 255, 255, 0);
        sprites = new Sprite[Rects.Length / 4];
        for (int s = 0; s < sprites.Length; s++)
        {
            int rx = Rects[s * 4], ry = Rects[s * 4 + 1], w = Rects[s * 4 + 2], h = Rects[s * 4 + 3];
            var cones = new float[7 * 3];
            for (int k = 0; k < 7; k++)
            {
                float ang = Hash(k, s, 3) * 6.283f, rad = Hash(k, s, 5);
                cones[k * 3] = w * .5f + Mathf.Cos(ang) * rad * w * .2f;
                cones[k * 3 + 1] = h * .5f + Mathf.Sin(ang) * rad * h * .18f;
                cones[k * 3 + 2] = Mathf.Min(w, h) * (.22f + .08f * Hash(k, s, 7));
            }
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    float v = Coverage(s, x, y, w, h, cones) * Steps;
                    int lo = Mathf.FloorToInt(v);
                    float frac = v - lo;
                    int level = frac > (Bayer[(y & 3) * 4 + (x & 3)] + .5f) / 16f ? lo + 1 : lo;
                    byte a = (byte)Mathf.RoundToInt(Mathf.Clamp(level, 0, Steps) * 255f / Steps);
                    px[(ry + y) * Size + rx + x] = new Color32(255, 255, 255, a);
                }
            sprites[s] = Sprite.Create(tex, new Rect(rx, ry, w, h), new Vector2(.5f, .5f), PixelsPerUnit, 0, SpriteMeshType.FullRect);
            sprites[s].name = "cloud_shadow_" + s;
            sprites[s].hideFlags = HideFlags.HideAndDontSave;
        }
        uint seen = 0;
        foreach (var p in px) seen |= 1u << (p.a * Steps / 255);
        AlphaLevels = System.Convert.ToString(seen, 2).Replace("0", "").Length;
        tex.SetPixels32(px);
        tex.Apply(false, true);
    }
}
