using System.Collections.Generic;
using UnityEngine;

// The original 16-frame fireball (Art/Resources/Weapons/LegacyExplosion.png,
// 128 px cells, point filtered), played as an overlay on top of the death
// visuals of enemy ships, elites, the player and bosses. It never replaces
// them. Once spawned it is left behind in world space: it drifts down the
// screen at the world's scroll speed (the same rule FlipbookFx uses) and
// ignores whatever the dying ship was doing. Pooled; one shared spawner.
//
// Rocks, asteroids, mines and other debris obstacles never get it
// (IsRockLike): every ship-death hook runs its target through that filter.
public class LegacyExplosionFx : MonoBehaviour
{
    public const int FrameCount = 16;
    public const float Fps = 26f;
    public const int SortingOrder = 70;   // above the v2 blast (66) and death strips (67)
    // world-unit width of the drawn cell, by who died
    public const float SmallSize = 1.1f, MediumSize = 1.5f, LargeSize = 2f, EliteSize = 2.8f, PlayerSize = 2.6f;

    static Sprite[] frames;
    static readonly Stack<LegacyExplosionFx> pool = new Stack<LegacyExplosionFx>();
    public static int ActiveCount { get; private set; }

    SpriteRenderer sr;
    float clock;

    public static Sprite[] Frames()
    {
        if (frames != null && frames[0] != null) return frames;
        var tex = Resources.Load<Texture2D>("Weapons/LegacyExplosion");
        if (tex == null) return null;
        tex.filterMode = FilterMode.Point;
        int cell = tex.height;
        int n = Mathf.Min(FrameCount, tex.width / cell);
        frames = new Sprite[n];
        for (int i = 0; i < n; i++)
        {
            frames[i] = Sprite.Create(tex, new Rect(i * cell, 0f, cell, cell), new Vector2(.5f, .5f), cell, 0, SpriteMeshType.FullRect);
            frames[i].name = "LegacyExplosion_" + i;
        }
        return frames;
    }

    // Rocks and mines: the overlay is for ships only.
    public static bool IsRockLike(GameObject go)
    {
        if (go == null) return true;
        if (go.CompareTag("Astr")) return true;
        var def = EnemyIdentity.Of(go);
        if (def != null) return def.role == EnemyRole.Rock || def.role == EnemyRole.Mine;
        string n = go.name.ToLowerInvariant();
        return n.Contains("rock") || n.Contains("asteroid") || n.Contains("boulder") || n.Contains("debris") || n.Contains("mine");
    }

    // An enemy ship that just died (called from TargetExplosion.Spawn(GameObject)).
    public static void ForShip(GameObject ship, TargetExplosion.Size size)
    {
        if (ship == null || IsRockLike(ship)) return;
        Spawn(ship.transform.position, size == TargetExplosion.Size.Large ? LargeSize : size == TargetExplosion.Size.Medium ? MediumSize : SmallSize);
    }

    public static void Spawn(Vector3 at, float worldSize)
    {
        if (!Application.isPlaying) return;
        var f = Frames();
        if (f == null) return;
        LegacyExplosionFx fx = null;
        while (pool.Count > 0 && fx == null) fx = pool.Pop();
        if (fx == null)
        {
            var go = new GameObject("LegacyExplosion");
            fx = go.AddComponent<LegacyExplosionFx>();
            fx.sr = go.AddComponent<SpriteRenderer>();
            fx.sr.sortingOrder = SortingOrder;
        }
        fx.clock = 0f;
        fx.sr.sprite = f[0];
        fx.transform.position = new Vector3(at.x, at.y, 0f);
        fx.transform.localScale = Vector3.one * worldSize;
        fx.gameObject.SetActive(true);
        ActiveCount++;
    }

    void Update() { Tick(TargetExplosion.Delta()); }

    public void Tick(float dt)
    {
        if (dt <= 0f) return;
        clock += dt;
        // left behind: the world scrolls past it, same rule as FlipbookFx
        if (TargetExplosion.WorldScrolling || buttonClicks.playerDied)   // after the player dies the world is frozen but its last speed stays in moveBackGround.speed
            transform.position += Vector3.down * (moveBackGround.speed * dt * 30f);
        int f = Mathf.FloorToInt(clock * Fps);
        var fr = Frames();
        if (fr == null || f >= fr.Length) { Release(); return; }
        sr.sprite = fr[f];
    }

    void Release()
    {
        gameObject.SetActive(false);
        ActiveCount--;
        pool.Push(this);
    }
}

// Boss death: a staggered run of overlays at random points over the hull.
public class LegacyBossBlasts
{
    public const int Count = 9;
    public const float Window = .7f;        // seconds the run is spread over
    public const float SpreadK = .38f;      // +-this x boss world size, from its centre
    readonly float[] at = new float[Count];
    readonly Vector2[] off = new Vector2[Count];
    readonly float[] size = new float[Count];
    int next;
    float clock;

    public LegacyBossBlasts(float bossWorldSize)
    {
        for (int i = 0; i < Count; i++)
        {
            // evenly staggered with jitter, so the run reads as a chain not a clump
            at[i] = Window * (i + Random.value * .8f) / Count;
            off[i] = new Vector2(Random.Range(-1f, 1f), Random.Range(-1f, 1f)) * bossWorldSize * SpreadK;
            size[i] = Random.Range(1.6f, 3.6f);
        }
    }

    // `centre` is where the boss is now; each blast is planted there and left behind.
    public void Step(float dt, Vector3 centre)
    {
        clock += dt;
        while (next < Count && clock >= at[next])
        {
            LegacyExplosionFx.Spawn(centre + (Vector3)off[next], size[next]);
            next++;
        }
    }
}
