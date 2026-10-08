using System.Collections.Generic;
using UnityEngine;

// Lift-off: the way off a planet that has one (LiftoffCatalog: Frost), the
// planetfall flown backwards. WorldManager starts it where it would open a
// portal, after the boss: the stage is Portal (the level clock is stopped)
// but nothing presses -- PortalPressure only opens with the gateway.
//
//   BEAT       a moment for the boss's wreck; nothing new spawns
//   RISE       LIFT OFF: input is taken over and the sequence plays
//              (LiftoffTimeline has every moment): the thrusters flare and
//              the ground rushes away, the ship climbs through the planet's
//              two cloud decks under its plasma, and while the clouds hide
//              the view the backdrop and rails become the space between
//              planets (Space's). The decks break and fall away, the
//              planet's horizon lies below the ship and recedes into the
//              whole globe; the ship settles under the pilot's finger and
//              control returns. Nothing can hurt the ship (ShieldsShip),
//              the board was cleared at the take; score, hearts, pickups
//              and the run carry through untouched.
//   INTERLUDE  calm space: the pilot flies, nothing spawns (the Space
//              backdrop holds back its stations and rocks, SpaceDirector.
//              Quiet), the planet sinks away behind. Then the gateway:
//              WorldManager.OpenGateway -- the next planet's planetfall if
//              it has one, else the portal, with its pressure, exactly as
//              after any other boss. The world itself changes only there.
//
// Clock: like the planetfall it moves only while the world is running
// (WorldManager.Flying), on Time.deltaTime, so a lifted finger freezes it
// and a press during the rise never spends a pause (FreePress). Every
// renderer is built at the start and reused; a frame allocates nothing.
// It draws with the planet's planetfall art and Planetfall's own layers
// and helpers; only the plume is its own.
public class Liftoff : MonoBehaviour
{
    // ---- tunables (the timing is LiftoffTimeline's) ----
    // The ship's climb spot, a share of the view's height from the bottom,
    // at the take and at the break.
    public static float ClimbHeight = .3f, BreakHeight = .4f;
    // The planet below: the limb's scale (1 the landing's horizon view) and
    // its horizon's top (a share of the half-height from the view's centre)
    // at the break and at the horizon view; then the globe's radius (world
    // units) and its centre's height (share of the half-height) when sunk.
    public static float BreakScale = 1.7f, BreakApex = -.15f, HorizonApex = -.5f;
    public static float GlobeRadius = 1.15f, GlobeCentre = -.88f;
    // The thrusters' plume: width in hull widths, length in hull widths at
    // idle and full thrust; and its flicker.
    public static float PlumeWidth = .55f, PlumeIdle = .9f, PlumeFull = 2.4f, PlumeFlicker = .12f;
    // Cloud scroll, tiles a second at rush 1 (the landing's flows).
    public static float DeckFlow = .55f, DarkDeckFlow = .28f;

    public const float MaxStep = Planetfall.MaxStep;
    public const int PlumeOrderBelowHull = 1;

    public enum Stage { Beat, Rise, Interlude, Done }

    // The lift-off in progress, if any (one at a time).
    public static Liftoff Live { get; private set; }

    // ---- hooks for shared code (alongside Planetfall's) ----
    public static bool HoldsShip { get { return Live != null && Live.state == Stage.Rise; } }
    public static bool ShieldsShip { get { return HoldsShip; } }
    // From the boss's end to the gateway: nothing new arrives.
    public static bool SuspendsSpawning { get { return Live != null && Live.state != Stage.Done; } }
    public static bool FreePress { get { return HoldsShip; } }

    // ---- state ----
    LiftoffDef def;
    PlanetfallDef pdef;
    PlanetfallArt art;
    Stage state;
    float t;
    bool swapped, gatewayOpened, failed;
    Transform ship;
    SpriteRenderer hull;
    Vector3 shipFrom, burstAt;
    float shipSpan, shroudScale, hullHeight;
    Vector3 shipMid;
    float deckScroll, darkScroll, streakScroll, deckDrift;
    float shake, backdropBoost = 1f;
    Vector3 shakeApplied;
    Camera shakeCam, cam;

    Vector3 centre;
    float halfW, halfH;

    Transform stage, group;
    SpriteRenderer planet, rim, limb, dark, deck, streakL, streakR, tint, vignette, flash, shroud, shroudGlow, burst, plume;
    Material limbMat, darkMat, deckMat, streakLMat, streakRMat, glowMat, plumeMat;
    readonly List<Renderer> lifted = new List<Renderer>(32);

    static readonly int FadeId = Shader.PropertyToID("_Fade");

    public Stage State { get { return state; } }
    public LiftoffDef Def { get { return def; } }
    public PlanetfallArt Art { get { return art; } }
    public float Seconds { get { return t; } }
    public bool Swapped { get { return swapped; } }
    public bool GatewayOpened { get { return gatewayOpened; } }
    public Transform Ship { get { return ship; } }
    public float ShakeNow { get { return shake; } }
    public float BackdropBoost { get { return backdropBoost; } }

    // Renderers, for the tests and the preview.
    public SpriteRenderer PlanetRenderer { get { return planet; } }
    public SpriteRenderer LimbRenderer { get { return limb; } }
    public SpriteRenderer DarkDeckRenderer { get { return dark; } }
    public SpriteRenderer DeckRenderer { get { return deck; } }
    public SpriteRenderer ShroudRenderer { get { return shroud; } }
    public SpriteRenderer PlumeRenderer { get { return plume; } }
    public SpriteRenderer BurstRenderer { get { return burst; } }
    public SpriteRenderer FlashRenderer { get { return flash; } }

    // WorldManager.OpenPortal: the boss is over. Null (and nothing built)
    // when the art is missing; the caller opens the gateway straight away.
    public static Liftoff Spawn(LiftoffDef def)
    {
        if (def == null || def.planet == null) return null;
        var art = PlanetfallArt.Load(def.planet);
        var shader = Resources.Load<Shader>(Planetfall.ShaderPath);
        if (!art.Complete || shader == null)
        {
            Debug.LogWarning("[Liftoff] art or shader missing for world " + def.world + "; opening the gateway instead");
            art.Release();
            return null;
        }
        if (Live != null) BossUtil.Kill(Live.gameObject);
        var go = new GameObject("~Liftoff");
        var l = go.AddComponent<Liftoff>();
        l.def = def;
        l.pdef = def.planet;
        l.art = art;
        l.Build(shader);
        Live = l;
        l.Step(0f, true);
        return l;
    }

    void Build(Shader shader)
    {
        cam = Camera.main;
        View();
        stage = new GameObject("~LiftoffStage").transform;
        group = new GameObject("Surface").transform;
        group.SetParent(stage, false);

        planet = Planetfall.Part("Planet", group, art.Planet, Planetfall.PlanetOrder, null);
        rim = Planetfall.Part("Rim", group, art.Ring, Planetfall.RimOrder, null);
        limbMat = Planetfall.Layer(shader, false);
        limb = Planetfall.Part("Limb", group, art.Limb, Planetfall.LimbOrder, limbMat);
        limbMat.SetVector(FadeId, new Vector4(0f, .3f, 0f, 0f));

        darkMat = Planetfall.Layer(shader, false);
        dark = Planetfall.Part("DeepClouds", stage, art.DeckDark, Planetfall.DarkDeckOrder, darkMat);
        deckMat = Planetfall.Layer(shader, false);
        deck = Planetfall.Part("Clouds", stage, art.Deck, Planetfall.DeckOrder, deckMat);
        streakLMat = Planetfall.Layer(shader, true);
        streakL = Planetfall.Part("StreaksL", stage, art.Streaks, Planetfall.StreakOrder, streakLMat);
        streakRMat = Planetfall.Layer(shader, true);
        streakR = Planetfall.Part("StreaksR", stage, art.Streaks, Planetfall.StreakOrder, streakRMat);
        tint = Planetfall.Part("Tint", stage, art.White, Planetfall.TintOrder, null);
        vignette = Planetfall.Part("Vignette", stage, art.Vignette, Planetfall.VignetteOrder, null);
        shroud = Planetfall.Part("Shroud", stage, art.Entry[0], 0, null);
        glowMat = Planetfall.Layer(shader, true);
        shroudGlow = Planetfall.Part("ShroudGlow", stage, art.Entry[0], 0, glowMat);
        plumeMat = Planetfall.Layer(shader, true);
        plume = Planetfall.Part("Plume", stage, art.Plume, 0, plumeMat);
        burst = Planetfall.Part("Burst", stage, art.Burst[0], Planetfall.BurstOrder, null);
        flash = Planetfall.Part("Flash", stage, art.White, Planetfall.FlashOrder, null);
    }

    void Update()
    {
        if (WorldManager.Flying) Step(Time.deltaTime);
    }

    void LateUpdate()
    {
        UndoShake();
        if (state != Stage.Rise || !WorldManager.Flying || shake < .002f) return;
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
        try
        {
            View();
            t += dt;
            if (state == Stage.Beat && t >= LiftoffTimeline.TakeAt) Take();
            Draw(dt);
            if (state == Stage.Rise && t >= LiftoffTimeline.ReleaseAt) Release();
            if (state == Stage.Interlude && t >= LiftoffTimeline.GatewayAt) Finish();
        }
        catch (System.Exception e)
        {
            // Never strand the ship: hand it back and open the way on.
            if (failed) return;
            failed = true;
            Debug.LogException(e);
            Finish();
        }
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

    // ---- the take -------------------------------------------------------------

    // The beat is over: LIFT OFF. The board goes, the ship is taken over.
    void Take()
    {
        state = Stage.Rise;
        var mover = Object.FindFirstObjectByType<movePlayer>();
        ship = mover != null ? mover.transform : null;
        shipFrom = ship != null ? ship.position : new Vector3(centre.x, ShipReach.StartY, 0f);
        hull = ship != null ? ship.GetComponent<SpriteRenderer>() : null;
        Planetfall.FitShroud(ship, hull, art, pdef, out shipSpan, out shipMid, out shroudScale);
        hullHeight = hull != null && hull.sprite != null ? Mathf.Abs(hull.sprite.bounds.size.y * hull.transform.lossyScale.y) : shipSpan;
        if (shipSpan <= 0f) shipSpan = .8f;
        if (hullHeight <= 0f) hullHeight = shipSpan;
        Planetfall.ClearBoard(cam);
        Lift(true);
        if (!string.IsNullOrEmpty(def.banner)) WorldBanner.Show(def.banner);
    }

    void Lift(bool up)
    {
        if (up)
        {
            lifted.Clear();
            if (ship == null) return;
            ship.GetComponentsInChildren(true, lifted);
            foreach (var r in lifted) r.sortingOrder += Planetfall.Raise;
        }
        else
        {
            foreach (var r in lifted) if (r != null) r.sortingOrder -= Planetfall.Raise;
            lifted.Clear();
        }
    }

    // ---- the frame ------------------------------------------------------------

    void Draw(float dt)
    {
        float tl = t;

        // ---- the ship ----
        Vector3 pos = ship != null ? ship.position : shipFrom;
        if (state == Stage.Rise)
        {
            float bottom = centre.y - halfH;
            float climbY = bottom + 2f * halfH * Mathf.Lerp(ClimbHeight, BreakHeight, LiftoffTimeline.Rise01(tl));
            Vector3 climb = new Vector3(centre.x, climbY, shipFrom.z);
            pos = Vector3.Lerp(shipFrom, climb, LiftoffTimeline.Steer01(tl));
            float buffet = LiftoffTimeline.Thrust(tl) * (1f - LiftoffTimeline.Ramp(tl, LiftoffTimeline.BreakAt, LiftoffTimeline.SettleTo));
            pos.x += buffet * .04f * Mathf.Sin(tl * 9.3f);
            pos.y += buffet * .03f * Mathf.Sin(tl * 13.1f);
            if (tl >= LiftoffTimeline.SettleFrom)
                pos = Vector3.Lerp(pos, Planetfall.HandBack(cam, centre, shakeApplied, shipFrom.z), LiftoffTimeline.Settle01(tl));
            if (ship != null) ship.position = pos;
        }
        int hullOrder = hull != null ? hull.sortingOrder : Planetfall.Raise;

        // ---- the thrusters ----
        float pa = state == Stage.Rise ? LiftoffTimeline.PlumeAlpha(tl) : 0f;
        plume.enabled = pa > .002f;
        if (plume.enabled)
        {
            float thrust = LiftoffTimeline.Thrust(tl);
            float flick = 1f + PlumeFlicker * (.6f * Mathf.Sin(tl * 47f) + .4f * Mathf.Sin(tl * 83f + .7f));
            float len = shipSpan * Mathf.Lerp(PlumeIdle, PlumeFull, thrust) * flick;
            float wid = shipSpan * PlumeWidth * (1f + .25f * thrust) * (2f - flick);
            plume.transform.position = pos + shipMid + new Vector3(0f, -.42f * hullHeight, 0f);
            plume.transform.localScale = new Vector3(wid, len * art.Plume.rect.width / art.Plume.rect.height, 1f);
            plume.sortingOrder = hullOrder - PlumeOrderBelowHull;
            Color pc = Color.Lerp(pdef.cue, Color.white, .45f);
            pc.a = pa * (.7f + .3f * thrust);
            plume.color = pc;
        }

        // ---- the climb ----
        float sa = state == Stage.Rise ? LiftoffTimeline.ShroudAt(tl) : 0f;
        float deep = LiftoffTimeline.Ramp(tl, LiftoffTimeline.ShroudInTo, LiftoffTimeline.BreakAt);
        Planetfall.DrawShroud(shroud, shroudGlow, art, pdef, tl, sa, deep, pos + shipMid, shroudScale, shipSpan, hullOrder);

        float qw = 2f * halfW * Planetfall.Overscan, qh = 2f * halfH * Planetfall.Overscan;
        float stripW = 2f * halfW * Planetfall.StreakShare;
        streakScroll += dt * Planetfall.StreakFlow * 1.4f;
        float sk = LiftoffTimeline.StreakAlpha(tl);
        Planetfall.Streak(streakL, streakLMat, art.StreaksTex, pdef.cue, centre, halfW, -1f, stripW, qh, streakScroll, sk);
        Planetfall.Streak(streakR, streakRMat, art.StreaksTex, pdef.cue, centre, halfW, 1f, stripW, qh, streakScroll, sk);

        Color heat = Color.Lerp(pdef.heat, pdef.cold, LiftoffTimeline.Coolness(tl));
        heat.a = LiftoffTimeline.TintAt(tl);
        Planetfall.Quad(tint, centre, qw, qh, heat);
        Color shade = pdef.shade;
        shade.a = LiftoffTimeline.VignetteAt(tl);
        Planetfall.Quad(vignette, centre, qw, qh, shade);

        // ---- the clouds: streaming down past the ship, falling away ----
        float zoom = LiftoffTimeline.DeckZoom(tl);
        float rush = LiftoffTimeline.DeckRush(tl);
        darkScroll += dt * DarkDeckFlow * rush;
        deckScroll += dt * DeckFlow * rush;
        deckDrift += dt * .015f;
        Planetfall.Deck(dark, darkMat, art.DeckDarkTex, Planetfall.DarkDeckPixels * zoom, darkScroll, -deckDrift, centre, qw, qh,
                        LiftoffTimeline.DarkDeckAlpha(tl));
        Planetfall.Deck(deck, deckMat, art.DeckTex, Planetfall.DeckPixels * zoom, deckScroll, deckDrift, centre, qw, qh,
                        LiftoffTimeline.DeckAlphaAt(tl));

        // ---- the break ----
        int bf = LiftoffTimeline.BurstFrame(tl, art.Burst.Length);
        if (bf < 0 || tl < LiftoffTimeline.BreakAt) burstAt = pos + shipMid;
        burst.enabled = bf >= 0;
        if (burst.enabled)
        {
            burst.sprite = art.Burst[bf];
            float grow = 1f + .3f * LiftoffTimeline.Ramp(tl, LiftoffTimeline.BreakAt, LiftoffTimeline.BreakAt + .8f);
            burst.transform.position = burstAt;
            burst.transform.localScale = Vector3.one * (Planetfall.BurstWidth * grow);
            burst.color = new Color(1f, 1f, 1f, LiftoffTimeline.BurstAlpha(tl, art.Burst.Length));
        }
        Color fl = pdef.flash;
        fl.a = LiftoffTimeline.Flash(tl);
        Planetfall.Quad(flash, centre, qw, qh, fl);

        // ---- the planet beneath ----
        Surface(tl);

        backdropBoost = LiftoffTimeline.Boost(tl);
        shake = state == Stage.Rise ? LiftoffTimeline.Shake(tl) : 0f;

        // ---- the backdrop becomes space, unseen ----
        if (!swapped && tl >= LiftoffTimeline.SwapAt) Swap();
    }

    // The horizon close below at the break, receding to the landing's
    // horizon view, then the whole globe shrinking and sinking away. Disc
    // and limb share one circle (Planetfall.PlaceSurface), so the
    // cross-fade between them never jumps.
    void Surface(float tl)
    {
        float alpha = LiftoffTimeline.SurfaceAlpha(tl);
        if (alpha <= .002f)
        {
            planet.enabled = limb.enabled = rim.enabled = false;
            return;
        }
        float ra = Planetfall.ArcRadius(pdef, art, halfW);
        float scale, apexY;
        if (tl < LiftoffTimeline.RecedeTo)
        {
            float f = LiftoffTimeline.Recede01(tl);
            scale = Mathf.Exp(Mathf.Lerp(Mathf.Log(BreakScale), 0f, f));
            apexY = centre.y + halfH * Mathf.Lerp(BreakApex, HorizonApex, f);
        }
        else
        {
            float g = LiftoffTimeline.Sink01(tl);
            float end = Mathf.Max(.05f, GlobeRadius / Mathf.Max(.01f, ra));
            scale = Mathf.Exp(Mathf.Lerp(0f, Mathf.Log(end), g));
            float endApex = centre.y + halfH * GlobeCentre + GlobeRadius;
            apexY = Mathf.Lerp(centre.y + halfH * HorizonApex, endApex, g);
        }
        Planetfall.PlaceSurface(group, planet, rim, limb, pdef, art, halfW, new Vector3(centre.x, apexY, 0f), scale);
        Planetfall.Show(planet, alpha);
        Planetfall.Show(limb, LiftoffTimeline.LimbAlpha(tl));
        Color rc = pdef.cue;
        rc.a = LiftoffTimeline.RimAlpha(tl);
        rim.color = rc;
        rim.enabled = rc.a > .002f;
    }

    // The clouds cover the whole view: the backdrop and the rails become
    // the interlude world's. The world itself (music, enemies, its index)
    // changes only at the gateway.
    void Swap()
    {
        swapped = true;
        try
        {
            var theme = WorldManager.Worlds[Mathf.Clamp(def.interludeWorld, 0, WorldManager.Worlds.Length - 1)];
            if (WorldBackdrop.Instance != null) WorldBackdrop.Instance.Show(theme.displayName, false);
            WorldPainter.Apply(theme);
            // the planet's backdrop is gone: let its textures go too
            if (Application.isPlaying) Resources.UnloadUnusedAssets();
        }
        catch (System.Exception e) { Debug.LogException(e); }
    }

    // Control returns; the interlude begins.
    void Release()
    {
        state = Stage.Interlude;
        if (ship != null) ship.position = Planetfall.HandBack(cam, centre, shakeApplied, shipFrom.z);
        Lift(false);
        UndoShake();
        shake = 0f;
        backdropBoost = 1f;
        plume.enabled = shroud.enabled = shroudGlow.enabled = false;
    }

    // The interlude is over: the way on opens, and the lift-off is gone.
    void Finish()
    {
        if (state == Stage.Rise) Release();
        if (!swapped) Swap();
        state = Stage.Done;
        Teardown();
        Gateway();
        BossUtil.Kill(gameObject);
    }

    void Gateway()
    {
        if (gatewayOpened) return;
        gatewayOpened = true;
        var wm = WorldManager.Instance;
        if (wm == null) return;
        try { wm.OpenGateway(); }
        catch (System.Exception e) { Debug.LogException(e); }
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
        foreach (var m in new[] { limbMat, darkMat, deckMat, streakLMat, streakRMat, glowMat, plumeMat }) if (m != null) BossUtil.Kill(m);
        limbMat = darkMat = deckMat = streakLMat = streakRMat = glowMat = plumeMat = null;
        if (art != null) art.Release();
        art = null;
    }
}
