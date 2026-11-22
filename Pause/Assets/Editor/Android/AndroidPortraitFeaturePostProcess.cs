using System.IO;
using System.Xml;
using UnityEditor.Android;

// Play availability: the game is portrait-locked (screenOrientation=portrait),
// so Play implies <uses-feature android.hardware.screen.portrait required=true>
// and hides the app from landscape-only devices (desktop, XR, some tablets).
// Declaring the feature explicitly as optional keeps the portrait lock but
// lifts the filter. Patched into the generated gradle project's manifests
// (unityLibrary and launcher), idempotently.
public class AndroidPortraitFeaturePostProcess : IPostGenerateGradleAndroidProject
{
    public const string FeatureName = "android.hardware.screen.portrait";
    const string AndroidNs = "http://schemas.android.com/apk/res/android";

    public int callbackOrder { get { return 10; } }

    public void OnPostGenerateGradleAndroidProject(string path)
    {
        // path is the unityLibrary module; the launcher sits beside it.
        string[] manifests =
        {
            Path.Combine(path, "src/main/AndroidManifest.xml"),
            Path.Combine(path, "../launcher/src/main/AndroidManifest.xml"),
        };
        foreach (var file in manifests)
        {
            if (!File.Exists(file)) continue;
            var doc = new XmlDocument();
            doc.Load(file);
            if (!EnsureOptionalPortrait(doc)) continue;
            doc.Save(file);
        }
    }

    // Adds <uses-feature android:name="android.hardware.screen.portrait"
    // android:required="false"/> under <manifest>. Returns true if the document
    // changed. An existing entry is forced to required=false, never duplicated.
    public static bool EnsureOptionalPortrait(XmlDocument doc)
    {
        var root = doc.DocumentElement;
        if (root == null || root.Name != "manifest") return false;
        foreach (XmlNode n in root.ChildNodes)
        {
            var e = n as XmlElement;
            if (e == null || e.Name != "uses-feature") continue;
            if (e.GetAttribute("name", AndroidNs) != FeatureName) continue;
            if (e.GetAttribute("required", AndroidNs) == "false") return false;
            e.SetAttribute("required", AndroidNs, "false");
            return true;
        }
        var feature = doc.CreateElement("uses-feature");
        var name = doc.CreateAttribute("android", "name", AndroidNs);
        name.Value = FeatureName;
        feature.SetAttributeNode(name);
        var req = doc.CreateAttribute("android", "required", AndroidNs);
        req.Value = "false";
        feature.SetAttributeNode(req);
        root.AppendChild(feature);
        return true;
    }
}
