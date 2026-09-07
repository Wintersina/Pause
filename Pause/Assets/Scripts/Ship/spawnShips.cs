using UnityEngine;

public class spawnShips : MonoBehaviour
{
    public GameObject ship;

    void Start()
    {
        int index = PlayerPrefs.GetInt("spawnShip", shopingShips.StarterShip);
        if (index < 1 || index >= shopingShips.shipTotal ||
            (index != shopingShips.StarterShip && PlayerPrefs.GetString("boughtship" + index) != "True"))
            index = shopingShips.StarterShip;

        // Original hulls share the starter's gameplay components and effects.
        // Their selected index remains on the instance for health art and exhaust.
        int prefabIndex = index <= 7 ? index : shopingShips.StarterShip;
        ship = Resources.Load<GameObject>("Prefabs/Ships/inGameShips/ship" + prefabIndex);
        if (ship == null) return;
        var instance = Instantiate(ship, new Vector3(0f, -2f, 1f), Quaternion.identity);
        instance.name = "ship" + index + "(Clone)";
        var hull = instance.GetComponent<SpriteRenderer>();
        Sprite sprite = shopingShips.SpriteFor(index);
        if (hull != null && sprite != null)
        {
            hull.sprite = sprite;
            float scale = shopingShips.NormalizedHullScale(sprite);
            instance.transform.localScale = new Vector3(scale, scale, 1f);
            var collider = instance.GetComponent<BoxCollider2D>();
            if (collider != null)
            {
                collider.offset = sprite.bounds.center;
                collider.size = (Vector2)sprite.bounds.size * .78f;
            }
        }
        ShipExhaust.ConfigureBoost(instance, index);
    }
}
