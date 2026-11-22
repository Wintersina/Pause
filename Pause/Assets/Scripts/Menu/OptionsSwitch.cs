using UnityEngine;
using UnityEngine.UI;

// The little pixel-art switch drawn at the right end of an Options toggle row (VIBRATION, UPGRADE OFFERS), so the
// row reads as a tappable control rather than plain text: a chunky pill that is cyan with the knob on the right
// when ON, and dim with the knob on the left when OFF. The knob slides when the state flips. Drawn procedurally
// with hard pixels (point filtered, no outline box), so there is no art file. The row itself already carries the
// CelPress squash (DeveloperOptions.Clone), so a tap dips the whole row, switch included.
public class OptionsSwitch : MonoBehaviour
{
    public const string SwitchName = "OnOffSwitch", KnobName = "Knob";
    public const float Width = 84f, Height = 44f, RightPad = 22f, LabelGap = 14f;
    const int TexW = 42, TexH = 22, KnobPx = 14;
    const float KnobTravel = 20f;   // knob centre sits at +-KnobTravel from the pill's centre
    const float Speed = 480f;       // canvas units per second

    static Sprite pillSprite, knobSprite;

    public Image track, knob;
    public bool On { get; private set; }
    float knobX;

    // Adds the switch to a cloned row and keeps the row's label clear of it. Returns the switch.
    public static OptionsSwitch Attach(GameObject row, Text label, bool on)
    {
        var root = new GameObject(SwitchName, typeof(RectTransform));
        var rt = (RectTransform)root.transform;
        rt.SetParent(row.transform, false);
        rt.anchorMin = rt.anchorMax = new Vector2(1f, .5f);
        rt.pivot = new Vector2(1f, .5f);
        rt.anchoredPosition = new Vector2(-RightPad, 0f);
        rt.sizeDelta = new Vector2(Width, Height);
        var sw = root.AddComponent<OptionsSwitch>();
        sw.track = root.AddComponent<Image>();
        sw.track.sprite = Pill();
        sw.track.raycastTarget = false;

        var k = new GameObject(KnobName, typeof(RectTransform));
        var krt = (RectTransform)k.transform;
        krt.SetParent(rt, false);
        krt.anchorMin = krt.anchorMax = krt.pivot = new Vector2(.5f, .5f);
        krt.sizeDelta = new Vector2(KnobPx * 2f, KnobPx * 2f);
        sw.knob = k.AddComponent<Image>();
        sw.knob.sprite = Knob();
        sw.knob.raycastTarget = false;

        if (label != null && label.gameObject != row)
        {
            // Keep the caption inside the part of the row left of the switch.
            var lrt = label.rectTransform;
            lrt.offsetMax = new Vector2(-(RightPad + Width + LabelGap), lrt.offsetMax.y);
        }
        sw.Set(on, false);
        return sw;
    }

    public static OptionsSwitch Find(GameObject row)
    {
        return row == null ? null : row.GetComponentInChildren<OptionsSwitch>(true);
    }

    public static float KnobTarget(bool on) { return on ? KnobTravel : -KnobTravel; }

    public void Set(bool on, bool animate = true)
    {
        On = on;
        if (track != null) track.color = on ? AkiraPalette.Cyan : AkiraPalette.WithAlpha(AkiraPalette.Hairline, .9f);
        if (knob != null) knob.color = on ? AkiraPalette.Bone : AkiraPalette.Muted;
        if (!animate) knobX = KnobTarget(on);
        Place();
    }

    public float KnobX { get { return knobX; } }

    void Update()
    {
        float target = KnobTarget(On);
        if (Mathf.Approximately(knobX, target)) return;
        knobX = Mathf.MoveTowards(knobX, target, Speed * Time.unscaledDeltaTime);
        Place();
    }

    void Place()
    {
        if (knob != null) ((RectTransform)knob.transform).anchoredPosition = new Vector2(knobX, 0f);
    }

    // ---- procedural pixel art ----------------------------------------------------------------
    static Sprite Pill()
    {
        if (pillSprite != null) return pillSprite;
        var px = new Color32[TexW * TexH];
        for (int y = 0; y < TexH; y++)
            for (int x = 0; x < TexW; x++)
            {
                // stair-stepped rounded ends: cut 3 / 2 / 1 / 1 pixels off the corners
                int dx = Mathf.Min(x, TexW - 1 - x), dy = Mathf.Min(y, TexH - 1 - y);
                int cut = dy == 0 ? 4 : (dy == 1 ? 2 : (dy == 2 ? 1 : 0));
                bool inside = dx >= cut;
                // a one-pixel darker rim along the bottom edge gives it a chunky key look
                byte shade = (byte)(y <= 1 ? 190 : 255);
                px[y * TexW + x] = inside ? new Color32(shade, shade, shade, 255) : new Color32(0, 0, 0, 0);
            }
        pillSprite = Make(TexW, TexH, px, "OptionsSwitchPill");
        return pillSprite;
    }

    static Sprite Knob()
    {
        if (knobSprite != null) return knobSprite;
        var px = new Color32[KnobPx * KnobPx];
        for (int y = 0; y < KnobPx; y++)
            for (int x = 0; x < KnobPx; x++)
            {
                int dx = Mathf.Min(x, KnobPx - 1 - x), dy = Mathf.Min(y, KnobPx - 1 - y);
                bool corner = dx + dy < 2;   // clipped corners
                byte shade = (byte)(y <= 1 ? 200 : 255);
                px[y * KnobPx + x] = corner ? new Color32(0, 0, 0, 0) : new Color32(shade, shade, shade, 255);
            }
        knobSprite = Make(KnobPx, KnobPx, px, "OptionsSwitchKnob");
        return knobSprite;
    }

    static Sprite Make(int w, int h, Color32[] px, string name)
    {
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, name = name };
        tex.SetPixels32(px);
        tex.Apply(false, true);
        // 1 texture pixel = 2 canvas units (Width / TexW)
        return Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(.5f, .5f), 50f);
    }
}
