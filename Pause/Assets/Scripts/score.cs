using UnityEngine;
using System.Collections;
using UnityEngine.UI;


public class score : MonoBehaviour {
    public static float totalCurrency;
    public static float tutorialCurrency;
    public Text currencyText;
    public Text speedValue;

    private float currencyHolder;
    public static int pauseCounter;
    public Text pauseCounterText;
    private bool pauseCounterBool;
    private const int PAUSECOUNTER = 5;
    private const int TUTPAUSECOUNTER = 50;


    // at awake, load up all the correct numbers for scores.
    void Awake()
    {
        
        // currencyText.text = "Currency Gathered : " + PlayerPrefs.GetInt("brickScore").ToString();
        tutorialCurrency = 0;
        if (PlayerPrefs.GetString("HasDoneTut") == "true")
        {
  
            currencyText.text = "Star Dust : " + PlayerPrefs.GetFloat("PlayerCurrecny").ToString("F2");
            currencyHolder = PlayerPrefs.GetFloat("PlayerCurrecny");
        }
        else
        {

            currencyText.text = "Star Dust : 0";
        }
    }
	// Use this for initialization
	void Start () {

        pauseCounterBool = false;


        if (PlayerPrefs.GetString("HasDoneTut") == "true")
        {
            pauseCounter = PAUSECOUNTER;
            pauseCounterText.text = "Pauses Remaining : " + pauseCounter.ToString();
            speedValue.text = "Currnet Speed : 0";
            totalCurrency = currencyHolder;
        }
        else
        {
            pauseCounter = TUTPAUSECOUNTER;
            pauseCounterText.text = "Pauses Remaining : " + pauseCounter.ToString();
            speedValue.text = "Currnet Speed : 0";
        }
    }
	
	// Update is called once per frame
	void Update () {
        if (TouchInput.IsPressed && !buttonClicks.playerDied)
        {
            
            if (!pauseCounterBool)
                pauseCounterFunction();
            if (PlayerPrefs.GetString("HasDoneTut") == "true" && !startMenu.youAreInTutorial)
            {
                calcScore(ref totalCurrency);
            }
            else
            {
                calcScore(ref tutorialCurrency);
            }
            // calculate speed
            showSpeed();

        }
        else if (pauseCounter <= 0 && !buttonClicks.playerDied)
        {
            if (PlayerPrefs.GetString("HasDoneTut") == "true" && !startMenu.youAreInTutorial)
            {
                calcScore(ref totalCurrency);
            }
            else
            {
                calcScore(ref tutorialCurrency);
            }
            // calculate speed
            showSpeed();
        }
        else
            pauseCounterBool = false;
	}
    // calculates score and updates canvis
    void calcScore(ref float tc)
    {
        tc += (int)moveBackGround.speed + .001f;
        currencyText.text = "Star Dust : " + tc.ToString("F2");
        pauseCounterText.text = "Pauses Remaining : " + pauseCounter.ToString();
    }
    
    // calculates speed and updates canvis.
    void showSpeed()
    {
        speedValue.text = "Current Speed : " + ( Mathf.Round(moveBackGround.speed * 100)).ToString();

    }
    void pauseCounterFunction()
    {
        pauseCounter = pauseCounter - 1;
        pauseCounterBool = true;
    }
    public static void incromentPause()
    {
        pauseCounter += 2;
    }

}
