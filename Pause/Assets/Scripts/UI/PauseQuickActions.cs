using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// A small always-reachable leave/replay pair pinned to the top-right corner.
// It is shared by an ordinary pause and the death screen so the controls keep
// the same place and remain above the death dialog's raycast blocker.
//
// Clones of replayWhenPausedButton/mainMenuWhenPausedButton rather than new
// buttons from scratch, so they inherit the same sprites and the same
// Button.onClick wiring straight to the existing buttonClicks instance --
// Instantiate() does not touch a persistent listener's reference to an
// object outside the cloned hierarchy, so that wiring carries over intact.
// The originals remain hidden templates. The live copies sit on their own
// high-sorting canvas, above both gameplay and the death popup.
public class PauseQuickActions : MonoBehaviour
{
    GameObject replayClone, leaveClone;

    void Start()
    {
        var replaySource = SceneUtil.FindAny("replayWhenPausedButton");
        var leaveSource = SceneUtil.FindAny("mainMenuWhenPausedButton");
        if (replaySource == null || leaveSource == null) return;

        var holder = new GameObject("RunActionCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        var canvas = holder.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 90;
        var scaler = holder.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(800f, 1000f);
        scaler.matchWidthOrHeight = 0.5f;

        replayClone = Instantiate(replaySource, holder.transform);
        leaveClone = Instantiate(leaveSource, holder.transform);
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
        // Once the pause stock is empty the run continues even with no finger
        // on screen. Leave remains available in that state as well.
        bool stoppedTouching = !TouchInput.IsPressed;
        bool show = buttonClicks.playerDied || (!buttonClicks.playerDied && stoppedTouching);
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
