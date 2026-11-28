using UnityEngine;

// Frost's LIVING PROPS (user, 2026-11-28: colour and life from overlay things pinned to the ground, "like abandoned science
// centers, whales popping their head up"; the ground tiles are never recoloured, nothing pink): six small animated
// drawings standing on the ground like the landmarks -- PINNED to the mid tile (same rate, same scroll, placed by the
// same GroundPlanner, so they never overlap a landmark or a dock), drawn at the landmarks' scale (FrostTuning.PieceScale)
// and graded like them (the "props" layer, Graded(1)).
//
//   whale, orca        (open water) one-shots: a faint shadow under the surface (frame 0) for a random WaitMin..WaitMax s,
//                      then they surface, blow and dive (frames 1..7) and go back to the shadow
//   icebreaker         (open water) its churning wake and funnel puff loop; lit bridge windows, a masthead strobe
//   station, lab,      (a flat top, mask O, with a clear rim, like every landmark) loop: the drawing's own blinking beacon,
//   plane               flickering window, pulsing panels -- backed by lights (Lights below) so they read at phone size
//
// ROUND 2 (props_more, user-approved 2026-11-28): nine more kinds, most in four colour / pose variants (each copy picks
// one by hash, never the previous copy's of that kind), all PINNED to the ground -- nothing flies free (the user: "it has to
// be trapped or something somewhere"):
//   snowcat, radar, sonde  a station's outfit: only beside a station or lab prop already on the ground (Host, HostReach)
//   fishcamp               shanties on sea ice by a hole: a flat top with open water / a lead just beside it
//   flares                 a landing strip with flickering flare pots: a flat top
//   tanker                 frozen into the ice: on ice with a lead beside it
//   bears, seals           on an ice edge: mostly water, with ice in the ring round them
//   narwhal                a pod surfacing in open water (one-shot, like the orcas)
//   heli, airlift          crashed / belly-landed on a snow field well away from water (no water inside FarRim)
// KEEP-OUT (the user, 2026-11-28: nothing that moves may pass through or touch a prop): a prop claims its whole drawing plus
// KeepOut of its width all round, so no landmark, dock or other prop (whales / orcas / narwhals surfacing) is placed in that
// margin; no prop stands under a landmark's rising smoke / steam column (PlumeBox) and no landmark is placed whose column
// would rise through a prop; a prop also keeps SiteKeepOut of a launch site's width clear round a dock (the elite lifts off
// from it). The floe strip (flow) is pinned to the mid tile: nothing drifts.
// RARE: an accent, not clutter. At most Pool (2) stand on the ground at once (in view or just above it), one every
// GapMin..GapMax of scrolled ground (the view is ~16.5 u tall), so one or two are in view and often none. Nothing that
// floats comes to the glacier (v4: its dark mask class is the crevasses).
// Art: Backdrop3/props (128 px cells, <name>_00..07 or <name>_<v>_00..07 for a variant, two strips a row, 2048 x 3072;
// frost_redraw/props/build_props_atlas.py from the approved strips).
// Animated from each piece's own age (scaled dt): a paused game is a still frame. Nothing is allocated per frame.
public static class FrostPropsTuning
{
    public const string Atlas = "props";
    public const string Layer = "props";
    public static readonly string[] Names = { "whale", "orca", "station", "lab", "icebreaker", "plane",
                                              "snowcat", "radar", "fishcamp", "flares", "tanker", "bears", "narwhal", "sonde", "seals", "heli", "airlift" };
    public const int Whale = 0, Orca = 1, Station = 2, Lab = 3, Icebreaker = 4, Plane = 5,
                     Snowcat = 6, Radar = 7, Fishcamp = 8, Flares = 9, Tanker = 10, Bears = 11, Narwhal = 12, Sonde = 13, Seals = 14, Heli = 15, Airlift = 16;
    // colour / pose variants per kind (1: one drawing, cells <name>_00..07; else <name>_<v>_00..07)
    public static readonly int[] Variants = { 1, 1, 1, 1, 1, 1, 4, 4, 4, 4, 4, 4, 4, 4, 1, 4, 4 };
    // the atlas strip of kind `kind`, variant `v` (BackdropAtlas.Frames)
    public static string Strip(int kind, int v) { return Variants[kind] <= 1 ? Names[kind] : Names[kind] + "_" + v; }
    public const int Frames = 8;
    public const float CellPx = 128f;

    // WHERE each may stand (Rule):
    //   OpenWater  open water (mask W), its whole footprint and a clear rim -- whale, orca, icebreaker, narwhal
    //   Flat       a flat top (mask O) with a clear rim, like a landmark -- station, lab, plane, flares
    //   Outfit     solid ice / snow (O / C), its rim mostly so, beside its Host -- snowcat, radar, sonde
    //   ByHole     a flat top with open water in the ring just round it (an ice-fishing hole / lead) -- fishcamp
    //   FrozenIn   on ice (O / C), with a lead in the ring round it -- tanker
    //   IceEdge    mostly water, with ice in the ring round it -- bears, seals
    //   SnowField  on snow (O / C) with no water anywhere inside FarRim x its footprint -- heli, airlift
    public enum Place { OpenWater, Flat, ByHole, FrozenIn, IceEdge, SnowField, Outfit }
    public static readonly Place[] Where =
    {
        Place.OpenWater, Place.OpenWater, Place.Flat, Place.Flat, Place.OpenWater, Place.Flat,
        Place.Outfit, Place.Outfit, Place.ByHole, Place.Flat, Place.FrozenIn, Place.IceEdge, Place.OpenWater, Place.Outfit, Place.IceEdge, Place.SnowField, Place.SnowField,
    };
    // stands in / at the water (never on the glacier, v4: its dark mask class is the crevasses)
    public static bool Wet(int kind) { var w = Where[kind]; return w == Place.OpenWater || w == Place.IceEdge || w == Place.ByHole || w == Place.FrozenIn; }
    // the whales, orcas and narwhals surface, blow and dive once, then wait; the rest loop
    public static readonly bool[] OneShot = { true, true, false, false, false, false, false, false, false, false, false, false, true, false, false, false, false };
    // a station's outfit (snowcat, radar, sonde): only within HostReach of a station or lab prop on the ground
    public static bool NeedsHost(int kind) { return kind == Snowcat || kind == Radar || kind == Sonde; }
    public static bool IsHost(int kind) { return kind == Station || kind == Lab; }
    // the gap between the outfit's and its host's drawings (edge to edge), as a share of the host's drawn width
    public const float HostGapMin = .05f, HostGapMax = .6f;
    // ms per frame (frost props meta.json, the approved previews). A one-shot's frame 0 is its wait (WaitMin..WaitMax), not this.
    public static readonly int[][] FrameMs =
    {
        new[] { 1400, 110, 110, 130, 160, 150, 130, 150 },
        new[] { 1200, 110, 120, 140, 160, 140, 120, 140 },
        new[] { 125, 125, 125, 125, 125, 125, 125, 125 },
        new[] { 125, 125, 125, 125, 125, 125, 125, 125 },
        new[] { 120, 120, 120, 120, 120, 120, 120, 120 },
        new[] { 125, 125, 125, 125, 125, 125, 125, 125 },
        new[] { 125, 125, 125, 125, 125, 125, 125, 125 },     // snowcat: headlights sweep
        new[] { 250, 250, 250, 250, 250, 250, 250, 250 },     // radar: a 2 s dish sweep
        new[] { 150, 150, 150, 150, 150, 150, 150, 150 },     // fishcamp: windows, stovepipe smoke
        new[] { 110, 110, 110, 110, 110, 110, 110, 110 },     // flares: pots flicker
        new[] { 125, 125, 125, 125, 125, 125, 125, 125 },     // tanker
        new[] { 180, 180, 180, 180, 180, 180, 180, 180 },     // bears: ripples round the floe
        new[] { 1200, 110, 120, 140, 160, 140, 120, 140 },    // narwhal (one-shot)
        new[] { 125, 125, 125, 125, 125, 125, 125, 125 },     // sonde
        new[] { 180, 180, 180, 180, 180, 180, 180, 180 },     // seals
        new[] { 125, 125, 125, 125, 125, 125, 125, 125 },     // heli: tail distress beacon
        new[] { 125, 125, 125, 125, 125, 125, 125, 125 },     // airlift: distress beacon, cabin light
    };
    public static float WaitMin = 3f, WaitMax = 8f;

    // Drawn width (a 128 cell) x FrostTuning.PieceScale: the approved previews' sizes on a 1080 px phone (whale 54 px,
    // orca 46, station 62, lab 58, icebreaker 58, plane 56; a 256 landmark cell is ~65 px), ~.75-1 x the landmarks' range.
    // round 2 (px on a 1080 px phone / 41.6): snowcat 60, radar 62, fishcamp 64, flares 64, tanker 68 (capped: max <= LandmarkMax),
    // bears 50, narwhal 52, sonde 66, seals 48, heli 64, airlift 68
    public static readonly float[] SizeK = { 1.30f, 1.10f, 1.49f, 1.39f, 1.39f, 1.34f,
                                             1.44f, 1.49f, 1.54f, 1.54f, 1.64f, 1.20f, 1.25f, 1.58f, 1.15f, 1.54f, 1.64f };
    public const float SizeJitter = .06f;
    public static float Size(int kind) { return SizeK[kind] * FrostTuning.PieceScale; }
    // where the slab / ripples sit (share of the drawn width from the centre, y up): measured on the strips (rows >= 70% of
    // the widest, all frames)
    public static readonly float[] Feet = { 0f, 0f, -.06f, -.08f, 0f, .02f,
                                            -.05f, -.17f, -.06f, -.02f, -.07f, 0f, 0f, 0f, 0f, -.09f, -.02f };
    // footprint half-size as a share of the drawn width: the whales' ripple rings and the icebreaker's wake fill most of the
    // cell, so the water they need is wider than a landmark's slab; the structures stand on their slab like a landmark
    public const float WaterCore = .42f, LandCore = .34f;
    // the ground a prop CLAIMS (GroundPlanner footprint) is its whole drawing, so no landmark or dock is later placed over it
    public const float ClaimCore = .5f;
    // KEEP-OUT: the margin claimed round the drawing (share of its width, each side), so nothing else is placed in it
    public const float KeepOut = .25f;
    public static float ClaimHalf(float size) { return size * (ClaimCore + KeepOut); }
    // and the clear margin kept round a launch site (share of the site's width) and round a rising plume column
    public const float SiteKeepOut = .4f, PlumeKeepOut = .1f;
    // IceEdge: the footprint share that must be water; SnowField: the ring (x footprint) that must hold no water
    public const float EdgeWater = .6f, FarRim = 1.3f, FarRimNeed = .85f;
    public static readonly int[] Weights = { 3, 2, 2, 2, 2, 2,
                                             2, 2, 2, 2, 2, 2, 2, 1, 1, 2, 2 };
    public const int Pool = 2;
    public static float GapMin = 9f, GapMax = 16f;        // scrolled ground (world units) between two props
    public const int Tries = 40;

    const GroundClass Ice = GroundClass.Open | GroundClass.Canopy;
    public static PieceRule Rule(int kind)
    {
        PieceRule r;
        switch (Where[kind])
        {
            case Place.OpenWater: r = new PieceRule(GroundClass.Water, 1f, WaterCore).WithRim(FrostTuning.RimScale, FrostTuning.RimNeed); break;
            case Place.ByHole: r = new PieceRule(GroundClass.Open, FrostTuning.LandNeed, LandCore, GroundClass.Water).WithRim(FrostTuning.RimScale, FrostTuning.RimRelaxed); break;
            case Place.FrozenIn: r = new PieceRule(Ice, FrostTuning.LandNeed, LandCore, GroundClass.Water); break;
            case Place.IceEdge: r = new PieceRule(GroundClass.Water, EdgeWater, WaterCore, Ice); break;
            case Place.SnowField: r = new PieceRule(Ice, FrostTuning.LandNeed, LandCore).WithRim(FarRim, FarRimNeed); break;
            case Place.Outfit: r = new PieceRule(Ice, FrostTuning.LandNeed, LandCore).WithRim(FrostTuning.RimScale, FrostTuning.RimRelaxed); break;
            default: r = new PieceRule(GroundClass.Open, FrostTuning.LandNeed, LandCore).WithRim(FrostTuning.RimScale, FrostTuning.RimNeed); break;
        }
        return r.WithFoot(Feet[kind]);
    }

    public static bool Fits(int kind, int variant) { return kind >= 0 && kind < Names.Length && !(Wet(kind) && FrostTuning.IsGlacier(variant)); }

    // The prop kind of a props-atlas sprite ("whale_03" -> Whale, "radar_2_05" -> Radar), -1 for none; its variant; its frame.
    public static int KindOf(string sprite)
    {
        if (string.IsNullOrEmpty(sprite)) return -1;
        for (int i = 0; i < Names.Length; i++)
        {
            int n = Names[i].Length;
            if (!sprite.StartsWith(Names[i], System.StringComparison.Ordinal) || sprite.Length <= n || sprite[n] != '_') continue;
            if (Variants[i] <= 1 && sprite.Length == n + 3) return i;
            if (Variants[i] > 1 && sprite.Length == n + 5 && sprite[n + 2] == '_' && sprite[n + 1] - '0' >= 0 && sprite[n + 1] - '0' < Variants[i]) return i;
        }
        return -1;
    }
    public static int VariantOf(string sprite)
    {
        int k = KindOf(sprite);
        return k < 0 ? -1 : Variants[k] <= 1 ? 0 : sprite[Names[k].Length + 1] - '0';
    }
    public static int FrameOf(string sprite)
    {
        int k = KindOf(sprite);
        return k < 0 ? -1 : (sprite[sprite.Length - 2] - '0') * 10 + (sprite[sprite.Length - 1] - '0');
    }

    // A one-shot's surfacing (frames 1..7) in s; a loop's cycle.
    public static float ShowSeconds(int kind)
    {
        int ms = 0;
        for (int f = OneShot[kind] ? 1 : 0; f < Frames; f++) ms += FrameMs[kind][f];
        return ms / 1000f;
    }

    // ---- LIGHTS: FrostAmbient-style emitters riding the props ----
    // The drawings' baked lamps are 1-2 px at phone size; a soft glow (FrostGroundLights' sprite, tinted) sits on each, a
    // child of the prop's body (scrolls, scales, mirrors with it). Kinds:
    //   x  red beacon, lit exactly on the frames the drawing lights it (LitMask bit per frame)
    //   w  amber window, a gentle flicker (FrostGroundLights 'w')
    //   s  white masthead strobe, one short flash every few s (FrostGroundLights 's')
    // Never lifted by the grade, alpha <= Peak, never pink (WorldCue.IsPink), nothing faster than FrostGroundLightsTuning.MaxHz.
    public struct Light
    {
        public int prop;
        public Vector2 at;        // cell pixels from the top-left of the 128 cell
        public char kind;
        public float px;          // glow width in cell pixels
        public int litMask;       // 'x': frames the drawing shows its lamp lit
        public int variants;      // bit per variant it sits on (0: every variant)
    }
    public const int MaxLights = 3;
    public static readonly Light[] Lights =
    {
        L(Station, 103, 4, 'x', 18, 0x03),       // the red mast beacon (lit frames 0-1)
        L(Station, 77, 66, 'w', 14),             // the amber windows of the hut
        L(Station, 89, 73, 'w', 14),
        L(Lab, 56, 11, 'x', 18, 0x03),           // the mast tip
        L(Plane, 33, 14, 'x', 18, 0x13),         // the red tail distress beacon (frames 0-1 and 4)
        L(Icebreaker, 48, 46, 'w', 15),          // the lit bridge windows
        L(Icebreaker, 61, 53, 'w', 15),
        L(Icebreaker, 60, 15, 's', 15),          // the masthead light
        // round 2, measured on the strips (frost_redraw/props: the blinking / flickering texels' centres and lit frames)
        L(Snowcat, 55, 61, 'w', 14),             // the two cabs' headlights
        L(Snowcat, 78, 29, 'w', 14),
        L(Radar, 78, 12, 'x', 16, 0x03),         // the aviation mast's red beacon
        L(Fishcamp, 18, 82, 'w', 13, 0, 0x3),    // shanty windows (drawing A: variants 0, 1)
        L(Fishcamp, 90, 70, 'w', 13, 0, 0x3),
        L(Fishcamp, 100, 96, 'w', 13, 0, 0x3),
        L(Fishcamp, 42, 91, 'w', 13, 0, 0xC),    // (drawing B: variants 2, 3)
        L(Fishcamp, 28, 93, 'w', 13, 0, 0xC),
        L(Fishcamp, 31, 98, 'w', 13, 0, 0xC),
        L(Flares, 100, 42, 'w', 14, 0, 0x3),     // the hut window and two flare pots (drawing A)
        L(Flares, 34, 61, 'w', 12, 0, 0x3),
        L(Flares, 63, 77, 'w', 12, 0, 0x3),
        L(Flares, 81, 31, 'w', 14, 0, 0xC),      // (drawing B)
        L(Flares, 108, 40, 'w', 12, 0, 0xC),
        L(Flares, 48, 50, 'w', 12, 0, 0xC),
        L(Tanker, 93, 18, 'x', 16, 0x03),        // the funnel-top red lamp
        L(Tanker, 81, 27, 'w', 13),              // the bridge windows
        L(Tanker, 76, 33, 'w', 13),
        L(Sonde, 106, 99, 'x', 16, 0x03, 0x3),   // the gondola's red beacon (drawing A) ...
        L(Sonde, 63, 118, 's', 14, 0, 0x3),      // ... and its white strobe
        L(Sonde, 110, 101, 'x', 16, 0x03, 0xC),  // (drawing B)
        L(Sonde, 114, 115, 's', 14, 0, 0xC),
        L(Heli, 32, 25, 'x', 16, 0x03),          // the crashed heli's tail distress beacon
        L(Airlift, 85, 12, 'x', 16, 0x03),       // the airlift's distress beacon ...
        L(Airlift, 74, 58, 'w', 13),             // ... and its cabin light
    };
    static Light L(int prop, float x, float y, char kind, float px, int mask = 0, int variants = 0)
    {
        return new Light { prop = prop, at = new Vector2(x, y), kind = kind, px = px, litMask = mask, variants = variants };
    }
    public static bool On(Light l, int variant) { return l.variants == 0 || ((l.variants >> variant) & 1) != 0; }
    // a kind with lamps (bears, narwhals, seals and the whales have none)
    public static bool HasLights(int kind) { foreach (var l in Lights) if (l.prop == kind) return true; return false; }

    // The variant copy number `n` of `kind` shows (a hash: copies of a kind spread over its variants), never `last`.
    public static int PickVariant(int kind, uint n, int last)
    {
        int k = Variants[kind];
        if (k <= 1) return 0;
        uint h = (n + 1u) * 2654435761u ^ (uint)(kind + 1) * 40503u;
        h ^= h >> 15; h *= 2246822519u; h ^= h >> 13;
        int v = (int)(h % (uint)k);
        return v == last ? (v + 1) % k : v;
    }

    public static readonly Color Red = new Color(1f, .27f, .2f, 1f);
    public const float Peak = .9f, BeaconLow = .1f, WindowLow = .5f, WindowPeak = .8f;
    public static Color ColorOf(char k) { return k == 'x' ? Red : k == 's' ? FrostGroundLightsTuning.White : FrostGroundLightsTuning.Amber; }

    // A light's alpha (before the set's fade) on frame `frame` at the prop's age `t`.
    public static float Alpha(Light l, int frame, float t, uint seed)
    {
        switch (l.kind)
        {
            case 'x': return ((l.litMask >> frame) & 1) != 0 ? Peak : BeaconLow;
            case 'w': return Mathf.Lerp(WindowLow, WindowPeak, FrostGroundLights.Level(FrostGroundLightsTuning.Kinds.IndexOf('w'), seed, t, 0f, 0f));
            default: return Peak * FrostGroundLights.Level(FrostGroundLightsTuning.Kinds.IndexOf(l.kind), seed, t, 0f, 0f);
        }
    }
}

public partial class FrostDirector
{
    BackdropPool props;
    Sprite[][][] propFrames;               // per kind, per variant: its 8 cells (null: not in the atlas)
    PieceRule[] propRules;                 // per pool slot: the rule it was placed by
    int[] propKind;                        // per pool slot
    int[] propVar;                         // per pool slot: the variant it shows
    int[] lastVar;                         // per kind: the variant its last copy showed
    float[] propSurface;                   // per pool slot, one-shots: the age its next surfacing starts
    SpriteRenderer[][] propLights;         // per pool slot: MaxLights glow renderers under its body
    int[][] propLight;                     // per pool slot, per renderer: index into FrostPropsTuning.Lights, -1 off
    uint[] propSeed;
    Texture2D propGlowTex;
    Sprite propGlow;
    float propTravel, propGap;
    int lastProp = -1;
    System.Random propRng;                 // its own stream: a prop never shifts the landmarks' or the weather's dice

    public BackdropPool Props => props;
    public int PropsSpawned { get; private set; }
    public int PropKind(int slot) { return propKind != null && slot >= 0 && slot < propKind.Length ? propKind[slot] : -1; }
    public int PropVariant(int slot) { return propVar != null && slot >= 0 && slot < propVar.Length ? propVar[slot] : -1; }
    public PieceRule PropRule(int slot) { return propRules[slot]; }
    public SpriteRenderer[] PropLightRenderers(int slot) { return propLights[slot]; }

    float PRand(float a, float b) { return a + (float)propRng.NextDouble() * (b - a); }

    void BuildProps()
    {
        propRng = new System.Random((SeedOverride > 0 ? SeedOverride : 1989) * 53 + 11);
        var art = set.Atlas(FrostPropsTuning.Atlas);
        int kinds = FrostPropsTuning.Names.Length;
        propFrames = new Sprite[kinds][][];
        lastVar = new int[kinds];
        for (int k = 0; k < kinds; k++)
        {
            lastVar[k] = -1;
            int nv = FrostPropsTuning.Variants[k];
            var all = new Sprite[nv][];
            bool any = false;
            for (int v = 0; v < nv; v++)
            {
                var f = art.Frames(FrostPropsTuning.Strip(k, v));
                all[v] = f.Length >= FrostPropsTuning.Frames ? f : null;
                any |= all[v] != null;
            }
            propFrames[k] = any ? all : null;
        }
        props = Pool(FrostPropsTuning.Layer, FrostPropsTuning.Pool);
        int n = props.items.Count;
        propRules = new PieceRule[n];
        propKind = new int[n];
        propVar = new int[n];
        propSurface = new float[n];
        propSeed = new uint[n];
        propLights = new SpriteRenderer[n][];
        propLight = new int[n][];
        propGlow = FrostGroundLights.MakeSprite(out propGlowTex);
        for (int i = 0; i < n; i++)
        {
            propKind[i] = -1;
            propLights[i] = new SpriteRenderer[FrostPropsTuning.MaxLights];
            propLight[i] = new int[FrostPropsTuning.MaxLights];
            for (int j = 0; j < FrostPropsTuning.MaxLights; j++)
            {
                var go = new GameObject("proplight" + j);
                go.transform.SetParent(props.items[i].body, false);
                var sr = go.AddComponent<SpriteRenderer>();
                sr.sprite = propGlow;
                sr.sortingOrder = props.items[i].sr.sortingOrder + 1;
                sr.enabled = false;
                propLights[i][j] = sr;
                propLight[i][j] = -1;
            }
        }
        Grade(props, FrostPropsTuning.Layer);
        propGap = PRand(FrostPropsTuning.GapMin, FrostPropsTuning.GapMax);
    }

    void TeardownProps()
    {
        if (propGlow != null) BackdropAtlas.Kill(propGlow);
        if (propGlowTex != null) BackdropAtlas.Kill(propGlowTex);
        propGlow = null;
        propGlowTex = null;
    }

    // the opening view: one prop already standing in the upper middle of the view (the landmarks are dealt around it)
    void OpeningProp()
    {
        double t = planner.Travel;
        SpawnProp(-1, t - HalfH * .15f, t + HalfH * .55f);
    }

    // The pool slot of a station / lab prop on the ground (the outfit's host), -1 none.
    int HostSlot()
    {
        if (props == null) return -1;
        for (int i = 0; i < props.items.Count; i++)
            if (props.items[i].active && propKind[i] >= 0 && FrostPropsTuning.IsHost(propKind[i])) return i;
        return -1;
    }

    bool CanPick(int i, int not, int host)
    {
        return i != lastProp && i != not && propFrames[i] != null && FrostPropsTuning.Fits(i, set.Variant) && (!FrostPropsTuning.NeedsHost(i) || host >= 0);
    }

    int PickProp(int not)
    {
        int host = HostSlot(), total = 0;
        for (int i = 0; i < FrostPropsTuning.Weights.Length; i++)
            if (CanPick(i, not, host)) total += FrostPropsTuning.Weights[i];
        if (total <= 0) return -1;
        int r = propRng.Next(total);
        for (int i = 0; i < FrostPropsTuning.Weights.Length; i++)
        {
            if (!CanPick(i, not, host)) continue;
            r -= FrostPropsTuning.Weights[i];
            if (r < 0) return i;
        }
        return -1;
    }

    // ---- KEEP-OUT ----------------------------------------------------------------------------------------------------

    // The rising plume columns (smoke / steam loops) of ground piece `p` as boxes in ground space (centre x, gy; half-sizes),
    // from FrostAmbientCatalog's emitters: a loop's 256 cell drawn at the host's size x its scale, its anchor (128, 236) on the
    // emitter, so the column spans 20 / 256 below the point to 236 / 256 above, half a cell either side. False past the last.
    public static bool PlumeBox(string piece, int index, float size, bool mirror, float x, double gy, out float cx, out double cy, out float hx, out float hy)
    {
        cx = 0f; cy = 0.0; hx = hy = 0f;
        var list = FrostAmbientCatalog.For(piece);
        if (list == null) return false;
        int seen = 0;
        for (int e = 0; e < list.Length && e < FrostAmbientCatalog.MaxPerPiece; e++)
        {
            int li = FrostAmbientCatalog.LoopIndex(list[e].loop);
            if (li < 0 || !FrostAmbientCatalog.Lifted(FrostAmbientCatalog.Loops[li].atlas)) continue;
            if (seen++ < index) continue;
            float ps = size * FrostAmbientCatalog.Loops[li].scale * list[e].scale;
            float ex = (list[e].at.x - 128f) / 256f * size * (mirror ? -1f : 1f), ey = (128f - list[e].at.y) / 256f * size;
            hx = ps * .5f * (1f + FrostPropsTuning.PlumeKeepOut);
            hy = ps * .5f * (1f + FrostPropsTuning.PlumeKeepOut);
            cx = x + ex;
            cy = gy + ey + ps * (236f - 20f) / 2f / 256f;
            return true;
        }
        return false;
    }

    static bool Hit(float ax, double ay, float ahx, float ahy, float bx, double by, float bhx, float bhy)
    {
        return Mathf.Abs(ax - bx) < ahx + bhx && System.Math.Abs(ay - by) < ahy + bhy;
    }

    // Is a prop's claimed box (centre x, gy; half-sizes) clear of every landmark's / dock's plume column and of every dock's
    // keep-out margin?
    bool PropClear(float x, double gy, float hx, float hy)
    {
        var pools = Landmarks;
        for (int pi = 0; pi < pools.Count; pi++)
            foreach (var q in pools[pi].items)
            {
                if (!q.active || q.sr.sprite == null) continue;
                bool mirror = q.body.localScale.x < 0f;
                float cx, bhx, bhy; double cy;
                for (int e = 0; PlumeBox(q.sr.sprite.name, e, q.size, mirror, q.x, q.gy, out cx, out cy, out bhx, out bhy); e++)
                    if (Hit(x, gy, hx, hy, cx, cy, bhx, bhy)) return false;
            }
        if (sites != null)
            foreach (var q in sites.items)
            {
                if (!q.active) continue;
                float m = q.size * (Mathf.Max(q.core, GroundPlanner.MinCore) + FrostPropsTuning.SiteKeepOut);
                if (Hit(x, gy, hx, hy, q.x, q.gy, m, m * .8f)) return false;
            }
        return true;
    }

    // Would landmark `piece` (size, mirrored, at x, gy) send a plume column through a prop's claimed box? (SpawnLandmark)
    bool PlumeHitsProp(string piece, float size, bool mirror, float x, double gy)
    {
        if (props == null) return false;
        float cx, hx, hy; double cy;
        for (int e = 0; PlumeBox(piece, e, size, mirror, x, gy, out cx, out cy, out hx, out hy); e++)
            foreach (var p in props.items)
            {
                if (!p.active) continue;
                float h = FrostPropsTuning.ClaimHalf(p.size);
                if (Hit(cx, cy, hx, hy, p.x, p.gy, h, h * .8f)) return true;
            }
        return false;
    }

    // Is a dock at x, gy (size) inside a prop's keep-out? (SpawnSite)
    bool SiteHitsProp(float x, double gy, float size, float core)
    {
        if (props == null) return false;
        float m = size * (Mathf.Max(core, GroundPlanner.MinCore) + FrostPropsTuning.SiteKeepOut);
        foreach (var p in props.items)
        {
            if (!p.active) continue;
            float h = FrostPropsTuning.ClaimHalf(p.size);
            if (Hit(x, gy, m, m * .8f, p.x, p.gy, h, h * .8f)) return true;
        }
        return false;
    }

    // A candidate centre for `kind`: anywhere in the band, or (the outfit) in a ring just outside its host's drawing.
    bool PropCandidate(int kind, float size, int host, double lo, double hi, out float x, out double gy)
    {
        if (!FrostPropsTuning.NeedsHost(kind))
        {
            x = PRand(XMin(size), XMax(size));
            gy = lo + propRng.NextDouble() * (hi - lo);
            return true;
        }
        var h = props.items[host];
        float a = PRand(0f, 2f * Mathf.PI);
        float reach = (h.size + size) * (FrostPropsTuning.ClaimCore + FrostPropsTuning.KeepOut) + h.size * PRand(FrostPropsTuning.HostGapMin, FrostPropsTuning.HostGapMax);
        x = h.x + Mathf.Cos(a) * reach;
        gy = h.gy + Mathf.Sin(a) * reach * .8f;
        return x >= XMin(size) && x <= XMax(size) && gy >= lo - reach && gy <= hi + reach;
    }

    // A prop (kind < 0: one picked) with its centre in the ground band [lo, hi] (the outfit: beside its host) where the ground
    // suits it, nothing stands on its whole drawing and its keep-out, and no plume column or dock margin reaches it. A kind
    // that fits nowhere is swapped for another (up to three). False: nothing placed.
    public bool SpawnProp(int kind, double lo, double hi, int tries = FrostPropsTuning.Tries)
    {
        if (props == null || !planner.Ready || props.ActiveCount >= props.Capacity) return false;
        int tried = -1;
        for (int k = 0; k < 3; k++)
        {
            int kd = kind >= 0 ? kind : PickProp(tried);
            if (kd < 0 || !FrostPropsTuning.Fits(kd, set.Variant) || propFrames[kd] == null) return false;
            tried = kd;
            int host = FrostPropsTuning.NeedsHost(kd) ? HostSlot() : -1;
            if (FrostPropsTuning.NeedsHost(kd) && host < 0) { if (kind >= 0) return false; continue; }
            float size = FrostPropsTuning.Size(kd) * (1f + FrostPropsTuning.SizeJitter * PRand(-1f, 1f));
            var rule = FrostPropsTuning.Rule(kd);
            float rx = FrostPropsTuning.ClaimHalf(size), ry = rx * .8f;
            float x = 0f; double gy = 0.0; bool found = false;
            for (int t = 0; t < tries && !found; t++)
            {
                float cx; double cy;
                if (!PropCandidate(kd, size, host, lo, hi, out cx, out cy)) continue;
                if (!planner.Free(cx, cy, rx, ry) || !planner.Accepts(rule, cx, cy, size) || !PropClear(cx, cy, rx, ry)) continue;
                x = cx; gy = cy; found = true;
            }
            if (!found) { if (kind >= 0) return false; continue; }
            int foot = planner.Claim(x, gy, rx, ry);
            if (foot < 0) return false;
            var p = props.Spawn();
            if (p == null) { planner.Release(foot); return false; }
            int i = props.items.IndexOf(p);
            int v = FrostPropsTuning.PickVariant(kd, (uint)PropsSpawned ^ propSeedBase, lastVar[kd]);
            if (propFrames[kd][v] == null) for (int w = 0; w < propFrames[kd].Length; w++) if (propFrames[kd][w] != null) { v = w; break; }
            lastVar[kd] = v;
            propRules[i] = rule;
            propKind[i] = kd;
            propVar[i] = v;
            propSeed[i] = (uint)propRng.Next() | 1u;
            propSurface[i] = FrostPropsTuning.OneShot[kd] ? PRand(FrostPropsTuning.WaitMin, FrostPropsTuning.WaitMax) : 0f;
            SetSprite(p, propFrames[kd][v][0], Mathf.Min(size, BackdropCatalog.MaxLandmarkSize));
            p.body.localScale = propRng.NextDouble() < .5 ? new Vector3(-1f, 1f, 1f) : Vector3.one;
            p.footprint = foot;
            p.core = FrostPropsTuning.ClaimCore + FrostPropsTuning.KeepOut;
            p.gy = gy;
            p.x = x;
            p.y = planner.Y(gy);
            p.vx = p.vy = 0f;
            p.rate = set.Spec.Rate(FrostPropsTuning.Layer);
            p.color = tintGround;
            p.dockTint = Color.white;
            p.kind = kd;
            Place(p);
            Tinted(p, tintGround, 1f);
            AttachPropLights(i, kd, v);
            lastProp = kd;
            PropsSpawned++;
            return true;
        }
        return false;
    }

    // the variant hash's per-run salt (the seed), so two runs do not deal the same variant sequence
    uint propSeedBase => (uint)(SeedOverride > 0 ? SeedOverride : 1989) * 2246822519u;

    // Tests / previews: a prop of `kind` around screen y (searched a little up and down).
    public bool SpawnPropAt(int kind, float y)
    {
        double g = planner.GroundY(y);
        return SpawnProp(kind, g - 1.2, g + 1.2, 400);
    }

    // Tests / previews: every prop gone, its ground released.
    public void ClearProps()
    {
        if (props == null) return;
        foreach (var p in props.items)
        {
            if (!p.active) continue;
            planner.Release(p.footprint);
            p.footprint = -1;
            Despawn(p);
        }
    }

    // Tests / previews: every whale / orca / narwhal on the ground starts its surfacing now.
    public void SurfaceNow()
    {
        if (props == null) return;
        for (int i = 0; i < props.items.Count; i++)
            if (props.items[i].active && propKind[i] >= 0 && FrostPropsTuning.OneShot[propKind[i]]) propSurface[i] = props.items[i].age;
    }

    void AttachPropLights(int slot, int kind, int variant)
    {
        var rs = propLights[slot];
        int j = 0;
        for (int l = 0; l < FrostPropsTuning.Lights.Length && j < rs.Length; l++)
        {
            var li = FrostPropsTuning.Lights[l];
            if (li.prop != kind || !FrostPropsTuning.On(li, variant)) continue;
            const float ppu = BackdropAtlas.PixelsPerUnit, half = FrostPropsTuning.CellPx * .5f;
            rs[j].transform.localPosition = new Vector3((li.at.x - half) / ppu, (half - li.at.y) / ppu, 0f);
            float s = li.px / ppu;
            rs[j].transform.localScale = new Vector3(s, s, 1f);
            propLight[slot][j] = l;
            j++;
        }
        for (; j < rs.Length; j++) { propLight[slot][j] = -1; rs[j].enabled = false; }
    }
    // The frame a prop shows at its age (a one-shot's wait / surfacing advanced here).
    int PropFrame(int slot, BackdropPiece p)
    {
        int kind = propKind[slot];
        var ms = FrostPropsTuning.FrameMs[kind];
        if (FrostPropsTuning.OneShot[kind])
        {
            if (p.age < propSurface[slot]) return 0;
            float t = (p.age - propSurface[slot]) * 1000f;
            for (int f = 1; f < FrostPropsTuning.Frames; f++)
            {
                if (t < ms[f]) return f;
                t -= ms[f];
            }
            propSurface[slot] = p.age + PRand(FrostPropsTuning.WaitMin, FrostPropsTuning.WaitMax);
            return 0;
        }
        int cycle = 0;
        for (int f = 0; f < ms.Length; f++) cycle += ms[f];
        float c = (p.age * 1000f) % cycle;
        for (int f = 0; f < ms.Length; f++)
        {
            if (c < ms[f]) return f;
            c -= ms[f];
        }
        return 0;
    }

    void StepProps(float dt, float scrolled)
    {
        if (props == null) return;
        propTravel += scrolled;
        if (planner.Ready && propTravel >= propGap && props.ActiveCount < props.Capacity)
        {
            double lo = planner.Travel + HalfH + FrostTuning.SpawnAbove, hi = lo + FrostTuning.SpawnBand;
            if (SpawnProp(-1, lo, hi)) { propTravel = 0f; propGap = PRand(FrostPropsTuning.GapMin, FrostPropsTuning.GapMax); }
            else propGap = propTravel + FrostTuning.SpawnRetry;
        }
        StepPinned(props, propRules, dt);
        float fade = set.Alpha * FrostGroundLightsTuning.Brightness;
        for (int i = 0; i < props.items.Count; i++)
        {
            var p = props.items[i];
            if (!p.active || propKind[i] < 0) continue;
            int f = PropFrame(i, p);
            var want = propFrames[propKind[i]][propVar[i]][f];
            if (p.sr.sprite != want) p.sr.sprite = want;
            var rs = propLights[i];
            for (int j = 0; j < rs.Length; j++)
            {
                int l = propLight[i][j];
                if (l < 0) continue;
                float a = FrostPropsTuning.Alpha(FrostPropsTuning.Lights[l], f, p.age, propSeed[i] * (uint)(j + 1)) * fade;
                if (a <= .004f) { if (rs[j].enabled) rs[j].enabled = false; continue; }
                Color c = FrostPropsTuning.ColorOf(FrostPropsTuning.Lights[l].kind);
                c.a = a;
                rs[j].color = c;
                if (!rs[j].enabled) rs[j].enabled = true;
            }
        }
    }
}
