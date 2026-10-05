using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// "Sign in with Play Games in Options to save progress to your account":
// the one launch-time signal that sign-in exists, shown on the home screen
// when the silent launch sign-in failed -- at most once per install
// (AccountLink.HintShownKey), never after the player chose SIGN OUT, and
// never as a permanent home-screen button.
//
// Same plate as the "press back again" toast (BackQuitToast), a little
// higher and longer-lived. It never blocks input (no raycaster, no raycast
// targets), steps aside when the quit toast shows, and belongs to the scene
// it was shown in.
public class AccountHintToast : MonoBehaviour
{
    public const float Width = 620f, Height = 96f;
    public const float BottomMargin = 230f;   // above the quit toast and the Quit button
    public const float Duration = 5.5f, InDuration = .18f, OutDuration = .35f;

    static AccountHintToast instance;

    RectTransform box;
    CanvasGroup group;
    Canvas canvas;
    Text label;
    float shownAt = -1f;

    public static AccountHintToast Current { get { return instance; } }
    public bool Showing { get { return shownAt >= 0f && gameObject.activeSelf; } }
    public string ShownText { get { return label != null ? label.text : null; } }

    [RuntimeInitializeOnLoadMethod]
    static void Init()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
        AccountLink.Changed -= OnAccountChanged;
        AccountLink.Changed += OnAccountChanged;
        BackNavigator.QuitArmed -= OnQuitArmed;
        BackNavigator.QuitArmed += OnQuitArmed;
    }

    static void OnSceneLoaded(Scene scene, LoadSceneMode mode) { TryShow(scene.name); }

    // The silent sign-in usually answers after the home screen is up.
    static void OnAccountChanged()
    {
        if (Application.isPlaying) TryShow(SceneManager.GetActiveScene().name);
    }

    static void OnQuitArmed()
    {
        if (instance != null && instance.Showing) instance.Hide();
    }

    // Shows the hint if it is due on this screen. Returns it, or null.
    public static AccountHintToast TryShow(string scene)
    {
        if (scene != BackNavigator.HomeScene || !AccountLink.ShouldShowLaunchHint) return null;
        AccountLink.MarkLaunchHintShown();
        return Show(AccountLink.LaunchHintText);
    }

    public static AccountHintToast Show(string text)
    {
        if (instance == null) instance = Build();
        instance.label.text = text;
        instance.gameObject.SetActive(true);
        instance.shownAt = Time.unscaledTime;
        instance.ApplyAt(0f);
        Debug.Log("[Account] home hint: " + text);
        return instance;
    }

    public void Hide()
    {
        shownAt = -1f;
        gameObject.SetActive(false);
    }

    static AccountHintToast Build()
    {
        var c = CodexUi.NewOverlayCanvas("~AccountHintToast", 640, false);
        var toast = c.gameObject.AddComponent<AccountHintToast>();
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

        label = CodexUi.NewText("Label", box, font, "", 22, CodexUi.Title, TextAnchor.MiddleCenter);
        label.raycastTarget = false;
        label.horizontalOverflow = HorizontalWrapMode.Wrap;
        label.resizeTextForBestFit = true;
        label.resizeTextMinSize = 14;
        label.resizeTextMaxSize = 22;
        CodexUi.AddOutline(label.gameObject, CodexUi.Ink, 1.5f);
        CodexUi.Place(label.rectTransform, CodexUi.Centered(0f, 0f, Width - 56f, Height - 16f));
    }

    void Update()
    {
        if (shownAt < 0f) return;
        float t = Time.unscaledTime - shownAt;
        if (t >= Duration + OutDuration)
        {
            Hide();
            return;
        }
        ApplyAt(t);
    }

    void ApplyAt(float t)
    {
        float a;
        if (t < InDuration) a = CodexUi.EaseOutCubic(t / InDuration);
        else if (t < Duration) a = 1f;
        else a = 1f - Mathf.Clamp01((t - Duration) / OutDuration);
        group.alpha = a;
        float sf = Mathf.Max(canvas.scaleFactor, .0001f);
        float safeBottom = ScreenInfo.SafeArea.yMin / sf;
        float slide = (1f - CodexUi.EaseOutCubic(Mathf.Clamp01(t / InDuration))) * -18f;
        box.anchoredPosition = new Vector2(0f, safeBottom + BottomMargin + slide);
    }

    void OnDestroy()
    {
        if (instance == this) instance = null;
    }
}
