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
        // Back/Escape. Was Android-gated, and the touchCount == 0 guard
        // swallowed the key on every other platform.
        {
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                SceneManager.LoadScene("startS4");
                return;
            }
        }
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
        Application.Quit();
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
