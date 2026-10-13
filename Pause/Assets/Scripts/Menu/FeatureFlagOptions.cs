using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// The FEATURE FLAGS section of the Options screen (leaderboardS3): every flag in
// the FeatureFlags registry with its current state, tap a row to flip it. Shown
// only while developer mode is on (builds that offer it: DeveloperUnlocks.Available);
// for players the screen is exactly as before.
//
// The rows are discovered from FeatureFlags.All (reflection over the registry), so
// a new flag shows up here by itself. The list scrolls when it has more rows than fit.
//
// Built at runtime on the scene's own canvas, in the free space above the button
// stack (and the developer rows), like the Account card it replaced there; it
// scales down if a short screen leaves less room and never leaves the safe area.
public class FeatureFlagOptions : MonoBehaviour
{
    public const string CardName = "FlagsRow";
    public const float CardWidth = 640f, CardHeight = 300f;
    public const float CanvasUnitsWide = 800f;   // the Options canvas matches width at 800
    public const float TopMargin = 24f, Gap = 28f, Lift = 40f, MinScale = .55f;
    const float RowH = 64f, ListTop = 78f, ListHeight = 206f;

    public static FeatureFlagOptions Current { get; private set; }

    // ---- tests read these ----

    public RectTransform Card { get { return card; } }
    public ScrollRect List { get { return scroll; } }
    public bool Shown { get { return card != null && card.gameObject.activeSelf; } }
    public CardLayout AppliedLayout { get; private set; }
    public int RowCount { get { return rows.Count; } }

    public string RowName(int i) { return rows[i].flag; }
    public string RowLabel(int i) { return rows[i].label.text; }
    public string RowState(int i) { return OptionsUi.Caption(rows[i].button); }
    public Button RowButton(int i) { return rows[i].button; }

    sealed class Row
    {
        public string flag;
        public Text label;
        public Button button;
    }

    readonly List<Row> rows = new List<Row>();
    Font font;
    RectTransform canvasRect, card, content;
    ScrollRect scroll;
    bool shown;
    bool overridden;
    Vector2 screenSize;
    Rect safeArea;
    float rowsTop = float.NaN;

    // ---- bootstrap ----

    [RuntimeInitializeOnLoadMethod]
    static void Init()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (scene.name == "leaderboardS3" && DeveloperUnlocks.Available && FindFirstObjectByType<FeatureFlagOptions>() == null)
            new GameObject("~FeatureFlagOptions").AddComponent<FeatureFlagOptions>();
    }

    // The Options canvas: the one holding the Credits button.
    public const string OptionsCreditsButton = "CreditsButton";

    public static RectTransform FindOptionsCanvas()
    {
        var anchor = SceneUtil.FindAny(OptionsCreditsButton);
        return anchor != null ? anchor.transform.parent as RectTransform : null;
    }

    void Start()
    {
        if (card == null) Build(new Vector2(ScreenInfo.Width, ScreenInfo.Height), ScreenInfo.SafeArea, false);
    }

    // Tests pass an explicit screen and safe area.
    public void Build(Vector2 screen, Rect safe, bool overrideScreen = true)
    {
        canvasRect = FindOptionsCanvas();
        if (canvasRect == null) return;
        Current = this;
        overridden = overrideScreen;
        screenSize = screen;
        safeArea = safe;
        font = DockArt.FindSceneFont();
        BuildCard();
        Refresh();
    }

    void OnDestroy()
    {
        if (Current == this) Current = null;
        if (card != null)
        {
            if (Application.isPlaying) Destroy(card.gameObject);
            else DestroyImmediate(card.gameObject);
        }
    }

    // ---- build ----

    void BuildCard()
    {
        card = OptionsUi.Child(canvasRect, CardName);
        card.anchorMin = card.anchorMax = card.pivot = new Vector2(.5f, .5f);
        card.sizeDelta = new Vector2(CardWidth, CardHeight);

        var plate = CelShape.Add(card.gameObject, CelShape.Kind.Chamfer,
                                 AkiraPalette.WithAlpha(AkiraPalette.Night1, .96f), 4.5f)
                            .Shadow(AkiraPalette.Ink, new Vector2(8f, -8f));
        plate.cut = 24f;
        plate.raycastTarget = true;   // taps on the card don't reach the stars behind it

        // Tag slab: FEATURE FLAGS, developer amber (a developer-only section).
        var tag = OptionsUi.Place(card, "Tag", -8f, -14f, 330f, 50f);
        var slab = CelShape.Add(tag.gameObject, CelShape.Kind.Slab, AkiraPalette.Amber, 3.5f)
                           .Shadow(AkiraPalette.Ink, new Vector2(5f, -5f));
        slab.slant = 16f;
        var tagLabel = OptionsUi.Child(tag, "Label");
        OptionsUi.Stretch(tagLabel, 16f);
        OptionsUi.Label(tagLabel, font, "FEATURE FLAGS", 26, AkiraPalette.Ink, TextAnchor.MiddleCenter, 0f);

        // The scrolling list: a masked viewport over a content strip, one row per flag.
        var viewport = OptionsUi.Place(card, "Viewport", 20f, ListTop, CardWidth - 40f, ListHeight);
        var catcher = viewport.gameObject.AddComponent<Image>();
        catcher.color = new Color(0f, 0f, 0f, 0f);   // drags start anywhere in the list
        catcher.raycastTarget = true;
        viewport.gameObject.AddComponent<RectMask2D>();
        content = OptionsUi.Child(viewport, "Content");
        content.anchorMin = new Vector2(0f, 1f);
        content.anchorMax = new Vector2(1f, 1f);
        content.pivot = new Vector2(.5f, 1f);
        content.anchoredPosition = Vector2.zero;
        scroll = viewport.gameObject.AddComponent<ScrollRect>();
        scroll.viewport = viewport;
        scroll.content = content;
        scroll.horizontal = false;
        scroll.vertical = true;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.scrollSensitivity = 40f;

        var flags = FeatureFlags.All;
        content.sizeDelta = new Vector2(0f, Mathf.Max(ListHeight, flags.Length * RowH));
        for (int i = 0; i < flags.Length; i++)
        {
            var row = new Row { flag = flags[i].name };
            var line = OptionsUi.Child(content, "Flag_" + flags[i].name);
            line.anchorMin = new Vector2(0f, 1f);
            line.anchorMax = new Vector2(1f, 1f);
            line.pivot = new Vector2(.5f, 1f);
            line.sizeDelta = new Vector2(0f, RowH - 8f);
            line.anchoredPosition = new Vector2(0f, -i * RowH);
            var name = OptionsUi.Child(line, "Name");
            name.anchorMin = Vector2.zero;
            name.anchorMax = new Vector2(1f, 1f);
            name.offsetMin = new Vector2(8f, 0f);
            name.offsetMax = new Vector2(-170f, 0f);
            row.label = OptionsUi.Label(name, font, flags[i].label, 24, AkiraPalette.Bone, TextAnchor.MiddleLeft, 2.5f);
            string flag = flags[i].name;
            row.button = OptionsUi.MakeButton(line, "Toggle", font, 0f, 0f, 150f, RowH - 8f, "", AkiraPalette.Cyan, true,
                                              () => Toggle(flag), 24);
            var btn = (RectTransform)row.button.transform;
            btn.anchorMin = btn.anchorMax = new Vector2(1f, .5f);
            btn.pivot = new Vector2(1f, .5f);
            btn.anchoredPosition = new Vector2(-4f, 0f);
            rows.Add(row);
        }
    }

    // ---- state ----

    void Toggle(string flag)
    {
        FeatureFlags.Set(flag, !FeatureFlags.IsOn(flag));
        RefreshRows();
    }

    void RefreshRows()
    {
        foreach (var row in rows)
        {
            bool on = FeatureFlags.IsOn(row.flag);
            OptionsUi.SetButton(row.button, on ? "ON" : "OFF", on ? AkiraPalette.Cyan : AkiraPalette.Indigo1, true, true);
        }
    }

    public void Refresh()
    {
        if (card == null) return;
        shown = DeveloperUnlocks.Enabled;
        card.gameObject.SetActive(shown);
        RefreshRows();
        Relayout();
    }

    // ---- layout ----

    public struct CardLayout
    {
        public float centerY;      // canvas units, from the canvas centre
        public float scale;
        public float canvasHeight; // canvas units
        public float safeTop;      // highest y the card may reach
        public float safeBottom;
        public float rowsTop;      // top of the highest button/row under the card
    }

    // The card sits just above the button stack (rowsTop) and below the
    // safe area's top edge; it shrinks when that gap is too small.
    // `unitsPerPixel`: the Options canvas's units per screen pixel (0: taken
    // as CanvasUnitsWide across the screen, as the scene authors it).
    public static CardLayout ComputeLayout(Vector2 screen, Rect safe, float rowsTop, float cardHeight, float unitsPerPixel = 0f)
    {
        if (screen.x <= 0f || screen.y <= 0f) screen = new Vector2(1080f, 1920f);
        if (safe.width <= 0f || safe.height <= 0f) safe = new Rect(0f, 0f, screen.x, screen.y);
        float units = unitsPerPixel > 0f ? unitsPerPixel : CanvasUnitsWide / screen.x;
        float height = screen.y * units;
        float safeTop = height * .5f - (screen.y - safe.yMax) * units - TopMargin;
        float safeBottom = -height * .5f + safe.yMin * units;
        float safeWidth = safe.width * units;
        float bottom = rowsTop + Gap;
        float room = safeTop - bottom;
        float scale = Mathf.Min(1f, room / cardHeight, safeWidth * .94f / (CardWidth + 8f));
        scale = Mathf.Max(MinScale, scale);
        float h = cardHeight * scale;
        float center = Mathf.Min(bottom + Lift * scale + h * .5f, safeTop - h * .5f);
        center = Mathf.Max(center, bottom + h * .5f);
        return new CardLayout
        {
            centerY = center, scale = scale, canvasHeight = height,
            safeTop = safeTop + TopMargin, safeBottom = safeBottom, rowsTop = rowsTop,
        };
    }

    // Top of the highest other control on the Options canvas (the Credits /
    // Tutorial / Back stack and DeveloperOptions' rows).
    public float RowsTop()
    {
        float top = float.NegativeInfinity;
        if (canvasRect == null) return 0f;
        for (int i = 0; i < canvasRect.childCount; i++)
        {
            var rt = canvasRect.GetChild(i) as RectTransform;
            if (rt == null || rt == card) continue;
            if (rt.GetComponentInChildren<Button>(true) == null) continue;
            if (rt.anchorMin != rt.anchorMax || rt.anchorMin != new Vector2(.5f, .5f)) continue;
            top = Mathf.Max(top, rt.anchoredPosition.y + rt.sizeDelta.y * (1f - rt.pivot.y));
        }
        return float.IsNegativeInfinity(top) ? 0f : top;
    }

    // Canvas units per screen pixel on `screen`, from the Options canvas's
    // scaler (UiScale's floor included).
    public float UnitsPerPixel(Vector2 screen)
    {
        var root = canvasRect != null ? canvasRect.GetComponentInParent<Canvas>() : null;
        if (root == null || screen.x <= 0f) return 0f;
        root = root.rootCanvas;
        var scaler = root.GetComponent<CanvasScaler>();
        if (scaler == null || scaler.uiScaleMode != CanvasScaler.ScaleMode.ScaleWithScreenSize) return 0f;
        // a scaler switched off (the screen-fit rig sets the scale itself)
        float s = scaler.enabled ? HudStyler.HudCanvasScale(root, scaler, screen) : root.scaleFactor;
        return s > 0f ? 1f / s : 0f;
    }

    public void Relayout()
    {
        if (card == null) return;
        rowsTop = RowsTop();
        var layout = ComputeLayout(screenSize, safeArea, rowsTop, CardHeight, UnitsPerPixel(screenSize));
        AppliedLayout = layout;
        card.anchoredPosition = new Vector2(0f, layout.centerY);
        card.localScale = new Vector3(layout.scale, layout.scale, 1f);
    }

    void Update()
    {
        if (card == null) return;
        bool changed = false;
        if (!overridden && (ScreenInfo.Width != screenSize.x || ScreenInfo.Height != screenSize.y || ScreenInfo.SafeArea != safeArea))
        {
            screenSize = new Vector2(ScreenInfo.Width, ScreenInfo.Height);
            safeArea = ScreenInfo.SafeArea;
            changed = true;
        }
        if (DeveloperUnlocks.Enabled != shown) { Refresh(); return; }
        if (changed || !Mathf.Approximately(RowsTop(), rowsTop)) Relayout();
    }
}
