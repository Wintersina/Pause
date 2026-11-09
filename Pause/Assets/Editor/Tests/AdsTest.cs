using System;
using UnityEditor;
using UnityEngine;

// Ads: the master switch (on for Android, iOS gate closed), AdPolicy (every forced-ad rule), the double dust
// rules, and AdController on a stub provider (boot, consent, preload, interstitial, rewarded, timeouts,
// failures, exactly-once). No SDK, no network.
public static class AdsTest
{
    static int failures;

    static void Check(string label, bool condition)
    {
        if (!condition) failures++;
        Debug.Log("[ADS] " + (condition ? "PASS " : "FAIL ") + label);
    }

    public static void Run() { TestHarness.Exit(Execute()); }

    class FakeEnv : IAdEnvironment
    {
        public int suspends, resumes;
        public void Suspend() { suspends++; }
        public void Resume() { resumes++; }
    }

    class FakeStore : IRewardStore
    {
        public long paid; public int pays; public float total;
        public int grants; public float granted;
        public long LastPaidToken { get { return paid; } }
        public void Pay(long token, float amount) { paid = token; pays++; total += amount; }
        public void Grant(float amount) { grants++; granted += amount; }
    }

    class Rig
    {
        public StubAdProvider provider = new StubAdProvider();
        public FakeEnv env = new FakeEnv();
        public FakeStore store = new FakeStore();
        public float now = 100f;
        public bool enabled = true;
        public AdController controller;
        public Rig(IAdProvider p = null)
        {
            if (p != null) provider = (StubAdProvider)p;
            controller = new AdController(provider, () => now, () => enabled, env, store);
        }
        public void BootReady() { controller.Boot(); controller.Tick(); }
    }

    static AdPolicy.Inputs Good()
    {
        return new AdPolicy.Inputs
        {
            removeAdsOwned = false, paragonOwned = false, playSeconds = AdPolicy.AdFreePlaySeconds + 1f,
            boosterActive = false, secondsSinceLastAd = float.MaxValue, runSeconds = AdPolicy.MinRunSecondsForAd + 1f,
            watchedRewardedAtDeath = false, tutorialOrPractice = false,
        };
    }

    public static int Execute()
    {
        failures = 0;
        using var sandbox = new TestHarness.Sandbox();
        PlayerPrefs.DeleteKey(DeveloperUnlocks.EnabledKey);
        var savedBooster = AdPolicy.BoosterActiveCheck;
        foreach (string k in BoosterKeys) PlayerPrefs.DeleteKey(k);
        Boosters.Reload();
        Boosters.ResetRun();
        float savedScale = Time.timeScale;
        try
        {
            Config();
            Policy();
            BoosterWiring(savedBooster);
            DoubleDust();
            DoubleDustWithBooster();
            DustTickUpCases();
            Cadence();
            RunEndAndShopFlow();
            ReplaySpam();
            BannerLayout();
            DoubleDustButton();
            Controller();
            Environment();
            BuildSync();
            ManifestGuard();
        }
        finally
        {
            AdPolicy.BoosterActiveCheck = savedBooster;
            foreach (string k in BoosterKeys) PlayerPrefs.DeleteKey(k);
            Boosters.Reload();
            Boosters.ResetRun();
            Time.timeScale = savedScale;
            AdManager.Reset();
        }
        return failures;
    }

    static void Config()
    {
        Check("MASTER SWITCH: AdsConfig.MasterSwitch is on (the free-with-ads release)", AdsConfig.MasterSwitch == true);
        Check("MASTER SWITCH: a release Android build shows ads", AdsConfig.Resolve(AdsConfig.MasterSwitch, AdsConfig.EnabledOnIos, false, false));
        Check("MASTER SWITCH: a release iOS build never shows ads (no StoreKit)", !AdsConfig.Resolve(AdsConfig.MasterSwitch, AdsConfig.EnabledOnIos, true, false));
        Check("EnabledOnIos defaults to false (no StoreKit yet)", AdsConfig.EnabledOnIos == false);
        Check("resolve: off stays off", !AdsConfig.Resolve(false, false, false, false) && !AdsConfig.Resolve(false, true, true, false));
        Check("resolve: master on, Android", AdsConfig.Resolve(true, false, false, false));
        Check("resolve: master on, iOS gate closed -> no ads", !AdsConfig.Resolve(true, false, true, false));
        Check("resolve: master on, iOS gate open", AdsConfig.Resolve(true, true, true, false));
        Check("resolve: developer mode forces on", AdsConfig.Resolve(false, false, false, true) && AdsConfig.Resolve(false, false, true, true));
        Check("App IDs have the ~ form, ad units the / form",
            AdsConfig.AndroidAppId.Contains("~") && AdsConfig.IosAppId.Contains("~") && AdsConfig.TestAndroidAppId.Contains("~") && AdsConfig.TestIosAppId.Contains("~")
            && AdsConfig.AndroidInterstitialUnit.Contains("/") && AdsConfig.AndroidRewardedUnit.Contains("/")
            && AdsConfig.IosInterstitialUnit.Contains("/") && AdsConfig.IosRewardedUnit.Contains("/"));
        Check("real IDs are the publisher's, not Google's",
            AdsConfig.AndroidAppId.StartsWith("ca-app-pub-4397064133944218~") && AdsConfig.IosRewardedUnit.StartsWith("ca-app-pub-4397064133944218/")
            && !AdsConfig.AndroidInterstitialUnit.StartsWith(AdsConfig.TestPublisher) && !AdsConfig.IosInterstitialUnit.StartsWith(AdsConfig.TestPublisher));
        Check("test IDs are Google's", AdsConfig.TestAndroidAppId.StartsWith(AdsConfig.TestPublisher) && AdsConfig.TestAndroidInterstitialUnit.StartsWith(AdsConfig.TestPublisher)
            && AdsConfig.TestAndroidRewardedUnit.StartsWith(AdsConfig.TestPublisher) && AdsConfig.TestIosAppId.StartsWith(AdsConfig.TestPublisher)
            && AdsConfig.TestIosInterstitialUnit.StartsWith(AdsConfig.TestPublisher) && AdsConfig.TestIosRewardedUnit.StartsWith(AdsConfig.TestPublisher));
        Check("unit choice: developer mode gets the test unit, release the real one",
            AdsConfig.Pick(true, AdsConfig.AndroidInterstitialUnit, AdsConfig.TestAndroidInterstitialUnit) == AdsConfig.TestAndroidInterstitialUnit
            && AdsConfig.Pick(false, AdsConfig.AndroidInterstitialUnit, AdsConfig.TestAndroidInterstitialUnit) == AdsConfig.AndroidInterstitialUnit);
        Check("App ID choice: dev builds carry the test App ID, release the real one",
            AdsConfig.AppIdFor(false, true) == AdsConfig.TestAndroidAppId && AdsConfig.AppIdFor(true, true) == AdsConfig.TestIosAppId
            && AdsConfig.AppIdFor(false, false) == AdsConfig.AndroidAppId && AdsConfig.AppIdFor(true, false) == AdsConfig.IosAppId);
        Check("outside developer mode the live units are the real ones (not test)", !AdsConfig.UsingTestIds && !AdsConfig.InterstitialUnit.StartsWith(AdsConfig.TestPublisher));
        PlayerPrefs.SetInt(DeveloperUnlocks.EnabledKey, 1);
        Check("in developer mode the live units are Google's test units", AdsConfig.UsingTestIds && AdsConfig.RewardedUnit.StartsWith(AdsConfig.TestPublisher));
        PlayerPrefs.DeleteKey(DeveloperUnlocks.EnabledKey);
        Check("tracking text is set", !string.IsNullOrEmpty(AdsConfig.TrackingUsageDescription));
    }

    static void Policy()
    {
        Check("policy numbers", AdPolicy.AdFreePlaySeconds == 900f && AdPolicy.MinSecondsBetweenAds == 180f && AdPolicy.MinRunSecondsForAd == 45f);
        Check("policy defaults: Paragon removes ads, Remove Ads owners keep the double dust offer",
            AdPolicy.ParagonRemovesAds && AdPolicy.RemoveAdsOwnersSeeDoubleDust);
        Check("all clear -> allowed", AdPolicy.ForcedAd(Good()) == AdPolicy.Reason.Allowed && AdPolicy.MayShowForcedAd(Good()));

        var i = Good(); i.removeAdsOwned = true;
        Check("Remove Ads owner: none", AdPolicy.ForcedAd(i) == AdPolicy.Reason.RemoveAdsOwned);
        i = Good(); i.paragonOwned = true;
        Check("Paragon owner: none", AdPolicy.ForcedAd(i) == AdPolicy.Reason.ParagonOwned);
        i = Good(); i.playSeconds = AdPolicy.AdFreePlaySeconds - 0.5f;
        Check("under 15 minutes: none", AdPolicy.ForcedAd(i) == AdPolicy.Reason.UnderPlayTime);
        i.playSeconds = AdPolicy.AdFreePlaySeconds;
        Check("exactly 15 minutes: allowed", AdPolicy.ForcedAd(i) == AdPolicy.Reason.Allowed);
        i = Good(); i.boosterActive = true;
        Check("booster active: none", AdPolicy.ForcedAd(i) == AdPolicy.Reason.BoosterActive);
        i = Good(); i.secondsSinceLastAd = AdPolicy.MinSecondsBetweenAds - 1f;
        Check("under 3 min since the last ad: none", AdPolicy.ForcedAd(i) == AdPolicy.Reason.TooSoonAfterAd);
        i.secondsSinceLastAd = AdPolicy.MinSecondsBetweenAds;
        Check("exactly 3 min since the last ad: allowed", AdPolicy.ForcedAd(i) == AdPolicy.Reason.Allowed);
        i = Good(); i.runSeconds = AdPolicy.MinRunSecondsForAd - 1f;
        Check("run shorter than 45 s: none", AdPolicy.ForcedAd(i) == AdPolicy.Reason.RunTooShort);
        i.runSeconds = AdPolicy.MinRunSecondsForAd;
        Check("run of exactly 45 s: allowed", AdPolicy.ForcedAd(i) == AdPolicy.Reason.Allowed);
        i = Good(); i.watchedRewardedAtDeath = true;
        Check("watched the rewarded ad at this death: none", AdPolicy.ForcedAd(i) == AdPolicy.Reason.WatchedRewardedAtDeath);
        i = Good(); i.tutorialOrPractice = true;
        Check("tutorial or practice: none", AdPolicy.ForcedAd(i) == AdPolicy.Reason.TutorialOrPractice);
        i = new AdPolicy.Inputs { removeAdsOwned = true, paragonOwned = true, boosterActive = true, tutorialOrPractice = true, watchedRewardedAtDeath = true };
        Check("a fresh default (zeroed) input set never allows", !AdPolicy.MayShowForcedAd(new AdPolicy.Inputs()));
        Check("several reasons: the first in order wins", AdPolicy.ForcedAd(i) == AdPolicy.Reason.RemoveAdsOwned);

        Check("booster hook defaults to no booster", AdPolicy.BoosterActiveCheck != null && !AdPolicy.BoosterActive);
        AdPolicy.BoosterActiveCheck = () => true;
        Check("booster hook is honoured", AdPolicy.BoosterActive);
        AdPolicy.BoosterActiveCheck = () => { throw new Exception("x"); };
        Check("a throwing booster hook counts as none", !AdPolicy.BoosterActive);
        AdPolicy.BoosterActiveCheck = null;
        Check("a null booster hook counts as none", !AdPolicy.BoosterActive);
        AdPolicy.BoosterActiveCheck = () => false;

        Check("double dust: offered to a normal player", AdPolicy.DoubleDustOffered(false, false, 3f));
        Check("double dust: Remove Ads owners still see it (default)", AdPolicy.DoubleDustOffered(true, false, 3f));
        Check("double dust: not in the tutorial or practice", !AdPolicy.DoubleDustOffered(false, true, 3f));
        Check("double dust: nothing earned, nothing to double", !AdPolicy.DoubleDustOffered(false, false, 0f));
    }

    // ---- run cadence, banner vs interstitial, the shop ad, the banner layout ----

    static void Cadence()
    {
        Check("CADENCE: the full ad fires every 3rd eligible run end (one constant)", AdPolicy.RunsPerInterstitial == 3);
        Check("CADENCE: runs 1 and 2 are not due, 3 is, 4 and 5 are not, 6 is",
            !AdPolicy.CadenceDue(1) && !AdPolicy.CadenceDue(2) && AdPolicy.CadenceDue(3) && !AdPolicy.CadenceDue(4) && !AdPolicy.CadenceDue(5) && AdPolicy.CadenceDue(6));
        Check("CADENCE: run 0 or below is never due", !AdPolicy.CadenceDue(0) && !AdPolicy.CadenceDue(-3));

        // banner vs interstitial selection
        Check("CHOICE: eligible run 1 -> banner, 2 -> banner, 3 -> interstitial, 4 -> banner",
            AdPolicy.ChooseRunEndAd(Good(), 1, true, true) == AdPolicy.RunEndAd.Banner
            && AdPolicy.ChooseRunEndAd(Good(), 2, true, true) == AdPolicy.RunEndAd.Banner
            && AdPolicy.ChooseRunEndAd(Good(), 3, true, true) == AdPolicy.RunEndAd.Interstitial
            && AdPolicy.ChooseRunEndAd(Good(), 4, true, true) == AdPolicy.RunEndAd.Banner);
        Check("CHOICE: due but no interstitial loaded -> banner", AdPolicy.ChooseRunEndAd(Good(), 3, false, true) == AdPolicy.RunEndAd.Banner);
        Check("CHOICE: no banner unit (placeholder) -> nothing on a non-due run", AdPolicy.ChooseRunEndAd(Good(), 1, true, false) == AdPolicy.RunEndAd.None);
        var i = Good(); i.removeAdsOwned = true;
        Check("CHOICE: Remove Ads -> no banner and no forced ad, even on a due run",
            AdPolicy.ChooseRunEndAd(i, 3, true, true) == AdPolicy.RunEndAd.None && !AdPolicy.BannerAllowed(i));
        i = Good(); i.paragonOwned = true;
        Check("CHOICE: Paragon -> nothing", AdPolicy.ChooseRunEndAd(i, 3, true, true) == AdPolicy.RunEndAd.None);
        i = Good(); i.playSeconds = AdPolicy.AdFreePlaySeconds - 1f;
        Check("CHOICE: first 15 minutes -> nothing", AdPolicy.ChooseRunEndAd(i, 3, true, true) == AdPolicy.RunEndAd.None);
        i = Good(); i.tutorialOrPractice = true;
        Check("CHOICE: tutorial or practice -> nothing", AdPolicy.ChooseRunEndAd(i, 3, true, true) == AdPolicy.RunEndAd.None);
        i = Good(); i.secondsSinceLastAd = 5f;
        Check("CHOICE: a due run too soon after an ad gets only the banner", AdPolicy.ChooseRunEndAd(i, 3, true, true) == AdPolicy.RunEndAd.Banner);
        i = Good(); i.boosterActive = true;
        Check("CHOICE: a booster blocks the forced ad, not the banner", AdPolicy.ChooseRunEndAd(i, 3, true, true) == AdPolicy.RunEndAd.Banner);

        // banner ids
        Check("BANNER: test ids are Google's, the real ones are the Pause Banner units",
            AdsConfig.TestAndroidBannerUnit.StartsWith(AdsConfig.TestPublisher) && AdsConfig.TestIosBannerUnit.StartsWith(AdsConfig.TestPublisher)
            && AdsConfig.AndroidBannerUnit == "ca-app-pub-4397064133944218/7474385975" && AdsConfig.IosBannerUnit == "ca-app-pub-4397064133944218/8592026521"
            && !AdsConfig.IsPlaceholder(AdsConfig.AndroidBannerUnit) && !AdsConfig.IsPlaceholder(AdsConfig.IosBannerUnit)
            && !AdsConfig.IsPlaceholder(AdsConfig.TestAndroidBannerUnit) && AdsConfig.IsPlaceholder(""));
        Check("BANNER: a release build requests the real unit", AdsConfig.BannerRequestable && AdsConfig.BannerUnit.StartsWith("ca-app-pub-4397064133944218/"));
        PlayerPrefs.SetInt(DeveloperUnlocks.EnabledKey, 1);
        Check("BANNER: developer mode uses the test unit, so it is requestable", AdsConfig.BannerRequestable && AdsConfig.BannerUnit.StartsWith(AdsConfig.TestPublisher));
        PlayerPrefs.DeleteKey(DeveloperUnlocks.EnabledKey);
        Check("BANNER: 50 dp is 50 px at 160 dpi and 131 px at 420 dpi; no density counts as 160",
            Mathf.Approximately(AdsConfig.BannerHeightPx(160f), 50f) && Mathf.Approximately(AdsConfig.BannerHeightPx(420f), 131.25f) && Mathf.Approximately(AdsConfig.BannerHeightPx(0f), 50f));
    }

    static void RunEndAndShopFlow()
    {
        // three eligible run ends: banner, banner, interstitial (auto-plays BEFORE the results build)
        var r = new Rig();
        r.BootReady();
        r.controller.OnRunEnd(true); r.controller.Tick();
        int built = 0;
        var c = r.controller;
        Check("FLOW: run end 1 -> a banner, results build at once (caller proceeds)",
            c.TryRunEndAd(Good(), () => built++) == AdPolicy.RunEndAd.Banner && built == 0 && r.provider.bannerShows == 1 && c.BannerShowing && r.provider.interstitialShows == 0);
        c.OnRunStart();
        Check("FLOW: a run start takes the banner down", !c.BannerShowing && r.provider.bannerHides == 1 && !r.provider.bannerUp);
        c.OnRunEnd(true); c.Tick();
        Check("FLOW: run end 2 -> a banner", c.TryRunEndAd(Good(), () => built++) == AdPolicy.RunEndAd.Banner && r.provider.bannerShows == 2);
        c.OnRunStart(); c.OnRunEnd(true); c.Tick();
        Check("FLOW: run end 3 -> the interstitial takes over, no banner, results wait",
            c.TryRunEndAd(Good(), () => built++) == AdPolicy.RunEndAd.Interstitial && r.provider.interstitialShows == 1 && built == 0 && !c.BannerShowing && c.Busy);
        r.provider.lastEvents.opened(); r.provider.lastEvents.closed();
        Check("FLOW: the results build once, when the interstitial closes", built == 1 && !c.Busy);
        c.OnRunStart(); c.OnRunEnd(true); c.Tick();
        Check("FLOW: right after an ad the next run end gets a banner and does not count as eligible",
            c.TryRunEndAd(Good(), () => built++) == AdPolicy.RunEndAd.Banner && c.EligibleRunEnds == 3);

        // Remove Ads: no banner, no forced ad; the shop ad stays
        r = new Rig(); r.BootReady(); r.controller.OnRunEnd(true); r.controller.Tick();
        c = r.controller;
        var owner = Good(); owner.removeAdsOwned = true;
        for (int n = 0; n < 4; n++) c.TryRunEndAd(owner, () => built++);
        Check("REMOVE ADS: four run ends show no banner and no interstitial", r.provider.bannerShows == 0 && r.provider.interstitialShows == 0 && !c.BannerShowing);
        float paid = -1f;
        bool done = false;
        Check("REMOVE ADS: the shop ad still starts", c.ShopAdReady && c.TryShowShopReward(x => paid = x, () => done = true) && r.provider.rewardedShows == 1);
        Check("SHOP AD: nothing is credited before the SDK's reward callback", r.store.grants == 0 && r.store.granted == 0f);
        r.provider.lastEvents.opened();
        r.provider.lastEvents.closed();
        Check("SHOP AD: closing without earning pays nothing, the flow still ends", r.store.grants == 0 && done && !c.Busy);

        r.provider.rewardedReady = true; done = false;
        Check("SHOP AD: repeatable (a second show starts)", c.TryShowShopReward(x => paid = x, () => done = true));
        r.provider.lastEvents.opened();
        r.provider.lastEvents.earned();
        r.provider.lastEvents.earned();
        Check("SHOP AD: +50 star dust on the reward callback, once", r.store.grants == 1 && Mathf.Approximately(r.store.granted, 50f) && Mathf.Approximately(paid, 50f) && AdController.ShopAdDust == 50f);
        r.provider.lastEvents.closed();
        Check("SHOP AD: it does not touch the death token or the death-ad flag", r.store.pays == 0 && !c.WatchedRewardedAtDeath);
        Check("SHOP AD: counts as an ad for the minimum gap (a forced ad cannot follow)",
            c.SecondsSinceLastAd < AdPolicy.MinSecondsBetweenAds && !c.TryShowInterstitial(Good(), null));

        // Paragon owner keeps it too; loading state when nothing is loaded
        r = new Rig(); r.BootReady(); c = r.controller;
        r.provider.rewardedReady = false; r.provider.loadSucceeds = false;
        Check("SHOP AD: no ad loaded -> not ready, cannot start", !c.ShopAdReady && !c.TryShowShopReward(null, null) && r.provider.rewardedShows == 0);
        var par = Good(); par.paragonOwned = true;
        c.OnRunEnd(true);
        Check("PARAGON: no banner either", c.TryRunEndAd(par, null) == AdPolicy.RunEndAd.None && r.provider.bannerShows == 0);
        r.provider.loadSucceeds = true; r.provider.rewardedReady = true;
        Check("PARAGON: the shop ad is still on", c.ShopAdReady && c.TryShowShopReward(null, null));

        // provider has no banner -> nothing requested
        r = new Rig(); r.provider.bannerAvailable = false; r.BootReady(); r.controller.OnRunEnd(true);
        Check("BANNER: provider cannot show one -> nothing is requested", r.controller.TryRunEndAd(Good(), null) == AdPolicy.RunEndAd.None && r.provider.bannerShows == 0);
        // disabled: nothing at all
        r = new Rig { enabled = false };
        Check("DISABLED: no banner, no interstitial, no shop ad",
            r.controller.TryRunEndAd(Good(), null) == AdPolicy.RunEndAd.None && !r.controller.TryShowShopReward(null, null) && r.provider.bannerShows == 0);
    }

    // The banner never overlaps the panel or the double dust button under its frame, on tall phones, foldables (the Flip
    // cover screen too) and tablets; the button never overlaps REPLAY / MENU / SPACE DOCK and its label fits one line.
    // Replay presses (results Replay, pause-menu / quick-action Replay) count toward a forced ad; run length is irrelevant.
    static void ReplaySpam()
    {
        PlayerPrefs.DeleteKey(ReplayAdCounter.Key);
        Check("REPLAY: the constant is 3", AdPolicy.ReplaysPerForcedAd == 3);
        var quick = Good(); quick.runSeconds = 5f;   // far under MinRunSecondsForAd
        var r = new Rig(); r.BootReady();
        var c = r.controller;
        int started = 0, tookOver = 0;
        for (int n = 1; n <= 2; n++)
        {
            c.OnRunStart(); c.OnRunEnd(false);
            if (c.TryReplayAd(quick, () => started++)) tookOver++;
        }
        Check("REPLAY: 2 quick replays (runs < 45 s) show nothing, the count is 2",
            tookOver == 0 && r.provider.interstitialShows == 0 && ReplayAdCounter.Count == 2);
        Check("REPLAY: the 3rd replay takes over with an interstitial before the new run starts",
            c.TryReplayAd(quick, () => started++) && r.provider.interstitialShows == 1 && started == 0 && c.Busy);
        r.provider.lastEvents.opened(); r.provider.lastEvents.closed();
        Check("REPLAY: the new run starts once the ad closes, the counter is reset", started == 1 && !c.Busy && ReplayAdCounter.Count == 0);

        // then it starts over: 5 more
        r.now += 1000f; r.provider.interstitialReady = true;
        for (int n = 1; n <= 2; n++) c.TryReplayAd(quick, () => started++);
        Check("REPLAY: after the reset it takes 3 more (exactly one ad per 3)", r.provider.interstitialShows == 1 && ReplayAdCounter.Count == 2);

        // persistence: the count lives in PlayerPrefs, a new controller (app restart) continues from it
        var r2 = new Rig(); r2.BootReady();
        Check("REPLAY: the counter survives an app restart (new controller)",
            ReplayAdCounter.Count == 2 && r2.controller.TryReplayAd(quick, () => started++) && r2.provider.interstitialShows == 1);
        r2.provider.lastEvents.closed();

        // no ad loaded: never blocks, keeps the count, fires at the next press
        PlayerPrefs.DeleteKey(ReplayAdCounter.Key);
        r = new Rig(); r.BootReady(); c = r.controller;
        r.provider.loadSucceeds = false; r.provider.interstitialReady = false;
        int go = 0;
        bool blocked = false;
        for (int n = 1; n <= 4; n++) blocked |= c.TryReplayAd(quick, () => go++);
        Check("REPLAY: no ad loaded -> never takes over (the game is never blocked), the count keeps growing",
            !blocked && ReplayAdCounter.Count == 4 && r.provider.interstitialShows == 0 && !c.Busy);
        r.provider.interstitialReady = true;
        Check("REPLAY: an ad loads later -> the very next press shows it and resets",
            c.TryReplayAd(quick, () => go++) && r.provider.interstitialShows == 1 && ReplayAdCounter.Count == 0);
        r.provider.lastEvents.closed();

        // exemptions
        PlayerPrefs.DeleteKey(ReplayAdCounter.Key);
        r = new Rig(); r.BootReady(); c = r.controller;
        var owner = quick; owner.removeAdsOwned = true;
        for (int n = 0; n < 12; n++) c.TryReplayAd(owner, null);
        Check("REPLAY: Remove Ads owners never get one (and nothing is counted)", r.provider.interstitialShows == 0 && ReplayAdCounter.Count == 0);
        var fresh = quick; fresh.playSeconds = AdPolicy.AdFreePlaySeconds - 1f;
        for (int n = 0; n < 12; n++) c.TryReplayAd(fresh, null);
        Check("REPLAY: inside the first 15 minutes never", r.provider.interstitialShows == 0 && ReplayAdCounter.Count == 0);
        var par = quick; par.paragonOwned = true;
        for (int n = 0; n < 12; n++) c.TryReplayAd(par, null);
        Check("REPLAY: Paragon owners never (while it removes ads)", r.provider.interstitialShows == 0);
        r.enabled = false;
        for (int n = 0; n < 12; n++) c.TryReplayAd(quick, null);
        Check("REPLAY: ads disabled -> nothing", r.provider.interstitialShows == 0 && ReplayAdCounter.Count == 0);

        // the original loophole: runs under MinRunSecondsForAd never became eligible, so replay spam skipped the every-3rd ad
        PlayerPrefs.DeleteKey(ReplayAdCounter.Key);
        r = new Rig(); r.BootReady(); c = r.controller;
        int bannersOrNone = 0;
        for (int n = 0; n < 6; n++)
        {
            c.OnRunStart(); c.OnRunEnd(true); c.Tick();
            if (c.TryRunEndAd(quick, null) != AdPolicy.RunEndAd.Interstitial) bannersOrNone++;
        }
        Check("LOOPHOLE: run ends under 45 s never reach the every-3rd interstitial on their own", bannersOrNone == 6 && r.provider.interstitialShows == 0 && c.EligibleRunEnds == 0);
        int ok = 0;
        for (int n = 0; n < 3; n++) { c.OnRunStart(); if (c.TryReplayAd(quick, () => ok++)) break; }
        Check("LOOPHOLE: ... but 3 results replays of those quick runs do show one", r.provider.interstitialShows == 1);
        r.provider.lastEvents.closed();

        // a run-end interstitial that was shown also resets the replay count
        PlayerPrefs.SetInt(ReplayAdCounter.Key, 2);
        r = new Rig(); r.BootReady(); c = r.controller;
        c.OnRunEnd(true); c.Tick();
        for (int n = 0; n < 3; n++) c.TryRunEndAd(Good(), null);
        Check("REPLAY: a shown run-end interstitial resets the replay count", r.provider.interstitialShows == 1 && ReplayAdCounter.Count == 0);

        // a due run-end interstitial that found no ad stays owed (the miss does not push it a whole cycle away)
        r = new Rig(); r.BootReady(); c = r.controller;
        r.provider.loadSucceeds = false; r.provider.interstitialReady = false;
        c.OnRunEnd(true);
        for (int n = 0; n < 3; n++) c.TryRunEndAd(Good(), null);
        Check("OWED: the 3rd eligible run end found no ad -> banner, no interstitial", r.provider.interstitialShows == 0);
        r.provider.interstitialReady = true;
        Check("OWED: the very next eligible run end gets the interstitial", c.TryRunEndAd(Good(), null) == AdPolicy.RunEndAd.Interstitial && r.provider.interstitialShows == 1);
        r.provider.lastEvents.closed();
        PlayerPrefs.DeleteKey(ReplayAdCounter.Key);
    }

    static void BannerLayout()
    {
        Check("LAYOUT: no banner -> the area is untouched", DeathPanelView.ReserveBottomForBanner(new Rect(0, 0, 1080, 2400), 0f) == new Rect(0, 0, 1080, 2400));
        var screens = new[]
        {
            new { name = "tall phone", w = 1080, h = 2520, safe = new Rect(0, 90, 1080, 2250), dpi = 420f },
            new { name = "phone gesture bar", w = 1080, h = 2400, safe = new Rect(0, 63, 1080, 2230), dpi = 420f },
            new { name = "foldable open", w = 2208, h = 1840, safe = new Rect(0, 0, 2208, 1840), dpi = 420f },
            new { name = "flip cover", w = 720, h = 748, safe = new Rect(0, 0, 720, 748), dpi = 306f },
            new { name = "tablet", w = 1600, h = 2560, safe = new Rect(0, 0, 1600, 2560), dpi = 280f },
            new { name = "short phone", w = 1080, h = 1920, safe = new Rect(0, 0, 1080, 1920), dpi = 420f },
        };
        var tabRect = DoubleDustOffer.PanelRect;
        Check("TAB: sits below the frame's glow edge, not on the frame art", tabRect.yMax <= -DeathPanelView.FrameHeight * .5f + .01f);
        Check("TAB: hangs BelowFrame under the frame", Mathf.Abs(-DeathPanelView.FrameHeight * .5f - tabRect.yMin - DoubleDustOffer.BelowFrame) < .01f);
        Check("TAB: caption is one short line", !DoubleDustOffer.Caption().Contains("\n") && DoubleDustOffer.Caption().Length <= 24);
        // Bold caps run about .75 em per glyph; at the minimum font size the whole caption still fits inside the plate.
        float needed = DoubleDustOffer.Caption().Length * DoubleDustOffer.MinFontSize * .75f;
        Check("TAB: caption fits one line at the minimum font size", needed <= DoubleDustOffer.Width - 2f * DoubleDustOffer.LabelInset);
        Check("TAB: minimum font size is at the 2 units per dp floor of 7 dp", DoubleDustOffer.MinFontSize >= UiScale.MinTextPt * 2f - .01f);
        for (int world = 0; world < 6; world++)
        {
            foreach (var r in DeathPanelView.ButtonRects(world))
                Check("TAB: world " + world + " does not overlap a button", !r.Overlaps(tabRect));
        }
        foreach (var sc in screens)
        {
            foreach (bool tab in new[] { false, true })
            {
                float banner = AdsConfig.BannerHeightPx(sc.dpi);
                float bannerTop = sc.safe.yMin + banner;   // conservative: the banner may sit above the safe inset
                var area = DeathPanelView.ReserveBottomForBanner(sc.safe, banner);
                DeathPanelView.ComputeFit(area, null, out var centre, out var scale, tab ? DoubleDustOffer.BelowFrame : 0f);
                float panelBottom = centre.y - DeathPanelView.Height * scale * .5f;
                float frameBottom = centre.y - DeathPanelView.FrameHeight * scale * .5f;
                float tabBottom = centre.y + tabRect.yMin * scale;
                float tabTop = centre.y + tabRect.yMax * scale;
                string tag = "LAYOUT " + sc.name + (tab ? " + tab" : "") + ": ";
                Check(tag + "panel and glow sit above the banner", panelBottom >= bannerTop && frameBottom >= bannerTop - 0.5f);
                if (tab)
                {
                    Check(tag + "the double dust button is above the banner", tabBottom >= bannerTop - 0.5f);
                    Check(tag + "the double dust button is inside the screen",
                        tabBottom >= 0f && tabTop <= sc.h && centre.x - DoubleDustOffer.Width * scale * .5f >= 0f && centre.x + DoubleDustOffer.Width * scale * .5f <= sc.w);
                    Check(tag + "the double dust button is below the frame", tabTop <= frameBottom + .5f);
                    Check(tag + "the double dust button is inside the safe area", tabBottom >= sc.safe.yMin - .5f);
                }
                Check(tag + "the panel stays inside the safe area's sides and top",
                    centre.y + DeathPanelView.FrameHeight * scale * .5f <= sc.safe.yMax + 0.5f && scale > .1f
                    && centre.x - DeathPanelView.Width * scale * .5f >= sc.safe.xMin - 0.5f && centre.x + DeathPanelView.Width * scale * .5f <= sc.safe.xMax + 0.5f);
            }
        }
    }

    static readonly string[] BoosterKeys = Boosters.AllKeys;

    // The wiring itself (not only the hook): the shipped default reads the real Boosters state, so a booster being active
    // really blocks forced ads. The field initializer runs in every build and in the editor, so no init order is involved.
    static void BoosterWiring(System.Func<bool> shippedDefault)
    {
        Check("WIRING: the shipped BoosterActiveCheck is the boosters check, not a stub",
              shippedDefault != null && shippedDefault.Method.DeclaringType == typeof(AdPolicy) && shippedDefault.Method.Name == "DefaultBoosterCheck");
        AdPolicy.BoosterActiveCheck = () => false;   // as a test left it
        AdPolicy.ResetBoosterCheck();
        Check("WIRING: no booster held -> BoosterActive is false", !AdPolicy.BoosterActive);
        // a hazard charge held does NOT block ads (Boosters.AnyActive), only the dust multiplier does
        PlayerPrefs.SetInt(Boosters.ClearBoughtKey, 2); Boosters.Reload();
        Check("WIRING: held hazard clear charges do not count as an active booster", !AdPolicy.BoosterActive);
        PlayerPrefs.SetInt(Boosters.DustBoughtKey, 1); Boosters.Reload();
        Check("WIRING: an active dust booster makes AdPolicy.BoosterActive true", AdPolicy.BoosterActive && Boosters.AnyActive);
        var inputs = Good();
        inputs.boosterActive = AdPolicy.BoosterActive;
        Check("WIRING: forced ads refuse with BoosterActive while the booster runs", AdPolicy.ForcedAd(inputs) == AdPolicy.Reason.BoosterActive && !AdPolicy.MayShowForcedAd(inputs));
        Check("WIRING: the live inputs the ad manager builds carry it", AdManager.LiveInputs().boosterActive);
        PlayerPrefs.SetInt(Boosters.DustUsedMsKey, (int)(Boosters.DustBoosterSeconds * 1000f)); Boosters.Reload();
        Check("WIRING: once the booster runs out ads are allowed again", !AdPolicy.BoosterActive && !AdManager.LiveInputs().boosterActive);
        foreach (string k in BoosterKeys) PlayerPrefs.DeleteKey(k);
        Boosters.Reload();
        AdPolicy.BoosterActiveCheck = () => false;
    }

    // Double dust pays on the unboosted base: booster + ad is 3x in total, never 4x.
    static void DoubleDustWithBooster()
    {
        float baseDust = 100f;
        Check("DOUBLE DUST: base run, ad pays 100 -> 200 (2x)", Mathf.Approximately(DoubleDustRules.Payout(baseDust, 0f), 100f));
        float boostedEarned = baseDust * Boosters.DustMultiplier(true, false);   // 200, booster extra 100
        Check("DOUBLE DUST: booster only earns 200 (2x)", Mathf.Approximately(boostedEarned, 200f));
        float pay = DoubleDustRules.Payout(boostedEarned, 100f);
        Check("DOUBLE DUST: booster + ad pays 100 (the base), not 200", Mathf.Approximately(pay, 100f));
        Check("DOUBLE DUST: booster + ad totals 3x the base, the Boosters cap",
              Mathf.Approximately(boostedEarned + pay, baseDust * Boosters.DustMultiplier(true, true)) && Boosters.MaxDustMultiplier == 3f);
        Check("DOUBLE DUST: ad only totals 2x", Mathf.Approximately(baseDust + DoubleDustRules.Payout(baseDust, 0f), baseDust * Boosters.DustMultiplier(false, true)));
        Check("DOUBLE DUST: unboosted score bonus in the base is doubled once (earned 230, extra 100 -> pays 130, total 360 <= 3x base)",
              Mathf.Approximately(DoubleDustRules.Payout(230f, 100f), 130f));
        Check("DOUBLE DUST: payout of nothing, non-finite or an extra above earned is 0",
              DoubleDustRules.Payout(0f, 0f) == 0f && DoubleDustRules.Payout(float.NaN, 0f) == 0f && DoubleDustRules.Payout(50f, 80f) == 0f);
        Boosters.ResetRun();
        Check("DOUBLE DUST: the payout base equals Boosters.RankedDust", Mathf.Approximately(Boosters.RankedDust(200f), 200f - Boosters.RunBoostExtra));
    }

    static void DoubleDust()
    {
        Check("bonus is a copy of what the run earned", Mathf.Approximately(DoubleDustRules.Bonus(12.345f), 12.35f) || Mathf.Approximately(DoubleDustRules.Bonus(12.345f), 12.34f));
        Check("bonus of nothing, negative or non-finite is 0", DoubleDustRules.Bonus(0f) == 0f && DoubleDustRules.Bonus(-4f) == 0f
            && DoubleDustRules.Bonus(float.NaN) == 0f && DoubleDustRules.Bonus(float.PositiveInfinity) == 0f);
        Check("a new death can claim", DoubleDustRules.CanClaim(500L, 0L) && DoubleDustRules.CanClaim(501L, 500L));
        Check("the paid death cannot claim again, nor an older one", !DoubleDustRules.CanClaim(500L, 500L) && !DoubleDustRules.CanClaim(499L, 500L));
        Check("a zero token cannot claim", !DoubleDustRules.CanClaim(0L, 0L));
        Check("tokens only go up, whatever the clock does", DoubleDustRules.NextToken(0L, 0L) == 1L && DoubleDustRules.NextToken(5L, 3L) == 6L && DoubleDustRules.NextToken(3L, 5L) == 6L);
        PlayerPrefs.DeleteKey(DoubleDustRules.DeathCounterKey);
        PlayerPrefs.SetString(DoubleDustRules.PaidTokenKey, "10");
        long t1 = PrefsRewardStore.NewDeathToken(), t2 = PrefsRewardStore.NewDeathToken();
        Check("stored death tokens are increasing and newer than the last paid death", t1 == 11L && t2 == 12L && DoubleDustRules.CanClaim(t1, 10L));
        Check("a paid death never blocks the next one", DoubleDustRules.CanClaim(PrefsRewardStore.NewDeathToken(), t2));

        // The prefs store credits dust and records the token together.
        PlayerPrefs.SetFloat(StarDustLedger.CurrencyKey, 10f);
        PlayerPrefs.DeleteKey(DoubleDustRules.PaidTokenKey);
        var store = new PrefsRewardStore();
        Check("store starts unpaid", store.LastPaidToken == 0L);
        store.Pay(777L, 2.5f);
        Check("store credits the dust and remembers the death", Mathf.Approximately(PlayerPrefs.GetFloat(StarDustLedger.CurrencyKey), 12.5f) && store.LastPaidToken == 777L);
    }

    // The results screen's star dust tick-up after the ad pays (DustTickUp, driven by DeathPanelView.AddDustWon).
    static void DustTickUpCases()
    {
        // Paid ad: the counter eases from the old amount and lands exactly on the doubled one.
        var r = new Rig(); r.BootReady();
        var tick = new DustTickUp();
        int shown = 10030;                        // 100.30 earned
        r.controller.TryShowRewarded(950L, 100.30f, extra => tick.Begin(shown, shown + Mathf.RoundToInt(extra * 100f), 5f), null);
        r.provider.lastEvents.opened();
        Check("TICKUP: nothing animates before the reward", !tick.Active);
        r.provider.lastEvents.earned(); r.provider.lastEvents.closed();
        Check("TICKUP: a paid ad starts the tick-up", tick.Active && tick.Target == 20060);
        Check("TICKUP: starts at the old value", tick.Value(5f) == 10030);
        int mid = tick.Value(5.3f);
        Check("TICKUP: counts up through the middle, eased out", mid > 10030 && mid < 20060 && mid > 10030 + (20060 - 10030) * .3f);
        Check("TICKUP: never overshoots and only climbs", tick.Value(5.6f) >= mid && tick.Value(5.9f) <= 20060);
        Check("TICKUP: ends exactly on the doubled amount", tick.Value(6.5f) == 20060 && !tick.Active);
        Check("TICKUP: stays on it after the tick", tick.Value(99f) == 20060);

        // Cancelled (closed without the reward) and failed ads never animate.
        var t2 = new DustTickUp(); int calls = 0;
        r = new Rig(); r.BootReady();
        r.controller.TryShowRewarded(951L, 8f, extra => { calls++; t2.Begin(800, 1600, 0f); }, null);
        r.provider.lastEvents.opened(); r.provider.lastEvents.closed();
        Check("TICKUP: a cancelled ad does not animate or double", calls == 0 && !t2.Active);
        r = new Rig(); r.BootReady();
        r.controller.TryShowRewarded(952L, 8f, extra => { calls++; t2.Begin(800, 1600, 0f); }, null);
        r.provider.lastEvents.failed("no fill");
        Check("TICKUP: a failed ad does not animate or double", calls == 0 && !t2.Active);

        // Teardown mid-animation is safe and leaves the true amount; an empty tick is a no-op.
        var t3 = new DustTickUp();
        t3.Begin(500, 1000, 0f);
        t3.Value(.4f);
        bool threw = false; int left = 0;
        try { left = t3.Finish(); t3.Finish(); } catch (Exception) { threw = true; }
        Check("TICKUP: leaving mid-animation does not throw and holds the true amount", !threw && left == 1000 && !t3.Active && t3.Value(.5f) == 1000);
        var idle = new DustTickUp();
        Check("TICKUP: an unused ticker is idle and harmless", !idle.Active && idle.Value(1f) == 0 && idle.Finish() == 0);
        t3.Begin(700, 700, 0f);
        Check("TICKUP: no change, no animation", !t3.Active);
    }

    // The results "2x STAR DUST" button: gone once the ad was shown, however the reward and the close fall out.
    static void DoubleDustButton()
    {
        long tok = 8100L;
        DoubleDustOffer.ButtonState Make(Rig r, float[] paid)
        {
            var c = r.controller; long t = ++tok;
            return new DoubleDustOffer.ButtonState((p, d) => c.TryShowRewarded(t, 4f, p, d), () => c.WatchedRewardedAtDeath, x => paid[0] += x);
        }
        Rig Fresh() { var rg = new Rig(); rg.BootReady(); return rg; }

        var r1 = Fresh(); var paid1 = new float[1]; var b = Make(r1, paid1);
        Check("DDBTN: starts visible", b.Visible);
        b.Click(); r1.provider.lastEvents.opened(); r1.provider.lastEvents.earned(); r1.provider.lastEvents.closed();
        Check("DDBTN: reward then close -> gone, paid once", !b.Visible && b.Used && paid1[0] > 0f && r1.store.pays == 1);

        var r2 = Fresh(); var paid2 = new float[1]; b = Make(r2, paid2);
        b.Click(); r2.provider.lastEvents.opened(); r2.provider.lastEvents.closed();
        Check("DDBTN: close before reward -> gone already", !b.Visible);
        r2.provider.lastEvents.earned();
        Check("DDBTN: late reward -> stays gone", !b.Visible && b.Used);

        var r3 = Fresh(); b = Make(r3, new float[1]);
        b.Click(); r3.provider.lastEvents.opened(); r3.provider.lastEvents.closed();
        Check("DDBTN: close without reward -> gone, no second offer", !b.Visible && r3.store.pays == 0);
        for (int i = 0; i < 3; i++) { r3.provider.rewardedReady = true; Check("DDBTN: re-layout " + i + " keeps it gone and a tap shows nothing", !b.Visible && !b.Click()); }

        var r4 = Fresh(); b = Make(r4, new float[1]);
        b.Click(); Check("DDBTN: hidden while the ad starts", !b.Visible);
        r4.provider.lastEvents.failed("no fill");
        Check("DDBTN: open failure -> button back, retry allowed", b.Visible);

        var r5 = Fresh(); b = Make(r5, new float[1]);
        bool first = b.Click(); bool second = b.Click();
        Check("DDBTN: double tap -> one show", first && !second && r5.provider.rewardedShows == 1);
    }

    static void Controller()
    {
        // ---- disabled: nothing is touched ----
        var r = new Rig { enabled = false };
        r.controller.Boot(); r.controller.Tick(); r.controller.OnRunEnd(true);
        Check("disabled: no consent, init or load", r.provider.consentCalls == 0 && r.provider.initCalls == 0 && r.provider.interstitialLoads == 0 && r.provider.rewardedLoads == 0);
        r.provider.interstitialReady = true; r.provider.rewardedReady = true;
        bool proceeded = false;
        Check("disabled: no interstitial, no rewarded, not initialised",
            !r.controller.TryShowInterstitial(Good(), () => proceeded = true) && !r.controller.TryShowRewarded(1L, 5f, null, null)
            && !r.controller.Initialised && !r.controller.InterstitialReady && !r.controller.RewardedReady && r.provider.interstitialShows == 0);

        // ---- boot: consent -> tracking -> init -> loads ----
        r = new Rig();
        r.controller.Boot();
        Check("boot runs consent, then tracking, then init, in order", r.provider.consentCalls == 1 && r.provider.trackingCalls == 1 && r.provider.initCalls == 1 && r.controller.Initialised);
        Check("no load before the first tick", r.provider.interstitialLoads == 0);
        r.controller.OnRunStart();
        r.controller.Tick();
        Check("no load request while a run is on", r.provider.interstitialLoads == 0 && r.provider.rewardedLoads == 0);
        r.controller.OnRunEnd(true);
        r.controller.Tick();
        Check("loads after the run ends", r.provider.interstitialLoads == 1 && r.provider.rewardedLoads == 1 && r.controller.InterstitialReady && r.controller.RewardedReady);
        r.controller.Tick();
        Check("no reload while an ad is loaded", r.provider.interstitialLoads == 1);

        // ---- consent denied / slow ----
        r = new Rig(); r.provider.canRequestAds = false;
        r.controller.Boot(); r.controller.Tick();
        Check("no consent: the SDK is never initialised, nothing loads", r.provider.initCalls == 0 && r.provider.interstitialLoads == 0 && !r.controller.Initialised);
        r = new Rig(); r.provider.autoCompleteCallbacks = false;
        r.controller.Boot();
        r.now += AdController.ConsentTimeoutSeconds + 1f; r.controller.Tick();
        r.provider.pendingConsent(true);
        Check("consent that never answers times out, a late answer is ignored", r.provider.initCalls == 0 && !r.controller.Initialised);

        // ---- the player reading the consent form is not a timeout ----
        r = new Rig(); r.provider.autoCompleteCallbacks = false; r.provider.consentFormShowing = true;
        r.controller.Boot();
        r.now += AdController.ConsentTimeoutSeconds * 4f; r.controller.Tick();
        r.now += AdController.ConsentTimeoutSeconds * 4f; r.controller.Tick();
        r.provider.consentFormShowing = false;
        r.provider.pendingConsent(true);
        r.provider.autoCompleteCallbacks = true; r.provider.pendingInit();
        Check("consent form on screen for minutes: still waiting, and consenting initialises ads", r.provider.initCalls == 1 && r.controller.Initialised);
        r = new Rig(); r.provider.autoCompleteCallbacks = false; r.provider.consentFormShowing = true;
        r.controller.Boot(); r.controller.Tick();
        r.provider.consentFormShowing = false;
        r.now += AdController.ConsentTimeoutSeconds - 1f; r.controller.Tick();
        Check("the timeout clock restarts when the form closes", r.provider.pendingConsent != null && !r.controller.Initialised);
        r.now += 2f; r.controller.Tick(); r.provider.pendingConsent(true);
        Check("a form load that then hangs still times out", r.provider.initCalls == 0);
        r = new Rig(); r.provider.canRequestAds = false; r.provider.consentFormShowing = true;
        r.controller.Boot(); r.controller.Tick();
        Check("declining consent keeps ads blocked", r.provider.initCalls == 0 && !r.controller.Initialised);

        // ---- load failure backs off ----
        r = new Rig(); r.provider.loadSucceeds = false;
        r.BootReady();
        Check("failed load: one try now", r.provider.interstitialLoads == 1);
        r.controller.Tick();
        Check("failed load: waits before retrying", r.provider.interstitialLoads == 1);
        r.now += AdController.FirstRetrySeconds + 1f; r.controller.Tick();
        Check("failed load: retries after the backoff", r.provider.interstitialLoads == 2);

        // ---- interstitial: closes normally ----
        r = new Rig(); r.BootReady();
        int proceeds = 0;
        Check("interstitial takes over when allowed and loaded", r.controller.TryShowInterstitial(Good(), () => proceeds++) && r.provider.interstitialShows == 1 && r.controller.Busy);
        Check("replay waits for the ad", proceeds == 0);
        Check("a second show is refused while busy", !r.controller.TryShowInterstitial(Good(), () => proceeds += 100));
        r.provider.lastEvents.opened();
        Check("audio and time are suspended while the ad is up", r.env.suspends == 1 && r.env.resumes == 0);
        r.provider.lastEvents.closed();
        r.provider.lastEvents.closed();
        Check("closing continues exactly once and restores audio and time", proceeds == 1 && r.env.resumes == 1 && !r.controller.Busy);
        Check("the next ad is requested right away", r.provider.interstitialLoads == 2);
        Check("3 minute rule starts at the close", r.controller.SecondsSinceLastAd < 1f);
        Check("an immediate second interstitial is blocked by the gap",
            !r.controller.TryShowInterstitial(Good(), () => proceeds += 100) && r.provider.interstitialShows == 1);
        r.now += AdPolicy.MinSecondsBetweenAds + 1f;
        Check("after the gap it may show again", r.controller.TryShowInterstitial(Good(), () => proceeds++));
        r.provider.lastEvents.closed();

        // ---- interstitial: policy says no, or none loaded ----
        r = new Rig(); r.BootReady();
        var blocked = Good(); blocked.removeAdsOwned = true;
        Check("policy no: false, no ad shown", !r.controller.TryShowInterstitial(blocked, () => { }) && r.provider.interstitialShows == 0);
        r.provider.interstitialReady = false;
        Check("no ad loaded: false at once, Replay not delayed", !r.controller.TryShowInterstitial(Good(), () => { }));

        // ---- interstitial that never opens ----
        r = new Rig(); r.BootReady(); proceeds = 0;
        r.controller.TryShowInterstitial(Good(), () => proceeds++);
        r.now += AdController.OpenTimeoutSeconds - 1f; r.controller.Tick();
        Check("still waiting inside the open timeout", proceeds == 0 && r.controller.Busy);
        r.now += 2f; r.controller.Tick();
        Check("never opened: Replay carries on after the timeout", proceeds == 1 && !r.controller.Busy && r.env.suspends == 0);
        r.provider.lastEvents.opened(); r.provider.lastEvents.closed();
        Check("a late-opening abandoned ad is muted while up and restored on close", proceeds == 1 && r.env.suspends == 1 && r.env.resumes == 1);
        Check("an abandoned ad starts the 3 minute clock (it may still appear)", r.controller.SecondsSinceLastAd < 1f);

        // ---- interstitial that fails ----
        r = new Rig(); r.BootReady(); proceeds = 0;
        r.controller.TryShowInterstitial(Good(), () => proceeds++);
        r.provider.lastEvents.failed("no fill");
        r.provider.lastEvents.closed();
        Check("a failed show continues exactly once", proceeds == 1 && !r.controller.Busy);

        // ---- interstitial that opens and never closes ----
        r = new Rig(); r.BootReady(); proceeds = 0;
        r.controller.TryShowInterstitial(Good(), () => proceeds++);
        r.provider.lastEvents.opened();
        r.now += AdController.OpenWatchdogSeconds + 1f; r.controller.Tick();
        Check("an ad that never reports closing cannot trap the player", proceeds == 1 && r.env.resumes == 1);

        // ---- provider that throws ----
        var thrower = new ThrowingShow();
        r = new Rig(thrower); r.BootReady(); proceeds = 0;
        Check("a provider that throws still gives Replay back", r.controller.TryShowInterstitial(Good(), () => proceeds++) && proceeds == 1 && !r.controller.Busy);

        // ---- rewarded ----
        r = new Rig(); r.BootReady();
        float paid = 0f; int paidCalls = 0, doneCalls = 0;
        Check("rewarded starts", r.controller.TryShowRewarded(900L, 7.5f, extra => { paid += extra; paidCalls++; }, () => doneCalls++));
        r.provider.lastEvents.opened();
        Check("opening the rewarded ad marks this death as watched", r.controller.WatchedRewardedAtDeath);
        Check("nothing is paid before the reward is earned", r.store.pays == 0);
        r.provider.lastEvents.earned();
        r.provider.lastEvents.earned();
        Check("reward pays exactly once, a copy of the run's dust", r.store.pays == 1 && Mathf.Approximately(r.store.total, 7.5f) && paidCalls == 1 && Mathf.Approximately(paid, 7.5f));
        r.provider.lastEvents.closed();
        Check("closing finishes the flow and restores audio and time", doneCalls == 1 && r.env.resumes == 1 && !r.controller.Busy);
        r.provider.rewardedReady = true;
        Check("the same death cannot be doubled twice", !r.controller.TryShowRewarded(900L, 7.5f, null, null));
        Check("an older death cannot be doubled", !r.controller.TryShowRewarded(899L, 7.5f, null, null));
        var afterReward = Good();
        Check("no forced ad after the rewarded ad at this death", !r.controller.TryShowInterstitial(afterReward, () => { }));
        r.controller.OnDeathPanel();
        Check("a new death panel clears the watched flag", !r.controller.WatchedRewardedAtDeath);

        r = new Rig(); r.BootReady(); paidCalls = 0;
        r.controller.TryShowRewarded(901L, 4f, x => paidCalls++, null);
        r.provider.lastEvents.opened(); r.provider.lastEvents.closed();
        Check("closed without earning: nothing is paid", r.store.pays == 0 && paidCalls == 0);
        r.provider.rewardedReady = true;
        Check("and the death can still be doubled later", r.controller.TryShowRewarded(901L, 4f, null, null));

        r = new Rig(); r.BootReady();
        r.provider.rewardedReady = false;
        Check("no rewarded ad loaded: false (the button stays hidden)", !r.controller.TryShowRewarded(902L, 4f, null, null) && !r.controller.RewardedReady);
        r.provider.rewardedReady = true;
        Check("nothing earned, nothing to double", !r.controller.TryShowRewarded(903L, 0f, null, null));

        r = new Rig(); r.BootReady(); r.store.paid = 905L;
        Check("a death the store already paid is refused", !r.controller.TryShowRewarded(905L, 4f, null, null));

        // ---- privacy options ----
        r = new Rig(); r.provider.privacyOptionsRequired = true; r.BootReady();
        bool reopened = false;
        Check("privacy options entry follows UMP", r.controller.PrivacyOptionsRequired);
        r.controller.ShowPrivacyOptions(() => reopened = true);
        Check("privacy options calls back", reopened);
        r = new Rig(); r.BootReady();
        Check("no entry when UMP does not require one", !r.controller.PrivacyOptionsRequired);
    }

    class ThrowingShow : StubAdProvider
    {
        public override void ShowInterstitial(AdShowEvents e) { throw new InvalidOperationException("boom"); }
    }

    static void Environment()
    {
        var env = new AdEnvironment();
        bool pause = AudioListener.pause; float vol = AudioListener.volume;
        AudioListener.volume = 0.6f;
        Time.timeScale = 0.5f;
        env.Suspend();
        Check("ad up: audio paused and muted, time frozen", AudioListener.pause && AudioListener.volume == 0f && Time.timeScale == 0f);
        env.Suspend();
        env.Resume();
        Check("ad down: audio and time come back exactly", AudioListener.pause == pause && Mathf.Approximately(AudioListener.volume, 0.6f) && Mathf.Approximately(Time.timeScale, 0.5f));
        env.Resume();
        Check("a second resume changes nothing", Mathf.Approximately(AudioListener.volume, 0.6f));
        AudioListener.volume = vol;
    }

    static void ManifestGuard()
    {
        Check("manifest guard strips only while the master switch is off and not a dev build",
            AdsManifestGuard.ShouldStrip(false, false) && !AdsManifestGuard.ShouldStrip(true, false)
            && !AdsManifestGuard.ShouldStrip(false, true) && !AdsManifestGuard.ShouldStrip(true, true));
        const string manifest = "<?xml version=\"1.0\" encoding=\"utf-8\"?><manifest xmlns:android=\"http://schemas.android.com/apk/res/android\" package=\"x\"><uses-permission android:name=\"android.permission.INTERNET\"/><application/></manifest>";
        string once = AdsManifestGuard.Strip(manifest);
        foreach (string p in AdsManifestGuard.RemovedPermissions)
            Check("guard removes " + p, once.Contains("android:name=\"" + p + "\"") && once.Contains("tools:node=\"remove\""));
        Check("guard removes the SDK's init provider and foreground service", once.Contains(AdsManifestGuard.RemovedProvider) && once.Contains(AdsManifestGuard.RemovedService));
        Check("guard keeps the app's own permissions", once.Contains("android.permission.INTERNET") && once.Contains("xmlns:tools"));
        Check("guard is idempotent", AdsManifestGuard.Strip(once) == once);
    }

    static void BuildSync()
    {
        AdsBuildSync.Sync(false);
        var asset = AssetDatabase.LoadMainAssetAtPath(AdsBuildSync.SettingsPath);
        Check("the plugin settings asset exists", asset != null);
        if (asset == null) return;
        var so = new SerializedObject(asset);
        Check("settings: Android App ID comes from AdsConfig", so.FindProperty("adMobAndroidAppId").stringValue == AdsConfig.AppIdFor(false, false));
        Check("settings: iOS App ID comes from AdsConfig", so.FindProperty("adMobIOSAppId").stringValue == AdsConfig.AppIdFor(true, false));
        Check("settings: ATT text comes from AdsConfig", so.FindProperty("userTrackingUsageDescription").stringValue == AdsConfig.TrackingUsageDescription);
        Check("settings: the plugin does not add a second dependency manager", !so.FindProperty("enableExternalDependencyManager").boolValue);
        Check("a second sync changes nothing", !AdsBuildSync.Sync(false));
        Check("a dev build syncs the test App IDs", AdsBuildSync.Sync(true) && new SerializedObject(asset).FindProperty("adMobAndroidAppId").stringValue == AdsConfig.TestAndroidAppId);
        AdsBuildSync.Sync(false);
    }
}
