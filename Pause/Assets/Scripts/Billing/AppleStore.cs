using System;
using System.Collections.Generic;
using UnityEngine;

// The platform-independent half of the App Store billing (AppStoreBilling.cs is the iOS-only half that talks to
// Plugins/iOS/PauseStoreKit.swift). Everything here is plain C# over the existing purchase handlers, compiled on
// every platform so EditMode tests (AppleStoreTest) cover it on CI:
//
//   AppleProducts      the one product-id table (Apple ids are IDENTICAL to the Google Play ids)
//   AppleTransaction   one StoreKit 2 transaction, reduced to what the game reads
//   AppleStoreMessages parsing of the native bridge's tab/newline payloads
//   AppleStoreLedger   transaction ids of consumables already granted AND finished (persisted)
//   AppleStoreRouter   turns native messages into grants through ParagonPurchase / RemoveAdsPurchase /
//                      SupportPurchase.Handle, finishes transactions only after the grant, reconciles entitlements

public enum AppleProductKind { Consumable, NonConsumable }

public enum AppleTxState { Purchased, Pending, Revoked }

public struct AppleTransaction
{
    public string transactionId;
    public string productId;
    public string originalId;
    public AppleTxState state;
}

public static class AppleProducts
{
    public struct Entry
    {
        public string productId;
        public AppleProductKind kind;
    }

    // THE table. Ids come from the same constants Google Play uses, so there is one id per product on both stores
    // (docs/store-products-and-rewards.md section 5.2 lists what to create in App Store Connect). Not sold on iOS:
    // the Play pre-registration reward (prereg_stardust_100), Apple has no such thing.
    public static List<Entry> All()
    {
        var list = new List<Entry>
        {
            new Entry { productId = PremiumShips.ParagonProductId, kind = AppleProductKind.NonConsumable },
            new Entry { productId = RemoveAdsPurchase.ProductId, kind = AppleProductKind.NonConsumable },
        };
        foreach (var pack in SupportPurchase.Packs)
            list.Add(new Entry { productId = pack.productId, kind = AppleProductKind.Consumable });
        return list;
    }

    public static bool TryFind(string productId, out Entry entry)
    {
        foreach (var e in All())
            if (e.productId == productId) { entry = e; return true; }
        entry = default(Entry);
        return false;
    }

    public static List<string> Ids()
    {
        var ids = new List<string>();
        foreach (var e in All()) ids.Add(e.productId);
        return ids;
    }
}

// Payloads from the native bridge: one record per line, fields separated by a tab.
public static class AppleStoreMessages
{
    public static List<string[]> Records(string payload)
    {
        var list = new List<string[]>();
        if (string.IsNullOrEmpty(payload)) return list;
        foreach (string line in payload.Split('\n'))
            if (line.Length > 0) list.Add(line.Split('\t'));
        return list;
    }

    // "<transactionId>\t<productId>\t<originalId>\t<purchased|revoked>"
    public static bool TryParseTransaction(string payload, out AppleTransaction tx)
    {
        tx = default(AppleTransaction);
        var recs = Records(payload);
        if (recs.Count != 1 || recs[0].Length < 4) return false;
        var f = recs[0];
        if (f[0].Length == 0 || f[1].Length == 0) return false;
        tx.transactionId = f[0];
        tx.productId = f[1];
        tx.originalId = f[2];
        switch (f[3])
        {
            case "purchased": tx.state = AppleTxState.Purchased; break;
            case "pending": tx.state = AppleTxState.Pending; break;
            case "revoked": tx.state = AppleTxState.Revoked; break;
            default: return false;
        }
        return true;
    }

    // "<productId>\t<displayPrice>" per line.
    public static Dictionary<string, string> ParsePrices(string payload)
    {
        var map = new Dictionary<string, string>();
        foreach (var f in Records(payload))
            if (f.Length >= 2 && f[0].Length > 0 && f[1].Length > 0) map[f[0]] = f[1];
        return map;
    }

    // "<productId>" per line.
    public static List<string> ParseIds(string payload)
    {
        var ids = new List<string>();
        foreach (var f in Records(payload)) if (f[0].Length > 0) ids.Add(f[0]);
        return ids;
    }
}

// Consumable transactions already granted and finished, newest last, persisted. Belt and braces behind
// SupportPurchase's own token record: Apple does not redeliver a finished transaction, but a redelivery (a
// restore, a replayed update) must never pay a second time. Capped so it cannot grow forever.
public static class AppleStoreLedger
{
    public const string Key = "appleFinishedTx";
    public const int Cap = 64;

    public static List<string> Finished()
    {
        var list = new List<string>();
        foreach (string t in PlayerPrefs.GetString(Key, "").Split('\n'))
            if (t.Length > 0) list.Add(t);
        return list;
    }

    public static bool WasFinished(string transactionId)
    {
        return Finished().Contains(transactionId);
    }

    public static void MarkFinished(string transactionId)
    {
        if (string.IsNullOrEmpty(transactionId)) return;
        var list = Finished();
        if (list.Contains(transactionId)) return;
        list.Add(transactionId);
        if (list.Count > Cap) list.RemoveRange(0, list.Count - Cap);
        PlayerPrefs.SetString(Key, string.Join("\n", list));
        PrefsSaver.SaveNow();
    }
}

public static class AppleStoreRouter
{
    public enum Delivery { Ignored, Pending, Granted, AlreadyGranted, Revoked }

    public const string RestoredMessage = "Purchases restored.";
    public const string NothingToRestoreMessage = "Your purchases are up to date.";
    public const string RestoreErrorMessage = "Couldn't restore purchases. Please try again later.";

    // Native seams, set by AppStoreBilling (tests set fakes). Finish must eventually call OnFinished.
    public static Action<string> NativeFinish;
    public static Action<string> NativeBuy;
    public static Action NativeRestore;
    public static Action<string> Toast = text => AccountHintToast.Show(text);

    // The Restore Purchases button is busy while true.
    public static bool Restoring { get; private set; }
    public static event Action RestoringChanged;
    static int restoreGrants;

    static readonly Dictionary<string, List<Action<bool>>> finishWaiters = new Dictionary<string, List<Action<bool>>>();

    // ---- wiring ----

    // Opens the buy flows: a row is Available once its price also arrives.
    public static void Attach()
    {
        ParagonPurchase.LaunchFlow = () => Buy(PremiumShips.ParagonProductId);
        RemoveAdsPurchase.LaunchFlow = () => Buy(RemoveAdsPurchase.ProductId);
        foreach (var pack in SupportPurchase.Packs)
        {
            var p = pack;
            p.LaunchFlow = () => Buy(p.productId);
        }
    }

    // No store (native refused to start): rows go back to UNAVAILABLE.
    public static void Detach()
    {
        ParagonPurchase.SetPrice(null);
        RemoveAdsPurchase.SetPrice(null);
        foreach (var pack in SupportPurchase.Packs) pack.SetPrice(null);
        ParagonPurchase.LaunchFlow = null;
        RemoveAdsPurchase.LaunchFlow = null;
        foreach (var pack in SupportPurchase.Packs) pack.LaunchFlow = null;
    }

    static void Buy(string productId)
    {
        if (NativeBuy == null) { EndBuy(productId); Toast(ParagonPurchase.ErrorMessage); return; }
        NativeBuy(productId);
    }

    public static void Restore()
    {
        if (Restoring || NativeRestore == null) return;
        SetRestoring(true);
        restoreGrants = 0;
        NativeRestore();
    }

    static void SetRestoring(bool on)
    {
        if (Restoring == on) return;
        Restoring = on;
        var h = RestoringChanged;
        if (h != null) h();
    }

    // ---- native messages (main thread) ----

    public static void OnMessage(string kind, string payload)
    {
        switch (kind)
        {
            case "products": OnProducts(payload); break;
            case "tx":
                AppleTransaction tx;
                if (AppleStoreMessages.TryParseTransaction(payload, out tx)) Deliver(tx, true);
                break;
            case "pending":
                foreach (var id in AppleStoreMessages.ParseIds(payload)) { PendingToast(id); EndBuy(id); }
                break;
            case "cancelled":
                foreach (var id in AppleStoreMessages.ParseIds(payload)) EndBuy(id);
                break;
            case "error": OnError(payload); break;
            case "entitlements": Reconcile(AppleStoreMessages.ParseIds(payload), true); break;
            case "finished": OnFinished(payload); break;
            case "restored":
                bool ok = payload == "ok";
                bool wasRestoring = Restoring;
                SetRestoring(false);
                if (wasRestoring) Toast(ok ? (restoreGrants > 0 ? RestoredMessage : NothingToRestoreMessage) : RestoreErrorMessage);
                break;
        }
    }

    static void OnProducts(string payload)
    {
        var prices = AppleStoreMessages.ParsePrices(payload);
        string price;
        ParagonPurchase.SetPrice(prices.TryGetValue(PremiumShips.ParagonProductId, out price) ? price : null);
        RemoveAdsPurchase.SetPrice(prices.TryGetValue(RemoveAdsPurchase.ProductId, out price) ? price : null);
        foreach (var pack in SupportPurchase.Packs)
            pack.SetPrice(prices.TryGetValue(pack.productId, out price) ? price : null);
    }

    static void OnError(string payload)
    {
        var recs = AppleStoreMessages.Records(payload);
        if (recs.Count == 0) return;
        string id = recs[0][0];
        Debug.Log("[AppStore] error " + payload.Replace('\t', ' '));
        AppleProducts.Entry e;
        if (!AppleProducts.TryFind(id, out e)) return;   // "-": a catalog-level error, nothing was buying
        if (IsBuying(id)) Toast(ParagonPurchase.ErrorMessage);
        EndBuy(id);
    }

    // ---- granting ----

    // Grants one transaction through the existing handlers, finishing it only after the grant.
    // `announce` shows the toasts (off for the silent entitlement reconcile).
    public static Delivery Deliver(AppleTransaction tx, bool announce)
    {
        AppleProducts.Entry entry;
        if (!AppleProducts.TryFind(tx.productId, out entry)) return Delivery.Ignored;   // not ours: leave it unfinished

        if (tx.state == AppleTxState.Pending)
        {
            if (announce && IsBuying(tx.productId)) PendingToast(tx.productId);
            EndBuy(tx.productId);
            return Delivery.Pending;
        }
        if (tx.state == AppleTxState.Revoked)
        {
            // Refunded: nothing is granted (and an owned non-consumable is never taken away, like Play), but the
            // transaction is finished so it stops coming back.
            RequestFinish(tx.transactionId, null);
            EndBuy(tx.productId);
            return Delivery.Revoked;
        }

        var purchase = new PlayPurchase
        {
            productId = tx.productId,
            token = tx.transactionId,
            state = PlayPurchaseState.Purchased,
            acknowledged = false,
        };
        Delivery result;
        if (entry.kind == AppleProductKind.Consumable)
        {
            var pack = SupportPurchase.ForProduct(tx.productId);
            if (AppleStoreLedger.WasFinished(tx.transactionId))
            {
                RequestFinish(tx.transactionId, null);   // finished before: a redelivery pays nothing
                result = Delivery.AlreadyGranted;
            }
            else
            {
                var r = SupportPurchase.Handle(purchase, (token, done) =>
                    RequestFinish(token, ok => { if (ok) AppleStoreLedger.MarkFinished(token); done(ok); }));
                result = r == SupportPurchase.Result.Granted ? Delivery.Granted
                       : r == SupportPurchase.Result.Pending ? Delivery.Pending
                       : r == SupportPurchase.Result.AlreadyGranted ? Delivery.AlreadyGranted : Delivery.Ignored;
                if (r == SupportPurchase.Result.Granted && announce) Toast(pack.grantedMessage);
            }
            if (pack != null) pack.EndBuy();
        }
        else if (tx.productId == PremiumShips.ParagonProductId)
        {
            var r = ParagonPurchase.Handle(purchase, t => RequestFinish(t, null));
            result = r == ParagonPurchase.Result.Granted ? Delivery.Granted
                   : r == ParagonPurchase.Result.AlreadyOwned ? Delivery.AlreadyGranted : Delivery.Ignored;
            if (r == ParagonPurchase.Result.Granted && announce) Toast(ParagonPurchase.GrantedMessage);
            ParagonPurchase.EndBuy();
        }
        else
        {
            var r = RemoveAdsPurchase.Handle(purchase, t => RequestFinish(t, null));
            result = r == RemoveAdsPurchase.Result.Granted ? Delivery.Granted
                   : r == RemoveAdsPurchase.Result.AlreadyOwned ? Delivery.AlreadyGranted : Delivery.Ignored;
            if (r == RemoveAdsPurchase.Result.Granted && announce) Toast(RemoveAdsPurchase.GrantedMessage);
            RemoveAdsPurchase.EndBuy();
        }
        if (result == Delivery.Granted && Restoring) restoreGrants++;
        return result;
    }

    // Non-consumables are re-derived from the current entitlements at every launch and on Restore: each owned
    // product is granted idempotently (no finish needed: entitlements are already-finished transactions).
    // Returns how many were new.
    public static int Reconcile(IEnumerable<string> ownedProductIds, bool announce)
    {
        int fresh = 0;
        foreach (string id in ownedProductIds)
        {
            AppleProducts.Entry e;
            if (!AppleProducts.TryFind(id, out e) || e.kind != AppleProductKind.NonConsumable) continue;
            var purchase = new PlayPurchase
            {
                productId = id,
                token = "entitlement:" + id,
                state = PlayPurchaseState.Purchased,
                acknowledged = true,
            };
            bool granted;
            if (id == PremiumShips.ParagonProductId)
            {
                granted = ParagonPurchase.Handle(purchase, null) == ParagonPurchase.Result.Granted;
                if (granted && announce) Toast(ParagonPurchase.GrantedMessage);
                if (granted) ParagonPurchase.EndBuy();
            }
            else
            {
                granted = RemoveAdsPurchase.Handle(purchase, null) == RemoveAdsPurchase.Result.Granted;
                if (granted && announce) Toast(RemoveAdsPurchase.GrantedMessage);
                if (granted) RemoveAdsPurchase.EndBuy();
            }
            if (granted) { fresh++; if (Restoring) restoreGrants++; }
        }
        return fresh;
    }

    // ---- finish bookkeeping ----

    static void RequestFinish(string transactionId, Action<bool> done)
    {
        if (done != null)
        {
            List<Action<bool>> waiters;
            if (!finishWaiters.TryGetValue(transactionId, out waiters))
                finishWaiters[transactionId] = waiters = new List<Action<bool>>();
            waiters.Add(done);
        }
        if (NativeFinish != null) NativeFinish(transactionId);
    }

    public static void OnFinished(string transactionId)
    {
        List<Action<bool>> waiters;
        if (!finishWaiters.TryGetValue(transactionId, out waiters)) return;
        finishWaiters.Remove(transactionId);
        foreach (var w in waiters) w(true);
    }

    // ---- small helpers ----

    static bool IsBuying(string productId)
    {
        if (productId == PremiumShips.ParagonProductId) return ParagonPurchase.Buying;
        if (productId == RemoveAdsPurchase.ProductId) return RemoveAdsPurchase.Buying;
        var pack = SupportPurchase.ForProduct(productId);
        return pack != null && pack.Buying;
    }

    static void EndBuy(string productId)
    {
        if (productId == PremiumShips.ParagonProductId) ParagonPurchase.EndBuy();
        else if (productId == RemoveAdsPurchase.ProductId) RemoveAdsPurchase.EndBuy();
        else { var pack = SupportPurchase.ForProduct(productId); if (pack != null) pack.EndBuy(); }
    }

    static void PendingToast(string productId)
    {
        if (productId == PremiumShips.ParagonProductId) Toast(ParagonPurchase.PendingMessage);
        else if (productId == RemoveAdsPurchase.ProductId) Toast(RemoveAdsPurchase.PendingMessage);
        else Toast(SupportPurchase.PendingMessage);
    }

    // Test seam.
    public static void ResetSession()
    {
        finishWaiters.Clear();
        Restoring = false;
        restoreGrants = 0;
        NativeFinish = null; NativeBuy = null; NativeRestore = null;
        Toast = text => AccountHintToast.Show(text);
    }
}
