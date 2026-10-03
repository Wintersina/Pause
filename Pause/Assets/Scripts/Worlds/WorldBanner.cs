using System.Collections;
using UnityEngine;
using UnityEngine.UI;

// Transient centre-screen announcement ("FROST", "PORTAL OPEN").
// Self-building so it needs no scene wiring.
//
// Styled as an Akira title card (docs/art-style.md): BONE type with an INK
// outline over the red title slab (DeathPanel/dp_slab), with flipbook timing
// rather than a fade: the slab snaps in with an overshoot, the word pops in a
// tick later, holds, and the card wipes off. Every pose is held on 24 fps
// ticks. Unscaled time throughout: the banner must still animate while the
// world is frozen at timeScale 0.
public class WorldBanner : MonoBehaviour
{
    static WorldBanner instance;
    Text label;
    Image slab;
    RectTransform card;
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

        var cardGo = new GameObject("Card", typeof(RectTransform));
        cardGo.transform.SetParent(root.transform, false);
        var cardRt = (RectTransform)cardGo.transform;
        cardRt.anchorMin = cardRt.anchorMax = cardRt.pivot = new Vector2(0.5f, 0.5f);
        cardRt.sizeDelta = new Vector2(640, 110);
        cardRt.anchoredPosition = new Vector2(0, 170);

        var slabGo = new GameObject("Slab", typeof(Image));
        slabGo.transform.SetParent(cardRt, false);
        var slabImage = slabGo.GetComponent<Image>();
        slabImage.sprite = Resources.Load<Sprite>("DeathPanel/dp_slab");
        slabImage.color = slabImage.sprite != null ? Color.white : AkiraPalette.Red;
        slabImage.raycastTarget = false;
        var slabRt = (RectTransform)slabGo.transform;
        slabRt.anchorMin = slabRt.anchorMax = slabRt.pivot = new Vector2(0.5f, 0.5f);
        slabRt.sizeDelta = new Vector2(640, 88);

        var textGo = new GameObject("Label", typeof(Text), typeof(Outline), typeof(Shadow));
        textGo.transform.SetParent(cardRt, false);

        var text = textGo.GetComponent<Text>();
        text.font = OrbitronOrBuiltin();
        text.fontSize = 56;
        text.fontStyle = FontStyle.BoldAndItalic;
        text.alignment = TextAnchor.MiddleCenter;
        text.color = AkiraPalette.Bone;
        text.raycastTarget = false;
        text.horizontalOverflow = HorizontalWrapMode.Overflow;

        var outline = textGo.GetComponent<Outline>();
        outline.effectColor = AkiraPalette.Ink;
        outline.effectDistance = new Vector2(3, -3);
        // A hard ink cel drop under the outline, like the guide's UI type.
        var shadow = textGo.GetComponent<Shadow>();
        shadow.effectColor = AkiraPalette.Ink;
        shadow.effectDistance = new Vector2(5, -6);

        var rt = textGo.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(760, 110);
        rt.anchoredPosition = new Vector2(0, -2);

        var banner = root.AddComponent<WorldBanner>();
        banner.label = text;
        banner.slab = slabImage;
        banner.card = cardRt;
        text.text = "";
        cardGo.SetActive(false);
        return banner;
    }

    // The scenes use Orbitron Bold (the guide's UI face); borrow it from any
    // Text already loaded, else fall back to the built-in font.
    static Font OrbitronOrBuiltin()
    {
        foreach (var f in Resources.FindObjectsOfTypeAll<Font>())
            if (f != null && f.name.StartsWith("Orbitron")) return f;
        return Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
    }

    void Play(string message, float seconds)
    {
        if (running != null) StopCoroutine(running);
        running = StartCoroutine(Run(message, seconds));
    }

    // (slab scale x, slab x offset, text scale, hold ticks)
    static readonly Vector4[] Intro =
    {
        new Vector4(.35f, -120f, 0f, 1),
        new Vector4(1.12f, 12f, 0f, 1),     // overshoot
        new Vector4(.97f, 0f, 1.35f, 2),    // the word pops big
        new Vector4(1f, 0f, .94f, 2),
        new Vector4(1f, 0f, 1f, 0),
    };
    static readonly Vector4[] Outro =
    {
        new Vector4(1.04f, -8f, 1.06f, 1),  // anticipation: lean back
        new Vector4(.7f, 140f, 1f, 1),      // smear off to the right
        new Vector4(.25f, 320f, 0f, 1),
    };

    IEnumerator Run(string message, float seconds)
    {
        label.text = message;
        card.gameObject.SetActive(true);
        // A short card fits its word: the slab is never narrower than the text.
        float width = Mathf.Max(420f, label.preferredWidth + 140f);
        slab.rectTransform.sizeDelta = new Vector2(width, 88);

        yield return Poses(Intro);
        yield return new WaitForSecondsRealtime(seconds);
        yield return Poses(Outro);

        label.text = "";
        card.gameObject.SetActive(false);
        running = null;
    }

    IEnumerator Poses(Vector4[] poses)
    {
        foreach (var p in poses)
        {
            Apply(p);
            float hold = p.w / 24f;
            for (float t = 0; t < hold; t += Time.unscaledDeltaTime) yield return null;
        }
    }

    void Apply(Vector4 p)
    {
        var s = slab.rectTransform;
        s.localScale = new Vector3(p.x, 1f, 1f);
        s.anchoredPosition = new Vector2(p.y, 0f);
        label.rectTransform.localScale = new Vector3(p.z, p.z, 1f);
        label.rectTransform.anchoredPosition = new Vector2(p.y * .5f, -2f);
    }
}
