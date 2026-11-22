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
            case HazardShape.Sweep:
            {
                // a train of vertical bands crossing sideways: inside a band the push is along the travel direction, strongest at the
                // shocked leading edge (`shock`), tapering to nothing at both rims. Bands do not overlap, so the strongest one counts.
                float half = d.size * .5f;
                float best = 0f;
                for (int b = 0; b < d.bands; b++)
                {
                    float cx = f.sweepX - f.side * b * d.bandGap;
                    float u = (p.x - cx) * f.side;            // + toward the leading rim
                    if (Mathf.Abs(u) >= half) continue;
                    if (InHole(f, b, p.y, 0f)) continue;      // the opening: no push at all
                    float w = Mathf.Sin(Mathf.PI * (1f - Mathf.Abs(u) / half) * .5f);
                    w *= 1f - d.shock + d.shock * 2f * Mathf.Clamp01(.5f + u / d.size);
                    if (w > best) best = w;
                }
                if (best <= 0f) return false;
                v = s * best * new Vector2(d.push.x * f.side, d.push.y);
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

    // Does a Sweep's band (any band of the train) touch a disc of `radius` at `p`? Geometry only: no envelope, no mix (the flares wipe
    // shots and stun ships by touch). False for every other shape.
    public static bool Touches(HazardField f, Vector2 p, float radius)
    {
        var d = f.def;
        if (d.shape != HazardShape.Sweep) return false;
        float half = d.size * .5f + radius;
        for (int b = 0; b < d.bands; b++)
        {
            float cx = f.sweepX - f.side * b * d.bandGap;
            if (Mathf.Abs(p.x - cx) < half && !InHole(f, b, p.y, radius)) return true;   // a disc wholly inside the opening is not touched
        }
        return false;
    }

    // ---- the slip-through hole of a Sweep band ----
    public const float HoleEdgeBottom = 1.4f, HoleEdgeTop = 2.2f;   // keep the opening this far inside the view's bottom / top (the playable lane)
    public const float HoleMinApart = .85f;                           // consecutive holes of a train differ by at least this x the hole's height

    public static bool HasHole(HazardField f, int band) { return f.def.holeSize > 0f && band < f.holes; }

    // Is a disc of `radius` at height y wholly inside band `band`'s opening?
    public static bool InHole(HazardField f, int band, float y, float radius)
    {
        if (!HasHole(f, band)) return false;
        return Mathf.Abs(y - f.holeY[band]) <= f.def.holeSize * .5f - radius;
    }

    // [low, high] the centre of a hole may take for this view.
    public static void HoleRange(HazardField f, out float lo, out float hi)
    {
        float half = f.def.holeSize * .5f;
        lo = f.viewBottom + HoleEdgeBottom + half;
        hi = f.viewTop - HoleEdgeTop - half;
        // a short view: two openings 0.85 heights apart need this much room, so give up some of the edge margin (never beyond the view itself)
        float need = f.def.holeSize * HoleMinApart * 2f + .02f;
        if (f.def.bands > 1 && hi - lo < need)
        {
            float floor = f.viewBottom + half + .1f, ceil = f.viewTop - half - .1f;
            lo = Mathf.Max(floor, lo - (need - (hi - lo)) * .5f);
            hi = Mathf.Min(ceil, lo + need);                      // whatever the bottom could not give comes off the top
            if (hi - lo < need) lo = Mathf.Max(floor, hi - need);
        }
        if (hi < lo) lo = hi = (f.viewBottom + f.viewTop) * .5f;
    }

    // How far apart consecutive openings are kept: HoleMinApart x the opening's height (less only on a view too short to fit it even with the margins given up).
    public static float HoleApart(HazardField f)
    {
        HoleRange(f, out float lo, out float hi);
        return Mathf.Min(f.def.holeSize * HoleMinApart, (hi - lo) * .5f);
    }

    // Roll one hole per band (rand returns 0..1): inside the lane, and each hole at least HoleMinApart x its height from the one before.
    public static void RollHoles(HazardField f, System.Func<float> rand)
    {
        f.holes = 0;
        if (f.def.shape != HazardShape.Sweep || f.def.holeSize <= 0f) return;
        HoleRange(f, out float lo, out float hi);
        float apart = HoleApart(f);
        int n = Mathf.Min(f.def.bands, HazardField.MaxBands);
        for (int b = 0; b < n; b++)
        {
            float y = Mathf.Lerp(lo, hi, rand());
            if (b > 0 && Mathf.Abs(y - f.holeY[b - 1]) < apart)
            {
                // too close: step to the side of the previous hole that has more room (the other side if it does not fit)
                float prev = f.holeY[b - 1];
                float up = prev + apart, down = prev - apart;
                bool upFits = up <= hi, downFits = down >= lo;
                if (upFits && downFits) y = (y >= prev) ? up : down;
                else if (upFits) y = up;
                else if (downFits) y = down;
                else y = (prev - lo > hi - prev) ? lo : hi;   // a view too short to fit both: as far as it goes
            }
            f.holeY[b] = y;
            f.holes = b + 1;
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

    // The field's RIGID sideways part: what a Front or Sweep would do to a body that sits in it, as ONE number (u/s) that does not
    // depend on where the target is. Boss waves use only this (WeatherWind.BossShotWind): every live bullet moves the same, so a
    // wave's gap moves with its bullets instead of closing.
    public static float RigidX(HazardField f, HazardTarget target)
    {
        var d = f.def;
        if (d.shape != HazardShape.Front && d.shape != HazardShape.Sweep) return 0f;
        float e = f.Envelope;
        if (e <= 0f) return 0f;
        return d.strength * e * d.MixOf(target).gain * d.push.x * f.side;
    }

    public static Vector2 Clamp(Vector2 v, float max)
    {
        float m = v.sqrMagnitude;
        return m > max * max ? v * (max / Mathf.Sqrt(m)) : v;
    }
}
