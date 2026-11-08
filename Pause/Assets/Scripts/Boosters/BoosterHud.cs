using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// The run's ACTIVE BOOSTER icons: a small column at the right edge, one icon per booster that is on right now
// (BoosterHudSource: Dust x2, Homing Atoms, Hazard Clear), each the Shop's own pixel-art icon (Resources/ShopIcons) with a
// small countdown ("9:59") under a timed one and a count ("x2") under Hazard Clear. Icons come and go as boosters start
// and run out; the countdowns are the booster clocks themselves, so they stop while the world is frozen.
//
// NOT a control: no Button, no GraphicRaycaster, every graphic has raycastTarget off and the canvas group blocks nothing,
// so a finger on it is an ordinary press (teleport / resume). Built on gameS1 only (never the tutorial).
//
// PLACEMENT (pure, SlotRect, BoostersTest): at the right end of the top band, under the Replay/Home pair
// (PauseQuickActions.ScreenRectFor) and under anything else of the HUD that reaches over the column on this screen: the
// boss warning's chip and banner (BossWarningHud.ComputeLayout; reserved even while they are down, so the icons never jump),
// PORTAL DANGER's chip and the score read-out (Obstacles). It never reaches below LowestShare of the screen: a column that
// would wraps into a second column to its left. It follows ScreenFit's top band (rails, notches, cutouts, safe area), so
// tall phones, the Flip cover screen, foldables and tablets all get it.
//
// LOOK. The 256 px Shop icons are a 64 px pixel grid upscaled 4x, so the icon is drawn at a whole multiple of 64 screen px
// (half of that on a very narrow screen), point filtered: every art pixel lands on whole screen pixels (crisp, no shimmer).
public class BoosterHud : MonoBehaviour
{
    public const string HostName = "~BoosterHud";
    public const float IconUnits = 30f;        // the icon's target size in quick-action canvas units (snapped to ArtGrid px)
    public const int ArtGrid = 64;             // the Shop icons' own pixel grid
    public const float LabelUnits = 12f;       // the countdown / count line under the icon
    public const float SlotWidthUnits = 44f;   // wide enough for "60:00"
    public const float SlotGap = 4f;           // between two slots
    public const float TopGap = 6f;            // under whatever is above the column
    // Never lower than this share of the screen's height: the bottom third (the ship, its hearts, the thumb) is left alone.
    // Every phone and tablet keeps the column in its top ~third; the near-square Flip cover screen needs the room.
    public const float LowestShare = .35f;

    public const int MaxSlots = 3;

    public const float MaxWidthShare = .1f;    // an icon is never wider than this share of the screen

    // The icon's size in screen px: IconUnits at this screen's quick-action scale, snapped to a whole multiple of ArtGrid.
    // On a screen too narrow for one ArtGrid square (a low-dpi 480 px phone) it is half a grid: every other art pixel, still
    // one screen pixel each.
    public static float IconPx(Vector2 screen)
    {
        float want = IconUnits * PauseQuickActions.CanvasScaleFor(screen);
        int px = Mathf.Max(1, Mathf.RoundToInt(want / ArtGrid)) * ArtGrid;
        while (px > ArtGrid && px > screen.x * MaxWidthShare) px -= ArtGrid;
        if (px > screen.x * MaxWidthShare) px = ArtGrid / 2;
        return px;
    }

    // What the column must never meet, in screen px: the Replay/Home pair, the boss warning's chip and banner (where this
    // screen puts them, up or not: they come and go in the 30 s before a boss), PORTAL DANGER's chip, and the score read-out.
    public static Rect[] Obstacles(TopBand.Frame band, Rect safe, Vector2 screen, Rect hud)
    {
        var warning = BossWarningHud.ComputeLayout(safe, screen, hud, band);
        float portalScale = Mathf.Min(screen.x / 800f, screen.y / 1200f);   // its canvas: 800x1200, Expand
        return new[]
        {
            PauseQuickActions.ScreenRectFor(band, screen),
            warning.chip,
            warning.banner,
            PortalPressureHud.ChipScreenRect(band, portalScale > 0f ? portalScale : 1f),
            hud,
        };
    }

    // The top edge of a column spanning [xMin, xMax]: under every obstacle that reaches over that span (they are stacked
    // top to bottom, so a few passes settle it).
    public static float ColumnTop(Rect[] obstacles, float xMin, float xMax, float startTop, float gap)
    {
        float top = startTop;
        for (int pass = 0; pass < obstacles.Length; pass++)
        {
            bool moved = false;
            foreach (var o in obstacles)
            {
                if (o.width <= 0f || o.height <= 0f) continue;
                if (o.xMax <= xMin || o.xMin >= xMax) continue;   // beside the column
                if (o.yMin - gap >= top) continue;                // already above it
                top = o.yMin - gap;
                moved = true;
            }
            if (!moved) break;
        }
        return top;
    }

    // The label line's height in whole px.
    public static float LabelPx(Vector2 screen)
    {
        return Mathf.Round(LabelUnits * PauseQuickActions.CanvasScaleFor(screen));
    }

    static float ColumnRight(TopBand.Frame band, Vector2 screen)
    {
        return PauseQuickActions.ScreenRectFor(band, screen).xMax - PauseQuickActions.PanelPad * PauseQuickActions.CanvasScaleFor(screen);
    }

    static float SlotWidthPx(Vector2 screen)
    {
        return Mathf.Round(Mathf.Max(IconPx(screen), SlotWidthUnits * PauseQuickActions.CanvasScaleFor(screen)));
    }

    // Slot `index`'s screen-px rect (origin bottom-left): the icon on top, the label under it. `hud` is the score read-out's
    // screen rect (zero-sized when there is none).
    public static Rect SlotRect(TopBand.Frame band, Rect safe, Vector2 screen, Rect hud, int index)
    {
        float s = PauseQuickActions.CanvasScaleFor(screen);
        float icon = IconPx(screen), width = SlotWidthPx(screen), gap = SlotGap * s;
        float height = icon + LabelPx(screen), step = height + gap;
        float lowest = Mathf.Max(screen.y * LowestShare, safe.yMin);
        var obstacles = Obstacles(band, safe, screen, hud);
        float right = ColumnRight(band, screen);
        float startTop = PauseQuickActions.ScreenRectFor(band, screen).yMax;
        for (int col = 0; ; col++)
        {
            float top = ColumnTop(obstacles, right - width, right, startTop, TopGap * s);
            int fits = Mathf.FloorToInt((top - lowest + gap) / step);
            if (index < fits || col >= MaxSlots - 1 || right - 2f * width - gap < band.left)
            {
                float y = top - index * step - height;
                return new Rect(Mathf.Round(right - width), Mathf.Round(y), width, height);
            }
            index -= Mathf.Max(0, fits);
            right -= width + gap;
        }
    }

    // The icon's square inside a slot (top centre, whole px).
    public static Rect IconRect(Rect slot, Vector2 screen)
    {
        float icon = IconPx(screen);
        return new Rect(Mathf.Round(slot.center.x - icon * .5f), slot.yMax - icon, icon, icon);
    }

    // ---- live ----

    sealed class Slot
    {
        public RectTransform root;
        public Image icon;
        public Text label;
        public Boosters.Kind kind;
        public string text;
        public int value;
        public bool armed = true;
    }

    readonly List<BoosterHudSource.Entry> entries = new List<BoosterHudSource.Entry>(MaxSlots);
    readonly Slot[] slots = new Slot[MaxSlots];
    Canvas canvas;
    CanvasGroup group;
    Font font;
    int shownCount = -1;
    readonly Boosters.Kind[] shownKinds = new Boosters.Kind[MaxSlots];

    Vector2 appliedScreen;
    Rect appliedSafe, appliedHud;
    TopBand.Frame appliedBand;
    bool placed;

    HudStyler styler;
    RectTransform hudRoot;
    Canvas hudCanvas;
    CanvasScaler hudScaler;
    int hudLookFrame = -1000;

    public int VisibleCount { get { return shownCount < 0 ? 0 : shownCount; } }

    void Build()
    {
        var holder = new GameObject("BoosterHudCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(CanvasGroup));
        holder.transform.SetParent(transform, false);
        canvas = holder.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 89;      // under the quick actions (90)
        canvas.pixelPerfect = true;    // whole-pixel icons
        var scaler = holder.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;   // 1 canvas unit = 1 px: SlotRect is in screen pixels
        scaler.scaleFactor = 1f;
        group = holder.GetComponent<CanvasGroup>();
        group.blocksRaycasts = false;
        group.interactable = false;
        font = CodexUi.FindFont();

        for (int i = 0; i < MaxSlots; i++)
        {
            var slot = new Slot();
            var go = new GameObject("BoosterSlot" + i, typeof(RectTransform));
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
        int n = BoosterHudSource.Read(entries);
        if (n > 0 && canvas == null) Build();
        if (canvas == null) return;

        bool kindsChanged = n != shownCount;
        for (int i = 0; i < n && !kindsChanged; i++) kindsChanged = shownKinds[i] != entries[i].kind;
        if (kindsChanged)
        {
            shownCount = n;
            for (int i = 0; i < MaxSlots; i++)
            {
                bool on = i < n;
                if (on)
                {
                    shownKinds[i] = entries[i].kind;
                    slots[i].kind = entries[i].kind;
                    slots[i].icon.sprite = ShopIconArt.Get(BoosterHudSource.IconName(entries[i].kind));
                    slots[i].text = null;
                }
                if (slots[i].root.gameObject.activeSelf != on) slots[i].root.gameObject.SetActive(on);
            }
        }
        if (n == 0) return;

        Place();
        for (int i = 0; i < n; i++)
        {
            var e = entries[i];
            var slot = slots[i];
            // re-formatted only when the shown second / count changes (no per-frame string)
            int value = e.timed ? Mathf.CeilToInt(Mathf.Max(0f, e.secondsLeft)) : -1 - e.count;
            if (value != slot.value || slot.text == null) { slot.value = value; slot.text = e.Label; slot.label.text = slot.text; }
            if (e.armed != slot.armed)
            {
                slot.armed = e.armed;
                float a = e.armed ? 1f : .45f;
                slot.icon.color = new Color(1f, 1f, 1f, a);
                slot.label.color = AkiraPalette.WithAlpha(AkiraPalette.Bone, a);
            }
        }
    }

    // Every slot is laid out at once, re-laid only when the screen, safe area, band or read-out changes.
    void Place()
    {
        var screen = new Vector2(Screen.width, Screen.height);
        Rect safe = Screen.safeArea;
        var band = TopBand.FrameFor(safe, screen);
        Rect hud = LiveHudRect(band, screen);
        if (placed && screen == appliedScreen && safe == appliedSafe && band.Same(appliedBand) && hud == appliedHud) return;
        placed = true;
        appliedScreen = screen; appliedSafe = safe; appliedBand = band; appliedHud = hud;
        float s = PauseQuickActions.CanvasScaleFor(screen);
        int fontSize = Mathf.Max(10, Mathf.RoundToInt(LabelUnits * s));
        for (int i = 0; i < MaxSlots; i++)
        {
            Rect r = SlotRect(band, safe, screen, hud, i);
            Rect ic = IconRect(r, screen);
            var slot = slots[i];
            slot.root.anchoredPosition = r.position;
            slot.root.sizeDelta = r.size;
            var ir = slot.icon.rectTransform;
            ir.anchoredPosition = ic.position - r.position;
            ir.sizeDelta = ic.size;
            var tr = slot.label.rectTransform;
            tr.anchoredPosition = Vector2.zero;
            tr.sizeDelta = new Vector2(0f, LabelPx(screen));
            slot.label.fontSize = fontSize;
        }
    }

    // The score read-out's screen rect (zero-sized when there is none), as BossWarningHud reads it.
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

// Builds the column when the run scene loads.
public static class BoosterHudBootstrap
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
        if (GameObject.Find(BoosterHud.HostName) != null) return;
        new GameObject(BoosterHud.HostName).AddComponent<BoosterHud>();
    }
}
