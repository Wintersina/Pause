using UnityEngine;

// The real-money Paragon purchase: granted only when Purchased and only for its
// product, pending and unknown states grant nothing, an unacknowledged purchase
// is acknowledged once and retried after a failure, star dust never buys it, and
// the dock keeps COMING SOON without a store price.
//
//   Unity -batchmode -quit -projectPath Pause -executeMethod ParagonPurchaseTest.Run
public static class ParagonPurchaseTest
{
    static int failures;

    static void Check(string label, bool condition)
    {
        if (!condition) failures++;
        Debug.Log("[PARP] " + (condition ? "PASS " : "FAIL ") + label);
    }

    public static void Run()
    {
        TestHarness.Exit(Execute());
    }

    const int P = PremiumShips.Paragon;

    static PlayPurchase Paragon(string token, PlayPurchaseState state = PlayPurchaseState.Purchased,
                                bool acknowledged = false, string product = PremiumShips.ParagonProductId)
    {
        return new PlayPurchase { productId = product, token = token, state = state, acknowledged = acknowledged };
    }

    public static int Execute()
    {
        failures = 0;
        using var sandbox = new TestHarness.Sandbox();
        PlayerPrefs.SetInt(DeveloperUnlocks.EnabledKey, 0);   // the real rules, not the dev override
        PlayerPrefs.DeleteKey(ShipId.OwnedKey(P));
        ParagonPurchase.ResetSession();

        int acks = 0;
        System.Action<string> ack = t => acks++;
        int changed = 0;
        System.Action onChanged = () => changed++;
        ParagonPurchase.Changed += onChanged;

        try
        {
            // Without a store price the dock reads COMING SOON, exactly as before.
            Check("no price: not available", !ParagonPurchase.Available && ParagonPurchase.BuyLabel == "");
            Check("no price: still coming soon", PremiumShips.IsPremium(P) && !ShipId.IsOwned(P));
            Check("buy tap without a store does nothing", !ParagonPurchase.RequestBuy() && !ParagonPurchase.Buying);

            // A price alone is not enough: the billing flow must be wired too.
            ParagonPurchase.SetPrice("$4.99");
            Check("price without a billing client is not available", !ParagonPurchase.Available);
            int launches = 0;
            ParagonPurchase.LaunchFlow = () => launches++;
            Check("price and billing: available", ParagonPurchase.Available);
            Check("buy label carries the store price", ParagonPurchase.BuyLabel == "BUY $4.99");
            Check("price change redraws the dock", changed == 1);
            ParagonPurchase.SetPrice("$4.99");
            Check("same price does not redraw", changed == 1);

            // Paragon opens at 5 owned ships (ShipUnlocks): fewer, and the tap does nothing.
            Check("buy tap with too few ships does nothing", !ParagonPurchase.RequestBuy() && launches == 0 && !ParagonPurchase.Buying);
            for (int id = 2; id <= 5; id++) PlayerPrefs.SetString(ShipId.OwnedKey(id), "True");

            // The buy tap opens the store once; a second tap while buying is ignored.
            Check("buy tap launches the flow", ParagonPurchase.RequestBuy() && launches == 1 && ParagonPurchase.Buying);
            Check("second tap while buying is ignored", !ParagonPurchase.RequestBuy() && launches == 1);
            ParagonPurchase.EndBuy();

            // Star dust never buys the hull.
            PlayerPrefs.SetFloat(StarDustLedger.CurrencyKey, 1000000f);
            Check("dust cannot buy the hull", !PremiumShips.DustPurchasable(P) && !shopingShips.TryPurchase(P, 1f) && !ShipId.IsOwned(P));

            // Pending, unknown and tokenless purchases grant nothing.
            var r = ParagonPurchase.Handle(Paragon("tok-p", PlayPurchaseState.Pending), ack);
            Check("pending is not granted", r == ParagonPurchase.Result.Pending && !ShipId.IsOwned(P) && acks == 0);
            r = ParagonPurchase.Handle(Paragon("tok-u", PlayPurchaseState.Unspecified), ack);
            Check("unspecified state is ignored", r == ParagonPurchase.Result.Ignored && !ShipId.IsOwned(P));
            r = ParagonPurchase.Handle(Paragon(""), ack);
            Check("a purchase without a token is ignored", r == ParagonPurchase.Result.Ignored && !ShipId.IsOwned(P));
            r = ParagonPurchase.Handle(Paragon("tok-x", PlayPurchaseState.Purchased, false, PreRegistrationReward.ProductId), ack);
            Check("another product is ignored", r == ParagonPurchase.Result.Ignored && !ShipId.IsOwned(P) && acks == 0);

            // Purchased: owned, acknowledged once, duplicate deliveries share one acknowledge.
            changed = 0;
            r = ParagonPurchase.Handle(Paragon("tok-1"), ack);
            Check("purchased is granted", r == ParagonPurchase.Result.Granted && ShipId.IsOwned(P));
            Check("grant persists through the boughtship key", PlayerPrefs.GetString(ShipId.OwnedKey(P), "") == "True");
            Check("grant redraws the dock", changed == 1);
            Check("acknowledged once", acks == 1);
            r = ParagonPurchase.Handle(Paragon("tok-1"), ack);
            Check("duplicate delivery grants nothing new", r == ParagonPurchase.Result.AlreadyOwned && acks == 1 && changed == 1);

            // A failed acknowledge is retried by the next query; a confirmed one is not.
            ParagonPurchase.OnAcknowledged("tok-1", false);
            r = ParagonPurchase.Handle(Paragon("tok-1"), ack);
            Check("failed acknowledge is retried", r == ParagonPurchase.Result.AlreadyOwned && acks == 2);
            ParagonPurchase.OnAcknowledged("tok-1", true);
            r = ParagonPurchase.Handle(Paragon("tok-1", PlayPurchaseState.Purchased, true), ack);
            Check("an acknowledged purchase is not acknowledged again", acks == 2);

            // Restore on a fresh install: an owned, already acknowledged purchase grants.
            PlayerPrefs.DeleteKey(ShipId.OwnedKey(P));
            ParagonPurchase.ResetSession();
            acks = 0;
            r = ParagonPurchase.Handle(Paragon("tok-2", PlayPurchaseState.Purchased, true), ack);
            Check("restore grants an owned purchase", r == ParagonPurchase.Result.Granted && ShipId.IsOwned(P));
            Check("restore of an acknowledged purchase acknowledges nothing", acks == 0);

            // A pending purchase that later completes grants then.
            PlayerPrefs.DeleteKey(ShipId.OwnedKey(P));
            r = ParagonPurchase.Handle(Paragon("tok-3", PlayPurchaseState.Pending), ack);
            Check("pending first grants nothing", !ShipId.IsOwned(P));
            r = ParagonPurchase.Handle(Paragon("tok-3"), ack);
            Check("the same purchase grants once it becomes Purchased", r == ParagonPurchase.Result.Granted && ShipId.IsOwned(P));

            // Billing response codes.
            Check("OK continues", ParagonPurchase.Classify(0) == ParagonPurchase.Outcome.Ok);
            Check("USER_CANCELED is a cancel", ParagonPurchase.Classify(1) == ParagonPurchase.Outcome.Cancelled);
            Check("ITEM_ALREADY_OWNED restores", ParagonPurchase.Classify(7) == ParagonPurchase.Outcome.AlreadyOwned);
            Check("service / network errors retry",
                  ParagonPurchase.Classify(-1) == ParagonPurchase.Outcome.Retry && ParagonPurchase.Classify(2) == ParagonPurchase.Outcome.Retry
                  && ParagonPurchase.Classify(12) == ParagonPurchase.Outcome.Retry);
            Check("other codes are errors", ParagonPurchase.Classify(6) == ParagonPurchase.Outcome.Error && ParagonPurchase.Classify(5) == ParagonPurchase.Outcome.Error);

            // The store going away puts the dock back to COMING SOON.
            ParagonPurchase.SetPrice(null);
            Check("losing the price withdraws the offer", !ParagonPurchase.Available);
        }
        finally
        {
            ParagonPurchase.Changed -= onChanged;
            PlayerPrefs.DeleteKey(ShipId.OwnedKey(P));
            ParagonPurchase.ResetSession();
        }
        return failures;
    }
}
