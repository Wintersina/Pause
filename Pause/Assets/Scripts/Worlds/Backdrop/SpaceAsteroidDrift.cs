using System.Collections.Generic;
using UnityEngine;

// Space's drifting asteroid field: Codex's three cratered rocks with the
// glowing magenta cracks (asteroid_00..02) float through the backdrop on
// their own, in every direction, tumbling, their cracks blinking and smoke
// trailing off them.
//
//   presence   a steady few in flight (AsteroidDensity), entering from any
//              screen edge and recycled through a fixed pool once they have
//              left the view. Space only (only SpaceDirector builds this).
//   size       every rock its own size: a continuous AsteroidSizeMin..Max x
//              AsteroidBaseSize, weighted toward small / medium with a few
//              big ones, capped at AsteroidMaxScreenShare of the screen width.
//              The size picks the depth tier (big -> near: faster parallax,
//              brighter, sorted in front; small -> deep: hazed and dim) with
//              some overlap, so any size can turn up at any depth.
//   motion     the board's parallax scroll for its tier PLUS its own velocity
//              vector: a random heading (spread so no two in flight share
//              one) at AsteroidSpeedMin..Max u/s (small rocks skittish, big
//              ones slow, far ones slower), a gentle sideways sway, and a slow
//              tumble of AsteroidSpinMin..Max deg/s either way (small spin
//              faster). Rocks keep clear of stations, planetoids and each other
//              at their own depth (they steer round them).
//   blink      only the cracks: an additive mask of the crack pixels
//              (crack{i}_*, baked by build_asteroid_drift.py from the
//              saturated magenta pixels of each PNG) stepped through three
//              brightness levels by one of three patterns (slow pulse,
//              double blink, flicker), each rock on its own phase and pace.
//              1-3 crack hot spots also pop a small hard-edged spark.
//   smoke      pixel puffs (puff{i}x{64|32|16}_00..03: Codex's asteroid_fx
//              smoke flipbook frames for that rock, hardened to two alpha
//              steps, point-sampled from the copy nearest the puff's size)
//              shed from the crack hot spots, inheriting part of the rock's
//              velocity and pushed back against its travel, so they trail
//              behind it while they grow through their four stages and fade.
//              Bigger rocks smoke more, bigger and longer.
//
// Pixel look without mipmaps (the backdrop has none): the atlas carries
// each rock pre-shrunk to 256 / 128 / 64 px (Codex's 512 original above
// that) and a rock picks, when it spawns, the copy nearest its drawn size,
// so the art is never minified more than ~1.6x and stays crisp as it turns.
//
// Cost: AsteroidSlots pooled pieces (each: rock + crack mask + 2 spark
// renderers, made once) and AsteroidPuffCap pooled puff renderers. Per frame
// a few float ops per rock and per live puff; no allocations. Everything
// integrates the scaled dt the backdrop is handed, so a paused game freezes
// drift, tumble, blink and smoke mid-motion.
public partial class SpaceDirector
{
    // ---- presence ----
    public const int AsteroidDensity = 5;               // rocks kept in flight (target: 3-6 visible at once)
    public const int AsteroidSlots = 8;                 // pool: never more than this
    public const float AsteroidSpawnGapMin = 0.5f, AsteroidSpawnGapMax = 1.6f;   // s between arrivals
    // ---- size (drawn sprite width = AsteroidBaseSize x a multiplier) ----
    public const float AsteroidBaseSize = 0.9f;         // world units at multiplier 1
    public const float AsteroidSizeMin = 0.25f, AsteroidSizeMax = 1.6f;
    public const float AsteroidSizeSkew = 2.0f;         // > 1: most rocks small / medium, a few big
    public const float AsteroidArtFill = 0.86f;         // the rock's art spans this share of its sprite
    public const float AsteroidMaxScreenShare = 0.28f;  // the biggest rock's art: at most this share of the screen width
    // ---- motion ----
    public const float AsteroidSpeedMin = 0.05f, AsteroidSpeedMax = 0.35f;       // own speed, u/s
    public const float AsteroidSpeedSmall = 0.32f, AsteroidSpeedBig = 0.08f;    // ... at the smallest / biggest size
    public static readonly float[] AsteroidDepthSpeed = { 0.78f, 0.88f, 1f, 1f };   // per tier, far -> near
    public const float AsteroidSpinMin = 3f, AsteroidSpinMax = 15f;             // tumble, deg/s, either way
    public const float AsteroidSwayMin = 0.02f, AsteroidSwayMax = 0.06f;        // sideways sway amplitude, u
    public const float AsteroidClearance = 0.15f;      // gap kept to a station / rock at the same depth
    public const float AsteroidEnterMargin = 0.1f;      // spawn this far outside the view
    public const float AsteroidLeaveMargin = 0.6f;      // ... recycled once this far outside
    // ---- blink ----
    public const int BlinkPulse = 0, BlinkDouble = 1, BlinkFlicker = 2;
    public static readonly float[] AsteroidBlinkPeriods = { 1.4f, 2.2f, 1.1f };
    public const float CrackGain = 0.85f;               // additive strength of the brightest step
    public static readonly Color CrackTint = new Color(1f, 0.55f, 0.95f);        // magenta-pink, toward white
    // ---- sparks ----
    public const float AsteroidSparkMin = 1.6f, AsteroidSparkMax = 4.2f, AsteroidSparkLife = 0.24f;
    public const float AsteroidSparkPerSize = 0.07f, AsteroidSparkSizeMin = 0.035f, AsteroidSparkSizeMax = 0.09f;
    // ---- smoke ----
    public const int AsteroidPuffsPer = 4, AsteroidPuffCap = 24, AsteroidPuffStages = 4;
    public const float PuffEmitSmall = 1.1f, PuffEmitBig = 0.5f;     // s between puffs
    public const float PuffLifeSmall = 1.5f, PuffLifeBig = 2.8f;     // s
    public const float PuffPerSize = 0.30f, PuffSizeMin = 0.05f;     // birth width (u); 0.05 u ~ 10 px on a 2532 px phone
    public const float PuffGrow = 1.8f;                 // width at death / at birth
    public const float PuffInherit = 0.55f;             // share of the rock's velocity a puff is born with
    public const float PuffAway = 0.10f;                // u/s pushed back against the rock's travel (x size factor)
    public const float PuffDrag = 0.6f;                 // 1/s: a puff's own velocity decays
    public const float AsteroidPuffAlpha = 0.5f;        // peak opacity (x depth light): see-through
    public static readonly Color[] AsteroidPuffTints =
    {
        // x the smoke's baked 0.58..0.75 grey (build_asteroid_drift.py)
        new Color(0.81f, 0.77f, 1.00f), new Color(0.80f, 0.83f, 0.96f), new Color(0.87f, 0.71f, 1.00f),
    };
    // Sorting offsets inside the tier's layer (stations: 0, their lamps / steam 1-2):
    // the smoke over its rock (it rises out of the cracks), the crack glow over both.
    public const int RockOrder = 3, PuffOrder = 4, CrackOrder = 5, SparkOrder = 6;

    // Seeded hook: tests pin the field's own random stream (the director's
    // spawns are on their own stream and untouched either way).
    public const int AsteroidSeed = 7151;
    public static int AsteroidSeedOverride = -1;
    // Screen height in pixels used to pick a rock's pre-shrunk copy; 0: the
    // main camera's (or a 2532 px phone when there is none).
    public static float AsteroidPixelHeightOverride;

    SpaceAsteroidDrift asteroidDrift;
    public SpaceAsteroidDrift AsteroidDrift { get { return asteroidDrift; } }

    void BuildAsteroidDrift()
    {
        var pool = Pool(Tiers[0].layer, AsteroidSlots);
        string folder = BackdropCatalog.Folder("Space");
        var atlas = new BackdropAtlas(Resources.Load<Texture2D>(folder + SpaceAsteroidDrift.AtlasName),
                                      Resources.Load<TextAsset>(folder + SpaceAsteroidDrift.AtlasName));
        var fxTexture = Resources.Load<Texture2D>(folder + SpaceAsteroidDrift.FxAtlasName);
        // smoke stages and sparks are tiny pixel sprites drawn magnified: point
        // sampling keeps their pixels hard (the rocks stay bilinear: they turn)
        if (fxTexture != null) fxTexture.filterMode = FilterMode.Point;
        var fxAtlas = new BackdropAtlas(fxTexture, Resources.Load<TextAsset>(folder + SpaceAsteroidDrift.FxAtlasName));
        asteroidDrift = new SpaceAsteroidDrift(this, pool, atlas, fxAtlas, asteroids);
    }

    void TickAsteroidDrift(float dt, float v)
    {
        if (asteroidDrift != null) asteroidDrift.Tick(dt, v);
    }

    void TeardownAsteroidDrift()
    {
        if (asteroidDrift != null) asteroidDrift.Destroy();
        asteroidDrift = null;
    }

    // What the field needs from its director (all private there).
    internal float DriftHalfW { get { return HalfW; } }
    internal float DriftHalfH { get { return HalfH; } }
    internal float DriftAlpha { get { return set.Alpha; } }
    internal BackdropCatalog.Spec DriftSpec { get { return set.Spec; } }
    internal Color DriftLit(int tier) { return Lit(RockTint, Tiers[tier]); }
    // Lone structures (stations, planetoids) the rocks keep clear of.
    internal BackdropPool DriftStations { get { return stations; } }
    internal BackdropPool DriftPlanetoids { get { return planetoids; } }
    internal float DriftReach(BackdropPiece p) { return Reach(p); }

    // A rock's crack brightness step (0, Mid or 1) under `pattern` at phase
    // f (0..1 through its period). CPU only; the tests read it too.
    public const float CrackMid = 0.45f;
    public static float CrackLevel(int pattern, float f, float seed)
    {
        f -= Mathf.Floor(f);
        switch (pattern)
        {
            case BlinkPulse:        // slow pulse: dark, rising, bright, falling
                return f < 0.34f ? 0f : f < 0.5f ? CrackMid : f < 0.72f ? 1f : f < 0.86f ? CrackMid : 0f;
            case BlinkDouble:       // two quick flashes, then a dim tail
                return f < 0.07f ? 1f : f < 0.14f ? 0f : f < 0.21f ? 1f : f < 0.34f ? CrackMid : 0f;
            default:                // flicker: a new step every twelfth of the period
            {
                float slot = Mathf.Floor(f * 12f);
                float h = Mathf.Sin(slot * 12.9898f + seed * 78.233f) * 43758.5453f;
                h -= Mathf.Floor(h);
                return h < 0.42f ? 0f : h < 0.74f ? CrackMid : 1f;
            }
        }
    }
}

public class SpaceAsteroidDrift
{
    public const string AtlasName = "asteroid_drift", FxAtlasName = "asteroid_drift_fx";
    public const string AdditiveShader = "BackdropShaders/BackdropAdditive";
    public const int Levels = 4;                // atlas copies: 256, 128, 64, 32 px
    public static readonly int[] LevelPx = { 256, 128, 64, 32 };
    public const float MaxMinify = 1.6f;        // a copy is never drawn smaller than 1 / this of its pixels

    // Crack hot spots per asteroid (fractions of the sprite width from its
    // centre, x right, y up): the brightest crack clusters, measured by
    // build_asteroid_drift.py (AsteroidDriftTest checks they sit on crack
    // pixels of the mask).
    public static readonly Vector2[][] HotSpots =
    {
        new[] { new Vector2(0.032f, 0.028f), new Vector2(-0.155f, 0.087f), new Vector2(0.280f, -0.200f) },
        new[] { new Vector2(-0.075f, -0.021f), new Vector2(-0.249f, 0.218f), new Vector2(0.171f, -0.165f) },
        new[] { new Vector2(0.036f, 0.050f), new Vector2(-0.103f, 0.282f), new Vector2(-0.122f, -0.126f) },
    };

    public class Rock
    {
        public BackdropPiece piece;
        public SpriteRenderer crack;
        public SpriteRenderer[] sparks = new SpriteRenderer[2];
        public bool active;
        public int art, level;                  // level -1: Codex's 512 original
        public float sizeMul, sizeT;            // multiplier on AsteroidBaseSize; 0 smallest .. 1 biggest
        public float heading, speed, spin, rot; // heading deg (0 = +x, ccw), speed u/s, spin deg/s, rotation deg
        public float baseX, baseY;              // centre before sway
        public float swayAmp, swayRate, swayPhase;
        public int pattern;
        public float blinkPeriod, blinkPhase, blinkSeed, crackLevel;
        public int spots;                       // hot spots in use (1-3)
        public float emitIn, emitEvery, puffLife;
        public int livePuffs;
        public float sparkIn, sparkAge = -1f, sparkPace;
        public int sparkSpot;
        public float light;
        public float ppu;                       // screen pixels per world unit when it spawned
        public int order;                       // the rock's sorting order
        public float vx { get { return piece.vx; } }
        public float vy { get { return piece.vy; } }
    }

    public readonly List<Rock> Rocks = new List<Rock>();
    // Puffs: one shared pool; owner < 0 = free.
    public readonly SpriteRenderer[] Puffs = new SpriteRenderer[SpaceDirector.AsteroidPuffCap];
    public readonly int[] PuffOwner = new int[SpaceDirector.AsteroidPuffCap];
    public readonly float[] PuffAge = new float[SpaceDirector.AsteroidPuffCap];
    readonly float[] puffLife = new float[SpaceDirector.AsteroidPuffCap];
    readonly float[] puffX = new float[SpaceDirector.AsteroidPuffCap], puffY = new float[SpaceDirector.AsteroidPuffCap];
    readonly float[] puffVx = new float[SpaceDirector.AsteroidPuffCap], puffVy = new float[SpaceDirector.AsteroidPuffCap];
    readonly float[] puffW = new float[SpaceDirector.AsteroidPuffCap], puffRate = new float[SpaceDirector.AsteroidPuffCap];
    readonly int[] puffArt = new int[SpaceDirector.AsteroidPuffCap];
    readonly Color[] puffTint = new Color[SpaceDirector.AsteroidPuffCap];

    public int Spawned, PuffsEmitted, SparksPopped, SpawnRejects;
    public BackdropAtlas Atlas { get { return atlas; } }
    public BackdropAtlas FxAtlas { get { return fxAtlas; } }
    public Material CrackMaterial { get { return additive; } }
    public Sprite[][][] PuffStages { get { return puffStages; } }
    public static readonly int[] PuffPx = { 64, 32, 16 };

    readonly SpaceDirector director;
    readonly BackdropPool pool;
    readonly BackdropAtlas atlas, fxAtlas;
    readonly Sprite[] originals;               // Codex's asteroid_00..02
    readonly Sprite[][] rockLevels = new Sprite[3][], crackLevels = new Sprite[3][];
    readonly Sprite[][][] puffStages = new Sprite[3][][];   // [art][size copy][stage]
    readonly int[] puffLevel = new int[SpaceDirector.AsteroidPuffCap];
    readonly Sprite[] sparkSprites = new Sprite[2];
    readonly System.Random rng;
    readonly float[] headingScratch = new float[SpaceDirector.AsteroidSlots + 1];
    Material additive;
    float spawnIn, lastV = WorldBackdrop.ScrollVelocity(0f);
    bool ready;

    public SpaceAsteroidDrift(SpaceDirector director, BackdropPool pool, BackdropAtlas atlas, BackdropAtlas fxAtlas,
                              Sprite[] originals)
    {
        this.director = director;
        this.pool = pool;
        this.atlas = atlas;
        this.fxAtlas = fxAtlas;
        this.originals = originals;
        rng = new System.Random(SpaceDirector.AsteroidSeedOverride >= 0 ? SpaceDirector.AsteroidSeedOverride
                                                                          : SpaceDirector.AsteroidSeed);
        ready = atlas != null && atlas.Count > 0 && fxAtlas != null && fxAtlas.Count > 0;
        for (int i = 0; i < 3 && ready; i++)
        {
            rockLevels[i] = atlas.Frames("rock" + i);
            crackLevels[i] = atlas.Frames("crack" + i);
            ready &= rockLevels[i].Length == Levels && crackLevels[i].Length == Levels;
            puffStages[i] = new Sprite[PuffPx.Length][];
            for (int k = 0; k < PuffPx.Length; k++)
            {
                puffStages[i][k] = fxAtlas.Frames("puff" + i + "x" + PuffPx[k]);
                ready &= puffStages[i][k].Length == SpaceDirector.AsteroidPuffStages;
            }
        }
        if (ready)
        {
            sparkSprites[0] = fxAtlas.Get("spark_00");
            sparkSprites[1] = fxAtlas.Get("spark_01");
            ready = sparkSprites[0] != null && sparkSprites[1] != null;
        }
        if (!ready) return;

        var shader = Resources.Load<Shader>(AdditiveShader);
        if (shader != null) additive = new Material(shader) { name = "SpaceAsteroidCracks" };

        foreach (var p in pool.items)
        {
            var r = new Rock { piece = p };
            r.crack = Child(p.body, "cracks");
            for (int k = 0; k < r.sparks.Length; k++) r.sparks[k] = Child(p.body, "spark");
            Rocks.Add(r);
        }
        var holder = new GameObject("asteroid smoke").transform;
        holder.SetParent(pool.items[0].root.parent, false);
        for (int i = 0; i < Puffs.Length; i++)
        {
            var go = new GameObject("puff");
            go.transform.SetParent(holder, false);
            Puffs[i] = go.AddComponent<SpriteRenderer>();
            Puffs[i].enabled = false;
            PuffOwner[i] = -1;
        }

        // The field is already there when the world opens (not in a
        // lift-off's calm interlude: SpaceDirector.Quiet).
        if (!SpaceDirector.Quiet) for (int i = 0; i < SpaceDirector.AsteroidDensity; i++) Spawn(false);
        spawnIn = Rand(SpaceDirector.AsteroidSpawnGapMin, SpaceDirector.AsteroidSpawnGapMax);
    }

    SpriteRenderer Child(Transform parent, string name)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var sr = go.AddComponent<SpriteRenderer>();
        if (additive != null) sr.sharedMaterial = additive;
        sr.enabled = false;
        return sr;
    }

    public bool Ready { get { return ready; } }

    float Rand(float a, float b) { return a + (float)rng.NextDouble() * (b - a); }
    float HalfW { get { return director.DriftHalfW; } }
    float HalfH { get { return director.DriftHalfH; } }

    public int ActiveCount
    {
        get { int n = 0; foreach (var r in Rocks) if (r.active) n++; return n; }
    }

    // Rocks whose art overlaps the view right now.
    public int VisibleCount
    {
        get
        {
            int n = 0;
            foreach (var r in Rocks)
                if (r.active && Mathf.Abs(r.piece.x) < HalfW + Reach(r) && Mathf.Abs(r.piece.y) < HalfH + Reach(r)) n++;
            return n;
        }
    }

    public int LivePuffs
    {
        get { int n = 0; for (int i = 0; i < PuffOwner.Length; i++) if (PuffOwner[i] >= 0) n++; return n; }
    }

    public static float Reach(Rock r) { return r.piece.size * SpaceDirector.AsteroidArtFill * 0.5f; }

    // ------------------------------------------------------------- spawn --

    // The heading in the middle of the widest gap between the headings in
    // flight (with a little jitter): no two rocks ever head the same way.
    float FreshHeading()
    {
        int n = 0;
        foreach (var r in Rocks) if (r.active && n < headingScratch.Length) headingScratch[n++] = Mathf.Repeat(r.heading, 360f);
        if (n == 0) return Rand(0f, 360f);
        for (int i = 1; i < n; i++)            // insertion sort, no allocation
        {
            float h = headingScratch[i];
            int j = i - 1;
            for (; j >= 0 && headingScratch[j] > h; j--) headingScratch[j + 1] = headingScratch[j];
            headingScratch[j + 1] = h;
        }
        float bestGap = -1f, bestStart = 0f;
        for (int i = 0; i < n; i++)
        {
            float a = headingScratch[i], b = i + 1 < n ? headingScratch[i + 1] : headingScratch[0] + 360f;
            if (b - a > bestGap) { bestGap = b - a; bestStart = a; }
        }
        return Mathf.Repeat(bestStart + bestGap * Rand(0.35f, 0.65f), 360f);
    }

    float PixelsPerUnit()
    {
        float h = SpaceDirector.AsteroidPixelHeightOverride;
        if (h <= 0f)
        {
            var cam = Camera.main;
            h = cam != null ? cam.pixelHeight : 2532f;
        }
        return h / Mathf.Max(0.01f, 2f * HalfH);
    }

    // The biggest copy that is minified no more than MaxMinify (copies double,
    // so it is magnified at most 2 / MaxMinify = 1.25x): Codex's 512 original
    // once that fits, else the 256 / 128 / 64 / 32 px copies.
    public static int LevelFor(float drawnPx)
    {
        if (512f <= drawnPx * MaxMinify) return -1;
        for (int k = 0; k < Levels; k++)
            if (LevelPx[k] <= drawnPx * MaxMinify) return k;
        return Levels - 1;
    }

    bool Spawn(bool entering)
    {
        Rock r = null;
        int slot = -1;
        for (int i = 0; i < Rocks.Count; i++) if (!Rocks[i].active) { r = Rocks[i]; slot = i; break; }
        if (r == null) return false;

        // Size first (continuous, skewed small), then the depth it implies.
        float u = (float)rng.NextDouble();
        float m = Mathf.Lerp(SpaceDirector.AsteroidSizeMin, SpaceDirector.AsteroidSizeMax,
                             Mathf.Pow(u, SpaceDirector.AsteroidSizeSkew));
        float cap = SpaceDirector.AsteroidMaxScreenShare * 2f * HalfW / SpaceDirector.AsteroidArtFill /
                    SpaceDirector.AsteroidBaseSize;
        m = Mathf.Min(m, cap);
        float sizeT = Mathf.InverseLerp(SpaceDirector.AsteroidSizeMin, SpaceDirector.AsteroidSizeMax, m);
        int tier = Mathf.Clamp(Mathf.FloorToInt(sizeT * 4f + Rand(-0.75f, 0.75f)), 0, SpaceDirector.Tiers.Length - 1);
        float size = SpaceDirector.AsteroidBaseSize * m;
        float reach = size * SpaceDirector.AsteroidArtFill * 0.5f;
        string layer = SpaceDirector.LayerOf(tier, false);
        float rate = director.DriftSpec.Rate(layer);

        float heading = FreshHeading();
        float speed = Mathf.Clamp(Mathf.Lerp(SpaceDirector.AsteroidSpeedSmall, SpaceDirector.AsteroidSpeedBig, sizeT) *
                                  Rand(0.85f, 1.15f) * SpaceDirector.AsteroidDepthSpeed[tier],
                                  SpaceDirector.AsteroidSpeedMin, SpaceDirector.AsteroidSpeedMax);
        float vx = Mathf.Cos(heading * Mathf.Deg2Rad) * speed, vy = Mathf.Sin(heading * Mathf.Deg2Rad) * speed;

        // Where: entering across the edge it is drifting in from (on screen,
        // with the scroll, most come in at the top), or (the opening field)
        // anywhere in view. Clear of the stations and rocks at its depth.
        float x = 0f, y = 0f;
        bool placed = false;
        for (int attempt = 0; attempt < 4 && !placed; attempt++)
        {
            if (entering)
            {
                float nx = vx, ny = vy - rate * lastV;
                float wTop = Mathf.Max(0f, -ny), wBottom = Mathf.Max(0f, ny);
                float wLeft = Mathf.Max(0f, nx), wRight = Mathf.Max(0f, -nx);
                float pick = Rand(0f, wTop + wBottom + wLeft + wRight);
                float outX = HalfW + reach + SpaceDirector.AsteroidEnterMargin;
                float outY = HalfH + reach + SpaceDirector.AsteroidEnterMargin;
                if ((pick -= wTop) < 0f) { x = Rand(-HalfW, HalfW); y = outY; }
                else if ((pick -= wBottom) < 0f) { x = Rand(-HalfW, HalfW); y = -outY; }
                else if ((pick -= wLeft) < 0f) { x = -outX; y = Rand(-HalfH * 0.2f, HalfH); }
                else { x = outX; y = Rand(-HalfH * 0.2f, HalfH); }
            }
            else
            {
                x = Rand(-HalfW + reach * 0.5f, HalfW - reach * 0.5f);
                y = Rand(-HalfH * 0.8f, HalfH * 0.9f);
            }
            placed = Clear(x, y, reach, tier, slot);
        }
        if (!placed) { SpawnRejects++; return false; }

        var p = pool.items[slot];
        p.Show(true);
        p.age = 0f;
        r.active = true;
        r.art = rng.Next(3);
        r.sizeMul = m;
        r.sizeT = sizeT;
        r.ppu = PixelsPerUnit();
        r.level = LevelFor(size * r.ppu);
        if (r.level < 0 && (originals == null || originals[r.art] == null)) r.level = 0;
        Sprite rockSprite = r.level < 0 ? originals[r.art] : rockLevels[r.art][r.level];
        Sprite crackSprite = crackLevels[r.art][Mathf.Max(0, r.level)];
        p.sr.sharedMaterial = pool.items[0].sr.sharedMaterial;
        p.sr.sprite = rockSprite;
        float w = rockSprite.bounds.size.x;
        p.size = size;
        float k = size / Mathf.Max(1e-4f, w);
        p.root.localScale = new Vector3(k, k, 1f);
        p.kind = SpaceDirector.Planetoid;
        p.tier = tier;
        p.rate = rate;
        p.vx = vx;
        p.vy = vy;
        p.x = r.baseX = x;
        p.y = r.baseY = y;
        r.heading = heading;
        r.speed = speed;
        r.spin = Mathf.Clamp(Mathf.Lerp(SpaceDirector.AsteroidSpinMax, SpaceDirector.AsteroidSpinMin, sizeT) *
                             Rand(0.85f, 1.15f), SpaceDirector.AsteroidSpinMin, SpaceDirector.AsteroidSpinMax) *
                 (rng.Next(2) == 0 ? -1f : 1f);
        r.rot = Rand(0f, 360f);
        r.swayAmp = Mathf.Lerp(SpaceDirector.AsteroidSwayMax, SpaceDirector.AsteroidSwayMin, sizeT);
        r.swayRate = Rand(0.8f, 1.5f);                 // rad/s
        r.swayPhase = Rand(0f, 6.283f);
        p.color = director.DriftLit(tier);
        r.light = SpaceDirector.Tiers[tier].light;

        int baseOrder = director.DriftSpec.Order(layer);
        r.order = baseOrder + SpaceDirector.RockOrder;
        p.sr.sortingOrder = r.order;
        r.crack.sprite = crackSprite;
        float ck = w / Mathf.Max(1e-4f, crackSprite.bounds.size.x);
        r.crack.transform.localScale = new Vector3(ck, ck, 1f);
        r.crack.sortingOrder = baseOrder + SpaceDirector.CrackOrder;
        r.crack.enabled = true;
        foreach (var s in r.sparks) { s.enabled = false; s.sortingOrder = baseOrder + SpaceDirector.SparkOrder; }

        r.pattern = rng.Next(3);
        r.blinkPeriod = SpaceDirector.AsteroidBlinkPeriods[r.pattern] * Mathf.Lerp(0.85f, 1.3f, sizeT) * Rand(0.9f, 1.1f);
        r.blinkPhase = Rand(0f, 1f);
        r.blinkSeed = Rand(0f, 1f);
        r.spots = 1 + rng.Next(3);
        r.emitEvery = Mathf.Lerp(SpaceDirector.PuffEmitSmall, SpaceDirector.PuffEmitBig, sizeT);
        r.puffLife = Mathf.Lerp(SpaceDirector.PuffLifeSmall, SpaceDirector.PuffLifeBig, sizeT);
        r.emitIn = Rand(0f, r.emitEvery);
        r.livePuffs = 0;
        r.sparkPace = Mathf.Lerp(0.8f, 1.3f, sizeT);
        r.sparkIn = Rand(0.3f, SpaceDirector.AsteroidSparkMax) * r.sparkPace;
        r.sparkAge = -1f;
        Spawned++;
        Pose(r, 0f);
        Paint(r);
        return true;
    }

    // Would a rock at (x, y) sit clear of every station / planetoid and rock?
    // (At spawn every body counts; in flight only its own depth, see Steer.)
    bool Clear(float x, float y, float reach, int tier, int self)
    {
        if (!ClearOf(director.DriftStations, x, y, reach)) return false;
        if (!ClearOf(director.DriftPlanetoids, x, y, reach)) return false;
        for (int i = 0; i < Rocks.Count; i++)
        {
            var o = Rocks[i];
            if (i == self || !o.active) continue;
            float dx = x - o.piece.x, dy = y - o.piece.y, min = reach + Reach(o) + SpaceDirector.AsteroidClearance;
            if (dx * dx + dy * dy < min * min) return false;
        }
        return true;
    }

    bool ClearOf(BackdropPool bodies, float x, float y, float reach)
    {
        if (bodies == null) return true;
        foreach (var b in bodies.items)
        {
            if (!b.active || b.parent != null) continue;
            float dx = x - b.x, dy = y - b.y, min = reach + director.DriftReach(b) + SpaceDirector.AsteroidClearance;
            if (dx * dx + dy * dy < min * min) return false;
        }
        return true;
    }

    // ------------------------------------------------------------- frame --

    public void Tick(float dt, float v)
    {
        if (!ready) return;
        lastV = v;
        for (int i = 0; i < Rocks.Count; i++)
        {
            var r = Rocks[i];
            if (!r.active) continue;
            var p = r.piece;
            if (dt > 0f) Steer(r, i, dt);
            p.age += dt;
            r.baseX += p.vx * dt;
            r.baseY += (p.vy - p.rate * v) * dt;
            r.rot += r.spin * dt;
            Pose(r, dt);
            float out_ = Reach(r) + SpaceDirector.AsteroidLeaveMargin;
            if (Mathf.Abs(p.x) > HalfW + out_ || Mathf.Abs(p.y) > HalfH + out_) { Retire(r, i); continue; }
            Emit(r, i, dt);
            Sparks(r, dt);
            Paint(r);
        }
        TickPuffs(dt, v);

        if (dt > 0f && !SpaceDirector.Quiet)
        {
            spawnIn -= dt;
            if (spawnIn <= 0f)
            {
                bool ok = ActiveCount >= SpaceDirector.AsteroidDensity || Spawn(true);
                spawnIn = ok ? Rand(SpaceDirector.AsteroidSpawnGapMin, SpaceDirector.AsteroidSpawnGapMax) : 0.4f;
            }
        }
    }

    // Keep clear of the stations / planetoids / rocks at the same depth:
    // turn the heading away from anything inside the clearance zone (the
    // speed is kept). Different depths may pass in front / behind.
    void Steer(Rock r, int self, float dt)
    {
        var p = r.piece;
        float reach = Reach(r);
        float ax = 0f, ay = 0f;
        Repel(director.DriftStations, r, reach, ref ax, ref ay);
        Repel(director.DriftPlanetoids, r, reach, ref ax, ref ay);
        for (int i = 0; i < Rocks.Count; i++)
        {
            var o = Rocks[i];
            if (i == self || !o.active || o.piece.tier != p.tier) continue;
            Push(r.baseX - o.baseX, r.baseY - o.baseY, reach + Reach(o), ref ax, ref ay);
        }
        if (ax == 0f && ay == 0f) return;
        // remove the approaching part of the velocity, nudge outward, keep speed
        float n = Mathf.Sqrt(ax * ax + ay * ay);
        float nx = ax / n, ny = ay / n;
        float vx = p.vx, vy = p.vy;
        float into = vx * nx + vy * ny;
        if (into < 0f) { vx -= into * nx; vy -= into * ny; }
        vx += nx * r.speed * 0.5f;
        vy += ny * r.speed * 0.5f;
        float s = Mathf.Sqrt(vx * vx + vy * vy);
        if (s > 1e-5f) { vx *= r.speed / s; vy *= r.speed / s; }
        p.vx = vx;
        p.vy = vy;
        r.heading = Mathf.Atan2(vy, vx) * Mathf.Rad2Deg;
        // already overlapping (a station arrived on top of it): ease out
        r.baseX += nx * Mathf.Min(1f, n) * 0.3f * dt;
        r.baseY += ny * Mathf.Min(1f, n) * 0.3f * dt;
    }

    void Repel(BackdropPool bodies, Rock r, float reach, ref float ax, ref float ay)
    {
        if (bodies == null) return;
        foreach (var b in bodies.items)
        {
            if (!b.active || b.parent != null || b.tier != r.piece.tier) continue;
            Push(r.baseX - b.x, r.baseY - b.y, reach + director.DriftReach(b), ref ax, ref ay);
        }
    }

    // Accumulates a push away from something `contact` away (centre to centre)
    // once inside contact + clearance + a look-ahead zone, stronger the closer.
    static void Push(float dx, float dy, float contact, ref float ax, ref float ay)
    {
        float zone = contact + SpaceDirector.AsteroidClearance + 0.45f;
        float d2 = dx * dx + dy * dy;
        if (d2 >= zone * zone) return;
        float d = Mathf.Sqrt(d2);
        if (d < 1e-4f) { dx = 1f; dy = 0f; d = 1f; }
        float k = (zone - d) / zone;
        ax += dx / d * k;
        ay += dy / d * k;
    }

    void Pose(Rock r, float dt)
    {
        var p = r.piece;
        float h = r.heading * Mathf.Deg2Rad;
        float sway = r.swayAmp * Mathf.Sin(p.age * r.swayRate + r.swayPhase);
        p.x = r.baseX - Mathf.Sin(h) * sway;
        p.y = r.baseY + Mathf.Cos(h) * sway;
        p.root.localPosition = new Vector3(p.x, p.y, 0f);
        p.root.localRotation = Quaternion.Euler(0f, 0f, r.rot);
    }

    void Paint(Rock r)
    {
        var p = r.piece;
        float alpha = director.DriftAlpha;
        Color c = p.color;
        c.a *= alpha;
        p.sr.color = c;
        // cracks: one of three brightness steps, additive, on the crack pixels only
        r.crackLevel = SpaceDirector.CrackLevel(r.pattern, p.age / r.blinkPeriod + r.blinkPhase, r.blinkSeed);
        Color k = SpaceDirector.CrackTint;
        k.a = r.crackLevel * SpaceDirector.CrackGain * r.light * alpha;
        r.crack.color = k;
    }

    void Retire(Rock r, int index)
    {
        r.active = false;
        r.crack.enabled = false;
        foreach (var s in r.sparks) s.enabled = false;
        r.sparkAge = -1f;
        for (int i = 0; i < PuffOwner.Length; i++)
            if (PuffOwner[i] == index) { PuffOwner[i] = -1; Puffs[i].enabled = false; }
        r.livePuffs = 0;
        r.piece.Show(false);
    }

    // A crack hot spot in world (pool) space.
    void SpotAt(Rock r, int spot, out float wx, out float wy)
    {
        var p = r.piece;
        Vector2 f = HotSpots[r.art][spot];
        float a = r.rot * Mathf.Deg2Rad, ca = Mathf.Cos(a), sa = Mathf.Sin(a);
        wx = p.x + (f.x * ca - f.y * sa) * p.size;
        wy = p.y + (f.x * sa + f.y * ca) * p.size;
    }

    void Emit(Rock r, int index, float dt)
    {
        if (dt <= 0f) return;
        r.emitIn -= dt;
        if (r.emitIn > 0f) return;
        r.emitIn = r.emitEvery * Rand(0.8f, 1.2f);
        if (r.livePuffs >= SpaceDirector.AsteroidPuffsPer) return;
        int slot = -1;
        for (int i = 0; i < PuffOwner.Length; i++) if (PuffOwner[i] < 0) { slot = i; break; }
        if (slot < 0) return;                                           // the field-wide cap
        var p = r.piece;
        float wx, wy;
        SpotAt(r, rng.Next(r.spots), out wx, out wy);
        float away = SpaceDirector.PuffAway * (0.6f + 0.8f * r.sizeT);
        float s = Mathf.Max(1e-4f, r.speed);
        float dx = p.vx / s, dy = p.vy / s;
        float jitter = Rand(-0.03f, 0.03f);
        PuffOwner[slot] = index;
        PuffAge[slot] = 0f;
        puffLife[slot] = r.puffLife * Rand(0.85f, 1.15f);
        puffX[slot] = wx;
        puffY[slot] = wy;
        puffVx[slot] = p.vx * SpaceDirector.PuffInherit - dx * away - dy * jitter;
        puffVy[slot] = p.vy * SpaceDirector.PuffInherit - dy * away + dx * jitter;
        puffW[slot] = Mathf.Max(SpaceDirector.PuffSizeMin, p.size * SpaceDirector.PuffPerSize);
        puffRate[slot] = p.rate;
        puffArt[slot] = r.art;
        // the smoke copy nearest 1:1 at birth (it grows to PuffGrow x that)
        float birthPx = puffW[slot] * r.ppu;
        int lv = PuffPx.Length - 1;
        for (int k = 0; k < PuffPx.Length; k++) if (PuffPx[k] <= birthPx * 1.25f) { lv = k; break; }
        puffLevel[slot] = lv;
        Color t = SpaceDirector.AsteroidPuffTints[rng.Next(SpaceDirector.AsteroidPuffTints.Length)];
        t.r *= r.light; t.g *= r.light; t.b *= r.light;
        puffTint[slot] = t;
        var sr = Puffs[slot];
        sr.sortingOrder = r.order - SpaceDirector.RockOrder + SpaceDirector.PuffOrder;
        sr.flipX = rng.Next(2) == 0;
        sr.enabled = true;
        r.livePuffs++;
        PuffsEmitted++;
    }

    void TickPuffs(float dt, float v)
    {
        float alpha = director.DriftAlpha;
        float drag = Mathf.Max(0f, 1f - SpaceDirector.PuffDrag * dt);
        for (int i = 0; i < Puffs.Length; i++)
        {
            int owner = PuffOwner[i];
            if (owner < 0) continue;
            PuffAge[i] += dt;
            float f = PuffAge[i] / puffLife[i];
            var sr = Puffs[i];
            if (f >= 1f)
            {
                PuffOwner[i] = -1;
                sr.enabled = false;
                Rocks[owner].livePuffs--;
                continue;
            }
            puffVx[i] *= drag;
            puffVy[i] *= drag;
            puffX[i] += puffVx[i] * dt;
            puffY[i] += (puffVy[i] - puffRate[i] * v) * dt;
            int stage = Mathf.Min(SpaceDirector.AsteroidPuffStages - 1, (int)(f * SpaceDirector.AsteroidPuffStages));
            var sprite = puffStages[puffArt[i]][puffLevel[i]][stage];
            sr.sprite = sprite;
            float w = puffW[i] * Mathf.Lerp(1f, SpaceDirector.PuffGrow, f);
            float k = w / Mathf.Max(1e-4f, sprite.bounds.size.x);
            var tr = sr.transform;
            tr.localPosition = new Vector3(puffX[i], puffY[i], 0f);
            tr.localScale = new Vector3(k, k, 1f);
            // stepped fade: in, hold, thin, out
            float a = f < 0.12f ? 0.5f : f < 0.55f ? 1f : f < 0.8f ? 0.6f : 0.3f;
            Color c = puffTint[i];
            c.a = SpaceDirector.AsteroidPuffAlpha * a * alpha;
            sr.color = c;
        }
    }

    void Sparks(Rock r, float dt)
    {
        var s = r.sparks[0];
        var core = r.sparks[1];
        if (r.sparkAge < 0f)
        {
            if (dt <= 0f) return;
            r.sparkIn -= dt;
            if (r.sparkIn > 0f) return;
            r.sparkAge = 0f;
            r.sparkIn = Rand(SpaceDirector.AsteroidSparkMin, SpaceDirector.AsteroidSparkMax) * r.sparkPace;
            r.sparkSpot = rng.Next(r.spots);
            SparksPopped++;
        }
        else r.sparkAge += dt;
        if (r.sparkAge >= SpaceDirector.AsteroidSparkLife)
        {
            r.sparkAge = -1f;
            s.enabled = core.enabled = false;
            return;
        }
        // body space: the hot spot as a fraction of the sprite width
        var p = r.piece;
        float sw = p.sr.sprite.bounds.size.x;
        Vector2 f = HotSpots[r.art][r.sparkSpot];
        float world = Mathf.Clamp(p.size * SpaceDirector.AsteroidSparkPerSize, SpaceDirector.AsteroidSparkSizeMin,
                                  SpaceDirector.AsteroidSparkSizeMax);
        float rootK = Mathf.Max(1e-4f, p.root.localScale.x);
        bool pop = r.sparkAge < SpaceDirector.AsteroidSparkLife * 0.45f;
        var on = pop ? s : core;
        var off = pop ? core : s;
        off.enabled = false;
        on.sprite = sparkSprites[pop ? 0 : 1];
        float k = (pop ? world : world * 0.6f) / rootK / Mathf.Max(1e-4f, on.sprite.bounds.size.x);
        on.transform.localPosition = new Vector3(f.x * sw, f.y * sw, 0f);
        on.transform.localScale = new Vector3(k, k, 1f);
        on.color = new Color(1f, 1f, 1f, (pop ? 1f : 0.6f) * r.light * director.DriftAlpha);
        on.enabled = true;
    }

    public void Destroy()
    {
        if (atlas != null) atlas.Destroy();
        if (fxAtlas != null) fxAtlas.Destroy();
        BackdropAtlas.Kill(additive);
        additive = null;
        Rocks.Clear();
    }
}
