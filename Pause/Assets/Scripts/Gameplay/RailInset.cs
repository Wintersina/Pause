using UnityEngine;

// How far the side rails are pushed out toward the screen edges on this
// screen: the one source every lane width reads its screen-aware part from.
//
// The gameplay camera is width-fitted (CameraFit.GameplayHalfWidth 3.72): on
// every phone the view is 7.44 u wide and only its height changes. The
// reinforced rails were drawn 1.09 u wide (FullRailWidth: 14.7% of that
// width, art from 2.61 to 3.70); they are now RailWidthScale (80%) of that
// ("squash the rails down to 80%"): the art itself is squashed horizontally
// (Art/Worlds/Rails80~), and WorldPainter draws it RailWidth (0.87 u, 11.8%)
// wide. The outer edge stays where it was (3.70, at the screen's edge); the
// inner edge moves out by NarrowShift (0.22 u), so the flight lane is that
// much wider per side -- on EVERY screen, the batch editor's too, since it is
// the art's own width, not the screen's. Where the screen has room to spare
// the rails move out by up to MaxShift more per side, so the flight lane is
// wider still; up to a fifth of the rail's outer art (its outer conduit, not
// the lamp column) is then cut off by the screen edge (BaseShift and MaxShift
// are scaled with the rail, so the share cut off is what it was).
//
// Room to spare, by the screen's height / width ratio r:
//   tall phones   r from 16:9 (no shift; up to 1.8) to 19.5:9 (full shift)
//                 and beyond
//                 -- their view is 16+ u tall, with height to spare;
//   tablets and foldables' inner screens
//                 r from 3:2 (no shift) to 4:3 (full shift) and squarer
//                 -- a physically big screen;
//   in between (16:9 and 16:10-ish phones and tablets): the authored
//                 layout plus BaseShift, exactly as before.
//   wider than a phone's view (a foldable's inner screen, a square or
//                 landscape window): full room plus LaneMetrics.WideExtra,
//                 so the rails sit at the screen's edges and the lane
//                 takes the extra width (LaneMetrics has the rule).
//
// Everything that knows the lane -- WorldPainter (the rails themselves, so
// BossRails.InnerEdge and the HUD inside the rails follow), ShipReach (the
// ship's sideways reach), SpawnLane (enemy lane), the pickup and atom lanes,
// the boss slots -- adds Shift (or calls Lane) instead of a constant of its
// own. A pure function of ScreenInfo: two int reads and a few flops, no state.
public static class RailInset
{
    // ---- tunables ----
    // The rails' drawn width as a share of the 1.09 u they were drawn at
    // (the squashed art is this share of its old 725 px width too).
    public const float RailWidthScale = .8f;
    // The full-width rail's visible width (Frost's approved 1.25x of the
    // 1.43 u wall quad x 443 / 725 of its canvas: 1.092 u).
    public const float FullRailWidth = 1.43f * 1.25f * 443f / 725f;
    // The rail's visible width now (0.874 u, 11.7% of the phone view).
    public const float RailWidth = FullRailWidth * RailWidthScale;
    // The inner edge moves out by the width the rail lost; the outer edge
    // stays (0.218 u per side, on every screen).
    public const float NarrowShift = FullRailWidth - RailWidth;
    // Outward shift at full room, world units per side: 0.24 u for the
    // full-width rail (a fifth of its outer art off-screen), scaled with it.
    public const float MaxShift = .24f * RailWidthScale;
    // Widen-the-lane knob: extra outward shift on EVERY portrait screen (the
    // 16:9s too), world units per side, on top of the room-based MaxShift.
    // 0.24 u for the full-width rail, scaled with it (0.192). Set 0 for the
    // old layout.
    public const float BaseShift = .24f * RailWidthScale;
    // Height / width ratios where the tall-phone ramp starts and is full.
    // (just past 16:9 -- the iPhone SE's 750x1334 is 16:9 to 0.05% -- and
    // just short of 19.5:9 -- an iPhone 13's 1170x2532 is 2.164)
    public const float TallStart = 1.8f, TallFull = 2.15f;
    // Height / width ratios where the tablet ramp starts and is full.
    public const float WideStart = 3f / 2f, WideFull = 4f / 3f;

    // Tools only (before / after renders): false lays every screen out as
    // before the inset existed (the narrower rail's NarrowShift stays).
    public static bool Enabled = true;
    // Tools only: false drops BaseShift (the before shot of the lane widening).
    public static bool WidenLane = true;

    // World units the rails move out on a w x h screen: the room-based shift
    // above plus, on a screen wider than a phone's view, LaneMetrics.WideExtra
    // (the rails then sit against the screen's edges, the lane takes the room).
    // NarrowShift is always in: it is the art's width, not the screen's.
    public static float ShiftFor(float w, float h)
    {
        if (!Enabled || w <= 0f || h <= 0f) return NarrowShift;   // no screen
        return NarrowShift + RoomShiftFor(w, h) + LaneMetrics.WideExtraFor(w, h);
    }

    // The room-based part alone (phones and tablets; a landscape screen,
    // squarer than any tablet, counts as full room).
    public static float RoomShiftFor(float w, float h)
    {
        if (!Enabled || w <= 0f || h <= 0f) return 0f;
        float r = h / w;
        float tall = Mathf.InverseLerp(TallStart, TallFull, r);
        float wide = Mathf.InverseLerp(WideStart, WideFull, r);
        float room = Mathf.Max(tall, wide);
        // ease in so a ratio just past the threshold barely moves
        room = room * room * (3f - 2f * room);
        return (WidenLane ? BaseShift : 0f) + MaxShift * room;
    }

    public static float ShiftFor(Vector2 screen) { return ShiftFor(screen.x, screen.y); }

    // The live screen (ScreenInfo: the device, or a test's override). The
    // batch editor with no override (LaneMetrics.HasDevice false: a 640x480
    // window no player sees) keeps the authored layout: NarrowShift alone.
    public static float Shift { get { return LaneMetrics.HasDevice ? ShiftFor(ScreenInfo.Width, ScreenInfo.Height) : NarrowShift; } }

    // An authored lane half-width (written for the authored rails) on this screen.
    public static float Lane(float authoredHalfWidth) { return authoredHalfWidth + Shift; }
    public static float LaneFor(float authoredHalfWidth, Vector2 screen) { return authoredHalfWidth + ShiftFor(screen); }

    // Star dust, heal atoms and the tutorial's pickups spawn with their
    // centre within +/- this (2.2 at the authored rails).
    public const float AuthoredPickupLane = 2.2f;
    public static float PickupLaneHalf { get { return Lane(AuthoredPickupLane); } }
}
