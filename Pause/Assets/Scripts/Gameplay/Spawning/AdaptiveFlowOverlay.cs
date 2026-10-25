using UnityEngine;
using UnityEngine.UI;

// Developer-only readout of the adaptive difficulty (docs/adaptive-difficulty.md):
// one line above the DEV badge. Compiled only into the editor and PAUSE_DEV
// builds, so a release build has neither the object nor the string.
#if UNITY_EDITOR || PAUSE_DEV
public class AdaptiveFlowOverlay : MonoBehaviour
{
    static AdaptiveFlowOverlay instance;
    Text text;

    public static void Create()
    {
        if (instance != null) return;
        var go = new GameObject("~AdaptiveFlowOverlay");
        DontDestroyOnLoad(go);
        instance = go.AddComponent<AdaptiveFlowOverlay>();
    }

    void Awake()
    {
        var canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 31999;
        var label = new GameObject("Label", typeof(RectTransform));
        label.transform.SetParent(transform, false);
        text = label.AddComponent<Text>();
        text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        text.fontSize = 18;
        text.color = new Color(.6f, 1f, .8f, .8f);
        text.alignment = TextAnchor.LowerLeft;
        text.raycastTarget = false;
        var r = label.GetComponent<RectTransform>();
        r.anchorMin = r.anchorMax = r.pivot = Vector2.zero;
        r.sizeDelta = new Vector2(520f, 26f);
        r.anchoredPosition = new Vector2(24f, 56f);
    }

    void Update()
    {
        bool show = AdaptiveDifficulty.RunActive && DeveloperUnlocks.Enabled;
        text.enabled = show;
        if (!show) return;
        text.text = AdaptiveDifficulty.Enabled
            ? "FLOW " + AdaptiveDifficulty.Meter.Flow.ToString("+0.00;-0.00;0.00") +
              "  en x" + AdaptiveDifficulty.EnemyScale.ToString("0.00") +
              "  dust x" + AdaptiveDifficulty.DustScale.ToString("0.00") +
              (AdaptiveDifficulty.Held ? "  HELD" : "")
            : "FLOW off";
    }
}
#endif
