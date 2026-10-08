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

    // ---- hearts (BossHearts) ----
    // The boss wears its health as Hearts hearts spinning round it. They
    // split the same HitPoints pool into equal segments: each heart is
    // HeartWeight full hits (3 / 5 = 0.6), and goes the moment its segment
    // is spent -- so the fight is exactly as long and as hard as before,
    // the last heart going with the last hit point. (A full-weight hit
    // takes one heart, sometimes two: 5 -> 4 -> 2 -> 0 under three of them.)
    // The attack phases follow the hearts as well as the clock: the fight's
    // progress is the further on of the two (BossEncounter.PhaseProgress01),
    // so 5-4 hearts is phase 1, 3-2 phase 2 and the last heart phase 3, at
    // the same thirds as the clock's.
    public static int Hearts = 5;
    public static float HeartWeight => HitPoints / (float)Mathf.Max(1, Hearts);
    // Each heart's world size: the player's (ShipLivesIndicator, 0.22) x 1.3.
    public static float HeartSize = .29f;
    // One revolution of the ring, in seconds of world time.
    public static float HeartRevolutionSeconds = 5f;
    // The ring's gap outside the body's drawn silhouette (BossHearts.Body),
    // between the silhouette and the heart's edge.
    public static float HeartClearance = .06f;
    // Kept this far inside the rails and under the HUD band.
    public static float HeartScreenMargin = .05f;
    // Over the boss (-3), its charges (28/29) and its shots (30-32), under
    // the HUD (screen-space canvases).
    public const int HeartSortingOrder = 40;

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

    // The ship's ceiling while a boss is up (ShipReach.BossCeilingFor):
    // FightCeilingShare of the view, but never closer to the boss than its
    // Underside (its lowest muzzle in any drawing, or its body hitbox if
    // that hangs lower, at the bottom of its sway) + a clear gap + the top
    // of the ship's hull. The gap is ShipGap in the authored view x
    // ShotScale (the same time for a shot to cross it on every screen), and
    // never less than the radius of a muzzle's charge glow, so a tell never
    // draws onto the hull. On 16:9 phones and the iPhone the boss's own
    // height binds (60% - 64% of the view, per boss); from 21:9 up it is
    // 65%. Tested per boss and shape: every muzzle in every drawing stays
    // this gap above the hull at the ceiling.
    public static float FightCeilingShare = .65f;
    public static float ShipGap = .3f;
    public static float ShipGapFor(PlayField.Frame f) => Mathf.Max(ShipGap * ShotScaleFor(f), ChargeMaxSize * .5f);

    // How far under its centre the boss reaches: the lowest muzzle in the
    // generated table (every part, every drawing) or its body hitbox's
    // bottom, whichever is lower, plus its sway. Cached per boss.
    public static float Underside(BossDef boss)
    {
        if (boss == null) return TopReach;   // unknown: its whole cell and the largest sway
        if (boss.underside >= 0f) return boss.underside;
        float low = BodyHitbox.y * .5f;
        int w = BossEmitters.World(boss);
        if (w < 0) low = BossWorldSize * .5f;   // unmeasured art: its whole cell
        else
            for (int part = 0; part < BossEmitterTable.Parts[w].Length; part++)
                for (int frame = 0; frame < BossEmitterTable.Frames; frame++)
                    low = Mathf.Max(low, -BossEmitters.Local(boss, part, frame).y);
        boss.underside = low + Mathf.Abs(boss.swayY);
        return boss.underside;
    }

    // How far under the boss's centre the ship's centre stays while it is up.
    public static float ShipBelowBossFor(PlayField.Frame f, BossDef boss)
    {
        return Underside(boss) + ShipGapFor(f) + ShipReach.HullAbove;
    }

    // The ship's ceiling with `boss` at rest on this view (world y).
    public static float ShipCeilingFor(PlayField.Frame f, BossDef boss)
    {
        if (!FitToView) return float.PositiveInfinity;
        return Mathf.Min(f.At(FightCeilingShare), RestYFor(f) - ShipBelowBossFor(f, boss));
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
