using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// The run's SHOT ATOM row: a horizontal row of small coins right under the score board, one per shot atom caught this run, in the
// order they were caught, each with its level under it ("x1".."x3"). Only the permanent shot atoms (ShotAtoms): never the
// boosters (BoosterHud keeps the right-hand column) and never the timed atoms. A coin is the atom's own idle flipbook, small, with
// the border spinning and the symbol still.
//
// NOT a control: no Button, no GraphicRaycaster, every graphic has raycastTarget off and the canvas group blocks nothing, so a
// finger on it is an ordinary press. Built on gameS1 only (never the tutorial).
//
// PLACEMENT (pure, Geometry / SlotRect, ShotAtomsTest): the row starts at the score board's inner left edge, a gap under the
// board's bottom FRAME (the art reaches below the layout rect), and stays inside the board's width, so it never meets the rails.
// It is slid down only under what is actually showing under the board: the boss warning's banner and chip while they are up,
// PORTAL DANGER's chip while it is up. (They used to be reserved always, which pushed the row below the boss banner's empty
// slot: well under the board.) The Replay/Home pair is always an obstacle; the booster column only narrows the row. All six slots
// are always reserved, so a coin joining the row never moves anything. It follows ScreenFit's top band (rails, notches,
// cutouts, safe area), so tall phones, the Flip cover screen, foldables and tablets all get it; on a narrow board the coins shrink.
public class ShotAtomHud : MonoBehaviour
{
    public const string HostName = "~ShotAtomHud";
    public const float IconUnits = 34f;        // a coin's size in quick-action canvas units
    public const float MinSlotUnits = 30f;     // wide enough for "x3"
    public const float LabelUnits = 14f;       // the level line under a coin
    public const float GapUnits = 4f;          // between two slots, and under the score board
    public const int MaxSlots = ShotAtomTuning.Count;

    public struct Geometry
    {
        public float left, top, icon, pitch, slotWidth, slotHeight, label;
    }

    // Every rect the row must stay clear of, in screen px: the Replay/Home pair always; the boss warning's chip and banner while
    // `boss` is up; PORTAL DANGER's chip while `portal` is up. (The score read-out and the booster column are handled in Compute:
    // the row starts under the first and is kept beside the second.)
    public static Rect[] Obstacles(TopBand.Frame band, Rect safe, Vector2 screen, Rect hud, bool boss, bool portal)
    {
        var list = new System.Collections.Generic.List<Rect>(4);
        list.Add(PauseQuickActions.ScreenRectFor(band, screen));
        if (boss)
        {
            var warning = BossWarningHud.ComputeLayout(safe, screen, hud, band);
            list.Add(warning.chip);
            list.Add(warning.banner);
        }
        if (portal)
        {
            float portalScale = Mathf.Min(screen.x / 800f, screen.y / 1200f);   // its canvas: 800x1200, Expand
            list.Add(PortalPressureHud.ChipScreenRect(band, portalScale > 0f ? portalScale : 1f));
        }
        return list.ToArray();
    }

    // Everything that can ever sit under the board (tests: nothing may overlap the row while it is all up).
    public static Rect[] Obstacles(TopBand.Frame band, Rect safe, Vector2 screen, Rect hud)
    {
        return Obstacles(band, safe, screen, hud, true, true);
    }

    public static Geometry Compute(TopBand.Frame band, Rect safe, Vector2 screen, Rect hud)
    {
        return Compute(band, safe, screen, hud, false, false);
    }

    public static Geometry Compute(TopBand.Frame band, Rect safe, Vector2 screen, Rect hud, bool boss, bool portal)
    {
        var g = new Geometry();
        float s = PauseQuickActions.CanvasScaleFor(screen);
        float gap = Mathf.Max(1f, Mathf.Round(GapUnits * s));
        bool board = hud.height > 0f;
        float inset = board ? gap : 0f;
        g.left = Mathf.Max(board ? hud.xMin + inset : band.left, band.left);
        float right = board ? Mathf.Min(hud.xMax - inset, band.right) : Mathf.Min(band.right, PauseQuickActions.ScreenRectFor(band, screen).xMin - gap);
        float startTop = (board ? hud.yMin - HudStyler.BoardBottomGrowthPx(screen) : band.top) - gap;
        float want = Mathf.Max(8f, Mathf.Round(IconUnits * s));
        float label = Mathf.Round(LabelUnits * s);

        // the booster column, where it reaches into the row's band, narrows the row instead of pushing it down
        for (int i = 0; i < BoosterHud.MaxSlots; i++)
        {
            Rect b = BoosterHud.SlotRect(band, safe, screen, hud, i);
            if (b.width <= 0f || b.xMax <= g.left || b.yMax < startTop - want - label || b.yMin > startTop) continue;
            right = Mathf.Min(right, Mathf.Max(g.left, b.xMin - gap));
        }

        float avail = Mathf.Max(MaxSlots, right - g.left);
        float slotWidth = Mathf.Max(want, Mathf.Round(MinSlotUnits * s));
        float pitch = slotWidth + gap;
        if (pitch * MaxSlots - gap > avail)
        {
            pitch = Mathf.Floor((avail + gap) / MaxSlots);
            slotWidth = Mathf.Max(1f, pitch - gap);
        }
        g.pitch = pitch;
        g.slotWidth = slotWidth;
        g.icon = Mathf.Min(want, slotWidth);
        g.label = label;
        g.slotHeight = g.icon + g.label;
        float xMax = g.left + MaxSlots * pitch - gap;
        g.top = BoosterHud.ColumnTop(Obstacles(band, safe, screen, hud, boss, portal), g.left, xMax, startTop, gap);
        return g;
    }

    // Slot `index`'s screen-px rect (origin bottom-left): the coin on top, its level under it.
    public static Rect SlotRect(TopBand.Frame band, Rect safe, Vector2 screen, Rect hud, int index)
    {
        return SlotRect(Compute(band, safe, screen, hud), index);
    }

    public static Rect SlotRect(Geometry g, int index)
    {
        return new Rect(Mathf.Round(g.left + index * g.pitch), Mathf.Round(g.top - g.slotHeight), g.slotWidth, g.slotHeight);
    }

    // The coin's square inside a slot (top centre, whole px).
    public static Rect IconRect(Geometry g, Rect slot)
    {
        return new Rect(Mathf.Round(slot.center.x - g.icon * .5f), slot.yMax - g.icon, g.icon, g.icon);
    }

    // "x2"
    public static string LevelLabel(int level) { return "x" + Mathf.Clamp(level, 0, ShotAtomTuning.MaxLevel); }

    // ---- live ----

    sealed class Slot
    {
        public RectTransform root;
        public Image icon;
        public Text label;
        public ShotAtom atom;
        public int level = -1;
        public float pulse;      // 1 -> 0 after a level up: the coin swells a little
    }

    public const float PulseSeconds = .45f, PulseSwell = .4f;

    readonly Slot[] slots = new Slot[MaxSlots];
    Canvas canvas;
    CanvasGroup group;
    Font font;
    int shownVersion = -1, shownCount;
    int frame;
    float clock;

    Vector2 appliedScreen;
    Rect appliedSafe, appliedHud;
    TopBand.Frame appliedBand;
    bool placed;

    HudStyler styler;
    RectTransform hudRoot;
    Canvas hudCanvas;
    CanvasScaler hudScaler;
    int hudLookFrame = -1000;

    BossWarningHud warning;
    int warningLookFrame = -1000;
    bool bossUp, portalUp;
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
        var screen = new Vector2(Screen.width, Screen.height);
        Rect safe = Screen.safeArea;
        var band = TopBand.FrameFor(safe, screen);
        Rect hud = row.LiveHudRect(band, screen);
        row.RefreshWarnings();
        var g = Compute(band, safe, screen, hud, row.bossUp, row.portalUp);
        float gap = Mathf.Max(1f, Mathf.Round(GapUnits * PauseQuickActions.CanvasScaleFor(screen)));
        float cx = hud.height > 0f ? hud.center.x : (band.left + band.right) * .5f;
        topCentre = new Vector2(Mathf.Round(cx), Mathf.Round(g.top - g.slotHeight - gap));
        return true;
    }

    // Is the boss warning / PORTAL DANGER up (cached lookups, re-found twice a second)?
    void RefreshWarnings()
    {
        if (warning == null && Time.frameCount - warningLookFrame >= 30)
        {
            warningLookFrame = Time.frameCount;
            warning = FindFirstObjectByType<BossWarningHud>();
        }
        bossUp = warning != null && (warning.BannerVisible || warning.ChipVisible || warning.FinalVisible);
        var portal = PortalPressureHud.Instance;
        portalUp = portal != null && portal.Showing;
    }

    public int VisibleCount { get { return shownCount; } }
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
        canvas.sortingOrder = 88;      // under the booster column (89) and the quick actions (90)
        canvas.pixelPerfect = true;
        var scaler = holder.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;   // 1 canvas unit = 1 px: the rects are in screen pixels
        scaler.scaleFactor = 1f;
        group = holder.GetComponent<CanvasGroup>();
        group.blocksRaycasts = false;
        group.interactable = false;
        font = CodexUi.FindFont();

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
            ir.pivot = new Vector2(.5f, .5f);   // swells about its middle on a level up

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

    void Update()
    {
        int n = ShotAtoms.OrderCount;
        if (n > 0 && canvas == null) Build();
        if (canvas == null) return;

        if (shownVersion != ShotAtoms.Version)
        {
            shownVersion = ShotAtoms.Version;
            shownCount = n;
            for (int i = 0; i < MaxSlots; i++)
            {
                bool on = i < n;
                var slot = slots[i];
                if (on)
                {
                    var atom = ShotAtoms.OrderAt(i);
                    int level = ShotAtoms.Level(atom);
                    if (atom != slot.atom || level != slot.level) slot.pulse = 1f;
                    slot.atom = atom;
                    slot.level = level;
                    slot.label.text = LevelLabel(slot.level);
                    slot.icon.sprite = Frame(slot.atom);
                }
                if (slot.root.gameObject.activeSelf != on) slot.root.gameObject.SetActive(on);
            }
        }
        if (shownCount == 0) return;
        float dt = Time.unscaledDeltaTime;
        for (int i = 0; i < shownCount; i++)
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
            for (int i = 0; i < shownCount; i++) slots[i].icon.sprite = Frame(slots[i].atom);
        Place();
    }

    Sprite Frame(ShotAtom atom)
    {
        var frames = PickupArt.Frames(PickupArt.IdleName(ShotAtoms.KindOf(atom)), PickupArt.ShotIdleTicks.Length);
        return frames[Mathf.Min(frame, frames.Length - 1)];
    }

    // Re-laid only when the screen, safe area, band or read-out changes.
    void Place()
    {
        var screen = new Vector2(Screen.width, Screen.height);
        Rect safe = Screen.safeArea;
        var band = TopBand.FrameFor(safe, screen);
        Rect hud = LiveHudRect(band, screen);
        bool boss = bossUp, portal = portalUp;
        RefreshWarnings();
        if (placed && screen == appliedScreen && safe == appliedSafe && band.Same(appliedBand) && hud == appliedHud &&
            boss == bossUp && portal == portalUp) return;
        placed = true;
        appliedScreen = screen; appliedSafe = safe; appliedBand = band; appliedHud = hud;
        liveHud = hud;
        var g = Compute(band, safe, screen, hud, bossUp, portalUp);
        geometry = g;
        int fontSize = Mathf.Max(9, Mathf.RoundToInt(LabelUnits * PauseQuickActions.CanvasScaleFor(screen)));
        for (int i = 0; i < MaxSlots; i++)
        {
            Rect r = SlotRect(g, i);
            Rect ic = IconRect(g, r);
            var slot = slots[i];
            slot.root.anchoredPosition = r.position;
            slot.root.sizeDelta = r.size;
            var ir = slot.icon.rectTransform;
            ir.anchoredPosition = ic.position - r.position + ic.size * .5f;
            ir.sizeDelta = ic.size;
            var tr = slot.label.rectTransform;
            tr.anchoredPosition = Vector2.zero;
            tr.sizeDelta = new Vector2(0f, g.label);
            slot.label.fontSize = fontSize;
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
        return HudStyler.HudScreenRect(band, screen, HudStyler.HudCanvasScale(hudCanvas, hudScaler, screen), hudRoot.rect.size);
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
