using UnityEngine;
using UnityEngine.SceneManagement;

// "Everyone who paid for Pause gets Remove Ads free": the one-time existing-buyer grant for the paid -> free switch
// (docs/monetization/go-free-checklist.md). Android only: iOS has never been sold, so it has no paying buyers.
//
// LOCAL RULE, once per install, at the very first launch of a build containing this code and BEFORE the first scene
// (so before any cloud restore and before the pre-registration reward can add dust): a device that already has real
// progress (tutorial done, anything owned beyond the starter, any star dust, a reached world, a best score) is an
// existing buyer and AdFree is granted. A brand-new install has none of that at that moment, so a new free player is
// never granted. The result is stored under MarkerKey and never re-evaluated, so later progress cannot grant it.
//
// OPTIONAL PAID-ERA CLAUSE: PaidEraEndsUtc (unix seconds, 0 = off) covers someone who bought AFTER updating to this
// build but BEFORE the price flip and has no progress yet. While the first launch is earlier than that instant the
// install is a paid install, so it is granted as well. Set it in the release that carries this code to the planned
// flip time; leave 0 and that small gap is simply not covered.
//
// CLOUD RULE: a cloud save written by a build WITHOUT this code (ProgressSnapshot.adsEra false) with progress can only
// have been written in the paid era, so applying it grants too. That recognises a buyer who reinstalled and restores
// from their account. A save from this build or later (adsEra true) is trusted for adFree only.
public static class ExistingBuyer
{
    public const string MarkerKey = "adFreeBuyerRuleDone";
    public const string ToastDueKey = "adFreeBuyerToastDue";
    public const string Message = "THANKS FOR BUYING PAUSE: ADS ARE OFF";

    // Unix seconds the paid era ends; 0 = the paid-era clause is off. Set at release time (see the checklist).
    public const long PaidEraEndsUtc = 0;

    // Test override: null = the real platform (Android only).
    public static bool? ForcePlatformSoldPaid;
    static bool PlatformSoldPaid { get { return ForcePlatformSoldPaid ?? (Application.platform == RuntimePlatform.Android); } }

    // ---- pure rules ----

    // A device snapshot taken before anything this session could change it.
    public static bool IsExistingBuyer(ProgressSnapshot local, long nowUnix, long paidEraEnds)
    {
        if (local != null && (local.adFree || local.hasDoneTut || local.HasProgress())) return true;
        return paidEraEnds > 0 && nowUnix < paidEraEnds;
    }

    // A downloaded cloud save: legacy (no adsEra) and not empty.
    public static bool CloudSaveGrants(ProgressSnapshot cloud)
    {
        if (cloud == null || cloud.adsEra) return false;
        return cloud.adFree || cloud.hasDoneTut || cloud.HasProgress();
    }

    // ---- wiring ----

    public static bool Evaluated { get { return PlayerPrefs.GetInt(MarkerKey, 0) != 0; } }

    // Runs the local rule once. Returns true when it granted just now.
    public static bool EnsureEvaluated(long nowUnix)
    {
        if (!PlatformSoldPaid || Evaluated) return false;
        bool buyer = IsExistingBuyer(ProgressSnapshot.Capture(0), nowUnix, PaidEraEndsUtc);
        PlayerPrefs.SetInt(MarkerKey, 1);
        bool granted = false;
        if (buyer && AdFree.Grant())
        {
            granted = true;
            PlayerPrefs.SetInt(ToastDueKey, 1);
        }
        PrefsSaver.SaveNow();
        Debug.Log("[ExistingBuyer] first launch: " + (buyer ? "existing buyer, ads off." : "new install, nothing granted."));
        return granted;
    }

    // Called by CloudSync with the downloaded save; true when it granted the entitlement.
    public static bool ApplyCloud(ProgressSnapshot cloud)
    {
        if (!PlatformSoldPaid || !CloudSaveGrants(cloud)) return false;
        if (!AdFree.Grant()) return false;
        PlayerPrefs.SetInt(ToastDueKey, 1);
        PrefsSaver.SaveNow();
        TryToast(SceneManager.GetActiveScene().name);
        return true;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Init()
    {
        EnsureEvaluated(System.DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    static void OnSceneLoaded(Scene scene, LoadSceneMode mode) { TryToast(scene.name); }

    // Once, on the home screen, with the existing hint-toast plate.
    public static bool TryToast(string sceneName)
    {
        if (sceneName != BackNavigator.HomeScene || PlayerPrefs.GetInt(ToastDueKey, 0) == 0) return false;
        PlayerPrefs.DeleteKey(ToastDueKey);
        PrefsSaver.SaveNow();
        AccountHintToast.Show(Message);
        return true;
    }
}
