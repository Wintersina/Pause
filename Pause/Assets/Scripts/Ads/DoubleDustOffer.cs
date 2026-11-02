using UnityEngine;
using UnityEngine.UI;

// The death panel's optional "watch an ad to double the star dust" button. Hangs off the panel's
// bottom edge like a tab (the panel's card stack is packed tight and fitted to the screen, so there
// is no free row inside it). Built only while AdsConfig.Enabled; shown only when a rewarded ad is
// loaded (no ad, no button), and gone once used. It doubles only what THIS run earned
// (StarDustLedger.Earned), and pays once per death (DoubleDustRules).
public class DoubleDustOffer : MonoBehaviour
{
    public const string ObjectName = "DoubleDustOffer";
    public const float Width = 420f, Height = 56f, Drop = 4f;

    DeathPanelView view;
    CanvasGroup group;
    Text label;
    Button button;
    long token;
    float earned;
    bool used, working;

    public static DoubleDustOffer Attach(DeathPanelView view)
    {
        if (!AdsConfig.Enabled || view == null || view.PracticeRun) return null;
        // Real runs only, with something earned, and Remove Ads owners per AdPolicy.
        float earned = StarDustLedger.IsActive ? StarDustLedger.Earned : 0f;
        if (!AdPolicy.DoubleDustOffered(AdFree.IsActive, !score.paysRealDust, earned)) return null;

        var go = new GameObject(ObjectName, typeof(RectTransform), typeof(CanvasGroup), typeof(Image), typeof(Button));
        go.transform.SetParent(view.Panel, false);
        var offer = go.AddComponent<DoubleDustOffer>();
        offer.view = view;
        offer.earned = earned;
        offer.token = System.DateTime.UtcNow.Ticks;
        offer.Build(go);
        return offer;
    }

    void Build(GameObject go)
    {
        var rt = (RectTransform)go.transform;
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(.5f, .5f);
        rt.sizeDelta = new Vector2(Width, Height);
        rt.anchoredPosition = new Vector2(0f, -(DeathPanelView.Height * .5f) - Drop);
        group = go.GetComponent<CanvasGroup>();

        var image = go.GetComponent<Image>();
        image.sprite = DeathPanelView.ButtonArt();
        image.type = Image.Type.Sliced;
        image.color = AkiraPalette.Amber;
        button = go.GetComponent<Button>();
        button.targetGraphic = image;
        button.transition = Selectable.Transition.None;
        button.onClick.AddListener(OnClick);

        var textGo = new GameObject("Label", typeof(RectTransform), typeof(Text));
        textGo.transform.SetParent(rt, false);
        var tr = (RectTransform)textGo.transform;
        tr.anchorMin = Vector2.zero; tr.anchorMax = Vector2.one;
        tr.offsetMin = new Vector2(12f, 0f); tr.offsetMax = new Vector2(-12f, 0f);
        label = textGo.GetComponent<Text>();
        label.font = view.ViewFont;
        label.fontSize = 22;
        label.fontStyle = FontStyle.Bold;
        label.alignment = TextAnchor.MiddleCenter;
        label.color = AkiraPalette.Ink;
        label.raycastTarget = false;
        label.resizeTextForBestFit = true;
        label.resizeTextMinSize = 14;
        label.resizeTextMaxSize = 22;
        label.text = Caption();
        SetVisible(false);
    }

    public static string Caption() { return "WATCH AN AD  DOUBLE STAR DUST"; }

    void SetVisible(bool v)
    {
        group.alpha = v ? 1f : 0f;
        group.interactable = v;
        group.blocksRaycasts = v;
    }

    void Update()
    {
        if (used) { SetVisible(false); return; }
        // No ad loaded: no button. Appears once the intro has settled and an ad is ready.
        SetVisible(view != null && view.IntroFinished && !working && AdManager.RewardedReady);
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
