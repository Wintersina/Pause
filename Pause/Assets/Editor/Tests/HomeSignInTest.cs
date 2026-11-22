using System;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

// The home screen's SIGN IN button (HomeSignIn): hidden while the launch sign-in runs, shown once it failed or was
// cancelled and the player is still signed out, hidden the moment they sign in; a tap runs the interactive sign-in;
// iOS toasts "Sign in to Game Center in Settings" when Game Center refuses. Layout (footer row, no overlap, safe area,
// finger-sized) is the "home-signin" screen of ResponsiveLayoutTest.
//
//   Unity -batchmode -quit -projectPath Pause -executeMethod HomeSignInTest.Run
public static class HomeSignInTest
{
    static int failures;

    static void Check(string label, bool ok)
    {
        if (!ok) failures++;
        Debug.Log("[HSI] " + (ok ? "PASS " : "FAIL ") + label);
    }

    public static void Run() { TestHarness.Exit(Execute()); }

    // A scripted store account: the silent (launch) answer can be held back, interactive answers wait for Complete().
    sealed class FakeAccount : IPlayerAccount, IAccountProfile
    {
        public bool signedIn, deferSilent, silentOk;
        public string platform = "Google Play Games";
        public int silentSignIns, interactiveSignIns;
        Action<bool> pendingSilent, pendingInteractive;

        public string PlatformName { get { return platform; } }
        public bool IsSignedIn { get { return signedIn; } }
        public string PlayerId { get { return signedIn ? "p1" : null; } }
        public string DisplayName { get { return signedIn ? "PILOT" : null; } }
        public SignInReport LastSignIn { get; set; }

        public void SignIn(bool interactive, Action<bool> done)
        {
            if (interactive) { interactiveSignIns++; pendingInteractive = done; return; }
            silentSignIns++;
            if (deferSilent) { pendingSilent = done; return; }
            signedIn = silentOk;
            done(silentOk);
        }

        public void CompleteSilent(bool ok)
        {
            var done = pendingSilent; pendingSilent = null;
            signedIn = ok;
            if (done != null) done(ok);
        }

        public void CompleteInteractive(bool ok, SignInOutcome outcome)
        {
            var done = pendingInteractive; pendingInteractive = null;
            signedIn = ok;
            LastSignIn = SignInReport.Of(ok ? SignInOutcome.Success : outcome, true, ok ? "SUCCESS" : outcome.ToString(), "");
            if (done != null) done(ok);
        }

        public void LoadCloudSave(Action<bool, string> done) { done(true, null); }
        public void WriteCloudSave(string json, Action<bool> done) { done(true); }
    }

    // The home-screen layout rig: the launch sign-in failed, the player is still signed out.
    public static void StageFailedLaunch(bool ios)
    {
        var fake = new FakeAccount { platform = ios ? "Game Center" : "Google Play Games" };
        AccountLink.AccountPlatformOverride = ios ? "ios" : "android";
        AccountLink.Account = fake;
        AccountLink.OnSignInResult(false, false, fake);
    }

    static startMenu menu;

    // A fresh launch on the home screen with `fake` as the store account.
    static CloudSync Launch(FakeAccount fake, string platform)
    {
        AccountLink.ResetSession();
        PlayerPrefs.DeleteKey(AccountLink.DisconnectedKey);
        PlayerPrefs.DeleteKey(AccountLink.HintShownKey);
        PlayerPrefs.SetString("HasDoneTut", "true");
        if (AccountHintToast.Current != null) UnityEngine.Object.DestroyImmediate(AccountHintToast.Current.gameObject);
        AccountLink.AccountPlatformOverride = platform;
        PlayerAccounts.Current = fake;
        EditorSceneManager.OpenScene("Assets/Scenes/startS4.unity", OpenSceneMode.Single);
        menu = UnityEngine.Object.FindFirstObjectByType<startMenu>();
        var sync = new CloudSync(fake, () => 1000);
        CloudSync.SetInstance(sync);
        sync.Start();
        return sync;
    }

    static Button TheButton()
    {
        var go = HomeSignIn.Find();
        return go != null ? go.GetComponent<Button>() : null;
    }

    public static int Execute()
    {
        failures = 0;
        using var sandbox = new TestHarness.Sandbox();
        var savedAccount = PlayerAccounts.Current;
        var savedSync = CloudSync.Instance;
        try
        {
            Flow(false);
            Flow(true);
            NotShown();
        }
        finally
        {
            if (menu != null) AccountLink.Changed -= menu.OnAccountChanged;
            if (AccountHintToast.Current != null) UnityEngine.Object.DestroyImmediate(AccountHintToast.Current.gameObject);
            PlayerAccounts.Current = savedAccount;
            CloudSync.SetInstance(savedSync);
            AccountLink.ResetSession();
        }
        Debug.Log("[HSI] failures: " + failures);
        return failures;
    }

    // The launch sign-in in progress -> fails -> tap (cancelled) -> tap (succeeds).
    static void Flow(bool ios)
    {
        string p = ios ? "ios: " : "android: ";
        var fake = new FakeAccount { deferSilent = true, platform = ios ? "Game Center" : "Google Play Games" };
        Launch(fake, ios ? "ios" : "android");
        AccountLink.Changed += menu.OnAccountChanged;   // startMenu.OnEnable does this at runtime
        menu.OnAccountChanged();
        Check(p + "hidden while the launch sign-in is running", !HomeSignIn.Visible && !AccountLink.ShouldShowHomeSignIn);

        fake.CompleteSilent(false);
        Check(p + "shown after the launch sign-in failed", HomeSignIn.Visible && AccountLink.ShouldShowHomeSignIn);
        var button = TheButton();
        var label = button != null ? button.GetComponentInChildren<Text>(true) : null;
        Check(p + "label is SIGN IN", label != null && label.text == "SIGN IN");
        var mark = button != null ? button.transform.Find("Mark") : null;
        var image = mark != null ? mark.GetComponent<Image>() : null;
        Check(p + "platform mark is the " + (ios ? "apple" : "robot"),
              image != null && image.sprite == PlatformMark.For(ios ? SharedBoardLimits.Ios : SharedBoardLimits.Android));

        // tap, then the player closes the sheet: the button stays
        button.onClick.Invoke();
        Check(p + "tap runs the interactive sign-in", fake.interactiveSignIns == 1);
        fake.CompleteInteractive(false, SignInOutcome.Canceled);
        Check(p + "still shown after a cancelled sign-in", HomeSignIn.Visible);
        if (ios) Check(p + "Game Center refused: toast says to sign in from Settings", HomeSignIn.LastToast == HomeSignIn.GameCenterSettingsToast);
        else Check(p + "closing Play Games' own prompt is silent", string.IsNullOrEmpty(HomeSignIn.LastToast));

        // tap again, success: hidden at once, no per-frame polling involved
        button.onClick.Invoke();
        Check(p + "second tap starts a fresh attempt", fake.interactiveSignIns == 2);
        fake.CompleteInteractive(true, SignInOutcome.Success);
        Check(p + "hidden once signed in", !HomeSignIn.Visible && !AccountLink.ShouldShowHomeSignIn);
        AccountLink.Changed -= menu.OnAccountChanged;
    }

    static void NotShown()
    {
        var ok = new FakeAccount { silentOk = true };
        Launch(ok, "android");
        HomeSignIn.Refresh();
        Check("launch sign-in succeeded: never shown", !HomeSignIn.Visible);

        var failed = new FakeAccount();
        Launch(failed, "android");
        PlayerPrefs.SetInt(AccountLink.DisconnectedKey, 1);
        Check("signed out in Pause on purpose: not shown", !AccountLink.ShouldShowHomeSignIn);
        PlayerPrefs.DeleteKey(AccountLink.DisconnectedKey);

        AccountLink.ResetSession();
        AccountLink.Account = new NullAccount();
        AccountLink.OnSignInResult(false, false, AccountLink.Account);
        Check("no store account on this platform (editor / desktop): not shown", !AccountLink.ShouldShowHomeSignIn);

        Check("failure toasts: a network error is named",
              HomeSignIn.FailureToast(SignInReport.Of(SignInOutcome.NetworkError, true, "NET", "")) != null);
    }
}
