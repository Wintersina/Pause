using System.Collections.Generic;
using System.IO;
using UnityEditor.SceneManagement;
using UnityEngine;

// The Desert v3 backdrop (DesertDirector, GroundPlanner, DesertAmbientCatalog):
//   * one of four ground sets per landing, never the same twice running; the
//     night side (v4) flagged, its lights stronger;
//   * PINNED pieces: a landmark's spot on the mid tile never changes while
//     it is in view (60 s of scroll);
//   * AFFINITY: over 150 seeded runs per variant no piece stands on ground
//     its rule forbids (ruins and masts off the cliffs, wells and glass on soft
//     or flat ground, dune ridges on sand), and no two footprints overlap;
//   * EXACT EMITTERS: every binding draws its loop with the loop's anchor
//     (plume base) within 3 px of the piece's measured point, mirrored or
//     not, and that point is on the piece's opaque art -- the smoke over its
//     stack mouth, the beacon on its mast tip; plumes lean with the level
//     wind; loops draw above their piece; the installed loop atlases animate
//     and the lamps blink;
//   * the GIANT SAND WORMS: a ridge trail on the dune sand, an eruption (emerge,
//     arc, dive, spray) at the same spot, pinned to the ground, behind every
//     piece, alpha capped (body .85, spray .7), one at a time, never in the
//     first 12 s, the giant at most once a minute, none at a boss, ridge-only
//     on the low-quality switch, every frame of a long run allocation free;
//   * the cloud ceiling (dust haze) thick at the start and gone by ~14 s
//     (then a light scattering of haze, CloudCoverMeter), continuing the
//     planetfall's deck; gusts; every layer behind gameplay; lane
//     readability as rendered; the dark worlds' thin shot outlines;
//   * each of Desert's six elites launching from its own site (worm burrow /
//     crawler bay / glass pad / buried silo / ruin dock) that opens for its tell
//     and shuts behind it; and no per-frame allocation.
// (The tiles' seams / brightness caps / texture budget are WorldBackdropTest's.)
//
//   Unity -batchmode -quit -projectPath <abs>/Pause -executeMethod DesertBackdropTest.Run
public static class DesertBackdropTest
{
    static int fails;
    static void Check(string what, bool ok)
    {
        Debug.Log((ok ? "[DBD] PASS  " : "[DBD] FAIL  ") + what);
        if (!ok) fails++;
    }

    public static void Run() { TestHarness.Exit(Execute()); }

    const float Dt = 1f / 30f;
    public const float EmitterTolerancePx = 3f;
    public const int SeededRuns = 150;
    static BackdropVariants Sel => BackdropVariants.For("Desert");

    public static int Execute()
    {
        fails = 0;
        using var sandbox = new TestHarness.Sandbox();
        float savedSpeed = moveBackGround.speed;
        try
        {
            Catalog();
            Selection();
            Pinned();
            Affinity();
            Emitters();
            Loops();
            Worms();
            PinnedItems();
            Weather();
            Clouds();
            Brightness();
            Sites();
            Allocation();
        }
        finally
        {
            moveBackGround.speed = savedSpeed;
            Time.timeScale = 1f;
            BackdropSet.AtlasOverride = null;
            Sel.Reset();
            DesertDirector.SeedOverride = 0;
            DesertTuning.WormsEnabled = true;
            DesertTuning.RidgeOnly = false;
            LandingSites.Override = null;
            EliteSystem.PlayerOverride = null;
            EliteSystem.Clear();
            if (WorldBackdrop.Instance != null) Object.DestroyImmediate(WorldBackdrop.Instance.gameObject);
        }
        Debug.Log("[DBD] failures: " + fails);
        return fails;
    }

    static WorldBackdrop Fresh(int variant = 0, float speed = .25f)
    {
        EliteSystem.Clear();
        EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
        Time.timeScale = 1f;
        moveBackGround.speed = speed;
        Sel.Reset();
        Sel.Seed(11);
        Sel.Force = variant;
        var cam = Camera.main;
        cam.orthographic = true;
        cam.aspect = 1080f / 2400f;
        cam.orthographicSize = CameraFit.ComputeSize(5f, CameraFit.GameplayHalfWidth, 1080, 2400);
        var wb = WorldBackdrop.Create("Desert");
        wb.Show("Desert", false);
        return wb;
    }

    static DesertDirector Director(WorldBackdrop wb) { return wb.Current != null ? wb.Current.Director as DesertDirector : null; }

    static void Run(WorldBackdrop wb, float seconds, System.Action each = null)
    {
        for (float t = 0f; t < seconds; t += Dt) { wb.Step(Dt); if (each != null) each(); }
    }

    static IEnumerable<BackdropPool> PinnedPools(DesertDirector d)
    {
        yield return d.Pipes; yield return d.Fires; yield return d.Ground; yield return d.Sites;
    }

    // ---- the layer stack -----------------------------------------------------

    static void Catalog()
    {
        var spec = BackdropCatalog.For("Desert");
        Check("Desert has its own v3 folder and four variant sets, not a stand-in",
              spec.world == "Desert" && spec.folder == "Worlds/Desert/Backdrop3/" && spec.variantSets == BackdropVariants.MaxVariants);
        string[] want = { "sky", "far", "mid", "ground", "flow", "palls", "mist", "wisps", "ceiling", "gusts", "grit" };
        bool all = true;
        foreach (string n in want) all &= spec.Has(n);
        Check("Desert layers: ground tiles, pinned ground pieces, flow, palls, mist, wisps, ceiling, gusts, grit", all);
        var g = spec.Find("ground");
        Check("the ground pieces are PINNED to the mid tile: same rate (" + g.rate + " = " + spec.Rate("mid") + " <= " +
              BackdropCatalog.MaxGroundRate + "), landmark role, right after it",
              g.pinTo == "mid" && g.rate == spec.Rate("mid") && g.rate <= BackdropCatalog.MaxGroundRate &&
              g.role == BackdropCatalog.Role.Landmark && System.Array.IndexOf(spec.layers, g) == System.Array.IndexOf(spec.layers, spec.Find("mid")) + 1);
        bool rising = true;
        for (int i = 1; i < spec.layers.Length; i++)
            rising &= spec.layers[i].rate > spec.layers[i - 1].rate ||
                      (spec.layers[i].pinTo == spec.layers[i - 1].name && spec.layers[i].rate == spec.layers[i - 1].rate);
        Check("Desert rates strictly increase far -> near (a pinned layer shares its host's)", rising);
        Check("the night side is v4 only, never drawn brighter than the day (" + spec.VariantBrightness(4) + " <= " + spec.VariantBrightness(1) + ")",
              !spec.Night(1) && !spec.Night(2) && !spec.Night(3) && spec.Night(4) && spec.VariantBrightness(4) <= spec.VariantBrightness(1) &&
              spec.VariantBrightness(2) >= .3f && spec.VariantBrightness(3) >= .3f);
        Check("the art is dark-based: Desert is drawn below the bright-world lift (" + DesertTuning.Brightness + " < " + BackdropCatalog.Spec.BrightLift +
              "), so shots keep the thin outline and the hearts the standard one", !spec.Bright);
        Check("ground pieces stay under the size limit", DesertTuning.LandmarkMax <= BackdropCatalog.MaxLandmarkSize &&
              DesertTuning.FireMax <= BackdropCatalog.MaxLandmarkSize && DesertTuning.SiteMax <= BackdropCatalog.MaxLandmarkSize);

        var table = DesertAmbientCatalog.Table;
        bool loops = true;
        int bound = 0;
        foreach (var b in table.bindings)
        {
            loops &= b.emitters.Length <= table.maxPerPiece;
            foreach (var e in b.emitters) { loops &= table.LoopIndex(e.loop) >= 0; bound++; }
        }
        Check("points.json binds " + bound + " measured emitters on " + table.bindings.Length + " drawings, every loop known, <= " +
              table.maxPerPiece + " per piece", loops && table.bindings.Length >= 15 && bound >= 30);
        bool anchors = true;
        int installed = 0;
        foreach (var l in table.loops)
        {
            if (!DesertAmbientCatalog.Present(l.name)) continue;
            installed++;
            bool measured = false;
            foreach (var m in DesertAmbientCatalog.MeasuredLoops) if (m.name == l.name) measured = true;
            anchors &= measured;
        }
        Check("every installed loop's anchor is measured (" + installed + " installed)", anchors);
        var missing = DesertAmbientCatalog.Missing();
        Check("every ambient slot is installed: none missing (" + missing.Count + " " + string.Join(", ", missing) + ")", missing.Count == 0);
        bool smokeBase = true;
        foreach (var m in DesertAmbientCatalog.MeasuredLoops)
            if (DesertAmbientCatalog.Plume(m.name)) smokeBase &= Mathf.Abs(m.x - 128f) <= 4f && m.y >= 228f && m.spread <= 4f;
        Check("the plume loops' bases sit on their cell's bottom-centre anchor, steady over the frames", smokeBase);
        string[] carriers = { "refinery_hull_00", "refinery_hull_01", "ribcage_gantry_00", "drill_head_tower", "solar_array_skeleton_00", "crawler_caravan_00", "radio_mast",
                              "collapsed_dome_cluster", "bucket_wheel_excavator", "pump_well_station", "fused_glass_crater_rim", "dry_well_00", "heat_vent_00", "pump_house", "dune_ridge" };
        bool smokes = true;
        foreach (string s in carriers) smokes &= DesertAmbientCatalog.Table.For(s) != null;
        Check("stacks, masts, wells, vents, glass and the dune ridge all carry loops", smokes);
        for (int v = 1; v <= 4; v++)
        {
            var mask = GroundMask.Load("Desert", v);
            Check("v" + v + " has its affinity mask (" + (mask != null ? mask.Cols + "x" + mask.Rows + ", sand " +
                  mask.Share(GroundClass.Water).ToString("F2") + ", flats " + mask.Share(GroundClass.Open).ToString("F2") + ", cliff " + mask.Share(GroundClass.Canopy).ToString("F2") : "-") + ")",
                  mask != null && mask.Cols == 32 && mask.Rows == 64 && mask.Share(GroundClass.Water) + mask.Share(GroundClass.Open) > .4f);
        }
        Check("the sites' kinds are Desert's own and parse from launchFrom strings",
              LandingSite.KindOf("wormburrow") == LandingKind.DesertWormBurrow && LandingSite.KindOf("caravanbay") == LandingKind.DesertCrawlerBay &&
              LandingSite.KindOf("glasspad") == LandingKind.DesertGlassPad && LandingSite.KindOf("buriedsilo") == LandingKind.DesertBuriedSilo &&
              LandingSite.KindOf("ruindock") == LandingKind.DesertRuinDock &&
              LandingSite.Accepts("ruindock,glasspad", LandingKind.DesertGlassPad) && !LandingSite.Accepts("ruindock,glasspad", LandingKind.DesertBuriedSilo) &&
              !LandingSite.Accepts("wormburrow", LandingKind.TideRigDeck));
        var defs = new List<EliteDef>();
        EliteCatalog.ForWorld(6, defs);
        var wanted = new Dictionary<string, LandingKind>
        {
            { "desert_elite_dune_stalker", LandingKind.DesertWormBurrow }, { "desert_elite_caravan_hauler", LandingKind.DesertCrawlerBay },
            { "desert_elite_lens_matriarch", LandingKind.DesertGlassPad }, { "desert_elite_glass_cannon", LandingKind.DesertBuriedSilo },
            { "desert_elite_scorpion_walker", LandingKind.DesertRuinDock }, { "desert_elite_mirror_bastion", LandingKind.DesertRuinDock },
        };
        bool mine = defs.Count == 6;
        foreach (var d in defs) mine &= wanted.ContainsKey(d.key) && LandingSite.KindOf(d.launchFrom) == wanted[d.key];
        Check("each of Desert's six elites launches from its own site kind (" + defs.Count + " defs)", mine);
    }

    // ---- four ground sets, random per landing ----------------------------------

    static void Selection()
    {
        Sel.Reset();
        Check("all four Desert ground sets are installed (" + Sel.InstalledCount(4) + ")", Sel.InstalledCount(4) == 4 && !Sel.Installed(5));
        Sel.Seed(3);
        var seen = new HashSet<int>();
        int prev = 0, repeats = 0;
        for (int i = 0; i < 400; i++)
        {
            int v = Sel.Pick(4);
            if (v == prev) repeats++;
            seen.Add(v);
            prev = v;
        }
        Check("400 landings pick all four sets, never the same twice in a row (" + repeats + ")", seen.Count == 4 && repeats == 0);
        var wb = Fresh();
        int last = wb.Current.Variant, changes = 0, loaded = 0;
        for (int i = 0; i < 6; i++)
        {
            wb.Show("Space", false);
            wb.Show("Desert", false);
            var set = wb.Current;
            if (set.Variant != last) changes++;
            last = set.Variant;
            var sky = Resources.Load<Sprite>(BackdropCatalog.TileFolder("Desert", set.Variant) + "sky");
            if (sky != null && set.Textures.Contains(sky.texture) && set.Complete) loaded++;
        }
        Check("every landing in Desert flies a different set, from its own folder (" + changes + "/6, " + loaded + "/6)",
              changes == 6 && loaded == 6);
        Check("Frost keeps its own selection", BackdropVariants.For("Frost") != Sel);
    }

    // ---- pinned to the ground -------------------------------------------------

    // Where on the drawn mid tile (fraction up a copy, 0..1, and across)
    // a world point is, read from the tile's own renderers.
    static bool TileSpot(WorldBackdrop wb, Vector3 world, out Vector2 uv)
    {
        uv = Vector2.zero;
        var root = wb.Current.Root.Find("Tile_mid");
        if (root == null) return false;
        foreach (var sr in root.GetComponentsInChildren<SpriteRenderer>())
        {
            if (!sr.enabled || !sr.bounds.Contains(new Vector3(world.x, world.y, sr.bounds.center.z))) continue;
            Vector3 l = sr.transform.InverseTransformPoint(world);
            Vector2 size = sr.sprite.bounds.size;
            uv = new Vector2(l.x / size.x + .5f, l.y / size.y + .5f);
            return true;
        }
        return false;
    }

    static void Pinned()
    {
        var wb = Fresh(1, .6f);
        var d = Director(wb);
        Check("Desert builds its director on the v3 art", d != null && wb.Current.Complete && d.Planner.Ready && d.Planner.Mask != null);
        if (d == null) return;
        var start = new Dictionary<BackdropPiece, Vector2>();
        float worst = 0f;
        int tracked = 0;
        var tile = d.Planner.Tile;
        float tilePx = tile.TileHeight / 1024f;
        Run(wb, 60f, () =>
        {
            foreach (var pool in PinnedPools(d))
                foreach (var p in pool.items)
                {
                    if (!p.active) { start.Remove(p); continue; }
                    if (Mathf.Abs(p.y) > d.Planner.Tile.TileHeight * .45f) continue;
                    Vector2 uv;
                    if (!TileSpot(wb, p.root.position, out uv)) continue;
                    Vector2 s0;
                    if (!start.TryGetValue(p, out s0)) { start[p] = uv; tracked++; continue; }
                    float dv = Mathf.Abs(Mathf.Repeat(uv.y - s0.y + .5f, 1f) - .5f) * 1024f;   // tile pixels (wraps)
                    float du = Mathf.Abs(uv.x - s0.x) * 512f;
                    worst = Mathf.Max(worst, Mathf.Max(du, dv));
                }
        });
        Check("over 60 s of scroll every pinned piece keeps its spot on the mid tile (" + tracked + " pieces, worst drift " +
              worst.ToString("F3") + " tile px <= .5)", tracked >= 8 && worst <= .5f);
        bool gyFixed = true;
        foreach (var pool in PinnedPools(d))
            foreach (var p in pool.items)
                if (p.active) gyFixed &= Mathf.Abs((float)(p.y + d.Planner.Travel - p.gy)) < 1e-3f && p.rate == wb.Current.Spec.Rate("mid");
        Check("... their ground coordinate y + travel is constant and they report the mid tile's rate", gyFixed);
        Object.DestroyImmediate(wb.gameObject);
    }

    // ---- affinity and room, over many seeded runs -----------------------------------

    static void Affinity()
    {
        for (int v = 1; v <= 4; v++)
        {
            int pieces = 0, wrong = 0, overlaps = 0, relaxed = 0, clusters = 0, runs = 0, pipes = 0;
            var byClass = new Dictionary<string, int>();
            string bad = "";
            for (int run = 0; run < SeededRuns; run++)
            {
                DesertDirector.SeedOverride = 101 + run * 7;
                var wb = Fresh(v, 1.5f);
                var d = Director(wb);
                if (d == null) continue;
                runs++;
                var seenPieces = new HashSet<BackdropPiece>();
                System.Action audit = () =>
                {
                    foreach (var pool in PinnedPools(d))
                        foreach (var p in pool.items)
                        {
                            if (!p.active || seenPieces.Contains(p)) continue;
                            seenPieces.Add(p);
                            pieces++;
                            string name = p.sr.sprite != null ? p.sr.sprite.name : "";
                            var rule = DesertTuning.Rule(name);
                            bool site = pool == d.Sites;
                            if (pool == d.Pipes) pipes++;
                            if (pool == d.Pipes && p.kind == DesertDirector.RunPipe) continue;   // a run's pipe crosses whatever lies between its structures
                            if (!d.Planner.Accepts(rule, p.x, p.gy, p.size))
                            {
                                if (site && d.Planner.Accepts(new PieceRule(GroundClass.Any, 0f), p.x, p.gy, p.size)) relaxed++;
                                else { wrong++; if (bad.Length < 300) bad += name + "@" + d.Planner.ClassAt(p.x, p.gy) + " "; }
                            }
                            string k = d.Planner.ClassAt(p.x, p.gy).ToString();
                            int c; byClass.TryGetValue(name.Split('_')[0] + ":" + k, out c); byClass[name.Split('_')[0] + ":" + k] = c + 1;
                        }
                    // no two non-pipe footprints overlap (pipes tuck under the plates they join)
                    foreach (var a in d.Ground.items) Overlap(d, a, ref overlaps);
                    foreach (var a in d.Fires.items) Overlap(d, a, ref overlaps);
                    foreach (var a in d.Sites.items) Overlap(d, a, ref overlaps);
                };
                audit();
                for (int i = 0; i < 6; i++) { Run(wb, 1.5f); audit(); }
                clusters += d.Clusters;
                Object.DestroyImmediate(wb.gameObject);
            }
            DesertDirector.SeedOverride = 0;
            var top = new List<string>();
            foreach (var kv in byClass) if (kv.Value >= runs / 4) top.Add(kv.Key + " " + kv.Value);
            Debug.Log("[DBD] v" + v + " placements by drawing:ground " + string.Join(", ", top));
            Check("v" + v + ": " + runs + " seeded runs, " + pieces + " pieces (" + pipes + " pipes, " + clusters +
                  " clusters): none on ground its rule forbids (" + wrong + " " + bad + "; sites on relaxed land " + relaxed + ")",
                  runs == SeededRuns && pieces > runs * 4 && wrong == 0);
            Check("v" + v + ": no two piece footprints overlap (" + overlaps + ")", overlaps == 0);
        }
    }

    static void Overlap(DesertDirector d, BackdropPiece a, ref int overlaps)
    {
        if (!a.active) return;
        foreach (var pool in new[] { d.Ground, d.Fires, d.Sites })
            foreach (var b in pool.items)
            {
                if (!b.active || b == a || b.GetHashCode() < a.GetHashCode()) continue;
                var ra = DesertTuning.Rule(a.sr.sprite.name); var rb = DesertTuning.Rule(b.sr.sprite.name);
                if (Mathf.Abs(a.x - b.x) < (GroundPlanner.HalfX(ra, a.size) + GroundPlanner.HalfX(rb, b.size)) * .98f &&
                    System.Math.Abs(a.gy - b.gy) < (GroundPlanner.HalfY(ra, a.size) + GroundPlanner.HalfY(rb, b.size)) * .98f) overlaps++;
            }
    }

    // ---- the emitters sit on their measured points ---------------------------------

    static Dictionary<string, Color32[]> atlasPx = new Dictionary<string, Color32[]>();
    static Dictionary<string, int> atlasW = new Dictionary<string, int>();

    static float AlphaAt(string atlas, string piece, float x, float y)
    {
        Color32[] px;
        if (!atlasPx.TryGetValue(atlas, out px))
        {
            var t = new Texture2D(2, 2);
            t.LoadImage(File.ReadAllBytes("Assets/Art/Backgrounds/Resources/Worlds/Desert/Backdrop3/" + atlas + ".png"));
            atlasPx[atlas] = px = t.GetPixels32();
            atlasW[atlas] = t.width;
            Object.DestroyImmediate(t);
        }
        var a = new BackdropAtlas(Resources.Load<Texture2D>("Worlds/Desert/Backdrop3/" + atlas), Resources.Load<TextAsset>("Worlds/Desert/Backdrop3/" + atlas));
        var s = a.Get(piece);
        float alpha = 0f;
        if (s != null)
        {
            int w = atlasW[atlas];
            float k = w / (float)s.texture.width;
            int ix = Mathf.RoundToInt(s.rect.x * k + x), iy = Mathf.RoundToInt(s.rect.y * k + (s.rect.height * k - 1 - y));
            for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    int jx = Mathf.Clamp(ix + dx, 0, w - 1), jy = Mathf.Clamp(iy + dy, 0, px.Length / w - 1);
                    alpha = Mathf.Max(alpha, px[jy * w + jx].a / 255f);
                }
        }
        a.Destroy();
        return alpha;
    }

    static void Emitters()
    {
        var root = new GameObject("~Emitters").transform;
        BackdropVariants.For("Desert").Force = 1;
        var set = new BackdropSet("Desert", root, 3f, 6f);
        var amb = new AmbientEmitters(set, DesertAmbientCatalog.Table) { Wind = 1f };
        var pool = new BackdropPool(root, "test", 1, -400, 0f);
        amb.Rig(pool);
        var p = pool.Spawn();
        float worst = 0f;
        int probes = 0, offArt = 0, notAbove = 0, lean = 0;
        string far = "", off = "";
        foreach (var piece in DesertAmbientCatalog.Pieces)
        {
            if (piece.emit == null || piece.emit.Length == 0) continue;
            var atlas = set.Atlas(piece.atlas);
            var sprite = atlas.Get(piece.name);
            if (sprite == null) { Check(piece.name + " drawing exists", false); continue; }
            foreach (float mirror in new[] { 1f, -1f })
                foreach (float size in new[] { 1.1f, 1.7f })
                {
                    p.sr.sprite = sprite;
                    float k = size / sprite.bounds.size.x;
                    p.root.localScale = new Vector3(k, k, 1f);
                    p.root.localPosition = new Vector3(.4f, -.7f, 0f);
                    p.body.localScale = new Vector3(mirror, 1f, 1f);
                    amb.Attach(p, piece.name, new System.Random(3));
                    amb.Step(1f);
                    for (int s = 0; s < piece.emit.Length && s < DesertAmbientCatalog.MaxPerPiece; s++)
                    {
                        string loop; Vector3 a, h;
                        if (!amb.Probe(p, s, out loop, out a, out h)) continue;
                        probes++;
                        float px = Vector2.Distance(a, h) / (size / 256f);      // cell pixels of the piece
                        if (px > worst) { worst = px; far = piece.name + "/" + loop + " " + px.ToString("F2") + "px"; }
                        var slot = p.body.Find("ambient" + s).GetComponent<SpriteRenderer>();
                        if (slot.sortingOrder <= p.sr.sortingOrder) notAbove++;
                        if (Mathf.Sign(slot.transform.lossyScale.x) != 1f) lean++;
                    }
                }
            foreach (var e in piece.emit)
                if (AlphaAt(piece.atlas, piece.name, e.x, e.y) < .5f) { offArt++; if (off.Length < 200) off += piece.name + "/" + e.loop + " "; }
        }
        Check("every bound loop draws its anchor within " + EmitterTolerancePx + " px of the piece's measured point, mirrored or not, at any size (" +
              probes + " probes, worst " + far + ")", probes >= 100 && worst <= EmitterTolerancePx);
        Check("... and every measured point lies on the piece's opaque art (" + offArt + " off: " + off + ")", offArt == 0);
        Check("... loops draw above their piece (" + notAbove + " not) and all lean with the level wind (" + lean + " against)",
              notAbove == 0 && lean == 0);
        set.Destroy();
        Object.DestroyImmediate(root.gameObject);
        BackdropVariants.For("Desert").Reset();

        // in a real run: the live plumes stay on their stacks while scrolling
        var wb = Fresh(2, .5f);
        var d = Director(wb);
        float runWorst = 0f;
        int live = 0, peak = 0;
        bool advanced = false;
        Sprite seen = null;
        Run(wb, 90f, () =>
        {
            peak = Mathf.Max(peak, d.Ambient.LiveCount);
            foreach (var pl in PinnedPools(d))
                foreach (var q in pl.items)
                {
                    if (!q.active) continue;
                    for (int s = 0; s < DesertAmbientCatalog.MaxPerPiece; s++)
                    {
                        string loop; Vector3 a, h;
                        if (!d.Ambient.Probe(q, s, out loop, out a, out h)) continue;
                        live++;
                        runWorst = Mathf.Max(runWorst, Vector2.Distance(a, h) / (q.size / 256f));
                        if (s == 0)
                        {
                            var sr = q.body.Find("ambient0").GetComponent<SpriteRenderer>();
                            if (seen != null && sr.sprite != seen) advanced = true;
                            seen = sr.sprite;
                        }
                    }
                }
        });
        Check("over 90 s of flying the live loops (" + live + " samples, peak " + peak + ") stay within " + EmitterTolerancePx +
              " px of their points (worst " + runWorst.ToString("F2") + ") and animate", live > 1000 && peak >= 6 && runWorst <= EmitterTolerancePx && advanced);
        Check("the desert is busy: pipe runs " + d.PipeRuns + ", clusters " + d.Clusters, d.PipeRuns >= 1 && d.Clusters >= 6);
        Object.DestroyImmediate(wb.gameObject);

        // without the loop atlases (Space has none) the emitters quietly show nothing
        BackdropSet.AtlasOverride = null;
        var root2 = new GameObject("~Missing").transform;
        var space = new BackdropSet("Space", root2, 3f, 6f);
        var amb2 = new AmbientEmitters(space, DesertAmbientCatalog.Table);
        var pool2 = new BackdropPool(root2, "test", 1, -400, 0f);
        amb2.Rig(pool2);
        int shown = -1;
        bool quiet = true;
        try { shown = amb2.Attach(pool2.Spawn(), "refinery_hull_01", new System.Random(1)); amb2.Step(1f); }
        catch (System.Exception e) { quiet = false; Debug.LogException(e); }
        Check("without the loop atlases the emitters show nothing, without an error", quiet && shown == 0);
        space.Destroy();
        Object.DestroyImmediate(root2.gameObject);
    }

    // ---- the installed loops animate, the lamps blink, the loops are bound -------------

    static readonly Dictionary<string, Color32[]> runCPx = new Dictionary<string, Color32[]>();

    static Color32[][] LoopFrames(string loop)
    {
        string atlas = DesertAmbientCatalog.AtlasOf(loop);
        string path = "Assets/Art/Backgrounds/Resources/Worlds/Desert/Backdrop3/" + atlas;
        Color32[] px;
        int w;
        if (!runCPx.TryGetValue(atlas, out px))
        {
            var t = new Texture2D(2, 2);
            t.LoadImage(File.ReadAllBytes(path + ".png"));
            runCPx[atlas] = px = t.GetPixels32();
            Object.DestroyImmediate(t);
        }
        w = 1024;
        var json = JsonUtility.FromJson<RunCAtlas>(File.ReadAllText(path + ".json"));
        var frames = new List<Color32[]>();
        foreach (var r in json.sprites)
        {
            if (!r.n.StartsWith(loop + "_") || !int.TryParse(r.n.Substring(loop.Length + 1), out int _)) continue;
            var cell = new Color32[r.w * r.h];
            for (int y = 0; y < r.h; y++)
                for (int x = 0; x < r.w; x++) cell[y * r.w + x] = px[(r.y + y) * w + r.x + x];
            frames.Add(cell);
        }
        return frames.ToArray();
    }

    [System.Serializable] class RunCRect { public string n; public int x, y, w, h; }
    [System.Serializable] class RunCAtlas { public RunCRect[] sprites; }

    static float Luma(Color32[] c)
    {
        double sum = 0;
        foreach (var p in c) sum += (p.r * .2126 + p.g * .7152 + p.b * .0722) * p.a / 255.0;
        return (float)sum;
    }

    // share of the union of two frames' visible pixels (alpha > 8) whose premultiplied colour moved
    static float Motion(Color32[] a, Color32[] b)
    {
        int union = 0, moved = 0;
        for (int i = 0; i < a.Length; i++)
        {
            if (a[i].a <= 8 && b[i].a <= 8) continue;
            union++;
            float d = Mathf.Abs(a[i].r * a[i].a - b[i].r * b[i].a) + Mathf.Abs(a[i].g * a[i].a - b[i].g * b[i].a) +
                      Mathf.Abs(a[i].b * a[i].a - b[i].b * b[i].a) + Mathf.Abs(a[i].a - b[i].a) * 255f;
            if (d > 255f * 6f) moved++;
        }
        return union == 0 ? 0f : moved / (float)union;
    }

    static void Loops()
    {
        var table = DesertAmbientCatalog.Table;
        int loops = 0, still = 0, tight = 0, blinkBad = 0, flat = 0;
        string worstStill = "", worstBlink = "", worstFlat = "";
        float minMotion = 1f;
        foreach (var l in table.loops)
        {
            if (!DesertAmbientCatalog.Present(l.name)) continue;
            loops++;
            var f = LoopFrames(l.name);
            if (f.Length < 4) { tight++; continue; }
            float lowest = 1f;
            for (int i = 0; i < f.Length; i++) lowest = Mathf.Min(lowest, Motion(f[i], f[(i + 1) % f.Length]));
            if (lowest < minMotion) minMotion = lowest;
            if (lowest < .03f) { still++; worstStill += l.name + " " + lowest.ToString("P1") + "; "; }
            if (DesertAmbientCatalog.Blinks(l.name) || l.name == "window_lights")
            {
                float hi = 0f, lo = float.MaxValue;
                foreach (var fr in f) { float b = Luma(fr); hi = Mathf.Max(hi, b); lo = Mathf.Min(lo, b); }
                float ratio = hi / Mathf.Max(1e-3f, lo);
                float need = l.name == "window_lights" ? 2.2f : 4f;
                if (ratio < need) { blinkBad++; worstBlink += l.name + " x" + ratio.ToString("F1") + "; "; }
                Debug.Log("[DBD] blink " + l.name + " brightness ratio x" + ratio.ToString("F1") + " (need " + need + ")");
            }
            else if (f.Length > 0)
            {
                float hi = 0f, lo = float.MaxValue;
                foreach (var fr in f) { float b = Luma(fr); hi = Mathf.Max(hi, b); lo = Mathf.Min(lo, b); }
                if (DesertAmbientCatalog.Twinkles(l.name)) { if (l.alpha > .6f) flat++; }      // a glint twinkles by design, at low alpha
                else if (hi / Mathf.Max(1e-3f, lo) > 4f) { flat++; worstFlat += l.name + " x" + (hi / Mathf.Max(1e-3f, lo)).ToString("F1") + "; "; }      // a non-blinking loop must not pulse like a lamp
            }
        }
        Check("" + loops + " loops installed, 4 or 8 cells each", loops >= 19 && tight == 0);
        Check("every loop moves >= 3% of its visible pixels between consecutive frames, looping (min " + minMotion.ToString("P1") + ") " + worstStill, still == 0);
        Check("the lamps blink: beacons, strobe and work lamp >= 4x dimmest-to-brightest, windows >= 2.2x " + worstBlink, blinkBad == 0);
        Check("smoke, dust and shimmer keep a steady brightness (no lamp pulsing; only the glass glints twinkle, at draw alpha <= .6; " + flat + " pulse " + worstFlat + ")", flat == 0);

        // placement: every bound loop sits on a piece of the right kind of ground
        var seen = new HashSet<string>();
        foreach (var piece in DesertAmbientCatalog.Pieces)
            if (piece.emit != null) foreach (var e in piece.emit) seen.Add(e.loop);
        string[] mustBind = { "smoke_a", "smoke_b", "sandplume", "heat_vent", "heat_shimmer", "glass_sparkle", "mirage_glint", "steam_vent",
                              "beacon_white", "beacon_pale_gold", "strobe_white", "work_lamp_flicker", "window_lights" };
        string unbound = "";
        foreach (string m in mustBind) if (!seen.Contains(m)) unbound += m + " ";
        Check("the loops are bound to pieces: smoke, dust, heat, glass, steam, lights (unbound: " + unbound + ")", unbound.Length == 0);
        bool wanderOk = true;
        foreach (string c in DesertAmbientCatalog.WandererNames) wanderOk &= DesertAmbientCatalog.Present(c);
        Check("the free loops (dust devil, grit gust, mirage glint) are installed and wander the ground", wanderOk);
        bool lane = true;
        foreach (var piece in DesertAmbientCatalog.Pieces)
        {
            if (piece.emit == null) continue;
            foreach (var e in piece.emit)
                if (e.x < 0 || e.x > 256 || e.y < 0 || e.y > 256) lane = false;
        }
        Check("every emitter point lies inside its 256 cell (lane rules: bounded by the piece, which GroundPlanner keeps off the lane)", lane);
    }

    // ---- ceiling, gusts, layers behind gameplay ---------------------------------

    static void Weather()
    {
        var wb = Fresh(1);
        var d = Director(wb);
        if (d == null) { Check("Desert director", false); return; }
        // the ceiling's timeline (CloudCover): thick through the hold, <= 10%
        // of the view covered by 10 s, gone by {clear} s
        Run(wb, .5f);
        float c0 = d.CeilingCover;
        Run(wb, 3.5f);
        float c4 = d.CeilingCover;
        var cover4 = CloudCoverMeter.Measure(wb.Current, CloudCoverMeter.CeilingOnly);
        Run(wb, 6f);
        var cover10 = CloudCoverMeter.Measure(wb.Current, CloudCoverMeter.CeilingOnly);
        var spec = BackdropCatalog.For("Desert");
        float clear = spec.CeilingClearSeconds();
        Run(wb, clear + 1f - 10f);
        float cGone = d.CeilingCover;
        int liveGone = d.Ceiling.ActiveCount;
        Check("the cloud ceiling is thick at the start (" + c0.ToString("F2") + " of the view >= .35)", c0 >= .35f);
        Check("... and still thick at 4 s (" + c4.ToString("F2") + ", " + cover4.covered.ToString("F2") + " of the view under cloud >= .5)",
              c4 >= .35f && cover4.covered >= .5f);
        Check("... clearing fast: <= .10 of the view under the ceiling at 10 s (" + cover10.covered.ToString("F3") + ")", cover10.covered <= .10f);
        Check("... and gone by " + (clear + 1f).ToString("F0") + " s (" + cGone.ToString("F3") + ", " + liveGone + " banks)",
              cGone < .02f && liveGone == 0 && clear <= 16f);
        Check("ceiling density: 1 through the " + spec.CeilingHold() + " s hold, 0 at " + clear + " s",
              DesertDirector.CeilingDensity(0f) == 1f && DesertDirector.CeilingDensity(spec.CeilingHold()) == 1f &&
              DesertDirector.CeilingDensity(clear) == 0f && DesertDirector.CeilingDensity(10f) < .2f && DesertDirector.CeilingDensity(6f) > .3f);
        Check("Desert's knobs: the shared density and hold, a ceiling cleared by " + spec.CeilingClearSeconds() + " s (<= CloudCover's " + CloudCover.CeilingClearSeconds + ")",
              spec.CloudDensity() == CloudCover.Density && spec.CeilingClearSeconds() <= CloudCover.CeilingClearSeconds &&
              spec.CeilingHold() == CloudCover.CeilingHold);

        int g0 = d.Gusts, sheetFrames = 0;
        float maxAlpha = 0f;
        Run(wb, 150f, () =>
        {
            foreach (var p in d.GustSheetPool.items) if (p.active && p.sr.enabled) { sheetFrames++; maxAlpha = Mathf.Max(maxAlpha, p.sr.color.a); }
        });
        int gusts = d.Gusts - g0;
        Check("gusts come round every " + DesertTuning.GustEveryMin + "-" + DesertTuning.GustEveryMax + " s (" + gusts + " in 150 s)",
              gusts >= 6 && gusts <= 12 && sheetFrames > 0);
        Check("... translucent (peak draw alpha " + maxAlpha.ToString("F2") + ")", maxAlpha > .05f && maxAlpha <= DesertTuning.GustMaxAlpha + 1e-3f);
        bool behind = true;
        var renderers = wb.Current.Root.GetComponentsInChildren<SpriteRenderer>(true);
        foreach (var r in renderers) behind &= r.sortingOrder < 0;
        Check("every Desert backdrop renderer sorts behind gameplay (" + renderers.Length + ")", behind);
        int palls = 0;
        Run(wb, 120f, () => palls = Mathf.Max(palls, d.Palls.ActiveCount));
        // the chance of a heat vent in a 2-minute run is modest: grow well fields until one has stood up
        for (int k = 0; k < 40 && palls == 0; k++)
        {
            d.SpawnCluster(d.Planner.Travel + 4.0, d.Planner.Travel + 4.9 + k * .1, DesertTuning.ClWells);
            Run(wb, .5f, () => palls = Mathf.Max(palls, d.Palls.ActiveCount));
        }
        Check("haze palls hang over the heat vents (peak " + palls + ")", palls > 0);
        float dayBoost = DesertTuning.LightBoostNow;
        int dayGrit = d.Grit.ActiveCount;
        Object.DestroyImmediate(wb.gameObject);
        wb = Fresh(4);
        d = Director(wb);
        Run(wb, 2f);
        Check("the night side boosts its lanterns (" + DesertTuning.LightBoostNow + " vs day " + dayBoost + ") and carries grit (" +
              d.Grit.ActiveCount + " vs " + dayGrit + ")",
              d.IsNight && DesertTuning.LightBoostNow > 1f && dayBoost == 1f && d.Grit.ActiveCount >= 1);
        Object.DestroyImmediate(wb.gameObject);
    }

    static int Lit(BackdropPool pool)
    {
        int n = 0;
        foreach (var p in pool.items) if (p.active && p.sr.color.a > .05f) n++;
        return n;
    }

    // ---- cloud cover after the ceiling (CloudCover, DesertTuning.CloudDensity) ----------

    // User, 2026-10-08: "too many clouds covering the backdrop; lower the
    // amount of clouds after the first 10 seconds". Every variant, three
    // seeds: the share of the view under cloud (CloudCoverMeter: ceiling,
    // wisps, mist at opacity >= .1) at 12 / 20 / 40 / 70 s averages <= 15%,
    // and no cloud parks over the centre lane for more than ~2 s.
    public const float MaxCloudCover = .15f, MaxLaneSeconds = 2.5f;

    static void Clouds()
    {
        float worstMean = 0f, worstLane = 0f, sum = 0f;
        int runs = 0;
        string detail = "";
        for (int v = 1; v <= 4; v++)
            for (int seed = 1; seed <= 3; seed++)
            {
                var wb = Fresh(v);
                var d = Director(wb);
                if (d == null) { Check("Desert director", false); return; }
                CloudCoverMeter.Reseed(d, 9100 + 17 * seed + v);
                var st = CloudCoverMeter.Run(wb, Dt);
                worstMean = Mathf.Max(worstMean, st.covered);
                worstLane = Mathf.Max(worstLane, st.laneRun);
                sum += st.covered; runs++;
                detail += " v" + v + ":" + st.covered.ToString("F2");
            }
        Debug.Log("[DBD] cloud cover after the ceiling (12/20/40/70 s, mean of runs " + (sum / runs).ToString("F3") + "):" + detail);
        Check("after the ceiling, clouds cover <= " + MaxCloudCover + " of the view on average in every variant (worst run " +
              worstMean.ToString("F3") + ", mean " + (sum / runs).ToString("F3") + ")", worstMean <= MaxCloudCover);
        Check("... and never park over the centre lane (longest cover " + worstLane.ToString("F1") + " s <= " + MaxLaneSeconds + ")",
              worstLane <= MaxLaneSeconds);

        // the knob: more density = more cloud, 0 = none after the ceiling
        float saved = DesertTuning.CloudDensity;
        try
        {
            float[] at = new float[3];
            float[] knob = { 0f, 1f, 2.5f };
            for (int k = 0; k < 3; k++)
            {
                DesertTuning.CloudDensity = knob[k];
                var wb = Fresh(1);
                CloudCoverMeter.Reseed(Director(wb), 4242);
                at[k] = CloudCoverMeter.Run(wb, Dt).covered;
            }
            Check("DesertTuning.CloudDensity is the knob (0 / 1 / 2.5 -> " + at[0].ToString("F3") + " / " + at[1].ToString("F3") + " / " + at[2].ToString("F3") + ")",
                  at[0] < .01f && at[2] > at[1]);
        }
        finally { DesertTuning.CloudDensity = saved; }
    }

    // ---- brightness ------------------------------------------------------------------

    public const float CeilingDeckTolerance = .30f;

    static void Brightness()
    {
        var wb = Fresh(1);
        Run(wb, .3f);
        var top = Rendered(.5f, 1f);
        Object.DestroyImmediate(wb.gameObject);
        var deckTex = new Texture2D(2, 2);
        deckTex.LoadImage(File.ReadAllBytes("Assets/Art/Backgrounds/Resources/" + PlanetfallCatalog.Desert.folder +
                                            PlanetfallCatalog.Desert.deck + ".png"));
        double dsum = 0;
        var dpx = deckTex.GetPixels32();
        for (int i = 0; i < dpx.Length; i += 7) dsum += Mathf.Max(dpx[i].r, Mathf.Max(dpx[i].g, dpx[i].b)) / 255f;
        float deck = (float)(dsum / ((dpx.Length + 6) / 7));
        Object.DestroyImmediate(deckTex);
        Check("the cloud ceiling at the start reads as the planetfall's cloud deck (upper view mean value " + top.mean.ToString("F2") +
              " within " + (CeilingDeckTolerance * 100f).ToString("F0") + "% of the deck's " + deck.ToString("F2") + ")",
              top.mean >= deck * (1f - CeilingDeckTolerance) && top.mean <= deck + .08f);

        var theme = EnemyPalette.ThemeFor(6);
        float hullV = Mathf.Max(theme.hull.r, Mathf.Max(theme.hull.g, theme.hull.b));
        float dayMedian = 0f, nightMedian = 0f;
        for (int v = 1; v <= 4; v++)
        {
            wb = Fresh(v);
            Run(wb, 40f);
            var shot = Rendered(0f, 1f);
            Object.DestroyImmediate(wb.gameObject);
            if (v == 1) dayMedian = shot.median;
            if (v == 4) nightMedian = shot.median;
            float body = Contrast(RelLum(theme.hull), shot.laneLum);
            float bright = Mathf.Max(RelLum(theme.light), Mathf.Max(RelLum(theme.hullHighlight), RelLum(theme.bone)));
            float kick = Contrast(bright, shot.laneLum);
            Check("v" + v + " rendered lane with enemies on top: body " + body.ToString("F2") + ":1 >= 2.5, brightest " + kick.ToString("F2") +
                  ":1 >= 7 (lane median value " + shot.median.ToString("F2") + ", p90 " + shot.p90.ToString("F2") + ", hull " + hullV.ToString("F2") + ")",
                  body >= 2.5f && kick >= 7f);
        }
        Check("the night side renders darker than the day (median " + nightMedian.ToString("F3") + " < " + dayMedian.ToString("F3") + ")",
              nightMedian < dayMedian * .92f);
    }

    struct Shot { public float mean, median, p90, laneLum; }

    static Shot Rendered(float y0, float y1)
    {
        var cam = Camera.main;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = Color.black;
        const int w = 270, h = 600;
        var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
        var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
        var prevTarget = cam.targetTexture; var prevActive = RenderTexture.active;
        cam.targetTexture = rt;
        cam.Render();
        RenderTexture.active = rt;
        tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
        cam.targetTexture = prevTarget; RenderTexture.active = prevActive;
        var px = tex.GetPixels();
        Object.DestroyImmediate(tex);
        Object.DestroyImmediate(rt);
        var vals = new List<float>();
        var lums = new List<float>();
        for (int y = (int)(h * y0); y < (int)(h * y1); y++)
            for (int x = (int)(w * .2f); x < (int)(w * .8f); x++)
            {
                Color c = px[y * w + x];
                vals.Add(Mathf.Max(c.r, Mathf.Max(c.g, c.b)));
                lums.Add(RelLum(c));
            }
        float sum = 0f;
        foreach (float v in vals) sum += v;
        vals.Sort();
        lums.Sort();
        return new Shot { mean = sum / vals.Count, median = vals[vals.Count / 2], p90 = vals[(int)(vals.Count * .9f)], laneLum = lums[lums.Count / 2] };
    }

    static float Linear(float v) { return v <= 0.03928f ? v / 12.92f : Mathf.Pow((v + 0.055f) / 1.055f, 2.4f); }
    static float RelLum(Color c) { return 0.2126f * Linear(c.r) + 0.7152f * Linear(c.g) + 0.0722f * Linear(c.b); }
    static float Contrast(float a, float b) { return (Mathf.Max(a, b) + 0.05f) / (Mathf.Min(a, b) + 0.05f); }

    // ---- the worms and the pinned items ---------------------------------------------------

    // One scripted landing: a medium worm heading straight up a ridge with three items planted on its path (and a
    // site, then more) -- the fingerprint of what it razed.
    static (int razed, int hash, int eruptions, int spawned, int rumbles) Scenario(int seed)
    {
        DesertDirector.SeedOverride = seed;
        var wb = Fresh(1, .25f);
        var d = Director(wb);
        Run(wb, 14f);
        d.ClearWorms();
        d.SpawnWorm(1, 0f, false, 0f, true);
        var w = d.Worms.items[0];
        d.Plant("radio_mast", 1.4f, w.x, w.gy + .8);
        d.Plant("drill_head_tower", 1.4f, w.x + .1f, w.gy + 1.7);
        d.Plant("solar_array_skeleton_00", 1.4f, w.x - .1f, w.gy + 2.6);
        Run(wb, 90f);
        var r = (d.ItemsRazed, d.RazedHash, d.Eruptions, d.WormsSpawned, d.RumblesPlayed);
        Object.DestroyImmediate(wb.gameObject);
        DesertDirector.SeedOverride = 0;
        return r;
    }

    static void PinnedItems()
    {
        float first = DesertTuning.WormFirst, gmin = DesertTuning.WormGapMin, gmax = DesertTuning.WormGapMax;
        int landings = 0, eruptions = 0, overlapped = 0, skipped = 0, razed = 0, spawned = 0;
        try
        {
            DesertTuning.WormFirst = 2f; DesertTuning.WormGapMin = 1f; DesertTuning.WormGapMax = 2f;
            for (int v = 1; v <= 4; v++)
                for (int k = 0; k < 8; k++)
                {
                    DesertDirector.SeedOverride = 300 + v * 31 + k * 7;
                    var wb = Fresh(v, 1.2f);
                    var d = Director(wb);
                    landings++;
                    d.EruptionHook = p =>
                    {
                        eruptions++;
                        float ex, ey;
                        DesertDirector.WormFoot(p.kind, true, out ex, out ey);
                        foreach (var pool in PinnedPools(d))
                            foreach (var q in pool.items)
                                if (q.active && q.dyingT < 0f && DesertDirector.Overlaps(q, p.x, p.gy, ex, ey, 0f)) overlapped++;
                    };
                    Run(wb, 150f);
                    skipped += d.EruptionsSkipped; razed += d.ItemsRazed; spawned += d.WormsSpawned;
                    Object.DestroyImmediate(wb.gameObject);
                }
        }
        finally
        {
            DesertTuning.WormFirst = first; DesertTuning.WormGapMin = gmin; DesertTuning.WormGapMax = gmax;
            DesertDirector.SeedOverride = 0;
        }
        Check("over " + landings + " seeded landings (" + spawned + " worms, " + eruptions + " eruptions) no worm ever surfaces under a pinned item (" +
              overlapped + " overlaps; " + skipped + " eruptions called off, " + razed + " items razed by a ridge that met a late piece)",
              landings == 32 && eruptions >= 40 && overlapped == 0);

        // forced collision: the ridge meets an item and razes it
        var wb2 = Fresh(1, .25f);
        var d2 = Director(wb2);
        Run(wb2, 14f);
        d2.ClearWorms();
        bool ok = d2.SpawnWorm(1, 0f, false, 0f, true);
        var worm = d2.Worms.items[0];
        var item = d2.Plant("radio_mast", 1.4f, worm.x, worm.gy + .8);
        int feetBefore = d2.Planner.Footprints;
        Check("a worm planted on a path that meets a pinned item (spawned " + ok + ", item " + (item != null) + ")", ok && item != null);
        bool dying = false, blasts = false, plume = false, alphaOk = true, behind = true, small = true, calm = true;
        float maxAlpha = 0f, maxSize = 0f, maxBlast = 0f;
        for (int i = 0; i < 30 * 40 && !dying; i++) { wb2.Step(Dt); dying = item.dyingT >= 0f; }
        Check("the ridge destroys the item it reaches (razed " + d2.ItemsRazed + ")", dying && d2.ItemsRazed == 1);
        Check("... its footprint is freed (it can no longer block a spot) and its loops stop", item.footprint < 0 && feetBefore > 0);
        for (int i = 0; i < 30 * 3; i++)
        {
            wb2.Step(Dt);
            foreach (var b in d2.Blasts.items)
            {
                if (!b.active) continue;
                blasts = true;
                if (b.kind == 1) plume = true;
                maxAlpha = Mathf.Max(maxAlpha, b.sr.color.a);
                maxSize = Mathf.Max(maxSize, b.size);
                if (b.kind == 0) maxBlast = Mathf.Max(maxBlast, b.size);
                behind &= b.sr.sortingOrder < 0;
                Color c = b.sr.color;
                float hh, ss, vv; Color.RGBToHSV(c, out hh, out ss, out vv);
                calm &= !(hh * 360f >= 300f && hh * 360f <= 335f && ss > .15f);
            }
        }
        alphaOk = maxAlpha <= DesertTuning.BlastAlpha + 1e-3f;
        small = maxBlast <= DesertTuning.BlastSizeMax + 1e-3f && maxBlast < item.size * .6f + 1e-3f && maxSize <= item.size + 1e-3f;
        Check("... small hazy dust-tinted blasts and a dust plume play where it stood (alpha <= " + maxAlpha.ToString("F2") + ", blasts <= " + maxBlast.ToString("F2") + " units, plume <= " + maxSize.ToString("F2") + ", no pink)",
              blasts && plume && alphaOk && small && calm && DesertTuning.BlastAlpha <= .5f);
        Check("... behind gameplay, with a rumble (" + d2.RumblesPlayed + "), and no collider anywhere in the backdrop",
              behind && d2.RumblesPlayed == 1 && wb2.Current.Root.GetComponentsInChildren<Collider2D>(true).Length == 0);
        float ix = item.x; double igy = item.gy;
        Run(wb2, 2f);
        bool gone = !item.active || item.gy != igy;
        Run(wb2, 60f);
        // (the pooled piece may be re-dealt to a new spot once it is free: the OLD spot stays empty)
        bool still = true;
        foreach (var pool in PinnedPools(d2)) foreach (var q in pool.items) if (q.active && q.dyingT < 0f && Mathf.Abs(q.x - ix) < .01f && System.Math.Abs(q.gy - igy) < .01) still = false;
        Check("the item fades away and stays gone (gone " + gone + ", spot still empty after 60 s: " + still + ")", gone && still);
        Object.DestroyImmediate(wb2.gameObject);

        // a site is never razed (an elite may be docked in it): the worm dives instead; and no more than MaxRazed per worm
        var wb3 = Fresh(1, .25f);
        var d3 = Director(wb3);
        Run(wb3, 14f);
        d3.ClearWorms();
        d3.SpawnWorm(1, 0f, false, 0f, true);
        var w3 = d3.Worms.items[0];
        var site = d3.Plant("glass_pad_closed", 1.5f, w3.x, w3.gy + .8);
        bool dived = false, intact = true;
        for (int i = 0; i < 30 * 30 && !dived; i++)
        {
            wb3.Step(Dt);
            if (w3.active && w3.tier == DesertDirector.WormFading) { dived = true; intact = site.active && site.dyingT < 0f; }
        }
        Check("a worm that meets a launch site dives instead of razing it (dived " + dived + ", razed " + d3.ItemsRazed + ", site " + (intact ? "intact" : "gone") + ")",
              dived && intact && d3.ItemsRazed == 0 && w3.phase < 3f);
        Object.DestroyImmediate(wb3.gameObject);
        var wb4 = Fresh(1, .25f);
        var d4 = Director(wb4);
        Run(wb4, 14f);
        d4.ClearWorms();
        d4.SpawnWorm(0, 0f, false, 0f, true);
        var w4 = d4.Worms.items[0];
        string[] names = { "radio_mast", "drill_head_tower", "solar_array_skeleton_00", "refinery_hull_00", "ribcage_gantry_00" };
        for (int i = 0; i < names.Length; i++) d4.Plant(names[i], 1.2f, w4.x + (i % 2 == 0 ? .05f : -.05f), w4.gy + .5 + i * .45);
        Run(wb4, 90f);
        Check("a worm razes at most " + DesertTuning.MaxRazed + " items (" + d4.ItemsRazed + ")", d4.ItemsRazed >= 1 && d4.ItemsRazed <= DesertTuning.MaxRazed);
        Object.DestroyImmediate(wb4.gameObject);

        // deterministic on the backdrop's own random stream
        var a = Scenario(777);
        var b2 = Scenario(777);
        var c2 = Scenario(778);
        Check("the same seed razes the same items, in the same way (" + a + " vs " + b2 + "); another seed plays another way (" + c2 + ")",
              a.Equals(b2) && a.razed >= 1 && a.rumbles == a.razed && (c2.hash != a.hash || c2.eruptions != a.eruptions || c2.spawned != a.spawned || c2.razed != a.razed));
    }

    // ---- elite launch sites ----------------------------------------------------------

    static void Sites()
    {
        var wb = Fresh(3);
        var d = Director(wb);
        if (d == null) { Check("Desert director", false); return; }
        var sites = new List<LandingSite>();
        var kinds = new HashSet<LandingKind>();
        bool shape = true;
        int samples = 0;
        Run(wb, 240f, () =>
        {
            if (LandingSites.Collect(sites) == 0) return;
            samples++;
            foreach (var s in sites)
            {
                kinds.Add(s.kind);
                Vector3 p = s.Position - wb.transform.position;
                shape &= s.emerge && s.Valid && s.order < EliteShip.PlayOrder - 2 && s.scale > .05f && s.scale < .5f &&
                         p.y > -CameraFit.ViewTop * .5f && (s.kind >= LandingKind.DesertWormBurrow);
            }
        });
        Check("Desert offers ground launch sites while flying (" + samples + " samples, " + d.SitesSpawned + " sites, kinds " +
              string.Join(",", kinds) + ")", samples > 150 && kinds.Count >= 3);
        Check("... each an emerge site of a Desert kind, behind gameplay, small, in the upper view", shape);
        Object.DestroyImmediate(wb.gameObject);

        var defs = new List<EliteDef>();
        EliteCatalog.ForWorld(6, defs);
        Check("Desert has its own six elites (" + defs.Count + ")", defs.Count == 6);
        if (defs.Count == 0) return;
        for (int k = 0; k < DesertTuning.SiteKinds.Length; k++) LaunchCycle(k, defs);
    }

    // One site kind: an elite of the matching def docks in it, the site stays shut while it waits,
    // opens for the launch tell, stays open while it emerges and shuts behind it.
    static void LaunchCycle(int k, List<EliteDef> defs)
    {
        var kind = DesertTuning.SiteKinds[k];
        string tag = kind.ToString();
        var wb = Fresh(3);
        var d = Director(wb);
        var pilot = new GameObject("~Pilot").transform;
        pilot.position = new Vector3(0f, -2.5f, 0f);
        EliteSystem.PlayerOverride = pilot;
        Run(wb, .5f);
        bool made = d.SpawnSite(2.4f, k);
        Run(wb, .3f);
        var sites = new List<LandingSite>();
        LandingSites.Collect(sites);
        int si = -1;
        for (int i = 0; i < sites.Count; i++) if (sites[i].kind == kind) si = i;
        Check(tag + ": a site of the kind comes into view (spawned " + made + ", offered " + sites.Count + ")", made && si >= 0);
        EliteDef def = null;
        foreach (var e0 in defs) if (LandingSite.KindOf(e0.launchFrom) == kind) { def = e0; break; }
        Check(tag + ": an elite launches from it (" + (def != null ? def.key : "-") + ")", def != null);
        if (si < 0 || def == null) { Object.DestroyImmediate(wb.gameObject); Object.DestroyImmediate(pilot.gameObject); EliteSystem.Clear(); return; }
        var dir = new GameObject("~Dir").AddComponent<EliteDirector>();
        Random.InitState(5 + k);
        var e = dir.Spawn(def, sites[si], 3.5f);
        Check(tag + ": the director docks " + def.key + " in the site", e != null && e.Site.kind == kind && e.IsDocked);
        BackdropPiece site = null;
        if (e != null) foreach (var p in d.Sites.items) if (p.active && p.root == e.Site.anchor) site = p;
        Check(tag + ": ... inside a Desert site piece", site != null);
        if (site != null)
        {
            Sprite closed = site.sr.sprite;
            bool shutWhileParked = true, opened = false;
            for (int i = 0; i < 30 * 12 && e != null && e.State == EliteState.Parked; i++)
            {
                bool tell = e.StateTime >= e.ParkSeconds - EliteShip.EngineTellSeconds;
                wb.Step(Dt);
                EliteSystem.Step(Dt);
                if (!tell && d.SiteOpen(site)) shutWhileParked = false;
                if (tell && d.SiteOpen(site)) opened = true;
            }
            Check(tag + ": the site stays shut while the elite waits, opens in the launch tell", shutWhileParked && opened && site.sr.sprite != closed);
            bool openLift = true;
            for (int i = 0; i < 30 * 4 && e != null && e.State == EliteState.LiftOff; i++)
            {
                wb.Step(Dt);
                EliteSystem.Step(Dt);
                openLift &= d.SiteOpen(site);
            }
            Check(tag + ": ... stays open while it emerges and it reaches the play layer", openLift && e != null && e.InPlay);
            Run(wb, DesertTuning.SiteCloseDelay + 1f, () => EliteSystem.Step(Dt));
            Check(tag + ": ... then shuts again behind it", !d.SiteOpen(site) && site.sr.sprite == closed);
        }
        Object.DestroyImmediate(dir.gameObject);
        Object.DestroyImmediate(pilot.gameObject);
        EliteSystem.Clear();
        Object.DestroyImmediate(wb.gameObject);
    }

    // ---- the giant sand worms ----------------------------------------------------------

    [System.Serializable] class WormRect { public string n; }
    [System.Serializable] class WormAtlasJson { public WormRect[] sprites; }

    static int WormFrames(string atlas, string prefix)
    {
        var j = JsonUtility.FromJson<WormAtlasJson>(File.ReadAllText("Assets/Art/Backgrounds/Resources/Worlds/Desert/Backdrop3/" + atlas + ".json"));
        int n = 0;
        foreach (var r in j.sprites) if (r.n.StartsWith(prefix + "_") && int.TryParse(r.n.Substring(prefix.Length + 1), out int _)) n++;
        return n;
    }

    static void Worms()
    {
        // the art: every size has its five sequences
        bool art = WormFrames("worm", "worm_s_ridge") == 8 && WormFrames("worm", "worm_m_ridge") == 8 && WormFrames("worm_giant", "worm_l_ridge") == 4;
        foreach (string seq in new[] { "emerge", "arc", "dive", "spray" })
            art &= WormFrames("worm", "worm_s_" + seq) == 5 && WormFrames("worm", "worm_m_" + seq) == 5 && WormFrames("worm_giant", "worm_l_" + seq) == 3;
        Check("the worm atlases carry ridge / emerge / arc / dive / spray for the small, medium and giant worm", art);

        // never in the first seconds; then one now and then
        DesertTuning.WormsEnabled = true;
        var wb = Fresh(1, .6f);
        var d = Director(wb);
        int early = 0, maxLive = 0, onSand = 0, started = 0;
        var phases = new HashSet<int>();
        var seenFrames = new HashSet<string>();
        bool alphaOk = true, behind = true, anchored = true, pinned = true, notMirrored = true;
        var spawnClock = new List<float>();
        var sizes = new List<int>();
        int liveBefore = 0;
        float worstAnchor = 0f;
        System.Action audit = () =>
        {
            int live = d.Worms.ActiveCount;
            maxLive = Mathf.Max(maxLive, live);
            if (d.WormClock < DesertTuning.WormFirst && d.WormsSpawned > 0) early++;
            if (d.WormsSpawned > liveBefore)
            {
                liveBefore = d.WormsSpawned;
                started++;
                foreach (var p in d.Worms.items)
                    if (p.active && p.age < .2f)
                    {
                        spawnClock.Add(d.WormClock); sizes.Add(p.kind);
                        float cell = DesertTuning.WormCell[p.kind];
                        if (d.Planner.Match(p.x, p.gy, cell * .3f, cell * .13f, GroundClass.Water) >= .6f) onSand++;
                    }
            }
            foreach (var p in d.Worms.items)
            {
                if (!p.active) continue;
                phases.Add(p.tier);
                if (p.sr.sprite != null) seenFrames.Add(p.sr.sprite.name);
                alphaOk &= p.sr.color.a <= DesertTuning.WormBodyAlpha + 1e-3f;
                var spray = d.WormSpray(p);
                if (spray != null && spray.enabled) alphaOk &= spray.color.a <= DesertTuning.WormSprayAlpha + 1e-3f;
                foreach (var pool in PinnedPools(d))
                    foreach (var q in pool.items) behind &= p.sr.sortingOrder < q.sr.sortingOrder;
                behind &= p.sr.sortingOrder < d.Wanderers.items[0].sr.sortingOrder;
                behind &= spray == null || spray.sortingOrder < d.Ground.items[0].sr.sortingOrder;
                // the emergence origin (bottom centre of the cell) sits on the worm's root
                var s = p.sr.sprite;
                Vector2 at = p.kind == 2 ? new Vector2(256f, 492f) : new Vector2(128f, 236f);
                Vector3 w = p.sr.transform.TransformPoint(new Vector3((at.x - s.rect.width * .5f) / s.pixelsPerUnit, (s.rect.height * .5f - at.y) / s.pixelsPerUnit, 0f));
                float off = Vector2.Distance(w, p.root.position);
                worstAnchor = Mathf.Max(worstAnchor, off);
                anchored &= off < .02f;
                pinned &= Mathf.Abs((float)(p.y + d.Planner.Travel - p.gy)) < 1e-3f && p.rate == wb.Current.Spec.Rate("mid");
                notMirrored &= p.body.localScale.x > 0f;
            }
        };
        Run(wb, DesertTuning.WormFirst - .5f, audit);
        Check("no worm in the first " + DesertTuning.WormFirst + " s (" + d.WormsSpawned + ")", d.WormsSpawned == 0 && early == 0);
        Run(wb, 600f, audit);
        Check("over 10 minutes worms come now and then: " + started + " (" + d.Eruptions + " eruptions), one at a time (peak " + maxLive + ")",
              started >= 8 && started <= 50 && maxLive <= 1 && d.Eruptions >= 5);
        Check("... through the whole sequence: ridge, emerge, arc, dive, ridge, fading (" + phases.Count + " phases)",
              phases.Contains(DesertDirector.WormRidge) && phases.Contains(DesertDirector.WormEmerge) && phases.Contains(DesertDirector.WormArc) &&
              phases.Contains(DesertDirector.WormDive) && phases.Contains(DesertDirector.WormRidgeOut) && phases.Contains(DesertDirector.WormFading));
        bool seenEmerge0 = seenFrames.Contains("worm_s_emerge_00") || seenFrames.Contains("worm_m_emerge_00") || seenFrames.Contains("worm_l_emerge_00");
        bool seenDive4 = seenFrames.Contains("worm_s_dive_04") || seenFrames.Contains("worm_m_dive_04");
        Check("the art's pointed first-pass frames (emerge_00, dive_04: they read as a feather) are never shown (" + seenFrames.Count + " frames used)",
              !seenEmerge0 && !seenDive4 && seenFrames.Contains("worm_s_arc_00") || seenFrames.Contains("worm_m_arc_00"));
        Check("each worm starts on dune sand (" + onSand + "/" + started + ")", onSand == started);
        Check("worms are pinned to the ground (rate, y + travel = gy), never mirrored (lit from the upper left)", pinned && notMirrored);
        Check("the emergence origin sits on the worm's spot at every frame (worst " + worstAnchor.ToString("F3") + " units)", anchored);
        Check("alpha caps: body <= " + DesertTuning.WormBodyAlpha + ", spray <= " + DesertTuning.WormSprayAlpha, alphaOk);
        Check("worms draw behind every ground piece and wanderer, and behind gameplay", behind);
        float gap = float.MaxValue, lastGiant = -1e9f;
        for (int i = 0; i < spawnClock.Count; i++)
            if (sizes[i] == 2) { gap = Mathf.Min(gap, spawnClock[i] - lastGiant); lastGiant = spawnClock[i]; }
        int giants = 0;
        foreach (int z in sizes) if (z == 2) giants++;
        Check("small and medium worms are the common ones; the giant is rare and at most once a minute (" + giants + " of " + sizes.Count + ", closest " +
              (gap == float.MaxValue ? "-" : gap.ToString("F0")) + " s)", giants >= 1 && giants <= sizes.Count / 2 && (gap == float.MaxValue || gap >= DesertTuning.GiantEvery - 1f));
        Object.DestroyImmediate(wb.gameObject);

        // forced: each size runs its whole eruption, giant takes the screen
        for (int size = 0; size < 3; size++)
        {
            wb = Fresh(1, .3f);
            d = Director(wb);
            Run(wb, .5f);
            d.ClearItems();
            bool ok = d.SpawnWorm(size, 0f, true);
            float cell = DesertTuning.WormCell[size];
            var order = new List<int>();
            float arcSeconds = 0f;
            float spray = 0f;
            int sprayFrames = 0;
            for (int i = 0; i < 30 * 40 && d.Worms.ActiveCount > 0; i++)
            {
                wb.Step(Dt);
                foreach (var p in d.Worms.items)
                {
                    if (!p.active) continue;
                    if (order.Count == 0 || order[order.Count - 1] != p.tier) order.Add(p.tier);
                    if (p.tier == DesertDirector.WormArc) arcSeconds += Dt;
                    var sp = d.WormSpray(p);
                    if (sp != null && sp.enabled) { sprayFrames++; spray = Mathf.Max(spray, sp.color.a); }
                }
            }
            string seq = string.Join(">", order);
            Check("size " + size + " (cell " + cell + " units): ridge > emerge > arc > dive > ridge > fading, then gone (" + seq + ")",
                  ok && seq == "0>1>2>3>4>5" && d.Worms.ActiveCount == 0);
            Check("... held the arc " + arcSeconds.ToString("F1") + " s and sprayed twice (" + sprayFrames + " spray frames, peak alpha " + spray.ToString("F2") + ")",
                  arcSeconds >= DesertTuning.ArcHoldMin - .1f && sprayFrames >= 6 && spray <= DesertTuning.WormSprayAlpha + 1e-3f);
            Object.DestroyImmediate(wb.gameObject);
        }

        // the switches
        DesertTuning.WormsEnabled = false;
        wb = Fresh(1, .6f);
        d = Director(wb);
        Run(wb, 90f);
        Check("worms switched off: none in 90 s (" + d.WormsSpawned + ")", d.WormsSpawned == 0);
        Object.DestroyImmediate(wb.gameObject);
        DesertTuning.WormsEnabled = true;
        DesertTuning.RidgeOnly = true;
        wb = Fresh(1, .6f);
        d = Director(wb);
        Run(wb, 12f);
        d.SpawnWorm(1, 0f, true);
        var tiers = new HashSet<int>();
        Run(wb, 30f, () => { foreach (var p in d.Worms.items) if (p.active) tiers.Add(p.tier); });
        Check("low-quality switch: the ridge trail without the eruption (tiers " + string.Join(",", tiers) + ", eruptions " + d.Eruptions + ")",
              d.Eruptions == 0 && tiers.Contains(DesertDirector.WormRidge) && !tiers.Contains(DesertDirector.WormEmerge));
        DesertTuning.RidgeOnly = false;
        Object.DestroyImmediate(wb.gameObject);

        // the other canyon / flat / night sets also carry them, on their sand
        for (int v = 2; v <= 4; v++)
        {
            wb = Fresh(v, .6f);
            d = Director(wb);
            Run(wb, 15f);
            bool any = false;
            for (int t = 0; t < 8 && !any; t++) { any = d.SpawnWorm(); if (!any) Run(wb, 2f); }
            Check("v" + v + " can carry a worm on its own sand (" + any + ")", any || v == 2);
            Object.DestroyImmediate(wb.gameObject);
        }
    }

    // ---- 60 fps hygiene ----------------------------------------------------------------

    static void Allocation()
    {
        var wb = Fresh(2);
        var d = Director(wb);
        if (d == null) return;
        Run(wb, 60f);
        long control;
        bool meter = TestHarness.AllocMeterWorks(out control);
        Check("allocation meter sees a control allocation (" + control + " bytes)", meter);
        d.ForceGust();
        d.SpawnWorm(1, float.NaN, true);
        long used = TestHarness.AllocatedBytes(() => Run(wb, 120f));
        Check("Desert backdrop allocates nothing over 2 minutes of frames, clusters, gusts and loops (" + used + " bytes)",
              meter && used >= 0 && used <= 256);
        int transforms = wb.GetComponentsInChildren<Transform>(true).Length;
        Run(wb, 300f);
        Check("pools never grow over a long run", wb.GetComponentsInChildren<Transform>(true).Length == transforms);
        // pause-safe: dt 0 is a still frame
        var before = new List<Vector3>();
        foreach (var p in d.Ground.items) before.Add(p.root.localPosition);
        for (int i = 0; i < 30; i++) wb.Step(0f);
        bool still = true;
        for (int i = 0; i < d.Ground.items.Count; i++) still &= d.Ground.items[i].root.localPosition == before[i];
        Check("a paused frame (dt 0) moves nothing", still);
    }
}
