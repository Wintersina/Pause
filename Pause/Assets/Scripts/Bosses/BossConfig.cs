using UnityEngine;

// How an encounter ends: whichever comes first.
//
//   DESTROYED  the boss's HitPoints run out. Every ship attack that lands
//              adds its weight (one full ultimate hit = 1; a weaker contact
//              a share of it), and each whole hit takes one point.
//   SURVIVED   the fight clock (FightSeconds of flying time) runs out first:
//              the boss retreats.
//
// The clock is fixed: hits never shave it. The two endings stay clearly
// apart -- hurting the boss is how you destroy it, outlasting it is how you
// survive it -- and a fight never ends early in a retreat because the pilot
// landed blows. If the last hit point goes in the same frame the clock runs
// out, DESTROYED wins (the hit counts).
public enum BossEndRule { HitPointsOrSurvival }

// Every tunable number for the end-of-level boss encounters, in one table.
// Per-boss attack patterns live in BossCatalog; everything shared is here.
//
// The flow (BossEncounter):
//   level clock runs out -> Pending (waits out an ultimate cinematic)
//   -> Intro   IntroSeconds of real time with the world frozen (timeScale 0,
//              never a spent pause): warning flash, the boss warps in, its
//              name card; speed drains to 0 over SpeedDrainSeconds
//   -> Fight   speed held at FightSpeed (HUD "20"); patterns escalate in
//              thirds of FightSeconds; normal pausing and teleport work
//   -> Outro   OutroSeconds: explodes (hit points gone) or retreats (timer)
//   -> Done    speed lock released, the portal opens as before.
public static class BossConfig
{
    // ---- how a fight ends (see BossEndRule): hit points or the timer ----
    public static BossEndRule EndRule = BossEndRule.HitPointsOrSurvival;

    // ---- intro (real time; the world is frozen throughout) ----
    public static float IntroSeconds = 2.2f;
    public static float SpeedDrainSeconds = 0.9f;
    public static float WarningAt = 0.05f;      // warning slab + screen flash
    public static float BossArriveAt = 0.35f;   // boss starts its warp in
    public static float BossArriveSeconds = 0.7f;
    public static float NameCardAt = 0.85f;
    // The name card shows the boss's name only. It slams in (settled after
    // 3 ticks), holds readable, then cracks (NameCrackTicks) and crumbles:
    // the letters drop one after another over NameStaggerSeconds, each
    // falling past the ship and off the bottom within NameFallSeconds. The
    // fall runs on into the first moments of the fight (purely visual).
    public static float NameBreakAt = 1.6f;
    public static int NameCrackTicks = 2;
    public static float NameStaggerSeconds = 0.2f;
    public static float NameFallSeconds = 0.7f;
    public static float NameFallAt => NameBreakAt + NameCrackTicks * BossArt.Tick;
    public static float NameGoneAt => NameFallAt + NameStaggerSeconds + NameFallSeconds;

    // ---- fight ----
    // moveBackGround.speed for the whole fight: the HUD shows round(x*100) = 20.
    public static float FightSpeed = 0.20f;
    public static float FightSeconds = 36f;
    public static float FirstAttackDelay = 1.2f;
    // Phase 3 (the last third) shortens every pattern's cooldown by this.
    public static float FinalPhaseCooldownScale = 0.75f;

    // Full-weight ship-attack hits needed to destroy the boss before the
    // fight clock runs out (weighted hits add up).
    public static int HitPoints = 3;

    // ---- outro ----
    public static float OutroSeconds = 1.6f;

    // ---- the boss ----
    public static float BossY = 3.15f;          // resting height (camera top is +5)
    public static float BossWorldSize = 3.3f;   // one atlas cell, in world units
    public static Vector2 BodyHitbox = new Vector2(2.1f, 1.1f);
    // A body hitbox the player rammed (and so destroyed) comes back after this.
    public static float BodyRespawnSeconds = 1f;

    // ---- projectiles ----
    public static int ProjectilePoolMax = 48;
    public static int BeamPoolMax = 6;
    public static float BoltWorldSize = .46f;
    public static float ShardWorldSize = .42f;
    public static float BoltHitRadius = .13f;
    public static float ShardHitRadius = .12f;
    // Lanes split the playfield (the ship's x clamp, +/-2.4) into this many
    // columns; a lane beam's hitbox is this fraction of its column.
    public static int LaneSlots = 5;
    public static float LaneHalfWidth = 2.4f;
    public static float LaneHitFraction = .7f;

    // ---- developer ----
    // Boss rush (Options > developer): seconds of flight before the boss.
    public static float DevRushAfterSeconds = 3f;
}
