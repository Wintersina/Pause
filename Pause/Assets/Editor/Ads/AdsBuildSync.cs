using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

// Keeps the Google Mobile Ads plugin's settings asset (the App IDs the plugin writes into the
// Android manifest and the iOS Info.plist, and the ATT prompt text) equal to AdsConfig, so filling in the
// real AdMob IDs is a one-file change. Runs before the plugin's own pre-build steps.
public class AdsBuildSync : IPreprocessBuildWithReport
{
    public const string SettingsPath = "Assets/GoogleMobileAds/Resources/GoogleMobileAdsSettings.asset";

    public int callbackOrder { get { return -100; } }

    public void OnPreprocessBuild(BuildReport report) { Sync(); }

    // Returns true when the asset was changed.
    public static bool Sync() { return Sync(AdsManifestGuard.DevBuild || EditorUserBuildSettings.development); }

    public static bool Sync(bool devBuild)
    {
        var asset = AssetDatabase.LoadMainAssetAtPath(SettingsPath);
        if (asset == null)
        {
            Debug.LogWarning("[ADS] " + SettingsPath + " is missing; the Google Mobile Ads plugin will create an empty one and fail the build.");
            return false;
        }
        var so = new SerializedObject(asset);
        bool changed = Set(so, "adMobAndroidAppId", AdsConfig.AppIdFor(false, devBuild));
        changed |= Set(so, "adMobIOSAppId", AdsConfig.AppIdFor(true, devBuild));
        changed |= Set(so, "userTrackingUsageDescription", AdsConfig.TrackingUsageDescription);
        changed |= SetBool(so, "enableExternalDependencyManager", false);   // this project keeps its own (vendored) EDM
        if (changed)
        {
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(asset);
            AssetDatabase.SaveAssets();
        }
        return changed;
    }

    static bool Set(SerializedObject so, string name, string value)
    {
        var p = so.FindProperty(name);
        if (p == null || p.stringValue == value) return false;
        p.stringValue = value;
        return true;
    }

    static bool SetBool(SerializedObject so, string name, bool value)
    {
        var p = so.FindProperty(name);
        if (p == null || p.boolValue == value) return false;
        p.boolValue = value;
        return true;
    }
}
