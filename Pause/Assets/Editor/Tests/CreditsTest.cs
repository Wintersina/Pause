using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

// The credits are the HapticGate lockup, then Sina Serati, plus a Special thanks list (Josh Morris,
// Pouya Vafaee, Preston Nalls, Cody Everett). Guards against the old role lines and the Kenney
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
        foreach (string banned in new[] { "Phaser", "Mastermind", "Master Mind", "Creative director", "Beta",
                                          "Developed By", "Audio Engineer", "Kenney", "CC0", "Particles and sprites" })
            Check("credits do not mention '" + banned + "'",
                  credits.IndexOf(banned, System.StringComparison.OrdinalIgnoreCase) < 0);

        Check("the roll opens with the name under the lockup (no 'Made by' line: the lockup says HapticGate)",
              credits.StartsWith("Sina Serati") && credits.IndexOf("Made by") < 0);
        Check("HapticGate lockup art is installed (head of the roll)", HapticGateMark.Lockup() != null);
        Check("Sina Serati is credited once",
              credits.IndexOf("Sina Serati") >= 0 &&
              credits.IndexOf("Sina Serati") == credits.LastIndexOf("Sina Serati"));
        Check("Special thanks section lists everyone",
              credits.Contains("Special thanks") && credits.IndexOf("Special thanks") > credits.IndexOf("Sina Serati") &&
              credits.Contains("Josh Morris") && credits.Contains("Pouya Vafaee") && credits.Contains("Preston Nalls") &&
              credits.Contains("Cody Everett") && credits.IndexOf("Cody Everett") > credits.IndexOf("Special thanks"));
        Check("Cody Everett is credited once", credits.IndexOf("Cody Everett") == credits.LastIndexOf("Cody Everett"));
        Check("no iOS/Android duplicate lines", !credits.Contains("iOS Developer") && !credits.Contains("Android Developer"));
        Check("closing line is spelled right", credits.Contains("Thank you for playing.") && !credits.Contains("playting"));
        Check("no runs of 5+ blank lines", !credits.Contains("\n\n\n\n\n\n"));

        GateAnimation(mover, text);

        // The text box truncates overflow, so the whole roll must fit in it.
        var rt = text.rectTransform;
        var settings = text.GetGenerationSettings(rt.rect.size);
        float needed = text.cachedTextGeneratorForLayout.GetPreferredHeight(credits, settings) / text.pixelsPerUnit;
        Check("credits fit their text box (" + needed + " <= " + rt.rect.height + ")", needed <= rt.rect.height);

        return failures;
    }

    // The HapticGate logo at the head of the roll plays the gate-opening animation
    // (GateOpenPlayer, shared with the splash) once, then holds the last frame; the
    // roll is on screen at once and starts rising within half a second.
    static void GateAnimation(CreditsMove mover, Text text)
    {
        var frames = GateOpenFrames.Load();
        Check("gate-opening frames are installed (" + (frames == null ? 0 : frames.Length) + ")",
              frames != null && frames.Length == GateOpenFrames.Count);
        if (frames == null) return;
        bool square = true, small = true;
        for (int i = 0; i < frames.Length; i++)
        {
            if (frames[i].texture.width != 512 || frames[i].texture.height != 512) square = false;
            string path = AssetDatabase.GetAssetPath(frames[i]);
            if (new System.IO.FileInfo(path).Length >= 1024 * 1024) small = false;
        }
        Check("gate frames are 512x512", square);
        Check("every gate frame file is under 1 MiB", small);
        Check("frame clock starts at 0 and holds the last frame",
              GateOpenFrames.FrameAt(0f) == 0 && GateOpenFrames.FrameAt(0.99f / GateOpenFrames.Fps) == 0 &&
              GateOpenFrames.FrameAt(1f / GateOpenFrames.Fps) == 1 &&
              GateOpenFrames.FrameAt(GateOpenFrames.Duration + 10f) == GateOpenFrames.Count - 1);

        Check("no lead-in before the roll moves (" + CreditsMove.LeadIn + " s)", CreditsMove.LeadIn <= 0.05f);
        Check("roll is moving within 0.5 s", CreditsMove.ScrollDistance(0f) == 0f &&
              CreditsMove.ScrollDistance(0.3f) > 0f && CreditsMove.ScrollDistance(0.5f) > 3f);
        Check("scroll distance never goes backwards",
              CreditsMove.ScrollDistance(0.39f) <= CreditsMove.ScrollDistance(0.41f) &&
              CreditsMove.ScrollDistance(2f) < CreditsMove.ScrollDistance(3f));

        mover.EnsureLockup();
        var lockup = mover.transform.Find(CreditsMove.LockupName) as RectTransform;
        var player = lockup != null ? lockup.GetComponent<GateOpenPlayer>() : null;
        Check("credits logo has the gate-opening player", player != null && mover.Gate == player);
        if (player == null) return;
        Check("logo shows frame 0 when the screen opens", player.Frame == 0 && lockup.GetComponent<Image>().sprite == frames[0]);

        // On a 9:16 phone (SafeAreaClamp's 800x1422 reference) the logo and the first line are on screen at t = 0.
        var trt = text.rectTransform;
        float half = SafeAreaClamp.PortraitReference.y * 0.5f;
        float textTop = trt.anchoredPosition.y + trt.rect.height * (1f - trt.pivot.y);
        float logoTop = textTop + lockup.anchoredPosition.y + lockup.rect.height;
        Check("first credit line visible at once (top " + textTop + " within +/-" + half + ")", textTop < half && textTop > -half + 100f);
        Check("logo fully on screen at once (top " + logoTop + " < " + half + ")", logoTop < half);

        Vector2 at0 = trt.anchoredPosition;
        for (int i = 0; i < 18; i++) mover.Step(1f / 60f);   // 0.3 s
        Check("roll has started rising at 0.3 s", trt.anchoredPosition.y > at0.y);
        Check("gate animation has advanced at 0.3 s (frame " + player.Frame + ")", player.Frame >= 3 && !player.Finished);
        for (int i = 0; i < 282; i++) mover.Step(1f / 60f);  // 5 s
        Check("gate animation finished and holds the last frame by 5 s",
              player.Finished && player.Frame == GateOpenFrames.Count - 1 && lockup.GetComponent<Image>().sprite == frames[frames.Length - 1]);
        Check("logo still on screen at 5 s while it plays out", textTop + (trt.anchoredPosition.y - at0.y) + lockup.anchoredPosition.y + lockup.rect.height * 0.5f < half);
        trt.anchoredPosition = at0;
    }
}
