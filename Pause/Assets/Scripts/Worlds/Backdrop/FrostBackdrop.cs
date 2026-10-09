using System.Collections.Generic;
using UnityEngine;

// Frost's backdrop: the ship flies at atmosphere level just under the cloud
// ceiling, looking down on a frozen ocean, its coast and ice-bound industry.
// Nothing space-like: no stars, planets, stations or rocks.
//
//   GROUND     one of four tile sets per landing (FrostBackdropSelection):
//              sky / far / mid tiles and the drifting-floe flow strip.
//   LANDMARKS  rigs, platforms, refineries, relays, causeways, icebreakers,
//              cliffs, derricks, convoys (landmarks atlas) on one far ground
//              plane, spaced by scrolled distance so they never overlap,
//              never the same family twice running, mixed per variant
//              (FrostTuning.FamilyWeights). Their ambient loops -- smoke,
//              flares, beacons, searchlights, windows, steam, geysers -- are
//              FrostAmbientCatalog's emitters.
//   SITES      the elites' launch sites on the same ground plane (sites
//              atlas): ice-shelf hangar, rig lift bay, relay pad ring,
//              crawler garage, silo hatch. Drawn shut; the launch tell opens
//              them (open drawing, lamp row blinking) while the elite
//              emerges; they close again behind it.
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
    // "A bit brighter / darker" = change this one number (1.5 .. 2.0).
    public static float Brightness = 1.7f;
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
    public static float GapMin = .5f, GapMax = 1.6f;                   // scrolled ground distance between spawns
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
    public const int FamIcebreaker = 5, FamConvoy = 8;
    public static readonly int[][] FamilyWeights =
    {
        new[] { 3, 3, 1, 1, 1, 3, 1, 1, 0 },   // (none) = frozen ocean
        new[] { 3, 3, 1, 1, 1, 3, 1, 1, 0 },   // v1 frozen ocean: rigs, platforms, icebreakers
        new[] { 1, 1, 2, 1, 2, 3, 3, 0, 1 },   // v2 coast + harbour
        new[] { 0, 1, 3, 3, 2, 0, 1, 3, 3 },   // v3 inland tundra industry
        new[] { 0, 1, 1, 3, 1, 1, 3, 2, 2 },   // v4 glacier / crevasse night
    };
    // launch sites: drawings (closed, open) and how often each comes
    public static readonly string[] SiteNames = { "hangar", "rigbay", "padring", "crawlerbay", "hatch" };
    public static readonly string[] SiteClosed = { "hangar_closed", "rigbay_closed", "padring_idle", "crawlerbay_closed", "hatch_closed" };
    public static readonly string[] SiteOpen = { "hangar_open", "rigbay_open", "padring_active", "crawlerbay_open", "hatch_open" };
    public static readonly LandingKind[] SiteKinds = { LandingKind.Hangar, LandingKind.RigBay, LandingKind.PadRing, LandingKind.CrawlerBay, LandingKind.Hatch };
    public static readonly int[] SiteWeights = { 3, 2, 2, 3, 2 };
    // where the elite comes out: fractions of the drawing's bounds from its centre (y up)
    public static readonly Vector2[] SitePads =
    {
        new Vector2(0f, -.05f), new Vector2(0f, -.03f), new Vector2(0f, 0f), new Vector2(0f, -.04f), new Vector2(0f, -.02f),
    };
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
    BackdropAtlas landmarkArt, siteArt, weather;
    AmbientEmitters ambient;
    Sprite[] auroraFrames;
    Sprite[] siteClosed, siteOpen, banks, wispArt, mistArt, gustArt, snowArt;
    Sprite lampsOff, lampsOn;
    Color tintGround, tintAurora, tintMist, tintWisps, tintCeiling, tintBlizzard;
    readonly Color[] tintSnow = new Color[3];
    float auroraAlpha;

    float groundTravel, groundGap, siteTravel, siteGap, ceilingTravel;
    int lastFamily = -1, lastLane;
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

    public FrostDirector() : base(1989) { }

    public float Clock => clock;
    public int Gusts { get; private set; }
    public int SitesSpawned { get; private set; }
    public AmbientEmitters Ambient => ambient;
    public BackdropPool Ground => ground;
    public BackdropPool Sites => sites;
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
        weather = set.Atlas("weather");
        ambient = new AmbientEmitters(set);
        var spec = set.Spec;
        tintGround = spec.Tint("landmarks");
        tintAurora = spec.Tint("aurora");
        tintMist = spec.Tint("mist");
        tintWisps = spec.Tint("wisps");
        tintCeiling = spec.Tint("ceiling");
        tintBlizzard = spec.Tint("blizzard");

        siteClosed = Sprites(siteArt, FrostTuning.SiteClosed);
        siteOpen = Sprites(siteArt, FrostTuning.SiteOpen);
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
        groundGap = Rand(FrostTuning.GapMin, FrostTuning.GapMax);
        siteGap = FrostTuning.SiteFirst;
        SpawnLandmark(HalfH * .62f);
        SpawnLandmark(-HalfH * .05f);
        SpawnLandmark(-HalfH * .7f);
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
        if (groundTravel >= groundGap && SpawnGround())
        {
            groundTravel = 0f;
            groundGap = Rand(FrostTuning.GapMin, FrostTuning.GapMax);
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
        StepGround(ground, dt, v);
        StepGround(sites, dt, v);
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

    void StepGround(BackdropPool pool, float dt, float v)
    {
        foreach (var p in pool.items)
        {
            if (!p.active || !Drift(p, dt, v)) continue;
            if (p.vx != 0f || p.vy != 0f) HoldStation(p);
            Tinted(p, tintGround, 1f);
        }
    }

    // A ship under way (icebreaker, convoy) stops before it sails into the
    // piece ahead of it: the ground plane never stacks.
    void HoldStation(BackdropPiece p)
    {
        for (int k = 0; k < 2; k++)
            foreach (var q in (k == 0 ? ground : sites).items)
            {
                if (!q.active || q == p) continue;
                float r = (p.size + q.size) * .5f;
                if (Mathf.Abs(p.x - q.x) < r * .8f && Mathf.Abs(p.y - q.y) < r * .8f + .15f &&
                    (p.vy * (q.y - p.y) > 0f || p.vx * (q.x - p.x) > 0f))
                {
                    p.vx = 0f;
                    p.vy = 0f;
                    return;
                }
            }
    }

    // ------------------------------------------------------------ ground --

    // A landmark (or, when one is due, a launch site) entering at the top.
    bool SpawnGround()
    {
        if (siteTravel >= siteGap && SpawnSite(float.NaN))
        {
            siteTravel = 0f;
            siteGap = Rand(FrostTuning.SiteGapMin, FrostTuning.SiteGapMax);
            return true;
        }
        return SpawnLandmark(float.NaN);
    }

    float LaneX(float size)
    {
        // left, middle, right of the ground under the board, never twice running
        int lane = rng.Next(2);
        lane = lane >= lastLane ? lane + 1 : lane;
        lastLane = lane;
        float edge = EdgeX - size * .35f;
        float x = lane == 0 ? Rand(-edge, -edge * .45f) : lane == 1 ? Rand(-edge * .25f, edge * .25f) : Rand(edge * .45f, edge);
        return x;
    }

    int PickFamily()
    {
        var weights = FrostTuning.FamilyWeights[Mathf.Clamp(set.Variant, 0, FrostTuning.FamilyWeights.Length - 1)];
        int total = 0;
        for (int i = 0; i < weights.Length; i++) if (i != lastFamily) total += weights[i];
        int r = rng.Next(Mathf.Max(1, total));
        for (int i = 0; i < weights.Length; i++)
        {
            if (i == lastFamily || weights[i] <= 0) continue;
            r -= weights[i];
            if (r < 0) return i;
        }
        return 0;
    }

    bool SpawnLandmark(float y)
    {
        int fam = PickFamily();
        string name = Pick(FrostTuning.Families[fam]);
        var sprite = landmarkArt.Get(name);
        float size = Rand(FrostTuning.LandmarkMin, FrostTuning.LandmarkMax);
        var p = SpawnLandmark(ground, sprite, size, "landmarks", y);
        if (p == null) return false;
        lastFamily = fam;
        p.x = LaneX(p.size);
        p.color = tintGround;
        p.kind = fam;
        // the ships under way move a little on their own
        if (fam == FrostTuning.FamIcebreaker) p.vy = Rand(.04f, .08f);
        else if (fam == FrostTuning.FamConvoy) { float dir = p.body.localScale.x; p.vx = .05f * dir; p.vy = .05f; }
        Place(p);
        ambient.Attach(p, name, rng);
        return true;
    }

    // A launch site entering at the top (or at `y`). False when the pool is
    // full, the art is missing or the top of the ground is busy.
    public bool SpawnSite(float y, int kind = -1)
    {
        if (kind < 0)
        {
            int total = 0;
            foreach (int w in FrostTuning.SiteWeights) total += w;
            int r = rng.Next(total);
            for (kind = 0; kind < FrostTuning.SiteWeights.Length - 1; kind++)
            {
                r -= FrostTuning.SiteWeights[kind];
                if (r < 0) break;
            }
        }
        var closed = siteClosed[kind];
        if (closed == null || siteOpen[kind] == null) return false;
        float size = Rand(FrostTuning.SiteMin, FrostTuning.SiteMax);
        var p = SpawnLandmark(sites, closed, size, "landmarks", y);
        if (p == null) return false;
        p.body.localScale = Vector3.one;     // doors face the viewer: never mirrored
        p.x = LaneX(p.size);
        p.kind = kind;
        p.tier = 0;
        p.life = 0f;
        p.color = tintGround;
        Place(p);
        ambient.Attach(p, SiteKey(kind), rng);
        int i = sites.items.IndexOf(p);
        if (i >= 0 && lamps[i] != null) { lamps[i].sprite = lampsOff; lamps[i].enabled = lampsOff != null; }
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
            var want = open ? siteOpen[p.kind] : siteClosed[p.kind];
            if (p.sr.sprite != want) p.sr.sprite = want;
            var lamp = lamps[i];
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
