using System;

// When a FORCED ad (the interstitial between runs) may show. Pure: every input is a
// parameter, nothing reads PlayerPrefs, time or the scene, so every rule is tested.
//
// A forced ad never shows when ANY of these holds (checked in this order; the first
// that holds is the Reason):
//   - the player owns Remove Ads (AdFree.IsActive)
//   - the player owns Paragon, while ParagonRemovesAds is true (default; unconfirmed)
//   - under AdFreePlaySeconds (1 hour) of play
//   - a booster is active (BoosterActiveCheck)
//   - less than MinSecondsBetweenAds since the last ad of ANY kind (interstitial or rewarded)
//   - the run just finished was shorter than MinRunSecondsForAd
//   - the player watched the rewarded ad at this death
//   - it is the tutorial or a practice run
//
// The OPTIONAL rewarded "double dust" offer is a separate question (DoubleDustOffered):
// Remove Ads owners still get it while RemoveAdsOwnersSeeDoubleDust is true.
public static class AdPolicy
{
    public const float AdFreePlaySeconds = 3600f;
    public const float MinSecondsBetweenAds = 180f;
    public const float MinRunSecondsForAd = 45f;

    // Defaults the user has not confirmed (tracker open questions 2 and 3): one constant each.
    public const bool ParagonRemovesAds = true;
    public const bool RemoveAdsOwnersSeeDoubleDust = true;

    // Boosters are built elsewhere; they set this so a booster being active blocks forced ads.
    // Default: no booster is ever active.
    public static Func<bool> BoosterActiveCheck = () => false;

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
        ParagonOwned,
        UnderPlayTime,
        BoosterActive,
        TooSoonAfterAd,
        RunTooShort,
        WatchedRewardedAtDeath,
        TutorialOrPractice,
    }

    public struct Inputs
    {
        public bool removeAdsOwned;
        public bool paragonOwned;
        public float playSeconds;            // lifetime unfrozen real play (PlayTime)
        public bool boosterActive;
        public float secondsSinceLastAd;     // any ad; float.MaxValue / infinity when none was shown yet
        public float runSeconds;             // the run that just ended
        public bool watchedRewardedAtDeath;
        public bool tutorialOrPractice;
    }

    public static Reason ForcedAd(Inputs i)
    {
        if (i.removeAdsOwned) return Reason.RemoveAdsOwned;
        if (ParagonRemovesAds && i.paragonOwned) return Reason.ParagonOwned;
        if (i.playSeconds < AdFreePlaySeconds) return Reason.UnderPlayTime;
        if (i.boosterActive) return Reason.BoosterActive;
        if (i.secondsSinceLastAd < MinSecondsBetweenAds) return Reason.TooSoonAfterAd;
        if (i.runSeconds < MinRunSecondsForAd) return Reason.RunTooShort;
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
}
