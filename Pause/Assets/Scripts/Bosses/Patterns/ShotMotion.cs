using UnityEngine;

// PARAMETRIC SHOT MOTION (the bullet-pattern library's mover): a boss shot's path as a function of its age, so a pattern
// costs no per-bullet MonoBehaviour logic. BossProjectile.Step and PatternSim.Advance both call ShotMotion.Apply after the
// straight-line move, so the sim the tests measure flies the same paths as the game. Plain structs and maths: no allocation.
public enum MotionKind
{
    None,
    Sway,      // sine offset sideways of the heading (a weaving stream); a cos twin is the same with phase +90 / +180
    Bob,       // sine offset straight up and down (a wave wall: the row bobs in a travelling wave)
    Curve,     // the heading turns `a` deg/s for `sec` seconds (a tornado's swirl), speed x `b` over that time
    Accel,     // speed x 1 -> `a` over `sec` seconds (slow then fast)
    Orbit,     // a figure-8 round `anchor` for `sec` seconds, then released outward at speed `a`
}

public struct PMotion
{
    public MotionKind kind;
    public float amp;          // sway / bob: u. orbit: radius u
    public float hz;           // cycles a second
    public float phase;        // radians
    public float sec;          // curve / accel: how long it lasts. orbit: seconds held before release
    public float delay;        // seconds before the motion starts (a deploying shot's spray)
    public float a, b;         // see MotionKind
    public Vector2 anchor;     // orbit centre
    public Vector2 last;       // state: the offset already applied
    public float mult;         // state: accel's multiplier so far

    public bool Active => kind != MotionKind.None;
}

public static class ShotMotion
{
    public const float RampSeconds = .45f;   // a sway / bob eases in over this long, so a shot leaves its muzzle straight

    static float Ramp(float t) { float x = Mathf.Clamp01(t / RampSeconds); return x * x * (3f - 2f * x); }

    // One step: `p` is the position after the straight move, `v` the velocity, `age` the shot's age after this step.
    // Returns true when `v` changed (the caller turns the art).
    public static bool Apply(ref PMotion m, ref Vector2 p, ref Vector2 v, float age, float dt)
    {
        float t = age - m.delay;
        if (m.kind == MotionKind.None || t <= 0f) return false;
        switch (m.kind)
        {
            case MotionKind.Sway:
            case MotionKind.Bob:
            {
                float off = m.amp * Ramp(t) * Mathf.Sin(6.2831853f * m.hz * t + m.phase);
                Vector2 axis;
                if (m.kind == MotionKind.Bob) axis = Vector2.up;
                else
                {
                    float sp = v.magnitude;
                    axis = sp > 1e-4f ? new Vector2(-v.y, v.x) / sp : Vector2.right;
                }
                var now = axis * off;
                p += now - m.last;
                m.last = now;
                return false;
            }
            case MotionKind.Curve:
            {
                if (t - dt >= m.sec) return false;
                float ease = Mathf.Clamp01((m.sec - t) / .3f);
                float turn = m.a * dt * ease * Mathf.Deg2Rad;
                float c = Mathf.Cos(turn), s = Mathf.Sin(turn);
                v = new Vector2(v.x * c - v.y * s, v.x * s + v.y * c);
                if (m.b > 0f && m.b != 1f && m.sec > 0f) v *= Mathf.Pow(m.b, Mathf.Min(dt, m.sec) / m.sec);
                return true;
            }
            case MotionKind.Accel:
            {
                float x = Mathf.Clamp01(t / Mathf.Max(.05f, m.sec));
                float now = Mathf.Lerp(1f, m.a, x * x * (3f - 2f * x));
                float prev = m.mult <= 0f ? 1f : m.mult;
                if (Mathf.Abs(now - prev) > 1e-5f) v *= now / prev;
                m.mult = now;
                return true;
            }
            case MotionKind.Orbit:
            {
                if (t < m.sec)
                {
                    float th = 6.2831853f * m.hz * t + m.phase;
                    float r = m.amp * Ramp(t * 1.6f);
                    p = m.anchor + new Vector2(r * Mathf.Sin(th), r * m.b * Mathf.Sin(2f * th));
                    v = Vector2.zero;
                    return false;
                }
                var out_ = p - m.anchor;
                out_.y = -(Mathf.Abs(out_.y) + .45f * Mathf.Max(.2f, m.amp));
                v = out_.normalized * m.a;
                m.kind = MotionKind.None;
                return true;
            }
        }
        return false;
    }
}
