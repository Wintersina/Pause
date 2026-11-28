using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// Read-only squad HUD under the scoreboard: collected modifiers for the selected drone in first-catch order,
// including shared RAPID, then compact mode pips for all six possible slots. Paused rows use the same ordering.
// Nearby pickup previews select their recipient; dormant slots remain visible from run start.
// All graphics ignore raycasts so the pilot can press anywhere to pause. Geometry follows
// the scoreboard's painted bottom edge, rails, quick actions and safe-area bounds.
public partial class ShotAtomHud : MonoBehaviour
{
    public const string HostName = "~ShotAtomHud";
    public const float IconUnits = 24f;        // a coin's size in quick-action canvas units
    public const float MinSlotUnits = 26f;     // wide enough for "x10"
    public const float LabelUnits = 11f;       // the level line under a coin
    public const float GapUnits = 3f;          // between two slots, and under the score board
    public const float MaxHeightShare = .34f;  // of the screen's height, the most the block may take (it shrinks to fit)
    public const float CoinTopClearShare = 10f / 196f; // the installed idle flipbooks' transparent top strip
    public const int Columns = 7;              // the drone glyph, Twin .. Arc, then the shared Rapid coin
    public static int SelectedAtomSlot(ShotAtom atom) => atom == ShotAtom.Rapid ? RapidColumn : (int)atom + 1;
    public static int PipSlot(int drone) => Columns + drone;
    public const int GlyphColumn = 0, RapidColumn = 6, MaxRows = ShotAtomTuning.MaxDrones;
    public const int SortOrder = 88;           // above the boss warning (85) and PORTAL DANGER (84), under the booster column (89)
    public const int MaxSlots = Columns + DroneSquad.MaxSlots;

    public struct Geometry
    {
        public float left, top, icon, pitch, slotWidth, slotHeight, label, rowPitch;
        public float originalVisibleGap; // before moving the visible coins halfway toward the board
        public int rows;
    }

    // Every rect the block must stay clear of, in screen px: the Replay/Home pair. (The boss warning's banner and chip and PORTAL
    // DANGER's chip are NOT obstacles: the block never moves for them; it is drawn above them, canvas sort order 88 over 85.
    // The score read-out and the booster column are handled in Compute: the block starts under the first and is kept beside the second.)
    public static Rect[] Obstacles(TopBand.Frame band, Rect safe, Vector2 screen, Rect hud)
    {
        return new[] { PauseQuickActions.ScreenRectFor(band, screen) };
    }

    public static Geometry Compute(TopBand.Frame band, Rect safe, Vector2 screen, Rect hud)
    {
        return Compute(band, safe, screen, hud, -1);
    }

    // `world`: whose board frame is on screen (-1 = the live world). Visible coin art moves halfway closer to its painted bottom edge.
    public static Geometry Compute(TopBand.Frame band, Rect safe, Vector2 screen, Rect hud, int world)
    {
        return Compute(band, safe, screen, hud, world, ShotAtoms.DroneCount);
    }

    public static Geometry Compute(TopBand.Frame band, Rect safe, Vector2 screen, Rect hud, int world, int rows)
    {
        rows = Mathf.Clamp(rows, 1, MaxRows);
        var g = new Geometry { rows = rows };
        float s = PauseQuickActions.CanvasScaleFor(screen);
        float gap = Mathf.Max(1f, Mathf.Round(GapUnits * s));
        bool board = hud.height > 0f;
        float inset = board ? gap : 0f;
        g.left = Mathf.Max(board ? hud.xMin + inset : band.left, band.left);
        float right = board ? Mathf.Min(hud.xMax - inset, band.right) : Mathf.Min(band.right, PauseQuickActions.ScreenRectFor(band, screen).xMin - gap);
        float startTop = (board ? hud.yMin - HudStyler.BoardVisibleBottomPx(hud, world) : band.top) - gap;
        float want = Mathf.Max(8f, Mathf.Round(IconUnits * s));
        float label = Mathf.Round(LabelUnits * s);

        // the booster column, where it reaches into the row's band, narrows the row instead of pushing it down
        for (int i = 0; i < BoosterHud.MaxSlots; i++)
        {
            Rect b = BoosterHud.SlotRect(band, safe, screen, hud, i);
            if (b.width <= 0f || b.xMax <= g.left || b.yMax < startTop - want - label || b.yMin > startTop) continue;
            right = Mathf.Min(right, Mathf.Max(g.left, b.xMin - gap));
        }

        Fit(ref g, right, want, label, gap, screen);
        var obstacles = Obstacles(band, safe, screen, hud);
        float xMax = g.left + Columns * g.pitch - gap;
        g.top = BoosterHud.ColumnTop(obstacles, g.left, xMax, startTop, gap);
        if (board)
        {
            float boardBottom = hud.yMin - HudStyler.BoardVisibleBottomPx(hud, world);
            g.originalVisibleGap = boardBottom - VisibleIconTop(g);
            float visibleGap = g.originalVisibleGap * .5f;
            float targetTop = boardBottom - visibleGap + g.icon * CoinTopClearShare;
            // A right-hand control used to push the ENTIRE reserved row below it. Use the available
            // width beside it first; account for the coin's transparent pixels when moving the art.
            foreach (var obstacle in obstacles)
            {
                float bottom = targetTop - (g.rows * g.rowPitch - gap);
                if (obstacle.width <= 0f || obstacle.xMin >= xMax || obstacle.xMax <= g.left ||
                    obstacle.yMax <= bottom || obstacle.yMin >= targetTop) continue;
                float narrowedRight = Mathf.Min(right, obstacle.xMin - gap);
                if (narrowedRight - g.left < Columns * (8f + gap) - gap) continue;
                right = narrowedRight;
                Fit(ref g, right, want, label, gap, screen);
                xMax = g.left + Columns * g.pitch - gap;
                targetTop = boardBottom - visibleGap + g.icon * CoinTopClearShare;
            }
            g.top = targetTop;
            foreach (var obstacle in obstacles)
            {
                float bottom = g.top - (g.rows * g.rowPitch - gap);
                if (obstacle.width > 0f && obstacle.xMax > g.left && obstacle.xMin < xMax &&
                    obstacle.yMax > bottom && obstacle.yMin < g.top)
                    g.top = Mathf.Min(g.top, obstacle.yMin - gap);
            }
        }
        return g;
    }

    public static float VisibleIconTop(Geometry g) { return g.top - g.icon * CoinTopClearShare; }

    static void Fit(ref Geometry g, float right, float want, float label, float gap, Vector2 screen)
    {
        float avail = Mathf.Max(Columns, right - g.left);
        float slotWidth = Mathf.Max(want, Mathf.Round(MinSlotUnits * PauseQuickActions.CanvasScaleFor(screen)));
        float pitch = slotWidth + gap;
        if (pitch * Columns - gap > avail)
        {
            pitch = Mathf.Floor((avail + gap) / Columns);
            slotWidth = Mathf.Max(1f, pitch - gap);
        }
        float icon = Mathf.Min(want, slotWidth);
        float maxHeight = screen.y * MaxHeightShare;
        if (g.rows * (icon + label + gap) > maxHeight)
        {
            float k = maxHeight / (g.rows * (icon + label + gap));
            icon = Mathf.Max(8f, Mathf.Floor(icon * k));
            label = Mathf.Max(6f, Mathf.Floor(label * k));
        }
        g.pitch = pitch;
        g.slotWidth = slotWidth;
        g.icon = icon;
        g.label = label;
        g.slotHeight = icon + label;
        g.rowPitch = g.slotHeight + gap;
    }

    // Slot `index`'s screen-px rect (origin bottom-left): the coin on top, its level under it. index = row * Columns + column.
    public static Rect SlotRect(TopBand.Frame band, Rect safe, Vector2 screen, Rect hud, int index)
    {
        return SlotRect(Compute(band, safe, screen, hud), index);
    }

    public static Rect SlotRect(Geometry g, int index)
    {
        int row = index / Columns, col = index % Columns;
        return new Rect(Mathf.Round(g.left + col * g.pitch), Mathf.Round(g.top - g.slotHeight - row * g.rowPitch), g.slotWidth, g.slotHeight);
    }

    // The whole block's rect (every reserved row).
    public static Rect BlockRect(Geometry g)
    {
        float h = g.rows * g.rowPitch - (g.rowPitch - g.slotHeight);
        return new Rect(g.left, g.top - h, Columns * g.pitch - (g.pitch - g.slotWidth), h);
    }

    // The slot index of drone `drone`'s atom (Twin .. Arc), the drone's glyph, and the shared Rapid coin.
    public static int AtomSlot(int drone, ShotAtom atom) { return atom == ShotAtom.Rapid ? RapidColumn : drone * Columns + 1 + (int)atom; }
    public static int GlyphSlot(int drone) { return drone * Columns + GlyphColumn; }

    // The coin's square inside a slot (top centre, whole px).
    public static Rect IconRect(Geometry g, Rect slot)
    {
        return new Rect(Mathf.Round(slot.center.x - g.icon * .5f), slot.yMax - g.icon, g.icon, g.icon);
    }

    // "x2", "x10"
    public static string LevelLabel(int level) { return "x" + Mathf.Clamp(level, 0, ShotAtomTuning.MaxLevel); }

    public const float LabelCharWidth = .62f;   // of the font size: a wide digit in the HUD font

    // The level line's font size: LabelUnits' size, shrunk on a narrow board so even "x10" (two digits) fits inside its slot.
    public static int LabelFontSize(int baseSize, float slotWidth, int chars)
    {
        int fit = Mathf.FloorToInt(slotWidth / (Mathf.Max(1, chars) * LabelCharWidth));
        return Mathf.Max(6, Mathf.Min(baseSize, fit));
    }

    // ---- live ----

    sealed class Slot
    {
        public RectTransform root;
        public Image icon;
        public Text label;
        public ShotAtom atom;
        public bool glyph;
        public int drone;
        public int level = -1;
        public float pulse;      // 1 -> 0 after a level up: the coin swells a little
    }

    public const float PulseSeconds = .45f, PulseSwell = .4f;

    readonly Slot[] slots = new Slot[MaxSlots];
    Canvas canvas;
    CanvasGroup group;
    Font font;
    int shownVersion = -1, shownSquadVersion = -1, shownCount;
    int frame;
    float clock;

    Vector2 appliedScreen;
    Rect appliedSafe, appliedHud;
    TopBand.Frame appliedBand;
    bool placed;
    int placedRows;

    HudStyler styler;
    RectTransform hudRoot;
    Canvas hudCanvas;
    CanvasScaler hudScaler;
    int hudLookFrame = -1000;

    int appliedWorld = -1;
    Geometry geometry;
    Rect liveHud;

    public static ShotAtomHud Instance { get; private set; }
    void OnEnable() { Instance = this; }
    void OnDisable() { if (Instance == this) Instance = null; }

    // Where the upgrade popup hangs, in screen px: the top-centre point just under the row's reserved slots, centred on the
    // score board. False when there is no row on this screen (the tutorial, the menus): the popup then falls back.
    public static bool PopupAnchor(out Vector2 topCentre)
    {
        topCentre = Vector2.zero;
        var row = Instance;
        if (row == null || !row.isActiveAndEnabled) return false;
        var screen = ScreenInfo.Size;
        Rect safe = ScreenInfo.SafeArea;
        var band = TopBand.FrameFor(safe, screen);
        Rect hud = row.LiveHudRect(band, screen);
        var g = Compute(band, safe, screen, hud, -1, 1);
        float gap = Mathf.Max(1f, Mathf.Round(GapUnits * PauseQuickActions.CanvasScaleFor(screen)));
        float cx = hud.height > 0f ? hud.center.x : (band.left + band.right) * .5f;
        topCentre = new Vector2(Mathf.Round(cx), Mathf.Round(g.top - g.rows * g.rowPitch - gap - 28f * PauseQuickActions.CanvasScaleFor(screen)));
        return true;
    }

    public int VisibleCount { get { return shownCount; } }   // atom coins showing (the drone glyphs are not counted)
    public bool ActiveAt(int slot) { return slots[slot] != null && slots[slot].root.gameObject.activeSelf; }
    public Geometry CurrentGeometry { get { return geometry; } }
    public Rect CurrentHud { get { return liveHud; } }
    public float PulseAt(int slot) { return slots[slot].pulse; }
    public int FrameIndex { get { return frame; } }
    public void Advance(float dt) { clock += dt; }   // tests: spin by dt seconds
    public ShotAtom AtomAt(int slot) { return slots[slot].atom; }
    public string LabelAt(int slot) { return slots[slot].label != null ? slots[slot].label.text : null; }
    public Sprite IconAt(int slot) { return slots[slot].icon != null ? slots[slot].icon.sprite : null; }

    void Build()
    {
        var holder = new GameObject("ShotAtomHudCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(CanvasGroup));
        holder.transform.SetParent(transform, false);
        canvas = holder.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = SortOrder;      // under the booster column (89) and the quick actions (90)
        canvas.pixelPerfect = true;
        var scaler = holder.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;   // 1 canvas unit = 1 px: the rects are in screen pixels
        scaler.scaleFactor = 1f;
        group = holder.GetComponent<CanvasGroup>();
        group.blocksRaycasts = false;
        group.interactable = false;
        font = CodexUi.FindFont();

        BuildPausedRows(holder.transform);
        for (int i = 0; i < MaxSlots; i++)
        {
            var slot = new Slot();
            var go = new GameObject("ShotAtomSlot" + i, typeof(RectTransform));
            go.transform.SetParent(holder.transform, false);
            slot.root = (RectTransform)go.transform;
            slot.root.anchorMin = slot.root.anchorMax = Vector2.zero;
            slot.root.pivot = Vector2.zero;

            var ig = new GameObject("Icon", typeof(RectTransform), typeof(Image));
            ig.transform.SetParent(go.transform, false);
            slot.icon = ig.GetComponent<Image>();
            slot.icon.raycastTarget = false;
            slot.icon.preserveAspect = true;
            var ir = (RectTransform)ig.transform;
            ir.anchorMin = ir.anchorMax = Vector2.zero;
            ir.pivot = new Vector2(.5f, 1f);   // level-up swell grows down, keeping the board clear

            var tg = new GameObject("Label", typeof(RectTransform), typeof(Text), typeof(Outline));
            tg.transform.SetParent(go.transform, false);
            slot.label = tg.GetComponent<Text>();
            slot.label.font = font;
            slot.label.alignment = TextAnchor.UpperCenter;
            slot.label.raycastTarget = false;
            slot.label.horizontalOverflow = HorizontalWrapMode.Overflow;
            slot.label.verticalOverflow = VerticalWrapMode.Overflow;
            slot.label.color = AkiraPalette.Bone;
            var outline = tg.GetComponent<Outline>();
            outline.effectColor = AkiraPalette.WithAlpha(AkiraPalette.Ink, .9f);
            outline.effectDistance = new Vector2(1f, -1f);
            var tr = (RectTransform)tg.transform;
            tr.anchorMin = new Vector2(0f, 0f);
            tr.anchorMax = new Vector2(1f, 0f);
            tr.pivot = new Vector2(.5f, 0f);

            go.SetActive(false);
            slots[i] = slot;
        }
    }

    static bool DroneHasAny(int drone)
    {
        for (int a = 0; a < ShotAtomTuning.PerDroneAtoms; a++) if (ShotAtoms.Level(drone, (ShotAtom)a) > 0) return true;
        return false;
    }

    Sprite GlyphSprite()
    {
        var b = FloatieBattery.Current;
        return UltimateGun.CompanionSprite(b != null ? b.Ship : ShipId.Equipped(), DroneSquad.Selected);   // the selected drone's own drawing
    }

    void Refresh()
    {
        int coins = 0, selected = Mathf.Clamp(DroneSquad.Selected, 0, ShotAtoms.DroneCount - 1);
        for (int i = 0; i < MaxSlots; i++)
        {
            var slot = slots[i];
            bool pip = i >= Columns;
            slot.drone = pip ? i - Columns : selected;
            slot.glyph = pip || i == GlyphColumn;
            bool on = false;
            if (slot.glyph)
            {
                on = slot.drone < DroneSquad.Slots;
                slot.label.text = (slot.drone + 1).ToString();
                slot.icon.sprite = DronePickupArt.For(DroneSquad.Mode(slot.drone));
                slot.icon.color = DroneSquad.IsActive(slot.drone) ? Color.white : new Color(.55f, .6f, .65f, .65f);
                if (pip && DroneSquad.IsActive(slot.drone) && slot.drone != selected) slot.icon.color = new Color(.7f, .76f, .82f, .8f);
                slot.label.color = slot.drone == selected ? Color.white : new Color(.65f, .72f, .8f, .85f);
                slot.level = 0;
            }
            else
            {
                var atom = i == RapidColumn ? ShotAtom.Rapid : (ShotAtom)(i - 1);
                int level = ShotAtoms.Level(selected, atom);
                on = level > 0;
                if (on)
                {
                    if (atom != slot.atom || level != slot.level) slot.pulse = 1f;
                    slot.atom = atom; slot.level = level;
                    slot.label.text = LevelLabel(level); slot.icon.sprite = Frame(atom); slot.icon.color = Color.white;
                    coins++;
                }
                else slot.level = -1;
            }
            slot.root.gameObject.SetActive(on);
        }
        placed = false;
        shownCount = coins;
    }

    void Update()
    {
        if (startMenu.youAreInTutorial || ShipId.TutorialFlight) { if (group != null) group.alpha = 0f; return; }
        if (group != null) group.alpha = buttonClicks.playerDied ? 0f : 1f;
        if ((FloatieBattery.Current != null || ShotAtoms.OrderCount > 0) && canvas == null) Build();
        if (canvas == null) return;

        if (shownVersion != ShotAtoms.Version || shownSquadVersion != DroneSquad.Version)
        {
            shownVersion = ShotAtoms.Version; shownSquadVersion = DroneSquad.Version;
            Refresh();
        }

        float dt = Time.unscaledDeltaTime;
        for (int i = 0; i < MaxSlots; i++)
            if (slots[i].pulse > 0f)
            {
                slots[i].pulse = Mathf.Max(0f, slots[i].pulse - dt / PulseSeconds);
                float k = 1f + PulseSwell * slots[i].pulse;
                slots[i].icon.rectTransform.localScale = new Vector3(k, k, 1f);
            }

        // the border spins (the coin's own flipbook), on scaled time like every pickup
        clock += Time.unscaledDeltaTime * Time.timeScale;
        bool moved = false;
        while (clock >= PickupArt.ShotFrameSeconds)
        {
            clock -= PickupArt.ShotFrameSeconds;
            frame = (frame + 1) % PickupArt.ShotIdleTicks.Length;
            moved = true;
        }
        if (moved)
            for (int i = 0; i < MaxSlots; i++)
                if (!slots[i].glyph && slots[i].root.gameObject.activeSelf) slots[i].icon.sprite = Frame(slots[i].atom);
        Place();
        TickPausedRows(Time.unscaledDeltaTime);
    }

    Sprite Frame(ShotAtom atom)
    {
        var frames = PickupArt.Frames(PickupArt.IdleName(ShotAtoms.KindOf(atom)), PickupArt.ShotIdleTicks.Length);
        return frames[Mathf.Min(frame, frames.Length - 1)];
    }

    // Re-laid only when the screen, safe area, band or read-out changes.
    void Place()
    {
        var screen = ScreenInfo.Size;
        Rect safe = ScreenInfo.SafeArea;
        var band = TopBand.FrameFor(safe, screen);
        Rect hud = LiveHudRect(band, screen);
        int world = HudSkin.CurrentWorld;
        // re-anchored whenever the screen, the read-out, the drone count or the board's frame (the world) changes; never for a warning
        if (placed && placedRows == 1 && screen == appliedScreen && safe == appliedSafe && band.Same(appliedBand) && hud == appliedHud && world == appliedWorld) return;
        placed = true;
        appliedWorld = world;
        appliedScreen = screen; appliedSafe = safe; appliedBand = band; appliedHud = hud;
        liveHud = hud;
        var g = Compute(band, safe, screen, hud, world, 1);
        geometry = g;
        placedRows = g.rows;
        int fontSize = Mathf.Max(8, Mathf.RoundToInt(LabelUnits * PauseQuickActions.CanvasScaleFor(screen)));
        for (int i = 0; i < MaxSlots; i++)
        {
            int column = i > 0 && i < Columns ? CollectedColumn(DroneSquad.Selected, slots[i].atom) : i;
            Rect r = SlotRect(g, column);
            Rect ic = IconRect(g, r);
            if (i >= Columns)
            {
                float width = (Columns * g.pitch - Mathf.Max(1f, GapUnits * PauseQuickActions.CanvasScaleFor(screen))) / DroneSquad.MaxSlots;
                float icon = Mathf.Clamp(width - 2f, 8f, 22f), label = Mathf.Max(8f, icon * .45f);
                float top = g.top - g.slotHeight - g.label - 4f;
                r = new Rect(g.left + (i - Columns) * width, top - icon - label, width, icon + label);
                ic = new Rect(r.center.x - icon * .5f, r.yMin + label, icon, icon);
            }
            var slot = slots[i];
            slot.root.anchoredPosition = r.position;
            slot.root.sizeDelta = r.size;
            var ir = slot.icon.rectTransform;
            ir.anchoredPosition = new Vector2(ic.center.x - r.xMin, ic.yMax - r.yMin);
            ir.sizeDelta = ic.size;
            var tr = slot.label.rectTransform;
            tr.anchoredPosition = Vector2.zero;
            tr.sizeDelta = new Vector2(0f, g.label);
            slot.label.fontSize = LabelFontSize(fontSize, g.slotWidth, LevelLabel(ShotAtomTuning.MaxLevel).Length);
        }
    }

    // The score read-out's screen rect (zero-sized when there is none), as BoosterHud reads it.
    Rect LiveHudRect(TopBand.Frame band, Vector2 screen)
    {
        if (hudRoot == null && Time.frameCount - hudLookFrame >= 30)
        {
            hudLookFrame = Time.frameCount;
            if (styler == null) styler = FindFirstObjectByType<HudStyler>();
            if (styler != null && styler.HudRoot != null)
            {
                hudRoot = styler.HudRoot;
                hudCanvas = hudRoot.GetComponentInParent<Canvas>();
                if (hudCanvas != null) hudCanvas = hudCanvas.rootCanvas;
                hudScaler = hudCanvas != null ? hudCanvas.GetComponent<CanvasScaler>() : null;
            }
        }
        if (hudRoot == null || !hudRoot.gameObject.activeInHierarchy) return new Rect(band.left, band.top, 0f, 0f);
        // The read-out where it is DRAWN, not where the layout maths say it should be: the board is placed by HudStyler from the
        // scale / size it saw when it last re-placed itself, so anything the row predicted from the formulas (a stale scale after a
        // window change, a panel whose layout settled late) would hang the row away from the board. The corners are screen px on an
        // overlay canvas. The formula is only the fallback (a canvas that has no size yet).
        Rect drawn = DrawnRect(hudRoot, hudCanvas);
        if (drawn.width > 1f && drawn.height > 1f) return drawn;
        return HudStyler.HudScreenRect(band, screen, HudStyler.HudCanvasScale(hudCanvas, hudScaler, screen), hudRoot.rect.size);
    }

    static readonly Vector3[] corners = new Vector3[4];

    // `rt`'s screen-px rect as drawn (quarter-pixel steps, so a float wobble never re-places the row).
    public static Rect DrawnRect(RectTransform rt, Canvas canvas)
    {
        Camera cam = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
        rt.GetWorldCorners(corners);
        float x0 = float.MaxValue, y0 = float.MaxValue, x1 = float.MinValue, y1 = float.MinValue;
        for (int i = 0; i < 4; i++)
        {
            Vector2 p = RectTransformUtility.WorldToScreenPoint(cam, corners[i]);
            x0 = Mathf.Min(x0, p.x); y0 = Mathf.Min(y0, p.y); x1 = Mathf.Max(x1, p.x); y1 = Mathf.Max(y1, p.y);
        }
        x0 = Mathf.Round(x0 * 4f) * .25f; y0 = Mathf.Round(y0 * 4f) * .25f; x1 = Mathf.Round(x1 * 4f) * .25f; y1 = Mathf.Round(y1 * 4f) * .25f;
        return Rect.MinMaxRect(x0, y0, x1, y1);
    }
}

// Builds the row when the run scene loads.
public static class ShotAtomHudBootstrap
{
    [RuntimeInitializeOnLoadMethod]
    static void Init()
    {
        SceneManager.sceneLoaded -= OnLoaded;
        SceneManager.sceneLoaded += OnLoaded;
    }

    static void OnLoaded(Scene scene, LoadSceneMode mode)
    {
        if (scene.name != "gameS1") return;
        if (GameObject.Find(ShotAtomHud.HostName) != null) return;
        new GameObject(ShotAtomHud.HostName).AddComponent<ShotAtomHud>();
    }
}
