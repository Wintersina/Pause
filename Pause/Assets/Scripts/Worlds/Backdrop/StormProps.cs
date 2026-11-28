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
//          tethered aerostats, a freighter convoy MOORED to buoys (cables baked into the art), sky jellies, manta rays. (The
//          moored gas SKIMMER moved to the ground on 2026-11-28 so it rides the moorings with the buoys and the tug.)
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
    // the px below were approved at the .41 altitude; everything on (and over) the ground follows the ground's zoom since
    // (2026-11-28: .49, every world 20% closer), so sizes, caps, gaps and reaches are x ZoomK
    public const float ApprovedZoom = .41f;
    public const float ZoomK = StormTuning.GroundZoom / ApprovedZoom;
    // a ground prop's drawn-size cap: BackdropCatalog.MaxLandmarkSize at .41 (the vortex), x ZoomK
    public const float MaxSize = BackdropCatalog.MaxLandmarkSize * ZoomK;

    // ---- ground ----
    public static readonly string[] Ground = { "refinery", "mast", "wreck", "vortex", "cell", "gunship", "tug", "rods", "buoys", "skimmer" };
    public const int Refinery = 0, Mast = 1, Wreck = 2, Vortex = 3, Cell = 4, Gunship = 5, Tug = 6, Rods = 7, Buoys = 8, Skimmer = 9;
    public static readonly int[] GroundVariants = { 4, 4, 4, 3, 3, 4, 4, 4, 4, 4 };
    // drawn cell width, screen px at .41 (pl/storm/r3/composite.py RULES, user-approved); drawn x ZoomK
    public static readonly float[] GroundPx = { 124f, 92f, 128f, 284f, 250f, 240f, 200f, 110f, 120f, 128f };   // (the vortex at MaxSize; the gunship "nice and large")
    public static float GroundSize(int kind) { return GroundPx[kind] * ZoomK / PxPerUnit; }
    // where each may stand (r3 masks: O lit cloud tops, C textured deck, W dark lanes) and how much of its footprint must match
    public static readonly GroundClass[] GroundOn = { GroundClass.Open, GroundClass.Open, GroundClass.Water, GroundClass.Water | GroundClass.Canopy, GroundClass.Open, GroundClass.Water | GroundClass.Canopy, GroundClass.Water | GroundClass.Canopy, GroundClass.Open, GroundClass.Water | GroundClass.Canopy, GroundClass.Water | GroundClass.Canopy };
    public static readonly float[] GroundNeed = { .6f, .5f, .6f, .75f, .45f, .6f, .6f, .55f, .6f, .6f };
    public static readonly float[] GroundCore = { .34f, .3f, .36f, .42f, .3f, .36f, .34f, .32f, .3f, .32f };
    public static PieceRule GroundRule(int kind) { return new PieceRule(GroundOn[kind], GroundNeed[kind], GroundCore[kind]); }
    // PLACEMENT (user rule 2026-11-28: props stand where they make sense and nothing moving passes through or touches one):
    //   * every ground prop holds its ground -- the cell too now -- with a KEEP-OUT box KeepK x its footprint (the crashed gunship
    //     the widest: nothing comes near it), so no landmark, launch site or other prop is ever placed on or against it;
    //   * the moorings stay together: a tug or a skimmer is only ever placed beside a buoy chain and a buoy chain beside a tug or a
    //     skimmer (Companion; all or nothing); a lightning-rod platform only beside a refinery deck, on the flat lit tops (O);
    //   * moving things keep clear of every pinned piece (StormDirector.ClearOfPinned): fork bolts and arcs, a strike's bolt
    //     (except on its own mast), the tumbling debris along its whole path, and the aloft pieces, whose path over the ground is a
    //     straight slide down (no sideways drift; creatures drift down with the wind): spawned only where the stretch it will still
    //     slide over (AloftSlide) holds no pinned piece, and while one is up that stretch is a LANE no new ground piece is placed in
    //     (GroundPlanner.LaneX / LaneLo / LaneHi). The vapour (flow) strip runs UNDER the pinned layer.
    public static bool Claims(int kind) { return true; }
    public static readonly float[] KeepK = { 1.15f, 1.15f, 1.15f, 1.2f, 1.1f, 1.45f, 1.15f, 1.1f, 1.1f, 1.15f };
    public const float BodyShare = .4f;                              // a moving piece's drawn half-extent, x its cell size
    public const float BoltHalfX = .18f, BoltHalfY = .45f;           // a bolt's (thin, tall) drawn half-extents, x its cell size
    public static float MoorReach = .5f * ZoomK;                             // a companion's footprint edge at most this far from its anchor's (u)
    // the companion a kind must stand beside (-1 none); `coin` picks between two
    public static int Companion(int kind, bool coin)
    {
        if (kind == Tug || kind == Skimmer) return Buoys;
        if (kind == Buoys) return coin ? Tug : Skimmer;
        if (kind == Rods) return Refinery;
        return -1;
    }
    // per ground set (index = variant, 0 none): v1 banded deck and v4 shelf arcs more industry, v2 thunderhead field more cells,
    // v3 night side more lit kinds
    public static readonly int[][] GroundWeights =
    {
        new[] { 3, 3, 2, 1, 3, 2, 2, 2, 2, 2 },
        new[] { 4, 3, 2, 1, 2, 2, 2, 2, 2, 2 },
        new[] { 2, 3, 1, 1, 5, 2, 2, 3, 2, 2 },
        new[] { 3, 4, 2, 1, 3, 2, 3, 2, 2, 2 },
        new[] { 4, 3, 2, 1, 2, 2, 2, 2, 3, 3 },
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
        new[] { 125, 125, 125, 125, 125, 125, 125, 125 },          // the skimmer on its mooring lines: nav lamps, idling intake glow
    };
    public const int GroundPool = 8, CellPool = 2;
    public static float GroundGapMin = 4.2f * ZoomK, GroundGapMax = 6.8f * ZoomK;   // scrolled ground (world units) between two ground props
    public const float SpawnAhead = 1f * ZoomK;                      // a ground prop is dealt this far above the landmarks' band (out of sight)
    public const float SizeJitter = .06f;
    public const float VortexSpin = 7f;                              // degrees / s about its eye
    public const float CellAlpha = .8f;                              // the cell's light, at most (additive)
    public const int Tries = 40;

    // ---- aloft ----
    public static readonly string[] Aloft = { "aerostat", "convoy", "jelly", "rays" };
    public const int Aerostat = 0, Convoy = 1, Jelly = 2, Rays = 3;
    public static readonly int[] AloftVariants = { 4, 3, 7, 4 };
    // their ground size (px at .41 x ZoomK, as above) x AloftSizeK: 1.6 - 2.5x (user: "closer and larger")
    public static readonly float[] AloftBasePx = { 100f, 176f, 116f, 124f };
    public static float AloftGroundSize(int kind) { return AloftBasePx[kind] * ZoomK / PxPerUnit; }
    public const float AloftMaxSize = 2.3f * ZoomK;                  // the largest an aloft piece is drawn (u)
    public static readonly float[] AloftSizeK = { 2.0f, 1.82f, 2.0f, 1.9f };
    public static float AloftSize(int kind) { return AloftGroundSize(kind) * AloftSizeK[kind]; }
    public const float AloftRateK = 1.45f;                           // x the ground's parallax: closer to the camera
    public const float AloftRate = StormTuning.GroundRate * AloftRateK;
    public const float AloftGrade = .8f;                             // of the world's lift (BackdropCatalog: Graded)
    // depth haze: a cool tint and a little transparency, so they sit behind the play and dimmer than every shot
    public static readonly Color AloftTint = new Color(.80f, .84f, .93f, .82f);
    public static readonly int[][] AloftWeights =
    {
        new[] { 3, 2, 3, 2 },
        new[] { 3, 3, 2, 3 },
        new[] { 3, 2, 3, 2 },
        new[] { 2, 2, 5, 3 },
        new[] { 3, 3, 2, 2 },
    };
    public static readonly int[] AloftMs = { 125, 125, 180, 160 };
    public const int AloftPool = 1;                                  // at most one in view
    public static float AloftGapMin = 16f * ZoomK, AloftGapMax = 34f * ZoomK;   // aloft-layer travel (world units) between two (a view is ~15 u tall)
    public static float AloftFirst = 9f * ZoomK;
    // own motion: NO vehicle travels (the user's rule). The tethered aerostats and the moored convoy only bob on their
    // lines (amplitude x their size, period s) and sway (degrees, period s); the jellies and rays, living creatures, also drift slowly.
    public static readonly float[] BobAmp = { .03f, .01f, .045f, .03f };
    public static readonly float[] BobPeriod = { 5.5f, 6.5f, 3.6f, 4.6f };
    public static readonly float[] SwayDeg = { 2.5f, 1f, 0f, 0f };
    public static readonly float[] SwayPeriod = { 7f, 8f, 1f, 1f };
    public const float JellyDrift = .05f * ZoomK;                          // straight DOWN with the wind only (no sideways drift)
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
            float rx = KeepX(kd, size), ry = rx * .8f;
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
            var placed = Commit(kd, x, gy, size, variant);
            if (placed == null) return false;
            int ck = StormPropsTuning.Companion(kd, propRng.NextDouble() < .5);
            if (ck >= 0 && HasGroundArt(ck) && !Beside(ck, placed) &&
                (kd != StormPropsTuning.Buoys || !Beside(ck == StormPropsTuning.Tug ? StormPropsTuning.Skimmer : StormPropsTuning.Tug, placed)))
            {
                Remove(placed);                                   // the moorings come together or not at all
                if (kind >= 0) return false;
                continue;
            }
            return true;
        }
        return false;
    }

    // a ground prop's keep-out half-width (its footprint x KeepK) -- the box it claims and every check uses
    public static float KeepX(int kind, float size) { return GroundPlanner.HalfX(StormPropsTuning.GroundRule(kind), size) * StormPropsTuning.KeepK[kind]; }

    // place a ground prop of `kind` at (x, gy): its ground claimed, its livery hashed. Null: no room.
    BackdropPiece Commit(int kd, float x, double gy, float size, int variant)
    {
        var rule = StormPropsTuning.GroundRule(kd);
        var pool = kd == StormPropsTuning.Cell ? cells : props;
        float rx = KeepX(kd, size), ry = rx * .8f;
        int foot = -1;
        if (StormPropsTuning.Claims(kd)) { foot = planner.Claim(x, gy, rx, ry); if (foot < 0) return null; }
        var p = pool.Spawn();
        if (p == null) { if (foot >= 0) planner.Release(foot); return null; }
        int n = StormPropsTuning.GroundVariants[kd];
        int v = variant >= 0 ? Mathf.Min(variant, n - 1) : StormPropsTuning.Variant(PropsSpawned, kd, n, lastGroundVariant[kd]);
        if (groundStrips[kd][v] == null) v = 0;
        lastGroundVariant[kd] = v;
        p.frames = groundStrips[kd][v];
        SetSprite(p, p.frames[0], Mathf.Min(size, StormPropsTuning.MaxSize));
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
        return p;
    }

    // a companion of `kind` beside `anchor` (left or right, its footprint edge within MoorReach of the anchor's)
    bool Beside(int kind, BackdropPiece anchor)
    {
        var pool = kind == StormPropsTuning.Cell ? cells : props;
        if (pool.ActiveCount >= pool.Capacity) return false;
        float size = StormPropsTuning.GroundSize(kind) * (1f + StormPropsTuning.SizeJitter * PRand(-1f, 1f));
        var rule = StormPropsTuning.GroundRule(kind);
        float ra = KeepX(anchor.kind, anchor.size), rb = KeepX(kind, size);
        for (int t = 0; t < 24; t++)
        {
            float side = (t & 1) == 0 ? -1f : 1f;
            float cx = anchor.x + side * (ra + rb + PRand(.02f, StormPropsTuning.MoorReach));
            double cy = anchor.gy + PRand(-1f, 1f) * (ra + rb) * .6f;
            if (cx < XMin(size) - .1f || cx > XMax(size) + .1f) continue;
            if (!planner.Free(cx, cy, rb, rb * .8f) || !planner.Accepts(rule, cx, cy, size)) continue;
            if (Commit(kind, cx, cy, size, -1) != null) return true;
        }
        return false;
    }

    void Remove(BackdropPiece p)
    {
        if (p.footprint >= 0) planner.Release(p.footprint);
        p.footprint = -1;
        Despawn(p);
    }

    // ---- keeping clear of the pinned pieces -------------------------------------------------------------------------------

    // a pinned piece's keep-out half-width (landmarks and sites: their footprint; props: x KeepK)
    public float PinnedHalf(BackdropPiece p, bool prop)
    {
        float h = p.size * Mathf.Max(p.core, GroundPlanner.MinCore);
        return prop ? h * StormPropsTuning.KeepK[p.kind] : h;
    }

    // true when a box (centre x, y; half-extents hx, hy; screen units) touches no pinned piece but `except`
    public bool ClearOfPinned(float x, float y, float hx, float hy, BackdropPiece except = null)
    {
        return ClearIn(Ground, false, x, y, hx, hy, except) && ClearIn(Sites, false, x, y, hx, hy, except) &&
               ClearIn(props, true, x, y, hx, hy, except) && ClearIn(cells, true, x, y, hx, hy, except);
    }

    bool ClearIn(BackdropPool pool, bool prop, float x, float y, float hx, float hy, BackdropPiece except)
    {
        if (pool == null) return true;
        foreach (var p in pool.items)
        {
            if (!p.active || p == except) continue;
            float h = PinnedHalf(p, prop);
            if (Mathf.Abs(p.x - x) < hx + h && Mathf.Abs(p.y - y) < hy + h * .8f) return false;
        }
        return true;
    }

    // How far (screen units) an aloft piece at screen y still slides DOWN over the ground before it leaves the view: its layer
    // scrolls faster than the pinned one (and a creature drifts down with the wind), so relative to the ground it moves straight
    // down. Taken at the slowest scroll (WorldBackdrop.ScrollVelocity(0)), where that share is largest.
    public float AloftSlide(float y, float size, float vy)
    {
        float ra = set.Spec.Rate(StormPropsTuning.AloftLayer), rg = set.Spec.Rate("ground"), v = WorldBackdrop.ScrollVelocity(0f), d = Mathf.Abs(vy);
        float share = ((ra - rg) * v + d) / Mathf.Max(1e-5f, ra * v + d);
        return Mathf.Max(0f, y + HalfH + size) * share;
    }

    // the stretch an aloft piece (x, y, half-width h) will pass over -- from where it is down by `slide` -- touches no pinned piece
    public bool StretchClear(float x, float y, float h, float slide)
    {
        return StretchIn(Ground, false, x, y, h, slide) && StretchIn(Sites, false, x, y, h, slide) &&
               StretchIn(props, true, x, y, h, slide) && StretchIn(cells, true, x, y, h, slide);
    }

    bool StretchIn(BackdropPool pool, bool prop, float x, float y, float h, float slide)
    {
        if (pool == null) return true;
        foreach (var p in pool.items)
        {
            if (!p.active) continue;
            float hp = PinnedHalf(p, prop);
            if (Mathf.Abs(p.x - x) < h + hp && p.y + hp * .8f > y - slide - h && p.y - hp * .8f < y + h) return false;
        }
        return true;
    }

    // a straight path relative to the ground (from x, y moving vx, vy u/s for `seconds`) that passes no pinned piece
    public bool PathClear(float x, float y, float vx, float vy, float seconds, float h)
    {
        float len = Mathf.Sqrt(vx * vx + vy * vy) * seconds;
        int n = Mathf.Clamp(Mathf.CeilToInt(len / Mathf.Max(.05f, h * .5f)), 1, 96);
        for (int i = 0; i <= n; i++)
        {
            float t = seconds * i / n;
            if (!ClearOfPinned(x + vx * t, y + vy * t, h, h)) return false;
        }
        return true;
    }

    // every aloft piece's column is a lane no new ground piece may be placed in
    void UpdateLanes()
    {
        int n = 0;
        if (aloft != null)
            foreach (var p in aloft.items)
            {
                if (!p.active || n >= planner.LaneX.Length) continue;
                float h = p.size * StormPropsTuning.BodyShare;
                planner.LaneX[n] = p.x; planner.LaneR[n] = h;
                planner.LaneHi[n] = planner.GroundY(p.y + h);
                planner.LaneLo[n] = planner.GroundY(p.y - AloftSlide(p.y, p.size, p.vy) - h);
                n++;
            }
        planner.Lanes = n;
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
        float size = StormPropsTuning.AloftSize(kd) * (1f + StormPropsTuning.SizeJitter * PRand(-1f, 1f));
        float vy0 = StormPropsTuning.Drifts(kd) ? -PRand(.3f, 1f) * StormPropsTuning.JellyDrift : 0f;   // creatures drift down with the wind
        float y0 = float.IsNaN(y) ? SpawnY(size) : y;
        if (float.IsNaN(x))
        {
            // a spot whose whole slide over the ground (straight down, AloftSlide) passes no pinned piece
            float h = size * StormPropsTuning.BodyShare, slide = AloftSlide(y0, size, vy0);
            bool found = false;
            for (int t = 0; t < 24 && !found; t++)
            {
                float cx = PRand(-HalfW * .62f, HalfW * .62f);
                if (StretchClear(cx, y0, h, slide)) { x = cx; found = true; }
            }
            if (!found) return false;
        }
        var p = aloft.Spawn();
        if (p == null) return false;
        int n = StormPropsTuning.AloftVariants[kd];
        int v = variant >= 0 ? Mathf.Min(variant, n - 1) : StormPropsTuning.Variant(AloftSpawned, kd + 11, n, lastAloftVariant[kd]);
        if (aloftStrips[kd][v] == null) v = 0;
        lastAloftVariant[kd] = v;
        SetSprite(p, aloftStrips[kd][v][0], size);
        p.body.localPosition = Vector3.zero;
        p.frames = aloftStrips[kd][v];
        bool mirror = propRng.NextDouble() < .5;
        p.body.localScale = mirror ? new Vector3(-1f, 1f, 1f) : Vector3.one;
        p.vx = p.vy = 0f;                                  // moored / tethered: it rides the parallax only
        p.vy = vy0;
        p.x = x;
        p.y = y0;
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
        UpdateLanes();
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
        UpdateLanes();
        propTravel += scrolled;
        if (planner.Ready && propTravel >= propGap)
        {
            double lo = planner.Travel + HalfH + StormTuning.SpawnAbove + StormPropsTuning.SpawnAhead, hi = lo + StormTuning.SpawnBand;
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
