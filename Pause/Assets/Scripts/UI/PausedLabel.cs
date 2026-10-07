using UnityEngine;
using UnityEngine.UI;

// A very small "PAUSED" caption sitting just above the in-run pause icon (the
// "paused" SpriteRenderer that moveStarsBackground shows while the finger is
// lifted and PausedOverlayAnim flips through).
//
// It is lettered after the home screen's PAUSE logo (Art/UI/Title/pause_title_2.png,
// which is never touched): the logo's red fill (lighter at the top, deeper at
// the foot), its thin white contour, its solid muted-red drop shadow knocked
// down-right, and its soft red halo. The logo face itself isn't shipped as a
// font, so the caption uses the game's UI face, Orbitron Bold, in capitals
// like the logo.
//
// It lives on a child of the icon, so it shows and hides with it, and pops in
// on the icon's own ticks (squash, stretch, rest; PausedOverlayAnim.Tick) on
// unscaled time, the world being frozen. A world-space canvas with no
// GraphicRaycaster and nothing raycastable: it can never take a touch.
public class PausedLabel : MonoBehaviour
{
    public const string ObjectName = "PausedLabel";
    public const string Caption = "PAUSED";

    // Logo colours, sampled from pause_title_2.png.
    public static readonly Color FillTop = new Color32(242, 52, 50, 255);
    public static readonly Color FillBottom = new Color32(206, 2, 6, 255);
    public static readonly Color Contour = new Color32(255, 255, 255, 255);
    public static readonly Color DropShadow = new Color32(192, 64, 64, 255);
    public static readonly Color Halo = new Color32(190, 63, 63, 110);

    // Size, in world units. Cap height about a third of the icon's bars.
    public const float WorldCapHeight = 0.105f;
    // Offset of the caption's box from the icon sprite's top edge. The box
    // carries a little padding under the letters and the bars' drawing stops
    // short of the sprite's edge, so a slight overlap reads as a small gap.
    public const float WorldGap = -0.015f;

    const int FontSize = 64;
    // Orbitron's cap height as a share of the font size.
    const float CapRatio = 0.72f;

    // WorldCapHeight -- or, on a screen small in points / dp (a 480x854
    // phone), as much more as keeps the type at UiScale.MinTextPt.
    public static float CapHeightFor(Camera cam)
    {
        if (!UiScale.Active || cam == null || !cam.orthographic || ScreenInfo.Height <= 0) return WorldCapHeight;
        float worldPerPixel = 2f * cam.orthographicSize / ScreenInfo.Height;
        float minFont = UiScale.MinTextPt * 1.05f * UiScale.PxPerPoint * worldPerPixel;
        return Mathf.Max(WorldCapHeight, minFont * CapRatio);
    }

    // Pop-in poses (x, y scale) for the squash and stretch ticks.
    static readonly Vector2 SquashPose = new Vector2(1.18f, 0.78f);
    static readonly Vector2 StretchPose = new Vector2(0.9f, 1.16f);

    RectTransform box;
    int step;
    float clock;
    Texture2D haloTex;
    Sprite haloSprite;

    public Text Text { get; private set; }
    public RectTransform Box { get { return box; } }

    // Builds (once) the caption under the pause icon.
    public static PausedLabel AttachTo(GameObject icon)
    {
        if (icon == null) return null;
        var existing = icon.GetComponentInChildren<PausedLabel>(true);
        if (existing != null) return existing;

        var go = new GameObject(ObjectName, typeof(RectTransform), typeof(Canvas), typeof(CanvasGroup));
        go.layer = icon.layer;
        go.transform.SetParent(icon.transform, false);
        var label = go.AddComponent<PausedLabel>();
        label.Build(icon);
        return label;
    }

    void Build(GameObject icon)
    {
        var iconRenderer = icon.GetComponent<SpriteRenderer>();

        var canvas = GetComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        if (iconRenderer != null)
        {
            canvas.sortingLayerID = iconRenderer.sortingLayerID;
            canvas.sortingOrder = iconRenderer.sortingOrder;
        }
        var group = GetComponent<CanvasGroup>();
        group.blocksRaycasts = false;
        group.interactable = false;

        // Canvas units -> world units, whatever the icon's own scale.
        float unitsPerCanvas = CapHeightFor(Camera.main) / (FontSize * CapRatio);
        Vector3 parentScale = icon.transform.lossyScale;
        float sx = Mathf.Abs(parentScale.x) > 1e-5f ? parentScale.x : 1f;
        float sy = Mathf.Abs(parentScale.y) > 1e-5f ? parentScale.y : 1f;
        transform.localScale = new Vector3(unitsPerCanvas / sx, unitsPerCanvas / sy, 1f);

        // Halo first so it draws under the lettering.
        var halo = new GameObject("Halo", typeof(RectTransform), typeof(Image));
        halo.layer = gameObject.layer;
        halo.transform.SetParent(transform, false);
        var haloImage = halo.GetComponent<Image>();
        haloTex = BuildHaloTexture();
        haloSprite = Sprite.Create(haloTex, new Rect(0, 0, haloTex.width, haloTex.height), new Vector2(.5f, .5f));
        haloImage.sprite = haloSprite;
        haloImage.color = Halo;
        haloImage.raycastTarget = false;

        var textGo = new GameObject("Text", typeof(RectTransform), typeof(Text));
        textGo.layer = gameObject.layer;
        textGo.transform.SetParent(transform, false);
        var text = textGo.GetComponent<Text>();
        Text = text;
        text.font = WorldBanner.OrbitronOrBuiltin();
        text.fontSize = FontSize;
        text.fontStyle = FontStyle.Normal;
        text.alignment = TextAnchor.MiddleCenter;
        text.horizontalOverflow = HorizontalWrapMode.Overflow;
        text.verticalOverflow = VerticalWrapMode.Overflow;
        text.supportRichText = false;
        text.raycastTarget = false;
        text.color = Color.white;
        text.text = Caption;

        // Effect order matters: tint the fill, contour it, then shadow the
        // contoured glyph (the logo's shadow sits behind the white line).
        textGo.AddComponent<PausedLabelGradient>().Set(FillTop, FillBottom);
        var contour = textGo.AddComponent<Outline>();
        contour.effectColor = Contour;
        contour.effectDistance = new Vector2(2.2f, 2.2f);
        var shadow = textGo.AddComponent<Shadow>();
        shadow.effectColor = DropShadow;
        shadow.effectDistance = new Vector2(4f, -5f);

        // The box hugs the lettering (plus contour and shadow); pivot at its
        // foot so the pop squashes onto the icon rather than away from it.
        float w = text.preferredWidth + 10f;
        float h = FontSize * CapRatio + 14f;
        box = (RectTransform)transform;
        box.sizeDelta = new Vector2(w, h);
        box.pivot = new Vector2(.5f, 0f);
        var textRect = (RectTransform)textGo.transform;
        textRect.anchorMin = textRect.anchorMax = new Vector2(.5f, .5f);
        textRect.sizeDelta = new Vector2(w, h);
        textRect.anchoredPosition = Vector2.zero;
        var haloRect = (RectTransform)halo.transform;
        haloRect.anchorMin = haloRect.anchorMax = new Vector2(.5f, .5f);
        haloRect.sizeDelta = new Vector2(w * 1.35f, h * 2.1f);

        // Just above the icon's sprite, centred on it.
        float top = 0.64f;
        if (iconRenderer != null && iconRenderer.sprite != null) top = iconRenderer.sprite.bounds.max.y;
        float gapLocal = WorldGap / sy;
        transform.localPosition = new Vector3(0f, top + gapLocal, 0f);
        transform.localRotation = Quaternion.identity;
        Pose();
    }

    // A soft elliptical falloff for the red halo behind the caption.
    static Texture2D BuildHaloTexture()
    {
        const int W = 64, H = 32;
        var tex = new Texture2D(W, H, TextureFormat.RGBA32, false);
        tex.wrapMode = TextureWrapMode.Clamp;
        tex.filterMode = FilterMode.Bilinear;
        tex.hideFlags = HideFlags.DontSave;
        var px = new Color32[W * H];
        for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                float dx = (x + .5f) / W * 2f - 1f;
                float dy = (y + .5f) / H * 2f - 1f;
                float d = Mathf.Sqrt(dx * dx + dy * dy);
                float a = Mathf.Clamp01(1f - d);
                a = a * a * (3f - 2f * a);
                px[y * W + x] = new Color32(255, 255, 255, (byte)(a * 255f));
            }
        tex.SetPixels32(px);
        tex.Apply(false, true);
        return tex;
    }

    void OnEnable()
    {
        step = 0;
        clock = 0f;
        Pose();
    }

    void Update()
    {
        if (step >= 2) return;
        clock += Time.unscaledDeltaTime;
        if (clock < PausedOverlayAnim.Tick) return;
        clock = 0f;
        step++;
        Pose();
    }

    // Test hook: advance the pop as if `seconds` of unscaled time passed.
    public void Advance(float seconds)
    {
        while (step < 2 && seconds >= PausedOverlayAnim.Tick - clock)
        {
            seconds -= PausedOverlayAnim.Tick - clock;
            clock = 0f;
            step++;
        }
        if (step < 2) clock += seconds;
        Pose();
    }

    public int PopStep { get { return step; } }

    void Pose()
    {
        if (box == null) return;
        Vector2 p = step == 0 ? SquashPose : step == 1 ? StretchPose : Vector2.one;
        foreach (Transform child in box) child.localScale = new Vector3(p.x, p.y, 1f);
        // Children are centred; keep the foot planted on the icon.
        foreach (Transform child in box)
            ((RectTransform)child).anchoredPosition = new Vector2(0f, box.sizeDelta.y * .5f * (p.y - 1f));
    }

    void OnDestroy()
    {
        Release(haloSprite);
        Release(haloTex);
    }

    static void Release(Object o)
    {
        if (o == null) return;
        if (Application.isPlaying) Destroy(o);
        else DestroyImmediate(o);
    }
}
