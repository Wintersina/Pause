using UnityEngine;

// The handful of easing curves the dock animations use. Pure functions on a
// 0..1 parameter; no tween library, no allocations.
public static class DockTween
{
    public static float Clamp01Range(float t, float start, float end)
    {
        return end <= start ? (t >= end ? 1f : 0f) : Mathf.Clamp01((t - start) / (end - start));
    }

    public static float OutCubic(float t)
    {
        t = 1f - Mathf.Clamp01(t);
        return 1f - t * t * t;
    }

    public static float InCubic(float t)
    {
        t = Mathf.Clamp01(t);
        return t * t * t;
    }

    public static float InOutCubic(float t)
    {
        t = Mathf.Clamp01(t);
        return t < .5f ? 4f * t * t * t : 1f - Mathf.Pow(-2f * t + 2f, 3f) * .5f;
    }

    // Overshoots slightly, then settles: used for the popup's appearance.
    public static float OutBack(float t)
    {
        t = Mathf.Clamp01(t);
        const float c1 = 1.70158f, c3 = c1 + 1f;
        float u = t - 1f;
        return 1f + c3 * u * u * u + c1 * u * u;
    }

    public static Vector3 Bezier(Vector3 a, Vector3 b, Vector3 c, float t)
    {
        float u = 1f - t;
        return u * u * a + 2f * u * t * b + t * t * c;
    }

    public static Vector3 BezierTangent(Vector3 a, Vector3 b, Vector3 c, float t)
    {
        return 2f * (1f - t) * (b - a) + 2f * t * (c - b);
    }
}

// The space dock's launch move (SpaceDock.LaunchRoutine), as reusable curves:
// clamps release, the hull lifts and eases back out of its berth (undock),
// then accelerates hard along a banked curve with its exhaust flaring and
// growing as it goes. The home-screen traffic's zip-offs and zoomers fly the
// same curves so every launch in the game reads as one family.
//
//   t: seconds into the undock (0..UndockTime)
//   f: 0..1 progress through the fly-out (FlightTime long)
public static class DockLaunch
{
    public const float Duration = 2.1f;
    public const float UndockTime = .8f;
    public const float FlightTime = Duration - UndockTime;

    public const float BackOffDistance = .09f;   // berth-space units the hull backs out
    public const float LiftGrow = .16f;          // scale gained lifting off the pad
    public const float FlightGrowth = .14f;      // and again climbing out
    public const float FlightExponent = 1.9f;    // the hard ease-in on the fly-out
    public const float ApproachDistance = 1.25f; // straight run out of the berth before the bank

    public static float Clamps(float t) { return DockTween.OutCubic(DockTween.Clamp01Range(t, 0f, .32f)); }
    public static float Lift(float t) { return DockTween.InOutCubic(DockTween.Clamp01Range(t, .12f, .75f)); }
    public static float BackOff(float t) { return DockTween.InOutCubic(DockTween.Clamp01Range(t, .22f, .8f)); }
    public static float UndockFlame(float lift) { return Mathf.Lerp(.55f, .7f, lift); }

    // Path parameter along the fly-out Bezier: slow off the mark, then hard.
    public static float Flight(float f) { return Mathf.Pow(Mathf.Clamp01(f), FlightExponent); }
    public static float FlightGrow(float f) { return 1f + FlightGrowth * DockTween.InOutCubic(f); }
    public static float Flare(float f) { return Mathf.Lerp(.7f, 1.6f, DockTween.OutCubic(f * 1.4f)); }
    // How far the hull has swung from its berth heading onto the path.
    public static float TurnIn(float f) { return Mathf.Clamp01(f * 3f); }
}
