using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

// The ship's lives as small hearts flying with it, on every hull: one heart
// per life the ship flies with (ShipLives -- 2 on the starter, 3 once it has
// a colour of its own and on the cheap ships, 4 on the dear ones, 5 on Gold
// Warden).
//
// The player's thumb sits a hull-length under the ship, so the hearts
// gather at the TAIL: just behind the hull, either side of the engine flame,
// in each ship's own formation (ShipHeartStyles: a tail arc, a comet trail,
// wing-tip pairs, a beating cluster). The formation is measured off the
// hull's sprite and its exhaust plume and pushed clear of every registered
// ship element (charge indicator, gun, secret meter -- ShipUiSlots). Near a
// screen edge a one-sided formation mirrors, a two-sided one gives way to a
// compact cluster on the inner side; failing all of those, the old free
// slots round the hull (left, right, above, below).
//
// The spinners (Ninja, UFO) wear a shield ring instead: their hearts orbit
// snug round the spinning hull, upright, carried round by the hull's own
// spin (orbitRate), whipping quickly through the lower arc where the thumb
// is (OrbitWarp) and lingering on top. A heart ducks (shrinks and fades)
// past whatever is drawn on the ring this frame (ShipUiSlots.Drawn); at a
// screen edge the ring flattens against it.
//
// A hit breaks the lost heart away: it shakes and flashes, cracks into pixel
// shards that tumble off and fade. A heal pops a heart back in. The break-up
// is pooled (built once with the hearts) and runs on scaled time, like every
// formation motion here, so it all holds still while the world is frozen.
//
// For anything that comes out of the hearts (the fire-retardant spray):
// HeartPositions / HeartPosition / ShownCount, and HeartBounds.
//
// Placed in world space each frame, after the ship elements have moved.
[DefaultExecutionOrder(50)]
public class ShipLivesIndicator : MonoBehaviour
{
    [Tooltip("Gap kept between the hearts and the hull, flame or any other " +
             "ship element, in world units.")]
    public float clearance = 0.04f;

    [Tooltip("Gap between hearts in a fallback row, in world units.")]
    public float spacing = 0.24f;

    [Tooltip("Each heart's world size (diameter).")]
    public float heartSize = 0.22f;

    [Tooltip("Furthest a tail formation may be pushed down to clear something.")]
    public float maxPush = 0.45f;

    [Tooltip("Fallback slots: one within this of the hull is preferred.")]
    public float nearReach = 0.3f;

    [Tooltip("Fallback slots: furthest from the hull.")]
    public float maxReach = 1.2f;

    // Formation motion: positional sway and scale swell stay inside these.
    public const float BobAmplitude = 0.015f;
    public const float PulseAmplitude = 0.015f;
    public const float SwellMax = 1.08f;

    [Tooltip("Spinners: how far round the hearts go per turn of the hull.")]
    public float orbitRate = 1f / 3f;

    [Tooltip("Spinners: gap kept to the hull's spin circle and to what's drawn round it.")]
    public float orbitClearance = 0.04f;

    [Tooltip("Spinners: how far ahead of an obstacle a heart starts to shrink.")]
    public float squashBand = 0.08f;

    // Spinners: how much faster than the ring the hearts cross the bottom
    // (and slower over the top): displayed angle = u + OrbitWarp*sin(u - 270).
    public const float OrbitWarp = 0.45f;

    // The break-up: shake/flash, crack, then shards fall and fade.
    public const float BreakShake = .12f, BreakCrack = .1f, BreakFall = .6f;
    public const float BreakSeconds = BreakShake + BreakCrack + BreakFall;
    public const float PopSeconds = .28f;
    const int MaxBreaks = 3;
    const int ShardCols = 3, ShardRows = 2, Shards = ShardCols * ShardRows;

    Transform[] hearts;
    SpriteRenderer[] renderers;
    int lastShown = -1;
    float seed, clock;
    int shipId;
    HeartStyle style;
    float baseScale;
    float[] popLeft;

    // Formation candidates (non-spinners): offsets from the ship, per count.
    const int Primary = 0, Mirrored = 1, ClusterHome = 2, ClusterAway = 3, FirstSlot = 4;
    const int CandidateCount = FirstSlot + 4;
    Vector2[][] candidates;
    bool[] candidateClear;
    int laidOutFor = -1, layoutVersion = -1, chosen = -1;
    float relayoutIn;
    readonly List<Bounds> obstacles = new List<Bounds>();

    // Orbit (spinners only).
    bool orbit;
    float orbitAngle, lastHullAngle;
    bool haveHullAngle;
    float[] spot, shrink;
    readonly List<Bounds> drawn = new List<Bounds>();

    // Break-up pool.
    struct Break
    {
        public bool live;
        public float t;
        public Vector3 at;
        public Vector2 drift;
        public float scale;
    }
    Break[] breaks;
    SpriteRenderer[] ghosts;
    SpriteRenderer[] shards;          // MaxBreaks * Shards
    Vector2[] shardVelocity;
    float[] shardSpin;
    static Sprite[] shardSprites;
    static Vector2[] shardHome;       // each shard's centre in the heart, unit heart size

    public HeartStyle Style { get { return style; } }
    public Transform[] Hearts { get { return hearts; } }
    public bool Orbiting { get { return orbit; } }
    // Which formation is in use: 0 the ship's own, 1 mirrored, 2-3 a
    // compact cluster, 4+ a fallback slot (-1 orbiting / none yet).
    public int Formation { get { return orbit ? -1 : chosen; } }
    public bool AtTail { get { return !orbit && chosen >= 0 && chosen < FirstSlot; } }
    // Degrees the ring has turned (each heart adds its own even spacing).
    public float OrbitAngle { get { return orbitAngle; } }
    public float OrbitRadius { get; private set; }
    // 1 full size .. 0 ducked out of sight, per heart.
    public float Shrink(int i) { return shrink != null && i < shrink.Length ? shrink[i] : 1f; }
    // A spinner's heart i on the ring before the warp (degrees, from +x).
    public float RingAngle(int i) { return spot != null && i < spot.Length ? Mathf.Repeat(orbitAngle + spot[i] + 90f, 360f) : 0f; }
    public static float Warp(float degrees)
    {
        return degrees + OrbitWarp * Mathf.Rad2Deg * Mathf.Sin((degrees - 270f) * Mathf.Deg2Rad);
    }
    public int ActiveBreaks
    {
        get
        {
            int n = 0;
            if (breaks != null) foreach (var b in breaks) if (b.live) n++;
            return n;
        }
    }

    void Start()
    {
        BuildHearts();
        seed = Random.value * 10f;
    }

    // One heart per life of this run (ShipLives.RunMax).
    public void BuildHearts()
    {
        BuildHearts(ShipLives.RunMax);
    }

    public void BuildHearts(int count)
    {
        var hull = GetComponent<SpriteRenderer>();
        var sprite = Resources.Load<Sprite>("Vfx/lifeHeart");
        if (sprite == null) { Destroy(this); return; }
        // The flown ship's own id (its name), not the raw saved selection:
        // an unowned selection flies the starter.
        shipId = ShipId.Of(gameObject, ShipId.Equipped());
        style = ShipHeartStyles.For(shipId);

        DestroyBuilt();
        lastShown = -1;
        count = Mathf.Clamp(count, 1, ShipLives.Most);
        hearts = new Transform[count];
        renderers = new SpriteRenderer[count];
        spot = new float[count];
        shrink = new float[count];
        popLeft = new float[count];
        orbit = style == HeartStyle.ShieldRing;
        haveHullAngle = false;
        float parentScale = Mathf.Max(Mathf.Abs(transform.lossyScale.x), Mathf.Abs(transform.lossyScale.y), 0.0001f);
        float localSize = heartSize / parentScale;
        int order = (hull != null ? hull.sortingOrder : 0) + 2;
        baseScale = localSize / Mathf.Max(sprite.bounds.size.x, sprite.bounds.size.y);

        for (int i = 0; i < count; i++)
        {
            var go = new GameObject("Heart" + i);
            go.transform.SetParent(transform, false);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.sortingOrder = order;
            go.transform.localScale = Vector3.one * baseScale;
            hearts[i] = go.transform;
            renderers[i] = sr;
            spot[i] = 360f * i / count;
            shrink[i] = 1f;
        }

        candidates = new Vector2[CandidateCount][];
        for (int c = 0; c < CandidateCount; c++) candidates[c] = new Vector2[ShipLives.Most];
        candidateClear = new bool[CandidateCount];
        laidOutFor = -1;
        chosen = -1;

        BuildBreakPool(sprite, order + 1);
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

    // ---- the break-up pool ------------------------------------------------

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
        shards = new SpriteRenderer[MaxBreaks * Shards];
        shardVelocity = new Vector2[MaxBreaks * Shards];
        shardSpin = new float[MaxBreaks * Shards];
        for (int b = 0; b < MaxBreaks; b++)
        {
            ghosts[b] = NewPiece(root, "Ghost" + b, heart, order);
            for (int s = 0; s < Shards; s++)
                shards[b * Shards + s] = NewPiece(root, "Shard" + b + "_" + s, shardSprites[s], order);
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

    // Heart i was lost: it breaks away from where it is drawn now.
    void StartBreak(int i)
    {
        if (breaks == null || hearts[i] == null) return;
        int slot = 0;
        for (int b = 0; b < breaks.Length; b++)
        {
            if (!breaks[b].live) { slot = b; break; }
            if (breaks[b].t > breaks[slot].t) slot = b;   // all busy: reuse the oldest
        }
        Vector3 at = hearts[i].position;
        Vector3 away = at - transform.position;
        away.z = 0f;
        Vector2 dir = away.sqrMagnitude > 1e-6f ? (Vector2)away.normalized : Vector2.down;
        float lossy = Mathf.Max(Mathf.Abs(transform.lossyScale.x), 0.0001f);
        breaks[slot] = new Break
        {
            live = true, t = 0f, at = at, drift = dir * .35f + Vector2.down * .1f,
            scale = Mathf.Max(hearts[i].localScale.x, baseScale * .5f) * lossy,
        };
        int key = slot * 7919 + i * 31 + Mathf.FloorToInt(clock * 60f);
        for (int s = 0; s < Shards; s++)
        {
            Vector2 out0 = shardHome[s].sqrMagnitude > 1e-6f ? shardHome[s].normalized : Vector2.up;
            float speed = .45f + Hash01(key + s * 13) * .5f;
            shardVelocity[slot * Shards + s] = out0 * speed + dir * .25f + Vector2.up * .35f;
            shardSpin[slot * Shards + s] = (Hash01(key + s * 13 + 7) * 2f - 1f) * 540f;
        }
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

    // Advances every break-up by dt seconds of gameplay time.
    public void StepBreaks(float dt)
    {
        if (breaks == null) return;
        dt = Mathf.Max(0f, dt);
        for (int b = 0; b < breaks.Length; b++)
        {
            var br = breaks[b];
            var ghost = ghosts[b];
            if (!br.live)
            {
                if (ghost.enabled) ghost.enabled = false;
                for (int s = 0; s < Shards; s++) if (shards[b * Shards + s].enabled) shards[b * Shards + s].enabled = false;
                continue;
            }
            br.t += dt;
            float t = br.t;
            Transform root = ghost.transform.parent;
            Vector3 rootScale = root.lossyScale;
            float inv = 1f / Mathf.Max(.0001f, Mathf.Abs(rootScale.x));
            Vector3 drift = (Vector3)(br.drift * Mathf.Min(t, BreakShake + BreakCrack));
            if (t < BreakShake)
            {
                // breaks away from the formation, shaking and flashing white
                float k = t / BreakShake;
                float shake = Mathf.Sin(t * 140f) * .012f * (1f - k * .5f);
                ghost.enabled = true;
                ghost.transform.position = br.at + drift + new Vector3(shake, 0f, -.01f);
                ghost.transform.localScale = Vector3.one * br.scale * (1f + .12f * Mathf.Sin(k * Mathf.PI)) * inv;
                // a pale flash as it cracks
                float flash = Mathf.Sin(k * Mathf.PI);
                ghost.color = new Color(1f, 1f - .45f * flash, 1f - .45f * flash, 1f);
                for (int s = 0; s < Shards; s++) shards[b * Shards + s].enabled = false;
            }
            else if (t < BreakSeconds)
            {
                ghost.enabled = false;
                float crack = Mathf.Clamp01((t - BreakShake) / BreakCrack);
                float fall = Mathf.Max(0f, t - BreakShake - BreakCrack);
                float alpha = 1f - Mathf.Clamp01(fall / BreakFall);
                float size = br.scale;
                for (int s = 0; s < Shards; s++)
                {
                    int i = b * Shards + s;
                    var sr = shards[i];
                    sr.enabled = true;
                    // crack: the pieces part along their seams, then tumble
                    Vector3 home = (Vector3)(shardHome[s] * size * (1f + .25f * crack));
                    Vector2 v = shardVelocity[i];
                    Vector3 move = new Vector3(v.x * fall, v.y * fall - 2.4f * fall * fall, 0f);
                    sr.transform.position = br.at + drift + home * HeartWorldSpan() + move + new Vector3(0f, 0f, -.01f);
                    sr.transform.rotation = Quaternion.Euler(0f, 0f, shardSpin[i] * fall);
                    sr.transform.localScale = Vector3.one * size * (1f - .35f * Mathf.Clamp01(fall / BreakFall)) * inv;
                    sr.color = new Color(1f, 1f, 1f, alpha);
                }
            }
            else
            {
                br.live = false;
                ghost.enabled = false;
                for (int s = 0; s < Shards; s++) shards[b * Shards + s].enabled = false;
            }
            breaks[b] = br;
        }
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

    void Update()
    {
        if (hearts == null) return;

        // Hearts left: the lives built, less the hits taken (ShipLives).
        int remaining = Mathf.Clamp(hearts.Length - collisionDetection.lifeCounter, 0, hearts.Length);
        if (remaining != lastShown)
        {
            for (int i = 0; i < hearts.Length; i++)
            {
                if (hearts[i] == null) continue;
                bool show = i < remaining;
                bool was = hearts[i].gameObject.activeSelf;
                if (was && !show && lastShown >= 0) StartBreak(i);
                if (!was && show && lastShown >= 0) popLeft[i] = PopSeconds;
                hearts[i].gameObject.SetActive(show);
            }
            lastShown = remaining;
            laidOutFor = -1;   // the ones left close up
        }
    }

    void LateUpdate()
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

    // ---- placement -------------------------------------------------------

    // Lays the hearts out for where the ship is now. `unscaledDt` drives the
    // periodic re-measure; `scaledDt` (gameplay time) all motion, so the
    // hearts hold still while the world is frozen.
    public void Place(float unscaledDt, float scaledDt = 0f)
    {
        if (hearts == null) return;
        scaledDt = Mathf.Max(0f, scaledDt);
        clock += scaledDt;
        for (int i = 0; i < popLeft.Length; i++) if (popLeft[i] > 0f) popLeft[i] = Mathf.Max(0f, popLeft[i] - scaledDt);
        if (orbit) { Orbit(scaledDt); return; }

        int n = ShownCount;
        if (n == 0) return;
        relayoutIn -= unscaledDt;
        if (laidOutFor != n || layoutVersion != ShipUiSlots.Version || relayoutIn <= 0f) Relayout(n);

        Vector3 p = transform.position;
        chosen = ChooseFormation(n, p, ShipUiSlots.ScreenRect(null));
        if (chosen < 0) return;
        var offsets = candidates[chosen];
        var animStyle = chosen < ClusterHome ? style : chosen < FirstSlot ? HeartStyle.PulseCluster : HeartStyle.TailArc;
        int k = 0;
        for (int i = 0; i < hearts.Length; i++)
        {
            if (hearts[i] == null || !hearts[i].gameObject.activeSelf) continue;
            Vector2 sway; float swell;
            Motion(animStyle, k, n, clock + seed, out sway, out swell);
            Vector2 o = offsets[k++] + sway;
            hearts[i].position = new Vector3(p.x + o.x, p.y + o.y, p.z - 0.1f);
            hearts[i].rotation = Quaternion.identity;
            hearts[i].localScale = Vector3.one * (baseScale * swell * PopScale(popLeft[i]));
            var sr = renderers[i];
            if (sr != null && !sr.enabled) sr.enabled = true;
        }
    }

    // Each style's own life, inside BobAmplitude / SwellMax.
    static void Motion(HeartStyle style, int k, int n, float t, out Vector2 sway, out float swell)
    {
        sway = Vector2.zero;
        swell = 1f;
        switch (style)
        {
            case HeartStyle.TailArc:
                // a ripple running along the arc
                sway.y = Mathf.Sin(t * 3f + (k / 2) * .9f) * BobAmplitude;
                break;
            case HeartStyle.CometTrail:
            {
                // the tail wags, more towards its tip, and tapers
                float reach = n > 1 ? (float)k / (n - 1) : 0f;
                sway.y = Mathf.Sin(t * 4f - k * .8f) * BobAmplitude * (.35f + .65f * reach);
                sway.x = Mathf.Cos(t * 4f - k * .8f) * BobAmplitude * .5f * reach;
                swell = 1f - .1f * reach * (1f - .3f * Mathf.Sin(t * 4f - k * .8f));
                break;
            }
            case HeartStyle.WingTips:
                // the two wing lights bob out of step with each other
                sway.y = Mathf.Sin(t * 2.4f + (k % 2) * Mathf.PI) * BobAmplitude;
                break;
            case HeartStyle.PulseCluster:
            default:
            {
                // lub-dub: two quick beats, then a rest
                float beat = Mathf.Repeat(t * 1.1f, 1f);
                float pulse = Mathf.Exp(-Mathf.Pow((beat - .1f) / .05f, 2f)) + .7f * Mathf.Exp(-Mathf.Pow((beat - .28f) / .05f, 2f));
                swell = 1f + (SwellMax - 1f) * Mathf.Clamp01(pulse);
                break;
            }
        }
    }

    // A heart's keep-out box: its largest drawing plus its sway.
    float HeartBox { get { return heartSize * SwellMax + BobAmplitude * 2f; } }

    // Re-measures every formation for n hearts against the hull, its flame
    // and every registered ship element.
    public void Relayout() { Relayout(Mathf.Max(1, ShownCount)); }

    void Relayout(int n)
    {
        laidOutFor = n;
        layoutVersion = ShipUiSlots.Version;
        relayoutIn = 1f;

        Vector3 p = transform.position;
        Bounds hull = ShipUiSlots.HullBounds(transform, shipId);
        Bounds exhaust;
        bool hasExhaust = ShipUiSlots.ExhaustBounds(transform, shipId, out exhaust);
        obstacles.Clear();
        obstacles.Add(hull);
        if (hasExhaust) obstacles.Add(exhaust);
        int first = obstacles.Count;
        ShipUiSlots.Occupied(transform, obstacles, this);
        // The ship's elements live beside and above the hull; their keep-out
        // boxes are unions of every pose (the gun's rest, hover and ride to
        // the firing slot), which only reach under the tail line as an
        // artefact of being one box. Under it is the hearts' room.
        float tailLine = hull.min.y;
        for (int i = first; i < obstacles.Count; i++)
        {
            var o = obstacles[i];
            if (o.max.y <= tailLine || o.min.y >= tailLine) continue;
            o.SetMinMax(new Vector3(o.min.x, tailLine, o.min.z), o.max);
            obstacles[i] = o;
        }
        // ...except the gun's own rest pose, which does hover down there.
        var gun = GetComponentInChildren<UltimateGun>(true);
        if (gun != null) obstacles.Add(ShipUiSlots.GunToWorld(transform, shipId, gun.LocalRestEnvelope()));

        // The tail: the hull's bottom edge; the flame's sides (or a nominal
        // narrow flame under the nozzles' middle).
        var f = new ShipHeartStyles.Frame
        {
            tailY = hull.min.y - p.y,
            hullL = hull.min.x - p.x,
            hullR = hull.max.x - p.x,
            size = HeartBox,
            gap = clearance,
        };
        if (hasExhaust)
        {
            // the drawn plume flickers and flares wider than its rest
            // frame's box: keep half as much again to either side
            float mid = exhaust.center.x - p.x, flare = exhaust.extents.x * FlameFlare;
            f.flameL = Mathf.Max(mid - flare, f.hullL);
            f.flameR = Mathf.Min(mid + flare, f.hullR);
            exhaust.Expand(new Vector3(exhaust.size.x * (FlameFlare - 1f), 0f, 0f));
            obstacles[1] = exhaust;
        }
        else { f.flameL = -.06f; f.flameR = .06f; }

        float home = ShipHeartStyles.HomeSide(shipId);
        Lay(Primary, style, n, home, f);
        Lay(Mirrored, style, n, -home, f);
        Lay(ClusterHome, HeartStyle.PulseCluster, n, home, f);
        Lay(ClusterAway, HeartStyle.PulseCluster, n, -home, f);

        // Last resort: the free slots round the hull, as a row or a stack.
        float length = (n - 1) * spacing + heartSize;
        var slots = ShipUiSlots.Candidates(transform, shipId, new ShipUiSlots.Request
        {
            rowSize = new Vector2(length, HeartBox),
            columnSize = new Vector2(HeartBox, length + BobAmplitude * 2f),
            gap = clearance,
            nearReach = nearReach,
            maxReach = maxReach,
        }, this);
        // tried left, right, above, then below (the thumb's side)
        for (int o = 0; o < FallbackOrder.Length; o++)
        {
            int c = FirstSlot + o;
            candidateClear[c] = false;
            foreach (var slot in slots)
            {
                if (slot.side != FallbackOrder[o]) continue;
                bool across = slot.side == ShipUiSlots.Side.Above || slot.side == ShipUiSlots.Side.Below;
                float start = -(n - 1) * spacing * .5f;
                for (int k = 0; k < n; k++)
                {
                    float d = start + k * spacing;
                    candidates[c][k] = across ? slot.offset + new Vector2(d, 0f) : slot.offset + new Vector2(0f, -d);
                }
                candidateClear[c] = slot.clear;
            }
        }
    }

    static readonly ShipUiSlots.Side[] FallbackOrder =
        { ShipUiSlots.Side.Left, ShipUiSlots.Side.Right, ShipUiSlots.Side.Above, ShipUiSlots.Side.Below };

    // One tail formation, pushed straight down (whole) until clear of every
    // obstacle; unclear if that takes more than maxPush.
    void Lay(int c, HeartStyle s, int n, float side, ShipHeartStyles.Frame f)
    {
        var offs = candidates[c];
        for (int k = 0; k < n; k++) offs[k] = ShipHeartStyles.Offset(s, k, n, side, f);
        // A two-sided formation pushes each side on its own, so a low
        // element on one side doesn't drag the other side down too; a side
        // that has to drop far (the gun hovers low there) gives its hearts
        // to the other side.
        bool split = ShipHeartStyles.TwoSided(s) && n > 1;
        bool clear;
        if (split)
        {
            float pushR, pushL;
            clear = PushClear(offs, n, 1, out pushR) & PushClear(offs, n, -1, out pushL);
            float worst = Mathf.Max(pushR, pushL);
            if (!clear || worst > OneSidedAfter)
            {
                float freeSide = pushR <= pushL ? 1f : -1f;
                for (int k = 0; k < n; k++) offs[k] = ShipHeartStyles.Offset(s, k, n, freeSide, f, true);
                float ignored;
                clear = PushClear(offs, n, 0, out ignored);
            }
        }
        else
        {
            float ignored;
            clear = PushClear(offs, n, 0, out ignored);
        }
        candidateClear[c] = clear;
    }

    // How much wider than its measured box the flame is kept clear of.
    const float FlameFlare = 1.6f;

    // A side pushed further down than this goes one-sided.
    const float OneSidedAfter = .1f;

    // The thumb: movePlayer flies the ship ThumbBelow above the finger; its
    // pad covers ThumbRadius round that point, the rest of it everything
    // under. Hearts keep out of both.
    public const float ThumbBelow = 1f, ThumbRadius = .42f;

    public static bool UnderThumb(Vector2 offset, float half)
    {
        float cy = -ThumbBelow;
        if (offset.y + half < cy) return true;
        float dx = Mathf.Max(Mathf.Abs(offset.x) - half, 0f);
        float dy = Mathf.Max(0f, Mathf.Max((offset.y - half) - cy, cy - (offset.y + half)));
        return dx * dx + dy * dy < ThumbRadius * ThumbRadius;
    }

    // Furthest a formation slides out sideways to clear the thumb.
    const float MaxThumbShift = .3f;

    // Which way is "out" for a group: its own side, or for a whole
    // one-sided formation the side most of it is on.
    static float Outward(Vector2[] offs, int n, int group)
    {
        if (group != 0) return group;
        float sum = 0f;
        for (int k = 0; k < n; k++) sum += offs[k].x;
        return sum >= 0f ? 1f : -1f;
    }

    // Pushes the hearts on `group`'s side (+1 right of the ship, -1 left,
    // 0 all) straight down together until clear of every obstacle. False if
    // that takes more than maxPush.
    bool PushClear(Vector2[] offs, int n, int group, out float pushed)
    {
        Vector3 p = transform.position;
        // (a hair under the full gap: the styles sit hearts exactly one gap
        // off the flame and hull, which must not count as touching)
        float half = HeartBox * .5f + clearance * .9f;
        pushed = 0f;
        float shifted = 0f;
        bool clear = false;
        for (int pass = 0; pass < 24; pass++)
        {
            float need = 0f;
            bool thumb = false;
            for (int k = 0; k < n; k++)
            {
                if (group != 0 && Mathf.Sign(offs[k].x) != group) continue;
                var box = new Bounds(new Vector3(p.x + offs[k].x + Outward(offs, n, group) * shifted, p.y + offs[k].y - pushed, p.z),
                                     new Vector3(half * 2f, half * 2f, 0f));
                foreach (var o in obstacles)
                {
                    var flat = new Bounds(new Vector3(o.center.x, o.center.y, p.z), new Vector3(o.size.x, o.size.y, 0f));
                    if (!ShipUiSlots.Overlaps(flat, box)) continue;
                    need = Mathf.Max(need, box.max.y - flat.min.y + .001f);
                }
                var at = new Vector2(offs[k].x + Outward(offs, n, group) * shifted, offs[k].y - pushed);
                if (UnderThumb(at, HeartBox * .5f)) thumb = true;
            }
            if (need > 0f) { pushed += need; if (pushed > maxPush) break; continue; }
            if (thumb && shifted < MaxThumbShift) { shifted += .02f; continue; }
            clear = !thumb;
            break;
        }
        float dir = Outward(offs, n, group);
        for (int k = 0; k < n; k++)
            if (group == 0 || Mathf.Sign(offs[k].x) == group) { offs[k].y -= pushed; offs[k].x += dir * shifted; }
        return clear && pushed <= maxPush;
    }

    // The formation to use: the first clear one (in candidate order) wholly
    // on screen, the one in use needing no extra margin to stay (so a ship
    // hovering at an edge doesn't flick its hearts back and forth).
    int ChooseFormation(int n, Vector3 p, Rect screen)
    {
        const float hysteresis = .1f;
        for (int pass = 0; pass < 2; pass++)
            for (int c = 0; c < CandidateCount; c++)
            {
                if (!candidateClear[c] && pass == 0) continue;
                float inset = c == chosen ? 0f : hysteresis;
                if (FitsScreen(c, n, p, screen, inset)) return c;
            }
        return chosen >= 0 ? chosen : Primary;
    }

    bool FitsScreen(int c, int n, Vector3 p, Rect screen, float inset)
    {
        float half = HeartBox * .5f;
        var offs = candidates[c];
        for (int k = 0; k < n; k++)
        {
            float x = p.x + offs[k].x, y = p.y + offs[k].y;
            if (x - half < screen.xMin + inset || x + half > screen.xMax - inset ||
                y - half < screen.yMin + inset || y + half > screen.yMax - inset) return false;
        }
        return true;
    }

    // World centre of formation slot k as laid out now (tests, previews).
    public Vector3 FormationPoint(int k)
    {
        if (candidates == null || chosen < 0) return transform.position;
        Vector2 o = candidates[chosen][Mathf.Clamp(k, 0, ShipLives.Most - 1)];
        return transform.position + new Vector3(o.x, o.y, 0f);
    }

    // ---- spinners: the shield ring -----------------------------------------

    void Orbit(float dt)
    {
        // Carried round by the hull's own spin, which already stops when the
        // world freezes or the pilot lets go.
        float hullAngle = transform.eulerAngles.z;
        if (haveHullAngle)
            orbitAngle = Mathf.Repeat(orbitAngle + Mathf.DeltaAngle(lastHullAngle, hullAngle) * orbitRate, 360f);
        lastHullAngle = hullAngle;
        haveHullAngle = true;

        // Snug round the circle the spinning hull sweeps, with room for the
        // heart's corner, the bob and the pulse.
        float hullRadius = ShipUiSlots.HullBounds(transform, shipId).extents.x;
        float half = heartSize * .5f;
        float diagonal = half * 1.4143f;
        float edge = half + .002f;
        OrbitRadius = hullRadius + orbitClearance + diagonal + BobAmplitude + PulseAmplitude;
        float radius = OrbitRadius + Mathf.Sin((clock + seed) * 2.1f) * PulseAmplitude;
        float bob = Mathf.Sin((clock + seed) * 3f) * BobAmplitude;

        drawn.Clear();
        ShipUiSlots.Drawn(transform, drawn, this);
        Rect screen = ShipUiSlots.ScreenRect(null);
        Vector3 p = transform.position;

        // The hearts left share the ring evenly; a lost one's gap closes up.
        int shown = ShownCount;
        float ease = 1f - Mathf.Exp(-6f * dt);
        int k = 0;
        for (int i = 0; i < hearts.Length; i++)
        {
            if (hearts[i] == null || !hearts[i].gameObject.activeSelf) continue;
            float target = 360f * k++ / Mathf.Max(1, shown);
            spot[i] = Mathf.LerpAngle(spot[i], target, ease);

            float a = Warp(orbitAngle + spot[i] + 90f) * Mathf.Deg2Rad;
            var at = new Vector3(p.x + Mathf.Cos(a) * radius, p.y + Mathf.Sin(a) * radius + bob, p.z - 0.1f);
            // flatten against a screen edge rather than leave it
            at.x = Mathf.Clamp(at.x, screen.xMin + edge, Mathf.Max(screen.xMin + edge, screen.xMax - edge));
            at.y = Mathf.Clamp(at.y, screen.yMin + edge, Mathf.Max(screen.yMin + edge, screen.yMax - edge));

            // Duck round the hull's circle and anything drawn here now.
            float s = Mathf.Clamp01(((new Vector2(at.x - p.x, at.y - p.y)).magnitude - hullRadius - orbitClearance) / diagonal);
            foreach (var o in drawn)
            {
                float gap = Mathf.Max(Mathf.Abs(at.x - o.center.x) - o.extents.x, Mathf.Abs(at.y - o.center.y) - o.extents.y);
                s = Mathf.Min(s, Mathf.Clamp01((gap - orbitClearance) / (half + squashBand)));
            }
            shrink[i] = s;

            hearts[i].position = at;
            hearts[i].rotation = Quaternion.identity;
            hearts[i].localScale = Vector3.one * (baseScale * s * PopScale(popLeft[i]));
            var sr = renderers[i];
            if (sr != null)
            {
                sr.enabled = s > .12f;
                var c = sr.color;
                c.a = Mathf.Clamp01(s * 1.25f);
                sr.color = c;
            }
        }
    }
}

// Attaches the indicator to the player ship once it exists, matching the
// same poll-until-found pattern used by ShipThruster/ShipPowerController.
public class ShipLivesIndicatorAttach : MonoBehaviour
{
    float giveUp = 6f;

    void Update()
    {
        var player = Object.FindFirstObjectByType<movePlayer>();
        if (player != null)
        {
            if (player.GetComponent<ShipLivesIndicator>() == null)
                player.gameObject.AddComponent<ShipLivesIndicator>();
            Destroy(gameObject);
            return;
        }

        giveUp -= Time.unscaledDeltaTime;
        if (giveUp <= 0f) Destroy(gameObject);
    }
}

public static class ShipLivesIndicatorBootstrap
{
    [RuntimeInitializeOnLoadMethod]
    static void Init()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (scene.name != "gameS1" && scene.name != "tutorialS5") return;
        new GameObject("~ShipLivesIndicatorAttach").AddComponent<ShipLivesIndicatorAttach>();
    }
}
