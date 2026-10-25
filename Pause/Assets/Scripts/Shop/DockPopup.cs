using UnityEngine;
using UnityEngine.UI;

// The ship card that floats over the dock beside the selected ship.
//
// It replaces the old full-screen yes/no dialog. It lives on its own
// world-space canvas, re-anchored to the ship every frame it is visible, and
// is clamped to the visible dock: if there is no room above the ship it
// flips below it, pointer and all; if there is room on neither side (a short
// phone) the dock slides the rack just far enough for it to fit (onNudge).
//
//   owned ship  -> one action, LAUNCH, plus a row of hull-skin swatches:
//                  tap an owned one to equip it (saved at once), a locked one
//                  to preview it on the hull -> its price and BUY
//   locked ship -> its price and BUY; can't afford -> shake + "NEED n MORE"
//
// Sizes: the card is laid out in canvas units that are POINTS / DP (SetDensity:
// one unit = one point on iOS, one dp on Android, from UiScale), so its type
// and touch targets have the same physical size on every phone:
//   name 17, status 13 (price 17), skin name / prices / weapon / start speed
//   12, action label 18; the action button 48 tall, every swatch's touch
//   slot 53 x 50 with ~15 between the drawn chips, the close button 48 x 48.
// The card is PanelWidth (300) units wide; on a screen too narrow or short
// for that (none of the supported phones) the unit shrinks to fit.
public class DockPopup : MonoBehaviour
{
    // ---- layout, canvas units (= points / dp) ----
    public const float PanelWidth = 300f;
    public const float Pad = 12f;
    public const float HeaderHeight = 44f;      // name line + skin-name / hearts / status line
    public const float ButtonHeight = 48f;
    // A locked ship's card: header and the action button.
    // The power preview at the top of the card: art frame, kind chips, name, line.
    // ...and under it, both of the ship's weapons at once (name + line each) and what sets its power off.
    public const float PreviewHeight = 171f;
    public const float BaseHeight = PreviewHeight + Pad + HeaderHeight + 10f + ButtonHeight + Pad;
    // Added for an owned ship: the swatches (their touch slots), the weapon
    // row and the START SPEED line.
    public const float SkinRowHeight = 50f;
    public const float WeaponRowHeight = 20f;
    public const float StartSpeedLineHeight = 18f;
    public const float OwnedHeight = BaseHeight + SkinRowHeight + WeaponRowHeight + StartSpeedLineHeight;
    // Paragon only: a one-line warning strip along the card's bottom edge
    // (PremiumShips.LeaderboardNotice). Other ships' cards are unchanged.
    public const float NoticeHeight = 18f;
    public const float TailUnits = 14f;
    // The whole card is drawn at this fraction of its original size (the
    // layout above is the original, 100%): one constant, applied to the unit
    // in SetDensity, so type, chips, buttons and the card all shrink together.
    public const float PopupScale = .85f;
    // Touch targets keep the platform minimum (48 dp / 44 pt) however small
    // the card is drawn: this many canvas units is 48 points at PopupScale.
    public const float HitUnits = 48f / PopupScale;
    public const float CloseSize = HitUnits;
    // The type sizes (units).
    public const int TitleSize = 17, TitleMinSize = 13, StatusSize = 13, PriceSize = 17, SmallSize = 12, PriceMinSize = 7, ButtonSize = 18;

    // World units per canvas unit before the dock has measured the screen
    // (tests without a camera): about a dp on a 1080-wide phone.
    public const float DefaultUnit = .0132f;
    // World units between the ship's hull and the tail's tip.
    public const float Gap = .04f;
    // A popup flipped below its ship hangs this far under the hull, so the
    // tail's tip clears the engine plume. Ship geometry: not scaled.
    public const float PlumeClearance = .34f;
    const float AppearTime = .2f;

    public enum Mode { Launch, Buy, BuySkin, Locked }

    public int ShipIndex { get; private set; }
    public Mode CurrentMode { get; private set; }
    public bool Visible { get { return ShipIndex > 0 && gameObject.activeSelf; } }
    public bool Flipped { get; private set; }
    public Rect safeView = new Rect(-2.8f, -4f, 5.6f, 8f);

    public System.Action<int> onLaunch;
    public System.Action<int> onBuy;
    public System.Action<int, int> onSkin;      // (ship, skin) swatch tapped
    public System.Action<int, int> onBuySkin;   // (ship, skin) BUY on a previewed skin
    public System.Action onClose;               // the close button
    // Asked to move the ship by `dy` world units (the dock slides its rack)
    // when the card fits neither above nor below it.
    public System.Action<float> onNudge;

    // World units per canvas unit (SetDensity).
    public float Unit { get; private set; }
    public float WorldWidth { get { return PanelWidth * Unit; } }
    public float TailLength { get { return TailUnits * Unit; } }

    // The skin row: shown for owned ships. SkinShown is the swatch outlined
    // (the skin on the hull right now).
    public bool SkinRowVisible { get; private set; }
    public int SkinShown { get; private set; }
    public float CurrentHeightUnits { get { return (SkinRowVisible ? OwnedHeight : BaseHeight) + (noticeOn ? NoticeHeight : 0f); } }
    // The leaderboard warning strip (Paragon's card only).
    bool noticeOn;
    Text notice;
    public bool NoticeVisible { get { return noticeOn; } }
    public string NoticeText { get { return noticeOn && notice != null ? notice.text : ""; } }
    public Color NoticeColor { get { return notice != null ? notice.color : Color.clear; } }
    public float CurrentHeight { get { return CurrentHeightUnits * Unit; } }

    public class Swatch
    {
        public RectTransform root;
        public Image body, band, stripe, ink, ring, check, dust, hit;
        public Text price;
        public Button button;
        public bool owned, equipped;
    }
    public readonly Swatch[] swatches = new Swatch[ShipSkins.PerShip];
    RectTransform skinRow;
    // ---- START SPEED line (ShipStartSpeed) ----
    Text startSpeed;
    public string StartSpeedText { get { return startSpeed != null && startSpeed.gameObject.activeSelf ? startSpeed.text : ""; } }
    // ...and on the same line, right: the hearts and where they come from
    // (SkinHearts.HeartsLine), or what a previewed colour would add (gold).
    Text heartsLine;
    public string HeartsLineText { get { return heartsLine != null && heartsLine.gameObject.activeSelf ? heartsLine.text : ""; } }
    public const float StartSpeedWidth = 96f;

    Canvas canvas;
    Font font;
    CanvasGroup group;
    RectTransform panel, body, tail, dustIcon;
    Text title, skinName, status, message, buttonLabel;
    public Button ActionButton { get; private set; }
    public Button CloseButton { get; private set; }
    // The ship's lives (ShipLives): a heart and its count, left of the status.
    RectTransform livesBadge;
    Text livesLabel;
    Image buttonImage;
    Transform target;
    float above, below;
    float shownAt;
    float shakeUntil, messageUntil;

    public static DockPopup Create(Transform parent, Font font, Camera eventCamera)
    {
        var go = new GameObject("~DockPopup", typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var popup = go.AddComponent<DockPopup>();
        popup.Build(font, eventCamera);
        go.SetActive(false);
        return popup;
    }

    // `worldPerPoint`: world units one point / dp covers on this screen. The
    // card keeps that size unless it would not fit the safe view.
    public void SetDensity(float worldPerPoint)
    {
        float u = (worldPerPoint > 0f ? worldPerPoint : DefaultUnit) * PopupScale;
        if (safeView.width > 0f) u = Mathf.Min(u, safeView.width / PanelWidth);
        if (safeView.height > 0f) u = Mathf.Min(u, safeView.height / (OwnedHeight + (noticeOn ? NoticeHeight : 0f) + TailUnits));
        Unit = u;
        if (gameObject.activeInHierarchy) Follow();
    }

    void Build(Font font, Camera eventCamera)
    {
        this.font = font;
        Unit = DefaultUnit;
        var root = (RectTransform)transform;
        root.sizeDelta = new Vector2(PanelWidth, BaseHeight);
        root.localScale = Vector3.one * Unit;
        canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.worldCamera = eventCamera;
        canvas.overrideSorting = true;
        canvas.sortingOrder = 60;
        var scaler = gameObject.AddComponent<CanvasScaler>();
        scaler.dynamicPixelsPerUnit = 4f;
        scaler.referencePixelsPerUnit = 100f;
        gameObject.AddComponent<GraphicRaycaster>();
        group = gameObject.AddComponent<CanvasGroup>();

        panel = Rect("Panel", root);
        Stretch(panel);
        var panelImage = panel.gameObject.AddComponent<Image>();
        panelImage.sprite = DockArt.Get("popup", 12f);
        panelImage.type = Image.Type.Sliced;
        panelImage.raycastTarget = true;   // taps on the panel never fall through

        tail = Rect("Tail", panel);
        tail.sizeDelta = new Vector2(TailUnits * 1.6f, TailUnits);
        var tailImage = tail.gameObject.AddComponent<Image>();
        tailImage.sprite = DockArt.Get("popup_tail");
        tailImage.raycastTarget = false;

        // Everything below the power preview lives in `body`, laid out from its
        // own top exactly as before the preview existed.
        body = Rect("Body", panel);
        Stretch(body);
        body.offsetMax = new Vector2(0f, -PreviewHeight);
        BuildPreview();

        // Paragon's warning strip: amber, under the action button (the body is raised above it while it shows).
        notice = Label("Notice", panel, font, SmallSize, TextAnchor.MiddleCenter, AkiraPalette.Amber);
        notice.fontStyle = FontStyle.Bold;
        notice.horizontalOverflow = HorizontalWrapMode.Wrap;
        notice.verticalOverflow = VerticalWrapMode.Truncate;
        notice.resizeTextForBestFit = true;
        notice.resizeTextMinSize = SmallSize - 2;
        notice.resizeTextMaxSize = SmallSize;
        notice.text = PremiumShips.LeaderboardNotice;
        Place(notice.rectTransform, new Vector2(.5f, 0f), new Vector2(0f, 3f), new Vector2(PanelWidth - 2f * Pad, NoticeHeight - 4f), new Vector2(.5f, 0f));
        notice.gameObject.SetActive(false);

        // Line 1: the ship's name, and the close button in the corner.
        title = Label("Title", body, font, TitleSize, TextAnchor.MiddleLeft, AkiraPalette.Bone);
        title.fontStyle = FontStyle.Bold;
        title.horizontalOverflow = HorizontalWrapMode.Wrap;
        title.verticalOverflow = VerticalWrapMode.Truncate;
        title.resizeTextForBestFit = true;
        title.resizeTextMinSize = TitleMinSize;
        title.resizeTextMaxSize = TitleSize;
        Place(title.rectTransform, new Vector2(0f, 1f), new Vector2(Pad, -Pad), new Vector2(PanelWidth - 2f * Pad - 36f, 24f), new Vector2(0f, 1f));
        BuildClose();

        // Line 2: the colour's name on the left; hearts and status / price on the right.
        skinName = Label("SkinName", body, font, SmallSize + 1, TextAnchor.MiddleLeft, AkiraPalette.WithAlpha(AkiraPalette.Cyan, .9f));
        skinName.horizontalOverflow = HorizontalWrapMode.Wrap;
        skinName.verticalOverflow = VerticalWrapMode.Truncate;
        skinName.resizeTextForBestFit = true;
        skinName.resizeTextMinSize = SmallSize;
        skinName.resizeTextMaxSize = SmallSize + 1;
        Place(skinName.rectTransform, new Vector2(0f, 1f), new Vector2(Pad, -Pad - 24f), new Vector2(150f, 20f), new Vector2(0f, 1f));
        status = Label("Status", body, font, StatusSize, TextAnchor.MiddleRight, DockArt.Gold);
        Place(status.rectTransform, new Vector2(1f, 1f), new Vector2(-Pad, -Pad - 24f), new Vector2(120f, 20f), new Vector2(1f, 1f));
        dustIcon = Rect("Dust", body);
        var dust = dustIcon.gameObject.AddComponent<Image>();
        dust.sprite = DockArt.Get("icon_dust");
        dust.raycastTarget = false;
        Place(dustIcon, new Vector2(1f, 1f), new Vector2(-80f, -Pad - 34f), new Vector2(15f, 15f), new Vector2(.5f, .5f));
        livesBadge = Rect("Lives", body);
        Place(livesBadge, new Vector2(1f, 1f), new Vector2(-100f, -Pad - 34f), new Vector2(LivesIconSize + 16f, 18f), new Vector2(1f, .5f));
        var heart = Rect("Heart", livesBadge);
        Place(heart, new Vector2(0f, .5f), Vector2.zero, new Vector2(LivesIconSize, LivesIconSize), new Vector2(0f, .5f));
        var heartImage = heart.gameObject.AddComponent<Image>();
        heartImage.sprite = Resources.Load<Sprite>("Vfx/lifeHeart");
        heartImage.preserveAspect = true;
        heartImage.raycastTarget = false;
        livesLabel = Label("Count", livesBadge, font, StatusSize, TextAnchor.MiddleLeft, AkiraPalette.Bone);
        Place(livesLabel.rectTransform, new Vector2(0f, .5f), new Vector2(LivesIconSize + 3f, 0f), new Vector2(14f, 18f), new Vector2(0f, .5f));
        // NEED n MORE / ACQUIRED: over the header while it is up.
        message = Label("Message", body, font, 15, TextAnchor.MiddleCenter, DockArt.Warn);
        message.fontStyle = FontStyle.Bold;
        Place(message.rectTransform, new Vector2(.5f, 1f), new Vector2(0f, -Pad), new Vector2(PanelWidth - 2f * Pad, HeaderHeight), new Vector2(.5f, 1f));

        var buttonRect = Rect("Action", body);
        Place(buttonRect, new Vector2(.5f, 0f), new Vector2(0f, Pad), new Vector2(PanelWidth - 2f * Pad, ButtonHeight), new Vector2(.5f, 0f));
        buttonImage = buttonRect.gameObject.AddComponent<Image>();
        buttonImage.sprite = DockArt.Get("button", 8f);
        buttonImage.type = Image.Type.Sliced;
        // drawn ButtonHeight tall, its touch target HitUnits (grows up and down)
        float actionPad = Mathf.Max(0f, (HitUnits - ButtonHeight) * .5f);
        buttonImage.raycastPadding = new Vector4(0f, -actionPad, 0f, -actionPad);
        var button = buttonRect.gameObject.AddComponent<Button>();
        button.targetGraphic = buttonImage;
        var colors = button.colors;
        colors.pressedColor = new Color(.8f, .8f, .8f, 1f);
        colors.highlightedColor = Color.white;
        colors.selectedColor = Color.white;
        button.colors = colors;
        button.onClick.AddListener(Clicked);
        ActionButton = button;
        CelPress.AddTo(buttonRect.gameObject);   // cartoon squash-and-pop on press
        buttonLabel = Label("Label", buttonRect, font, ButtonSize, TextAnchor.MiddleCenter, DockArt.Ink);
        Stretch(buttonLabel.rectTransform);
        buttonLabel.fontStyle = FontStyle.Bold;

        BuildSkinRow();
        BuildWeaponRow();   // weapon level row (ShipWeaponUpgrades)
        BuildStartSpeedLine();
    }

    // ---- BEGIN power preview ---------------------------------------------
    //
    // A small looping flipbook of the selected ship's ULTIMATE (its attack
    // strip) and POWER (its secret-power strip): loop frames, then the burst.
    // It alternates every PreviewAutoSeconds until the player taps a chip (or
    // the frame). Sprites are cached per ship in Show; the tick only touches
    // the Image when the frame changes, and only runs while the popup is up.
    public const float PreviewAutoSeconds = 3f;
    const float PreviewArtTop = 8f, PreviewArtHeight = 62f, ChipWidth = 74f, ChipHeight = 16f;
    RectTransform previewRoot;
    Image previewArt;
    Vector2 previewArtAt;
    int previewSkinSeen = -1;   // the skin whose attack art the cells hold (Special skins can have their own strips)
    RectTransform previewArtRect;
    public RectTransform PreviewArtRect { get { return previewArtRect; } }
    Text previewName, previewLine, powerNameText, powerLineText, triggerText;
    readonly Image[] previewChips = new Image[2];
    readonly Text[] previewChipLabels = new Text[2];
    // [0] ultimate (attack), [1] power; each the ship's PowerPreview plan, cells end to end.
    readonly Sprite[][] previewCells = { new Sprite[PowerPreview.Cells], new Sprite[PowerPreview.Cells] };
    int previewShip = -1;
    bool previewPower, previewAuto = true;
    float previewKindAt, previewClockAt;
    int previewTick = -2;
    public bool PreviewShowsPower { get { return previewPower; } }
    public bool PreviewAutoAlternates { get { return previewAuto; } }
    public string PreviewName { get { var t = previewPower ? powerNameText : previewName; return t != null ? t.text : ""; } }
    public string PreviewLine { get { var t = previewPower ? powerLineText : previewLine; return t != null ? t.text : ""; } }
    // the card lists both weapons at once (the frame's kind is only the bright one)
    public string AttackNameText { get { return previewName != null ? previewName.text : ""; } }
    public string AttackLineText { get { return previewLine != null ? previewLine.text : ""; } }
    public string PowerNameText { get { return powerNameText != null ? powerNameText.text : ""; } }
    public string PowerLineText { get { return powerLineText != null ? powerLineText.text : ""; } }
    public string TriggerText { get { return triggerText != null ? triggerText.text : ""; } }
    public Sprite PreviewSprite { get { return previewArt != null && previewArt.enabled ? previewArt.sprite : null; } }
    public RectTransform PreviewRect { get { return previewRoot; } }

    void BuildPreview()
    {
        previewRoot = Rect("Preview", panel);
        Place(previewRoot, new Vector2(.5f, 1f), Vector2.zero, new Vector2(PanelWidth, PreviewHeight), new Vector2(.5f, 1f));
        float w = PanelWidth - 2f * Pad;
        // dark neon-framed panel: a cyan rim and an ink body, both the dock's button art
        var rim = Rect("Rim", previewRoot);
        Place(rim, new Vector2(.5f, 1f), new Vector2(0f, -PreviewArtTop), new Vector2(w, PreviewArtHeight), new Vector2(.5f, 1f));
        var rimImage = rim.gameObject.AddComponent<Image>();
        rimImage.sprite = DockArt.Get("button", 8f);
        rimImage.type = Image.Type.Sliced;
        rimImage.color = AkiraPalette.WithAlpha(AkiraPalette.Cyan, .55f);
        rimImage.raycastTarget = true;
        var tap = rim.gameObject.AddComponent<Button>();
        tap.targetGraphic = rimImage;
        tap.transition = Selectable.Transition.None;
        tap.onClick.AddListener(() => SetPreviewKind(!previewPower));
        var inner = Rect("Inner", rim);
        Stretch(inner);
        inner.offsetMin = new Vector2(2f, 2f);
        inner.offsetMax = new Vector2(-2f, -2f);
        var innerImage = inner.gameObject.AddComponent<Image>();
        innerImage.sprite = DockArt.Get("button", 8f);
        innerImage.type = Image.Type.Sliced;
        innerImage.color = new Color(.03f, .04f, .10f, 1f);
        innerImage.raycastTarget = false;
        // the art may be scaled past the panel (PowerPreview.Fit): clip it to the frame
        var clip = Rect("Clip", rim);
        Stretch(clip);
        clip.offsetMin = new Vector2(3f, 3f);
        clip.offsetMax = new Vector2(-3f, -3f);
        clip.gameObject.AddComponent<RectMask2D>();
        var art = Rect("Art", clip);
        previewArtRect = art;
        Place(art, new Vector2(.5f, .5f), Vector2.zero, new Vector2(PreviewArtHeight - 6f, PreviewArtHeight - 6f), new Vector2(.5f, .5f));
        previewArt = art.gameObject.AddComponent<Image>();
        previewArt.preserveAspect = true;
        previewArt.raycastTarget = false;
        previewArt.enabled = false;
        for (int k = 0; k < 2; k++)
        {
            var chip = Rect(k == 0 ? "ChipUltimate" : "ChipPower", rim);
            Place(chip, new Vector2(k == 0 ? 0f : 1f, 1f), new Vector2(k == 0 ? 6f : -6f, -5f),
                  new Vector2(ChipWidth, ChipHeight), new Vector2(k == 0 ? 0f : 1f, 1f));
            var image = chip.gameObject.AddComponent<Image>();
            image.sprite = DockArt.Get("button", 8f);
            image.type = Image.Type.Sliced;
            image.raycastPadding = new Vector4(-4f, -(HitUnits - ChipHeight) * .5f, -4f, -(HitUnits - ChipHeight) * .5f);
            var button = chip.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            button.transition = Selectable.Transition.None;
            bool power = k == 1;
            button.onClick.AddListener(() => { previewAuto = false; SetPreviewKind(power); });
            var label = Label("Label", chip, font, 12, TextAnchor.MiddleCenter, AkiraPalette.Ink);
            label.text = k == 0 ? "ULTIMATE" : "POWER";
            label.fontStyle = FontStyle.Bold;
            Stretch(label.rectTransform);
            previewChips[k] = image;
            previewChipLabels[k] = label;
        }
        float textTop = PreviewArtTop + PreviewArtHeight + 3f;
        // Two stacked blocks: the ultimate (gold) and the secret power (cyan), each a bold
        // name and a wrapped line, then the power's trigger. The kind in the frame above
        // is the bright one.
        float y = textTop;
        previewName = InfoName("AttackName", y, DockArt.Gold, w);
        previewLine = InfoLine("AttackLine", y + 14f, w);
        y += 14f + 26f + 2f;
        powerNameText = InfoName("PowerName", y, DockArt.Cyan, w);
        powerLineText = InfoLine("PowerLine", y + 14f, w);
        y += 14f + 26f + 2f;
        triggerText = Label("PowerTrigger", previewRoot, font, SmallSize - 1, TextAnchor.MiddleCenter, AkiraPalette.Muted);
        triggerText.horizontalOverflow = HorizontalWrapMode.Wrap;
        triggerText.verticalOverflow = VerticalWrapMode.Truncate;
        Place(triggerText.rectTransform, new Vector2(.5f, 1f), new Vector2(0f, -y), new Vector2(w, 13f), new Vector2(.5f, 1f));
    }

    Text InfoName(string name, float top, Color colour, float w)
    {
        var t = Label(name, previewRoot, font, SmallSize + 1, TextAnchor.MiddleCenter, colour);
        t.fontStyle = FontStyle.Bold;
        t.horizontalOverflow = HorizontalWrapMode.Wrap;
        t.verticalOverflow = VerticalWrapMode.Truncate;
        Place(t.rectTransform, new Vector2(.5f, 1f), new Vector2(0f, -top), new Vector2(w, 14f), new Vector2(.5f, 1f));
        return t;
    }

    Text InfoLine(string name, float top, float w)
    {
        var t = Label(name, previewRoot, font, SmallSize - 1, TextAnchor.UpperCenter, AkiraPalette.Muted);
        t.horizontalOverflow = HorizontalWrapMode.Wrap;
        t.verticalOverflow = VerticalWrapMode.Truncate;
        Place(t.rectTransform, new Vector2(.5f, 1f), new Vector2(0f, -top), new Vector2(w, 26f), new Vector2(.5f, 1f));
        return t;
    }

    // A new ship (or the card just opened): reload its cells, start on the
    // ultimate, and alternate on its own again.
    void ResetPreview()
    {
        previewShip = ShipIndex;
        previewSkinSeen = ShipSkins.Shown(ShipIndex);
        PowerPreview.Fill(ShipIndex, false, previewCells[0]);
        PowerPreview.Fill(ShipIndex, true, previewCells[1]);
        previewAuto = true;
        previewKindAt = Time.unscaledTime;
        ApplyPreviewKind(false);
    }

    // A tap on the frame toggles (and stops the auto-alternation).
    void SetPreviewKind(bool power)
    {
        previewAuto = false;
        if (previewPower != power || previewShip < 0) ApplyPreviewKind(power);
    }

    public void TapPreviewChip(bool power) { previewAuto = false; SetPreviewKind(power); }

    void ApplyPreviewKind(bool power)
    {
        previewPower = power;
        previewClockAt = Time.unscaledTime;
        previewTick = -2;
        Vector2 artSize, artAt;
        PowerPreview.Fit(ShipIndex, power, previewCells[power ? 1 : 0], PreviewArtHeight, PanelWidth - 2f * Pad, out artSize, out artAt);
        previewArtRect.sizeDelta = artSize;
        previewArtRect.anchoredPosition = artAt;
        previewArtAt = artAt;
        previewName.text = PowerPreview.Name(ShipIndex, false);
        previewLine.text = PowerPreview.Line(ShipIndex, false);
        powerNameText.text = PowerPreview.Name(ShipIndex, true);
        powerLineText.text = PowerPreview.Line(ShipIndex, true);
        triggerText.text = "POWER FIRES  " + ShipLoadoutTable.TriggerLine(ShipLoadoutTable.For(ShipIndex)).ToUpperInvariant();
        for (int k = 0; k < 2; k++)
        {
            bool on = (k == 1) == power;
            previewChips[k].color = on ? DockArt.Cyan : new Color(.12f, .15f, .28f, 1f);
            previewChipLabels[k].color = on ? DockArt.Ink : AkiraPalette.Muted;
        }
        // the kind shown in the frame is the bright one
        previewName.color = AkiraPalette.WithAlpha(DockArt.Gold, power ? .6f : 1f);
        powerNameText.color = AkiraPalette.WithAlpha(DockArt.Cyan, power ? 1f : .6f);
        TickPreview(Time.unscaledTime);
    }

    void TickPreview(float now)
    {
        if (previewShip < 0) return;
        int skinNow = ShipSkins.Shown(ShipIndex);
        if (skinNow != previewSkinSeen)
        {
            // a swatch was tapped: the shown skin may have its own attack art
            previewSkinSeen = skinNow;
            PowerPreview.Fill(ShipIndex, false, previewCells[0]);
            PowerPreview.Fill(ShipIndex, true, previewCells[1]);
            previewTick = -2;
        }
        if (previewAuto && now - previewKindAt >= PreviewAutoSeconds)
        {
            previewKindAt = now;
            ApplyPreviewKind(!previewPower);
            return;
        }
        var seq = PowerPreview.Sequence(ShipIndex, previewPower);
        int tick = (int)((now - previewClockAt) * PowerPreview.Fps) % seq.Length;
        if (tick < 0 || tick == previewTick) return;
        previewTick = tick;
        int cell = seq[tick];
        var sprite = cell >= 0 ? previewCells[previewPower ? 1 : 0][cell] : null;
        previewArt.sprite = sprite;
        previewArt.enabled = sprite != null;
        // the wave and the gust travel up the frame instead of sitting as a thin smudge
        if (!previewPower && PowerPreview.Rises(ShipIndex))
            previewArtRect.anchoredPosition = previewArtAt + Vector2.up * PowerPreview.RiseOffset(tick, PreviewArtHeight);
    }
    // ---- END power preview ------------------------------------------------

    // The card's corner X: closes it (as tapping anywhere off it or Back does).
    void BuildClose()
    {
        var rt = Rect("Close", body);
        // drawn 28 x 28 in the top-right corner; its touch target 48 x 48
        Place(rt, new Vector2(1f, 1f), new Vector2(-8f, -8f), new Vector2(28f, 28f), new Vector2(1f, 1f));
        var hit = rt.gameObject.AddComponent<Image>();
        hit.color = new Color(0f, 0f, 0f, 0f);
        hit.raycastTarget = true;
        // The target grows inward only (left and down): the drawn X sits 8 from
        // the card's corner, so reaching 8 outward would just touch the card
        // edge, and the card may sit against the screen edge.
        float grow = CloseSize - 28f - 8f;
        hit.raycastPadding = new Vector4(-grow, -grow, -8f, -8f);
        var label = Label("X", rt, font, 16, TextAnchor.MiddleCenter, AkiraPalette.Muted);
        label.fontStyle = FontStyle.Bold;
        label.text = "X";
        Stretch(label.rectTransform);
        CloseButton = rt.gameObject.AddComponent<Button>();
        CloseButton.targetGraphic = hit;
        CloseButton.transition = Selectable.Transition.None;
        CloseButton.onClick.AddListener(CloseClicked);
        CelPress.AddTo(rt.gameObject);
    }

    void CloseClicked()
    {
        if (!Visible) return;
        if (onClose != null) onClose();
        else Hide();
    }

    // ---- START SPEED line: the colour shown's start speed, under the weapon
    // row, above the action button.
    void BuildStartSpeedLine()
    {
        float y = -(SkinRowTop + SkinRowHeight + WeaponRowHeight) - 1f;
        startSpeed = Label("StartSpeed", body, font, SmallSize, TextAnchor.MiddleLeft,
                           AkiraPalette.WithAlpha(AkiraPalette.Cyan, .9f));
        Place(startSpeed.rectTransform, new Vector2(0f, 1f), new Vector2(Pad, y),
              new Vector2(StartSpeedWidth, StartSpeedLineHeight - 2f), new Vector2(0f, 1f));
        startSpeed.gameObject.SetActive(false);

        heartsLine = Label("Hearts", body, font, SmallSize, TextAnchor.MiddleRight,
                           AkiraPalette.WithAlpha(AkiraPalette.Cyan, .9f));
        heartsLine.horizontalOverflow = HorizontalWrapMode.Wrap;
        heartsLine.verticalOverflow = VerticalWrapMode.Truncate;
        heartsLine.resizeTextForBestFit = true;
        heartsLine.resizeTextMinSize = SmallSize - 2;
        heartsLine.resizeTextMaxSize = SmallSize;
        Place(heartsLine.rectTransform, new Vector2(1f, 1f), new Vector2(-Pad, y),
              new Vector2(PanelWidth - 2f * Pad - StartSpeedWidth - 4f, StartSpeedLineHeight - 2f), new Vector2(1f, 1f));
        heartsLine.gameObject.SetActive(false);
    }

    void ShowStartSpeed(int index, int skin)
    {
        startSpeed.text = ShipStartSpeed.Label(ShipStartSpeed.HudFor(index, skin));
        startSpeed.gameObject.SetActive(true);
        // An unbought colour previewed: what buying it adds to the hearts.
        string buy = ShipSkins.IsOwned(index, skin) ? "" : SkinHearts.BuyLine(index, skin);
        heartsLine.text = buy.Length > 0 ? buy : SkinHearts.HeartsLine(index);
        heartsLine.color = buy.Length > 0 ? DockArt.Gold : AkiraPalette.WithAlpha(AkiraPalette.Cyan, .9f);
        heartsLine.gameObject.SetActive(true);
    }

    // A locked ship has no skin row, so its start speed takes the colour-name
    // slot of the header: "START SPD 10", the gain over the equipped ship in gold.
    void ShowLockedSpeed(int index)
    {
        string gain;
        string line = ShipStartSpeed.LockedLabel(index, ShipStartSpeed.EquippedHud(), out gain);
        skinName.text = gain.Length > 0
            ? line + " <color=#" + ColorUtility.ToHtmlStringRGB(DockArt.Gold) + ">" + gain + "</color>"
            : line;
    }

    // Five angular chips in each skin's own colours (base, shadow band, and a
    // special's livery stripe), between the header and the action. Each sits
    // in its own touch slot: the row's width split five ways (~55 units),
    // the drawn chips ~15 apart so a finger lands on one or the other.
    const float SkinRowTop = Pad + HeaderHeight + 6f;
    const float ChipW = 40f, ChipH = 24f;
    // The slots tile the whole card width (the outer ones reach into its
    // padding) so each is a finger wide even at PopupScale.
    const float SlotW = PanelWidth / ShipSkins.PerShip;
    const float SlotContent = ChipH + 4f + 14f;   // chip, gap, price line

    void BuildSkinRow()
    {
        skinRow = Rect("Skins", body);
        Place(skinRow, new Vector2(.5f, 1f), new Vector2(0f, -SkinRowTop), new Vector2(PanelWidth, SkinRowHeight), new Vector2(.5f, 1f));
        float top = (SkinRowHeight - SlotContent) * .5f;   // the slot's overhang above the chip
        for (int n = 0; n < swatches.Length; n++)
        {
            var w = new Swatch();
            w.root = Rect("Swatch" + n, skinRow);
            Place(w.root, new Vector2(0f, 1f), new Vector2(n * SlotW + SlotW * .5f, -top - ChipH * .5f),
                  new Vector2(ChipW, ChipH), new Vector2(.5f, .5f));
            // The touch target is the whole slot (chip, price, half of each
            // gap): 2 units narrower than the slot so neighbours never touch.
            w.hit = Chip(w.root, "Hit", null, new Vector2(SlotW - 2f, HitUnits));
            w.hit.rectTransform.anchoredPosition = new Vector2(0f, ChipH * .5f + top - SkinRowHeight * .5f);
            w.hit.color = new Color(0f, 0f, 0f, 0f);
            w.hit.raycastTarget = true;
            w.ring = Chip(w.root, "Ring", "swatch_ring", new Vector2(ChipW + 6f, ChipH + 6f));
            w.body = Chip(w.root, "Body", "swatch", new Vector2(ChipW, ChipH));
            w.band = Chip(w.root, "Band", "swatch_band", new Vector2(ChipW, ChipH));
            w.stripe = Chip(w.root, "Stripe", "swatch_stripe", new Vector2(ChipW, ChipH));
            w.ink = Chip(w.root, "Ink", "swatch_ink", new Vector2(ChipW, ChipH));
            w.check = Chip(w.root, "Check", "swatch_check", new Vector2(13f, 11.6f));
            w.check.rectTransform.anchoredPosition = new Vector2(ChipW * .5f - 5f, ChipH * .5f - 2.5f);
            float priceY = -ChipH * .5f - 4f - 7f;
            w.dust = Chip(w.root, "Dust", "icon_dust", new Vector2(10f, 10f));
            w.dust.rectTransform.anchoredPosition = new Vector2(-15f, priceY);
            w.price = Label("Price", w.root, font, SmallSize, TextAnchor.MiddleLeft, DockArt.Gold);
            // Whatever the price reads ("1,600", "50K"), it shrinks to fit the slot instead of
            // running past the chip row (five chips share the card's width).
            w.price.resizeTextForBestFit = true;
            w.price.resizeTextMinSize = PriceMinSize;
            w.price.resizeTextMaxSize = SmallSize;
            w.price.horizontalOverflow = HorizontalWrapMode.Wrap;
            w.price.verticalOverflow = VerticalWrapMode.Truncate;
            Place(w.price.rectTransform, new Vector2(.5f, .5f), new Vector2(-9f, priceY),
                  new Vector2(SlotW * .5f + 4f, 14f), new Vector2(0f, .5f));
            w.button = w.root.gameObject.AddComponent<Button>();
            w.button.targetGraphic = w.hit;
            w.button.transition = Selectable.Transition.None;
            int skin = n;
            w.button.onClick.AddListener(() => SwatchClicked(skin));
            CelPress.AddTo(w.root.gameObject);
            swatches[n] = w;
        }
        skinRow.gameObject.SetActive(false);
    }

    Image Chip(RectTransform parent, string name, string sprite, Vector2 size)
    {
        var rt = Rect(name, parent);
        Place(rt, new Vector2(.5f, .5f), Vector2.zero, size, new Vector2(.5f, .5f));
        var image = rt.gameObject.AddComponent<Image>();
        if (sprite != null) image.sprite = DockArt.Get(sprite);
        image.raycastTarget = false;
        return image;
    }

    void SwatchClicked(int skin)
    {
        if (!Visible || !SkinRowVisible || CurrentMode == Mode.Buy || CurrentMode == Mode.Locked) return;
        if (onSkin != null) onSkin(ShipIndex, skin);
    }

    // Taps a swatch as a player would (tests, previews).
    public void TapSwatch(int skin) { SwatchClicked(skin); }

    // Fills the skin row for owned ship `index`: `shown` is the skin on the
    // hull. A shown skin that isn't owned turns the action into BUY.
    public void ShowSkins(int index, int shown, float balance)
    {
        if (index != ShipIndex || CurrentMode == Mode.Buy || CurrentMode == Mode.Locked) return;
        SkinRowVisible = true;
        SkinShown = shown;
        skinRow.gameObject.SetActive(true);
        ShowStartSpeed(index, shown);
        int equipped = ShipSkins.Equipped(index);
        for (int n = 0; n < swatches.Length; n++)
        {
            var w = swatches[n];
            bool exists = ShipSkins.Has(index, n);
            w.root.gameObject.SetActive(exists);
            if (!exists) continue;
            var skin = ShipSkins.Get(index, n);
            w.owned = ShipSkins.IsOwned(index, n);
            w.equipped = n == equipped;
            // Locked chips sit a touch back (dimmer), owned ones at full colour.
            float k = w.owned ? 1f : .72f;
            w.body.color = Dim(skin.primary, k);
            w.band.color = Dim(skin.shadow, k);
            w.stripe.enabled = skin.pattern != null;
            w.stripe.color = Dim(skin.accentColor, k);
            w.ring.enabled = n == shown;
            w.check.enabled = w.owned;
            w.check.color = new Color(1f, 1f, 1f, w.equipped ? 1f : .55f);
            w.price.enabled = !w.owned;
            w.dust.enabled = !w.owned;
            if (!w.owned)
            {
                float price = ShipSkins.PriceOf(index, n);
                // (a premium ship's 50,000 skins read "50K": five chips share the row)
                w.price.text = price >= 10000f ? Mathf.RoundToInt(price / 1000f) + "K" : Mathf.RoundToInt(price).ToString("N0");
                w.price.color = balance >= price ? DockArt.Gold : DockArt.Warn;
            }
        }
        ShowWeaponRow(index, shown);   // weapon level row (ShipWeaponUpgrades)
        // The colour's name: line 2, under the ship's.
        skinName.text = shown == ShipSkins.Stock ? "" : ShipSkins.Get(index, shown).DisplayName;
        title.text = (shopingShips.NameFor(index) ?? "").ToUpperInvariant();
        ShowLives(index);
        if (!ShipSkins.IsOwned(index, shown))
        {
            CurrentMode = Mode.BuySkin;
            float price = ShipSkins.PriceOf(index, shown);
            bool affordable = balance >= price;
            ShowPrice(price, affordable);
            buttonLabel.text = "BUY";
            buttonImage.color = affordable ? DockArt.Gold : AkiraPalette.GunHi;
        }
        else
        {
            CurrentMode = Mode.Launch;
            ShowStatus(shown == equipped && ShipIndex == SpaceDock.EquippedIndex() ? "EQUIPPED" : "");
            buttonLabel.text = "LAUNCH";
            buttonImage.color = DockArt.Cyan;
        }
        FitHeader();
        SetRowVisible(messageUntil <= 0f);
        Resize();
        Follow();
    }

    void ShowPrice(float price, bool affordable)
    {
        status.text = Mathf.RoundToInt(price).ToString("N0");
        status.color = affordable ? DockArt.Gold : DockArt.Warn;
        status.fontSize = PriceSize;
        status.fontStyle = FontStyle.Bold;
    }

    void ShowStatus(string text)
    {
        status.text = text;
        status.color = AkiraPalette.WithAlpha(AkiraPalette.Cyan, .9f);
        status.fontSize = StatusSize;
        status.fontStyle = FontStyle.Normal;
    }

    // ---- BEGIN weapon row (ShipWeaponUpgrades) -------------------------
    //
    // Under the skin chips: WEAPON, one pip per level (lit = colours owned),
    // and what one more colour adds. A previewed (unbought) skin lights the
    // pip it would add in gold and names the upgrade; at the top level the
    // row reads MAX. Lives inside the skin row, so it shows and hides with it.
    RectTransform weaponRow;
    Text weaponTitle, weaponLabel;
    readonly Image[] weaponPips = new Image[ShipWeaponUpgrades.MaxLevel];
    public int WeaponLevelShown { get; private set; }
    public string WeaponLine { get { return weaponLabel != null ? weaponLabel.text : ""; } }
    public Image WeaponPip(int i) { return weaponPips[i]; }
    const float PipSize = 9f, PipGap = 4f, PipX = 66f;

    void BuildWeaponRow()
    {
        float rowW = PanelWidth - 2f * Pad;
        weaponRow = Rect("Weapon", skinRow);
        Place(weaponRow, new Vector2(.5f, 1f), new Vector2(0f, -SkinRowHeight - 1f), new Vector2(rowW, WeaponRowHeight - 2f), new Vector2(.5f, 1f));
        weaponTitle = Label("Title", weaponRow, font, SmallSize, TextAnchor.MiddleLeft, AkiraPalette.Muted);
        weaponTitle.text = "WEAPON";
        Place(weaponTitle.rectTransform, new Vector2(0f, .5f), Vector2.zero, new Vector2(PipX - 4f, WeaponRowHeight - 2f), new Vector2(0f, .5f));
        for (int i = 0; i < weaponPips.Length; i++)
        {
            var rt = Rect("Pip" + i, weaponRow);
            Place(rt, new Vector2(0f, .5f), new Vector2(PipX + i * (PipSize + PipGap), 0f),
                  new Vector2(PipSize, PipSize), new Vector2(0f, .5f));
            weaponPips[i] = rt.gameObject.AddComponent<Image>();
            weaponPips[i].raycastTarget = false;
        }
        float labelX = PipX + weaponPips.Length * (PipSize + PipGap) + 4f;
        weaponLabel = Label("Next", weaponRow, font, SmallSize, TextAnchor.MiddleRight, DockArt.Gold);
        weaponLabel.horizontalOverflow = HorizontalWrapMode.Wrap;
        weaponLabel.verticalOverflow = VerticalWrapMode.Truncate;
        weaponLabel.resizeTextForBestFit = true;
        weaponLabel.resizeTextMinSize = SmallSize - 1;
        weaponLabel.resizeTextMaxSize = SmallSize;
        Place(weaponLabel.rectTransform, new Vector2(1f, .5f), Vector2.zero, new Vector2(rowW - labelX, WeaponRowHeight - 2f), new Vector2(1f, .5f));
    }

    void ShowWeaponRow(int index, int shown)
    {
        int level = ShipWeaponUpgrades.Level(index);
        bool buying = !ShipSkins.IsOwned(index, shown) && level < ShipWeaponUpgrades.MaxLevel;
        WeaponLevelShown = level;
        for (int i = 0; i < weaponPips.Length; i++)
            weaponPips[i].color = i < level ? AkiraPalette.Cyan
                                : buying && i == level ? DockArt.Gold
                                : AkiraPalette.Hairline;
        string next = ShipWeaponUpgrades.NextLabel(index);
        if (next == null) { weaponLabel.text = "MAX"; weaponLabel.color = AkiraPalette.Cyan; }
        else if (buying) { weaponLabel.text = next; weaponLabel.color = DockArt.Gold; }
        else { weaponLabel.text = "NEXT " + next; weaponLabel.color = AkiraPalette.Muted; }
    }
    // ---- END weapon row ----------------------------------------------

    public void HideSkins()
    {
        SkinRowVisible = false;
        if (skinRow != null) skinRow.gameObject.SetActive(false);
        if (startSpeed != null) startSpeed.gameObject.SetActive(false);
        if (heartsLine != null) heartsLine.gameObject.SetActive(false);
        if (skinName != null) skinName.text = "";
        Resize();
    }

    static Color Dim(Color c, float k) { return new Color(c.r * k, c.g * k, c.b * k, 1f); }

    // Shows / hides Paragon's warning strip and makes room for it (Resize follows).
    void SetNotice(bool on)
    {
        noticeOn = on;
        notice.gameObject.SetActive(on);
        body.offsetMin = new Vector2(0f, on ? NoticeHeight : 0f);
    }

    void Resize()
    {
        ((RectTransform)transform).sizeDelta = new Vector2(PanelWidth, CurrentHeightUnits);
    }

    const float LivesIconSize = 15f;

    // Hearts the ship shown flies with (ShipLives.Max: its hull plus what its
    // colours add, SkinHearts). While an unbought colour that would add
    // hearts is previewed the badge reads e.g. "3+1", the gain in gold.
    public int LivesShown { get; private set; }
    public int LivesGainShown { get; private set; }
    public string LivesBadgeText { get { return livesLabel != null ? livesLabel.text : ""; } }
    public bool LivesBadgeVisible { get { return livesBadge != null && livesBadge.gameObject.activeSelf; } }

    void ShowLives(int index)
    {
        LivesShown = ShipLives.Max(index);
        LivesGainShown = SkinRowVisible && index == ShipIndex && !ShipSkins.IsOwned(index, SkinShown)
            ? SkinHearts.GainIfBought(index, SkinShown) : 0;
        livesLabel.text = LivesGainShown > 0
            ? LivesShown + "<color=#" + ColorUtility.ToHtmlStringRGB(DockArt.Gold) + ">+" + LivesGainShown + "</color>"
            : LivesShown.ToString();
    }

    // Line 2, right to left: status / price (with the dust icon before a
    // price), the hearts, then whatever is left for the colour's name.
    void FitHeader()
    {
        bool price = CurrentMode == Mode.Buy || CurrentMode == Mode.BuySkin;
        float statusWidth = status.text.Length > 0 ? status.preferredWidth : 0f;
        float x = Pad + statusWidth + (statusWidth > 0f ? 6f : 0f);
        if (price)
        {
            dustIcon.anchoredPosition = new Vector2(-x - 7.5f, -Pad - 34f);
            x += 15f + 8f;
        }
        // ("3+1" while a colour that adds a heart is previewed: wider)
        livesLabel.rectTransform.sizeDelta = new Vector2(Mathf.Max(14f, livesLabel.preferredWidth + 1f), 18f);
        float livesWidth = LivesIconSize + 3f + livesLabel.preferredWidth;
        livesBadge.sizeDelta = new Vector2(livesWidth, 18f);
        livesBadge.anchoredPosition = new Vector2(-x, -Pad - 34f);
        x += livesWidth + 10f;
        skinName.rectTransform.sizeDelta = new Vector2(Mathf.Max(0f, PanelWidth - Pad - x), 20f);
    }

    // The right end (panel units from its left edge) of the name line and of
    // the colour-name line, and the left end of the hearts: for tests.
    public float TitleRight { get { return title.rectTransform.anchoredPosition.x + title.rectTransform.sizeDelta.x; } }
    public float SkinNameRight { get { return skinName.rectTransform.anchoredPosition.x + skinName.rectTransform.sizeDelta.x; } }
    public float LivesLeft { get { return PanelWidth + livesBadge.anchoredPosition.x - livesBadge.sizeDelta.x; } }
    public float CloseLeft { get { var rt = (RectTransform)CloseButton.transform; return PanelWidth + rt.anchoredPosition.x - rt.sizeDelta.x; } }
    public string SkinNameText { get { return skinName.text; } }

    public void Show(int index, Transform ship, float halfHeight, bool owned, bool equipped,
                     float price, float balance)
    {
        bool wasVisible = Visible && ShipIndex == index;
        ShipIndex = index;
        target = ship;
        above = halfHeight + Gap;
        below = halfHeight + PlumeClearance;
        gameObject.SetActive(true);
        title.text = (shopingShips.NameFor(index) ?? "").ToUpperInvariant();
        skinName.text = "";
        ShowLives(index);
        messageUntil = 0f;
        if (owned)
        {
            CurrentMode = Mode.Launch;
            // LAUNCH already says it's yours; only call out the equipped one.
            ShowStatus(equipped ? "EQUIPPED" : "");
            dustIcon.gameObject.SetActive(false);
            buttonLabel.text = "LAUNCH";
            buttonImage.color = DockArt.Cyan;
        }
        else if (PremiumShips.IsPremium(index))
        {
            // TODO(IAP): a real-money ship; until the store purchase exists it
            // can't be bought with dust (PremiumShips). The popup says so.
            CurrentMode = Mode.Locked;
            ShowStatus(PremiumShips.ComingSoonLabel);
            dustIcon.gameObject.SetActive(false);
            buttonLabel.text = "SOON";
            buttonImage.color = AkiraPalette.GunHi;
        }
        else
        {
            CurrentMode = Mode.Buy;
            bool affordable = balance >= price;
            ShowPrice(price, affordable);
            dustIcon.gameObject.SetActive(true);
            buttonLabel.text = "BUY";
            buttonImage.color = affordable ? DockArt.Gold : AkiraPalette.GunHi;
        }
        SetNotice(PremiumShips.IsPremium(index));
        HideSkins();
        if (!owned) ShowLockedSpeed(index);
        FitHeader();
        SetRowVisible(true);
        if (!wasVisible) { shownAt = Time.unscaledTime; ResetPreview(); }
        else if (previewShip != index) ResetPreview();
        Follow();
    }

    public void ShowCantAfford(float shortBy)
    {
        message.text = "NEED " + Mathf.CeilToInt(Mathf.Max(0f, shortBy)).ToString("N0") + " MORE";
        message.color = DockArt.Warn;
        messageUntil = Time.unscaledTime + 1.8f;
        shakeUntil = Time.unscaledTime + .35f;
        SetRowVisible(false);
    }

    public void ShowMessage(string text, Color color, float seconds)
    {
        message.text = text;
        message.color = color;
        messageUntil = Time.unscaledTime + seconds;
        SetRowVisible(false);
    }

    public void Hide()
    {
        ShipIndex = 0;
        target = null;
        gameObject.SetActive(false);
    }

    void Clicked()
    {
        if (!Visible) return;
        if (CurrentMode == Mode.Locked) return;   // coming soon (real-money IAP)
        if (CurrentMode == Mode.Launch) { if (onLaunch != null) onLaunch(ShipIndex); }
        else if (CurrentMode == Mode.BuySkin) { if (onBuySkin != null) onBuySkin(ShipIndex, SkinShown); }
        else if (onBuy != null) onBuy(ShipIndex);
    }

    // Shows the popup fully grown at once (previews, tests).
    public void SkipAppear() { shownAt = -100f; }

    // Lets the legacy shopingShips.yes_no() entry point press the action.
    public void Press() { Clicked(); }

    void SetRowVisible(bool row)
    {
        title.enabled = row;
        skinName.enabled = row;
        status.enabled = row;
        livesBadge.gameObject.SetActive(row);
        dustIcon.gameObject.SetActive(row && (CurrentMode == Mode.Buy || CurrentMode == Mode.BuySkin));
        message.enabled = !row;
    }

    void LateUpdate()
    {
        if (target == null) { Hide(); return; }
        float now = Time.unscaledTime;
        TickPreview(now);
        if (messageUntil > 0f && now >= messageUntil)
        {
            messageUntil = 0f;
            SetRowVisible(true);
        }
        float e = Mathf.Clamp01((now - shownAt) / AppearTime);
        group.alpha = DockTween.OutCubic(e);
        float s = Mathf.LerpUnclamped(.82f, 1f, DockTween.OutBack(e)) * Unit;
        transform.localScale = new Vector3(s, s, 1f);
        float shake = now < shakeUntil ? Mathf.Sin(now * 70f) * 5f * ((shakeUntil - now) / .35f) : 0f;
        panel.anchoredPosition = new Vector2(shake, 0f);
        Follow();
    }

    void Follow()
    {
        if (target == null) return;
        bool flipped;
        float shift;
        var size = new Vector2(WorldWidth, CurrentHeight);
        Vector2 center = Place(target.position, above, below, TailLength, size, safeView, out flipped, out shift);
        if (shift != 0f && onNudge != null)
        {
            onNudge(shift);   // the ship moves; place the card against where it is now
            center = Place(target.position, above, below, TailLength, size, safeView, out flipped, out shift);
        }
        Flipped = flipped;
        Vector2 ship = target.position;
        transform.position = new Vector3(center.x, center.y, -1f);
        float margin = 20f * Unit;
        float tailX = Mathf.Clamp(ship.x - center.x, -WorldWidth * .5f + margin, WorldWidth * .5f - margin) / Unit;
        float edge = CurrentHeightUnits * .5f;
        float half = TailUnits * .5f - 1f;   // the tail tucks 1 unit under the frame
        tail.anchoredPosition = new Vector2(tailX, flipped ? edge + half : -edge - half);
        tail.localRotation = flipped ? Quaternion.Euler(0f, 0f, 180f) : Quaternion.identity;
    }

    // Pure placement: centre of a popup of `size` floating above a ship at
    // `ship` (its tail `tail` long), clamped inside `view`. Falls back below
    // the ship when there is no room above. When it fits on neither side,
    // `shift` is how far the ship would have to move (world y; negative =
    // down) for it to fit on the nearer one -- the card is placed as if it
    // had (and clamped into the view).
    public static Vector2 Place(Vector2 ship, float above, float below, float tail, Vector2 size, Rect view,
                                out bool flipped, out float shift)
    {
        float halfW = size.x * .5f, halfH = size.y * .5f;
        float x = view.width >= size.x
            ? Mathf.Clamp(ship.x, view.xMin + halfW, view.xMax - halfW)
            : view.center.x;
        float upY = ship.y + above + tail + halfH;
        float downY = ship.y - below - tail - halfH;
        float y = upY;
        flipped = false;
        shift = 0f;
        if (upY + halfH > view.yMax)
        {
            if (downY - halfH >= view.yMin)
            {
                y = downY;
                flipped = true;
            }
            else
            {
                float needDown = upY + halfH - view.yMax;   // ship moves down by this
                float needUp = view.yMin - (downY - halfH);  // ship moves up by this
                if (needDown <= needUp) { shift = -needDown; y = upY - needDown; }
                else { shift = needUp; y = downY + needUp; flipped = true; }
            }
        }
        if (view.height >= size.y) y = Mathf.Clamp(y, view.yMin + halfH, view.yMax - halfH);
        return new Vector2(x, y);
    }

    // World-space corners of the popup panel, for tests.
    public Rect WorldRect
    {
        get
        {
            Vector3 c = transform.position;
            return new Rect(c.x - WorldWidth * .5f, c.y - CurrentHeight * .5f, WorldWidth, CurrentHeight);
        }
    }

    static RectTransform Rect(string name, Transform parent)
    {
        var rt = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
        rt.SetParent(parent, false);
        return rt;
    }

    static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = rt.offsetMax = Vector2.zero;
    }

    static void Place(RectTransform rt, Vector2 anchor, Vector2 position, Vector2 size, Vector2 pivot)
    {
        rt.anchorMin = rt.anchorMax = anchor;
        rt.pivot = pivot;
        rt.anchoredPosition = position;
        rt.sizeDelta = size;
    }

    static Text Label(string name, Transform parent, Font font, int size, TextAnchor align, Color color)
    {
        var rt = Rect(name, parent);
        var text = rt.gameObject.AddComponent<Text>();
        text.font = font;
        text.fontSize = size;
        text.alignment = align;
        text.color = color;
        text.raycastTarget = false;
        text.horizontalOverflow = HorizontalWrapMode.Overflow;
        text.verticalOverflow = VerticalWrapMode.Overflow;
        return text;
    }
}

// What the dock's power preview plays: the ship's attack (ULTIMATE) and secret
// power strips from ShipFxArt, as one fixed tick sequence each (24 fps). By
// default a strip's loop twice, then its burst; a ship whose attack needs more
// than its own strip to read (a nuke's shockwave and mushroom, the sentries'
// bolt, the frost nova's shatter, a beam that wants zooming) lists its own
// parts in AttackPlan below.
public static class PowerPreview
{
    // Room for every part of the longest plan (cells are laid end to end).
    public const int Cells = 64;
    public const float Fps = 24f;
    const int LoopCycles = 2, HoldTicks = 8;

    // A run of cells from one strip: suffix "attack" / "power" / "attack_ice" ..., cells first..first+count-1
    // played `repeat` times with the given ticks per cell.
    public struct Part
    {
        public string suffix;
        public int first, count, repeat;
        public int[] ticks;
        public Part(string suffix, int first, int count, int repeat, int[] ticks)
        {
            this.suffix = suffix; this.first = first; this.count = count; this.repeat = repeat; this.ticks = ticks;
        }
    }

    public struct Plan
    {
        public Part[] parts;
        public float maxZoom;   // how far past the base size the art may be scaled to fill the panel
    }

    static readonly int[] OneTick = { 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1 };

    static Part[] Default(string suffix, int[] loop, int[] burst)
    {
        return new[] { new Part(suffix, 0, 8, LoopCycles, loop), new Part(suffix, 8, 8, 1, burst) };
    }

    // The attack preview of each ship; ships not listed play the default (loop x2, burst).
    public static Plan AttackPlan(int ship)
    {
        var plan = new Plan { parts = Default("attack", ShipFxArt.LoopTicks, ShipFxArt.BurstTicks), maxZoom = 3f };
        string burstStrip = BurstStrip(ship);
        if (burstStrip != null)   // the loop of the ship's own strip, the burst of another (the Paranoid's rune mark)
            plan.parts = new[] { new Part("attack", 0, 8, LoopCycles, ShipFxArt.LoopTicks), new Part("attack_" + burstStrip, 8, 8, 1, ShipFxArt.BurstTicks) };
        switch (ship)
        {
            case 5:   // ion laser: a thin horizontal beam, so zoom well past the usual cap to read as a beam
                plan.maxZoom = 8f;
                break;
            case 6:   // frost nova: the ring, its shards, then the ice crust cracking
                plan.parts = new[]
                {
                    new Part("attack", 0, 8, 1, ShipFxArt.LoopTicks), new Part("attack", 8, 8, 1, ShipFxArt.BurstTicks),
                    new Part("attack_ice", 0, 8, 1, ShipFxArt.LoopTicks), new Part("attack_ice", 8, 8, 1, ShipFxArt.BurstTicks),
                };
                break;
            case 7:   // warden sentries: hover, deploy, then a bolt
                plan.parts = new[]
                {
                    new Part("attack", 0, 8, 2, ShipFxArt.LoopTicks), new Part("attack", 8, 8, 1, ShipFxArt.BurstTicks),
                    new Part("attack_shot", 0, 8, 1, ShipFxArt.LoopTicks), new Part("attack_shot", 8, 8, 1, ShipFxArt.BurstTicks),
                };
                break;
            case 16:  // nuke: the warhead, the core flash, the red shockwave, the mushroom cloud
                plan.parts = new[]
                {
                    new Part("attack", 0, 8, 2, ShipFxArt.LoopTicks), new Part("attack", 8, 8, 1, ShipFxArt.BurstTicks),
                    new Part("attack_shockwave", 0, 16, 1, OneTick), new Part("attack_mushroom", 0, 16, 1, OneTick),
                };
                break;
        }
        return plan;
    }

    static Plan PlanFor(int ship, bool secretPower)
    {
        if (!secretPower) return AttackPlan(ship);
        return new Plan { parts = Default("power", ShipFxArt.PowerLoopTicks, ShipFxArt.PowerBurstTicks), maxZoom = 3f };
    }

    // How many cells a ship's plan lays out.
    public static int CellCount(int ship, bool secretPower)
    {
        int n = 0;
        foreach (var part in PlanFor(ship, secretPower).parts) n += part.count;
        return n;
    }

    // Cell index (into Fill's array) per tick: each part's cells in order, then a blank beat (-1).
    public static int[] Sequence(int ship, bool secretPower)
    {
        var list = new System.Collections.Generic.List<int>();
        int at = 0;
        foreach (var part in PlanFor(ship, secretPower).parts)
        {
            for (int r = 0; r < part.repeat; r++)
                for (int i = 0; i < part.count; i++)
                    for (int t = 0; t < part.ticks[Mathf.Min(i, part.ticks.Length - 1)]; t++) list.Add(at + i);
            at += part.count;
        }
        for (int t = 0; t < HoldTicks; t++) list.Add(-1);
        return list.ToArray();
    }

    // Which strip the attack preview's burst comes from (null: the ship's own attack strip): the Paranoid's
    // eye loops, then its rune mark detonates.
    public static string BurstStrip(int ship) { return ship == 10 ? "mark" : null; }

    // Attacks that travel up the screen (Solar Fang's heat wave, Dove's gust): the preview lifts them through
    // the frame during the loop, so a wide thin strip reads as motion and not a smudge.
    public static bool Rises(int ship) { return ship == 3 || ship == 14; }
    public static float RiseOffset(int tick, float panelHeight)
    {
        return Mathf.Lerp(-.32f, .32f, Mathf.Clamp01(tick / (float)(LoopCycles * ShipFxArt.LoopFrames))) * panelHeight;
    }

    // The default sequence (the loop twice, the burst) of ship-agnostic callers.
    public static int[] Sequence(bool secretPower) { return Sequence(0, secretPower); }

    static Sprite Cell(int ship, bool secretPower, string suffix, int cell)
    {
        if (suffix == "attack" || suffix == "power")
        {
            bool burst = cell >= ShipFxArt.LoopFrames;
            int i = burst ? cell - ShipFxArt.LoopFrames : cell;
            if (secretPower) return burst ? ShipFxArt.PowerBurst(ship, i) : ShipFxArt.PowerLoop(ship, i);
            return burst ? ShipFxArt.AttackBurst(ship, i) : ShipFxArt.AttackLoop(ship, i);
        }
        return ShipFxArt.Extra(ship, suffix, cell);
    }

    // Fills `cells` (Cells long) with the ship's plan sprites end to end; false if any is missing.
    public static bool Fill(int ship, bool secretPower, Sprite[] cells)
    {
        bool ok = true;
        int at = 0;
        for (int i = 0; i < cells.Length; i++) cells[i] = null;
        foreach (var part in PlanFor(ship, secretPower).parts)
            for (int i = 0; i < part.count && at < cells.Length; i++)
            {
                cells[at] = Cell(ship, secretPower, part.suffix, part.first + i);
                ok &= cells[at] != null;
                at++;
            }
        return ok;
    }

    const float FitFraction = .85f, AlphaCut = .15f, BaseSide = 56f;
    static readonly System.Collections.Generic.Dictionary<int, Rect> bounds = new System.Collections.Generic.Dictionary<int, Rect>();

    // The visible (alpha) box over all the plan's cells, in 0..1 cell coordinates (y up).
    // Read through a temporary RenderTexture (the strips are not readable);
    // cached per ship and kind, so it costs once when a card opens.
    public static Rect VisibleBounds(int ship, bool secretPower, Sprite[] cells)
    {
        int key = ship * 2 + (secretPower ? 1 : 0);
        Rect box;
        if (bounds.TryGetValue(key, out box)) return box;
        box = new Rect(0f, 0f, 1f, 1f);
        float x0 = float.MaxValue, y0 = float.MaxValue, x1 = -1f, y1 = -1f;
        Texture2D lastTex = null;
        Color32[] px = null;
        int cut = (int)(AlphaCut * 255f);
        for (int c = 0; c < cells.Length && cells[c] != null; c++)
        {
            var sp = cells[c];
            var tex = sp.texture;
            if (tex == null) continue;
            if (tex != lastTex)
            {
                lastTex = tex;
                var rt = RenderTexture.GetTemporary(tex.width, tex.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
                var prev = RenderTexture.active;
                Graphics.Blit(tex, rt);
                RenderTexture.active = rt;
                var read = new Texture2D(tex.width, tex.height, TextureFormat.RGBA32, false);
                read.ReadPixels(new Rect(0, 0, tex.width, tex.height), 0, 0);
                RenderTexture.active = prev;
                RenderTexture.ReleaseTemporary(rt);
                px = read.GetPixels32();
                Object.DestroyImmediate(read);
            }
            Rect r = sp.rect;
            int rx = (int)r.x, rw = (int)r.width, rh = (int)r.height;
            for (int y = 0; y < rh; y++)
                for (int x = 0; x < rw; x++)
                    if (px[y * tex.width + rx + x].a > cut)
                    {
                        float fx = x / (float)rw, fy = y / (float)rh;
                        if (fx < x0) x0 = fx; if (fx > x1) x1 = fx;
                        if (fy < y0) y0 = fy; if (fy > y1) y1 = fy;
                    }
        }
        if (x1 >= x0 && y1 >= y0) box = Rect.MinMaxRect(x0, y0, x1 + 1f / 256f, y1 + 1f / 256f);
        bounds[key] = box;
        return box;
    }

    // Image size and offset (canvas units) that centre the strip's visible box
    // in the panel and fill ~85% of its height (up to 3x, within the panel's width).
    public static void Fit(int ship, bool secretPower, Sprite[] cells, float panelHeight, float panelWidth,
                           out Vector2 size, out Vector2 at)
    {
        Rect b = VisibleBounds(ship, secretPower, cells);
        float side = Mathf.Min(FitFraction * panelHeight / Mathf.Max(.05f, b.height),
                               FitFraction * panelWidth / Mathf.Max(.05f, b.width),
                               PlanFor(ship, secretPower).maxZoom * BaseSide);
        size = new Vector2(side, side);
        at = new Vector2(-(b.center.x - .5f) * side, -(b.center.y - .5f) * side);
    }

    public static string Name(int ship, bool secretPower)
    {
        var l = ShipLoadoutTable.For(ship);
        return secretPower ? l.powerName : l.attackName;
    }

    public static string Line(int ship, bool secretPower)
    {
        var l = ShipLoadoutTable.For(ship);
        return (secretPower ? l.powerLine : l.attackLine).TrimEnd('.');
    }
}
