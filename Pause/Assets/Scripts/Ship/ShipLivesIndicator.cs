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
// they flip to the other side. They're placed in world space each frame, so
// a spinning hull (Ninja, UFO) doesn't swing them round.
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

    Transform[] hearts;
    int lastShown = -1;
    float seed;
    int shipId;
    ShipUiSlots.Slot[] slots;
    int layoutVersion = -1;
    float relayoutIn;
    int slotIndex = -1;

    public ShipUiSlots.Side Side { get { return slotIndex >= 0 ? slots[slotIndex].side : ShipUiSlots.Side.Above; } }
    public Transform[] Hearts { get { return hearts; } }

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

            hearts[i] = go.transform;
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
        Place(Time.unscaledDeltaTime);
    }

    // Picks the slot for where the ship is now and lays the hearts out in it,
    // with a gentle float around each heart's own fixed spot -- echoes the
    // thruster/portal motion used elsewhere. Computed fresh each frame from
    // the slot rather than accumulated onto the current position, which
    // would drift the hearts away from the ship.
    public void Place(float unscaledDt)
    {
        if (hearts == null) return;
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
