using UnityEngine;

// POWER CREEP: the board's spawn pressure follows ShotAtoms.PowerLevel (0..60): shared Rapid,
// modifier levels normalized to the dock-unlocked starting slots, and an active-drone bonus. Recruitment
// never dilutes existing power. Two multipliers read it:
//
//   SpawnScale     ordinary enemy / hazard spawn RATE (the spawn delays are divided by it, enmiesOnBoard.RollWith, next to
//                  AdaptiveDifficulty): x1 at power 0, up to x2 at power 60
//   EliteGapScale  the gap between elites (EliteDirector.NextGap is multiplied by it): x1 at power 0, down to x0.5 at power 60
//
// Both follow the same responsive curve, t^Exponent with t = power / MaxPower: responsive to the opening pickups, monotonic, capped.
// Unchanged by it: bosses, the one-elite-at-a-time rule (EliteDirector.MaxAlive), the landing-site / dock rules, the first-elite
// delay and hit damage. Toughness also reads PowerLevel when assigning new armoured hulls.
// Rails (rides) are not scaled, as for AdaptiveDifficulty.
// EVERY number lives here.
public static class PowerCreep
{
    public const int MaxPower = ShotAtoms.MaxPower;       // 60
    public const float Exponent = .75f;                   // the curve's bend (1 = a straight line)
    public const float MaxSpawnBonus = 1f;               // +100% enemies at full power
    public const float MaxEliteGapCut = .5f;             // elite gaps 50% shorter at full power

    // Tests: stand in for the run's power (negative: ask ShotAtoms).
    public static int PowerOverride = -1;

    public static int PowerLevel { get { return PowerOverride >= 0 ? PowerOverride : ShotAtoms.PowerLevel; } }

    // 0..1 along the curve.
    public static float Curve(int power)
    {
        float t = Mathf.Clamp01(power / (float)MaxPower);
        return Mathf.Pow(t, Exponent);
    }

    public static float SpawnScaleFor(int power) { return 1f + MaxSpawnBonus * Curve(power); }
    public static float EliteGapScaleFor(int power) { return 1f - MaxEliteGapCut * Curve(power); }

    // Not in the tutorial or practice (no real atoms there anyway).
    public static float SpawnScale { get { return startMenu.youAreInTutorial ? 1f : SpawnScaleFor(PowerLevel); } }
    public static float EliteGapScale { get { return startMenu.youAreInTutorial ? 1f : EliteGapScaleFor(PowerLevel); } }
}
