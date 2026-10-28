using UnityEngine;

// The flight lane's width on this screen: the one source of truth every
// lane-aware piece of gameplay reads (rails, ship reach, spawners, bosses,
// elites, hazards, mines, tutorial, HUD frame).
//
// PHONES. The gameplay camera is width-fitted (CameraFit.GameplayHalfWidth
// 3.72): on every portrait phone the view is 7.44 u wide and only its height
// changes. The rails sit at their authored place plus RailInset's small
// room-based shift (0.24 .. 0.48 u per side). Nothing here changes that:
// WideExtra is exactly 0 on every screen whose width / height is at most
// 3.72 / 5 = 0.744 (every phone, 16:10 and 3:4 tablets to within 0.03 u).
//
// WIDE SCREENS (foldables' inner screens, tablets, square / landscape
// windows). The camera keeps its authored height (size 5) and shows more
// width: half-width 5 x w / h (CameraFit.GameplayViewHalfWidth). The rails
// used to stay where a phone has them and the extra width was dead backdrop
// outside them. Now the rails move out by WideExtra -- the view's extra
// half-width over the phone's -- so they sit against the screen edges exactly
// as on a phone (same thickness, same share of their art cut by the edge, the
// rail art only tiled vertically, never stretched), and the flight lane gets
// the room. Everything that used a phone-lane constant adds WideExtra
// (Widen) or reads the lane's live edges (InnerEdge / ShipReach.HalfWidth).
//
// DYNAMIC DIFFICULTY. A wider lane is easier (more room to dodge) and would
// look empty with a phone's spawn rate. Spawning and pattern density scale
// with the lane's width, roughly proportionally:
//
//     Density = clamp(1 + WideExtra / ReferenceLaneHalf, 1, MaxDensity)
//
// ReferenceLaneHalf (3.086 u) is the widest phone / tablet lane before any
// wide extra (BossRails.ReinforcedInnerEdge 2.606 + RailInset's full 0.48 u
// shift), so Density is the lane's width over that lane's width: 1 on every
// phone, ~1.41 on a 1:1 foldable, ~1.96 on a 4:3 landscape window. It
// multiplies the roster spawn rate, the threat ceiling and the pilot load
// (enmiesOnBoard / EnemyDensity), and the shot counts of the boss's spread
// patterns (fans, rain, mines; LaneMetrics.ScaleCount) -- spreads widen with
// the count at the SAME angular / lane pitch, so gaps between shots never
// shrink and every pattern still leaves its gap. WideExtra is clamped to
// MaxWideExtra (= ReferenceLaneHalf, so Density <= 2): past ~1.36:1
// (16:10 / 16:9 landscape, ultrawide windows) the rails stop moving out and
// the backdrop shows outside them, as every screen did before, so no extreme
// aspect can open an unbounded lane or an unbounded spawn rate.
//
// A pure function of ScreenInfo (the device, or a test's override): the
// lane follows a fold / unfold / rotation the frame it happens (RailFit
// re-lays the rails; everything else reads it live). The batch editor
// (CI, command-line tools) has no device screen (HasDevice): there, unless
// a test overrides ScreenInfo, the lane is laid out as it always was.
public static class LaneMetrics
{
    // ---- tunables ----
    // The phone view's half-width (every phone shows exactly this much).
    public const float PhoneViewHalfWidth = CameraFit.GameplayHalfWidth;
    // The widest pre-wide lane (inner rail edge, world u): reinforced rail
    // edge + RailInset's full room shift (BaseShift + MaxShift).
    public const float ReferenceLaneHalf = BossRails.ReinforcedInnerEdge + RailInset.BaseShift + RailInset.MaxShift;
    // At most this much extra half-width (world u per side): Density <= 2.
    public const float MaxWideExtra = ReferenceLaneHalf;
    public const float MaxDensity = 2f;

    // Tools only (before / after renders): false = every screen keeps the
    // phone lane (the rails no further out than RailInset puts them).
    public static bool Enabled = true;

    // ---- the screen ----
    // True when the live screen is a real one: a device / editor Game view,
    // or a test's ScreenInfo override. False in the batch editor (CI,
    // command-line tools) with no override, where Screen is a 640x480
    // landscape window that no player ever sees: there the lane keeps the
    // layout it always had (no rail shift, no wide extra, Density 1).
    public static bool HasDevice { get { return ScreenInfo.Overridden || !Application.isBatchMode; } }

    // The gameplay view's half-width on a w x h screen (CameraFit's).
    public static float ViewHalfWidthFor(float w, float h) { return CameraFit.GameplayViewHalfWidth(new Vector2(w, h)); }
    // The live one (the phone view's in the batch editor with no override).
    public static float ViewHalfWidth { get { return HasDevice ? ViewHalfWidthFor(ScreenInfo.Width, ScreenInfo.Height) : PhoneViewHalfWidth; } }

    // ---- the lane ----
    // World units the rails (and every lane span) move out per side beyond
    // a phone's layout: the view's extra half-width, clamped.
    public static float WideExtraFor(float w, float h)
    {
        if (!Enabled || w <= 0f || h <= 0f) return 0f;
        return Mathf.Clamp(ViewHalfWidthFor(w, h) - PhoneViewHalfWidth, 0f, MaxWideExtra);
    }
    public static float WideExtraFor(Vector2 screen) { return WideExtraFor(screen.x, screen.y); }
    public static float WideExtra { get { return HasDevice ? WideExtraFor(ScreenInfo.Width, ScreenInfo.Height) : 0f; } }

    // How far the rails move out from their authored place in all (RailInset's
    // room shift + WideExtra).
    public static float RailShiftFor(float w, float h) { return RailInset.ShiftFor(w, h); }
    public static float RailShift { get { return RailInset.Shift; } }

    // The drawn rails' inner edge on a w x h screen, as WorldPainter lays them out.
    public static float InnerEdgeFor(float w, float h) { return BossRails.ReinforcedInnerEdge + RailShiftFor(w, h); }
    public static float InnerEdgeFor(Vector2 screen) { return InnerEdgeFor(screen.x, screen.y); }
    // The live one: the measured walls when they are up, else where they would be.
    public static float InnerEdge { get { return BossRails.DrawnInnerEdge; } }

    // A half-width (or a side x) authored for the phone lane, on this screen:
    // pushed out with the rails on wide screens, unchanged on phones.
    public static float Widen(float phoneHalfWidth) { return phoneHalfWidth + WideExtra; }
    public static float WidenFor(float phoneHalfWidth, Vector2 screen) { return phoneHalfWidth + WideExtraFor(screen); }

    // ---- dynamic difficulty ----
    public static float DensityFor(float w, float h)
    {
        return Mathf.Clamp(1f + WideExtraFor(w, h) / ReferenceLaneHalf, 1f, MaxDensity);
    }
    public static float DensityFor(Vector2 screen) { return DensityFor(screen.x, screen.y); }
    public static float Density { get { return HasDevice ? DensityFor(ScreenInfo.Width, ScreenInfo.Height) : 1f; } }

    // A count authored for the phone lane (shots in a fan, rain drops, mines),
    // on a lane `density` times as wide: never fewer than authored, never more
    // than `max`.
    public static int ScaleCount(int authored, float density, int max = int.MaxValue)
    {
        if (authored <= 0) return authored;
        int n = Mathf.Max(authored, Mathf.RoundToInt(authored * Mathf.Max(1f, density)));
        return Mathf.Min(n, Mathf.Max(authored, max));
    }
    public static int ScaleCount(int authored) { return ScaleCount(authored, Density); }
}
