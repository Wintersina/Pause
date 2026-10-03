using UnityEngine;
using UnityEngine.UI;

// "PRESS BACK AGAIN TO QUIT": the home screen's guard against an accidental
// system-back quit (BackNavigator.HomeBack). A small angular ink-outlined cel
// -- the codex toast's plate, per docs/art-style.md -- pops up above the
// footer, stays for the quit window and fades. It never blocks input (no
// raycaster, no raycast targets) and belongs to the scene it was shown in.
public class BackQuitToast : MonoBehaviour
{
    public const string Message = "PRESS BACK AGAIN TO QUIT";
    public const float Width = 440f, Height = 64f;
    public const float BottomMargin = 150f;   // clears the home Quit button
    public const float InDuration = .18f, OutDuration = .3f;

    static BackQuitToast instance;

    RectTransform box;
    CanvasGroup group;
    Canvas canvas;
    Text label;
    float shownAt = -1f;

    public static BackQuitToast Current { get { return instance; } }
    public bool Showing { get { return shownAt >= 0f && gameObject.activeSelf; } }
    public string ShownText { get { return label != null ? label.text : null; } }

    public static BackQuitToast Show()
    {
        if (instance == null) instance = Build();
        instance.gameObject.SetActive(true);
        instance.shownAt = BackNavigator.Clock();
        instance.ApplyAt(0f);
        return instance;
    }

    static BackQuitToast Build()
    {
        var c = CodexUi.NewOverlayCanvas("~BackQuitToast", 650, false);
        var toast = c.gameObject.AddComponent<BackQuitToast>();
        toast.canvas = c;
        toast.BuildUi(CodexUi.FindFont());
        return toast;
    }

    void BuildUi(Font font)
    {
        var go = new GameObject("Toast", typeof(RectTransform), typeof(CanvasGroup));
        go.transform.SetParent(transform, false);
        box = (RectTransform)go.transform;
        box.anchorMin = box.anchorMax = new Vector2(.5f, 0f);
        box.pivot = new Vector2(.5f, 0f);
        box.sizeDelta = new Vector2(Width, Height);
        group = go.GetComponent<CanvasGroup>();
        group.interactable = false;
        group.blocksRaycasts = false;

        var bg = CodexUi.NewImage("Background", box, CodexUi.CodexSprite("cx_toast"), Color.white, true);
        bg.raycastTarget = false;
        CodexUi.Stretch(bg.rectTransform);

        label = CodexUi.NewText("Label", box, font, Message, 22, CodexUi.Title, TextAnchor.MiddleCenter);
        label.raycastTarget = false;
        label.horizontalOverflow = HorizontalWrapMode.Wrap;
        label.resizeTextForBestFit = true;
        label.resizeTextMinSize = 14;
        label.resizeTextMaxSize = 22;
        CodexUi.AddOutline(label.gameObject, CodexUi.Ink, 1.5f);
        CodexUi.Place(label.rectTransform, CodexUi.Centered(0f, 0f, Width - 48f, Height - 12f));
    }

    void Update()
    {
        if (shownAt < 0f) return;
        float t = BackNavigator.Clock() - shownAt;
        if (t >= BackNavigator.QuitWindow + OutDuration)
        {
            shownAt = -1f;
            gameObject.SetActive(false);
            return;
        }
        ApplyAt(t);
    }

    void ApplyAt(float t)
    {
        float a;
        if (t < InDuration) a = CodexUi.EaseOutCubic(t / InDuration);
        else if (t < BackNavigator.QuitWindow) a = 1f;
        else a = 1f - Mathf.Clamp01((t - BackNavigator.QuitWindow) / OutDuration);
        group.alpha = a;
        float sf = Mathf.Max(canvas.scaleFactor, .0001f);
        float safeBottom = Screen.safeArea.yMin / sf;
        float slide = (1f - CodexUi.EaseOutCubic(t / InDuration)) * -18f;
        box.anchoredPosition = new Vector2(0f, safeBottom + BottomMargin + slide);
    }

    void OnDestroy()
    {
        if (instance == this) instance = null;
    }
}
