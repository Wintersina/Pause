using System;

// Used wherever a real SDK is not available (editor, tests, desktop): loads "succeed" at once,
// shows are simulated by the caller ticking. Behaviour is scriptable for tests.
public class StubAdProvider : IAdProvider
{
    public bool canRequestAds = true;
    public bool privacyOptionsRequired;
    public bool interstitialReady, rewardedReady;
    public bool loadSucceeds = true;
    public bool autoCompleteCallbacks = true;   // consent / tracking / init / load callbacks fire synchronously
    public int consentCalls, trackingCalls, initCalls, interstitialLoads, rewardedLoads, interstitialShows, rewardedShows;
    public AdShowEvents lastEvents;
    public Action<bool> pendingConsent;
    public Action pendingInit;

    public void GatherConsent(Action<bool> done)
    {
        consentCalls++;
        if (autoCompleteCallbacks) done(canRequestAds); else pendingConsent = done;
    }

    public bool PrivacyOptionsRequired { get { return privacyOptionsRequired; } }
    public void ShowPrivacyOptions(Action done) { if (done != null) done(); }
    public void RequestTracking(Action done) { trackingCalls++; done(); }

    public void Initialize(Action done)
    {
        initCalls++;
        if (autoCompleteCallbacks) done(); else pendingInit = done;
    }

    public bool InterstitialReady { get { return interstitialReady; } }
    public bool RewardedReady { get { return rewardedReady; } }

    public void LoadInterstitial(Action<bool> done)
    {
        interstitialLoads++;
        if (loadSucceeds) interstitialReady = true;
        if (done != null) done(loadSucceeds);
    }

    public void LoadRewarded(Action<bool> done)
    {
        rewardedLoads++;
        if (loadSucceeds) rewardedReady = true;
        if (done != null) done(loadSucceeds);
    }

    public virtual void ShowInterstitial(AdShowEvents events)
    {
        interstitialShows++;
        interstitialReady = false;
        lastEvents = events;
    }

    public void ShowRewarded(AdShowEvents events)
    {
        rewardedShows++;
        rewardedReady = false;
        lastEvents = events;
    }
}
