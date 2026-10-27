using System.Collections.Generic;
using UnityEngine;

// Warms the art a world loads LAZILY on first use in the middle of play: the
// boss's atlas and shots (BossArt, read at the boss's entrance), its elites'
// strips (EliteArt, read when an elite spawns) and the cover boulders
// (CoverBoulderArt, read at the first boulder). Each of those is a synchronous
// Resources.Load of a multi-megabyte ASTC sheet, i.e. a 20-80 ms hitch on a
// phone at the worst moment. Instead, as a world begins (the run's 8 s calm
// start, or under the planetfall clouds), the same paths are asked for with
// Resources.LoadAsync, a couple at a time; a later Resources.Load of a path
// already loaded returns the resident texture at once. The loaded objects are
// HELD here (so an asset sweep cannot drop them before their first use) until
// the next world begins or the scene goes: one world's worth, the same art the
// world would load anyway, just earlier. The lazy loaders and their unload
// paths are unchanged: nothing is sliced or cached on their behalf.
//
// Out of play (edit-mode suites) Pump loads synchronously through SyncLoader.
public static class ArtWarm
{
    public const int MaxInFlight = 2;

    // Test seam / edit mode: a synchronous load (Resources.Load by default).
    public static System.Func<string, Object> SyncLoader;

    static readonly List<string> queue = new List<string>(32);
    static readonly HashSet<string> queued = new HashSet<string>();
    static readonly List<ResourceRequest> inFlight = new List<ResourceRequest>(MaxInFlight);
    static readonly List<Object> held = new List<Object>(32);

    public static int World { get; private set; } = -1;
    public static int Queued { get { return queue.Count; } }
    public static int InFlight { get { return inFlight.Count; } }
    public static int HeldCount { get { return held.Count; } }
    public static bool Done { get { return queue.Count == 0 && inFlight.Count == 0; } }
    public static bool Holds(Object o) { return o != null && held.Contains(o); }

    // Every path world `world` will load lazily in play, in the order it is wanted
    // (the boss first: its warning comes before any elite's dock).
    public static void Paths(int world, List<string> into)
    {
        into.Clear();
        BossArt.WarmPaths(BossCatalog.ForWorld(world), into);
        var elites = new List<EliteDef>();
        EliteCatalog.ForWorld(world, elites);
        foreach (var d in elites) EliteArt.WarmPaths(d, into);
        CoverBoulderArt.WarmPaths(world, into);
    }

    // World `world` begins: drop the last world's hold and queue this one's art.
    public static void Begin(int world)
    {
        Forget();
        World = world;
        var paths = new List<string>();
        Paths(world, paths);
        foreach (var p in paths) if (!string.IsNullOrEmpty(p) && queued.Add(p)) queue.Add(p);
        if (Application.isPlaying) ArtWarmRunner.Ensure();
        else Pump();
    }

    // One frame: collect finished loads, keep up to MaxInFlight going.
    public static void Pump()
    {
        for (int i = inFlight.Count - 1; i >= 0; i--)
        {
            var r = inFlight[i];
            if (r == null) { inFlight.RemoveAt(i); continue; }
            if (!r.isDone) continue;
            inFlight.RemoveAt(i);
            if (r.asset != null) held.Add(r.asset);
        }
        bool sync = !Application.isPlaying || SyncLoader != null;
        while (queue.Count > 0 && (sync || inFlight.Count < MaxInFlight))
        {
            string path = queue[0];
            queue.RemoveAt(0);
            if (sync)
            {
                var o = SyncLoader != null ? SyncLoader(path) : Resources.Load<Object>(path);
                if (o != null) held.Add(o);
            }
            else inFlight.Add(Resources.LoadAsync(path, typeof(Object)));
        }
    }

    // Lets go of everything: the lazy caches keep what is in use, the next
    // asset sweep frees the rest (AssetSweep / a scene load).
    public static void Forget()
    {
        queue.Clear();
        queued.Clear();
        inFlight.Clear();
        held.Clear();
        World = -1;
    }
}

// Pumps the warm-up each frame while something is loading; dies with its scene.
public sealed class ArtWarmRunner : MonoBehaviour
{
    static ArtWarmRunner live;

    public static void Ensure()
    {
        if (live != null) { live.enabled = true; return; }
        live = new GameObject("~ArtWarm").AddComponent<ArtWarmRunner>();
    }

    void Update()
    {
        ArtWarm.Pump();
        if (ArtWarm.Done) enabled = false;
    }

    void OnDestroy()
    {
        if (live == this) { live = null; ArtWarm.Forget(); }
    }
}
