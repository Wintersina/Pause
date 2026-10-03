using UnityEngine;
using System.Collections;
using UnityEngine.UI;


public class score : MonoBehaviour {

    [Header("Star dust")]
    [Tooltip("Star dust earned per second at top speed. Payout scales with " +
             "speed, so flying fast is worth more than crawling.")]
    public float dustPerSecondAtTopSpeed = 0.05f;

    [Tooltip("Speed treated as 'top speed' for the payout curve. Should match " +
             "moveBackGround.maxSpeed.")]
    public float topSpeed = 0.6f;

    public static float totalCurrency;
    public static float tutorialCurrency;
    // Snapshot used by the death card to show what this run earned before it
    // was added to the player's lifetime total.
    public static float runStartCurrency;
    public Text currencyText;
    public Text speedValue;

    private float currencyHolder;
    public static int pauseCounter;
    public Text pauseCounterText;
    private bool pauseCounterBool;
    // The launch touch starts a run. It is not a pause-resume action and must
    // never consume one of the player's earned teleports.
    private bool hasStartedRun;
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
        hasStartedRun = false;


        if (PlayerPrefs.GetString("HasDoneTut") == "true")
        {
            pauseCounter = PAUSECOUNTER;
            pauseCounterText.text = "Pauses Remaining : " + pauseCounter.ToString();
            speedValue.text = "Currnet Speed : 0";
            totalCurrency = currencyHolder;
            runStartCurrency = totalCurrency;
        }
        else
        {
            pauseCounter = TUTPAUSECOUNTER;
            pauseCounterText.text = "Pauses Remaining : " + pauseCounter.ToString();
            speedValue.text = "Currnet Speed : 0";
            runStartCurrency = 0f;
        }
    }
	
	// Update is called once per frame
	void Update () {
        if (TouchInput.IsPressed && !buttonClicks.playerDied)
        {
            // Holding the first touch must behave exactly like a normal held
            // run without subtracting a pause. Once it is released, the next
            // press is a genuine resume/teleport and spends as usual.
            if (!hasStartedRun)
            {
                hasStartedRun = true;
                pauseCounterBool = true;
            }
            else if (ShouldSpendPause(hasStartedRun, pauseCounterBool))
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
        // Was `(int)speed + .001f`. speed never reaches 1, so the cast was
        // always 0 and this was really a flat .001 *per frame* -- framerate
        // dependent, paying out twice as fast at 120Hz as at 60Hz.
        float t = topSpeed <= 0 ? 0 : Mathf.Clamp01(moveBackGround.speed / topSpeed);
        tc += dustPerSecondAtTopSpeed * t * Time.deltaTime;
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

    public static bool ShouldSpendPause(bool runHasStarted, bool alreadySpentThisPress)
    {
        return runHasStarted && !alreadySpentThisPress;
    }

    public static void AwardStarDust(float amount)
    {
        if (PlayerPrefs.GetString("HasDoneTut") == "true") totalCurrency += amount;
        else tutorialCurrency += amount;

        var hud = Object.FindFirstObjectByType<score>();
        if (hud != null && hud.currencyText != null)
        {
            float value = PlayerPrefs.GetString("HasDoneTut") == "true" ? totalCurrency : tutorialCurrency;
            hud.currencyText.text = "Star Dust : " + value.ToString("F2");
        }
    }
    public static void incromentPause()
    {
        pauseCounter += 2;
    }

}
