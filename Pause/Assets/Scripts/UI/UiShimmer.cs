using UnityEngine;
using UnityEngine.UI;

// Idle shimmer for a UI Image: every so often it flips through a few
// pre-drawn glint frames (one tick each, on 2s at the ends) and comes back to
// its own sprite. Unscaled time: it runs on the frozen pause screen too.
public class UiShimmer : MonoBehaviour
{
    public Sprite[] frames;
    public float interval = 3.2f;
    public float phase;

    Image image;
    Sprite rest;
    float clock;
    int frame = -1;
    float frameClock;

    public static UiShimmer AddTo(Image image, Sprite[] frames, float interval, float phase = 0f)
    {
        if (image == null || frames == null || frames.Length == 0 || frames[0] == null) return null;
        var s = image.GetComponent<UiShimmer>();
        if (s == null) s = image.gameObject.AddComponent<UiShimmer>();
        s.frames = frames;
        s.interval = interval;
        s.phase = phase;
        s.clock = -phase;
        return s;
    }

    void Awake() { image = GetComponent<Image>(); }

    void Update()
    {
        if (image == null || frames == null) return;
        float dt = Time.unscaledDeltaTime;
        if (frame < 0)
        {
            clock += dt;
            if (clock < interval) return;
            clock = 0f;
            rest = image.sprite;
            frame = 0;
            frameClock = 0f;
            image.sprite = frames[0];
            return;
        }
        frameClock += dt;
        float hold = (frame == 0 || frame == frames.Length - 1 ? 2f : 1f) / 24f;
        if (frameClock < hold) return;
        frameClock = 0f;
        frame++;
        if (frame >= frames.Length)
        {
            frame = -1;
            image.sprite = rest;
            return;
        }
        image.sprite = frames[frame];
    }

    void OnDisable()
    {
        if (frame >= 0 && image != null && rest != null) image.sprite = rest;
        frame = -1;
    }
}
