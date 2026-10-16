using UnityEngine;

// BOUNCING BOSS SHOTS (docs/boss-bullet-waves.md, "Ricochets"): about BouncyFraction of the boss bullets that meet a side
// rail ricochet off it instead of splashing. A ricochet mirrors the shot off the rail's inner face (angle in = angle out),
// keeps SpeedKeep of its speed, and a shot bounces at most MaxBounces times: the next rail contact splashes it as usual.
// Top / bottom exits, the ship, the shield and a bomb end it like any hostile shot; hitbox, speed class and damage are
// the shot's own.
//
// Which shots: only straight-flying bullets (no homing seed, no lob, no bomb / mine and not a bomb's burst shards) whose
// course meets a rail inside the play field. A wall / slant falls straight down its lane and never meets a rail, so the
// eligible bullets are the rings, fans and aimed / spiral / pincer streams. The pick is a running accumulator
// (BounceSelector): every eligible bullet adds the fraction, a bullet is picked when it reaches one -- exactly the
// fraction over a fight, spread evenly through each wave (no clumps, never a whole wave).
//
// Fairness: a picked bullet is only made bouncy when it is CERTIFIED. Trace() follows its course after the first bounce
// through the ship's band (the zone it can fly in, grown by the ship's and the shot's radius); the x range it sweeps there
// (grown by the worst weather drift) is its SHADOW. In the song (BossWaveVoice) the shadow must miss every x the ship
// uses to reach this wave's gap, and every later gap is planned around the live shadows (WaveGaps). A vetoed bullet keeps
// the accumulator's credit, so the next eligible bullet is picked instead.
public static class BossBounce
{
    public const float BouncyFraction = .30f;   // of the bullets that meet a rail
    public const int MaxBounces = 2;            // ricochets; the next rail contact splashes it
    public const float SpeedKeep = .96f;        // speed kept per ricochet (4 % lost); the angle is mirrored exactly
    public const float MaxCredit = 2f;          // the accumulator never banks more than this (a veto streak cannot burst)
    public const float ShadowMargin = .1f;      // u: on top of ship radius + shot radius
    public const float RowSlack = .5f;          // u: the song's shadows cover the ship's row this far up and down (plus R)

    // Per-boss tuning (world index order: Space, Frost, Verdant, Ember, Tide, Storm, Desert).
    public static readonly float[] PerBoss = { BouncyFraction, BouncyFraction, BouncyFraction, BouncyFraction, BouncyFraction, BouncyFraction, BouncyFraction };

    public static float FractionFor(int world) => PerBoss[Mathf.Clamp(world, 0, PerBoss.Length - 1)];

    // Does a shot at p flying v (radius r) meet a rail (centre at +/- (edge - r)) before it drops under `floorY`?
    public static bool MeetsRail(Vector2 p, Vector2 v, float r, float edge, float floorY)
    {
        if (v.y >= -1e-3f || Mathf.Abs(v.x) < 1e-3f) return false;
        float e = edge - r;
        float t = (Mathf.Sign(v.x) * e - p.x) / v.x;
        if (t < 0f) t = 0f;
        return p.y + v.y * t > floorY;
    }

    // Follows a bouncy shot (straight flight, MaxBounces ricochets) and returns the window [t0, t1] (seconds after the
    // shot is fired) and the x range [x0, x1] of its centre while it is inside the band [bandLo, bandHi] AFTER its first
    // ricochet. False: it never comes back into the band (the whole ricochet flies above the ship or past the floor).
    public static bool Trace(Vector2 p, Vector2 v, float r, float edge, float bandLo, float bandHi,
                             out float t0, out float t1, out float x0, out float x1)
    {
        t0 = t1 = x0 = x1 = 0f;
        if (v.y >= -1e-3f || Mathf.Abs(v.x) < 1e-3f) return false;
        float e = edge - r;
        float t = 0f;
        bool any = false;
        for (int seg = 0; seg <= MaxBounces; seg++)
        {
            // time to the next rail contact from p
            float tc = (Mathf.Sign(v.x) * e - p.x) / v.x;
            if (tc < 0f) tc = 0f;
            if (seg > 0)
            {
                // this segment [t, t + tc] flies after a ricochet: clip it to the band
                float ya = p.y, yb = p.y + v.y * tc;      // ya > yb (falling)
                if (!(yb > bandHi || ya < bandLo))
                {
                    float sa = ya > bandHi ? (bandHi - p.y) / v.y : 0f;
                    float sb = yb < bandLo ? (bandLo - p.y) / v.y : tc;
                    float xa = p.x + v.x * sa, xb = p.x + v.x * sb;
                    if (!any) { t0 = t + sa; x0 = Mathf.Min(xa, xb); x1 = Mathf.Max(xa, xb); any = true; }
                    else { x0 = Mathf.Min(x0, Mathf.Min(xa, xb)); x1 = Mathf.Max(x1, Mathf.Max(xa, xb)); }
                    t1 = t + sb;
                }
                if (yb < bandLo) break;                   // under the band: it never comes back up
            }
            // at the contact: ricochet (or, after the last one, splash)
            p += v * tc;
            t += tc;
            if (seg == MaxBounces) break;
            p.x = Mathf.Sign(v.x) * e;
            v = new Vector2(-v.x, v.y) * SpeedKeep;
        }
        return any;
    }

    // The padding round a shadow: the ship's and the shot's radius, a margin, and the furthest the boss-fight weather can
    // drift the shot in `seconds` (WeatherWind.BossShotWindCap).
    public static float ShadowPad(float shotR, float seconds) =>
        PatternSim.ShipRadius + shotR + ShadowMargin + WeatherWind.BossShotWindCap * Mathf.Max(0f, seconds);
}

// The low-discrepancy pick: every eligible bullet adds `fraction`; one is picked at a whole credit.
public struct BounceSelector
{
    float credit;
    public int eligible, picked, vetoed;

    public void Reset(float phase = 0f) { credit = Mathf.Repeat(phase, 1f); eligible = picked = vetoed = 0; }

    // An eligible bullet: true when it should be bouncy (pending certification; call Take or Veto).
    public bool Offer(float fraction)
    {
        eligible++;
        credit = Mathf.Min(credit + Mathf.Clamp01(fraction), BossBounce.MaxCredit);
        return credit >= 1f;
    }

    public void Take() { credit -= 1f; picked++; }
    public void Veto() { vetoed++; }
}
