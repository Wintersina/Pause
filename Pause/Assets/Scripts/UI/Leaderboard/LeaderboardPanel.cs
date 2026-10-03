using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// The in-game leaderboard, opened from Options (leaderboardS3) -> LeaderBoard.
//
// Built entirely at runtime on its own overlay canvas, so the scene only
// keeps its existing button. Board tabs (enabled boards only), the top 10,
// the player's own rank row, and "View all" for the store's native screen.
//
// States: signed out (one-tap sign-in), loading, empty, error/offline,
// unavailable (no store on this device), no boards configured, populated.
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

    const float Inner = 640f;                 // content width
    const float RowHeight = 54f, RowStep = 61f;
    const float ListTop = 276f;               // from the panel top
    const float PlayerRowTop = 920f, PlayerRowHeight = 66f;
    const float ButtonsTop = 1036f, ButtonHeight = 100f;

    public struct Layout
    {
        public float scale;          // canvas units -> screen pixels
        public Rect safe;            // pixels
        public Rect panelPixels;     // pixels
    }

    public static Layout ComputeLayout(Vector2 screen, Rect safe)
    {
        if (safe.width <= 0f || safe.height <= 0f) safe = new Rect(0f, 0f, screen.x, screen.y);
        float scale = Mathf.Min(safe.width * FillWidth / DesignWidth, safe.height * FillHeight / DesignHeight);
        var size = new Vector2(DesignWidth, DesignHeight) * scale;
        return new Layout
        {
            scale = scale,
            safe = safe,
            panelPixels = new Rect(safe.center - size * .5f, size),
        };
    }

    // ---- public surface (tests read these) ----

    public static LeaderboardPanel Current { get; private set; }

    public State CurrentState { get; private set; }
    public string SelectedBoard { get; private set; }
    public Layout AppliedLayout { get; private set; }
    public readonly List<Button> Tabs = new List<Button>();
    public readonly List<string> TabBoards = new List<string>();
    public Button ViewAllButton { get; private set; }
    public Button CloseButton { get; private set; }
    public Button ActionButton { get; private set; }   // SIGN IN / RETRY, when shown
    public int RowCount { get; private set; }
    public bool PlayerRowShown { get; private set; }
    public string PlayerRankText { get; private set; }
    public string MessageText { get; private set; }

    public RectTransform SafeRoot { get { return safeRoot; } }
    public RectTransform PanelRoot { get { return panelRoot; } }

    LeaderboardService service;
    Font font;
    Canvas canvas;
    CanvasScaler scaler;
    RectTransform safeRoot, panelRoot, body;
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
        return Open(service, font, new Vector2(Screen.width, Screen.height), Screen.safeArea, false);
    }

    // Tests pass an explicit screen and safe area.
    public static LeaderboardPanel Open(LeaderboardService service, Font font, Vector2 screen, Rect safe,
                                       bool overrideScreen = true)
    {
        Close();
        var go = new GameObject("~LeaderboardPanel", typeof(RectTransform));
        var panel = go.AddComponent<LeaderboardPanel>();
        panel.service = service ?? LeaderboardService.Instance;
        panel.font = font != null ? font : DockArt.FindSceneFont();
        panel.screenOverridden = overrideScreen;
        panel.screenSize = screen;
        panel.safeArea = safe;
        panel.Build();
        Current = panel;
        // Back closes the panel before Options' own back (home) is reached.
        BackNavigator.Register(panel, panel.OnBackPressed);
        panel.SelectFirstBoard();
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

    // Android back / Escape: closes the panel instead of leaving Options
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

    void OnDestroy()
    {
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

        // Full-screen dim that also swallows taps meant for the Options buttons.
        var dim = Child(transform, "Dim");
        Stretch(dim);
        var dimImage = dim.gameObject.AddComponent<Image>();
        dimImage.color = AkiraPalette.WithAlpha(AkiraPalette.Night0, .88f);

        safeRoot = Child(transform, "SafeArea");
        panelRoot = Child(safeRoot, "Panel");
        panelRoot.anchorMin = panelRoot.anchorMax = panelRoot.pivot = new Vector2(.5f, .5f);
        panelRoot.sizeDelta = new Vector2(DesignWidth, DesignHeight);
        ApplyLayout();

        // Frame: chamfered night plate, thick ink, inner panel line.
        var frame = CelShape.Add(Fill(panelRoot, "Frame").gameObject, CelShape.Kind.Chamfer,
                                 AkiraPalette.WithAlpha(AkiraPalette.Night1, .97f), 5f);
        frame.cut = 30f;
        frame.raycastTarget = true;   // taps on the panel body don't fall through to the dim
        var line = CelShape.Add(Place(panelRoot, "PanelLine", 0f, 10f, DesignWidth - 20f, DesignHeight - 20f).gameObject,
                                CelShape.Kind.Chamfer, Color.clear, 1.5f);
        line.cut = 25f;
        line.hollow = true;
        line.ink = AkiraPalette.Hairline;

        BuildTitle();
        BuildTabs();

        var desc = Place(panelRoot, "Description", 0f, 230f, Inner, 34f);
        descriptionText = Label(desc, "", 20, AkiraPalette.Muted, TextAnchor.MiddleLeft, 2f);

        body = Place(panelRoot, "Body", 0f, ListTop, Inner, PlayerRowTop + PlayerRowHeight - ListTop);

        playerRow = Place(panelRoot, "PlayerRow", 0f, PlayerRowTop, Inner, PlayerRowHeight).gameObject;

        CloseButton = MakeButton(panelRoot, "Back", -Inner * .25f - 8f, ButtonsTop, Inner * .5f - 16f, ButtonHeight,
                                 "BACK", AkiraPalette.Red, Close);
        ViewAllButton = MakeButton(panelRoot, "ViewAll", Inner * .25f + 8f, ButtonsTop, Inner * .5f - 16f, ButtonHeight,
                                   "VIEW ALL", AkiraPalette.Cyan, ViewAll);
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
        var boards = service.UsableBoards();
        if (boards.Count == 0) return;
        const float gap = 14f, top = 150f, height = 68f;
        float w = (Inner - gap * (boards.Count - 1)) / boards.Count;
        for (int i = 0; i < boards.Count; i++)
        {
            var board = boards[i];
            float x = -Inner * .5f + w * .5f + i * (w + gap);
            var rt = Place(panelRoot, "Tab_" + board.id, x, top, w, height);
            var shape = CelShape.Add(rt.gameObject, CelShape.Kind.Chamfer, AkiraPalette.Card, 3.5f)
                                .Cuts(true, false, true, false);
            shape.cut = 14f;
            shape.raycastTarget = true;
            var label = Child(rt, "Label");
            Stretch(label, 10f);
            Label(label, board.displayName.ToUpperInvariant(), 26, AkiraPalette.Muted, TextAnchor.MiddleCenter, 2.5f);
            var button = rt.gameObject.AddComponent<Button>();
            button.targetGraphic = shape;
            button.transition = Selectable.Transition.None;
            string id = board.id;
            button.onClick.AddListener(() => SelectBoard(id));
            Tabs.Add(button);
            TabBoards.Add(id);
        }
    }

    // ---- behaviour ----

    void SelectFirstBoard()
    {
        if (TabBoards.Count == 0)
        {
            SelectedBoard = null;
            Show(State.NoBoards);
            return;
        }
        SelectBoard(TabBoards[0]);
    }

    public void SelectBoard(string boardId)
    {
        SelectedBoard = boardId;
        for (int i = 0; i < Tabs.Count; i++)
        {
            bool on = TabBoards[i] == boardId;
            var shape = Tabs[i].GetComponent<CelShape>();
            shape.SetFill(on ? AkiraPalette.Red : AkiraPalette.Card);
            shape.hasShadow = on;
            shape.shadow = AkiraPalette.Ink;
            shape.shadowOffset = new Vector2(5f, -5f);
            var text = Tabs[i].GetComponentInChildren<Text>();
            text.color = on ? AkiraPalette.Bone : AkiraPalette.Muted;
        }
        var board = LeaderboardBoards.Get(boardId);
        if (descriptionText != null && board != null)
            descriptionText.text = board.description.ToUpperInvariant();
        Reload();
    }

    public void Reload()
    {
        if (SelectedBoard == null) { Show(State.NoBoards); return; }
        var platform = service.Platform;
        if (!platform.IsAvailable) { Show(State.Unavailable); return; }
        if (!platform.IsSignedIn) { Show(State.SignedOut); return; }

        int token = ++request;
        string boardId = SelectedBoard;
        Show(State.Loading);
        service.LoadBoard(boardId, LeaderboardService.PanelRows, (top, me) =>
        {
            if (this == null || token != request) return;   // closed, or another tab since
            OnLoaded(boardId, top, me);
        });
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

    void ViewAll()
    {
        service.ShowNativeUI(SelectedBoard);
    }

    // ---- states ----

    void Show(State state, LeaderboardBoard board = null, LeaderboardPage top = null, LeaderboardEntry? player = null)
    {
        CurrentState = state;
        Clear(body);
        Clear(playerRow.transform);
        loadingCells.Clear();
        ActionButton = null;
        RowCount = 0;
        PlayerRowShown = false;
        PlayerRankText = null;
        MessageText = null;

        string platformName = service.Platform.PlatformName;
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
                ActionButton = MakeButton(body, "Retry", 0f, 330f, 420f, ButtonHeight, "RETRY", AkiraPalette.Red, Reload, true);
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

        bool storeReachable = state == State.Populated || state == State.Empty || state == State.Error;
        ViewAllButton.gameObject.SetActive(storeReachable);
        // Alone, BACK sits in the middle.
        var back = (RectTransform)CloseButton.transform;
        back.anchoredPosition = new Vector2(storeReachable ? -Inner * .25f - 8f : 0f, back.anchoredPosition.y);
    }

    static string StoreName(string platformName)
    {
        return platformName == "none" || string.IsNullOrEmpty(platformName) ? "your game account" : platformName;
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
        int n = Mathf.Min(top.entries.Count, LeaderboardService.PanelRows);
        for (int i = 0; i < n; i++)
        {
            var e = top.entries[i];
            bool mine = e.isLocalPlayer || (player.HasValue && e.rank == player.Value.rank && player.Value.rank > 0);
            var rt = Place(body, "Row" + i, 0f, i * RowStep, Inner, RowHeight);
            Row(rt, e, board, mine, false);
        }
        RowCount = n;

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
            PlayerRankText = "#" + me.rank;
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
        Anchor(rank, 18f, 100f);
        Label(rank, e.rank > 0 ? "#" + e.rank : "#--", 26, rankColor, TextAnchor.MiddleLeft, 2.5f);

        var name = Child(rt, "Name");
        Anchor(name, 120f, 290f);
        string who = string.IsNullOrEmpty(e.playerName) ? "PILOT" : e.playerName.ToUpperInvariant();
        if (own && e.rank > 0) who = "YOU  " + who;
        Label(name, who, 24, nameColor, TextAnchor.MiddleLeft, 2.5f);

        if (e.rank > 0 || e.value != 0)
        {
            var value = Child(rt, "Value");
            value.anchorMin = new Vector2(1f, 0f);
            value.anchorMax = new Vector2(1f, 1f);
            value.pivot = new Vector2(1f, .5f);
            value.sizeDelta = new Vector2(200f, 0f);
            value.anchoredPosition = new Vector2(-22f, 0f);
            Label(value, board != null ? board.Format(e.value) : e.value.ToString(), 30, valueColor,
                  TextAnchor.MiddleRight, 3f);
        }
    }

    // ---- per-frame ----

    void Update()
    {
        if (CurrentState == State.Loading && loadingCells.Count > 0) AnimateLoading(Time.unscaledTime);
        if (!screenOverridden && (Screen.width != screenSize.x || Screen.height != screenSize.y
                                  || Screen.safeArea != safeArea))
        {
            screenSize = new Vector2(Screen.width, Screen.height);
            safeArea = Screen.safeArea;
            ApplyLayout();
        }
    }

    void ApplyLayout()
    {
        var layout = ComputeLayout(screenSize, safeArea);
        AppliedLayout = layout;
        scaler.scaleFactor = layout.scale;
        Vector2 screen = new Vector2(Mathf.Max(1f, screenSize.x), Mathf.Max(1f, screenSize.y));
        safeRoot.anchorMin = new Vector2(layout.safe.xMin / screen.x, layout.safe.yMin / screen.y);
        safeRoot.anchorMax = new Vector2(layout.safe.xMax / screen.x, layout.safe.yMax / screen.y);
        safeRoot.offsetMin = safeRoot.offsetMax = Vector2.zero;
    }

    // ---- building blocks ----

    static RectTransform Child(Transform parent, string name)
    {
        var go = new GameObject(name, typeof(RectTransform));
        var rt = go.GetComponent<RectTransform>();
        rt.SetParent(parent, false);
        return rt;
    }

    static void Stretch(RectTransform rt, float inset = 0f)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = new Vector2(inset, 0f);
        rt.offsetMax = new Vector2(-inset, 0f);
    }

    static RectTransform Fill(RectTransform parent, string name)
    {
        var rt = Child(parent, name);
        Stretch(rt);
        return rt;
    }

    // Centre x, top y (downwards from the parent's top edge), size.
    static RectTransform Place(RectTransform parent, string name, float x, float top, float w, float h)
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

    Text Label(RectTransform rt, string s, int size, Color color, TextAnchor align, float ink)
    {
        var text = rt.gameObject.AddComponent<Text>();
        text.font = font;
        text.text = s;
        text.fontSize = size;
        text.fontStyle = FontStyle.BoldAndItalic;
        text.color = color;
        text.alignment = align;
        text.raycastTarget = false;
        text.horizontalOverflow = HorizontalWrapMode.Wrap;
        text.verticalOverflow = VerticalWrapMode.Overflow;
        text.resizeTextForBestFit = true;
        text.resizeTextMinSize = Mathf.Max(12, size / 2);
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
        var rt = Place(parent, name, x, top, w, h);
        var shape = CelShape.Add(rt.gameObject, CelShape.Kind.Chamfer, filled ? accent : AkiraPalette.Card, 4f)
                            .Shadow(filled ? AkiraPalette.Ink : accent, new Vector2(6f, -6f));
        shape.cut = 16f;
        shape.raycastTarget = true;
        var label = Child(rt, "Label");
        Stretch(label, 12f);
        Label(label, caption, 34, AkiraPalette.Bone, TextAnchor.MiddleCenter, 3.5f);
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
