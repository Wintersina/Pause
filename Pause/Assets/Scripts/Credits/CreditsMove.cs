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

    // After the first pass the roll bounces like a screensaver logo: it rises until the
    // top of the block meets the top of the screen, turns round, and from then on every
    // edge hit picks a fresh heading (within BounceSpread degrees of straight back into
    // the screen) at the same speed.
    public const float BounceSpread = 55f;   // degrees either side of the inward normal

    RectTransform rect;
    Vector2 start;
    Vector2 pos;
    Vector2 velocity;
    bool started;
    bool drifting;
    float time;
    System.Random rng = new System.Random();
    GateOpenPlayer gate;

    public float Elapsed { get { return time; } }
    public int GateFrame { get { return gate != null ? gate.Frame : -1; } }
    public bool Drifting { get { return drifting; } }
    public Vector2 Velocity { get { return velocity; } }
    public void Seed(int seed) { rng = new System.Random(seed); }

    public GateOpenPlayer Gate { get { return gate; } }

    void Awake() { EnsureLockup(); }

    // Reopening the screen starts the roll from its authored spot again.
    void OnEnable()
    {
        if (!started) return;
        rect.anchoredPosition = start;
        started = false;
        drifting = false;
        time = 0f;
        velocity = Vector2.zero;
        if (gate != null) gate.Play();
    }

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

    // The range of anchoredPosition values that keeps the block (logo + text) inside the
    // visible area (canvas minus the device safe area). A block taller than the screen
    // swaps the vertical ends so it still spans the screen: lo.y puts its top edge on the
    // screen top, hi.y its bottom edge on the screen bottom. A block wider than the
    // screen is pinned at the centre.
    public void GetLimits(out Vector2 lo, out Vector2 hi)
    {
        if (rect == null) rect = transform as RectTransform;
        var parent = rect.parent as RectTransform;
        Rect area = parent != null ? parent.rect : new Rect(-400f, -711f, 800f, 1422f);
        Vector2 center = area.center;
        float left = area.xMin - center.x, right = area.xMax - center.x;
        float bottom = area.yMin - center.y, top = area.yMax - center.y;
        if (Screen.width > 0 && Screen.height > 0)
        {
            Rect safe = Screen.safeArea;
            float ux = area.width / Screen.width, uy = area.height / Screen.height;
            left += safe.xMin * ux;
            right -= (Screen.width - safe.xMax) * ux;
            bottom += safe.yMin * uy;
            top -= (Screen.height - safe.yMax) * uy;
        }
        float logo = 0f;
        var lockup = transform.Find(LockupName) as RectTransform;
        if (lockup != null) logo = lockup.anchoredPosition.y + lockup.rect.height;
        float up = rect.rect.height * (1f - rect.pivot.y) + logo;   // block top above the anchor
        float down = rect.rect.height * rect.pivot.y;               // block bottom below the anchor
        float halfW = Mathf.Max(rect.rect.width, lockup != null ? lockup.rect.width : 0f) * 0.5f;
        float a = bottom + down, b = top - up;
        lo = new Vector2(left + halfW, Mathf.Min(a, b));
        hi = new Vector2(right - halfW, Mathf.Max(a, b));
        if (hi.x < lo.x) lo.x = hi.x = 0f;
    }

    // A new heading of length Speed, within BounceSpread degrees of `inward`.
    Vector2 Heading(Vector2 inward)
    {
        float angle = ((float)rng.NextDouble() * 2f - 1f) * BounceSpread * Mathf.Deg2Rad;
        float c = Mathf.Cos(angle), s = Mathf.Sin(angle);
        return new Vector2(inward.x * c - inward.y * s, inward.x * s + inward.y * c) * Speed;
    }

    // Advances the roll and the logo animation by dt seconds.
    public void Step(float dt)
    {
        if (rect == null) rect = transform as RectTransform;
        if (!started) { start = rect.anchoredPosition; pos = start; started = true; }
        time += dt;
        Vector2 lo, hi;
        GetLimits(out lo, out hi);
        if (!drifting)
        {
            pos = start + new Vector2(0f, ScrollDistance(time));
            if (pos.y >= hi.y)
            {
                pos.y = hi.y;
                drifting = true;
                velocity = new Vector2(0f, -Speed);
            }
        }
        else
        {
            pos += velocity * dt;
            bool roomX = hi.x > lo.x, roomY = hi.y > lo.y;
            if (roomX && pos.x >= hi.x) { pos.x = hi.x; velocity = Heading(Vector2.left); }
            else if (roomX && pos.x <= lo.x) { pos.x = lo.x; velocity = Heading(Vector2.right); }
            if (roomY && pos.y >= hi.y) { pos.y = hi.y; velocity = Heading(Vector2.down); }
            else if (roomY && pos.y <= lo.y) { pos.y = lo.y; velocity = Heading(Vector2.up); }
            if (!roomX) { pos.x = lo.x; velocity.x = 0f; }
            pos.x = Mathf.Clamp(pos.x, lo.x, hi.x);
            pos.y = Mathf.Clamp(pos.y, lo.y, hi.y);
        }
        rect.anchoredPosition = pos;
        if (gate != null) gate.Step(dt);
    }

    void Update () {
        // Clamped: the first frame after the scene load can carry the load hitch,
        // which would otherwise skip the start of the gate animation.
        Step(Mathf.Min(Time.unscaledDeltaTime, 0.05f));
    }
}
