using UnityEngine;

// Where the gameplay HUD's top band goes: the score read-out on the left,
// the home / replay quick actions on the right (and the boss chip with them).
//
// The band is anchored to the flight lane, not the screen: its left and right
// ends sit just inside the side rails' inner edges (BossRails.InnerEdge, the
// edge gameplay itself uses), so nothing in it is drawn over rail art, and
// never outside the safe area's own side margins (waterfall edges, a
// landscape notch). Its top sits just under the safe area's top, and drops
// below any display cutout (notch, Dynamic Island, punch-hole) that reaches
// into its span with less than CutoutClearance to spare; a cutout out over a
// rail (a corner punch-hole) is beside the band and leaves it alone.
//
// Pure arithmetic over screen pixels (origin bottom-left) so it can be tested
// for any screen, safe area and cutout set.
public static class TopBand
{
    // All in quick-action canvas units (PauseQuickActions.CanvasScaleFor).
    // Clear strip between a rail's inner edge and the band.
    public const float LaneMargin = 6f;
    // Kept between any cutout and the band.
    public const float CutoutClearance = 10f;
    // The read-out shrinks to share the lane with the quick actions; on every
    // supported screen it keeps at least this share of its authored size
    // (its smallest type, 26 units, stays >= ~28 px on a 1080-wide phone).
    public const float ReadoutMinScale = .8f;

    public struct Frame
    {
        public float left, right, top;   // screen px

        public bool Same(Frame o) { return left == o.left && right == o.right && top == o.top; }
    }

    static readonly Rect[] NoCutouts = new Rect[0];
    static Rect[] liveCutouts = NoCutouts;
    static Rect liveSafe;
    static Vector2 liveScreen;
    static bool liveRead;

    // The device's cutouts when `safe` / `screen` are the live ones (read
    // again only when those change: Screen.cutouts allocates), none for a
    // hypothetical screen.
    public static Rect[] CutoutsFor(Rect safe, Vector2 screen)
    {
        if (screen.x != Screen.width || screen.y != Screen.height || safe != Screen.safeArea) return NoCutouts;
        if (!liveRead || liveSafe != safe || liveScreen != screen)
        {
            liveCutouts = Screen.cutouts ?? NoCutouts;
            liveSafe = safe;
            liveScreen = screen;
            liveRead = true;
        }
        return liveCutouts;
    }

    // The band for the rails as they stand (before a world is painted, the
    // authored wall edge) and the device's own cutouts.
    public static Frame FrameFor(Rect safe, Vector2 screen)
    {
        return FrameFor(safe, screen, BossRails.InnerEdge, CutoutsFor(safe, screen));
    }

    // `railInnerEdge`: world x of the rails' inner edges (<= 0: no rails, the
    // band spans the safe area). The gameplay camera is centred on the lane
    // and CameraFit sets how much of the world the width shows.
    public static Frame FrameFor(Rect safe, Vector2 screen, float railInnerEdge, Rect[] cutouts)
    {
        float s = PauseQuickActions.CanvasScaleFor(screen);
        var f = new Frame
        {
            left = safe.xMin + PauseQuickActions.EdgeMargin * s,
            right = safe.xMax - PauseQuickActions.EdgeMargin * s,
            top = safe.yMax - PauseQuickActions.TopMargin * s,
        };
        if (railInnerEdge > 0f && screen.x > 0f && screen.y > 0f)
        {
            float lane = railInnerEdge / CameraFit.GameplayViewHalfWidth(screen) * screen.x * .5f;
            f.left = Mathf.Max(f.left, screen.x * .5f - lane + LaneMargin * s);
            f.right = Mathf.Min(f.right, screen.x * .5f + lane - LaneMargin * s);
        }
        if (cutouts != null)
        {
            float clear = CutoutClearance * s;
            for (int i = 0; i < cutouts.Length; i++)
            {
                Rect c = cutouts[i];
                if (c.width <= 0f || c.height <= 0f) continue;
                if (c.yMax <= screen.y * .5f) continue;                                 // a bottom cutout
                if (c.xMax + clear <= f.left || c.xMin - clear >= f.right) continue;    // beside the band
                f.top = Mathf.Min(f.top, c.yMin - clear);
            }
        }
        return f;
    }
}
