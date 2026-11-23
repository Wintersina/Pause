using UnityEngine;

// The planet behind the Space Dock: one of Space's own world planets (giant_00..05 of the anim_hires atlas, the same
// cut and the same BackdropPlanet shader the in-game backdrop turns), drawn big and partly off the lower right of the
// lot, behind the translucent deck. Not a bespoke vista: the art and the turn are the game's. It sits above the menu
// backdrop's tile layers and below every lot piece (Order), dimmed so the deck lines and labels stay readable.
public class DockPlanet : MonoBehaviour
{
    public const int Order = -100;               // above the backdrop (BackdropCatalog.BaseOrder -500 up), below the lane (0)
    public const float Dim = .72f;
    public const float TurnSeconds = 50f;        // per half turn, like a backdrop planet (PlanetTurnSecondsMin..Max)
    public const int Cells = 6;                  // giant_00 Storm, 01 Tide, 02 Ember, 03 Verdant, 04 Frost, 05 Desert
    const string AtlasPath = "Worlds/Space/Backdrop/anim_hires";

    [System.Serializable] class Cell { public string n; public int x, y, w, h; }
    [System.Serializable] class Sheet { public Cell[] sprites; public float pixelScale; public int sheetW, sheetH; }

    static readonly int IdMainTex = Shader.PropertyToID("_MainTex");
    static readonly int IdDisc = Shader.PropertyToID("_Disc");
    static readonly int IdSpin = Shader.PropertyToID("_Spin");

    public SpriteRenderer Renderer { get; private set; }
    public string CellName { get; private set; }
    public float Diameter { get; private set; }
    Sprite sprite;
    Material material;
    Vector4 disc;
    float turn, rate;
    MaterialPropertyBlock mpb;   // made lazily: a MonoBehaviour field initializer cannot make native objects

    public static DockPlanet Create(Transform parent, int cell)
    {
        var go = new GameObject("~DockPlanet");
        go.transform.SetParent(parent, false);
        var p = go.AddComponent<DockPlanet>();
        p.Build(Mathf.Abs(cell) % Cells);
        return p;
    }

    void Build(int cell)
    {
        Renderer = gameObject.AddComponent<SpriteRenderer>();
        Renderer.sortingOrder = Order;
        Renderer.color = new Color(Dim, Dim, Dim, 1f);
        var tex = WorldArtPreload.Load<Texture2D>(AtlasPath);
        var json = WorldArtPreload.Load<TextAsset>(AtlasPath);
        if (tex == null || json == null) { enabled = false; return; }
        var sheet = JsonUtility.FromJson<Sheet>(json.text);
        string want = "giant_" + cell.ToString("00");
        Cell c = null;
        if (sheet != null && sheet.sprites != null) foreach (var s in sheet.sprites) if (s.n == want) c = s;
        if (c == null) { enabled = false; return; }
        float fx = sheet.sheetW > 0 ? tex.width / (float)sheet.sheetW : 1f;
        float fy = sheet.sheetH > 0 ? tex.height / (float)sheet.sheetH : 1f;
        float scale = (sheet.pixelScale > 0f ? sheet.pixelScale : 1f) * fx;
        // (a whole-rect cut: FullRect, never a tight mesh)
        sprite = Sprite.Create(tex, new Rect(c.x * fx, c.y * fy, c.w * fx, c.h * fy), new Vector2(.5f, .5f),
                               BackdropAtlas.PixelsPerUnit * scale, 0, SpriteMeshType.FullRect);
        sprite.name = want;
        CellName = want;
        Renderer.sprite = sprite;
        var shader = Resources.Load<Shader>(SpaceDirector.PlanetShader);
        if (shader != null)
        {
            material = new Material(shader) { name = "DockPlanet" };
            Renderer.sharedMaterial = material;
            disc = SpaceDirector.DiscOf(sprite);
            turn = Random.Range(0f, 6.283f);
            rate = (Random.value < .5f ? 1f : -1f) * Mathf.PI / TurnSeconds;
            Paint();
        }
    }

    // `centre` and `diameter` in world units (the drawn disc's width).
    public void Place(Vector2 centre, float diameter)
    {
        Diameter = diameter;
        transform.position = new Vector3(centre.x, centre.y, 0f);
        if (sprite == null) return;
        // the disc is the cell less its clear border (BackdropPlanet's _Disc)
        float cellWidth = sprite.bounds.size.x;
        float discShare = Mathf.Max(.1f, disc.z * 2f * sprite.texture.width / sprite.textureRect.width);
        float k = diameter / (cellWidth * discShare);
        transform.localScale = new Vector3(k, k, 1f);
    }

    void Paint()
    {
        if (mpb == null) mpb = new MaterialPropertyBlock();
        mpb.Clear();
        mpb.SetTexture(IdMainTex, sprite.texture);
        mpb.SetVector(IdDisc, disc);
        mpb.SetFloat(IdSpin, turn);
        Renderer.SetPropertyBlock(mpb);
    }

    void Update()
    {
        if (material == null) return;
        turn += rate * Time.unscaledDeltaTime;   // a run may have left timeScale at 0
        Paint();
    }

    void OnDestroy()
    {
        BackdropAtlas.Kill(sprite);
        BackdropAtlas.Kill(material);
    }
}
