using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

// A detached, three-drawing death pose for any enemy whose world painted a
// strip (Enemies/Death/<key>.png, width = 3 x height); the rest keep the
// generic debris. The original
// seven-frame idle/tell/hit strip remains unchanged while this finishes.
//
// The poses are POOLED (like PickupBurst): a death takes a parked object (a
// GameObject with one SpriteRenderer and this), resets everything about it
// and shows it; when its last drawing is over it is parked again instead of
// destroyed. A run kills an enemy every second or two, so this keeps a
// GameObject + 2 components from being built and torn down per kill. Nothing
// else holds a pose: the kill cam records only active renderers, and a parked
// pose is inactive. Objects that went with the last scene are pruned.
public class EnemyDeathFlipbook : MonoBehaviour
{
    const float FlashSeconds = .08f;
    const float RuptureSeconds = .11f;
    const float SmokeSeconds = .2f;
    public const float TotalSeconds = FlashSeconds + RuptureSeconds + SmokeSeconds;
    public const int PoolCap = 24;    // parked poses kept; a burst of kills beyond it is built, then destroyed as usual

    // Test seam: swaps the Resources load for a synthetic strip.
    public static System.Func<string, Texture2D> TextureLoader;
    public static void ClearCache() { cache.Clear(); }
    // Test seam: a pose is normally shown only in play (edit-mode suites kill
    // enemies without wanting stray objects); the pool's own suite steps it by hand.
    public static bool AllowInEditMode;

    static readonly Dictionary<string, Sprite[]> cache = new Dictionary<string, Sprite[]>();
    static readonly Stack<EnemyDeathFlipbook> free = new Stack<EnemyDeathFlipbook>(PoolCap);
    public static int FreeCount { get { return free.Count; } }
    public static int Created { get; private set; }
    public static EnemyDeathFlipbook LastSpawned { get; private set; }

    Sprite[] frames;
    SpriteRenderer renderer;
    float age;
    public float Age { get { return age; } }
    public Sprite[] Drawings { get { return frames; } }
    public SpriteRenderer Renderer { get { return renderer; } }

    [RuntimeInitializeOnLoadMethod]
    static void Init()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (mode == LoadSceneMode.Single) Prune();
    }

    // Poses whose objects went with the last scene.
    public static void Prune()
    {
        if (free.Count == 0) return;
        bool stale = false;
        foreach (var p in free) if (p == null) { stale = true; break; }
        if (!stale) return;
        var keep = new List<EnemyDeathFlipbook>();
        foreach (var p in free) if (p != null) keep.Add(p);
        free.Clear();
        for (int i = keep.Count - 1; i >= 0; i--) free.Push(keep[i]);
    }

    // Tests: destroys every parked pose and forgets it.
    public static void DestroyPool()
    {
        while (free.Count > 0)
        {
            var p = free.Pop();
            if (p == null) continue;
            if (Application.isPlaying) Destroy(p.gameObject); else DestroyImmediate(p.gameObject);
        }
        LastSpawned = null;
    }

    public static Sprite[] Frames(EnemyDef def) { return Frames(def, 0); }

    // `variant` > 0: the obsidian shard's re-coloured burst (ObsidianShards), matching the body that died.
    public static Sprite[] Frames(EnemyDef def, int variant)
    {
        if (def == null) return null;
        string path = ObsidianShards.Applies(def) ? ObsidianShards.DeathPath(variant) : null;
        string id = path ?? def.key;
        Sprite[] result;
        if (cache.TryGetValue(id, out result) && result != null && result[0] != null) return result;
        var texture = TextureLoader != null ? TextureLoader(def.key) : Resources.Load<Texture2D>(path ?? "Enemies/Death/" + def.key);
        if (texture == null || texture.width != texture.height * 3) return null;
        float side = texture.height;
        float ppu = side / Mathf.Max(.01f, def.FrameWorldSize);
        result = new Sprite[3];
        for (int i = 0; i < result.Length; i++)
        {
            result[i] = Sprite.Create(texture, new Rect(i * side, 0f, side, side), new Vector2(.5f, .5f), ppu);
            result[i].name = id + "_death_" + i;
        }
        cache[id] = result;
        return result;
    }

    static EnemyDeathFlipbook Create()
    {
        var go = new GameObject("enemy death", typeof(SpriteRenderer));
        var effect = go.AddComponent<EnemyDeathFlipbook>();
        effect.renderer = go.GetComponent<SpriteRenderer>();
        go.SetActive(false);
        Created++;
        return effect;
    }

    static EnemyDeathFlipbook Take()
    {
        while (free.Count > 0)
        {
            var p = free.Pop();
            if (p != null) return p;
        }
        return Create();
    }

    public static void Spawn(GameObject target)
    {
        if ((!Application.isPlaying && !AllowInEditMode) || target == null) return;
        var def = EnemyIdentity.Of(target);
        var frames = Frames(def, EnemyIdentity.VariantOf(target));
        if (frames == null) frames = Frames(def);
        if (frames == null) return;
        var source = target.GetComponent<SpriteRenderer>();
        var effect = Take();
        var go = effect.gameObject;
        // every bit of the last death is overwritten: where, how big, how drawn, how old
        go.transform.SetPositionAndRotation(target.transform.position, target.transform.rotation);
        go.transform.localScale = target.transform.lossyScale;
        effect.frames = frames;
        effect.age = 0f;
        effect.renderer.sprite = frames[0];
        effect.renderer.sortingLayerID = source != null ? source.sortingLayerID : 0;
        effect.renderer.sortingOrder = source != null ? Mathf.Max(source.sortingOrder, 67) : 67;
        go.SetActive(true);
        LastSpawned = effect;
    }

    void Update()
    {
        // Gameplay time, like the blast under it: frozen while the world is
        // paused, and on the death crash's clock while that plays (timeScale
        // is 0 then; Time.deltaTime would hold the pose on screen for good).
        Step(TargetExplosion.Delta());
    }

    // One frame of `dt` (tests drive it by hand).
    public void Step(float dt)
    {
        if (frames == null || renderer == null) return;
        age += dt;
        if (age < FlashSeconds) renderer.sprite = frames[0];
        else if (age < FlashSeconds + RuptureSeconds) renderer.sprite = frames[1];
        else if (age < TotalSeconds) renderer.sprite = frames[2];
        else Release();
    }

    // The pose is over: parked for the next death (or destroyed past the cap).
    void Release()
    {
        frames = null;
        gameObject.SetActive(false);
        if (free.Count < PoolCap) free.Push(this);
        else if (Application.isPlaying) Destroy(gameObject);
        else DestroyImmediate(gameObject);
    }
}
