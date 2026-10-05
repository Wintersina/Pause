using UnityEngine;

// How a real-game pickup atom (blue shield, red pause, green heal) floats down
// the screen: a playful curly wander on top of a guaranteed downward drift.
//
// Why it exists: atoms rode the shared world scroller,
// moveItemEnmInStrightLine, whose transform.Translate(down) is in LOCAL space,
// while AtomSpin turns the atom 32 deg/s. Local "down" therefore swept round
// the full circle every 11.25 s and the atom flew a circle of radius
// v / 0.5585 rad/s instead of falling: y(t) = y0 - (v/w) sin(wt). For 5.6 s of
// every 11.25 s it climbed, back up through its spawn height above the screen
// to v/w (1.79 v) beyond it. It only ever dipped v/w below the spawn, so
// unless v/w covered the ~12.5 units from the spawn (5.5) to the bottom
// destroyer (-7) -- v >= 7 u/s, HUD ~23 -- it never left: it dipped, flew back
// off the top, and came round again, out of the ship's reach (the ship tops
// out at y 4.5 then; ShipReach now). Every run starts below HUD 23 (stock HUD 0 ramps ~0.3/s).
//
// Now the scroller hands atoms to this instead (Space.World; the spin stays
// purely visual):
//   - descent  vDown = max(world speed x 30, MinDescent), every frame the
//              world moves -- so it falls at least as fast as the world and
//              still falls when the world crawls (HUD 0 starts, boss drain);
//   - wander   a loop-de-loop: lateral sway +/-~0.8 and a vertical swing
//              whose up-stroke can briefly beat the descent (a little bob),
//              capped so it never adds up to a net climb; it averages to zero
//              over each loop, so the net path is always vDown;
//   - ceiling  a soft ceiling just under the top of the view and within the
//              ship's reach: inside the band below it any upward motion is
//              eased out, and an atom above it (still entering, or let go by
//              the magnet) never moves up at all;
//   - rails    the sway bounces off the lane edges instead of sliding along;
//   - exit     once it is below the visible bottom it is destroyed.
// It moves on the scaled dt it is given, so slow-mo stretches the same path
// and a pause (no step) freezes it in place.
public class AtomWander
{
    // ---- tuning (world units, seconds) ----
    public const float MinDescent = 1.2f;           // floor on the downward drift
    public const float WanderUpRatio = 1.35f;       // vertical swing vs. descent (>1: a brief bob up)
    public const float MaxVerticalSwing = 2.0f;     // cap on that swing's speed
    public const float LateralSwing = 1.1f;         // sideways sway speed (amplitude ~0.8)
    public const float MinOmega = 1.2f, MaxOmega = 1.8f;   // loop rate, rad/s (3.5 - 5.2 s loops)
    public const float CeilingBelowTop = .8f;       // ceiling sits this far under the view's top
    public const float ShipReachAbove = .35f;       // the hull still touches this far above it
    public const float SoftBand = 1.0f;             // upward motion fades out over this band
    public const float ExitBelowBottom = 1.0f;      // destroyed this far under the view
    public const float LaneHalfWidth = 2.17f;       // AtomSpin's lane (2.35 - an atom's half width)
    public const float MaxSubstep = .05f;           // big frames are split (hitches stay on the path)

    public float phase;
    public float omega;
    public float side;      // +1 / -1: which way the sway starts and bounces

    public AtomWander(float phase, float omega, float side)
    {
        this.phase = phase;
        this.omega = omega;
        this.side = side >= 0f ? 1f : -1f;
    }

    // In game: rolled from UnityEngine.Random. Tests pass a seeded System.Random.
    public static AtomWander Roll()
    {
        return new AtomWander(Random.Range(0f, 2f * Mathf.PI), Random.Range(MinOmega, MaxOmega),
                              Random.value < .5f ? -1f : 1f);
    }

    public static AtomWander Roll(System.Random rng)
    {
        return new AtomWander((float)rng.NextDouble() * 2f * Mathf.PI,
                              Mathf.Lerp(MinOmega, MaxOmega, (float)rng.NextDouble()),
                              rng.NextDouble() < .5 ? -1f : 1f);
    }

    // World-space descent for a moveBackGround.speed (u/s).
    public static float Descent(float worldSpeed)
    {
        return Mathf.Max(Mathf.Max(0f, worldSpeed) * 30f, MinDescent);
    }

    // The highest an atom may float once it is on screen: under the top edge
    // and within the ship's reach, whatever the screen's height.
    public static float Ceiling(float viewTop)
    {
        return Mathf.Min(viewTop - CeilingBelowTop, ShipTopFor(viewTop) + ShipReachAbove);
    }

    // The top of the ship's reach in a view centred on y 0 (the run's
    // camera) whose top is `viewTop` (ShipReach: a share of the view).
    public static float ShipTopFor(float viewTop)
    {
        return ShipReach.TopFor(PlayField.For(-viewTop, 2f * viewTop, Vector2.zero, default(Rect), -1f));
    }

    public static bool Gone(float y, float viewBottom)
    {
        return y < viewBottom - ExitBelowBottom;
    }

    // One step of motion. dt is scaled time.
    public Vector3 Advance(Vector3 p, float dt, float worldSpeed, float viewTop)
    {
        if (dt <= 0f) return p;
        float down = Descent(worldSpeed);
        float swing = Mathf.Min(down * WanderUpRatio, MaxVerticalSwing);
        float ceiling = Ceiling(viewTop);

        int steps = Mathf.Max(1, Mathf.CeilToInt(dt / MaxSubstep));
        float h = dt / steps;
        for (int i = 0; i < steps; i++)
        {
            phase += omega * h;
            if (phase > 2f * Mathf.PI) phase -= 2f * Mathf.PI;

            float vx = side * LateralSwing * Mathf.Sin(phase);
            float vy = -down + swing * Mathf.Cos(phase);

            // soft ceiling: upward motion fades out over the band below it,
            // and is gone at (or above) it
            if (vy > 0f)
                vy *= Mathf.Clamp01((ceiling - p.y) / SoftBand);

            p.x += vx * h;
            p.y += vy * h;

            // rail bounce: reverse the sway at the lane edges
            if ((p.x > LaneHalfWidth && vx > 0f) || (p.x < -LaneHalfWidth && vx < 0f))
                side = -side;
            p.x = Mathf.Clamp(p.x, -LaneHalfWidth, LaneHalfWidth);
        }
        return p;
    }
}
