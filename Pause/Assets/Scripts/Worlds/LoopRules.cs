using UnityEngine;

// Every tunable number for looping, in one table.
//
// Every level ends the same way: its boss, then a portal that stays open
// until the ship flies through it (WorldManager, Portal, PortalPressure).
// After the final world that portal leads back to the world the run started
// in: the score carries on, RunLoop.Index goes up by one and the run plays
// every world and boss again.
//
// Speed is capped at SpeedRamp.Cap (HUD 35) on every loop, so a loop is
// harder -- and worth more -- along other axes. Per-loop scaling uses
// min(loop, MaxScaledLoops), so a fifth loop is no harder than the fourth
// pass. Defaults (loop 0 is the first pass, always x1 / +0):
//
//                          loop 1   loop 2   loop 3+
//   arrival speed (HUD)       4        8       12      speed after a portal (never below the ship's start)
//   speed ramp              x1.10    x1.20    x1.30    35 arrives sooner
//   enemy phase ramp        x1.15    x1.30    x1.45    enmiesOnBoard.phaseRampScale
//   spawn density           x1.10    x1.20    x1.30    LoopDifficulty.DensityScale (enmiesOnBoard.Roll)
//   pilot load               +0.5     +1.0     +1.5    EnemyDensity.MaxPilotLoad
//   threat ceiling            +1       +2       +3     EnemyDensity.MaxThreats (x view scale)
//   fighter tiers             +1       +2       +2     enmiesOnBoard.ChooseExtraDef
//   roster shots alive        +2       +4       +6     EnemyThreat.ShotBudget
//   volley gap              x0.90    x0.80    x0.70    EnemyThreat.Gap
//   boss cooldowns          x0.90    x0.80    x0.70
//   boss patterns           one more pattern from the start (head start 1/3)
//   boss / world bonus      x1.5     x2.0     x2.5     (x3.0 at loop 4+, BonusLoopCap)
//   flight / kill points    x1.15    x1.30    x1.45    (x1.60 at loop 4+, BonusLoopCap)
public static class LoopRules
{
    // ---- per loop ----
    public static int MaxScaledLoops = 3;
    public static float ArrivalSpeedPerLoop = .04f;
    public static float RampPerLoop = .10f;
    public static float PhaseRampPerLoop = .15f;
    public static float DensityPerLoop = .10f;
    public static float PilotLoadPerLoop = .5f;
    public static float ThreatsPerLoop = 1f;
    public static int TierShiftPerLoop = 1, TierShiftCap = 2;
    public static int ShotsPerLoop = 2;
    public static float VolleyGapPerLoop = .10f, VolleyGapFloor = .70f;
    public static float BossCooldownPerLoop = .10f;
    public static float BossCooldownFloor = .70f;
    // Fight progress added for the pattern unlocks (BossCatalog.UnlockedAttacks
    // works in thirds): 1/3 = the second pattern from the first attack.
    public static float BossPatternHeadStart = 1f / 3f;

    // ---- bonuses ----
    public static float BonusPerLoop = .5f;
    public static int BonusLoopCap = 4;
    // Flight (distance) and kill points per loop: speed can no longer rise
    // with the loops, so the loop itself is what they are worth more for.
    public static float ScorePerLoop = .15f;

    // ---- per-loop values ----

    static int Scaled(int loop) { return Mathf.Clamp(loop, 0, MaxScaledLoops); }

    // No longer the arrival speed: every world, loops included, is entered at the
    // ship's start speed (WorldManager.ArrivalSpeed). Kept as a loop-difficulty reference.
    public static float ArrivalSpeed(int loop) { return Mathf.Min(SpeedRamp.Cap, ArrivalSpeedPerLoop * Scaled(loop)); }
    public static float RampScale(int loop) { return 1f + RampPerLoop * Scaled(loop); }
    public static float PhaseRampScale(int loop) { return 1f + PhaseRampPerLoop * Scaled(loop); }
    public static float DensityScale(int loop) { return 1f + DensityPerLoop * Scaled(loop); }
    public static float PilotLoadBonus(int loop) { return PilotLoadPerLoop * Scaled(loop); }
    public static float ThreatBonus(int loop) { return ThreatsPerLoop * Scaled(loop); }
    public static int TierShift(int loop) { return Mathf.Min(TierShiftCap, TierShiftPerLoop * Scaled(loop)); }
    public static int ShotBonus(int loop) { return ShotsPerLoop * Scaled(loop); }
    public static float VolleyGapScale(int loop)
    {
        return Mathf.Max(VolleyGapFloor, 1f - VolleyGapPerLoop * Scaled(loop));
    }
    public static float BossCooldownScale(int loop)
    {
        return Mathf.Max(BossCooldownFloor, 1f - BossCooldownPerLoop * Scaled(loop));
    }
    public static float BossHeadStart(int loop) { return loop > 0 ? BossPatternHeadStart : 0f; }

    public static float BonusScale(int loop)
    {
        return 1f + BonusPerLoop * Mathf.Clamp(loop, 0, BonusLoopCap);
    }

    public static float ScoreScale(int loop)
    {
        return 1f + ScorePerLoop * Mathf.Clamp(loop, 0, BonusLoopCap);
    }
}

// The live per-loop spawn-density factor, read by the enemy spawner
// (enmiesOnBoard.Roll divides every rolled delay by DensityMultiplier() *
// DensityScale). WorldManager sets it (LoopRules.DensityScale) on every
// world arrival; it is 1 on a first pass. SpawnLane still guards every row,
// so denser never means a closed lane.
public static class LoopDifficulty
{
    public static float DensityScale = 1f;

    public static void Reset() { DensityScale = 1f; }
}

// This run's place in the loop: which pass it is on and where it began.
// Static and in memory only -- never saved, so a developer run (or any run)
// leaves nothing behind. RunScore.BeginRun resets it with the run.
public static class RunLoop
{
    // 0 on the first pass; +1 each time the final world's portal is flown.
    public static int Index { get; private set; }
    // The world this run started in (where the final portal leads back to).
    public static int StartWorld { get; set; }

    // "LOOP 2" for Index 1: the pass the pilot is on, counted from one.
    public static int DisplayNumber { get { return Index + 1; } }

    public static void Reset()
    {
        Index = 0;
        StartWorld = 0;
        LoopDifficulty.Reset();
    }

    public static int Advance()
    {
        Index++;
        return Index;
    }
}
