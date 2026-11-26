using UnityEngine;

// The banked heal atom follows the centre of the ship's tail, with its count beneath.
// At the bottom edge it moves above the hull if the full badge cannot fit below.
// Its foreground layer keeps it legible over exhaust without pushing it sideways.
[DefaultExecutionOrder(70)]
public class HealBankIcon : MonoBehaviour
{
    public const string ObjectName = "~HealBankIcon";
    public const float IconOfHull = .2f;       // the icon's edge, of the hull's longest edge (at least MinSize)
    public const float MinSize = .2f, MaxSize = .34f;
    public const float GapOfIcon = .35f;       // clear space to the hull / plumes, of the icon's edge
    public const float LabelOfIcon = .8f;      // the count line's height, of the icon's edge
    static readonly string[] Labels = { "", "", "x2", "x3" };

    static HealBankIcon instance;

    Transform ship;
    SpriteRenderer hull, icon;
    TextMesh label;
    MeshRenderer labelRenderer;
    int shipId, shown = -1;
    float size = MinSize, labelSize = -1f;
    Vector3 center;

    public static HealBankIcon For(Transform ship) { return instance != null && instance.ship == ship ? instance : null; }

    public static HealBankIcon Attach(GameObject shipGo)
    {
        if (instance != null && instance.ship == shipGo.transform) return instance;
        if (instance != null) { if (Application.isPlaying) Destroy(instance.gameObject); else DestroyImmediate(instance.gameObject); }
        var go = new GameObject(ObjectName);
        var b = go.AddComponent<HealBankIcon>();
        b.Init(shipGo.transform);
        instance = b;
        return b;
    }

    void Init(Transform shipTransform)
    {
        ship = shipTransform;
        hull = ship.GetComponent<SpriteRenderer>();
        shipId = ShipId.Of(ship.gameObject, ShipId.Equipped());
        var iconGo = new GameObject("Icon", typeof(SpriteRenderer));
        iconGo.transform.SetParent(transform, false);
        icon = iconGo.GetComponent<SpriteRenderer>();
        icon.sprite = HealAtom.IconSprite;
        icon.enabled = false;
        var labelGo = new GameObject("Count");
        labelGo.transform.SetParent(transform, false);
        label = labelGo.AddComponent<TextMesh>();
        label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        label.fontSize = 64;
        label.fontStyle = FontStyle.Bold;
        label.anchor = TextAnchor.MiddleCenter;
        label.alignment = TextAlignment.Center;
        label.color = PickupGlow.HealGreen;
        labelRenderer = labelGo.GetComponent<MeshRenderer>();
        if (label.font != null) labelRenderer.sharedMaterial = label.font.material;
        labelRenderer.enabled = false;
        Refresh();
    }

    void OnDestroy() { if (instance == this) instance = null; }

    void LateUpdate()
    {
        if (ship == null) { Destroy(gameObject); return; }
        Refresh();
    }

    // ---- what it shows (tests) ---------------------------------------------

    public bool Visible { get { return icon != null && icon.enabled; } }
    public bool CountShown { get { return labelRenderer != null && labelRenderer.enabled; } }
    public string CountText { get { return label != null ? label.text : ""; } }
    public SpriteRenderer IconRenderer { get { return icon; } }
    public float Size { get { return size; } }
    // Icon centre (world).
    public Vector3 Center { get { return center; } }
    public Bounds Footprint { get { return FootprintAt(center, size, HealBank.Count > 1); } }

    // The icon's box, and the count line under it when `count`.
    public static Bounds FootprintAt(Vector3 iconCentre, float size, bool count)
    {
        var b = new Bounds(new Vector3(iconCentre.x, iconCentre.y, 0f), new Vector3(size, size, 0f));
        if (count) b.Encapsulate(new Bounds(new Vector3(iconCentre.x, iconCentre.y - size * (.5f + LabelOfIcon * .5f), 0f),
                                            new Vector3(size, size * LabelOfIcon, 0f)));
        return b;
    }

    public static float SizeFor(Bounds hull)
    {
        return Mathf.Clamp(Mathf.Max(hull.size.x, hull.size.y) * IconOfHull, MinSize, MaxSize);
    }

    // Centre the bank under the tail; keep the complete count on screen.
    public static Vector3 Place(Bounds hull, bool hasExhaust, Bounds exhaust, Rect screen, float size, bool count)
    {
        float half = size * .5f, gap = size * GapOfIcon;
        float low = screen.yMin + half + (count ? size * LabelOfIcon : 0f);
        float high = Mathf.Max(low, screen.yMax - half);
        float y = hull.min.y - half - gap;
        if (y < low) y = hull.max.y + half + gap + (count ? size * LabelOfIcon : 0f);
        float x = Mathf.Clamp(hull.center.x, screen.xMin + half, Mathf.Max(screen.xMin + half, screen.xMax - half));
        return new Vector3(x, Mathf.Clamp(y, low, high), hull.center.z);
    }

    // The exhaust's box, kept as an offset from the ship and measured again only when the ship's scale changes
    // (ShipUiSlots.ExhaustBounds builds arrays: not for every frame). Every drawing of a hull shares its rect.
    Vector3 exOffset, exSize;
    float exScale = -1f;
    bool exHas;

    bool Exhaust(out Bounds world)
    {
        float s = ship.lossyScale.x;
        if (!Mathf.Approximately(s, exScale))
        {
            exScale = s;
            Bounds b;
            exHas = ShipUiSlots.ExhaustBounds(ship, shipId, out b);
            exOffset = b.center - ship.position;
            exSize = b.size;
        }
        world = new Bounds(ship.position + exOffset, exSize);
        return exHas;
    }

    public void Refresh()
    {
        if (ship == null || icon == null) return;
        int n = Mathf.Clamp(HealBank.Count, 0, HealBank.Max);
        bool on = n > 0 && ship.gameObject.activeInHierarchy && !buttonClicks.playerDied;
        icon.enabled = on;
        labelRenderer.enabled = on && n > 1;
        if (!on) return;
        Bounds hb = ShipUiSlots.HullBounds(ship, shipId);
        Bounds ex;
        bool hasEx = Exhaust(out ex);
        size = SizeFor(hb);
        center = Place(hb, hasEx, ex, ShipUiSlots.ScreenRect(null), size, n > 1);
        transform.position = center;
        var sprite = icon.sprite;
        float k = sprite != null ? size / Mathf.Max(1e-4f, Mathf.Max(sprite.bounds.size.x, sprite.bounds.size.y)) : 1f;
        icon.transform.localScale = new Vector3(k, k, 1f);
        int order = (hull != null ? hull.sortingOrder : 0) + 1;
        icon.sortingOrder = order;
        labelRenderer.sortingOrder = order;
        if (shown != n)
        {
            shown = n;
            label.text = Labels[n];
        }
        if (Mathf.Abs(size - labelSize) > 1e-4f)   // (a TextMesh rebuilds its mesh when touched: only when the size really changed)
        {
            labelSize = size;
            label.transform.localPosition = new Vector3(0f, -size * (.5f + LabelOfIcon * .5f), 0f);
            label.characterSize = size * LabelOfIcon * 10f / 64f;
        }
    }
}
