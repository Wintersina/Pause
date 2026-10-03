using UnityEngine;

// The one-shot pickup burst, spawned where a pickup is collected.
public class PickupBurst : MonoBehaviour
{
    static int lastPickup;

    Sprite[] frames;
    SpriteRenderer sr;
    int frame;
    float clock;

    public static GameObject Play(GameObject pickup)
    {
        PickupKind kind;
        if (pickup == null || !PickupArt.TryKindOf(pickup, out kind)) return null;
        // Two ship colliders can report the same pickup before it is destroyed.
        if (pickup.GetInstanceID() == lastPickup) return null;
        lastPickup = pickup.GetInstanceID();
        var frames = PickupArt.Frames(PickupArt.BurstName(kind), PickupArt.BurstTicks.Length);
        if (frames[0] == null) return null;

        var go = new GameObject("pickupBurst");
        go.transform.position = pickup.transform.position;
        float scale = kind == PickupKind.DustSmall ? .45f : kind == PickupKind.Dust ? .9f : 1f;
        go.transform.localScale = new Vector3(scale, scale, 1f);
        var sr = go.AddComponent<SpriteRenderer>();
        var src = pickup.GetComponent<SpriteRenderer>();
        sr.sortingLayerID = src != null ? src.sortingLayerID : 0;
        sr.sortingOrder = (src != null ? src.sortingOrder : 0) + 10;
        sr.sprite = frames[0];
        var burst = go.AddComponent<PickupBurst>();
        burst.frames = frames;
        burst.sr = sr;
        return go;
    }

    void Update()
    {
        // Scaled time, like the pickups: a burst caught by a freeze holds its pose.
        clock += Time.unscaledDeltaTime * Time.timeScale;
        while (clock >= PickupArt.BurstTicks[frame] * PickupArt.Tick)
        {
            clock -= PickupArt.BurstTicks[frame] * PickupArt.Tick;
            frame++;
            if (frame >= frames.Length) { Destroy(gameObject); return; }
            sr.sprite = frames[frame];
        }
    }
}
