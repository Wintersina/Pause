using System;
using UnityEngine;
using UnityEngine.UI;

// The small "UPGRADE OFFER" card shown after a death and before the Flight
// Complete panel (playerIsDead), only when UpgradeOfferStore says so. One tap
// buys with star dust (price and the balance it leaves are on the card), one
// tap on NO THANKS goes straight on; either way `onDone` builds the results
// panel, so the score screen is never more than one tap away.
//
// Same look as the Flight Complete panel (its sprites, palette and press
// feedback), built on PopUpCanvas over a dimmed, frozen run; animation runs on
// unscaled time. No bordered line boxes.
public class UpgradeOfferCard : MonoBehaviour
{
    public const string RootName = "UpgradeOffer";
    public const string BuyName = "OfferBuy", DeclineName = "OfferDecline";
    public const float Width = 640f, Height = 470f, GlowMargin = 20f;
    const float ButtonW = 270f, ButtonH = 96f, PopIn = .2f;
    const string SpriteRoot = "DeathPanel/";

    public static string BuyLabel { get { return "BUY"; } }
    public static string DeclineLabel { get { return "NO THANKS"; } }

    public static string PriceLine(float price) { return "PRICE  " + Mathf.RoundToInt(price).ToString("N0"); }

    public static string BalanceLine(float balance, float price)
    {
        return "BALANCE  " + Mathf.FloorToInt(balance).ToString("N0") + "  >  " +
               Mathf.FloorToInt(Mathf.Max(0f, balance - price)).ToString("N0");
    }

    UpgradeOfferRules.Candidate offer;
    Action<bool> onDone;
    Font font;
    CanvasGroup group;
    RectTransform panel;
    float shownAt;
    bool finished;

    public Button BuyButton { get; private set; }
    public Button DeclineButton { get; private set; }

    // Builds the card under `canvasRoot`. onDone(bought) runs after it closes.
    public static UpgradeOfferCard Show(Transform canvasRoot, Font font, UpgradeOfferRules.Candidate offer,
                                        float balance, Action<bool> onDone)
    {
        var old = canvasRoot.Find(RootName);
        if (old != null) DestroyImmediate(old.gameObject);

        var root = new GameObject(RootName, typeof(RectTransform), typeof(CanvasGroup));
        root.transform.SetParent(canvasRoot, false);
        var rootRt = (RectTransform)root.transform;
        rootRt.anchorMin = Vector2.zero; rootRt.anchorMax = Vector2.one;
        rootRt.offsetMin = rootRt.offsetMax = Vector2.zero;
        root.transform.SetAsLastSibling();

        var card = root.AddComponent<UpgradeOfferCard>();
        card.offer = offer;
        card.onDone = onDone;
        card.font = font;
        card.group = root.GetComponent<CanvasGroup>();
        card.Build(rootRt, balance);
        card.shownAt = Time.unscaledTime;
        card.Apply(0f);
        return card;
    }

    void Build(RectTransform root, float balance)
    {
        // Dim the frozen run; the full-screen blocker also keeps taps off the game.
        var scrim = NewImage("Scrim", root, null, AkiraPalette.WithAlpha(AkiraPalette.Night0, .72f));
        Stretch(scrim.rectTransform);
        scrim.raycastTarget = true;

        var go = new GameObject("Panel", typeof(RectTransform));
        go.transform.SetParent(root, false);
        panel = (RectTransform)go.transform;
        Place(panel, Centered(0f, 0f, Width, Height));

        var frame = NewImage("Frame", panel, Load("dp_panel"), Color.white);
        frame.type = Image.Type.Sliced;
        frame.raycastTarget = true;
        Place(frame.rectTransform, Centered(0f, 0f, Width + 2f * GlowMargin, Height + 2f * GlowMargin));

        var slab = NewImage("TitleSlab", panel, Load("dp_slab"), Color.white);
        Place(slab.rectTransform, Centered(0f, 172f, 600f, 80f));
        var heading = NewText("Heading", panel, "UPGRADE OFFER", 36, AkiraPalette.Bone, TextAnchor.MiddleCenter);
        Place(heading.rectTransform, Centered(0f, 174f, 560f, 56f));
        Outline(heading.gameObject, AkiraPalette.Ink, 2f);

        var title = NewText("Item", panel, offer.title ?? "", 42, AkiraPalette.Cyan, TextAnchor.MiddleCenter);
        Fit(title, 24, 42);
        Place(title.rectTransform, Centered(0f, 82f, 560f, 56f));
        Outline(title.gameObject, AkiraPalette.Ink, 2f);

        var detail = NewText("Detail", panel, offer.detail ?? "", 22, AkiraPalette.Bone, TextAnchor.MiddleCenter);
        Fit(detail, 14, 22);
        Place(detail.rectTransform, Centered(0f, 32f, 560f, 40f));

        var price = NewText("Price", panel, PriceLine(offer.price), 38, AkiraPalette.Amber, TextAnchor.MiddleCenter);
        Place(price.rectTransform, Centered(0f, -36f, 560f, 52f));
        Outline(price.gameObject, AkiraPalette.Ink, 2f);

        var after = NewText("Balance", panel, BalanceLine(balance, offer.price), 22, AkiraPalette.Muted, TextAnchor.MiddleCenter);
        Place(after.rectTransform, Centered(0f, -80f, 560f, 32f));

        BuyButton = NewButton(BuyName, BuyLabel, AkiraPalette.Cyan, Centered(-150f, -166f, ButtonW, ButtonH), Buy);
        DeclineButton = NewButton(DeclineName, DeclineLabel, AkiraPalette.Red, Centered(150f, -166f, ButtonW, ButtonH), Decline);
    }

    Button NewButton(string name, string label, Color accent, Rect rect, UnityEngine.Events.UnityAction click)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
        go.transform.SetParent(panel, false);
        var rt = (RectTransform)go.transform;
        Place(rt, rect);
        var img = go.GetComponent<Image>();
        img.sprite = Load("dp_button");
        img.type = Image.Type.Sliced;
        img.color = accent;
        img.raycastTarget = true;
        var button = go.GetComponent<Button>();
        button.targetGraphic = img;
        button.transition = Selectable.Transition.None;   // press feedback is DeathPanelPress
        button.onClick.AddListener(click);
        var text = NewText("Label", rt, label, 28, AkiraPalette.Bone, TextAnchor.MiddleCenter);
        Fit(text, 18, 28);
        Stretch(text.rectTransform);
        Outline(text.gameObject, AkiraPalette.Ink, 1.5f);
        var press = go.AddComponent<DeathPanelPress>();
        press.target = rt;
        return button;
    }

    void Buy()
    {
        if (finished) return;
        // A failed buy (the balance changed under the card) just moves on.
        Finish(UpgradeOfferStore.Buy(offer));
    }

    void Decline()
    {
        if (finished) return;
        UpgradeOfferStore.Declined();
        Finish(false);
    }

    void Finish(bool bought)
    {
        finished = true;
        var done = onDone;
        Destroy(gameObject);
        if (done != null) done(bought);
    }

    void Update()
    {
        if (finished) return;
        Apply(Mathf.Clamp01((Time.unscaledTime - shownAt) / PopIn));
    }

    void Apply(float t)
    {
        if (group != null) group.alpha = t;
        if (panel != null)
        {
            float s = Mathf.Lerp(.92f, 1f, 1f - (1f - t) * (1f - t));
            panel.localScale = new Vector3(s, s, 1f);
        }
    }

    // ---- small builders (same as DeathPanelView's) ----

    static Sprite Load(string name) { return Resources.Load<Sprite>(SpriteRoot + name); }

    static Image NewImage(string name, Transform parent, Sprite sprite, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);
        var img = go.GetComponent<Image>();
        img.sprite = sprite;
        img.color = color;
        img.raycastTarget = false;
        return img;
    }

    Text NewText(string name, Transform parent, string content, int size, Color color, TextAnchor align)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Text));
        go.transform.SetParent(parent, false);
        var text = go.GetComponent<Text>();
        text.font = font;
        text.text = content;
        text.fontSize = size;
        text.fontStyle = FontStyle.Bold;
        text.color = color;
        text.alignment = align;
        text.horizontalOverflow = HorizontalWrapMode.Overflow;
        text.verticalOverflow = VerticalWrapMode.Overflow;
        text.raycastTarget = false;
        return text;
    }

    static void Fit(Text t, int min, int max)
    {
        t.horizontalOverflow = HorizontalWrapMode.Wrap;
        t.resizeTextForBestFit = true;
        t.resizeTextMinSize = min;
        t.resizeTextMaxSize = max;
    }

    static void Outline(GameObject go, Color color, float distance)
    {
        var o = go.GetComponent<Outline>() ?? go.AddComponent<Outline>();
        o.effectColor = color;
        o.effectDistance = new Vector2(distance, -distance);
    }

    static void Place(RectTransform rt, Rect r)
    {
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(.5f, .5f);
        rt.anchoredPosition = r.center;
        rt.sizeDelta = r.size;
    }

    static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = rt.offsetMax = Vector2.zero;
    }

    static Rect Centered(float x, float y, float w, float h)
    {
        return new Rect(x - w * .5f, y - h * .5f, w, h);
    }
}
