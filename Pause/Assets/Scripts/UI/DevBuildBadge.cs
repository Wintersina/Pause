using UnityEngine;
using UnityEngine.UI;

// A small "DEV" tag in the bottom-left corner of every screen of a PAUSE_DEV
// build, so a developer build is never mistaken for a release one. Purely
// visual: it never takes touches.
public class DevBuildBadge : MonoBehaviour
{
    static DevBuildBadge instance;
    RectTransform rect;
    Canvas canvas;

    public static void Create()
    {
        if (instance != null) return;
        var go = new GameObject("~DevBuildBadge");
        DontDestroyOnLoad(go);
        instance = go.AddComponent<DevBuildBadge>();
    }

    void Awake()
    {
        canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 32000;
        var scaler = gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(800f, 1000f);
        scaler.matchWidthOrHeight = 0.5f;

        var label = new GameObject("Label", typeof(RectTransform));
        label.transform.SetParent(transform, false);
        var text = label.AddComponent<Text>();
        text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        text.text = "DEV";
        text.fontSize = 20;
        text.fontStyle = FontStyle.Bold;
        text.color = new Color(1f, 0.79f, 0.26f, 0.75f);
        text.alignment = TextAnchor.LowerLeft;
        text.raycastTarget = false;

        rect = label.GetComponent<RectTransform>();
        rect.anchorMin = rect.anchorMax = rect.pivot = Vector2.zero;
        rect.sizeDelta = new Vector2(80f, 28f);
        Place();
    }

    void Update() { Place(); }

    // Clear of the rounded corner / gesture bar on phones.
    void Place()
    {
        float scale = canvas.scaleFactor > 0f ? canvas.scaleFactor : 1f;
        var safe = ScreenInfo.SafeArea;
        rect.anchoredPosition = new Vector2(safe.xMin / scale + 10f, safe.yMin / scale + 8f);
    }
}
