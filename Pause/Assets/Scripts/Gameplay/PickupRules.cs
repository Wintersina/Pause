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
//
//   Violet capacitor atoms (cooldownAtom): a fixed CooldownAtomsPerWorld per
//   planet, the first after CooldownAtomFirstDelay seconds, then one every
//   CooldownAtomRepeatDelay. Like the red atom it keeps the plain clock --
//   no boss or shield multiplier: it already shortcuts the weapon charge, so
//   bunching both of a planet's capacitors into a boss fight would hand out
//   back-to-back ultimates.
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

    // ---- violet capacitor atom ----
    public const int CooldownAtomsPerWorld = 2;
    public const float CooldownAtomFirstDelayMin = 35f, CooldownAtomFirstDelayMax = 65f;
    public const float CooldownAtomRepeatDelayMin = 75f, CooldownAtomRepeatDelayMax = 115f;
    // Its timer's rate: the plain clock, boss fight or not.
    public const float CooldownAtomRate = 1f;

    public static float CooldownAtomFirstDelay()
    {
        return Random.Range(CooldownAtomFirstDelayMin, CooldownAtomFirstDelayMax);
    }

    public static float CooldownAtomRepeatDelay()
    {
        return Random.Range(CooldownAtomRepeatDelayMin, CooldownAtomRepeatDelayMax);
    }
}
