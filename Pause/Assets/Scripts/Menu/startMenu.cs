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
    private Text loggedoutTextObj;
    private float logoutTimer;
    private int layoutWidth, layoutHeight;

    void Start()
    {
        Time.timeScale = 1;
        // initilize all game materials
        logoutTimer = 0;

        // will change scenes based on char selection
        playerDied = false;
        playB.gameObject.SetActive(true);
        aboutB.gameObject.SetActive(true);
        quitB.gameObject.SetActive(true);
        // find it and turn it off.
        // Inactive objects are invisible to GameObject.Find, and a null here
        // used to abort the rest of Start().
        GameObject loggedOut = SceneUtil.FindAny("LoggedoutText");
        loggedoutTextObj = loggedOut != null ? loggedOut.GetComponent<Text>() : null;
        if (loggedoutTextObj != null) loggedoutTextObj.gameObject.SetActive(false);
        // if ads are showing in main menu, turn them off.
        if (AdMob.isAdsShowwing)
            AdMob.hide();
        LayoutHome();
    }

    // Update is called once per frame
    void Update()
    {
        if (layoutWidth != Screen.width || layoutHeight != Screen.height)
            LayoutHome();
        // Back/Escape quits. Was Android-gated and additionally required
        // touchCount == 0, which swallowed the keypress on other platforms.
        if (Input.GetKeyDown(KeyCode.Escape))
        {
            Application.Quit();
            return;
        }

        // used for logout display.
        if (logoutTimer > 0)
        {
            logoutTimer -= Time.deltaTime;
            if (logoutTimer <= 0 && loggedoutTextObj != null)
                loggedoutTextObj.gameObject.SetActive(false);
        }
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
        float safeBottom = canvas != null ? Screen.safeArea.yMin / canvas.scaleFactor : 0f;
        Place("UIPanel", new Vector2(0.5f, 0.5f), new Vector2(0f, -height * 0.08f),
              new Vector2(Mathf.Min(520f, width * 0.78f), Mathf.Min(330f, height * 0.4f)));
        var panel = SceneUtil.FindAny("UIPanel");
        var group = panel != null ? panel.GetComponent<VerticalLayoutGroup>() : null;
        if (group != null)
        {
            group.spacing = 12f;
            group.childAlignment = TextAnchor.MiddleCenter;
        }
        float footerWidth = Mathf.Min(180f, (width - 64f) * 0.5f);
        Place("LogOutButton", new Vector2(0.5f, 0f),
              new Vector2(-footerWidth * 0.5f - 12f, 44f + safeBottom), new Vector2(footerWidth, 56f));
        Place("QuitButton", new Vector2(0.5f, 0f),
              new Vector2(footerWidth * 0.5f + 12f, 44f + safeBottom), new Vector2(footerWidth, 56f));
        Place("LoggedoutText", new Vector2(0.5f, 0f), new Vector2(0f, 110f + safeBottom),
              new Vector2(Mathf.Min(430f, width - 32f), 44f));
        Place("LoginButton", new Vector2(1f, 1f), new Vector2(-64f, -64f), new Vector2(80f, 80f));
        foreach (string name in new[] { "PlayButton", "shopButton", "achivButton", "CreditsButton", "LogOutButton", "QuitButton" })
        {
            var button = SceneUtil.FindAny(name);
            if (button == null) continue;
            foreach (var label in button.GetComponentsInChildren<Text>(true))
            {
                label.resizeTextForBestFit = true;
                label.resizeTextMinSize = 22;
                label.resizeTextMaxSize = name == "LogOutButton" || name == "QuitButton" ? 28 : 38;
            }
        }
        layoutWidth = Screen.width;
        layoutHeight = Screen.height;
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
            youAreInTutorial = true;
            moveBackGround.speed = 0f;
            score.totalCurrency = 0;
            SceneManager.LoadScene("tutorialS5");
        }
    }

    public void logoutButton()
    {
        logoutTimer = 2f;
        if (loggedoutTextObj != null) loggedoutTextObj.gameObject.SetActive(true);
        SocialBridge.SignOut();
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
    public void quit()
    {
        Application.Quit();
    }
    public void login()
    {
        // No longer Android-only; on iOS this signs in to Game Center.
        SocialBridge.Authenticate(success =>
        {
            if (success)
                achievementAPICalls.achievement_logged_on_successfully();
        });
    }
}
