using UnityEngine;

// COVER BOULDERS: big decorative LANDFORMS of the backdrop (docs/cover-boulders.md), in every world.
//
// One huge boulder now and then (CoverBoulderDirector spawns it, the table CoverBoulderTable says how) is pinned to the
// backdrop's ground and scrolls with it, drawn behind everything (CoverBoulder.SortingOrder). It is scenery only: it has
// no collision and no effect on the ship, shots, beams, enemies, elites or world hazards. A boss arriving, a world change
// or a death just fades it out.
//
// Everything is pooled (Slots boulders, built once by Prewarm) and steps on the world's clock.
public static class CoverBoulders
{
    // ---- tunables (the per-world numbers are CoverBoulderTable) ----
    public const int Slots = 2;                  // pool size; MaxAlive 1 on screen, the 2nd slot is a fade still playing
    public const int MaxAlive = 1;
    public const float FirstSeconds = 20f;       // none in the first 20 s of a world (the 8 s calm start is inside it)
    public const float BossLeadSeconds = 14f;    // none in the run-up to a boss
    public const float SpawnMargin = .4f;        // enters this far above the view
    // bigger and slower than the old cover boulders, with a steady depth haze
    public const float DecorScale = 1.5f, DecorHaze = .2f, DecorMaxShare = .98f;    // a boulder is at most this share of the rail-to-rail width wide

    // test / tool switches
    public static bool Disabled;                 // (a scene without boulders)
    public static bool TestAnyWorld;             // spawn in worlds whose art is not wired (they borrow Space's)

    public static int Spawned;                   // (tests, previews)

    // ---- the pool ----
    static readonly CoverBoulder[] pool = new CoverBoulder[Slots];
    static GameObject root;
    static readonly StepContext ctx = new StepContext();

    public sealed class StepContext
    {
        public float viewBottom;
    }

    public static Transform Root
    {
        get
        {
            if (root == null) { root = new GameObject("~CoverBoulders"); pool[0] = pool[1] = null; }
            return root.transform;
        }
    }

    // Builds the pool (once); the director calls it at scene start so nothing is made mid-run.
    public static void Prewarm()
    {
        var r = Root;
        for (int i = 0; i < pool.Length; i++)
            if (pool[i] == null) { pool[i] = new CoverBoulder(); pool[i].Build(r, "Boulder" + i); }
    }

    // Every boulder alive (the pool / one-at-a-time bookkeeping).
    public static int AliveAny
    {
        get { int n = 0; for (int i = 0; i < pool.Length; i++) if (pool[i] != null && pool[i].Alive) n++; return n; }
    }

    public static CoverBoulder First
    {
        get { for (int i = 0; i < pool.Length; i++) if (pool[i] != null && pool[i].Alive) return pool[i]; return null; }
    }

    // ---- life ----

    public static CoverBoulder Spawn(int world, int variant)
    {
        Prewarm();
        CoverBoulder slot = null;
        for (int i = 0; i < pool.Length; i++) if (!pool[i].Alive) { slot = pool[i]; break; }
        if (slot == null) return null;
        var e = CoverBoulderTable.For(world);
        var art = CoverBoulderArt.For(world);
        if (art == null || art.Length == 0) return null;
        variant = Mathf.Clamp(variant, 0, Mathf.Min(art.Length, e.Variants) - 1);
        var v = art[variant];
        float boundPx = 0f;
        for (int i = 0; i < v.circles.Length && i < CoverBoulder.MaxCircles; i++)
            boundPx = Mathf.Max(boundPx, new Vector2(v.circles[i].x, v.circles[i].y).magnitude + v.circles[i].r);
        // huge and slow (far below the ship), anywhere across the view
        float width = Mathf.Min(e.widths[variant] * DecorScale, Edge * 2f * DecorMaxShare);
        float bound = boundPx * width / v.artWidth;
        float x = Random.Range(-Edge * .55f, Edge * .55f);
        float y = CameraFit.ViewTop + bound + SpawnMargin;
        // anchored: the backdrop's ground carries it (CoverBoulder.groundTile), it has no motion of its own
        slot.Spawn(world, variant, v, width, new Vector2(x, y), Random.value, e.TintFor(CoverBoulderDirector.BackdropVariant(world)));
        Spawned++;
        return slot;
    }

    // The ground tile (the backdrop's "mid" layer) a landform of `world` is pinned to, or null when that world's backdrop is not up.
    public static BackdropTile GroundTile(string world)
    {
        var wb = WorldBackdrop.Instance;
        var set = wb != null ? wb.Current : null;
        if (set == null || set.Spec.world != world) return null;
        foreach (var t in set.Tiles) if (t.layer.name == "mid") return t;
        return null;
    }

    // How fast the world's ground scrolls (u/s) -- the pace of a landform with no backdrop to be pinned to.
    public static float GroundRate(string world)
    {
        return BackdropCatalog.For(world).Rate("mid") * WorldBackdrop.ScrollVelocity(moveBackGround.speed) * WorldBackdrop.ScrollBoost;
    }

    // Where the backdrop's root sits relative to the camera's resting position (the CameraKick's shake, see WorldBackdrop.FollowCamera).
    public static Vector2 AnchorOrigin;

    // WorldBackdrop.Step, after the tiles have moved: every landform is drawn on its ground tile's current travel.
    public static void AnchorToBackdrop()
    {
        for (int i = 0; i < pool.Length; i++) if (pool[i] != null) pool[i].Anchor();
    }

    public static void Clear()
    {
        for (int i = 0; i < pool.Length; i++) if (pool[i] != null) pool[i].Release();
    }

    // Everything gone and the objects with it (scene change, tests).
    public static void Dispose()
    {
        for (int i = 0; i < pool.Length; i++) pool[i] = null;
        if (root != null) BossUtil.Kill(root);
        root = null;
        CoverBoulderArt.Forget();
    }

    // The quiet exit of every boulder (a world change, a death, a transition, a boss arriving).
    public static void DismissAll()
    {
        for (int i = 0; i < pool.Length; i++) if (pool[i] != null) pool[i].Dismiss();
    }

    // One running frame of world time.
    public static void Step(float dt)
    {
        if (dt <= 0f) return;
        ctx.viewBottom = CameraFit.ViewBottom;
        for (int i = 0; i < pool.Length; i++) if (pool[i] != null && pool[i].Alive) pool[i].Step(dt, ctx);
    }

    // The rails' drawn inner face: a landform is kept within it.
    public static float Edge => BossRails.DrawnInnerEdge;
}
