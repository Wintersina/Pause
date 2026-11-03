using System.Collections.Generic;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

// "NEW RECORD" mid-run (NewRecordTracker, NewRecordPopup, the HUD's NEW tag): the tag shows only once the stored
// best is passed, each leaderboard's popup fires once per run, nothing fires on a board with no stored best, the
// popups queue, and the text is the board's own. (The popup's and the tag's fit on every screen shape is the
// "game-popups" screen of ScreenFitTest.)
//
//   Unity -batchmode -quit -projectPath Pause -executeMethod NewRecordTest.Run
public static class NewRecordTest
{
    static int fails;
    static void Check(string what, bool ok)
    {
        Debug.Log((ok ? "[NRC] PASS  " : "[NRC] FAIL  ") + what);
        if (!ok) fails++;
    }

    public static void Run() { TestHarness.Exit(Execute()); }

    const float Dt = 1f / 60f;

    public static int Execute()
    {
        fails = 0;
        using var sandbox = new TestHarness.Sandbox();
        try
        {
            Wording();
            TrackerFiresOncePerBoard();
            NoStoredBestNoPopup();
            StoredBests();
            PopupQueuesAndTimes();
            HudTagAndPopupInARun();
            TagLayout();
            WorstCaseRow();
        }
        finally
        {
            ClearTables();
            DestroyPopup();
            moveBackGround.speed = 0f;
        }
        Debug.Log("[NRC] failures: " + fails);
        return fails;
    }

    static void ClearTables()
    {
        PlayerPrefs.DeleteKey(LeaderboardService.PendingKey);
        PlayerPrefs.DeleteKey(LeaderboardService.SubmittedKey);
        PlayerPrefs.DeleteKey(RunScore.BestScoreKey);
    }

    static void DestroyPopup()
    {
        if (NewRecordPopup.Current != null) Object.DestroyImmediate(NewRecordPopup.Current.gameObject);
    }

    static void Wording()
    {
        var seen = new HashSet<string>();
        foreach (var b in LeaderboardBoards.All)
        {
            string text = NewRecordPopup.TextFor(b.id);
            Check(b.id + ": its own wording, '" + text + "'", text != NewRecordPopup.Generic && text.EndsWith("!") && seen.Add(text));
            Check(b.id + ": upper case, short enough for one line", text == text.ToUpperInvariant() && text.Length <= 24);
        }
        Check("the generic line", NewRecordPopup.TextFor("nonsense") == "NEW RECORD REACHED!");
        Check("the score board says NEW HIGH SCORE!", NewRecordPopup.TextFor(LeaderboardBoards.TopScore) == "NEW HIGH SCORE!");
        foreach (var b in LeaderboardBoards.All)
        {
            var c = NewRecordPopup.ColourFor(b.id);
            Check(b.id + ": colour is not pink (pink is Space's)", !(c.r > .8f && c.b > .4f && c.g < .4f));
        }
    }

    static NewRecordTracker Fake(Dictionary<string, long> best, Dictionary<string, long> live, System.Func<bool> eligible = null)
    {
        var t = new NewRecordTracker();
        t.Eligible = eligible ?? (() => true);
        t.Baseline = (LeaderboardBoard b, out long v) => best.TryGetValue(b.id, out v);
        t.Live = () => new LeaderboardRunStats
        {
            score = live.ContainsKey(LeaderboardBoards.TopScore) ? live[LeaderboardBoards.TopScore] : 0,
            starDust = live.ContainsKey(LeaderboardBoards.RunStarDust) ? live[LeaderboardBoards.RunStarDust] / 100f : 0f,
            worldsCleared = (int)(live.ContainsKey(LeaderboardBoards.FurthestWorld) ? live[LeaderboardBoards.FurthestWorld] : 0),
        };
        return t;
    }

    static string Join(List<string> l) { return string.Join(",", l); }

    static void TrackerFiresOncePerBoard()
    {
        var best = new Dictionary<string, long> { { LeaderboardBoards.TopScore, 100 }, { LeaderboardBoards.RunStarDust, 500 }, { LeaderboardBoards.FurthestWorld, 2 } };
        var live = new Dictionary<string, long> { { LeaderboardBoards.TopScore, 100 }, { LeaderboardBoards.RunStarDust, 500 }, { LeaderboardBoards.FurthestWorld, 2 } };
        var t = Fake(best, live);
        t.Begin();
        Check("every board with a stored best is watched", t.Watching == best.Count);
        Check("tying the best is not beating it", t.Poll().Count == 0 && !t.Crossed(LeaderboardBoards.TopScore));
        live[LeaderboardBoards.TopScore] = 101;
        Check("score passes its best: only the score board fires", Join(t.Poll()) == LeaderboardBoards.TopScore && t.Crossed(LeaderboardBoards.TopScore));
        live[LeaderboardBoards.TopScore] = 5000;
        Check("... and never again in the run", t.Poll().Count == 0);
        live[LeaderboardBoards.RunStarDust] = 501;
        Check("star dust fires on its own", Join(t.Poll()) == LeaderboardBoards.RunStarDust);
        live[LeaderboardBoards.FurthestWorld] = 3;
        live[LeaderboardBoards.RunStarDust] = 900;
        Check("worlds fires on its own, dust does not repeat", Join(t.Poll()) == LeaderboardBoards.FurthestWorld);
        Check("nothing more fires after all three", t.Poll().Count == 0);
        live[LeaderboardBoards.TopScore] = 1; live[LeaderboardBoards.RunStarDust] = 1; live[LeaderboardBoards.FurthestWorld] = 1;
        Check("the live value dropping back does not re-arm a board", t.Poll().Count == 0);

        live[LeaderboardBoards.TopScore] = 101;
        live[LeaderboardBoards.RunStarDust] = 501; live[LeaderboardBoards.FurthestWorld] = 3;
        t.Begin();
        Check("two boards crossing together fire together, in table order",
              Join(t.Poll()) == LeaderboardBoards.TopScore + "," + LeaderboardBoards.RunStarDust + "," + LeaderboardBoards.FurthestWorld);
        t.Begin();
        Check("a new run starts fresh (the boards can fire again)", t.Poll().Count == 3);
    }

    static void NoStoredBestNoPopup()
    {
        var best = new Dictionary<string, long>();
        var live = new Dictionary<string, long> { { LeaderboardBoards.TopScore, 999999 }, { LeaderboardBoards.RunStarDust, 99999 }, { LeaderboardBoards.FurthestWorld, 9 } };
        var t = Fake(best, live);
        t.Begin();
        Check("a first run ever: no board is watched", t.Watching == 0);
        Check("... and nothing fires however far it goes", t.Poll().Count == 0);
        best[LeaderboardBoards.TopScore] = 10;
        t.Begin();
        Check("a stored best on one board only watches that board", t.Watching == 1 && Join(t.Poll()) == LeaderboardBoards.TopScore);
        var off = Fake(new Dictionary<string, long> { { LeaderboardBoards.TopScore, 10 } }, live, () => false);
        off.Begin();
        Check("an ineligible run (premium ship, developer mode, tutorial) fires nothing", off.Watching == 0 && off.Poll().Count == 0);
        bool eligible = true;
        var mid = Fake(new Dictionary<string, long> { { LeaderboardBoards.TopScore, 10 } }, live, () => eligible);
        mid.Begin();
        eligible = false;
        Check("... and a run that stops being eligible stops firing", mid.Poll().Count == 0);
    }

    static void StoredBests()
    {
        ClearTables();
        var dust = LeaderboardBoards.Get(LeaderboardBoards.RunStarDust);
        long v;
        Check("no stored dust best on a fresh install", !NewRecordTracker.StoredBest(dust, out v));
        var pending = new LeaderboardService.ScoreTable();
        pending.Set(dust.id, 500);
        pending.Save(LeaderboardService.PendingKey);
        Check("the offline queue's value is the stored best", NewRecordTracker.StoredBest(dust, out v) && v == 500);
        var sub = new LeaderboardService.ScoreTable();
        sub.Set(dust.id, 800);
        sub.Save(LeaderboardService.SubmittedKey);
        Check("the better of submitted and pending wins", NewRecordTracker.StoredBest(dust, out v) && v == 800);

        var top = LeaderboardBoards.Get(LeaderboardBoards.TopScore);
        PlayerPrefs.DeleteKey(RunScore.BestScoreKey);
        RunScore.BeginRun(true, true);
        Check("no saved best score: the score board has none", !NewRecordTracker.StoredBest(top, out v));
        RunScore.EndRun(RunScore.RunId);
        PlayerPrefs.SetInt(RunScore.BestScoreKey, 1234);
        RunScore.BeginRun(true, true);
        Check("the saved best score, captured at the run's start", NewRecordTracker.StoredBest(top, out v) && v == 1234);
        RunScore.EndRun(RunScore.RunId);
        ClearTables();
    }

    static float clock;

    static NewRecordPopup NewPopup()
    {
        DestroyPopup();
        clock = 100f;
        var p = NewRecordPopup.Build();
        p.Clock = () => clock;
        return p;
    }

    static void PopupQueuesAndTimes()
    {
        float scale = Time.timeScale;
        var p = NewPopup();
        Check("the popup is brief: 1.5 to 2 s", NewRecordPopup.TotalSeconds >= 1.5f && NewRecordPopup.TotalSeconds <= 2f);
        p.Push(LeaderboardBoards.TopScore);
        p.Push(LeaderboardBoards.RunStarDust);
        p.Push(LeaderboardBoards.FurthestWorld);
        Check("three at once: the first shows, two wait", p.Showing && p.ShowingText == "NEW HIGH SCORE!" && p.Queued == 2);
        clock += NewRecordPopup.TotalSeconds * .5f;
        p.Tick(clock);
        Check("mid-way the first is still up, fully visible", p.ShowingText == "NEW HIGH SCORE!" && p.Queued == 2 && p.Box.GetComponent<CanvasGroup>().alpha > .99f);
        clock += NewRecordPopup.TotalSeconds * .5f + .01f;
        p.Tick(clock);
        Check("then the second takes over", p.ShowingText == NewRecordPopup.TextFor(LeaderboardBoards.RunStarDust) && p.Queued == 1);
        clock += NewRecordPopup.TotalSeconds + .01f;
        p.Tick(clock);
        Check("then the third", p.ShowingText == NewRecordPopup.TextFor(LeaderboardBoards.FurthestWorld) && p.Queued == 0);
        p.ApplyAt(NewRecordPopup.TotalSeconds - .02f);
        Check("it fades out at the end", p.Box.GetComponent<CanvasGroup>().alpha < .2f);
        p.ApplyAt(0f);
        Check("it pops in (starts big, small alpha)", p.Box.localScale.x > 1.2f && p.Box.GetComponent<CanvasGroup>().alpha < .1f);
        clock += NewRecordPopup.TotalSeconds + .01f;
        p.Tick(clock);
        Check("when the queue is empty it switches itself off", !p.Showing && !p.gameObject.activeSelf);

        Check("it never touches the game clock", Mathf.Approximately(Time.timeScale, scale));
        Check("no box or border: text only", p.GetComponentsInChildren<Image>(true).Length == 0 && p.GetComponentsInChildren<Text>(true).Length == 1);
        Check("no sound", p.GetComponentsInChildren<AudioSource>(true).Length == 0);
        Check("never blocks a tap", p.GetComponent<UnityEngine.UI.GraphicRaycaster>() == null && !p.Label.raycastTarget &&
              !p.Box.GetComponent<CanvasGroup>().blocksRaycasts);
        Check("text outline plus glow, no separate border object", p.Label.GetComponents<Outline>().Length == 2);
        DestroyPopup();
    }

    static ScoreHud BuildHud(out GameObject go)
    {
        EditorSceneManager.OpenScene("Assets/Scenes/gameS1.unity", OpenSceneMode.Single);
        PlayerPrefs.SetString("HasDoneTut", "true");
        PlayerPrefs.SetInt(DeveloperUnlocks.EnabledKey, 0);
        startMenu.youAreInTutorial = false;
        buttonClicks.playerDied = false;
        moveBackGround.speed = SpeedRamp.Cap;
        go = new GameObject("~HudNewRecordTest");
        go.AddComponent<HudStyler>().SendMessage("Start");
        return go.GetComponent<ScoreHud>();
    }

    static void Fly(long target)
    {
        for (int i = 0; i < 40000 && RunScore.Total <= target; i++) RunScore.Tick(Dt, SpeedRamp.Cap);
    }

    static void HudTagAndPopupInARun()
    {
        ClearTables();
        RunScore.EndRun(RunScore.RunId);
        PlayerPrefs.SetInt(RunScore.BestScoreKey, 60);
        DestroyPopup();
        RunScore.BeginRun(true, true);
        var hud = BuildHud(out GameObject go);
        if (hud == null || hud.NewTag == null) { Check("the HUD has a NEW tag", false); return; }
        float now = 10f;
        hud.CheckRecords(now);
        Check("the tag exists, between SCORE and the chain badge, and is hidden at the start", hud.NewTag.transform.parent == hud.ScoreText.transform && hud.NewTag.text == "");
        Fly(20);
        hud.CheckRecords(now += .3f);
        Check("below the best: no NEW, no popup", hud.NewTag.text == "" && NewRecordPopup.Current == null);
        Fly(60);
        hud.CheckRecords(now += .3f);
        Check("past the best: NEW appears", hud.NewTag.text == "NEW");
        var popup = NewRecordPopup.Current;
        Check("... with NEW HIGH SCORE! up", popup != null && popup.Showing && popup.ShowingText == "NEW HIGH SCORE!" && popup.Queued == 0);
        popup.Clock = () => clock;
        for (int i = 0; i < 8; i++) hud.CheckRecords(now += .3f);
        Fly(RunScore.Total + 500);
        for (int i = 0; i < 8; i++) hud.CheckRecords(now += .3f);
        Check("the tag stays and the popup never re-triggers in the run", hud.NewTag.text == "NEW" && popup.Queued == 0 && popup.ShowingBoard == LeaderboardBoards.TopScore);
        // the next run: best is now the saved score, the tag clears
        RunScore.EndRun(RunScore.RunId);
        RunScore.BeginRun(true, true);
        hud.CheckRecords(now += .3f);
        Check("a new run clears the tag", hud.NewTag.text == "");

        // a first run ever: nothing, however far
        DestroyPopup();
        RunScore.EndRun(RunScore.RunId);
        PlayerPrefs.DeleteKey(RunScore.BestScoreKey);
        RunScore.BeginRun(true, true);
        hud.CheckRecords(now += .3f);
        Fly(300);
        hud.CheckRecords(now += .3f);
        Check("no stored best: no NEW and no popup", hud.NewTag.text == "" && NewRecordPopup.Current == null);
        Object.DestroyImmediate(go);
        DestroyPopup();
        RunScore.EndRun(RunScore.RunId);
        ClearTables();
    }

    static void TagLayout()
    {
        RunScore.BeginRun(true, true);
        var hud = BuildHud(out GameObject go);
        if (hud == null || hud.NewTag == null) { Check("layout: the HUD has a NEW tag", false); return; }
        var row = hud.ScoreText;
        foreach (bool chain in new[] { false, true })
            foreach (string label in new[] { "SCORE  0", "SCORE  123,456", "SCORE  9,999,999" })
            {
                row.text = label;
                hud.ChainText.text = chain ? "x4" : "";
                hud.NewTag.text = ScoreHud.NewTagLabel;
                Canvas.ForceUpdateCanvases();
                hud.ForceNewTagPlacement();
                float rowW = row.rectTransform.rect.width;
                float tagRight = rowW + hud.NewTag.rectTransform.anchoredPosition.x, tagLeft = tagRight - hud.NewTag.preferredWidth;
                float chainLeft = rowW - ScoreHud.BadgeInset - (chain ? hud.ChainText.preferredWidth : 0f);
                string what = "'" + label + "'" + (chain ? " + x4" : "");
                Check(what + ": NEW fits its box and the row", hud.NewTag.preferredWidth <= ScoreHud.NewTagWidth && tagLeft >= 0f);
                Check(what + ": NEW is " + (hud.NewTagInline ? "inline" : "dropped under") + ", clear of the figure and the chain badge",
                      hud.NewTagInline ? row.preferredWidth <= tagLeft && tagRight <= chainLeft : hud.NewTag.rectTransform.anchoredPosition.y < 0f);
                Check(what + ": NEW text is a readable size", hud.NewTag.fontSize >= 14);
            }
        Check("the tag is not a raycast target, and has no box", !hud.NewTag.raycastTarget && hud.NewTag.GetComponent<Image>() == null);
        Object.DestroyImmediate(go);
    }

    // The crowded row: a 7-digit score, the x2 plate ACTIVE, the NEW tag and an x4 chain at once. Nothing overlaps and the
    // x2 plate is never hidden for NEW (it is a gameplay state).
    static void WorstCaseRow()
    {
        ClearTables();
        RunScore.EndRun(RunScore.RunId);
        PlayerPrefs.SetInt(RunScore.BestScoreKey, 5);
        RunScore.BeginRun(true, true);
        var hud = BuildHud(out GameObject go);
        if (hud == null || hud.NewTag == null) { Check("worst case: the HUD has a NEW tag", false); return; }
        var cue = hud.ScoreText.GetComponent<ScoreX2Cue>();
        Fly(10);
        float now = 50f;
        hud.CheckRecords(now);
        ScoreMultiplier.Tick(ScoreMultiplier.StreakSeconds + .1f, SpeedRamp.Cap);
        cue.Refresh(1000f);
        cue.Refresh(1001f);
        var row = hud.ScoreText;
        row.text = ScoreHud.Label(9999999);
        hud.ChainText.text = "x4";
        Canvas.ForceUpdateCanvases();
        cue.Remeasure();
        hud.CheckRecords(now += .3f);
        Check("worst case: NEW is showing and the x2 plate is active and shown", hud.NewTag.text == "NEW" && cue.State == ScoreX2Cue.CueState.Active && cue.Plate.enabled && cue.PlateX >= 0f);
        hud.ForceNewTagPlacement();
        float rowW = row.rectTransform.rect.width;
        float chainRight = rowW - ScoreHud.BadgeInset, chainLeft = chainRight - hud.ChainText.preferredWidth;
        float newRight = rowW + hud.NewTag.rectTransform.anchoredPosition.x, newLeft = newRight - hud.NewTag.preferredWidth;
        float figureRight = row.preferredWidth, plateLeft = cue.PlateX, plateRight = plateLeft + ScoreX2Cue.PlateWidth;
        Debug.Log("[NRC] worst case row " + rowW.ToString("F0") + ": figure ..." + figureRight.ToString("F0") + " plate " + plateLeft.ToString("F0") + ".." + plateRight.ToString("F0") +
                  " NEW " + newLeft.ToString("F0") + ".." + newRight.ToString("F0") + (hud.NewTagInline ? " inline" : " under") + " chain " + chainLeft.ToString("F0") + ".." + chainRight.ToString("F0"));
        Check("worst case: figure, x2 plate and x4 in order, no overlap", figureRight <= plateLeft && plateRight <= chainLeft - 1f && chainRight <= rowW);
        if (hud.NewTagInline)
            Check("worst case: NEW inline between the plate and x4", plateRight <= newLeft - 1f && newRight <= chainLeft - 1f);
        else
        {
            // under the row, in the gap between the SPEED figure and its SPD badge
            var speed = SceneUtil.FindAny("SpeedText").GetComponent<Text>();
            Check("worst case: NEW drops under, clear of the SPEED figure and the SPD badge column",
                  hud.NewTag.rectTransform.anchoredPosition.y < 0f && newLeft >= speed.preferredWidth + 3f && newRight <= rowW - ScoreHud.BadgeInset - 100f);
        }
        Check("worst case: NEW keeps its size and fits its box", hud.NewTag.fontSize == ScoreHud.NewTagFontSize && hud.NewTag.preferredWidth <= ScoreHud.NewTagWidth);
        Check("worst case: the plate is still the same size and not shrunk", Mathf.Approximately(cue.Plate.rectTransform.sizeDelta.x, ScoreX2Cue.PlateWidth));
        Object.DestroyImmediate(go);
        RunScore.EndRun(RunScore.RunId);
        ScoreMultiplier.Reset();
        ClearTables();
    }
}
