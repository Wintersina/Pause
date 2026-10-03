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
        if (buttonClicks.playerDied)
        {
         
            showButton();
        }
    }
    public void replay()
    {
            startMenu.youAreInTutorial = false;
            moveBackGround.speed = 0f;
            score.totalCurrency = 0;
            SceneManager.LoadScene("gameS1");
    }
    public void quit()
    {
        BackNavigator.QuitNow();
    }
    void showButton()
    {
        activeCanvis.gameObject.SetActive(true);   
    }
    public void mainMenuButton()
    {
        SceneManager.LoadScene("startS4");
    }
}
