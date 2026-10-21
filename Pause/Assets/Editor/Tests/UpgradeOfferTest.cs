using System.Collections.Generic;
using UnityEngine;
using R = UpgradeOfferRules;

// The death-screen upgrade offer: UpgradeOfferRules' decision logic (when it is
// due, what is offered, the 2/4/8 backoff, the cap of three declines), the
// store that reads the real ship/skin state, and the setting.
public static class UpgradeOfferTest
{
    static int fails;
    static void Check(string what, bool ok)
    {
        Debug.Log((ok ? "[OFFER] PASS  " : "[OFFER] FAIL  ") + what);
        if (!ok) fails++;
    }

    public static void Run() { TestHarness.Exit(Execute()); }

    public static int Execute()
    {
        fails = 0;
        using (new TestHarness.Sandbox())
        {
            Rules();
            Backoff();
            Store();
        }
        Debug.Log("[OFFER] failures: " + fails);
        return fails;
    }

    static R.Candidate Skin(int ship, int skin, float price)
    {
        return new R.Candidate { kind = R.Kind.Skin, shipId = ship, skin = skin, price = price, title = "S" + ship };
    }

    static R.Candidate Ship(int ship, float price)
    {
        return new R.Candidate { kind = R.Kind.Ship, shipId = ship, price = price, title = "N" + ship };
    }

    static R.State Streak(int streak, int declined = 0)
    {
        return new R.State { streak = streak, declined = declined, lastRunDied = true };
    }

    static void Rules()
    {
        var none = default(R.Candidate);
        R.Candidate c;

        // eligibility: the 2nd death in a row
        Check("one death is not enough", !R.Due(Streak(1)));
        Check("the 2nd death in a row is due", R.Due(Streak(2)));

        // what is offered
        var sel = Skin(3, 1, 50f);
        var others = new List<R.Candidate> { Skin(2, 1, 300f), Ship(5, 600f) };
        Check("the selected ship's upgrade wins when affordable",
              R.ShouldOffer(true, Streak(2), 500f, sel, others, out c) && c.shipId == 3 && c.skin == 1);
        sel = Skin(3, 2, 300f);
        Check("selected too dear: the cheapest affordable other is offered",
              R.ShouldOffer(true, Streak(2), 200f, sel, new List<R.Candidate> { Ship(5, 150f), Skin(2, 1, 100f) }, out c) && c.shipId == 2);
        Check("selected too dear: a tie keeps the earlier",
              R.ShouldOffer(true, Streak(2), 200f, sel, new List<R.Candidate> { Skin(2, 1, 100f), Ship(5, 100f) }, out c) && c.shipId == 2);
        Check("nothing affordable: no offer",
              !R.ShouldOffer(true, Streak(2), 40f, sel, others, out c));
        Check("exact balance is affordable", R.ShouldOffer(true, Streak(2), 300f, sel, null, out c) && c.price == 300f);
        Check("everything owned (no candidates): no offer",
              !R.ShouldOffer(true, Streak(2), 99999f, none, new List<R.Candidate> { none, none }, out c));
        Check("no candidate list: no offer", !R.ShouldOffer(true, Streak(2), 99999f, none, null, out c));

        // the setting
        Check("setting off: never offered", !R.ShouldOffer(false, Streak(2), 99999f, Skin(3, 1, 50f), others, out c));
        Check("not due: not offered", !R.ShouldOffer(true, Streak(1), 99999f, Skin(3, 1, 50f), others, out c));
    }

    static void Backoff()
    {
        Check("thresholds are 2, 4, 8 then spent",
              R.ThresholdFor(0) == 2 && R.ThresholdFor(1) == 4 && R.ThresholdFor(2) == 8 && R.ThresholdFor(3) == -1);

        // play the sequence: die, decline whenever offered
        var s = default(R.State);
        var offeredAt = new List<int>();
        for (int death = 1; death <= 30; death++)
        {
            s = R.OnRunStart(s);
            s = R.OnDeath(s);
            if (R.Due(s)) { offeredAt.Add(death); s = R.OnDeclined(s); }
        }
        Check("offers come at deaths 2, 4, 8 and then stop",
              offeredAt.Count == 3 && offeredAt[0] == 2 && offeredAt[1] == 4 && offeredAt[2] == 8);

        // a run that is not a death breaks the streak but keeps the declines
        s = default(R.State);
        s = R.OnDeath(R.OnRunStart(s));
        s = R.OnDeath(R.OnRunStart(s));
        Check("due at 2", R.Due(s));
        s = R.OnDeclined(s);
        s = R.OnRunStart(R.OnRunStart(s));   // the run after a death keeps it, the next (no death between) breaks it
        Check("a non-death run resets the streak", s.streak == 0);
        Check("a non-death run keeps the declines", s.declined == 1);
        s = R.OnDeath(R.OnRunStart(s));
        s = R.OnDeath(R.OnRunStart(s));
        Check("after one decline 2 deaths are not enough", !R.Due(s));

        // a run that follows a death keeps the streak
        s = R.OnDeath(R.OnRunStart(default(R.State)));
        s = R.OnRunStart(s);
        Check("the run after a death keeps the streak", s.streak == 1);

        // buying resets the cycle
        s = new R.State { streak = 9, declined = 3, lastRunDied = true };
        Check("spent cycle is never due", !R.Due(s));
        s = R.OnBought(s);
        Check("a buy resets streak and declines", s.streak == 0 && s.declined == 0);
        s = R.OnDeath(R.OnRunStart(R.OnDeath(R.OnRunStart(s))));
        Check("offers resume after a buy", R.Due(s));

        var big = new R.State { streak = R.StreakCap, lastRunDied = true };
        Check("the streak is capped", R.OnDeath(big).streak == R.StreakCap);
    }

    static void ClearShips()
    {
        for (int id = 2; id < shopingShips.RosterTotal; id++)
        {
            PlayerPrefs.DeleteKey(ShipId.OwnedKey(id));
            for (int n = 1; n < ShipSkins.PerShip; n++) PlayerPrefs.DeleteKey(ShipSkins.OwnedKey(id, n));
        }
        for (int n = 1; n < ShipSkins.PerShip; n++) PlayerPrefs.DeleteKey(ShipSkins.OwnedKey(1, n));
        PlayerPrefs.DeleteKey(UpgradeOfferStore.StreakKey);
        PlayerPrefs.DeleteKey(UpgradeOfferStore.DeclinedKey);
        PlayerPrefs.DeleteKey(UpgradeOfferStore.LastDiedKey);
        PlayerPrefs.DeleteKey(UpgradeOfferStore.EnabledKey);
        ShipId.Equip(1);
    }

    static void Store()
    {
        DeveloperUnlocks.SetEnabled(false);
        ClearShips();
        PlayerPrefs.SetString(ShipId.OwnedKey(1), "True");
        R.Candidate c;

        // not due yet
        PlayerPrefs.SetFloat(StarDustLedger.CurrencyKey, 100000f);
        Check("no offer before two deaths", !UpgradeOfferStore.TryGetOffer(out c));
        UpgradeOfferStore.RunDied();
        Check("no offer after one death", !UpgradeOfferStore.TryGetOffer(out c));
        UpgradeOfferStore.RunStarted();
        UpgradeOfferStore.RunDied();
        Check("an offer after the second death in a row", UpgradeOfferStore.TryGetOffer(out c));
        Check("it is the equipped ship's first colour", c.kind == R.Kind.Skin && c.shipId == 1 && c.skin == 1 && c.price > 0f);

        // setting off
        UpgradeOfferStore.Enabled = false;
        Check("the setting turns it off", !UpgradeOfferStore.TryGetOffer(out c));
        UpgradeOfferStore.Enabled = true;
        Check("the setting defaults on again", UpgradeOfferStore.TryGetOffer(out c));
        Check("the setting stores nothing while on", !PlayerPrefs.HasKey(UpgradeOfferStore.EnabledKey));

        // cannot afford: the equipped colour and every other step are too dear
        PlayerPrefs.SetFloat(StarDustLedger.CurrencyKey, 1f);
        Check("cannot afford anything: no offer", !UpgradeOfferStore.TryGetOffer(out c));

        // can only afford another step: the cheapest ship is offered, never Paragon
        PlayerPrefs.SetFloat(StarDustLedger.CurrencyKey, 100000f);
        for (int n = 1; n < ShipSkins.CountFor(1); n++) PlayerPrefs.SetInt(ShipSkins.OwnedKey(1, n), 1);
        Check("equipped ship fully upgraded: the next ship is offered",
              UpgradeOfferStore.TryGetOffer(out c) && c.kind == R.Kind.Ship && !PremiumShips.IsPremium(c.shipId));
        float cheapest = float.MaxValue;
        foreach (int id in ShipId.All)
            if (!PremiumShips.IsPremium(id) && !ShipId.IsOwned(id)) cheapest = Mathf.Min(cheapest, shopingShips.CostFor(id));
        Check("it is the cheapest unowned ship", c.price == cheapest);

        // buying through the store spends, grants and resets the backoff
        PlayerPrefs.SetFloat(StarDustLedger.CurrencyKey, c.price + 7f);
        int shipId = c.shipId;
        Check("buying succeeds", UpgradeOfferStore.Buy(c));
        Check("the ship is owned and the dust spent", ShipId.IsOwned(shipId) &&
              Mathf.Approximately(PlayerPrefs.GetFloat(StarDustLedger.CurrencyKey), 7f));
        Check("the backoff is reset", PlayerPrefs.GetInt(UpgradeOfferStore.StreakKey) == 0 &&
              PlayerPrefs.GetInt(UpgradeOfferStore.DeclinedKey) == 0);
        Check("a repeat buy of an owned ship is refused", !UpgradeOfferStore.Buy(c));

        // everything owned: no offer, whatever the balance
        PlayerPrefs.SetFloat(StarDustLedger.CurrencyKey, 1000000f);
        foreach (int id in ShipId.All)
        {
            if (PremiumShips.IsPremium(id)) continue;
            PlayerPrefs.SetString(ShipId.OwnedKey(id), "True");
            for (int n = 1; n < ShipSkins.CountFor(id); n++) PlayerPrefs.SetInt(ShipSkins.OwnedKey(id, n), 1);
        }
        UpgradeOfferStore.RunStarted();
        UpgradeOfferStore.RunDied();
        UpgradeOfferStore.RunStarted();
        UpgradeOfferStore.RunDied();
        Check("everything bought: no offer", !UpgradeOfferStore.TryGetOffer(out c));
        Check("Paragon is never a candidate", !PremiumShips.IsPremium(UpgradeOfferStore.CheapestShip().shipId) ||
              UpgradeOfferStore.CheapestShip().price <= 0f);

        // declines persist and the cloud save carries the backoff
        ClearShips();
        UpgradeOfferStore.Declined();
        Check("a decline is saved", UpgradeOfferStore.Load().declined == 1);
        Check("the backoff counters are in the cloud save",
              ProgressSnapshot.CounterKeys().Contains(UpgradeOfferStore.StreakKey) &&
              ProgressSnapshot.CounterKeys().Contains(UpgradeOfferStore.DeclinedKey));
        ClearShips();
    }
}
