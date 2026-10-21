using System.Collections.Generic;
using UnityEngine;

// Storm's backdrop: the ship flies at atmosphere level inside a gas giant's
// thunderstorm, looking down on a churning cloud sea under a storm-cloud ceiling.
// Nothing about it may read as Tide's ocean: no water, no green, no rigs.
//
//   GROUND     one of four tile sets per landing (BackdropVariants): v1 the banded deck
//              (wind-sheared slate bands and cyclone scars), v2 the thunderhead field
//              (towering lit cumulonimbus tops with lightning in the canyons), v3 the
//              NIGHT side, v4 gust-front shelf arcs.
//   PIECES     landmarks pinned to the mid tile (GroundPlanner): anvil thunderhead towers,
//              lightning-rod masts, skyhook anchors, floating refineries, siphon towers, an
//              airship dock, a gantry, a turbine raft, a relay dish; clusters (a storm
//              front of towers, a refinery with its neighbour, a mast with a dish, a
//              skyhook) with long calm gaps. Elite launch sites on the same plane.
//   LIGHTNING  StormLightning schedules the weather: sheet flashes that light the cloud
//              layers (soft glow sprites, a faint bloom over the ceiling), forked
//              cloud-to-cloud bolts and strikes down onto a lightning-rod mast. It is
//              deliberately gentle (photosensitivity): see StormTuning's limits, held
//              by StormBackdropTest. No sound: the user authors the world's ambience.
//   WEATHER    the CEILING of storm cloud (continuing the planetfall deck) held
//              CeilingHold s then clearing fast (CloudCover), wisps, mist, wind-shear
//              gusts, heavy rain sheets, floating debris.
//
// Everything sits far below gameplay (BackdropCatalog.BaseOrder); pools are fixed and a
// frame allocates nothing.
public static class StormTuning
{
    // ---- brightness ------------------------------------------------------------
    public static float Brightness = 1f;
    // Storm ships FOUR ground sets (BackdropVariants rotates through them, never the same one twice in a row): v1 banded deck and v2 thunderhead
    // field (day), v3 the NIGHT side (Night[3]), v4 the gust-front shelf arcs (day). A set that is not on disk is never picked.
    public const int Variants = 4;
    public static readonly float[] VariantBrightness = { 1f, .6f, .6f, .6f, .6f };   // index = variant (0 none)
    public static readonly bool[] Night = { false, false, false, true, false };

    // ---- ground pieces -----------------------------------------------------------
    public static float LandmarkMin = 1.5f, LandmarkMax = 1.75f;
    public static float TowerMin = 1.6f, TowerMax = 1.8f;
    public static float ClusterGapMin = 1.6f, ClusterGapMax = 3.2f;
    public static float LoneGapMin = .8f, LoneGapMax = 1.5f;
    public static float NeighbourGapMin = .1f, NeighbourGapMax = .5f;
    public static float Wind = 1f;                    // +1: gusts and rain lean right

    static readonly PieceRule Platform = new PieceRule(GroundClass.Open, .55f, .3f);
    static readonly PieceRule Tower = new PieceRule(GroundClass.Any, 0f, .3f);

    public static bool IsTower(string piece) { return piece != null && piece.StartsWith("anvil_tower"); }
    public static bool IsSpire(string piece) { return piece != null && piece.StartsWith("spire"); }
    public static PieceRule Rule(string piece) { return IsTower(piece) ? Tower : Platform; }

    // Every landmark drawing the director may place (tests: they must all exist in the landmarks atlas).
    public static string[] AllPieces()
    {
        var names = new List<string>();
        foreach (var group in new[] { Towers, Spires, Refineries, Neighbours, Skyhooks, Dishes, Lone })
            foreach (string n in group) if (!names.Contains(n)) names.Add(n);
        return names.ToArray();
    }

    public const int ClFront = 0, ClRefinery = 1, ClMast = 2, ClSkyhook = 3, ClLone = 4;
    public static readonly int[][] ClusterWeights =
    {
        new[] { 2, 2, 2, 2, 2 },   // (none)
        new[] { 2, 3, 2, 2, 2 },   // v1 banded deck: refineries and skyhooks on the bands
        new[] { 5, 1, 3, 1, 2 },   // v2 thunderhead field: towers and the masts that draw the bolts
        new[] { 2, 2, 4, 2, 2 },   // v3 night side: masts that draw the bolts
        new[] { 3, 2, 2, 3, 2 },   // v4 gust-front shelf arcs: skyhooks on the shelves
    };
    public static readonly string[] Towers = { "anvil_tower_00", "anvil_tower_01", "anvil_tower_02", "anvil_tower_03" };
    public static readonly string[] Spires = { "spire_00", "spire_01" };
    public static readonly string[] Refineries = { "refinery_float_00", "refinery_float_01", "refinery_float_02", "siphon_tower_00" };
    public static readonly string[] Neighbours = { "gantry_00", "airship_dock_00", "turbine_raft_00", "siphon_tower_00", "refinery_float_01" };
    public static readonly string[] Skyhooks = { "skyhook_00", "skyhook_01" };
    public static readonly string[] Dishes = { "relay_dish_00", "turbine_raft_00", "gantry_00" };
    public static readonly string[] Lone = { "spire_00", "spire_01", "skyhook_00", "skyhook_01", "refinery_float_00", "refinery_float_02", "airship_dock_00", "gantry_00", "turbine_raft_00", "relay_dish_00", "anvil_tower_00", "anvil_tower_02" };

    // The lightning rod's tip on a spire's 256 cell (x = 128 from the left, y from the top).
    public const float RodTipY = 40f;
    // A strike bolt's lower end on its 256 cell (bottom centre, y from the top).
    public const float StrikeEndY = 226f;

    // ---- launch sites ------------------------------------------------------------
    public static float SiteMin = 1.5f, SiteMax = 1.7f, SiteFirst = 3f, SiteGapMin = 5f, SiteGapMax = 8.5f;
    public static float SiteCloseDelay = 1.4f, SiteMinSize = 1.2f;
    // Entries 0-4 are the old two-drawing sites (sites atlas, kept as the elites' fallback homes); 5-8 the launch-strip ones (docks atlas:
    // <name>_00..05, DocksAtlas), for which SiteClosed / SiteOpen name the idle and the open cell (Rule picks the ground by the name).
    public static readonly string[] SiteNames = { "skyhook_dock", "refinery_pad", "balloon_hangar", "gantry_hatch", "cloud_bay", "airshipdock", "rodsilo", "skyhookcradle", "refinerybay" };
    public static readonly string[] SiteClosed = { "skyhook_dock_closed", "refinery_pad_closed", "balloon_hangar_closed", "gantry_hatch_closed", "cloud_bay_closed", "airshipdock_00", "rodsilo_00", "skyhookcradle_00", "refinerybay_00" };
    public static readonly string[] SiteOpen = { "skyhook_dock_open", "refinery_pad_open", "balloon_hangar_open", "gantry_hatch_open", "cloud_bay_open", "airshipdock_04", "rodsilo_04", "skyhookcradle_04", "refinerybay_04" };
    public static readonly LandingKind[] SiteKinds = { LandingKind.PadRing, LandingKind.RigBay, LandingKind.Hangar, LandingKind.TowerBay, LandingKind.Hatch,
                                                       LandingKind.StormAirshipDock, LandingKind.StormRodSilo, LandingKind.StormSkyhookCradle, LandingKind.StormRefineryBay };
    public static readonly int[] SiteWeights = { 0, 0, 0, 0, 0, 2, 2, 2, 2 };
    // where the elite comes out of the launch-strip sites: fractions of the drawing's bounds from its centre (y up), the pad pixel of Codex's
    // masters (new_docks_lights~/manifest.md: (384,430) on every 768 x 768 cell)
    public static readonly Vector2[] SitePads =
    {
        new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(0f, 0f),
        new Vector2(0f, -.0599f), new Vector2(0f, -.0599f), new Vector2(0f, -.0599f), new Vector2(0f, -.0599f),
    };

    // ---- the launch strips (the docks atlas): closed, lamp, opening, opening, open, closing ----
    // Codex's launch_v2 strips: 0 closed, 1 closed + a small amber lamp (never shown: the ship's own launch light is the tell), 2-3 the doors
    // opening, 4 fully open, 5 closing half shut. All six are in the atlas (build_launch_atlas.py), as <name>_00..05.
    public const string DocksAtlas = "docks";
    public const int LaunchCells = 6, LaunchIdle = 0, LaunchBlink = 1, LaunchOpening = 2, LaunchOpen = 3, LaunchHold = 4, LaunchClosing = 5;
    public const int FirstLaunchSite = 5;                      // SiteNames[FirstLaunchSite..] have a strip
    public static bool HasLaunchStrip(int kind) { return kind >= FirstLaunchSite && kind < SiteNames.Length; }
    // drawn width of a strip site, as a share of the usual SiteMin..SiteMax
    public static readonly float[] SiteWidthK = { 1f, 1f, 1f, 1f, 1f, 1f, 1f, 1f, 1f };
    // The tell: the site stays closed through its first share (the ship's own small blinking launch light, EliteShip.LaunchTell, is the
    // tell), then the doors open (2); the lift-off has them still opening (3 for its first share), then fully open (4, held until the elite
    // is clear); then they close for LaunchCloseSeconds (5) and the site is closed again. No flash, flare, halo, ring or lightning.
    public const float LaunchBlinkShare = .4f, LaunchOpenShare = .25f, LaunchCloseSeconds = .6f;

    // The vapour of an opening strip site (Storm's existing weather wisps, no new art): a pooled renderer per site slot plays the cloud_wisp
    // frames as a flipbook at the pad point while the strip is in its open cells (2-4), fading in over the opening and out through the closing.
    public const float MistAlpha = .6f, MistFadeIn = .45f, MistWidthShare = .9f, MistFps = 5f, MistRise = .3f;

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
    public static Vector2 LampsAt = new Vector2(128f, 128f);
    public static float LampsScale = .5f;
    public const float EmergeScalePerUnit = .22f, EmergeScaleMin = .26f, EmergeScaleMax = .4f;
    public const int SiteIdBase = 900;

    // ---- weather -------------------------------------------------------------------
    public static float CloudDensity = CloudCover.Density;
    public static float CeilingHold = CloudCover.CeilingHold;
    public static float CeilingClearSeconds = 13f;
    public static float CeilingMin = 4.2f, CeilingMax = 5.6f, CeilingGap = 1.5f, CeilingLowShare = .55f;
    public static float CeilingThicken = 2.2f;
    public static Color CeilingTint = new Color(.92f, .94f, 1f, 1f);
    public static float WispMin = 1.9f, WispMax = 2.8f, WispAlphaMin = .3f, WispAlphaMax = .45f;
    public static float WispEveryMin = 6f, WispEveryMax = 10f, WispDrift = .2f;
    public static float MistAlphaMin = .3f, MistAlphaMax = .42f;
    public static float MistWidthMin = .5f, MistWidthMax = .7f;
    public static float MistEveryMin = 14f, MistEveryMax = 22f, MistDrift = .1f;
    public static float GustFirst = 8f, GustEveryMin = 10f, GustEveryMax = 17f;
    public static int GustSheets = 3;
    public static float GustStagger = .6f, GustSpeed = 3.8f, GustMin = 4.5f, GustMax = 6f, GustMaxAlpha = 1f;
    public static float PallAlpha = .5f;

    // heavy rain: slanted sheets falling through the view (the streaks lean with the wind)
    public static int RainSheets = 8;
    public static float RainMin = 2.6f, RainMax = 3.4f, RainAlpha = .55f;
    public static float RainFall = 2.6f;             // extra downward units/s beyond the parallax
    public static float RainLean = .29f;              // sideways units per unit fallen (the art's streak slant)

    // floating debris tumbling past
    public static int DebrisCount = 5;
    public static float DebrisMin = .38f, DebrisMax = .62f, DebrisEveryMin = 3.5f, DebrisEveryMax = 8f;

    // ---- LIGHTNING (photosensitivity-safe by construction; StormBackdropTest holds every limit) ----
    // An EVENT is one weather moment (a sheet flash, a fork, a strike) and has one or two
    // PULSES (surges of light). No two pulses start closer than MinPulseGap, so the light never
    // surges faster than 1 / MinPulseGap per second (2.5 /s, below the 3 Hz photosensitivity
    // threshold), and events are further apart still. Peaks are low-alpha and soft-edged.
    public const float FirstEvent = 4.5f;                    // never in the opening seconds (the calm start, the ceiling)
    public const float EventGapMin = 2.4f, EventGapMax = 5.6f;
    public const float MinPulseGap = .4f;
    public const float RestrikeGapMin = .45f, RestrikeGapMax = .7f;
    public const float RestrikeChance = .3f, RestrikePeak = .55f;
    public const float PulseRise = .10f, PulseFall = .5f;    // glow envelope seconds (smoothstepped)
    public const float BoltRise = .06f, BoltHold = .14f, BoltFall = .27f;
    public const float GlowAlpha = .22f, BloomAlpha = .10f;  // the most any glow / ceiling wash may reach
    public const float GlowMin = 3.4f, GlowMax = 5.2f, BloomSize = 7f;
    public const float BoltMin = 2.2f, BoltMax = 3.6f, StrikeSize = 2.6f, ArcSize = 5.4f;
    public const float SheetChance = .42f, ForkChance = .38f;   // the rest: strikes (when a mast is in view)
    public const float StrikeFirst = 12f;                    // the first mast strike no earlier than this
}

// The weather's schedule: when the next lightning event happens, what kind, its pulses.
// Pure logic (no Unity objects, no allocation): the director draws it, the tests audit it.
public sealed class StormLightning
{
    public enum Kind { Sheet, Fork, Strike }

    public struct Event
    {
        public Kind kind;
        public int pulses;           // 1 or 2
        public float restrikeIn;     // seconds after the first pulse
    }

    readonly System.Random rng;
    float clock, nextEvent = StormTuning.FirstEvent;
    // pulses still to start: (time, peak)
    float pendingAt = -1f, pendingPeak;
    float lastPulse = -100f;
    public int Events { get; private set; }
    public int Pulses { get; private set; }
    public float LastPulseAt { get { return lastPulse; } }

    // The pulse log, a ring of the most recent starts (tests read it).
    public const int LogSize = 256;
    public readonly float[] PulseLog = new float[LogSize];
    public readonly float[] PeakLog = new float[LogSize];

    public StormLightning(int seed) { rng = new System.Random(seed); }

    public float Clock { get { return clock; } }

    // Advances by dt. Returns true when a pulse starts now; `peak` (0..1) scales its light and
    // `fresh` is true for the first pulse of an event (the one that draws a bolt), `kind` is its event's.
    // `strikeOk`: a lightning-rod mast is in view (a strike may be drawn).
    public bool Tick(float dt, bool strikeOk, out Kind kind, out float peak, out bool fresh)
    {
        clock += dt;
        kind = Kind.Sheet; peak = 0f; fresh = false;
        if (pendingAt >= 0f && clock >= pendingAt && clock - lastPulse >= StormTuning.MinPulseGap)
        {
            pendingAt = -1f;
            kind = pendingKind;
            peak = pendingPeak;
            Fire(peak);
            return true;
        }
        if (clock < nextEvent) return false;
        if (clock - lastPulse < StormTuning.MinPulseGap) return false;
        // a new event
        double r = rng.NextDouble();
        if (r < StormTuning.SheetChance) kind = Kind.Sheet;
        else if (r < StormTuning.SheetChance + StormTuning.ForkChance || !strikeOk || clock < StormTuning.StrikeFirst) kind = Kind.Fork;
        else kind = Kind.Strike;
        peak = .75f + (float)rng.NextDouble() * .25f;
        fresh = true;
        Events++;
        nextEvent = clock + StormTuning.EventGapMin + (float)rng.NextDouble() * (StormTuning.EventGapMax - StormTuning.EventGapMin);
        if (rng.NextDouble() < StormTuning.RestrikeChance && kind != Kind.Strike)
        {
            pendingAt = clock + StormTuning.RestrikeGapMin + (float)rng.NextDouble() * (StormTuning.RestrikeGapMax - StormTuning.RestrikeGapMin);
            pendingPeak = StormTuning.RestrikePeak;
            pendingKind = Kind.Sheet;       // the re-strike is only a glow
            nextEvent = Mathf.Max(nextEvent, pendingAt + StormTuning.EventGapMin);
        }
        Fire(peak);
        return true;
    }

    Kind pendingKind;

    void Fire(float peak)
    {
        lastPulse = clock;
        PulseLog[Pulses % LogSize] = clock;
        PeakLog[Pulses % LogSize] = peak;
        Pulses++;
    }

    // The soft glow envelope of a pulse `age` seconds old: smoothstep up over PulseRise, down over PulseFall.
    public static float Envelope(float age)
    {
        if (age <= 0f) return 0f;
        if (age < StormTuning.PulseRise) return Smooth(age / StormTuning.PulseRise);
        float k = (age - StormTuning.PulseRise) / StormTuning.PulseFall;
        return k >= 1f ? 0f : 1f - Smooth(k);
    }

    // A bolt's own envelope: quick rise, a short hold, a fade; total BoltLife seconds. No re-flicker.
    public static float BoltEnvelope(float age)
    {
        if (age <= 0f) return 0f;
        if (age < StormTuning.BoltRise) return age / StormTuning.BoltRise;
        if (age < StormTuning.BoltRise + StormTuning.BoltHold) return 1f;
        float k = (age - StormTuning.BoltRise - StormTuning.BoltHold) / StormTuning.BoltFall;
        return k >= 1f ? 0f : 1f - Smooth(k);
    }

    public const float BoltLife = .06f + .14f + .27f;
    public const float GlowLife = .10f + .5f;

    static float Smooth(float x) { x = Mathf.Clamp01(x); return x * x * (3f - 2f * x); }
}

public class StormDirector : PlanetDirector
{
    BackdropPool ground, sites, glow, bloom, bolts, debris, mist, wisps, ceiling, gusts, rain;
    readonly SpriteRenderer[] lamps = new SpriteRenderer[3];
    readonly SpriteRenderer[] mists = new SpriteRenderer[3];
    readonly float[] mistA = new float[3];
    float mistClock;
    BackdropAtlas landmarkArt, siteArt, dockArt, weather, lightning;
    Color[] dockTints;                                 // per site kind: its DockGrade colour on this ground set
    Sprite[][] siteLaunch;                             // per site kind: its 6 launch cells (null: an old two-drawing site, or the atlas is missing)
    GroundPlanner planner;
    Sprite[] siteClosed, siteOpen, banks, wispArt, mistArt, gustArt, pallArt, rainArt, boltArt, strikeArt, flashArt, arcArt, debrisArt;
    Sprite lampsOff, lampsOn;
    float groundTravel, groundGap, siteTravel, siteGap, ceilingTravel;
    int lastCluster = -1, lastLone = -1;
    bool night;
    float mistIn = 2.5f, wispIn = 6f, debrisIn = 5f;
    readonly Timer gustTimer = new Timer(StormTuning.GustEveryMin, StormTuning.GustEveryMax, StormTuning.GustFirst);
    int sheetsLeft;
    float sheetIn;
    readonly List<Material> mats = new List<Material>();
    StormLightning storm;
    StormLife life;
    public StormLife Life => life;

    public static int SeedOverride;
    public StormDirector() : base(SeedOverride > 0 ? SeedOverride : 2077) { }

    public float Clock => clock;
    public bool IsNight => night;
    public int Gusts { get; private set; }
    public int SitesSpawned { get; private set; }
    public int Clusters { get; private set; }
    public int Strikes { get; private set; }
    public int Forks { get; private set; }
    public int Flashes { get; private set; }
    public StormLightning Storm => storm;
    public GroundPlanner Planner => planner;
    public BackdropPool Ground => ground;
    public BackdropPool Sites => sites;
    // The six launch cells of a launch-strip site kind (StormTuning.SiteNames index), null for an old two-drawing site (tests, previews).
    public Sprite[] LaunchStrip(int kind) { return siteLaunch != null && kind >= 0 && kind < siteLaunch.Length ? siteLaunch[kind] : null; }
    public BackdropPool Ceiling => ceiling;
    public BackdropPool Glow => glow;
    public BackdropPool Bloom => bloom;
    public BackdropPool Bolts => bolts;
    public BackdropPool Debris => debris;
    public BackdropPool Rain => rain;
    public BackdropPool GustSheetPool => gusts;
    public Material CeilingMaterial { get; private set; }

    public override void Teardown()
    {
        foreach (var m in mats) BackdropAtlas.Kill(m);
        mats.Clear();
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
        siteArt = set.Atlas("sites");
        weather = set.Atlas("weather");
        lightning = set.Atlas("lightning");
        night = set.Spec.Night(set.Variant);
        planner = new GroundPlanner(set, "mid", GroundMask.Load("Storm", set.Variant));
        storm = new StormLightning(rng.Next(1, int.MaxValue));

        dockArt = set.Atlas(StormTuning.DocksAtlas);
        siteClosed = Sprites(siteArt, StormTuning.SiteClosed);
        dockTints = DockGrade.Tints("Storm", set.Variant, StormTuning.SiteClosed);
        siteOpen = Sprites(siteArt, StormTuning.SiteOpen);
        // the launch-strip sites take their idle / open cells (and all six) from the docks atlas; without it they are simply never spawned
        siteLaunch = new Sprite[StormTuning.SiteNames.Length][];
        for (int k = StormTuning.FirstLaunchSite; k < siteLaunch.Length; k++)
        {
            var f = dockArt.Frames(StormTuning.SiteNames[k]);
            if (f.Length < StormTuning.LaunchCells) { siteClosed[k] = siteOpen[k] = null; continue; }
            siteLaunch[k] = f;
            siteClosed[k] = f[StormTuning.LaunchIdle];
            siteOpen[k] = f[StormTuning.LaunchOpen];
        }
        lampsOff = siteArt.Get("lights_off");
        lampsOn = siteArt.Get("lights_on");
        banks = weather.Frames("cloud_bank");
        wispArt = weather.Frames("cloud_wisp");
        mistArt = weather.Frames("mist");
        gustArt = weather.Frames("gust");
        pallArt = weather.Frames("pall");
        rainArt = weather.Frames("rain");
        boltArt = lightning.Frames("bolt");
        strikeArt = lightning.Frames("strike");
        flashArt = lightning.Frames("flash");
        arcArt = lightning.Frames("arc");
        debrisArt = lightning.Frames("debris");

        // ground plane
        ground = LandmarkPool("ground", 12);
        sites = LandmarkPool("ground", 3);
        for (int i = 0; i < sites.items.Count; i++)
        {
            var go = new GameObject("lamps");
            go.transform.SetParent(sites.items[i].body, false);
            lamps[i] = go.AddComponent<SpriteRenderer>();
            lamps[i].sortingOrder = sites.items[i].sr.sortingOrder + 1;
            lamps[i].sprite = lampsOff;
            lamps[i].enabled = false;
            const float ppu = BackdropAtlas.PixelsPerUnit;
            float k = StormTuning.LampsScale;
            go.transform.localPosition = new Vector3((StormTuning.LampsAt.x - 128f) / ppu, (128f - StormTuning.LampsAt.y) / ppu, 0f);
            go.transform.localScale = new Vector3(k, k, 1f);
            // the launch vapour: above the site, below the elite emerging from it (LandingSites order = site + 2)
            var mg = new GameObject("mist");
            mg.transform.SetParent(sites.items[i].body, false);
            mists[i] = mg.AddComponent<SpriteRenderer>();
            mists[i].sortingOrder = sites.items[i].sr.sortingOrder + 1;
            mists[i].enabled = false;
        }
        life = new StormLife(ground, set.Atlas("rotors"), landmarkArt.Get(StormLifeTuning.TurbinePiece),
                             flashArt.Length > 0 ? flashArt[0] : null, 4242, landmarkArt, set.Atlas(StormLifeTuning.BoltAtlas));   // own seed: never draws from the director's rng (that would reshuffle the whole backdrop)
        glow = Pool("glow", 4);
        bolts = Pool("bolts", 4);
        mist = Pool("mist", 3);
        wisps = Pool("wisps", 4);
        ceiling = Pool("ceiling", 26);
        bloom = Pool("bloom", 2);
        gusts = Pool("gusts", StormTuning.GustSheets + 1);
        debris = Pool("debris", StormTuning.DebrisCount);
        rain = Pool("rain", StormTuning.RainSheets);

        CeilingMaterial = Grade(ceiling, "ceiling", StormTuning.CeilingThicken, 0f, 1f, 1f);

        groundGap = Rand(StormTuning.LoneGapMin, StormTuning.LoneGapMax);
        siteGap = StormTuning.SiteFirst;
        if (planner.Ready)
        {
            double t = planner.Travel;
            for (float y = -HalfH * .85f; y < HalfH * .8f; y += Rand(2.6f, 3.6f))
                SpawnCluster(t + y, t + y + 1.2f);
        }
        BuildCeiling();
        BuildRain();
        SpawnMist(Rand(-HalfH * .2f, HalfH * .4f));
    }

    static Sprite[] Sprites(BackdropAtlas atlas, string[] names)
    {
        var s = new Sprite[names.Length];
        for (int i = 0; i < names.Length; i++) s[i] = atlas.Get(names[i]);
        return s;
    }

    // 1 while the ceiling is thick, falling fast to 0 at CeilingClearSeconds (CloudCover.Ceiling).
    public static float CeilingDensity(float t) { return BackdropCatalog.For("Storm").CeilingDensity(t); }

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
            else groundGap = groundTravel + .25f;
        }
        ceilingTravel += set.Spec.Rate("ceiling") * v * dt;
        float density = set.Spec.CeilingDensity(clock);
        if (ceilingTravel >= StormTuning.CeilingGap && density > CloudCover.BankSpawnFloor)
        {
            ceilingTravel = 0f;
            SpawnBank(Rand(-HalfW * .8f, HalfW * .8f), float.NaN);
        }
        float air = set.Spec.CloudDensity();
        if (CloudCover.Tick(ref mistIn, dt, air, StormTuning.MistEveryMin, StormTuning.MistEveryMax, rng)) SpawnMist(float.NaN);
        if (CloudCover.Tick(ref wispIn, dt, air, StormTuning.WispEveryMin, StormTuning.WispEveryMax, rng)) SpawnWisp();
        if (gustTimer.Tick(dt, rng)) ForceGust();
        if (sheetsLeft > 0)
        {
            sheetIn -= dt;
            if (sheetIn <= 0f) { SpawnSheet(); sheetsLeft--; sheetIn = StormTuning.GustStagger; }
        }
        debrisIn -= dt;
        if (debrisIn <= 0f) { debrisIn = Rand(StormTuning.DebrisEveryMin, StormTuning.DebrisEveryMax); SpawnDebris(); }

        StepPinned(ground, dt);
        life.Step(dt);
        StepPinned(sites, dt);
        StepSites(dt);
        StepLightning(dt, v);

        foreach (var m in mist.items)
            if (m.active && Drift(m, dt, v)) Tinted(m, m.phase);
        foreach (var w in wisps.items)
            if (w.active && Drift(w, dt, v)) Tinted(w, w.phase);
        foreach (var b in ceiling.items)
        {
            if (!b.active) continue;
            if (density <= 0f) { Despawn(b); continue; }
            b.x += Mathf.Sign(b.x == 0f ? 1f : b.x) * CloudCover.PartSpeed * (1f - density) * dt;
            if (!Drift(b, dt, v)) continue;
            float y01 = Mathf.Clamp01((b.y / HalfH + 1f) * .5f);
            float low = Mathf.Lerp(StormTuning.CeilingLowShare, 1f, y01 * y01 * (3f - 2f * y01));
            Color c = StormTuning.CeilingTint;
            c.a = b.phase * density * low * set.Alpha;
            b.sr.color = c;
        }
        foreach (var g in gusts.items)
        {
            if (!g.active || !Drift(g, dt, v)) continue;
            float k = Mathf.Clamp01(g.age / Mathf.Max(.01f, g.life));
            if (k >= 1f) { Despawn(g); continue; }
            Tinted(g, StormTuning.GustMaxAlpha * Mathf.Sin(k * Mathf.PI));
        }
        foreach (var s in rain.items)
        {
            if (!s.active) continue;
            RecycleRain(s, dt, v);
            Tinted(s, StormTuning.RainAlpha);
        }
        foreach (var d in debris.items)
        {
            if (!d.active) continue;
            d.body.localRotation = Quaternion.Euler(0f, 0f, d.spin * d.age);
            if (Drift(d, dt, v)) Tinted(d, 1f);
        }
    }

    void Tinted(BackdropPiece p, float alpha)
    {
        p.sr.color = new Color(p.dockTint.r, p.dockTint.g, p.dockTint.b, alpha * set.Alpha);
    }

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

    // ------------------------------------------------------------- lightning --

    // The most recent lightning light, 0..1 of its caps: the brightest live glow (tests read it).
    public float FlashLevel { get; private set; }
    // The sum over live glows and blooms of alpha x area share, to bound how much of the view brightens.
    public int LiveBolts { get { return bolts.ActiveCount; } }

    void StepLightning(float dt, float v)
    {
        StormLightning.Kind kind;
        float peak; bool fresh;
        bool mast = FindMast() != null;
        if (storm.Tick(dt, mast, out kind, out peak, out fresh)) DrawPulse(kind, peak, fresh);

        // Overlapping pulses (a flash and its re-strike's tail, glows and the ceiling bloom) are scaled so the
        // COMBINED light never passes the cap: the sum of live glow alphas <= GlowAlpha, of blooms <= BloomAlpha.
        float glowSum = 0f, bloomSum = 0f;
        foreach (var g in glow.items)
        {
            if (!g.active) continue;
            g.age += dt;
            g.y -= g.rate * v * dt;
            Place(g);
            if (g.age >= StormLightning.GlowLife) { Despawn(g); continue; }
            g.life = StormLightning.Envelope(g.age) * g.phase * StormTuning.GlowAlpha;
            glowSum += g.life;
        }
        foreach (var g in bloom.items)
        {
            if (!g.active) continue;
            g.age += dt;
            g.y -= g.rate * v * dt;
            Place(g);
            if (g.age >= StormLightning.GlowLife) { Despawn(g); continue; }
            g.life = StormLightning.Envelope(g.age) * g.phase * StormTuning.BloomAlpha;
            bloomSum += g.life;
        }
        float glowK = glowSum > StormTuning.GlowAlpha ? StormTuning.GlowAlpha / glowSum : 1f;
        float bloomK = bloomSum > StormTuning.BloomAlpha ? StormTuning.BloomAlpha / bloomSum : 1f;
        float level = 0f;
        foreach (var g in glow.items)
        {
            if (!g.active) continue;
            float a = g.life * glowK;
            level = Mathf.Max(level, a / StormTuning.GlowAlpha);
            g.sr.color = new Color(1f, 1f, 1f, a * set.Alpha);
        }
        foreach (var g in bloom.items)
            if (g.active) g.sr.color = new Color(1f, 1f, 1f, g.life * bloomK * set.Alpha);
        foreach (var b in bolts.items)
        {
            if (!b.active) continue;
            b.age += dt;
            if (b.age >= StormLightning.BoltLife) { Despawn(b); continue; }
            if (b.parent != null && b.parent.active) { b.x = b.parent.x; b.y = b.parent.y + b.phase; }
            else b.y -= b.rate * v * dt;
            Place(b);
            b.sr.color = new Color(1f, 1f, 1f, StormLightning.BoltEnvelope(b.age) * set.Alpha);
        }
        FlashLevel = level;
    }

    // Any lightning-rod mast on screen where a strike would read.
    BackdropPiece FindMast()
    {
        foreach (var p in ground.items)
        {
            if (!p.active || p.kind != KindSpire) continue;
            if (p.y > HalfH * .75f || p.y < -HalfH * .55f) continue;
            return p;
        }
        return null;
    }

    public const int KindSpire = 7;

    void DrawPulse(StormLightning.Kind kind, float peak, bool fresh)
    {
        Flashes++;
        BackdropPiece mast = kind == StormLightning.Kind.Strike ? FindMast() : null;
        if (kind == StormLightning.Kind.Strike && mast == null) kind = StormLightning.Kind.Fork;
        float gx, gy;
        if (mast != null) { gx = mast.x; gy = mast.y + (128f - StormTuning.RodTipY) / 256f * mast.size; }
        else
        {
            // sheet flashes love a thunderhead tower: light it from inside when one is on screen
            BackdropPiece tower = kind == StormLightning.Kind.Sheet ? FindTower() : null;
            if (tower != null) { gx = tower.x + Rand(-.3f, .3f); gy = tower.y + tower.size * Rand(.1f, .3f); life.Strike(tower, Mathf.Clamp01(peak / .8f)); }   // its own bolts flash with the weather
            else { gx = Rand(-HalfW * .8f, HalfW * .8f); gy = Rand(-HalfH * .1f, HalfH * .85f); }
        }
        SpawnGlow(glow, gx, gy, Rand(StormTuning.GlowMin, StormTuning.GlowMax), peak, "glow");
        if (kind == StormLightning.Kind.Sheet && Chance(.6)) SpawnGlow(bloom, gx * .5f, gy * .6f, StormTuning.BloomSize, peak, "bloom");
        if (!fresh) return;
        if (kind == StormLightning.Kind.Strike)
        {
            Strikes++;
            if (strikeArt.Length > 0) SpawnBolt(Pick(strikeArt), StormTuning.StrikeSize, mast.x, 0f, mast);
        }
        else if (kind == StormLightning.Kind.Fork)
        {
            Forks++;
            if (boltArt.Length > 0) SpawnBolt(Pick(boltArt), Rand(StormTuning.BoltMin, StormTuning.BoltMax), gx, gy, null);
        }
        else if (arcArt.Length > 0 && Chance(.5)) SpawnBolt(Pick(arcArt), StormTuning.ArcSize, gx * .3f, gy, null);
    }

    BackdropPiece FindTower()
    {
        foreach (var p in ground.items)
        {
            if (!p.active || p.kind != KindTower) continue;
            if (p.y > HalfH * .8f || p.y < -HalfH * .5f) continue;
            return p;
        }
        return null;
    }

    public const int KindTower = 6;

    void SpawnGlow(BackdropPool pool, float x, float y, float size, float peak, string layer)
    {
        if (flashArt.Length == 0) return;
        var g = pool.Spawn();
        if (g == null) return;
        SetSprite(g, Pick(flashArt), size);
        g.x = x; g.y = y;
        g.rate = set.Spec.Rate(layer);
        g.phase = peak;
        g.age = 0f;
        Place(g);
        g.sr.color = new Color(1f, 1f, 1f, 0f);
    }

    // A bolt: `mast` pins a strike's lower end on the rod tip (phase = its offset above the mast's centre).
    void SpawnBolt(Sprite s, float size, float x, float y, BackdropPiece mast)
    {
        var b = bolts.Spawn();
        if (b == null) return;
        SetSprite(b, s, size);
        b.body.localScale = mast == null && Chance(.5) ? new Vector3(-1f, 1f, 1f) : Vector3.one;
        b.rate = set.Spec.Rate("bolts");
        b.age = 0f;
        if (mast != null)
        {
            b.parent = mast;
            float tip = (128f - StormTuning.RodTipY) / 256f * mast.size;
            float end = (StormTuning.StrikeEndY - 128f) / 256f * size;
            b.phase = tip + end;
            b.x = mast.x; b.y = mast.y + b.phase;
        }
        else { b.x = x; b.y = y; b.phase = 0f; }
        Place(b);
        b.sr.color = new Color(1f, 1f, 1f, 0f);
    }

    // --------------------------------------------------------------- ground --

    float SpawnGround()
    {
        double t = planner.Travel;
        double lo = t + HalfH + .95f, hi = lo + .9f;
        if (siteTravel >= siteGap && SpawnSite(float.NaN))
        {
            siteTravel = 0f;
            siteGap = Rand(StormTuning.SiteGapMin, StormTuning.SiteGapMax);
            return Rand(StormTuning.LoneGapMin, StormTuning.LoneGapMax);
        }
        int n = SpawnCluster(lo, hi);
        if (n == 0) return 0f;
        return n > 1 ? Rand(StormTuning.ClusterGapMin, StormTuning.ClusterGapMax)
                     : Rand(StormTuning.LoneGapMin, StormTuning.LoneGapMax);
    }

    float XMin(float size) { return -EdgeX + size * .3f; }
    float XMax(float size) { return EdgeX - size * .3f; }

    int PickCluster()
    {
        var w = StormTuning.ClusterWeights[Mathf.Clamp(set.Variant, 0, StormTuning.ClusterWeights.Length - 1)];
        int total = 0;
        for (int i = 0; i < w.Length; i++) if (i != lastCluster) total += w[i];
        int r = rng.Next(Mathf.Max(1, total));
        for (int i = 0; i < w.Length; i++)
        {
            if (i == lastCluster || w[i] <= 0) continue;
            r -= w[i];
            if (r < 0) return i;
        }
        return StormTuning.ClLone;
    }

    public int SpawnCluster(double lo, double hi, int kind = -1)
    {
        if (kind < 0) kind = PickCluster();
        int n = 0;
        switch (kind)
        {
            case StormTuning.ClFront: n = Front(lo, hi); break;
            case StormTuning.ClRefinery: n = Pair(Pick(StormTuning.Refineries), StormTuning.Neighbours, lo, hi); break;
            case StormTuning.ClMast: n = Pair(Pick(StormTuning.Spires), StormTuning.Dishes, lo, hi); break;
            case StormTuning.ClSkyhook: n = Pair(Pick(StormTuning.Skyhooks), StormTuning.Neighbours, lo, hi); break;
        }
        if (n == 0) n = LonePiece(lo, hi);
        if (n > 0) { lastCluster = kind; Clusters++; }
        return n;
    }

    BackdropPiece Put(BackdropPool pool, BackdropAtlas atlas, string name, float size, float x, double gy, bool mirror)
    {
        var sprite = atlas.Get(name);
        if (sprite == null) return null;
        var rule = StormTuning.Rule(name);
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
        p.kind = StormTuning.IsSpire(name) ? KindSpire : StormTuning.IsTower(name) ? KindTower : 0;
        Place(p);
        Tinted(p, 1f);
        return p;
    }

    bool Spot(string name, float size, double lo, double hi, out float x, out double gy)
    {
        return planner.Find(StormTuning.Rule(name), size, XMin(size), XMax(size), lo, hi, rng, out x, out gy);
    }

    // A storm front: one to three thunderhead towers side by side.
    int Front(double lo, double hi)
    {
        int n = 0, want = 1 + rng.Next(3);
        for (int k = 0; k < 6 && n < want; k++)
        {
            string name = Pick(StormTuning.Towers);
            float s = Rand(StormTuning.TowerMin, StormTuning.TowerMax);
            float x; double gy;
            if (!Spot(name, s, lo, hi + .6f, out x, out gy)) continue;
            if (Put(ground, landmarkArt, name, s, x, gy, Chance(.5)) != null) n++;
        }
        return n;
    }

    // A structure with a neighbour beside it.
    int Pair(string anchor, string[] neighbours, double lo, double hi)
    {
        float sa = Rand(StormTuning.LandmarkMin, StormTuning.LandmarkMax);
        float xa; double ga;
        if (!Spot(anchor, sa, lo, hi, out xa, out ga)) return 0;
        var a = Put(ground, landmarkArt, anchor, sa, xa, ga, Chance(.5));
        if (a == null) return 0;
        int n = 1;
        for (int pick = 0; pick < 3 && n == 1; pick++)
        {
            string nb = Pick(neighbours);
            if (nb == anchor) continue;
            float sb = Rand(StormTuning.LandmarkMin * .8f, StormTuning.LandmarkMax * .9f);
            float plateA = sa * .4f, plateB = sb * .4f;
            for (int attempt = 0; attempt < 2; attempt++)
            {
                float side = (xa > 0f) == (attempt == 0) ? -1f : 1f;
                float gap = Rand(StormTuning.NeighbourGapMin, StormTuning.NeighbourGapMax);
                float xb = xa + side * (plateA + gap + plateB);
                double gb = ga + Rand(-.12f, .12f);
                if (xb < XMin(sb) - .2f || xb > XMax(sb) + .2f) continue;
                var rule = StormTuning.Rule(nb);
                if (!planner.Accepts(rule, xb, gb, sb) || !planner.Free(xb, gb, GroundPlanner.HalfX(rule, sb), GroundPlanner.HalfY(rule, sb))) continue;
                if (Put(ground, landmarkArt, nb, sb, xb, gb, Chance(.5)) == null) break;
                n++;
                break;
            }
        }
        return n;
    }

    int LonePiece(double lo, double hi)
    {
        for (int k = 0; k < 3; k++)
        {
            int i = rng.Next(StormTuning.Lone.Length);
            if (i == lastLone) i = (i + 1) % StormTuning.Lone.Length;
            string name = StormTuning.Lone[i];
            float s = StormTuning.IsTower(name) ? Rand(StormTuning.TowerMin, StormTuning.TowerMax) : Rand(StormTuning.LandmarkMin, StormTuning.LandmarkMax);
            float x; double gy;
            if (!Spot(name, s, lo, hi, out x, out gy)) continue;
            if (Put(ground, landmarkArt, name, s, x, gy, Chance(.5)) == null) continue;
            lastLone = i;
            return 1;
        }
        return 0;
    }

    // Preview / tests: clears the ground plane and shows `name` alone at screen x, y.
    public BackdropPiece Showcase(string name, float size, float x, float y, bool mirror = false, bool keep = false)
    {
        if (!keep)
        foreach (var pool in new[] { ground, sites })
            foreach (var q in pool.items)
                if (q.active) { planner.Release(q.footprint); q.footprint = -1; Despawn(q); }
        bool site = siteArt.Has(name);
        var p = Put(site ? sites : ground, site ? siteArt : landmarkArt, name, size, x, planner.GroundY(y), mirror);
        groundTravel = -1000f;
        siteTravel = -1000f;
        return p;
    }

    // ----------------------------------------------------------- launch sites --

    public bool SpawnSite(float y, int kind = -1)
    {
        if (!planner.Ready) return false;
        if (DockGrade.AnyOnGround(sites, HalfH)) return false;      // one dock at a time: the next comes once this one has scrolled off
        if (DockGrade.Strict && kind >= 0 && StormTuning.SiteWeights[kind] <= 0) return false;   // (the old floating machines: no ground to stand on)
        if (kind < 0)
        {
            int total = 0;
            foreach (int w in StormTuning.SiteWeights) total += w;
            int r = rng.Next(total);
            for (kind = 0; kind < StormTuning.SiteWeights.Length - 1; kind++)
            {
                r -= StormTuning.SiteWeights[kind];
                if (r < 0) break;
            }
        }
        var closed = siteClosed[kind];
        if (closed == null || siteOpen[kind] == null) return false;
        float size = Rand(StormTuning.SiteMin, StormTuning.SiteMax) * StormTuning.SiteWidthK[kind];
        double lo = float.IsNaN(y) ? planner.Travel + HalfH + .95f : planner.GroundY(y) - .3f;
        double hi = lo + (float.IsNaN(y) ? .9f : .6f);
        float x; double gy;
        if (!planner.Find(StormTuning.Rule(StormTuning.SiteClosed[kind]), size, XMin(size), XMax(size), lo, hi, rng, out x, out gy) &&
            (DockGrade.Strict || !planner.Find(new PieceRule(GroundClass.Any, 0f), size, XMin(size), XMax(size), lo, hi, rng, out x, out gy))) return false;   // (no 'anywhere' fallback)
        var p = Put(sites, siteLaunch[kind] != null ? dockArt : siteArt, StormTuning.SiteClosed[kind], size, x, gy, false);   // (doors face the viewer: never mirrored)
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
            if (ph > 0f && ph < 2f) return StormTuning.LaunchCellAt(ph);
            return p.tier == 1 ? StormTuning.LaunchOpening : StormTuning.LaunchHold;
        }
        if (p.tier == 3) return p.life > StormTuning.LaunchCloseSeconds ? StormTuning.LaunchHold : StormTuning.LaunchClosing;
        return StormTuning.LaunchIdle;
    }

    // Is the launch vapour of site slot `i` showing (tests)?
    public bool MistShowing(int i) { return i >= 0 && i < mists.Length && mists[i] != null && mists[i].enabled; }
    public SpriteRenderer MistRenderer(int i) { return i >= 0 && i < mists.Length ? mists[i] : null; }
    public float MistFade(int i) { return i >= 0 && i < mistA.Length ? mistA[i] : 0f; }

    // The vapour of strip site `p` (slot i) in strip cell `cell`: the weather wisps played as a flipbook over the pad point, as wide as the bay.
    void StepMist(int i, BackdropPiece p, int cell, float dt)
    {
        var sr = mists[i];
        if (sr == null) return;
        bool open = cell >= StormTuning.LaunchOpening && cell < StormTuning.LaunchClosing;
        float target = open ? 1f : 0f;
        mistA[i] = Mathf.MoveTowards(mistA[i], target, dt / (open ? StormTuning.MistFadeIn : StormTuning.LaunchCloseSeconds));
        if (wispArt == null || wispArt.Length == 0 || mistA[i] <= .001f) { sr.enabled = false; return; }
        var f = wispArt[((int)(mistClock * StormTuning.MistFps) + i) % wispArt.Length];
        if (f == null) { sr.enabled = false; return; }
        sr.sprite = f;
        if (wisps != null && wisps.items.Count > 0) sr.sharedMaterial = wisps.items[0].sr.sharedMaterial;   // (the vapour wears the wisps' own grade)
        Bounds b = p.sr.sprite.bounds;
        Vector3 pad = p.root.TransformPoint(new Vector3(b.center.x + StormTuning.SitePads[p.kind].x * b.size.x, b.center.y + StormTuning.SitePads[p.kind].y * b.size.y, 0f));
        float siteW = b.size.x * p.root.lossyScale.x;
        float world = siteW * StormTuning.MistWidthShare / Mathf.Max(.01f, f.bounds.size.x);   // world scale of the wisp sprite
        var t = sr.transform;
        float k = Mathf.Max(.0001f, t.parent != null ? t.parent.lossyScale.x : 1f);
        // the vapour rises out of the bay: its middle a share of its own height above the pad point
        t.position = new Vector3(pad.x, pad.y + f.bounds.size.y * world * StormTuning.MistRise, t.position.z);
        t.localScale = new Vector3(world / k, world / k, 1f);
        sr.color = new Color(1f, 1f, 1f, StormTuning.MistAlpha * mistA[i] * set.Alpha);
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
            if (launch > 0) { p.tier = launch; p.life = StormTuning.SiteCloseDelay; }
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
            if (strip != null) { if (lamp != null) lamp.enabled = false; StepMist(i, p, LaunchCell(p), dt); continue; }
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
            if (p.size < StormTuning.SiteMinSize) continue;
            if (p.y < -HalfH * .15f || p.y > HalfH - p.size * .3f) continue;
            if (Mathf.Abs(p.x) > HalfW - .25f) continue;
            Bounds b = p.sr.sprite.bounds;
            into.Add(new LandingSite
            {
                anchor = p.root,
                local = new Vector3(b.center.x + StormTuning.SitePads[p.kind].x * b.size.x, b.center.y + StormTuning.SitePads[p.kind].y * b.size.y, 0f),   // the cell centre (the strip sites: their pad pixel)
                scale = Mathf.Clamp(p.size * StormTuning.EmergeScalePerUnit, StormTuning.EmergeScaleMin, StormTuning.EmergeScaleMax),
                order = p.sr.sortingOrder + 2,
                id = StormTuning.SiteIdBase + i,
                kind = StormTuning.SiteKinds[p.kind],
                emerge = true,
            });
        }
    }

    // The elite director wants a site of one of `launchFrom`'s kinds in view soon (LandingSites.BringHome), as Frost's, Ember's and Tide's: the ground
    // is fixed, so none is conjured -- but the site spawner is asked for one of that kind at the top of the view now (it scrolls in over the
    // next seconds, as the ground pieces always do), unless one of them is already in view or on its way. False when none of the kinds is
    // one of Storm's sites or there is no room for one on the ground.
    public override bool BringHome(string launchFrom)
    {
        if (sites == null || !planner.Ready || string.IsNullOrEmpty(launchFrom)) return false;
        var want = new List<int>(StormTuning.SiteKinds.Length);   // (a rare call: one per elite)
        bool mine = false;      // (one of the kinds it names is a dock of this world, even if none can be placed here)
        foreach (string part in launchFrom.Split(','))
        {
            var k = LandingSite.KindOf(part.Trim());
            if (k == null) continue;
            for (int t = 0; t < StormTuning.SiteKinds.Length; t++)
            {
                if (StormTuning.SiteKinds[t] == k.Value) mine = true;
                if (StormTuning.SiteKinds[t] == k.Value && siteClosed[t] != null && siteOpen[t] != null && StormTuning.SiteWeights[t] > 0 && !want.Contains(t)) want.Add(t);
            }
        }
        if (want.Count == 0 && mine && DockGrade.Strict)     // (none of the kinds it names has a dock here: any dock will do)
            for (int t = 0; t < StormTuning.SiteKinds.Length; t++)
                if (siteClosed[t] != null && siteOpen[t] != null && StormTuning.SiteWeights[t] > 0) want.Add(t);
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
        SetSprite(b, Pick(banks), Rand(StormTuning.CeilingMin, StormTuning.CeilingMax));
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
        SetSprite(w, Pick(wispArt), Rand(StormTuning.WispMin, StormTuning.WispMax));
        if (Chance(.5)) w.body.localScale = new Vector3(-1f, 1f, 1f);
        w.x = Rand(-HalfW * .7f, HalfW * .7f);
        w.y = SpawnY(w.size);
        w.vx = StormTuning.Wind * StormTuning.WispDrift * Rand(.7f, 1.3f);
        w.rate = set.Spec.Rate("wisps");
        w.phase = Rand(StormTuning.WispAlphaMin, StormTuning.WispAlphaMax) * CloudCover.Alpha(set.Spec.CloudDensity());
        Place(w);
        Tinted(w, 0f);
    }

    void SpawnMist(float y)
    {
        if (mistArt.Length == 0) return;
        var m = mist.Spawn();
        if (m == null) return;
        SetSprite(m, Pick(mistArt), HalfW * 2f * Rand(StormTuning.MistWidthMin, StormTuning.MistWidthMax));
        if (Chance(.5)) m.body.localScale = new Vector3(-1f, 1f, 1f);
        m.x = Rand(-HalfW * .3f, HalfW * .3f);
        m.y = float.IsNaN(y) ? SpawnY(m.size * .5f) : y;
        m.vx = StormTuning.Wind * StormTuning.MistDrift * Rand(.6f, 1.4f);
        m.rate = set.Spec.Rate("mist");
        m.phase = Rand(StormTuning.MistAlphaMin, StormTuning.MistAlphaMax) * CloudCover.Alpha(set.Spec.CloudDensity());
        Place(m);
        Tinted(m, 0f);
    }

    public void ForceGust() { sheetsLeft = StormTuning.GustSheets; sheetIn = 0f; Gusts++; }

    void SpawnSheet()
    {
        if (gustArt.Length == 0) return;
        var g = gusts.Spawn();
        if (g == null) return;
        SetSprite(g, Pick(gustArt), Rand(StormTuning.GustMin, StormTuning.GustMax));
        float dir = StormTuning.Wind >= 0f ? 1f : -1f;
        g.x = -dir * (HalfW + g.size * .25f);
        g.y = Rand(HalfH * .1f, HalfH * .9f);
        float speed = StormTuning.GustSpeed * Rand(.85f, 1.15f);
        g.vx = dir * speed;
        g.vy = -speed * .35f;
        g.life = (2f * HalfW + g.size * .6f) / speed;
        g.rate = set.Spec.Rate("gusts");
        Place(g);
        Tinted(g, 0f);
    }

    // Floating debris: a torn panel, a drum, a bent mast tumbling across, now and then.
    void SpawnDebris()
    {
        if (debrisArt.Length == 0) return;
        var d = debris.Spawn();
        if (d == null) return;
        SetSprite(d, Pick(debrisArt), Rand(StormTuning.DebrisMin, StormTuning.DebrisMax));
        float dir = Chance(.5) ? 1f : -1f;
        d.x = -dir * Rand(HalfW * .3f, HalfW * .9f);
        d.y = SpawnY(d.size);
        d.vx = dir * Rand(.15f, .5f);
        d.vy = -Rand(.1f, .4f);
        d.spin = Rand(40f, 120f) * (Chance(.5) ? 1f : -1f);
        d.rate = set.Spec.Rate("debris");
        Place(d);
        Tinted(d, 0f);
    }

    void BuildRain()
    {
        foreach (var s in rain.items)
        {
            if (rainArt.Length == 0) break;
            s.Show(true);
            SetSprite(s, Pick(rainArt), Rand(StormTuning.RainMin, StormTuning.RainMax));
            s.x = Rand(-HalfW, HalfW);
            s.y = Rand(-HalfH, HalfH);
            s.phase = Rand(0f, 6.28f);
            s.rate = set.Spec.Rate("rain");
            Place(s);
            Tinted(s, StormTuning.RainAlpha);
        }
    }

    // A rain sheet falls with the storm (its streaks lean with the wind) and re-enters at the top.
    void RecycleRain(BackdropPiece s, float dt, float v)
    {
        float fall = StormTuning.RainFall;
        s.age += dt;
        s.y += (-fall - s.rate * v) * dt;
        s.x += StormTuning.Wind * StormTuning.RainLean * fall * dt;
        float lim = HalfH + s.size * .5f;
        if (s.y < -lim) { s.y += 2f * lim; s.x = Rand(-HalfW, HalfW); }
        float xl = HalfW + s.size * .5f;
        if (s.x > xl) s.x -= 2f * xl;
        else if (s.x < -xl) s.x += 2f * xl;
        Place(s);
    }
}
