using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// Adds a SKIP button to the tutorial.
//
// Built at runtime on its own canvas rather than authored into tutorialS5, so
// it cannot be knocked loose by scene edits and needs no wiring. Drop this
// component on any object in the tutorial scene (or let it be added by code).
public class TutorialSkip : MonoBehaviour
{
    [Tooltip("Corner inset from the top, in reference pixels.")]
    public float topMargin = 24f;

    [Tooltip("Button size in reference pixels.")]
    public Vector2 size = new Vector2(150, 64);

    [Tooltip("Right edge of the button is kept at this world x or further " +
             "left -- inside the player's own reach, comfortably clear of " +
             "the rail on any device rather than a fixed pixel margin that " +
             "the rail's screen position moves past on wider screens.")]
    public float clampWorldX = 2.15f;

    RectTransform buttonRect;
    RectTransform canvasRect;
    int lastScreenW = -1, lastScreenH = -1;

    void Start()
    {
        Build();
    }

    void Update()
    {
        // Screen.width/height change on rotation, resize, or a foldable
        // changing state mid-session -- recheck cheaply and only reposition
        // on an actual change, mirroring CameraFit's own pattern.
        if (Screen.width != lastScreenW || Screen.height != lastScreenH)
            Reposition();
    }

    void Build()
    {
        var canvasGo = new GameObject("SkipCanvas",
            typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvasGo.transform.SetParent(transform, false);

        var canvas = canvasGo.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 500;   // above the tutorial HUD

        var scaler = canvasGo.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(800, 1200);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
        canvasRect = canvasGo.GetComponent<RectTransform>();

        var btnGo = new GameObject("SkipButton", typeof(Image), typeof(Button));
        btnGo.transform.SetParent(canvasGo.transform, false);

        var rt = btnGo.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = new Vector2(1, 1);
        rt.pivot = new Vector2(1, 1);
        rt.sizeDelta = size;
        buttonRect = rt;

        var img = btnGo.GetComponent<Image>();
        img.color = new Color(0.09f, 0.10f, 0.14f, 0.72f);

        var labelGo = new GameObject("Label", typeof(Text));
        labelGo.transform.SetParent(btnGo.transform, false);
        var label = labelGo.GetComponent<Text>();
        label.text = "SKIP  >>";
        label.alignment = TextAnchor.MiddleCenter;
        label.color = Color.white;
        label.fontSize = 28;
        label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        var lrt = labelGo.GetComponent<RectTransform>();
        lrt.anchorMin = Vector2.zero;
        lrt.anchorMax = Vector2.one;
        lrt.offsetMin = lrt.offsetMax = Vector2.zero;

        btnGo.GetComponent<Button>().onClick.AddListener(Skip);

        Reposition();
    }

    // The button used to sit a fixed 24px from the true screen edge, which
    // put it right on top of the rail on any device wide enough to show much
    // of the rail's own width (the rail's screen position depends on device
    // aspect via CameraFit; a fixed pixel margin does not track that). Its
    // right edge is now kept at a world position just inside the player's
    // own reach instead, which the rail's inner edge always sits outside of.
    void Reposition()
    {
        if (buttonRect == null || canvasRect == null) return;
        var cam = Camera.main;
        if (cam == null) return;

        lastScreenW = Screen.width;
        lastScreenH = Screen.height;

        Vector3 worldClamp = new Vector3(clampWorldX, 0f, 0f);
        Vector2 screenPoint = cam.WorldToScreenPoint(worldClamp);

        Vector2 local;
        RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, screenPoint, null, out local);

        // Never sit further right than that world-clamped x, but still allow
        // the usual top-right placement on a screen wide enough that the
        // fixed margin alone would already clear the rail.
        float rightEdgeFromFixedMargin = canvasRect.rect.xMax - 24f;
        float rightEdge = Mathf.Min(rightEdgeFromFixedMargin, local.x);

        buttonRect.anchoredPosition = new Vector2(rightEdge - canvasRect.rect.xMax, -topMargin);
    }

    // Finish the tutorial exactly the way completing it does, then go straight
    // into the game so the player is not bounced back to the menu.
    public void Skip()
    {
        PlayerPrefs.SetString("HasDoneTut", "true");
        PlayerPrefs.Save();

        Hints.reachedTheEndOfTut = true;
        startMenu.youAreInTutorial = false;
        buttonClicks.playerDied = false;
        score.totalCurrency = 0;
        score.tutorialCurrency = 0;
        moveBackGround.speed = 0f;
        Time.timeScale = 1f;

        achievementAPICalls.achievement_tutorial_completed();
        SceneManager.LoadScene("gameS1");
    }
}

// Attaches the skip button whenever the tutorial scene loads, so tutorialS5
// itself needs no edits and the button cannot go missing.
public static class TutorialSkipBootstrap
{
    const string TutorialScene = "tutorialS5";

    [RuntimeInitializeOnLoadMethod]
    static void Init()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (scene.name != TutorialScene) return;
        if (Object.FindFirstObjectByType<TutorialSkip>() != null) return;

        var go = new GameObject("~TutorialSkip");
        go.AddComponent<TutorialSkip>();
    }
}
