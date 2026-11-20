using System;
using System.Collections.Concurrent;
using UnityEngine;
#if UNITY_IOS && !UNITY_EDITOR
using System.Runtime.InteropServices;
using AOT;
#endif

// App Store (StoreKit 2) billing: the iOS counterpart of PlayBilling. Same shape: it loads the products, fills the
// Price / LaunchFlow of the existing purchase classes (so the Shop rows become buyable by themselves), and feeds
// every transaction into their Handle methods. All decisions live in AppleStoreRouter (plain C#, unit tested); this
// MonoBehaviour only owns the native bridge (Plugins/iOS/PauseStoreKit.swift) and moves its messages onto Unity's
// main thread.
//
//   * Launch: init (starts Transaction.updates, redelivers unfinished transactions, reports the current
//     entitlements) and load the products. Non-consumables (Paragon, Remove Ads) are re-derived from the current
//     entitlements every time; consumables (star dust packs) are paid once per transaction id and finished only
//     after the dust was saved.
//   * Restore Purchases (RestoreAction) calls AppStore.sync() and re-reads the entitlements. Apple review needs
//     a visible one; the Shop shows it on iOS only (ShopCatalog.RestoreVisible).
//   * Everywhere else (editor, Android) this component does nothing and Supported is false.
public class AppStoreBilling : MonoBehaviour
{
    // True only in an iOS player.
    public static bool Supported
    {
        get
        {
#if UNITY_IOS && !UNITY_EDITOR
            return true;
#else
            return false;
#endif
        }
    }

    static AppStoreBilling instance;
    readonly ConcurrentQueue<Action> mainThread = new ConcurrentQueue<Action>();

    // The Shop's RESTORE PURCHASES button.
    public static void RestoreAction() { AppleStoreRouter.Restore(); }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        if (!Supported || instance != null) return;
        var go = new GameObject("~AppStoreBilling");
        go.hideFlags = HideFlags.HideInHierarchy;
        DontDestroyOnLoad(go);
        instance = go.AddComponent<AppStoreBilling>();
        instance.Connect();
    }

    void Update()
    {
        Action work;
        while (mainThread.TryDequeue(out work))
        {
            try { work(); }
            catch (Exception e) { Debug.Log("[AppStore] handler failed: " + e.Message); }
        }
    }

#if UNITY_IOS && !UNITY_EDITOR
    delegate void NativeCallback(IntPtr kind, IntPtr payload);

    [DllImport("__Internal")] static extern void PauseStore_Init(NativeCallback callback);
    [DllImport("__Internal")] static extern void PauseStore_LoadProducts(string idsCsv);
    [DllImport("__Internal")] static extern void PauseStore_Buy(string productId);
    [DllImport("__Internal")] static extern void PauseStore_Finish(string transactionId);
    [DllImport("__Internal")] static extern void PauseStore_QueryEntitlements();
    [DllImport("__Internal")] static extern void PauseStore_Restore();

    void Connect()
    {
        try
        {
            AppleStoreRouter.NativeBuy = PauseStore_Buy;
            AppleStoreRouter.NativeFinish = PauseStore_Finish;
            AppleStoreRouter.NativeRestore = PauseStore_Restore;
            AppleStoreRouter.Attach();
            PauseStore_Init(OnNative);
            PauseStore_LoadProducts(string.Join(",", AppleProducts.Ids()));
        }
        catch (Exception e)
        {
            Debug.Log("[AppStore] init failed: " + e.Message);
            AppleStoreRouter.Detach();
        }
    }

    // Returning to the app re-reads the entitlements (a refund, a Family Sharing change, an Ask to Buy approval).
    void OnApplicationFocus(bool focused)
    {
        if (!focused || instance != this) return;
        try { PauseStore_QueryEntitlements(); }
        catch (Exception e) { Debug.Log("[AppStore] entitlement query failed: " + e.Message); }
    }

    [MonoPInvokeCallback(typeof(NativeCallback))]
    static void OnNative(IntPtr kindPtr, IntPtr payloadPtr)
    {
        string kind = Marshal.PtrToStringUTF8(kindPtr);
        string payload = payloadPtr == IntPtr.Zero ? "" : Marshal.PtrToStringUTF8(payloadPtr);
        var self = instance;
        if (self == null) return;
        self.mainThread.Enqueue(() => AppleStoreRouter.OnMessage(kind, payload));
    }
#else
    void Connect() { }
#endif
}
