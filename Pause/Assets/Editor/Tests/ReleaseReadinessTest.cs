using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

// Guards the Google Play release configuration (docs/release/google-play-audit.md):
//  - package id, a x.y.z versionName and a positive versionCode;
//  - Android: IL2CPP, ARM64 only, target API >= 36, min API >= 23;
//  - only the four shipping worlds are reachable (Tide's release switch is off,
//    no Storm / Desert world exists);
//  - every Android achievement id and every Android leaderboard id is a real
//    Play Games id, never a TODO_ placeholder; the iOS-only gaps do not block Android;
//  - no developer define (PAUSE_DEV) is baked into Player Settings;
//  - the only <uses-permission> entries in project manifests are the allowed ones,
//    and no SD-card permission is forced;
//  - Build Settings holds exactly the seven shipping scenes, all enabled.
public static class ReleaseReadinessTest
{
    static int fails;
    static void Check(string what, bool ok)
    {
        Debug.Log((ok ? "[RELEASE] PASS  " : "[RELEASE] FAIL  ") + what);
        if (!ok) fails++;
    }

    public const string PackageId = "me.hapticgate.pause";
    public const int MinTargetSdk = 36;   // Play: new apps must target Android 16 (API 36) from 31 Aug 2026

    static readonly string[] ShippingScenes =
    {
        "Assets/Scenes/spashS7.unity", "Assets/Scenes/startS4.unity", "Assets/Scenes/leaderboardS3.unity",
        "Assets/Scenes/gameS1.unity", "Assets/Scenes/shopS6.unity", "Assets/Scenes/creditsS7.unity",
        "Assets/Scenes/tutorialS5.unity",
    };

    static readonly string[] ShippingWorlds = { "Space", "Frost", "Verdant", "Ember" };

    // What the project's own manifests may request (Play Games adds INTERNET /
    // ACCESS_NETWORK_STATE itself; the merged-manifest list is in the audit doc).
    static readonly string[] AllowedPermissions =
    {
        "android.permission.INTERNET", "android.permission.ACCESS_NETWORK_STATE",
    };

    public static void Run() { TestHarness.Exit(Execute()); }

    public static int Execute()
    {
        fails = 0;
        PlayerSettingsChecks();
        Worlds();
        StoreIds();
        Scenes();
        Permissions();
        return fails;
    }

    static void PlayerSettingsChecks()
    {
        Check("Android package id is " + PackageId, PlayerSettings.GetApplicationIdentifier(NamedBuildTarget.Android) == PackageId);
        Check("versionName is x.y.z (" + PlayerSettings.bundleVersion + ")", Regex.IsMatch(PlayerSettings.bundleVersion ?? "", @"^\d+\.\d+\.\d+$"));
        Check("versionCode is positive (" + PlayerSettings.Android.bundleVersionCode + ")", PlayerSettings.Android.bundleVersionCode >= 1);
        Check("Android uses IL2CPP", PlayerSettings.GetScriptingBackend(NamedBuildTarget.Android) == ScriptingImplementation.IL2CPP);
        Check("Android builds ARM64 only", PlayerSettings.Android.targetArchitectures == AndroidArchitecture.ARM64);
        Check("target API >= " + MinTargetSdk + " (" + (int)PlayerSettings.Android.targetSdkVersion + ")", (int)PlayerSettings.Android.targetSdkVersion >= MinTargetSdk);
        Check("min API >= 23 (" + (int)PlayerSettings.Android.minSdkVersion + ")", (int)PlayerSettings.Android.minSdkVersion >= 23);
        Check("portrait only", PlayerSettings.defaultInterfaceOrientation == UIOrientation.Portrait
              || PlayerSettings.defaultInterfaceOrientation == UIOrientation.AutoRotation && PlayerSettings.allowedAutorotateToPortrait
                 && !PlayerSettings.allowedAutorotateToLandscapeLeft && !PlayerSettings.allowedAutorotateToLandscapeRight);
        string defines = PlayerSettings.GetScriptingDefineSymbols(NamedBuildTarget.Android) ?? "";
        Check("no PAUSE_DEV in Android scripting defines", !defines.Contains(BuildScript.DevDefine));
        Check("no SD-card permission forced", !PlayerSettings.Android.forceSDCardPermission);
    }

    static void Worlds()
    {
        Check("Tide release switch is off", !WorldManager.TideEnabled);
        Check("exactly four live worlds", WorldManager.LiveWorldCount == 4);
        for (int i = 0; i < ShippingWorlds.Length; i++)
            Check("world " + i + " is " + ShippingWorlds[i], WorldManager.Worlds.Length > i && WorldManager.Worlds[i].displayName == ShippingWorlds[i]);
        Check("last live world is Ember", WorldManager.Worlds[WorldManager.LastLiveWorld].displayName == "Ember");
        Check("no Storm or Desert world exists",
              !WorldManager.Worlds.Any(w => w.displayName == "Storm" || w.displayName == "Desert"));
    }

    static void StoreIds()
    {
        AchievementIds.Reload();
        int bad = 0;
        foreach (var d in AchievementCatalog.All)
        {
            string id = AchievementIds.AndroidId(d);
            if (AchievementIds.IsPlaceholder(id) || !id.StartsWith("CgkI")) { bad++; Debug.Log("[RELEASE] bad achievement id: " + d.id + " -> " + id); }
        }
        Check("all " + AchievementCatalog.All.Length + " Android achievement ids are real", bad == 0 && AchievementCatalog.All.Length > 0);
        var ids = AchievementCatalog.All.Select(AchievementIds.AndroidId).ToList();
        Check("Android achievement ids are unique", ids.Distinct().Count() == ids.Count);

        Check("Top Score board has an Android id",
              LeaderboardBoards.Get(LeaderboardBoards.TopScore) != null && LeaderboardBoards.Get(LeaderboardBoards.TopScore).EnabledOn(false));
        foreach (var b in LeaderboardBoards.All)
        {
            Check("board " + b.id + " Android id is real (" + b.androidId + ")",
                  b.androidId.StartsWith("CgkI") && !AchievementIds.IsPlaceholder(b.androidId));
        }
        Check("Play Games app id is set", File.ReadAllText("Assets/GooglePlayGames/Resources/PlayGamesSettings.asset").Contains("mAppId: 528367766818"));
    }

    static void Scenes()
    {
        var scenes = EditorBuildSettings.scenes;
        Check("Build Settings has exactly the " + ShippingScenes.Length + " shipping scenes",
              scenes.Length == ShippingScenes.Length && ShippingScenes.All(p => scenes.Any(s => s.path == p)));
        Check("every build scene is enabled and exists", scenes.All(s => s.enabled && File.Exists(s.path)));
    }

    static void Permissions()
    {
        var found = new List<string>();
        foreach (string guid in AssetDatabase.FindAssets("", new[] { "Assets/Plugins" }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (!path.EndsWith("AndroidManifest.xml")) continue;
            foreach (Match m in Regex.Matches(File.ReadAllText(path), @"<uses-permission[^>]*android:name=""([^""]+)"""))
                found.Add(m.Groups[1].Value);
        }
        var extra = found.Where(p => !AllowedPermissions.Contains(p)).ToList();
        Check("project manifests request only allowed permissions" + (extra.Count > 0 ? " (extra: " + string.Join(", ", extra) + ")" : ""), extra.Count == 0);
        foreach (string banned in new[] { "RECORD_AUDIO", "READ_PHONE_STATE", "EXTERNAL_STORAGE", "READ_CONTACTS", "ACCESS_FINE_LOCATION", "CAMERA" })
            Check("no " + banned + " in project manifests", !found.Any(p => p.Contains(banned)));
    }
}
