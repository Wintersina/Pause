using UnityEngine;

public sealed class DroneCorePickup : MonoBehaviour
{
    public DroneMode mode;
    bool claimed;
    public bool TryClaim()
    {
        if (claimed) return false;
        claimed = true;
        return true;
    }
    public void Consume()
    {
        if (Application.isPlaying) Pooled.Destroy(gameObject);
        else Object.DestroyImmediate(gameObject);
    }
    public static GameObject Spawn(DroneMode mode, Vector3 at)
    {
        at = SpawnAboveCamera.PickupEntry(at, .4f);
        at = SpawnSpace.PickupSpot(at, Vector2.one * .20f, -RailInset.PickupLaneHalf, RailInset.PickupLaneHalf);
        var go = Build(mode, at);
        SpawnFootprint.Attach(go, Vector2.one * .20f, SpawnLayer.Pickup);
        return go;
    }
    public static GameObject SpawnRandom(Vector3 at, float roll = -1f)
    {
        if (roll < 0f) roll = Random.value;
        int index = Mathf.Min(2, Mathf.FloorToInt(Mathf.Clamp01(roll) * 3f));
        return Spawn((DroneMode)(1 + index), at);
    }
    public static GameObject Build(DroneMode mode, Vector3 at)
    {
        var go = new GameObject("droneCore_" + DroneSquad.Name(mode));
        go.tag = "pickUp"; go.transform.position = at;
        var sr = go.AddComponent<SpriteRenderer>(); sr.sprite = DronePickupArt.For(mode); sr.sortingOrder = 8;
        go.transform.localScale = Vector3.one * .8f;
        var col = go.AddComponent<CircleCollider2D>(); col.isTrigger = true; col.radius = .25f;
        var rb = go.AddComponent<Rigidbody2D>(); rb.bodyType = RigidbodyType2D.Kinematic; rb.gravityScale = 0f;
        PickupFlipbook.AddTo(go, (PickupKind)(12 + (int)mode));
        go.AddComponent<AtomSpin>().degreesPerSecond = 0f;
        go.AddComponent<moveItemEnmInStrightLine>();
        go.AddComponent<DroneCorePickup>().mode = mode;
        go.AddComponent<DronePickupPreview>();
        return go;
    }
}
