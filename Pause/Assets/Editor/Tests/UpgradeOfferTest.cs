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
            HeartsText();
            Card();
        }
        Debug.Log("[OFFER] failures: " + fails);
        return fails;
    }

    static R.Candidate Skin(int ship, int skin, float price)
    {
        return new R.Candidate { shipId = ship, skin = skin, price = price, title = "S" + ship };
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

        // what is offered: only the flown ship's next upgrade, only if affordable
        var sel = Skin(3, 1, 50f);
        Check("the selected ship's upgrade is offered when affordable",
              R.ShouldOffer(true, Streak(2), 500f, sel, out c) && c.shipId == 3 && c.skin == 1);
        Check("exact balance is affordable", R.ShouldOffer(true, Streak(2), 50f, sel, out c) && c.price == 50f);
        Check("too dear: no offer, never another ship's upgrade", !R.ShouldOffer(true, Streak(2), 40f, sel, out c));
        Check("nothing left to upgrade: no offer", !R.ShouldOffer(true, Streak(2), 99999f, none, out c));

        // the setting
        Check("setting off: never offered", !R.ShouldOffer(false, Streak(2), 99999f, Skin(3, 1, 50f), out c));
        Check("not due: not offered", !R.ShouldOffer(true, Streak(1), 99999f, Skin(3, 1, 50f), out c));
    }

    static void HeartsText()
    {
        int viper = ShipId.FromKey("VoltViper");
        var first = Skin(viper, 1, 50f);
        int gain = SkinHearts.GainIfBought(viper, 1);
        Check("offer card: a colour that adds a heart says so (" + UpgradeOfferCard.HeartsLine(first) + ")",
              gain > 0 && UpgradeOfferCard.HeartsLine(first) == "+" + gain + " " + SkinHearts.HeartsWord(gain));
        Check("offer card: no candidate shows no hearts line", UpgradeOfferCard.HeartsLine(default(R.Candidate)) == "");
        Check("offer card: the stock colour adds nothing", UpgradeOfferCard.HeartsLine(Skin(viper, ShipSkins.Stock, 50f)) == "");
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

    // The card itself: Android Back is NO THANKS (a Back layer, like the ship
    // offer card). It used to have no layer, so Back fell through to the scene
    // root: home screen, no results panel, no decline recorded.
    static void Card()
    {
        DeveloperUnlocks.SetEnabled(false);
        ClearShips();
        PlayerPrefs.SetString(ShipId.OwnedKey(1), "True");
        PlayerPrefs.SetFloat(StarDustLedger.CurrencyKey, 100000f);
        UpgradeOfferStore.RunStarted(); UpgradeOfferStore.RunDied();
        UpgradeOfferStore.RunStarted(); UpgradeOfferStore.RunDied();
        R.Candidate c;
        if (!UpgradeOfferStore.TryGetOffer(out c)) { Check("an offer to draw", false); return; }
        var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        var canvasGo = new GameObject("~UpgradeOfferTestCanvas", typeof(RectTransform), typeof(Canvas));
        canvasGo.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
        var loadScene = BackNavigator.LoadScene;
        string loaded = null;
        BackNavigator.LoadScene = s => loaded = s;   // the scene root would load the home screen
        try
        {
            int layers = BackNavigator.LayerCount;
            int declined = PlayerPrefs.GetInt(UpgradeOfferStore.DeclinedKey);
            int doneCount = 0; bool doneBought = true;
            var card = UpgradeOfferCard.Show(canvasGo.transform, font, c, 100000f, b => { doneCount++; doneBought = b; });
            Check("the card is a Back layer (like the ship offer)", BackNavigator.IsRegistered(card) && BackNavigator.LayerCount == layers + 1);
            BackNavigator.Back();
            Check("Back declines: the results follow, the decline is recorded, no scene change",
                  doneCount == 1 && !doneBought && card == null &&
                  PlayerPrefs.GetInt(UpgradeOfferStore.DeclinedKey) == declined + 1 && loaded == null);
            Check("and its layer is gone", BackNavigator.LayerCount == layers);

            // NO THANKS still closes, and takes the layer with it
            doneCount = 0;
            card = UpgradeOfferCard.Show(canvasGo.transform, font, c, 100000f, b => { doneCount++; doneBought = b; });
            card.DeclineButton.onClick.Invoke();
            Check("NO THANKS closes and unregisters", doneCount == 1 && !doneBought && card == null && BackNavigator.LayerCount == layers);
        }
        finally
        {
            BackNavigator.LoadScene = loadScene;
            Object.DestroyImmediate(canvasGo);
        }
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
        Check("it is the equipped ship's first colour", c.shipId == 1 && c.skin == 1 && c.price == ShipSkins.PriceOf(1, 1));

        // setting off
        UpgradeOfferStore.Enabled = false;
        Check("the setting turns it off", !UpgradeOfferStore.TryGetOffer(out c));
        UpgradeOfferStore.Enabled = true;
        Check("the setting defaults on again", UpgradeOfferStore.TryGetOffer(out c));
        Check("the setting stores nothing while on", !PlayerPrefs.HasKey(UpgradeOfferStore.EnabledKey));

        // cannot afford the equipped ship's next colour: nothing, even with other ships owned
        PlayerPrefs.SetString(ShipId.OwnedKey(2), "True");
        PlayerPrefs.SetFloat(StarDustLedger.CurrencyKey, ShipSkins.PriceOf(1, 1) - 1f);
        Check("cannot afford the flown ship's colour: no offer", !UpgradeOfferStore.TryGetOffer(out c));
        PlayerPrefs.DeleteKey(ShipId.OwnedKey(2));

        // later colours follow as each becomes the next step
        PlayerPrefs.SetFloat(StarDustLedger.CurrencyKey, 100000f);
        PlayerPrefs.SetInt(ShipSkins.OwnedKey(1, 1), 1);
        Check("after the first colour the second is offered",
              UpgradeOfferStore.TryGetOffer(out c) && c.shipId == 1 && c.skin == 2);

        // fully upgraded flown ship: no offer, never another ship or a new one
        for (int n = 1; n < ShipSkins.CountFor(1); n++) PlayerPrefs.SetInt(ShipSkins.OwnedKey(1, n), 1);
        Check("flown ship fully upgraded: no offer", !UpgradeOfferStore.TryGetOffer(out c));
        for (int n = 1; n < ShipSkins.PerShip; n++) PlayerPrefs.DeleteKey(ShipSkins.OwnedKey(1, n));
        Check("the offer is skin 1 again once reset", UpgradeOfferStore.TryGetOffer(out c) && c.skin == 1);

        // buying through the store spends, grants and resets the backoff
        PlayerPrefs.SetFloat(StarDustLedger.CurrencyKey, c.price + 7f);
        Check("buying succeeds", UpgradeOfferStore.Buy(c));
        Check("the colour is owned and the dust spent", ShipSkins.IsOwned(1, c.skin) &&
              Mathf.Approximately(PlayerPrefs.GetFloat(StarDustLedger.CurrencyKey), 7f));
        Check("the backoff is reset", PlayerPrefs.GetInt(UpgradeOfferStore.StreakKey) == 0 &&
              PlayerPrefs.GetInt(UpgradeOfferStore.DeclinedKey) == 0);
        Check("a repeat buy of an owned colour is refused", !UpgradeOfferStore.Buy(c));

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

        // declines persist and the cloud save carries the backoff
        ClearShips();
        UpgradeOfferStore.Declined();
        Check("a decline is saved", UpgradeOfferStore.Load().declined == 1);
        Check("the backoff counters are local only (not in the cloud save)",
              !ProgressSnapshot.CounterKeys().Contains(UpgradeOfferStore.StreakKey) &&
              !ProgressSnapshot.CounterKeys().Contains(UpgradeOfferStore.DeclinedKey));

        // three declines stop offers; a buy resets them and they stay reset
        PlayerPrefs.SetFloat(StarDustLedger.CurrencyKey, 100000f);
        UpgradeOfferStore.Declined();
        UpgradeOfferStore.Declined();
        Check("three declines are saved", UpgradeOfferStore.Load().declined == 3);
        UpgradeOfferStore.RunStarted(); UpgradeOfferStore.RunDied();
        UpgradeOfferStore.RunStarted(); UpgradeOfferStore.RunDied();
        Check("no offer after three declines", !UpgradeOfferStore.TryGetOffer(out c));
        R.Candidate pick = UpgradeOfferStore.NextSkin(1);
        Check("a buy resets declines", UpgradeOfferStore.Buy(pick) && UpgradeOfferStore.Load().declined == 0);
        UpgradeOfferStore.RunStarted(); UpgradeOfferStore.RunDied();
        UpgradeOfferStore.RunStarted(); UpgradeOfferStore.RunDied();
        Check("the reset stays: offers resume", UpgradeOfferStore.Load().declined == 0 && UpgradeOfferStore.TryGetOffer(out c));
        ClearShips();
    }
}
