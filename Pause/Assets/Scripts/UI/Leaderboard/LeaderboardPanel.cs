using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// The in-game leaderboard, opened from the home screen (startS4) -> Leaderboard.
//
// Built entirely at runtime on its own overlay canvas, so the scene only
// keeps its existing button. TWO tabs: MAIN (non-boosted runs; one table with three sortable column headers, SCORE /
// DUST / WORLDS) and POWER-UPS (the Boosted board). Then the top 10, the player's own rank row, and "View all" for the
// store's native screen.
//
// Sorting a column loads THAT column's store board (Top Score / Star Dust / Worlds) and shows its real ranks; rows
// are never re-sorted on the client (a page is only the top N of one stat). Each row shows its three numbers from the
// one run (carried in the score tag); the sorted column shows the board's own value, a number the row has no tag
// for shows a dash. A header whose board is not enabled on the platform is not offered.
//
// States: signed out (one-tap sign-in), loading, empty, error/offline,
// unavailable (no store on this device), no boards configured, populated.
//
// Shared mode (OpenHome / Open with a SharedLeaderboardService): the same panel reads the cross-platform Firebase
// board instead. MAIN tab (SCORE / DUST / WORLDS headers, each loads that sort from SharedLeaderboardService, never
// re-sorted here) and POWER-UPS tab; the player's own row shows even off the page (rank 0 = no number); each row carries
// a small robot (Android) or apple (iOS) mark by the name. A failed shared load shows the error state with a
// "show Play Games / Game Center board" action that switches this panel to the native boards.
//
// Look: docs/art-style.md -- flat cel plates with thick INK contours and one
// hard offset shadow, chamfered corners, the red title slab, Orbitron type
// with an ink stroke. Kaneda red marks the player's own row.
public class LeaderboardPanel : MonoBehaviour
{
    public enum State { SignedOut, Loading, Empty, Error, Unavailable, NoBoards, Populated }

    // ---- layout, in panel units (origin: panel centre, y up) ----

    public const float DesignWidth = 720f, DesignHeight = 1180f;
    // Share of the safe area the panel may take at most.
    public const float FillWidth = .94f, FillHeight = .95f;

    const float SideInset = (DesignWidth - 640f) * .5f;   // frame -> content, each side
    const float RowHeight = 52f, RowStep = 58f;   // 10 rows exactly fill the list at the design height
    const float ListTop = 316f;               // from the panel top (below the sort headers)
    const float PlayerRowTop = 920f, PlayerRowHeight = 66f;
    const float ButtonsTop = 1036f, ButtonHeight = 100f;
    internal const float ButtonsTopUnits = ButtonsTop;
    public const float TabTop = 144f, TabHeight = 80f, TabTouchMargin = 10f;
    // The main tab's sort headers: 48 units of art with 26 of touch margin above and below = a 100-unit target; each
    // is as wide as its column (100), and the three columns replace the old hearts / pauses / score block.
    public const float HeaderTop = 262f, HeaderHeight = 48f, HeaderTouchMargin = 26f;
    public const float ColWidth = 100f;
    public const string MainTab = "main";
    public const string Dash = "-";
    // Player names: NameSize, shrinking to NameMinSize at the least for a
    // long one, then cut short with an ellipsis -- never smaller. The rank
    // and score columns keep their own boxes, so they stay aligned and whole.
    public const int NameSize = 24, NameMinSize = 20;
    // Row columns, left to right: rank, the ship the run started with (its hull
    // art), the name, the hearts it started with (the HUD heart + a count), the
    // pauses it ended with (two bars + a count), the score. Widths in panel units:
    // at the narrowest panel (a 320 dp phone, 626.7 units wide, Inner 546.7) the
    // name column is 546.7 - 130 - 14 - 170 - 6 - 2 x 66 = 94.7.
    public const float NameLeft = 130f, NameWidth = 210f;   // NameWidth: at the design width
    const float RankWidth = 62f, ShipLeft = 78f, ShipWidth = 46f;
    // The run line under the name (MAIN and Power-Ups, shared and native): the hearts the run started with (the HUD heart + a
    // count) and the pauses it ended with (two bars + a count), as two fixed slots so they line up row to row. They sit in
    // the name column's lower band, so they take no width from the numbers or the name (at a 320 dp phone the name column is
    // 96.7 units, the two slots are 94). A third and fourth column beside the name would leave it about 5 units.
    public const float RunLineHeartsLeft = 0f, RunLineHeartsWidth = 46f, RunLinePausesLeft = 48f, RunLinePausesWidth = 46f;
    const float RunLineGlyph = 16f;
    const int RunLineSize = 18;
    const float NameBand = .42f;   // the name takes the top .58 of a stats row, the run line the lower .42
    const float BadgeWidth = 66f;   // the old hearts / pauses badge width; the Power-Ups name column is still derived from it
    const float ValueWidth = 170f, ValueInset = 14f;
    const int ValueMinSize = 16;
    // Tab captions shrink (or wrap onto a second line) to this at the least: four tabs share the width.
    public const int TabMinSize = 16;
    // The main tab's cells: the sorted column's value, and the other two (a little smaller), each shrinking to ValueMinSize.
    const int SortedCellSize = 26, OtherCellSize = 22;
    // The Boosted board's row: no hearts / pauses badges; their room holds a two-line star dust + worlds block,
    // and under the score the power-ups the run used (all at 16 units or more).
    const float StatsWidth = 150f;
    // The Boosted tab narrows the score column (170 to 150) to make room for the stats block.
    const float BoostedValueWidth = 150f;
    const int StatsSize = 18;
    // Text is fitted this far inside its box: the glyphs are laid out at pixel size on a device, a little wider than measured here.
    const float FitMargin = 12f;
    // UiScale's floor: the buttons and the tab are 100-unit touch targets.
    public const float MinTapUnits = 100f;
    public const string Ellipsis = "...";

    public struct Layout
    {
        public float scale;          // canvas units -> screen pixels
        public Rect safe;            // pixels
        public Rect panelPixels;     // pixels
        public Vector2 panelSize;    // panel units: the design size, or less on a small screen
    }

    // The whole design panel fitted into the safe area -- unless that would
    // draw it below UiScale's floor (a small phone): then it is drawn at the
    // floor, and gets fewer units: narrower (the name column gives way, names
    // cut sooner) and shorter (the top-10 list scrolls).
    public static Layout ComputeLayout(Vector2 screen, Rect safe)
    {
        if (safe.width <= 0f || safe.height <= 0f) safe = new Rect(0f, 0f, screen.x, screen.y);
        float fit = Mathf.Min(safe.width * FillWidth / DesignWidth, safe.height * FillHeight / DesignHeight);
        float scale = UiScale.Apply(fit, MinTapUnits, 0f);
        var units = new Vector2(Mathf.Min(DesignWidth, safe.width * FillWidth / scale),
                                Mathf.Min(DesignHeight, safe.height * FillHeight / scale));
        var size = units * scale;
        return new Layout
        {
            scale = scale,
            safe = safe,
            panelPixels = new Rect(safe.center - size * .5f, size),
            panelSize = units,
        };
    }

    // ---- this panel's size (panel units) and what follows from it ----
    float panelW = DesignWidth, panelH = DesignHeight;
    float Inner { get { return panelW - 2f * SideInset; } }
    float Drop { get { return DesignHeight - panelH; } }   // the lower block moves up by this
    float ListHeight { get { return PlayerRowTop - Drop - 30f - ListTop; } }
    public float BoostedNameWidth { get { return NameColumnWidth + 2f * BadgeWidth + (ValueWidth - BoostedValueWidth) - StatsWidth; } }
    // The main tab's name column: what the (offered) number columns leave.
    public float MainNameWidth { get { return Inner - NameLeft - ValueInset - 6f - ColWidth * mainColumns.Count; } }
    // The old hearts / pauses / score layout's name column; the Power-Ups tab's width is derived from it.
    public float NameColumnWidth { get { return Inner - NameLeft - ValueInset - ValueWidth - 6f - 2f * BadgeWidth; } }
    public Vector2 PanelSize { get { return new Vector2(panelW, panelH); } }
    public bool ListScrolls { get { return list != null && list.GetComponent<ScrollRect>().enabled; } }

    // ---- public surface (tests read these) ----

    public static LeaderboardPanel Current { get; private set; }

    public State CurrentState { get; private set; }
    public string SelectedBoard { get; private set; }
    public Layout AppliedLayout { get; private set; }
    public readonly List<Button> Tabs = new List<Button>();
    // The tab keys: MainTab, then the Power-Ups board's id.
    public readonly List<string> TabBoards = new List<string>();
    // The sort headers, left to right, and the store boards they load.
    public readonly List<Button> Headers = new List<Button>();
    public readonly List<string> HeaderBoards = new List<string>();
    public string SelectedTab { get; private set; }
    public Button ViewAllButton { get; private set; }
    public Button CloseButton { get; private set; }
    public Button ActionButton { get; private set; }   // SIGN IN / RETRY, when shown
    readonly List<Image> shipImages = new List<Image>();
    readonly List<int> shipIds = new List<int>();
    readonly List<int> shipSkins = new List<int>();
    int shipDrawing = -1;
    // The offered number columns of the main tab (usable, not Power-Ups), in table order; and the sort kept across tab switches.
    readonly List<LeaderboardBoard> mainColumns = new List<LeaderboardBoard>();
    string mainSort;
    public int RowCount { get; private set; }
    public bool PlayerRowShown { get; private set; }
    public string PlayerRankText { get; private set; }
    public string UnrankedNote { get; private set; }   // the note under an unranked own row, else null
    public string MessageText { get; private set; }

    public RectTransform SafeRoot { get { return safeRoot; } }
    public RectTransform PanelRoot { get { return panelRoot; } }

    LeaderboardService service;
    SharedLeaderboardService shared;   // non-null: the panel shows the shared board
    bool forceReload;                  // the next shared load bypasses the cache (RETRY)
    public bool SharedMode { get { return shared != null; } }
    public Button NativeButton { get; private set; }   // the error state's "show the native board" action, shared mode
    public string AgeNote { get; private set; }
    Font font;
    Canvas canvas;
    CanvasScaler scaler;
    RectTransform safeRoot, panelRoot, body, list, rows;
    Text descriptionText;
    GameObject playerRow;
    int request;
    bool screenOverridden;
    Vector2 screenSize;
    Rect safeArea;
    readonly List<CelShape> loadingCells = new List<CelShape>();

    // ---- open / close ----

    public static LeaderboardPanel Open(LeaderboardService service = null, Font font = null)
    {
        return Open(service, font, new Vector2(ScreenInfo.Width, ScreenInfo.Height), ScreenInfo.SafeArea, false);
    }

    // The home Leaderboard button: the shared board when sharing is on and the shared backend is usable, else the
    // native Play Games / Game Center panel exactly as before.
    public static LeaderboardPanel OpenHome()
    {
        return OpenHome(new Vector2(ScreenInfo.Width, ScreenInfo.Height), ScreenInfo.SafeArea, false);
    }

    // Tests pass an explicit screen and safe area.
    public static LeaderboardPanel OpenHome(Vector2 screen, Rect safe, bool overrideScreen)
    {
        var sharedService = SharedLeaderboardService.Instance;
        bool useShared = sharedService != null && sharedService.BoardUsable;
        return Open(null, null, screen, safe, overrideScreen, useShared ? sharedService : null);
    }

    // Tests pass an explicit screen and safe area.
    public static LeaderboardPanel Open(LeaderboardService service, Font font, Vector2 screen, Rect safe,
                                       bool overrideScreen = true, SharedLeaderboardService sharedBoard = null)
    {
        Close();
        var go = new GameObject("~LeaderboardPanel", typeof(RectTransform));
        var panel = go.AddComponent<LeaderboardPanel>();
        panel.shared = sharedBoard;
        panel.service = service ?? LeaderboardService.Instance;
        panel.font = font != null ? font : DockArt.FindSceneFont();
        panel.screenOverridden = overrideScreen;
        panel.screenSize = screen;
        panel.safeArea = safe;
        panel.Build();
        Current = panel;
        // Back closes the panel before the screen's own back is reached.
        BackNavigator.Register(panel, panel.OnBackPressed);
        panel.SelectFirstBoard();
        // A queued score goes out now (not after its backoff); the board
        // reloads once the store confirms it.
        panel.service.Confirmed += panel.OnConfirmed;
        panel.service.FlushNow();
        if (panel.shared != null)
        {
            panel.shared.Uploaded += panel.OnSharedUploaded;
            panel.shared.FlushNow();
        }
        return panel;
    }

    public static bool IsOpen { get { return Current != null; } }

    public static void Close()
    {
        if (Current == null) return;
        var go = Current.gameObject;
        BackNavigator.Unregister(Current);
        Current = null;
        if (Application.isPlaying) Destroy(go);
        else DestroyImmediate(go);
    }

    // Android back / Escape: closes the panel instead of leaving the screen
    // (the open panel is a BackNavigator layer; see Open).
    public static bool CloseIfOpen()
    {
        if (!IsOpen) return false;
        Close();
        return true;
    }

    bool OnBackPressed()
    {
        if (Current != this) return false;
        Close();
        return true;
    }

    void OnConfirmed(string boardId)
    {
        if (boardId == SelectedBoard) Reload();
    }

    void OnSharedUploaded(SharedTable table)
    {
        if (shared != null && SelectedBoard != null && TableOf(SelectedBoard) == table) Reload();
    }

    void OnDestroy()
    {
        if (service != null) service.Confirmed -= OnConfirmed;
        if (shared != null) shared.Uploaded -= OnSharedUploaded;
        BackNavigator.Unregister(this);
        if (Current == this) Current = null;
    }

    // ---- build ----

    void Build()
    {
        canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 500;
        scaler = gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
        gameObject.AddComponent<GraphicRaycaster>();

        // Full-screen dim that also swallows taps meant for the menu buttons behind it.
        var dim = Child(transform, "Dim");
        Stretch(dim);
        var dimImage = dim.gameObject.AddComponent<Image>();
        dimImage.color = AkiraPalette.WithAlpha(AkiraPalette.Night0, .88f);

        safeRoot = Child(transform, "SafeArea");
        panelRoot = Child(safeRoot, "Panel");
        panelRoot.anchorMin = panelRoot.anchorMax = panelRoot.pivot = new Vector2(.5f, .5f);
        ApplyLayout();
        BuildPanel();
    }

    // Everything inside the panel, for its current size.
    void BuildPanel()
    {
        panelRoot.sizeDelta = new Vector2(panelW, panelH);
        mainColumns.Clear();
        foreach (var b in PanelBoards()) if (!b.poweredUpOnly) mainColumns.Add(b);
        // Native mode ranks by score only (the Star Dust and Worlds store boards are retired): the DUST and WORLDS
        // columns still show each row's numbers from its tag, and their headers are there but disabled.
        // Canonical order: SCORE, DUST, WORLDS (a column a store board still backs keeps its own board).
        if (shared == null && IsMainColumn(LeaderboardBoards.TopScore))
        {
            var ordered = new List<LeaderboardBoard> { mainColumns.Find(c => c.id == LeaderboardBoards.TopScore) };
            foreach (var col in LeaderboardBoards.SharedOnlyColumns)
                ordered.Add(mainColumns.Find(c => c.id == col.id) ?? col);
            foreach (var c in mainColumns) if (!ordered.Exists(o => o.id == c.id)) ordered.Add(c);
            mainColumns.Clear();
            mainColumns.AddRange(ordered);
        }

        // Frame: chamfered night plate, thick ink, inner panel line.
        var frame = CelShape.Add(Fill(panelRoot, "Frame").gameObject, CelShape.Kind.Chamfer,
                                 AkiraPalette.WithAlpha(AkiraPalette.Night1, .97f), 5f);
        frame.cut = 30f;
        frame.raycastTarget = true;   // taps on the panel body don't fall through to the dim
        var line = CelShape.Add(Place(panelRoot, "PanelLine", 0f, 10f, panelW - 20f, panelH - 20f).gameObject,
                                CelShape.Kind.Chamfer, Color.clear, 1.5f);
        line.cut = 25f;
        line.hollow = true;
        line.ink = AkiraPalette.Hairline;

        BuildTitle();
        BuildTabs();
        BuildHeaders();

        var desc = Place(panelRoot, "Description", 0f, TabTop + TabHeight + 8f, Inner, 34f);
        descriptionText = Label(desc, "", 20, AkiraPalette.Muted, TextAnchor.MiddleLeft, 2f);

        body = Place(panelRoot, "Body", 0f, ListTop, Inner, ListHeight);

        // The top-10 rows: a clipped viewport that scrolls when the panel is
        // too short for all of them.
        list = Place(panelRoot, "List", 0f, ListTop, Inner, ListHeight);
        list.gameObject.AddComponent<RectMask2D>();
        var scroll = list.gameObject.AddComponent<ScrollRect>();
        scroll.horizontal = false;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.scrollSensitivity = 30f;
        var hit = list.gameObject.AddComponent<Image>();   // drags anywhere on the list scroll it
        hit.color = Color.clear;
        rows = Child(list, "Rows");
        rows.anchorMin = new Vector2(0f, 1f);
        rows.anchorMax = new Vector2(1f, 1f);
        rows.pivot = new Vector2(.5f, 1f);
        rows.offsetMin = rows.offsetMax = Vector2.zero;
        scroll.content = rows;
        scroll.viewport = list;
        scroll.enabled = false;

        playerRow = Place(panelRoot, "PlayerRow", 0f, PlayerRowTop - Drop, Inner, PlayerRowHeight).gameObject;

        CloseButton = MakeButton(panelRoot, "Back", -Inner * .25f - 8f, ButtonsTop - Drop, Inner * .5f - 16f, ButtonHeight,
                                 "BACK", AkiraPalette.Red, Close);
        ViewAllButton = MakeButton(panelRoot, "ViewAll", Inner * .25f + 8f, ButtonsTop - Drop, Inner * .5f - 16f, ButtonHeight,
                                   "VIEW ALL", AkiraPalette.Cyan, ViewAll);
    }

    // The boards this panel offers: the native ones the platform has ids for, or, in shared mode, all four (the shared
    // board has no per-platform ids).
    List<LeaderboardBoard> PanelBoards()
    {
        if (shared == null) return service.UsableBoards();
        var list = new List<LeaderboardBoard>();
        foreach (string id in SharedBoardIds)
        {
            var b = LeaderboardBoards.Get(id);
            if (b != null) list.Add(b);
        }
        return list;
    }

    public static readonly string[] SharedBoardIds =
    {
        LeaderboardBoards.TopScore, LeaderboardBoards.RunStarDust, LeaderboardBoards.FurthestWorld, LeaderboardBoards.BoostedRuns,
    };

    // Which shared table and sort column a board id stands for.
    public static SharedTable TableOf(string boardId) { return boardId == LeaderboardBoards.BoostedRuns ? SharedTable.Power : SharedTable.Main; }

    public static SharedColumn ColumnOf(string boardId)
    {
        return boardId == LeaderboardBoards.RunStarDust ? SharedColumn.Dust
             : boardId == LeaderboardBoards.FurthestWorld ? SharedColumn.Worlds : SharedColumn.Score;
    }

    void BuildTitle()
    {
        var slabRect = Place(panelRoot, "TitleSlab", -6f, 34f, Inner + 30f, 96f);
        var slab = CelShape.Add(slabRect.gameObject, CelShape.Kind.Slab, AkiraPalette.Red, 4f)
                           .Shadow(AkiraPalette.Ink, new Vector2(8f, -8f));
        slab.slant = 24f;
        // The one hard highlight kick along the slab's top edge.
        var kick = Child(slabRect, "Kick");
        kick.anchorMin = new Vector2(0f, 1f);
        kick.anchorMax = new Vector2(1f, 1f);
        kick.pivot = new Vector2(.5f, 1f);
        kick.offsetMin = new Vector2(24f, -18f);
        kick.offsetMax = new Vector2(-6f, -4f);
        var kickShape = CelShape.Add(kick.gameObject, CelShape.Kind.Slab, AkiraPalette.RedHi, 0f);
        kickShape.slant = 4f;
        var title = Child(slabRect, "Title");
        Stretch(title);
        Label(title, "LEADERBOARD", 52, AkiraPalette.Bone, TextAnchor.MiddleCenter, 3.5f);
    }

    void BuildTabs()
    {
        // Two tabs: MAIN (when any normal board is offered) and one per Power-Ups board.
        var tabs = new List<KeyValuePair<string, string>>();
        if (mainColumns.Count > 0) tabs.Add(new KeyValuePair<string, string>(MainTab, "MAIN"));
        foreach (var b in PanelBoards())
            if (b.poweredUpOnly) tabs.Add(new KeyValuePair<string, string>(b.id, b.displayName.ToUpperInvariant()));
        if (tabs.Count == 0) return;
        // 80 tall plus 10 of touch margin above and below: a 100-unit target
        // (>= 48 dp / 44 pt at UiScale's 0.5 dp-per-unit floor).
        const float gap = 14f, top = TabTop, height = TabHeight;
        float w = (Inner - gap * (tabs.Count - 1)) / tabs.Count;
        for (int i = 0; i < tabs.Count; i++)
        {
            string key = tabs[i].Key;
            float x = -Inner * .5f + w * .5f + i * (w + gap);
            var rt = Place(panelRoot, "Tab_" + key, x, top, w, height);
            var shape = CelShape.Add(rt.gameObject, CelShape.Kind.Chamfer, AkiraPalette.Card, 3.5f)
                                .Cuts(true, false, true, false);
            shape.cut = 14f;
            shape.raycastTarget = true;
            shape.raycastPadding = new Vector4(0f, -TabTouchMargin, 0f, -TabTouchMargin);   // a bigger finger target, same art
            var label = Child(rt, "Label");
            Stretch(label, 10f);
            Label(label, tabs[i].Value, 26, AkiraPalette.Muted, TextAnchor.MiddleCenter, 2.5f, TabMinSize);
            var button = rt.gameObject.AddComponent<Button>();
            button.targetGraphic = shape;
            button.transition = Selectable.Transition.None;
            button.onClick.AddListener(() => SelectTab(key));
            Tabs.Add(button);
            TabBoards.Add(key);
        }
    }

    // The main tab's sort headers, one per offered column, right-aligned over the number columns.
    void BuildHeaders()
    {
        int n = mainColumns.Count;
        if (n == 0) return;
        float right = Inner * .5f - ValueInset;
        for (int i = 0; i < n; i++)
        {
            var board = mainColumns[i];
            float x = right - (n - i - .5f) * ColWidth;
            var rt = Place(panelRoot, "Header_" + board.id, x, HeaderTop, ColWidth - 6f, HeaderHeight);
            var shape = CelShape.Add(rt.gameObject, CelShape.Kind.Chamfer, AkiraPalette.Card, 3f).Cuts(true, false, true, false);
            shape.cut = 10f;
            shape.raycastTarget = true;
            shape.raycastPadding = new Vector4(-3f, -HeaderTouchMargin, -3f, -HeaderTouchMargin);   // 100 tall, columns touch
            var label = Child(rt, "Label");
            Stretch(label, 6f);
            Label(label, HeaderLabel(board), 22, AkiraPalette.Muted, TextAnchor.MiddleCenter, 2f, TabMinSize);
            var button = rt.gameObject.AddComponent<Button>();
            button.targetGraphic = shape;
            button.transition = Selectable.Transition.None;
            string id = board.id;
            button.onClick.AddListener(() => SelectBoard(id));
            button.interactable = ColumnEnabled(id);   // a disabled header is greyed (SelectBoard) and ignores taps
            Headers.Add(button);
            HeaderBoards.Add(id);
        }
    }

    // A header can be sorted on: always in shared mode, and in native mode only when a store board backs it.
    bool ColumnEnabled(string boardId)
    {
        return shared != null || service.Usable(boardId) != null;
    }

    // The short note shown under the headers in native mode while the disabled DUST / WORLDS headers are there.
    public const string NativeSortNote = "SORT DUST / WORLDS ON THE SHARED BOARD";

    public static string HeaderLabel(LeaderboardBoard board)
    {
        switch (board.id)
        {
            case LeaderboardBoards.TopScore: return "SCORE";
            case LeaderboardBoards.RunStarDust: return "DUST";
            case LeaderboardBoards.FurthestWorld: return "WORLDS";
            default: return board.displayName.ToUpperInvariant();
        }
    }

    // ---- behaviour ----

    void SelectFirstBoard()
    {
        if (TabBoards.Count == 0)
        {
            SelectedBoard = null;
            SelectedTab = null;
            Show(State.NoBoards);
            return;
        }
        SelectTab(TabBoards[0]);
    }

    // A tab: MAIN opens the column last sorted on (SCORE the first time), a Power-Ups key its own board.
    public void SelectTab(string key)
    {
        if (key == MainTab) SelectBoard(mainSort != null && IsMainColumn(mainSort) ? mainSort : mainColumns[0].id);
        else SelectBoard(key);
    }

    bool IsMainColumn(string boardId)
    {
        foreach (var b in mainColumns) if (b.id == boardId) return true;
        return false;
    }

    bool AnyDisabledHeader()
    {
        foreach (var h in Headers) if (!h.interactable) return true;
        return false;
    }

    // Loads one STORE board: a main-tab column (the header) or the Power-Ups board. Always that board's own ranks.
    public void SelectBoard(string boardId)
    {
        if (boardId != null && LeaderboardBoards.IsRetiredNative(boardId) && !ColumnEnabled(boardId)) return;   // a retired native column: no sort
        SelectedBoard = boardId;
        var board = LeaderboardBoards.Get(boardId);
        bool main = board == null || !board.poweredUpOnly;
        if (main) mainSort = boardId;
        SelectedTab = main ? MainTab : boardId;
        for (int i = 0; i < Tabs.Count; i++)
        {
            bool on = TabBoards[i] == SelectedTab;
            var shape = Tabs[i].GetComponent<CelShape>();
            shape.SetFill(on ? AkiraPalette.Red : AkiraPalette.Card);
            shape.hasShadow = on;
            shape.shadow = AkiraPalette.Ink;
            shape.shadowOffset = new Vector2(5f, -5f);
            var text = Tabs[i].GetComponentInChildren<Text>();
            text.color = on ? AkiraPalette.Bone : AkiraPalette.Muted;
        }
        // The active header is marked: red plate, ink shadow, bone type.
        for (int i = 0; i < Headers.Count; i++)
        {
            bool on = main && HeaderBoards[i] == boardId;
            Headers[i].gameObject.SetActive(main);
            var shape = Headers[i].GetComponent<CelShape>();
            shape.SetFill(on ? AkiraPalette.Red : AkiraPalette.Card);
            shape.hasShadow = on;
            shape.shadow = AkiraPalette.Ink;
            shape.shadowOffset = new Vector2(4f, -4f);
            Headers[i].GetComponentInChildren<Text>().color = !Headers[i].interactable ? AkiraPalette.WithAlpha(AkiraPalette.Muted, .4f)
                : on ? AkiraPalette.Bone : AkiraPalette.Muted;
        }
        if (descriptionText != null)
        {
            bool note = main && shared == null && AnyDisabledHeader();
            descriptionText.text = !main && board != null ? board.description.ToUpperInvariant() : note ? NativeSortNote : "";
            FitText(descriptionText, 20, ValueMinSize, Inner - FitMargin);
        }
        Reload();
    }

    // The header's board is the active sort (main tab only); null otherwise.
    public string SortedBoard { get { return SelectedTab == MainTab ? SelectedBoard : null; } }

    public void Reload()
    {
        if (SelectedBoard == null) { Show(State.NoBoards); return; }
        if (shared != null) { ReloadShared(); return; }
        var platform = service.Platform;
        if (!platform.IsAvailable) { Show(State.Unavailable); return; }
        if (!service.SignedIn) { Show(State.SignedOut); return; }

        int token = ++request;
        string boardId = SelectedBoard;
        Show(State.Loading);
        service.LoadBoard(boardId, LeaderboardService.PanelRows, (top, me) =>
        {
            if (this == null || token != request) return;   // closed, or another tab since
            OnLoaded(boardId, top, me);
        });
    }

    // Shared mode: one load of (table, sort column) from the shared service, which owns the cache. RETRY bypasses it
    // (at most once a minute per board, the service's rule). Rows are shown in the order the service returns them.
    void ReloadShared()
    {
        int token = ++request;
        string boardId = SelectedBoard;
        bool force = forceReload;
        forceReload = false;
        Show(State.Loading);
        shared.LoadBoard(TableOf(boardId), ColumnOf(boardId), force, loaded =>
        {
            if (this == null || token != request) return;
            var page = loaded != null ? loaded.page : null;
            if (page == null || !page.Ok)
            {
                Show(State.Error);   // offline, denied, quota, signed out of the shared board: native board offered
                return;
            }
            AgeNote = loaded.fromCache && loaded.ageSeconds >= 60 ? AgeText(loaded.ageSeconds) : null;
            var board = LeaderboardBoards.Get(boardId);
            if (descriptionText != null)
            {
                string desc = board != null && board.poweredUpOnly ? board.description.ToUpperInvariant() : "";
                descriptionText.text = AgeNote ?? desc;
            }
            bool hasPlayer = page.hasPlayer;
            if (page.entries.Count == 0 && !hasPlayer) { Show(State.Empty); return; }
            Show(State.Populated, board, page, hasPlayer ? page.player : (LeaderboardEntry?)null);
        });
    }

    public static string AgeText(long seconds)
    {
        long minutes = seconds / 60;
        return minutes < 60 ? "UPDATED " + System.Math.Max(1, minutes) + " MIN AGO" : "UPDATED " + (minutes / 60) + " H AGO";
    }

    // Shared mode, error state: this panel becomes the native Play Games / Game Center board.
    public void SwitchToNative()
    {
        if (shared == null) return;
        shared.Uploaded -= OnSharedUploaded;
        shared = null;
        AgeNote = null;
        Rebuild();
    }

    void OnLoaded(string boardId, LeaderboardPage top, LeaderboardPage me)
    {
        if (top == null || !top.Ok)
        {
            var status = top != null ? top.status : LeaderboardStatus.Error;
            Show(status == LeaderboardStatus.NotSignedIn ? State.SignedOut
               : status == LeaderboardStatus.Unavailable ? State.Unavailable : State.Error);
            return;
        }
        bool hasPlayer = me != null && me.Ok && me.hasPlayer;
        if (top.entries.Count == 0 && !hasPlayer) { Show(State.Empty); return; }
        Show(State.Populated, LeaderboardBoards.Get(boardId), top, hasPlayer ? me.player : (LeaderboardEntry?)null);
    }

    void SignIn()
    {
        Show(State.Loading);
        service.SignIn(ok =>
        {
            if (this == null) return;
            if (ok) Reload();
            else Show(State.SignedOut);
        });
    }

    void RetryShared()
    {
        forceReload = true;
        Reload();
    }

    void ViewAll()
    {
        service.ShowNativeUI(SelectedBoard);
    }

    // The list catches drags (and blocks taps under it) only while it scrolls.
    void SetScrolling(bool on)
    {
        list.GetComponent<ScrollRect>().enabled = on;
        list.GetComponent<Image>().raycastTarget = on;
    }

    // ---- states ----

    void Show(State state, LeaderboardBoard board = null, LeaderboardPage top = null, LeaderboardEntry? player = null)
    {
        CurrentState = state;
        Clear(body);
        Clear(rows);
        rows.sizeDelta = new Vector2(0f, 0f);
        rows.anchoredPosition = Vector2.zero;
        SetScrolling(false);
        Clear(playerRow.transform);
        loadingCells.Clear();
        ActionButton = null;
        RowCount = 0;
        PlayerRowShown = false;
        PlayerRankText = null;
        UnrankedNote = null;
        MessageText = null;

        string platformName = service.Platform.PlatformName;
        NativeButton = null;
        AgeNote = state == State.Populated ? AgeNote : null;
        switch (state)
        {
            case State.SignedOut:
                Message("SIGN IN TO SEE RANKINGS", "Sign in with " + StoreName(platformName) + " to post your runs.");
                ActionButton = MakeButton(body, "SignIn", 0f, 330f, 420f, ButtonHeight, "SIGN IN", AkiraPalette.Red, SignIn, true);
                break;
            case State.Loading:
                Message("LOADING RANKINGS", "");
                BuildLoadingBar();
                break;
            case State.Empty:
                Message("NO SCORES YET", "Finish a run to set the first one.");
                break;
            case State.Error:
                Message("CAN'T REACH THE RANKINGS", "You may be offline. Your best runs are kept and sent later.");
                if (shared == null)
                {
                    ActionButton = MakeButton(body, "Retry", 0f, 330f, 420f, ButtonHeight, "RETRY", AkiraPalette.Red, Reload, true);
                    break;
                }
                // Shared board failed: RETRY, and the native board as the way out (never a dead end).
                if (service.Platform.IsAvailable)
                {
                    float half = (Inner - 16f) * .5f;
                    ActionButton = MakeButton(body, "Retry", -half * .5f - 8f, 330f, half, ButtonHeight, "RETRY", AkiraPalette.Red, RetryShared, true);
                    NativeButton = MakeButton(body, "ShowNative", half * .5f + 8f, 330f, half, ButtonHeight,
                                              "SHOW " + StoreName(platformName).ToUpperInvariant() + " BOARD", AkiraPalette.Cyan, SwitchToNative);
                }
                else ActionButton = MakeButton(body, "Retry", 0f, 330f, 420f, ButtonHeight, "RETRY", AkiraPalette.Red, RetryShared, true);
                break;
            case State.Unavailable:
                Message("RANKINGS UNAVAILABLE", "Leaderboards need Google Play Games or Game Center.");
                break;
            case State.NoBoards:
                Message("NO LEADERBOARDS YET", "Rankings open once the boards are set up.");
                break;
            case State.Populated:
                BuildRows(board, top, player);
                break;
        }

        bool storeReachable = (state == State.Populated || state == State.Empty || state == State.Error)
                              && (shared == null || service.Platform.IsAvailable);
        ViewAllButton.gameObject.SetActive(storeReachable);
        // Alone, BACK sits in the middle.
        var back = (RectTransform)CloseButton.transform;
        back.anchoredPosition = new Vector2(storeReachable ? -Inner * .25f - 8f : 0f, back.anchoredPosition.y);
    }

    static string StoreName(string platformName)
    {
        return platformName == "none" || string.IsNullOrEmpty(platformName) ? "your game account" : platformName;
    }

    // Shown under an own row that has a score but no public rank.
    public static string UnrankedMessage(string platformName)
    {
        return platformName == "Game Center"
            ? "Your score is saved, but Game Center has not ranked it yet. It will appear once it does."
            : "Your profile is private, so you won't appear on the public leaderboard. Make it public in the Play Games app to be ranked.";
    }

    void Message(string title, string sub)
    {
        MessageText = title;
        var t = Place(body, "MessageTitle", 0f, 130f, Inner, 60f);
        Label(t, title, 36, AkiraPalette.Bone, TextAnchor.MiddleCenter, 3f);
        if (!string.IsNullOrEmpty(sub))
        {
            var s = Place(body, "MessageSub", 0f, 200f, Inner - 40f, 90f);
            var text = Label(s, sub, 24, AkiraPalette.Muted, TextAnchor.UpperCenter, 2f);
            text.fontStyle = FontStyle.Bold;
            text.resizeTextForBestFit = false;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
        }
    }

    // Hard segmented cells lit one after another on 2s (art-style section 3).
    void BuildLoadingBar()
    {
        const int cells = 8;
        const float w = 40f, gap = 10f;
        float total = cells * w + (cells - 1) * gap;
        for (int i = 0; i < cells; i++)
        {
            var rt = Place(body, "Cell" + i, -total * .5f + w * .5f + i * (w + gap), 230f, w, 34f);
            var cell = CelShape.Add(rt.gameObject, CelShape.Kind.Slab, AkiraPalette.Indigo0, 2.5f);
            cell.slant = 10f;
            loadingCells.Add(cell);
        }
        AnimateLoading(0f);
    }

    void AnimateLoading(float t)
    {
        int lit = Mathf.FloorToInt(t * 12f) % (loadingCells.Count + 3);   // 12 fps = 24 fps on 2s
        for (int i = 0; i < loadingCells.Count; i++)
            loadingCells[i].SetFill(i < lit ? AkiraPalette.Red : AkiraPalette.Indigo0);
    }

    void BuildRows(LeaderboardBoard board, LeaderboardPage top, LeaderboardEntry? player)
    {
        shipImages.Clear();
        shipIds.Clear();
        shipSkins.Clear();
        shipDrawing = -1;
        int n = Mathf.Min(top.entries.Count, LeaderboardService.PanelRows);
        for (int i = 0; i < n; i++)
        {
            var e = top.entries[i];
            bool mine = e.isLocalPlayer || (player.HasValue && e.rank == player.Value.rank && player.Value.rank > 0);
            var rt = Place(rows, "Row" + i, 0f, i * RowStep, Inner, RowHeight);
            Row(rt, e, board, mine, false);
        }
        RowCount = n;
        float content = n > 0 ? (n - 1) * RowStep + RowHeight : 0f;
        rows.sizeDelta = new Vector2(0f, content);
        SetScrolling(content > ListHeight + .5f);

        // Own row, always: the player's rank even outside the top 10.
        PlayerRowShown = true;
        var prt = playerRow.GetComponent<RectTransform>();
        var label = Place(prt, "YourRank", -Inner * .5f + 90f, -34f, 180f, 30f);
        Label(label, "YOUR RANK", 18, AkiraPalette.Muted, TextAnchor.MiddleLeft, 2f);
        var rowRect = Child(prt, "Row");
        Stretch(rowRect);
        if (player.HasValue)
        {
            var me = player.Value;
            me.isLocalPlayer = true;
            if (string.IsNullOrEmpty(me.playerName)) me.playerName = "YOU";
            Row(rowRect, me, board, true, true);
            PlayerRankText = me.rank > 0 ? "#" + me.rank : "#--";
            if (me.rank <= 0 && shared == null)
            {
                // The store kept the score but gave it no public rank: say why, under the row.
                UnrankedNote = UnrankedMessage(service.Platform.PlatformName);
                var note = Place(prt, "UnrankedNote", 0f, PlayerRowHeight + 4f, Inner, 46f);
                var text = Label(note, UnrankedNote, 18, AkiraPalette.Muted, TextAnchor.UpperLeft, 2f);
                text.fontStyle = FontStyle.Bold;
            }
        }
        else
        {
            var none = new LeaderboardEntry { rank = 0, playerName = "NO SCORE YET", isLocalPlayer = true };
            Row(rowRect, none, board, true, true);
            PlayerRankText = "#--";
        }
    }

    void Row(RectTransform rt, LeaderboardEntry e, LeaderboardBoard board, bool mine, bool own)
    {
        var shape = CelShape.Add(rt.gameObject, CelShape.Kind.Chamfer, mine ? AkiraPalette.Red : AkiraPalette.Card,
                                 own ? 4f : 3f).Cuts(false, true, false, true);
        shape.cut = 12f;
        if (own) shape.Shadow(AkiraPalette.Ink, new Vector2(6f, -6f));

        Color rankColor = mine ? AkiraPalette.Bone : AkiraPalette.Amber;
        Color nameColor = AkiraPalette.Bone;
        Color valueColor = mine ? AkiraPalette.Bone : AkiraPalette.Cyan;

        var rank = Child(rt, "Rank");
        Anchor(rank, 14f, RankWidth);
        Label(rank, e.rank > 0 ? "#" + e.rank : "#--", 26, rankColor, TextAnchor.MiddleLeft, 2.5f);

        // The premium (cheat) ship is never shown: such an entry reads as unknown.
        if (PremiumShips.IsPremium(e.startShip)) { e.startShip = 0; e.startHearts = 0; e.startSkin = 0; e.hasPauses = false; e.pauses = 0; }
        if (ShipHullArt.Has(e.startShip))
        {
            var ship = Child(rt, "Ship");
            Anchor(ship, ShipLeft, ShipWidth);
            var image = ship.gameObject.AddComponent<Image>();
            image.sprite = ShipHullArt.Get(e.startShip, e.startSkin, 0, 0);
            image.preserveAspect = true;
            image.raycastTarget = false;
            shipImages.Add(image);
            shipIds.Add(e.startShip);
            shipSkins.Add(e.startSkin);
        }
        bool hasRow = e.rank > 0 || e.value != 0;   // the "no score yet" own row has nothing to show but its name
        if (hasRow && !ShipHullArt.Has(e.startShip))
        {
            var none = Child(rt, "ShipNone");
            Anchor(none, ShipLeft, ShipWidth);
            Label(none, Dash, 22, mine ? AkiraPalette.Bone : AkiraPalette.Muted, TextAnchor.MiddleCenter, 2f, ValueMinSize);
        }

        bool boosted = board != null && board.poweredUpOnly;
        float nameWidth = boosted ? BoostedNameWidth : MainNameWidth;
        var name = Child(rt, "Name");
        float markRoom = 0f;
        var markSprite = PlatformMark.For(e.platform);
        if (markSprite != null)
        {
            // Shared board: the robot / apple mark sits before the name and takes its room from the name column.
            var mark = Child(rt, "Platform");
            float markY = hasRow ? (NameBand + 1f) * .5f : .5f;   // level with the name line
            mark.anchorMin = new Vector2(0f, markY);
            mark.anchorMax = new Vector2(0f, markY);
            mark.pivot = new Vector2(0f, .5f);
            mark.sizeDelta = new Vector2(PlatformMark.Units, PlatformMark.Units);
            mark.anchoredPosition = new Vector2(NameLeft, 0f);
            var markImage = mark.gameObject.AddComponent<Image>();
            markImage.sprite = markSprite;
            markImage.preserveAspect = true;
            markImage.raycastTarget = false;
            markRoom = PlatformMark.Reserve;
        }
        Anchor(name, NameLeft + markRoom, nameWidth - markRoom);
        if (hasRow) { name.anchorMin = new Vector2(0f, NameBand); name.anchorMax = new Vector2(0f, 1f); }
        string who = string.IsNullOrEmpty(e.playerName) ? "PILOT" : e.playerName.ToUpperInvariant();
        if (own && e.rank > 0) who = "YOU  " + who;
        FitName(Label(name, who, NameSize, nameColor, TextAnchor.MiddleLeft, 2.5f), who, nameWidth - markRoom - 4f);

        if (hasRow) RunLine(rt, e, mine);

        if (!boosted)
        {
            // The main tab: the offered columns (SCORE, DUST, WORLDS); hearts and pauses ride on the run line under the name.
            // A row with nothing to show (the "no score yet" own row) draws no cells.
            if (hasRow) MainCells(rt, e, board, mine);
            return;
        }

        // Star dust and worlds of the player's best powered-up run, from its score tag; blank without one.
        if (e.hasRunStats && e.starDustHundredths >= 0 && e.worlds >= 0)
        {
            var stats = Child(rt, "Stats");
            Anchor(stats, NameLeft + nameWidth + 2f, StatsWidth - 4f);
            var statsText = Label(stats, "DUST " + Compact(e.starDustHundredths / 100) + "\nWORLDS " + Mathf.Min(e.worlds, 99),
                                  StatsSize, AkiraPalette.Amber, TextAnchor.MiddleLeft, 2f, ValueMinSize);
            statsText.lineSpacing = .9f;
            FitText(statsText, StatsSize, ValueMinSize, StatsWidth - FitMargin);
        }

        if (e.rank > 0 || e.value != 0)
        {
            var value = Child(rt, "Value");
            bool hasPowerUps = e.hasRunStats && LeaderboardRunTag.PowerUpShort(e.powerUps).Length > 0;
            value.anchorMin = new Vector2(1f, hasPowerUps ? .38f : 0f);
            value.anchorMax = new Vector2(1f, 1f);
            value.pivot = new Vector2(1f, .5f);
            float valueWidth = BoostedValueWidth;
            value.sizeDelta = new Vector2(valueWidth, 0f);
            value.anchoredPosition = new Vector2(-ValueInset, 0f);
            string shown = board.Format(e.value);
            int valueSize = hasPowerUps ? 26 : 30;
            var valueText = Label(value, shown, valueSize, valueColor, TextAnchor.MiddleRight, 3f, ValueMinSize);
            FitText(valueText, valueSize, ValueMinSize, valueWidth);
            if (hasPowerUps)
            {
                var used = Child(rt, "PowerUps");
                used.anchorMin = new Vector2(1f, 0f);
                used.anchorMax = new Vector2(1f, .4f);
                used.pivot = new Vector2(1f, .5f);
                used.sizeDelta = new Vector2(valueWidth - 8f, 0f);
                used.anchoredPosition = new Vector2(-ValueInset - 8f, 0f);   // clear of the italic overhang and the ink stroke
                var usedText = Label(used, LeaderboardRunTag.PowerUpShort(e.powerUps), StatsSize, mine ? AkiraPalette.Bone : AkiraPalette.Amber,
                                     TextAnchor.MiddleRight, 2f, ValueMinSize);
                FitText(usedText, StatsSize, ValueMinSize, valueWidth - 8f - FitMargin);
                usedText.horizontalOverflow = HorizontalWrapMode.Wrap;   // fitted above, so it never wraps; right-aligned Overflow text is laid out from the wrong edge
            }
        }
    }

    // The run line under the name: "Hearts" (heart glyph + the start hearts) and "Pauses" (two bars + the pauses left at the end).
    // A value the entry does not carry (an old score, a store with no tag, the premium ship) shows a dash.
    void RunLine(RectTransform rt, LeaderboardEntry e, bool mine)
    {
        float left = NameLeft;
        int hearts = e.startHearts > 0 ? e.startHearts : -1;   // Row() already blanked a premium entry
        int pauses = e.hasPauses ? Mathf.Clamp(e.pauses, 0, LeaderboardRunTag.MostPauses) : -1;
        Color numColor = mine ? AkiraPalette.Bone : AkiraPalette.Amber;

        var h = RunSlot(rt, "Hearts", left + RunLineHeartsLeft, RunLineHeartsWidth);
        var heart = Child(h, "Glyph");
        GlyphBox(heart);
        var heartImage = heart.gameObject.AddComponent<Image>();
        heartImage.sprite = Resources.Load<Sprite>(ShipLivesIndicator.HeartSpritePath);
        heartImage.preserveAspect = true;
        heartImage.raycastTarget = false;
        RunCount(h, hearts, RunLineHeartsWidth, numColor, mine);

        var p = RunSlot(rt, "Pauses", left + RunLinePausesLeft, RunLinePausesWidth);
        var glyph = Child(p, "Glyph");
        GlyphBox(glyph);
        for (int i = 0; i < 2; i++)
        {
            var bar = Child(glyph, "Bar" + i);
            bar.anchorMin = bar.anchorMax = new Vector2(0f, .5f);
            bar.pivot = new Vector2(0f, .5f);
            bar.sizeDelta = new Vector2(4f, RunLineGlyph - 2f);
            bar.anchoredPosition = new Vector2(2f + i * 7f, 0f);
            var img = bar.gameObject.AddComponent<Image>();
            img.color = pauses >= 0 ? numColor : (mine ? AkiraPalette.Bone : AkiraPalette.Muted);
            img.raycastTarget = false;
        }
        RunCount(p, pauses, RunLinePausesWidth, numColor, mine);
    }

    RectTransform RunSlot(RectTransform rt, string name, float left, float width)
    {
        var slot = Child(rt, name);
        slot.anchorMin = new Vector2(0f, 0f);
        slot.anchorMax = new Vector2(0f, NameBand);
        slot.pivot = new Vector2(0f, .5f);
        slot.sizeDelta = new Vector2(width, 0f);
        slot.anchoredPosition = new Vector2(left, 0f);
        return slot;
    }

    static void GlyphBox(RectTransform glyph)
    {
        glyph.anchorMin = glyph.anchorMax = new Vector2(0f, .5f);
        glyph.pivot = new Vector2(0f, .5f);
        glyph.sizeDelta = new Vector2(RunLineGlyph, RunLineGlyph);
        glyph.anchoredPosition = Vector2.zero;
    }

    void RunCount(RectTransform slot, int value, float width, Color color, bool mine)
    {
        var count = Child(slot, "Count");
        float x = RunLineGlyph + 2f;
        count.anchorMin = new Vector2(0f, 0f);
        count.anchorMax = new Vector2(0f, 1f);
        count.pivot = new Vector2(0f, .5f);
        count.sizeDelta = new Vector2(width - x, 0f);
        count.anchoredPosition = new Vector2(x, 0f);
        string shown = value >= 0 ? value.ToString() : Dash;
        var text = Label(count, shown, RunLineSize, value >= 0 ? color : (mine ? AkiraPalette.Bone : AkiraPalette.Muted),
                         TextAnchor.MiddleLeft, 2f, ValueMinSize);
        FitText(text, RunLineSize, ValueMinSize, width - x - 2f);
    }

    // The row's number cells on the main tab, one per offered column ("Cell_<board id>"). The column being sorted shows the
    // board's own value (its real rank's value); the others come from the row's score tag, or a dash when the row has none.
    void MainCells(RectTransform rt, LeaderboardEntry e, LeaderboardBoard sortedBoard, bool mine)
    {
        int n = mainColumns.Count;
        for (int i = 0; i < n; i++)
        {
            var col = mainColumns[i];
            bool sorted = sortedBoard != null && col.id == sortedBoard.id;
            string full, compact;
            CellText(col, e, sorted, out full, out compact);
            var cell = Child(rt, "Cell_" + col.id);
            cell.anchorMin = new Vector2(1f, 0f);
            cell.anchorMax = new Vector2(1f, 1f);
            cell.pivot = new Vector2(1f, .5f);
            float width = ColWidth - 8f;
            cell.sizeDelta = new Vector2(width, 0f);
            cell.anchoredPosition = new Vector2(-ValueInset - (n - 1 - i) * ColWidth - 4f, 0f);
            int size = sorted ? SortedCellSize : OtherCellSize;
            Color color = full == Dash ? (mine ? AkiraPalette.Bone : AkiraPalette.Muted)
                        : sorted ? (mine ? AkiraPalette.Bone : AkiraPalette.Cyan)
                        : (mine ? AkiraPalette.Bone : AkiraPalette.Amber);
            var text = Label(cell, full, size, color, TextAnchor.MiddleRight, sorted ? 3f : 2f, ValueMinSize);
            FitText(text, size, ValueMinSize, width - FitMargin);
            // The full number does not fit even at the 16-unit floor (a long score): the compact form (1.2M) does.
            if (compact != full && text.preferredWidth > width - FitMargin)
            {
                text.text = compact;
                FitText(text, size, ValueMinSize, width - FitMargin);
            }
        }
    }

    // One cell's text: the full form and the compact form (the same unless the number can be long).
    static void CellText(LeaderboardBoard col, LeaderboardEntry e, bool sorted, out string full, out string compact)
    {
        full = compact = Dash;
        switch (col.id)
        {
            case LeaderboardBoards.TopScore:
                // a shared row can have no known score (0): a dash, never "0"
                if ((sorted && e.value > 0) || (!sorted && e.hasScore))
                {
                    long v = sorted ? e.value : e.runScore;
                    full = LeaderboardBoards.FormatScore(v);
                    compact = Compact(v);
                }
                break;
            case LeaderboardBoards.RunStarDust:
                // star dust in hundredths, shown whole and compact like the Power-Ups rows
                if ((sorted && e.value > 0) || (!sorted && e.hasRunStats && e.starDustHundredths >= 0))
                    full = compact = Compact((sorted ? e.value : e.starDustHundredths) / 100L);
                break;
            case LeaderboardBoards.FurthestWorld:
                if (sorted || (e.hasRunStats && e.worlds >= 0))
                {
                    long n = System.Math.Max(0L, sorted ? e.value : (long)e.worlds);
                    full = n.ToString();
                    compact = Compact(n);   // the store caps worlds at 9999; the compact form is only a net for an absurd value
                }
                break;
            default:
                if (sorted) full = compact = col.Format(e.value);
                break;
        }
    }

    // 999 as is, then 1.2K, 12K, 1M: the star dust block is only 150 units wide.
    public static string Compact(long v)
    {
        if (v < 1000L) return Mathf.Max(0L, v).ToString();
        if (v < 10000L) return (v / 1000L) + "." + ((v % 1000L) / 100L) + "K";
        if (v < 1000000L) return (v / 1000L) + "K";
        return (v / 1000000L) + "M";
    }

    // `text` at `size`, one unit at a time down to `min` until its widest line fits `width` (never smaller, never cut).
    void FitText(Text text, int size, int min, float width)
    {
        text.resizeTextForBestFit = false;
        text.horizontalOverflow = HorizontalWrapMode.Overflow;
        text.verticalOverflow = VerticalWrapMode.Overflow;
        for (; size > min; size--)
        {
            text.fontSize = size;
            if (text.preferredWidth <= width) return;
        }
        text.fontSize = min;
    }

    // One line at NameSize .. NameMinSize; a name too long even at the
    // smallest keeps as many characters as fit, then "...". Measured with the
    // font itself (wide glyphs, any script), once, when the row is built.
    public static void FitName(Text text, string full, float width)
    {
        text.resizeTextForBestFit = false;
        text.horizontalOverflow = HorizontalWrapMode.Overflow;
        text.verticalOverflow = VerticalWrapMode.Truncate;
        text.text = full;
        for (int size = NameSize; size >= NameMinSize; size -= 2)
        {
            text.fontSize = size;
            if (text.preferredWidth <= width) return;
        }
        text.fontSize = NameMinSize;
        int lo = 0, hi = full.Length;   // longest prefix that fits with the ellipsis
        while (lo < hi)
        {
            int mid = (lo + hi + 1) / 2;
            text.text = Cut(full, mid);
            if (text.preferredWidth <= width) lo = mid; else hi = mid - 1;
        }
        text.text = Cut(full, lo);
    }

    static string Cut(string s, int n)
    {
        if (n > 0 && n < s.Length && char.IsHighSurrogate(s[n - 1])) n--;   // never split a surrogate pair
        return s.Substring(0, n).TrimEnd() + Ellipsis;
    }

    // ---- per-frame ----

    void Update()
    {
        if (CurrentState == State.Loading && loadingCells.Count > 0) AnimateLoading(Time.unscaledTime);
        AnimateShips();
        if (!screenOverridden && (ScreenInfo.Width != screenSize.x || ScreenInfo.Height != screenSize.y
                                  || ScreenInfo.SafeArea != safeArea))
        {
            screenSize = new Vector2(ScreenInfo.Width, ScreenInfo.Height);
            safeArea = ScreenInfo.SafeArea;
            ApplyLayout();
        }
    }

    // The row ships' idle flipbook (ShipHullArt's tick table), one shared
    // clock: the drawing is worked out once a frame and sprites are swapped
    // only when it changes (a few times a second) -- at most 11 images.
    void AnimateShips()
    {
        if (shipImages.Count == 0) return;
        int drawing = ShipHullArt.IdleDrawingAt(Time.unscaledTime * ShipHullArt.TicksPerSecond);
        if (drawing == shipDrawing) return;
        shipDrawing = drawing;
        for (int i = 0; i < shipImages.Count; i++)
        {
            if (shipImages[i] == null) continue;
            var sprite = ShipHullArt.Get(shipIds[i], shipSkins[i], 0, drawing);
            if (sprite != null) shipImages[i].sprite = sprite;
        }
    }

    void ApplyLayout()
    {
        var layout = ComputeLayout(screenSize, safeArea);
        AppliedLayout = layout;
        bool resized = panelRoot.childCount > 0 && (layout.panelSize.x != panelW || layout.panelSize.y != panelH);
        panelW = layout.panelSize.x;
        panelH = layout.panelSize.y;
        scaler.scaleFactor = layout.scale;
        Vector2 screen = new Vector2(Mathf.Max(1f, screenSize.x), Mathf.Max(1f, screenSize.y));
        safeRoot.anchorMin = new Vector2(layout.safe.xMin / screen.x, layout.safe.yMin / screen.y);
        safeRoot.anchorMax = new Vector2(layout.safe.xMax / screen.x, layout.safe.yMax / screen.y);
        safeRoot.offsetMin = safeRoot.offsetMax = Vector2.zero;
        if (resized) Rebuild();   // a new size (rotation, a foldable, a window): build the panel again
    }

    // Builds the panel again for its current size and mode, keeping the board it showed.
    void Rebuild()
    {
        {
            Tabs.Clear();
            TabBoards.Clear();
            Headers.Clear();
            HeaderBoards.Clear();
            loadingCells.Clear();
            for (int i = panelRoot.childCount - 1; i >= 0; i--)
            {
                var child = panelRoot.GetChild(i).gameObject;
                if (Application.isPlaying) Destroy(child); else DestroyImmediate(child);
            }
            BuildPanel();
            if (SelectedBoard != null) SelectBoard(SelectedBoard); else SelectFirstBoard();
        }
    }

    // ---- building blocks ----

    internal static RectTransform Child(Transform parent, string name)
    {
        var go = new GameObject(name, typeof(RectTransform));
        var rt = go.GetComponent<RectTransform>();
        rt.SetParent(parent, false);
        return rt;
    }

    internal static void Stretch(RectTransform rt, float inset = 0f)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = new Vector2(inset, 0f);
        rt.offsetMax = new Vector2(-inset, 0f);
    }

    internal static RectTransform Fill(RectTransform parent, string name)
    {
        var rt = Child(parent, name);
        Stretch(rt);
        return rt;
    }

    // Centre x, top y (downwards from the parent's top edge), size.
    internal static RectTransform Place(RectTransform parent, string name, float x, float top, float w, float h)
    {
        var rt = Child(parent, name);
        rt.anchorMin = rt.anchorMax = new Vector2(.5f, 1f);
        rt.pivot = new Vector2(.5f, .5f);
        rt.sizeDelta = new Vector2(w, h);
        rt.anchoredPosition = new Vector2(x, -top - h * .5f);
        return rt;
    }

    // Left-anchored box inside a row.
    static void Anchor(RectTransform rt, float left, float w)
    {
        rt.anchorMin = new Vector2(0f, 0f);
        rt.anchorMax = new Vector2(0f, 1f);
        rt.pivot = new Vector2(0f, .5f);
        rt.sizeDelta = new Vector2(w, 0f);
        rt.anchoredPosition = new Vector2(left, 0f);
    }

    Text Label(RectTransform rt, string s, int size, Color color, TextAnchor align, float ink, int minSize = 0)
    {
        return LabelWith(font, rt, s, size, color, align, ink, minSize);
    }

    internal static Text LabelWith(Font font, RectTransform rt, string s, int size, Color color, TextAnchor align, float ink, int minSize = 0)
    {
        var text = rt.gameObject.AddComponent<Text>();
        text.font = font;
        text.text = s;
        text.fontSize = size;
        text.fontStyle = FontStyle.BoldAndItalic;
        text.color = color;
        text.alignment = align;
        text.raycastTarget = false;
        text.supportRichText = false;   // server-supplied names render literally
        text.horizontalOverflow = HorizontalWrapMode.Wrap;
        text.verticalOverflow = VerticalWrapMode.Overflow;
        text.resizeTextForBestFit = true;
        text.resizeTextMinSize = minSize > 0 ? minSize : Mathf.Max(12, size / 2);
        text.resizeTextMaxSize = size;
        if (ink > 0f)
        {
            // Ink stroke under the type (art-style: Orbitron with an INK stroke).
            var outline = rt.gameObject.AddComponent<Outline>();
            outline.effectColor = AkiraPalette.Ink;
            outline.effectDistance = new Vector2(ink * .6f, -ink * .6f);
        }
        return text;
    }

    Button MakeButton(RectTransform parent, string name, float x, float top, float w, float h, string caption,
                      Color accent, UnityEngine.Events.UnityAction onClick, bool filled = false)
    {
        return ButtonWith(font, parent, name, x, top, w, h, caption, accent, onClick, filled);
    }

    internal static Button ButtonWith(Font font, RectTransform parent, string name, float x, float top, float w, float h, string caption,
                                      Color accent, UnityEngine.Events.UnityAction onClick, bool filled = false)
    {
        var rt = Place(parent, name, x, top, w, h);
        var shape = CelShape.Add(rt.gameObject, CelShape.Kind.Chamfer, filled ? accent : AkiraPalette.Card, 4f)
                            .Shadow(filled ? AkiraPalette.Ink : accent, new Vector2(6f, -6f));
        shape.cut = 16f;
        shape.raycastTarget = true;
        var label = Child(rt, "Label");
        Stretch(label, 12f);
        LabelWith(font, label, caption, 34, AkiraPalette.Bone, TextAnchor.MiddleCenter, 3.5f);
        var button = rt.gameObject.AddComponent<Button>();
        button.targetGraphic = shape;
        button.transition = Selectable.Transition.None;
        button.onClick.AddListener(onClick);
        rt.gameObject.AddComponent<LeaderboardCelPress>();
        return button;
    }

    static void Clear(Transform t)
    {
        for (int i = t.childCount - 1; i >= 0; i--)
        {
            var child = t.GetChild(i).gameObject;
            if (Application.isPlaying) Destroy(child);
            else DestroyImmediate(child);
        }
        // Components on the container itself (a row plate) go too.
        foreach (var c in t.GetComponents<Component>())
        {
            if (c is RectTransform) continue;
            if (Application.isPlaying) Destroy(c);
            else DestroyImmediate(c);
        }
    }
}

// Snappy press: the plate drops onto its shadow while held (no easing).
public class LeaderboardCelPress : MonoBehaviour, UnityEngine.EventSystems.IPointerDownHandler,
                        UnityEngine.EventSystems.IPointerUpHandler, UnityEngine.EventSystems.IPointerExitHandler
{
    Vector2 rest;
    bool down;

    public void OnPointerDown(UnityEngine.EventSystems.PointerEventData e)
    {
        if (down) return;
        var rt = (RectTransform)transform;
        rest = rt.anchoredPosition;
        rt.anchoredPosition = rest + new Vector2(4f, -4f);
        down = true;
    }

    public void OnPointerUp(UnityEngine.EventSystems.PointerEventData e) { Release(); }
    public void OnPointerExit(UnityEngine.EventSystems.PointerEventData e) { Release(); }

    void Release()
    {
        if (!down) return;
        ((RectTransform)transform).anchoredPosition = rest;
        down = false;
    }
}
