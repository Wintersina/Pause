using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

// The credits are "Made by Sina Serati" plus a Special thanks list (Josh Morris,
// Pouya Vafaee, Preston). Guards against the old role lines and the Kenney
// attribution (CC0, not legally required; licence files stay in docs/licenses/)
// coming back, and checks the scroll still fits its text box (no truncation).
public static class CreditsTest
{
    static int failures;

    static void Check(string label, bool condition)
    {
        if (!condition) failures++;
        Debug.Log("[CR] " + (condition ? "PASS " : "FAIL ") + label);
    }

    public static void Run()
    {
        TestHarness.Exit(Execute());
    }

    public static int Execute()
    {
        failures = 0;
        using var sandbox = new TestHarness.Sandbox();

        EditorSceneManager.OpenScene("Assets/Scenes/creditsS7.unity", OpenSceneMode.Single);
        var mover = Object.FindFirstObjectByType<CreditsMove>();
        var text = mover != null ? mover.GetComponent<Text>() : null;
        Check("scrolling credits text exists", text != null);
        if (text == null) return failures;

        string credits = text.text;
        foreach (string banned in new[] { "Phaser", "Mastermind", "Master Mind", "Creative director", "Beta", "Cody",
                                          "Developed By", "Audio Engineer", "Kenney", "CC0", "Particles and sprites" })
            Check("credits do not mention '" + banned + "'",
                  credits.IndexOf(banned, System.StringComparison.OrdinalIgnoreCase) < 0);

        Check("credits say 'Made by' once",
              credits.IndexOf("Made by") >= 0 && credits.IndexOf("Made by") == credits.LastIndexOf("Made by"));
        Check("Sina Serati is credited once",
              credits.IndexOf("Sina Serati") >= 0 &&
              credits.IndexOf("Sina Serati") == credits.LastIndexOf("Sina Serati"));
        Check("Special thanks section lists the three originals",
              credits.Contains("Special thanks") && credits.IndexOf("Special thanks") > credits.IndexOf("Sina Serati") &&
              credits.Contains("Josh Morris") && credits.Contains("Pouya Vafaee") && credits.Contains("Preston"));
        Check("no iOS/Android duplicate lines", !credits.Contains("iOS Developer") && !credits.Contains("Android Developer"));
        Check("closing line is spelled right", credits.Contains("Thank you for playing.") && !credits.Contains("playting"));
        Check("no runs of 5+ blank lines", !credits.Contains("\n\n\n\n\n\n"));

        // The text box truncates overflow, so the whole roll must fit in it.
        var rt = text.rectTransform;
        var settings = text.GetGenerationSettings(rt.rect.size);
        float needed = text.cachedTextGeneratorForLayout.GetPreferredHeight(credits, settings) / text.pixelsPerUnit;
        Check("credits fit their text box (" + needed + " <= " + rt.rect.height + ")", needed <= rt.rect.height);

        return failures;
    }
}
