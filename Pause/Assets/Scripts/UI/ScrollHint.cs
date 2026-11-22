using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// "There is more below": a soft fade over the bottom edge of a ScrollRect, a small pixel chevron that bobs gently and a
// short label. It shows while content is hidden below the viewport, fades out near the bottom (and when everything fits)
// and returns if the player scrolls back up. Tapping the chevron eases the list down. The fade and the label never take
// taps (raycastTarget off); only the chevron's own small strip does, and it stops taking them once it has faded out.
// Everything is a child of the ScrollRect (outside the masked viewport). Owner: whoever builds the list ticks it.
public sealed class ScrollHint : MonoBehaviour, IPointerClickHandler
{
    public const float ShowAbove = 48f;      // units of content left below: the hint appears
    public const float HideBelow = 24f;      // ...and goes away under this (hysteresis: no flicker at the threshold)
    public const float FitSlack = 8f;        // content within this much of the viewport "fits": no hint
    public const float FadeHeight = 120f;
    public const float FadeSpeed = 6f;       // alpha per second
    public const float BobAmplitude = 5f, BobSpeed = 3.2f;
    public const float PageFraction = .8f;   // one tap scrolls this much of the viewport
    public const float ScrollSeconds = .4f;
    public const float PeekDistance = 44f, PeekSeconds = 1.1f;

    // ---- the pure rule (tests drive it directly) ----

    // overflow = content height - viewport height; fromBottom = how far the list is still from its end, in units.
    public static bool ShouldShow(float overflow, float fromBottom, bool wasShown)
    {
        if (overflow <= FitSlack) return false;
        return wasShown ? fromBottom > HideBelow : fromBottom > ShowAbove;
    }

    static float Ease(float t) { t = Mathf.Clamp01(t); return t * t * (3f - 2f * t); }

    ScrollRect scroll;
    RectTransform fade, chevron, tapStrip;
    Text label;
    Image fadeImage, chevronImage;
    CanvasGroup group;
    bool shown;
    float alpha;
    float animStart = -1f, animFrom, animTo, animSeconds;
    float lastSet;
    bool peeking;

    public bool Shown { get { return shown; } }
    public float Alpha { get { return alpha; } }
    public RectTransform TapStrip { get { return tapStrip; } }
    public Text Label { get { return label; } }
    public bool Animating { get { return animStart >= 0f; } }

    // ---- optional solid band (opt-in via UseBand; the default look above is unchanged) ----
    // The hint sits on an opaque strip at the bottom of the scroll and the viewport is shrunk by the strip's height
    // while it shows, so no row ever renders behind it. The viewport grows back (eased with the alpha) at the end.
    public const float BandMinHeight = 76f, BandSoftEdge = 10f;
    bool bandMode;
    float bandHeight;
    RectTransform bandRoot, bandChevronL, bandChevronR;
    Vector2 baseOffMin, baseOffMax;
    public bool BandMode { get { return bandMode; } }
    public RectTransform Band { get { return bandRoot; } }
    // Pixels (units) currently taken off the bottom of the viewport; 0 when the band is hidden.
    public float BandInset { get { return bandMode ? bandHeight * alpha : 0f; } }
    public float BandHeight { get { return bandHeight; } }

    // Switches this hint to the solid-band look. `bandColor` should be the panel's background; `labelColor` null = a
    // brightened accent. `height` <= 0 = finger-sized (at least BandMinHeight). Call once, right after Attach.
    public void UseBand(Color bandColor, float height = 0f, Color? labelColor = null)
    {
        if (bandMode) return;
        bandMode = true;
        bandHeight = height > 0f ? height : Mathf.Max(BandMinHeight, CodexUi.TapUnits);
        var vp = scroll.viewport;
        baseOffMin = vp.offsetMin; baseOffMax = vp.offsetMax;
        var rt = (RectTransform)transform;
        var col = labelColor ?? Color.Lerp(chevronImage.color, Color.white, .35f);

        // the soft fade becomes a short edge above the band; the band itself is solid
        fadeImage.color = bandColor;
        fade.anchorMin = new Vector2(0f, 0f); fade.anchorMax = new Vector2(1f, 0f);
        fade.pivot = new Vector2(.5f, 0f);
        fade.anchoredPosition = new Vector2(0f, bandHeight); fade.sizeDelta = new Vector2(0f, BandSoftEdge);
        var solid = CodexUi.NewImage("Band", rt, null, bandColor);
        solid.raycastTarget = false;
        bandRoot = solid.rectTransform;
        bandRoot.anchorMin = new Vector2(0f, 0f); bandRoot.anchorMax = new Vector2(1f, 0f);
        bandRoot.pivot = new Vector2(.5f, 0f);
        bandRoot.anchoredPosition = Vector2.zero; bandRoot.sizeDelta = new Vector2(0f, bandHeight);
        bandRoot.SetSiblingIndex(1);   // above the fade, below the label / chevrons

        label.color = col;
        label.resizeTextMaxSize = 30; label.resizeTextMinSize = 16; label.fontSize = 30; label.fontStyle = FontStyle.Bold;
        chevronImage.color = col;
        chevron.localScale = new Vector3(1.4f, 1.4f, 1f);
        var l2 = CodexUi.NewImage("ChevronR", rt, chevronImage.sprite, col);
        l2.raycastTarget = false;
        bandChevronR = l2.rectTransform;
        bandChevronL = chevron;
        // label centred in the band; a chevron either side of it (they bob together)
        label.alignment = TextAnchor.MiddleCenter;
        SetBottomCenter(label.rectTransform, 0f, 0f, 260f, bandHeight);
        // the whole band is the tap target
        tapStrip.anchorMin = new Vector2(0f, 0f); tapStrip.anchorMax = new Vector2(1f, 0f);
        tapStrip.pivot = new Vector2(.5f, 0f);
        tapStrip.anchoredPosition = Vector2.zero; tapStrip.sizeDelta = new Vector2(0f, bandHeight);
        bandChevronL.anchorMin = bandChevronL.anchorMax = bandChevronR.anchorMin = bandChevronR.anchorMax = new Vector2(.5f, 0f);
        bandChevronL.pivot = bandChevronR.pivot = new Vector2(.5f, .5f);
        bandChevronL.sizeDelta = bandChevronR.sizeDelta = new Vector2(44f, 24f);
        bandChevronR.localScale = bandChevronL.localScale;
        baseChevronY = bandHeight * .5f;
    }

    float FullViewportHeight()
    {
        var sr = (RectTransform)scroll.transform;
        return sr.rect.height - baseOffMin.y + baseOffMax.y;
    }

    void ApplyBand()
    {
        var vp = scroll.viewport;
        vp.offsetMin = new Vector2(baseOffMin.x, baseOffMin.y + BandInset);
        vp.offsetMax = baseOffMax;
        float labelW = Mathf.Clamp(label.preferredWidth + 8f, 120f, 300f);
        float half = labelW * .5f + 34f;
        float bob = Mathf.Sin(Time.unscaledTime * BobSpeed) * BobAmplitude;
        bandChevronL.anchoredPosition = new Vector2(-half, baseChevronY + bob);
        bandChevronR.anchoredPosition = new Vector2(half, baseChevronY + bob);
        bandChevronR.localScale = bandChevronL.localScale;
        // the viewport just grew (band collapsing): never leave a gap under the last row
        if (!shown && scroll.content != null)
        {
            float max = Mathf.Max(0f, scroll.content.rect.height - (FullViewportHeight() - BandInset));
            var c = scroll.content;
            if (c.anchoredPosition.y > max && animStart < 0f && alpha < 1f)
                c.anchoredPosition = new Vector2(c.anchoredPosition.x, max);
        }
    }

    // Builds the hint as the last child of the scroll's rect. `font` may be null (default font).
    public static ScrollHint Attach(ScrollRect scroll, Font font, string text, Color accent, Color fadeColor)
    {
        var rt = CodexUi.NewRect("ScrollHint", scroll.transform);
        CodexUi.Stretch(rt);
        var hint = rt.gameObject.AddComponent<ScrollHint>();
        hint.scroll = scroll;
        hint.group = rt.gameObject.AddComponent<CanvasGroup>();
        hint.group.alpha = 0f; hint.group.blocksRaycasts = false; hint.group.interactable = false;

        hint.fadeImage = CodexUi.NewImage("Fade", rt, FadeSprite(), fadeColor);
        hint.fade = hint.fadeImage.rectTransform;
        hint.fade.anchorMin = new Vector2(0f, 0f); hint.fade.anchorMax = new Vector2(1f, 0f);
        hint.fade.pivot = new Vector2(.5f, 0f);
        hint.fade.anchoredPosition = Vector2.zero; hint.fade.sizeDelta = new Vector2(0f, FadeHeight);
        hint.fadeImage.raycastTarget = false;

        hint.chevronImage = CodexUi.NewImage("Chevron", rt, ChevronSprite(), accent);
        hint.chevron = hint.chevronImage.rectTransform;
        hint.chevronImage.raycastTarget = false;
        hint.label = CodexUi.NewText("Label", rt, font, text, 20, accent, TextAnchor.MiddleCenter);
        CodexUi.AddOutline(hint.label.gameObject, CodexUi.Ink, 2f);
        hint.label.resizeTextForBestFit = true; hint.label.resizeTextMinSize = 15; hint.label.resizeTextMaxSize = 20;
        hint.label.horizontalOverflow = HorizontalWrapMode.Wrap; hint.label.verticalOverflow = VerticalWrapMode.Truncate;

        // the chevron's tap strip: invisible, finger-sized, the only part of the hint that takes a tap
        var strip = CodexUi.NewImage("Tap", rt, null, new Color(0f, 0f, 0f, 0f));
        strip.raycastTarget = true;
        hint.tapStrip = strip.rectTransform;
        hint.Place(0f, 0f);   // the host re-places it once it knows its layout
        return hint;
    }

    // Puts the hint's column (label over chevron, plus the finger strip over both) at `centerX` in the scroll's own space
    // (0 = its middle), `width` wide at most, sitting `bottomPad` above the viewport's bottom edge.
    public void Place(float centerX, float width, float bottomPad = 10f)
    {
        if (bandMode) return;   // the band is full width and centred; nothing to place
        float tapH = CodexUi.TapUnits;
        float w = Mathf.Max(CodexUi.TapUnits, width);
        SetBottomCenter(tapStrip, centerX, bottomPad, w, tapH);
        SetBottomCenter(label.rectTransform, centerX, bottomPad + 44f, w, 30f);
        SetBottomCenter(chevron, centerX, bottomPad + 12f, 40f, 20f);
        baseChevronY = bottomPad + 12f; baseX = centerX;
    }
    float baseChevronY, baseX;

    static void SetBottomCenter(RectTransform rt, float x, float y, float w, float h)
    {
        rt.anchorMin = rt.anchorMax = new Vector2(.5f, 0f);
        rt.pivot = new Vector2(.5f, 0f);
        rt.anchoredPosition = new Vector2(x, y);
        rt.sizeDelta = new Vector2(w, h);
    }

    // How much list is still hidden below the viewport, and how much overflows at all.
    public void Measure(out float overflow, out float fromBottom)
    {
        overflow = 0f; fromBottom = 0f;
        if (scroll == null || scroll.content == null || scroll.viewport == null) return;
        float vh = bandMode ? FullViewportHeight() - BandInset : scroll.viewport.rect.height;
        overflow = Mathf.Max(0f, scroll.content.rect.height - vh);
        fromBottom = Mathf.Max(0f, overflow - scroll.content.anchoredPosition.y);
    }

    // Decides visible / hidden from the list's present state; `dt` eases the alpha (pass a big dt to snap).
    public void Tick(float dt)
    {
        float overflow, fromBottom;
        Measure(out overflow, out fromBottom);
        // the "fits" rule judges the full-height viewport, so the band's own inset never makes a fitting list look scrollable
        shown = ShouldShow(overflow - BandInset, fromBottom, shown);
        float target = shown ? 1f : 0f;
        alpha = Mathf.MoveTowards(alpha, target, FadeSpeed * Mathf.Max(0f, dt));
        group.alpha = alpha;
        group.blocksRaycasts = alpha > .3f && shown;   // a hidden hint never eats a tap
        float bob = Mathf.Sin(Time.unscaledTime * BobSpeed) * BobAmplitude;
        if (bandMode) ApplyBand();
        else if (chevron != null) chevron.anchoredPosition = new Vector2(baseX, baseChevronY + bob);
        StepAnim();
    }

    public void Snap() { Tick(10f); }

    // Drops any scroll / peek in flight (the host closed and reopened the list).
    public void Cancel() { animStart = -1f; }

    // ---- scrolling ----

    public void OnPointerClick(PointerEventData e) { ScrollDown(); }

    // One page down (clamped to the end), eased.
    public void ScrollDown()
    {
        float overflow, fromBottom;
        Measure(out overflow, out fromBottom);
        if (overflow <= 0f) return;
        float y = scroll.content.anchoredPosition.y;
        float to = Mathf.Min(overflow, y + scroll.viewport.rect.height * PageFraction);
        BeginAnim(y, to, ScrollSeconds, false);
    }

    // The first-open nudge: a short bounce down and back. Does nothing when the list fits.
    public void Peek()
    {
        float overflow, fromBottom;
        Measure(out overflow, out fromBottom);
        if (overflow <= FitSlack) return;
        float y = scroll.content.anchoredPosition.y;
        BeginAnim(y, Mathf.Min(overflow, y + PeekDistance), PeekSeconds, true);
    }

    void BeginAnim(float from, float to, float seconds, bool bounce)
    {
        animFrom = from; animTo = to; animSeconds = seconds; peeking = bounce;
        animStart = Time.unscaledTime; lastSet = from;
        scroll.StopMovement();
    }

    void StepAnim()
    {
        if (animStart < 0f) return;
        var c = scroll.content;
        // the player grabbed the list (it no longer sits where the animation left it): hands off
        if (Mathf.Abs(c.anchoredPosition.y - lastSet) > 1.5f) { animStart = -1f; return; }
        float t = Mathf.Clamp01((Time.unscaledTime - animStart) / animSeconds);
        float k = peeking ? Mathf.Sin(t * Mathf.PI) : Ease(t);   // peek: down and back; scroll: there and stay
        float y = Mathf.Lerp(animFrom, animTo, k);
        c.anchoredPosition = new Vector2(c.anchoredPosition.x, y);
        scroll.velocity = Vector2.zero;
        lastSet = y;
        if (t >= 1f) animStart = -1f;
    }

    // ---- art: tiny generated sprites (a vertical alpha ramp, an 11x6 pixel chevron) ----

    static Sprite fadeSprite, chevronSprite;

    static Sprite FadeSprite()
    {
        if (fadeSprite != null) return fadeSprite;
        const int h = 32;
        var tex = new Texture2D(1, h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, name = "ScrollHintFade" };
        for (int y = 0; y < h; y++)
        {
            float t = 1f - y / (h - 1f);          // y = 0 is the bottom edge: opaque, fading to clear upwards
            tex.SetPixel(0, y, new Color(1f, 1f, 1f, t * t * .92f));
        }
        tex.Apply(false, true);
        fadeSprite = Sprite.Create(tex, new Rect(0, 0, 1, h), new Vector2(.5f, .5f), 100f);
        return fadeSprite;
    }

    static Sprite ChevronSprite()
    {
        if (chevronSprite != null) return chevronSprite;
        const int w = 11, h = 6;
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Point, name = "ScrollHintChevron" };
        var clear = new Color(1f, 1f, 1f, 0f);
        for (int y = 0; y < h; y++) for (int x = 0; x < w; x++) tex.SetPixel(x, y, clear);
        // a two-pixel-thick V: row 5 (top) is the widest, the tip is at the bottom middle
        for (int row = 0; row < h; row++)
        {
            int y = row;                       // 0 = bottom
            int left = 5 - y, right = 5 + y;   // y = 0 -> the tip at x = 5
            if (y > 4) break;
            foreach (int x in new[] { left, left + 1, right - 1, right })
                if (x >= 0 && x < w) tex.SetPixel(x, y, Color.white);
        }
        tex.Apply(false, true);
        chevronSprite = Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(.5f, .5f), 100f);
        return chevronSprite;
    }
}
