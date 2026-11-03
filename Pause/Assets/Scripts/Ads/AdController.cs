using System;
using UnityEngine;

// The ad flow, with no SDK and no Unity scene dependency (the provider, clock, environment and
// reward store are injected), so every path is tested: boot (consent, ATT, init), preloading,
// the forced interstitial before Replay, and the rewarded double-dust. Ticked each frame by AdRunner.
//
// Hard rules, all enforced here:
//   - AdsConfig.Enabled false: the provider is never touched. Nothing initialises, loads or shows.
//   - A show never blocks the game: if the ad does not open within OpenTimeoutSeconds, or fails, or
//     throws, the continuation runs (exactly once) and play goes on.
//   - The environment (audio, time scale) is suspended while an ad is up and put back afterwards.
//   - Loads happen outside runs (menus and the death screen), plus one right after a show closes.
public interface IAdEnvironment
{
    void Suspend();
    void Resume();
}

public interface IRewardStore
{
    long LastPaidToken { get; }
    // Persists the paid token and credits the dust together (one save).
    void Pay(long token, float amount);
}

public sealed class AdController
{
    public const float ConsentTimeoutSeconds = 25f;
    public const float OpenTimeoutSeconds = 6f;      // an ad that has not opened by now is abandoned
    public const float OpenWatchdogSeconds = 180f;   // an ad that opened and never reports closing
    public const float FirstRetrySeconds = 15f, MaxRetrySeconds = 120f;

    readonly IAdProvider provider;
    readonly Func<float> clock;
    readonly Func<bool> enabled;
    readonly IAdEnvironment env;
    readonly IRewardStore store;

    enum Phase { Idle, Consent, Tracking, Initialising, Ready, Blocked }
    Phase phase = Phase.Idle;
    float phaseStarted;

    bool runActive;
    bool loadingInterstitial, loadingRewarded;
    float nextInterstitialTry, nextRewardedTry;
    float interstitialBackoff = FirstRetrySeconds, rewardedBackoff = FirstRetrySeconds;

    // One show at a time; `session` invalidates stale callbacks of an abandoned show.
    int session;
    bool busy, sessionOpened, sessionFinished;
    float sessionStarted, sessionOpenedAt;
    Action sessionDone;
    bool sessionRewarded, sessionPaid, lateOpen;
    long sessionToken; float sessionEarned; Action<float> sessionOnPaid;

    float lastAdAt = float.NegativeInfinity;
    bool watchedRewardedAtDeath;

    public AdController(IAdProvider provider, Func<float> clock, Func<bool> enabled, IAdEnvironment env, IRewardStore store)
    {
        this.provider = provider; this.clock = clock; this.enabled = enabled; this.env = env; this.store = store;
    }

    public bool Initialised { get { return phase == Phase.Ready; } }
    public bool Busy { get { return busy; } }
    public bool WatchedRewardedAtDeath { get { return watchedRewardedAtDeath; } }
    public bool PrivacyOptionsRequired { get { return phase == Phase.Ready && enabled() && provider.PrivacyOptionsRequired; } }

    public float SecondsSinceLastAd
    {
        get { return float.IsNegativeInfinity(lastAdAt) ? float.MaxValue : Mathf.Max(0f, clock() - lastAdAt); }
    }

    public bool InterstitialReady { get { return phase == Phase.Ready && enabled() && provider.InterstitialReady; } }
    public bool RewardedReady { get { return phase == Phase.Ready && enabled() && provider.RewardedReady; } }

    // ---- run lifecycle (called by the game) ----

    public void OnRunStart() { runActive = true; watchedRewardedAtDeath = false; }

    // The death panel is up (or the run was left): loads are allowed again, and an ad that was
    // not ready is asked for now.
    public void OnRunEnd(bool died)
    {
        runActive = false;
        if (died) { nextInterstitialTry = 0f; nextRewardedTry = 0f; }
    }

    public void OnDeathPanel() { watchedRewardedAtDeath = false; }

    // ---- boot ----

    public void Boot()
    {
        if (phase != Phase.Idle || !enabled()) return;
        phase = Phase.Consent;
        phaseStarted = clock();
        int mark = ++session;
        try
        {
            provider.GatherConsent(can =>
            {
                if (phase != Phase.Consent || mark != session) return;
                if (!can) { phase = Phase.Blocked; return; }   // no consent to request ads: asked again next launch
                StartTracking();
            });
        }
        catch (Exception e) { Debug.Log("[ADS] consent failed: " + e.Message); phase = Phase.Blocked; }
    }

    void StartTracking()
    {
        phase = Phase.Tracking;
        phaseStarted = clock();
        int mark = session;
        try
        {
            provider.RequestTracking(() =>
            {
                if (phase != Phase.Tracking || mark != session) return;
                StartInit();
            });
        }
        catch (Exception e) { Debug.Log("[ADS] tracking failed: " + e.Message); StartInit(); }
    }

    void StartInit()
    {
        phase = Phase.Initialising;
        phaseStarted = clock();
        int mark = session;
        try
        {
            provider.Initialize(() =>
            {
                if (phase != Phase.Initialising || mark != session) return;
                phase = Phase.Ready;
                nextInterstitialTry = nextRewardedTry = 0f;
            });
        }
        catch (Exception e) { Debug.Log("[ADS] init failed: " + e.Message); phase = Phase.Blocked; }
    }

    public void ShowPrivacyOptions(Action done)
    {
        if (!PrivacyOptionsRequired) { if (done != null) done(); return; }
        provider.ShowPrivacyOptions(done);
    }

    // ---- per frame ----

    public void Tick()
    {
        float now = clock();
        if (!enabled()) return;

        switch (phase)
        {
            case Phase.Consent:
                // Never wait on the network, but the player reading the form is not the network.
                if (provider.ConsentFormShowing) phaseStarted = now;
                else if (now - phaseStarted > ConsentTimeoutSeconds) phase = Phase.Blocked;
                return;
            case Phase.Tracking:
                if (now - phaseStarted > AdTracking.TimeoutSeconds + 5f) StartInit();
                return;
            case Phase.Initialising:
                if (now - phaseStarted > ConsentTimeoutSeconds) phase = Phase.Blocked;
                return;
            case Phase.Ready:
                break;
            default:
                return;
        }

        if (busy)
        {
            if (!sessionOpened && now - sessionStarted > OpenTimeoutSeconds) FinishShow(false, true);
            else if (sessionOpened && now - sessionOpenedAt > OpenWatchdogSeconds) FinishShow(true);
            return;
        }

        if (runActive) return;   // no load requests mid-run
        if (!loadingInterstitial && !provider.InterstitialReady && now >= nextInterstitialTry) LoadInterstitialNow();
        if (!loadingRewarded && !provider.RewardedReady && now >= nextRewardedTry) LoadRewardedNow();
    }

    void LoadInterstitialNow()
    {
        loadingInterstitial = true;
        try
        {
            provider.LoadInterstitial(ok =>
            {
                loadingInterstitial = false;
                if (ok) interstitialBackoff = FirstRetrySeconds;
                else { nextInterstitialTry = clock() + interstitialBackoff; interstitialBackoff = Mathf.Min(interstitialBackoff * 2f, MaxRetrySeconds); }
            });
        }
        catch (Exception) { loadingInterstitial = false; nextInterstitialTry = clock() + interstitialBackoff; }
    }

    void LoadRewardedNow()
    {
        loadingRewarded = true;
        try
        {
            provider.LoadRewarded(ok =>
            {
                loadingRewarded = false;
                if (ok) rewardedBackoff = FirstRetrySeconds;
                else { nextRewardedTry = clock() + rewardedBackoff; rewardedBackoff = Mathf.Min(rewardedBackoff * 2f, MaxRetrySeconds); }
            });
        }
        catch (Exception) { loadingRewarded = false; nextRewardedTry = clock() + rewardedBackoff; }
    }

    // ---- the forced interstitial ----

    // Shows an interstitial before `proceed` when AdPolicy allows it and one is loaded; returns
    // true when it took over (proceed will run when the ad ends, was abandoned or failed --
    // always exactly once). Returns false at once, having done nothing, when no ad applies:
    // the caller proceeds itself.
    public bool TryShowInterstitial(AdPolicy.Inputs inputs, Action proceed)
    {
        if (!enabled() || phase != Phase.Ready || busy) return false;
        inputs.secondsSinceLastAd = SecondsSinceLastAd;
        inputs.watchedRewardedAtDeath = watchedRewardedAtDeath;
        if (!AdPolicy.MayShowForcedAd(inputs)) return false;
        if (!provider.InterstitialReady) return false;

        BeginShow(false);
        sessionDone = proceed;
        int mark = session;
        var events = Events(mark);
        try { provider.ShowInterstitial(events); }
        catch (Exception e) { Debug.Log("[ADS] interstitial show threw: " + e.Message); FinishShow(false); }
        return true;
    }

    // ---- the rewarded double dust ----

    // Shows the rewarded ad for one death. `onPaid(extra)` runs when the reward was credited
    // (once); `onDone` when the ad flow ends either way. False (nothing done) when it cannot start.
    public bool TryShowRewarded(long token, float earned, Action<float> onPaid, Action onDone)
    {
        if (!enabled() || phase != Phase.Ready || busy) return false;
        if (!provider.RewardedReady || !DoubleDustRules.CanClaim(token, store.LastPaidToken)) return false;
        if (DoubleDustRules.Bonus(earned) <= 0f) return false;

        BeginShow(true);
        sessionDone = onDone;
        sessionToken = token; sessionEarned = earned; sessionOnPaid = onPaid;
        int mark = session;
        var events = Events(mark);
        try { provider.ShowRewarded(events); }
        catch (Exception e) { Debug.Log("[ADS] rewarded show threw: " + e.Message); FinishShow(false); }
        return true;
    }

    void BeginShow(bool rewarded)
    {
        busy = true;
        session++;
        sessionStarted = clock();
        sessionOpened = sessionFinished = sessionPaid = lateOpen = false;
        sessionRewarded = rewarded;
        sessionOnPaid = null;
    }

    AdShowEvents Events(int mark)
    {
        return new AdShowEvents
        {
            opened = () =>
            {
                if (mark == session && sessionFinished && !sessionOpened && !lateOpen)
                {
                    // Opened after Replay already carried on: it cannot be cancelled, but mute and freeze while it is up.
                    lateOpen = true; env.Suspend(); return;
                }
                if (mark != session || sessionFinished || sessionOpened) return;
                sessionOpened = true;
                sessionOpenedAt = clock();
                if (sessionRewarded) watchedRewardedAtDeath = true;
                env.Suspend();
            },
            earned = () =>
            {
                if (mark != session || sessionFinished || !sessionRewarded) return;
                Pay();
            },
            closed = () =>
            {
                if (mark == session && lateOpen) { lateOpen = false; env.Resume(); return; }
                if (mark == session && !sessionFinished) FinishShow(true);
            },
            failed = message => { if (mark == session && !sessionFinished) FinishShow(sessionOpened); },
        };
    }

    void Pay()
    {
        if (sessionPaid) return;
        if (!DoubleDustRules.CanClaim(sessionToken, store.LastPaidToken)) return;
        sessionPaid = true;
        float extra = DoubleDustRules.Bonus(sessionEarned);
        if (extra <= 0f) return;
        try { store.Pay(sessionToken, extra); }
        catch (Exception e) { Debug.Log("[ADS] reward store threw: " + e.Message); return; }
        var cb = sessionOnPaid;
        if (cb != null) { try { cb(extra); } catch (Exception) { } }
    }

    // Ends the show exactly once: environment back, the clock for MinSecondsBetweenAds started, the
    // continuation run, and the next ad requested.
    void FinishShow(bool wasShown, bool abandoned = false)
    {
        if (sessionFinished) return;
        sessionFinished = true;
        busy = false;
        if (sessionOpened) { try { env.Resume(); } catch (Exception) { } }
        // An abandoned ad may still open late: count it as shown so no second ad follows right behind it.
        if (wasShown || sessionOpened || abandoned) lastAdAt = clock();
        var done = sessionDone;
        sessionDone = null;
        sessionOnPaid = null;
        if (!loadingInterstitial) LoadInterstitialNow();   // the one load that may overlap a run start: its calm opening
        if (!loadingRewarded) LoadRewardedNow();
        if (done != null) { try { done(); } catch (Exception e) { Debug.LogException(e); } }
    }
}
