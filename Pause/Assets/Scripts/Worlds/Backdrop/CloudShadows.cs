using UnityEngine;

// CLOUD SHADOWS: big, soft, pixel-art shadows of clouds hovering over the ground, in every planet world but Storm (Frost, Verdant,
// Ember, Tide, Desert); never in Space (the Space loop after Desert included) and never in Storm, which has its own thunderheads
// (no table row = no layer).
//
// WHAT IT IS  2-3 pooled SpriteRenderers under the backdrop set's root, all sharing ONE material (shader Pause/CloudShadow) and
//   ONE procedural mask atlas (CloudShadowArt: six cloud shapes, 384 cells across at most, built once per process, time-sliced
//   through WorldArtPreload so a world swap never hitches). A shape is a density mask: cumulus (lobes, billowy fBm rim, satellite
//   wisps), stratocumulus (broken rolls with clear lanes) or cirrus (thin streaks), dense in the core and ragged and thin at the edge.
//   The shader reads each cell's density, multiplies it by two slowly scrolling noise samples (the cloud morphs while it drifts),
//   and draws it as a stepped alpha (5 levels, ordered 4x4 dither on the rims). One mask texel is one 4-art-pixel cell (the ground
//   tiles are 896 px wide, so a cell is mainWidth / 896 * 4 world units) and the quad is snapped to that grid: pixel art, no blur.
//
// HOW BIG  A shape is up to 384 x 320 cells (about 1.45 screen widths x 0.7 screen heights). Spawns are gated by the world's
//   `budget`: a new shadow starts only while the shadows already out leave room, so roughly 30-45% of the view is shadowed at a
//   time with clear lit gaps between them (Coverage() measures it; CloudShadowTest holds it).
//
// WHERE IT SORTS  Above EVERY ground-plane layer of the world (tiles, strips, landmarks, docks, pipes: everything with role Ground /
//   Landmark, plus their +1 / +2 lamps and mist), so a cloud shadow darkens the landforms and structures too; below the world's
//   weather (Atmosphere / Cloud layers) and far below gameplay (order 0: enemies, elites, shots, pickups, the ship) and the HUD.
//
// HOW DARK  Each shadow's draw alpha is  peak x fade x suppress x setAlpha, every factor in 0..1, and the shader's stepped alpha is <= 1
//   (written to the vertex alpha, the only place the cap is applied: PeakAlpha), so no pixel is darker than `Cap` (<= HardCap, 0.5).
//   Two shadows keep apart (the younger of two overlapping ones fades), so cores never stack. Night-side variants (Spec.Night)
//   take NightCapMul of the cap so the backdrop never goes black.
//
// MOTION  Scaled time only (dt = 0 while paused: frozen). A shadow drifts at its own slow heading (wandering a few degrees) plus the
//   ground's scroll x Parallax (1.2: it floats above the ground, so it slides slowly relative to it). Born just outside the view, fades
//   in over FadeSeconds, retired when it has left the view or its life is over. Nothing allocates after the set is built.
//
// COST  2-3 quads (the shapes are bounded by their rects, so the worst case is the sum of up to three quads of up to 320 x 384 cells, up to ~117% of the
//   screen each, but only the part over the view is filled, and the spawn gate keeps about two out at a time; one shared material, one batch); a low-end phone (the KillCamRecorder memory thresholds) gets 2 smaller shapes at a lower
//   cover, a very low-memory one none (OffSystemMemoryMb). Kill switch: Enabled. The mask is built once (512 KB) and a 128x128 detail map.
//
// EDIT-MODE SUITES  measure the art tiles (luminance, lane, dock, mask): the layer is only built in play (or when a test sets
//   ForceInEditMode), so those measurements never include it.
public sealed class CloudShadowLayer
{
    public static bool Enabled = true;               // kill switch
    public static bool ForceInEditMode;              // tests / previews only
    public static int TestSeed;                      // tests: a fixed seed for the shadows' random choices (0 = random)
    public const float HardCap = 0.5f;               // no shadow is ever drawn darker than this (alpha)
    public const int MaxShadows = 3;
    public const int LowEndCount = 2;
    public const int OffSystemMemoryMb = 2500;       // below this much RAM: no shadows at all
    public const float FadeSeconds = 4f;
    public const float MaxScrollVelocity = 20f;      // a lift-off / planetfall rush never streaks the shadows
    public const int OrderAboveGround = 5;           // sorting order added to the last ground-plane layer (pieces use +1, +2)
    public const int CellArtPixels = 4;
    public const float ArtWidthPixels = 896f;
    public const float Parallax = 1.2f;              // x the ground tile's scroll rate: the clouds float above the ground
    public const float MaxCoverage = .6f;            // no more than this share of the view is ever shadowed
    public const string ShaderName = "BackdropShaders/CloudShadow";

    public enum Tier { Full, Low, Off }

    // Same thresholds as KillCamRecorder.Gap (3600 MB RAM / 1024 MB GPU), plus the Off floor.
    public static Tier TierFor(int systemMemoryMb, int graphicsMemoryMb)
    {
        if (systemMemoryMb > 0 && systemMemoryMb < OffSystemMemoryMb) return Tier.Off;
        return PerfTier.IsLowEnd(systemMemoryMb, graphicsMemoryMb) ? Tier.Low : Tier.Full;
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
        public bool big;             // only the big shapes (no cirrus): the most shadow (Frost)
        public float budget;         // spawn budget: the sum of the out shadows' full ink shares of the view (gates spawns; coverage measured in tests)
        public float speed;          // own drift, world units / s
        public float dir;
    }

    public static readonly Look[] Looks =
    {
        new Look { world = "Frost",   color = new Color(.01f, .04f, .12f), cap = .50f, nightCapMul = .6f,  count = 3, big = true, budget = 1.3f, speed = .08f, dir = 200f },
        new Look { world = "Verdant", color = new Color(.00f, .07f, .02f), cap = .48f, nightCapMul = .5f,  count = 3, budget = .95f, speed = .07f, dir = 330f },
        new Look { world = "Ember",   color = new Color(.12f, .02f, .01f), cap = .46f, nightCapMul = .6f,  count = 3, budget = .95f, speed = .09f, dir = 155f },
        new Look { world = "Tide",    color = new Color(.00f, .05f, .11f), cap = .50f, nightCapMul = .6f,  count = 3, budget = .95f, speed = .10f, dir = 25f },
        new Look { world = "Desert",  color = new Color(.11f, .045f, .01f), cap = .50f, nightCapMul = .55f, count = 3, budget = .95f, speed = .08f, dir = 20f },
    };
    public const float LowEndCoverMul = .55f;

    public static bool Has(string world) { return Find(world, out _); }

    static bool Find(string world, out Look look)
    {
        for (int i = 0; i < Looks.Length; i++) if (Looks[i].world == world) { look = Looks[i]; return true; }
        look = default;
        return false;
    }

    // The stepped alpha of a density `a` (0..1) at cell (ix, iy): the shader's maths (ordered 4x4 Bayer dither, `Steps` levels).
    public static float Quantize(float a, int ix, int iy)
    {
        float th = (Bayer4(ix & 3, iy & 3) + .5f) / 16f;
        int lvl = Mathf.Min(CloudShadowArt.Steps, Mathf.FloorToInt(a * CloudShadowArt.Steps + th));
        return Mathf.Max(0, lvl) / (float)CloudShadowArt.Steps;
    }

    public static int Bayer4(int x, int y) { return 4 * M2(x & 1, y & 1) + M2(x >> 1, y >> 1); }
    static int M2(int x, int y) { return x == y ? x : 3 - x; }

    // ---- build ----

    struct Slot
    {
        public SpriteRenderer sr;
        public Transform tr;
        public bool active;
        public int shape;
        public float x, y, heading, speed, phase, age, life, half, halfH, wait, suppress;
        public bool hasWait;
    }

    readonly Slot[] slots;
    readonly Transform root;
    readonly Look look;
    readonly float peak, cover;
    readonly float groundRate;
    readonly int count;
    readonly bool low;
    readonly Sprite[] shapes;
    readonly Material material;
    uint rng;
    float cell = .02f, halfW = 3f, halfH = 6f;
    float scrollDown, clock, crowdTimer;
    int crowdMask;                                  // slots yielding to the coverage ceiling (bit i = slot i)

    public int Count { get { return count; } }
    public float Peak { get { return peak; } }
    public float Budget { get { return cover; } }
    public int Order { get; private set; }
    public Transform Root { get { return root; } }
    public Material SharedMaterial { get { return material; } }
    public bool LowEnd { get { return low; } }
    public int ActiveCount
    {
        get { int n = 0; for (int i = 0; i < count; i++) if (slots[i].active) n++; return n; }
    }
    public SpriteRenderer Renderer(int i) { return slots[i].sr; }
    public bool IsActive(int i) { return slots[i].active; }
    public int Shape(int i) { return slots[i].shape; }
    public float Suppress(int i) { return slots[i].suppress; }
    public float Age(int i) { return slots[i].age; }
    public Vector2 Position(int i) { return new Vector2(slots[i].x, slots[i].y); }
    public float HalfExtent(int i) { return slots[i].half; }
    public float HalfExtentY(int i) { return slots[i].halfH; }
    public float CellSize { get { return cell; } }
    public float Clock { get { return clock; } }

    // The layer for `set`, or null (Space / Storm / unknown world, kill switch, off tier, edit mode, missing shader).
    public static CloudShadowLayer TryCreate(BackdropSet set, int seed = 0)
    {
        if (!Enabled || set == null) return null;
        if (!Application.isPlaying && !ForceInEditMode) return null;
        Look look;
        if (!Find(set.Spec.world, out look)) return null;
        if (DeviceTier == Tier.Off) return null;
        var shader = WorldArtPreload.Load<Shader>(ShaderName);
        if (shader == null) { Debug.LogWarning("[CloudShadows] shader " + ShaderName + " missing: no cloud shadows"); return null; }
        return new CloudShadowLayer(set, look, DeviceTier, seed, shader);
    }

    CloudShadowLayer(BackdropSet set, Look look, Tier tier, int seed, Shader shader)
    {
        this.look = look;
        low = tier == Tier.Low;
        peak = Mathf.Min(HardCap, look.cap * (set.Spec.Night(set.Variant) ? look.nightCapMul : 1f));
        count = Mathf.Min(MaxShadows, low ? Mathf.Min(LowEndCount, look.count) : look.count);
        cover = look.budget * (low ? LowEndCoverMul : 1f);
        shapes = CloudShadowArt.Shapes();
        if (seed == 0) seed = TestSeed;
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

        material = new Material(shader) { name = "CloudShadow_" + look.world };
        material.SetTexture("_DetailTex", CloudShadowArt.Detail);
        material.SetColor("_Tint", new Color(look.color.r, look.color.g, look.color.b, 1f));
        material.SetVector("_MaskSize", new Vector4(CloudShadowArt.Size, CloudShadowArt.Height, 0f, 0f));
        material.SetFloat("_Steps", CloudShadowArt.Steps);

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
            sr.sharedMaterial = material;
            sr.sortingOrder = Order;
            sr.color = new Color(Rand(), Rand(), 0f, 0f);       // rgb: this quad's noise seed; a: the draw alpha
            sr.enabled = false;
            slots[i].sr = sr;
            slots[i].tr = go.transform;
        }
        Layout(set.HalfWidth, set.HalfHeight);
        for (int i = 0; i < count; i++)
        {
            int shape = PickShape();
            if (i == 0 || Predicted() + InkShare(shape) <= cover) Spawn(i, true, shape);
            else { slots[i].hasWait = true; slots[i].wait = Range(2f, 6f); }
            if (slots[i].active && i > 0 && Coverage() > CrowdOn)
            {
                // the level opens with the view part-shadowed, never crowded: this one waits and comes in over the top later
                slots[i].active = false; slots[i].sr.enabled = false;
                slots[i].hasWait = true; slots[i].wait = Range(6f, 14f);
            }
        }
        material.SetFloat("_T", 0f);
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
            if (slots[i].active) { slots[i].half = CloudShadowArt.ShapeW(slots[i].shape) * .5f * cell; slots[i].halfH = CloudShadowArt.ShapeH(slots[i].shape) * .5f * cell; }
        }
    }

    float Rand() { rng ^= rng << 13; rng ^= rng >> 17; rng ^= rng << 5; return (rng & 0xFFFFFF) / 16777216f; }
    float Range(float a, float b) { return a + (b - a) * Rand(); }

    // Which shape next: cumulus 44%, stratocumulus 36%, cirrus 20%; the low-end tier only the smaller ones (<= 192 cells tall).
    int PickShape()
    {
        float r = Rand();
        if (look.big && !low) return r < .28f ? 0 : r < .56f ? 1 : r < .78f ? 2 : 3;
        if (low) return r < .4f ? 3 : r < .75f ? 4 : 5;
        return r < .22f ? 0 : r < .44f ? 1 : r < .62f ? 2 : r < .8f ? 3 : r < .9f ? 4 : 5;
    }

    // If `shape` would not fit the cover budget, the smallest of a few other draws instead.
    int BestFit(int shape)
    {
        if (Predicted() + InkShare(shape) <= cover * 1.15f) return shape;
        int best = shape;
        for (int k = 0; k < 3; k++)
        {
            int alt = PickShape();
            if (InkShare(alt) < InkShare(best)) best = alt;
        }
        return best;
    }

    // Would a new shadow of this shape, coming in over the top edge, keep clear of the ones out (YieldTarget would not make it yield)?
    bool TopClear(int shape)
    {
        float yn = halfH + CloudShadowArt.ShapeH(shape) * cell * .5f * .45f;
        float wn = CloudShadowArt.ShapeW(shape) * cell * .5f;
        for (int j = 0; j < count; j++)
        {
            if (!slots[j].active) continue;
            float r = (slots[j].half + wn) * .72f;
            if (Mathf.Abs(yn - slots[j].y) < r * .95f) return false;
        }
        return true;
    }

    // Is any shadow out over the view (its rect overlaps it)?
    bool AnyInView()
    {
        for (int i = 0; i < count; i++)
            if (slots[i].active && Mathf.Abs(slots[i].x) < halfW + slots[i].half * .6f && Mathf.Abs(slots[i].y) < halfH + slots[i].halfH * .6f) return true;
        return false;
    }

    // The share of the view a shape's ink covers when it is wholly in view.
    float InkShare(int shape)
    {
        return CloudShadowArt.Ink(shape) * CloudShadowArt.ShapeW(shape) * CloudShadowArt.ShapeH(shape) * cell * cell / (4f * halfW * halfH);
    }

    // What the shadows that are out would shadow if all were in view (spawn gate: leaves room, with the clear gaps between them).
    float Predicted()
    {
        float p = 0f;
        for (int i = 0; i < count; i++) if (slots[i].active) p += InkShare(slots[i].shape);
        return p;
    }

    // Put slot i into play. `initial`: somewhere in view, already part-way through its life (the level opens with shadows about).
    void Spawn(int i, bool initial, int shape)
    {
        ref Slot s = ref slots[i];
        s.shape = shape;
        s.sr.sprite = shapes[shape];
        s.sr.flipX = Rand() < .5f;
        s.sr.flipY = Rand() < .5f;
        s.half = CloudShadowArt.ShapeW(shape) * .5f * cell;
        s.halfH = CloudShadowArt.ShapeH(shape) * .5f * cell;
        s.heading = look.dir + Range(-22f, 22f);
        s.speed = look.speed * Range(.75f, 1.25f);
        s.phase = Range(0f, 6.28f);
        s.life = Range(110f, 170f);
        s.age = initial ? Range(.08f, .3f) * s.life : 0f;
        s.suppress = 0f;
        s.hasWait = false;
        var c = s.sr.color;
        c.r = Rand(); c.g = Rand();
        s.sr.color = c;

        // net heading over the ground (drift + the ground's scroll)
        float rad = s.heading * Mathf.Deg2Rad;
        float nx = Mathf.Cos(rad) * s.speed, ny = Mathf.Sin(rad) * s.speed - scrollDown;
        float nl = Mathf.Max(.01f, Mathf.Sqrt(nx * nx + ny * ny));
        nx /= nl; ny /= nl;

        float bestD = -1f, bx = 0f, by = 0f;
        bool fromTop = !initial && ny < -.2f;
        for (int tries = 0; tries < 8; tries++)
        {
            float px = Range(-halfW, halfW), py = Range(-halfH, halfH);
            if (fromTop)
            {
                // the ground scrolls down: the shadow comes in over the top edge, centred anywhere across the view
                py = halfH + s.halfH * .45f;      // about half of it already over the view: the fade-in (FadeSeconds) hides its arrival
                px *= .45f;
            }
            else if (!initial)
            {
                // enter from the edge the shadow is heading away from: back off along the net heading until clear of the view
                float back = 0f;
                float limX = halfW + s.half, limY = halfH + s.halfH;
                while (back < 80f && Mathf.Abs(px - nx * back) < limX && Mathf.Abs(py - ny * back) < limY) back += .25f;
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
            if (nearest >= 1.1f) break;
        }
        s.x = bx; s.y = by;
        s.active = true;
        s.sr.enabled = true;
        Place(ref s);
        s.suppress = YieldTarget(i);
        Apply(ref s, 1f);
    }

    void Place(ref Slot s)
    {
        s.tr.localPosition = new Vector3(Mathf.Round(s.x / cell) * cell, Mathf.Round(s.y / cell) * cell, 0f);
    }

    // The draw alpha: peak x fade-in x fade-out x suppress x the set's cross-fade, each in 0..1.
    void Apply(ref Slot s, float setAlpha)
    {
        float inF = Mathf.Clamp01(s.age / FadeSeconds), outF = Mathf.Clamp01((s.life - s.age) / FadeSeconds);
        inF = inF * inF * (3f - 2f * inF); outF = outF * outF * (3f - 2f * outF);
        float a = peak * inF * outF * Mathf.Clamp01(1f - s.suppress) * Mathf.Clamp01(setAlpha);
        var c = s.sr.color;
        c.a = Mathf.Min(a, peak);
        s.sr.color = c;
    }

    // dt is already scaled (0 while paused); velocity: the set's scroll velocity (world units / s).
    public void Step(float dt, float velocity, float setAlpha)
    {
        scrollDown = Mathf.Min(velocity, MaxScrollVelocity) * groundRate * Parallax;
        if (dt > 0f)
        {
            // the morph clock: the shaders' noise scrolls on scaled time (frozen while paused)
            clock += dt;
            material.SetFloat("_T", clock);
        }
        if (dt > 0f)
        {
            // the coverage ceiling: when more than CrowdOn of the view is shadowed, the youngest shadow yields until it is below CrowdOff
            crowdTimer += dt;
            if (crowdTimer >= .25f)
            {
                crowdTimer = 0f;
                float c = Coverage();
                if (c > CrowdOn) crowdMask |= Youngest(crowdMask);
                else if (c < CrowdOff) crowdMask = 0;
            }
        }
        for (int i = 0; i < count; i++)
        {
            ref Slot s = ref slots[i];
            if (!s.active)
            {
                if (dt > 0f)
                {
                    if (!s.hasWait) { s.wait = Range(.3f, 1.2f); s.hasWait = true; }
                    s.wait -= dt;
                    if (s.wait <= 0f)
                    {
                        int shape = BestFit(PickShape());
                        // spawn only while the shadows already out leave room (the clear lit gaps) -- but never leave the view without one
                        if (!TopClear(shape)) s.wait = Range(.3f, .7f);
                        else if (!AnyInView() || Predicted() + InkShare(shape) <= cover * 1.15f) Spawn(i, false, shape);
                        else s.wait = Range(.7f, 1.6f);
                    }
                }
                continue;
            }
            if (dt > 0f)
            {
                s.age += dt;
                float rad = (s.heading + 12f * Mathf.Sin(s.age * .12f + s.phase)) * Mathf.Deg2Rad;
                s.x += Mathf.Cos(rad) * s.speed * dt;
                s.y += Mathf.Sin(rad) * s.speed * dt - scrollDown * dt;
                bool gone = s.age >= s.life || Mathf.Abs(s.x) > halfW + s.half + .5f || s.y < -halfH - s.halfH - .5f || s.y > halfH + s.halfH + 12f;
                if (gone && s.age > 1f)
                {
                    s.active = false; s.sr.enabled = false; s.hasWait = false;
                    crowdMask &= ~(1 << i);
                    continue;
                }
                Place(ref s);
            }
            // keep apart: the younger of two overlapping shadows fades until only their soft edges touch
            float target = (crowdMask & (1 << i)) != 0 ? 1f : YieldTarget(i);
            s.suppress = Mathf.MoveTowards(s.suppress, target, dt * ((crowdMask & (1 << i)) != 0 ? .9f : .4f));
            Apply(ref s, setAlpha);
        }
    }

    public const float CrowdOn = .48f, CrowdOff = .40f;    // coverage hysteresis of the ceiling guard (MaxCoverage .6 is the hard bound)

    // the bit of the youngest active slot not yet in `mask` (0 when all are)
    int Youngest(int mask)
    {
        int y = -1;
        for (int i = 0; i < count; i++)
            if (slots[i].active && (mask & (1 << i)) == 0 && (y < 0 || slots[i].age < slots[y].age)) y = i;
        return y < 0 ? 0 : 1 << y;
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
            float r = (s.half + slots[j].half) * .72f;   // the shapes' cores sit well inside their rects
            float d = Mathf.Sqrt(dx * dx + dy * dy);
            target = Mathf.Max(target, 1f - Mathf.Clamp01((d - r * .5f) / (r * .4f)));
        }
        return target;
    }

    // ---- measurement (tests, previews, spawn tuning) ----

    public const int GridW = 24, GridH = 48;

    // The share of the view the shadows are shadowing now (the shapes' coarse ink fractions weighted by each one's draw alpha over its peak). Allocation-free.
    public float Coverage()
    {
        float sum = 0f;
        for (int gy = 0; gy < GridH; gy++)
        {
            float wy = -halfH + (gy + .5f) / GridH * 2f * halfH;
            for (int gx = 0; gx < GridW; gx++)
            {
                float wx = -halfW + (gx + .5f) / GridW * 2f * halfW;
                float best = 0f;
                for (int i = 0; i < count; i++)
                {
                    ref Slot s = ref slots[i];
                    if (!s.active) continue;
                    int w = CloudShadowArt.ShapeW(s.shape), h = CloudShadowArt.ShapeH(s.shape);
                    float lx = (wx - s.x) / cell + w * .5f, ly = (wy - s.y) / cell + h * .5f;
                    if (lx < 0f || ly < 0f || lx >= w || ly >= h) continue;
                    if (s.sr.flipX) lx = w - lx;
                    if (s.sr.flipY) ly = h - ly;
                    float ink = CloudShadowArt.CoarseInk(s.shape, lx / w, ly / h);
                    best = Mathf.Max(best, ink * Mathf.Clamp01(s.sr.color.a / peak));
                }
                sum += best;
            }
        }
        return sum / (GridW * GridH);
    }

    public void Teardown()
    {
        if (root != null) BackdropAtlas.Kill(root.gameObject);   // the shared sprites / textures stay cached
        if (material != null) BackdropAtlas.Kill(material);
    }
}
