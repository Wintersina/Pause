using UnityEngine;

// Joins ShipOfferRules to the game: the flown ship's colours, the roster in
// the dock's order, ownership and prices, the shared upgrade-offer backoff
// (UpgradeOfferStore) and the purchase.
//
// Saved: nothing of its own. The backoff and the Options toggle are the
// upgrade offer's PlayerPrefs (UpgradeOfferStore); the "already shown this
// session" marks are deliberately in memory only, so a fresh launch may show
// a ship again (still subject to the saved backoff).
public static class ShipOfferStore
{
    // Bit per ship id: shown this app session (ids are < 32).
    static int shownMask;

    public static int ShownThisSession(int id)
    {
        return id > 0 && id < 32 && (shownMask & (1 << id)) != 0 ? 1 : 0;
    }

    public static void MarkShown(int id)
    {
        if (id > 0 && id < 32) shownMask |= 1 << id;
    }

    public static void ResetSession() { shownMask = 0; }

    // Purchasable with star dust right now: a visible roster ship, not owned,
    // not premium (Paragon is real money only), with a real dust price.
    public static bool Offerable(int id)
    {
        if (!ShipId.IsValid(id) || ShipId.IsOwned(id) || PremiumShips.IsPremium(id) || ShipUnlocks.IsLocked(id)) return false;
        float price = shopingShips.CostFor(id);
        return price > 0f && price < float.MaxValue;
    }

    // Every colour of the ship is bought (saved ownership, not developer mode).
    public static bool FullyUpgraded(int id)
    {
        if (!ShipId.IsValid(id) || !ShipId.IsOwned(id)) return false;
        return ShipOfferRules.FullyUpgraded(ShipSkins.CountFor(id), skin => ShipSkins.IsOwnedReal(id, skin));
    }

    // The next roster ship after `flown` (dock order) that can be offered, or 0.
    public static int NextShip(int flown)
    {
        return ShipOfferRules.NextShip(flown, SpaceDock.BayOrder, Offerable);
    }

    // The offer to make after this death, or false. Reads the saved dust.
    public static bool TryGetOffer(out ShipOfferRules.Offer offer)
    {
        offer = default(ShipOfferRules.Offer);
        if (DeveloperUnlocks.Enabled) return false;   // developer mode owns everything
        int flown = ShipId.Equipped();
        bool maxed = FullyUpgraded(flown);
        int next = maxed ? NextShip(flown) : 0;
        float price = next > 0 ? shopingShips.CostFor(next) : 0f;
        float balance = PlayerPrefs.GetFloat(StarDustLedger.CurrencyKey);
        if (!ShipOfferRules.ShouldOffer(UpgradeOfferStore.Enabled, UpgradeOfferStore.Load(), maxed,
                                        next, price, balance, ShownThisSession(next)))
            return false;
        offer = new ShipOfferRules.Offer { shipId = next, flownId = flown, price = price };
        return true;
    }

    // NOT NOW: a decline in the shared backoff (the next offer of either kind
    // needs a longer streak of deaths).
    public static void Declined() { UpgradeOfferStore.Declined(); }

    // Buys through the dock's own path (shopingShips.TryPurchase: spends, marks
    // owned, equips, reports the achievement, saves), then keeps the run's
    // ledger from writing the old balance back, and restarts the backoff.
    public static bool Buy(ShipOfferRules.Offer o)
    {
        if (!Offerable(o.shipId)) return false;
        float price = shopingShips.CostFor(o.shipId);
        if (!shopingShips.TryPurchase(o.shipId, price)) return false;
        startMenu.spawnTracker = o.shipId;
        StarDustLedger.Spend(price);
        score.totalCurrency = Mathf.Max(0f, score.totalCurrency - price);
        UpgradeOfferStore.Save(UpgradeOfferRules.OnBought(UpgradeOfferStore.Load()));
        PrefsSaver.SaveNow();
        return true;
    }
}
