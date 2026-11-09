using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// "A new record!" mid-run: the moment a live run first beats the player's STORED best on a leaderboard.
// Compared locally, never against a network read:
//   Top Score   the saved best score (RunScore.BestAtStart)
//   other boards the best already on the offline queues (LeaderboardService's submitted / pending tables)
// A board with no stored best yet (a first run) never fires, or every first run would at once.
// Each board fires at most once per run (a crossing is remembered), and premium / developer / tutorial runs fire nothing.
public sealed class NewRecordTracker
{
    public delegate bool BaselineFn(LeaderboardBoard board, out long best);

    // Seams for tests; the defaults read the real run and the real stored bests.
    public Func<bool> Eligible = DefaultEligible;
    public BaselineFn Baseline = StoredBest;
    public Func<LeaderboardRunStats> Live = DefaultLive;

    // Seam for tests: true = the shared board is live (compare against the player's own shared row).
    public Func<bool> SharedMode = NewBestCheck.DefaultSharedMode;
    public Func<SharedTable, SharedColumn, long> SharedBest = NewBestCheck.Baseline;

    struct Entry { public LeaderboardBoard board; public long best; public bool crossed; public bool power; }
    readonly List<Entry> entries = new List<Entry>();
    readonly List<string> fresh = new List<string>();

    public int Watching { get { return entries.Count; } }

    public bool Crossed(string boardId)
    {
        foreach (var e in entries) if (e.board.id == boardId && e.crossed) return true;
        return false;
    }

    // Starts watching a run: every enabled board that already has a stored best.
    public void Begin()
    {
        entries.Clear();
        if (!Eligible()) return;
        if (SharedMode())
        {
            // Sharing on: every column against the player's own shared row. No row yet = a first run is a new best.
            AddShared(SharedTable.Main, LeaderboardBoards.Get(LeaderboardBoards.TopScore));
            AddShared(SharedTable.Power, LeaderboardBoards.Get(LeaderboardBoards.BoostedRuns));
            return;
        }
        foreach (var board in LeaderboardBoards.Enabled())
        {
            long best;
            if (Baseline(board, out best)) entries.Add(new Entry { board = board, best = best, power = board.poweredUpOnly });
        }
    }

    void AddShared(SharedTable table, LeaderboardBoard scoreBoard)
    {
        var boards = new[] { scoreBoard, LeaderboardBoards.DustColumn, LeaderboardBoards.WorldsColumn };
        for (int i = 0; i < boards.Length; i++)
        {
            if (boards[i] == null) continue;
            entries.Add(new Entry { board = boards[i], best = SharedBest(table, SharedDoc.Columns[i]), power = table == SharedTable.Power });
        }
    }

    // The ids of boards the run crossed since the last call (reused list: read it before the next call).
    public List<string> Poll()
    {
        fresh.Clear();
        if (entries.Count == 0 || !Eligible()) return fresh;
        var run = Live();
        for (int i = 0; i < entries.Count; i++)
        {
            var e = entries[i];
            // Routing: a run that used a booster only chases the Power-Ups board, any other run only the normal boards.
            if (e.power != run.PoweredUp) continue;
            var measured = run;
            if (e.power) measured.starDust = run.dustEarned;   // a Power-Ups row keeps everything the run earned
            long value = e.board.Measure(measured);
            if (e.crossed || !e.board.IsBetter(value, e.best)) continue;
            e.crossed = true;
            entries[i] = e;
            fresh.Add(e.board.id);
        }
        return fresh;
    }

    static bool DefaultEligible()
    {
        return RunScore.Scoring && RunScore.SavesBest && !RunScore.Unranked && !RunScore.Ended;
    }

    public static LeaderboardRunStats DefaultLive()
    {
        return new LeaderboardRunStats { score = RunScore.Total, starDust = Boosters.RankedDust(StarDustLedger.Earned), worldsCleared = RunScore.Parts.worldCount, dustEarned = StarDustLedger.Earned,
            powerUps = (Boosters.RunDustUsed ? LeaderboardRunTag.PowerDust : 0) | (HazardClear.UsedThisRun > 0 ? LeaderboardRunTag.PowerClear : 0) |
                       (Boosters.RunHomingUsed ? LeaderboardRunTag.PowerHoming : 0) };
    }

    public static bool StoredBest(LeaderboardBoard board, out long best)
    {
        best = 0;
        if (board.id == LeaderboardBoards.TopScore)
        {
            best = RunScore.BestAtStart;
            return best > 0;
        }
        bool any = false;
        foreach (string key in new[] { LeaderboardService.SubmittedKey, LeaderboardService.PendingKey })
        {
            long v;
            if (!LeaderboardService.ScoreTable.Load(key).TryGet(board.id, out v)) continue;
            if (!any || board.IsBetter(v, best)) best = v;
            any = true;
        }
        return any;
    }
}

// The short "NEW HIGH SCORE!" popup, one wording per leaderboard. Text only (ink outline + a soft glow, the HUD's
// own look), no box, no sound. It drops in under the HUD band where the codex toast does (clear of the play
// area's centre), pops, holds and fades in about 2 s, never blocks input and never touches the game clock.
// Boards that fire together queue; it waits while a codex toast is up rather than cover it.
public class NewRecordPopup : MonoBehaviour
{
    public const string Generic = "NEW RECORD REACHED!";
    public const float Width = 520f, Height = 56f;
    public const float InDuration = .25f, HoldDuration = 1.3f, OutDuration = .4f;
    public const int FontSize = 30;
    const int QueueSize = 6;

    // The wording per board (the table in LeaderboardBoards); anything else gets the generic line.
    public static string TextFor(string boardId)
    {
        switch (boardId)
        {
            case LeaderboardBoards.TopScore: return "NEW HIGH SCORE!";
            case LeaderboardBoards.RunStarDust: return "NEW STAR DUST RECORD!";
            case LeaderboardBoards.FurthestWorld: return "NEW WORLDS RECORD!";
            case LeaderboardBoards.BoostedRuns: return "NEW BOOSTED RECORD!";
            default: return Generic;
        }
    }

    // Pink is Space's colour: gold for the score, cyan and teal for the rest.
    public static Color ColourFor(string boardId)
    {
        switch (boardId)
        {
            case LeaderboardBoards.RunStarDust: return AkiraPalette.Cyan;
            case LeaderboardBoards.FurthestWorld: return AkiraPalette.Teal;
            default: return AkiraPalette.Amber;
        }
    }

    static NewRecordPopup instance;
    public static NewRecordPopup Current { get { return instance; } }

    Canvas canvas;
    RectTransform box;
    CanvasGroup group;
    Text label;
    Outline glow;
    readonly string[] queue = new string[QueueSize];
    int queueHead, queueCount;
    float shownAt = -1f;
    string showingId;
    // The unscaled clock; tests swap it.
    public Func<float> Clock = () => Time.unscaledTime;
    BossWarningHud warning;

    public bool Showing { get { return shownAt >= 0f; } }
    public string ShowingText { get { return label != null ? label.text : null; } }
    public string ShowingBoard { get { return showingId; } }
    public int Queued { get { return queueCount; } }
    public RectTransform Box { get { return box; } }
    public Text Label { get { return label; } }

    public static NewRecordPopup Build()
    {
        var canvas = CodexUi.NewOverlayCanvas("~NewRecordPopup", 639, false, 0f, CodexUi.TextUnits);
        var popup = canvas.gameObject.AddComponent<NewRecordPopup>();
        popup.canvas = canvas;
        popup.BuildUi(CodexUi.FindFont());
        popup.gameObject.SetActive(false);
        return popup;
    }

    void BuildUi(Font font)
    {
        var go = new GameObject("Popup", typeof(RectTransform), typeof(CanvasGroup));
        go.transform.SetParent(transform, false);
        box = (RectTransform)go.transform;
        box.anchorMin = box.anchorMax = new Vector2(.5f, 1f);
        box.pivot = new Vector2(.5f, 1f);
        box.sizeDelta = new Vector2(Width, Height);
        group = go.GetComponent<CanvasGroup>();
        group.interactable = false;
        group.blocksRaycasts = false;

        label = CodexUi.NewText("Label", box, font, "", FontSize, AkiraPalette.Amber, TextAnchor.MiddleCenter);
        label.horizontalOverflow = HorizontalWrapMode.Wrap;
        label.verticalOverflow = VerticalWrapMode.Truncate;
        label.resizeTextForBestFit = true;
        label.resizeTextMinSize = 18;
        label.resizeTextMaxSize = FontSize;
        CodexUi.Stretch(label.rectTransform);
        // ink outline for contrast, then a soft coloured glow round it (no border, no box)
        CodexUi.AddOutline(label.gameObject, AkiraPalette.WithAlpha(AkiraPalette.Ink, .95f), 2f);
        glow = label.gameObject.AddComponent<Outline>();
        glow.effectDistance = new Vector2(ScoreHud.GlowDistance, -ScoreHud.GlowDistance);
        glow.useGraphicAlpha = true;
    }

    // Queues the popup for a board (the live HUD calls this; any scene without a run ignores it).
    public static void Show(string boardId)
    {
        if (instance == null) instance = Build();
        instance.Push(boardId);
    }

    public void Push(string boardId)
    {
        if (queueCount >= QueueSize) return;
        queue[(queueHead + queueCount) % QueueSize] = boardId;
        queueCount++;
        if (!gameObject.activeSelf) gameObject.SetActive(true);
        if (shownAt < 0f) Next();
    }

    void Next()
    {
        if (queueCount == 0)
        {
            shownAt = -1f;
            showingId = null;
            gameObject.SetActive(false);
            return;
        }
        showingId = queue[queueHead];
        queue[queueHead] = null;
        queueHead = (queueHead + 1) % QueueSize;
        queueCount--;
        label.text = TextFor(showingId);
        var c = ColourFor(showingId);
        label.color = c;
        glow.effectColor = AkiraPalette.WithAlpha(c, .35f);
        shownAt = Clock();
        ApplyAt(0f);
    }

    void Update() { Tick(Clock()); }

    public void Tick(float now)
    {
        if (shownAt < 0f) return;
        // a codex toast shares this slot: it goes first
        var toast = CodexToast.Current;
        if (toast != null && toast.Showing && toast.gameObject.activeInHierarchy)
        {
            shownAt = now;
            group.alpha = 0f;
            return;
        }
        float t = now - shownAt;
        if (t >= TotalSeconds) { Next(); return; }
        ApplyAt(t);
    }

    public const float TotalSeconds = InDuration + HoldDuration + OutDuration;

    // The frame at `t` seconds: stepped pop (big, squash, settled; 12 drawings a second like the HUD), fade out at the end.
    public void ApplyAt(float t)
    {
        float a = t < InDuration ? CodexUi.EaseOutCubic(t / InDuration)
                : t < InDuration + HoldDuration ? 1f
                : 1f - Mathf.Clamp01((t - InDuration - HoldDuration) / OutDuration);
        group.alpha = a;
        float tick = Mathf.Floor(t * 12f) / 12f;
        float s = tick < 1f / 12f ? 1.35f : tick < 2f / 12f ? .92f : 1f;
        if (!Mathf.Approximately(box.localScale.x, s)) box.localScale = new Vector3(s, s, 1f);
        box.anchoredPosition = new Vector2(0f, -Top());
    }

    // Canvas units below the screen's top: where the codex toast sits (under the safe area and the HUD
    // band, dodging the boss warning and the portal chip), so it is never over the middle of the play area.
    float Top()
    {
        float sf = Mathf.Max(canvas.scaleFactor, .0001f);
        float safeTop = (ScreenInfo.Height - ScreenInfo.SafeArea.yMax) / sf;
        if (warning == null) warning = FindFirstObjectByType<BossWarningHud>();
        var portal = PortalPressureHud.Instance;
        float top = CodexToast.TopOffset(safeTop, ScreenInfo.Height, sf,
            warning != null && warning.BannerVisible ? warning.CurrentLayout.banner : default(Rect),
            warning != null && warning.ChipVisible ? warning.CurrentLayout.chip : default(Rect),
            portal != null && portal.Showing ? portal.ChipRect : default(Rect));
        return CodexToast.UnderCard(top, ScreenInfo.Height, sf, WorldBanner.ScreenRect);
    }

    void OnDestroy() { if (instance == this) instance = null; }
}
