using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// The full-screen review of one unlocked backdrop, opened by tapping its card in the codex BACKDROPS tab.
//
// WHAT IT SHOWS  The REAL in-game backdrop: a BackdropSet of the world's chosen variant with its atlases
//                and director (set pieces, ambient loops, weather, Storm's lightning under StormLightning's
//                own photosensitivity caps), scrolled slowly like a run's first seconds
//                (WorldBackdrop.ScrollVelocity(0)) on unscaled time. No bespoke art and no UI boxes around it:
//                a title, the world and position, variant dots, two arrows and a BACK button.
// HOW            The set lives off to the side of the world (OffsetX) and is drawn by the viewer's own
//                orthographic camera into a screen-sized RenderTexture shown full screen on the viewer's
//                overlay canvas. The menu backdrop, the scene and the codex panel underneath are not touched
//                (the opaque image hides and blocks them).
// STEPPING       Swipe left = next, swipe right = previous (also the on-screen arrows, and the arrow keys on a
//                keyboard) through every unlocked backdrop in world order, wrapping at the ends. A step dips
//                through black (the old set is destroyed before the next is built, so only one is ever alive).
// BACK           BACK / Escape / Android back closes it (BackNavigator layer above the codex); the codex list
//                is still there. Closing destroys the set, the camera, the RenderTexture and the canvas.
public sealed class CodexBackdropViewer : MonoBehaviour
{
    // ---- constants ----
    public const float OffsetX = 4000f;          // where the set sits in the world, far from the scene
    public const int MaxRenderEdge = 1920;       // the RenderTexture's longer edge, in pixels
    public const float DipSeconds = .16f;        // black dip out, then the same back in
    public const float Margin = 16f;
    public const float BackWidth = 288f, BackHeight = 100f;   // as the codex's own BACK: >= 48 dp tall
    public const float ArrowHit = 120f, ArrowIcon = 64f;
    public const float DotSize = 22f, DotGap = 16f;
    public const float SwipeMinFraction = .12f, SwipeMinPixels = 60f, SwipeAxisRatio = 1.4f;
    public const int SortingOrder = 600;         // above the codex panel (500)

    public struct Layout
    {
        public Rect back, title, sub, dots, prev, next;
        public Rect shade;     // bottom gradient behind the text
    }

    // Pure: where the controls go for a safe area (canvas units, centre-origin).
    public static Layout ComputeLayout(Rect safe)
    {
        var l = new Layout();
        float left = safe.xMin + Margin, right = safe.xMax - Margin, bottom = safe.yMin + Margin, top = safe.yMax - Margin;
        float w = Mathf.Max(0f, right - left);
        l.back = new Rect(left, top - BackHeight, Mathf.Min(BackWidth, w), BackHeight);
        float dotsW = 4f * DotSize + 3f * DotGap;
        l.dots = CodexUi.Centered(safe.center.x, bottom + DotSize * .5f + 4f, dotsW + 40f, DotSize + 8f);
        l.sub = CodexUi.Centered(safe.center.x, l.dots.yMax + 6f + 16f, w, 32f);
        l.title = CodexUi.Centered(safe.center.x, l.sub.yMax + 4f + 34f, w, 68f);
        float midY = safe.center.y;
        l.prev = CodexUi.Centered(left + ArrowHit * .5f, midY, ArrowHit, ArrowHit * 1.3f);
        l.next = CodexUi.Centered(right - ArrowHit * .5f, midY, ArrowHit, ArrowHit * 1.3f);
        l.shade = new Rect(safe.xMin - 400f, safe.yMin - 400f, safe.width + 800f, 400f + (l.title.yMax - safe.yMin) + 90f);
        return l;
    }

    // Pure: what a finger travelling `delta` pixels on a screen `screenWidth` pixels wide means:
    // +1 = next (swiped left), -1 = previous (swiped right), 0 = not a swipe.
    public static int SwipeDirection(Vector2 delta, float screenWidth)
    {
        float need = Mathf.Max(SwipeMinPixels, screenWidth * SwipeMinFraction);
        if (Mathf.Abs(delta.x) < need) return 0;
        if (Mathf.Abs(delta.x) < Mathf.Abs(delta.y) * SwipeAxisRatio) return 0;
        return delta.x < 0f ? 1 : -1;
    }

    // ---- opening ----

    static CodexBackdropViewer instance;
    public static CodexBackdropViewer Current { get { return instance; } }
    public static bool IsOpen { get { return instance != null; } }
    public static event Action Closed;

    // Opens the viewer on `start` (must be listed and unlocked). Null when it cannot be shown.
    public static CodexBackdropViewer Open(Font font, CodexBackdrop start)
    {
        if (start == null || !CodexBackdrops.IsListed(start) || !CodexBackdrops.IsUnlocked(start)) return null;
        if (instance != null) instance.Close();
        var canvas = CodexUi.NewOverlayCanvas("CodexBackdropViewer", SortingOrder, true);
        var viewer = canvas.gameObject.AddComponent<CodexBackdropViewer>();
        viewer.canvas = canvas;
        viewer.font = font != null ? font : CodexUi.FindFont();
        instance = viewer;
        viewer.Begin(start);
        return viewer;
    }

    // ---- state ----

    Canvas canvas;
    Font font;
    RectTransform root;
    RawImage view;
    Image fade, shade;
    Texture2D shadeTexture;
    Text title, sub;
    Button backBtn, prevBtn, nextBtn;
    RectTransform backRt, prevRt, nextRt, titleRt, subRt, dotsRt;
    Image[] dots;
    Image prevIcon, nextIcon;

    GameObject worldGo;
    Camera cam;
    RenderTexture rt;
    BackdropSet set;
    int rtW, rtH, lastW, lastH;
    Rect lastSafe;

    List<CodexBackdrop> sequence = new List<CodexBackdrop>();
    int index;
    float fadeAt = -10f;           // start of the dip
    int pendingIndex = -1;         // the step the dip is carrying out
    bool closed;

    public CodexBackdrop CurrentBackdrop { get { return index >= 0 && index < sequence.Count ? sequence[index] : null; } }
    public int Index { get { return index; } }
    public int Count { get { return sequence.Count; } }
    public IList<CodexBackdrop> Sequence { get { return sequence; } }
    public BackdropSet Set { get { return set; } }
    public Camera ViewCamera { get { return cam; } }
    public RenderTexture Target { get { return rt; } }
    public Text Title { get { return title; } }
    public Text Sub { get { return sub; } }
    public Button BackButton { get { return backBtn; } }
    public Button PrevButton { get { return prevBtn; } }
    public Button NextButton { get { return nextBtn; } }
    public Image[] Dots { get { return dots; } }
    public RectTransform BackRect { get { return backRt; } }
    public RectTransform PrevRect { get { return prevRt; } }
    public RectTransform NextRect { get { return nextRt; } }
    public RectTransform TitleRect { get { return titleRt; } }
    public RectTransform SubRect { get { return subRt; } }
    public RectTransform DotsRect { get { return dotsRt; } }
    public RawImage ViewImage { get { return view; } }
    public Layout CurrentLayout { get; private set; }
    public bool Dipping { get { return pendingIndex >= 0; } }
    public float SecondsShown { get; private set; }

    // ---- building ----

    void Begin(CodexBackdrop start)
    {
        sequence = CodexBackdrops.UnlockedList();
        index = Mathf.Max(0, sequence.FindIndex(b => b.id == start.id));

        root = (RectTransform)transform;
        BuildView();
        BuildHud();
        BackNavigator.Register(this, OnBackPressed);
        EnsureTarget();
        Fit();
        Build(sequence[index]);
        Paint();
        RenderFrame();
    }

    void BuildView()
    {
        view = new GameObject("View", typeof(RectTransform), typeof(RawImage)).GetComponent<RawImage>();
        view.transform.SetParent(root, false);
        CodexUi.Stretch(view.rectTransform);
        view.color = Color.white;
        view.raycastTarget = true;   // opaque: hides the codex and the scene, and takes the swipe
        view.gameObject.AddComponent<CodexBackdropSwipe>().viewer = this;

        shadeTexture = new Texture2D(1, 64, TextureFormat.RGBA32, false) { name = "CodexBackdropShade", wrapMode = TextureWrapMode.Clamp };
        for (int y = 0; y < 64; y++)
        {
            float k = 1f - y / 63f;                       // y 0 = bottom: darkest
            shadeTexture.SetPixel(0, y, new Color(.02f, .02f, .05f, .66f * k * k));
        }
        shadeTexture.Apply();
        var shadeGo = new GameObject("Shade", typeof(RectTransform), typeof(RawImage));
        shadeGo.transform.SetParent(root, false);
        var shadeRaw = shadeGo.GetComponent<RawImage>();
        shadeRaw.texture = shadeTexture;
        shadeRaw.raycastTarget = false;
        shadeRt = shadeRaw.rectTransform;

        fade = CodexUi.NewImage("Dip", root, null, new Color(0f, 0f, 0f, 0f));
        CodexUi.Stretch(fade.rectTransform);
        fade.raycastTarget = false;
    }

    RectTransform shadeRt;

    void BuildHud()
    {
        title = CodexUi.NewText("Title", root, font, "", 46, CodexUi.Body, TextAnchor.MiddleCenter, true);
        title.verticalOverflow = VerticalWrapMode.Truncate;
        title.resizeTextForBestFit = true;
        title.resizeTextMinSize = 22;
        title.resizeTextMaxSize = 46;
        title.fontStyle = FontStyle.BoldAndItalic;
        CodexUi.AddOutline(title.gameObject, CodexUi.Ink, 3f);
        titleRt = title.rectTransform;

        sub = CodexUi.NewText("Sub", root, font, "", 22, CodexUi.Title, TextAnchor.MiddleCenter, true);
        sub.verticalOverflow = VerticalWrapMode.Truncate;
        sub.resizeTextForBestFit = true;
        sub.resizeTextMinSize = 12;
        sub.resizeTextMaxSize = 22;
        CodexUi.AddOutline(sub.gameObject, CodexUi.Ink, 2f);
        subRt = sub.rectTransform;

        dotsRt = CodexUi.NewRect("Dots", root);
        dots = new Image[BackdropVariants.MaxVariants];
        for (int i = 0; i < dots.Length; i++)
        {
            dots[i] = CodexUi.NewImage("Dot" + i, dotsRt, CodexUi.CodexSprite("cx_circle"), Color.white);
            dots[i].raycastTarget = false;
        }

        // BACK: the codex's own cel button, smaller
        var backSlot = CodexUi.NewRect("BackSlot", root);
        backRt = backSlot;
        var frame = CodexUi.NewImage("BackButton", backSlot, CodexUi.CodexSprite("cx_button"), Color.white, true);
        frame.raycastTarget = true;
        CodexUi.Stretch(frame.rectTransform);
        var edge = CodexUi.NewImage("Edge", frame.rectTransform, CodexUi.CodexSprite("cx_button_edge"), CodexUi.Accent, true);
        CodexUi.Stretch(edge.rectTransform);
        backBtn = frame.gameObject.AddComponent<Button>();
        backBtn.targetGraphic = frame;
        var colors = backBtn.colors;
        colors.pressedColor = CodexUi.Select;
        colors.fadeDuration = .08f;
        backBtn.colors = colors;
        backBtn.onClick.AddListener(Close);
        var label = CodexUi.NewText("Label", frame.rectTransform, font, "BACK", 26, CodexUi.Body, TextAnchor.MiddleLeft);
        CodexUi.AddOutline(label.gameObject, CodexUi.Ink, 2f);
        const float iconSize = 36f, gap = 10f;
        float textWidth = Mathf.Ceil(label.preferredWidth);
        float x = -(textWidth + iconSize + gap) * .5f;
        var icon = CodexUi.NewImage("Icon", frame.rectTransform, CodexUi.CodexSprite("cx_back"), Color.white);
        icon.preserveAspect = true;
        CodexUi.Place(icon.rectTransform, CodexUi.Centered(x + iconSize * .5f, 0f, iconSize, iconSize));
        CodexUi.Place(label.rectTransform, new Rect(x + iconSize + gap, -24f, textWidth + 4f, 48f));

        prevBtn = BuildArrow("Prev", false, out prevRt, out prevIcon);
        nextBtn = BuildArrow("Next", true, out nextRt, out nextIcon);
    }

    Button BuildArrow(string name, bool flip, out RectTransform rect, out Image icon)
    {
        var hit = CodexUi.NewImage(name, root, null, new Color(0f, 0f, 0f, 0f));
        hit.raycastTarget = true;      // an invisible hit area; the arrow glyph itself is the only drawing
        rect = hit.rectTransform;
        var button = hit.gameObject.AddComponent<Button>();
        button.transition = Selectable.Transition.None;
        button.targetGraphic = hit;
        int dir = flip ? 1 : -1;
        button.onClick.AddListener(() => Move(dir));
        icon = CodexUi.NewImage("Icon", rect, CodexUi.CodexSprite("cx_back"), Color.white);
        icon.preserveAspect = true;
        CodexUi.Place(icon.rectTransform, CodexUi.Centered(0f, 0f, ArrowIcon, ArrowIcon));
        if (flip) icon.rectTransform.localScale = new Vector3(-1f, 1f, 1f);
        var shadow = icon.gameObject.AddComponent<Shadow>();
        shadow.effectColor = new Color(0f, 0f, 0f, .7f);
        shadow.effectDistance = new Vector2(3f, -3f);
        return button;
    }

    // ---- layout ----

    void Fit()
    {
        lastW = ScreenInfo.Width;
        lastH = ScreenInfo.Height;
        lastSafe = ScreenInfo.SafeArea;
        Canvas.ForceUpdateCanvases();
        ApplyLayout(CodexUi.SafeAreaUnits(canvas));
    }

    // Public so tests can lay the viewer out for any safe area (canvas units, centre-origin).
    public void ApplyLayout(Rect safeUnits)
    {
        var l = ComputeLayout(safeUnits);
        CurrentLayout = l;
        CodexUi.Place(backRt, l.back);
        CodexUi.Place(titleRt, l.title);
        CodexUi.Place(subRt, l.sub);
        CodexUi.Place(dotsRt, l.dots);
        CodexUi.Place(prevRt, l.prev);
        CodexUi.Place(nextRt, l.next);
        CodexUi.Place(shadeRt, l.shade);
        PlaceDots();
    }

    void PlaceDots()
    {
        int n = dots.Length;
        float total = n * DotSize + (n - 1) * DotGap;
        for (int i = 0; i < n; i++)
            CodexUi.Place(dots[i].rectTransform, CodexUi.Centered(-total * .5f + DotSize * .5f + i * (DotSize + DotGap), 0f, DotSize, DotSize));
    }

    // ---- the backdrop ----

    void EnsureTarget()
    {
        float scale = Mathf.Min(1f, MaxRenderEdge / (float)Mathf.Max(1, Mathf.Max(ScreenInfo.Width, ScreenInfo.Height)));
        int w = Mathf.Max(16, Mathf.RoundToInt(ScreenInfo.Width * scale));
        int h = Mathf.Max(16, Mathf.RoundToInt(ScreenInfo.Height * scale));
        if (rt != null && w == rtW && h == rtH) return;
        ReleaseTarget();
        rtW = w; rtH = h;
        rt = new RenderTexture(w, h, 24) { name = "CodexBackdropViewer", filterMode = FilterMode.Bilinear, useMipMap = false };
        rt.Create();
        if (worldGo == null)
        {
            worldGo = new GameObject("~CodexBackdropWorld");
            worldGo.transform.position = new Vector3(OffsetX, 0f, 0f);
            var camGo = new GameObject("~CodexBackdropCamera");
            camGo.transform.SetParent(worldGo.transform, false);
            camGo.transform.localPosition = new Vector3(0f, 0f, -10f);
            cam = camGo.AddComponent<Camera>();
            cam.orthographic = true;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Color.black;
            cam.nearClipPlane = .1f;
            cam.farClipPlane = 50f;
            cam.allowHDR = false;
            cam.allowMSAA = false;
            cam.enabled = false;     // rendered by hand, once a frame (RenderFrame)
        }
        cam.targetTexture = rt;
        view.texture = rt;
    }

    void ReleaseTarget()
    {
        if (cam != null) cam.targetTexture = null;
        if (view != null) view.texture = null;
        if (rt != null)
        {
            rt.Release();
            DestroyObject(rt);
            rt = null;
        }
    }

    void ViewExtents(out float halfW, out float halfH)
    {
        halfW = CameraFit.GameplayHalfWidth;
        halfH = halfW * (float)rtH / Mathf.Max(1, rtW);
    }

    // Replaces the shown set with the backdrop `b`'s (the old one is destroyed first).
    void Build(CodexBackdrop b)
    {
        DestroySet();
        float hw, hh;
        ViewExtents(out hw, out hh);
        cam.orthographicSize = hh;
        var options = new BackdropSet.MenuOptions { variant = b.variant, halfTurn = false, dim = 1f, withDirector = true };
        var s = new BackdropSet(b.world, worldGo.transform, hw, hh, options);
        if (!s.Complete) { s.Destroy(); set = null; return; }
        set = s;
        PreRoll(set);
        SecondsShown = 0f;
    }

    // A run opens under the planetfall's cloud ceiling (held, then cleared over ~14 s) and with
    // its set pieces still arriving. The viewer is for looking at the backdrop itself, so the
    // director is run unseen past that (PreRollSeconds of its own clock, no rendering) before the
    // first frame: the ground, its landmarks and the weather are as they are mid-flight.
    public const float PreRollStep = .05f;
    public static float PreRollSeconds(BackdropSet s)
    {
        return Mathf.Max(8f, s.Spec.CeilingClearSeconds()) + 1f;
    }

    static void PreRoll(BackdropSet s)
    {
        float v = WorldBackdrop.ScrollVelocity(0f);
        s.Tick(0f, v);
        for (float t = 0f; t < PreRollSeconds(s); t += PreRollStep) s.Tick(PreRollStep, v);
    }

    void DestroySet()
    {
        if (set != null) { set.Destroy(); set = null; }
    }

    void RenderFrame()
    {
        if (cam == null || rt == null || closed) return;
        float hw, hh;
        ViewExtents(out hw, out hh);
        cam.orthographicSize = hh;
        if (set != null) set.Layout(hw, hh);
        cam.Render();
    }

    // ---- text and arrows ----

    void Paint()
    {
        var b = CurrentBackdrop;
        title.text = b != null ? b.title : "";
        sub.text = b == null ? "" : b.world.ToUpperInvariant() + "   " + (index + 1) + " / " + sequence.Count;

        // dots: this world's variants; lit = the one shown, bright = unlocked, dim = not yet seen
        int variants = 0;
        foreach (var o in CodexBackdrops.All) if (b != null && o.world == b.world && CodexBackdrops.IsListed(o)) variants++;
        for (int i = 0; i < dots.Length; i++)
        {
            var d = dots[i];
            bool on = i < variants;
            d.gameObject.SetActive(on);
            if (!on) continue;
            var other = b == null ? null : CodexBackdrops.Find(b.world, b.variant == 0 ? 0 : i + 1);
            bool current = b != null && b.variant == i + 1;
            bool unlocked = other != null && CodexBackdrops.IsUnlocked(other);
            d.color = current ? CodexUi.Title : unlocked ? new Color(CodexUi.Body.r, CodexUi.Body.g, CodexUi.Body.b, .75f) : new Color(CodexUi.Locked.r, CodexUi.Locked.g, CodexUi.Locked.b, .6f);
            d.rectTransform.localScale = current ? new Vector3(1.45f, 1.45f, 1f) : Vector3.one;
        }
        bool many = sequence.Count > 1;
        prevBtn.gameObject.SetActive(many);
        nextBtn.gameObject.SetActive(many);
    }

    // ---- stepping ----

    // One step (+1 next, -1 previous), wrapping. With the dip running it queues nothing.
    public void Move(int dir)
    {
        if (closed || sequence.Count < 2 || dir == 0 || pendingIndex >= 0) return;
        int target = ((index + dir) % sequence.Count + sequence.Count) % sequence.Count;
        pendingIndex = target;
        fadeAt = Time.unscaledTime;
    }

    // Test seam and quick path: do the swap now, no dip.
    public void MoveNow(int dir)
    {
        if (closed || sequence.Count < 2 || dir == 0) return;
        pendingIndex = ((index + dir) % sequence.Count + sequence.Count) % sequence.Count;
        CompleteSwap();
        fade.color = new Color(0f, 0f, 0f, 0f);
    }

    void CompleteSwap()
    {
        index = pendingIndex;
        pendingIndex = -1;
        Build(sequence[index]);
        Paint();
        RenderFrame();
    }

    // ---- frame ----

    void Update()
    {
        if (closed) return;
        if (ScreenInfo.Width != lastW || ScreenInfo.Height != lastH || ScreenInfo.SafeArea != lastSafe)
        {
            EnsureTarget();
            Fit();
        }
        if (Input.GetKeyDown(KeyCode.RightArrow)) Move(1);
        else if (Input.GetKeyDown(KeyCode.LeftArrow)) Move(-1);
        Step(Time.unscaledDeltaTime);
    }

    // One frame of `dt` unscaled seconds: the dip, the backdrop's tick, the render.
    public void Step(float dt)
    {
        if (closed) return;
        if (pendingIndex >= 0)
        {
            float k = (Time.unscaledTime - fadeAt) / DipSeconds;
            if (k >= 1f)
            {
                CompleteSwap();
                fadeAt = Time.unscaledTime;       // now fading back in
            }
            fade.color = new Color(0f, 0f, 0f, Mathf.Clamp01(k));
        }
        else
        {
            float k = (Time.unscaledTime - fadeAt) / DipSeconds;
            fade.color = new Color(0f, 0f, 0f, 1f - Mathf.Clamp01(k));
        }
        dt = Mathf.Min(dt, Time.maximumDeltaTime);
        SecondsShown += dt;
        if (set != null) set.Tick(dt, WorldBackdrop.ScrollVelocity(0f));
        RenderFrame();
    }

    // ---- closing ----

    bool OnBackPressed()
    {
        if (closed) return false;
        Close();
        return true;
    }

    // Releases everything the viewer made: the set (and its director's pools), the camera, the RenderTexture,
    // the gradient texture and the canvas. Safe to call twice.
    public void Close()
    {
        if (closed) return;
        closed = true;
        BackNavigator.Unregister(this);
        DestroySet();
        ReleaseTarget();
        if (worldGo != null) DestroyObject(worldGo);
        worldGo = null;
        cam = null;
        if (shadeTexture != null) DestroyObject(shadeTexture);
        shadeTexture = null;
        if (instance == this) instance = null;
        var go = gameObject;
        DestroyObject(go);
        if (Application.isPlaying) Resources.UnloadUnusedAssets();
        var handler = Closed;
        if (handler != null) handler();
    }

    void OnDestroy()
    {
        // Destroyed from outside (a scene change): release the same things.
        if (!closed)
        {
            closed = true;
            BackNavigator.Unregister(this);
            DestroySet();
            ReleaseTarget();
            if (worldGo != null) DestroyObject(worldGo);
            if (shadeTexture != null) DestroyObject(shadeTexture);
            if (instance == this) instance = null;
        }
    }

    static void DestroyObject(UnityEngine.Object o)
    {
        if (o == null) return;
        if (Application.isPlaying) UnityEngine.Object.Destroy(o);
        else UnityEngine.Object.DestroyImmediate(o);
    }

    // The swipe: pointer down / up on the full-screen view.
    public void OnSwipe(Vector2 downPosition, Vector2 upPosition)
    {
        int dir = SwipeDirection(upPosition - downPosition, Mathf.Max(1, ScreenInfo.Width));
        if (dir != 0) Move(dir);
    }
}

// Forwards the finger's travel across the viewer to it.
public sealed class CodexBackdropSwipe : MonoBehaviour, IPointerDownHandler, IPointerUpHandler
{
    public CodexBackdropViewer viewer;
    Vector2 down;
    bool tracking;

    public void OnPointerDown(PointerEventData e) { down = e.position; tracking = true; }

    public void OnPointerUp(PointerEventData e)
    {
        if (!tracking) return;
        tracking = false;
        if (viewer != null) viewer.OnSwipe(down, e.position);
    }
}
