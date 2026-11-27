using UnityEngine;

// THE ONE PLACE the signature bullet patterns are tuned (BossSignatureTables holds their shapes, this holds their pace).
//
//   AttackRate   every signature stream's firing rate (its interval / this), 1.2 as the elite scripts' EliteTuning.AttackRate
//   PhaseRate    x this by the fight's phase (1..3): the same pattern comes denser as the boss's health falls
//   MaxLive      the most shots one signature pattern may have alive at once (the tests measure it); the boss's pool is
//                warmed to at least this at its spawn
//   GapMargin    the least clear room a pattern leaves beside the ship's hitbox width (BossSignatureTest)
//   CadenceBars  bars between two signature patterns (BossSpecials lands them half a cadence after its own specials)
//
// Escalation keys off ONE query, the fight's progress 0..1 (BossEncounter.PhaseProgress01: the larger of the clock and the
// hearts lost), never the hearts' own layers: Phase(progress) is 1, 2 or 3, and the table unlocks one more pattern per phase.
public static class BossSignatureTuning
{
    public static float AttackRate = 1.2f;
    public static readonly float[] PhaseRate = { 1f, 1.1f, 1.2f };
    public const int MaxLive = 96;
    public const float GapMargin = .12f;
    public static int CadenceBars = 8;

    public static int Phase(float progress01) => BossCatalog.PhaseOf(progress01);

    public static float Rate(float progress01) => RateForPhase(Phase(progress01));

    // x the loop's density (BossPatternScaling: 1 on loop 1)
    public static float RateForPhase(int phase) => AttackRate * PhaseRate[Mathf.Clamp(phase - 1, 0, PhaseRate.Length - 1)] * BossPatternScaling.Density;

    // dev / test switch: false = the bosses fight exactly as before the library
    public static bool Enabled = true;
}
