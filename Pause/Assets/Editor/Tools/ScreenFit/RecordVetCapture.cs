using System;
using System.IO;
using UnityEngine;

// Renders the NEW tag and a NEW RECORD popup in every world's HUD, one board per world in turn.
//   SCREEN_FIT_DIR = output folder
//   Unity -batchmode -quit -projectPath Pause -executeMethod RecordVetCapture.Run
public static class RecordVetCapture
{
    public static void Run()
    {
        string dir = Environment.GetEnvironmentVariable("SCREEN_FIT_DIR") ?? "Builds/RecordVet";
        string[] names = { "space", "frost", "verdant", "ember", "tide", "storm", "desert" };
        var boards = LeaderboardBoards.All;
        var devices = new[] { FitDevice.Find("and-480x854"), FitDevice.Find("and-720x1280"), FitDevice.Find("flip7-cover-948x1048") };
        using (new TestHarness.Sandbox())
        {
            for (int w = 0; w < names.Length; w++)
                foreach (var d in devices)
                {
                    string board = boards[w % boards.Length].id;
                    var shot = ScreenFitRunner.Run(ScreenFitScreens.RecordVet(w, board), d, Path.Combine(dir, names[w] + "_" + board + "_" + d.w + "x" + d.h + ".png"), 0);
                    Debug.Log("[RECVET] " + names[w] + " " + d.id + " " + board + " err=" + shot.error + " failures=" + shot.failures);
                }
        }
        UnityEditor.EditorApplication.Exit(0);
    }
}
