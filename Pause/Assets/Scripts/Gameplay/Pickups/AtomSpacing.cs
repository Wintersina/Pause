// Keeps the real game's atoms apart: no two pickup atoms -- blue shield, red
// pause, violet capacitor (spawnGoodStuff) or green heal (HealAtomSpawner) --
// are released within Gap seconds of each other.
//
// Why: the two spawners run independent timers, so nothing stopped them
// coming due together. Since worlds became a ~120 s distance
// (WorldManager.BaselineWorldSeconds) they did so by design: the blue atom's
// end-of-level guarantee (60 s left) lands at ~60 s of flight, the second the
// green atom's first chance comes up (firstChanceAfter 60 s), with the
// capacitor's first one (35-65 s) close by -- two or three atoms at once on
// most worlds (AtomSpacingTest).
//
// An atom that comes due inside the gap simply waits (its timer stays run
// out) and goes as soon as the gap has passed; budgets and delays are
// otherwise untouched. The clock is spawnGoodStuff's: it ticks on its
// running spawn frames only, so a pause, a planetfall's descent or a
// lift-off (when nothing new arrives) doesn't count toward the gap.
// The boss fight's three free-shot atoms (BossFreeShotAtoms) are spaced too.
// Star dust is not an atom and is not spaced.
public static class AtomSpacing
{
    public const float Gap = 4f;

    static float since = Gap;

    // Has the gap since the last atom passed?
    public static bool Ready => since >= Gap;
    public static float SinceLast => since;

    // spawnGoodStuff, each running spawn frame.
    public static void Tick(float dt) { if (dt > 0f) since += dt; }

    // An atom just went out.
    public static void Released() { since = 0f; }

    // A new run (spawnGoodStuff.Start): the first atom needn't wait.
    public static void Reset() { since = Gap; }
}
