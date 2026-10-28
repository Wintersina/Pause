using System.Collections.Generic;
using UnityEngine;

// A boss's ENTRANCE -- the encounter's Intro phase (BossEncounter), every
// boss in every world:
//
//   0.00  the boss song starts (WorldMusic.BeginBoss, prewarmed by the
//         BOSS INCOMING warning) and the world sinks into slow motion
//         (EntranceSlowScale, eased in over SlowInSeconds). The pilot is
//         untouchable from here to the fight (BossEncounter.ShieldsShip).
//   0.35  the boss rises out of the world's own backdrop (BossEmergence: a
//         landing site on one of the backdrop's set pieces -- a station's
//         hangar, a planet's surface, a ground hatch; else a far point in the
//         sky) and grows into the play field, drawn behind the near
//         backdrop layers at first, hazed like the far scenery (BossActor).
//   3.75  it lands (LandAt): the shockwave (BossShockwave) sweeps from it
//         past every edge of the view and clears the board -- hazards burst
//         as friendly fire, hostile shots pop, area hazards and rail-mine
//         lasers go.
//   4.00  the slow motion eases back to 1x over SlowOutSeconds ...
//   4.80  ... and the fight begins at full speed (IntroSeconds).
//
// The slow motion is not stored anywhere: moveBackGround asks
// BossEncounter.EntranceTimeScale every frame while the intro runs, so
// whatever ends the intro -- the fight, the pilot's death (Abort), a scene
// load (the encounter goes with the scene) -- ends the slow motion on the
// same frame. A finger lifted with pauses left freezes the world exactly as
// in flight (and the entrance's clock, on real time, carries on).
public static class BossEntrance
{
    // Time.timeScale `introClock` real seconds into the entrance (while the
    // world runs). 1 before and after it.
    public static float TimeScaleAt(float introClock)
    {
        float slow = Mathf.Clamp(BossConfig.EntranceSlowScale, .05f, 1f);
        if (introClock < 0f || introClock >= BossConfig.IntroSeconds) return 1f;
        if (introClock < BossConfig.SlowInSeconds)
            return Mathf.Lerp(1f, slow, Smooth(introClock / Mathf.Max(.01f, BossConfig.SlowInSeconds)));
        if (introClock < BossConfig.SlowOutAt) return slow;
        float u = (introClock - BossConfig.SlowOutAt) / Mathf.Max(.01f, BossConfig.SlowOutSeconds);
        return Mathf.Lerp(slow, 1f, Smooth(u));
    }

    public static float Smooth(float t)
    {
        t = Mathf.Clamp01(t);
        return t * t * (3f - 2f * t);
    }
}

// Where the boss rises from: a spot on the current world's backdrop.
//
// The backdrop directors already report the places its set pieces can
// launch a ship from (LandingSites: Space's stations, planets and big
// asteroids, the planet worlds' hangars, hatches and pads) with how big a
// play-size ship looks down there and the sorting order just above the
// piece. The boss takes the one in view highest up the screen and nearest
// the middle -- it flies up and out toward its resting place. A backdrop
// with no site in view (or none at all: tests, a world without set pieces)
// gives a far point high in the sky, drawn just above the world's ground /
// station layer.
public static class BossEmergence
{
    public struct Origin
    {
        public bool hasSite;
        public LandingSite site;
        public Vector3 position;
        public float scale;    // the boss's scale there (1 = its play size)
        public int order;      // sorting order there (behind the near backdrop layers)
        public bool emerge;    // it comes out of the piece: fades in from nothing
    }

    // Size of the boss at the site, relative to a play-size ship there (the
    // boss is ~3x an elite's cell), and its bounds.
    public const float SiteScaleK = .55f;
    public const float MinScale = .06f, MaxScale = .3f;
    public const float FallbackScale = .1f;
    public const float FallbackHeightShare = .78f;   // of the view, bottom to top

    static readonly List<LandingSite> sites = new List<LandingSite>(16);

    public static Origin Pick()
    {
        float top = CameraFit.ViewTop, bottom = CameraFit.ViewBottom;
        float halfW = HalfWidth();
        var mainCam = CameraFit.Main;
        float cx = mainCam != null ? mainCam.transform.position.x : 0f;
        LandingSites.Gather(sites);
        int best = -1;
        float bestScore = float.NegativeInfinity;
        for (int i = 0; i < sites.Count; i++)
        {
            var s = sites[i];
            if (!s.Valid) continue;
            Vector3 p = s.Position;
            if (p.y < bottom + 1f || p.y > top - .5f || Mathf.Abs(p.x - cx) > halfW) continue;
            float score = p.y - Mathf.Abs(p.x - cx) * .5f;
            if (score > bestScore) { bestScore = score; best = i; }
        }
        if (best >= 0)
        {
            var s = sites[best];
            var o = new Origin
            {
                hasSite = true,
                site = s,
                position = s.Position,
                scale = Mathf.Clamp((s.scale > 0f ? s.scale : .3f) * SiteScaleK, MinScale, MaxScale),
                order = s.order,
                emerge = s.emerge,
            };
            sites.Clear();
            return o;
        }
        sites.Clear();
        return Fallback(cx, bottom, top);
    }

    public static Origin Fallback(float cx, float bottom, float top)
    {
        return new Origin
        {
            hasSite = false,
            position = new Vector3(cx, Mathf.Lerp(bottom, top, FallbackHeightShare), 0f),
            scale = FallbackScale,
            order = FallbackOrder(),
            emerge = true,
        };
    }

    // Just above the deepest ground plane of the world's backdrop (a planet
    // world's landmarks / ground pieces, Space's station layer).
    public static int FallbackOrder()
    {
        var spec = BackdropCatalog.For(WorldManager.Current.displayName);
        if (spec == null || spec.layers == null) return BackdropCatalog.BaseOrder + 1;
        int at = -1;
        for (int i = 0; i < spec.layers.Length; i++)
        {
            var l = spec.layers[i];
            if (l.role == BackdropCatalog.Role.Landmark || l.name == "mid" || l.name == "planet_near") at = i;
        }
        return at >= 0 ? spec.Order(spec.layers[at].name) + 1 : BackdropCatalog.BaseOrder + 1;
    }

    static float HalfWidth()
    {
        var c = CameraFit.Main;
        if (c != null && c.orthographic) return c.orthographicSize * c.aspect;
        return CameraFit.GameplayHalfWidth;
    }
}

// The landing shockwave. Its front runs from the boss past the farthest
// corner of the view in BossConfig.ShockwaveSeconds of real time (an
// ease-out: it bursts away and slows), and everything it reaches goes:
//
//   hazards (rocks, enemies, mines, rail mines, elites in play)
//            die as FRIENDLY FIRE (FriendlyFire.ShockwaveHit): the hostile-
//            fire attribution, the FRIENDLY FIRE word, no pilot credit --
//            with no kill cap and no spawn-in protection, so nothing is left
//   hostile shots   pop where they are
// and when it is done (or the fight starts first: Finish) the rest of the
// board goes too -- anything above the view, elites still parked or rising,
// area hazards and their previews, rail-mine lasers. The boss is never
// touched, nor are pickups.
//
// Drawn as a chunky pixel ring (one generated texture, point-filtered, in
// the boss's flash colour) with a fainter echo behind it.
public sealed class BossShockwave
{
    public const int RingSortingOrder = 45;   // over the boss, its shots and hearts; under the HUD canvases
    const int TexSize = 128;

    readonly Vector3 at;
    readonly float maxRadius;
    float age;
    bool finished;
    GameObject root;
    SpriteRenderer ring, echo;
    Color tint;

    public int Kills { get; private set; }
    public int ShotsPopped { get; private set; }
    public Vector3 Origin => at;
    public float Age => age;
    public bool Done => age >= BossConfig.ShockwaveSeconds;
    public bool Finished => finished;
    public float MaxRadius => maxRadius;
    public float Radius => maxRadius * EaseOut(age / Mathf.Max(.01f, BossConfig.ShockwaveSeconds));
    public GameObject Visual => root;

    static Texture2D ringTex;
    static Sprite ringSprite;
    static readonly List<ClearTarget> scratch = new List<ClearTarget>(64);

    public BossShockwave(Vector3 origin, Color flash)
    {
        at = new Vector3(origin.x, origin.y, 0f);
        maxRadius = ReachFrom(at);
        tint = Color.Lerp(flash, Color.white, .35f);
        BuildVisual();
        Sweep(0f);
    }

    // From `p` past the farthest corner of the view.
    public static float ReachFrom(Vector3 p)
    {
        var c = CameraFit.Main;
        float top = CameraFit.ViewTop, bottom = CameraFit.ViewBottom;
        float cx = c != null ? c.transform.position.x : 0f;
        float hw = c != null && c.orthographic ? c.orthographicSize * c.aspect : CameraFit.GameplayHalfWidth;
        float dx = Mathf.Max(Mathf.Abs(cx - hw - p.x), Mathf.Abs(cx + hw - p.x));
        float dy = Mathf.Max(Mathf.Abs(top - p.y), Mathf.Abs(bottom - p.y));
        return Mathf.Sqrt(dx * dx + dy * dy) + 1f;
    }

    static float EaseOut(float t)
    {
        t = Mathf.Clamp01(t);
        float u = 1f - t;
        return 1f - u * u * u;
    }

    // One frame, real seconds.
    public void Step(float realDt)
    {
        if (finished) return;
        age += Mathf.Max(0f, realDt);
        Sweep(Radius);
        DrawVisual();
        if (Done) Finish();
    }

    // Everything inside `radius` goes.
    void Sweep(float radius)
    {
        scratch.Clear();
        scratch.AddRange(ClearTarget.Live);
        for (int i = 0; i < scratch.Count; i++)
        {
            var t = scratch[i];
            if (t == null || !t.isActiveAndEnabled || !ClearTarget.IsHazard(t.gameObject)) continue;
            float d = ((Vector2)t.transform.position - (Vector2)at).magnitude - t.Radius;
            if (d > radius) continue;
            if (FriendlyFire.ShockwaveHit(t, t.transform.position)) Kills++;
        }
        scratch.Clear();
        var shots = HostileShots.All;
        for (int i = shots.Count - 1; i >= 0; i--)
        {
            if (i >= shots.Count) continue;
            var s = shots[i];
            if (s == null || !s.ShotCollidable) continue;
            if ((s.ShotPosition - (Vector2)at).magnitude - s.ShotRadius > radius) continue;
            s.ShotPop(s.ShotPosition);
            ShotsPopped++;
        }
    }

    // The front is past the view (or the fight is starting): the rest of the
    // board goes, the ring is put away. Safe to call twice.
    public void Finish()
    {
        if (finished) return;
        finished = true;
        Sweep(float.PositiveInfinity);
        // elites still parked or rising in the backdrop (not in play: no target)
        var elites = EliteShip.Live;
        for (int i = elites.Count - 1; i >= 0; i--)
            if (elites[i] != null && !elites[i].InPlay && elites[i].State != EliteState.Dead) BossUtil.Kill(elites[i].gameObject);
        AttackPools.ClearAll();   // the themed area hazards and their previews
        RailMineLasers.Clear();   // rail-mine lasers
        Dispose();
    }

    // The ring only (the encounter's teardown, a death mid-entrance).
    public void Dispose()
    {
        if (root != null) BossUtil.Kill(root);
        root = null;
        ring = echo = null;
    }

    // ---- the ring ---------------------------------------------------------

    void BuildVisual()
    {
        var sprite = RingSprite();
        root = new GameObject("~BossShockwave");
        root.transform.position = at;
        ring = Layer("Ring", sprite, RingSortingOrder);
        echo = Layer("Echo", sprite, RingSortingOrder - 1);
        DrawVisual();
    }

    SpriteRenderer Layer(string name, Sprite sprite, int order)
    {
        var go = new GameObject(name);
        go.transform.SetParent(root.transform, false);
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = sprite;
        sr.sortingOrder = order;
        return sr;
    }

    void DrawVisual()
    {
        if (root == null) return;
        float k = Mathf.Clamp01(age / Mathf.Max(.01f, BossConfig.ShockwaveSeconds));
        float r = Mathf.Max(.2f, Radius);
        ring.transform.localScale = Vector3.one * r * 2f;
        // fades in held steps (pixel art: no smooth fades)
        float a = k < .5f ? 1f : k < .75f ? .66f : .33f;
        ring.color = new Color(tint.r, tint.g, tint.b, a);
        float kEcho = Mathf.Clamp01(k - .12f);
        float rEcho = Mathf.Max(.1f, maxRadius * EaseOut(kEcho) * .82f);
        echo.transform.localScale = Vector3.one * rEcho * 2f;
        echo.color = new Color(tint.r, tint.g, tint.b, k < .12f ? 0f : a * .45f);
    }

    // A white ring on a 128 px canvas, 1 world unit across: a 3 px solid
    // front, a 3 px half-bright band inside it and a 6 px faint trail, in
    // hard steps.
    public static Sprite RingSprite()
    {
        if (ringSprite != null) return ringSprite;
        ringTex = new Texture2D(TexSize, TexSize, TextureFormat.RGBA32, false)
        {
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Clamp,
            name = "BossShockwaveRing",
            hideFlags = HideFlags.DontSave,
        };
        var px = new Color32[TexSize * TexSize];
        float c = (TexSize - 1) * .5f, outer = TexSize * .5f - 1f;
        for (int y = 0; y < TexSize; y++)
            for (int x = 0; x < TexSize; x++)
            {
                float d = outer - Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c));
                byte alpha = d < 0f ? (byte)0 : d < 3f ? (byte)255 : d < 6f ? (byte)150 : d < 12f ? (byte)55 : (byte)0;
                px[y * TexSize + x] = new Color32(255, 255, 255, alpha);
            }
        ringTex.SetPixels32(px);
        ringTex.Apply(false, false);
        ringSprite = Sprite.Create(ringTex, new Rect(0, 0, TexSize, TexSize), new Vector2(.5f, .5f), TexSize);
        ringSprite.hideFlags = HideFlags.DontSave;
        return ringSprite;
    }
}
