using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;

// Stills of the codex BACKDROPS tab and the backdrop viewer, into $CODEX_BACKDROP_DIR (default /private/tmp):
//
//   Unity -batchmode -quit -projectPath Pause -executeMethod CodexBackdropPreview.Run
//     viewer-<world>-v<n>.png   the viewer's own frame (no HUD) of every backdrop, 20 s into the view
//   Unity -batchmode -quit -projectPath Pause -executeMethod CodexBackdropPreview.Shots
//     codex-backdrops-tab.png / codex-backdrops-viewer.png   the real screens on a 1080x2400 phone
//     (the ScreenFit rig: safe area, HUD and all), everything unlocked (developer mode)
public static class CodexBackdropPreview
{
    static string Dir
    {
        get
        {
            string dir = Environment.GetEnvironmentVariable("CODEX_BACKDROP_DIR");
            if (string.IsNullOrEmpty(dir)) dir = "/private/tmp";
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    public static void Run() { Exit(() => Frames()); }
    public static void Shots() { Exit(() => Screens()); }
    public static void All() { Exit(() => { Frames(); Screens(); }); }

    static void Exit(Action work)
    {
        int code = 0;
        try { work(); }
        catch (Exception e) { Debug.LogException(e); code = 1; }
        EditorApplication.Exit(code);
    }

    static void Frames()
    {
        {
            using (new TestHarness.Sandbox())
            using (ScreenInfo.Override(1080, 2400, new Rect(0, 0, 1080, 2400)))
            {
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                var es = new GameObject("EventSystem", typeof(EventSystem));
                DeveloperUnlocks.SetEnabled(true);
                foreach (var b in CodexBackdrops.All)
                {
                    var viewer = CodexBackdropViewer.Open(null, b);
                    if (viewer == null) { Debug.LogWarning("[CodexBackdropPreview] cannot open " + b.id); continue; }
                    for (int i = 0; i < 600; i++) viewer.Step(1f / 30f);
                    var rt = viewer.Target;
                    var prev = RenderTexture.active;
                    RenderTexture.active = rt;
                    var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
                    tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
                    tex.Apply();
                    RenderTexture.active = prev;
                    File.WriteAllBytes(Path.Combine(Dir, "viewer-" + b.world.ToLowerInvariant() + "-v" + b.variant + ".png"), tex.EncodeToPNG());
                    UnityEngine.Object.DestroyImmediate(tex);
                    viewer.Close();
                }
                UnityEngine.Object.DestroyImmediate(es);
                DeveloperUnlocks.SetEnabled(false);
            }
        }
    }

    static void Screens()
    {
        {
            var device = FitDevice.Find("and-1080x2400-corner");
            foreach (string id in new[] { "codex-backdrops", "codex-backdrop-viewer" })
            {
                var screen = ScreenFitScreens.Find(id);
                using (new TestHarness.Sandbox())
                {
                    var shot = ScreenFitRunner.Run(screen, device, Path.Combine(Dir, id + ".png"), 2400);
                    if (shot.error != null) Debug.LogWarning("[CodexBackdropPreview] " + id + ": " + shot.error);
                }
            }
        }
    }
}
