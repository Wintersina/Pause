using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;

public class startMenu : MonoBehaviour {

    public Button playB;
    public Button aboutB;
    public Button quitB;
    public static bool playerDied;
    public static bool youAreInTutorial;
    public static int spawnTracker;
    private int layoutWidth, layoutHeight;

    void Start()
    {
        Time.timeScale = 1;
        // will change scenes based on char selection
        playerDied = false;
        playB.gameObject.SetActive(true);
        aboutB.gameObject.SetActive(true);
        // iOS has no system back and apps must not quit themselves: no Quit.
        quitB.gameObject.SetActive(BackNavigator.QuitAllowed);
        // if ads are showing in main menu, turn them off.
        if (AdMob.isAdsShowwing)
            AdMob.hide();
        LayoutHome();
    }

    // Update is called once per frame
    void Update()
    {
        if (layoutWidth != ScreenInfo.Width || layoutHeight != ScreenInfo.Height)
            LayoutHome();
        // Back/Escape is BackNavigator's: on the home screen it closes the
        // codex one level at a time, then quits on a second press within
        // two seconds ("press back again to quit").
    }

    // Keep the main actions together and the footer at the bottom, regardless
    // of phone aspect ratio or desktop window size.
    public void LayoutHome()
    {
        var canvasObject = SceneUtil.FindAny("MainMenuCanvas");
        if (canvasObject == null) return;
        var canvas = canvasObject.GetComponent<Canvas>();
        var scaler = canvasObject.GetComponent<CanvasScaler>();
        if (scaler != null)
        {
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(800f, 1000f);
            scaler.matchWidthOrHeight = 1f;
        }
        Canvas.ForceUpdateCanvases();
        var bounds = canvasObject.GetComponent<RectTransform>().rect;
        float width = bounds.width;
        float height = bounds.height;
        float safeBottom = canvas != null ? ScreenInfo.SafeArea.yMin / canvas.scaleFactor : 0f;
        Place("UIPanel", new Vector2(0.5f, 0.5f), new Vector2(0f, -height * 0.08f),
              new Vector2(Mathf.Min(520f, width * 0.78f), Mathf.Min(330f, height * 0.4f)));
        var panel = SceneUtil.FindAny("UIPanel");
        var group = panel != null ? panel.GetComponent<VerticalLayoutGroup>() : null;
        if (group != null)
        {
            group.spacing = 12f;
            group.childAlignment = TextAnchor.MiddleCenter;
            EvenOutRows(panel.transform, group.spacing);
        }
        // Sign-in is automatic now, so the footer is just Quit.
        float footerWidth = Mathf.Min(180f, (width - 64f) * 0.5f);
        Place("QuitButton", new Vector2(0.5f, 0f),
              new Vector2(0f, 44f + safeBottom), new Vector2(footerWidth, 56f));
        // a finger-sized target without bigger art (the art is 56 units tall)
        var quit = SceneUtil.FindAny("QuitButton");
        var quitGraphic = quit != null ? quit.GetComponent<Graphic>() : null;
        if (quitGraphic != null) quitGraphic.raycastPadding = new Vector4(-8f, -8f, -8f, -8f);
        foreach (string name in new[] { "PlayButton", "shopButton", "achivButton", "CreditsButton", "QuitButton" })
        {
            var button = SceneUtil.FindAny(name);
            if (button == null) continue;
            foreach (var label in button.GetComponentsInChildren<Text>(true))
            {
                label.resizeTextForBestFit = true;
                label.resizeTextMinSize = 22;
                label.resizeTextMaxSize = name == "QuitButton" ? 28 : 38;
            }
        }
        layoutWidth = ScreenInfo.Width;
        layoutHeight = ScreenInfo.Height;
    }

    // Every row of the main menu gets the same share of the panel (the
    // Options row used to come out ~20% shorter than the rest: its label's
    // preferred height was smaller), and each button's hit area reaches
    // across half the gap to its neighbours, so the whole column is
    // tappable without bigger art -- 48 dp rows on a 16:9 1080p phone.
    static void EvenOutRows(Transform panel, float spacing)
    {
        int rows = 0;
        foreach (Transform child in panel) if (child.gameObject.activeSelf) rows++;
        if (rows == 0) return;
        float height = ((RectTransform)panel).sizeDelta.y;
        float share = Mathf.Max(1f, (height - spacing * (rows - 1)) / rows);
        foreach (Transform child in panel)
        {
            if (!child.gameObject.activeSelf) continue;
            var element = child.GetComponent<LayoutElement>() ?? child.gameObject.AddComponent<LayoutElement>();
            element.minHeight = element.preferredHeight = share;
            var button = child.GetComponent<Button>();
            var graphic = button != null ? button.targetGraphic : child.GetComponent<Graphic>();
            if (graphic != null) graphic.raycastPadding = new Vector4(0f, -spacing * .5f, 0f, -spacing * .5f);
        }
    }

    static void Place(string name, Vector2 anchor, Vector2 position, Vector2 size)
    {
        var go = SceneUtil.FindAny(name);
        var rect = go != null ? go.GetComponent<RectTransform>() : null;
        if (rect == null) return;
        rect.anchorMin = rect.anchorMax = anchor;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
    }

    public void play()
    {
        if(PlayerPrefs.GetString("HasDoneTut") == "true")
        {
            youAreInTutorial = false;
            moveBackGround.speed = 0f;
            score.totalCurrency = 0;
            SceneManager.LoadScene("gameS1"); // start and load scene 1
        }
        else
        {
            PrepareTutorialRun();
            SceneManager.LoadScene("tutorialS5");
        }
    }

    // Every way into the tutorial -- first launch or replaying it from the
    // leaderboard screen -- goes through here, so they behave identically.
    public static void PrepareTutorialRun()
    {
        youAreInTutorial = true;
        moveBackGround.speed = 0f;
        score.totalCurrency = 0;
    }

   public void achivements()
    {
        SceneManager.LoadScene("leaderboardS3");
    }
    public void shop()
    {
        SceneManager.LoadScene("shopS6");
    }
    public void credit()
    {
        SceneManager.LoadScene("creditsS7");
    }
    // The Quit button: a deliberate quit, so no confirmation (never on iOS).
    public void quit()
    {
        BackNavigator.QuitNow();
    }
}
