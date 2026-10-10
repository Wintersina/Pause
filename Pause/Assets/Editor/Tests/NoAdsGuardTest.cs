using System.IO;
using System.Text.RegularExpressions;
using UnityEngine;

// Pause v1.0 is a paid app with no ads (docs/release/no-ads.md). The Play
// Console "no ads" and Data-safety answers are only true while nothing in the
// project can show an ad or ask for the advertising id. Fails if a script,
// scene, prefab, manifest, gradle file or package list names AdMob or an ad
// SDK again.
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
            if (AdTerms.IsMatch(File.ReadAllText(file)))
                offenders.Add(file.Substring(project.Length + 1));
        }
        Check("no script, scene, prefab or manifest under Assets names an ad SDK (" + scanned + " files)"
              + (offenders.Count > 0 ? ": " + string.Join(", ", offenders) : ""), offenders.Count == 0);

        foreach (string rel in new[] { "Packages/manifest.json", "ProjectSettings/ProjectSettings.asset" })
        {
            string path = Path.Combine(project, rel);
            Check(rel + " names no ad SDK", !File.Exists(path) || !AdTerms.IsMatch(File.ReadAllText(path)));
        }

        Check("AdMob.cs is gone", !File.Exists(Path.Combine(Application.dataPath, "Scripts/UI/AdMob.cs")));
        return fails;
    }
}
