using System;
using System.Collections.Generic;
using UnityEngine;

// "Support the Developer": repeatable real-money one-time Google Play products
// (the Packs table below: $4 pays 850 star dust, $10 pays 2,500) that each pay
// their dust every time they are bought. Adding a pack is one row in Packs.
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
    // One purchasable pack. Its price and buy state are per pack.
    public sealed class Pack
    {
        public readonly string id;          // stable short id (news / tile)
        public readonly string productId;   // the Play Console product id
        public readonly int dust;
        public readonly string label;       // "+850 STAR DUST"
        public readonly string grantedMessage;

        // The store's formatted price ("$3.99"), null while unknown or unavailable.
        public string Price { get; private set; }
        // True from a buy tap until the store answers.
        public bool Buying { get; private set; }
        // Set by PlayBilling while connected: opens Play's purchase sheet.
        public Action LaunchFlow;

        public Pack(string id, string productId, int dust)
        {
            this.id = id;
            this.productId = productId;
            this.dust = dust;
            label = "+" + dust.ToString("N0") + " STAR DUST";
            grantedMessage = "Thank you for supporting the developer! +" + dust.ToString("N0") + " star dust";
        }

        public bool Available { get { return !string.IsNullOrEmpty(Price) && LaunchFlow != null; } }

        public void SetPrice(string formatted)
        {
            string clean = string.IsNullOrEmpty(formatted) ? null : formatted;
            if (clean == Price) return;
            Price = clean;
            var handler = Changed;
            if (handler != null) handler();
        }

        // The player tapped the pack. Never without a store or while one is open.
        public bool RequestBuy()
        {
            if (!Available || Buying) return false;
            Buying = true;
            LaunchFlow();
            return true;
        }

        public void EndBuy() { Buying = false; }

        internal void Reset() { Price = null; Buying = false; LaunchFlow = null; }
    }

    // The pack table, in dock order. Add a pack by adding a row (and the same
    // product id in the Play Console).
    public static readonly Pack Small = new Pack("support850", "support_developer_850", 850);
    public static readonly Pack Large = new Pack("support2500", "support_developer_2500", 2500);
    public static readonly Pack[] Packs = { Small, Large };

    // Purchase tokens already paid whose consume has not been confirmed yet,
    // newline separated. Empty in the normal case.
    public const string GrantedKey = "supportGrantedTokens";

    public const string PendingMessage = "Purchase pending. The star dust arrives when the payment completes.";
    public const string ErrorMessage = "Purchase failed. Please try again later.";

    public enum Result { Ignored, Pending, Granted, AlreadyGranted }

    // The pack sold under a Play product id, or null.
    public static Pack ForProduct(string productId)
    {
        foreach (var p in Packs) if (p.productId == productId) return p;
        return null;
    }

    // The pack whose purchase is open right now, or null.
    public static Pack Buying
    {
        get { foreach (var p in Packs) if (p.Buying) return p; return null; }
    }

    // Raised when any price arrives or goes away, so the dock redraws.
    public static event Action Changed;

    // Raised after dust is paid, with the amount.
    public static event Action<int> Rewarded;

    static readonly HashSet<string> consuming = new HashSet<string>();
    // Tokens whose consume succeeded this session. The store's owned list is
    // queried on every focus return, which is also when a live purchase lands:
    // a query snapshot taken before the consume can still be delivered after
    // it, and by then the token is gone from GrantedTokens -- it must not read
    // as a fresh purchase and pay a second time.
    static readonly HashSet<string> consumed = new HashSet<string>();

    // Handles one owned or updated purchase. `consume(token, done)` asks the
    // store to consume it and reports success through `done`.
    public static Result Handle(PlayPurchase purchase, Action<string, Action<bool>> consume)
    {
        var pack = ForProduct(purchase.productId);
        if (pack == null) return Result.Ignored;
        // Not paid yet: wait for it to become Purchased.
        if (purchase.state == PlayPurchaseState.Pending) return Result.Pending;
        if (purchase.state != PlayPurchaseState.Purchased || string.IsNullOrEmpty(purchase.token)) return Result.Ignored;

        string token = purchase.token;
        if (consumed.Contains(token)) return Result.AlreadyGranted;   // a stale owned-list after the consume
        var granted = GrantedTokens();
        bool fresh = !granted.Contains(token);
        if (fresh)
        {
            // The token and the dust land in one save, so a kill can't pay without remembering it.
            granted.Add(token);
            PlayerPrefs.SetString(GrantedKey, string.Join("\n", granted));
            StarDustLedger.Grant(pack.dust);
            PrefsSaver.SaveNow();
            Debug.Log("[Support] granted " + pack.dust + " star dust");
            var handler = Rewarded;
            if (handler != null) handler(pack.dust);
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
        consumed.Add(token);
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
    public static Result Simulate(Pack pack = null)
    {
        if (!DeveloperUnlocks.Available) return Result.Ignored;
        var fake = new PlayPurchase
        {
            productId = (pack ?? Large).productId,
            token = "simulated-" + DateTime.UtcNow.Ticks,
            state = PlayPurchaseState.Purchased,
        };
        return Handle(fake, (token, done) => done(true));
    }

    // Test seam: forgets the session state.
    public static void ResetSession()
    {
        consuming.Clear();
        consumed.Clear();
        foreach (var p in Packs) p.Reset();
    }
}
