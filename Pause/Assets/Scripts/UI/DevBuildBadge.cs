using UnityEngine;
using UnityEngine.UI;

// A small "DEV" tag in the bottom-left corner of every screen of a PAUSE_DEV
// build, so a developer build is never mistaken for a release one, followed by
// the short commit hash baked into Resources/BuildInfo.txt by BuildScript
// (absent in the editor, where it is just "DEV"). Purely visual: it never
// takes touches.
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
        scaler.matchWidthOrHeight = 0.5f;
        UiScaleFloor.Configure(scaler, new Vector2(800f, 1000f), 0f, 0f);   // developer builds only: no floor

        var label = new GameObject("Label", typeof(RectTransform));
        label.transform.SetParent(transform, false);
        var text = label.AddComponent<Text>();
        text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        var info = Resources.Load<TextAsset>("BuildInfo");
        text.text = info != null && info.text.Trim().Length > 0 ? "DEV " + info.text.Trim() : "DEV";
        text.fontSize = 20;
        text.fontStyle = FontStyle.Bold;
        text.color = new Color(1f, 0.79f, 0.26f, 0.75f);
        text.alignment = TextAnchor.LowerLeft;
        text.raycastTarget = false;

        rect = label.GetComponent<RectTransform>();
        rect.anchorMin = rect.anchorMax = rect.pivot = Vector2.zero;
        rect.sizeDelta = new Vector2(240f, 28f);
        Place();
    }

    void Update() { Place(); }

    // Clear of the rounded corner / gesture bar on phones. Android reports no
    // inset for a display's rounded corners (only cutouts and bars), so with
    // no bottom inset the tag keeps CornerMargin from both edges: enough to
    // clear the arc of the roundest phone corners (about 0.3 x the radius).
    public const float CornerMargin = 24f;

    void Place()
    {
        float scale = canvas.scaleFactor > 0f ? canvas.scaleFactor : 1f;
        var safe = ScreenInfo.SafeArea;
        float left = safe.xMin / scale, bottom = safe.yMin / scale;
        rect.anchoredPosition = new Vector2(left + Mathf.Max(10f, CornerMargin - left), bottom + Mathf.Max(8f, CornerMargin - bottom));
    }
}
