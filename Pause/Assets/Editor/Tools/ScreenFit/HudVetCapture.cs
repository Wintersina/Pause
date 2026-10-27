using System;
using System.IO;
using UnityEngine;

// Renders every world's HUD with worst-case text at two device sizes.
//   SCREEN_FIT_DIR = output folder
//   Unity -batchmode -quit -projectPath Pause -executeMethod HudVetCapture.Run
public static class HudVetCapture
{
    public static void Run()
    {
        string dir = Environment.GetEnvironmentVariable("SCREEN_FIT_DIR") ?? "Builds/HudVet";
        string[] names = { "space", "frost", "verdant", "ember", "tide", "storm", "desert" };
        var devices = new[] { FitDevice.Find("and-480x854"), FitDevice.Find("and-1080x2340-notch") };
        using (new TestHarness.Sandbox())
        {
            for (int w = 0; w < names.Length; w++)
                foreach (var d in devices)
                {
                    var shot = ScreenFitRunner.Run(ScreenFitScreens.HudVet(w), d, Path.Combine(dir, names[w] + "_" + d.w + "x" + d.h + ".png"), 0);
                    Debug.Log("[HUDVET] " + names[w] + " " + d.id + " err=" + shot.error + " failures=" + shot.failures);
                }
        }
        UnityEditor.EditorApplication.Exit(0);
    }
}
