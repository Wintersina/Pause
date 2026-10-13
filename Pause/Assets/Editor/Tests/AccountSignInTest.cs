using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

// Visible sign-in and Pause's SIGN OUT: the Options Account row, the
// interactive sign-in path, failure diagnosis, the disconnect switch and the
// one-time home-screen hint.
//
//   Unity -batchmode -quit -projectPath Pause -executeMethod AccountSignInTest.Run
public static class AccountSignInTest
{
    static int failures;

    static void Check(string label, bool condition)
    {
        if (!condition) failures++;
        Debug.Log("[ASI] " + (condition ? "PASS " : "FAIL ") + label);
    }

    public static void Run()
    {
        TestHarness.Exit(Execute());
    }

    // A scripted store account. The silent answer is immediate; interactive
    // answers wait for Complete() so a test can see "signing in".
    sealed class FakeAccount : IPlayerAccount, IAccountProfile
    {
        public bool silentOk;
        public SignInReport silentReport = SignInReport.Of(SignInOutcome.SignInRequired, false, "SIGN_IN_REQUIRED", "");
        public string id = "player-A", name = "Sina Serati";
        public bool signedIn;
        public string cloudJson;
        public int silentSignIns, interactiveSignIns, loads;
        public readonly List<string> writes = new List<string>();
        Action<bool> pending;

        public string PlatformName { get { return "Google Play Games"; } }
        public bool IsSignedIn { get { return signedIn; } }
        public string PlayerId { get { return signedIn ? id : null; } }
        public string DisplayName { get { return signedIn ? name : null; } }
        public SignInReport LastSignIn { get; set; }

        public void SignIn(bool interactive, Action<bool> done)
        {
            if (!interactive)
            {
                silentSignIns++;
                signedIn = silentOk;
                LastSignIn = silentOk ? SignInReport.Of(SignInOutcome.Success, false, "SUCCESS", "") : silentReport;
                done(silentOk);
                return;
            }
            interactiveSignIns++;
            pending = done;
        }

        public bool Waiting { get { return pending != null; } }

        public void Complete(bool ok, SignInReport report)
        {
            var done = pending;
            pending = null;
            signedIn = ok;
            LastSignIn = report;
            if (done != null) done(ok);
        }

        public void LoadCloudSave(Action<bool, string> done) { loads++; done(true, cloudJson); }

        public void WriteCloudSave(string json, Action<bool> done)
        {
            writes.Add(json);
            cloudJson = json;
            done(true);
        }
    }

    static readonly SignInReport Ok = SignInReport.Of(SignInOutcome.Success, true, "SUCCESS", "");

    public static int Execute()
    {
        failures = 0;
        using var sandbox = new TestHarness.Sandbox();
        var savedAccount = PlayerAccounts.Current;
        var savedSync = CloudSync.Instance;
        var savedOpen = AccountSettingsLink.OpenOverride;
        // The shipped table has no live board until Top Score's Play Console
        // id is in (the Top Speed board these checks used is retired): a
        // table with live boards, as LeaderboardTest uses.
        LeaderboardBoards.OverrideForTests(LeaderboardTest.LiveTable());
        try
        {
            Diagnosis();
            SilentFailureShowsSignIn();
            ConfigErrorHint();
            GameCenterWording();
            SignOutAndBackIn();
            FreshLaunchAfterSignOut();
            LaunchHintOnce();
            SharedInteractivePath();
            HomeHasNoAccountButtons();
        }
        finally
        {
            Cleanup();
            PlayerAccounts.Current = savedAccount;
            CloudSync.SetInstance(savedSync);
            AccountSettingsLink.OpenOverride = savedOpen;
            AccountLink.ResetSession();
            LeaderboardService.Instance = null;
            LeaderboardBoards.OverrideForTests(null);
        }
        Debug.Log("[ASI] failures: " + failures);
        return failures;
    }

    // ---- helpers ----

    static void Cleanup()
    {
        LeaderboardPanel.Close();
        if (AccountHintToast.Current != null) UnityEngine.Object.DestroyImmediate(AccountHintToast.Current.gameObject);
    }

    // A fresh "launch": no session state, prefs as the test sets them.
    static CloudSync Launch(FakeAccount fake)
    {
        Cleanup();
        AccountLink.ResetSession();
        AccountLink.AccountPlatformOverride = "android";
        PlayerAccounts.Current = fake;
        var sync = new CloudSync(fake, () => 1000);
        CloudSync.SetInstance(sync);
        sync.Start();
        return sync;
    }

    static void ClearAccountPrefs()
    {
        PlayerPrefs.DeleteKey(AccountLink.DisconnectedKey);
        PlayerPrefs.DeleteKey(AccountLink.HintShownKey);
        PlayerPrefs.DeleteKey(CloudSync.LastAccountKey);
        PlayerPrefs.DeleteKey(CloudSync.BackupsKey);
        PlayerPrefs.DeleteKey(LeaderboardService.PendingKey);
        PlayerPrefs.DeleteKey(LeaderboardService.SubmittedKey);
        PlayerPrefs.SetInt(DeveloperUnlocks.EnabledKey, 0);
        PlayerPrefs.SetString("HasDoneTut", "true");
        startMenu.youAreInTutorial = false;
    }

    // A leaderboard service with one score waiting to be sent.
    static LeaderboardService QueueScore(out FakeLeaderboards platform)
    {
        platform = new FakeLeaderboards { SignedIn = true };
        var service = new LeaderboardService(platform, () => 0f) { Ios = false };
        LeaderboardService.Instance = service;
        LeaderboardService.Install();
        string board = service.UsableBoards()[0].id;
        var table = new LeaderboardService.ScoreTable();
        table.Set(board, 4242);
        table.Save(LeaderboardService.PendingKey);
        return service;
    }

    static string Progress()
    {
        return ProgressSnapshot.Capture(0).ContentKey() + "|" + PlayerPrefs.GetFloat("PlayerCurrecny");
    }

    // ---- diagnosis ----

    static void Diagnosis()
    {
        var silent = SignInDiagnosis.FromPlayGames("Canceled", false, "Returning an error code.", .3f, false);
        Check("diagnosis: silent 'not authenticated' is SIGN_IN_REQUIRED",
              silent.outcome == SignInOutcome.SignInRequired && silent.code == "SIGN_IN_REQUIRED");

        var dev = SignInDiagnosis.FromPlayGames("InternalError", true,
            "Authentication failed - com.google.android.gms.common.api.ApiException: 10: ", .5f, false);
        Check("diagnosis: ApiException 10 is a configuration error",
              dev.outcome == SignInOutcome.ConfigError && dev.code == "DEVELOPER_ERROR(10)");
        Check("diagnosis: the full status is kept", dev.detail.Contains("ApiException: 10"));

        var noPrompt = SignInDiagnosis.FromPlayGames("Canceled", true, "Returning an error code.", .4f, false);
        Check("diagnosis: interactive refusal without a prompt is a configuration error",
              noPrompt.outcome == SignInOutcome.ConfigError);

        var cancel = SignInDiagnosis.FromPlayGames("Canceled", true, "Returning an error code.", 4f, true);
        Check("diagnosis: closing Google's prompt is a cancel", cancel.outcome == SignInOutcome.Canceled);

        var net = SignInDiagnosis.FromPlayGames("InternalError", true,
            "Authentication failed - com.google.android.gms.common.api.ApiException: 7: ", 2f, true);
        Check("diagnosis: ApiException 7 is a network error", net.outcome == SignInOutcome.NetworkError);

        var gcUnknown = SignInDiagnosis.FromGameCenter(false,
            "The requested operation could not be completed because this application is not recognized by Game Center.",
            true, 1f);
        Check("diagnosis: Game Center 'not recognized' is a configuration error",
              gcUnknown.outcome == SignInOutcome.ConfigError);
        var gcOff = SignInDiagnosis.FromGameCenter(false,
            "The requested operation has been cancelled or disabled by the user.", true, .1f);
        Check("diagnosis: Game Center refusing at once is 'disabled' (Settings)", gcOff.outcome == SignInOutcome.Disabled);
        var gcCancel = SignInDiagnosis.FromGameCenter(false,
            "The requested operation has been cancelled or disabled by the user.", true, 5f);
        Check("diagnosis: dismissing Apple's sheet is a cancel", gcCancel.outcome == SignInOutcome.Canceled);
    }

    // ---- SIGN_IN_REQUIRED -> sign-in is offered; success -> signed in ----

    static void SilentFailureShowsSignIn()
    {
        ClearAccountPrefs();
        var fake = new FakeAccount { silentOk = false };
        var sync = Launch(fake);
        Check("launch: one silent attempt", fake.silentSignIns == 1 && fake.interactiveSignIns == 0);
        Check("launch: SIGN_IN_REQUIRED recorded", AccountLink.LastReport.outcome == SignInOutcome.SignInRequired);
        Check("launch: account is signed out", AccountLink.Current == AccountLink.Status.SignedOut);

        Check("signed out: sign-in is offered", AccountLink.SignInLabel == "SIGN IN WITH GOOGLE PLAY GAMES");

        int signedInEvents = 0;
        Action onSignedIn = () => signedInEvents++;
        SocialBridge.SignedIn += onSignedIn;
        FakeLeaderboards board;
        var service = QueueScore(out board);
        try
        {
            AccountLink.SignIn();
            Check("sign in: the interactive path runs", fake.interactiveSignIns == 1 && fake.Waiting);
            Check("sign in: status is signing in", AccountLink.Current == AccountLink.Status.SigningIn);
            AccountLink.SignIn();
            Check("tap again while signing in: still one interactive attempt", fake.interactiveSignIns == 1);

            fake.Complete(true, Ok);
            Check("success: signed in with the player's name",
                  AccountLink.Current == AccountLink.Status.SignedIn && AccountLink.DisplayName == "Sina Serati");
            Check("success: SignedIn event fired once", signedInEvents == 1);
            Check("success: cloud save read and merged", fake.loads == 1 && sync.Current == CloudSync.State.Ready);
            Check("success: queued leaderboard score flushed", service.SubmitCount == 1 && board.Submissions.Count == 1);
            Check("success: account remembered", PlayerPrefs.GetString(CloudSync.LastAccountKey) == "player-A");
            Check("details line shows the last status code", AccountLink.DetailsLine.Contains("SUCCESS"));
        }
        finally
        {
            SocialBridge.SignedIn -= onSignedIn;
        }
    }

    // ---- config error -> hint ----

    static void ConfigErrorHint()
    {
        ClearAccountPrefs();
        var fake = new FakeAccount { silentOk = false };
        Launch(fake);
        AccountLink.SignIn();
        var report = SignInDiagnosis.FromPlayGames("Canceled", true, "Returning an error code.", .3f, false);
        fake.Complete(false, report);
        Check("config error: status is Failed", AccountLink.Current == AccountLink.Status.Failed);
        Check("config error: one-line set-up hint",
              AccountLink.FailureHint(AccountLink.LastReport) == "Play Games isn't set up for this build yet.");
        Check("config error: sign-in is still offered", AccountLink.SignInLabel == "SIGN IN WITH GOOGLE PLAY GAMES");
        Check("config error: details carry the status code",
              AccountLink.DetailsLine.Contains("NOT_AUTHENTICATED_NO_PROMPT"));

        // A plain cancel: reason, no set-up hint.
        AccountLink.SignIn();
        fake.Complete(false, SignInDiagnosis.FromPlayGames("Canceled", true, "Returning an error code.", 5f, true));
        Check("cancel: reason shown without the set-up hint",
              AccountLink.FailureReason(AccountLink.LastReport) == "Sign-in was cancelled."
              && string.IsNullOrEmpty(AccountLink.FailureHint(AccountLink.LastReport)));
    }

    static void GameCenterWording()
    {
        ClearAccountPrefs();
        var fake = new FakeAccount { silentOk = false };
        Launch(fake);
        AccountLink.AccountPlatformOverride = "ios";
        Check("iOS: signed-out offers SIGN IN TO GAME CENTER", AccountLink.SignInLabel == "SIGN IN TO GAME CENTER");
        AccountLink.SignIn();
        fake.Complete(false, SignInDiagnosis.FromGameCenter(false,
            "The requested operation has been cancelled or disabled by the user.", true, .1f));
        Check("iOS: sheet turned off -> Settings hint",
              (AccountLink.FailureHint(AccountLink.LastReport) ?? "").Contains("Settings > Game Center"));
        Check("iOS: home hint names Game Center", AccountLink.LaunchHintText.Contains("Game Center"));
        AccountLink.AccountPlatformOverride = "android";
    }

    // ---- sign out -> disconnected; sign in again -> reconnected ----

    static void SignOutAndBackIn()
    {
        ClearAccountPrefs();
        PlayerPrefs.SetFloat("PlayerCurrecny", 77f);
        var fake = new FakeAccount { silentOk = true };
        var sync = Launch(fake);
        Check("signed-in launch: Ready", sync.Current == CloudSync.State.Ready && AccountLink.Connected);
        Check("signed-in launch: the account has the name", AccountLink.DisplayName == "Sina Serati");
        sync.Poll(urgent: true, force: true);
        int writesBefore = fake.writes.Count;
        string lastAccount = PlayerPrefs.GetString(CloudSync.LastAccountKey);
        string progress = Progress();

        AccountLink.Disconnect();
        Check("sign out: disconnect flag stored", AccountLink.Disconnected
              && PlayerPrefs.GetInt(AccountLink.DisconnectedKey) == 1);
        Check("sign out: cloud sync stopped", sync.Current == CloudSync.State.Disconnected);
        Check("sign out: local progress unchanged", Progress() == progress);
        Check("sign out: per-account bookkeeping kept", PlayerPrefs.GetString(CloudSync.LastAccountKey) == lastAccount);
        Check("sign out: status is Disconnected", AccountLink.Current == AccountLink.Status.Disconnected);

        PlayerPrefs.SetFloat("PlayerCurrecny", 99f);
        PlayerPrefs.SetString("boughtship8", "True");
        sync.Poll(urgent: true, force: true);
        Check("signed out: no cloud upload", fake.writes.Count == writesBefore);
        Check("signed out: SocialBridge reports nothing", !SocialBridge.IsAuthenticated);
        bool reported = true;
        SocialBridge.ReportProgress("CgkI3eXNjrQcEAIQCQ", 50.0, ok => reported = ok);
        Check("signed out: achievement report dropped", !reported);
        FakeLeaderboards board;
        var service = QueueScore(out board);
        service.Flush();
        Check("signed out: leaderboard score stays queued", board.Submissions.Count == 0);
        LeaderboardStatus loadStatus = LeaderboardStatus.Ok;
        service.LoadBoard(service.UsableBoards()[0].id, 10, (top, me) => loadStatus = top.status);
        Check("signed out: leaderboard panel sees signed out", loadStatus == LeaderboardStatus.NotSignedIn);

        // Sign in again: the platform never signed out, so it is simply used again.
        int signedInEvents = 0;
        Action onSignedIn = () => signedInEvents++;
        SocialBridge.SignedIn += onSignedIn;
        try
        {
            int loads = fake.loads;
            AccountLink.SignIn();
            Check("sign in again: flag cleared", !AccountLink.Disconnected && !PlayerPrefs.HasKey(AccountLink.DisconnectedKey));
            Check("sign in again: reconnected", sync.Current == CloudSync.State.Ready && AccountLink.Connected
                  && AccountLink.Current == AccountLink.Status.SignedIn);
            Check("sign in again: cloud save read again", fake.loads == loads + 1);
            Check("sign in again: SignedIn fired and the queue flushed", signedInEvents >= 1 && board.Submissions.Count == 1);
            sync.Poll(urgent: true, force: true);
            ProgressSnapshot uploaded;
            bool hasShip = fake.writes.Count > writesBefore &&
                ProgressSnapshot.TryParse(fake.writes[fake.writes.Count - 1], out uploaded) == ProgressSnapshot.ParseResult.Ok &&
                Array.IndexOf(uploaded.boughtShips, 8) >= 0;
            Check("sign in again: progress made while signed out is uploaded", hasShip);
        }
        finally
        {
            SocialBridge.SignedIn -= onSignedIn;
        }
        PlayerPrefs.DeleteKey("boughtship8");
    }

    static void FreshLaunchAfterSignOut()
    {
        ClearAccountPrefs();
        PlayerPrefs.SetInt(AccountLink.DisconnectedKey, 1);
        PlayerPrefs.SetFloat("PlayerCurrecny", 55f);
        string progress = Progress();
        var fake = new FakeAccount { silentOk = true };
        var sync = Launch(fake);
        Check("next launch: no silent sign-in while signed out", fake.silentSignIns == 0 && fake.interactiveSignIns == 0);
        Check("next launch: sync stays off", sync.Current == CloudSync.State.Disconnected && fake.loads == 0);
        Check("next launch: local progress unchanged", Progress() == progress);
        Check("next launch: no home hint after a deliberate sign-out", AccountHintToast.TryShow("startS4") == null);

        Check("next launch: status is Disconnected", AccountLink.Current == AccountLink.Status.Disconnected);
        AccountLink.SignIn();
        Check("next launch: SIGN IN is interactive", fake.interactiveSignIns == 1);
        fake.Complete(true, Ok);
        Check("next launch: signing in reconnects", sync.Current == CloudSync.State.Ready && !AccountLink.Disconnected);
    }

    // ---- the home-screen hint ----

    static void LaunchHintOnce()
    {
        ClearAccountPrefs();
        var fake = new FakeAccount { silentOk = false };
        Launch(fake);
        EditorSceneManager.OpenScene("Assets/Scenes/startS4.unity", OpenSceneMode.Single);
        Check("hint: not on other screens", AccountHintToast.TryShow("leaderboardS3") == null);
        var toast = AccountHintToast.TryShow("startS4");
        Check("hint: shown on home after the silent sign-in failed", toast != null && toast.Showing
              && toast.ShownText == "Sign in with Play Games from the leaderboard to save progress to your account");
        Check("hint: never blocks input", toast != null && toast.GetComponent<GraphicRaycaster>() == null
              && toast.GetComponentsInChildren<Button>(true).Length == 0);
        Check("hint: remembered", PlayerPrefs.GetInt(AccountLink.HintShownKey) == 1);
        Check("hint: not again this launch", AccountHintToast.TryShow("startS4") == null);

        Launch(new FakeAccount { silentOk = false });
        Check("hint: not again on the next launch", AccountHintToast.TryShow("startS4") == null);

        PlayerPrefs.DeleteKey(AccountLink.HintShownKey);
        Launch(new FakeAccount { silentOk = true });
        Check("hint: not when the silent sign-in worked", AccountHintToast.TryShow("startS4") == null);
    }

    // ---- every "sign in" goes through AccountLink ----

    static void SharedInteractivePath()
    {
        ClearAccountPrefs();
        var fake = new FakeAccount { silentOk = false };
        Launch(fake);
        bool answer = false;
        SocialBridge.Authenticate(ok => answer = ok);
        Check("SocialBridge.Authenticate uses the interactive path once", fake.interactiveSignIns == 1);
        fake.Complete(true, Ok);
        Check("SocialBridge.Authenticate gets the answer", answer && AccountLink.Current == AccountLink.Status.SignedIn);

        // The store leaderboard platforms sign in through SocialBridge.Authenticate.
        foreach (string file in new[] { "PlayGamesLeaderboards.cs", "GameCenterLeaderboards.cs" })
        {
            string src = File.ReadAllText("Assets/Scripts/Core/Leaderboards/" + file);
            Check(file + ": SIGN IN goes through SocialBridge.Authenticate", src.Contains("SocialBridge.Authenticate("));
        }
        string bridge = File.ReadAllText("Assets/Scripts/Core/SocialBridge.cs");
        Check("SocialBridge.Authenticate routes to AccountLink.SignIn", bridge.Contains("AccountLink.SignIn("));
        string account = File.ReadAllText("Assets/Scripts/Core/CloudSave/PlayGamesAccount.cs");
        Check("Play Games interactive sign-in is ManuallyAuthenticate",
              account.Contains("if (interactive) PlayGamesPlatform.Instance.ManuallyAuthenticate"));
    }

    static void HomeHasNoAccountButtons()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/startS4.unity", OpenSceneMode.Single);
        AccountHintToast.Show(AccountLink.LaunchHintText);
        var offenders = new List<string>();
        foreach (var b in UnityEngine.Object.FindObjectsByType<Button>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            string n = b.name.ToLowerInvariant();
            var t = b.GetComponentInChildren<Text>(true);
            string caption = t != null ? t.text.ToLowerInvariant() : "";
            if (n.Contains("login") || n.Contains("logout") || n.Contains("signin") || n.Contains("signout") ||
                n.Contains("account") || caption.Contains("sign in") || caption.Contains("sign out") ||
                caption.Contains("log out") || caption.Contains("login"))
                offenders.Add(b.name);
        }
        Check("home: no sign-in / sign-out buttons (" + string.Join(",", offenders) + ")", offenders.Count == 0);
    }
}
