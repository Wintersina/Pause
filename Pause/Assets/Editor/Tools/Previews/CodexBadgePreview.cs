using System;
using System.IO;
using System.Linq;
using UnityEngine;

// The home screen with 0, 3 and 12 unclaimed achievements (the Codex button's
// notification badge), through the ScreenFit rig:
//
//   scripts/unity-batch.sh -executeMethod CodexBadgePreview.Run
//   (writes home_0.png, home_3.png, home_12.png to $CODEX_BADGE_DIR, else /private/tmp)
public static class CodexBadgePreview
{
    public static void Run()
    {
        string dir = Environment.GetEnvironmentVariable("CODEX_BADGE_DIR");
        if (string.IsNullOrEmpty(dir)) dir = "/private/tmp";
        Directory.CreateDirectory(dir);
        foreach (int n in new[] { 0, 3, 12 })
        {
            int count = n;
            var screen = new FitScreen
            {
                id = "home-badge-" + n, scene = "startS4", title = "badge " + n, fullBleed = true,
                stage = rig =>
                {
                    AchievementStore.ResetAll();
                    foreach (var d in AchievementCatalog.All.Where(x => AchievementCatalog.IsActive(x)).Take(count)) AchievementStore.Unlock(d);
                    ScreenFitScreens.HomeBase(rig);
                }
            };
            using (new TestHarness.Sandbox())
                ScreenFitRunner.Run(screen, FitDevice.Find("and-1080x2340-notch"), Path.Combine(dir, "home_" + n + ".png"), 2340);
        }
        CodexPreview.NewDotsShot(dir);
        UnityEditor.EditorApplication.Exit(0);
    }
}
