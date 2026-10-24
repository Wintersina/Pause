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
//
// Sideload build for a free personal Apple ID (no paid Game Center / iCloud
// profile): PAUSE_IOS_NO_GAMECENTER=1 leaves Pause.entitlements empty and adds
// neither capability, and PAUSE_IOS_TEAM_ID=<10-char team id> turns on
// automatic signing for that team. Unset, the output is unchanged.
public static class IOSCapabilitiesPostProcess
{
    public const string EntitlementsFile = "Unity-iPhone/Pause.entitlements";

    public const string NoGameCenterEnv = "PAUSE_IOS_NO_GAMECENTER";
    public const string TeamIdEnv = "PAUSE_IOS_TEAM_ID";

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

        bool noGameCenter = System.Environment.GetEnvironmentVariable(NoGameCenterEnv) == "1";
        string teamId = System.Environment.GetEnvironmentVariable(TeamIdEnv);
        if (noGameCenter)
        {
            // An empty entitlements plist, so xcodebuild asks for no capability profile.
            string entitlementsPath = Path.Combine(buildPath, EntitlementsFile);
            Directory.CreateDirectory(Path.GetDirectoryName(entitlementsPath));
            File.WriteAllText(entitlementsPath,
                "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n"
                + "<!DOCTYPE plist PUBLIC \"-//Apple//DTD PLIST 1.0//EN\" \"http://www.apple.com/DTDs/PropertyList-1.0.dtd\">\n"
                + "<plist version=\"1.0\">\n<dict/>\n</plist>\n");
            project.SetBuildProperty(mainTarget, "CODE_SIGN_ENTITLEMENTS", EntitlementsFile);
        }
        if (!string.IsNullOrEmpty(teamId))
        {
            project.SetBuildProperty(mainTarget, "DEVELOPMENT_TEAM", teamId);
            project.SetBuildProperty(mainTarget, "CODE_SIGN_STYLE", "Automatic");
        }
        project.WriteToFile(projectPath);

        if (noGameCenter)
        {
            UnityEngine.Debug.Log("[BUILD] iOS: " + NoGameCenterEnv + "=1, empty entitlements, no Game Center / iCloud ("
                + Path.Combine(buildPath, EntitlementsFile) + ")");
            return;
        }

        var capabilities = new ProjectCapabilityManager(projectPath, EntitlementsFile, null, mainTarget);
        capabilities.AddGameCenter();
        capabilities.AddiCloud(false, true, false, true, null);
        capabilities.WriteToFile();

        UnityEngine.Debug.Log("[BUILD] iOS: added Game Center + iCloud Documents capabilities ("
            + Path.Combine(buildPath, EntitlementsFile) + ")");
    }
}
#endif
