using System.Collections.Generic;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

// Leaderboard foundation: board registry, submission rules (improvement only,
// debounced, offline queue flushed once on sign-in, nothing from developer
// mode or the tutorial), and the in-game panel built from FakeLeaderboards in
// every state, at several screen shapes, opened from the real Options button.
public static class LeaderboardTest
{
    static int failures;

    static void Check(string what, bool ok)
    {
        Debug.Log((ok ? "[LB] PASS  " : "[LB] FAIL  ") + what);
        if (!ok) failures++;
    }

    public static void Run()
    {
        TestHarness.Exit(Execute());
    }

    public static int Execute()
    {
        failures = 0;
        using var sandbox = new TestHarness.Sandbox();

        Registry();
        ImprovementOnly();
        OfflineQueue();
        Blocked();
        PanelStates();
        SafeAreaFit();
        OptionsButton();

        Debug.Log("[LB] failures: " + failures);
        return failures;
    }

    // ---- helpers ----

    static float now;

    static void RealRunContext()
    {
        PlayerPrefs.DeleteKey(LeaderboardService.PendingKey);
        PlayerPrefs.DeleteKey(LeaderboardService.SubmittedKey);
        PlayerPrefs.SetInt(DeveloperUnlocks.EnabledKey, 0);
        PlayerPrefs.SetString("HasDoneTut", "true");
        startMenu.youAreInTutorial = false;
        LeaderboardPanel.Close();
    }

    static LeaderboardService Service(FakeLeaderboards fake)
    {
        now = 0f;
        var service = new LeaderboardService(fake, () => now) { Ios = false };
        LeaderboardService.Instance = service;
        return service;
    }

    static string TopSpeedId { get { return LeaderboardBoards.Get(LeaderboardBoards.TopSpeed).PlatformId(false); } }

    // ---- registry ----

    static void Registry()
    {
        var ids = new HashSet<string>();
        bool unique = true;
        foreach (var b in LeaderboardBoards.All) unique &= ids.Add(b.id);
        Check("registry: logical ids are unique", unique);

        bool both = true, named = true;
        foreach (var b in LeaderboardBoards.Enabled())
        {
            both &= !string.IsNullOrEmpty(b.androidId) && !string.IsNullOrEmpty(b.iosId);
            named &= !string.IsNullOrEmpty(b.displayName) && !string.IsNullOrEmpty(b.description);
        }
        Check("registry: every enabled board has both platform ids", both);
        Check("registry: every enabled board has a name and description", named);

        var top = LeaderboardBoards.Get(LeaderboardBoards.TopSpeed);
        Check("Top Speed uses the Play Games id from StringHolder",
              top != null && top.androidId == StringHolder.leaderboard_highest_speed_reached);
        Check("Top Speed uses me.sinaserati.Pause.highest_speed on iOS",
              top != null && top.iosId == "me.sinaserati.Pause.highest_speed");
        Check("Top Speed is enabled", top != null && top.Enabled);
        Check("Top Speed agrees with AchievementIds' leaderboard entry",
              AchievementIds.Resolve(StringHolder.leaderboard_highest_speed_reached, true) == top.iosId);

        var dust = LeaderboardBoards.Get(LeaderboardBoards.RunStarDust);
        var world = LeaderboardBoards.Get(LeaderboardBoards.FurthestWorld);
        Check("Star Dust placeholder exists and is disabled", dust != null && !dust.Enabled);
        Check("Furthest World placeholder exists and is disabled", world != null && !world.Enabled);
        Check("disabled board has no id on either platform",
              dust.PlatformId(false) == null && dust.PlatformId(true) == null);
        Check("formatters: hundredths", LeaderboardBoards.FormatHundredths(1234) == "12.34");
        Check("formatters: world name", LeaderboardBoards.FormatWorld(2) == WorldManager.Worlds[1].displayName);

        var lower = new LeaderboardBoard("t", "a", "i", "T", "d", LeaderboardSort.LowerIsBetter, null, r => 0);
        Check("sort order: lower-is-better compares the other way", lower.IsBetter(5, 9) && !lower.IsBetter(9, 5));

        RealRunContext();
        var fake = new FakeLeaderboards();
        var service = Service(fake);
        Check("offer to a disabled board is skipped", !service.Offer(LeaderboardBoards.RunStarDust, 500));
        long v;
        Check("nothing queued for a disabled board", !service.HasPending(LeaderboardBoards.RunStarDust, out v));
        int accepted = service.SubmitRun(new LeaderboardRunStats { topSpeed = 50, starDust = 3f, worldIndex = 2 });
        Check("a run end only queues enabled boards", accepted == LeaderboardBoards.Enabled().Count);
        Check("only enabled boards appear as panel tabs", service.UsableBoards().Count == LeaderboardBoards.Enabled().Count);
    }

    // ---- submission ----

    static void ImprovementOnly()
    {
        RealRunContext();
        var fake = new FakeLeaderboards();
        var service = Service(fake);

        Check("first score is accepted", service.Offer(LeaderboardBoards.TopSpeed, 100));
        service.Tick();
        Check("debounced: nothing sent straight away", fake.Submissions.Count == 0);
        now = LeaderboardService.DebounceSeconds + .01f;
        service.Tick();
        Check("sent after the debounce", fake.SubmissionsTo(TopSpeedId) == 1 && fake.Submissions[0].score == 100);
        long v;
        Check("submitted value recorded", service.HasSubmitted(LeaderboardBoards.TopSpeed, out v) && v == 100);
        Check("queue empty after success", !service.HasPending(LeaderboardBoards.TopSpeed, out v));

        Check("a worse score is dropped", !service.Offer(LeaderboardBoards.TopSpeed, 90));
        Check("an equal score is dropped", !service.Offer(LeaderboardBoards.TopSpeed, 100));
        now += 10f;
        service.Tick();
        Check("nothing sent for non-improvements", fake.Submissions.Count == 1);

        // Two offers inside the debounce window: one submission, the better value.
        Check("improvement accepted", service.Offer(LeaderboardBoards.TopSpeed, 110));
        now += .5f;
        Check("further improvement accepted", service.Offer(LeaderboardBoards.TopSpeed, 115));
        now += .5f;
        service.Tick();
        Check("still waiting inside the window", fake.Submissions.Count == 1);
        now += LeaderboardService.DebounceSeconds;
        service.Tick();
        Check("one submission for the burst", fake.Submissions.Count == 2);
        Check("the burst sent its best value", fake.Submissions[1].score == 115);
        service.Tick();
        service.Flush();
        Check("flushing again sends nothing", fake.Submissions.Count == 2);

        // The old direct report now goes through the same rule.
        achievementAPICalls.leaderboard_highest_speed_reached(80f);
        Check("legacy speed report below best: not queued", !service.HasPending(LeaderboardBoards.TopSpeed, out v));
        achievementAPICalls.leaderboard_highest_speed_reached(130f);
        Check("legacy speed report above best: queued",
              service.HasPending(LeaderboardBoards.TopSpeed, out v) && v == 130);
    }

    static void OfflineQueue()
    {
        RealRunContext();
        var fake = new FakeLeaderboards { SignedIn = false };
        var service = Service(fake);

        service.Offer(LeaderboardBoards.TopSpeed, 150);
        Check("signed out: a lower score than the pending one is dropped", !service.Offer(LeaderboardBoards.TopSpeed, 140));
        service.Offer(LeaderboardBoards.TopSpeed, 160);
        now = 60f;
        service.Tick();
        Check("signed out: nothing sent", fake.Submissions.Count == 0);

        var table = LeaderboardService.ScoreTable.Load(LeaderboardService.PendingKey);
        int forBoard = 0;
        foreach (var e in table.entries) if (e.board == LeaderboardBoards.TopSpeed) forBoard++;
        Check("queue persisted in PlayerPrefs under PendingKey", PlayerPrefs.HasKey(LeaderboardService.PendingKey));
        Check("queue holds one entry per board", forBoard == 1);
        long v;
        Check("queue holds the best pending value", table.TryGet(LeaderboardBoards.TopSpeed, out v) && v == 160);

        // Relaunch: a fresh service reads the same queue.
        var relaunched = Service(fake);
        Check("queue survives a relaunch", relaunched.HasPending(LeaderboardBoards.TopSpeed, out v) && v == 160);

        // A failed send keeps it queued.
        fake.SignedIn = true;
        fake.SubmitSucceeds = false;
        relaunched.Flush();
        Check("failed send stays queued", relaunched.HasPending(LeaderboardBoards.TopSpeed, out v) && v == 160);
        fake.SubmitSucceeds = true;
        fake.Submissions.Clear();

        // Sign-in success flushes, exactly once.
        LeaderboardService.Install();
        SocialBridge.NotifySignedIn();
        Check("sign-in flushes the queue", fake.SubmissionsTo(TopSpeedId) == 1 && fake.Submissions[0].score == 160);
        SocialBridge.NotifySignedIn();
        LeaderboardService.Install();
        SocialBridge.NotifySignedIn();
        Check("a second sign-in sends nothing more", fake.Submissions.Count == 1);
        Check("queue cleared after the flush", !relaunched.HasPending(LeaderboardBoards.TopSpeed, out v));
        Check("pending key removed once empty", !PlayerPrefs.HasKey(LeaderboardService.PendingKey));
    }

    static void Blocked()
    {
        RealRunContext();
        var fake = new FakeLeaderboards();
        var service = Service(fake);
        var run = new LeaderboardRunStats { topSpeed = 999 };
        long v;

        PlayerPrefs.SetInt(DeveloperUnlocks.EnabledKey, 1);
        Check("developer mode: run not submitted", service.SubmitRun(run) == 0);
        Check("developer mode: offer refused", !service.Offer(LeaderboardBoards.TopSpeed, 999));
        achievementAPICalls.leaderboard_highest_speed_reached(999f);
        Check("developer mode: nothing queued", !service.HasPending(LeaderboardBoards.TopSpeed, out v));
        PlayerPrefs.SetInt(DeveloperUnlocks.EnabledKey, 0);

        startMenu.youAreInTutorial = true;
        Check("tutorial: run not submitted", service.SubmitRun(run) == 0);
        startMenu.youAreInTutorial = false;

        PlayerPrefs.DeleteKey("HasDoneTut");
        Check("before the tutorial is done: run not submitted", service.SubmitRun(run) == 0);
        PlayerPrefs.SetString("HasDoneTut", "true");

        now = 60f;
        service.Tick();
        service.Flush();
        Check("blocked runs never reach the store", fake.Submissions.Count == 0);
        Check("a real run is submitted", service.SubmitRun(run) == 1);
    }

    // ---- panel ----

    static readonly Vector2 Phone = new Vector2(1080f, 2340f);
    static readonly Rect PhoneSafe = new Rect(0f, 63f, 1080f, 2202f);

    static LeaderboardPanel OpenPanel(FakeLeaderboards fake)
    {
        var service = Service(fake);
        return LeaderboardPanel.Open(service, null, Phone, PhoneSafe);
    }

    static void PanelStates()
    {
        RealRunContext();
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        // Populated (player outside the top 10).
        var fake = FakeLeaderboards.Demo();
        var panel = OpenPanel(fake);
        Check("populated: state", panel.CurrentState == LeaderboardPanel.State.Populated);
        Check("populated: top 10 rows", panel.RowCount == 10);
        Check("populated: own rank row shown", panel.PlayerRowShown && panel.PlayerRankText == "#14");
        Check("populated: one tab per enabled board", panel.Tabs.Count == LeaderboardBoards.Enabled().Count);
        Check("populated: first tab selected", panel.SelectedBoard == LeaderboardBoards.TopSpeed);
        Check("populated: View all shown", panel.ViewAllButton.gameObject.activeSelf);
        var row0 = panel.PanelRoot.Find("Body/Row0");
        var shape0 = row0 != null ? row0.GetComponent<CelShape>() : null;
        Check("populated: other players' rows are card-coloured", shape0 != null && shape0.fill == AkiraPalette.Card);
        var own = panel.PanelRoot.Find("PlayerRow/Row");
        var ownShape = own != null ? own.GetComponent<CelShape>() : null;
        Check("populated: own row is Kaneda red", ownShape != null && ownShape.fill == AkiraPalette.Red);

        // Player inside the top 10: their list row turns red too.
        var mine = new FakeLeaderboards();
        mine.SetBoard(TopSpeedId, FakeLeaderboards.Entry(1, "TETSUO", 300), FakeLeaderboards.Entry(2, "KANEDA", 250, true),
                      FakeLeaderboards.Entry(3, "KAI", 200));
        panel = OpenPanel(mine);
        var row1 = panel.PanelRoot.Find("Body/Row1");
        Check("top-10 player: list row highlighted red",
              row1 != null && row1.GetComponent<CelShape>().fill == AkiraPalette.Red);
        Check("top-10 player: rows match entries", panel.RowCount == 3 && panel.PlayerRankText == "#2");

        // Signed out -> one tap signs in, then loads.
        var signedOut = FakeLeaderboards.Demo();
        signedOut.SignedIn = false;
        panel = OpenPanel(signedOut);
        Check("signed out: state", panel.CurrentState == LeaderboardPanel.State.SignedOut);
        Check("signed out: message", panel.MessageText == "SIGN IN TO SEE RANKINGS");
        Check("signed out: sign-in button", panel.ActionButton != null);
        Check("signed out: no View all", !panel.ViewAllButton.gameObject.activeSelf);
        panel.ActionButton.onClick.Invoke();
        Check("signed out: tap runs one interactive sign-in", signedOut.SignInCalls == 1);
        Check("signed out: rankings load after sign-in", panel.CurrentState == LeaderboardPanel.State.Populated);

        // Sign-in refused: back to signed out.
        var refused = FakeLeaderboards.Demo();
        refused.SignedIn = false;
        refused.SignInSucceeds = false;
        panel = OpenPanel(refused);
        panel.ActionButton.onClick.Invoke();
        Check("sign-in refused: stays signed out", panel.CurrentState == LeaderboardPanel.State.SignedOut);

        // Loading.
        var slow = FakeLeaderboards.Demo();
        slow.DeferLoads = true;
        panel = OpenPanel(slow);
        Check("loading: state", panel.CurrentState == LeaderboardPanel.State.Loading);
        Check("loading: no rows", panel.RowCount == 0 && !panel.PlayerRowShown);
        slow.CompleteLoads();
        Check("loading -> populated", panel.CurrentState == LeaderboardPanel.State.Populated && panel.RowCount == 10);

        // Empty.
        var empty = new FakeLeaderboards();
        panel = OpenPanel(empty);
        Check("empty: state", panel.CurrentState == LeaderboardPanel.State.Empty);
        Check("empty: message", panel.MessageText == "NO SCORES YET");

        // Error / offline, with retry.
        var broken = FakeLeaderboards.Demo();
        broken.LoadsFail = true;
        panel = OpenPanel(broken);
        Check("error: state", panel.CurrentState == LeaderboardPanel.State.Error);
        Check("error: retry button", panel.ActionButton != null);
        broken.LoadsFail = false;
        panel.ActionButton.onClick.Invoke();
        Check("error: retry loads", panel.CurrentState == LeaderboardPanel.State.Populated);

        // No store on this device (editor, Mac).
        panel = LeaderboardPanel.Open(Service(null), null, Phone, PhoneSafe);
        Check("null platform: unavailable state", panel.CurrentState == LeaderboardPanel.State.Unavailable);

        // A stale load for a tab the player left is ignored.
        var stale = FakeLeaderboards.Demo();
        stale.DeferLoads = true;
        panel = OpenPanel(stale);                        // request 1: waits
        var saved = stale.Boards[TopSpeedId];
        stale.DeferLoads = false;
        stale.SetBoard(TopSpeedId);
        panel.Reload();                                  // request 2: answers at once, empty
        stale.Boards[TopSpeedId] = saved;
        stale.CompleteLoads();                           // request 1 answers late, populated
        Check("a stale answer doesn't overwrite the newer one", panel.CurrentState == LeaderboardPanel.State.Empty);

        LeaderboardPanel.Close();
        Check("close removes the panel", !LeaderboardPanel.IsOpen);
    }

    static void SafeAreaFit()
    {
        RealRunContext();
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var cases = new[]
        {
            ("9:16 phone", new Vector2(1080, 1920), new Rect(0, 0, 1080, 1920)),
            ("9:19.5 notch", new Vector2(1170, 2532), new Rect(0, 102, 1170, 2361)),
            ("9:20 punch-hole", new Vector2(1080, 2400), new Rect(0, 0, 1080, 2310)),
            ("9:21 tall", new Vector2(1080, 2520), new Rect(0, 90, 1080, 2340)),
            ("9:22 tall", new Vector2(1080, 2640), new Rect(0, 90, 1080, 2460)),
            ("9:24 tall", new Vector2(1080, 2880), new Rect(0, 90, 1080, 2700)),
            ("Z Fold cover", new Vector2(968, 2376), new Rect(0, 60, 968, 2226)),
            ("3:4 tablet", new Vector2(1536, 2048), new Rect(0, 40, 1536, 1968)),
            ("16:9 landscape", new Vector2(1920, 1080), new Rect(0, 0, 1920, 1080)),
            ("landscape notch", new Vector2(2532, 1170), new Rect(141, 63, 2250, 1107)),
            ("small 2:3", new Vector2(640, 960), new Rect(0, 0, 640, 960)),
        };
        foreach (var (name, screen, safe) in cases)
        {
            var layout = LeaderboardPanel.ComputeLayout(screen, safe);
            var p = layout.panelPixels;
            bool inside = p.xMin >= safe.xMin - .5f && p.xMax <= safe.xMax + .5f &&
                          p.yMin >= safe.yMin - .5f && p.yMax <= safe.yMax + .5f;
            Check(name + ": panel inside the safe area", inside);
            bool big = p.width >= safe.width * .5f || p.height >= safe.height * .9f;
            Check(name + ": panel uses the space (not tiny)", big);
            Check(name + ": row type stays readable (>= 18 px)", 24f * layout.scale >= 18f || screen.x < 700);

            var panel = LeaderboardPanel.Open(Service(FakeLeaderboards.Demo()), null, screen, safe);
            var a = panel.SafeRoot;
            bool anchors = Mathf.Abs(a.anchorMin.x - safe.xMin / screen.x) < 1e-4f &&
                           Mathf.Abs(a.anchorMax.y - safe.yMax / screen.y) < 1e-4f &&
                           Mathf.Abs(a.anchorMin.y - safe.yMin / screen.y) < 1e-4f &&
                           Mathf.Abs(a.anchorMax.x - safe.xMax / screen.x) < 1e-4f;
            Check(name + ": built panel is anchored to the safe area", anchors);
            var scaler = panel.GetComponent<CanvasScaler>();
            Check(name + ": canvas scale applied", scaler != null && Mathf.Approximately(scaler.scaleFactor, layout.scale));
            Check(name + ": everything drawn stays inside the panel", ContentInsidePanel(panel));
        }
        LeaderboardPanel.Close();
    }

    // Every rect under the panel, in panel units, inside the panel (plus the
    // few units a hard cel shadow reaches out).
    static bool ContentInsidePanel(LeaderboardPanel panel)
    {
        var root = panel.PanelRoot;
        float hx = LeaderboardPanel.DesignWidth * .5f + 10f, hy = LeaderboardPanel.DesignHeight * .5f + 10f;
        var corners = new Vector3[4];
        foreach (var rt in root.GetComponentsInChildren<RectTransform>(true))
        {
            if (rt == root) continue;
            rt.GetWorldCorners(corners);
            foreach (var c in corners)
            {
                var local = root.InverseTransformPoint(c);
                if (Mathf.Abs(local.x) > hx || Mathf.Abs(local.y) > hy)
                {
                    Debug.Log("[LB] outside the panel: " + rt.name + " at " + local);
                    return false;
                }
            }
        }
        return true;
    }

    static void OptionsButton()
    {
        RealRunContext();
        EditorSceneManager.OpenScene("Assets/Scenes/leaderboardS3.unity", OpenSceneMode.Single);
        var fake = FakeLeaderboards.Demo();
        Service(fake);

        var go = GameObject.Find("pullUpLeaderBoard");
        var button = go != null ? go.GetComponent<Button>() : null;
        Check("Options has the LeaderBoard button", button != null);
        if (button == null) return;
        int calls = button.onClick.GetPersistentEventCount();
        Object target = calls > 0 ? button.onClick.GetPersistentTarget(0) : null;
        Check("LeaderBoard button calls leaderboard.pull_up_leaderboard",
              calls == 1 && target is leaderboard && button.onClick.GetPersistentMethodName(0) == "pull_up_leaderboard");
        Check("Options still has its Tutorial button (DeveloperOptions' template)", GameObject.Find("Tutorial") != null);

        // Persistent calls are runtime-only; invoke the same method directly.
        ((leaderboard)target).pull_up_leaderboard();
        var panel = LeaderboardPanel.Current;
        Check("LeaderBoard button opens the in-game panel", panel != null);
        if (panel == null) return;
        Check("panel is populated from the fake", panel.CurrentState == LeaderboardPanel.State.Populated);
        Check("panel uses the scene's Orbitron",
              panel.GetComponentInChildren<Text>().font != null &&
              panel.GetComponentInChildren<Text>().font.name.Contains("Orbitron"));
        Check("panel sits above the Options canvas", panel.GetComponent<Canvas>().sortingOrder > 0);

        panel.ViewAllButton.onClick.Invoke();
        Check("View all opens the native UI for the selected board",
              fake.NativeUICalls.Count == 1 && fake.NativeUICalls[0] == TopSpeedId);

        fake.SignedIn = false;
        fake.NativeUICalls.Clear();
        panel.ViewAllButton.onClick.Invoke();
        Check("View all signed out: signs in first, then opens",
              fake.SignInCalls == 1 && fake.NativeUICalls.Count == 1);

        Check("back closes the panel instead of leaving Options", LeaderboardPanel.CloseIfOpen());
        Check("panel gone", !LeaderboardPanel.IsOpen && !LeaderboardPanel.CloseIfOpen());
    }
}
