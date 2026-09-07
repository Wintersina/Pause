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
        loggedoutTextObj = GameObject.Find("LoggedoutText").GetComponent<Text>();
        loggedoutTextObj.gameObject.SetActive(false);
        // if ads are showing in main menu, turn them off.
        if (AdMob.isAdsShowwing)
            AdMob.hide();
    }

    // Update is called once per frame
    void Update()
    {
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
            if (logoutTimer <= 0)
                loggedoutTextObj.gameObject.SetActive(false);
        }
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
        loggedoutTextObj.gameObject.SetActive(true);
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
