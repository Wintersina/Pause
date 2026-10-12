using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// The how-to-play loop on the Options screen (leaderboardS3), next to the
// Tutorial (replay) button: a muted 7 s clip of the game's one trick -- let go
// (time freezes), touch (teleport) -- so a player deciding whether to replay the
// tutorial can see what it teaches. Nothing in the tutorial scene itself changes
// (no extra beat, no timer, TutorialPolishTest untouched).
//
// Built at runtime like START WORLD / SOUND. Hidden until the clip's first frame
// has decoded (no empty frame, no load cost); if the clip cannot play the screen
// is exactly what it was.
public class HowToPlayClip : MonoBehaviour
{
    public const string File = "tutorial_demo.mp4";   // 540x960, 7.2 s
    public const int Width = 540, Height = 960;
    public const string FrameName = "HowToPlayClip";
    public const float ClipWidth = 128f, ClipHeight = ClipWidth * Height / Width;   // canvas units
    public const float Border = 3f;

    // Previews / tests: show this still instead of decoding the clip.
    public static Texture PreviewTexture;

    public LoopingClip Clip { get; private set; }
    public RectTransform Frame { get; private set; }
    RawImage image;

    [RuntimeInitializeOnLoadMethod]
    static void Init()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (scene.name == "leaderboardS3" && FindFirstObjectByType<HowToPlayClip>() == null)
            new GameObject("~HowToPlayClip").AddComponent<HowToPlayClip>();
    }

    void Start()
    {
        if (PreviewTexture != null) { Build(PreviewTexture); return; }
        Clip = LoopingClip.Create(gameObject, File, Width, Height);
        if (Clip == null) return;
        Clip.BecameReady += () => Build(Clip.Texture);
    }

    // Frame position: the canvas' bottom-left corner area beside the Tutorial row.
    public static Vector2 FramePosition = new Vector2(-300f, -333f);

    public void Build(Texture texture)
    {
        if (Frame != null) return;
        var template = SceneUtil.FindAny("Tutorial");
        if (template == null) return;
        var parent = template.transform.parent;

        var frame = new GameObject(FrameName, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        Frame = (RectTransform)frame.transform;
        Frame.SetParent(parent, false);
        Frame.anchorMin = Frame.anchorMax = Frame.pivot = new Vector2(0.5f, 0.5f);
        Frame.anchoredPosition = FramePosition;
        Frame.sizeDelta = new Vector2(ClipWidth + 2f * Border, ClipHeight + 2f * Border);
        var border = frame.GetComponent<Image>();
        border.color = new Color(0.78f, 0.82f, 0.95f, 0.9f);
        border.raycastTarget = false;

        var inner = new GameObject("Clip", typeof(RectTransform), typeof(CanvasRenderer), typeof(RawImage));
        var rt = (RectTransform)inner.transform;
        rt.SetParent(Frame, false);
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = new Vector2(Border, Border); rt.offsetMax = new Vector2(-Border, -Border);
        image = inner.GetComponent<RawImage>();
        image.texture = texture;
        image.raycastTarget = false;
    }

    void OnDestroy()
    {
        if (Frame != null) Destroy(Frame.gameObject);
    }
}
