using System;
using System.IO;
using System.Reflection;
using UnityEngine;

// The Options screen (player view, effects + music on) at volume 0 / 30% / 70% / 100%
// on 1080x2400 and 1080x1920 phones, through the screen-fit rig at native height.
//
//   scripts/unity-batch.sh -executeMethod OptionsIconsPreview.Run
//   (output: $OPTIONS_ICONS_DIR, else Builds/OptionsIcons)
public static class OptionsIconsPreview
{
    public static void Run()
    {
        string dir = Environment.GetEnvironmentVariable("OPTIONS_ICONS_DIR");
        if (string.IsNullOrEmpty(dir)) dir = "Builds/OptionsIcons";
        Directory.CreateDirectory(dir);
        using (new TestHarness.Sandbox())
        {
            foreach (var devId in new[] { "and-1080x2400-gesture", "and-1080x1920" })
            {
                var device = FitDevice.Find(devId);
                foreach (int pct in new[] { 0, 30, 70, 100 })
                {
                    float v = pct / 100f;
                    var screen = new FitScreen
                    {
                        id = "options-" + pct, scene = "leaderboardS3", title = "Options volume " + pct, fullBleed = true,
                        stage = rig =>
                        {
                            PlayerPrefs.SetFloat(SoundSettings.SfxVolumeKey, v);
                            PlayerPrefs.SetFloat(SoundSettings.MusicVolumeKey, v);
                            typeof(ScreenFitScreens).GetMethod("Options", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)
                                .Invoke(null, new object[] { rig, 5 });
                        },
                    };
                    string name = devId.Replace("and-", "") + "_vol" + pct;
                    ScreenFitRunner.Run(screen, device, Path.Combine(dir, name + ".png"), device.h);
                }
            }
        }
        UnityEditor.EditorApplication.Exit(0);
    }
}
