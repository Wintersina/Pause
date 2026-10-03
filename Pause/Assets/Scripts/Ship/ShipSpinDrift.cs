using UnityEngine;

// Ninja and UFO turn continuously while advancing, so a nozzle plume would
// rotate round the hull and read as a broken flame. They get a spin drift
// instead (ShipExhaustStyle's SpinBlades / SpinVortex), in two layers:
//
//   Ring  centred on the hull and parented to it, so it turns in sync with
//         the hull spin: Ninja's blade-arc afterimages ride its four blade
//         tips, UFO's ring of light pulses orbits its rim.
//   Wake  hangs behind the ship, world-aligned: Ninja's curling shuriken cut
//         streaks, UFO's tractor-beam shimmer and vortex swirl.
//
// Both are flipbooks on scaled time (frozen at timeScale 0, like the hull and
// the plumes), with boost drawings while the boost holder is lit (blue atom)
// or `boost` is set (the dock launch). No allocation per frame.
public class ShipSpinDrift : MonoBehaviour
{
    public float degreesPerSecond = 300f;

    [Tooltip("Turn the hull itself (the flying player ship). False when " +
             "something else owns the hull's rotation (the dock, traffic).")]
    public bool spinHull = true;

    [Tooltip("Turn the ring on its own, for a hull that is not spinning " +
             "(title-screen traffic), so it still reads as a spinner.")]
    public bool spinRing;

    [Tooltip("Hang the wake behind the ship's own heading instead of " +
             "straight down the screen (traffic flies any direction).")]
    public bool wakeFollowsHeading;

    [Tooltip("Pilot rules: the drift spins up only while flying.")]
    public bool respondToPause = true;

    [Tooltip("False: no drift at all (a powered-down dock berth).")]
    public bool powered = true;

    [Tooltip("Force the boost drawings (the dock launch).")]
    public bool boost;

    [Tooltip("Wake length multiplier (the dock keeps it inside the berth).")]
    public float wakeScale = 1f;

    // Ring diameter and wake length relative to the hull's longest edge.
    public const float RingSize = 1.22f;
    // The UFO's beam-and-vortex column runs long; Ninja's cut streaks stay
    // close so the star still reads first.
    public static float WakeLength(int shipId)
    {
        return ShipExhaustStyle.For(shipId).kind == ExhaustKind.SpinBlades ? 1.15f : 1.45f;
    }
    public const float WakeOffset = .22f;     // wake head below the hull centre

    public int ShipIndex { get; private set; }
    public SpriteRenderer Ring { get { return ring; } }
    public SpriteRenderer Wake { get { return wake; } }
    public bool ShowingBoost { get { return shownBoost; } }

    Transform root, ringT, wakeT;
    SpriteRenderer ring, wake, hull;
    GameObject boostHolder;
    float ticks, ringAngle, wakeDrop;
    int shown = -1;
    bool shownBoost;

    void Start()
    {
        ticks = Random.value * 8f;
        Rebuild();
    }

    // (Re)reads the ship and builds or re-skins the two layers.
    public void Rebuild()
    {
        hull = GetComponent<SpriteRenderer>();
        ShipIndex = ShipExhaust.IndexFor(gameObject);
        boostHolder = null;
        foreach (Transform child in transform)
            if (child.CompareTag("boost") || child.name.StartsWith("Boost")) { boostHolder = child.gameObject; break; }

        var existing = transform.Find("~SpinDrift");
        root = existing != null ? existing : new GameObject("~SpinDrift").transform;
        if (existing == null) root.SetParent(transform, false);
        root.localPosition = Vector3.zero;
        root.localRotation = Quaternion.identity;
        root.localScale = Vector3.one;
        ring = Layer("Ring");
        wake = Layer("Wake");
        ringT = ring.transform;
        wakeT = wake.transform;

        shown = -1;
        if (!ShipExhaust.UsesSpinDrift(ShipIndex))
        {
            ring.enabled = wake.enabled = false;
            return;
        }
        var ringSprite = ShipExhaustStyle.Frame(ShipIndex, ExhaustLayer.Ring, 0);
        var wakeSprite = ShipExhaustStyle.Frame(ShipIndex, ExhaustLayer.Wake, 0);
        float extent = hull != null && hull.sprite != null
            ? Mathf.Max(hull.sprite.bounds.size.x, hull.sprite.bounds.size.y) : 1f;
        if (ringSprite != null)
            ringT.localScale = Vector3.one * (extent * RingSize / ringSprite.bounds.size.x);
        if (wakeSprite != null)
            wakeT.localScale = Vector3.one * (extent * WakeLength(ShipIndex) * wakeScale / wakeSprite.bounds.size.y);
        wakeDrop = extent * WakeOffset;
        int order = (hull != null ? hull.sortingOrder : 0) - 1;
        ring.sortingOrder = order;
        wake.sortingOrder = order - 1;
        Apply(true);
    }

    SpriteRenderer Layer(string name)
    {
        var t = root.Find(name);
        var go = t != null ? t.gameObject : new GameObject(name, typeof(SpriteRenderer));
        if (t == null) go.transform.SetParent(root, false);
        var sr = go.GetComponent<SpriteRenderer>();
        sr.color = ExhaustColors.Tint(ShipIndex);
        return sr;
    }

    bool Flying
    {
        get
        {
            return !respondToPause ||
                   (!buttonClicks.playerDied && (TouchInput.IsPressed || score.pauseCounter <= 0));
        }
    }

    void Update()
    {
        if (spinHull && Flying) transform.Rotate(0f, 0f, degreesPerSecond * Time.deltaTime);
    }

    void LateUpdate() { Step(Time.deltaTime); }

    // Advances by `deltaTime` seconds of (scaled) game time; zero holds.
    public void Step(float deltaTime)
    {
        if (ring == null) return;
        bool flying = Flying;
        if (deltaTime > 0f)
        {
            ticks += deltaTime * ShipHullArt.TicksPerSecond;
            if (spinRing && flying) ringAngle = Mathf.Repeat(ringAngle + degreesPerSecond * deltaTime, 360f);
        }
        Apply(false);
        bool on = powered && ShipExhaust.UsesSpinDrift(ShipIndex);
        ring.enabled = on;
        wake.enabled = on && flying;
    }

    void Apply(bool force)
    {
        if (ring == null || !ShipExhaust.UsesSpinDrift(ShipIndex)) return;
        bool b = boost || (boostHolder != null && boostHolder.activeInHierarchy);
        int frame = ShipExhaustStyle.FrameAt(ShipIndex, ticks);
        if (force || frame != shown || b != shownBoost)
        {
            var r = ShipExhaustStyle.Frame(ShipIndex, b ? ExhaustLayer.RingBoost : ExhaustLayer.Ring, frame);
            var w = ShipExhaustStyle.Frame(ShipIndex, b ? ExhaustLayer.WakeBoost : ExhaustLayer.Wake, frame);
            if (r != null) ring.sprite = r;
            if (w != null) wake.sprite = w;
            shown = frame;
            shownBoost = b;
        }

        // The ring is the hull's child, so the hull's own spin carries it.
        ringT.localRotation = spinRing ? Quaternion.Euler(0f, 0f, ringAngle) : Quaternion.identity;

        // The wake trails behind: straight down the screen for the player and
        // the dock, or behind the ship's own nose for free-flying traffic.
        if (wakeFollowsHeading)
        {
            wakeT.localRotation = Quaternion.identity;
            wakeT.localPosition = new Vector3(0f, -wakeDrop, 0f);
        }
        else
        {
            wakeT.rotation = Quaternion.identity;
            wakeT.position = transform.position + Vector3.down * (wakeDrop * Mathf.Abs(transform.lossyScale.y));
        }
    }
}
