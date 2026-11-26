using UnityEngine;

public sealed class DroneCorePickup : MonoBehaviour
{
    public DroneMode mode;
    public DroneCoreChoiceGroup group;
    public bool TryClaim() => group == null || group.TryClaim(this);
    public void Consume()
    {
        var root = group != null ? group.gameObject : gameObject;
        if (Application.isPlaying) Pooled.Destroy(root);
        else Object.DestroyImmediate(root);
    }
    public static GameObject Spawn(DroneMode mode, Vector3 at)
    {
        at = SpawnAboveCamera.PickupEntry(at, .4f);
        at = SpawnSpace.PickupSpot(at, Vector2.one * .20f, -RailInset.PickupLaneHalf, RailInset.PickupLaneHalf);
        var go = Build(mode, at);
        SpawnFootprint.Attach(go, Vector2.one * .20f, SpawnLayer.Pickup);
        return go;
    }
    public static void SpawnChoice(Vector3 at)
    {
        var root = new GameObject("~DroneCoreChoice").AddComponent<DroneCoreChoiceGroup>();
        float middle = Mathf.Clamp(at.x, -RailInset.PickupLaneHalf + .8f, RailInset.PickupLaneHalf - .8f);
        var preferred = DroneSquad.PreferredMode;
        int outside = 0;
        foreach (var mode in new[] { DroneMode.Straight, DroneMode.Alternating, DroneMode.Aimed })
        {
            float dx = mode == preferred ? 0f : (++outside == 1 ? -.8f : .8f);
            var go = Spawn(mode, new Vector3(middle + dx, at.y, 0f));
            go.transform.SetParent(root.transform, true);
            go.GetComponent<DroneCorePickup>().group = root;
        }
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

public sealed class DroneCoreChoiceGroup : MonoBehaviour
{
    bool claimed;
    public bool TryClaim(DroneCorePickup selected)
    {
        if (claimed) return false;
        claimed = true;
        foreach (var core in GetComponentsInChildren<DroneCorePickup>())
            if (core != selected) core.gameObject.SetActive(false);
        return true;
    }
    void Update() { if (transform.childCount == 0) Destroy(gameObject); }
}
