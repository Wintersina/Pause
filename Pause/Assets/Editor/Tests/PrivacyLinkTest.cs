using System;
using System.Linq;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

// The in-game PRIVACY POLICY link: the one URL constant, the home screen's
// footer button (clicked through its real onClick) and the double-tap guard.
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

        // The link moved from Options to the home screen's footer (2026-10-12).
        EditorSceneManager.OpenScene("Assets/Scenes/leaderboardS3.unity", OpenSceneMode.Single);
        Check("Options has no PRIVACY POLICY row any more", SceneUtil.FindAny("PrivacyPolicy") == null);
        foreach (bool quit in new[] { true, false })
        {
            string tag = quit ? " (Android: with Quit)" : " (iOS: no Quit)";
            EditorSceneManager.OpenScene("Assets/Scenes/startS4.unity", OpenSceneMode.Single);
            var menu = UnityEngine.Object.FindFirstObjectByType<startMenu>();
            menu.quitB.gameObject.SetActive(quit);
            menu.LayoutHome();
            menu.LayoutHome();
            var row = SceneUtil.FindAny(startMenu.PrivacyButtonName);
            Check("home has a privacy policy button" + tag, row != null);
            if (row == null) continue;
            Check("only one" + tag, UnityEngine.Object.FindObjectsByType<Button>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                  .Count(b => b.name == startMenu.PrivacyButtonName) == 1);
            var text = row.GetComponentInChildren<Text>(true);
            Check("it reads Privacy Policy" + tag, text != null && text.text == startMenu.PrivacyCaption);
            Check("it is shown" + tag, row.activeInHierarchy);
            var r = (RectTransform)row.transform;
            var q = (RectTransform)menu.quitB.transform;
            if (quit && startMenu.FooterStacked)
                Check("it sits above Quit, clear of it" + tag,
                      r.anchoredPosition.y - r.sizeDelta.y * .5f >= q.anchoredPosition.y + q.sizeDelta.y * .5f - .01f);
            else if (quit)
                Check("short canvas: it sits beside Quit, clear of it" + tag,
                      Mathf.Abs(r.anchoredPosition.y - q.anchoredPosition.y) < .01f
                      && r.anchoredPosition.x + r.sizeDelta.x * .5f + 16f <= q.anchoredPosition.x - q.sizeDelta.x * .5f + .01f);
            else
                Check("it takes Quit's place at the bottom" + tag, Mathf.Abs(r.anchoredPosition.y - q.anchoredPosition.y) < .01f);
            var panel = (RectTransform)GameObject.Find("UIPanel").transform;
            Check("it sits below the menu column" + tag,
                  r.parent == panel.parent && r.anchorMin.y == 0f);
            Check("it is in the main menu canvas" + tag, row.GetComponentInParent<Canvas>().name == "MainMenuCanvas");
            ExternalLinks.ResetGuard(); opened = 0;
            row.GetComponent<Button>().onClick.Invoke();
            row.GetComponent<Button>().onClick.Invoke();
            Check("tapping it opens the policy once" + tag, opened == 1 && last == ExternalLinks.PrivacyPolicy);
        }
    }
}
