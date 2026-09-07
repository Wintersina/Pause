using System.Collections;
using UnityEngine;
using UnityEngine.UI;

// Transient centre-screen announcement ("FROST", "PORTAL OPEN").
// Self-building so it needs no scene wiring.
public class WorldBanner : MonoBehaviour
{
    static WorldBanner instance;
    Text label;
    Coroutine running;

    public static void Show(string message, float seconds = 2.5f)
    {
        if (instance == null) instance = Build();
        instance.Play(message, seconds);
    }

    static WorldBanner Build()
    {
        var root = new GameObject("~WorldBanner",
            typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));

        var canvas = root.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 600;

        var scaler = root.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(800, 1200);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;

        var textGo = new GameObject("Label", typeof(Text), typeof(Outline));
        textGo.transform.SetParent(root.transform, false);

        var text = textGo.GetComponent<Text>();
        text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        text.fontSize = 62;
        text.fontStyle = FontStyle.Bold;
        text.alignment = TextAnchor.MiddleCenter;
        text.color = Color.white;
        text.raycastTarget = false;

        var outline = textGo.GetComponent<Outline>();
        outline.effectColor = new Color(0f, 0f, 0f, 0.85f);
        outline.effectDistance = new Vector2(3, -3);

        var rt = textGo.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(760, 160);
        rt.anchoredPosition = new Vector2(0, 170);

        var banner = root.AddComponent<WorldBanner>();
        banner.label = text;
        text.text = "";
        return banner;
    }

    void Play(string message, float seconds)
    {
        if (running != null) StopCoroutine(running);
        running = StartCoroutine(Fade(message, seconds));
    }

    // Unscaled time throughout: the banner must still animate while the world
    // is frozen at timeScale 0.
    IEnumerator Fade(string message, float seconds)
    {
        label.text = message;

        const float fade = 0.45f;
        for (float t = 0; t < fade; t += Time.unscaledDeltaTime)
        {
            SetAlpha(t / fade);
            yield return null;
        }
        SetAlpha(1f);

        yield return new WaitForSecondsRealtime(seconds);

        for (float t = 0; t < fade; t += Time.unscaledDeltaTime)
        {
            SetAlpha(1f - t / fade);
            yield return null;
        }
        SetAlpha(0f);
        label.text = "";
        running = null;
    }

    void SetAlpha(float a)
    {
        var c = label.color; c.a = Mathf.Clamp01(a); label.color = c;
        var o = label.GetComponent<Outline>();
        if (o != null)
        {
            var oc = o.effectColor; oc.a = Mathf.Clamp01(a) * 0.85f; o.effectColor = oc;
        }
    }
}
