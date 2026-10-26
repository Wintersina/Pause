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
//   (boss fights included) -- and on top of that the ATOMIC FUSIONS spawn
//   multipliers (FusionTuning.*SpawnMultiplier, x1.5 for the shield, pause,
//   capacitor and magnet atoms: more atoms, so combos come up). The green
//   heal atom is untouched (HealAtomSpawner reads none of this).
//
//   Magnetic atoms (magnetAtom): see the magnetic atom block below.
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

    // No star clusters are released while a portal is kept waiting past its
    // grace (PortalPressure.EarningsClosed): waiting must not pay.
    public static float StarDustRate()
    {
        if (PortalPressure.EarningsClosed) return 0f;
        return StarDustRate(BossEncounter.Running);
    }

    // The blue atom's timer runs down this much faster (x2, then the fusion x1.5: x3).
    public static float ShieldAtomRate => ShieldAtomRateMultiplier * FusionTuning.ShieldSpawnMultiplier;

    // A planet's blue-atom allowance from its base roll (3-5 -> 6-10 -> 9-15).
    public static int ShieldAtomBudget(int baseRoll)
    {
        return AtomSpawnBias.Budget(AtomKind.Shield, Mathf.Max(0, baseRoll) * ShieldAtomRateMultiplier);
    }

    // ---- red pause atom ----
    // Its inspector allowance (spawnGoodStuff.redAtomsPerWorld, 5) and
    // delays (15-40 s first, then 50-90 s) are the old ones; the fusion
    // multiplier grows the allowance (5 -> 8) and speeds the clock (x1.5).
    public static float PauseAtomRate => FusionTuning.PauseSpawnMultiplier;
    public static int PauseAtomBudget(int baseBudget) { return AtomSpawnBias.Budget(AtomKind.Pause, baseBudget); }

    // ---- violet capacitor atom ----
    // Base 2 a world, first 35-65 s, then 75-115 s; with the fusion
    // multiplier (x1.5): 3 a world, delays / 1.5.
    public const int CooldownAtomsPerWorld = (int)(2 * FusionTuning.CapacitorSpawnMultiplier + .5f);
    public const float CooldownAtomFirstDelayMin = 35f / FusionTuning.CapacitorSpawnMultiplier, CooldownAtomFirstDelayMax = 65f / FusionTuning.CapacitorSpawnMultiplier;
    public const float CooldownAtomRepeatDelayMin = 75f / FusionTuning.CapacitorSpawnMultiplier, CooldownAtomRepeatDelayMax = 115f / FusionTuning.CapacitorSpawnMultiplier;
    // Its timer's rate: the plain clock, boss fight or not.
    public const float CooldownAtomRate = 1f;

    // ---- magnetic atom ----
    // Scarce, like the violet capacitor: a fixed MagnetAtomsPerWorld per planet,
    // the first after MagnetAtomFirstDelay seconds, then one every
    // MagnetAtomRepeatDelay. Plain clock, and none are released during a boss
    // fight (its pull would find a boss that is never dragged, and the
    // shield bonus alone is already the blue atom's job).
    // Base 2 a world, first 45-80 s, then 90-130 s; with the fusion
    // multiplier (x1.5): 3 a world, delays / 1.5.
    public const int MagnetAtomsPerWorld = (int)(2 * FusionTuning.MagnetSpawnMultiplier + .5f);
    public const float MagnetAtomFirstDelayMin = 45f / FusionTuning.MagnetSpawnMultiplier, MagnetAtomFirstDelayMax = 80f / FusionTuning.MagnetSpawnMultiplier;
    public const float MagnetAtomRepeatDelayMin = 90f / FusionTuning.MagnetSpawnMultiplier, MagnetAtomRepeatDelayMax = 130f / FusionTuning.MagnetSpawnMultiplier;
    public const float MagnetAtomRate = 1f;

    public static float MagnetAtomFirstDelay()
    {
        return Random.Range(MagnetAtomFirstDelayMin, MagnetAtomFirstDelayMax);
    }

    public static float MagnetAtomRepeatDelay()
    {
        return Random.Range(MagnetAtomRepeatDelayMin, MagnetAtomRepeatDelayMax);
    }

    public static float CooldownAtomFirstDelay()
    {
        return Random.Range(CooldownAtomFirstDelayMin, CooldownAtomFirstDelayMax);
    }

    public static float CooldownAtomRepeatDelay()
    {
        return Random.Range(CooldownAtomRepeatDelayMin, CooldownAtomRepeatDelayMax);
    }
}
