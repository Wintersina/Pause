using UnityEngine;
using UnityEngine.UI;

// The home screen's SIGN IN button: a compact copy of the Quit button (same art, label font and press) with the
// platform's pixel mark (apple on iOS / Game Center, robot on Android / Play Games) and "SIGN IN". It sits in the
// footer row beside PRIVACY POLICY (startMenu.LayoutHome), so it can never overlap the menu column.
//
// Shown ONLY while AccountLink.ShouldShowHomeSignIn: the launch sign-in has finished and failed (or was
// cancelled) and the player is still not signed in. While the launch sign-in runs, after any success, after SIGN
// OUT, and on platforms without a store account (editor, desktop) it does not exist at all. startMenu follows
// AccountLink.Changed (an event, never a per-frame poll) and calls Refresh. No popup, no nagging.
//
// Tap = AccountLink.SignIn, the same interactive sign-in the leaderboard panel uses (Play Games:
// ManuallyAuthenticate; Game Center: its authenticate sheet). iOS only shows that sheet when it is allowed to; after
// the player cancelled it a few times it refuses (Disabled / Canceled / failed), and the tap then toasts
// "Sign in to Game Center in Settings" instead of doing nothing.
public static class HomeSignIn
{
    public const string ButtonName = "HomeSignInButton";
    public const string Caption = "SIGN IN";
    public const string GameCenterSettingsToast = "Sign in to Game Center in Settings";
    public const float Width = 150f, MarkSize = 24f, MarkInset = 14f;

    // Tests: the toast text of the last failed tap.
    public static string LastToast { get; private set; }

    public static GameObject Find() { return SceneUtil.FindAny(ButtonName); }
    public static bool Visible { get { var go = Find(); return go != null && go.activeSelf; } }

    // Shows or hides the button to match the account state. Returns true when its visibility changed.
    public static bool Refresh()
    {
        bool want = AccountLink.ShouldShowHomeSignIn;
        var go = Find();
        if (want && go == null) go = Build();
        if (go == null || go.activeSelf == want) return false;
        go.SetActive(want);
        if (want) ApplyMark(go);
        return true;
    }

    static GameObject Build()
    {
        var template = SceneUtil.FindAny("QuitButton");
        if (template == null) return null;
        var go = Object.Instantiate(template, template.transform.parent, false);
        go.name = ButtonName;
        go.SetActive(false);
        var button = go.GetComponent<Button>();
        if (button != null)
        {
            button.onClick = new Button.ButtonClickedEvent();
            button.onClick.AddListener(OnTap);
        }
        foreach (var label in go.GetComponentsInChildren<Text>(true))
        {
            label.text = Caption;
            var r = label.rectTransform;
            r.offsetMin = new Vector2(MarkInset + MarkSize + 6f, r.offsetMin.y);
            r.offsetMax = new Vector2(-8f, r.offsetMax.y);
        }
        var mark = new GameObject("Mark", typeof(RectTransform), typeof(Image));
        mark.transform.SetParent(go.transform, false);
        var rect = (RectTransform)mark.transform;
        rect.anchorMin = rect.anchorMax = new Vector2(0f, .5f);
        rect.pivot = new Vector2(0f, .5f);
        rect.anchoredPosition = new Vector2(MarkInset, 0f);
        rect.sizeDelta = new Vector2(MarkSize, MarkSize);
        var image = mark.GetComponent<Image>();
        image.preserveAspect = true;
        image.raycastTarget = false;
        return go;
    }

    // The mark follows the platform's store (Game Center -> apple, Play Games -> robot).
    static void ApplyMark(GameObject go)
    {
        var mark = go.transform.Find("Mark");
        var image = mark != null ? mark.GetComponent<Image>() : null;
        if (image == null) return;
        image.sprite = PlatformMark.For(AccountLink.IsGameCenter ? SharedBoardLimits.Ios : SharedBoardLimits.Android);
        image.enabled = image.sprite != null;
    }

    public static void OnTap()
    {
        AccountLink.SignIn(ok =>
        {
            if (!ok) ShowFailure(AccountLink.LastReport);
        });
    }

    // What a failed tap tells the player, or null for nothing (Play Games' own prompt was just closed).
    public static string FailureToast(SignInReport report)
    {
        if (report.outcome == SignInOutcome.NetworkError) return AccountLink.FailureReason(report);
        if (AccountLink.IsGameCenter) return GameCenterSettingsToast;   // sheet refused or dismissed: Settings is the way
        return report.outcome == SignInOutcome.Canceled ? null : AccountLink.FailureReason(report);
    }

    static void ShowFailure(SignInReport report)
    {
        string text = FailureToast(report);
        LastToast = text;
        if (!string.IsNullOrEmpty(text)) AccountHintToast.Show(text);
    }
}
