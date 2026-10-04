using UnityEngine;

// Bounded pool of flat ink-outlined shards the contour shield breaks into.
// One scene-scoped pool shared by every shield; never grows past Capacity --
// when it is full the shard closest to dying is recycled. Shards live in
// world space (the ship flies on without them) and advance on scaled time, so
// they hang frozen while the game is paused.
public sealed class ShieldShards : MonoBehaviour
{
    public const int Capacity = 32;
    public const float Life = .42f;

    static ShieldShards instance;

    SpriteRenderer[] items = new SpriteRenderer[Capacity];
    readonly Vector3[] velocity = new Vector3[Capacity];
    readonly float[] spin = new float[Capacity];
    readonly float[] age = new float[Capacity];
    readonly float[] scale = new float[Capacity];
    readonly bool[] alive = new bool[Capacity];
    int created;

    public static ShieldShards Instance
    {
        get
        {
            if (instance != null) return instance;
            var go = new GameObject("~ShieldShards");
            instance = go.AddComponent<ShieldShards>();
            return instance;
        }
    }

    public static bool Exists { get { return instance != null; } }

    // Creates the pool and every shard up front (inactive), so the first
    // shatter never instantiates anything mid-run.
    public static void Prewarm()
    {
        var pool = Instance;
        var frames = ShieldArt.Shards;
        for (int i = 0; i < Capacity; i++)
        {
            if (pool.items[i] != null) continue;
            pool.items[i] = pool.NewShard(i);
            pool.items[i].sprite = frames[i % frames.Length];
            pool.items[i].gameObject.SetActive(false);
        }
    }

    SpriteRenderer NewShard(int slot)
    {
        var go = new GameObject("Shard" + slot, typeof(SpriteRenderer));
        go.transform.SetParent(transform, false);
        created++;
        return go.GetComponent<SpriteRenderer>();
    }
    public int Created { get { return created; } }

    public int ActiveCount
    {
        get
        {
            int n = 0;
            for (int i = 0; i < Capacity; i++) if (alive[i]) n++;
            return n;
        }
    }

    public void Spawn(Vector3 position, Vector3 vel, float size, float rotation, int variant,
                      int sortingLayerId, int sortingOrder)
    {
        int slot = -1;
        for (int i = 0; i < Capacity; i++) if (!alive[i]) { slot = i; break; }
        if (slot < 0)
        {
            float oldest = -1f;
            for (int i = 0; i < Capacity; i++) if (age[i] > oldest) { oldest = age[i]; slot = i; }
        }
        var sr = items[slot];
        if (sr == null) sr = items[slot] = NewShard(slot);
        var frames = ShieldArt.Shards;
        sr.sprite = frames[Mathf.Abs(variant) % frames.Length];
        sr.sortingLayerID = sortingLayerId;
        sr.sortingOrder = sortingOrder;
        sr.color = Color.white;
        sr.transform.position = position;
        sr.transform.rotation = Quaternion.Euler(0f, 0f, rotation);
        sr.transform.localScale = new Vector3(size, size, 1f);
        sr.gameObject.SetActive(true);
        velocity[slot] = vel;
        spin[slot] = (variant % 2 == 0 ? 1f : -1f) * (420f + 90f * (variant % 3));
        age[slot] = 0f;
        scale[slot] = size;
        alive[slot] = true;
    }

    void Update() { Tick(ShipShield.ScaledDelta()); }

    // Held key poses: a beat at full size, a drift, then a hard shrink.
    public void Tick(float dt)
    {
        if (dt <= 0f) return;
        for (int i = 0; i < Capacity; i++)
        {
            if (!alive[i]) continue;
            age[i] += dt;
            var sr = items[i];
            if (sr == null || age[i] >= Life)
            {
                alive[i] = false;
                if (sr != null) sr.gameObject.SetActive(false);
                continue;
            }
            var t = sr.transform;
            t.position += velocity[i] * dt;
            velocity[i] *= 1f - Mathf.Min(1f, 3.2f * dt);
            t.Rotate(0f, 0f, spin[i] * dt);
            float k = age[i] < Life * .7f ? 1f : .55f;
            t.localScale = new Vector3(scale[i] * k, scale[i] * k, 1f);
        }
    }
}
