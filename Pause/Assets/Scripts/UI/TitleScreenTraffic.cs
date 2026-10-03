using UnityEngine;
using UnityEngine.SceneManagement;

// Idle ship traffic behind the home screen (startS4).
//
// The whole roster flies around behind the menu in three depth layers:
//
//   Back   small, slow, hazed (TitleTrafficHaze: desaturated, dimmed, mixed
//          toward the INDIGO_1 night sky), drawn behind the PAUSE logo.
//   Mid    full colour at a modest size, also behind the logo.
//   Front  big, crisp and quick, drawn in front of the world (the menu
//          buttons are a screen-space overlay, so nothing here can ever sit
//          over them). Front ships pick lanes that avoid the logo and the
//          menu column and fade through fast if they cross either.
//
// Scale, speed, haze and sorting order all come from the layer (Depths).
//
// Flight is cartoon, not physics: ships cruise on gentle curves toward
// waypoints, leaning into the turns with a little bob; now and then one hits
// the boost (the hull's own ShipExhaust boost flame flares, a squash on the
// anticipation, a stretch and an Akira light streak on the burst, then it
// settles), throws a loop or a barrel roll, or two of them fly a short
// formation before the wingman peels off.
//
// Zips move the way ships leave the space dock, on the very same curves
// (DockLaunch, shared with SpaceDock.LaunchRoutine): a ship drifts to a
// pause, backs off and lifts, banks, then accelerates hard along the launch
// Bezier with its exhaust flaring and flies off-screen. Zoomers are that move
// in reverse: they streak in from off-screen on the mirrored curve, brake
// into a spot, settle, and a moment later zip off again. Poses (squash,
// stretch, roll) are held, as docs/art-style.md section 3 asks.
//
// Crashes are rare and cute: every 6-12 s two ships on the same layer pick a
// fight (only same-layer ships ever collide, so depth reads right). On
// contact one blows apart -- the pooled WeaponFx flash, shockwave ring and
// metal cel explosion, at a size scaled to the layer, while its hull breaks
// into four spinning pieces -- and the other usually tumbles off dizzy with
// little stars round its head, then either pops or shakes it off and boosts
// away. The population is topped back up from offscreen.
//
// Everything is pooled up front (one ship per roster id, so no two hulls in
// the air are ever the same), nothing allocates per frame, and the whole
// thing runs on unscaled time: moveBackGround can freeze Time.timeScale on
// the menu after a run. Purely cosmetic: no colliders, no UI graphics, all on
// the Ignore Raycast layer, and it never touches the logo or the menu.
public class TitleScreenTraffic : MonoBehaviour
{
    public enum Depth { Back = 0, Mid = 1, Front = 2 }

    public struct DepthSpec
    {
        public float scale;        // x the 0.58u reference hull
        public float speed;        // cruise, world units per second
        public float turnRate;     // max steering, radians per second
        public int sortBase;       // first sorting order of the layer's band
        public int fxSort;         // explosions / debris on this layer
        public bool haze;          // TitleTrafficHaze material
        public Color fxTint;       // multiplies explosions on this layer
    }

    // Ordered back to front. Every field that reads as depth grows forward.
    public static readonly DepthSpec[] Depths =
    {
        new DepthSpec { scale = .55f, speed = .42f, turnRate = .75f,
                        sortBase = -300, fxSort = -215, haze = true,
                        fxTint = new Color(.62f, .64f, .80f, .9f) },
        new DepthSpec { scale = .92f, speed = .78f, turnRate = 1.0f,
                        sortBase = -200, fxSort = -115, haze = false,
                        fxTint = Color.white },
        new DepthSpec { scale = 1.45f, speed = 1.25f, turnRate = 1.25f,
                        sortBase = 10, fxSort = 95, haze = false,
                        fxTint = Color.white },
    };

    public const int SortSlots = 5;            // per ship: trail x2, flame, hull, spare
    public const int MaxCap = 14;

    [Tooltip("Most ships in the air at once (10-14 reads busy but not chaotic).")]
    [Range(1, MaxCap)] public int maxShips = 12;

    [Tooltip("Cruising ships kept on each layer, back to front. Zoomers and " +
             "formation wingmen come on top, up to maxShips.")]
    public int[] layerTargets = { 5, 4, 2 };

    [Tooltip("Seconds between crashes.")]
    public Vector2 crashInterval = new Vector2(6f, 9f);

    [Tooltip("Seconds between zoomers.")]
    public Vector2 zoomInterval = new Vector2(6f, 12f);

    [Tooltip("Seconds between formation fly-bys.")]
    public Vector2 formationInterval = new Vector2(10f, 18f);

    [Tooltip("Seconds before a lost ship is replaced from offscreen.")]
    public Vector2 respawnDelay = new Vector2(.6f, 2.0f);

    public const float Tick = 1f / 24f;
    const float ReferenceHull = shopingShips.ReferenceHullSize;
    const float IdleFlame = .42f;

    // docs/art-style.md palette
    static readonly Color Red = new Color(.847f, .137f, .173f, 1f);     // #D8232C
    static readonly Color Amber = new Color(1f, .706f, .235f, 1f);      // #FFB43C
    static readonly Color Bone = new Color(.957f, .918f, .831f, 1f);    // #F4EAD4
    static readonly Color Haze = new Color(.165f, .18f, .42f, 1f);      // #2A2E6B

    public enum State { Idle, Cruise, Boost, ZipIn, ZipOut, Pursue, Formation, Dizzy }
    public enum Trick { None, Loop, Roll }

    // One pooled ship per roster id.
    public class Flyer
    {
        public int id;
        public GameObject go;
        public Transform tr;
        public SpriteRenderer hull;
        public Transform boost;               // ShipExhaust's "Boost<id>" flame root
        public SpriteRenderer[] nozzles;
        public Sprite[] shards;               // hull quarters for crash debris
        public float normScale;               // NormalizedHullScale for its sprite
        public bool wind;                     // Ninja / UFO: spinning craft, no flame
        public int slot;                      // pool index, for sorting

        public bool active;
        public Depth layer;
        public State state;
        public Vector2 pos;
        public float heading;                 // radians, 0 = +x
        public float speed, baseSpeed, scale, radius;
        public float turn;                    // this frame's steering, rad/s
        public Vector2 waypoint;
        public int waypointsLeft;
        public float stateT, spin, spinVel, alpha, phase, wobF, wobA, born;
        public float nextBoost, nextTrick;
        public Trick trick;
        public float trickT, trickDir;
        public float speedMul, flame, stretchX, stretchY;
        public Flyer partner;                 // pursuit target / formation leader
        public Vector2 wingOffset;
        public Vector2 drift;
        public bool zoomer;
        // dock-launch zips (DockLaunch curves): Bezier p0 -> p1 -> p2
        public Vector2 zipP0, zipP1, zipP2, zipRest, zipDir;
        public float zipHeading, zipBank, grow = 1f, nextZip;
        public bool zipFlying;
        public Trail trail;
        public bool pendingPop;
    }

    // An Akira tail-light streak: RED outer, AMBER middle, BONE core, hard
    // flat bands like the ultimate's shot trail. Pooled; ships borrow one
    // while boosting or zooming.
    public class Trail
    {
        public const int Segments = 6;
        public const float Spacing = .14f;
        public SpriteRenderer[] outer = new SpriteRenderer[Segments];
        public SpriteRenderer[] mid = new SpriteRenderer[Segments];
        public SpriteRenderer[] core = new SpriteRenderer[Segments];
        public readonly Vector3[] hist = new Vector3[Segments + 1];
        public int count;
        public Flyer owner;
        public bool releasing;
        public float width, alpha, clock;
        public bool InUse => owner != null || releasing;
    }

    class Shard
    {
        public SpriteRenderer sr;
        public Vector2 vel;
        public float spin, age, life, size;
        public bool on;
    }

    class Star
    {
        public SpriteRenderer sr;
        public Flyer owner;
        public int index;
    }

    public const int TrailPool = 4, ShardPool = 16, StarPool = 6, FxTrack = 12;

    Flyer[] pool;
    readonly Trail[] trails = new Trail[TrailPool];
    readonly Shard[] shards = new Shard[ShardPool];
    readonly Star[] stars = new Star[StarPool];
    readonly FlipbookFx[] fx = new FlipbookFx[FxTrack];
    int fxCount;
    Material hazeMaterial, spriteMaterial;
    bool built;

    // ---- clock and pacing (own clock: deterministic in tests, unscaled in play)
    float now;
    float nextCrashAt, pursuitStarted;
    float nextZoomAt, nextFormationAt;
    readonly float[] nextSpawnAt = new float[3];
    Flyer pursuerA, pursuerB;

    // ---- screen geometry (world units)
    Camera cam;
    Rect view, safe, logo, menu;
    int screenW = -1, screenH = -1;
    float camSize = -1f, camAspect = -1f;
    Transform menuPanel;
    SpriteRenderer logoRenderer;
    readonly Vector3[] corners = new Vector3[4];

    // ---- stats, read by the tests
    public const int CrashLog = 64;
    readonly float[] crashTimes = new float[CrashLog];
    readonly int[] crashLayers = new int[CrashLog];
    readonly Vector2[] crashSites = new Vector2[CrashLog];
    bool geometryPinned;
    public int Crashes { get; private set; }
    public int CrossLayerCrashes { get; private set; }
    public int Boosts { get; private set; }
    public int Zooms { get; private set; }
    public int Zips { get; private set; }
    public int Formations { get; private set; }
    public int Loops { get; private set; }
    public int Rolls { get; private set; }
    public int Explosions { get; private set; }
    public int DizzyBeats { get; private set; }
    public FlipbookFx LastExplosion { get; private set; }
    public float Now => now;
    public float NextCrashAt { get => nextCrashAt; set => nextCrashAt = value; }
    public Flyer[] Pool => pool;
    public Trail[] Trails => trails;
    public int ShardPoolSize => ShardPool;
    public int TrackedFx => fxCount;
    public Rect View => view;
    public Rect LogoRect => logo;
    public Rect MenuRect => menu;
    public float CrashTime(int i) => crashTimes[i % CrashLog];
    public int CrashLayer(int i) => crashLayers[i % CrashLog];
    public Vector2 CrashSite(int i) => crashSites[i % CrashLog];
    public Rect Safe => safe;

    // Tests: pretend the screen is a given shape (world-space view and safe
    // area) instead of reading the camera and Screen.
    public void PinGeometry(Rect worldView, Rect worldSafe)
    {
        geometryPinned = true;
        view = worldView;
        safe = worldSafe;
    }

    public int ActiveCount
    {
        get { int n = 0; for (int i = 0; i < pool.Length; i++) if (pool[i].active) n++; return n; }
    }

    public int CountIn(Depth d)
    {
        int n = 0;
        for (int i = 0; i < pool.Length; i++) if (pool[i].active && pool[i].layer == d) n++;
        return n;
    }

    int CruisersIn(Depth d)
    {
        int n = 0;
        for (int i = 0; i < pool.Length; i++)
        {
            var f = pool[i];
            if (f.active && f.layer == d && !f.zoomer && f.state != State.Formation) n++;
        }
        return n;
    }

    // ------------------------------------------------------------------ setup

    void Start() { Init(); }

    // Idempotent; public so the edit-mode tests can build it without Play.
    public void Init()
    {
        if (built) return;
        built = true;
        maxShips = Mathf.Clamp(maxShips, 1, MaxCap);
        gameObject.layer = 2; // Ignore Raycast

        var shader = Resources.Load<Shader>("TitleTraffic/TitleTrafficHaze");
        if (shader != null && shader.isSupported)
        {
            hazeMaterial = new Material(shader) { name = "TitleTrafficHaze" };
        }

        BuildPool();
        BuildTrails();
        BuildShards();
        BuildStars();
        FindScene();
        RefreshGeometry(true);

        now = 0f;
        nextCrashAt = Random.Range(crashInterval.x * .6f, crashInterval.y * .6f);
        nextZoomAt = Random.Range(1.5f, 4f);
        nextFormationAt = Random.Range(3f, 7f);
        for (int i = 0; i < 3; i++) nextSpawnAt[i] = 0f;

        // open on a populated sky rather than an empty one filling up
        for (int d = 0; d < 3; d++)
            for (int i = 0; i < layerTargets[d] && ActiveCount < maxShips; i++)
                Launch((Depth)d, true, false, null);
    }

    void BuildPool()
    {
        pool = new Flyer[ShipId.Count];
        int slot = 0;
        foreach (int id in ShipId.All)
        {
            var go = new GameObject(ShipId.ObjectName(id));
            go.layer = 2;
            go.transform.SetParent(transform, false);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sortingOrder = Depths[1].sortBase + slot * SortSlots + 3;

            // The same dressing gameplay ships get: roster hull art (whatever
            // the hull redraw puts behind shopingShips/OriginalShipArt), its
            // normalised size and the ship's own nozzle-mounted boost flame.
            spawnShips.ApplyHull(go, id);
            go.name = ShipId.ObjectName(id);

            var f = new Flyer { id = id, go = go, tr = go.transform, hull = sr, slot = slot };
            f.wind = ShipExhaust.UsesWind(id);
            f.normScale = sr.sprite != null ? shopingShips.NormalizedHullScale(sr.sprite) : 1f;
            var boost = go.transform.Find("Boost" + id);
            f.boost = boost;
            if (boost != null)
            {
                boost.gameObject.layer = 2;
                boost.gameObject.SetActive(true);
                f.nozzles = boost.GetComponentsInChildren<SpriteRenderer>(true);
                for (int i = 0; i < f.nozzles.Length; i++) f.nozzles[i].gameObject.layer = 2;
            }
            else f.nozzles = new SpriteRenderer[0];
            f.shards = Quarters(sr.sprite);
            go.SetActive(false);
            pool[slot++] = f;
        }
        spriteMaterial = pool.Length > 0 ? pool[0].hull.sharedMaterial : null;
    }

    // The hull cut into four pieces for debris.
    static Sprite[] Quarters(Sprite s)
    {
        var result = new Sprite[4];
        if (s == null || s.texture == null) return result;
        Rect r = s.textureRect;
        float w = r.width * .5f, h = r.height * .5f;
        for (int i = 0; i < 4; i++)
        {
            var q = new Rect(r.x + (i & 1) * w, r.y + (i >> 1) * h, w, h);
            result[i] = Sprite.Create(s.texture, q, new Vector2(.5f, .5f), s.pixelsPerUnit);
        }
        return result;
    }

    void BuildTrails()
    {
        for (int t = 0; t < TrailPool; t++)
        {
            var trail = new Trail();
            for (int i = 0; i < Trail.Segments; i++)
            {
                trail.outer[i] = Renderer("~trail", WeaponFx.Solid);
                trail.mid[i] = Renderer("~trail", WeaponFx.Solid);
                trail.core[i] = Renderer("~trail", WeaponFx.Solid);
            }
            trails[t] = trail;
        }
    }

    void BuildShards()
    {
        for (int i = 0; i < ShardPool; i++) shards[i] = new Shard { sr = Renderer("~debris", null) };
    }

    void BuildStars()
    {
        for (int i = 0; i < StarPool; i++) stars[i] = new Star { sr = Renderer("~dizzy", null), index = i % 3 };
    }

    SpriteRenderer Renderer(string name, Sprite sprite)
    {
        var go = new GameObject(name);
        go.layer = 2;
        go.transform.SetParent(transform, false);
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = sprite;
        sr.enabled = false;
        return sr;
    }

    // Reads (never writes) where the logo and the menu column are, so lanes
    // and crash sites can keep clear of them.
    void FindScene()
    {
        var title = GameObject.Find("menuTitle");
        logoRenderer = title != null ? title.GetComponent<SpriteRenderer>() : null;
        var panel = GameObject.Find("UIPanel");
        menuPanel = panel != null ? panel.transform : null;
    }

    void RefreshGeometry(bool force)
    {
        if (geometryPinned) return;
        if (cam == null) cam = Camera.main;
        float size = cam != null && cam.orthographic ? cam.orthographicSize : 5f;
        float aspect = cam != null ? cam.aspect : .5625f;
        if (!(aspect > .2f && aspect < 4f)) aspect = .5625f;
        if (!force && Screen.width == screenW && Screen.height == screenH && size == camSize && aspect == camAspect)
            return;
        screenW = Screen.width; screenH = Screen.height; camSize = size; camAspect = aspect;

        Vector3 c = cam != null ? cam.transform.position : Vector3.zero;
        float halfH = size, halfW = size * aspect;
        view = new Rect(c.x - halfW, c.y - halfH, halfW * 2f, halfH * 2f);

        safe = view;
        if (screenW > 0 && screenH > 0)
        {
            Rect sa = Screen.safeArea;
            safe = new Rect(view.x + view.width * sa.x / screenW, view.y + view.height * sa.y / screenH,
                            view.width * sa.width / screenW, view.height * sa.height / screenH);
            if (safe.width <= 0f || safe.height <= 0f) safe = view;
        }

        logo = Rect.zero;
        if (logoRenderer != null)
        {
            Bounds b = logoRenderer.bounds;
            logo = new Rect(b.min.x, b.min.y, b.size.x, b.size.y);
        }

        menu = Rect.zero;
        var rt = menuPanel as RectTransform;
        if (rt != null && screenW > 0 && screenH > 0)
        {
            rt.GetWorldCorners(corners);   // screen pixels for an overlay canvas
            Vector2 a = ScreenToWorld(corners[0]), b = ScreenToWorld(corners[2]);
            menu = Rect.MinMaxRect(Mathf.Min(a.x, b.x), Mathf.Min(a.y, b.y), Mathf.Max(a.x, b.x), Mathf.Max(a.y, b.y));
        }
    }

    Vector2 ScreenToWorld(Vector3 p)
    {
        return new Vector2(view.x + view.width * p.x / screenW, view.y + view.height * p.y / screenH);
    }

    // ------------------------------------------------------------------ loop

    void Update()
    {
        // Unscaled and clamped: the menu's moveBackGround may freeze
        // timeScale, and a resumed app must not leap ships across the sky.
        Step(Mathf.Min(Time.unscaledDeltaTime, .05f));
    }

    public void Step(float dt)
    {
        if (!built) Init();
        if (dt <= 0f) return;
        now += dt;
        RefreshGeometry(false);

        Populate();
        Matchmake();

        for (int i = 0; i < pool.Length; i++)
            if (pool[i].active) Fly(pool[i], dt);

        Separate(dt);
        DetectCrashes();

        for (int i = 0; i < pool.Length; i++)
            if (pool[i].active) Draw(pool[i], dt);

        TickTrails(dt);
        TickShards(dt);
        TickStars(dt);
        TickFx(dt);
    }

    // ------------------------------------------------------------- spawning

    void Populate()
    {
        for (int d = 0; d < 3; d++)
        {
            if (now < nextSpawnAt[d]) continue;
            if (CruisersIn((Depth)d) >= layerTargets[d] || ActiveCount >= maxShips) continue;
            nextSpawnAt[d] = now + Random.Range(respawnDelay.x, respawnDelay.y);

            var lead = Launch((Depth)d, false, false, null);
            // now and then a pair comes in together
            if (lead != null && d > 0 && now >= nextFormationAt && ActiveCount < maxShips)
            {
                if (Launch((Depth)d, false, false, lead) != null) Formations++;
                nextFormationAt = now + Random.Range(formationInterval.x, formationInterval.y);
            }
        }

        if (now >= nextZoomAt)
        {
            nextZoomAt = now + Random.Range(zoomInterval.x, zoomInterval.y);
            if (ActiveCount < maxShips)
                Launch(Random.value < .6f ? Depth.Front : Depth.Mid, false, true, null);
        }
    }

    Flyer FreeFlyer()
    {
        int n = pool.Length, start = Random.Range(0, n);
        for (int k = 0; k < n; k++)
        {
            var f = pool[(start + k) % n];
            if (!f.active && f.hull.sprite != null) return f;
        }
        return null;
    }

    // leader != null: launch as that ship's wingman.
    public Flyer Launch(Depth layer, bool insideView, bool zoomer, Flyer leader)
    {
        if (ActiveCount >= maxShips) return null;
        var f = FreeFlyer();
        if (f == null) return null;
        var spec = Depths[(int)layer];

        f.active = true;
        f.layer = layer;
        f.zoomer = zoomer;
        f.state = zoomer ? State.ZipIn : State.Cruise;
        f.stateT = 0f;
        f.scale = spec.scale * Random.Range(.92f, 1.08f);
        f.radius = ReferenceHull * f.scale * .42f;
        f.baseSpeed = spec.speed * Random.Range(.85f, 1.15f);
        f.speed = f.baseSpeed;
        f.speedMul = 1f;
        f.flame = IdleFlame;
        f.stretchX = 1f; f.stretchY = 1f;
        f.grow = 1f;
        f.zipFlying = false;
        f.nextZip = float.MaxValue;
        f.alpha = 1f;
        f.spin = 0f; f.spinVel = f.wind ? Random.Range(170f, 260f) * (Random.value < .5f ? -1f : 1f) : 0f;
        f.phase = Random.Range(0f, 6.283f);
        f.wobF = Random.Range(2.2f, 3.6f);
        f.wobA = ReferenceHull * f.scale * Random.Range(.04f, .08f);
        f.trick = Trick.None;
        f.partner = null;
        f.drift = Vector2.zero;
        f.pendingPop = false;
        f.born = now;
        f.nextBoost = now + Random.Range(2.5f, 8f);
        f.nextTrick = now + Random.Range(4f, 11f);
        f.turn = 0f;

        if (leader != null && leader.active)
        {
            f.layer = leader.layer;
            f.state = State.Formation;
            f.partner = leader;
            f.scale = leader.scale;
            f.radius = leader.radius;
            float side = Random.value < .5f ? -1f : 1f;
            f.wingOffset = new Vector2(-1.25f, 1.15f * side) * ReferenceHull * f.scale;
            f.heading = leader.heading;
            f.pos = leader.pos + Rotate(f.wingOffset, leader.heading);
            f.stateT = Random.Range(3f, 5f);   // time left in formation
            f.baseSpeed = leader.baseSpeed;
            f.speed = leader.speed;
        }
        else if (insideView)
        {
            f.pos = PickWaypoint(layer);
            f.heading = Random.Range(0f, 6.283f);
            f.waypoint = PickWaypoint(layer);
            f.waypointsLeft = Random.Range(0, 2);
        }
        else
        {
            EnterFromEdge(f);
        }

        ApplyLook(f);
        f.go.SetActive(true);
        if (zoomer) { Zooms++; AttachTrail(f); }
        Draw(f, 0f);
        return f;
    }

    void EnterFromEdge(Flyer f)
    {
        float m = ReferenceHull * f.scale + .4f;
        int edge = Random.value < .7f ? Random.Range(0, 2) : Random.Range(2, 4); // sides more often
        Vector2 p;
        switch (edge)
        {
            case 0: p = new Vector2(view.xMin - m, Random.Range(view.yMin + 1f, view.yMax - 1f)); break;
            case 1: p = new Vector2(view.xMax + m, Random.Range(view.yMin + 1f, view.yMax - 1f)); break;
            case 2: p = new Vector2(Random.Range(view.xMin, view.xMax), view.yMin - m); break;
            default: p = new Vector2(Random.Range(view.xMin, view.xMax), view.yMax + m); break;
        }
        f.pos = p;
        if (f.zoomer)
        {
            // The dock launch in reverse: streak in from offscreen along the
            // launch curve, braking hard into a spot clear of the logo.
            Vector2 rest = PickWaypoint(f.layer);
            Vector2 dIn = (rest - p).normalized;
            float bank = (Random.value < .5f ? -1f : 1f) * Random.Range(.4f, .9f);
            f.zipP0 = rest;
            f.zipP1 = rest - dIn * DockLaunch.ApproachDistance * f.scale;
            Vector2 away = -Rotate(dIn, bank);
            f.zipP2 = f.zipP1 + away * (RayExit(f.zipP1, away) + ReferenceHull * f.scale + 1.2f);
            f.zipDir = dIn;
            f.pos = f.zipP2;
            Vector2 t0 = -(Vector2)DockTween.BezierTangent(f.zipP0, f.zipP1, f.zipP2, 1f);
            f.heading = Mathf.Atan2(t0.y, t0.x);
            f.waypoint = rest;
            f.waypointsLeft = 0;
        }
        else
        {
            f.waypoint = PickWaypoint(f.layer);
            f.waypointsLeft = Random.Range(0, 2);
            Vector2 d = f.waypoint - p;
            f.heading = Mathf.Atan2(d.y, d.x) + Random.Range(-.35f, .35f);
        }
    }

    // A point inside the safe area. Front-layer lanes keep off the logo and
    // the menu column when they can (a few tries, then accept).
    Vector2 PickWaypoint(Depth layer)
    {
        Rect r = safe;
        float inset = .35f;
        Vector2 p = r.center;
        for (int tries = 0; tries < 10; tries++)
        {
            p = new Vector2(Random.Range(r.xMin + inset, r.xMax - inset), Random.Range(r.yMin + inset, r.yMax - inset));
            if (layer != Depth.Front) break;
            if (Overlaps(logo, p, .9f)) continue;
            if (tries < 7 && Overlaps(menu, p, .35f)) continue;
            break;
        }
        return p;
    }

    // Distance from p (inside or near the view) along unit d to the view's edge.
    float RayExit(Vector2 p, Vector2 d)
    {
        float tx = d.x > 1e-4f ? (view.xMax - p.x) / d.x : d.x < -1e-4f ? (view.xMin - p.x) / d.x : float.MaxValue;
        float ty = d.y > 1e-4f ? (view.yMax - p.y) / d.y : d.y < -1e-4f ? (view.yMin - p.y) / d.y : float.MaxValue;
        return Mathf.Max(0f, Mathf.Min(tx, ty));
    }

    Vector2 ExitPoint(Flyer f)
    {
        // carry on roughly the way it's going, out past the nearest far edge
        Vector2 dir = new Vector2(Mathf.Cos(f.heading), Mathf.Sin(f.heading));
        float reach = view.width + view.height;
        Vector2 p = f.pos + dir * reach;
        p.x = Mathf.Clamp(p.x, view.xMin - 2f, view.xMax + 2f);
        p.y = Mathf.Clamp(p.y, view.yMin - 2f, view.yMax + 2f);
        if (view.Contains(p)) p.x = dir.x >= 0f ? view.xMax + 2f : view.xMin - 2f;
        return p;
    }

    void ApplyLook(Flyer f)
    {
        var spec = Depths[(int)f.layer];
        int order = spec.sortBase + f.slot * SortSlots;
        f.hull.sortingOrder = order + 3;
        var mat = spec.haze && hazeMaterial != null ? hazeMaterial : spriteMaterial;
        if (mat != null) f.hull.sharedMaterial = mat;
        f.hull.color = Color.white;
        for (int i = 0; i < f.nozzles.Length; i++)
        {
            f.nozzles[i].sortingOrder = order + 2;
            if (mat != null) f.nozzles[i].sharedMaterial = mat;
        }
    }

    void Retire(Flyer f)
    {
        f.active = false;
        f.state = State.Idle;
        if (f.trail != null) ReleaseTrail(f);
        if (pursuerA == f || pursuerB == f) EndPursuit();
        for (int i = 0; i < StarPool; i++) if (stars[i].owner == f) { stars[i].owner = null; stars[i].sr.enabled = false; }
        f.go.SetActive(false);
        // a wingman whose leader leaves breaks off on its own
        for (int i = 0; i < pool.Length; i++)
            if (pool[i].active && pool[i].partner == f) BreakFormation(pool[i]);
    }

    // ---------------------------------------------------------------- flight

    void Fly(Flyer f, float dt)
    {
        var spec = Depths[(int)f.layer];
        f.stateT += dt;
        float want = f.heading;
        float turnRate = spec.turnRate;
        f.turn = 0f;

        switch (f.state)
        {
            case State.Formation:
                FlyFormation(f, dt);
                return;
            case State.Dizzy:
                FlyDizzy(f, dt);
                return;
            case State.Pursue:
                if (f.partner == null || !f.partner.active) { f.state = State.Cruise; break; }
                want = Mathf.Atan2(f.partner.pos.y - f.pos.y, f.partner.pos.x - f.pos.x);
                turnRate = 3.2f;
                f.speedMul = 1.35f;
                break;
            case State.Boost:
                TickBoost(f);
                break;
            case State.ZipIn:
                FlyZipIn(f, dt);
                return;
            case State.ZipOut:
                FlyZipOut(f, dt);
                return;
        }

        if (f.state == State.Cruise || f.state == State.Boost)
        {
            Vector2 to = f.waypoint - f.pos;
            want = Mathf.Atan2(to.y, to.x);
            if (to.sqrMagnitude < .8f * .8f)
            {
                if (f.waypointsLeft > 0) { f.waypointsLeft--; f.waypoint = PickWaypoint(f.layer); }
                else if (view.Contains(f.waypoint)) f.waypoint = ExitPoint(f);
            }
            if (f.state == State.Cruise) MaybeStartSomething(f);
        }

        // steering: turn toward the waypoint, plus a lazy weave
        float diff = Mathf.DeltaAngle(f.heading * Mathf.Rad2Deg, want * Mathf.Rad2Deg) * Mathf.Deg2Rad;
        float steer = Mathf.Clamp(diff * 1.6f, -turnRate, turnRate);
        if (!f.zoomer) steer += Mathf.Sin(now * .9f + f.phase) * .22f;

        if (f.trick == Trick.Loop)
        {
            // a full circle at 2 turns' worth of rate, then back on course
            steer = f.trickDir * 6.283f / 1.05f;
            f.trickT += dt;
            if (f.trickT >= 1.05f) f.trick = Trick.None;
        }
        else if (f.trick == Trick.Roll)
        {
            f.trickT += dt;
            if (f.trickT >= .5f) f.trick = Trick.None;
        }

        f.turn = steer;
        f.heading += steer * dt;
        f.speed = f.baseSpeed * f.speedMul;
        f.pos += new Vector2(Mathf.Cos(f.heading), Mathf.Sin(f.heading)) * (f.speed * dt);

        // out of the sky and heading away: back to the pool
        float m = ReferenceHull * f.scale + .9f;
        bool outside = f.pos.x < view.xMin - m || f.pos.x > view.xMax + m || f.pos.y < view.yMin - m || f.pos.y > view.yMax + m;
        if (outside && (now - f.born > 2f))
        {
            Vector2 toCenter = view.center - f.pos;
            if (f.pos.x < view.xMin - 3f || f.pos.x > view.xMax + 3f || f.pos.y < view.yMin - 3f || f.pos.y > view.yMax + 3f
                || Vector2.Dot(toCenter, new Vector2(Mathf.Cos(f.heading), Mathf.Sin(f.heading))) < 0f)
                Retire(f);
        }
    }

    void MaybeStartSomething(Flyer f)
    {
        if (now >= f.nextZip) { StartZip(f); return; }
        if (now >= f.nextBoost)
        {
            f.nextBoost = now + Random.Range(5f, 12f);
            // some take off like a ship leaving the dock, the rest just boost
            if (Random.value < .3f && InsideSafe(f.pos, .8f)) { StartZip(f); return; }
            if (Random.value < .4f && f.layer != Depth.Back || Random.value < .15f) { StartBoost(f); return; }
        }
        if (now >= f.nextTrick && f.trick == Trick.None)
        {
            f.nextTrick = now + Random.Range(8f, 16f);
            if (Random.value < .45f)
            {
                f.trickT = 0f;
                f.trickDir = Random.value < .5f ? -1f : 1f;
                if (Random.value < .45f) { f.trick = Trick.Loop; Loops++; }
                else { f.trick = Trick.Roll; Rolls++; }
            }
        }
    }

    public void StartBoost(Flyer f)
    {
        f.state = State.Boost;
        f.stateT = 0f;
        Boosts++;
    }

    // Boost, on the dock launch's curves: the anticipation back-off (the
    // ship eases off and its flame pulls in, a held squash), then the hard
    // eased acceleration with DockLaunch.Flare's exhaust flare and a stretch
    // along the flight line, then it settles back with a small overshoot.
    public const float BoostAnticipation = .3f, BoostBurst = .7f, BoostSettle = .5f;

    void TickBoost(Flyer f)
    {
        float t = f.stateT;
        if (t < BoostAnticipation)
        {
            float back = DockLaunch.BackOff(DockLaunch.UndockTime * t / BoostAnticipation);
            f.speedMul = 1f - .8f * back;
            f.flame = IdleFlame * (1f - .5f * back);
            f.stretchX = 1.04f; f.stretchY = .95f;
        }
        else if (t < BoostAnticipation + BoostBurst)
        {
            float g = (t - BoostAnticipation) / BoostBurst;
            // speed follows the slope of the launch's Flight(f) curve: hard and eased
            f.speedMul = .2f + 2.5f * Mathf.Pow(Mathf.Clamp01(g * 1.6f), DockLaunch.FlightExponent - 1f);
            f.flame = DockLaunch.Flare(g);
            f.stretchX = .95f; f.stretchY = 1.06f;
            if (f.trail == null) AttachTrail(f);
        }
        else if (t < BoostAnticipation + BoostBurst + BoostSettle)
        {
            float g = (t - BoostAnticipation - BoostBurst) / BoostSettle;
            float e = DockTween.InOutCubic(g);
            f.speedMul = Mathf.Lerp(2.7f, 1f, e);
            f.flame = Mathf.Lerp(DockLaunch.Flare(1f), IdleFlame, e);
            bool overshoot = g > .75f;
            f.stretchX = overshoot ? 1.03f : .98f; f.stretchY = overshoot ? .97f : 1.03f;
            if (g > .5f && f.trail != null) ReleaseTrail(f);
        }
        else
        {
            f.flame = IdleFlame; f.stretchX = 1f; f.stretchY = 1f; f.speedMul = 1f;
            f.state = State.Cruise;
        }
    }

    // ------------------------------------------------------- dock-style zips

    public const float ZipBrake = .3f;

    // Take off the way a ship leaves the space dock: brake to a pause, back
    // off and lift (DockLaunch.BackOff / Lift), bank, then accelerate hard
    // along the launch Bezier with the exhaust flaring and fly off-screen.
    public void StartZip(Flyer f)
    {
        f.state = State.ZipOut;
        f.stateT = 0f;
        f.trick = Trick.None;
        f.zipFlying = false;
        f.zipRest = f.pos;
        f.nextZip = float.MaxValue;
        f.zipBank = (Random.value < .5f ? -1f : 1f) * Random.Range(.5f, 1.1f);
        Zips++;
    }

    void FlyZipOut(Flyer f, float dt)
    {
        float t = f.stateT;
        Vector2 dir = new Vector2(Mathf.Cos(f.heading), Mathf.Sin(f.heading));
        if (t < ZipBrake)
        {
            // drift to a stop
            float a = t / ZipBrake;
            f.speedMul = 1f - DockTween.OutCubic(a);
            f.pos += dir * (f.baseSpeed * f.speedMul * dt);
            f.flame = Mathf.Lerp(IdleFlame, DockLaunch.UndockFlame(0f), a);
            f.zipRest = f.pos;
            return;
        }
        float u = t - ZipBrake;
        if (u < DockLaunch.UndockTime)
        {
            // the undock: ease back, lift, lean into the coming turn
            float lift = DockLaunch.Lift(u), back = DockLaunch.BackOff(u);
            f.speedMul = 0f;
            f.pos = f.zipRest - dir * (DockLaunch.BackOffDistance * 2.5f * f.scale * back);
            f.grow = 1f + DockLaunch.LiftGrow * lift;
            f.flame = DockLaunch.UndockFlame(lift);
            f.turn = f.zipBank * 1.6f * lift;
            return;
        }
        if (!f.zipFlying)
        {
            f.zipFlying = true;
            f.zipHeading = f.heading;
            f.zipP0 = f.pos;
            f.zipP1 = f.pos + dir * DockLaunch.ApproachDistance * f.scale;
            Vector2 d2 = Rotate(dir, f.zipBank);
            f.zipP2 = f.zipP1 + d2 * (RayExit(f.zipP1, d2) + ReferenceHull * f.scale + 1.2f);
        }
        float g = (u - DockLaunch.UndockTime) / DockLaunch.FlightTime;
        if (g >= 1f) { Retire(f); return; }
        FlightPose(f, dt, g, DockLaunch.Flight(g), 1f, g);
    }

    // Shared by both zips: place on the launch Bezier at path parameter u and
    // pose (heading along the path, growth, flare, exit stretch, streak).
    void FlightPose(Flyer f, float dt, float g, float u, float facing, float flare)
    {
        Vector2 prevPos = f.pos;
        f.pos = DockTween.Bezier(f.zipP0, f.zipP1, f.zipP2, u);
        Vector2 tangent = (Vector2)DockTween.BezierTangent(f.zipP0, f.zipP1, f.zipP2, u) * facing;
        float along = Mathf.Atan2(tangent.y, tangent.x);
        float prev = f.heading;
        f.heading = facing > 0f
            ? f.zipHeading + Mathf.DeltaAngle(f.zipHeading * Mathf.Rad2Deg, along * Mathf.Rad2Deg) * Mathf.Deg2Rad * DockLaunch.TurnIn(g)
            : along;
        f.turn = dt > 0f ? Mathf.DeltaAngle(prev * Mathf.Rad2Deg, f.heading * Mathf.Rad2Deg) * Mathf.Deg2Rad / dt : 0f;
        f.speed = dt > 0f ? (f.pos - prevPos).magnitude / dt : f.speed;
        f.grow = (1f + DockLaunch.LiftGrow) * DockLaunch.FlightGrow(flare);
        f.flame = DockLaunch.Flare(flare);
        bool stretched = flare > .55f;
        f.stretchX = stretched ? .95f : 1f; f.stretchY = stretched ? 1.06f : 1f;
        if (flare > .12f && f.trail == null) AttachTrail(f);
    }

    // Zoomers: the launch run backwards in space (offscreen -> spot), nose
    // first, braking along the mirrored curve, then the undock in reverse
    // (settle, shrink back down) -- a lazy drift, a pause, and off it zips.
    void FlyZipIn(Flyer f, float dt)
    {
        float g = f.stateT / DockLaunch.FlightTime;
        if (g < 1f)
        {
            FlightPose(f, dt, g, DockLaunch.Flight(1f - g), -1f, 1f - g);
            if (g > .75f && f.trail != null) ReleaseTrail(f);
            return;
        }
        if (f.trail != null) ReleaseTrail(f);
        float h = f.stateT - DockLaunch.FlightTime;
        if (h < DockLaunch.UndockTime)
        {
            float s = DockLaunch.UndockTime - h;
            float lift = DockLaunch.Lift(s);
            f.pos = f.zipP0 + f.zipDir * (DockLaunch.BackOffDistance * 2.5f * f.scale * (1f - DockLaunch.BackOff(s)));
            f.heading = Mathf.Atan2(f.zipDir.y, f.zipDir.x);
            f.grow = 1f + DockLaunch.LiftGrow * lift;
            f.flame = DockLaunch.UndockFlame(lift);
            f.stretchX = f.stretchY = 1f;
            f.turn = 0f;
            return;
        }
        f.grow = 1f;
        f.flame = IdleFlame;
        f.state = State.Cruise;
        f.stateT = 0f;
        f.speedMul = 1f;
        f.baseSpeed = Depths[(int)f.layer].speed * .45f;   // a lazy drift
        f.waypoint = PickWaypoint(f.layer);
        f.waypointsLeft = 1;
        f.nextZip = now + Random.Range(.8f, 2.2f);
        f.nextBoost = f.nextTrick = float.MaxValue;
    }

    void FlyFormation(Flyer f, float dt)
    {
        var lead = f.partner;
        f.stateT -= 2f * dt;   // stateT counts down here (Fly added dt)
        if (lead == null || !lead.active || lead.state == State.Dizzy || f.stateT <= 0f)
        {
            BreakFormation(f);
            return;
        }
        Vector2 target = lead.pos + Rotate(f.wingOffset, lead.heading);
        float k = 1f - Mathf.Exp(-7f * dt);
        Vector2 before = f.pos;
        f.pos = Vector2.Lerp(f.pos, target, k);
        float prev = f.heading;
        f.heading = Mathf.LerpAngle(f.heading * Mathf.Rad2Deg, lead.heading * Mathf.Rad2Deg, k) * Mathf.Deg2Rad;
        f.turn = (f.heading - prev) / dt;
        f.flame = lead.flame;
        f.stretchX = lead.stretchX; f.stretchY = lead.stretchY;
        f.speed = (f.pos - before).magnitude / dt;
    }

    void BreakFormation(Flyer f)
    {
        f.partner = null;
        f.state = State.Cruise;
        f.stateT = 0f;
        f.heading += (Random.value < .5f ? -1f : 1f) * Random.Range(.35f, .6f);
        f.waypoint = PickWaypoint(f.layer);
        f.waypointsLeft = 0;
        f.trick = Trick.Roll; f.trickT = 0f; Rolls++;   // peel off with a roll
        f.flame = IdleFlame; f.stretchX = f.stretchY = 1f; f.speedMul = 1f;
    }

    void FlyDizzy(Flyer f, float dt)
    {
        // tumbling off from the bump, slowing down, spinning less and less
        f.pos += f.drift * dt;
        f.drift *= Mathf.Exp(-1.6f * dt);
        f.spinVel = Mathf.Lerp(f.spinVel, Mathf.Sign(f.spinVel) * 160f, 1f - Mathf.Exp(-2f * dt));
        f.spin += f.spinVel * dt;
        f.flame = .15f;
        f.stretchX = f.stretchY = 1f;
        if (f.stateT < 1.15f) return;

        // the beat lands: pop, or shake it off and boost away
        ReleaseStars(f);
        if (f.pendingPop)
        {
            Explode(f, .75f);
            Retire(f);
            return;
        }
        f.spin = 0f;
        f.spinVel = f.wind ? 200f : 0f;
        f.heading = Mathf.Atan2(safe.center.y - f.pos.y, safe.center.x - f.pos.x) + Random.Range(-.6f, .6f);
        f.waypoint = ExitPoint(f);
        f.waypointsLeft = 0;
        StartBoost(f);
    }

    // Same-layer ships that aren't meant to crash swerve apart: a near miss.
    void Separate(float dt)
    {
        for (int i = 0; i < pool.Length; i++)
        {
            var a = pool[i];
            if (!a.active || a.state == State.Dizzy || a.state == State.ZipIn || a.state == State.ZipOut) continue;
            for (int j = i + 1; j < pool.Length; j++)
            {
                var b = pool[j];
                if (!b.active || b.layer != a.layer || b.state == State.Dizzy || b.state == State.ZipIn || b.state == State.ZipOut) continue;
                if (a.partner == b || b.partner == a) continue;
                if (CanCrash(a, b)) continue;
                Vector2 d = b.pos - a.pos;
                float near = (a.radius + b.radius) * 2.2f;
                if (d.sqrMagnitude > near * near || d.sqrMagnitude < 1e-6f) continue;
                float side = Cross(new Vector2(Mathf.Cos(a.heading), Mathf.Sin(a.heading)), d) > 0f ? -1f : 1f;
                if (a.state != State.Formation) a.heading += side * 1.8f * dt;
                if (b.state != State.Formation) b.heading -= side * 1.8f * dt;
            }
        }
    }

    bool CanCrash(Flyer a, Flyer b)
    {
        if (a.layer != b.layer) return false;          // depth reads right
        if (now < nextCrashAt) return false;           // rate limit
        if (a.state == State.Dizzy || b.state == State.Dizzy) return false;
        if (a.partner == b && a.state == State.Formation) return false;
        if (b.partner == a && b.state == State.Formation) return false;
        return true;
    }

    // Circle overlap within a layer; no physics engine.
    void DetectCrashes()
    {
        for (int i = 0; i < pool.Length; i++)
        {
            var a = pool[i];
            if (!a.active) continue;
            for (int j = i + 1; j < pool.Length; j++)
            {
                var b = pool[j];
                if (!b.active || !CanCrash(a, b)) continue;
                float r = a.radius + b.radius;
                if ((a.pos - b.pos).sqrMagnitude > r * r) continue;
                Vector2 at = (a.pos + b.pos) * .5f;
                if (!CrashSiteOk(a.layer, at)) continue;
                Crash(a, b, at);
                return;   // one per frame
            }
        }
    }

    bool CrashSiteOk(Depth layer, Vector2 at)
    {
        Rect s = safe;
        if (at.x < s.xMin + .3f || at.x > s.xMax - .3f || at.y < s.yMin + .3f || at.y > s.yMax - .3f) return false;
        if (Overlaps(logo, at, .5f)) return false;
        if (layer == Depth.Front && Overlaps(menu, at, .2f)) return false;
        return true;
    }

    void Crash(Flyer a, Flyer b, Vector2 at)
    {
        if (a.layer != b.layer) CrossLayerCrashes++;   // can't happen; the tests watch it
        crashTimes[Crashes % CrashLog] = now;
        crashLayers[Crashes % CrashLog] = (int)a.layer;
        crashSites[Crashes % CrashLog] = at;
        Crashes++;
        nextCrashAt = now + Random.Range(crashInterval.x, crashInterval.y);
        EndPursuit();

        // who blows up and who gets the dizzy beat
        if (Random.value < .5f) { var t = a; a = b; b = t; }

        var spec = Depths[(int)a.layer];
        Impact(at, a.layer, a.scale, a.id);
        Explode(a, 1f);
        Retire(a);

        if (b.wind || Random.value < .3f)
        {
            Explode(b, 1f);
            Retire(b);
        }
        else
        {
            if (b.trail != null) ReleaseTrail(b);
            b.state = State.Dizzy; b.grow = 1f; b.stretchX = b.stretchY = 1f;
            b.stateT = 0f;
            b.trick = Trick.None;
            b.partner = null;
            b.speedMul = 1f;
            Vector2 away = (b.pos - at).sqrMagnitude > 1e-6f ? (b.pos - at).normalized : Vector2.up;
            b.drift = away * spec.speed * 1.6f;
            b.spinVel = (Random.value < .5f ? -1f : 1f) * 900f;
            b.pendingPop = Random.value < .45f;
            AttachStars(b);
            DizzyBeats++;
        }
        nextSpawnAt[(int)a.layer] = Mathf.Max(nextSpawnAt[(int)a.layer], now + respawnDelay.x);
    }

    // Pick two cruisers on one layer and send them at each other.
    void Matchmake()
    {
        if (pursuerA != null)
        {
            if (now - pursuitStarted > 4f) { EndPursuit(); nextCrashAt = now + 1f; }
            return;
        }
        if (now < nextCrashAt) return;

        float roll = Random.value;
        int first = roll < .5f ? 1 : roll < .8f ? 0 : 2;
        for (int k = 0; k < 3; k++)
        {
            var layer = (Depth)((first + k) % 3);
            Flyer best1 = null, best2 = null;
            float bestD = float.MaxValue;
            for (int i = 0; i < pool.Length; i++)
            {
                var a = pool[i];
                if (!Matchable(a, layer)) continue;
                for (int j = i + 1; j < pool.Length; j++)
                {
                    var b = pool[j];
                    if (!Matchable(b, layer)) continue;
                    if (!CrashSiteOk(layer, (a.pos + b.pos) * .5f)) continue;
                    float d = (a.pos - b.pos).sqrMagnitude;
                    if (d < bestD) { bestD = d; best1 = a; best2 = b; }
                }
            }
            if (best1 == null) continue;
            pursuerA = best1; pursuerB = best2;
            best1.state = State.Pursue; best1.partner = best2; best1.trick = Trick.None;
            best2.state = State.Pursue; best2.partner = best1; best2.trick = Trick.None;
            pursuitStarted = now;
            return;
        }
    }

    bool Matchable(Flyer f, Depth layer)
    {
        return f.active && f.layer == layer && f.state == State.Cruise && !f.zoomer &&
               InsideSafe(f.pos, .6f);
    }

    void EndPursuit()
    {
        if (pursuerA != null && pursuerA.state == State.Pursue) { pursuerA.state = State.Cruise; pursuerA.partner = null; pursuerA.speedMul = 1f; }
        if (pursuerB != null && pursuerB.state == State.Pursue) { pursuerB.state = State.Cruise; pursuerB.partner = null; pursuerB.speedMul = 1f; }
        pursuerA = pursuerB = null;
    }

    // ------------------------------------------------------------- drawing

    void Draw(Flyer f, float dt)
    {
        var spec = Depths[(int)f.layer];
        Vector2 dir = new Vector2(Mathf.Cos(f.heading), Mathf.Sin(f.heading));
        Vector2 perp = new Vector2(-dir.y, dir.x);
        float bob = Mathf.Sin(now * f.wobF + f.phase) * f.wobA;
        Vector2 p = f.pos + perp * bob;
        f.tr.position = new Vector3(p.x, p.y, 0f);

        // lean into the turn and narrow a touch (a 2D bank)
        float turn = Mathf.Clamp(f.turn, -2.5f, 2.5f);
        float lean = f.state == State.Dizzy ? 0f : -turn * 5f;
        float bank = 1f - Mathf.Min(.22f, Mathf.Abs(turn) * .09f);
        if (f.trick == Trick.Roll)
        {
            // barrel roll, held on 2s: full width -> edge-on -> flipped -> back
            int pose = Mathf.FloorToInt(f.trickT / (2f * Tick));
            bank = Mathf.Cos(pose * (2f * Tick) / .5f * 6.283f);
            if (Mathf.Abs(bank) < .18f) bank = .18f * (bank < 0f ? -1f : 1f);
        }
        float angle = f.wind ? f.spin : f.heading * Mathf.Rad2Deg - 90f + lean + f.spin;
        f.tr.rotation = Quaternion.Euler(0f, 0f, angle);
        float k = f.normScale * f.scale;
        k *= f.grow;
        f.tr.localScale = new Vector3(k * bank * f.stretchX, k * f.stretchY, 1f);

        // front ships fade through the logo fast, and thin out over the menu
        float target = 1f;
        if (f.layer == Depth.Front)
        {
            float ext = ReferenceHull * f.scale * .5f;
            if (Overlaps(logo, p, ext)) target = .18f;
            else if (Overlaps(menu, p, ext * .5f)) target = .55f;
        }
        f.alpha = Mathf.MoveTowards(f.alpha, target, 8f * dt);
        Color c = f.hull.color;
        if (c.a != f.alpha) { c = Color.white; c.a = f.alpha; f.hull.color = c; }

        if (f.boost != null)
        {
            float fl = f.wind ? 0f : f.flame * Mathf.Clamp01((f.alpha - .2f) / .6f);
            f.boost.localScale = new Vector3(fl, fl, 1f);
        }

        if (f.trail != null)
        {
            Vector2 tail = p - dir * (ReferenceHull * f.scale * .45f);
            f.trail.hist[0] = new Vector3(tail.x, tail.y, 0f);
            f.trail.alpha = f.alpha;
        }
    }

    // ---------------------------------------------------------------- trails

    void AttachTrail(Flyer f)
    {
        for (int i = 0; i < TrailPool; i++)
        {
            var t = trails[i];
            if (t.InUse) continue;
            t.owner = f;
            t.releasing = false;
            t.count = 1;
            t.clock = 0f;
            t.width = .11f * f.scale;
            Vector2 dir = new Vector2(Mathf.Cos(f.heading), Mathf.Sin(f.heading));
            Vector2 tail = f.pos - dir * (ReferenceHull * f.scale * .45f);
            for (int h = 0; h < t.hist.Length; h++) t.hist[h] = new Vector3(tail.x, tail.y, 0f);
            var spec = Depths[(int)f.layer];
            int order = spec.sortBase + f.slot * SortSlots;
            var mat = spec.haze && hazeMaterial != null ? hazeMaterial : spriteMaterial;
            for (int s = 0; s < Trail.Segments; s++)
            {
                Setup(t.outer[s], order, Red, mat);
                Setup(t.mid[s], order + 1, Amber, mat);
                Setup(t.core[s], order + 1, Bone, mat);
            }
            f.trail = t;
            return;
        }
    }

    static void Setup(SpriteRenderer r, int order, Color c, Material mat)
    {
        r.sortingOrder = order;
        r.color = c;
        if (mat != null) r.sharedMaterial = mat;
    }

    void ReleaseTrail(Flyer f)
    {
        var t = f.trail;
        f.trail = null;
        if (t == null) return;
        t.owner = null;
        t.releasing = true;
    }

    void TickTrails(float dt)
    {
        for (int i = 0; i < TrailPool; i++)
        {
            var t = trails[i];
            if (!t.InUse) continue;
            t.clock += dt;
            if (t.owner != null)
            {
                var h = t.hist;
                if ((h[0] - h[1]).sqrMagnitude >= Trail.Spacing * Trail.Spacing)
                {
                    for (int k = h.Length - 1; k > 1; k--) h[k] = h[k - 1];
                    h[1] = h[0];
                    if (t.count < h.Length) t.count++;
                }
            }
            else if (t.clock >= Tick)
            {
                // a released streak drops a segment a tick from the far end
                t.clock = 0f;
                t.count--;
                if (t.count <= 1)
                {
                    t.releasing = false;
                    for (int s = 0; s < Trail.Segments; s++) { t.outer[s].enabled = false; t.mid[s].enabled = false; t.core[s].enabled = false; }
                    continue;
                }
            }
            // length pulses a little frame to frame, as the art guide's streak does
            float pulse = (Mathf.FloorToInt(now / (2f * Tick)) & 1) == 0 ? 1f : .86f;
            for (int s = 0; s < Trail.Segments; s++)
            {
                bool on = s + 1 < t.count;
                float taper = 1f - s / (float)Trail.Segments;
                Vector3 a = t.hist[s], b = t.hist[s + 1];
                if (s == t.count - 2) b = Vector3.Lerp(a, b, pulse);
                Segment(t.outer[s], on, a, b, t.width * taper, t.alpha * .9f);
                Segment(t.mid[s], on, a, b, t.width * taper * .55f, t.alpha);
                Segment(t.core[s], on && s < Trail.Segments / 2, a, b, t.width * taper * .22f, t.alpha);
            }
        }
    }

    static void Segment(SpriteRenderer r, bool on, Vector3 a, Vector3 b, float width, float alpha)
    {
        Vector3 d = b - a;
        float len = d.magnitude;
        if (!on || len < .0005f || alpha < .05f) { r.enabled = false; return; }
        r.enabled = true;
        var t = r.transform;
        t.position = (a + b) * .5f;
        t.rotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg);
        t.localScale = new Vector3(len + width * .5f, width, 1f);
        Color c = r.color;
        if (c.a != alpha) { c.a = alpha; r.color = c; }
    }

    // ------------------------------------------------------------ crash fx

    // The impact frame: a white star with the ship's energy-coloured flash.
    void Impact(Vector2 at, Depth layer, float scale, int ship)
    {
        var spec = Depths[(int)layer];
        float size = .85f * scale;
        Color energy = WeaponStyleTable.For(ship).energy * spec.fxTint;
        PlayFx(FlipbookFx.Mode.Flash, at, size * 1.3f, ship, energy, spec.fxSort);
    }

    // TargetExplosion's metal blast (flash + ring + cel explosion), at the
    // Small size for a reference hull and scaled with the layer, plus the
    // hull breaking into four spinning pieces.
    void Explode(Flyer f, float sizeK)
    {
        var spec = Depths[(int)f.layer];
        float size = TargetExplosion.WorldSizeFor(TargetExplosion.Size.Small, TargetExplosion.Kind.Metal) * f.scale * sizeK;
        Vector2 at = f.pos;
        Color energy = WeaponStyleTable.For(f.id).energy * spec.fxTint;
        PlayFx(FlipbookFx.Mode.Flash, at, size * 1.15f, f.id, energy, spec.fxSort);
        PlayFx(FlipbookFx.Mode.Ring, at, size * 1.35f, f.id, energy, spec.fxSort + 1);
        LastExplosion = PlayFx(FlipbookFx.Mode.Explosion, at, size, f.id, spec.fxTint, spec.fxSort + 3);
        Explosions++;
        Debris(f, spec);
    }

    FlipbookFx PlayFx(FlipbookFx.Mode mode, Vector2 at, float size, int ship, Color color, int order)
    {
        var book = WeaponFx.Flipbook();
        book.Play(mode, new Vector3(at.x, at.y, 0f), size, ship, TargetExplosion.Kind.Metal, color, 1f, order);
        // Drive it here on unscaled time and keep it where it went off: its
        // own Update runs on gameplay time and drifts with the game world.
        for (int i = 0; i < fxCount; i++) if (fx[i] == book) return book;
        if (fxCount < FxTrack)
        {
            book.enabled = false;
            fx[fxCount++] = book;
        }
        return book;
    }

    void TickFx(float dt)
    {
        for (int i = fxCount - 1; i >= 0; i--)
        {
            var book = fx[i];
            if (book != null && book.Active)
            {
                Vector3 p = book.transform.position;
                book.Tick(dt);
                if (book.Active) { book.transform.position = p; continue; }
            }
            if (book != null) book.enabled = true;
            fx[i] = fx[--fxCount];
            fx[fxCount] = null;
        }
    }

    void Debris(Flyer f, DepthSpec spec)
    {
        var mat = spec.haze && hazeMaterial != null ? hazeMaterial : spriteMaterial;
        int placed = 0;
        for (int i = 0; i < ShardPool && placed < 4; i++)
        {
            var s = shards[i];
            if (s.on) continue;
            var sprite = f.shards[placed];
            float ang = (placed * 90f + 45f + Random.Range(-25f, 25f)) * Mathf.Deg2Rad + f.heading;
            s.vel = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * Random.Range(.9f, 1.7f) * Mathf.Sqrt(f.scale);
            s.spin = Random.Range(420f, 900f) * (Random.value < .5f ? -1f : 1f);
            s.age = 0f;
            s.life = Random.Range(.55f, .8f);
            s.size = f.normScale * f.scale;
            s.on = true;
            s.sr.sprite = sprite;
            s.sr.sortingOrder = spec.fxSort + 2;
            if (mat != null) s.sr.sharedMaterial = mat;
            s.sr.color = Color.white;
            s.sr.enabled = sprite != null;
            s.sr.transform.position = new Vector3(f.pos.x, f.pos.y, 0f);
            s.sr.transform.rotation = f.tr.rotation;
            placed++;
        }
    }

    void TickShards(float dt)
    {
        for (int i = 0; i < ShardPool; i++)
        {
            var s = shards[i];
            if (!s.on) continue;
            s.age += dt;
            if (s.age >= s.life) { s.on = false; s.sr.enabled = false; continue; }
            var t = s.sr.transform;
            t.position += new Vector3(s.vel.x, s.vel.y, 0f) * dt;
            s.vel *= Mathf.Exp(-1.2f * dt);
            t.Rotate(0f, 0f, s.spin * dt);
            // shrink in held steps, cel style
            float k = s.age / s.life;
            float step = k < .5f ? 1f : k < .8f ? .7f : .4f;
            t.localScale = new Vector3(s.size * step, s.size * step, 1f);
        }
    }

    // --------------------------------------------------------- dizzy stars

    void AttachStars(Flyer f)
    {
        var spec = Depths[(int)f.layer];
        var mat = spec.haze && hazeMaterial != null ? hazeMaterial : spriteMaterial;
        int placed = 0;
        for (int i = 0; i < StarPool && placed < 3; i++)
        {
            var s = stars[i];
            if (s.owner != null) continue;
            s.owner = f;
            s.index = placed++;
            s.sr.sprite = WeaponArt.ExplosionFlash(1);
            s.sr.color = Amber;
            s.sr.sortingOrder = spec.sortBase + f.slot * SortSlots + 4;
            if (mat != null) s.sr.sharedMaterial = mat;
            s.sr.enabled = s.sr.sprite != null;
        }
    }

    void ReleaseStars(Flyer f)
    {
        for (int i = 0; i < StarPool; i++)
            if (stars[i].owner == f) { stars[i].owner = null; stars[i].sr.enabled = false; }
    }

    void TickStars(float dt)
    {
        for (int i = 0; i < StarPool; i++)
        {
            var s = stars[i];
            if (s.owner == null) continue;
            var f = s.owner;
            float r = ReferenceHull * f.scale * .55f;
            // orbit stepped on 2s so it reads as drawn, not tweened
            float t = Mathf.Floor(now / (2f * Tick)) * (2f * Tick);
            float a = t * 7f + s.index * 2.094f;
            var tr = s.sr.transform;
            tr.position = new Vector3(f.pos.x + Mathf.Cos(a) * r, f.pos.y + r * .5f + Mathf.Sin(a) * r * .45f, 0f);
            float size = .2f * f.scale * ((Mathf.FloorToInt(now / (3f * Tick)) + s.index) % 2 == 0 ? 1f : .7f);
            tr.localScale = new Vector3(size, size, 1f);
        }
    }

    // ------------------------------------------------------------- helpers

    static Vector2 Rotate(Vector2 v, float radians)
    {
        float c = Mathf.Cos(radians), s = Mathf.Sin(radians);
        return new Vector2(v.x * c - v.y * s, v.x * s + v.y * c);
    }

    static float Cross(Vector2 a, Vector2 b) { return a.x * b.y - a.y * b.x; }

    static bool Overlaps(Rect r, Vector2 p, float pad)
    {
        if (r.width <= 0f || r.height <= 0f) return false;
        return p.x > r.xMin - pad && p.x < r.xMax + pad && p.y > r.yMin - pad && p.y < r.yMax + pad;
    }

    bool InsideSafe(Vector2 p, float inset)
    {
        return p.x > safe.xMin + inset && p.x < safe.xMax - inset && p.y > safe.yMin + inset && p.y < safe.yMax - inset;
    }

    void OnDestroy()
    {
        // hand any explosions we were driving back to their own clock
        for (int i = 0; i < fxCount; i++) if (fx[i] != null) fx[i].enabled = true;
        fxCount = 0;
        if (hazeMaterial != null)
        {
            if (Application.isPlaying) Destroy(hazeMaterial);
            else DestroyImmediate(hazeMaterial);
        }
    }
}

public static class TitleScreenTrafficBootstrap
{
    [RuntimeInitializeOnLoadMethod]
    static void Init()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (scene.name != "startS4") return;
        if (Object.FindFirstObjectByType<TitleScreenTraffic>() != null) return;
        new GameObject("~TitleScreenTraffic").AddComponent<TitleScreenTraffic>();
    }
}
