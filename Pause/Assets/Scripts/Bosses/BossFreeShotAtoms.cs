using UnityEngine;

// The boss fight's free-shot atoms: during every boss fight exactly Count (2)
// red (pause) atoms arrive, one at about 30% and one at about 65% of the fight
// (jittered), both before the boss escapes (the fight clock,
// BossConfig.FightSeconds, runs out). Each hands the player a CHARGED free
// main-weapon shot (ShipPowerController.FreeShot; its hit budget is
// ChargedBudget instead of 1, see ShipAttackRunner.FireFree).
//
// Count is also the hard cap on red atoms per encounter
// (BossEncounter.TryReserveRedAtom, MaxRedAtomsPerFight): the plan and every
// other red-atom source go through that one reservation.
//
// BossEncounter rolls the plan when the fight begins (Roll) and spawnGoodStuff
// asks each running frame whether one is due (BossEncounter.TakeDueFreeAtom).
// The times are on the fight clock (world time: a pause or frozen world holds
// them), drawn from UnityEngine.Random (seedable, like the rest of the
// spawners), inside [StartMargin, FightSeconds - EndMargin] and at least
// MinSpacing apart. A short window fits fewer; an atom that is due while
// another atom's AtomSpacing gap is still running waits for it, so EndMargin
// must stay >= AtomSpacing.Gap. Nothing is released once the boss is destroyed
// or has run out of clock. The normal red atoms are held back for the fight so
// the count stays exactly Count (spawnGoodStuff).
public static class BossFreeShotAtoms
{
    // ---- tuning ----
    public const int Count = 2;              // at most BossEncounter.MaxRedAtomsPerFight
    public static readonly float[] Shares = { .30f, .65f };   // of the fight, where each lands
    public const float ShareJitter = .04f;   // +-, of the fight
    // Hit budget of one of these shots against a boss (a normal firing: 1).
    // With only 2 atoms (was 7) the fight would lose ~5 hits of damage; each
    // now carries 1.5, so the pair is worth 3 full hits (was up to 7).
    public const float ChargedBudget = 1.5f;
    public const float StartMargin = 3f;     // none in the fight's first seconds
    public const float EndMargin = 5f;       // ... nor in the last (>= AtomSpacing.Gap; time to catch it too)
    public const float MinSpacing = 6f;      // between two of these (>= AtomSpacing.Gap)

    // Fills `times` (ascending, fight-clock seconds) and returns how many fit.
    public static int Roll(float fightSeconds, float[] times)
    {
        float lo = StartMargin, hi = fightSeconds - EndMargin;
        if (hi < lo) return 0;
        int n = 0;
        for (int i = 0; i < Count && i < times.Length; i++)
        {
            float t = fightSeconds * (Shares[i] + (Random.value - .5f) * 2f * ShareJitter);
            t = Mathf.Clamp(t, lo, hi);
            if (n > 0 && t < times[n - 1] + MinSpacing) t = times[n - 1] + MinSpacing;
            if (t > hi) break;
            times[n++] = t;
        }
        return n;
    }

    // ---- the rest of the fight's help: a mix of the other atoms ----
    // Besides the free shots, the fight guarantees one of each of these, at
    // fixed shares of its length (jittered a little): capacitors (shave the
    // ultimate's charge) early and late, a shield mid-fight, a heal in the
    // second half. They come on top of the level's own blue / violet / green
    // budgets (which keep running), like the free shots do. Only for a fight
    // long enough to carry them.
    public enum Kind { Free, Shield, Capacitor, Heal }
    public static readonly Kind[] OtherKinds = { Kind.Capacitor, Kind.Shield, Kind.Heal, Kind.Capacitor };
    public static readonly float[] OtherShares = { .22f, .42f, .62f, .80f };
    public const float OthersNeedSeconds = 60f;
    public const float OtherJitterShare = .04f;
    public const int PlanMax = Count + 4;

    // The whole plan, ascending: `times` and `kinds` need PlanMax entries.
    // Returns how many.
    public static int RollPlan(float fightSeconds, float[] times, Kind[] kinds)
    {
        float[] free = new float[Count];
        int n = Roll(fightSeconds, free);
        int total = 0;
        for (int i = 0; i < n; i++) { times[total] = free[i]; kinds[total++] = Kind.Free; }
        if (fightSeconds >= OthersNeedSeconds)
            for (int i = 0; i < OtherKinds.Length; i++)
            {
                float t = fightSeconds * (OtherShares[i] + (Random.value - .5f) * 2f * OtherJitterShare);
                times[total] = t; kinds[total++] = OtherKinds[i];
            }
        // insertion sort by time
        for (int i = 1; i < total; i++)
        {
            float t = times[i]; var k = kinds[i]; int j = i - 1;
            while (j >= 0 && times[j] > t) { times[j + 1] = times[j]; kinds[j + 1] = kinds[j]; j--; }
            times[j + 1] = t; kinds[j + 1] = k;
        }
        return total;
    }
}
