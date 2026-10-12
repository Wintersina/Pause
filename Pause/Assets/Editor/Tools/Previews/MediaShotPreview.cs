using System.IO;
using UnityEngine;

// Full-resolution captures (default 1080x2520, the Galaxy Z Flip7) of the screens
// that carry the HapticGate mark and the gameplay clips:
//   -executeMethod MediaShotPreview.Render -shotDir <dir> [-frames <dir with *.png>] [-device flip7-1080x2520]
//
// Writes credits.png, options.png (the how-to-play clip's place) and, for every
// PNG in -frames (stills decoded from the shipped mp4s with ffmpeg: a headless
// editor cannot play a VideoPlayer), home_video_<name>.png through the real
// TitleVideo quad and options_clip_<name>.png through the real HowToPlayClip.
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
        string frames = Arg("-frames", null);
        var device = FitDeviceLookup(Arg("-device", "flip7-1080x2520"));
        Directory.CreateDirectory(dir);
        LoopingClip.Disabled = true;   // no VideoPlayer in a headless editor

        foreach (string id in new[] { "credits", "options-mute-on", "home" })
            ScreenFitRunner.Run(ScreenFitScreens.Find(id), device, Path.Combine(dir, id + ".png"), 0);

        if (frames != null && Directory.Exists(frames))
        {
            foreach (string f in Directory.GetFiles(frames, "*.png"))
            {
                string name = Path.GetFileNameWithoutExtension(f);
                var tex = new Texture2D(2, 2, TextureFormat.RGB24, false);
                tex.LoadImage(File.ReadAllBytes(f)); tex.hideFlags = HideFlags.HideAndDontSave;
                var screen = new FitScreen { id = "home-video", scene = "startS4", fullBleed = true,
                    stage = r =>
                    {
                        ScreenFitScreens.HomeBase(r);
                        var video = new GameObject("~TitleVideo").AddComponent<TitleVideo>();
                        video.ShowTexture(tex);
                        video.SetFade(1f);
                    } };
                ScreenFitRunner.Run(screen, device, Path.Combine(dir, "home_video_" + name + ".png"), 0);
                var opt = new FitScreen { id = "options", scene = "leaderboardS3", fullBleed = true,
                    stage = r => ScreenFitScreens.Find("options-mute-on").stage(r) };
                HowToPlayClip.PreviewTexture = tex;
                ScreenFitRunner.Run(opt, device, Path.Combine(dir, "options_clip_" + name + ".png"), 0);
                HowToPlayClip.PreviewTexture = null;
                Object.DestroyImmediate(tex);
            }
        }
        EditorApplicationExit();
    }

    static FitDevice FitDeviceLookup(string id) { return FitDevice.Find(id); }
    static void EditorApplicationExit() { UnityEditor.EditorApplication.Exit(0); }
}
