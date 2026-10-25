using UnityEngine;
using UnityEngine.UI;

// The READY / GO!!!! caption at the start of a run (movePlayer drives the
// text, size and fade of the scene's "goText"). Lettered like the PAUSED
// caption (PausedLabel) and the REPLAY label: Orbitron Bold, a vertical
// gradient fill, a white contour, a knocked-down drop shadow and a soft halo
// behind it. READY is white-gold; GO!!!! takes the logo's red and pops in
// (squash, stretch, rest on PausedOverlayAnim.Tick) on the frame it appears.
// This component only styles: it never changes the text, size, colour alpha
// or position that movePlayer sets, so the timing is untouched.
[RequireComponent(typeof(Text))]
public sealed class LaunchBanner : MonoBehaviour
{
    public static readonly Color ReadyTop = new Color32(255, 250, 228, 255);
    public static readonly Color ReadyBottom = new Color32(255, 190, 56, 255);
    public static readonly Color ReadyShadow = new Color32(168, 104, 24, 255);
    public static readonly Color ReadyHalo = new Color32(255, 196, 80, 96);

    const float BaseFont = 64f;   // PausedLabel's effect metrics are for a 64 pt face
    const float CapRatio = .72f;
    static readonly Vector2 SquashPose = new Vector2(1.18f, .78f);
    static readonly Vector2 StretchPose = new Vector2(.9f, 1.16f);

    Text text;
    PausedLabelGradient gradient;
    Outline contour;
    Shadow shadow;
    Image halo;
    RectTransform haloRect, rect;
    Texture2D haloTex;
    Sprite haloSprite;
    string shown;
    float age;
    int fontShown = -1;

    public static bool IsGo(string label) { return label == movePlayer.GoLabel; }

    public static void Colours(bool go, out Color top, out Color bottom, out Color shade, out Color haloColour)
    {
        top = go ? PausedLabel.FillTop : ReadyTop;
        bottom = go ? PausedLabel.FillBottom : ReadyBottom;
        shade = go ? PausedLabel.DropShadow : ReadyShadow;
        haloColour = go ? PausedLabel.Halo : ReadyHalo;
    }

    // The squash / stretch / rest scale at `seconds` since the label appeared.
    public static Vector2 PopScale(float seconds)
    {
        if (seconds < PausedOverlayAnim.Tick) return SquashPose;
        if (seconds < 2f * PausedOverlayAnim.Tick) return StretchPose;
        return Vector2.one;
    }

    void Awake() { Init(); }

    // Also called by the edit-mode test, where Awake does not run.
    public void Init()
    {
        if (text != null) return;
        text = GetComponent<Text>();
        rect = (RectTransform)transform;
        text.font = WorldBanner.OrbitronOrBuiltin();
        text.fontStyle = FontStyle.Normal;
        text.supportRichText = false;
        text.raycastTarget = false;
        text.horizontalOverflow = HorizontalWrapMode.Overflow;
        text.verticalOverflow = VerticalWrapMode.Overflow;
        text.alignment = TextAnchor.MiddleCenter;
        // same effect order as PausedLabel: fill, contour, then shadow
        gradient = gameObject.AddComponent<PausedLabelGradient>();
        contour = gameObject.AddComponent<Outline>();
        contour.effectColor = PausedLabel.Contour;
        shadow = gameObject.AddComponent<Shadow>();
        BuildHalo();
    }

    void BuildHalo()
    {
        var go = new GameObject("GoHalo", typeof(RectTransform), typeof(Image));
        go.layer = gameObject.layer;
        go.transform.SetParent(transform.parent, false);
        go.transform.SetSiblingIndex(transform.GetSiblingIndex());   // behind the lettering
        haloRect = (RectTransform)go.transform;
        halo = go.GetComponent<Image>();
        haloTex = PausedLabel.BuildHaloTexture();
        haloSprite = Sprite.Create(haloTex, new Rect(0, 0, haloTex.width, haloTex.height), new Vector2(.5f, .5f));
        halo.sprite = haloSprite;
        halo.raycastTarget = false;
    }

    void OnEnable()
    {
        age = 0f;
        shown = null;
        if (halo != null) halo.enabled = true;
    }

    void OnDisable()
    {
        if (halo != null) halo.enabled = false;
    }

    void OnDestroy()
    {
        if (halo != null) Destroy(halo.gameObject);
        if (haloSprite != null) Destroy(haloSprite);
        if (haloTex != null) Destroy(haloTex);
    }

    void LateUpdate()
    {
        bool go = IsGo(text.text);
        if (text.text != shown)
        {
            shown = text.text;
            age = 0f;
            Colours(go, out var top, out var bottom, out var shade, out var haloColour);
            gradient.Set(top, bottom);
            shadow.effectColor = shade;
            halo.color = haloColour;
            fontShown = -1;
        }
        else age += Time.unscaledDeltaTime;

        if (text.fontSize != fontShown)
        {
            fontShown = text.fontSize;
            float k = fontShown / BaseFont;
            contour.effectDistance = new Vector2(2.2f, 2.2f) * k;
            shadow.effectDistance = new Vector2(4f, -5f) * k;
            float w = text.preferredWidth + 10f * k;
            float h = fontShown * CapRatio + 14f * k;
            haloRect.sizeDelta = new Vector2(w * 1.35f, h * 2.1f);
        }

        // The halo shares the lettering's place and fade (movePlayer fades the
        // text's alpha for GO); it sits just behind it.
        haloRect.anchorMin = rect.anchorMin;
        haloRect.anchorMax = rect.anchorMax;
        haloRect.pivot = rect.pivot;
        haloRect.anchoredPosition = rect.anchoredPosition;
        Colours(go, out _, out _, out _, out var hc);
        hc.a *= text.color.a;
        halo.color = hc;

        Vector2 s = go ? PopScale(age) : Vector2.one;
        rect.localScale = new Vector3(s.x, s.y, 1f);
        haloRect.localScale = rect.localScale;
    }
}
