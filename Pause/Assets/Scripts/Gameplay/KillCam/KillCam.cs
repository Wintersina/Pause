using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// The kill cam: the moment the ship dies, the last LiveWindow seconds of the
// run replay from the tape (KillCamRecorder) in slow motion that gets very
// slow at the end (KillCamClip.RateAt), so the player sees exactly what got
// them -- the shot, the ram, the beam -- arrive. The camera keeps its normal
// framing: no zoom, no drift, no name caption (the Flight Complete panel
// still names the killer).
//
// HOW. The death crash (DeathCrash) sets itself up on the fatal hit as ever,
// then hands over: while the replay plays the crash is held (its clock and
// FrameDt stay at zero, so its blasts, the hearts and the board stay frozen
// on the fatal frame; the world is already stopped by the death). Every live
// renderer is switched off (forceRenderingOff) and a KillCamStage draws the
// recorded frames in their place; the rails keep drawing, fed their recorded
// texture offsets; the camera takes each frame's recorded position (a shake
// on the tape replays as it happened). When the replay reaches the fatal
// frame and holds a beat, everything is put back exactly as it was and the
// crash plays from its first frame -- the replay hands straight into it.
//
// THE INTRO. The player must know a replay is coming, so before it plays:
//   Killed  the fatal frame freezes (the tape's last frame, no zoom, no extra
//           shake) under a KILLED banner in the boss banner's style
//   Rewind  the tape scrubs backwards to the start of the window under a cyan
//           tint, scanlines and a horizontal glitch while the song plays in
//           reverse (KillCamMusic); the REPLAY label pops in
//   Play    the replay runs forward in slow motion, the song carries on from
//           where it paused
// A fresh tap after SkipGuard skips to the crash at any point, so the replay
// never traps the player; the whole thing is IntroSeconds (~1.25 s) plus
// RealSeconds(LiveWindow) (~3.4 s). Soft shades top and bottom (no boxes, no
// lines) frame it, and a REPLAY label lettered like the PAUSED caption
// (ReplayLabel) marks it. Sounds are optional hooks (KillCamAudio).
[DefaultExecutionOrder(32000)]   // its LateUpdate draws after everything else has moved
public sealed class KillCam : MonoBehaviour
{
    public const float SkipGuard = .25f;
    public const float MaxStep = 1f / 20f;
    public const int MinFrames = 12;          // a run shorter than this has nothing worth replaying
    public const float ShadeFade = .18f;
    public const float ShadeAlpha = .55f;
    public const int OverlayOrder = 400;
    // A safety net: whatever happens, the replay is over after this much real time.
    public const float MaxReal = 8f;

    public const float KilledSeconds = .75f;   // the banner's stay on the frozen fatal frame
    public const float RewindSeconds = .5f;
    public const float IntroSeconds = KilledSeconds + RewindSeconds;
    const float FxFadeIn = .08f, FxFadeOut = .15f;
    const float LabelFontSize = 46f;

    public enum Phase { Killed, Rewind, Play }

    // Tests / previews: no kill cam at all (the crash plays at once, as before).
    public static bool Enabled = true;
    // Tests: false skips the KILLED + rewind intro and plays the replay at once.
    public static bool Intro = true;

    static KillCam instance;
    public static KillCam Current { get { return instance; } }
    public static bool Playing { get { return instance != null && instance.playing; } }

    KillCamStage stage;
    KillCamClip clip;
    readonly List<Renderer> hidden = new List<Renderer>(1024);
    readonly List<GameObject> roots = new List<GameObject>(64);
    readonly List<Renderer> scratch = new List<Renderer>(512);
    readonly List<Renderer> keep = new List<Renderer>(8);
    float clock, real, hold, phaseTime, labelTime;
    bool playing, wasPressed;
    Phase phase;
    Camera cam;
    Vector3 camSaved;
    float glitchX;

    Canvas canvas;
    CanvasGroup group;
    RectTransform shadesRt;
    ReplayLabel label;
    KilledBanner banner;
    Image tint;
    RawImage scan;
    Texture2D shadeTexture, scanTexture;
    const int Bands = 5;
    readonly Image[] bands = new Image[Bands];
    int fxTick = -1;

    // ---- read-outs (tests) ----
    public Phase CurrentPhase { get { return phase; } }
    public bool BannerShown { get { return banner != null && banner.Shown; } }
    public bool LabelShown { get { return label != null && label.Root.gameObject.activeSelf; } }
    public ReplayLabel Label { get { return label; } }
    public KilledBanner Banner { get { return banner; } }
    public float FxAlpha { get { return tint != null ? tint.color.a : 0f; } }
    public float ClipTime { get { return clock; } }
    public float RealTime { get { return real; } }
    public bool Skipped { get; private set; }
    public KillCamClip Clip { get { return clip; } }
    public KillCamStage Stage { get { return stage; } }
    public int HiddenCount { get { return hidden.Count; } }

    public static KillCam Ensure()
    {
        if (instance != null) return instance;
        var go = new GameObject("~KillCam");
        instance = go.AddComponent<KillCam>();
        return instance;
    }

    void OnDestroy()
    {
        if (playing) End();
        if (label != null) label.Destroy();
        foreach (var t in new[] { shadeTexture, scanTexture })
        {
            if (t == null) continue;
            if (Application.isPlaying) Destroy(t); else DestroyImmediate(t);
        }
        if (instance == this) instance = null;
    }

    // DeathCrash.Play, once the crash is set up: plays the replay first (the
    // crash holds meanwhile). False -- the crash just plays -- when there is no
    // tape (the tutorial, edit mode, a death in the first frames of a run).
    public static bool TryBegin()
    {
        if (!Enabled || !Application.isPlaying) return false;
        var recorder = KillCamRecorder.Instance;
        if (recorder == null) return false;
        var footage = recorder.Clip(KillCamClip.LiveWindow);
        if (footage == null || footage.frameCount < MinFrames) return false;
        Ensure().Begin(footage);
        return true;
    }

    // Plays `footage` now (tests and previews call this directly).
    public void Begin(KillCamClip footage)
    {
        if (playing) End();
        clip = footage;
        if (clip == null || !clip.Playable) return;
        if (stage == null) stage = new KillCamStage(transform, "~KillCamStage");
        cam = Camera.main;
        if (cam != null) camSaved = cam.transform.position;
        real = 0f;
        phaseTime = labelTime = 0f;
        hold = KillCamClip.HoldEnd;
        glitchX = 0f;
        fxTick = -1;
        // the intro freezes on the fatal frame (the tape's last); without it the replay starts at once
        phase = Intro ? Phase.Killed : Phase.Play;
        clock = Intro ? clip.EndTime : 0f;
        Skipped = false;
        wasPressed = TouchInput.IsPressed;   // the finger already down is not a skip
        HideLive();
        playing = true;
        ShowOverlay();
        if (phase == Phase.Killed) KillCamAudio.Play(KillCamAudio.Cue.Killed);
        else KillCamAudio.Play(KillCamAudio.Cue.ReplayStart);
        PaintOverlay();
        Render();
    }

    // Every live renderer off, but the scrolling quads the tape drives.
    void HideLive()
    {
        hidden.Clear();
        keep.Clear();
        int last = clip.frameCount - 1;
        for (int i = 0; i < clip.meshCount[last]; i++)
        {
            var r = clip.MeshEntry(last, i).renderer;
            if (r != null) keep.Add(r);
        }
        int sceneCount = SceneManager.sceneCount;
        for (int s = 0; s < sceneCount; s++)
        {
            var scene = SceneManager.GetSceneAt(s);
            if (!scene.isLoaded) continue;
            scene.GetRootGameObjects(roots);
            foreach (var root in roots)
            {
                if (root == null || root == gameObject || !root.activeInHierarchy) continue;
                root.GetComponentsInChildren(false, scratch);
                foreach (var r in scratch)
                {
                    if (r == null || !r.enabled || r.forceRenderingOff || keep.Contains(r)) continue;
                    r.forceRenderingOff = true;
                    hidden.Add(r);
                }
            }
        }
        roots.Clear();
        scratch.Clear();
    }

    void ShowLive()
    {
        foreach (var r in hidden) if (r != null) r.forceRenderingOff = false;
        hidden.Clear();
        keep.Clear();
    }

    void Update()
    {
        if (!playing) return;
        float dt = Mathf.Clamp(Time.unscaledDeltaTime, 0f, MaxStep);
        bool pressed = TouchInput.IsPressed;
        bool tap = pressed && !wasPressed;
        wasPressed = pressed;
        if (tap && real >= SkipGuard) { Skipped = true; End(); return; }
        Step(dt);
    }

    // One step of `dt` real seconds (tests drive it by hand).
    public void Step(float dt)
    {
        if (!playing) return;
        real += dt;
        if (real >= MaxReal) { End(); return; }
        float end = clip.EndTime;
        phaseTime += dt;
        if (phase == Phase.Killed)
        {
            clock = end;   // frozen on the fatal frame
            if (phaseTime >= KilledSeconds)
            {
                phase = Phase.Rewind;
                phaseTime = 0f;
                KillCamAudio.Play(KillCamAudio.Cue.Rewind);
                KillCamMusic.BeginReverse();
            }
        }
        if (phase == Phase.Rewind)
        {
            float u = Mathf.Clamp01(phaseTime / RewindSeconds);
            if (u >= 1f)
            {
                clock = 0f;
                glitchX = 0f;
                phase = Phase.Play;
                phaseTime = 0f;
                KillCamMusic.Restore();   // the song carries on forward from where it paused
                KillCamAudio.Play(KillCamAudio.Cue.ReplayStart);
            }
            else clock = end * (1f - u * u * (3f - 2f * u));   // eased scrub back to the start
        }
        else if (phase == Phase.Play)
        {
            // the song stays loud and at normal pitch, behind a gentle low-pass that eases in and out
            KillCamMusic.StepMuffle(dt, clock >= end);
            if (clock < end) clock = Mathf.Min(end, clock + dt * KillCamClip.RateAt(end - clock));
            else
            {
                hold -= dt;
                if (hold <= 0f) { KillCamAudio.Play(KillCamAudio.Cue.ReplayEnd); End(); return; }
            }
        }
        PaintOverlay();
    }

    void LateUpdate()
    {
        if (playing) Render();
    }

    void Render()
    {
        stage.Draw(clip, clock);
        if (cam == null) return;
        float a;
        int f = clip.FrameAt(clock, out a);
        int g = Mathf.Min(f + 1, clip.frameCount - 1);
        Vector2 at = Vector2.Lerp(clip.cam[f], clip.cam[g], g == f ? 0f : a);
        cam.transform.position = new Vector3(at.x + glitchX, at.y, camSaved.z);
    }

    // The replay is over (played out, skipped, or the death was taken back):
    // the board, the camera and the crash are exactly as they were.
    public void End()
    {
        if (!playing) return;
        playing = false;
        KillCamMusic.Restore();   // every exit: the song is un-paused and the reverse stopped
        if (stage != null) stage.Hide();
        ShowLive();
        if (cam != null) cam.transform.position = camSaved;
        cam = null;
        clip = null;
        if (canvas != null) canvas.enabled = false;
        if (banner != null) banner.Hide();
        glitchX = 0f;
    }

    // DeathCrash.Cancel (a revive), scene teardown: stop without a trace.
    public static void Stop()
    {
        if (instance != null) instance.End();
    }

    // ---- the overlay: shades, the KILLED banner, the rewind look, the REPLAY label ----

    void ShowOverlay()
    {
        if (canvas == null) BuildOverlay();
        canvas.enabled = true;
        group.alpha = 0f;
        banner.Hide();
        label.Root.gameObject.SetActive(false);
        SetFx(0f);
        PlaceLabel();
    }

    void PaintOverlay()
    {
        if (canvas == null) return;
        group.alpha = Mathf.Clamp01(real / ShadeFade);

        // the KILLED banner holds the first phase
        if (phase == Phase.Killed) banner.Pose(phaseTime, KilledSeconds); else banner.Hide();

        // the REPLAY label arrives with the rewind and stays for the replay
        bool labelOn = phase != Phase.Killed;
        if (labelOn)
        {
            if (!label.Root.gameObject.activeSelf) { label.Root.gameObject.SetActive(true); labelTime = 0f; }
            labelTime += Time.unscaledDeltaTime > 0f ? Mathf.Min(Time.unscaledDeltaTime, MaxStep) : 0f;
            label.Pop(labelTime);
        }

        // the rewind look: tint, scanlines and glitch bands over the scrub, fading out as the replay starts
        float fx = 0f;
        if (phase == Phase.Rewind) fx = Mathf.Clamp01(phaseTime / FxFadeIn);
        else if (phase == Phase.Play) fx = Mathf.Clamp01(1f - phaseTime / FxFadeOut);
        SetFx(fx);
        glitchX = 0f;
        if (fx > 0f)
        {
            // jumps on 24 fps ticks like the rest of the game's effects
            int tick = Mathf.FloorToInt(real / BossArt.Tick);
            if (tick != fxTick) { fxTick = tick; StepBands(tick); }
            if (phase == Phase.Rewind) glitchX = (((tick * 7) % 5) - 2) * .035f * fx;
        }
    }

    void SetFx(float fx)
    {
        tint.color = new Color(.25f, .8f, 1f, .16f * fx);
        scan.color = new Color(.4f, .9f, 1f, .22f * fx);
        for (int i = 0; i < Bands; i++)
        {
            var c = bands[i].color;
            c.a = (fx > 0f ? .2f : 0f) * fx;
            bands[i].color = c;
        }
        tint.enabled = scan.enabled = fx > 0f;
        for (int i = 0; i < Bands; i++) bands[i].enabled = fx > 0f;
    }

    // A few thin full-width glitch strips jump to new rows each tick (solid fills, no outlines).
    void StepBands(int tick)
    {
        for (int i = 0; i < Bands; i++)
        {
            int h = (tick * 31 + i * 17) & 1023;
            float y = (h / 1023f - .5f) * 1422f;
            float height = 6f + ((tick + i * 5) % 4) * 9f;
            var rt = bands[i].rectTransform;
            rt.anchoredPosition = new Vector2(((tick + i) % 3 - 1) * 14f, y);
            rt.sizeDelta = new Vector2(0f, height);
        }
    }

    void BuildOverlay()
    {
        var go = new GameObject("~KillCamOverlay");
        go.transform.SetParent(transform, false);
        canvas = go.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = OverlayOrder;
        var scaler = go.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(800f, 1422f);
        scaler.matchWidthOrHeight = 0f;

        // the shades fade in on their own group; the banner and label do not
        var shadesGo = new GameObject("Shades", typeof(RectTransform), typeof(CanvasGroup));
        shadesGo.transform.SetParent(go.transform, false);
        shadesRt = (RectTransform)shadesGo.transform;
        shadesRt.anchorMin = Vector2.zero; shadesRt.anchorMax = Vector2.one;
        shadesRt.offsetMin = shadesRt.offsetMax = Vector2.zero;
        group = shadesGo.GetComponent<CanvasGroup>();
        group.interactable = false;
        group.blocksRaycasts = false;   // a tap reaches the skip

        shadeTexture = new Texture2D(1, 64, TextureFormat.RGBA32, false) { name = "KillCamShade", wrapMode = TextureWrapMode.Clamp };
        for (int y = 0; y < 64; y++)
        {
            float k = 1f - y / 63f;   // y 0: the screen edge, darkest
            shadeTexture.SetPixel(0, y, new Color(.02f, .02f, .05f, ShadeAlpha * k * k));
        }
        shadeTexture.Apply();
        Shade("ShadeBottom", false);
        Shade("ShadeTop", true);

        // the rewind look: a flat tint, tiled scanlines and glitch strips
        tint = NewFill("RewindTint", go.transform);
        scanTexture = new Texture2D(1, 4, TextureFormat.RGBA32, false) { name = "KillCamScan", wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Point };
        for (int y = 0; y < 4; y++) scanTexture.SetPixel(0, y, new Color(1f, 1f, 1f, y < 2 ? 1f : 0f));
        scanTexture.Apply();
        var scanGo = new GameObject("RewindScan", typeof(RectTransform), typeof(RawImage));
        scanGo.transform.SetParent(go.transform, false);
        scan = scanGo.GetComponent<RawImage>();
        scan.texture = scanTexture;
        scan.raycastTarget = false;
        scan.uvRect = new Rect(0f, 0f, 1f, 1422f / 4f);
        var srt = scan.rectTransform;
        srt.anchorMin = Vector2.zero; srt.anchorMax = Vector2.one;
        srt.offsetMin = srt.offsetMax = Vector2.zero;
        for (int i = 0; i < Bands; i++)
        {
            var b = NewFill("RewindBand" + i, go.transform);
            b.color = new Color(.7f, 1f, 1f, 0f);
            var rt = b.rectTransform;
            rt.anchorMin = new Vector2(0f, .5f); rt.anchorMax = new Vector2(1f, .5f);
            rt.pivot = new Vector2(.5f, .5f);
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            rt.sizeDelta = new Vector2(0f, 8f);
            bands[i] = b;
        }

        banner = KilledBanner.Build(go.transform);
        banner.Place(170f);
        label = ReplayLabel.Build(go.transform, (int)LabelFontSize);
        canvas.enabled = false;
    }

    static Image NewFill(string name, Transform parent)
    {
        var g = new GameObject(name, typeof(RectTransform), typeof(Image));
        g.transform.SetParent(parent, false);
        var img = g.GetComponent<Image>();
        img.raycastTarget = false;
        var rt = img.rectTransform;
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = rt.offsetMax = Vector2.zero;
        return img;
    }

    void Shade(string name, bool top)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(RawImage));
        go.transform.SetParent(shadesRt, false);
        var raw = go.GetComponent<RawImage>();
        raw.texture = shadeTexture;
        raw.raycastTarget = false;
        var rt = raw.rectTransform;
        rt.anchorMin = new Vector2(0f, top ? .8f : 0f);
        rt.anchorMax = new Vector2(1f, top ? 1f : .2f);
        rt.offsetMin = rt.offsetMax = Vector2.zero;
        if (top) rt.localScale = new Vector3(1f, -1f, 1f);   // darkest at the top edge
    }

    // The label sits top-centre, just below the top safe inset (the KILLED banner is mid-screen, clear of it).
    void PlaceLabel()
    {
        if (label == null) return;
        float units = 800f / Mathf.Max(1f, ScreenInfo.Width);
        var rt = label.Root;
        rt.anchorMin = rt.anchorMax = new Vector2(.5f, 1f);
        rt.pivot = new Vector2(.5f, 1f);
        rt.anchoredPosition = new Vector2(0f, -(ScreenInfo.SafeTopInset * units + 28f));
    }
}
