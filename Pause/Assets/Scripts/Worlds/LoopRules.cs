using UnityEngine;

// Every tunable number for what happens after the final world, in one table.
//
// When the Ember boss encounter ends the pilot picks (FinalChoicePanel):
//
//   KEEP FLYING  stay in Ember for good. No portal, no more bosses: spawns and
//                speed keep escalating (the Endless* numbers) until game over.
//   LOOP BACK    a portal opens to the world the run started in. The score
//                carries on, RunLoop.Index goes up by one and the run plays
//                every world and boss again -- each loop a little harder (the
//                per-loop numbers), and its boss / world bonuses worth a
//                little more.
//   (no pick)    when the countdown runs out: ONE MORE EMBER, THEN LOOP.
//                The pilot stays in Ember and flies it once more as a loop
//                pass -- a full level (same length, enemies, ramp and
//                escalation, scaled as loop Index + 1), then the Ember boss
//                again. When that boss ends the run goes straight into LOOP
//                BACK (portal to the start world, score kept, loop + 1),
//                without asking a second time. Score bonuses on the encore
//                stay at the current loop's (it is the same loop until the
//                portal); only its difficulty is the next loop's.
//
// Per-loop scaling uses min(loop, MaxScaledLoops), so a fifth loop is no
// harder than the third. Defaults (loop 0 is the first pass, always x1):
//
//                          loop 1   loop 2   loop 3+
//   arrival speed (HUD)       4        8       12      speed after a portal (was 0)
//   speed ramp              x1.10    x1.20    x1.30
//   max speed (HUD)          +2       +4       +4      capped by MaxSpeedBonusCap
//   enemy phase ramp        x1.15    x1.30    x1.45    enmiesOnBoard.phaseRampScale
//   spawn density           x1.10    x1.20    x1.30    LoopDifficulty.DensityScale (enmiesOnBoard.Roll)
//   boss cooldowns          x0.90    x0.80    x0.70
//   boss patterns           one more pattern from the start (head start 1/3)
//   boss / world bonus      x1.5     x2.0     x2.5     (x3.0 at loop 4+, BonusLoopCap)
//
// KEEP FLYING (Ember, endless), on the world's clock while flying:
//   max speed   +1 HUD every 25s past Ember's cap, at most +8 (200s)
//   density     +20% a minute, at most x1.6 (on top of the loop's own)
//   and never past AbsoluteMaxSpeed (HUD 72) whatever the loop: the old 0.78
//   cap was dropped because the board stopped being readable up there.
public static class LoopRules
{
    // ---- the choice ----
    // Seconds (real time) before the panel picks ONE MORE EMBER, THEN LOOP
    // by itself; 0 = never.
    public static float AutoPickSeconds = 10f;
    // A missed loop portal comes back after this much flight.
    public static float LoopPortalRetrySeconds = 6f;

    // ---- per loop ----
    public static int MaxScaledLoops = 3;
    public static float ArrivalSpeedPerLoop = .04f;
    public static float RampPerLoop = .10f;
    public static float MaxSpeedPerLoop = .02f;
    public static float MaxSpeedBonusCap = .04f;
    public static float PhaseRampPerLoop = .15f;
    public static float DensityPerLoop = .10f;
    public static float BossCooldownPerLoop = .10f;
    public static float BossCooldownFloor = .70f;
    // Fight progress added for the pattern unlocks (BossCatalog.UnlockedAttacks
    // works in thirds): 1/3 = the second pattern from the first attack.
    public static float BossPatternHeadStart = 1f / 3f;

    // ---- bonuses ----
    public static float BonusPerLoop = .5f;
    public static int BonusLoopCap = 4;

    // ---- KEEP FLYING ----
    public static float EndlessSpeedPerSecond = .0004f;
    public static float EndlessSpeedCap = .08f;
    public static float EndlessDensityPerSecond = .2f / 60f;
    public static float EndlessDensityCap = 1.6f;
    // Seconds of flight between re-applying the endless numbers.
    public static float EndlessStepSeconds = 1f;

    // Nothing ever ramps past this (moveBackGround.speed; HUD 72).
    public static float AbsoluteMaxSpeed = .72f;

    // ---- per-loop values ----

    static int Scaled(int loop) { return Mathf.Clamp(loop, 0, MaxScaledLoops); }

    public static float ArrivalSpeed(int loop) { return ArrivalSpeedPerLoop * Scaled(loop); }
    public static float RampScale(int loop) { return 1f + RampPerLoop * Scaled(loop); }
    public static float MaxSpeedBonus(int loop) { return Mathf.Min(MaxSpeedBonusCap, MaxSpeedPerLoop * Scaled(loop)); }
    public static float PhaseRampScale(int loop) { return 1f + PhaseRampPerLoop * Scaled(loop); }
    public static float DensityScale(int loop) { return 1f + DensityPerLoop * Scaled(loop); }
    public static float BossCooldownScale(int loop)
    {
        return Mathf.Max(BossCooldownFloor, 1f - BossCooldownPerLoop * Scaled(loop));
    }
    public static float BossHeadStart(int loop) { return loop > 0 ? BossPatternHeadStart : 0f; }

    public static float BonusScale(int loop)
    {
        return 1f + BonusPerLoop * Mathf.Clamp(loop, 0, BonusLoopCap);
    }

    // A world's speed cap on `loop`, `endlessSeconds` into KEEP FLYING.
    public static float MaxSpeed(float worldMax, int loop, float endlessSeconds)
    {
        float endless = Mathf.Min(EndlessSpeedCap, Mathf.Max(0f, endlessSeconds) * EndlessSpeedPerSecond);
        return Mathf.Min(AbsoluteMaxSpeed, worldMax + MaxSpeedBonus(loop) + endless);
    }

    public static float Density(int loop, float endlessSeconds)
    {
        float endless = Mathf.Min(EndlessDensityCap, 1f + Mathf.Max(0f, endlessSeconds) * EndlessDensityPerSecond);
        return DensityScale(loop) * endless;
    }
}

// The live per-loop / KEEP FLYING spawn-density factor, read by the enemy
// spawner (enmiesOnBoard.Roll divides every rolled delay by
// DensityMultiplier() * DensityScale). WorldManager sets it (LoopRules.Density)
// on every world arrival and each endless step; it is 1 on a first pass.
// SpawnLane still guards every row, so denser never means a closed lane.
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
    // 0 on the first pass; +1 each time LOOP BACK's portal is flown.
    public static int Index { get; private set; }
    // The world this run started in (the LOOP BACK destination).
    public static int StartWorld { get; set; }
    // The final-choice countdown ran out: the final world is being flown
    // once more as a loop pass before the automatic LOOP BACK.
    public static bool EncorePass { get; set; }
    // The loop the world's difficulty is scaled for: the encore plays at the
    // next loop's, though Index (and the score bonuses) only move on with
    // LOOP BACK's portal.
    public static int DifficultyIndex { get { return Index + (EncorePass ? 1 : 0); } }

    // "LOOP 2" for Index 1: the pass the pilot is on, counted from one.
    public static int DisplayNumber { get { return Index + 1; } }

    public static void Reset()
    {
        Index = 0;
        StartWorld = 0;
        EncorePass = false;
        LoopDifficulty.Reset();
    }

    public static int Advance()
    {
        EncorePass = false;
        Index++;
        return Index;
    }
}
