using System;
using UnityEngine;
using UnityEngine.UI;

// The codex's LAST STAND viewer: the saved kill cam of the latest high-score
// death (KillCamArchive), full screen, played the way the live kill cam
// plays it (KillCamClip.RateAt: slowing to very slow motion into the fatal
// hit, then on through the death; the normal framing, no zoom), looping --
// approach, collision, death, a short hold -- with a dip through black.
//
// Built like CodexBackdropViewer: the clip is drawn by a KillCamStage far off
// to the side of the menu scene (OffsetX), by the viewer's own orthographic
// camera into a screen-sized RenderTexture shown on its overlay canvas; the
// codex underneath is untouched. Text over the picture (no boxes, no lines):
// the score and how it ended, a soft shade behind it, BACK. BACK / Escape /
// Android back close it and free everything (clip textures, stage, camera,
// RenderTexture, canvas).
public sealed class CodexReplayViewer : MonoBehaviour
{
    public const float OffsetX = -6000f;
    public const int MaxRenderEdge = 1920;
    public const float LoopPause = .9f;          // after the hold, before the dip
    public const float DipSeconds = .2f;
    public const float Margin = 16f;
    public const float BackWidth = 288f, BackHeight = 100f;
    public const int SortingOrder = 600;
    public const string Unavailable = "This replay could not be loaded.";

    static CodexReplayViewer instance;
    public static CodexReplayViewer Current { get { return instance; } }
    public static bool IsOpen { get { return instance != null; } }
    public static event Action Closed;

    public static CodexReplayViewer Open(Font font)
    {
        if (instance != null) instance.Close();
        var canvas = CodexUi.NewOverlayCanvas("CodexReplayViewer", SortingOrder, true);
        var viewer = canvas.gameObject.AddComponent<CodexReplayViewer>();
        viewer.canvas = canvas;
        viewer.font = font != null ? font : CodexUi.FindFont();
        instance = viewer;
        viewer.Begin();
        return viewer;
    }

    Canvas canvas;
    Font font;
    RectTransform root, backRt, titleRt, subRt, shadeRt, tagRt;
    RawImage view;
    Image fade;
    ReplayLabel replayLabel;
    Text title, sub;
    Button backBtn;
    Texture2D shadeTexture;
    GameObject worldGo;
    Camera cam;
    RenderTexture rt;
    int rtW, rtH, lastW, lastH;
    Rect lastSafe;
    KillCamArchive.Loaded loaded;
    KillCamStage stage;
    float clock, after, since, shown;
    bool closed;

    public bool HasClip { get { return loaded != null && loaded.clip != null; } }
    public KillCamClip Clip { get { return HasClip ? loaded.clip : null; } }
    public float ClipTime { get { return clock; } }
    public Text Title { get { return title; } }
    public Text Sub { get { return sub; } }
    public Button BackButton { get { return backBtn; } }
    public Camera ViewCamera { get { return cam; } }
    public KillCamStage Stage { get { return stage; } }
    public int Loops { get; private set; }

    void Begin()
    {
        root = (RectTransform)transform;
        loaded = KillCamArchive.Load();
        BuildView();
        BuildHud();
        BackNavigator.Register(this, OnBackPressed);
        EnsureTarget();
        Fit();
        Paint();
        RenderFrame();
    }

    void BuildView()
    {
        view = new GameObject("View", typeof(RectTransform), typeof(RawImage)).GetComponent<RawImage>();
        view.transform.SetParent(root, false);
        CodexUi.Stretch(view.rectTransform);
        view.color = Color.white;
        view.raycastTarget = true;   // opaque: hides and blocks the codex underneath

        shadeTexture = new Texture2D(1, 64, TextureFormat.RGBA32, false) { name = "CodexReplayShade", wrapMode = TextureWrapMode.Clamp };
        for (int y = 0; y < 64; y++)
        {
            float k = 1f - y / 63f;
            shadeTexture.SetPixel(0, y, new Color(.02f, .02f, .05f, .7f * k * k));
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

    void BuildHud()
    {
        title = CodexUi.NewText("Title", root, font, "", 44, CodexUi.Title, TextAnchor.MiddleCenter, true);
        title.verticalOverflow = VerticalWrapMode.Truncate;
        title.resizeTextForBestFit = true;
        title.resizeTextMinSize = 22;
        title.resizeTextMaxSize = 44;
        title.fontStyle = FontStyle.BoldAndItalic;
        CodexUi.AddOutline(title.gameObject, CodexUi.Ink, 3f);
        titleRt = title.rectTransform;

        sub = CodexUi.NewText("Sub", root, font, "", 22, CodexUi.Body, TextAnchor.MiddleCenter, true);
        sub.verticalOverflow = VerticalWrapMode.Truncate;
        sub.resizeTextForBestFit = true;
        sub.resizeTextMinSize = 12;
        sub.resizeTextMaxSize = 22;
        CodexUi.AddOutline(sub.gameObject, CodexUi.Ink, 2f);
        subRt = sub.rectTransform;

        // the REPLAY label, lettered like the PAUSED caption (as in a run)
        tagRt = CodexUi.NewRect("Tag", root);
        replayLabel = ReplayLabel.Build(tagRt, 34);
        replayLabel.Root.anchorMin = replayLabel.Root.anchorMax = new Vector2(.5f, .5f);
        replayLabel.Root.anchoredPosition = Vector2.zero;

        // BACK: the codex's own cel button
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
    }

    void Fit()
    {
        lastW = ScreenInfo.Width;
        lastH = ScreenInfo.Height;
        lastSafe = ScreenInfo.SafeArea;
        Canvas.ForceUpdateCanvases();
        ApplyLayout(CodexUi.SafeAreaUnits(canvas));
    }

    public void ApplyLayout(Rect safe)
    {
        float left = safe.xMin + Margin, right = safe.xMax - Margin, bottom = safe.yMin + Margin, top = safe.yMax - Margin;
        float w = Mathf.Max(0f, right - left);
        CodexUi.Place(backRt, new Rect(left, top - BackHeight, Mathf.Min(BackWidth, w), BackHeight));
        CodexUi.Place(tagRt, new Rect(right - 200f, top - BackHeight, 200f, BackHeight));
        var subRect = CodexUi.Centered(safe.center.x, bottom + 40f, w, 64f);
        CodexUi.Place(subRt, subRect);
        CodexUi.Place(titleRt, CodexUi.Centered(safe.center.x, subRect.yMax + 4f + 30f, w, 60f));
        CodexUi.Place(shadeRt, new Rect(safe.xMin - 400f, safe.yMin - 400f, safe.width + 800f, 400f + 200f));
    }

    void Paint()
    {
        var r = loaded != null ? loaded.meta : null;
        if (r == null || !HasClip)
        {
            title.text = "LAST STAND";
            sub.text = Unavailable;
            return;
        }
        title.text = "SCORE " + r.score.ToString("N0");
        var sb = new System.Text.StringBuilder(64);
        if (!string.IsNullOrEmpty(r.world)) sb.Append(r.world.ToUpperInvariant());
        string ship = ShipId.NameOf(r.ship);
        if (!string.IsNullOrEmpty(ship)) { if (sb.Length > 0) sb.Append("   "); sb.Append(ship.ToUpperInvariant()); }
        string killer = DamageLog.NameOf(r.killer);
        if (!string.IsNullOrEmpty(killer)) { if (sb.Length > 0) sb.Append('\n'); sb.Append("KILLED BY ").Append(killer.ToUpperInvariant()); }
        sub.text = sb.ToString();
    }

    void EnsureTarget()
    {
        float scale = Mathf.Min(1f, MaxRenderEdge / (float)Mathf.Max(1, Mathf.Max(ScreenInfo.Width, ScreenInfo.Height)));
        int w = Mathf.Max(16, Mathf.RoundToInt(ScreenInfo.Width * scale));
        int h = Mathf.Max(16, Mathf.RoundToInt(ScreenInfo.Height * scale));
        if (rt != null && w == rtW && h == rtH) return;
        ReleaseTarget();
        rtW = w; rtH = h;
        rt = new RenderTexture(w, h, 24) { name = "CodexReplayViewer", filterMode = FilterMode.Bilinear, useMipMap = false };
        rt.Create();
        if (worldGo == null)
        {
            worldGo = new GameObject("~CodexReplayWorld");
            worldGo.transform.position = new Vector3(OffsetX, 0f, 0f);
            var camGo = new GameObject("~CodexReplayCamera");
            camGo.transform.SetParent(worldGo.transform, false);
            cam = camGo.AddComponent<Camera>();
            cam.orthographic = true;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = HasClip ? loaded.clip.background : Color.black;
            cam.nearClipPlane = .1f;
            cam.farClipPlane = 200f;
            cam.allowHDR = false;
            cam.allowMSAA = false;
            cam.enabled = false;     // rendered by hand, once a frame
            stage = new KillCamStage(worldGo.transform, "~CodexReplayStage");
            stage.Offset = new Vector3(OffsetX, 0f, 0f);
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

    void RenderFrame()
    {
        if (cam == null || rt == null || closed) return;
        var clip = Clip;
        if (clip == null) { cam.Render(); return; }
        stage.Draw(clip, clock);
        float a;
        int f = clip.FrameAt(clock, out a);
        int g = Mathf.Min(f + 1, clip.frameCount - 1);
        Vector2 at = Vector2.Lerp(clip.cam[f], clip.cam[g], g == f ? 0f : a);
        // the run's own framing: its whole width and height in view, no zoom
        float aspect = rtW / (float)Mathf.Max(1, rtH);
        cam.orthographicSize = Mathf.Max(clip.camHalfHeight, clip.camHalfHeight * clip.camAspect / Mathf.Max(.01f, aspect));
        cam.transform.position = new Vector3(OffsetX + at.x, at.y, clip.camZ);
        cam.Render();
    }

    void Update()
    {
        if (closed) return;
        if (ScreenInfo.Width != lastW || ScreenInfo.Height != lastH || ScreenInfo.SafeArea != lastSafe)
        {
            EnsureTarget();
            Fit();
        }
        Step(Mathf.Min(Time.unscaledDeltaTime, 1f / 20f));
    }

    // One frame of `dt` real seconds: the clock (slowing into the hit), the hold, the loop's dip.
    public void Step(float dt)
    {
        if (closed) return;
        shown += dt;
        if (replayLabel != null) replayLabel.Pop(shown);
        var clip = Clip;
        if (clip != null)
        {
            float end = clip.EndTime;
            float wait = KillCamClip.HoldEnd + LoopPause;
            if (clock < end) { clock = Mathf.Min(end, clock + dt * clip.Rate(clock)); since += dt; }
            else
            {
                after += dt;
                if (after >= wait + DipSeconds) { clock = 0f; after = 0f; since = 0f; Loops++; }
            }
            float alpha = 0f;
            if (after > wait) alpha = Mathf.Clamp01((after - wait) / DipSeconds);              // dipping out
            else if (Loops > 0 && after <= 0f) alpha = Mathf.Clamp01(1f - since / DipSeconds); // coming back in
            fade.color = new Color(0f, 0f, 0f, alpha);
        }
        RenderFrame();
    }

    bool OnBackPressed()
    {
        if (closed) return false;
        Close();
        return true;
    }

    public void Close()
    {
        if (closed) return;
        closed = true;
        BackNavigator.Unregister(this);
        Release();
        if (instance == this) instance = null;
        DestroyObject(gameObject);
        if (Application.isPlaying) Resources.UnloadUnusedAssets();
        var handler = Closed;
        if (handler != null) handler();
    }

    void Release()
    {
        if (stage != null) { stage.Destroy(); stage = null; }
        ReleaseTarget();
        if (worldGo != null) DestroyObject(worldGo);
        worldGo = null;
        cam = null;
        if (loaded != null) { loaded.Dispose(); loaded = null; }
        if (shadeTexture != null) DestroyObject(shadeTexture);
        shadeTexture = null;
    }

    void OnDestroy()
    {
        if (closed) return;
        closed = true;
        BackNavigator.Unregister(this);
        Release();
        if (instance == this) instance = null;
    }

    static void DestroyObject(UnityEngine.Object o)
    {
        if (o == null) return;
        if (Application.isPlaying) UnityEngine.Object.Destroy(o);
        else UnityEngine.Object.DestroyImmediate(o);
    }
}
