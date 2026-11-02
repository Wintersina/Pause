using UnityEngine;

// Remove Ads (one-time, permanent): granted only when Purchased and only for its product, pending grants
// nothing, an unacknowledged purchase is acknowledged once and retried after a failure, a restore grants,
// and the entitlement merges through the cloud save as "owned on either side wins" and is never lost.
//
//   Unity -batchmode -quit -projectPath Pause -executeMethod RemoveAdsPurchaseTest.Run
public static class RemoveAdsPurchaseTest
{
    static int failures;

    static void Check(string label, bool condition)
    {
        if (!condition) failures++;
        Debug.Log("[ADFREE] " + (condition ? "PASS " : "FAIL ") + label);
    }

    public static void Run()
    {
        TestHarness.Exit(Execute());
    }

    static PlayPurchase Buy(string token, PlayPurchaseState state = PlayPurchaseState.Purchased,
                            bool acknowledged = false, string product = RemoveAdsPurchase.ProductId)
    {
        return new PlayPurchase { productId = product, token = token, state = state, acknowledged = acknowledged };
    }

    public static int Execute()
    {
        failures = 0;
        using var sandbox = new TestHarness.Sandbox();
        PlayerPrefs.DeleteKey(AdFree.Key);
        RemoveAdsPurchase.ResetSession();

        int acks = 0;
        System.Action<string> ack = t => acks++;
        int changed = 0;
        System.Action onChanged = () => changed++;
        RemoveAdsPurchase.Changed += onChanged;

        try
        {
            Check("product id", RemoveAdsPurchase.ProductId == "me.hapticgate.pause.remove_ads");
            Check("starts with ads on", !AdFree.IsActive);

            // Store price / launch.
            Check("no price: not available, buy does nothing", !RemoveAdsPurchase.Available && !RemoveAdsPurchase.RequestBuy());
            int launches = 0;
            RemoveAdsPurchase.LaunchFlow = () => launches++;
            Check("launcher alone is not available", !RemoveAdsPurchase.Available);
            RemoveAdsPurchase.SetPrice("$3.99");
            Check("price and launcher: available", RemoveAdsPurchase.Available);
            Check("buy launches the flow once", RemoveAdsPurchase.RequestBuy() && launches == 1 && !RemoveAdsPurchase.RequestBuy() && launches == 1);
            RemoveAdsPurchase.EndBuy();

            // Pending, other product, no token.
            changed = 0;
            var r = RemoveAdsPurchase.Handle(Buy("p-1", PlayPurchaseState.Pending), ack);
            Check("pending is not granted", r == RemoveAdsPurchase.Result.Pending && !AdFree.IsActive && acks == 0);
            r = RemoveAdsPurchase.Handle(Buy("x", PlayPurchaseState.Purchased, false, "me.hapticgate.pause.ship_paragon"), ack);
            Check("another product is ignored", r == RemoveAdsPurchase.Result.Ignored && !AdFree.IsActive && acks == 0);
            r = RemoveAdsPurchase.Handle(Buy(""), ack);
            Check("no token is ignored", r == RemoveAdsPurchase.Result.Ignored && !AdFree.IsActive);

            // Grant + acknowledge.
            r = RemoveAdsPurchase.Handle(Buy("tok-1"), ack);
            Check("purchased grants ad-free", r == RemoveAdsPurchase.Result.Granted && AdFree.IsActive);
            Check("grant is stored in PlayerPrefs", PlayerPrefs.GetInt(AdFree.Key, 0) == 1);
            Check("acknowledged once, change raised once", acks == 1 && changed == 1);
            r = RemoveAdsPurchase.Handle(Buy("tok-1"), ack);
            Check("duplicate delivery grants nothing new and acknowledges once", r == RemoveAdsPurchase.Result.AlreadyOwned && acks == 1 && changed == 1);

            // Failed acknowledge is retried.
            RemoveAdsPurchase.OnAcknowledged("tok-1", false);
            r = RemoveAdsPurchase.Handle(Buy("tok-1"), ack);
            Check("failed acknowledge is retried", r == RemoveAdsPurchase.Result.AlreadyOwned && acks == 2);
            RemoveAdsPurchase.OnAcknowledged("tok-1", true);
            r = RemoveAdsPurchase.Handle(Buy("tok-1", PlayPurchaseState.Purchased, true), ack);
            Check("an acknowledged purchase is not acknowledged again", acks == 2);

            // Restore on a fresh install.
            PlayerPrefs.DeleteKey(AdFree.Key);
            RemoveAdsPurchase.ResetSession();
            acks = 0;
            r = RemoveAdsPurchase.Handle(Buy("tok-1", PlayPurchaseState.Purchased, true), ack);
            Check("restore grants an owned purchase without acknowledging", r == RemoveAdsPurchase.Result.Granted && AdFree.IsActive && acks == 0);

            // Pending first, then Purchased.
            PlayerPrefs.DeleteKey(AdFree.Key);
            RemoveAdsPurchase.Handle(Buy("tok-2", PlayPurchaseState.Pending), ack);
            Check("pending first grants nothing", !AdFree.IsActive);
            r = RemoveAdsPurchase.Handle(Buy("tok-2"), ack);
            Check("grants once it becomes Purchased", r == RemoveAdsPurchase.Result.Granted && AdFree.IsActive);

            // Response codes are shared with Paragon: already owned -> restore.
            Check("ITEM_ALREADY_OWNED restores", ParagonPurchase.Classify(7) == ParagonPurchase.Outcome.AlreadyOwned);

            // A buy tap when already ad-free does nothing.
            RemoveAdsPurchase.ResetSession();
            RemoveAdsPurchase.LaunchFlow = () => launches++;
            RemoveAdsPurchase.SetPrice("$3.99");
            launches = 0;
            Check("owned: buy does nothing", !RemoveAdsPurchase.RequestBuy() && launches == 0);

            // ---- Cloud save ----
            PlayerPrefs.DeleteKey(AdFree.Key);
            var local = ProgressSnapshot.Capture(1000);
            Check("capture: not owned", !local.adFree);
            PlayerPrefs.SetInt(AdFree.Key, 1);
            var owned = ProgressSnapshot.Capture(1000);
            Check("capture: owned", owned.adFree);
            Check("a snapshot holding only Remove Ads counts as progress", owned.HasProgress() && !local.HasProgress());

            ProgressSnapshot back;
            Check("round trips through JSON", ProgressSnapshot.TryParse(owned.ToJson(), out back) == ProgressSnapshot.ParseResult.Ok && back.adFree);

            var merged = ProgressMerge.Merge(local, owned);
            Check("merge: owned on the cloud side wins", merged.adFree);
            merged = ProgressMerge.Merge(owned, local);
            Check("merge: owned on the local side wins", merged.adFree);
            var newerWithout = ProgressSnapshot.Capture(9000);
            newerWithout.adFree = false;
            newerWithout.currency = 50f;
            owned.currency = 10f;
            Check("merge: owned wins even against a newer save without it", ProgressMerge.Merge(owned, newerWithout).adFree && ProgressMerge.Merge(newerWithout, owned).adFree);
            Check("merge: neither side owns -> not owned", !ProgressMerge.Merge(local, newerWithout).adFree);

            // Older save lacking the field.
            string json = owned.ToJson();
            string old = json.Replace(",\"adFree\":true", "").Replace("\"adFree\":true,", "");
            Check("an older blob lacks the field", !old.Contains("adFree"));
            Check("older blob parses as not owned, schema unchanged",
                  ProgressSnapshot.TryParse(old, out back) == ProgressSnapshot.ParseResult.Ok && !back.adFree &&
                  back.schemaVersion == ProgressSnapshot.CurrentSchemaVersion);
            Check("older cloud save never takes ownership away", ProgressMerge.Merge(owned, back).adFree);

            // Apply never clears.
            PlayerPrefs.SetInt(AdFree.Key, 1);
            back.Apply();
            Check("applying a snapshot without it keeps the purchase", AdFree.IsActive);
            PlayerPrefs.DeleteKey(AdFree.Key);
            owned.Apply();
            Check("applying a snapshot with it grants", AdFree.IsActive);
        }
        finally
        {
            RemoveAdsPurchase.Changed -= onChanged;
            PlayerPrefs.DeleteKey(AdFree.Key);
            RemoveAdsPurchase.ResetSession();
        }
        return failures;
    }
}
