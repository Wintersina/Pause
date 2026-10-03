using UnityEngine;

// How an encounter ends.
//
//   Survival  (default) the player outlasts the boss's full pattern sequence
//             (FightSeconds of flying time). Each hit from the ultimate's
//             homing shots takes UltimateHitSeconds off what is left.
//   HitPoints the boss has HitPoints; each ultimate hit takes one. The fight
//             still ends when FightSeconds run out (the boss retreats), so a
//             player whose ultimate never charges is never stuck forever.
public enum BossEndRule { Survival, HitPoints }

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
//   -> Outro   OutroSeconds: explodes (if the ultimate hit it) or retreats
//   -> Done    speed lock released, the portal opens as before.
public static class BossConfig
{
    // ---- the one switch: how a fight ends ----
    public static BossEndRule EndRule = BossEndRule.Survival;

    // ---- intro (real time; the world is frozen throughout) ----
    public static float IntroSeconds = 2.2f;
    public static float SpeedDrainSeconds = 0.9f;
    public static float WarningAt = 0.05f;      // warning slab + screen flash
    public static float BossArriveAt = 0.35f;   // boss starts its warp in
    public static float BossArriveSeconds = 0.7f;
    public static float NameCardAt = 0.85f;

    // ---- fight ----
    // moveBackGround.speed for the whole fight: the HUD shows round(x*100) = 20.
    public static float FightSpeed = 0.20f;
    public static float FightSeconds = 36f;
    public static float FirstAttackDelay = 1.2f;
    // Phase 3 (the last third) shortens every pattern's cooldown by this.
    public static float FinalPhaseCooldownScale = 0.75f;

    // Survival: seconds each ultimate hit takes off the fight.
    public static float UltimateHitSeconds = 8f;
    // HitPoints: ultimate hits needed to destroy the boss.
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
