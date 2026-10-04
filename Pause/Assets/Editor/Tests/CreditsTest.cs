using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

// The credits were trimmed to the developer and the audio engineer (plus the
// CC0 Kenney thanks for art still in the game). Guards against the old
// Mastermind / Creative director / beta tester / Phaser.io lines coming back,
// and checks the shorter scroll still fits its text box (no truncation).
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
        foreach (string banned in new[] { "Phaser", "Mastermind", "Master Mind", "Creative director", "Beta", "Cody" })
            Check("credits do not mention '" + banned + "'",
                  credits.IndexOf(banned, System.StringComparison.OrdinalIgnoreCase) < 0);

        Check("credits say 'Developed By' once",
              credits.IndexOf("Developed By") >= 0 &&
              credits.IndexOf("Developed By") == credits.LastIndexOf("Developed By"));
        Check("Sina Serati is credited once",
              credits.IndexOf("Sina Serati") >= 0 &&
              credits.IndexOf("Sina Serati") == credits.LastIndexOf("Sina Serati"));
        Check("audio engineer is kept", credits.Contains("Audio Engineer") && credits.Contains("Josh Morris"));
        Check("Kenney CC0 thanks kept", credits.Contains("Kenney"));
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
