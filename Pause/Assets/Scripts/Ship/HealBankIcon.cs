using UnityEngine;

// The banked green atoms (HealBank), shown at the ship's tail: a small green atom (the pickup's own drawing)
// beside the exhaust, level with the tail, with "x2" / "x3" under it when more than one is banked. It follows
// the ship, is hidden when the bank is empty or the ship is gone, and is placed (Place, pure) so it never covers
// the hull or the exhaust plumes: out past the widest of them on whichever side has room on screen.
// Not parented to the ship (a spinner's turning hull would swing it); behind the hearts and the hull's art.
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

    // Where the icon's centre goes: past the widest of the hull and its exhaust on the side with room on
    // `screen` (right first), level with the tail (the hull's bottom edge), the count line kept on screen.
    public static Vector3 Place(Bounds hull, bool hasExhaust, Bounds exhaust, Rect screen, float size, bool count)
    {
        float half = size * .5f, gap = size * GapOfIcon;
        float right = hull.max.x, left = hull.min.x;
        if (hasExhaust) { right = Mathf.Max(right, exhaust.max.x); left = Mathf.Min(left, exhaust.min.x); }
        float xr = right + gap + half, xl = left - gap - half;
        bool rightFits = xr + half <= screen.xMax, leftFits = xl - half >= screen.xMin;
        float x;
        if (rightFits) x = xr;
        else if (leftFits) x = xl;
        else x = screen.xMax - xr >= xl - screen.xMin ? Mathf.Min(xr, screen.xMax - half) : Mathf.Max(xl, screen.xMin + half);
        float low = screen.yMin + half + (count ? size * LabelOfIcon : 0f);
        float y = Mathf.Max(hull.min.y + half, low);
        y = Mathf.Min(y, Mathf.Max(low, screen.yMax - half));
        return new Vector3(x, y, hull.center.z);
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
        int order = (hull != null ? hull.sortingOrder : 0) - 1;
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
