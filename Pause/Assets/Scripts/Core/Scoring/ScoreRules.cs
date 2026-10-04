using UnityEngine;

// Every number the run score is made of, in one table. RunScore applies
// them; nothing else in the game hard-codes a point value.
//
// A run's score accumulates for the whole run, across every world and boss,
// from what the pilot does:
//
//   distance    DistancePerSpeedSecond x HUD speed, per second of flight on
//               the world's (scaled) clock, so a frozen world earns nothing
//               and flying faster earns more; times the speed multiplier
//   kills       by role (below), times the kill-chain multiplier and the
//               speed multiplier (together capped at MaxTotalMultiplier);
//               a teleport kill adds TeleportKillBonus before multiplying
//   star dust   per pickup
//   atoms       per pickup (heal / shield / pause-refill)
//   teleports   per blink, scaled by how far it moved, capped per world
//   bosses      destroyed or survived, plus a time bonus under HitPoints
//   worlds      flying through a portal: WorldClearedPerWorld x the number
//               of the world just left (Space = 1)
//   loops       boss and world bonuses x LoopRules.BonusScale(loop) once the
//               run has looped back past the final world
//
// The tutorial scores nothing (the same rule as score.PaysRealDust).
// Developer runs score as usual but never save a best or reach a leaderboard.
//
// Numbers are kept small on purpose (tens, not thousands): a typical
// 3-minute Space level plus its boss lands around 1,200-1,500 (about 1,250
// before the speed multiplier), a whole first pass Space -> Ember ~6,600 and
// a second loop ~8,100 (LoopTest.SimulatedRun logs the modelled breakdown).
public static class ScoreRules
{
    // ---- distance ----
    // Points per second per point of HUD speed (round(speed * 100)), i.e.
    // HUD speed / 20 a second, accumulated as a fraction and shown as an
    // integer. At the Space cap (46) that is ~2.3 a second; a whole 3-minute
    // Space level (mean speed ~27) earns ~250.
    public static float DistancePerSpeedSecond = .05f;

    // ---- kills (before the chain multiplier) ----
    public static int Rock = 5;             // EnemyRole.Rock (asteroids, ground chunks)
    public static int FighterPerTier = 5;   // EnemyRole.Fighter: 5 x tier (1-4)
    public static int Chaser = 20;          // EnemyRole.Chaser
    public static int Alien = 15;           // EnemyRole.Alien
    public static int Heavy = 40;           // EnemyRole.Big
    public static int Mine = 10;            // EnemyRole.Mine
    // An enemy with no roster identity (legacy prefabs): tag Astr scores as
    // a rock, "alien1"/"mine" by name, anything else as a tier-1 fighter.
    public static int UnknownEnemy = 5;
    // A boss projectile shot down or absorbed. Never chained, and the boss's
    // own body hitbox (which respawns) and lane beams score nothing, so a
    // shielded ship can't farm the boss.
    public static int BossShot = 1;
    // A kill made by blinking onto it with the pause-teleport (TeleportFx):
    // added to the enemy's own points before the chain and speed multipliers,
    // a small reward for using a pause offensively. Not on boss parts.
    public static int TeleportKillBonus = 5;
    // An elite ship brought down (EliteShip), by any means: the pilot's
    // weapons, the ultimate, a blink, a shielded ram -- or baiting it into
    // a rock, a mine, a rail or another elite (luring is the skill). Flat:
    // no chain or speed multiplier, an "ELITE DOWN" popup, plus
    // EliteDownDust star dust through the normal currency path. Kills the
    // elites' own shots make of other hazards (friendly fire) pay nothing.
    public static int EliteDown = 50;
    public static float EliteDownDust = 15f;

    // ---- pickups ----
    public static int SmallDust = 2;
    public static int LargeDust = 5;
    public static int HealAtom = 10;
    public static int ShieldAtom = 10;
    public static int PauseAtom = 10;

    // ---- teleport ----
    // A blink earns Teleport x (distance / TeleportFullDistance), capped at
    // Teleport. Shorter than TeleportMinDistance earns nothing (a re-press in
    // place is not a reposition), and only the first TeleportsScoredPerWorld
    // blinks in each world count -- out of pauses, blinks are free once a
    // second, and must not out-earn actually flying.
    public static int Teleport = 3;
    public static float TeleportMinDistance = .75f;
    public static float TeleportFullDistance = 2f;
    public static int TeleportsScoredPerWorld = 12;

    // ---- bosses ----
    public static int BossDestroyed = 300;
    public static int BossSurvived = 150;    // it retreated: still a fight won
    // HitPoints rule only: seconds left on the fight clock when it blew up.
    public static int BossTimeBonusPerSecond = 5;

    // ---- worlds ----
    public static int WorldClearedPerWorld = 50;

    // ---- kill chain ----
    // Kills inside ComboWindowSeconds of each other build a chain; the chain
    // length picks the multiplier on kill points (x1 -> x4). The window runs
    // on the world's clock, so a pause neither drops nor extends a chain.
    public static bool ComboEnabled = true;
    public static float ComboWindowSeconds = 2f;
    // Chain length needed for x2, x3, x4.
    public static readonly int[] ComboThresholds = { 3, 6, 10 };

    public static int MultiplierFor(int chain)
    {
        if (!ComboEnabled) return 1;
        int m = 1;
        for (int i = 0; i < ComboThresholds.Length; i++)
            if (chain >= ComboThresholds[i]) m = i + 2;
        return m;
    }

    // ---- speed multiplier ----
    // "The faster they go, the higher the multiplier." Tiered by HUD speed
    // (round(speed * 100)) at the moment points are earned:
    //
    //   below 20  x1.0    20+ x1.25    35+ x1.5    50+ x2.0    65+ x2.5
    //
    // It multiplies flight (distance) and kills -- the points that come from
    // how the pilot flies. Boss and world bonuses are fixed rewards for
    // getting there (they scale with the loop instead, LoopRules.BonusScale),
    // and pickups stay flat. On kills it stacks with the chain multiplier
    // (x4 chain at x2.5 speed = x10), capped at MaxTotalMultiplier. Ember's
    // cap is HUD 62, so x2.5 is only reached on a loop or while KEEP FLYING.
    public static bool SpeedMultiplierEnabled = true;
    public static readonly int[] SpeedTierHud = { 20, 35, 50, 65 };
    public static readonly float[] SpeedTierMultiplier = { 1.25f, 1.5f, 2f, 2.5f };
    public static float MaxTotalMultiplier = 8f;

    public static int HudSpeed(float speed) { return Mathf.RoundToInt(speed * 100f); }

    public static float SpeedMultiplierFor(float speed)
    {
        if (!SpeedMultiplierEnabled) return 1f;
        int hud = HudSpeed(speed);
        float m = 1f;
        for (int i = 0; i < SpeedTierHud.Length; i++)
            if (hud >= SpeedTierHud[i]) m = SpeedTierMultiplier[i];
        return m;
    }

    // Chain x speed, within the overall cap.
    public static float Combined(int chainMultiplier, float speedMultiplier)
    {
        return Mathf.Min(MaxTotalMultiplier, Mathf.Max(1, chainMultiplier) * Mathf.Max(1f, speedMultiplier));
    }

    // "x1.25", "x1.5", "x2": the HUD badge and the death panel.
    public static string MultiplierLabel(float m)
    {
        return "x" + m.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
    }

    // ---- star dust for the score ----
    // At the end of a run the score tops up the star dust it earned:
    //
    //   bonus = min(ScoreDustCap, ScoreDustPerSqrtPoint x sqrt(score)), to 0.01
    //
    // sqrt so it keeps rising but ever more slowly, and a hard cap so even a
    // huge loop run is a small top-up, never an income. A run typically earns
    // ~1.5-3 dust (0.05/s at top speed, stars 0.5/1, kills 0.12) against
    // ships at 600-5,800 and skins at 300/750:
    //
    //   score     200    400    600   1,500  3,000  5,625+
    //   bonus    0.28   0.40   0.49   0.77   1.10   1.50 (cap)
    //
    // Paid once, through the run's own ledger commit (score.Settle); none in
    // the tutorial or in developer mode.
    public static float ScoreDustPerSqrtPoint = .02f;
    public static float ScoreDustCap = 1.5f;

    public static float ScoreDustBonus(long score)
    {
        if (score <= 0) return 0f;
        float raw = Mathf.Min(ScoreDustCap, ScoreDustPerSqrtPoint * Mathf.Sqrt(score));
        return Mathf.Round(raw * 100f) / 100f;
    }

    // ---- HUD ----
    // A gain of at least this much at once punches the HUD's SCORE figure.
    // ("+N" popups show for every kill, pickup, boss and world clear.)
    public static int PopupMinPoints = 10;

    // ---- per-event points ----

    public static int KillPoints(EnemyRole role, int tier)
    {
        switch (role)
        {
            case EnemyRole.Rock: return Rock;
            case EnemyRole.Big: return Heavy;
            case EnemyRole.Fighter: return FighterPerTier * Mathf.Clamp(tier, 1, 4);
            case EnemyRole.Chaser: return Chaser;
            case EnemyRole.Alien: return Alien;
            case EnemyRole.Mine: return Mine;
        }
        return UnknownEnemy;
    }

    public static int TeleportPoints(float distance)
    {
        if (distance < TeleportMinDistance) return 0;
        float k = TeleportFullDistance <= 0f ? 1f : Mathf.Clamp01(distance / TeleportFullDistance);
        return Mathf.RoundToInt(Teleport * k);
    }

    // `worldIndex` is the world just cleared (0 = Space); `loop` the run's
    // RunLoop.Index (LoopRules.BonusScale: x1, x1.5, x2 ...).
    public static int WorldClearedPoints(int worldIndex, int loop = 0)
    {
        return Mathf.RoundToInt(WorldClearedPerWorld * (worldIndex + 1) * LoopRules.BonusScale(loop));
    }

    public static int BossPoints(bool destroyed, float secondsLeft, bool hitPointsRule, int loop = 0)
    {
        int points;
        if (!destroyed) points = BossSurvived;
        else
        {
            int bonus = hitPointsRule ? Mathf.RoundToInt(Mathf.Max(0f, secondsLeft) * BossTimeBonusPerSecond) : 0;
            points = BossDestroyed + bonus;
        }
        return Mathf.RoundToInt(points * LoopRules.BonusScale(loop));
    }

    public static float DistancePoints(float speed, float dt)
    {
        if (dt <= 0f || speed <= 0f) return 0f;
        return DistancePerSpeedSecond * speed * 100f * dt;
    }
}
