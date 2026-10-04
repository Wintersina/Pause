using UnityEngine;

// Every number the run score is made of, in one table. RunScore applies
// them; nothing else in the game hard-codes a point value.
//
// A run's score accumulates for the whole run, across every world and boss,
// from what the pilot does:
//
//   distance    DistancePerSpeedSecond x HUD speed, per second of flight on
//               the world's (scaled) clock, so a frozen world earns nothing
//               and flying faster earns more
//   kills       by role (below), times the kill-chain multiplier
//   star dust   per pickup
//   atoms       per pickup (heal / shield / pause-refill)
//   teleports   per blink, scaled by how far it moved, capped per world
//   bosses      destroyed or survived, plus a time bonus under HitPoints
//   worlds      flying through a portal: WorldClearedPerWorld x the number
//               of the world just left (Space = 1)
//
// The tutorial scores nothing (the same rule as score.PaysRealDust).
// Developer runs score as usual but never save a best or reach a leaderboard.
//
// Numbers are kept small on purpose (tens, not thousands): a typical
// 3-minute Space level plus its boss lands around 1,000-1,200.
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

    // `worldIndex` is the world just cleared (0 = Space).
    public static int WorldClearedPoints(int worldIndex)
    {
        return WorldClearedPerWorld * (worldIndex + 1);
    }

    public static int BossPoints(bool destroyed, float secondsLeft, bool hitPointsRule)
    {
        if (!destroyed) return BossSurvived;
        int bonus = hitPointsRule ? Mathf.RoundToInt(Mathf.Max(0f, secondsLeft) * BossTimeBonusPerSecond) : 0;
        return BossDestroyed + bonus;
    }

    public static float DistancePoints(float speed, float dt)
    {
        if (dt <= 0f || speed <= 0f) return 0f;
        return DistancePerSpeedSecond * speed * 100f * dt;
    }
}
