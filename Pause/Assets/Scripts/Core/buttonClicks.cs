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

    }

    // Update is called once per frame
    void Update() {

        // The panel waits for the crash sequence (DeathCrash) to finish.
        if (playerDied && DeathCrash.PanelReady)
        {
            showButton();

        }
        // Back/Escape is BackNavigator's: in gameS1 it does mainMenuButton.
    }
    // The frame a replay was last asked for: a second request in the same
    // frame (two listeners on one tap) would queue a second load of gameS1
    // right behind the first -- the scene restarting twice over.
    static int replayFrame = -1;

    public void replay()
    {
        if (replayFrame == Time.frameCount) return;
        replayFrame = Time.frameCount;
        PrepareReplay();
        SceneManager.LoadScene(gameS1);
    }

    // Everything Replay does before reloading gameS1 (tests call it): a clean
    // run state, and the run's own start world pinned for the next
    // WorldManager.Start (WorldManager.RunStartWorld).
    public static void PrepareReplay()
    {
        GameStateReset.Clear();
        WorldManager.PinReplayWorld();
        startMenu.youAreInTutorial = false;
        moveBackGround.speed = 0f;
        score.totalCurrency = 0;
    }
    public void quit()
    {
        BackNavigator.QuitNow();
    }
    void showButton()
    {
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
