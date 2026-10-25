using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// The codex view, opened from the credits screen: category tabs, a scrollable
// grid of cards (sprite + name) and a detail view (large sprite, name,
// category and lore). Undiscovered entries show as a dark silhouette named
// "???" with no lore.
//
// ENEMIES and HAZARDS are one vertically scrolling list in world sections
// (SPACE, FROST, VERDANT, EMBER, TIDE, STORM, DESERT, then BOSSES for enemies), each under a flat
// cel header bar in that world's enemy light with a found/total counter.
// The current section's header stays pinned to the top of the list while
// you scroll through it (pushed up by the next one), and a row of jump chips
// above the list smooth-scrolls to any section. The BOSSES section keeps the
// bosses secret: it only appears once one has been met, lists only the met
// ones and counts only those (developer mode shows them all).
//
// Built entirely at runtime on its own overlay canvas as flat 80s-anime cels
// in the Akira palette (CodexPalette): thick ink outlines, flat fills, hard
// angular corners, Kaneda red for selection. Every rect comes from
// ComputeLayout(safe area), which is what CodexTest checks across aspect
// ratios. All motion runs on unscaled time and nothing allocates per frame.
//
// Every card's art and the detail art play the entry's idle animation
// (CodexAnimator, resolved through the game's own art loaders by
// CodexAnimations); the detail view also plays the occasional attack tell.
// Locked entries animate as the ink silhouette. Only cards inside the list's
// viewport tick, nothing ticks behind the detail view, and a closed panel is
// inactive, so nothing animates at all.
public class CodexPanel : MonoBehaviour
{
    // ---- Layout constants (canvas units) ----

    public const float GlowMargin = 0f;    // the cel frame's ink line sits inside the body
    public const float Margin = 12f;       // clear of the safe-area edge
    public const float MaxWidth = 820f;
    public const float Pad = 30f;
    public const float HeaderHeight = 64f;
    public const float DividerHeight = 16f;
    // Eight tabs (the six entry categories, then ACHIEVEMENTS and MUSIC) in two rows of four:
    // one row left each tab ~110
    // units wide and 60 tall (~32-40 dp touch targets even padded). Each
    // tab is drawn TabRowHeight tall and its touch target reaches half of
    // every gap around it: TabRowHeight + TabRowGap = 96 units, >= 48 dp /
    // 44 pt wherever the canvas is >= 0.5 dp per unit (UiScale's floor).
    public const int TabsPerRow = 4, TabRows = 2;
    public const int AchievementsTab = 6;           // index of the ACHIEVEMENTS tab (after CodexPanel.Tabs)
    public const int MusicTab = 7;                  // index of the MUSIC tab (after ACHIEVEMENTS)
    public const int TabCount = 8;
    // A discovered world's detail view has a sub-tab row: INFO (the lore) | BACKDROPS (that world's
    // backdrop gallery). Same cel tab look as the head tabs; each button is SubTabHeight tall with the
    // gap added as hit padding (>= 48 dp wherever the canvas is >= 0.5 dp per unit).
    public const int DetailTabInfo = 0, DetailTabBackdrops = 1;
    public const float SubTabHeight = 82f, SubTabGap = 14f, SubTabSplit = 12f;
    public const string InfoLabel = "INFO";
    public const float TabRowHeight = 84f;
    public const float TabRowGap = 12f;
    public const float TabsHeight = TabRows * TabRowHeight + (TabRows - 1) * TabRowGap;
    public const float TabGap = 12f;
    public const int TabLabelSize = 24, TabLabelMinSize = 16;
    public const float BackWidth = 288f, BackHeight = 100f;
    public const float Gap = 16f;
    public const float MinCard = 176f;
    public const float CardNameHeight = 54f;
    public const float GridInset = 8f;
    public const float ChipsHeight = 52f;
    public const float ChipGap = 8f;
    public const float ChipGapTight = 4f;      // with more than six chips (all seven worlds + bosses) the row is tight
    public static float ChipGapFor(int count) { return count > 6 ? ChipGapTight : ChipGap; }
    public const float ChipsGap = 12f;         // chip row -> list
    public const float SectionHeaderHeight = 56f;
    public const float SectionHeaderGap = 12f; // header -> its first card row
    public const float SectionGap = 28f;       // last card row -> next header
    public const int MaxSections = 8;          // up to seven worlds + bosses (Tide is the fifth; Storm the sixth; Desert the seventh)

    // ---- Timing (seconds, unscaled) ----

    public const float OpenDuration = .3f;
    public const float CloseDuration = .2f;
    public const float SwapDuration = .22f;
    public const float TabFadeDuration = .18f;
    public const float JumpDuration = .35f;
    // Cards start this far apart in their loops (seconds x card index), so a
    // row of one family doesn't move in lockstep.
    public const float CardPhaseStep = .37f;

    public static readonly CodexCategory[] Tabs =
    {
        CodexCategory.Log, CodexCategory.Enemies, CodexCategory.Hazards,
        CodexCategory.Atoms, CodexCategory.Worlds, CodexCategory.Ships,
    };

    public const string LockedHint = "Not yet discovered.\nKeep flying - it's out there somewhere.";

    public const string AchievementsLabel = "ACHIEVEMENTS";
    public const string MusicLabel = "MUSIC";
    public const string BackdropsLabel = "BACKDROPS";   // the sub-tab inside a world's detail

    public static string TabName(int i)
    {
        return i == AchievementsTab ? AchievementsLabel : i == MusicTab ? MusicLabel : CategoryLabel(Tabs[i]);
    }

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
        // Sectioned tabs (ENEMIES, HAZARDS): the jump-chip row, and the
        // scrolling list under it (the body less the chips).
        public Rect chips, list;
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
        l.chips = new Rect(left, l.body.yMax - ChipsHeight, iw, ChipsHeight);
        l.list = Rect.MinMaxRect(left, l.body.yMin, left + iw, l.chips.yMin - ChipsGap);
        l.detail = Rect.MinMaxRect(left, l.back.yMax + Gap, left + iw, l.divider.yMin - 10f);

        float gridW = iw - 2f * GridInset;
        l.columns = Mathf.Clamp(Mathf.FloorToInt((gridW + Gap) / (MinCard + Gap)), 2, 5);
        l.cardWidth = (gridW - Gap * (l.columns - 1)) / l.columns;
        l.cardHeight = l.cardWidth + CardNameHeight - 14f;
        l.tabWidth = (iw - TabGap * (TabsPerRow - 1)) / TabsPerRow;
        return l;
    }

    // Tab `i`'s drawn rect (panel-local): row-major, 4 + 4.
    public static Rect TabRect(Layout l, int i)
    {
        int row = i / TabsPerRow, col = i % TabsPerRow;
        int inRow = row == 0 ? TabsPerRow : TabCount - TabsPerRow;
        float w = (l.tabs.width - TabGap * (inRow - 1)) / inRow;
        float x = l.tabs.xMin + col * (w + TabGap);
        float y = l.tabs.yMax - (row + 1) * TabRowHeight - row * TabRowGap;
        return new Rect(x, y, w, TabRowHeight);
    }

    // ---------------------------------------------------------------------
    // Sections
    // ---------------------------------------------------------------------

    // Tabs shown as world sections rather than one plain grid.
    public static bool IsSectioned(CodexCategory c)
    {
        return c == CodexCategory.Enemies || c == CodexCategory.Hazards;
    }

    public const int BossWorld = -1;

    public sealed class Section
    {
        public string label;
        public Color color;
        public int world;            // WorldManager.Worlds index, or BossWorld
        public readonly List<CodexEntry> entries = new List<CodexEntry>();
        public int discovered;
        // "5/8" for a world; the bosses show only how many were met, so the
        // hidden total never leaks.
        public string counter;
    }

    public static string WorldLabel(int world)
    {
        return world == BossWorld ? "BOSSES" : EnemyRoster.WorldKeys[world].ToUpperInvariant();
    }

    // Header accent: each world's signature enemy light; the bosses get
    // the hostile bruise.
    public static Color SectionColor(int world)
    {
        return world == BossWorld ? EnemyPalette.BruiseHi : EnemyPalette.WorldLight(world);
    }

    // Order inside a world: fighters by tier, chaser, alien, big; rocks by
    // tier, then the mine.
    static int RoleRank(EnemyRole role)
    {
        switch (role)
        {
            case EnemyRole.Fighter: case EnemyRole.Rock: return 0;
            case EnemyRole.Chaser: case EnemyRole.Mine: return 1;
            case EnemyRole.Alien: return 2;
            default: return 3;
        }
    }

    // The listed entries of a tab, in display sections. A plain tab is one
    // unlabelled section. Built on Populate only, never per frame.
    public static List<Section> SectionsFor(CodexCategory c)
    {
        var sections = new List<Section>();
        var entries = Codex.Entries;
        if (!IsSectioned(c))
        {
            var all = new Section { label = CategoryLabel(c), color = CodexUi.Accent, world = 0 };
            foreach (var e in entries)
            {
                if (e.category != c || !Codex.IsListed(e)) continue;
                all.entries.Add(e);
            }
            Finish(all);
            sections.Add(all);
            return sections;
        }

        int worlds = EnemyRoster.WorldKeys.Length;
        var keys = new List<int>[worlds];
        for (int w = 0; w < worlds; w++)
        {
            sections.Add(new Section { label = WorldLabel(w), color = SectionColor(w), world = w });
            keys[w] = new List<int>();
        }
        var bosses = new Section { label = WorldLabel(BossWorld), color = SectionColor(BossWorld), world = BossWorld };
        for (int i = 0; i < entries.Length; i++)
        {
            var e = entries[i];
            if (e.category != c || !Codex.IsListed(e)) continue;
            if (BossCatalog.Find(e.id) != null) { bosses.entries.Add(e); continue; }
            var def = EnemyRoster.FindByCodexId(e.id);
            var elite = def == null ? EliteCatalog.FindByCodexId(e.id) : null;
            // elite ships close their own world's section
            int w = def != null ? Mathf.Clamp(def.world, 0, worlds - 1) :
                    elite != null ? Mathf.Clamp(elite.WorldIndex, 0, worlds - 1) : 0;
            // (an entry with no roster def: an elite closes its section; the Space drift asteroid sits after the rocks, before the mine)
            int key = def != null ? RoleRank(def.role) * 100 + def.tier :
                      elite == null && c == CodexCategory.Hazards ? RoleRank(EnemyRole.Rock) * 100 + 50 : 999;
            // Ties keep catalogue order.
            InsertSorted(sections[w].entries, keys[w], e, key * 1000 + i);
        }
        for (int w = 0; w < worlds; w++) Finish(sections[w]);
        if (bosses.entries.Count > 0)
        {
            Finish(bosses);
            bosses.counter = bosses.discovered.ToString();
            sections.Add(bosses);
        }
        return sections;
    }

    static void InsertSorted(List<CodexEntry> list, List<int> keys, CodexEntry e, int key)
    {
        int at = keys.Count;
        while (at > 0 && keys[at - 1] > key) at--;
        keys.Insert(at, key);
        list.Insert(at, e);
    }

    static void Finish(Section s)
    {
        s.discovered = 0;
        foreach (var e in s.entries) if (Codex.IsDiscovered(e)) s.discovered++;
        s.counter = s.discovered + "/" + s.entries.Count;
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
    Image[] tabDots;
    // Unlock-reward collecting: COLLECT ALL for the open tab (header, right of the counter) and COLLECT for the open
    // entry (detail, beside the category pill). Both hide when nothing is waiting.
    Image collectTabFrame, collectDetailFrame;
    Button collectTabButton, collectDetailButton;
    Text collectTabLabel, collectDetailLabel;
    // LAST STAND (KillCamArchive.CodexId): WATCH opens the saved kill cam (CodexReplayViewer), in the COLLECT slot.
    Image watchFrame;
    Button watchButton;
    Text watchLabel;
    // The hostile entries' VERSUS YOU table at the foot of the lore card (DamageLog): hurt you / killed you / share.
    RectTransform tallyRoot;
    Text[] tallyValues, tallyHeads;
    bool tallyShown;
    float feedbackUntil;
    string feedbackText;
    RectTransform grid, viewport, content;
    CanvasGroup gridGroup;
    ScrollRect scroll;
    Card[] cards;
    RectTransform detail;
    CanvasGroup detailGroup;
    Image detailFrame, detailFrameEdge, detailMask, detailArt;
    CodexAnimator detailAnim;
    Mask detailMaskComp;
    Text detailName, detailPillLabel, detailSubtitle, detailLore, detailIndex;
    Image detailPill, detailLoreCard, detailLoreEdge;
    RectTransform detailArtBox;
    RectTransform backSlot;
    Button backBtn;
    RectTransform chipsRoot;
    CanvasGroup chipsGroup;
    CodexAchievementsView ach;
    CodexMusicView music;
    CodexBackdropsView backdrops;           // the gallery inside a world's detail (BACKDROPS sub-tab)
    RectTransform infoRoot;                 // the detail's art / name / lore (hidden while the gallery shows)
    Image[] subFrames;
    Text[] subLabels;
    int detailTab;                          // DetailTabInfo | DetailTabBackdrops
    bool subTabsShown;                      // a discovered world's detail
    bool achievementsOpen, musicOpen;
    Chip[] chips;
    SectionBar[] headers;
    SectionBar sticky;

    Layout layout;
    CodexCategory category = CodexCategory.Log;
    CodexEntry[] shown = new CodexEntry[0];
    int shownCount;
    CodexEntry detailEntry;
    List<Section> sections = new List<Section>();
    readonly float[] sectionTop = new float[MaxSections];   // content-space y of each header's top edge
    readonly int[] sectionStart = new int[MaxSections];     // first card index of each section
    bool sectioned;
    Rect gridRect;
    int stickySection = -2, activeChip = -2;
    int jumpTarget = -1;      // chip highlighted after a jump, until the player scrolls
    bool jumping;
    float jumpAt, jumpFrom, jumpTo, lastSetY;

    enum Phase { Hidden, Opening, Open, Closing }
    Phase phase = Phase.Hidden;
    float phaseAt;
    bool inDetail;
    float swapAt = -10f, tabAt = -10f;
    int lastW, lastH;
    Rect lastSafe;

    class SectionBar
    {
        public RectTransform rt;
        public Image frame;
        public Text label, counter;
    }

    class Chip
    {
        public RectTransform rt;
        public Image frame;
        public Text label;
        public Button button;
    }

    class Card
    {
        public RectTransform rt;
        public Button button;
        public Image frame, edge, mask, art, lockIcon, newDot;
        public Mask maskComp;
        public RectTransform artBox;
        public Text name;
        public CodexEntry entry;
        public CodexAnimator anim;
    }

    public bool IsOpen { get { return phase == Phase.Opening || phase == Phase.Open; } }
    // The ACHIEVEMENTS tab (not a CodexCategory: its cards are achievements, not entries).
    public CodexAchievementsView Achievements { get { return ach; } }
    public bool AchievementsOpen { get { return achievementsOpen; } }
    // The MUSIC tab (not a CodexCategory: its cards are tracks).
    public CodexMusicView Music { get { return music; } }
    public bool MusicOpen { get { return musicOpen; } }
    // The BACKDROPS sub-tab of a discovered world's detail (its cards are backdrops, opened in CodexBackdropViewer).
    public CodexBackdropsView Backdrops { get { return backdrops; } }
    public bool BackdropsOpen { get { return inDetail && detailTab == DetailTabBackdrops && backdrops.Active; } }
    public bool SubTabsShown { get { return inDetail && subTabsShown; } }
    public int DetailTab { get { return detailTab; } }
    public Text SubTabLabel(int i) { return subLabels[i]; }
    public Image SubTabFrame(int i) { return subFrames[i]; }
    bool OtherViewOpen { get { return achievementsOpen || musicOpen; } }
    public bool InDetail { get { return inDetail; } }
    public CodexCategory Category { get { return category; } }
    public CodexEntry DetailEntry { get { return detailEntry; } }
    public RectTransform Panel { get { return panel; } }
    public Layout CurrentLayout { get { return layout; } }
    public Button BackButton { get { return backBtn; } }
    public Text DetailName { get { return detailName; } }
    public Image DetailPill { get { return detailPill; } }
    public Image DetailLoreCard { get { return detailLoreCard; } }
    public Text DetailLore { get { return detailLore; } }
    public Image DetailArt { get { return detailArt; } }
    public Text DetailSubtitle { get { return detailSubtitle; } }
    public Text Counter { get { return counter; } }
    public int VisibleCards { get { return shownCount; } }

    public Button CardButton(int i) { return cards[i].button; }
    public Text CardName(int i) { return cards[i].name; }
    public Image CardArt(int i) { return cards[i].art; }
    public CodexEntry CardEntry(int i) { return cards[i].entry; }
    public CodexAnimator CardAnimator(int i) { return cards[i].anim; }
    public CodexAnimator DetailAnimator { get { return detailAnim; } }
    public RectTransform CardArtBox(int i) { return cards[i].artBox; }
    public RectTransform CardRect(int i) { return cards[i].rt; }
    public Text TabLabel(int i) { return tabLabels[i]; }

    // Sections of the current tab (one unlabelled section on a plain tab).
    public bool Sectioned { get { return sectioned; } }
    public int SectionCount { get { return sections.Count; } }
    public Section SectionAt(int i) { return sections[i]; }
    public int SectionStart(int i) { return sectionStart[i]; }
    public float SectionTop(int i) { return sectionTop[i]; }
    public Text SectionLabel(int i) { return headers[i].label; }
    public Text SectionCounter(int i) { return headers[i].counter; }
    public RectTransform SectionHeaderRect(int i) { return headers[i].rt; }
    public Button ChipButton(int i) { return chips[i].button; }
    public Text ChipLabel(int i) { return chips[i].label; }
    public RectTransform ChipRect(int i) { return chips[i].rt; }
    public int ActiveChip { get { return activeChip; } }
    public RectTransform ChipsRoot { get { return chipsRoot; } }
    // The pinned header: which section it shows (-1 = none pinned) and its label.
    public int StickySection { get { return stickySection; } }
    public Text StickyLabel { get { return sticky.label; } }
    public Text StickyCounter { get { return sticky.counter; } }
    public RectTransform StickyRect { get { return sticky.rt; } }
    public RectTransform Viewport { get { return viewport; } }
    public RectTransform Content { get { return content; } }
    public float ScrollY { get { return content.anchoredPosition.y; } }
    public float MaxScroll { get { return Mathf.Max(0f, content.sizeDelta.y - viewport.rect.height); } }

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

        BuildCollectTab();
        BuildTabs();
        BuildChips();
        BuildGrid();
        ach = new CodexAchievementsView(this, panel, font);
        music = new CodexMusicView(this, panel, font);
        BuildDetail();
        BuildBack();
    }

    // A yellow tab-shaped chip with a label: the collect buttons.
    Image NewCollectButton(string name, Transform parent, int size, int minSize, out Button button, out Text label)
    {
        var frame = CodexUi.NewImage(name, parent, CodexUi.CodexSprite("cx_tab"), CodexHomeButton.BubbleYellow, true);
        frame.raycastTarget = true;
        frame.raycastPadding = new Vector4(-8f, -12f, -8f, -12f);
        button = frame.gameObject.AddComponent<Button>();
        button.transition = Selectable.Transition.None;
        button.targetGraphic = frame;
        label = CodexUi.NewText("Label", frame.rectTransform, font, "", size, CodexUi.Ink, TextAnchor.MiddleCenter);
        label.horizontalOverflow = HorizontalWrapMode.Wrap;
        label.verticalOverflow = VerticalWrapMode.Truncate;
        label.resizeTextForBestFit = true;
        label.resizeTextMinSize = minSize;
        label.resizeTextMaxSize = size;
        var lrt = label.rectTransform;
        CodexUi.Stretch(lrt);
        lrt.offsetMin = new Vector2(8f, 0f);
        lrt.offsetMax = new Vector2(-8f, 0f);
        frame.gameObject.SetActive(false);
        return frame;
    }

    void BuildCollectTab()
    {
        collectTabFrame = NewCollectButton("CollectTab", panel, 24, 12, out collectTabButton, out collectTabLabel);
        collectTabButton.onClick.AddListener(OnCollectTab);
    }

    void BuildTabs()
    {
        tabsRoot = CodexUi.NewRect("Tabs", panel);
        tabsGroup = tabsRoot.gameObject.AddComponent<CanvasGroup>();
        tabFrames = new Image[TabCount];
        tabLabels = new Text[TabCount];
        tabDots = new Image[TabCount];
        for (int i = 0; i < TabCount; i++)
        {
            int index = i;
            string tabName = TabName(i);
            var tabFrame = CodexUi.NewImage("Tab" + tabName, tabsRoot, CodexUi.CodexSprite("cx_tab"), CodexUi.Idle, true);
            tabFrame.raycastTarget = true;
            // the hit area spans half of every gap around the tab's art
            tabFrame.raycastPadding = new Vector4(-TabGap * .5f, -TabRowGap * .5f, -TabGap * .5f, -TabRowGap * .5f);
            var button = tabFrame.gameObject.AddComponent<Button>();
            button.transition = Selectable.Transition.None;
            button.targetGraphic = tabFrame;
            button.onClick.AddListener(() => ShowTab(index));

            var label = CodexUi.NewText("Label", tabFrame.rectTransform, font, tabName, TabLabelSize, Color.white,
                                        TextAnchor.MiddleCenter);
            label.horizontalOverflow = HorizontalWrapMode.Wrap;
            label.verticalOverflow = VerticalWrapMode.Truncate;
            label.resizeTextForBestFit = true;
            label.resizeTextMinSize = TabLabelMinSize;
            label.resizeTextMaxSize = TabLabelSize;
            var lrt = label.rectTransform;
            CodexUi.Stretch(lrt);
            lrt.offsetMin = new Vector2(6f, 0f);
            lrt.offsetMax = new Vector2(-6f, 0f);

            // NEW dot on the tab's top-right corner: shown until the tab is opened
            tabDots[i] = NewDot(tabFrame.rectTransform, new Vector2(-NewDotInset, -NewDotInset), false);
            tabFrames[i] = tabFrame;
            tabLabels[i] = label;
        }
    }

    void BuildChips()
    {
        chipsRoot = CodexUi.NewRect("Chips", panel);
        chipsGroup = chipsRoot.gameObject.AddComponent<CanvasGroup>();
        chips = new Chip[MaxSections];
        for (int i = 0; i < MaxSections; i++)
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
            chip.label.resizeTextMinSize = 11;
            chip.label.resizeTextMaxSize = 18;
            var lrt = chip.label.rectTransform;
            CodexUi.Stretch(lrt);
            lrt.offsetMin = new Vector2(8f, 0f);
            lrt.offsetMax = new Vector2(-8f, 0f);
            chips[i] = chip;
        }
    }

    // A flat cel header bar: the chamfered tab plate filled with the
    // section's accent, ink type leaning forward, counter on the right.
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

    static void PaintBar(SectionBar bar, Section s)
    {
        bar.frame.color = s.color;
        bar.label.text = s.label;
        bar.counter.text = s.counter;
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
        scroll.onValueChanged.AddListener(OnScrolled);

        int most = 0;
        foreach (var c in Tabs)
        {
            int total = Codex.CapacityIn(c);
            most = Mathf.Max(most, total);
        }
        cards = new Card[most];
        for (int i = 0; i < most; i++) cards[i] = BuildCard(i);

        headers = new SectionBar[MaxSections];
        for (int i = 0; i < MaxSections; i++) headers[i] = BuildSectionBar("Section" + i, content, false);
        // Pinned copy of the current section's header, drawn over the list
        // (inside the viewport's clip). It takes touches so a tap on it never
        // opens the card hidden underneath; drags still reach the ScrollRect.
        sticky = BuildSectionBar("StickyHeader", viewport, true);
        var srt = sticky.rt;
        srt.anchorMin = new Vector2(0f, 1f);
        srt.anchorMax = new Vector2(1f, 1f);
        srt.pivot = new Vector2(.5f, 1f);
        srt.offsetMin = new Vector2(GridInset, -SectionHeaderHeight);
        srt.offsetMax = new Vector2(-GridInset, 0f);
        sticky.rt.gameObject.SetActive(false);
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
        CodexUi.Isolate(card.mask.gameObject);
        card.art = CodexUi.NewImage("Art", card.artBox, null, Color.white);
        card.art.preserveAspect = true;
        CodexUi.Stretch(card.art.rectTransform);
        card.anim = CodexAnimator.On(card.art);

        card.name = CodexUi.NewText("Name", card.rt, font, "", 18, CodexUi.Body, TextAnchor.MiddleCenter, true);
        card.name.verticalOverflow = VerticalWrapMode.Truncate;
        card.name.resizeTextForBestFit = true;
        card.name.resizeTextMinSize = 11;
        card.name.resizeTextMaxSize = 18;
        CodexUi.AddOutline(card.name.gameObject, CodexUi.Ink, 1.5f);

        card.lockIcon = CodexUi.NewImage("Lock", card.rt, CodexUi.CodexSprite("cx_lock"), CodexUi.Select);
        card.lockIcon.preserveAspect = true;
        CodexUi.Isolate(card.lockIcon.gameObject);   // a sub-canvas draws over its parent: keep the lock above the art
        card.newDot = NewDot(card.rt, new Vector2(-NewDotInset, -NewDotInset), true);
        return card;
    }

    // The restrained amber pixel dot of the home bubble, no number: takes no touches, no animation.
    // 17 units (about 35% under the first 26), snapped to a whole multiple of the 13 px art; sits fully inside its
    // card / tab, NewDotInset from the top and right edges.
    public const float NewDotSize = 17f, NewDotInset = 6f;
    public float DotUnits() { return CodexHomeButton.SnapToArt(this, NewDotSize); }
    Image NewDot(RectTransform parent, Vector2 inset, bool above)
    {
        var dot = CodexUi.NewImage("NewDot", parent, CodexHomeButton.BadgeSprite(), Color.white);
        dot.raycastTarget = false;
        var rt = dot.rectTransform;
        rt.anchorMin = rt.anchorMax = Vector2.one;
        rt.pivot = new Vector2(1f, 1f);
        rt.anchoredPosition = inset;
        rt.sizeDelta = new Vector2(NewDotSize, NewDotSize);
        if (above) CodexUi.Isolate(dot.gameObject);   // a sub-canvas draws over its parent: keep the dot above the art
        dot.gameObject.SetActive(false);
        return dot;
    }

    public Image CardNewDot(int i) { return cards[i].newDot; }
    public Image TabNewDot(int i) { return tabDots[i]; }

    // The yellow dots follow the SAVED reward state, so they persist until the reward is collected (opening a tab or
    // an entry never clears them): a card while its unlock reward is waiting, a category tab while it has any, the
    // ACHIEVEMENTS tab while anything is claimable.
    void PaintNewDots()
    {
        if (cards == null || tabDots == null) return;
        for (int i = 0; i < cards.Length; i++)
            cards[i].newDot.gameObject.SetActive(i < shownCount && cards[i].entry != null && Codex.IsRewardPending(cards[i].entry));
        for (int i = 0; i < TabCount; i++)
            tabDots[i].gameObject.SetActive(TabHasReward(i));
        PaintCollect();
    }

    public static bool TabHasReward(int i)
    {
        return i == MusicTab ? false : i == AchievementsTab ? AchievementStore.ClaimableCount > 0 : Codex.PendingIn(Tabs[i]) > 0;
    }

    public Button CollectTabButton { get { return collectTabButton; } }
    public Text CollectTabLabel { get { return collectTabLabel; } }
    public Image CollectTabFrame { get { return collectTabFrame; } }
    public Button CollectEntryButton { get { return collectDetailButton; } }
    public Text CollectEntryLabel { get { return collectDetailLabel; } }
    public Image CollectEntryFrame { get { return collectDetailFrame; } }

    void PaintCollect()
    {
        if (collectTabFrame == null) return;
        int tabPending = !OtherViewOpen && !inDetail ? Codex.PendingIn(category) : 0;
        bool tabShown = tabPending > 0;
        bool wasShown = collectTabFrame.gameObject.activeSelf;
        collectTabFrame.gameObject.SetActive(tabShown);
        if (tabShown) collectTabLabel.text = "COLLECT ALL +" + tabPending * Codex.UnlockReward;
        if (wasShown != tabShown && layout.panel.width > 0f) { PlaceHeader(); RefreshCounter(); }   // the counter shortens beside the chip

        bool entryShown = inDetail && detailEntry != null && !OtherViewOpen && Codex.IsRewardPending(detailEntry);
        collectDetailFrame.gameObject.SetActive(entryShown);
        if (entryShown) collectDetailLabel.text = "COLLECT +" + Codex.UnlockReward;

        bool watch = inDetail && detailEntry != null && !OtherViewOpen && !entryShown &&
                     detailEntry.id == KillCamArchive.CodexId && KillCamArchive.HasRecord;
        if (watchFrame != null) watchFrame.gameObject.SetActive(watch);
    }

    public const string WatchLabel = "WATCH REPLAY";
    public Button WatchButton { get { return watchButton; } }
    public Image WatchFrame { get { return watchFrame; } }

    // LAST STAND's WATCH: the saved kill cam, full screen over the codex.
    public CodexReplayViewer OpenReplay()
    {
        if (!inDetail || detailEntry == null || detailEntry.id != KillCamArchive.CodexId || !KillCamArchive.HasRecord) return null;
        return CodexReplayViewer.Open(font);
    }

    void OnCollectTab()
    {
        if (OtherViewOpen || inDetail) return;
        AfterCollect(Codex.CollectRewards(category));
    }

    void OnCollectDetail()
    {
        if (!inDetail || detailEntry == null) return;
        AfterCollect(Codex.CollectReward(detailEntry.id));
    }

    // The counter flashes what was paid for a moment (the same star dust the shop spends).
    void AfterCollect(int dust)
    {
        if (dust <= 0) return;
        feedbackText = "+" + dust + " STAR DUST";
        feedbackUntil = Time.unscaledTime + 2.2f;
        RefreshCounter();
        PaintNewDots();
    }

    void BuildDetail()
    {
        var go = new GameObject("Detail", typeof(RectTransform), typeof(CanvasGroup));
        go.transform.SetParent(panel, false);
        detail = (RectTransform)go.transform;
        detailGroup = go.GetComponent<CanvasGroup>();
        infoRoot = CodexUi.NewRect("Info", detail);
        CodexUi.Stretch(infoRoot);

        detailFrame = CodexUi.NewImage("ArtFrame", infoRoot, CodexUi.CodexSprite("cx_card"), Color.white, true);
        detailFrameEdge = CodexUi.NewImage("Edge", detailFrame.rectTransform, CodexUi.CodexSprite("cx_card_edge"), CodexUi.Accent, true);
        CodexUi.Stretch(detailFrameEdge.rectTransform);
        detailMask = CodexUi.NewImage("ArtBox", detailFrame.rectTransform, CodexUi.CodexSprite("cx_circle"), Color.white);
        detailArtBox = detailMask.rectTransform;
        detailMaskComp = detailMask.gameObject.AddComponent<Mask>();
        detailMaskComp.showMaskGraphic = false;
        CodexUi.Isolate(detailMask.gameObject);
        detailArt = CodexUi.NewImage("Art", detailArtBox, null, Color.white);
        detailArt.preserveAspect = true;
        CodexUi.Stretch(detailArt.rectTransform);
        detailAnim = CodexAnimator.On(detailArt);
        detailMask.raycastTarget = true;   // the art box takes the triple tap (a Mask draws nothing of it)
        detailMask.gameObject.AddComponent<CodexArtTap>().panel = this;
        // The art box is its own sub-canvas (CodexUi.Isolate): graphics on a
        // nested canvas are only seen by a GraphicRaycaster ON that canvas, so
        // without this the box swallowed nothing and received nothing.
        detailMask.gameObject.AddComponent<GraphicRaycaster>();
        detailFrame.raycastTarget = true;  // ... and so does the whole frame around it (a fat finger's margin)
        detailFrame.gameObject.AddComponent<CodexArtTap>().panel = this;

        detailIndex = CodexUi.NewText("Index", infoRoot, font, "", 16, CodexUi.Muted, TextAnchor.UpperRight);

        detailName = CodexUi.NewText("Name", infoRoot, font, "", 40, CodexUi.Title, TextAnchor.MiddleCenter, true);
        detailName.verticalOverflow = VerticalWrapMode.Truncate;
        detailName.resizeTextForBestFit = true;
        detailName.resizeTextMinSize = 22;
        detailName.resizeTextMaxSize = 40;
        CodexUi.AddOutline(detailName.gameObject, CodexUi.Ink, 3f);
        detailName.fontStyle = FontStyle.BoldAndItalic;

        detailPill = CodexUi.NewImage("Category", infoRoot, CodexUi.CodexSprite("cx_tab"), CodexUi.Accent, true);
        collectDetailFrame = NewCollectButton("CollectEntry", infoRoot, 26, 12, out collectDetailButton, out collectDetailLabel);
        collectDetailButton.onClick.AddListener(OnCollectDetail);
        watchFrame = NewCollectButton("WatchReplay", infoRoot, 24, 12, out watchButton, out watchLabel);
        watchFrame.color = CodexUi.Select;
        watchLabel.color = CodexUi.Body;
        watchLabel.text = WatchLabel;
        watchButton.onClick.AddListener(() => OpenReplay());
        detailPillLabel = CodexUi.NewText("Label", detailPill.rectTransform, font, "", 18, CodexUi.Ink,
                                          TextAnchor.MiddleCenter);
        CodexUi.Stretch(detailPillLabel.rectTransform);

        detailSubtitle = CodexUi.NewText("Subtitle", infoRoot, font, "", 20, CodexUi.Title, TextAnchor.MiddleCenter);
        CodexUi.AddOutline(detailSubtitle.gameObject, CodexUi.Ink, 1.5f);

        detailLoreCard = CodexUi.NewImage("LoreCard", infoRoot, CodexUi.CodexSprite("cx_card"), Color.white, true);
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
        lrt.offsetMin = new Vector2(30f, LoreBottom);
        lrt.offsetMax = new Vector2(-34f, -26f);
        BuildTally();
    
        // the sub-tab row (INFO | BACKDROPS) and the world's backdrop gallery under it
        subFrames = new Image[2];
        subLabels = new Text[2];
        for (int i = 0; i < 2; i++)
        {
            int index = i;
            string name = i == DetailTabInfo ? InfoLabel : BackdropsLabel;
            var f = CodexUi.NewImage("SubTab" + name, detail, CodexUi.CodexSprite("cx_tab"), CodexUi.Idle, true);
            f.raycastTarget = true;
            f.raycastPadding = new Vector4(-SubTabSplit * .5f, -SubTabGap * .5f, -SubTabSplit * .5f, -SubTabGap * .5f);
            var button = f.gameObject.AddComponent<Button>();
            button.transition = Selectable.Transition.None;
            button.targetGraphic = f;
            button.onClick.AddListener(() => SelectDetailTab(index));
            var label = CodexUi.NewText("Label", f.rectTransform, font, name, TabLabelSize, Color.white, TextAnchor.MiddleCenter);
            label.horizontalOverflow = HorizontalWrapMode.Wrap;
            label.verticalOverflow = VerticalWrapMode.Truncate;
            label.resizeTextForBestFit = true;
            label.resizeTextMinSize = TabLabelMinSize;
            label.resizeTextMaxSize = TabLabelSize;
            var srt = label.rectTransform;
            CodexUi.Stretch(srt);
            srt.offsetMin = new Vector2(6f, 0f);
            srt.offsetMax = new Vector2(-6f, 0f);
            subFrames[i] = f;
            subLabels[i] = label;
        }
        backdrops = new CodexBackdropsView(this, detail, font);
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
        lastW = ScreenInfo.Width;
        lastH = ScreenInfo.Height;
        lastSafe = ScreenInfo.SafeArea;
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
        headerTitleW = titleW;
        PlaceHeader();
        CodexUi.Place(divider.rectTransform, layout.divider);

        CodexUi.Place(tabsRoot, layout.tabs);
        for (int i = 0; i < TabCount; i++)
        {
            Rect t = TabRect(layout, i);   // panel-local -> the tab band's own centre-origin space
            t.center -= layout.tabs.center;
            CodexUi.Place(tabFrames[i].rectTransform, t);
        }

        gridRect = sectioned ? layout.list : layout.body;
        CodexUi.Place(grid, gridRect);
        CodexUi.Place(chipsRoot, layout.chips);
        CodexUi.Place(detail, layout.detail);
        CodexUi.Place(backSlot, layout.back);

        SizeDots();
        LayoutCards();
        LayoutDetail();
        ach.ApplyLayout(layout.body, layout);
        music.ApplyLayout(layout.body, layout);
        LayoutSubTabs();
    }

    float headerTitleW;
    public const float CollectTabWidth = 230f;

    // The header's counter, and the COLLECT ALL chip at its right end while the open tab has rewards waiting.
    void PlaceHeader()
    {
        var h = layout.header;
        float left = h.xMin + 40f + headerTitleW + 16f;
        bool chip = collectTabFrame != null && collectTabFrame.gameObject.activeSelf;
        float chipW = Mathf.Clamp(h.width * .32f, 160f, CollectTabWidth);   // narrow panels leave the counter its room
        float right = chip ? h.xMax - chipW - 12f : h.xMax;
        CodexUi.Place(counter.rectTransform, new Rect(left, h.yMin, Mathf.Max(0f, right - left), h.height));
        if (collectTabFrame != null)
            CodexUi.Place(collectTabFrame.rectTransform, CodexUi.Centered(h.xMax - chipW * .5f, h.center.y, chipW, h.height - 8f));
    }

    void SizeDots()
    {
        float d = DotUnits();
        if (tabDots != null) foreach (var dot in tabDots) if (dot != null) dot.rectTransform.sizeDelta = new Vector2(d, d);
        if (cards != null) foreach (var c in cards) if (c != null && c.newDot != null) c.newDot.rectTransform.sizeDelta = new Vector2(d, d);
    }

    void LayoutCards()
    {
        float w = layout.cardWidth, h = layout.cardHeight;
        float gridW = layout.body.width - 2f * GridInset;
        float art = Mathf.Min(w - 36f, h - CardNameHeight - 14f);
        int cols = layout.columns;

        // Sections stack down the content: header bar, its card rows, a gap.
        float y = GridInset;
        int sectionCount = Mathf.Min(sections.Count, MaxSections);
        for (int s = 0; s < sectionCount; s++)
        {
            if (s > 0) y += SectionGap;
            sectionTop[s] = y;
            if (sectioned)
            {
                var hr = headers[s].rt;
                hr.anchorMin = hr.anchorMax = new Vector2(.5f, 1f);
                hr.pivot = new Vector2(.5f, 1f);
                hr.anchoredPosition = new Vector2(0f, -y);
                hr.sizeDelta = new Vector2(gridW, SectionHeaderHeight);
                y += SectionHeaderHeight + SectionHeaderGap;
            }
            int n = sections[s].entries.Count;
            for (int k = 0; k < n; k++)
            {
                int i = sectionStart[s] + k;
                if (i >= cards.Length) break;
                int col = k % cols, row = k / cols;
                var c = cards[i];
                c.rt.anchorMin = c.rt.anchorMax = new Vector2(.5f, 1f);
                c.rt.pivot = new Vector2(.5f, .5f);
                c.rt.anchoredPosition = new Vector2(-gridW * .5f + w * .5f + col * (w + Gap), -(y + h * .5f + row * (h + Gap)));
                c.rt.sizeDelta = new Vector2(w, h);
            }
            int rows = (n + cols - 1) / cols;
            if (rows > 0) y += rows * h + (rows - 1) * Gap;
        }
        for (int i = 0; i < cards.Length; i++)
        {
            var c = cards[i];
            CodexUi.Place(c.artBox, CodexUi.Centered(0f, h * .5f - 14f - art * .5f, art, art));
            c.anim.Relayout();
            CodexUi.Place(c.name.rectTransform, CodexUi.Centered(0f, -h * .5f + 8f + CardNameHeight * .5f, w - 20f, CardNameHeight));
            CodexUi.Place(c.lockIcon.rectTransform, CodexUi.Centered(w * .5f - 24f, h * .5f - 24f, 24f, 24f));
        }
        float height = shownCount <= 0 && !sectioned ? 0f : y + GridInset;
        content.sizeDelta = new Vector2(0f, height);

        // Jump chips share the row evenly.
        int chipCount = sectioned ? sectionCount : 0;
        if (chipCount > 0)
        {
            float gap = ChipGapFor(chipCount);
            float cw = (layout.chips.width - gap * (chipCount - 1)) / chipCount;
            for (int i = 0; i < chipCount; i++)
            {
                float x = -layout.chips.width * .5f + cw * .5f + i * (cw + gap);
                CodexUi.Place(chips[i].rt, CodexUi.Centered(x, 0f, cw, layout.chips.height));
            }
        }
        // A resize can move the headers under the pinned one.
        stickySection = -2;
        UpdateSticky();
    }

    // The detail art never gives up more than this (units) for the lore.
    public const float MinDetailArt = 140f;

    // Height the sub-tab row takes off the top of the detail (0 when it is not shown).
    float SubRowHeight { get { return subTabsShown ? SubTabHeight + SubTabGap : 0f; } }

    void LayoutDetail()
    {
        var d = layout.detail;
        float art = Mathf.Min(d.width * .5f, (d.height - SubRowHeight) * .34f, 300f);
        // On a short canvas (UiScale's floor on a phone small in points /
        // dp) the lore may not fit even at its smallest type: the art gives
        // up room, a step at a time, until it does (or reaches MinDetailArt).
        float minArt = Mathf.Min(art, MinDetailArt);
        float start = art;
        SetTallySide(false, art);
        float loreTop = PlaceDetail(art);
        while (art > minArt && !LoreFits(loreTop + d.height * .5f))
        {
            art = Mathf.Max(minArt, art - 16f);
            loreTop = PlaceDetail(art);
        }
        // A long lore on a short, wide canvas (a tablet): the VERSUS YOU table moves
        // beside the art, where there is room, and the lore gets the whole card.
        if (tallyShown && !LoreFits(loreTop + d.height * .5f, ReadableLore) && SideRoom(start) >= TallySideMin)
        {
            art = start;
            SetTallySide(true, art);
            loreTop = PlaceDetail(art);
            while (art > minArt && (!LoreFits(loreTop + d.height * .5f, ReadableLore) || SideRoom(art) < TallySideMin))
            {
                art = Mathf.Max(minArt, art - 16f);
                loreTop = PlaceDetail(art);
            }
            PlaceTallySide(art);
        }
    }

    // ---- the table's two homes: the foot of the lore card, or beside the art ----

    public const float TallySideMin = 170f, TallySideGap = 16f;
    bool tallySide;
    float artCentreY;
    public bool TallyBesideArt { get { return tallySide; } }

    // Width free to the right of an art frame `art` units across.
    float SideRoom(float art)
    {
        return layout.detail.width * .5f - (art * .5f + 18f) - TallySideGap - 8f;
    }

    void SetTallySide(bool side, float art)
    {
        if (tallyRoot == null) return;
        tallySide = side && tallyShown;
        var lrt = detailLore.rectTransform;
        lrt.offsetMin = new Vector2(lrt.offsetMin.x, LoreBottom + (tallyShown && !tallySide ? TallyHeight + TallyGap : 0f));
        for (int i = 0; i < TallyColumns; i++)
        {
            var h = tallyHeads[i].rectTransform;
            var v = tallyValues[i].rectTransform;
            if (tallySide)
            {
                float top = 1f - i / (float)TallyColumns, bottom = 1f - (i + 1) / (float)TallyColumns;
                float split = bottom + (top - bottom) * .62f;
                h.anchorMin = new Vector2(0f, split); h.anchorMax = new Vector2(1f, top);
                v.anchorMin = new Vector2(0f, bottom); v.anchorMax = new Vector2(1f, split);
            }
            else
            {
                h.anchorMin = new Vector2(i / (float)TallyColumns, .62f); h.anchorMax = new Vector2((i + 1) / (float)TallyColumns, 1f);
                v.anchorMin = new Vector2(i / (float)TallyColumns, 0f); v.anchorMax = new Vector2((i + 1) / (float)TallyColumns, .62f);
            }
            h.offsetMin = v.offsetMin = new Vector2(4f, 0f);
            h.offsetMax = v.offsetMax = new Vector2(-4f, 0f);
        }
        if (tallySide)
        {
            tallyRoot.SetParent(infoRoot, false);
        }
        else
        {
            tallyRoot.SetParent(detailLoreCard.rectTransform, false);
            tallyRoot.anchorMin = new Vector2(0f, 0f);
            tallyRoot.anchorMax = new Vector2(1f, 0f);
            tallyRoot.pivot = new Vector2(.5f, 0f);
            tallyRoot.offsetMin = new Vector2(30f, LoreBottom);
            tallyRoot.offsetMax = new Vector2(-34f, LoreBottom + TallyHeight);
        }
    }

    void PlaceTallySide(float art)
    {
        if (!tallySide) return;
        float left = art * .5f + 18f + TallySideGap;
        float right = layout.detail.width * .5f - 8f;
        float frame = art + 36f;
        float bottom = artCentreY - frame * .5f;
        float top = artCentreY + frame * .5f - 36f;   // clear of the entry number in the corner
        CodexUi.Place(tallyRoot, Rect.MinMaxRect(left, bottom, right, Mathf.Max(bottom + 120f, top)));
    }

    // The lore size the table must never squeeze the lore below (the codex's readable floor).
    public const int ReadableLore = 18;

    // Whether the lore, at its best-fit minimum size (or `size`), fits a lore card this tall.
    bool LoreFits(float cardHeight, int size = 0)
    {
        if (string.IsNullOrEmpty(detailLore.text)) return true;
        var lrt = detailLore.rectTransform;
        float width = layout.detail.width + lrt.offsetMax.x - lrt.offsetMin.x;
        float room = cardHeight - lrt.offsetMin.y + lrt.offsetMax.y;
        var settings = detailLore.GetGenerationSettings(new Vector2(width, 0f));
        settings.resizeTextForBestFit = false;
        settings.fontSize = size > 0 ? size : detailLore.resizeTextMinSize;
        settings.verticalOverflow = VerticalWrapMode.Overflow;
        float ppu = Mathf.Max(detailLore.pixelsPerUnit, .0001f);
        return detailLore.cachedTextGeneratorForLayout.GetPreferredHeight(detailLore.text, settings) / ppu <= room;
    }

    // Lays the detail out around `art` units of art; returns the lore card's top.
    float PlaceDetail(float art)
    {
        var d = layout.detail;
        float w = d.width, top = d.height * .5f - SubRowHeight;

        // Art frame flush with the top, then name, category pill, the
        // optional subtitle, and the lore card taking whatever is left.
        float y = top - 18f - art * .5f;
        artCentreY = y;
        CodexUi.Place(detailFrame.rectTransform, CodexUi.Centered(0f, y, art + 36f, art + 36f));
        CodexUi.Place(detailArtBox, CodexUi.Centered(0f, 0f, art, art));
        detailAnim.Relayout();
        CodexUi.Place(detailIndex.rectTransform, new Rect(w * .5f - 120f, top - 30f, 120f, 30f));
        y -= art * .5f + 18f;

        y -= 12f + 30f;
        CodexUi.Place(detailName.rectTransform, CodexUi.Centered(0f, y, w - 40f, 60f));
        y -= 30f;

        y -= 10f + 18f;
        CodexUi.Place(detailPill.rectTransform, CodexUi.Centered(0f, y, 200f, 36f));
        // COLLECT sits right of the pill, inside the detail's edge (sides stay clear of the pill at any width)
        float chipW = Mathf.Min(260f, w * .5f - 100f - 16f - 8f);
        CodexUi.Place(collectDetailFrame.rectTransform, Rect.MinMaxRect(100f + 16f, y - 22f, 100f + 16f + chipW, y + 22f));
        CodexUi.Place(watchFrame.rectTransform, Rect.MinMaxRect(100f + 16f, y - 22f, 100f + 16f + chipW, y + 22f));
        y -= 18f;

        if (detailSubtitle.gameObject.activeSelf && detailSubtitle.text.Length > 0)
        {
            y -= 8f + 16f;
            CodexUi.Place(detailSubtitle.rectTransform, CodexUi.Centered(0f, y, w - 40f, 32f));
            y -= 16f;
        }
        CodexUi.Place(detailLoreCard.rectTransform, Rect.MinMaxRect(-w * .5f, -d.height * .5f, w * .5f, y - 16f));
        return y - 16f;
    }

    // ---------------------------------------------------------------------
    // State
    // ---------------------------------------------------------------------

    void Show()
    {
        gameObject.SetActive(true);
        // Old progress becomes achievements the first time (and derived ones are current).
        AchievementMigration.RunIfNeeded();
        Codex.Reload();
        AchievementTracker.RefreshCodex();
        if (achievementsOpen) HideAchievements();
        if (musicOpen) HideMusic();
        HideWorldBackdrops();
        RefreshCounter();
        // The codex owns Back/Escape while it is up (BackNavigator layer), so
        // the home screen's back-to-quit never sees those presses.
        BackNavigator.Register(this, OnBackPressed);
        CodexTheme.Open();

        inDetail = false;
        detailEntry = null;
        Fit();
        Populate(category);
        Codex.AckTab(category);
        PaintNewDots();
        phase = Phase.Opening;
        phaseAt = Time.unscaledTime;
        swapAt = tabAt = -10f;
        ApplyFrame(0f);
    }

    public void Close()
    {
        if (phase == Phase.Hidden || phase == Phase.Closing) return;
        if (CodexBackdropViewer.IsOpen) CodexBackdropViewer.Current.Close();
        if (CodexReplayViewer.IsOpen) CodexReplayViewer.Current.Close();
        CodexMusicPlayer.Stop();
        CodexTheme.Close();   // the codex song ends and the menu music comes back as the codex closes
        phase = Phase.Closing;
        phaseAt = Time.unscaledTime;
    }

    // BackNavigator layer: consumes every press until fully hidden (a press
    // during the close animation must not fall through to quit).
    bool OnBackPressed()
    {
        if (phase == Phase.Hidden) return false;
        Back();
        return true;
    }

    // Back button / Escape: detail -> grid -> closed.
    public void Back()
    {
        if (phase != Phase.Open && phase != Phase.Opening) return;
        if (achievementsOpen && ach.InDetail) ach.CloseDetail();
        else if (inDetail && detailTab == DetailTabBackdrops) SelectDetailTab(DetailTabInfo);   // gallery -> world detail
        else if (inDetail) ShowGrid();
        else Close();
    }

    public void ShowCategory(CodexCategory c)
    {
        ShowCategoryInner(c);
        Codex.AckTab(c);   // the tab was interacted with: its dot (and home count share) goes
        PaintNewDots();
    }

    void ShowCategoryInner(CodexCategory c)
    {
        if (inDetail) ShowGrid();
        if (OtherViewOpen)
        {
            HideAchievements();
            HideMusic();
            Populate(c);
            tabAt = Time.unscaledTime;
            return;
        }
        if (c == category && shownCount > 0) return;
        Populate(c);
        tabAt = Time.unscaledTime;
    }

    // A tab button: 0-5 are the entry categories, AchievementsTab the achievements.
    public void ShowTab(int i)
    {
        feedbackUntil = 0f;
        if (i == AchievementsTab) ShowAchievements();
        else if (i == MusicTab) ShowMusic();
        else if (i >= 0 && i < Tabs.Length) ShowCategory(Tabs[i]);
    }

    public void ShowAchievements()
    {
        if (inDetail) ShowGrid();
        if (achievementsOpen) { ach.CloseDetail(); Codex.AckAchievements(); PaintNewDots(); return; }
        HideMusic();
        achievementsOpen = true;
        grid.gameObject.SetActive(false);
        chipsRoot.gameObject.SetActive(false);
        ach.ApplyLayout(layout.body, layout);
        ach.Show();
        PaintTabs();
        RefreshCounter();
        tabAt = Time.unscaledTime;
        Codex.AckAchievements();
        PaintNewDots();
    }

    public void ShowMusic()
    {
        if (inDetail) ShowGrid();
        if (musicOpen) return;
        if (achievementsOpen) HideAchievements();
        musicOpen = true;
        grid.gameObject.SetActive(false);
        chipsRoot.gameObject.SetActive(false);
        music.ApplyLayout(layout.body, layout);
        music.Show();
        PaintTabs();
        RefreshCounter();
        tabAt = Time.unscaledTime;
        PaintNewDots();
    }

    // A backdrop card was tapped: the full-screen viewer opens over the codex (BACK returns to this list).
    public CodexBackdropViewer OpenBackdropViewer(CodexBackdrop b)
    {
        return CodexBackdropViewer.Open(font, b);
    }

    // ---- the world detail's sub-tabs ----

    // Panel-free layout of the sub-tab row and the gallery under it (detail-local coordinates).
    void LayoutSubTabs()
    {
        if (subFrames == null) return;
        var d = layout.detail;
        float w = d.width, top = d.height * .5f;
        float each = (w - SubTabSplit) * .5f;
        for (int i = 0; i < 2; i++)
            CodexUi.Place(subFrames[i].rectTransform, new Rect(-w * .5f + i * (each + SubTabSplit), top - SubTabHeight, each, SubTabHeight));
        var body = Rect.MinMaxRect(-w * .5f, -d.height * .5f, w * .5f, top - SubTabHeight - SubTabGap);
        backdrops.ApplyLayout(body, layout);
        PaintSubTabs();
    }

    void PaintSubTabs()
    {
        if (subFrames == null) return;
        for (int i = 0; i < 2; i++)
        {
            bool shown = subTabsShown;
            subFrames[i].gameObject.SetActive(shown);
            bool active = i == detailTab;
            subFrames[i].color = active ? CodexUi.Select : CodexUi.Idle;
            subLabels[i].color = active ? CodexUi.Body : CodexUi.Muted;
        }
    }

    // Tapping INFO / BACKDROPS (only a discovered world's detail has them).
    public void SelectDetailTab(int tab)
    {
        if (!inDetail || !subTabsShown) return;
        SetDetailTab(tab);
        LayoutDetail();
        swapAt = Time.unscaledTime;
        PaintNewDots();
    }

    void SetDetailTab(int tab)
    {
        detailTab = subTabsShown ? tab : DetailTabInfo;
        bool gallery = detailTab == DetailTabBackdrops;
        infoRoot.gameObject.SetActive(!gallery);
        LayoutSubTabs();
        if (gallery)
        {
            int world = CodexCatalogue.WorldIndex(detailEntry.id);
            backdrops.Show(world);
        }
        else HideWorldBackdrops();
    }

    // Leaving the gallery (INFO, BACK, another tab, the codex closing): the viewer and the thumbnails go.
    void HideWorldBackdrops()
    {
        if (backdrops == null || !backdrops.Active) { detailTab = DetailTabInfo; return; }
        if (CodexBackdropViewer.IsOpen) CodexBackdropViewer.Current.Close();
        backdrops.Hide();
        detailTab = DetailTabInfo;
        if (infoRoot != null) infoRoot.gameObject.SetActive(true);
    }

    void HideMusic()
    {
        if (!musicOpen) return;
        musicOpen = false;
        music.Hide();   // stops the track, which gives the menu music back
        grid.gameObject.SetActive(true);
        chipsRoot.gameObject.SetActive(sectioned);
        RefreshCounter();
    }

    void HideAchievements()
    {
        if (!achievementsOpen) return;
        achievementsOpen = false;
        ach.Hide();
        grid.gameObject.SetActive(true);
        chipsRoot.gameObject.SetActive(sectioned);
        RefreshCounter();
    }

    // The tab row's selection colours (Kaneda red marks the selection).
    void PaintTabs()
    {
        for (int i = 0; i < TabCount; i++)
        {
            bool active = i == MusicTab ? musicOpen : i == AchievementsTab ? achievementsOpen : (!OtherViewOpen && Tabs[i] == category);
            tabFrames[i].color = active ? CodexUi.Select : CodexUi.Idle;
            tabLabels[i].color = active ? CodexUi.Body : CodexUi.Muted;
        }
    }

    // After a claim: the counter and the open view are current.
    public void OnAchievementsChanged()
    {
        RefreshCounter();
        PaintNewDots();
    }

    // The detail's text: the lore (LAST STAND describes the saved kill cam instead).
    static string DetailText(CodexEntry entry)
    {
        if (entry.id == KillCamArchive.CodexId) return KillCamArchive.Describe();
        return Codex.DisplayLore(entry);
    }

    // ---- the VERSUS YOU table (hostile entries: enemies, elites, bosses, hazards) ----

    public const float LoreBottom = 24f;
    public const float TallyHeight = 80f, TallyGap = 8f;
    public const int TallyColumns = 3;
    public static readonly string[] TallyHeadings = { "HURT YOU", "KILLED YOU", "OF YOUR DEATHS" };

    public static bool ShowsTally(CodexEntry e)
    {
        return e != null && (e.category == CodexCategory.Enemies || e.category == CodexCategory.Hazards);
    }

    // The table's three cells for an entry: hearts it took, deaths it dealt, its share of every death on record.
    public static string[] TallyCells(string id)
    {
        int share = DamageLog.ShareOfDeaths(id);
        return new[]
        {
            DamageLog.HurtCount(id).ToString("N0"),
            DamageLog.KilledCount(id).ToString("N0"),
            share < 0 ? "-" : share + "%",
        };
    }

    public RectTransform TallyRoot { get { return tallyRoot; } }
    public bool TallyShown { get { return tallyShown; } }
    public Text TallyValue(int column) { return tallyValues[column]; }
    public Text TallyHeading(int column) { return tallyHeads[column]; }

    // Three columns at the foot of the lore card: a small muted heading over a big number.
    // No rules or boxes: spacing, alignment and colour make the table.
    void BuildTally()
    {
        tallyRoot = CodexUi.NewRect("Tally", detailLoreCard.rectTransform);
        tallyRoot.anchorMin = new Vector2(0f, 0f);
        tallyRoot.anchorMax = new Vector2(1f, 0f);
        tallyRoot.pivot = new Vector2(.5f, 0f);
        tallyRoot.offsetMin = new Vector2(30f, LoreBottom);
        tallyRoot.offsetMax = new Vector2(-34f, LoreBottom + TallyHeight);
        tallyValues = new Text[TallyColumns];
        tallyHeads = new Text[TallyColumns];
        for (int i = 0; i < TallyColumns; i++)
        {
            var head = CodexUi.NewText("Head" + i, tallyRoot, font, TallyHeadings[i], 17, CodexUi.Muted, TextAnchor.LowerCenter);
            head.horizontalOverflow = HorizontalWrapMode.Wrap;
            head.resizeTextForBestFit = true;
            head.resizeTextMinSize = 11;
            head.resizeTextMaxSize = 17;
            var hrt = head.rectTransform;
            hrt.anchorMin = new Vector2(i / (float)TallyColumns, .62f);
            hrt.anchorMax = new Vector2((i + 1) / (float)TallyColumns, 1f);
            hrt.offsetMin = new Vector2(4f, 0f);
            hrt.offsetMax = new Vector2(-4f, 0f);
            tallyHeads[i] = head;

            var value = CodexUi.NewText("Value" + i, tallyRoot, font, "0", 40, CodexUi.Body, TextAnchor.MiddleCenter);
            value.fontStyle = FontStyle.Bold;
            value.resizeTextForBestFit = true;
            value.resizeTextMinSize = 20;
            value.resizeTextMaxSize = 40;
            CodexUi.AddOutline(value.gameObject, CodexUi.Ink, 2f);
            var vrt = value.rectTransform;
            vrt.anchorMin = new Vector2(i / (float)TallyColumns, 0f);
            vrt.anchorMax = new Vector2((i + 1) / (float)TallyColumns, .62f);
            vrt.offsetMin = new Vector2(4f, 0f);
            vrt.offsetMax = new Vector2(-4f, 0f);
            tallyValues[i] = value;
        }
        tallyRoot.gameObject.SetActive(false);
    }

    // Fills the table for `entry` (shown only for a discovered hostile entry) and
    // makes room for it under the lore.
    void PaintTally(CodexEntry entry, bool found)
    {
        tallyShown = found && ShowsTally(entry);
        tallyRoot.gameObject.SetActive(tallyShown);
        var lrt = detailLore.rectTransform;
        lrt.offsetMin = new Vector2(lrt.offsetMin.x, LoreBottom + (tallyShown ? TallyHeight + TallyGap : 0f));
        if (!tallyShown) return;
        var cells = TallyCells(entry.id);
        int hurt = DamageLog.HurtCount(entry.id), killed = DamageLog.KilledCount(entry.id);
        for (int i = 0; i < TallyColumns; i++) tallyValues[i].text = cells[i];
        tallyValues[0].color = hurt > 0 ? CodexUi.Title : CodexUi.Muted;
        tallyValues[1].color = killed > 0 ? CodexUi.Select : CodexUi.Muted;
        tallyValues[2].color = killed > 0 ? CodexUi.Body : CodexUi.Muted;
    }

    public void ShowDetail(CodexEntry entry)
    {
        if (entry == null) return;
        detailEntry = entry;
        bool found = Codex.IsDiscovered(entry);
        subTabsShown = found && entry.category == CodexCategory.Worlds && CodexCatalogue.WorldIndex(entry.id) >= 0;
        SetDetailTab(DetailTabInfo);
        if (found) Codex.MarkSeen(entry.id);   // opening the entry clears its NEW dot

        detailName.text = Codex.DisplayName(entry);
        detailPillLabel.text = CategoryLabel(entry.category);
        detailPill.color = found ? CodexUi.Accent : CodexUi.Locked;
        string sub = found ? entry.Subtitle : null;
        detailSubtitle.text = sub ?? string.Empty;
        detailSubtitle.gameObject.SetActive(sub != null);
        detailLore.text = found ? DetailText(entry) : LockedHint;
        PaintTally(entry, found);
        detailLore.color = found ? CodexUi.Body : CodexUi.Muted;
        detailLore.alignment = found ? TextAnchor.UpperLeft : TextAnchor.MiddleCenter;
        detailLoreEdge.color = found ? CodexUi.Accent : CodexUi.Locked;
        detailFrameEdge.color = found ? CodexUi.Accent : CodexUi.Locked;

        int number = Array.IndexOf(Codex.Entries, entry) + 1;
        detailIndex.text = "No. " + number.ToString("00");

        ApplyArt(detailArt, detailMask, detailMaskComp, entry, found);
        detailAnim.Bind(CodexAnimations.For(entry), !found, true);
        artTaps = 0;
        tapKey = null;
        pulsing = false;
        detailArtBox.localScale = Vector3.one;
        LayoutDetail();

        inDetail = true;
        swapAt = Time.unscaledTime;
        PaintNewDots();
    }

    // ---- Triple tap on the enemy: its death plays once ----
    // Three quick taps on the enemy in the detail view play the death (the
    // tap that opens the detail does not count). Each of the first
    // two taps squashes the art a hair so the player sees the count running.
    public const float TripleTapWindow = .7f;
    public const float PulseSeconds = .16f, PulseSquash = .07f;
    // Test seam for the clock the taps are stamped with (unscaled seconds).
    public static System.Func<float> TapClock;
    int artTaps;
    float lastArtTap = -100f;
    string tapKey;          // the entry the counter belongs to
    float pulseAt = -10f;
    bool pulsing;

    static float TapNow() { return TapClock != null ? TapClock() : Time.unscaledTime; }
    public int ArtTaps { get { return artTaps; } }
    public bool Pulsing { get { return pulsing; } }

    // One tap on the detail art at `now` (unscaled seconds). Three within
    // TripleTapWindow of each other play the entry's death strip + sound.
    // Returns true when that tap started the death. Taps are accepted while
    // the detail is sliding in (the art is already bound and visible).
    public bool TapDetailArt(float now)
    {
        if (!inDetail || (phase != Phase.Open && phase != Phase.Opening) || detailEntry == null ||
            !Codex.IsDiscovered(detailEntry) || detailAnim.Dying)
        {
            artTaps = 0;
            return false;
        }
        return CountTap(now, detailEntry.id);
    }

    public void TapDetailArt() { TapDetailArt(TapNow()); }

    bool CountTap(float now, string key)
    {
        bool same = key == tapKey && now - lastArtTap <= TripleTapWindow;
        artTaps = same ? artTaps + 1 : 1;
        tapKey = key;
        lastArtTap = now;
        if (artTaps < 3)
        {
            pulseAt = Time.unscaledTime;   // feedback: squash on tap 1 and 2
            pulsing = true;
            return false;
        }
        artTaps = 0;
        return PlayDetailDeath();
    }

    // A squash of the art box that settles back; arithmetic only.
    void UpdatePulse()
    {
        if (!pulsing) return;
        float k = (Time.unscaledTime - pulseAt) / PulseSeconds;
        if (k >= 1f) { pulsing = false; detailArtBox.localScale = Vector3.one; return; }
        float q = Mathf.Sin(k * Mathf.PI) * PulseSquash;
        detailArtBox.localScale = new Vector3(1f + q * .5f, 1f - q, 1f);
    }

    // Enemies play EnemyDeathFlipbook's three drawings; elites their own
    // `_death` strip when they ship one (none do yet), else the composed
    // break-up (hit drawing, then shards / sparks / ring: CodexBurst); bosses
    // their 6-cell death strip where one exists (all five bosses), else nothing. Mines
    // and anything else without a strip do nothing rather than risk a
    // mismatched pose.
    static readonly float[] BossDeathHolds = MakeHolds(BossArt.DeathStripCells, BossArt.DeathStripCellSeconds);
    public const float EliteStripFps = 12f;   // EliteDeath: Flipbook(death, 12f, ...)
    CodexBurst burst;

    static float[] MakeHolds(int n, float each)
    {
        var h = new float[n];
        for (int i = 0; i < n; i++) h[i] = each;
        return h;
    }

    // What the last triple tap played: "enemy", "elite-strip", "elite-burst", "boss-strip" or "" (tests, previews).
    public string LastDeathKind { get; private set; }

    bool PlayDetailDeath()
    {
        LastDeathKind = "";
        string id = detailEntry.id;
        var boss = BossCatalog.Find(id);
        if (boss != null)
        {
            if (!BossArt.HasDeathArt(boss)) return false;
            var frames = new Sprite[BossArt.DeathStripCells];
            for (int i = 0; i < frames.Length; i++) frames[i] = BossArt.DeathStrip(boss, i);
            if (!detailAnim.PlayDeath(frames, BossDeathHolds)) return false;
            LastDeathKind = "boss-strip";
            EnemyDeathAudio.PlayBossDeath(boss.artKey);
            return true;
        }
        var elite = EliteCatalog.FindByCodexId(id);
        if (elite != null) return PlayEliteDeath(elite);
        var def = EnemyRoster.FindByCodexId(id);
        if (def == null) return false;
        var enemy = EnemyDeathFlipbook.Frames(def);
        if (enemy == null || !detailAnim.PlayDeath(enemy)) return false;
        EnemyDeathAudio.PlayKey(def.key, def.role);
        LastDeathKind = "enemy";
        return true;
    }

    bool PlayEliteDeath(EliteDef def)
    {
        var strip = EliteArt.ExtraFrames(def, EliteArt.Extra.Death);
        if (strip != null)
        {
            if (!detailAnim.PlayDeath(strip, MakeHolds(strip.Length, 1f / EliteStripFps))) return false;
            LastDeathKind = "elite-strip";
        }
        else
        {
            var frames = EliteArt.Frames(def);
            if (frames == null || detailAnim.Box == null) return false;
            if (burst == null || !burst.Alive) burst = new CodexBurst(detailAnim.Box);
            burst.Fit(detailAnim.Box);
            if (!burst.Prepare(def, detailAnim.UnitScale())) return false;
            var flash = frames[Mathf.Clamp(def.cells.hit >= 0 ? def.cells.hit : def.cells.Flight0, 0, frames.Length - 1)];
            if (!detailAnim.PlayBurst(burst, flash)) return false;
            LastDeathKind = "elite-burst";
        }
        EnemyDeathAudio.PlayElite(def.key);
        return true;
    }

    public void ShowGrid()
    {
        artTaps = 0;
        if (!inDetail) return;
        inDetail = false;
        HideWorldBackdrops();
        swapAt = Time.unscaledTime;
        PaintNewDots();
        // Unlocks can't change while the panel is open, but names can be
        // re-read cheaply here if they ever do.
    }

    void OnCardClicked(int index)
    {
        if (index < 0 || index >= shownCount || inDetail) return;
        ShowDetail(cards[index].entry);
    }

    void Populate(CodexCategory c, bool keepScroll = false)
    {
        float keepY = content.anchoredPosition.y;
        category = c;
        sectioned = IsSectioned(c);
        sections = SectionsFor(c);
        shownCount = 0;
        for (int s = 0; s < sections.Count && s < MaxSections; s++)
        {
            sectionStart[s] = shownCount;
            foreach (var e in sections[s].entries)
            {
                if (shownCount >= cards.Length) break;
                var card = cards[shownCount++];
                card.entry = e;
                bool found = Codex.IsDiscovered(e);
                card.name.text = Codex.DisplayName(e);
                card.name.color = found ? CodexUi.Body : CodexUi.Muted;
                card.edge.color = found ? CodexUi.Accent : CodexUi.Locked;
                card.lockIcon.gameObject.SetActive(!found);
                ApplyArt(card.art, card.mask, card.maskComp, e, found);
                card.anim.Bind(CodexAnimations.For(e), !found, false, (shownCount - 1) * CardPhaseStep);
            }
        }
        for (int i = 0; i < cards.Length; i++)
        {
            cards[i].rt.gameObject.SetActive(i < shownCount);
            cards[i].anim.Ticking = false;
        }

        for (int i = 0; i < MaxSections; i++)
        {
            bool on = sectioned && i < sections.Count;
            headers[i].rt.gameObject.SetActive(on);
            chips[i].rt.gameObject.SetActive(on);
            if (!on) continue;
            PaintBar(headers[i], sections[i]);
            chips[i].label.text = sections[i].label;
        }
        chipsRoot.gameObject.SetActive(sectioned);

        PaintTabs();
        PaintNewDots();

        gridRect = sectioned ? layout.list : layout.body;
        CodexUi.Place(grid, gridRect);
        jumping = false;
        jumpTarget = -1;
        activeChip = -2;   // repaint the chips
        LayoutCards();
        scroll.velocity = Vector2.zero;
        SetScrollY(keepScroll ? Mathf.Clamp(keepY, 0f, MaxScroll) : 0f);
    }

    // ---------------------------------------------------------------------
    // Scrolling: pinned header and jump chips
    // ---------------------------------------------------------------------

    // Smooth-scrolls (unscaled time) so the section's header sits at the top
    // of the list, or as close as the end of the list allows.
    public void JumpToSection(int s)
    {
        if (!sectioned || s < 0 || s >= sections.Count || s >= MaxSections || inDetail) return;
        scroll.StopMovement();
        jumpFrom = content.anchoredPosition.y;
        jumpTo = JumpTargetY(s);
        jumpAt = Time.unscaledTime;
        lastSetY = jumpFrom;
        jumping = true;
        jumpTarget = s;
        UpdateSticky();
    }

    // Where a jump to the section lands: its header at the top of the list.
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

    void UpdateJump(float now)
    {
        if (!jumping) return;
        // The player grabbed the list mid-jump: let go of it.
        if (Mathf.Abs(content.anchoredPosition.y - lastSetY) > .5f) { jumping = false; return; }
        float t = (now - jumpAt) / JumpDuration;
        SetScrollY(Mathf.LerpUnclamped(jumpFrom, jumpTo, CodexUi.EaseOutCubic(t)));
        if (t >= 1f) jumping = false;
    }

    void OnScrolled(Vector2 normalized)
    {
        // Moved by a drag or inertia rather than a jump: the chip follows
        // the list again.
        if (!jumping && Mathf.Abs(content.anchoredPosition.y - lastSetY) > .5f)
        {
            jumpTarget = -1;
            lastSetY = content.anchoredPosition.y;
        }
        UpdateSticky();
    }

    // The section whose header has reached the top of the list, or -1.
    public int SectionAtScroll(float y)
    {
        int s = -1;
        if (!sectioned) return s;
        for (int i = 0; i < sections.Count && i < MaxSections; i++)
            if (sectionTop[i] <= y + .01f) s = i;
        return s;
    }

    // Arithmetic and, only when the section changes, a repaint: nothing
    // allocates while scrolling.
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
            // The next header pushes the pinned one up and out.
            float push = 0f;
            if (s + 1 < sections.Count && s + 1 < MaxSections)
                push = Mathf.Max(0f, SectionHeaderHeight - (sectionTop[s + 1] - y));
            if (!Mathf.Approximately(sticky.rt.anchoredPosition.y, push))
                sticky.rt.anchoredPosition = new Vector2(0f, push);
        }

        int chip = !sectioned ? -1 : jumpTarget >= 0 ? jumpTarget : Mathf.Max(s, 0);
        if (chip != activeChip)
        {
            activeChip = chip;
            for (int i = 0; i < sections.Count && i < MaxSections; i++)
            {
                bool on = i == chip;
                chips[i].frame.color = on ? sections[i].color : CodexUi.Idle;
                chips[i].label.color = on ? CodexUi.Ink : sections[i].color;
            }
        }
    }

    static void ApplyArt(Image art, Image mask, Mask maskComp, CodexEntry e, bool found)
    {
        art.sprite = e.Sprite;
        art.enabled = art.sprite != null;
        // Locked: "who's that Pokemon?" -- the sprite's own shape as one
        // flat ink colour (CodexUi.SilhouetteMaterial), no interior detail.
        CodexUi.PaintArt(art, !found);
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
        if (achievementsOpen) { ach.Show(true); return; }
        if (musicOpen) { music.Paint(); return; }
        var open = inDetail ? detailEntry : null;
        int tab = detailTab;
        Populate(category, true);
        if (open != null)
        {
            ShowDetail(open);
            if (subTabsShown && tab == DetailTabBackdrops) SelectDetailTab(tab);
            swapAt = -100f;
        }
        ApplyFrame(Time.unscaledTime);
    }

    void RefreshCounter()
    {
        if (Time.unscaledTime < feedbackUntil && !musicOpen && !achievementsOpen) { counter.text = feedbackText; return; }
        counter.text = musicOpen
            ? CodexMusic.UnlockedCount + " / " + CodexMusic.ListedCount + "  HEARD"
            : achievementsOpen
            ? AchievementStore.UnlockedCount + " / " + AchievementCatalog.ActiveCount + "  UNLOCKED"
            : Codex.DiscoveredCount + " / " + Codex.Total + (collectTabFrame != null && collectTabFrame.gameObject.activeSelf ? "" : "  DISCOVERED");
    }

    // ---------------------------------------------------------------------
    // Animation
    // ---------------------------------------------------------------------

    void Update()
    {
        if (ScreenInfo.Width != lastW || ScreenInfo.Height != lastH || ScreenInfo.SafeArea != lastSafe) Fit();
        if (feedbackUntil > 0f && Time.unscaledTime >= feedbackUntil) { feedbackUntil = 0f; RefreshCounter(); }
        UpdateJump(Time.unscaledTime);
        ApplyFrame(Time.unscaledTime);
        UpdatePulse();
        if (achievementsOpen) ach.Tick(Time.unscaledTime);
        if (musicOpen) music.Tick();
        if (phase != Phase.Hidden) TickAnimations(Time.unscaledDeltaTime);
    }

    // ---------------------------------------------------------------------
    // Art animation
    // ---------------------------------------------------------------------

    // Advances the art by dt seconds (unscaled; timeScale never enters it):
    // the cards inside the list's viewport while the grid shows, and the
    // detail art while the detail view shows. Everything else holds its
    // drawing and is marked not ticking. Arithmetic only, no allocation.
    public void TickAnimations(float dt)
    {
        bool gridShown = phase != Phase.Hidden && gridGroup.alpha > .001f;
        float top = content.anchoredPosition.y;
        float bottom = top + viewport.rect.height;
        for (int i = 0; i < cards.Length; i++)
        {
            var a = cards[i].anim;
            bool on = gridShown && i < shownCount && CardInView(i, top, bottom);
            a.Ticking = on && a.Animates;
            if (a.Ticking) a.Advance(dt);
        }
        detailAnim.Ticking = phase != Phase.Hidden && detailEntry != null && detailGroup.alpha > .001f &&
                             (detailAnim.Animates || detailAnim.Dying);
        if (detailAnim.Ticking) detailAnim.Advance(dt);
    }

    // Is any part of card i inside the viewport (content-space y, from the top)?
    public bool CardOnScreen(int i)
    {
        float top = content.anchoredPosition.y;
        return i >= 0 && i < shownCount && CardInView(i, top, top + viewport.rect.height);
    }

    bool CardInView(int i, float top, float bottom)
    {
        var rt = cards[i].rt;
        float centre = -rt.anchoredPosition.y;
        float half = rt.sizeDelta.y * .5f;
        return centre + half > top && centre - half < bottom;
    }

    void StopAnimations()
    {
        for (int i = 0; i < cards.Length; i++) cards[i].anim.Ticking = false;
        detailAnim.Ticking = false;
    }

    // Jump every running transition to its end (tests, and a tap mid-intro
    // never has to wait).
    public void SkipAnimations()
    {
        phaseAt = -100f;
        swapAt = tabAt = -100f;
        if (jumping) { jumpAt = -100f; UpdateJump(Time.unscaledTime); }
        if (achievementsOpen) ach.SkipAnimations();
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
                CodexMusicPlayer.Stop();
                CodexTheme.Close();
                HideWorldBackdrops();   // the thumbnails go with the panel
                StopAnimations();
                BackNavigator.Unregister(this);
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
        gridGroup.alpha = OtherViewOpen ? 0f : (1f - d) * tab;
        gridGroup.interactable = !inDetail && !OtherViewOpen;
        gridGroup.blocksRaycasts = !inDetail && !OtherViewOpen;
        if (achievementsOpen) ach.SetFade(tab);
        if (musicOpen) music.SetFade(tab);
        grid.anchoredPosition = gridRect.center + new Vector2(-d * 48f, (1f - tab) * -18f);
        chipsGroup.alpha = OtherViewOpen ? 0f : (1f - d) * tab;
        chipsGroup.interactable = !inDetail && !OtherViewOpen;
        chipsGroup.blocksRaycasts = !inDetail && !OtherViewOpen;
        // The detail view takes over the tab row's space too.
        tabsGroup.alpha = 1f - d;
        tabsGroup.interactable = !inDetail;
        tabsGroup.blocksRaycasts = !inDetail;

    }
}

// Forwards a tap on the detail art box to the panel (triple tap = death).
public class CodexArtTap : MonoBehaviour, UnityEngine.EventSystems.IPointerClickHandler
{
    public CodexPanel panel;
    public void OnPointerClick(UnityEngine.EventSystems.PointerEventData e) { if (panel != null) panel.TapDetailArt(); }
}
