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
    // A menu row's touch height: (330 - 4 x 12) / 5 + 12.
    public const float HomeRowUnits = 68f;

    public void LayoutHome()
    {
        var canvasObject = SceneUtil.FindAny("MainMenuCanvas");
        if (canvasObject == null) return;
        EnsurePrivacyButton();
        var canvas = canvasObject.GetComponent<Canvas>();
        var scaler = canvasObject.GetComponent<CanvasScaler>();
        if (scaler != null)
        {
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 1f;
            // The menu rows are HomeRowUnits tall (touch: half of each gap
            // too): never under 44 pt / 48 dp (UiScale's floor).
            UiScaleFloor.Configure(scaler, new Vector2(800f, 1000f), HomeRowUnits, 0f);
        }
        Canvas.ForceUpdateCanvases();
        var bounds = canvasObject.GetComponent<RectTransform>().rect;
        float width = bounds.width;
        float height = bounds.height;
        float safeBottom = canvas != null ? ScreenInfo.SafeArea.yMin / canvas.scaleFactor : 0f;
        // The column keeps its 330 units (five ~68-unit touch rows) on any
        // canvas of 660+ units: a short phone's floor-raised canvas is ~800.
        Place("UIPanel", new Vector2(0.5f, 0.5f), new Vector2(0f, -height * 0.08f),
              new Vector2(Mathf.Min(520f, width * 0.78f), Mathf.Min(330f, height * 0.5f)));
        var panel = SceneUtil.FindAny("UIPanel");
        var group = panel != null ? panel.GetComponent<VerticalLayoutGroup>() : null;
        if (group != null)
        {
            group.spacing = 12f;
            group.childAlignment = TextAnchor.MiddleCenter;
            EvenOutRows(panel.transform, group.spacing);
        }
        // The footer: PRIVACY POLICY, with Quit under it (no Quit on iOS: the
        // policy link takes its place at the bottom). On a canvas too short for
        // two stacked rows under the menu column, the two sit side by side.
        float footerWidth = Mathf.Min(180f, (width - 64f) * 0.5f);
        var quit = SceneUtil.FindAny("QuitButton");
        bool hasQuit = quit != null && quit.activeSelf;
        float footerY = 44f + safeBottom;
        float columnBottom = height * 0.5f - height * 0.08f - Mathf.Min(330f, height * 0.5f) * 0.5f - 6f;
        float stackedTop = footerY + FooterRowUnits * 1.5f + FooterGap + FooterGap * 0.5f;
        FooterStacked = !hasQuit || stackedTop <= columnBottom;
        if (FooterStacked)
        {
            Place("QuitButton", new Vector2(0.5f, 0f), new Vector2(0f, footerY), new Vector2(footerWidth, FooterRowUnits));
            Place(PrivacyButtonName, new Vector2(0.5f, 0f),
                  new Vector2(0f, footerY + (hasQuit ? FooterRowUnits + FooterGap : 0f)),
                  new Vector2(Mathf.Min(PrivacyWidth, width - 64f), FooterRowUnits));
        }
        else
        {
            float room = width - 64f - FooterSideGap;
            float privacyW = Mathf.Min(PrivacyWidth, room * 0.6f), quitW = Mathf.Min(180f, room - privacyW);
            float total = privacyW + FooterSideGap + quitW;
            Place(PrivacyButtonName, new Vector2(0.5f, 0f), new Vector2(-total * 0.5f + privacyW * 0.5f, footerY),
                  new Vector2(privacyW, FooterRowUnits));
            Place("QuitButton", new Vector2(0.5f, 0f), new Vector2(total * 0.5f - quitW * 0.5f, footerY),
                  new Vector2(quitW, FooterRowUnits));
        }
        // finger-sized targets without bigger art (the art is 56 units tall)
        foreach (string name in new[] { "QuitButton", PrivacyButtonName })
        {
            var go = SceneUtil.FindAny(name);
            var graphic = go != null ? go.GetComponent<Graphic>() : null;
            if (graphic != null) graphic.raycastPadding = new Vector4(-8f, -FooterGap * .5f, -8f, -FooterGap * .5f);
        }
        foreach (string name in new[] { "PlayButton", "shopButton", "achivButton", "LeaderboardButton", "QuitButton", PrivacyButtonName })
        {
            var button = SceneUtil.FindAny(name);
            if (button == null) continue;
            foreach (var label in button.GetComponentsInChildren<Text>(true))
            {
                label.resizeTextForBestFit = true;
                label.resizeTextMinSize = 22;
                label.resizeTextMaxSize = name == "QuitButton" || name == PrivacyButtonName ? 28 : 38;
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

    // The PRIVACY POLICY link (moved here from Options, 2026-10-12): a copy of
    // the Quit button (same art, label style and press) that opens the policy
    // in the browser. Menu buttons have no click sound, so neither does this.
    public const string PrivacyButtonName = "PrivacyButton";
    public const string PrivacyCaption = "Privacy Policy";
    public const float FooterRowUnits = 56f, FooterGap = 16f, FooterSideGap = 24f, PrivacyWidth = 300f;
    // false when the canvas is too short and Privacy / Quit share one row
    public static bool FooterStacked { get; private set; } = true;

    public static GameObject EnsurePrivacyButton()
    {
        var existing = SceneUtil.FindAny(PrivacyButtonName);
        if (existing != null) return existing;
        var template = SceneUtil.FindAny("QuitButton");
        if (template == null) return null;
        var go = Instantiate(template, template.transform.parent, false);
        go.name = PrivacyButtonName;
        go.SetActive(true);
        var button = go.GetComponent<Button>();
        if (button != null)
        {
            button.onClick = new Button.ButtonClickedEvent();
            button.onClick.AddListener(OpenPrivacyPolicy);
        }
        foreach (var label in go.GetComponentsInChildren<Text>(true)) label.text = PrivacyCaption;
        return go;
    }

    public static void OpenPrivacyPolicy() { ExternalLinks.Open(ExternalLinks.PrivacyPolicy); }

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
        ShipId.TutorialFlight = true;   // always Neon Comet, stock (ShipId.TutorialShip)
        moveBackGround.speed = 0f;
        score.totalCurrency = 0;
    }

    // The Options button (the scene still calls it by its old name).
    public void achivements()
    {
        SceneManager.LoadScene("leaderboardS3");
    }
    // LEADERBOARD (moved here from Options): the in-game rankings panel over
    // the home screen; its "View all" opens the store's own screen.
    public void leaderboard()
    {
        LeaderboardPanel.Open();
    }
    public void shop()
    {
        SceneManager.LoadScene("shopS6");
    }
    // The Quit button: a deliberate quit, so no confirmation (never on iOS).
    public void quit()
    {
        BackNavigator.QuitNow();
    }
}
