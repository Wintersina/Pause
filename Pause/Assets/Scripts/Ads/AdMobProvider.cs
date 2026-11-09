#if UNITY_ANDROID || UNITY_IOS
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using GoogleMobileAds.Api;
using GoogleMobileAds.Ump.Api;
using UnityEngine;

// Google Mobile Ads (AdMob) Unity plugin + its UMP consent API. The ONLY file that names an SDK
// type. Test IDs live in AdsConfig. Not used in the editor (AdProviders picks the stub there).
public sealed class AdMobProvider : IAdProvider
{
    // Google says an unshown loaded ad goes stale after an hour; refresh before that.
    const float MaxAdAgeSeconds = 50f * 60f;

    InterstitialAd interstitial;
    RewardedAd rewarded;
    float interstitialLoadedAt, rewardedLoadedAt;
    bool consentFormShowing;
    public bool ConsentFormShowing { get { return consentFormShowing; } }

    public void GatherConsent(Action<bool> done)
    {
        try
        {
            var parameters = new ConsentRequestParameters { TagForUnderAgeOfConsent = false };
            ConsentInformation.Update(parameters, updateError =>
            {
                if (updateError != null)
                {
                    Debug.Log("[ADS] consent update: " + updateError.Message);
                    done(SafeCanRequest());
                    return;
                }
                // Load (network, bounded by the controller's timeout), then show (the player's time, unbounded).
                if (ConsentInformation.ConsentStatus != ConsentStatus.Required || !ConsentInformation.IsConsentFormAvailable())
                {
                    done(SafeCanRequest());
                    return;
                }
                ConsentForm.Load((form, loadError) =>
                {
                    if (loadError != null || form == null)
                    {
                        Debug.Log("[ADS] consent form load: " + (loadError != null ? loadError.Message : "null"));
                        done(SafeCanRequest());
                        return;
                    }
                    consentFormShowing = true;
                    form.Show(showError =>
                    {
                        consentFormShowing = false;
                        if (showError != null) Debug.Log("[ADS] consent form: " + showError.Message);
                        done(SafeCanRequest());
                    });
                });
            });
        }
        catch (Exception e)
        {
            Debug.Log("[ADS] consent threw: " + e.Message);
            done(false);
        }
    }

    static bool SafeCanRequest()
    {
        try { return ConsentInformation.CanRequestAds(); } catch (Exception) { return false; }
    }

    public bool PrivacyOptionsRequired
    {
        get
        {
            try { return ConsentInformation.PrivacyOptionsRequirementStatus == PrivacyOptionsRequirementStatus.Required; }
            catch (Exception) { return false; }
        }
    }

    public void ShowPrivacyOptions(Action done)
    {
        try
        {
            ConsentForm.ShowPrivacyOptionsForm(error =>
            {
                if (error != null) Debug.Log("[ADS] privacy options: " + error.Message);
                if (done != null) done();
            });
        }
        catch (Exception e)
        {
            Debug.Log("[ADS] privacy options threw: " + e.Message);
            if (done != null) done();
        }
    }

#if UNITY_IOS
    [DllImport("__Internal")] static extern void PauseATTRequest();
    [DllImport("__Internal")] static extern int PauseATTStatus();   // 0 not determined / pending, 1 restricted, 2 denied, 3 authorized, 99 unavailable
#endif

    public void RequestTracking(Action done)
    {
#if UNITY_IOS
        // The system prompt is answered later; AdManager polls AdTracking.Pending until it settles.
        try { PauseATTRequest(); } catch (Exception e) { Debug.Log("[ADS] ATT threw: " + e.Message); }
        AdTracking.WaitForAnswer(() => { try { return PauseATTStatus(); } catch (Exception) { return 99; } }, done);
#else
        done();
#endif
    }

    public void Initialize(Action done)
    {
        try
        {
            MobileAds.RaiseAdEventsOnUnityMainThread = true;
#if UNITY_IOS
            MobileAds.SetiOSAppPauseOnBackground(true);
#endif
            MobileAds.Initialize(status => done());
        }
        catch (Exception e)
        {
            Debug.Log("[ADS] initialize threw: " + e.Message);
        }
    }

    public bool InterstitialReady
    {
        get
        {
            try { return interstitial != null && interstitial.CanShowAd() && Time.realtimeSinceStartup - interstitialLoadedAt < MaxAdAgeSeconds; }
            catch (Exception) { return false; }
        }
    }

    public bool RewardedReady
    {
        get
        {
            try { return rewarded != null && rewarded.CanShowAd() && Time.realtimeSinceStartup - rewardedLoadedAt < MaxAdAgeSeconds; }
            catch (Exception) { return false; }
        }
    }

    public void LoadInterstitial(Action<bool> done)
    {
        try
        {
            if (interstitial != null) { interstitial.Destroy(); interstitial = null; }
            InterstitialAd.Load(AdsConfig.InterstitialUnit, new AdRequest(), (ad, error) =>
            {
                if (error != null || ad == null) { Debug.Log("[ADS] interstitial load failed: " + (error != null ? error.GetMessage() : "null")); done(false); return; }
                interstitial = ad;
                interstitialLoadedAt = Time.realtimeSinceStartup;
                done(true);
            });
        }
        catch (Exception e) { Debug.Log("[ADS] interstitial load threw: " + e.Message); done(false); }
    }

    public void LoadRewarded(Action<bool> done)
    {
        try
        {
            PurgeRetired();
            if (rewarded != null) { rewarded.Destroy(); rewarded = null; }
            RewardedAd.Load(AdsConfig.RewardedUnit, new AdRequest(), (ad, error) =>
            {
                if (error != null || ad == null) { Debug.Log("[ADS] rewarded load failed: " + (error != null ? error.GetMessage() : "null")); done(false); return; }
                rewarded = ad;
                rewardedLoadedAt = Time.realtimeSinceStartup;
                done(true);
            });
        }
        catch (Exception e) { Debug.Log("[ADS] rewarded load threw: " + e.Message); done(false); }
    }

    public void ShowInterstitial(AdShowEvents events)
    {
        var ad = interstitial;
        interstitial = null;   // a shown ad is spent
        if (ad == null) { Fail(events, "not loaded"); return; }
        try
        {
            ad.OnAdFullScreenContentOpened += () => { if (events.opened != null) events.opened(); };
            ad.OnAdFullScreenContentClosed += () => { ad.Destroy(); if (events.closed != null) events.closed(); };
            ad.OnAdFullScreenContentFailed += error => { ad.Destroy(); Fail(events, error != null ? error.GetMessage() : "failed"); };
            ad.Show();
        }
        catch (Exception e) { Fail(events, e.Message); }
    }

    readonly System.Collections.Generic.List<KeyValuePair<RewardedAd, float>> retired = new System.Collections.Generic.List<KeyValuePair<RewardedAd, float>>();
    const float RetireSeconds = 20f;

    void Retire(RewardedAd ad) { retired.Add(new KeyValuePair<RewardedAd, float>(ad, Time.realtimeSinceStartup)); }

    void PurgeRetired()
    {
        for (int i = retired.Count - 1; i >= 0; i--)
        {
            if (Time.realtimeSinceStartup - retired[i].Value < RetireSeconds) continue;
            try { retired[i].Key.Destroy(); } catch (Exception) { }
            retired.RemoveAt(i);
        }
    }

    public void ShowRewarded(AdShowEvents events)
    {
        PurgeRetired();
        var ad = rewarded;
        rewarded = null;
        if (ad == null) { Fail(events, "not loaded"); return; }
        try
        {
            ad.OnAdFullScreenContentOpened += () => { if (events.opened != null) events.opened(); };
            // Not destroyed on close: the reward callback can still arrive after the close, so the ad is parked
            // and destroyed a while later (Retire).
            ad.OnAdFullScreenContentClosed += () => { Retire(ad); if (events.closed != null) events.closed(); };
            ad.OnAdFullScreenContentFailed += error => { Retire(ad); Fail(events, error != null ? error.GetMessage() : "failed"); };
            ad.Show(reward => { if (events.earned != null) events.earned(); });
        }
        catch (Exception e) { Fail(events, e.Message); }
    }

    BannerView banner;

    // No real banner unit yet (placeholder ID): a release build requests nothing.
    public bool BannerAvailable { get { return AdsConfig.BannerRequestable; } }

    public void ShowBanner()
    {
        if (!AdsConfig.BannerRequestable) return;
        try
        {
            HideBanner();
            banner = new BannerView(AdsConfig.BannerUnit, AdSize.Banner, AdPosition.Bottom);
            banner.LoadAd(new AdRequest());
        }
        catch (Exception e) { Debug.Log("[ADS] banner threw: " + e.Message); banner = null; }
    }

    public void HideBanner()
    {
        var b = banner;
        banner = null;
        if (b == null) return;
        try { b.Destroy(); } catch (Exception) { }
    }

    static void Fail(AdShowEvents events, string message)
    {
        Debug.Log("[ADS] show failed: " + message);
        if (events.failed != null) events.failed(message);
    }
}
#endif
