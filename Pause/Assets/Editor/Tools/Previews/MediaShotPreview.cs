using System.IO;

// Full-resolution captures (default 1080x2520, the Galaxy Z Flip7) of the menu
// screens: home (the in-game backdrop behind the title, no video), Options and
// the credits roll with the HapticGate mark:
//   -executeMethod MediaShotPreview.Render -shotDir <dir> [-device flip7-1080x2520]
// The splash's final frame comes from GateShotPreview.
public static class MediaShotPreview
{
    static string Arg(string name, string fallback)
    {
        var args = System.Environment.GetCommandLineArgs();
        for (int i = 0; i < args.Length - 1; i++) if (args[i] == name) return args[i + 1];
        return fallback;
    }

    public static void Render()
    {
        string dir = Arg("-shotDir", "MediaShots");
        var device = FitDevice.Find(Arg("-device", "flip7-1080x2520"));
        Directory.CreateDirectory(dir);
        foreach (string id in new[] { "credits", "options-mute-on", "home" })
            ScreenFitRunner.Run(ScreenFitScreens.Find(id), device, Path.Combine(dir, id + ".png"), 0);
        UnityEditor.EditorApplication.Exit(0);
    }
}
