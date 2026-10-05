using UnityEngine;

// The gameplay view in world units, as the device shows it: the camera's
// visible top and bottom, the safe area's bottom and top (home indicator /
// gesture bar, status bar, cutouts), and the bottom edge of the HUD's top
// band (score read-out, home / replay icons, the boss chip under them).
//
// Everything that has to sit "in the upper part of the screen" or "clear of
// the home indicator" reads it from here instead of a constant: the camera
// is width-fitted (CameraFit), so its height in world units is 10 u as
// authored, ~13.2 u on a 16:9 phone and ~17.4 u on a 21:9 one, and the HUD
// and the insets are screen pixels.
//
// Pure arithmetic (For) for tests and any hypothetical screen; Live reads the
// main camera and ScreenInfo and is recomputed only when one of them changes
// (a fold / unfold, a rotation, the HUD being laid out), so reading it every
// frame costs a handful of compares and allocates nothing.
public static class PlayField
{
    public struct Frame
    {
        public float bottom, top;          // the camera's visible edges (world y)
        public float safeBottom, safeTop;  // the safe area's edges (world y)
        public float bandBottom;           // the HUD top band's lowest edge (world y); = safeTop without a band
        public bool hasBand;               // false: no portrait screen to lay a band out on (editor, batch)
        public float Height { get { return top - bottom; } }
        // A point `share` of the way up the view (0 its bottom, 1 its top).
        public float At(float share) { return bottom + share * (top - bottom); }
        public float ShareOf(float y) { return Height > 0f ? (y - bottom) / Height : 0f; }
    }

    // `viewBottom` / `viewHeight`: the camera's visible world span; `screen`,
    // `safe`: pixels (origin bottom-left). `bandBottomPx` < 0: no band.
    public static Frame For(float viewBottom, float viewHeight, Vector2 screen, Rect safe, float bandBottomPx)
    {
        var f = new Frame { bottom = viewBottom, top = viewBottom + viewHeight };
        f.safeBottom = f.bottom;
        f.safeTop = f.top;
        f.bandBottom = f.top;
        if (screen.y <= 0f || viewHeight <= 0f) return f;
        float unit = viewHeight / screen.y;   // world units per pixel
        f.safeBottom = viewBottom + Mathf.Clamp(safe.yMin, 0f, screen.y) * unit;
        f.safeTop = viewBottom + Mathf.Clamp(safe.yMax, 0f, screen.y) * unit;
        f.bandBottom = f.safeTop;
        if (bandBottomPx >= 0f)
        {
            f.hasBand = true;
            f.bandBottom = viewBottom + Mathf.Clamp(bandBottomPx, 0f, screen.y) * unit;
        }
        return f;
    }

    // The lowest pixel row the top band always covers on this screen (px,
    // origin bottom-left): the home / replay quick actions and the score
    // read-out `hud` (zero-sized when there is none). The boss warning's
    // chip under the icons and its banner are not part of it: they are up
    // only in the 30 s before a boss and burst as its intro starts
    // (BossWarningHud), and nothing this frame is placed for those seconds.
    // -1 on a screen with no portrait band (a landscape window, batch mode).
    public static float BandBottomPx(Rect safe, Vector2 screen, Rect hud, Rect[] cutouts)
    {
        if (screen.x <= 0f || screen.y <= 0f || screen.y < screen.x) return -1f;
        var band = TopBand.FrameFor(safe, screen, BossRails.InnerEdge, cutouts);
        float y = PauseQuickActions.ScreenRectFor(band, screen).yMin;
        if (hud.height > 0f) y = Mathf.Min(y, hud.yMin);
        return y;
    }

    // ---- live ---------------------------------------------------------------

    static Frame live;
    static bool liveValid;
    static float keyBottom, keyHeight, keyEdge;
    static int keyW, keyH, keyFrame = -1000;
    static Rect keySafe;
    static bool keyHud;
    static Vector2 keyHudSize;
    static HudStyler styler;

    // Frames between two looks for the HUD while it is not there yet.
    const int HudRetryFrames = 30;

    public static Frame Live
    {
        get
        {
            var cam = Camera.main;
            float bottom = CameraFit.ViewBottom, height = CameraFit.ViewTop - bottom;
            int w = ScreenInfo.Width, h = ScreenInfo.Height;
            Rect safe = ScreenInfo.SafeArea;
            float edge = BossRails.InnerEdge;
            bool hud = hudRect != null || (styler != null && styler.HudRoot != null);
            // the read-out's own size too: its layout can settle after it is found
            Vector2 hudSize = hudRect == null && hud ? styler.HudRoot.rect.size : Vector2.zero;
            bool stale = !liveValid || bottom != keyBottom || height != keyHeight || w != keyW || h != keyH ||
                         safe != keySafe || edge != keyEdge || hud != keyHud || hudSize != keyHudSize;
            if (!hud && Application.isPlaying && Time.frameCount - keyFrame >= HudRetryFrames)
            {
                keyFrame = Time.frameCount;
                styler = Object.FindFirstObjectByType<HudStyler>();
                if (styler != null && styler.HudRoot != null) { hud = true; stale = true; hudSize = styler.HudRoot.rect.size; }
            }
            if (!stale) return live;
            liveValid = true;
            keyBottom = bottom; keyHeight = height; keyW = w; keyH = h; keySafe = safe; keyEdge = edge; keyHud = hud; keyHudSize = hudSize;
            var screen = new Vector2(w, h);
            Rect readout = cam != null ? HudRect(screen, safe) : default(Rect);
            Rect[] cutouts = ScreenInfo.Overridden ? ScreenInfo.Cutouts : TopBand.CutoutsFor(safe, screen);
            live = For(bottom, height, screen, safe, cam != null && cam.orthographic ? BandBottomPx(safe, screen, readout, cutouts) : -1f);
            return live;
        }
    }

    // The score read-out's rect on this screen (px), if the HUD is up.
    static Rect HudRect(Vector2 screen, Rect safe)
    {
        if (hudRect != null) return hudRect(screen, safe);
        if (styler == null || styler.HudRoot == null) return default(Rect);
        var canvas = styler.HudRoot.GetComponentInParent<Canvas>();
        if (canvas == null) return default(Rect);
        canvas = canvas.rootCanvas;
        var scaler = canvas.GetComponent<UnityEngine.UI.CanvasScaler>();
        float scale = HudStyler.HudCanvasScale(canvas, scaler, screen);
        var band = TopBand.FrameFor(safe, screen, BossRails.InnerEdge,
                                    ScreenInfo.Overridden ? ScreenInfo.Cutouts : TopBand.CutoutsFor(safe, screen));
        return HudStyler.HudScreenRect(band, screen, scale, styler.HudRoot.rect.size);
    }

    // Tests: forget the cached frame and the HUD it found.
    public static void Reset()
    {
        liveValid = false;
        styler = null;
        hudRect = null;
        keyFrame = -1000;
    }

    // Tests / tools: use this HUD read-out from now on (null: look it up again).
    public static void UseHud(HudStyler hud)
    {
        styler = hud;
        hudRect = null;
        liveValid = false;
    }

    // Tests / tools without the HUD's scene: the read-out's rect (px) for a
    // screen and safe area.
    public static void UseHud(System.Func<Vector2, Rect, Rect> rect)
    {
        hudRect = rect;
        liveValid = false;
    }

    static System.Func<Vector2, Rect, Rect> hudRect;
}
