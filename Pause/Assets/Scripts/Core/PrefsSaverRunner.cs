using UnityEngine;

// Lives for the whole session and drives PrefsSaver. Backgrounding is the
// last reliable callback a phone gives before the OS may kill the app.
public class PrefsSaverRunner : MonoBehaviour
{
    void Update()
    {
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
        PrefsSaver.SaveNow();
    }
}
