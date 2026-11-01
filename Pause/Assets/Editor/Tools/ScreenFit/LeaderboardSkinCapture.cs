using System;
using System.IO;
using UnityEngine;

// Renders the leaderboard panel with ships in their recorded skins.
//   SCREEN_FIT_DIR = output folder
//   Unity -projectPath Pause -executeMethod LeaderboardSkinCapture.Run   (needs a GPU: no -nographics)
public static class LeaderboardSkinCapture
{
    public static void Run()
    {
        string dir = Environment.GetEnvironmentVariable("SCREEN_FIT_DIR") ?? "Builds/LeaderboardSkin";
        Directory.CreateDirectory(dir);
        using (new TestHarness.Sandbox())
        {
            foreach (var d in new[] { FitDevice.Find("and-1080x2340-notch"), FitDevice.Find("tab-1600x2560") })
            {
                var shot = ScreenFitRunner.Run(ScreenFitScreens.Find("leaderboard"), d, Path.Combine(dir, "leaderboard_" + d.w + "x" + d.h + ".png"), 0);
                Debug.Log("[LBSKIN] " + d.id + " err=" + shot.error + " failures=" + shot.failures);
            }
        }
        UnityEditor.EditorApplication.Exit(0);
    }
}
