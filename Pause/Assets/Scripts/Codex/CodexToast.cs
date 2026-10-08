using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// "NEW CODEX ENTRY" toast: a small angular ink-outlined cel that drops in under the
// top of the safe area during a run the first time something is discovered,
// then fades away. It never blocks play -- no raycaster on its canvas and no
// raycast targets -- and queues a few names if several arrive at once.
//
// Runs on unscaled time (the world freezes at timeScale 0 between touches)
// and allocates nothing per frame; it switches itself off when idle.
public class CodexToast : MonoBehaviour
{
    public const float Width = 460f, Height = 76f;
    public const float TopMargin = 236f;   // clears the HUD stat panel (top-left)
    public const float InDuration = .25f, HoldDuration = 2.2f, OutDuration = .35f;
    const int QueueSize = 4;

    static CodexToast instance;

    RectTransform box;
    CanvasGroup group;
    Image icon;
    Text heading, nameText;
    Canvas canvas;

    // A queued toast: a codex entry, or an announcement (Announce).
    struct Item
    {
        public string heading, name;
        public Sprite sprite;
        public CodexEntry entry;
    }
    public const string EntryHeading = "NEW CODEX ENTRY";
    readonly Item[] queue = new Item[QueueSize];
    int queueHead, queueCount;
    float shownAt = -1f;

    public static CodexToast Current { get { return instance; } }
    public bool Showing { get { return shownAt >= 0f; } }
    public string ShowingName { get { return nameText != null ? nameText.text : null; } }
    public string ShowingHeading { get { return heading != null ? heading.text : null; } }

    [RuntimeInitializeOnLoadMethod]
    static void Init()
    {
        Codex.Discovered -= OnDiscovered;
        Codex.Discovered += OnDiscovered;
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    // Builds the (hidden) toast as a run loads, so a first discovery -- often
    // a first pickup -- doesn't build a canvas mid-flight.
    static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (scene.name == "gameS1" && instance == null) instance = Build();
    }

    static void OnDiscovered(CodexEntry entry)
    {
        // Only during a real run; the tutorial never discovers anything.
        if (entry == null || !Application.isPlaying || SceneManager.GetActiveScene().name != "gameS1") return;
        if (instance == null) instance = Build();
        instance.Enqueue(entry);
    }

    public static CodexToast Build()
    {
        var canvas = CodexUi.NewOverlayCanvas("~CodexToast", 640, false, 0f, CodexUi.TextUnits);
        var toast = canvas.gameObject.AddComponent<CodexToast>();
        toast.canvas = canvas;
        toast.BuildUi(CodexUi.FindFont());
        toast.gameObject.SetActive(false);
        return toast;
    }

    void BuildUi(Font font)
    {
        var go = new GameObject("Toast", typeof(RectTransform), typeof(CanvasGroup));
        go.transform.SetParent(transform, false);
        box = (RectTransform)go.transform;
        box.anchorMin = box.anchorMax = new Vector2(.5f, 1f);
        box.pivot = new Vector2(.5f, 1f);
        box.sizeDelta = new Vector2(Width, Height);
        group = go.GetComponent<CanvasGroup>();
        group.interactable = false;
        group.blocksRaycasts = false;

        var bg = CodexUi.NewImage("Background", box, CodexUi.CodexSprite("cx_toast"), Color.white, true);
        CodexUi.Stretch(bg.rectTransform);

        const float iconSize = 50f;
        icon = CodexUi.NewImage("Icon", box, null, Color.white);
        icon.preserveAspect = true;
        CodexUi.Place(icon.rectTransform, CodexUi.Centered(-Width * .5f + 34f + iconSize * .5f, 0f, iconSize, iconSize));

        float left = -Width * .5f + 34f + iconSize + 14f;
        float textWidth = Width * .5f - 24f - left;
        heading = CodexUi.NewText("Heading", box, font, EntryHeading, 15, CodexUi.Accent, TextAnchor.MiddleLeft);
        CodexUi.Place(heading.rectTransform, new Rect(left, 2f, textWidth, 24f));
        nameText = CodexUi.NewText("Name", box, font, "", 24, CodexUi.Title, TextAnchor.MiddleLeft);
        nameText.horizontalOverflow = HorizontalWrapMode.Wrap;
        nameText.verticalOverflow = VerticalWrapMode.Truncate;
        nameText.resizeTextForBestFit = true;
        nameText.resizeTextMinSize = 14;
        nameText.resizeTextMaxSize = 24;
        CodexUi.AddOutline(nameText.gameObject, CodexUi.Ink, 1.5f);
        CodexUi.Place(nameText.rectTransform, new Rect(left, -30f, textWidth, 32f));
    }

    // A one-off announcement in the toast's style, in any scene (the dock's
    // ALL SKINS: +2 HEARTS): `heading` over `name`, with `icon`.
    public static void Announce(string heading, string name, Sprite icon)
    {
        if (instance == null) instance = Build();
        instance.Push(new Item { heading = heading, name = name, sprite = icon });
    }

    public void Enqueue(CodexEntry entry)
    {
        if (entry == null) return;
        Push(new Item { heading = EntryHeading, entry = entry });
    }

    void Push(Item item)
    {
        if (queueCount >= QueueSize) return;   // a flood of firsts: the rest are in the codex anyway
        queue[(queueHead + queueCount) % QueueSize] = item;
        queueCount++;
        if (!gameObject.activeSelf) gameObject.SetActive(true);
        if (shownAt < 0f) Next();
    }

    void Next()
    {
        if (queueCount == 0)
        {
            shownAt = -1f;
            gameObject.SetActive(false);
            return;
        }
        var item = queue[queueHead];
        queue[queueHead] = default(Item);
        queueHead = (queueHead + 1) % QueueSize;
        queueCount--;

        heading.text = item.heading;
        nameText.text = item.entry != null ? item.entry.name : item.name;
        if (warning == null) warning = FindFirstObjectByType<BossWarningHud>();
        icon.sprite = item.entry != null ? item.entry.Sprite : item.sprite;
        icon.enabled = icon.sprite != null;
        shownAt = Time.unscaledTime;
        ApplyAt(0f);
    }

    void Update()
    {
        if (shownAt < 0f) return;
        float t = Time.unscaledTime - shownAt;
        if (t >= InDuration + HoldDuration + OutDuration) { Next(); return; }
        ApplyAt(t);
    }

    public void ApplyAt(float t)
    {
        float a;
        if (t < InDuration) a = CodexUi.EaseOutCubic(t / InDuration);
        else if (t < InDuration + HoldDuration) a = 1f;
        else a = 1f - Mathf.Clamp01((t - InDuration - HoldDuration) / OutDuration);
        group.alpha = a;

        // Drop in from just above, under the top of the safe area.
        float safeTop = SafeTopInset();
        float slide = (1f - CodexUi.EaseOutCubic(t / InDuration)) * 24f;
        float sf = Mathf.Max(canvas.scaleFactor, .0001f);
        bool warn = warning != null;
        var portal = PortalPressureHud.Instance;
        float top = TopOffset(safeTop, ScreenInfo.Height, sf,
            warn && warning.BannerVisible ? warning.CurrentLayout.banner : default(Rect),
            warn && warning.ChipVisible ? warning.CurrentLayout.chip : default(Rect),
            portal != null && portal.Showing ? portal.ChipRect : default(Rect));
        box.anchoredPosition = new Vector2(0f, -UnderCard(top, ScreenInfo.Height, sf, WorldBanner.ScreenRect) + slide);
    }

    // A centre-screen card (WorldBanner: ENTER THE PORTAL, a world's name)
    // that the toast at `top` would run into: the toast drops in under it.
    public static float UnderCard(float top, float screenH, float sf, Rect card)
    {
        if (card.height <= 0f) return top;
        float toastTop = screenH - top * sf, toastBottom = toastTop - Height * sf;
        if (toastBottom >= card.yMax || toastTop <= card.yMin) return top;
        return (screenH - card.yMin) / sf + WarningGap;
    }

    // BOSS INCOMING (BossWarningHud) hangs its banner centred under the top
    // band for the warning's first ~2.6 s, and its countdown chip under the
    // quick actions for the rest of it -- where this toast sits on most
    // phones. While either is up, the toast drops in under it instead of
    // over it. The same goes for PORTAL DANGER's chip (PortalPressureHud),
    // centred under the band while an open portal is kept waiting.
    public const float WarningGap = 12f;
    BossWarningHud warning;

    // The toast's top, in canvas units below the screen's top: TopMargin
    // under the safe area, or under the visible warning pieces (screen px
    // rects; an empty rect = not showing).
    public static float TopOffset(float safeTopUnits, float screenH, float sf, Rect banner, Rect chip)
    {
        return TopOffset(safeTopUnits, screenH, sf, banner, chip, default(Rect));
    }

    public static float TopOffset(float safeTopUnits, float screenH, float sf, Rect banner, Rect chip, Rect portalChip)
    {
        float top = safeTopUnits + TopMargin;
        if (banner.height > 0f) top = Mathf.Max(top, (screenH - banner.yMin) / sf + WarningGap);
        if (chip.height > 0f) top = Mathf.Max(top, (screenH - chip.yMin) / sf + WarningGap);
        if (portalChip.height > 0f) top = Mathf.Max(top, (screenH - portalChip.yMin) / sf + WarningGap);
        // the read-out, when a small phone stacks it under the quick actions
        Rect readout = HudStyler.StackedReadout;
        if (readout.height > 0f) top = Mathf.Max(top, (screenH - readout.yMin) / sf + WarningGap);
        return top;
    }

    float SafeTopInset()
    {
        float sf = Mathf.Max(canvas.scaleFactor, .0001f);
        return (ScreenInfo.Height - ScreenInfo.SafeArea.yMax) / sf;
    }
}
