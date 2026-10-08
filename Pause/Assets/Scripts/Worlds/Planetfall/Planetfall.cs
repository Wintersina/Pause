using System.Collections.Generic;
using UnityEngine;

// Planetfall: the way into a planet that has one (PlanetfallCatalog: Space
// -> Frost), in place of the portal. WorldManager opens it exactly where it
// would open a portal, and everything the open portal means still holds
// until the ship commits: the level clock is stopped, PortalPressure
// escalates the board for as long as the pilot lingers, and the column the
// planet hangs in is closed to pilots (Reserves).
//
//   APPROACH  the planet drifts down into the upper half of the Space
//             backdrop, growing as it nears, and holds there, drifting like
//             the portal. Its pull is the cue: a cyan atmosphere glow
//             beating faster as the pressure climbs, and a landing reticle
//             round the zone the ship must fly into (the planet's core).
//             The pilot flies as normal.
//   DESCENT   the ship touched the zone: input is taken over and the
//             sequence plays (PlanetfallTimeline has every moment): the
//             planet swells to a horizon, the ship dives through its
//             atmosphere and two cloud decks, and the world changes
//             (WorldManager.Advance -- music, palette, rails, backdrop,
//             enemy tables, loop rules, exactly as after a portal) while the
//             clouds hide the whole view. They break, the new world shows,
//             the ship settles under the pilot's finger and control returns.
//             Nothing can hurt the ship (ShieldsShip), nothing spawns
//             (SuspendsSpawning), the board was cleared at the commit; score,
//             hearts, pickups and the run carry through untouched.
//
// Clock: like the portal it moves only while the world is running
// (WorldManager.Flying), on Time.deltaTime, so a lifted finger freezes it
// mid-frame and a resume slow-mo slows it; a press during the descent never
// spends a pause (FreePress: the freeze is the cinematic's, not a tactic).
// Every renderer is built when the planet appears and reused; a frame
// allocates nothing.
//
// Layers: the planet, its glow and its limb sit in the backdrop (between
// Space's nearest bodies and its dust, so the speed dust streaks over them);
// the clouds, streaks and grade sit over the rails and the board; the ship's
// renderers are lifted above all of that for the descent (Raise) and put
// back after; only the flash covers the ship.
public class Planetfall : MonoBehaviour
{
    // ---- tunables: the approach ----
    // Seconds from first sight to holding station.
    public static float ArriveSeconds = 4.5f;
    // The disc's radius, world units: as it appears, and on station.
    public static float FirstRadius = .9f, StationRadius = 1.55f;
    // Where it holds: this share of the view's height, from the bottom.
    public static float StationHeight = .55f;
    // The zone the ship must touch: this share of the disc's radius.
    public static float ZoneShare = .6f;
    // Its home x is this far off the centre line, either side, and it drifts
    // this far either side of home once per DriftSeconds (as the portal).
    public static float HomeMinX = .4f, HomeMaxX = .7f;
    public static float DriftHalf = .3f, DriftSeconds = 9f;
    public static float ColumnMargin = .1f;

    // ---- tunables: the descent (the timing is PlanetfallTimeline's) ----
    // The limb's width at the horizon view, in view widths: wider is a
    // flatter horizon (and puts the planet's equatorial ring city below the
    // view for the dive).
    public static float LimbWidthShare = 2.2f;
    // The horizon view: the horizon's top this share of the half-height
    // above the view's centre; the dive scales about a point this share
    // below it.
    public static float ApexShare = .42f, DivePivotShare = -.35f;
    // How much the surface grows through the dive.
    public static float DiveScale = 2.8f;
    // The ship's dive spot: this share of the view's height, from the bottom.
    public static float DiveHeight = .34f;
    // The shroud's and the burst's cell widths, world units.
    public static float ShroudWidth = 2.6f, BurstWidth = 7.5f;
    public static float ShroudFps = 14f;
    // Clouds: world units per art pixel (deeper deck: finer), tiles a second.
    public static float DeckPixels = 1f / 70f, DarkDeckPixels = 1f / 110f;
    public static float DeckFlow = .55f, DarkDeckFlow = .28f;
    public static float StreakFlow = 1.5f, StreakShare = .3f, StreakStrength = .55f;
    // The cinematic quads overhang the view this much (the rumble never
    // shows an edge).
    public const float Overscan = 1.12f;

    // ---- sorting ----
    // Backdrop: between Space's "near" (-380) and "dust" (-370) layers.
    public const int PlanetOrder = BackdropCatalog.BaseOrder + 124, RimOrder = PlanetOrder + 1, LimbOrder = PlanetOrder + 2;
    public const int ReticleOrder = 45;
    // Over the rails (0) and everything on the board.
    public const int DarkDeckOrder = 200, DeckOrder = 210, StreakOrder = 220, TintOrder = 230, VignetteOrder = 240,
                     BurstOrder = 290, FlashOrder = 520;
    // The ship's renderers are lifted by this for the descent.
    public const int Raise = 300;

    // A long hitch (the frame after an app resume) moves the sequence at
    // most this far.
    public const float MaxStep = .1f;

    public enum Stage { Approach, Descent, Done }

    // The planetfall in progress, if any (one at a time).
    public static Planetfall Live { get; private set; }

    // ---- hooks for shared code ----
    // movePlayer: no steering or teleport; the descent flies the ship.
    public static bool HoldsShip { get { return Live != null && Live.state == Stage.Descent; } }
    // collisionDetection: hazards pass through harmlessly.
    public static bool ShieldsShip { get { return HoldsShip; } }
    // enmiesOnBoard, spawnGoodStuff: nothing new arrives.
    public static bool SuspendsSpawning { get { return HoldsShip; } }
    // score: a press during the descent never spends a pause.
    public static bool FreePress { get { return HoldsShip; } }

    // Does [xMin, xMax] cross the column the waiting planet's zone drifts in?
    public static bool Reserves(float xMin, float xMax)
    {
        var p = Live;
        if (p == null || p.state != Stage.Approach) return false;
        float h = ColumnHalf;
        return xMin < p.homeX + h && xMax > p.homeX - h;
    }

    public static float ColumnHalf { get { return DriftHalf + StationRadius * ZoneShare + ColumnMargin; } }

    // ---- state ----
    PlanetfallDef def;
    PlanetfallArt art;
    Stage state;
    float clock, held, homeX;       // approach
    float radius;                   // approach: the disc's radius now
    float t;                        // descent: seconds since the commit
    bool switched, bannered;
    string banner;
    Transform ship;
    Vector3 shipFrom;
    Vector3 commitApex, reticleScale;
    SpriteRenderer hull;
    float commitScale;
    Vector3 burstAt;
    float deckScroll, darkScroll, streakScroll, deckDrift;
    float shake, backdropBoost = 1f;
    Vector3 shakeApplied;
    Camera shakeCam, cam;
    CircleCollider2D zone;

    // view (world units), refreshed every step
    Vector3 centre;
    float halfW, halfH;

    // renderers
    Transform stage, group;
    SpriteRenderer planet, rim, limb, reticle, dark, deck, streakL, streakR, tint, vignette, flash, shroud, burst;
    Material limbMat, darkMat, deckMat, streakLMat, streakRMat;
    readonly List<Renderer> lifted = new List<Renderer>(32);

    static readonly int UvId = Shader.PropertyToID("_UV");
    static readonly int FadeId = Shader.PropertyToID("_Fade");
    static readonly int DstBlendId = Shader.PropertyToID("_DstBlend");
    public const string ShaderPath = "Planetfall/PlanetfallLayer";

    public Stage State { get { return state; } }
    public PlanetfallDef Def { get { return def; } }
    public PlanetfallArt Art { get { return art; } }
    public float Seconds { get { return state == Stage.Approach ? clock : t; } }
    public bool OnStation { get; private set; }
    public bool Switched { get { return switched; } }
    public float Radius { get { return radius; } }
    public float ZoneRadius { get { return radius * ZoneShare; } }
    public float HomeX { get { return homeX; } }
    public Transform Ship { get { return ship; } }
    public float ShakeNow { get { return shake; } }
    // What the backdrop's scroll is multiplied by (WorldBackdrop.ScrollBoost).
    public float BackdropBoost { get { return backdropBoost; } }

    // Renderers, for the tests and the preview.
    public SpriteRenderer PlanetRenderer { get { return planet; } }
    public SpriteRenderer LimbRenderer { get { return limb; } }
    public SpriteRenderer RimRenderer { get { return rim; } }
    public SpriteRenderer ReticleRenderer { get { return reticle; } }
    public SpriteRenderer DarkDeckRenderer { get { return dark; } }
    public SpriteRenderer DeckRenderer { get { return deck; } }
    public SpriteRenderer ShroudRenderer { get { return shroud; } }
    public SpriteRenderer BurstRenderer { get { return burst; } }
    public SpriteRenderer FlashRenderer { get { return flash; } }

    public static float StationY { get { return Mathf.Lerp(CameraFit.ViewBottom, CameraFit.ViewTop, StationHeight); } }

    // WorldManager.OpenPortal: the planet appears. Null (and nothing built)
    // when its art is missing; the caller opens a portal instead.
    public static Planetfall Spawn(PlanetfallDef def)
    {
        if (def == null) return null;
        var art = PlanetfallArt.Load(def);
        var shader = Resources.Load<Shader>(ShaderPath);
        if (!art.Complete || shader == null)
        {
            Debug.LogWarning("[Planetfall] art or shader missing for world " + def.world + "; opening a portal instead");
            art.Release();
            return null;
        }
        var go = new GameObject("~Planetfall");
        float side = Random.value < .5f ? -1f : 1f;
        var p = go.AddComponent<Planetfall>();
        p.def = def;
        p.art = art;
        p.homeX = side * Random.Range(HomeMinX, HomeMaxX);
        p.Build(shader);
        Live = p;
        p.Step(0f, true);
        PortalPressure.SetWording(def.urgeBanner, def.chipPrefix);
        WorldBanner.Show(def.openBanner);
        return p;
    }

    void Build(Shader shader)
    {
        cam = Camera.main;
        View();
        radius = FirstRadius;
        transform.position = new Vector3(homeX, centre.y + halfH + FirstRadius * .4f, 0f);

        zone = gameObject.AddComponent<CircleCollider2D>();
        zone.isTrigger = true;
        zone.radius = FirstRadius * ZoneShare;
        var rb = gameObject.AddComponent<Rigidbody2D>();
        rb.bodyType = RigidbodyType2D.Kinematic;
        rb.gravityScale = 0f;

        stage = new GameObject("~PlanetfallStage").transform;
        group = new GameObject("Surface").transform;
        group.SetParent(stage, false);

        planet = Part("Planet", group, art.Planet, PlanetOrder, null);
        rim = Part("Rim", group, art.Ring, RimOrder, null);
        limbMat = Layer(shader, false);
        limb = Part("Limb", group, art.Limb, LimbOrder, limbMat);
        limbMat.SetVector(FadeId, new Vector4(0f, .3f, 0f, 0f));   // the bottom 30% melts into the disc
        reticle = Part("Reticle", stage, art.Reticle, ReticleOrder, null);

        darkMat = Layer(shader, false);
        dark = Part("DeepClouds", stage, art.DeckDark, DarkDeckOrder, darkMat);
        deckMat = Layer(shader, false);
        deck = Part("Clouds", stage, art.Deck, DeckOrder, deckMat);
        streakLMat = Layer(shader, true);
        streakL = Part("StreaksL", stage, art.Streaks, StreakOrder, streakLMat);
        streakRMat = Layer(shader, true);
        streakR = Part("StreaksR", stage, art.Streaks, StreakOrder, streakRMat);
        tint = Part("Tint", stage, art.White, TintOrder, null);
        vignette = Part("Vignette", stage, art.Vignette, VignetteOrder, null);
        shroud = Part("Shroud", stage, art.Entry[0], 0, null);
        burst = Part("Burst", stage, art.Burst[0], BurstOrder, null);
        flash = Part("Flash", stage, art.White, FlashOrder, null);
    }

    static SpriteRenderer Part(string name, Transform parent, Sprite sprite, int order, Material mat)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = sprite;
        sr.sortingOrder = order;
        if (mat != null) sr.sharedMaterial = mat;
        sr.enabled = false;
        return sr;
    }

    static Material Layer(Shader shader, bool additive)
    {
        var m = new Material(shader) { name = "~PlanetfallLayer" };
        m.SetFloat(DstBlendId, additive ? (float)UnityEngine.Rendering.BlendMode.One
                                        : (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        return m;
    }

    void Update()
    {
        // Only while the world is actually moving: a paused game holds it.
        if (WorldManager.Flying) Step(Time.deltaTime);
    }

    // The rumble moves the camera after everything has drawn its frame
    // positions, and is taken off again before the next (as DeathCrash).
    void LateUpdate()
    {
        UndoShake();
        if (state != Stage.Descent || !WorldManager.Flying || shake < .002f) return;
        var c = Camera.main;
        if (c == null) return;
        shakeApplied = new Vector3(Mathf.Sin(t * 71f), Mathf.Cos(t * 53f) * .7f, 0f) * shake;
        c.transform.position += shakeApplied;
        shakeCam = c;
    }

    void UndoShake()
    {
        if (shakeCam != null) shakeCam.transform.position -= shakeApplied;
        shakeCam = null;
        shakeApplied = Vector3.zero;
    }

    // One running frame. Public so edit-mode tests and the preview can step
    // it (Time.deltaTime is 0 there).
    public void Step(float dt) { Step(dt, false); }

    void Step(float dt, bool force)
    {
        if (!force && dt <= 0f) return;
        if (state == Stage.Done) return;
        dt = Mathf.Min(Mathf.Max(0f, dt), MaxStep);
        View();
        if (state == Stage.Approach) StepApproach(dt);
        else StepDescent(dt);
    }

    void View()
    {
        if (cam == null) cam = Camera.main;
        if (cam != null && cam.orthographic)
        {
            centre = cam.transform.position - shakeApplied;
            halfH = cam.orthographicSize;
            halfW = halfH * cam.aspect;
        }
        else
        {
            centre = Vector3.zero;
            halfW = CameraFit.GameplayHalfWidth;
            halfH = halfW * 19.5f / 9f;
        }
        centre.z = 0f;
    }

    // ---- the approach -----------------------------------------------------

    void StepApproach(float dt)
    {
        clock += dt;
        float k = Mathf.Clamp01(clock / Mathf.Max(.01f, ArriveSeconds));
        float e = k * k * (3f - 2f * k);
        radius = Mathf.Lerp(FirstRadius, StationRadius, e);
        float top = centre.y + halfH + FirstRadius * .4f;
        Vector3 at = transform.position;
        at.y = Mathf.Lerp(top, Mathf.Lerp(centre.y - halfH, centre.y + halfH, StationHeight), e);
        if (k >= 1f)
        {
            OnStation = true;
            held += dt;
            at.x = homeX + DriftHalf * Mathf.Sin(held * 2f * Mathf.PI / Mathf.Max(.1f, DriftSeconds));
        }
        else at.x = homeX;
        transform.position = at;
        zone.radius = ZoneRadius;

        // the surface group: the disc centred on the zone
        float ra = ArcRadius();
        SetGroup(at + new Vector3(0f, radius, 0f), radius / ra);

        // the pull: the glow beats faster as the pressure climbs (as the
        // portal's core does)
        float rate = 1.1f * Mathf.Min(4f, 1f + .25f * PortalPressure.Level);
        float beat = .5f + .5f * Mathf.Sin(clock * rate * 2f * Mathf.PI);
        float appear = Mathf.Clamp01(clock / .6f);
        Show(planet, appear);
        Color cue = def.cue;
        cue.a = appear * (.45f + .35f * beat);
        rim.color = cue;
        rim.enabled = true;
        rim.transform.localScale *= 1f + .03f * beat;
        Show(limb, 0f);

        reticle.transform.position = at;
        reticle.transform.localScale = Vector3.one * (ZoneRadius / PlanetfallArt.ReticleRadius * 2f * (1f + .06f * beat));
        reticle.transform.localRotation = Quaternion.Euler(0f, 0f, clock * 40f);
        Color rc = def.cue;
        rc.a = appear * (.55f + .3f * beat);
        reticle.color = rc;
        reticle.enabled = true;
    }

    void OnTriggerEnter2D(Collider2D other)
    {
        if (state != Stage.Approach) return;
        var mover = other.GetComponentInParent<movePlayer>();
        if (mover == null) return;
        Commit(mover.transform);
    }

    // ---- the commit -------------------------------------------------------

    // The ship flew into the zone: the descent begins. False when it can't
    // (already committed, the pilot is dead, no ship).
    public bool Commit(Transform shipTransform)
    {
        if (state != Stage.Approach || shipTransform == null || buttonClicks.playerDied) return false;
        state = Stage.Descent;
        t = 0f;
        ship = shipTransform;
        shipFrom = ship.position;
        commitApex = group.position;
        commitScale = group.localScale.x;
        reticleScale = reticle.transform.localScale;
        hull = ship.GetComponent<SpriteRenderer>();
        zone.enabled = false;
        // the waiting is over: the pressure stops (the world bonus is paid
        // on arrival, by WorldManager.Advance)
        PortalPressure.Close(true);
        ClearBoard();
        Lift(true);
        StepDescent(0f);
        return true;
    }

    // Everything on the board goes, as for a boss's arrival: hazards in view
    // burst, the rest vanish, hostile shots pop.
    static readonly List<ClearTarget> clearing = new List<ClearTarget>(64);

    void ClearBoard()
    {
        clearing.Clear();
        clearing.AddRange(ClearTarget.Live);
        int shipId = ShipId.Equipped();
        foreach (var target in clearing)
        {
            if (target == null || !ClearTarget.IsHazard(target.gameObject)) continue;
            var go = target.gameObject;
            if (Application.isPlaying && cam != null)
            {
                Vector3 v = cam.WorldToViewportPoint(go.transform.position);
                if (v.x >= 0f && v.x <= 1f && v.y >= 0f && v.y <= 1f) TargetExplosion.Spawn(go, shipId);
            }
            ClearTarget.Release(go);
            BossUtil.Kill(go);
        }
        clearing.Clear();
        var shots = HostileShots.All;
        for (int i = 0; i < shots.Count; i++)
        {
            var s = shots[i];
            if (s != null && s.ShotCollidable) s.ShotPop(s.ShotPosition);
        }
    }

    // The ship's renderers over the clouds for the descent (and back).
    void Lift(bool up)
    {
        if (up)
        {
            lifted.Clear();
            if (ship == null) return;
            ship.GetComponentsInChildren(true, lifted);
            foreach (var r in lifted) r.sortingOrder += Raise;
        }
        else
        {
            foreach (var r in lifted) if (r != null) r.sortingOrder -= Raise;
            lifted.Clear();
        }
    }

    // ---- the descent ------------------------------------------------------

    void StepDescent(float dt)
    {
        t += dt;
        float tl = t;

        // ---- the surface: swell, then dive ----
        Vector3 apexView = new Vector3(centre.x, centre.y + ApexShare * halfH, 0f);
        if (tl <= PlanetfallTimeline.SwellSeconds)
        {
            float f = PlanetfallTimeline.Swell01(tl);
            float s = Mathf.Exp(Mathf.Lerp(Mathf.Log(Mathf.Max(1e-3f, commitScale)), 0f, f));
            SetGroup(Vector3.Lerp(commitApex, apexView, f), s);
        }
        else
        {
            float f = PlanetfallTimeline.Dive01(tl);
            float s = Mathf.Exp(f * Mathf.Log(DiveScale));
            Vector3 pivot = new Vector3(centre.x, centre.y + DivePivotShare * halfH, 0f);
            SetGroup(pivot + (apexView - pivot) * s, s);
        }
        Show(planet, PlanetfallTimeline.PlanetAlpha(tl));
        Color rc = Color.Lerp(def.cue, Color.white, .5f * PlanetfallTimeline.RimGlow(tl));
        rc.a = Mathf.Max(.8f * (1f - PlanetfallTimeline.Ramp(tl, 0f, .5f)), PlanetfallTimeline.RimGlow(tl));
        rim.color = rc;
        rim.enabled = rc.a > .002f;
        Show(limb, PlanetfallTimeline.LimbAlpha(tl));

        // the reticle snaps shut on the ship
        float lockOn = 1f - PlanetfallTimeline.Ramp(tl, 0f, .35f);
        reticle.enabled = lockOn > .002f;
        if (reticle.enabled)
        {
            reticle.transform.localScale = reticleScale * lockOn;
            reticle.transform.localRotation = Quaternion.Euler(0f, 0f, (clock + tl * 4f) * 40f);
            Color c = def.cue; c.a = lockOn; reticle.color = c;
        }

        // ---- the ship ----
        Vector3 dive = new Vector3(centre.x, centre.y - halfH + 2f * halfH * DiveHeight, shipFrom.z);
        Vector3 pos = Vector3.Lerp(shipFrom, dive, PlanetfallTimeline.Steer01(tl));
        float buffet = PlanetfallTimeline.ShroudAlpha(tl);
        pos.x += buffet * .05f * Mathf.Sin(tl * 9.3f);
        pos.y += buffet * .03f * Mathf.Sin(tl * 13.1f);
        if (tl >= PlanetfallTimeline.SettleFrom) pos = Vector3.Lerp(pos, HandBack(shipFrom.z), PlanetfallTimeline.Settle01(tl));
        if (ship != null) ship.position = pos;

        // ---- the entry ----
        float sa = PlanetfallTimeline.ShroudAlpha(tl);
        shroud.enabled = sa > .002f;
        if (shroud.enabled)
        {
            shroud.sprite = art.Entry[Mathf.FloorToInt(tl * ShroudFps) % art.Entry.Length];
            shroud.sortingOrder = HullOrder() - 2;
            shroud.transform.position = pos + new Vector3(.025f * Mathf.Sin(tl * 37f), 0f, 0f);
            shroud.transform.localScale = new Vector3(ShroudWidth, ShroudWidth * (1f + .035f * Mathf.Sin(tl * 23f)), 1f);
            shroud.color = new Color(1f, 1f, 1f, sa);
        }

        float qw = 2f * halfW * Overscan, qh = 2f * halfH * Overscan;
        float stripW = 2f * halfW * StreakShare;
        streakScroll += dt * StreakFlow;
        float sk = PlanetfallTimeline.StreakAlpha(tl);
        Streak(streakL, streakLMat, -1f, stripW, qh, sk);
        Streak(streakR, streakRMat, 1f, stripW, qh, sk);

        Color heat = Color.Lerp(def.heat, def.cold, PlanetfallTimeline.Coolness(tl));
        heat.a = PlanetfallTimeline.TintAt(tl);
        Quad(tint, qw, qh, heat);
        Color shade = def.shade;
        shade.a = PlanetfallTimeline.VignetteAt(tl);
        Quad(vignette, qw, qh, shade);

        // ---- the clouds ----
        float zoom = PlanetfallTimeline.DeckZoom(tl);
        float rush = 1f + 2f * PlanetfallTimeline.Ramp(tl, PlanetfallTimeline.BreakAt, PlanetfallTimeline.CloudsOutTo);
        darkScroll += dt * DarkDeckFlow * rush;
        deckScroll += dt * DeckFlow * rush;
        deckDrift += dt * .015f;
        Deck(dark, darkMat, art.DeckDarkTex, DarkDeckPixels * zoom, darkScroll, -deckDrift, qw, qh,
             PlanetfallTimeline.DarkDeckAlpha(tl));
        Deck(deck, deckMat, art.DeckTex, DeckPixels * zoom, deckScroll, deckDrift, qw, qh,
             PlanetfallTimeline.DeckAlphaAt(tl));

        // ---- the break ----
        int bf = PlanetfallTimeline.BurstFrame(tl, art.Burst.Length);
        if (bf < 0 || tl < PlanetfallTimeline.BreakAt) burstAt = pos;
        burst.enabled = bf >= 0;
        if (burst.enabled)
        {
            burst.sprite = art.Burst[bf];
            float grow = 1f + .3f * PlanetfallTimeline.Ramp(tl, PlanetfallTimeline.BreakAt, PlanetfallTimeline.BreakAt + .8f);
            burst.transform.position = burstAt;
            burst.transform.localScale = Vector3.one * (BurstWidth * grow);
            burst.color = new Color(1f, 1f, 1f, PlanetfallTimeline.BurstAlpha(tl, art.Burst.Length));
        }
        Color fl = def.flash;
        fl.a = PlanetfallTimeline.Flash(tl);
        Quad(flash, qw, qh, fl);

        backdropBoost = PlanetfallTimeline.Boost(tl);
        shake = PlanetfallTimeline.Shake(tl);

        // ---- the world changes, unseen ----
        if (!switched && tl >= PlanetfallTimeline.SwitchAt) Switch();
        if (switched && !bannered && tl >= PlanetfallTimeline.BannerAt)
        {
            bannered = true;
            if (!string.IsNullOrEmpty(banner)) WorldBanner.Show(banner);
        }
        if (tl >= PlanetfallTimeline.Seconds) Finish();
    }

    // The planet's horizon radius at the horizon view, world units.
    float ArcRadius()
    {
        float limbW = 2f * halfW * LimbWidthShare;
        return def.LimbArcPx(art.LimbTex.width) / art.LimbTex.width * limbW;
    }

    // Places the surface: `apex` is the top of the disc (and of the limb's
    // horizon), `scale` 1 is the horizon view. Disc and limb share the one
    // circle, so the cross-fade between them never jumps.
    void SetGroup(Vector3 apex, float scale)
    {
        float limbW = 2f * halfW * LimbWidthShare;
        float ra = def.LimbArcPx(art.LimbTex.width) / art.LimbTex.width * limbW;
        group.position = apex;
        group.localScale = Vector3.one * scale;
        planet.transform.localPosition = new Vector3(0f, -ra, 0f);
        planet.transform.localScale = Vector3.one * (ra / def.planetDiscPx * art.PlanetTex.width);
        rim.transform.localPosition = new Vector3(0f, -ra, 0f);
        float rimW = 2f * ra * 1.015f / PlanetfallArt.RingRadius;
        rim.transform.localScale = Vector3.one * rimW;
        limb.transform.localPosition = Vector3.zero;
        limb.transform.localScale = Vector3.one * limbW;
    }

    static void Show(SpriteRenderer sr, float alpha)
    {
        sr.enabled = alpha > .002f;
        if (sr.enabled) sr.color = new Color(1f, 1f, 1f, alpha);
    }

    void Quad(SpriteRenderer sr, float w, float h, Color c)
    {
        sr.enabled = c.a > .002f;
        if (!sr.enabled) return;
        sr.transform.position = centre;
        sr.transform.localScale = new Vector3(w, h * sr.sprite.rect.width / sr.sprite.rect.height, 1f);
        sr.color = c;
    }

    // A cloud deck: one quad over the view, its art tiled `pixel` world units
    // a pixel, scrolled `scroll` tiles down and zoomed about the view's centre.
    void Deck(SpriteRenderer sr, Material m, Texture2D tex, float pixel, float scroll, float drift,
              float w, float h, float alpha)
    {
        sr.enabled = alpha > .002f;
        if (!sr.enabled) return;
        sr.transform.position = centre;
        sr.transform.localScale = new Vector3(w, h * tex.width / tex.height, 1f);   // the sprite is 1 x (h/w) units
        float tileW = tex.width * pixel, tileH = tex.height * pixel;
        float sx = w / tileW, sy = h / tileH;
        m.SetVector(UvId, new Vector4(sx, sy, .5f - .5f * sx + drift, .5f - .5f * sy + scroll));
        sr.color = new Color(1f, 1f, 1f, alpha);
    }

    // A strip of speed lines down one side of the view (side -1 left, +1
    // right), cut from that side of the streak art.
    void Streak(SpriteRenderer sr, Material m, float side, float w, float h, float alpha)
    {
        sr.enabled = alpha > .002f;
        if (!sr.enabled) return;
        var tex = art.StreaksTex;
        sr.transform.position = new Vector3(centre.x + side * (halfW * Overscan - w * .5f), centre.y, 0f);
        sr.transform.localScale = new Vector3(w, h * tex.width / tex.height, 1f);
        float share = w / (2f * halfW);             // the art spans the view's width ...
        float tileH = 2f * halfW * tex.height / tex.width;
        float sy = h / tileH;
        m.SetVector(UvId, new Vector4(share, sy, side < 0f ? 0f : 1f - share, streakScroll));
        Color c = Color.Lerp(def.cue, Color.white, .35f);
        c.a = alpha * StreakStrength;
        sr.color = c;
    }

    int HullOrder()
    {
        return hull != null ? hull.sortingOrder : Raise;
    }

    // Where the ship goes when control returns: under the finger, as
    // movePlayer would put it (so the first steered frame doesn't jump), or
    // its start on the centre line.
    Vector3 HandBack(float z)
    {
        if (TouchInput.IsPressed && cam != null)
        {
            Vector2 sp = TouchInput.Position;
            Vector3 w = cam.ScreenToWorldPoint(new Vector3(sp.x, sp.y, 0f)) - shakeApplied;
            return new Vector3(ShipReach.ClampX(w.x), movePlayer.ClampPlayerY(w.y + ShipReach.FingerOffset), z);
        }
        return new Vector3(centre.x, ShipReach.StartY, z);
    }

    // The clouds cover the whole view: on to the next world, exactly as
    // through a portal (its banner waits for the clouds to clear).
    void Switch()
    {
        switched = true;
        var wm = WorldManager.Instance;
        if (wm == null) return;
        try { banner = wm.Advance(false); }
        catch (System.Exception e) { Debug.LogException(e); }   // never strand the ship in the clouds
    }

    void Finish()
    {
        if (!switched) Switch();
        state = Stage.Done;
        if (ship != null) ship.position = HandBack(shipFrom.z);
        Teardown();
        BossUtil.Kill(gameObject);
    }

    void OnDestroy() { Teardown(); }

    void Teardown()
    {
        if (Live == this) Live = null;
        UndoShake();
        Lift(false);
        backdropBoost = 1f;
        if (stage != null) BossUtil.Kill(stage.gameObject);
        stage = null;
        foreach (var m in new[] { limbMat, darkMat, deckMat, streakLMat, streakRMat }) if (m != null) BossUtil.Kill(m);
        limbMat = darkMat = deckMat = streakLMat = streakRMat = null;
        if (art != null) art.Release();
        art = null;
    }
}
