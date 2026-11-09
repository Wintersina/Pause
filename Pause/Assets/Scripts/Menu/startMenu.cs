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
    private float layoutCamSize;
    private float logoBaseY = float.NaN;   // the scene's authored logo height, before any lift
    private Vector3 logoBaseScale = Vector3.one;

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
        // Sent here from the dock's Paragon berth: the Shop opens over the home screen.
        if (ShopCatalog.OpenOnHome)
        {
            ShopCatalog.OpenOnHome = false;
            if (ShopCatalog.EntryVisible()) ShopPanel.Open();
        }
    }

    // Update is called once per frame
    void Update()
    {
        var cam = Camera.main;
        if (layoutWidth != ScreenInfo.Width || layoutHeight != ScreenInfo.Height ||
            (cam != null && cam.orthographic && !Mathf.Approximately(layoutCamSize, cam.orthographicSize)))
            LayoutHome();
        // Back/Escape is BackNavigator's: on the home screen it closes the
        // codex one level at a time, then quits on a second press within
        // two seconds ("press back again to quit").
    }

    // Keep the main actions together and the footer at the bottom, regardless
    // of phone aspect ratio or desktop window size.
    // A menu row's touch height: (330 - 4 x 12) / 5 + 12.
    public const float HomeRowUnits = 68f;

    // The menu column's height for `rows` rows: 330 units for the five rows, plus 70 for each row more (the
    // Shop row), so every row keeps its ~68-unit touch height.
    public static float HomeColumnUnits(int rows) { return 330f + Mathf.Max(0, rows - 5) * 70f; }

    // Tall phones (Galaxy Z Flip7 21:9, 19.5:9 and up) have a lot of room above the logo, and a column hung
    // from the middle of the screen sat in the bottom half. The logo and the column both rise by a share of
    // the screen height that grows with how tall the screen is: nothing at 16:9 and shorter (tablets, the
    // foldable's cover display), the full amount from about 21:9. The column rises a little more than the
    // logo (the logo's gap to Play was generous) so the group reads as centred.
    public const float TallStart = 1.85f, TallFull = 2.33f;       // screen height / width
    public const float LogoLiftFraction = .13f, ColumnExtraFraction = .012f;   // of the screen height

    // 0 at 16:9 and shorter, 1 from ~21:9, smooth between.
    public static float Tallness(float screenW, float screenH)
    {
        if (screenW <= 0f) return 0f;
        return Mathf.Clamp01((screenH / screenW - TallStart) / (TallFull - TallStart));
    }

    static int ActiveRows(GameObject panel)
    {
        int rows = 0;
        if (panel != null) foreach (Transform child in panel.transform) if (child.gameObject.activeSelf) rows++;
        return Mathf.Max(5, rows);
    }

    public void LayoutHome()
    {
        var canvasObject = SceneUtil.FindAny("MainMenuCanvas");
        if (canvasObject == null) return;
        EnsurePrivacyButton();
        ShopHomeEntry.Ensure();
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
        var panel = SceneUtil.FindAny("UIPanel");
        // The column's top edge stays where the five-row menu had it (under the logo); the Shop row (every
        // platform) makes it a row taller, growing downward.
        float tall = Tallness(ScreenInfo.Width, ScreenInfo.Height);
        float topEdge = -height * 0.08f + Mathf.Min(HomeColumnUnits(5), height * 0.5f) * 0.5f
                        + height * (LogoLiftFraction + ColumnExtraFraction) * tall;
        LiftLogo(LogoLiftFraction * tall);
        // The five-row cap is half the canvas; each row more earns its share of that half, so a short canvas
        // (the foldable's cover display) keeps every row at its touch height instead of squeezing six into five.
        int rowCount = ActiveRows(panel);
        float columnHeight = Mathf.Min(HomeColumnUnits(rowCount), height * 0.5f * rowCount / 5f);
        Place("UIPanel", new Vector2(0.5f, 0.5f), new Vector2(0f, topEdge - columnHeight * 0.5f),
              new Vector2(Mathf.Min(520f, width * 0.78f), columnHeight));
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
        float columnBottom = height * 0.5f + topEdge - columnHeight - 6f;
        float stackedTop = footerY + FooterRowUnits * 1.5f + FooterGap + FooterGap * 0.5f;
        FooterStacked = !hasQuit || stackedTop <= columnBottom;
        // Side by side the footer row is one row tall; on a canvas too short even for that under a six-row
        // column (the foldable's cover display), the column rises by what it overlaps instead of covering it.
        if (!FooterStacked)
        {
            float sideTop = footerY + FooterRowUnits + FooterGap * .5f;
            float deficit = sideTop - (columnBottom + 6f);
            if (deficit > 0f)
            {
                topEdge += deficit;
                Place("UIPanel", new Vector2(0.5f, 0.5f), new Vector2(0f, topEdge - columnHeight * 0.5f),
                      new Vector2(Mathf.Min(520f, width * 0.78f), columnHeight));
            }
        }
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
        foreach (string name in new[] { "PlayButton", "shopButton", ShopHomeEntry.ButtonName, "achivButton", "LeaderboardButton", "QuitButton", PrivacyButtonName })
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
        // Short / wide screens: the column rose (footer deficit) or the canvas floor scaled it up while the world-space
        // logo stayed put. Whatever the shape, the logo ends up clear of the column (lifted, then shrunk if need be).
        float rowShare = (columnHeight - group_spacing * (rowCount - 1)) / rowCount;
        FitLogo(canvas != null ? canvas.scaleFactor : 1f, (height * 0.5f + topEdge) * (canvas != null ? canvas.scaleFactor : 1f),
                LogoClearShare * rowShare);
        layoutWidth = ScreenInfo.Width;
        layoutHeight = ScreenInfo.Height;
    }

    const float group_spacing = 12f;
    // The least gap kept between the logo's drawn bottom and the column's top, as a share of one menu row.
    public const float LogoClearShare = .5f;
    public const float LogoTopMarginPx = 28f;

    // Lifts the logo (up to the top safe edge), then shrinks it about its top, until its drawn bottom is `clearUnits`
    // (canvas units) above the column's top edge `columnTopPx` (screen px from the bottom). Does nothing when it already clears.
    void FitLogo(float scale, float columnTopPx, float clearUnits)
    {
        var logo = GameObject.Find("menuTitle");
        var sr = logo != null ? logo.GetComponent<SpriteRenderer>() : null;
        var cam = Camera.main;
        if (sr == null || sr.sprite == null || cam == null || !cam.orthographic || float.IsNaN(logoBaseY) || ScreenInfo.Height <= 0) return;
        float ppw = ScreenInfo.Height / (cam.orthographicSize * 2f);
        float minBottom = columnTopPx + clearUnits * scale;
        float bottom, top;
        LogoExtent(sr, cam, ppw, out bottom, out top);
        if (bottom >= minBottom) return;
        float maxTop = ScreenInfo.Height - ScreenInfo.SafeTopInset - LogoTopMarginPx;
        float lift = Mathf.Min(minBottom - bottom, Mathf.Max(0f, maxTop - top));
        var t = logo.transform;
        t.localPosition += new Vector3(0f, lift / ppw, 0f);
        bottom += lift; top += lift;
        if (bottom >= minBottom) return;
        float k = Mathf.Clamp((top - minBottom) / Mathf.Max(1f, top - bottom), .55f, 1f);
        if (k >= 1f) return;
        t.localScale = new Vector3(t.localScale.x * k, t.localScale.y * k, t.localScale.z);
        float newBottom, newTop;
        LogoExtent(sr, cam, ppw, out newBottom, out newTop);
        t.localPosition += new Vector3(0f, (top - newTop) / ppw, 0f);   // shrink about the top edge
    }

    // The logo's drawn outline (tight sprite mesh), in screen px from the bottom.
    static void LogoExtent(SpriteRenderer sr, Camera cam, float ppw, out float bottom, out float top)
    {
        float lo = float.MaxValue, hi = float.MinValue;
        foreach (var v in sr.sprite.vertices)
        {
            float wy = sr.transform.TransformPoint(new Vector3(0f, sr.flipY ? -v.y : v.y, 0f)).y;
            lo = Mathf.Min(lo, wy); hi = Mathf.Max(hi, wy);
        }
        float mid = ScreenInfo.Height * .5f, cy = cam.transform.position.y;
        bottom = (lo - cy) * ppw + mid;
        top = (hi - cy) * ppw + mid;
    }

    // The PAUSE logo is a world sprite authored for a 16:9 view: raise it by `fraction` of the view height.
    void LiftLogo(float fraction)
    {
        var logo = GameObject.Find("menuTitle");
        var cam = Camera.main;
        if (logo == null || cam == null || !cam.orthographic) return;
        if (float.IsNaN(logoBaseY)) { logoBaseY = logo.transform.localPosition.y; logoBaseScale = logo.transform.localScale; }
        logo.transform.localScale = logoBaseScale;   // FitLogo may have shrunk it for a short screen
        var p = logo.transform.localPosition;
        p.y = logoBaseY + fraction * cam.orthographicSize * 2f;
        logo.transform.localPosition = p;
        layoutCamSize = cam.orthographicSize;
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
            MenuNav.Load("gameS1"); // start and load scene 1
        }
        else
        {
            PrepareTutorialRun();
            MenuNav.Load("tutorialS5");
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
        MenuNav.Load("leaderboardS3");
    }
    // LEADERBOARD (moved here from Options): the in-game rankings panel over
    // the home screen; its "View all" opens the store's own screen.
    public void leaderboard()
    {
        LeaderboardPanel.OpenHome();
    }
    public void shop()
    {
        MenuNav.Load("shopS6");
    }
    // The Quit button: a deliberate quit, so no confirmation (never on iOS).
    public void quit()
    {
        BackNavigator.QuitNow();
    }
}
