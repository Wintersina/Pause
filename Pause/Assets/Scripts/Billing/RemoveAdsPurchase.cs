using System;
using System.Collections.Generic;
using UnityEngine;

// "Support the devs by removing the ads": the one-time, permanent Remove Ads
// product on Google Play (non-consumable), same pattern as ParagonPurchase.
//
// PlayBilling queries its details (Play's localized price), launches the
// billing flow, and feeds every owned or updated purchase to Handle below:
//   * Purchased -> AdFree.Grant (PlayerPrefs + cloud save) and acknowledge the
//     purchase. Play refunds an unacknowledged purchase after 3 days, so a
//     failed acknowledge is simply retried by the next owned-purchases query,
//     which is also the restore after a reinstall or on a new device.
//   * Pending -> wait; nothing is granted until Play says Purchased.
//   * anything else, or another product -> ignored.
// Response codes are classified by ParagonPurchase.Classify (ITEM_ALREADY_OWNED
// triggers a restore query). There is no UI for it yet; a Shop screen is next.
public static class RemoveAdsPurchase
{
    public enum Result { Ignored, Pending, Granted, AlreadyOwned }

    public const string ProductId = "me.hapticgate.pause.remove_ads";

    public const string GrantedMessage = "Ads removed. Thank you for supporting the devs!";
    public const string PendingMessage = "Purchase pending. Ads are removed when the payment completes.";
    public const string ErrorMessage = "Purchase failed. Please try again later.";

    // The store's formatted price ("$3.99"), null while unknown or unavailable.
    public static string Price { get; private set; }

    // True from a buy tap until the store answers.
    public static bool Buying { get; private set; }

    // Raised when the price arrives or the entitlement is granted.
    public static event Action Changed;

    // Set by PlayBilling while connected: opens Play's purchase sheet.
    public static Action LaunchFlow;

    public static bool Available { get { return !string.IsNullOrEmpty(Price) && LaunchFlow != null; } }

    public static void SetPrice(string formatted)
    {
        string clean = string.IsNullOrEmpty(formatted) ? null : formatted;
        if (clean == Price) return;
        Price = clean;
        Raise();
    }

    // The player tapped buy. Never when already ad-free or without a store.
    public static bool RequestBuy()
    {
        if (!Available || Buying || AdFree.IsActive) return false;
        Buying = true;
        LaunchFlow();
        return true;
    }

    public static void EndBuy() { Buying = false; }

    static readonly HashSet<string> acknowledging = new HashSet<string>();

    // Handles one owned or updated purchase. `acknowledge(token)` asks the store
    // to acknowledge it; a failure needs no bookkeeping, the next query retries.
    public static Result Handle(PlayPurchase purchase, Action<string> acknowledge)
    {
        if (purchase.productId != ProductId) return Result.Ignored;
        if (purchase.state == PlayPurchaseState.Pending) return Result.Pending;
        if (purchase.state != PlayPurchaseState.Purchased || string.IsNullOrEmpty(purchase.token)) return Result.Ignored;

        bool fresh = AdFree.Grant();
        if (fresh) Debug.Log("[RemoveAds] granted");

        // Duplicate deliveries (launch query + live callback) share one acknowledge.
        if (!purchase.acknowledged && acknowledge != null && acknowledging.Add(purchase.token))
            acknowledge(purchase.token);

        if (fresh) Raise();
        return fresh ? Result.Granted : Result.AlreadyOwned;
    }

    // The store answered an acknowledge; on failure the token may be tried again.
    public static void OnAcknowledged(string token, bool ok)
    {
        if (!ok) acknowledging.Remove(token);
    }

    // Test seam: forgets the session state.
    public static void ResetSession()
    {
        acknowledging.Clear();
        Price = null;
        Buying = false;
        LaunchFlow = null;
    }

    static void Raise()
    {
        var handler = Changed;
        if (handler != null) handler();
    }
}
