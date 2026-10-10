using UnityEngine;
using System.Collections;
using UnityEngine.UI;


public class score : MonoBehaviour {

    // The flight trickle of star dust is paid per unit of distance flown
    // (ScoreRules.DustPerDistance), so flying fast is worth more than
    // crawling and a boost pays for the ground it covers.

    public static float totalCurrency;
    public static float tutorialCurrency;
    // Snapshot used by the death card to show what this run earned before it
    // was added to the player's lifetime total.
    public static float runStartCurrency;
    // The in-run HUD has no star dust read-out: a run's dust is shown when it
    // ends (DeathPanelView, TutorialCompletePanel). It is still earned and
    // banked here exactly as before (StarDustLedger).
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
    // This run's RunScore token (the points score; see RunScore).
    private int scoreRun;


    // at awake, load up all the correct numbers for scores.
    void Awake()
    {
        // A run still open (Replay: this Awake beat the old scene's
        // OnDestroy) gets its score bonus before the ledger banks it.
        Settle(RunScore.RunId, StarDustLedger.RunId);
        tutorialCurrency = 0;
        paysRealDust = PaysRealDust(gameObject.scene.name,
            PlayerPrefs.GetString("HasDoneTut") == "true", startMenu.youAreInTutorial);
        ledgerRun = StarDustLedger.BeginRun(paysRealDust);
        // Practice runs score nothing; developer runs score but never save a
        // best (or reach a leaderboard).
        scoreRun = RunScore.BeginRun(paysRealDust, !DeveloperUnlocks.Enabled);
        committedOnDeath = false;
        if (paysRealDust) currencyHolder = StarDustLedger.Balance;
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
        // Bank the run once it has ended: the moment the death crash is
        // over (DeathCrash.PanelReady), so its DEATH COMBO is in the score,
        // the best and the dust bonus. playerIsDead also writes the total;
        // the ledger writes the same absolute value, so the order of the two
        // does not matter and nothing is counted twice.
        if (buttonClicks.playerDied && !committedOnDeath && DeathCrash.PanelReady)
        {
            committedOnDeath = true;
            Settle(scoreRun, ledgerRun);
            StarDustLedger.Commit(ledgerRun);
        }

        // The boss intro's freeze is scripted: a press during it, or the
        // first one after it, never spends a pause (BossEncounter).
        if (BossEncounter.FreePress && TouchInput.IsPressed) pauseCounterBool = true;
        // A planetfall's descent is scripted the same way (Planetfall.FreePress).
        if (Planetfall.FreePress && TouchInput.IsPressed) pauseCounterBool = true;
        // ... and a lift-off's rise (Liftoff.FreePress).
        if (Liftoff.FreePress && TouchInput.IsPressed) pauseCounterBool = true;
        // ... and so is a run's entry (WorldEntry: the portal arrival).
        if (PortalArrival.FreePress && TouchInput.IsPressed) pauseCounterBool = true;

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
            StepRunning();
        }
        else if (pauseCounter <= 0 && !buttonClicks.playerDied)
        {
            StepRunning();
        }
        else
            pauseCounterBool = false;
	}
    // A running frame: the flight dust, the speed read-out and the score. The
    // run has not begun while its entry plays (WorldEntry): nothing is earned.
    void StepRunning()
    {
        showSpeed();
        if (WorldEntry.Active) return;
        payDust();
        RunScore.Tick(Time.deltaTime, moveBackGround.speed);
    }

    // Leaving the run by any route (Menu, Replay, Back, scene change) banks
    // what it earned. Replay/Menu zero totalCurrency first, which is why the
    // ledger, not that field, is what gets saved.
    void OnDestroy()
    {
        Settle(scoreRun, ledgerRun);
        StarDustLedger.EndRun(ledgerRun);
    }

    // Ends the score run and pays its star-dust score bonus into the same
    // run's ledger, before the ledger banks it. Idempotent: the score run
    // banks once (RunScore.EndRun) and the bonus is paid once per ledger run
    // (StarDustLedger.PayScoreBonus); a stale token does nothing. Only a run
    // that may set a best (not the tutorial, not developer mode) earns it.
    public static float Settle(int scoreToken, int ledgerToken)
    {
        if (scoreToken <= 0 || scoreToken != RunScore.RunId) return 0f;
        RunScore.EndRun(scoreToken);
        float amount = RunScore.SavesBest ? ScoreRules.ScoreDustBonus(RunScore.Total) : 0f;
        float paid = StarDustLedger.PayScoreBonus(ledgerToken, amount);
        if (paid > 0f && paysRealDust) totalCurrency += paid;
        return paid;
    }

    // The live run (the death panel, in the run's own scene).
    public static float SettleCurrentRun()
    {
        Settle(RunScore.RunId, StarDustLedger.RunId);
        return StarDustLedger.Bonus;
    }

    void payDust()
    {
        if (paysRealDust) StarDustLedger.Earn(calcScore(ref totalCurrency));
        else calcScore(ref tutorialCurrency);
    }

    // calculates the dust earned this frame and updates the pause read-out;
    // returns the dust just earned
    float calcScore(ref float tc)
    {
        // Was `(int)speed + .001f`. speed never reaches 1, so the cast was
        // always 0 and this was really a flat .001 *per frame* -- framerate
        // dependent, paying out twice as fast at 120Hz as at 60Hz.
        // (nothing while a portal is kept waiting past its grace: PortalPressure)
        float earned = PortalPressure.EarningsClosed ? 0f : ScoreRules.FlightDust(moveBackGround.speed, Time.deltaTime);
        tc += earned;
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
        AchievementTracker.OnPauseSpent();
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

    // A dust reward that is not the collectible itself (a kill's crumb, an
    // elite going down): paid at ScoreRules.DustRewardScale of its base value.
    // Returns what was actually paid. Star and blue-atom pickups use
    // AwardStarDust directly and stay at full value.
    public static float AwardRewardDust(float baseAmount)
    {
        float paid = ScoreRules.RewardDust(baseAmount);
        AwardStarDust(paid);
        return paid;
    }

    public static void AwardStarDust(float amount)
    {
        dustPickups++;
        // A portal kept waiting past its grace: nothing earns (PortalPressure).
        if (PortalPressure.EarningsClosed) return;
        if (paysRealDust)
        {
            totalCurrency += amount;
            StarDustLedger.Earn(amount);
        }
        else tutorialCurrency += amount;
    }
    public static void incromentPause()
    {
        pauseCounter += 2;
        AchievementTracker.OnPauseCount(pauseCounter);
    }

}
