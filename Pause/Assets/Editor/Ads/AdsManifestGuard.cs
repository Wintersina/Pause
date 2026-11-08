using System.IO;
using System.Xml.Linq;
using UnityEditor;
using UnityEditor.Android;
using UnityEngine;

// While the ads master switch is off (AdsConfig.MasterSwitch), the Android build must look to Google
// Play exactly as it did before the SDK was added: the SDK's library manifests would otherwise merge in
// the advertising ID permissions (which need a Play Console declaration), a content provider that
// starts the SDK at app launch, and WorkManager's foreground service and wake lock. This removes them from the app manifest at build time.
//
// It does NOT apply to dev builds (BuildScript.BuildAndroidDev, or Unity's Development Build checkbox),
// where ads are forced on for testing with Google's test IDs. With MasterSwitch true (the shipped state) nothing
// is removed, so AD_ID is merged in and the SDK behaves as shipped.
public class AdsManifestGuard : IPostGenerateGradleAndroidProject
{
    public static bool DevBuild;   // set by BuildScript.BuildAndroidDev

    public int callbackOrder { get { return 1000; } }

    static readonly XNamespace Android = "http://schemas.android.com/apk/res/android";
    static readonly XNamespace Tools = "http://schemas.android.com/tools";

    public static readonly string[] RemovedPermissions =
    {
        "com.google.android.gms.permission.AD_ID",
        "android.permission.ACCESS_ADSERVICES_AD_ID",
        "android.permission.ACCESS_ADSERVICES_ATTRIBUTION",
        "android.permission.ACCESS_ADSERVICES_TOPICS",
        // pulled in by the SDK's WorkManager; the app's baseline has neither
        "android.permission.FOREGROUND_SERVICE",
        "android.permission.WAKE_LOCK",
    };
    public const string RemovedProvider = "com.google.android.gms.ads.MobileAdsInitProvider";
    public const string RemovedService = "androidx.work.impl.foreground.SystemForegroundService";

    public static bool ShouldStrip(bool masterSwitch, bool devBuild) { return !masterSwitch && !devBuild; }

    public void OnPostGenerateGradleAndroidProject(string path)
    {
        bool dev = DevBuild || EditorUserBuildSettings.development;
        if (!ShouldStrip(AdsConfig.MasterSwitch, dev)) return;
        // `path` is the unityLibrary module; the app's own manifest is the launcher's.
        string manifest = Path.GetFullPath(Path.Combine(path, "..", "launcher", "src", "main", "AndroidManifest.xml"));
        if (!File.Exists(manifest))
        {
            Debug.LogWarning("[ADS] launcher manifest not found at " + manifest + "; ads permissions were not stripped.");
            return;
        }
        File.WriteAllText(manifest, Strip(File.ReadAllText(manifest)));
        Debug.Log("[ADS] master switch off: stripped the ads permissions and init provider from " + manifest);
    }

    // Pure: the manifest text with the removal markers added (idempotent).
    public static string Strip(string manifestXml)
    {
        var doc = XDocument.Parse(manifestXml);
        var root = doc.Root;
        if (root.Attribute(XNamespace.Xmlns + "tools") == null) root.SetAttributeValue(XNamespace.Xmlns + "tools", Tools.NamespaceName);
        foreach (string permission in RemovedPermissions)
            AddRemoval(root, "uses-permission", permission, root);
        var application = root.Element("application");
        if (application == null) { application = new XElement("application"); root.Add(application); }
        AddRemoval(application, "provider", RemovedProvider, application);
        AddRemoval(application, "service", RemovedService, application);
        return doc.Declaration != null ? doc.Declaration + "\n" + doc.ToString() : doc.ToString();
    }

    static void AddRemoval(XElement parent, string tag, string name, XElement into)
    {
        foreach (var e in into.Elements(tag))
            if ((string)e.Attribute(Android + "name") == name)
            {
                e.SetAttributeValue(Tools + "node", "remove");
                return;
            }
        into.Add(new XElement(tag, new XAttribute(Android + "name", name), new XAttribute(Tools + "node", "remove")));
    }
}
