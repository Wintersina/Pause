using System;
using System.Collections.Generic;
using UnityEngine;

// The real-money purchase of the premium Paragon ship (Google Play).
//
// Paragon is a non-consumable one-time product, PremiumShips.ParagonProductId.
// PlayBilling queries its details (the store's own formatted price, localized),
// launches the billing flow when the player taps buy in the dock, and feeds
// every owned or updated purchase to Handle below, which decides what to do:
//   * Purchased -> PremiumShips.Grant (the same boughtship<id> key the roster
//     always used, so cloud save, the dock and the spawner pick it up) and
//     acknowledge the purchase. Play refunds an unacknowledged purchase after 3
//     days, so an acknowledge that fails is simply retried: every launch and
//     return to the app queries the owned purchases again (that is also the
//     restore after a reinstall or on a new device).
//   * Pending -> wait; nothing is granted until Play says Purchased.
//   * anything else, or another product -> ignored.
// Star dust never buys the hull (PremiumShips.DustPurchasable).
//
// iOS and the editor never get a price, so Available stays false and the dock
// keeps its COMING SOON berth.
public static class ParagonPurchase
{
    public enum Result { Ignored, Pending, Granted, AlreadyOwned }

    // What a Play Billing response code means for the flow.
    public enum Outcome { Ok, Cancelled, AlreadyOwned, Retry, Error }

    public const string GrantedMessage = "Paragon unlocked";
    public const string PendingMessage = "Purchase pending. Paragon unlocks when the payment completes.";
    public const string ErrorMessage = "Purchase failed. Please try again later.";

    // The store's formatted price ("$4.99"), null while unknown or unavailable.
    public static string Price { get; private set; }

    // True from a buy tap until the store answers.
    public static bool Buying { get; private set; }

    // Raised when the price arrives or the ship becomes owned, so the dock redraws.
    public static event Action Changed;

    // Set by PlayBilling while connected: opens Play's purchase sheet.
    public static Action LaunchFlow;

    // The dock may offer the hull for real money.
    public static bool Available
    {
        get { return !string.IsNullOrEmpty(Price) && LaunchFlow != null; }
    }

    // The dock's button text, e.g. "BUY $4.99". Empty when there is no price.
    public static string BuyLabel
    {
        get { return string.IsNullOrEmpty(Price) ? "" : "BUY " + Price; }
    }

    public static void SetPrice(string formatted)
    {
        string clean = string.IsNullOrEmpty(formatted) ? null : formatted;
        if (clean == Price) return;
        Price = clean;
        Raise();
    }

    // The player tapped buy. Never for an owned ship or without a store.
    public static bool RequestBuy()
    {
        if (!Available || Buying || ShipId.IsOwned(PremiumShips.Paragon)) return false;
        if (ShipUnlocks.IsLocked(PremiumShips.Paragon)) return false;   // own enough ships first (restore never comes through here)
        Buying = true;
        LaunchFlow();
        return true;
    }

    public static void EndBuy() { Buying = false; }

    // Maps a BillingResponseCode to what the flow does next.
    public static Outcome Classify(int responseCode)
    {
        switch (responseCode)
        {
            case 0: return Outcome.Ok;
            case 1: return Outcome.Cancelled;                 // USER_CANCELED
            case 7: return Outcome.AlreadyOwned;              // ITEM_ALREADY_OWNED: restore it
            case -3: case -1: case 2: case 12: return Outcome.Retry;   // timeout, disconnected, unavailable, network
            default: return Outcome.Error;
        }
    }

    static readonly HashSet<string> acknowledging = new HashSet<string>();

    // Handles one owned or updated purchase. `acknowledge(token)` asks the store
    // to acknowledge it; a failure needs no bookkeeping, the next query retries.
    public static Result Handle(PlayPurchase purchase, Action<string> acknowledge)
    {
        if (purchase.productId != PremiumShips.ParagonProductId) return Result.Ignored;
        if (purchase.state == PlayPurchaseState.Pending) return Result.Pending;
        if (purchase.state != PlayPurchaseState.Purchased || string.IsNullOrEmpty(purchase.token)) return Result.Ignored;

        bool fresh = !ShipId.IsOwned(PremiumShips.Paragon);
        if (fresh)
        {
            PremiumShips.Grant(PremiumShips.Paragon);
            Debug.Log("[Paragon] granted");
        }

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
