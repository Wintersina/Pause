using System;
using System.Collections.Generic;
using UnityEngine;
using Debug = UnityEngine.Debug;
using Object = UnityEngine.Object;

// NOTHING GROWS OVER A LEVEL.
//
// Build 10026: frame rate fell steadily through a level and the app was killed for memory near the boss (2.6 GB on the
// owner's phone; the emulator grew 8.5 MB a second). WorldHazards' drawn layer could not be built in a player build (a
// stripped engine class, see EngineModuleGuardTest), and its lazy getter then built -- and leaked -- a whole pool of
// 162 objects on EVERY frame. No test flew long enough, or counted objects, to notice.
//
//   1  the 10026 failure itself: with no SpriteMask class the drawn layer is still built exactly once, draws the flare
//      without a mask, and nothing grows over hundreds of frames
//   2  a layer that cannot draw is given up for the run (one error), never rebuilt or retried per frame
//   3  the soak: minutes of the real spawners, the elite director, the weather (scheduler, fields, all three drawn layers)
//      and every pooled player-attack line (Volt arcs, FxLinks, ship link lines, attack links) at full fire rate, with
//      live GameObjects, SpriteRenderers, LineRenderers, SpriteMasks, materials, meshes and textures counted every ten
//      simulated seconds: the last minute may not be above the minute before it by more than the board's own noise
//   4  the per-frame draw path of the weather and the attack lines allocates no managed memory in steady state
//
//   Unity -batchmode -quit -projectPath Pause -executeMethod PerfSoakTest.Run
public static class PerfSoakTest
{
    static int fails;
    static void Check(string what, bool ok, string extra = "")
    {
        Debug.Log((ok ? "[SOAK] PASS  " : "[SOAK] FAIL  ") + what + (extra.Length > 0 ? "  [" + extra + "]" : ""));
        if (!ok) fails++;
    }
    static void Info(string what) { Debug.Log("[SOAK] INFO  " + what); }

    public static void Run() { TestHarness.Exit(Execute()); }

    const float Dt = 1f / 30f;
    static readonly List<Object> made = new List<Object>();

    static HazardDef Def(string id)
    {
        foreach (var e in WorldWeatherTable.Entries) foreach (var h in e.hazards) if (h.id == id) return h;
        return null;
    }

    static WorldHazards NewManager()
    {
        if (WorldHazards.Instance != null) Object.DestroyImmediate(WorldHazards.Instance.gameObject);
        var go = new GameObject("~PerfSoakHazards");
        made.Add(go);
        return go.AddComponent<WorldHazards>();
    }

    static void DestroyManager(WorldHazards m)
    {
        if (m == null) return;
        m.SendMessage("OnDestroy", SendMessageOptions.DontRequireReceiver);   // (edit mode: Unity does not send it)
        Object.DestroyImmediate(m.gameObject);
    }

    static int Count<T>() where T : Object { return Object.FindObjectsByType<T>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length; }
    static int All<T>() where T : Object { return Resources.FindObjectsOfTypeAll<T>().Length; }

    static void LiveFlare(int holes)
    {
        var f = WorldHazards.Fields[0];
        f.def = Def("flare_storm"); f.phase = HazardField.Phase.Live; f.side = 1f; f.sweepX = 0f; f.t = 5f;
        f.viewBottom = -6f; f.viewTop = 6f; f.laneHalf = 4f; f.liveFor = 600f;
        f.holes = holes; f.holeY[0] = 1f; f.holeY[1] = -1.5f;
    }

    public static int Execute()
    {
        fails = 0;
        using var sandbox = new TestHarness.Sandbox();
        var factory = HazardVisuals.MaskFactory;
        try
        {
            EliteCatalog.Reload();
            StrippedMaskNeverLeaks();
            BrokenLayerIsGivenUp();
            HazardVisuals.MaskFactory = factory;
            Soak();
            SteadyStateAllocatesNothing();
        }
        finally
        {
            HazardVisuals.MaskFactory = factory;
            foreach (var o in made) if (o != null) Object.DestroyImmediate(o);
            made.Clear();
            StopFx();
            WorldLeakTest.TearDown();
            WorldHazards.SituationOverride = null;
            WorldHazards.BossRunningOverride = null;
            WorldHazards.ClearAll();
            WorldHazards.PlayerOffset = Vector2.zero;
            WeatherWind.Reset(null);
            EliteSystem.PlayerOverride = null;
            LandingSites.Override = null;
            SpawnSpace.ClockOverride = null;
            WorldManager.ClearReplayWorld();
            RunLoop.Reset();
            BossEncounter.ResetRun();
            PortalPressure.Reset();
            LoopDifficulty.Reset();
            ShipStartSpeed.EquippedHudOverride = null;
            PlanetfallCatalog.Enabled = true;
            buttonClicks.playerDied = false;
            moveBackGround.speed = 0f;
        }
        Debug.Log("[SOAK] " + (fails == 0 ? "ALL PASS" : fails + " FAILED"));
        return fails;
    }

    // ---- 1: the 10026 build, simulated: AddComponent<SpriteMask>() gives null ----
    static void StrippedMaskNeverLeaks()
    {
        HazardVisuals.MaskFactory = go => null;
        var m = NewManager(); WorldHazards.ClearAll();
        LiveFlare(2);
        int built0 = HazardVisuals.Built;
        bool threw = false;
        try { for (int i = 0; i < 10; i++) m.DrawLayers(Dt); } catch (Exception) { threw = true; }
        int objects10 = Count<Transform>(), renderers10 = Count<SpriteRenderer>();
        try { for (int i = 0; i < 600; i++) m.DrawLayers(Dt); } catch (Exception) { threw = true; }
        int objects = Count<Transform>(), renderers = Count<SpriteRenderer>();
        Check("with no SpriteMask class the drawn layer does not throw", !threw && !m.DrawingBroken);
        Check("... and is built exactly once in 610 frames (10026 built one per frame)", HazardVisuals.Built - built0 == 1, "built " + (HazardVisuals.Built - built0));
        Check("... and no object is added after the first frames (10026 added 162 a frame)", objects == objects10 && renderers == renderers10,
              "objects " + objects10 + " -> " + objects + ", sprite renderers " + renderers10 + " -> " + renderers);
        Check("... and the flare draws without masks", !m.Visuals.MasksAvailable && m.Visuals.HoleMasksUsed == 0 && Count<SpriteMask>() == 0);
        if (HazardArt.Loaded)
        {
            // a tile over an opening is left out rather than drawn across it
            bool covered = false; int shown = 0;
            var f = WorldHazards.Fields[0];
            float w = f.def.size * HazardVisuals.FlareTile;
            foreach (var r in m.Visuals.ArtRenderers)
            {
                if (r == null || !r.enabled) continue;
                shown++;
                for (int b = 0; b < f.holes; b++)
                    if (Mathf.Abs(r.transform.position.y - f.holeY[b]) < (w + f.def.holeSize) * .5f - 1e-3f &&
                        Mathf.Abs(r.transform.position.x - BandX(f, b, w)) < 1e-3f) covered = true;
            }
            Check("... leaving the openings open (no painted tile across one; " + shown + " tiles drawn)", !covered && shown > 0);
        }
        DestroyManager(m);
        Check("the layer is destroyed with its manager", Count<SpriteRenderer>() < renderers);
        WorldHazards.ClearAll();
    }

    static float BandX(HazardField f, int band, float w)
    {
        float lead = f.sweepX - f.side * band * f.def.bandGap + f.side * f.def.size * .5f;
        return lead - f.side * (HazardVisuals.FlareLead - .5f) * w;
    }

    // ---- 2: a layer that throws is switched off, not retried ----
    static void BrokenLayerIsGivenUp()
    {
        HazardVisuals.MaskFactory = go => go.AddComponent<SpriteMask>();
        var m = NewManager(); WorldHazards.ClearAll();
        LiveFlare(1);
        m.DrawLayers(Dt);
        Check("the drawn layer works with the real SpriteMask", !m.DrawingBroken && m.Visuals.MasksAvailable);
        int built0 = HazardVisuals.Built;
        m.Visuals.Destroy();   // its renderers are gone: every Draw throws from here on
        bool threw = false;
        try { for (int i = 0; i < 200; i++) m.DrawLayers(Dt); } catch (Exception) { threw = true; }
        int objects = Count<Transform>();
        try { for (int i = 0; i < 200; i++) m.DrawLayers(Dt); } catch (Exception) { threw = true; }
        Check("a layer that cannot draw is switched off for the run: nothing thrown out of the frame, nothing rebuilt, nothing added",
              !threw && m.DrawingBroken && HazardVisuals.Built == built0 && Count<Transform>() == objects,
              "threw " + threw + ", broken " + m.DrawingBroken + ", rebuilt " + (HazardVisuals.Built - built0));
        DestroyManager(m);
        WorldHazards.ClearAll();
    }

    // ---- 3: the soak ----

    static ShipLinkLines shipLinks;
    static readonly AttackLink[] attackLinks = new AttackLink[3];
    static VoltArc held; static int heldGen;
    static int fxFrame;

    // Every pooled player-attack line, fired far faster than any ship does: a pool that grows shows at once.
    static void FireFx(float dt, Vector3 ship)
    {
        fxFrame++;
        var target = new Vector3(Mathf.Sin(fxFrame * .21f) * 2.5f, 3f + Mathf.Cos(fxFrame * .13f), 0f);
        // Volt Viper: a chain of five every 4 frames plus a held link
        if (fxFrame % 4 == 0) for (int i = 0; i < 5; i++) VoltArcs.Begin(ship + Vector3.right * i * .3f, target + Vector3.up * i * .2f);
        held = VoltArcs.Hold(held, heldGen, ship, target); heldGen = VoltArcs.GenerationOf(held);
        VoltArcs.Tick(dt);
        // Lightning Sky javelins, Paragon missile trails and impact sparks
        if (fxFrame % 3 == 0)
        {
            FxLinks.JavelinTelegraph(8, target.x, 6f, target.y);
            FxLinks.MissileTrail(16, ship, target, FxLinks.TrailBow);
            FxLinks.ImpactSpark(16, target);
        }
        var pool = FxLinks.Pool;
        for (int i = 0; i < pool.Count; i++) if (pool[i] != null && pool[i].Active) pool[i].Advance(dt);
        // Paranoid / Saboteur / Jade Phantom
        if (shipLinks == null) { var host = new GameObject("~PerfSoakLinks"); made.Add(host); shipLinks = new ShipLinkLines(host.transform); }
        for (int i = 0; i < 30; i++)   // (more than the pool holds: the cap, not growth)
            shipLinks.Link(ShipLinkLines.Kind.Tether, ship, target + Vector3.right * i * .1f, .2f, .2f, ShipLinkLines.TetherWidth, ShipLinkLines.TetherColor, .05f, i);
        if (fxFrame % 2 == 0) { shipLinks.Burn(ship, target, .3f); shipLinks.Snap(ship, target, .2f); shipLinks.Crack(ship, target, .25f); }
        shipLinks.EndFrame(dt);
        // Gold Warden aim lines / UFO tractor lines: made and destroyed by their owners (a sentry, a beam)
        for (int i = 0; i < attackLinks.Length; i++)
        {
            if (attackLinks[i] == null || fxFrame % (40 + i * 7) == 0)
            {
                if (attackLinks[i] != null) attackLinks[i].Destroy();
                attackLinks[i] = new AttackLink(Color.yellow, 60);
            }
            attackLinks[i].Set(ship, target + Vector3.left * i, .03f, .6f);
        }
    }

    static void StopFx()
    {
        VoltArcs.StopAll();
        FxLinks.StopAll();
        if (shipLinks != null) { shipLinks.Dispose(); shipLinks = null; }
        for (int i = 0; i < attackLinks.Length; i++) { if (attackLinks[i] != null) attackLinks[i].Destroy(); attackLinks[i] = null; }
        held = null;
        foreach (var name in new[] { "~VoltArcs", "~FxLinks" })
        {
            var go = GameObject.Find(name);
            if (go != null) Object.DestroyImmediate(go);
        }
    }

    struct Counts
    {
        public int objects, sprites, lines, masks, materials, meshes, textures;
        public static Counts Now()
        {
            return new Counts
            {
                objects = Count<Transform>(), sprites = Count<SpriteRenderer>(), lines = Count<LineRenderer>(), masks = Count<SpriteMask>(),
                materials = All<Material>(), meshes = All<Mesh>(), textures = All<Texture2D>(),
            };
        }
        public void Max(Counts o)
        {
            objects = Mathf.Max(objects, o.objects); sprites = Mathf.Max(sprites, o.sprites); lines = Mathf.Max(lines, o.lines); masks = Mathf.Max(masks, o.masks);
            materials = Mathf.Max(materials, o.materials); meshes = Mathf.Max(meshes, o.meshes); textures = Mathf.Max(textures, o.textures);
        }
        public override string ToString()
        {
            return "objects " + objects + ", sprites " + sprites + ", lines " + lines + ", masks " + masks + ", materials " + materials + ", meshes " + meshes + ", textures " + textures;
        }
    }

    // How far the last minute's peak may sit above the minute before it. The board itself breathes by a few hundred
    // objects as waves and elites come and go; the 10026 leak added 162 objects a FRAME (290 000 a minute at 30 fps).
    public const int ObjectSlack = 900, MaterialSlack = 12, MeshSlack = 12, TextureSlack = 160;
    public const float SoakSeconds = 240f;

    static void Soak()
    {
        int worlds = WorldManager.LiveWorldCount;
        for (int w = 0; w < worlds; w++)
        {
            if (!TestHarness.SweepWorld(w)) continue;   // gate lane: every third world; the full lane flies them all
            WorldLeakTest.Prefs(w);
            UnityEngine.Random.InitState(4400 + w);
            WorldLeakTest.LoadRun(true);
            var m = NewManager(); WorldHazards.ClearAll();
            int world = w;
            // the weather as a flight has it: nothing blocks it, so events come as often as the table allows
            WorldHazards.SituationOverride = () => new WorldHazards.Situation { world = world };
            WorldHazards.BossRunningOverride = false;
            int built0 = HazardVisuals.Built;
            fxFrame = 0;

            var mid = new Counts(); var late = new Counts();
            float t = 0f, next = 60f;
            int frames = 0, flushed = 0;
            bool threw = false;
            string error = "";
            while (t < SoakSeconds)
            {
                try
                {
                    WorldLeakTest.Step(Dt);
                    m.Step(Dt);
                    m.DrawLayers(Dt);
                    FireFx(Dt, WorldLeakTest.pilot.position);
                }
                catch (Exception e) { if (!threw) error = e.GetType().Name + ": " + e.Message; threw = true; }
                t += Dt; frames++;
                if (++flushed >= TestHarness.FlushEvery) { flushed = 0; TestHarness.FlushGpu(); }
                if (t < next) continue;
                next += 10f;
                var now = Counts.Now();
                if (t <= 120f + Dt) mid.Max(now); else if (t > 180f) late.Max(now);
            }
            string name = WorldLeakTest.W(w);
            Info(name + ": minute 2 peak: " + mid);
            Info(name + ": minute 4 peak: " + late);
            Check(name + ": " + frames + " frames of spawners + weather + attack lines run without an exception", !threw, error);
            Check(name + ": the weather's drawn layer was built once", HazardVisuals.Built - built0 == 1 && !m.DrawingBroken, "built " + (HazardVisuals.Built - built0));
            Check(name + ": live objects do not grow over the level", late.objects <= mid.objects + ObjectSlack, mid.objects + " -> " + late.objects);
            Check(name + ": sprite renderers do not grow over the level", late.sprites <= mid.sprites + ObjectSlack, mid.sprites + " -> " + late.sprites);
            int lineCap = VoltArcs.MaxArcs * 4 + FxLinks.MaxLines + ShipLinkLines.MaxLines;
            Check(name + ": line renderers stay within their pools (" + lineCap + ")", late.lines <= lineCap && late.lines <= mid.lines, mid.lines + " -> " + late.lines);
            Check(name + ": sprite masks stay at the flare's four", late.masks <= HazardField.MaxBands && late.masks <= mid.masks, mid.masks + " -> " + late.masks);
            Check(name + ": materials do not grow over the level", late.materials <= mid.materials + MaterialSlack, mid.materials + " -> " + late.materials);
            Check(name + ": meshes do not grow over the level", late.meshes <= mid.meshes + MeshSlack, mid.meshes + " -> " + late.meshes);
            Check(name + ": textures do not grow over the level beyond art seen for the first time", late.textures <= mid.textures + TextureSlack, mid.textures + " -> " + late.textures);
            Check(name + ": Volt arcs, FxLinks and ship link lines are at (not above) their caps",
                  VoltArcs.PoolSize <= VoltArcs.MaxArcs && FxLinks.Pool.Count <= FxLinks.MaxLines, "volt " + VoltArcs.PoolSize + ", fx " + FxLinks.Pool.Count);

            StopFx();
            DestroyManager(m);
            WorldHazards.SituationOverride = null;
            WorldHazards.ClearAll();
            WorldLeakTest.TearDown();
            TestHarness.FlushGpu();
        }
    }

    // ---- 4: the per-frame draw path allocates nothing ----
    static void SteadyStateAllocatesNothing()
    {
        long control;
        if (!TestHarness.AllocMeterWorks(out control)) { Check("the allocation meter sees allocations", false, "control " + control); return; }
        var m = NewManager(); WorldHazards.ClearAll();
        LiveFlare(2);
        var ship = new Vector3(0f, -3f, 0f);
        fxFrame = 0;
        for (int i = 0; i < 240; i++) { m.StepFields(1f / 60f); m.DrawLayers(1f / 60f); FireFx(1f / 60f, ship); }   // warm: pools full, art loaded
        WorldHazards.Fields[0].liveFor = 600f;
        long weather = TestHarness.AllocatedBytes(() => { for (int i = 0; i < 120; i++) m.DrawLayers(1f / 60f); });
        Check("120 frames of the weather's three drawn layers (a live flare with openings) allocate nothing", weather == 0, weather + " B");
        long arcs = TestHarness.AllocatedBytes(() =>
        {
            for (int i = 0; i < 120; i++)
            {
                if (i % 4 == 0) for (int k = 0; k < 5; k++) VoltArcs.Begin(ship, ship + Vector3.up * (2f + k));
                VoltArcs.Tick(1f / 60f);
            }
        });
        Check("120 frames of Volt arcs (begin, re-jag, fade) allocate nothing", arcs == 0, arcs + " B");
        long links = TestHarness.AllocatedBytes(() =>
        {
            for (int i = 0; i < 120; i++)
            {
                for (int k = 0; k < 12; k++) shipLinks.Link(ShipLinkLines.Kind.Wire, ship, ship + Vector3.up * (1f + k * .2f), .1f, .1f, ShipLinkLines.WireWidth, ShipLinkLines.WireColor, .04f, k);
                shipLinks.EndFrame(1f / 60f);
                var pool = FxLinks.Pool;
                for (int k = 0; k < pool.Count; k++) if (pool[k] != null && pool[k].Active) pool[k].Advance(1f / 60f);
            }
        });
        Check("120 frames of ship link lines and FxLinks allocate nothing", links == 0, links + " B");
        StopFx();
        DestroyManager(m);
        WorldHazards.ClearAll();
    }
}
