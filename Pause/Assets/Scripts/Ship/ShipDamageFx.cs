using System.Collections.Generic;
using UnityEngine;

// The flying ship's damage FX: each hull's own emitters for its damage state
// (ShipDamageTable), drawn from the flat-cel DamageFx atlas
// (Art/Resources/ShipArt/Hulls/src~/damage_fx.py).
//
//   sparks  the blown panel crackles: a spark-flash flipbook on the panel and
//           two or three hot sparks flying off it
//   arc     an electric arc flicks across the bare wiring, on and off
//   smoke   puffs from the damaged engine, trailing down behind the ship
//   flame   a small flame lick, tip trailing down
//   leak    fuel / coolant droplets in the hull's (skin's) colour
//
// A state shows its own emitters and every earlier one's, so the last life
// has the most. A hit that worsens the state sprays a burst of hull chunks
// and dark scrap from the spot it just broke (the state's first emitter);
// healing (the green atom, Dove's Mending) steps the state back down and the
// emitters it added stop. The hull's hit flash (ShipHullAnimator) is
// untouched; the shield and Cloak don't involve this at all.
//
// Emitters ride the hull: each is placed every frame at its canvas point
// posed like the frame on screen (bob, bank lean -- ShipDamageTable.Local),
// under the hull's transform, so they turn with the Ninja / UFO spin. What
// flies off (sparks, smoke, drops, chunks) lives in world space once
// launched, so smoke trails behind a moving ship. Nothing is drawn over the
// hearts or the ship elements registered in ShipUiSlots (secret meter, gun,
// charge indicator): a particle there is hidden for that frame.
//
// Scaled time: at timeScale 0 nothing advances (no frame, no motion, no
// emission). Everything is pooled when the component starts -- one renderer
// per emitter plus a fixed particle pool, with the oldest particle reused
// when it is full -- so a frame allocates nothing.
[DefaultExecutionOrder(60)]   // after the hull pose (lifeControler) and the hearts (50)
public class ShipDamageFx : MonoBehaviour
{
    public const int MaxParticles = 32;
    public const int Ticks = 24;            // flipbook clock, frames per second

    // DamageFx atlas rows (damage_fx.py), 4 drawings each.
    public const int RowCrackle = 0, RowSpark = 1, RowArc = 2, RowSmoke = 3, RowFlame = 4,
                     RowDrop = 5, RowChunk = 6, RowScrap = 7;
    const int AtlasColumns = 4, AtlasRows = 8;

    // Sizes and speeds are fractions of the hull's longest edge (world), so a
    // big and a small hull read alike.
    public const float SparkSize = .18f, ArcSize = .34f, FlameSize = .24f, CrackleSize = .26f;
    public const float SmokeStart = .12f, SmokeEnd = .24f, DropSize = .08f, ChunkSize = .11f;
    // The furthest anything flies from its emitter, in hull lengths (tests).
    public const float MaxReach = .75f;

    static Sprite[] frames;
    static readonly int[] CrackleTicks = { 1, 1, 2, 2 };

    struct Particle
    {
        public Vector3 pos, vel;
        public float age, life, size0, size1, angle, spin, gravity, drag;
        public int row;
        public bool alive, faceVelocity;
        public Vector3 origin;
    }

    struct Emitter
    {
        public SpriteRenderer sr;
        public float next, clock, angle;
        public bool playing;
    }

    int id;
    SpriteRenderer hull;
    lifeControler life;
    ShipLivesIndicator hearts;
    Transform root;
    Emitter[] emitters;
    Particle[] particles;
    SpriteRenderer[] particleRenderers;
    readonly List<Bounds> avoid = new List<Bounds>();
    int state;
    float hullSize = .58f;
    Color leakTint = Color.white;
    bool started;

    public int ShipIdShown { get { return id; } }
    public int State { get { return state; } }
    public int EmitterCount { get { return emitters != null ? emitters.Length : 0; } }
    public int LiveEmitters { get { return ShipDamageTable.Count(id, state); } }
    public int PoolSize { get { return particleRenderers != null ? particleRenderers.Length : 0; } }
    public float HullSize { get { return hullSize; } }
    public int Bursts { get; private set; }

    public int ActiveParticles
    {
        get
        {
            int n = 0;
            if (particles != null) for (int i = 0; i < particles.Length; i++) if (particles[i].alive) n++;
            return n;
        }
    }

    public SpriteRenderer EmitterRenderer(int i) { return emitters[i].sr; }
    public SpriteRenderer ParticleRenderer(int i) { return particleRenderers[i]; }
    public Vector3 ParticlePosition(int i) { return particles[i].pos; }
    public Vector3 ParticleOrigin(int i) { return particles[i].origin; }
    public bool ParticleAlive(int i) { return particles[i].alive; }

    public static Sprite Frame(int row, int drawing)
    {
        if (frames == null || frames[0] == null) frames = Slice();
        if (frames == null) return null;
        return frames[row * AtlasColumns + Mathf.Clamp(drawing, 0, AtlasColumns - 1)];
    }

    static Sprite[] Slice()
    {
        var atlas = Resources.Load<Texture2D>("ShipArt/DamageFx");
        if (atlas == null) return null;
        var result = new Sprite[AtlasColumns * AtlasRows];
        float w = atlas.width / (float)AtlasColumns, h = atlas.height / (float)AtlasRows;
        for (int row = 0; row < AtlasRows; row++)
            for (int col = 0; col < AtlasColumns; col++)
            {
                // the flame hangs from its base (top of the cell)
                var pivot = row == RowFlame ? new Vector2(.5f, .86f) : new Vector2(.5f, .5f);
                var s = Sprite.Create(atlas, new Rect(col * w, (AtlasRows - 1 - row) * h, w, h), pivot, w);
                s.name = "damageFx_" + row + "_" + col;
                result[row * AtlasColumns + col] = s;
            }
        return result;
    }

    void Start() { Build(); }

    // Creates every renderer up front (idempotent).
    public void Build()
    {
        if (started) return;
        started = true;
        id = ShipId.Of(gameObject, ShipId.Equipped());
        hull = GetComponent<SpriteRenderer>();
        life = GetComponent<lifeControler>();
        if (hull != null && hull.sprite != null)
        {
            var size = hull.sprite.bounds.size;
            float scale = Mathf.Abs(transform.lossyScale.x);
            hullSize = Mathf.Max(size.x, size.y) * (scale > 0f ? scale : 1f);
        }
        var c = ShipHullArt.SkinHue(id);
        leakTint = c[0];

        root = new GameObject("~DamageFx").transform;
        root.SetParent(transform, false);
        int order = hull != null ? hull.sortingOrder : 0;
        int layer = hull != null ? hull.sortingLayerID : 0;

        int n = ShipDamageTable.Total(id);
        emitters = new Emitter[n];
        for (int i = 0; i < n; i++)
        {
            var go = new GameObject("~DamageEmitter" + i, typeof(SpriteRenderer));
            go.transform.SetParent(root, false);
            var sr = go.GetComponent<SpriteRenderer>();
            sr.sortingLayerID = layer;
            sr.sortingOrder = order + 1;   // over the hull, under the hearts (+2)
            sr.enabled = false;
            emitters[i].sr = sr;
            emitters[i].next = Random.Range(0f, .3f);
        }

        particles = new Particle[MaxParticles];
        particleRenderers = new SpriteRenderer[MaxParticles];
        for (int i = 0; i < MaxParticles; i++)
        {
            var go = new GameObject("~DamageBit" + i, typeof(SpriteRenderer));
            go.transform.SetParent(root, false);
            var sr = go.GetComponent<SpriteRenderer>();
            sr.sortingLayerID = layer;
            sr.sortingOrder = order + 1;
            sr.enabled = false;
            particleRenderers[i] = sr;
        }
        hearts = GetComponent<ShipLivesIndicator>();
        state = CurrentState();
    }

    int CurrentState()
    {
        if (buttonClicks.playerDied) return 0;
        return ShipDamageTable.StateFor(collisionDetection.lifeCounter);
    }

    void LateUpdate()
    {
        Tick(Time.deltaTime);
    }

    // One frame of scaled time. dt <= 0 (the world frozen) changes nothing.
    public void Tick(float dt)
    {
        if (!started) Build();
        if (dt <= 0f) return;

        int now = CurrentState();
        if (now != state)
        {
            int was = state;
            state = now;
            if (now > was) Burst(now);
            else for (int i = ShipDamageTable.Count(id, now); i < emitters.Length; i++) StopEmitter(i);
        }

        if (hearts == null) TryGetComponent(out hearts);   // attached after the ship spawns
        avoid.Clear();
        ShipUiSlots.Drawn(transform, avoid, this);
        if (hearts != null) hearts.HeartBounds(avoid);

        int column = life != null && life.HullAnimator != null ? life.HullAnimator.Column : 0;
        int live = ShipDamageTable.Count(id, state);
        for (int i = 0; i < emitters.Length; i++)
        {
            if (i < live) StepEmitter(i, column, dt);
            else if (emitters[i].sr.enabled) StopEmitter(i);
        }
        StepParticles(dt);
    }

    // The emitter's world position for the pose on screen.
    public Vector3 EmitterWorld(int i, int column)
    {
        var e = ShipDamageTable.Get(id, i);
        Vector2 local = ShipDamageTable.Local(id, e, column);
        return transform.TransformPoint(new Vector3(local.x, local.y, 0f));
    }

    Vector2 OutwardWorld(DamageEmitter e)
    {
        Vector2 d = ShipDamageTable.Outward(e);
        Vector3 w = transform.TransformDirection(new Vector3(d.x, d.y, 0f));
        return new Vector2(w.x, w.y).normalized;
    }

    void StopEmitter(int i)
    {
        emitters[i].playing = false;
        emitters[i].sr.enabled = false;
    }

    void StepEmitter(int i, int column, float dt)
    {
        var e = ShipDamageTable.Get(id, i);
        ref Emitter em = ref emitters[i];
        var sr = em.sr;
        var t = sr.transform;
        Vector3 at = EmitterWorld(i, column);
        t.position = new Vector3(at.x, at.y, transform.position.z - .01f);
        float local = 1f / Mathf.Max(1e-4f, Mathf.Abs(root.lossyScale.x));
        float hurry = state >= 2 ? .7f : 1f;   // the last life crackles more
        em.next -= dt;
        em.clock += dt;

        switch (e.kind)
        {
            case DamageEmitterKind.Sparks:
                if (em.next <= 0f)
                {
                    em.next = Random.Range(.35f, .8f) * hurry;
                    em.playing = true;
                    em.clock = 0f;
                    em.angle = Random.Range(0f, 90f);
                    Vector2 o = OutwardWorld(e);
                    int n = Random.Range(2, 4);
                    for (int k = 0; k < n; k++) SpawnSpark(at, o);
                }
                PlayOnce(ref em, RowCrackle, CrackleSize * hullSize * local, CrackleTicks);
                t.rotation = Quaternion.Euler(0f, 0f, em.angle);
                break;

            case DamageEmitterKind.Arc:
                if (em.next <= 0f)
                {
                    em.playing = !em.playing;
                    em.clock = 0f;
                    em.next = em.playing ? Random.Range(.2f, .35f) : Random.Range(.35f, .9f) * hurry;
                    em.angle = Random.Range(-35f, 35f) + (Random.value < .5f ? 0f : 180f);
                }
                sr.enabled = em.playing;
                if (em.playing)
                {
                    sr.sprite = Frame(RowArc, Mathf.FloorToInt(em.clock * Ticks / 2f) % AtlasColumns);
                    t.localScale = Vector3.one * (ArcSize * hullSize * local);
                    // turns with the hull (it's on its wiring)
                    t.rotation = transform.rotation * Quaternion.Euler(0f, 0f, em.angle);
                }
                break;

            case DamageEmitterKind.Flame:
                sr.enabled = true;
                int f = Mathf.FloorToInt(em.clock * Ticks / 2f);
                sr.sprite = Frame(RowFlame, f % AtlasColumns);
                float flicker = 1f + ((f * 7) % 3 - 1) * .08f;
                t.localScale = Vector3.one * (FlameSize * hullSize * local * flicker);
                t.rotation = Quaternion.identity;   // the tip always trails down
                break;

            case DamageEmitterKind.Smoke:
                if (em.next <= 0f)
                {
                    em.next = Random.Range(.09f, .16f) * hurry;
                    var v = new Vector3(Random.Range(-.18f, .18f), -Random.Range(.8f, 1.1f), 0f) * hullSize;
                    Spawn(RowSmoke, at + (Vector3)(Random.insideUnitCircle * .03f * hullSize), v,
                          Random.Range(.5f, .7f), SmokeStart * hullSize, SmokeEnd * hullSize, 0f, 0f, 1.2f,
                          Color.white, -1, false);
                }
                break;

            case DamageEmitterKind.Leak:
                if (em.next <= 0f)
                {
                    em.next = Random.Range(.22f, .4f) * hurry;
                    Vector2 o = OutwardWorld(e);
                    var v = new Vector3(o.x * .25f, -.25f, 0f) * hullSize;
                    Spawn(RowDrop, at, v, .45f, DropSize * hullSize, DropSize * hullSize, 0f, -3.2f * hullSize, 0f,
                          leakTint, -1, true);
                }
                break;
        }
        Duck(sr, t.position, sr.enabled ? CrackleSize * hullSize * .5f : 0f);
    }

    // A one-shot flipbook on an emitter's own renderer.
    void PlayOnce(ref Emitter em, int row, float scale, int[] ticks)
    {
        if (!em.playing) { em.sr.enabled = false; return; }
        float t = em.clock * Ticks;
        int frame = 0;
        while (frame < ticks.Length && t >= ticks[frame]) { t -= ticks[frame]; frame++; }
        if (frame >= ticks.Length) { em.playing = false; em.sr.enabled = false; return; }
        em.sr.enabled = true;
        em.sr.sprite = Frame(row, frame);
        em.sr.transform.localScale = Vector3.one * scale;
    }

    void SpawnSpark(Vector3 at, Vector2 outward)
    {
        float a = Random.Range(-45f, 45f) * Mathf.Deg2Rad;
        var dir = new Vector2(outward.x * Mathf.Cos(a) - outward.y * Mathf.Sin(a),
                              outward.x * Mathf.Sin(a) + outward.y * Mathf.Cos(a));
        Spawn(RowSpark, at, (Vector3)(dir * Random.Range(1.4f, 2.1f) * hullSize), Random.Range(.18f, .28f),
              SparkSize * hullSize, SparkSize * hullSize * .6f, 0f, -1.5f * hullSize, 3f, Color.white, -1, true);
    }

    // The hit that just made the ship worse: hull chunks and dark scrap
    // spraying out of the spot it broke, with a crackle and sparks.
    void Burst(int newState)
    {
        DamageEmitter e;
        if (!ShipDamageTable.BurstAt(id, newState, out e)) return;
        Bursts++;
        int column = life != null && life.HullAnimator != null ? life.HullAnimator.Column : 0;
        int index = ShipDamageTable.Count(id, newState - 1);
        Vector3 at = EmitterWorld(index, column);
        Vector2 o = OutwardWorld(e);
        for (int k = 0; k < 8; k++)
        {
            float a = Random.Range(-70f, 70f) * Mathf.Deg2Rad;
            var dir = new Vector2(o.x * Mathf.Cos(a) - o.y * Mathf.Sin(a), o.x * Mathf.Sin(a) + o.y * Mathf.Cos(a));
            bool skin = k % 2 == 0;
            float size = ChunkSize * hullSize * Random.Range(.7f, 1.1f);
            Spawn(skin ? RowChunk : RowScrap, at, (Vector3)(dir * Random.Range(1.6f, 2.4f) * hullSize),
                  Random.Range(.32f, .45f), size, size * .8f, Random.Range(-720f, 720f), -2f * hullSize, 3.5f,
                  skin ? leakTint : Color.white, Random.Range(0, AtlasColumns), false);
        }
        for (int k = 0; k < 3; k++) SpawnSpark(at, o);
        // the new panel crackles at once
        for (int i = index; i < emitters.Length; i++)
            if (ShipDamageTable.Get(id, i).state == newState) emitters[i].next = 0f;
    }

    // drawing < 0: the row's flipbook plays over the particle's life.
    void Spawn(int row, Vector3 at, Vector3 vel, float lifeSeconds, float size0, float size1, float spin,
               float gravity, float drag, Color tint, int drawing, bool faceVelocity)
    {
        int slot = -1;
        float oldest = -1f;
        for (int i = 0; i < particles.Length; i++)
        {
            if (!particles[i].alive) { slot = i; break; }
            if (particles[i].age / particles[i].life > oldest) { oldest = particles[i].age / particles[i].life; slot = i; }
        }
        if (slot < 0) return;
        particles[slot] = new Particle
        {
            pos = new Vector3(at.x, at.y, transform.position.z - .01f), origin = at, vel = vel, age = 0f,
            life = Mathf.Max(.05f, lifeSeconds), size0 = size0, size1 = size1, angle = Random.Range(0f, 360f),
            spin = spin, gravity = gravity, drag = drag, row = row, alive = true, faceVelocity = faceVelocity,
        };
        var sr = particleRenderers[slot];
        sr.color = tint;
        // smoke and drips go behind the hull, sparks and debris in front
        int order = hull != null ? hull.sortingOrder : 0;
        sr.sortingOrder = row == RowSmoke || row == RowDrop ? order - 1 : order + 1;
        sr.sprite = Frame(row, drawing < 0 ? 0 : drawing);
        sr.enabled = true;
        particles[slot].angle = drawing >= 0 && !faceVelocity ? particles[slot].angle : 0f;
        fixedDrawing[slot] = drawing;
    }

    readonly int[] fixedDrawing = new int[MaxParticles];

    void StepParticles(float dt)
    {
        float local = 1f / Mathf.Max(1e-4f, Mathf.Abs(root.lossyScale.x));
        for (int i = 0; i < particles.Length; i++)
        {
            ref Particle p = ref particles[i];
            var sr = particleRenderers[i];
            if (!p.alive) { if (sr.enabled) sr.enabled = false; continue; }
            p.age += dt;
            if (p.age >= p.life) { p.alive = false; sr.enabled = false; continue; }
            p.vel.y += p.gravity * dt;
            p.vel *= Mathf.Max(0f, 1f - p.drag * dt);
            p.pos += p.vel * dt;
            // never further than MaxReach hull lengths from where it started
            Vector3 off = p.pos - p.origin;
            off.z = 0f;
            float reach = MaxReach * hullSize;
            if (off.sqrMagnitude > reach * reach) { p.pos = p.origin + off.normalized * reach; p.vel = Vector3.zero; }
            p.angle += p.spin * dt;

            float u = p.age / p.life;
            int d = fixedDrawing[i];
            if (d < 0)
            {
                d = Mathf.Min(AtlasColumns - 1, Mathf.FloorToInt(u * AtlasColumns));
                // a drop: round as it leaves, then stretching as it falls
                if (p.row == RowDrop) d = u < .15f ? 0 : p.vel.y < -.9f * hullSize ? 2 : 1;
            }
            sr.sprite = Frame(p.row, d);
            sr.enabled = true;
            var t = sr.transform;
            t.position = p.pos;
            float angle = p.angle;
            if (p.faceVelocity && p.vel.sqrMagnitude > 1e-6f)
                angle = Mathf.Atan2(p.vel.y, p.vel.x) * Mathf.Rad2Deg - 90f;   // drawn pointing +y
            t.rotation = Quaternion.Euler(0f, 0f, angle);
            t.localScale = Vector3.one * (Mathf.Lerp(p.size0, p.size1, u) * local);
            Duck(sr, p.pos, Mathf.Lerp(p.size0, p.size1, u) * .5f);
        }
    }

    // Hidden this frame if it would cover a heart or a ship element.
    void Duck(SpriteRenderer sr, Vector3 at, float radius)
    {
        if (!sr.enabled) return;
        for (int k = 0; k < avoid.Count; k++)
        {
            var b = avoid[k];
            if (Mathf.Abs(at.x - b.center.x) < b.extents.x + radius &&
                Mathf.Abs(at.y - b.center.y) < b.extents.y + radius)
            {
                sr.enabled = false;
                return;
            }
        }
    }
}
