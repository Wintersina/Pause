using UnityEngine;

// The permanent "ad-free" entitlement, bought with the one-time Remove Ads
// product (RemoveAdsPurchase). This is the one read-only switch that future ad
// code checks before it shows anything:
//
//     if (AdFree.IsActive) skip the ad
//
// It lives in PlayerPrefs under Key and is part of the cloud save
// (ProgressSnapshot.adFree, merged "owned on either side wins" and never
// cleared by a restore), so a purchase can never be lost. Only the purchase
// handler (and the cloud apply) write it, through Grant.
public static class AdFree
{
    public const string Key = "adFreeOwned";

    public static bool IsActive { get { return PlayerPrefs.GetInt(Key, 0) != 0; } }

    // Marks the entitlement owned and saves at once. Idempotent; true when it was new.
    internal static bool Grant()
    {
        if (IsActive) return false;
        PlayerPrefs.SetInt(Key, 1);
        PrefsSaver.SaveNow();
        return true;
    }
}
