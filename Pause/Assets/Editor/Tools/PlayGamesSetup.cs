using System;
using System.IO;
using UnityEditor;
using UnityEngine;

// One-shot, re-runnable Google Play Games configuration for batch mode:
//
//   Unity -batchmode -quit -projectPath Pause -buildTarget Android \
//         -executeMethod PlayGamesSetup.Run
//
// Does what the plugin's "Window > Google Play Games > Setup > Android setup"
// dialog does, without the dialog:
//   - enables Unity's custom Gradle templates so the External Dependency
//     Manager can add play-services-games-v2 to the Gradle build (instead of
//     copying AARs into Assets)
//   - writes the Play Games APP_ID into the plugin's AndroidManifest/GameInfo
//   - resolves the Android dependencies
//
// The APP_ID is the Play Console "Games services" project id. It is the same
// number encoded in every StringHolder id (CgkI<id>EAIQ..) and the one the
// 2016 build shipped with.
public static class PlayGamesSetup
{
    public const string AppId = "528367766818";

    static readonly string[] Templates = { "mainTemplate.gradle", "settingsTemplate.gradle", "gradleTemplate.properties" };

    public static void Run()
    {
        int code = 0;
        try
        {
            EnableGradleTemplates();
            if (!GooglePlayGames.Editor.GPGSAndroidSetupUI.PerformSetup(null, AppId, null))
                throw new Exception("GPGS setup failed");
            Debug.Log("[GPGS] app id configured: " + AppId);
            Resolve();
            AssetDatabase.SaveAssets();
        }
        catch (Exception e)
        {
            Debug.LogError("[GPGS] setup failed: " + e);
            code = 1;
        }
        EditorApplication.Exit(code);
    }

    static void EnableGradleTemplates()
    {
        string source = Path.Combine(BuildPipeline.GetPlaybackEngineDirectory(BuildTarget.Android, BuildOptions.None),
                                     "Tools/GradleTemplates");
        string dest = "Assets/Plugins/Android";
        Directory.CreateDirectory(dest);
        foreach (string name in Templates)
        {
            string target = Path.Combine(dest, name);
            if (File.Exists(target)) continue;
            File.Copy(Path.Combine(source, name), target);
            Debug.Log("[GPGS] enabled custom " + name);
        }
        AssetDatabase.Refresh();
    }

    static void Resolve()
    {
        var resolver = Type.GetType("GooglePlayServices.PlayServicesResolver, Google.JarResolver");
        if (resolver == null)
        {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                resolver = asm.GetType("GooglePlayServices.PlayServicesResolver");
                if (resolver != null) break;
            }
        }
        if (resolver == null) throw new Exception("External Dependency Manager not loaded");
        var resolveSync = resolver.GetMethod("ResolveSync", new[] { typeof(bool) });
        bool ok = (bool)resolveSync.Invoke(null, new object[] { true });
        Debug.Log("[GPGS] dependency resolution " + (ok ? "succeeded" : "FAILED"));
        if (!ok) throw new Exception("dependency resolution failed");
    }
}
