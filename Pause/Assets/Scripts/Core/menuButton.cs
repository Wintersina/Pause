using UnityEngine;
using System.Collections;
using UnityEngine.UI;
using UnityEngine.SceneManagement;

public class menuButton : MonoBehaviour {

    public Button playB;
    public Button backB;
    public static bool playerDied;

    // Use this for initialization
    void Start()
    {
        playerDied = false;
        playB.gameObject.SetActive(true);
        backB.gameObject.SetActive(true);
    }

    // Update is called once per frame

    public void play()
    {
        moveBackGround.speed = 0f;
        score.totalCurrency = 0;
        // In the space dock, lift-off undocks the selected (else equipped) ship and the dock
        // continues to the same place this always went (game, or tutorial).
        if (SpaceDock.Instance != null)
        {
            SpaceDock.Instance.LiftOff();
            return;
        }
        if (PlayerPrefs.GetString("HasDoneTut") == "true")
        {
            rotateRight.flyOffTimer = 2f;
            rotateRight.flyOffChecker = true;
            shopingShips.turnOffCanves();
           
        }
        else
        {
            SceneManager.LoadScene("tutorialS5");
        }
    }
    // BACK (UnityEvent-wired): one level up via BackNavigator -- in the dock
    // that first undoes a ship selection, on Options it closes the
    // leaderboard panel, otherwise it goes home.
    public void back()
    {
        BackNavigator.Back();
    }
 
}
