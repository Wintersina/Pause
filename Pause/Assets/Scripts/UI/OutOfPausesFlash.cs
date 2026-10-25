using UnityEngine;
using UnityEngine.UI;

// The last pause was just spent: one amber glow around the screen edges that
// ramps up fast and eases out (~0.4 s), so the player notices at 30x speed
// that the world no longer freezes when the finger lifts.
//
// Trigger: score.pauseCounterFunction calls OnPauseSpent(before, after) on the
// one place a pause is consumed. It fires only on the 1 -> 0 transition of a
// real run (score.paysRealDust: never the tutorial or practice), never after
// death or while the kill cam replays. Gaining pauses back (incromentPause,
// the tutorial's top-ups) never goes through it, so a revive or refill cannot
// re-fire it; reaching 1 -> 0 again after a refill is a new transition.
//
// Drawn in a screen-space overlay canvas under the critical UI, on unscaled
// time (independent of Time.timeScale and the game speed). It is an image, not
// a text element, and takes no touches. The haptic tick is skipped: the death
// vibration is a native call per platform and a new iOS plugin is not trivial.
public class OutOfPausesFlash : MonoBehaviour
{
    public const string ObjectName = "~OutOfPausesFlash";
    // The warning amber the dev badge uses.
    public static readonly Color Amber = new Color(1f, 0.79f, 0.26f, 1f);
    public const float PeakAlpha = 0.85f;
    public const float RampUpSeconds = 0.06f;
    public const float EaseOutSeconds = 0.34f;
    public const float TotalSeconds = RampUpSeconds + EaseOutSeconds;
    // How deep the glow reaches in from each edge, as a share of the screen.
    public const float EdgeDepth = 0.2f;
    // Under the pause quick actions (90) and the death/results UI.
    public const int SortingOrder = 80;

    static Texture2D glow;

    // Pure: the fire rule. A real run's last pause was spent: before >= 1, after <= 0.
    public static bool ShouldFire(int before, int after, bool realRun, bool dead, bool killCamPlaying)
    {
        return realRun && !dead && !killCamPlaying && before >= 1 && after <= 0;
    }

    // Pure: opacity (0..1 of PeakAlpha) at t seconds into the flash.
    public static float Intensity(float t)
    {
        if (t <= 0f || t >= TotalSeconds) return 0f;
        if (t < RampUpSeconds) return t / RampUpSeconds;
        float k = 1f - (t - RampUpSeconds) / EaseOutSeconds;
        return k * k;
    }

    // Pure: the glow's alpha at a point of the screen (u, v in 0..1).
    public static float EdgeAlpha(float u, float v)
    {
        float d = Mathf.Min(Mathf.Min(u, 1f - u), Mathf.Min(v, 1f - v));
        if (d >= EdgeDepth) return 0f;
        float k = 1f - d / EdgeDepth;
        return k * k;
    }

    // Called by score when a pause is spent.
    public static void OnPauseSpent(int before, int after)
    {
        if (!ShouldFire(before, after, score.paysRealDust, buttonClicks.playerDied, KillCam.Playing)) return;
        Fire();
    }

    static void Fire()
    {
        var old = GameObject.Find(ObjectName);
        if (old != null) Destroy(old);   // never two at once
        var go = new GameObject(ObjectName);
        go.AddComponent<OutOfPausesFlash>();
    }

    static Texture2D Glow()
    {
        if (glow != null) return glow;
        const int n = 64;
        glow = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
        var px = new Color[n * n];
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
                px[y * n + x] = new Color(Amber.r, Amber.g, Amber.b, EdgeAlpha((x + 0.5f) / n, (y + 0.5f) / n));
        glow.SetPixels(px);
        glow.Apply(false, false);
        return glow;
    }

    RawImage image;
    float elapsed;

    void Awake()
    {
        var canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = SortingOrder;
        var img = new GameObject("Glow", typeof(RectTransform));
        img.transform.SetParent(transform, false);
        image = img.AddComponent<RawImage>();
        image.texture = Glow();
        image.raycastTarget = false;
        var rt = image.rectTransform;
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = rt.offsetMax = Vector2.zero;
        Apply();
    }

    void Update()
    {
        elapsed += Time.unscaledDeltaTime;
        if (elapsed >= TotalSeconds) { Destroy(gameObject); return; }
        Apply();
    }

    void Apply()
    {
        var c = Color.white;
        c.a = PeakAlpha * Intensity(elapsed);
        image.color = c;
    }
}
