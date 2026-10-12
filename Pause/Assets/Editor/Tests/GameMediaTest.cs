using System.IO;
using UnityEditor.SceneManagement;
using UnityEngine;

// No gameplay video ships (user, 2026-10-12). The game-media merge had added a
// recorded Space loop behind the title menu (TitleVideo, title_loop.mp4) and a
// "HOW TO PLAY" loop on the Options screen (HowToPlayClip, tutorial_demo.mp4),
// both played by LoopingClip from StreamingAssets/Video. The user rejected both:
// the home backdrop is the real in-game backdrop layers (MenuBackdrop) and
// Options has no clip. This keeps them from coming back.
public static class GameMediaTest
{
    static int fails;
    static void Check(string what, bool ok)
    {
        Debug.Log((ok ? "[MEDIA] PASS  " : "[MEDIA] FAIL  ") + what);
        if (!ok) fails++;
    }

    public static void Run() { TestHarness.Exit(Execute()); }

    public static int Execute()
    {
        fails = 0;
        using var sandbox = new TestHarness.Sandbox();
        Files();
        Code();
        Scenes();
        Debug.Log("[MEDIA] failures: " + fails);
        return fails;
    }

    static void Files()
    {
        int clips = 0;
        foreach (string ext in new[] { "*.mp4", "*.webm", "*.mov", "*.m4v" })
            clips += Directory.GetFiles("Assets", ext, SearchOption.AllDirectories).Length;
        Check("no video files under Assets (" + clips + " found)", clips == 0);
        Check("no StreamingAssets/Video folder", !Directory.Exists("Assets/StreamingAssets/Video"));
    }

    static void Code()
    {
        foreach (string type in new[] { "TitleVideo", "HowToPlayClip", "LoopingClip" })
        {
            bool found = false;
            foreach (var asm in System.AppDomain.CurrentDomain.GetAssemblies())
                if (asm.GetType(type) != null) found = true;
            Check("no " + type + " type", !found);
        }
        int users = 0;
        foreach (string f in Directory.GetFiles("Assets/Scripts", "*.cs", SearchOption.AllDirectories))
        {
            string text = File.ReadAllText(f);
            if (text.Contains("VideoPlayer") || text.Contains("UnityEngine.Video"))
            {
                users++;
                Debug.Log("[MEDIA]   video use in " + f);
            }
        }
        Check("no game script plays video", users == 0);
        Check("the home screen rolls the in-game backdrop (MenuBackdrop)", MenuBackdropSelection.IsMenuScene("startS4"));
    }

    static void Scenes()
    {
        foreach (string scene in new[] { "startS4", "leaderboardS3" })
        {
            EditorSceneLoader.Open(scene, OpenSceneMode.Single);
            bool none = true;
            foreach (var b in Object.FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (b != null && b.GetType().Namespace == "UnityEngine.Video") none = false;
            Check(scene + " has no video component", none);
        }
    }
}
