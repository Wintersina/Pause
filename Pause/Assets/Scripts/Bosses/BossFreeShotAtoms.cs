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
    public const int Count = 3;
    public const float StartMargin = 3f;     // none in the fight's first seconds
    public const float EndMargin = 5f;       // ... nor in the last (>= AtomSpacing.Gap; time to catch it too)
    public const float MinSpacing = 6f;      // between two of these (>= AtomSpacing.Gap)

    // Fills `times` (ascending, fight-clock seconds) and returns how many fit.
    public static int Roll(float fightSeconds, float[] times)
    {
        float window = fightSeconds - StartMargin - EndMargin;
        if (window < 0f) return 0;
        int n = Mathf.Min(Mathf.Min(Count, times.Length), 1 + Mathf.FloorToInt(window / MinSpacing + 1e-4f));
        // n uniform points in the window shrunk by the spacing, sorted, then
        // pushed apart by i * MinSpacing: uniform and never closer than that.
        float free = window - (n - 1) * MinSpacing;
        for (int i = 0; i < n; i++) times[i] = Random.value * free;
        System.Array.Sort(times, 0, n);
        for (int i = 0; i < n; i++) times[i] += StartMargin + i * MinSpacing;
        return n;
    }
}
