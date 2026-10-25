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
// A fresh tap after SkipGuard skips to the crash, so the replay never traps
// the player; the whole replay is about RealSeconds(LiveWindow) (~3.4 s).
// Soft shades top and bottom and a small blinking REPLAY tag mark it as a
// replay (no boxes, no lines).
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

    // Tests / previews: no kill cam at all (the crash plays at once, as before).
    public static bool Enabled = true;

    static KillCam instance;
    public static KillCam Current { get { return instance; } }
    public static bool Playing { get { return instance != null && instance.playing; } }

    KillCamStage stage;
    KillCamClip clip;
    readonly List<Renderer> hidden = new List<Renderer>(1024);
    readonly List<GameObject> roots = new List<GameObject>(64);
    readonly List<Renderer> scratch = new List<Renderer>(512);
    readonly List<Renderer> keep = new List<Renderer>(8);
    float clock, real, hold;
    bool playing, wasPressed;
    Camera cam;
    Vector3 camSaved;

    Canvas canvas;
    CanvasGroup group;
    RectTransform tagRt;
    Image dot;
    Texture2D shadeTexture;

    // ---- read-outs (tests) ----
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
        if (shadeTexture != null)
        {
            if (Application.isPlaying) Destroy(shadeTexture); else DestroyImmediate(shadeTexture);
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
        clock = 0f;
        real = 0f;
        hold = KillCamClip.HoldEnd;
        Skipped = false;
        wasPressed = TouchInput.IsPressed;   // the finger already down is not a skip
        HideLive();
        playing = true;
        ShowOverlay();
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
        if (clock < end) clock = Mathf.Min(end, clock + dt * KillCamClip.RateAt(end - clock));
        else
        {
            hold -= dt;
            if (hold <= 0f) { End(); return; }
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
        cam.transform.position = new Vector3(at.x, at.y, camSaved.z);
    }

    // The replay is over (played out, skipped, or the death was taken back):
    // the board, the camera and the crash are exactly as they were.
    public void End()
    {
        if (!playing) return;
        playing = false;
        if (stage != null) stage.Hide();
        ShowLive();
        if (cam != null) cam.transform.position = camSaved;
        cam = null;
        clip = null;
        if (canvas != null) canvas.enabled = false;
    }

    // DeathCrash.Cancel (a revive), scene teardown: stop without a trace.
    public static void Stop()
    {
        if (instance != null) instance.End();
    }

    // ---- the overlay: soft shades top and bottom, a blinking REPLAY tag ----

    void ShowOverlay()
    {
        if (canvas == null) BuildOverlay();
        canvas.enabled = true;
        group.alpha = 0f;
        PlaceTag();
    }

    void PaintOverlay()
    {
        if (canvas == null) return;
        group.alpha = Mathf.Clamp01(real / ShadeFade);
        if (dot != null) dot.enabled = Mathf.Repeat(real, .8f) < .5f;
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
        group = go.AddComponent<CanvasGroup>();
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

        var tagGo = new GameObject("Tag", typeof(RectTransform));
        tagGo.transform.SetParent(go.transform, false);
        tagRt = (RectTransform)tagGo.transform;
        tagRt.anchorMin = tagRt.anchorMax = tagRt.pivot = new Vector2(0f, 1f);
        tagRt.sizeDelta = new Vector2(260f, 48f);

        var dotGo = new GameObject("Dot", typeof(RectTransform), typeof(Image));
        dotGo.transform.SetParent(tagRt, false);
        dot = dotGo.GetComponent<Image>();
        dot.sprite = CodexUi.CodexSprite("cx_circle");
        dot.color = AkiraPalette.Red;
        dot.raycastTarget = false;
        var drt = dot.rectTransform;
        drt.anchorMin = drt.anchorMax = new Vector2(0f, .5f);
        drt.sizeDelta = new Vector2(20f, 20f);
        drt.anchoredPosition = new Vector2(10f, 0f);

        var textGo = new GameObject("Label", typeof(RectTransform), typeof(Text));
        textGo.transform.SetParent(tagRt, false);
        var text = textGo.GetComponent<Text>();
        text.font = Font();
        text.fontSize = 30;
        text.fontStyle = FontStyle.Bold;
        text.color = AkiraPalette.WithAlpha(AkiraPalette.Bone, .85f);
        text.alignment = TextAnchor.MiddleLeft;
        text.text = "REPLAY";
        text.raycastTarget = false;
        var trt = text.rectTransform;
        trt.anchorMin = new Vector2(0f, 0f);
        trt.anchorMax = new Vector2(1f, 1f);
        trt.offsetMin = new Vector2(32f, 0f);
        trt.offsetMax = Vector2.zero;
        canvas.enabled = false;
    }

    void Shade(string name, bool top)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(RawImage));
        go.transform.SetParent(canvas.transform, false);
        var raw = go.GetComponent<RawImage>();
        raw.texture = shadeTexture;
        raw.raycastTarget = false;
        var rt = raw.rectTransform;
        rt.anchorMin = new Vector2(0f, top ? .8f : 0f);
        rt.anchorMax = new Vector2(1f, top ? 1f : .2f);
        rt.offsetMin = rt.offsetMax = Vector2.zero;
        if (top) rt.localScale = new Vector3(1f, -1f, 1f);   // darkest at the top edge
    }

    // The tag sits in the safe area's top-left corner.
    void PlaceTag()
    {
        if (tagRt == null) return;
        float units = 800f / Mathf.Max(1f, ScreenInfo.Width);
        var safe = ScreenInfo.SafeArea;
        tagRt.anchoredPosition = new Vector2(safe.xMin * units + 28f, -(ScreenInfo.SafeTopInset * units + 28f));
    }

    static Font font;
    static Font Font()
    {
        if (font == null) font = CodexUi.FindFont();
        return font;
    }
}
