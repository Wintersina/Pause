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
        Run(BuildTarget.Android, Path.Combine(OutputRoot, "Android/Pause.apk"));
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
