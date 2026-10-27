using UnityEngine;

// Space backdrop redo (feature/space-backdrop): the ambient and landmark art
// that sits on top of the painted sky tiles, all ADDITIVE and driven by a
// stream of its own (`xrng`), so the original director's seeded planet /
// station sequence is untouched. Every atlas is optional: a missing one
// spawns nothing.
//
//   twinkle  animated four-point stars (4 colours x 8 frames), on the stars layer
//   nebula   big, faint painted gas clouds drifting on the wisps layer
//   debris   small cratered rock / scrap fragments drifting past on the deep layer
//   wrecks   a rare derelict capital-ship hulk crossing on the far layer
//   outposts the two dark moons (a half-built scaffolded sphere, a cratered moon with a domed
//            outpost): part of the planet art pool (Enter), the far field only. They turn with
//            the BackdropPlanet shader like the giants and their lamps blink on their own
//            clocks (shader _BlinkAmount).
//   lamps    the wrecks' and debris' little lights blink (BackdropBlink shader on a shared
//            material whose clock is the director's scaled time); wrecks tumble slowly.
//
// Nothing here spawns while Planetfall / Lift-off hold the sky quiet, so the
// arriving planet and the interlude stay clean.
public partial class SpaceDirector
{
    public const int TwinkleCount = 10, NebulaCount = 3, DebrisCount = 7;
    public const string OutpostPrefix = "outpost_";
    public const float OutpostDiscShare = 0.78f;      // the outpost cells' disc, as a share of the cell
    // The outpost moons' art is modest in resolution (a 250 px disc; the moons were approved at that size): they are only ever the FAR
    // field, small on screen. Nearer planets (and every hero) use the hi-res giants.
    public const int OutpostMaxTier = 1;              // Tiers[0] deep, Tiers[1] far
    public const float OutpostMaxWidth = 1.4f;        // world units on screen (the view is ~7.4 wide)

    readonly System.Random xrng = new System.Random(7741);
    BackdropPool twinkles, nebulae, debris, wrecks;
    Sprite[][] twinkleArt;
    Sprite[] nebulaArt, debrisArt, wreckArt, outpostArt;
    Material outpostMat, blinkMat;
    static readonly int IdBlink = Shader.PropertyToID("_Blink");
    readonly System.Collections.Generic.HashSet<Sprite> outpostSet = new System.Collections.Generic.HashSet<Sprite>();
    Timer debrisTimer = new Timer(2.5f, 5.5f, 3f);
    Timer wreckTimer = new Timer(45f, 80f, 20f);
    Timer nebulaTimer = new Timer(16f, 26f, 9f);

    public BackdropPool Twinkles { get { return twinkles; } }
    public BackdropPool Nebulae { get { return nebulae; } }
    public BackdropPool Debris { get { return debris; } }
    public BackdropPool Wrecks { get { return wrecks; } }
    public bool HasOutposts { get { return outpostArt != null && outpostArt.Length > 0; } }

    public Material OutpostMaterial { get { return outpostMat; } }
    public Material BlinkMaterial { get { return blinkMat; } }
    public bool IsOutpostPiece(BackdropPiece p) { return p != null && p.active && IsOutpost(p.sr.sprite); }

    bool IsOutpost(Sprite s) { return s != null && outpostSet.Contains(s); }

    float XRand(float a, float b) { return a + (float)xrng.NextDouble() * (b - a); }

    void BuildExtras()
    {
        var tw = set.Atlas("twinkle");
        twinkleArt = new Sprite[4][];
        bool anyTwinkle = false;
        for (int t = 0; t < 4; t++)
        {
            twinkleArt[t] = tw.Frames("twinkle_" + t);
            anyTwinkle |= twinkleArt[t].Length > 0;
        }
        nebulaArt = set.Atlas("nebula").Frames("neb");
        debrisArt = set.Atlas("debris").Frames("dbr");
        wreckArt = set.Atlas("wrecks").Frames("wreck");
        outpostArt = set.Atlas("outposts").Frames("outpost");
        var blinkShader = Resources.Load<Shader>("BackdropShaders/BackdropBlink");
        if (blinkShader != null) blinkMat = new Material(blinkShader) { name = "SpaceBlink" };
        if (planetMat != null)
        {
            outpostMat = new Material(planetMat.shader) { name = "SpaceOutpost" };
            outpostMat.SetFloat("_BlinkAmount", 1f);
        }
        foreach (var c in outpostArt) outpostSet.Add(c);

        if (anyTwinkle) twinkles = Pool("stars", TwinkleCount, false, 1);
        if (nebulaArt.Length > 0) nebulae = Pool("wisps", NebulaCount);
        if (debrisArt.Length > 0) debris = Pool("deep", DebrisCount);
        if (wreckArt.Length > 0) wrecks = Pool("far", 1);

        if (twinkles != null)
        {
            float rate = set.Spec.Rate("stars");
            foreach (var s in twinkles.items)
            {
                var frames = twinkleArt[xrng.Next(4)];
                if (frames.Length == 0) continue;
                s.Show(true);
                SetSprite(s, frames[0], XRand(0.20f, 0.28f));
                s.frames = frames;
                s.fps = XRand(5f, 9f);
                s.age = XRand(0f, 8f);
                s.x = XRand(-HalfW, HalfW);
                s.y = XRand(-HalfH, HalfH);
                s.rate = rate * XRand(0.7f, 1f);
                s.color = new Color(1f, 1f, 1f, XRand(0.65f, 0.95f));
                Place(s);
            }
        }
        if (nebulae != null)
        {
            SpawnNebula(HalfH * 0.15f);
            SpawnNebula(-HalfH * 0.55f);
        }
    }

    void TeardownExtras()
    {
        BackdropAtlas.Kill(outpostMat);
        BackdropAtlas.Kill(blinkMat);
        outpostMat = blinkMat = null;
    }

    void SpawnNebula(float y)
    {
        var n = nebulae.Spawn();
        if (n == null) return;
        SetSprite(n, nebulaArt[xrng.Next(nebulaArt.Length)], XRand(4.2f, 6f));
        n.x = XRand(-1.6f, 1.6f);
        n.y = y;
        n.rate = set.Spec.Rate("wisps");
        n.spin = XRand(-0.6f, 0.6f);
        n.phase = XRand(0f, 360f);
        n.color = new Color(1f, 1f, 1f, XRand(0.75f, 1f));
    }

    void StepExtras(float dt, float v)
    {
        // The lamps' clock: scaled time, so a paused game holds them still.
        if (outpostMat != null) outpostMat.SetFloat(IdBlink, clock);
        if (blinkMat != null) blinkMat.SetFloat(IdBlink, clock);
        if (twinkles != null)
            foreach (var s in twinkles.items)
            {
                if (!s.active) continue;
                s.age += dt;
                s.y -= s.rate * v * dt;
                float lim = HalfH + 0.4f;
                if (s.y < -lim) { s.y += 2f * lim; s.x = XRand(-HalfW, HalfW); }
                Place(s);
                s.Animate();
                Paint(s, 1f);
            }

        if (nebulae != null)
        {
            if (nebulaTimer.Tick(dt, xrng) && !Quiet) SpawnNebula(SpawnY(5f));
            foreach (var n in nebulae.items)
            {
                if (!n.active || !Drift(n, dt, v)) continue;
                n.body.localRotation = Quaternion.Euler(0f, 0f, n.phase + n.age * n.spin);
                Paint(n, 1f);
            }
        }

        if (debris != null)
        {
            if (debrisTimer.Tick(dt, xrng) && !Quiet) SpawnDebris();
            foreach (var d in debris.items)
            {
                if (!d.active || !Drift(d, dt, v)) continue;
                d.body.localRotation = Quaternion.Euler(0f, 0f, d.phase + d.age * d.spin);
                Paint(d, 1f);
            }
        }

        if (wrecks != null)
        {
            if (wreckTimer.Tick(dt, xrng) && !Quiet && WreckLaneClear()) SpawnWreck();
            foreach (var w in wrecks.items)
            {
                if (!w.active || !Drift(w, dt, v)) continue;
                w.root.localRotation = Quaternion.Euler(0f, 0f, w.phase + w.age * w.spin);
                Paint(w, 1f);
            }
        }
    }

    void SpawnDebris()
    {
        var d = debris.Spawn();
        if (d == null) return;
        SetSprite(d, debrisArt[xrng.Next(debrisArt.Length)], XRand(0.20f, 0.42f));
        if (blinkMat != null) d.sr.sharedMaterial = blinkMat;
        d.x = XRand(-EdgeX, EdgeX);
        d.y = SpawnY(d.size);
        d.rate = set.Spec.Rate("deep") * XRand(0.8f, 1.3f);
        d.vx = XRand(-0.05f, 0.05f);
        d.spin = XRand(4f, 16f) * (xrng.NextDouble() < 0.5 ? 1f : -1f);
        d.phase = XRand(0f, 360f);
        // Dim and hazed: background rubble, never a target.
        d.color = new Color(0.62f, 0.64f, 0.82f, XRand(0.55f, 0.8f));
    }

    // A wreck is a rare landmark: only when the lone-structure lane is clear at the top.
    bool WreckLaneClear()
    {
        foreach (var pool in bodies)
            foreach (var e in pool.items)
                if (e.active && e.parent == null && e.kind != Planet && e.y > HalfH - 4.5f) return false;
        return true;
    }

    void SpawnWreck()
    {
        var w = wrecks.Spawn();
        if (w == null) return;
        SetSprite(w, wreckArt[xrng.Next(wreckArt.Length)], XRand(0.95f, 1.35f));
        float side = xrng.NextDouble() < 0.5 ? -1f : 1f;
        w.x = side * XRand(1.1f, 1.9f);
        w.y = SpawnY(w.size * 2f);
        w.rate = set.Spec.Rate("far");
        w.phase = XRand(-30f, 30f);
        w.spin = XRand(2f, 5f) * (xrng.NextDouble() < 0.5 ? 1f : -1f);
        w.root.localRotation = Quaternion.Euler(0f, 0f, w.phase);
        if (blinkMat != null) w.sr.sharedMaterial = blinkMat;
        w.color = new Color(0.78f, 0.8f, 0.95f, 0.9f);
    }
}
