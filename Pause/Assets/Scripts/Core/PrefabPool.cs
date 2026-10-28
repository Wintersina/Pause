using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

// Implemented by a component on a pooled object that keeps state of its own
// (timers, flipbook frames, cached lookups): OnPoolAcquire puts it back to
// what a freshly spawned one starts with. Called on every reuse, with the
// object still inactive, after the generic snapshot has been restored.
public interface IPoolReset
{
    void OnPoolAcquire();
}

// Object pooling for the pickups (star dust, atoms): ONE funnel in
// (PrefabPool.Get) and ONE funnel out (Pooled.Destroy / PooledObject.Release),
// so no call site can leak or release twice.
//
//   spawn   PrefabPool.Get(prefab, pos, rot, decorate)   instead of Instantiate + decorating
//   remove  Pooled.Destroy(go)                           instead of Destroy(go)
//
// `decorate` runs ONCE, when the instance is first built (scale, halo, footprint ...);
// the decorated instance is then snapshotted (PooledObject.Capture) and every reuse
// restores that snapshot, so decorations are re-armed rather than re-added.
//
// A release is deferred to the end of the frame in play (like Destroy, so every
// script that still touches the object this frame sees it exactly as before) and
// immediate in edit mode (like DestroyImmediate). A second release of an object that
// is already released or pending is ignored (and counted: DoubleReleases).
//
// Lifetime: the pooled objects live in the loaded scene. A scene load (run start,
// retry, menu) drops the whole pool with it (nothing active survives into the next
// run); stale entries are skipped when they turn up as destroyed objects.
//
// Outside play mode the pool is OFF (plain Instantiate / DestroyImmediate, the old
// behaviour) unless a test switches ForceInEditMode on.
public static class PrefabPool
{
    public const int MaxFreePerKey = 48;       // a bigger pool destroys the surplus
    static readonly Vector3 Parked = new Vector3(0f, -1000f, 0f);

    // Tests: run the pool in edit mode.
    public static bool ForceInEditMode;
    public static bool Enabled { get { return Application.isPlaying || ForceInEditMode; } }
    // Tests: defer releases to Flush() in edit mode, as play defers them to the end of the frame.
    public static bool ForceDeferred;
    public static bool Deferred { get { return Application.isPlaying || ForceDeferred; } }

    internal sealed class Pool
    {
        public object key;
        public GameObject prefab;               // prefab pools
        public Func<GameObject> build;          // code-built pools
        public Action<GameObject> decorate;
        public readonly Stack<PooledObject> free = new Stack<PooledObject>();
    }

    static readonly Dictionary<object, Pool> pools = new Dictionary<object, Pool>();
    static readonly List<PooledObject> pending = new List<PooledObject>();
    static int live;

    // ---- counters (tests, profiling) ----
    public static int Created { get; private set; }      // instances built
    public static int Reused { get; private set; }       // gets served from the pool
    public static int Released { get; private set; }     // instances returned to a pool
    public static int DoubleReleases { get; private set; }
    public static int Discarded { get; private set; }    // returned over the cap, or damaged
    public static int PendingCount { get { return pending.Count; } }
    // Handed out and not yet returned (pending releases included): 0 after a run ends cleanly.
    public static int InUse { get { return live; } }

    // Free (idle) instances of a key.
    public static int FreeCount(object key)
    {
        Pool p;
        return key != null && pools.TryGetValue(key, out p) ? p.free.Count : 0;
    }

    // ------------------------------------------------------------------
    // acquire

    // A pooled copy of a prefab at pos/rot. `decorate` (null: none) runs once per built instance,
    // right after it is made (and, outside the pool, on every plain copy).
    public static GameObject Get(GameObject prefab, Vector3 pos, Quaternion rot, Action<GameObject> decorate = null)
    {
        if (!Enabled || prefab == null)
        {
            var plain = Object.Instantiate(prefab, pos, rot);
            if (decorate != null) decorate(plain);
            return plain;
        }
        Pool pool;
        if (!pools.TryGetValue(prefab, out pool))
        {
            pool = new Pool { key = prefab, prefab = prefab, decorate = decorate };
            pools[prefab] = pool;
        }
        return Take(pool, pos, rot);
    }

    // The same for an object built in code (key names the pool; build makes a new one).
    public static GameObject Get(string key, Func<GameObject> build, Vector3 pos, Quaternion rot, Action<GameObject> decorate = null)
    {
        if (!Enabled)
        {
            var plain = build();
            plain.transform.SetPositionAndRotation(pos, rot);
            if (decorate != null) decorate(plain);
            return plain;
        }
        Pool pool;
        if (!pools.TryGetValue(key, out pool))
        {
            pool = new Pool { key = key, build = build, decorate = decorate };
            pools[key] = pool;
        }
        return Take(pool, pos, rot);
    }

    static GameObject Take(Pool pool, Vector3 pos, Quaternion rot)
    {
        while (pool.free.Count > 0)
        {
            var po = pool.free.Pop();
            if (po == null) continue;                       // went with its scene
            if (!po.TryAcquire(pos, rot)) { Discarded++; Destroy(po.gameObject); continue; }
            Reused++;
            live++;
            return po.gameObject;
        }
        return Build(pool, pos, rot);
    }

    static GameObject Build(Pool pool, Vector3 pos, Quaternion rot)
    {
        GameObject go;
        if (pool.prefab != null) go = Object.Instantiate(pool.prefab, pos, rot);
        else { go = pool.build(); go.transform.SetPositionAndRotation(pos, rot); }
        if (pool.decorate != null) pool.decorate(go);
        var po = go.AddComponent<PooledObject>();
        po.Init(pool);
        Created++;
        live++;
        return go;
    }

    // Fills a pool up to `count` idle instances (scene start), so the first spawns build nothing.
    public static void Prewarm(GameObject prefab, int count, Action<GameObject> decorate = null)
    {
        if (!Enabled || prefab == null) return;
        Pool pool;
        if (!pools.TryGetValue(prefab, out pool))
        {
            pool = new Pool { key = prefab, prefab = prefab, decorate = decorate };
            pools[prefab] = pool;
        }
        Prewarm(pool, count);
    }

    public static void Prewarm(string key, Func<GameObject> build, int count, Action<GameObject> decorate = null)
    {
        if (!Enabled || build == null) return;
        Pool pool;
        if (!pools.TryGetValue(key, out pool))
        {
            pool = new Pool { key = key, build = build, decorate = decorate };
            pools[key] = pool;
        }
        Prewarm(pool, count);
    }

    static void Prewarm(Pool pool, int count)
    {
        count = Mathf.Min(count, MaxFreePerKey);
        while (pool.free.Count < count)
        {
            var go = Build(pool, Parked, Quaternion.identity);
            go.GetComponent<PooledObject>().ReleaseNow();
        }
    }

    // ------------------------------------------------------------------
    // release

    internal static void Enqueue(PooledObject po)
    {
        pending.Add(po);
        PoolRunner.Ensure();
    }

    // The end-of-frame step: every release asked for this frame happens now (public for tests).
    public static void Flush()
    {
        for (int i = 0; i < pending.Count; i++)
        {
            var po = pending[i];
            if (po != null) po.ReleaseNow();
        }
        pending.Clear();
    }

    // The instance is off and clean: keep it, or drop the surplus.
    internal static void Push(Pool pool, PooledObject po)
    {
        live--;
        if (pool.free.Count >= MaxFreePerKey) { Discarded++; Destroy(po.gameObject); return; }
        pool.free.Push(po);
        Released++;
    }

    // Returned for good (not pooled any more): it was in use, now it is not.
    internal static void Forget() { live--; }

    internal static void NoteDoubleRelease() { DoubleReleases++; }

    static void Destroy(GameObject go)
    {
        if (go == null) return;
        if (Application.isPlaying) Object.Destroy(go); else Object.DestroyImmediate(go);
    }

    // Drops every pooled object's bookkeeping (a scene load already took the objects; tests).
    public static void Clear()
    {
        pools.Clear();
        pending.Clear();
        Created = Reused = Released = DoubleReleases = Discarded = 0;
        live = 0;
    }

    // Tests: destroys every idle instance too.
    public static void ClearAndDestroy()
    {
        foreach (var pool in pools.Values)
            foreach (var po in pool.free)
                if (po != null) Destroy(po.gameObject);
        foreach (var po in pending)
            if (po != null) Destroy(po.gameObject);
        Clear();
    }

    [RuntimeInitializeOnLoadMethod]
    static void Init()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (mode == LoadSceneMode.Single) Clear();
    }
}

// The one way to remove a pickup-type object: pooled instances go back to
// their pool, anything else is destroyed exactly as before.
public static class Pooled
{
    // Exactly Object.Destroy for anything that is not pooled.
    public static void Destroy(GameObject go)
    {
        if (go == null) return;
        PooledObject po;
        if (go.TryGetComponent(out po)) { po.Release(); return; }
        Object.Destroy(go);
    }

    // Exactly "Destroy in play, DestroyImmediate in edit mode" for anything that is not pooled.
    public static void Remove(GameObject go)
    {
        if (go == null) return;
        PooledObject po;
        if (go.TryGetComponent(out po)) { po.Release(); return; }
        if (Application.isPlaying) Object.Destroy(go); else Object.DestroyImmediate(go);
    }

    public static void Destroy(Component c)
    {
        if (c != null) Destroy(c.gameObject);
    }

    public static bool IsPooled(GameObject go) { return go != null && go.TryGetComponent(out PooledObject _); }
}

// Flushes the deferred releases at the end of the frame's LateUpdates.
[DefaultExecutionOrder(32000)]
public sealed class PoolRunner : MonoBehaviour
{
    static PoolRunner instance;

    internal static void Ensure()
    {
        if (instance != null || !Application.isPlaying) return;
        var go = new GameObject("~PrefabPool");
        go.hideFlags = HideFlags.HideInHierarchy;
        Object.DontDestroyOnLoad(go);
        instance = go.AddComponent<PoolRunner>();
    }

    void LateUpdate() { PrefabPool.Flush(); }
}

// Sits on every pooled instance: the pool it belongs to, its lifecycle state and
// the snapshot of its freshly built state that every reuse restores.
public sealed class PooledObject : MonoBehaviour
{
    internal enum State { InUse, Pending, Free }
    internal State state = State.InUse;
    internal PrefabPool.Pool pool;

    public bool IsFree { get { return state == State.Free; } }
    public bool IsPending { get { return state == State.Pending; } }
    public object PoolKey { get { return pool != null ? pool.key : null; } }

    // ---- the snapshot ----
    struct TransformState { public Transform t; public GameObject go; public bool active; public Vector3 pos; public Quaternion rot; public Vector3 scale; public int layer; public string tag; public string name; }
    struct BehaviourState { public Behaviour b; public bool enabled; }
    struct RendererState { public Renderer r; public bool enabled; public bool sprite; public Sprite art; public Color color; public bool flipX, flipY; public int order; public int layerId; public Material material; }
    struct ColliderState { public Collider2D c; public bool enabled, trigger; public Vector2 offset; public Vector2 size; public float radius; }
    struct BodyState { public Rigidbody2D rb; public bool simulated; public RigidbodyType2D type; }

    TransformState[] transforms;
    BehaviourState[] behaviours;
    RendererState[] renderers;
    ColliderState[] colliders;
    BodyState[] bodies;
    IPoolReset[] resets;
    HashSet<int> knownTransforms, knownComponents;
    Transform parent;
    static readonly List<Component> scratch = new List<Component>(32);
    static readonly List<Transform> scratchT = new List<Transform>(16);

    internal void Init(PrefabPool.Pool owner)
    {
        pool = owner;
        parent = transform.parent;
        Capture();
        state = State.InUse;
    }

    void Capture()
    {
        var ts = new List<TransformState>();
        var bs = new List<BehaviourState>();
        var rs = new List<RendererState>();
        var cs = new List<ColliderState>();
        var rbs = new List<BodyState>();
        var rst = new List<IPoolReset>();
        knownTransforms = new HashSet<int>();
        knownComponents = new HashSet<int>();

        GetComponentsInChildren(true, scratch);
        for (int i = 0; i < scratch.Count; i++)
        {
            var c = scratch[i];
            if (c == null) continue;
            knownComponents.Add(c.GetInstanceID());
            var t = c as Transform;
            if (t != null)
            {
                knownTransforms.Add(t.GetInstanceID());
                ts.Add(new TransformState
                {
                    t = t, go = t.gameObject, active = t.gameObject.activeSelf, pos = t.localPosition, rot = t.localRotation,
                    scale = t.localScale, layer = t.gameObject.layer, tag = t.gameObject.tag, name = t.gameObject.name
                });
                continue;
            }
            var col = c as Collider2D;
            if (col != null)
            {
                var s = new ColliderState { c = col, enabled = col.enabled, trigger = col.isTrigger, offset = col.offset };
                var box = col as BoxCollider2D; if (box != null) s.size = box.size;
                var circle = col as CircleCollider2D; if (circle != null) s.radius = circle.radius;
                cs.Add(s);
                continue;
            }
            var rb = c as Rigidbody2D;
            if (rb != null) { rbs.Add(new BodyState { rb = rb, simulated = rb.simulated, type = rb.bodyType }); continue; }
            var ren = c as Renderer;
            if (ren != null)
            {
                var s = new RendererState { r = ren, enabled = ren.enabled, material = ren.sharedMaterial };
                var sr = ren as SpriteRenderer;
                if (sr != null)
                {
                    s.sprite = true; s.art = sr.sprite; s.color = sr.color; s.flipX = sr.flipX; s.flipY = sr.flipY;
                    s.order = sr.sortingOrder; s.layerId = sr.sortingLayerID;
                }
                rs.Add(s);
                continue;
            }
            var beh = c as Behaviour;
            if (beh != null && !(beh is PooledObject)) bs.Add(new BehaviourState { b = beh, enabled = beh.enabled });
            var reset = c as IPoolReset;
            if (reset != null) rst.Add(reset);
        }
        scratch.Clear();
        transforms = ts.ToArray();
        behaviours = bs.ToArray();
        renderers = rs.ToArray();
        colliders = cs.ToArray();
        bodies = rbs.ToArray();
        resets = rst.ToArray();
    }

    // ---- release ----

    // The funnel out. Play: deferred to the end of the frame; edit mode: now.
    public void Release()
    {
        if (state != State.InUse) { PrefabPool.NoteDoubleRelease(); return; }
        if (pool == null || !PrefabPool.Enabled) { DestroyForReal(); return; }
        if (!PrefabPool.Deferred) { ReleaseNow(); return; }
        state = State.Pending;
        PrefabPool.Enqueue(this);
    }

    void DestroyForReal()
    {
        state = State.Free;
        PrefabPool.Forget();
        if (Application.isPlaying) Destroy(gameObject); else DestroyImmediate(gameObject);
    }

    internal void ReleaseNow()
    {
        if (state == State.Free) return;
        state = State.Free;
        var go = gameObject;
        go.SetActive(false);                      // OnDisable: registries, footprints, markers let go
        CancelInvokes();
        RemoveExtras();
        if (transform.parent != parent) transform.SetParent(parent, false);
        PrefabPool.Push(pool, this);
    }

    void CancelInvokes()
    {
        for (int i = 0; i < behaviours.Length; i++)
        {
            var m = behaviours[i].b as MonoBehaviour;
            if (m != null) m.CancelInvoke();
        }
    }

    // Anything added after the snapshot (a refund mark, a tutorial drift, a lock-on marker, a child
    // sprite) is removed, so a reused instance never carries the last life's decorations.
    void RemoveExtras()
    {
        GetComponentsInChildren(true, scratchT);
        for (int i = 0; i < scratchT.Count; i++)
        {
            var t = scratchT[i];
            if (t == null || knownTransforms.Contains(t.GetInstanceID())) continue;
            if (t.parent != null && knownTransforms.Contains(t.parent.GetInstanceID())) DestroyImmediate(t.gameObject);
        }
        scratchT.Clear();
        GetComponentsInChildren(true, scratch);
        for (int i = 0; i < scratch.Count; i++)
        {
            var c = scratch[i];
            if (c == null || knownComponents.Contains(c.GetInstanceID())) continue;
            DestroyImmediate(c);
        }
        scratch.Clear();
    }

    // ---- acquire ----

    // Puts the instance back to its snapshot, places it and switches it on. False: it was damaged
    // (a part destroyed from outside); the pool throws it away.
    internal bool TryAcquire(Vector3 pos, Quaternion rot)
    {
        if (state != State.Free || !Intact()) return false;
        Restore();
        transform.SetPositionAndRotation(pos, rot);
        for (int i = 0; i < resets.Length; i++)
        {
            var r = resets[i] as Component;
            if (r != null) resets[i].OnPoolAcquire();
        }
        state = State.InUse;
        gameObject.SetActive(transforms[0].active);
        return true;
    }

    bool Intact()
    {
        for (int i = 0; i < transforms.Length; i++) if (transforms[i].t == null) return false;
        for (int i = 0; i < behaviours.Length; i++) if (behaviours[i].b == null) return false;
        for (int i = 0; i < renderers.Length; i++) if (renderers[i].r == null) return false;
        for (int i = 0; i < colliders.Length; i++) if (colliders[i].c == null) return false;
        for (int i = 0; i < bodies.Length; i++) if (bodies[i].rb == null) return false;
        for (int i = 0; i < resets.Length; i++) if ((resets[i] as Component) == null) return false;
        return true;
    }

    void Restore()
    {
        for (int i = 0; i < transforms.Length; i++)
        {
            var s = transforms[i];
            var t = s.t;
            if (i > 0)
            {
                t.localPosition = s.pos; t.localRotation = s.rot;
                if (t.gameObject.activeSelf != s.active) t.gameObject.SetActive(s.active);
            }
            t.localScale = s.scale;
            var g = s.go;
            if (g.layer != s.layer) g.layer = s.layer;
            if (!g.CompareTag(s.tag)) g.tag = s.tag;
            g.name = s.name;   // (assigning does not allocate; reading the name does)
        }
        for (int i = 0; i < behaviours.Length; i++) if (behaviours[i].b.enabled != behaviours[i].enabled) behaviours[i].b.enabled = behaviours[i].enabled;
        for (int i = 0; i < renderers.Length; i++)
        {
            var s = renderers[i];
            s.r.enabled = s.enabled;
            if (s.r.sharedMaterial != s.material) s.r.sharedMaterial = s.material;
            if (!s.sprite) continue;
            var sr = (SpriteRenderer)s.r;
            sr.sprite = s.art; sr.color = s.color; sr.flipX = s.flipX; sr.flipY = s.flipY;
            sr.sortingOrder = s.order; sr.sortingLayerID = s.layerId;
        }
        for (int i = 0; i < colliders.Length; i++)
        {
            var s = colliders[i];
            var c = s.c;
            c.enabled = s.enabled; c.isTrigger = s.trigger; c.offset = s.offset;
            var box = c as BoxCollider2D; if (box != null) box.size = s.size;
            var circle = c as CircleCollider2D; if (circle != null) circle.radius = s.radius;
        }
        for (int i = 0; i < bodies.Length; i++)
        {
            var s = bodies[i];
            s.rb.bodyType = s.type; s.rb.simulated = s.simulated;
            s.rb.linearVelocity = Vector2.zero; s.rb.angularVelocity = 0f;
        }
    }

    // Tests: does the object still equal its snapshot? (null: yes; else what differs.)
    public string Diff()
    {
        if (transforms == null) return "no snapshot";
        for (int i = 0; i < transforms.Length; i++)
        {
            var s = transforms[i];
            if (s.t == null) return "transform " + i + " destroyed";
            if (i > 0 && (s.t.localPosition != s.pos || s.t.localRotation != s.rot)) return "transform " + s.name + " moved";
            if (s.t.localScale != s.scale) return "scale of " + s.name;
            if (i > 0 && s.t.gameObject.activeSelf != s.active) return "active of " + s.name;
            if (s.go.layer != s.layer) return "layer of " + s.name;
            if (!s.go.CompareTag(s.tag)) return "tag of " + s.name;
            if (s.go.name != s.name) return "name of " + s.name;
        }
        for (int i = 0; i < behaviours.Length; i++)
            if (behaviours[i].b == null || behaviours[i].b.enabled != behaviours[i].enabled) return "enabled of " + (behaviours[i].b != null ? behaviours[i].b.GetType().Name : "?");
        for (int i = 0; i < renderers.Length; i++)
        {
            var s = renderers[i];
            if (s.r == null || s.r.enabled != s.enabled) return "renderer enabled";
            var sr = s.r as SpriteRenderer;
            if (sr != null && (sr.sprite != s.art || sr.color != s.color || sr.flipX != s.flipX || sr.flipY != s.flipY)) return "sprite state of " + sr.name;
        }
        for (int i = 0; i < colliders.Length; i++)
        {
            var s = colliders[i];
            if (s.c == null || s.c.enabled != s.enabled || s.c.isTrigger != s.trigger || s.c.offset != s.offset) return "collider state";
            var box = s.c as BoxCollider2D; if (box != null && box.size != s.size) return "box size";
            var circle = s.c as CircleCollider2D; if (circle != null && !Mathf.Approximately(circle.radius, s.radius)) return "circle radius";
        }
        for (int i = 0; i < bodies.Length; i++)
            if (bodies[i].rb == null || bodies[i].rb.linearVelocity != Vector2.zero || bodies[i].rb.angularVelocity != 0f) return "body velocity";
        scratch.Clear();
        GetComponentsInChildren(true, scratch);
        int extra = 0;
        for (int i = 0; i < scratch.Count; i++) if (scratch[i] != null && !knownComponents.Contains(scratch[i].GetInstanceID())) extra++;
        scratch.Clear();
        return extra > 0 ? extra + " extra component(s)" : null;
    }
}
