using System;
using UnityEngine;

// The game-facing ad API. Everything is a no-op (and never touches an SDK) while
// AdsConfig.Enabled is false. The hooks the game calls:
//
//   AdManager.OnRunStart()                  score.Start, real runs
//   AdManager.OnRunEnd(died)                death panel / leaving a run
//   AdManager.TryShowBeforeReplay(proceed)  buttonClicks.replay: true = an interstitial took over
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

    public static bool TryShowBeforeReplay(Action proceed)
    {
        if (!EnsureBooted() || !buttonClicks.playerDied) return false;   // only after the death screen
        return Controller.TryShowInterstitial(LiveInputs(), proceed);
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
        if (c != null && scene.name != "gameS1" && scene.name != score.TutorialScene) c.OnRunEnd(false);
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

    public void Pay(long token, float amount)
    {
        PlayerPrefs.SetString(DoubleDustRules.PaidTokenKey, token.ToString());
        StarDustLedger.Grant(amount);
        PrefsSaver.SaveNow();
    }
}
