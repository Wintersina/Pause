using System.Collections.Generic;
using UnityEngine;

// The pause-teleport kill's own blast: an enemy the ship blinks onto is
// "erased by the pause" rather than blown up. It freezes inside a time-stop
// viewfinder, the PAUSE bars flash over it, a glitch tears it sideways, it
// implodes to a point and shatters into ice-cyan time crystals with violet
// glitch pixels -- never the fiery cel explosion a weapon kill makes (and
// never the player's red).
//
// Art: Resources/Vfx/teleport_kill_atlas.png, rendered by
// Art/Resources/Vfx/src~/teleport_kill.py (PixelKit neon pixel art):
// 8 frames, 4 x 2, 256 px cells (64 game px x4), read left to right, top to
// bottom. A frame is 1 world unit at scale 1, so localScale is world size.
public static class TeleportKillArt
{
    public const string AtlasPath = "Vfx/teleport_kill_atlas";
    public const int Columns = 4, Rows = 2;
    public const int Frames = Columns * Rows;
    public const float PixelsPerUnit = 256f;
    // Hold per frame at 24 fps -- teleport_kill.py TICKS.
    public static readonly int[] Ticks = { 1, 2, 2, 1, 2, 2, 2, 3 };

    static Sprite[] sprites;

    public static Sprite Frame(int i)
    {
        // Sprite.Create()d frames die with an editor scene swap: slice again.
        if (sprites == null || sprites[0] == null)
        {
            var tex = Resources.Load<Texture2D>(AtlasPath);
            if (tex == null) return null;
            sprites = new Sprite[Frames];
            float w = tex.width / (float)Columns, h = tex.height / (float)Rows;
            for (int k = 0; k < Frames; k++)
            {
                int col = k % Columns, row = k / Columns;
                sprites[k] = Sprite.Create(tex, new Rect(col * w, (Rows - 1 - row) * h, w, h),
                                           new Vector2(.5f, .5f), w, 0, SpriteMeshType.FullRect);
                sprites[k].name = "teleport_kill_" + k;
            }
        }
        return sprites[Mathf.Clamp(i, 0, Frames - 1)];
    }
}

// Pooled player for TeleportKillArt, the same rules as the weapon
// explosions (FlipbookFx): grows only to the peak in flight, then reuses;
// over the cap it steals the oldest. Nothing allocates per frame. Runs on
// gameplay time (TargetExplosion.Delta) -- frozen while the world is paused,
// readable through the ultimate's slow motion -- and drifts down with the
// scrolling world like the debris it is.
public class TeleportKillFx : MonoBehaviour
{
    public const int MaxInFlight = 16;
    public const int SortingOrder = 66;   // with the target explosions
    // A touch bigger than a weapon explosion of the same target size: the
    // crystals fly out past where the hull was.
    public const float SizeScale = 1.15f;

    static readonly List<TeleportKillFx> pool = new List<TeleportKillFx>();
    static Transform root;

    SpriteRenderer sr;
    float clock;

    public bool Active { get; private set; }
    public int Frame { get; private set; }
    public float StartedAt { get; private set; }
    public Sprite CurrentSprite => sr != null ? sr.sprite : null;

    // Every blast played (tests, diagnostics).
    public static int Played { get; private set; }
    public static TeleportKillFx Last { get; private set; }

    public static int PoolSize { get { Purge(); return pool.Count; } }

    public static int ActiveCount
    {
        get
        {
            int n = 0;
            for (int i = 0; i < pool.Count; i++) if (pool[i] != null && pool[i].Active) n++;
            return n;
        }
    }

    public static float WorldSizeFor(GameObject target)
    {
        var size = TargetExplosion.SizeFor(target);
        return TargetExplosion.WorldSizeFor(size, TargetExplosion.Kind.Metal) * SizeScale;
    }

    public static TeleportKillFx Spawn(GameObject target)
    {
        if (target == null) return null;
        return Spawn(target.transform.position, WorldSizeFor(target));
    }

    public static TeleportKillFx Spawn(Vector3 at, float worldSize)
    {
        var fx = Take();
        fx.Play(at, worldSize);
        Played++;
        Last = fx;
        return fx;
    }

    static TeleportKillFx Take()
    {
        if (root == null)
        {
            root = new GameObject("~TeleportKillFx").transform;
            pool.Clear();
        }
        Purge();
        TeleportKillFx oldest = null;
        for (int i = 0; i < pool.Count; i++)
        {
            var f = pool[i];
            if (!f.Active) return f;
            if (oldest == null || f.StartedAt < oldest.StartedAt) oldest = f;
        }
        if (pool.Count >= MaxInFlight && oldest != null) return oldest;
        var go = new GameObject("~teleportKill", typeof(SpriteRenderer));
        go.transform.SetParent(root, false);
        var fx = go.AddComponent<TeleportKillFx>();
        fx.sr = go.GetComponent<SpriteRenderer>();
        fx.sr.sortingOrder = SortingOrder;
        go.SetActive(false);
        pool.Add(fx);
        return fx;
    }

    // Edit-mode tests open fresh scenes under the static pool.
    static void Purge()
    {
        if (root == null) { pool.Clear(); return; }
        for (int i = pool.Count - 1; i >= 0; i--) if (pool[i] == null) pool.RemoveAt(i);
    }

    void Play(Vector3 at, float worldSize)
    {
        clock = 0f;
        Frame = 0;
        StartedAt = Time.unscaledTime;
        transform.position = new Vector3(at.x, at.y, 0f);
        // A quarter turn at random so repeats don't read as stamps; the
        // PAUSE bars stay upright on frames 1-2 either way (0 / 180 only).
        transform.rotation = Quaternion.Euler(0f, 0f, Random.Range(0, 2) * 180f);
        transform.localScale = Vector3.one * worldSize;
        sr.sprite = TeleportKillArt.Frame(0);
        sr.color = Color.white;
        Active = true;
        gameObject.SetActive(true);
    }

    void Update()
    {
        if (Active) Tick(TargetExplosion.Delta());
    }

    public void Tick(float dt)
    {
        if (!Active || dt <= 0f) return;
        clock += dt;
        if (TargetExplosion.WorldScrolling)
            transform.position += Vector3.down * (moveBackGround.speed * dt * 30f);
        int f = WeaponArt.FrameAt(TeleportKillArt.Ticks, clock, false);
        if (f >= TeleportKillArt.Frames) { Stop(); return; }
        if (f != Frame) { Frame = f; sr.sprite = TeleportKillArt.Frame(f); }
    }

    public void Stop()
    {
        Active = false;
        gameObject.SetActive(false);
    }
}
