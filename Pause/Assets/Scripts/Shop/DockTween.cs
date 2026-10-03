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
