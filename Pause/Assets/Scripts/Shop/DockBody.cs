using UnityEngine;

// The art of the Space selection, loaded once for the dock's life: the orange sun's flipbook (4096x3072, a 4x3 grid
// of 1024 px frames, row-major from the top-left; the layout is read from the texture) and the drifting asteroids,
// which are the game's EXISTING Space rocks (nothing is copied): the decorative drift field's iron / ice / molten /
// geode (SpaceRockTypes) and the hazard field's ore / crystal / ember / shard (SpaceHazardRockTypes), each a
// 4-frame blink strip whose accent pulses. The dock tumbles them by rotating the sprite.
public sealed class DockSpaceArt
{
    public const string SunPath = "Worlds/Dock/space_sun_anim";
    public const int SunFrames = 12;
    public const int Kinds = 8;
    // Seconds each of the sun's 12 frames shows (was 1/8 s, which read as boiling; now a calm 6 s loop).
    public static float SunFrameSeconds = .5f;

    public Sprite[] sun;
    public Sprite[][] rocks;
    Texture2D sunTex;

    public bool Ready { get { return sun != null && rocks != null; } }

    public static DockSpaceArt Load()
    {
        var a = new DockSpaceArt();
        a.sunTex = Resources.Load<Texture2D>(SunPath);
        if (a.sunTex != null)
        {
            var l = FlipbookGrid.Find(a.sunTex.width, a.sunTex.height, SunFrames);
            if (l.Valid) a.sun = FlipbookGrid.Cut(a.sunTex, l, "dock_sun");
        }
        var rocks = new Sprite[Kinds][];
        int n = 0;
        string[] drift = { "iron", "ice", "molten", "geode" };
        foreach (var key in drift)
            foreach (var t in SpaceRockTypes.All)
                if (t.key == key) rocks[n++] = t.Frames;
        foreach (var key in new[] { "ore", "crystal", "ember", "shard" })
            foreach (var t in SpaceHazardRockTypes.All)
                if (t.key == key) rocks[n++] = t.Frames;
        bool ok = n == Kinds;
        for (int i = 0; ok && i < Kinds; i++) ok = rocks[i] != null && rocks[i].Length > 0 && rocks[i][0] != null;
        if (ok) a.rocks = rocks;
        return a;
    }

    // The rock sprites belong to the game's rock types (shared with the drift and hazard fields): only the sun is ours.
    public void Destroy()
    {
        if (sun != null) foreach (var s in sun) if (s != null) BossUtil.Kill(s);
        sun = null; rocks = null;
        if (sunTex != null) Resources.UnloadAsset(sunTex);
        sunTex = null;
    }
}

// One destination on show in the dock: a planet (its rotation, two cross-faded frames) or Space's sun with its
// asteroids. Everything is drawn in unit space (the destination's diameter is 1) under `root`, whose scale is the
// diameter times the warp's scale, so the warp just scales / fades the whole thing. Renderers are built once
// (Space's lazily) and reused; a Tick allocates nothing.
public sealed class DockBody
{
    // A planet's frames are the whole sprite: the disc is DiscShare of it, so it is drawn this much bigger.
    static readonly float PlanetScale = 1f / PlanetFlipbooks.DiscShare;
    // Space: the sun's width and the asteroids (width, orbit half-width, orbit period s, kind, start angle, tumble s/turn),
    // all in shares of the destination's diameter. Eight kinds (the game's own Space rocks), several sizes and speeds,
    // bigger than in-game but not huge; the ellipses are tilted away from the screen so rocks pass behind and in
    // front of the sun. A negative tumble turns the other way.
    public const float SunWidth = .46f, OrbitFlatten = .42f;
    public struct Rock { public float width, orbit, period, start, tumble; public int kind; }
    public static readonly Rock[] Rocks =
    {
        new Rock { width = .14f, orbit = .33f, period = 23f, start = 0.4f, tumble = 41f,  kind = 0 },
        new Rock { width = .17f, orbit = .37f, period = 37f, start = 2.1f, tumble = -53f, kind = 1 },
        new Rock { width = .11f, orbit = .30f, period = 17f, start = 3.7f, tumble = 29f,  kind = 2 },
        new Rock { width = .16f, orbit = .40f, period = 44f, start = 5.0f, tumble = -47f, kind = 3 },
        new Rock { width = .19f, orbit = .35f, period = 31f, start = 1.2f, tumble = 61f,  kind = 4 },
        new Rock { width = .12f, orbit = .28f, period = 27f, start = 4.4f, tumble = -35f, kind = 5 },
        new Rock { width = .15f, orbit = .42f, period = 52f, start = 2.9f, tumble = 57f,  kind = 6 },
        new Rock { width = .13f, orbit = .31f, period = 20f, start = 5.7f, tumble = -43f, kind = 7 },
    };
    Rock[] rocks;

    public readonly Transform root;
    readonly SpriteRenderer pa, pb;            // the planet: this frame and the next, faded in over it
    SpriteRenderer sun;
    SpriteRenderer[] rockA;
    readonly DockSpaceArt art;
    readonly int order;

    // -1 empty, 0 Space, 1.. a planet world.
    public int World { get; private set; }
    public int HeldWorld { get; private set; }          // the planet world this body holds a PlanetFlipbooks reference to (0 none)
    Sprite[] frames;
    float clock, scaleMul = 1f, alpha = 1f, diameter = 1f;
    int lastA = -1;

    public DockBody(Transform parent, string name, int order, DockSpaceArt art)
    {
        this.art = art;
        this.order = order;
        World = -1;
        root = new GameObject(name).transform;
        root.SetParent(parent, false);
        pa = NewRenderer("A", order);
        pb = NewRenderer("B", order + 1);
        pa.transform.localScale = pb.transform.localScale = new Vector3(PlanetScale, PlanetScale, 1f);
        root.gameObject.SetActive(false);
    }

    SpriteRenderer NewRenderer(string name, int sortingOrder)
    {
        var go = new GameObject(name);
        go.transform.SetParent(root, false);
        var r = go.AddComponent<SpriteRenderer>();
        r.sortingOrder = sortingOrder;
        r.enabled = false;
        return r;
    }

    // The renderer a test / a fit check reads: the planet's sprite, or the sun.
    public SpriteRenderer Main { get { return World == 0 ? sun : pa; } }
    public bool Ready { get { return World == 0 ? (sun != null && sun.sprite != null) : (frames != null); } }
    public string SpriteName { get { var m = Main; return m != null && m.sprite != null ? m.sprite.name : null; } }
    public bool Showing { get { return root.gameObject.activeSelf; } }
    public float Alpha { get { return alpha; } }
    public float ScaleMul { get { return scaleMul; } }
    public int ShownRocks { get { int n = 0; if (World == 0 && rockA != null) foreach (var r in rockA) if (r.enabled) n++; return n; } }
    public SpriteRenderer Sun { get { return sun; } }
    public SpriteRenderer Pb { get { return pb; } }

    // Empties the body, giving its planet reference back.
    public void Clear()
    {
        if (HeldWorld > 0) PlanetFlipbooks.Release(HeldWorld);
        HeldWorld = 0;
        frames = null;
        World = -1;
        lastA = -1;
        pa.enabled = pb.enabled = false;
        if (sun != null) { sun.enabled = false; foreach (var r in rockA) r.enabled = false; }
        root.gameObject.SetActive(false);
    }

    // Space: the sun and the asteroids (needs the Space art).
    public bool ShowSpace()
    {
        Clear();
        if (art == null || !art.Ready) return false;
        BuildSpace();
        World = 0;
        pa.enabled = pb.enabled = false;
        sun.enabled = true;
        foreach (var r in rockA) r.enabled = true;
        root.gameObject.SetActive(true);
        Apply();
        return true;
    }

    // A planet world: asks for its art (a reference is held from now on); the frames arrive through Pump.
    // `requested` false while another sheet is still resident (the memory cap): Pump asks later.
    public void WantPlanet(int world)
    {
        Clear();
        World = world;
        root.gameObject.SetActive(false);
    }

    // Steps a wanted planet toward showing: asks for the art once the cap allows, picks the frames up when they land.
    // True once the planet is showing.
    public bool Pump(bool mayRequest)
    {
        if (World < 1) return Ready;
        if (frames != null) return true;
        if (HeldWorld == 0)
        {
            if (!mayRequest) return false;
            if (!PlanetFlipbooks.Request(World)) { return false; }
            HeldWorld = World;
        }
        var f = PlanetFlipbooks.Frames(World);
        if (f == null) return false;
        frames = f;
        pa.enabled = pb.enabled = true;
        root.gameObject.SetActive(true);
        lastA = -1;
        Apply();
        return true;
    }

    // A planet world whose art is already resident (a synchronous take: first show, tests).
    public bool ShowPlanetNow(int world)
    {
        WantPlanet(world);
        var f = PlanetFlipbooks.Acquire(world);
        if (f == null) { World = -1; return false; }
        HeldWorld = world;
        frames = f;
        pa.enabled = pb.enabled = true;
        root.gameObject.SetActive(true);
        lastA = -1;
        Apply();
        return true;
    }

    void BuildSpace()
    {
        if (sun != null) return;
        sun = NewRenderer("Sun", order);
        sun.sprite = art.sun[0];
        float k = SunWidth;
        sun.transform.localScale = new Vector3(k, k, 1f);
        rocks = Rocks;
        rockA = new SpriteRenderer[rocks.Length];
        for (int i = 0; i < rocks.Length; i++)
        {
            rockA[i] = NewRenderer("Rock" + i, order + 2);
            var sp = art.rocks[rocks[i].kind][0];
            float rs = rocks[i].width / Mathf.Max(.0001f, sp.bounds.size.x);
            rockA[i].transform.localScale = new Vector3(rs, rs, 1f);
            rockA[i].sprite = sp;
        }
    }

    public void SetLook(float scaleMul, float alpha, float diameter)
    {
        this.scaleMul = scaleMul;
        this.alpha = alpha;
        this.diameter = diameter;
        Apply();
    }

    void Apply()
    {
        float s = Mathf.Max(.0001f, diameter * scaleMul);
        root.localScale = new Vector3(s, s, 1f);
        float a = alpha;
        if (World >= 1 && frames != null)
        {
            int next; float b;
            int i = PlanetFlipbooks.FrameAt(clock, out next, out b);
            if (i != lastA) { pa.sprite = frames[i]; pb.sprite = frames[next]; lastA = i; }
            float d = DockPlanet.Dim;
            pa.color = new Color(d, d, d, a);
            pb.color = new Color(d, d, d, a * b);
        }
        else if (World == 0 && sun != null)
        {
            float d = DockPlanet.Dim;
            int sf = Mathf.FloorToInt(clock / Mathf.Max(.01f, DockSpaceArt.SunFrameSeconds)) % art.sun.Length;
            sun.sprite = art.sun[sf];
            sun.color = new Color(d, d, d, a);
            for (int i = 0; i < rocks.Length; i++)
            {
                var rk = rocks[i];
                float ang = rk.start + clock * 6.2832f / rk.period;
                float sx = Mathf.Cos(ang), sy = Mathf.Sin(ang);
                var p = new Vector3(sx * rk.orbit, sy * rk.orbit * OrbitFlatten, 0f);
                rockA[i].transform.localPosition = p;
                // the far half of the ellipse passes behind the sun
                int o = order + (sy > 0f ? -1 : 2);
                rockA[i].sortingOrder = o;
                rockA[i].transform.localRotation = Quaternion.Euler(0f, 0f, 360f * (clock / rk.tumble + rk.start));
                var sprites = art.rocks[rk.kind];
                rockA[i].sprite = sprites[Mathf.FloorToInt(clock * SpaceRockTypes.BlinkFps + rk.start * 3f) % sprites.Length];
                rockA[i].color = new Color(d, d, d, a);
            }
        }
    }

    // dt in unscaled seconds.
    public void Tick(float dt)
    {
        if (World < 0 || !root.gameObject.activeSelf) return;
        clock += dt;
        Apply();
    }

    public void Destroy()
    {
        Clear();
        if (root != null) Object.Destroy(root.gameObject);
    }
}
