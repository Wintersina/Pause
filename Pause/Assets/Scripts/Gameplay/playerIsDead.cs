using UnityEngine;
using UnityEngine.UI;

// Settles a finished run (best speed, star dust) and hands the numbers -- with
// the run score (RunScore, banked by score.cs) -- to the Flight Complete
// panel. The panel itself -- frame, cards, buttons and the intro animation --
// lives in DeathPanelView.
public class playerIsDead : MonoBehaviour
{
    public Text deathHighScoreText;
    public Text deathHighestSpeedText;
    public Text deathSpeedReachedThisRoundText;
    bool waitingForDeath = true;

    void Start()
    {
        deathHighScoreText.gameObject.SetActive(false);
        deathHighestSpeedText.gameObject.SetActive(false);
        deathSpeedReachedThisRoundText.gameObject.SetActive(false);
    }

    void Update()
    {
        // The panel waits for the crash sequence (and its domino) to play
        // out (DeathCrash); the run is banked then too (score.cs).
        if (buttonClicks.playerDied && waitingForDeath && DeathCrash.PanelReady) { BeginResults(); waitingForDeath = false; }
    }

    void BeginResults()
    {
        // Bank the run and its star-dust score bonus first, so the totals
        // below (and the dust written) include it.
        float scoreBonus = score.SettleCurrentRun();
        int previousBest = Mathf.RoundToInt(PlayerPrefs.GetFloat("HighestSpeed"));
        int runSpeed = Mathf.RoundToInt(moveBackGround.speed * 100f);
        int bestSpeed = Mathf.Max(previousBest, runSpeed);
        PlayerPrefs.SetFloat("HighestSpeed", bestSpeed);
        PlayerPrefs.SetFloat("PlayerCurrecny", score.totalCurrency);
        PlayerPrefs.Save();

        var results = new DeathPanelView.Results
        {
            score = RunScore.Total,
            bestScore = RunScore.SavesBest ? System.Math.Max(RunScore.BestAtStart, RunScore.Total) : RunScore.BestAtStart,
            newBest = RunScore.IsNewBest,
            ranked = RunScore.SavesBest,
            practice = !RunScore.Scoring,
            parts = RunScore.Parts,
            bestSpeed = bestSpeed,
            runSpeed = runSpeed,
            dustAtStart = score.runStartCurrency,
            dustWon = Mathf.Max(0f, score.totalCurrency - score.runStartCurrency),
            dustBonus = scoreBonus,
        };

        var canvas = SceneUtil.FindAny("PopUpCanvas");
        if (canvas == null) return;
        DeathPanelView.Build(canvas.transform, deathHighestSpeedText, deathSpeedReachedThisRoundText,
                             deathHighScoreText, FindButton("Replay"), FindButton("MainMenu"), results);
    }

    static Button FindButton(string name)
    {
        var go = SceneUtil.FindAny(name);
        return go != null ? go.GetComponent<Button>() : null;
    }
}
