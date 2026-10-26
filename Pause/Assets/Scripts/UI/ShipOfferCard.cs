using System;
using UnityEngine;
using UnityEngine.UI;

// The "SHIP OFFER" card: after a death, when the ship just flown has nothing
// left to upgrade and the next roster ship is affordable (ShipOfferStore), it
// offers that ship before the Flight Complete panel, in the same slot and
// look as the upgrade offer (UpgradeOfferCard, whose builders it shares).
//
// The ship is shown the way the Space Dock parks an unbought one: its stock
// hull drawn through the dock's own ship material (DockArt.ShipMaterial,
// Shaders/Resources/Dock/DockSprite) at _Silhouette 1 in DockBay.SilhouetteInk,
// so only the outline shape shows, sitting in a dock berth. Under it, the
// ship's name and the dock card's own text for its ULTIMATE and its secret
// POWER and what sets the power off (PowerPreview / ShipLoadoutTable), then
// the price and the balance it leaves.
//
// BUY spends through the dock's path (ShipOfferStore.Buy), then the ink fades
// into the hull's colours with a soft white lift (ShipOfferRules: reveal
// numbers), holds on ACQUIRED and closes. NOT NOW (or Back) declines into the
// shared backoff. Either way `onDone(bought)` builds the results panel.
// No bordered line boxes, no sounds; animation runs on unscaled time and the
// per-frame path only writes numbers (no allocations).
public class ShipOfferCard : MonoBehaviour
{
    public const string RootName = "ShipOffer";
    public const string BuyName = "ShipOfferBuy", DeclineName = "ShipOfferDecline";
    public const float Width = 640f, Height = 820f, GlowMargin = 20f;
    public const float BerthWidth = 300f, BerthHeight = 252f, HullSize = 156f;
    const float ButtonW = 270f, ButtonH = 96f;

    public static string Heading { get { return "SHIP OFFER"; } }
    public static string BuyLabel { get { return "BUY"; } }
    public static string DeclineLabel { get { return "NOT NOW"; } }
    public static string AcquiredLabel { get { return "ACQUIRED"; } }

    public static string NameLine(int ship) { return (ShipId.NameOf(ship) ?? "SHIP").ToUpperInvariant(); }

    public static string ReasonLine(int flown)
    {
        return NameLine(flown) + " IS FULLY UPGRADED";
    }

    // The dock card's lines (DockPopup.ApplyPreviewKind), word for word.
    public static string AttackName(int ship) { return PowerPreview.Name(ship, false); }
    public static string AttackLine(int ship) { return PowerPreview.Line(ship, false); }
    public static string PowerName(int ship) { return PowerPreview.Name(ship, true); }
    public static string PowerLine(int ship) { return PowerPreview.Line(ship, true); }
    public static string TriggerLine(int ship)
    {
        return "POWER FIRES  " + ShipLoadoutTable.TriggerLine(ShipLoadoutTable.For(ship)).ToUpperInvariant();
    }

    static int silhouetteId, inkId, flashId, saturationId;

    ShipOfferRules.Offer offer;
    Action<bool> onDone;
    Font font;
    CanvasGroup group;
    RectTransform panel;
    Image hull;
    Material hullMaterial;      // this card's copy of the dock ship material
    Text priceText, balanceText;
    float shownAt, revealAt = -1f;
    float ink = 1f, flash;
    bool finished, bought;

    public Button BuyButton { get; private set; }
    public Button DeclineButton { get; private set; }
    public Image Hull { get { return hull; } }
    public float InkAmount { get { return ink; } }
    public float FlashAmount { get { return flash; } }
    public bool Revealing { get { return revealAt >= 0f; } }
    public float RevealStartedAt { get { return revealAt; } }
    public float ShownAt { get { return shownAt; } }
    public bool Finished { get { return finished; } }
    public string PriceText { get { return priceText != null ? priceText.text : ""; } }
    // Whether the hull is drawn by the dock's silhouette shader (false only
    // where the shader is unsupported: then a flat ink tint stands in, as in DockBay).
    public bool UsesDockMaterial { get { return hullMaterial != null; } }

    // Builds the card under `canvasRoot`. onDone(bought) runs after it closes.
    public static ShipOfferCard Show(Transform canvasRoot, Font font, ShipOfferRules.Offer offer,
                                     float balance, Action<bool> onDone)
    {
        var old = canvasRoot.Find(RootName);
        if (old != null) DestroyImmediate(old.gameObject);

        var root = new GameObject(RootName, typeof(RectTransform), typeof(CanvasGroup));
        root.transform.SetParent(canvasRoot, false);
        var rootRt = (RectTransform)root.transform;
        UpgradeOfferCard.Stretch(rootRt);
        root.transform.SetAsLastSibling();

        var card = root.AddComponent<ShipOfferCard>();
        card.offer = offer;
        card.onDone = onDone;
        card.font = font;
        card.group = root.GetComponent<CanvasGroup>();
        card.Build(rootRt, balance);
        card.shownAt = Time.unscaledTime;
        card.Apply(0f);
        ShipOfferStore.MarkShown(offer.shipId);
        BackNavigator.Register(card, card.OnBack);
        return card;
    }

    void Build(RectTransform root, float balance)
    {
        // Dim the frozen run; the full-screen blocker also keeps taps off the game.
        var scrim = UpgradeOfferCard.NewImage("Scrim", root, null, AkiraPalette.WithAlpha(AkiraPalette.Night0, .72f));
        UpgradeOfferCard.Stretch(scrim.rectTransform);
        scrim.raycastTarget = true;

        var go = new GameObject("Panel", typeof(RectTransform));
        go.transform.SetParent(root, false);
        panel = (RectTransform)go.transform;
        UpgradeOfferCard.Place(panel, UpgradeOfferCard.Centered(0f, 0f, Width, Height));

        var frame = UpgradeOfferCard.NewImage("Frame", panel, UpgradeOfferCard.Load("dp_panel"), Color.white);
        frame.type = Image.Type.Sliced;
        frame.raycastTarget = true;
        UpgradeOfferCard.Place(frame.rectTransform, UpgradeOfferCard.Centered(0f, 0f, Width + 2f * GlowMargin, Height + 2f * GlowMargin));

        var slab = UpgradeOfferCard.NewImage("TitleSlab", panel, UpgradeOfferCard.Load("dp_slab"), Color.white);
        UpgradeOfferCard.Place(slab.rectTransform, UpgradeOfferCard.Centered(0f, 345f, 600f, 80f));
        var heading = NewText("Heading", Heading, 36, AkiraPalette.Bone);
        UpgradeOfferCard.Place(heading.rectTransform, UpgradeOfferCard.Centered(0f, 347f, 560f, 56f));
        UpgradeOfferCard.Outline(heading.gameObject, AkiraPalette.Ink, 2f);

        var reason = NewText("Reason", ReasonLine(offer.flownId), 18, AkiraPalette.Muted);
        UpgradeOfferCard.Fit(reason, 12, 18);
        UpgradeOfferCard.Place(reason.rectTransform, UpgradeOfferCard.Centered(0f, 287f, 560f, 26f));

        BuildShip();

        var title = NewText("Item", NameLine(offer.shipId), 42, AkiraPalette.Cyan);
        UpgradeOfferCard.Fit(title, 24, 42);
        UpgradeOfferCard.Place(title.rectTransform, UpgradeOfferCard.Centered(0f, -4f, 560f, 52f));
        UpgradeOfferCard.Outline(title.gameObject, AkiraPalette.Ink, 2f);

        // ULTIMATE (gold) and POWER (cyan) as in the dock card: bold name, wrapped line.
        Info("AttackName", "AttackLine", AttackName(offer.shipId), AttackLine(offer.shipId), DockArt.Gold, -46f);
        Info("PowerName", "PowerLine", PowerName(offer.shipId), PowerLine(offer.shipId), DockArt.Cyan, -136f);
        var trigger = NewText("PowerTrigger", TriggerLine(offer.shipId), 17, AkiraPalette.Muted);
        UpgradeOfferCard.Fit(trigger, 12, 17);
        UpgradeOfferCard.Place(trigger.rectTransform, UpgradeOfferCard.Centered(0f, -214f, 560f, 24f));

        priceText = NewText("Price", UpgradeOfferCard.PriceLine(offer.price), 36, AkiraPalette.Amber);
        UpgradeOfferCard.Place(priceText.rectTransform, UpgradeOfferCard.Centered(0f, -258f, 560f, 46f));
        UpgradeOfferCard.Outline(priceText.gameObject, AkiraPalette.Ink, 2f);

        balanceText = NewText("Balance", UpgradeOfferCard.BalanceLine(balance, offer.price), 20, AkiraPalette.Muted);
        UpgradeOfferCard.Place(balanceText.rectTransform, UpgradeOfferCard.Centered(0f, -294f, 560f, 28f));

        BuyButton = NewButton(BuyName, BuyLabel, AkiraPalette.Cyan, UpgradeOfferCard.Centered(-150f, -358f, ButtonW, ButtonH), Buy);
        DeclineButton = NewButton(DeclineName, DeclineLabel, AkiraPalette.Red, UpgradeOfferCard.Centered(150f, -358f, ButtonW, ButtonH), Decline);
    }

    // The berth and the hull in it, blacked out exactly as the dock parks an unbought ship.
    void BuildShip()
    {
        var berth = UpgradeOfferCard.NewImage("Berth", panel, DockArt.Get("bay"), Color.white);
        berth.preserveAspect = true;
        UpgradeOfferCard.Place(berth.rectTransform, UpgradeOfferCard.Centered(0f, 148f, BerthWidth, BerthHeight));

        hull = UpgradeOfferCard.NewImage("Hull", panel, ShipHullArt.Get(offer.shipId, ShipSkins.Stock, 0, 0), Color.white);
        hull.preserveAspect = true;
        hull.enabled = hull.sprite != null;   // never a flat ink square
        // centred on the berth's inner frame (its art sits a little above the middle)
        UpgradeOfferCard.Place(hull.rectTransform, UpgradeOfferCard.Centered(0f, 159f, HullSize, HullSize));
        var shared = DockArt.ShipMaterial;
        if (shared != null)
        {
            if (silhouetteId == 0)
            {
                silhouetteId = Shader.PropertyToID("_Silhouette");
                inkId = Shader.PropertyToID("_Ink");
                flashId = Shader.PropertyToID("_Flash");
                saturationId = Shader.PropertyToID("_Saturation");
            }
            // UI graphics take no property blocks: one copy for this card.
            hullMaterial = new Material(shared) { name = "ShipOfferHull", hideFlags = HideFlags.DontSave };
            hullMaterial.SetFloat(saturationId, 1f);
            hull.material = hullMaterial;
        }
        PaintHull(1f);
    }

    void Info(string nameName, string lineName, string name, string line, Color colour, float y)
    {
        var n = NewText(nameName, name, 26, colour);
        UpgradeOfferCard.Fit(n, 16, 26);
        UpgradeOfferCard.Place(n.rectTransform, UpgradeOfferCard.Centered(0f, y, 560f, 32f));
        var l = NewText(lineName, line, 19, AkiraPalette.Bone);
        UpgradeOfferCard.Fit(l, 13, 19);
        l.verticalOverflow = VerticalWrapMode.Truncate;
        UpgradeOfferCard.Place(l.rectTransform, UpgradeOfferCard.Centered(0f, y - 40f, 560f, 48f));
    }

    Text NewText(string name, string content, int size, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Text));
        go.transform.SetParent(panel, false);
        var text = go.GetComponent<Text>();
        text.font = font;
        text.text = content ?? "";
        text.fontSize = size;
        text.fontStyle = FontStyle.Bold;
        text.color = color;
        text.alignment = TextAnchor.MiddleCenter;
        text.horizontalOverflow = HorizontalWrapMode.Overflow;
        text.verticalOverflow = VerticalWrapMode.Overflow;
        text.raycastTarget = false;
        return text;
    }

    Button NewButton(string name, string label, Color accent, Rect rect, UnityEngine.Events.UnityAction click)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
        go.transform.SetParent(panel, false);
        var rt = (RectTransform)go.transform;
        UpgradeOfferCard.Place(rt, rect);
        var img = go.GetComponent<Image>();
        img.sprite = UpgradeOfferCard.Load("dp_button");
        img.type = Image.Type.Sliced;
        img.color = accent;
        img.raycastTarget = true;
        var button = go.GetComponent<Button>();
        button.targetGraphic = img;
        button.transition = Selectable.Transition.None;   // press feedback is DeathPanelPress
        button.onClick.AddListener(click);
        var text = NewText("Label", label, 28, AkiraPalette.Bone);
        text.transform.SetParent(rt, false);
        UpgradeOfferCard.Fit(text, 18, 28);
        UpgradeOfferCard.Stretch(text.rectTransform);
        UpgradeOfferCard.Outline(text.gameObject, AkiraPalette.Ink, 1.5f);
        var press = go.AddComponent<DeathPanelPress>();
        press.target = rt;
        return button;
    }

    // ---- actions ----

    public void Buy()
    {
        if (finished || Revealing) return;
        if (!ShipOfferStore.Buy(offer))
        {
            // The balance changed under the card: nothing bought, move on.
            Finish(false);
            return;
        }
        bought = true;
        BuyButton.gameObject.SetActive(false);
        DeclineButton.gameObject.SetActive(false);
        priceText.text = AcquiredLabel;
        priceText.color = AkiraPalette.Cyan;
        balanceText.text = "BALANCE  " + Mathf.FloorToInt(PlayerPrefs.GetFloat(StarDustLedger.CurrencyKey)).ToString("N0");
        revealAt = Time.unscaledTime;
    }

    public void Decline()
    {
        if (finished || Revealing) return;
        ShipOfferStore.Declined();
        Finish(false);
    }

    bool OnBack()
    {
        if (finished) return false;
        if (Revealing) StepReveal(float.MaxValue);
        else Decline();
        return true;
    }

    // Advances the reveal to `elapsed` seconds after BUY; closes after the hold.
    public void StepReveal(float elapsed)
    {
        if (!Revealing || finished) return;
        float t = elapsed / ShipOfferRules.RevealSeconds;
        PaintHull(ShipOfferRules.RevealInk(t), ShipOfferRules.RevealFlash(t));
        if (elapsed >= ShipOfferRules.RevealSeconds + ShipOfferRules.RevealHoldSeconds) Finish(bought);
    }

    void Finish(bool result)
    {
        finished = true;
        BackNavigator.Unregister(this);
        var done = onDone;
        ReleaseMaterial();
        if (Application.isPlaying) Destroy(gameObject);
        else DestroyImmediate(gameObject);
        if (done != null) done(result);
    }

    void PaintHull(float inkAmount, float flashAmount = 0f, float alpha = 1f)
    {
        ink = inkAmount;
        flash = flashAmount;
        if (hull == null) return;
        if (hullMaterial != null)
        {
            // The dock shader ignores the vertex alpha at full ink, so the
            // card's fade reaches the silhouette through the ink's alpha.
            Color c = DockBay.SilhouetteInk;
            c.a *= alpha;
            hullMaterial.SetFloat(silhouetteId, ink);
            hullMaterial.SetColor(inkId, c);
            hullMaterial.SetFloat(flashId, flash);
            hull.color = Color.white;
        }
        else
        {
            // No dock shader (unsupported device): the flat tint DockBay falls back to.
            Color c = Color.Lerp(Color.white, DockBay.SilhouetteInk, ink);
            c = Color.Lerp(c, Color.white, flash);
            hull.color = c;
        }
    }

    void Update() { Tick(Time.unscaledTime); }

    // One frame at unscaled time `now` (tests drive it directly).
    public void Tick(float now)
    {
        if (finished) return;
        if (Revealing) { StepReveal(now - revealAt); return; }
        float t = Mathf.Clamp01((now - shownAt) / ShipOfferRules.PopInSeconds);
        if (t < 1f || group.alpha < 1f) Apply(t);
    }

    void Apply(float t)
    {
        if (group != null) group.alpha = t;
        if (panel != null)
        {
            float s = Mathf.Lerp(.92f, 1f, 1f - (1f - t) * (1f - t));
            panel.localScale = new Vector3(s, s, 1f);
        }
        PaintHull(1f, 0f, t);
    }

    void OnDestroy()
    {
        BackNavigator.Unregister(this);
        ReleaseMaterial();
    }

    void ReleaseMaterial()
    {
        if (hullMaterial == null) return;
        if (hull != null) hull.material = null;
        if (Application.isPlaying) Destroy(hullMaterial);
        else DestroyImmediate(hullMaterial);
        hullMaterial = null;
    }
}
