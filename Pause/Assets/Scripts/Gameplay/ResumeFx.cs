using UnityEngine;
using UnityEngine.SceneManagement;

// "When the ship is coming off pause and slowly winding back up to current
// speed, we need an indicator to show that's happening -- a slow motion
// animation to current speed vibe."
//
// The indicator for ResumeSlowMo's window (resume at HUD speed >= 15: 0.6x
// for 0.5s, then an ease-out back to 1x over 0.4s). Every element is driven
// by the *actual* time factor ResumeSlowMo is applying this frame, so it is
// strongest at 0.6x and fades to nothing exactly as the world reaches full
// speed; nothing here has its own timeline that could drift from the curve.
//
//   Intensity = (1 - factor) / (1 - 0.6)    1 during the hold, 0 at 1x
//   Progress  = 1 - Intensity               0 slow .. 1 back up to speed
//
// One cohesive "time is thick, now spooling back up" look, in the Akira
// neon palette (MAGENTA = slowed, CYAN = speed):
//
//   World layer (ResumeFxView, sprites under every gameplay renderer):
//   - a stepped, dithered MAGENTA side-edge vignette flecked with CYAN
//     (a chromatic split), alpha = Intensity;
//   - speed streaks in two side lanes that crawl as short dashes while time
//     is slow and stretch into long fast lines as speed returns ("warp back
//     up"), fading out on the last few percent;
//   - three chromatic afterimages of the hull trailing below the ship.
//   HUD (ResumeSpeedRow, inside the SPEED row of the existing read-out):
//   - the row reads SLOW-MO and counts the effective speed back up
//     (HUD speed x factor) in MAGENTA, with a spool bar filling 60% -> 100%;
//     it punches back to "SPEED n" in CYAN when the wind-up completes.
//
// Placement rules:
// - The world layer sorts below the hull (order 0), its exhaust, the hearts,
//   the charge indicator and every enemy, and above the backdrop (-500..),
//   so it can never draw over a gameplay element. No colliders, no raycast
//   targets: it cannot take input.
// - The streak lanes run down the sides inside the rails; the bottom-centre
//   thumb area and the play field's middle stay clear.
// - The HUD part lives entirely inside the SPEED row's own rect, so it cannot
//   overlap the hearts, gun charge, score or quick actions at any screen size.
// - A re-pause (or death, or the ultimate's cinematic) cancels ResumeSlowMo
//   on that frame; Intensity reads 0 and everything is hidden on the same
//   frame (LateUpdate, after moveBackGround's Update decided the time scale).
// - Animation runs on unscaled time: the effect is about time being slowed,
//   so it must not slow itself.
// - Nothing allocates per frame: sprites, renderers and label strings are
//   built once.
public static class ResumeFx
{
    // ResumeSlowMo's slowest factor; Intensity is 1 there.
    public static float SlowScale => ResumeSlowMo.SlowScale;

    // The time factor ResumeSlowMo is applying right now (1 when idle).
    public static float Factor => ResumeSlowMo.IsActive ? ResumeSlowMo.CurrentScale : 1f;

    public static float IntensityFor(float factor)
    {
        return Mathf.Clamp01((1f - factor) / (1f - SlowScale));
    }

    public static float Intensity => IntensityFor(Factor);
    public static bool Showing => Intensity > 0f;

    // ---- tuning (shared with the test) ---------------------------------

    public const float VignetteAlpha = 0.85f;

    public const int StreaksPerSide = 5;
    public const float StreakWidthScale = 2f;     // 4 texels -> 8 px-art cells
    public const float StreakMinLength = 0.14f;   // world units, while slow
    public const float StreakMaxLength = 1.7f;    // world units, back to speed
    public const float StreakMinSpeed = 1.2f;     // world units / real second
    public const float StreakMaxSpeed = 24f;
    public const float StreakAlpha = 1f;
    // Lanes: |x| between these, clamped inside the rails' inner edge.
    public const float LaneInner = 1.35f;
    public const float LaneOuter = 2.3f;
    public const float RailInnerEdge = 2.49f;

    public const int Ghosts = 3;
    public const float GhostStep = 0.22f;          // world units between afterimages
    public static readonly float[] GhostAlpha = { 0.55f, 0.38f, 0.22f };

    // Sorting: all under gameplay (hull 0, exhaust -1), over the backdrop.
    public const int GhostOrder = -3;
    public const int StreakOrder = -4;
    public const int VignetteOrder = -5;

    public const string SpriteRoot = "ResumeFx/";
    public const string VignetteSprite = SpriteRoot + "resume_vignette";
    public const string StreakSprite = SpriteRoot + "resume_streak";

    public static readonly Color Slow = AkiraPalette.Magenta;
    public static readonly Color Fast = AkiraPalette.Cyan;

    // Streak length / speed for a given intensity. Stretch leads the fade
    // (sqrt) so the lines are long while they are still clearly visible.
    public static float StreakLength(float intensity)
    {
        return Mathf.Lerp(StreakMinLength, StreakMaxLength, Mathf.Sqrt(1f - Mathf.Clamp01(intensity)));
    }

    public static float StreakSpeed(float intensity)
    {
        return Mathf.Lerp(StreakMinSpeed, StreakMaxSpeed, 1f - Mathf.Clamp01(intensity));
    }

    // Full brightness through the hold and most of the ramp, gone at 1x.
    public static float StreakFade(float intensity)
    {
        return Mathf.Clamp01(intensity / 0.35f);
    }
}

// The world-space part of the indicator. One per gameS1 (ResumeFxBootstrap).
public class ResumeFxView : MonoBehaviour
{
    SpriteRenderer vignette;
    SpriteRenderer[] streaks;
    float[] streakY, streakLane, streakPace, streakLen;
    SpriteRenderer[] ghosts;
    Vector3[] ghostPos;
    SpriteRenderer hull;
    Camera cam;
    bool shown;
    float streakSpriteHeight = 0.4f;
    Vector2 vignetteSpriteSize = new Vector2(0.9f, 2f);

    // Tests read these.
    public SpriteRenderer Vignette => vignette;
    public SpriteRenderer[] Streaks => streaks;
    public SpriteRenderer[] GhostRenderers => ghosts;
    public bool Shown => shown;

    // Tests (and previews) point the view at a camera / hull explicitly.
    public Camera CameraOverride;
    public SpriteRenderer HullOverride;

    public static ResumeFxView Create()
    {
        var go = new GameObject("~ResumeFx");
        return go.AddComponent<ResumeFxView>();
    }

    void Awake() { Build(); }

    void Build()
    {
        if (vignette != null) return;
        var vSprite = Resources.Load<Sprite>(ResumeFx.VignetteSprite);
        var sSprite = Resources.Load<Sprite>(ResumeFx.StreakSprite);
        if (vSprite != null) vignetteSpriteSize = vSprite.bounds.size;
        if (sSprite != null) streakSpriteHeight = sSprite.bounds.size.y;

        vignette = MakeRenderer("Vignette", vSprite, ResumeFx.VignetteOrder);

        int n = ResumeFx.StreaksPerSide * 2;
        streaks = new SpriteRenderer[n];
        streakY = new float[n];
        streakLane = new float[n];
        streakPace = new float[n];
        streakLen = new float[n];
        for (int i = 0; i < n; i++)
        {
            streaks[i] = MakeRenderer("Streak" + i, sSprite, ResumeFx.StreakOrder);
            // Fixed, evenly spread lanes per side with a little jitter, so the
            // pattern reads as speed lines and never clumps.
            int side = i % 2 == 0 ? -1 : 1;
            int k = i / 2;
            float t = (k + 0.5f) / ResumeFx.StreaksPerSide;
            float jitter = ((k * 37 + (side > 0 ? 11 : 0)) % 7 - 3) * 0.03f;
            streakLane[i] = side * (Mathf.Lerp(ResumeFx.LaneInner, ResumeFx.LaneOuter, t) + jitter);
            streakPace[i] = 0.8f + ((k * 53 + (side > 0 ? 29 : 0)) % 9) * 0.06f;
            streakLen[i] = 0.75f + ((k * 31 + (side > 0 ? 17 : 0)) % 6) * 0.1f;
        }

        ghosts = new SpriteRenderer[ResumeFx.Ghosts];
        ghostPos = new Vector3[ResumeFx.Ghosts];
        for (int i = 0; i < ghosts.Length; i++)
            ghosts[i] = MakeRenderer("Ghost" + i, null, ResumeFx.GhostOrder);

        HideAll();
    }

    SpriteRenderer MakeRenderer(string name, Sprite sprite, int order)
    {
        var go = new GameObject(name);
        go.transform.SetParent(transform, false);
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = sprite;
        sr.sortingLayerName = "Default";
        sr.sortingOrder = order;
        sr.enabled = false;
        return sr;
    }

    void HideAll()
    {
        shown = false;
        if (vignette != null) vignette.enabled = false;
        if (streaks != null) foreach (var s in streaks) s.enabled = false;
        if (ghosts != null) foreach (var g in ghosts) g.enabled = false;
    }

    // After every Update: moveBackGround has decided this frame's time scale
    // (and a re-pause has already cancelled ResumeSlowMo).
    void LateUpdate()
    {
        Tick(ResumeFx.Intensity, Time.unscaledDeltaTime);
    }

    public void Tick(float intensity, float realDt)
    {
        if (vignette == null) Build();
        if (intensity <= 0f)
        {
            if (shown) HideAll();
            return;
        }

        var c = CameraOverride != null ? CameraOverride : cam;
        if (c == null) c = cam = Camera.main;
        if (c == null) { if (shown) HideAll(); return; }

        bool starting = !shown;
        shown = true;
        float halfH = c.orthographicSize;
        float halfW = halfH * c.aspect;
        Vector3 centre = c.transform.position;

        if (starting) BeginRun(halfH);

        PlaceVignette(intensity, centre, halfW, halfH);
        MoveStreaks(intensity, realDt, centre, halfW, halfH);
        MoveGhosts(intensity, realDt, starting);
    }

    void BeginRun(float halfH)
    {
        // Spread the streaks over the view; each lane starts somewhere new.
        for (int i = 0; i < streaks.Length; i++)
            streakY[i] = Mathf.Lerp(-halfH, halfH * 1.4f, Rand());
        hull = HullOverride;
        if (hull == null)
        {
            var player = Object.FindFirstObjectByType<movePlayer>();
            hull = player != null ? player.GetComponent<SpriteRenderer>() : null;
        }
    }

    // A private LCG: the streaks' scatter must not disturb UnityEngine.Random,
    // which gameplay spawning uses.
    uint seed = 0x9E3779B9u;
    float Rand()
    {
        seed = seed * 1664525u + 1013904223u;
        return (seed >> 8) / 16777216f;
    }

    void PlaceVignette(float intensity, Vector3 centre, float halfW, float halfH)
    {
        if (vignette.sprite == null) return;
        vignette.enabled = true;
        var t = vignette.transform;
        t.position = new Vector3(centre.x, centre.y, 0f);
        t.localScale = new Vector3(2f * halfW / vignetteSpriteSize.x, 2f * halfH / vignetteSpriteSize.y, 1f);
        vignette.color = new Color(1f, 1f, 1f, ResumeFx.VignetteAlpha * intensity);
    }

    void MoveStreaks(float intensity, float realDt, Vector3 centre, float halfW, float halfH)
    {
        float length = ResumeFx.StreakLength(intensity);
        float speed = ResumeFx.StreakSpeed(intensity);
        float alpha = ResumeFx.StreakAlpha * ResumeFx.StreakFade(intensity);
        float maxX = Mathf.Min(halfW, ResumeFx.RailInnerEdge) - 0.06f;
        float bottom = -halfH - 0.2f;
        float top = halfH + 0.2f;
        for (int i = 0; i < streaks.Length; i++)
        {
            var s = streaks[i];
            if (s.sprite == null) continue;
            float len = length * streakLen[i];
            streakY[i] -= speed * streakPace[i] * realDt;
            // The head (pivot) wraps to the top once the whole streak is off
            // the bottom.
            if (streakY[i] + len < bottom) streakY[i] = top + halfH * 0.6f * Rand();
            float x = Mathf.Clamp(streakLane[i], -maxX, maxX);
            s.enabled = true;
            s.transform.position = new Vector3(centre.x + x, centre.y + streakY[i], 0f);
            s.transform.localScale = new Vector3(ResumeFx.StreakWidthScale, len / streakSpriteHeight, 1f);
            s.color = new Color(1f, 1f, 1f, alpha);
        }
    }

    void MoveGhosts(float intensity, float realDt, bool starting)
    {
        bool hullOk = hull != null && hull.enabled && hull.gameObject.activeInHierarchy && hull.sprite != null;
        var ht = hullOk ? hull.transform : null;
        for (int i = 0; i < ghosts.Length; i++)
        {
            var g = ghosts[i];
            if (!hullOk) { g.enabled = false; continue; }
            // Each afterimage chases the hull with more lag than the last,
            // and trails below it (the ship flies up the screen).
            Vector3 target = ht.position + new Vector3(0f, -ResumeFx.GhostStep * (i + 1) * (0.35f + 0.65f * intensity), 0f);
            if (starting) ghostPos[i] = target;
            float follow = 1f - Mathf.Exp(-realDt * (22f / (i + 1)));
            ghostPos[i] = Vector3.Lerp(ghostPos[i], target, follow);

            g.enabled = true;
            g.sprite = hull.sprite;
            g.flipX = hull.flipX;
            g.flipY = hull.flipY;
            g.sortingOrder = hull.sortingOrder + ResumeFx.GhostOrder;
            var t = g.transform;
            t.position = new Vector3(ghostPos[i].x, ghostPos[i].y, ht.position.z);
            t.rotation = ht.rotation;
            t.localScale = ht.lossyScale;
            Color col = i % 2 == 0 ? ResumeFx.Slow : ResumeFx.Fast;
            col.a = ResumeFx.GhostAlpha[i] * intensity * hull.color.a;
            g.color = col;
        }
    }
}

public static class ResumeFxBootstrap
{
    public const string Scene = "gameS1";

    [RuntimeInitializeOnLoadMethod]
    static void Init()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        // The tutorial never slows on resume (ResumeSlowMo.InTutorial).
        if (scene.name != Scene) return;
        if (Object.FindFirstObjectByType<ResumeFxView>() != null) return;
        ResumeFxView.Create();
    }
}
