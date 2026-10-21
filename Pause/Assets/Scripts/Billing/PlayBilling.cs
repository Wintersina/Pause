using System;
using System.Collections.Concurrent;
using UnityEngine;
using UnityEngine.SceneManagement;
#if UNITY_ANDROID && !UNITY_EDITOR
using System.Collections.Generic;
#endif

// Google Play Billing, for the pre-registration reward only (Paragon's real
// money purchase is still a TODO and will share this client).
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
    bool connecting, connected;
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
        while (mainThread.TryDequeue(out work)) work();

        // Tell the player once the home screen is up (never mid-run).
        if (PreRegistrationReward.ToastDue && SceneManager.GetActiveScene().name == BackNavigator.HomeScene)
        {
            PreRegistrationReward.ToastDue = false;
            AccountHintToast.Show(PreRegistrationReward.Message);
        }
    }

    void OnApplicationFocus(bool focused)
    {
        if (focused) QueryOwned();
    }

    void OnDestroy()
    {
        if (instance == this) instance = null;
    }

#if UNITY_ANDROID && !UNITY_EDITOR
    // ---------------------------------------------------------------- Android
    void Connect()
    {
        if (connecting || connected) return;
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
            client.Call("startConnection", new StateListener(this));
        }
        catch (Exception e)
        {
            // No Play, no billing library in the build: the game does not need it.
            Debug.Log("[PreReg] billing unavailable: " + e.Message);
            connecting = false;
        }
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
                    });
                }
            }
        }
        return result;
    }

    void Deliver(List<PlayPurchase> purchases)
    {
        mainThread.Enqueue(() =>
        {
            foreach (var p in purchases) PreRegistrationReward.Handle(p, Consume);
        });
    }

    sealed class StateListener : AndroidJavaProxy
    {
        readonly PlayBilling owner;
        public StateListener(PlayBilling owner) : base("com.android.billingclient.api.BillingClientStateListener") { this.owner = owner; }

        void onBillingSetupFinished(AndroidJavaObject result)
        {
            bool ok = result.Call<int>("getResponseCode") == Ok;
            owner.mainThread.Enqueue(() =>
            {
                owner.connecting = false;
                owner.connected = ok;
                if (ok) owner.QueryOwned();
            });
        }

        void onBillingServiceDisconnected()
        {
            owner.mainThread.Enqueue(() => { owner.connecting = false; owner.connected = false; });
        }
    }

    sealed class PurchasesListener : AndroidJavaProxy
    {
        readonly PlayBilling owner;
        public PurchasesListener(PlayBilling owner) : base("com.android.billingclient.api.PurchasesUpdatedListener") { this.owner = owner; }

        void onPurchasesUpdated(AndroidJavaObject result, AndroidJavaObject purchases)
        {
            if (result.Call<int>("getResponseCode") != Ok) return;
            owner.Deliver(Read(purchases));
        }
    }

    sealed class QueryListener : AndroidJavaProxy
    {
        readonly PlayBilling owner;
        public QueryListener(PlayBilling owner) : base("com.android.billingclient.api.PurchasesResponseListener") { this.owner = owner; }

        void onQueryPurchasesResponse(AndroidJavaObject result, AndroidJavaObject purchases)
        {
            if (result.Call<int>("getResponseCode") != Ok) return;
            owner.Deliver(Read(purchases));
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
            bool ok = result.Call<int>("getResponseCode") == Ok;
            owner.mainThread.Enqueue(() => done(ok));
        }
    }
#else
    // Editor, iOS and every non-Android platform: no store to talk to.
    void Connect() { }
    void QueryOwned() { }
#endif
}
