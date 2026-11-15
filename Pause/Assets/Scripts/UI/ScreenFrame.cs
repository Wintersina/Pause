using UnityEngine;

// The shared frame every menu screen can lay itself out in: a pure function of the safe area, so tests can feed
// any shape. Generalised from CodexPanel.ComputeLayout's approach (a centred content column, side rails, a
// footer) but with the content width a FRACTION of the safe width per aspect class instead of one fixed cap, so a
// 2560x1080 window and a 1280x720 one both use their space in proportion.
//
//   Tall    width / height < .8        a phone in portrait: content is the whole safe area
//   Square  .8 .. 1.25                 a foldable's inner screen, the Z Flip cover, a 4:3 tablet
//   Wide    1.25 .. 1.9                a laptop / desktop window, a phone in landscape
//   Ultra   > 1.9                      an ultrawide window
//
// `scale` is the UI-unit hint for Wide / Ultra (safe height over ReferenceHeight, so type and controls track the
// window's height); it is 1 for Tall and Square, which keep their existing point-based sizing.
// ScreenLayout.IsWide (the .8 breakpoint the Codex and Dock already use) is unchanged.
public static class ScreenFrame
{
    public enum Aspect { Tall, Square, Wide, Ultra }

    public const float SquareFrom = ScreenLayout.WideAspect, WideFrom = 1.25f, UltraFrom = 1.9f;
    public const float ReferenceHeight = 1100f;
    // share of the safe width the content column takes, per class
    public const float TallShare = 1f, SquareShare = .94f, WideShare = .8f, UltraShare = .62f;
    // the footer strip (back / dismiss row): this share of the safe height, never under FooterMin world px of it
    public const float FooterShare = .09f;

    public struct Frame
    {
        public Aspect aspect;
        public Rect safe, content, left, right, footer;
        public float scale;
        public bool IsWideOrUltra { get { return aspect == Aspect.Wide || aspect == Aspect.Ultra; } }
    }

    public static Aspect Classify(float width, float height)
    {
        if (height <= 0f) return Aspect.Tall;
        float a = width / height;
        if (a < SquareFrom) return Aspect.Tall;
        if (a < WideFrom) return Aspect.Square;
        return a < UltraFrom ? Aspect.Wide : Aspect.Ultra;
    }

    public static Aspect Classify(Rect safe) { return Classify(safe.width, safe.height); }

    public static float ContentShare(Aspect a)
    {
        switch (a)
        {
            case Aspect.Square: return SquareShare;
            case Aspect.Wide: return WideShare;
            case Aspect.Ultra: return UltraShare;
            default: return TallShare;
        }
    }

    public static Frame Compute(Rect safe)
    {
        var f = new Frame { safe = safe, aspect = Classify(safe) };
        float w = Mathf.Max(0f, safe.width), h = Mathf.Max(0f, safe.height);
        float cw = w * ContentShare(f.aspect);
        float fh = h * FooterShare;
        float cx = safe.center.x;
        f.footer = new Rect(cx - cw * .5f, safe.yMin, cw, fh);
        f.content = new Rect(cx - cw * .5f, safe.yMin + fh, cw, h - fh);
        float side = (w - cw) * .5f;
        f.left = new Rect(safe.xMin, safe.yMin, side, h);
        f.right = new Rect(safe.xMax - side, safe.yMin, side, h);
        f.scale = f.IsWideOrUltra ? h / ReferenceHeight : 1f;
        return f;
    }
}
