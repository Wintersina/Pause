// The decision logic of the "upgrade offer" shown after a second death in a
// row (UpgradeOfferStore wires it to the save, UpgradeOfferCard draws it).
// Plain C#, no Unity types, so UpgradeOfferTest can drive every branch.
//
// WHEN: the player has died in StreakForFirstOffer runs in a row, the Options
// toggle is on, and the backoff allows another offer (below).
//
// WHAT: stardust only, never real money, and only for the ship the player is
// flying: its next unbought colour (which is also its next weapon step and
// heart), starting with the discounted first colour (ShipSkins.FirstColourPrice)
// and moving on as each is bought. If the player cannot afford it, or the
// ship has none left, nothing is shown.
// TODO: a fallback may be added later that offers other owned ships' next
// colours, or the cheapest unowned stardust ship, when the active ship's next
// upgrade is out of reach.
//
// BACKOFF (all numbers live here): offers are spaced exponentially in
// consecutive deaths. Offer 1 comes at the 2nd death in a row, and each
// decline doubles the streak needed for the next one: 4, then 8. After
// MaxDeclines declined offers it stops until the player buys an offer, which
// resets the cycle. Breaking the streak (a run that is not a death) resets
// the streak but keeps the decline count, so a player who keeps saying no
// needs ever longer streaks.
public static class UpgradeOfferRules
{
    public const int StreakForFirstOffer = 2;
    public const int GrowthFactor = 2;
    public const int MaxDeclines = 3;
    // The streak counter stops here; far past the last threshold (8).
    public const int StreakCap = 1000;

    public struct Candidate
    {
        public int shipId;
        public int skin;
        public float price;       // stardust, > 0
        public string title;      // "JADE PHANTOM"...
        public string detail;     // "CRIMSON COLOUR", ...
    }

    public struct State
    {
        public int streak;          // consecutive deaths
        public int declined;        // offers declined this cycle
        public bool lastRunDied;    // the previous counted run ended in a death
    }

    // Streak needed for the next offer: 2, 4, 8; -1 once the cycle is spent.
    public static int ThresholdFor(int declined)
    {
        if (declined < 0) declined = 0;
        if (declined >= MaxDeclines) return -1;
        int t = StreakForFirstOffer;
        for (int i = 0; i < declined; i++) t *= GrowthFactor;
        return t;
    }

    public static bool Due(State s)
    {
        int t = ThresholdFor(s.declined);
        return t > 0 && s.streak >= t;
    }

    // A run is starting: if the last one was not a death the streak is broken.
    public static State OnRunStart(State s)
    {
        if (!s.lastRunDied) s.streak = 0;
        s.lastRunDied = false;
        return s;
    }

    public static State OnDeath(State s)
    {
        if (s.streak < StreakCap) s.streak++;
        s.lastRunDied = true;
        return s;
    }

    public static State OnDeclined(State s)
    {
        if (s.declined < MaxDeclines) s.declined++;
        return s;
    }

    // Buying an offer starts a fresh cycle.
    public static State OnBought(State s)
    {
        s.streak = 0;
        s.declined = 0;
        return s;
    }

    // The upgrade to offer, or false. `selected` is the next upgrade of the
    // flown ship (price <= 0 = none left); offered only when affordable.
    public static bool Choose(float balance, Candidate selected, out Candidate chosen)
    {
        chosen = default(Candidate);
        if (!Valid(selected) || selected.price > balance) return false;
        chosen = selected;
        return true;
    }

    static bool Valid(Candidate c) { return c.price > 0f; }

    // The whole decision: setting on, streak due, the next upgrade affordable.
    public static bool ShouldOffer(bool settingOn, State s, float balance, Candidate selected, out Candidate chosen)
    {
        chosen = default(Candidate);
        if (!settingOn || !Due(s)) return false;
        return Choose(balance, selected, out chosen);
    }
}
