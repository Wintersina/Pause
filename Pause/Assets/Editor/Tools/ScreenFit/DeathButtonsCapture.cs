using System;
using System.IO;
using UnityEngine;

// Renders the flight-complete panel on every world's frame at two device sizes.
//   SCREEN_FIT_DIR = output folder
//   Unity -projectPath Pause -executeMethod DeathButtonsCapture.Run   (needs a GPU: no -nographics)
public static class DeathButtonsCapture
{
    public static void Run()
    {
        string dir = Environment.GetEnvironmentVariable("SCREEN_FIT_DIR") ?? "Builds/DeathButtons";
        string[] names = { "space", "frost", "verdant", "ember", "tide", "storm", "desert" };
        var devices = new[] { FitDevice.Find("and-480x854"), FitDevice.Find("and-1080x2340-notch") };
        Directory.CreateDirectory(dir);
        using (new TestHarness.Sandbox())
        {
            for (int w = 0; w < names.Length; w++)
                foreach (var d in devices)
                {
                    var shot = ScreenFitRunner.Run(ScreenFitScreens.DeathOn(w), d, Path.Combine(dir, names[w] + "_" + d.w + "x" + d.h + ".png"), 0);
                    Debug.Log("[DEATHBTN] " + names[w] + " " + d.id + " err=" + shot.error + " failures=" + shot.failures + " minTextPt=" + shot.minTextPt.ToString("F1") + " (" + shot.minTextName + ")");
                    foreach (var f in shot.findings) if (!f.waived) Debug.Log("[DEATHFIND] " + names[w] + " " + d.id + " " + f.kind + " " + f.element + ": " + f.detail);
                }
        }
        UnityEditor.EditorApplication.Exit(0);
    }
}
