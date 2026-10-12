using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// The how-to-play loop on the Options screen (leaderboardS3), centred under the
// last row (Back) with a small HOW TO PLAY label, about a third of the screen
// wide (less on short phones, hidden when there is no clear space): a muted 7 s clip of the game's one trick -- let go
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
    public const string LabelText = "HOW TO PLAY";
    // Target width as a share of the Options canvas (~30-35% of the screen width),
    // shrunk to the clear space under the last row; hidden when that is too small.
    public const float WidthShare = 0.33f;
    public const float MinHeight = 150f;      // canvas units: under this the clip is not shown
    public const float Border = 3f;
    public const float LabelHeight = 34f;
    public const float Gap = 8f;
    public const float MarginAbove = 36f;     // clear space between the last row and the label
    public const float MarginBelow = 60f;     // above the safe area's bottom edge

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

    RectTransform group, label;

    public void Build(Texture texture)
    {
        if (Frame != null) return;
        var template = SceneUtil.FindAny("Tutorial");
        if (template == null) return;
        var parent = (RectTransform)template.transform.parent;

        var g = new GameObject(FrameName + "Group", typeof(RectTransform));
        group = (RectTransform)g.transform;
        group.SetParent(parent, false);
        group.anchorMin = group.anchorMax = group.pivot = new Vector2(0.5f, 0.5f);

        // "HOW TO PLAY" in the screen's own button font, small and quiet
        var l = new GameObject("Label", typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
        label = (RectTransform)l.transform;
        label.SetParent(group, false);
        var text = l.GetComponent<Text>();
        var tmpl = template.GetComponentInChildren<Text>(true);
        if (tmpl != null) { text.font = tmpl.font; text.fontStyle = tmpl.fontStyle; }
        text.text = LabelText;
        text.alignment = TextAnchor.MiddleCenter;
        text.color = new Color(0.78f, 0.82f, 0.95f, 0.95f);
        text.resizeTextForBestFit = true; text.resizeTextMinSize = 20; text.resizeTextMaxSize = 30;
        text.horizontalOverflow = HorizontalWrapMode.Overflow;
        text.raycastTarget = false;

        var frame = new GameObject(FrameName, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        Frame = (RectTransform)frame.transform;
        Frame.SetParent(group, false);
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
        Relayout();
    }

    int laidW = -1, laidH = -1;

    void Update()
    {
        if (group != null && (laidW != ScreenInfo.Width || laidH != ScreenInfo.Height)) Relayout();
    }

    // Centred under the last row, in the clear space above the safe area's bottom edge.
    public void Relayout()
    {
        if (group == null) return;
        laidW = ScreenInfo.Width; laidH = ScreenInfo.Height;
        var parent = (RectTransform)group.parent;
        var canvas = parent.GetComponentInParent<Canvas>();
        float scale = canvas != null && canvas.scaleFactor > 0f ? canvas.scaleFactor : 1f;
        Rect r = parent.rect;
        float lowest = r.yMax;
        foreach (var sel in parent.GetComponentsInChildren<UnityEngine.UI.Selectable>(false))
        {
            if (sel.transform.IsChildOf(group) || !sel.gameObject.activeInHierarchy) continue;
            var corners = new Vector3[4];
            ((RectTransform)sel.transform).GetWorldCorners(corners);
            for (int i = 0; i < 4; i++) lowest = Mathf.Min(lowest, parent.InverseTransformPoint(corners[i]).y);
        }
        float floor = r.yMin + ScreenInfo.SafeArea.yMin / scale + MarginBelow;
        float top = lowest - MarginAbove;
        float avail = top - floor - LabelHeight - Gap;
        float h = Mathf.Min(WidthShare * r.width * Height / Width, avail);
        bool show = h >= MinHeight;
        group.gameObject.SetActive(show);
        if (!show) return;
        float w = h * Width / Height;
        float total = LabelHeight + Gap + h;
        group.sizeDelta = new Vector2(Mathf.Max(w, 220f), total);
        group.anchoredPosition = new Vector2(0f, top - total * 0.5f);
        label.anchorMin = label.anchorMax = new Vector2(0.5f, 1f);
        label.pivot = new Vector2(0.5f, 1f);
        label.sizeDelta = new Vector2(Mathf.Max(w, 220f), LabelHeight);
        label.anchoredPosition = Vector2.zero;
        Frame.anchorMin = Frame.anchorMax = new Vector2(0.5f, 0f);
        Frame.pivot = new Vector2(0.5f, 0f);
        Frame.sizeDelta = new Vector2(w + 2f * Border, h + 2f * Border);
        Frame.anchoredPosition = Vector2.zero;
    }

    void OnDestroy()
    {
        if (group != null) Destroy(group.gameObject);
    }
}
