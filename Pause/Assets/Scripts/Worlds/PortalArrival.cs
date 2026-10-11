using System.Collections.Generic;
using UnityEngine;

// When everything in a portal arrival happens, in seconds after it opens:
// pure functions of that clock, so the tests and the preview read the very
// moments the game plays.
//
//    0.00  OPEN     the portal opens where the ship will be: the vortex
//                   swells from nothing, spinning, its frames playing
//    0.50  EMERGE   the ship comes out of it toward the viewer, small at
//                   first and growing to full size, settling onto the pilot's
//                   finger (or its start); a shock ring leaves the portal and
//                   the stars streak for a moment
//    1.75  CLOSE    the vortex spins back down (its frames playing in
//                   reverse) and shuts
//    2.60  RELEASE  control returns; the level's calm start begins
public static class PortalArrivalTimeline
{
    public static float OpenFrom = 0f, OpenTo = .65f;
    public static float EmergeFrom = .5f, EmergeTo = 1.6f;
    public static float CloseFrom = 1.75f, CloseTo = 2.4f;
    public static float RingAt = 1.0f, RingSeconds = .6f;
    public static float Seconds = 2.6f;
    // The ship's scale when it first shows, and how far it overshoots on the way.
    public static float ShipFromScale = .06f, Overshoot = .08f;
    // The backdrop's scroll boost at the emergence's peak.
    public static float BackdropBoost = 3f;

    public static float Ramp(float t, float a, float b)
    {
        if (b <= a) return t >= b ? 1f : 0f;
        float x = Mathf.Clamp01((t - a) / (b - a));
        return x * x * (3f - 2f * x);
    }

    // The vortex's size (1 fully open): grows with a little overshoot, shrinks to nothing.
    public static float PortalScale(float t)
    {
        float open = Mathf.Clamp01((t - OpenFrom) / Mathf.Max(.01f, OpenTo - OpenFrom));
        float grow = 1f + 2.2f * Mathf.Pow(open - 1f, 3f) + 1.2f * Mathf.Pow(open - 1f, 2f);   // easeOutBack: overshoots, ends at 1
        grow = open >= 1f ? 1f : Mathf.Max(0f, grow);
        float shut = 1f - Ramp(t, CloseFrom, CloseTo);
        return grow * shut;
    }

    public static float PortalAlpha(float t) { return Mathf.Clamp01(Ramp(t, OpenFrom, OpenFrom + .3f) * (1f - Ramp(t, CloseTo - .25f, CloseTo))); }

    // 0 hidden in the vortex .. 1 out and settled.
    public static float Emerge01(float t) { return Ramp(t, EmergeFrom, EmergeTo); }

    // The ship's scale as a share of its own: tiny, then past full size, then full.
    public static float ShipScale(float t)
    {
        if (t < EmergeFrom) return ShipFromScale;
        float k = Emerge01(t);
        float grow = Mathf.Lerp(ShipFromScale, 1f, 1f - (1f - k) * (1f - k));
        return grow + Overshoot * Mathf.Sin(k * Mathf.PI) * k;
    }

    public static float RingProgress(float t) { return Mathf.Clamp01((t - RingAt) / Mathf.Max(.01f, RingSeconds)); }

    public static float Boost(float t)
    {
        if (t >= EmergeTo) return 1f;
        return 1f + (BackdropBoost - 1f) * Mathf.Sin(Ramp(t, EmergeFrom, EmergeTo) * Mathf.PI);
    }

    // Which authored frame to show: forward while opening and open, backward while it closes.
    public static float FrameClock(float t)
    {
        return t < CloseFrom ? t : CloseFrom - (t - CloseFrom) * 1.5f;
    }
}

// The portal ARRIVAL: how a run in Space begins (WorldEntry), the same
// gateway the worlds are changed through (Portal) playing the other way
// round: it opens at the ship's start, the ship flies out of it and it
// closes. It is the Space counterpart of the planetfall a planet run begins
// with, and the one a first real run after the tutorial begins with too.
//
// Built from the portal's own authored frames (TeleportPortalSprites) in
// the colour of the world the gateway belongs to; no new art. It runs on
// the world's clock like every transition (WorldManager.Flying: a lifted
// finger freezes it) and, while it plays:
//   - the ship is held and shielded (movePlayer, collisionDetection), a
//     press is free (score), nothing spawns (enmiesOnBoard, spawnGoodStuff)
//   - the weapon charge is frozen (WorldTransition.InProgress)
//   - the level clock, the speed ramp, the score and the dust do not run
//     (WorldEntry.Active), and the calm start's window is held back
//     (enmiesOnBoard) so the full eight seconds are still ahead at release.
// Every renderer is built when it opens and reused; a frame allocates nothing.
public class PortalArrival : MonoBehaviour
{
    public enum Stage { Playing, Done }

    public static PortalArrival Live { get; private set; }
    public static bool Active { get { return Live != null && Live.state != Stage.Done; } }

    // ---- hooks for shared code (as Planetfall's) ----
    public static bool HoldsShip { get { return Active; } }
    public static bool ShieldsShip { get { return Active; } }
    public static bool SuspendsSpawning { get { return Active; } }
    public static bool FreePress { get { return Active; } }

    // Portal layers: over the rails, under the (raised) ship.
    public const int PortalOrder = 50, RingOrder = 54;
    public const int Raise = 300;
    public const float MaxStep = .1f;
    // The vortex's size at full, as a share of its authored sprite size.
    public static float PortalSize = 1.25f;
    // The ship comes out this far above the portal's middle (world units) and settles.
    public static float EmergeRise = .0f;

    Stage state;
    float t;
    Transform ship;
    Vector3 shipScale0, centre;
    Color color;
    SpriteRenderer ring, core, sparks, wave;
    readonly List<Renderer> lifted = new List<Renderer>(32);
    Camera cam;
    float backdropBoost = 1f;

    public Stage State { get { return state; } }
    public float Seconds { get { return t; } }
    public Transform Ship { get { return ship; } }
    // The ship's own (full-size) scale, which the arrival grows it back to.
    public Vector3 RestScale { get { return shipScale0; } }
    public Vector3 Centre { get { return centre; } }
    public float BackdropBoost { get { return backdropBoost; } }
    public SpriteRenderer RingRenderer { get { return ring; } }
    public SpriteRenderer CoreRenderer { get { return core; } }
    public SpriteRenderer SparksRenderer { get { return sparks; } }
    public SpriteRenderer WaveRenderer { get { return wave; } }

    // WorldEntry: the portal opens at the ship's start. Null: no ship.
    // The same gateway played backwards: it opens ahead of the ship, the ship
    // flies into it (shrinking away) and it closes. How the tutorial's LIFT
    // OFF leaves for the first level (TutorialLiftOff); the run then begins
    // with the usual arrival. It is driven from outside (Step) and takes
    // no part in the run-start hand-off (Live / Active stay untouched).
    public static PortalArrival SpawnDeparture(Transform shipTransform, Color portalColor, Vector3 portalAt)
    {
        if (shipTransform == null) return null;
        var go = new GameObject("~PortalDeparture");
        var p = go.AddComponent<PortalArrival>();
        p.departing = true;
        p.ship = shipTransform;
        p.color = portalColor;
        p.cam = Camera.main;
        p.shipScale0 = shipTransform.localScale;
        p.shipFrom = shipTransform.position;
        p.centre = portalAt;
        go.transform.position = p.centre;
        p.Build();
        p.Lift(true);
        p.Apply();
        return p;
    }

    bool departing;
    Vector3 shipFrom;
    public bool Departing { get { return departing; } }

    public static PortalArrival Spawn(Transform shipTransform, Color portalColor)
    {
        if (shipTransform == null) return null;
        if (Live != null) BossUtil.Kill(Live.gameObject);
        var go = new GameObject("~PortalArrival");
        var p = go.AddComponent<PortalArrival>();
        p.ship = shipTransform;
        p.color = portalColor;
        p.cam = Camera.main;
        p.shipScale0 = shipTransform.localScale;
        p.centre = Planetfall.HandBack(p.cam, p.ViewCentre(), Vector3.zero, shipTransform.position.z);
        go.transform.position = p.centre;
        p.Build();
        p.Lift(true);
        Live = p;
        p.Apply();
        return p;
    }

    Vector3 ViewCentre()
    {
        return cam != null ? new Vector3(cam.transform.position.x, cam.transform.position.y, 0f) : Vector3.zero;
    }

    void Build()
    {
        ring = Part("PortalVortex", TeleportPortalSprites.FrameAt(0), color, PortalOrder);
        core = Part("PortalCore", TeleportPortalSprites.FrameAt(4), new Color(.62f, .9f, 1f, .72f), PortalOrder + 1);
        sparks = Part("PortalSparks", TeleportPortalSprites.FrameAt(8), new Color(.9f, .48f, 1f, .36f), PortalOrder + 2);
        wave = Part("PortalWave", PortalArt.Ring(), color, RingOrder);
    }

    SpriteRenderer Part(string name, Sprite sprite, Color c, int order)
    {
        var go = new GameObject(name);
        go.transform.SetParent(transform, false);
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = sprite;
        sr.color = c;
        sr.sortingOrder = order;
        sr.enabled = false;
        return sr;
    }

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

    void Update()
    {
        // Only while the world is actually moving: a paused game holds it.
        if (!departing && WorldManager.Flying) Step(Time.deltaTime);
    }

    // One running frame. Public so edit-mode tests and the preview can step
    // it (Time.deltaTime is 0 there).
    public void Step(float dt)
    {
        if (state == Stage.Done || dt <= 0f) return;
        t += Mathf.Min(dt, MaxStep);
        Apply();
        if (t >= PortalArrivalTimeline.Seconds) Finish();
    }

    void Apply()
    {
        if (cam == null) cam = Camera.main;
        float tl = departing ? PortalArrivalTimeline.Seconds - t : t;
        float scale = PortalArrivalTimeline.PortalScale(tl);
        float alpha = PortalArrivalTimeline.PortalAlpha(tl);
        float spin = tl * 90f;
        int frame = Mathf.FloorToInt(PortalArrivalTimeline.FrameClock(tl) * 15f);
        float size = PortalSize * scale;

        bool on = alpha > .002f && scale > .002f;
        ring.enabled = core.enabled = sparks.enabled = on;
        if (on)
        {
            ring.sprite = TeleportPortalSprites.FrameAt(frame);
            ring.transform.localScale = Vector3.one * size;
            ring.transform.localRotation = Quaternion.Euler(0f, 0f, spin);
            Color rc = color; rc.a = alpha; ring.color = rc;

            core.sprite = TeleportPortalSprites.FrameAt(frame + 5);
            float pulse = .5f + Mathf.PingPong(tl * .9f, .25f);
            core.transform.localScale = Vector3.one * (size * pulse * 1.3f);
            core.transform.localRotation = Quaternion.Euler(0f, 0f, -spin * .6f);
            core.color = new Color(.62f, .9f, 1f, .72f * alpha);

            sparks.sprite = TeleportPortalSprites.FrameAt(frame + 10);
            sparks.transform.localScale = Vector3.one * (size * 1.14f);
            sparks.transform.localRotation = Quaternion.Euler(0f, 0f, spin * 1.45f);
            sparks.color = new Color(.9f, .48f, 1f, .36f * alpha);
        }

        float k = PortalArrivalTimeline.RingProgress(tl);
        wave.enabled = k > 0f && k < 1f;
        if (wave.enabled)
        {
            wave.transform.localScale = Vector3.one * (PortalSize * Mathf.Lerp(.7f, 2.2f, k));
            Color wc = Color.Lerp(color, Color.white, .5f); wc.a = .6f * (1f - k);
            wave.color = wc;
        }

        // the ship: out of the vortex toward the viewer, onto the pilot's finger
        if (ship != null)
        {
            float e = PortalArrivalTimeline.Emerge01(tl);
            Vector3 to = departing ? shipFrom : Planetfall.HandBack(cam, ViewCentre(), Vector3.zero, ship.position.z);
            ship.position = Vector3.Lerp(centre + new Vector3(0f, EmergeRise, 0f), to, e);
            ship.localScale = shipScale0 * PortalArrivalTimeline.ShipScale(tl);
        }
        backdropBoost = PortalArrivalTimeline.Boost(tl);
    }

    void Finish()
    {
        state = Stage.Done;
        if (departing)
        {
            // the ship went in: it stays gone, the scene is about to change
            if (ship != null) ship.localScale = shipScale0 * PortalArrivalTimeline.ShipFromScale * .5f;
            Lift(false);
            BossUtil.Kill(gameObject);
            return;
        }
        if (ship != null)
        {
            ship.localScale = shipScale0;
            ship.position = Planetfall.HandBack(cam, ViewCentre(), Vector3.zero, ship.position.z);
        }
        Teardown();
        BossUtil.Kill(gameObject);
    }

    void OnDestroy() { Teardown(); }

    void Teardown()
    {
        if (Live == this) Live = null;
        if (ship != null && state != Stage.Done) ship.localScale = shipScale0;   // killed mid-way: the ship goes back to full size
        Lift(false);
        backdropBoost = 1f;
    }
}
