#if UNITY_IOS
using System.IO;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEditor.iOS.Xcode;

// Adds the capabilities the iOS account code needs to the generated Xcode
// project, so they don't have to be ticked by hand after every build:
//
//   Game Center  - sign-in, achievements, leaderboard
//   iCloud       - iCloud Documents with the default container, which Game
//                  Center saved games (GKSavedGame, the cloud save) live in
//
// The App ID (me.hapticgate.pause) must have Game Center and iCloud enabled in
// the Apple Developer portal, with a provisioning profile that includes them;
// signing itself is left to Xcode.
public static class IOSCapabilitiesPostProcess
{
    public const string EntitlementsFile = "Unity-iPhone/Pause.entitlements";

    [PostProcessBuild(100)]
    public static void OnPostProcessBuild(BuildTarget target, string buildPath)
    {
        if (target != BuildTarget.iOS) return;

        string projectPath = PBXProject.GetPBXProjectPath(buildPath);
        var project = new PBXProject();
        project.ReadFromFile(projectPath);
        string mainTarget = project.GetUnityMainTargetGuid();
        string frameworkTarget = project.GetUnityFrameworkTargetGuid();
        // The native saved-games bridge is compiled into UnityFramework.
        project.AddFrameworkToProject(frameworkTarget, "GameKit.framework", false);
        project.WriteToFile(projectPath);

        var capabilities = new ProjectCapabilityManager(projectPath, EntitlementsFile, null, mainTarget);
        capabilities.AddGameCenter();
        capabilities.AddiCloud(false, true, false, true, null);
        capabilities.WriteToFile();

        UnityEngine.Debug.Log("[BUILD] iOS: added Game Center + iCloud Documents capabilities ("
            + Path.Combine(buildPath, EntitlementsFile) + ")");
    }
}
#endif
