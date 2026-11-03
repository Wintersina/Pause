using System.IO;
using System.Text.RegularExpressions;
using UnityEngine;

// Pause v1.0 is a paid app with no ads (docs/release/no-ads.md). The Play
// Console "no ads" and Data-safety answers are only true while nothing in the
// project can show an ad or ask for the advertising id. Fails if a script,
// scene, prefab, manifest, gradle file or package list names AdMob or an ad
// SDK again.
//
// Phase 4 of the free-with-ads move adds the AdMob SDK on purpose, behind
// AdsConfig.MasterSwitch (false). So the rule is now: an ad SDK may be named only
// inside the sanctioned ad code and plugin files (AllowedPaths), and while any
// ad code is present the master switch must be false -- the store's "no ads"
// answers stay true until the owner flips it together with the Play Console
// declarations (then update this guard in the same change).
public static class NoAdsGuardTest
{
    static int fails;
    static void Check(string what, bool ok)
    {
        Debug.Log((ok ? "[NOADS] PASS  " : "[NOADS] FAIL  ") + what);
        if (!ok) fails++;
    }

    public static void Run()
    {
        TestHarness.Exit(Execute());
    }

    // Case-insensitive; ad-SDK class names, ids and the advertising-id permission.
    static readonly Regex AdTerms = new Regex(
        @"AdMob|isAdsShowwing|GoogleMobileAds|google\.mobileads|play-services-ads|ca-app-pub|permission\.AD_ID|" +
        @"gms\.ads|BannerView|InterstitialAd|RewardedAd|AppLovin|IronSource|UnityAds|com\.unity\.ads|" +
        @"Vungle|AdColony|Chartboost",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static int Execute()
    {
        fails = 0;
        string project = Path.GetDirectoryName(Application.dataPath);
        string self = "NoAdsGuardTest.cs";
        // Third-party plugin trees only mention ads in their own docs.
        string[] skip = { "ExternalDependencyManager", "GooglePlayGames" };
        // The ads feature (Scripts/Ads, Editor/Ads), its tests and build hooks, and the Google Mobile Ads plugin
        // with the gradle / manifest / plist files its dependency manager writes.
        string[] allowed =
        {
            "Scripts/Ads/", "Editor/Ads/", "GoogleMobileAds/", "Plugins/Android/GoogleMobileAdsPlugin.androidlib/",
            "Plugins/Android/mainTemplate.gradle", "Plugins/Android/settingsTemplate.gradle", "Plugins/iOS/",
            "Editor/Tests/AdsTest.cs", "Editor/Tests/UnusedAssetGuardTest.cs", "Editor/Build/BuildScript.cs", "Editor/Build/IOSCapabilitiesPostProcess.cs",
        };

        int scanned = 0;
        var offenders = new System.Collections.Generic.List<string>();
        foreach (string file in Directory.EnumerateFiles(Application.dataPath, "*", SearchOption.AllDirectories))
        {
            string ext = Path.GetExtension(file).ToLowerInvariant();
            if (ext != ".cs" && ext != ".unity" && ext != ".prefab" && ext != ".xml" && ext != ".gradle"
                && ext != ".androidlib" && ext != ".json" && ext != ".asset" && ext != ".properties")
                continue;
            if (Path.GetFileName(file) == self) continue;
            bool skipped = false;
            foreach (string s in skip)
                if (file.Contains(Path.DirectorySeparatorChar + s + Path.DirectorySeparatorChar)) skipped = true;
            if (skipped) continue;
            scanned++;
            string rel = file.Substring(Application.dataPath.Length + 1).Replace('\\', '/');
            bool ok = false;
            foreach (string a in allowed) if (rel.StartsWith(a) || rel == a) ok = true;
            if (!ok && AdTerms.IsMatch(File.ReadAllText(file)))
                offenders.Add(file.Substring(project.Length + 1));
        }
        Check("no script, scene, prefab or manifest outside the sanctioned ad files names an ad SDK (" + scanned + " files)"
              + (offenders.Count > 0 ? ": " + string.Join(", ", offenders) : ""), offenders.Count == 0);

        foreach (string rel in new[] { "Packages/manifest.json", "ProjectSettings/ProjectSettings.asset" })
        {
            string path = Path.Combine(project, rel);
            Check(rel + " names no ad SDK", !File.Exists(path) || !AdTerms.IsMatch(File.ReadAllText(path)));
        }

        Check("ads stay off: AdsConfig.MasterSwitch is false (flip it only with the Play Console declarations)", AdsConfig.MasterSwitch == false);
        Check("ads stay off on iOS: AdsConfig.EnabledOnIos is false until StoreKit ships", AdsConfig.EnabledOnIos == false);
        Check("AdMob.cs is gone", !File.Exists(Path.Combine(Application.dataPath, "Scripts/UI/AdMob.cs")));
        return fails;
    }
}
