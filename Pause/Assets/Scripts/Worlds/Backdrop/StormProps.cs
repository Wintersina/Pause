using UnityEngine;

// Storm's LIVING PROPS (user, 2026-11-28: "storm stuff approved with latest version go storm"; then "some of these items, if
// it makes sense, they can be closer and larger in atmosphere level where they can look larger"). Small animated drawings in
// real-world liveries (pl/storm/r3/build_props2.py; lights are baked into the 8 frames: red aviation beacons blink, red / green
// navigation lights, white strobes, amber windows flicker, cyan intakes pulse), at two depths:
//
//   GROUND (the "ground" layer, PINNED to the mid tile like the landmarks: same rate, placed by the same GroundPlanner, so they
//          never overlap a landmark or a dock) -- refinery (a lit cloud-top platform), relay mast, a wrecked airship sinking
//          into a dark lane, a vortex (eyewall lightning, turning slowly), a lightning cell (a cloud tower lit from inside:
//          additive light over the deck, it may overlap other pieces) and a crashed GUNSHIP (the user-approved gunship as a dead
//          derelict: rotors dark, one torn off, rimed with hail, tangled in tethers, half sunk in the deck). Rare accents:
//          GroundPool at most, one every GroundGapMin..GroundGapMax of scrolled ground.
//   ALOFT  (its own "aloft" layer, closer to the camera, BackdropCatalog: above the vapour strip, under all the weather) --
//          tethered aerostats, a gas skimmer and a freighter convoy MOORED to buoys (cables baked into the art), sky jellies.
//          Drawn AloftSizeK x their ground size, scrolling AloftRateK x the ground's rate, hazed (a baked blur and a cool tint,
//          AloftTint), never pinned. VERY rare: one at most in view (AloftPool = 1) and often none.
//   USER RULE (2026-11-28): "I don't want things flying around if the player cannot shoot them down; it has to be trapped or
//          something somewhere." No vehicle moves on its own: the moored / tethered ones only bob and sway on their lines
//          (Bob*, Sway*), the wrecks are stuck; only the creatures (jellies, manta rays) drift, slowly.
//   2026-11-28 approved additions (all 4 liveries each): ground -- a storm TUG moored by cable to its buoy (W|C), a lightning-ROD
//          platform (O; tip lamps blink, an arc jumps once a second), a BUOY chain (W|C; lamps chase down it); aloft -- a school
//          of manta RAYS (creatures: they flap and drift like the jellies).
//
// COLOUR VARIANT PER COPY: each kind ships 3 - 7 liveries (<kind>_<variant>_<frame>, Backdrop3/props + aloft,
// backdrop_r3~/props/build_props_atlas.py). A copy's livery is a hash of its spawn count and kind, never the one the previous
// copy of that kind wore; its animation starts at its own phase. Animated from each piece's own age (scaled dt): a paused game
// is a still frame. Nothing is allocated per frame; the props draw from their own random stream (the landmarks' and the
// weather's dice never shift).
public static class StormPropsTuning
{
    public const string Atlas = "props", AloftAtlas = "aloft", AloftLayer = "aloft";
    public const int Frames = 8;
    // screen px (on a 1080 px wide phone, the approved composites) -> world units: a ~6.75 u wide view
    public const float PxPerUnit = 160f;

    // ---- ground ----
    public static readonly string[] Ground = { "refinery", "mast", "wreck", "vortex", "cell", "gunship", "tug", "rods", "buoys" };
    public const int Refinery = 0, Mast = 1, Wreck = 2, Vortex = 3, Cell = 4, Gunship = 5, Tug = 6, Rods = 7, Buoys = 8;
    public static readonly int[] GroundVariants = { 4, 4, 4, 3, 3, 4, 4, 4, 4 };
    // drawn cell width, screen px (pl/storm/r3/composite.py RULES, user-approved)
    public static readonly float[] GroundPx = { 124f, 92f, 128f, 284f, 250f, 240f, 200f, 110f, 120f };   // (the vortex at BackdropCatalog.MaxLandmarkSize; the gunship "nice and large")
    public static float GroundSize(int kind) { return GroundPx[kind] / PxPerUnit; }
    // where each may stand (r3 masks: O lit cloud tops, C textured deck, W dark lanes) and how much of its footprint must match
    public static readonly GroundClass[] GroundOn = { GroundClass.Open, GroundClass.Open, GroundClass.Water, GroundClass.Water | GroundClass.Canopy, GroundClass.Open, GroundClass.Water | GroundClass.Canopy, GroundClass.Water | GroundClass.Canopy, GroundClass.Open, GroundClass.Water | GroundClass.Canopy };
    public static readonly float[] GroundNeed = { .6f, .5f, .6f, .75f, .45f, .6f, .6f, .55f, .6f };
    public static readonly float[] GroundCore = { .34f, .3f, .36f, .42f, .3f, .36f, .34f, .32f, .3f };
    public static PieceRule GroundRule(int kind) { return new PieceRule(GroundOn[kind], GroundNeed[kind], GroundCore[kind]); }
    public static bool Claims(int kind) { return kind != Cell; }            // a cell is light: it never holds ground
    // per ground set (index = variant, 0 none): v1 banded deck and v4 shelf arcs more industry, v2 thunderhead field more cells,
    // v3 night side more lit kinds
    public static readonly int[][] GroundWeights =
    {
        new[] { 3, 3, 2, 1, 3, 2, 2, 2, 2 },
        new[] { 4, 3, 2, 1, 2, 2, 2, 2, 2 },
        new[] { 2, 3, 1, 1, 5, 2, 2, 3, 2 },
        new[] { 3, 4, 2, 1, 3, 2, 3, 2, 2 },
        new[] { 4, 3, 2, 1, 2, 2, 2, 2, 3 },
    };
    public static readonly int[][] GroundMs =
    {
        new[] { 125, 125, 125, 125, 125, 125, 125, 125 },
        new[] { 125, 125, 125, 125, 125, 125, 125, 125 },
        new[] { 125, 125, 125, 125, 125, 125, 125, 125 },
        new[] { 1500, 900, 90, 150, 220, 380, 160, 600 },          // eyewall lightning: two flicker groups a 4 s cycle
        new[] { 1700, 90, 140, 200, 420, 130, 260, 400 },          // the cell's flash and re-strike: a 3.3 s cycle
        new[] { 125, 125, 125, 125, 125, 125, 125, 125 },          // the gunship: the cloud boils round it, its distress lamp blinks once a second
        new[] { 90, 90, 90, 90, 90, 90, 90, 90 },                  // the tug idling on its mooring: props, nav lamps, beacons, the buoy lamp
        new[] { 125, 125, 125, 125, 125, 125, 125, 125 },          // the rod platform: tip lamps blink, an arc jumps once a second (frames 3-4)
        new[] { 140, 140, 140, 140, 140, 140, 140, 140 },          // the buoy chain: lamps chase down the chain, a dark pause between runs
    };
    public const int GroundPool = 6, CellPool = 2;
    public static float GroundGapMin = 2.6f, GroundGapMax = 4.4f;   // scrolled ground (world units) between two ground props
    public const float SizeJitter = .06f;
    public const float VortexSpin = 7f;                              // degrees / s about its eye
    public const float CellAlpha = .85f;                             // the cell's light, at most (additive)
    public const int Tries = 40;

    // ---- aloft ----
    public static readonly string[] Aloft = { "aerostat", "skimmer", "convoy", "jelly", "rays" };
    public const int Aerostat = 0, Skimmer = 1, Convoy = 2, Jelly = 3, Rays = 4;
    public static readonly int[] AloftVariants = { 4, 4, 3, 7, 4 };
    // their ground size (px, as above) x AloftSizeK: 1.6 - 2.5x (user: "closer and larger")
    public static readonly float[] AloftBasePx = { 100f, 118f, 176f, 116f, 124f };
    public static readonly float[] AloftSizeK = { 2.0f, 1.86f, 1.82f, 2.0f, 1.9f };
    public static float AloftSize(int kind) { return AloftBasePx[kind] * AloftSizeK[kind] / PxPerUnit; }
    public const float AloftRateK = 1.45f;                           // x the ground's parallax: closer to the camera
    public const float AloftRate = StormTuning.GroundRate * AloftRateK;
    public const float AloftGrade = .8f;                             // of the world's lift (BackdropCatalog: Graded)
    // depth haze: a cool tint and a little transparency, so they sit behind the play and dimmer than every shot
    public static readonly Color AloftTint = new Color(.80f, .84f, .93f, .82f);
    public static readonly int[][] AloftWeights =
    {
        new[] { 3, 3, 2, 3, 2 },
        new[] { 3, 3, 3, 2, 3 },
        new[] { 3, 2, 2, 3, 2 },
        new[] { 2, 2, 2, 5, 3 },
        new[] { 3, 3, 3, 2, 2 },
    };
    public static readonly int[] AloftMs = { 125, 125, 125, 180, 160 };
    public const int AloftPool = 1;                                  // at most one in view
    public static float AloftGapMin = 16f, AloftGapMax = 34f;        // aloft-layer travel (world units) between two (a view is ~15 u tall)
    public static float AloftFirst = 9f;
    // own motion: NO vehicle travels (the user's rule). The tethered aerostats and the moored skimmer / convoy only bob on their
    // lines (amplitude x their size, period s) and sway (degrees, period s); the jellies and rays, living creatures, also drift slowly.
    public static readonly float[] BobAmp = { .03f, .012f, .01f, .045f, .03f };
    public static readonly float[] BobPeriod = { 5.5f, 4.2f, 6.5f, 3.6f, 4.6f };
    public static readonly float[] SwayDeg = { 2.5f, 1.5f, 1f, 0f, 0f };
    public static readonly float[] SwayPeriod = { 7f, 5.5f, 8f, 1f, 1f };
    public const float JellyDrift = .05f;
    public static bool Drifts(int aloftKind) { return aloftKind == Jelly || aloftKind == Rays; }

    public static string Strip(string kind, int variant) { return kind + "_" + variant; }

    // a copy's livery: a hash of (spawn count, kind), never the previous copy's
    public static int Variant(int spawn, int kind, int count, int previous)
    {
        if (count <= 1) return 0;
        uint h = (uint)(spawn * 73856093) ^ (uint)(kind * 19349663) ^ 0x9e3779b9u;
        h ^= h >> 15; h *= 0x2c1b3c6du; h ^= h >> 12; h *= 0x297a2d39u; h ^= h >> 15;
        int v = (int)(h % (uint)count);
        return v == previous ? (v + 1) % count : v;
    }

    public static int CycleMs(int[] ms) { int c = 0; foreach (int m in ms) c += m; return c; }

    // the frame of a looping strip at time t (s)
    public static int FrameAt(int[] ms, float t)
    {
        int cycle = CycleMs(ms);
        float c = Mathf.Repeat(t * 1000f, cycle);
        for (int f = 0; f < ms.Length; f++)
        {
            if (c < ms[f]) return f;
            c -= ms[f];
        }
        return 0;
    }
    public static int AloftFrame(int kind, float t) { return Mathf.FloorToInt(t * 1000f / AloftMs[kind]) % Frames; }
}

public partial class StormDirector
{
    BackdropPool props, cells, aloft;
    Sprite[][][] groundStrips, aloftStrips;    // [kind][variant] -> its 8 frames (null: not in the atlas)
    int[] lastGroundVariant, lastAloftVariant;
    int lastGroundKind = -1, lastAloftKind = -1;
    float propTravel, propGap, aloftTravel, aloftGap;
    System.Random propRng;

    public BackdropPool Props => props;
    public BackdropPool Cells => cells;
    public BackdropPool AloftPool => aloft;
    public int PropsSpawned { get; private set; }
    public int AloftSpawned { get; private set; }

    float PRand(float a, float b) { return a + (float)propRng.NextDouble() * (b - a); }

    void BuildProps()
    {
        propRng = new System.Random((SeedOverride > 0 ? SeedOverride : 2077) * 53 + 17);
        groundStrips = Strips(set.Atlas(StormPropsTuning.Atlas), StormPropsTuning.Ground, StormPropsTuning.GroundVariants);
        aloftStrips = Strips(set.Atlas(StormPropsTuning.AloftAtlas), StormPropsTuning.Aloft, StormPropsTuning.AloftVariants);
        lastGroundVariant = new int[StormPropsTuning.Ground.Length];
        lastAloftVariant = new int[StormPropsTuning.Aloft.Length];
        for (int i = 0; i < lastGroundVariant.Length; i++) lastGroundVariant[i] = -1;
        for (int i = 0; i < lastAloftVariant.Length; i++) lastAloftVariant[i] = -1;

        props = Pool("ground", StormPropsTuning.GroundPool);
        Grade(props, "ground");
        cells = Pool("ground", StormPropsTuning.CellPool, false, 2);   // the cells' light over the deck and whatever stands on it
        var add = Resources.Load<Shader>(SpaceAsteroidDrift.AdditiveShader);
        if (add != null)
        {
            var m = new Material(add) { name = "StormCellLight" };
            mats.Add(m);
            foreach (var p in cells.items) p.sr.sharedMaterial = m;
        }
        else Grade(cells, "ground");
        aloft = Pool(StormPropsTuning.AloftLayer, StormPropsTuning.AloftPool);
        Grade(aloft, StormPropsTuning.AloftLayer);
        propGap = PRand(StormPropsTuning.GroundGapMin, StormPropsTuning.GroundGapMax);
        aloftGap = StormPropsTuning.AloftFirst;
    }

    static Sprite[][][] Strips(BackdropAtlas atlas, string[] kinds, int[] variants)
    {
        var s = new Sprite[kinds.Length][][];
        for (int k = 0; k < kinds.Length; k++)
        {
            s[k] = new Sprite[variants[k]][];
            for (int v = 0; v < variants[k]; v++)
            {
                var f = atlas != null ? atlas.Frames(StormPropsTuning.Strip(kinds[k], v)) : new Sprite[0];
                s[k][v] = f.Length >= StormPropsTuning.Frames ? f : null;
            }
        }
        return s;
    }

    public bool HasGroundArt(int kind) { return groundStrips != null && groundStrips[kind][0] != null; }
    public bool HasAloftArt(int kind) { return aloftStrips != null && aloftStrips[kind][0] != null; }

    // the opening view: one ground prop already standing in the upper half (the landmarks are dealt around it)
    void OpeningProps()
    {
        if (props == null || !planner.Ready) return;
        double t = planner.Travel;
        SpawnProp(-1, t + HalfH * .05f, t + HalfH * .6f);
    }

    // a weighted pick, never `last` or `not` (no closures: nothing is allocated)
    int PickWeighted(int[] w, int last, int not, bool ground)
    {
        int total = 0;
        for (int i = 0; i < w.Length; i++) if (i != last && i != not && Pickable(i, ground)) total += w[i];
        if (total <= 0) return -1;
        int r = propRng.Next(total);
        for (int i = 0; i < w.Length; i++)
        {
            if (i == last || i == not || !Pickable(i, ground)) continue;
            r -= w[i];
            if (r < 0) return i;
        }
        return -1;
    }

    bool Pickable(int kind, bool ground)
    {
        if (!ground) return HasAloftArt(kind);
        return HasGroundArt(kind) && (kind != StormPropsTuning.Vortex || !VortexInView());
    }

    bool VortexInView()
    {
        foreach (var p in props.items) if (p.active && p.kind == StormPropsTuning.Vortex) return true;
        return false;
    }

    // A ground prop (kind < 0: one picked) centred in the ground band [lo, hi] where the ground suits it and (unless it is a
    // cell) nothing else stands. `variant` < 0: the copy's own livery. False: nothing placed.
    public bool SpawnProp(int kind, double lo, double hi, int variant = -1, int tries = StormPropsTuning.Tries)
    {
        if (props == null || !planner.Ready) return false;
        int vset = Mathf.Clamp(set.Variant, 0, StormPropsTuning.GroundWeights.Length - 1);
        int tried = -1;
        for (int k = 0; k < 3; k++)
        {
            int kd = kind >= 0 ? kind : PickWeighted(StormPropsTuning.GroundWeights[vset], lastGroundKind, tried, true);
            if (kd < 0 || !HasGroundArt(kd)) return false;
            tried = kd;
            bool cell = kd == StormPropsTuning.Cell;
            var pool = cell ? cells : props;
            if (pool.ActiveCount >= pool.Capacity) { if (kind >= 0) return false; continue; }
            float size = StormPropsTuning.GroundSize(kd) * (1f + StormPropsTuning.SizeJitter * PRand(-1f, 1f));
            var rule = StormPropsTuning.GroundRule(kd);
            float rx = GroundPlanner.HalfX(rule, size), ry = GroundPlanner.HalfY(rule, size);
            float x = 0f; double gy = 0.0; bool found = false;
            for (int t = 0; t < tries && !found; t++)
            {
                float cx = PRand(XMin(size), XMax(size));
                double cy = lo + propRng.NextDouble() * (hi - lo);
                if (StormPropsTuning.Claims(kd) && !planner.Free(cx, cy, rx, ry)) continue;
                if (!planner.Accepts(rule, cx, cy, size)) continue;
                x = cx; gy = cy; found = true;
            }
            if (!found) { if (kind >= 0) return false; continue; }
            int foot = -1;
            if (StormPropsTuning.Claims(kd)) { foot = planner.Claim(x, gy, rx, ry); if (foot < 0) return false; }
            var p = pool.Spawn();
            if (p == null) { if (foot >= 0) planner.Release(foot); return false; }
            int n = StormPropsTuning.GroundVariants[kd];
            int v = variant >= 0 ? Mathf.Min(variant, n - 1) : StormPropsTuning.Variant(PropsSpawned, kd, n, lastGroundVariant[kd]);
            if (groundStrips[kd][v] == null) v = 0;
            lastGroundVariant[kd] = v;
            p.frames = groundStrips[kd][v];
            SetSprite(p, p.frames[0], Mathf.Min(size, BackdropCatalog.MaxLandmarkSize));
            p.frames = groundStrips[kd][v];
            p.body.localScale = kd != StormPropsTuning.Cell && propRng.NextDouble() < .5 ? new Vector3(-1f, 1f, 1f) : Vector3.one;
            p.footprint = foot;
            p.core = rule.core;
            p.gy = gy;
            p.x = x;
            p.y = planner.Y(gy);
            p.rate = set.Spec.Rate("ground");
            p.color = Color.white;
            p.dockTint = Color.white;
            p.kind = kd;
            p.tier = v;
            p.phase = PRand(0f, StormPropsTuning.CycleMs(StormPropsTuning.GroundMs[kd]) / 1000f);   // its own animation phase
            Place(p);
            PaintProp(p);
            lastGroundKind = kd;
            PropsSpawned++;
            return true;
        }
        return false;
    }

    // Tests / previews: a ground prop of `kind` around screen y (searched a little up and down).
    public bool SpawnPropAt(int kind, float y, int variant = -1)
    {
        double g = planner.GroundY(y);
        return SpawnProp(kind, g - 1.2, g + 1.2, variant, 400);
    }

    // An aloft prop (kind < 0: one picked) entering at the top of the view (y NaN) or at screen (x, y). False: none placed.
    public bool SpawnAloft(int kind, float x = float.NaN, float y = float.NaN, int variant = -1)
    {
        if (aloft == null || aloft.ActiveCount >= aloft.Capacity) return false;
        int vset = Mathf.Clamp(set.Variant, 0, StormPropsTuning.AloftWeights.Length - 1);
        int kd = kind >= 0 ? kind : PickWeighted(StormPropsTuning.AloftWeights[vset], lastAloftKind, -1, false);
        if (kd < 0 || !HasAloftArt(kd)) return false;
        var p = aloft.Spawn();
        if (p == null) return false;
        int n = StormPropsTuning.AloftVariants[kd];
        int v = variant >= 0 ? Mathf.Min(variant, n - 1) : StormPropsTuning.Variant(AloftSpawned, kd + 11, n, lastAloftVariant[kd]);
        if (aloftStrips[kd][v] == null) v = 0;
        lastAloftVariant[kd] = v;
        float size = StormPropsTuning.AloftSize(kd) * (1f + StormPropsTuning.SizeJitter * PRand(-1f, 1f));
        SetSprite(p, aloftStrips[kd][v][0], size);
        p.body.localPosition = Vector3.zero;
        p.frames = aloftStrips[kd][v];
        bool mirror = propRng.NextDouble() < .5;
        p.body.localScale = mirror ? new Vector3(-1f, 1f, 1f) : Vector3.one;
        p.vx = p.vy = 0f;                                  // moored / tethered: it rides the parallax only
        if (StormPropsTuning.Drifts(kd)) { p.vx = PRand(-1f, 1f) * StormPropsTuning.JellyDrift; p.vy = PRand(0f, 1f) * StormPropsTuning.JellyDrift; }
        p.x = float.IsNaN(x) ? PRand(-HalfW * .62f, HalfW * .62f) : x;
        p.y = float.IsNaN(y) ? SpawnY(p.size) + Mathf.Max(0f, p.vy) * 2f : y;
        p.body.localRotation = Quaternion.identity;
        p.rate = set.Spec.Rate(StormPropsTuning.AloftLayer);
        p.color = StormPropsTuning.AloftTint;
        p.dockTint = Color.white;
        p.kind = kd;
        p.tier = v;
        p.phase = PRand(0f, 8f);
        p.footprint = -1;
        Place(p);
        PaintAloft(p);
        lastAloftKind = kd;
        AloftSpawned++;
        return true;
    }

    // Tests / previews: every prop gone, its ground released.
    public void ClearProps()
    {
        foreach (var pool in new[] { props, cells, aloft })
        {
            if (pool == null) continue;
            foreach (var p in pool.items)
            {
                if (!p.active) continue;
                if (p.footprint >= 0) planner.Release(p.footprint);
                p.footprint = -1;
                Despawn(p);
            }
        }
    }

    // Tests / previews: hold off the props' own spawner (the ground and aloft gaps start over).
    public void HoldProps(float groundGap, float aloftGapUnits)
    {
        propTravel = 0f; propGap = groundGap;
        aloftTravel = 0f; aloftGap = aloftGapUnits;
    }

    // The frame a ground prop shows now (its age plus its own phase).
    public static int GroundFrame(BackdropPiece p) { return StormPropsTuning.FrameAt(StormPropsTuning.GroundMs[p.kind], p.age + p.phase); }
    public static int AloftFrameOf(BackdropPiece p) { return StormPropsTuning.AloftFrame(p.kind, p.age + p.phase); }

    void PaintProp(BackdropPiece p)
    {
        int f = GroundFrame(p);
        if (p.frames != null && p.sr.sprite != p.frames[f]) p.sr.sprite = p.frames[f];
        if (p.kind == StormPropsTuning.Vortex) p.body.localRotation = Quaternion.Euler(0f, 0f, -StormPropsTuning.VortexSpin * (p.age + p.phase));
        float a = p.kind == StormPropsTuning.Cell ? StormPropsTuning.CellAlpha : 1f;
        p.sr.color = new Color(1f, 1f, 1f, a * set.Alpha);
    }

    void PaintAloft(BackdropPiece p)
    {
        int f = AloftFrameOf(p);
        if (p.frames != null && p.sr.sprite != p.frames[f]) p.sr.sprite = p.frames[f];
        float amp = StormPropsTuning.BobAmp[p.kind];
        if (amp > 0f)
        {
            float b = Mathf.Sin((p.age + p.phase) * 2f * Mathf.PI / StormPropsTuning.BobPeriod[p.kind]) * amp * p.size;
            var lp = p.body.localPosition; lp.y = b / Mathf.Max(.0001f, p.root.localScale.y); p.body.localPosition = lp;
        }
        float sway = StormPropsTuning.SwayDeg[p.kind];
        if (sway > 0f) p.body.localRotation = Quaternion.Euler(0f, 0f, Mathf.Sin((p.age + p.phase) * 2f * Mathf.PI / StormPropsTuning.SwayPeriod[p.kind]) * sway);
        Color c = StormPropsTuning.AloftTint;
        c.a *= set.Alpha;
        p.sr.color = c;
    }

    void StepGroundProps(BackdropPool pool, float dt)
    {
        foreach (var p in pool.items)
        {
            if (!p.active) continue;
            p.age += dt;
            p.y = planner.Y(p.gy);
            Place(p);
            if (p.y < -HalfH - p.size * .9f - .5f)
            {
                if (p.footprint >= 0) planner.Release(p.footprint);
                p.footprint = -1;
                Despawn(p);
                continue;
            }
            PaintProp(p);
        }
    }

    void StepProps(float dt, float v, float scrolled)
    {
        if (props == null) return;
        propTravel += scrolled;
        if (planner.Ready && propTravel >= propGap)
        {
            double lo = planner.Travel + HalfH + StormTuning.SpawnAbove + 1f, hi = lo + StormTuning.SpawnBand;
            if (SpawnProp(-1, lo, hi)) { propTravel = 0f; propGap = PRand(StormPropsTuning.GroundGapMin, StormPropsTuning.GroundGapMax); }
            else propGap = propTravel + StormTuning.SpawnRetry;
        }
        StepGroundProps(props, dt);
        StepGroundProps(cells, dt);

        // the gap runs only while the sky is empty: the next one comes AloftGapMin..Max of scroll after the last one left
        if (aloft.ActiveCount == 0) aloftTravel += set.Spec.Rate(StormPropsTuning.AloftLayer) * v * dt;
        if (aloftTravel >= aloftGap && aloft.ActiveCount == 0 && SpawnAloft(-1))
        {
            aloftTravel = 0f;
            aloftGap = PRand(StormPropsTuning.AloftGapMin, StormPropsTuning.AloftGapMax);
        }
        foreach (var p in aloft.items)
            if (p.active && Drift(p, dt, v)) PaintAloft(p);
    }
}
