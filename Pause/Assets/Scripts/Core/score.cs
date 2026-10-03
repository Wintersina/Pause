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
    // A real run's allotment, for screens that explain it (tutorial end card).
    public const int RealRunPauses = PAUSECOUNTER;

    public const string TutorialScene = "tutorialS5";

    // Whether this run's dust is real (credited to the saved total). Decided
    // once when the run's scene loads so nothing mid-run can flip it --
    // Hints marks the tutorial done before it ends, and used to start
    // paying real dust from that moment.
    public static bool paysRealDust;
    // This run's StarDustLedger token, and whether death already committed it.
    private int ledgerRun;
    private bool committedOnDeath;


    // at awake, load up all the correct numbers for scores.
    void Awake()
    {
        
        // currencyText.text = "Currency Gathered : " + PlayerPrefs.GetInt("brickScore").ToString();
        tutorialCurrency = 0;
        paysRealDust = PaysRealDust(gameObject.scene.name,
            PlayerPrefs.GetString("HasDoneTut") == "true", startMenu.youAreInTutorial);
        ledgerRun = StarDustLedger.BeginRun(paysRealDust);
        committedOnDeath = false;
        if (paysRealDust)
        {
            currencyHolder = StarDustLedger.Balance;
            currencyText.text = "Star Dust : " + currencyHolder.ToString("F2");
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


        if (paysRealDust)
        {
            pauseCounter = PAUSECOUNTER;
            pauseCounterText.text = "Pauses Remaining : " + pauseCounter.ToString();
            speedValue.text = "Current Speed : 0";
            totalCurrency = currencyHolder;
            runStartCurrency = totalCurrency;
        }
        else
        {
            pauseCounter = TUTPAUSECOUNTER;
            pauseCounterText.text = "Pauses Remaining : " + pauseCounter.ToString();
            speedValue.text = "Current Speed : 0";
            totalCurrency = 0f;
            runStartCurrency = 0f;
        }
    }
	
	// Update is called once per frame
	void Update () {
        // Bank the run the moment it ends. playerIsDead also writes the
        // total; the ledger writes the same absolute value, so the order of
        // the two does not matter and nothing is counted twice.
        if (buttonClicks.playerDied && !committedOnDeath)
        {
            committedOnDeath = true;
            StarDustLedger.Commit(ledgerRun);
        }

        // The boss intro's freeze is scripted: a press during it, or the
        // first one after it, never spends a pause (BossEncounter).
        if (BossEncounter.FreePress && TouchInput.IsPressed) pauseCounterBool = true;

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
            payDust();
            // calculate speed
            showSpeed();

        }
        else if (pauseCounter <= 0 && !buttonClicks.playerDied)
        {
            payDust();
            // calculate speed
            showSpeed();
        }
        else
            pauseCounterBool = false;
	}
    // Leaving the run by any route (Menu, Replay, Back, scene change) banks
    // what it earned. Replay/Menu zero totalCurrency first, which is why the
    // ledger, not that field, is what gets saved.
    void OnDestroy()
    {
        StarDustLedger.EndRun(ledgerRun);
    }

    void payDust()
    {
        if (paysRealDust) StarDustLedger.Earn(calcScore(ref totalCurrency));
        else calcScore(ref tutorialCurrency);
    }

    // calculates score and updates canvis; returns the dust just earned
    float calcScore(ref float tc)
    {
        // Was `(int)speed + .001f`. speed never reaches 1, so the cast was
        // always 0 and this was really a flat .001 *per frame* -- framerate
        // dependent, paying out twice as fast at 120Hz as at 60Hz.
        float t = topSpeed <= 0 ? 0 : Mathf.Clamp01(moveBackGround.speed / topSpeed);
        float earned = dustPerSecondAtTopSpeed * t * Time.deltaTime;
        tc += earned;
        currencyText.text = "Star Dust : " + tc.ToString("F2");
        pauseCounterText.text = "Pauses Remaining : " + pauseCounter.ToString();
        return earned;
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

    // The one rule for tutorial runs: anything in the tutorial scene, or any
    // run before the tutorial is done, pays practice dust only and never
    // touches the saved total.
    public static bool PaysRealDust(string sceneName, bool hasDoneTut, bool inTutorial)
    {
        return hasDoneTut && !inTutorial && sceneName != TutorialScene;
    }

    public static bool ShouldSpendPause(bool runHasStarted, bool alreadySpentThisPress)
    {
        return runHasStarted && !alreadySpentThisPress;
    }

    // Counts every pickup that pays star dust (stars, the blue atom, a
    // destroyed enemy). The tutorial watches it to know a star was caught.
    public static int dustPickups;

    public static void AwardStarDust(float amount)
    {
        dustPickups++;
        if (paysRealDust)
        {
            totalCurrency += amount;
            StarDustLedger.Earn(amount);
        }
        else tutorialCurrency += amount;

        var hud = Object.FindFirstObjectByType<score>();
        if (hud != null && hud.currencyText != null)
        {
            float value = paysRealDust ? totalCurrency : tutorialCurrency;
            hud.currencyText.text = "Star Dust : " + value.ToString("F2");
        }
    }
    public static void incromentPause()
    {
        pauseCounter += 2;
    }

}
