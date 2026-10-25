using UnityEngine;
using UnityEngine.UI;

// The KILLED banner of the kill cam: the boss intro's name plate (the same
// code-drawn BossPlateGraphic slab, ink contour, accent bar and diamond; the
// same skewed Bone lettering with its thick ink contour and drop shadow; the
// same 24 fps tick slam-in with an overshoot and the one-tick white flash) in
// the death red. It slams in from the right, holds, then snaps out to the
// left in three ticks. Screen-space, built once, shown and hidden; posing it
// allocates nothing. Nothing is raycastable.
public sealed class KilledBanner
{
    public const string Caption = "KILLED";
    // The boss plate is 95% of the view wide; the user asked for smaller banners, so this one is smaller.
    public const float WidthFraction = .8f;
    public const float ReferenceWidth = 800f;
    const float MaxFontSize = 104f;
    const int FlashTicks = 2;

    public RectTransform Root { get; private set; }
    public Text Text { get; private set; }
    Image flash;
    float scale, slideFrom, homeY;
    bool shown;

    public bool Shown { get { return shown; } }

    public static KilledBanner Build(Transform parent)
    {
        var b = new KilledBanner();
        var flashGo = new GameObject("KilledFlash", typeof(RectTransform), typeof(Image));
        flashGo.transform.SetParent(parent, false);
        b.flash = flashGo.GetComponent<Image>();
        b.flash.color = new Color(1f, 1f, 1f, 0f);
        b.flash.raycastTarget = false;
        var frt = b.flash.rectTransform;
        frt.anchorMin = Vector2.zero; frt.anchorMax = Vector2.one;
        frt.offsetMin = frt.offsetMax = Vector2.zero;

        var go = new GameObject("KilledBanner", typeof(RectTransform));
        go.transform.SetParent(parent, false);
        b.Root = (RectTransform)go.transform;
        b.Root.anchorMin = b.Root.anchorMax = b.Root.pivot = new Vector2(.5f, .5f);
        b.Root.sizeDelta = new Vector2(BossNameShapes.PlateW, BossNameShapes.PlateH);
        b.scale = ReferenceWidth * WidthFraction / BossNameShapes.PlateW;
        b.Root.localScale = Vector3.one * b.scale;
        b.slideFrom = ReferenceWidth * .5f + BossNameShapes.PlateW * b.scale * .5f + 30f;

        var plateGo = new GameObject("Plate", typeof(RectTransform), typeof(CanvasRenderer), typeof(BossPlateGraphic));
        plateGo.transform.SetParent(b.Root, false);
        var plate = plateGo.GetComponent<BossPlateGraphic>();
        plate.light = AkiraPalette.Red;
        plate.raycastTarget = false;
        var prt = plate.rectTransform;
        prt.anchorMin = prt.anchorMax = prt.pivot = new Vector2(.5f, .5f);
        prt.sizeDelta = b.Root.sizeDelta;
        prt.anchoredPosition = Vector2.zero;

        // the lettering, as on a boss's plate: skewed, ink contour x3, drop shadow
        float avail = BossNameShapes.NameRight - BossNameShapes.NameLeft;
        var textGo = new GameObject("Name", typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
        textGo.transform.SetParent(b.Root, false);
        var text = textGo.GetComponent<Text>();
        b.Text = text;
        text.font = DockArt.FindSceneFont();
        text.fontSize = (int)MaxFontSize;
        text.alignment = TextAnchor.MiddleCenter;
        text.horizontalOverflow = HorizontalWrapMode.Overflow;
        text.verticalOverflow = VerticalWrapMode.Overflow;
        text.raycastTarget = false;
        text.color = new Color(.957f, .918f, .831f);   // the boss lettering's Bone
        text.text = Caption;
        float want = text.preferredWidth;
        if (want > avail) text.fontSize = Mathf.Max(8, (int)(MaxFontSize * avail / want));
        float fs = text.fontSize;
        float outline = Mathf.Max(2f, fs * .055f);
        AddOutline(textGo, new Vector2(outline, outline) * .72f);
        AddOutline(textGo, new Vector2(outline, .01f));
        AddOutline(textGo, new Vector2(.01f, outline));
        var drop = textGo.AddComponent<Shadow>();
        drop.effectColor = AkiraPalette.Ink;
        drop.effectDistance = new Vector2(outline * .9f, -outline * 1.2f);
        drop.useGraphicAlpha = false;
        textGo.AddComponent<BossSkewEffect>();
        var trt = text.rectTransform;
        trt.anchorMin = trt.anchorMax = trt.pivot = new Vector2(.5f, .5f);
        trt.sizeDelta = new Vector2(avail, fs * 1.5f);
        trt.anchoredPosition = BossNameShapes.Local((BossNameShapes.NameLeft + BossNameShapes.NameRight) * .5f, BossNameShapes.NameMidY);

        b.Hide();
        return b;
    }

    static void AddOutline(GameObject go, Vector2 d)
    {
        var o = go.AddComponent<Outline>();
        o.effectColor = AkiraPalette.Ink;
        o.effectDistance = d;
        o.useGraphicAlpha = false;
    }

    // Vertical position of the plate's centre, canvas units from the screen centre.
    public void Place(float y) { homeY = y; }

    // The banner `t` seconds into its stay of `stay` seconds: in over 4 ticks
    // (overshoot on the third), held, out over 3 ticks. Outside the stay it hides.
    public void Pose(float t, float stay)
    {
        float tick = BossArt.Tick;
        if (t < 0f || t >= stay) { Hide(); return; }
        if (!shown)
        {
            shown = true;
            Root.gameObject.SetActive(true);
        }
        float x;
        float outAt = stay - 3f * tick;
        if (t < tick) x = slideFrom * .55f;
        else if (t < 2f * tick) x = slideFrom * .18f;
        else if (t < 3f * tick) x = -slideFrom * .04f;
        else if (t < outAt) x = 0f;
        else if (t < outAt + tick) x = -slideFrom * .12f;
        else if (t < outAt + 2f * tick) x = -slideFrom * .45f;
        else x = -slideFrom;
        Root.anchoredPosition = new Vector2(x, homeY);
        flash.color = t < FlashTicks * tick ? new Color(1f, 1f, 1f, .55f) : new Color(1f, 1f, 1f, 0f);
    }

    public void Hide()
    {
        shown = false;
        if (Root != null) Root.gameObject.SetActive(false);
        if (flash != null) flash.color = new Color(1f, 1f, 1f, 0f);
    }
}
