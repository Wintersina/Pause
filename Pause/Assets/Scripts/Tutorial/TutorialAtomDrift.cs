using UnityEngine;

// How a tutorial atom moves: it drops in from just above the top edge at a
// steady, readable speed, eases into a hover in the ship's lane (the lower
// part of the screen) with a gentle bob, and if the player still hasn't
// grabbed it after a few seconds it slowly drifts toward the ship's x. It
// never leaves the screen, so the "keep it coming until caught" loop in
// spawnGoodStuffTut can't be left waiting on an atom nobody can see.
//
// Why it exists: tutorial atoms used to ride the shared world scroller
// (moveItemEnmInStrightLine: y -= moveBackGround.speed * 30 * dt). tutorialS5
// starts moveBackGround at speed 0 and only ramps it 0.0023/s while a
// finger is down, so in the first half minute an atom crept down at a few
// hundredths of a unit per second -- and it spawned at y 6.57, above the
// camera's top edge (5). It sat there, invisible and out of the ship's reach
// (the ship tops out at y 4.5), while the spawner waited for it to be caught.
//
// Only atoms spawned by spawnGoodStuffTut get this; spawnAtom switches their
// world scroller off. Real-game atoms are untouched.
//
// It moves on scaled time, and only while the world is moving (the same gate
// the world scroller uses), so letting go freezes it with everything else --
// that freeze is what the tutorial is teaching.
public class TutorialAtomDrift : MonoBehaviour
{
    // ---- Tuning (world units, seconds) ----

    // Descent speed floor, independent of the near-zero tutorial world speed.
    public const float FloorSpeed = 3.6f;
    // Slowest it moves while easing into the hover, so it never stalls short.
    public const float MinApproachSpeed = .7f;
    // Starts slowing down this far above the hover line.
    public const float EaseDistance = 1.6f;
    // Spawn this far above the camera's top edge (just out of view).
    public const float SpawnAboveTop = .45f;
    // Hover line, as a fraction of the camera's height from its bottom edge.
    public const float HoverFraction = .3f;
    // Hover never goes lower than this fraction (keeps it clear of the edge).
    public const float MinHoverFraction = .2f;
    // Hover sits this far above the ship when the ship is in the lower half.
    public const float AboveShip = .9f;
    public const float BobAmplitude = .12f;
    public const float BobHz = .55f;
    // After hovering this long uncaught, it drifts toward the ship's x.
    public const float LingerSeconds = 3.5f;
    public const float DriftTowardShipSpeed = .6f;

    // The play lane, the same one AtomSpin clamps atoms into.
    static float LaneHalfWidth => RailInset.Lane(2.2f);

    float hoverY;
    bool hoverSet;
    float hoverClock;     // time since it reached the hover line
    float bobWeight;      // eases the bob in so arrival doesn't jump
    bool arrived;
    Transform ship;

    public bool Arrived { get { return arrived; } }
    public float HoverY { get { return hoverY; } }

    // Puts the atom just above the camera's top edge at x and turns off the
    // world scroller it came with. Returns the component for chaining/tests.
    public static TutorialAtomDrift AddTo(GameObject atom, float x)
    {
        if (atom == null) return null;
        var scroller = atom.GetComponent<moveItemEnmInStrightLine>();
        if (scroller != null) scroller.enabled = false;

        var drift = atom.GetComponent<TutorialAtomDrift>();
        if (drift == null) drift = atom.AddComponent<TutorialAtomDrift>();

        float bottom, top;
        View(out bottom, out top);
        var p = atom.transform.position;
        atom.transform.position = new Vector3(Mathf.Clamp(x, -LaneHalfWidth, LaneHalfWidth), top + SpawnAboveTop, p.z);
        return drift;
    }

    // Visible world-space y range of the main camera (falls back to the
    // tutorial's size-5 camera at y 0).
    public static void View(out float bottom, out float top)
    {
        var cam = Camera.main;
        float half = cam != null && cam.orthographic ? cam.orthographicSize : 5f;
        float cy = cam != null ? cam.transform.position.y : 0f;
        bottom = cy - half;
        top = cy + half;
    }

    // Where the atom settles: around the ship's lane in the lower part of the
    // screen, always inside the bottom 40% of the view.
    public static float HoverLine(float bottom, float top, float shipY, bool hasShip)
    {
        float h = top - bottom;
        float y = bottom + HoverFraction * h;
        if (hasShip && shipY < bottom + .5f * h) y = Mathf.Max(y, shipY + AboveShip);
        return Mathf.Clamp(y, bottom + MinHoverFraction * h, bottom + .36f * h);
    }

    static bool WorldMoving()
    {
        return (TouchInput.IsPressed || score.pauseCounter <= 0) && !buttonClicks.playerDied;
    }

    void Update()
    {
        Step(Time.deltaTime, WorldMoving());
    }

    // One frame of movement. dt is scaled time, so at timeScale 0 nothing
    // moves; worldMoving mirrors the world scroller's own gate.
    public void Step(float dt, bool worldMoving)
    {
        if (!worldMoving || dt <= 0f) return;

        float bottom, top;
        View(out bottom, out top);
        if (ship == null)
        {
            var tutShip = FindFirstObjectByType<movePlayerInTut>();
            if (tutShip != null) ship = tutShip.transform;
        }
        bool hasShip = ship != null;
        float shipX = hasShip ? ship.position.x : 0f;
        float shipY = hasShip ? ship.position.y : bottom;
        // Fixed once, from where the ship is when the atom first moves: a
        // line that followed the ship would back away as the ship rises to
        // meet it.
        if (!hoverSet)
        {
            hoverSet = true;
            hoverY = HoverLine(bottom, top, shipY, hasShip);
        }
        var p = transform.position;

        if (!arrived)
        {
            // Steady descent, slowing over the last EaseDistance; never
            // slower than the world itself scrolls.
            float dist = p.y - hoverY;
            float world = Mathf.Max(0f, moveBackGround.speed) * 30f;
            float v = Mathf.Max(FloorSpeed, world) * Mathf.Clamp01(dist / EaseDistance);
            v = Mathf.Max(v, MinApproachSpeed);
            p.y = Mathf.Max(hoverY, p.y - v * dt);
            if (p.y <= hoverY + .001f) arrived = true;
        }
        else
        {
            hoverClock += dt;
            bobWeight = Mathf.Min(1f, bobWeight + dt / .6f);
            p.y = hoverY + bobWeight * BobAmplitude * Mathf.Sin(2f * Mathf.PI * BobHz * hoverClock);

            if (hasShip && hoverClock >= LingerSeconds)
                p.x = Mathf.MoveTowards(p.x, Mathf.Clamp(shipX, -LaneHalfWidth, LaneHalfWidth), DriftTowardShipSpeed * dt);
        }

        // Never below the bottom edge, even if the view changes under it.
        p.y = Mathf.Max(p.y, bottom + .3f);
        transform.position = p;
    }
}
