using System;
using UnityEngine;

// The game-facing ad API. Everything is a no-op (and never touches an SDK) while
// AdsConfig.Enabled is false. The hooks the game calls:
//
//   AdManager.OnRunStart()                  score.Start, real runs
//   AdManager.OnRunEnd(died)                death panel / leaving a run
//   AdManager.TryRunEndAd(proceed)          playerIsDead.BeginResults: true = an interstitial took over (else banner / none)
//   AdManager.TryReplayAd(proceed)          buttonClicks.replay: the 3rd results Replay since the last forced ad shows one
//   AdManager.TryShowRewardedDouble(...)    the death panel's double dust button
//   AdManager.PrivacyOptionsRequired / ShowPrivacyOptions   the Options entry
public static class AdManager
{
    // Test seam: install a controller built on a fake provider.
    public static AdController Controller { get; private set; }
    static AdRunner runner;

    // The live policy inputs (everything except the two the controller tracks itself).
    public static AdPolicy.Inputs LiveInputs()
    {
        var i = new AdPolicy.Inputs();
        i.removeAdsOwned = AdFree.IsActive;
        // Developer mode unlocks every ship, so ownership means nothing there: ignore it and skip the ad-free window,
        // so testers see ads. (Release builds can never be in developer mode.)
        bool dev = DeveloperUnlocks.Enabled;
        i.paragonOwned = !dev && ShipId.IsOwned(PremiumShips.Paragon);
        i.playSeconds = dev ? AdPolicy.AdFreePlaySeconds : PlayTime.Seconds;
        i.boosterActive = AdPolicy.BoosterActive;
        i.runSeconds = RunSeconds();
        i.tutorialOrPractice = !score.paysRealDust || !RunScore.Scoring;
        return i;
    }

    static float runStartPlay = -1f;
    static float RunSeconds() { return runStartPlay < 0f ? 0f : Mathf.Max(0f, PlayTime.Seconds - runStartPlay); }

    public static void OnRunStart()
    {
        runStartPlay = PlayTime.Seconds;
        if (EnsureBooted()) Controller.OnRunStart();
    }

    public static void OnRunEnd(bool died)
    {
        if (Controller != null) Controller.OnRunEnd(died);
    }

    public static void OnDeathPanel()
    {
        if (EnsureBooted()) Controller.OnDeathPanel();
    }

    public static bool ShowInProgress { get { return Controller != null && Controller.Busy; } }

    // The end of a real run, before the results modal. True when a full interstitial took over: it auto-plays and
    // `proceed` (build the modal) runs when it ends. False: the caller proceeds itself (a banner may now be up under the
    // modal -- see BannerHeightPx -- or nothing).
    public static bool TryRunEndAd(Action proceed)
    {
        if (!EnsureBooted()) return false;
        return Controller.TryRunEndAd(LiveInputs(), proceed) == AdPolicy.RunEndAd.Interstitial;
    }

    // Every Replay press (buttonClicks.replay): true when a forced interstitial took over; `proceed` starts the new run after it.
    public static bool TryReplayAd(Action proceed)
    {
        if (!EnsureBooted()) return false;
        return Controller.TryReplayAd(LiveInputs(), proceed);
    }

    // A banner on a menu screen (Credits), under the same rules as the run-end banner (Remove Ads, Paragon, the first ad-free
    // minutes; developer mode skips the window). True when it is up. The screen hides it again (HideBanner) when it closes.
    public static bool TryShowMenuBanner() { return TryShowMenuBanner(LiveInputs()); }

    public static bool TryShowMenuBanner(AdPolicy.Inputs inputs)
    {
        if (!EnsureBooted() || Controller.Busy || !AdPolicy.MenuBannerAllowed(inputs)) return false;
        Controller.ShowBanner();
        return Controller.BannerShowing;
    }

    public static void HideBanner() { if (Controller != null) Controller.HideBanner(); }
    public static bool BannerShowing { get { return Controller != null && Controller.BannerShowing; } }

    // Screen pixels the banner covers at the bottom of the screen (0 when none is up): the modal lays out above it.
    public static float BannerHeightPx { get { return BannerShowing ? AdsConfig.BannerHeightPx(ScreenInfo.Dpi) : 0f; } }

    // Canvas units a screen must keep clear at its bottom for the banner (0 when none is up); `sf` is the canvas scale factor.
    public static float BannerReserveUnits(float sf)
    {
        float px = BannerHeightPx;
        if (!(px > 0f)) return 0f;
        return DeathPanelView.ReserveBottomForBanner(new Rect(0f, 0f, 10000f, 10000f), px / Mathf.Max(sf, .0001f)).yMin;
    }

    // The Shop's "watch an ad: +50 star dust" row: always on (not an AdPolicy matter), needs only a loaded rewarded ad.
    public static bool ShopAdEnabled { get { return AdsConfig.Enabled; } }
    public static bool ShopAdReady { get { return EnsureBooted() && Controller.ShopAdReady; } }
    public static bool TryShowShopReward(Action<float> onPaid, Action onDone)
    {
        return EnsureBooted() && Controller.TryShowShopReward(onPaid, onDone);
    }

    public static bool RewardedReady { get { return EnsureBooted() && Controller.RewardedReady && !Controller.Busy; } }

    public static bool TryShowRewardedDouble(long token, float earned, Action<float> onPaid, Action onDone)
    {
        return EnsureBooted() && Controller.TryShowRewarded(token, earned, onPaid, onDone);
    }

    public static bool PrivacyOptionsRequired { get { return Controller != null && Controller.PrivacyOptionsRequired; } }

    public static void ShowPrivacyOptions(Action done)
    {
        if (Controller != null) Controller.ShowPrivacyOptions(done); else if (done != null) done();
    }

    // Creates the controller (once) when ads are enabled. Never when they are not.
    public static bool EnsureBooted()
    {
        if (!AdsConfig.Enabled) return false;
        if (Controller == null) Install(new AdController(AdProviders.Create(), () => Time.realtimeSinceStartup,
                                                          () => AdsConfig.Enabled, new AdEnvironment(), new PrefsRewardStore()));
        return true;
    }

    public static void Install(AdController controller)
    {
        Controller = controller;
        if (Application.isPlaying && runner == null)
        {
            var go = new GameObject("~Ads") { hideFlags = HideFlags.HideInHierarchy };
            UnityEngine.Object.DontDestroyOnLoad(go);
            runner = go.AddComponent<AdRunner>();
        }
        controller.Boot();
    }

    // Test hook.
    public static void Reset() { Controller = null; runStartPlay = -1f; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot() { EnsureBooted(); }
}

public sealed class AdRunner : MonoBehaviour
{
    void OnEnable() { UnityEngine.SceneManagement.SceneManager.sceneLoaded += OnSceneLoaded; }
    void OnDisable() { UnityEngine.SceneManagement.SceneManager.sceneLoaded -= OnSceneLoaded; }

    // Leaving the run scenes (menu, dock...) lets ad loads happen again.
    static void OnSceneLoaded(UnityEngine.SceneManagement.Scene scene, UnityEngine.SceneManagement.LoadSceneMode mode)
    {
        var c = AdManager.Controller;
        if (c == null) return;
        c.HideBanner();   // a banner never outlives its modal: any scene change takes it down
        if (scene.name != "gameS1" && scene.name != score.TutorialScene) c.OnRunEnd(false);
    }

    void Update()
    {
        var c = AdManager.Controller;
        if (c != null) c.Tick();
        AdTracking.Tick(Time.realtimeSinceStartup);
    }
}

public static class AdProviders
{
    public static IAdProvider Create()
    {
#if (UNITY_ANDROID || UNITY_IOS) && !UNITY_EDITOR
        return new AdMobProvider();
#else
        return new StubAdProvider();
#endif
    }
}

// Mute the game's audio and freeze time while an ad is up, then put both back exactly as they were.
public sealed class AdEnvironment : IAdEnvironment
{
    bool suspended, prevPause;
    float prevVolume, prevScale;

    public void Suspend()
    {
        if (suspended) return;
        suspended = true;
        prevPause = AudioListener.pause;
        prevVolume = AudioListener.volume;
        prevScale = Time.timeScale;
        AudioListener.pause = true;
        AudioListener.volume = 0f;
        Time.timeScale = 0f;
    }

    public void Resume()
    {
        if (!suspended) return;
        suspended = false;
        AudioListener.pause = prevPause;
        AudioListener.volume = prevVolume;
        Time.timeScale = prevScale;
    }
}

// The reward store on PlayerPrefs: the paid token and the credited dust go out in one save.
public sealed class PrefsRewardStore : IRewardStore
{
    // A fresh token for a death panel: persisted and strictly increasing.
    public static long NewDeathToken()
    {
        long counter;
        if (!long.TryParse(PlayerPrefs.GetString(DoubleDustRules.DeathCounterKey, "0"), out counter)) counter = 0L;
        long token = DoubleDustRules.NextToken(counter, new PrefsRewardStore().LastPaidToken);
        PlayerPrefs.SetString(DoubleDustRules.DeathCounterKey, token.ToString());
        PrefsSaver.MarkDirty();
        return token;
    }

    public long LastPaidToken
    {
        get
        {
            long v;
            return long.TryParse(PlayerPrefs.GetString(DoubleDustRules.PaidTokenKey, "0"), out v) ? v : 0L;
        }
    }

    public void Grant(float amount)
    {
        StarDustLedger.Grant(amount);
        PrefsSaver.SaveNow();
    }

    public void Pay(long token, float amount)
    {
        PlayerPrefs.SetString(DoubleDustRules.PaidTokenKey, token.ToString());
        StarDustLedger.Grant(amount);
        PrefsSaver.SaveNow();
    }
}
