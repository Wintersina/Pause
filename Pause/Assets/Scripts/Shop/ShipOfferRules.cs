using System;
using System.Collections.Generic;

// The decision logic of the "ship offer": the death-screen offer of a NEW
// ship, made when the ship the player just flew has nothing left to upgrade.
// ShipOfferStore wires it to the save, ShipOfferCard draws it. Plain C#, no
// Unity types, so ShipOfferTest can drive every branch.
//
// It is the same flow as the upgrade offer (UpgradeOfferRules): the same
// moment (after a death, before the Flight Complete panel), the same Options
// toggle and the same backoff state. The two never compete: the upgrade offer
// is for a ship that still has a colour to buy, the ship offer only for one
// that has none.
//
// WHEN: the upgrade offer's backoff says an offer is due (2nd death in a row,
// then 4, then 8 after declines, none after MaxDeclines until something is
// bought), the toggle is on, the flown ship is fully upgraded (every colour
// owned), the offered ship is affordable, and that ship has not already been
// offered this app session.
//
// WHICH: the next ship on the roster after the flown one, in the dock's
// order (SpaceDock.BayOrder: cheapest first), skipping ships already owned,
// real-money ships (Paragon) and anything without a dust price. Past the
// last berth it wraps to the first, so every unowned dust ship is reachable.
// Only the first such ship is considered: if it is too dear there is no
// offer (never a cheaper ship further round), so the offer always matches
// the next step the player sees in the dock.
public static class ShipOfferRules
{
    // ---- tuning (every number of the ship offer lives here) -------------
    // Backoff: shared with the upgrade offer (UpgradeOfferRules.StreakForFirstOffer,
    // GrowthFactor, MaxDeclines); a NOT NOW counts as a decline there.
    // The same ship is shown at most this many times per app session.
    public const int MaxShowsPerShipPerSession = 1;
    // Card pop-in (seconds, unscaled).
    public const float PopInSeconds = .2f;
    // BUY: the ink silhouette fades into the hull's colours over RevealSeconds
    // with a soft white lift that peaks at RevealFlashPeak (0..1) mid-way (the
    // dock's reveal goes to full white; this one stays restrained), then the
    // card holds on the revealed ship for RevealHoldSeconds before closing.
    public const float RevealSeconds = .55f;
    public const float RevealFlashPeak = .3f;
    public const float RevealHoldSeconds = .8f;

    public struct Offer
    {
        public int shipId;       // the ship offered
        public int flownId;      // the fully upgraded ship it follows
        public float price;      // stardust, > 0
    }

    // Every colour past the stock one (indices 1..skinCount-1) is owned.
    public static bool FullyUpgraded(int skinCount, Func<int, bool> colourOwned)
    {
        if (colourOwned == null) return false;
        for (int skin = 1; skin < skinCount; skin++)
            if (!colourOwned(skin)) return false;
        return true;
    }

    // The first offerable ship after `flown` in `order`, wrapping round; the
    // flown ship itself is never returned. 0 when there is none. A flown ship
    // that is not in `order` starts the scan at the first berth.
    public static int NextShip(int flown, IList<int> order, Func<int, bool> offerable)
    {
        if (order == null || offerable == null || order.Count == 0) return 0;
        int at = order.IndexOf(flown);
        int n = order.Count;
        for (int step = 1; step <= n; step++)
        {
            int id = order[(at + step + n) % n];
            if (id == flown) continue;
            if (offerable(id)) return id;
        }
        return 0;
    }

    // The whole decision.
    public static bool ShouldOffer(bool settingOn, UpgradeOfferRules.State s, bool flownFullyUpgraded,
                                   int nextShip, float price, float balance, int shownThisSession)
    {
        if (!settingOn || !UpgradeOfferRules.Due(s)) return false;
        if (!flownFullyUpgraded || nextShip <= 0) return false;
        if (price <= 0f || price > balance) return false;
        return shownThisSession < MaxShowsPerShipPerSession;
    }

    // Reveal curve, t = 0..1 of RevealSeconds: how much ink is left (1 = the
    // silhouette) and how much white lies over it.
    public static float RevealInk(float t)
    {
        t = t < 0f ? 0f : t > 1f ? 1f : t;
        float e = t * t * (3f - 2f * t);
        return 1f - e;
    }

    public static float RevealFlash(float t)
    {
        t = t < 0f ? 0f : t > 1f ? 1f : t;
        return RevealFlashPeak * (float)Math.Sin(Math.PI * t);
    }
}
