using System.Collections.Generic;
using System.IO;
using UnityEngine;

// Perf pass 2: the planetfall's procedural textures built once (and in slices), no per-spawn GameObject.Find, the
// run-start planet preloaded (menu hold through the scene load, zero synchronous loads), the variant probe cached,
// the one low-end decision, and the per-frame Camera.main lookups gone from the hot paths.
//   scripts/unity-batch.sh -executeMethod AllTests.RunSuites -suites PerfPass2Test
public static class PerfPass2Test
{
    static int fails;
    static void Check(string what, bool ok)
    {
        Debug.Log((ok ? "[PERF2] PASS  " : "[PERF2] FAIL  ") + what);
        if (!ok) fails++;
    }

    public static void Run() { TestHarness.Exit(Execute()); }

    public static int Execute()
    {
        fails = 0;
        using var sandbox = new TestHarness.Sandbox();
        var made = new List<Object>();
        try
        {
            ProceduralTextures();
            ProceduralWarmedByPreload();
            SceneLookupCaches(made);
            RailXAndTrailsDoNotFind(made);
            RunStartPreload();
            EveryWorldSwapIsPreloaded();
            VariantProbe();
            LowEndDecision();
            SourceAudit();
        }
        finally
        {
            WorldArtPreload.Clear();
            WorldArtPreload.SyncLoader = null;
            SceneLookup.CacheOutOfPlay = false;
            SceneLookup.ForgetAll();
            PerfTier.Force(null);
            for (int w = 0; w < WorldManager.Worlds.Length; w++) BackdropVariants.For(WorldManager.Worlds[w].displayName).Reset();
            foreach (var o in made) if (o != null) Object.DestroyImmediate(o);
        }
        Debug.Log("[PERF2] " + (fails == 0 ? "ALL PASS" : fails + " FAILED"));
        return fails;
    }

    // ---- 1. planetfall procedural textures ----

    // The pre-pass drawing, verbatim: one lambda per pixel.
    static byte[] Reference(int w, int h, System.Func<float, float, float> alpha)
    {
        var a = new byte[w * h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                a[y * w + x] = (byte)Mathf.RoundToInt(Mathf.Clamp01(alpha((x + .5f) / w * 2f - 1f, (y + .5f) / h * 2f - 1f)) * 255f);
        return a;
    }

    static bool SameAlpha(Sprite s, byte[] want)
    {
        var px = s.texture.GetPixels32();
        if (px.Length != want.Length) return false;
        for (int i = 0; i < px.Length; i++) if (px[i].a != want[i] || px[i].r != 255 || px[i].g != 255 || px[i].b != 255) return false;
        return true;
    }

    static void ProceduralTextures()
    {
        PlanetfallProc.Reset();
        PlanetfallProc.ResetCounters();
        PlanetfallProc.KeepReadable = true;
        Check("nothing is drawn before it is asked for", !PlanetfallProc.Ready && PlanetfallProc.Builds == 0);
        int steps = 0;
        while (PlanetfallProc.Step()) steps++;
        Check("the drawing is spread over several slices (" + (steps + 1) + " steps of " + PlanetfallProc.PixelsPerStep + " pixels)", steps >= 8);
        Check("five textures were built once", PlanetfallProc.Ready && PlanetfallProc.Builds == 5);

        const float R = PlanetfallArt.RingRadius, Q = PlanetfallArt.ReticleRadius;
        Check("white is white", SameAlpha(PlanetfallProc.White, Reference(4, 4, (x, y) => 1f)));
        Check("the ring is pixel-identical to the old per-pixel drawing", SameAlpha(PlanetfallProc.Ring, Reference(256, 256, (x, y) =>
        {
            float d = Mathf.Sqrt(x * x + y * y);
            float ring = Mathf.Exp(-Mathf.Pow((d - R) / .075f, 2f));
            float fill = d < R ? .10f * Mathf.SmoothStep(0f, 1f, d / R) : 0f;
            return Mathf.Clamp01(ring + fill);
        })));
        Check("the reticle is pixel-identical to the old drawing", SameAlpha(PlanetfallProc.Reticle, Reference(256, 256, (x, y) =>
        {
            float d = Mathf.Sqrt(x * x + y * y);
            float deg = Mathf.Repeat(Mathf.Atan2(y, x) * Mathf.Rad2Deg, 90f);
            bool gap = deg > 33f && deg < 57f;
            float line = Mathf.Clamp01(1.4f - Mathf.Abs(d - Q) / .03f);
            float glow = .35f * Mathf.Exp(-Mathf.Pow((d - Q) / .07f, 2f));
            float ring = gap ? 0f : Mathf.Max(line, glow);
            bool tickArm = Mathf.Abs(x) < .035f || Mathf.Abs(y) < .035f;
            float tick = tickArm && d > Q - .24f && d < Q - .05f ? 1f : 0f;
            return Mathf.Max(ring, tick);
        })));
        Check("the vignette is pixel-identical", SameAlpha(PlanetfallProc.Vignette, Reference(64, 128, (x, y) =>
        {
            float d = Mathf.Sqrt(x * x * .8f + y * y);
            return Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(.55f, 1.15f, d));
        })));
        Check("the plume is pixel-identical", SameAlpha(PlanetfallProc.Plume, Reference(64, 128, (x, y) =>
        {
            float along = (1f - y) * .5f;
            float half = Mathf.Lerp(.95f, .12f, Mathf.Pow(along, .7f));
            float across = Mathf.Exp(-3f * (x / half) * (x / half));
            return across * (1f - PlanetfallTimeline.Ramp(along, .15f, 1f)) * PlanetfallTimeline.Ramp(along, 0f, .06f);
        })));
        Check("the plume pivots on the nozzles (top centre), the rest on the centre",
              Mathf.Abs(PlanetfallProc.Plume.pivot.y / PlanetfallProc.Plume.rect.height - 1f) < 1e-4f &&
              Mathf.Abs(PlanetfallProc.Ring.pivot.x / PlanetfallProc.Ring.rect.width - .5f) < 1e-4f);

        PlanetfallProc.KeepReadable = false;
        // repeat descents cost nothing: same sprites, nothing drawn again
        var def = PlanetfallCatalog.For(0, 1, false);
        var first = PlanetfallArt.Load(def);
        var ring = first.Ring;
        first.Release();
        var second = PlanetfallArt.Load(def);
        Check("a second planetfall reuses the cached sprites and builds nothing (" + PlanetfallProc.Builds + " builds)",
              second.Ring == ring && second.Ring != null && second.Plume == PlanetfallProc.Plume && PlanetfallProc.Builds == 5);
        second.Release();
        Check("releasing a descent keeps the shared textures", PlanetfallProc.Ring != null && PlanetfallProc.Ring.texture != null);

        // a destroyed texture is rebuilt, not handed out dead
        Object.DestroyImmediate(PlanetfallProc.Vignette.texture);
        var third = PlanetfallArt.Load(def);
        Check("a destroyed shared texture is drawn again on the next load", third.Vignette != null && third.Vignette.texture != null && PlanetfallProc.Builds == 6);
        third.Release();

        // an unprepared Load finishes the job at once (the synchronous fallback)
        PlanetfallProc.Reset();
        PlanetfallProc.ResetCounters();
        var cold = PlanetfallArt.Load(def);
        Check("with nothing warmed, Load still delivers all five (synchronous fallback)",
              cold.White != null && cold.Ring != null && cold.Reticle != null && cold.Vignette != null && cold.Plume != null && PlanetfallProc.Builds == 5);
        cold.Release();
    }

    static void ProceduralWarmedByPreload()
    {
        PlanetfallProc.Reset();
        PlanetfallProc.ResetCounters();
        WorldArtPreload.Clear();
        var def = PlanetfallCatalog.For(0, 1, false);
        WorldArtPreload.Begin("pf-warm", () => WorldArtPreload.QueuePlanetfall(def));
        Check("queueing a planetfall's art draws its procedural textures with it (" + PlanetfallProc.Builds + " builds)", PlanetfallProc.Ready && PlanetfallProc.Builds == 5 && WorldArtPreload.Done);
        PlanetfallProc.ResetCounters();
        var art = PlanetfallArt.Load(def);
        Check("the descent that follows draws nothing", PlanetfallProc.Builds == 0 && art.Complete);
        art.Release();
        WorldArtPreload.Clear();
    }

    // ---- 2. scene lookups ----

    static void SceneLookupCaches(List<Object> made)
    {
        SceneLookup.CacheOutOfPlay = true;
        SceneLookup.ForgetAll();
        SceneLookup.ResetCounters();
        var a = new GameObject("~PerfLookup");
        made.Add(a);
        var r1 = SceneLookup.Active("~PerfLookup");
        var r2 = SceneLookup.Active("~PerfLookup");
        var r3 = SceneLookup.Active("~PerfLookup");
        Check("three lookups of one name cost one Find (" + SceneLookup.Finds + " finds, " + SceneLookup.Hits + " hits)",
              r1 == a && r2 == a && r3 == a && SceneLookup.Finds == 1 && SceneLookup.Hits == 2);
        a.SetActive(false);
        Check("an inactive object is not answered (as GameObject.Find does)", SceneLookup.Active("~PerfLookup") == null);
        a.SetActive(true);
        Check("switched back on, it is found again", SceneLookup.Active("~PerfLookup") == a);
        Object.DestroyImmediate(a);
        Check("a destroyed object is not answered", SceneLookup.Active("~PerfLookup") == null);
        var b = new GameObject("~PerfLookup");
        made.Add(b);
        Check("a replacement made later is found at once (misses are never remembered)", SceneLookup.Active("~PerfLookup") == b);
        int finds = SceneLookup.Finds;
        SceneLookup.Active("~PerfLookupNone"); SceneLookup.Active("~PerfLookupNone");
        Check("a name that does not exist is looked up every time", SceneLookup.Finds == finds + 2);
        SceneLookup.CacheOutOfPlay = false;
        SceneLookup.ForgetAll();
    }

    static void RailXAndTrailsDoNotFind(List<Object> made)
    {
        SceneLookup.CacheOutOfPlay = true;
        SceneLookup.ForgetAll();
        var left = new GameObject("leftPipe");
        left.transform.position = new Vector3(-3.15f, 0f, 0f);
        made.Add(left);
        float x0 = enmiesOnBoard.WorldRailX(true, 0);
        SceneLookup.ResetCounters();
        float x1 = 0f;
        for (int i = 0; i < 20; i++) x1 = enmiesOnBoard.WorldRailX(true, 0);
        Check("20 more rail-mine spawns do no GameObject.Find (" + SceneLookup.Finds + " finds)", SceneLookup.Finds == 0 && SceneLookup.Hits == 20);
        Check("the lane x is unchanged by the cache (" + x0 + " vs " + x1 + ")", Mathf.Approximately(x0, x1));

        var hud = new GameObject("PauseCounter");
        made.Add(hud);
        PowerTrailSprite.CounterWorldPosition(Vector3.zero);
        SceneLookup.ResetCounters();
        for (int i = 0; i < 20; i++) PowerTrailSprite.CounterWorldPosition(Vector3.zero);
        Check("20 more power-up trails do no GameObject.Find (" + SceneLookup.Finds + " finds)", SceneLookup.Finds == 0);
        SceneLookup.CacheOutOfPlay = false;
        SceneLookup.ForgetAll();
    }

    // ---- 3. the run start ----

    static void RunStartPreload()
    {
        PerfTier.Force(false);
        WorldArtPreload.SyncLoader = null;
        WorldArtPreload.Clear();
        int world = 1;
        var def = PlanetfallCatalog.For(world - 1, world, false);
        string name = WorldManager.Worlds[world].displayName;
        BackdropVariants.For(BackdropCatalog.For(name).world).Reset();
        PlanetfallProc.Reset();

        bool started = WorldArtPreload.QueueRunStart(world);
        Check("the menu starts a hold for the planet the next PLAY begins on", started && WorldArtPreload.Key == WorldArtPreload.RunStartKey(world) && WorldArtPreload.Pinned && WorldArtPreload.Done);
        Check("it holds the planetfall's seven textures and the backdrop", WorldArtPreload.HeldCount >= 11);

        WorldArtPreload.SceneLoaded("gameS1", true);
        Check("the hold survives the load of the game scene (once)", WorldArtPreload.Key != null && WorldArtPreload.HeldCount >= 11 && !WorldArtPreload.Pinned);
        WorldArtPreload.RunStarted(world, true);
        Check("the run starting on that same planet keeps it", WorldArtPreload.Key == WorldArtPreload.RunStartKey(world) && WorldArtPreload.HeldCount >= 11);

        WorldArtPreload.ResetCounters();
        PlanetfallProc.ResetCounters();
        var art = PlanetfallArt.Load(def);
        Check("the entry planetfall loads with zero synchronous loads (" + WorldArtPreload.SyncLoads + " sync, " + WorldArtPreload.Hits + " hits, " + PlanetfallProc.Builds + " drawn)",
              art.Complete && WorldArtPreload.SyncLoads == 0 && WorldArtPreload.Hits == 7 && PlanetfallProc.Builds == 0);
        var shader = WorldArtPreload.Load<Shader>(Planetfall.ShaderPath);
        Check("its shader too", shader != null && WorldArtPreload.SyncLoads == 0);
        art.Release();

        // the Space sky swapped in first does not drop the planet's hold; the planet's own swap does
        WorldArtPreload.Swapped("Space");
        Check("the Space sky's swap leaves the planet's hold alone", WorldArtPreload.Key != null && WorldArtPreload.HeldCount >= 11);
        var spec = BackdropCatalog.For(name);
        var root = new GameObject("~PerfRunStartRoot");
        WorldArtPreload.ResetCounters();
        var synced = new List<string>();
        WorldArtPreload.SyncLoader = (t, p) => { synced.Add(t.Name + ":" + p); return Resources.Load(p, t); };
        var set = new BackdropSet(spec.world, root.transform, 3f, 6f);
        try
        {
            Check("the swap under the clouds loads nothing synchronously (" + WorldArtPreload.SyncLoads + " sync, " + WorldArtPreload.Hits + " hits; " + string.Join(", ", synced) + ")",
                  set.Complete && WorldArtPreload.SyncLoads == 0 && WorldArtPreload.Hits >= set.Tiles.Count);
        }
        finally { set.Destroy(); Object.DestroyImmediate(root); WorldArtPreload.SyncLoader = null; }
        WorldArtPreload.Swapped(spec.world);
        Check("the planet's own swap lets go of the hold", WorldArtPreload.Key == null && WorldArtPreload.HeldCount == 0);

        // a hold for another world / another scene does not linger
        WorldArtPreload.QueueRunStart(world);
        WorldArtPreload.SceneLoaded("shopS6", true);
        Check("a hold that does not go straight into the game scene is dropped", WorldArtPreload.Key == null && WorldArtPreload.HeldCount == 0);
        WorldArtPreload.QueueRunStart(world);
        WorldArtPreload.SceneLoaded("gameS1", true);
        WorldArtPreload.RunStarted(world + 1, false);
        Check("a hold for a different start world is dropped when the run starts", WorldArtPreload.Key == null);
        WorldArtPreload.QueueRunStart(world);
        WorldArtPreload.SceneLoaded("gameS1", true);
        WorldArtPreload.SceneLoaded("startS4", true);
        Check("the menu's own load never keeps a stale hold", WorldArtPreload.Key == null || WorldArtPreload.Key == WorldArtPreload.RunStartKey(WorldManager.RunStartWorld(true)));

        // not for Space (portal arrival), not for a replay, not on a low-end phone
        WorldArtPreload.Clear();
        Check("Space's portal start has nothing to preload", !WorldArtPreload.QueueRunStart(0));
        PerfTier.Force(true);
        Check("a low-end phone keeps no menu-time hold (the old synchronous path)", !WorldArtPreload.QueueRunStart(world) && WorldArtPreload.Key == null);
        PerfTier.Force(false);

        WorldArtPreload.Clear();
    }

    // Every world's backdrop swap, after its preload, loads nothing synchronously (tiles, shaders, and every atlas the
    // directors ask for). A new atlas a director starts asking for fails here until WorldArtPreload.AtlasesOf lists it.
    static void EveryWorldSwapIsPreloaded()
    {
        foreach (var spec in BackdropCatalog.All)
        {
            WorldArtPreload.Clear();
            WorldArtPreload.SyncLoader = null;
            var vs = BackdropVariants.For(spec.world);
            vs.Reset();
            WorldArtPreload.BackdropFor(spec.world);
            var synced = new List<string>();
            WorldArtPreload.SyncLoader = (t, p) => { synced.Add(t.Name + ":" + p); return Resources.Load(p, t); };
            var root = new GameObject("~PerfSwapRoot");
            BackdropSet set = null;
            try
            {
                set = new BackdropSet(spec.world, root.transform, 3f, 6f);
                // the Space sky is chosen at the swap itself (SpaceSkySelection), so one sky tile may load
                synced.RemoveAll(s => spec.world == "Space" && s.Contains("/sky"));
                Check(spec.world + ": the swap loads nothing synchronously (" + synced.Count + ": " + string.Join(", ", synced) + ")", set.Complete && synced.Count == 0);
            }
            finally { if (set != null) set.Destroy(); Object.DestroyImmediate(root); WorldArtPreload.SyncLoader = null; WorldArtPreload.Clear(); vs.Reset(); }
        }
    }

    // ---- 4. variant probe ----

    static void VariantProbe()
    {
        // the manifest equals the Resources folders, both ways
        int bad = 0;
        foreach (var spec in BackdropCatalog.All)
        {
            if (spec.variantSets <= 0) continue;
            int mask = BackdropVariants.ShippedMask(spec.world);
            for (int v = 1; v <= BackdropVariants.MaxVariants; v++)
            {
                bool onDisk = Resources.Load<Sprite>(BackdropCatalog.TileFolder(spec.world, v) + "sky") != null;
                bool listed = (mask & (1 << (v - 1))) != 0;
                if (onDisk != listed) { bad++; Debug.Log("[PERF2] manifest/disk differ: " + spec.world + " v" + v + " disk " + onDisk + " listed " + listed); }
            }
        }
        foreach (var w in BackdropVariants.ShippedWorlds)
        {
            bool known = false;
            foreach (var spec in BackdropCatalog.All) if (spec.world == w && spec.variantSets > 0) known = true;
            if (!known) { bad++; Debug.Log("[PERF2] manifest names a world without variants: " + w); }
        }
        Check("BackdropVariants.Shipped matches the Resources folders (" + bad + " differences)", bad == 0);

        WorldArtPreload.SyncLoader = null;
        WorldArtPreload.Clear();
        WorldArtPreload.ResetCounters();
        BackdropVariants.ResetCounters();
        int picks = 0;
        foreach (var spec in BackdropCatalog.All)
        {
            if (spec.variantSets <= 0) continue;
            var vs = BackdropVariants.For(spec.world);
            vs.Reset();
            vs.Reserve(spec.variantSets);
            vs.Pick(spec.variantSets);
            vs.Pick(spec.variantSets);
            picks++;
        }
        Check("Reserve / Pick of every variant world load no sky texture (" + BackdropVariants.DiskProbes + " probes, " + WorldArtPreload.SyncLoads + " sync loads over " + picks + " worlds)",
              picks >= 5 && BackdropVariants.DiskProbes == 0 && WorldArtPreload.SyncLoads == 0);

        // an unlisted variant is still probed once and the answer remembered
        var frost = BackdropVariants.For("Frost");
        frost.Reset();
        BackdropVariants.ResetCounters();
        bool five = frost.Installed(5);
        Check("a variant beyond MaxVariants is simply absent, no probe", !five && BackdropVariants.DiskProbes == 0);
        var unknown = BackdropVariants.For("NoSuchWorld");
        unknown.Reset();
        unknown.Installed(1); unknown.Installed(1);
        Check("a world that is not in the manifest probes the disk once, then remembers (" + BackdropVariants.DiskProbes + ")", BackdropVariants.DiskProbes == 1);
        unknown.Reset();
    }

    // ---- 5. the one low-end decision ----

    static void LowEndDecision()
    {
        Check("under 3600 MB of RAM is low-end", PerfTier.IsLowEnd(3599, 4096));
        Check("under 1024 MB of graphics memory is low-end", PerfTier.IsLowEnd(8000, 1023));
        Check("3600 MB / 1024 MB is not", !PerfTier.IsLowEnd(3600, 1024) && !PerfTier.IsLowEnd(12000, 4096));
        Check("unknown memory (0) is never low-end", !PerfTier.IsLowEnd(0, 0));
        bool ok = true;
        foreach (int ram in new[] { 0, 1500, 2499, 2500, 3599, 3600, 8000 })
            foreach (int gpu in new[] { 0, 512, 1023, 1024, 4096 })
            {
                bool low = PerfTier.IsLowEnd(ram, gpu);
                ok &= (KillCamRecorder.Gap(ram, gpu) == KillCamRecorder.LowEndMinGap) == low;
                ok &= (CloudShadowLayer.TierFor(ram, gpu) != CloudShadowLayer.Tier.Full) == (low || (ram > 0 && ram < CloudShadowLayer.OffSystemMemoryMb));
            }
        Check("KillCamRecorder.Gap and CloudShadowLayer.TierFor still follow the shared decision exactly", ok);
        PerfTier.Force(true);
        Check("the decision can be forced for tests", PerfTier.LowEnd);
        PerfTier.Force(false);
        Check("... either way", !PerfTier.LowEnd);
        PerfTier.Force(null);
        Check("... and released", PerfTier.LowEnd == PerfTier.IsLowEnd(SystemInfo.systemMemorySize, SystemInfo.graphicsMemorySize));
    }

    // ---- 6. source audit ----

    static string Source(string relative) { return File.ReadAllText(Path.Combine(Application.dataPath, "Scripts", relative)); }

    static string Body(string src, string signature)
    {
        int i = src.IndexOf(signature, System.StringComparison.Ordinal);
        if (i < 0) return "";
        int open = src.IndexOf('{', i), depth = 0, j = open;
        for (; j < src.Length; j++)
        {
            if (src[j] == '{') depth++;
            else if (src[j] == '}' && --depth == 0) break;
        }
        return src.Substring(open, j - open);
    }

    static void SourceAudit()
    {
        Check("WorldRailX has no GameObject.Find / Camera.main", !Body(Source("Gameplay/Spawning/enmiesOnBoard.cs"), "public static float WorldRailX(bool left, int world)").Contains("GameObject.Find") &&
              !Body(Source("Gameplay/Spawning/enmiesOnBoard.cs"), "public static float WorldRailX(bool left, int world)").Contains("Camera.main"));
        Check("CounterWorldPosition has no GameObject.Find / Camera.main", !Body(Source("Gameplay/Weapons/PowerTrails.cs"), "public static Vector3 CounterWorldPosition").Contains("GameObject.Find") &&
              !Body(Source("Gameplay/Weapons/PowerTrails.cs"), "public static Vector3 CounterWorldPosition").Contains("Camera.main"));
        Check("AtomSpin.LateUpdate does not GetComponent every frame", !Body(Source("Gameplay/Pickups/AtomSpin.cs"), "void LateUpdate()").Contains("GetComponent<SpriteRenderer>()") ||
              Body(Source("Gameplay/Pickups/AtomSpin.cs"), "void LateUpdate()").Contains("sr == null"));

        // per-frame / per-spawn paths that used Camera.main now read the per-frame cache
        var hot = new Dictionary<string, string[]>
        {
            { "Gameplay/Spawning/SpawnAboveCamera.cs", new[] { "void Update()" } },
            { "Gameplay/Weapons/ShipAttackTarget.cs", new[] { "public static Rect View(" } },
            { "Gameplay/Weapons/TargetExplosion.cs", new[] { "public static void Kick(" } },
            { "Ship/DeathCrash.cs", new[] { "void LateUpdate()" } },
            { "Bosses/BossImpact.cs", new[] { "void LateUpdate()" } },
            { "Bosses/BossEntrance.cs", new[] { "public static Origin Pick()", "static float HalfWidth()", "public static float ReachFrom(" } },
            { "Worlds/Hazards/WorldHazards.cs", new[] { "var cam = CameraFit.Main;" } },
            { "Worlds/Hazards/WindVisuals.cs", new[] { "public void Draw(" } },
            { "Worlds/Hazards/WeatherLayer.cs", new[] { "public void Step(" } },
            { "Worlds/Planetfall/Liftoff.cs", new[] { "void LateUpdate()" } },
            { "Worlds/Planetfall/Planetfall.cs", new[] { "void LateUpdate()" } },
        };
        int stale = 0;
        foreach (var kv in hot)
        {
            string src = Source(kv.Key);
            foreach (var sig in kv.Value)
            {
                if (sig.StartsWith("var ")) { if (!src.Contains(sig)) { stale++; Debug.Log("[PERF2] no cached camera in " + kv.Key); } continue; }
                string body = Body(src, sig);
                if (body.Length == 0 || body.Contains("Camera.main")) { stale++; Debug.Log("[PERF2] Camera.main in " + kv.Key + " " + sig); }
            }
        }
        Check("the per-frame / per-spawn paths read CameraFit.Main, not Camera.main (" + stale + " left)", stale == 0);
    }
}
