using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// The Haptic Gate title card: the approved gate-opening film (GateFilm: the
// steel H-gate is forced open, smoke and steam spill out, the HapticGate
// wordmark flies out of the door and lands at the bottom), with a card shake
// and the user's gate-opening sound on top. Three taps smash it (GateSim /
// GateView), Back skips it, and it leaves for the title by itself after
// GateSim.IntroSeconds (the 3.33 s film plus a short hold).
//
// Layout. CameraFit grows the orthographic size on taller screens, so the card
// lays itself out from the live view every time the screen, safe area or
// camera size changes: the black backdrop covers the whole view (cutout
// included -- the app renders outside the safe area), the film is as wide as
// the view (never wider than its lit content allows in the safe area, never
// taller than the safe area) and centred in the safe area. The HapticGate mark
// sprite (logo) is the fallback when the film's art is missing, and sits
// exactly where the film's wordmark lands (the edit-mode layout tests and the
// screen-fit rig measure it there); while the film plays it is hidden. The
// "A" / "Game" words are scaled and moved with the mark as they were authored
// around it. The mark's art is never touched, only its transform.
public class splashScene : MonoBehaviour
{
    [Tooltip("How long the intro runs if the player does not tap (the film plays, holds " +
             "its last frame and the card leaves). Three taps smash the door and skip it sooner.")]
    public float holdSeconds = GateSim.IntroSeconds;

    [Tooltip("Ignore input for a moment so a stray tap carried over from a " +
             "previous screen cannot skip the card before it is even seen.")]
    public float skipLockout = 0.15f;

    [Header("Layout")]
    [Tooltip("The HapticGate mark (fallback when the film is missing). Only its transform is changed.")]
    public SpriteRenderer logo;

    [Tooltip("The overlay canvas carrying the \"A\" / \"Game\" words.")]
    public CanvasScaler wordsScaler;

    [Tooltip("The words placed around the mark.")]
    public RectTransform[] words;

    [Tooltip("Film width as a fraction of the visible width.")]
    public float filmFraction = FilmFraction;

    // The film is as wide as the view: its edges are soft smoke on black.
    public const float FilmFraction = 1f;

    // The film's lit content (gate leaves wide open, the wordmark's glow) spans
    // this share of its width; it must fit inside the safe area's width.
    public const float FilmContentShare = 0.88f;

    // Never let the film be taller than this share of the safe area's height.
    public const float SafeFill = 0.9f;

    // The card's worst shake, in film widths (GateSim: rattle 0.005 + a 1.6 kick x 0.022).
    public const float MaxShake = 0.005f + 0.022f * 1.6f;

    // How much bigger than the view the backdrop is drawn, so rounding and a
    // one-frame lag behind a resize can never open a hairline gap.
    public const float BackdropOverscan = 1.04f;

    // The words were placed when the mark was at scale 0.7 and one world unit
    // was 192 canvas pixels (1080x1920 at orthographic size 5). Their offsets
    // and font sizes are kept in those units and scaled with the mark.
    public const float AuthoredLogoScale = 0.7f;
    public const float AuthoredPixelsPerUnit = 192f;

    // ---- hooks (tests, previews, audio) -------------------------------------

    // Scene change, replaceable so tests can count loads without leaving the scene.
    public static System.Action<string> LoadScene = name => SceneManager.LoadScene(name);

    // Where the player's presses come from (legacy Input by default).
    public static System.Func<SplashInput.Sample> InputSource = SplashInput.ReadLegacy;

    // Build the film and effects while not playing (edit-mode tests and preview filmstrips).
    public static bool BuildGateInEditMode;

    // Sound hooks: the splash names each cue; GateAudio plays the authored clips,
    // and the event lets tests / previews listen in.
    // Cues: gate_rattle (intro start), gate_open (the user's opening clip), gate_step (each of the film's shoves),
    // gate_crack (tap 1 and 2), gate_smash (tap 3). See docs/hapticgate-splash.md.
    public static event System.Action<string> SoundCue;

    public const string NextScene = "startS4";

    bool leaving;
    int loadCount;
    public int LoadCount { get { return loadCount; } }
    public GateSim Sim { get { return sim; } }
    public GateView View { get { return view; } }
    public GateArt Art { get { return art; } }
    public GateFilm Film { get { return film; } }
    public Layout CurrentLayout { get { return layout; } }

    // The film clock: sim time, or the end of the film once the door is smashed.
    public float FilmTime { get { return sim == null ? 0f : (sim.smashed ? GateFilm.Length : sim.time); } }

    GateSim sim;
    GateArt art;
    GateView view;
    GateFilm film;
    Transform gateRoot;
    Layout layout;
    bool haveLayout;
    Vector3 logoBase, rootBase;

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
        public float filmWidth;           // world width of the film (its height is GateFilm.Aspect times that)
        public Vector2 filmCenter;        // centre of the film, camera-relative
        public float logoScale;           // uniform localScale of the mark
        public Vector2 logoCenter;        // where the film's wordmark lands (the mark's spot), camera-relative
        public Vector2 gateCenter;        // centre of the film's gate, camera-relative
        public Vector2 logoSize;          // world width/height of the mark
        public float pixelsPerUnit;       // screen pixels per world unit
        public float wordsScaleFactor;    // canvas scale factor
        public Vector2 wordsShift;        // canvas-unit shift applied to each word
    }

    // Pure: no scene, no Screen. logoNativeSize is the sprite's world size at
    // scale 1 (5.18 x 0.87 for the 518x87, 100 ppu mark).
    public static Layout Compute(float orthoSize, int screenW, int screenH, Rect safeArea,
                                 Vector2 logoNativeSize, float fraction = FilmFraction)
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

        float fw = fraction * viewW;
        fw = Mathf.Min(fw, l.safeWorld.width / FilmContentShare, SafeFill * l.safeWorld.height / GateFilm.Aspect);
        // The mark (fallback / screen-fit anchor, its sprite box is taller than the letters) plus the
        // worst shake must stay inside the safe area below the film's centre.
        float nwk = Mathf.Max(logoNativeSize.x, 0.0001f), nhk = Mathf.Max(logoNativeSize.y, 0.0001f);
        float below = -GateFilm.WordmarkCentreY + 0.5f * GateFilm.WordmarkWidth * nhk / nwk + MaxShake + 0.01f;   // + a hair: never exactly on the edge
        fw = Mathf.Min(fw, 0.5f * l.safeWorld.height / below);
        l.filmWidth = fw;
        l.filmCenter = l.safeWorld.center;
        l.gateCenter = l.filmCenter + new Vector2(0f, GateFilm.GateCentreY * fw);

        float nw = Mathf.Max(logoNativeSize.x, 0.0001f), nh = Mathf.Max(logoNativeSize.y, 0.0001f);
        float scale = GateFilm.WordmarkWidth * fw / nw;
        l.logoScale = scale;
        l.logoSize = new Vector2(nw * scale, nh * scale);
        l.logoCenter = l.filmCenter + new Vector2(0f, GateFilm.WordmarkCentreY * fw);

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
        var l = Compute(size, screenW, screenH, safeArea, NativeSize(logo), filmFraction);
        Vector3 camPos = c != null ? c.transform.position : Vector3.zero;

        // Backdrop: centred on the camera, covering the whole view.
        transform.position = new Vector3(camPos.x, camPos.y, transform.position.z);
        SetWorldScale(transform, new Vector3(l.backdropScale.x, l.backdropScale.y, 1f));

        if (Application.isPlaying || BuildGateInEditMode) EnsureGate();

        if (logo != null)
        {
            var t = logo.transform;
            t.position = new Vector3(camPos.x + l.logoCenter.x, camPos.y + l.logoCenter.y, t.position.z);
            SetWorldScale(t, new Vector3(l.logoScale, l.logoScale, 1f));
            logo.color = Color.white;
            // The film carries the wordmark; the mark is the fallback without it.
            logo.enabled = !FilmShowing;
        }

        if (gateRoot != null)
        {
            rootBase = new Vector3(camPos.x + l.filmCenter.x, camPos.y + l.filmCenter.y,
                                   logo != null ? logo.transform.position.z - 0.15f : 0f);
            gateRoot.position = rootBase;
            SetWorldScale(gateRoot, new Vector3(l.filmWidth, l.filmWidth, 1f));
            if (view != null) view.SetView(l.viewSize, l.filmWidth);
        }
        logoBase = new Vector3(camPos.x + l.logoCenter.x, camPos.y + l.logoCenter.y, logo != null ? logo.transform.position.z : 0f);

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

    // True while the film is on screen (it then carries the wordmark).
    public bool FilmShowing { get { return film != null && film.HasArt; } }

    // The "A" / "Game" words frame the wordmark, so with the film they come in only once
    // its wordmark has landed (fading in over WordsFadeSeconds); without the film they show at once.
    public const float WordsFadeSeconds = 0.2f;
    public static float WordsAlphaAt(float filmTime)
    {
        return Mathf.Clamp01((filmTime - GateFilm.WordmarkLands) / WordsFadeSeconds);
    }
    // After a smash the words come in over the same fade, from the smash.
    public float WordsAlpha
    {
        get
        {
            if (!FilmShowing) return 1f;
            if (sim != null && sim.smashed) return Mathf.Clamp01(sim.smashTime / WordsFadeSeconds);
            return WordsAlphaAt(FilmTime);
        }
    }

    CanvasRenderer[] wordRenderers;
    float wordsAlphaShown = -1f;

    // Through the CanvasRenderer's alpha: no graphic rebuild, no allocation per frame.
    void ApplyWordsAlpha()
    {
        if (words == null) return;
        if (wordRenderers == null || wordRenderers.Length != words.Length)
        {
            wordRenderers = new CanvasRenderer[words.Length];
            for (int i = 0; i < words.Length; i++)
                if (words[i] != null) wordRenderers[i] = words[i].GetComponent<CanvasRenderer>();
            wordsAlphaShown = -1f;
        }
        float a = WordsAlpha;
        if (a == wordsAlphaShown) return;
        wordsAlphaShown = a;
        for (int i = 0; i < wordRenderers.Length; i++)
            if (wordRenderers[i] != null) wordRenderers[i].SetAlpha(a);
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

    // Playing: build the film at once so frame 0 is up on the first rendered frame and the rest
    // starts streaming in before the first layout pass.
    void Awake() { EnsureSim(); if (Application.isPlaying) EnsureGate(); }

    void EnsureSim()
    {
        if (sim != null) return;
        sim = new GateSim { naturalSeconds = holdSeconds };
        sim.Impact += (strength, step) => { if (step >= 0) Cue("gate_step"); };
        sim.Cracked += stage => { if (stage < GateSim.TapsToSmash) Cue("gate_crack"); };
        sim.Smashed += () => Cue("gate_smash");
        sim.Done += Leave;
    }

    // The user's gate-opening clip (the film's own soundtrack) starts this long after the
    // card begins, as in the brand film (its audio is delayed 30 ms), so the clip's first big
    // hit (1.11 s into the file) lands on the film's first split (frame 34, 1.133 s).
    public const float OpenClipStart = 0.03f;
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
        if (view != null) view.Clear();
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
        if (film != null) film.Show(FilmTime);
        if (view != null) view.Sync(dt);
        ApplyShake();
    }

    void EnsureGate()
    {
        if (gateRoot != null) return;
        EnsureSim();
        gateRoot = new GameObject("HapticGate film").transform;
        film = new GateFilm(gateRoot, 2);
        if (!film.Begin()) { film.Release(); film = null; }
        art = GateArt.Load();
        if (art != null) view = new GateView(gateRoot, art, sim);
    }

    // Shake the whole card (door, mark and words) like a camera shake.
    void ApplyShake()
    {
        if (!haveLayout) return;
        Vector2 w = sim != null ? sim.shake * layout.filmWidth : Vector2.zero;
        if (gateRoot != null) gateRoot.position = rootBase + new Vector3(w.x, w.y, 0f);
        if (logo != null) logo.transform.position = logoBase + new Vector3(w.x, w.y, 0f);
        ApplyWordsAlpha();
        if (words != null && wordsAuthored != null && wordsAuthored.Length == words.Length)
        {
            Vector2 c = w * layout.pixelsPerUnit / Mathf.Max(layout.wordsScaleFactor, 0.0001f);
            for (int i = 0; i < words.Length; i++)
                if (words[i] != null) words[i].anchoredPosition = wordsAuthored[i] + layout.wordsShift + c;
        }
    }

    void OnDestroy()
    {
        if (film != null) film.Release();
        if (art != null) art.Release();
        art = null; view = null; film = null;
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
