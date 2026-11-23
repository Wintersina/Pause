using UnityEngine;
using UnityEngine.UI;

// The finger-lift pause, hyped: "PAUSED!!" slams in at the ship, holds a beat,
// then shrinks and fades into the subtle pause icon (which only appears as the
// slam settles into it). If the pause costs a pause, a "-1" floats up beside
// the HUD's PAUSES counter and the number ticks down live a moment later.
//
// Rules (they mirror score.Update, which is the one place a pause is spent):
//  * The slam shows on every finger lift of a started run that still has a
//    pause left (the same moment moveStarsBackground shows the icon): never
//    before the first touch, after death, or in the kill cam.
//  * A pause is spent when the finger comes back down (score.pauseCounterFunction),
//    so at the lift this predicts it: a started run, no scripted free press
//    (boss intro, planetfall, lift-off, portal arrival) and no free STASIS
//    freeze. Only then does the "-1" show and the counter read one less. The
//    counter variable itself is untouched: the HUD just shows it a moment early
//    (ShownPauses), and the real spend lands on the resume. A lift that will
//    not be charged still slams, with no -1.
//  * Touching again cuts the slam on the spot (OnResumed).
//
// One reused overlay canvas and one reused "-1" label: nothing is created per
// lift, nothing allocates per frame, and a lift while one is playing restarts
// it. Unscaled time throughout (the world is frozen); sized from the UI unit
// and the screen, and kept inside the safe area at every aspect.
public sealed class PauseSlam : MonoBehaviour
{
    public const string ObjectName = "~PauseSlam";
    public const string Caption = "PAUSED!!";

    // ---- timeline (seconds, unscaled) ----
    public const float SlamIn = 0.09f;     // scale crashes in
    public const float Settled = 0.24f;    // overshoot and shake are over
    public const float HoldEnd = 0.46f;    // then it shrinks into the icon
    public const float IconAt = 0.52f;     // the subtle icon appears (its own pop)
    public const float Total = 0.72f;
    public const float TickDelay = 0.20f;  // the counter rolls down this long after the lift
    public const float MinusDelay = 0.11f; // the -1 appears
    public const float MinusSeconds = 0.75f;
    public const int SortingOrder = 85;    // over the HUD, under the pause quick actions (90)

    const float StartScale = 2.6f, ImpactScale = 0.86f, Overshoot = 1.12f, EndScale = 0.2f;
    static readonly Vector2 ImpactSquash = new Vector2(1.22f, 0.8f);

    // Edit-mode tests: pretend the resume is free (a scripted press / STASIS).
    public static bool TestForceFree;

    static PauseSlam instance;

    // ---- pure rules ----
    public static bool ShouldSlam(bool runStarted, int pausesLeft, bool dead, bool killCam)
    {
        return runStarted && pausesLeft > 0 && !dead && !killCam;
    }

    // Will the resume spend a pause? (score.ShouldSpendPause + the free cases.)
    public static bool WillSpend(bool runStarted, bool scriptedFreePress, bool stasisFree)
    {
        return runStarted && !scriptedFreePress && !stasisFree;
    }

    // Slam body scale (uniform) at age t.
    public static float BodyScale(float t)
    {
        if (t <= 0f) return StartScale;
        if (t < SlamIn) { float u = t / SlamIn; return Mathf.Lerp(StartScale, ImpactScale, u * u); }
        if (t < 0.17f) return Mathf.Lerp(ImpactScale, Overshoot, Smooth((t - SlamIn) / (0.17f - SlamIn)));
        if (t < Settled) return Mathf.Lerp(Overshoot, 1f, Smooth((t - 0.17f) / (Settled - 0.17f)));
        if (t < HoldEnd) return 1f;
        float v = Mathf.Clamp01((t - HoldEnd) / (Total - HoldEnd));
        return Mathf.Lerp(1f, EndScale, v * v);
    }

    // Squash on impact, relaxing to 1 by Settled.
    public static Vector2 Squash(float t)
    {
        if (t < SlamIn || t >= Settled) return Vector2.one;
        float k = 1f - (t - SlamIn) / (Settled - SlamIn);
        k *= k;
        return new Vector2(1f + (ImpactSquash.x - 1f) * k, 1f + (ImpactSquash.y - 1f) * k);
    }

    // Shake strength 0..1 (after impact only).
    public static float Shake(float t)
    {
        if (t < SlamIn || t >= SlamIn + 0.16f) return 0f;
        float k = 1f - (t - SlamIn) / 0.16f;
        return k * k;
    }

    // Whole-pop opacity: in at once, out while it flies into the icon.
    public static float Alpha(float t)
    {
        if (t <= 0f || t >= Total) return 0f;
        if (t < 0.03f) return t / 0.03f;
        if (t < HoldEnd) return 1f;
        float v = (t - HoldEnd) / (Total - HoldEnd);
        return 1f - v * v;
    }

    // 0 at the slam's spot, 1 at the icon.
    public static float Travel(float t)
    {
        return t <= HoldEnd ? 0f : Smooth(Mathf.Clamp01((t - HoldEnd) / (Total - HoldEnd)));
    }

    // Afterimage: a bone copy that blows out from the impact. (scale multiplier, alpha)
    public static void Ghost(float t, out float scale, out float alpha)
    {
        scale = 1f; alpha = 0f;
        if (t < SlamIn || t >= SlamIn + 0.26f) return;
        float g = (t - SlamIn) / 0.26f;
        scale = 1f + 0.45f * g;
        alpha = 0.5f * (1f - g) * (1f - g);
    }

    // Halo flash: bright on impact, then a steady glow behind the lettering.
    public static float HaloAlpha(float t)
    {
        if (t < SlamIn) return 0.35f * (t / SlamIn);
        if (t < SlamIn + 0.3f) return Mathf.Lerp(1f, 0.55f, (t - SlamIn) / 0.3f);
        return 0.55f;
    }

    // Type size in pixels: from the UI unit, never wider than a tenth-ish of the screen or a short window's height.
    public static float FontPxFor(Vector2 screen, float unitPx)
    {
        return Mathf.Max(24f, Mathf.Min(unitPx * 40f, Mathf.Min(screen.x * 0.1f, screen.y * 0.08f)));
    }

    // Centre that keeps a half-size box inside the safe area.
    public static Vector2 Clamp(Vector2 centre, Vector2 half, Rect safe)
    {
        float x = half.x * 2f >= safe.width ? safe.center.x : Mathf.Clamp(centre.x, safe.xMin + half.x, safe.xMax - half.x);
        float y = half.y * 2f >= safe.height ? safe.center.y : Mathf.Clamp(centre.y, safe.yMin + half.y, safe.yMax - half.y);
        return new Vector2(x, y);
    }

    // The "-1" label's pose at age m: rise (in lines), scale, opacity.
    public static void Minus(float m, out float rise, out float scale, out float alpha)
    {
        float u = Mathf.Clamp01(m / MinusSeconds);
        rise = 1.25f * (1f - (1f - u) * (1f - u));
        scale = m < 0.1f ? Mathf.Lerp(1.7f, 1f, m / 0.1f) : 1f;
        alpha = u < 0.55f ? 1f : 1f - (u - 0.55f) / 0.45f;
    }

    static float Smooth(float u) { u = Mathf.Clamp01(u); return u * u * (3f - 2f * u); }

    // ---- state ----
    bool active;
    float age;
    // the pending spend: the HUD shows `target` once the tick delay has passed
    bool pending, resumed;
    int target, resolveFrames;
    float pendingAge;
    // the -1 label
    bool minusOn;
    float minusAge;

    Canvas canvas;
    RectTransform root, body, ghost, halo;
    CanvasGroup rootGroup, ghostGroup, haloGroup;
    Text bodyText, ghostText, minusText;
    CanvasGroup minusGroup;
    PausedLabelGradient gradient;
    Outline contour;
    Shadow shadow;
    Texture2D haloTex;
    Sprite haloSprite;
    float fontPx, fontBuilt = -1f;
    Vector2 slamAt, iconAt, half;
    float minusBaseX;
    Text counterText;

    // ---- queries (HUD, icon, tests) ----
    public static PauseSlam Live { get { return instance; } }
    public static bool Active { get { return instance != null && instance.active; } }
    public static float Age { get { return instance != null ? instance.age : 0f; } }
    // True until the slam has settled toward the icon: moveStarsBackground keeps the icon hidden.
    public static bool HoldsIcon { get { return instance != null && instance.active && instance.age < IconAt; } }
    public static bool MinusVisible { get { return instance != null && instance.minusOn; } }
    public static string MinusLabel { get { return instance != null && instance.minusText != null ? instance.minusText.text : null; } }
    public static bool Pending { get { return instance != null && instance.pending; } }

    // The pause count the HUD should read: one less from TickDelay after a charged lift, until the resume lands.
    public static int ShownPauses(int real)
    {
        var s = instance;
        if (s == null || !s.pending || s.pendingAge < TickDelay) return real;
        return Mathf.Min(real, s.target);
    }

    // ---- events (moveStarsBackground) ----
    public static void OnPaused(GameObject icon)
    {
        int left = score.pauseCounter;
        if (!ShouldSlam(score.RunHasStarted, left, buttonClicks.playerDied, KillCam.Playing))
        {
            if (instance != null) instance.Stop();
            return;
        }
        var s = Ensure();
        bool scripted = BossEncounter.FreePress || Planetfall.FreePress || Liftoff.FreePress || PortalArrival.FreePress;
        bool spend = WillSpend(score.RunHasStarted, scripted || TestForceFree, AtomFusions.StasisFreeLeft > 0);
        s.Begin(icon, left, spend);
    }

    public static void OnResumed()
    {
        var s = instance;
        if (s == null) return;
        s.Stop();
        if (s.pending)
        {
            s.resumed = true;
            s.resolveFrames = 0;
            if (s.pendingAge < TickDelay) s.pendingAge = TickDelay;   // never leave the old number up past the resume
        }
    }

    // Scene start: build the canvas, the glyphs and the -1 now, so the first lift of the run costs no hitch.
    public static void Prewarm()
    {
        var s = Ensure();
        s.Size(FontPxFor(ScreenInfo.Size, UiScale.UnitPx));
        if (s.bodyText.font != null) s.bodyText.font.RequestCharactersInTexture(Caption, s.bodyText.fontSize, FontStyle.Normal);
        if (s.EnsureMinus())
        {
            s.minusText.gameObject.SetActive(false);
            if (s.minusText.font != null) s.minusText.font.RequestCharactersInTexture("-1", s.minusText.fontSize, FontStyle.Bold);
        }
    }

    // Test hook: advance as if `seconds` of unscaled time passed.
    public static void Advance(float seconds)
    {
        if (instance != null) instance.Step(seconds);
    }

    static PauseSlam Ensure()
    {
        if (instance != null) return instance;
        var go = new GameObject(ObjectName, typeof(RectTransform));
        instance = go.AddComponent<PauseSlam>();
        instance.Build();
        return instance;
    }

    // ---- build (once) ----
    void Build()
    {
        canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = SortingOrder;
        root = (RectTransform)transform;
        rootGroup = gameObject.AddComponent<CanvasGroup>();
        rootGroup.blocksRaycasts = false;
        rootGroup.interactable = false;

        halo = NewRect("Halo", typeof(Image));
        haloGroup = halo.gameObject.AddComponent<CanvasGroup>();
        haloTex = PausedLabel.BuildHaloTexture();
        haloSprite = Sprite.Create(haloTex, new Rect(0, 0, haloTex.width, haloTex.height), new Vector2(.5f, .5f), 100f, 0, SpriteMeshType.FullRect);
        var haloImage = halo.GetComponent<Image>();
        haloImage.sprite = haloSprite;
        haloImage.color = PausedLabel.Halo;
        haloImage.raycastTarget = false;

        ghost = NewRect("Afterimage", typeof(Text));
        ghostGroup = ghost.gameObject.AddComponent<CanvasGroup>();
        ghostText = StyleText(ghost.GetComponent<Text>());
        ghostText.color = AkiraPalette.Bone;

        body = NewRect("Body", typeof(Text));
        bodyText = StyleText(body.GetComponent<Text>());
        bodyText.color = Color.white;
        // same effect order as PausedLabel: fill, contour, then the shadow of the contoured glyph
        gradient = body.gameObject.AddComponent<PausedLabelGradient>();
        gradient.Set(PausedLabel.FillTop, PausedLabel.FillBottom);
        contour = body.gameObject.AddComponent<Outline>();
        contour.effectColor = PausedLabel.Contour;
        shadow = body.gameObject.AddComponent<Shadow>();
        shadow.effectColor = PausedLabel.DropShadow;

        rootGroup.alpha = 0f;
    }

    RectTransform NewRect(string name, System.Type graphic)
    {
        var go = new GameObject(name, typeof(RectTransform), graphic);
        go.transform.SetParent(transform, false);
        var rt = (RectTransform)go.transform;
        rt.anchorMin = rt.anchorMax = Vector2.zero;   // positioned in screen pixels from the corner
        rt.pivot = new Vector2(.5f, .5f);
        var g = go.GetComponent<Graphic>();
        if (g != null) g.raycastTarget = false;
        return rt;
    }

    static Text StyleText(Text t)
    {
        t.font = WorldBanner.OrbitronOrBuiltin();
        t.fontStyle = FontStyle.Normal;
        t.alignment = TextAnchor.MiddleCenter;
        t.horizontalOverflow = HorizontalWrapMode.Overflow;
        t.verticalOverflow = VerticalWrapMode.Overflow;
        t.supportRichText = false;
        t.raycastTarget = false;
        t.text = Caption;
        return t;
    }

    void Size(float px)
    {
        if (Mathf.Approximately(px, fontBuilt)) return;
        fontBuilt = px;
        fontPx = px;
        int f = Mathf.RoundToInt(px);
        bodyText.fontSize = ghostText.fontSize = f;
        float k = px / 64f;
        contour.effectDistance = new Vector2(2.2f, 2.2f) * k;
        shadow.effectDistance = new Vector2(4f, -5f) * k;
        float w = bodyText.preferredWidth + 10f * k;
        float h = px * 0.72f + 14f * k;
        h = Mathf.Max(h, px * 1.1f);
        body.sizeDelta = ghost.sizeDelta = new Vector2(w, h);
        halo.sizeDelta = new Vector2(w * 1.35f, h * 2.1f);
    }

    // ---- play ----
    void Begin(GameObject icon, int left, bool spend)
    {
        Vector2 screen = ScreenInfo.Size;
        Size(FontPxFor(screen, UiScale.UnitPx));

        // At the ship, a little above it; the icon's own spot is where it flies to.
        Vector2 ship = screen * new Vector2(0.5f, 0.3f);
        var cam = Camera.main;
        var live = ShipSpawn.Live;
        if (cam != null && live != null) ship = cam.WorldToScreenPoint(live.transform.position);
        Vector2 iconPx = screen * 0.5f;
        if (cam != null && icon != null) iconPx = cam.WorldToScreenPoint(icon.transform.position);

        // the biggest the body gets (overshoot plus the afterimage) must stay on screen
        half = new Vector2(body.sizeDelta.x * 0.5f * Overshoot * 1.1f, body.sizeDelta.y * 0.5f * Overshoot * 1.3f);
        slamAt = Clamp(ship + new Vector2(0f, fontPx * 1.3f), half, ScreenInfo.SafeArea);
        iconAt = iconPx;

        active = true;
        age = 0f;
        root.gameObject.SetActive(true);
        Apply();

        if (spend)
        {
            pending = true;
            resumed = false;
            target = left - 1;
            pendingAge = 0f;
            resolveFrames = 0;
            PrepareMinus();
        }
        else if (pending && !resumed)
        {
            pending = false;   // a free lift right after: nothing to show early
        }
    }

    void Stop()
    {
        active = false;
        age = 0f;
        if (rootGroup != null) rootGroup.alpha = 0f;
    }

    void Update()
    {
        Step(Time.unscaledDeltaTime);
    }

    void Step(float dt)
    {
        if (active)
        {
            if (buttonClicks.playerDied) Stop();
            else
            {
                age += dt;
                if (age >= Total) Stop();
                else Apply();
            }
        }
        if (pending)
        {
            pendingAge += dt;
            if (resumed)
            {
                // the real spend lands on the press; hand the HUD back its own number
                if (score.pauseCounter <= target || ++resolveFrames >= 3) pending = false;
            }
            else if (buttonClicks.playerDied) pending = false;
        }
        if (minusOn) StepMinus(dt);
    }

    void Apply()
    {
        float t = age;
        float s = BodyScale(t);
        Vector2 sq = Squash(t);
        float shake = Shake(t) * fontPx * 0.07f;
        Vector2 pos = Vector2.Lerp(slamAt, iconAt, Travel(t));
        pos.x += Mathf.Sin(t * 173f) * shake;
        pos.y += Mathf.Cos(t * 151f) * shake;

        rootGroup.alpha = Alpha(t);
        body.anchoredPosition = pos;
        body.localScale = new Vector3(s * sq.x, s * sq.y, 1f);
        halo.anchoredPosition = pos;
        halo.localScale = new Vector3(s, s, 1f);
        haloGroup.alpha = HaloAlpha(t);
        Ghost(t, out float gs, out float ga);
        ghost.anchoredPosition = pos;
        ghost.localScale = new Vector3(s * gs, s * gs, 1f);
        ghostGroup.alpha = ga;
    }

    // ---- the -1 beside the counter ----
    // Finds the counter and builds the (hidden) -1 once. False if the scene has no counter.
    bool EnsureMinus()
    {
        if (counterText == null)
        {
            var go = SceneUtil.FindAny("PauseCounter");
            if (go == null) go = SceneUtil.FindAny("PausesRemainingText");
            counterText = go != null ? go.GetComponent<Text>() : null;
            minusText = null;
        }
        if (counterText == null) return false;
        if (minusText == null)
        {
            var existing = counterText.transform.Find("PauseMinusOne");
            var go = existing != null ? existing.gameObject : new GameObject("PauseMinusOne", typeof(RectTransform), typeof(Text));
            go.transform.SetParent(counterText.transform, false);
            minusText = go.GetComponent<Text>();
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0f, .5f);
            rt.pivot = new Vector2(0f, .5f);
            rt.sizeDelta = new Vector2(counterText.fontSize * 3f, counterText.fontSize * 1.4f);
            minusText.font = counterText.font;
            minusText.fontStyle = FontStyle.Bold;
            minusText.alignment = TextAnchor.MiddleLeft;
            minusText.horizontalOverflow = HorizontalWrapMode.Overflow;
            minusText.verticalOverflow = VerticalWrapMode.Overflow;
            minusText.supportRichText = false;
            minusText.raycastTarget = false;
            minusText.text = "-1";
            minusText.color = AkiraPalette.Amber;
            minusGroup = go.GetComponent<CanvasGroup>();
            if (minusGroup == null) minusGroup = go.AddComponent<CanvasGroup>();
            minusGroup.blocksRaycasts = false;
            var o = go.AddComponent<Outline>();
            o.effectColor = AkiraPalette.WithAlpha(AkiraPalette.Ink, .95f);
            o.effectDistance = new Vector2(2f, -2f);
        }
        minusText.font = counterText.font;   // the HUD may restyle the counter after we built
        minusText.fontSize = counterText.fontSize;
        return true;
    }

    void PrepareMinus()
    {
        if (!EnsureMinus()) return;
        minusText.gameObject.SetActive(false);
        minusBaseX = counterText.preferredWidth + counterText.fontSize * 0.35f;
        minusAge = -MinusDelay;   // appears MinusDelay after the lift
        minusOn = true;
    }

    void StepMinus(float dt)
    {
        if (minusText == null || counterText == null) { minusOn = false; return; }
        minusAge += dt;
        if (minusAge >= MinusSeconds) { minusText.gameObject.SetActive(false); minusOn = false; return; }
        if (minusAge < 0f) return;
        Minus(minusAge, out float rise, out float sc, out float a);
        if (!minusText.gameObject.activeSelf) minusText.gameObject.SetActive(true);
        var rt = (RectTransform)minusText.transform;
        float line = counterText.fontSize;
        rt.anchoredPosition = new Vector2(minusBaseX + line * 0.25f * rise, line * rise);
        rt.localScale = new Vector3(sc, sc, 1f);
        minusGroup.alpha = a;   // a group, not the colour: no mesh rebuild per frame
    }

    void OnDestroy()
    {
        if (instance == this) instance = null;
        if (minusText != null) minusText.gameObject.SetActive(false);
        if (haloSprite != null) Release(haloSprite);
        if (haloTex != null) Release(haloTex);
    }

    static void Release(Object o)
    {
        if (Application.isPlaying) Destroy(o); else DestroyImmediate(o);
    }
}
