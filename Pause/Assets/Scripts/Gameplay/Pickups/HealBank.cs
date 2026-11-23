using UnityEngine;

// The banked green atom: a green atom caught at FULL hearts is not wasted, the healing drone banks it
// (up to Max). When the ship next loses a heart, the drone sprays and uses one: the heart comes back
// RestoreDelay seconds after the hit. A hit that would have been the last heart is absorbed outright
// (the heart is never lost; the atom is spent). Below full hearts a green atom heals at once as ever,
// and the tutorial never banks (its heal step is unchanged). A run's state: reset at every run start
// (collisionDetection.Start) and whenever gameplay is left (GameStateReset).
//
// Pure state, no GameObjects: collisionDetection decides with OnCatch / OnHit, the ship's ShipDamageFx
// ticks Tick and restores the heart, HealBankIcon draws Count at the ship's tail.
public static class HealBank
{
    public const int Max = 3;
    public const float RestoreDelay = .6f;
    // A spray requested by a spent atom is dropped if the drone could not serve it this soon.
    public const float SprayRequestSeconds = 4f;

    public enum Catch { Heal, Bank, Wasted }
    public enum Hit { None, Restore, Absorb }

    public static int Count { get; private set; }
    public static int Pending { get; private set; }          // hearts waiting to come back
    public static int Banked { get; private set; }           // atoms banked this run (tests)
    public static int Spent { get; private set; }            // atoms spent this run (tests)
    public static int SprayRequests { get; private set; }    // bumped on every spend: the drone sprays now
    static float restoreIn, sprayLeft;

    public static bool NeedSpray { get { return sprayLeft > 0f; } }
    public static float RestoreIn { get { return Pending > 0 ? restoreIn : 0f; } }

    public static void Reset()
    {
        Count = 0; Pending = 0; Banked = 0; Spent = 0; SprayRequests = 0;
        restoreIn = 0f; sprayLeft = 0f;
    }

    // A green atom was caught. `damaged`: below full hearts. Banks it when at full hearts and there is room.
    public static Catch OnCatch(bool damaged, bool tutorial)
    {
        if (damaged) return Catch.Heal;
        if (tutorial || Count >= Max) return Catch.Wasted;
        Count++;
        Banked++;
        return Catch.Bank;
    }

    // A heart was lost (`fatal`: it was the last one). With an atom banked the drone spends it: a non-fatal
    // loss is restored after RestoreDelay, a fatal one is absorbed (the caller keeps the last heart).
    public static Hit OnHit(bool fatal)
    {
        if (Count <= 0) return Hit.None;
        Count--;
        Spent++;
        SprayRequests++;
        sprayLeft = SprayRequestSeconds;
        if (fatal) return Hit.Absorb;
        if (Pending == 0) restoreIn = RestoreDelay;
        Pending++;
        return Hit.Restore;
    }

    // The drone started its spray (or gave up): the request is served.
    public static void SprayServed() { sprayLeft = 0f; }

    // Scaled gameplay seconds. True once per heart that is due back.
    public static bool Tick(float dt)
    {
        if (dt <= 0f) return false;
        if (sprayLeft > 0f) sprayLeft = Mathf.Max(0f, sprayLeft - dt);
        if (Pending <= 0) return false;
        restoreIn -= dt;
        if (restoreIn > 0f) return false;
        Pending--;
        restoreIn = Pending > 0 ? RestoreDelay : 0f;
        return true;
    }
}
