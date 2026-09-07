using UnityEngine;
using UnityEngine.SceneManagement;

// A small always-reachable leave/replay pair pinned to the top-right corner,
// shown only during an ordinary pause -- finger lifted, still alive, pauses
// left. Until now the only way off a run mid-flight was to burn through
// every remaining pause and actually die first; this gives the player an
// exit without forcing that.
//
// Clones of replayWhenPausedButton/mainMenuWhenPausedButton rather than new
// buttons from scratch, so they inherit the same sprites and the same
// Button.onClick wiring straight to the existing buttonClicks instance --
// Instantiate() does not touch a persistent listener's reference to an
// object outside the cloned hierarchy, so that wiring carries over intact.
// The originals are untouched and keep their own death-only visibility
// (moveStarsBackground gates them on buttonClicks.playerDied); these clones
// are a second, independent pair with their own top-right position and
// their own "just paused" visibility rule, so the two never fight.
public class PauseQuickActions : MonoBehaviour
{
    GameObject replayClone, leaveClone;

    void Start()
    {
        var replaySource = SceneUtil.FindAny("replayWhenPausedButton");
        var leaveSource = SceneUtil.FindAny("mainMenuWhenPausedButton");
        if (replaySource == null || leaveSource == null) return;

        replayClone = Instantiate(replaySource, replaySource.transform.parent);
        leaveClone = Instantiate(leaveSource, leaveSource.transform.parent);
        replayClone.name = "replayQuickAction";
        leaveClone.name = "leaveQuickAction";

        PositionTopRight(replayClone, -46f);
        PositionTopRight(leaveClone, -122f);
    }

    static void PositionTopRight(GameObject go, float xFromRightEdge)
    {
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = new Vector2(1f, 1f);
        rt.pivot = new Vector2(1f, 1f);
        rt.sizeDelta = new Vector2(64f, 58f);
        rt.anchoredPosition = new Vector2(xFromRightEdge, -36f);
    }

    void Update()
    {
        bool show = !buttonClicks.playerDied && score.pauseCounter > 0 && !TouchInput.IsPressed;
        if (replayClone != null) replayClone.SetActive(show);
        if (leaveClone != null) leaveClone.SetActive(show);
    }
}

public static class PauseQuickActionsBootstrap
{
    [RuntimeInitializeOnLoadMethod]
    static void Init()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (scene.name != "gameS1") return;
        if (Object.FindFirstObjectByType<PauseQuickActions>() != null) return;
        new GameObject("~PauseQuickActions").AddComponent<PauseQuickActions>();
    }
}
