using System;
using System.IO;
using UnityEngine;
using UnityEngine.UI;

// Renders the home screen with every menu item in its highlighted (pressed) state.
//   SCREEN_FIT_DIR = output folder
//   Unity -projectPath Pause -executeMethod HomeHighlightCapture.Run   (needs a GPU: no -nographics)
public static class HomeHighlightCapture
{
    public static readonly string[] Items = { "PlayButton", "shopButton", "achivButton", "LeaderboardButton", "CodexButton", "PrivacyButton", "QuitButton" };

    public static void Highlight(ScreenFitRig rig)
    {
        ScreenFitScreens.HomeBase(rig);
        foreach (string name in Items)
        {
            var go = GameObject.Find(name);
            var button = go != null ? go.GetComponent<Button>() : null;
            if (button == null || button.targetGraphic == null) continue;
            button.targetGraphic.canvasRenderer.SetColor(button.colors.pressedColor);
            var corners = new Vector3[4];
            ((RectTransform)go.transform).GetWorldCorners(corners);
            Debug.Log("[HOMEHL] " + name + " " + corners[0] + " " + corners[2]);
        }
    }

    public static void Run()
    {
        string dir = Environment.GetEnvironmentVariable("SCREEN_FIT_DIR") ?? "Builds/HomeHighlight";
        Directory.CreateDirectory(dir);
        var screen = new FitScreen { id = "home-highlight", fullBleed = true, scene = "startS4", title = "Home, items highlighted", stage = Highlight };
        using (new TestHarness.Sandbox())
        {
            foreach (var d in new[] { FitDevice.Find("and-1080x2340-notch"), FitDevice.Find("and-480x854") })
            {
                var shot = ScreenFitRunner.Run(screen, d, Path.Combine(dir, "home_" + d.w + "x" + d.h + ".png"), 0);
                Debug.Log("[HOMEHL] " + d.id + " err=" + shot.error);
            }
        }
        UnityEditor.EditorApplication.Exit(0);
    }
}
