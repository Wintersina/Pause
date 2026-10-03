using System;
using UnityEngine;
using UnityEngine.UI;

// The codex view, opened from the credits screen: category tabs, a scrollable
// grid of cards (sprite + name) and a detail view (large sprite, name,
// category and lore). Undiscovered entries show as a dark silhouette named
// "???" with no lore.
//
// Built entirely at runtime on its own overlay canvas as flat 80s-anime cels
// in the Akira palette (CodexPalette): thick ink outlines, flat fills, hard
// angular corners, Kaneda red for selection. Every rect comes from
// ComputeLayout(safe area), which is what CodexTest checks across aspect
// ratios. All motion runs on unscaled time and nothing allocates per frame.
public class CodexPanel : MonoBehaviour
{
    // ---- Layout constants (canvas units) ----

    public const float GlowMargin = 0f;    // the cel frame's ink line sits inside the body
    public const float Margin = 12f;       // clear of the safe-area edge
    public const float MaxWidth = 820f;
    public const float Pad = 30f;
    public const float HeaderHeight = 64f;
    public const float DividerHeight = 16f;
    public const float TabsHeight = 60f;
    public const float TabGap = 8f;
    public const float BackWidth = 288f, BackHeight = 100f;
    public const float Gap = 16f;
    public const float MinCard = 176f;
    public const float CardNameHeight = 54f;
    public const float GridInset = 8f;

    // ---- Timing (seconds, unscaled) ----

    public const float OpenDuration = .3f;
    public const float CloseDuration = .2f;
    public const float SwapDuration = .22f;
    public const float TabFadeDuration = .18f;

    public static readonly CodexCategory[] Tabs =
    {
        CodexCategory.Log, CodexCategory.Enemies, CodexCategory.Hazards,
        CodexCategory.Atoms, CodexCategory.Worlds, CodexCategory.Ships,
    };

    public const string LockedHint = "Not yet discovered.\nKeep flying - it's out there somewhere.";

    public static string CategoryLabel(CodexCategory c)
    {
        switch (c)
        {
            case CodexCategory.Log: return "LOG";
            case CodexCategory.Enemies: return "ENEMIES";
            case CodexCategory.Hazards: return "HAZARDS";
            case CodexCategory.Atoms: return "ATOMS";
            case CodexCategory.Worlds: return "WORLDS";
            default: return "SHIPS";
        }
    }

    public struct Layout
    {
        public Rect panel;      // canvas units, centre-origin
        // The rest are panel-local (origin at the panel centre).
        public Rect header, divider, tabs, body, detail, back;
        public int columns;
        public float cardWidth, cardHeight, tabWidth;
    }

    // Pure: where everything goes for a given safe area (canvas units,
    // centre-origin). The panel fills the safe area (less margin and glow)
    // up to MaxWidth; the grid gets whatever height is left and scrolls.
    public static Layout ComputeLayout(Rect safe)
    {
        var l = new Layout();
        float m = Margin + GlowMargin;
        var avail = Rect.MinMaxRect(safe.xMin + m, safe.yMin + m, safe.xMax - m, safe.yMax - m);
        float w = Mathf.Max(0f, Mathf.Min(avail.width, MaxWidth));
        float h = Mathf.Max(0f, avail.height);
        l.panel = CodexUi.Centered(avail.center.x, avail.center.y, w, h);

        float iw = w - 2f * Pad;
        float left = -w * .5f + Pad, top = h * .5f - Pad, bottom = -h * .5f + Pad;
        l.header = new Rect(left, top - HeaderHeight, iw, HeaderHeight);
        l.divider = CodexUi.Centered(0f, l.header.yMin - 4f - DividerHeight * .5f, iw * .7f, DividerHeight);
        l.tabs = new Rect(left, l.divider.yMin - 10f - TabsHeight, iw, TabsHeight);
        l.back = CodexUi.Centered(0f, bottom + BackHeight * .5f, BackWidth, BackHeight);
        l.body = Rect.MinMaxRect(left, l.back.yMax + Gap, left + iw, l.tabs.yMin - Gap);
        l.detail = Rect.MinMaxRect(left, l.back.yMax + Gap, left + iw, l.divider.yMin - 10f);

        float gridW = iw - 2f * GridInset;
        l.columns = Mathf.Clamp(Mathf.FloorToInt((gridW + Gap) / (MinCard + Gap)), 2, 5);
        l.cardWidth = (gridW - Gap * (l.columns - 1)) / l.columns;
        l.cardHeight = l.cardWidth + CardNameHeight - 14f;
        l.tabWidth = (iw - TabGap * (Tabs.Length - 1)) / Tabs.Length;
        return l;
    }

    // ---------------------------------------------------------------------
    // Opening
    // ---------------------------------------------------------------------

    static CodexPanel instance;
    public static CodexPanel Current { get { return instance; } }

    public static event Action Closed;

    public static CodexPanel Open(Font font)
    {
        if (instance == null) instance = Build(font);
        instance.Show();
        return instance;
    }

    // ---- Built state (cached; nothing is looked up per frame) ----

    Font font;
    Canvas canvas;
    Image scrim;
    RectTransform panel;
    CanvasGroup panelGroup;
    Text title, counter;
    RectTransform marker;
    Image divider;
    RectTransform tabsRoot;
    CanvasGroup tabsGroup;
    Image[] tabFrames;
    Text[] tabLabels;
    RectTransform grid, viewport, content;
    CanvasGroup gridGroup;
    ScrollRect scroll;
    Card[] cards;
    RectTransform detail;
    CanvasGroup detailGroup;
    Image detailFrame, detailFrameEdge, detailMask, detailArt;
    Mask detailMaskComp;
    Text detailName, detailPillLabel, detailSubtitle, detailLore, detailIndex;
    Image detailPill, detailLoreCard, detailLoreEdge;
    RectTransform detailArtBox;
    RectTransform backSlot;
    Button backBtn;
    Behaviour sceneBack;   // the scene's Escape handler, muted while open

    Layout layout;
    CodexCategory category = CodexCategory.Log;
    CodexEntry[] shown = new CodexEntry[0];
    int shownCount;
    CodexEntry detailEntry;

    enum Phase { Hidden, Opening, Open, Closing }
    Phase phase = Phase.Hidden;
    float phaseAt;
    bool inDetail;
    float swapAt = -10f, tabAt = -10f;
    int lastW, lastH;
    Rect lastSafe;

    class Card
    {
        public RectTransform rt;
        public Button button;
        public Image frame, edge, mask, art, lockIcon;
        public Mask maskComp;
        public RectTransform artBox;
        public Text name;
        public CodexEntry entry;
    }

    public bool IsOpen { get { return phase == Phase.Opening || phase == Phase.Open; } }
    public bool InDetail { get { return inDetail; } }
    public CodexCategory Category { get { return category; } }
    public CodexEntry DetailEntry { get { return detailEntry; } }
    public RectTransform Panel { get { return panel; } }
    public Layout CurrentLayout { get { return layout; } }
    public Button BackButton { get { return backBtn; } }
    public Text DetailName { get { return detailName; } }
    public Text DetailLore { get { return detailLore; } }
    public Image DetailArt { get { return detailArt; } }
    public Text Counter { get { return counter; } }
    public int VisibleCards { get { return shownCount; } }

    public Button CardButton(int i) { return cards[i].button; }
    public Text CardName(int i) { return cards[i].name; }
    public Image CardArt(int i) { return cards[i].art; }
    public CodexEntry CardEntry(int i) { return cards[i].entry; }
    public RectTransform CardRect(int i) { return cards[i].rt; }
    public Text TabLabel(int i) { return tabLabels[i]; }

    // ---------------------------------------------------------------------
    // Building
    // ---------------------------------------------------------------------

    static CodexPanel Build(Font font)
    {
        var canvas = CodexUi.NewOverlayCanvas("CodexPanel", 500, true);
        var view = canvas.gameObject.AddComponent<CodexPanel>();
        view.canvas = canvas;
        view.font = font != null ? font : CodexUi.FindFont();
        view.BuildAll();
        DeveloperUnlocks.Changed -= OnDeveloperModeChanged;
        DeveloperUnlocks.Changed += OnDeveloperModeChanged;
        return view;
    }

    void BuildAll()
    {
        var root = (RectTransform)transform;

        var scrimColor = CodexPalette.Scrim; scrimColor.a = 0f;
        scrim = CodexUi.NewImage("Scrim", root, null, scrimColor);
        CodexUi.Stretch(scrim.rectTransform);
        scrim.raycastTarget = true;   // the home screen underneath stays untouchable

        var panelGo = new GameObject("Panel", typeof(RectTransform), typeof(CanvasGroup));
        panelGo.transform.SetParent(root, false);
        panel = (RectTransform)panelGo.transform;
        panelGroup = panelGo.GetComponent<CanvasGroup>();

        var frame = CodexUi.NewImage("Frame", panel, CodexUi.CodexSprite("cx_panel"), Color.white, true);
        frame.raycastTarget = true;
        frame.rectTransform.anchorMin = Vector2.zero;
        frame.rectTransform.anchorMax = Vector2.one;
        frame.rectTransform.offsetMin = new Vector2(-GlowMargin, -GlowMargin);
        frame.rectTransform.offsetMax = new Vector2(GlowMargin, GlowMargin);

        title = CodexUi.NewText("Title", panel, font, "CODEX", 44, CodexUi.Title, TextAnchor.MiddleLeft);
        CodexUi.AddOutline(title.gameObject, CodexUi.Ink, 3f);
        title.fontStyle = FontStyle.BoldAndItalic;   // style guide: headings lean forward
        var markerImage = CodexUi.NewImage("Marker", panel, CodexUi.CodexSprite("cx_marker"), Color.white);
        markerImage.preserveAspect = true;
        marker = markerImage.rectTransform;
        counter = CodexUi.NewText("Counter", panel, font, "", 20, CodexUi.Accent, TextAnchor.MiddleRight);
        CodexUi.AddOutline(counter.gameObject, CodexUi.Ink, 1.5f);

        divider = CodexUi.NewImage("Divider", panel, CodexUi.CodexSprite("cx_divider"), Color.white);
        divider.preserveAspect = true;

        BuildTabs();
        BuildGrid();
        BuildDetail();
        BuildBack();
    }

    void BuildTabs()
    {
        tabsRoot = CodexUi.NewRect("Tabs", panel);
        tabsGroup = tabsRoot.gameObject.AddComponent<CanvasGroup>();
        tabFrames = new Image[Tabs.Length];
        tabLabels = new Text[Tabs.Length];
        for (int i = 0; i < Tabs.Length; i++)
        {
            int index = i;
            var tabFrame = CodexUi.NewImage("Tab" + CategoryLabel(Tabs[i]), tabsRoot, CodexUi.CodexSprite("cx_tab"), CodexUi.Idle, true);
            tabFrame.raycastTarget = true;
            var button = tabFrame.gameObject.AddComponent<Button>();
            button.transition = Selectable.Transition.None;
            button.targetGraphic = tabFrame;
            button.onClick.AddListener(() => ShowCategory(Tabs[index]));

            var label = CodexUi.NewText("Label", tabFrame.rectTransform, font, CategoryLabel(Tabs[i]), 18, Color.white,
                                        TextAnchor.MiddleCenter);
            label.horizontalOverflow = HorizontalWrapMode.Wrap;
            label.verticalOverflow = VerticalWrapMode.Truncate;
            label.resizeTextForBestFit = true;
            label.resizeTextMinSize = 11;
            label.resizeTextMaxSize = 18;
            var lrt = label.rectTransform;
            CodexUi.Stretch(lrt);
            lrt.offsetMin = new Vector2(6f, 0f);
            lrt.offsetMax = new Vector2(-6f, 0f);

            tabFrames[i] = tabFrame;
            tabLabels[i] = label;
        }
    }

    void BuildGrid()
    {
        var go = new GameObject("Grid", typeof(RectTransform), typeof(CanvasGroup), typeof(ScrollRect));
        go.transform.SetParent(panel, false);
        grid = (RectTransform)go.transform;
        gridGroup = go.GetComponent<CanvasGroup>();

        viewport = CodexUi.NewRect("Viewport", grid);
        CodexUi.Stretch(viewport);
        viewport.gameObject.AddComponent<RectMask2D>();
        // Invisible, but gives drags that start between cards something to hit.
        var hit = viewport.gameObject.AddComponent<Image>();
        hit.color = new Color(0f, 0f, 0f, 0f);
        hit.raycastTarget = true;

        content = CodexUi.NewRect("Content", viewport);
        content.anchorMin = new Vector2(0f, 1f);
        content.anchorMax = new Vector2(1f, 1f);
        content.pivot = new Vector2(.5f, 1f);
        content.anchoredPosition = Vector2.zero;

        scroll = go.GetComponent<ScrollRect>();
        scroll.viewport = viewport;
        scroll.content = content;
        scroll.horizontal = false;
        scroll.vertical = true;
        scroll.movementType = ScrollRect.MovementType.Elastic;
        scroll.inertia = true;
        scroll.scrollSensitivity = 30f;

        int most = 0;
        foreach (var c in Tabs)
        {
            int total = Codex.CapacityIn(c);
            most = Mathf.Max(most, total);
        }
        cards = new Card[most];
        for (int i = 0; i < most; i++) cards[i] = BuildCard(i);
    }

    Card BuildCard(int index)
    {
        var card = new Card();
        card.frame = CodexUi.NewImage("Card" + index, content, CodexUi.CodexSprite("cx_card"), Color.white, true);
        card.frame.raycastTarget = true;
        card.rt = card.frame.rectTransform;
        card.button = card.frame.gameObject.AddComponent<Button>();
        card.button.targetGraphic = card.frame;
        var colors = card.button.colors;
        colors.pressedColor = CodexUi.Select;
        colors.highlightedColor = Color.white;
        colors.fadeDuration = .08f;
        card.button.colors = colors;
        card.button.onClick.AddListener(() => OnCardClicked(index));
        card.edge = CodexUi.NewImage("Edge", card.rt, CodexUi.CodexSprite("cx_card_edge"), CodexUi.Accent, true);
        CodexUi.Stretch(card.edge.rectTransform);

        // Art sits inside a holder that can become a round mask (worlds).
        card.mask = CodexUi.NewImage("ArtBox", card.rt, CodexUi.CodexSprite("cx_circle"), Color.white);
        card.artBox = card.mask.rectTransform;
        card.maskComp = card.mask.gameObject.AddComponent<Mask>();
        card.maskComp.showMaskGraphic = false;
        card.art = CodexUi.NewImage("Art", card.artBox, null, Color.white);
        card.art.preserveAspect = true;
        CodexUi.Stretch(card.art.rectTransform);

        card.name = CodexUi.NewText("Name", card.rt, font, "", 18, CodexUi.Body, TextAnchor.MiddleCenter, true);
        card.name.verticalOverflow = VerticalWrapMode.Truncate;
        card.name.resizeTextForBestFit = true;
        card.name.resizeTextMinSize = 11;
        card.name.resizeTextMaxSize = 18;
        CodexUi.AddOutline(card.name.gameObject, CodexUi.Ink, 1.5f);

        card.lockIcon = CodexUi.NewImage("Lock", card.rt, CodexUi.CodexSprite("cx_lock"), CodexUi.Select);
        card.lockIcon.preserveAspect = true;
        return card;
    }

    void BuildDetail()
    {
        var go = new GameObject("Detail", typeof(RectTransform), typeof(CanvasGroup));
        go.transform.SetParent(panel, false);
        detail = (RectTransform)go.transform;
        detailGroup = go.GetComponent<CanvasGroup>();

        detailFrame = CodexUi.NewImage("ArtFrame", detail, CodexUi.CodexSprite("cx_card"), Color.white, true);
        detailFrameEdge = CodexUi.NewImage("Edge", detailFrame.rectTransform, CodexUi.CodexSprite("cx_card_edge"), CodexUi.Accent, true);
        CodexUi.Stretch(detailFrameEdge.rectTransform);
        detailMask = CodexUi.NewImage("ArtBox", detailFrame.rectTransform, CodexUi.CodexSprite("cx_circle"), Color.white);
        detailArtBox = detailMask.rectTransform;
        detailMaskComp = detailMask.gameObject.AddComponent<Mask>();
        detailMaskComp.showMaskGraphic = false;
        detailArt = CodexUi.NewImage("Art", detailArtBox, null, Color.white);
        detailArt.preserveAspect = true;
        CodexUi.Stretch(detailArt.rectTransform);

        detailIndex = CodexUi.NewText("Index", detail, font, "", 16, CodexUi.Muted, TextAnchor.UpperRight);

        detailName = CodexUi.NewText("Name", detail, font, "", 40, CodexUi.Title, TextAnchor.MiddleCenter, true);
        detailName.verticalOverflow = VerticalWrapMode.Truncate;
        detailName.resizeTextForBestFit = true;
        detailName.resizeTextMinSize = 22;
        detailName.resizeTextMaxSize = 40;
        CodexUi.AddOutline(detailName.gameObject, CodexUi.Ink, 3f);
        detailName.fontStyle = FontStyle.BoldAndItalic;

        detailPill = CodexUi.NewImage("Category", detail, CodexUi.CodexSprite("cx_tab"), CodexUi.Accent, true);
        detailPillLabel = CodexUi.NewText("Label", detailPill.rectTransform, font, "", 18, CodexUi.Ink,
                                          TextAnchor.MiddleCenter);
        CodexUi.Stretch(detailPillLabel.rectTransform);

        detailSubtitle = CodexUi.NewText("Subtitle", detail, font, "", 20, CodexUi.Title, TextAnchor.MiddleCenter);
        CodexUi.AddOutline(detailSubtitle.gameObject, CodexUi.Ink, 1.5f);

        detailLoreCard = CodexUi.NewImage("LoreCard", detail, CodexUi.CodexSprite("cx_card"), Color.white, true);
        detailLoreEdge = CodexUi.NewImage("Edge", detailLoreCard.rectTransform, CodexUi.CodexSprite("cx_card_edge"), CodexUi.Accent, true);
        CodexUi.Stretch(detailLoreEdge.rectTransform);
        detailLore = CodexUi.NewText("Lore", detailLoreCard.rectTransform, font, "", 26, CodexUi.Body,
                                     TextAnchor.UpperLeft, true);
        detailLore.fontStyle = FontStyle.Normal;
        detailLore.lineSpacing = 1.15f;
        detailLore.verticalOverflow = VerticalWrapMode.Truncate;
        detailLore.resizeTextForBestFit = true;
        detailLore.resizeTextMinSize = 16;
        detailLore.resizeTextMaxSize = 26;
        var lrt = detailLore.rectTransform;
        CodexUi.Stretch(lrt);
        lrt.offsetMin = new Vector2(30f, 24f);
        lrt.offsetMax = new Vector2(-34f, -26f);
    }

    void BuildBack()
    {
        backSlot = CodexUi.NewRect("BackSlot", panel);
        var frame = CodexUi.NewImage("BackButton", backSlot, CodexUi.CodexSprite("cx_button"), Color.white, true);
        frame.raycastTarget = true;
        CodexUi.Stretch(frame.rectTransform);
        var edge = CodexUi.NewImage("Edge", frame.rectTransform, CodexUi.CodexSprite("cx_button_edge"), CodexUi.Accent, true);
        CodexUi.Stretch(edge.rectTransform);
        backBtn = frame.gameObject.AddComponent<Button>();
        backBtn.targetGraphic = frame;
        var colors = backBtn.colors;
        colors.pressedColor = CodexUi.Select;
        colors.fadeDuration = .08f;
        backBtn.colors = colors;
        backBtn.onClick.AddListener(Back);

        var label = CodexUi.NewText("Label", frame.rectTransform, font, "BACK", 28, CodexUi.Body, TextAnchor.MiddleLeft);
        CodexUi.AddOutline(label.gameObject, CodexUi.Ink, 2f);
        const float iconSize = 40f, gap = 12f;
        float textWidth = Mathf.Ceil(label.preferredWidth);
        float x = -(textWidth + iconSize + gap) * .5f;
        var icon = CodexUi.NewImage("Icon", frame.rectTransform, CodexUi.CodexSprite("cx_back"), Color.white);
        icon.preserveAspect = true;
        CodexUi.Place(icon.rectTransform, CodexUi.Centered(x + iconSize * .5f, 0f, iconSize, iconSize));
        CodexUi.Place(label.rectTransform, new Rect(x + iconSize + gap, -24f, textWidth + 4f, 48f));
    }

    // ---------------------------------------------------------------------
    // Layout
    // ---------------------------------------------------------------------

    void Fit()
    {
        lastW = Screen.width;
        lastH = Screen.height;
        lastSafe = Screen.safeArea;
        Canvas.ForceUpdateCanvases();
        ApplyLayout(CodexUi.SafeAreaUnits(canvas));
    }

    // Public so tests can lay the panel out for any safe area.
    public void ApplyLayout(Rect safeUnits)
    {
        layout = ComputeLayout(safeUnits);
        panel.anchorMin = panel.anchorMax = panel.pivot = new Vector2(.5f, .5f);
        panel.anchoredPosition = layout.panel.center;
        panel.sizeDelta = layout.panel.size;

        var h = layout.header;
        title.text = "CODEX";
        float titleW = Mathf.Ceil(title.preferredWidth) + 4f;
        CodexUi.Place(title.rectTransform, new Rect(h.xMin + 40f, h.yMin, titleW, h.height));
        CodexUi.Place(marker, CodexUi.Centered(h.xMin + 14f, h.center.y, 28f, 28f));
        CodexUi.Place(counter.rectTransform, new Rect(h.xMin + 40f + titleW + 16f, h.yMin,
                                                       h.xMax - (h.xMin + 40f + titleW + 16f), h.height));
        CodexUi.Place(divider.rectTransform, layout.divider);

        CodexUi.Place(tabsRoot, layout.tabs);
        for (int i = 0; i < Tabs.Length; i++)
        {
            float x = -layout.tabs.width * .5f + layout.tabWidth * .5f + i * (layout.tabWidth + TabGap);
            CodexUi.Place(tabFrames[i].rectTransform, CodexUi.Centered(x, 0f, layout.tabWidth, layout.tabs.height));
        }

        CodexUi.Place(grid, layout.body);
        CodexUi.Place(detail, layout.detail);
        CodexUi.Place(backSlot, layout.back);

        LayoutCards();
        LayoutDetail();
    }

    void LayoutCards()
    {
        float w = layout.cardWidth, h = layout.cardHeight;
        float gridW = layout.body.width - 2f * GridInset;
        float art = Mathf.Min(w - 36f, h - CardNameHeight - 14f);
        for (int i = 0; i < cards.Length; i++)
        {
            int col = i % layout.columns, row = i / layout.columns;
            float x = -gridW * .5f + w * .5f + col * (w + Gap);
            float y = -(GridInset + h * .5f + row * (h + Gap));
            var c = cards[i];
            c.rt.anchorMin = c.rt.anchorMax = new Vector2(.5f, 1f);
            c.rt.pivot = new Vector2(.5f, .5f);
            c.rt.anchoredPosition = new Vector2(x, y);
            c.rt.sizeDelta = new Vector2(w, h);
            CodexUi.Place(c.artBox, CodexUi.Centered(0f, h * .5f - 14f - art * .5f, art, art));
            CodexUi.Place(c.name.rectTransform, CodexUi.Centered(0f, -h * .5f + 8f + CardNameHeight * .5f, w - 20f, CardNameHeight));
            CodexUi.Place(c.lockIcon.rectTransform, CodexUi.Centered(w * .5f - 24f, h * .5f - 24f, 24f, 24f));
        }
        int rows = (shownCount + layout.columns - 1) / layout.columns;
        float height = rows <= 0 ? 0f : GridInset * 2f + rows * h + (rows - 1) * Gap;
        content.sizeDelta = new Vector2(0f, height);
    }

    void LayoutDetail()
    {
        var d = layout.detail;
        float w = d.width, top = d.height * .5f;
        float art = Mathf.Min(w * .5f, d.height * .34f, 300f);

        // Art frame flush with the top, then name, category pill, the
        // optional subtitle, and the lore card taking whatever is left.
        float y = top - 18f - art * .5f;
        CodexUi.Place(detailFrame.rectTransform, CodexUi.Centered(0f, y, art + 36f, art + 36f));
        CodexUi.Place(detailArtBox, CodexUi.Centered(0f, 0f, art, art));
        CodexUi.Place(detailIndex.rectTransform, new Rect(w * .5f - 120f, top - 30f, 120f, 30f));
        y -= art * .5f + 18f;

        y -= 12f + 30f;
        CodexUi.Place(detailName.rectTransform, CodexUi.Centered(0f, y, w - 40f, 60f));
        y -= 30f;

        y -= 10f + 18f;
        CodexUi.Place(detailPill.rectTransform, CodexUi.Centered(0f, y, 200f, 36f));
        y -= 18f;

        if (detailSubtitle.gameObject.activeSelf && detailSubtitle.text.Length > 0)
        {
            y -= 8f + 16f;
            CodexUi.Place(detailSubtitle.rectTransform, CodexUi.Centered(0f, y, w - 40f, 32f));
            y -= 16f;
        }
        CodexUi.Place(detailLoreCard.rectTransform, Rect.MinMaxRect(-w * .5f, -d.height * .5f, w * .5f, y - 16f));
    }

    // ---------------------------------------------------------------------
    // State
    // ---------------------------------------------------------------------

    void Show()
    {
        gameObject.SetActive(true);
        Codex.Reload();
        RefreshCounter();
        if (sceneBack == null)
        {
            // The home screen quits on Escape (startMenu); other menus go
            // back (backButton). Either way the codex owns Escape while open.
            Behaviour home = FindFirstObjectByType<startMenu>();
            sceneBack = home != null ? home : FindFirstObjectByType<global::backButton>();
        }
        if (sceneBack != null) sceneBack.enabled = false;

        inDetail = false;
        detailEntry = null;
        Fit();
        Populate(category);
        phase = Phase.Opening;
        phaseAt = Time.unscaledTime;
        swapAt = tabAt = -10f;
        ApplyFrame(0f);
    }

    public void Close()
    {
        if (phase == Phase.Hidden || phase == Phase.Closing) return;
        phase = Phase.Closing;
        phaseAt = Time.unscaledTime;
    }

    // Back button / Escape: detail -> grid -> closed.
    public void Back()
    {
        if (phase != Phase.Open && phase != Phase.Opening) return;
        if (inDetail) ShowGrid();
        else Close();
    }

    public void ShowCategory(CodexCategory c)
    {
        if (inDetail) ShowGrid();
        if (c == category && shownCount > 0) return;
        Populate(c);
        tabAt = Time.unscaledTime;
    }

    public void ShowDetail(CodexEntry entry)
    {
        if (entry == null) return;
        detailEntry = entry;
        bool found = Codex.IsDiscovered(entry);

        detailName.text = Codex.DisplayName(entry);
        detailPillLabel.text = CategoryLabel(entry.category);
        detailPill.color = found ? CodexUi.Accent : CodexUi.Locked;
        string sub = found ? entry.Subtitle : null;
        detailSubtitle.text = sub ?? string.Empty;
        detailSubtitle.gameObject.SetActive(sub != null);
        detailLore.text = found ? Codex.DisplayLore(entry) : LockedHint;
        detailLore.color = found ? CodexUi.Body : CodexUi.Muted;
        detailLore.alignment = found ? TextAnchor.UpperLeft : TextAnchor.MiddleCenter;
        detailLoreEdge.color = found ? CodexUi.Accent : CodexUi.Locked;
        detailFrameEdge.color = found ? CodexUi.Accent : CodexUi.Locked;

        int number = Array.IndexOf(Codex.Entries, entry) + 1;
        detailIndex.text = "No. " + number.ToString("00");

        ApplyArt(detailArt, detailMask, detailMaskComp, entry, found);
        LayoutDetail();

        inDetail = true;
        swapAt = Time.unscaledTime;
    }

    public void ShowGrid()
    {
        if (!inDetail) return;
        inDetail = false;
        swapAt = Time.unscaledTime;
        // Unlocks can't change while the panel is open, but names can be
        // re-read cheaply here if they ever do.
    }

    void OnCardClicked(int index)
    {
        if (index < 0 || index >= shownCount || inDetail) return;
        ShowDetail(cards[index].entry);
    }

    void Populate(CodexCategory c)
    {
        category = c;
        shownCount = 0;
        foreach (var e in Codex.Entries)
        {
            if (e.category != c || shownCount >= cards.Length || !Codex.IsListed(e)) continue;
            var card = cards[shownCount++];
            card.entry = e;
            bool found = Codex.IsDiscovered(e);
            card.name.text = Codex.DisplayName(e);
            card.name.color = found ? CodexUi.Body : CodexUi.Muted;
            card.edge.color = found ? CodexUi.Accent : CodexUi.Locked;
            card.lockIcon.gameObject.SetActive(!found);
            ApplyArt(card.art, card.mask, card.maskComp, e, found);
        }
        for (int i = 0; i < cards.Length; i++)
            cards[i].rt.gameObject.SetActive(i < shownCount);

        for (int i = 0; i < Tabs.Length; i++)
        {
            bool active = Tabs[i] == c;
            // Kaneda red marks the selection; idle tabs sit in the shadow tone.
            tabFrames[i].color = active ? CodexUi.Select : CodexUi.Idle;
            tabLabels[i].color = active ? CodexUi.Body : CodexUi.Muted;
        }

        LayoutCards();
        content.anchoredPosition = Vector2.zero;
        scroll.velocity = Vector2.zero;
    }

    static void ApplyArt(Image art, Image mask, Mask maskComp, CodexEntry e, bool found)
    {
        art.sprite = e.Sprite;
        art.enabled = art.sprite != null;
        // Locked: an ink blackout of the sprite's own shape.
        art.color = found ? Color.white : CodexUi.Silhouette;
        maskComp.enabled = e.round;
        mask.enabled = e.round;
    }

    // Developer mode reveals (or hides again) every entry; an open panel
    // redraws in place, keeping the tab and any open detail.
    static void OnDeveloperModeChanged()
    {
        if (instance != null) instance.Refresh();
    }

    public void Refresh()
    {
        if (phase == Phase.Hidden) return;
        RefreshCounter();
        var open = inDetail ? detailEntry : null;
        Populate(category);
        if (open != null)
        {
            ShowDetail(open);
            swapAt = -100f;
        }
        ApplyFrame(Time.unscaledTime);
    }

    void RefreshCounter()
    {
        counter.text = Codex.DiscoveredCount + " / " + Codex.Total + "  DISCOVERED";
    }

    // ---------------------------------------------------------------------
    // Animation
    // ---------------------------------------------------------------------

    void Update()
    {
        if (Screen.width != lastW || Screen.height != lastH || Screen.safeArea != lastSafe) Fit();
        if (Input.GetKeyDown(KeyCode.Escape)) Back();
        ApplyFrame(Time.unscaledTime);
    }

    // Jump every running transition to its end (tests, and a tap mid-intro
    // never has to wait).
    public void SkipAnimations()
    {
        phaseAt = -100f;
        swapAt = tabAt = -100f;
        ApplyFrame(Time.unscaledTime);
    }

    void ApplyFrame(float now)
    {
        float open;
        if (phase == Phase.Opening)
        {
            float p = Mathf.Clamp01((now - phaseAt) / OpenDuration);
            open = p;
            if (p >= 1f) phase = Phase.Open;
        }
        else if (phase == Phase.Closing)
        {
            float p = Mathf.Clamp01((now - phaseAt) / CloseDuration);
            open = 1f - p;
            if (p >= 1f)
            {
                phase = Phase.Hidden;
                if (sceneBack != null) sceneBack.enabled = true;
                gameObject.SetActive(false);
                var handler = Closed;
                if (handler != null) handler();
                return;
            }
        }
        else open = phase == Phase.Open ? 1f : 0f;

        CodexUi.SetAlpha(scrim, CodexPalette.Scrim.a * CodexUi.EaseOutCubic(open));
        panelGroup.alpha = CodexUi.EaseOutCubic(open * 1.4f);
        float s = phase == Phase.Closing
            ? Mathf.Lerp(.94f, 1f, open)
            : Mathf.LerpUnclamped(.9f, 1f, CodexUi.EaseOutBack(open));
        panel.localScale = new Vector3(s, s, 1f);
        panelGroup.interactable = phase == Phase.Open || phase == Phase.Opening;
        panelGroup.blocksRaycasts = phase != Phase.Hidden;

        // Grid <-> detail cross-slide.
        float swap = CodexUi.EaseOutCubic((now - swapAt) / SwapDuration);
        float d = inDetail ? swap : 1f - swap;   // 1 = detail fully shown
        detailGroup.alpha = d;
        detailGroup.interactable = inDetail;
        detailGroup.blocksRaycasts = inDetail;
        detail.anchoredPosition = layout.detail.center + new Vector2((1f - d) * 48f, 0f);

        float tab = CodexUi.EaseOutCubic((now - tabAt) / TabFadeDuration);
        gridGroup.alpha = (1f - d) * tab;
        gridGroup.interactable = !inDetail;
        gridGroup.blocksRaycasts = !inDetail;
        grid.anchoredPosition = layout.body.center + new Vector2(-d * 48f, (1f - tab) * -18f);
        // The detail view takes over the tab row's space too.
        tabsGroup.alpha = 1f - d;
        tabsGroup.interactable = !inDetail;
        tabsGroup.blocksRaycasts = !inDetail;

    }
}
