using System;
using System.Collections.Concurrent;
using UnityEngine;
using UnityEngine.SceneManagement;
using System.Collections.Generic;

// Google Play Billing: the pre-registration reward (PreRegistrationReward) and
// the real-money Paragon ship (ParagonPurchase) share this client.
//
// Android drives the Play Billing library (com.android.billingclient:billing,
// added in Assets/Editor/PlayBillingDependencies.xml and the Gradle template)
// through JNI; there is no Unity IAP package. Everywhere else -- the editor,
// iOS, a device without Play -- nothing starts and the game plays on. The
// reward is queried at launch and on every return to the app (a promo code or
// the pre-registration delivery is redeemed outside the game), and arrives live
// through the purchases-updated callback. Store callbacks come in on Android's
// threads, so they are queued and handled on Unity's main thread.
public class PlayBilling : MonoBehaviour
{
    static PlayBilling instance;

    readonly ConcurrentQueue<Action> mainThread = new ConcurrentQueue<Action>();

#if UNITY_ANDROID && !UNITY_EDITOR
    const int Ok = 0;
    AndroidJavaObject client;
    AndroidJavaObject paragonDetails;
    readonly Dictionary<string, AndroidJavaObject> supportDetails = new Dictionary<string, AndroidJavaObject>();
    bool connecting, connected;
    float connectStartedAt;
    const float ConnectStaleSeconds = 30f;   // a startConnection that never called back must not block reconnects
#endif

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        if (instance != null) return;
        var go = new GameObject("~PlayBilling");
        go.hideFlags = HideFlags.HideInHierarchy;
        DontDestroyOnLoad(go);
        instance = go.AddComponent<PlayBilling>();
        instance.Connect();
    }

    void Update()
    {
        Action work;
        while (mainThread.TryDequeue(out work))
        {
            // One delivery that throws (a toast, a handler) must not drop the
            // ones queued behind it; they would wait for the next focus return.
            try { work(); }
            catch (Exception e) { Debug.LogException(e); }
        }

        // Tell the player once the home screen is up (never mid-run).
        if (PreRegistrationReward.ToastDue && SceneManager.GetActiveScene().name == BackNavigator.HomeScene)
        {
            PreRegistrationReward.ToastDue = false;
            AccountHintToast.Show(PreRegistrationReward.Message);
        }
    }

    static bool AnySupportPriceMissing()
    {
        foreach (var p in SupportPurchase.Packs) if (string.IsNullOrEmpty(p.Price)) return true;
        return false;
    }

    void OnApplicationFocus(bool focused)
    {
        if (!focused) return;
        QueryOwned();
        if (string.IsNullOrEmpty(ParagonPurchase.Price) || AnySupportPriceMissing()) QueryParagonDetails();
    }

    void OnDestroy()
    {
        if (instance == this) instance = null;
        ReleaseClient();
    }

#if UNITY_ANDROID && !UNITY_EDITOR
    // ---------------------------------------------------------------- Android
    void Connect()
    {
        if (connected) return;
        if (connecting && Time.realtimeSinceStartup - connectStartedAt < ConnectStaleSeconds) return;
        ReleaseClient();
        try
        {
            using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
            using (var activity = player.GetStatic<AndroidJavaObject>("currentActivity"))
            using (var pending = new AndroidJavaClass("com.android.billingclient.api.PendingPurchasesParams"))
            using (var pendingBuilder = pending.CallStatic<AndroidJavaObject>("newBuilder"))
            {
                pendingBuilder.Call<AndroidJavaObject>("enableOneTimeProducts");
                using (var pendingParams = pendingBuilder.Call<AndroidJavaObject>("build"))
                using (var cls = new AndroidJavaClass("com.android.billingclient.api.BillingClient"))
                using (var builder = cls.CallStatic<AndroidJavaObject>("newBuilder", activity))
                {
                    builder.Call<AndroidJavaObject>("setListener", new PurchasesListener(this));
                    builder.Call<AndroidJavaObject>("enablePendingPurchases", pendingParams);
                    client = builder.Call<AndroidJavaObject>("build");
                }
            }
            connecting = true;
            connectStartedAt = Time.realtimeSinceStartup;
            ParagonPurchase.LaunchFlow = LaunchParagon;
            foreach (var pack in SupportPurchase.Packs)
            {
                var p = pack;
                p.LaunchFlow = () => LaunchSupport(p);
            }
            client.Call("startConnection", new StateListener(this));
        }
        catch (Exception e)
        {
            // No Play, no billing library in the build: the game does not need it.
            Debug.Log("[PreReg] billing unavailable: " + e.Message);
            connecting = false;
        }
    }

    // Ends the old client's connection and frees it and the details that belong
    // to it. Main thread only (Connect, OnDestroy): LaunchFlowFor uses the
    // details synchronously inside one main-thread call, so nothing can be
    // mid-flow while this runs.
    void ReleaseClient()
    {
        if (client != null)
        {
            try { client.Call("endConnection"); } catch (Exception) { }
            try { client.Dispose(); } catch (Exception) { }
            client = null;
        }
        connected = false;
        DisposeQuiet(paragonDetails);
        paragonDetails = null;
        foreach (var kv in supportDetails) DisposeQuiet(kv.Value);
        supportDetails.Clear();
    }

    static void DisposeQuiet(AndroidJavaObject o)
    {
        try { if (o != null) o.Dispose(); } catch (Exception) { }
    }

    void QueryOwned()
    {
        if (!connected || client == null) { Connect(); return; }
        try
        {
            using (var cls = new AndroidJavaClass("com.android.billingclient.api.QueryPurchasesParams"))
            using (var builder = cls.CallStatic<AndroidJavaObject>("newBuilder"))
            {
                builder.Call<AndroidJavaObject>("setProductType", "inapp");
                using (var query = builder.Call<AndroidJavaObject>("build"))
                    client.Call("queryPurchasesAsync", query, new QueryListener(this));
            }
        }
        catch (Exception e) { Debug.Log("[PreReg] query failed: " + e.Message); }
    }

    // Asks Play for the details of every product the game sells; the formatted
    // prices are what the dock shows.
    void QueryParagonDetails()
    {
        if (!connected || client == null) return;
        try
        {
            using (var list = new AndroidJavaObject("java.util.ArrayList"))
            using (var cls = new AndroidJavaClass("com.android.billingclient.api.QueryProductDetailsParams"))
            using (var builder = cls.CallStatic<AndroidJavaObject>("newBuilder"))
            {
                foreach (string id in ProductIds())
                using (var product = new AndroidJavaClass("com.android.billingclient.api.QueryProductDetailsParams$Product"))
                using (var pb = product.CallStatic<AndroidJavaObject>("newBuilder"))
                {
                    pb.Call<AndroidJavaObject>("setProductId", id);
                    pb.Call<AndroidJavaObject>("setProductType", "inapp");
                    using (var one = pb.Call<AndroidJavaObject>("build"))
                        list.Call<bool>("add", one);
                }
                builder.Call<AndroidJavaObject>("setProductList", list);
                using (var query = builder.Call<AndroidJavaObject>("build"))
                    client.Call("queryProductDetailsAsync", query, new DetailsListener(this));
            }
        }
        catch (Exception e) { Debug.Log("[Paragon] details query failed: " + e.Message); }
    }

    // The dock's buy tap: opens Play's purchase sheet for Paragon.
    void LaunchParagon()
    {
        try
        {
            if (!connected || client == null || paragonDetails == null) { FailParagon(); return; }
            LaunchFlowFor(paragonDetails, OnFlowResult);
        }
        catch (Exception e)
        {
            Debug.Log("[Paragon] launch failed: " + e.Message);
            FailParagon();
        }
    }

    // Every product the game sells: Paragon plus each support pack.
    static List<string> ProductIds()
    {
        var ids = new List<string> { PremiumShips.ParagonProductId };
        foreach (var p in SupportPurchase.Packs) ids.Add(p.productId);
        return ids;
    }

    // A dock tile's buy tap: opens Play's purchase sheet for that consumable pack.
    void LaunchSupport(SupportPurchase.Pack pack)
    {
        try
        {
            AndroidJavaObject details;
            if (!connected || client == null || !supportDetails.TryGetValue(pack.productId, out details) || details == null) { FailSupport(pack); return; }
            LaunchFlowFor(details, OnSupportFlowResult);
        }
        catch (Exception e)
        {
            Debug.Log("[Support] launch failed: " + e.Message);
            FailSupport(pack);
        }
    }

    void LaunchFlowFor(AndroidJavaObject details, Action<int> onResult)
    {
        using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
        using (var activity = player.GetStatic<AndroidJavaObject>("currentActivity"))
        using (var pcls = new AndroidJavaClass("com.android.billingclient.api.BillingFlowParams$ProductDetailsParams"))
        using (var pb = pcls.CallStatic<AndroidJavaObject>("newBuilder"))
        {
            pb.Call<AndroidJavaObject>("setProductDetails", details);
            using (var one = pb.Call<AndroidJavaObject>("build"))
            using (var list = new AndroidJavaObject("java.util.ArrayList"))
            using (var cls = new AndroidJavaClass("com.android.billingclient.api.BillingFlowParams"))
            using (var builder = cls.CallStatic<AndroidJavaObject>("newBuilder"))
            {
                list.Call<bool>("add", one);
                builder.Call<AndroidJavaObject>("setProductDetailsParamsList", list);
                using (var flow = builder.Call<AndroidJavaObject>("build"))
                using (var result = client.Call<AndroidJavaObject>("launchBillingFlow", activity, flow))
                    onResult(result.Call<int>("getResponseCode"));
            }
        }
    }

    void Acknowledge(string token)
    {
        try
        {
            using (var cls = new AndroidJavaClass("com.android.billingclient.api.AcknowledgePurchaseParams"))
            using (var builder = cls.CallStatic<AndroidJavaObject>("newBuilder"))
            {
                builder.Call<AndroidJavaObject>("setPurchaseToken", token);
                using (var p = builder.Call<AndroidJavaObject>("build"))
                    client.Call("acknowledgePurchase", p, new AcknowledgeListener(this, token));
            }
        }
        catch (Exception e)
        {
            Debug.Log("[Paragon] acknowledge call failed: " + e.Message);
            ParagonPurchase.OnAcknowledged(token, false);
        }
    }

    // A billing response to the Paragon flow (launch result or purchases-updated), main thread.
    void OnFlowResult(int code)
    {
        switch (ParagonPurchase.Classify(code))
        {
            case ParagonPurchase.Outcome.Ok: return;   // the purchases-updated callback delivers the purchase
            case ParagonPurchase.Outcome.Cancelled: ParagonPurchase.EndBuy(); return;
            case ParagonPurchase.Outcome.AlreadyOwned: ParagonPurchase.EndBuy(); QueryOwned(); return;
            default: FailParagon(); return;
        }
    }

    // A billing response to the support flow (launch result or purchases-updated), main thread.
    // Applies to the pack whose purchase is open.
    void OnSupportFlowResult(int code)
    {
        var pack = SupportPurchase.Buying;
        if (pack == null) return;
        switch (ParagonPurchase.Classify(code))
        {
            case ParagonPurchase.Outcome.Ok: return;   // the purchases-updated callback delivers the purchase
            case ParagonPurchase.Outcome.Cancelled: pack.EndBuy(); return;
            case ParagonPurchase.Outcome.AlreadyOwned: pack.EndBuy(); QueryOwned(); return;   // an unconsumed one: pays and consumes it
            default: FailSupport(pack); return;
        }
    }

    void FailSupport(SupportPurchase.Pack pack)
    {
        pack.EndBuy();
        AccountHintToast.Show(SupportPurchase.ErrorMessage);
        if (!connected) Connect();
    }

    void FailParagon()
    {
        ParagonPurchase.EndBuy();
        AccountHintToast.Show(ParagonPurchase.ErrorMessage);
        if (!connected) Connect();
    }

    void Consume(string token, Action<bool> done)
    {
        try
        {
            using (var cls = new AndroidJavaClass("com.android.billingclient.api.ConsumeParams"))
            using (var builder = cls.CallStatic<AndroidJavaObject>("newBuilder"))
            {
                builder.Call<AndroidJavaObject>("setPurchaseToken", token);
                using (var p = builder.Call<AndroidJavaObject>("build"))
                    client.Call("consumeAsync", p, new ConsumeListener(this, done));
            }
        }
        catch (Exception e)
        {
            Debug.Log("[PreReg] consume call failed: " + e.Message);
            done(false);
        }
    }

    // Reads a java.util.List<Purchase> into plain structs. Runs on the callback thread.
    static List<PlayPurchase> Read(AndroidJavaObject list)
    {
        var result = new List<PlayPurchase>();
        if (list == null) return result;
        int n = list.Call<int>("size");
        for (int i = 0; i < n; i++)
        {
            using (var p = list.Call<AndroidJavaObject>("get", i))
            using (var products = p.Call<AndroidJavaObject>("getProducts"))
            {
                int count = products.Call<int>("size");
                for (int k = 0; k < count; k++)
                {
                    result.Add(new PlayPurchase
                    {
                        productId = products.Call<string>("get", k),
                        token = p.Call<string>("getPurchaseToken"),
                        state = (PlayPurchaseState)p.Call<int>("getPurchaseState"),
                        acknowledged = p.Call<bool>("isAcknowledged"),
                    });
                }
            }
        }
        return result;
    }

    void Deliver(List<PlayPurchase> purchases, bool fromUpdate)
    {
        mainThread.Enqueue(() =>
        {
            bool buying = ParagonPurchase.Buying, supporting = SupportPurchase.Buying != null;
            foreach (var p in purchases)
            {
                PreRegistrationReward.Handle(p, Consume);
                var pack = SupportPurchase.ForProduct(p.productId);
                var sr = SupportPurchase.Handle(p, Consume);
                if (sr == SupportPurchase.Result.Granted) AccountHintToast.Show(pack.grantedMessage);
                else if (sr == SupportPurchase.Result.Pending && supporting) AccountHintToast.Show(SupportPurchase.PendingMessage);
                if (sr != SupportPurchase.Result.Ignored) pack.EndBuy();
                var r = ParagonPurchase.Handle(p, Acknowledge);
                if (r == ParagonPurchase.Result.Granted) AccountHintToast.Show(ParagonPurchase.GrantedMessage);
                else if (r == ParagonPurchase.Result.Pending && buying) AccountHintToast.Show(ParagonPurchase.PendingMessage);
                if (r != ParagonPurchase.Result.Ignored) ParagonPurchase.EndBuy();
            }
            // The live purchases-updated callback came back OK with nothing for
            // the open sheet (empty list, other products): release the buy button.
            // An owned-list query must not cancel a sheet that is still open.
            if (fromUpdate)
            {
                var open = SupportPurchase.Buying;
                if (open != null)
                {
                    bool matched = false;
                    foreach (var p in purchases) if (p.productId == open.productId) matched = true;
                    if (!matched) open.EndBuy();
                }
                if (ParagonPurchase.Buying)
                {
                    bool matched = false;
                    foreach (var p in purchases) if (p.productId == PremiumShips.ParagonProductId) matched = true;
                    if (!matched) ParagonPurchase.EndBuy();
                }
            }
        });
    }

    sealed class StateListener : AndroidJavaProxy
    {
        readonly PlayBilling owner;
        public StateListener(PlayBilling owner) : base("com.android.billingclient.api.BillingClientStateListener") { this.owner = owner; }

        void onBillingSetupFinished(AndroidJavaObject result)
        {
            bool ok;
            try { ok = result.Call<int>("getResponseCode") == Ok; }
            catch (Exception e) { Debug.Log("[PreReg] setup result read failed: " + e.Message); ok = false; }
            owner.mainThread.Enqueue(() =>
            {
                owner.connecting = false;
                owner.connected = ok;
                if (ok) { owner.QueryOwned(); owner.QueryParagonDetails(); }
            });
        }

        void onBillingServiceDisconnected()
        {
            owner.mainThread.Enqueue(() =>
            {
                owner.connecting = false;
                owner.connected = false;
                ParagonPurchase.SetPrice(null);   // no store: the dock reads COMING SOON again
                foreach (var pack in SupportPurchase.Packs) pack.SetPrice(null);
            });
        }
    }

    sealed class PurchasesListener : AndroidJavaProxy
    {
        readonly PlayBilling owner;
        public PurchasesListener(PlayBilling owner) : base("com.android.billingclient.api.PurchasesUpdatedListener") { this.owner = owner; }

        // Play's thread. A C# exception here is rethrown into Java and would
        // take the process down, so every proxy callback only reads and queues.
        void onPurchasesUpdated(AndroidJavaObject result, AndroidJavaObject purchases)
        {
            try
            {
                int code = result.Call<int>("getResponseCode");
                if (code != Ok)
                {
                    owner.mainThread.Enqueue(() =>
                    {
                        if (ParagonPurchase.Buying) owner.OnFlowResult(code);
                        if (SupportPurchase.Buying != null) owner.OnSupportFlowResult(code);
                    });
                    return;
                }
                owner.Deliver(Read(purchases), true);
            }
            catch (Exception e) { Debug.Log("[PreReg] purchases-updated read failed: " + e.Message); }
        }
    }

    sealed class QueryListener : AndroidJavaProxy
    {
        readonly PlayBilling owner;
        public QueryListener(PlayBilling owner) : base("com.android.billingclient.api.PurchasesResponseListener") { this.owner = owner; }

        void onQueryPurchasesResponse(AndroidJavaObject result, AndroidJavaObject purchases)
        {
            try
            {
                if (result.Call<int>("getResponseCode") != Ok) return;
                owner.Deliver(Read(purchases), false);
            }
            catch (Exception e) { Debug.Log("[PreReg] owned-list read failed: " + e.Message); }
        }
    }

    sealed class DetailsListener : AndroidJavaProxy
    {
        readonly PlayBilling owner;
        public DetailsListener(PlayBilling owner) : base("com.android.billingclient.api.ProductDetailsResponseListener") { this.owner = owner; }

        // Billing 8: the details arrive inside a QueryProductDetailsResult.
        void onProductDetailsResponse(AndroidJavaObject result, AndroidJavaObject queryResult)
        {
            AndroidJavaObject paragon = null;
            string paragonPrice = null;
            var support = new Dictionary<string, AndroidJavaObject>();
            var supportPrice = new Dictionary<string, string>();
            try
            {
                if (result.Call<int>("getResponseCode") == Ok)
                {
                    using (var list = queryResult.Call<AndroidJavaObject>("getProductDetailsList"))
                    {
                        int n = list.Call<int>("size");
                        for (int i = 0; i < n; i++)
                        {
                            var d = list.Call<AndroidJavaObject>("get", i);
                            string id = d.Call<string>("getProductId");
                            bool isParagon = id == PremiumShips.ParagonProductId, isSupport = SupportPurchase.ForProduct(id) != null;
                            if (!isParagon && !isSupport) { d.Dispose(); continue; }
                            string price;
                            using (var offer = d.Call<AndroidJavaObject>("getOneTimePurchaseOfferDetails"))
                            {
                                if (offer == null) { d.Dispose(); continue; }
                                price = offer.Call<string>("getFormattedPrice");
                            }
                            if (isParagon) { paragon = d; paragonPrice = price; }
                            else { support[id] = d; supportPrice[id] = price; }
                        }
                    }
                }
            }
            catch (Exception e)
            {
                Debug.Log("[Paragon] details read failed: " + e.Message);
                DisposeQuiet(paragon);
                foreach (var kv in support) DisposeQuiet(kv.Value);
                paragon = null; paragonPrice = null; support.Clear(); supportPrice.Clear();
            }
            owner.mainThread.Enqueue(() =>
            {
                // Replaced details are freed here, on the main thread. LaunchFlowFor
                // uses details synchronously inside a single main-thread call, so no
                // in-flight launchBillingFlow can still be holding the old ones.
                DisposeQuiet(owner.paragonDetails);
                owner.paragonDetails = paragon;
                foreach (var kv in owner.supportDetails) DisposeQuiet(kv.Value);
                owner.supportDetails.Clear();
                foreach (var kv in support) owner.supportDetails[kv.Key] = kv.Value;
                ParagonPurchase.SetPrice(paragon != null ? paragonPrice : null);
                foreach (var pack in SupportPurchase.Packs)
                {
                    string price;
                    pack.SetPrice(support.ContainsKey(pack.productId) && supportPrice.TryGetValue(pack.productId, out price) ? price : null);
                }
            });
        }
    }

    sealed class AcknowledgeListener : AndroidJavaProxy
    {
        readonly PlayBilling owner;
        readonly string token;
        public AcknowledgeListener(PlayBilling owner, string token) : base("com.android.billingclient.api.AcknowledgePurchaseResponseListener")
        {
            this.owner = owner;
            this.token = token;
        }

        void onAcknowledgePurchaseResponse(AndroidJavaObject result)
        {
            bool ok;
            try { ok = result.Call<int>("getResponseCode") == Ok; }
            catch (Exception e) { Debug.Log("[Paragon] acknowledge result read failed: " + e.Message); ok = false; }
            owner.mainThread.Enqueue(() => ParagonPurchase.OnAcknowledged(token, ok));
        }
    }

    sealed class ConsumeListener : AndroidJavaProxy
    {
        readonly PlayBilling owner;
        readonly Action<bool> done;
        public ConsumeListener(PlayBilling owner, Action<bool> done) : base("com.android.billingclient.api.ConsumeResponseListener")
        {
            this.owner = owner;
            this.done = done;
        }

        void onConsumeResponse(AndroidJavaObject result, string token)
        {
            bool ok;
            try { ok = result.Call<int>("getResponseCode") == Ok; }
            catch (Exception e) { Debug.Log("[PreReg] consume result read failed: " + e.Message); ok = false; }
            owner.mainThread.Enqueue(() => done(ok));
        }
    }
#else
    // Editor, iOS and every non-Android platform: no store to talk to.
    void Connect() { }
    void QueryOwned() { }
    void QueryParagonDetails() { }
    void ReleaseClient() { }
#endif
}
