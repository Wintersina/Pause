using UnityEngine;
using UnityEngine.SceneManagement;

// Starts the platform sign-in once per app launch and drives CloudSync for
// the rest of the session. Nothing waits on it: the first scene loads and
// plays normally while sign-in happens in the background.
public class CloudSyncRunner : MonoBehaviour
{
    const float PollInterval = 2f;
    float nextPollAt;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        if (FindFirstObjectByType<CloudSyncRunner>() != null) return;
        var go = new GameObject("~CloudSync");
        go.hideFlags = HideFlags.HideInHierarchy;
        DontDestroyOnLoad(go);
        go.AddComponent<CloudSyncRunner>();
        CloudSync.Launch(PlayerAccounts.Current);
    }

    void OnEnable() { SceneManager.sceneLoaded += OnSceneLoaded; }
    void OnDisable() { SceneManager.sceneLoaded -= OnSceneLoaded; }

    // A scene change is where runs end and the shop is left.
    void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (CloudSync.Instance != null) CloudSync.Instance.Poll(urgent: true);
    }

    void Update()
    {
        if (CloudSync.Instance == null || Time.unscaledTime < nextPollAt) return;
        nextPollAt = Time.unscaledTime + PollInterval;
        CloudSync.Instance.Poll();
    }

    void OnApplicationPause(bool paused)
    {
        if (!paused || CloudSync.Instance == null) return;
        StarDustLedger.Stage();   // include an in-progress run's dust
        CloudSync.Instance.Poll(urgent: true, force: true);
    }
}
