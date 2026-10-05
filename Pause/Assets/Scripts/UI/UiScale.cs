using UnityEngine;

// Physical sizes for UI: how many screen pixels make one point (iOS) or one
// dp (Android) on the screen ScreenInfo describes, and the comfortable
// minimum tap target (Apple HIG 44 pt, Material 48 dp) in pixels.
//
// Density comes from ScreenInfo.Dpi (Screen.dpi on a device, a synthetic
// value in the screen-fit rig):
//   * Android: dpi / 160 (one dp is 1/160 inch).
//   * iOS reports the panel's ppi; the point scale is the device's @2x / @3x:
//     3 from 380 ppi up (every @3x iPhone, the downsampled Plus models
//     included), else 2 (@2x iPhones, every iPad).
//   * Unreported or implausible (0, under 100 or over 1000 dpi): assume the
//     screen is as small as a phone / tablet of its shape can be, so the
//     estimate is never LOWER than the truth and a tap target sized from it
//     is never under the minimum: a phone shape (long side >= 1.7x the
//     short) is taken as 320 dp wide (the narrowest Android phone class), a
//     squarer one (tablet, foldable inner screen, iPad) as 600 dp wide;
//     clamped to 1..4 px per point.
// Pure functions of their inputs (the *For forms) so tests can feed any
// device; the properties read the live / overridden ScreenInfo. Neither
// allocates.
public static class UiScale
{
    public const float MinTapPt = 44f;   // iOS, points
    public const float MinTapDp = 48f;   // Android, dp
    public const float MinPlausibleDpi = 100f, MaxPlausibleDpi = 1000f;
    public const float FallbackPhoneWidthDp = 320f, FallbackTabletWidthDp = 600f;
    public const float PhoneAspect = 1.7f;

    public static bool Plausible(float dpi) { return dpi >= MinPlausibleDpi && dpi <= MaxPlausibleDpi; }

    public static float PxPerPointFor(float dpi, bool ios, int w, int h)
    {
        if (Plausible(dpi)) return ios ? (dpi >= 380f ? 3f : 2f) : dpi / 160f;
        float shortSide = Mathf.Max(1f, Mathf.Min(w, h)), longSide = Mathf.Max(w, h);
        float widthDp = longSide >= shortSide * PhoneAspect ? FallbackPhoneWidthDp : FallbackTabletWidthDp;
        return Mathf.Clamp(shortSide / widthDp, 1f, 4f);
    }

    public static float MinTapPointsFor(bool ios) { return ios ? MinTapPt : MinTapDp; }

    public static float PxPerPoint { get { return PxPerPointFor(ScreenInfo.Dpi, ScreenInfo.IsIos, ScreenInfo.Width, ScreenInfo.Height); } }
    public static float MinTapPoints { get { return MinTapPointsFor(ScreenInfo.IsIos); } }
    public static float MinTapPx { get { return MinTapPoints * PxPerPoint; } }
}
