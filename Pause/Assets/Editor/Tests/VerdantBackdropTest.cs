using System.Collections.Generic;
using System.IO;
using UnityEditor.SceneManagement;
using UnityEngine;

// The Verdant v3 backdrop (VerdantDirector, GroundPlanner, VerdantAmbientCatalog):
//   * one of four ground sets per landing, never the same twice running; the
//     night side (v4) flagged, drawn darker, its lights stronger;
//   * PINNED pieces: a landmark's spot on the mid tile never changes while
//     it is in view (60 s of scroll);
//   * AFFINITY: over 200 seeded runs per variant no piece stands on ground
//     its rule forbids (barges on water, fires in the forest ...), and no
//     two pieces' footprints overlap;
//   * EXACT EMITTERS: every binding draws its loop with the loop's anchor
//     (plume base) within 3 px of the piece's measured point, mirrored or
//     not, and that point is on the piece's opaque art; plumes lean with the
//     level wind; loops draw above their piece;
//   * the cloud ceiling (spore cloud) thick at the start and gone by ~35 s,
//     continuing the planetfall's deck; pollen gusts; every layer behind
//     gameplay; lane readability as rendered;
//   * the Resin Warden launching from a tower bay / root hangar that opens
//     for its tell and shuts behind it; and no per-frame allocation.
// (The tiles' seams / brightness caps / texture budget are WorldBackdropTest's.)
//
//   Unity -batchmode -quit -projectPath <abs>/Pause -executeMethod VerdantBackdropTest.Run
public static class VerdantBackdropTest
{
    static int fails;
    static void Check(string what, bool ok)
    {
        Debug.Log((ok ? "[VBD] PASS  " : "[VBD] FAIL  ") + what);
        if (!ok) fails++;
    }

    public static void Run() { TestHarness.Exit(Execute()); }

    const float Dt = 1f / 30f;
    public const float EmitterTolerancePx = 3f;
    public const int SeededRuns = 200;
    static BackdropVariants Sel => BackdropVariants.For("Verdant");

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
            Weather();
            Brightness();
            Sites();
            Allocation();
        }
        finally
        {
            moveBackGround.speed = savedSpeed;
            Time.timeScale = 1f;
            Sel.Reset();
            VerdantDirector.SeedOverride = 0;
            LandingSites.Override = null;
            EliteSystem.PlayerOverride = null;
            EliteSystem.Clear();
            if (WorldBackdrop.Instance != null) Object.DestroyImmediate(WorldBackdrop.Instance.gameObject);
        }
        Debug.Log("[VBD] failures: " + fails);
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
        var wb = WorldBackdrop.Create("Verdant");
        wb.Show("Verdant", false);
        return wb;
    }

    static VerdantDirector Director(WorldBackdrop wb) { return wb.Current != null ? wb.Current.Director as VerdantDirector : null; }

    static void Run(WorldBackdrop wb, float seconds, System.Action each = null)
    {
        for (float t = 0f; t < seconds; t += Dt) { wb.Step(Dt); if (each != null) each(); }
    }

    static IEnumerable<BackdropPool> PinnedPools(VerdantDirector d)
    {
        yield return d.Pipes; yield return d.Fires; yield return d.Ground; yield return d.Sites;
    }

    // ---- the layer stack -----------------------------------------------------

    static void Catalog()
    {
        var spec = BackdropCatalog.For("Verdant");
        Check("Verdant has its own v3 folder and four variant sets",
              spec.folder == "Worlds/Verdant/Backdrop3/" && spec.variantSets == BackdropVariants.MaxVariants);
        string[] want = { "sky", "far", "mid", "ground", "flow", "palls", "mist", "wisps", "ceiling", "pollen", "spores", "fireflies" };
        bool all = true;
        foreach (string n in want) all &= spec.Has(n);
        Check("Verdant layers: ground tiles, pinned ground pieces, flow, palls, mist, wisps, ceiling, pollen, spores, fireflies", all);
        var g = spec.Find("ground");
        Check("the ground pieces are PINNED to the mid tile: same rate (" + g.rate + " = " + spec.Rate("mid") + " <= " +
              BackdropCatalog.MaxGroundRate + "), landmark role, right after it",
              g.pinTo == "mid" && g.rate == spec.Rate("mid") && g.rate <= BackdropCatalog.MaxGroundRate &&
              g.role == BackdropCatalog.Role.Landmark && System.Array.IndexOf(spec.layers, g) == System.Array.IndexOf(spec.layers, spec.Find("mid")) + 1);
        bool rising = true;
        for (int i = 1; i < spec.layers.Length; i++)
            rising &= spec.layers[i].rate > spec.layers[i - 1].rate ||
                      (spec.layers[i].pinTo == spec.layers[i - 1].name && spec.layers[i].rate == spec.layers[i - 1].rate);
        Check("Verdant rates strictly increase far -> near (a pinned layer shares its host's)", rising);
        Check("the night side is v4 only, drawn darker (" + spec.VariantBrightness(4) + ")",
              !spec.Night(1) && !spec.Night(2) && !spec.Night(3) && spec.Night(4) && spec.VariantBrightness(4) < 1f &&
              spec.VariantBrightness(1) >= .9f);
        Check("ground pieces stay under the size limit", VerdantTuning.LandmarkMax <= BackdropCatalog.MaxLandmarkSize &&
              VerdantTuning.FireMax <= BackdropCatalog.MaxLandmarkSize && VerdantTuning.SiteMax <= BackdropCatalog.MaxLandmarkSize);

        var table = VerdantAmbientCatalog.Table;
        bool loops = true;
        int bound = 0;
        foreach (var b in table.bindings)
        {
            loops &= b.emitters.Length <= table.maxPerPiece;
            foreach (var e in b.emitters) { loops &= table.LoopIndex(e.loop) >= 0; bound++; }
        }
        Check("points.json binds " + bound + " measured emitters on " + table.bindings.Length + " drawings, every loop known, <= " +
              table.maxPerPiece + " per piece", loops && table.bindings.Length >= 25 && bound >= 70);
        bool anchors = true;
        foreach (var l in table.loops)
        {
            bool measured = false;
            foreach (var m in VerdantAmbientCatalog.MeasuredLoops) if (m.name == l.name) measured = true;
            if (l.name != "window_lights" && l.name != "strobe_white" && l.name != "fireflies") anchors &= measured;
        }
        Check("every bound loop's anchor is measured on its own frames", anchors);
        bool smokeBase = true;
        foreach (var m in VerdantAmbientCatalog.MeasuredLoops)
            if (VerdantAmbientCatalog.Plume(m.name)) smokeBase &= Mathf.Abs(m.x - 128f) <= 4f && m.y >= 228f && m.spread <= 4f;
        Check("the plume loops' bases sit on their cell's bottom-centre anchor, steady over the frames", smokeBase);
        string[] smokers = { "refinery_00", "refinery_01", "refinery_02", "burnfront_00", "pipe_leak_00", "scorched_00" };
        bool smokes = true;
        foreach (string s in smokers) smokes &= VerdantAmbientCatalog.Table.For(s) != null;
        Check("refineries, burn fronts, pipe leaks and burnt industry all carry loops", smokes);
        for (int v = 1; v <= 4; v++)
        {
            var mask = GroundMask.Load("Verdant", v);
            Check("v" + v + " has its affinity mask (" + (mask != null ? mask.Cols + "x" + mask.Rows + ", water " +
                  mask.Share(GroundClass.Water).ToString("F2") + ", built " + mask.Share(GroundClass.Built).ToString("F2") : "-") + ")",
                  mask != null && mask.Cols == 32 && mask.Rows == 64 && mask.Share(GroundClass.Canopy) > .1f);
        }
        var resin = EliteCatalog.Find("verdant_elite_resin_warden");
        Check("the Resin Warden launches from a tower bay or a root hangar",
              resin != null && LandingSite.Accepts(resin.launchFrom, LandingKind.TowerBay) &&
              LandingSite.Accepts(resin.launchFrom, LandingKind.RootHangar) && !LandingSite.Accepts(resin.launchFrom, LandingKind.Hangar));
    }

    // ---- four ground sets, random per landing ----------------------------------

    static void Selection()
    {
        Sel.Reset();
        Check("all four Verdant ground sets are installed (" + Sel.InstalledCount(4) + ")", Sel.InstalledCount(4) == 4 && !Sel.Installed(5));
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
            wb.Show("Verdant", false);
            var set = wb.Current;
            if (set.Variant != last) changes++;
            last = set.Variant;
            var sky = Resources.Load<Sprite>(BackdropCatalog.TileFolder("Verdant", set.Variant) + "sky");
            if (sky != null && set.Textures.Contains(sky.texture) && set.Complete) loaded++;
        }
        Check("every landing in Verdant flies a different set, from its own folder (" + changes + "/6, " + loaded + "/6)",
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
        Check("Verdant builds its director on the v3 art", d != null && wb.Current.Complete && d.Planner.Ready && d.Planner.Mask != null);
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
                VerdantDirector.SeedOverride = 101 + run * 7;
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
                            var rule = VerdantTuning.Rule(name);
                            bool site = pool == d.Sites;
                            if (pool == d.Pipes) pipes++;
                            if (pool == d.Pipes && p.kind == VerdantDirector.RunPipe) continue;   // a run's pipe crosses whatever lies between its structures
                            if (!d.Planner.Accepts(rule, p.x, p.gy, p.size))
                            {
                                if (site && d.Planner.Accepts(new PieceRule(GroundClass.Land, .5f), p.x, p.gy, p.size)) relaxed++;
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
            VerdantDirector.SeedOverride = 0;
            var top = new List<string>();
            foreach (var kv in byClass) if (kv.Value >= runs / 4) top.Add(kv.Key + " " + kv.Value);
            Debug.Log("[VBD] v" + v + " placements by drawing:ground " + string.Join(", ", top));
            Check("v" + v + ": " + runs + " seeded runs, " + pieces + " pieces (" + pipes + " pipes, " + clusters +
                  " clusters): none on ground its rule forbids (" + wrong + " " + bad + "; sites on relaxed land " + relaxed + ")",
                  runs == SeededRuns && pieces > runs * 4 && wrong == 0);
            Check("v" + v + ": no two piece footprints overlap (" + overlaps + ")", overlaps == 0);
        }
    }

    static void Overlap(VerdantDirector d, BackdropPiece a, ref int overlaps)
    {
        if (!a.active) return;
        foreach (var pool in new[] { d.Ground, d.Fires, d.Sites })
            foreach (var b in pool.items)
            {
                if (!b.active || b == a || b.GetHashCode() < a.GetHashCode()) continue;
                var ra = VerdantTuning.Rule(a.sr.sprite.name); var rb = VerdantTuning.Rule(b.sr.sprite.name);
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
            t.LoadImage(File.ReadAllBytes("Assets/Art/Backgrounds/Resources/Worlds/Verdant/Backdrop3/" + atlas + ".png"));
            atlasPx[atlas] = px = t.GetPixels32();
            atlasW[atlas] = t.width;
            Object.DestroyImmediate(t);
        }
        var a = new BackdropAtlas(Resources.Load<Texture2D>("Worlds/Verdant/Backdrop3/" + atlas), Resources.Load<TextAsset>("Worlds/Verdant/Backdrop3/" + atlas));
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
        BackdropVariants.For("Verdant").Force = 1;
        var set = new BackdropSet("Verdant", root, 3f, 6f);
        var amb = new AmbientEmitters(set, VerdantAmbientCatalog.Table) { Wind = 1f };
        var pool = new BackdropPool(root, "test", 1, -400, 0f);
        amb.Rig(pool);
        var p = pool.Spawn();
        float worst = 0f;
        int probes = 0, offArt = 0, notAbove = 0, lean = 0;
        string far = "", off = "";
        foreach (var piece in VerdantAmbientCatalog.Pieces)
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
                    for (int s = 0; s < piece.emit.Length && s < VerdantAmbientCatalog.MaxPerPiece; s++)
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
              probes + " probes, worst " + far + ")", probes >= 300 && worst <= EmitterTolerancePx);
        Check("... and every measured point lies on the piece's opaque art (" + offArt + " off: " + off + ")", offArt == 0);
        Check("... loops draw above their piece (" + notAbove + " not) and all lean with the level wind (" + lean + " against)",
              notAbove == 0 && lean == 0);
        set.Destroy();
        Object.DestroyImmediate(root.gameObject);
        BackdropVariants.For("Verdant").Reset();

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
                    for (int s = 0; s < VerdantAmbientCatalog.MaxPerPiece; s++)
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
        Check("the forest is busy: pipe runs " + d.PipeRuns + ", clusters " + d.Clusters + ", fires seen", d.PipeRuns >= 1 && d.Clusters >= 6);
        Object.DestroyImmediate(wb.gameObject);

        // without the loop atlases (Ember has none) the emitters quietly show nothing
        var root2 = new GameObject("~Missing").transform;
        var ember = new BackdropSet("Ember", root2, 3f, 6f);
        var amb2 = new AmbientEmitters(ember, VerdantAmbientCatalog.Table);
        var pool2 = new BackdropPool(root2, "test", 1, -400, 0f);
        amb2.Rig(pool2);
        int shown = -1;
        bool quiet = true;
        try { shown = amb2.Attach(pool2.Spawn(), "refinery_00", new System.Random(1)); amb2.Step(1f); }
        catch (System.Exception e) { quiet = false; Debug.LogException(e); }
        Check("without the loop atlases the emitters show nothing, without an error", quiet && shown == 0);
        ember.Destroy();
        Object.DestroyImmediate(root2.gameObject);
    }

    // ---- ceiling, gusts, layers behind gameplay ---------------------------------

    static void Weather()
    {
        var wb = Fresh(1);
        var d = Director(wb);
        if (d == null) { Check("Verdant director", false); return; }
        Run(wb, .5f);
        float c0 = d.CeilingCover, a0 = d.CeilingBankAlpha;
        Run(wb, 14.5f);
        float c15 = d.CeilingCover, a15 = d.CeilingBankAlpha;
        Run(wb, 20f);
        float c35 = d.CeilingCover;
        Check("the spore-cloud ceiling is thick at the start (" + c0.ToString("F2") + " >= .35)", c0 >= .35f);
        Check("... thinning by 15 s (cover " + c15.ToString("F2") + ", bank alpha " + a0.ToString("F2") + " -> " + a15.ToString("F2") + ")",
              c15 < c0 * .8f || a15 < a0 * .8f);
        Check("... and gone by 35 s (" + c35.ToString("F3") + ", " + d.Ceiling.ActiveCount + " banks)", c35 < .02f && d.Ceiling.ActiveCount == 0);
        int g0 = d.Gusts, sheetFrames = 0;
        float maxAlpha = 0f;
        Run(wb, 150f, () =>
        {
            foreach (var p in d.Pollen.items) if (p.active && p.sr.enabled) { sheetFrames++; maxAlpha = Mathf.Max(maxAlpha, p.sr.color.a); }
        });
        int gusts = d.Gusts - g0;
        Check("pollen gusts come round every " + VerdantTuning.PollenEveryMin + "-" + VerdantTuning.PollenEveryMax + " s (" + gusts + " in 150 s)",
              gusts >= 6 && gusts <= 12 && sheetFrames > 0);
        Check("... translucent (peak draw alpha " + maxAlpha.ToString("F2") + ")", maxAlpha > .05f && maxAlpha <= VerdantTuning.PollenMaxAlpha + 1e-3f);
        bool behind = true;
        var renderers = wb.Current.Root.GetComponentsInChildren<SpriteRenderer>(true);
        foreach (var r in renderers) behind &= r.sortingOrder < 0;
        Check("every Verdant backdrop renderer sorts behind gameplay (" + renderers.Length + ")", behind);
        int palls = 0;
        Run(wb, 60f, () => palls = Mathf.Max(palls, d.Palls.ActiveCount));
        Check("smoke palls hang over the wildfires (peak " + palls + ")", palls > 0);
        int dayFlies = Lit(d.Fireflies);
        Object.DestroyImmediate(wb.gameObject);
        wb = Fresh(4);
        d = Director(wb);
        Run(wb, 2f);
        int nightFlies = Lit(d.Fireflies);
        Check("the night side swarms with fireflies (" + nightFlies + " vs day " + dayFlies + ") and boosts its lights (" +
              VerdantTuning.LightBoostNow + ")", d.IsNight && nightFlies > dayFlies * 2 && VerdantTuning.LightBoostNow > 1f);
        Object.DestroyImmediate(wb.gameObject);
    }

    static int Lit(BackdropPool pool)
    {
        int n = 0;
        foreach (var p in pool.items) if (p.active && p.sr.color.a > .05f) n++;
        return n;
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
        deckTex.LoadImage(File.ReadAllBytes("Assets/Art/Backgrounds/Resources/" + PlanetfallCatalog.Verdant.folder +
                                            PlanetfallCatalog.Verdant.deck + ".png"));
        double dsum = 0;
        var dpx = deckTex.GetPixels32();
        for (int i = 0; i < dpx.Length; i += 7) dsum += Mathf.Max(dpx[i].r, Mathf.Max(dpx[i].g, dpx[i].b)) / 255f;
        float deck = (float)(dsum / ((dpx.Length + 6) / 7));
        Object.DestroyImmediate(deckTex);
        Check("the cloud ceiling at the start reads as the planetfall's cloud deck (upper view mean value " + top.mean.ToString("F2") +
              " within " + (CeilingDeckTolerance * 100f).ToString("F0") + "% of the deck's " + deck.ToString("F2") + ")",
              top.mean >= deck * (1f - CeilingDeckTolerance) && top.mean <= deck + .08f);

        var theme = EnemyPalette.ThemeFor(2);
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

    // ---- elite launch sites ----------------------------------------------------------

    static void Sites()
    {
        var wb = Fresh(3);
        var d = Director(wb);
        if (d == null) { Check("Verdant director", false); return; }
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
                         p.y > -CameraFit.ViewTop * .5f && (s.kind >= LandingKind.RootHangar || s.kind == LandingKind.Hatch);
            }
        });
        Check("Verdant offers ground launch sites while flying (" + samples + " samples, " + d.SitesSpawned + " sites, kinds " +
              string.Join(",", kinds) + ")", samples > 150 && kinds.Count >= 3);
        Check("... each an emerge site of a Verdant kind, behind gameplay, small, in the upper view", shape);

        var defs = new List<EliteDef>();
        EliteCatalog.ForWorld(2, defs);
        EliteDef resin = defs.Find(x => x.key == "verdant_elite_resin_warden");
        if (resin == null) { Check("the Resin Warden is defined", false); return; }
        var pilot = new GameObject("~Pilot").transform;
        pilot.position = new Vector3(0f, -2.5f, 0f);
        EliteSystem.PlayerOverride = pilot;
        bool found = false;
        for (int i = 0; i < 30 * 120 && !found; i++)
        {
            wb.Step(Dt);
            LandingSites.Collect(sites);
            foreach (var s in sites) if (s.kind == LandingKind.TowerBay || s.kind == LandingKind.RootHangar) found = true;
            if (!found && i % 300 == 299) d.SpawnSite(float.NaN, 3);
        }
        Check("a tower bay or root hangar comes into view", found);
        if (!found) return;
        var dir = new GameObject("~Dir").AddComponent<EliteDirector>();
        Random.InitState(5);
        dir.SpawnGroup(2, 1);
        var e = EliteShip.Live.Count > 0 ? EliteShip.Live[0] : null;
        Check("the director docks the Resin Warden in its own kind of site (" + (e != null ? e.Site.kind.ToString() : "-") + ")",
              e != null && (e.Site.kind == LandingKind.TowerBay || e.Site.kind == LandingKind.RootHangar) && e.IsDocked);
        if (e == null) return;
        BackdropPiece site = null;
        foreach (var p in d.Sites.items) if (p.active && p.root == e.Site.anchor) site = p;
        Check("... inside a Verdant site piece", site != null);
        if (site == null) return;
        Sprite closed = site.sr.sprite;
        bool shutWhileParked = true, opened = false, lit = false;
        var lamp = site.body.Find("lamps") != null ? site.body.Find("lamps").GetComponent<SpriteRenderer>() : null;
        for (int i = 0; i < 30 * 12 && e != null && e.State == EliteState.Parked; i++)
        {
            bool tell = e.StateTime >= e.ParkSeconds - EliteShip.EngineTellSeconds;
            wb.Step(Dt);
            EliteSystem.Step(Dt);
            if (!tell && d.SiteOpen(site)) shutWhileParked = false;
            if (tell && d.SiteOpen(site)) opened = true;
            if (lamp != null && lamp.sprite != null && lamp.sprite.name == "lights_on") lit = true;
        }
        Check("the site stays shut while the elite waits, opens in the launch tell", shutWhileParked && opened && site.sr.sprite != closed);
        Check("... its lamp row lights up", lit);
        bool openLift = true;
        for (int i = 0; i < 30 * 4 && e != null && e.State == EliteState.LiftOff; i++)
        {
            wb.Step(Dt);
            EliteSystem.Step(Dt);
            openLift &= d.SiteOpen(site);
        }
        Check("... stays open while it emerges and it reaches the play layer", openLift && e != null && e.InPlay);
        Run(wb, VerdantTuning.SiteCloseDelay + 1f, () => EliteSystem.Step(Dt));
        Check("... then shuts again behind it", !d.SiteOpen(site) && site.sr.sprite == closed);
        Object.DestroyImmediate(dir.gameObject);
        EliteSystem.Clear();
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
        long used = TestHarness.AllocatedBytes(() => Run(wb, 120f));
        Check("Verdant backdrop allocates nothing over 2 minutes of frames, clusters, gusts and loops (" + used + " bytes)",
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
