using System.Collections.Generic;
using UnityEngine;

// The NEXT world's art, loaded before the swap needs it.
//
// A world change used to hit synchronous Resources.Loads on the frame it happens: every backdrop tile
// (BackdropSet), the fx / anim atlases, the wrap shader (BackdropTile) and the planetfall's seven textures
// (PlanetfallArt) -- tens of MB of ASTC on a phone, a visible hitch under the clouds. Now, when the gateway
// is about to open (WorldManager.OpenPortal), the destination's paths are asked for with Resources.LoadAsync,
// MaxInFlight at a time, a frame apart; the loaded objects are HELD here, so an asset sweep (AssetSweep)
// cannot free them before the swap; the swap's loaders ask Load<T>(path), which answers from the held set (a
// cache hit) and only falls back to the synchronous Resources.Load when the preload isn't there yet (a skip,
// a developer jump, a slow phone). A key is requested once (a second request for the same world is a no-op: no
// double loads); a different key, Release or Clear lets go of the hold. Out of play (edit-mode suites) the
// queue is loaded synchronously by Pump (through SyncLoader, a test seam).
public static class WorldArtPreload
{
    public const int MaxInFlight = 1;

    // Test seam: replaces the synchronous load (type, path) -> object.
    public static System.Func<System.Type, string, Object> SyncLoader;

    struct Item { public string path; public System.Type type; public string fallback; }

    static readonly Queue<Item> queue = new Queue<Item>();
    static readonly List<ResourceRequest> inFlight = new List<ResourceRequest>(MaxInFlight);
    static readonly List<string> inFlightKey = new List<string>(MaxInFlight);
    static readonly List<Item> inFlightItem = new List<Item>(MaxInFlight);
    static readonly Dictionary<string, Object> held = new Dictionary<string, Object>();
    static readonly HashSet<string> asked = new HashSet<string>();
    static readonly HashSet<string> missing = new HashSet<string>();   // asked for and not installed: answered null from the cache too

    // The planetfall's procedural textures (PlanetfallProc) still being drawn, a fixed pixel budget per Pump.
    static bool warmProc;
    // A run-start hold (BeginRunStart) lives through the menu -> gameS1 scene load.
    public static bool Pinned { get; private set; }
    public const string RunStartPrefix = "start:";

    public static string Key { get; private set; }
    // The backdrop world the held art is for (Next): the swap that shows it lets go of the hold.
    public static string Target { get; private set; }
    public static int Hits { get; private set; }
    public static int SyncLoads { get; private set; }
    public static int AsyncStarted { get; private set; }
    public static int Queued { get { return queue.Count; } }
    public static int InFlight { get { return inFlight.Count; } }
    public static int HeldCount { get { return held.Count; } }
    public static bool Done { get { return queue.Count == 0 && inFlight.Count == 0 && !warmProc; } }
    public static bool Holds(string path, System.Type type) { return held.ContainsKey(K(type, path)); }

    static string K(System.Type t, string path) { return t.Name + ":" + path; }

    // The loader every swap-time site uses: the preloaded object when there is one, else a synchronous load.
    public static T Load<T>(string path) where T : Object
    {
        Object o;
        if (held.TryGetValue(K(typeof(T), path), out o) && o != null) { Hits++; return (T)o; }
        if (missing.Contains(K(typeof(T), path))) { Hits++; return null; }
        SyncLoads++;
        return SyncLoader != null ? SyncLoader(typeof(T), path) as T : Resources.Load<T>(path);
    }

    // Queues one asset (once per path+type); `fallback` is asked for when the first is not there (anim_hires -> anim).
    public static void Request<T>(string path, string fallback = null) where T : Object
    {
        if (string.IsNullOrEmpty(path) || !asked.Add(K(typeof(T), path))) return;
        queue.Enqueue(new Item { path = path, type = typeof(T), fallback = fallback });
    }

    // Starts preloading under `key` ("backdrop:Desert"). The same key again does nothing (no double loads);
    // another key lets go of the previous hold first. `fill` queues the paths (Request calls).
    public static bool Begin(string key, System.Action fill)
    {
        if (Key == key) return false;
        Clear();
        Key = key;
        fill();
        if (Application.isPlaying) WorldArtPreloadRunner.Ensure(); else Pump();
        return true;
    }

    // One frame: collect finished requests, keep MaxInFlight going.
    public static void Pump()
    {
        for (int i = inFlight.Count - 1; i >= 0; i--)
        {
            var r = inFlight[i];
            if (r != null && !r.isDone) continue;
            if (r != null && r.asset != null) held[inFlightKey[i]] = r.asset;
            else if (r != null)
            {
                missing.Add(inFlightKey[i]);
                var done = inFlightItem[i];
                if (done.fallback != null) queue.Enqueue(new Item { path = done.fallback, type = done.type });   // anim_hires not installed -> anim
            }
            inFlight.RemoveAt(i);
            inFlightKey.RemoveAt(i);
            inFlightItem.RemoveAt(i);
        }
        if (warmProc)
        {
            if (!Application.isPlaying) PlanetfallProc.Ensure();
            if (!PlanetfallProc.Step()) warmProc = false;
        }
        bool sync = !Application.isPlaying || SyncLoader != null;
        while (queue.Count > 0 && (sync || inFlight.Count < MaxInFlight))
        {
            var it = queue.Dequeue();
            string key = K(it.type, it.path);
            if (sync)
            {
                var o = SyncLoader != null ? SyncLoader(it.type, it.path) : Resources.Load(it.path, it.type);
                if (o == null) missing.Add(key);
                if (o == null && it.fallback != null)
                {
                    it = new Item { path = it.fallback, type = it.type };
                    key = K(it.type, it.path);
                    o = SyncLoader != null ? SyncLoader(it.type, it.path) : Resources.Load(it.path, it.type);
                }
                if (o != null) held[key] = o; else missing.Add(key);
            }
            else
            {
                AsyncStarted++;
                inFlight.Add(Resources.LoadAsync(it.path, it.type));
                inFlightKey.Add(key);
                inFlightItem.Add(it);
                break;   // one request per frame
            }
        }
    }

    // The swap has used the art: let go of the hold (the world's own objects keep what they use; a sweep frees the rest).
    public static void Release(string key)
    {
        if (Key == key) Clear();
    }

    // The backdrop swap to `world` has used what was preloaded: let go of the hold, unless it is for another world
    // (the run's Space sky is swapped in first while the starting planet's art is still on its way).
    public static void Swapped(string world)
    {
        if (Target != null && Target != world) return;
        Clear();
    }

    // A scene finished loading (GameStateReset): the hold goes, except a run-start hold (BeginRunStart) on its way
    // into the game scene, which is kept once and then unpinned.
    public static void SceneLoaded(string scene, bool single)
    {
        if (!single) return;
        if (Pinned && scene == "gameS1") { Pinned = false; return; }
        Clear();
        if (scene == MenuScene && Application.isPlaying) MenuPreload();
    }

    // The home screen preloads the art of the planet the next PLAY will begin on, so the run start does not load it.
    // The start world can change in the menu (Options -> START WORLD); WorldManager.Start lets go of a hold that turns
    // out to be for another world, and the game then loads what it needs synchronously, as it always did.
    public const string MenuScene = "startS4";

    static void MenuPreload()
    {
        try { BeginRunStart(WorldManager.RunStartWorld(true)); }
        catch (System.Exception e) { Debug.LogWarning("[WorldArtPreload] menu preload skipped: " + e.Message); }
    }

    public static void Clear()
    {
        Pinned = false;
        queue.Clear();
        inFlight.Clear();
        inFlightKey.Clear();
        inFlightItem.Clear();
        held.Clear();
        asked.Clear();
        missing.Clear();
        Key = null;
        Target = null;
    }

    // The pump's scene is going away: the hold goes with it, except a run-start hold, which keeps what it already
    // has (the unfinished loads are dropped; their art is loaded synchronously at the swap, as before).
    public static void RunnerGone()
    {
        if (!Pinned) { Clear(); return; }
        queue.Clear(); inFlight.Clear(); inFlightKey.Clear(); inFlightItem.Clear();
    }

    // Tests.
    public static void ResetCounters() { Hits = SyncLoads = AsyncStarted = 0; }

    // ---- what a destination preloads ----

    // The backdrop of world `worldIndex` (its displayName) as a new BackdropSet will ask for it: the variant is
    // reserved now so the set uses the one that was loaded. Missing art is simply not found.
    public static void BackdropFor(string displayName)
    {
        var spec = BackdropCatalog.For(displayName);
        string key = "backdrop:" + spec.world;
        Begin(key, () => { Target = spec.world; QueueBackdrop(spec); });
    }

    public static void QueueBackdrop(BackdropCatalog.Spec spec)
    {
        string folder = BackdropCatalog.Folder(spec.world);
        string tileFolder = folder;
        if (spec.variantSets > 0)
        {
            int v = BackdropVariants.For(spec.world).Reserve(spec.variantSets);
            tileFolder = BackdropCatalog.TileFolder(spec.world, v);
        }
        bool wrap = false;
        foreach (var layer in spec.layers)
        {
            if (layer.kind == BackdropCatalog.Kind.Pieces) continue;
            if (layer.wrapBlend > 0f) wrap = true;
            if (spec.world == "Space" && layer.name == "sky") continue;   // the sky is picked by SpaceSkySelection at the swap
            Request<Sprite>(tileFolder + layer.texture);
        }
        if (wrap) Request<Shader>(BackdropTile.WrapShader);
        if (Application.isPlaying && CloudShadowLayer.Enabled && CloudShadowLayer.Has(spec.world)) CloudShadowArt.Shapes();   // the cloud-shadow atlas is procedural: build it now, not mid-play
        foreach (var atlas in AtlasesOf(spec.world))
        {
            Request<Texture2D>(folder + atlas);
            Request<TextAsset>(folder + atlas);
        }
        Request<Shader>(BackdropGrade.ShaderName);
        Request<Texture2D>(folder + BackdropCatalog.AtlasFx);
        Request<TextAsset>(folder + BackdropCatalog.AtlasFx);
        Request<Texture2D>(folder + BackdropCatalog.AtlasAnimHires, folder + BackdropCatalog.AtlasAnim);
        Request<TextAsset>(folder + BackdropCatalog.AtlasAnimHires, folder + BackdropCatalog.AtlasAnim);
    }

    // The menu, for a run that will begin on `world` with its planetfall (WorldEntry): the planet's backdrop and its
    // planetfall art are loaded now, a frame apart, and held through the scene load into the game, so
    // Planetfall.SpawnEntry and the swap under the clouds find them (zero synchronous loads on the happy path).
    // Skipped on a low-end phone (the hold is tens of MB for as long as the menu is up) and when nothing is to be
    // preloaded. WorldManager.Start lets go of a hold that is for another world.
    public static bool BeginRunStart(int world)
    {
        return Application.isPlaying && QueueRunStart(world);
    }

    // BeginRunStart without the play-mode guard (edit-mode suites drive it).
    public static bool QueueRunStart(int world)
    {
        if (PerfTier.LowEnd) return false;
        if (WorldEntry.Plan(world, false) != WorldEntry.Kind.Planetfall) return false;
        var def = PlanetfallCatalog.For(world - 1, world, false);
        if (def == null || world < 0 || world >= WorldManager.Worlds.Length) return false;
        string name = WorldManager.Worlds[world].displayName;
        bool started = Begin(RunStartKey(world), () =>
        {
            var spec = BackdropCatalog.For(name);
            Target = spec.world;
            QueuePlanetfall(def);
            QueueBackdrop(spec);
        });
        if (started) Pinned = true;
        return started;
    }

    // WorldManager.Start: the run begins on `world`. A hold from the menu for another world (the pilot changed the
    // start world, a developer jump, a replay) goes; a planetfall start with no menu hold preloads its own planet now,
    // so the clouds' swap (ShowEntryWorld, a few seconds on) is a cache hit.
    public static void RunStarted(int world, bool planetfall)
    {
        if (Key != null && Key.StartsWith(RunStartPrefix) && Key != RunStartKey(world)) Clear();
        if (!planetfall || !Application.isPlaying || Key == RunStartKey(world)) return;
        Begin(RunStartKey(world), () =>
        {
            var spec = BackdropCatalog.For(WorldManager.Worlds[world].displayName);
            Target = spec.world;
            QueueBackdrop(spec);
        });
    }

    public static string RunStartKey(int world) { return RunStartPrefix + world; }

    // The piece atlases (sprite sheet + manifest) each world's directors ask for when the backdrop is built. They were
    // loaded synchronously inside the swap (up to 17 atlases for Tide); PerfPass2Test.EveryWorldSwapIsPreloaded builds
    // every world's set and fails when a director starts asking for one that is not listed here. An atlas listed but
    // not installed is simply remembered as missing. (The fx / anim atlases are queued by QueueBackdrop itself.)
    public static string[] AtlasesOf(string world)
    {
        switch (world)
        {
            case "Space": return new[] { "extras", "neon_frames", "asteroid_fx", "twinkle", "nebula", "debris", "wrecks", "outposts", "docks" };
            case "Frost": return new[] { "landmarks", "sites", "weather", "smoke", "steam", "fire_lights", "beacons", "aurora", "docks" };
            case "Verdant": return new[] { "landmarks", "pipes", "fires", "sites", "weather", "lights", "smoke", "wildfire", "firesmoke", "leaks" };
            case "Ember": return new[] { "landmarks", "pipes", "fires", "sites", "weather", "lights", "smoke", "lavafire", "eruption", "leaks", "docks" };
            case "Tide": return new[] { "landmarks", "pipes", "fires", "sites", "weather", "smoke", "smoke_2", "flames", "flames_2", "leaks", "leaks_2", "lights", "lights_2", "surf", "surf_2", "surf_3", "surf_4", "docks" };
            case "Storm": return new[] { "landmarks", "sites", "weather", "lightning", "docks", "rotors", "thunder" };
            case "Desert": return new[] { "landmarks", "pipes", "fires", "sites", "docks", "weather", "smoke", "dust", "dust_2", "dust_3", "shimmer", "shimmer_2", "leaks", "lights", "lights_2", "worm", "worm_giant" };
            default: return new string[0];
        }
    }

    public static void QueuePlanetfall(PlanetfallDef def)
    {
        warmProc = true;   // the ring / reticle / vignette / plume textures, drawn in slices alongside the loads
        string f = def.folder;
        foreach (var n in new[] { def.planet, def.limb, def.deck, def.deckDark, def.entryFx, def.burst, def.streaks })
            Request<Texture2D>(f + n);
        Request<Shader>(Planetfall.ShaderPath);
    }

    // WorldManager.OpenPortal: leaving `from` for `to` (loop: the way round to Space).
    public static void Next(int from, int to, bool loop, string toDisplayName)
    {
        if (!Application.isPlaying) return;   // edit-mode suites drive Begin / QueueBackdrop themselves
        var def = PlanetfallCatalog.For(from, to, loop);
        string key = "next:" + to + (def != null ? ":pf" : "");
        Begin(key, () =>
        {
            var spec = BackdropCatalog.For(toDisplayName);
            Target = spec.world;
            if (def != null) QueuePlanetfall(def);
            QueueBackdrop(spec);
        });
    }
}

// Pumps the preload each frame while something is loading.
public sealed class WorldArtPreloadRunner : MonoBehaviour
{
    static WorldArtPreloadRunner live;

    public static void Ensure()
    {
        if (live != null) { live.enabled = true; return; }
        live = new GameObject("~WorldArtPreload").AddComponent<WorldArtPreloadRunner>();   // dies with its scene (and lets go of the hold)
    }

    void Update()
    {
        WorldArtPreload.Pump();
        if (WorldArtPreload.Done) enabled = false;
    }

    void OnDestroy()
    {
        if (live != this) return;
        live = null;
        WorldArtPreload.RunnerGone();
    }
}

