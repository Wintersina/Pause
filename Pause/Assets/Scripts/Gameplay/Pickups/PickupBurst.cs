using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

// The one-shot pickup burst, spawned where a pickup is collected.
//
// Pooled: a finished burst is switched off and reused, and the pool plus every
// burst flipbook is loaded as each scene loads, so a pickup frame never loads
// art from Resources or instantiates anything (a first pickup of each kind
// used to cost ~5-8 ms in Resources.Load alone).
public class PickupBurst : MonoBehaviour
{
    public const int PoolSize = 6;

    static int lastPickup;
    static readonly Stack<PickupBurst> free = new Stack<PickupBurst>();
    static readonly PickupKind[] Kinds =
        { PickupKind.Shield, PickupKind.Pause, PickupKind.Cooldown, PickupKind.Dust, PickupKind.DustSmall, PickupKind.Heal };

    Sprite[] frames;
    SpriteRenderer sr;
    int frame;
    float clock;

    public static int FreeCount { get { return free.Count; } }
    public static int Created { get; private set; }
    public static PickupBurst LastPlayed { get; private set; }

    [RuntimeInitializeOnLoadMethod]
    static void Init()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (mode == LoadSceneMode.Single) Prewarm();
    }

    // Loads every burst flipbook and fills the pool.
    public static void Prewarm()
    {
        UnityEngine.Profiling.Profiler.BeginSample("PickupBurst.Prewarm");
        foreach (var kind in Kinds) PickupArt.Frames(PickupArt.BurstName(kind), PickupArt.BurstTicks.Length);
        Prune();
        for (int i = free.Count; i < PoolSize; i++) free.Push(Create());
        UnityEngine.Profiling.Profiler.EndSample();
    }

    // Bursts whose objects went with the last scene.
    static void Prune()
    {
        if (free.Count == 0) return;
        bool stale = false;
        foreach (var b in free) if (b == null) { stale = true; break; }
        if (!stale) return;
        var keep = new List<PickupBurst>();
        foreach (var b in free) if (b != null) keep.Add(b);
        free.Clear();
        for (int i = keep.Count - 1; i >= 0; i--) free.Push(keep[i]);
    }

    static PickupBurst Create()
    {
        var go = new GameObject("pickupBurst", typeof(SpriteRenderer));
        var burst = go.AddComponent<PickupBurst>();
        burst.sr = go.GetComponent<SpriteRenderer>();
        go.SetActive(false);
        Created++;
        return burst;
    }

    static PickupBurst Take()
    {
        while (free.Count > 0)
        {
            var b = free.Pop();
            if (b != null) return b;
        }
        return Create();
    }

    public static GameObject Play(GameObject pickup)
    {
        PickupKind kind;
        if (pickup == null || !PickupArt.TryKindOf(pickup, out kind)) return null;
        // Two ship colliders can report the same pickup before it is destroyed.
        if (pickup.GetInstanceID() == lastPickup) return null;
        lastPickup = pickup.GetInstanceID();
        var frames = PickupArt.Frames(PickupArt.BurstName(kind), PickupArt.BurstTicks.Length);
        if (frames[0] == null) return null;

        var burst = Take();
        var go = burst.gameObject;
        go.transform.position = pickup.transform.position;
        float scale = kind == PickupKind.DustSmall ? .45f : kind == PickupKind.Dust ? .9f : 1f;
        go.transform.localScale = new Vector3(scale, scale, 1f);
        var sr = burst.sr;
        SpriteRenderer src;
        pickup.TryGetComponent(out src);
        sr.sortingLayerID = src != null ? src.sortingLayerID : 0;
        sr.sortingOrder = (src != null ? src.sortingOrder : 0) + 10;
        sr.sprite = frames[0];
        burst.frames = frames;
        burst.frame = 0;
        burst.clock = 0f;
        go.SetActive(true);
        LastPlayed = burst;
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
            if (frame >= frames.Length) { Release(); return; }
            sr.sprite = frames[frame];
        }
    }

    // Ends the burst now and returns it to the pool.
    public void Finish()
    {
        if (gameObject.activeSelf) Release();
    }

    void Release()
    {
        gameObject.SetActive(false);
        if (free.Count < PoolSize * 2) free.Push(this);
        else Destroy(gameObject);
    }
}
