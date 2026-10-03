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
    }

    void OnDisable()
    {
        CodexPanel.Closed -= Refresh;
        DeveloperUnlocks.Changed -= Refresh;
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

        Refresh();
    }

    public static string CounterText()
    {
        return Codex.DiscoveredCount + "/" + Codex.Total + " DISCOVERED";
    }

    public void Refresh()
    {
        if (counter != null) counter.text = CounterText();
    }

    public void OpenCodex()
    {
        CodexPanel.Open(font);
    }
}
