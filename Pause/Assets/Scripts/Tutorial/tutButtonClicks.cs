using UnityEngine;
using System.Collections;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class tutButtonClicks : MonoBehaviour {

   
    public static Canvas activeCanvis;
   
    // Use this for initialization
    void Start()
    {
        activeCanvis = GameObject.Find("PopUpCanvas").GetComponent<Canvas>();
        activeCanvis.gameObject.SetActive(false);
        buttonClicks.playerDied = false;
 
    }

    // Update is called once per frame
    void Update()
    {
        // Back/Escape is BackNavigator's: in tutorialS5 it goes home, like
        // mainMenuButton / the Home quick action.
        // The panel waits for the crash sequence (DeathCrash) to finish.
        if (buttonClicks.playerDied && DeathCrash.PanelReady)
        {
         
            showButton();
        }
    }
    // REPLAY (the pause quick action, the scene's replay buttons): restarts
    // the TUTORIAL from its first step -- never the main game. LIFT OFF is
    // liftOff() below.
    static int replayFrame = -1;
    public void replay()
    {
        // two listeners on one tap must not queue two loads
        if (replayFrame == Time.frameCount) return;
        replayFrame = Time.frameCount;
        if (TutorialLiftOff.Playing) return;   // already on the way to the level
        PrepareRestart();
        LoadTutorial();
    }

    // Everything a restart does before reloading tutorialS5 (tests call it):
    // a clean run state, the tutorial's own flags set again, and nothing of
    // the old attempt (wall, atoms, popups, damage) left behind.
    public static void PrepareRestart()
    {
        TutorialWall.Clear();
        TutorialEnemy.Clear();
        spawnGoodStuffTut.RemoveLiveAtom();
        spawnGoodStuffTut.keepAtomComing = TutorialAtom.None;
        Hints.reachedTheEndOfTut = false;
        GameStateReset.Clear();
        startMenu.PrepareTutorialRun();
    }

    // Swapped by tests; the game reloads the tutorial scene.
    public static System.Action LoadTutorial = () => SceneManager.LoadScene(TutorialSkip.TutorialSceneName);

    // LIFT OFF (the Tutorial Complete card): the ship flies into a portal,
    // then the first level loads and its run begins with the usual portal
    // arrival (TutorialLiftOff).
    public void liftOff()
    {
        TutorialLiftOff.Begin();
    }
    public void quit()
    {
        BackNavigator.QuitNow();
    }
    void showButton()
    {
        activeCanvis.gameObject.SetActive(true);   
    }
    // Swapped by tests; the game loads the start menu.
    public static System.Action LoadMenu = () => SceneManager.LoadScene("startS4");

    // HOME: the start menu.
    public void mainMenuButton()
    {
        if (TutorialLiftOff.Playing) return;   // already on the way to the level
        LoadMenu();
    }
}
