using UnityEngine;

// The force model: pure functions (no scene, no allocation), so the tests can walk every shape and target.
// Sample() returns a VELOCITY in u/s the field gives a target at `p` (zero outside it and before it acts).
public static class HazardForce
{
    public static bool Sample(HazardField f, Vector2 p, HazardTarget target, out Vector2 v, float envelopeOverride = -1f)
    {
        v = Vector2.zero;
        var d = f.def;
        float e = envelopeOverride >= 0f ? envelopeOverride : f.Envelope;
        if (e <= 0f) return false;
        TargetMix mix = d.MixOf(target);
        if (mix.gain == 0f) return false;
        float s = d.strength * e * mix.gain;
        switch (d.shape)
        {
            case HazardShape.Swirl:
            {
                Vector2 to = f.centre - p;
                float dist = to.magnitude;
                if (dist >= d.size) return false;
                float w = Mathf.Pow(1f - dist / d.size, 1.2f);
                Vector2 inward = dist > 1e-4f ? to / dist : Vector2.zero;
                Vector2 tangent = new Vector2(-inward.y, inward.x) * (f.side * Mathf.Min(1f, dist / (.15f * d.size)));   // `side` mirrors the spin only; the eye is calm
                float core = Mathf.Min(1f, dist / (.3f * d.size));             // the pull fades at the core: no jitter
                v = s * w * (inward * (d.pull * mix.pullMul * core) + tangent * (d.swirl * mix.swirlMul));
                return true;
            }
            case HazardShape.Front:
            {
                float half = d.size * .5f;
                float off = Mathf.Abs(p.y - f.frontY);
                if (off >= half) return false;
                float w = Mathf.Sin(Mathf.PI * (1f - off / half) * .5f);   // 0 at the rim, 1 in the middle
                v = s * w * new Vector2(d.push.x * f.side, d.push.y);
                return true;
            }
            default:
            {
                float half = d.size * .5f;
                float dx = p.x - f.centre.x;
                if (Mathf.Abs(dx) >= half || p.y < f.viewBottom || p.y > f.viewTop) return false;
                float w = 1f - Mathf.Abs(dx) / half; w = w * w * (3f - 2f * w);
                float gate = 1f;
                if (d.pulseHz > 0f)
                {
                    float sn = Mathf.Sin(2f * Mathf.PI * d.pulseHz * f.t);
                    gate = d.pulseDepth >= 1f ? Mathf.Clamp01(sn * 1.6f) : 1f - d.pulseDepth + d.pulseDepth * (.5f + .5f * sn);
                }
                float outward = 4f * w * (1f - w) * d.spread * Mathf.Sign(dx);
                v = s * gate * new Vector2(outward, w * d.lift);
                return true;
            }
        }
    }

    // The Column's pulse / burst gate (1 when it has none): shared by the force and the drawn stream.
    public static float Gate(HazardField f)
    {
        var d = f.def;
        if (d.pulseHz <= 0f) return 1f;
        float sn = Mathf.Sin(2f * Mathf.PI * d.pulseHz * f.t);
        return d.pulseDepth >= 1f ? Mathf.Clamp01(sn * 1.6f) : 1f - d.pulseDepth + d.pulseDepth * (.5f + .5f * sn);
    }

    public static Vector2 Clamp(Vector2 v, float max)
    {
        float m = v.sqrMagnitude;
        return m > max * max ? v * (max / Mathf.Sqrt(m)) : v;
    }
}
