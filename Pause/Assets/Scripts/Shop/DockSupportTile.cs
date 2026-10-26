using UnityEngine;

// A "SUPPORT THE DEV" berth in the space dock's rack: one per support pack
// (SupportPurchase.Packs), parked in the slots after the ships. It shows the
// pack's icon, "SUPPORT THE DEV", what it pays and Play's localized price, and
// a tap starts that pack's purchase. SpaceDock shows a tile only while Play has
// a price for its pack (Android with a store), so iOS, the editor and a device
// without Play never see one.
//
// Art: the optional sprite Resources/Vfx/<productId>_ui (support_developer_850_ui,
// support_developer_2500_ui; drawn smaller for the smaller pack); the dock's own star-dust glyph when it is missing. Static
// once built: no Update, text is rebuilt only when the price changes.
public class DockSupportTile : MonoBehaviour
{
    public static string IconResource(SupportPurchase.Pack p) { return "Vfx/" + p.productId + "_ui"; }
    const float IconExtent = .64f;   // world units, the longest edge at full size

    public SupportPurchase.Pack pack;
    TextMesh title, amount, price;
    SpriteRenderer icon;
    string shownPrice;

    public static DockSupportTile Create(Transform parent, SupportPurchase.Pack pack, Font font)
    {
        var root = new GameObject("~DockSupport_" + pack.id);
        root.transform.SetParent(parent, false);
        var tile = root.AddComponent<DockSupportTile>();
        tile.Build(pack, font);
        return tile;
    }

    // How big the icon is drawn for a pack: the 2,500 pack reads bigger.
    public static float IconScale(SupportPurchase.Pack p)
    {
        return p.dust >= 2000 ? 1f : .74f;
    }

    void Build(SupportPurchase.Pack p, Font font)
    {
        pack = p;
        var plate = Sprite("Berth", DockArt.Get("bay"), Vector3.zero, DockBay.OrderBay);
        var art = Resources.Load<Sprite>(IconResource(p));
        bool fallback = art == null;
        icon = Sprite("Icon", fallback ? DockArt.Get("icon_dust") : art, new Vector3(0f, .17f, 0f), DockBay.OrderShip);
        if (icon.sprite != null)
        {
            float extent = Mathf.Max(icon.sprite.bounds.size.x, icon.sprite.bounds.size.y);
            float k = (fallback ? .5f : IconExtent) * IconScale(p) / Mathf.Max(extent, .0001f);
            icon.transform.localScale = new Vector3(k, k, 1f);
        }
        title = Text("Title", font, new Vector3(0f, -.2f, 0f), .075f, DockArt.Cyan);
        title.text = "SUPPORT THE DEV";
        amount = Text("Amount", font, new Vector3(0f, -.34f, 0f), .075f, DockArt.Gold);
        amount.text = p.label;
        price = Text("Price", font, new Vector3(0f, -.553f, 0f), .078f, AkiraPalette.WithAlpha(AkiraPalette.Bone, .95f));
        plate.sortingOrder = DockBay.OrderBay;
        Refresh();
    }

    SpriteRenderer Sprite(string name, Sprite sprite, Vector3 local, int order)
    {
        var go = new GameObject(name);
        go.transform.SetParent(transform, false);
        go.transform.localPosition = local;
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = sprite;
        sr.sortingOrder = order;
        return sr;
    }

    TextMesh Text(string name, Font font, Vector3 local, float height, Color color)
    {
        var go = new GameObject(name);
        go.transform.SetParent(transform, false);
        go.transform.localPosition = local;
        var text = go.AddComponent<TextMesh>();
        text.font = font;
        text.fontSize = 64;
        // TextMesh lines are fontSize * characterSize / 10 units tall.
        text.characterSize = height * 10f / 64f;
        text.anchor = TextAnchor.MiddleCenter;
        text.alignment = TextAlignment.Center;
        text.color = color;
        var renderer = go.GetComponent<MeshRenderer>();
        if (font != null) renderer.sharedMaterial = font.material;
        renderer.sortingOrder = DockBay.OrderLabel + 1;
        return text;
    }

    // Whether the tile is on show: its pack has a price and a store.
    public bool Shown { get { return pack != null && pack.Available; } }

    // Rebuilds the price line, only when it changed.
    public void Refresh()
    {
        string p = pack.Price ?? "";
        if (p == shownPrice) return;
        shownPrice = p;
        price.text = p;
    }

    public bool Contains(Vector3 world)
    {
        Vector3 local = transform.InverseTransformPoint(world);
        return Mathf.Abs(local.x) <= DockLayout.BaySize.x * .5f &&
               Mathf.Abs(local.y) <= DockLayout.BaySize.y * .5f;
    }

    public bool Tap() { return pack.RequestBuy(); }
}
