using UnityEngine;
using UnityEngine.SceneManagement;
public class buttonClicks : MonoBehaviour {

    private string gameS1;
    public Canvas popUpCanvas;
    public static bool playerDied;
	// Use this for initialization
	void Start () {
        popUpCanvas = GameObject.Find("PopUpCanvas").GetComponent<Canvas>();
        gameS1 = "gameS1";
        playerDied = false;
        popUpCanvas.gameObject.SetActive(false);

        //hide the ads at the start
        if (AdMob.isAdsShowwing)
            AdMob.hide();
    }

    // Update is called once per frame
    void Update() {

        // The panel waits for the crash sequence (DeathCrash) to finish.
        if (playerDied && DeathCrash.PanelReady)
        {
            //show the ads if the player dies
            showButton();

        }
        else if (AdMob.isAdsShowwing)
            AdMob.hide();
        // Back/Escape is BackNavigator's: in gameS1 it does mainMenuButton.
    }
    public void replay()
    {
        GameStateReset.Clear();
        startMenu.youAreInTutorial = false;
        moveBackGround.speed = 0f;
        score.totalCurrency = 0;
        SceneManager.LoadScene(gameS1);
    }
    public void quit()
    {
        BackNavigator.QuitNow();
    }
    void showButton()
    {
        // save the highscore and speed
        AdMob.show();
        popUpCanvas.gameObject.SetActive(true);
    }
    public void mainMenuButton()
    {
        // Death leaves timeScale at 0 and playerDied set; carrying either into
        // the menu leaves it frozen and unresponsive. GoHome hides any ad,
        // clears run state and loads startS4 -- the same path system back
        // takes in gameS1.
        BackNavigator.GoHome();
    }
}
