using System.Reflection;
using UnityEditor.SceneManagement;
using UnityEngine;

// Tutorial runs never pay real star dust and never change the saved total,
// whether it is the first-time tutorial or a replay from the leaderboard
// screen. The replay used to skip setting youAreInTutorial, so it ran as a
// real run (real dust, 5 pauses), and AwardStarDust looked only at
// HasDoneTut -- which Hints sets part-way through the tutorial.
public static class TutorialCurrencyTest
{
    const string Key = "PlayerCurrecny";
    static int fails;
    static void Check(string what, bool ok)
    {
        Debug.Log((ok ? "[TC] PASS  " : "[TC] FAIL  ") + what);
        if (!ok) fails++;
    }

    public static void Run()
    {
        TestHarness.Exit(Execute());
    }

    public static int Execute()
    {
        fails = 0;
        using var sandbox = new TestHarness.Sandbox();

        TheRule();
        LeaderboardReplayEntersLikeFirstTime();
        TutorialRun("first-time tutorial", hasDoneTut: false);
        TutorialRun("tutorial replayed from the leaderboard", hasDoneTut: true);

        Debug.Log("[TC] failures: " + fails);
        return fails;
    }

    static void TheRule()
    {
        Check("a normal game run pays real dust", score.PaysRealDust("gameS1", true, false));
        Check("the tutorial scene never does, even after HasDoneTut",
              !score.PaysRealDust("tutorialS5", true, false));
        Check("nor while flagged as the tutorial", !score.PaysRealDust("gameS1", true, true));
        Check("nor before the tutorial is done", !score.PaysRealDust("gameS1", false, false));
    }

    static void LeaderboardReplayEntersLikeFirstTime()
    {
        startMenu.youAreInTutorial = false;
        score.totalCurrency = 42f;
        var go = new GameObject("~Leaderboard");
        var board = go.AddComponent<leaderboard>();
        try { board.playTut(); }
        catch (System.Exception) { /* LoadScene refuses edit mode; state is set before it */ }
        Check("replaying from the leaderboard flags the tutorial", startMenu.youAreInTutorial);
        Check("and clears the run total like the first-time entry", score.totalCurrency == 0f);
        Object.DestroyImmediate(go);

        startMenu.youAreInTutorial = true;
        GameStateReset.Clear();
        Check("leaving for a menu scene clears the tutorial flag", !startMenu.youAreInTutorial);
    }

    static void Call(score hud, string method)
    {
        typeof(score).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
            .Invoke(hud, null);
    }

    static void TutorialRun(string label, bool hasDoneTut)
    {
        EditorSceneManager.OpenScene("Assets/Scenes/tutorialS5.unity", OpenSceneMode.Single);
        var hud = Object.FindFirstObjectByType<score>();
        Check(label + ": tutorialS5 has a score HUD", hud != null);
        if (hud == null) return;

        PlayerPrefs.SetString("HasDoneTut", hasDoneTut ? "true" : "false");
        PlayerPrefs.SetFloat(Key, 25f);
        buttonClicks.playerDied = false;
        startMenu.PrepareTutorialRun();

        Call(hud, "Awake");
        Call(hud, "Start");
        Check(label + ": does not pay real dust", !score.paysRealDust);
        Check(label + ": gets the tutorial's 50 pauses", score.pauseCounter == 50);
        Check(label + ": practice dust starts at 0", score.tutorialCurrency == 0f);
        Check(label + ": no star dust read-out in the tutorial HUD",
              typeof(score).GetField("currencyText") == null && SceneUtil.FindAny("CurrecnyGatheredText") == null);
        Check(label + ": speed readout is spelled \"Current\"", hud.speedValue.text == "Current Speed : 0");

        score.AwardStarDust(3f);
        // Hints marks the tutorial done part-way through.
        PlayerPrefs.SetString("HasDoneTut", "true");
        score.AwardStarDust(4f);
        Check(label + ": pickups go to practice dust", Mathf.Approximately(score.tutorialCurrency, 7f));
        Check(label + ": the real total stays at 0", score.totalCurrency == 0f);

        buttonClicks.playerDied = true;
        Call(hud, "Update");
        Call(hud, "OnDestroy");
        buttonClicks.playerDied = false;
        Check(label + ": saved dust is unchanged", Mathf.Approximately(PlayerPrefs.GetFloat(Key), 25f));
    }
}
