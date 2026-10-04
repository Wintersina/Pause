using System.Collections.Generic;
using UnityEngine;

// Hearts that orbit a hull and shield it: the shared engine behind the
// player's lives (ShipLivesIndicator) and the elite ships' two hearts
// (EliteHearts).
//
// The hearts circle the hull slowly and continuously on tilted orbits drawn
// as 3D: a heart swinging round the back draws behind the hull (under it,
// smaller and dimmer), and comes round the front over it, a little larger.
// Each owner picks a flavour (ShipHeartStyles: crossing atom orbits, a
// halo, a comet's train, counter-rotating gyro rings, a swarm, the
// spinners' shield ring); every heart has its own plane, phase and way
// round, the planes slowly precess and nod, and now and then a heart throws
// a loop-de-loop or turns back. Hearts that drift too close ease apart, a
// one-orbit style keeps its hearts evenly spaced (or in a train).
//
// The orbit is measured off the hull (HullBounds). The player's version
// also speeds through the bottom (where the thumb is) and past the
// companion gun's resting spot, and keeps its hearts on screen; those are
// the virtual hooks below, which a subclass switches on or off.
//
// The hearts are shields: when the hull is hit (the owner reports where
// the hit came from), the best-placed heart darts to the impact point
// between the hull and the hit, flashes as a shield burst and crumbles into
// pixel shards. The hearts left re-space their orbits smoothly; a heal pops
// a heart back into orbit. All of it is pooled (built once with the
// hearts) and runs on scaled time, so it holds still while the world is
// frozen.
//
// Subclass hooks: RemainingHearts (how many are left), TakeImpact (the hit
// point the next lost heart shields against), HullBounds, HeartSprite,
// HeartTint, ClampRect, AvoidThumb, FindGun, ResolveStyle.
[DefaultExecutionOrder(50)]
public abstract class HeartOrbit : MonoBehaviour
{
    [Tooltip("Each heart's world size (diameter), at the orbit's middle depth.")]
    public float heartSize = 0.22f;

    [Tooltip("Hearts closer than this many heart sizes ease apart.")]
    public float personalSpace = 1.45f;

    // Depth: a heart at the front draws this much larger, at the back this
    // much smaller (and DimBack darker).
    public const float DepthScale = .14f, DimBack = .38f;
    // Furthest a heart tilts as it swings (degrees).
    public const float MaxLean = 12f;
    // The orbit's warp: up to this much faster through the thumb's side and
    // past the gun.
    // (Warped: the phase speeds up to 1 + BottomWarp through the bottom,
    // slows to 1 - BottomWarp over the top.)
    public const float BottomWarp = .45f, GunWarp = 1.4f;
    // The orbit reaches this many heart sizes past the hull (its half-size).
    public const float OrbitReach = 1.05f;
    // ...and this much wider (share) per heart over three.
    public const float CrowdGrow = .07f;
    // Easing apart may slow a heart to this share of its speed, never turn it.
    public const float MinSpeed = .35f;
    // A loop-de-loop flourish.
    public const float LoopSeconds = .95f, LoopRadius = .1f;
    // How quickly a turn-back swings round (units of direction a second).
    public const float TurnRate = 2.2f;

    // The shield: dart to the impact, flash, crack, then shards fall and fade.
    public const float DartSeconds = .14f, ShieldSeconds = .16f, BreakCrack = .1f, BreakFall = .6f;
    public const float BreakSeconds = DartSeconds + ShieldSeconds + BreakCrack + BreakFall;
    public const float PopSeconds = .28f;
    const int MaxBreaks = 3;
    const int ShardCols = 3, ShardRows = 2, Shards = ShardCols * ShardRows;

    // The thumb: movePlayer flies the ship ThumbBelow above the finger; its
    // pad covers ThumbRadius round that point, the rest of it everything
    // under.
    public const float ThumbBelow = 1f, ThumbRadius = .42f;

    public static bool UnderThumb(Vector2 offset, float half)
    {
        float cy = -ThumbBelow;
        if (offset.y + half < cy) return true;
        float dx = Mathf.Max(Mathf.Abs(offset.x) - half, 0f);
        float dy = Mathf.Max(0f, Mathf.Max((offset.y - half) - cy, cy - (offset.y + half)));
        return dx * dx + dy * dy < ThumbRadius * ThumbRadius;
    }

    protected Transform[] hearts;
    protected SpriteRenderer[] renderers;
    int lastShown = -1;
    float seed, clock;
    // Seed for the per-heart jitter (the player's ship id).
    protected int styleKey;
    protected HeartStyle style;
    protected OrbitStyle orbit;
    float baseScale;
    int frontOrder, backOrder;

    // Per heart (indexed like hearts; swapped together).
    float[] theta;       // radians round its orbit
    float[] dir;         // -1..1, eased towards dirTarget (a turn-back stalls through 0)
    float[] dirTarget;
    float[] rollBase;    // its plane's heading before precession/sway (degrees)
    float[] tiltOwn;     // its own tilt offset (degrees)
    float[] rollOwn;     // its own heading offset (degrees)
    float[] loopT;       // seconds into a loop-de-loop (-1: none)
    float[] flourishIn;  // seconds to its next flourish
    float[] popLeft;
    float[] depth;       // -1 back .. 1 front
    float[] shrink;
    float[] lean;
    float[] push;
    Vector2[] pos;       // where it drew last (world)
    Vector2[] tangent;   // its screen direction of travel last frame
    int[] slotOf;        // slot k among the shown hearts
    int[] aheadOf;       // one orbit: the heart it follows

    // The orbit's measure: centre offset from the ship and half-sizes.
    Vector2 centre, radii;
    float crowd;
    float remeasureIn;
    bool measured;
    UltimateGun gun;
    protected SpriteRenderer hull;
    float spinSign = 1f, lastHullAngle;
    bool haveHullAngle;


    // The shield / break-up pool.
    struct Break
    {
        public bool live;
        public float t;
        public Vector3 from;      // where the heart was when it darted
        public Vector2 shieldRel; // the impact point, relative to the ship
        public Vector3 at;        // where it cracked (world; the shards stay)
        public Vector2 drift;
        public float scale;
    }
    Break[] breaks;
    SpriteRenderer[] ghosts;
    SpriteRenderer[] bursts;
    SpriteRenderer[] shards;          // MaxBreaks * Shards
    Vector2[] shardVelocity;
    float[] shardSpin;
    static Sprite[] shardSprites;
    static Vector2[] shardHome;       // each shard's centre in the heart, unit heart size

    public HeartStyle Style { get { return style; } }
    public OrbitStyle OrbitParams { get { return orbit; } }
    public Transform[] Hearts { get { return hearts; } }
    public bool Orbiting { get { return true; } }
    // The orbit's half-sizes (before any squash at a screen edge).
    public Vector2 OrbitRadii { get { return radii; } }
    public float OrbitRadius { get { return Mathf.Max(radii.x, radii.y) * Mathf.Max(1f, crowd); } }
    // 1 in full view .. lower while round the back of the hull, per heart.
    public float Shrink(int i) { return shrink != null && i >= 0 && i < shrink.Length ? shrink[i] : 1f; }
    // -1 at the back of its orbit .. 1 at the front.
    public float Depth(int i) { return depth != null && i >= 0 && i < depth.Length ? depth[i] : 0f; }
    public bool InFront(int i) { return renderers != null && i >= 0 && i < renderers.Length && renderers[i] != null && renderers[i].sortingOrder == frontOrder; }
    public int FrontOrder { get { return frontOrder; } }
    public int BackOrder { get { return backOrder; } }
    // A heart's phase round its orbit (radians, before the warp).
    public float Theta(int i) { return theta != null && i >= 0 && i < theta.Length ? theta[i] : 0f; }
    public bool Looping(int i) { return loopT != null && i >= 0 && i < loopT.Length && loopT[i] >= 0f; }
    // Flourishes thrown since the hearts were built.
    public int Loops { get; private set; }
    public int TurnBacks { get; private set; }
    public float Direction(int i) { return dir != null && i >= 0 && i < dir.Length ? dir[i] : 0f; }

    public int ActiveBreaks
    {
        get
        {
            int n = 0;
            if (breaks != null) foreach (var b in breaks) if (b.live) n++;
            return n;
        }
    }
    // Where break slot b's heart is shielding (world), and how far in it is.
    public Vector3 ShieldPoint(int b) { return breaks != null && b >= 0 && b < breaks.Length ? transform.position + (Vector3)breaks[b].shieldRel : transform.position; }
    public float BreakTime(int b) { return breaks != null && b >= 0 && b < breaks.Length && breaks[b].live ? breaks[b].t : -1f; }

    protected virtual void Start()
    {
        seed = Random.value * 10f;
        BuildHearts();
    }

    // ---- hooks ---------------------------------------------------------------

    // Builds the owner's own number of hearts.
    public abstract void BuildHearts();
    // How many hearts are left right now.
    protected abstract int RemainingHearts();
    // The hit the next lost heart shields against, if one was reported
    // (and forgets it).
    protected abstract bool TakeImpact(out Vector3 at);
    // The heart drawing (HeartTint multiplies it).
    protected abstract Sprite HeartSprite();
    // The orbit flavour and its numbers.
    protected abstract void ResolveStyle(out HeartStyle style, out OrbitStyle orbit);
    // The hull the orbit goes round (world space).
    protected abstract Bounds HullBounds();
    // Multiplies every heart, ghost and shard.
    protected virtual Color HeartTint { get { return Color.white; } }
    // Hearts are kept inside this world rect.
    protected virtual Rect ClampRect() { return Rect.MinMaxRect(-1e5f, -1e5f, 1e5f, 1e5f); }
    // Keep under-hull hearts off the thumb's pad (the player only).
    protected virtual bool AvoidThumb { get { return false; } }
    // The companion gun the orbit hurries past (the player only).
    protected virtual UltimateGun FindGun() { return null; }
    protected virtual Bounds GunRestBounds(UltimateGun g) { return default(Bounds); }
    protected virtual int MaxHearts { get { return 8; } }
    // The hull drawing the hearts sort round (front over it, back under it).
    protected virtual SpriteRenderer HullRenderer() { return GetComponent<SpriteRenderer>(); }

    public void BuildHearts(int count)
    {
        hull = HullRenderer();
        var sprite = HeartSprite();
        if (sprite == null) { Destroy(this); return; }
        ResolveStyle(out style, out orbit);

        DestroyBuilt();
        lastShown = -1;
        count = Mathf.Clamp(count, 1, MaxHearts);
        hearts = new Transform[count];
        renderers = new SpriteRenderer[count];
        theta = new float[count];
        dir = new float[count];
        dirTarget = new float[count];
        rollBase = new float[count];
        tiltOwn = new float[count];
        rollOwn = new float[count];
        loopT = new float[count];
        flourishIn = new float[count];
        popLeft = new float[count];
        depth = new float[count];
        shrink = new float[count];
        lean = new float[count];
        push = new float[count];
        pos = new Vector2[count];
        tangent = new Vector2[count];
        slotOf = new int[count];
        aheadOf = new int[count];
        haveHullAngle = false;
        float parentScale = Mathf.Max(Mathf.Abs(transform.lossyScale.x), Mathf.Abs(transform.lossyScale.y), 0.0001f);
        float localSize = heartSize / parentScale;
        int hullOrder = hull != null ? hull.sortingOrder : 0;
        frontOrder = hullOrder + 2;   // over the hull, its damage FX and the gun
        backOrder = hullOrder - 1;    // under the hull (with its exhaust)
        baseScale = localSize / Mathf.Max(sprite.bounds.size.x, sprite.bounds.size.y);

        int key = styleKey * 131 + Mathf.FloorToInt(seed * 977f);
        for (int i = 0; i < count; i++)
        {
            var go = new GameObject("Heart" + i);
            go.transform.SetParent(transform, false);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.color = HeartTint;
            sr.sortingOrder = frontOrder;
            go.transform.localScale = Vector3.one * baseScale;
            hearts[i] = go.transform;
            renderers[i] = sr;
            tiltOwn[i] = (Hash01(key + i * 17) * 2f - 1f) * orbit.tiltJitter;
            rollOwn[i] = (Hash01(key + i * 17 + 5) * 2f - 1f) * orbit.rollJitter;
            float way = orbit.alternate && i % 2 == 1 ? -1f : 1f;
            dir[i] = dirTarget[i] = way;
            loopT[i] = -1f;
            flourishIn[i] = orbit.flourishEvery > 0f ? orbit.flourishEvery * (.4f + 1.2f * Hash01(key + i * 17 + 9)) : -1f;
            shrink[i] = 1f;
            slotOf[i] = i;
            rollBase[i] = SlotRoll(i, count);
            theta[i] = SlotTheta(i, count, 0f, way);
        }

        measured = false;
        BuildBreakPool(sprite, frontOrder + 1);
        Place(0f);
    }

    void DestroyBuilt()
    {
        foreach (Transform child in transform)
            if (child.name.StartsWith("Heart") || child.name == "~HeartBreaks") pendingDestroy.Add(child.gameObject);
        foreach (var go in pendingDestroy)
        {
            if (Application.isPlaying) Destroy(go);
            else DestroyImmediate(go);
        }
        pendingDestroy.Clear();
        breaks = null;
    }
    static readonly List<GameObject> pendingDestroy = new List<GameObject>();

    // A shown heart's slot k of n: its plane's heading (crossing styles fan
    // evenly over `spread`), and where it starts round the orbit.
    float SlotRoll(int k, int n)
    {
        return orbit.spread * k / Mathf.Max(1, n);
    }

    float SlotTheta(int k, int n, float lead, float way)
    {
        if (orbit.gap > 0f) return lead - way * orbit.gap * k;     // a train
        // crossing planes: heading + phase spread evenly round the ship (a
        // heart on a plane turned by r, at phase u, sits about r + u round)
        return lead + 2f * Mathf.PI * k / Mathf.Max(1, n) - way * SlotRoll(k, n) * Mathf.Deg2Rad;
    }

    static float Hash01(int n)
    {
        unchecked
        {
            uint x = (uint)n * 747796405u + 2891336453u;
            x = ((x >> (int)((x >> 28) + 4u)) ^ x) * 277803737u;
            return ((x >> 22) ^ x) / 4294967296f;
        }
    }

    // ---- the shield / break-up pool -----------------------------------------

    static void EnsureShardSprites(Sprite heart)
    {
        if (shardSprites != null && shardSprites.Length == Shards && shardSprites[0] != null) return;
        shardSprites = new Sprite[Shards];
        shardHome = new Vector2[Shards];
        Rect r = heart.textureRect;
        float w = r.width / ShardCols, h = r.height / ShardRows;
        float longest = Mathf.Max(r.width, r.height);
        for (int row = 0; row < ShardRows; row++)
            for (int col = 0; col < ShardCols; col++)
            {
                int i = row * ShardCols + col;
                var sub = new Rect(r.x + col * w, r.y + row * h, w, h);
                shardSprites[i] = Sprite.Create(heart.texture, sub, new Vector2(.5f, .5f), heart.pixelsPerUnit, 0, SpriteMeshType.FullRect);
                shardSprites[i].name = "lifeHeartShard" + i;
                shardHome[i] = new Vector2((col + .5f) * w - r.width * .5f, (row + .5f) * h - r.height * .5f) / longest;
            }
    }

    void BuildBreakPool(Sprite heart, int order)
    {
        EnsureShardSprites(heart);
        var root = new GameObject("~HeartBreaks").transform;
        root.SetParent(transform, false);
        breaks = new Break[MaxBreaks];
        ghosts = new SpriteRenderer[MaxBreaks];
        bursts = new SpriteRenderer[MaxBreaks];
        shards = new SpriteRenderer[MaxBreaks * Shards];
        shardVelocity = new Vector2[MaxBreaks * Shards];
        shardSpin = new float[MaxBreaks * Shards];
        var burstFrames = ShieldArt.Impact;
        for (int b = 0; b < MaxBreaks; b++)
        {
            bursts[b] = NewPiece(root, "Burst" + b, burstFrames[1], order);
            ghosts[b] = NewPiece(root, "Ghost" + b, heart, order + 1);
            for (int s = 0; s < Shards; s++)
                shards[b * Shards + s] = NewPiece(root, "Shard" + b + "_" + s, shardSprites[s], order + 1);
        }
    }

    static SpriteRenderer NewPiece(Transform root, string name, Sprite sprite, int order)
    {
        var go = new GameObject(name);
        go.transform.SetParent(root, false);
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = sprite;
        sr.sortingOrder = order;
        sr.enabled = false;
        return sr;
    }

    // Heart i was lost shielding the hull from a hit at `impact`: it darts
    // there from where it is drawn now.
    void StartShield(int i, Vector3 impact)
    {
        if (breaks == null || hearts[i] == null) return;
        int slot = 0;
        for (int b = 0; b < breaks.Length; b++)
        {
            if (!breaks[b].live) { slot = b; break; }
            if (breaks[b].t > breaks[slot].t) slot = b;   // all busy: reuse the oldest
        }
        Vector3 p = transform.position;
        Vector2 rel = ShieldOffset(impact);
        Vector2 dirOut = rel.sqrMagnitude > 1e-6f ? rel.normalized : Vector2.down;
        float lossy = Mathf.Max(Mathf.Abs(transform.lossyScale.x), 0.0001f);
        breaks[slot] = new Break
        {
            live = true, t = 0f, from = hearts[i].position, shieldRel = rel,
            at = p + (Vector3)rel, drift = dirOut * .3f + Vector2.down * .08f,
            scale = baseScale * lossy,
        };
        int key = slot * 7919 + i * 31 + Mathf.FloorToInt(clock * 60f);
        for (int s = 0; s < Shards; s++)
        {
            Vector2 out0 = shardHome[s].sqrMagnitude > 1e-6f ? shardHome[s].normalized : Vector2.up;
            float speed = .45f + Hash01(key + s * 13) * .5f;
            shardVelocity[slot * Shards + s] = out0 * speed + dirOut * .35f + Vector2.up * .3f;
            shardSpin[slot * Shards + s] = (Hash01(key + s * 13 + 7) * 2f - 1f) * 540f;
        }
    }

    // The point between the hull and the hit where a heart blocks it,
    // relative to the ship: just off the hull's edge towards the hit (no
    // further out than the hit itself), kept on screen.
    public Vector2 ShieldOffset(Vector3 impact)
    {
        if (!measured) Measure();
        Vector3 p = transform.position;
        Vector2 c = (Vector2)p + centre;
        Vector2 to = (Vector2)impact - c;
        float dist = to.magnitude;
        Vector2 d = dist > 1e-4f ? to / dist : Vector2.down;
        // the hull's edge along d (its bounds as an ellipse)
        Vector2 ext = hullExtents;
        float edge = 1f / Mathf.Sqrt(d.x * d.x / Mathf.Max(1e-4f, ext.x * ext.x) + d.y * d.y / Mathf.Max(1e-4f, ext.y * ext.y));
        float reach = Mathf.Min(edge + heartSize * .45f, Mathf.Max(dist, edge * .8f));
        Vector2 at = c + d * reach;
        Rect screen = ClampRect();
        float half = HalfBox;
        at.x = Mathf.Clamp(at.x, screen.xMin + half, Mathf.Max(screen.xMin + half, screen.xMax - half));
        at.y = Mathf.Clamp(at.y, screen.yMin + half, Mathf.Max(screen.yMin + half, screen.yMax - half));
        return at - (Vector2)p;
    }

    // Advances every shield / break-up by dt seconds of gameplay time.
    public void StepBreaks(float dt)
    {
        if (breaks == null) return;
        dt = Mathf.Max(0f, dt);
        Vector3 p = transform.position;
        var burstFrames = ShieldArt.Impact;
        for (int b = 0; b < breaks.Length; b++)
        {
            var br = breaks[b];
            var ghost = ghosts[b];
            var burst = bursts[b];
            if (!br.live)
            {
                if (ghost.enabled) ghost.enabled = false;
                if (burst.enabled) burst.enabled = false;
                for (int s = 0; s < Shards; s++) if (shards[b * Shards + s].enabled) shards[b * Shards + s].enabled = false;
                continue;
            }
            br.t += dt;
            float t = br.t;
            Transform root = ghost.transform.parent;
            float inv = 1f / Mathf.Max(.0001f, Mathf.Abs(root.lossyScale.x));
            Vector3 shieldAt = p + (Vector3)br.shieldRel;
            shieldAt.z = p.z - .12f;
            if (t < DartSeconds + ShieldSeconds)
            {
                ghost.enabled = true;
                for (int s = 0; s < Shards; s++) if (shards[b * Shards + s].enabled) shards[b * Shards + s].enabled = false;
                if (t < DartSeconds)
                {
                    // darts to the impact point, swelling as it goes
                    float k = t / DartSeconds;
                    float e = 1f - (1f - k) * (1f - k) * (1f - k);
                    Vector3 at = Vector3.Lerp(br.from, shieldAt, e);
                    at.z = shieldAt.z;
                    ghost.transform.position = at;
                    ghost.transform.localScale = Vector3.one * br.scale * (1f + .35f * e) * inv;
                    ghost.transform.rotation = Quaternion.identity;
                    ghost.color = HeartTint;
                    burst.enabled = false;
                }
                else
                {
                    // blocks it: a shield burst round it and a quiver
                    float k = (t - DartSeconds) / ShieldSeconds;
                    float shake = Mathf.Sin(t * 140f) * .012f * (1f - k);
                    ghost.transform.position = shieldAt + new Vector3(shake, 0f, 0f);
                    ghost.transform.localScale = Vector3.one * br.scale * (1.35f + .2f * Mathf.Sin(k * Mathf.PI)) * inv;
                    float flash = Mathf.Sin(k * Mathf.PI);
                    Color gt = HeartTint;
                    ghost.color = new Color(gt.r, gt.g * (1f - .4f * flash), gt.b * (1f - .25f * flash), gt.a);
                    burst.enabled = true;
                    int frame = Mathf.Clamp((int)(k * 3f), 0, 2);
                    if (burst.sprite != burstFrames[frame]) burst.sprite = burstFrames[frame];
                    float bs = heartSize * (1.7f + 1.1f * k) / Mathf.Max(.0001f, BurstSpan(burst.sprite));
                    burst.transform.position = shieldAt + new Vector3(0f, 0f, .01f);
                    burst.transform.localScale = Vector3.one * bs * inv;
                    burst.color = new Color(1f, .82f, .92f, 1f - .5f * k);
                }
                br.at = ghost.transform.position;
            }
            else if (t < BreakSeconds)
            {
                ghost.enabled = false;
                burst.enabled = false;
                float t2 = t - DartSeconds - ShieldSeconds;
                float crack = Mathf.Clamp01(t2 / BreakCrack);
                float fall = Mathf.Max(0f, t2 - BreakCrack);
                float alpha = 1f - Mathf.Clamp01(fall / BreakFall);
                float size = br.scale * 1.2f;
                Vector3 drift = (Vector3)(br.drift * Mathf.Min(t2, BreakCrack));
                for (int s = 0; s < Shards; s++)
                {
                    int i = b * Shards + s;
                    var sr = shards[i];
                    sr.enabled = true;
                    // crack: the pieces part along their seams, then tumble
                    Vector3 home = (Vector3)(shardHome[s] * size * (1f + .25f * crack));
                    Vector2 v = shardVelocity[i];
                    Vector3 move = new Vector3(v.x * fall, v.y * fall - 2.4f * fall * fall, 0f);
                    sr.transform.position = br.at + drift + home * HeartWorldSpan() + move;
                    sr.transform.rotation = Quaternion.Euler(0f, 0f, shardSpin[i] * fall);
                    sr.transform.localScale = Vector3.one * size * (1f - .35f * Mathf.Clamp01(fall / BreakFall)) * inv;
                    Color st = HeartTint;
                    sr.color = new Color(st.r, st.g, st.b, alpha * st.a);
                }
            }
            else
            {
                br.live = false;
                ghost.enabled = false;
                burst.enabled = false;
                for (int s = 0; s < Shards; s++) shards[b * Shards + s].enabled = false;
            }
            breaks[b] = br;
        }
    }

    static float BurstSpan(Sprite s)
    {
        return s != null ? Mathf.Max(s.bounds.size.x, s.bounds.size.y) : 1f;
    }

    // The heart sprite's longest edge in world units at scale 1.
    float HeartWorldSpan()
    {
        var sprite = renderers != null && renderers.Length > 0 && renderers[0] != null ? renderers[0].sprite : null;
        return sprite != null ? Mathf.Max(sprite.bounds.size.x, sprite.bounds.size.y) : 1f;
    }

    // ---- what the hearts give other effects ------------------------------

    public int ShownCount
    {
        get
        {
            int n = 0;
            if (hearts != null) foreach (var h in hearts) if (h != null && h.gameObject.activeSelf) n++;
            return n;
        }
    }

    // World position of heart i (whether shown or not).
    public Vector3 HeartPosition(int i)
    {
        return hearts != null && i >= 0 && i < hearts.Length && hearts[i] != null ? hearts[i].position : transform.position;
    }

    // Adds the world position of every heart shown right now; returns how
    // many. No allocation (fill a list you keep).
    public int HeartPositions(List<Vector3> into)
    {
        int n = 0;
        if (hearts == null || into == null) return 0;
        for (int i = 0; i < hearts.Length; i++)
        {
            if (hearts[i] == null || !hearts[i].gameObject.activeSelf) continue;
            into.Add(hearts[i].position);
            n++;
        }
        return n;
    }

    // Where the hearts shown right now draw (world space): anything that
    // flies off the hull keeps clear of them (ShipDamageFx).
    public void HeartBounds(List<Bounds> into)
    {
        if (renderers == null) return;
        foreach (var r in renderers)
            if (r != null && r.enabled && r.gameObject.activeInHierarchy && r.sprite != null) into.Add(r.bounds);
    }

    // ---- lives -------------------------------------------------------------

    protected virtual void Update()
    {
        if (hearts == null) return;

        int remaining = Mathf.Clamp(RemainingHearts(), 0, hearts.Length);
        if (remaining != lastShown)
        {
            if (lastShown >= 0 && remaining < lastShown)
            {
                // Each heart lost shields the hull: the best-placed shown one
                // takes the lost one's place in the list and darts off.
                Vector3 impact;
                if (!TakeImpact(out impact)) impact = DefaultImpact();
                for (int lost = lastShown - 1; lost >= remaining; lost--)
                {
                    if (hearts[lost] == null || !hearts[lost].gameObject.activeSelf) continue;
                    int best = BestShield(impact, lost);
                    if (best != lost) Swap(best, lost);
                    StartShield(lost, impact);
                }
            }
            for (int i = 0; i < hearts.Length; i++)
            {
                if (hearts[i] == null) continue;
                bool show = i < remaining;
                bool was = hearts[i].gameObject.activeSelf;
                if (!was && show && lastShown >= 0) Healed(i, remaining);
                hearts[i].gameObject.SetActive(show);
            }
            lastShown = remaining;
        }
        else { Vector3 unused; TakeImpact(out unused); }
    }

    // No hit point given: a hit from straight ahead.
    Vector3 DefaultImpact() { return transform.position + Vector3.up; }

    // The shown heart (index <= upTo) nearest where it would block `impact`,
    // favouring one round the front.
    int BestShield(Vector3 impact, int upTo)
    {
        Vector2 target = (Vector2)transform.position + ShieldOffset(impact);
        int best = upTo;
        float bestScore = float.MaxValue;
        for (int i = 0; i <= upTo; i++)
        {
            if (hearts[i] == null || !hearts[i].gameObject.activeSelf) continue;
            float score = ((Vector2)hearts[i].position - target).magnitude + (depth[i] < 0f ? .25f * -depth[i] : 0f);
            if (score < bestScore) { bestScore = score; best = i; }
        }
        return best;
    }

    void Swap(int a, int b)
    {
        Sw(hearts, a, b); Sw(renderers, a, b); Sw(theta, a, b); Sw(dir, a, b); Sw(dirTarget, a, b);
        Sw(rollBase, a, b); Sw(tiltOwn, a, b); Sw(rollOwn, a, b); Sw(loopT, a, b); Sw(flourishIn, a, b);
        Sw(popLeft, a, b); Sw(depth, a, b); Sw(shrink, a, b); Sw(lean, a, b); Sw(push, a, b);
        Sw(pos, a, b); Sw(tangent, a, b);
    }
    static void Sw<T>(T[] arr, int a, int b) { T t = arr[a]; arr[a] = arr[b]; arr[b] = t; }

    // Heart i comes back: it pops in at a free spot on its orbit.
    void Healed(int i, int shownAfter)
    {
        popLeft[i] = PopSeconds;
        loopT[i] = -1f;
        rollBase[i] = SlotRoll(i, shownAfter);
        int lead = -1;
        for (int j = 0; j < hearts.Length; j++)
            if (j != i && hearts[j] != null && hearts[j].gameObject.activeSelf) { lead = j; break; }
        if (lead < 0) { theta[i] = Mathf.PI * .5f; return; }
        float way = orbit.alternate && (i % 2) != (lead % 2) ? -dirTarget[lead] : dirTarget[lead];
        dir[i] = dirTarget[i] = way;
        if (orbit.spread != 0f) { theta[i] = theta[lead] + Mathf.PI * .9f; return; }
        // one orbit: into the widest gap (a ring), or onto the end of the train
        float bestGap = -1f, at = theta[lead];
        for (int j = 0; j < hearts.Length; j++)
        {
            if (j == i || hearts[j] == null || !hearts[j].gameObject.activeSelf) continue;
            float behind = BehindGap(j, i, way);
            if (behind > bestGap)
            {
                bestGap = behind;
                at = orbit.gap > 0f ? theta[j] - way * orbit.gap : theta[j] - way * behind * .5f;
            }
        }
        theta[i] = at;
    }

    // How far round (radians, against the way round) from heart j to the
    // nearest shown heart behind it (other than `skip`): 2 pi if none.
    float BehindGap(int j, int skip, float way)
    {
        float best = 2f * Mathf.PI;
        for (int q = 0; q < hearts.Length; q++)
        {
            if (q == j || q == skip || hearts[q] == null || !hearts[q].gameObject.activeSelf) continue;
            float d = Mathf.Repeat((theta[j] - theta[q]) * way, 2f * Mathf.PI);
            if (d > 1e-4f && d < best) best = d;
        }
        return best;
    }

    protected virtual void LateUpdate()
    {
        Place(Time.unscaledDeltaTime, Time.deltaTime);
        StepBreaks(Time.deltaTime);
    }

    // A healed heart pops in: grows from nothing past full size and settles.
    static float PopScale(float left)
    {
        if (left <= 0f) return 1f;
        float x = 1f - left / PopSeconds;
        const float c = 2.2f;
        float y = x - 1f;
        return Mathf.Max(0f, 1f + (c + 1f) * y * y * y + c * y * y);
    }

    // ---- the orbit -----------------------------------------------------------

    Vector2 hullExtents;

    // Half the box a heart can draw in (largest depth, leaning).
    float HalfBox { get { return heartSize * .5f * (1f + DepthScale) * 1.2f; } }

    // Re-measures the orbit round the hull's sprite (and finds the gun).
    public void Measure()
    {
        measured = true;
        remeasureIn = 1f;
        if (hull == null) hull = HullRenderer();
        Vector3 p = transform.position;
        Bounds hb = HullBounds();
        centre = (Vector2)(hb.center - p);
        hullExtents = hb.extents;
        float ex = hb.extents.x, ey = hb.extents.y;
        // a skinny hull still gets a roundish orbit
        float rx = Mathf.Max(ex, ey * .8f) * orbit.radius + heartSize * OrbitReach;
        float ry = Mathf.Max(ey, ex * .8f) * orbit.radius + heartSize * OrbitReach;
        radii = new Vector2(rx, ry);
        gun = FindGun();
    }

    // Heart i's spot on its unit orbit at angle th: screen-plane unit
    // offset (before the radii) and depth (-1 back .. 1 front).
    void Project(int i, float th, float tiltDeg, float rollDeg, out Vector2 unit, out float z)
    {
        float a = tiltDeg * Mathf.Deg2Rad, r = rollDeg * Mathf.Deg2Rad;
        float s = Mathf.Sin(th), c = Mathf.Cos(th);
        float x = c, y = s * Mathf.Cos(a);
        z = s * Mathf.Sin(a);
        float cr = Mathf.Cos(r), sr = Mathf.Sin(r);
        unit = new Vector2(x * cr - y * sr, x * sr + y * cr);
    }

    // The orbit's half-sizes this frame, each side squashed to stay on screen.
    float rxL, rxR, ryD, ryU;

    Vector2 Scale(Vector2 u)
    {
        return new Vector2(u.x * (u.x > 0f ? rxR : rxL), u.y * (u.y > 0f ? ryU : ryD));
    }

    float Tilt(int i)
    {
        float own = orbit.spread != 0f ? i * 1.7f : 0f;   // one orbit nods as one
        return Mathf.Clamp(orbit.tilt + tiltOwn[i] + orbit.tiltWobble * Mathf.Sin((clock + seed) * .41f + own), 0f, 85f);
    }

    float Roll(int i)
    {
        float own = orbit.spread != 0f ? i * .9f : 0f;
        return rollBase[i] + rollOwn[i] + orbit.precession * clock + orbit.sway * Mathf.Sin((clock + seed) * .23f + own);
    }

    // Where round its orbit a heart at phase u draws: unevenly, quick
    // through the orbit's lowest point (where the thumb is) and lingering
    // over the top. (The bottom of a plane tilted `tiltDeg`, headed
    // `rollDeg`: where its projected height is least.)
    public static float Warped(float u, float tiltDeg, float rollDeg)
    {
        float r = rollDeg * Mathf.Deg2Rad;
        float A = Mathf.Sin(r), B = Mathf.Cos(tiltDeg * Mathf.Deg2Rad) * Mathf.Cos(r);
        float bottom = Mathf.Atan2(-B, -A);
        return u + BottomWarp * Mathf.Sin(u - bottom);
    }

    // Lays the hearts out for where the ship is now. `unscaledDt` drives the
    // periodic re-measure; `scaledDt` (gameplay time) all motion, so the
    // hearts hold still while the world is frozen.
    public void Place(float unscaledDt, float scaledDt = 0f)
    {
        if (hearts == null) return;
        float dt = Mathf.Max(0f, scaledDt);
        clock += dt;
        for (int i = 0; i < popLeft.Length; i++) if (popLeft[i] > 0f) popLeft[i] = Mathf.Max(0f, popLeft[i] - dt);
        remeasureIn -= unscaledDt;
        if (!measured || remeasureIn <= 0f) Measure();

        Vector3 p = transform.position;
        Vector2 c = (Vector2)p + centre;
        Rect screen = ClampRect();
        float half = HalfBox;
        // A spinner's ring turns the way its hull does.
        if (style == HeartStyle.ShieldRing)
        {
            float hullAngle = transform.eulerAngles.z;
            if (haveHullAngle)
            {
                float d = Mathf.DeltaAngle(lastHullAngle, hullAngle);
                if (Mathf.Abs(d) > .01f) spinSign = Mathf.Sign(d);
            }
            lastHullAngle = hullAngle;
            haveHullAngle = true;
        }

        // The gun's resting spot (where it hovers most of the time).
        bool hasGun = gun != null && gun.isActiveAndEnabled;
        Bounds gunBox = hasGun ? GunRestBounds(gun) : default(Bounds);

        // Slots among the hearts shown, and ease apart any too close.
        int n = 0;
        for (int i = 0; i < hearts.Length; i++)
        {
            push[i] = 0f;
            if (hearts[i] == null || !hearts[i].gameObject.activeSelf) continue;
            slotOf[i] = n++;
        }
        if (n == 0) return;

        // A crowd of hearts flies a little wider (eased as hearts come and go).
        float wantCrowd = 1f + CrowdGrow * Mathf.Max(0, n - 3);
        crowd = crowd <= 0f ? wantCrowd : Mathf.Lerp(crowd, wantCrowd, 1f - Mathf.Exp(-2f * dt));
        Vector2 r = radii * crowd;
        rxR = Mathf.Clamp(screen.xMax - half - c.x, .02f, r.x);
        rxL = Mathf.Clamp(c.x - (screen.xMin + half), .02f, r.x);
        ryU = Mathf.Clamp(screen.yMax - half - c.y, .02f, r.y);
        // under the hull, but never down onto the thumb's pad
        float overThumb = AvoidThumb ? c.y - (p.y - ThumbBelow + ThumbRadius + half + .01f) : float.MaxValue;
        ryD = Mathf.Clamp(Mathf.Min(c.y - (screen.yMin + half), overThumb), .02f, r.y * (AvoidThumb ? .92f : 1f));

        if (dt > 0f) Spacing(n);

        // One orbit: how near the gun its nearest heart is (last frame).
        float ringNear = 0f;
        if (hasGun && orbit.spread == 0f)
            for (int i = 0; i < hearts.Length; i++)
                if (hearts[i] != null && hearts[i].gameObject.activeSelf) ringNear = Mathf.Max(ringNear, Near(pos[i], gunBox));

        float rollEase = 1f - Mathf.Exp(-1.6f * dt);
        for (int i = 0; i < hearts.Length; i++)
        {
            if (hearts[i] == null || !hearts[i].gameObject.activeSelf) continue;
            int k = slotOf[i];
            rollBase[i] = Mathf.LerpAngle(rollBase[i], SlotRoll(k, n), rollEase);
            if (style == HeartStyle.ShieldRing) dirTarget[i] = spinSign;
            dir[i] = Mathf.MoveTowards(dir[i], dirTarget[i], TurnRate * dt);

            float tilt = Tilt(i), roll = Roll(i);
            Vector2 unit; float z;
            Project(i, Warped(theta[i], tilt, roll), tilt, roll, out unit, out z);
            Vector2 at = c + Scale(unit);

            // quick past the gun's resting spot (the warp already hurries
            // it through the bottom, past the thumb)
            float yOff = at.y - p.y;
            float near = hasGun ? Near(at, gunBox) : 0f;
            // (a one-orbit style's hearts fly as one: the whole ring hurries)
            float m = 1f + GunWarp * (orbit.spread != 0f ? near : ringNear);
            float way = Mathf.Abs(dir[i]) > .05f ? Mathf.Sign(dir[i]) : Mathf.Sign(dirTarget[i]);
            // (a one-orbit style's train or ring keeps moving through a loop)
            float loopSlow = loopT[i] >= 0f && orbit.spread != 0f ? .45f : 1f;
            // (nobody hangs back on the gun)
            float v = orbit.speed * m * dir[i] * loopSlow + way * (near > 0f ? Mathf.Max(0f, push[i]) : push[i]);
            // easing apart never turns a heart round
            if (Mathf.Abs(dir[i]) > .5f && v * way < MinSpeed * orbit.speed) v = way * MinSpeed * orbit.speed;
            theta[i] = Mathf.Repeat(theta[i] + v * dt, 2f * Mathf.PI);

            // flourishes: a loop-de-loop or a turn back, only up top, clear of the gun
            if (dt > 0f && orbit.flourishEvery > 0f && loopT[i] < 0f)
            {
                flourishIn[i] -= dt;
                if (flourishIn[i] <= 0f && yOff > -.05f && (!hasGun || GunGap(at, gunBox) > heartSize) && Mathf.Abs(dir[i] - dirTarget[i]) < .01f)
                {
                    int key = i * 101 + Mathf.FloorToInt((clock + seed) * 7f);
                    // a turn-back stalls a moment: only where it can't drift onto the gun
                    bool away = !hasGun || GunGap(at, gunBox) > heartSize * 1.2f;
                    if (Hash01(key) < orbit.turnBackChance && away) { dirTarget[i] = -dirTarget[i]; TurnBacks++; }
                    else { loopT[i] = 0f; Loops++; }
                    flourishIn[i] = orbit.flourishEvery * (.6f + .8f * Hash01(key + 3));
                }
            }

            Project(i, Warped(theta[i], tilt, roll), tilt, roll, out unit, out z);
            at = c + Scale(unit);
            // the screen direction it's travelling
            Vector2 unit2; float z2;
            Project(i, Warped(theta[i] + .02f * way, tilt, roll), tilt, roll, out unit2, out z2);
            Vector2 tan = Scale(unit2) - Scale(unit);
            tangent[i] = tan.sqrMagnitude > 1e-10f ? tan.normalized : Vector2.right;

            if (loopT[i] >= 0f)
            {
                loopT[i] += dt;
                float u = Mathf.Clamp01(loopT[i] / LoopSeconds);
                float psi = 2f * Mathf.PI * u * u * (3f - 2f * u);
                Vector2 outward = at - c;
                outward = outward.sqrMagnitude > 1e-8f ? outward.normalized : Vector2.up;
                // forward, out over the top, back and down into the orbit again
                at += LoopRadius * (Mathf.Sin(psi) * tangent[i] + (1f - Mathf.Cos(psi)) * outward);
                if (loopT[i] >= LoopSeconds) loopT[i] = -1f;
            }

            at.x = Mathf.Clamp(at.x, screen.xMin + half, Mathf.Max(screen.xMin + half, screen.xMax - half));
            at.y = Mathf.Clamp(at.y, screen.yMin + half, Mathf.Max(screen.yMin + half, screen.yMax - half));

            // swinging: lean into the turn
            if (dt > 0f && pos[i] != Vector2.zero)
            {
                float vx = (at.x - pos[i].x) / dt;
                float target = Mathf.Clamp(-vx * 14f, -MaxLean, MaxLean);
                lean[i] = Mathf.Lerp(lean[i], target, 1f - Mathf.Exp(-8f * dt));
            }
            pos[i] = at;
            depth[i] = z;

            bool front = z >= 0f;
            float depthScale = 1f + DepthScale * z;
            hearts[i].position = new Vector3(at.x, at.y, p.z - .1f - .01f * z);
            hearts[i].rotation = Quaternion.Euler(0f, 0f, lean[i]);
            hearts[i].localScale = Vector3.one * (baseScale * depthScale * PopScale(popLeft[i]));
            shrink[i] = front ? 1f : 1f - .65f * -z;
            var sr = renderers[i];
            if (sr != null)
            {
                if (!sr.enabled) sr.enabled = true;
                int order = front ? frontOrder : backOrder;
                if (sr.sortingOrder != order) sr.sortingOrder = order;
                float lit = front ? 1f : 1f - DimBack * -z;
                Color tint = HeartTint;
                sr.color = new Color(lit * tint.r, lit * tint.g, lit * tint.b, tint.a);
            }
        }
    }

    // 1 on the gun's resting spot .. 0 clear of it by most of a heart.
    float Near(Vector2 at, Bounds gunBox)
    {
        return 1f - Mathf.Clamp01(GunGap(at, gunBox) / (heartSize * .8f));
    }

    // How far `at` is from the gun's resting spot (0 on it).
    static float GunGap(Vector2 at, Bounds gunBox)
    {
        float gx = Mathf.Max(Mathf.Abs(at.x - gunBox.center.x) - gunBox.extents.x, 0f);
        float gy = Mathf.Max(Mathf.Abs(at.y - gunBox.center.y) - gunBox.extents.y, 0f);
        return Mathf.Sqrt(gx * gx + gy * gy);
    }

    // Hearts that drift too close ease apart along their orbits; a
    // one-orbit style also keeps its hearts evenly round it (or in a train).
    void Spacing(int n)
    {
        // (one orbit: its hearts never meet but in a loop -- Keep spaces them)
        float minD = orbit.spread == 0f ? 0f : heartSize * personalSpace;
        for (int a = 0; a < hearts.Length; a++)
        {
            if (hearts[a] == null || !hearts[a].gameObject.activeSelf) continue;
            for (int b = a + 1; b < hearts.Length; b++)
            {
                if (hearts[b] == null || !hearts[b].gameObject.activeSelf) continue;
                Vector2 d = pos[a] - pos[b];
                float dist = d.magnitude;
                if (dist >= minD) continue;
                float k = 2.6f * (minD - dist) / minD;
                Vector2 dn = dist > 1e-5f ? d / dist : Vector2.right;
                // Each hurries on along its own path -- one leaving, or both
                // meeting head on, rush past -- except one catching the
                // other up, which hangs back.
                bool aCloses = Vector2.Dot(tangent[a], dn) < 0f, bCloses = Vector2.Dot(tangent[b], -dn) < 0f;
                push[a] += aCloses && !bCloses ? -k * .6f : k;
                push[b] += bCloses && !aCloses ? -k * .6f : k;
            }
        }

        if (n < 2) return;
        if (orbit.spread != 0f)
        {
            // crossing planes: each keeps its phase relative to the first
            // heart going its way round, so they stay spread round the ship
            int leadUp = -1, leadDown = -1;
            for (int i = 0; i < hearts.Length; i++)
            {
                if (hearts[i] == null || !hearts[i].gameObject.activeSelf) continue;
                bool up = dirTarget[i] > 0f;
                int lead = up ? leadUp : leadDown;
                if (lead < 0) { if (up) leadUp = i; else leadDown = i; continue; }
                float way = Mathf.Sign(dirTarget[lead]);
                float want = theta[lead] + SlotTheta(slotOf[i], n, 0f, way) - SlotTheta(slotOf[lead], n, 0f, way);
                float err = Mathf.DeltaAngle(theta[i] * Mathf.Rad2Deg, want * Mathf.Rad2Deg) * Mathf.Deg2Rad;
                push[i] += Mathf.Clamp(.8f * err * way, -.6f, .6f);
            }
            return;
        }
        // one orbit: each heart keeps its place behind the nearest one ahead
        // of it (a ring all the way round; a train but for its leader, the
        // one with the widest gap ahead)
        float gap = orbit.gap > 0f ? orbit.gap : 2f * Mathf.PI / n;
        int leader = -1;
        float widest = -1f;
        for (int i = 0; i < hearts.Length; i++)
        {
            if (hearts[i] == null || !hearts[i].gameObject.activeSelf) continue;
            float ahead;
            aheadOf[i] = Ahead(i, out ahead);
            if (ahead > widest) { widest = ahead; leader = i; }
        }
        for (int i = 0; i < hearts.Length; i++)
        {
            if (hearts[i] == null || !hearts[i].gameObject.activeSelf || aheadOf[i] < 0) continue;
            if (orbit.gap > 0f && i == leader) continue;
            Keep(i, aheadOf[i], gap);
        }
    }

    // The nearest shown heart ahead of heart i the way it goes round, and
    // how far ahead (radians).
    int Ahead(int i, out float distance)
    {
        float way = Mathf.Sign(dirTarget[i]);
        int best = -1;
        distance = 2f * Mathf.PI;
        for (int q = 0; q < hearts.Length; q++)
        {
            if (q == i || hearts[q] == null || !hearts[q].gameObject.activeSelf) continue;
            float d = Mathf.Repeat((theta[q] - theta[i]) * way, 2f * Mathf.PI);
            if (d < distance) { distance = d; best = q; }
        }
        return best;
    }

    void Keep(int i, int ahead, float gap)
    {
        float way = Mathf.Sign(dirTarget[ahead]);
        if (Mathf.Sign(dirTarget[i]) != way) return;
        float want = theta[ahead] - way * gap;
        float err = Mathf.DeltaAngle(theta[i] * Mathf.Rad2Deg, want * Mathf.Rad2Deg) * Mathf.Deg2Rad;
        push[i] += Mathf.Clamp(.9f * err * way, -.8f, .8f);
    }
}
