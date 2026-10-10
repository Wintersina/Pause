using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

// Command-line build entry points.
//
//   Unity -batchmode -quit -projectPath . -executeMethod BuildScript.BuildMac
//
// Output goes to Builds/<platform>/ (gitignored).
public static class BuildScript
{
    static string[] Scenes
    {
        get
        {
            return EditorBuildSettings.scenes
                .Where(s => s.enabled)
                .Select(s => s.path)
                .ToArray();
        }
    }

    static string OutputRoot
    {
        get { return Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Builds"); }
    }

    public static void BuildMac()
    {
        Run(BuildTarget.StandaloneOSX, Path.Combine(OutputRoot, "Mac/Pause.app"));
    }

    public static void BuildAndroid()
    {
        ConfigureAndroidSigning();
        Run(BuildTarget.Android, Path.Combine(OutputRoot, "Android/Pause.apk"));
    }

    // The project is set to sign with the release keystore, which needs
    // passwords a command-line build has no way to prompt for -- so an
    // unattended build just fails with "please provide passwords".
    //
    // Pass them in the environment to produce a release-signed APK:
    //   PAUSE_KEYSTORE_PASS=... PAUSE_KEYALIAS_PASS=...
    //
    // With neither set we fall back to Unity's debug key, which is what you
    // want for a build you are only installing on your own device.
    static void ConfigureAndroidSigning()
    {
        string storePass = Environment.GetEnvironmentVariable("PAUSE_KEYSTORE_PASS");
        string aliasPass = Environment.GetEnvironmentVariable("PAUSE_KEYALIAS_PASS");

        if (!string.IsNullOrEmpty(storePass) && !string.IsNullOrEmpty(aliasPass))
        {
            PlayerSettings.Android.useCustomKeystore = true;
            PlayerSettings.Android.keystorePass = storePass;
            PlayerSettings.Android.keyaliasPass = aliasPass;
            Debug.Log("[BUILD] signing Android with the release keystore");
            return;
        }

        PlayerSettings.Android.useCustomKeystore = false;
        Debug.Log("[BUILD] no keystore passwords in the environment; " +
                  "signing Android with the debug key (not for distribution)");
    }

    // Developer builds: the same game compiled with PAUSE_DEV, which starts
    // with developer mode on (see DeveloperUnlocks.ShouldDefaultOn) and shows
    // a DEV badge. The define is passed to this build only, through
    // BuildPlayerOptions.extraScriptingDefines, so PlayerSettings and the
    // committed ProjectSettings never change and release builds are unaffected.
    public const string DevDefine = "PAUSE_DEV";

    public static void BuildAndroidDev()
    {
        ConfigureAndroidSigning();
        Run(BuildTarget.Android, Path.Combine(OutputRoot, "Android/Pause-dev.apk"), DevDefine);
    }


    // Play Store release: a signed AAB (upload) and a signed APK (sideload
    // test) in Builds/Android/Release/. No PAUSE_DEV, no development flag.
    //
    //   PAUSE_KEYSTORE_PASS=... PAUSE_KEYALIAS_PASS=... make android-release
    //   (PAUSE_KEYSTORE_PATH overrides the keystore file; default is
    //    Pause/PauseKey.keystore, alias pausealias.)
    //
    // Without the passwords it still builds, signed with the debug key, into
    // Pause-DEBUGSIGNED.* so the manifest and sizes can be audited; those
    // files must never be uploaded.
    public static void BuildAndroidRelease()
    {
        string dir = Path.Combine(OutputRoot, "Android/Release");
        Directory.CreateDirectory(dir);
        bool signed = ConfigureReleaseSigning();
        string stem = signed ? "Pause" : "Pause-DEBUGSIGNED";
        bool bundleWas = EditorUserBuildSettings.buildAppBundle;
        try
        {
            EditorUserBuildSettings.buildAppBundle = true;
            if (!Build(BuildTarget.Android, Path.Combine(dir, stem + ".aab"))) { EditorApplication.Exit(1); return; }
            EditorUserBuildSettings.buildAppBundle = false;
            if (!Build(BuildTarget.Android, Path.Combine(dir, stem + ".apk"))) { EditorApplication.Exit(1); return; }
        }
        finally { EditorUserBuildSettings.buildAppBundle = bundleWas; }
        EditorApplication.Exit(0);
    }

    static bool ConfigureReleaseSigning()
    {
        string storePass = Environment.GetEnvironmentVariable("PAUSE_KEYSTORE_PASS");
        string aliasPass = Environment.GetEnvironmentVariable("PAUSE_KEYALIAS_PASS");
        string path = Environment.GetEnvironmentVariable("PAUSE_KEYSTORE_PATH");
        if (string.IsNullOrEmpty(path))
            path = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "PauseKey.keystore");
        if (!string.IsNullOrEmpty(storePass) && !string.IsNullOrEmpty(aliasPass) && File.Exists(path))
        {
            PlayerSettings.Android.useCustomKeystore = true;
            PlayerSettings.Android.keystoreName = path;
            PlayerSettings.Android.keyaliasName = "pausealias";
            PlayerSettings.Android.keystorePass = storePass;
            PlayerSettings.Android.keyaliasPass = aliasPass;
            Debug.Log("[BUILD] release signing with " + path);
            return true;
        }
        PlayerSettings.Android.useCustomKeystore = false;
        Debug.LogWarning("[BUILD] keystore passwords/file missing; DEBUG-signed audit build (never upload)");
        return false;
    }

    public static void BuildMacDev()
    {
        Run(BuildTarget.StandaloneOSX, Path.Combine(OutputRoot, "Mac/Pause-dev.app"), DevDefine);
    }

    public static void BuildIOS()
    {
        // Produces an Xcode project, not a finished .ipa.
        Run(BuildTarget.iOS, Path.Combine(OutputRoot, "iOS"));
    }

    public static void BuildIOSDev()
    {
        // Keep this project separate from the release build so Xcode's derived
        // data and the two generated projects cannot be accidentally mixed.
        Run(BuildTarget.iOS, Path.Combine(OutputRoot, "iOS-dev"), DevDefine);
    }

    static void Run(BuildTarget target, string outputPath, params string[] extraDefines)
    {
        EditorApplication.Exit(Build(target, outputPath, extraDefines) ? 0 : 1);
    }

    static bool Build(BuildTarget target, string outputPath, params string[] extraDefines)
    {
        var scenes = Scenes;
        if (scenes.Length == 0)
            throw new Exception("No enabled scenes in Build Settings.");

        Directory.CreateDirectory(Path.GetDirectoryName(outputPath));

        var options = new BuildPlayerOptions
        {
            scenes = scenes,
            locationPathName = outputPath,
            target = target,
            options = BuildOptions.None,
            extraScriptingDefines = extraDefines,
        };

        Debug.Log("[BUILD] " + target + " -> " + outputPath + " (" + scenes.Length + " scenes)"
            + (extraDefines.Length > 0 ? " defines=" + string.Join(",", extraDefines) : ""));
        BuildReport report = BuildPipeline.BuildPlayer(options);
        BuildSummary summary = report.summary;

        Debug.Log("[BUILD] result=" + summary.result
            + " errors=" + summary.totalErrors
            + " warnings=" + summary.totalWarnings
            + " size=" + summary.totalSize + " bytes"
            + " time=" + summary.totalTime);

        if (summary.result != BuildResult.Succeeded)
        {
            foreach (var step in report.steps)
                foreach (var msg in step.messages)
                    if (msg.type == LogType.Error || msg.type == LogType.Exception)
                        Debug.LogError("[BUILD] " + step.name + ": " + msg.content);

            return false;
        }

        return true;
    }
}
