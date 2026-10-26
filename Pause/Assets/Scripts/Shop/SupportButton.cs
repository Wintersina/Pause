using UnityEngine;
using UnityEngine.UI;

// The dock's "SUPPORT THE DEVELOPER" button (SupportPurchase): a small plate
// under the header showing Play's localized price and what it pays. It exists
// only while Play has a price for the product (Android with a store), so iOS,
// the editor and a device without Play never see it.
//
// Art: ONE optional sprite slot, Resources/Vfx/support_developer_2500_ui. When
// it is missing the plate is text only.
public class SupportButton : MonoBehaviour
{
    public const string Name = "~SupportButton";
    public const string IconResource = "Vfx/support_developer_2500_ui";
    public const float Width = 330f, Height = 46f, IconSize = 36f;

    Image icon;
    Text title, detail;

    public static SupportButton Build(Transform canvas)
    {
        var existing = SceneUtil.FindAny(Name);
        if (existing != null) return existing.GetComponent<SupportButton>();
        var go = new GameObject(Name, typeof(RectTransform), typeof(Image), typeof(Button));
        go.transform.SetParent(canvas, false);
        var rt = (RectTransform)go.transform;
        rt.anchorMin = rt.anchorMax = new Vector2(.5f, 1f);
        rt.pivot = new Vector2(.5f, .5f);
        rt.sizeDelta = new Vector2(Width, Height);
        var button = go.GetComponent<SupportButton>() ?? go.AddComponent<SupportButton>();
        button.Construct(go);
        button.Refresh();
        return button;
    }

    void Construct(GameObject go)
    {
        var plate = go.GetComponent<Image>();
        plate.sprite = DockArt.Get("button", 8f);
        plate.type = Image.Type.Sliced;
        plate.color = plate.sprite != null ? Color.white : AkiraPalette.WithAlpha(AkiraPalette.Night0, .8f);
        var btn = go.GetComponent<Button>();
        btn.targetGraphic = plate;
        var colors = btn.colors;
        colors.pressedColor = new Color(.8f, .8f, .8f, 1f);
        btn.colors = colors;
        btn.onClick.AddListener(() => SupportPurchase.RequestBuy());
        CelPress.AddTo(go);

        var font = DockArt.FindSceneFont();
        var iconRect = new GameObject("Icon", typeof(RectTransform), typeof(Image)).GetComponent<RectTransform>();
        iconRect.SetParent(transform, false);
        iconRect.anchorMin = iconRect.anchorMax = new Vector2(0f, .5f);
        iconRect.pivot = new Vector2(0f, .5f);
        iconRect.anchoredPosition = new Vector2(8f, 0f);
        iconRect.sizeDelta = new Vector2(IconSize, IconSize);
        icon = iconRect.GetComponent<Image>();
        icon.preserveAspect = true;
        icon.raycastTarget = false;
        var sprite = Resources.Load<Sprite>(IconResource);
        icon.sprite = sprite;
        icon.enabled = sprite != null;

        float left = sprite != null ? IconSize + 14f : 8f;
        title = MakeLabel("Title", font, 16, DockArt.Ink, new Vector2(left, 4f), new Vector2(-8f, 0f), true);
        detail = MakeLabel("Detail", font, 13, DockArt.Ink, new Vector2(left, 0f), new Vector2(-8f, -22f), false);
        title.text = "SUPPORT THE DEVELOPER";
        title.fontStyle = FontStyle.Bold;
    }

    Text MakeLabel(string name, Font font, int size, Color color, Vector2 offsetMin, Vector2 offsetMax, bool top)
    {
        var rt = new GameObject(name, typeof(RectTransform), typeof(Text)).GetComponent<RectTransform>();
        rt.SetParent(transform, false);
        rt.anchorMin = top ? new Vector2(0f, .5f) : new Vector2(0f, 0f);
        rt.anchorMax = top ? new Vector2(1f, 1f) : new Vector2(1f, .5f);
        rt.offsetMin = new Vector2(offsetMin.x, top ? 0f : 3f);
        rt.offsetMax = new Vector2(offsetMax.x, top ? -3f : 0f);
        var t = rt.GetComponent<Text>();
        t.font = font;
        t.fontSize = size;
        t.color = color;
        t.alignment = TextAnchor.MiddleCenter;
        t.resizeTextForBestFit = true;
        t.resizeTextMinSize = 8;
        t.resizeTextMaxSize = size;
        t.raycastTarget = false;
        return t;
    }

    // Play's price and the payout. Hidden without a price.
    public void Refresh()
    {
        bool show = SupportPurchase.Available;
        if (show) detail.text = SupportPurchase.Price + "  ·  +" + SupportPurchase.DustAmount.ToString("N0") + " star dust";
        if (gameObject.activeSelf == show) return;
        gameObject.SetActive(show);   // SpaceDock calls Refresh again when the price changes
        if (SpaceDock.Instance != null) SpaceDock.Instance.Relayout();   // the rack refits under the header
    }
}
