using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// The BACKDROPS sub-tab of a discovered world's codex detail (INFO | BACKDROPS): a header strip (how many of
// this world's backdrops the pilot has seen) above the scrolling card grid -- one card per variant of that
// world's backdrop. (Shown with no world filter it lists every world as sections.)
// A card shows the variant's real ground (or Space's sky) tile and its name; one the pilot has not
// flown over yet is a "???" silhouette with a lock and does nothing. Tapping an unlocked card opens
// the full-screen viewer (CodexBackdropViewer) on it; BACK returns here. Same cel cards, ink
// outlines and palette as the other tabs; the textures are loaded for unlocked cards only and
// dropped again when the tab is left.
public sealed class CodexBackdropsView
{
    public const float StripHeight = 96f, StripGap = 12f;
    // Clear space between the strip's top (and bottom) edge and its text rows, so the "n / m  SEEN"
    // count is not jammed against the edge (it was 6 px, now 14 px). Strip-relative, so it holds on
    // every aspect ratio.
    public const float StripTextInset = 14f;
    public const string LockedHint = "Not seen yet.";
    public const string IdleLine = "Tap a backdrop to look around. Swipe to browse.";

    sealed class Card
    {
        public RectTransform rt;
        public Image frame, edge, lockIcon, dot, collectFrame;
        public RawImage thumb;
        public RectTransform thumbBox;
        public Text name, collectLabel;
        public Button button, collectButton;
        public CodexBackdrop backdrop;
    }

    sealed class Header
    {
        public RectTransform rt;
        public Image frame;
        public Text label, counter;
    }

    readonly CodexPanel panel;
    readonly Font font;
    RectTransform root;
    CanvasGroup rootGroup;
    RectTransform strip;
    Text stripCount, stripLine;
    RectTransform grid, viewport, content;
    ScrollRect scroll;
    Card[] cards;
    Header[] headers;
    readonly List<int> headerWorlds = new List<int>();     // world index of each shown header
    readonly List<int> headerStart = new List<int>();      // first card index of each shown header
    int shownCards, shownHeaders;
    Rect body, stripRect, listRect;
    int worldFilter = -1;      // WorldManager.Worlds index this gallery is limited to; -1 = every world
    CodexPanel.Layout layout;

    public CodexBackdropsView(CodexPanel panel, RectTransform parent, Font font)
    {
        this.panel = panel;
        this.font = font;
        Build(parent);
    }

    // ---- accessors for the tests ----
    public bool Active { get { return root.gameObject.activeSelf; } }
    public int VisibleCards { get { return shownCards; } }
    public int VisibleHeaders { get { return shownHeaders; } }
    public CodexBackdrop CardBackdrop(int i) { return cards[i].backdrop; }
    public Button CardButton(int i) { return cards[i].button; }
    public Text CardName(int i) { return cards[i].name; }
    public RectTransform CardRect(int i) { return cards[i].rt; }
    public RawImage CardThumb(int i) { return cards[i].thumb; }
    public Image CardLock(int i) { return cards[i].lockIcon; }
    public Image CardDot(int i) { return cards[i].dot; }
    public Button CardCollectButton(int i) { return cards[i].collectButton; }
    public Text CardCollectLabel(int i) { return cards[i].collectLabel; }
    public Image CardCollectFrame(int i) { return cards[i].collectFrame; }
    public Text HeaderLabel(int i) { return headers[i].label; }
    public Text HeaderCounter(int i) { return headers[i].counter; }
    public RectTransform HeaderRect(int i) { return headers[i].rt; }
    public int CardIndexOf(string id)
    {
        for (int i = 0; i < shownCards; i++) if (cards[i].backdrop.id == id) return i;
        return -1;
    }
    public int WorldFilter { get { return worldFilter; } }
    public Text StripCount { get { return stripCount; } }
    public Text StripLine { get { return stripLine; } }
    public RectTransform StripRect { get { return strip; } }
    public Rect ListRect { get { return listRect; } }
    public float ContentHeight { get { return content.sizeDelta.y; } }
    public RectTransform Viewport { get { return viewport; } }
    public int LoadedThumbnails
    {
        get { int n = 0; if (cards != null) foreach (var c in cards) if (c.thumb != null && c.thumb.texture != null) n++; return n; }
    }

    void Build(RectTransform parent)
    {
        root = CodexUi.NewRect("Backdrops", parent);
        rootGroup = root.gameObject.AddComponent<CanvasGroup>();

        strip = CodexUi.NewRect("Strip", root);
        var frame = CodexUi.NewImage("Frame", strip, CodexUi.CodexSprite("cx_card"), Color.white, true);
        CodexUi.Stretch(frame.rectTransform);
        var edge = CodexUi.NewImage("Edge", strip, CodexUi.CodexSprite("cx_card_edge"), CodexUi.Accent, true);
        CodexUi.Stretch(edge.rectTransform);
        stripCount = Label("Count", strip, 26, 14, CodexUi.Title, TextAnchor.MiddleLeft, 2f);
        stripLine = Label("Line", strip, 18, 11, CodexUi.Body, TextAnchor.MiddleLeft, 1.5f);

        var go = new GameObject("Grid", typeof(RectTransform), typeof(ScrollRect));
        go.transform.SetParent(root, false);
        grid = (RectTransform)go.transform;
        viewport = CodexUi.NewRect("Viewport", grid);
        CodexUi.Stretch(viewport);
        viewport.gameObject.AddComponent<RectMask2D>();
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

        int worlds = WorldManager.Worlds.Length;
        headers = new Header[worlds];
        for (int i = 0; i < worlds; i++) headers[i] = BuildHeader(i);
        cards = new Card[CodexBackdrops.All.Count];
        for (int i = 0; i < cards.Length; i++) cards[i] = BuildCard(i);
        root.gameObject.SetActive(false);
    }

    Text Label(string name, Transform parent, int size, int min, Color color, TextAnchor anchor, float outline)
    {
        var t = CodexUi.NewText(name, parent, font, "", size, color, anchor);
        t.horizontalOverflow = HorizontalWrapMode.Wrap;
        t.verticalOverflow = VerticalWrapMode.Truncate;
        t.resizeTextForBestFit = true;
        t.resizeTextMinSize = min;
        t.resizeTextMaxSize = size;
        CodexUi.AddOutline(t.gameObject, CodexUi.Ink, outline);
        return t;
    }

    Header BuildHeader(int world)
    {
        var h = new Header();
        h.frame = CodexUi.NewImage("Header" + world, content, CodexUi.CodexSprite("cx_tab"), Color.white, true);
        h.frame.raycastTarget = false;
        h.rt = h.frame.rectTransform;
        h.label = CodexUi.NewText("Label", h.rt, font, "", 26, CodexUi.Ink, TextAnchor.MiddleLeft);
        h.label.fontStyle = FontStyle.BoldAndItalic;
        h.label.horizontalOverflow = HorizontalWrapMode.Wrap;
        h.label.verticalOverflow = VerticalWrapMode.Truncate;
        h.label.resizeTextForBestFit = true;
        h.label.resizeTextMinSize = 14;
        h.label.resizeTextMaxSize = 26;
        var lrt = h.label.rectTransform;
        CodexUi.Stretch(lrt);
        lrt.offsetMin = new Vector2(28f, 0f);
        lrt.offsetMax = new Vector2(-124f, 0f);
        h.counter = CodexUi.NewText("Counter", h.rt, font, "", 22, CodexUi.Ink, TextAnchor.MiddleRight);
        var crt = h.counter.rectTransform;
        crt.anchorMin = new Vector2(1f, 0f);
        crt.anchorMax = new Vector2(1f, 1f);
        crt.pivot = new Vector2(1f, .5f);
        crt.anchoredPosition = new Vector2(-28f, 0f);
        crt.sizeDelta = new Vector2(96f, 0f);
        return h;
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
        c.edge = CodexUi.NewImage("Edge", c.rt, CodexUi.CodexSprite("cx_card_edge"), CodexUi.Accent, true);
        CodexUi.Stretch(c.edge.rectTransform);

        // the thumbnail: a square cut of the tile (RawImage uvRect), clipped to its box
        c.thumbBox = CodexUi.NewRect("ThumbBox", c.rt);
        c.thumbBox.gameObject.AddComponent<RectMask2D>();
        var thumbGo = new GameObject("Thumb", typeof(RectTransform), typeof(RawImage));
        thumbGo.transform.SetParent(c.thumbBox, false);
        c.thumb = thumbGo.GetComponent<RawImage>();
        c.thumb.raycastTarget = false;
        CodexUi.Stretch(c.thumb.rectTransform);

        c.name = Label("Name", c.rt, 18, 11, CodexUi.Body, TextAnchor.MiddleCenter, 1.5f);
        c.lockIcon = CodexUi.NewImage("Lock", c.rt, CodexUi.CodexSprite("cx_lock"), CodexUi.Select);
        c.lockIcon.preserveAspect = true;
        CodexUi.Isolate(c.lockIcon.gameObject);
        // a seen backdrop's star dust waits here until COLLECT (over the lower part of its picture)
        c.collectFrame = CodexUi.NewCollectChip("Collect", c.rt, font, out c.collectButton, out c.collectLabel);
        c.collectButton.onClick.AddListener(() => OnCollectClicked(index));
        c.dot = CodexUi.NewRewardDot(c.rt);
        return c;
    }

    // ---------------------------------------------------------------------
    // Layout
    // ---------------------------------------------------------------------

    public void ApplyLayout(Rect bodyRect, CodexPanel.Layout l)
    {
        body = bodyRect;
        layout = l;
        CodexUi.Place(root, bodyRect);
        float w = bodyRect.width, h = bodyRect.height;
        stripRect = new Rect(-w * .5f, h * .5f - StripHeight, w, StripHeight);
        listRect = Rect.MinMaxRect(-w * .5f, -h * .5f, w * .5f, stripRect.yMin - StripGap);
        float pad = 20f;
        CodexUi.Place(strip, stripRect);
        CodexUi.Place(stripCount.rectTransform, new Rect(-w * .5f + pad + 8f, 2f, w - 2f * pad - 16f, StripHeight * .5f - 2f - StripTextInset));
        CodexUi.Place(stripLine.rectTransform, new Rect(-w * .5f + pad + 8f, -StripHeight * .5f + StripTextInset, w - 2f * pad - 16f, StripHeight * .5f - 2f - StripTextInset));
        CodexUi.Place(grid, listRect);
        LayoutCards();
    }

    void LayoutCards()
    {
        if (cards == null) return;
        float gridW = listRect.width - 2f * CodexPanel.GridInset;
        int cols = Mathf.Max(2, layout.columns);
        float cw = (gridW - CodexPanel.Gap * (cols - 1)) / cols;
        float art = cw - 24f;
        float ch = art + CodexPanel.CardNameHeight + 6f;
        float y = CodexPanel.GridInset;
        for (int s = 0; s < shownHeaders; s++)
        {
            if (s > 0) y += CodexPanel.SectionGap;
            bool showHeader = worldFilter < 0;     // a one-world gallery needs no section header
            var hr = headers[headerWorlds[s]].rt;
            if (showHeader)
            {
                hr.anchorMin = hr.anchorMax = new Vector2(.5f, 1f);
                hr.pivot = new Vector2(.5f, 1f);
                hr.anchoredPosition = new Vector2(0f, -y);
                hr.sizeDelta = new Vector2(gridW, CodexPanel.SectionHeaderHeight);
                y += CodexPanel.SectionHeaderHeight + CodexPanel.SectionHeaderGap;
            }
            int end = s + 1 < shownHeaders ? headerStart[s + 1] : shownCards;
            int n = end - headerStart[s];
            for (int k = 0; k < n; k++)
            {
                var c = cards[headerStart[s] + k];
                int col = k % cols, row = k / cols;
                c.rt.anchorMin = c.rt.anchorMax = new Vector2(.5f, 1f);
                c.rt.pivot = new Vector2(.5f, .5f);
                c.rt.anchoredPosition = new Vector2(-gridW * .5f + cw * .5f + col * (cw + CodexPanel.Gap), -(y + ch * .5f + row * (ch + CodexPanel.Gap)));
                c.rt.sizeDelta = new Vector2(cw, ch);
            }
            int rows = (n + cols - 1) / cols;
            if (rows > 0) y += rows * ch + (rows - 1) * CodexPanel.Gap;
        }
        for (int i = 0; i < cards.Length; i++)
        {
            var c = cards[i];
            CodexUi.Place(c.thumbBox, CodexUi.Centered(0f, ch * .5f - 12f - art * .5f, art, art));
            CodexUi.Place(c.name.rectTransform, CodexUi.Centered(0f, -ch * .5f + 6f + CodexPanel.CardNameHeight * .5f, cw - 20f, CodexPanel.CardNameHeight));
            CodexUi.Place(c.lockIcon.rectTransform, CodexUi.Centered(cw * .5f - 24f, ch * .5f - 24f, 24f, 24f));
            CodexUi.Place(c.collectFrame.rectTransform, CodexUi.Centered(0f, ch * .5f - 12f - art + 34f, art - 16f, 52f));
        }
        content.sizeDelta = new Vector2(0f, shownCards > 0 ? y + CodexPanel.GridInset : 0f);
    }

    // ---------------------------------------------------------------------
    // State
    // ---------------------------------------------------------------------

    public void Show(int onlyWorld = -1)
    {
        worldFilter = onlyWorld;
        root.gameObject.SetActive(true);
        shownCards = shownHeaders = 0;
        headerWorlds.Clear();
        headerStart.Clear();
        int world = -1;
        foreach (var b in CodexBackdrops.All)
        {
            if (!CodexBackdrops.IsListed(b) || shownCards >= cards.Length) continue;
            if (worldFilter >= 0 && b.worldIndex != worldFilter) continue;
            if (b.worldIndex != world)
            {
                world = b.worldIndex;
                headerWorlds.Add(world);
                headerStart.Add(shownCards);
                shownHeaders++;
            }
            cards[shownCards++].backdrop = b;
        }
        for (int i = 0; i < cards.Length; i++) cards[i].rt.gameObject.SetActive(i < shownCards);
        for (int i = 0; i < headers.Length; i++) headers[i].rt.gameObject.SetActive(worldFilter < 0 && headerWorlds.Contains(i));
        scroll.velocity = Vector2.zero;
        content.anchoredPosition = Vector2.zero;
        Paint();
        LayoutCards();
    }

    // Leaving the tab: the thumbnails are released.
    public void Hide()
    {
        root.gameObject.SetActive(false);
        ReleaseThumbs();
    }

    void ReleaseThumbs()
    {
        bool any = false;
        foreach (var c in cards)
            if (c.thumb != null && c.thumb.texture != null) { c.thumb.texture = null; any = true; }
        if (any && Application.isPlaying) Resources.UnloadUnusedAssets();
    }

    public void SetFade(float alpha) { rootGroup.alpha = alpha; }

    // Repaints every card, header and the strip from the unlock state.
    public void Paint()
    {
        if (cards == null || !root.gameObject.activeSelf) return;
        for (int i = 0; i < shownCards; i++)
        {
            var c = cards[i];
            bool unlocked = CodexBackdrops.IsUnlocked(c.backdrop);
            c.name.text = CodexBackdrops.DisplayTitle(c.backdrop);
            c.name.color = unlocked ? CodexUi.Body : CodexUi.Muted;
            c.edge.color = unlocked ? CodexUi.Accent : CodexUi.Locked;
            c.lockIcon.gameObject.SetActive(!unlocked);
            c.button.interactable = unlocked;
            bool pending = Codex.IsRewardPending(c.backdrop.id);
            c.dot.gameObject.SetActive(pending);
            c.collectFrame.gameObject.SetActive(pending);
            if (pending) c.collectLabel.text = "COLLECT +" + Codex.PendingDust(c.backdrop.id);
            if (unlocked)
            {
                var tex = Resources.Load<Texture2D>(c.backdrop.ThumbResource);
                c.thumb.texture = tex;
                c.thumb.color = Color.white;
                // a square from the middle of the tall tile
                float k = tex != null ? (float)tex.width / Mathf.Max(1, tex.height) : .5f;
                c.thumb.uvRect = new Rect(0f, .5f - k * .5f, 1f, k);
            }
            else
            {
                c.thumb.texture = null;                 // a flat ink silhouette
                c.thumb.color = CodexUi.Silhouette;
            }
        }
        for (int s = 0; s < shownHeaders; s++)
        {
            int world = headerWorlds[s];
            int end = s + 1 < shownHeaders ? headerStart[s + 1] : shownCards;
            int found = 0;
            for (int k = headerStart[s]; k < end; k++) if (CodexBackdrops.IsUnlocked(cards[k].backdrop)) found++;
            var h = headers[world];
            h.frame.color = CodexPanel.SectionColor(world);
            h.label.text = CodexPanel.WorldLabel(world);
            h.counter.text = found + "/" + (end - headerStart[s]);
        }
        int seen = 0;
        for (int i = 0; i < shownCards; i++) if (CodexBackdrops.IsUnlocked(cards[i].backdrop)) seen++;
        stripCount.text = (worldFilter < 0 ? CodexBackdrops.UnlockedCount + " / " + CodexBackdrops.ListedCount : seen + " / " + shownCards) + "  SEEN";
        stripLine.text = IdleLine;
    }

    void OnCollectClicked(int index)
    {
        if (index < 0 || index >= shownCards || cards[index].backdrop == null) return;
        panel.AfterCollect(Codex.CollectReward(cards[index].backdrop.id));
        Paint();
    }

    void OnCardClicked(int index)
    {
        if (index < 0 || index >= shownCards) return;
        var b = cards[index].backdrop;
        if (b == null || !CodexBackdrops.IsUnlocked(b)) return;
        panel.OpenBackdropViewer(b);
    }
}
