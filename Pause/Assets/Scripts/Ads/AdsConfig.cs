using UnityEngine;

// Everything about ads that is a decision or an ID, in ONE file.
//
// MASTER SWITCH. MasterSwitch is false in the PR that adds ads: with it false the Google
// SDK is never initialised, no consent prompt shows, no ad is requested and the double
// dust button is not built -- the game behaves exactly as before. Flip it to true only
// after the Play Console declarations (contains ads, Data safety, advertising ID) are
// updated and real AdMob IDs are filled in below. Developer mode (DeveloperUnlocks, only
// in editor / development / PAUSE_DEV builds) forces ads on so they can be tested with
// Google's test IDs.
//
// iOS. EnabledOnIos must stay false until StoreKit ships (tracker phase 7): iOS has no
// purchase code, so an iOS player could not buy Remove Ads. While false, an iOS release
// build never shows ads even with MasterSwitch true.
//
// IDs. The real AdMob IDs (publisher pub-4397064133944218) are the constants without "Test". Google's
// published TEST IDs sit beside them. A real ad unit must never be requested by our own devices (AdMob treats a
// publisher's own traffic as invalid), so whenever developer mode is on (editor, development and PAUSE_DEV builds
// only) the unit getters return the test units; release builds get the real ones. Dev builds also carry the test
// App ID in the manifest / Info.plist (Editor/Ads/AdsBuildSync.cs). The Google Mobile Ads settings asset is filled
// from this file at build time, so changing an ID here is the only edit.
public static class AdsConfig
{
    public const bool MasterSwitch = false;
    public const bool EnabledOnIos = false;

    // ---- real IDs (AdMob account, created 2026-11-02) ----
    public const string AndroidAppId = "ca-app-pub-4397064133944218~1721630335";
    public const string AndroidInterstitialUnit = "ca-app-pub-4397064133944218/9827798178";   // "Pause Forced Ad"
    public const string AndroidRewardedUnit = "ca-app-pub-4397064133944218/3137607605";       // "Pause Double Dust"

    public const string IosAppId = "ca-app-pub-4397064133944218~1298571462";
    public const string IosInterstitialUnit = "ca-app-pub-4397064133944218/3266259255";       // "Pause Forced Ad"
    public const string IosRewardedUnit = "ca-app-pub-4397064133944218/8095466996";           // "Pause Double Dust"

    // ---- Google's published test IDs (safe to ship and to click) ----
    public const string TestAndroidAppId = "ca-app-pub-3940256099942544~3347511713";
    public const string TestAndroidInterstitialUnit = "ca-app-pub-3940256099942544/1033173712";
    public const string TestAndroidRewardedUnit = "ca-app-pub-3940256099942544/5224354917";
    public const string TestIosAppId = "ca-app-pub-3940256099942544~1458002511";
    public const string TestIosInterstitialUnit = "ca-app-pub-3940256099942544/4411468910";
    public const string TestIosRewardedUnit = "ca-app-pub-3940256099942544/1712485313";

    // The shown text for the iOS App Tracking Transparency system prompt (NSUserTrackingUsageDescription).
    public const string TrackingUsageDescription =
        "This lets Pause show you ads that are more relevant to you. You can say no and still play.";

    // Google publishes every test ID under this publisher number.
    public const string TestPublisher = "ca-app-pub-3940256099942544";

    public static bool IsIos { get { return Application.platform == RuntimePlatform.IPhonePlayer; } }

    // The live answer. False in every shipped build until MasterSwitch is flipped.
    public static bool Enabled
    {
        get { return Resolve(MasterSwitch, EnabledOnIos, IsIos, DeveloperUnlocks.Enabled); }
    }

    // Pure form of Enabled, for tests.
    public static bool Resolve(bool masterSwitch, bool enabledOnIos, bool isIos, bool developerMode)
    {
        if (developerMode) return true;
        if (!masterSwitch) return false;
        return !isIos || enabledOnIos;
    }

    // Pure choice, for tests: developer mode (or a dev build) gets the test ID, release the real one.
    public static string Pick(bool useTest, string real, string test) { return useTest ? test : real; }

    // The App ID a build carries (AdsBuildSync): dev builds the test one.
    public static string AppIdFor(bool ios, bool devBuild)
    {
        return ios ? Pick(devBuild, IosAppId, TestIosAppId) : Pick(devBuild, AndroidAppId, TestAndroidAppId);
    }

    public static string AppId { get { return AppIdFor(IsIos, DeveloperUnlocks.Enabled); } }
    public static string InterstitialUnit
    {
        get { return IsIos ? Pick(DeveloperUnlocks.Enabled, IosInterstitialUnit, TestIosInterstitialUnit) : Pick(DeveloperUnlocks.Enabled, AndroidInterstitialUnit, TestAndroidInterstitialUnit); }
    }
    public static string RewardedUnit
    {
        get { return IsIos ? Pick(DeveloperUnlocks.Enabled, IosRewardedUnit, TestIosRewardedUnit) : Pick(DeveloperUnlocks.Enabled, AndroidRewardedUnit, TestAndroidRewardedUnit); }
    }

    // True when the IDs the running game uses are Google test IDs (developer mode).
    public static bool UsingTestIds
    {
        get { return InterstitialUnit.StartsWith(TestPublisher, System.StringComparison.Ordinal) && RewardedUnit.StartsWith(TestPublisher, System.StringComparison.Ordinal); }
    }
}
