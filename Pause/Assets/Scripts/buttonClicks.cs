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

        if (playerDied)
        {
            //show the ads if the player dies
            showButton();

        }
        else if (AdMob.isAdsShowwing)
            AdMob.hide();

        // Back/Escape. Was Android-gated, and the touchCount == 0 guard
        // swallowed the key on every other platform.
        {
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                if (AdMob.isAdsShowwing)
                    AdMob.hide();
                SceneManager.LoadScene("startS4");
                return;
            }
        }
    }
    public void replay()
    {
        
        startMenu.youAreInTutorial = false;
        moveBackGround.speed = 0f;
        score.totalCurrency = 0;
        SceneManager.LoadScene(gameS1);
    }
    public void quit()
    {
        if (AdMob.isAdsShowwing)
            AdMob.hide();
        Application.Quit();
    }
    void showButton()
    {
        // save the highscore and speed
        AdMob.show();
        popUpCanvas.gameObject.SetActive(true);
    }
    public void mainMenuButton()
    {
        if(AdMob.isAdsShowwing)
            AdMob.hide();
        SceneManager.LoadScene("startS4");
    }
}
