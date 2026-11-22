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
// PLACEMENT (pure, Geometry / SlotRect, ShotAtomsTest): the row starts at the score board's left edge, a gap under its bottom, and
// is slid down under whatever else reaches over it on this screen (the Replay/Home pair, the boss warning's chip and banner, PORTAL
// DANGER's chip, the booster column's slots: BoosterHud.Obstacles). All six slots are always reserved, so a coin joining the row
// never moves anything. It follows ScreenFit's top band (rails, notches, cutouts, safe area), so tall phones, the Flip cover
// screen, foldables and tablets all get it; on a narrow band the coins shrink to fit.
public class ShotAtomHud : MonoBehaviour
{
    public const string HostName = "~ShotAtomHud";
    public const float IconUnits = 26f;        // a coin's size in quick-action canvas units
    public const float MinSlotUnits = 22f;     // wide enough for "x3"
    public const float LabelUnits = 11f;       // the level line under a coin
    public const float GapUnits = 4f;          // between two slots, and under the score board
    public const int MaxSlots = ShotAtomTuning.Count;

    public struct Geometry
    {
        public float left, top, icon, pitch, slotWidth, slotHeight, label;
    }

    // Every rect the row must stay clear of, in screen px: the Replay/Home pair, the boss warning's chip and banner, PORTAL DANGER's
    // chip, the score read-out and the booster column's three slots.
    public static Rect[] Obstacles(TopBand.Frame band, Rect safe, Vector2 screen, Rect hud)
    {
        var basic = BoosterHud.Obstacles(band, safe, screen, hud);
        var all = new Rect[basic.Length + BoosterHud.MaxSlots];
        for (int i = 0; i < basic.Length; i++) all[i] = basic[i];
        for (int i = 0; i < BoosterHud.MaxSlots; i++) all[basic.Length + i] = BoosterHud.SlotRect(band, safe, screen, hud, i);
        return all;
    }

    public static Geometry Compute(TopBand.Frame band, Rect safe, Vector2 screen, Rect hud)
    {
        var g = new Geometry();
        float s = PauseQuickActions.CanvasScaleFor(screen);
        float gap = Mathf.Max(1f, Mathf.Round(GapUnits * s));
        g.left = Mathf.Max(hud.height > 0f ? hud.xMin : band.left, band.left);
        float avail = Mathf.Max(MaxSlots, band.right - g.left);
        float want = Mathf.Max(8f, Mathf.Round(IconUnits * s));
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
        g.label = Mathf.Round(LabelUnits * s);
        g.slotHeight = g.icon + g.label;
        float startTop = (hud.height > 0f ? hud.yMin : band.top) - gap;
        float xMax = g.left + MaxSlots * pitch - gap;
        g.top = BoosterHud.ColumnTop(Obstacles(band, safe, screen, hud), g.left, xMax, startTop, gap);
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
    }

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

    public int VisibleCount { get { return shownCount; } }
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
            ir.pivot = Vector2.zero;

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
                    slot.atom = ShotAtoms.OrderAt(i);
                    slot.level = ShotAtoms.Level(slot.atom);
                    slot.label.text = LevelLabel(slot.level);
                    slot.icon.sprite = Frame(slot.atom);
                }
                if (slot.root.gameObject.activeSelf != on) slot.root.gameObject.SetActive(on);
            }
        }
        if (shownCount == 0) return;

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
        if (placed && screen == appliedScreen && safe == appliedSafe && band.Same(appliedBand) && hud == appliedHud) return;
        placed = true;
        appliedScreen = screen; appliedSafe = safe; appliedBand = band; appliedHud = hud;
        var g = Compute(band, safe, screen, hud);
        int fontSize = Mathf.Max(9, Mathf.RoundToInt(LabelUnits * PauseQuickActions.CanvasScaleFor(screen)));
        for (int i = 0; i < MaxSlots; i++)
        {
            Rect r = SlotRect(g, i);
            Rect ic = IconRect(g, r);
            var slot = slots[i];
            slot.root.anchoredPosition = r.position;
            slot.root.sizeDelta = r.size;
            var ir = slot.icon.rectTransform;
            ir.anchoredPosition = ic.position - r.position;
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
