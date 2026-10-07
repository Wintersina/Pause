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
//   loops       boss and world bonuses x LoopRules.BonusScale(loop), flight
//               and kill points x LoopRules.ScoreScale(loop), once the run
//               has looped back past the final world
//   waiting     nothing but boss and world bonuses is earned while a portal
//               is kept waiting past its grace (PortalPressure.EarningsClosed)
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
    // integer. At the cap (35) that is 1.75 a second before the multiplier.
    public static float DistancePerSpeedSecond = .05f;

    // ---- star dust for flying ----
    // The flight trickle: star dust per unit of distance flown
    // (moveBackGround.speed x seconds). 1/12 is exactly what the old
    // "0.05 a second at speed 0.6, in proportion below it" paid at every
    // speed the game reaches: 0.029 dust a second at the cap, about 1.9 dust
    // for a stock level.
    public static float DustPerDistance = 1f / 12f;

    public static float FlightDust(float speed, float dt)
    {
        if (dt <= 0f || speed <= 0f) return 0f;
        return DustPerDistance * speed * dt;
    }

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
    // A hostile projectile -- a roster enemy's, an elite's or a boss's shot,
    // or a landed resin pool -- absorbed by the blue-atom shield: flat, no
    // chain or speed multiplier, an "ABSORB" popup. Only the first
    // ShieldedShotsPerShield of each shield pay, so standing in a boss's fan
    // is worth at most ShieldedShot x that (60) per blue atom: a bonus for
    // flying through fire with the shield up, not something to farm. Past
    // the cap a boss shot pays its usual BossShot, the others nothing.
    // Cloak, Hard Shell and post-hit invulnerability are not a shield and
    // pay nothing. Lasers and the boss's body never pay.
    public static int ShieldedShot = 5;
    public static int ShieldedShotsPerShield = 12;
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

    // ---- death combo (DeathCrash's domino) ----
    // The wreckage of the pilot's death destroys what it hits, and what that
    // breaks into can hit more. The n-th kill of that chain earns its kill
    // points above x n (DOMINO x2, x3 ...), capped at DominoMaxMultiplier;
    // no speed multiplier (the run is over) and it never touches the in-run
    // kill chain. An elite counts DominoEliteBase (its own EliteDown reward
    // is paid as well, as for any elite going down). A MEGA DOMINO (the death
    // takes the whole screen) adds MegaDominoBonus. The chain's total is
    // added once, as RunScore's deathCombo.
    public static int DominoMaxMultiplier = 5;
    public static int DominoEliteBase = 40;
    public static int MegaDominoBonus = 100;

    public static int DominoMultiplier(int killIndex)
    {
        return Mathf.Clamp(killIndex, 1, Mathf.Max(1, DominoMaxMultiplier));
    }

    // ---- pickups ----
    public static int SmallDust = 2;
    public static int LargeDust = 5;
    public static int HealAtom = 10;
    public static int ShieldAtom = 10;
    public static int PauseAtom = 10;
    public static int CooldownAtom = 10;    // violet capacitor: an atom like the rest

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
    //   below 20  x1.0    20+ x1.25    30+ x1.5    35 (the cap) x2.0
    //   LIMIT BREAK (above the cap, on the boost shield)  x2.5
    //
    // (2026-10, second pass: speed is capped at 35 in every world and on
    // every loop -- SpeedRamp.Cap. The tiers were 20 / 30 / 40 / 46, with x2
    // near a later world's cap and x2.5 only on a loop. Now x2 is the final
    // speed itself, held for as long as the pilot can hold it, and x2.5 is
    // the few seconds of a limit break. A stock ship reaches 35 only late in
    // the later worlds or on a loop; a fast-start ship holds it for most of
    // every level.)
    //
    // It multiplies flight (distance) and kills -- the points that come from
    // how the pilot flies. Boss and world bonuses are fixed rewards for
    // getting there (they scale with the loop instead, LoopRules.BonusScale),
    // and pickups stay flat. On kills it stacks with the chain multiplier
    // (x4 chain at x2.5 speed = x10), capped at MaxTotalMultiplier. Flight
    // and kill points are then worth LoopRules.ScoreScale(loop) more on each
    // loop (applied after that cap: it is the loop's reward, not a
    // multiplier to chain).
    public static bool SpeedMultiplierEnabled = true;
    public static readonly int[] SpeedTierHud = { 20, 30, 35 };
    public static readonly float[] SpeedTierMultiplier = { 1.25f, 1.5f, 2f };
    // Any HUD speed above the cap's: only a boost gets there.
    public static float LimitBreakMultiplier = 2.5f;
    public static float MaxTotalMultiplier = 8f;

    public static int HudSpeed(float speed) { return Mathf.RoundToInt(speed * 100f); }

    public static bool IsLimitBreak(float speed) { return HudSpeed(speed) > SpeedRamp.CapHud; }

    public static float SpeedMultiplierFor(float speed)
    {
        if (!SpeedMultiplierEnabled) return 1f;
        int hud = HudSpeed(speed);
        if (hud > SpeedRamp.CapHud) return LimitBreakMultiplier;
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
    // ~1.5-3 dust (the flight trickle, stars 0.5/1, kills 0.12) against
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
