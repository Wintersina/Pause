using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// The ship-select space dock (shopS6).
//
// Every ship is parked in its own bay of the SPACE PARKING LOT (DockLayout): bays backed in against service walls,
// facing a drive lane. Phones get horizontal lanes with rows of bays facing each other across them; wide and tablet
// views get vertical lanes with a bay column on each side and the ships' noses pointing sideways into the lane. The
// lot scrolls vertically behind opaque header and footer bands, so it never runs under the star dust read-out, the
// start-world picker or the Back / Lift-off buttons. Parked ships are powered down. Tapping one powers it up and floats its card above (or
// below) it (LAUNCH if owned, price + BUY if not); its X, tapping anywhere
// else or Back dismisses it. An owned ship's popup also carries its hull-skin swatches: an owned
// skin equips on tap (saved at once); a locked one previews on the hull with
// its price and BUY, and Back (or leaving the ship) puts the equipped skin
// back. Launching undocks the ship -- it lifts and pulls straight out of its bay
// into the lane (forward = its nose), turns nose-up and accelerates up the lane and out of view with its engine flaring
// -- and the game continues to the same scene the PLAY button always led to.
//
// Berths are laid out cheapest first (BayOrder: price ascending, ties by
// ShipId), so the starter leads the rack. That is display order only: bays[]
// and everything else stay indexed by ShipId. Ships not bought yet park as
// ink silhouettes (DockBay) and are revealed when bought.
//
// Built entirely from code by ShopSceneExtender when shopS6 loads.
public class SpaceDock : MonoBehaviour
{
    public static SpaceDock Instance { get; private set; }

    public const float LaunchDuration = DockLaunch.Duration;
    const float UndockTime = DockLaunch.UndockTime;
    const float DragThresholdInches = .08f;

    public DockBay[] bays;            // by ship index (ShipId); [0] unused
    public DockPopup popup;
    public Transform rack;
    public DockLayout layout;

    public int Selected { get; private set; }
    public bool Launching { get; private set; }

    Camera cam;
    shopingShips shop;
    SpriteRenderer edgeLeft, edgeRight, headerBand, footerBand;
    readonly List<SpriteRenderer> laneTiles = new List<SpriteRenderer>();
    readonly List<SpriteRenderer> dividers = new List<SpriteRenderer>();
    // the "more below" hint over the lot's bottom edge (ScrollHint's rule, drawn in world space)
    SpriteRenderer hintFade, hintChevron;
    TextMesh hintLabel;
    bool hintShown;
    float hintAlpha;
    readonly List<RectTransform> header = new List<RectTransform>();
    readonly List<RectTransform> footer = new List<RectTransform>();
    readonly Vector3[] corners = new Vector3[4];

    int lastScreenW, lastScreenH;
    float lastOrtho;
    bool needsLayout = true;
    float rackY, rackYMin, rackYMax, scrollVelocity;
    bool pressing, dragging;
    Vector2 pressAt;
    float lastPointerY;
    bool skipLaunch;
    // shopingShips' selection statics as they were before the current
    // selection began, so Back can put them back.
    int preSelectShipNumber, preSelectLastShip;

    // Where launching continues to: the same destination the PLAY button has
    // always had (menuButton.play): the game, or the tutorial first.
    public static string Destination
    {
        get { return PlayerPrefs.GetString("HasDoneTut") == "true" ? "gameS1" : "tutorialS5"; }
    }

    public static SpaceDock Build()
    {
        var existing = Object.FindFirstObjectByType<SpaceDock>();
        if (existing != null) return existing;
        var dock = new GameObject("~SpaceDock").AddComponent<SpaceDock>();
        dock.Construct();
        return dock;
    }

    void Awake() { Instance = this; }

    void OnEnable()
    {
        Instance = this;
        DeveloperUnlocks.Changed -= RefreshStatuses;
        DeveloperUnlocks.Changed += RefreshStatuses;
        ParagonPurchase.Changed -= RefreshStatuses;
        ParagonPurchase.Changed += RefreshStatuses;
        SupportPurchase.Rewarded -= OnSupportRewarded;
        SupportPurchase.Rewarded += OnSupportRewarded;
        BackNavigator.Register(this, OnBackPressed);
    }

    void OnDisable()
    {
        DeveloperUnlocks.Changed -= RefreshStatuses;
        ParagonPurchase.Changed -= RefreshStatuses;
        SupportPurchase.Rewarded -= OnSupportRewarded;
        BackNavigator.Unregister(this);
        if (Instance == this) Instance = null;
    }

    void OnDestroy()
    {
        DeveloperUnlocks.Changed -= RefreshStatuses;
        ParagonPurchase.Changed -= RefreshStatuses;
        SupportPurchase.Rewarded -= OnSupportRewarded;
        BackNavigator.Unregister(this);
        if (Instance == this) Instance = null;
    }

    // A pack paid while the dock is open (a pending payment completing) refreshes the dust read-out.
    void OnSupportRewarded(int amount)
    {
        if (shop != null) shop.ShowPurchase(amount);
    }

    // BackNavigator layer. While launching, Back is swallowed (the choice is
    // already saved and the scene is about to change; skipping would be a
    // second, irreversible action). With a skin being previewed it puts the
    // equipped skin back; then, with a ship selected, it undoes the
    // selection. Otherwise it passes, and the scene root goes home.
    bool OnBackPressed()
    {
        if (Launching) return true;
        if (Selected > 0 && ShipSkins.IsPreviewing(Selected)) { RevertSkinPreview(); return true; }
        if (HasSelection) { UndoSelection(); return true; }
        return false;
    }

    // ------------------------------------------------------------- skins

    public bool PreviewingSkin { get { return Selected > 0 && ShipSkins.IsPreviewing(Selected); } }

    // The popup's line after the last skin bought ("ACQUIRED  +1 HEART"): tests.
    public string LastPurchaseMessage { get; private set; }

    // A swatch was tapped in the popup of owned ship `index`.
    public void TapSkin(int index, int skin)
    {
        if (Launching || index != Selected || !IsOwned(index) || !ShipSkins.Has(index, skin)) return;
        if (ShipSkins.IsOwned(index, skin))
        {
            ShipSkins.Equip(index, skin);   // saved at once; ends any preview
            if (shop != null) shop.RefreshStarDust();
        }
        else ShipSkins.SetPreview(index, skin);
        ShowBaySkin(index);
        ShowPopup(index);
    }

    // BUY on a previewed skin: deduct once, own, equip, save at once.
    public void BuySkin(int index, int skin)
    {
        if (Launching || index < 1 || index >= bays.Length) return;
        // What the colour adds to the hearts (SkinHearts), read before it's owned.
        bool completesSet;
        int gain = SkinHearts.GainIfBought(index, skin, out completesSet);
        var result = ShipSkins.TryPurchase(index, skin);
        if (result == ShipSkins.PurchaseResult.Bought)
        {
            if (shop != null) shop.RefreshStarDust();
            ShowBaySkin(index);
            ShowPopup(index);
            LastPurchaseMessage = SkinHearts.PurchaseMessage(gain);
            popup.ShowMessage(LastPurchaseMessage, DockArt.Cyan, gain > 0 ? 1.6f : 1.2f);
            // The last skin of the set: every ship gets its +2, said once.
            if (completesSet) CodexToast.Announce(SkinHearts.AllSkinsHeading, SkinHearts.AllSkinsTitle,
                                                  Resources.Load<Sprite>("Vfx/lifeHeart"));
        }
        else if (result == ShipSkins.PurchaseResult.CantAfford)
        {
            popup.ShowCantAfford(ShipSkins.ShortBy(index, skin));
        }
        else ShowPopup(index);
    }

    // Leaves a preview without buying: the hull goes back to its equipped skin.
    public void RevertSkinPreview()
    {
        int ship = ShipSkins.PreviewShip;
        ShipSkins.ClearPreview();
        if (ship > 0) ShowBaySkin(ship);
        if (popup != null && popup.Visible && !Launching) ShowPopup(popup.ShipIndex);
    }

    void ShowBaySkin(int index)
    {
        if (bays != null && index > 0 && index < bays.Length && bays[index] != null)
            bays[index].ShowSkin(ShipSkins.Shown(index));
        // Only the equipped skins and the one being previewed stay decoded.
        ShipHullArt.ReleaseUnusedSkins();
    }

    public bool HasSelection
    {
        get { return Selected > 0 || (popup != null && popup.Visible); }
    }

    // Back with a ship tapped: close the popup, power the ship down and show
    // the equipped ship as the selection again. Nothing is saved.
    public void UndoSelection()
    {
        if (Launching) return;
        bool had = Selected > 0;
        Deselect();
        if (had)
        {
            shopingShips.shipNumber = preSelectShipNumber;
            shopingShips.LastShipSelected = preSelectLastShip;
        }
        RefreshStatuses();
    }

    void Construct()
    {
        Instance = this;
        DeveloperUnlocks.Changed -= RefreshStatuses;
        DeveloperUnlocks.Changed += RefreshStatuses;
        ParagonPurchase.Changed -= RefreshStatuses;
        ParagonPurchase.Changed += RefreshStatuses;
        SupportPurchase.Rewarded -= OnSupportRewarded;
        SupportPurchase.Rewarded += OnSupportRewarded;
        BackNavigator.Register(this, OnBackPressed);
        cam = Camera.main;
        // Beyond the starfield (wide windows) show deep space, not the
        // scene's authored mid-blue clear colour.
        if (cam != null) cam.backgroundColor = AkiraPalette.Night0;
        shop = Object.FindFirstObjectByType<shopingShips>();
        var font = DockArt.FindSceneFont();

        rack = new GameObject("~DockRack").transform;
        rack.SetParent(transform, false);

        edgeLeft = Piece("EdgeLeft", DockArt.Lot("lot_edge_left"), SpriteDrawMode.Tiled, 1);
        edgeRight = Piece("EdgeRight", DockArt.Lot("lot_edge_right"), SpriteDrawMode.Tiled, 1);
        headerBand = Band("HeaderBand");
        footerBand = Band("FooterBand");
        hintFade = Band("HintBand");   // solid: nothing shows behind the hint
        hintFade.sortingOrder = BandOrder;
        hintChevron = Band("HintChevron");
        hintChevron.sprite = DockArt.HintChevron;
        hintChevron.sortingOrder = BandOrder + 1;
        hintChevron.color = DockArt.Cyan;
        var labelGo = new GameObject("HintLabel");
        labelGo.transform.SetParent(transform, false);
        hintLabel = labelGo.AddComponent<TextMesh>();
        hintLabel.font = font;
        hintLabel.fontSize = 64;
        hintLabel.characterSize = .15f * 10f / 64f;
        hintLabel.anchor = TextAnchor.MiddleCenter;
        hintLabel.alignment = TextAlignment.Center;
        hintLabel.text = HintText;
        hintLabel.color = DockArt.Cyan;
        var hintRenderer = labelGo.GetComponent<MeshRenderer>();
        if (font != null) hintRenderer.sharedMaterial = font.material;
        hintRenderer.sortingOrder = BandOrder + 2;

        int total = shopingShips.shipTotal;
        if (shopingShips.ships == null || shopingShips.ships.Length < total)
            shopingShips.ships = new GameObject[total];
        bays = new DockBay[total];
        // Built in berth order (cheapest first) so the hierarchy reads like
        // the rack; still stored by ShipId.
        foreach (int i in BayOrder)
        {
            bays[i] = DockBay.Create(rack, i, font);
            shopingShips.ships[i] = bays[i].ship.gameObject;
        }

        popup = DockPopup.Create(transform, font, cam);
        popup.onLaunch = Launch;
        popup.onBuy = Buy;
        popup.onShop = GoToShop;
        popup.onSkin = TapSkin;
        popup.onBuySkin = BuySkin;
        popup.onClose = UndoSelection;
        popup.onNudge = Nudge;

        FindChrome();
        RefreshStatuses();
        Relayout();
    }

    const int BandOrder = 30;    // above the lot and its ships, below a launching ship (DockBay.OrderLaunch) and the card
    public const string HintText = "MORE BELOW";

    // A solid / sprite piece under the dock root (not the scrolling lot).
    SpriteRenderer Band(string name)
    {
        var go = new GameObject(name);
        go.transform.SetParent(transform, false);
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = DockArt.Solid;
        sr.sortingOrder = BandOrder;
        sr.color = AkiraPalette.Night0;
        return sr;
    }

    SpriteRenderer Piece(string name, Sprite sprite, SpriteDrawMode mode, int order)
    {
        var go = new GameObject(name);
        go.transform.SetParent(rack, false);
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = sprite;
        sr.drawMode = mode;
        if (mode == SpriteDrawMode.Tiled) sr.tileMode = SpriteTileMode.Continuous;
        sr.sortingOrder = order;
        return sr;
    }

    void FindChrome()
    {
        header.Clear();
        footer.Clear();
        foreach (string name in new[] { "starDustText", "~DockInstruction" })
        {
            var go = SceneUtil.FindAny(name);
            if (go != null && go.transform is RectTransform) header.Add((RectTransform)go.transform);
        }
        var startWorld = SceneUtil.FindAny(DockStartWorld.Name);
        var startRow = startWorld != null ? startWorld.GetComponent<DockStartWorld>() : null;
        if (startRow != null && startRow.Row != null) header.Add((RectTransform)startRow.Row.transform);
        foreach (string name in new[] { "PlayButton", "BackButton" })
        {
            var go = SceneUtil.FindAny(name);
            if (go != null && go.transform is RectTransform) footer.Add((RectTransform)go.transform);
        }
    }

    // ---------------------------------------------------------------- layout

    public void Relayout()
    {
        if (cam == null) cam = Camera.main;
        if (cam == null) return;
        needsLayout = false;
        lastScreenW = ScreenInfo.Width;
        lastScreenH = ScreenInfo.Height;
        lastOrtho = cam.orthographicSize;

        float halfH = cam.orthographicSize;
        float halfW = halfH * cam.aspect;
        Vector3 camPos = cam.transform.position;
        // The 16 roster ships fill the lot; the lone Paragon bay is the last one (centred in its row on a phone).
        int count = shopingShips.shipTotal - 1;

        // The header and footer follow the safe area (notch / Dynamic Island / punch-hole above, home indicator / nav
        // bar below) before the lot is fitted between them.
        PlaceChrome();

        // The lot fills the space between the HUD header (star dust, instruction, start-world picker) and the footer
        // buttons, and scrolls inside it. Opaque bands cover everything outside, so a bay never rides under a button.
        float viewTop = camPos.y + halfH, viewBottom = camPos.y - halfH;
        Canvas.ForceUpdateCanvases();
        float headerBottom = viewTop, footerTop = viewBottom, headerArt = viewTop;
        foreach (var rt in header)
            if (rt != null && rt.gameObject.activeInHierarchy)
            {
                headerArt = Mathf.Min(headerArt, ScreenToWorldY(rt, 0));
                headerBottom = Mathf.Min(headerBottom, ScreenToWorldY(rt, 0), HitEdgeY(rt, false));
            }
        // the footer buttons' tap areas reach FooterTapPad above their art: the lot stays clear of those too
        foreach (var rt in footer)
            if (rt != null && rt.gameObject.activeInHierarchy) footerTop = Mathf.Max(footerTop, HitEdgeY(rt, true));
        HeaderBottomY = headerBottom;
        FooterTopY = footerTop;
        // the lot stays clear of the header's tap areas (the start-world picker's reach past its art); the ship card, a
        // modal above them, only keeps clear of the art
        float top = headerBottom - .02f, bottom = footerTop + .06f, cardTop = headerArt - .06f;
        if (top - bottom < 2f) { top = cardTop = viewTop - .1f; bottom = viewBottom + .1f; }
        float hintBottom = bottom;     // (the solid hint band, when the lot scrolls, takes the next HintBand above it)

        // Sideways the popup keeps inside the safe area too (waterfall edges).
        float worldPerPixel = halfH * 2f / Mathf.Max(1, ScreenInfo.Height);
        float left = camPos.x - halfW + ScreenInfo.SafeLeftInset * worldPerPixel + .05f;
        float right = camPos.x + halfW - ScreenInfo.SafeRightInset * worldPerPixel - .05f;
        // The card is sized in points / dp (UiScale): world units per point.
        float worldPerPoint = UiScale.PxPerPoint * worldPerPixel;

        // A near-square view: the lot on the left, the selected ship's card docked on the right.
        wide = DockLayout.IsWide(halfW, halfH);
        // Wide / Ultra (a desktop window): the card is fit to a pane of PaneShare of the width, not phone-sized.
        bool fit = wide && ScreenFrame.Classify(halfW, halfH) >= ScreenFrame.Aspect.Wide;
        Rect view = new Rect(left, bottom, right - left, cardTop - bottom), rackPane = view, cardPane = view;
        if (wide)
        {
            // the same gutter at both screen edges, a gap between the panes
            view = new Rect(left + DockLayout.Gutter, bottom, right - left - 2f * DockLayout.Gutter, cardTop - bottom);
            float cardWidth = fit ? view.width * DockPopup.PaneShare - DockLayout.PaneGap : worldPerPoint * DockPopup.PopupScale * DockPopup.PanelWidth;
            DockLayout.Panes(view, cardWidth, out rackPane, out cardPane);
        }
        // The lot is fitted to the width it keeps while a card is docked beside it (its own pane), so selecting a ship
        // only slides it aside and never puts a lane under the card.
        // A bay is a finger-sized tap target: where the 75% bay would be under 48 dp / pt (a dense cover screen), the
        // whole lot is drawn larger (and fitted to the width that leaves).
        float tapWorld = 48f * (fit ? UiScale.UnitPx * worldPerPixel : worldPerPoint);
        float needScale = tapWorld / DockLayout.BayW;
        float lotWidth = wide ? rackPane.width : right - left;
        layout = DockLayout.For(count, lotWidth, cardTop - bottom - HintBand, needScale);
        if (wide && layout.scale < needScale - .001f)
        {
            // a dense pane (a foldable cover): the lot may take the outer gutter too, so its bays stay finger-sized
            rackPane.xMin = Mathf.Max(left, rackPane.xMin - DockLayout.Gutter);
            layout = DockLayout.For(count, rackPane.width, cardTop - bottom - HintBand, needScale);
        }
        camX = camPos.x;
        browseOffset = (wide ? view.center.x : (left + right) * .5f) - camPos.x;
        this.rackPane = rackPane;
        spaceTop = top;
        spaceBottom = bottom;
        spaceLeft = left;
        spaceRight = right;

        hintFloor = hintBottom;
        // A lot taller than its space gets a solid band at its foot for the MORE BELOW hint, so no bay ever sits
        // behind the chevron: the scroll viewport ends above it.
        if (layout.Height * layout.scale > spaceTop - spaceBottom + .001f && spaceTop - spaceBottom - HintBand > 2f)
        {
            spaceBottom += HintBand;
            // cut the viewport at a whole bay row, so the scroll never ends on a half-bay
            float rowH = DockLayout.BayW * layout.scale;
            int whole = Mathf.FloorToInt((spaceTop - spaceBottom) / rowH + .001f);
            if (whole >= 2 && whole < layout.rows) spaceBottom = spaceTop - whole * rowH;
            hintFloor = Mathf.Max(hintFloor, spaceBottom - HintBand);   // the hint hugs the viewport, not the footer
        }
        BuildLot();
        ComputeSelectOffset();

        ApplyRackScale();
        nudge = 0f;
        PlaceRack();
        PlaceBands(camPos, halfW, halfH);

        if (wide)
        {
            cardPane.xMin += DockLayout.PaneGap;
            if (fit)
            {
                // the card's pane spans the lot's own vertical extent
                float rackH = Mathf.Min(layout.Height * layout.scale, top - bottom);
                float centerY = layout.Height * layout.scale <= top - bottom ? (top + bottom) * .5f : top - rackH * .5f;
                float paneTop = centerY + rackH * .5f, paneBottom = centerY - rackH * .5f;
                // ...but clear of the header buttons' tap areas, which reach past their art (the footer's PLAY reach
                // stays under the card: a modal card sits over it by design)
                foreach (var rt in header) if (rt != null && rt.gameObject.activeInHierarchy) paneTop = Mathf.Min(paneTop, HitEdgeY(rt, false) - 16f * worldPerPixel);
                cardPane.yMin = paneBottom;
                cardPane.height = Mathf.Max(.5f, paneTop - paneBottom);
            }
            popup.safeView = cardPane;
        }
        else popup.safeView = new Rect(left, bottom, right - left, cardTop - bottom);
        popup.Docked = wide;
        popup.FitPane = fit;
        popup.SetDensity(fit ? UiScale.UnitPx * worldPerPixel : worldPerPoint);
    }

    // Lays every lot piece out for `layout`: bays, runway tiles, seams, edge rails. Pieces are pooled (built
    // on the first layout that needs them), so a resize allocates nothing new once the lot has been seen.
    void BuildLot()
    {
        for (int i = 1; i < bays.Length; i++)
        {
            if (bays[i] == null) continue;
            int slot = SlotOf(i);
            bays[i].transform.localPosition = layout.BayCenter(slot);
            bays[i].SetFacing(layout.FacingOf(slot));
            bays[i].SetNumber(slot + 1);
            bays[i].SetLabelScale(1f);
        }

        float w = layout.Width, h = layout.Height;
        float railX = (w - DockLayout.Rail) * .5f;
        edgeLeft.size = edgeRight.size = new Vector2(DockLayout.Rail, h);
        edgeLeft.transform.localPosition = new Vector3(-railX, 0f, 0f);
        edgeRight.transform.localPosition = new Vector3(railX, 0f, 0f);

        // vertical runways (a dashed centre line, no arrows)
        for (int l = 0; l < Mathf.Max(layout.lanes, laneTiles.Count); l++)
        {
            if (l >= laneTiles.Count) laneTiles.Add(Piece("Lane" + l, null, SpriteDrawMode.Tiled, 0));
            var tile = laneTiles[l];
            tile.gameObject.SetActive(l < layout.lanes);
            if (l >= layout.lanes) continue;
            tile.sprite = DockArt.Lot("lane_tile_v");
            tile.size = new Vector2(DockLayout.Lane, h);
            tile.transform.localPosition = new Vector3(layout.LaneCenter(l), 0f, 0f);
        }
        // thin painted seams between neighbouring bays
        int seamCount = 0;
        for (int slot = 0; slot < layout.count; slot++)
        {
            if (layout.RowIndex(slot) == 0) continue;
            if (seamCount >= dividers.Count) dividers.Add(Piece("Seam" + seamCount, null, SpriteDrawMode.Simple, 3));
            var s = dividers[seamCount++];
            s.gameObject.SetActive(true);
            Vector2 c = layout.BayCenter(slot);
            s.sprite = DockArt.Lot("bay_divider_h");
            s.transform.localPosition = new Vector3(c.x, c.y + DockLayout.BayW * .5f, 0f);
            s.color = new Color(1f, 1f, 1f, .7f);
        }
        for (int s = seamCount; s < dividers.Count; s++) dividers[s].gameObject.SetActive(false);
    }

    // Opaque bands over everything outside the scroll viewport, and the hint at its bottom edge.
    void PlaceBands(Vector3 camPos, float halfW, float halfH)
    {
        float span = halfW * 2f + 4f;
        float viewTop = camPos.y + halfH, viewBottom = camPos.y - halfH;
        SetBand(headerBand, camPos.x, spaceTop, viewTop + 2f, span);
        SetBand(footerBand, camPos.x, viewBottom - 2f, spaceBottom, span);
        bool band = spaceBottom - hintFloor > .001f;
        hintFade.gameObject.SetActive(band);
        SetBand(hintFade, camPos.x, hintFloor, spaceBottom, span);
        hintChevronBase = hintFloor + .3f;
        hintChevron.transform.localScale = Vector3.one * 3f;
        hintLabel.transform.position = new Vector3(camPos.x + browseOffset, hintFloor + .62f, 0f);
        TickHint(10f);
    }

    static void SetBand(SpriteRenderer band, float cx, float yMin, float yMax, float width)
    {
        band.transform.position = new Vector3(cx, (yMin + yMax) * .5f, 0f);
        band.transform.localScale = new Vector3(width, Mathf.Max(.0001f, yMax - yMin), 1f);
    }

    // ----------------------------------------------------------------- scroll hint

    float hintChevronBase, hintFloor;
    public const float HintBand = .95f;   // world height of the solid band the hint sits on
    float pageFrom, pageTo, pageT = -1f;
    const float HintUnits = 100f;   // ScrollHint's thresholds are canvas units; one world unit is about this many
    public bool HintShown { get { return hintShown; } }
    public float HintAlpha { get { return hintAlpha; } }
    // Tappable strip of the hint (world), for tests and Tap.
    public Rect HintRect
    {
        get { return new Rect(camX + browseOffset - .9f, hintFloor, 1.8f, spaceBottom - hintFloor); }
    }

    // ScrollHint's rule: shown while content hides below, gone at the end of the lot or when it all fits.
    void TickHint(float dt)
    {
        float overflow = Mathf.Max(0f, rackYMax - rackYMin), fromBottom = Mathf.Max(0f, rackYMax - rackY);
        hintShown = ScrollHint.ShouldShow(overflow * HintUnits, fromBottom * HintUnits, hintShown);
        hintAlpha = Mathf.MoveTowards(hintAlpha, hintShown ? 1f : 0f, ScrollHint.FadeSpeed * Mathf.Max(0f, dt));
        var accent = DockArt.Cyan; accent.a = hintAlpha;
        hintChevron.color = accent;
        hintLabel.color = accent;
        float bob = Mathf.Sin(Time.unscaledTime * ScrollHint.BobSpeed) * .04f;
        var cp = hintChevron.transform.position;
        hintChevron.transform.position = new Vector3(camX + browseOffset, hintChevronBase + bob, cp.z);
    }

    // One page down, eased.
    void PageDown()
    {
        pageFrom = rackY;
        pageTo = Mathf.Min(rackYMax, rackY + (spaceTop - spaceBottom) * ScrollHint.PageFraction);
        pageT = 0f;
        scrollVelocity = 0f;
    }

    void TickPage(float dt)
    {
        if (pageT < 0f) return;
        pageT += dt / ScrollHint.ScrollSeconds;
        float k = DockTween.InOutCubic(pageT);
        SetRackY(Mathf.Lerp(pageFrom, pageTo, k));
        if (pageT >= 1f) pageT = -1f;
    }

    // The lot slides aside for the docked card; where it does, and by how much it has slid.
    float browseOffset, selectOffset, spaceLeft, spaceRight;
    Rect rackPane;

    // Where the lot's centre sits (relative to the camera) while a ship is selected in a wide view: centred in the
    // lot pane if it fits there, else shifted just enough to keep the selected bay inside the pane.
    void ComputeSelectOffset()
    {
        selectOffset = browseOffset;
        if (!wide) return;
        float w = layout.Width * layout.scale;
        if (w <= rackPane.width + .001f) { selectOffset = rackPane.center.x - camX; return; }
        float centreX = rackPane.center.x;
        if (Selected > 0)
        {
            float bx = layout.BayCenter(SlotOf(Selected)).x * layout.scale;
            centreX = Mathf.Clamp(rackPane.center.x - bx, rackPane.xMax - w * .5f, rackPane.xMin + w * .5f);
        }
        selectOffset = centreX - camX;
    }

    // The card fits neither above nor below the selected ship (a short
    // phone): slide the lot by `dy` so it does. Undone when the selection
    // changes or ends.
    float nudge;

    void Nudge(float dy)
    {
        nudge += dy;
        PlaceRack();
    }

    void ClearNudge()
    {
        if (nudge == 0f) return;
        nudge = 0f;
        PlaceRack();
    }

    void PlaceRack()
    {
        if (rack == null) return;
        var p = rack.position;
        rack.position = new Vector3(camX + Mathf.Lerp(browseOffset, selectOffset, DockTween.OutCubic(rackSlide)), rackY + nudge, p.z);
    }

    // Wide view: the lot sits in the middle until a ship is picked, then slides left to make room for its docked
    // card (rackSlide 0..1). Outside play there is no Update, so it snaps.
    bool wide;
    float spaceTop, spaceBottom;

    // The lot's scale (1 unless not even the narrowest lot fits) and the scroll range it leaves.
    void ApplyRackScale()
    {
        rack.localScale = new Vector3(layout.scale, layout.scale, 1f);
        layout.ScrollRange(spaceTop, spaceBottom, out rackYMin, out rackYMax);
        // an untouched lot rests at the top of its range (a layout pass before the header was sized must not strand it higher)
        rackY = scrolled ? Mathf.Clamp(rackY, rackYMin, rackYMax) : rackYMin;
    }
    bool scrolled;

    // The lot's drawn extent in world space (for tests).
    public Rect RackWorldRect
    {
        get
        {
            float w = layout.Width * layout.scale, h = layout.Height * layout.scale;
            return new Rect(rack.position.x - w * .5f, rack.position.y - h * .5f, w, h);
        }
    }
    // World y of the header's lowest edge and the footer buttons' highest tap edge (the lot's viewport sits between).
    public float HeaderBottomY { get; private set; }
    public float FooterTopY { get; private set; }

    // Scrolls the lot to `t` (0 = top, 1 = bottom). Tests.
    public void SetScroll(float t)
    {
        SetRackY(Mathf.Lerp(rackYMin, rackYMax, Mathf.Clamp01(t)));
    }
    public bool Scrollable { get { return rackYMax - rackYMin > .001f; } }

    // A bay's drawn extent in world space (for tests and the screen-fit rig).
    public Rect BayWorldRect(int id)
    {
        var bay = bays[id];
        Vector2 half = bay.Extent * layout.scale;
        Vector3 c = bay.transform.position;
        return new Rect(c.x - half.x, c.y - half.y, half.x * 2f, half.y * 2f);
    }
    // The scroll viewport between the header and footer bands (world), for tests.
    public Rect ViewportWorldRect { get { return new Rect(spaceLeft, spaceBottom, spaceRight - spaceLeft, spaceTop - spaceBottom); } }
    float camX, rackSlide;
    const float SlideTime = .22f;

    void SlideRack()
    {
        if (!Application.isPlaying) rackSlide = wide && Selected > 0 ? 1f : 0f;
        ComputeSelectOffset();
        ApplyRackScale();
        PlaceRack();
    }

    void TickSlide(float dt)
    {
        float target = wide && Selected > 0 ? 1f : 0f;
        if (rackSlide == target) return;
        rackSlide = Mathf.MoveTowards(rackSlide, target, dt / SlideTime);
        PlaceRack();
    }

    // World y of the nearest tap-area edge of the buttons under `root`: the highest top edge (top = true) or the
    // lowest bottom edge (top = false), with each button's raycast padding applied (negative padding grows it).
    float HitEdgeY(RectTransform root, bool top)
    {
        float edge = top ? float.MinValue : float.MaxValue;
        foreach (var sel in root.GetComponentsInChildren<UnityEngine.UI.Selectable>(false))
        {
            var g = sel.targetGraphic != null ? sel.targetGraphic : sel.GetComponent<UnityEngine.UI.Graphic>();
            if (g == null || !g.raycastTarget) continue;
            var grt = g.rectTransform;
            float y0 = ScreenToWorldY(grt, 0), y1 = ScreenToWorldY(grt, 1);
            float perUnit = grt.rect.height > .01f ? (y1 - y0) / grt.rect.height : 0f;
            Vector4 pad = g.raycastPadding;   // left, bottom, right, top
            if (top) edge = Mathf.Max(edge, y1 - pad.w * perUnit);
            else edge = Mathf.Min(edge, y0 + pad.y * perUnit);
        }
        if (edge == float.MinValue || edge == float.MaxValue) edge = top ? ScreenToWorldY(root, 1) : ScreenToWorldY(root, 0);
        return edge;
    }

    // World y of a screen-space-overlay element's bottom (corner 0) or top
    // (corner 1) edge.
    float ScreenToWorldY(RectTransform rt, int corner)
    {
        rt.GetWorldCorners(corners);
        var canvas = rt.GetComponentInParent<Canvas>();
        Vector3 p = corners[corner];
        if (canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay)
            p = RectTransformUtility.WorldToScreenPoint(canvas.worldCamera, p);
        return cam.ScreenToWorldPoint(new Vector3(p.x, p.y, -cam.transform.position.z)).y;
    }

    // ----------------------------------------------------------------- order

    static int[] bayOrder;

    // ShipIds in berth order (slot 0 = top-left, row-major): price ascending,
    // ties broken by ShipId. Display only -- never saved.
    public static int[] BayOrder
    {
        get
        {
            if (bayOrder == null || bayOrder.Length != ShipId.Count)
            {
                var ids = new List<int>(ShipId.All);
                ids.Sort((a, b) =>
                {
                    int byPrice = shopingShips.CostFor(a).CompareTo(shopingShips.CostFor(b));
                    return byPrice != 0 ? byPrice : a.CompareTo(b);
                });
                bayOrder = ids.ToArray();
            }
            return bayOrder;
        }
    }

    // The berth slot ship `id` is parked in, or -1.
    public static int SlotOf(int id)
    {
        return System.Array.IndexOf(BayOrder, id);
    }

    // The ship's row (1 = top) and column (1 = left) in the grid as the
    // player sees it, from its berth slot: the one source for the hull's base
    // hearts (= row, ShipLives) and base start speed (by column,
    // ShipStartSpeed). 0 for an unknown ship.
    public static int RowOf(int id)
    {
        int slot = ShipId.IsValid(id) ? SlotOf(id) : -1;
        return slot < 0 ? 0 : slot / DockLayout.MaxColumns + 1;
    }

    public static int ColumnOf(int id)
    {
        int slot = ShipId.IsValid(id) ? SlotOf(id) : -1;
        return slot < 0 ? 0 : slot % DockLayout.MaxColumns + 1;
    }

    // The ship parked in berth `slot`, or ShipId.None.
    public static int ShipAt(int slot)
    {
        var order = BayOrder;
        return slot >= 0 && slot < order.Length ? order[slot] : ShipId.None;
    }

    // ----------------------------------------------------------------- state

    public static bool IsOwned(int index)
    {
        return ShipId.IsOwned(index);
    }

    // The ship gameS1 will spawn, validated the same way spawnShips does.
    public static int EquippedIndex()
    {
        return ShipId.Equipped();
    }

    public void RefreshStatuses()
    {
        if (this == null || bays == null) return;
        int equipped = EquippedIndex();
        for (int i = 1; i < bays.Length; i++)
            if (bays[i] != null)
            {
                bays[i].RefreshStatus(IsOwned(i), i == equipped, shopingShips.CostFor(i));
                if (!Launching) bays[i].ShowSkin(ShipSkins.Shown(i));
            }
        if (!Launching) ShipHullArt.ReleaseUnusedSkins();
        if (popup != null && popup.Visible && !Launching) ShowPopup(popup.ShipIndex);
    }

    public void Select(int index)
    {
        if (Launching || index < 1 || index >= bays.Length || bays[index] == null) return;
        if (index == Selected)
        {
            if (!popup.Visible) ShowPopup(index);
            return;
        }
        if (Selected > 0 && bays[Selected] != null) bays[Selected].SetPowered(false);
        if (ShipSkins.PreviewShip > 0) RevertSkinPreview();
        if (Selected == 0)
        {
            preSelectShipNumber = shopingShips.shipNumber;
            preSelectLastShip = shopingShips.LastShipSelected;
        }
        Selected = index;
        ClearNudge();
        SlideRack();
        shopingShips.LastShipSelected = shopingShips.shipNumber;
        shopingShips.shipNumber = index;
        rotateRight.shipSelected = index;
        bays[index].SetPowered(true);
        ShowPopup(index);
    }

    public void Deselect()
    {
        if (Launching) return;
        if (ShipSkins.PreviewShip > 0) RevertSkinPreview();
        if (Selected > 0 && bays[Selected] != null) bays[Selected].SetPowered(false);
        Selected = 0;
        rotateRight.shipSelected = 0;
        popup.Hide();
        ClearNudge();
        SlideRack();
    }

    void ShowPopup(int index)
    {
        var bay = bays[index];
        popup.Show(index, bay.ship, bay.ScreenHalfHeight * layout.scale, IsOwned(index),
                   index == EquippedIndex(), shopingShips.CostFor(index), StarDustLedger.Saved);
        // Skins appear once the ship itself is owned.
        if (IsOwned(index)) popup.ShowSkins(index, ShipSkins.Shown(index), StarDustLedger.Saved);
    }

    // BUY: the existing purchase rules -- deduct once, mark bought, equip,
    // save immediately (shopingShips.TryPurchase).
    public void Buy(int index)
    {
        if (Launching || index < 1 || index >= bays.Length) return;
        if (IsOwned(index)) { ShowPopup(index); return; }
        // Premium ships are real-money only (PremiumShips) and bought in the Shop screen, never here: the dock
        // neither sells one for dust nor starts a purchase, it sends the player to the Shop (which shows the
        // own-N-ships gate and refuses the purchase while it holds).
        if (PremiumShips.IsPremium(index))
        {
            GoToShop();
            return;
        }
        // The last berths stay shut until enough ships are owned (ShipUnlocks): no purchase, no dust taken.
        if (ShipUnlocks.IsLocked(index))
        {
            popup.ShowMessage(ShipUnlocks.LockedMessage(index), DockArt.Warn, 1.4f);
            return;
        }
        float price = shopingShips.CostFor(index);
        if (shopingShips.TryPurchase(index, price))
        {
            // Out of the shadows: black -> white -> the hull's colours.
            if (bays[index] != null) bays[index].BeginReveal();
            startMenu.spawnTracker = index;
            rotateRight.shipSelected = index;
            if (shop != null) shop.RefreshStarDust();
            RefreshStatuses();
            ShowPopup(index);
            popup.ShowMessage("ACQUIRED", DockArt.Cyan, 1.2f);
        }
        else
        {
            popup.ShowCantAfford(price - StarDustLedger.Saved);
        }
    }

    // Leaves for the Shop (home, with the Shop panel opening). Where it cannot, the berth says so.
    public void GoToShop()
    {
        if (!ShopCatalog.Open()) popup.ShowMessage(PremiumShips.ComingSoonLabel, DockArt.Cyan, 1.2f);
    }

    // Equips the ship (saved at once) and launches it.
    public void Launch(int index)
    {
        if (Launching || index < 1 || index >= bays.Length || bays[index] == null) return;
        if (!IsOwned(index)) { Buy(index); return; }
        // An unbought preview never flies: the equipped skin launches.
        if (ShipSkins.PreviewShip > 0) RevertSkinPreview();
        ShipId.Equip(index);
        PrefsSaver.SaveNow();
        startMenu.spawnTracker = index;
        rotateRight.shipSelected = index;
        moveBackGround.speed = 0f;
        score.totalCurrency = 0;

        if (Selected > 0 && Selected != index && bays[Selected] != null) bays[Selected].SetPowered(false);
        Selected = index;
        popup.Hide();
        Launching = true;
        shopingShips.turnOffCanves();
        RefreshStatuses();
        if (Application.isPlaying) StartCoroutine(LaunchRoutine(index));
    }

    // Launches the equipped ship (ignores the current selection).
    public void LaunchEquipped()
    {
        Launch(EquippedIndex());
    }

    // The ship the LIFT-OFF button launches: the one selected in the dock if
    // it is owned, else the equipped one. LIFT-OFF used to always launch the
    // equipped ship, so tapping a ship (its popup offering LAUNCH) and then
    // pressing LIFT-OFF flew whichever ship had been equipped before.
    public int LiftOffIndex()
    {
        return Selected > 0 && IsOwned(Selected) ? Selected : EquippedIndex();
    }

    // PLAY / LIFT-OFF button.
    public void LiftOff()
    {
        Launch(LiftOffIndex());
    }

    IEnumerator LaunchRoutine(int index)
    {
        var bay = bays[index];
        var ship = bay.ship;
        bay.Launching = true;
        bay.SetPowered(true);
        bay.SetHighlightOff();
        foreach (var sr in ship.GetComponentsInChildren<SpriteRenderer>(true))
            sr.sortingOrder = sr == bay.hull ? DockBay.OrderLaunch : DockBay.OrderLaunch - 1;
        var thruster = bay.thruster;
        Vector3 rest = ship.localPosition;
        Vector3 restScale = ship.localScale;
        Quaternion restRotation = ship.localRotation;
        float t = 0f;
        skipLaunch = false;

        // 1. Undock, in bay space: the hull lifts off the pad (grows, shadow slides away) and pulls straight out of
        //    its bay, forward = its nose, to the bay's mouth at the lane.
        while (t < UndockTime && !skipLaunch)
        {
            t += Time.unscaledDeltaTime;
            float lift = DockLaunch.Lift(t);
            float slide = DockLaunch.Slide(t);
            ship.localPosition = rest + new Vector3(0f, DockLaunch.SlideDistance * slide, 0f);
            ship.localScale = restScale * (1f + DockLaunch.LiftGrow * lift);
            ship.localRotation = restRotation;
            bay.SetShadow(.1f * lift, .55f - .2f * lift);
            if (thruster != null) thruster.idleScale = DockLaunch.UndockFlame(lift);
            yield return null;
        }

        // 2. Fly out: accelerate forward into the lane, turn nose-up and drive up the lane and out of view.
        Vector3 worldScale = ship.lossyScale;
        Vector3 nose = ship.up;
        ship.SetParent(transform, true);
        Vector3 p0 = ship.position;
        float halfH = cam != null ? cam.orthographicSize : 5f;
        float camX = cam != null ? cam.transform.position.x : 0f;
        float camTop = (cam != null ? cam.transform.position.y : 0f) + halfH;
        Vector3 p1, p2;
        DockLaunch.ExitPath(p0, nose, camX, camTop, true, out p1, out p2);
        float flight = LaunchDuration - UndockTime;
        float f = 0f;
        float spin = 0f;
        while (f < 1f && !skipLaunch)
        {
            f = Mathf.Min(1f, f + Time.unscaledDeltaTime / flight);
            float u = DockLaunch.Flight(f);
            Vector3 pos = DockTween.Bezier(p0, p1, p2, u);
            ship.position = pos;
            if (bay.wind)
            {
                spin += Mathf.Lerp(120f, 900f, f) * Time.unscaledDeltaTime;
                ship.rotation = Quaternion.Euler(0f, 0f, spin);
            }
            else
            {
                // the path starts along the nose and ends pointing up the lane: the hull simply follows it
                Vector3 tangent = DockTween.BezierTangent(p0, p1, p2, u);
                float heading = Mathf.Atan2(tangent.y, tangent.x) * Mathf.Rad2Deg - 90f;
                ship.rotation = Quaternion.Euler(0f, 0f, heading);
            }
            float grow = DockLaunch.FlightGrow(f);
            ship.localScale = new Vector3(worldScale.x * grow, worldScale.y * grow, 1f);
            bay.SetShadow(.1f + f * .4f, Mathf.Max(0f, .35f - f * .8f));
            if (thruster != null) thruster.idleScale = DockLaunch.Flare(f);
            yield return null;
        }

        Time.timeScale = 1f;
        MenuNav.Load(Destination);
    }

    // ----------------------------------------------------------------- input

    void Update()
    {
        if (cam == null) return;
        if (Launching)
        {
            // A tap skips the launch animation.
            if (Input.GetMouseButtonDown(0)) skipLaunch = true;
            return;
        }

        float dt = Time.unscaledDeltaTime;
        TickSlide(dt);
        TickPage(dt);
        TickHint(dt);
        bool scrollable = rackYMax - rackYMin > .001f;
        Vector2 pointer = Input.mousePosition;

        if (Input.GetMouseButtonDown(0))
        {
            pressing = !PointerOverUI();
            dragging = false;
            pressAt = pointer;
            lastPointerY = pointer.y;
            scrollVelocity = 0f;
        }
        if (pressing && Input.GetMouseButton(0))
        {
            float dpi = Screen.dpi > 0f ? Screen.dpi : 160f;
            if (!dragging && (pointer - pressAt).magnitude > DragThresholdInches * dpi) dragging = true;
            if (dragging && scrollable)
            {
                float worldPerPixel = cam.orthographicSize * 2f / Mathf.Max(1, ScreenInfo.Height);
                float delta = (pointer.y - lastPointerY) * worldPerPixel;
                SetRackY(rackY + delta);
                if (dt > 0f) scrollVelocity = delta / dt;
            }
            lastPointerY = pointer.y;
        }
        if (pressing && Input.GetMouseButtonUp(0))
        {
            pressing = false;
            if (!dragging)
            {
                Vector3 world = cam.ScreenToWorldPoint(new Vector3(pointer.x, pointer.y, -cam.transform.position.z));
                // the bands over the lot's ends are not part of the lot: a tap there reaches nothing underneath
                if (world.y > spaceTop || world.y < hintFloor) { }
                else if (hintShown && hintAlpha > .3f && HintRect.Contains(world)) PageDown();
                else Tap(world);
            }
        }
        if (!pressing && scrollVelocity != 0f)
        {
            SetRackY(rackY + scrollVelocity * dt);
            scrollVelocity *= Mathf.Exp(-6f * dt);
            if (Mathf.Abs(scrollVelocity) < .02f) scrollVelocity = 0f;
        }
    }

    void LateUpdate()
    {
        if (cam == null) return;
        if (needsLayout || ScreenInfo.Width != lastScreenW || ScreenInfo.Height != lastScreenH ||
            !Mathf.Approximately(cam.orthographicSize, lastOrtho))
            Relayout();
    }

    void Start()
    {
        // CameraFit resizes the camera in its own Start, and the HUD canvas
        // only becomes active in shopingShips.Start: lay out again once both
        // have happened.
        needsLayout = true;
    }

    public void Tap(Vector3 world)
    {
        for (int i = 1; i < bays.Length; i++)
            if (bays[i] != null && bays[i].Contains(world)) { Select(i); return; }
        Deselect();
    }

    void SetRackY(float y)
    {
        scrolled = true;
        rackY = Mathf.Clamp(y, rackYMin, rackYMax);
        PlaceRack();
    }

    static bool PointerOverUI()
    {
        var events = EventSystem.current;
        if (events == null) return false;
        if (Input.touchCount > 0) return events.IsPointerOverGameObject(Input.GetTouch(0).fingerId);
        return events.IsPointerOverGameObject();
    }

    // Header and footer styling for the dock: star dust readout and a short
    // instruction line on top, BACK and LIFT-OFF at the bottom.
    public static void StyleChrome(GameObject canvas)
    {
        foreach (var c in new[] { canvas, SceneUtil.FindAny("StarDustCanvas") })
        {
            if (c == null) continue;
            var scaler = c.GetComponent<CanvasScaler>();
            if (scaler == null) continue;
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;
            // BACK / LIFT-OFF: FooterH + FooterTapPad tall (UiScale's floor)
            UiScaleFloor.Configure(scaler, new Vector2(720, 960), FooterH + FooterTapPad, UiScaleFloor.SceneTextUnits);
        }
        if (canvas == null) return;
        DockStartWorld.Build(canvas.transform);
        PlaceFooter("BackButton", canvas.transform, -1);
        PlaceFooter("PlayButton", canvas.transform, 1);

        var instructionGo = SceneUtil.FindAny("~DockInstruction");
        if (instructionGo != null)
        {
            var instruction = instructionGo.GetComponent<Text>();
            instruction.text = "SPACE DOCK  ·  TAP A SHIP";
            instruction.fontSize = 19;
            instruction.color = AkiraPalette.WithAlpha(AkiraPalette.Cyan, .85f);
            instruction.rectTransform.anchoredPosition = new Vector2(0, InstructionY);
            instruction.rectTransform.sizeDelta = new Vector2(650, 30);
        }
        var dust = SceneUtil.FindAny("starDustText");
        if (dust != null)
        {
            var t = dust.GetComponent<Text>();
            t.fontSize = 25;
            t.resizeTextForBestFit = false;
            t.alignment = TextAnchor.MiddleCenter;
            t.color = DockArt.Gold;
            t.raycastTarget = false;
            var rt = t.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 1);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.localScale = Vector3.one;
            rt.anchoredPosition = new Vector2(0, DustY);
            rt.sizeDelta = new Vector2(640, 40);
        }
        PlaceChrome();
    }

    // Authored header / footer positions (canvas units, reference 720x960),
    // measured from the safe area's edges.
    const float DustY = -27f, InstructionY = -60f;
    const float FooterY = 24f, FooterW = 260f, FooterH = 64f, FooterGap = 40f, FooterSide = 20f;
    // The footer buttons' hit areas reach this far above their art (a
    // finger-sized target on short phones without bigger art). Only upwards:
    // below, they would reach into the display's rounded corners, which
    // Android does not report as a safe-area inset.
    const float FooterTapPad = 24f;

    static void PlaceFooter(string name, Transform parent, int side)
    {
        var go = SceneUtil.FindAny(name);
        if (go == null) return;
        go.transform.SetParent(parent, false);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0);
        rt.pivot = new Vector2(0.5f, 0);
        rt.anchoredPosition = new Vector2(side * (FooterGap + FooterW) * .5f, FooterY);
        rt.sizeDelta = new Vector2(FooterW, FooterH);
        rt.localScale = Vector3.one;
        var t = go.GetComponentInChildren<Text>();
        if (t != null) { t.fontSize = 30; t.resizeTextForBestFit = false; }
        var g = go.GetComponent<Graphic>();
        if (g != null) g.raycastPadding = new Vector4(0f, 0f, 0f, -FooterTapPad);
    }

    // Keeps the star dust read-out and the instruction under the safe area's
    // top, and BACK / LIFT-OFF above its bottom and inside its width: on
    // 20:9 and taller phones the canvas (matched half width, half height) is
    // under 600 units wide, so the 260-unit buttons ran off both sides, and
    // they sat on the home indicator / under the 3-button bar. On a phone
    // without insets that is wide enough this is exactly the authored layout.
    public static void PlaceChrome()
    {
        Place("starDustText", DustY, true);
        Place("~DockInstruction", InstructionY, true);
        Place(DockStartWorld.Name, DockStartWorld.Y, true);
        foreach (string name in new[] { "BackButton", "PlayButton" })
        {
            var go = SceneUtil.FindAny(name);
            var rt = go != null ? go.GetComponent<RectTransform>() : null;
            var canvas = go != null ? go.GetComponentInParent<Canvas>() : null;
            if (rt == null || canvas == null) continue;
            float sf = Mathf.Max(canvas.rootCanvas.scaleFactor, .0001f);
            float canvasW = ScreenInfo.Width / sf;
            float safeL = ScreenInfo.SafeLeftInset / sf, safeR = ScreenInfo.SafeRightInset / sf;
            float room = canvasW - safeL - safeR - 2f * FooterSide - FooterGap;
            float w = Mathf.Clamp(room * .5f, 0f, FooterW);
            float side = Mathf.Sign(rt.anchoredPosition.x == 0f ? (name == "PlayButton" ? 1f : -1f) : rt.anchoredPosition.x);
            float centre = (safeL - safeR) * .5f;
            rt.sizeDelta = new Vector2(w, FooterH);
            rt.anchoredPosition = new Vector2(centre + side * (FooterGap + w) * .5f, FooterY + ScreenInfo.SafeBottomInset / sf);
        }
    }

    static void Place(string name, float y, bool fromTop)
    {
        var go = SceneUtil.FindAny(name);
        var rt = go != null ? go.GetComponent<RectTransform>() : null;
        var canvas = go != null ? go.GetComponentInParent<Canvas>() : null;
        if (rt == null || canvas == null) return;
        float sf = Mathf.Max(canvas.rootCanvas.scaleFactor, .0001f);
        float inset = fromTop ? -ScreenInfo.SafeTopInset / sf : ScreenInfo.SafeBottomInset / sf;
        rt.anchoredPosition = new Vector2(rt.anchoredPosition.x, y + inset);
    }
}
