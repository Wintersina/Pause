using UnityEngine;
using UnityEngine.UI;

// The home menu's buttons hug their own label: a button's plate (its pressed / highlight border) is as wide as
// its widest text plus a small, equal padding on each side, instead of every row sharing the width of the
// longest label. Sizes come from the text generator's preferred size (so they follow the font, the language
// and the canvas scale); a plate narrower than a finger keeps a larger invisible hit area (raycastPadding).
public static class HomeButtonFit
{
    // Padding between the text and the plate's edge, each side (canvas units). The plate's chamfer is 16 units.
    public const float PadX = 22f;
    // The least tappable width (canvas units), as a multiple of the row height: ~two touch rows.
    public const float MinTouchRows = 2f;

    static float PreferredWidth(Text t, int size)
    {
        var s = t.GetGenerationSettings(Vector2.zero);
        s.resizeTextForBestFit = false;
        s.fontSize = size;
        s.horizontalOverflow = HorizontalWrapMode.Overflow;
        s.verticalOverflow = VerticalWrapMode.Overflow;
        return t.cachedTextGeneratorForLayout.GetPreferredWidth(t.text, s) / Mathf.Max(.01f, t.pixelsPerUnit);
    }

    static float PreferredHeight(Text t, int size)
    {
        var s = t.GetGenerationSettings(Vector2.zero);
        s.resizeTextForBestFit = false;
        s.fontSize = size;
        s.horizontalOverflow = HorizontalWrapMode.Overflow;
        s.verticalOverflow = VerticalWrapMode.Overflow;
        return t.cachedTextGeneratorForLayout.GetPreferredHeight(t.text, s) / Mathf.Max(.01f, t.pixelsPerUnit);
    }

    // The widest an effect (outline / drop shadow) pushes the glyphs sideways.
    static float EffectReach(Text t)
    {
        float reach = 0f;
        foreach (var e in t.GetComponents<Shadow>())
            if (e.enabled) reach = Mathf.Max(reach, Mathf.Abs(e.effectDistance.x));
        return reach;
    }

    // The plate width (canvas units) that hugs `button`'s texts, never over `maxWidth`. Each label is also
    // told the size it will draw at (one line, no wrapping), so the plate and the text always agree.
    public static float HugWidth(RectTransform button, float rowHeight, float maxWidth)
    {
        float need = 0f;
        foreach (var t in button.GetComponentsInChildren<Text>(false))
        {
            if (string.IsNullOrEmpty(t.text) || t.font == null) continue;
            var r = t.rectTransform;
            float h = rowHeight * (r.anchorMax.y - r.anchorMin.y) + (r.offsetMax.y - r.offsetMin.y);
            bool best = t.resizeTextForBestFit;
            int top = best ? Mathf.Max(t.resizeTextMaxSize, t.resizeTextMinSize) : t.fontSize;
            int low = best ? t.resizeTextMinSize : top;
            float reach = EffectReach(t);
            int size = top;
            while (size > low && (PreferredHeight(t, size) > h || PreferredWidth(t, size) + reach + 2f * PadX > maxWidth)) size--;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            if (best) t.resizeTextMaxSize = size;
            need = Mathf.Max(need, PreferredWidth(t, size) + reach);
        }
        if (need <= 0f) return maxWidth;
        return Mathf.Min(maxWidth, Mathf.Ceil(need + 2f * PadX));
    }

    // The widest text inside `button`, as it will draw (canvas units, effects included): what the plate hugs.
    public static float WidestText(RectTransform button)
    {
        float w = 0f;
        foreach (var t in button.GetComponentsInChildren<Text>(false))
        {
            if (string.IsNullOrEmpty(t.text) || t.font == null) continue;
            int size = t.resizeTextForBestFit ? Mathf.Max(t.resizeTextMaxSize, t.resizeTextMinSize) : t.fontSize;
            w = Mathf.Max(w, PreferredWidth(t, size) + EffectReach(t));
        }
        return w;
    }

    // Widens the hit area (not the plate) up to a finger-sized width, by at most `maxSidePad` a side.
    public static void KeepTouchWidth(RectTransform button, float width, float rowHeight, float maxSidePad)
    {
        var graphic = button.GetComponent<Graphic>();
        var b = button.GetComponent<Button>();
        if (b != null && b.targetGraphic != null) graphic = b.targetGraphic;
        if (graphic == null) return;
        var p = graphic.raycastPadding;
        float side = Mathf.Max(-p.x, Mathf.Clamp((MinTouchRows * rowHeight - width) * .5f, 0f, maxSidePad));
        graphic.raycastPadding = new Vector4(-side, p.y, -side, p.w);
    }

    // The main column: each active row gets its hugging width as a layout width (the column's group centres it).
    public static void FitColumn(Transform panel, float maxWidth)
    {
        foreach (Transform child in panel)
        {
            if (!child.gameObject.activeSelf) continue;
            var element = child.GetComponent<LayoutElement>() ?? child.gameObject.AddComponent<LayoutElement>();
            float rowH = Mathf.Max(1f, element.preferredHeight);
            var rt = (RectTransform)child;
            float w = HugWidth(rt, rowH, maxWidth);
            element.minWidth = element.preferredWidth = w;
            element.flexibleWidth = 0f;
            KeepTouchWidth(rt, w, rowH, float.MaxValue);
        }
    }

    // A footer button (already placed): narrows about its centre to hug its text, never wider than placed.
    public static void FitFooter(string name, float rowHeight, float maxSidePad)
    {
        var go = SceneUtil.FindAny(name);
        var rt = go != null ? go.GetComponent<RectTransform>() : null;
        if (rt == null || !go.activeInHierarchy) return;
        float w = HugWidth(rt, rowHeight, rt.sizeDelta.x);
        rt.sizeDelta = new Vector2(Mathf.Min(rt.sizeDelta.x, w), rt.sizeDelta.y);
        KeepTouchWidth(rt, rt.sizeDelta.x, rowHeight, maxSidePad);
    }

    // Changes whenever any menu text changes (language, counters), so the layout can be redone.
    public static int TextStamp(Transform panel)
    {
        int h = 17;
        if (panel == null) return h;
        foreach (var t in panel.GetComponentsInChildren<Text>(false)) h = h * 31 + (t.text != null ? t.text.GetHashCode() : 0);
        return h;
    }
}
