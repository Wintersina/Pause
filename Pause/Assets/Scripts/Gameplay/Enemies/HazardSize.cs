using UnityEngine;

// SIZE VARIETY for the hazard rocks (docs/enemy-behaviours.md, "Hazard
// sizes"): every rock the spawner fields gets its own size, so a field of
// rocks reads as small, medium and large bodies floating around instead of
// clones.
//
// DATA. Each rock's behaviour record carries three tier sizes
// (EnemyBehaviour.Sizes: small / typical / large, as multiples of the
// roster's nominal size). A spawn draws ONE number from the spawner's random
// stream (UnityEngine.Random, so a seed gives the same sizes every time):
// it picks the tier by SmallShare / LargeShare (the rest are typical) and,
// inside the tier, a size within +/-Jitter of its centre. The tiers are
// chosen per rock so a rock's mean AREA is today's (sum of share x size^2
// within 3% of 1, HazardSizeTest): the board holds as much rock as it was
// tuned with, in more varied pieces.
//
// REAL, NOT COSMETIC. The size is a uniform transform scale on top of the
// roster's nominal size (FrameWorldSize and the roster sizes are untouched),
// so the sprite, the trigger collider and the split fragments follow it, and
// it is recorded on EnemyIdentity.Scale for everything that reasons about a
// body without looking at its transform: the footprint and placement
// envelope (SpawnSpace.BodyHalf, SpawnLane.HalfExtents / Fits / MaxX), the
// brain's lane clamp, the hit radius every system reads (ClearTarget.Radius:
// elite evasion, mine blasts, crashes, the domino, weapons), the shockwave's
// shove, the explosion size and the kill value.
//
// WEIGHT. Motion is derived from the size, not tabled: a big rock moves
// through a smaller band (Reach: drift / sway / bob / orbit amplitude and the
// upright tilt) at a slower pace (Pace: drift and glide speeds, sway, bob and
// orbit cycles, the tumble), a small one the other way, both clamped. A
// shove moves a small rock further than a large one (ShoveScale).
//
// Rail mines and pilots (fighters, heavies, aliens, chasers) are never
// scaled: Varies() is rocks only, and EnemyFactory ignores a scale for
// anything else.
public static class HazardSize
{
    // ---- tunables ----
    // Off: every rock is the nominal size (the draw still consumes its random
    // number, so a seeded board is otherwise identical: before / after probes).
    public static bool Enabled = true;
    // Share of spawns drawn from the small and the large tier; the rest are
    // typical.
    public static float SmallShare = .28f, LargeShare = .17f;
    // Spread inside a tier, as a share of its centre (+/-).
    public static float Jitter = .05f;
    // Weight: motion amplitude and pace go as size^-exponent, clamped.
    public static float ReachExponent = .5f, PaceExponent = .5f;
    public static float MinReach = .8f, MaxReach = 1.15f;
    public static float MinPace = .8f, MaxPace = 1.2f;
    // A floating rock's upright tilt never passes this (degrees).
    public static float MaxTilt = 15f;
    // A shove moves a body by size^-ShoveExponent, clamped.
    public static float ShoveExponent = 1f, MinShove = .6f, MaxShove = 1.4f;
    // Explosion size by body size: below SmallBlastBelow a small blast, above
    // LargeBlastAbove a large one, otherwise the roster's own.
    public static float SmallBlastBelow = .85f, LargeBlastAbove = 1.3f;
    // Kill value: ScoreRules.Rock x size, rounded, within -1 / +2 of it.
    public const int PointsBelow = 1, PointsAbove = 2;

    // ---- which bodies vary ----
    public static bool Varies(EnemyDef def)
    {
        if (def == null || def.role != EnemyRole.Rock) return false;
        var b = def.Behaviour;
        return b != null && b.sizeLarge > 0f;
    }

    // ---- the draw ----
    // The size of the next spawn of `def`: one Random.value for a rock (also
    // when switched off), nothing drawn for anything else.
    public static float Draw(EnemyDef def)
    {
        float u = float.NaN;
        return Draw(def, ref u);
    }

    // The same, keeping the draw: `u` NaN draws a new number and returns it
    // in `u`; a number already drawn is used again. A spawn that found no
    // room and waits (the spawner's deferred queue) keeps its draw, so a big
    // rock is not traded for a smaller one just because it had to wait.
    public static float Draw(EnemyDef def, ref float u)
    {
        if (!Varies(def)) return 1f;
        if (float.IsNaN(u)) u = Random.value;
        return Enabled ? FromUniform(def.Behaviour, u) : 1f;
    }

    // The size a uniform u in [0, 1) maps to (monotone within each tier).
    public static float FromUniform(EnemyBehaviour b, float u)
    {
        if (b == null || b.sizeLarge <= 0f) return 1f;
        u = Mathf.Clamp(u, 0f, .99999f);
        float small = Mathf.Clamp01(SmallShare), large = Mathf.Clamp01(LargeShare);
        float typical = Mathf.Max(0f, 1f - small - large);
        float centre, v;
        if (u < small) { centre = b.sizeSmall; v = u / Mathf.Max(1e-5f, small); }
        else if (u < small + typical) { centre = b.sizeTypical; v = (u - small) / Mathf.Max(1e-5f, typical); }
        else { centre = b.sizeLarge; v = (u - small - typical) / Mathf.Max(1e-5f, large); }
        return centre * (1f + Jitter * (2f * Mathf.Clamp01(v) - 1f));
    }

    public static float Min(EnemyBehaviour b) => b != null && b.sizeLarge > 0f ? b.sizeSmall * (1f - Jitter) : 1f;
    public static float Max(EnemyBehaviour b) => b != null && b.sizeLarge > 0f ? b.sizeLarge * (1f + Jitter) : 1f;

    // Expected area (size^2) of a draw, nominal = 1: what the density was
    // tuned with.
    public static float MeanArea(EnemyBehaviour b)
    {
        if (b == null || b.sizeLarge <= 0f) return 1f;
        float small = SmallShare, large = LargeShare, typical = 1f - small - large;
        float spread = 1f + Jitter * Jitter / 3f;   // E[(1 + J(2v-1))^2], v uniform
        return (small * b.sizeSmall * b.sizeSmall + typical * b.sizeTypical * b.sizeTypical +
                large * b.sizeLarge * b.sizeLarge) * spread;
    }

    // ---- what a size means ----
    public static float Reach(float size) =>
        Mathf.Clamp(Mathf.Pow(Mathf.Max(.05f, size), -ReachExponent), MinReach, MaxReach);

    public static float Pace(float size) =>
        Mathf.Clamp(Mathf.Pow(Mathf.Max(.05f, size), -PaceExponent), MinPace, MaxPace);

    public static float ShoveScale(float size) =>
        Mathf.Clamp(Mathf.Pow(Mathf.Max(.05f, size), -ShoveExponent), MinShove, MaxShove);

    public static TargetExplosion.Size Blast(EnemyDef def, float size)
    {
        if (def == null) return TargetExplosion.Size.Medium;
        if (!Varies(def) || Mathf.Approximately(size, 1f)) return def.explosionSize;
        if (size < SmallBlastBelow) return TargetExplosion.Size.Small;
        if (size > LargeBlastAbove) return TargetExplosion.Size.Large;
        return def.explosionSize;
    }

    public static int RockPoints(float size)
    {
        int nominal = ScoreRules.Rock;
        return Mathf.Clamp(Mathf.RoundToInt(nominal * size), nominal - PointsBelow, nominal + PointsAbove);
    }

    // The hit radius of a rock of this size: its collider's half-extent, as
    // ClearTarget.MeasureRadius reads an unrotated collider, times the size.
    public static float RadiusFor(EnemyDef def, float size) =>
        Mathf.Clamp(Mathf.Max(def.ColliderSize.x, def.ColliderSize.y) * .5f * size, .15f, 1.3f);

    // Sets every per-instance consequence of `size` on a freshly built (or
    // reused) roster body, from the def's nominal values -- absolute, never
    // multiplied onto what is there, so applying again resets it. Returns
    // the size it applied (1 for anything that does not vary).
    public static float Apply(GameObject go, EnemyDef def, float size)
    {
        if (go == null) return 1f;
        if (!Varies(def) || !(size > 0f)) size = 1f;
        go.transform.localScale = new Vector3(size, size, 1f);

        EnemyIdentity id;
        if (go.TryGetComponent(out id)) id.SetScale(size);

        if (Varies(def))
        {
            var b = def.Behaviour;
            float reach = Reach(size), pace = Pace(size);
            if (Mathf.Approximately(size, 1f)) reach = pace = 1f;
            AsteroidSpin spin;
            if (go.TryGetComponent(out spin))
            {
                spin.speedRange = b.spin * pace;
                if (def.floating)
                {
                    float tilt = b.tilt > 0f ? b.tilt : EnemyRoster.FloatSwayDegrees;
                    float period = b.tilt > 0f ? b.tiltPeriod : EnemyRoster.FloatSwayPeriod;
                    spin.swayDegrees = Mathf.Min(Mathf.Max(MaxTilt, tilt), tilt * reach);
                    spin.swayPeriod = period / pace;
                }
            }
            EnemyBrain brain;
            if (go.TryGetComponent(out brain)) brain.SetSize(size, reach, pace);
            ClearTarget target;
            if (go.TryGetComponent(out target)) target.SetRadius(RadiusFor(def, size));
        }

        SpawnFootprint print;
        if (go.TryGetComponent(out print)) print.half = SpawnSpace.BodyHalf(def, size);
        return size;
    }
}
