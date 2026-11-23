using UnityEngine;

// The planet behind the Space Dock: the DESTINATION of the start world the pilot has picked in the picker at the top of the
// dock (DockStartWorld / PlayerStartWorld). Each world shows the Space-backdrop planet that looks like it (a giant_XX cell of
// the anim_hires atlas, the same cut and the same BackdropPlanet shader the in-game backdrop turns); plain Space, where
// there is no landfall, shows one of Space's own hazard rocks instead, big and slowly tumbling. Drawn big and partly off the
// lower right of the lot, behind the translucent deck, above the menu backdrop's tile layers and below every lot piece (Order),
// dimmed so the deck lines and labels stay readable.
//
// A change of selection crossfades (FadeSeconds) between two layers; nothing is allocated per frame (the property blocks are
// reused, the cells are cut once on first use, whole-rect sprites, never tight meshes, never GetPixels).
//
// WORLD -> CELL, checked against each world's own planetfall planet (Worlds/<World>/Planetfall/<world>_planet) and the
// rendered dock: Frost giant_04 (ice-white swirls), Verdant giant_03 (green land and cloud), Ember giant_02 (black rock, lava
// seams), Tide giant_01 (ocean and cloud swirls), Storm giant_00 (dark storm bands), Desert giant_05 (orange dunes and mesas);
// Space (no landfall) shows the ember hazard rock.
public class DockPlanet : MonoBehaviour
{
    public const int Order = -100;               // above the backdrop (BackdropCatalog.BaseOrder -500 up), below the lane (0)
    public const float Dim = .72f;
    public const float TurnSeconds = 50f;        // per half turn, like a backdrop planet (PlanetTurnSecondsMin..Max)
    public const float RockTurnSeconds = 90f;    // a whole turn of the asteroid
    public const float FadeSeconds = .25f;
    public const int Cells = 6;                  // giant_00 .. giant_05 in the atlas
    public const int Worlds = 7;                 // Space, Frost, Verdant, Ember, Tide, Storm, Desert (WorldManager order)
    public const string RockName = "space_hazard_rock_ember";
    const string AtlasPath = "Worlds/Space/Backdrop/anim_hires";
    const string RockPath = "Worlds/Space/HazardRocks/" + RockName;
    const float RockShare = .86f;                // the drawn rock's share of its 512 px cell
    const float RockSize = .85f;                 // the rock's width as a share of the planet's diameter

    // The atlas cell for each world index (-1: Space, the rock).
    public static readonly int[] WorldCell = { -1, 4, 3, 2, 1, 0, 5 };

    [System.Serializable] class Cell { public string n; public int x, y, w, h; }
    [System.Serializable] class Sheet { public Cell[] sprites; public float pixelScale; public int sheetW, sheetH; }

    static readonly int IdMainTex = Shader.PropertyToID("_MainTex");
    static readonly int IdDisc = Shader.PropertyToID("_Disc");
    static readonly int IdSpin = Shader.PropertyToID("_Spin");

    sealed class Layer
    {
        public SpriteRenderer r;
        public Sprite sprite;
        public Vector4 disc;
        public bool sphere;
        public MaterialPropertyBlock mpb;   // made lazily: a MonoBehaviour field initializer cannot make native objects
    }

    public SpriteRenderer Renderer { get { return front != null ? front.r : null; } }
    public string CellName { get; private set; }
    public float Diameter { get; private set; }
    public int World { get; private set; }               // the world selection on show (0 = Space)
    public bool IsAsteroid { get { return front != null && front.sprite != null && !front.sphere; } }
    public Sprite ShownSprite { get { return front != null ? front.sprite : null; } }
    public bool Fading { get { return fade < 1f; } }
    public float FrontAlpha { get { return front != null ? front.r.color.a : 0f; } }

    Layer front, back;
    Material material, defaultMaterial;
    readonly Sprite[] cells = new Sprite[Cells];
    Texture2D atlas;
    Sheet sheet;
    Sprite rock;
    float turn, rate, rockAngle, fade = 1f;
    bool placed;

    // `world`: the start-world selection to show first (0 = Space: the asteroid).
    public static DockPlanet Create(Transform parent, int world)
    {
        var go = new GameObject("~DockPlanet");
        go.transform.SetParent(parent, false);
        var p = go.AddComponent<DockPlanet>();
        p.Build();
        p.Show(world, false);
        return p;
    }

    Layer NewLayer(string name)
    {
        var go = new GameObject(name);
        go.transform.SetParent(transform, false);
        var l = new Layer { r = go.AddComponent<SpriteRenderer>() };
        l.r.sortingOrder = Order;
        l.r.enabled = false;
        return l;
    }

    void Build()
    {
        front = NewLayer("Planet");
        back = NewLayer("PlanetOut");
        defaultMaterial = front.r.sharedMaterial;
        var shader = Resources.Load<Shader>(SpaceDirector.PlanetShader);
        if (shader != null) material = new Material(shader) { name = "DockPlanet" };
        atlas = WorldArtPreload.Load<Texture2D>(AtlasPath);
        var json = WorldArtPreload.Load<TextAsset>(AtlasPath);
        if (json != null) sheet = JsonUtility.FromJson<Sheet>(json.text);
        rate = (Random.value < .5f ? 1f : -1f) * Mathf.PI / TurnSeconds;
        turn = Random.Range(0f, 6.283f);
    }

    // The atlas cell `index` as a whole-rect sprite, cut once.
    Sprite CellSprite(int index)
    {
        if (cells[index] != null) return cells[index];
        if (atlas == null || sheet == null || sheet.sprites == null) return null;
        string want = "giant_" + index.ToString("00");
        Cell c = null;
        foreach (var s in sheet.sprites) if (s.n == want) c = s;
        if (c == null) return null;
        float fx = sheet.sheetW > 0 ? atlas.width / (float)sheet.sheetW : 1f;
        float fy = sheet.sheetH > 0 ? atlas.height / (float)sheet.sheetH : 1f;
        float scale = (sheet.pixelScale > 0f ? sheet.pixelScale : 1f) * fx;
        // (a whole-rect cut: FullRect, never a tight mesh)
        var sp = Sprite.Create(atlas, new Rect(c.x * fx, c.y * fy, c.w * fx, c.h * fy), new Vector2(.5f, .5f),
                               BackdropAtlas.PixelsPerUnit * scale, 0, SpriteMeshType.FullRect);
        sp.name = want;
        cells[index] = sp;
        return sp;
    }

    // Shows what the start-world selection `world` lands on: its planet, or for Space (and anything unknown) the asteroid.
    // A change crossfades; the first call, or `animate` false, sets it at once.
    public void Show(int world) { Show(world, true); }

    public void Show(int world, bool animate)
    {
        if (front == null) return;
        world = world >= 1 && world < Worlds ? world : 0;
        if (front.sprite != null && world == World) return;
        int cell = WorldCell[world];
        Sprite sp;
        if (cell >= 0) sp = CellSprite(cell);
        else { if (rock == null) rock = Resources.Load<Sprite>(RockPath); sp = rock; }
        if (sp == null) return;
        bool crossfade = animate && front.sprite != null;
        if (crossfade) { var t = front; front = back; back = t; }
        World = world;
        Configure(front, sp, cell >= 0 && material != null);
        CellName = sp.name;
        if (crossfade)
        {
            fade = 0f;
            SetAlpha(front, 0f);
            SetAlpha(back, 1f);
        }
        else
        {
            fade = 1f;
            back.r.enabled = false;
            back.sprite = null;
        }
        if (placed) Fit(front);
        Paint(front);
    }

    void Configure(Layer l, Sprite sp, bool sphere)
    {
        l.sprite = sp;
        l.sphere = sphere;
        l.r.sprite = sp;
        l.r.enabled = true;
        l.r.sharedMaterial = sphere ? material : defaultMaterial;
        if (sphere) l.disc = SpaceDirector.DiscOf(sp);
        else l.r.SetPropertyBlock(null);
        l.r.transform.localRotation = Quaternion.identity;
        SetAlpha(l, 1f);
    }

    void SetAlpha(Layer l, float a)
    {
        l.r.color = new Color(Dim, Dim, Dim, a);
    }

    // `centre` and `diameter` in world units (the drawn disc's width).
    public void Place(Vector2 centre, float diameter)
    {
        Diameter = diameter;
        placed = true;
        transform.position = new Vector3(centre.x, centre.y, 0f);
        if (front != null && front.sprite != null) Fit(front);
        if (back != null && back.sprite != null) Fit(back);
    }

    void Fit(Layer l)
    {
        if (l.sprite == null) return;
        float cellWidth = l.sprite.bounds.size.x;
        float k;
        if (l.sphere)
        {
            // the disc is the cell less its clear border (BackdropPlanet's _Disc)
            float discShare = Mathf.Max(.1f, l.disc.z * 2f * l.sprite.texture.width / l.sprite.textureRect.width);
            k = Diameter / (cellWidth * discShare);
        }
        else k = Diameter * RockSize / (cellWidth * RockShare);
        l.r.transform.localScale = new Vector3(k, k, 1f);
    }

    void Paint(Layer l)
    {
        if (l == null || !l.sphere || l.sprite == null) return;
        if (l.mpb == null) l.mpb = new MaterialPropertyBlock();
        l.mpb.Clear();
        l.mpb.SetTexture(IdMainTex, l.sprite.texture);
        l.mpb.SetVector(IdDisc, l.disc);
        l.mpb.SetFloat(IdSpin, turn);
        l.r.SetPropertyBlock(l.mpb);
    }

    // One step of the turn and the crossfade, dt in unscaled seconds (tests drive it directly).
    public void Tick(float dt)
    {
        if (front == null) return;
        turn += rate * dt;
        rockAngle += 360f / RockTurnSeconds * dt;
        if (fade < 1f)
        {
            fade = Mathf.Min(1f, fade + dt / FadeSeconds);
            SetAlpha(front, fade);
            SetAlpha(back, 1f - fade);
            if (fade >= 1f) { back.r.enabled = false; back.sprite = null; }
        }
        Paint(front);
        Paint(back);
        if (!front.sphere) front.r.transform.localRotation = Quaternion.Euler(0f, 0f, rockAngle);
        if (!back.sphere && back.sprite != null) back.r.transform.localRotation = Quaternion.Euler(0f, 0f, rockAngle);
    }

    void Update()
    {
        Tick(Time.unscaledDeltaTime);   // a run may have left timeScale at 0
    }

    void OnDestroy()
    {
        for (int i = 0; i < cells.Length; i++) BackdropAtlas.Kill(cells[i]);
        BackdropAtlas.Kill(material);
    }
}
