using System.Collections.Generic;
using UnityEngine;

// Verdant's backdrop: the ship flies HIGH (r1, 2026-11-28: GroundZoom .49, as Frost / Desert / Ember) over the jungle planet, just
// under a lime-white spore-cloud ceiling, looking down on a forest full of rivers, wildfires and (soon) its living props.
//
//   GROUND     one of four PixelLab-repainted tile sets per landing (BackdropVariants), 896 x 4096, drawn at VerdantTuning.TileZoom
//              (two .49 columns side by side): v1 AUTUMN canopy sea with rivers and waterfalls, v2 swamp delta, v3 overgrown ruined
//              city, v4 the NIGHT side's bioluminescent forest (VerdantTuning.Night: drawn darker, lamps / fireflies / fire glow
//              emphasised). Each set's v<n>/mask.txt is its 64 x 292 affinity grid (W water, B built, O open, C canopy).
//   PIECES     the WILDFIRE grounds (the fires atlas: burn fronts, burn patches, coal beds, burnt forest, firebreaks) with their smoke
//              palls, PINNED to the mid tile and placed where its mask says they belong (GroundPlanner, VerdantTuning.Rules), grown as
//              clusters (SpawnCluster: a burn front with patches / beds downwind and a smoke pall over it). Their loops -- flames,
//              wildfire smoke, beacons, spore bursts -- sit on MEASURED points (VerdantAmbientCatalog), all leaning with the one wind.
//              The round-1 landmarks, pipes and their clusters (refineries, towers, relays, silos, domes, barges, bridges, pipe runs)
//              RETIRED 2026-11-28: the new living props replace them (art: Backdrop3/v<n>/props, VerdantPropsArt; NOT drawn yet:
//              VerdantTuning.PropsEnabled, the placement is another agent's).
//   SITES      the elites' launch sites on the same pinned plane (root hangar, river bay, pod pad, tower bay, hatch), shut until the
//              launch tell opens them.
//   WEATHER    the CEILING of spore cloud (soft painted banks), thick at the start (continuing the planetfall's cloud break), held
//              CeilingHold s, then clearing fast, gone by CeilingClearSeconds (CloudCover); then only a light scattering of wisps
//              (rain-curtain paintings drifting with the wind) and low mist, POLLEN GUSTS sweeping across now and then, drifting
//              spores, and fireflies (many on the night side). The humid FRONT wall is the weather hazard (wx_hazard row 0).
//
// Everything sits far below gameplay (BackdropCatalog.BaseOrder); the pools are fixed and a frame allocates nothing.
public static class VerdantTuning
{
    // ---- brightness ----------------------------------------------------------
    // The Verdant art is painted lit (tile value p90 ~.47): the day side is
    // drawn as painted (1). The NIGHT side (v4) is drawn darker by its entry
    // in VariantBrightness (k < 1 is a darkening curve, BackdropGrade) while
    // its lights are drawn stronger (NightLightBoost). One number each.
    public static float Brightness = 1f;
    // r1 (2026-11-28): the repainted grounds measure (as drawn at 1: lane median linear luminance over the 49% of the tile one screen
    // shows, worst window; the bright shots' 7:1 against the lane needs <= .077): v1 .051, v2 .074 (the swamp's pale channels), v3 .061,
    // v4 .029 (the night side). v1 is drawn at .95 (its value p90 .51 under the tile cap .52), v2 at .9 (.062), v3 at .95 (.056) and the
    // night side at .85 (value median .19 against the day's .27: well under the .92 rule).
    public static readonly float[] VariantBrightness = { 1f, .95f, .9f, .95f, .85f };   // index = variant (0 none)
    public static readonly bool[] Night = { false, false, false, false, true };
    public static float NightLightBoost = 1.45f;
    public static float PlumeShare = 1f;
    // the stack / wildfire smoke is painted dark grey: lifted so it reads
    // as pale smoke over the green canopy
    public static float PlumeLift = 1.6f;
    public static float LoopBrightness = 1f;
    public static float LightBoostNow = 1f;           // set by the director for its variant

    // ---- ALTITUDE (2026-11-28, the user-approved r1 ground; as Desert's / Ember's) ----------------------------------------------
    // The ground is seen from GroundZoom (.49) of the old view; the r1 tiles (896 x 4096, PixelLab repaints of the old layouts, quilted)
    // hold TWO columns side by side, so they are drawn at TileZoom (2 x GroundZoom) of the view's width (BackdropCatalog.Spec.groundZoom).
    // Everything that stands on it is keyed to GroundZoom (Spec.pieceZoom), never TileZoom: the ground layers' rates, the sites, the
    // fires, the props, the cover boulders; x PieceFit (.7, as Frost) for the pieces' sizes.
    public const float GroundZoom = .49f, TileZoom = 2f * GroundZoom, PieceFit = .7f;
    public const float PieceScale = GroundZoom * PieceFit;
    public const float GroundRate = .024f * GroundZoom;               // the mid tile's (and everything pinned to it) parallax rate
    // where a new piece is looked for: a band SpawnBand tall starting SpawnAbove over the top of the view
    public static float SpawnAbove = .95f * PieceScale, SpawnBand = .9f * GroundZoom, SpawnRetry = .25f * GroundZoom;

    // The LIVING PROPS (11 kinds x 4 variants x 8 frames + the beast walkers, Backdrop3/v<n>/props, VerdantPropsArt) are installed as ART
    // only. Nothing draws them until the placement lands (it flips this flag): the director does not even load the atlas (and
    // WorldArtPreload does not queue it) while it is false.
    public static bool PropsEnabled = false;

    // ---- ground pieces (the wildfires: the only round-1 pieces kept besides the sites) -------------------------------------------
    public static float FireMin = 1.4f * PieceScale, FireMax = 1.75f * PieceScale, SatelliteMin = 1.05f * PieceScale, SatelliteMax = 1.35f * PieceScale;
    public static float ClusterGapMin = 9f * GroundZoom, ClusterGapMax = 16f * GroundZoom;     // scrolled ground distance after a wildfire cluster
    public static float LoneGapMin = 5f * GroundZoom, LoneGapMax = 9f * GroundZoom;            // ... after a lone burn piece
    public static float Wind = 1f;                                        // +1: plumes lean right (as painted)
    public static float PlateFeather = BackdropGrade.PlateFeatherTexels;

    // what each drawing may stand on (GroundPlanner)
    static readonly PieceRule Clearing = new PieceRule(GroundClass.Open | GroundClass.Canopy | GroundClass.Built, .85f, .3f);
    static readonly PieceRule Industry = new PieceRule(GroundClass.Land, .85f, .3f);
    static readonly PieceRule Fire = new PieceRule(GroundClass.Canopy | GroundClass.Open, .8f, .3f);
    static readonly PieceRule Bank = new PieceRule(GroundClass.Land, .6f, .28f, GroundClass.Water);
    static readonly PieceRule Anywhere = new PieceRule(GroundClass.Any, 0f, .2f);

    public static PieceRule Rule(string piece)
    {
        if (piece == null) return Anywhere;
        if (piece.StartsWith("riverbay")) return Bank;
        if (piece.StartsWith("towerbay")) return Industry;
        if (piece.StartsWith("podpad") || piece.StartsWith("hatch")) return Clearing;
        if (piece.StartsWith("burn") || piece.StartsWith("coal") || piece.StartsWith("scorched") ||
            piece.StartsWith("firebreak") || piece.StartsWith("roothangar")) return Fire;
        return Anywhere;
    }

    // ---- clusters --------------------------------------------------------------
    public static readonly string[] Fronts = { "burnfront_00", "burnfront_01", "burnfront_02", "burnfront_03", "firebreak_00" };
    public static readonly string[] Satellites = { "burnpatch_00", "burnpatch_01", "burnpatch_02", "burnpatch_03", "burnpatch_04",
                                                   "coalbed_00", "coalbed_01", "burntforest_00", "burntforest_01", "scorched_00", "scorched_01" };

    // ---- launch sites ----
    // (x PieceScale / x GroundZoom: the same docks, seen from the new height, as often per ground scrolled)
    public static float SiteMin = 1.5f * PieceScale, SiteMax = 1.7f * PieceScale, SiteFirst = 3f * GroundZoom, SiteGapMin = 5f * GroundZoom, SiteGapMax = 8.5f * GroundZoom;
    public static float SiteCloseDelay = .9f, SiteMinSize = 1.2f * PieceScale;
    public static readonly string[] SiteClosed = { "roothangar_closed", "riverbay_closed", "podpad_idle", "towerbay_closed", "hatch_closed" };
    public static readonly string[] SiteOpen = { "roothangar_open", "riverbay_open", "podpad_active", "towerbay_open", "hatch_open" };
    public static readonly LandingKind[] SiteKinds = { LandingKind.RootHangar, LandingKind.RiverBay, LandingKind.PodPad, LandingKind.TowerBay, LandingKind.Hatch };
    public static readonly int[] SiteWeights = { 3, 2, 2, 3, 2 };
    public static Vector2 LampsAt = new Vector2(128f, 224f);    // the lamp row's centre on the site's cell
    public static float LampsScale = .5f;
    public const float EmergeScalePerUnit = .22f, EmergeScaleMin = .26f * PieceScale, EmergeScaleMax = .4f;
    public const int SiteIdBase = 600;

    // ---- weather (the atlas bakes its translucency in: banks <= 80/255) ----
    // CLOUD COVER: the knobs (CloudCover documents the timeline). How much
    // cloud drifts over the ground once the ceiling has cleared: 1 = a
    // light scattering (default), 2 = about twice as much, 0 = none.
    public static float CloudDensity = CloudCover.Density;
    public static float CeilingHold = CloudCover.CeilingHold;                   // s at full thickness after landing
    public static float CeilingClearSeconds = CloudCover.CeilingClearSeconds;   // s: the ceiling is gone
    public static float CeilingMin = 4.2f, CeilingMax = 5.6f, CeilingGap = 1.5f, CeilingLowShare = .55f;
    public static float CeilingThicken = 6f;
    // ... and paled toward the planetfall's lime-white cloud break
    public static float CeilingLift = 2.1f, CeilingSaturation = .6f;
    // the banks are painted teal-green: tinted toward the deck's lime-white
    public static Color CeilingTint = new Color(1f, 1f, .78f, 1f);
    // wisps and mist at CloudDensity 1 (density divides the gaps, lifts the alpha);
    // they drift downwind (Wind)
    public static float WispMin = 2.2f, WispMax = 3.2f, WispAlphaMin = .35f, WispAlphaMax = .55f;
    public static float WispEveryMin = 6f, WispEveryMax = 10f, WispDrift = .18f;
    public static float MistAlphaMin = .35f, MistAlphaMax = .5f;
    public static float MistEveryMin = 14f, MistEveryMax = 22f, MistDrift = .08f;
    public static float PollenFirst = 12f, PollenEveryMin = 14f, PollenEveryMax = 22f;
    public static int PollenSheets = 3;
    public static float PollenStagger = .6f, PollenSpeed = 3.4f, PollenMin = 4.5f, PollenMax = 6f, PollenMaxAlpha = 1f;
    public static int SporeCount = 5;
    public static float SporeMin = .7f, SporeMax = 1.1f, SporeAlpha = .4f;
    public static int FirefliesDay = 3, FirefliesNight = 14;
    public static float FireflySize = .9f;
    public static float PallAlpha = .5f, PallLife = 22f;
}

public partial class VerdantDirector : PlanetDirector
{
    BackdropPool fires, sites, palls, mist, wisps, ceiling, pollen, spores, flies;
    readonly SpriteRenderer[] lamps = new SpriteRenderer[3];
    Color[] dockTints;                                 // per site kind: its DockGrade colour on this ground set
    BackdropAtlas fireArt, siteArt, weather, lights;
    AmbientEmitters ambient;
    GroundPlanner planner;
    Sprite[] siteClosed, siteOpen, banks, wispArt, mistArt, pollenArt, pallArt, sporeArt, flyFrames;
    Sprite lampsOff, lampsOn;
    float groundTravel, groundGap, siteTravel, siteGap, ceilingTravel;
    bool night;
    float mistIn = 2.5f, wispIn = 6f;
    readonly Timer gustTimer = new Timer(VerdantTuning.PollenEveryMin, VerdantTuning.PollenEveryMax, VerdantTuning.PollenFirst);
    int sheetsLeft;
    float sheetIn;
    readonly List<Material> mats = new List<Material>();

    // Tests: > 0 seeds the next directors' streams (statistics over many runs).
    public static int SeedOverride;
    public VerdantDirector() : base(SeedOverride > 0 ? SeedOverride : 1990) { }

    public float Clock => clock;
    public bool IsNight => night;
    public int Gusts { get; private set; }
    public int SitesSpawned { get; private set; }
    public int Clusters { get; private set; }
    public AmbientEmitters Ambient => ambient;
    public GroundPlanner Planner => planner;
    public BackdropPool Fires => fires;
    public BackdropPool Sites => sites;
    public BackdropPool Ceiling => ceiling;
    public BackdropPool Pollen => pollen;
    public BackdropPool Palls => palls;
    public BackdropPool Fireflies => flies;
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
        VerdantTuning.LightBoostNow = night ? VerdantTuning.NightLightBoost : 1f;
        ambient = new AmbientEmitters(set, VerdantAmbientCatalog.Table) { Wind = VerdantTuning.Wind };
        planner = new GroundPlanner(set, "mid", GroundMask.Load("Verdant", set.Variant), 96);

        siteClosed = Sprites(siteArt, VerdantTuning.SiteClosed);
        dockTints = DockGrade.Tints("Verdant", set.Variant, VerdantTuning.SiteClosed);
        siteOpen = Sprites(siteArt, VerdantTuning.SiteOpen);
        lampsOff = siteArt.Get("lights_off");
        lampsOn = siteArt.Get("lights_on");
        banks = weather.Frames("cloud_bank");
        wispArt = weather.Frames("cloud_wisp");
        mistArt = weather.Frames("mist");
        pollenArt = weather.Frames("pollen");
        pallArt = weather.Frames("smokepall");
        sporeArt = weather.Frames("spore");
        flyFrames = lights.Frames("fireflies");

        // ground plane, back to front: (the living props: a placement pool belongs here, VerdantPropsArt), fires (-2), sites (0). The
        // landmarks and pipes of round 1 are retired (2026-11-28).
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
            float k = VerdantTuning.LampsScale;
            var box = VerdantAmbientCatalog.Piece("lights_off");
            float rowY = box != null && box.box != null && box.box.Length == 4 ? (box.box[1] + box.box[3]) * .5f : 128f;
            go.transform.localPosition = new Vector3((VerdantTuning.LampsAt.x - 128f) / ppu,
                                                     (128f - VerdantTuning.LampsAt.y) / ppu - (128f - rowY) / ppu * k, 0f);
            go.transform.localScale = new Vector3(k, k, 1f);
        }
        palls = Pool("palls", 4);
        mist = Pool("mist", 3);
        wisps = Pool("wisps", 4);
        ceiling = Pool("ceiling", 26);
        pollen = Pool("pollen", VerdantTuning.PollenSheets + 1);
        spores = Pool("spores", VerdantTuning.SporeCount);
        flies = Pool("fireflies", Mathf.Max(VerdantTuning.FirefliesDay, VerdantTuning.FirefliesNight));

        BuildProps();
        PlateMaterial = Grade(fires, "ground", 1f, VerdantTuning.PlateFeather);
        Grade(sites, "ground", 1f, VerdantTuning.PlateFeather);
        Grade(palls, "palls");
        Grade(mist, "mist");
        Grade(wisps, "wisps");
        CeilingMaterial = Grade(ceiling, "ceiling", VerdantTuning.CeilingThicken, 0f, VerdantTuning.CeilingLift, VerdantTuning.CeilingSaturation);

        // the opening view: the ground already dressed, under a thick ceiling
        groundGap = Rand(VerdantTuning.LoneGapMin, VerdantTuning.LoneGapMax);
        siteGap = VerdantTuning.SiteFirst;
        if (planner.Ready)
        {
            double t = planner.Travel;
            for (float y = -HalfH * .85f; y < HalfH * .8f; y += Rand(5.5f, 8f))
                SpawnCluster(t + y, t + y + 1.2f * VerdantTuning.GroundZoom);
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
    public static float CeilingDensity(float t) { return BackdropCatalog.For("Verdant").CeilingDensity(t); }

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
            else groundGap = groundTravel + VerdantTuning.SpawnRetry;     // nothing fitted: look again a little further on
        }
        ceilingTravel += set.Spec.Rate("ceiling") * v * dt;
        float density = set.Spec.CeilingDensity(clock);
        if (ceilingTravel >= VerdantTuning.CeilingGap && density > CloudCover.BankSpawnFloor)
        {
            ceilingTravel = 0f;
            SpawnBank(Rand(-HalfW * .8f, HalfW * .8f), float.NaN);
        }
        float air = set.Spec.CloudDensity();
        if (CloudCover.Tick(ref mistIn, dt, air, VerdantTuning.MistEveryMin, VerdantTuning.MistEveryMax, rng)) SpawnMist(float.NaN);
        if (CloudCover.Tick(ref wispIn, dt, air, VerdantTuning.WispEveryMin, VerdantTuning.WispEveryMax, rng)) SpawnWisp();
        if (gustTimer.Tick(dt, rng)) ForceGust();
        if (sheetsLeft > 0)
        {
            sheetIn -= dt;
            if (sheetIn <= 0f) { SpawnSheet(); sheetsLeft--; sheetIn = VerdantTuning.PollenStagger; }
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
            float low = Mathf.Lerp(VerdantTuning.CeilingLowShare, 1f, y01 * y01 * (3f - 2f * y01));
            Color c = VerdantTuning.CeilingTint;
            c.a = b.phase * density * low * set.Alpha;
            b.sr.color = c;
        }
        foreach (var g in pollen.items)
        {
            if (!g.active || !Drift(g, dt, v)) continue;
            float k = Mathf.Clamp01(g.age / Mathf.Max(.01f, g.life));
            if (k >= 1f) { Despawn(g); continue; }
            Tinted(g, VerdantTuning.PollenMaxAlpha * Mathf.Sin(k * Mathf.PI));
        }
        foreach (var s in spores.items)
        {
            if (!s.active) continue;
            Recycle(s, .18f, dt, v, .35f);
            Tinted(s, VerdantTuning.SporeAlpha);
        }
        int liveFlies = night ? VerdantTuning.FirefliesNight : VerdantTuning.FirefliesDay;
        for (int i = 0; i < flies.items.Count; i++)
        {
            var f = flies.items[i];
            if (!f.active) continue;
            Recycle(f, .12f, dt, v, .6f);
            f.Animate();
            Tinted(f, i < liveFlies ? 1f : 0f);
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

    // A cluster (or a launch site, when one is due) entering at the top.
    // Returns the scrolled ground distance to wait before the next, 0 when
    // nothing fitted.
    float SpawnGround()
    {
        double t = planner.Travel;
        double lo = t + HalfH + VerdantTuning.SpawnAbove, hi = lo + VerdantTuning.SpawnBand;
        if (siteTravel >= siteGap && SpawnSite(float.NaN))
        {
            siteTravel = 0f;
            siteGap = Rand(VerdantTuning.SiteGapMin, VerdantTuning.SiteGapMax);
            return Rand(VerdantTuning.LoneGapMin, VerdantTuning.LoneGapMax);
        }
        int n = SpawnCluster(lo, hi);
        if (n == 0) return 0f;
        return n > 1 ? Rand(VerdantTuning.ClusterGapMin, VerdantTuning.ClusterGapMax)
                     : Rand(VerdantTuning.LoneGapMin, VerdantTuning.LoneGapMax);
    }

    float XMin(float size) { return -EdgeX + size * .3f; }
    float XMax(float size) { return EdgeX - size * .3f; }

    // Grows one wildfire cluster with its anchor in the ground band [lo, hi]; returns how many pieces it placed (0: nothing fitted).
    // (The round-1 refinery / pipeline / river / lone-landmark clusters retired 2026-11-28; the living props are placed elsewhere.)
    public int SpawnCluster(double lo, double hi)
    {
        int n = Wildfire(lo, hi);
        if (n > 0) Clusters++;
        return n;
    }

    // Draws `name` from its atlas at x, gy (pinned), claiming its footprint.
    BackdropPiece Put(BackdropPool pool, BackdropAtlas atlas, string name, float size, float x, double gy, bool mirror)
    {
        var sprite = atlas.Get(name);
        if (sprite == null) return null;
        var rule = VerdantTuning.Rule(name);
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
        p.dockTint = Color.white;
        p.kind = 0;
        Place(p);
        Tinted(p, 1f);
        ambient.Attach(p, name, rng);
        return p;
    }

    bool Spot(string name, float size, double lo, double hi, out float x, out double gy)
    {
        return planner.Find(VerdantTuning.Rule(name), size, XMin(size), XMax(size), lo, hi, rng, out x, out gy);
    }

    // A burn front with its burn patches / coal beds downwind and a smoke
    // pall hanging over it.
    int Wildfire(double lo, double hi)
    {
        string front = Pick(VerdantTuning.Fronts);
        float sf = Rand(VerdantTuning.FireMin, VerdantTuning.FireMax);
        float xf; double gf;
        if (!Spot(front, sf, lo, hi, out xf, out gf)) return 0;
        var f = Put(fires, fireArt, front, sf, xf, gf, false);
        if (f == null) return 0;
        int n = 1, want = 1 + rng.Next(2);
        float wind = VerdantTuning.Wind;
        for (int k = 0; k < 6 && n <= want; k++)
        {
            string sat = Pick(VerdantTuning.Satellites);
            float ss = Rand(VerdantTuning.SatelliteMin, VerdantTuning.SatelliteMax);
            float dir = k % 2 == 0 ? wind : -wind;
            float xs = xf + dir * Rand(.75f, 1.15f) * (sf + ss) * .5f;
            double gs = gf + Rand(-.7f, .5f) * VerdantTuning.PieceScale;
            if (xs < XMin(ss) || xs > XMax(ss)) continue;
            var rule = VerdantTuning.Rule(sat);
            if (!planner.Accepts(rule, xs, gs, ss) || !planner.Free(xs, gs, GroundPlanner.HalfX(rule, ss), GroundPlanner.HalfY(rule, ss))) continue;
            if (Put(fires, fireArt, sat, ss, xs, gs, Chance(.5)) != null) n++;
        }
        SpawnPall(f);
        return n;
    }

    // A smoke pall over a fire: rides the fire's ground spot (pinned),
    // rising slowly and drifting downwind as it ages, fading in and out.
    void SpawnPall(BackdropPiece fire)
    {
        if (pallArt.Length == 0) return;
        var p = palls.Spawn();
        if (p == null) return;
        SetSprite(p, Pick(pallArt), Rand(2.2f, 3f) * VerdantTuning.PieceScale);
        if (Chance(.5)) p.body.localScale = new Vector3(-1f, 1f, 1f);
        p.gy = fire.gy + fire.size * .35f;
        p.x = fire.x + VerdantTuning.Wind * fire.size * .3f;
        p.vx = VerdantTuning.Wind * Rand(.03f, .06f);
        p.vy = Rand(.02f, .05f);
        p.life = VerdantTuning.PallLife;
        p.rate = set.Spec.Rate("ground");
        p.phase = VerdantTuning.PallAlpha * Rand(.8f, 1f);
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

    // Preview / tests: clears the ground plane and shows `name` alone at screen x, y (pinned there), size `size`, its rule ignored.
    public BackdropPiece Showcase(string name, float size, float x, float y, bool mirror = false)
    {
        foreach (var pool in new[] { fires, sites })
            foreach (var q in pool.items)
                if (q.active) { planner.Release(q.footprint); q.footprint = -1; Despawn(q); }
        foreach (var q in palls.items) if (q.active) Despawn(q);
        bool fire = fireArt.Has(name), site = siteArt.Has(name);
        var atlas = fire ? fireArt : siteArt;
        var p = Put(fire ? fires : sites, atlas, name, size, x, planner.GroundY(y), mirror);
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
        if (DockGrade.AnyOnGround(sites, HalfH)) return false;      // one dock at a time: the next comes once this one has scrolled off
        if (kind < 0)
        {
            int total = 0;
            foreach (int w in VerdantTuning.SiteWeights) total += w;
            int r = rng.Next(total);
            for (kind = 0; kind < VerdantTuning.SiteWeights.Length - 1; kind++)
            {
                r -= VerdantTuning.SiteWeights[kind];
                if (r < 0) break;
            }
        }
        var closed = siteClosed[kind];
        if (closed == null || siteOpen[kind] == null) return false;
        float size = Rand(VerdantTuning.SiteMin, VerdantTuning.SiteMax);
        double lo = float.IsNaN(y) ? planner.Travel + HalfH + .95f : planner.GroundY(y) - .3f;
        double hi = lo + (float.IsNaN(y) ? .9f : .6f);
        float x; double gy;
        if (!planner.Find(VerdantTuning.Rule(VerdantTuning.SiteClosed[kind]), size, XMin(size), XMax(size), lo, hi, rng, out x, out gy))
        {
            // the elite must still get its site: relax the ground rule, keep the room
            if (!planner.Find(new PieceRule(GroundClass.Land, .5f), size, XMin(size), XMax(size), lo, hi, rng, out x, out gy)) return false;
        }
        var p = Put(sites, siteArt, VerdantTuning.SiteClosed[kind], size, x, gy, false);
        if (p == null) return false;
        p.dockTint = dockTints[kind];
        Tinted(p, 1f);
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
            if (launch > 0) { p.tier = launch; p.life = VerdantTuning.SiteCloseDelay; }
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
            if (p.size < VerdantTuning.SiteMinSize) continue;
            if (p.y < -HalfH * .15f || p.y > HalfH - p.size * .3f) continue;
            if (Mathf.Abs(p.x) > HalfW - .25f) continue;
            Bounds b = p.sr.sprite.bounds;
            into.Add(new LandingSite
            {
                anchor = p.root,
                local = new Vector3(b.center.x, b.center.y, 0f),     // the measured emergence point: the cell centre
                scale = Mathf.Clamp(p.size * VerdantTuning.EmergeScalePerUnit, VerdantTuning.EmergeScaleMin, VerdantTuning.EmergeScaleMax),
                order = p.sr.sortingOrder + 2,
                id = VerdantTuning.SiteIdBase + i,
                kind = VerdantTuning.SiteKinds[p.kind],
                emerge = true,
            });
        }
    }

    // The elite director wants a site of one of `launchFrom`'s kinds in view soon (LandingSites.BringHome): the ground is fixed, so none is
    // conjured -- but the site spawner is asked for one of that kind at the top of the view now (it scrolls in over the next seconds, as the
    // ground pieces always do), unless one of them is already in view or on its way. False when none of the kinds is one of Verdant's
    // sites or there is no room for one on the ground. (As FrostDirector.BringHome.)
    public override bool BringHome(string launchFrom)
    {
        if (sites == null || !planner.Ready || string.IsNullOrEmpty(launchFrom)) return false;
        var want = new List<int>(VerdantTuning.SiteKinds.Length);   // (a rare call: one per elite)
        bool mine = false;      // (one of the kinds it names is a dock of this world, even if none can be placed here)
        foreach (string part in launchFrom.Split(','))
        {
            var k = LandingSite.KindOf(part.Trim());
            if (k == null) continue;
            for (int t = 0; t < VerdantTuning.SiteKinds.Length; t++)
            {
                if (VerdantTuning.SiteKinds[t] == k.Value) mine = true;
                if (VerdantTuning.SiteKinds[t] == k.Value && siteClosed[t] != null && siteOpen[t] != null && !want.Contains(t)) want.Add(t);
            }
        }
        if (want.Count == 0 && mine && DockGrade.Strict)     // (none of the kinds it names has a dock here: any dock will do)
            for (int t = 0; t < VerdantTuning.SiteKinds.Length; t++)
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
        SetSprite(b, Pick(banks), Rand(VerdantTuning.CeilingMin, VerdantTuning.CeilingMax));
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
        SetSprite(w, Pick(wispArt), Rand(VerdantTuning.WispMin, VerdantTuning.WispMax));
        if (Chance(.5)) w.body.localScale = new Vector3(-1f, 1f, 1f);
        w.x = Rand(-HalfW * .7f, HalfW * .7f);
        w.y = SpawnY(w.size);
        w.vx = VerdantTuning.Wind * VerdantTuning.WispDrift * Rand(.7f, 1.3f);
        w.rate = set.Spec.Rate("wisps");
        w.phase = Rand(VerdantTuning.WispAlphaMin, VerdantTuning.WispAlphaMax) * CloudCover.Alpha(set.Spec.CloudDensity());
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
        m.vx = VerdantTuning.Wind * VerdantTuning.MistDrift * Rand(.6f, 1.4f);
        m.rate = set.Spec.Rate("mist");
        m.phase = Rand(VerdantTuning.MistAlphaMin, VerdantTuning.MistAlphaMax) * CloudCover.Alpha(set.Spec.CloudDensity());
        Place(m);
        Tinted(m, 0f);
    }

    public void ForceGust() { sheetsLeft = VerdantTuning.PollenSheets; sheetIn = 0f; Gusts++; }

    // One pollen gust sheet: in from the upper left (with the wind), sweeping
    // across, faded in and out over its crossing.
    void SpawnSheet()
    {
        if (pollenArt.Length == 0) return;
        var g = pollen.Spawn();
        if (g == null) return;
        SetSprite(g, Pick(pollenArt), Rand(VerdantTuning.PollenMin, VerdantTuning.PollenMax));
        float dir = VerdantTuning.Wind >= 0f ? 1f : -1f;
        g.x = -dir * (HalfW + g.size * .25f);
        g.y = Rand(HalfH * .1f, HalfH * .9f);
        float speed = VerdantTuning.PollenSpeed * Rand(.85f, 1.15f);
        g.vx = dir * speed;
        g.vy = -speed * .35f;
        g.life = (2f * HalfW + g.size * .6f) / speed;
        g.rate = set.Spec.Rate("pollen");
        Place(g);
        Tinted(g, 0f);
    }

    void BuildParticles()
    {
        foreach (var s in spores.items)
        {
            if (sporeArt.Length == 0) break;
            s.Show(true);
            SetSprite(s, Pick(sporeArt), Rand(VerdantTuning.SporeMin, VerdantTuning.SporeMax));
            s.x = Rand(-HalfW, HalfW);
            s.y = Rand(-HalfH, HalfH);
            s.phase = Rand(0f, 6.28f);
            s.rate = set.Spec.Rate("spores");
            Place(s);
        }
        foreach (var f in flies.items)
        {
            if (flyFrames.Length == 0) break;
            f.Show(true);
            SetSprite(f, flyFrames[0], VerdantTuning.FireflySize * Rand(.8f, 1.2f));
            f.frames = flyFrames;
            f.fps = 6f * Rand(.85f, 1.15f);
            f.age = Rand(0f, 3f);
            f.x = Rand(-HalfW, HalfW);
            f.y = Rand(-HalfH, HalfH);
            f.phase = Rand(0f, 6.28f);
            f.rate = set.Spec.Rate("fireflies");
            Place(f);
        }
    }
}
