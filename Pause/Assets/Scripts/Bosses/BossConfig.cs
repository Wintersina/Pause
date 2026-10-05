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
    // Resting height: derived from the view the device shows (it was the
    // constant 3.15, written for the authored 10 u view whose top is +5; on
    // a phone the view's top is +6.6 .. +9.1 and the boss rested mid-screen).
    // The top of its cell, at the top of its sway, sits BossTopMargin under
    // the HUD's top band (score read-out, home / replay icons, which already
    // drop below any display cutout), so it is as high as it can be while
    // fully visible: 3.38 on a 16:9 phone, 3.78 on an iPhone 15, 4.70 on a
    // 21:9 one. With no band to measure (the editor's authored view, a
    // landscape window) it keeps the authored share of the view
    // (AuthoredBossShare: 3.15 in the 10 u view).
    public static float BossY => RestYFor(PlayField.Live);
    // false: the old constants on every screen (rest 3.15, shots at their
    // authored speed, lobs aimed at y -3.2): for before / after comparisons.
    public static bool FitToView = true;
    public static float BossTopMargin = .2f;     // world u between the cell's top (top of its sway) and the band
    public const float AuthoredBossY = 3.15f;
    public const float AuthoredBossShare = (AuthoredBossY + 5f) / 10f;
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
    // Lobbed shots (Frost's hail) split the playfield (the ship's x clamp,
    // +/-2.4) into this many columns and rain on all but one, aimed to be
    // over their column by LobTargetY (where the ship usually flies).
    public static int LaneSlots = 5;
    public static float LaneHalfWidth = 2.4f;
    // (-3.2 in the authored 10 u view: ShipRowShare of the way up any view.)
    public static float LobTargetY => FitToView ? PlayField.Live.At(ShipRowShare) : -3.2f;
    public static float LobJitter = .15f;

    // ---- lasers (BossBeam) ----
    // A live beam's hitbox is this fraction of its drawn width.
    public static float BeamHitFraction = .7f;
    // It grows out of its part at this speed (world units / s) -- never
    // pops in full length -- and the tell's sight line at GrowSpeed x 2.
    public static float BeamGrowSpeed = 26f;
    // Width of the tell's sight line, as a fraction of the beam's.
    public static float BeamSightWidth = .3f;
    // The tell's sight line scans the arc the beam will sweep this often.
    public static float BeamScanSeconds = .4f;
    // After its hold the beam thins out over this (harmless) before it goes.
    public static float BeamFadeSeconds = .1f;
    // Size of the flash at a beam's root and of the spark where it meets a rail.
    public static float BeamFlashSize = .55f;

    // ---- rails ----
    // Size of the spark a ricochet or a splash makes on a rail.
    public static float RailSparkSize = .5f;
    public static int SparkPoolMax = 16;
    // Size of the flash at a part the instant it fires, and of its tell's charge.
    public static float MuzzleFlashSize = .6f;
    public static float ChargeMinSize = .3f, ChargeMaxSize = .75f;

    // ---- the view (every screen shape) ----
    // Attacks were tuned in the authored 10 u view: the boss at 3.15, the
    // ship flying round y -3.2 (LobTargetY), 6.35 u below it. On a phone
    // that gap is 7.6 u (16:9) to 11 u (22:9), so every attack's speed is
    // multiplied by ShotScale -- the gap on this screen over the authored
    // gap -- and a shot, a lob or a growing beam takes the same time to
    // reach the ship's rows on every screen (EnemyBrain.ViewScale does the
    // same for pilots). Straight shots scale as a whole (aim and angles are
    // kept); a lob stretches vertically (its columns stay where they are).
    public const float ShipRowShare = .18f;            // the ship's usual row: (-3.2 + 5) / 10
    public const float AuthoredShotGap = AuthoredBossY - (-3.2f);

    public static float ShotScale => ShotScaleFor(PlayField.Live);

    public static float ShotScaleFor(PlayField.Frame f)
    {
        if (!FitToView) return 1f;
        return Mathf.Max(.5f, (RestYFor(f) - f.At(ShipRowShare)) / AuthoredShotGap);
    }

    // The highest the boss's art can reach above its centre: half its cell
    // (any drawing can touch the cell's edge) plus the largest sway.
    public static float TopReach
    {
        get
        {
            if (maxSwayY < 0f)
            {
                maxSwayY = 0f;
                foreach (var b in BossCatalog.All) if (b != null) maxSwayY = Mathf.Max(maxSwayY, Mathf.Abs(b.swayY));
            }
            return BossWorldSize * .5f + maxSwayY;
        }
    }
    static float maxSwayY = -1f;

    public static float RestYFor(PlayField.Frame f)
    {
        if (!FitToView) return AuthoredBossY;
        float authored = f.At(AuthoredBossShare);
        if (!f.hasBand) return authored;
        // never lower than the middle of the view (a squat window)
        return Mathf.Max(f.bandBottom - BossTopMargin - TopReach, f.At(.5f));
    }

    // The ship stays this far under the boss's resting centre while it is
    // up (ShipReach): its lowest muzzle (about 1.2 u under its centre, the
    // Space chin cannon and the Frost jaw), the bottom of its sway, a clear
    // gap (ShipGap in the authored view, x ShotScale: the same time for a
    // shot to cross it on every screen) and the top of the ship's hull.
    // Tested per boss: no muzzle ever fires from inside this gap.
    public const float MuzzleDrop = 1.2f;
    public static float ShipGap = 1f;
    public static float ShipCeilingBelowFor(PlayField.Frame f)
    {
        float sway = TopReach - BossWorldSize * .5f;
        return MuzzleDrop + sway + ShipGap * ShotScaleFor(f) + ShipReach.HullAbove;
    }

    // Scales one shot to this view (see ShotScale). gravity > 0: a lob.
    public static void FitShot(ref Vector2 v, ref float gravity, ref float fall)
    {
        float k = ShotScale;
        if (gravity > 0f) { v.y *= k; gravity *= k; fall *= k; }
        else v *= k;
    }

    // ---- developer ----
    // Boss rush (Options > developer): seconds of flight before the boss.
    public static float DevRushAfterSeconds = 3f;
}
