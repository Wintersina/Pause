using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// Makes an Options slider's handle read as grabbable: a crisp pixel-art ring (hard 2-unit pixels, stair-stepped
// corners, no glow or bloom) around the chunky handle, bright cyan at rest. While the finger is down the ring and
// handle turn bone-white and the handle swells a little (pressed). Lives on the slider's root next to the Slider,
// which already receives the pointer events (its invisible hit image is the target).
public class OptionsSliderHandle : MonoBehaviour, IPointerDownHandler, IPointerUpHandler
{
    public const string RingName = "HandleRing";
    public const float Grow = 4f, PressedScale = 1.12f;
    const int RingW = 16, RingH = 22;   // texture pixels; 1 px = 2 canvas units

    static Sprite ringSprite;

    public Image handle, ring;
    public bool Pressed { get; private set; }
    public bool Dim { get; set; }

    public static OptionsSliderHandle Attach(Slider slider, Image handle)
    {
        var rt = new GameObject(RingName, typeof(RectTransform)).transform as RectTransform;
        rt.SetParent(handle.transform, false);
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = new Vector2(-Grow, -Grow); rt.offsetMax = new Vector2(Grow, Grow);
        var ring = rt.gameObject.AddComponent<Image>();
        ring.sprite = Ring();
        ring.type = Image.Type.Simple;
        ring.raycastTarget = false;
        var h = slider.gameObject.AddComponent<OptionsSliderHandle>();
        h.handle = handle;
        h.ring = ring;
        h.Refresh();
        return h;
    }

    public void OnPointerDown(PointerEventData e) { Pressed = true; Refresh(); }
    public void OnPointerUp(PointerEventData e) { Pressed = false; Refresh(); }
    void OnDisable() { Pressed = false; Refresh(); }

    // Call after changing the handle's tint (a muted bus dims the whole slider).
    public void Refresh()
    {
        if (handle == null || ring == null) return;
        var tint = Dim ? new Color(.55f, .58f, .72f, .85f) : Color.white;
        handle.color = Pressed ? Color.Lerp(tint, AkiraPalette.Bone, .8f) : tint;
        ring.color = Dim ? new Color(.55f, .58f, .72f, .6f) : (Pressed ? AkiraPalette.Bone : AkiraPalette.WithAlpha(AkiraPalette.Cyan, .9f));
        handle.rectTransform.localScale = Pressed ? Vector3.one * PressedScale : Vector3.one;
    }

    static Sprite Ring()
    {
        if (ringSprite != null) return ringSprite;
        var px = new Color32[RingW * RingH];
        for (int y = 0; y < RingH; y++)
            for (int x = 0; x < RingW; x++)
            {
                int dx = Mathf.Min(x, RingW - 1 - x), dy = Mathf.Min(y, RingH - 1 - y);
                bool border = dx == 0 || dy == 0;
                bool corner = dx + dy < 2;   // clipped corners
                bool on = border && !corner || (dx == 1 && dy == 1);
                px[y * RingW + x] = on ? new Color32(255, 255, 255, 255) : new Color32(0, 0, 0, 0);
            }
        var tex = new Texture2D(RingW, RingH, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, name = "OptionsHandleRing" };
        tex.SetPixels32(px);
        tex.Apply(false, true);
        ringSprite = Sprite.Create(tex, new Rect(0, 0, RingW, RingH), new Vector2(.5f, .5f), 50f);
        return ringSprite;
    }
}
