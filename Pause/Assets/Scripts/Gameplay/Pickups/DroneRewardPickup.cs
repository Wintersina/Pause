using UnityEngine;

public sealed class DroneRewardPickup : MonoBehaviour
{
    public const float MaxedDust = 5f;
    // The boss's drone hangs around: it falls at half the normal pickup descent, so it stays on screen (and
    // collectable) about twice as long. Planetfall only clears hazards, so it stays collectable through the transition.
    public const float LingerDescentScale = .5f;
    public void Consume()
    {
        if (Application.isPlaying) Pooled.Destroy(gameObject);
        else Object.DestroyImmediate(gameObject);
    }
    public static GameObject Spawn(Vector3 at)
    {
        var go = new GameObject("bossDroneReward");
        go.tag = "pickUp"; go.transform.position = at;
        var sr = go.AddComponent<SpriteRenderer>(); sr.sprite = DronePickupArt.Reward; sr.sortingOrder = 9;
        var col = go.AddComponent<CircleCollider2D>(); col.isTrigger = true; col.radius = .25f;
        var rb = go.AddComponent<Rigidbody2D>(); rb.bodyType = RigidbodyType2D.Kinematic; rb.gravityScale = 0f;
        PickupFlipbook.AddTo(go, PickupKind.DroneRecruit);
        go.AddComponent<AtomSpin>().degreesPerSecond = 0f;
        var mover = go.AddComponent<moveItemEnmInStrightLine>(); go.AddComponent<DroneRewardPickup>();
        var wander = AtomWander.Roll(); wander.descentScale = LingerDescentScale; mover.SetWander(wander);
        go.AddComponent<DronePickupPreview>();
        SpawnFootprint.Attach(go, Vector2.one * .25f, SpawnLayer.Pickup);
        return go;
    }
}
