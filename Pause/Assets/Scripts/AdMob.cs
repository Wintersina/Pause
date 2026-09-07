using UnityEngine;

// Ad placeholder.
//
// The 2016 Google Mobile Ads SDK this wrapped (play-services-ads 10.0.1) was
// removed during the Unity 6 upgrade -- it no longer builds. The call surface
// below is kept intact so the ~20 existing call sites still compile and the
// game's show/hide logic keeps working; every call is currently a no-op.
//
// To bring ads back: install the current Google Mobile Ads Unity plugin, then
// fill in RequestBanner/show/hide here. Nothing outside this file needs to change.
//
// Original ad unit id (Android banner), kept for reference when re-wiring:
//   ca-app-pub-6947554333794592/9092756042
public class AdMob : MonoBehaviour
{
    // Note: name kept (typo and all) -- referenced across the project.
    public static bool isAdsShowwing = false;

    static bool isAdLoaded;

    void Start()
    {
        RequestBanner();
    }

    // Was called every frame from Update() until a banner loaded; a single
    // Start() call is enough and avoids the per-frame retry.
    static void RequestBanner()
    {
        if (isAdLoaded) return;
        isAdLoaded = true;
        isAdsShowwing = false;
        Debug.Log("[AdMob] Ads are stubbed out; no banner requested.");
    }

    public static void hide()
    {
        isAdsShowwing = false;
    }

    public static void show()
    {
        isAdsShowwing = true;
    }
}
