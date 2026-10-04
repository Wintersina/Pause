using UnityEngine;

// Release-rate tuning for the real game's pickups (spawnGoodStuff). The
// tutorial's spawner (spawnGoodStuffTut) never reads these.
//
//   Star dust (smStar1 / LargeStar1 clusters): every pickup fills the secret
//   power meter (SecretPowerController.OnDust) and shortens the ultimate's
//   timer, so releasing more dust while a boss is up makes powers come round
//   faster in the fight. The per-pickup value is unchanged -- there is
//   simply BossStarDustMultiplier times as much dust to catch.
//
//   Blue shield atoms (atom3a): ShieldAtomRateMultiplier times as often and
//   ShieldAtomRateMultiplier times the per-planet allowance, all level long
//   (boss fights included). Red (pause) and green (heal) atoms are untouched.
public static class PickupRules
{
    public const float BossStarDustMultiplier = 3f;
    public const int ShieldAtomRateMultiplier = 2;

    // How fast the star-cluster timers run down: x3 from the boss's intro to
    // its retreat/destruction (BossEncounter.Running), x1 otherwise.
    public static float StarDustRate(bool bossActive)
    {
        return bossActive ? BossStarDustMultiplier : 1f;
    }

    public static float StarDustRate()
    {
        return StarDustRate(BossEncounter.Running);
    }

    // The blue atom's timer runs down this much faster.
    public static float ShieldAtomRate => ShieldAtomRateMultiplier;

    // A planet's blue-atom allowance from its base roll (3-5 -> 6-10).
    public static int ShieldAtomBudget(int baseRoll)
    {
        return Mathf.Max(0, baseRoll) * ShieldAtomRateMultiplier;
    }
}
