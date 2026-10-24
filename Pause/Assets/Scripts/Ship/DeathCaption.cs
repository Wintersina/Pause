using UnityEngine;
using UnityEngine.UI;

// "YOU DIED BY <ENEMY>", shown over the death crash (DeathCrash): the crash
// is already the slow-motion beat (the board drops to DeathCrashDomino.SlowMo
// on the sequence's own unscaled clock, the killer tumbling into its rail), so
// the caption just names the killer while it plays. Time.timeScale is never
// touched: nothing here can leave the game slowed down, and the sequence's
// own skip (a tap) and teardown (Finish / Cancel / Stop / scene change) hide
// it. One screen-space canvas, built once and reused; showing it allocates
// only the caption string.
public class DeathCaption : MonoBehaviour
{
    public const float FadeIn = .18f;
    // Held back this long after the fatal hit, so the blast lands first.
    public const float Delay = .12f;

    const string Lead = "YOU DIED BY";

    Canvas canvas;
    CanvasGroup group;
    RectTransform block;
    Text lead, nameText;

    public bool Visible { get { return canvas != null && canvas.enabled; } }
    public string NameText { get { return nameText != null ? nameText.text : null; } }

    public static DeathCaption Create(Transform parent)
    {
        var go = new GameObject("~DeathCaption");
        go.transform.SetParent(parent, false);
        var caption = go.AddComponent<DeathCaption>();
        caption.Build();
        return caption;
    }

    void Build()
    {
        canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 400;
        var scaler = gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(800f, 1422f);
        scaler.matchWidthOrHeight = 0f;
        group = gameObject.AddComponent<CanvasGroup>();
        group.interactable = false;
        group.blocksRaycasts = false;   // a tap still reaches the skip

        var blockGo = new GameObject("Block", typeof(RectTransform));
        blockGo.transform.SetParent(transform, false);
        block = (RectTransform)blockGo.transform;
        block.anchorMin = block.anchorMax = new Vector2(.5f, .66f);
        block.sizeDelta = new Vector2(800f, 150f);

        // a soft ink band behind the words, so they read over any backdrop
        var band = new GameObject("Band", typeof(RectTransform), typeof(Image));
        band.transform.SetParent(block, false);
        var bandRt = (RectTransform)band.transform;
        bandRt.anchorMin = Vector2.zero; bandRt.anchorMax = Vector2.one;
        bandRt.offsetMin = bandRt.offsetMax = Vector2.zero;
        var img = band.GetComponent<Image>();
        img.color = AkiraPalette.WithAlpha(AkiraPalette.Ink, .6f);
        img.raycastTarget = false;

        lead = NewText("Lead", 30, AkiraPalette.Red, new Vector2(0f, 38f), 44f);
        nameText = NewText("Name", 58, AkiraPalette.Bone, new Vector2(0f, -22f), 80f);
        nameText.fontStyle = FontStyle.Bold;
        lead.text = Lead;
        canvas.enabled = false;
    }

    Text NewText(string label, int size, Color color, Vector2 at, float height)
    {
        var go = new GameObject(label, typeof(RectTransform), typeof(Text));
        go.transform.SetParent(block, false);
        var rt = (RectTransform)go.transform;
        rt.anchorMin = rt.anchorMax = new Vector2(.5f, .5f);
        rt.sizeDelta = new Vector2(760f, height);
        rt.anchoredPosition = at;
        var t = go.GetComponent<Text>();
        t.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        t.fontSize = size;
        t.color = color;
        t.alignment = TextAnchor.MiddleCenter;
        t.horizontalOverflow = HorizontalWrapMode.Wrap;
        t.verticalOverflow = VerticalWrapMode.Overflow;
        t.resizeTextForBestFit = true;
        t.resizeTextMinSize = 24;
        t.resizeTextMaxSize = size;
        t.raycastTarget = false;
        return t;
    }

    // Names the killer. A null / empty name shows nothing (an unknown killer is not faked).
    public void Show(string killerName)
    {
        if (string.IsNullOrEmpty(killerName)) { Hide(); return; }
        nameText.text = killerName.ToUpperInvariant();
        group.alpha = 0f;
        block.localScale = Vector3.one * 1.12f;
        canvas.enabled = true;
    }

    public void Hide()
    {
        if (canvas != null) canvas.enabled = false;
    }

    // `elapsed`: seconds of the crash sequence's clock since the fatal hit.
    public void Step(float elapsed)
    {
        if (!Visible) return;
        float u = Mathf.Clamp01((elapsed - Delay) / FadeIn);
        group.alpha = u;
        block.localScale = Vector3.one * Mathf.Lerp(1.12f, 1f, u);
    }

    void OnDestroy()
    {
        if (canvas != null) canvas.enabled = false;
    }
}
