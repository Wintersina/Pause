using System;

// When a FORCED ad (the interstitial between runs) may show. Pure: every input is a
// parameter, nothing reads PlayerPrefs, time or the scene, so every rule is tested.
//
// A forced ad never shows when ANY of these holds (checked in this order; the first
// that holds is the Reason):
//   - the player owns Remove Ads (AdFree.IsActive)
//   - under AdFreePlaySeconds (15 minutes) of play
//   - a booster is active (BoosterActiveCheck)
//   - less than MinSecondsBetweenAds since the last ad of ANY kind (interstitial or rewarded)
//   - the player watched the rewarded ad at this death
//   - it is the tutorial or a practice run
//
// Paragon does NOT remove ads (user decision 2026-11-08): only the Remove Ads purchase does. Run length is no gate
// either: every run end counts toward RunsPerInterstitial (see CountsRunEnd).
//
// The OPTIONAL rewarded "double dust" offer is a separate question (DoubleDustOffered):
// Remove Ads owners still get it while RemoveAdsOwnersSeeDoubleDust is true.
public static class AdPolicy
{
    public const float AdFreePlaySeconds = 900f;
    public const float MinSecondsBetweenAds = 180f;

    // EVERY run end (however short) that CountsRunEnd counts; the Nth since the last forced ad is due. If it cannot show right then
    // (not loaded, booster, inside MinSecondsBetweenAds) it stays owed and shows at the next run end where it can.
    public const int RunsPerInterstitial = 3;

    // Every Replay press (results screen, pause menu, quick actions), whatever the run length, counts; the Nth since the last forced ad was shown gets a
    // full interstitial before the new run starts. Shown replay ads and run-end ads reset each other's counters.
    public const int ReplaysPerForcedAd = 3;

    // Default the user has not confirmed (tracker open question 3).
    public const bool RemoveAdsOwnersSeeDoubleDust = true;

    // A booster being active blocks forced ads ("no forced ads while a booster is active"). Wired to the boosters by
    // the field initializer itself, so it holds in every build and in edit-mode tests, with no init order to get wrong.
    // Tests may replace the hook; ResetBoosterCheck puts the shipped wiring back.
    public static Func<bool> BoosterActiveCheck = DefaultBoosterCheck;

    public static bool DefaultBoosterCheck() { return Boosters.AnyActive; }

    public static void ResetBoosterCheck() { BoosterActiveCheck = DefaultBoosterCheck; }

    public static bool BoosterActive
    {
        get
        {
            var check = BoosterActiveCheck;
            if (check == null) return false;
            try { return check(); } catch (Exception) { return false; }   // a faulty hook must not turn ads on or break the death flow
        }
    }

    public enum Reason
    {
        Allowed,
        RemoveAdsOwned,
        UnderPlayTime,
        BoosterActive,
        TooSoonAfterAd,
        WatchedRewardedAtDeath,
        TutorialOrPractice,
    }

    public struct Inputs
    {
        public bool removeAdsOwned;
        public float playSeconds;            // lifetime unfrozen real play (PlayTime)
        public bool boosterActive;
        public float secondsSinceLastAd;     // any ad; float.MaxValue / infinity when none was shown yet
        public bool watchedRewardedAtDeath;
        public bool tutorialOrPractice;
    }

    public static Reason ForcedAd(Inputs i)
    {
        if (i.removeAdsOwned) return Reason.RemoveAdsOwned;
        if (i.playSeconds < AdFreePlaySeconds) return Reason.UnderPlayTime;
        if (i.boosterActive) return Reason.BoosterActive;
        if (i.secondsSinceLastAd < MinSecondsBetweenAds) return Reason.TooSoonAfterAd;
        if (i.watchedRewardedAtDeath) return Reason.WatchedRewardedAtDeath;
        if (i.tutorialOrPractice) return Reason.TutorialOrPractice;
        return Reason.Allowed;
    }

    public static bool MayShowForcedAd(Inputs i) { return ForcedAd(i) == Reason.Allowed; }

    // Whether the optional double dust button may be offered (before checking an ad is loaded).
    public static bool DoubleDustOffered(bool removeAdsOwned, bool tutorialOrPractice, float dustEarned)
    {
        if (tutorialOrPractice || !(dustEarned > 0f)) return false;
        return !removeAdsOwned || RemoveAdsOwnersSeeDoubleDust;
    }

    // Whether a Replay press counts toward ReplaysPerForcedAd (and may show the ad): the same exemptions as forced ads
    // (Remove Ads, the first 15 minutes) -- but not run length, cooldown, booster or tutorial, which is the point.
    public static bool ReplayCounts(Inputs i)
    {
        if (i.removeAdsOwned) return false;
        return i.playSeconds >= AdFreePlaySeconds;
    }

    public static bool ReplayAdDue(int replaysSinceAd) { return replaysSinceAd >= ReplaysPerForcedAd; }

    // ---- run-end ad choice: interstitial, banner or nothing ----

    public enum RunEndAd { None, Interstitial, Banner }

    // Whether a run end counts toward RunsPerInterstitial: the replay exemptions plus not the tutorial / practice. Run length,
    // booster and cooldown do not matter here (they only hold the ad back; the count stays owed).
    public static bool CountsRunEnd(Inputs i) { return ReplayCounts(i) && !i.tutorialOrPractice; }

    // `runEndsSinceAd` counts run ends since the last forced interstitial; the Nth (or later: owed) is due.
    public static bool CadenceDue(int runEndsSinceAd) { return runEndsSinceAd >= RunsPerInterstitial; }

    // A banner is for run ends the player could see ads at: not Remove Ads, not inside the
    // first 15 ad-free minutes, and never the tutorial or practice. The rest of the forced-ad guards (booster, min gap, short
    // run, watched the rewarded ad) only hold back the interstitial, not the small banner.
    public static bool BannerAllowed(Inputs i)
    {
        if (i.removeAdsOwned) return false;
        if (i.playSeconds < AdFreePlaySeconds) return false;
        if (i.tutorialOrPractice) return false;
        return true;
    }

    // The same banner rules for a menu screen (Credits): no tutorial / practice notion there, everything else as BannerAllowed.
    public static bool MenuBannerAllowed(Inputs i)
    {
        i.tutorialOrPractice = false;
        return BannerAllowed(i);
    }

    // What this run end gets. `eligibleRunNumber` counts this run when CountsRunEnd (the caller owns the counter).
    // When the interstitial is due but not loaded the run end falls back to a banner.
    public static RunEndAd ChooseRunEndAd(Inputs i, int eligibleRunNumber, bool interstitialReady, bool bannerAvailable)
    {
        if (ForcedAd(i) == Reason.Allowed && CadenceDue(eligibleRunNumber) && interstitialReady) return RunEndAd.Interstitial;
        return bannerAvailable && BannerAllowed(i) ? RunEndAd.Banner : RunEndAd.None;
    }
}
