using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// Phase 2 of the shared leaderboard (docs/shared-leaderboard.md): the UI, against FakeSharedBoard and FakeLeaderboards.
//   1  the home Leaderboard button: shared board only when sharing is on and the backend usable, else the native panel
//   2  the shared panel: sorts load from the service (never re-sorted here), own row off the page, the cache rule,
//      platform marks, the error state with the native-board fallback
//   3  the Leaderboard Settings modal: sync now results, the sharing switch, delete with its confirm step, privacy link, back
public static class LeaderboardSettingsTest
{
    static int failures;

    static void Check(string what, bool ok)
    {
        Debug.Log((ok ? "[LBS] PASS  " : "[LBS] FAIL  ") + what);
        if (!ok) failures++;
    }

    public static void Run() { TestHarness.Exit(Execute()); }

    static readonly Vector2 Phone = new Vector2(1080f, 2340f);
    static readonly Rect PhoneSafe = new Rect(0f, 63f, 1080f, 2202f);
    static float nativeNow, sharedNow;
    static long unix;
    static FakeLeaderboards nativeFake;
    static FakeSharedBoard sharedFake;

    static LeaderboardBoard[] FullTable()
    {
        return new[]
        {
            new LeaderboardBoard(LeaderboardBoards.TopScore, "CgkUScore", AchievementIds.IosPrefix + "top_score", "Top Score", "s",
                LeaderboardSort.HigherIsBetter, LeaderboardBoards.FormatScore, r => r.score, 1),
            new LeaderboardBoard(LeaderboardBoards.RunStarDust, "CgkUDust", "", "Star Dust", "d",
                LeaderboardSort.HigherIsBetter, LeaderboardBoards.FormatHundredths, r => (long)System.Math.Round(r.starDust * 100.0), 1),
            new LeaderboardBoard(LeaderboardBoards.FurthestWorld, "CgkUWorlds", "", "Worlds", "w",
                LeaderboardSort.HigherIsBetter, LeaderboardBoards.FormatWorlds, r => System.Math.Max(0, r.worldsCleared), 1),
            new LeaderboardBoard(LeaderboardBoards.BoostedRuns, "CgkUBoosted", "", "Power-Ups", "b",
                LeaderboardSort.HigherIsBetter, LeaderboardBoards.FormatScore, r => r.score, 1, true),
        };
    }

    public static int Execute()
    {
        failures = 0;
        using var sandbox = new TestHarness.Sandbox();
        var settling = LeaderboardService.CloudSettling;
        var shared = SharedLeaderboardService.HasInstance ? SharedLeaderboardService.Instance : null;
        var native = LeaderboardService.HasInstance ? LeaderboardService.Instance : null;
        var opener = ExternalLinks.Opener;
        var clock = LeaderboardSettingsPanel.Clock;
        var deviceOffline = LeaderboardSettingsPanel.DeviceOffline;
        LeaderboardService.CloudSettling = () => false;
        LeaderboardBoards.OverrideForTests(FullTable());
        try
        {
            Home();
            SharedPanel();
            Modal();
        }
        finally
        {
            LeaderboardPanel.Close();
            LeaderboardSettingsPanel.Close();
            LeaderboardBoards.OverrideForTests(null);
            LeaderboardService.CloudSettling = settling;
            SharedLeaderboardService.Instance = shared;
            LeaderboardService.Instance = native;
            ExternalLinks.Opener = opener;
            ExternalLinks.ResetGuard();
            LeaderboardSettingsPanel.Clock = clock;
            LeaderboardSettingsPanel.DeviceOffline = deviceOffline;
        }
        Debug.Log("[LBS] failures: " + failures);
        return failures;
    }

    static string[] Keys()
    {
        var keys = new List<string>
        {
            SharedLeaderboardService.SharingKey, SharedLeaderboardService.PendingKey, SharedLeaderboardService.SubmittedKey,
            SharedLeaderboardService.BoundUidKey,
            LeaderboardService.PendingKey, LeaderboardService.SubmittedKey, LeaderboardService.NormalBestKey, LeaderboardService.BoostedBestKey,
            LeaderboardService.BoundPlayerKey, LeaderboardService.SeasonKey, RunScore.BestScoreKey, AccountLink.DisconnectedKey,
        };
        for (int t = 0; t < 2; t++) for (int c = 0; c < 3; c++) keys.Add(SharedLeaderboardService.CachePrefix + t + "_" + c);
        return keys.ToArray();
    }

    // Both services on fresh fakes: platform and Firebase signed in, sharing on (the default).
    static void Setup(bool sharedSignedIn = true)
    {
        LeaderboardPanel.Close();
        LeaderboardSettingsPanel.Close();
        foreach (string k in Keys()) PlayerPrefs.DeleteKey(k);
        PlayerPrefs.SetInt(DeveloperUnlocks.EnabledKey, 0);
        PlayerPrefs.SetString("HasDoneTut", "true");
        startMenu.youAreInTutorial = false;
        nativeNow = 0f; sharedNow = 100f; unix = 1700000000;
        nativeFake = new FakeLeaderboards { LocalPlayerName = "KANEDA", LocalPlayerId = "local" };
        LeaderboardService.Instance = new LeaderboardService(nativeFake, () => nativeNow) { PlayerId = () => "local" };
        sharedFake = new FakeSharedBoard { SignedIn = sharedSignedIn };
        sharedFake.Now = 1000000 + sharedNow;
        SharedLeaderboardService.Instance = new SharedLeaderboardService(sharedFake, () => sharedNow)
        {
            PlatformSignedIn = () => true,
            Disconnected = () => false,
            PlayerName = () => "KANEDA",
            UnixNow = () => unix,
        };
    }

    static SharedDoc Doc(string name, string platform, long score, long dust, int worlds, int ship, int powerUps = 0)
    {
        var d = new SharedDoc { name = name, platform = platform };
        var run = SharedRun.Blank();
        run.score = score; run.dust = dust; run.worlds = worlds; run.ship = ship; run.hearts = 3; run.endHearts = 0; run.pauses = 1; run.powerUps = powerUps;
        d.Offer(run);
        return d;
    }

    // 12 other players on both platforms, the local player far down (off the page).
    static void SeedBoards()
    {
        for (int t = 0; t < 2; t++)
        {
            for (int i = 0; i < 12; i++)
                sharedFake.Seed((SharedTable)t, "p" + i,
                    Doc("PILOT" + i, i % 2 == 0 ? SharedBoardLimits.Android : SharedBoardLimits.Ios,
                        10000 - i * 100, 100 + i * 50, 3 + i, 1 + i % 9, t == 1 ? 1 : 0));
            sharedFake.Seed((SharedTable)t, sharedFake.Uid, Doc("KANEDA", SharedBoardLimits.Ios, 50, 10, 1, 2, t == 1 ? 1 : 0));
        }
    }

    // A native score mark with its run, in the submitted or the pending record.
    static void Mark(string key)
    {
        var marks = LeaderboardService.ScoreTable.Load(key);
        marks.Set(LeaderboardBoards.TopScore, 4000, LeaderboardRunTag.Encode(5, 3, 0, 1, 0, 100, 2, 0, 4000));
        marks.Save(key);
    }

    static int MarkCount(LeaderboardPanel panel)
    {
        int n = 0;
        foreach (var img in panel.GetComponentsInChildren<Image>()) if (img.name == "Platform" && img.sprite != null) n++;
        return n;
    }

    // ---- 1 the home button ----

    static void Home()
    {
        Setup();
        var panel = LeaderboardPanel.OpenHome(Phone, PhoneSafe, true);
        Check("home: sharing on and the backend usable opens the shared board", panel.SharedMode);
        LeaderboardPanel.Close();

        SharedLeaderboardService.SharingOn = false;
        panel = LeaderboardPanel.OpenHome(Phone, PhoneSafe, true);
        Check("home: sharing off opens the native panel", !panel.SharedMode);
        LeaderboardPanel.Close();

        Setup();
        SharedLeaderboardService.Instance = new SharedLeaderboardService(new NullSharedBoard(), () => 0f);
        panel = LeaderboardPanel.OpenHome(Phone, PhoneSafe, true);
        Check("home: the Null backend (no SDK) opens the native panel", !panel.SharedMode);
        LeaderboardPanel.Close();

        Setup(false);
        panel = LeaderboardPanel.OpenHome(Phone, PhoneSafe, true);
        Check("home: a backend with no Firebase user opens the native panel", !panel.SharedMode);
        LeaderboardPanel.Close();

        Setup();
        sharedFake.Available = false;
        panel = LeaderboardPanel.OpenHome(Phone, PhoneSafe, true);
        Check("home: an unavailable backend opens the native panel", !panel.SharedMode);
        LeaderboardPanel.Close();
    }

    // ---- 2 the shared panel ----

    static void SharedPanel()
    {
        Setup();
        SeedBoards();
        var panel = LeaderboardPanel.OpenHome(Phone, PhoneSafe, true);
        Check("shared: two tabs (MAIN, POWER-UPS) and three sort headers",
              panel.SharedMode && panel.Tabs.Count == 2 && panel.Headers.Count == 3 && panel.SelectedBoard == LeaderboardBoards.TopScore);
        Check("shared: populated with the top 10 in the service's order",
              panel.CurrentState == LeaderboardPanel.State.Populated && panel.RowCount == 10 && sharedFake.LoadCalls == 1);
        Check("shared: own row shows with no rank number (off the page)",
              panel.PlayerRowShown && panel.PlayerRankText == "#--" && panel.UnrankedNote == null);
        Check("shared: every row carries a platform mark (10 rows + own)", MarkCount(panel) == 11);

        panel.SelectBoard(LeaderboardBoards.RunStarDust);
        Check("shared: the DUST header loads that sort from the service", sharedFake.LoadCalls == 2 && panel.SortedBoard == LeaderboardBoards.RunStarDust);
        var first = panel.PanelRoot.Find("List/Rows/Row0/Name");
        var firstName = first != null ? first.GetComponent<Text>() : null;
        Check("shared: rows are not re-sorted client-side (dust order = PILOT11 first)", firstName != null && firstName.text.Contains("PILOT11"));
        panel.SelectBoard(LeaderboardBoards.TopScore);
        Check("shared: SCORE again is within the 5 minute cache, no new read", sharedFake.LoadCalls == 2);
        panel.SelectBoard(LeaderboardBoards.BoostedRuns);
        Check("shared: the POWER-UPS tab reads the power table", sharedFake.LoadCalls == 3 && panel.SelectedTab == LeaderboardBoards.BoostedRuns
              && panel.CurrentState == LeaderboardPanel.State.Populated);
        LeaderboardPanel.Close();

        // The same page opened again inside the TTL costs nothing, and says nothing of its age.
        panel = LeaderboardPanel.OpenHome(Phone, PhoneSafe, true);
        Check("shared: reopening within the cache window reads nothing", sharedFake.LoadCalls == 3 && panel.AgeNote == null);
        LeaderboardPanel.Close();

        // An aged cache shown because the load failed carries its age.
        unix += 3600;
        sharedFake.Fail = SharedResult.Offline;
        panel = LeaderboardPanel.OpenHome(Phone, PhoneSafe, true);
        Check("shared: a failed refresh serves the cached page with its age",
              panel.CurrentState == LeaderboardPanel.State.Populated && panel.AgeNote != null && panel.AgeNote.StartsWith("UPDATED"));
        LeaderboardPanel.Close();

        // Error state with the native fallback.
        Setup();
        SeedBoards();
        sharedFake.Fail = SharedResult.Offline;
        panel = LeaderboardPanel.OpenHome(Phone, PhoneSafe, true);
        Check("fallback: a failed shared load shows the error state with RETRY and a native-board action",
              panel.CurrentState == LeaderboardPanel.State.Error && panel.ActionButton != null && panel.NativeButton != null);
        sharedFake.Fail = null;
        panel.ActionButton.onClick.Invoke();
        Check("fallback: RETRY bypasses the cache and recovers", panel.CurrentState == LeaderboardPanel.State.Populated);
        LeaderboardPanel.Close();

        Setup();
        SeedBoards();
        sharedFake.Fail = SharedResult.Offline;
        nativeFake.SetBoard("CgkUScore", new LeaderboardEntry { rank = 1, playerId = "x", playerName = "NATIVEPILOT", value = 5000 });
        panel = LeaderboardPanel.OpenHome(Phone, PhoneSafe, true);
        panel.NativeButton.onClick.Invoke();
        Check("fallback: the action turns the panel into the native board", !panel.SharedMode && panel.NativeButton == null);
        Check("fallback: and the native board loads", panel.CurrentState == LeaderboardPanel.State.Populated && MarkCount(panel) == 0);
        LeaderboardPanel.Close();
    }

    // ---- 3 the settings modal ----

    static void Modal()
    {
        // Sync: signed out everywhere.
        Setup(false);
        nativeFake.SignedIn = false;
        var modal = LeaderboardSettingsPanel.Open(null, Phone, PhoneSafe);
        modal.SyncButton.onClick.Invoke();
        Check("sync: signed out says to sign in", modal.SyncText == LeaderboardSettingsPanel.SignInText);
        LeaderboardSettingsPanel.Close();

        // Sync: signed in, the queues drain.
        Setup();
        nativeNow = 100f;
        modal = LeaderboardSettingsPanel.Open(null, Phone, PhoneSafe);
        Mark(LeaderboardService.SubmittedKey);
        modal.SyncButton.onClick.Invoke();
        Check("sync: sends the bests and says Synced",
              modal.SyncText == LeaderboardSettingsPanel.SyncedText && !modal.Syncing && sharedFake.UploadCalls >= 1);
        LeaderboardSettingsPanel.Close();

        // Sync: nothing confirms. Offline only when the device is offline; otherwise a plain "couldn't sync".
        foreach (bool offline in new[] { true, false })
        {
            Setup();
            nativeFake.SubmitSucceeds = false;
            float t = 0f;
            LeaderboardSettingsPanel.Clock = () => t;
            LeaderboardSettingsPanel.DeviceOffline = () => offline;
            Mark(LeaderboardService.PendingKey);
            modal = LeaderboardSettingsPanel.Open(null, Phone, PhoneSafe);
            modal.SyncButton.onClick.Invoke();
            Check("sync: an unconfirmed send shows Syncing first", modal.Syncing && modal.SyncText == LeaderboardSettingsPanel.SyncingText);
            t = LeaderboardSettingsPanel.SyncWaitSeconds + 1f;
            modal.SendMessage("Update");
            string want = offline ? LeaderboardSettingsPanel.OfflineText : LeaderboardSettingsPanel.FailedText;
            Check("sync: times out as " + want + (offline ? " (device offline)" : " (device online)"), !modal.Syncing && modal.SyncText == want);
            LeaderboardSettingsPanel.Clock = () => Time.unscaledTime;
            LeaderboardSettingsPanel.Close();
        }

        // The sharing switch.
        Setup();
        Check("share: on by default", SharedLeaderboardService.SharingOn);
        modal = LeaderboardSettingsPanel.Open(null, Phone, PhoneSafe);
        modal.ShareButton.onClick.Invoke();
        Check("share: the toggle turns it off and persists it", !SharedLeaderboardService.SharingOn && PlayerPrefs.GetInt(SharedLeaderboardService.SharingKey, 1) == 0);
        Check("share: turning it off deletes nothing", sharedFake.DeleteCalls == 0);
        modal.ShareButton.onClick.Invoke();
        Check("share: and back on", SharedLeaderboardService.SharingOn);
        LeaderboardSettingsPanel.Close();

        // Delete with its confirm step.
        Setup();
        SeedBoards();
        modal = LeaderboardSettingsPanel.Open(null, Phone, PhoneSafe);
        Check("delete: a separate button, no confirm yet", modal.DeleteButton != null && modal.Stage == LeaderboardSettingsPanel.DeleteStage.Idle);
        modal.DeleteButton.onClick.Invoke();
        Check("delete: pressing it asks first (CANCEL / DELETE), nothing deleted",
              modal.Stage == LeaderboardSettingsPanel.DeleteStage.Confirm && modal.ConfirmButton != null && modal.CancelButton != null
              && sharedFake.DeleteCalls == 0 && sharedFake.Get(SharedTable.Main, sharedFake.Uid) != null);
        modal.CancelButton.onClick.Invoke();
        Check("delete: CANCEL goes back and deletes nothing", modal.Stage == LeaderboardSettingsPanel.DeleteStage.Idle && sharedFake.DeleteCalls == 0);
        modal.DeleteButton.onClick.Invoke();
        modal.ConfirmButton.onClick.Invoke();
        Check("delete: DELETE removes both documents, turns sharing off and says so",
              sharedFake.DeleteCalls == 1 && sharedFake.Get(SharedTable.Main, "uid-local") == null && sharedFake.Get(SharedTable.Power, "uid-local") == null
              && !SharedLeaderboardService.SharingOn && modal.DeleteText == LeaderboardSettingsPanel.DeletedText
              && modal.Stage == LeaderboardSettingsPanel.DeleteStage.Idle);
        LeaderboardSettingsPanel.Close();

        Setup();
        SeedBoards();
        sharedFake.AuthDeleteSucceeds = false;
        modal = LeaderboardSettingsPanel.Open(null, Phone, PhoneSafe);
        modal.DeleteButton.onClick.Invoke();
        modal.ConfirmButton.onClick.Invoke();
        Check("delete: if the account could not be removed it says it signed out", modal.DeleteText == LeaderboardSettingsPanel.DeletedSignedOutText);
        LeaderboardSettingsPanel.Close();

        Setup();
        SeedBoards();
        sharedFake.DeleteFails = true;
        modal = LeaderboardSettingsPanel.Open(null, Phone, PhoneSafe);
        modal.DeleteButton.onClick.Invoke();
        modal.ConfirmButton.onClick.Invoke();
        Check("delete: a failure says so and changes nothing",
              modal.DeleteText == LeaderboardSettingsPanel.DeleteFailedText && SharedLeaderboardService.SharingOn
              && sharedFake.Get(SharedTable.Main, sharedFake.Uid) != null);
        LeaderboardSettingsPanel.Close();

        Setup();
        SharedLeaderboardService.Instance = new SharedLeaderboardService(new NullSharedBoard(), () => 0f);
        modal = LeaderboardSettingsPanel.Open(null, Phone, PhoneSafe);
        modal.DeleteButton.onClick.Invoke();
        modal.ConfirmButton.onClick.Invoke();
        Check("delete: with no shared backend it says the board isn't available", modal.DeleteText == LeaderboardSettingsPanel.UnavailableText);
        LeaderboardSettingsPanel.Close();

        // Privacy link and back.
        Setup();
        var opened = new List<string>();
        ExternalLinks.Opener = u => opened.Add(u);
        ExternalLinks.ResetGuard();
        modal = LeaderboardSettingsPanel.Open(null, Phone, PhoneSafe);
        modal.PrivacyButton.onClick.Invoke();
        Check("privacy: the link opens the privacy page", opened.Count == 1 && opened[0] == ExternalLinks.PrivacyPolicy);
        Check("back: the modal is a BackNavigator layer", LeaderboardSettingsPanel.IsOpen && BackNavigator.IsRegistered(modal));
        BackNavigator.Back();
        Check("back: the back button closes it", !LeaderboardSettingsPanel.IsOpen);

        // Sizes: 100-unit buttons, type at least 16 units, a short screen scrolls.
        Setup();
        modal = LeaderboardSettingsPanel.Open(null, Phone, PhoneSafe);
        bool tall = true, type = true;
        foreach (var b in modal.GetComponentsInChildren<Button>())
            if (((RectTransform)b.transform).rect.height < 99.9f) tall = false;
        foreach (var x in modal.GetComponentsInChildren<Text>())
            if (x.resizeTextForBestFit ? x.resizeTextMinSize < 16 : x.fontSize < 16) type = false;
        Check("sizes: every button is a 100-unit target", tall);
        Check("sizes: all type is 16 units or more", type);
        Check("sizes: the phone shows the whole modal without scrolling", !modal.Scrolls);
        LeaderboardSettingsPanel.Close();
        // A small 480 x 640 phone at UiScale's floor gets fewer units, and the middle scrolls.
        using (ScreenInfo.Override(480, 640, new Rect(0f, 0f, 480f, 640f)))
        {
            modal = LeaderboardSettingsPanel.Open(null, new Vector2(480f, 640f), new Rect(0f, 0f, 480f, 640f));
            Check("sizes: a short screen scrolls the middle (panel " + modal.AppliedLayout.panelSize + ")", modal.Scrolls);
            LeaderboardSettingsPanel.Close();
        }
    }
}
