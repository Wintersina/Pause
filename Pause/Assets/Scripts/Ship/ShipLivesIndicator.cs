using UnityEngine;
using UnityEngine.SceneManagement;

// Three small hearts floating beside the ship, shown only for hulls that
// have no damage-frame art of their own (the eight single-image legacy ships
// -- Lightning through Turtle -- whose SpriteFor ignores damage state
// entirely and always draws the same sprite). Every other ship already shows
// damage through its own intact/damaged/critical art, so this would be
// redundant there.
//
// Where they float comes from ShipUiSlots: the first free slot around the
// hull -- above, below, left, right -- that is clear of the hull, its exhaust
// and every registered ship element (the ultimate's charge indicator in
// every pose, the gun, the secret-power meter...) and fits the safe area.
// Above or below they're a row; beside the hull, a stack. Near a screen edge
// they flip to the other side.
//
// The spinners (Ninja, UFO) are different: their hearts orbit. They ride a
// ring snug round the hull's spin circle, evenly spaced, carried round by
// the hull's own spin (a third as fast -- orbitRate -- so they read) and
// always upright. Everything else around a spinner -- the upright gun, the
// charge indicator in every pose, the secret meter -- sits on or across
// that ring somewhere, and a ring wide enough to clear all of it would be
// nearly three hull-radii out. So the ring stays tight and a heart ducks
// instead: it shrinks and fades as it nears whatever is drawn there this
// frame (ShipUiSlots.Drawn) and grows back past it. At a screen edge the
// ring flattens against the edge. All of it runs on gameplay time (the
// hull's spin is scaled), so it holds still while the world is frozen.
//
// Placed in world space each frame, after the ship elements have moved.
[DefaultExecutionOrder(50)]
public class ShipLivesIndicator : MonoBehaviour
{
    [Tooltip("Ships at or past this roster index have no damage sprites of " +
             "their own -- see shopingShips.SpriteFor.")]
    public const int FirstShipWithoutDamageArt = 8;

    [Tooltip("Gap kept between the hearts and the hull or any other ship " +
             "element, in world units.")]
    public float clearance = 0.08f;

    [Tooltip("Gap between hearts, in world units.")]
    public float spacing = 0.22f;

    [Tooltip("Each heart's world size (diameter).")]
    public float heartSize = 0.18f;

    [Tooltip("A slot within this of the hull (world units) is preferred " +
             "over any further one, whatever its order.")]
    public float nearReach = 0.3f;

    [Tooltip("Furthest the hearts may be pushed from the hull to clear " +
             "something, in world units.")]
    public float maxReach = 1.2f;

    public const float BobAmplitude = 0.02f;

    [Tooltip("Spinners: how far round the hearts go per turn of the hull.")]
    public float orbitRate = 1f / 3f;

    public const float PulseAmplitude = 0.015f;

    [Tooltip("Spinners: gap kept to the hull's spin circle and to what's " +
             "drawn round it, in world units. Tighter than `clearance`: these " +
             "are this frame's drawings (and the circle round the hull's " +
             "corners), not keep-out envelopes.")]
    public float orbitClearance = 0.04f;

    [Tooltip("Spinners: how far ahead of an obstacle a heart starts to " +
             "shrink, in world units, so it ducks rather than pops.")]
    public float squashBand = 0.08f;

    Transform[] hearts;
    int lastShown = -1;
    float seed;
    int shipId;
    ShipUiSlots.Slot[] slots;
    int layoutVersion = -1;
    float relayoutIn;
    int slotIndex = -1;

    // Orbit (spinners only).
    bool orbit;
    SpriteRenderer[] renderers;
    float baseScale;
    float orbitAngle, lastHullAngle, clock;
    bool haveHullAngle;
    float[] spot, shrink;
    readonly System.Collections.Generic.List<Bounds> drawn = new System.Collections.Generic.List<Bounds>();

    public ShipUiSlots.Side Side { get { return slotIndex >= 0 && slots != null ? slots[slotIndex].side : ShipUiSlots.Side.Above; } }
    public Transform[] Hearts { get { return hearts; } }
    public bool Orbiting { get { return orbit; } }
    // Degrees the ring has turned (each heart adds its own even spacing).
    public float OrbitAngle { get { return orbitAngle; } }
    public float OrbitRadius { get; private set; }
    // 1 full size .. 0 ducked out of sight, per heart.
    public float Shrink(int i) { return shrink != null && i < shrink.Length ? shrink[i] : 1f; }

    void Start()
    {
        // The flown ship's own id (its name), not the raw saved selection:
        // an unowned selection flies the starter, which has damage art.
        int shipIndex = ShipId.Of(gameObject, ShipId.Equipped());
        if (shipIndex < FirstShipWithoutDamageArt)
        {
            Destroy(this);
            return;
        }

        BuildHearts();
        seed = Random.value * 10f;
    }

    public void BuildHearts()
    {
        var hull = GetComponent<SpriteRenderer>();
        var sprite = Resources.Load<Sprite>("Vfx/lifeHeart");
        if (sprite == null) { Destroy(this); return; }
        shipId = ShipId.Of(gameObject, ShipId.Equipped());

        int count = Mathf.Max(1, collisionDetection.MAXLIFE);
        hearts = new Transform[count];
        renderers = new SpriteRenderer[count];
        spot = new float[count];
        shrink = new float[count];
        orbit = ShipUiSlots.Spins(shipId);
        haveHullAngle = false;
        float parentScale = Mathf.Max(Mathf.Abs(transform.lossyScale.x), Mathf.Abs(transform.lossyScale.y), 0.0001f);
        float localSize = heartSize / parentScale;

        for (int i = 0; i < count; i++)
        {
            var go = new GameObject("Heart" + i);
            go.transform.SetParent(transform, false);

            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.sortingOrder = (hull != null ? hull.sortingOrder : 0) + 2;

            float scale = localSize / Mathf.Max(sprite.bounds.size.x, sprite.bounds.size.y);
            go.transform.localScale = Vector3.one * scale;
            baseScale = scale;

            hearts[i] = go.transform;
            renderers[i] = sr;
            spot[i] = 360f * i / count;
            shrink[i] = 1f;
        }
        layoutVersion = -1;
        Place(0f);
    }

    public ShipUiSlots.Request Request()
    {
        int count = hearts != null ? hearts.Length : Mathf.Max(1, collisionDetection.MAXLIFE);
        float length = (count - 1) * spacing + heartSize;
        float thick = heartSize + BobAmplitude * 2f;
        return new ShipUiSlots.Request
        {
            rowSize = new Vector2(length, thick),
            columnSize = new Vector2(heartSize, length + BobAmplitude * 2f),
            gap = clearance,
            nearReach = nearReach,
            maxReach = maxReach,
        };
    }

    // Re-reads the free slots (the hull, its elements or the registry changed).
    public void Relayout()
    {
        slots = ShipUiSlots.Candidates(transform, shipId, Request(), this);
        layoutVersion = ShipUiSlots.Version;
        relayoutIn = 1f;
    }

    void Update()
    {
        if (hearts == null) return;

        int remaining = Mathf.Clamp(collisionDetection.MAXLIFE - collisionDetection.lifeCounter, 0, hearts.Length);
        if (remaining != lastShown)
        {
            lastShown = remaining;
            for (int i = 0; i < hearts.Length; i++)
                if (hearts[i] != null) hearts[i].gameObject.SetActive(i < remaining);
        }
    }

    void LateUpdate()
    {
        Place(Time.unscaledDeltaTime, Time.deltaTime);
    }

    // Picks the slot for where the ship is now and lays the hearts out in it,
    // with a gentle float around each heart's own fixed spot -- echoes the
    // thruster/portal motion used elsewhere. Computed fresh each frame from
    // the slot rather than accumulated onto the current position, which
    // would drift the hearts away from the ship.
    // `scaledDt` (gameplay time) only drives a spinner's orbit.
    public void Place(float unscaledDt, float scaledDt = 0f)
    {
        if (hearts == null) return;
        if (orbit) { Orbit(scaledDt); return; }
        relayoutIn -= unscaledDt;
        if (slots == null || layoutVersion != ShipUiSlots.Version || relayoutIn <= 0f) Relayout();

        Vector3 p = transform.position;
        slotIndex = ShipUiSlots.Choose(slots, p, ShipUiSlots.ScreenRect(null), nearReach, slotIndex);
        if (slotIndex < 0) return;
        var slot = slots[slotIndex];
        bool across = slot.side == ShipUiSlots.Side.Above || slot.side == ShipUiSlots.Side.Below;
        Vector3 center = new Vector3(p.x + slot.offset.x, p.y + slot.offset.y, p.z - 0.1f);
        float start = -(hearts.Length - 1) * spacing * .5f;
        float bob = Mathf.Sin((Time.unscaledTime + seed) * 3f) * BobAmplitude;
        for (int i = 0; i < hearts.Length; i++)
        {
            if (hearts[i] == null) continue;
            float d = start + i * spacing;
            // A row reads left to right; a stack top down, so the hearts
            // that go first are the far end of either.
            Vector3 at = across ? new Vector3(center.x + d, center.y + bob, center.z)
                                : new Vector3(center.x, center.y - d + bob, center.z);
            hearts[i].position = at;
            hearts[i].rotation = Quaternion.identity;
        }
    }

    // ---- spinners: the orbit --------------------------------------------

    void Orbit(float dt)
    {
        dt = Mathf.Max(0f, dt);
        // Carried round by the hull's own spin, which already stops when the
        // world freezes or the pilot lets go.
        float hullAngle = transform.eulerAngles.z;
        if (haveHullAngle)
            orbitAngle = Mathf.Repeat(orbitAngle + Mathf.DeltaAngle(lastHullAngle, hullAngle) * orbitRate, 360f);
        lastHullAngle = hullAngle;
        haveHullAngle = true;
        clock += dt;

        // Snug round the circle the spinning hull sweeps, with room for the
        // heart's corner, the bob and the pulse.
        float hullRadius = ShipUiSlots.HullBounds(transform, shipId).extents.x;
        float half = heartSize * .5f;
        float diagonal = half * 1.4143f;
        OrbitRadius = hullRadius + orbitClearance + diagonal + BobAmplitude + PulseAmplitude;
        float radius = OrbitRadius + Mathf.Sin((clock + seed) * 2.1f) * PulseAmplitude;
        float bob = Mathf.Sin((clock + seed) * 3f) * BobAmplitude;

        drawn.Clear();
        ShipUiSlots.Drawn(transform, drawn, this);
        Rect screen = ShipUiSlots.ScreenRect(null);
        Vector3 p = transform.position;

        // The hearts left share the ring evenly; a lost one's gap closes up.
        int shown = 0;
        for (int i = 0; i < hearts.Length; i++)
            if (hearts[i] != null && hearts[i].gameObject.activeSelf) shown++;
        float ease = 1f - Mathf.Exp(-6f * dt);
        int k = 0;
        for (int i = 0; i < hearts.Length; i++)
        {
            if (hearts[i] == null || !hearts[i].gameObject.activeSelf) continue;
            float target = 360f * k++ / Mathf.Max(1, shown);
            spot[i] = Mathf.LerpAngle(spot[i], target, ease);

            float a = (orbitAngle + spot[i] + 90f) * Mathf.Deg2Rad;
            var at = new Vector3(p.x + Mathf.Cos(a) * radius, p.y + Mathf.Sin(a) * radius + bob, p.z - 0.1f);
            // flatten against a screen edge rather than leave it
            at.x = Mathf.Clamp(at.x, screen.xMin + half, Mathf.Max(screen.xMin + half, screen.xMax - half));
            at.y = Mathf.Clamp(at.y, screen.yMin + half, Mathf.Max(screen.yMin + half, screen.yMax - half));

            // Duck round the hull's circle and anything drawn here now.
            float s = Mathf.Clamp01(((new Vector2(at.x - p.x, at.y - p.y)).magnitude - hullRadius - orbitClearance) / diagonal);
            foreach (var o in drawn)
            {
                float gap = Mathf.Max(Mathf.Abs(at.x - o.center.x) - o.extents.x, Mathf.Abs(at.y - o.center.y) - o.extents.y);
                s = Mathf.Min(s, Mathf.Clamp01((gap - orbitClearance) / (half + squashBand)));
            }
            shrink[i] = s;

            hearts[i].position = at;
            hearts[i].rotation = Quaternion.identity;
            hearts[i].localScale = Vector3.one * (baseScale * s);
            var sr = renderers[i];
            if (sr != null)
            {
                sr.enabled = s > .12f;
                var c = sr.color;
                c.a = Mathf.Clamp01(s * 1.25f);
                sr.color = c;
            }
        }
    }
}

// Attaches the indicator to the player ship once it exists, matching the
// same poll-until-found pattern used by ShipThruster/ShipPowerController.
public class ShipLivesIndicatorAttach : MonoBehaviour
{
    float giveUp = 6f;

    void Update()
    {
        var player = Object.FindFirstObjectByType<movePlayer>();
        if (player != null)
        {
            if (player.GetComponent<ShipLivesIndicator>() == null)
                player.gameObject.AddComponent<ShipLivesIndicator>();
            Destroy(gameObject);
            return;
        }

        giveUp -= Time.unscaledDeltaTime;
        if (giveUp <= 0f) Destroy(gameObject);
    }
}

public static class ShipLivesIndicatorBootstrap
{
    [RuntimeInitializeOnLoadMethod]
    static void Init()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (scene.name != "gameS1" && scene.name != "tutorialS5") return;
        new GameObject("~ShipLivesIndicatorAttach").AddComponent<ShipLivesIndicatorAttach>();
    }
}
