using UnityEngine;

// Shared asteroid art for every dock destination. Space has no sun or planet of its own.
public sealed class DockSpaceArt
{
    public const int Kinds = 8;
    public Sprite[][] rocks;

    public bool Ready { get { return rocks != null; } }

    public static DockSpaceArt Load()
    {
        var a = new DockSpaceArt();
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

    // Borrowed from the game's shared rock caches; no textures belong to the dock.
    public void Destroy() { rocks = null; }

}

// One destination on show: a turning planet, or empty Space. Background asteroids live in
// DockAsteroids, independently of these bodies, so changing worlds never removes or scales the drift field.
public sealed class DockBody
{
    static readonly float PlanetScale = 1f / PlanetFlipbooks.DiscShare;

    public readonly Transform root;
    readonly SpriteRenderer pa, pb;            // the planet: this frame and the next, faded in over it
    readonly MaterialPropertyBlock sphereBlock = new MaterialPropertyBlock();
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
    public SpriteRenderer Main { get { return World == 0 ? null : pa; } }
    public bool Ready { get { return World == 0 ? (art != null && art.Ready) : (frames != null); } }
    public string SpriteName { get { var m = Main; return m != null && m.sprite != null ? m.sprite.name : null; } }
    public bool Showing { get { return root.gameObject.activeSelf; } }
    public float Alpha { get { return alpha; } }
    public float ScaleMul { get { return scaleMul; } }
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
        root.gameObject.SetActive(false);
    }

    // Space: only the shared starfield and asteroid field, no central body.
    public bool ShowSpace()
    {
        Clear();
        if (art == null || !art.Ready) return false;
        World = 0;
        pa.enabled = pb.enabled = false;
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
        pa.enabled = true;
        pb.enabled = false;
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
        pa.enabled = true;
        pb.enabled = false;
        root.gameObject.SetActive(true);
        lastA = -1;
        Apply();
        return true;
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
            DockSphereMotion.Apply(pa, sphereBlock, i, b);
            pb.enabled = false;
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

// A persistent field across the entire dock view, behind both the destination and deck. Rocks drift across
// the screen and wrap only once fully outside it; each has its own blink, tumble, size and travel time.
// Its root never inherits a destination body's warp scale. All sprites are borrowed from the game's rock cache.
public sealed class DockAsteroids
{
    public struct Rock { public float width, period, start, tumble; public int kind; }
    public static readonly Rock[] Rocks =
    {
        new Rock { width = .14f, period = 63f, start = .04f, tumble = 41f, kind = 0 },
        new Rock { width = .17f, period = 87f, start = .21f, tumble = -53f, kind = 1 },
        new Rock { width = .11f, period = 57f, start = .37f, tumble = 29f, kind = 2 },
        new Rock { width = .16f, period = 94f, start = .50f, tumble = -47f, kind = 3 },
        new Rock { width = .19f, period = 71f, start = .62f, tumble = 61f, kind = 4 },
        new Rock { width = .12f, period = 77f, start = .74f, tumble = -35f, kind = 5 },
        new Rock { width = .15f, period = 102f, start = .89f, tumble = 57f, kind = 6 },
        new Rock { width = .13f, period = 60f, start = .97f, tumble = -43f, kind = 7 },
    };
    public readonly SpriteRenderer[] Renderers;
    readonly DockSpaceArt art;
    Rect view;
    float clock, diameter = 1f;

    public DockAsteroids(Transform parent, DockSpaceArt art)
    {
        this.art = art;
        Renderers = new SpriteRenderer[art.rocks != null ? Rocks.Length : 0];
        for (int i = 0; i < Renderers.Length; i++)
        {
            var go = new GameObject("DriftRock" + i);
            go.transform.SetParent(parent, false);
            var r = go.AddComponent<SpriteRenderer>();
            r.sortingOrder = DockPlanet.Order - 2;
            r.sprite = art.rocks[Rocks[i].kind][0];
            r.color = new Color(DockPlanet.Dim, DockPlanet.Dim, DockPlanet.Dim, 1f);
            r.enabled = false;
            Renderers[i] = r;
        }
    }

    public void Place(Rect view, float diameter)
    {
        this.view = view;
        this.diameter = diameter;
        Tick(0f);
    }

    public void Tick(float dt)
    {
        clock += dt;
        for (int i = 0; i < Renderers.Length; i++)
        {
            var r = Renderers[i];
            r.enabled = view.width > 0f && view.height > 0f;
            if (!r.enabled) continue;
            var rock = Rocks[i];
            var frames = art.rocks[rock.kind];
            r.sprite = frames[Mathf.FloorToInt(clock * SpaceRockTypes.BlinkFps + i) % frames.Length];
            float width = diameter * rock.width;
            float scale = width / Mathf.Max(.0001f, r.sprite.bounds.size.x);
            r.transform.localScale = new Vector3(scale, scale, 1f);
            // Padding covers even a rotated sprite: recycling is entirely beyond the visible camera edges.
            float pad = width;
            float direction = (i % 2 == 0) ? 1f : -1f;
            float x = Mathf.Repeat(rock.start + direction * clock / rock.period, 1f);
            float y = Mathf.Repeat(.13f + i * .381966f + clock / (rock.period * 3f), 1f);
            r.transform.position = new Vector3(Mathf.Lerp(view.xMin - pad, view.xMax + pad, x),
                                                Mathf.Lerp(view.yMin - pad, view.yMax + pad, y), 0f);
            r.transform.localRotation = Quaternion.Euler(0f, 0f, 360f * (clock / rock.tumble + rock.start));
        }
    }
}

// Shared shader/material; each destination supplies its clock through a reused property block.
public static class DockSphereMotion
{
    static Material material;
    static readonly int FrameId = Shader.PropertyToID("_Frame"), PhaseId = Shader.PropertyToID("_Phase");
    public static Material Material
    {
        get
        {
            if (material == null)
            {
                var shader = Resources.Load<Shader>("Dock/DockSphereMotion");
                if (shader != null) material = new Material(shader) { name = "DockSphereMotion" };
            }
            return material;
        }
    }
    public static void Apply(SpriteRenderer renderer, MaterialPropertyBlock block, int frame, float phase)
    {
        renderer.sharedMaterial = Material;
        block.SetFloat(FrameId, frame);
        block.SetFloat(PhaseId, phase);
        renderer.SetPropertyBlock(block);
    }
}
