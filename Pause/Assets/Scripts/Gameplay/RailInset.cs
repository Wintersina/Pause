using UnityEngine;

// How far the side rails are pushed out toward the screen edges on this
// screen: the one source every lane width reads its screen-aware part from.
//
// The gameplay camera is width-fitted (CameraFit.GameplayHalfWidth 3.72): on
// every phone the view is 7.44 u wide and only its height changes. At the
// authored layout each reinforced rail fills 14.7% of that width (its art
// from 2.61 to 3.70). Where the screen has room to spare the rails move out
// by up to MaxShift per side, so the flight lane is wider and the rail reads
// at ~11.7% of the width, as in the approved 900x1600 reference; up to a
// fifth of the rail's outer art (its outer conduit, not the lamp column) is
// then cut off by the screen edge.
//
// Room to spare, by the screen's height / width ratio r:
//   tall phones   r from 16:9 (no shift; up to 1.8) to 19.5:9 (full shift)
//                 and beyond
//                 -- their view is 16+ u tall, with height to spare;
//   tablets and foldables' inner screens
//                 r from 3:2 (no shift) to 4:3 (full shift) and squarer
//                 -- a physically big screen;
//   in between (16:9 and 16:10-ish phones and tablets) and landscape: the
//                 authored layout, exactly as before.
//
// Everything that knows the lane -- WorldPainter (the rails themselves, so
// BossRails.InnerEdge and the HUD inside the rails follow), ShipReach (the
// ship's sideways reach), SpawnLane (enemy lane), the pickup and atom lanes,
// the boss slots -- adds Shift (or calls Lane) instead of a constant of its
// own. A pure function of ScreenInfo: two int reads and a few flops, no state.
public static class RailInset
{
    // ---- tunables ----
    // Outward shift at full room, world units per side (3.2% of the 7.44 u view).
    public const float MaxShift = .24f;
    // Widen-the-lane knob: extra outward shift on EVERY portrait screen (the
    // 16:9s too), world units per side, on top of the room-based MaxShift.
    // 0.24 = lane half-width 2.70 -> 2.94 on 16:9 (+8.9% wider lane),
    // 2.94 -> 3.18 on tall phones (+8.2%). Set 0 for the old layout.
    public const float BaseShift = .24f;
    // Height / width ratios where the tall-phone ramp starts and is full.
    // (just past 16:9 -- the iPhone SE's 750x1334 is 16:9 to 0.05% -- and
    // just short of 19.5:9 -- an iPhone 13's 1170x2532 is 2.164)
    public const float TallStart = 1.8f, TallFull = 2.15f;
    // Height / width ratios where the tablet ramp starts and is full.
    public const float WideStart = 3f / 2f, WideFull = 4f / 3f;

    // Tools only (before / after renders): false lays every screen out as
    // before the inset existed.
    public static bool Enabled = true;
    // Tools only: false drops BaseShift (the before shot of the lane widening).
    public static bool WidenLane = true;

    // World units the rails move out on a w x h screen.
    public static float ShiftFor(float w, float h)
    {
        if (!Enabled || w <= 0f || h <= 0f || h < w) return 0f;   // no screen / landscape
        float r = h / w;
        float tall = Mathf.InverseLerp(TallStart, TallFull, r);
        float wide = Mathf.InverseLerp(WideStart, WideFull, r);
        float room = Mathf.Max(tall, wide);
        // ease in so a ratio just past the threshold barely moves
        room = room * room * (3f - 2f * room);
        return (WidenLane ? BaseShift : 0f) + MaxShift * room;
    }

    public static float ShiftFor(Vector2 screen) { return ShiftFor(screen.x, screen.y); }

    // The live screen (ScreenInfo: the device, or a test's override).
    public static float Shift { get { return ShiftFor(ScreenInfo.Width, ScreenInfo.Height); } }

    // An authored lane half-width (written for the authored rails) on this screen.
    public static float Lane(float authoredHalfWidth) { return authoredHalfWidth + Shift; }
    public static float LaneFor(float authoredHalfWidth, Vector2 screen) { return authoredHalfWidth + ShiftFor(screen); }

    // Star dust, heal atoms and the tutorial's pickups spawn with their
    // centre within +/- this (2.2 at the authored rails).
    public const float AuthoredPickupLane = 2.2f;
    public static float PickupLaneHalf { get { return Lane(AuthoredPickupLane); } }
}
