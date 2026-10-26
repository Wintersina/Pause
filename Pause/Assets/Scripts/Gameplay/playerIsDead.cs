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
        if (buttonClicks.playerDied && waitingForDeath && DeathCrash.PanelReady) { waitingForDeath = false; BeginResults(); }   // flag first: a throw must not re-run it every frame
    }

    // The death screen's save of the run's dust. It writes the ledger's
    // balance (StarDustLedger.Stage), not score.totalCurrency: a grant that
    // landed mid-run (a Play purchase or reward delivered on a focus return)
    // went into the ledger's baseline only, and writing score's mirror here
    // flushed the lower figure -- lost for good if the app was killed on
    // this screen. A practice run has no paying ledger and writes nothing
    // (score.totalCurrency is 0 there, which used to wipe the saved dust).
    public static void SaveDustForResults()
    {
        StarDustLedger.Stage();
        PlayerPrefs.Save();
    }

    void BeginResults()
    {
        // Bank the run and its star-dust score bonus first, so the totals
        // below (and the dust written) include it.
        float scoreBonus = score.SettleCurrentRun();
        SaveDustForResults();

        var results = new DeathPanelView.Results
        {
            score = RunScore.Total,
            bestScore = RunScore.SavesBest ? System.Math.Max(RunScore.BestAtStart, RunScore.Total) : RunScore.BestAtStart,
            newBest = RunScore.IsNewBest,
            ranked = RunScore.SavesBest,
            practice = !RunScore.Scoring,
            unranked = RunScore.Unranked,
            parts = RunScore.Parts,
            dustAtStart = score.runStartCurrency,
            dustWon = Mathf.Max(0f, score.totalCurrency - score.runStartCurrency),
            dustBonus = scoreBonus,
            killedBy = DamageLog.NameOf(DamageLog.RunKiller),
            hurtBy = DamageLog.RunHurtLine(DeathPanelView.HurtLineChars),
        };

        // A new best on a ranked run: its kill cam is kept for the codex
        // (KillCamArchive; only the latest such death, each replaces the last).
        SaveKillCam(results.newBest, results.ranked, results.score);

        var canvas = SceneUtil.FindAny("PopUpCanvas");
        if (canvas == null) return;

        // After a second death in a row, maybe offer an affordable upgrade
        // first (UpgradeOfferRules); either answer goes on to the results.
        UpgradeOfferRules.Candidate offer;
        if (score.paysRealDust)
        {
            UpgradeOfferStore.RunDied();
            if (UpgradeOfferStore.TryGetOffer(out offer))
            {
                float balance = PlayerPrefs.GetFloat(StarDustLedger.CurrencyKey);
                UpgradeOfferCard.Show(canvas.transform, deathHighScoreText.font, offer, balance, bought =>
                {
                    // The panel's TOTAL line is built from this: keep it the real balance.
                    if (bought) results.dustAtStart -= offer.price;
                    DeathPanelView.Build(canvas.transform, deathScoreText, deathSpareText,
                                         deathHighScoreText, FindButton("Replay"), FindButton("MainMenu"), results);
                });
                return;
            }
            // Same moment and backoff, but the flown ship has nothing left
            // to upgrade: offer the next roster ship instead (ShipOfferRules).
            ShipOfferRules.Offer shipOffer;
            if (ShipOfferStore.TryGetOffer(out shipOffer))
            {
                float balance = PlayerPrefs.GetFloat(StarDustLedger.CurrencyKey);
                ShipOfferCard.Show(canvas.transform, deathHighScoreText.font, shipOffer, balance, bought =>
                {
                    if (bought) results.dustAtStart -= shipOffer.price;
                    DeathPanelView.Build(canvas.transform, deathScoreText, deathSpareText,
                                         deathHighScoreText, FindButton("Replay"), FindButton("MainMenu"), results);
                });
                return;
            }
        }
        DeathPanelView.Build(canvas.transform, deathScoreText, deathSpareText,
                             deathHighScoreText, FindButton("Replay"), FindButton("MainMenu"), results);
    }

    static void SaveKillCam(bool newBest, bool ranked, long runScore)
    {
        if (!KillCamArchive.ShouldSave(newBest, ranked)) return;
        var recorder = KillCamRecorder.Instance;
        var clip = recorder != null ? recorder.Clip(KillCamClip.LiveWindow) : null;
        if (clip == null) return;
        KillCamArchive.Save(clip, KillCamArchive.RecordForThisRun(System.Math.Max(0L, runScore)));
    }

    static Button FindButton(string name)
    {
        var go = SceneUtil.FindAny(name);
        return go != null ? go.GetComponent<Button>() : null;
    }
}
