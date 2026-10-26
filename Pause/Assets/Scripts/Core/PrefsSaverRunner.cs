using UnityEngine;

// Lives for the whole session and drives PrefsSaver. Backgrounding is the
// last reliable callback a phone gives before the OS may kill the app, so
// that is where an in-progress run's star dust is written out as well.
public class PrefsSaverRunner : MonoBehaviour
{
    float nextStageAt;

    void Update()
    {
        // Keep the saved dust no more than one interval behind a live run,
        // so even a crash with no pause callback loses very little.
        if (StarDustLedger.IsActive && Time.unscaledTime >= nextStageAt)
        {
            StarDustLedger.Stage();
            RunScore.Stage();
            nextStageAt = Time.unscaledTime + PrefsSaver.SaveInterval;
        }
        // A finished kill-cam write is committed to PlayerPrefs here, not only
        // when the codex reads it (Settle returns at once with nothing pending).
        KillCamArchive.Settle();
        if (!PrefsSaver.Dirty) return;
        PrefsSaver.SaveIfDue(Time.unscaledTime);
    }

    void OnApplicationPause(bool paused)
    {
        if (paused) Flush();
    }

    void OnApplicationQuit()
    {
        Flush();
    }

    static void Flush()
    {
        KillCamArchive.Settle();   // a replay written just before quitting still gets its record
        StarDustLedger.Stage();
        RunScore.Stage();   // best score so far; the run stays open
        PrefsSaver.SaveNow();
    }
}
