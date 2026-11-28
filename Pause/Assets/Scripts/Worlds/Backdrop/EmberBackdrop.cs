using System.Collections.Generic;
using UnityEngine;

// Ember's backdrop: the ship flies HIGH (r1, 2026-11-28: GroundZoom .49, as Frost / Desert) over the forge planet, under an
// amber-charcoal ash-cloud ceiling, looking down on lava rivers, cracked basalt crust, ash plains and calderas, with a dying
// industrial age standing on them.
//
//   GROUND     one of four PixelLab-repainted tile sets per landing (BackdropVariants), 896 x 4096, drawn at EmberTuning.TileZoom
//              (two columns side by side): v1 caldera fields (lava rivers and lakes through black basalt), v2 cooled-crust plates,
//              v3 ash ridges (furrowed ash, lava threads), v4 the cracked-crust NIGHT side (EmberTuning.Night: drawn darker).
//              Each has its class grid (mask.txt: W lava, B built, O open, C crust) and context grid (ctx.txt: flat, rocky,
//              shore, plain; GroundMask.WithContext).
//   PROPS      the living props and the mountains (EmberProps.cs): power plant, foundry, mining rig, stuck ore train over the lava,
//              bunker, quarry, stilted station in a lava lake, flooded domes, a sinking crawler, basalt columns, hot springs,
//              flare-stack vents, the MASSIVE crashed starship with its molten scar, and the volcano / caldera / spires. Pinned to
//              the mid tile, each on the ground its context says (GroundPlanner), keep-out claimed on the planner.
//   ERUPTIONS  the one ground feature of round 1 that stays: lava fountains, eruption scars with coal beds and flare stacks, lava
//              pools (the fires atlas), grown as small CLUSTERS (EmberDirector.SpawnCluster) with an ash pall over each. Their loops --
//              eruption smoke, flares, steam and lava leaks, ember rain -- sit on MEASURED points (EmberAmbientCatalog), all leaning
//              with the one level wind. They are MOVERS to the props: claimed whole-cell, their palls' drift boxes too.
//   SITES      the elites' launch sites on the same pinned plane (foundry hangar, magma bay, slag pad, furnace bay, hatch, crucible,
//              bunker, gantry, kiln mouth), shut until the launch tell opens them.
//   WEATHER    the CEILING of ash cloud, thick at the start (continuing the planetfall's ash deck), held CeilingHold s, then
//              clearing fast, gone by CeilingClearSeconds (CloudCover); then only a light scattering of wisps and low mist
//              drifting with the wind (EmberTuning.CloudDensity), ASH GUSTS sweeping across now and then, and drifting embers.
//              (weather.png: the soft painted redo under the old sprite names)
//
// Everything sits far below gameplay (BackdropCatalog.BaseOrder); the pools are fixed and a frame allocates nothing.
public static class EmberTuning
{
    // ---- brightness ----------------------------------------------------------
    // The Ember art is painted dark and lava-lit (tile value p90 ~.40-.47,
    // v4 ~.32-.40) but its ground is lighter than the enemies fly over
    // comfortably: the forge city's grey plate maze (v2) has a lane median
    // value of .29, the caldera / ridge sets a lane p90 of .43-.49 against
    // the .42 enemy hull. Every world's gameplay guard is that an enemy body
    // reads 2.5:1 against the rendered lane and the lane stays darker than
    // the hull (WorldBackdropTest.CheckReadability, EmberBackdropTest), so each
    // set is DRAWN darker by its entry in VariantBrightness (k < 1 is a
    // darkening curve, BackdropGrade; a layer takes its Graded share of it):
    // the lava stays the bright accent. The world's own lift stays 1 (below
    // BackdropCatalog.Spec.BrightLift: the dark worlds' thin shot outlines).
    // The cracked-crust NIGHT side (v4) is the darkest and draws its lights
    // stronger (NightLightBoost). One number each.
    public static float Brightness = 1f;
    // 2026-10-27: the colourful redraw is drawn as painted (the old .6 / .35 / .7 / .55 darkening made every set near-black);
    // the guard moved to the art (calm central lane, vivid colour in the side bands) and to EmberBodyContrastMin 2.0.
    public static readonly float[] VariantBrightness = { 1f, 1f, 1f, .9f, .8f };   // index = variant (0 none)
    public static readonly bool[] Night = { false, false, false, false, true };
    public static float NightLightBoost = 1.3f;
    public static float PlumeShare = 1f;
    public static float PlumeLift = 1f;
    public static float LoopBrightness = 1f;
    public static float LightBoostNow = 1f;           // set by the director for its variant

    // ---- ALTITUDE (2026-11-28, the user-approved r1 ground; as Desert's) ---------------------------------------------------------
    // The ground is seen from GroundZoom (.49) of the old view; the r1 tiles (896 x 4096, PixelLab repaint of the old layouts) hold TWO
    // columns side by side, so they are drawn at TileZoom (2 x GroundZoom) of the view's width (BackdropCatalog.Spec.groundZoom).
    // Everything that stands on it is keyed to GroundZoom (Spec.pieceZoom), never TileZoom: the ground layers' rates, the sites, the
    // eruptions, the props, the cover boulders; x PieceFit (.7, as Frost) for the pieces' sizes.
    public const float GroundZoom = .49f, TileZoom = 2f * GroundZoom, PieceFit = .7f;
    // the SHOT LINE (Desert's): a shot's white-hot core (linear luminance ~1) stands 3:1 over the ground it crosses only while that ground
    // stays under (1 + .05) / 3 - .05 = .30, so each layout's lit ground keeps its 32 px patches' 99th percentile linear luminance at or
    // under ShotLine (build_r1.py shot_line exposes a brighter tile down in linear light; grade.json "exposure" keeps the factor)
    public const float ShotLine = .24f;
    public const int ShotPatch = 32;
    public const float PieceScale = GroundZoom * PieceFit;
    public const float GroundRate = .024f * GroundZoom;               // the mid tile's (and everything pinned to it) parallax rate
    // where a new piece is looked for: a band SpawnBand tall starting SpawnAbove over the top of the view
    public static float SpawnAbove = .95f * PieceScale, SpawnBand = .9f * GroundZoom, SpawnRetry = .25f * GroundZoom;
    public static float Wind = 1f;                                        // +1: plumes lean right (as painted)
    public static float PlateFeather = BackdropGrade.PlateFeatherTexels;

    // ---- the eruptions (the fires atlas: lava fountains, eruption scars, coal beds, flare stacks, lava pools) -------------------
    // The only ground pieces left of the round-1 forge world (the landmarks and pipes retired 2026-11-28: the living props, EmberProps.cs,
    // replace them). They erupt, smoke and glow (EmberAmbientCatalog loops, ash palls), so every prop keeps out of them: their footprint
    // is the whole drawn cell (the plume's reach) and the pall's drift box is claimed on the planner while it hangs.
    public static float FireMin = 1.4f * PieceScale, FireMax = 1.75f * PieceScale, SatelliteMin = 1.05f * PieceScale, SatelliteMax = 1.35f * PieceScale;
    public static float ClusterGapMin = 9f * GroundZoom, ClusterGapMax = 16f * GroundZoom;     // scrolled ground distance after an eruption cluster
    public static float LoneGapMin = 5f * GroundZoom, LoneGapMax = 9f * GroundZoom;            // ... after a lava pool
    public const float FireKeepShare = .5f;                        // the whole cell (its plume rises to the cell's top)

    // what each drawing may stand on (GroundPlanner). Lava is GroundClass.Water.
    static readonly PieceRule Clearing = new PieceRule(GroundClass.Land, .85f, .3f);
    static readonly PieceRule Vent = new PieceRule(GroundClass.Land, .8f, .3f);              // eruptions, beds, flares: through the crust
    static readonly PieceRule LavaRun = new PieceRule(GroundClass.Water, .45f, .2f);         // pools, channels, the magma bay
    // Every launch dock stands on solid crust (forge floor, ash flat, crust): never in the lava, never on its bank line.
    public static readonly PieceRule DockGround = new PieceRule(GroundClass.Land, .85f, .3f);
    public static readonly PieceRule DockGroundRelaxed = new PieceRule(GroundClass.Land, .7f, .3f);
    static readonly PieceRule Anywhere = new PieceRule(GroundClass.Any, 0f, .2f);

    public static PieceRule Rule(string piece)
    {
        if (piece == null) return Anywhere;
        if (piece.StartsWith("lavapool") || piece.StartsWith("lavachannel") || piece.StartsWith("firebreak") || piece.StartsWith("magmabay")) return LavaRun;
        if (piece.StartsWith("slagpad") || piece.StartsWith("hatch") || piece.StartsWith("crucible") || piece.StartsWith("bunker")) return Clearing;   // (the launch-strip sites: crucible silo, moat bunker; the gantry and the kiln mouth are docks below)
        if (piece.StartsWith("lavafountain") || piece.StartsWith("eruptionscar") || piece.StartsWith("coalbed") ||
            piece.StartsWith("scorched") || piece.StartsWith("flarestack")) return Vent;
        if (piece.StartsWith("foundryhangar") || piece.StartsWith("furnacebay") || piece.StartsWith("brassgantry") || piece.StartsWith("kilnmouth")) return DockGround;
        return Anywhere;
    }

    // ---- clusters --------------------------------------------------------------
    public const int ClEruption = 0, ClLava = 1;
    public static readonly int[][] ClusterWeights =
    {
        new[] { 3, 1 },   // (none)
        new[] { 3, 2 },   // v1 caldera fields: eruptions, lava pools
        new[] { 3, 1 },   // v2 forge plates
        new[] { 3, 1 },   // v3 ash ridges
        new[] { 4, 1 },   // v4 cracked crust night: eruptions
    };
    public static readonly string[] Fronts = { "lavafountain_00", "lavafountain_01", "lavafountain_02", "eruptionscar_00", "eruptionscar_01", "eruptionscar_02" };
    public static readonly string[] Satellites = { "coalbed_00", "coalbed_01", "scorched_00", "scorched_01", "flarestack_00", "flarestack_01" };
    public static readonly string[] Pools = { "lavapool_00", "lavapool_01", "lavachannel_00", "firebreak_00" };

    // ---- launch sites ----
    // (x PieceScale / x GroundZoom: the same docks, seen from the new height, as often per ground scrolled)
    public static float SiteMin = 1.5f * PieceScale, SiteMax = 1.7f * PieceScale, SiteFirst = 3f * GroundZoom, SiteGapMin = 5f * GroundZoom, SiteGapMax = 8.5f * GroundZoom;
    public static float SiteCloseDelay = .9f, SiteMinSize = 1.2f * PieceScale;
    // Entries 0-4 are the old two-drawing sites (sites atlas); 5-8 the launch-strip ones (docks atlas: <name>_00..05, DocksAtlas), for which
    // SiteClosed / SiteOpen name the idle and the open cell (Rule picks the ground by the name).
    public static readonly string[] SiteNames = { "foundryhangar", "magmabay", "slagpad", "furnacebay", "hatch", "crucible", "bunker", "brassgantry", "kilnmouth" };
    public static readonly string[] SiteClosed = { "foundryhangar_closed", "magmabay_closed", "slagpad_idle", "furnacebay_closed", "hatch_closed", "crucible_00", "bunker_00", "brassgantry_00", "kilnmouth_00" };
    public static readonly string[] SiteOpen = { "foundryhangar_open", "magmabay_open", "slagpad_active", "furnacebay_open", "hatch_open", "crucible_04", "bunker_04", "brassgantry_04", "kilnmouth_04" };
    public static readonly LandingKind[] SiteKinds = { LandingKind.FoundryHangar, LandingKind.MagmaBay, LandingKind.SlagPad, LandingKind.FurnaceBay, LandingKind.Hatch,
                                                       LandingKind.CrucibleSilo, LandingKind.MoatBunker, LandingKind.BrassGantry, LandingKind.KilnMouth };
    public static readonly int[] SiteWeights = { 3, 2, 2, 3, 2, 2, 2, 2, 2 };
    // where the elite comes out of the launch-strip sites: fractions of the drawing's bounds from its centre (y up), the pad pixels of Codex's
    // masters (new_docks_lights~/manifest.md: (384,399) of 768x767, (384,384) of 767x768, (256,407) of 512x768, (384,276) of 768x512)
    public static readonly Vector2[] SitePads =
    {
        new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(0f, 0f),
        new Vector2(0f, -.0203f), new Vector2(0f, 0f), new Vector2(0f, -.0299f), new Vector2(0f, -.0391f),
    };

    // ---- the launch strips (the docks atlas): closed, lamp, opening, opening, open, closing ----
    // Codex's launch_v2 strips (the first delivery had flat black panels and played backwards): 0 closed, 1 closed + a small amber lamp (never
    // shown: the ship's own launch light is the tell), 2-3 the doors opening, 4 fully open, 5 closing half shut. All six are in the atlas
    // (build_launch_atlas.py), as <name>_00..05.
    public const string DocksAtlas = "docks";
    public const int LaunchCells = 6, LaunchIdle = 0, LaunchBlink = 1, LaunchOpening = 2, LaunchOpen = 3, LaunchHold = 4, LaunchClosing = 5;
    public const int FirstLaunchSite = 5;                      // SiteNames[FirstLaunchSite..] have a strip
    public static bool HasLaunchStrip(int kind) { return kind >= FirstLaunchSite && kind < SiteNames.Length; }
    // drawn width of a strip site, as a share of the usual SiteMin..SiteMax (the gantry is a tall, narrow piece)
    public static readonly float[] SiteWidthK = { 1f, 1f, 1f, 1f, 1f, 1f, 1f, .8f, 1f };
    // The tell: the site stays closed through its first share (the ship's own small blinking launch light, EliteShip.LaunchTell, is the
    // tell; the strip's lamp cell 1 is never shown), then the doors open (2); the lift-off has them still opening (3 for its first share), then
    // fully open (4, held until the elite is clear); then they close for LaunchCloseSeconds (5) and the site is closed again. No flash, flare, halo or ring.
    public const float LaunchBlinkShare = .4f, LaunchOpenShare = .25f, LaunchCloseSeconds = .6f;

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
    public static Vector2 LampsAt = new Vector2(128f, 214f);    // the lamp row's centre on the site's cell
    public static float LampsScale = .5f;
    public const float EmergeScalePerUnit = .22f, EmergeScaleMin = .26f * PieceScale, EmergeScaleMax = .4f;
    public const int SiteIdBase = 700;

    // ---- weather (the atlas bakes its translucency in: banks <= 80/255) ----
    // CLOUD COVER: the knobs (CloudCover documents the timeline). How much
    // ash cloud drifts over the ground once the ceiling has cleared: 1 = a
    // light scattering (default), 2 = about twice as much, 0 = none.
    public static float CloudDensity = CloudCover.Density;
    public static float CeilingHold = CloudCover.CeilingHold;                   // s at full thickness after landing
    public static float CeilingClearSeconds = CloudCover.CeilingClearSeconds;   // s: the ceiling is gone
    public static float CeilingMin = 4.2f, CeilingMax = 5.6f, CeilingGap = 1.5f, CeilingLowShare = .55f;
    public static float CeilingThicken = 6f;
    // ... warmed toward the planetfall's amber-charcoal ash deck
    public static float CeilingLift = 1.3f, CeilingSaturation = 1.3f;
    // the banks are painted warm grey-brown: tinted toward the deck's amber
    public static Color CeilingTint = new Color(1f, .86f, .68f, 1f);
    // wisps and mist at CloudDensity 1 (density divides the gaps, lifts the alpha);
    // they drift downwind (Wind)
    public static float WispMin = 2.2f, WispMax = 3.2f, WispAlphaMin = .35f, WispAlphaMax = .55f;
    public static float WispEveryMin = 6f, WispEveryMax = 10f, WispDrift = .18f;
    public static float MistAlphaMin = .35f, MistAlphaMax = .5f;
    public static float MistEveryMin = 14f, MistEveryMax = 22f, MistDrift = .08f;
    public static float GustFirst = 12f, GustEveryMin = 14f, GustEveryMax = 22f;
    public static int GustSheets = 3;
    public static float GustStagger = .6f, GustSpeed = 3.4f, GustMin = 4.5f, GustMax = 6f, GustMaxAlpha = 1f;
    public static int EmberCount = 5;
    public static float EmberMin = .7f, EmberMax = 1.1f, EmberAlpha = .55f;
    public static float PallAlpha = .5f, PallLife = 22f;
    public const float PallSweep = .7f;     // the keep-out margin beyond its cell, for the drift (up to .05 x 22 s of rise, .06 x 22 s of lean)
}

public partial class EmberDirector : PlanetDirector
{
    BackdropPool fires, sites, palls, mist, wisps, ceiling, gusts, embers;
    readonly SpriteRenderer[] lamps = new SpriteRenderer[3];
    BackdropAtlas fireArt, siteArt, dockArt, weather, lights;
    Color[] dockTints;                                 // per site kind: its DockGrade colour on this ground set
    Sprite[][] siteLaunch;                             // per site kind: its 6 launch cells (null: an old two-drawing site, or the atlas is missing)
    AmbientEmitters ambient;
    GroundPlanner planner;
    Sprite[] siteClosed, siteOpen, banks, wispArt, mistArt, gustArt, pallArt, emberArt;
    Sprite lampsOff, lampsOn;
    float groundTravel, groundGap, siteTravel, siteGap, ceilingTravel;
    int[] pallFeet;                                     // per palls slot: the pall's claimed drift box (planner footprint)
    int lastCluster = -1;
    bool night;
    float mistIn = 2.5f, wispIn = 6f;
    readonly Timer gustTimer = new Timer(EmberTuning.GustEveryMin, EmberTuning.GustEveryMax, EmberTuning.GustFirst);
    int sheetsLeft;
    float sheetIn;
    readonly List<Material> mats = new List<Material>();

    // Tests: > 0 seeds the next directors' streams (statistics over many runs).
    public static int SeedOverride;
    public EmberDirector() : base(SeedOverride > 0 ? SeedOverride : 1990) { }

    public float Clock => clock;
    public bool IsNight => night;
    public int Gusts { get; private set; }
    public int SitesSpawned { get; private set; }
    public int Clusters { get; private set; }
    public AmbientEmitters Ambient => ambient;
    public GroundPlanner Planner => planner;
    public BackdropPool Fires => fires;
    public BackdropPool Sites => sites;
    // The six launch cells of a launch-strip site kind (EmberTuning.SiteNames index), null for an old two-drawing site (tests, previews).
    public Sprite[] LaunchStrip(int kind) { return siteLaunch != null && kind >= 0 && kind < siteLaunch.Length ? siteLaunch[kind] : null; }
    public BackdropPool Ceiling => ceiling;
    public BackdropPool GustSheetPool => gusts;
    public BackdropPool Palls => palls;
    public BackdropPool Embers => embers;
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
        fireArt = set.Atlas("fires");
        siteArt = set.Atlas("sites");
        weather = set.Atlas("weather");
        lights = set.Atlas("lights");
        night = set.Spec.Night(set.Variant);
        EmberTuning.LightBoostNow = night ? EmberTuning.NightLightBoost : 1f;
        ambient = new AmbientEmitters(set, EmberAmbientCatalog.Table) { Wind = EmberTuning.Wind };
        planner = new GroundPlanner(set, "mid", GroundMask.Load("Ember", set.Variant), 96);

        dockArt = set.Atlas(EmberTuning.DocksAtlas);
        siteClosed = Sprites(siteArt, EmberTuning.SiteClosed);
        dockTints = DockGrade.Tints("Ember", set.Variant, EmberTuning.SiteClosed);
        siteOpen = Sprites(siteArt, EmberTuning.SiteOpen);
        // the launch-strip sites take their idle / open cells (and all six) from the docks atlas; without it they are simply never spawned
        siteLaunch = new Sprite[EmberTuning.SiteNames.Length][];
        for (int k = EmberTuning.FirstLaunchSite; k < siteLaunch.Length; k++)
        {
            var f = dockArt.Frames(EmberTuning.SiteNames[k]);
            if (f.Length < EmberTuning.LaunchCells) { siteClosed[k] = siteOpen[k] = null; continue; }
            siteLaunch[k] = f;
            siteClosed[k] = f[EmberTuning.LaunchIdle];
            siteOpen[k] = f[EmberTuning.LaunchOpen];
        }
        lampsOff = siteArt.Get("lights_off");
        lampsOn = siteArt.Get("lights_on");
        banks = weather.Frames("cloud_bank");
        wispArt = weather.Frames("cloud_wisp");
        mistArt = weather.Frames("mist");
        gustArt = weather.Frames("ashgust");
        pallArt = weather.Frames("smokepall");
        emberArt = weather.Frames("embers");

        // ground plane, back to front: mountains (-6), props (-3), eruptions (-2), sites (0). The landmarks and pipes of round 1 are retired
        // (2026-11-28): the living props are EmberProps.cs.
        fires = LandmarkPool("ground", 6, -2);
        sites = LandmarkPool("ground", 3);
        // Every pool's loops draw above EVERY pool's plates: a fire's plume rises through the ground, so a plate higher up the screen
        // must not slice it off flat (the "smoke cut off at the top" report).
        ambient.Rig(fires, 3);
        ambient.Rig(sites, 1);
        for (int i = 0; i < sites.items.Count; i++)
        {
            var go = new GameObject("lamps");
            go.transform.SetParent(sites.items[i].body, false);
            lamps[i] = go.AddComponent<SpriteRenderer>();
            lamps[i].sortingOrder = sites.items[i].sr.sortingOrder + 1;
            lamps[i].sprite = lampsOff;
            lamps[i].enabled = false;
            const float ppu = BackdropAtlas.PixelsPerUnit;
            float k = EmberTuning.LampsScale;
            var box = EmberAmbientCatalog.Piece("lights_off");
            float rowY = box != null && box.box != null && box.box.Length == 4 ? (box.box[1] + box.box[3]) * .5f : 128f;
            go.transform.localPosition = new Vector3((EmberTuning.LampsAt.x - 128f) / ppu,
                                                     (128f - EmberTuning.LampsAt.y) / ppu - (128f - rowY) / ppu * k, 0f);
            go.transform.localScale = new Vector3(k, k, 1f);
        }
        palls = Pool("palls", 4);
        pallFeet = new int[palls.items.Count];
        for (int i = 0; i < pallFeet.Length; i++) pallFeet[i] = -1;
        mist = Pool("mist", 3);
        wisps = Pool("wisps", 4);
        ceiling = Pool("ceiling", 26);
        gusts = Pool("gusts", EmberTuning.GustSheets + 1);
        embers = Pool("embers", EmberTuning.EmberCount);

        BuildProps();
        PlateMaterial = Grade(fires, "ground", 1f, EmberTuning.PlateFeather);
        Grade(sites, "ground", 1f, EmberTuning.PlateFeather);
        Grade(palls, "palls");
        Grade(mist, "mist");
        Grade(wisps, "wisps");
        CeilingMaterial = Grade(ceiling, "ceiling", EmberTuning.CeilingThicken, 0f, EmberTuning.CeilingLift, EmberTuning.CeilingSaturation);

        // the opening view: the ground already dressed, under a thick ceiling
        groundGap = Rand(EmberTuning.LoneGapMin, EmberTuning.LoneGapMax);
        siteGap = EmberTuning.SiteFirst;
        if (planner.Ready)
        {
            double t = planner.Travel;
            OpeningProps();            // (the mountains and props first: the eruptions find their own room among them)
            for (float y = -HalfH * .85f; y < HalfH * .8f; y += Rand(5.5f, 8f))
                SpawnCluster(t + y, t + y + 1.2f * EmberTuning.GroundZoom);
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
    public static float CeilingDensity(float t) { return BackdropCatalog.For("Ember").CeilingDensity(t); }

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
        if (ceilingTravel >= EmberTuning.CeilingGap && density > CloudCover.BankSpawnFloor)
        {
            ceilingTravel = 0f;
            SpawnBank(Rand(-HalfW * .8f, HalfW * .8f), float.NaN);
        }
        float air = set.Spec.CloudDensity();
        if (CloudCover.Tick(ref mistIn, dt, air, EmberTuning.MistEveryMin, EmberTuning.MistEveryMax, rng)) SpawnMist(float.NaN);
        if (CloudCover.Tick(ref wispIn, dt, air, EmberTuning.WispEveryMin, EmberTuning.WispEveryMax, rng)) SpawnWisp();
        if (gustTimer.Tick(dt, rng)) ForceGust();
        if (sheetsLeft > 0)
        {
            sheetIn -= dt;
            if (sheetIn <= 0f) { SpawnSheet(); sheetsLeft--; sheetIn = EmberTuning.GustStagger; }
        }

        StepProps(dt, scrolled);
        StepPinned(fires, dt);
        StepPinned(sites, dt);
        StepSites(dt);
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
            float low = Mathf.Lerp(EmberTuning.CeilingLowShare, 1f, y01 * y01 * (3f - 2f * y01));
            Color c = EmberTuning.CeilingTint;
            c.a = b.phase * density * low * set.Alpha;
            b.sr.color = c;
        }
        foreach (var g in gusts.items)
        {
            if (!g.active || !Drift(g, dt, v)) continue;
            float k = Mathf.Clamp01(g.age / Mathf.Max(.01f, g.life));
            if (k >= 1f) { Despawn(g); continue; }
            Tinted(g, EmberTuning.GustMaxAlpha * Mathf.Sin(k * Mathf.PI));
        }
        foreach (var s in embers.items)
        {
            if (!s.active) continue;
            Recycle(s, .18f, dt, v, .35f);
            Tinted(s, EmberTuning.EmberAlpha);
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
            Tinted(p, 1f);
        }
    }

    // --------------------------------------------------------------- ground --

    // An eruption cluster (or a launch site, when one is due) entering at the top.
    // Returns the scrolled ground distance to wait before the next, 0 when
    // nothing fitted.
    float SpawnGround()
    {
        double t = planner.Travel;
        double lo = t + HalfH + EmberTuning.SpawnAbove, hi = lo + EmberTuning.SpawnBand;
        if (siteTravel >= siteGap && SpawnSite(float.NaN))
        {
            siteTravel = 0f;
            siteGap = Rand(EmberTuning.SiteGapMin, EmberTuning.SiteGapMax);
            return Rand(EmberTuning.LoneGapMin, EmberTuning.LoneGapMax);
        }
        int n = SpawnCluster(lo, hi);
        if (n == 0) return 0f;
        return n > 1 ? Rand(EmberTuning.ClusterGapMin, EmberTuning.ClusterGapMax)
                     : Rand(EmberTuning.LoneGapMin, EmberTuning.LoneGapMax);
    }

    float XMin(float size) { return -EdgeX + size * .3f; }
    float XMax(float size) { return EdgeX - size * .3f; }

    int PickCluster()
    {
        var w = EmberTuning.ClusterWeights[Mathf.Clamp(set.Variant, 0, EmberTuning.ClusterWeights.Length - 1)];
        int total = 0;
        for (int i = 0; i < w.Length; i++) if (i != lastCluster) total += w[i];
        int r = rng.Next(Mathf.Max(1, total));
        for (int i = 0; i < w.Length; i++)
        {
            if (i == lastCluster || w[i] <= 0) continue;
            r -= w[i];
            if (r < 0) return i;
        }
        return EmberTuning.ClEruption;
    }

    // Grows one eruption cluster (or a lava pool) with its anchor in the ground band [lo, hi]; returns
    // how many pieces it placed (0: nothing fitted).
    public int SpawnCluster(double lo, double hi, int kind = -1)
    {
        if (kind < 0) kind = PickCluster();
        int n = kind == EmberTuning.ClLava ? LavaPool(lo, hi) : Eruption(lo, hi);
        if (n == 0) n = kind == EmberTuning.ClLava ? Eruption(lo, hi) : LavaPool(lo, hi);
        if (n > 0) { lastCluster = kind; Clusters++; }
        return n;
    }

    // Draws `name` from its atlas at x, gy (pinned), claiming its footprint: the WHOLE drawn cell (the plume rises through it), and
    // never over a prop (the planner holds the props' keep-out boxes too).
    BackdropPiece Put(BackdropPool pool, BackdropAtlas atlas, string name, float size, float x, double gy, bool mirror)
    {
        var sprite = atlas.Get(name);
        if (sprite == null) return null;
        var rule = EmberTuning.Rule(name);
        float rx = pool == fires ? size * EmberTuning.FireKeepShare : GroundPlanner.HalfX(rule, size);
        float ry = pool == fires ? size * EmberTuning.FireKeepShare : GroundPlanner.HalfY(rule, size);
        int foot = planner.Claim(x, gy, rx, ry);
        if (foot < 0) return null;
        var p = pool.Spawn();
        if (p == null) { planner.Release(foot); return null; }
        SetSprite(p, sprite, Mathf.Min(size, BackdropCatalog.MaxLandmarkSize));
        p.body.localScale = mirror ? new Vector3(-1f, 1f, 1f) : Vector3.one;
        p.footprint = foot;
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

    // The footprint `name` would claim at `size` (a fire: the whole cell; a site: its rule's core).
    float KeepHalf(string name, float size, bool fire)
    {
        return fire ? size * EmberTuning.FireKeepShare : GroundPlanner.HalfX(EmberTuning.Rule(name), size);
    }

    // A spot for `name` (fire: its whole cell must be free; the ground rule is read on the rule's core).
    bool Spot(string name, float size, double lo, double hi, bool fire, out float x, out double gy)
    {
        var rule = EmberTuning.Rule(name);
        float k = KeepHalf(name, size, fire);
        for (int t = 0; t < 28; t++)
        {
            float cx = Rand(XMin(size), XMax(size));
            double cy = lo + rng.NextDouble() * (hi - lo);
            if (!planner.Free(cx, cy, k, k) || !planner.Accepts(rule, cx, cy, size)) continue;
            x = cx; gy = cy;
            return true;
        }
        x = 0f; gy = 0.0;
        return false;
    }

    // A lava fountain / eruption scar with coal beds and flare stacks beside
    // it (downwind first) and an ash pall hanging over it.
    int Eruption(double lo, double hi)
    {
        string front = Pick(EmberTuning.Fronts);
        float sf = Rand(EmberTuning.FireMin, EmberTuning.FireMax);
        float xf; double gf;
        if (!Spot(front, sf, lo, hi, true, out xf, out gf)) return 0;
        var f = Put(fires, fireArt, front, sf, xf, gf, false);
        if (f == null) return 0;
        int n = 1, want = 1 + rng.Next(2);
        float wind = EmberTuning.Wind;
        for (int k = 0; k < 6 && n <= want; k++)
        {
            string sat = Pick(EmberTuning.Satellites);
            float ss = Rand(EmberTuning.SatelliteMin, EmberTuning.SatelliteMax);
            float dir = k % 2 == 0 ? wind : -wind;
            float xs = xf + dir * Rand(.75f, 1.15f) * (sf + ss) * .5f;
            double gs = gf + Rand(-.7f, .5f) * EmberTuning.PieceScale;
            if (xs < XMin(ss) || xs > XMax(ss)) continue;
            var rule = EmberTuning.Rule(sat);
            float ks = KeepHalf(sat, ss, true);
            if (!planner.Accepts(rule, xs, gs, ss) || !planner.Free(xs, gs, ks, ks)) continue;
            if (Put(fires, fireArt, sat, ss, xs, gs, Chance(.5)) != null) n++;
        }
        SpawnPall(f);
        return n;
    }

    // A lava pool, channel or fire break in the lava.
    int LavaPool(double lo, double hi)
    {
        string pool = Pick(EmberTuning.Pools);
        float sp = Rand(EmberTuning.FireMin, EmberTuning.FireMax);
        float x; double gy;
        if (Spot(pool, sp, lo - .4f * EmberTuning.GroundZoom, hi + 1f * EmberTuning.GroundZoom, true, out x, out gy) && Put(fires, fireArt, pool, sp, x, gy, Chance(.5)) != null) return 1;
        return 0;
    }

    // A smoke pall over a fire: rides the fire's ground spot (pinned),
    // rising slowly and drifting downwind as it ages, fading in and out.
    void SpawnPall(BackdropPiece fire)
    {
        if (pallArt.Length == 0) return;
        var p = palls.Spawn();
        if (p == null) return;
        SetSprite(p, Pick(pallArt), Rand(2.2f, 3f) * EmberTuning.PieceScale);
        if (Chance(.5)) p.body.localScale = new Vector3(-1f, 1f, 1f);
        p.gy = fire.gy + fire.size * .35f;
        p.x = fire.x + EmberTuning.Wind * fire.size * .3f;
        p.vx = EmberTuning.Wind * Rand(.03f, .06f);
        p.vy = Rand(.02f, .05f);
        p.life = EmberTuning.PallLife;
        // the pall is a MOVER: it drifts up and downwind over its life, so props keep out of the whole box it will sweep (claimed on
        // the planner while it hangs); a pall that would meet a prop is not drawn at all
        float reach = p.size * .5f + EmberTuning.PallSweep;
        int slot = palls.items.IndexOf(p);
        double cy = p.gy + p.vy * p.life * .5;
        float cx = p.x + p.vx * p.life * .5f;
        if (!planner.Free(cx, cy, reach, reach)) { Despawn(p); return; }
        pallFeet[slot] = planner.Claim(cx, cy, reach, reach);
        if (pallFeet[slot] < 0) { Despawn(p); return; }
        p.rate = set.Spec.Rate("ground");
        p.phase = EmberTuning.PallAlpha * Rand(.8f, 1f);
        p.y = planner.Y(p.gy);
        Place(p);
        Tinted(p, 0f);
    }

    void ReleasePall(BackdropPiece p)
    {
        int slot = palls.items.IndexOf(p);
        if (slot < 0 || pallFeet[slot] < 0) return;
        planner.Release(pallFeet[slot]);
        pallFeet[slot] = -1;
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
            if (k >= 1f || p.y < -HalfH - p.size) { ReleasePall(p); Despawn(p); continue; }
            Tinted(p, p.phase * Mathf.Min(1f, Mathf.Min(k * 6f, (1f - k) * 4f)));
        }
    }

    // Preview / tests: clears the ground plane and shows `name` alone at
    // screen x, y (pinned there), size `size`, its rule ignored.
    public BackdropPiece Showcase(string name, float size, float x, float y, bool mirror = false)
    {
        foreach (var pool in new[] { fires, sites })
            foreach (var q in pool.items)
                if (q.active) { planner.Release(q.footprint); q.footprint = -1; Despawn(q); }
        foreach (var q in palls.items) if (q.active) { ReleasePall(q); Despawn(q); }
        ClearProps();
        bool fire = fireArt.Has(name), site = siteArt.Has(name);
        if (!fire && !site) return null;
        var p = Put(fire ? fires : sites, fire ? fireArt : siteArt, name, size, x, planner.GroundY(y), mirror);
        if (p != null && fire) SpawnPall(p);
        groundTravel = -1000f;     // nothing else spawns while it is shown
        siteTravel = -1000f;
        return p;
    }

    // Tests: stop dealing eruptions (a gap that never ends); every eruption and ash pall gone, their ground released
    public void HoldGround(float gap) { groundGap = gap; groundTravel = 0f; }
    public void ClearEruptions()
    {
        foreach (var q in fires.items)
            if (q.active) { planner.Release(q.footprint); q.footprint = -1; Despawn(q); }
        foreach (var q in palls.items) if (q.active) { ReleasePall(q); Despawn(q); }
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
            foreach (int w in EmberTuning.SiteWeights) total += w;
            int r = rng.Next(total);
            for (kind = 0; kind < EmberTuning.SiteWeights.Length - 1; kind++)
            {
                r -= EmberTuning.SiteWeights[kind];
                if (r < 0) break;
            }
        }
        var closed = siteClosed[kind];
        if (closed == null || siteOpen[kind] == null) return false;
        float size = Rand(EmberTuning.SiteMin, EmberTuning.SiteMax) * EmberTuning.SiteWidthK[kind];
        double lo = float.IsNaN(y) ? planner.Travel + HalfH + .95f : planner.GroundY(y) - .3f;
        double hi = lo + (float.IsNaN(y) ? .9f : .6f);
        float x; double gy;
        if (!planner.Find(EmberTuning.DockGround, size, XMin(size), XMax(size), lo, hi, rng, out x, out gy) &&
            !planner.Find(EmberTuning.DockGroundRelaxed, size, XMin(size), XMax(size), lo, hi, rng, out x, out gy) &&
            (DockGrade.Strict || !planner.Find(new PieceRule(GroundClass.Any, 0f), size, XMin(size), XMax(size), lo, hi, rng, out x, out gy))) return false;   // solid crust only, never lava
        var p = Put(sites, siteLaunch[kind] != null ? dockArt : siteArt, EmberTuning.SiteClosed[kind], size, x, gy, false);   // (doors face the viewer: never mirrored)
        if (p == null) return false;
        p.dockTint = dockTints[kind];
        Tinted(p, 1f);
        p.kind = kind;
        p.tier = 0;
        p.life = 0f;
        int i = sites.items.IndexOf(p);
        if (i >= 0 && lamps[i] != null) { lamps[i].sprite = lampsOff; lamps[i].enabled = lampsOff != null && siteLaunch[kind] == null; }   // (a strip site has no lamp row)
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
            if (ph > 0f && ph < 2f) return EmberTuning.LaunchCellAt(ph);
            return p.tier == 1 ? EmberTuning.LaunchOpening : EmberTuning.LaunchHold;
        }
        if (p.tier == 3) return p.life > EmberTuning.LaunchCloseSeconds ? EmberTuning.LaunchHold : EmberTuning.LaunchClosing;
        return EmberTuning.LaunchIdle;
    }

    void StepSites(float dt)
    {
        for (int i = 0; i < sites.items.Count; i++)
        {
            var p = sites.items[i];
            if (!p.active) continue;
            int launch = LaunchState(p);
            if (launch > 0) { p.tier = launch; p.life = EmberTuning.SiteCloseDelay; }
            else if (p.tier > 0)
            {
                p.life -= dt;
                p.tier = p.life <= 0f ? 0 : 3;
            }
            bool open = p.tier > 0;
            var strip = siteLaunch[p.kind];
            var want = strip != null ? strip[LaunchCell(p)] : open ? siteOpen[p.kind] : siteClosed[p.kind];
            if (p.sr.sprite != want) p.sr.sprite = want;
            var lamp = lamps[i];
            if (strip != null) { if (lamp != null) lamp.enabled = false; continue; }
            if (lamp != null && lampsOn != null)
            {
                bool lit = p.tier == 2 || p.tier == 3 || (p.tier == 1 && Mathf.Repeat(p.age * 4f, 1f) < .55f);
                lamp.sprite = lit ? lampsOn : lampsOff;
                lamp.color = new Color(1f, 1f, 1f, set.Alpha);
            }
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
            if (p.size < EmberTuning.SiteMinSize) continue;
            if (p.y < -HalfH * .15f || p.y > HalfH - p.size * .3f) continue;
            if (Mathf.Abs(p.x) > HalfW - .25f) continue;
            Bounds b = p.sr.sprite.bounds;
            into.Add(new LandingSite
            {
                anchor = p.root,
                local = new Vector3(b.center.x + EmberTuning.SitePads[p.kind].x * b.size.x, b.center.y + EmberTuning.SitePads[p.kind].y * b.size.y, 0f),   // the measured emergence point: the cell centre (the strip sites: their pad pixel)
                scale = Mathf.Clamp(p.size * EmberTuning.EmergeScalePerUnit, EmberTuning.EmergeScaleMin, EmberTuning.EmergeScaleMax),
                order = p.sr.sortingOrder + 2,
                id = EmberTuning.SiteIdBase + i,
                kind = EmberTuning.SiteKinds[p.kind],
                emerge = true,
            });
        }
    }

    // The elite director wants a site of one of `launchFrom`'s kinds in view soon (LandingSites.BringHome), as Frost's: the ground is
    // fixed, so none is conjured -- but the site spawner is asked for one of that kind at the top of the view now (it scrolls in over the
    // next seconds, as the ground pieces always do), unless one of them is already in view or on its way. False when none of the kinds
    // is one of Ember's sites or there is no room for one on the ground.
    public override bool BringHome(string launchFrom)
    {
        if (sites == null || !planner.Ready || string.IsNullOrEmpty(launchFrom)) return false;
        var want = new List<int>(EmberTuning.SiteKinds.Length);   // (a rare call: one per elite)
        bool mine = false;      // (one of the kinds it names is a dock of this world, even if none can be placed here)
        foreach (string part in launchFrom.Split(','))
        {
            var k = LandingSite.KindOf(part.Trim());
            if (k == null) continue;
            for (int t = 0; t < EmberTuning.SiteKinds.Length; t++)
            {
                if (EmberTuning.SiteKinds[t] == k.Value) mine = true;
                if (EmberTuning.SiteKinds[t] == k.Value && siteClosed[t] != null && siteOpen[t] != null && !want.Contains(t)) want.Add(t);
            }
        }
        if (want.Count == 0 && mine && DockGrade.Strict)     // (none of the kinds it names has a dock here: any dock will do)
            for (int t = 0; t < EmberTuning.SiteKinds.Length; t++)
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
        SetSprite(b, Pick(banks), Rand(EmberTuning.CeilingMin, EmberTuning.CeilingMax));
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
        SetSprite(w, Pick(wispArt), Rand(EmberTuning.WispMin, EmberTuning.WispMax));
        if (Chance(.5)) w.body.localScale = new Vector3(-1f, 1f, 1f);
        w.x = Rand(-HalfW * .7f, HalfW * .7f);
        w.y = SpawnY(w.size);
        w.vx = EmberTuning.Wind * EmberTuning.WispDrift * Rand(.7f, 1.3f);
        w.rate = set.Spec.Rate("wisps");
        w.phase = Rand(EmberTuning.WispAlphaMin, EmberTuning.WispAlphaMax) * CloudCover.Alpha(set.Spec.CloudDensity());
        Place(w);
        Tinted(w, 0f);
    }

    void SpawnMist(float y)
    {
        if (mistArt.Length == 0) return;
        var m = mist.Spawn();
        if (m == null) return;
        SetSprite(m, Pick(mistArt), HalfW * 2f * Rand(.85f, 1.1f));
        if (Chance(.5)) m.body.localScale = new Vector3(-1f, 1f, 1f);
        m.x = Rand(-HalfW * .3f, HalfW * .3f);
        m.y = float.IsNaN(y) ? SpawnY(m.size * .5f) : y;
        m.vx = EmberTuning.Wind * EmberTuning.MistDrift * Rand(.6f, 1.4f);
        m.rate = set.Spec.Rate("mist");
        m.phase = Rand(EmberTuning.MistAlphaMin, EmberTuning.MistAlphaMax) * CloudCover.Alpha(set.Spec.CloudDensity());
        Place(m);
        Tinted(m, 0f);
    }

    public void ForceGust() { sheetsLeft = EmberTuning.GustSheets; sheetIn = 0f; Gusts++; }

    // One ash gust sheet: in from the upper left (with the wind), sweeping
    // across, faded in and out over its crossing.
    void SpawnSheet()
    {
        if (gustArt.Length == 0) return;
        var g = gusts.Spawn();
        if (g == null) return;
        SetSprite(g, Pick(gustArt), Rand(EmberTuning.GustMin, EmberTuning.GustMax));
        float dir = EmberTuning.Wind >= 0f ? 1f : -1f;
        g.x = -dir * (HalfW + g.size * .25f);
        g.y = Rand(HalfH * .1f, HalfH * .9f);
        float speed = EmberTuning.GustSpeed * Rand(.85f, 1.15f);
        g.vx = dir * speed;
        g.vy = -speed * .35f;
        g.life = (2f * HalfW + g.size * .6f) / speed;
        g.rate = set.Spec.Rate("gusts");
        Place(g);
        Tinted(g, 0f);
    }

    void BuildParticles()
    {
        foreach (var s in embers.items)
        {
            if (emberArt.Length == 0) break;
            s.Show(true);
            SetSprite(s, Pick(emberArt), Rand(EmberTuning.EmberMin, EmberTuning.EmberMax));
            s.x = Rand(-HalfW, HalfW);
            s.y = Rand(-HalfH, HalfH);
            s.phase = Rand(0f, 6.28f);
            s.rate = set.Spec.Rate("embers");
            Place(s);
        }
    }
}
