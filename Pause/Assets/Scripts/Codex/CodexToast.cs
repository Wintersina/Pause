using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// "NEW CODEX ENTRY" toast: a small angular ink-outlined cel that drops in under the
// top of the safe area during a run the first time something is discovered,
// then fades away. It never blocks play -- no raycaster on its canvas and no
// raycast targets -- and queues a few names if several arrive at once.
//
// Runs on unscaled time (the world freezes at timeScale 0 between touches)
// and allocates nothing per frame; it switches itself off when idle.
public class CodexToast : MonoBehaviour
{
    public const float Width = 460f, Height = 76f;
    public const float TopMargin = 236f;   // clears the HUD stat panel (top-left)
    public const float InDuration = .25f, HoldDuration = 2.2f, OutDuration = .35f;
    const int QueueSize = 4;

    static CodexToast instance;

    RectTransform box;
    CanvasGroup group;
    Image icon;
    Text heading, nameText;
    Canvas canvas;

    readonly CodexEntry[] queue = new CodexEntry[QueueSize];
    int queueHead, queueCount;
    float shownAt = -1f;

    public static CodexToast Current { get { return instance; } }
    public bool Showing { get { return shownAt >= 0f; } }
    public string ShowingName { get { return nameText != null ? nameText.text : null; } }

    [RuntimeInitializeOnLoadMethod]
    static void Init()
    {
        Codex.Discovered -= OnDiscovered;
        Codex.Discovered += OnDiscovered;
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    // Builds the (hidden) toast as a run loads, so a first discovery -- often
    // a first pickup -- doesn't build a canvas mid-flight.
    static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (scene.name == "gameS1" && instance == null) instance = Build();
    }

    static void OnDiscovered(CodexEntry entry)
    {
        // Only during a real run; the tutorial never discovers anything.
        if (entry == null || !Application.isPlaying || SceneManager.GetActiveScene().name != "gameS1") return;
        if (instance == null) instance = Build();
        instance.Enqueue(entry);
    }

    public static CodexToast Build()
    {
        var canvas = CodexUi.NewOverlayCanvas("~CodexToast", 640, false);
        var toast = canvas.gameObject.AddComponent<CodexToast>();
        toast.canvas = canvas;
        toast.BuildUi(CodexUi.FindFont());
        toast.gameObject.SetActive(false);
        return toast;
    }

    void BuildUi(Font font)
    {
        var go = new GameObject("Toast", typeof(RectTransform), typeof(CanvasGroup));
        go.transform.SetParent(transform, false);
        box = (RectTransform)go.transform;
        box.anchorMin = box.anchorMax = new Vector2(.5f, 1f);
        box.pivot = new Vector2(.5f, 1f);
        box.sizeDelta = new Vector2(Width, Height);
        group = go.GetComponent<CanvasGroup>();
        group.interactable = false;
        group.blocksRaycasts = false;

        var bg = CodexUi.NewImage("Background", box, CodexUi.CodexSprite("cx_toast"), Color.white, true);
        CodexUi.Stretch(bg.rectTransform);

        const float iconSize = 50f;
        icon = CodexUi.NewImage("Icon", box, null, Color.white);
        icon.preserveAspect = true;
        CodexUi.Place(icon.rectTransform, CodexUi.Centered(-Width * .5f + 34f + iconSize * .5f, 0f, iconSize, iconSize));

        float left = -Width * .5f + 34f + iconSize + 14f;
        float textWidth = Width * .5f - 24f - left;
        heading = CodexUi.NewText("Heading", box, font, "NEW CODEX ENTRY", 15, CodexUi.Accent, TextAnchor.MiddleLeft);
        CodexUi.Place(heading.rectTransform, new Rect(left, 2f, textWidth, 24f));
        nameText = CodexUi.NewText("Name", box, font, "", 24, CodexUi.Title, TextAnchor.MiddleLeft);
        nameText.horizontalOverflow = HorizontalWrapMode.Wrap;
        nameText.verticalOverflow = VerticalWrapMode.Truncate;
        nameText.resizeTextForBestFit = true;
        nameText.resizeTextMinSize = 14;
        nameText.resizeTextMaxSize = 24;
        CodexUi.AddOutline(nameText.gameObject, CodexUi.Ink, 1.5f);
        CodexUi.Place(nameText.rectTransform, new Rect(left, -30f, textWidth, 32f));
    }

    public void Enqueue(CodexEntry entry)
    {
        if (queueCount >= QueueSize) return;   // a flood of firsts: the rest are in the codex anyway
        queue[(queueHead + queueCount) % QueueSize] = entry;
        queueCount++;
        if (!gameObject.activeSelf) gameObject.SetActive(true);
        if (shownAt < 0f) Next();
    }

    void Next()
    {
        if (queueCount == 0)
        {
            shownAt = -1f;
            gameObject.SetActive(false);
            return;
        }
        var entry = queue[queueHead];
        queue[queueHead] = null;
        queueHead = (queueHead + 1) % QueueSize;
        queueCount--;

        nameText.text = entry.name;
        icon.sprite = entry.Sprite;
        icon.enabled = icon.sprite != null;
        shownAt = Time.unscaledTime;
        ApplyAt(0f);
    }

    void Update()
    {
        if (shownAt < 0f) return;
        float t = Time.unscaledTime - shownAt;
        if (t >= InDuration + HoldDuration + OutDuration) { Next(); return; }
        ApplyAt(t);
    }

    public void ApplyAt(float t)
    {
        float a;
        if (t < InDuration) a = CodexUi.EaseOutCubic(t / InDuration);
        else if (t < InDuration + HoldDuration) a = 1f;
        else a = 1f - Mathf.Clamp01((t - InDuration - HoldDuration) / OutDuration);
        group.alpha = a;

        // Drop in from just above, under the top of the safe area.
        float safeTop = SafeTopInset();
        float slide = (1f - CodexUi.EaseOutCubic(t / InDuration)) * 24f;
        box.anchoredPosition = new Vector2(0f, -(safeTop + TopMargin) + slide);
    }

    float SafeTopInset()
    {
        float sf = Mathf.Max(canvas.scaleFactor, .0001f);
        return (ScreenInfo.Height - ScreenInfo.SafeArea.yMax) / sf;
    }
}
