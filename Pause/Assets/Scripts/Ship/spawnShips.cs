using UnityEngine;
using UnityEngine.SceneManagement;

// Spawns the player's ship in gameS1: the ship picked in the space dock
// (ShipId.Equipped(): the saved "spawnShip" if owned, else the starter).
public class spawnShips : MonoBehaviour
{
    public GameObject ship;

    public static readonly Vector3 SpawnPoint = new Vector3(0f, -2f, 1f);

    void Start()
    {
        Spawn(ShipId.Equipped());
    }

    public GameObject Spawn(int id)
    {
        if (!ShipId.IsValid(id)) id = ShipId.Starter;
        ship = Resources.Load<GameObject>(PrefabPathFor(id));
        if (ship == null) return null;
        var instance = Instantiate(ship, SpawnPoint, Quaternion.identity);
        instance.name = ShipId.ObjectName(id) + "(Clone)";
        ApplyHull(instance, id);
        return instance;
    }

    // Original hulls share the starter's gameplay components and effects.
    // Their id stays on the instance (its name) for health art and exhaust.
    public static string PrefabPathFor(int id)
    {
        int prefab = ShipId.IsRetro(id) ? id : ShipId.Starter;
        return "prefabs/Ships/inGameShips/ship" + prefab;
    }

    // Dresses a gameplay ship (spawned, or authored into a scene like the
    // tutorial's) as roster ship `id`: name, hull art, size, collider and
    // engine. Whatever 2016 art the prefab carried must not show through.
    public static void ApplyHull(GameObject instance, int id)
    {
        if (instance == null || !ShipId.IsValid(id)) return;
        bool clone = instance.name.EndsWith("(Clone)");
        instance.name = ShipId.ObjectName(id) + (clone ? "(Clone)" : "");

        // The ship4/5/7 prefabs still carry an enabled 2016 Animator whose
        // only clip animates the hull's sprite (Paranoid, Ligher and
        // Lightning art). Animators write after Update, so it overwrote the
        // roster art lifeControler sets every frame: picking Crimson Halo,
        // Ion Lancer or Gold Warden flew a different ship. The hull's art
        // comes from the roster now; the animator has nothing left to do.
        var animator = instance.GetComponent<Animator>();
        if (animator != null) animator.enabled = false;

        // The same four prefabs (ship4-7: Crimson Halo, Ion Lancer, Jade
        // Phantom, Gold Warden) never had a lifeControler -- their 2016
        // animator was their only "art" -- so those ships flew with the rest
        // sprite set below for the whole run: no damaged / critical drawing
        // (in any skin), no idle loop, bank or hit flash, no damage FX. Every
        // gameplay ship (it has a collisionDetection) gets the hull driver.
        if (instance.GetComponent<collisionDetection>() != null && instance.GetComponent<lifeControler>() == null)
            instance.AddComponent<lifeControler>();

        var hull = instance.GetComponent<SpriteRenderer>();
        Sprite sprite = shopingShips.SpriteFor(id);
        if (hull != null && sprite != null)
        {
            hull.sprite = sprite;
            float scale = shopingShips.NormalizedHullScale(sprite);
            instance.transform.localScale = new Vector3(scale, scale, 1f);
            // The tight hull polygon and the shield zone, baked per ship
            // (ShipHitbox); they replace the prefab's box. Only a hull with no
            // bake keeps the old box at 78% of the sprite. A hull with no
            // collider at all (title-screen traffic) stays without one.
            var collider = instance.GetComponent<BoxCollider2D>();
            bool gameplay = collider != null || ShipHitbox.Of(instance) != null;
            if (gameplay && ShipHitbox.Install(instance, id) == null)
            {
                if (collider != null)
                {
                    collider.offset = sprite.bounds.center;
                    collider.size = (Vector2)sprite.bounds.size * .78f;
                }
            }
        }
        ShipExhaust.ConfigureBoost(instance, id);
    }
}

// The tutorial's ship is authored into tutorialS5 (a ship1 instance), so it
// flew Neon Comet whatever was picked in the dock. Re-dress it as the
// equipped ship as the scene loads -- after Awake, before any Start, so
// lifeControler, the thruster and the damage fx all see the right id.
public static class TutorialShipHull
{
    public const string Scene = "tutorialS5";

    [RuntimeInitializeOnLoadMethod]
    static void Init()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (scene.name == Scene) Apply();
    }

    // Returns the tutorial ship it dressed, or null if there is none.
    public static GameObject Apply()
    {
        var player = Object.FindFirstObjectByType<movePlayerInTut>();
        if (player == null) return null;
        spawnShips.ApplyHull(player.gameObject, ShipId.Equipped());
        return player.gameObject;
    }
}
