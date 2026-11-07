using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// The Shop screen: a full-screen panel over the home menu (like the Codex), reached from the SHOP row
// (ShopHomeEntry) or from the dock's Paragon berth. It scrolls vertically and lays out from the canvas' size, so
// it reads the same on a tall phone and a tablet. Top to bottom (ShopCatalog.Rows):
//
//     header: SHOP + the star dust balance
//     ADS          Remove Ads (one-time, owned/not)
//     SHIP         Paragon (one-time; bought here, then it sits in the Space Dock)
//     STAR DUST    the 850 / 2,500 / 6,000 packs (repeatable)
//     BOOSTERS     Dust x2 and Hazard Clear (bought with star dust, no store)
//     a restore note, BACK
//
// Rows are flat bands (no outlines), type and buttons in the codex's cel style. Every state comes from
// ShopCatalog; a row without a price from Play reads UNAVAILABLE and does nothing. Purchase results, pending and
// failures keep using the existing toasts (PlayBilling); this panel only redraws: the star dust balance follows
// SupportPurchase.Rewarded, the rows follow the *Purchase.Changed events and the Buying flags.
//
// Art: placeholders from existing assets only (the dock's star dust glyph for the packs, Paragon's hull, a plain
// NO ADS tile): proper product art is a separate decision.
public class ShopPanel : MonoBehaviour
{
    public const int SortingOrder = 500;       // the codex's: one of them is up at a time
    public const float MaxContentWidth = 680f;
    public const float Margin = 24f;
    public const float ButtonHeight = 96f;     // CodexUi.TapUnits: never under a finger target
    public const float ButtonMaxWidth = 220f;
    public const float RowPad = 20f;
    public const float SectionGap = 36f;
    public const float HeaderHeight = 84f;
    public const float FooterHeight = ButtonHeight + 48f;
    public const float PollSeconds = .25f;

    static ShopPanel instance;
    public static ShopPanel Current { get { return instance; } }
    public static bool IsOpen { get { return instance != null && instance.open; } }
    public static event Action Closed;

    public static ShopPanel Open()
    {
        if (instance == null) instance = Build();
        instance.Show();
        return instance;
    }

    sealed class RowView
    {
        public ShopRow row;
        public RectTransform rt;
        public Image band, icon, button, edge;
        public Text title, sub, notice, buttonText, iconText;
        public Button click;
        public float height;
    }

    Canvas canvas;
    Font font;
    RectTransform panel, viewport, content, headerRoot, footerRoot;
    Image scrim;
    Text title, dust, storeNote, restoreNote;
    Image dustIcon;
    ScrollRect scroll;
    readonly List<RowView> rows = new List<RowView>();
    readonly List<int> sectionSizes = new List<int>();
    readonly List<RectTransform> sectionHeads = new List<RectTransform>();
    readonly List<Text> sectionLabels = new List<Text>();
    bool open, dirty;
    float nextPoll;
    string signature = "";
    Vector2 lastSize;
    Rect lastSafe;

    // ---- test / rig access ----
    public RectTransform Panel { get { return panel; } }
    public RectTransform Content { get { return content; } }
    public ScrollRect Scroll { get { return scroll; } }
    public Button BackButton { get; private set; }
    public Text DustText { get { return dust; } }
    public Text StoreNote { get { return storeNote; } }
    public int RowCount { get { return rows.Count; } }
    public ShopRow RowAt(int i) { return rows[i].row; }
    public Button RowButton(int i) { return rows[i].click; }
    public Text RowButtonText(int i) { return rows[i].buttonText; }
    public Text RowSubtitle(int i) { return rows[i].sub; }
    public Text RowNotice(int i) { return rows[i].notice; }
    public Canvas CanvasOf { get { return canvas; } }

    static ShopPanel Build()
    {
        var c = CodexUi.NewOverlayCanvas("ShopPanel", SortingOrder, true);
        var view = c.gameObject.AddComponent<ShopPanel>();
        view.canvas = c;
        view.font = CodexUi.FindFont();
        view.BuildAll();
        return view;
    }

    // ------------------------------------------------------------------ building

    void BuildAll()
    {
        var root = (RectTransform)transform;
        var scrimColor = AkiraPalette.Night0; scrimColor.a = .86f;   // the menu's star backdrop shows through; the menu rows themselves are hidden while open
        scrim = CodexUi.NewImage("Scrim", root, null, scrimColor);
        CodexUi.Stretch(scrim.rectTransform);
        scrim.raycastTarget = true;   // the home screen underneath stays untouchable

        panel = CodexUi.NewRect("Panel", root);
        CodexUi.Stretch(panel);

        // header
        headerRoot = CodexUi.NewRect("Header", panel);
        title = CodexUi.NewText("Title", headerRoot, font, ShopCatalog.Title, 52, CodexUi.Title, TextAnchor.MiddleLeft);
        title.fontStyle = FontStyle.BoldAndItalic;
        CodexUi.AddOutline(title.gameObject, CodexUi.Ink, 3f);
        title.resizeTextForBestFit = true; title.resizeTextMinSize = 24; title.resizeTextMaxSize = 52;
        title.horizontalOverflow = HorizontalWrapMode.Wrap; title.verticalOverflow = VerticalWrapMode.Truncate;
        dustIcon = CodexUi.NewImage("DustIcon", headerRoot, DockArt.Get("icon_dust"), Color.white);
        dustIcon.preserveAspect = true;
        dust = CodexUi.NewText("Dust", headerRoot, font, "0", 30, AkiraPalette.Amber, TextAnchor.MiddleRight);
        CodexUi.AddOutline(dust.gameObject, CodexUi.Ink, 2f);
        dust.resizeTextForBestFit = true; dust.resizeTextMinSize = 16; dust.resizeTextMaxSize = 30;
        dust.horizontalOverflow = HorizontalWrapMode.Wrap; dust.verticalOverflow = VerticalWrapMode.Truncate;

        // scrolling body
        var go = new GameObject("Scroll", typeof(RectTransform), typeof(ScrollRect));
        go.transform.SetParent(panel, false);
        var scrollRt = (RectTransform)go.transform;
        viewport = CodexUi.NewRect("Viewport", scrollRt);
        CodexUi.Stretch(viewport);
        viewport.gameObject.AddComponent<RectMask2D>();
        var hit = viewport.gameObject.AddComponent<Image>();
        hit.color = new Color(0f, 0f, 0f, 0f);
        hit.raycastTarget = true;   // drags that start between rows still scroll
        content = CodexUi.NewRect("Content", viewport);
        content.anchorMin = content.anchorMax = new Vector2(.5f, 1f);
        content.pivot = new Vector2(.5f, 1f);
        scroll = go.GetComponent<ScrollRect>();
        scroll.viewport = viewport;
        scroll.content = content;
        scroll.horizontal = false;
        scroll.vertical = true;
        scroll.movementType = ScrollRect.MovementType.Elastic;
        scroll.inertia = true;
        scroll.scrollSensitivity = 30f;

        storeNote = CodexUi.NewText("StoreNote", content, font, ShopCatalog.StoreDownNote, 20, AkiraPalette.RedHi, TextAnchor.MiddleLeft, true);
        FitText(storeNote, 15, 20);
        restoreNote = CodexUi.NewText("RestoreNote", content, font, ShopCatalog.RestoreNote, 18, CodexUi.Muted, TextAnchor.UpperLeft, true);
        FitText(restoreNote, 15, 18);

        // Sections come from the catalog, so a new one (Boosters) needs no change here beyond its row kind's art.
        foreach (var section in ShopCatalog.Sections())
        {
            AddSection(section.label);
            sectionSizes.Add(section.rows.Count);
            foreach (var row in section.rows) rows.Add(BuildRow(row));
        }

        // footer: BACK
        footerRoot = CodexUi.NewRect("Footer", panel);
        var frame = CodexUi.NewImage("BackButton", footerRoot, CodexUi.CodexSprite("cx_button"), Color.white, true);
        frame.raycastTarget = true;
        CodexUi.Stretch(frame.rectTransform);
        var backEdge = CodexUi.NewImage("Edge", frame.rectTransform, CodexUi.CodexSprite("cx_button_edge"), CodexUi.Accent, true);
        CodexUi.Stretch(backEdge.rectTransform);
        var back = frame.gameObject.AddComponent<Button>();
        back.targetGraphic = frame;
        var colors = back.colors; colors.pressedColor = CodexUi.Select; colors.fadeDuration = .08f; back.colors = colors;
        back.onClick.AddListener(BackNavigator.Back);
        BackButton = back;
        var label = CodexUi.NewText("Label", frame.rectTransform, font, "BACK", 30, CodexUi.Body, TextAnchor.MiddleCenter);
        CodexUi.AddOutline(label.gameObject, CodexUi.Ink, 2f);
        CodexUi.Stretch(label.rectTransform);
    }

    static void FitText(Text t, int min, int max)
    {
        t.resizeTextForBestFit = true; t.resizeTextMinSize = min; t.resizeTextMaxSize = max;
        t.horizontalOverflow = HorizontalWrapMode.Wrap; t.verticalOverflow = VerticalWrapMode.Truncate;
    }

    void AddSection(string label)
    {
        var rt = CodexUi.NewRect("Section_" + label, content);
        var text = CodexUi.NewText("Label", rt, font, label, 24, CodexUi.Accent, TextAnchor.LowerLeft);
        CodexUi.AddOutline(text.gameObject, CodexUi.Ink, 2f);
        FitText(text, 15, 24);
        CodexUi.Stretch(text.rectTransform);
        var line = CodexUi.NewImage("Divider", rt, CodexUi.CodexSprite("cx_divider"), Color.white);
        line.preserveAspect = false;
        sectionHeads.Add(rt);
        sectionLabels.Add(text);
    }

    RowView BuildRow(ShopRow row)
    {
        var v = new RowView { row = row };
        v.rt = CodexUi.NewRect("Row_" + row.kind + (row.pack != null ? "_" + row.pack.id : ""), content);
        v.band = CodexUi.NewImage("Band", v.rt, null, WithAlpha(AkiraPalette.Indigo0, .85f));
        CodexUi.Stretch(v.band.rectTransform);
        v.icon = CodexUi.NewImage("Icon", v.rt, null, Color.white);
        v.icon.preserveAspect = true;
        v.iconText = CodexUi.NewText("IconText", v.icon.rectTransform, font, "", 26, AkiraPalette.Bone, TextAnchor.MiddleCenter);
        CodexUi.AddOutline(v.iconText.gameObject, CodexUi.Ink, 2f);
        FitText(v.iconText, 15, 26);
        CodexUi.Stretch(v.iconText.rectTransform);
        v.title = CodexUi.NewText("Title", v.rt, font, row.title, 32, CodexUi.Body, TextAnchor.LowerLeft, true);
        CodexUi.AddOutline(v.title.gameObject, CodexUi.Ink, 2f);
        FitText(v.title, 18, 32);
        v.sub = CodexUi.NewText("Sub", v.rt, font, row.subtitle ?? "", 20, CodexUi.Muted, TextAnchor.UpperLeft, true);
        FitText(v.sub, 15, 20);
        v.notice = CodexUi.NewText("Notice", v.rt, font, "", 18, AkiraPalette.Amber, TextAnchor.MiddleLeft, true);
        FitText(v.notice, 11, 18);

        v.button = CodexUi.NewImage("Buy", v.rt, CodexUi.CodexSprite("cx_button"), Color.white, true);
        v.button.raycastTarget = true;
        v.edge = CodexUi.NewImage("Edge", v.button.rectTransform, CodexUi.CodexSprite("cx_button_edge"), AkiraPalette.Amber, true);
        CodexUi.Stretch(v.edge.rectTransform);
        v.click = v.button.gameObject.AddComponent<Button>();
        v.click.targetGraphic = v.button;
        var colors = v.click.colors; colors.pressedColor = CodexUi.Select; colors.fadeDuration = .08f; v.click.colors = colors;
        var captured = v;
        v.click.onClick.AddListener(() => Tapped(captured));
        v.buttonText = CodexUi.NewText("Label", v.button.rectTransform, font, "", 28, CodexUi.Body, TextAnchor.MiddleCenter);
        CodexUi.AddOutline(v.buttonText.gameObject, CodexUi.Ink, 2f);
        FitText(v.buttonText, 15, 22);   // UNAVAILABLE and a long price must stay inside the plate
        CodexUi.Stretch(v.buttonText.rectTransform);
        v.buttonText.rectTransform.offsetMin = new Vector2(8f, 0f);
        v.buttonText.rectTransform.offsetMax = new Vector2(-8f, 0f);
        return v;
    }

    static Color WithAlpha(Color c, float a) { c.a = a; return c; }

    // ------------------------------------------------------------------ painting

    // Redraws every row from the billing state, then lays out again (a row's text or the store note may change height).
    public void Refresh()
    {
        var fresh = ShopCatalog.Rows();
        for (int i = 0; i < rows.Count && i < fresh.Count; i++)
        {
            rows[i].row = fresh[i];
            Paint(rows[i]);
        }
        dust.text = Mathf.FloorToInt(StarDustLedger.Saved).ToString("N0");
        storeNote.text = ShopCatalog.StoreDownText;
        storeNote.gameObject.SetActive(ShopCatalog.StoreDown);
        signature = Signature();
        dirty = true;
        Layout();
    }

    void Paint(RowView v)
    {
        var r = v.row;
        v.title.text = r.title;
        v.sub.text = r.subtitle ?? "";
        v.buttonText.text = r.button ?? "";
        bool live = r.state == ShopRowState.Buyable;
        // Only a buyable row (or one waiting on the store) is bright; owned, locked and unavailable ones are muted.
        Color edgeColor = live ? AkiraPalette.Amber : r.state == ShopRowState.Buying ? CodexUi.Accent : CodexUi.Locked;
        v.edge.color = edgeColor;
        v.buttonText.color = live || r.state == ShopRowState.Buying ? CodexUi.Body : CodexUi.Muted;
        v.button.color = live ? Color.white : new Color(.8f, .8f, .9f, 1f);
        // a locked Paragon still takes the tap (it explains the gate); other non-buyable rows do not
        // (and nothing is while another product's sheet is open: one purchase flow at a time)
        // (booster rows spend dust, not a store sheet, so an open sheet never greys them)
        bool other = ShopCatalog.AnyBuying && r.state != ShopRowState.Buying && !r.IsBooster;
        if (other) { v.edge.color = CodexUi.Locked; v.button.color = new Color(.8f, .8f, .9f, 1f); v.buttonText.color = CodexUi.Muted; }
        v.click.interactable = !other && (live || r.state == ShopRowState.Locked);
        PaintIcon(v);
        bool paragon = r.kind == ShopRowKind.Paragon;
        v.notice.gameObject.SetActive(paragon && r.state != ShopRowState.Owned);
        if (paragon) v.notice.text = PremiumShips.LeaderboardNotice;
        v.sub.color = r.state == ShopRowState.Locked ? AkiraPalette.RedHi : CodexUi.Muted;
    }

    void PaintIcon(RowView v)
    {
        var r = v.row;
        v.iconText.text = "";
        switch (r.kind)
        {
            case ShopRowKind.RemoveAds:
                // placeholder: a plain tile (art is a separate decision)
                v.icon.sprite = null;
                v.icon.color = AkiraPalette.RedShadow;
                CutCornerShape.Apply(v.icon);
                v.iconText.text = "NO\nADS";
                break;
            case ShopRowKind.Paragon:
                v.icon.sprite = ShipHullArt.Get(PremiumShips.Paragon, 0, 0, 0);
                v.icon.color = Color.white;
                v.icon.type = Image.Type.Simple;
                break;
            case ShopRowKind.HazardClear:
                v.icon.sprite = null;
                v.icon.color = AkiraPalette.TealShadow;
                CutCornerShape.Apply(v.icon);
                v.iconText.text = "CLEAR";
                break;
            case ShopRowKind.HomingAtoms:
                v.icon.sprite = null;
                v.icon.color = AkiraPalette.VioletShadow;
                CutCornerShape.Apply(v.icon);
                v.iconText.text = "ATOMS";
                break;
            default:
                v.icon.sprite = DockArt.Get("icon_dust");
                v.icon.color = Color.white;
                v.icon.type = Image.Type.Simple;
                break;
        }
    }

    // How big a pack's star dust glyph is drawn (share of the icon box): bigger packs read bigger.
    public static float PackIconScale(SupportPurchase.Pack p)
    {
        return p.dust >= 5000 ? 1f : p.dust >= 2000 ? .8f : .6f;
    }

    // ------------------------------------------------------------------ layout

    // Lays everything out for the canvas' current size and safe area.
    public void Layout()
    {
        dirty = false;
        Canvas.ForceUpdateCanvases();
        var size = ((RectTransform)canvas.transform).rect.size;
        var safe = CodexUi.SafeAreaUnits(canvas);
        lastSize = size;
        lastSafe = safe;

        float width = Mathf.Min(MaxContentWidth, safe.width - 2f * Margin);
        float centerX = safe.center.x;
        // header
        float headerTop = safe.yMax - 12f;
        Place(headerRoot, new Rect(centerX - width * .5f, headerTop - HeaderHeight, width, HeaderHeight));
        float dustW = Mathf.Min(300f, width * .5f);
        SetRect(title.rectTransform, 0f, 0f, width - dustW - 12f, HeaderHeight, headerRoot);
        SetRect(dust.rectTransform, width - dustW, 0f, dustW, HeaderHeight, headerRoot);
        // the star sits just left of the number, which is right-aligned to the header's edge
        float numW = Mathf.Min(dustW - 52f, dust.preferredWidth);
        SetRect(dustIcon.rectTransform, width - numW - 52f, 20f, 44f, 44f, headerRoot);
        // footer
        float footerBottom = safe.yMin + 12f;
        float backW = Mathf.Min(300f, width);
        Place(footerRoot, new Rect(centerX - backW * .5f, footerBottom + 24f, backW, ButtonHeight));
        // viewport between them
        float vTop = headerTop - HeaderHeight - 8f;
        float vBottom = footerBottom + 24f + ButtonHeight + 24f;
        Place(scroll.GetComponent<RectTransform>(), new Rect(centerX - width * .5f - Margin, vBottom, width + 2f * Margin, Mathf.Max(100f, vTop - vBottom)));

        // content: top to bottom
        content.sizeDelta = new Vector2(width, 0f);
        float y = 8f;
        if (storeNote.gameObject.activeSelf)
        {
            float h = NoteHeight(storeNote, width, 20f);
            SetTop(storeNote.rectTransform, 0f, y, width, h);
            y += h + 16f;
        }
        int sec = 0, rowIndex = 0;
        for (; sec < sectionHeads.Count; sec++)
        {
            var head = sectionHeads[sec];
            SetTop(head, 0f, y, width, 40f);
            var line = head.Find("Divider").GetComponent<RectTransform>();
            line.anchorMin = new Vector2(0f, 0f); line.anchorMax = new Vector2(1f, 0f);
            line.pivot = new Vector2(.5f, 0f); line.anchoredPosition = Vector2.zero; line.sizeDelta = new Vector2(0f, 8f);
            y += 40f + 10f;
            for (int k = 0; k < sectionSizes[sec]; k++, rowIndex++)
            {
                var v = rows[rowIndex];
                float h = LayoutRow(v, width);
                SetTop(v.rt, 0f, y, width, h);
                y += h + 14f;
            }
            y += SectionGap - 14f;
        }
        float rn = NoteHeight(restoreNote, width, 18f);
        SetTop(restoreNote.rectTransform, 0f, y, width, rn);
        y += rn + 24f;
        content.sizeDelta = new Vector2(width, y);
        scroll.verticalNormalizedPosition = Mathf.Clamp01(scroll.verticalNormalizedPosition);
    }

    float NoteHeight(Text t, float width, float size)
    {
        // wrapped height at the nominal size, floored at one line, capped so a huge note can't eat the screen
        var gen = t.cachedTextGeneratorForLayout;
        var settings = t.GetGenerationSettings(new Vector2(width, 0f));
        settings.resizeTextForBestFit = false;
        settings.fontSize = Mathf.RoundToInt(size);
        float h = gen.GetPreferredHeight(t.text, settings) / Mathf.Max(.01f, t.pixelsPerUnit);
        return Mathf.Clamp(h + 6f, size * 1.6f, size * 6f);
    }

    // Positions a row's parts inside its band; returns the band's height.
    float LayoutRow(RowView v, float width)
    {
        bool paragon = v.row.kind == ShopRowKind.Paragon;
        bool pack = v.row.kind == ShopRowKind.Pack;
        float iconBox = paragon ? 150f : 96f;
        float h = paragon ? 210f : pack ? 128f : v.row.IsBooster ? 176f : 140f;   // boosters carry a 2-3 line description
        float btnW = Mathf.Min(ButtonMaxWidth, width * .36f);
        SetRect(v.icon.rectTransform, RowPad, (h - iconBox) * .5f, iconBox, iconBox, v.rt, true);
        if (pack)
        {
            float k = PackIconScale(v.row.pack);
            v.icon.rectTransform.localScale = new Vector3(k, k, 1f);
        }
        else v.icon.rectTransform.localScale = Vector3.one;
        float textX = RowPad + iconBox + 16f;
        float textW = Mathf.Max(60f, width - textX - btnW - 2f * RowPad);
        bool showNotice = v.notice.gameObject.activeSelf;
        float noticeH = showNotice ? 56f : 0f;
        float titleH = 44f;
        float subTop = RowPad + titleH + 2f;
        float subH = Mathf.Max(24f, h - subTop - RowPad - noticeH);
        if (!pack || string.IsNullOrEmpty(v.sub.text))
        {
            SetRect(v.title.rectTransform, textX, RowPad, textW, titleH, v.rt, true);
            SetRect(v.sub.rectTransform, textX, subTop, textW, subH, v.rt, true);
            v.title.alignment = TextAnchor.LowerLeft;
        }
        if (pack)
        {
            // pack rows have only the headline, centred on the row
            SetRect(v.title.rectTransform, textX, RowPad, textW, h - 2f * RowPad, v.rt, true);
            v.title.alignment = TextAnchor.MiddleLeft;
            SetRect(v.sub.rectTransform, textX, h - RowPad, textW, 1f, v.rt, true);
        }
        SetRect(v.notice.rectTransform, textX, h - RowPad - noticeH, textW + btnW * .0f, noticeH, v.rt, true);
        SetRect(v.button.rectTransform, width - RowPad - btnW, (h - ButtonHeight) * .5f, btnW, ButtonHeight, v.rt, true);
        return h;
    }

    // ---- rect helpers: centre-origin placement and top-left placement ----

    static void Place(RectTransform rt, Rect r) { CodexUi.Place(rt, r); }

    // x/y from the parent's top-left, size w x h (parent anchored top-left style).
    static void SetRect(RectTransform rt, float x, float y, float w, float h, Transform parent, bool topLeft = false)
    {
        rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
        rt.pivot = new Vector2(0f, 1f);
        rt.anchoredPosition = new Vector2(x, topLeft ? -y : y - 0f);
        rt.sizeDelta = new Vector2(w, h);
        if (!topLeft)
        {
            // header children: y measured from the bottom of the header strip
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 0f);
            rt.pivot = new Vector2(0f, 0f);
            rt.anchoredPosition = new Vector2(x, y);
        }
    }

    // A child of `content`, hung from its top edge: x from the left, y down from the top.
    static void SetTop(RectTransform rt, float x, float y, float w, float h)
    {
        rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
        rt.pivot = new Vector2(0f, 1f);
        rt.anchoredPosition = new Vector2(x, -y);
        rt.sizeDelta = new Vector2(w, h);
    }

    // ------------------------------------------------------------------ input and lifecycle

    void Tapped(RowView v)
    {
        if (v.row.state == ShopRowState.Locked)
        {
            AccountHintToast.Show(ShipUnlocks.LockedMessage(PremiumShips.Paragon));   // the existing gate wording
            return;
        }
        ShopCatalog.Tap(v.row);
        Refresh();   // Buying flips on at once
    }

    void Show()
    {
        if (open) return;
        open = true;
        gameObject.SetActive(true);
        HideMenu(true);
        BackNavigator.Register(this, OnBackPressed);
        RemoveAdsPurchase.Changed += OnChanged;
        ParagonPurchase.Changed += OnChanged;
        SupportPurchase.Changed += OnChanged;
        SupportPurchase.Rewarded += OnRewarded;
        DeveloperUnlocks.Changed += OnChanged;
        Refresh();
        scroll.verticalNormalizedPosition = 1f;
    }

    public void Close()
    {
        if (!open) return;
        open = false;
        BackNavigator.Unregister(this);
        RemoveAdsPurchase.Changed -= OnChanged;
        ParagonPurchase.Changed -= OnChanged;
        SupportPurchase.Changed -= OnChanged;
        SupportPurchase.Rewarded -= OnRewarded;
        DeveloperUnlocks.Changed -= OnChanged;
        gameObject.SetActive(false);
        HideMenu(false);
        var handler = Closed;
        if (handler != null) handler();
    }

    // The home menu's own rows are covered while the Shop is up (its scrim lets the star backdrop show).
    CanvasGroup menuGroup;
    float menuAlpha = 1f;
    void HideMenu(bool hide)
    {
        if (hide)
        {
            var menu = SceneUtil.FindAny("MainMenuCanvas");
            if (menu == null) return;
            if (!menu.TryGetComponent(out menuGroup)) menuGroup = menu.AddComponent<CanvasGroup>();
            menuAlpha = menuGroup.alpha;
            menuGroup.alpha = 0f;
        }
        else if (menuGroup != null) { menuGroup.alpha = menuAlpha; menuGroup = null; }
    }

    // BackNavigator layer: Back closes the Shop (one level), never falls through to quit.
    bool OnBackPressed()
    {
        if (!open) return false;
        Close();
        return true;
    }

    // A panel whose scene was unloaded outside play (edit-mode suites never run OnDestroy) is a destroyed
    // object still on the static events: drop it instead of painting its dead texts.
    void OnChanged() { if (this == null) { Unsubscribe(); return; } if (open) Refresh(); }
    void OnRewarded(int amount) { if (this == null) { Unsubscribe(); return; } if (open) Refresh(); }

    void Unsubscribe()
    {
        RemoveAdsPurchase.Changed -= OnChanged;
        ParagonPurchase.Changed -= OnChanged;
        SupportPurchase.Changed -= OnChanged;
        SupportPurchase.Rewarded -= OnRewarded;
        DeveloperUnlocks.Changed -= OnChanged;
    }

    void OnDestroy()
    {
        HideMenu(false);
        BackNavigator.Unregister(this);
        Unsubscribe();
        if (instance == this) instance = null;
    }

    string Signature()
    {
        var sb = new System.Text.StringBuilder();
        foreach (var v in rows) sb.Append((int)v.row.state).Append(v.row.button).Append(v.row.title).Append('|');
        sb.Append(Mathf.FloorToInt(StarDustLedger.Saved));
        return sb.ToString();
    }

    void OnRectTransformDimensionsChange() { dirty = true; }

    void Update()
    {
        if (!open) return;
        // The store answers without an event for some changes (a cancelled sheet clears Buying): poll cheaply.
        if (Time.unscaledTime >= nextPoll)
        {
            nextPoll = Time.unscaledTime + PollSeconds;
            if (Signature() != signature) { Refresh(); return; }
        }
        var size = ((RectTransform)canvas.transform).rect.size;
        if (dirty || size != lastSize || CodexUi.SafeAreaUnits(canvas) != lastSafe) Layout();
    }
}
