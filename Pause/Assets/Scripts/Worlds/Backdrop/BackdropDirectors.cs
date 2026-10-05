using System.Collections.Generic;
using UnityEngine;

// Per-world set pieces. Colours here are multiplied into already-dim art and
// kept well below gameplay brightness; see WorldBackdropTest's contrast guard.

// Space has no ground to give it depth, so the set pieces carry it: every
// body (planet, station, planetoid, moon) lives in one of four depth tiers,
// and the tier decides how big it is, how lit it is and what it sorts behind.
//
// How fast a body parallaxes depends on how far away it is, which is NOT how
// big it is drawn: a planet is enormous, so one that looks large is still
// immensely farther off than a station or rock drawn the same size. Bodies
// therefore come in two depth classes, each with its own run of catalog
// layers: planets (with their moons / orbiting stations) on the planet_*
// layers, which barely move and sort behind everything else, and lone
// structures (stations, planetoids) on the much faster deep..near layers in
// front of them. Within a class a nearer tier is bigger and a little faster.
// (Planets used to share the structures' layers, so the biggest planet was
// also the fastest thing in the sky and left the view in a few seconds.)
//
// Most bodies are far away; a near planet is a rare event. Bodies are
// composed, not sprinkled: each class has its own spawn queue, alternating
// sides, with clear sky between arrivals and no body ever overtaking another
// of its class in its lane. A structure may drift across in front of a
// planet -- that relative motion is what makes the planet read as distant.
//
// Atlas cells are variants (twelve different giants, eight stations...), not
// flipbook frames: a body picks one when it spawns and keeps it.
public class SpaceDirector : BackdropDirector
{
    public struct Tier
    {
        public string layer;        // BackdropCatalog layer of a lone station / planetoid: parallax rate and sorting order
        public string planetLayer;  // ... of a planet and its companions: far slower, behind every structure
        public float scale;         // size multiplier on KindSize
        public float light;         // brightness
        public float clarity;       // 1 = the art's own colour, lower = hazed toward the sky
        public int weight;          // how often a planet lands here
    }

    // Far -> near. Scales are spaced wider than SizeJitter, so within a kind
    // a farther body is always smaller than a nearer one.
    public static readonly Tier[] Tiers =
    {
        new Tier { layer = "deep", planetLayer = "planet_deep", scale = 0.60f, light = 0.62f, clarity = 0.60f, weight = 42 },
        new Tier { layer = "far",  planetLayer = "planet_far",  scale = 1.00f, light = 0.74f, clarity = 0.75f, weight = 31 },
        new Tier { layer = "mid",  planetLayer = "planet_mid",  scale = 1.60f, light = 0.86f, clarity = 0.90f, weight = 18 },
        // A near body is a composition anchor, not just a slightly larger
        // decoration. At this scale a planet fills roughly 60% of a phone's
        // width and crops behind one rail, like the Space art direction.
        // (Near in its own depth class: a near planet is still far slower
        // than any station.)
        new Tier { layer = "near", planetLayer = "planet_near", scale = 3.30f, light = 0.96f, clarity = 1.00f, weight = 9 },
    };

    // BackdropPiece.kind of a body.
    public const int Planet = 0, Station = 1, Planetoid = 2, Moon = 3;
    static readonly float[] KindSize = { 1.0f, 0.65f, 0.5f, 0.16f };   // width in units at scale 1
    const float SizeJitterLo = 0.9f, SizeJitterHi = 1.25f;
    // A rare near station reads as the derelict megastructure silhouette in
    // the reference composition; most remain deep/far through tier weights.
    const int StationMaxTier = 3, PlanetoidMaxTier = 2;

    const float MinSpacing = 3.5f;      // clear sky between a new structure and the one before it
    const float PlanetSpacing = 5f;     // ... between planets: they linger, so at most a couple share the view
    const float Clearance = 0.5f;       // gap kept between two bodies passing each other
    const float MiniBelow = 0.42f;      // narrower than this on screen: use the pre-shrunk sprite

    // The sky's indigo as a multiply tint: what distance fades a body toward.
    static readonly Color Haze = new Color(0.55f, 0.62f, 0.95f);
    static readonly Color[] PlanetTints =
    {
        // Light shifts on the art's own indigo. Blue stays at 1 so brightness
        // depends on the tier alone.
        new Color(1.00f, 1.00f, 1.00f),
        new Color(0.80f, 0.84f, 1.00f), // deeper indigo
        new Color(0.66f, 0.90f, 1.00f), // teal
        new Color(0.92f, 0.76f, 1.00f), // violet
    };
    static readonly Color StationTint = new Color(0.82f, 0.82f, 0.90f);
    static readonly Color RockTint = new Color(0.85f, 0.85f, 0.95f);

    // Spheres (giants, planetoids, rock moons) turn: the BackdropPlanet
    // shader slides the surface of the one static variant across the disc
    // under a fixed terminator and rim, at a pace that reads as a world,
    // not a decal. Stations hold or wheel as rigid sprites.
    public const string PlanetShader = "BackdropShaders/BackdropPlanet";
    public const float PlanetTurnSecondsMin = 40f, PlanetTurnSecondsMax = 60f;   // per half turn
    public const float RockTurnSecondsMin = 24f, RockTurnSecondsMax = 34f;
    const float DiscMarginPx = 2.5f;    // cuts are centred with this much clear border round the disc
    static readonly int IdMainTex = Shader.PropertyToID("_MainTex");
    static readonly int IdDisc = Shader.PropertyToID("_Disc");
    static readonly int IdSpin = Shader.PropertyToID("_Spin");
    Material planetMat, spriteMat;
    readonly MaterialPropertyBlock mpb = new MaterialPropertyBlock();

    // Comets cross far behind everything: small, dim and pulled toward the sky.
    public const float CometMinWidth = 0.7f, CometMaxWidth = 1.05f, CometMaxAlpha = 0.56f;
    static readonly Color CometTint = new Color(0.62f, 0.74f, 0.95f);

    static readonly Color[] WispTints =
    {
        new Color(0.55f, 0.35f, 0.75f, 0.24f), new Color(0.3f, 0.6f, 0.75f, 0.22f), new Color(0.45f, 0.45f, 0.85f, 0.24f),
    };
    static readonly Color[] ShooterTints =
    {
        new Color(0.85f, 0.75f, 1f, 0.58f),
        new Color(0.70f, 0.95f, 1f, 0.58f),
        new Color(1.00f, 0.63f, 0.22f, 0.68f), // rare warm punctuation against the blue field
    };

    BackdropPool wisps, galaxies, stars, comets, shooters, planets, stations, planetoids, moons, dust;
    Sprite[] giant, rocky, moonArt, station, ringStation, miniRocky, miniStation, miniRingStation, comet;
    readonly List<BackdropPool> bodies = new List<BackdropPool>();
    readonly List<BackdropPool> setPieces = new List<BackdropPool>();
    Timer galaxyTimer = new Timer(30f, 50f, 30f);
    Timer cometTimer = new Timer(16f, 26f, 7f);
    Timer shooterTimer = new Timer(5f, 11f, 2.5f);
    // Hero planets recur by count, not by the clock: planets cross slowly
    // (distance parallax), so a 30-second timer would make nearly every
    // planet a hero. One in HeroEvery keeps the far field the majority.
    public const int HeroEvery = 4;
    int planetsSinceHero;

    // The next body of each depth class waits here until the sky has room.
    struct Plan { public int kind, tier, companion; public float size, companionSize, reach; public bool ring; }
    Plan nextPlanet, nextStructure;
    float planetWait, structureWait;
    int planetSide = 1, structureSide = -1;

    public SpaceDirector() : base(1988) { }

    // Planets, stations, planetoids and moons: what the depth model governs.
    public IList<BackdropPool> Bodies { get { return bodies; } }
    // Everything that picks an atlas variant at spawn.
    public IList<BackdropPool> SetPieces { get { return setPieces; } }
    // A companion (moon, orbiting station) belongs to its planet's group.
    public static BackdropPiece Group(BackdropPiece p) { return p.parent ?? p; }
    // The planet depth class: a planet, or a moon / station in orbit round one.
    public static bool InPlanetClass(BackdropPiece p) { return Group(p).kind == Planet; }
    // The catalog layer (parallax rate, sorting order) of a body of that class in that tier.
    public static string LayerOf(int tier, bool planetClass)
    {
        return planetClass ? Tiers[tier].planetLayer : Tiers[tier].layer;
    }

    protected override void Build()
    {
        giant = anim.Frames("giant");
        rocky = anim.Frames("rocky");
        station = fx.Frames("station");
        ringStation = fx.Frames("ringstation");
        miniRocky = fx.Frames("mini_rocky");
        miniStation = fx.Frames("mini_station");
        miniRingStation = fx.Frames("mini_ringstation");
        comet = fx.Frames("comet");
        var m = new List<Sprite>(rocky);
        if (fx.Has("moon")) m.Add(fx.Get("moon"));
        moonArt = m.ToArray();

        wisps = Pool("wisps", 2);
        galaxies = Pool("galaxies", 1);
        stars = Pool("stars", 26);
        shooters = Pool("stars", 2, false, 1);
        comets = Pool("comets", 1);
        // Bodies take their tier's sorting order when they spawn.
        string deep = Tiers[0].layer, planetDeep = Tiers[0].planetLayer;
        planets = Pool(planetDeep, 3);
        stations = Pool(deep, 4);
        planetoids = Pool(deep, 2);
        moons = Pool(planetDeep, 3);
        dust = Pool("dust", 12);
        var sphere = Resources.Load<Shader>(PlanetShader);
        if (sphere != null) planetMat = new Material(sphere) { name = "SpacePlanet" };
        spriteMat = planets.items[0].sr.sharedMaterial;
        bodies.AddRange(new[] { planets, stations, planetoids, moons });
        setPieces.AddRange(bodies);
        setPieces.AddRange(new[] { wisps, galaxies, comets });

        // Stars sit at their own small spread of depths: the farther, the
        // smaller, dimmer and slower. Most are pinpoints, a few glint.
        var starColors = new[] { new Color(0.85f, 0.92f, 1f, 0.8f), new Color(0.55f, 0.95f, 1f, 0.8f),
                                 new Color(0.85f, 0.70f, 1f, 0.8f) };
        float starRate = set.Spec.Rate("stars");
        for (int i = 0; i < stars.items.Count; i++)
        {
            var s = stars.items[i];
            bool glint = i % 5 == 0;
            float k = Rand(0f, 1f);
            s.Show(true);
            SetSprite(s, fx.Get(glint ? "star" : "dot"),
                      glint ? Mathf.Lerp(0.09f, 0.15f, k) : Mathf.Lerp(0.03f, 0.055f, k));
            s.x = Rand(-HalfW, HalfW);
            s.y = Rand(-HalfH, HalfH);
            s.phase = Rand(0f, 6.283f);
            s.color = Pick(starColors);
            s.color.a *= Mathf.Lerp(0.55f, 1f, k);
            s.rate = starRate * Mathf.Lerp(0.6f, 1f, k);
            Place(s);
        }
        Scatter(dust, fx.Get("streak"), 0.3f, 0.5f, new[] { new Color(0.6f, 0.85f, 1f, 0.16f) },
                set.Spec.Rate("dust"));
        // The streak's head is at +x; dust falls, so point it down.
        foreach (var d in dust.items) d.body.localRotation = Quaternion.Euler(0, 0, -90f);

        // Open on a hero planet already in view. It is deliberately large,
        // off-centre and partially cropped, establishing the world's scale
        // before the normal body queues take over.
        Enter(PlanHero(), HalfH * 0.3f);
        nextPlanet = NextPlanet();
        planetWait = Rand(3f, 5f);
        nextStructure = PlanStructure();
        structureWait = Rand(2f, 4f);
        SpawnGalaxy(-HalfH * 0.45f);

        // Two nebula wisps are always present and simply recycle.
        for (int i = 0; i < 2; i++)
        {
            var w = wisps.Spawn();
            SetupWisp(w, i == 0 ? HalfH * 0.4f : -HalfH * 0.5f);
        }
    }

    void SetupWisp(BackdropPiece w, float y)
    {
        SetSprite(w, fx.Get(Chance(0.5) ? "wisp0" : "wisp1"), Rand(4.5f, 6f));
        w.age = 0f;                     // a new variant is a new life
        w.x = Rand(-1.4f, 1.4f);
        w.y = y;
        w.rate = set.Spec.Rate("wisps");
        w.spin = Rand(-1f, 1f);
        w.phase = Rand(0f, 360f);
        w.color = Pick(WispTints);
    }

    protected override void Step(float dt, float v)
    {
        foreach (var w in wisps.items)
        {
            if (!w.active) continue;
            w.age += dt;
            w.y -= w.rate * v * dt;
            if (w.y < -HalfH - w.size * 0.6f) SetupWisp(w, SpawnY(w.size));
            Place(w);
            w.body.localRotation = Quaternion.Euler(0, 0, w.phase + w.age * w.spin);
            float breathe = 1f + 0.06f * Mathf.Sin(w.age * 0.5f);
            w.body.localScale = new Vector3(breathe, 1f / breathe, 1f);
            Paint(w, 1f);
        }

        // Twinkle: hard on/off-ish pulses read as anime star glints.
        foreach (var s in stars.items)
        {
            Recycle(s, 0f, dt, v, 0f);
            float tw = Mathf.Sin(s.age * (1.5f + (s.phase % 1.7f)) + s.phase);
            Paint(s, tw > 0.75f ? 1f : 0.45f + 0.15f * tw);
        }

        // Dust becomes speed lines as the run gets faster.
        float speedK = Mathf.Clamp01((v - 6f) / 12f);
        foreach (var d in dust.items)
        {
            Recycle(d, 0f, dt, v, 0f);
            d.body.localScale = new Vector3(1f + 2.5f * speedK, 1f, 1f);
            Paint(d, 0.4f + speedK);
        }

        if (galaxyTimer.Tick(dt, rng)) SpawnGalaxy(float.NaN);
        if (cometTimer.Tick(dt, rng)) SpawnComet(v);
        if (shooterTimer.Tick(dt, rng)) SpawnShooter();

        // One queue per depth class. A plan that doesn't fit yet is held, not
        // re-rolled, so waiting for room never skews the mix toward small.
        // Planets are paced by the sky having room (PlanetSpacing), which at
        // their crawl is a long wait; structures mostly by the clock.
        planetWait -= dt;
        if (planetWait <= 0f)
        {
            if (Enter(nextPlanet, float.NaN)) { nextPlanet = NextPlanet(); planetWait = Rand(5f, 9f); }
            else planetWait = 0.5f;
        }
        structureWait -= dt;
        if (structureWait <= 0f)
        {
            if (Enter(nextStructure, float.NaN)) { nextStructure = PlanStructure(); structureWait = Rand(9f, 16f); }
            else structureWait = 0.5f;
        }

        foreach (var g in galaxies.items)
        {
            if (!g.active || !Drift(g, dt, v)) continue;
            g.body.localRotation = Quaternion.Euler(0, 0, g.age * g.spin);
            Paint(g, 1f);
        }
        foreach (var pool in bodies)
            foreach (var p in pool.items)
            {
                if (!p.active || p.parent != null) continue;
                if (!Drift(p, dt, v)) continue;
                // phase: tilt at spawn. Only ring stations turn (spin != 0).
                p.root.localRotation = Quaternion.Euler(0f, 0f, p.phase + p.age * p.spin);
                p.turn += p.turnRate * dt;
                Paint(p, 1f);
                PaintSphere(p);
                if (p.children != null) Orbit(p, p.children[0], dt);
            }
        foreach (var c in comets.items)
            if (c.active && Drift(c, dt, v))
            {
                OrientComet(c, v);
                Paint(c, 1f);
            }
        foreach (var s in shooters.items)
        {
            if (!s.active) continue;
            s.age += dt;
            s.x += s.vx * dt;
            s.y += s.vy * dt;
            Place(s);
            float k = s.age / s.life;
            if (k >= 1f) { Despawn(s); continue; }
            float stretch = Mathf.Lerp(0.45f, 1.45f, Mathf.Min(1f, k * 4f));
            s.body.localScale = new Vector3(stretch, 1f, 1f);
            // The streak sprite's head is at +x. Offset the scaled body so
            // that head stays on the flight point and the trail grows back.
            float spriteWidth = s.sr.sprite != null ? s.sr.sprite.bounds.size.x : 0f;
            s.body.localPosition = new Vector3((1f - stretch) * spriteWidth * 0.5f, 0f, 0f);
            float alpha = k < 0.12f ? k / 0.12f : k < 0.68f ? 1f : (1f - k) / 0.32f;
            Paint(s, alpha);
        }
    }

    // ------------------------------------------------------------- bodies --

    int PickTier(int max)
    {
        int total = 0;
        for (int i = 0; i <= max; i++) total += Tiers[i].weight;
        int roll = rng.Next(total);
        for (int i = 0; i < max; i++)
        {
            if (roll < Tiers[i].weight) return i;
            roll -= Tiers[i].weight;
        }
        return max;
    }

    float SizeOf(int kind, int tier)
    {
        return KindSize[kind] * Tiers[tier].scale * Rand(SizeJitterLo, SizeJitterHi);
    }

    // A lone station or planetoid: small, and so genuinely close.
    Plan PlanStructure()
    {
        int kind = Chance(0.5) ? Station : Planetoid;
        var p = new Plan { kind = kind, tier = PickTier(kind == Station ? StationMaxTier : PlanetoidMaxTier),
                           companion = -1, ring = Chance(0.4) };
        p.size = SizeOf(kind, p.tier);
        // A station's rotated silhouette reaches further than half its width.
        p.reach = p.size * (kind == Station ? 0.75f : 0.55f);
        return p;
    }

    // A planet may bring one companion, at its own depth: a moon, or (not for
    // the tiny deep ones) a station in orbit.
    Plan PlanPlanet(int tier)
    {
        var p = new Plan { kind = Planet, tier = tier, companion = -1, ring = Chance(0.4) };
        p.size = SizeOf(Planet, tier);
        p.reach = p.size * 0.55f;
        double roll = rng.NextDouble();
        if (roll < 0.45 || (roll < 0.70 && tier == 0)) p.companion = Moon;
        else if (roll < 0.70) p.companion = Station;
        if (p.companion >= 0)
        {
            p.companionSize = SizeOf(p.companion, tier);
            p.reach = OrbitRadius(p.size, p.companionSize) + p.companionSize * 0.75f;
        }
        return p;
    }

    // Hero planets carry the frame alone. Ordinary planets can have orbiting
    // moons/stations; on a 60%-wide anchor those companions muddy the clean
    // silhouette and central gameplay lane.
    // The planet queue: ordinary planets by tier weight, every HeroEvery-th
    // one a hero.
    Plan NextPlanet()
    {
        if (++planetsSinceHero >= HeroEvery) { planetsSinceHero = 0; return PlanHero(); }
        return PlanPlanet(PickTier(Tiers.Length - 1));
    }

    Plan PlanHero()
    {
        var p = PlanPlanet(Tiers.Length - 1);
        p.companion = -1;
        p.companionSize = 0f;
        p.reach = p.size * 0.55f;
        return p;
    }

    static float OrbitRadius(float planetSize, float companionSize)
    {
        return planetSize * 0.72f + companionSize * 0.5f;
    }

    // How far a body's group extends from its centre.
    float Reach(BackdropPiece p)
    {
        var c = p.children != null ? p.children[0] : null;
        if (c != null) return OrbitRadius(p.size, c.size) + c.size * 0.75f;
        return p.size * (p.kind == Station ? 0.75f : 0.55f);
    }

    // Would a body entering at (x, y) keep clear of every body of its own
    // depth class already in the sky, for as long as both are in view? Every
    // body moves at rate x the same scroll, so gaps change in proportion to
    // distance scrolled and the answer doesn't depend on how the speed
    // changes later. The other class is at a very different depth and is
    // free to pass in front of / behind it.
    bool Fits(float x, float y, float reach, float rate, bool planetClass)
    {
        float spacing = planetClass ? PlanetSpacing : MinSpacing;
        foreach (var pool in bodies)
            foreach (var e in pool.items)
            {
                if (!e.active || e.parent != null || (e.kind == Planet) != planetClass) continue;
                float er = Reach(e), gap = y - e.y;
                if (gap < spacing + reach + er) return false;
                if (rate <= e.rate) continue;                                   // falls behind
                if (Mathf.Abs(x - e.x) >= reach + er + Clearance) continue;     // passes alongside
                float scroll = (e.y + HalfH + e.size * 0.9f + 1f) / e.rate;     // until e is recycled (see Drift)
                if (gap - (rate - e.rate) * scroll < reach + er + Clearance) return false;
            }
        return true;
    }

    // Bring the planned body in at the top (or at `y`, for the opening one).
    // False if the sky has no room for it yet.
    bool Enter(Plan n, float y)
    {
        Tier tier = Tiers[n.tier];
        bool planetClass = n.kind == Planet;
        float rate = set.Spec.Rate(LayerOf(n.tier, planetClass));
        int side = planetClass ? planetSide : structureSide;
        bool opening = !float.IsNaN(y);
        if (!opening) y = HalfH + n.reach + 0.3f;
        // Bigger bodies sit further out, half behind the walls.
        float lane = Rand(Mathf.Min(0.7f + 0.35f * n.reach, 1.6f), 1.9f);
        // Alternate sides; take the other one if this lane would overtake.
        float x = -side * lane;
        if (!opening && !Fits(x, y, n.reach, rate, planetClass))
        {
            x = side * lane;
            if (!Fits(x, y, n.reach, rate, planetClass)) return false;
        }

        var pool = n.kind == Planet ? planets : n.kind == Station ? stations : planetoids;
        var p = pool.Spawn();
        if (p == null) return false;
        Sprite[] art = n.kind == Planet ? giant : n.kind == Station ? StationArt(n.ring, n.size) : RockArt(n.size);
        if (art.Length == 0) { p.Show(false); return true; }
        Dress(p, Pick(art), n.kind, n.tier, n.size, 0, planetClass);
        p.x = x;
        p.y = y;
        // A lit sphere with a fixed terminator can't turn in the picture
        // plane without looking like a spinning decal, so planets and rocks
        // hold a tilt. Ring stations wheel slowly; the others hold theirs.
        p.phase = n.kind == Station ? Rand(-10f, 10f) : Rand(-14f, 14f);
        if (n.kind == Station && n.ring) p.spin = Rand(1.2f, 2.2f) * (Chance(0.5) ? 1f : -1f);
        p.color = Lit(n.kind == Planet ? Pick(PlanetTints) : n.kind == Station ? StationTint : RockTint, tier);
        if (planetClass) planetSide = x < 0f ? -1 : 1;
        else structureSide = x < 0f ? -1 : 1;

        if (n.companion < 0) return true;
        var c = (n.companion == Moon ? moons : stations).Spawn();
        if (c == null) return true;
        art = n.companion == Moon ? MoonArt(n.companionSize) : StationArt(n.ring, n.companionSize);
        if (art.Length == 0) { c.Show(false); return true; }
        Dress(c, Pick(art), n.companion, n.tier, n.companionSize, 2, true);
        c.parent = p;
        c.phase = Rand(0f, 6.283f);
        c.spin = Rand(0.10f, 0.20f) * (Chance(0.5) ? 1f : -1f);    // orbit, rad/s
        c.root.localRotation = Quaternion.Euler(0f, 0f, n.companion == Moon ? p.phase : Rand(-10f, 10f));
        c.color = Lit(n.companion == Moon ? RockTint : StationTint, tier);
        p.slot[0] = c;
        p.children = p.slot;                    // no allocation per spawn
        Orbit(p, c, 0f);
        return true;
    }

    // planetClass: a planet or its companion (which shares the planet's depth).
    void Dress(BackdropPiece p, Sprite s, int kind, int tier, float size, int orderOffset, bool planetClass)
    {
        SetSprite(p, s, size);
        p.kind = kind;
        p.tier = tier;
        string layer = LayerOf(tier, planetClass);
        p.rate = set.Spec.Rate(layer);
        p.sr.sortingOrder = set.Spec.Order(layer) + orderOffset;

        bool sphere = planetMat != null && kind != Station && IsSphere(s);
        p.planet = sphere;
        p.sr.sharedMaterial = sphere ? planetMat : spriteMat;
        if (!sphere) { p.sr.SetPropertyBlock(null); return; }
        p.disc = DiscOf(s);
        p.turn = Rand(0f, 6.283f);
        float seconds = kind == Planet ? Rand(PlanetTurnSecondsMin, PlanetTurnSecondsMax)
                                       : Rand(RockTurnSecondsMin, RockTurnSecondsMax);
        p.turnRate = (Chance(0.5) ? 1f : -1f) * Mathf.PI / seconds;
    }

    // Round bodies whose surface can turn: the giants and the cratered rocks
    // (full size and pre-shrunk). The lone `moon` cell is not a disc.
    public static bool IsSphere(Sprite s)
    {
        if (s == null) return false;
        string n = s.name;
        return n.StartsWith("giant_", System.StringComparison.Ordinal) ||
               n.StartsWith("rocky_", System.StringComparison.Ordinal) ||
               n.StartsWith("mini_rocky_", System.StringComparison.Ordinal);
    }

    // The drawn disc in atlas uv: Space's cuts are centred on their art
    // (build_atlas.py) with a small clear border, so the disc is the rect's
    // centre and half-size less that border (WorldBackdropTest re-measures).
    public static Vector4 DiscOf(Sprite s)
    {
        Rect r = s.textureRect;
        float tw = s.texture.width, th = s.texture.height;
        return new Vector4(r.center.x / tw, r.center.y / th,
                           (r.width * 0.5f - DiscMarginPx) / tw, (r.height * 0.5f - DiscMarginPx) / th);
    }

    void PaintSphere(BackdropPiece p)
    {
        if (!p.planet) return;
        mpb.Clear();
        mpb.SetTexture(IdMainTex, p.sr.sprite.texture);
        mpb.SetVector(IdDisc, p.disc);
        mpb.SetFloat(IdSpin, p.turn);
        p.sr.SetPropertyBlock(mpb);
    }

    public override void Teardown()
    {
        BackdropAtlas.Kill(planetMat);
        planetMat = null;
    }

    // Small on screen, the full-size cells would shimmer (no mipmaps): the
    // atlas carries pre-shrunk copies for that.
    Sprite[] StationArt(bool ring, float size)
    {
        if (size < MiniBelow) return ring ? miniRingStation : miniStation;
        return ring ? ringStation : station;
    }

    Sprite[] RockArt(float size) { return size < MiniBelow ? miniRocky : rocky; }
    Sprite[] MoonArt(float size) { return size < MiniBelow ? miniRocky : moonArt; }

    Color Lit(Color c, Tier t)
    {
        Color o = Color.Lerp(Haze, c, t.clarity) * t.light;
        o.a = 1f;       // solid bodies stay opaque: stars must not show through
        return o;
    }

    // A companion circles its planet on an ellipse tilted with the planet,
    // passing behind it on the far half.
    void Orbit(BackdropPiece p, BackdropPiece c, float dt)
    {
        if (c == null || !c.active) return;
        c.age += dt;
        float a = c.phase + c.age * c.spin;
        float r = OrbitRadius(p.size, c.size);
        float ox = Mathf.Cos(a) * r, oy = Mathf.Sin(a) * r * 0.3f;
        float t = p.phase * Mathf.Deg2Rad, ct = Mathf.Cos(t), st = Mathf.Sin(t);
        c.x = p.x + ox * ct - oy * st;
        c.y = p.y + ox * st + oy * ct;
        Place(c);
        c.sr.sortingOrder = p.sr.sortingOrder + (Mathf.Sin(a) > 0f ? -2 : 2);
        c.turn += c.turnRate * dt;
        Paint(c, 1f);
        PaintSphere(c);
    }

    // ---------------------------------------------------------------- sky --

    void SpawnGalaxy(float y)
    {
        var g = galaxies.Spawn();
        if (g == null) return;
        SetSprite(g, fx.Get(Chance(0.5) ? "galaxy0" : "galaxy1"), Rand(0.9f, 1.5f));
        g.x = Rand(-EdgeX + 0.6f, EdgeX - 0.6f);
        g.y = float.IsNaN(y) ? SpawnY(g.size) : y;
        g.rate = set.Spec.Rate("galaxies");
        // Seen at an angle: the disc is squashed, and turns within its plane.
        g.spin = Rand(1.5f, 3f) * (Chance(0.5) ? 1f : -1f);
        g.root.localScale = new Vector3(g.root.localScale.x, g.root.localScale.y * Rand(0.45f, 0.75f), 1f);
        g.root.localRotation = Quaternion.Euler(0, 0, Rand(-35f, 35f));
        g.color = new Color(0.7f, 0.65f, 0.9f, 0.5f);
    }

    // Comets cross far behind every body (their layer sorts below the deep
    // tier), small and dim.
    void SpawnComet(float scrollVelocity)
    {
        if (comet.Length == 0) return;
        var c = comets.Spawn();
        if (c == null) return;
        SetSprite(c, Pick(comet), Rand(CometMinWidth, CometMaxWidth));
        float dir = Chance(0.5) ? -1f : 1f;              // -1: travels right-to-left
        c.x = -dir * (HalfW + 1f);
        c.y = Rand(HalfH * 0.1f, HalfH * 0.8f);
        c.vx = dir * Rand(1.15f, 1.75f);
        c.vy = -Rand(0.25f, 0.55f);
        c.rate = set.Spec.Rate("comets");
        OrientComet(c, scrollVelocity);
        // Hazed toward the sky's indigo and mostly see-through: the art's
        // white-hot head would otherwise outshine the gameplay in front.
        c.color = CometTint;
        c.color.a = Rand(0.42f, CometMaxAlpha);
    }

    // The visible vertical velocity includes parallax scroll. Orienting from
    // authored velocity alone made the head point away from its path as the
    // run accelerated.
    void OrientComet(BackdropPiece c, float scrollVelocity)
    {
        float screenVy = c.vy - c.rate * scrollVelocity;
        float ang = Mathf.Atan2(screenVy, c.vx) * Mathf.Rad2Deg - 213f;
        c.root.localRotation = Quaternion.Euler(0f, 0f, ang);
    }

    void SpawnShooter()
    {
        var s = shooters.Spawn();
        if (s == null) return;
        SetSprite(s, fx.Get("streak"), Rand(0.5f, 0.9f));
        s.x = Rand(-HalfW, HalfW);
        s.y = Rand(0f, HalfH);
        float ang = Rand(200f, 250f) * Mathf.Deg2Rad;
        float spd = Rand(7f, 11f);
        s.vx = Mathf.Cos(ang) * spd;
        s.vy = Mathf.Sin(ang) * spd;
        s.life = Rand(0.4f, 0.6f);
        s.root.localRotation = Quaternion.Euler(0, 0, ang * Mathf.Rad2Deg);
        s.body.localPosition = Vector3.zero;
        s.body.localScale = Vector3.one;
        s.color = Pick(ShooterTints);
    }
}

// Shared depth model for the planet worlds. The ship flies at atmosphere
// level: ground tiles and landmark set pieces are far below (slow parallax,
// small, art pre-hazed toward the air colour); only haze bands, cloud cels
// and particles pass close. Landmarks are spaced so at most a couple are in
// view, and never spawn on top of each other.
public abstract class PlanetDirector : BackdropDirector
{
    protected BackdropPool haze, clouds;
    readonly Timer hazeTimer = new Timer(6f, 11f, 2.5f);
    readonly Timer cloudTimer = new Timer(7f, 14f, 4f);
    protected readonly List<BackdropPool> landmarks = new List<BackdropPool>();

    protected PlanetDirector(int seed) : base(seed) { }

    public IList<BackdropPool> Landmarks { get { return landmarks; } }

    protected void BuildAir()
    {
        haze = Pool("haze", 2);
        clouds = Pool("clouds", 2);
        SpawnHaze(Rand(-HalfH * 0.3f, HalfH * 0.5f));
    }

    protected BackdropPool LandmarkPool(string layer, int capacity, int orderOffset = 0)
    {
        var p = Pool(layer, capacity, false, orderOffset);
        landmarks.Add(p);
        return p;
    }

    protected void StepAir(float dt, float v)
    {
        if (hazeTimer.Tick(dt, rng)) SpawnHaze(float.NaN);
        if (cloudTimer.Tick(dt, rng)) SpawnCloud();
        foreach (var h in haze.items)
            if (h.active && Drift(h, dt, v)) Paint(h, 1f);
        foreach (var c in clouds.items)
            if (c.active && Drift(c, dt, v)) Paint(c, 1f);
    }

    void SpawnHaze(float y)
    {
        var h = haze.Spawn();
        if (h == null) return;
        SetSprite(h, fx.Get("haze"), HalfW * 2f * Rand(1.15f, 1.35f));
        h.root.localScale = new Vector3(h.root.localScale.x, h.root.localScale.y * Rand(1.2f, 2.2f), 1f);
        h.x = Rand(-0.3f, 0.3f);
        h.y = float.IsNaN(y) ? HalfH + 1.2f : y;
        h.size = 1.5f;
        h.rate = set.Spec.Rate("haze");
        h.color = new Color(1f, 1f, 1f, Rand(0.22f, 0.32f));
    }

    void SpawnCloud()
    {
        var c = clouds.Spawn();
        if (c == null) return;
        SetSprite(c, fx.Get(Chance(0.5) ? "cloud0" : "cloud1"), Rand(2.0f, 3.0f));
        if (Chance(0.5)) c.body.localScale = new Vector3(-1f, 1f, 1f);
        c.x = Rand(-HalfW * 0.6f, HalfW * 0.6f);
        c.y = SpawnY(1f);
        c.vx = Rand(-0.12f, 0.12f);
        c.rate = set.Spec.Rate("clouds");
        c.color = new Color(1f, 1f, 1f, Rand(0.26f, 0.36f));
    }

    // True when the top of the view is clear of other landmarks, so a new
    // one entering doesn't overlap one that just arrived.
    protected bool TopClear(float size)
    {
        foreach (var pool in landmarks)
            foreach (var p in pool.items)
                if (p.active && p.size > 0.6f && p.y > HalfH - (p.size + size) * 0.6f) return false;
        return true;
    }

    // A landmark far below: small, pre-hazed art, far parallax, anywhere
    // across the ground (it's far beneath the play area, not beside it).
    protected BackdropPiece SpawnLandmark(BackdropPool pool, Sprite first, float size, string layer, float y)
    {
        if (first == null || (float.IsNaN(y) && !TopClear(size))) return null;
        var p = pool.Spawn();
        if (p == null) return null;
        SetSprite(p, first, Mathf.Min(size, BackdropCatalog.MaxLandmarkSize));
        p.x = Rand(-EdgeX + p.size * 0.35f, EdgeX - p.size * 0.35f);
        p.y = float.IsNaN(y) ? SpawnY(p.size) : y;
        p.rate = set.Spec.Rate(layer);
        p.color = Color.white;
        if (Chance(0.5)) p.body.localScale = new Vector3(-1f, 1f, 1f);
        return p;
    }

    // Elite landing pads on `pool`'s landmarks still in the upper part of
    // the view (a parked ship has time to be seen before it lifts off):
    // each pad is a fraction of the drawing's bounds from its centre (x
    // right, y up; mirrored with the drawing), `pick` filters by drawing.
    // Ids are unique per landmark and pad (`idBase` per pool).
    protected void LandmarkPads(BackdropPool pool, List<LandingSite> into, System.Func<BackdropPiece, Vector2[]> pads,
                                float scale, int idBase)
    {
        for (int i = 0; i < pool.items.Count; i++)
        {
            var p = pool.items[i];
            if (!p.active || p.sr.sprite == null) continue;
            if (p.y < -HalfH * .15f || p.y > HalfH - p.size * .3f) continue;
            var list = pads(p);
            if (list == null) continue;
            Bounds b = p.sr.sprite.bounds;
            float flip = p.body.localScale.x < 0f ? -1f : 1f;
            for (int k = 0; k < list.Length; k++)
            {
                into.Add(new LandingSite
                {
                    anchor = p.root,
                    local = new Vector3(flip * (b.center.x + list[k].x * b.size.x), b.center.y + list[k].y * b.size.y, 0f),
                    scale = scale,
                    order = p.sr.sortingOrder + 1,
                    id = idBase + i * 8 + k,
                });
            }
        }
    }

    protected void StepLandmarks(BackdropPool pool, float dt, float v)
    {
        foreach (var p in pool.items)
        {
            if (!p.active || !Drift(p, dt, v)) continue;
            p.Animate();
            if (p.Finished) { Despawn(p); continue; }
            Paint(p, 1f);
        }
    }
}

public class FrostDirector : PlanetDirector
{
    BackdropPool glaciers, geysers, aurora, snow;
    Sprite[] auroraFrames, geyserFrames, glacierFrames;
    Timer glacierTimer = new Timer(9f, 15f, 6f);
    Timer geyserTimer = new Timer(5f, 9f, 2f);
    Timer auroraTimer = new Timer(9f, 15f, 1.5f);

    public FrostDirector() : base(1989) { }

    // Elite landing pads (Rimebreaker): on a valley glacier, the lit ice
    // apron at its snout and the two lateral ridges either side of the
    // ice tongue; on an ice massif, the saddle between its left and middle
    // peaks. Far below the play area: parked ships are drawn small.
    public static readonly Vector2[] GlacierPads = { new Vector2(0f, -.37f), new Vector2(-.33f, .06f), new Vector2(.32f, 0f) };
    public static readonly Vector2[] MassifPads = { new Vector2(-.1f, -.14f) };
    public const float ParkedScale = .36f;

    public override void LandingSites(List<LandingSite> into)
    {
        LandmarkPads(glaciers, into, p => p.frames != null ? GlacierPads : MassifPads, ParkedScale, 0);
    }

    protected override void Build()
    {
        auroraFrames = anim.Frames("aurora");
        geyserFrames = fx.Frames("geyser");
        glacierFrames = fx.Frames("glacier");
        geysers = LandmarkPool("geysers", 2);
        glaciers = LandmarkPool("glaciers", 2);
        aurora = Pool("aurora", 2);
        BuildAir();
        snow = Pool("snow", 30);
        Scatter(snow, fx.Get("dot"), 0.05f, 0.11f, new[] { new Color(0.8f, 0.9f, 1f, 0.5f),
            new Color(0.7f, 0.85f, 1f, 0.35f) }, set.Spec.Rate("snow"));
        SpawnGlacier(Rand(-HalfH * 0.1f, HalfH * 0.5f));
    }

    protected override void Step(float dt, float v)
    {
        if (glacierTimer.Tick(dt, rng)) SpawnGlacier(float.NaN);
        if (geyserTimer.Tick(dt, rng)) SpawnGeyser();
        if (auroraTimer.Tick(dt, rng)) SpawnAurora();
        StepLandmarks(glaciers, dt, v);
        StepLandmarks(geysers, dt, v);
        foreach (var a in aurora.items)
        {
            if (!a.active || !Drift(a, dt, v)) continue;
            a.Animate();
            float flare = a.kind == 1 ? Mathf.Max(0f, Mathf.Sin(a.age * 1.3f)) : 0f;
            Paint(a, 0.7f + 0.45f * flare * flare);
        }
        StepAir(dt, v);
        foreach (var s in snow.items)
        {
            Recycle(s, -0.6f, dt, v, 0.5f);
            Paint(s, 1f);
        }
    }

    void SpawnGlacier(float y)
    {
        // Two landmark kinds: a valley glacier (animated meltwater) or a
        // small ice massif.
        if (Chance(0.6) && glacierFrames.Length > 0)
        {
            var g = SpawnLandmark(glaciers, glacierFrames[0], Rand(1.3f, 1.7f), "glaciers", y);
            if (g != null) { g.frames = glacierFrames; g.fps = 4f; g.body.localScale = Vector3.one; }
        }
        else
        {
            SpawnLandmark(glaciers, fx.Get(Chance(0.5) ? "massif0" : "massif1"), Rand(1.1f, 1.5f), "glaciers", y);
        }
    }

    void SpawnGeyser()
    {
        if (geyserFrames.Length == 0) return;
        var g = SpawnLandmark(geysers, geyserFrames[0], Rand(0.26f, 0.36f), "geysers", Rand(-HalfH * 0.4f, HalfH * 0.7f));
        if (g == null) return;
        g.frames = geyserFrames;
        g.fps = 9f;
        g.loop = false;
        g.body.localScale = Vector3.one;
        g.color = new Color(0.75f, 0.88f, 1f, 0.8f);
    }

    void SpawnAurora()
    {
        if (auroraFrames.Length == 0) return;
        var a = aurora.Spawn();
        if (a == null) return;
        a.frames = auroraFrames;
        a.fps = 8f;
        SetSprite(a, auroraFrames[0], HalfW * 2f * Rand(1.0f, 1.25f));
        a.x = Rand(-0.6f, 0.6f);
        a.y = SpawnY(2f);
        a.rate = set.Spec.Rate("aurora");
        a.kind = Chance(0.45) ? 1 : 0;
        a.root.localRotation = Quaternion.Euler(0, 0, Rand(-12f, 12f));
        if (Chance(0.5)) a.body.localScale = new Vector3(-1f, 1f, 1f);
        a.color = new Color(0.75f, 0.9f, 0.9f, 0.5f);
    }
}

public class VerdantDirector : PlanetDirector
{
    BackdropPool waterfalls, ruins, glowspores, spores, steam;
    Sprite[] fall, ruin, firefly, spore, steamFrames;
    Timer fallTimer = new Timer(10f, 16f, 7f);
    Timer ruinTimer = new Timer(7f, 12f, 3f);
    // Steam is a quiet near-rail accent, not a foreground effect. The static
    // tile supplies the distant refinery stacks; these sparse plumes make a
    // few of their vents feel alive without obscuring the flight lane.
    Timer steamTimer = new Timer(5.5f, 8.5f, 2.2f);

    public VerdantDirector() : base(1990) { }

    // Elite landing pads (Resin Warden): on a stepped ruin, its summit
    // platform and the two lower terrace ledges; on the waterfall plateau,
    // the open canopy right of the falls. Obelisks are too thin to land on.
    public static readonly Vector2[] RuinPads = { new Vector2(0f, .33f), new Vector2(-.27f, -.1f), new Vector2(.28f, -.1f) };
    public static readonly Vector2[] CanopyPads = { new Vector2(.25f, .3f) };
    public const float ParkedScale = .32f;

    public override void LandingSites(List<LandingSite> into)
    {
        LandmarkPads(ruins, into, p => p.frames != null ? RuinPads : null, ParkedScale, 0);
        LandmarkPads(waterfalls, into, p => CanopyPads, ParkedScale, 100);
    }

    protected override void Build()
    {
        fall = anim.Frames("waterfall");
        ruin = anim.Frames("ruin");
        firefly = fx.Frames("firefly");
        spore = fx.Frames("spore");
        waterfalls = LandmarkPool("waterfalls", 1);
        ruins = LandmarkPool("ruins", 3);
        BuildAir();
        BuildSteam();
        glowspores = Pool("glowspores", 14);
        spores = Pool("spores", 12);
        // Fireflies and spores are drawn flipbooks (blink / tumble on held
        // drawings); the tint only sets how bright each one is.
        Scatter(glowspores, firefly.Length > 0 ? firefly[0] : fx.Get("dot"), 0.13f, 0.19f,
                new[] { new Color(1f, 1f, 1f, 0.95f), new Color(1f, 0.9f, 0.8f, 0.8f) }, set.Spec.Rate("glowspores"));
        Scatter(spores, spore.Length > 0 ? spore[0] : fx.Get("dot"), 0.07f, 0.1f,
                new[] { new Color(1f, 1f, 1f, 0.6f), new Color(0.8f, 1f, 0.9f, 0.45f) }, set.Spec.Rate("spores"));
        foreach (var f in glowspores.items) { f.frames = firefly.Length > 0 ? firefly : null; f.fps = 8f; f.age = Rand(0f, 3f); }
        foreach (var s in spores.items) { s.frames = spore.Length > 0 ? spore : null; s.fps = 4f; s.age = Rand(0f, 3f); }
        SpawnFall(Rand(-HalfH * 0.1f, HalfH * 0.5f));
    }

    protected override void Step(float dt, float v)
    {
        if (fallTimer.Tick(dt, rng)) SpawnFall(float.NaN);
        if (ruinTimer.Tick(dt, rng)) SpawnRuin();
        if (steamTimer.Tick(dt, rng)) SpawnSteam();
        StepLandmarks(waterfalls, dt, v);
        foreach (var r in ruins.items)
        {
            if (!r.active || !Drift(r, dt, v)) continue;
            if (r.frames != null) r.Animate();
            else
            {
                // Obelisk beacon: a snappy beat, held off between beats.
                float beat = Mathf.Repeat(r.age * 0.6f + r.phase, 1f);
                r.sr.color = new Color(1f, 1f, 1f, beat < 0.12f ? 1f : 0.8f) * new Color(1, 1, 1, set.Alpha);
                continue;
            }
            Paint(r, 1f);
        }
        StepAir(dt, v);
        foreach (var f in glowspores.items)
        {
            Recycle(f, 0.25f, dt, v, 0.9f);
            if (f.frames != null) { f.Animate(); Paint(f, 1f); }
            else Paint(f, Mathf.Sin(f.age * 2.4f + f.phase) > 0.55f ? 1f : 0.06f);
        }
        foreach (var s in spores.items)
        {
            Recycle(s, 0.5f, dt, v, 0.4f);
            s.Animate();
            Paint(s, 1f);
        }
        foreach (var p in steam.items)
        {
            if (!p.active || !Drift(p, dt, v)) continue;
            p.Animate();
            if (p.Finished) { Despawn(p); continue; }
            Paint(p, 1f);
        }
    }

    void BuildSteam()
    {
        steam = Pool("haze", 4, false, 2);
        var tex = Resources.Load<Texture2D>(BackdropCatalog.Folder("Verdant") + "steam_plume_4f_v1");
        if (tex == null) { steamFrames = new Sprite[0]; return; }
        set.Textures.Add(tex);
        int frameWidth = tex.width / 4;
        steamFrames = new Sprite[4];
        for (int i = 0; i < steamFrames.Length; i++)
        {
            steamFrames[i] = Sprite.Create(tex, new UnityEngine.Rect(i * frameWidth, 0, frameWidth, tex.height),
                                            new Vector2(.5f, 0f), BackdropAtlas.PixelsPerUnit,
                                            0, SpriteMeshType.FullRect);
            steamFrames[i].name = "verdant_steam_" + i.ToString("00");
        }
    }

    void SpawnSteam()
    {
        if (steamFrames == null || steamFrames.Length == 0) return;
        var p = steam.Spawn();
        if (p == null) return;
        SetSprite(p, steamFrames[0], Rand(.58f, .82f));
        p.frames = steamFrames;
        p.fps = Rand(4.5f, 6f);
        p.loop = false;
        p.age = 0f;
        // Keep plumes just inside the thick rails: visible in the open scene,
        // never pasted over the frame or across the ship's flight lane.
        p.x = (Chance(.5) ? -1f : 1f) * Rand(HalfW * .46f, HalfW * .62f);
        p.y = Rand(-HalfH * .65f, HalfH * .75f);
        p.vx = Rand(-.035f, .035f);
        p.vy = Rand(.04f, .10f);
        p.rate = set.Spec.Rate("haze") * .35f;
        p.color = new Color(.67f, .90f, .82f, Rand(.24f, .38f));
        Place(p);
    }

    public override void Teardown()
    {
        if (steamFrames == null) return;
        foreach (var s in steamFrames) BackdropAtlas.Kill(s);
        steamFrames = null;
    }

    void SpawnFall(float y)
    {
        if (fall.Length == 0) return;
        var w = SpawnLandmark(waterfalls, fall[0], Rand(1.3f, 1.7f), "waterfalls", y);
        if (w == null) return;
        w.frames = fall;
        w.fps = 12f;
        w.body.localScale = Vector3.one;      // water falls toward the bottom of the art
    }

    void SpawnRuin()
    {
        if (Chance(0.6) && ruin.Length > 0)
        {
            var r = SpawnLandmark(ruins, ruin[0], Rand(0.9f, 1.15f), "ruins", float.NaN);
            if (r != null) { r.frames = ruin; r.fps = 3f; }
        }
        else
        {
            var o = SpawnLandmark(ruins, fx.Get(Chance(0.5) ? "obelisk0" : "obelisk1"), Rand(0.42f, 0.55f), "ruins", float.NaN);
            if (o != null) o.phase = Rand(0f, 1f);
        }
    }
}

public class EmberDirector : PlanetDirector
{
    BackdropPool volcanoes, bursts, embers, ash;
    Sprite[] volcano, burst;
    Timer volcanoTimer = new Timer(8f, 13f, 5f);
    Timer burstTimer = new Timer(1.8f, 3.8f, 1f);

    public EmberDirector() : base(1991) { }

    protected override void Build()
    {
        volcano = anim.Frames("volcano");
        burst = fx.Frames("burst");
        bursts = LandmarkPool("bursts", 3);
        volcanoes = LandmarkPool("volcanoes", 2);
        BuildAir();
        embers = Pool("embers", 24);
        ash = Pool("ash", 12);
        Scatter(embers, fx.Get("dot"), 0.05f, 0.1f, new[] { new Color(1f, 0.55f, 0.18f, 0.75f),
            new Color(1f, 0.75f, 0.3f, 0.6f) }, set.Spec.Rate("embers"));
        Scatter(ash, fx.Get("dot"), 0.04f, 0.08f, new[] { new Color(0.45f, 0.4f, 0.42f, 0.5f) },
                set.Spec.Rate("ash"));
        SpawnVolcano(Rand(-HalfH * 0.1f, HalfH * 0.4f));
    }

    protected override void Step(float dt, float v)
    {
        if (volcanoTimer.Tick(dt, rng)) SpawnVolcano(float.NaN);
        if (burstTimer.Tick(dt, rng)) SpawnBurst();
        StepLandmarks(volcanoes, dt, v);
        StepLandmarks(bursts, dt, v);
        StepAir(dt, v);
        foreach (var e in embers.items)
        {
            Recycle(e, 1.1f, dt, v, 0.7f);
            float f = Mathf.Sin(e.age * 9f + e.phase);
            Paint(e, f > 0.2f ? 1f : 0.55f);
        }
        foreach (var a in ash.items)
        {
            Recycle(a, -0.3f, dt, v, 0.4f);
            Paint(a, 1f);
        }
    }

    // Elite landing pads: the two rock shoulders either side of each
    // forge-volcano's cone (fractions of the drawing's bounds), on volcanoes
    // still in the upper part of the view -- a ship parked there has time to
    // be seen before it lifts off. Far below the play area, so a parked
    // ship is drawn small (ParkedScale) just above the volcano.
    public static readonly Vector2[] VolcanoPads = { new Vector2(-.27f, -.2f), new Vector2(.27f, -.24f) };
    public const float ParkedScale = .36f;

    public override void LandingSites(List<LandingSite> into)
    {
        for (int v = 0; v < volcanoes.items.Count; v++)
        {
            var p = volcanoes.items[v];
            if (!p.active || p.sr.sprite == null) continue;
            if (p.y < -HalfH * .15f || p.y > HalfH - p.size * .3f) continue;
            Bounds b = p.sr.sprite.bounds;
            for (int k = 0; k < VolcanoPads.Length; k++)
            {
                into.Add(new LandingSite
                {
                    anchor = p.root,
                    local = new Vector3(b.center.x + VolcanoPads[k].x * b.size.x, b.center.y + VolcanoPads[k].y * b.size.y, 0f),
                    scale = ParkedScale,
                    order = p.sr.sortingOrder + 1,
                    id = v * 8 + k,
                });
            }
        }
    }

    void SpawnVolcano(float y)
    {
        if (volcano.Length == 0) return;
        var vo = SpawnLandmark(volcanoes, volcano[0], Rand(1.1f, 1.5f), "volcanoes", y);
        if (vo == null) return;
        vo.frames = volcano;
        vo.fps = 6f;
        vo.age = Rand(0f, 2f);       // eruptions out of step with each other
    }

    void SpawnBurst()
    {
        if (burst.Length == 0) return;
        var b = bursts.Spawn();
        if (b == null) return;
        b.frames = burst;
        b.fps = 10f;
        b.loop = false;
        SetSprite(b, burst[0], Rand(0.22f, 0.32f));
        b.x = Rand(-0.12f, 0.12f) * set.TileScale;     // on the lava river, far below
        b.y = Rand(-HalfH * 0.6f, HalfH * 0.8f);
        b.rate = set.Spec.Rate("bursts");
        b.color = new Color(0.9f, 0.78f, 0.68f, 0.85f);
    }
}
