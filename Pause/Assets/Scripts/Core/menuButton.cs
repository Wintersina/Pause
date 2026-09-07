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
    public void back()
    {
        if(AdMob.isAdsShowwing)
            AdMob.hide();
        SceneManager.LoadScene("startS4");
    }
 
}
