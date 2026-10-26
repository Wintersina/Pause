using UnityEngine;
using UnityEngine.UI;

// The COMBOS tab of the codex: every ATOMIC FUSION (AtomFusionTable) as one
// row of a scrolling list under a header strip (how many are found). A row
// shows the recipe's atoms as small glyphs on the left, then:
//
//   undiscovered   the glyphs blacked out in the codex's ink silhouette (the
//                  locked look of every codex card), "???" and the row's hint
//   discovered     the glyphs in colour, the fusion's name, its one-line
//                  effect, and "FIRED n  BEST CHAIN xN  FOUND 26 OCT 2026"
//
// Same cel cards, ink outlines and palette as the MUSIC tab. Opening the tab
// clears its NEW dot (FusionDiscoveryStore.MarkSeen).
public sealed class CodexCombosView
{
    public const float CardBorder = CodexMusicView.CardBorder;
    public const float PadX = 12f, PadY = 6f;
    public const float InsetX = CardBorder + PadX, InsetY = CardBorder + PadY;
    public const float StripHeight = 128f, StripGap = 12f;
    public const float RowHeight = 176f, RowGap = 12f;
    public const float GlyphSize = 46f, GlyphGap = 4f;
    public const int MaxGlyphs = 3;
    public const string LockedName = "???";
    public const string IdleLine = "Catch two atoms close together.";

    sealed class Row
    {
        public RectTransform rt;
        public Image frame, edge;
        public Image[] glyphs;
        public Text title, line, stats;
        public FusionDef def;
    }

    readonly Font font;
    RectTransform root;
    CanvasGroup rootGroup;
    RectTransform strip;
    Text stripCount, stripLine;
    RectTransform grid, viewport, content;
    ScrollRect scroll;
    Row[] rows;
    Rect listRect;

    public CodexCombosView(RectTransform parent, Font font)
    {
        this.font = font;
        Build(parent);
    }

    // ---- accessors for the tests ----
    public bool Active { get { return root.gameObject.activeSelf; } }
    public int RowCount { get { return rows.Length; } }
    public FusionDef RowDef(int i) { return rows[i].def; }
    public Text RowTitle(int i) { return rows[i].title; }
    public Text RowLine(int i) { return rows[i].line; }
    public Text RowStats(int i) { return rows[i].stats; }
    public Image RowGlyph(int i, int g) { return rows[i].glyphs[g]; }
    public RectTransform RowRect(int i) { return rows[i].rt; }
    public Text StripCount { get { return stripCount; } }
    public Rect ListRect { get { return listRect; } }

    void Build(RectTransform parent)
    {
        root = CodexUi.NewRect("Combos", parent);
        rootGroup = root.gameObject.AddComponent<CanvasGroup>();

        strip = CodexUi.NewRect("Strip", root);
        var frame = CodexUi.NewImage("Frame", strip, CodexUi.CodexSprite("cx_card"), Color.white, true);
        CodexUi.Stretch(frame.rectTransform);
        var edge = CodexUi.NewImage("Edge", strip, CodexUi.CodexSprite("cx_card_edge"), CodexUi.Accent, true);
        CodexUi.Stretch(edge.rectTransform);
        stripCount = Label("Count", strip, 30, 16, CodexUi.Title, TextAnchor.MiddleLeft, 2f);
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

        rows = new Row[AtomFusionTable.Count];
        for (int i = 0; i < rows.Length; i++) rows[i] = BuildRow(i, AtomFusionTable.All[i]);
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

    Row BuildRow(int index, FusionDef def)
    {
        var r = new Row { def = def };
        r.frame = CodexUi.NewImage("Combo" + index, content, CodexUi.CodexSprite("cx_card"), Color.white, true);
        r.rt = r.frame.rectTransform;
        r.edge = CodexUi.NewImage("Edge", r.rt, CodexUi.CodexSprite("cx_card_edge"), CodexUi.Locked, true);
        CodexUi.Stretch(r.edge.rectTransform);
        r.glyphs = new Image[MaxGlyphs];
        for (int g = 0; g < MaxGlyphs; g++)
        {
            var img = CodexUi.NewImage("Atom" + g, r.rt, g < def.atoms.Length ? GlyphOf(def.atoms[g]) : null, Color.white);
            img.preserveAspect = true;
            img.raycastTarget = false;
            img.gameObject.SetActive(g < def.atoms.Length);
            r.glyphs[g] = img;
        }
        r.title = Label("Title", r.rt, 28, 14, CodexUi.Body, TextAnchor.MiddleLeft, 2f);
        r.line = Label("Line", r.rt, 18, 10, CodexUi.Body, TextAnchor.MiddleLeft, 1.5f);
        r.stats = Label("Stats", r.rt, 16, 9, CodexUi.Muted, TextAnchor.MiddleLeft, 1.5f);
        return r;
    }

    // An atom's small glyph: the pickup's own idle frame; the ship-power
    // "atoms" use their ship's power icon.
    public static Sprite GlyphOf(AtomKind k)
    {
        switch (k)
        {
            case AtomKind.Shield: return PickupFrame(PickupKind.Shield);
            case AtomKind.Pause: return PickupFrame(PickupKind.Pause);
            case AtomKind.Capacitor: return PickupFrame(PickupKind.Cooldown);
            case AtomKind.Magnet: return PickupFrame(PickupKind.Magnet);
            case AtomKind.Heal:
                {
                    if (healGlyph != null) return healGlyph;
                    var tex = Resources.Load<Texture2D>("Pickups/heal_atom_green");
                    if (tex != null) return healGlyph = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(.5f, .5f), 100f);
                    return PickupFrame(PickupKind.Heal);
                }
            case AtomKind.Mending: return ShipIcon("Dove");
            default: return ShipIcon("Turtle");
        }
    }

    static Sprite healGlyph;

    static Sprite PickupFrame(PickupKind kind)
    {
        var frames = PickupArt.Frames(PickupArt.IdleName(kind), PickupArt.IdleTicks(kind).Length);
        return frames != null && frames.Length > 0 ? frames[0] : null;
    }

    static Sprite ShipIcon(string key)
    {
        int id = ShipId.FromKey(key);
        return ShipId.IsValid(id) ? ShipFxArt.MeterIcon(id) : null;
    }

    // ---------------------------------------------------------------------
    // Layout
    // ---------------------------------------------------------------------

    public void ApplyLayout(Rect bodyRect)
    {
        CodexUi.Place(root, bodyRect);
        float w = bodyRect.width, h = bodyRect.height;
        var stripRect = new Rect(-w * .5f, h * .5f - StripHeight, w, StripHeight);
        listRect = Rect.MinMaxRect(-w * .5f, -h * .5f, w * .5f, stripRect.yMin - StripGap);
        CodexUi.Place(strip, stripRect);
        float textW = w - InsetX * 2f;
        float lineH = StripHeight * .5f - InsetY - 2f;
        CodexUi.Place(stripCount.rectTransform, new Rect(-w * .5f + InsetX, 2f, textW, lineH));
        CodexUi.Place(stripLine.rectTransform, new Rect(-w * .5f + InsetX, -2f - lineH, textW, lineH));
        CodexUi.Place(grid, listRect);
        LayoutRows();
    }

    void LayoutRows()
    {
        if (rows == null) return;
        float w = listRect.width - 2f * CodexPanel.GridInset;
        float y = 0f;
        float glyphsW = MaxGlyphs * GlyphSize + (MaxGlyphs - 1) * GlyphGap;
        for (int i = 0; i < rows.Length; i++)
        {
            var r = rows[i];
            r.rt.anchorMin = r.rt.anchorMax = new Vector2(.5f, 1f);
            r.rt.pivot = new Vector2(.5f, 1f);
            r.rt.anchoredPosition = new Vector2(0f, -y);
            r.rt.sizeDelta = new Vector2(w, RowHeight);
            float left = -w * .5f + InsetX;
            int n = r.def.atoms.Length;
            for (int g = 0; g < n && g < MaxGlyphs; g++)
                CodexUi.Place(r.glyphs[g].rectTransform, CodexUi.Centered(left + GlyphSize * .5f + g * (GlyphSize + GlyphGap), 0f, GlyphSize, GlyphSize));
            float textLeft = left + glyphsW + 14f;
            float textW = w * .5f - InsetX - textLeft;
            float inner = RowHeight - 2f * InsetY;
            float titleH = inner * .4f, lineH = inner * .32f, statsH = inner - titleH - lineH;
            float top = inner * .5f;
            CodexUi.Place(r.title.rectTransform, new Rect(textLeft, top - titleH, textW, titleH));
            CodexUi.Place(r.line.rectTransform, new Rect(textLeft, top - titleH - lineH, textW, lineH));
            CodexUi.Place(r.stats.rectTransform, new Rect(textLeft, top - titleH - lineH - statsH, textW, statsH));
            y += RowHeight + RowGap;
        }
        content.sizeDelta = new Vector2(0f, Mathf.Max(0f, y - RowGap) + 8f);
    }

    // ---------------------------------------------------------------------
    // State
    // ---------------------------------------------------------------------

    public void Show()
    {
        root.gameObject.SetActive(true);
        FusionDiscoveryStore.Reload();
        LayoutRows();
        scroll.velocity = Vector2.zero;
        content.anchoredPosition = Vector2.zero;
        Paint();
        FusionDiscoveryStore.MarkSeen();
    }

    public void Hide() { root.gameObject.SetActive(false); }

    public void SetFade(float alpha) { rootGroup.alpha = alpha; }

    public void Paint()
    {
        if (rows == null || !root.gameObject.activeSelf) return;
        for (int i = 0; i < rows.Length; i++)
        {
            var r = rows[i];
            var rec = FusionDiscoveryStore.Get(r.def.id);
            bool found = rec.Discovered;
            for (int g = 0; g < r.def.atoms.Length && g < MaxGlyphs; g++) CodexUi.PaintArt(r.glyphs[g], !found);
            r.title.text = found ? r.def.name : LockedName;
            r.title.color = found ? CodexUi.Title : CodexUi.Muted;
            r.line.text = found ? r.def.recipe + ":  " + r.def.description : r.def.hint;
            r.line.color = found ? CodexUi.Body : CodexUi.Muted;
            r.stats.text = found ? StatsLine(rec) : "";
            r.edge.color = found ? r.def.glow : CodexUi.Locked;
            r.frame.color = found ? Color.white : new Color(.7f, .7f, .8f, 1f);
        }
        stripCount.text = FusionDiscoveryStore.DiscoveredCount + " / " + FusionDiscoveryStore.Total + "  FOUND";
        stripLine.text = IdleLine;
    }

    public static string StatsLine(FusionDiscoveryStore.Record rec)
    {
        return "FIRED " + rec.count + "   BEST CHAIN x" + Mathf.Max(1, rec.bestChain) +
               (rec.firstDay > 0 ? "   FOUND " + FusionDiscoveryStore.FormatDay(rec.firstDay) : "");
    }
}
