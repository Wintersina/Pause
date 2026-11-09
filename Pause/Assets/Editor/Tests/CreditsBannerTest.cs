using UnityEngine;
using UnityEngine.UI;

// The Credits banner ad: requested when eligible, never for Remove Ads owners or inside the ad-free window, taken down when
// the screen closes, and the reserved bottom strip never overlaps a button (BACK) on a spread of screens.
public static class CreditsBannerTest
{
    static int fails;
    static void Check(string what, bool ok)
    {
        Debug.Log((ok ? "[CREDBANNER] PASS  " : "[CREDBANNER] FAIL  ") + what);
        if (!ok) fails++;
    }

    static readonly string[] Devices =
    {
        "flip7-1080x2520", "and-1080x1920", "and-480x854", "tab-1600x2560", "fold-1812x2176", "ipad-9", "iphone-se",
    };

    static AdPolicy.Inputs Good()
    {
        return new AdPolicy.Inputs { playSeconds = AdPolicy.AdFreePlaySeconds + 1f, secondsSinceLastAd = float.MaxValue };
    }

    static StubAdProvider Install()
    {
        var provider = new StubAdProvider();
        var controller = new AdController(provider, () => 100f, () => true, new Env(), new Store());
        AdManager.Install(controller);
        controller.Tick();
        return provider;
    }

    class Env : IAdEnvironment { public void Suspend() { } public void Resume() { } }
    class Store : IRewardStore
    {
        public long LastPaidToken { get { return 0; } }
        public void Pay(long token, float amount) { }
        public void Grant(float amount) { }
    }

    public static int Execute()
    {
        fails = 0;
        using var sandbox = new TestHarness.Sandbox();
        PlayerPrefs.SetString("HasDoneTut", "true");
        try
        {
            DeveloperUnlocks.TestAvailableOverride = null;
            DeveloperUnlocks.SetEnabled(true);   // ads on in the editor; skips the ad-free window in LiveInputs
            Eligibility();
            Lifecycle();
            foreach (var id in Devices) Layout(id);
        }
        finally { AdManager.Reset(); DeveloperUnlocks.SetEnabled(false); DeveloperUnlocks.TestAvailableOverride = null; }
        Debug.Log("[CREDBANNER] failures: " + fails);
        return fails;
    }

    static void Eligibility()
    {
        AdManager.Reset();
        var p = Install();
        Check("eligible: Credits requests the banner", AdManager.TryShowMenuBanner(Good()) && p.bannerUp && p.bannerShows == 1);
        AdManager.HideBanner();
        Check("hiding takes the banner down", !p.bannerUp && !AdManager.BannerShowing);

        var owner = Good(); owner.removeAdsOwned = true;
        Check("Remove Ads owner: no banner", !AdManager.TryShowMenuBanner(owner) && !p.bannerUp);
        var paragon = Good(); paragon.paragonOwned = true;
        Check("Paragon owner: no banner", !AdManager.TryShowMenuBanner(paragon));
        var early = Good(); early.playSeconds = AdPolicy.AdFreePlaySeconds - 1f;
        Check("inside the ad-free window: no banner", !AdManager.TryShowMenuBanner(early) && !p.bannerUp);

        AdManager.Reset();
        var q = Install();
        q.bannerAvailable = false;
        Check("no banner unit: nothing shown", !AdManager.TryShowMenuBanner(Good()) && !q.bannerUp);
    }

    static void Lifecycle()
    {
        AdManager.Reset();
        var p = Install();
        EditorSceneLoader.Open(CreditsBanner.SceneName);
        var banner = CreditsBanner.Attach(CreditsBanner.SceneName);
        Check("Attach only on the Credits scene", CreditsBanner.Attach("startS4") == null && banner != null);
        banner.SendMessage("Start");
        Check("opening Credits shows the banner", p.bannerUp);
        banner.SendMessage("OnApplicationPause", true);
        Check("app pause hides it", !p.bannerUp);
        banner.SendMessage("OnApplicationPause", false);
        Check("app resume puts it back", p.bannerUp);
        banner.SendMessage("OnDisable");   // edit mode does not run the Unity disable/destroy messages itself
        banner.SendMessage("OnDestroy");
        Object.DestroyImmediate(banner.gameObject);
        Check("closing Credits hides it", !p.bannerUp && !AdManager.BannerShowing);
    }

    static void Layout(string deviceId)
    {
        AdManager.Reset();
        var p = Install();
        var device = FitDevice.Find(deviceId);
        var screen = ScreenFitScreens.Find("credits");
        EditorSceneLoader.Open(CreditsBanner.SceneName);
        Time.timeScale = 1f;
        using (var rig = new ScreenFitRig(device, screen.MinHalfWidth))
        {
            MenuStyler.StyleScene();
            rig.Sync();
            var mover = Object.FindFirstObjectByType<CreditsMove>();
            Vector2 loOff = default, hiOff = default;
            if (mover != null) mover.GetLimits(out loOff, out hiOff);
            SafeAreaClamp.AttachAll(CreditsBanner.SceneName);
            rig.Sync();
            SafeAreaClamp.AttachAll(CreditsBanner.SceneName);

            AdManager.TryShowMenuBanner(Good());
            string tag = deviceId + ": ";
            Check(tag + "banner is up", p.bannerUp);
            SafeAreaClamp.AttachAll(CreditsBanner.SceneName);
            foreach (var c in Object.FindObjectsByType<SafeAreaClamp>(FindObjectsSortMode.None)) c.Apply();
            rig.Sync();

            float bannerPx = AdManager.BannerHeightPx;
            Check(tag + "banner height is positive", bannerPx > 0f);
            var banner = new Rect(0f, 0f, ScreenInfo.Width, bannerPx);
            int buttons = 0, overlaps = 0;
            foreach (var b in Object.FindObjectsByType<Button>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                var rt = b.transform as RectTransform;
                if (rt == null || !b.gameObject.activeInHierarchy) continue;
                var r = rig.PixelRect(rt);
                if (r.width < 1f || r.height < 1f) continue;
                buttons++;
                if (r.Overlaps(banner)) { overlaps++; Check(tag + b.name + " overlaps the banner (rect " + r + ", banner height " + bannerPx + ")", false); }
            }
            Check(tag + buttons + " buttons, none under the banner", buttons > 0 && overlaps == 0);

            if (mover != null)
            {
                Vector2 lo, hi;
                mover.GetLimits(out lo, out hi);
                Check(tag + "the roll's bounce area stops above the banner", lo.y + hi.y > loOff.y + hiOff.y);
            }
        }
        AdManager.HideBanner();
    }
}
