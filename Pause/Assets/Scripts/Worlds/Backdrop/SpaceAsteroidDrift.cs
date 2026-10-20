using System.Collections.Generic;
using UnityEngine;

// Space's drifting asteroid field: Codex's three cratered rocks with the
// glowing magenta cracks (asteroid_00..02) cross the backdrop like comets,
// tumbling, their cracks blinking and smoke trailing off them. They are the
// BACKGROUND rocks: they must never be mistaken for the hazard rocks of
// SpaceAsteroidField (same art, but those fall from the top, full colour, and
// cost a heart), so these differ in three ways:
//
//   look       noticeably bigger than any hazard rock (the smallest is wider
//              than the largest hazard), tinted toward grey and dimmer
//              (DriftRockTint) with muted cracks, so they read as distant.
//   motion     purely horizontal: in from the left edge and across to the
//              right, or in from the right and across to the left (the side
//              with fewer rocks in flight is preferred, so both are always
//              used). NO scroll with the backdrop parallax and no random
//              heading: they never travel down (nor up) the screen. Only a tiny
//              sideways-axis bob (AsteroidSwayMin..Max, zero net) and a gentle
//              dodge round stations / planets keep them from being rulers.
//   presence   a steady few in flight (AsteroidDensity), recycled through a
//              fixed pool once they have left the view. Space only (only
//              SpaceDirector builds this).
//   size       every rock its own size: a continuous AsteroidSizeMin..Max x
//              AsteroidBaseSize, weighted toward the smaller end with a few
//              big ones, capped at AsteroidMaxScreenShare of the screen width.
//              The size picks the depth tier (big -> near: brighter, sorted in
//              front; small -> deep: hazed and dim) with some overlap, so any
//              size can turn up at any depth. Speed: small rocks faster, big
//              ones slower, far ones slower (AsteroidSpeedMin..Max u/s), and a
//              slow tumble of AsteroidSpinMin..Max deg/s either way.
//   events     purely decorative (they never touch the ship or gameplay and
//              pay nothing). Now and then a rock is "fated" at spawn: it eases
//              onto a meeting course and
//                - collides with another rock at its depth: the smaller bursts
//                  (both, when they are alike) in the rock explosion with
//                  smoke debris, the bigger one is only shaken; or
//                - flies into a backdrop planet / planetoid: an impact burst
//                  plays ON the planet's surface at the contact point, a smoke
//                  plume leaves it, and a glowing crater mark fades over
//                  MarkLife (it rides with the planet); the rock is consumed.
//              Every other rock gives planets, planetoids, stations and the
//              other rocks of its depth a wide berth (a vertical dodge).
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
//   breakable  the rocks are no longer only scenery: each carries a pooled, invisible, collider-less hit body
//              (BackdropRockTarget: tag Astr, ClearTarget, registered as scenery) at its drawn position, so
//              every ship attack and the ultimate hit it through the ordinary hazard plumbing. It takes
//              RockHits (1 small, 2 medium, 3 big) full hits, blinks bright and smokes harder as it is hurt,
//              then bursts in the rock explosion and pays what a hazard rock pays (score, star dust, codex,
//              secret meter, rock count). Its smoke keeps drifting after it is gone. It never touches the
//              ship and nothing else treats it as a body (ClearTarget.Scenery).
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
    public const int AsteroidDensity = 4;               // rocks kept in flight (target: 2-5 visible at once: they are big)
    public const int AsteroidSlots = 8;                 // pool: never more than this
    public const float AsteroidSpawnGapMin = 0.8f, AsteroidSpawnGapMax = 2.4f;   // s between arrivals
    // ---- size (drawn sprite width = AsteroidBaseSize x a multiplier) ----
    // Hazard rocks are at most SpaceAsteroidField.Width[2] = 1.25 u wide: even the smallest backdrop rock is wider.
    public const float AsteroidBaseSize = 1.5f;         // world units at multiplier 1
    public const float AsteroidSizeMin = 0.85f, AsteroidSizeMax = 2.1f;
    public const float AsteroidSizeSkew = 2.0f;         // > 1: most rocks small / medium, a few big
    public const float AsteroidArtFill = 0.86f;         // the rock's art spans this share of its sprite
    public const float AsteroidMaxScreenShare = 0.42f;  // the biggest rock's art: at most this share of the screen width
    // ---- motion ----
    public const float AsteroidSpeedMin = 0.12f, AsteroidSpeedMax = 0.55f;       // own (horizontal) speed, u/s
    public const float AsteroidSpeedSmall = 0.48f, AsteroidSpeedBig = 0.16f;    // ... at the smallest / biggest size
    public static readonly float[] AsteroidDepthSpeed = { 0.78f, 0.88f, 1f, 1f };   // per tier, far -> near
    public const float AsteroidSpinMin = 3f, AsteroidSpinMax = 15f;             // tumble, deg/s, either way
    public const float AsteroidSwayMin = 0.02f, AsteroidSwayMax = 0.06f;        // vertical bob amplitude, u (zero net)
    public const float AsteroidClearance = 0.15f;      // gap kept to a station / rock at the same depth
    public const float AsteroidDodgeSpeed = 0.9f;       // u/s a rock slides up / down to clear something at its depth
    // Greyed out and a bit dimmer than the hazard rocks (which are drawn in full colour).
    public static readonly Color DriftRockTint = new Color(0.60f, 0.62f, 0.67f);
    public const float AsteroidEnterMargin = 0.1f;      // spawn this far outside the view
    public const float AsteroidLeaveMargin = 0.6f;      // ... recycled once this far outside
    // ---- blink ----
    public const int BlinkPulse = 0, BlinkDouble = 1, BlinkFlicker = 2;
    public static readonly float[] AsteroidBlinkPeriods = { 1.4f, 2.2f, 1.1f };
    public const float CrackGain = 0.55f;               // additive strength of the brightest step (muted: background rocks)
    public static readonly Color CrackTint = new Color(0.86f, 0.58f, 0.86f);     // muted magenta-pink (Space's cue colour)
    // ---- breakable ----
    public const string RockObjectName = "space_drift_asteroid";   // the hit body's name (codex match: spacedriftasteroid)
    public const int RockHitsSmall = 1, RockHitsMedium = 2, RockHitsBig = 3;   // full ship-attack hits to break it
    public const float RockHitShare = 0.8f;             // hit radius as a share of the rock's drawn half-width
    public const float RockHitFlash = 0.09f;            // s a hit lights the rock
    public const float RockHurtSmoke = 0.45f;           // smoke gap x this on the last hit point (1 at full health)
    public const int RockBreakPuffs = 2;                // extra puffs as it breaks (the smoke then lingers)
    public const float RockBigShare = 0.5f;             // sizeT from here the burst is Medium, below Small
    // The hit bodies exist while a run is on (the world manager) or when a test forces them.
    public static bool RocksBreakableOverride;
    public static bool RocksBreakable { get { return RocksBreakableOverride || WorldManager.Instance != null; } }

    public static int RockHitsFor(float sizeT)
    {
        return sizeT < 1f / 3f ? RockHitsSmall : sizeT < 2f / 3f ? RockHitsMedium : RockHitsBig;
    }

    // ---- decorative events (fated rocks) ----
    public const float RockCollideChance = 0.20f;       // share of spawns fated to hit another rock
    public const float PlanetImpactChance = 0.22f;      // ... to fly into a planet / planetoid
    public const float AimSpeed = 0.4f;                 // u/s a fated rock slides vertically onto its target's line
    public const float CollideShare = 0.75f;            // rocks collide when closer than this x the sum of their reaches
    public const float PlanetRadiusShare = 0.42f;       // a planet's surface radius as a share of its drawn size
    public const float AlikeSizeT = 0.3f;               // sizes closer than this burst together; else the bigger survives
    public const int ImpactPuffs = 4, PlumePuffs = 5;   // smoke debris of a rock collision / of a planet impact
    public const float DebrisSpeedMin = 0.2f, DebrisSpeedMax = 0.5f;   // u/s the debris flies out at
    public static bool EventsDisabled;                  // test switch: no fates, no collisions, no impacts
    // Like the hit bodies, the burst events (real explosion effects) only happen while a run is on (or a test forces them),
    // never in the menus / codex viewer.
    public static bool EventsOn { get { return !EventsDisabled && RocksBreakable; } }
    public const int MarkSlots = 4;                     // pooled crater marks
    public const float MarkLife = 3.2f;                 // s a crater mark glows
    public const float MarkPerSize = 0.22f, MarkSizeMin = 0.12f;       // mark width: x rock size (u)
    public const int MarkOrder = 1;                     // sorting offset over the planet it sits on

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
    internal Color DriftLit(int tier) { return Lit(DriftRockTint, Tiers[tier]); }
    // Lone structures (stations, planetoids) the rocks keep clear of.
    internal BackdropPool DriftStations { get { return stations; } }
    internal BackdropPool DriftPlanetoids { get { return planetoids; } }
    internal BackdropPool DriftPlanets { get { return planets; } }
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
        public float heading, speed, spin, rot; // heading deg (0 = travelling right, 180 = left), speed u/s, spin deg/s, rotation deg
        public int id;                          // bumped on every spawn (a fated rock's aim checks it)
        public int fate;                        // 0 none, FateRock: meets aimRock, FatePlanet: flies into aimPlanet
        public Rock aimRock; public int aimId;
        public BackdropPiece aimPlanet; public float aimAge;
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
        public BackdropRockTarget target;       // the hit body (pooled with the rock)
        public int maxHealth;
        public float health, flash;
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

    public const int FateNone = 0, FateRock = 1, FatePlanet = 2;
    public int Spawned, PuffsEmitted, SparksPopped, SpawnRejects;
    public int RockCollisions, PlanetImpacts;       // decorative events so far (tests)
    public int RockFates, PlanetFates;              // rocks fated at spawn
    // Crater marks: pooled glows pinned on a planet; owner piece null = free.
    public readonly SpriteRenderer[] Marks = new SpriteRenderer[SpaceDirector.MarkSlots];
    public readonly BackdropPiece[] MarkPlanet = new BackdropPiece[SpaceDirector.MarkSlots];
    public readonly float[] MarkAge = new float[SpaceDirector.MarkSlots];
    readonly float[] markPlanetAge = new float[SpaceDirector.MarkSlots];
    readonly float[] markOffX = new float[SpaceDirector.MarkSlots], markOffY = new float[SpaceDirector.MarkSlots];
    readonly float[] markW = new float[SpaceDirector.MarkSlots];
    public int LiveMarks
    {
        get { int n = 0; for (int i = 0; i < MarkPlanet.Length; i++) if (MarkPlanet[i] != null) n++; return n; }
    }
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
    Material additive;
    Transform targetHolder;
    float spawnIn;
    int nextId;
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
        targetHolder = new GameObject("asteroid targets").transform;
        targetHolder.SetParent(pool.items[0].root.parent, false);
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
        for (int i = 0; i < Marks.Length; i++)
        {
            Marks[i] = Child(holder, "crater");
            Marks[i].sprite = sparkSprites[1];
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

    // Which way the next rock crosses: toward the side with fewer rocks in flight (random on a tie),
    // so left-to-right and right-to-left are both always in the sky.
    int FreshDirection()
    {
        int right = 0, left = 0;
        foreach (var r in Rocks) if (r.active) { if (r.heading < 90f) right++; else left++; }
        if (right == left) return rng.Next(2) == 0 ? 1 : -1;
        return right < left ? 1 : -1;
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
        m = Mathf.Max(SpaceDirector.AsteroidSizeMin, Mathf.Min(m, cap));
        float sizeT = Mathf.InverseLerp(SpaceDirector.AsteroidSizeMin, SpaceDirector.AsteroidSizeMax, m);
        int tier = Mathf.Clamp(Mathf.FloorToInt(sizeT * 4f + Rand(-0.75f, 0.75f)), 0, SpaceDirector.Tiers.Length - 1);
        float size = SpaceDirector.AsteroidBaseSize * m;

        // Which way it crosses (left -> right is heading 0, right -> left 180): never up or down.
        int dir = FreshDirection();
        float heading = dir > 0 ? 0f : 180f;

        // Now and then fated to meet another rock or a planet at its depth (it takes its target's depth).
        int fate = FateNone;
        Rock aimRock = null;
        BackdropPiece aimPlanet = null;
        if (entering && SpaceDirector.EventsOn)
        {
            double roll = rng.NextDouble();
            if (roll < SpaceDirector.RockCollideChance) aimRock = PickAimRock(dir, slot);
            else if (roll < SpaceDirector.RockCollideChance + SpaceDirector.PlanetImpactChance) aimPlanet = PickAimPlanet(dir);
            if (aimRock != null) { fate = FateRock; tier = aimRock.piece.tier; }
            else if (aimPlanet != null) { fate = FatePlanet; tier = Mathf.Clamp(aimPlanet.tier, 0, SpaceDirector.Tiers.Length - 1); }
        }
        float reach = size * SpaceDirector.AsteroidArtFill * 0.5f;
        string layer = SpaceDirector.LayerOf(tier, false);

        float speed = Mathf.Clamp(Mathf.Lerp(SpaceDirector.AsteroidSpeedSmall, SpaceDirector.AsteroidSpeedBig, sizeT) *
                                  Rand(0.85f, 1.15f) * SpaceDirector.AsteroidDepthSpeed[tier],
                                  SpaceDirector.AsteroidSpeedMin, SpaceDirector.AsteroidSpeedMax);
        float vx = dir * speed, vy = 0f;

        // Where: across the edge it crosses in from (just outside the view), or (the opening field) anywhere in
        // view. A fated rock starts on its target's line. Clear of the stations and rocks at its depth.
        float x = 0f, y = 0f;
        bool placed = false;
        for (int attempt = 0; attempt < 4 && !placed; attempt++)
        {
            y = Rand(-HalfH * 0.75f, HalfH * 0.85f);
            if (fate == FateRock) y = aimRock.baseY + Rand(-0.3f, 0.3f);
            else if (fate == FatePlanet) y = aimPlanet.y + Rand(-0.3f, 0.3f);
            if (entering) x = -dir * (HalfW + reach + SpaceDirector.AsteroidEnterMargin);
            else x = Rand(-HalfW + reach * 0.5f, HalfW - reach * 0.5f);
            placed = Clear(x, y, reach, tier, slot, fate);
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
        p.rate = 0f;            // no parallax scroll: the rocks never travel down with the backdrop
        p.vx = vx;
        p.vy = vy;
        p.x = r.baseX = x;
        p.y = r.baseY = y;
        r.heading = heading;
        r.speed = speed;
        r.id = ++nextId;
        r.fate = fate;
        r.aimRock = aimRock;
        r.aimId = aimRock != null ? aimRock.id : 0;
        r.aimPlanet = aimPlanet;
        r.aimAge = aimPlanet != null ? aimPlanet.age : 0f;
        if (fate == FateRock) RockFates++; else if (fate == FatePlanet) PlanetFates++;
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
        r.livePuffs = 0;      // (a broken rock's last puffs may still be in the air: counted back in below)
        for (int i = 0; i < PuffOwner.Length; i++) if (PuffOwner[i] == slot) r.livePuffs++;
        r.sparkPace = Mathf.Lerp(0.8f, 1.3f, sizeT);
        r.sparkIn = Rand(0.3f, SpaceDirector.AsteroidSparkMax) * r.sparkPace;
        r.sparkAge = -1f;
        r.maxHealth = SpaceDirector.RockHitsFor(sizeT);
        r.health = r.maxHealth;
        r.flash = 0f;
        Spawned++;
        Pose(r, 0f);
        Paint(r);
        SyncTarget(r);
        return true;
    }

    // A rock at this depth to run into: in view, and ahead of a rock that enters on the `dir` side (it will cross
    // the whole view toward it). Null when none is suitable.
    Rock PickAimRock(int dir, int self)
    {
        Rock best = null;
        int n = 0;
        for (int i = 0; i < Rocks.Count; i++)
        {
            var o = Rocks[i];
            if (i == self || !o.active || o.fate != FateNone) continue;
            if (Mathf.Abs(o.baseX) > HalfW * 0.7f || Mathf.Abs(o.baseY) > HalfH * 0.8f) continue;
            if (o.piece.vx * dir > 0f && o.speed >= SpaceDirector.AsteroidSpeedMax * 0.5f) continue;   // fleeing ahead of it
            if (rng.Next(++n) == 0) best = o;                     // reservoir pick
        }
        return best;
    }

    // A planet / planetoid in view to fly into.
    BackdropPiece PickAimPlanet(int dir)
    {
        BackdropPiece best = null;
        int n = 0;
        for (int g = 0; g < 2; g++)
        {
            var bodies = g == 0 ? director.DriftPlanets : director.DriftPlanetoids;
            if (bodies == null) continue;
            foreach (var b in bodies.items)
            {
                if (!b.active || b.parent != null) continue;
                if (Mathf.Abs(b.x) > HalfW * 0.8f || Mathf.Abs(b.y) > HalfH * 0.8f) continue;
                if (rng.Next(++n) == 0) best = b;
            }
        }
        return best;
    }

    // Would a rock at (x, y) sit clear of every station / planetoid and rock?
    // (At spawn every body counts; in flight only its own depth, see Steer.)
    bool Clear(float x, float y, float reach, int tier, int self, int fate)
    {
        if (!ClearOf(director.DriftStations, x, y, reach)) return false;
        if (fate != FatePlanet && !ClearOf(director.DriftPlanetoids, x, y, reach)) return false;
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

    // ------------------------------------------------------------- hits --

    // The rock's hit body follows it while it is in view (and a run is on); it is made on first need and
    // recreated if something swept it away (a boss arriving clears the board's hazards).
    void SyncTarget(Rock r)
    {
        bool want = r.active && SpaceDirector.RocksBreakable && director.DriftAlpha > 0.5f &&
                    Mathf.Abs(r.piece.x) < HalfW + Reach(r) && Mathf.Abs(r.piece.y) < HalfH + Reach(r);
        if (r.target == null)
        {
            if (!want) return;
            var go = new GameObject(SpaceDirector.RockObjectName);
            go.tag = "Astr";
            go.transform.SetParent(targetHolder, false);
            go.SetActive(false);
            r.target = go.AddComponent<BackdropRockTarget>();
            r.target.Owner = this;
            r.target.Rock = r;
            r.target.Clear = ClearTarget.Ensure(go);
            r.target.Clear.Scenery = true;
        }
        var tr = r.target.transform;
        if (want)
        {
            tr.position = r.piece.root.position;
            r.target.Clear.SetRadius(Reach(r) * SpaceDirector.RockHitShare);
            r.target.Clear.enabled = true;      // (a break released it: the slot's next rock is a fresh target)
        }
        if (r.target.gameObject.activeSelf != want) r.target.gameObject.SetActive(want);
    }

    public int Breaks, RockHits;

    // A ship attack lands on a rock (`weight`: its share of one full hit).
    internal void Hit(Rock r, int ship, float weight)
    {
        if (!r.active) return;
        RockHits++;
        r.health -= weight;
        r.flash = SpaceDirector.RockHitFlash;
        r.sparkIn = 0f;                         // the cracks spit a spark
        if (r.health <= 0.0001f) { Break(r, ship); return; }
        r.emitIn = 0f;                          // ... and a puff
        Paint(r);
    }

    // Bursts like a destroyed hazard rock (explosion, sound, score, codex, meter), keeps its smoke in the air.
    void Break(Rock r, int ship)
    {
        Breaks++;
        var go = r.target.gameObject;
        TargetExplosion.Spawn(go.transform.position, TargetExplosion.Kind.Rock,
                              r.sizeT < SpaceDirector.RockBigShare ? TargetExplosion.Size.Small : TargetExplosion.Size.Medium, ship);
        EnemyDeathAudio.Play(go);
        collisionDetection.AwardDestroyedTarget(go);
        ClearTarget.Release(go);
        int index = Rocks.IndexOf(r);
        for (int k = 0; k < SpaceDirector.RockBreakPuffs; k++) Puff(r, index);   // a last breath of smoke
        Consume(r);
    }

    // The rock leaves the sky at once (its puffs stay: Retire would clear them) and frees its slot.
    void Consume(Rock r)
    {
        r.active = false;
        r.fate = FateNone;
        r.crack.enabled = false;
        foreach (var sp in r.sparks) sp.enabled = false;
        r.sparkAge = -1f;
        r.piece.Show(false);
        SyncTarget(r);
    }

    Vector3 WorldAt(float x, float y) { return pool.items[0].root.parent.TransformPoint(new Vector3(x, y, 0f)); }

    // ---- decorative events: pure scenery, no score, no ship contact ----

    // Two rocks at one depth meet. The smaller bursts (both when alike); the bigger is only shaken.
    public void Collide(Rock a, Rock b)
    {
        if (!a.active || !b.active) return;
        RockCollisions++;
        Rock small = a.sizeT <= b.sizeT ? a : b, big = small == a ? b : a;
        float mx = (a.piece.x + b.piece.x) * 0.5f, my = (a.piece.y + b.piece.y) * 0.5f;
        bool both = Mathf.Abs(a.sizeT - b.sizeT) < SpaceDirector.AlikeSizeT;
        Pop(small, mx, my);
        if (both) Pop(big, mx, my);
        else
        {
            big.health -= 1f;                          // shaken: a hit point, a flash, a spark and a puff
            big.flash = SpaceDirector.RockHitFlash;
            big.sparkIn = 0f;
            big.emitIn = 0f;
            if (big.health <= 0.0001f) Pop(big, mx, my);
        }
    }

    // A rock flies into a planet / planetoid: an impact on its surface where it touched, the rock is consumed.
    public void Impact(Rock r, BackdropPiece planet)
    {
        if (!r.active) return;
        PlanetImpacts++;
        float dx = r.piece.x - planet.x, dy = r.piece.y - planet.y;
        float d = Mathf.Sqrt(dx * dx + dy * dy);
        float nx = d > 1e-4f ? dx / d : -Mathf.Sign(r.piece.vx), ny = d > 1e-4f ? dy / d : 0f;
        float radius = planet.size * SpaceDirector.PlanetRadiusShare;
        float cx = planet.x + nx * radius, cy = planet.y + ny * radius;     // the contact point on the surface
        int index = Rocks.IndexOf(r);
        TargetExplosion.Spawn(WorldAt(cx, cy), TargetExplosion.Kind.Rock, TargetExplosion.Size.Small, 0);
        for (int k = 0; k < SpaceDirector.PlumePuffs; k++)
        {
            // a plume leaving the surface along its normal, fanned out; it rides the planet's own scroll
            float ang = Mathf.Atan2(ny, nx) + Rand(-0.9f, 0.9f), sp = Rand(SpaceDirector.DebrisSpeedMin, SpaceDirector.DebrisSpeedMax);
            EmitPuff(r, index, cx, cy, Mathf.Cos(ang) * sp, Mathf.Sin(ang) * sp, planet.rate);
        }
        StartMark(planet, cx - planet.x, cy - planet.y, r.piece.size * SpaceDirector.MarkPerSize);
        if (r.target != null && r.target.gameObject.activeSelf) ClearTarget.Release(r.target.gameObject);
        Consume(r);
    }

    // Bursts a rock in a collision at (mx, my): explosion, smoke debris flying out. No score, no codex.
    void Pop(Rock r, float mx, float my)
    {
        int index = Rocks.IndexOf(r);
        if (r.target != null && r.target.gameObject.activeSelf) ClearTarget.Release(r.target.gameObject);
        TargetExplosion.Spawn(WorldAt(r.piece.x, r.piece.y), TargetExplosion.Kind.Rock,
                              r.sizeT < SpaceDirector.RockBigShare ? TargetExplosion.Size.Small : TargetExplosion.Size.Medium, 0);
        for (int k = 0; k < SpaceDirector.ImpactPuffs; k++)
        {
            float ang = Rand(0f, 6.283f), sp = Rand(SpaceDirector.DebrisSpeedMin, SpaceDirector.DebrisSpeedMax);
            EmitPuff(r, index, Mathf.Lerp(r.piece.x, mx, 0.5f), Mathf.Lerp(r.piece.y, my, 0.5f),
                     Mathf.Cos(ang) * sp, Mathf.Sin(ang) * sp, 0f);
        }
        Consume(r);
    }

    void StartMark(BackdropPiece planet, float offX, float offY, float width)
    {
        int slot = -1;
        for (int i = 0; i < MarkPlanet.Length; i++) if (MarkPlanet[i] == null) { slot = i; break; }
        if (slot < 0) return;
        MarkPlanet[slot] = planet;
        markPlanetAge[slot] = planet.age;
        MarkAge[slot] = 0f;
        markOffX[slot] = offX;
        markOffY[slot] = offY;
        markW[slot] = Mathf.Max(SpaceDirector.MarkSizeMin, width);
        Marks[slot].sortingOrder = planet.sr.sortingOrder + SpaceDirector.MarkOrder;
        Marks[slot].enabled = true;
    }

    // The marks ride their planet, step-fade, and go when it is recycled or their time is up.
    void TickMarks(float dt)
    {
        float alpha = director.DriftAlpha;
        for (int i = 0; i < MarkPlanet.Length; i++)
        {
            var b = MarkPlanet[i];
            if (b == null) continue;
            MarkAge[i] += dt;
            var sr = Marks[i];
            if (!b.active || b.age < markPlanetAge[i] - 1e-4f || MarkAge[i] >= SpaceDirector.MarkLife)
            {
                MarkPlanet[i] = null;
                sr.enabled = false;
                continue;
            }
            markPlanetAge[i] = b.age;
            float f = MarkAge[i] / SpaceDirector.MarkLife;
            float k = markW[i] / Mathf.Max(1e-4f, sr.sprite.bounds.size.x) * (f < 0.15f ? 1.4f : 1f);
            sr.transform.localPosition = new Vector3(b.x + markOffX[i], b.y + markOffY[i], 0f);
            sr.transform.localScale = new Vector3(k, k, 1f);
            Color c = SpaceDirector.CrackTint;
            c.a = (f < 0.15f ? 1f : f < 0.5f ? 0.7f : f < 0.8f ? 0.4f : 0.2f) * alpha;      // stepped fade
            sr.color = c;
        }
    }

    // ------------------------------------------------------------- frame --

    public void Tick(float dt, float v)
    {
        if (!ready) return;
        for (int i = 0; i < Rocks.Count; i++)
        {
            var r = Rocks[i];
            if (!r.active) continue;
            var p = r.piece;
            if (dt > 0f) { Aim(r, dt); Steer(r, i, dt); }
            if (r.flash > 0f) r.flash = Mathf.Max(0f, r.flash - dt);
            p.age += dt;
            r.baseX += p.vx * dt;
            r.baseY += p.vy * dt;          // (zero: purely horizontal)
            r.rot += r.spin * dt;
            Pose(r, dt);
            if (dt > 0f && Meet(r)) continue;
            float out_ = Reach(r) + SpaceDirector.AsteroidLeaveMargin;
            if (Mathf.Abs(p.x) > HalfW + out_ || Mathf.Abs(p.y) > HalfH + out_) { Retire(r, i); continue; }
            Emit(r, i, dt);
            Sparks(r, dt);
            Paint(r);
            SyncTarget(r);
        }
        TickPuffs(dt, v);
        TickMarks(dt);

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

    // A fated rock slides vertically onto its target's line so the two meet (the target may be moving or
    // scrolling); it forgets the fate when the target is gone or was recycled for another.
    void Aim(Rock r, float dt)
    {
        float ty;
        if (r.fate == FateRock)
        {
            var o = r.aimRock;
            if (o == null || !o.active || o.id != r.aimId) { r.fate = FateNone; return; }
            ty = o.baseY;
        }
        else if (r.fate == FatePlanet)
        {
            var b = r.aimPlanet;
            if (b == null || !b.active || b.age < r.aimAge - 1e-4f) { r.fate = FateNone; return; }
            r.aimAge = b.age;
            ty = b.y;
        }
        else return;
        float d = ty - r.baseY, step = SpaceDirector.AimSpeed * dt;
        r.baseY += Mathf.Clamp(d, -step, step);
    }

    // Has a fated rock reached its target? Returns true when the rock was consumed.
    bool Meet(Rock r)
    {
        if (!SpaceDirector.EventsOn) return false;
        if (r.fate == FateRock)
        {
            var o = r.aimRock;
            if (o == null || !o.active || o.id != r.aimId) return false;
            float dx = r.piece.x - o.piece.x, dy = r.piece.y - o.piece.y;
            float min = (Reach(r) + Reach(o)) * SpaceDirector.CollideShare;
            if (dx * dx + dy * dy > min * min) return false;
            Collide(r, o);
            return !r.active;
        }
        if (r.fate == FatePlanet)
        {
            var b = r.aimPlanet;
            if (b == null || !b.active) return false;
            float dx = r.piece.x - b.x, dy = r.piece.y - b.y;
            float min = b.size * SpaceDirector.PlanetRadiusShare + Reach(r) * 0.2f;
            if (dx * dx + dy * dy > min * min) return false;
            Impact(r, b);
            return true;
        }
        return false;
    }

    // Keep clear of the stations / planets / planetoids / rocks at the same depth by sliding up or down (a
    // rock only moves along its own horizontal line otherwise). A fated rock does not dodge its target.
    void Steer(Rock r, int self, float dt)
    {
        var p = r.piece;
        float reach = Reach(r);
        float ax = 0f, ay = 0f;
        Repel(director.DriftStations, r, reach, ref ax, ref ay);
        Repel(director.DriftPlanetoids, r, reach, ref ax, ref ay);
        Repel(director.DriftPlanets, r, reach, ref ax, ref ay);
        for (int i = 0; i < Rocks.Count; i++)
        {
            var o = Rocks[i];
            if (i == self || !o.active || o.piece.tier != p.tier || r.fate == FateRock || o.fate == FateRock) continue;
            Push(r.baseX - o.baseX, r.baseY - o.baseY, reach + Reach(o), ref ax, ref ay);
        }
        if (ay == 0f) return;
        r.baseY += Mathf.Clamp(ay, -1f, 1f) * SpaceDirector.AsteroidDodgeSpeed * dt;
    }

    void Repel(BackdropPool bodies, Rock r, float reach, ref float ax, ref float ay)
    {
        if (bodies == null) return;
        foreach (var b in bodies.items)
        {
            if (!b.active || b.parent != null || b.tier != r.piece.tier || b == r.aimPlanet) continue;
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
        if (r.flash > 0f) c = Color.Lerp(c, new Color(1f, 1f, 1f, c.a), 0.55f * r.flash / SpaceDirector.RockHitFlash);   // a hit lights it
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
        SyncTarget(r);
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
        // a hurt rock smokes harder: the gap closes to RockHurtSmoke x on its last hit point
        float hurt = Mathf.Lerp(1f, SpaceDirector.RockHurtSmoke, Mathf.Clamp01((r.maxHealth - r.health) / Mathf.Max(1f, r.maxHealth - 1f)));
        r.emitIn = r.emitEvery * hurt * Rand(0.8f, 1.2f);
        if (r.livePuffs >= SpaceDirector.AsteroidPuffsPer) return;
        Puff(r, index);
    }

    // One smoke puff off a crack hot spot (false: the field-wide cap held it back).
    bool Puff(Rock r, int index)
    {
        var p = r.piece;
        float wx, wy;
        SpotAt(r, rng.Next(r.spots), out wx, out wy);
        float away = SpaceDirector.PuffAway * (0.6f + 0.8f * r.sizeT);
        float s = Mathf.Max(1e-4f, r.speed);
        float dx = p.vx / s, dy = p.vy / s;
        float jitter = Rand(-0.03f, 0.03f);
        return EmitPuff(r, index, wx, wy, p.vx * SpaceDirector.PuffInherit - dx * away - dy * jitter,
                        p.vy * SpaceDirector.PuffInherit - dy * away + dx * jitter, p.rate);
    }

    // A smoke puff at (wx, wy) with its own velocity, scrolling with `rate` (a planet's plume rides the planet).
    bool EmitPuff(Rock r, int index, float wx, float wy, float pvx, float pvy, float rate)
    {
        int slot = -1;
        for (int i = 0; i < PuffOwner.Length; i++) if (PuffOwner[i] < 0) { slot = i; break; }
        if (slot < 0) return false;
        var p = r.piece;
        PuffOwner[slot] = index;
        PuffAge[slot] = 0f;
        puffLife[slot] = r.puffLife * Rand(0.85f, 1.15f);
        puffX[slot] = wx;
        puffY[slot] = wy;
        puffVx[slot] = pvx;
        puffVy[slot] = pvy;
        puffW[slot] = Mathf.Max(SpaceDirector.PuffSizeMin, p.size * SpaceDirector.PuffPerSize);
        puffRate[slot] = rate;
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
        return true;
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
                if (Rocks[owner].livePuffs > 0) Rocks[owner].livePuffs--;
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
        if (targetHolder != null)
        {
            if (Application.isPlaying) Object.Destroy(targetHolder.gameObject); else Object.DestroyImmediate(targetHolder.gameObject);
        }
        Rocks.Clear();
    }
}

// The hit body of one drifting backdrop rock: invisible, no collider (it can never touch the ship), tagged
// like a hazard rock and registered as scenery, so the ship's attacks find it through ClearTarget and the
// drift field decides what a hit does. Takes every hit in full (IShipAttackTargetUncapped).
public class BackdropRockTarget : MonoBehaviour, IShipAttackTargetUncapped
{
    [System.NonSerialized] public SpaceAsteroidDrift Owner;
    [System.NonSerialized] public SpaceAsteroidDrift.Rock Rock;
    [System.NonSerialized] public ClearTarget Clear;

    public void TakeShipAttack(int ship, float weight, Vector3 at)
    {
        if (Owner != null && Rock != null) Owner.Hit(Rock, ship, weight);
    }
}
