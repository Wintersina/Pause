using System.IO;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.UI;

// The READY / GO!!!! start-of-run caption wears the PAUSED lettering
// (LaunchBanner), READY and GO are distinct, and no Text that a scene ships
// uses Unity's built-in font beyond the known hidden button templates.
public static class LaunchBannerTest
{
    static int fails;
    static void Check(string what, bool ok)
    {
        Debug.Log((ok ? "[LB] PASS  " : "[LB] FAIL  ") + what);
        if (!ok) fails++;
    }

    public static void Run() { TestHarness.Exit(Execute()); }

    // Built-in-font Texts left in scenes: hidden template buttons (REPLAY and
    // MENU in gameS1, CONTINUE in tutorialS5) that PauseQuickActions /
    // TutorialCompletePanel clone and restyle; they are never shown as is.
    static readonly (string scene, int count)[] BuiltinAllowed = { ("gameS1", 2), ("tutorialS5", 1) };

    public static int Execute()
    {
        fails = 0;
        using var sandbox = new TestHarness.Sandbox();

        LaunchBanner.Colours(false, out var rt, out var rb, out _, out _);
        LaunchBanner.Colours(true, out var gt, out var gb, out _, out _);
        Check("READY and GO use different fills", rt != gt && rb != gb);
        Check("GO is the PAUSED red", gt == PausedLabel.FillTop && gb == PausedLabel.FillBottom);
        Check("READY fill is lighter than GO's", rt.g > gt.g && rb.g > gb.g);

        Check("pop squashes, then stretches, then rests",
              LaunchBanner.PopScale(0f).x > 1f && LaunchBanner.PopScale(0f).y < 1f &&
              LaunchBanner.PopScale(PausedOverlayAnim.Tick * 1.5f).y > 1f &&
              LaunchBanner.PopScale(1f) == Vector2.one);

        var canvasGo = new GameObject("LB_Canvas", typeof(Canvas));
        var goText = new GameObject("goText", typeof(RectTransform), typeof(Text));
        goText.transform.SetParent(canvasGo.transform, false);
        var text = goText.GetComponent<Text>();
        text.text = movePlayer.ReadyLabel;
        text.fontSize = 30;
        goText.AddComponent<LaunchBanner>().Init();
        Check("styled text uses the Orbitron face", text.font != null && text.font.name.StartsWith("Orbitron"));
        Check("it carries the gradient, contour and shadow",
              goText.GetComponent<PausedLabelGradient>() != null &&
              goText.GetComponent<Outline>() != null && goText.GetComponent<Shadow>() != null);
        Check("it takes no touches", !text.raycastTarget);
        Object.DestroyImmediate(canvasGo);   // the halo is a sibling, destroyed with the canvas

        foreach (var (scene, allowed) in BuiltinAllowed)
        {
            string path = Path.Combine(Application.dataPath, "Scenes", scene + ".unity");
            int n = Regex.Matches(File.ReadAllText(path), @"m_Font: \{fileID: 10102, guid: 0000000000000000e000000000000000").Count;
            Check(scene + " has only the " + allowed + " known built-in-font template Text(s) (" + n + ")", n == allowed);
        }
        foreach (var scene in new[] { "startS4", "spashS7", "shopS6", "leaderboardS3", "creditsS7" })
        {
            string path = Path.Combine(Application.dataPath, "Scenes", scene + ".unity");
            if (!File.Exists(path)) continue;
            Check(scene + " has no built-in-font Text",
                  !Regex.IsMatch(File.ReadAllText(path), @"m_Font: \{fileID: 10102, guid: 0000000000000000e000000000000000"));
        }
        return fails;
    }
}
