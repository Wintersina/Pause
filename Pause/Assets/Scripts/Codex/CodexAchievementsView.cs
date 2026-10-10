using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// The ACHIEVEMENTS tab of the codex: a header strip ("12/58 UNLOCKED", what
// is waiting to be collected, the star dust balance, COLLECT ALL), jump chips
// and one scrolling list of badge cards in seven sections (CodexAchievements
// layout mirrors the ENEMIES list: pinned section header, smooth jump).
//
// A card is LOCKED (dark badge, padlock, a progress bar for counters),
// UNLOCKED (gold edge and glow, a pulsing COLLECT 25 chip) or CLAIMED (full
// colour, "COLLECTED"). Tapping a card opens its detail with the same states
// and a big COLLECT button. A hidden achievement reads "???" until earned; the
// dormant Tide ones are not listed at all (AchievementCatalog.IsActive).
//
// Touch: every button is a real Button; each sits on its own isolated
// sub-canvas WITH a GraphicRaycaster (a graphic on a nested canvas is only
// seen by a raycaster on that canvas), so a pulsing chip never rebuilds the
// list's big canvas. Nothing allocates per frame: the pulse is arithmetic on
// the visible chips' scale.
public sealed class CodexAchievementsView
{
    public const float StripHeight = 112f, StripGap = 10f;
    public const float CardInset = 30f;                // badge margin inside a card (each side)
    public const float CardNameHeight = 48f, CardBottomHeight = 64f;
    public const float ChipHeight = 56f;               // COLLECT chip on a card; the hit area adds the raycast padding
    public const float ButtonPad = 12f;                // raycast padding around every button
    public const float CollectAllWidth = 250f;
    public const float PulseHz = 1.1f, PulseScale = .07f;
    public const float CountUpSeconds = .55f;
    public const string HiddenName = "???";
    public const string HiddenHint = "A hidden achievement.\nKeep flying - it's out there somewhere.";

    // ---- tint roles ----
    static Color Gold { get { return CodexPalette.Amber; } }
    static readonly Color LockedBadge = new Color(.58f, .60f, .70f, 1f); // dimmed but still readable (was .26/.28/.36)

    sealed class Card
    {
        public RectTransform rt;
        public Button button;
        public Image frame, edge, glow, badge, lockIcon, barBack, barFill;
        public Text name, barText, tierLabel, collectedLabel;
        public ChipButton collect;
        public AchievementDef def;
    }

    // A button on its own sub-canvas + raycaster, with a label.
    public sealed class ChipButton
    {
        public RectTransform rt;
        public Image frame;
        public Text label;
        public Button button;
        public Canvas canvas;
    }

    sealed class SectionBar
    {
        public RectTransform rt;
        public Image frame;
        public Text label, counter;
    }

    sealed class Chip
    {
        public RectTransform rt;
        public Image frame;
        public Text label;
        public Button button;
    }

    public sealed class Section
    {
        public AchievementSection kind;
        public string label;
        public Color color;
        public readonly List<AchievementDef> defs = new List<AchievementDef>();
        public int unlocked;
        public string counter;
    }

    readonly CodexPanel panel;
    readonly Font font;
    RectTransform root;
    CanvasGroup rootGroup;

    // strip
    RectTransform strip;
    Image stripFrame;
    Text stripCount, stripLine;
    ChipButton collectAll;

    // chips + list
    RectTransform chipsRoot, grid, viewport, content;
    ScrollRect scroll;
    Chip[] chips;
    SectionBar[] headers;
    SectionBar sticky;
    Card[] cards;

    // detail
    RectTransform detail;
    CanvasGroup detailGroup;
    Image dFrame, dFrameEdge, dBadge, dLock, dBarBack, dBarFill, dPill, dLoreCard, dLoreEdge;
    Text dName, dPillLabel, dLore, dBarText, dReward, dCollected;
    ChipButton dCollect;
    RectTransform dBadgeBox;
    AchievementDef detailDef;

    List<Section> sections = new List<Section>();
    readonly float[] sectionTop = new float[CodexPanel.MaxSections];
    readonly int[] sectionStart = new int[CodexPanel.MaxSections];
    int shownCount;
    int stickySection = -2, activeChip = -2, jumpTarget = -1;
    bool jumping;
    float jumpAt, jumpFrom, jumpTo, lastSetY;
    Rect body, stripRect, chipsRect, listRect;
    float cardW, cardH;
    int columns;

    // balance count-up
    float countFrom, countTo, countAt;
    bool counting;
    int countShown = -1;

    public CodexAchievementsView(CodexPanel panel, RectTransform parent, Font font)
    {
        this.panel = panel;
        this.font = font;
        Build(parent);
    }

    // ---- accessors for the tests ----
    public RectTransform Root { get { return root; } }
    public bool Active { get { return root.gameObject.activeSelf; } }
    public bool InDetail { get { return detailDef != null; } }
    public AchievementDef DetailDef { get { return detailDef; } }
    public int VisibleCards { get { return shownCount; } }
    public AchievementDef CardDef(int i) { return cards[i].def; }
    public Button CardButton(int i) { return cards[i].button; }
    public Text CardName(int i) { return cards[i].name; }
    public RectTransform CardRect(int i) { return cards[i].rt; }
    public ChipButton CardCollect(int i) { return cards[i].collect; }
    public bool CardCollectVisible(int i) { return cards[i].collect.rt.gameObject.activeSelf; }
    public Image CardBadge(int i) { return cards[i].badge; }
    public Image CardLock(int i) { return cards[i].lockIcon; }
    public Image CardEdge(int i) { return cards[i].edge; }
    public Text CardTier(int i) { return cards[i].tierLabel; }
    public Text CardCollected(int i) { return cards[i].collectedLabel; }
    public Image CardBarFill(int i) { return cards[i].barFill; }
    public Text CardBarText(int i) { return cards[i].barText; }
    public bool CardBarVisible(int i) { return cards[i].barBack.gameObject.activeSelf; }
    public int CardIndexOf(string id)
    {
        for (int i = 0; i < shownCount; i++) if (cards[i].def.id == id) return i;
        return -1;
    }
    public ChipButton CollectAll { get { return collectAll; } }
    public Text StripCount { get { return stripCount; } }
    public Text StripLine { get { return stripLine; } }
    public RectTransform StripRect { get { return strip; } }
    public int SectionCount { get { return sections.Count; } }
    public Section SectionAt(int i) { return sections[i]; }
    public float SectionTop(int i) { return sectionTop[i]; }
    public int SectionStart(int i) { return sectionStart[i]; }
    public Button ChipButtonAt(int i) { return chips[i].button; }
    public RectTransform ChipRect(int i) { return chips[i].rt; }
    public Text ChipLabel(int i) { return chips[i].label; }
    public Text SectionLabel(int i) { return headers[i].label; }
    public Text SectionCounter(int i) { return headers[i].counter; }
    public RectTransform SectionHeaderRect(int i) { return headers[i].rt; }
    public RectTransform Viewport { get { return viewport; } }
    public float ScrollY { get { return content.anchoredPosition.y; } }
    public float ContentHeight { get { return content.sizeDelta.y; } }
    public float MaxScroll { get { return Mathf.Max(0f, content.sizeDelta.y - viewport.rect.height); } }
    public int StickySection { get { return stickySection; } }
    public Rect ListRect { get { return listRect; } }
    public Rect StripRectLocal { get { return stripRect; } }
    public Rect ChipsRectLocal { get { return chipsRect; } }
    public int Columns { get { return columns; } }
    public float CardWidth { get { return cardW; } }
    public float CardHeight { get { return cardH; } }
    public ChipButton DetailCollect { get { return dCollect; } }
    public Text DetailName { get { return dName; } }
    public Text DetailDescription { get { return dLore; } }
    public Text DetailReward { get { return dReward; } }
    public Text DetailCollected { get { return dCollected; } }
    public Text DetailBarText { get { return dBarText; } }
    public Text DetailPillLabel { get { return dPillLabel; } }
    public Image DetailBadge { get { return dBadge; } }
    public RectTransform DetailRoot { get { return detail; } }
    public float ShownBalance { get { return countShown; } }

    // ---------------------------------------------------------------------
    // Building
    // ---------------------------------------------------------------------

    void Build(RectTransform parent)
    {
        root = CodexUi.NewRect("Achievements", parent);
        rootGroup = root.gameObject.AddComponent<CanvasGroup>();

        BuildStrip();
        BuildChips();
        BuildList();
        BuildDetail();
        root.gameObject.SetActive(false);
    }

    ChipButton NewChipButton(string name, Transform parent, string text, int size, int minSize)
    {
        var b = new ChipButton();
        b.frame = CodexUi.NewImage(name, parent, CodexUi.CodexSprite("cx_tab"), Gold, true);
        b.frame.raycastTarget = true;
        b.frame.raycastPadding = new Vector4(-ButtonPad, -ButtonPad, -ButtonPad, -ButtonPad);
        b.rt = b.frame.rectTransform;
        b.button = b.frame.gameObject.AddComponent<Button>();
        b.button.transition = Selectable.Transition.None;
        b.button.targetGraphic = b.frame;
        b.label = CodexUi.NewText("Label", b.rt, font, text, size, CodexUi.Ink, TextAnchor.MiddleCenter);
        b.label.horizontalOverflow = HorizontalWrapMode.Wrap;
        b.label.verticalOverflow = VerticalWrapMode.Truncate;
        b.label.resizeTextForBestFit = true;
        b.label.resizeTextMinSize = minSize;
        b.label.resizeTextMaxSize = size;
        var lrt = b.label.rectTransform;
        CodexUi.Stretch(lrt);
        lrt.offsetMin = new Vector2(8f, 0f);
        lrt.offsetMax = new Vector2(-8f, 0f);
        // Its own canvas (so a pulse rebuilds only this chip) and its own raycaster
        // (a graphic on a nested canvas is invisible to the parent canvas's).
        b.canvas = CodexUi.Isolate(b.frame.gameObject);
        b.frame.gameObject.AddComponent<GraphicRaycaster>();
        return b;
    }

    void BuildStrip()
    {
        strip = CodexUi.NewRect("Strip", root);
        stripFrame = CodexUi.NewImage("Frame", strip, CodexUi.CodexSprite("cx_card"), Color.white, true);
        CodexUi.Stretch(stripFrame.rectTransform);
        var edge = CodexUi.NewImage("Edge", strip, CodexUi.CodexSprite("cx_card_edge"), CodexUi.Accent, true);
        CodexUi.Stretch(edge.rectTransform);

        stripCount = CodexUi.NewText("Count", strip, font, "", 30, CodexUi.Title, TextAnchor.MiddleLeft);
        stripCount.horizontalOverflow = HorizontalWrapMode.Wrap;
        stripCount.verticalOverflow = VerticalWrapMode.Truncate;
        stripCount.resizeTextForBestFit = true;
        stripCount.resizeTextMinSize = 16;
        stripCount.resizeTextMaxSize = 30;
        CodexUi.AddOutline(stripCount.gameObject, CodexUi.Ink, 2f);

        stripLine = CodexUi.NewText("Line", strip, font, "", 18, CodexUi.Body, TextAnchor.MiddleLeft);
        stripLine.horizontalOverflow = HorizontalWrapMode.Wrap;
        stripLine.verticalOverflow = VerticalWrapMode.Truncate;
        stripLine.resizeTextForBestFit = true;
        stripLine.resizeTextMinSize = 11;
        stripLine.resizeTextMaxSize = 18;
        CodexUi.AddOutline(stripLine.gameObject, CodexUi.Ink, 1.5f);

        collectAll = NewChipButton("CollectAll", strip, "COLLECT ALL", 24, 13);
        collectAll.button.onClick.AddListener(OnCollectAll);
    }

    void BuildChips()
    {
        chipsRoot = CodexUi.NewRect("Chips", root);
        chips = new Chip[CodexPanel.MaxSections];
        for (int i = 0; i < chips.Length; i++)
        {
            int index = i;
            var chip = new Chip();
            chip.frame = CodexUi.NewImage("Chip" + i, chipsRoot, CodexUi.CodexSprite("cx_tab"), CodexUi.Idle, true);
            chip.frame.raycastTarget = true;
            chip.rt = chip.frame.rectTransform;
            chip.button = chip.frame.gameObject.AddComponent<Button>();
            chip.button.transition = Selectable.Transition.None;
            chip.button.targetGraphic = chip.frame;
            chip.button.onClick.AddListener(() => JumpToSection(index));
            chip.label = CodexUi.NewText("Label", chip.rt, font, "", 18, Color.white, TextAnchor.MiddleCenter);
            chip.label.horizontalOverflow = HorizontalWrapMode.Wrap;
            chip.label.verticalOverflow = VerticalWrapMode.Truncate;
            chip.label.resizeTextForBestFit = true;
            chip.label.resizeTextMinSize = 10;
            chip.label.resizeTextMaxSize = 18;
            var lrt = chip.label.rectTransform;
            CodexUi.Stretch(lrt);
            lrt.offsetMin = new Vector2(4f, 0f);
            lrt.offsetMax = new Vector2(-4f, 0f);
            chips[i] = chip;
        }
    }

    SectionBar BuildSectionBar(string name, Transform parent, bool blocksTouches)
    {
        var bar = new SectionBar();
        bar.frame = CodexUi.NewImage(name, parent, CodexUi.CodexSprite("cx_tab"), Color.white, true);
        bar.frame.raycastTarget = blocksTouches;
        bar.rt = bar.frame.rectTransform;
        bar.label = CodexUi.NewText("Label", bar.rt, font, "", 26, CodexUi.Ink, TextAnchor.MiddleLeft);
        bar.label.fontStyle = FontStyle.BoldAndItalic;
        bar.label.horizontalOverflow = HorizontalWrapMode.Wrap;
        bar.label.verticalOverflow = VerticalWrapMode.Truncate;
        bar.label.resizeTextForBestFit = true;
        bar.label.resizeTextMinSize = 14;
        bar.label.resizeTextMaxSize = 26;
        var lrt = bar.label.rectTransform;
        CodexUi.Stretch(lrt);
        lrt.offsetMin = new Vector2(28f, 0f);
        lrt.offsetMax = new Vector2(-124f, 0f);
        bar.counter = CodexUi.NewText("Counter", bar.rt, font, "", 22, CodexUi.Ink, TextAnchor.MiddleRight);
        var crt = bar.counter.rectTransform;
        crt.anchorMin = new Vector2(1f, 0f);
        crt.anchorMax = new Vector2(1f, 1f);
        crt.pivot = new Vector2(1f, .5f);
        crt.anchoredPosition = new Vector2(-28f, 0f);
        crt.sizeDelta = new Vector2(96f, 0f);
        return bar;
    }

    void BuildList()
    {
        var go = new GameObject("Grid", typeof(RectTransform), typeof(ScrollRect));
        go.transform.SetParent(root, false);
        grid = (RectTransform)go.transform;

        viewport = CodexUi.NewRect("Viewport", grid);
        CodexUi.Stretch(viewport);
        viewport.gameObject.AddComponent<RectMask2D>();
        var hit = viewport.gameObject.AddComponent<Image>();   // drags that start between cards hit this
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
        scroll.onValueChanged.AddListener(OnScrolled);

        cards = new Card[AchievementCatalog.Count];
        for (int i = 0; i < cards.Length; i++) cards[i] = BuildCard(i);
        headers = new SectionBar[CodexPanel.MaxSections];
        for (int i = 0; i < headers.Length; i++) headers[i] = BuildSectionBar("Section" + i, content, false);
        sticky = BuildSectionBar("StickyHeader", viewport, true);
        var srt = sticky.rt;
        srt.anchorMin = new Vector2(0f, 1f);
        srt.anchorMax = new Vector2(1f, 1f);
        srt.pivot = new Vector2(.5f, 1f);
        srt.offsetMin = new Vector2(CodexPanel.GridInset, -CodexPanel.SectionHeaderHeight);
        srt.offsetMax = new Vector2(-CodexPanel.GridInset, 0f);
        sticky.rt.gameObject.SetActive(false);
    }

    Card BuildCard(int index)
    {
        var c = new Card();
        c.frame = CodexUi.NewImage("Card" + index, content, CodexUi.CodexSprite("cx_card"), Color.white, true);
        c.frame.raycastTarget = true;
        c.rt = c.frame.rectTransform;
        c.button = c.frame.gameObject.AddComponent<Button>();
        c.button.targetGraphic = c.frame;
        var colors = c.button.colors;
        colors.pressedColor = CodexUi.Select;
        colors.highlightedColor = Color.white;
        colors.fadeDuration = .08f;
        c.button.colors = colors;
        c.button.onClick.AddListener(() => OnCardClicked(index));
        c.edge = CodexUi.NewImage("Edge", c.rt, CodexUi.CodexSprite("cx_card_edge"), CodexUi.Locked, true);
        CodexUi.Stretch(c.edge.rectTransform);

        c.glow = CodexUi.NewImage("Glow", c.rt, CodexUi.CodexSprite("cx_circle"), new Color(Gold.r, Gold.g, Gold.b, .28f));
        c.badge = CodexUi.NewImage("Badge", c.rt, null, Color.white);
        c.badge.preserveAspect = true;
        c.lockIcon = CodexUi.NewImage("Lock", c.rt, CodexUi.CodexSprite("cx_lock"), CodexUi.Select);
        c.lockIcon.preserveAspect = true;

        c.name = CodexUi.NewText("Name", c.rt, font, "", 18, CodexUi.Body, TextAnchor.MiddleCenter, true);
        c.name.verticalOverflow = VerticalWrapMode.Truncate;
        c.name.resizeTextForBestFit = true;
        c.name.resizeTextMinSize = 11;
        c.name.resizeTextMaxSize = 18;
        CodexUi.AddOutline(c.name.gameObject, CodexUi.Ink, 1.5f);

        c.barBack = CodexUi.NewImage("BarBack", c.rt, CodexUi.CodexSprite("cx_tab"), CodexUi.Idle, true);
        c.barFill = CodexUi.NewImage("BarFill", c.barBack.rectTransform, CodexUi.CodexSprite("cx_tab"), CodexUi.Accent, true);
        c.barFill.rectTransform.pivot = new Vector2(0f, .5f);
        c.barText = CodexUi.NewText("BarText", c.barBack.rectTransform, font, "", 16, CodexUi.Body, TextAnchor.MiddleCenter);
        c.barText.resizeTextForBestFit = true;
        c.barText.resizeTextMinSize = 10;
        c.barText.resizeTextMaxSize = 16;
        CodexUi.AddOutline(c.barText.gameObject, CodexUi.Ink, 1.5f);
        CodexUi.Stretch(c.barText.rectTransform);

        c.tierLabel = CodexUi.NewText("Tier", c.rt, font, "", 18, CodexUi.Muted, TextAnchor.MiddleCenter);
        c.tierLabel.resizeTextForBestFit = true;
        c.tierLabel.resizeTextMinSize = 10;
        c.tierLabel.resizeTextMaxSize = 18;
        c.collectedLabel = CodexUi.NewText("Collected", c.rt, font, "COLLECTED", 18, CodexUi.Accent, TextAnchor.MiddleCenter);
        c.collectedLabel.resizeTextForBestFit = true;
        c.collectedLabel.resizeTextMinSize = 10;
        c.collectedLabel.resizeTextMaxSize = 18;
        CodexUi.AddOutline(c.collectedLabel.gameObject, CodexUi.Ink, 1.5f);

        c.collect = NewChipButton("Collect", c.rt, "COLLECT " + AchievementCatalog.RewardDust, 22, 11);
        int idx = index;
        c.collect.button.onClick.AddListener(() => OnCollectCard(idx));
        return c;
    }

    void BuildDetail()
    {
        var go = new GameObject("Detail", typeof(RectTransform), typeof(CanvasGroup));
        go.transform.SetParent(root, false);
        detail = (RectTransform)go.transform;
        detailGroup = go.GetComponent<CanvasGroup>();

        // an opaque plate so the list underneath never shows through, and takes touches
        var plate = CodexUi.NewImage("Plate", detail, null, new Color(CodexPalette.Night1.r, CodexPalette.Night1.g, CodexPalette.Night1.b, 1f));
        plate.raycastTarget = true;
        CodexUi.Stretch(plate.rectTransform);

        dFrame = CodexUi.NewImage("ArtFrame", detail, CodexUi.CodexSprite("cx_card"), Color.white, true);
        dFrameEdge = CodexUi.NewImage("Edge", dFrame.rectTransform, CodexUi.CodexSprite("cx_card_edge"), CodexUi.Accent, true);
        CodexUi.Stretch(dFrameEdge.rectTransform);
        dBadge = CodexUi.NewImage("Badge", dFrame.rectTransform, null, Color.white);
        dBadge.preserveAspect = true;
        dBadgeBox = dBadge.rectTransform;
        dLock = CodexUi.NewImage("Lock", dFrame.rectTransform, CodexUi.CodexSprite("cx_lock"), CodexUi.Select);
        dLock.preserveAspect = true;

        dName = CodexUi.NewText("Name", detail, font, "", 40, CodexUi.Title, TextAnchor.MiddleCenter, true);
        dName.verticalOverflow = VerticalWrapMode.Truncate;
        dName.resizeTextForBestFit = true;
        dName.resizeTextMinSize = 22;
        dName.resizeTextMaxSize = 40;
        dName.fontStyle = FontStyle.BoldAndItalic;
        CodexUi.AddOutline(dName.gameObject, CodexUi.Ink, 3f);

        dPill = CodexUi.NewImage("Pill", detail, CodexUi.CodexSprite("cx_tab"), CodexUi.Accent, true);
        dPillLabel = CodexUi.NewText("Label", dPill.rectTransform, font, "", 18, CodexUi.Ink, TextAnchor.MiddleCenter);
        CodexUi.Stretch(dPillLabel.rectTransform);

        dLoreCard = CodexUi.NewImage("LoreCard", detail, CodexUi.CodexSprite("cx_card"), Color.white, true);
        dLoreEdge = CodexUi.NewImage("Edge", dLoreCard.rectTransform, CodexUi.CodexSprite("cx_card_edge"), CodexUi.Accent, true);
        CodexUi.Stretch(dLoreEdge.rectTransform);
        dLore = CodexUi.NewText("Lore", dLoreCard.rectTransform, font, "", 26, CodexUi.Body, TextAnchor.MiddleCenter, true);
        dLore.fontStyle = FontStyle.Normal;
        dLore.lineSpacing = 1.15f;
        dLore.verticalOverflow = VerticalWrapMode.Truncate;
        dLore.resizeTextForBestFit = true;
        dLore.resizeTextMinSize = 16;
        dLore.resizeTextMaxSize = 26;
        var lrt = dLore.rectTransform;
        CodexUi.Stretch(lrt);
        lrt.offsetMin = new Vector2(24f, 16f);
        lrt.offsetMax = new Vector2(-24f, -16f);

        dBarBack = CodexUi.NewImage("BarBack", detail, CodexUi.CodexSprite("cx_tab"), CodexUi.Idle, true);
        dBarFill = CodexUi.NewImage("BarFill", dBarBack.rectTransform, CodexUi.CodexSprite("cx_tab"), CodexUi.Accent, true);
        dBarFill.rectTransform.pivot = new Vector2(0f, .5f);
        dBarText = CodexUi.NewText("BarText", dBarBack.rectTransform, font, "", 22, CodexUi.Body, TextAnchor.MiddleCenter);
        dBarText.resizeTextForBestFit = true;
        dBarText.resizeTextMinSize = 12;
        dBarText.resizeTextMaxSize = 22;
        CodexUi.AddOutline(dBarText.gameObject, CodexUi.Ink, 1.5f);
        CodexUi.Stretch(dBarText.rectTransform);

        dReward = CodexUi.NewText("Reward", detail, font, "", 22, CodexUi.Title, TextAnchor.MiddleCenter);
        dReward.resizeTextForBestFit = true;
        dReward.resizeTextMinSize = 12;
        dReward.resizeTextMaxSize = 22;
        CodexUi.AddOutline(dReward.gameObject, CodexUi.Ink, 1.5f);

        dCollected = CodexUi.NewText("Collected", detail, font, "COLLECTED", 30, CodexUi.Accent, TextAnchor.MiddleCenter);
        dCollected.resizeTextForBestFit = true;
        dCollected.resizeTextMinSize = 14;
        dCollected.resizeTextMaxSize = 30;
        CodexUi.AddOutline(dCollected.gameObject, CodexUi.Ink, 2f);

        dCollect = NewChipButton("Collect", detail, "COLLECT " + AchievementCatalog.RewardDust, 34, 16);
        dCollect.button.onClick.AddListener(OnCollectDetail);
        detail.gameObject.SetActive(false);
    }

    // ---------------------------------------------------------------------
    // Layout
    // ---------------------------------------------------------------------

    // `bodyRect` is the codex body (panel-local); the view fills it.
    public void ApplyLayout(Rect bodyRect, CodexPanel.Layout l)
    {
        body = bodyRect;
        CodexUi.Place(root, bodyRect);
        float w = bodyRect.width, h = bodyRect.height;
        stripRect = new Rect(-w * .5f, h * .5f - StripHeight, w, StripHeight);
        chipsRect = new Rect(-w * .5f, stripRect.yMin - StripGap - CodexPanel.ChipsHeight, w, CodexPanel.ChipsHeight);
        listRect = Rect.MinMaxRect(-w * .5f, -h * .5f, w * .5f, chipsRect.yMin - CodexPanel.ChipsGap);

        CodexUi.Place(strip, stripRect);
        float pad = 20f;
        float btnW = Mathf.Min(CollectAllWidth, w * .42f), btnH = 72f;
        CodexUi.Place(collectAll.rt, CodexUi.Centered(w * .5f - pad - btnW * .5f, 0f, btnW, btnH));
        float textW = w - btnW - pad * 3f;
        CodexUi.Place(stripCount.rectTransform, new Rect(-w * .5f + pad + 8f, 2f, textW - 8f, StripHeight * .5f - 12f));
        CodexUi.Place(stripLine.rectTransform, new Rect(-w * .5f + pad + 8f, -StripHeight * .5f + 10f, textW - 8f, StripHeight * .5f - 14f));

        CodexUi.Place(chipsRoot, chipsRect);
        CodexUi.Place(grid, listRect);
        CodexUi.Place(detail, new Rect(-w * .5f, -h * .5f, w, h));

        // card geometry: the codex grid's column rule, taller cards (name + bottom row)
        columns = l.columns;
        float gridW = w - 2f * CodexPanel.GridInset;
        cardW = (gridW - CodexPanel.Gap * (columns - 1)) / columns;
        float badge = BadgeSize();
        cardH = 14f + badge + 6f + CardNameHeight + 4f + CardBottomHeight + 8f;
        LayoutCards();
        LayoutDetail();
    }

    float BadgeSize() { return Mathf.Max(60f, cardW - 2f * CardInset); }

    void LayoutCards()
    {
        float gridW = body.width - 2f * CodexPanel.GridInset;
        float badge = BadgeSize();
        float y = CodexPanel.GridInset;
        int sectionCount = Mathf.Min(sections.Count, CodexPanel.MaxSections);
        for (int s = 0; s < sectionCount; s++)
        {
            if (s > 0) y += CodexPanel.SectionGap;
            sectionTop[s] = y;
            var hr = headers[s].rt;
            hr.anchorMin = hr.anchorMax = new Vector2(.5f, 1f);
            hr.pivot = new Vector2(.5f, 1f);
            hr.anchoredPosition = new Vector2(0f, -y);
            hr.sizeDelta = new Vector2(gridW, CodexPanel.SectionHeaderHeight);
            y += CodexPanel.SectionHeaderHeight + CodexPanel.SectionHeaderGap;
            int n = sections[s].defs.Count;
            for (int k = 0; k < n; k++)
            {
                int i = sectionStart[s] + k;
                if (i >= cards.Length) break;
                int col = k % columns, row = k / columns;
                var c = cards[i];
                c.rt.anchorMin = c.rt.anchorMax = new Vector2(.5f, 1f);
                c.rt.pivot = new Vector2(.5f, .5f);
                c.rt.anchoredPosition = new Vector2(-gridW * .5f + cardW * .5f + col * (cardW + CodexPanel.Gap),
                                                    -(y + cardH * .5f + row * (cardH + CodexPanel.Gap)));
                c.rt.sizeDelta = new Vector2(cardW, cardH);
            }
            int rows = (n + columns - 1) / columns;
            if (rows > 0) y += rows * cardH + (rows - 1) * CodexPanel.Gap;
        }
        for (int i = 0; i < cards.Length; i++)
        {
            var c = cards[i];
            float top = cardH * .5f - 14f - badge * .5f;
            CodexUi.Place(c.badge.rectTransform, CodexUi.Centered(0f, top, badge, badge));
            CodexUi.Place(c.glow.rectTransform, CodexUi.Centered(0f, top, badge * 1.12f, badge * 1.12f));
            float lockSize = Mathf.Clamp(badge * .3f, 28f, 48f);
            CodexUi.Place(c.lockIcon.rectTransform, CodexUi.Centered(cardW * .5f - 20f - lockSize * .5f, cardH * .5f - 20f - lockSize * .5f, lockSize, lockSize));
            float nameY = cardH * .5f - 14f - badge - 6f - CardNameHeight * .5f;
            CodexUi.Place(c.name.rectTransform, CodexUi.Centered(0f, nameY, cardW - 20f, CardNameHeight));
            float bottomY = -cardH * .5f + 8f + CardBottomHeight * .5f;
            float barW = cardW - 36f;
            CodexUi.Place(c.barBack.rectTransform, CodexUi.Centered(0f, bottomY, barW, 30f));
            CodexUi.Place(c.tierLabel.rectTransform, CodexUi.Centered(0f, bottomY, cardW - 24f, 30f));
            CodexUi.Place(c.collectedLabel.rectTransform, CodexUi.Centered(0f, bottomY, cardW - 24f, 30f));
            CodexUi.Place(c.collect.rt, CodexUi.Centered(0f, bottomY, cardW - 36f, ChipHeight));
        }
        content.sizeDelta = new Vector2(0f, y + CodexPanel.GridInset);

        int chipCount = Mathf.Min(sections.Count, CodexPanel.MaxSections);
        if (chipCount > 0)
        {
            float cw = (chipsRect.width - CodexPanel.ChipGap * (chipCount - 1) - 0f) / chipCount;
            for (int i = 0; i < chipCount; i++)
            {
                float x = -chipsRect.width * .5f + cw * .5f + i * (cw + CodexPanel.ChipGap);
                CodexUi.Place(chips[i].rt, CodexUi.Centered(x, 0f, cw, chipsRect.height));
            }
        }
        stickySection = -2;
        UpdateSticky();
    }

    void LayoutDetail()
    {
        float w = body.width, h = body.height;
        float art = Mathf.Min(w * .46f, h * .30f, 280f);
        float y = h * .5f - 14f - art * .5f - 18f;
        CodexUi.Place(dFrame.rectTransform, CodexUi.Centered(0f, y, art + 36f, art + 36f));
        CodexUi.Place(dBadge.rectTransform, CodexUi.Centered(0f, 0f, art, art));
        float lockSize = Mathf.Clamp(art * .22f, 30f, 56f);
        CodexUi.Place(dLock.rectTransform, CodexUi.Centered((art + 36f) * .5f - 14f - lockSize * .5f, (art + 36f) * .5f - 14f - lockSize * .5f, lockSize, lockSize));
        y -= art * .5f + 18f + 12f + 28f;
        CodexUi.Place(dName.rectTransform, CodexUi.Centered(0f, y, w - 40f, 56f));
        y -= 28f + 10f + 18f;
        CodexUi.Place(dPill.rectTransform, CodexUi.Centered(0f, y, Mathf.Min(360f, w - 40f), 36f));
        y -= 18f + 14f;
        // the action row at the bottom: reward line above the button
        float btnH = 92f;
        float bottom = -h * .5f + 10f;
        CodexUi.Place(dCollect.rt, CodexUi.Centered(0f, bottom + btnH * .5f, Mathf.Min(420f, w - 60f), btnH));
        CodexUi.Place(dCollected.rectTransform, CodexUi.Centered(0f, bottom + btnH * .5f, Mathf.Min(420f, w - 60f), btnH));
        float rewardY = bottom + btnH + 10f + 18f;
        CodexUi.Place(dReward.rectTransform, CodexUi.Centered(0f, rewardY, w - 40f, 36f));
        float barY = rewardY + 18f + 10f + 18f;
        CodexUi.Place(dBarBack.rectTransform, CodexUi.Centered(0f, barY, w - 80f, 36f));
        float loreTop = y, loreBottom = barY + 18f + 12f;
        CodexUi.Place(dLoreCard.rectTransform, Rect.MinMaxRect(-w * .5f, loreBottom, w * .5f, Mathf.Max(loreBottom + 60f, loreTop)));
    }

    // ---------------------------------------------------------------------
    // Content
    // ---------------------------------------------------------------------

    // The listed sections of the live catalogue (dormant achievements are not listed).
    public static List<Section> BuildSections()
    {
        var list = new List<Section>();
        for (int s = 0; s <= (int)AchievementSection.Collection; s++)
        {
            var kind = (AchievementSection)s;
            var section = new Section { kind = kind, label = AchievementCatalog.SectionLabel(kind), color = SectionColor(kind) };
            foreach (var d in AchievementCatalog.All)
                if (d.Section == kind && AchievementCatalog.IsActive(d)) section.defs.Add(d);
            if (section.defs.Count == 0) continue;
            Count(section);
            list.Add(section);
        }
        return list;
    }

    static void Count(Section s)
    {
        s.unlocked = 0;
        foreach (var d in s.defs) if (AchievementStore.IsUnlocked(d)) s.unlocked++;
        s.counter = s.unlocked + "/" + s.defs.Count;
    }

    public static Color SectionColor(AchievementSection kind)
    {
        switch (kind)
        {
            case AchievementSection.Journey: return CodexPalette.Cyan;
            case AchievementSection.Bosses: return EnemyPalette.BruiseHi;
            case AchievementSection.Elites: return CodexPalette.Magenta;
            case AchievementSection.Combat: return CodexPalette.Sodium;
            case AchievementSection.Pause: return CodexPalette.Teal;
            case AchievementSection.Codex: return CodexPalette.SteelHi;
            default: return CodexPalette.Amber;
        }
    }

    public void Show(bool keepScroll = false)
    {
        float keepY = content.anchoredPosition.y;
        root.gameObject.SetActive(true);
        sections = BuildSections();
        shownCount = 0;
        for (int s = 0; s < sections.Count && s < CodexPanel.MaxSections; s++)
        {
            sectionStart[s] = shownCount;
            foreach (var d in sections[s].defs)
            {
                if (shownCount >= cards.Length) break;
                cards[shownCount++].def = d;
            }
        }
        for (int i = 0; i < cards.Length; i++) cards[i].rt.gameObject.SetActive(i < shownCount);
        for (int i = 0; i < CodexPanel.MaxSections; i++)
        {
            bool on = i < sections.Count;
            headers[i].rt.gameObject.SetActive(on);
            chips[i].rt.gameObject.SetActive(on);
            if (on) chips[i].label.text = sections[i].label;
        }
        LayoutCards();
        PaintAll();
        jumping = false;
        jumpTarget = -1;
        activeChip = -2;
        scroll.velocity = Vector2.zero;
        SetScrollY(keepScroll ? Mathf.Clamp(keepY, 0f, MaxScroll) : 0f);
        if (detailDef != null) ShowDetail(detailDef);
    }

    public void Hide()
    {
        detailDef = null;
        detail.gameObject.SetActive(false);
        root.gameObject.SetActive(false);
    }

    // Repaints every card, the strip and the open detail from the store.
    public void PaintAll()
    {
        for (int s = 0; s < sections.Count; s++)
        {
            Count(sections[s]);
            if (s < CodexPanel.MaxSections) PaintBar(headers[s], sections[s]);
        }
        for (int i = 0; i < shownCount; i++) PaintCard(cards[i]);
        PaintStrip();
        stickySection = -2;
        UpdateSticky();
        if (detailDef != null) PaintDetail(detailDef);
    }

    static void PaintBar(SectionBar bar, Section s)
    {
        bar.frame.color = s.color;
        bar.label.text = s.label;
        bar.counter.text = s.counter;
    }

    static bool Secret(AchievementDef d) { return d.hidden && !AchievementStore.IsUnlocked(d); }

    static string Fraction(AchievementDef d)
    {
        int v = AchievementStore.Progress(d), t = d.Target;
        return v.ToString() + "/" + t.ToString();
    }

    void PaintCard(Card c)
    {
        var d = c.def;
        bool unlocked = AchievementStore.IsUnlocked(d);
        bool claimed = AchievementStore.IsClaimed(d);
        bool claimable = unlocked && !claimed;
        bool secret = Secret(d);
        c.name.text = secret ? HiddenName : d.title;
        c.name.color = unlocked ? CodexUi.Body : CodexUi.Muted;
        c.badge.sprite = AchievementArt.For(d);
        c.badge.enabled = c.badge.sprite != null;
        c.badge.color = unlocked ? (claimed ? new Color(.88f, .88f, .88f, 1f) : Color.white) : LockedBadge;
        c.lockIcon.gameObject.SetActive(!unlocked);
        c.glow.gameObject.SetActive(claimable);
        c.edge.color = claimable ? Gold : claimed ? CodexUi.Accent : CodexUi.Locked;

        bool showBar = !unlocked && !secret && d.ShowsProgress;
        c.barBack.gameObject.SetActive(showBar);
        if (showBar)
        {
            float frac = Mathf.Clamp01((float)AchievementStore.Progress(d) / Mathf.Max(1, d.Target));
            var fr = c.barFill.rectTransform;
            fr.anchorMin = Vector2.zero;
            fr.anchorMax = new Vector2(Mathf.Max(.0001f, frac), 1f);
            fr.offsetMin = fr.offsetMax = Vector2.zero;
            c.barFill.enabled = frac > 0f;
            c.barText.text = Fraction(d);
        }
        c.collect.rt.gameObject.SetActive(claimable);
        c.collectedLabel.gameObject.SetActive(claimed);
        bool showTier = !unlocked && !showBar;
        c.tierLabel.gameObject.SetActive(showTier);
        if (showTier)
        {
            c.tierLabel.text = secret ? "?" : TierName(d.tier);
            c.tierLabel.color = secret ? CodexUi.Muted : AchievementArt.Metal(d.tier);
        }
    }

    public static string TierName(AchievementTier t) { return t.ToString().ToUpperInvariant(); }

    void PaintStrip()
    {
        int total = AchievementCatalog.ActiveCount;
        int claimable = AchievementStore.ClaimableCount;
        stripCount.text = AchievementStore.UnlockedCount + "/" + total + " UNLOCKED";
        stripLine.text = (claimable > 0 ? claimable + " TO COLLECT" : "NOTHING TO COLLECT") + "   -   DUST " + Balance(countShown >= 0 ? countShown : Mathf.FloorToInt(StarDustLedger.Saved));
        bool any = claimable > 0;
        collectAll.rt.gameObject.SetActive(true);
        collectAll.button.interactable = any;
        collectAll.frame.color = any ? Gold : CodexUi.Locked;
        collectAll.label.color = any ? CodexUi.Ink : CodexUi.Muted;
        collectAll.label.text = any ? "COLLECT ALL +" + (claimable * AchievementCatalog.RewardDust) : "COLLECT ALL";
        if (countShown < 0) countShown = Mathf.FloorToInt(StarDustLedger.Saved);
    }

    static string Balance(int dust) { return dust.ToString("N0", System.Globalization.CultureInfo.InvariantCulture); }

    // ---------------------------------------------------------------------
    // Detail
    // ---------------------------------------------------------------------

    public void ShowDetail(AchievementDef d)
    {
        if (d == null) return;
        detailDef = d;
        detail.gameObject.SetActive(true);
        PaintDetail(d);
    }

    public void CloseDetail()
    {
        detailDef = null;
        detail.gameObject.SetActive(false);
    }

    void PaintDetail(AchievementDef d)
    {
        bool unlocked = AchievementStore.IsUnlocked(d);
        bool claimed = AchievementStore.IsClaimed(d);
        bool claimable = unlocked && !claimed;
        bool secret = Secret(d);
        dName.text = secret ? HiddenName : d.title;
        dBadge.sprite = AchievementArt.For(d);
        dBadge.enabled = dBadge.sprite != null;
        dBadge.color = unlocked ? Color.white : LockedBadge;
        dLock.gameObject.SetActive(!unlocked);
        dFrameEdge.color = claimable ? Gold : claimed ? CodexUi.Accent : CodexUi.Locked;
        dLoreEdge.color = dFrameEdge.color;
        dPillLabel.text = secret ? "?" : TierName(d.tier) + "  -  " + AchievementCatalog.SectionLabel(d.Section);
        dPill.color = secret ? CodexUi.Locked : AchievementArt.Metal(d.tier);
        dLore.text = secret ? HiddenHint : d.description;
        dLore.color = unlocked ? CodexUi.Body : CodexUi.Muted;

        bool bar = d.ShowsProgress && !secret;
        dBarBack.gameObject.SetActive(bar);
        if (bar)
        {
            float frac = Mathf.Clamp01((float)AchievementStore.Progress(d) / Mathf.Max(1, d.Target));
            var fr = dBarFill.rectTransform;
            fr.anchorMin = Vector2.zero;
            fr.anchorMax = new Vector2(Mathf.Max(.0001f, frac), 1f);
            fr.offsetMin = fr.offsetMax = Vector2.zero;
            dBarFill.enabled = frac > 0f;
            dBarText.text = Fraction(d);
        }
        dReward.text = "REWARD  " + AchievementCatalog.RewardDust + " STAR DUST";
        dCollect.rt.gameObject.SetActive(claimable);
        dCollected.gameObject.SetActive(claimed);
    }

    // ---------------------------------------------------------------------
    // Input
    // ---------------------------------------------------------------------

    void OnCardClicked(int index)
    {
        if (index < 0 || index >= shownCount || detailDef != null) return;
        ShowDetail(cards[index].def);
    }

    void OnCollectCard(int index)
    {
        if (index < 0 || index >= shownCount || detailDef != null) return;
        Collect(cards[index].def);
    }

    void OnCollectDetail()
    {
        if (detailDef != null) Collect(detailDef);
    }

    void OnCollectAll()
    {
        if (detailDef != null) return;
        float before = StarDustLedger.Saved;
        int paid = AchievementStore.ClaimAll();
        if (paid > 0) AfterClaim(before);
    }

    void Collect(AchievementDef d)
    {
        float before = StarDustLedger.Saved;
        if (AchievementStore.Claim(d) > 0) AfterClaim(before);
    }

    void AfterClaim(float before)
    {
        countFrom = Mathf.Floor(before);
        countTo = Mathf.Floor(StarDustLedger.Saved);
        countAt = Time.unscaledTime;
        counting = true;
        countShown = Mathf.FloorToInt(countFrom);
        PaintAll();
        panel.OnAchievementsChanged();
    }

    // ---------------------------------------------------------------------
    // Scrolling
    // ---------------------------------------------------------------------

    public void JumpToSection(int s)
    {
        if (s < 0 || s >= sections.Count || s >= CodexPanel.MaxSections || detailDef != null) return;
        scroll.StopMovement();
        jumpFrom = content.anchoredPosition.y;
        jumpTo = JumpTargetY(s);
        jumpAt = Time.unscaledTime;
        lastSetY = jumpFrom;
        jumping = true;
        jumpTarget = s;
        UpdateSticky();
    }

    public float JumpTargetY(int s)
    {
        return Mathf.Clamp(s == 0 ? 0f : sectionTop[s], 0f, MaxScroll);
    }

    public void SetScrollY(float y)
    {
        var p = content.anchoredPosition;
        p.y = y;
        content.anchoredPosition = p;
        lastSetY = y;
        UpdateSticky();
    }

    void OnScrolled(Vector2 normalized)
    {
        if (!jumping && Mathf.Abs(content.anchoredPosition.y - lastSetY) > .5f)
        {
            jumpTarget = -1;
            lastSetY = content.anchoredPosition.y;
        }
        UpdateSticky();
    }

    public int SectionAtScroll(float y)
    {
        int s = -1;
        for (int i = 0; i < sections.Count && i < CodexPanel.MaxSections; i++)
            if (sectionTop[i] <= y + .01f) s = i;
        return s;
    }

    void UpdateSticky()
    {
        if (sticky == null) return;
        float y = content.anchoredPosition.y;
        int s = SectionAtScroll(y);
        if (s != stickySection)
        {
            stickySection = s;
            sticky.rt.gameObject.SetActive(s >= 0);
            if (s >= 0) PaintBar(sticky, sections[s]);
        }
        if (s >= 0)
        {
            float push = 0f;
            if (s + 1 < sections.Count && s + 1 < CodexPanel.MaxSections)
                push = Mathf.Max(0f, CodexPanel.SectionHeaderHeight - (sectionTop[s + 1] - y));
            if (!Mathf.Approximately(sticky.rt.anchoredPosition.y, push))
                sticky.rt.anchoredPosition = new Vector2(0f, push);
        }
        int chip = jumpTarget >= 0 ? jumpTarget : Mathf.Max(s, 0);
        if (chip != activeChip)
        {
            activeChip = chip;
            for (int i = 0; i < sections.Count && i < CodexPanel.MaxSections; i++)
            {
                bool on = i == chip;
                chips[i].frame.color = on ? sections[i].color : CodexUi.Idle;
                chips[i].label.color = on ? CodexUi.Ink : sections[i].color;
            }
        }
    }

    // ---------------------------------------------------------------------
    // Per frame (unscaled time; arithmetic only, no allocation)
    // ---------------------------------------------------------------------

    public void Tick(float now)
    {
        if (!root.gameObject.activeSelf) return;
        if (jumping)
        {
            if (Mathf.Abs(content.anchoredPosition.y - lastSetY) > .5f) jumping = false;
            else
            {
                float t = (now - jumpAt) / CodexPanel.JumpDuration;
                SetScrollY(Mathf.LerpUnclamped(jumpFrom, jumpTo, CodexUi.EaseOutCubic(t)));
                if (t >= 1f) jumping = false;
            }
        }
        float k = 1f + PulseScale * (.5f + .5f * Mathf.Sin(now * PulseHz * Mathf.PI * 2f));
        if (detailDef != null)
        {
            if (dCollect.rt.gameObject.activeSelf) dCollect.rt.localScale = new Vector3(k, k, 1f);
        }
        else
        {
            float top = content.anchoredPosition.y, bottom = top + viewport.rect.height;
            for (int i = 0; i < shownCount; i++)
            {
                var c = cards[i];
                if (!c.collect.rt.gameObject.activeSelf) continue;
                float centre = -c.rt.anchoredPosition.y, half = cardH * .5f;
                if (centre + half <= top || centre - half >= bottom) continue;   // off screen: holds its pose
                c.collect.rt.localScale = new Vector3(k, k, 1f);
            }
            if (collectAll.button.interactable) collectAll.rt.localScale = new Vector3(k, k, 1f);
            else collectAll.rt.localScale = Vector3.one;
        }
        if (counting)
        {
            float p = (now - countAt) / CountUpSeconds;
            int shown = p >= 1f ? Mathf.FloorToInt(countTo) : Mathf.FloorToInt(Mathf.Lerp(countFrom, countTo, CodexUi.EaseOutCubic(p)));
            if (shown != countShown)
            {
                countShown = shown;
                PaintStrip();
            }
            if (p >= 1f) counting = false;
        }
    }

    // The tab-swap fade (CodexPanel.ApplyFrame).
    public void SetFade(float alpha)
    {
        rootGroup.alpha = alpha;
        rootGroup.interactable = true;
        rootGroup.blocksRaycasts = true;
    }

    // Jump every running transition to its end (tests).
    public void SkipAnimations()
    {
        if (jumping) { jumpAt = Time.unscaledTime - 100f; Tick(Time.unscaledTime); }
        if (counting) { countAt = Time.unscaledTime - 100f; Tick(Time.unscaledTime); }
    }
}
