using System.Collections.Generic;
using UnityEngine;

// Desert's backdrop: the ship flies at atmosphere level over the dead strip-mined
// planet, under a thin high haze of dust, looking down on bone-gold dune seas, a dry
// sandstone canyon of ancient ruins, a cracked salt pan of toppled solar arrays and
// the NIGHT side's cold moon-lit dunes -- and, under the sand, the GIANT WORMS.
//
//   GROUND     one of four tile sets per landing (BackdropVariants): v1 the day
//              dunes (barchans, long wind ripples), v2 the canyon and ruins (layered
//              sandstone mesas, fallen arches and columns, small oases, a pale winding floor), v3 the salt flat (a cracked
//              pan, wreckage lines, dead mirror fields), v4 the NIGHT side
//              (DesertTuning.Night: drawn darker, work lamps and beacons emphasised).
//   PIECES     landmarks (buried refinery hulls, ribcage gantries, a drill head,
//              solar-array skeletons, crawler caravans, a radio mast, a collapsed dome
//              cluster, a bucket-wheel excavator, a sunken cargo crawler, a fused-glass
//              crater rim, a cliff arch, a pump-well station), pipes (straights,
//              bends, manifolds, bridges, a pump house, a half-buried pipe) and ground
//              features (rock fins, wall drifts, fulgurite glass, dry wells, machine
//              ribs, heat vents, a crack scar, a dune ridge, wreckage, a lone crawler),
//              PINNED to the mid tile and placed where its affinity mask says they
//              belong (GroundPlanner, DesertTuning.Rule; mask.txt letters: W dune
//              sand, B built, O hardpan, C cliff). Grown as CLUSTERS
//              (DesertDirector.SpawnCluster): a ruin with a pipe run to a neighbour;
//              a well / vent front with rock and ribs beside it; a dune scene (a
//              ridge, a drift, a caravan); lone pieces -- then a long calm gap.
//              Their loops (stack smoke, vent heat, beacons, glints) sit on MEASURED
//              points (DesertAmbientCatalog, points.json): smoke exactly over stack
//              mouths, all leaning with the one level wind.
//   WORMS      giant sand worms (DesertDirector worm emitters, atlases worm /
//              worm_giant): a surface RIDGE trail crawls across the dune sand with a
//              dust wake, now and then the worm ERUPTS (emerge, a held arc, a dive,
//              each in a spray of sand) and the ridge resumes. Pinned to the ground
//              (they ride it, never float), always behind pieces, enemies and the
//              player, alpha capped (body .85, spray .7), one at a time with long
//              calm gaps (small and medium often, a giant rare), none at a boss.
//   SITES      the elites' launch sites on the same pinned plane (a worm burrow, a
//              crawler bay, a glass pad, a buried silo, a ruin dock), shut until the
//              launch tell opens them.
//   WEATHER    the CEILING of dust haze, thick at the start (continuing the
//              planetfall's dust deck), held CeilingHold s, then clearing fast,
//              gone by CeilingClearSeconds (CloudCover); then only a light scattering
//              of wisps and low mist drifting with the wind, GUSTS of grit now and
//              then, a few grit specks, and a pall over each heat vent.
//
// Everything sits far below gameplay (BackdropCatalog.BaseOrder); the pools
// are fixed and a frame allocates nothing.
public static class DesertTuning
{
    // ---- brightness ----------------------------------------------------------
    // The Desert art is painted dark-base with bone-gold crest lights (tile value
    // p90 .40-.49, night .32-.42). Every world's gameplay guard is that an enemy
    // body reads 2.5:1 against the rendered lane and the lane stays darker than
    // the hull (WorldBackdropTest.CheckReadability, DesertBackdropTest), so each
    // set may be DRAWN darker by its entry in VariantBrightness (k < 1 is a
    // darkening curve, BackdropGrade; a layer takes its Graded share of it). The
    // world's own lift stays 1 (below BackdropCatalog.Spec.BrightLift: the dark
    // worlds' thin shot outlines). Undarkened, the brightest enemy tone (the sun-white light) read
    // 6.8:1 (v1) and 5.9:1 (v3) against the rendered lane, under the 7:1 guard; the entries below
    // bring every set to >= 7:1 and the night side below the day.
    public static float Brightness = 1f;
    public static readonly float[] VariantBrightness = { 1f, .93f, .95f, .8f, 1f };   // index = variant (0 none)
    public static readonly bool[] Night = { false, false, false, false, true };
    public static float NightLightBoost = 1.3f;
    public static float PlumeShare = 1f;
    public static float PlumeLift = 1f;
    public static float LoopBrightness = 1f;
    public static float LightBoostNow = 1f;           // set by the director for its variant

    // ---- ground pieces -------------------------------------------------------
    public static float LandmarkMin = 1.5f, LandmarkMax = 1.75f;
    public static float FireMin = 1.2f, FireMax = 1.6f, SatelliteMin = 1.0f, SatelliteMax = 1.3f;
    public static float PipeMin = .5f, PipeMax = 1.45f;
    public static float ClusterGapMin = 1.6f, ClusterGapMax = 3.2f;     // scrolled ground distance after a cluster
    public static float LoneGapMin = .8f, LoneGapMax = 1.5f;             // ... after a lone piece
    public static float NeighbourGapMin = .1f, NeighbourGapMax = .45f;  // between a ruin's plate and its neighbour's
    public static float Wind = 1f;                                        // +1: plumes lean right (as painted)
    public static float PlateFeather = 0f;                                // the pieces carry their own sand skirt

    // what each drawing may stand on (GroundPlanner). Mask letters: W dune sand,
    // B built, O hardpan, C cliff (ground_masks.py).
    static readonly PieceRule Structure = new PieceRule(GroundClass.Water | GroundClass.Open | GroundClass.Built, .7f, .3f);    // ruins, rigs, masts, caravans: anywhere but the cliffs
    static readonly PieceRule Feature = new PieceRule(GroundClass.Any, 0f, .3f);                                              // rock, ribs, drifts, wreckage: anywhere
    static readonly PieceRule Hollow = new PieceRule(GroundClass.Open | GroundClass.Water, .75f, .3f);                         // wells, vents, glass: soft or flat ground
    static readonly PieceRule Dune = new PieceRule(GroundClass.Water, .8f, .3f);                                               // a dune ridge: sand
    public static readonly PieceRule DockRelaxed = new PieceRule(GroundClass.Water | GroundClass.Open | GroundClass.Built, .6f, .3f);   // (dunes / flats / built ground, never the cliffs)
    static readonly PieceRule Anywhere = new PieceRule(GroundClass.Any, 0f, .2f);
    // a pipe bridge laid ALONE (a bank piece; runs between structures lay
    // their own): its pipes are sliced off at the cell edge, so both ends must
    // come down on painted pipeline / ruin (Built) -- it carries a painted line
    // over a gap -- never left standing on open sand. Never mirrored. Ends:
    // share of the drawn width from the centre, y up, over its feet.
    static readonly PieceRule PipeSpan0 = new PieceRule(GroundClass.Any, 0f, .2f)
        .WithEnds(new Vector2(-.42f, -.12f), new Vector2(.42f, -.12f), GroundClass.Built);
    static readonly PieceRule PipeSpan1 = new PieceRule(GroundClass.Any, 0f, .2f)
        .WithEnds(new Vector2(-.30f, -.15f), new Vector2(.42f, -.15f), GroundClass.Built);

    public static PieceRule Rule(string piece)
    {
        if (piece == null) return Anywhere;
        if (piece == "pipe_bridge_00") return PipeSpan0;
        if (piece == "pipe_bridge_01") return PipeSpan1;
        if (piece == "dune_ridge") return Dune;
        if (piece.StartsWith("dry_well") || piece.StartsWith("heat_vent") || piece.StartsWith("fulgurite") || piece == "fused_glass_crater_rim")
            return Hollow;
        if (piece.StartsWith("rock_fin") || piece.StartsWith("wall_drift") || piece.StartsWith("machine_ribs") || piece == "crack_scar" ||
            piece == "wreckage" || piece == "cliff_arch")
            return Feature;
        if (piece.StartsWith("refinery_hull") || piece.StartsWith("ribcage_gantry") || piece == "drill_head_tower" || piece.StartsWith("solar_array") ||
            piece.StartsWith("crawler_caravan") || piece == "radio_mast" || piece == "collapsed_dome_cluster" || piece == "bucket_wheel_excavator" ||
            piece == "sunken_cargo_crawler" || piece == "pump_well_station" || piece == "lone_crawler" || piece == "pump_house" ||
            piece.StartsWith("worm_burrow") || piece.StartsWith("crawler_bay") || piece.StartsWith("glass_pad") || piece.StartsWith("buried_silo") ||
            piece.StartsWith("ruin_dock") || piece.StartsWith("buriedbunker") || piece.StartsWith("crawlerhangar") || piece.StartsWith("mirrorsilo") ||
            piece.StartsWith("canyondock"))
            return Structure;
        return Anywhere;     // pipes: they connect what they connect
    }

    // ---- clusters --------------------------------------------------------------
    public const int ClRuin = 0, ClWells = 1, ClDunes = 2, ClLone = 3, ClPipeline = 4;
    public static readonly int[][] ClusterWeights =
    {
        new[] { 2, 2, 2, 2, 1 },   // (none)
        new[] { 1, 3, 4, 2, 1 },   // v1 day dunes: ridges, drifts, caravans, a lone ruin
        new[] { 1, 3, 1, 4, 1 },   // v2 canyon and ruins (an ancient stone ruin tile): few industrial hulls and pipe runs, mostly lone pieces (arches, rock fins, ribs, wells)
        new[] { 2, 3, 1, 3, 1 },   // v3 salt flat: wells and glass, lone solar skeletons
        new[] { 1, 3, 3, 3, 1 },   // v4 night: vents, dunes, lone lit towers
    };
    public static readonly string[] Ruins = { "refinery_hull_00", "refinery_hull_01", "ribcage_gantry_00", "ribcage_gantry_01", "drill_head_tower", "bucket_wheel_excavator" };
    public static readonly string[] Neighbours = { "radio_mast", "solar_array_skeleton_00", "solar_array_skeleton_01", "collapsed_dome_cluster", "pump_house", "crawler_caravan_00" };
    public static readonly string[] Fronts = { "dry_well_00", "dry_well_01", "heat_vent_00", "heat_vent_01", "fulgurite_00", "fulgurite_01" };
    public static readonly string[] Satellites = { "rock_fin_00", "rock_fin_01", "machine_ribs_00", "machine_ribs_01", "wreckage", "lone_crawler", "crack_scar", "fulgurite_00" };
    public static readonly string[] Caravans = { "crawler_caravan_00", "crawler_caravan_01", "sunken_cargo_crawler", "lone_crawler" };
    public static readonly string[] Banks = { "dune_ridge", "wall_drift_00", "wall_drift_01", "crack_scar", "pipe_bridge_00", "pipe_bridge_01" };
    public static readonly string[] Lone = { "refinery_hull_00", "refinery_hull_01", "ribcage_gantry_00", "ribcage_gantry_01", "drill_head_tower", "solar_array_skeleton_00", "solar_array_skeleton_01",
                                             "radio_mast", "collapsed_dome_cluster", "bucket_wheel_excavator", "sunken_cargo_crawler", "fused_glass_crater_rim", "cliff_arch", "pump_well_station" };
    public static readonly string[] HorizontalPipes = { "straight_00", "straight_01", "manifold_00", "manifold_01", "tangle_01", "leak_01", "leak_00" };

    // ---- launch sites ----
    public static float SiteMin = 1.5f, SiteMax = 1.7f, SiteFirst = 3f, SiteGapMin = 5f, SiteGapMax = 8.5f;
    public static float SiteCloseDelay = .9f, SiteMinSize = 1.2f;
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
    public const float EmergeScalePerUnit = .22f, EmergeScaleMin = .26f, EmergeScaleMax = .4f;
    public const int SiteIdBase = 900;

    // ---- the giant sand worms ----
    // Sizes are the cell's width in world units (a cell is 256 px, a giant's 512): a small
    // worm is ship-sized, a medium a few ships, a giant takes most of the screen.
    public static bool WormsEnabled = true;
    public static bool RidgeOnly = false;                       // the low-quality kill switch: the trail without the eruption
    public static readonly float[] WormCell = { 1.25f, 2.1f, 4.2f };           // small, medium, giant
    public static readonly float[] WormWeight = { 5f, 3f, 1f };               // (the giant is also rate-limited: GiantEvery)
    public static float WormFirst = 12f;                        // never in the first seconds
    public static float WormGapMin = 9f, WormGapMax = 18f;      // calm between one worm's end and the next one's start
    public static float GiantEvery = 60f;                       // a giant at most this often
    public static float WormSpeedMin = .14f, WormSpeedMax = .3f;                // units/s of ground along its heading
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
    // PINNED ITEMS vs the worm: its ridge trail and eruption keep clear of every pinned item (landmark, pipe,
    // feature, site) by this margin (world units, on top of the footprints); an item that still ends up under the
    // ridge (one that scrolled into its path) is DESTROYED, up to MaxRazed per worm -- never a site (an elite may
    // be docked in it: the worm dives instead). Backdrop only: no collider, nothing in gameplay changes.
    public static float WormMargin = .14f;
    public static int MaxRazed = 3;
    // the distant blasts of a razed item: the Rock row of the weapon explosions scaled down, dust-tinted and hazy
    public static float RazeFadeDelay = .25f, RazeFadeSeconds = .7f;
    public static float BlastSizeK = .5f, BlastSizeMin = .45f, BlastSizeMax = .8f, BlastAlpha = .5f, BlastRumbleVolume = .2f;
    public static int BlastsPerItem = 3;
    public static Color BlastTint = new Color(.82f, .76f, .64f, 1f);

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
    public static Color CeilingTint = new Color(.55f, .52f, .45f, 1f);
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
    public static float PallAlpha = .6f, PallLife = 22f;
    // free loops wandering the ground (a dust devil over the dunes, a mirage glint over the pans, a grit gust)
    public static int WandererCount = 4;
    public static float WandererMin = 1.1f, WandererMax = 1.7f, WandererAlpha = 1f, WandererLife = 7f, WandererEveryMin = 3f, WandererEveryMax = 6.5f;
}

public class DesertDirector : PlanetDirector
{
    BackdropPool ground, pipes, fires, sites, palls, mist, wisps, ceiling, gusts, grit, wanderers, blasts;
    BackdropAtlas landmarkArt, pipeArt, fireArt, siteArt, dockArt, weather;
    readonly SpriteRenderer[] mists = new SpriteRenderer[3];
    readonly float[] mistA = new float[3];
    float mistClock;
    Color[] dockTints;                                 // per site kind: its DockGrade colour on this ground set
    Sprite[][] siteLaunch;                             // per site kind: its 6 launch cells (null: an old two-drawing site, or the atlas is missing)
    AmbientEmitters ambient;
    GroundPlanner planner;
    readonly Sprite[][] wandererArt = new Sprite[DesertAmbientCatalog.WandererNames.Length][];
    float wandererIn = 4f;
    Sprite[] siteClosed, siteOpen, banks, wispArt, mistArt, gustArt, pallArt, gritArt;
    float groundTravel, groundGap, siteTravel, siteGap, ceilingTravel;
    int lastCluster = -1, lastLone = -1;
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
    public int Clusters { get; private set; }
    public int PipeRuns { get; private set; }
    public AmbientEmitters Ambient => ambient;
    public GroundPlanner Planner => planner;
    public BackdropPool Ground => ground;
    public BackdropPool Pipes => pipes;
    public BackdropPool Fires => fires;
    public BackdropPool Sites => sites;
    // The six launch cells of a launch-strip site kind (DesertTuning.SiteNames index), null for an old two-drawing site (tests, previews).
    public Sprite[] LaunchStrip(int kind) { return siteLaunch != null && kind >= 0 && kind < siteLaunch.Length ? siteLaunch[kind] : null; }
    public BackdropPool Ceiling => ceiling;
    public BackdropPool GustSheetPool => gusts;
    public BackdropPool Palls => palls;
    public BackdropPool Grit => grit;
    public BackdropPool Wanderers => wanderers;
    public BackdropPool Blasts => blasts;
    public Material CeilingMaterial { get; private set; }
    public Material PlateMaterial { get; private set; }

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
        landmarkArt = set.Atlas("landmarks");
        pipeArt = set.Atlas("pipes");
        fireArt = set.Atlas("fires");
        siteArt = set.Atlas("sites");
        dockArt = set.Atlas(DesertTuning.DocksAtlas);
        weather = set.Atlas("weather");
        night = set.Spec.Night(set.Variant);
        DesertTuning.LightBoostNow = night ? DesertTuning.NightLightBoost : 1f;
        ambient = new AmbientEmitters(set, DesertAmbientCatalog.Table) { Wind = DesertTuning.Wind };
        planner = new GroundPlanner(set, "mid", GroundMask.Load("Desert", set.Variant));

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
        pallArt = weather.Frames("pall");
        gritArt = weather.Frames("particle");

        // ground plane, back to front: pipes tuck under the plates they join
        pipes = LandmarkPool("ground", 8, -4);
        fires = LandmarkPool("ground", 6, -2);
        ground = LandmarkPool("ground", 8);
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
        // Every pool's loops draw above EVERY pool's plates (pipes -4, fires -2,
        // ground / sites 0 of the layer's order): a fire's or pipe's plume rises
        // through the ground, so a landmark plate higher up the screen must not
        // slice it off flat (the "smoke cut off at the top" report).
        ambient.Rig(pipes, 5);
        ambient.Rig(fires, 3);
        ambient.Rig(ground, 1);
        ambient.Rig(sites, 1);
        wanderers = Pool("ground", DesertTuning.WandererCount, false, -8);
        for (int i = 0; i < wandererArt.Length; i++)
        {
            var atlas = set.Atlas(DesertAmbientCatalog.AtlasOf(DesertAmbientCatalog.WandererNames[i]));
            wandererArt[i] = atlas.Frames(DesertAmbientCatalog.WandererNames[i]);
        }
        BuildWorms();
        BuildBlasts();
        palls = Pool("palls", 4);
        mist = Pool("mist", 3);
        wisps = Pool("wisps", 4);
        ceiling = Pool("ceiling", 26);
        gusts = Pool("gusts", DesertTuning.GustSheets + 1);
        grit = Pool("grit", DesertTuning.GritCount);

        PlateMaterial = Grade(ground, "ground", 1f, DesertTuning.PlateFeather);
        Grade(fires, "ground", 1f, DesertTuning.PlateFeather);
        Grade(sites, "ground", 1f, DesertTuning.PlateFeather);
        Grade(pipes, "ground");
        GradeWorms();
        Grade(palls, "palls");
        Grade(mist, "mist");
        Grade(wisps, "wisps");
        CeilingMaterial = Grade(ceiling, "ceiling", DesertTuning.CeilingThicken, 0f, DesertTuning.CeilingLift, DesertTuning.CeilingSaturation);

        // the opening view: the ground already dressed, under a thick ceiling
        groundGap = Rand(DesertTuning.LoneGapMin, DesertTuning.LoneGapMax);
        siteGap = DesertTuning.SiteFirst;
        if (planner.Ready)
        {
            double t = planner.Travel;
            for (float y = -HalfH * .85f; y < HalfH * .8f; y += Rand(2.6f, 3.6f))
                SpawnCluster(t + y, t + y + 1.2f);
        }
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
        groundTravel += scrolled;
        siteTravel += scrolled;
        planner.Prune();
        if (planner.Ready && groundTravel >= groundGap)
        {
            float gap = SpawnGround();
            if (gap > 0f) { groundTravel = 0f; groundGap = gap; }
            else groundGap = groundTravel + .25f;     // nothing fitted: look again a little further on
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

        StepPinned(pipes, dt);
        StepPinned(fires, dt);
        StepPinned(ground, dt);
        StepPinned(sites, dt);
        StepSites(dt);
        StepWanderers(dt);
        StepWorms(dt, v);
        StepBlasts(dt);
        ambient.Step(1f);
        StepPalls(dt);

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
            Color c = DesertTuning.CeilingTint;
            c.a = b.phase * density * low * set.Alpha;
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
            if (p.dyingT >= 0f)
            {
                p.dyingT += dt;
                p.fade = 1f - Mathf.Clamp01((p.dyingT - DesertTuning.RazeFadeDelay) / DesertTuning.RazeFadeSeconds);
                if (p.fade <= 0f) { Despawn(p); continue; }
            }
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

    // Free loops wandering the ground: a dust devil over the dunes, a gust of grit,
    // a mirage glint over the pans. Pinned like the ground (each rides its spot),
    // faded in and out. Nothing spawns while their atlases are not installed.
    void SpawnWanderer()
    {
        int kind = rng.Next(wandererArt.Length);
        var frames = wandererArt[kind];
        if (frames == null || frames.Length == 0 || !planner.Ready) return;
        string loop = DesertAmbientCatalog.WandererNames[kind];
        // a dust devil wants the soft dune sand, a mirage glint the flats; a gust goes anywhere
        GroundClass on = loop == "dustdevil" ? GroundClass.Water : loop == "mirage_glint" ? GroundClass.Open : GroundClass.Any;
        float size = Rand(DesertTuning.WandererMin, DesertTuning.WandererMax);
        for (int t = 0; t < 12; t++)
        {
            float x = Rand(XMin(size), XMax(size));
            double gy = planner.Travel + HalfH + size * .5f + Rand(0f, 2f);
            if (planner.Match(x, gy, size * .3f, size * .25f, on) < .8f) continue;
            var p = wanderers.Spawn();
            if (p == null) return;
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
            p.body.localScale = Chance(.5) ? new Vector3(-1f, 1f, 1f) : Vector3.one;
            // a foot-anchored loop (the dust devil) stands on its spot, a centred one is centred on it
            p.body.localPosition = DesertAmbientCatalog.Centred(loop) ? Vector3.zero : new Vector3(0f, 1.08f, 0f);
            Place(p);
            Tinted(p, 0f);
            return;
        }
    }

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
            if (k >= 1f || p.y < -HalfH - p.size) { Despawn(p); continue; }
            p.Animate();
            Tinted(p, p.phase * Mathf.Min(1f, Mathf.Min(k * 5f, (1f - k) * 4f)));
        }
    }

    // --------------------------------------------------------------- ground --

    // A cluster (or a launch site, when one is due) entering at the top.
    // Returns the scrolled ground distance to wait before the next, 0 when
    // nothing fitted.
    float SpawnGround()
    {
        double t = planner.Travel;
        double lo = t + HalfH + .95f, hi = lo + .9f;
        if (siteTravel >= siteGap && SpawnSite(float.NaN))
        {
            siteTravel = 0f;
            siteGap = Rand(DesertTuning.SiteGapMin, DesertTuning.SiteGapMax);
            return Rand(DesertTuning.LoneGapMin, DesertTuning.LoneGapMax);
        }
        int n = SpawnCluster(lo, hi);
        if (n == 0) return 0f;
        return n > 1 ? Rand(DesertTuning.ClusterGapMin, DesertTuning.ClusterGapMax)
                     : Rand(DesertTuning.LoneGapMin, DesertTuning.LoneGapMax);
    }

    float XMin(float size) { return -EdgeX + size * .3f; }
    float XMax(float size) { return EdgeX - size * .3f; }

    int PickCluster()
    {
        var w = DesertTuning.ClusterWeights[Mathf.Clamp(set.Variant, 0, DesertTuning.ClusterWeights.Length - 1)];
        int total = 0;
        for (int i = 0; i < w.Length; i++) if (i != lastCluster) total += w[i];
        int r = rng.Next(Mathf.Max(1, total));
        for (int i = 0; i < w.Length; i++)
        {
            if (i == lastCluster || w[i] <= 0) continue;
            r -= w[i];
            if (r < 0) return i;
        }
        return DesertTuning.ClLone;
    }

    // Grows one cluster with its anchor in the ground band [lo, hi]; returns
    // how many pieces it placed (0: nothing fitted).
    public int SpawnCluster(double lo, double hi, int kind = -1)
    {
        if (kind < 0) kind = PickCluster();
        int n = 0;
        switch (kind)
        {
            case DesertTuning.ClRuin: n = Industrial(Pick(DesertTuning.Ruins), lo, hi); break;
            case DesertTuning.ClPipeline: n = Industrial(Pick(DesertTuning.Lone), lo, hi); break;
            case DesertTuning.ClWells: n = WellField(lo, hi); break;
            case DesertTuning.ClDunes: n = DuneScene(lo, hi); break;
        }
        if (n == 0) n = LonePiece(lo, hi);
        if (n > 0) { lastCluster = kind; Clusters++; }
        return n;
    }

    // Draws `name` from its atlas at x, gy (pinned), claiming its footprint.
    BackdropPiece Put(BackdropPool pool, BackdropAtlas atlas, string name, float size, float x, double gy, bool mirror)
    {
        var sprite = atlas.Get(name);
        if (sprite == null) return null;
        var rule = DesertTuning.Rule(name);
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

    bool Spot(string name, float size, double lo, double hi, out float x, out double gy)
    {
        return planner.Find(DesertTuning.Rule(name), size, XMin(size), XMax(size), lo, hi, rng, out x, out gy);
    }

    // A structure (refinery / tower ...) with a neighbour joined by a pipe run.
    int Industrial(string anchor, double lo, double hi)
    {
        float sa = Rand(DesertTuning.LandmarkMin, DesertTuning.LandmarkMax);
        float xa; double ga;
        if (!Spot(anchor, sa, lo, hi, out xa, out ga)) return 0;
        var a = Put(ground, landmarkArt, anchor, sa, xa, ga, Chance(.5));
        if (a == null) return 0;
        int n = 1;
        // the neighbour: toward the middle first, then the other side; a neighbour that does not fit
        // (the deck / shoals under it, the room beside the anchor) is swapped for another, up to three tries
        for (int pick = 0; pick < 3 && n == 1; pick++)
        {
            string nb = Pick(DesertTuning.Neighbours);
            if (nb == anchor) nb = "radio_mast";
            bool pump = nb == "pump_house";
            float sb = pump ? Rand(.9f, 1.1f) : Rand(DesertTuning.LandmarkMin * .85f, DesertTuning.LandmarkMax * .9f);
            float plateA = sa * .4f, plateB = sb * (pump ? .44f : .4f);
            for (int attempt = 0; attempt < 2; attempt++)
            {
                float side = (xa > 0f) == (attempt == 0) ? -1f : 1f;
                float gap = Rand(DesertTuning.NeighbourGapMin, DesertTuning.NeighbourGapMax);
                float xb = xa + side * (plateA + gap + plateB);
                double gb = ga + Rand(-.12f, .12f);
                if (xb < XMin(sb) - .2f || xb > XMax(sb) + .2f) continue;
                var rule = DesertTuning.Rule(nb);
                if (!planner.Accepts(rule, xb, gb, sb) || !planner.Free(xb, gb, GroundPlanner.HalfX(rule, sb), GroundPlanner.HalfY(rule, sb))) continue;
                var b = Put(pump ? pipes : ground, pump ? pipeArt : landmarkArt, nb, sb, xb, gb, !pump && Chance(.5));
                if (b == null) break;
                n++;
                if (PipeRun(Mathf.Min(xa, xb) + (xa < xb ? plateA : plateB) * .75f,
                            Mathf.Max(xa, xb) - (xa < xb ? plateB : plateA) * .75f,
                            (ga + gb) * .5 - sa * .16f)) n++;
                break;
            }
        }
        return n;
    }

    // A horizontal pipe (or two) whose flange faces land on x0 and x1 at
    // ground row gy: the measured pipe ends (points.json) set its scale.
    bool PipeRun(float x0, float x1, double gy)
    {
        float len = x1 - x0;
        if (len < .3f) return false;
        // over soft dune sand the run is half buried, over a cliff or gap it is a trestle bridge
        var under = planner.ClassAt((x0 + x1) * .5f, gy);
        bool buried = under == GroundClass.Water, trestle = under == GroundClass.Canopy;
        string name = buried ? "half_buried_pipe" : trestle ? "pipe_bridge_00" : Pick(DesertTuning.HorizontalPipes);
        int pieces = 1;
        float span = SpanOf(name);
        float w = len / span;
        if (w > DesertTuning.PipeMax) { pieces = 2; w = len * .5f / span; }
        if (w < DesertTuning.PipeMin * .6f) return false;
        if (!planner.Free((x0 + x1) * .5f, gy, Mathf.Max(.05f, len * .5f - .25f), .1f)) return false;
        float placed = x0;
        for (int i = 0; i < pieces; i++)
        {
            string n = i == 0 ? name : (buried ? "half_buried_pipe" : trestle ? "pipe_bridge_00" : "straight_00");
            float sp = SpanOf(n);
            float wi = (len / pieces) / sp;
            var ends = Ends(n);
            // the piece's centre: its first flange face on `placed`, its flange line on gy
            float e0x = ends != null ? ends[0].x : 14f, ey = ends != null ? ends[0].y : 128f;
            float cx = placed - (e0x - 128f) / 256f * wi;
            double cy = gy - (128f - ey) / 256f * wi;
            var p = Put(pipes, pipeArt, n, wi, cx, cy, false);
            if (p == null) return i > 0;
            p.kind = RunPipe;
            placed += len / pieces;
        }
        PipeRuns++;
        return true;
    }

    // BackdropPiece.kind of a pipe laid by a run between two structures (its
    // ground is whatever it crosses; a lone pipe bridge keeps its rule).
    public const int RunPipe = 1;

    // Share of the cell width between a horizontal pipe's two flange faces.
    static float SpanOf(string pipe)
    {
        var e = Ends(pipe);
        if (e == null || e.Length < 2) return 227f / 256f;
        return Mathf.Max(.3f, Mathf.Abs(e[1].x - e[0].x) / 256f);
    }

    static DesertAmbientCatalog.PEnd[] Ends(string pipe)
    {
        var p = DesertAmbientCatalog.Piece(pipe);
        return p != null && p.ends != null && p.ends.Length >= 2 ? p.ends : null;
    }

    // A dry well, heat vent or fulgurite with rock, ribs, wreckage or a lone
    // crawler beside it (downwind first) and, over a heat vent, a pale pall.
    int WellField(double lo, double hi)
    {
        string front = Pick(DesertTuning.Fronts);
        float sf = Rand(DesertTuning.FireMin, DesertTuning.FireMax);
        float xf; double gf;
        if (!Spot(front, sf, lo, hi, out xf, out gf)) return 0;
        var f = Put(fires, fireArt, front, sf, xf, gf, false);
        if (f == null) return 0;
        int n = 1, want = 1 + rng.Next(2);
        float wind = DesertTuning.Wind;
        for (int k = 0; k < 6 && n <= want; k++)
        {
            string sat = Pick(DesertTuning.Satellites);
            float ss = Rand(DesertTuning.SatelliteMin, DesertTuning.SatelliteMax);
            float dir = k % 2 == 0 ? wind : -wind;
            float xs = xf + dir * Rand(.75f, 1.15f) * (sf + ss) * .5f;
            double gs = gf + Rand(-.7f, .5f);
            if (xs < XMin(ss) || xs > XMax(ss)) continue;
            var rule = DesertTuning.Rule(sat);
            if (!planner.Accepts(rule, xs, gs, ss) || !planner.Free(xs, gs, GroundPlanner.HalfX(rule, ss), GroundPlanner.HalfY(rule, ss))) continue;
            if (Put(fires, fireArt, sat, ss, xs, gs, Chance(.5)) != null) n++;
        }
        if (front.StartsWith("heat_vent")) SpawnPall(f);
        return n;
    }

    // A dune scene on the sand: a dune ridge, wall drift or crack scar with a
    // crawler caravan (or a wreck) crossing near it, sometimes a pipe bridge.
    int DuneScene(double lo, double hi)
    {
        int n = 0;
        string bank = Pick(DesertTuning.Banks);
        float sk = Rand(DesertTuning.LandmarkMin, DesertTuning.LandmarkMax);
        bool pipeBank = bank.StartsWith("pipe_bridge");
        var atlas = pipeBank ? pipeArt : fireArt;
        var pool = pipeBank ? pipes : fires;
        float x; double gy;
        if (Spot(bank, sk, lo - .4f, hi + .4f, out x, out gy) && Put(pool, atlas, bank, sk, x, gy, !DesertTuning.Rule(bank).HasEnds && Chance(.5)) != null) n++;
        string caravan = Pick(DesertTuning.Caravans);
        float sv = Rand(DesertTuning.LandmarkMin * .9f, DesertTuning.LandmarkMax);
        bool lone = caravan == "lone_crawler";
        if (Spot(caravan, sv, lo - .4f, hi + 1.4f, out x, out gy) &&
            Put(lone ? fires : ground, lone ? fireArt : landmarkArt, caravan, sv, x, gy, Chance(.5)) != null) n++;
        return n;
    }

    int LonePiece(double lo, double hi)
    {
        for (int k = 0; k < 3; k++)
        {
            int i = rng.Next(DesertTuning.Lone.Length);
            if (i == lastLone) i = (i + 1) % DesertTuning.Lone.Length;
            string name = DesertTuning.Lone[i];
            float s = Rand(DesertTuning.LandmarkMin, DesertTuning.LandmarkMax);
            float x; double gy;
            if (!Spot(name, s, lo, hi, out x, out gy)) continue;
            if (Put(ground, landmarkArt, name, s, x, gy, Chance(.5)) == null) continue;
            lastLone = i;
            return 1;
        }
        return 0;
    }

    // A smoke pall over a fire: rides the fire's ground spot (pinned),
    // rising slowly and drifting downwind as it ages, fading in and out.
    void SpawnPall(BackdropPiece fire)
    {
        if (pallArt.Length == 0) return;
        var p = palls.Spawn();
        if (p == null) return;
        SetSprite(p, Pick(pallArt), Rand(2.2f, 3f));
        if (Chance(.5)) p.body.localScale = new Vector3(-1f, 1f, 1f);
        p.gy = fire.gy + fire.size * .35f;
        p.x = fire.x + DesertTuning.Wind * fire.size * .3f;
        p.vx = DesertTuning.Wind * Rand(.03f, .06f);
        p.vy = Rand(.02f, .05f);
        p.life = DesertTuning.PallLife;
        p.rate = set.Spec.Rate("ground");
        p.phase = DesertTuning.PallAlpha * Rand(.8f, 1f);
        p.y = planner.Y(p.gy);
        Place(p);
        Tinted(p, 0f);
    }

    void StepPalls(float dt)
    {
        foreach (var p in palls.items)
        {
            if (!p.active) continue;
            p.age += dt;
            p.x += p.vx * dt;
            p.y = planner.Y(p.gy) + p.vy * p.age;
            Place(p);
            float k = Mathf.Clamp01(p.age / Mathf.Max(.01f, p.life));
            if (k >= 1f || p.y < -HalfH - p.size) { Despawn(p); continue; }
            Tinted(p, p.phase * Mathf.Min(1f, Mathf.Min(k * 6f, (1f - k) * 4f)));
        }
    }

    // Preview / tests: clears the ground plane and shows `name` alone at
    // screen x, y (pinned there), size `size`, its rule ignored.
    public BackdropPiece Showcase(string name, float size, float x, float y, bool mirror = false)
    {
        ClearWorms();
        foreach (var pool in new[] { pipes, fires, ground, sites })
            foreach (var q in pool.items)
                if (q.active) { planner.Release(q.footprint); q.footprint = -1; Despawn(q); }
        foreach (var q in palls.items) if (q.active) Despawn(q);
        bool pipe = pipeArt.Has(name), fire = fireArt.Has(name), site = siteArt.Has(name);
        var atlas = pipe ? pipeArt : fire ? fireArt : site ? siteArt : landmarkArt;
        var p = Put(pipe ? pipes : fire ? fires : site ? sites : ground, atlas, name, size, x, planner.GroundY(y), mirror);
        if (p != null && fire) SpawnPall(p);
        groundTravel = -1000f;     // nothing else spawns while it is shown
        siteTravel = -1000f;
        return p;
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
        // behind everything else on the ground plane (landmarks, pipes, fires, sites, wanderers)
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
        Grade(worms, "ground", 1f, DesertTuning.PlateFeather);
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

    GroundClass WormGround { get { return set.Variant == 2 || set.Variant == 3 ? GroundClass.Water | GroundClass.Open : GroundClass.Water; } }

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
        for (int t = 0; t < 30; t++)
        {
            float x = float.IsNaN(atX) ? Rand(XMin(cell * .5f), XMax(cell * .5f)) : atX;
            double gy = planner.Travel + Rand(-.05f, .5f) * HalfH;
            float speed = Rand(DesertTuning.WormSpeedMin, DesertTuning.WormSpeedMax) * (size == 2 ? .6f : 1f);
            float a = float.IsNaN(heading) ? Rand(-.7f, .7f) : heading;
            float vx = speed * Mathf.Sin(a), vy = speed * Mathf.Cos(a);
            if (Mathf.Abs(x) > EdgeX * .4f) vx = -Mathf.Sign(x) * Mathf.Abs(vx);
            double gy2 = gy + vy * ridge1 * .5; float x2 = x + vx * ridge1 * .5f;
            double gy3 = gy + vy * ridge1; float x3 = x + vx * ridge1;
            if (!float.IsNaN(atX) && t == 0) { /* a forced spot: still checked below */ }
            if (Mathf.Abs(x3) > EdgeX - cell * .15f || !planner.Free(x, gy, rx, ry)) continue;
            if (!ignoreItems && !PathClear(x, gy, vx, vy, ridge1, rx, ry, cell)) continue;
            if (planner.Match(x, gy, rx, ry, WormGround) < .65f || planner.Match(x2, gy2, rx, ry, WormGround) < .6f ||
                planner.Match(x3, gy3, rx, ry, WormGround) < .55f) continue;
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
            p.body.localPosition = new Vector3((s0.rect.width * .5f - art.anchor.x) / ppu, (art.anchor.y - s0.rect.height * .5f) / ppu, 0f);
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

    // Tests: a piece planted on the ground at x, gy (its rule ignored), nothing else disturbed.
    public BackdropPiece Plant(string name, float size, float x, double gy)
    {
        bool pipe = pipeArt.Has(name), fire = fireArt.Has(name), site = siteArt.Has(name);
        var atlas = pipe ? pipeArt : fire ? fireArt : site ? siteArt : landmarkArt;
        return Put(pipe ? pipes : fire ? fires : site ? sites : ground, atlas, name, size, x, gy, false);
    }

    // Tests / previews: every pinned item gone, footprints freed (the worms stay).
    public void ClearItems()
    {
        foreach (var pool in new[] { pipes, fires, ground, sites })
            foreach (var q in pool.items)
                if (q.active) { planner.Release(q.footprint); q.footprint = -1; Despawn(q); }
        foreach (var q in palls.items) if (q.active) Despawn(q);
    }

    public void ClearWorms()
    {
        if (worms == null) return;
        foreach (var p in worms.items) if (p.active) { Despawn(p); wormSpray[worms.items.IndexOf(p)].enabled = false; }
    }

    // ---- pinned items vs the worm ----------------------------------------------------

    public int ItemsRazed { get; private set; }
    public int RumblesPlayed { get; private set; }
    public int EruptionsSkipped { get; private set; }
    // an order-independent fingerprint of everything razed so far (determinism tests; no allocation)
    public int RazedHash { get; private set; }
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

    // A worm's ridge touches a pinned item: destroy it (a site makes the worm dive instead).
    void WormContacts(BackdropPiece worm, int size)
    {
        float rx, ry;
        WormFoot(size, false, out rx, out ry);
        foreach (var pool in razable)
            foreach (var q in pool.items)
            {
                if (!q.active || q.dyingT >= 0f || !Overlaps(q, worm.x, worm.gy, rx, ry, 0f)) continue;
                if (pool == sites) { worm.tier = WormFading; worm.phase = 0f; return; }
                if (worm.kind >= 0 && razedByThis >= DesertTuning.MaxRazed) { worm.tier = WormFading; worm.phase = 0f; return; }
                Raze(q);
            }
    }

    int razedByThis;
    BackdropPool[] razable;

    // The item is gone for good (until the tile re-seeds): its loops stop, it fades under a few small, hazy
    // blasts and a plume of dust, and a low rumble plays.
    void Raze(BackdropPiece q)
    {
        razedByThis++;
        ItemsRazed++;
        RazedHash = (RazedHash * 31 + Mathf.RoundToInt(q.size * 100f)) * 31 + Mathf.RoundToInt((float)q.gy * 100f) * 7 + Mathf.RoundToInt(q.x * 100f);
        planner.Release(q.footprint);
        q.footprint = -1;
        q.dyingT = 0f;
        ambient.Detach(q);
        for (int i = 0; i < DesertTuning.BlastsPerItem; i++)
            StartBlast(q.x + Rand(-.3f, .3f) * q.size * .5f, q.gy + Rand(-.2f, .2f) * q.size * .5f, q.size, i * Rand(.18f, .3f), false);
        StartBlast(q.x, q.gy, q.size, .1f, true);
        RumblesPlayed++;
        if (Application.isPlaying) EnemyDeathAudio.PlayAuthored("frost_rock_chunk", DesertTuning.BlastRumbleVolume);
    }

    // ---- distant blasts -----------------------------------------------------------------

    Sprite[] blastFrames, plumeFrames;
    static readonly int[] BlastTicks = { 1, 1, 1, 3, 2, 2, 2, 2, 3, 3 };    // WeaponArt.ExplosionTicks at 24 fps

    void BuildBlasts()
    {
        blasts = Pool("ground", 10, false, 6);
        blastFrames = new Sprite[WeaponArt.ExplosionFrames];
        for (int i = 0; i < blastFrames.Length; i++) blastFrames[i] = WeaponArt.Explosion(TargetExplosion.Kind.Rock, i);
        plumeFrames = set.Atlas("dust").Frames("sandplume");
        razable = new[] { ground, fires, pipes, sites };
    }

    // kind: BackdropPiece.kind 0 = blast, 1 = dust plume; phase = the delay before it shows
    void StartBlast(float x, double gy, float itemSize, float delay, bool plume)
    {
        var p = blasts.Spawn();
        if (p == null) return;
        var frames = plume ? plumeFrames : blastFrames;
        if (frames == null || frames.Length == 0 || frames[0] == null) { Despawn(p); return; }
        float size = plume ? Mathf.Clamp(itemSize * .9f, .9f, 1.5f)
                           : Mathf.Clamp(itemSize * DesertTuning.BlastSizeK, DesertTuning.BlastSizeMin, DesertTuning.BlastSizeMax) * Rand(.8f, 1.1f);
        SetSprite(p, frames[0], size);
        p.frames = frames;
        p.kind = plume ? 1 : 0;
        p.phase = -delay;
        p.age = 0f;
        p.x = x; p.gy = gy; p.y = planner.Y(gy);
        p.rate = set.Spec.Rate("ground");
        p.body.localScale = Chance(.5) ? new Vector3(-1f, 1f, 1f) : Vector3.one;
        p.body.localPosition = plume ? new Vector3(0f, 1.08f, 0f) : Vector3.zero;       // a plume stands on its foot
        Place(p);
        Tinted(p, 0f);
    }

    void StepBlasts(float dt)
    {
        foreach (var p in blasts.items)
        {
            if (!p.active) continue;
            p.phase += dt;
            p.y = planner.Y(p.gy);
            Place(p);
            if (p.y < -HalfH - p.size) { Despawn(p); continue; }
            if (p.phase < 0f) { Tinted(p, 0f); continue; }
            float t = p.phase, a;
            int frame = 0;
            if (p.kind == 0)
            {
                // the weapon explosion's own timing: 10 cells, 20 ticks at 24 fps, stretched a little for the haze
                float ticks = t * 24f * .8f;
                int acc = 0;
                for (frame = 0; frame < BlastTicks.Length; frame++) { acc += BlastTicks[frame]; if (ticks < acc) break; }
                if (frame >= BlastTicks.Length) { Despawn(p); continue; }
                a = DesertTuning.BlastAlpha * (frame < 4 ? 1f : 1f - (frame - 3) / 7f);
            }
            else
            {
                frame = (int)(t * 8f);
                if (frame >= plumeFrames.Length) { Despawn(p); continue; }
                a = DesertTuning.BlastAlpha * .8f * Mathf.Min(1f, (plumeFrames.Length - frame) / 3f);
            }
            p.sr.sprite = p.frames[frame];
            Color c = DesertTuning.BlastTint;
            c.a = a * set.Alpha;
            p.sr.color = c;
        }
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
            if (p.age <= dt * 1.01f) razedByThis = 0;
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
                            // never surface under a pinned item: one that has scrolled into the spot cancels the eruption
                            float ex, ey;
                            WormFoot(p.kind, true, out ex, out ey);
                            bool blocked = false;
                            foreach (var pool in razable)
                                foreach (var q in pool.items)
                                    if (q.active && q.dyingT < 0f && Overlaps(q, p.x, p.gy, ex, ey, 0f)) blocked = true;
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
        double lo = float.IsNaN(y) ? planner.Travel + HalfH + .95f : planner.GroundY(y) - .3f;
        double hi = lo + (float.IsNaN(y) ? .9f : .6f);
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
            s.x = Rand(-HalfW, HalfW);
            s.y = Rand(-HalfH, HalfH);
            s.phase = Rand(0f, 6.28f);
            s.rate = set.Spec.Rate("grit");
            Place(s);
        }
    }
}
