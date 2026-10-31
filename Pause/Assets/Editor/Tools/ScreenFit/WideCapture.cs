using System;
using System.IO;
using UnityEngine;

// Dock / codex screens on the Z Flip7 cover screen next to a tall phone, as PNGs:
//
//   scripts/unity-batch.sh -executeMethod WideCapture.Run
//   (env WIDE_CAPTURE_DIR, else /private/tmp/wide-capture; WIDE_CAPTURE_SCREENS = comma list)
public static class WideCapture
{
    static readonly string[] Devices = { "flip7-cover-948x1048", "flip7-1080x2520" };
    static readonly string[] Screens = { "dock", "dock-popup-owned", "dock-popup-buy", "codex-grid", "codex-detail", "codex-music", "codex-world-detail", "codex-backdrops", "home", "options", "leaderboard", "credits" };

    public static void Run()
    {
        string dir = Environment.GetEnvironmentVariable("WIDE_CAPTURE_DIR");
        if (string.IsNullOrEmpty(dir)) dir = "/private/tmp/wide-capture";
        Directory.CreateDirectory(dir);
        string only = Environment.GetEnvironmentVariable("WIDE_CAPTURE_SCREENS");
        var ids = string.IsNullOrEmpty(only) ? Screens : only.Split(',');
        foreach (var id in ids)
            foreach (var dev in Devices)
                using (new TestHarness.Sandbox())
                {
                    var shot = ScreenFitRunner.Run(ScreenFitScreens.Find(id), FitDevice.Find(dev), Path.Combine(dir, id + "_" + dev + ".png"), 2520);
                    foreach (var f in shot.findings) Debug.Log("[WIDE] " + id + " @ " + dev + " " + (f.waived ? "WAIVED " : "") + f.kind + " " + f.element + ": " + f.detail);
                }
        UnityEditor.EditorApplication.Exit(0);
    }
}
