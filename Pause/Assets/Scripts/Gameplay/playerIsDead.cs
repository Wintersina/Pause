using UnityEngine;
using UnityEngine.UI;

// Settles a finished run (star dust) and hands the numbers -- with the run
// score (RunScore, banked by score.cs) -- to the Flight Complete panel. The
// panel itself -- frame, cards, buttons and the intro animation -- lives in
// DeathPanelView. No speed is recorded or shown: speed is capped
// (SpeedRamp.Cap), so it is not a result of a run.
public class playerIsDead : MonoBehaviour
{
    public Text deathHighScoreText;
    // The scene's two old speed Texts, kept wired under their serialized
    // names: the first is the panel's score figure, the second has no job.
    [UnityEngine.Serialization.FormerlySerializedAs("deathHighestSpeedText")]
    public Text deathScoreText;
    [UnityEngine.Serialization.FormerlySerializedAs("deathSpeedReachedThisRoundText")]
    public Text deathSpareText;
    bool waitingForDeath = true;

    void Start()
    {
        deathHighScoreText.gameObject.SetActive(false);
        deathScoreText.gameObject.SetActive(false);
        deathSpareText.gameObject.SetActive(false);
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
            dustAtStart = score.runStartCurrency,
            dustWon = Mathf.Max(0f, score.totalCurrency - score.runStartCurrency),
            dustBonus = scoreBonus,
        };

        var canvas = SceneUtil.FindAny("PopUpCanvas");
        if (canvas == null) return;
        DeathPanelView.Build(canvas.transform, deathScoreText, deathSpareText,
                             deathHighScoreText, FindButton("Replay"), FindButton("MainMenu"), results);
    }

    static Button FindButton(string name)
    {
        var go = SceneUtil.FindAny(name);
        return go != null ? go.GetComponent<Button>() : null;
    }
}
