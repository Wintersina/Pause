using UnityEngine;

// The boss fight's free-shot atoms: during every boss fight exactly Count red
// (pause) atoms arrive at random moments of the fight, all before the boss
// escapes (the fight clock, BossConfig.FightSeconds, runs out), so the player
// always has a few free main-weapon shots (ShipPowerController.FreeShot) to
// spend on the boss.
//
// BossEncounter rolls the plan when the fight begins (Roll) and spawnGoodStuff
// asks each running frame whether one is due (BossEncounter.TakeDueFreeAtom).
// The times are on the fight clock (world time: a pause or frozen world holds
// them), drawn from UnityEngine.Random (seedable, like the rest of the
// spawners), uniform within [StartMargin, FightSeconds - EndMargin] and at
// least MinSpacing apart. A short window fits fewer; an atom that is due while
// another atom's AtomSpacing gap is still running waits for it, so EndMargin
// must stay >= AtomSpacing.Gap. Nothing is released once the boss is destroyed
// or has run out of clock. The normal red atoms are held back for the fight so
// the count stays exactly Count (spawnGoodStuff).
public static class BossFreeShotAtoms
{
    // ---- tuning ----
    public const int Count = 7;              // a 120 s fight (BossConfig.EscapeSeconds) hands out at least this many
    public const float StartMargin = 3f;     // none in the fight's first seconds
    public const float EndMargin = 5f;       // ... nor in the last (>= AtomSpacing.Gap; time to catch it too)
    public const float MinSpacing = 6f;      // between two of these (>= AtomSpacing.Gap)

    // Fills `times` (ascending, fight-clock seconds) and returns how many fit.
    // Stratified, not luck: the window is cut into n equal slots and one atom
    // lands at a random moment of each slot's first part (kept >= MinSpacing
    // clear of the next slot's), so the first comes early (the player can
    // start hurting the boss) and the rest follow at a steady cadence across
    // the whole fight (7 in 120 s: about one every 16 s).
    public static int Roll(float fightSeconds, float[] times)
    {
        float window = fightSeconds - StartMargin - EndMargin;
        if (window < 0f) return 0;
        int n = Mathf.Min(Mathf.Min(Count, times.Length), 1 + Mathf.FloorToInt(window / MinSpacing + 1e-4f));
        float slot = n > 1 ? Mathf.Max(MinSpacing, window / n) : window;
        float jitter = n > 1 ? Mathf.Max(0f, slot - MinSpacing) : window;
        for (int i = 0; i < n; i++) times[i] = StartMargin + i * slot + Random.value * jitter;
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
