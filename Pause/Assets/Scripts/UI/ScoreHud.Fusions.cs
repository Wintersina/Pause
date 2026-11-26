using UnityEngine;
using UnityEngine.UI;

public partial class ScoreHud
{
    public const float FusionIconUnits = 32f, FusionPopupSeconds = 1.5f;
    public const int FusionLabelSize = 18;
    RectTransform fusionRoot;
    CanvasGroup fusionGroup;
    Image[] fusionIcons;
    Text fusionLabel;
    float fusionAge = -1f;
    Vector2 fusionFrom;
    public RectTransform FusionPopupRoot => fusionRoot;
    public Text FusionPopupLabel => fusionLabel;
    public Image FusionIcon(int index) => fusionIcons != null && index >= 0 && index < fusionIcons.Length ? fusionIcons[index] : null;

    public static void PrewarmFusionGlyphs()
    {
        for (int kind = 0; kind < AtomFusionTable.KindCount; kind++) CodexCombosView.GlyphOf((AtomKind)kind);
    }

    void BuildFusionPopup()
    {
        if (fusionRoot != null) return;
        var go = new GameObject("FusionPopup", typeof(RectTransform), typeof(CanvasGroup));
        fusionRoot = go.GetComponent<RectTransform>(); fusionRoot.SetParent(canvasRect, false);
        fusionRoot.anchorMin = fusionRoot.anchorMax = fusionRoot.pivot = Vector2.one * .5f;
        fusionRoot.sizeDelta = new Vector2(160f, 64f);
        fusionGroup = go.GetComponent<CanvasGroup>(); fusionGroup.blocksRaycasts = false; fusionGroup.interactable = false;
        fusionIcons = new Image[3];
        for (int i = 0; i < fusionIcons.Length; i++)
        {
            var icon = new GameObject("Atom" + i, typeof(RectTransform), typeof(Image)).GetComponent<Image>();
            icon.transform.SetParent(fusionRoot, false); icon.raycastTarget = false; icon.preserveAspect = true;
            icon.rectTransform.sizeDelta = Vector2.one * FusionIconUnits;
            fusionIcons[i] = icon;
        }
        var label = new GameObject("Name", typeof(RectTransform), typeof(Text));
        label.transform.SetParent(fusionRoot, false); fusionLabel = label.GetComponent<Text>();
        Style(fusionLabel, font, FusionLabelSize, ScoreColour, TextAnchor.MiddleCenter);
        fusionLabel.rectTransform.anchoredPosition = new Vector2(0f, -21f);
        fusionLabel.rectTransform.sizeDelta = new Vector2(160f, 22f);
        go.SetActive(false);
    }

    public void ShowFusion(FusionDef fusion, Vector3 at, int chain = 1)
    {
        if (fusion == null || canvasRect == null) return;
        if (popups == null) BuildPopups();
        BuildFusionPopup();
        int count = Mathf.Min(fusionIcons.Length, fusion.atoms.Length);
        for (int i = 0; i < fusionIcons.Length; i++)
        {
            fusionIcons[i].gameObject.SetActive(i < count);
            if (i >= count) continue;
            fusionIcons[i].sprite = CodexCombosView.GlyphOf(fusion.atoms[i]);
            fusionIcons[i].rectTransform.anchoredPosition = new Vector2((i - (count - 1) * .5f) * 40f, 10f);
        }
        fusionLabel.text = chain > 1 ? fusion.name + " x" + chain : fusion.name;
        fusionLabel.color = Color.Lerp(fusion.glow, Color.white, .65f);
        Rect safe = SafeUnits(); Vector2 point = ToCanvas(at);
        float halfWidth = Mathf.Max(86f, fusionLabel.preferredWidth * .5f + 8f);
        float left = safe.xMin + halfWidth, right = safe.xMax - halfWidth;
        point.x = right >= left ? Mathf.Clamp(point.x, left, right) : safe.center.x;
        float bottom = safe.yMin + 38f, top = safe.yMax - 58f;
        point.y = top >= bottom ? Mathf.Clamp(point.y, bottom, top) : safe.center.y;
        fusionFrom = point; fusionRoot.anchoredPosition = point;
        fusionRoot.localScale = Vector3.one; fusionGroup.alpha = 1f; fusionAge = 0f;
        fusionRoot.gameObject.SetActive(true); fusionRoot.SetAsLastSibling();
    }

    void StepFusionPopup(float dt)
    {
        if (fusionAge < 0f || fusionRoot == null) return;
        fusionAge = dt < 0f ? FusionPopupSeconds : fusionAge + dt;
        if (fusionAge >= FusionPopupSeconds)
        {
            fusionAge = -1f; fusionRoot.gameObject.SetActive(false); return;
        }
        float q = fusionAge / FusionPopupSeconds;
        fusionRoot.anchoredPosition = fusionFrom + Vector2.up * (16f * q);
        fusionRoot.localScale = Vector3.one * (1f + .08f * Mathf.Max(0f, 1f - fusionAge * 6f));
        fusionGroup.alpha = q < .65f ? 1f : Mathf.Clamp01((1f - q) / .35f);
    }
}
