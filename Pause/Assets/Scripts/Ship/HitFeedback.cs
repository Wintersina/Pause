using UnityEngine;

// The hit that costs a heart but not the run needs to be felt as well as
// seen: a short decaying camera shake and a red flash along the screen
// edges. Not the death crash (DeathCrash owns the fatal hit, so
// collisionDetection only calls this on the non-fatal branch).
//
//   Shake  the gameplay camera is nudged by a fast decaying rattle (sines, no
//          allocation, no UnityEngine.Random). The offset is added and taken
//          back in LateUpdate, so whatever else moves the camera keeps
//          working. The HUD (a screen-space canvas) stays still.
//   Flash  a soft red vignette, clear in the middle so bullets and enemies
//          stay readable, faded out fast. One sprite built once from a
//          generated texture, re-tinted per hit.
//
// A second hit while the first is still playing restarts it (it never stacks
// past one hit's strength). Runs on real time but holds while the world is
// paused (timeScale 0). Off when Enabled is false (hook for a future
// screen-effects / reduced-motion setting; none exists yet).
public class HitFeedback : MonoBehaviour
{
    // ---- tuning ---------------------------------------------------------
    public const float ShakeTime = .26f;        // seconds
    public const float ShakeAmplitude = .14f;   // world units at the first instant (~2% of the view width)
    public const float ShakeHz = 34f;           // rattle frequency
    public const float FlashTime = .38f;        // seconds to fade out
    public const float FlashAlpha = .5f;        // vignette opacity at the first instant
    public const float FlashInner = .55f;       // how far out the middle stays clear (0..1)
    public static readonly Color FlashColor = new Color(1f, .16f, .12f, 1f);
    public const int FlashOrder = 400;          // over the world, under the UI canvas

    // Screen effects on/off. No settings screen drives it yet.
    public static bool Enabled = true;

    const int TexW = 32, TexH = 64;   // the vignette texture; the sprite is 1 x 2 world units

    static HitFeedback instance;
    static Sprite vignetteSprite;

    Camera cam;
    Vector3 applied;        // the offset currently added to the camera
    float shakeLeft, flashLeft;
    float phase;
    SpriteRenderer flash;

    public static bool Active { get { return instance != null && (instance.shakeLeft > 0f || instance.flashLeft > 0f); } }

    // The shake's strength 1 -> 0 over its time (quadratic: a hard start, a quick settle).
    public static float ShakeEnvelope(float left)
    {
        float k = Mathf.Clamp01(left / ShakeTime);
        return k * k;
    }

    // The flash's opacity at `left` seconds remaining.
    public static float FlashOpacity(float left)
    {
        return FlashAlpha * Mathf.Clamp01(left / FlashTime);
    }

    // A heart was lost but the run goes on.
    public static void Play()
    {
        if (!Enabled) return;
        if (instance == null)
        {
            var go = new GameObject("~HitFeedback");
            instance = go.AddComponent<HitFeedback>();
        }
        instance.shakeLeft = ShakeTime;
        instance.flashLeft = FlashTime;
        instance.phase = Time.realtimeSinceStartup * 7.3f;   // a different rattle each hit
    }

    void LateUpdate()
    {
        if (cam == null) cam = Camera.main;
        // take back last frame's nudge before anything else reads the position
        if (cam != null) cam.transform.position -= applied;
        applied = Vector3.zero;

        if (Time.timeScale > 0f)   // held while the world is paused
        {
            float dt = Time.unscaledDeltaTime;
            shakeLeft = Mathf.Max(0f, shakeLeft - dt);
            flashLeft = Mathf.Max(0f, flashLeft - dt);
            if (shakeLeft > 0f && cam != null)
            {
                float t = (ShakeTime - shakeLeft) * ShakeHz * Mathf.PI * 2f + phase;
                float a = ShakeAmplitude * ShakeEnvelope(shakeLeft);
                applied = new Vector3(Mathf.Sin(t) * a, Mathf.Sin(t * 1.31f + 1.7f) * a * .7f, 0f);
            }
        }
        if (cam != null) cam.transform.position += applied;
        UpdateFlash();
    }

    void UpdateFlash()
    {
        if (flashLeft <= 0f || cam == null)
        {
            if (flash != null && flash.enabled) flash.enabled = false;
            return;
        }
        if (flash == null) BuildFlash();
        float halfH = cam.orthographicSize, halfW = halfH * cam.aspect;
        Vector3 c = cam.transform.position - applied;   // the flash stays on the screen, not on the rattle
        var t = flash.transform;
        t.position = new Vector3(c.x, c.y, 0f);
        t.localScale = new Vector3(2f * halfW, halfH, 1f);   // the sprite is 1 x 2 world units
        Color col = FlashColor;
        col.a = FlashOpacity(flashLeft);
        flash.color = col;
        flash.enabled = true;
    }

    void BuildFlash()
    {
        if (vignetteSprite == null) vignetteSprite = MakeVignette();
        var go = new GameObject("Flash");
        go.transform.SetParent(transform, false);
        flash = go.AddComponent<SpriteRenderer>();
        flash.sprite = vignetteSprite;
        flash.sortingLayerName = "Default";
        flash.sortingOrder = FlashOrder;
        flash.enabled = false;
    }

    // White, alpha 0 inside FlashInner and rising smoothly to 1 at the edges. Built once.
    static Sprite MakeVignette()
    {
        var tex = new Texture2D(TexW, TexH, TextureFormat.RGBA32, false);
        tex.wrapMode = TextureWrapMode.Clamp;
        tex.filterMode = FilterMode.Bilinear;
        var px = new Color32[TexW * TexH];
        for (int y = 0; y < TexH; y++)
            for (int x = 0; x < TexW; x++)
            {
                float nx = (x + .5f) / TexW * 2f - 1f, ny = (y + .5f) / TexH * 2f - 1f;
                float d = Mathf.Max(Mathf.Abs(nx), Mathf.Abs(ny)) * .5f + Mathf.Sqrt(nx * nx + ny * ny) * .5f / 1.2f;
                float a = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(FlashInner, 1f, d));
                px[y * TexW + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(a * 255f));
            }
        tex.SetPixels32(px);
        tex.Apply(false, true);
        return Sprite.Create(tex, new Rect(0, 0, TexW, TexH), new Vector2(.5f, .5f), TexW);
    }

    void OnDestroy()
    {
        if (cam != null) cam.transform.position -= applied;
        if (instance == this) instance = null;
    }
}
