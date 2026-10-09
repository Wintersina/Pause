using System.Collections.Generic;
using UnityEngine;

// Tide's backdrop: the ship flies at atmosphere level over the ocean planet,
// under a teal storm-cloud ceiling, looking down on open sea, a field of oil
// rigs and refineries, a drowned city and the night side's glowing currents.
//
//   GROUND     one of four tile sets per landing (BackdropVariants): v1 the
//              open sea (swell, whitecaps, a few buoys), v2 the rig field (rusted
//              decks, walkways and pipe bridges over dark water), v3 the drowned
//              city (a lit street grid and tower crowns under shallow water), v4
//              the NIGHT side (TideTuning.Night: drawn darker, lamps and plankton
//              emphasised).
//   PIECES     landmarks (oil rigs, refineries, a crane gantry, tankers and
//              trawlers, a lighthouse, sunken towers, pump platforms, a wind
//              turbine, mooring buoys, a bridge section), pipes (straights,
//              bends, manifolds, bridges, leaks, a pump house) and sea features
//              (whirlpools, oil slicks, reef heads, wrecks, bubbling vents, flare
//              stacks, plankton blooms, a salvage crawler), PINNED to the mid
//              tile and placed where its affinity mask says they belong
//              (GroundPlanner, TideTuning.Rule): the ground is mostly open
//              WATER (GroundClass.Water), so platforms, stacks and refineries
//              stand on the painted decks / city blocks (Built) or the shoals
//              (Open) and only vessels, whirlpools and slicks float on the water;
//              on the sea sets (v1, v4: almost no deck) a platform's own foam
//              base is its ground. Grown as CLUSTERS (TideDirector.SpawnCluster):
//              a rig with a pipe run to a neighbouring structure; a vent field
//              (flare stacks and bubbling vents with a reef or wreck beside them
//              and a smoke pall over the flare); a vessel on the water with a pipe
//              bridge or a whirlpool; lone pieces -- then a long calm gap. Mixes
//              per variant (TideTuning.ClusterWeights). Their loops (stack smoke,
//              flares, steam and bubbles from leaks and vents, surf, beacons) sit
//              on MEASURED points (TideAmbientCatalog, points.json): smoke exactly
//              over derrick tips / column tops / flare mouths, all leaning with the
//              one level wind. Run C (the animated loop atlases) is installed
//              separately: a loop whose atlas is not there yet simply does not show
//              (TideAmbientCatalog.Missing lists them).
//   SITES      the elites' launch sites on the same pinned plane (trench hatch,
//              rig bay, reef dock, vent stack, wreck bay), shut until the launch
//              tell opens them.
//   WEATHER    the CEILING of storm cloud, thick at the start (continuing the
//              planetfall's teal cloud deck), held CeilingHold s, then clearing
//              fast, gone by CeilingClearSeconds (CloudCover); then only a light
//              scattering of wisps and low mist drifting with the wind
//              (TideTuning.CloudDensity), GUSTS sweeping across now and then, a
//              rain streak or two and a smoke pall over each flare stack.
//
// Everything sits far below gameplay (BackdropCatalog.BaseOrder); the pools
// are fixed and a frame allocates nothing.
public static class TideTuning
{
    // ---- brightness ----------------------------------------------------------
    // The Tide art is painted dark with mint foam and lamps (tile value p90
    // ~.38-.47, v4 ~.30-.40) but its sea is lighter than the enemies fly over
    // comfortably: against the stand-in cast (Ember's grey char hull, value .42)
    // the undarkened lanes read 2.4:1 (v1), 2.2:1 (v2), 2.1:1 (v3), 2.3:1 (v4)
    // and their value p90 reaches .47-.48 -- over the hull. Every world's
    // gameplay guard is that an enemy body reads 2.5:1 against the rendered lane
    // and the lane stays darker than the hull (WorldBackdropTest.CheckReadability,
    // TideBackdropTest), so each set is DRAWN darker by its entry in
    // VariantBrightness (k < 1 is a darkening curve, BackdropGrade; a layer takes
    // its Graded share of it): the foam and lamps stay the bright accent. The
    // world's own lift stays 1 (below BackdropCatalog.Spec.BrightLift: the dark
    // worlds' thin shot outlines). Re-check when the Tide roster's own palette
    // lands (add-world phase 12): a lighter hull lets these come back up.
    public static float Brightness = 1f;
    public static readonly float[] VariantBrightness = { 1f, .7f, .55f, .5f, .55f };   // index = variant (0 none)
    public static readonly bool[] Night = { false, false, false, false, true };
    public static float NightLightBoost = 1.3f;
    public static float PlumeShare = 1f;
    public static float PlumeLift = 1f;
    public static float LoopBrightness = 1f;
    public static float LightBoostNow = 1f;           // set by the director for its variant

    // ---- ground pieces -------------------------------------------------------
    public static float LandmarkMin = 1.5f, LandmarkMax = 1.75f;
    public static float FireMin = 1.4f, FireMax = 1.75f, SatelliteMin = 1.05f, SatelliteMax = 1.35f;
    public static float PipeMin = .5f, PipeMax = 1.45f;
    public static float ClusterGapMin = 1.6f, ClusterGapMax = 3.2f;     // scrolled ground distance after a cluster
    public static float LoneGapMin = .8f, LoneGapMax = 1.5f;             // ... after a lone piece
    public static float NeighbourGapMin = .1f, NeighbourGapMax = .45f;  // between a rig's plate and its neighbour's
    public static float Wind = 1f;                                        // +1: plumes lean right (as painted)
    public static float PlateFeather = 0f;                                // the pieces carry their own foam fringe

    // what each drawing may stand on (GroundPlanner). The ground is mostly water.
    static readonly PieceRule Platform = new PieceRule(GroundClass.Built | GroundClass.Open, .6f, .3f);   // rigs, refineries, stacks, sunken towers
    static readonly PieceRule Floating = new PieceRule(GroundClass.Water, .8f, .2f);                     // vessels, buoys, whirlpools, slicks
    static readonly PieceRule Shoal = new PieceRule(GroundClass.Open | GroundClass.Water, .85f, .3f);   // reefs, wrecks, vents, blooms
    static readonly PieceRule Anywhere = new PieceRule(GroundClass.Any, 0f, .2f);
    static readonly PieceRule SeaPlatform = new PieceRule(GroundClass.Any, 0f, .3f);                     // the sea sets: no deck to stand on

    // The sets with next to no painted deck (open sea, night currents): a platform
    // there stands on its own foam base, anywhere free.
    public static readonly bool[] OpenSea = { false, true, false, false, true };

    public static PieceRule Rule(string piece, bool openSea = false)
    {
        if (piece == null) return Anywhere;
        if (piece.StartsWith("tanker") || piece.StartsWith("trawlerbarge") || piece.StartsWith("mooring") ||
            piece.StartsWith("whirlpool") || piece.StartsWith("oil_slick") || piece.StartsWith("plankton")) return Floating;
        if (piece.StartsWith("reef_head") || piece.StartsWith("wreck_hull") || piece.StartsWith("bubbling_vent") ||
            piece.StartsWith("salvage_crawler")) return Shoal;
        if (piece.StartsWith("oilrig") || piece.StartsWith("refineryrig") || piece.StartsWith("pump_platform") ||
            piece.StartsWith("crane_gantry") || piece.StartsWith("lighthouse") || piece.StartsWith("wind_turbine") ||
            piece.StartsWith("bridge_section") || piece.StartsWith("sunken_city") || piece.StartsWith("flare_stack") ||
            piece == "pumphouse_00" || piece.StartsWith("rig_bay") || piece.StartsWith("trench_hatch") ||
            piece.StartsWith("reef_dock") || piece.StartsWith("vent_stack") || piece.StartsWith("wreck_bay"))
            return openSea ? SeaPlatform : Platform;
        return Anywhere;     // pipes: they connect what they connect
    }

    // ---- clusters --------------------------------------------------------------
    public const int ClRig = 0, ClVents = 1, ClSea = 2, ClLone = 3, ClPipeline = 4;
    public static readonly int[][] ClusterWeights =
    {
        new[] { 2, 2, 2, 2, 1 },   // (none)
        new[] { 1, 3, 3, 3, 1 },   // v1 open sea: vessels, whirlpools, lone rigs, reefs
        new[] { 3, 1, 1, 2, 3 },   // v2 rig field: rigs, refineries, pipe runs
        new[] { 2, 2, 2, 3, 1 },   // v3 drowned city: lone towers, vents, wrecks
        new[] { 1, 3, 2, 3, 1 },   // v4 night: flare stacks, vents, a lone lit rig
    };
    public static readonly string[] Rigs = { "oilrig_00", "oilrig_01", "oilrig_02", "refineryrig_00", "refineryrig_01", "pump_platform_00" };
    public static readonly string[] Neighbours = { "crane_gantry_00", "oilrig_02", "pump_platform_00", "sunken_city_01", "lighthouse_00", "pumphouse_00" };
    public static readonly string[] Fronts = { "bubbling_vent_00", "bubbling_vent_01", "flare_stack_00", "flare_stack_01" };
    public static readonly string[] Satellites = { "reef_head_00", "reef_head_01", "wreck_hull_00", "wreck_hull_01", "wreck_hull_02", "plankton_bloom_00", "plankton_bloom_01", "salvage_crawler_00" };
    public static readonly string[] Vessels = { "tanker_00", "trawlerbarge_00", "trawlerbarge_01", "mooring_buoys_00" };
    public static readonly string[] Banks = { "whirlpool_00", "whirlpool_01", "oil_slick_00", "oil_slick_01", "bridge_section_00", "pipebridge_00", "pipebridge_01" };
    public static readonly string[] Lone = { "oilrig_00", "oilrig_02", "refineryrig_01", "lighthouse_00", "wind_turbine_00", "sunken_city_00", "sunken_city_01", "pump_platform_00", "crane_gantry_00" };
    public static readonly string[] HorizontalPipes = { "pipe_straight_00", "manifold_00", "manifold_01", "pipe_tangle_01", "pipe_leak_01", "pipe_leak_00" };

    // ---- launch sites ----
    public static float SiteMin = 1.5f, SiteMax = 1.7f, SiteFirst = 3f, SiteGapMin = 5f, SiteGapMax = 8.5f;
    public static float SiteCloseDelay = 1.4f, SiteMinSize = 1.2f;
    public static readonly string[] SiteClosed = { "trench_hatch_closed", "rig_bay_closed", "reef_dock_closed", "vent_stack_closed", "wreck_bay_closed" };
    public static readonly string[] SiteOpen = { "trench_hatch_open", "rig_bay_open", "reef_dock_open", "vent_stack_open", "wreck_bay_open" };
    public static readonly LandingKind[] SiteKinds = { LandingKind.TideTrenchHatch, LandingKind.TideRigDeck, LandingKind.TideReefDock, LandingKind.TideVentStack, LandingKind.TideWreckBay };
    public static readonly int[] SiteWeights = { 2, 3, 2, 2, 2 };
    public static Vector2 LampsAt = new Vector2(128f, 214f);    // the lamp row's centre on the site's cell
    public static float LampsScale = .5f;
    public const float EmergeScalePerUnit = .22f, EmergeScaleMin = .26f, EmergeScaleMax = .4f;
    public const int SiteIdBase = 800;

    // ---- weather (the atlas bakes its translucency in: every sprite <= 105/255) ----
    // CLOUD COVER: the knobs (CloudCover documents the timeline). How much
    // cloud drifts over the sea once the ceiling has cleared: 1 = a light
    // scattering (default), 2 = about twice as much, 0 = none.
    public static float CloudDensity = CloudCover.Density;
    public static float CeilingHold = CloudCover.CeilingHold;                   // s at full thickness after landing
    public static float CeilingClearSeconds = 13f;                              // s: the ceiling is gone (CloudCover's 14: the teal banks read heavier, so <= 10% by 10 s needs it a touch earlier)
    public static float CeilingMin = 4.2f, CeilingMax = 5.6f, CeilingGap = 1.5f, CeilingLowShare = .55f;
    public static float CeilingThicken = 5f;
    // NOTE: the painted banks are cut flat at the 16 px margin of their cells (Codex run B: bbox 16..239 on every bank), so the
    // thickened ceiling shows faint straight seams where banks meet. BackdropGrade's _Feather thins translucent art far too
    // much to hide them (tried: the ceiling went to a veil); a soft-edged bank re-paint is the fix (reported to the coordinator).
    // ... matched to the planetfall's teal deck (tide_cloud_deck: rgb ~66,103,107; the banks are painted
    // greener and darker, ~59,90,79): lifted and tinted toward the deck's blue-teal
    public static float CeilingLift = 1.6f, CeilingSaturation = .95f;
    public static Color CeilingTint = new Color(.86f, .95f, 1f, 1f);
    // wisps and mist at CloudDensity 1 (density divides the gaps, lifts the alpha);
    // they drift downwind (Wind)
    public static float WispMin = 1.9f, WispMax = 2.8f, WispAlphaMin = .3f, WispAlphaMax = .45f;
    public static float WispEveryMin = 6f, WispEveryMax = 10f, WispDrift = .18f;
    public static float MistAlphaMin = .3f, MistAlphaMax = .42f;
    public static float MistWidthMin = .5f, MistWidthMax = .7f;     // of the view's width: the mist sprites are square, so a full-width one parks over the lane too long
    public static float MistEveryMin = 14f, MistEveryMax = 22f, MistDrift = .08f;
    public static float GustFirst = 12f, GustEveryMin = 14f, GustEveryMax = 22f;
    public static int GustSheets = 3;
    public static float GustStagger = .6f, GustSpeed = 3.4f, GustMin = 4.5f, GustMax = 6f, GustMaxAlpha = 1f;
    public static int RainCount = 5;
    public static float RainMin = .9f, RainMax = 1.5f, RainAlpha = .9f;
    public static float PallAlpha = .6f, PallLife = 22f;
    // run C's free ocean crests (wave_crest_a / _b): wandering over the open water
    public static int CrestCount = 6;
    public static float CrestMin = 1.1f, CrestMax = 1.7f, CrestAlpha = .6f, CrestLife = 7f, CrestEveryMin = 1.2f, CrestEveryMax = 2.6f;
}

public class TideDirector : PlanetDirector
{
    BackdropPool ground, pipes, fires, sites, palls, mist, wisps, ceiling, gusts, rain, crests;
    readonly SpriteRenderer[] lamps = new SpriteRenderer[3];
    BackdropAtlas landmarkArt, pipeArt, fireArt, siteArt, weather;
    AmbientEmitters ambient;
    GroundPlanner planner;
    readonly Sprite[][] crestArt = new Sprite[2][];
    float crestIn = 1f;
    Sprite[] siteClosed, siteOpen, banks, wispArt, mistArt, gustArt, pallArt, rainArt;
    Sprite lampsOff, lampsOn;
    float groundTravel, groundGap, siteTravel, siteGap, ceilingTravel;
    int lastCluster = -1, lastLone = -1;
    bool night, openSea;
    float mistIn = 2.5f, wispIn = 6f;
    readonly Timer gustTimer = new Timer(TideTuning.GustEveryMin, TideTuning.GustEveryMax, TideTuning.GustFirst);
    int sheetsLeft;
    float sheetIn;
    readonly List<Material> mats = new List<Material>();

    // Tests: > 0 seeds the next directors' streams (statistics over many runs).
    public static int SeedOverride;
    public TideDirector() : base(SeedOverride > 0 ? SeedOverride : 1990) { }

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
    public BackdropPool Ceiling => ceiling;
    public BackdropPool GustSheetPool => gusts;
    public BackdropPool Palls => palls;
    public BackdropPool Rain => rain;
    public BackdropPool Crests => crests;
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
        weather = set.Atlas("weather");
        night = set.Spec.Night(set.Variant);
        TideTuning.LightBoostNow = night ? TideTuning.NightLightBoost : 1f;
        ambient = new AmbientEmitters(set, TideAmbientCatalog.Table) { Wind = TideTuning.Wind };
        planner = new GroundPlanner(set, "mid", GroundMask.Load("Tide", set.Variant));
        openSea = set.Variant >= 0 && set.Variant < TideTuning.OpenSea.Length && TideTuning.OpenSea[set.Variant];

        siteClosed = Sprites(siteArt, TideTuning.SiteClosed);
        siteOpen = Sprites(siteArt, TideTuning.SiteOpen);
        lampsOff = siteArt.Get("lights_off");
        lampsOn = siteArt.Get("lights_on");
        banks = weather.Frames("cloud_bank");
        wispArt = weather.Frames("cloud_wisp");
        mistArt = weather.Frames("mist");
        gustArt = weather.Frames("gust");
        pallArt = weather.Frames("pall");
        rainArt = weather.Frames("rain");

        // ground plane, back to front: pipes tuck under the plates they join
        pipes = LandmarkPool("ground", 8, -4);
        fires = LandmarkPool("ground", 6, -2);
        ground = LandmarkPool("ground", 8);
        sites = LandmarkPool("ground", 3);
        ambient.Rig(pipes, 1);
        ambient.Rig(fires, 1);
        ambient.Rig(ground, 1);
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
            float k = TideTuning.LampsScale;
            var box = TideAmbientCatalog.Piece("lights_off");
            float rowY = box != null && box.box != null && box.box.Length == 4 ? (box.box[1] + box.box[3]) * .5f : 128f;
            go.transform.localPosition = new Vector3((TideTuning.LampsAt.x - 128f) / ppu,
                                                     (128f - TideTuning.LampsAt.y) / ppu - (128f - rowY) / ppu * k, 0f);
            go.transform.localScale = new Vector3(k, k, 1f);
        }
        crests = Pool("ground", TideTuning.CrestCount, false, -8);
        for (int i = 0; i < 2; i++) crestArt[i] = set.Atlas("surf").Frames(TideAmbientCatalog.Crests[i]);
        palls = Pool("palls", 4);
        mist = Pool("mist", 3);
        wisps = Pool("wisps", 4);
        ceiling = Pool("ceiling", 26);
        gusts = Pool("gusts", TideTuning.GustSheets + 1);
        rain = Pool("rain", TideTuning.RainCount);

        PlateMaterial = Grade(ground, "ground", 1f, TideTuning.PlateFeather);
        Grade(fires, "ground", 1f, TideTuning.PlateFeather);
        Grade(sites, "ground", 1f, TideTuning.PlateFeather);
        Grade(pipes, "ground");
        Grade(palls, "palls");
        Grade(mist, "mist");
        Grade(wisps, "wisps");
        CeilingMaterial = Grade(ceiling, "ceiling", TideTuning.CeilingThicken, 0f, TideTuning.CeilingLift, TideTuning.CeilingSaturation);

        // the opening view: the ground already dressed, under a thick ceiling
        groundGap = Rand(TideTuning.LoneGapMin, TideTuning.LoneGapMax);
        siteGap = TideTuning.SiteFirst;
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
    public static float CeilingDensity(float t) { return BackdropCatalog.For("Tide").CeilingDensity(t); }

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
        if (ceilingTravel >= TideTuning.CeilingGap && density > CloudCover.BankSpawnFloor)
        {
            ceilingTravel = 0f;
            SpawnBank(Rand(-HalfW * .8f, HalfW * .8f), float.NaN);
        }
        float air = set.Spec.CloudDensity();
        if (CloudCover.Tick(ref mistIn, dt, air, TideTuning.MistEveryMin, TideTuning.MistEveryMax, rng)) SpawnMist(float.NaN);
        if (CloudCover.Tick(ref wispIn, dt, air, TideTuning.WispEveryMin, TideTuning.WispEveryMax, rng)) SpawnWisp();
        if (gustTimer.Tick(dt, rng)) ForceGust();
        if (sheetsLeft > 0)
        {
            sheetIn -= dt;
            if (sheetIn <= 0f) { SpawnSheet(); sheetsLeft--; sheetIn = TideTuning.GustStagger; }
        }

        StepPinned(pipes, dt);
        StepPinned(fires, dt);
        StepPinned(ground, dt);
        StepPinned(sites, dt);
        StepSites(dt);
        StepCrests(dt);
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
            float low = Mathf.Lerp(TideTuning.CeilingLowShare, 1f, y01 * y01 * (3f - 2f * y01));
            Color c = TideTuning.CeilingTint;
            c.a = b.phase * density * low * set.Alpha;
            b.sr.color = c;
        }
        foreach (var g in gusts.items)
        {
            if (!g.active || !Drift(g, dt, v)) continue;
            float k = Mathf.Clamp01(g.age / Mathf.Max(.01f, g.life));
            if (k >= 1f) { Despawn(g); continue; }
            Tinted(g, TideTuning.GustMaxAlpha * Mathf.Sin(k * Mathf.PI));
        }
        foreach (var s in rain.items)
        {
            if (!s.active) continue;
            Recycle(s, .18f, dt, v, .35f);
            Tinted(s, TideTuning.RainAlpha);
        }
    }

    void Tinted(BackdropPiece p, float alpha)
    {
        p.sr.color = new Color(1f, 1f, 1f, alpha * set.Alpha);
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
            if (p.y < -HalfH - p.size * .9f - .5f)
            {
                planner.Release(p.footprint);
                p.footprint = -1;
                Despawn(p);
                continue;
            }
            Tinted(p, 1f);
        }
    }

    // ---------------------------------------------------------------- crests --

    // Run C's ocean crests: a wave crest loop wandering over open water, pinned
    // like the ground (it rides its spot of the sea), faded in and out. Nothing
    // spawns until the surf atlas is installed.
    void SpawnCrest()
    {
        int kind = rng.Next(2);
        var frames = crestArt[kind];
        if (frames == null || frames.Length == 0 || !planner.Ready) return;
        float size = Rand(TideTuning.CrestMin, TideTuning.CrestMax);
        for (int t = 0; t < 12; t++)
        {
            float x = Rand(XMin(size), XMax(size));
            double gy = planner.Travel + HalfH + size * .5f + Rand(0f, 2f);
            if (planner.Match(x, gy, size * .3f, size * .25f, GroundClass.Water) < .9f) continue;
            var p = crests.Spawn();
            if (p == null) return;
            SetSprite(p, frames[0], size);
            p.frames = frames;
            p.fps = 8f * Rand(.9f, 1.1f);
            p.age = 0f;
            p.life = TideTuning.CrestLife * Rand(.8f, 1.2f);
            p.gy = gy;
            p.x = x;
            p.y = planner.Y(gy);
            p.rate = set.Spec.Rate("ground");
            p.body.localScale = Chance(.5) ? new Vector3(-1f, 1f, 1f) : Vector3.one;
            Place(p);
            Tinted(p, 0f);
            return;
        }
    }

    void StepCrests(float dt)
    {
        crestIn -= dt;
        if (crestIn <= 0f) { crestIn = Rand(TideTuning.CrestEveryMin, TideTuning.CrestEveryMax); SpawnCrest(); }
        foreach (var p in crests.items)
        {
            if (!p.active) continue;
            p.age += dt;
            p.y = planner.Y(p.gy);
            Place(p);
            float k = p.age / Mathf.Max(.01f, p.life);
            if (k >= 1f || p.y < -HalfH - p.size) { Despawn(p); continue; }
            p.Animate();
            Tinted(p, TideTuning.CrestAlpha * Mathf.Min(1f, Mathf.Min(k * 5f, (1f - k) * 4f)));
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
            siteGap = Rand(TideTuning.SiteGapMin, TideTuning.SiteGapMax);
            return Rand(TideTuning.LoneGapMin, TideTuning.LoneGapMax);
        }
        int n = SpawnCluster(lo, hi);
        if (n == 0) return 0f;
        return n > 1 ? Rand(TideTuning.ClusterGapMin, TideTuning.ClusterGapMax)
                     : Rand(TideTuning.LoneGapMin, TideTuning.LoneGapMax);
    }

    float XMin(float size) { return -EdgeX + size * .3f; }
    float XMax(float size) { return EdgeX - size * .3f; }

    int PickCluster()
    {
        var w = TideTuning.ClusterWeights[Mathf.Clamp(set.Variant, 0, TideTuning.ClusterWeights.Length - 1)];
        int total = 0;
        for (int i = 0; i < w.Length; i++) if (i != lastCluster) total += w[i];
        int r = rng.Next(Mathf.Max(1, total));
        for (int i = 0; i < w.Length; i++)
        {
            if (i == lastCluster || w[i] <= 0) continue;
            r -= w[i];
            if (r < 0) return i;
        }
        return TideTuning.ClLone;
    }

    // Grows one cluster with its anchor in the ground band [lo, hi]; returns
    // how many pieces it placed (0: nothing fitted).
    public int SpawnCluster(double lo, double hi, int kind = -1)
    {
        if (kind < 0) kind = PickCluster();
        int n = 0;
        switch (kind)
        {
            case TideTuning.ClRig: n = Industrial(Pick(TideTuning.Rigs), lo, hi); break;
            case TideTuning.ClPipeline: n = Industrial(Pick(TideTuning.Lone), lo, hi); break;
            case TideTuning.ClVents: n = VentField(lo, hi); break;
            case TideTuning.ClSea: n = SeaScene(lo, hi); break;
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
        var rule = TideTuning.Rule(name, openSea);
        float rx = GroundPlanner.HalfX(rule, size), ry = GroundPlanner.HalfY(rule, size);
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
        p.kind = 0;
        Place(p);
        Tinted(p, 1f);
        ambient.Attach(p, name, rng);
        return p;
    }

    bool Spot(string name, float size, double lo, double hi, out float x, out double gy)
    {
        return planner.Find(TideTuning.Rule(name, openSea), size, XMin(size), XMax(size), lo, hi, rng, out x, out gy);
    }

    // A structure (refinery / tower ...) with a neighbour joined by a pipe run.
    int Industrial(string anchor, double lo, double hi)
    {
        float sa = Rand(TideTuning.LandmarkMin, TideTuning.LandmarkMax);
        float xa; double ga;
        if (!Spot(anchor, sa, lo, hi, out xa, out ga)) return 0;
        var a = Put(ground, landmarkArt, anchor, sa, xa, ga, Chance(.5));
        if (a == null) return 0;
        int n = 1;
        // the neighbour: toward the middle first, then the other side
        string nb = Pick(TideTuning.Neighbours);
        if (nb == anchor) nb = "crane_gantry_00";
        bool pump = nb == "pumphouse_00";
        float sb = pump ? Rand(.9f, 1.1f) : Rand(TideTuning.LandmarkMin * .85f, TideTuning.LandmarkMax * .9f);
        float plateA = sa * .4f, plateB = sb * (pump ? .44f : .4f);
        for (int attempt = 0; attempt < 2; attempt++)
        {
            float side = (xa > 0f) == (attempt == 0) ? -1f : 1f;
            float gap = Rand(TideTuning.NeighbourGapMin, TideTuning.NeighbourGapMax);
            float xb = xa + side * (plateA + gap + plateB);
            double gb = ga + Rand(-.12f, .12f);
            if (xb < XMin(sb) - .2f || xb > XMax(sb) + .2f) continue;
            var rule = TideTuning.Rule(nb, openSea);
            if (!planner.Accepts(rule, xb, gb, sb) || !planner.Free(xb, gb, GroundPlanner.HalfX(rule, sb), GroundPlanner.HalfY(rule, sb))) continue;
            var b = Put(pump ? pipes : ground, pump ? pipeArt : landmarkArt, nb, sb, xb, gb, !pump && Chance(.5));
            if (b == null) break;
            n++;
            if (PipeRun(Mathf.Min(xa, xb) + (xa < xb ? plateA : plateB) * .75f,
                        Mathf.Max(xa, xb) - (xa < xb ? plateB : plateA) * .75f,
                        (ga + gb) * .5 - sa * .16f)) n++;
            break;
        }
        return n;
    }

    // A horizontal pipe (or two) whose flange faces land on x0 and x1 at
    // ground row gy: the measured pipe ends (points.json) set its scale.
    bool PipeRun(float x0, float x1, double gy)
    {
        float len = x1 - x0;
        if (len < .3f) return false;
        bool overWater = planner.ClassAt((x0 + x1) * .5f, gy) == GroundClass.Water;
        string name = overWater ? "pipebridge_00" : Pick(TideTuning.HorizontalPipes);
        int pieces = 1;
        float span = SpanOf(name);
        float w = len / span;
        if (w > TideTuning.PipeMax) { pieces = 2; w = len * .5f / span; }
        if (w < TideTuning.PipeMin * .6f) return false;
        if (!planner.Free((x0 + x1) * .5f, gy, Mathf.Max(.05f, len * .5f - .25f), .1f)) return false;
        float placed = x0;
        for (int i = 0; i < pieces; i++)
        {
            string n = i == 0 ? name : (overWater ? "pipebridge_00" : "pipe_straight_00");
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

    static TideAmbientCatalog.PEnd[] Ends(string pipe)
    {
        var p = TideAmbientCatalog.Piece(pipe);
        return p != null && p.ends != null && p.ends.Length >= 2 ? p.ends : null;
    }

    // A bubbling vent or flare stack with a reef, wreck, plankton bloom or
    // salvage crawler beside it (downwind first) and, over a flare, a smoke pall.
    int VentField(double lo, double hi)
    {
        string front = Pick(TideTuning.Fronts);
        float sf = Rand(TideTuning.FireMin, TideTuning.FireMax);
        float xf; double gf;
        if (!Spot(front, sf, lo, hi, out xf, out gf)) return 0;
        var f = Put(fires, fireArt, front, sf, xf, gf, false);
        if (f == null) return 0;
        int n = 1, want = 1 + rng.Next(2);
        float wind = TideTuning.Wind;
        for (int k = 0; k < 6 && n <= want; k++)
        {
            string sat = Pick(TideTuning.Satellites);
            float ss = Rand(TideTuning.SatelliteMin, TideTuning.SatelliteMax);
            float dir = k % 2 == 0 ? wind : -wind;
            float xs = xf + dir * Rand(.75f, 1.15f) * (sf + ss) * .5f;
            double gs = gf + Rand(-.7f, .5f);
            if (xs < XMin(ss) || xs > XMax(ss)) continue;
            var rule = TideTuning.Rule(sat, openSea);
            if (!planner.Accepts(rule, xs, gs, ss) || !planner.Free(xs, gs, GroundPlanner.HalfX(rule, ss), GroundPlanner.HalfY(rule, ss))) continue;
            if (Put(fires, fireArt, sat, ss, xs, gs, Chance(.5)) != null) n++;
        }
        if (front.StartsWith("flare_stack")) SpawnPall(f);
        return n;
    }

    // A vessel on the open water and, nearby on the same water, a whirlpool, an
    // oil slick, a bridge section or a pipe bridge.
    int SeaScene(double lo, double hi)
    {
        int n = 0;
        string vessel = Pick(TideTuning.Vessels);
        float sv = Rand(TideTuning.LandmarkMin * .9f, TideTuning.LandmarkMax);
        float x; double gy;
        if (Spot(vessel, sv, lo - .4f, hi + .4f, out x, out gy) && Put(ground, landmarkArt, vessel, sv, x, gy, Chance(.5)) != null) n++;
        string bank = Pick(TideTuning.Banks);
        float sk = Rand(TideTuning.LandmarkMin, TideTuning.LandmarkMax);
        bool pipeBank = bank.StartsWith("pipebridge"), feature = bank.StartsWith("whirlpool") || bank.StartsWith("oil_slick");
        var atlas = pipeBank ? pipeArt : feature ? fireArt : landmarkArt;
        var pool = pipeBank ? pipes : feature ? fires : ground;
        if (Spot(bank, sk, lo - .4f, hi + 1.4f, out x, out gy) && Put(pool, atlas, bank, sk, x, gy, Chance(.5)) != null) n++;
        return n;
    }

    int LonePiece(double lo, double hi)
    {
        for (int k = 0; k < 3; k++)
        {
            int i = rng.Next(TideTuning.Lone.Length);
            if (i == lastLone) i = (i + 1) % TideTuning.Lone.Length;
            string name = TideTuning.Lone[i];
            float s = Rand(TideTuning.LandmarkMin, TideTuning.LandmarkMax);
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
        p.x = fire.x + TideTuning.Wind * fire.size * .3f;
        p.vx = TideTuning.Wind * Rand(.03f, .06f);
        p.vy = Rand(.02f, .05f);
        p.life = TideTuning.PallLife;
        p.rate = set.Spec.Rate("ground");
        p.phase = TideTuning.PallAlpha * Rand(.8f, 1f);
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

    // ----------------------------------------------------------- launch sites --

    // A launch site entering at the top (or at screen `y`), where its rule fits.
    public bool SpawnSite(float y, int kind = -1)
    {
        if (!planner.Ready) return false;
        if (kind < 0)
        {
            int total = 0;
            foreach (int w in TideTuning.SiteWeights) total += w;
            int r = rng.Next(total);
            for (kind = 0; kind < TideTuning.SiteWeights.Length - 1; kind++)
            {
                r -= TideTuning.SiteWeights[kind];
                if (r < 0) break;
            }
        }
        var closed = siteClosed[kind];
        if (closed == null || siteOpen[kind] == null) return false;
        float size = Rand(TideTuning.SiteMin, TideTuning.SiteMax);
        double lo = float.IsNaN(y) ? planner.Travel + HalfH + .95f : planner.GroundY(y) - .3f;
        double hi = lo + (float.IsNaN(y) ? .9f : .6f);
        float x; double gy;
        if (!planner.Find(TideTuning.Rule(TideTuning.SiteClosed[kind], openSea), size, XMin(size), XMax(size), lo, hi, rng, out x, out gy))
        {
            // the elite must still get its site: relax the ground rule, keep the room
            if (!planner.Find(new PieceRule(GroundClass.Any, 0f), size, XMin(size), XMax(size), lo, hi, rng, out x, out gy)) return false;
        }
        var p = Put(sites, siteArt, TideTuning.SiteClosed[kind], size, x, gy, false);
        if (p == null) return false;
        p.kind = kind;
        p.tier = 0;
        p.life = 0f;
        int i = sites.items.IndexOf(p);
        if (i >= 0 && lamps[i] != null) { lamps[i].sprite = lampsOff; lamps[i].enabled = lampsOff != null; }
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

    void StepSites(float dt)
    {
        for (int i = 0; i < sites.items.Count; i++)
        {
            var p = sites.items[i];
            if (!p.active) continue;
            int launch = LaunchState(p);
            if (launch > 0) { p.tier = launch; p.life = TideTuning.SiteCloseDelay; }
            else if (p.tier > 0)
            {
                p.life -= dt;
                p.tier = p.life <= 0f ? 0 : 3;
            }
            bool open = p.tier > 0;
            var want = open ? siteOpen[p.kind] : siteClosed[p.kind];
            if (p.sr.sprite != want) p.sr.sprite = want;
            var lamp = lamps[i];
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
            if (p.size < TideTuning.SiteMinSize) continue;
            if (p.y < -HalfH * .15f || p.y > HalfH - p.size * .3f) continue;
            if (Mathf.Abs(p.x) > HalfW - .25f) continue;
            Bounds b = p.sr.sprite.bounds;
            into.Add(new LandingSite
            {
                anchor = p.root,
                local = new Vector3(b.center.x, b.center.y, 0f),     // the measured emergence point: the cell centre
                scale = Mathf.Clamp(p.size * TideTuning.EmergeScalePerUnit, TideTuning.EmergeScaleMin, TideTuning.EmergeScaleMax),
                order = p.sr.sortingOrder + 2,
                id = TideTuning.SiteIdBase + i,
                kind = TideTuning.SiteKinds[p.kind],
                emerge = true,
            });
        }
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
        SetSprite(b, Pick(banks), Rand(TideTuning.CeilingMin, TideTuning.CeilingMax));
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
        SetSprite(w, Pick(wispArt), Rand(TideTuning.WispMin, TideTuning.WispMax));
        if (Chance(.5)) w.body.localScale = new Vector3(-1f, 1f, 1f);
        w.x = Rand(-HalfW * .7f, HalfW * .7f);
        w.y = SpawnY(w.size);
        w.vx = TideTuning.Wind * TideTuning.WispDrift * Rand(.7f, 1.3f);
        w.rate = set.Spec.Rate("wisps");
        w.phase = Rand(TideTuning.WispAlphaMin, TideTuning.WispAlphaMax) * CloudCover.Alpha(set.Spec.CloudDensity());
        Place(w);
        Tinted(w, 0f);
    }

    void SpawnMist(float y)
    {
        if (mistArt.Length == 0) return;
        var m = mist.Spawn();
        if (m == null) return;
        SetSprite(m, Pick(mistArt), HalfW * 2f * Rand(TideTuning.MistWidthMin, TideTuning.MistWidthMax));
        if (Chance(.5)) m.body.localScale = new Vector3(-1f, 1f, 1f);
        m.x = Rand(-HalfW * .3f, HalfW * .3f);
        m.y = float.IsNaN(y) ? SpawnY(m.size * .5f) : y;
        m.vx = TideTuning.Wind * TideTuning.MistDrift * Rand(.6f, 1.4f);
        m.rate = set.Spec.Rate("mist");
        m.phase = Rand(TideTuning.MistAlphaMin, TideTuning.MistAlphaMax) * CloudCover.Alpha(set.Spec.CloudDensity());
        Place(m);
        Tinted(m, 0f);
    }

    public void ForceGust() { sheetsLeft = TideTuning.GustSheets; sheetIn = 0f; Gusts++; }

    // One ash gust sheet: in from the upper left (with the wind), sweeping
    // across, faded in and out over its crossing.
    void SpawnSheet()
    {
        if (gustArt.Length == 0) return;
        var g = gusts.Spawn();
        if (g == null) return;
        SetSprite(g, Pick(gustArt), Rand(TideTuning.GustMin, TideTuning.GustMax));
        float dir = TideTuning.Wind >= 0f ? 1f : -1f;
        g.x = -dir * (HalfW + g.size * .25f);
        g.y = Rand(HalfH * .1f, HalfH * .9f);
        float speed = TideTuning.GustSpeed * Rand(.85f, 1.15f);
        g.vx = dir * speed;
        g.vy = -speed * .35f;
        g.life = (2f * HalfW + g.size * .6f) / speed;
        g.rate = set.Spec.Rate("gusts");
        Place(g);
        Tinted(g, 0f);
    }

    void BuildParticles()
    {
        foreach (var s in rain.items)
        {
            if (rainArt.Length == 0) break;
            s.Show(true);
            SetSprite(s, Pick(rainArt), Rand(TideTuning.RainMin, TideTuning.RainMax));
            s.x = Rand(-HalfW, HalfW);
            s.y = Rand(-HalfH, HalfH);
            s.phase = Rand(0f, 6.28f);
            s.rate = set.Spec.Rate("rain");
            Place(s);
        }
    }
}
