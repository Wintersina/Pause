// Nudges spawnGoodStuff toward fusion-friendly sequences, lightly and
// deterministically (the random roll is passed in, so tests can pin it):
//
//   * NO SAME KIND BACK TO BACK: an atom isn't released within
//     FusionTuning.SameKindGap seconds of the last atom if that one was the
//     same kind -- it waits its turn, like AtomSpacing's gap, so a different
//     kind can go first. (Its allowance and clock are untouched.)
//   * A COMPLEMENT: catching a shield / pause / capacitor / magnet atom
//     sometimes (FusionTuning.ComplementChance) asks for a partner it fuses
//     with to come FusionTuning.ComplementDelay seconds later. The partner is
//     pulled forward out of its own per-world allowance -- never an extra
//     atom -- and never green (heal stays exactly as rare), never during a
//     boss fight, at most FusionTuning.ComplementsPerWorld a world.
//
// Pure static state; spawnGoodStuff ticks and consumes it.
public static class AtomSpawnBias
{
    // Partners that fuse with each pickup atom (the heal atom is never asked for).
    static readonly AtomKind[] ShieldPartners = { AtomKind.Magnet, AtomKind.Capacitor, AtomKind.Pause };
    static readonly AtomKind[] PausePartners = { AtomKind.Capacitor, AtomKind.Shield };
    static readonly AtomKind[] CapacitorPartners = { AtomKind.Shield, AtomKind.Pause, AtomKind.Magnet };
    static readonly AtomKind[] MagnetPartners = { AtomKind.Shield, AtomKind.Capacitor };
    static readonly AtomKind[] NoPartners = { };

    public static AtomKind[] PartnersOf(AtomKind k)
    {
        switch (k)
        {
            case AtomKind.Shield: return ShieldPartners;
            case AtomKind.Pause: return PausePartners;
            case AtomKind.Capacitor: return CapacitorPartners;
            case AtomKind.Magnet: return MagnetPartners;
            default: return NoPartners;
        }
    }

    static bool hasLast;
    static AtomKind lastKind;
    static float sinceLast;

    static bool pending;
    static AtomKind pendingKind;
    static float pendingIn;
    static int complementsThisWorld;

    public static bool Pending => pending;
    public static AtomKind PendingKind => pendingKind;
    public static float PendingIn => pendingIn;
    public static int ComplementsThisWorld => complementsThisWorld;
    public static bool HasLast => hasLast;
    public static AtomKind LastKind => lastKind;

    // A new run.
    public static void Reset()
    {
        hasLast = false;
        sinceLast = 0f;
        pending = false;
        complementsThisWorld = 0;
    }

    // A new world: a fresh complement allowance.
    public static void NewWorld()
    {
        complementsThisWorld = 0;
        pending = false;
    }

    // The spawner's running clock.
    public static void Tick(float dt)
    {
        if (dt <= 0f) return;
        sinceLast += dt;
        if (pending) pendingIn -= dt;
    }

    // An atom of `k` was released (any spawner).
    public static void Released(AtomKind k)
    {
        hasLast = true;
        lastKind = k;
        sinceLast = 0f;
        if (pending && pendingKind == k) pending = false;   // it came anyway: the ask is met
    }

    // May an atom of `k` be released now?
    public static bool Allows(AtomKind k)
    {
        return !hasLast || lastKind != k || sinceLast >= FusionTuning.SameKindGap;
    }

    // An atom of `k` was caught; `roll` in [0,1). Asks for a partner on a
    // roll under ComplementChance (the roll also picks which partner).
    // `boss`: no complement during a boss fight. Returns whether it asked.
    public static bool OnCaught(AtomKind k, float roll, bool boss)
    {
        var partners = PartnersOf(k);
        if (boss || pending || partners.Length == 0 || complementsThisWorld >= FusionTuning.ComplementsPerWorld) return false;
        if (roll >= FusionTuning.ComplementChance) return false;
        int pick = (int)(roll / FusionTuning.ComplementChance * partners.Length);
        if (pick >= partners.Length) pick = partners.Length - 1;
        pending = true;
        pendingKind = partners[pick];
        pendingIn = FusionTuning.ComplementDelay;
        return true;
    }

    // Is the asked-for partner due now (its delay run out)?
    public static bool Due => pending && pendingIn <= 0f;

    // The spawner released the partner (true) or couldn't (its allowance is
    // spent: the ask is dropped).
    public static void Resolve(bool released)
    {
        if (!pending) return;
        pending = false;
        if (released) complementsThisWorld++;
    }

    // ---- spawn multipliers (FusionTuning; the heal atom's is 1) ----

    public static float RateOf(AtomKind k)
    {
        switch (k)
        {
            case AtomKind.Shield: return FusionTuning.ShieldSpawnMultiplier;
            case AtomKind.Pause: return FusionTuning.PauseSpawnMultiplier;
            case AtomKind.Capacitor: return FusionTuning.CapacitorSpawnMultiplier;
            case AtomKind.Magnet: return FusionTuning.MagnetSpawnMultiplier;
            case AtomKind.Heal: return FusionTuning.HealSpawnMultiplier;
            default: return 1f;
        }
    }

    // A per-world allowance grown by the atom's multiplier (rounded to nearest).
    public static int Budget(AtomKind k, int baseBudget)
    {
        if (baseBudget <= 0) return 0;
        return (int)(baseBudget * RateOf(k) + .5f);
    }
}
