using System.Xml;
using UnityEngine;

// The Android build declares android.hardware.screen.portrait as optional
// (required=false) so the portrait lock does not restrict Play availability:
// added once, idempotent, and an existing required=true entry is relaxed.
public static class AndroidPortraitFeatureTest
{
    const string Ns = "http://schemas.android.com/apk/res/android";
    static int fails;
    static void Check(string what, bool ok)
    {
        Debug.Log((ok ? "[PORTRAIT] PASS  " : "[PORTRAIT] FAIL  ") + what);
        if (!ok) fails++;
    }

    public static void Run() { TestHarness.Exit(Execute()); }

    static XmlDocument Load(string inner)
    {
        var d = new XmlDocument();
        d.LoadXml("<manifest xmlns:android=\"" + Ns + "\">" + inner + "<application/></manifest>");
        return d;
    }

    static int Count(XmlDocument d, out string required)
    {
        int n = 0; required = null;
        foreach (XmlNode x in d.DocumentElement.ChildNodes)
        {
            var e = x as XmlElement;
            if (e != null && e.Name == "uses-feature" &&
                e.GetAttribute("name", Ns) == AndroidPortraitFeaturePostProcess.FeatureName)
            {
                n++; required = e.GetAttribute("required", Ns);
            }
        }
        return n;
    }

    public static int Execute()
    {
        fails = 0;
        string req;

        var d = Load("<uses-feature android:glEsVersion=\"0x00030000\" android:required=\"true\"/>");
        Check("adds the feature to a manifest without it", AndroidPortraitFeaturePostProcess.EnsureOptionalPortrait(d));
        Check("exactly one portrait feature, required=false", Count(d, out req) == 1 && req == "false");
        Check("other uses-feature entries are kept", d.GetElementsByTagName("uses-feature").Count == 2);
        Check("second run changes nothing", !AndroidPortraitFeaturePostProcess.EnsureOptionalPortrait(d));
        Check("still exactly one after the second run", Count(d, out req) == 1 && req == "false");

        var d2 = Load("<uses-feature android:name=\"android.hardware.screen.portrait\" android:required=\"true\"/>");
        Check("relaxes an existing required=true entry", AndroidPortraitFeaturePostProcess.EnsureOptionalPortrait(d2));
        Check("still one entry, now required=false", Count(d2, out req) == 1 && req == "false");

        var d3 = Load("");
        AndroidPortraitFeaturePostProcess.EnsureOptionalPortrait(d3);
        Check("works on a manifest with no uses-feature", Count(d3, out req) == 1 && req == "false");

        Check("manifest xml serialises the android prefix",
            d3.OuterXml.Contains("android:name=\"android.hardware.screen.portrait\"") &&
            d3.OuterXml.Contains("android:required=\"false\""));
        return fails;
    }
}
