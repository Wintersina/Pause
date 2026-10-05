using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// The ship-select space dock (shopS6).
//
// Every ship is parked in its own berth of a docking rack: walls, support
// spines, gantries between rows and a launch gate at the top. Parked ships
// are powered down. Tapping one powers it up and floats its card above (or
// below) it (LAUNCH if owned, price + BUY if not); its X, tapping anywhere
// else or Back dismisses it. An owned ship's popup also carries its hull-skin swatches: an owned
// skin equips on tap (saved at once); a locked one previews on the hull with
// its price and BUY, and Back (or leaving the ship) puts the equipped skin
// back. Launching undocks the ship -- clamps release, it lifts and backs out of
// its berth, then accelerates out through the gate with its engine flaring
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
    SpriteRenderer backplate, gate;
    readonly SpriteRenderer[] walls = new SpriteRenderer[2];
    readonly List<SpriteRenderer> spines = new List<SpriteRenderer>();
    readonly List<SpriteRenderer> gantries = new List<SpriteRenderer>();
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
    bool rackPlaced;
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
        BackNavigator.Register(this, OnBackPressed);
    }

    void OnDisable()
    {
        DeveloperUnlocks.Changed -= RefreshStatuses;
        BackNavigator.Unregister(this);
        if (Instance == this) Instance = null;
    }

    void OnDestroy()
    {
        DeveloperUnlocks.Changed -= RefreshStatuses;
        BackNavigator.Unregister(this);
        if (Instance == this) Instance = null;
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
        var result = ShipSkins.TryPurchase(index, skin);
        if (result == ShipSkins.PurchaseResult.Bought)
        {
            if (shop != null) shop.RefreshStarDust();
            ShowBaySkin(index);
            ShowPopup(index);
            popup.ShowMessage("ACQUIRED", DockArt.Cyan, 1.2f);
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
        BackNavigator.Register(this, OnBackPressed);
        cam = Camera.main;
        // Beyond the starfield (wide windows) show deep space, not the
        // scene's authored mid-blue clear colour.
        if (cam != null) cam.backgroundColor = AkiraPalette.Night0;
        shop = Object.FindFirstObjectByType<shopingShips>();
        var font = DockArt.FindSceneFont();

        rack = new GameObject("~DockRack").transform;
        rack.SetParent(transform, false);

        backplate = Piece("Backplate", DockArt.Get("backplate", 14f), SpriteDrawMode.Sliced, 0);
        for (int k = 0; k < 2; k++)
        {
            walls[k] = Piece("Wall" + k, DockArt.Get("wall"), SpriteDrawMode.Tiled, 1);
            walls[k].flipX = k == 1;
        }
        gate = Piece("LaunchGate", DockArt.Get("gate"), SpriteDrawMode.Tiled, 3);

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
        popup.onSkin = TapSkin;
        popup.onBuySkin = BuySkin;
        popup.onClose = UndoSelection;
        popup.onNudge = Nudge;

        FindChrome();
        RefreshStatuses();
        Relayout();
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
        int count = shopingShips.shipTotal - 1;
        layout = DockLayout.For(count, halfW);
        rack.localScale = new Vector3(layout.scale, layout.scale, 1f);

        for (int i = 1; i < bays.Length; i++)
            if (bays[i] != null) bays[i].transform.localPosition = layout.BayCenter(SlotOf(i));

        float w = layout.Width, h = layout.Height;
        backplate.size = new Vector2(w, h);
        backplate.transform.localPosition = Vector3.zero;
        float wallH = h;
        for (int k = 0; k < 2; k++)
        {
            walls[k].size = new Vector2(DockLayout.Wall, wallH);
            walls[k].transform.localPosition = new Vector3((k == 0 ? -1f : 1f) * (w - DockLayout.Wall) * .5f, 0f, 0f);
        }
        float inner = w - 2f * DockLayout.Wall;
        gate.size = new Vector2(inner, DockLayout.Gate);
        gate.transform.localPosition = new Vector3(0f, h * .5f - DockLayout.Gate * .5f, 0f);

        float bayTop = h * .5f - DockLayout.Gate;
        float bayBottom = bayTop - (layout.rows * DockLayout.BaySize.y + (layout.rows - 1) * DockLayout.Gantry);
        int spineCount = layout.columns - 1;
        for (int c = 0; c < Mathf.Max(spineCount, spines.Count); c++)
        {
            if (c >= spines.Count) spines.Add(Piece("Spine" + c, DockArt.Get("spine"), SpriteDrawMode.Tiled, 1));
            var spine = spines[c];
            spine.gameObject.SetActive(c < spineCount);
            if (c >= spineCount) continue;
            float x = layout.BayCenter(c).x + layout.PitchX * .5f;
            spine.size = new Vector2(DockLayout.Spine, bayTop - bayBottom);
            spine.transform.localPosition = new Vector3(x, (bayTop + bayBottom) * .5f, 0f);
        }
        int gantryCount = layout.rows - 1;
        for (int r = 0; r < Mathf.Max(gantryCount, gantries.Count); r++)
        {
            if (r >= gantries.Count) gantries.Add(Piece("Gantry" + r, DockArt.Get("gantry"), SpriteDrawMode.Tiled, 3));
            var gantry = gantries[r];
            gantry.gameObject.SetActive(r < gantryCount);
            if (r >= gantryCount) continue;
            float y = layout.BayCenter(r * layout.columns).y - layout.PitchY * .5f;
            gantry.size = new Vector2(inner, DockLayout.Gantry);
            gantry.transform.localPosition = new Vector3(0f, y, 0f);
        }

        // The header and footer follow the safe area (notch / Dynamic Island /
        // punch-hole above, home indicator / nav bar below) before the rack is
        // fitted between them.
        PlaceChrome();

        // The rack fills the space between the HUD header and the footer
        // buttons, and scrolls only if it cannot fit there.
        float viewTop = camPos.y + halfH, viewBottom = camPos.y - halfH;
        Canvas.ForceUpdateCanvases();
        float headerBottom = viewTop, footerTop = viewBottom;
        foreach (var rt in header)
            if (rt != null && rt.gameObject.activeInHierarchy) headerBottom = Mathf.Min(headerBottom, ScreenToWorldY(rt, 0));
        foreach (var rt in footer)
            if (rt != null && rt.gameObject.activeInHierarchy) footerTop = Mathf.Max(footerTop, ScreenToWorldY(rt, 1));
        float top = headerBottom - .06f, bottom = footerTop + .06f;
        if (top - bottom < 2f) { top = viewTop - .1f; bottom = viewBottom + .1f; }
        layout.ScrollRange(top, bottom, out rackYMin, out rackYMax);
        rackY = Mathf.Clamp(rackPlaced ? rackY : rackYMin, rackYMin, rackYMax);
        rackPlaced = true;
        nudge = 0f;
        rack.position = new Vector3(camPos.x, rackY, 0f);

        // Sideways the popup keeps inside the safe area too (waterfall edges).
        float worldPerPixel = halfH * 2f / Mathf.Max(1, ScreenInfo.Height);
        float left = camPos.x - halfW + ScreenInfo.SafeLeftInset * worldPerPixel + .05f;
        float right = camPos.x + halfW - ScreenInfo.SafeRightInset * worldPerPixel - .05f;
        popup.safeView = new Rect(left, bottom, right - left, top - bottom);
        // The card is sized in points / dp (UiScale): world units per point.
        popup.SetDensity(UiScale.PxPerPoint * worldPerPixel);
    }

    // The card fits neither above nor below the selected ship (a short
    // phone): slide the rack by `dy` so it does. Undone when the selection
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
        rack.position = new Vector3(p.x, rackY + nudge, p.z);
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
    }

    void ShowPopup(int index)
    {
        var bay = bays[index];
        popup.Show(index, bay.ship, bay.hullHalfSize.y * layout.scale, IsOwned(index),
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
        foreach (var sr in ship.GetComponentsInChildren<SpriteRenderer>(true))
            sr.sortingOrder = sr == bay.hull ? DockBay.OrderLaunch : DockBay.OrderLaunch - 1;
        var thruster = bay.thruster;
        Vector3 rest = ship.localPosition;
        Vector3 restScale = ship.localScale;
        Quaternion restRotation = ship.localRotation;
        float t = 0f;
        skipLaunch = false;

        // 1. Undock, in berth space: clamps release, the hull lifts off the
        //    pad (grows, shadow slides away) and eases back out of the cradle.
        while (t < UndockTime && !skipLaunch)
        {
            t += Time.unscaledDeltaTime;
            bay.SetClampOpen(DockLaunch.Clamps(t));
            float lift = DockLaunch.Lift(t);
            float back = DockLaunch.BackOff(t);
            ship.localPosition = rest + new Vector3(0f, -DockLaunch.BackOffDistance * back, 0f);
            ship.localScale = restScale * (1f + DockLaunch.LiftGrow * lift);
            ship.localRotation = restRotation;
            bay.SetShadow(.1f * lift, .55f - .2f * lift);
            if (thruster != null) thruster.idleScale = DockLaunch.UndockFlame(lift);
            yield return null;
        }

        // 2. Fly out: accelerate forward through the berth mouth, bank toward
        //    the centre lane and climb out through the launch gate.
        Vector3 worldScale = ship.lossyScale;
        ship.SetParent(transform, true);
        Vector3 p0 = ship.position;
        float halfH = cam != null ? cam.orthographicSize : 5f;
        float camX = cam != null ? cam.transform.position.x : 0f;
        float camTop = (cam != null ? cam.transform.position.y : 0f) + halfH;
        Vector3 p1 = p0 + new Vector3(0f, DockLaunch.ApproachDistance, 0f);
        Vector3 p2 = new Vector3(camX + (p0.x - camX) * .2f, camTop + 1.2f, p0.z);
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
                Vector3 tangent = DockTween.BezierTangent(p0, p1, p2, u);
                float heading = Mathf.Atan2(tangent.y, tangent.x) * Mathf.Rad2Deg - 90f;
                ship.rotation = Quaternion.Euler(0f, 0f, heading * DockLaunch.TurnIn(f));
            }
            float grow = DockLaunch.FlightGrow(f);
            ship.localScale = new Vector3(worldScale.x * grow, worldScale.y * grow, 1f);
            bay.SetShadow(.1f + f * .4f, Mathf.Max(0f, .35f - f * .8f));
            if (thruster != null) thruster.idleScale = DockLaunch.Flare(f);
            yield return null;
        }

        Time.timeScale = 1f;
        SceneManager.LoadScene(Destination);
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
                Tap(world);
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
            scaler.referenceResolution = new Vector2(720, 960);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;
        }
        if (canvas == null) return;
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
