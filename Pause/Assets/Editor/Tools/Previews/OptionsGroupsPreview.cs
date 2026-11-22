using System;
using System.IO;
using UnityEngine;

// The player's Options screen (developer mode off, every runtime row) through the screen-fit rig on the phone, the
// Z Flip cover, a tablet and a wide foldable, plus the SHIP PERCH direction at each of its 3 buttons.
//
//   scripts/unity-batch.sh -executeMethod OptionsGroupsPreview.Run
//   (output: $OPTIONS_GROUPS_DIR, else Builds/OptionsGroups)
public static class OptionsGroupsPreview
{
    static readonly string[] Devices = { "and-1080x2400-gesture", "flip7-cover-948x1048", "tab-1600x2560", "fold-1812x2176" };

    public static void Run()
    {
        string dir = Environment.GetEnvironmentVariable("OPTIONS_GROUPS_DIR");
        if (string.IsNullOrEmpty(dir)) dir = "Builds/OptionsGroups";
        Directory.CreateDirectory(dir);
        using (new TestHarness.Sandbox())
        {
            foreach (var devId in Devices)
            {
                var device = FitDevice.Find(devId);
                for (int stop = 0; stop < PerchSettings.TiltCount; stop++)
                {
                    int s = stop;
                    var screen = new FitScreen
                    {
                        id = "options-groups-" + s, scene = "leaderboardS3", title = "Options groups", fullBleed = true,
                        stage = rig =>
                        {
                            PlayerPrefs.SetString("HasDoneTut", "true");
                            DeveloperUnlocks.TestAvailableOverride = false;
                            PerchSettings.ReloadForTests();
                            PerchSettings.SetTilt(PerchSettings.TiltOfSlot(s));
                            ScreenFitScreens.StagePlayerOptions(rig);
                            var r = rig.PixelRect((RectTransform)GameObject.Find(PerchOptions.SliderName).transform);
                            File.WriteAllText(Path.Combine(dir, devId + "_perch.txt"), r.x + "," + r.y + "," + r.width + "," + r.height);
                        },
                    };
                    var shot = ScreenFitRunner.Run(screen, device, Path.Combine(dir, devId + "_stop" + (s + 1) + ".png"), Mathf.Min(device.h, 1800));
                    DeveloperUnlocks.TestAvailableOverride = null;
                    foreach (var f in shot.findings) Debug.Log("[OPTGROUPS] " + devId + " stop" + (s + 1) + " " + (f.waived ? "WAIVED " : "") + f.kind + " " + f.element + ": " + f.detail);
                }
            }
        }
        UnityEditor.EditorApplication.Exit(0);
    }
}
