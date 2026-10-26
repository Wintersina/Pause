using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using R = ShipOfferRules;

// The death-screen ship offer: ShipOfferRules' decision (flown ship fully
// upgraded, next roster ship, affordable, shared backoff, once per session),
// the store against the real roster / skins / prices (dock order, owned and
// Paragon skipped, the dock's purchase path), and the card (dock silhouette,
// the dock's ultimate / power text, BUY reveal, NOT NOW, no per-frame garbage).
//
//   Unity -batchmode -quit -projectPath Pause -executeMethod ShipOfferTest.Run
public static class ShipOfferTest
{
    static int fails;
    static void Check(string what, bool ok)
    {
        Debug.Log((ok ? "[SHIPOFFER] PASS  " : "[SHIPOFFER] FAIL  ") + what);
        if (!ok) fails++;
    }

    public static void Run() { TestHarness.Exit(Execute()); }

    public static int Execute()
    {
        fails = 0;
        using (new TestHarness.Sandbox())
        {
            try
            {
                Rules();
                Store();
                Card();
            }
            finally { ClearAll(); }
        }
        Debug.Log("[SHIPOFFER] failures: " + fails);
        return fails;
    }

    static UpgradeOfferRules.State Due() { return new UpgradeOfferRules.State { streak = 2, lastRunDied = true }; }

    static void Rules()
    {
        // fully upgraded = every colour past stock owned
        Check("all colours owned is fully upgraded", R.FullyUpgraded(4, s => true));
        Check("one colour missing is not", !R.FullyUpgraded(4, s => s != 2));
        Check("a ship with only its stock colour is fully upgraded", R.FullyUpgraded(1, s => false));
        Check("the stock colour is never asked about", R.FullyUpgraded(3, s => s != 0));

        // next ship: roster order after the flown one, skipping what can't be offered, wrapping
        var order = new List<int> { 1, 2, 8, 9, 3 };
        var blocked = new HashSet<int> { 1 };
        Func<int, bool> ok = id => !blocked.Contains(id);
        Check("the next roster ship after the flown one", R.NextShip(2, order, ok) == 8);
        blocked.Add(8);
        Check("an owned / gated next ship is skipped for the following one", R.NextShip(2, order, ok) == 9);
        Check("past the last berth it wraps round", R.NextShip(3, order, ok) == 2);
        Check("the flown ship itself is never offered", R.NextShip(2, new List<int> { 2 }, id => true) == 0);
        Check("a flown ship not in the order starts at the first berth", R.NextShip(16, order, ok) == 2);
        Check("nothing offerable: no ship", R.NextShip(2, order, id => false) == 0);
        Check("an empty roster: no ship", R.NextShip(2, new List<int>(), id => true) == 0);

        // the whole decision
        Check("due, maxed, affordable: offered", R.ShouldOffer(true, Due(), true, 8, 950f, 1000f, 0));
        Check("the exact balance is affordable", R.ShouldOffer(true, Due(), true, 8, 950f, 950f, 0));
        Check("the flown ship still has upgrades: no ship offer", !R.ShouldOffer(true, Due(), false, 8, 950f, 99999f, 0));
        Check("too dear: no offer", !R.ShouldOffer(true, Due(), true, 8, 950f, 949f, 0));
        Check("no next ship: no offer", !R.ShouldOffer(true, Due(), true, 0, 950f, 99999f, 0));
        Check("no price: no offer", !R.ShouldOffer(true, Due(), true, 8, 0f, 99999f, 0));
        Check("the setting off: no offer", !R.ShouldOffer(false, Due(), true, 8, 950f, 99999f, 0));
        Check("not due (one death): no offer",
              !R.ShouldOffer(true, new UpgradeOfferRules.State { streak = 1, lastRunDied = true }, true, 8, 950f, 99999f, 0));
        Check("already shown this session: no offer", !R.ShouldOffer(true, Due(), true, 8, 950f, 99999f, R.MaxShowsPerShipPerSession));
        // the shared backoff: a decline needs 4 deaths, three declines stop it until a buy
        var s = UpgradeOfferRules.OnDeclined(Due());
        Check("after one NOT NOW two deaths are not enough", !R.ShouldOffer(true, s, true, 8, 950f, 99999f, 0));
        s.streak = 4;
        Check("...four are", R.ShouldOffer(true, s, true, 8, 950f, 99999f, 0));
        s = new UpgradeOfferRules.State { streak = 100, declined = UpgradeOfferRules.MaxDeclines, lastRunDied = true };
        Check("after the last decline: never, however long the streak", !R.ShouldOffer(true, s, true, 8, 950f, 99999f, 0));
        Check("a buy restarts the cycle", R.ShouldOffer(true, UpgradeOfferRules.OnDeath(UpgradeOfferRules.OnDeath(UpgradeOfferRules.OnBought(s))), true, 8, 950f, 99999f, 0));

        // reveal curve: ink -> colour, a soft white lift that never reaches full white
        Check("the reveal starts as ink", Mathf.Approximately(R.RevealInk(0f), 1f) && Mathf.Approximately(R.RevealFlash(0f), 0f));
        Check("the reveal ends in full colour, no white", Mathf.Approximately(R.RevealInk(1f), 0f) && R.RevealFlash(1f) < .001f);
        Check("the white lift peaks at the restrained cap", Mathf.Approximately(R.RevealFlash(.5f), R.RevealFlashPeak) && R.RevealFlashPeak <= .35f);
        bool mono = true;
        for (int i = 1; i <= 20; i++) mono &= R.RevealInk(i / 20f) <= R.RevealInk((i - 1) / 20f);
        Check("the ink only ever fades", mono);
    }

    // ---- store -------------------------------------------------------------

    static void ClearAll()
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
        ShipOfferStore.ResetSession();
        ShipId.Equip(1);
    }

    static void MaxOut(int id)
    {
        for (int n = 1; n < ShipSkins.CountFor(id); n++) PlayerPrefs.SetInt(ShipSkins.OwnedKey(id, n), 1);
    }

    static void MakeDue()
    {
        UpgradeOfferStore.RunStarted(); UpgradeOfferStore.RunDied();
        UpgradeOfferStore.RunStarted(); UpgradeOfferStore.RunDied();
    }

    // The expected pick, worked out independently from the dock's berth order.
    static int ExpectedAfter(int flown)
    {
        var order = SpaceDock.BayOrder;
        int at = Array.IndexOf(order, flown);
        for (int k = 1; k <= order.Length; k++)
        {
            int id = order[((at + k) % order.Length + order.Length) % order.Length];
            if (id == flown || ShipId.IsOwned(id) || PremiumShips.IsPremium(id)) continue;
            return id;
        }
        return 0;
    }

    static void Store()
    {
        DeveloperUnlocks.SetEnabled(false);
        ClearAll();
        PlayerPrefs.SetString(ShipId.OwnedKey(1), "True");
        PlayerPrefs.SetFloat(StarDustLedger.CurrencyKey, 100000f);
        R.Offer o;
        UpgradeOfferRules.Candidate colour;

        Check("not due: no ship offer", !ShipOfferStore.TryGetOffer(out o));
        MakeDue();
        Check("flown ship has colours left: no ship offer", !ShipOfferStore.TryGetOffer(out o));
        Check("...the colour offer takes that death", UpgradeOfferStore.TryGetOffer(out colour));

        MaxOut(1);
        Check("fully upgraded: the colour offer has nothing", !UpgradeOfferStore.TryGetOffer(out colour));
        Check("fully upgraded and affordable: a ship offer", ShipOfferStore.TryGetOffer(out o));
        int next = ExpectedAfter(1);
        Check("it is the next ship in the dock's order (" + o.shipId + " vs " + next + ")", o.shipId == next && next > 0);
        Check("the starter's next berth is Volt Viper", o.shipId == ShipId.FromKey("VoltViper"));
        Check("its price is the dock's", Mathf.Approximately(o.price, shopingShips.CostFor(o.shipId)) && o.price > 0f);
        Check("it remembers the flown ship", o.flownId == 1);

        // an owned next ship is skipped for the following one
        PlayerPrefs.SetString(ShipId.OwnedKey(next), "True");
        int following = ExpectedAfter(1);
        Check("next one owned: the following roster ship (" + following + ")",
              ShipOfferStore.TryGetOffer(out o) && o.shipId == following && following != next);
        PlayerPrefs.DeleteKey(ShipId.OwnedKey(next));

        // never offered when unaffordable
        PlayerPrefs.SetFloat(StarDustLedger.CurrencyKey, shopingShips.CostFor(next) - 1f);
        Check("one dust short: no offer (never a cheaper ship further round)", !ShipOfferStore.TryGetOffer(out o));
        PlayerPrefs.SetFloat(StarDustLedger.CurrencyKey, 100000f);

        // the toggle
        UpgradeOfferStore.Enabled = false;
        Check("the UPGRADE OFFERS setting off: no ship offer", !ShipOfferStore.TryGetOffer(out o));
        UpgradeOfferStore.Enabled = true;

        // once per session per ship
        ShipOfferStore.MarkShown(next);
        Check("shown this session: not again", !ShipOfferStore.TryGetOffer(out o));
        ShipOfferStore.ResetSession();
        Check("a new session may show it again", ShipOfferStore.TryGetOffer(out o));

        // Paragon (real money) and owned ships are never offerable
        Check("Paragon is never a dust offer", !ShipOfferStore.Offerable(PremiumShips.Paragon));
        Check("an owned ship is never offered", !ShipOfferStore.Offerable(1));
        foreach (int id in PremiumShips.StandardIds) PlayerPrefs.SetString(ShipId.OwnedKey(id), "True");
        Check("every dust ship owned: no offer, Paragon or not", !ShipOfferStore.TryGetOffer(out o));
        Check("...NextShip finds nothing", ShipOfferStore.NextShip(1) == 0);
        for (int id = 2; id < shopingShips.RosterTotal; id++) PlayerPrefs.DeleteKey(ShipId.OwnedKey(id));

        // flying a different maxed ship: the berth after IT
        int fang = ShipId.FromKey("SolarFang");
        PlayerPrefs.SetString(ShipId.OwnedKey(fang), "True");
        MaxOut(fang);
        ShipId.Equip(fang);
        Check("flying Solar Fang: the berth after it (" + ExpectedAfter(fang) + ")",
              ShipOfferStore.TryGetOffer(out o) && o.shipId == ExpectedAfter(fang) && o.flownId == fang);
        PlayerPrefs.DeleteKey(ShipId.OwnedKey(fang));
        for (int n = 1; n < ShipSkins.PerShip; n++) PlayerPrefs.DeleteKey(ShipSkins.OwnedKey(fang, n));
        ShipId.Equip(1);

        // a NOT NOW is a decline in the shared backoff
        int before = PlayerPrefs.GetInt(UpgradeOfferStore.DeclinedKey);
        ShipOfferStore.Declined();
        Check("NOT NOW counts as a decline", PlayerPrefs.GetInt(UpgradeOfferStore.DeclinedKey) == before + 1);
        Check("...so the same streak is no longer due", !ShipOfferStore.TryGetOffer(out o));
        PlayerPrefs.DeleteKey(UpgradeOfferStore.DeclinedKey);
        MakeDue();

        // buying through the dock's path
        Check("due again", ShipOfferStore.TryGetOffer(out o));
        float price = shopingShips.CostFor(o.shipId);
        PlayerPrefs.SetFloat(StarDustLedger.CurrencyKey, price + 5f);
        Check("buy succeeds", ShipOfferStore.Buy(o));
        Check("the ship is owned, equipped and the dust spent", ShipId.IsOwned(o.shipId) && ShipId.Equipped() == o.shipId &&
              Mathf.Approximately(PlayerPrefs.GetFloat(StarDustLedger.CurrencyKey), 5f));
        Check("the backoff restarts", PlayerPrefs.GetInt(UpgradeOfferStore.StreakKey) == 0 &&
              PlayerPrefs.GetInt(UpgradeOfferStore.DeclinedKey) == 0);
        Check("a repeat buy is refused and spends nothing", !ShipOfferStore.Buy(o) &&
              Mathf.Approximately(PlayerPrefs.GetFloat(StarDustLedger.CurrencyKey), 5f));
        var paragon = new R.Offer { shipId = PremiumShips.Paragon, flownId = 1, price = 1f };
        Check("Paragon can never be bought through the offer", !ShipOfferStore.Buy(paragon) && !ShipId.IsOwned(PremiumShips.Paragon));
        int dear = ExpectedAfter(o.shipId);
        var tooDear = new R.Offer { shipId = dear, flownId = 1, price = shopingShips.CostFor(dear) };
        Check("an unaffordable buy is refused and grants nothing", dear > 0 && !ShipOfferStore.Buy(tooDear) && !ShipId.IsOwned(dear) &&
              Mathf.Approximately(PlayerPrefs.GetFloat(StarDustLedger.CurrencyKey), 5f));
    }

    // ---- card --------------------------------------------------------------

    static Transform NewCanvas()
    {
        var go = new GameObject("~ShipOfferTestCanvas", typeof(RectTransform), typeof(Canvas));
        go.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
        return go.transform;
    }

    static string TextOf(ShipOfferCard card, string name)
    {
        foreach (var t in card.GetComponentsInChildren<Text>(true))
            if (t.gameObject.name == name) return t.text;
        return null;
    }

    static void Card()
    {
        DeveloperUnlocks.SetEnabled(false);
        ClearAll();
        PlayerPrefs.SetFloat(StarDustLedger.CurrencyKey, 100000f);
        MaxOut(1);
        MakeDue();
        R.Offer o;
        if (!ShipOfferStore.TryGetOffer(out o)) { Check("an offer to draw", false); return; }
        var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        var canvas = NewCanvas();
        try
        {
            int doneCount = 0; bool doneBought = true;
            var card = ShipOfferCard.Show(canvas, font, o, 100000f, b => { doneCount++; doneBought = b; });
            int id = o.shipId;
            Check("the card shows the ship's name", TextOf(card, "Item") == ShipOfferCard.NameLine(id));
            Check("its ultimate, as the dock card words it", TextOf(card, "AttackName") == PowerPreview.Name(id, false) &&
                  TextOf(card, "AttackLine") == PowerPreview.Line(id, false) && TextOf(card, "AttackName").Length > 0);
            Check("its secret power, as the dock card words it", TextOf(card, "PowerName") == PowerPreview.Name(id, true) &&
                  TextOf(card, "PowerLine") == PowerPreview.Line(id, true) && TextOf(card, "PowerName").Length > 0);
            Check("what sets the power off", TextOf(card, "PowerTrigger") == ShipOfferCard.TriggerLine(id) &&
                  TextOf(card, "PowerTrigger").StartsWith("POWER FIRES  "));
            Check("the price", card.PriceText == UpgradeOfferCard.PriceLine(o.price));
            Check("why: the flown ship is maxed", TextOf(card, "Reason") == ShipOfferCard.ReasonLine(1));
            Check("BUY / NOT NOW", card.BuyButton != null && card.DeclineButton != null &&
                  card.DeclineButton.GetComponentInChildren<Text>().text == "NOT NOW");
            Check("shown marks the session", ShipOfferStore.ShownThisSession(id) == 1);

            // the dock's blacked-out hull
            Check("the hull is the ship's stock dock sprite", card.Hull != null && card.Hull.sprite == ShipHullArt.Get(id, ShipSkins.Stock, 0, 0));
            if (card.UsesDockMaterial)
            {
                var m = card.Hull.material;
                Check("drawn through the dock ship shader", DockArt.ShipMaterial != null && m.shader == DockArt.ShipMaterial.shader);
                Check("at full silhouette in the dock's ink", Mathf.Approximately(m.GetFloat("_Silhouette"), 1f) &&
                      Mathf.Abs(m.GetColor("_Ink").r - DockBay.SilhouetteInk.r) < .02f &&Mathf.Approximately(m.GetFloat("_Flash"), 0f));
            }
            else Check("no dock shader: the flat ink tint", card.Hull.color == DockBay.SilhouetteInk);
            Check("no outline on the ship art", card.Hull.GetComponent<Outline>() == null && card.Hull.GetComponent<Shadow>() == null);

            // no garbage per frame: pop-in and settled
            float t0 = card.ShownAt;
            for (int i = 0; i < 10; i++) card.Tick(t0 + i * .016f);
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 600; i++) card.Tick(t0 + i * .016f);
            long grown = GC.GetAllocatedBytesForCurrentThread() - before;
            Check("the card's frames allocate nothing (" + grown + " B)", grown == 0);

            // NOT NOW
            int declined = PlayerPrefs.GetInt(UpgradeOfferStore.DeclinedKey);
            card.DeclineButton.onClick.Invoke();
            Check("NOT NOW closes, reports no purchase and declines", doneCount == 1 && !doneBought && card == null &&
                  PlayerPrefs.GetInt(UpgradeOfferStore.DeclinedKey) == declined + 1 && !ShipId.IsOwned(id));

            // BUY and the reveal
            float price = shopingShips.CostFor(id);
            PlayerPrefs.SetFloat(StarDustLedger.CurrencyKey, price + 10f);
            doneCount = 0;
            card = ShipOfferCard.Show(canvas, font, o, price + 10f, b => { doneCount++; doneBought = b; });
            card.BuyButton.onClick.Invoke();
            Check("BUY owns the ship and spends", ShipId.IsOwned(id) && Mathf.Approximately(PlayerPrefs.GetFloat(StarDustLedger.CurrencyKey), 10f));
            Check("then reveals instead of closing", card != null && card.Revealing && doneCount == 0 &&
                  !card.BuyButton.gameObject.activeSelf && card.PriceText == ShipOfferCard.AcquiredLabel);
            float r0 = card.RevealStartedAt;
            card.Tick(r0 + R.RevealSeconds * .5f);
            Check("half way: part ink, a soft white lift", card.InkAmount > 0f && card.InkAmount < 1f &&
                  card.FlashAmount > 0f && card.FlashAmount <= R.RevealFlashPeak + .001f);
            before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 30; i++) card.Tick(r0 + i * .016f);
            grown = GC.GetAllocatedBytesForCurrentThread() - before;
            Check("the reveal allocates nothing (" + grown + " B)", grown == 0);
            card.Tick(r0 + R.RevealSeconds);
            Check("revealed: full colour, no white", card.InkAmount < .001f && card.FlashAmount < .001f &&
                  (!card.UsesDockMaterial || card.Hull.material.GetFloat("_Silhouette") < .001f));
            Check("held on the ship before closing", doneCount == 0);
            card.Tick(r0 + R.RevealSeconds + R.RevealHoldSeconds + .01f);
            Check("then closes, reporting the purchase", doneCount == 1 && doneBought && card == null);
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(canvas.gameObject);
        }
    }
}
