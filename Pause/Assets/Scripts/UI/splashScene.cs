using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// The Haptic Gate title card.
//
// It used to hold for a flat three seconds with no way past it, which is a long
// time to stare at a logo you have already seen -- especially on a game whose
// runs are a couple of minutes. It is shorter now, and a tap skips it.
//
// Layout. The card was authored as fixed scene transforms for a 9:16 phone: a
// black backdrop quad 7.02 x 11.05 world units, the HapticGate mark at scale
// 0.7, and the "A" / "Game" words on a constant-pixel-size overlay canvas at
// fixed pixel offsets. CameraFit (attached to every scene's camera at runtime)
// then grows the orthographic size on taller screens so the play field keeps
// its width -- on the 1080x2520 Galaxy Z Flip the view becomes 13.3 units tall,
// so the 11.05-unit backdrop stopped short and the camera's blue clear colour
// showed as a band across the top and bottom of the card. The words also
// drifted against the mark whenever the screen's pixel density differed from
// the one they were placed on.
//
// So the card now lays itself out from the live view every time the screen,
// safe area or camera size changes: the backdrop covers the whole view (cutout
// included -- the app renders outside the safe area), the mark is a fixed
// fraction of the shorter screen dimension, centred in the safe area and never
// larger than it, and the words are scaled and moved with the mark exactly as
// they were authored around it. The mark's art is never touched, only its
// uniform transform scale and position.
public class splashScene : MonoBehaviour
{
    [Tooltip("How long the card holds if the player does not skip it.")]
    public float holdSeconds = 1.25f;

    [Tooltip("Ignore input for a moment so a stray tap carried over from a " +
             "previous screen cannot skip the card before it is even seen.")]
    public float skipLockout = 0.15f;

    [Header("Layout")]
    [Tooltip("The HapticGate mark. Only its transform is changed.")]
    public SpriteRenderer logo;

    [Tooltip("The overlay canvas carrying the \"A\" / \"Game\" words.")]
    public CanvasScaler wordsScaler;

    [Tooltip("The words placed around the mark.")]
    public RectTransform[] words;

    [Tooltip("Logo width as a fraction of the shorter visible screen dimension.")]
    public float logoFraction = LogoFraction;

    // 0.64 of the shorter side is what the authored 0.7 scale works out to on
    // the 9:16 phone the card was made on, after CameraFit's small bump there.
    public const float LogoFraction = 0.64f;

    // Never let the mark fill more than this share of the safe area's width or
    // height, so it keeps a margin on squat or cut-into screens.
    public const float SafeFill = 0.9f;

    // How much bigger than the view the backdrop is drawn, so rounding and a
    // one-frame lag behind a resize can never open a hairline gap.
    public const float BackdropOverscan = 1.04f;

    // The words were placed when the mark was at scale 0.7 and one world unit
    // was 192 canvas pixels (1080x1920 at orthographic size 5). Their offsets
    // and font sizes are kept in those units and scaled with the mark.
    public const float AuthoredLogoScale = 0.7f;
    public const float AuthoredPixelsPerUnit = 192f;

    float elapsed;
    bool leaving;
    Transform leftGate, rightGate;
    Transform gateRoot;
    Sprite leftPanelSprite, rightPanelSprite, steamSprite;
    readonly Transform[] steam = new Transform[6];
    readonly SpriteRenderer[] steamRenderers = new SpriteRenderer[6];

    Camera cam;
    Vector2[] wordsAuthored;
    int lastW = -1, lastH = -1;
    Rect lastSafe;
    float lastSize = -1f;

    // Everything the layout decides, in world units relative to the camera
    // centre (x right, y up) and canvas units for the words.
    public struct Layout
    {
        public Vector2 viewSize;          // visible world width/height
        public Rect safeWorld;            // safe area, world units, camera-relative
        public Vector2 backdropScale;     // quad localScale (unit mesh)
        public float logoScale;           // uniform localScale of the mark
        public Vector2 logoCenter;        // camera-relative world position
        public Vector2 logoSize;          // world width/height of the mark
        public float pixelsPerUnit;       // screen pixels per world unit
        public float wordsScaleFactor;    // canvas scale factor
        public Vector2 wordsShift;        // canvas-unit shift applied to each word
    }

    // Pure: no scene, no Screen. logoNativeSize is the sprite's world size at
    // scale 1 (5.18 x 0.87 for the 518x87, 100 ppu mark).
    public static Layout Compute(float orthoSize, int screenW, int screenH, Rect safeArea,
                                 Vector2 logoNativeSize, float fraction = LogoFraction)
    {
        var l = new Layout();
        if (screenW <= 0 || screenH <= 0) { screenW = 1080; screenH = 1920; safeArea = new Rect(0, 0, 1080, 1920); }

        float viewH = 2f * orthoSize;
        float viewW = viewH * screenW / screenH;
        float ppu = screenH / viewH;
        l.viewSize = new Vector2(viewW, viewH);
        l.pixelsPerUnit = ppu;

        // Clamp the safe area to the screen; an empty or bogus one means the
        // whole screen is safe.
        float sx0 = Mathf.Clamp(safeArea.xMin, 0, screenW), sx1 = Mathf.Clamp(safeArea.xMax, 0, screenW);
        float sy0 = Mathf.Clamp(safeArea.yMin, 0, screenH), sy1 = Mathf.Clamp(safeArea.yMax, 0, screenH);
        if (sx1 - sx0 < 1f || sy1 - sy0 < 1f) { sx0 = 0; sy0 = 0; sx1 = screenW; sy1 = screenH; }
        l.safeWorld = Rect.MinMaxRect((sx0 - screenW * 0.5f) / ppu, (sy0 - screenH * 0.5f) / ppu,
                                      (sx1 - screenW * 0.5f) / ppu, (sy1 - screenH * 0.5f) / ppu);

        l.backdropScale = new Vector2(viewW * BackdropOverscan, viewH * BackdropOverscan);

        float nw = Mathf.Max(logoNativeSize.x, 0.0001f), nh = Mathf.Max(logoNativeSize.y, 0.0001f);
        float scale = fraction * Mathf.Min(viewW, viewH) / nw;
        scale = Mathf.Min(scale, SafeFill * l.safeWorld.width / nw, SafeFill * l.safeWorld.height / nh);
        l.logoScale = scale;
        l.logoSize = new Vector2(nw * scale, nh * scale);
        l.logoCenter = l.safeWorld.center;

        // Canvas pixels per world unit at the mark's current scale, relative to
        // the authored relationship, is the canvas scale factor; the mark's
        // offset from screen centre, in canvas units, is the words' shift.
        l.wordsScaleFactor = ppu * (scale / AuthoredLogoScale) / AuthoredPixelsPerUnit;
        l.wordsShift = l.logoCenter * ppu / Mathf.Max(l.wordsScaleFactor, 0.0001f);
        return l;
    }

    public static Vector2 NativeSize(SpriteRenderer sr)
    {
        if (sr == null || sr.sprite == null) return new Vector2(5.18f, 0.87f);
        return sr.sprite.bounds.size;
    }

    Camera FindCamera()
    {
        if (cam == null) cam = GetComponentInParent<Camera>();
        if (cam == null) cam = Camera.main;
        return cam;
    }

    // Applies the layout for a given screen. Public so the editor tests can lay
    // the card out at any aspect ratio without entering Play mode.
    public Layout ApplyLayout(int screenW, int screenH, Rect safeArea)
    {
        var c = FindCamera();
        float size = c != null && c.orthographic ? c.orthographicSize : 5f;
        var l = Compute(size, screenW, screenH, safeArea, NativeSize(logo), logoFraction);
        Vector3 camPos = c != null ? c.transform.position : Vector3.zero;

        // Backdrop: centred on the camera, covering the whole view.
        transform.position = new Vector3(camPos.x, camPos.y, transform.position.z);
        SetWorldScale(transform, new Vector3(l.backdropScale.x, l.backdropScale.y, 1f));

        if (logo != null)
        {
            var t = logo.transform;
            t.position = new Vector3(camPos.x + l.logoCenter.x, camPos.y + l.logoCenter.y, t.position.z);
            SetWorldScale(t, new Vector3(l.logoScale, l.logoScale, 1f));
            logo.color = Color.white;
        }

        if (Application.isPlaying)
        {
            EnsureGate();
            if (gateRoot != null)
            {
                gateRoot.position = new Vector3(camPos.x + l.logoCenter.x, camPos.y + l.logoCenter.y,
                                                logo != null ? logo.transform.position.z - 0.15f : 0f);
                gateRoot.localScale = Vector3.one * l.logoSize.x;
                AnimateGate();
            }
        }

        if (words != null)
        {
            if (wordsAuthored == null || wordsAuthored.Length != words.Length)
            {
                wordsAuthored = new Vector2[words.Length];
                for (int i = 0; i < words.Length; i++)
                    if (words[i] != null) wordsAuthored[i] = words[i].anchoredPosition;
            }
            for (int i = 0; i < words.Length; i++)
                if (words[i] != null) words[i].anchoredPosition = wordsAuthored[i] + l.wordsShift;
        }
        if (wordsScaler != null)
        {
            wordsScaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
            wordsScaler.scaleFactor = l.wordsScaleFactor;
            var canvas = wordsScaler.GetComponent<Canvas>();
            if (canvas != null) canvas.scaleFactor = l.wordsScaleFactor;
        }
        return l;
    }

    static void SetWorldScale(Transform t, Vector3 world)
    {
        Vector3 p = t.parent != null ? t.parent.lossyScale : Vector3.one;
        t.localScale = new Vector3(world.x / NonZero(p.x), world.y / NonZero(p.y), world.z / NonZero(p.z));
    }

    static float NonZero(float v) { return Mathf.Abs(v) < 0.0001f ? 1f : v; }

    void LateUpdate()
    {
        // LateUpdate, so CameraFit (which grows the camera in its own Start and
        // Update) has already settled the size this frame will render with.
        var c = FindCamera();
        float size = c != null ? c.orthographicSize : 5f;
        Rect safe = ScreenInfo.SafeArea;
        if (ScreenInfo.Width == lastW && ScreenInfo.Height == lastH && safe == lastSafe
            && Mathf.Approximately(size, lastSize))
            return;
        lastW = ScreenInfo.Width; lastH = ScreenInfo.Height; lastSafe = safe; lastSize = size;
        ApplyLayout(ScreenInfo.Width, ScreenInfo.Height, safe);
    }

    void Update()
    {
        // Unscaled: this is the first scene, and a timeScale left at 0 by a
        // previous run would otherwise stall the card indefinitely.
        elapsed += Time.unscaledDeltaTime;
        AnimateGate();

        if (elapsed >= holdSeconds || (elapsed >= skipLockout && Skipped()))
            Leave();
    }

    void EnsureGate()
    {
        if (gateRoot != null) return;
        var panels = Resources.Load<Texture2D>("HapticGate/industrial_gate");
        var vapor = Resources.Load<Texture2D>("HapticGate/steam");
        if (panels == null || vapor == null) return;
        panels.filterMode = FilterMode.Point;
        vapor.filterMode = FilterMode.Point;
        gateRoot = new GameObject("Industrial gate").transform;
        leftPanelSprite = PanelSprite(panels, 0.055f);
        rightPanelSprite = PanelSprite(panels, 0.51f);
        leftGate = NewPanel("Left steel door", leftPanelSprite);
        rightGate = NewPanel("Right steel door", rightPanelSprite);
        steamSprite = Sprite.Create(vapor, new Rect(0, 0, vapor.width, vapor.height),
                                    new Vector2(0.5f, 0.5f), 100f);
        for (int i = 0; i < steam.Length; i++)
        {
            var puff = new GameObject("Vent steam " + i);
            puff.transform.SetParent(gateRoot, false);
            steam[i] = puff.transform;
            var sr = puff.AddComponent<SpriteRenderer>();
            sr.sprite = steamSprite;
            sr.sortingOrder = 3;
            steamRenderers[i] = sr;
        }
    }

    static Sprite PanelSprite(Texture2D texture, float x)
    {
        return Sprite.Create(texture,
                             new Rect(texture.width * x, texture.height * 0.05f,
                                      texture.width * 0.44f, texture.height * 0.9f),
                             new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect);
    }

    Transform NewPanel(string name, Sprite sprite)
    {
        var go = new GameObject(name);
        go.transform.SetParent(gateRoot, false);
        go.transform.localScale = Vector3.one * (0.5f / sprite.bounds.size.x);
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = sprite;
        sr.sortingOrder = 2;
        return go.transform;
    }

    void AnimateGate()
    {
        if (gateRoot == null) return;
        // A held latch, then a smooth powered slide with a small damped stop.
        float t = elapsed;
        float latch = t < 0.32f ? Mathf.Sin(t * 68f) * (0.32f - t) * 0.002f : 0f;
        float open = Mathf.Clamp01((t - 0.32f) / 1.10f);
        float smooth = open * open * (3f - 2f * open);
        float stop = open > 0.8f ? Mathf.Sin((open - 0.8f) * 24f) * (1f - open) * 0.009f : 0f;
        float travel = 0.58f * smooth + stop;
        leftGate.localPosition = new Vector3(-0.25f - travel + latch, 0f, 0f);
        rightGate.localPosition = new Vector3(0.25f + travel - latch, 0f, 0f);

        // Repeating puffs rise from both vents. Their staggered life phases
        // keep the smoke moving smoothly between rendered frames.
        for (int i = 0; i < steam.Length; i++)
        {
            float age = Mathf.Repeat(t - 0.28f - i * 0.18f, 1.08f) / 1.08f;
            bool active = t >= 0.28f + i * 0.18f && t < 1.85f;
            float side = i % 2 == 0 ? -1f : 1f;
            steam[i].localPosition = new Vector3(side * (0.10f + smooth * 0.44f + age * 0.10f),
                                                  -0.21f + age * 0.31f, -0.02f);
            float size = 0.065f + age * 0.12f;
            steam[i].localScale = Vector3.one * (size / steamSprite.bounds.size.x);
            float alpha = active ? Mathf.Sin(age * Mathf.PI) * 0.56f * (1f - 0.35f * smooth) : 0f;
            steamRenderers[i].color = new Color(0.74f, 0.85f, 0.92f, alpha);
        }
    }

    void OnDestroy()
    {
        Release(leftPanelSprite);
        Release(rightPanelSprite);
        Release(steamSprite);
    }

    static void Release(Object asset)
    {
        if (asset == null) return;
        if (Application.isPlaying) Destroy(asset);
        else DestroyImmediate(asset);
    }

    static bool Skipped()
    {
        return Input.GetMouseButtonDown(0)
            || Input.touchCount > 0
            || Input.anyKeyDown;
    }

    void Leave()
    {
        if (leaving) return;
        leaving = true;
        Time.timeScale = 1f;
        SceneManager.LoadScene("startS4");
    }
}
