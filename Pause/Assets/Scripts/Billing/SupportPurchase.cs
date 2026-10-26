using System;
using System.Collections.Generic;
using UnityEngine;

// "Support the Developer": a repeatable real-money one-time Google Play product
// (ProductId, $10) that pays DustAmount star dust each time it is bought.
//
// It is a consumable: PlayBilling queries its details (Play's own localized
// price), launches the billing flow from the dock's button, and feeds every
// owned or updated purchase to Handle below, which pays and consumes it so the
// player can buy it again.
//
// Granting is idempotent without trusting the consume, exactly like the
// pre-registration reward: the purchase token is written to PlayerPrefs in the
// same save as the dust and forgotten only once the consume succeeded. A crash
// between grant and consume (or a failed consume) just retries the consume on
// the next launch -- no second payout. A new purchase has a new token, so it
// pays again.
//
// Android only: iOS and the editor never get a price, so Available stays false
// and the dock hides the button.
public static class SupportPurchase
{
    // The one place the Play Console product id lives.
    public const string ProductId = "support_developer_2500";

    public const int DustAmount = 2500;

    // Purchase tokens already paid whose consume has not been confirmed yet,
    // newline separated. Empty in the normal case.
    public const string GrantedKey = "supportGrantedTokens";

    public const string GrantedMessage = "Thank you for supporting the developer! +2,500 star dust";
    public const string PendingMessage = "Purchase pending. The star dust arrives when the payment completes.";
    public const string ErrorMessage = "Purchase failed. Please try again later.";

    public enum Result { Ignored, Pending, Granted, AlreadyGranted }

    // The store's formatted price ("$9.99"), null while unknown or unavailable.
    public static string Price { get; private set; }

    // True from a buy tap until the store answers.
    public static bool Buying { get; private set; }

    // Raised when the price arrives or goes away, so the dock redraws.
    public static event Action Changed;

    // Raised after dust is paid, with the amount.
    public static event Action<int> Rewarded;

    // Set by PlayBilling while connected: opens Play's purchase sheet.
    public static Action LaunchFlow;

    public static bool Available
    {
        get { return !string.IsNullOrEmpty(Price) && LaunchFlow != null; }
    }

    public static void SetPrice(string formatted)
    {
        string clean = string.IsNullOrEmpty(formatted) ? null : formatted;
        if (clean == Price) return;
        Price = clean;
        var handler = Changed;
        if (handler != null) handler();
    }

    // The player tapped the button. Never without a store or while one is open.
    public static bool RequestBuy()
    {
        if (!Available || Buying) return false;
        Buying = true;
        LaunchFlow();
        return true;
    }

    public static void EndBuy() { Buying = false; }

    static readonly HashSet<string> consuming = new HashSet<string>();

    // Handles one owned or updated purchase. `consume(token, done)` asks the
    // store to consume it and reports success through `done`.
    public static Result Handle(PlayPurchase purchase, Action<string, Action<bool>> consume)
    {
        if (purchase.productId != ProductId) return Result.Ignored;
        // Not paid yet: wait for it to become Purchased.
        if (purchase.state == PlayPurchaseState.Pending) return Result.Pending;
        if (purchase.state != PlayPurchaseState.Purchased || string.IsNullOrEmpty(purchase.token)) return Result.Ignored;

        string token = purchase.token;
        var granted = GrantedTokens();
        bool fresh = !granted.Contains(token);
        if (fresh)
        {
            // The token and the dust land in one save, so a kill can't pay without remembering it.
            granted.Add(token);
            PlayerPrefs.SetString(GrantedKey, string.Join("\n", granted));
            StarDustLedger.Grant(DustAmount);
            PrefsSaver.SaveNow();
            Debug.Log("[Support] granted " + DustAmount + " star dust");
            var handler = Rewarded;
            if (handler != null) handler(DustAmount);
        }

        // Duplicate deliveries (launch query + live callback) share one consume.
        if (consume != null && consuming.Add(token))
            consume(token, ok => OnConsumed(token, ok));
        return fresh ? Result.Granted : Result.AlreadyGranted;
    }

    static void OnConsumed(string token, bool ok)
    {
        consuming.Remove(token);
        if (!ok)
        {
            Debug.Log("[Support] consume failed, will retry next launch");
            return;
        }
        var granted = GrantedTokens();
        if (!granted.Remove(token)) return;
        PlayerPrefs.SetString(GrantedKey, string.Join("\n", granted));
        PrefsSaver.SaveNow();
    }

    // Tokens paid but not yet confirmed consumed.
    public static List<string> GrantedTokens()
    {
        var list = new List<string>();
        foreach (string t in PlayerPrefs.GetString(GrantedKey, "").Split('\n'))
            if (t.Length > 0) list.Add(t);
        return list;
    }

    // Developer mode / editor: pays a fake purchase as if Play had delivered it.
    public static Result Simulate()
    {
        if (!DeveloperUnlocks.Available) return Result.Ignored;
        var fake = new PlayPurchase
        {
            productId = ProductId,
            token = "simulated-" + DateTime.UtcNow.Ticks,
            state = PlayPurchaseState.Purchased,
        };
        return Handle(fake, (token, done) => done(true));
    }

    // Test seam: forgets the session state.
    public static void ResetSession()
    {
        consuming.Clear();
        Price = null;
        Buying = false;
        LaunchFlow = null;
    }
}
