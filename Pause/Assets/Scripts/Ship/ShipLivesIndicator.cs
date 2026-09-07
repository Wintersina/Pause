using UnityEngine;
using UnityEngine.SceneManagement;

// Three small hearts floating above the ship, shown only for hulls that have
// no damage-frame art of their own (the eight single-image legacy ships --
// Lightning through Turtle -- whose SpriteFor ignores damage state entirely
// and always draws the same sprite). Every other ship already shows damage
// through its own intact/damaged/critical art, so this would be redundant
// there.
public class ShipLivesIndicator : MonoBehaviour
{
    [Tooltip("Ships at or past this roster index have no damage sprites of " +
             "their own -- see shopingShips.SpriteFor.")]
    public const int FirstShipWithoutDamageArt = 8;

    [Tooltip("Gap between the top of the hull and the row of hearts, in " +
             "world units.")]
    public float clearance = 0.12f;

    [Tooltip("Gap between hearts, in world units.")]
    public float spacing = 0.22f;

    [Tooltip("Each heart's world size (diameter).")]
    public float heartSize = 0.18f;

    Transform[] hearts;
    Vector3[] heartBasePositions;
    int lastShown = -1;
    float seed;

    void Start()
    {
        int shipIndex = PlayerPrefs.GetInt("spawnShip", 0);
        if (shipIndex < FirstShipWithoutDamageArt)
        {
            Destroy(this);
            return;
        }

        Build();
        seed = Random.value * 10f;
    }

    void Build()
    {
        var hull = GetComponent<SpriteRenderer>();
        var sprite = Resources.Load<Sprite>("Vfx/lifeHeart");
        if (sprite == null) { Destroy(this); return; }

        float hullTop = hull != null && hull.sprite != null
            ? hull.sprite.bounds.extents.y * Mathf.Max(transform.lossyScale.x, transform.lossyScale.y)
            : 0.5f;

        int count = Mathf.Max(1, collisionDetection.MAXLIFE);
        hearts = new Transform[count];

        float totalWidth = (count - 1) * spacing;
        // World-space clearance/size, converted to local space since the
        // hearts are parented under the (possibly scaled) ship.
        float parentScale = Mathf.Max(transform.lossyScale.x, transform.lossyScale.y);
        float localY = (hullTop + clearance) / Mathf.Max(parentScale, 0.0001f);
        float localSpacing = spacing / Mathf.Max(parentScale, 0.0001f);
        float localSize = heartSize / Mathf.Max(parentScale, 0.0001f);
        float localStartX = -totalWidth / Mathf.Max(parentScale, 0.0001f) / 2f;

        for (int i = 0; i < count; i++)
        {
            var go = new GameObject("Heart" + i);
            go.transform.SetParent(transform, false);
            go.transform.localPosition = new Vector3(localStartX + i * localSpacing, localY, -0.1f);

            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.sortingOrder = (hull != null ? hull.sortingOrder : 0) + 2;

            float scale = localSize / Mathf.Max(sprite.bounds.size.x, sprite.bounds.size.y);
            go.transform.localScale = Vector3.one * scale;

            hearts[i] = go.transform;
        }

        heartBasePositions = new Vector3[count];
        for (int i = 0; i < count; i++)
            heartBasePositions[i] = hearts[i].localPosition;
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

        // A gentle float around each heart's own fixed base position --
        // echoes the thruster/portal motion used elsewhere. Computed fresh
        // from the stored base each frame rather than accumulated onto the
        // current position, which would have drifted the hearts away from
        // the ship over time instead of oscillating in place.
        float bob = Mathf.Sin((Time.unscaledTime + seed) * 3f) * 0.02f;
        for (int i = 0; i < hearts.Length; i++)
        {
            if (hearts[i] == null || !hearts[i].gameObject.activeSelf) continue;
            var b = heartBasePositions[i];
            hearts[i].localPosition = new Vector3(b.x, b.y + bob, b.z);
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
