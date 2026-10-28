using System;
using System.IO;
using UnityEngine;

// Frost and Verdant HUD glyph contrast sweep (see ScreenFitScreens.HudContrast).
//   SCREEN_FIT_DIR = output folder
//   Unity -batchmode -quit -projectPath Pause -executeMethod HudContrastCapture.Run
public static class HudContrastCapture
{
    public static void Run()
    {
        string dir = Environment.GetEnvironmentVariable("SCREEN_FIT_DIR") ?? "Builds/HudContrast";
        var devices = new[] { FitDevice.Find("and-480x854"), FitDevice.Find("and-720x1280"), FitDevice.Find("and-1080x2340-notch") };
        using (new TestHarness.Sandbox())
        {
            foreach (int w in new[] { 1, 2 })
                foreach (var d in devices)
                {
                    var shot = ScreenFitRunner.Run(ScreenFitScreens.HudContrast(w), d, Path.Combine(dir, "_last_" + w + "_" + d.w + ".png"), 0);
                    Debug.Log("[HCR] world " + w + " " + d.id + " err=" + shot.error);
                }
        }
        UnityEditor.EditorApplication.Exit(0);
    }
}
