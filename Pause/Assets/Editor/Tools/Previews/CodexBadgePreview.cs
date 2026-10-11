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
        CropShot(dir);
        CodexPreview.NewDotsShot(dir);
        UnityEditor.EditorApplication.Exit(0);
    }

    // 6x crop of the bubble art with counts 1, 3, 7 and 9+ side by side
    static void CropShot(string dir)
    {
        const int k = 6, cell = 13 * k, gap = 2 * k;
        var labels = new[] { "1", "3", "7", "9+" };
        var disc = CodexHomeButton.BadgeSprite().texture;
        var bg = new Color32(40, 30, 60, 255);
        var outTex = new Texture2D(labels.Length * (cell + gap) + gap, cell + 2 * gap, TextureFormat.RGBA32, false);
        for (int y = 0; y < outTex.height; y++) for (int x = 0; x < outTex.width; x++) outTex.SetPixel(x, y, bg);
        for (int i = 0; i < labels.Length; i++)
        {
            var glyph = CodexHomeButton.CountTexture(labels[i]);
            for (int y = 0; y < 13; y++)
                for (int x = 0; x < 13; x++)
                {
                    Color c = disc.GetPixel(x, y);
                    Color g = glyph.GetPixel(x, y);
                    if (g.a > .5f) c = g;
                    if (c.a < .5f) continue;
                    for (int dy = 0; dy < k; dy++) for (int dx = 0; dx < k; dx++)
                        outTex.SetPixel(gap + i * (cell + gap) + x * k + dx, gap + y * k + dy, c);
                }
        }
        outTex.Apply();
        File.WriteAllBytes(Path.Combine(dir, "badge_6x.png"), outTex.EncodeToPNG());
    }
}
