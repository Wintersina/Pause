using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

// The Options screen (leaderboardS3) carries no account controls: sign-in lives
// on the leaderboard screen.
public static class OptionsScreenTest
{
    static int fails;
    static void Check(string what, bool ok)
    {
        Debug.Log((ok ? "[OPTS] PASS  " : "[OPTS] FAIL  ") + what);
        if (!ok) fails++;
    }

    public static void Run() { TestHarness.Exit(Execute()); }

    public static int Execute()
    {
        fails = 0;
        using var sandbox = new TestHarness.Sandbox();
        NoAccountControls();
        Debug.Log("[OPTS] failures: " + fails);
        return fails;
    }

    static void NoAccountControls()
    {
        PlayerPrefs.SetInt(DeveloperUnlocks.EnabledKey, 0);
        EditorSceneManager.OpenScene("Assets/Scenes/leaderboardS3.unity", OpenSceneMode.Single);
        var offenders = new System.Collections.Generic.List<string>();
        foreach (var b in Object.FindObjectsByType<Button>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            var t = b.GetComponentInChildren<Text>(true);
            string caption = t != null ? t.text.ToLowerInvariant() : "";
            string n = b.name.ToLowerInvariant();
            if (n.Contains("account") || n.Contains("signin") || n.Contains("signout") ||
                caption.Contains("sign in") || caption.Contains("sign out") || caption.Contains("play games") ||
                caption.Contains("game center"))
                offenders.Add(b.name);
        }
        Check("options: no account / sign-in / sign-out buttons (" + string.Join(",", offenders) + ")", offenders.Count == 0);
        Check("options: no AccountRow object", SceneUtil.FindAny("AccountRow") == null);
    }
}
