using System;

// The seam between the game's ad rules and an ad SDK. AdMobProvider (Android / iOS device
// builds) and StubAdProvider (editor, tests, other platforms) implement it; nothing else in
// the game touches an SDK type. Every callback is invoked on Unity's main thread.
public sealed class AdShowEvents
{
    public Action opened;        // the full-screen ad is on screen
    public Action earned;        // rewarded only: the user earned the reward
    public Action closed;        // the full-screen ad was dismissed
    public Action<string> failed; // it could not be shown (or errored while showing)
}

public interface IAdProvider
{
    // UMP: update the consent state and show the form when one is required (EEA / UK).
    // done(canRequestAds). Must be called before anything else.
    void GatherConsent(Action<bool> done);

    // True after GatherConsent when a "privacy options" entry must be offered in settings.
    bool PrivacyOptionsRequired { get; }

    // True while the consent form is on screen. The player's reading time must not count against the consent timeout.
    bool ConsentFormShowing { get; }
    void ShowPrivacyOptions(Action done);

    // iOS App Tracking Transparency prompt (after UMP). A no-op that completes at once elsewhere.
    void RequestTracking(Action done);

    // Starts the SDK. done() when it is ready for load requests.
    void Initialize(Action done);

    bool InterstitialReady { get; }
    bool RewardedReady { get; }
    void LoadInterstitial(Action<bool> done);
    void LoadRewarded(Action<bool> done);
    void ShowInterstitial(AdShowEvents events);
    void ShowRewarded(AdShowEvents events);

    // The small banner on the results modal, bottom centre of the screen. Never requested when no real unit ID exists
    // (BannerAvailable false). Show is fire-and-forget; Hide removes it at once.
    bool BannerAvailable { get; }
    void ShowBanner();
    void HideBanner();
}
