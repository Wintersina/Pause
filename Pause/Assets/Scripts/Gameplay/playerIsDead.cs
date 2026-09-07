using UnityEngine;
using System.Collections;
using UnityEngine.UI;

public class playerIsDead : MonoBehaviour {

    public Text deathHighScoreText; //currency
    public Text deathHighestSpeedText; //speed
    public Text deathSpeedReachedThisRoundText; //temp speed
    private bool callthisonce; // this is used so that the score is only updated once.

    // Use this for initialization
    void Awake()
    {
       

    }
    void Start () {
        deathHighScoreText.gameObject.SetActive(false);
        deathHighestSpeedText.gameObject.SetActive(false);
        deathSpeedReachedThisRoundText.gameObject.SetActive(false);
        callthisonce = true;
    }
	
	// Update is called once per frame
	void Update () {
        if (buttonClicks.playerDied && callthisonce )
        {
            float totalSpeed = PlayerPrefs.GetFloat("HighestSpeed");
            int current_speed = (int)(Mathf.Round(moveBackGround.speed * 100));
            if (current_speed > totalSpeed) PlayerPrefs.SetFloat("HighestSpeed", current_speed); else PlayerPrefs.SetFloat("HighestSpeed", totalSpeed);
            PlayerPrefs.SetFloat("PlayerCurrecny", score.totalCurrency);

            showHighScoreAfterPlayerDied(current_speed);
            callthisonce = false;
        }
    }
    void showHighScoreAfterPlayerDied(int c_s)
    {
       deathHighScoreText.text = "Star Dust \n " + PlayerPrefs.GetFloat("PlayerCurrecny").ToString("F2");
        
        deathHighestSpeedText.text = "BEST \n " + PlayerPrefs.GetFloat("HighestSpeed");
        deathSpeedReachedThisRoundText.text = "Your Speed \n" + c_s.ToString();
        deathHighScoreText.gameObject.SetActive(true);
       deathHighestSpeedText.gameObject.SetActive(true);
        deathSpeedReachedThisRoundText.gameObject.SetActive(true);
    }

}
