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
            OptionsLayoutFitsSafeArea();
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
        AccountDialog.Close();
        LeaderboardPanel.Close();
        if (AccountOptions.Current != null) UnityEngine.Object.DestroyImmediate(AccountOptions.Current.gameObject);
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

    static AccountOptions OpenOptions(Vector2? screen = null, Rect? safe = null)
    {
        EditorSceneManager.OpenScene("Assets/Scenes/leaderboardS3.unity", OpenSceneMode.Single);
        var go = new GameObject("~AccountOptions");
        var row = go.AddComponent<AccountOptions>();
        var s = screen ?? new Vector2(1080f, 2340f);
        row.Build(s, safe ?? new Rect(0f, 0f, s.x, s.y));
        return row;
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

    // ---- SIGN_IN_REQUIRED -> the Account row offers sign-in; success -> signed in ----

    static void SilentFailureShowsSignIn()
    {
        ClearAccountPrefs();
        var fake = new FakeAccount { silentOk = false };
        var sync = Launch(fake);
        Check("launch: one silent attempt", fake.silentSignIns == 1 && fake.interactiveSignIns == 0);
        Check("launch: SIGN_IN_REQUIRED recorded", AccountLink.LastReport.outcome == SignInOutcome.SignInRequired);
        Check("launch: account is signed out", AccountLink.Current == AccountLink.Status.SignedOut);

        var row = OpenOptions();
        Check("options: Account row is built", row.Card != null && row.Card.gameObject.activeInHierarchy);
        Check("options: signed-out row offers SIGN IN WITH GOOGLE PLAY GAMES",
              row.ActionCaption == "SIGN IN WITH GOOGLE PLAY GAMES" && row.NameText == "NOT SIGNED IN");
        Check("options: no details line outside developer mode", row.DetailsText == null);

        int signedInEvents = 0;
        Action onSignedIn = () => signedInEvents++;
        SocialBridge.SignedIn += onSignedIn;
        FakeLeaderboards board;
        var service = QueueScore(out board);
        try
        {
            row.ActionButton.onClick.Invoke();
            Check("tap: the interactive path runs", fake.interactiveSignIns == 1 && fake.Waiting);
            Check("tap: row shows signing in", row.ShownStatus == AccountLink.Status.SigningIn
                  && row.NameText == "SIGNING IN..." && !row.ActionButton.interactable);
            row.ActionButton.onClick.Invoke();
            AccountLink.SignIn();
            Check("tap again while signing in: still one interactive attempt", fake.interactiveSignIns == 1);

            fake.Complete(true, Ok);
            Check("success: row shows the player's name", row.NameText == "SINA SERATI" && row.AvatarText == "S");
            Check("success: row says SIGNED IN and offers SIGN OUT",
                  row.StatusText == "SIGNED IN" && row.ActionCaption == "SIGN OUT");
            Check("success: SignedIn event fired once", signedInEvents == 1);
            Check("success: cloud save read and merged", fake.loads == 1 && sync.Current == CloudSync.State.Ready);
            Check("success: queued leaderboard score flushed", service.SubmitCount == 1 && board.Submissions.Count == 1);
            Check("success: account remembered", PlayerPrefs.GetString(CloudSync.LastAccountKey) == "player-A");

            PlayerPrefs.SetInt(DeveloperUnlocks.EnabledKey, 1);
            row.Refresh();
            Check("developer mode: details line shows the last status code",
                  row.DetailsText != null && row.DetailsText.Contains("SUCCESS"));
            PlayerPrefs.SetInt(DeveloperUnlocks.EnabledKey, 0);
            row.Refresh();
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
        var row = OpenOptions();
        row.ActionButton.onClick.Invoke();
        var report = SignInDiagnosis.FromPlayGames("Canceled", true, "Returning an error code.", .3f, false);
        fake.Complete(false, report);
        Check("config error: row shows SIGN-IN FAILED", row.ShownStatus == AccountLink.Status.Failed
              && row.NameText == "SIGN-IN FAILED");
        Check("config error: one-line set-up hint", row.HintText == "Play Games isn't set up for this build yet.");
        Check("config error: sign-in is still offered", row.ActionCaption == "SIGN IN WITH GOOGLE PLAY GAMES"
              && row.ActionButton.interactable);
        PlayerPrefs.SetInt(DeveloperUnlocks.EnabledKey, 1);
        row.Refresh();
        Check("config error: developer details carry the status code",
              row.DetailsText != null && row.DetailsText.Contains("NOT_AUTHENTICATED_NO_PROMPT"));
        PlayerPrefs.SetInt(DeveloperUnlocks.EnabledKey, 0);

        // A plain cancel: reason, no set-up hint.
        row.ActionButton.onClick.Invoke();
        fake.Complete(false, SignInDiagnosis.FromPlayGames("Canceled", true, "Returning an error code.", 5f, true));
        row.Refresh();
        Check("cancel: reason shown without the set-up hint",
              row.StatusText == "Sign-in was cancelled." && row.HintText == null);
    }

    static void GameCenterWording()
    {
        ClearAccountPrefs();
        var fake = new FakeAccount { silentOk = false };
        Launch(fake);
        AccountLink.AccountPlatformOverride = "ios";
        var row = OpenOptions();
        Check("iOS: signed-out row offers SIGN IN TO GAME CENTER", row.ActionCaption == "SIGN IN TO GAME CENTER");
        row.ActionButton.onClick.Invoke();
        fake.Complete(false, SignInDiagnosis.FromGameCenter(false,
            "The requested operation has been cancelled or disabled by the user.", true, .1f));
        Check("iOS: sheet turned off -> Settings hint",
              row.HintText != null && row.HintText.Contains("Settings > Game Center"));
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
        var row = OpenOptions();
        Check("signed-in launch: row shows the name", row.NameText == "SINA SERATI");
        sync.Poll(urgent: true, force: true);
        int writesBefore = fake.writes.Count;
        string lastAccount = PlayerPrefs.GetString(CloudSync.LastAccountKey);
        string progress = Progress();

        int opened = 0;
        AccountSettingsLink.OpenOverride = () => { opened++; return true; };

        row.ActionButton.onClick.Invoke();
        var dialog = AccountDialog.Current;
        Check("sign out: asks first", dialog != null && dialog.Shown == AccountDialog.Kind.ConfirmSignOut
              && dialog.TitleText == "SIGN OUT?");
        Check("sign out: confirm dialog is the top Back layer", BackNavigator.Top == (object)dialog);
        dialog.SecondaryButton.onClick.Invoke();
        Check("sign out: CANCEL keeps the account", !AccountDialog.IsOpen && !AccountLink.Disconnected
              && row.ShownStatus == AccountLink.Status.SignedIn);

        row.ActionButton.onClick.Invoke();
        AccountDialog.Current.PrimaryButton.onClick.Invoke();
        Check("sign out: disconnect flag stored", AccountLink.Disconnected
              && PlayerPrefs.GetInt(AccountLink.DisconnectedKey) == 1);
        Check("sign out: cloud sync stopped", sync.Current == CloudSync.State.Disconnected);
        Check("sign out: local progress unchanged", Progress() == progress);
        Check("sign out: per-account bookkeeping kept", PlayerPrefs.GetString(CloudSync.LastAccountKey) == lastAccount);
        Check("sign out: row shows SIGNED OUT with sign-in", row.ShownStatus == AccountLink.Status.Disconnected
              && row.NameText == "SIGNED OUT" && row.ActionCaption == "SIGN IN WITH GOOGLE PLAY GAMES");
        var info = AccountDialog.Current;
        Check("sign out: explains the store's own sign-out", info != null && info.Shown == AccountDialog.Kind.SignedOut
              && info.BodyText.Contains("Play Games app"));
        Check("sign out: offers OPEN PLAY GAMES", info != null && info.PrimaryButton != null
              && AccountUi.Caption(info.PrimaryButton) == "OPEN PLAY GAMES");
        if (info != null && info.PrimaryButton != null) info.PrimaryButton.onClick.Invoke();
        Check("sign out: OPEN PLAY GAMES opens the store settings", opened == 1);
        AccountDialog.Close();

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
            row.ActionButton.onClick.Invoke();
            Check("sign in again: flag cleared", !AccountLink.Disconnected && !PlayerPrefs.HasKey(AccountLink.DisconnectedKey));
            Check("sign in again: reconnected", sync.Current == CloudSync.State.Ready && AccountLink.Connected
                  && row.ShownStatus == AccountLink.Status.SignedIn);
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

        var row = OpenOptions();
        Check("next launch: row shows SIGNED OUT", row.ShownStatus == AccountLink.Status.Disconnected);
        row.ActionButton.onClick.Invoke();
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
              && toast.ShownText == "Sign in with Play Games in Options to save progress to your account");
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

    // ---- layout ----

    static void OptionsLayoutFitsSafeArea()
    {
        ClearAccountPrefs();
        var fake = new FakeAccount { silentOk = true };
        Launch(fake);
        var screens = new[]
        {
            (new Vector2(1080f, 2340f), new Rect(0f, 0f, 1080f, 2250f), "1080x2340 notch"),
            (new Vector2(1080f, 1920f), new Rect(0f, 0f, 1080f, 1920f), "1080x1920"),
            (new Vector2(1170f, 2532f), new Rect(0f, 102f, 1170f, 2328f), "iPhone 1170x2532"),
            (new Vector2(1536f, 2048f), new Rect(0f, 0f, 1536f, 2048f), "tablet 3:4"),
            (new Vector2(720f, 1280f), new Rect(0f, 0f, 720f, 1280f), "720x1280"),
            (new Vector2(1080f, 2640f), new Rect(0f, 48f, 1080f, 2496f), "9:22 1080x2640"),
            (new Vector2(1080f, 2880f), new Rect(0f, 48f, 1080f, 2736f), "9:24 1080x2880"),
            (new Vector2(968f, 2376f), new Rect(0f, 0f, 968f, 2286f), "Z Fold cover 968x2376"),
        };
        foreach (bool dev in new[] { false, true })
        {
            foreach (var (screen, safe, name) in screens)
            {
                PlayerPrefs.SetInt(DeveloperUnlocks.EnabledKey, dev ? 1 : 0);
                var row = OpenOptions(screen, safe);
                var devOptions = new GameObject("~DeveloperOptions").AddComponent<DeveloperOptions>();
                typeof(DeveloperOptions).GetMethod("Start", BindingFlags.Instance | BindingFlags.NonPublic)
                    .Invoke(devOptions, null);
                row.Refresh();
                row.Relayout();
                string label = name + (dev ? " (developer)" : "");
                var layout = row.AppliedLayout;
                float h = AccountOptions.Height(dev) * layout.scale;
                float top = layout.centerY + h * .5f + 14f * layout.scale;   // the ACCOUNT tag pokes out
                float bottom = layout.centerY - h * .5f;
                float halfW = (AccountOptions.CardWidth * .5f + 8f) * layout.scale;
                // the Options canvas's own units (not always 800 across: Expand on a tablet)
                float units = row.UnitsPerPixel(screen) > 0f ? row.UnitsPerPixel(screen) : AccountOptions.CanvasUnitsWide / screen.x;
                float safeHalfW = safe.width * units * .5f;
                Check(label + ": card inside the safe area (top)", top <= layout.safeTop + .01f);
                Check(label + ": card inside the safe area (sides)", halfW <= safeHalfW);
                Check(label + ": card readable (scale " + layout.scale.ToString("0.00") + ")", layout.scale >= .7f);

                bool overlaps = false;
                var canvas = (RectTransform)row.Card.parent;
                foreach (RectTransform rt in canvas)
                {
                    if (rt == row.Card || rt.GetComponentInChildren<Button>(true) == null) continue;
                    if (!rt.gameObject.activeInHierarchy) continue;
                    float rTop = rt.anchoredPosition.y + rt.sizeDelta.y * (1f - rt.pivot.y);
                    if (rTop > bottom) { overlaps = true; Debug.Log("[ASI] overlap with " + rt.name); }
                }
                Check(label + ": card clear of the Options buttons", !overlaps);
                var boss = SceneUtil.FindAny("DeveloperBossRush");
                var lb = SceneUtil.FindAny("pullUpLeaderBoard");
                if (dev && boss != null && lb != null)
                {
                    var b = (RectTransform)boss.transform;
                    var l = (RectTransform)lb.transform;
                    Check(label + ": BOSS RUSH clear of LeaderBoard",
                          b.anchoredPosition.y - b.sizeDelta.y * .5f >= l.anchoredPosition.y + l.sizeDelta.y * .5f - .01f);
                }
                UnityEngine.Object.DestroyImmediate(devOptions.gameObject);
            }
        }
        PlayerPrefs.SetInt(DeveloperUnlocks.EnabledKey, 0);
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
        Check("home: no Account row on the home screen", UnityEngine.Object.FindFirstObjectByType<AccountOptions>() == null);
    }
}
