using System.Collections.Generic;
using UnityEngine;

// Desert's backdrop: the ship flies HIGH over the dead strip-mined planet (r1, 2026-11-28: the user-approved .49 altitude, like
// Frost and Storm), under a thin high haze of dust, looking down on bone-gold dune seas, a lilac salt mine, a rose dusk oasis in the
// rock and the NIGHT side's purple craters -- and, under the sand, the GIANT WORMS.
//
//   GROUND     one of four stitched ground tiles per landing (BackdropVariants; v<n>/mid.png, 896 x 4096: two ground columns side by
//              side, drawn at DesertTuning.TileZoom of the view, so the ground reads at GroundZoom), quilted from the approved round-5
//              paintings with their painted features re-scattered: never recoloured (the colour and life come from the props).
//              v1 khaki dune sea, v2 lilac salt mine, v3 a golden-hour amber oasis in the rock, v4 the moonlit blue-grey NIGHT side
//              (DesertTuning.Night) -- v3 / v4 the user-approved regrades; each layout's haze and weather tint are DATA (its grade.json).
//   PROPS      the living props and the big mountains (DesertProps.cs): PINNED to the mid tile, placed where they make sense
//              (mask.txt + ctx.txt), each with a keep-out nothing moving enters. They replace the round-1 landmarks, pipes and
//              ground features, retired with their loops (smoke stacks, lamps, leaks) on 2026-11-28.
//   WORMS      giant sand worms (atlases worm / worm_giant, drawn at DesertTuning.PieceScale): a surface RIDGE trail crawls across
//              the sand with a dust wake, now and then the worm ERUPTS (emerge, a held arc, a dive, each in a spray of sand) and the
//              ridge resumes. Pinned to the ground, behind every piece, enemy and the player, alpha capped (body .85, spray .7), one
//              at a time with long calm gaps (small and medium often, a giant rare), none at a boss. A worm's whole planned path
//              keeps out of every claimed footprint (props, sites, wanderers); it never razes anything -- meeting a site or a prop
//              it dives and fades.
//   SITES      the elites' launch sites on the same pinned plane (a worm burrow, a crawler bay, a glass pad, a buried silo, a ruin
//              dock, the launch-strip bunker / hangar / silo / canyon dock), shut until the launch tell opens them.
//   WEATHER    the CEILING of dust haze, thick at the start (continuing the planetfall's dust deck), held CeilingHold s, then
//              clearing fast, gone by CeilingClearSeconds (CloudCover); then a light scattering of wisps and low mist drifting with
//              the wind, GUSTS of grit now and then, a few grit specks; dust devils, grit gusts and mirage glints wander the open
//              ground (never into a prop's keep-out). The soft painted r1 redo (backdrop_r1~/build_r1.py) under the same sprite names.
//
// Everything sits far below gameplay (BackdropCatalog.BaseOrder); the pools
// are fixed and a frame allocates nothing.
public static class DesertTuning
{
    // ---- brightness ----------------------------------------------------------
    // ROUND 5 (2026-11): the four ground sets are the owner-approved painted scenes -- v1 khaki dune sea, v2 lilac salt mine, v3 rose
    // dusk rock oasis, v4 purple night craters -- bright and colourful, props in their natural colours, so NO set is darkened or
    // graded any more: every VariantBrightness entry is 1 (it was 1 / 1.05 / 1.12 / 1 for the old dark-base art, which was lifted to its
    // tile caps). The day sets are the bright ones (mean luminance .53 / .47 / .41) and the night side is held far below them (.17):
    // DesertBackdropTest keeps v4 <= v1 and "night < .92 x day". Tile caps live in WorldBackdropTest (Desert-only, retuned to this art)
    // and enemy readability on the bright lanes in WorldBackdropTest.CheckReadability / DesertBackdropTest.Brightness.
    public static float Brightness = 1f;
    public static readonly float[] VariantBrightness = { 1f, 1f, 1f, 1f, 1f };   // index = variant (0 none)
    public static readonly bool[] Night = { false, false, false, false, true };
    public static float NightLightBoost = 1.3f;
    public static float PlumeShare = 1f;
    public static float PlumeLift = 1f;
    public static float LoopBrightness = 1f;
    public static float LightBoostNow = 1f;           // set by the director for its variant

    // ---- ALTITUDE (2026-11-28, the user-approved r1 ground) -------------------------------------------------------------------
    // The ground is seen from Frost's height: .41 of the old view (GroundZoom). The r1 tiles (896 x 4096) hold TWO .41 columns side
    // by side, so they are drawn at TileZoom (2 x GroundZoom) of the view's width (BackdropCatalog.Spec.groundZoom).
    // (2026-11-28, later: the user moved every world to .49 -- was .41; everything below follows GroundZoom.)
    // Everything that stands on the ground is keyed to GroundZoom (Spec.pieceZoom), never TileZoom: the ground layers' rates, the
    // worms, the sites, the wanderers, the cover boulders; x PieceFit (.7, as Frost) for the pieces' sizes.
    public const float GroundZoom = .49f, TileZoom = 2f * GroundZoom, PieceFit = .7f;
    public const float PieceScale = GroundZoom * PieceFit;
    public const float GroundRate = .024f * GroundZoom;               // the mid tile's (and everything pinned to it) parallax rate
    public const float FlowRate = .025f * GroundZoom, FlowDrift = .30f * GroundZoom;
    // where a new piece is looked for: a band SpawnBand tall starting SpawnAbove over the top of the view
    public static float SpawnAbove = .95f * PieceScale, SpawnBand = .9f * GroundZoom, SpawnRetry = .25f * GroundZoom;
    public static float Wind = 1f;                                        // +1: dust leans right (as painted)

    // What a site may stand on (GroundPlanner). Mask letters: W soft sand / dust, O hardpan, T / N / M zone-flagged sand / pan, C
    // BLOCKED (a painted feature: outposts, rigs, rails, bones, rocks, oases, palms, crater rims). Every rule demands its WHOLE
    // footprint on unpainted ground, so nothing the game places -- docks, wanderers, worms, props -- lands on art.
    public const GroundClass Clear = GroundClass.Water | GroundClass.Open | GroundClass.Built;                                          // (everything but a painted feature)
    static readonly PieceRule Structure = new PieceRule(Clear, 1f, .3f);
    public static readonly PieceRule DockRelaxed = new PieceRule(Clear, 1f, .3f);                                            // (any clear ground: dunes / flats)
    static readonly PieceRule Anywhere = new PieceRule(Clear, 1f, .2f);

    public static bool IsDock(string piece)
    {
        return piece != null && (piece.StartsWith("worm_burrow") || piece.StartsWith("crawler_bay") || piece.StartsWith("glass_pad") || piece.StartsWith("buried_silo") ||
               piece.StartsWith("ruin_dock") || piece.StartsWith("buriedbunker") || piece.StartsWith("crawlerhangar") || piece.StartsWith("mirrorsilo") ||
               piece.StartsWith("canyondock"));
    }

    // (the round-1 landmarks, pipes and features and their story zones retired on 2026-11-28: only the sites are placed by rule)
    public static PieceRule Rule(string piece, int variant) { return Rule(piece); }
    public static PieceRule Rule(string piece) { return IsDock(piece) ? Structure : Anywhere; }

    // ---- launch sites ----
    // (x PieceScale / x GroundZoom: the same docks, seen from the new height, as often per ground scrolled)
    public static float SiteMin = 1.5f * PieceScale, SiteMax = 1.7f * PieceScale, SiteFirst = 3f * GroundZoom, SiteGapMin = 5f * GroundZoom, SiteGapMax = 8.5f * GroundZoom;
    public static float SiteCloseDelay = .9f, SiteMinSize = 1.2f * PieceScale;
    // Entries 0-4 are the old two-drawing sites (sites atlas, kept as the elites' fallback homes); 5-8 the launch-strip ones (docks atlas:
    // <name>_00..05, DocksAtlas), for which SiteClosed / SiteOpen name the idle and the open cell (Rule picks the ground by the name).
    public static readonly string[] SiteNames = { "worm_burrow", "crawler_bay", "glass_pad", "buried_silo", "ruin_dock", "buriedbunker", "crawlerhangar", "mirrorsilo", "canyondock" };
    public static readonly string[] SiteClosed = { "worm_burrow_closed", "crawler_bay_closed", "glass_pad_closed", "buried_silo_closed", "ruin_dock_closed",
                                                   "buriedbunker_00", "crawlerhangar_00", "mirrorsilo_00", "canyondock_00" };
    public static readonly string[] SiteOpen = { "worm_burrow_open", "crawler_bay_open", "glass_pad_open", "buried_silo_open", "ruin_dock_open",
                                                 "buriedbunker_04", "crawlerhangar_04", "mirrorsilo_04", "canyondock_04" };
    public static readonly LandingKind[] SiteKinds = { LandingKind.DesertWormBurrow, LandingKind.DesertCrawlerBay, LandingKind.DesertGlassPad, LandingKind.DesertBuriedSilo, LandingKind.DesertRuinDock,
                                                       LandingKind.DesertBuriedBunker, LandingKind.DesertCrawlerHangar, LandingKind.DesertMirrorSilo, LandingKind.DesertCanyonDock };
    public static readonly int[] SiteWeights = { 2, 2, 2, 2, 3, 2, 2, 2, 2 };
    // where the elite comes out of the launch-strip sites: fractions of the drawing's bounds from its centre (y up), the pad pixel of Codex's
    // masters (new_docks_lights~/manifest.md, on 768 x 768 cells): (384,430) bunker and silo, (384,470) hangar, (360,470) canyon dock
    public static readonly Vector2[] SitePads =
    {
        new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(0f, 0f),
        new Vector2(0f, -.0599f), new Vector2(0f, -.1120f), new Vector2(0f, -.0599f), new Vector2(-.03125f, -.1120f),
    };

    // ---- the launch strips (the docks atlas): closed, lamp, opening, opening, open, closing ----
    // Codex's launch_v2 strips: 0 closed, 1 closed + a small amber lamp (never shown: the ship's own launch light is the tell), 2-3 the doors
    // opening, 4 fully open, 5 closing half shut. All six are in the atlas (build_launch_atlas.py), as <name>_00..05.
    public const string DocksAtlas = "docks";
    public const int LaunchCells = 6, LaunchIdle = 0, LaunchBlink = 1, LaunchOpening = 2, LaunchOpen = 3, LaunchHold = 4, LaunchClosing = 5;
    public const int FirstLaunchSite = 5;                      // SiteNames[FirstLaunchSite..] have a strip
    public static bool HasLaunchStrip(int kind) { return kind >= FirstLaunchSite && kind < SiteNames.Length; }
    // drawn width of a site, as a share of the usual SiteMin..SiteMax
    public static readonly float[] SiteWidthK = { 1f, 1f, 1f, 1f, 1f, 1f, 1f, 1f, 1f };
    // The tell: the site stays closed through its first share (the ship's own small blinking launch light, EliteShip.LaunchTell, is the
    // tell), then the doors open (2); the lift-off has them still opening (3 for its first share), then fully open (4, held until the elite
    // is clear); then they close for LaunchCloseSeconds (5) and the site is closed again. No flash, flare, halo, ring or lightning.
    public const float LaunchBlinkShare = .4f, LaunchOpenShare = .25f, LaunchCloseSeconds = .6f;

    // The dust of an opening strip site: Desert's own ambient smoke_b (the pale puff, DesertAmbientCatalog) played as a flipbook at the pad point
    // while the strip is in its open cells (2-4), fading in over the opening and out through the closing.
    public const string MistLoop = "smoke_b";
    public const float MistAlpha = .6f, MistFadeIn = .45f, MistWidthShare = .9f, MistRise = .42f;

    // The cell a strip site shows at EliteShip.LaunchPhase `ph` in (0, 2): (0, 1] the tell, [1, 2) the lift-off.
    public static int LaunchCellAt(float ph)
    {
        if (ph < 1f)
        {
            float t = ph * EliteShip.EngineTellSeconds;
            return t >= LaunchBlinkShare * EliteShip.EngineTellSeconds ? LaunchOpening : LaunchIdle;
        }
        return ph - 1f < LaunchOpenShare ? LaunchOpen : LaunchHold;
    }
    // the elite's size as it comes out of the pad: x the pad's drawn width, its floor x PieceScale (as Frost's and Storm's)
    public const float EmergeScalePerUnit = .22f, EmergeScaleMin = .26f * PieceScale, EmergeScaleMax = .4f;
    public const int SiteIdBase = 900;

    // ---- the giant sand worms ----
    // Sizes are the cell's width in world units (a cell is 256 px, a giant's 512): a small
    // worm is ship-sized, a medium a few ships, a giant takes most of the screen.
    public static bool WormsEnabled = true;
    public static bool RidgeOnly = false;                       // the low-quality kill switch: the trail without the eruption
    public static readonly float[] WormCell = { 1.25f * PieceScale, 2.1f * PieceScale, 4.2f * PieceScale };   // small, medium, giant (x PieceScale: seen from the new height)
    public static readonly float[] WormWeight = { 5f, 3f, 1f };               // (the giant is also rate-limited: GiantEvery)
    public static float WormFirst = 12f;                        // never in the first seconds
    public static float WormGapMin = 9f, WormGapMax = 18f;      // calm between one worm's end and the next one's start
    public static float GiantEvery = 60f;                       // a giant at most this often
    public static float WormSpeedMin = .14f * PieceScale, WormSpeedMax = .3f * PieceScale;   // units/s of ground along its heading
    public static float RidgeSecondsMin = 6f, RidgeSecondsMax = 11f;
    public static float ArcHoldMin = 2.2f, ArcHoldMax = 3.4f;
    public static float WormBodyAlpha = .85f, WormSprayAlpha = .7f;            // the art's rule: body <= 85%, spray <= 70%
    public static float WormFade = .8f;                         // s: fade in / out of the whole worm
    public static float WakeAlpha = .5f;
    // The art's emerge_00 / dive_04 (and the cascade loop) are the pointed first-pass ridge
    // (a tall pale "feather"): they read as a leaf, not a mound. The director hides the
    // hand-off behind the spray and starts the eruption on frame 1 instead (art/desert-worm
    // flagged it: the sequence handoff is abrupt).
    public static bool UseFeatherFrames = false;
    public static int WormPoolSize = 1;
    // PINNED ITEMS vs the worm: its ridge trail and eruption keep clear of every claimed footprint (sites, props and their keep-outs,
    // wanderers) by this margin (world units, on top of the footprints). A worm never razes anything (2026-11-28: the props replace
    // the razable landmarks): one that still meets a site or a prop dives and fades. Backdrop only: no collider.
    public static float WormMargin = .14f * PieceScale;

    // ---- weather (the atlas bakes its translucency in) ----
    // CLOUD COVER: the knobs (CloudCover documents the timeline). How much
    // haze drifts over the dunes once the ceiling has cleared: 1 = a light
    // scattering (default), 2 = about twice as much, 0 = none.
    public static float CloudDensity = CloudCover.Density;
    public static float CeilingHold = CloudCover.CeilingHold;                   // s at full thickness after landing
    public static float CeilingClearSeconds = CloudCover.CeilingClearSeconds;   // s: the ceiling is gone
    public static float CeilingMin = 4.2f, CeilingMax = 5.6f, CeilingGap = 1.5f, CeilingLowShare = .55f;
    public static float CeilingThicken = 4f;
    // the haze deck of the planetfall (desert_cloud_deck): the banks are painted pale grey; tint them toward its bone-gold dust
    public static float CeilingLift = 1f, CeilingSaturation = 1f;
    public static Color CeilingTint = new Color(.55f, .52f, .45f, 1f);            // v1: warm khaki dust haze over the khaki dune sea
    // The haze takes each ground's own pale dust (no pink / violet: Space's cue colours), at 70% strength. 2026-11-28 (r1): the colour is
    // DATA, read from the layout's grade.json ("haze": its pale dust, written by backdrop_r1~/build_r1.py from the tile itself), so a
    // regraded ground PNG (same size, same mask) brings its own haze with no code change. These neutral greys are only the fallback.
    public static readonly Color[] CeilingTints =
    {
        new Color(.70f, .64f, .52f, 1f), new Color(.70f, .64f, .52f, 1f), new Color(.72f, .71f, .74f, 1f),
        new Color(.68f, .66f, .62f, 1f), new Color(.40f, .40f, .42f, 1f),
    };
    [System.Serializable] class GradeFile { public float[] haze, weather; }
    // The layout's weather tint (wisps, mist, gusts, grit, dust devils): its grade.json "weather" (v3 golden afternoon, v4 moonlit
    // blue-grey), else as painted (white).
    public static Color WeatherTintOf(int variant)
    {
        var t = WorldArtPreload.Load<TextAsset>(BackdropCatalog.TileFolder("Desert", variant) + GradeFileName);
        var g = t != null ? JsonUtility.FromJson<GradeFile>(t.text) : null;
        return g != null && g.weather != null && g.weather.Length >= 3 ? new Color(g.weather[0], g.weather[1], g.weather[2], 1f) : Color.white;
    }
    // The layout's haze colour: its grade.json, else the fallback above.
    public static Color HazeOf(int variant)
    {
        var t = WorldArtPreload.Load<TextAsset>(BackdropCatalog.TileFolder("Desert", variant) + GradeFileName);
        if (t != null)
        {
            var g = JsonUtility.FromJson<GradeFile>(t.text);
            if (g != null && g.haze != null && g.haze.Length >= 3) return new Color(g.haze[0], g.haze[1], g.haze[2], 1f);
        }
        return CeilingTints[Mathf.Clamp(variant, 0, CeilingTints.Length - 1)];
    }
    public const string GradeFileName = "grade";
    public static float CeilingAlpha = .7f;      // round 5: the dust haze is a lighter veil (was full strength), so the map under it reads from the first second
    public static float WispMin = 1.9f, WispMax = 2.8f, WispAlphaMin = .3f, WispAlphaMax = .45f;
    public static float WispEveryMin = 6f, WispEveryMax = 10f, WispDrift = .18f;
    public static float MistAlphaMin = .3f, MistAlphaMax = .42f;
    public static float MistWidthMin = .5f, MistWidthMax = .7f;
    public static float MistEveryMin = 14f, MistEveryMax = 22f, MistDrift = .08f;
    public static float GustFirst = 12f, GustEveryMin = 14f, GustEveryMax = 22f;
    public static int GustSheets = 3;
    public static float GustStagger = .6f, GustSpeed = 3.4f, GustMin = 4.5f, GustMax = 6f, GustMaxAlpha = 1f;
    public static int GritCount = 6;
    public static float GritMin = .6f, GritMax = 1.1f, GritAlpha = .9f;
    // free loops wandering the ground (a dust devil over the dunes, a mirage glint over the pans, a grit gust), x GroundZoom; each claims
    // its spot (WandererKeep x its size, each side) so no prop / site is placed under it and it is never spawned into a prop's keep-out
    public static float WandererKeep = .3f;
    public static int WandererCount = 4;
    public static float WandererMin = 1.1f * GroundZoom, WandererMax = 1.7f * GroundZoom, WandererAlpha = 1f, WandererLife = 7f, WandererEveryMin = 3f, WandererEveryMax = 6.5f;
}

public partial class DesertDirector : PlanetDirector
{
    BackdropPool sites, mist, wisps, ceiling, gusts, grit, wanderers;
    BackdropAtlas siteArt, dockArt, weather;
    readonly SpriteRenderer[] mists = new SpriteRenderer[3];
    readonly float[] mistA = new float[3];
    float mistClock;
    Color haze, weatherTint;                                        // the layout's haze (DesertTuning.HazeOf: its grade.json)
    Color[] dockTints;                                 // per site kind: its DockGrade colour on this ground set
    Sprite[][] siteLaunch;                             // per site kind: its 6 launch cells (null: an old two-drawing site, or the atlas is missing)
    AmbientEmitters ambient;
    GroundPlanner planner;
    readonly Sprite[][] wandererArt = new Sprite[DesertAmbientCatalog.WandererNames.Length][];
    float wandererIn = 4f;
    Sprite[] siteClosed, siteOpen, banks, wispArt, mistArt, gustArt, gritArt;
    float siteTravel, siteGap, ceilingTravel;
    bool night;
    float mistIn = 2.5f, wispIn = 6f;
    readonly Timer gustTimer = new Timer(DesertTuning.GustEveryMin, DesertTuning.GustEveryMax, DesertTuning.GustFirst);
    int sheetsLeft;
    float sheetIn;
    readonly List<Material> mats = new List<Material>();

    // Tests: > 0 seeds the next directors' streams (statistics over many runs).
    public static int SeedOverride;
    public DesertDirector() : base(SeedOverride > 0 ? SeedOverride : 1990) { }

    public float Clock => clock;
    public bool IsNight => night;
    public int Gusts { get; private set; }
    public int SitesSpawned { get; private set; }
    public AmbientEmitters Ambient => ambient;
    public GroundPlanner Planner => planner;
    public BackdropPool Sites => sites;
    // The six launch cells of a launch-strip site kind (DesertTuning.SiteNames index), null for an old two-drawing site (tests, previews).
    public Sprite[] LaunchStrip(int kind) { return siteLaunch != null && kind >= 0 && kind < siteLaunch.Length ? siteLaunch[kind] : null; }
    public BackdropPool Ceiling => ceiling;
    public BackdropPool GustSheetPool => gusts;
    public BackdropPool Grit => grit;
    public BackdropPool Wanderers => wanderers;
    public Material CeilingMaterial { get; private set; }

    public override void Teardown()
    {
        foreach (var m in mats) BackdropAtlas.Kill(m);
        mats.Clear();
        if (ambient != null) ambient.Destroy();
    }

    // ------------------------------------------------------------------ build --

    Material Grade(BackdropPool pool, string layer, float alphaLift = 1f, float feather = 0f, float liftOverride = 0f, float sat = 0f)
    {
        float lift = liftOverride > 0f ? liftOverride : BackdropGrade.Lift(set.Spec, set.Spec.Find(layer), set.Variant);
        var m = BackdropGrade.Create(pool.name, lift, sat > 0f ? sat : BackdropGrade.Saturation(set.Spec, lift), alphaLift, feather);
        if (m == null) return null;
        mats.Add(m);
        foreach (var p in pool.items) p.sr.sharedMaterial = m;
        return m;
    }

    protected override void Build()
    {
        siteArt = set.Atlas("sites");
        dockArt = set.Atlas(DesertTuning.DocksAtlas);
        weather = set.Atlas("weather");
        night = set.Spec.Night(set.Variant);
        haze = DesertTuning.HazeOf(set.Variant);
        weatherTint = DesertTuning.WeatherTintOf(set.Variant);
        DesertTuning.LightBoostNow = night ? DesertTuning.NightLightBoost : 1f;
        ambient = new AmbientEmitters(set, DesertAmbientCatalog.Table) { Wind = DesertTuning.Wind };
        planner = new GroundPlanner(set, "mid", GroundMask.Load("Desert", set.Variant), 64);

        siteClosed = Sprites(siteArt, DesertTuning.SiteClosed);
        dockTints = DockGrade.Tints("Desert", set.Variant, DesertTuning.SiteClosed);
        siteOpen = Sprites(siteArt, DesertTuning.SiteOpen);
        // the launch-strip sites take their idle / open cells (and all six) from the docks atlas; without it they are simply never spawned
        siteLaunch = new Sprite[DesertTuning.SiteNames.Length][];
        for (int k = DesertTuning.FirstLaunchSite; k < siteLaunch.Length; k++)
        {
            var f = dockArt.Frames(DesertTuning.SiteNames[k]);
            if (f.Length < DesertTuning.LaunchCells) { siteClosed[k] = siteOpen[k] = null; continue; }
            siteLaunch[k] = f;
            siteClosed[k] = f[DesertTuning.LaunchIdle];
            siteOpen[k] = f[DesertTuning.LaunchOpen];
        }
        banks = weather.Frames("cloud_bank");
        wispArt = weather.Frames("cloud_wisp");
        mistArt = weather.Frames("mist");
        gustArt = weather.Frames("gust");
        gritArt = weather.Frames("particle");


        // the ground plane: sites (the landmarks, pipes and ground features retired on 2026-11-28; the props are DesertProps.cs)
        sites = LandmarkPool("ground", 3);
        // the launch dust: above the site, below the elite emerging from it (LandingSites order = site + 2)
        for (int i = 0; i < sites.items.Count && i < mists.Length; i++)
        {
            var mg = new GameObject("dust");
            mg.transform.SetParent(sites.items[i].body, false);
            mists[i] = mg.AddComponent<SpriteRenderer>();
            mists[i].sortingOrder = sites.items[i].sr.sortingOrder + 1;
            mists[i].enabled = false;
        }
        ambient.Rig(sites, 1);
        wanderers = Pool("ground", DesertTuning.WandererCount, false, -8);
        for (int i = 0; i < wandererArt.Length; i++)
        {
            var atlas = set.Atlas(DesertAmbientCatalog.AtlasOf(DesertAmbientCatalog.WandererNames[i]));
            wandererArt[i] = atlas.Frames(DesertAmbientCatalog.WandererNames[i]);
        }
        BuildWorms();
        BuildProps();
        mist = Pool("mist", 3);
        wisps = Pool("wisps", 4);
        ceiling = Pool("ceiling", 26);
        gusts = Pool("gusts", DesertTuning.GustSheets + 1);
        grit = Pool("grit", DesertTuning.GritCount);

        Grade(sites, "ground", 1f, 0f);
        GradeWorms();
        Grade(mist, "mist");
        Grade(wisps, "wisps");
        CeilingMaterial = Grade(ceiling, "ceiling", DesertTuning.CeilingThicken, 0f, DesertTuning.CeilingLift, DesertTuning.CeilingSaturation);

        // the opening view: the ground already dressed, under a thick ceiling
        siteGap = DesertTuning.SiteFirst;
        OpeningProps();
        BuildCeiling();
        BuildParticles();
        SpawnMist(Rand(-HalfH * .2f, HalfH * .4f));
    }

    static Sprite[] Sprites(BackdropAtlas atlas, string[] names)
    {
        var s = new Sprite[names.Length];
        for (int i = 0; i < names.Length; i++) s[i] = atlas.Get(names[i]);
        return s;
    }

    // 1 while the ceiling is thick, falling fast to 0 at CeilingClearSeconds (CloudCover.Ceiling).
    public static float CeilingDensity(float t) { return BackdropCatalog.For("Desert").CeilingDensity(t); }

    public float CeilingCover
    {
        get
        {
            float sum = 0f;
            foreach (var p in ceiling.items)
            {
                if (!p.active || !p.sr.enabled) continue;
                Bounds b = p.sr.bounds;
                b.center -= set.Root.position;
                float w = Mathf.Min(b.max.x, HalfW) - Mathf.Max(b.min.x, -HalfW);
                float h = Mathf.Min(b.max.y, HalfH) - Mathf.Max(b.min.y, -HalfH);
                if (w <= 0f || h <= 0f) continue;
                sum += w * h * p.sr.color.a;
            }
            return Mathf.Clamp01(sum / (4f * HalfW * HalfH));
        }
    }

    public float CeilingBankAlpha
    {
        get
        {
            float sum = 0f; int n = 0;
            foreach (var p in ceiling.items)
                if (p.active && p.sr.enabled && Mathf.Abs(p.y) < HalfH + p.size * .5f) { sum += p.sr.color.a; n++; }
            return n > 0 ? sum / n : 0f;
        }
    }

    // ------------------------------------------------------------------- step --

    protected override void Step(float dt, float v)
    {
        float scrolled = set.Spec.Rate("ground") * v * dt;
        siteTravel += scrolled;
        planner.Prune();
        if (planner.Ready && siteTravel >= siteGap)
        {
            if (SpawnSite(float.NaN)) { siteTravel = 0f; siteGap = Rand(DesertTuning.SiteGapMin, DesertTuning.SiteGapMax); }
            else siteGap = siteTravel + DesertTuning.SpawnRetry;
        }
        ceilingTravel += set.Spec.Rate("ceiling") * v * dt;
        float density = set.Spec.CeilingDensity(clock);
        if (ceilingTravel >= DesertTuning.CeilingGap && density > CloudCover.BankSpawnFloor)
        {
            ceilingTravel = 0f;
            SpawnBank(Rand(-HalfW * .8f, HalfW * .8f), float.NaN);
        }
        float air = set.Spec.CloudDensity();
        if (CloudCover.Tick(ref mistIn, dt, air, DesertTuning.MistEveryMin, DesertTuning.MistEveryMax, rng)) SpawnMist(float.NaN);
        if (CloudCover.Tick(ref wispIn, dt, air, DesertTuning.WispEveryMin, DesertTuning.WispEveryMax, rng)) SpawnWisp();
        if (gustTimer.Tick(dt, rng)) ForceGust();
        if (sheetsLeft > 0)
        {
            sheetIn -= dt;
            if (sheetIn <= 0f) { SpawnSheet(); sheetsLeft--; sheetIn = DesertTuning.GustStagger; }
        }

        StepProps(dt, scrolled);
        StepPinned(sites, dt);
        StepSites(dt);
        StepWanderers(dt);
        StepWorms(dt, v);
        ambient.Step(1f);

        foreach (var m in mist.items)
            if (m.active && Drift(m, dt, v)) Tinted(m, m.phase);
        foreach (var w in wisps.items)
            if (w.active && Drift(w, dt, v)) Tinted(w, w.phase);
        foreach (var b in ceiling.items)
        {
            if (!b.active) continue;
            if (density <= 0f) { Despawn(b); continue; }
            b.x += Mathf.Sign(b.x == 0f ? 1f : b.x) * CloudCover.PartSpeed * (1f - density) * dt;    // parting from the middle
            if (!Drift(b, dt, v)) continue;
            float y01 = Mathf.Clamp01((b.y / HalfH + 1f) * .5f);
            float low = Mathf.Lerp(DesertTuning.CeilingLowShare, 1f, y01 * y01 * (3f - 2f * y01));
            Color c = haze;
            c.a = b.phase * density * low * set.Alpha * DesertTuning.CeilingAlpha;
            b.sr.color = c;
        }
        foreach (var g in gusts.items)
        {
            if (!g.active || !Drift(g, dt, v)) continue;
            float k = Mathf.Clamp01(g.age / Mathf.Max(.01f, g.life));
            if (k >= 1f) { Despawn(g); continue; }
            Tinted(g, DesertTuning.GustMaxAlpha * Mathf.Sin(k * Mathf.PI));
        }
        foreach (var s in grit.items)
        {
            if (!s.active) continue;
            Recycle(s, .18f, dt, v, .35f);
            Tinted(s, DesertTuning.GritAlpha);
        }
    }

    void Tinted(BackdropPiece p, float alpha)
    {
        p.sr.color = new Color(p.dockTint.r, p.dockTint.g, p.dockTint.b, alpha * set.Alpha);
    }

    // Pinned pieces: placed from their ground coordinate every frame.
    void StepPinned(BackdropPool pool, float dt)
    {
        foreach (var p in pool.items)
        {
            if (!p.active) continue;
            p.age += dt;
            p.y = planner.Y(p.gy);
            Place(p);
            if (p.y < GroundDespawnY(p, ambient))
            {
                planner.Release(p.footprint);
                p.footprint = -1;
                Despawn(p);
                continue;
            }
            Tinted(p, p.fade);
        }
    }

    // ------------------------------------------------------------ wanderers --

    // Free loops wandering the ground: a dust devil over the dunes, a gust of grit, a mirage glint over the pans. Pinned like the
    // ground (each rides its spot), faded in and out; each claims its spot in the planner, so no prop or site is placed under it, and
    // is never spawned into a prop's keep-out. Nothing spawns while their atlases are not installed.
    void SpawnWanderer()
    {
        int kind = rng.Next(wandererArt.Length);
        var frames = wandererArt[kind];
        if (frames == null || frames.Length == 0 || !planner.Ready) return;
        string loop = DesertAmbientCatalog.WandererNames[kind];
        // a dust devil wants the soft dune sand, a mirage glint the flats; a gust goes anywhere
        GroundClass on = loop == "dustdevil" ? GroundClass.Water : loop == "mirage_glint" ? GroundClass.Open : DesertTuning.Clear;     // (never over a painted feature)
        float size = Rand(DesertTuning.WandererMin, DesertTuning.WandererMax);
        float keep = size * DesertTuning.WandererKeep;
        for (int t = 0; t < 12; t++)
        {
            float x = Rand(XMin(size), XMax(size));
            double gy = planner.Travel + HalfH + size * .5f + Rand(0f, 2f * DesertTuning.GroundZoom);
            if (planner.Match(x, gy, size * .3f, size * .25f, on) < 1f) continue;
            if (!planner.Free(x, gy, keep, keep) || TouchesProp(x, gy, keep, keep)) continue;
            int foot = planner.Claim(x, gy, keep, keep);
            if (foot < 0) return;
            var p = wanderers.Spawn();
            if (p == null) { planner.Release(foot); return; }
            p.footprint = foot;
            SetSprite(p, frames[0], size);
            p.frames = frames;
            p.fps = DesertAmbientCatalog.FpsOf(loop) * Rand(.9f, 1.1f);
            p.age = 0f;
            p.life = DesertTuning.WandererLife * Rand(.8f, 1.2f);
            p.gy = gy;
            p.x = x;
            p.y = planner.Y(gy);
            p.rate = set.Spec.Rate("ground");
            p.phase = DesertAmbientCatalog.AlphaOf(loop) * DesertTuning.WandererAlpha;
            p.kind = kind;
            p.body.localScale = Chance(.5) ? new Vector3(-1f, 1f, 1f) : Vector3.one;
            // a foot-anchored loop (the dust devil) stands on its spot, a centred one is centred on it
            p.body.localPosition = DesertAmbientCatalog.Centred(loop) ? Vector3.zero : new Vector3(0f, 1.08f, 0f);
            p.dockTint = weatherTint;
            Place(p);
            Tinted(p, 0f);
            return;
        }
    }

    // a wanderer's claimed keep-out half-size (tests)
    public static float WandererKeepOf(BackdropPiece p) { return p.size * DesertTuning.WandererKeep; }

    void StepWanderers(float dt)
    {
        wandererIn -= dt;
        if (wandererIn <= 0f) { wandererIn = Rand(DesertTuning.WandererEveryMin, DesertTuning.WandererEveryMax); SpawnWanderer(); }
        foreach (var p in wanderers.items)
        {
            if (!p.active) continue;
            p.age += dt;
            p.y = planner.Y(p.gy);
            Place(p);
            float k = p.age / Mathf.Max(.01f, p.life);
            if (k >= 1f || p.y < -HalfH - p.size) { planner.Release(p.footprint); p.footprint = -1; Despawn(p); continue; }
            p.Animate();
            Tinted(p, p.phase * Mathf.Min(1f, Mathf.Min(k * 5f, (1f - k) * 4f)));
        }
    }

    // --------------------------------------------------------------- ground --

    float XMin(float size) { return -EdgeX + size * .3f; }
    float XMax(float size) { return EdgeX - size * .3f; }

    // Draws site `name` from its atlas at x, gy (pinned), claiming its footprint.
    BackdropPiece Put(BackdropPool pool, BackdropAtlas atlas, string name, float size, float x, double gy, bool mirror)
    {
        var sprite = atlas.Get(name);
        if (sprite == null) return null;
        var rule = DesertTuning.Rule(name, set.Variant);
        float rx = GroundPlanner.HalfX(rule, size), ry = GroundPlanner.HalfY(rule, size);
        int foot = planner.Claim(x, gy, rx, ry);
        if (foot < 0) return null;
        var p = pool.Spawn();
        if (p == null) { planner.Release(foot); return null; }
        SetSprite(p, sprite, Mathf.Min(size, BackdropCatalog.MaxLandmarkSize));
        p.body.localScale = mirror ? new Vector3(-1f, 1f, 1f) : Vector3.one;
        p.footprint = foot;
        p.core = rule.core;
        p.gy = gy;
        p.x = x;
        p.y = planner.Y(gy);
        p.rate = set.Spec.Rate("ground");
        p.color = Color.white;
        p.dockTint = Color.white;
        p.kind = 0;
        Place(p);
        Tinted(p, 1f);
        ambient.Attach(p, name, rng);
        return p;
    }

    // Preview / tests: clears the ground plane (props too) and shows site `name` alone at screen x, y (pinned there), size `size`,
    // its rule ignored; nothing else spawns while it is shown.
    public BackdropPiece Showcase(string name, float size, float x, float y, bool mirror = false)
    {
        ClearWorms();
        ClearItems();
        ClearProps();
        HoldProps(1e6f);
        var p = Put(sites, siteArt.Has(name) ? siteArt : dockArt, name, size, x, planner.GroundY(y), mirror);
        siteTravel = -1000f;
        return p;
    }

    // Tests: site `name` put exactly at (x, gy), its footprint claimed (null: no room / no drawing).
    public BackdropPiece PlantSite(string name, float size, float x, double gy)
    {
        return Put(sites, siteArt.Has(name) ? siteArt : dockArt, name, size, x, gy, false);
    }

    // Tests / previews: every site and wanderer gone, footprints freed (the worms and the props stay).
    public void ClearItems()
    {
        foreach (var pool in new[] { sites, wanderers })
            foreach (var q in pool.items)
                if (q.active) { planner.Release(q.footprint); q.footprint = -1; Despawn(q); }
    }

    // ------------------------------------------------------------------ worms --

    // One size's frames (small and medium from atlas "worm", the giant from "worm_giant").
    class WormArt
    {
        public Sprite[] ridge, emerge, arc, dive, spray;
        public Vector2 anchor;           // the emergence origin in the cell, px from the top-left
        public const float FpsRidge = 10f, FpsEmerge = 12f, FpsArc = 8f, FpsDive = 12f, FpsSpray = 14f;   // the art's manifest
    }

    // BackdropPiece.tier of a worm
    public const int WormRidge = 0, WormEmerge = 1, WormArc = 2, WormDive = 3, WormRidgeOut = 4, WormFading = 5;

    readonly WormArt[] wormArt = new WormArt[3];
    BackdropPool worms;
    SpriteRenderer[] wormSpray;
    float wormIn = DesertTuning.WormFirst;
    float lastGiant = -1000f;
    public int WormsSpawned { get; private set; }
    public int Eruptions { get; private set; }
    public int GiantsSpawned { get; private set; }
    public BackdropPool Worms => worms;
    public SpriteRenderer WormSpray(BackdropPiece p) { int i = worms.items.IndexOf(p); return i >= 0 ? wormSpray[i] : null; }
    public float WormClock => clock;

    void BuildWorms()
    {
        var small = set.Atlas("worm");
        var giant = set.Atlas("worm_giant");
        string[] letters = { "s", "m", "l" };
        for (int k = 0; k < 3; k++)
        {
            var atlas = k == 2 ? giant : small;
            string t = "worm_" + letters[k] + "_";
            var a = new WormArt
            {
                ridge = atlas.Frames(t + "ridge"), emerge = atlas.Frames(t + "emerge"), arc = atlas.Frames(t + "arc"),
                dive = atlas.Frames(t + "dive"), spray = atlas.Frames(t + "spray"),
                anchor = k == 2 ? new Vector2(256f, 492f) : new Vector2(128f, 236f),
            };
            wormArt[k] = a.ridge.Length > 0 ? a : null;
        }
        // behind everything else on the ground plane (mountains, props, sites, wanderers)
        worms = Pool("ground", DesertTuning.WormPoolSize, false, -9);
        wormSpray = new SpriteRenderer[worms.items.Count];
        for (int i = 0; i < wormSpray.Length; i++)
        {
            var go = new GameObject("spray");
            go.transform.SetParent(worms.items[i].body, false);
            wormSpray[i] = go.AddComponent<SpriteRenderer>();
            wormSpray[i].sortingOrder = worms.items[i].sr.sortingOrder + 1;
            wormSpray[i].enabled = false;
        }
    }

    void GradeWorms()
    {
        Grade(worms, "ground", 1f, 0f);
        for (int i = 0; i < wormSpray.Length; i++) wormSpray[i].sharedMaterial = worms.items[i].sr.sharedMaterial;
    }

    int PickWormSize()
    {
        bool giantOk = clock >= DesertTuning.WormFirst + 13f && clock - lastGiant >= DesertTuning.GiantEvery && wormArt[2] != null;
        float total = 0f;
        for (int i = 0; i < 3; i++) if (wormArt[i] != null && (i < 2 || giantOk)) total += DesertTuning.WormWeight[i];
        if (total <= 0f) return -1;
        float r = Rand(0f, total);
        for (int i = 0; i < 3; i++)
        {
            if (wormArt[i] == null || (i == 2 && !giantOk)) continue;
            r -= DesertTuning.WormWeight[i];
            if (r <= 0f) return i;
        }
        return 0;
    }

    // (v2's salt flat has no dune sand: its worms swim the hardpan; never a painted prop)
    GroundClass WormGround { get { return GroundClass.Water | GroundClass.Open; } }   // (a zone-flagged cell is Water / Open too)

    // A worm's ridge starts crawling in the upper half of the view, on sand where the
    // whole of its path fits. `size` -1 picks one (the weights, the giant's cool-down).
    // False when the ground has no room, a worm is already about, or the art is missing.
    public bool SpawnWorm(int size = -1, float atX = float.NaN, bool eruptNow = false, float heading = float.NaN, bool ignoreItems = false)
    {
        if (worms == null || !planner.Ready || worms.ActiveCount > 0) return false;
        if (size < 0) size = PickWormSize();
        if (size < 0 || wormArt[size] == null) return false;
        var art = wormArt[size];
        float cell = DesertTuning.WormCell[size];
        float rx = cell * .3f, ry = cell * .13f;
        float k = size == 2 ? 1.2f : 1f;
        float ridge1 = eruptNow ? .6f : Rand(DesertTuning.RidgeSecondsMin, DesertTuning.RidgeSecondsMax) * k;
        for (int t = 0; t < 48; t++)
        {
            float x = float.IsNaN(atX) ? Rand(XMin(cell * .5f), XMax(cell * .5f)) : atX;
            double gy = planner.Travel + Rand(-.05f, .5f) * HalfH;
            float speed = Rand(DesertTuning.WormSpeedMin, DesertTuning.WormSpeedMax) * (size == 2 ? .6f : 1f);
            float a = float.IsNaN(heading) ? Rand(-.7f, .7f) : heading;
            float vx = speed * Mathf.Sin(a), vy = speed * Mathf.Cos(a);
            if (Mathf.Abs(x) > EdgeX * .4f) vx = -Mathf.Sign(x) * Mathf.Abs(vx);
            float x3 = x + vx * ridge1;
            if (!float.IsNaN(atX) && t == 0) { /* a forced spot: still checked below */ }
            if (Mathf.Abs(x3) > EdgeX - cell * .15f || !planner.Free(x, gy, rx, ry)) continue;
            if (!ignoreItems && !PathClear(x, gy, vx, vy, ridge1, rx, ry, cell)) continue;
            if (!PathOnGround(x, gy, vx, vy, ridge1, rx, ry, cell)) continue;
            var p = worms.Spawn();
            if (p == null) return false;
            p.kind = size;
            p.tier = WormRidge;
            p.phase = 0f;
            p.life = ridge1;
            p.age = 0f;
            p.x = x; p.gy = gy; p.y = planner.Y(gy);
            p.vx = vx; p.vy = vy;
            p.rate = set.Spec.Rate("ground");
            p.spin = 0f;
            SetSprite(p, art.ridge[0], cell);
            // the emergence origin (bottom centre) sits on the worm's spot: the sprite is offset from it
            Sprite s0 = art.ridge[0];
            float ppu = s0.pixelsPerUnit;
            float ak = ppu / BackdropAtlas.PixelsPerUnit;     // the anchor is in the authored cell's px; the sheet may be imported smaller
            p.body.localPosition = new Vector3((s0.rect.width * .5f - art.anchor.x * ak) / ppu, (art.anchor.y * ak - s0.rect.height * .5f) / ppu, 0f);
            p.body.localScale = Vector3.one;      // never mirrored: the art is lit from the upper left
            var spray = wormSpray[worms.items.IndexOf(p)];
            spray.enabled = false;
            Place(p);
            Tinted(p, 0f);
            WormsSpawned++;
            if (size == 2) { GiantsSpawned++; lastGiant = clock; }
            return true;
        }
        return false;
    }

    public void ClearWorms()
    {
        if (worms == null) return;
        foreach (var p in worms.items) if (p.active) { Despawn(p); wormSpray[worms.items.IndexOf(p)].enabled = false; }
    }

    // ---- pinned items vs the worm ----------------------------------------------------

    public int EruptionsSkipped { get; private set; }
    public System.Action<BackdropPiece> EruptionHook;      // tests: a worm is about to erupt

    // the worm's footprint on the ground: its ridge, and the taller one of its eruption
    public static void WormFoot(int size, bool erupting, out float rx, out float ry)
    {
        float cell = DesertTuning.WormCell[size];
        rx = cell * .3f;
        ry = cell * (erupting ? .2f : .13f);
    }

    public static bool Overlaps(BackdropPiece item, float x, double gy, float rx, float ry, float margin)
    {
        float irx = item.size * Mathf.Max(item.core, GroundPlanner.MinCore), iry = irx * .8f;
        return Mathf.Abs(item.x - x) < irx + rx + margin && System.Math.Abs(item.gy - gy) < iry + ry + margin;
    }

    // The whole planned life of a worm (ridge, eruption, the ridge after it) clear of every claimed footprint.
    bool PathClear(float x, double gy, float vx, float vy, float ridgeSeconds, float rx, float ry, float cell)
    {
        float m = DesertTuning.WormMargin;
        float ex = rx, ey = cell * .2f;
        float total = ridgeSeconds + 1.2f + 4.3f;
        for (float t = 0f; t <= total; t += .25f)
        {
            // the ridge runs at full speed, the eruption (a second or so of arc) at a quarter of it
            float tt = t <= ridgeSeconds ? t : ridgeSeconds + Mathf.Min(1.2f, t - ridgeSeconds) * .25f + Mathf.Max(0f, t - ridgeSeconds - 1.2f);
            float px = x + vx * tt; double pgy = gy + vy * tt;
            bool erupt = Mathf.Abs(t - ridgeSeconds) < .6f;
            if (!planner.Free(px, pgy, (erupt ? ex : rx) + m, (erupt ? ey : ry) + m)) return false;
        }
        return true;
    }

    // A worm's planned life on ground with no painted prop under its key points: where it starts, its SURFACING SPOT (the eruption; its body,
    // half the footprint the spray and razing use: a giant's whole 13-cell spray cannot be kept off a painted tile) and where its trail ends.
    // (The trail between them is a faint dust line that may cross a bit of rubble; the points that read as the worm are kept clear.)
    bool PathOnGround(float x, double gy, float vx, float vy, float ridgeSeconds, float rx, float ry, float cell)
    {
        float ey = cell * .2f;
        float total = ridgeSeconds + 1.2f + 4.3f;
        float tEnd = ridgeSeconds + Mathf.Min(1.2f, 1.2f) * .25f + (total - ridgeSeconds - 1.2f);
        return planner.Match(x, gy, rx * .4f, ry * .6f, WormGround) >= 1f &&
               planner.Match(x + vx * tEnd, gy + vy * tEnd, rx * .4f, ry * .6f, WormGround) >= 1f &&
               planner.Match(x + vx * ridgeSeconds, gy + vy * ridgeSeconds, rx * .5f, ey * .6f, WormGround) >= 1f;
    }

    // A worm's ridge meets a site or a prop (it cannot, by plan: belt and braces): it dives and fades -- nothing is ever razed.
    void WormContacts(BackdropPiece worm, int size)
    {
        float rx, ry;
        WormFoot(size, false, out rx, out ry);
        if (worm.tier == WormFading) return;
        bool hit = TouchesProp(worm.x, worm.gy, rx, ry);
        if (!hit)
            foreach (var q in sites.items)
                if (q.active && Overlaps(q, worm.x, worm.gy, rx, ry, 0f)) { hit = true; break; }
        if (!hit) return;
        if (TouchesProp(worm.x, worm.gy, rx, ry)) WormsStoppedByProp++;
        worm.tier = WormFading; worm.phase = 0f;
    }

    // never surface under a pinned item: one in the spot cancels the eruption
    bool EruptionBlocked(BackdropPiece p)
    {
        float ex, ey;
        WormFoot(p.kind, true, out ex, out ey);
        if (TouchesProp(p.x, p.gy, ex, ey)) return true;
        foreach (var q in sites.items)
            if (q.active && Overlaps(q, p.x, p.gy, ex, ey, 0f)) return true;
        return false;
    }

    void StepWorms(float dt, float v)
    {
        if (worms == null) return;
        if (DesertTuning.WormsEnabled && worms.ActiveCount == 0 && planner.Ready)
        {
            wormIn -= dt;
            if (wormIn <= 0f)
            {
                if (BossEncounter.Running || !SpawnWorm()) wormIn = BossEncounter.Running ? 3f : 2.5f;
                else wormIn = 1e9f;       // re-armed when this one has gone
            }
        }
        for (int i = 0; i < worms.items.Count; i++)
        {
            var p = worms.items[i];
            if (!p.active) continue;
            var art = wormArt[p.kind];
            var spray = wormSpray[i];
            p.age += dt;
            p.phase += dt;
            float travelK = p.tier == WormRidge || p.tier == WormRidgeOut || p.tier == WormFading ? 1f : .25f;
            p.x += p.vx * travelK * dt;
            p.gy += p.vy * travelK * dt;
            p.y = planner.Y(p.gy);
            Place(p);
            bool sprayOn = false;
            int sprayFrame = 0;
            switch (p.tier)
            {
                case WormRidge:
                case WormRidgeOut:
                    p.sr.sprite = art.ridge[(int)(p.age * WormArt.FpsRidge) % art.ridge.Length];
                    if (p.tier == WormRidge || p.tier == WormRidgeOut) WormContacts(p, p.kind);
                    if (p.tier == WormFading) break;
                    if (p.phase >= p.life)
                    {
                        if (p.tier == WormRidge && !DesertTuning.RidgeOnly && art.arc.Length > 0 && art.emerge.Length > 0)
                        {
                            bool blocked = EruptionBlocked(p);
                            if (blocked) { EruptionsSkipped++; p.tier = WormFading; p.phase = 0f; }
                            else { p.tier = WormEmerge; p.phase = 0f; Eruptions++; if (EruptionHook != null) EruptionHook(p); }
                        }
                        else { p.tier = WormFading; p.phase = 0f; }
                    }
                    break;
                case WormEmerge:
                {
                    int first = DesertTuning.UseFeatherFrames ? 0 : Mathf.Min(1, art.emerge.Length - 1);
                    int n = art.emerge.Length - first;
                    int f = Mathf.Min((int)(p.phase * WormArt.FpsEmerge), n - 1);
                    p.sr.sprite = art.emerge[first + f];
                    sprayFrame = (int)(p.phase * WormArt.FpsSpray);
                    sprayOn = sprayFrame < art.spray.Length;
                    if (p.phase * WormArt.FpsEmerge >= n)
                    {
                        p.tier = WormArc; p.phase = 0f;
                        p.life = Rand(DesertTuning.ArcHoldMin, DesertTuning.ArcHoldMax) * (p.kind == 2 ? 1.3f : 1f);
                    }
                    break;
                }
                case WormArc:
                    p.sr.sprite = art.arc[(int)(p.phase * WormArt.FpsArc) % art.arc.Length];
                    if (p.phase >= p.life) { p.tier = art.dive.Length > 0 ? WormDive : WormRidgeOut; p.phase = 0f; p.life = Rand(2.5f, 4.2f); }
                    break;
                case WormDive:
                {
                    int n = art.dive.Length - (DesertTuning.UseFeatherFrames ? 0 : Mathf.Min(1, art.dive.Length - 1));
                    int f = Mathf.Min((int)(p.phase * WormArt.FpsDive), n - 1);
                    p.sr.sprite = art.dive[f];
                    sprayFrame = (int)(p.phase * WormArt.FpsSpray);
                    sprayOn = sprayFrame < art.spray.Length;
                    if (p.phase * WormArt.FpsDive >= n)
                    {
                        // back under the sand: the ridge trail resumes from the same spot
                        p.tier = WormRidgeOut; p.phase = 0f; p.age = 0f;
                        p.age = 1000f;       // (fade-in is over)
                    }
                    break;
                }
            }
            float a = DesertTuning.WormBodyAlpha * Mathf.Min(1f, p.age / DesertTuning.WormFade);
            if (p.tier == WormFading)
            {
                p.sr.sprite = art.ridge[(int)(p.age * WormArt.FpsRidge) % art.ridge.Length];
                a *= Mathf.Clamp01(1f - p.phase / DesertTuning.WormFade);
                if (p.phase >= DesertTuning.WormFade) { Despawn(p); spray.enabled = false; wormIn = Rand(DesertTuning.WormGapMin, DesertTuning.WormGapMax); continue; }
            }
            if (p.y < -HalfH - p.size * .7f) { Despawn(p); spray.enabled = false; wormIn = Rand(DesertTuning.WormGapMin, DesertTuning.WormGapMax); continue; }
            p.sr.color = new Color(1f, 1f, 1f, a * set.Alpha);
            spray.enabled = sprayOn;
            if (sprayOn)
            {
                spray.sprite = art.spray[sprayFrame];
                spray.color = new Color(1f, 1f, 1f, DesertTuning.WormSprayAlpha * set.Alpha);
            }
        }
    }

    // ----------------------------------------------------------- launch sites --

    // A launch site entering at the top (or at screen `y`), where its rule fits.
    public bool SpawnSite(float y, int kind = -1)
    {
        if (!planner.Ready) return false;
        if (DockGrade.AnyOnGround(sites, HalfH)) return false;      // one dock at a time: the next comes once this one has scrolled off
        if (kind < 0)
        {
            int total = 0;
            foreach (int w in DesertTuning.SiteWeights) total += w;
            int r = rng.Next(total);
            for (kind = 0; kind < DesertTuning.SiteWeights.Length - 1; kind++)
            {
                r -= DesertTuning.SiteWeights[kind];
                if (r < 0) break;
            }
        }
        var closed = siteClosed[kind];
        if (closed == null || siteOpen[kind] == null) return false;
        float size = Rand(DesertTuning.SiteMin, DesertTuning.SiteMax) * DesertTuning.SiteWidthK[kind];
        double lo = float.IsNaN(y) ? planner.Travel + HalfH + DesertTuning.SpawnAbove : planner.GroundY(y) - .3f;
        double hi = lo + (float.IsNaN(y) ? DesertTuning.SpawnBand : .6f);
        float x; double gy;
        if (!planner.Find(DesertTuning.Rule(DesertTuning.SiteClosed[kind]), size, XMin(size), XMax(size), lo, hi, rng, out x, out gy) &&
            !planner.Find(DesertTuning.DockRelaxed, size, XMin(size), XMax(size), lo, hi, rng, out x, out gy) &&
            (DockGrade.Strict || !planner.Find(new PieceRule(GroundClass.Any, 0f), size, XMin(size), XMax(size), lo, hi, rng, out x, out gy))) return false;   // sand / flats / built ground, never the cliffs
        var p = Put(sites, siteLaunch[kind] != null ? dockArt : siteArt, DesertTuning.SiteClosed[kind], size, x, gy, false);   // (doors face the viewer: never mirrored)
        if (p == null) return false;
        p.dockTint = dockTints[kind];
        Tinted(p, 1f);
        p.kind = kind;
        p.tier = 0;
        p.life = 0f;
        SitesSpawned++;
        return true;
    }

    static int LaunchState(BackdropPiece p)
    {
        var live = EliteShip.Live;
        for (int i = 0; i < live.Count; i++)
        {
            var e = live[i];
            if (e == null || e.Site.anchor != p.root) continue;
            if (e.State == EliteState.LiftOff) return 2;
            if (e.State == EliteState.Parked && e.StateTime >= e.ParkSeconds - EliteShip.EngineTellSeconds) return 1;
        }
        return 0;
    }

    // The launch phase of the elite (if any) parked in / leaving site piece `p` (EliteShip.LaunchPhase), -1 with none.
    static float PhaseOf(BackdropPiece p)
    {
        var live = EliteShip.Live;
        for (int i = 0; i < live.Count; i++)
        {
            var e = live[i];
            if (e != null && e.State != EliteState.Dead && e.Site.anchor == p.root) return e.LaunchPhase;
        }
        return -1f;
    }

    // The cell of a strip site: the launch tell and lift-off by the elite's phase, then closing behind it for the last
    // LaunchCloseSeconds of the close delay (p.tier 3, p.life counting down), held open before that.
    int LaunchCell(BackdropPiece p)
    {
        if (p.tier == 1 || p.tier == 2)
        {
            float ph = PhaseOf(p);
            if (ph > 0f && ph < 2f) return DesertTuning.LaunchCellAt(ph);
            return p.tier == 1 ? DesertTuning.LaunchOpening : DesertTuning.LaunchHold;
        }
        if (p.tier == 3) return p.life > DesertTuning.LaunchCloseSeconds ? DesertTuning.LaunchHold : DesertTuning.LaunchClosing;
        return DesertTuning.LaunchIdle;
    }

    // Is the launch dust of site slot `i` showing (tests)?
    public bool MistShowing(int i) { return i >= 0 && i < mists.Length && mists[i] != null && mists[i].enabled; }
    public SpriteRenderer MistRenderer(int i) { return i >= 0 && i < mists.Length ? mists[i] : null; }
    public float MistFade(int i) { return i >= 0 && i < mistA.Length ? mistA[i] : 0f; }

    // The dust of strip site `p` (slot i) in strip cell `cell`: Desert's ambient smoke_b flipbook (DesertAmbientCatalog: its own fps, a foot-anchored
    // puff) over the pad point, as wide as the bay, rising out of it.
    void StepMist(int i, BackdropPiece p, int cell, float dt)
    {
        if (i >= mists.Length) return;
        var sr = mists[i];
        if (sr == null) return;
        bool open = cell >= DesertTuning.LaunchOpening && cell < DesertTuning.LaunchClosing;
        float target = open ? 1f : 0f;
        mistA[i] = Mathf.MoveTowards(mistA[i], target, dt / (open ? DesertTuning.MistFadeIn : DesertTuning.LaunchCloseSeconds));
        var art = ambient != null ? ambient.Frames(DesertTuning.MistLoop) : null;
        if (art == null || art.Length == 0 || mistA[i] <= .001f) { sr.enabled = false; return; }
        var f = art[((int)(mistClock * DesertAmbientCatalog.FpsOf(DesertTuning.MistLoop)) + i) % art.Length];
        if (f == null) { sr.enabled = false; return; }
        sr.sprite = f;
        if (ambient.PlumeMaterial != null) sr.sharedMaterial = ambient.PlumeMaterial;   // (the dust wears the ambient plumes' own grade)
        Bounds b = p.sr.sprite.bounds;
        Vector3 pad = p.root.TransformPoint(new Vector3(b.center.x + DesertTuning.SitePads[p.kind].x * b.size.x, b.center.y + DesertTuning.SitePads[p.kind].y * b.size.y, 0f));
        float siteW = b.size.x * p.root.lossyScale.x;
        float world = siteW * DesertTuning.MistWidthShare / Mathf.Max(.01f, f.bounds.size.x);   // world scale of the puff sprite
        var t = sr.transform;
        float k = Mathf.Max(.0001f, t.parent != null ? t.parent.lossyScale.x : 1f);
        // the puff rises out of the bay: its foot (low in its cell) on the pad point
        t.position = new Vector3(pad.x, pad.y + f.bounds.size.y * world * DesertTuning.MistRise, t.position.z);
        t.localScale = new Vector3(world / k, world / k, 1f);
        sr.color = new Color(1f, 1f, 1f, DesertTuning.MistAlpha * mistA[i] * set.Alpha);
        sr.enabled = true;
    }

    void StepSites(float dt)
    {
        mistClock += dt;
        for (int i = 0; i < sites.items.Count; i++)
        {
            var p = sites.items[i];
            if (!p.active)
            {
                if (i < mists.Length && mists[i] != null) { mists[i].enabled = false; mistA[i] = 0f; }
                continue;
            }
            int launch = LaunchState(p);
            if (launch > 0) { p.tier = launch; p.life = DesertTuning.SiteCloseDelay; }
            else if (p.tier > 0)
            {
                p.life -= dt;
                p.tier = p.life <= 0f ? 0 : 3;
            }
            bool open = p.tier > 0;
            var strip = siteLaunch[p.kind];
            var want = strip != null ? strip[LaunchCell(p)] : open ? siteOpen[p.kind] : siteClosed[p.kind];
            if (p.sr.sprite != want) p.sr.sprite = want;
            if (strip != null) StepMist(i, p, LaunchCell(p), dt);
        }
    }

    public bool SiteOpen(BackdropPiece p) { return p.tier > 0; }

    public override void LandingSites(List<LandingSite> into)
    {
        if (sites == null) return;
        for (int i = 0; i < sites.items.Count; i++)
        {
            var p = sites.items[i];
            if (!p.active || p.sr.sprite == null) continue;
            if (p.size < DesertTuning.SiteMinSize) continue;
            if (p.y < -HalfH * .15f || p.y > HalfH - p.size * .3f) continue;
            if (Mathf.Abs(p.x) > HalfW - .25f) continue;
            Bounds b = p.sr.sprite.bounds;
            into.Add(new LandingSite
            {
                anchor = p.root,
                local = new Vector3(b.center.x + DesertTuning.SitePads[p.kind].x * b.size.x, b.center.y + DesertTuning.SitePads[p.kind].y * b.size.y, 0f),   // the cell centre (the strip sites: their pad pixel)
                scale = Mathf.Clamp(p.size * DesertTuning.EmergeScalePerUnit, DesertTuning.EmergeScaleMin, DesertTuning.EmergeScaleMax),
                order = p.sr.sortingOrder + 2,
                id = DesertTuning.SiteIdBase + i,
                kind = DesertTuning.SiteKinds[p.kind],
                emerge = true,
            });
        }
    }

    // The elite director wants a site of one of `launchFrom`'s kinds in view soon (LandingSites.BringHome), as Frost's, Ember's and Tide's: the ground
    // is fixed, so none is conjured -- but the site spawner is asked for one of that kind at the top of the view now (it scrolls in over the
    // next seconds, as the ground pieces always do), unless one of them is already in view or on its way. False when none of the kinds is
    // one of Desert's sites or there is no room for one on the ground.
    public override bool BringHome(string launchFrom)
    {
        if (sites == null || !planner.Ready || string.IsNullOrEmpty(launchFrom)) return false;
        var want = new List<int>(DesertTuning.SiteKinds.Length);   // (a rare call: one per elite)
        bool mine = false;      // (one of the kinds it names is a dock of this world, even if none can be placed here)
        foreach (string part in launchFrom.Split(','))
        {
            var k = LandingSite.KindOf(part.Trim());
            if (k == null) continue;
            for (int t = 0; t < DesertTuning.SiteKinds.Length; t++)
            {
                if (DesertTuning.SiteKinds[t] == k.Value) mine = true;
                if (DesertTuning.SiteKinds[t] == k.Value && siteClosed[t] != null && siteOpen[t] != null && !want.Contains(t)) want.Add(t);
            }
        }
        if (want.Count == 0 && mine && DockGrade.Strict)     // (none of the kinds it names has a dock here: any dock will do)
            for (int t = 0; t < DesertTuning.SiteKinds.Length; t++)
                if (siteClosed[t] != null && siteOpen[t] != null) want.Add(t);
        if (want.Count == 0) return false;
        for (int i = 0; i < sites.items.Count; i++)
        {
            var p = sites.items[i];
            if (p.active && (DockGrade.Strict || want.Contains(p.kind)) && p.y >= -HalfH * .15f) return true;   // (a dock in view, or still on its way in from the top: the one dock)
        }
        return SpawnSite(float.NaN, want[rng.Next(want.Count)]);
    }

    // --------------------------------------------------------------- weather --

    void BuildCeiling()
    {
        if (banks.Length == 0) return;
        float[] rows = { 1.05f, .8f, .55f, .3f, .05f, -.25f, -.55f };
        for (int r = 0; r < rows.Length; r++)
            for (int c = 0; c < 3; c++)
                SpawnBank(HalfW * (-.62f + .62f * c + Rand(-.12f, .12f) + (r % 2 == 0 ? 0f : .2f)), HalfH * rows[r] + Rand(-.3f, .3f));
    }

    void SpawnBank(float x, float y)
    {
        if (banks.Length == 0) return;
        var b = ceiling.Spawn();
        if (b == null) return;
        SetSprite(b, Pick(banks), Rand(DesertTuning.CeilingMin, DesertTuning.CeilingMax));
        if (Chance(.5)) b.body.localScale = new Vector3(-1f, 1f, 1f);
        b.x = x;
        b.y = float.IsNaN(y) ? HalfH + b.size * .35f : y;
        b.vx = Mathf.Sign(x == 0f ? 1f : x) * Rand(.04f, .14f);
        b.vy = Rand(0f, .15f);
        b.rate = set.Spec.Rate("ceiling");
        b.phase = Rand(.8f, 1f);
        Place(b);
        Tinted(b, 0f);
    }

    void SpawnWisp()
    {
        if (wispArt.Length == 0) return;
        var w = wisps.Spawn();
        if (w == null) return;
        SetSprite(w, Pick(wispArt), Rand(DesertTuning.WispMin, DesertTuning.WispMax));
        if (Chance(.5)) w.body.localScale = new Vector3(-1f, 1f, 1f);
        w.x = Rand(-HalfW * .7f, HalfW * .7f);
        w.y = SpawnY(w.size);
        w.vx = DesertTuning.Wind * DesertTuning.WispDrift * Rand(.7f, 1.3f);
        w.rate = set.Spec.Rate("wisps");
        w.phase = Rand(DesertTuning.WispAlphaMin, DesertTuning.WispAlphaMax) * CloudCover.Alpha(set.Spec.CloudDensity());
        w.dockTint = weatherTint;
        Place(w);
        Tinted(w, 0f);
    }

    void SpawnMist(float y)
    {
        if (mistArt.Length == 0) return;
        var m = mist.Spawn();
        if (m == null) return;
        SetSprite(m, Pick(mistArt), HalfW * 2f * Rand(DesertTuning.MistWidthMin, DesertTuning.MistWidthMax));
        if (Chance(.5)) m.body.localScale = new Vector3(-1f, 1f, 1f);
        m.x = Rand(-HalfW * .3f, HalfW * .3f);
        m.y = float.IsNaN(y) ? SpawnY(m.size * .5f) : y;
        m.vx = DesertTuning.Wind * DesertTuning.MistDrift * Rand(.6f, 1.4f);
        m.rate = set.Spec.Rate("mist");
        m.phase = Rand(DesertTuning.MistAlphaMin, DesertTuning.MistAlphaMax) * CloudCover.Alpha(set.Spec.CloudDensity());
        m.dockTint = weatherTint;
        Place(m);
        Tinted(m, 0f);
    }

    public void ForceGust() { sheetsLeft = DesertTuning.GustSheets; sheetIn = 0f; Gusts++; }

    // One ash gust sheet: in from the upper left (with the wind), sweeping
    // across, faded in and out over its crossing.
    void SpawnSheet()
    {
        if (gustArt.Length == 0) return;
        var g = gusts.Spawn();
        if (g == null) return;
        SetSprite(g, Pick(gustArt), Rand(DesertTuning.GustMin, DesertTuning.GustMax));
        float dir = DesertTuning.Wind >= 0f ? 1f : -1f;
        g.x = -dir * (HalfW + g.size * .25f);
        g.y = Rand(HalfH * .1f, HalfH * .9f);
        float speed = DesertTuning.GustSpeed * Rand(.85f, 1.15f);
        g.vx = dir * speed;
        g.vy = -speed * .35f;
        g.life = (2f * HalfW + g.size * .6f) / speed;
        g.rate = set.Spec.Rate("gusts");
        g.dockTint = weatherTint;
        Place(g);
        Tinted(g, 0f);
    }

    void BuildParticles()
    {
        foreach (var s in grit.items)
        {
            if (gritArt.Length == 0) break;
            s.Show(true);
            SetSprite(s, Pick(gritArt), Rand(DesertTuning.GritMin, DesertTuning.GritMax));
            s.dockTint = weatherTint;
            s.x = Rand(-HalfW, HalfW);
            s.y = Rand(-HalfH, HalfH);
            s.phase = Rand(0f, 6.28f);
            s.rate = set.Spec.Rate("grit");
            Place(s);
        }
    }
}
