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
    public const float BaseHeight = Pad + HeaderHeight + 10f + ButtonHeight + Pad;
    // Added for an owned ship: the swatches (their touch slots), the weapon
    // row and the START SPEED line.
    public const float SkinRowHeight = 50f;
    public const float WeaponRowHeight = 20f;
    public const float StartSpeedLineHeight = 18f;
    public const float OwnedHeight = BaseHeight + SkinRowHeight + WeaponRowHeight + StartSpeedLineHeight;
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
    public const int TitleSize = 17, TitleMinSize = 13, StatusSize = 13, PriceSize = 17, SmallSize = 12, ButtonSize = 18;

    // World units per canvas unit before the dock has measured the screen
    // (tests without a camera): about a dp on a 1080-wide phone.
    public const float DefaultUnit = .0132f;
    // World units between the ship's hull and the tail's tip.
    public const float Gap = .04f;
    // A popup flipped below its ship hangs this far under the hull, so the
    // tail's tip clears the engine plume. Ship geometry: not scaled.
    public const float PlumeClearance = .34f;
    const float AppearTime = .2f;

    public enum Mode { Launch, Buy, BuySkin }

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
    public float CurrentHeightUnits { get { return SkinRowVisible ? OwnedHeight : BaseHeight; } }
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
    RectTransform panel, tail, dustIcon;
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
        if (safeView.height > 0f) u = Mathf.Min(u, safeView.height / (OwnedHeight + TailUnits));
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

        // Line 1: the ship's name, and the close button in the corner.
        title = Label("Title", panel, font, TitleSize, TextAnchor.MiddleLeft, AkiraPalette.Bone);
        title.fontStyle = FontStyle.Bold;
        title.horizontalOverflow = HorizontalWrapMode.Wrap;
        title.verticalOverflow = VerticalWrapMode.Truncate;
        title.resizeTextForBestFit = true;
        title.resizeTextMinSize = TitleMinSize;
        title.resizeTextMaxSize = TitleSize;
        Place(title.rectTransform, new Vector2(0f, 1f), new Vector2(Pad, -Pad), new Vector2(PanelWidth - 2f * Pad - 36f, 24f), new Vector2(0f, 1f));
        BuildClose();

        // Line 2: the colour's name on the left; hearts and status / price on the right.
        skinName = Label("SkinName", panel, font, SmallSize + 1, TextAnchor.MiddleLeft, AkiraPalette.WithAlpha(AkiraPalette.Cyan, .9f));
        skinName.horizontalOverflow = HorizontalWrapMode.Wrap;
        skinName.verticalOverflow = VerticalWrapMode.Truncate;
        skinName.resizeTextForBestFit = true;
        skinName.resizeTextMinSize = SmallSize;
        skinName.resizeTextMaxSize = SmallSize + 1;
        Place(skinName.rectTransform, new Vector2(0f, 1f), new Vector2(Pad, -Pad - 24f), new Vector2(150f, 20f), new Vector2(0f, 1f));
        status = Label("Status", panel, font, StatusSize, TextAnchor.MiddleRight, DockArt.Gold);
        Place(status.rectTransform, new Vector2(1f, 1f), new Vector2(-Pad, -Pad - 24f), new Vector2(120f, 20f), new Vector2(1f, 1f));
        dustIcon = Rect("Dust", panel);
        var dust = dustIcon.gameObject.AddComponent<Image>();
        dust.sprite = DockArt.Get("icon_dust");
        dust.raycastTarget = false;
        Place(dustIcon, new Vector2(1f, 1f), new Vector2(-80f, -Pad - 34f), new Vector2(15f, 15f), new Vector2(.5f, .5f));
        livesBadge = Rect("Lives", panel);
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
        message = Label("Message", panel, font, 15, TextAnchor.MiddleCenter, DockArt.Warn);
        message.fontStyle = FontStyle.Bold;
        Place(message.rectTransform, new Vector2(.5f, 1f), new Vector2(0f, -Pad), new Vector2(PanelWidth - 2f * Pad, HeaderHeight), new Vector2(.5f, 1f));

        var buttonRect = Rect("Action", panel);
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

    // The card's corner X: closes it (as tapping anywhere off it or Back does).
    void BuildClose()
    {
        var rt = Rect("Close", panel);
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
        startSpeed = Label("StartSpeed", panel, font, SmallSize, TextAnchor.MiddleLeft,
                           AkiraPalette.WithAlpha(AkiraPalette.Cyan, .9f));
        Place(startSpeed.rectTransform, new Vector2(0f, 1f), new Vector2(Pad, y),
              new Vector2(StartSpeedWidth, StartSpeedLineHeight - 2f), new Vector2(0f, 1f));
        startSpeed.gameObject.SetActive(false);

        heartsLine = Label("Hearts", panel, font, SmallSize, TextAnchor.MiddleRight,
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
        skinRow = Rect("Skins", panel);
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
            w.dust.rectTransform.anchoredPosition = new Vector2(-12f, priceY);
            w.price = Label("Price", w.root, font, SmallSize, TextAnchor.MiddleLeft, DockArt.Gold);
            Place(w.price.rectTransform, new Vector2(.5f, .5f), new Vector2(-6f, priceY),
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
        if (!Visible || !SkinRowVisible || CurrentMode == Mode.Buy) return;
        if (onSkin != null) onSkin(ShipIndex, skin);
    }

    // Taps a swatch as a player would (tests, previews).
    public void TapSwatch(int skin) { SwatchClicked(skin); }

    // Fills the skin row for owned ship `index`: `shown` is the skin on the
    // hull. A shown skin that isn't owned turns the action into BUY.
    public void ShowSkins(int index, int shown, float balance)
    {
        if (index != ShipIndex || CurrentMode == Mode.Buy) return;
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
                w.price.text = Mathf.RoundToInt(price).ToString("N0");
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
        bool price = CurrentMode != Mode.Launch;
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
        else
        {
            CurrentMode = Mode.Buy;
            bool affordable = balance >= price;
            ShowPrice(price, affordable);
            dustIcon.gameObject.SetActive(true);
            buttonLabel.text = "BUY";
            buttonImage.color = affordable ? DockArt.Gold : AkiraPalette.GunHi;
        }
        HideSkins();
        FitHeader();
        SetRowVisible(true);
        if (!wasVisible) shownAt = Time.unscaledTime;
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
        dustIcon.gameObject.SetActive(row && CurrentMode != Mode.Launch);
        message.enabled = !row;
    }

    void LateUpdate()
    {
        if (target == null) { Hide(); return; }
        float now = Time.unscaledTime;
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
