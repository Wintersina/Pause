using System.Collections.Generic;
using UnityEngine;

// App Store billing logic (AppleStoreRouter and friends, the platform-independent half of AppStoreBilling):
// one id table shared with Google Play, message parsing, consumables paid exactly once per transaction id and
// finished only after the dust was saved (also across a "relaunch" and a failed finish), non-consumables re-derived
// from the current entitlements, Ask to Buy, cancellations, refunds, and Restore Purchases.
//
//   Unity -batchmode -quit -projectPath Pause -executeMethod AppleStoreTest.Run
public static class AppleStoreTest
{
    static int failures;

    static void Check(string label, bool condition)
    {
        if (!condition) failures++;
        Debug.Log("[APPLE] " + (condition ? "PASS " : "FAIL ") + label);
    }

    public static void Run() { TestHarness.Exit(Execute()); }

    static float Dust { get { return PlayerPrefs.GetFloat(StarDustLedger.CurrencyKey); } }

    static string Tx(string id, string product, string state = "purchased")
    {
        return id + "\t" + product + "\t" + id + "\t" + state;
    }

    static void Reset()
    {
        PlayerPrefs.DeleteKey(SupportPurchase.GrantedKey);
        PlayerPrefs.DeleteKey(AppleStoreLedger.Key);
        PlayerPrefs.DeleteKey(AdFree.Key);
        PlayerPrefs.DeleteKey(ShipId.OwnedKey(PremiumShips.Paragon));
        PlayerPrefs.SetFloat(StarDustLedger.CurrencyKey, 10f);
        SupportPurchase.ResetSession();
        ParagonPurchase.ResetSession();
        RemoveAdsPurchase.ResetSession();
        AppleStoreRouter.ResetSession();
    }

    public static int Execute()
    {
        failures = 0;
        using var sandbox = new TestHarness.Sandbox();
        Reset();
        var finished = new List<string>();
        var toasts = new List<string>();
        try
        {
            AppleStoreRouter.NativeFinish = id => finished.Add(id);   // answers only when the test calls "finished"
            AppleStoreRouter.Toast = t => toasts.Add(t);
            string small = SupportPurchase.Small.productId, large = SupportPurchase.Large.productId;

            // ---- the id table ----
            var ids = AppleProducts.Ids();
            Check("table: five products", ids.Count == 5);
            Check("table: ids identical to Google Play's",
                  ids.Contains("me.hapticgate.pause.ship_paragon") && ids.Contains("me.hapticgate.pause.remove_ads") &&
                  ids.Contains("support_developer_850") && ids.Contains("support_developer_2500") && ids.Contains("stardust_6000"));
            Check("table: the pre-registration reward is not sold on iOS", !ids.Contains(PreRegistrationReward.ProductId));
            AppleProducts.Entry e;
            Check("table: Paragon and Remove Ads are non-consumable, packs are consumable",
                  AppleProducts.TryFind(PremiumShips.ParagonProductId, out e) && e.kind == AppleProductKind.NonConsumable &&
                  AppleProducts.TryFind(RemoveAdsPurchase.ProductId, out e) && e.kind == AppleProductKind.NonConsumable &&
                  AppleProducts.TryFind(large, out e) && e.kind == AppleProductKind.Consumable &&
                  !AppleProducts.TryFind("nope", out e));

            // ---- parsing ----
            AppleTransaction tx;
            Check("parse: a transaction", AppleStoreMessages.TryParseTransaction(Tx("77", large), out tx) &&
                  tx.transactionId == "77" && tx.productId == large && tx.state == AppleTxState.Purchased);
            Check("parse: revoked state", AppleStoreMessages.TryParseTransaction(Tx("78", large, "revoked"), out tx) && tx.state == AppleTxState.Revoked);
            Check("parse: garbage is rejected",
                  !AppleStoreMessages.TryParseTransaction("", out tx) && !AppleStoreMessages.TryParseTransaction("a\tb", out tx) &&
                  !AppleStoreMessages.TryParseTransaction(Tx("1", large, "weird"), out tx) && !AppleStoreMessages.TryParseTransaction("\t" + large + "\t1\tpurchased", out tx));
            var prices = AppleStoreMessages.ParsePrices(large + "\t$9.99\n" + small + "\t$3.99\nbad");
            Check("parse: prices", prices.Count == 2 && prices[large] == "$9.99" && prices[small] == "$3.99");
            Check("parse: id lists", AppleStoreMessages.ParseIds("a\nb\n\n").Count == 2 && AppleStoreMessages.ParseIds("").Count == 0);

            // ---- products and launch flows ----
            AppleStoreRouter.Attach();
            Check("attach: launch flows set, no price yet, so nothing is buyable",
                  ParagonPurchase.LaunchFlow != null && RemoveAdsPurchase.LaunchFlow != null && SupportPurchase.Large.LaunchFlow != null &&
                  !ParagonPurchase.Available && !SupportPurchase.Large.Available);
            AppleStoreRouter.OnMessage("products", PremiumShips.ParagonProductId + "\t$4.99\n" + large + "\t$9.99");
            Check("products: localized prices land, missing ones stay unavailable",
                  ParagonPurchase.Price == "$4.99" && ParagonPurchase.Available && SupportPurchase.Large.Price == "$9.99" &&
                  SupportPurchase.Large.Available && !SupportPurchase.Small.Available && !RemoveAdsPurchase.Available);
            var bought = new List<string>();
            AppleStoreRouter.NativeBuy = id => bought.Add(id);
            Check("buy: a tap reaches the store with the product id and marks Buying",
                  SupportPurchase.Large.RequestBuy() && bought.Count == 1 && bought[0] == large && SupportPurchase.Large.Buying);
            AppleStoreRouter.OnMessage("cancelled", large);
            Check("cancelled: Buying clears, no toast, no dust", !SupportPurchase.Large.Buying && toasts.Count == 0 && Dust == 10f);

            // ---- consumable: exactly once, finished after the grant ----
            SupportPurchase.Large.RequestBuy();
            AppleStoreRouter.OnMessage("tx", Tx("1001", large));
            Check("consumable: dust paid and the sheet released", Dust == 2510f && !SupportPurchase.Large.Buying && toasts.Contains("+2,500 star dust"));
            Check("consumable: finish asked once, after the grant", finished.Count == 1 && finished[0] == "1001");
            Check("consumable: not recorded as finished until the store confirms", !AppleStoreLedger.WasFinished("1001") && SupportPurchase.GrantedTokens().Contains("1001"));
            AppleStoreRouter.OnMessage("tx", Tx("1001", large));   // updates + unfinished both deliver it
            Check("consumable: a duplicate delivery pays nothing and does not finish twice", Dust == 2510f && finished.Count == 1);
            AppleStoreRouter.OnMessage("finished", "1001");
            Check("consumable: confirmed finish clears the pending token and records it",
                  AppleStoreLedger.WasFinished("1001") && !SupportPurchase.GrantedTokens().Contains("1001"));
            // a "relaunch": session state gone, the persisted record is what remains
            SupportPurchase.ResetSession();
            AppleStoreRouter.ResetSession();
            AppleStoreRouter.NativeFinish = id => finished.Add(id);
            AppleStoreRouter.Toast = t => toasts.Add(t);
            AppleStoreRouter.OnMessage("tx", Tx("1001", large));
            Check("consumable: a redelivery after a relaunch never pays again", Dust == 2510f);
            AppleStoreRouter.OnMessage("tx", Tx("1002", large));
            Check("consumable: a new transaction id pays again", Dust == 5010f);
            AppleStoreRouter.OnMessage("finished", "1002");

            // crash between grant and finish: paid and remembered, finish retried on the next launch, still paid once
            AppleStoreRouter.OnMessage("tx", Tx("1003", small));
            float afterSmall = Dust;
            Check("crash case: 850 paid", afterSmall == 5860f);
            SupportPurchase.ResetSession();   // killed before the store confirmed the finish
            AppleStoreRouter.ResetSession();
            int finishesBefore = finished.Count;
            AppleStoreRouter.NativeFinish = id => finished.Add(id);
            AppleStoreRouter.Toast = t => toasts.Add(t);
            AppleStoreRouter.OnMessage("tx", Tx("1003", small));
            Check("crash case: redelivery pays nothing but retries the finish", Dust == afterSmall && finished.Count == finishesBefore + 1);
            AppleStoreRouter.OnMessage("finished", "1003");
            Check("crash case: then it is recorded", AppleStoreLedger.WasFinished("1003"));

            // ---- Ask to Buy and refunds ----
            SupportPurchase.Small.SetPrice("$3.99");
            SupportPurchase.Small.LaunchFlow = () => { };
            SupportPurchase.Small.RequestBuy();
            toasts.Clear();
            AppleStoreRouter.OnMessage("pending", small);
            Check("pending: nothing paid, a pending toast, the button is free again",
                  Dust == afterSmall && toasts.Count == 1 && toasts[0] == SupportPurchase.PendingMessage && !SupportPurchase.Small.Buying);
            AppleStoreRouter.OnMessage("tx", Tx("1004", small));
            Check("pending: the approved transaction arrives later through updates and pays", Dust == afterSmall + 850f);
            AppleStoreRouter.OnMessage("finished", "1004");
            float beforeRefund = Dust;
            int f0 = finished.Count;
            AppleStoreRouter.OnMessage("tx", Tx("1005", small, "revoked"));
            Check("revoked consumable: nothing paid, but finished so it stops coming back", Dust == beforeRefund && finished.Count == f0 + 1);
            AppleStoreRouter.OnMessage("tx", Tx("1006", "com.other.product"));
            Check("unknown product: ignored and left unfinished", Dust == beforeRefund && finished.Count == f0 + 1);

            // ---- non-consumables ----
            AppleStoreRouter.OnMessage("products", PremiumShips.ParagonProductId + "\t$4.99\n" + RemoveAdsPurchase.ProductId + "\t$3.99");
            ParagonPurchase.RequestBuy();
            Check("paragon: not owned yet", !ShipId.IsOwned(PremiumShips.Paragon));
            f0 = finished.Count;
            toasts.Clear();
            AppleStoreRouter.OnMessage("tx", Tx("2001", PremiumShips.ParagonProductId));
            Check("paragon: purchase grants the ship, toasts and finishes after the grant",
                  ShipId.IsOwned(PremiumShips.Paragon) && toasts.Contains(ParagonPurchase.GrantedMessage) && finished.Count == f0 + 1 && !ParagonPurchase.Buying);
            Check("remove ads: not owned", !AdFree.IsActive);
            AppleStoreRouter.OnMessage("tx", Tx("2002", RemoveAdsPurchase.ProductId));
            Check("remove ads: purchase grants the entitlement", AdFree.IsActive);
            AppleStoreRouter.OnMessage("tx", Tx("2003", RemoveAdsPurchase.ProductId, "revoked"));
            Check("remove ads: a refund transaction never takes ownership away (same rule as Play)", AdFree.IsActive);

            // new device / reinstall: entitlements re-derive both, silently idempotent
            PlayerPrefs.DeleteKey(AdFree.Key);
            PlayerPrefs.DeleteKey(ShipId.OwnedKey(PremiumShips.Paragon));
            ParagonPurchase.ResetSession();
            RemoveAdsPurchase.ResetSession();
            toasts.Clear();
            int fresh = AppleStoreRouter.Reconcile(new[] { PremiumShips.ParagonProductId, RemoveAdsPurchase.ProductId, large, "junk" }, true);
            Check("entitlements: owned non-consumables are re-derived, consumables and junk ignored",
                  fresh == 2 && ShipId.IsOwned(PremiumShips.Paragon) && AdFree.IsActive && Dust == beforeRefund + 0f);
            Check("entitlements: running it again grants nothing new",
                  AppleStoreRouter.Reconcile(new[] { PremiumShips.ParagonProductId, RemoveAdsPurchase.ProductId }, true) == 0);
            Check("entitlements: an empty list changes nothing", AppleStoreRouter.Reconcile(new string[0], true) == 0 && AdFree.IsActive);

            // ---- Restore Purchases ----
            int restores = 0;
            AppleStoreRouter.NativeRestore = () => restores++;
            toasts.Clear();
            AppleStoreRouter.Restore();
            AppleStoreRouter.Restore();
            Check("restore: asks the store once while busy", restores == 1 && AppleStoreRouter.Restoring);
            AppleStoreRouter.OnMessage("entitlements", PremiumShips.ParagonProductId);
            AppleStoreRouter.OnMessage("restored", "ok");
            Check("restore: finished, no longer busy, 'up to date' when nothing was new",
                  !AppleStoreRouter.Restoring && toasts.Count == 1 && toasts[0] == AppleStoreRouter.NothingToRestoreMessage);
            PlayerPrefs.DeleteKey(AdFree.Key);
            RemoveAdsPurchase.ResetSession();
            toasts.Clear();
            AppleStoreRouter.Restore();
            AppleStoreRouter.OnMessage("entitlements", RemoveAdsPurchase.ProductId);
            AppleStoreRouter.OnMessage("restored", "ok");
            Check("restore: a restored entitlement is granted and reported",
                  AdFree.IsActive && toasts.Contains(AppleStoreRouter.RestoredMessage));
            toasts.Clear();
            AppleStoreRouter.Restore();
            AppleStoreRouter.OnMessage("restored", "error\tnetwork");
            Check("restore: a failure reports and frees the button again", !AppleStoreRouter.Restoring && toasts.Contains(AppleStoreRouter.RestoreErrorMessage));

            // ---- errors ----
            SupportPurchase.Large.SetPrice("$9.99");
            SupportPurchase.Large.LaunchFlow = () => { };
            AppleStoreRouter.NativeBuy = id => { };
            SupportPurchase.Large.RequestBuy();
            toasts.Clear();
            AppleStoreRouter.OnMessage("error", large + "\tboom");
            Check("error: toast and the button is free again", toasts.Contains(SupportPurchase.ErrorMessage) && !SupportPurchase.Large.Buying);
            toasts.Clear();
            AppleStoreRouter.OnMessage("error", "-\tproducts: offline");
            Check("error: a catalog-level error is silent", toasts.Count == 0);

            // ---- ledger cap ----
            PlayerPrefs.DeleteKey(AppleStoreLedger.Key);
            for (int i = 0; i < AppleStoreLedger.Cap + 10; i++) AppleStoreLedger.MarkFinished("t" + i);
            Check("ledger: capped, newest kept", AppleStoreLedger.Finished().Count == AppleStoreLedger.Cap &&
                  AppleStoreLedger.WasFinished("t" + (AppleStoreLedger.Cap + 9)) && !AppleStoreLedger.WasFinished("t0"));
        }
        finally
        {
            Reset();
            PlayerPrefs.DeleteKey(AppleStoreLedger.Key);
        }
        return failures;
    }
}
