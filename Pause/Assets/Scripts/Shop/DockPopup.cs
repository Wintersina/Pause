using UnityEngine;
using UnityEngine.UI;

// The small popup that floats just above the selected ship.
//
// It replaces the old full-screen yes/no dialog. It lives on its own
// world-space canvas, re-anchored to the ship every frame it is visible, and
// is clamped to the visible dock: if there is no room above the ship it
// flips below it, pointer and all.
//
//   owned ship  -> one action, LAUNCH, plus a row of hull-skin swatches:
//                  tap an owned one to equip it (saved at once), a locked one
//                  to preview it on the hull -> its price and BUY
//   locked ship -> its price and BUY; can't afford -> shake + "NEED n MORE"
public class DockPopup : MonoBehaviour
{
    // World units. The canvas is scaled so 1 canvas unit = 0.01 world units.
    public const float Width = 1.50f;
    public const float Height = .62f;
    // Extra height when the skin swatch row is shown (owned ships).
    public const float SkinRowHeight = .27f;
    public const float TailLength = .085f;
    public const float Gap = .04f;
    const float CanvasScale = .01f;
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

    // The skin row: shown for owned ships. SkinShown is the swatch outlined
    // (the skin on the hull right now).
    public bool SkinRowVisible { get; private set; }
    public int SkinShown { get; private set; }
    public float CurrentHeight { get { return SkinRowVisible ? Height + SkinRowHeight + WeaponRowHeight : Height; } }

    public class Swatch
    {
        public RectTransform root;
        public Image body, band, stripe, ink, ring, check, dust;
        public Text price;
        public Button button;
        public bool owned, equipped;
    }
    public readonly Swatch[] swatches = new Swatch[ShipSkins.PerShip];
    RectTransform skinRow;

    Canvas canvas;
    Font font;
    CanvasGroup group;
    RectTransform panel, tail, dustIcon;
    Text title, status, message, buttonLabel;
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

    void Build(Font font, Camera eventCamera)
    {
        this.font = font;
        var root = (RectTransform)transform;
        root.sizeDelta = new Vector2(Width, Height) / CanvasScale;
        root.localScale = Vector3.one * CanvasScale;
        canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.worldCamera = eventCamera;
        canvas.overrideSorting = true;
        canvas.sortingOrder = 60;
        var scaler = gameObject.AddComponent<CanvasScaler>();
        scaler.dynamicPixelsPerUnit = 5f;
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
        tail.sizeDelta = new Vector2(16f, 10f);
        var tailImage = tail.gameObject.AddComponent<Image>();
        tailImage.sprite = DockArt.Get("popup_tail");
        tailImage.raycastTarget = false;

        title = Label("Title", panel, font, 10, TextAnchor.MiddleLeft, AkiraPalette.Bone);
        title.horizontalOverflow = HorizontalWrapMode.Wrap;
        title.verticalOverflow = VerticalWrapMode.Truncate;
        title.resizeTextForBestFit = true;
        title.resizeTextMinSize = 6;
        title.resizeTextMaxSize = 10;
        Place(title.rectTransform, new Vector2(0f, 1f), new Vector2(11f, -9f), new Vector2(84f, 16f), new Vector2(0f, 1f));
        status = Label("Status", panel, font, 11, TextAnchor.MiddleRight, DockArt.Gold);
        Place(status.rectTransform, new Vector2(1f, 1f), new Vector2(-11f, -9f), new Vector2(64f, 16f), new Vector2(1f, 1f));
        dustIcon = Rect("Dust", panel);
        var dust = dustIcon.gameObject.AddComponent<Image>();
        dust.sprite = DockArt.Get("icon_dust");
        dust.raycastTarget = false;
        Place(dustIcon, new Vector2(1f, 1f), new Vector2(-60f, -10f), new Vector2(12f, 12f), new Vector2(.5f, .5f));
        livesBadge = Rect("Lives", panel);
        Place(livesBadge, new Vector2(1f, 1f), new Vector2(-80f, -17f), new Vector2(LivesIconSize + 10f, 12f), new Vector2(1f, .5f));
        var heart = Rect("Heart", livesBadge);
        Place(heart, new Vector2(0f, .5f), Vector2.zero, new Vector2(LivesIconSize, LivesIconSize), new Vector2(0f, .5f));
        var heartImage = heart.gameObject.AddComponent<Image>();
        heartImage.sprite = Resources.Load<Sprite>("Vfx/lifeHeart");
        heartImage.preserveAspect = true;
        heartImage.raycastTarget = false;
        livesLabel = Label("Count", livesBadge, font, 10, TextAnchor.MiddleLeft, AkiraPalette.Bone);
        Place(livesLabel.rectTransform, new Vector2(0f, .5f), new Vector2(LivesIconSize + 2f, 0f), new Vector2(10f, 12f), new Vector2(0f, .5f));
        message = Label("Message", panel, font, 9, TextAnchor.MiddleCenter, DockArt.Warn);
        Place(message.rectTransform, new Vector2(.5f, 1f), new Vector2(0f, -9f), new Vector2(Width / CanvasScale - 16f, 16f), new Vector2(.5f, 1f));

        var buttonRect = Rect("Action", panel);
        Place(buttonRect, new Vector2(.5f, 0f), new Vector2(0f, 8f), new Vector2(Width / CanvasScale - 18f, 26f), new Vector2(.5f, 0f));
        buttonImage = buttonRect.gameObject.AddComponent<Image>();
        buttonImage.sprite = DockArt.Get("button", 8f);
        buttonImage.type = Image.Type.Sliced;
        var button = buttonRect.gameObject.AddComponent<Button>();
        button.targetGraphic = buttonImage;
        var colors = button.colors;
        colors.pressedColor = new Color(.8f, .8f, .8f, 1f);
        colors.highlightedColor = Color.white;
        colors.selectedColor = Color.white;
        button.colors = colors;
        button.onClick.AddListener(Clicked);
        CelPress.AddTo(buttonRect.gameObject);   // cartoon squash-and-pop on press
        buttonLabel = Label("Label", buttonRect, font, 12, TextAnchor.MiddleCenter, DockArt.Ink);
        Stretch(buttonLabel.rectTransform);
        buttonLabel.fontStyle = FontStyle.Bold;

        BuildSkinRow();
        BuildWeaponRow();   // weapon level row (ShipWeaponUpgrades)
    }

    // Five compact angular chips in each skin's own colours (base, shadow
    // band, and a special's livery stripe), between the name and the action.
    const float ChipW = 22f, ChipH = 14f, ChipGap = 4f;

    void BuildSkinRow()
    {
        skinRow = Rect("Skins", panel);
        float rowW = ShipSkins.PerShip * ChipW + (ShipSkins.PerShip - 1) * ChipGap;
        Place(skinRow, new Vector2(.5f, 1f), new Vector2(0f, -29f), new Vector2(rowW, 24f), new Vector2(.5f, 1f));
        for (int n = 0; n < swatches.Length; n++)
        {
            var w = new Swatch();
            w.root = Rect("Swatch" + n, skinRow);
            Place(w.root, new Vector2(0f, 1f), new Vector2(n * (ChipW + ChipGap) + ChipW * .5f, -ChipH * .5f - 1f),
                  new Vector2(ChipW, ChipH), new Vector2(.5f, .5f));
            // The touch target is the whole slot (chip, price and the gap),
            // bigger than the chip itself.
            var hit = Chip(w.root, "Hit", null, new Vector2(ChipW + ChipGap, 26f));
            hit.rectTransform.anchoredPosition = new Vector2(0f, -4f);
            hit.color = new Color(0f, 0f, 0f, 0f);
            hit.raycastTarget = true;
            w.ring = Chip(w.root, "Ring", "swatch_ring", new Vector2(ChipW + 4f, ChipH + 4f));
            w.body = Chip(w.root, "Body", "swatch", new Vector2(ChipW, ChipH));
            w.band = Chip(w.root, "Band", "swatch_band", new Vector2(ChipW, ChipH));
            w.stripe = Chip(w.root, "Stripe", "swatch_stripe", new Vector2(ChipW, ChipH));
            w.ink = Chip(w.root, "Ink", "swatch_ink", new Vector2(ChipW, ChipH));
            w.check = Chip(w.root, "Check", "swatch_check", new Vector2(7.5f, 6.7f));
            w.check.rectTransform.anchoredPosition = new Vector2(ChipW * .5f - 3f, ChipH * .5f - 1.5f);
            w.dust = Chip(w.root, "Dust", "icon_dust", new Vector2(5.5f, 5.5f));
            w.price = Label("Price", w.root, font, 6, TextAnchor.MiddleLeft, DockArt.Gold);
            Place(w.price.rectTransform, new Vector2(.5f, .5f), new Vector2(-4f, -ChipH * .5f - 5f),
                  new Vector2(20f, 8f), new Vector2(0f, .5f));
            w.dust.rectTransform.anchoredPosition = new Vector2(-7f, -ChipH * .5f - 5f);
            w.button = w.root.gameObject.AddComponent<Button>();
            w.button.targetGraphic = hit;
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
        // The skin's name sits on its own line under the ship's.
        string skinName = shown == ShipSkins.Stock ? "" : "\n" + ShipSkins.Get(index, shown).DisplayName;
        title.text = (shopingShips.NameFor(index) ?? "").ToUpperInvariant() + skinName;
        ShowLives(index);
        if (!ShipSkins.IsOwned(index, shown))
        {
            CurrentMode = Mode.BuySkin;
            float price = ShipSkins.PriceOf(index, shown);
            bool affordable = balance >= price;
            status.text = Mathf.RoundToInt(price).ToString("N0");
            status.color = affordable ? DockArt.Gold : DockArt.Warn;
            status.fontSize = 11;
            buttonLabel.text = "BUY";
            buttonImage.color = affordable ? DockArt.Gold : AkiraPalette.GunHi;
            dustIcon.anchoredPosition = new Vector2(-11f - status.preferredWidth - 8f, -17f);
        }
        else
        {
            CurrentMode = Mode.Launch;
            status.text = shown == equipped && ShipIndex == SpaceDock.EquippedIndex() ? "EQUIPPED" : "";
            status.color = AkiraPalette.WithAlpha(AkiraPalette.Cyan, .9f);
            status.fontSize = 8;
            buttonLabel.text = "LAUNCH";
            buttonImage.color = DockArt.Cyan;
        }
        FitTitle();
        SetRowVisible(messageUntil <= 0f);
        Resize();
        Follow();
    }

    // ---- BEGIN weapon row (ShipWeaponUpgrades) -------------------------
    //
    // Under the skin chips: WEAPON, one pip per level (lit = colours owned),
    // and what one more colour adds. A previewed (unbought) skin lights the
    // pip it would add in gold and names the upgrade; at the top level the
    // row reads MAX. Lives inside the skin row, so it shows and hides with it.
    public const float WeaponRowHeight = .12f;
    RectTransform weaponRow;
    Text weaponTitle, weaponLabel;
    readonly Image[] weaponPips = new Image[ShipWeaponUpgrades.MaxLevel];
    public int WeaponLevelShown { get; private set; }
    public string WeaponLine { get { return weaponLabel != null ? weaponLabel.text : ""; } }
    public Image WeaponPip(int i) { return weaponPips[i]; }
    const float PipSize = 5f, PipGap = 2f, PipX = 35f;

    void BuildWeaponRow()
    {
        float rowW = ShipSkins.PerShip * ChipW + (ShipSkins.PerShip - 1) * ChipGap;
        weaponRow = Rect("Weapon", skinRow);
        Place(weaponRow, new Vector2(.5f, 1f), new Vector2(0f, -26f), new Vector2(rowW, 10f), new Vector2(.5f, 1f));
        weaponTitle = Label("Title", weaponRow, font, 6, TextAnchor.MiddleLeft, AkiraPalette.Muted);
        weaponTitle.text = "WEAPON";
        Place(weaponTitle.rectTransform, new Vector2(0f, .5f), Vector2.zero, new Vector2(30f, 10f), new Vector2(0f, .5f));
        for (int i = 0; i < weaponPips.Length; i++)
        {
            var rt = Rect("Pip" + i, weaponRow);
            Place(rt, new Vector2(0f, .5f), new Vector2(PipX + i * (PipSize + PipGap), 0f),
                  new Vector2(PipSize, PipSize), new Vector2(0f, .5f));
            weaponPips[i] = rt.gameObject.AddComponent<Image>();
            weaponPips[i].raycastTarget = false;
        }
        float labelX = PipX + weaponPips.Length * (PipSize + PipGap) + 2f;
        weaponLabel = Label("Next", weaponRow, font, 6, TextAnchor.MiddleRight, DockArt.Gold);
        weaponLabel.horizontalOverflow = HorizontalWrapMode.Wrap;
        weaponLabel.resizeTextForBestFit = true;
        weaponLabel.resizeTextMinSize = 4;
        weaponLabel.resizeTextMaxSize = 6;
        Place(weaponLabel.rectTransform, new Vector2(1f, .5f), Vector2.zero, new Vector2(rowW - labelX, 10f), new Vector2(1f, .5f));
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
        Resize();
    }

    static Color Dim(Color c, float k) { return new Color(c.r * k, c.g * k, c.b * k, 1f); }

    void Resize()
    {
        ((RectTransform)transform).sizeDelta = new Vector2(Width, CurrentHeight) / CanvasScale;
    }

    const float LivesIconSize = 9f;

    // Hearts the ship shown flies with (ShipLives.Max: the starter's goes
    // 2 -> 3 once it owns a colour).
    public int LivesShown { get; private set; }
    public bool LivesBadgeVisible { get { return livesBadge != null && livesBadge.gameObject.activeSelf; } }

    void ShowLives(int index)
    {
        LivesShown = ShipLives.Max(index);
        livesLabel.text = LivesShown.ToString();
    }

    void FitTitle()
    {
        float statusWidth = status.text.Length > 0 ? status.preferredWidth + (CurrentMode != Mode.Launch ? 16f : 0f) + 6f : 0f;
        float livesWidth = LivesIconSize + 2f + livesLabel.preferredWidth;
        livesBadge.sizeDelta = new Vector2(livesWidth, 12f);
        livesBadge.anchoredPosition = new Vector2(-11f - statusWidth, -17f);
        title.rectTransform.sizeDelta = new Vector2(Width / CanvasScale - 22f - statusWidth - livesWidth - 6f, 16f);
    }

    public void Show(int index, Transform ship, float halfHeight, bool owned, bool equipped,
                     float price, float balance)
    {
        bool wasVisible = Visible && ShipIndex == index;
        ShipIndex = index;
        target = ship;
        above = halfHeight + Gap;
        below = halfHeight + .34f;        // clear the engine plume
        gameObject.SetActive(true);
        title.text = (shopingShips.NameFor(index) ?? "").ToUpperInvariant();
        ShowLives(index);
        messageUntil = 0f;
        if (owned)
        {
            CurrentMode = Mode.Launch;
            // LAUNCH already says it's yours; only call out the equipped one.
            status.text = equipped ? "EQUIPPED" : "";
            status.color = AkiraPalette.WithAlpha(AkiraPalette.Cyan, .9f);
            status.fontSize = 8;
            dustIcon.gameObject.SetActive(false);
            buttonLabel.text = "LAUNCH";
            buttonImage.color = DockArt.Cyan;
        }
        else
        {
            CurrentMode = Mode.Buy;
            bool affordable = balance >= price;
            status.text = Mathf.RoundToInt(price).ToString("N0");
            status.color = affordable ? DockArt.Gold : DockArt.Warn;
            status.fontSize = 11;
            dustIcon.gameObject.SetActive(true);
            dustIcon.anchoredPosition = new Vector2(-11f - status.preferredWidth - 8f, -17f);
            buttonLabel.text = "BUY";
            buttonImage.color = affordable ? DockArt.Gold : AkiraPalette.GunHi;
        }
        // The name gets whatever width the status leaves, shrinking to fit
        // long names rather than running into the price.
        FitTitle();
        HideSkins();
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
        float s = Mathf.LerpUnclamped(.82f, 1f, DockTween.OutBack(e)) * CanvasScale;
        transform.localScale = new Vector3(s, s, 1f);
        float shake = now < shakeUntil ? Mathf.Sin(now * 70f) * 5f * ((shakeUntil - now) / .35f) : 0f;
        panel.anchoredPosition = new Vector2(shake, 0f);
        Follow();
    }

    void Follow()
    {
        if (target == null) return;
        Vector2 ship = target.position;
        bool flipped;
        Vector2 center = Place(ship, above, below, new Vector2(Width, CurrentHeight), safeView, out flipped);
        Flipped = flipped;
        transform.position = new Vector3(center.x, center.y, -1f);
        float tailX = Mathf.Clamp(ship.x - center.x, -Width * .5f + .14f, Width * .5f - .14f) / CanvasScale;
        float edge = CurrentHeight * .5f / CanvasScale;
        tail.anchoredPosition = new Vector2(tailX, flipped ? edge + 3.5f : -edge - 3.5f);
        tail.localRotation = flipped ? Quaternion.Euler(0f, 0f, 180f) : Quaternion.identity;
    }

    // Pure placement: centre of a popup of `size` floating above a ship at
    // `ship`, clamped inside `view`. Falls back below the ship when there is
    // no room above.
    public static Vector2 Place(Vector2 ship, float above, float below, Vector2 size, Rect view, out bool flipped)
    {
        float halfW = size.x * .5f, halfH = size.y * .5f;
        float x = view.width >= size.x
            ? Mathf.Clamp(ship.x, view.xMin + halfW, view.xMax - halfW)
            : view.center.x;
        float y = ship.y + above + TailLength + halfH;
        flipped = false;
        if (y + halfH > view.yMax)
        {
            float belowY = ship.y - below - TailLength - halfH;
            if (belowY - halfH >= view.yMin)
            {
                y = belowY;
                flipped = true;
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
            return new Rect(c.x - Width * .5f, c.y - CurrentHeight * .5f, Width, CurrentHeight);
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
