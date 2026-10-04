using UnityEngine;

// Per-world set pieces. Colours here are multiplied into already-dim art and
// kept well below gameplay brightness; see WorldBackdropTest's contrast guard.

public class SpaceDirector : BackdropDirector
{
    BackdropPool wisps, galaxies, stars, stations, planets, rings, moons, rocky, comets, shooters, dust;
    Sprite[] giant, rockyFrames, ringBack, ringFront, comet, station;
    Timer planetTimer = new Timer(16f, 28f, 14f);
    Timer rockyTimer = new Timer(11f, 20f, 9f);
    Timer galaxyTimer = new Timer(20f, 34f, 1f);
    Timer stationTimer = new Timer(26f, 40f, 7f);
    Timer cometTimer = new Timer(14f, 26f, 3.5f);
    Timer shooterTimer = new Timer(2.5f, 6f, 1.5f);

    static readonly Color[] PlanetTints =
    {
        // Multiplied into neutral-grey art: lands on the guide's planet tones
        // (INDIGO_1 #2A2E6B and its family), never red.
        new Color(0.62f, 0.66f, 1.00f), // INDIGO_1
        new Color(0.48f, 0.74f, 0.86f), // teal
        new Color(0.78f, 0.62f, 1.00f), // DUSK violet
        new Color(0.62f, 0.66f, 1.00f), // INDIGO_1 (weighted: most planets are indigo)
        new Color(0.86f, 0.66f, 0.50f), // sodium (rare)
    };

    public SpaceDirector() : base(1988) { }

    protected override void Build()
    {
        giant = anim.Frames("giant");
        rockyFrames = fx.Frames("rocky");
        ringBack = fx.Frames("ringback");
        ringFront = fx.Frames("ringfront");
        comet = fx.Frames("comet");
        station = fx.Frames("station");

        wisps = Pool("wisps", 2);
        galaxies = Pool("galaxies", 1);
        stars = Pool("stars", 26);
        stations = Pool("stations", 1);
        rings = Pool("planets", 2, false, -1);   // back half, behind the planet
        planets = Pool("planets", 1);
        moons = Pool("moons", 2);
        rocky = Pool("moons", 1);
        comets = Pool("comets", 1);
        shooters = Pool("comets", 3, false, 2);
        dust = Pool("dust", 12);

        Scatter(stars, fx.Get("star"), 0.10f, 0.24f, new[] {
            new Color(0.85f, 0.92f, 1f, 0.75f), new Color(1f, 0.82f, 0.62f, 0.7f),
            new Color(0.62f, 0.95f, 1f, 0.75f) }, set.Spec.Rate("stars"));
        Scatter(dust, fx.Get("streak"), 0.35f, 0.6f, new[] { new Color(0.6f, 0.85f, 1f, 0.16f) },
                set.Spec.Rate("dust"));
        foreach (var d in dust.items) d.body.localRotation = Quaternion.Euler(0, 0, 90f);

        // Open on a planet already sweeping past, so a run never starts empty.
        SpawnPlanet(HalfH * 0.3f);
        SpawnGalaxy();

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
        w.x = Rand(-1.4f, 1.4f);
        w.y = y;
        w.rate = set.Spec.Rate("wisps");
        w.spin = Rand(-4f, 4f);
        w.color = Pick(new[] { new Color(0.45f, 0.22f, 0.5f, 0.28f), new Color(0.2f, 0.42f, 0.55f, 0.26f),
                               new Color(0.55f, 0.3f, 0.2f, 0.22f) });
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
            w.body.localRotation = Quaternion.Euler(0, 0, w.age * w.spin);
            float breathe = 1f + 0.06f * Mathf.Sin(w.age * 0.5f + w.phase);
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

        if (galaxyTimer.Tick(dt, rng)) SpawnGalaxy();
        if (stationTimer.Tick(dt, rng)) SpawnStation();
        if (planetTimer.Tick(dt, rng) && planets.ActiveCount == 0) SpawnPlanet(float.NaN);
        if (rockyTimer.Tick(dt, rng)) SpawnRocky();
        if (cometTimer.Tick(dt, rng)) SpawnComet();
        if (shooterTimer.Tick(dt, rng)) SpawnShooter();

        foreach (var g in galaxies.items)
        {
            if (!g.active || !Drift(g, dt, v)) continue;
            g.body.localRotation = Quaternion.Euler(0, 0, g.age * g.spin);
            Paint(g, 1f);
        }
        foreach (var s in stations.items)
        {
            if (!s.active || !Drift(s, dt, v)) continue;
            s.Animate();
            Paint(s, 1f);
        }
        foreach (var p in planets.items)
        {
            if (!p.active) continue;
            if (!Drift(p, dt, v)) continue;
            // The atlas provides the planet's surface animation; a very slow
            // physical turn keeps a giant world feeling alive even during a
            // quiet stretch between frame changes. Rings stay on `root`, so
            // they retain their orbital tilt instead of spinning like a decal.
            p.body.localRotation = Quaternion.Euler(0f, 0f, p.age * p.spin);
            p.Animate();
            Paint(p, 1f);
            if (p.children == null) continue;
            // children[0] back ring, [1] front ring (a child of the planet), [2] moon
            var back = p.children[0];
            if (back != null && back.active)
            {
                back.age = p.age;
                back.root.localPosition = p.root.localPosition;
                back.root.localRotation = p.root.localRotation;
                back.Animate();
                Paint(back, 1f);
            }
            var front = p.children[1];
            if (front != null && front.active)
            {
                front.age = p.age;
                front.root.localPosition = p.root.localPosition + new Vector3(0, 0, -0.01f);
                front.root.localRotation = p.root.localRotation;
                front.Animate();
                Paint(front, 1f);
            }
            var moon = p.children[2];
            if (moon != null && moon.active)
            {
                float a = p.age * moon.spin + moon.phase;
                float r = p.size * 0.8f;
                moon.x = p.x + Mathf.Cos(a) * r;
                moon.y = p.y + Mathf.Sin(a) * r * 0.3f;
                Place(moon);
                // Behind the planet on the far half of its orbit.
                moon.sr.sortingOrder = set.Spec.Order("moons") + (Mathf.Sin(a) > 0f ? -25 : 0);
                Paint(moon, 1f);
            }
        }
        foreach (var r in rocky.items)
        {
            if (!r.active || !Drift(r, dt, v)) continue;
            r.Animate();
            Paint(r, 1f);
        }
        foreach (var c in comets.items)
        {
            if (!c.active || !Drift(c, dt, v)) continue;
            c.Animate();
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
            s.body.localScale = new Vector3(0.4f + 1.6f * Mathf.Min(1f, k * 3f), 1f, 1f);
            Paint(s, k < 0.7f ? 1f : (1f - k) / 0.3f);
        }
    }

    void SpawnGalaxy()
    {
        var g = galaxies.Spawn();
        if (g == null) return;
        SetSprite(g, fx.Get(Chance(0.5) ? "galaxy0" : "galaxy1"), Rand(1.2f, 1.9f));
        g.x = Rand(-EdgeX + 0.6f, EdgeX - 0.6f);
        g.y = SpawnY(g.size);
        g.rate = set.Spec.Rate("galaxies");
        g.spin = Rand(-9f, 9f);
        g.root.localScale = new Vector3(g.root.localScale.x, g.root.localScale.y * Rand(0.45f, 0.75f), 1f);
        g.root.localRotation = Quaternion.Euler(0, 0, Rand(-35f, 35f));
        g.color = new Color(0.65f, 0.6f, 0.8f, 0.55f);
    }

    void SpawnStation()
    {
        if (station.Length == 0) return;
        var s = stations.Spawn();
        if (s == null) return;
        s.frames = station;
        s.fps = 3f;
        SetSprite(s, station[0], Rand(2.2f, 3.0f));
        s.x = (Chance(0.5) ? -1f : 1f) * Rand(0.9f, 1.6f);
        s.y = SpawnY(s.size);
        s.rate = set.Spec.Rate("stations");
        s.vx = Rand(-0.08f, 0.08f);
        s.root.localRotation = Quaternion.Euler(0, 0, Rand(-12f, 12f));
        s.color = new Color(0.78f, 0.78f, 0.86f, 0.9f);
    }

    void SpawnPlanet(float y)
    {
        if (giant.Length == 0) return;
        var p = planets.Spawn();
        if (p == null) return;
        p.frames = giant;
        p.fps = Rand(3.5f, 5f);
        float size = Rand(2.6f, 4.2f);
        SetSprite(p, giant[0], size);
        if (Chance(0.5)) p.body.localScale = new Vector3(-1f, 1f, 1f);   // spin the other way
        p.x = (Chance(0.5) ? -1f : 1f) * Rand(0.6f, 1.5f);
        p.y = float.IsNaN(y) ? SpawnY(size * 1.5f) : y;
        p.rate = set.Spec.Rate("planets") * (size / 3.4f);
        p.vx = -Mathf.Sign(p.x) * Rand(0.02f, 0.07f);
        p.spin = Rand(-2.4f, 2.4f);
        if (Mathf.Abs(p.spin) < 0.7f) p.spin = Mathf.Sign(p.spin == 0f ? 1f : p.spin) * 0.7f;
        p.color = Pick(PlanetTints);
        p.color.a = 1f;
        float tilt = Rand(-28f, 28f);
        p.root.localRotation = Quaternion.Euler(0, 0, tilt);
        p.children = new BackdropPiece[3];

        if (Chance(0.7) && ringBack.Length > 0)
        {
            var back = rings.Spawn();
            var front = rings.Spawn();
            if (back != null && front != null)
            {
                Color rc = Color.Lerp(p.color, new Color(0.8f, 0.78f, 0.9f), 0.5f);
                rc.a = 0.9f;
                SetupRing(back, ringBack, size, rc, true);
                SetupRing(front, ringFront, size, rc, false);
                p.children[0] = back;
                p.children[1] = front;
            }
            else { if (back != null) back.Show(false); if (front != null) front.Show(false); }
        }
        if (Chance(0.6))
        {
            var moon = moons.Spawn();
            if (moon != null)
            {
                SetSprite(moon, fx.Get("moon"), size * Rand(0.11f, 0.16f));
                moon.spin = Rand(0.35f, 0.7f) * (Chance(0.5) ? 1f : -1f);
                moon.phase = Rand(0f, 6.283f);
                moon.color = new Color(0.72f, 0.72f, 0.82f, 1f);
                moon.rate = p.rate;
                p.children[2] = moon;
            }
        }
    }

    void SetupRing(BackdropPiece r, Sprite[] frames, float planetSize, Color c, bool back)
    {
        r.frames = frames;
        r.fps = 6f;
        // Ring art is 448 px wide for a 252 px planet sprite.
        SetSprite(r, frames[0], planetSize * 448f / 252f);
        float halfH = frames[0].bounds.size.y * r.root.localScale.y * 0.5f;
        r.body.localPosition = new Vector3(0f, back ? halfH / r.root.localScale.y : -halfH / r.root.localScale.y, 0f);
        r.color = c;
        r.sr.sortingOrder = set.Spec.Order("planets") + (back ? -1 : 2);
    }

    void SpawnRocky()
    {
        if (rockyFrames.Length == 0) return;
        var r = rocky.Spawn();
        if (r == null) return;
        r.frames = rockyFrames;
        r.fps = Rand(4f, 6f);
        SetSprite(r, rockyFrames[0], Rand(0.7f, 1.3f));
        r.x = Rand(-EdgeX + 0.5f, EdgeX - 0.5f);
        r.y = SpawnY(r.size);
        r.rate = set.Spec.Rate("moons");
        r.color = Pick(PlanetTints);
    }

    void SpawnComet()
    {
        if (comet.Length == 0) return;
        var c = comets.Spawn();
        if (c == null) return;
        c.frames = comet;
        c.fps = 10f;
        SetSprite(c, comet[0], Rand(2.0f, 2.8f));
        float dir = Chance(0.5) ? -1f : 1f;              // -1: travels right-to-left
        c.x = -dir * (HalfW + 1.2f);
        c.y = Rand(HalfH * 0.1f, HalfH * 0.8f);
        c.vx = dir * Rand(0.9f, 1.5f);
        c.vy = -Rand(0.3f, 0.7f);
        c.rate = set.Spec.Rate("comets");
        // Head leads: the art's head is on the left, the tail trails right.
        float ang = Mathf.Atan2(c.vy, c.vx) * Mathf.Rad2Deg + 180f;
        c.root.localRotation = Quaternion.Euler(0, 0, ang);
        c.color = new Color(0.75f, 0.9f, 1f, 0.8f);
    }

    void SpawnShooter()
    {
        var s = shooters.Spawn();
        if (s == null) return;
        SetSprite(s, fx.Get("streak"), Rand(0.9f, 1.5f));
        s.x = Rand(-HalfW, HalfW);
        s.y = Rand(0f, HalfH);
        float ang = Rand(200f, 250f) * Mathf.Deg2Rad;
        float spd = Rand(9f, 14f);
        s.vx = Mathf.Cos(ang) * spd;
        s.vy = Mathf.Sin(ang) * spd;
        s.life = Rand(0.45f, 0.7f);
        s.root.localRotation = Quaternion.Euler(0, 0, ang * Mathf.Rad2Deg);
        s.color = Pick(new[] { new Color(1f, 0.85f, 0.6f, 0.8f), new Color(0.7f, 0.95f, 1f, 0.8f) });
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
    protected readonly System.Collections.Generic.List<BackdropPool> landmarks =
        new System.Collections.Generic.List<BackdropPool>();

    protected PlanetDirector(int seed) : base(seed) { }

    public System.Collections.Generic.IList<BackdropPool> Landmarks { get { return landmarks; } }

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
    BackdropPool waterfalls, ruins, glowspores, spores;
    Sprite[] fall, ruin, firefly, spore;
    Timer fallTimer = new Timer(10f, 16f, 7f);
    Timer ruinTimer = new Timer(7f, 12f, 3f);

    public VerdantDirector() : base(1990) { }

    protected override void Build()
    {
        fall = anim.Frames("waterfall");
        ruin = anim.Frames("ruin");
        firefly = fx.Frames("firefly");
        spore = fx.Frames("spore");
        waterfalls = LandmarkPool("waterfalls", 1);
        ruins = LandmarkPool("ruins", 3);
        BuildAir();
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
