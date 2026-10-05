using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// Adds a SKIP button to the tutorial.
//
// Built at runtime on its own canvas rather than authored into tutorialS5, so
// it cannot be knocked loose by scene edits and needs no wiring. Drop this
// component on any object in the tutorial scene (or let it be added by code).
//
// Styled as a flat cel pill in the tutorial's Akira palette (tut_button
// tinted steel, ink outline, an orange chevron, Orbitron) instead of the old
// flat grey box, and parked
// just under the top-right quick actions (PauseQuickActions) -- it used to
// sit exactly where they appear whenever the player lifts their finger. It
// shares their canvas scaler (800x1000, match 0.5) so the offsets line up.
public class TutorialSkip : MonoBehaviour
{
    [Tooltip("Gap from the top of the safe area, in reference units: below " +
             "the quick-action row (18 margin + 72 button) plus a 14 gap.")]
    public float topMargin = 104f;

    [Tooltip("Button size in reference units.")]
    public Vector2 size = new Vector2(136, 52);

    [Tooltip("Right edge of the button is kept at this world x or further " +
             "left -- inside the player's own reach, comfortably clear of " +
             "the rail on any device rather than a fixed pixel margin that " +
             "the rail's screen position moves past on wider screens.")]
    public float clampWorldX = 2.15f;

    public const string ExitScene = "gameS1";


    RectTransform buttonRect;
    RectTransform canvasRect;
    Canvas canvas;
    CanvasGroup group;
    bool visible = true;
    int lastScreenW = -1, lastScreenH = -1;
    Rect lastSafe;
    float lastScaleFactor;

    public Button Button { get; private set; }
    public RectTransform ButtonRect { get { return buttonRect; } }

    void Start()
    {
        Build();
    }

    void Update()
    {
        // Screen.width/height/safe area change on rotation, resize, or a
        // foldable changing state mid-session -- recheck cheaply and only
        // reposition on an actual change, mirroring CameraFit's own pattern.
        if (ScreenInfo.Width != lastScreenW || ScreenInfo.Height != lastScreenH || ScreenInfo.SafeArea != lastSafe
            || (canvas != null && !Mathf.Approximately(canvas.scaleFactor, lastScaleFactor)))
            Reposition();

        if (group != null)
        {
            float target = visible ? 1f : 0f;   // snaps, like the rest of the tutorial UI
            if (!Mathf.Approximately(group.alpha, target)) group.alpha = target;
        }
    }

    // Hidden once the tutorial reaches its own end panel.
    public void SetVisible(bool on)
    {
        visible = on;
        if (group != null)
        {
            group.interactable = on;
            group.blocksRaycasts = on;
        }
    }

    void Build()
    {
        var canvasGo = new GameObject("SkipCanvas",
            typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvasGo.transform.SetParent(transform, false);

        canvas = canvasGo.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 500;   // above the tutorial HUD and the robot

        var scaler = canvasGo.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = RobotSpeaker.ReferenceResolution;
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = RobotSpeaker.MatchWidthOrHeight;
        canvasRect = canvasGo.GetComponent<RectTransform>();

        var btnGo = new GameObject("SkipButton", typeof(Image), typeof(Button), typeof(CanvasGroup));
        btnGo.transform.SetParent(canvasGo.transform, false);
        group = btnGo.GetComponent<CanvasGroup>();

        var rt = btnGo.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = new Vector2(1, 1);
        rt.pivot = new Vector2(1, 1);
        rt.sizeDelta = size;
        buttonRect = rt;

        var img = btnGo.GetComponent<Image>();
        img.sprite = Resources.Load<Sprite>("Tutorial/tut_button");
        img.type = Image.Type.Sliced;
        img.color = TutorialPalette.Steel;
        // a finger-sized target (the plate is ~25 dp tall) without bigger art
        img.raycastPadding = new Vector4(-16f, -20f, -16f, -20f);

        // Label + chevron as one centred group, drawn inside a holder that the
        // press spring scales (the button rect itself keeps its hit area).
        var face = new GameObject("Face", typeof(RectTransform)).GetComponent<RectTransform>();
        face.SetParent(btnGo.transform, false);
        face.anchorMin = Vector2.zero; face.anchorMax = Vector2.one;
        face.offsetMin = face.offsetMax = Vector2.zero;

        var labelGo = new GameObject("Label", typeof(Text));
        labelGo.transform.SetParent(face, false);
        var label = labelGo.GetComponent<Text>();
        label.text = "SKIP";
        label.alignment = TextAnchor.MiddleLeft;
        label.color = TutorialPalette.Paper;
        label.fontSize = 22;
        label.fontStyle = FontStyle.Bold;
        label.font = SceneFont();
        label.horizontalOverflow = HorizontalWrapMode.Overflow;
        label.raycastTarget = false;
        var outline = labelGo.AddComponent<Outline>();
        outline.effectColor = TutorialPalette.Ink;
        outline.effectDistance = new Vector2(2f, -2f);

        const float chevron = 22f, gap = 8f;
        float textWidth = Mathf.Ceil(label.preferredWidth);
        float x = -(textWidth + gap + chevron) * .5f;
        var lrt = labelGo.GetComponent<RectTransform>();
        lrt.anchorMin = lrt.anchorMax = lrt.pivot = new Vector2(.5f, .5f);
        lrt.sizeDelta = new Vector2(textWidth + 4f, 40f);
        lrt.anchoredPosition = new Vector2(x + textWidth * .5f, 0f);

        var arrowGo = new GameObject("Chevron", typeof(Image));
        arrowGo.transform.SetParent(face, false);
        var arrow = arrowGo.GetComponent<Image>();
        arrow.sprite = Resources.Load<Sprite>("Tutorial/tut_arrow");
        arrow.color = Color.white;   // colours baked in (orange, ink)
        arrow.raycastTarget = false;
        var art = arrow.rectTransform;
        art.anchorMin = art.anchorMax = art.pivot = new Vector2(.5f, .5f);
        art.sizeDelta = new Vector2(chevron, chevron);
        art.anchoredPosition = new Vector2(x + textWidth + gap + chevron * .5f, 0f);
        art.localRotation = Quaternion.Euler(0f, 0f, -90f);   // art points up; point right

        var button = btnGo.GetComponent<Button>();
        button.transition = Selectable.Transition.None;
        button.onClick.AddListener(Skip);
        Button = button;
        var press = btnGo.AddComponent<DeathPanelPress>();
        press.target = face;

        Reposition();
    }

    static Font SceneFont()
    {
        var hud = Object.FindFirstObjectByType<score>();
        if (hud != null && hud.speedValue != null && hud.speedValue.font != null) return hud.speedValue.font;
        return Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
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

        lastScreenW = ScreenInfo.Width;
        lastScreenH = ScreenInfo.Height;
        lastSafe = ScreenInfo.SafeArea;
        if (canvas != null) lastScaleFactor = canvas.scaleFactor;

        Vector3 worldClamp = new Vector3(clampWorldX, 0f, 0f);
        Vector2 screenPoint = cam.WorldToScreenPoint(worldClamp);

        Vector2 local;
        RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, screenPoint, null, out local);

        // Never sit further right than that world-clamped x, but still allow
        // the usual top-right placement on a screen wide enough that the
        // fixed margin alone would already clear the rail.
        float sf = canvas != null ? Mathf.Max(canvas.scaleFactor, .0001f) : 1f;
        Rect safe = ScreenInfo.SafeArea;
        float safeRightInset = (ScreenInfo.Width - safe.xMax) / sf;
        float safeTopInset = (ScreenInfo.Height - safe.yMax) / sf;
        float rightEdgeFromFixedMargin = canvasRect.rect.xMax - safeRightInset - 18f;
        float rightEdge = Mathf.Min(rightEdgeFromFixedMargin, local.x);

        buttonRect.anchoredPosition = new Vector2(rightEdge - canvasRect.rect.xMax, -(safeTopInset + topMargin));
    }

    // Finish the tutorial exactly the way completing it does, then go straight
    // into the game so the player is not bounced back to the menu.
    public void Skip()
    {
        SceneManager.LoadScene(FinishBySkipping());
    }

    // Everything Skip does except the scene load, so it can be tested in
    // edit mode. Returns the scene to load.
    public static string FinishBySkipping()
    {
        PlayerPrefs.SetString("HasDoneTut", "true");
        PlayerPrefs.Save();

        Hints.reachedTheEndOfTut = true;
        startMenu.youAreInTutorial = false;
        buttonClicks.playerDied = false;
        score.totalCurrency = 0;
        score.tutorialCurrency = 0;
        moveBackGround.speed = 0f;
        spawnGoodStuffTut.keepAtomComing = TutorialAtom.None;
        Time.timeScale = 1f;

        achievementAPICalls.achievement_tutorial_completed();
        return ExitScene;
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
