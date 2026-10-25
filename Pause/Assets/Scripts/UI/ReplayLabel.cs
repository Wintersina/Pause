using UnityEngine;
using UnityEngine.UI;

// The REPLAY label of the kill cam and the codex's last-stand viewer: the
// same lettering as the in-run PAUSED caption (PausedLabel) -- the PAUSE
// logo's red gradient fill, thin white contour, muted-red drop shadow knocked
// down-right and soft red halo, set in the game's UI face -- built on a
// screen-space UI instead of a world one. It pops in the way PAUSED does
// (squash, stretch, rest on PausedOverlayAnim.Tick) and then holds still.
// Code-drawn, no baked art; nothing here is raycastable.
public sealed class ReplayLabel
{
    public const string Caption = "REPLAY";
    const float CapRatio = .72f;
    static readonly Vector2 SquashPose = new Vector2(1.18f, .78f);
    static readonly Vector2 StretchPose = new Vector2(.9f, 1.16f);

    public RectTransform Root { get; private set; }
    public Text Text { get; private set; }
    RectTransform body;
    Texture2D haloTex;
    Sprite haloSprite;
    int pose = -1;

    // Builds the label under `parent` (centred, sized to its lettering; the caller places it).
    public static ReplayLabel Build(Transform parent, int fontSize)
    {
        var l = new ReplayLabel();
        float k = fontSize / 64f;   // PausedLabel's metrics are for a 64 pt face

        var rootGo = new GameObject("ReplayLabel", typeof(RectTransform));
        rootGo.transform.SetParent(parent, false);
        l.Root = (RectTransform)rootGo.transform;
        var bodyGo = new GameObject("Body", typeof(RectTransform));
        bodyGo.transform.SetParent(l.Root, false);
        l.body = (RectTransform)bodyGo.transform;

        var haloGo = new GameObject("Halo", typeof(RectTransform), typeof(Image));
        haloGo.transform.SetParent(l.body, false);
        var haloImage = haloGo.GetComponent<Image>();
        l.haloTex = PausedLabel.BuildHaloTexture();
        l.haloSprite = Sprite.Create(l.haloTex, new Rect(0, 0, l.haloTex.width, l.haloTex.height), new Vector2(.5f, .5f));
        haloImage.sprite = l.haloSprite;
        haloImage.color = PausedLabel.Halo;
        haloImage.raycastTarget = false;

        var textGo = new GameObject("Text", typeof(RectTransform), typeof(Text));
        textGo.transform.SetParent(l.body, false);
        var text = textGo.GetComponent<Text>();
        l.Text = text;
        text.font = WorldBanner.OrbitronOrBuiltin();
        text.fontSize = fontSize;
        text.fontStyle = FontStyle.Normal;
        text.alignment = TextAnchor.MiddleCenter;
        text.horizontalOverflow = HorizontalWrapMode.Overflow;
        text.verticalOverflow = VerticalWrapMode.Overflow;
        text.supportRichText = false;
        text.raycastTarget = false;
        text.color = Color.white;
        text.text = Caption;
        // same effect order as PausedLabel: fill, contour, then shadow
        textGo.AddComponent<PausedLabelGradient>().Set(PausedLabel.FillTop, PausedLabel.FillBottom);
        var contour = textGo.AddComponent<Outline>();
        contour.effectColor = PausedLabel.Contour;
        contour.effectDistance = new Vector2(2.2f, 2.2f) * k;
        var shadow = textGo.AddComponent<Shadow>();
        shadow.effectColor = PausedLabel.DropShadow;
        shadow.effectDistance = new Vector2(4f, -5f) * k;

        float w = text.preferredWidth + 10f * k;
        float h = fontSize * CapRatio + 14f * k;
        l.Root.sizeDelta = new Vector2(w, h);
        Centre(l.body, new Vector2(w, h));
        Centre((RectTransform)textGo.transform, new Vector2(w, h));
        Centre((RectTransform)haloGo.transform, new Vector2(w * 1.35f, h * 2.1f));
        l.Pop(0f);
        return l;
    }

    static void Centre(RectTransform rt, Vector2 size)
    {
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(.5f, .5f);
        rt.sizeDelta = size;
        rt.anchoredPosition = Vector2.zero;
    }

    // `seconds` since the label appeared (real time): squash, stretch, rest.
    public void Pop(float seconds)
    {
        int p = seconds < PausedOverlayAnim.Tick ? 0 : seconds < 2f * PausedOverlayAnim.Tick ? 1 : 2;
        if (p == pose) return;
        pose = p;
        Vector2 s = p == 0 ? SquashPose : p == 1 ? StretchPose : Vector2.one;
        body.localScale = new Vector3(s.x, s.y, 1f);
    }

    public int PopStep { get { return pose; } }

    public void Destroy()
    {
        Kill(haloSprite);
        Kill(haloTex);
        if (Root != null) Kill(Root.gameObject);
    }

    static void Kill(Object o)
    {
        if (o == null) return;
        if (Application.isPlaying) Object.Destroy(o); else Object.DestroyImmediate(o);
    }
}
