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
    [Tooltip("How long the intro runs if the player does not tap (the door is forced open " +
             "and the card leaves). Three taps smash the door and skip it sooner.")]
    public float holdSeconds = GateSim.IntroSeconds;

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

    // The wordmark flies out of the door (brand animation v2): small and dim in the
    // middle of the gap as if far behind the gate, it rushes out between the leaves,
    // growing, then travels DOWN to its final spot below the gate, lands with a small
    // bounce and flickers on like neon. Times are sim seconds of the 2.3 s card (the
    // brand film's 1.07 s / 1.43 s / 2.17 s after the first split, squeezed to fit).
    public const float FlyStart = 1.00f;      // appears in the gap, tiny
    public const float FlyExit = 1.30f;       // comes out in front of the leaves, heading down
    public const float FlyLand = 1.70f;       // reaches its final spot (then the bounce)
    public const float BounceSeconds = 0.20f;
    public const float FlickerEnd = 2.08f;    // steady from here
    public const float SmashFlySeconds = 0.30f;   // after a smash the flight is over this fast
    public const float StartScale = 0.08f;    // share of the final size when it appears

    // Gate height / width (a leaf is ~0.68 of the gate's width tall) and the space
    // between the gate and the wordmark, in gate widths. The gate + wordmark together
    // are centred in the safe area.
    public const float GateHeight = 0.68f;
    public const float GateWordGap = 0.12f;

    // ---- hooks (tests, previews, audio) -------------------------------------

    // Scene change, replaceable so tests can count loads without leaving the scene.
    public static System.Action<string> LoadScene = name => SceneManager.LoadScene(name);

    // Where the player's presses come from (legacy Input by default).
    public static System.Func<SplashInput.Sample> InputSource = SplashInput.ReadLegacy;

    // Build the door while not playing (edit-mode tests and preview filmstrips).
    public static bool BuildGateInEditMode;

    // Sound hooks: the splash names each cue; GateAudio plays the authored clips,
    // and the event lets tests / previews listen in.
    // Cues: gate_rattle (intro start), gate_step (each forced-open jolt),
    // gate_crack (tap 1 and 2), gate_smash (tap 3). See docs/hapticgate-splash.md.
    public static event System.Action<string> SoundCue;

    public const string NextScene = "startS4";

    bool leaving;
    int loadCount;
    public int LoadCount { get { return loadCount; } }
    public GateSim Sim { get { return sim; } }
    public GateView View { get { return view; } }
    public GateArt Art { get { return art; } }
    public WordPose Pose { get { return pose; } }

    GateSim sim;
    GateArt art;
    GateView view;
    Transform gateRoot;
    Layout layout;
    bool haveLayout;
    Vector3 logoBase, rootBase, gateBase;
    Vector3 logoScaleFinal;
    int logoOrderFinal;
    bool orderKnown;
    WordPose pose;

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
        public Vector2 logoCenter;        // FINAL centre of the wordmark (below the gate), camera-relative
        public Vector2 gateCenter;        // centre of the gate, camera-relative
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
        // gate + gap + wordmark, one column, as tall as it is wide times this:
        float column = GateHeight + GateWordGap + nh / nw;
        scale = Mathf.Min(scale, SafeFill * l.safeWorld.width / nw, SafeFill * l.safeWorld.height / (nw * column));
        l.logoScale = scale;
        l.logoSize = new Vector2(nw * scale, nh * scale);
        float w = l.logoSize.x, centreY = l.safeWorld.center.y;
        float bottom = centreY - column * w * 0.5f;
        l.logoCenter = new Vector2(l.safeWorld.center.x, bottom + l.logoSize.y * 0.5f);
        l.gateCenter = new Vector2(l.safeWorld.center.x, centreY + column * w * 0.5f - GateHeight * w * 0.5f);

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
            logoScaleFinal = t.localScale;
            if (logoOrderFinal == 0 && !orderKnown) { logoOrderFinal = logo.sortingOrder; orderKnown = true; }
            logo.color = Color.white;
        }

        if (Application.isPlaying || BuildGateInEditMode)
        {
            EnsureGate();
            if (gateRoot != null)
            {
                rootBase = new Vector3(camPos.x + l.gateCenter.x, camPos.y + l.gateCenter.y,
                                       logo != null ? logo.transform.position.z - 0.15f : 0f);
                gateRoot.position = rootBase;
                gateRoot.localScale = Vector3.one * l.logoSize.x;
                view.SetView(l.viewSize, l.logoSize.x);
            }
        }
        logoBase = new Vector3(camPos.x + l.logoCenter.x, camPos.y + l.logoCenter.y, logo != null ? logo.transform.position.z : 0f);
        gateBase = new Vector3(camPos.x + l.gateCenter.x, camPos.y + l.gateCenter.y, logoBase.z);

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
        layout = l;
        haveLayout = true;
        ApplyShake();
        return l;
    }

    // ---- the wordmark's flight --------------------------------------------

    public struct WordPose
    {
        public float scale;     // share of the final size
        public float travel;    // 0 = in the gap, 1 = final spot (can exceed 1 slightly: the bounce is added to y)
        public float bounce;    // upward offset in wordmark heights
        public float alpha;     // 0..1 (neon flicker on landing)
        public bool front;      // drawn in front of the leaves
        public bool done;       // exactly final
    }

    // The flight clock: sim time normally; after a smash it runs the rest of the
    // flight (to the end of the flicker) in SmashFlySeconds.
    public static float FlightTime(GateSim s)
    {
        if (s == null) return 0f;
        if (!s.smashed) return s.time;
        return Mathf.Lerp(s.smashAt, FlickerEnd, Mathf.Clamp01(s.smashTime / SmashFlySeconds));
    }

    static float Smooth(float x) { x = Mathf.Clamp01(x); return x * x * (3f - 2f * x); }

    public static WordPose PoseAt(float t)
    {
        var p = new WordPose();
        if (t < FlyStart) { p.scale = StartScale; p.alpha = 0f; return p; }
        float q = Mathf.Clamp01((t - FlyStart) / (FlyLand - FlyStart));
        p.alpha = Mathf.Clamp01((t - FlyStart) / 0.10f) * (t < FlyExit ? 0.8f : 1f);
        // grows slowly in the gap, then rushes
        p.scale = Mathf.Lerp(StartScale, 1f, Mathf.Pow(q, 2.2f));
        p.front = t >= FlyExit;
        float drop = Mathf.Clamp01((t - FlyExit) / (FlyLand - FlyExit));
        p.travel = t < FlyExit ? 0.03f * Smooth((t - FlyStart) / (FlyExit - FlyStart)) : Mathf.Lerp(0.03f, 1f, drop * drop);
        if (t >= FlyLand)
        {
            float u = Mathf.Clamp01((t - FlyLand) / BounceSeconds);
            p.bounce = 0.55f * Mathf.Sin(u * Mathf.PI) * (1f - u);
            p.scale = 1f + 0.05f * Mathf.Sin(u * Mathf.PI) * (1f - u);
            // neon flicker-on: a few hard dips that settle
            float f = Mathf.Clamp01((t - FlyLand) / (FlickerEnd - FlyLand));
            if (f < 1f)
            {
                int k = (int)(f * 14f);
                uint h = (uint)(k * 2654435761u) ^ 0x9E3779B9u; h ^= h >> 15; h *= 0x85ebca6bu; h ^= h >> 13;
                float n = (h & 0xFFFF) / 65535f;
                p.alpha = Mathf.Lerp(0.15f + 0.5f * n, 1f, f * f);
            }
            else p.alpha = 1f;
        }
        p.done = t >= FlickerEnd;
        if (p.done) { p.scale = 1f; p.travel = 1f; p.bounce = 0f; p.alpha = 1f; p.front = true; }
        return p;
    }

    // Place the wordmark for the current sim state. Off while not playing (edit-mode
    // layout tests and the screen-fit rig see it at its final spot).
    void ApplyWordmark(Vector2 shake)
    {
        if (logo == null) return;
        bool animate = (Application.isPlaying || BuildGateInEditMode) && sim != null;
        pose = animate ? PoseAt(FlightTime(sim)) : PoseAt(FlickerEnd);
        Vector3 gateWorld = new Vector3(gateBase.x, gateBase.y, logoBase.z);
        Vector3 p = Vector3.Lerp(gateWorld, logoBase, pose.travel);
        p.y += pose.bounce * layout.logoSize.y;
        var t = logo.transform;
        t.position = p + new Vector3(shake.x, shake.y, 0f);
        t.localScale = logoScaleFinal * pose.scale;
        logo.color = new Color(1f, 1f, 1f, pose.alpha);
        logo.sortingOrder = pose.front ? 4 : logoOrderFinal;
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

    void Awake() { EnsureSim(); }

    void EnsureSim()
    {
        if (sim != null) return;
        sim = new GateSim { naturalSeconds = holdSeconds };
        sim.Impact += (strength, step) => { if (step >= 0) Cue("gate_step"); };
        sim.Cracked += stage => { if (stage < GateSim.TapsToSmash) Cue("gate_crack"); };
        sim.Smashed += () => Cue("gate_smash");
        sim.Done += Leave;
    }

    // The user's gate-opening clip starts this long after the card begins, so its first big
    // hit (1.11 s into the file) lands on the door's biggest forced step (1.22 s).
    public const float OpenClipStart = 0.11f;
    bool openStarted;

    static void Cue(string name)
    {
        if (name == "gate_open") GateAudio.PlayOpen();
        else if (!GateAudio.OpenReplaces(name)) GateAudio.Play(name);
        if (SoundCue != null) SoundCue(name);
    }

    void Start() { Cue("gate_rattle"); }

    // Back / Escape skips the card at once, as it always did (routed through
    // BackNavigator, the only reader of Escape).
    void OnEnable() { BackNavigator.Register(this, OnBackPressed); }
    void OnDisable() { BackNavigator.Unregister(this); }

    public bool OnBackPressed()
    {
        GateAudio.StopOpen(GateAudio.OpenSmashFade);
        Leave();
        return true;
    }

    void Update()
    {
        // Unscaled: this is the first scene, and a timeScale left at 0 by a
        // previous run would otherwise stall the card indefinitely.
        var press = InputSource != null ? InputSource() : default(SplashInput.Sample);
        if (press.tap) Tap();
        Step(Time.unscaledDeltaTime);
    }

    // Back to the first frame (previews and tests).
    public void Restart()
    {
        EnsureSim();
        sim.Reset();
        leaving = false;
        openStarted = false;
        loadCount = 0;
    }

    // One discrete press. Up to three are kept (queued), applied in order.
    public bool Tap()
    {
        EnsureSim();
        return !leaving && sim.Tap();
    }

    // Advance the card by dt seconds (clamped: a loading hitch must not skip the show).
    public void Step(float dt)
    {
        EnsureSim();
        sim.naturalSeconds = holdSeconds;
        dt = Mathf.Min(dt, 0.1f);
        if (!leaving) sim.Advance(dt);
        // Once, unless the door is already (or about to be) smashed: then it never starts.
        if (!openStarted && !leaving && sim.time >= OpenClipStart && !sim.smashed
            && sim.pendingTaps + sim.crackStage < GateSim.TapsToSmash)
        {
            openStarted = true;
            Cue("gate_open");
        }
        if (view != null) view.Sync(dt);
        ApplyShake();
    }

    void EnsureGate()
    {
        if (gateRoot != null) return;
        EnsureSim();
        art = GateArt.Load();
        if (art == null) return;
        gateRoot = new GameObject("Industrial gate").transform;
        view = new GateView(gateRoot, art, sim);
    }

    // Shake the whole card (door, mark and words) like a camera shake.
    void ApplyShake()
    {
        if (!haveLayout) return;
        Vector2 w = sim != null ? sim.shake * layout.logoSize.x : Vector2.zero;
        ApplyWordmark(w);
        if (gateRoot != null) gateRoot.position = rootBase + new Vector3(w.x, w.y, 0f);
        if (words != null && wordsAuthored != null && wordsAuthored.Length == words.Length)
        {
            Vector2 c = w * layout.pixelsPerUnit / Mathf.Max(layout.wordsScaleFactor, 0.0001f);
            for (int i = 0; i < words.Length; i++)
                if (words[i] != null) words[i].anchoredPosition = wordsAuthored[i] + layout.wordsShift + c;
        }
    }

    void OnDestroy()
    {
        if (art != null) art.Release();
        art = null; view = null;
    }

    void Leave()
    {
        if (leaving) return;
        leaving = true;
        loadCount++;
        // A natural finish fades the opening clip out as the title comes up; a smash already stopped it.
        if (sim == null || !sim.smashed) GateAudio.StopOpen(GateAudio.OpenLeaveFade);
        Time.timeScale = 1f;
        LoadScene(NextScene);
    }
}
