#if UNITY_IOS
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEditor.iOS.Xcode;

// App Store compliance for every iOS export (debug and release alike, so a
// TestFlight build never differs from what was tested):
//
//   Info.plist
//     ITSAppUsesNonExemptEncryption = false   only HTTPS / OS crypto (SHA-1 for
//                                             ids, Firebase + AdMob over TLS):
//                                             exempt, so no export-compliance
//                                             question on every upload.
//     UIRequiresFullScreen = true             portrait-only game; without it iPad
//                                             validation demands all 4 orientations.
//     UIRequiredDeviceCapabilities has arm64.
//     Usage-description keys the game never needs (camera, microphone, location,
//     photos, ...) are removed so review cannot ask why they are there. The
//     tracking prompt text (NSUserTrackingUsageDescription), the AdMob app id
//     (GADApplicationIdentifier) and SKAdNetworkItems are written by Google's
//     PListProcessor; this step only checks they are present and logs a loud
//     [BUILD] ERROR if one is missing.
//
//   PrivacyInfo.xcprivacy
//     Unity, Firebase and Google Mobile Ads ship their own manifests. The app's
//     own code reads/writes PlayerPrefs (UserDefaults) and save files, which are
//     required-reason APIs; they are declared here, merged into the manifest
//     Unity wrote if there is one, otherwise added as a new resource.
public static class IOSReleasePostProcess
{
    // Keys that would have to be justified to review; the game uses none of the APIs behind them.
    static readonly string[] UnusedUsageKeys =
    {
        "NSCameraUsageDescription", "NSMicrophoneUsageDescription", "NSPhotoLibraryUsageDescription",
        "NSPhotoLibraryAddUsageDescription", "NSLocationWhenInUseUsageDescription", "NSLocationAlwaysUsageDescription",
        "NSLocationAlwaysAndWhenInUseUsageDescription", "NSContactsUsageDescription", "NSCalendarsUsageDescription",
        "NSBluetoothAlwaysUsageDescription", "NSBluetoothPeripheralUsageDescription", "NSMotionUsageDescription",
        "NSFaceIDUsageDescription", "NSSpeechRecognitionUsageDescription", "NSAppleMusicUsageDescription",
        "NSHealthShareUsageDescription", "NSHealthUpdateUsageDescription", "NSLocalNetworkUsageDescription",
    };

    public const string PrivacyFile = "PrivacyInfo.xcprivacy";

    [PostProcessBuild(300)]
    public static void OnPostProcessBuild(BuildTarget target, string buildPath)
    {
        if (target != BuildTarget.iOS) return;
        PatchInfoPlist(buildPath);
        PatchPrivacyManifest(buildPath);
    }

    static void PatchInfoPlist(string buildPath)
    {
        string path = Path.Combine(buildPath, "Info.plist");
        var doc = new PlistDocument();
        doc.ReadFromFile(path);
        var root = doc.root;

        root.SetBoolean("ITSAppUsesNonExemptEncryption", false);
        root.SetBoolean("UIRequiresFullScreen", true);

        var removed = new List<string>();
        foreach (string key in UnusedUsageKeys)
            if (root.values.ContainsKey(key)) { root.values.Remove(key); removed.Add(key); }

        PlistElementArray caps = root.values.ContainsKey("UIRequiredDeviceCapabilities")
            ? root["UIRequiredDeviceCapabilities"] as PlistElementArray : null;
        if (caps == null) caps = root.CreateArray("UIRequiredDeviceCapabilities");
        bool hasArm64 = false;
        foreach (var e in caps.values) if (e.AsString() == "arm64") hasArm64 = true;
        if (!hasArm64) caps.AddString("arm64");

        foreach (string must in new[] { "GADApplicationIdentifier", "NSUserTrackingUsageDescription", "SKAdNetworkItems" })
            if (!root.values.ContainsKey(must))
                UnityEngine.Debug.LogError("[BUILD] ERROR iOS Info.plist is missing " + must + " (Google Mobile Ads plist step did not run?)");

        doc.WriteToFile(path);
        UnityEngine.Debug.Log("[BUILD] iOS Info.plist: encryption-exempt, full screen, arm64"
            + (removed.Count > 0 ? ", removed unused " + string.Join(",", removed) : ""));
    }

    // (type, reason) pairs for the app's own code. Reasons per Apple's required-reason list:
    //   UserDefaults CA92.1            PlayerPrefs: the app's own settings and saves.
    //   FileTimestamp C617.1           timestamps of files inside the app container (save files).
    // Unity's own manifest already covers the engine's boot-time and disk-space use; only the app's code is added.
    static readonly string[][] ApiReasons =
    {
        new[] { "NSPrivacyAccessedAPICategoryUserDefaults", "CA92.1" },
        new[] { "NSPrivacyAccessedAPICategoryFileTimestamp", "C617.1" },
    };

    static void PatchPrivacyManifest(string buildPath)
    {
        string path = Path.Combine(buildPath, PrivacyFile);
        var doc = new PlistDocument();
        bool existed = File.Exists(path);
        if (existed) doc.ReadFromFile(path);
        else doc.Create();
        var root = doc.root;

        // The app shows ads with ATT consent, so it is a tracking app; the tracking domains come from the
        // Google Mobile Ads SDK's own manifest. (Not set false over a manifest that already says true.)
        if (!root.values.ContainsKey("NSPrivacyTracking")) root.SetBoolean("NSPrivacyTracking", true);
        if (!root.values.ContainsKey("NSPrivacyTrackingDomains")) root.CreateArray("NSPrivacyTrackingDomains");
        if (!root.values.ContainsKey("NSPrivacyCollectedDataTypes")) root.CreateArray("NSPrivacyCollectedDataTypes");

        PlistElementArray apis = root.values.ContainsKey("NSPrivacyAccessedAPITypes")
            ? root["NSPrivacyAccessedAPITypes"] as PlistElementArray : root.CreateArray("NSPrivacyAccessedAPITypes");
        foreach (var pair in ApiReasons)
        {
            PlistElementArray reasons = null;
            foreach (var e in apis.values)
            {
                var d = e as PlistElementDict;
                if (d != null && d.values.ContainsKey("NSPrivacyAccessedAPIType") && d["NSPrivacyAccessedAPIType"].AsString() == pair[0])
                    reasons = d["NSPrivacyAccessedAPITypeReasons"] as PlistElementArray;
            }
            if (reasons == null)
            {
                var d = apis.AddDict();
                d.SetString("NSPrivacyAccessedAPIType", pair[0]);
                reasons = d.CreateArray("NSPrivacyAccessedAPITypeReasons");
            }
            bool has = false;
            foreach (var r in reasons.values) if (r.AsString() == pair[1]) has = true;
            if (!has) reasons.AddString(pair[1]);
        }
        doc.WriteToFile(path);

        if (!existed)
        {
            // Not produced by Unity: add the file to the main target's resources.
            string projPath = PBXProject.GetPBXProjectPath(buildPath);
            var project = new PBXProject();
            project.ReadFromFile(projPath);
            string guid = project.AddFile(PrivacyFile, PrivacyFile);
            project.AddFileToBuild(project.GetUnityMainTargetGuid(), guid);
            project.WriteToFile(projPath);
        }
        UnityEngine.Debug.Log("[BUILD] iOS privacy manifest: " + (existed ? "merged app-level required-reason APIs into Unity's " : "wrote ") + PrivacyFile);
    }
}
#endif
