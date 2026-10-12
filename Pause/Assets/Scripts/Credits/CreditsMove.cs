using UnityEngine;
using UnityEngine.UI;

public class CreditsMove : MonoBehaviour {

    // The HapticGate logo heads the roll: a child of the scrolling text that
    // sits just above its top edge, so it rises and leaves with the words. It
    // plays the gate-opening animation (GateOpenPlayer) once as the screen
    // opens and then holds the last frame (open gate + wordmark). Without the
    // animation frames it falls back to the static lockup.
    public const string LockupName = "HapticGate lockup";
    public const float LockupWidth = 360f;   // canvas units (square frames)

    // The roll is on screen from the first frame (the logo and "Sina Serati"
    // sit in the middle of the screen) and starts rising at once: no lead-in,
    // the speed ramps up over the first EaseIn seconds.
    public const float LeadIn = 0f;
    public const float EaseIn = 0.4f;
    public const float Speed = 45f;          // canvas units per second

    RectTransform rect;
    Vector2 start;
    bool started;
    float time;
    GateOpenPlayer gate;

    public float Elapsed { get { return time; } }
    public int GateFrame { get { return gate != null ? gate.Frame : -1; } }
    public GateOpenPlayer Gate { get { return gate; } }

    void Awake() { EnsureLockup(); }

    // How far the roll has risen (canvas units) `t` seconds after the screen opened.
    public static float ScrollDistance(float t)
    {
        t -= LeadIn;
        if (t <= 0f) return 0f;
        if (t < EaseIn) return Speed * t * t / (2f * EaseIn);
        return Speed * (t - EaseIn * 0.5f);
    }

    public void EnsureLockup()
    {
        var existing = transform.Find(LockupName);
        if (existing != null)
        {
            if (gate == null) gate = existing.GetComponent<GateOpenPlayer>();
            return;
        }
        bool animated = GateOpenFrames.Load() != null;
        var sprite = animated ? GateOpenFrames.Load()[0] : HapticGateMark.Lockup();
        if (sprite == null) return;
        var go = new GameObject(LockupName, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        var rt = (RectTransform)go.transform;
        rt.SetParent(transform, false);
        float width = animated ? LockupWidth : 300f;
        float aspect = sprite.rect.height / sprite.rect.width;
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 1f);
        rt.pivot = new Vector2(0.5f, 0f);
        rt.sizeDelta = new Vector2(width, width * aspect);
        rt.anchoredPosition = new Vector2(0f, 10f);
        var image = go.GetComponent<Image>();
        image.sprite = sprite;
        image.preserveAspect = true;
        image.raycastTarget = false;
        if (animated)
        {
            gate = GateOpenPlayer.Attach(image);
            gate.selfDriven = false;          // stepped by this roll's clock
            gate.Play();
        }
    }

    // Advances the roll and the logo animation by dt seconds.
    public void Step(float dt)
    {
        if (rect == null) rect = transform as RectTransform;
        if (!started) { start = rect.anchoredPosition; started = true; }
        time += dt;
        rect.anchoredPosition = start + new Vector2(0f, ScrollDistance(time));
        if (gate != null) gate.Step(dt);
    }

    void Update () {
        // Clamped: the first frame after the scene load can carry the load hitch,
        // which would otherwise skip the start of the gate animation.
        Step(Mathf.Min(Time.unscaledDeltaTime, 0.05f));
    }
}
