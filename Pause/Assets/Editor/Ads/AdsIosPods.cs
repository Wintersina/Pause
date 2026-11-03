#if UNITY_IOS
using System.IO;
using UnityEditor;
using UnityEditor.Callbacks;

// The Google Mobile Ads and UMP native SDKs reach the iOS build as CocoaPods. The External Dependency Manager
// writes the Podfile from the plugin's *Dependencies.xml when CocoaPods is available on the build machine. If it
// did not (no `pod` tool installed, resolver disabled), this writes an equivalent Podfile so the Xcode project
// still names the pods: run `pod install` in the export folder and open Unity-iPhone.xcworkspace.
public static class AdsIosPods
{
    public const string AdsPod = "Google-Mobile-Ads-SDK";
    public const string AdsPodVersion = "~> 13.11";
    public const string UmpPod = "GoogleUserMessagingPlatform";
    public const string UmpPodVersion = "3.1.0";

    public static string Podfile()
    {
        return "source 'https://github.com/CocoaPods/Specs'\n"
            + "platform :ios, '13.0'\n"
            + "use_frameworks! :linkage => :static\n"
            + "target 'UnityFramework' do\n"
            + "  pod '" + AdsPod + "', '" + AdsPodVersion + "'\n"
            + "  pod '" + UmpPod + "', '" + UmpPodVersion + "'\n"
            + "end\n"
            + "target 'Unity-iPhone' do\nend\n";
    }

    [PostProcessBuild(500)]
    public static void OnPostProcessBuild(BuildTarget target, string buildPath)
    {
        if (target != BuildTarget.iOS) return;
        string podfile = Path.Combine(buildPath, "Podfile");
        if (File.Exists(podfile)) return;   // the dependency manager already wrote it
        File.WriteAllText(podfile, Podfile());
        UnityEngine.Debug.Log("[ADS] iOS: no Podfile from the dependency manager; wrote one at " + podfile + " (run `pod install`).");
    }
}
#endif
