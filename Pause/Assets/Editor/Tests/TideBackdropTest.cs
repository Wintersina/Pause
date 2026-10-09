using System.Collections.Generic;
using System.IO;
using UnityEditor.SceneManagement;
using UnityEngine;

// The Tide v3 backdrop (TideDirector, GroundPlanner, TideAmbientCatalog):
//   * one of four ground sets per landing, never the same twice running; the
//     night side (v4) flagged, drawn darker, its lights stronger;
//   * PINNED pieces: a landmark's spot on the mid tile never changes while
//     it is in view (60 s of scroll);
//   * AFFINITY: over 150 seeded runs per variant no piece stands on ground
//     its rule forbids (vessels, whirlpools and slicks on open water, rigs and
//     stacks on decks / shoals -- or anywhere on the open-sea sets, whose
//     platforms stand on their own foam), and no two footprints overlap;
//   * EXACT EMITTERS: every binding draws its loop with the loop's anchor
//     (plume base) within 3 px of the piece's measured point, mirrored or
//     not, and that point is on the piece's opaque art -- the smoke over its
//     derrick tip / column top / flare mouth, bubbles at the vent's crater;
//     plumes lean with the level wind; loops draw above their piece. Run C
//     (the loop atlases) is not painted yet: the geometry is held with
//     SYNTHETIC loop sheets (BackdropSet.AtlasOverride), and the catalog
//     reports which loops are still missing;
//   * the cloud ceiling (storm cloud) thick at the start and gone by ~15 s
//     (then a light scattering of cloud, CloudCoverMeter), continuing the
//     planetfall's deck; gusts; every layer behind gameplay; lane
//     readability as rendered; the dark worlds' thin shot outlines;
//   * an elite launching from a Tide site (trench hatch / rig bay / reef dock
//     / vent stack / wreck bay) that opens for its tell and shuts behind it
//     (Tide has no elites of its own yet: Ember's are borrowed); and no
//     per-frame allocation.
// (The tiles' seams / brightness caps / texture budget are WorldBackdropTest's.)
//
//   Unity -batchmode -quit -projectPath <abs>/Pause -executeMethod TideBackdropTest.Run
public static class TideBackdropTest
{
    static int fails;
    static void Check(string what, bool ok)
    {
        Debug.Log((ok ? "[TBD] PASS  " : "[TBD] FAIL  ") + what);
        if (!ok) fails++;
    }

    public static void Run() { TestHarness.Exit(Execute()); }

    const float Dt = 1f / 30f;
    public const float EmitterTolerancePx = 3f;
    public const int SeededRuns = 150;
    static BackdropVariants Sel => BackdropVariants.For("Tide");

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
            TideDirector.SeedOverride = 0;
            LandingSites.Override = null;
            EliteSystem.PlayerOverride = null;
            EliteSystem.Clear();
            if (WorldBackdrop.Instance != null) Object.DestroyImmediate(WorldBackdrop.Instance.gameObject);
        }
        Debug.Log("[TBD] failures: " + fails);
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
        var wb = WorldBackdrop.Create("Tide");
        wb.Show("Tide", false);
        return wb;
    }

    static TideDirector Director(WorldBackdrop wb) { return wb.Current != null ? wb.Current.Director as TideDirector : null; }

    static void Run(WorldBackdrop wb, float seconds, System.Action each = null)
    {
        for (float t = 0f; t < seconds; t += Dt) { wb.Step(Dt); if (each != null) each(); }
    }

    static IEnumerable<BackdropPool> PinnedPools(TideDirector d)
    {
        yield return d.Pipes; yield return d.Fires; yield return d.Ground; yield return d.Sites;
    }

    // ---- the layer stack -----------------------------------------------------

    static void Catalog()
    {
        var spec = BackdropCatalog.For("Tide");
        Check("Tide has its own v3 folder and four variant sets",
              spec.folder == "Worlds/Tide/Backdrop3/" && spec.variantSets == BackdropVariants.MaxVariants);
        string[] want = { "sky", "far", "mid", "ground", "flow", "palls", "mist", "wisps", "ceiling", "gusts", "rain" };
        bool all = true;
        foreach (string n in want) all &= spec.Has(n);
        Check("Tide layers: ground tiles, pinned ground pieces, flow, palls, mist, wisps, ceiling, gusts, rain", all);
        var g = spec.Find("ground");
        Check("the ground pieces are PINNED to the mid tile: same rate (" + g.rate + " = " + spec.Rate("mid") + " <= " +
              BackdropCatalog.MaxGroundRate + "), landmark role, right after it",
              g.pinTo == "mid" && g.rate == spec.Rate("mid") && g.rate <= BackdropCatalog.MaxGroundRate &&
              g.role == BackdropCatalog.Role.Landmark && System.Array.IndexOf(spec.layers, g) == System.Array.IndexOf(spec.layers, spec.Find("mid")) + 1);
        bool rising = true;
        for (int i = 1; i < spec.layers.Length; i++)
            rising &= spec.layers[i].rate > spec.layers[i - 1].rate ||
                      (spec.layers[i].pinTo == spec.layers[i - 1].name && spec.layers[i].rate == spec.layers[i - 1].rate);
        Check("Tide rates strictly increase far -> near (a pinned layer shares its host's)", rising);
        Check("the night side is v4 only, never drawn brighter than the day (" + spec.VariantBrightness(4) + " <= " + spec.VariantBrightness(1) + ")",
              !spec.Night(1) && !spec.Night(2) && !spec.Night(3) && spec.Night(4) && spec.VariantBrightness(4) <= spec.VariantBrightness(1) &&
              spec.VariantBrightness(2) >= .3f && spec.VariantBrightness(3) >= .3f);
        Check("the art is dark: Tide is drawn below the bright-world lift (" + TideTuning.Brightness + " < " + BackdropCatalog.Spec.BrightLift +
              "), so shots keep the thin outline and the hearts the standard one", !spec.Bright);
        Check("ground pieces stay under the size limit", TideTuning.LandmarkMax <= BackdropCatalog.MaxLandmarkSize &&
              TideTuning.FireMax <= BackdropCatalog.MaxLandmarkSize && TideTuning.SiteMax <= BackdropCatalog.MaxLandmarkSize);

        var table = TideAmbientCatalog.Table;
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
            if (!TideAmbientCatalog.Present(l.name)) continue;
            installed++;
            bool measured = false;
            foreach (var m in TideAmbientCatalog.MeasuredLoops) if (m.name == l.name) measured = true;
            anchors &= measured;
        }
        Check("every INSTALLED run C loop's anchor is measured on its own frames (" + installed + " installed)", anchors);
        var missing = TideAmbientCatalog.Missing();
        Debug.Log("[TBD] run C loops still missing (" + missing.Count + " of " + table.loops.Length + "): " + string.Join(", ", missing));
        Check("run C is accounted for: " + installed + " loops installed + " + missing.Count + " listed missing = " + table.loops.Length + " slots",
              installed + missing.Count == table.loops.Length);
        bool known = true;
        foreach (var b in table.bindings) foreach (var e in b.emitters) known &= table.LoopIndex(e.loop) >= 0;
        Check("every measured emitter names a declared loop slot", known);
        bool smokeBase = true;
        foreach (var m in TideAmbientCatalog.MeasuredLoops)
            if (TideAmbientCatalog.Plume(m.name)) smokeBase &= Mathf.Abs(m.x - 128f) <= 4f && m.y >= 228f && m.spread <= 4f;
        Check("the plume loops' bases sit on their cell's bottom-centre anchor, steady over the frames", smokeBase);
        string[] smokers = { "oilrig_00", "oilrig_02", "refineryrig_00", "refineryrig_01", "pump_platform_00", "lighthouse_00", "flare_stack_00", "flare_stack_01", "bubbling_vent_00", "bubbling_vent_01", "whirlpool_00", "pipe_leak_00", "pipe_leak_01", "pumphouse_00" };
        bool smokes = true;
        foreach (string s in smokers) smokes &= TideAmbientCatalog.Table.For(s) != null;
        Check("derricks, refinery columns, the lighthouse, flare stacks, vents, whirlpools and leaking pipes all carry loops", smokes);
        for (int v = 1; v <= 4; v++)
        {
            var mask = GroundMask.Load("Tide", v);
            Check("v" + v + " has its affinity mask (" + (mask != null ? mask.Cols + "x" + mask.Rows + ", water " +
                  mask.Share(GroundClass.Water).ToString("F2") + ", built " + mask.Share(GroundClass.Built).ToString("F2") : "-") + ")",
                  mask != null && mask.Cols == 32 && mask.Rows == 64 && mask.Share(GroundClass.Water) > .5f);
        }
        var kiln = EliteCatalog.Find("ember_elite_kilnback");
        bool any = kiln != null;
        foreach (var k in TideTuning.SiteKinds) any &= LandingSite.Accepts(kiln != null ? kiln.launchFrom : null, k);
        Check("an elite with no launchFrom takes any of the five Tide site kinds", any);
        Check("the sites' kinds are Tide's own and parse from launchFrom strings",
              LandingSite.KindOf("trenchhatch") == LandingKind.TideTrenchHatch && LandingSite.KindOf("rigdeck") == LandingKind.TideRigDeck &&
              LandingSite.KindOf("reefdock") == LandingKind.TideReefDock && LandingSite.KindOf("ventstack") == LandingKind.TideVentStack &&
              LandingSite.KindOf("wreckbay") == LandingKind.TideWreckBay &&
              LandingSite.Accepts("ventstack,reefdock", LandingKind.TideReefDock) && !LandingSite.Accepts("ventstack,reefdock", LandingKind.TideRigDeck) &&
              !LandingSite.Accepts("rigdeck", LandingKind.FurnaceBay));
    }

    // ---- four ground sets, random per landing ----------------------------------

    static void Selection()
    {
        Sel.Reset();
        Check("all four Tide ground sets are installed (" + Sel.InstalledCount(4) + ")", Sel.InstalledCount(4) == 4 && !Sel.Installed(5));
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
            wb.Show("Tide", false);
            var set = wb.Current;
            if (set.Variant != last) changes++;
            last = set.Variant;
            var sky = Resources.Load<Sprite>(BackdropCatalog.TileFolder("Tide", set.Variant) + "sky");
            if (sky != null && set.Textures.Contains(sky.texture) && set.Complete) loaded++;
        }
        Check("every landing in Tide flies a different set, from its own folder (" + changes + "/6, " + loaded + "/6)",
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
        Check("Tide builds its director on the v3 art", d != null && wb.Current.Complete && d.Planner.Ready && d.Planner.Mask != null);
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
                TideDirector.SeedOverride = 101 + run * 7;
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
                            var rule = TideTuning.Rule(name, TideTuning.OpenSea[v]);
                            bool site = pool == d.Sites;
                            if (pool == d.Pipes) pipes++;
                            if (pool == d.Pipes && p.kind == TideDirector.RunPipe) continue;   // a run's pipe crosses whatever lies between its structures
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
            TideDirector.SeedOverride = 0;
            var top = new List<string>();
            foreach (var kv in byClass) if (kv.Value >= runs / 4) top.Add(kv.Key + " " + kv.Value);
            Debug.Log("[TBD] v" + v + " placements by drawing:ground " + string.Join(", ", top));
            Check("v" + v + ": " + runs + " seeded runs, " + pieces + " pieces (" + pipes + " pipes, " + clusters +
                  " clusters): none on ground its rule forbids (" + wrong + " " + bad + "; sites on relaxed land " + relaxed + ")",
                  runs == SeededRuns && pieces > runs * 4 && wrong == 0);
            Check("v" + v + ": no two piece footprints overlap (" + overlaps + ")", overlaps == 0);
        }
    }

    static void Overlap(TideDirector d, BackdropPiece a, ref int overlaps)
    {
        if (!a.active) return;
        foreach (var pool in new[] { d.Ground, d.Fires, d.Sites })
            foreach (var b in pool.items)
            {
                if (!b.active || b == a || b.GetHashCode() < a.GetHashCode()) continue;
                var ra = TideTuning.Rule(a.sr.sprite.name); var rb = TideTuning.Rule(b.sr.sprite.name);
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
            t.LoadImage(File.ReadAllBytes("Assets/Art/Backgrounds/Resources/Worlds/Tide/Backdrop3/" + atlas + ".png"));
            atlasPx[atlas] = px = t.GetPixels32();
            atlasW[atlas] = t.width;
            Object.DestroyImmediate(t);
        }
        var a = new BackdropAtlas(Resources.Load<Texture2D>("Worlds/Tide/Backdrop3/" + atlas), Resources.Load<TextAsset>("Worlds/Tide/Backdrop3/" + atlas));
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

    // ---- synthetic run C sheets --------------------------------------------------

    // Stand-ins for the loop atlases Codex has not painted yet (smoke, flames,
    // surf, leaks, lights): every declared loop gets two 256 cells of a bar
    // standing on the foot anchor (128, 236) -- or a square on the centre for
    // the centred loops -- so the geometry (anchor on the measured point, mirrored
    // or not, leaning with the wind, above the piece) is held today. Real run C
    // art replaces them with no change here: AtlasOverride yields to an installed atlas.
    static readonly Dictionary<string, Texture2D> fakeTex = new Dictionary<string, Texture2D>();

    static BackdropAtlas FakeLoops(string world, string atlas)
    {
        if (world != "Tide") return null;
        if (Resources.Load<TextAsset>(BackdropCatalog.Folder("Tide") + atlas) != null) return null;     // the real sheet is in
        var names = new List<string>();
        foreach (string loop in TideAmbientCatalog.LoopNames) if (TideAmbientCatalog.AtlasOf(loop) == atlas) names.Add(loop);
        if (names.Count == 0 || names.Count > 8) return null;
        Texture2D tex;
        if (!fakeTex.TryGetValue(atlas, out tex) || tex == null)
        {
            tex = new Texture2D(1024, 1024, TextureFormat.RGBA32, false) { name = "fake_" + atlas };
            var px = new Color32[1024 * 1024];
            for (int n = 0; n < names.Count; n++)
                for (int f = 0; f < 2; f++)
                {
                    int cell = n * 2 + f, cx = (cell % 4) * 256, cyTop = (cell / 4) * 256;
                    bool centred = TideAmbientCatalog.AtlasOf(names[n]) == "surf" || names[n] == "window_lights";
                    int x0 = centred ? 114 : 122, x1 = centred ? 142 : 134, y0 = centred ? 114 : 206 - f * 4, y1 = centred ? 142 : 236;
                    for (int y = y0; y < y1; y++)
                        for (int x = x0; x < x1; x++)
                            px[(1023 - (cyTop + y)) * 1024 + cx + x] = new Color32(230, 240, 235, 255);
                }
            tex.SetPixels32(px);
            tex.Apply();
            fakeTex[atlas] = tex;
        }
        var sb = new System.Text.StringBuilder("{\"sprites\":[");
        for (int n = 0; n < names.Count; n++)
            for (int f = 0; f < 2; f++)
            {
                int cell = n * 2 + f;
                if (cell > 0) sb.Append(',');
                sb.Append("{\"n\":\"").Append(names[n]).Append('_').Append(f.ToString("00")).Append("\",\"x\":").Append((cell % 4) * 256)
                  .Append(",\"y\":").Append(768 - (cell / 4) * 256).Append(",\"w\":256,\"h\":256}");
            }
        sb.Append("]}");
        return new BackdropAtlas(tex, new TextAsset(sb.ToString()));
    }

    static void Emitters()
    {
        BackdropSet.AtlasOverride = FakeLoops;
        try { EmittersBody(); }
        finally { BackdropSet.AtlasOverride = null; }
    }

    static void EmittersBody()
    {
        var root = new GameObject("~Emitters").transform;
        BackdropVariants.For("Tide").Force = 1;
        var set = new BackdropSet("Tide", root, 3f, 6f);
        var amb = new AmbientEmitters(set, TideAmbientCatalog.Table) { Wind = 1f };
        var pool = new BackdropPool(root, "test", 1, -400, 0f);
        amb.Rig(pool);
        var p = pool.Spawn();
        float worst = 0f;
        int probes = 0, offArt = 0, notAbove = 0, lean = 0;
        string far = "", off = "";
        foreach (var piece in TideAmbientCatalog.Pieces)
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
                    for (int s = 0; s < piece.emit.Length && s < TideAmbientCatalog.MaxPerPiece; s++)
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
        BackdropVariants.For("Tide").Reset();

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
                    for (int s = 0; s < TideAmbientCatalog.MaxPerPiece; s++)
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
        Check("the ocean world is busy: pipe runs " + d.PipeRuns + ", clusters " + d.Clusters + ", fires seen", d.PipeRuns >= 1 && d.Clusters >= 6);
        Object.DestroyImmediate(wb.gameObject);

        // without the loop atlases (Space has none) the emitters quietly show nothing
        BackdropSet.AtlasOverride = null;
        var root2 = new GameObject("~Missing").transform;
        var space = new BackdropSet("Space", root2, 3f, 6f);
        var amb2 = new AmbientEmitters(space, TideAmbientCatalog.Table);
        var pool2 = new BackdropPool(root2, "test", 1, -400, 0f);
        amb2.Rig(pool2);
        int shown = -1;
        bool quiet = true;
        try { shown = amb2.Attach(pool2.Spawn(), "refineryrig_01", new System.Random(1)); amb2.Step(1f); }
        catch (System.Exception e) { quiet = false; Debug.LogException(e); }
        Check("without the loop atlases the emitters show nothing, without an error", quiet && shown == 0);
        space.Destroy();
        Object.DestroyImmediate(root2.gameObject);
    }

    // ---- ceiling, gusts, layers behind gameplay ---------------------------------

    static void Weather()
    {
        var wb = Fresh(1);
        var d = Director(wb);
        if (d == null) { Check("Tide director", false); return; }
        // the ceiling's timeline (CloudCover): thick through the hold, <= 10%
        // of the view covered by 10 s, gone by {clear} s
        Run(wb, .5f);
        float c0 = d.CeilingCover;
        Run(wb, 3.5f);
        float c4 = d.CeilingCover;
        var cover4 = CloudCoverMeter.Measure(wb.Current, CloudCoverMeter.CeilingOnly);
        Run(wb, 6f);
        var cover10 = CloudCoverMeter.Measure(wb.Current, CloudCoverMeter.CeilingOnly);
        var spec = BackdropCatalog.For("Tide");
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
              TideDirector.CeilingDensity(0f) == 1f && TideDirector.CeilingDensity(spec.CeilingHold()) == 1f &&
              TideDirector.CeilingDensity(clear) == 0f && TideDirector.CeilingDensity(10f) < .2f && TideDirector.CeilingDensity(6f) > .3f);
        Check("Tide's knobs: the shared density and hold, a ceiling cleared by " + spec.CeilingClearSeconds() + " s (<= CloudCover's " + CloudCover.CeilingClearSeconds + ")",
              spec.CloudDensity() == CloudCover.Density && spec.CeilingClearSeconds() <= CloudCover.CeilingClearSeconds &&
              spec.CeilingHold() == CloudCover.CeilingHold);

        int g0 = d.Gusts, sheetFrames = 0;
        float maxAlpha = 0f;
        Run(wb, 150f, () =>
        {
            foreach (var p in d.GustSheetPool.items) if (p.active && p.sr.enabled) { sheetFrames++; maxAlpha = Mathf.Max(maxAlpha, p.sr.color.a); }
        });
        int gusts = d.Gusts - g0;
        Check("gusts come round every " + TideTuning.GustEveryMin + "-" + TideTuning.GustEveryMax + " s (" + gusts + " in 150 s)",
              gusts >= 6 && gusts <= 12 && sheetFrames > 0);
        Check("... translucent (peak draw alpha " + maxAlpha.ToString("F2") + ")", maxAlpha > .05f && maxAlpha <= TideTuning.GustMaxAlpha + 1e-3f);
        bool behind = true;
        var renderers = wb.Current.Root.GetComponentsInChildren<SpriteRenderer>(true);
        foreach (var r in renderers) behind &= r.sortingOrder < 0;
        Check("every Tide backdrop renderer sorts behind gameplay (" + renderers.Length + ")", behind);
        int palls = 0;
        Run(wb, 120f, () => palls = Mathf.Max(palls, d.Palls.ActiveCount));
        // the chance of a flare stack in a 2-minute run is modest: grow vent fields until one has stood up
        for (int k = 0; k < 40 && palls == 0; k++)
        {
            d.SpawnCluster(d.Planner.Travel + 4.0, d.Planner.Travel + 4.9 + k * .1, TideTuning.ClVents);
            Run(wb, .5f, () => palls = Mathf.Max(palls, d.Palls.ActiveCount));
        }
        Check("smoke palls hang over the flare stacks (peak " + palls + ")", palls > 0);
        float dayBoost = TideTuning.LightBoostNow;
        int dayRain = d.Rain.ActiveCount;
        Object.DestroyImmediate(wb.gameObject);
        wb = Fresh(4);
        d = Director(wb);
        Run(wb, 2f);
        Check("the night side boosts its lanterns (" + TideTuning.LightBoostNow + " vs day " + dayBoost + ") and rains (" +
              d.Rain.ActiveCount + " vs " + dayRain + ")",
              d.IsNight && TideTuning.LightBoostNow > 1f && dayBoost == 1f && d.Rain.ActiveCount >= 1);
        Object.DestroyImmediate(wb.gameObject);
    }

    static int Lit(BackdropPool pool)
    {
        int n = 0;
        foreach (var p in pool.items) if (p.active && p.sr.color.a > .05f) n++;
        return n;
    }

    // ---- cloud cover after the ceiling (CloudCover, TideTuning.CloudDensity) ----------

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
                if (d == null) { Check("Tide director", false); return; }
                CloudCoverMeter.Reseed(d, 9100 + 17 * seed + v);
                var st = CloudCoverMeter.Run(wb, Dt);
                worstMean = Mathf.Max(worstMean, st.covered);
                worstLane = Mathf.Max(worstLane, st.laneRun);
                sum += st.covered; runs++;
                detail += " v" + v + ":" + st.covered.ToString("F2");
            }
        Debug.Log("[TBD] cloud cover after the ceiling (12/20/40/70 s, mean of runs " + (sum / runs).ToString("F3") + "):" + detail);
        Check("after the ceiling, clouds cover <= " + MaxCloudCover + " of the view on average in every variant (worst run " +
              worstMean.ToString("F3") + ", mean " + (sum / runs).ToString("F3") + ")", worstMean <= MaxCloudCover);
        Check("... and never park over the centre lane (longest cover " + worstLane.ToString("F1") + " s <= " + MaxLaneSeconds + ")",
              worstLane <= MaxLaneSeconds);

        // the knob: more density = more cloud, 0 = none after the ceiling
        float saved = TideTuning.CloudDensity;
        try
        {
            float[] at = new float[3];
            float[] knob = { 0f, 1f, 2.5f };
            for (int k = 0; k < 3; k++)
            {
                TideTuning.CloudDensity = knob[k];
                var wb = Fresh(1);
                CloudCoverMeter.Reseed(Director(wb), 4242);
                at[k] = CloudCoverMeter.Run(wb, Dt).covered;
            }
            Check("TideTuning.CloudDensity is the knob (0 / 1 / 2.5 -> " + at[0].ToString("F3") + " / " + at[1].ToString("F3") + " / " + at[2].ToString("F3") + ")",
                  at[0] < .01f && at[2] > at[1]);
        }
        finally { TideTuning.CloudDensity = saved; }
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
        deckTex.LoadImage(File.ReadAllBytes("Assets/Art/Backgrounds/Resources/" + PlanetfallCatalog.Tide.folder +
                                            PlanetfallCatalog.Tide.deck + ".png"));
        double dsum = 0;
        var dpx = deckTex.GetPixels32();
        for (int i = 0; i < dpx.Length; i += 7) dsum += Mathf.Max(dpx[i].r, Mathf.Max(dpx[i].g, dpx[i].b)) / 255f;
        float deck = (float)(dsum / ((dpx.Length + 6) / 7));
        Object.DestroyImmediate(deckTex);
        Check("the cloud ceiling at the start reads as the planetfall's cloud deck (upper view mean value " + top.mean.ToString("F2") +
              " within " + (CeilingDeckTolerance * 100f).ToString("F0") + "% of the deck's " + deck.ToString("F2") + ")",
              top.mean >= deck * (1f - CeilingDeckTolerance) && top.mean <= deck + .08f);

        var theme = EnemyPalette.ThemeFor(4);
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
        if (d == null) { Check("Tide director", false); return; }
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
                         p.y > -CameraFit.ViewTop * .5f && (s.kind >= LandingKind.TideTrenchHatch);
            }
        });
        Check("Tide offers ground launch sites while flying (" + samples + " samples, " + d.SitesSpawned + " sites, kinds " +
              string.Join(",", kinds) + ")", samples > 150 && kinds.Count >= 3);
        Check("... each an emerge site of a Tide kind, behind gameplay, small, in the upper view", shape);

        var defs = new List<EliteDef>();
        EliteCatalog.ForWorld(3, defs);
        Check("Tide has no elites of its own yet; Ember's six are borrowed to prove the sites (" + defs.Count + ")", defs.Count == 6);
        if (defs.Count == 0) return;
        var pilot = new GameObject("~Pilot").transform;
        pilot.position = new Vector3(0f, -2.5f, 0f);
        EliteSystem.PlayerOverride = pilot;
        bool found = false;
        for (int i = 0; i < 30 * 120 && !found; i++)
        {
            wb.Step(Dt);
            LandingSites.Collect(sites);
            foreach (var s in sites) if (s.kind >= LandingKind.TideTrenchHatch) found = true;
            if (!found && i % 300 == 299) d.SpawnSite(float.NaN, 3);
        }
        Check("a Tide launch site comes into view", found);
        if (!found) return;
        var dir = new GameObject("~Dir").AddComponent<EliteDirector>();
        Random.InitState(5);
        dir.SpawnGroup(3, 1);
        var e = EliteShip.Live.Count > 0 ? EliteShip.Live[0] : null;
        Check("the director docks an elite in one of the world's own sites (" + (e != null ? e.Site.kind.ToString() : "-") + ")",
              e != null && e.Site.kind >= LandingKind.TideTrenchHatch && e.IsDocked);
        if (e == null) return;
        BackdropPiece site = null;
        foreach (var p in d.Sites.items) if (p.active && p.root == e.Site.anchor) site = p;
        Check("... inside a Tide site piece", site != null);
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
        Run(wb, TideTuning.SiteCloseDelay + 1f, () => EliteSystem.Step(Dt));
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
        Check("Tide backdrop allocates nothing over 2 minutes of frames, clusters, gusts and loops (" + used + " bytes)",
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
