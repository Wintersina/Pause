using UnityEngine;

// The hit that costs a heart but not the run: a short blast on the hull and
// a cloud of smoke that is left behind. Not the death crash (DeathCrash owns
// the fatal hit) and not the standing damage emitters (ShipDamageFx's engine
// smoke and sparks, which stay with the ship).
//
//   Blast  the shared v2 neon burst (TargetExplosion.Burst, a pooled
//          flipbook), small, centred on the hull, sized with the ship
//          (ShipScale.Live). Brief: it is gone in well under a second.
//   Smoke  HeartLossSmoke: a few soft grey puffs from the DamageFx smoke row,
//          pooled (a fixed ring of renderers built once, never per hit).
//          They are world things: they stop following the ship, ride the
//          scrolling world down past it (the same rule hazards and the
//          explosion flipbooks use), swell, thin out and are gone after about
//          two seconds or once off the bottom. They draw BEHIND the hull and
//          semi-transparent so the ship, the hearts and the hazards stay
//          readable through them.
//
// Both run on gameplay time (TargetExplosion.Delta): frozen while the world
// is paused (timeScale 0), slowed by slow motion (the boss entrance, resume).
public static class HeartLossFx
{
    // Burst: small, so it never hides what hit the ship.
    public const float BlastHoldScale = .8f;

    public static void Play(GameObject ship, int shipId)
    {
        if (ship == null) return;
        Vector3 at = ship.transform.position;
        float world = TargetExplosion.WorldSizeFor(TargetExplosion.Size.Small, TargetExplosion.Kind.Metal) * ShipScale.Live * 1.1f;
        TargetExplosion.Burst(at, TargetExplosion.Kind.Metal, world, shipId, BlastHoldScale, true, 1.1f, 1.3f);
        var sr = ship.GetComponent<SpriteRenderer>();
        HeartLossSmoke.Emit(at, sr != null ? sr.sortingLayerID : 0, sr != null ? sr.sortingOrder : 0);
    }
}

public class HeartLossSmoke : MonoBehaviour
{
    public const int MaxPuffs = 24;       // the whole pool; a hit uses PuffsPerHit
    public const int PuffsPerHit = 7;
    public const float LifeMin = 1.5f, LifeMax = 2.1f;   // smoke lingers longer than the blast
    public const float StartSize = .45f, EndSize = 1.15f;   // world units, x ship scale
    public const float PeakAlpha = .5f;   // semi-transparent: readable through it
    const float OffscreenMargin = 1.5f;
    static readonly Color Tint = new Color(.74f, .70f, .82f, 1f);

    struct Puff
    {
        public Vector3 pos, vel;
        public float age, life, spin, angle, size;
        public bool alive;
    }

    static HeartLossSmoke instance;

    Puff[] puffs;
    SpriteRenderer[] renderers;

    public static int ActiveCount
    {
        get
        {
            if (instance == null || instance.puffs == null) return 0;
            int n = 0;
            for (int i = 0; i < instance.puffs.Length; i++) if (instance.puffs[i].alive) n++;
            return n;
        }
    }

    public static int PoolSize { get { return instance != null && instance.puffs != null ? instance.puffs.Length : 0; } }
    public static HeartLossSmoke Instance { get { return instance; } }
    public Vector3 PuffPosition(int i) { return puffs[i].pos; }
    public bool PuffAlive(int i) { return puffs[i].alive; }
    public SpriteRenderer PuffRenderer(int i) { return renderers[i]; }

    static HeartLossSmoke Ensure()
    {
        if (instance != null) return instance;
        var go = new GameObject("~HeartLossSmoke");
        instance = go.AddComponent<HeartLossSmoke>();
        instance.Build();
        return instance;
    }

    void Build()
    {
        puffs = new Puff[MaxPuffs];
        renderers = new SpriteRenderer[MaxPuffs];
        for (int i = 0; i < MaxPuffs; i++)
        {
            var go = new GameObject("~puff" + i, typeof(SpriteRenderer));
            go.transform.SetParent(transform, false);
            renderers[i] = go.GetComponent<SpriteRenderer>();
            renderers[i].enabled = false;
        }
    }

    // A hit's smoke: PuffsPerHit puffs from the hull, thrown out softly.
    // Reuses free slots, then steals the oldest (never grows the pool).
    public static void Emit(Vector3 at, int sortingLayerId, int hullOrder)
    {
        var s = Ensure();
        float scale = ShipScale.Live;
        for (int k = 0; k < PuffsPerHit; k++)
        {
            int slot = s.Slot();
            float a = (k / (float)PuffsPerHit) * Mathf.PI * 2f + Random.Range(-.4f, .4f);
            float speed = Random.Range(.25f, .8f) * scale;
            s.puffs[slot] = new Puff
            {
                pos = new Vector3(at.x + Mathf.Cos(a) * .12f * scale, at.y + Mathf.Sin(a) * .12f * scale, at.z + .01f),
                vel = new Vector3(Mathf.Cos(a), Mathf.Sin(a) * .6f, 0f) * speed,
                age = 0f, life = Random.Range(LifeMin, LifeMax), spin = Random.Range(-40f, 40f),
                angle = Random.Range(0f, 360f), size = Random.Range(.85f, 1.15f) * scale, alive = true,
            };
            var sr = s.renderers[slot];
            sr.sortingLayerID = sortingLayerId;
            sr.sortingOrder = hullOrder - 1;   // behind the hull
            sr.sprite = ShipDamageFx.Frame(ShipDamageFx.RowSmoke, 0);
            sr.color = new Color(Tint.r, Tint.g, Tint.b, 0f);
            sr.enabled = true;
            s.Apply(slot);
        }
    }

    int steal;

    // A free slot, else the oldest puff (ties: round-robin, so one hit's own
    // puffs never overwrite each other when the pool is full).
    int Slot()
    {
        int oldest = -1;
        float best = -1f;
        for (int i = 0; i < puffs.Length; i++)
        {
            if (!puffs[i].alive) return i;
            float u = puffs[i].age / puffs[i].life;
            if (u > best + 1e-4f) { best = u; oldest = i; }
        }
        if (best <= 1e-4f) { steal = (steal + 1) % puffs.Length; return steal; }   // all brand new
        return oldest;
    }

    void Update() { Tick(TargetExplosion.Delta()); }

    // One frame of gameplay time; dt <= 0 (paused) changes nothing.
    public void Tick(float dt)
    {
        if (dt <= 0f || puffs == null) return;
        bool scrolling = TargetExplosion.WorldScrolling;
        // the world moves on scaled time (not the explosions' cinematic floor), and not at all during the death crash
        float worldDt = Application.isPlaying ? Mathf.Min(dt, Time.deltaTime) : dt;
        float scroll = scrolling && !DeathCrash.Animating ? moveBackGround.speed * worldDt * 30f : 0f;
        float bottom = CameraFit.ViewBottom - OffscreenMargin;
        for (int i = 0; i < puffs.Length; i++)
        {
            if (!puffs[i].alive) continue;
            ref Puff p = ref puffs[i];
            p.age += dt;
            if (p.age >= p.life) { Retire(i); continue; }
            p.vel *= Mathf.Max(0f, 1f - 2.2f * dt);   // the throw dies away...
            p.pos += p.vel * dt;
            p.pos.y -= scroll;                          // ...and the world carries it past the ship
            p.angle += p.spin * dt;
            if (p.pos.y < bottom) { Retire(i); continue; }
            Apply(i);
        }
    }

    // Alpha: fades in over the first tenth, then thins out (quadratically) to nothing.
    public static float AlphaAt(float u)
    {
        u = Mathf.Clamp01(u);
        float fadeIn = Mathf.Clamp01(u / .1f);
        return PeakAlpha * fadeIn * (1f - u * u);
    }

    void Apply(int i)
    {
        ref Puff p = ref puffs[i];
        float u = Mathf.Clamp01(p.age / p.life);
        var sr = renderers[i];
        sr.sprite = ShipDamageFx.Frame(ShipDamageFx.RowSmoke, Mathf.Min(3, Mathf.FloorToInt(u * 4f)));
        var c = Tint;
        c.a = AlphaAt(u);
        sr.color = c;
        var t = sr.transform;
        t.position = p.pos;
        t.rotation = Quaternion.Euler(0f, 0f, p.angle);
        t.localScale = Vector3.one * (Mathf.Lerp(StartSize, EndSize, u) * p.size);
    }

    void Retire(int i)
    {
        puffs[i].alive = false;
        renderers[i].enabled = false;
    }

    // Tests / scene resets.
    public static void Clear()
    {
        if (instance == null || instance.puffs == null) return;
        for (int i = 0; i < instance.puffs.Length; i++) instance.Retire(i);
    }
}
