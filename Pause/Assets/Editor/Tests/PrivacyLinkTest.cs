using System;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

// The in-game PRIVACY POLICY link: the one URL constant, the Options row
// (clicked through its real onClick) and the double-tap guard.
public static class PrivacyLinkTest
{
    static int fails;
    static void Check(string what, bool ok)
    {
        Debug.Log((ok ? "[PRIV] PASS  " : "[PRIV] FAIL  ") + what);
        if (!ok) fails++;
    }

    public static void Run() { TestHarness.Exit(Execute()); }

    public static int Execute()
    {
        fails = 0;
        using var sandbox = new TestHarness.Sandbox();
        var opener = ExternalLinks.Opener;
        try { Body(); }
        finally { ExternalLinks.Opener = opener; ExternalLinks.ResetGuard(); }
        Debug.Log("[PRIV] failures: " + fails);
        return fails;
    }

    static void Body()
    {
        var uri = new Uri(ExternalLinks.PrivacyPolicy);
        Check("policy URL is https", uri.Scheme == "https");
        Check("policy host is hapticgategames.com", uri.Host == "hapticgategames.com");
        Check("policy path is /privacy/", uri.AbsolutePath == "/privacy/");

        int opened = 0; string last = null;
        ExternalLinks.Opener = u => { opened++; last = u; };
        ExternalLinks.ResetGuard();
        Check("first open goes through", ExternalLinks.Open(ExternalLinks.PrivacyPolicy) && opened == 1);
        Check("immediate second open is swallowed", !ExternalLinks.Open(ExternalLinks.PrivacyPolicy) && opened == 1);
        Check("the opened URL is the policy", last == ExternalLinks.PrivacyPolicy);

        foreach (bool dev in new[] { false, true })
        {
            EditorSceneManager.OpenScene("Assets/Scenes/leaderboardS3.unity", OpenSceneMode.Single);
            DeveloperUnlocks.SetEnabled(dev);
            var o = new GameObject("~PrivacyOptions").AddComponent<PrivacyOptions>();
            o.SendMessage("Start");
            var row = SceneUtil.FindAny(PrivacyOptions.RowName);
            string tag = dev ? " (developer)" : "";
            Check("PRIVACY POLICY row exists" + tag, row != null);
            if (row == null) continue;
            var text = row.GetComponentInChildren<Text>(true);
            Check("row reads PRIVACY POLICY" + tag, text != null && text.text == "PRIVACY POLICY");
            var sound = SceneUtil.FindAny(SoundOptions.RowName);
            Check("row sits above the SOUND slot" + tag,
                  row.GetComponent<RectTransform>().anchoredPosition.y > SoundOptions.EffectsY);
            ExternalLinks.ResetGuard(); opened = 0;
            row.GetComponent<Button>().onClick.Invoke();
            row.GetComponent<Button>().onClick.Invoke();
            Check("tapping the row opens the policy once" + tag, opened == 1 && last == ExternalLinks.PrivacyPolicy);
        }
    }
}
