using UnityEngine;
using UnityEngine.SceneManagement;

// A looping Space gameplay clip (a freeze and a teleport in it) behind the
// title screen's menu. User-requested; it replaces the rolled menu backdrop on
// the title screen only (credits, options and the shop keep MenuBackdrop).
//
// Where it draws: a quad in world space, covering the camera's view, at
// sorting order VideoOrder -- above the backdrop tile layers (BaseOrder -500..)
// and below the title traffic and logo (-300 and up), so ships still fly over
// it and every menu button (a screen-space overlay) is above all of it. It is
// tinted down to Dim so the logo and the buttons stay readable.
//
// Loading: nothing is created until the clip has decoded its first frame
// (LoopingClip prepares in the background), so the title opens with the normal
// backdrop exactly as before and the video fades in over FadeSeconds. If the
// clip is missing or fails, the backdrop simply stays.
public class TitleVideo : MonoBehaviour
{
    public const string File = "title_loop.mp4";      // 540x960, 12.5 s, seamless crossfaded loop
    public const int Width = 540, Height = 960;
    public const int VideoOrder = -400;
    public const float Dim = 0.5f;                    // tint brightness over the video
    public const float FadeSeconds = 0.8f;

    public static TitleVideo Instance { get; private set; }

    public LoopingClip Clip { get; private set; }
    public MeshRenderer Quad { get; private set; }
    public float Alpha { get; private set; }
    Material material;
    Camera cam;
    float fade;

    void Awake()
    {
        Instance = this;
        Clip = LoopingClip.Create(gameObject, File, Width, Height);
        if (Clip == null) { enabled = false; return; }
        Clip.BecameReady += Show;
    }

    void Show()
    {
        if (Clip == null || Clip.Texture == null) return;
        ShowTexture(Clip.Texture);
    }

    // Puts `texture` on the quad (the decoded clip; previews pass a still frame).
    public void ShowTexture(Texture texture)
    {
        if (Quad != null || texture == null) { Debug.LogWarning("[TitleVideo] ShowTexture skipped: quad=" + Quad + " texture=" + texture); return; }
        var shader = Shader.Find("Sprites/Default");
        if (shader == null) shader = Shader.Find("UI/Default");
        if (shader == null) { Debug.LogWarning("[TitleVideo] no sprite shader; keeping the still backdrop"); return; }
        material = new Material(shader) { mainTexture = texture, color = new Color(Dim, Dim, Dim, 0f) };
        var go = new GameObject("Title video quad");
        go.transform.SetParent(transform, false);
        go.AddComponent<MeshFilter>().sharedMesh = QuadMesh();
        Quad = go.AddComponent<MeshRenderer>();
        Quad.sharedMaterial = material;
        Quad.sortingOrder = VideoOrder;
        Quad.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        Quad.receiveShadows = false;
        // The scene's own full-screen backdrop quad (MenuBackdrop switches it off too)
        // must not sit over the video when the rolled backdrop failed to build.
        foreach (string n in CameraFit.BackdropNames)
        {
            var bg = GameObject.Find(n);
            var r = bg != null ? bg.GetComponent<Renderer>() : null;
            if (r != null) r.enabled = false;
        }
        Fit();
    }

    static Mesh QuadMesh()
    {
        var m = new Mesh { name = "TitleVideoQuad" };
        m.vertices = new[] { new Vector3(-.5f, -.5f), new Vector3(.5f, -.5f), new Vector3(.5f, .5f), new Vector3(-.5f, .5f) };
        m.uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) };
        m.triangles = new[] { 0, 2, 1, 0, 3, 2 };
        m.bounds = new Bounds(Vector3.zero, new Vector3(1000f, 1000f, 1f));
        return m;
    }

    // Cover the view, centre-cropping the 9:16 frame to the screen's shape.
    public void Fit()
    {
        if (Quad == null) return;
        if (cam == null) cam = Camera.main;
        float hh = 5f, hw = 2.8f, cx = 0f, cy = 0f;
        if (cam != null && cam.orthographic)
        {
            hh = cam.orthographicSize; hw = hh * cam.aspect;
            cx = cam.transform.position.x; cy = cam.transform.position.y;
        }
        Quad.transform.position = new Vector3(cx, cy, 5f);
        Quad.transform.localScale = new Vector3(hw * 2f * 1.02f, hh * 2f * 1.02f, 1f);
        float view = hw / hh, video = (float)Width / Height;
        Vector2 scale = Vector2.one, offset = Vector2.zero;
        if (view < video) { scale.x = view / video; offset.x = (1f - scale.x) * .5f; }
        else { scale.y = video / view; offset.y = (1f - scale.y) * .5f; }
        material.mainTextureScale = scale;
        material.mainTextureOffset = offset;
    }

    // Previews: jump the fade (0..1) and refit.
    public void SetFade(float f)
    {
        fade = Mathf.Clamp01(f);
        if (Quad == null) return;
        Alpha = fade;
        material.color = new Color(Dim, Dim, Dim, Mathf.SmoothStep(0f, 1f, fade));
        Fit();
    }

    void Update()
    {
        if (Clip != null && Clip.Failed) { Hide(); return; }
        if (Quad == null) return;
        fade = Mathf.Min(1f, fade + Time.unscaledDeltaTime / FadeSeconds);
        Alpha = fade;
        material.color = new Color(Dim, Dim, Dim, Mathf.SmoothStep(0f, 1f, fade));
        Fit();
    }

    void Hide()
    {
        if (Quad != null) Destroy(Quad.gameObject);
        Quad = null;
        enabled = false;
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
        if (material != null) Destroy(material);
    }
}

public static class TitleVideoBootstrap
{
    [RuntimeInitializeOnLoadMethod]
    static void Init()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (scene.name != "startS4") return;
        if (Object.FindFirstObjectByType<TitleVideo>() != null) return;
        new GameObject("~TitleVideo").AddComponent<TitleVideo>();
    }
}
