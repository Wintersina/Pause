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

    // Share build (make apk-share): a release-style APK (no PAUSE_DEV, no
    // development flag, APK not AAB) for friends and testers. It is ALWAYS
    // signed with Unity's debug key: it never looks at PAUSE_KEYSTORE_PASS /
    // PAUSE_KEYALIAS_PASS, so a shell that happens to export them cannot leak
    // the release key into a file that gets passed around. Output path comes
    // from PAUSE_SHARE_APK (absolute); default Builds/Android/Pause-share.apk.
    public static void BuildAndroidShare()
    {
        string path = Environment.GetEnvironmentVariable("PAUSE_SHARE_APK");
        if (string.IsNullOrEmpty(path)) path = Path.Combine(OutputRoot, "Android/Pause-share.apk");
        PlayerSettings.Android.useCustomKeystore = false;
        PlayerSettings.Android.keystorePass = "";
        PlayerSettings.Android.keyaliasPass = "";
        EditorUserBuildSettings.buildAppBundle = false;
        Directory.CreateDirectory(OutputRoot); // size/timing reports land here even when the APK does not
        Debug.Log("[BUILD] share build: debug-key signed (not the release key), APK");
        Run(BuildTarget.Android, path);
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
    // The AAB also gets a native debug symbols zip (*.symbols.zip) next to it
    // for Play Console; it is not part of the bundle, so shipped size is unchanged.
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
        var symbolsWas = UnityEditor.Android.UserBuildSettings.DebugSymbols.level;
        try
        {
            EditorUserBuildSettings.buildAppBundle = true;
            UnityEditor.Android.UserBuildSettings.DebugSymbols.level = Unity.Android.Types.DebugSymbolLevel.Full;
            if (!Build(BuildTarget.Android, Path.Combine(dir, stem + ".aab"))) { EditorApplication.Exit(1); return; }
            if (Environment.GetEnvironmentVariable("PAUSE_SKIP_APK") != "1")
            {
                EditorUserBuildSettings.buildAppBundle = false;
                UnityEditor.Android.UserBuildSettings.DebugSymbols.level = Unity.Android.Types.DebugSymbolLevel.None;
                if (!Build(BuildTarget.Android, Path.Combine(dir, stem + ".apk"))) { EditorApplication.Exit(1); return; }
            }
        }
        finally
        {
            EditorUserBuildSettings.buildAppBundle = bundleWas;
            UnityEditor.Android.UserBuildSettings.DebugSymbols.level = symbolsWas;
        }
        EditorApplication.Exit(0);
    }

    // NOTE: ProjectSettings.asset keeps AndroidKeystoreName empty on purpose (it used to
    // hold a Windows-only absolute path). Release signing is configured here from
    // PAUSE_KEYSTORE_PATH (default Pause/PauseKey.keystore) and the PAUSE_* passwords;
    // an editor "Build and Run" without them is debug-signed. Point Project Settings >
    // Player > Publishing Settings at your own keystore locally if you need it, and do
    // not commit that path.
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

    // iOS Simulator export (make build-ios-sim): same project, Simulator SDK, own folder.
    public static void BuildIOSSim()
    {
        var sdkWas = PlayerSettings.iOS.sdkVersion;
        PlayerSettings.iOS.sdkVersion = iOSSdkVersion.SimulatorSDK;
        try { Run(BuildTarget.iOS, Path.Combine(OutputRoot, "iOS-sim"), DevDefine); }
        finally { PlayerSettings.iOS.sdkVersion = sdkWas; }
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
        if (target == BuildTarget.Android)
        {
            var grp = UnityEditor.Build.NamedBuildTarget.Android;
            Debug.Log("[BUILD] stripping=" + PlayerSettings.GetManagedStrippingLevel(grp)
                + " il2cppCodeGen=" + PlayerSettings.GetIl2CppCodeGeneration(grp)
                + " minifyRelease=" + EditorUserBuildSettings.androidReleaseMinification);
        }
        BuildReport report = BuildPipeline.BuildPlayer(options);
        BuildSummary summary = report.summary;

        Debug.Log("[BUILD] result=" + summary.result
            + " errors=" + summary.totalErrors
            + " warnings=" + summary.totalWarnings
            + " size=" + summary.totalSize + " bytes"
            + " time=" + summary.totalTime);

        LogStepTimings(report);
        if (summary.result == BuildResult.Succeeded) LogSizeReport(report);

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

    // Build report steps with durations ([STEP] lines in the log, and
    // Builds/build-timings.txt) so slow phases are visible without a profiler.
    static void LogStepTimings(BuildReport report)
    {
        var lines = new System.Collections.Generic.List<string>();
        foreach (var step in report.steps)
            lines.Add(string.Format("{0,8:F1}s  {1}{2}", step.duration.TotalSeconds, new string(' ', step.depth * 2), step.name));
        lines.Add(string.Format("{0,8:F1}s  TOTAL", report.summary.totalTime.TotalSeconds));
        foreach (var l in lines) Debug.Log("[STEP] " + l);
        try { File.WriteAllLines(Path.Combine(OutputRoot, "build-timings.txt"), lines.ToArray()); } catch (Exception) { }
    }

    // Size report: the biggest packed assets (their size inside the build,
    // i.e. after texture compression) and totals per top-level asset folder.
    // Lines are tagged [SIZE] so they are easy to grep from the log.
    static void LogSizeReport(BuildReport report)
    {
        var rows = report.packedAssets
            .SelectMany(p => p.contents)
            .Where(c => !string.IsNullOrEmpty(c.sourceAssetPath))
            .GroupBy(c => c.sourceAssetPath)
            .Select(g => new { path = g.Key, size = g.Sum(c => (long)c.packedSize) })
            .OrderByDescending(r => r.size)
            .ToList();
        foreach (var r in rows.Take(120))
            Debug.Log("[SIZE] " + r.size + " " + r.path);
        // the full list, one "<bytes> <path>" per line, for scripts
        File.WriteAllLines(Path.Combine(OutputRoot, "size-report.txt"), rows.Select(r => r.size + " " + r.path).ToArray());
        foreach (var g in rows.GroupBy(r => string.Join("/", r.path.Split('/').Take(Math.Min(4, r.path.Split('/').Length - 1))))
                     .Select(g => new { g.Key, size = g.Sum(r => r.size) }).OrderByDescending(g => g.size).Take(60))
            Debug.Log("[SIZEDIR] " + g.size + " " + g.Key);
    }
}
