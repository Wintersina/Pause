using UnityEngine;

// The results screen's star dust tick-up after the double dust ad pays: a pure clock from an old amount to the true
// new one (both in hundredths), eased out over about a second. Takes the time as an argument (the panel feeds it
// unscaled time) so it works with the world frozen and is testable. Always lands exactly on the target.
public sealed class DustTickUp
{
    public const float Duration = 1f;

    int from, to;
    float start, duration;

    public bool Active { get; private set; }
    public int Target { get { return to; } }

    public void Begin(int fromCents, int toCents, float now, float seconds = Duration)
    {
        from = fromCents; to = toCents; start = now; duration = Mathf.Max(.01f, seconds);
        Active = toCents != fromCents;
    }

    // 0..1 progress; ends the tick when it reaches 1.
    public float Progress(float now)
    {
        if (!Active) return 1f;
        float p = Mathf.Clamp01((now - start) / duration);
        if (p >= 1f) Active = false;
        return p;
    }

    // The amount to show now, in hundredths. Exactly the target once the tick has finished.
    public int Value(float now)
    {
        float p = Progress(now);
        if (p >= 1f) return to;
        float i = 1f - p;
        float e = 1f - i * i * i;
        return from + Mathf.RoundToInt((to - from) * e);
    }

    // Stops the tick (the panel is leaving) and returns the target so nothing is left half-counted.
    public int Finish()
    {
        Active = false;
        return to;
    }
}
