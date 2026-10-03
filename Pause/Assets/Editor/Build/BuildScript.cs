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

    public static void BuildIOS()
    {
        // Produces an Xcode project, not a finished .ipa.
        Run(BuildTarget.iOS, Path.Combine(OutputRoot, "iOS"));
    }

    static void Run(BuildTarget target, string outputPath)
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
        };

        Debug.Log("[BUILD] " + target + " -> " + outputPath + " (" + scenes.Length + " scenes)");
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

            EditorApplication.Exit(1);
        }

        EditorApplication.Exit(0);
    }
}
