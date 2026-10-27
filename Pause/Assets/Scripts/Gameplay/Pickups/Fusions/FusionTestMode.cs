using UnityEngine;

// DEV TOOL (Options > developer section > "FUSION TEST"): flip through the
// fusion combos and the spawner releases ONLY the atoms that make up the
// chosen combo, so every effect can be seen in any world, on demand.
//
//   * Not a feature flag and not progress: the choice lives in memory only
//     (never PlayerPrefs), so it is OFF after a restart, and it is cleared
//     the moment developer mode goes off (DeveloperUnlocks.SetEnabled).
//   * Never applied unless developer mode is on (Active), and a developer
//     run is already "DEV RUN - NOT SAVED": no best score, leaderboard,
//     achievement or progress. The fusions fired in a test are not recorded
//     in the codex either (AtomFusions.Fire).
//   * OFF is the extra position at the end of the cycle: spawning is then
//     exactly the normal rules (nothing here is read).
//   * Atoms come every FusionTestMode.AtomGap seconds, round-robin through
//     the combo's pickup atoms. The ship powers (Dove's Mending, Turtle's
//     Hard Shell) are not pickups: FEATHER STORM and SHELL BURST release
//     their pickup half (heal / shield), the power comes from the ship.
public static class FusionTestMode
{
    public const float AtomGap = 3f;       // seconds between two test atoms (a primed instant atom lasts 6)
    public const int Off = -1;

    static int index = Off;
    static int round;
    static float timer;

    // The positions: 0..combos-1 are the combos, then OFF. Pure.
    public static int Next(int current, int combos)
    {
        if (combos <= 0) return Off;
        return current >= combos - 1 ? Off : current + 1;
    }

    public static int Selected => index;
    public static int Positions => AtomFusionTable.Count + 1;

    // The test set is in force only in developer mode.
    public static bool Active => index != Off && DeveloperUnlocks.Enabled;

    public static void Step()
    {
        index = Next(index, AtomFusionTable.Count);
        round = 0;
        timer = 0f;
    }

    // Developer mode went off, or a fresh start: back to normal spawning.
    public static void Reset()
    {
        index = Off;
        round = 0;
        timer = 0f;
    }

    public static void SetForTest(int i) { index = i; round = 0; timer = 0f; }

    public static string Label(int i)
    {
        return "FUSION TEST\n" + (i == Off ? "OFF" : AtomFusionTable.All[i].name);
    }

    // How many of the combo's atoms the spawner can release (the pickups: Mending / Shell are ship powers).
    public static int PickupCount(FusionDef f)
    {
        int n = 0;
        for (int i = 0; i < f.atoms.Length; i++) if (AtomFusionTable.IsPickup(f.atoms[i])) n++;
        return n;
    }

    // The k-th pickup atom of a combo, in recipe order (round-robin: k wraps).
    public static AtomKind PickupAt(FusionDef f, int k)
    {
        int n = PickupCount(f);
        k = ((k % n) + n) % n;
        for (int i = 0; i < f.atoms.Length; i++)
            if (AtomFusionTable.IsPickup(f.atoms[i]) && k-- == 0) return f.atoms[i];
        return f.atoms[0];
    }

    // The spawner's frame while a combo is selected: true when it is time to
    // release `kind` (the next atom of the combo).
    public static bool Due(float dt, out AtomKind kind)
    {
        kind = AtomKind.Shield;
        if (!Active) return false;
        timer -= dt;
        if (timer > 0f) return false;
        timer = AtomGap;
        kind = PickupAt(AtomFusionTable.All[index], round++);
        return true;
    }
}
