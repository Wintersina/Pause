using System.Collections.Generic;
using UnityEngine;

// Spawns, moves and animates one world's set pieces and particles.
//
// Each world schedules a few "events" (a ringed planet sweeping past, an
// aurora flare, an eruption) on timers so something happens every few
// seconds, while pools cap how much is ever on screen at once. All motion is
// integrated from the scaled dt WorldBackdrop passes in -- nothing here reads
// Time.*, so a paused game is a still frame.
public abstract class BackdropDirector
{
    protected BackdropSet set;
    protected BackdropAtlas fx, anim;
    protected readonly System.Random rng;       // own stream: never perturbs gameplay's Random
    protected readonly List<BackdropPool> pools = new List<BackdropPool>();
    protected float clock;

    protected BackdropDirector(int seed) { rng = new System.Random(seed); }

    public IList<BackdropPool> Pools { get { return pools; } }

    public void Init(BackdropSet owner)
    {
        set = owner;
        fx = owner.Fx;
        anim = owner.Anim;
        Build();
    }

    protected abstract void Build();
    protected abstract void Step(float dt, float v);

    // Releases anything the director created itself (materials, textures).
    public virtual void Teardown() { }

    public void Tick(float dt, float velocity)
    {
        clock += dt;
        Step(dt, velocity);
    }

    // ------------------------------------------------------------ helpers --

    protected float Rand(float a, float b) { return a + (float)rng.NextDouble() * (b - a); }
    protected bool Chance(double p) { return rng.NextDouble() < p; }
    protected T Pick<T>(T[] options) { return options[rng.Next(options.Length)]; }

    protected float HalfW { get { return set.HalfWidth; } }
    protected float HalfH { get { return set.HalfHeight; } }
    // The walls cover the outer strip; set pieces hug the playfield edges.
    protected float EdgeX { get { return Mathf.Min(HalfW, 2.5f); } }

    protected BackdropPool Pool(string layer, int capacity, bool blend = false, int orderOffset = 0)
    {
        var spec = set.Spec;
        var p = new BackdropPool(set.Root, layer, capacity, spec.Order(layer) + orderOffset,
                                 set.DepthZ(layer), blend);
        pools.Add(p);
        return p;
    }

    protected void SetSprite(BackdropPiece p, Sprite s, float widthUnits)
    {
        p.sr.sprite = s;
        if (p.blend != null) p.blend.sprite = s;
        float w = s != null ? s.bounds.size.x : 1f;
        p.size = widthUnits;
        float k = widthUnits / Mathf.Max(0.0001f, w);
        p.root.localScale = new Vector3(k, k, 1f);
    }

    protected void Place(BackdropPiece p)
    {
        p.root.localPosition = new Vector3(p.x, p.y, 0f);
    }

    protected void Paint(BackdropPiece p, float alpha)
    {
        Color c = p.color;
        c.a *= alpha * set.Alpha;
        p.sr.color = c;
        if (p.blend != null && p.frames == null) p.blend.color = new Color(0, 0, 0, 0);
    }

    // Standard drift: parallax with the world plus the piece's own velocity.
    // Returns false (and recycles the piece) once it has left the view.
    protected bool Drift(BackdropPiece p, float dt, float v)
    {
        p.age += dt;
        p.x += p.vx * dt;
        p.y += (p.vy - p.rate * v) * dt;
        Place(p);
        float margin = p.size * 0.9f + 1f;
        if (p.y < -HalfH - margin || p.y > HalfH + margin + 6f ||
            Mathf.Abs(p.x) > HalfW + margin + 4f)
        {
            Despawn(p);
            return false;
        }
        return true;
    }

    protected void Despawn(BackdropPiece p)
    {
        if (p.children != null)
            foreach (var c in p.children) if (c != null) c.Show(false);
        p.Show(false);
    }

    protected float SpawnY(float size) { return HalfH + size * 0.6f + 0.3f; }

    // A countdown that fires at random intervals in [min, max].
    protected class Timer
    {
        readonly float min, max;
        float left;
        public Timer(float min, float max, float first) { this.min = min; this.max = max; left = first; }
        public bool Tick(float dt, System.Random rng)
        {
            left -= dt;
            if (left > 0f) return false;
            left = min + (float)rng.NextDouble() * (max - min);
            return true;
        }
    }

    // Particles that recycle: when one leaves the view it re-enters on the
    // opposite edge. Used for stars, snow, spores, embers, ash and dust.
    protected void Scatter(BackdropPool pool, Sprite sprite, float minSize, float maxSize, Color[] colors,
                           float rate)
    {
        foreach (var p in pool.items)
        {
            p.Show(true);
            SetSprite(p, sprite, Rand(minSize, maxSize));
            p.x = Rand(-HalfW, HalfW);
            p.y = Rand(-HalfH, HalfH);
            p.phase = Rand(0f, 6.283f);
            p.color = Pick(colors);
            p.rate = rate;
            Place(p);
        }
    }

    protected void Recycle(BackdropPiece p, float extraVy, float dt, float v, float sway)
    {
        p.age += dt;
        p.y += (extraVy - p.rate * v) * dt;
        p.x += Mathf.Sin(p.age * 1.7f + p.phase) * sway * dt;
        float lim = HalfH + 0.4f;
        if (p.y < -lim) { p.y += 2f * lim; p.x = Rand(-HalfW, HalfW); }
        else if (p.y > lim) { p.y -= 2f * lim; p.x = Rand(-HalfW, HalfW); }
        if (p.x < -HalfW - 0.3f) p.x += 2f * HalfW + 0.6f;
        else if (p.x > HalfW + 0.3f) p.x -= 2f * HalfW + 0.6f;
        Place(p);
    }
}
