using System.Collections.Generic;
using UnityEngine;

// Frost's backdrop: the ship flies at atmosphere level just under the cloud
// ceiling, looking down on a frozen ocean, its coast and ice-bound industry.
// Nothing space-like: no stars, planets, stations or rocks.
//
//   GROUND     one of four tile sets per landing (FrostBackdropSelection):
//              sky / far / mid tiles and the drifting-floe flow strip.
//   LANDMARKS  rigs, platforms, refineries, relays, causeways, icebreakers,
//              cliffs, derricks, convoys (landmarks atlas) on one ground
//              plane PINNED to the mid tile and placed where its affinity
//              mask says they belong (GroundPlanner, FrostTuning.Rule): each
//              drawing stands on its own skirt of floes, so rigs, platforms
//              and icebreakers sit in the open-water leads, refineries,
//              relays and derricks in a lead or on a snow flat (never stacked
//              on a painted ice sheet or quay), the convoy drives on the sheet
//              ice, a cliff falls into water and a causeway spans water with
//              both deck ends on land; on the glacier (v4) everything stands
//              on the plateau tops, never over a crevasse. Footprints never
//              overlap; never the same family twice running, mixed per
//              variant (FrostTuning.FamilyWeights). Their ambient loops -- smoke,
//              flares, beacons, searchlights, windows, steam, geysers -- are
//              FrostAmbientCatalog's emitters.
//   SITES      the elites' launch sites on the same ground plane (sites
//              atlas): ice-shelf hangar, rig lift bay, relay pad ring,
//              crawler garage, silo hatch. Drawn shut; the launch tell opens
//              them (open drawing, lamp row blinking) while the elite
//              emerges; they close again behind it. Four more come with a
//              6-cell launch strip each (docks atlas): an ice-locked offshore
//              rig and an icebreaker deck in the leads, a cryo silo on the
//              snow flats, a glacier cave hangar on the ridges. They play
//              idle (the elite's one small blinking launch light is the tell:
//              EliteLaunchTell, drawn by the ship at the pad) -> doors opening
//              -> open -> closing through the launch (FrostTuning.LaunchCellAt)
//              -- never a flash, flare, halo or ring, and no blink cell of
//              the site's own.
//   WEATHER    the CLOUD CEILING (cloud banks thick over the view at the
//              start -- the planetfall drops through it -- held for
//              FrostTuning.CeilingHold s, then clearing fast, gone by
//              CeilingClearSeconds; see CloudCover), then only a light
//              scattering of cloud wisps and low mist drifting with the wind
//              (FrostTuning.CloudDensity), snow fields at three depths, and BLIZZARD gusts:
//              translucent diagonal sheets sweeping across every so often.
//   AURORA     a faint animated overlay; strong only over the glacier night.
//
// Everything sits far below gameplay (BackdropCatalog.BaseOrder), so no
// weather ever covers an enemy or a bullet; the pools are fixed and a frame
// allocates nothing.
public static class FrostTuning
{
    // ---- brightness: THE knob -------------------------------------------
    // The art is painted dark (backdrop forms at HSV value <= .33); it is
    // lifted at draw time (BackdropGrade.shader) so the level reads brighter
    // without regrading a PNG. 1 = as painted; 1.7 lifts the ground's
    // midtones by ~+50% (shadows x1.7, highlights rolling off below white,
    // hue kept). The sky / far layers, mist, wisps, aurora and plumes take a
    // share of it (BackdropCatalog's .Graded); lights are never lifted.
    // "A bit brighter / darker" = change this one number. History: 1.7 (user,
    // 2026-10-08 "too dark"), 1.95 (2026-10-11 "too night-timey"). Ceiling: the
    // drawn-art caps in WorldBackdropTest (value p90 .55, tile luminance .24,
    // chroma .26) hold up to ~1.95; 2.3 = +15% frame luminance, 2.55 = +21%
    // need those caps raised (and Saturation eased to 1.0 for chroma).
    public static float Brightness = 1.95f;
    public static float Saturation = 1.05f;        // nudge on the lifted tones so the ice stays blue-cyan
    public static float PlumeShare = .6f;          // smoke / steam loops' share of the lift
    // The cloud ceiling's banks are painted translucent (alpha ~.28 at their
    // core); this thickens them (alpha a drawn as 1 - (1 - a)^k) so the
    // arrival reads as just having dropped out of the planetfall's bright
    // cloud deck (FrostBackdropTest holds it to the deck's brightness).
    public static float CeilingThicken = 7f;
    // ... and pales them toward the deck's white-blue (saturation x this).
    public static float CeilingSaturation = .7f;

    // ---- ground plane ----
    public static float LandmarkMin = 1.45f, LandmarkMax = 1.75f;     // world units (a 256 cell, ~1:1 on a phone)
    // scrolled ground distance between spawns. (The old spacing-only spawn also waited for the top
    // of the view to clear; the planner only keeps footprints apart, so the gap carries the density:
    // ~ the same number of landmarks in view as before, 2026-10-11.)
    public static float GapMin = .8f, GapMax = 2.4f;
    public static float SiteMin = 1.5f, SiteMax = 1.7f;
    public static float SiteFirst = 3.2f;                              // ground distance before the first site
    public static float SiteGapMin = 5f, SiteGapMax = 8.5f;
    public static float SiteCloseDelay = 1.4f;                         // seconds a site stays open after its elite left
    public static float SiteMinSize = 1.2f;
    // landmark families and their mix per variant (index 0 = no variant)
    public static readonly string[][] Families =
    {
        new[] { "rig_00", "rig_01" },
        new[] { "platform_00", "platform_01", "platform_02" },
        new[] { "refinery_00", "refinery_01" },
        new[] { "relay_00", "relay_01" },
        new[] { "causeway_00" },
        new[] { "icebreaker_00", "icebreaker_01" },
        new[] { "cliff_00", "cliff_01" },
        new[] { "derrick_00" },
        new[] { "convoy_00" },
    };
    public const int FamRig = 0, FamPlatform = 1, FamCauseway = 4, FamIcebreaker = 5, FamCliff = 6, FamConvoy = 8;
    // v4 (glacier) has no open water: its dark mask class is the crevasse
    // depths, so nothing that floats (icebreaker) or stands in the sea (rig,
    // platform) is drawn there -- it would hang over a chasm.
    public static readonly int[][] FamilyWeights =
    {
        new[] { 3, 3, 1, 1, 1, 3, 1, 1, 0 },   // (none) = frozen ocean
        new[] { 3, 3, 1, 1, 1, 3, 1, 1, 0 },   // v1 frozen ocean: rigs, platforms, icebreakers
        new[] { 1, 1, 2, 1, 2, 3, 3, 0, 1 },   // v2 coast + harbour
        new[] { 0, 1, 3, 3, 2, 0, 1, 3, 3 },   // v3 inland tundra industry
        new[] { 0, 0, 1, 3, 1, 0, 3, 2, 2 },   // v4 glacier / crevasse night
    };

    // ---- WHERE A PIECE MAY STAND (GroundPlanner; masks from
    // Art/Worlds/Frost/backdrop_v3/src~/ground_masks.py, letters: W open
    // water / harbour basin / frozen pond / crevasse, B painted quays, roads,
    // plants, O snow flats / glacier plateau tops, C ice sheet / pack /
    // ridge). Every landmark is drawn on its own skirt of floes and snow, an
    // island: it reads right with water round it, never stacked on top of a
    // painted ice sheet or quay. Variant v4 (Glacier) has no sea: its "water"
    // is the crevasse depths, so there the islands stand on the plateau tops.
    public static readonly bool[] Glacier = { false, false, false, false, true };   // index = variant (0 none)
    static readonly PieceRule Floating = new PieceRule(GroundClass.Water, .8f, .26f);                         // icebreakers: open water
    static readonly PieceRule Offshore = new PieceRule(GroundClass.Water, .6f, .34f);                         // rigs, platforms, rig bay
    static readonly PieceRule Island = new PieceRule(GroundClass.Water | GroundClass.Open, .72f, .34f);       // refinery, relay, derrick, sites: in a lead or on a snow flat, never on a sheet / quay
    static readonly PieceRule IceRoad = new PieceRule(GroundClass.Canopy | GroundClass.Open, .72f, .26f);     // the convoy drives over the sheet ice
    static readonly PieceRule Shore = new PieceRule(GroundClass.Open | GroundClass.Canopy, .55f, .28f, GroundClass.Water);   // an ice cliff: ice / snow falling into water (never on a quay)
    static readonly PieceRule Flats = new PieceRule(GroundClass.Open, .72f, .34f);                            // a cryo silo: squat on a snow flat
    static readonly PieceRule Ridge = new PieceRule(GroundClass.Canopy, .7f, .34f);                           // a glacier cave hangar: cut into the ridge / pack ice
    static readonly PieceRule Plateau = new PieceRule(GroundClass.Open | GroundClass.Canopy, .85f, .34f);     // v4: on the plateau, never over a crevasse
    // the causeway: over water, both landfalls on land. Its two landfalls (the
    // snow shelves its abutments stand on) as a share of its drawn width from
    // the centre, y up (the landfall causeway_00, Codex 2026-10-11: shelves
    // centred ~(45, 80) and ~(205, 156) of the 256 cell). Never mirrored.
    public static readonly PieceRule Span = new PieceRule(GroundClass.Water, .3f, .34f, GroundClass.Land)
        .WithEnds(new Vector2(-.32f, .19f), new Vector2(.30f, -.11f), GroundClass.Land, .12f);
    static readonly PieceRule Anywhere = new PieceRule(GroundClass.Any, 0f, .2f);

    // v3 (inland tundra) is mostly painted plant and roads with frozen ponds
    // and snow fields between: there an island may stand on any ground that is
    // not painted industry (its snow skirt on the snow field).
    public static readonly bool[] Inland = { false, false, false, true, false };
    static readonly PieceRule Unbuilt = new PieceRule(GroundClass.Water | GroundClass.Open | GroundClass.Canopy, .8f, .34f);
    // a launch site that finds no ground of its own kind: anywhere not painted industry
    public static readonly PieceRule SiteRelaxed = new PieceRule(GroundClass.Water | GroundClass.Open | GroundClass.Canopy, .6f, .3f);

    public static bool IsGlacier(int variant) { return variant >= 0 && variant < Glacier.Length && Glacier[variant]; }
    public static bool IsInland(int variant) { return variant >= 0 && variant < Inland.Length && Inland[variant]; }

    public static PieceRule Rule(string piece, int variant = 1)
    {
        if (piece == null) return Anywhere;
        if (piece.StartsWith("causeway")) return Span;
        if (piece.StartsWith("cliff")) return Shore;
        if (piece.StartsWith("convoy")) return IceRoad;
        if (IsGlacier(variant)) return Plateau;
        if (piece.StartsWith("icebreaker")) return Floating;       // (the icebreaker deck site's cells too: icebreaker_00..05)
        if (piece.StartsWith("rig_") || piece.StartsWith("platform") || piece.StartsWith("rigbay")) return Offshore;   // (the offshore rig site: rig_00..05)
        if (piece.StartsWith("silo_")) return Flats;
        if (piece.StartsWith("cave_")) return Ridge;
        return IsInland(variant) ? Unbuilt : Island;     // refinery, relay, derrick and the other launch sites
    }

    // launch sites: drawings (closed, open) and how often each comes
    // Entries 0-4 are the old two-drawing sites (sites atlas); 5-8 the launch-strip ones (docks atlas: <name>_00..05, DocksAtlas), for which
    // SiteClosed / SiteOpen name the idle and the open cell (the rule is picked by the name: Rule).
    public static readonly string[] SiteNames = { "hangar", "rigbay", "padring", "crawlerbay", "hatch", "rig", "silo", "cave", "icebreaker" };
    public static readonly string[] SiteClosed = { "hangar_closed", "rigbay_closed", "padring_idle", "crawlerbay_closed", "hatch_closed", "rig_00", "silo_00", "cave_00", "icebreaker_00" };
    public static readonly string[] SiteOpen = { "hangar_open", "rigbay_open", "padring_active", "crawlerbay_open", "hatch_open", "rig_03", "silo_03", "cave_03", "icebreaker_03" };
    public static readonly LandingKind[] SiteKinds = { LandingKind.Hangar, LandingKind.RigBay, LandingKind.PadRing, LandingKind.CrawlerBay, LandingKind.Hatch,
                                                       LandingKind.Rig, LandingKind.Silo, LandingKind.Cave, LandingKind.Icebreaker };
    public static readonly int[] SiteWeights = { 3, 2, 2, 3, 2, 2, 2, 2, 2 };
    // where the elite comes out: fractions of the drawing's bounds from its centre (y up). The launch-strip sites' are the pad pixels of
    // Codex's masters (build_art.py: .5 across; .5 / .519 / .4375 / .514 down the 768 px cell): the dark bay the elite rises from.
    public static readonly Vector2[] SitePads =
    {
        new Vector2(0f, -.05f), new Vector2(0f, -.03f), new Vector2(0f, 0f), new Vector2(0f, -.04f), new Vector2(0f, -.02f),
        new Vector2(0f, 0f), new Vector2(0f, -.0195f), new Vector2(0f, .0625f), new Vector2(0f, -.0143f),
    };

    // ---- the launch strips (the docks atlas): idle, blink, opening, open, open hold, closing ----
    public const string DocksAtlas = "docks";
    public const int LaunchCells = 6, LaunchIdle = 0, LaunchBlink = 1, LaunchOpening = 2, LaunchOpen = 3, LaunchHold = 4, LaunchClosing = 5;
    public const int FirstLaunchSite = 5;                      // SiteNames[FirstLaunchSite..] have a strip
    // The rig and the icebreaker deck are drawn in a frozen lead: the glacier (v4) has no sea, so they never come there (nothing that floats
    // is drawn over its crevasses: FrostBackdropTest); its silo and cave, and the old five, do.
    public static bool SiteFits(int kind, int variant) { return !(IsGlacier(variant) && (SiteKinds[kind] == LandingKind.Rig || SiteKinds[kind] == LandingKind.Icebreaker)); }
    public static bool HasLaunchStrip(int kind) { return kind >= FirstLaunchSite && kind < SiteNames.Length; }
    // drawn width of a strip site, as a share of the usual SiteMin..SiteMax (a 192 px wide icebreaker cell is 3/4 of a 256 px one)
    public static readonly float[] SiteWidthK = { 1f, 1f, 1f, 1f, 1f, 1f, 1f, 1f, .85f };
    // The tell: the site stays idle through its first share (the ship's own small blinking launch light, EliteShip.LaunchTell, is the
    // tell; the strip's lamp-blink cell 1 is never shown), then the doors open (2); the lift-off has them open (3 for its first share,
    // then 4 held until the elite is clear); then they close for LaunchCloseSeconds (5) and the site is idle again. No flash, flare,
    // halo or ring at any point (as SpaceDirector.DockCellAt after the launch-tell change).
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
    // the lamp row (lights_off / lights_on) under the door: cell point and scale
    public static Vector2 LampsAt = new Vector2(128f, 226f);
    public static float LampsScale = .6f;
    public const float EmergeScalePerUnit = .22f, EmergeScaleMin = .26f, EmergeScaleMax = .4f;
    public const int SiteIdBase = 500;

    // The weather atlas bakes its translucency into the art (cloud banks
    // peak at alpha ~.28, blizzard sheets ~.19, snow ~.35), so the draw
    // alphas below multiply that: 1 draws the art as painted.
    // ---- CLOUD COVER: the knobs (CloudCover documents the timeline) ----
    // How much cloud drifts over the ground once the ceiling has cleared:
    // 1 = a light scattering (default), 2 = about twice as much, 0 = none.
    public static float CloudDensity = CloudCover.Density;
    public static float CeilingHold = CloudCover.CeilingHold;                   // s at full thickness after landing
    public static float CeilingClearSeconds = CloudCover.CeilingClearSeconds;   // s: the ceiling is gone
    public static float Wind = -1f;                // the level wind: -1 blows toward the left, with the blizzard
    // ---- the cloud ceiling ----
    public static float CeilingMin = 4.2f, CeilingMax = 5.6f;
    public static float CeilingAlpha = 1f;
    public static float CeilingGap = 1.5f;         // scrolled distance between new banks while it lasts
    public static float CeilingLowShare = .55f;    // alpha share left at the bottom of the view (the ceiling is above)
    // ---- air ----
    // wisps and mist at CloudDensity 1 (density divides the gaps, lifts the alpha)
    public static float WispMin = 2.2f, WispMax = 3.2f, WispAlphaMin = .45f, WispAlphaMax = .65f;
    public static float WispEveryMin = 6f, WispEveryMax = 10f, WispDrift = .18f;
    public static float MistAlphaMin = .35f, MistAlphaMax = .5f;
    public static float MistEveryMin = 14f, MistEveryMax = 22f, MistDrift = .08f;
    // snow fields at three depths: size, alpha, count, fall speed
    public static readonly float[] SnowSize = { 2.0f, 2.6f, 3.2f };
    public static readonly float[] SnowAlpha = { .6f, .65f, .45f };
    public static readonly int[] SnowCount = { 7, 5, 4 };
    public static readonly float[] SnowFall = { .25f, .45f, .8f };
    // ---- blizzard gusts ----
    public static float BlizzardFirst = 11f, BlizzardEveryMin = 13f, BlizzardEveryMax = 21f;
    public static int BlizzardSheets = 4;
    public static float BlizzardStagger = .45f;
    public static float BlizzardSpeed = 4.8f;
    public static float BlizzardMaxAlpha = 1f;
    // Optional: gust sheets add light (BackdropAdditive) instead of
    // alpha-blending (off: the brightness pass decides).
    public static bool BlizzardAdditive = false;
    public static float BlizzardMin = 5f, BlizzardMax = 7f;
    public static float BlizzardWind = 1.6f;       // sideways push given to the snow while a gust blows
}

public class FrostDirector : PlanetDirector
{
    BackdropPool ground, sites, auroraPool, mist, wisps, ceiling, blizzard;
    readonly BackdropPool[] snow = new BackdropPool[3];
    readonly SpriteRenderer[] lamps = new SpriteRenderer[3];
    BackdropAtlas landmarkArt, siteArt, dockArt, weather;
    Sprite[][] siteLaunch;                             // per site kind: its 6 launch cells (null: an old two-drawing site, or the atlas is missing)
    AmbientEmitters ambient;
    Sprite[] auroraFrames;
    Sprite[] siteClosed, siteOpen, banks, wispArt, mistArt, gustArt, snowArt;
    Sprite lampsOff, lampsOn;
    Color tintGround, tintAurora, tintMist, tintWisps, tintCeiling, tintBlizzard;
    readonly Color[] tintSnow = new Color[3];
    float auroraAlpha;

    float groundTravel, groundGap, siteTravel, siteGap, ceilingTravel;
    int lastFamily = -1;
    GroundPlanner planner;
    // the rule each pooled piece was placed by (index = its pool slot), so a
    // ship under way can check the ground ahead without a name lookup
    PieceRule[] groundRules, siteRules;
    float mistIn = 2.5f, wispIn = 6f;
    readonly Timer auroraTimer = new Timer(10f, 18f, 1.5f);
    readonly Timer gustTimer = new Timer(FrostTuning.BlizzardEveryMin, FrostTuning.BlizzardEveryMax, FrostTuning.BlizzardFirst);
    int sheetsLeft;
    float sheetIn, wind;
    Material additive;
    readonly List<Material> grades = new List<Material>();

    public override void Teardown()
    {
        BackdropAtlas.Kill(additive);
        additive = null;
        foreach (var m in grades) BackdropAtlas.Kill(m);
        grades.Clear();
        if (ambient != null) ambient.Destroy();
    }

    // Draws `pool` brightened by its layer's share of FrostTuning.Brightness
    // (BackdropGrade), its alpha thickened by `alphaLift`.
    Material Grade(BackdropPool pool, string layer, float alphaLift = 1f, float satScale = 1f)
    {
        float lift = BackdropGrade.Lift(set.Spec, set.Spec.Find(layer));
        var m = BackdropGrade.Create(pool.name, lift, BackdropGrade.Saturation(set.Spec, lift) * satScale, alphaLift);
        if (m == null) return null;
        grades.Add(m);
        foreach (var p in pool.items)
        {
            p.sr.sharedMaterial = m;
            if (p.blend != null) p.blend.sharedMaterial = m;
        }
        return m;
    }

    public Material CeilingMaterial { get; private set; }

    // Tests: > 0 seeds the next directors' streams (statistics over many runs).
    public static int SeedOverride;
    public FrostDirector() : base(SeedOverride > 0 ? SeedOverride : 1989)
    {
        groundRng = new System.Random((SeedOverride > 0 ? SeedOverride : 1989) * 31 + 7);
    }

    // The ground plane (landmarks, sites) draws from its own stream, so how
    // many tries the placement takes never shifts the weather (ceiling banks,
    // wisps, gusts) and the reverse.
    readonly System.Random groundRng;
    float GRand(float a, float b) { return a + (float)groundRng.NextDouble() * (b - a); }
    bool GChance(double p) { return groundRng.NextDouble() < p; }
    T GPick<T>(T[] options) { return options[groundRng.Next(options.Length)]; }

    public float Clock => clock;
    public int Gusts { get; private set; }
    public int SitesSpawned { get; private set; }
    public AmbientEmitters Ambient => ambient;
    public BackdropPool Ground => ground;
    public BackdropPool Sites => sites;
    public GroundPlanner Planner => planner;
    public BackdropPool Ceiling => ceiling;
    public BackdropPool Blizzard => blizzard;
    public BackdropPool Aurora => auroraPool;

    // 1 while the ceiling is thick, falling fast to 0 at CeilingClearSeconds (CloudCover.Ceiling).
    public static float CeilingDensity(float t) { return BackdropCatalog.For("Frost").CeilingDensity(t); }

    // The share of the view the ceiling's banks cover, alpha-weighted (0..1).
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

    // The mean draw alpha of the banks in view (the thick ceiling covers
    // the whole view, so CeilingCover saturates at 1 until it thins).
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

    protected override void Build()
    {
        landmarkArt = set.Atlas("landmarks");
        siteArt = set.Atlas("sites");
        planner = new GroundPlanner(set, "mid", GroundMask.Load("Frost", set.Variant));
        weather = set.Atlas("weather");
        ambient = new AmbientEmitters(set);
        var spec = set.Spec;
        tintGround = spec.Tint("landmarks");
        tintAurora = spec.Tint("aurora");
        tintMist = spec.Tint("mist");
        tintWisps = spec.Tint("wisps");
        tintCeiling = spec.Tint("ceiling");
        tintBlizzard = spec.Tint("blizzard");

        dockArt = set.Atlas(FrostTuning.DocksAtlas);
        siteClosed = Sprites(siteArt, FrostTuning.SiteClosed);
        siteOpen = Sprites(siteArt, FrostTuning.SiteOpen);
        // the launch-strip sites take their idle / open cells (and all six) from the docks atlas; without it they are simply never spawned
        siteLaunch = new Sprite[FrostTuning.SiteNames.Length][];
        for (int k = FrostTuning.FirstLaunchSite; k < siteLaunch.Length; k++)
        {
            var f = dockArt.Frames(FrostTuning.SiteNames[k]);
            if (f.Length < FrostTuning.LaunchCells) { siteClosed[k] = siteOpen[k] = null; continue; }
            siteLaunch[k] = f;
            siteClosed[k] = f[FrostTuning.LaunchIdle];
            siteOpen[k] = f[FrostTuning.LaunchOpen];
        }
        lampsOff = siteArt.Get("lights_off");
        lampsOn = siteArt.Get("lights_on");
        banks = weather.Frames("cloud_bank");
        wispArt = weather.Frames("cloud_wisp");
        mistArt = weather.Frames("mist");
        gustArt = weather.Frames("blizzard");
        snowArt = weather.Frames("snow");
        auroraFrames = ambient.Frames(FrostAmbientCatalog.AuroraLoop);
        int v = Mathf.Clamp(set.Variant, 0, FrostAmbientCatalog.AuroraAlpha.Length - 1);
        auroraAlpha = FrostAmbientCatalog.AuroraAlpha[v];

        auroraPool = Pool("aurora", 2);
        ground = LandmarkPool("landmarks", 7);
        sites = LandmarkPool("landmarks", 3, 3);
        groundRules = new PieceRule[ground.items.Count];
        siteRules = new PieceRule[sites.items.Count];
        // loops above both pools' plates (the sites' sit 3 above the ground's): a
        // site plate must not slice a neighbouring stack's plume off flat
        ambient.Rig(ground, 4);
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
            // the lamp row's own centre sits ~150 px down its cell
            float k = FrostTuning.LampsScale;
            go.transform.localPosition = new Vector3((FrostTuning.LampsAt.x - 128f) / ppu,
                                                     (128f - FrostTuning.LampsAt.y) / ppu - (128f - 150f) / ppu * k, 0f);
            go.transform.localScale = new Vector3(k, k, 1f);
        }
        mist = Pool("mist", 3);
        wisps = Pool("wisps", 4);
        ceiling = Pool("ceiling", 26);
        string[] snowLayers = { "snow_far", "snow_mid", "snow_near" };
        for (int d = 0; d < 3; d++)
        {
            snow[d] = Pool(snowLayers[d], FrostTuning.SnowCount[d]);
            tintSnow[d] = spec.Tint(snowLayers[d]);
        }
        blizzard = Pool("blizzard", FrostTuning.BlizzardSheets + 1);
        Grade(auroraPool, "aurora");
        Grade(ground, "landmarks");
        Grade(sites, "landmarks");
        Grade(mist, "mist");
        Grade(wisps, "wisps");
        CeilingMaterial = Grade(ceiling, "ceiling", FrostTuning.CeilingThicken, FrostTuning.CeilingSaturation);
        if (!FrostTuning.BlizzardAdditive) Grade(blizzard, "blizzard");
        if (FrostTuning.BlizzardAdditive)
        {
            var shader = Resources.Load<Shader>(SpaceAsteroidDrift.AdditiveShader);
            if (shader != null)
            {
                additive = new Material(shader) { name = "FrostGust" };
                foreach (var g in blizzard.items) g.sr.sharedMaterial = additive;
            }
        }

        // the opening view: ground already in place, under a thick ceiling
        groundGap = GRand(FrostTuning.GapMin, FrostTuning.GapMax);
        siteGap = FrostTuning.SiteFirst;
        if (planner.Ready)
        {
            double t = planner.Travel;
            for (float y = -HalfH * .85f; y < HalfH * .8f; y += GRand(2.6f, 3.4f))
                SpawnLandmark(t + y, t + y + 1f);
        }
        BuildCeiling();
        BuildSnow();
        SpawnMist(Rand(-HalfH * .2f, HalfH * .4f));
    }

    static Sprite[] Sprites(BackdropAtlas atlas, string[] names)
    {
        var s = new Sprite[names.Length];
        for (int i = 0; i < names.Length; i++) s[i] = atlas.Get(names[i]);
        return s;
    }

    protected override void Step(float dt, float v)
    {
        float groundRate = set.Spec.Rate("landmarks");
        float scrolled = groundRate * v * dt;
        groundTravel += scrolled;
        siteTravel += scrolled;
        planner.Prune();
        if (planner.Ready && groundTravel >= groundGap)
        {
            if (SpawnGround()) { groundTravel = 0f; groundGap = GRand(FrostTuning.GapMin, FrostTuning.GapMax); }
            else groundGap = groundTravel + .25f;     // nothing fitted: look again a little further on
        }
        ceilingTravel += set.Spec.Rate("ceiling") * v * dt;
        float density = set.Spec.CeilingDensity(clock);
        if (ceilingTravel >= FrostTuning.CeilingGap && density > CloudCover.BankSpawnFloor)
        {
            ceilingTravel = 0f;
            SpawnBank(Rand(-HalfW * .8f, HalfW * .8f), float.NaN);
        }
        float air = set.Spec.CloudDensity();
        if (CloudCover.Tick(ref mistIn, dt, air, FrostTuning.MistEveryMin, FrostTuning.MistEveryMax, rng)) SpawnMist(float.NaN);
        if (CloudCover.Tick(ref wispIn, dt, air, FrostTuning.WispEveryMin, FrostTuning.WispEveryMax, rng)) SpawnWisp();
        if (auroraTimer.Tick(dt, rng)) SpawnAurora();
        if (gustTimer.Tick(dt, rng)) { sheetsLeft = FrostTuning.BlizzardSheets; sheetIn = 0f; Gusts++; }
        if (sheetsLeft > 0)
        {
            sheetIn -= dt;
            if (sheetIn <= 0f) { SpawnSheet(); sheetsLeft--; sheetIn = FrostTuning.BlizzardStagger; }
        }

        foreach (var a in auroraPool.items)
        {
            if (!a.active || !Drift(a, dt, v)) continue;
            a.Animate();
            float swell = .75f + .25f * Mathf.Sin(a.age * .5f + a.phase);
            Color c = tintAurora;
            c.a *= auroraAlpha * swell * set.Alpha * FrostAmbientCatalog.Brightness;
            a.sr.color = c;
        }
        StepPinned(ground, groundRules, dt);
        StepPinned(sites, siteRules, dt);
        StepSites(dt);
        ambient.Step(1f);

        foreach (var m in mist.items)
            if (m.active && Drift(m, dt, v)) Tinted(m, tintMist, m.phase);
        foreach (var w in wisps.items)
            if (w.active && Drift(w, dt, v)) Tinted(w, tintWisps, w.phase);
        foreach (var b in ceiling.items)
        {
            if (!b.active) continue;
            if (density <= 0f) { Despawn(b); continue; }
            b.x += Mathf.Sign(b.x == 0f ? 1f : b.x) * CloudCover.PartSpeed * (1f - density) * dt;    // parting from the middle
            if (!Drift(b, dt, v)) continue;
            float y01 = Mathf.Clamp01((b.y / HalfH + 1f) * .5f);
            float low = Mathf.Lerp(FrostTuning.CeilingLowShare, 1f, y01 * y01 * (3f - 2f * y01));
            Tinted(b, tintCeiling, b.phase * density * low);
        }

        wind = Mathf.MoveTowards(wind, 0f, dt * .8f);
        foreach (var g in blizzard.items)
        {
            if (!g.active || !Drift(g, dt, v)) continue;
            float k = Mathf.Clamp01(g.age / Mathf.Max(.01f, g.life));
            if (k >= 1f) { Despawn(g); continue; }
            float env = Mathf.Sin(k * Mathf.PI);
            wind = Mathf.Max(wind, env);
            Tinted(g, tintBlizzard, FrostTuning.BlizzardMaxAlpha * env);
        }
        for (int d = 0; d < 3; d++)
            foreach (var s in snow[d].items)
            {
                StepFlakes(s, d, dt, v);
                Tinted(s, tintSnow[d], FrostTuning.SnowAlpha[d]);
            }
    }

    void Tinted(BackdropPiece p, Color tint, float alpha)
    {
        tint.a *= alpha * set.Alpha;
        p.sr.color = tint;
    }

    // Pinned pieces: placed from their ground coordinate every frame, so they
    // stay on their spot of the mid tile. A ship under way (icebreaker,
    // convoy) moves its ground spot on its own, but only while the ground
    // ahead still suits it (open water for an icebreaker, sheet ice for the
    // convoy) and is free: it never sails onto a quay or into another piece.
    void StepPinned(BackdropPool pool, PieceRule[] rules, float dt)
    {
        for (int i = 0; i < pool.items.Count; i++)
        {
            var p = pool.items[i];
            if (!p.active) continue;
            p.age += dt;
            if (p.vx != 0f || p.vy != 0f) Steam(p, rules[i], dt);
            p.y = planner.Y(p.gy);
            Place(p);
            if (p.y < GroundDespawnY(p, ambient))
            {
                planner.Release(p.footprint);
                p.footprint = -1;
                Despawn(p);
                continue;
            }
            Tinted(p, tintGround, 1f);
        }
    }

    void Steam(BackdropPiece p, PieceRule rule, float dt)
    {
        float nx = p.x + p.vx * dt;
        double ngy = p.gy + p.vy * dt;
        float rx = GroundPlanner.HalfX(rule, p.size), ry = GroundPlanner.HalfY(rule, p.size);
        // look a little ahead (a quarter of the footprint) so it stops short, not on the edge
        float ax = nx + Mathf.Sign(p.vx) * (p.vx != 0f ? rx * .25f : 0f);
        double agy = ngy + System.Math.Sign(p.vy) * (p.vy != 0f ? ry * .25f : 0f);
        if (nx < XMin(p.size) || nx > XMax(p.size) || !planner.Accepts(rule, nx, ngy, p.size) || !planner.Accepts(rule, ax, agy, p.size) ||
            !planner.Free(nx, ngy, rx, ry, p.footprint))
        {
            p.vx = 0f;
            p.vy = 0f;
            return;
        }
        planner.Release(p.footprint);
        p.footprint = planner.Claim(nx, ngy, rx, ry);
        p.x = nx;
        p.gy = ngy;
    }

    // ------------------------------------------------------------ ground --

    float XMin(float size) { return -EdgeX + size * .3f; }
    float XMax(float size) { return EdgeX - size * .3f; }

    // A landmark (or, when one is due, a launch site) entering at the top.
    bool SpawnGround()
    {
        double lo = planner.Travel + HalfH + .95f, hi = lo + .9f;
        if (siteTravel >= siteGap && SpawnSite(float.NaN))
        {
            siteTravel = 0f;
            siteGap = GRand(FrostTuning.SiteGapMin, FrostTuning.SiteGapMax);
            return true;
        }
        return SpawnLandmark(lo, hi);
    }

    int PickFamily(int not)
    {
        var weights = FrostTuning.FamilyWeights[Mathf.Clamp(set.Variant, 0, FrostTuning.FamilyWeights.Length - 1)];
        int total = 0;
        for (int i = 0; i < weights.Length; i++) if (i != lastFamily && i != not) total += weights[i];
        if (total <= 0) return -1;
        int r = groundRng.Next(total);
        for (int i = 0; i < weights.Length; i++)
        {
            if (i == lastFamily || i == not || weights[i] <= 0) continue;
            r -= weights[i];
            if (r < 0) return i;
        }
        return -1;
    }

    // A landmark with its centre in the ground band [lo, hi], where the
    // ground under it suits its family (FrostTuning.Rule). A family that
    // fits nowhere in the band is swapped for another (up to three).
    // False when nothing fitted.
    bool SpawnLandmark(double lo, double hi)
    {
        int tried = -1;
        for (int k = 0; k < 3; k++)
        {
            int fam = PickFamily(tried);
            if (fam < 0) return false;
            tried = fam;
            string name = GPick(FrostTuning.Families[fam]);
            float size = GRand(FrostTuning.LandmarkMin, FrostTuning.LandmarkMax);
            var rule = FrostTuning.Rule(name, set.Variant);
            bool mirror = !rule.HasEnds && GChance(.5);
            float x; double gy;
            if (!Spot(rule, size, lo, hi, out x, out gy)) continue;
            var p = Put(ground, groundRules, landmarkArt.Get(name), rule, size, x, gy, mirror);
            if (p == null) return false;
            lastFamily = fam;
            p.kind = fam;
            // the ships under way move a little on their own
            if (fam == FrostTuning.FamIcebreaker) p.vy = GRand(.04f, .08f);
            else if (fam == FrostTuning.FamConvoy) { float dir = p.body.localScale.x; p.vx = .05f * dir; p.vy = .05f; }
            ambient.Attach(p, name, groundRng);
            return true;
        }
        return false;
    }

    // A spot for `rule` in the band (a causeway's rule also wants both
    // landfalls on land: PieceRule ends, checked by the planner).
    bool Spot(PieceRule rule, float size, double lo, double hi, out float x, out double gy)
    {
        return planner.Find(rule, size, XMin(size), XMax(size), lo, hi, groundRng, out x, out gy, rule.HasEnds ? 48 : 28);
    }

    // Draws `sprite` at x, gy (pinned), claiming its footprint.
    BackdropPiece Put(BackdropPool pool, PieceRule[] rules, Sprite sprite, PieceRule rule, float size, float x, double gy, bool mirror)
    {
        if (sprite == null) return null;
        float rx = GroundPlanner.HalfX(rule, size), ry = GroundPlanner.HalfY(rule, size);
        int foot = planner.Claim(x, gy, rx, ry);
        if (foot < 0) return null;
        var p = pool.Spawn();
        if (p == null) { planner.Release(foot); return null; }
        int i = pool.items.IndexOf(p);
        if (i >= 0) rules[i] = rule;
        SetSprite(p, sprite, Mathf.Min(size, BackdropCatalog.MaxLandmarkSize));
        p.body.localScale = mirror ? new Vector3(-1f, 1f, 1f) : Vector3.one;
        p.footprint = foot;
        p.core = rule.core;
        p.gy = gy;
        p.x = x;
        p.y = planner.Y(gy);
        p.vx = p.vy = 0f;
        p.rate = set.Spec.Rate("landmarks");
        p.color = tintGround;
        p.kind = 0;
        Place(p);
        Tinted(p, tintGround, 1f);
        return p;
    }

    // A launch site entering at the top (or at screen `y`), where its rule
    // fits (relaxed to "not on a painted quay", then anywhere free, so the
    // elite still gets its site). False when the pool is full, the art is
    // missing or nothing is free.
    public bool SpawnSite(float y, int kind = -1)
    {
        if (!planner.Ready) return false;
        if (kind < 0)
        {
            int total = 0;
            for (int k = 0; k < FrostTuning.SiteWeights.Length; k++) if (FrostTuning.SiteFits(k, set.Variant)) total += FrostTuning.SiteWeights[k];
            int r = groundRng.Next(total);
            for (kind = 0; kind < FrostTuning.SiteWeights.Length - 1; kind++)
            {
                if (!FrostTuning.SiteFits(kind, set.Variant)) continue;
                r -= FrostTuning.SiteWeights[kind];
                if (r < 0) break;
            }
        }
        if (!FrostTuning.SiteFits(kind, set.Variant)) return false;
        var closed = siteClosed[kind];
        if (closed == null || siteOpen[kind] == null) return false;
        float size = GRand(FrostTuning.SiteMin, FrostTuning.SiteMax) * FrostTuning.SiteWidthK[kind];
        double lo = float.IsNaN(y) ? planner.Travel + HalfH + .95f : planner.GroundY(y) - .3f;
        double hi = lo + (float.IsNaN(y) ? .9f : .6f);
        var rule = FrostTuning.Rule(FrostTuning.SiteClosed[kind], set.Variant);
        float x; double gy;
        if (!planner.Find(rule, size, XMin(size), XMax(size), lo, hi, groundRng, out x, out gy) &&
            !planner.Find(rule = FrostTuning.SiteRelaxed, size, XMin(size), XMax(size), lo, hi, groundRng, out x, out gy) &&
            !planner.Find(rule = new PieceRule(GroundClass.Any, 0f, FrostTuning.SiteRelaxed.core), size, XMin(size), XMax(size), lo, hi, groundRng, out x, out gy))
            return false;
        var p = Put(sites, siteRules, closed, rule, size, x, gy, false);     // doors face the viewer: never mirrored
        if (p == null) return false;
        p.kind = kind;
        p.tier = 0;
        p.life = 0f;
        ambient.Attach(p, SiteKey(kind), groundRng);
        int i = sites.items.IndexOf(p);
        if (i >= 0 && lamps[i] != null) { lamps[i].sprite = lampsOff; lamps[i].enabled = lampsOff != null && siteLaunch[kind] == null; }   // (a strip site has its own lamp drawn in)
        SitesSpawned++;
        return true;
    }

    static string SiteKey(int kind) { return FrostTuning.SiteClosed[kind]; }

    // The elite (if any) launching from site piece `p`: 2 lifting off, 1 in
    // its launch tell, 0 parked quietly / none.
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

    // The cell of a strip site: the launch tell and lift-off by the elite's phase, then closing
    // behind it for the last LaunchCloseSeconds of the close delay (p.tier 3, p.life counting down), held open before that.
    int LaunchCell(BackdropPiece p)
    {
        if (p.tier == 1 || p.tier == 2)
        {
            float ph = PhaseOf(p);
            if (ph > 0f && ph < 2f) return FrostTuning.LaunchCellAt(ph);
            return p.tier == 1 ? FrostTuning.LaunchOpening : FrostTuning.LaunchHold;
        }
        if (p.tier == 3) return p.life > FrostTuning.LaunchCloseSeconds ? FrostTuning.LaunchHold : FrostTuning.LaunchClosing;
        return FrostTuning.LaunchIdle;
    }

    void StepSites(float dt)
    {
        for (int i = 0; i < sites.items.Count; i++)
        {
            var p = sites.items[i];
            if (!p.active) continue;
            int launch = LaunchState(p);
            if (launch > 0) { p.tier = launch; p.life = FrostTuning.SiteCloseDelay; }
            else if (p.tier > 0)
            {
                p.life -= dt;
                if (p.life <= 0f) p.tier = 0;
                else p.tier = 3;         // closing behind it
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
                Color c = tintGround;
                c.a *= set.Alpha;
                lamp.color = c;
            }
        }
    }

    public bool SiteOpen(BackdropPiece p) { return p.tier > 0; }

    // Launch sites for the elites: the sites still in the upper part of the
    // view (a launch has time to be seen), inside the view sideways, drawn
    // big enough. The elite comes out of the door (emerge).
    public override void LandingSites(List<LandingSite> into)
    {
        if (sites == null) return;
        for (int i = 0; i < sites.items.Count; i++)
        {
            var p = sites.items[i];
            if (!p.active || p.sr.sprite == null) continue;
            if (p.size < FrostTuning.SiteMinSize) continue;
            if (p.y < -HalfH * .15f || p.y > HalfH - p.size * .3f) continue;
            if (Mathf.Abs(p.x) > HalfW - .25f) continue;
            Bounds b = p.sr.sprite.bounds;
            Vector2 pad = FrostTuning.SitePads[p.kind];
            into.Add(new LandingSite
            {
                anchor = p.root,
                local = new Vector3(b.center.x + pad.x * b.size.x, b.center.y + pad.y * b.size.y, 0f),
                scale = Mathf.Clamp(p.size * FrostTuning.EmergeScalePerUnit, FrostTuning.EmergeScaleMin, FrostTuning.EmergeScaleMax),
                order = p.sr.sortingOrder + 2,
                id = FrostTuning.SiteIdBase + i,
                kind = FrostTuning.SiteKinds[p.kind],
                emerge = true,
            });
        }
    }

    // The elite director wants a site of one of `launchFrom`'s kinds in view soon (LandingSites.BringHome): the ground is fixed, so
    // none is conjured -- but the site spawner is asked for one of that kind at the top of the view now (it scrolls in over the next
    // seconds, as the ground pieces always do), unless one of them is already in view or on its way. False when none of the kinds is
    // one of Frost's sites or there is no room for one on the ground.
    public override bool BringHome(string launchFrom)
    {
        if (sites == null || !planner.Ready || string.IsNullOrEmpty(launchFrom)) return false;
        var want = new List<int>(FrostTuning.SiteKinds.Length);   // (a rare call: one per elite)
        foreach (string part in launchFrom.Split(','))
        {
            var k = LandingSite.KindOf(part.Trim());
            if (k == null) continue;
            for (int t = 0; t < FrostTuning.SiteKinds.Length; t++)
                if (FrostTuning.SiteKinds[t] == k.Value && siteClosed[t] != null && siteOpen[t] != null && FrostTuning.SiteFits(t, set.Variant) && !want.Contains(t)) want.Add(t);
        }
        if (want.Count == 0) return false;
        for (int i = 0; i < sites.items.Count; i++)
        {
            var p = sites.items[i];
            if (p.active && want.Contains(p.kind) && p.y >= -HalfH * .15f) return true;   // (in view, or still on its way in from the top)
        }
        return SpawnSite(float.NaN, want[groundRng.Next(want.Count)]);
    }

    // ----------------------------------------------------------- weather --

    void BuildCeiling()
    {
        if (banks.Length == 0) return;
        // staggered rows over the whole view (thinner low down,
        // CeilingLowShare): the deck the planetfall has just dropped through
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
        SetSprite(b, Pick(banks), Rand(FrostTuning.CeilingMin, FrostTuning.CeilingMax));
        if (Chance(.5)) b.body.localScale = new Vector3(-1f, 1f, 1f);
        b.x = x;
        b.y = float.IsNaN(y) ? HalfH + b.size * .35f : y;
        // drifting apart: the ceiling opens from the middle
        b.vx = Mathf.Sign(x == 0f ? 1f : x) * Rand(.04f, .14f);
        b.vy = Rand(.0f, .15f);
        b.rate = set.Spec.Rate("ceiling");
        b.phase = FrostTuning.CeilingAlpha * Rand(.8f, 1f);
        Place(b);
        Tinted(b, tintCeiling, 0f);
    }

    void SpawnWisp()
    {
        if (wispArt.Length == 0) return;
        var w = wisps.Spawn();
        if (w == null) return;
        SetSprite(w, Pick(wispArt), Rand(FrostTuning.WispMin, FrostTuning.WispMax));
        if (Chance(.5)) w.body.localScale = new Vector3(-1f, 1f, 1f);
        w.x = Rand(-HalfW * .7f, HalfW * .7f);
        w.y = SpawnY(w.size);
        w.vx = FrostTuning.Wind * FrostTuning.WispDrift * Rand(.7f, 1.3f);
        w.rate = set.Spec.Rate("wisps");
        w.phase = Rand(FrostTuning.WispAlphaMin, FrostTuning.WispAlphaMax) * CloudCover.Alpha(set.Spec.CloudDensity());
        Place(w);
        Tinted(w, tintWisps, 0f);
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
        m.vx = FrostTuning.Wind * FrostTuning.MistDrift * Rand(.6f, 1.4f);
        m.rate = set.Spec.Rate("mist");
        m.phase = Rand(FrostTuning.MistAlphaMin, FrostTuning.MistAlphaMax) * CloudCover.Alpha(set.Spec.CloudDensity());
        Place(m);
        Tinted(m, tintMist, 0f);
    }

    void SpawnAurora()
    {
        if (auroraFrames.Length == 0 || auroraAlpha <= 0f) return;
        var a = auroraPool.Spawn();
        if (a == null) return;
        a.frames = auroraFrames;
        a.fps = FrostAmbientCatalog.Loops[FrostAmbientCatalog.LoopIndex(FrostAmbientCatalog.AuroraLoop)].fps;
        SetSprite(a, auroraFrames[0], HalfW * 2f * Rand(1.1f, 1.4f));
        a.x = Rand(-.6f, .6f);
        a.y = SpawnY(a.size * .4f);
        a.rate = set.Spec.Rate("aurora");
        a.phase = Rand(0f, 6.28f);
        a.root.localRotation = Quaternion.Euler(0, 0, Rand(-14f, 14f));
        if (Chance(.5)) a.body.localScale = new Vector3(-1f, 1f, 1f);
        Place(a);
        a.sr.color = new Color(1f, 1f, 1f, 0f);
    }

    // One blizzard sheet: in from the upper right, sweeping down-left along
    // its streaks, faded in and out over its crossing.
    void SpawnSheet()
    {
        if (gustArt.Length == 0) return;
        var g = blizzard.Spawn();
        if (g == null) return;
        SetSprite(g, Pick(gustArt), Rand(FrostTuning.BlizzardMin, FrostTuning.BlizzardMax));
        g.x = HalfW + g.size * .25f;
        g.y = Rand(HalfH * .2f, HalfH * 1.0f);
        float speed = FrostTuning.BlizzardSpeed * Rand(.85f, 1.15f);
        g.vx = -speed;
        g.vy = -speed * .55f;
        g.life = (2f * HalfW + g.size * .6f) / speed;
        g.rate = set.Spec.Rate("blizzard");
        Place(g);
        Tinted(g, tintBlizzard, 0f);
    }

    public void ForceGust() { sheetsLeft = FrostTuning.BlizzardSheets; sheetIn = 0f; Gusts++; }

    void BuildSnow()
    {
        if (snowArt.Length == 0) return;
        string[] layers = { "snow_far", "snow_mid", "snow_near" };
        for (int d = 0; d < 3; d++)
            foreach (var p in snow[d].items)
            {
                p.Show(true);
                SetSprite(p, Pick(snowArt), FrostTuning.SnowSize[d] * Rand(.85f, 1.15f));
                if (Chance(.5)) p.body.localScale = new Vector3(-1f, 1f, 1f);
                p.x = Rand(-HalfW, HalfW);
                p.y = Rand(-HalfH, HalfH);
                p.phase = Rand(0f, 6.28f);
                p.rate = set.Spec.Rate(layers[d]);
                Place(p);
            }
    }

    void StepFlakes(BackdropPiece p, int depth, float dt, float v)
    {
        if (!p.active) return;
        p.age += dt;
        p.y += (-FrostTuning.SnowFall[depth] - p.rate * v) * dt;
        p.x += (Mathf.Sin(p.age * .9f + p.phase) * .25f - wind * FrostTuning.BlizzardWind * (.5f + .5f * depth)) * dt;
        float my = HalfH + p.size * .55f, mx = HalfW + p.size * .55f;
        if (p.y < -my) { p.y += 2f * my; p.x = Rand(-HalfW, HalfW); }
        else if (p.y > my) p.y -= 2f * my;
        if (p.x < -mx) p.x += 2f * mx;
        else if (p.x > mx) p.x -= 2f * mx;
        Place(p);
    }
}
