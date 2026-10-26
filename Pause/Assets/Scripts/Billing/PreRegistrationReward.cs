using System;
using System.Collections.Generic;
using UnityEngine;

// The Google Play pre-registration reward.
//
// Play delivers it as a free one-time (managed) in-app product owned by the
// player's Google account. The game finds it at launch (PlayBilling queries
// the owned purchases) and live (the purchases-updated callback), pays
// DustAmount star dust and consumes the purchase, so Play will never hand it
// out again. It arrives without a developerPayload and may arrive without an
// orderId, so neither is read.
//
// Granting is idempotent without trusting the consume: the purchase token is
// written to PlayerPrefs in the same save as the dust, and forgotten only once
// the consume succeeded. A crash between grant and consume (or a consume that
// fails) therefore just retries the consume next launch -- no second payout.
//
// Create the product in Play Console > Monetize > In-app products with id
// ProductId (one-time, free) and point the pre-registration reward at it.
public static class PreRegistrationReward
{
    // The one place the Play Console product id lives.
    public const string ProductId = "prereg_stardust_100";

    public const int DustAmount = 100;

    // Purchase tokens already paid whose consume has not been confirmed yet,
    // newline separated. Empty in the normal case.
    public const string GrantedKey = "preregGrantedTokens";

    public const string Message = "Pre-registration reward: +100 star dust";

    public enum Result { Ignored, Pending, Granted, AlreadyGranted }

    // Raised after dust is paid, with the amount.
    public static event Action<int> Rewarded;

    // True from a payout until the home screen has told the player.
    public static bool ToastDue { get; set; }

    static readonly HashSet<string> consuming = new HashSet<string>();

    // Handles one owned purchase. `consume(token, done)` asks the store to
    // consume it and reports success through `done`.
    public static Result Handle(PlayPurchase purchase, Action<string, Action<bool>> consume)
    {
        if (purchase.productId != ProductId) return Result.Ignored;
        // A pending purchase is not paid for yet (or not confirmed): wait for it to become Purchased.
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
            ToastDue = true;
            Debug.Log("[PreReg] granted " + DustAmount + " star dust");
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
            Debug.Log("[PreReg] consume failed, will retry next launch");
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

    // Developer mode / editor: pays a fake reward as if Play had delivered it.
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

    // Test seam: forgets the in-flight consume bookkeeping.
    public static void ResetSession()
    {
        consuming.Clear();
        ToastDue = false;
    }
}

public enum PlayPurchaseState { Unspecified = 0, Purchased = 1, Pending = 2 }

// One owned purchase, reduced to what the game reads. orderId and
// developerPayload are deliberately absent: the reward has neither.
// `acknowledged` only matters to non-consumables (Paragon).
public struct PlayPurchase
{
    public string productId;
    public string token;
    public PlayPurchaseState state;
    public bool acknowledged;
}
