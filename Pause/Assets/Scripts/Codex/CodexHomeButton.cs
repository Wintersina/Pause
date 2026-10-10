using UnityEngine;
using UnityEngine.UI;

// The CODEX entry on the home screen (startS4): a fifth row in the main
// menu's UIPanel, under Credits, with a small "12/42 DISCOVERED" counter.
//
// Authored into startS4 as an empty RectTransform inside UIPanel carrying
// this component, so the panel's VerticalLayoutGroup sizes it like the other
// rows. Its look is copied at runtime from the scene's own CreditsButton
// (same Button colours, same Orbitron label style), so it can never drift
// from the home screen's buttons. The codex itself opens as a full-screen
// panel over the home page (CodexPanel).
public class CodexHomeButton : MonoBehaviour
{
    public const string Template = "CreditsButton";
    // Label takes the upper part of the row, the counter the lower.
    public const float LabelShare = .68f;

    Button button;
    Text label, counter;
    Font font;
    // The notification bubble: unclaimed achievements (25 star dust each),
    // pinned to the button's top-right corner. A small amber pixel disc with
    // the count in ink; hidden at 0, "9+" above 9. Art slot: a sprite named
    // Resources/Codex/cx_badge (13x13 px, point filtered) replaces the
    // procedural disc when Codex paints one.
    Image badge;
    Text badgeText;
    public const float BadgeSize = 32f, BadgeInset = 6f;
    public const int BadgeMax = 9;
    public const string BadgeArtSlot = "cx_badge";

    public Image Badge { get { return badge; } }
    public Text BadgeText { get { return badgeText; } }
    public bool BadgeVisible { get { return badge != null && badge.gameObject.activeSelf; } }

    public static string BadgeLabel(int n)
    {
        return n > BadgeMax ? BadgeMax + "+" : n.ToString();
    }

    static Sprite procBadge;

    // A 13x13 pixel disc: ink outline, amber fill, one shadow row along the
    // bottom. Point filtered, so it stays crisp at any scale.
    static Sprite BadgeSprite()
    {
        var art = CodexUi.CodexSprite(BadgeArtSlot);
        if (art != null) return art;
        if (procBadge != null) return procBadge;
        const int n = 13;
        var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, name = "cx_badge_proc" };
        float c = (n - 1) * .5f;
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float d = Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c));
                Color col = new Color(0, 0, 0, 0);
                if (d <= 6.3f) col = CodexPalette.Ink;
                if (d <= 5.2f) col = y <= 2 ? CodexPalette.SodiumShadow : CodexPalette.Amber;
                tex.SetPixel(x, y, col);
            }
        tex.Apply(false, false);
        procBadge = Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(.5f, .5f), n);
        procBadge.name = "cx_badge_proc";
        return procBadge;
    }

    public Button Button { get { return button; } }
    public Text Counter { get { return counter; } }
    public Text Label { get { return label; } }

    void Awake()
    {
        Build();
    }

    void OnEnable()
    {
        CodexPanel.Closed += Refresh;
        DeveloperUnlocks.Changed += Refresh;   // N/N while developer mode is on
        AchievementStore.Unlocked += OnAchievementChanged;
        AchievementStore.Claimed += Refresh;
        Refresh();
    }

    void OnDisable()
    {
        CodexPanel.Closed -= Refresh;
        DeveloperUnlocks.Changed -= Refresh;
        AchievementStore.Unlocked -= OnAchievementChanged;
        AchievementStore.Claimed -= Refresh;
    }

    // Idempotent; public so the edit-mode tests can build it without Play mode.
    public void Build()
    {
        if (button != null) return;

        var template = transform.parent != null ? transform.parent.Find(Template) : null;
        var templateImage = template != null ? template.GetComponent<Image>() : null;
        var templateButton = template != null ? template.GetComponent<Button>() : null;
        var templateText = template != null ? template.GetComponentInChildren<Text>(true) : null;

        var image = gameObject.GetComponent<Image>() ?? gameObject.AddComponent<Image>();
        if (templateImage != null)
        {
            image.sprite = templateImage.sprite;
            image.type = templateImage.type;
            image.color = templateImage.color;
        }
        image.raycastTarget = true;

        button = gameObject.GetComponent<Button>() ?? gameObject.AddComponent<Button>();
        if (templateButton != null)
        {
            button.transition = templateButton.transition;
            button.colors = templateButton.colors;
            button.navigation = templateButton.navigation;
        }
        button.targetGraphic = image;
        button.onClick.AddListener(OpenCodex);

        font = templateText != null ? templateText.font : CodexUi.FindFont();
        label = CodexUi.NewText("CodexText", transform, font, "Codex", 38, Color.white, TextAnchor.MiddleCenter);
        if (templateText != null)
        {
            label.fontSize = templateText.fontSize;
            label.fontStyle = templateText.fontStyle;
            label.color = templateText.color;
            label.alignment = templateText.alignment;
            label.lineSpacing = templateText.lineSpacing;
            foreach (var o in templateText.GetComponents<Outline>()) CodexUi.AddOutline(label.gameObject, o.effectColor, o.effectDistance.x);
        }
        label.horizontalOverflow = HorizontalWrapMode.Wrap;
        label.verticalOverflow = VerticalWrapMode.Truncate;
        label.resizeTextForBestFit = true;
        label.resizeTextMinSize = 22;
        label.resizeTextMaxSize = 38;
        var lrt = label.rectTransform;
        lrt.anchorMin = new Vector2(0f, 1f - LabelShare);
        lrt.anchorMax = Vector2.one;
        lrt.offsetMin = lrt.offsetMax = Vector2.zero;

        counter = CodexUi.NewText("Counter", transform, font, "", 16, CodexUi.Title, TextAnchor.UpperCenter);
        counter.horizontalOverflow = HorizontalWrapMode.Wrap;
        counter.verticalOverflow = VerticalWrapMode.Truncate;
        counter.resizeTextForBestFit = true;
        counter.resizeTextMinSize = 11;
        counter.resizeTextMaxSize = 16;
        CodexUi.AddOutline(counter.gameObject, CodexUi.Ink, 1.5f);
        var crt = counter.rectTransform;
        crt.anchorMin = Vector2.zero;
        crt.anchorMax = new Vector2(1f, 1f - LabelShare + .04f);
        crt.offsetMin = crt.offsetMax = Vector2.zero;

        // The bubble: only there while something is waiting to be collected.
        // It takes no touches (the whole row opens the codex).
        badge = CodexUi.NewImage("CollectBadge", transform, BadgeSprite(), Color.white);
        badge.raycastTarget = false;
        var brt = badge.rectTransform;
        brt.anchorMin = brt.anchorMax = new Vector2(1f, 1f);
        brt.pivot = new Vector2(1f, 1f);
        brt.anchoredPosition = new Vector2(-BadgeInset, -BadgeInset);
        brt.sizeDelta = new Vector2(BadgeSize, BadgeSize);
        badgeText = CodexUi.NewText("Count", brt, font, "", 18, CodexPalette.Ink, TextAnchor.MiddleCenter);
        badgeText.resizeTextForBestFit = true;
        badgeText.resizeTextMinSize = 10;
        badgeText.resizeTextMaxSize = 18;
        var trt = badgeText.rectTransform;
        trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one;
        trt.offsetMin = new Vector2(3f, 5f); trt.offsetMax = new Vector2(-3f, -1f);
        badge.gameObject.SetActive(false);

        Refresh();
    }

    void OnAchievementChanged(AchievementDef def) { Refresh(); }

    public static string CounterText()
    {
        return Codex.DiscoveredCount + "/" + Codex.Total + " DISCOVERED";
    }

    public void Refresh()
    {
        if (counter != null) counter.text = CounterText();
        if (badge != null)
        {
            int n = AchievementStore.ClaimableCount;
            badge.gameObject.SetActive(n > 0);
            badgeText.text = n > 0 ? BadgeLabel(n) : "";
        }
    }

    public void OpenCodex()
    {
        CodexPanel.Open(font);
    }
}
