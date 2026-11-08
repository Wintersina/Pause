using UnityEngine;
using UnityEngine.UI;

// The death panel's optional "watch an ad to double the star dust" button. It is a fourth button of the
// panel in the same style as REPLAY / MENU / SPACE DOCK (translucent dp_button plate, amber accent, bone label),
// attached just under the panel's frame so it never sits on the frame or rail art (the card stack is packed
// tight, so there is no free row inside the frame). Built only while AdsConfig.Enabled; once the intro has
// settled it shows dimmed and untappable while no rewarded ad is loaded, lit when one is, and is gone once used. It doubles only what THIS run earned,
// without the booster's share (DoubleDustRules.Payout), and pays once per death (DoubleDustRules).
public class DoubleDustOffer : MonoBehaviour
{
    public const string ObjectName = "DoubleDustOffer";
    public const float Width = 420f, Height = 72f, Gap = 8f;
    // Panel units from the frame's bottom edge (glow included) down to the button's bottom edge.
    public const float BelowFrame = Gap + Height;
    public const float MinFontSize = 14f, MaxFontSize = 24f, LabelInset = 14f;
    public const string Caption_ = "WATCH AD: 2x STAR DUST";
    // Panel-space rect (centre origin): directly under the frame.
    public static Rect PanelRect
    {
        get
        {
            float top = -(DeathPanelView.Height * .5f + DeathPanelView.GlowMargin) - Gap;
            return new Rect(-Width * .5f, top - Height, Width, Height);
        }
    }
    static readonly Color DimPlate = new Color(AkiraPalette.Muted.r, AkiraPalette.Muted.g, AkiraPalette.Muted.b, .75f);

    DeathPanelView view;
    CanvasGroup group;
    Text label;
    Image plate;
    bool lit = true;
    Button button;
    long token;
    float earned;
    bool used, working;

    public static DoubleDustOffer Attach(DeathPanelView view)
    {
        if (!AdsConfig.Enabled || view == null || view.PracticeRun) return null;
        // Real runs only, with something earned, and Remove Ads owners per AdPolicy.
        // What it pays is the unboosted base (DoubleDustRules.Payout): Earned already holds the booster's extra.
        float earned = StarDustLedger.IsActive ? DoubleDustRules.Payout(StarDustLedger.Earned, Boosters.RunBoostExtra) : 0f;
        if (!AdPolicy.DoubleDustOffered(AdFree.IsActive, !score.paysRealDust, earned)) return null;

        var go = new GameObject(ObjectName, typeof(RectTransform), typeof(CanvasGroup), typeof(Image), typeof(Button));
        go.transform.SetParent(view.Panel, false);
        var offer = go.AddComponent<DoubleDustOffer>();
        offer.view = view;
        offer.earned = earned;
        offer.token = PrefsRewardStore.NewDeathToken();
        offer.Build(go);
        return offer;
    }

    void Build(GameObject go)
    {
        var rt = (RectTransform)go.transform;
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(.5f, .5f);
        rt.sizeDelta = new Vector2(Width, Height);
        rt.anchoredPosition = PanelRect.center;
        group = go.GetComponent<CanvasGroup>();

        var image = go.GetComponent<Image>();
        image.sprite = DeathPanelView.ButtonArt();
        image.type = Image.Type.Sliced;
        image.color = AkiraPalette.Amber;
        plate = image;
        button = go.GetComponent<Button>();
        button.targetGraphic = image;
        button.transition = Selectable.Transition.None;
        button.onClick.AddListener(OnClick);

        var textGo = new GameObject("Label", typeof(RectTransform), typeof(Text));
        textGo.transform.SetParent(rt, false);
        var tr = (RectTransform)textGo.transform;
        tr.anchorMin = Vector2.zero; tr.anchorMax = Vector2.one;
        tr.offsetMin = new Vector2(LabelInset, 0f); tr.offsetMax = new Vector2(-LabelInset, 0f);
        label = textGo.GetComponent<Text>();
        label.font = view.ViewFont;
        label.fontSize = (int)MaxFontSize;
        label.fontStyle = FontStyle.Bold;
        label.alignment = TextAnchor.MiddleCenter;
        label.color = AkiraPalette.Bone;
        label.horizontalOverflow = HorizontalWrapMode.Overflow;
        label.verticalOverflow = VerticalWrapMode.Overflow;
        var outline = textGo.AddComponent<Outline>();
        outline.effectColor = AkiraPalette.Ink;
        outline.effectDistance = new Vector2(1.5f, -1.5f);
        label.raycastTarget = false;
        label.resizeTextForBestFit = true;
        label.resizeTextMinSize = (int)MinFontSize;
        label.resizeTextMaxSize = (int)MaxFontSize;
        label.text = Caption();
        SetVisible(false);
        SetLit(false);
    }

    public static string Caption() { return Caption_; }

    // Lit = an ad is loaded and the button works; otherwise a muted plate and a dim label.
    void SetLit(bool on)
    {
        if (on == lit) return;
        lit = on;
        plate.color = on ? AkiraPalette.Amber : DimPlate;
        label.color = on ? AkiraPalette.Bone : AkiraPalette.WithAlpha(AkiraPalette.Bone, .55f);
        group.interactable = on;
        group.blocksRaycasts = on;
    }

    void SetVisible(bool v)
    {
        group.alpha = v ? 1f : 0f;
        group.interactable = v && lit;
        group.blocksRaycasts = v && lit;
    }

    void Update()
    {
        if (used) { SetVisible(false); return; }
        // Appears once the intro has settled; dimmed and untappable until an ad is loaded (and while one plays).
        bool shown = view != null && view.IntroFinished;
        SetLit(!working && AdManager.RewardedReady);
        SetVisible(shown);
    }

    void OnClick()
    {
        if (used || working) return;
        working = true;
        bool started = AdManager.TryShowRewardedDouble(token, earned,
            extra => { used = true; if (view != null) view.AddDustWon(extra); },
            () => { working = false; });
        if (!started) working = false;
    }
}
