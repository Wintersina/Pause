using System.Collections.Generic;
using UnityEngine;

// Joins UpgradeOfferRules to the game: the saved backoff state and Options
// toggle, the candidates read from the ship/skin systems, and the purchase.
//
// Saved (PlayerPrefs):
// All of it is per-device pacing state and is NOT in the cloud save (a max
// merge could never let a buy's reset win):
//   offerStreak, offerDeclined   ints
//   offerLastDied                int (was the last run a death)
//   offerEnabled                 int; absent = ON, 0 = OFF
public static class UpgradeOfferStore
{
    public const string StreakKey = "offerStreak";
    public const string DeclinedKey = "offerDeclined";
    public const string LastDiedKey = "offerLastDied";
    public const string EnabledKey = "offerEnabled";

    public static bool Enabled
    {
        get { return PlayerPrefs.GetInt(EnabledKey, 1) != 0; }
        set
        {
            if (value) PlayerPrefs.DeleteKey(EnabledKey);
            else PlayerPrefs.SetInt(EnabledKey, 0);
            PrefsSaver.MarkDirty();
        }
    }

    public static UpgradeOfferRules.State Load()
    {
        return new UpgradeOfferRules.State
        {
            streak = PlayerPrefs.GetInt(StreakKey, 0),
            declined = PlayerPrefs.GetInt(DeclinedKey, 0),
            lastRunDied = PlayerPrefs.GetInt(LastDiedKey, 0) != 0,
        };
    }

    public static void Save(UpgradeOfferRules.State s)
    {
        PlayerPrefs.SetInt(StreakKey, s.streak);
        PlayerPrefs.SetInt(DeclinedKey, s.declined);
        PlayerPrefs.SetInt(LastDiedKey, s.lastRunDied ? 1 : 0);
        PrefsSaver.MarkDirty();
    }

    // A dust-paying run begins (score.Awake).
    public static void RunStarted() { Save(UpgradeOfferRules.OnRunStart(Load())); }

    // A dust-paying run ended in a death (playerIsDead).
    public static void RunDied() { Save(UpgradeOfferRules.OnDeath(Load())); }

    public static void Declined() { Save(UpgradeOfferRules.OnDeclined(Load())); }

    // Returns the candidate to offer now, or false. Reads the saved dust.
    public static bool TryGetOffer(out UpgradeOfferRules.Candidate offer)
    {
        offer = default(UpgradeOfferRules.Candidate);
        if (DeveloperUnlocks.Enabled) return false;   // developer mode owns everything
        var others = new List<UpgradeOfferRules.Candidate>();
        int equipped = ShipId.Equipped();
        var selected = NextSkin(equipped);
        foreach (int id in ShipId.All)
        {
            if (PremiumShips.IsPremium(id) || id == equipped) continue;
            if (ShipId.IsOwned(id)) others.Add(NextSkin(id));
        }
        var ship = CheapestShip();
        others.Add(ship);
        float balance = PlayerPrefs.GetFloat(StarDustLedger.CurrencyKey);
        return UpgradeOfferRules.ShouldOffer(Enabled, Load(), balance, selected, others, out offer);
    }

    // The next unbought colour of an owned dust ship; price 0 = none left.
    public static UpgradeOfferRules.Candidate NextSkin(int id)
    {
        var none = default(UpgradeOfferRules.Candidate);
        if (!ShipId.IsValid(id) || PremiumShips.IsPremium(id) || !ShipId.IsOwned(id)) return none;
        for (int skin = 1; skin < ShipSkins.CountFor(id); skin++)
        {
            if (ShipSkins.IsOwnedReal(id, skin)) continue;
            float price = ShipSkins.PriceOf(id, skin);
            if (price <= 0f) return none;
            int level = ShipWeaponUpgrades.Level(id);
            string detail = ShipSkins.Get(id, skin).DisplayName + " COLOUR";
            if (ShipWeaponUpgrades.Has(id) && level < ShipWeaponUpgrades.MaxLevel)
                detail += "   " + ShipWeaponUpgrades.Step(id, level + 1).label;
            return new UpgradeOfferRules.Candidate
            {
                kind = UpgradeOfferRules.Kind.Skin, shipId = id, skin = skin, price = price,
                title = (ShipId.NameOf(id) ?? "SHIP").ToUpperInvariant(), detail = detail,
            };
        }
        return none;
    }

    // The cheapest unowned dust ship; price 0 = all owned.
    public static UpgradeOfferRules.Candidate CheapestShip()
    {
        var best = default(UpgradeOfferRules.Candidate);
        foreach (int id in ShipId.All)
        {
            if (PremiumShips.IsPremium(id) || ShipId.IsOwned(id)) continue;
            float price = shopingShips.CostFor(id);
            if (price <= 0f || price >= float.MaxValue) continue;
            if (best.price > 0f && price >= best.price) continue;
            best = new UpgradeOfferRules.Candidate
            {
                kind = UpgradeOfferRules.Kind.Ship, shipId = id, price = price,
                title = (ShipId.NameOf(id) ?? "SHIP").ToUpperInvariant(), detail = "NEW SHIP",
            };
        }
        return best;
    }

    // Spends the dust and grants the item. A buy restarts the backoff cycle.
    public static bool Buy(UpgradeOfferRules.Candidate c)
    {
        bool ok;
        if (c.kind == UpgradeOfferRules.Kind.Skin)
            ok = ShipSkins.TryPurchase(c.shipId, c.skin) == ShipSkins.PurchaseResult.Bought;
        else
            ok = shopingShips.TryPurchase(c.shipId, c.price);
        if (!ok) return false;
        // The run's ledger and score still hold the pre-purchase balance and
        // would write it back over the spend (StarDustLedger.Spend).
        StarDustLedger.Spend(c.price);
        score.totalCurrency = Mathf.Max(0f, score.totalCurrency - c.price);
        Save(UpgradeOfferRules.OnBought(Load()));
        PrefsSaver.SaveNow();
        return true;
    }
}
