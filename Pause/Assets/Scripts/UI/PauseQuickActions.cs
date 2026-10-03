using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.EventSystems;

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

    // Player movement runs separately from Unity UI. This geometric check is
    // deliberately independent of EventSystem timing, so the first press on
    // a pause button cannot also be interpreted as a teleport destination.
    public static bool IsScreenPointOnAction(Vector2 screenPoint)
    {
        var actions = Object.FindFirstObjectByType<PauseQuickActions>();
        return actions != null && actions.Contains(screenPoint);
    }

    bool Contains(Vector2 screenPoint)
    {
        return Contains(replayClone, screenPoint) || Contains(leaveClone, screenPoint);
    }

    static bool Contains(GameObject go, Vector2 screenPoint)
    {
        if (go == null || !go.activeInHierarchy) return false;
        var rect = go.GetComponent<RectTransform>();
        return rect != null && RectTransformUtility.RectangleContainsScreenPoint(rect, screenPoint, null);
    }

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

        var clicks = Object.FindFirstObjectByType<buttonClicks>();
        var replay = replayClone.GetComponent<Button>();
        var leave = leaveClone.GetComponent<Button>();
        if (clicks != null)
        {
            // The old persistent listeners lived behind a paused/death canvas
            // and were easy to block. These live copies call the action
            // directly, above every gameplay raycast blocker.
            if (replay != null)
            {
                replay.onClick.RemoveAllListeners();
                replay.onClick.AddListener(clicks.replay);
            }
            if (leave != null)
            {
                leave.onClick.RemoveAllListeners();
                leave.onClick.AddListener(clicks.mainMenuButton);
            }
        }
        StyleAction(replayClone, "REPLAY", new Color(.15f, .85f, 1f));
        StyleAction(leaveClone, "MENU", new Color(1f, .42f, .3f));
    }

    static void PositionTopRight(GameObject go, float yFromTop)
    {
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = new Vector2(1f, 1f);
        rt.pivot = new Vector2(1f, 1f);
        rt.sizeDelta = new Vector2(178f, 64f);
        rt.anchoredPosition = new Vector2(-20f, yFromTop);
    }

    static void StyleAction(GameObject go, string label, Color accent)
    {
        if (go == null) return;
        var image = go.GetComponent<Image>();
        if (image != null) image.color = new Color(.018f, .035f, .11f, .96f);
        var edge = go.GetComponent<Outline>() ?? go.AddComponent<Outline>();
        edge.effectColor = new Color(accent.r, accent.g, accent.b, .9f);
        edge.effectDistance = new Vector2(2f, -2f);
        var button = go.GetComponent<Button>();
        if (button != null)
        {
            var colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1f, 1f, 1f, .82f);
            colors.pressedColor = new Color(accent.r, accent.g, accent.b, .75f);
            button.colors = colors;
        }
        var text = go.GetComponentInChildren<Text>(true);
        if (text != null)
        {
            text.text = label;
            text.fontSize = 20;
            text.fontStyle = FontStyle.Bold;
            text.alignment = TextAnchor.MiddleCenter;
            text.color = Color.white;
            var textEdge = text.GetComponent<Outline>() ?? text.gameObject.AddComponent<Outline>();
            textEdge.effectColor = new Color(.02f, .08f, .18f, 1f);
            textEdge.effectDistance = new Vector2(2f, -2f);
        }
        AddAccent(go.transform, "ActionRail", new Vector2(-80f, 0f), new Vector2(7f, 50f), accent);
        AddAccent(go.transform, "ActionTickTop", new Vector2(-55f, 20f), new Vector2(33f, 3f), accent);
        AddAccent(go.transform, "ActionTickBottom", new Vector2(-55f, -20f), new Vector2(18f, 3f), accent);
    }

    static void AddAccent(Transform parent, string name, Vector2 position, Vector2 size, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);
        var image = go.GetComponent<Image>();
        image.color = new Color(color.r, color.g, color.b, .9f);
        image.raycastTarget = false;
        var rt = image.rectTransform;
        rt.anchorMin = rt.anchorMax = new Vector2(.5f, .5f);
        rt.anchoredPosition = position;
        rt.sizeDelta = size;
        rt.SetAsFirstSibling();
    }

    void Update()
    {
        // Once the pause stock is empty the run continues even with no finger
        // on screen. Leave remains available in that state as well.
        bool stoppedTouching = !TouchInput.IsPressed;
        bool pointerOnAction = EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
        bool touchOnAction = TouchInput.IsPressed && Contains(TouchInput.Position);
        bool show = buttonClicks.playerDied || stoppedTouching || pointerOnAction || touchOnAction;
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
        if (scene.name != "gameS1" && scene.name != "tutorialS5") return;
        if (Object.FindFirstObjectByType<PauseQuickActions>() != null) return;
        new GameObject("~PauseQuickActions").AddComponent<PauseQuickActions>();
    }
}
