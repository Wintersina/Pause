using UnityEngine;
using UnityEngine.UI;

// One sizing rule for the modal cards (the FLIGHT COMPLETE panel, the ship and upgrade offers).
//
// Their art is laid out in canvas units and the canvas scales with the screen, so a card that fills a phone's width also
// fills an iPad's, a Mac window's or a wide desktop view's: a poster. A card is instead never drawn wider than a tall
// phone draws it, in PHYSICAL terms:
//
//     widthPx <= min( 0.6 * safeHeight,           portrait-phone proportions (a phone's card is up to ~0.56 of its safe height)
//                     MaxWidthPt * pxPerPoint )   no wider than ~a big phone in points / dp
//
// (staying inside the safe area's width is each card's own fit, with its 12-unit margin, as before).
//
// The whole card scales uniformly (localScale), so the type and the buttons shrink with the frame, never past what a
// phone already shows (a phone's card is ~0.5 pt per unit; the cap leaves tablets at ~0.58). On phones none of the
// two terms binds beyond what the fit already does, so a phone looks exactly as before; it is gated by size math, not
// by platform, so Android tablets, foldables unfolded and Chromebooks get it too.
public static class PopupFit
{
    public const float MaxWidthPt = 420f;
    public const float PhoneHeightFraction = .6f;
    public const float Margin = 12f;   // canvas units kept clear of the safe area

    public static float MaxWidthPx(Rect safePx, float pxPerPt)
    {
        float cap = safePx.height * PhoneHeightFraction;
        if (pxPerPt > 0f) cap = Mathf.Min(cap, MaxWidthPt * pxPerPt);
        return cap;
    }

    // The largest scale (never above 1) at which a card `unitsW` canvas units wide is within the cap on a canvas drawn
    // at `canvasScale` pixels per unit.
    public static float CapScale(float unitsW, float canvasScale, Rect safePx, float pxPerPt)
    {
        if (unitsW <= 0f || canvasScale <= 0f) return 1f;
        return Mathf.Clamp(MaxWidthPx(safePx, pxPerPt) / (unitsW * canvasScale), .1f, 1f);
    }

    // Scale and centre for a card of unitsW x unitsH canvas units inside `safeUnits` (canvas units, origin at the canvas
    // centre): the existing fit-inside-the-safe-area scale, further limited by the cap.
    public static void Compute(Rect safeUnits, float unitsW, float unitsH, float canvasScale, Rect safePx, float pxPerPt,
                               out Vector2 centre, out float scale)
    {
        float fit = Mathf.Min(1f, (safeUnits.width - 2f * Margin) / unitsW, (safeUnits.height - 2f * Margin) / unitsH);
        scale = Mathf.Max(.1f, Mathf.Min(fit, CapScale(unitsW, canvasScale, safePx, pxPerPt)));
        centre = safeUnits.center;
    }

    // The safe area of `rootRect`'s canvas in its own units (origin at the root's centre).
    public static bool SafeUnits(RectTransform root, out Rect safeUnits, out float canvasScale)
    {
        safeUnits = default(Rect);
        canvasScale = 1f;
        var canvas = root != null ? root.GetComponentInParent<Canvas>() : null;
        if (canvas == null) return false;
        canvasScale = Mathf.Max(canvas.rootCanvas.scaleFactor, .0001f);
        Rect safe = ScreenInfo.SafeArea;
        var r = root.rect;
        safeUnits = new Rect(safe.x / canvasScale - r.width * .5f, safe.y / canvasScale - r.height * .5f,
                             safe.width / canvasScale, safe.height / canvasScale);
        return true;
    }

    // Places `panel` (unitsW x unitsH, pivot centred, parented to the stretched `root`) and returns its scale.
    public static float Place(RectTransform root, RectTransform panel, float unitsW, float unitsH)
    {
        Rect safeUnits;
        float sf;
        if (!SafeUnits(root, out safeUnits, out sf)) return 1f;
        Vector2 centre;
        float scale;
        Compute(safeUnits, unitsW, unitsH, sf, ScreenInfo.SafeArea, UiScale.PxPerPoint, out centre, out scale);
        panel.anchoredPosition = centre;
        return scale;
    }

    // A card shrunk by `scale` keeps finger-sized tap targets: the touch area of each button grows (invisibly) to the
    // scene's 96-unit floor, up to `bottomRoom` units below its art and the rest above.
    public static void PadTouch(Button[] buttons, float scale, float buttonHeight, float bottomRoom)
    {
        float pad = Mathf.Max(0f, (UiScaleFloor.SceneTapUnits / Mathf.Max(scale, .1f) - buttonHeight) * .5f);
        float below = Mathf.Min(pad, bottomRoom), above = 2f * pad - below;
        foreach (var b in buttons)
        {
            var g = b != null ? b.targetGraphic : null;
            if (g != null) g.raycastPadding = new Vector4(0f, -below, 0f, -above);
        }
    }
}
