using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using System.IO;

public static class HomePauseTest
{
    static int failures;

    static void Check(string label, bool condition)
    {
        if (!condition) failures++;
        Debug.Log("[HP] " + (condition ? "PASS " : "FAIL ") + label);
    }

    public static void Run()
    {
        failures = 0;
        EditorSceneManager.OpenScene("Assets/Scenes/startS4.unity");
        var menu = Object.FindFirstObjectByType<startMenu>();
        Check("home menu exists", menu != null);
        if (menu != null)
        {
            menu.LayoutHome();
            Canvas.ForceUpdateCanvases();
            var panel = GameObject.Find("UIPanel").GetComponent<RectTransform>();
            Check("actions stay in a compact group", panel.sizeDelta.y <= 330f && panel.sizeDelta.y > 0f);
            foreach (string name in new[] { "LogOutButton", "QuitButton" })
            {
                var footer = GameObject.Find(name).GetComponent<RectTransform>();
                Check(name + " anchors to bottom", footer.anchorMin.y == 0f && footer.anchorMax.y == 0f);
                Check(name + " remains above bottom edge", footer.anchoredPosition.y > footer.sizeDelta.y * 0.5f);
            }
            var group = panel.GetComponent<VerticalLayoutGroup>();
            Check("actions use a small consistent gap", group.spacing == 12f);
            RenderHome(menu, 1100, 800);
            RenderHome(menu, 1080, 2400);
        }

        // PauseOverlay (the big, un-stripped gameIcon.png shown centre-screen on
        // pause) and moveStarsBackground's stripped-and-cropped glow icon fired
        // on the identical condition -- Time.timeScale == 0 && !playerDied --
        // so both showed at once. The user asked to keep only the smaller one;
        // PauseOverlay is removed from both scenes rather than adjusted, so
        // this now guards against it quietly coming back.
        foreach (string scene in new[] { "gameS1", "tutorialS5" })
        {
            EditorSceneManager.OpenScene("Assets/Scenes/" + scene + ".unity");
            var overlay = Object.FindFirstObjectByType<PauseOverlay>();
            Check(scene + " no longer shows the big pause overlay", overlay == null);
        }
        Check("paused living player sees icon", PauseOverlay.ShouldShow(0f, false));
        Check("running game hides icon", !PauseOverlay.ShouldShow(1f, false));
        Check("death screen hides pause icon", !PauseOverlay.ShouldShow(0f, true));
        Debug.Log("[HP] failures: " + failures);
        EditorApplication.Exit(failures == 0 ? 0 : 1);
    }

    static void RenderHome(startMenu menu, int width, int height)
    {
        var camera = Camera.main;
        var canvas = GameObject.Find("MainMenuCanvas").GetComponent<Canvas>();
        var target = new RenderTexture(width, height, 24);
        camera.targetTexture = target;
        camera.orthographicSize = CameraFit.ComputeSize(5f, 2.85f, width, height);
        canvas.renderMode = RenderMode.ScreenSpaceCamera;
        canvas.worldCamera = camera;
        canvas.planeDistance = 1f;
        menu.LayoutHome();
        // Match the target height explicitly: editor Screen dimensions are
        // independent of the render texture used for these aspect previews.
        var scaler = canvas.GetComponent<CanvasScaler>();
        scaler.enabled = false;
        canvas.scaleFactor = height / 1000f;
        Canvas.ForceUpdateCanvases();
        menu.LayoutHome();
        canvas.scaleFactor = height / 1000f;
        Canvas.ForceUpdateCanvases();
        camera.Render();
        var previous = RenderTexture.active;
        RenderTexture.active = target;
        var png = new Texture2D(width, height, TextureFormat.RGB24, false);
        png.ReadPixels(new Rect(0, 0, width, height), 0, 0);
        png.Apply();
        string path = "/private/tmp/pause-home-" + width + "x" + height + ".png";
        File.WriteAllBytes(path, png.EncodeToPNG());
        Debug.Log("[HP] screenshot " + path);
        RenderTexture.active = previous;
        camera.targetTexture = null;
        Object.DestroyImmediate(png);
        Object.DestroyImmediate(target);
    }
}
