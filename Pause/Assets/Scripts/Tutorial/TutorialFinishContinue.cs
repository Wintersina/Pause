using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// The authored tutorial popup was only a message card on some device layouts.
// This dedicated canvas makes the final action unmistakable and always usable.
public class TutorialFinishContinue : MonoBehaviour
{
    Canvas canvas;

    void Awake()
    {
        Build();
        canvas.gameObject.SetActive(false);
    }

    public void Show()
    {
        if (canvas == null) Build();
        canvas.gameObject.SetActive(true);
    }

    void Build()
    {
        if (canvas != null) return;
        var root = new GameObject("TutorialFinishCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        DontDestroyOnLoad(root);
        canvas = root.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 700;
        var scaler = root.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1080f, 1920f);

        var buttonGo = new GameObject("ContinueToGameButton", typeof(Image), typeof(Button));
        buttonGo.transform.SetParent(root.transform, false);
        var image = buttonGo.GetComponent<Image>();
        image.color = new Color(0.14f, 0.72f, 0.95f, 0.96f);
        var rect = buttonGo.GetComponent<RectTransform>();
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = new Vector2(0f, -280f);
        rect.sizeDelta = new Vector2(440f, 108f);
        buttonGo.GetComponent<Button>().onClick.AddListener(Continue);

        var labelGo = new GameObject("Label", typeof(Text));
        labelGo.transform.SetParent(buttonGo.transform, false);
        var label = labelGo.GetComponent<Text>();
        label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        label.fontSize = 42;
        label.alignment = TextAnchor.MiddleCenter;
        label.color = Color.white;
        label.text = "CONTINUE TO GAME  >>";
        label.rectTransform.anchorMin = Vector2.zero;
        label.rectTransform.anchorMax = Vector2.one;
        label.rectTransform.sizeDelta = Vector2.zero;
    }

    public void Continue()
    {
        startMenu.youAreInTutorial = false;
        moveBackGround.speed = 0f;
        score.totalCurrency = 0f;
        SceneManager.LoadScene("gameS1");
    }
}

public static class TutorialFinishContinueBootstrap
{
    static TutorialFinishContinue current;

    [RuntimeInitializeOnLoadMethod]
    static void Init()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (scene.name != "tutorialS5") return;
        current = new GameObject("~TutorialFinishContinue").AddComponent<TutorialFinishContinue>();
    }

    public static void Show()
    {
        if (current == null) current = Object.FindFirstObjectByType<TutorialFinishContinue>();
        if (current != null) current.Show();
    }
}
