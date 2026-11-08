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

    public const string FirebaseVersion = "12.19.3";   // keep equal to the iosPod versions in Assets/Firebase/Editor/*Dependencies.xml
    public const string MinIos = "15.0";               // Firebase 12 needs iOS 15; also the Player minimum

    public static string Podfile()
    {
        return "source 'https://github.com/CocoaPods/Specs'\n"
            + "platform :ios, '" + MinIos + "'\n"
            + "use_frameworks! :linkage => :static\n"
            + "target 'UnityFramework' do\n"
#if PAUSE_FIREBASE
            + "  pod 'Firebase/Core', '" + FirebaseVersion + "'\n"
            + "  pod 'Firebase/Auth', '" + FirebaseVersion + "'\n"
            + "  pod 'Firebase/Firestore', '" + FirebaseVersion + "'\n"
            + "  pod 'Firebase/AppCheck', '" + FirebaseVersion + "'\n"
#endif
            + "  pod '" + AdsPod + "', '" + AdsPodVersion + "'\n"
            + "  pod '" + UmpPod + "', '" + UmpPodVersion + "'\n"
            + "end\n"
            + "target 'Unity-iPhone' do\nend\n"
            // Older transitive pods (gRPC, leveldb, GoogleUtilities...) declare iOS 10-12, which current Xcode rejects.
            + "post_install do |installer|\n"
            + "  installer.pods_project.targets.each do |t|\n"
            + "    t.build_configurations.each { |c| c.build_settings['IPHONEOS_DEPLOYMENT_TARGET'] = '" + MinIos + "' }\n"
            + "  end\n"
            + "end\n";
    }

    [PostProcessBuild(500)]
    public static void OnPostProcessBuild(BuildTarget target, string buildPath)
    {
        if (target != BuildTarget.iOS) return;
        string podfile = Path.Combine(buildPath, "Podfile");
        if (File.Exists(podfile)) return;   // the dependency manager already wrote it
        File.WriteAllText(podfile, Podfile());
        UnityEngine.Debug.Log("[ADS] iOS: no Podfile from the dependency manager; wrote one at " + podfile + " (run `pod install` there; `make build-ios-sim` does).");
    }
}
#endif
