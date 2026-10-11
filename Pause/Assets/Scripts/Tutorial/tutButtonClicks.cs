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
    // LIFT OFF: the ship flies into a portal, then the first level loads and
    // its run begins with the usual portal arrival (TutorialLiftOff).
    public void replay()
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
