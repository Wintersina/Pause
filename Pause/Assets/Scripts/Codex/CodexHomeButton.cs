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
    // The notification bubble: unclaimed achievements (25 star dust each) plus NEW codex entries,
    // riding the top-right corner of the "Codex" word (measured from the rendered text). A small amber pixel disc with
    // the count in ink; hidden at 0, "9+" above 9. Art slot: a sprite named
    // Resources/Codex/cx_badge (13x13 px, point filtered) replaces the
    // procedural disc when Codex paints one.
    Image badge;
    Text badgeText;
    public const float BadgeSize = 14f, BadgeOverlapX = 2f, BadgeOverlapY = 2f;
    // size/position are recomputed when the label's rendered text or the canvas scale changes
    string badgeSig;
    public const int BadgeMax = 9, BadgeFont = 11;
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
    public static Sprite BadgeSprite()
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
        Codex.NewChanged += Refresh;
        Refresh();
    }

    void OnDisable()
    {
        CodexPanel.Closed -= Refresh;
        DeveloperUnlocks.Changed -= Refresh;
        AchievementStore.Unlocked -= OnAchievementChanged;
        AchievementStore.Claimed -= Refresh;
        Codex.NewChanged -= Refresh;
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
        badge = CodexUi.NewImage("CollectBadge", label.transform, BadgeSprite(), Color.white);
        badge.raycastTarget = false;
        var brt = badge.rectTransform;
        brt.anchorMin = brt.anchorMax = new Vector2(.5f, .5f);
        brt.pivot = new Vector2(0f, 0f);
        brt.sizeDelta = new Vector2(BadgeSize, BadgeSize);
        badgeText = CodexUi.NewText("Count", brt, font, "", 18, CodexPalette.Ink, TextAnchor.MiddleCenter);
        badgeText.resizeTextForBestFit = false;
        badgeText.fontSize = BadgeFont;
        badgeText.horizontalOverflow = HorizontalWrapMode.Overflow;
        badgeText.verticalOverflow = VerticalWrapMode.Overflow;
        var trt = badgeText.rectTransform;
        trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one;
        trt.offsetMin = new Vector2(0f, 2f); trt.offsetMax = new Vector2(0f, 0f);
        badge.gameObject.SetActive(false);

        Refresh();
    }

    void OnAchievementChanged(AchievementDef def) { Refresh(); }

    public static string CounterText()
    {
        return Codex.DiscoveredCount + "/" + Codex.Total + " DISCOVERED";
    }

    // What the bubble counts: star dust waiting to be claimed plus NEW codex entries
    // whose tab has not been opened yet.
    public static int Total()
    {
        return AchievementStore.ClaimableCount + Codex.UnackedNewCount;
    }

    public void Refresh()
    {
        if (counter != null) counter.text = CounterText();
        if (badge != null)
        {
            int n = Total();
            badge.gameObject.SetActive(n > 0);
            badgeText.text = n > 0 ? BadgeLabel(n) : "";
            PlaceBadge(true);
        }
    }

    void LateUpdate()
    {
        if (badge != null && badge.gameObject.activeSelf) PlaceBadge(false);
    }

    void OnRectTransformDimensionsChange() { badgeSig = null; }

    static TextGenerator badgeGen;

    // Rendered extent of the label's text in the label's local space: the
    // right edge of the last glyph, the top of the tallest glyph and the top
    // of the last glyph. False when there is nothing to measure.
    public bool MeasureWord(out float right, out float top, out float lastTop)
    {
        right = top = lastTop = 0f;
        if (label == null || string.IsNullOrEmpty(label.text)) return false;
        var rect = label.rectTransform.rect.size;
        if (rect.x <= 0f || rect.y <= 0f) return false;
        if (badgeGen == null) badgeGen = new TextGenerator();
        float ppu = Mathf.Max(.01f, label.pixelsPerUnit);
        var settings = label.GetGenerationSettings(rect);
        badgeGen.Populate(label.text, settings);
        var v = badgeGen.verts;
        int chars = badgeGen.characterCount;
        if (v.Count < 4 || chars < 1) return false;
        // 4 verts per visible glyph; the last visible one is the last letter
        int glyphs = v.Count / 4;
        right = float.MinValue; top = float.MinValue; lastTop = 0f;
        for (int g = 0; g < glyphs; g++)
        {
            float gx = float.MinValue, gy = float.MinValue;
            for (int k = 0; k < 4; k++) { gx = Mathf.Max(gx, v[g * 4 + k].position.x); gy = Mathf.Max(gy, v[g * 4 + k].position.y); }
            if (g == glyphs - 1) { right = gx / ppu; lastTop = gy / ppu; }
            top = Mathf.Max(top, gy / ppu);
        }
        return true;
    }

    // Integer pixel multiple of the 13 px art at the canvas scale, nearest to `units` (at least 1x).
    public static float SnapToArt(Component c, float units)
    {
        var canvas = c != null ? c.GetComponentInParent<Canvas>() : null;
        float scale = canvas != null ? Mathf.Max(.01f, canvas.rootCanvas.scaleFactor) : 1f;
        if (scale < .7f) return units;   // canvas not scaled yet (edit-mode tests)
        int mult = Mathf.Max(1, Mathf.RoundToInt(units * scale / 13f));
        return mult * 13f / scale;
    }

    float BadgeUnits() { return SnapToArt(this, BadgeSize); }

    // Pin the bubble to the top-right of the word: its bottom-left corner sits
    // just inside the last letter's top-right, so it rides the cap line and
    // never lands on the letter body.
    public void PlaceBadge(bool force)
    {
        if (badge == null || label == null) return;
        var canvas = GetComponentInParent<Canvas>();
        float scale = canvas != null ? canvas.rootCanvas.scaleFactor : 1f;
        var lr = label.rectTransform.rect;
        string sig = label.text + "|" + lr.size + "|" + scale + "|" + label.cachedTextGenerator.fontSizeUsedForBestFit;
        if (!force && sig == badgeSig) return;
        float right, top, lastTop;
        if (!MeasureWord(out right, out top, out lastTop)) return;
        badgeSig = sig;
        float size = BadgeUnits();
        var brt = badge.rectTransform;
        brt.sizeDelta = new Vector2(size, size);
        // label-local point just above the last letter's top-right corner
        var local = new Vector3(right - BadgeOverlapX, lastTop - BadgeOverlapY, 0f);
        // keep it inside the button: on short rows the cap line is near the top edge, so
        // slide it down and, to stay off the letter, right of the word's last glyph
        var btn = (RectTransform)transform;
        float topLimit = label.rectTransform.InverseTransformPoint(transform.TransformPoint(new Vector3(0f, btn.rect.yMax, 0f))).y;
        float rightLimit = label.rectTransform.InverseTransformPoint(transform.TransformPoint(new Vector3(btn.rect.xMax, 0f, 0f))).x;
        if (local.y + size > topLimit)
        {
            local.y = topLimit - size;
            local.x = Mathf.Max(local.x, right + 1f);
        }
        local.x = Mathf.Min(local.x, rightLimit - size);
        brt.anchoredPosition = new Vector2(local.x - lr.center.x, local.y - lr.center.y);
    }

    public void OpenCodex()
    {
        CodexPanel.Open(font);
    }
}
