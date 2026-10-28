using System.Collections.Generic;
using UnityEditor.SceneManagement;
using UnityEngine;

// ANCHORED OVERLAYS stay put on their backdrop. A planet world's landmarks,
// launch sites, pipes and fires are painted as separate overlay sprites over
// the ground tile they stand on; if they scroll at any other rate than that
// tile they slide over it and the illusion breaks (Frost's landmarks ran at
// 0.030 against the mid tile's 0.024).
//
// For every live world with a ground tile and every installed backdrop
// variant this advances the real WorldBackdrop for longer than a full loop
// of the host tile and asserts that every stationary piece of an anchored
// layer keeps a constant offset to its host tile (drift < 0.01 world units),
// and that each anchored layer shares its host's exact rate. NO anchored
// piece may carry a velocity of its own (Frost's icebreaker and convoy used to
// creep over the ice at up to 0.3 u in 6 s: they are pinned like the rest),
// and the host tile itself must be drawn from the same travel (its copies keep
// a constant ground phase through every wrap) at the size its current art
// gives (no hard-coded 512 / 1024 / 896 sizes). Free-floating ambient layers
// (snow, mist, wisps, aurora, gusts, cloud shadows, the flow strip) and the
// Desert worms (creatures under the sand) are deliberately not anchored.
//
//   Unity -batchmode -quit -projectPath <abs>/Pause -executeMethod BackdropAnchorTest.Run
public static class BackdropAnchorTest
{
    static int fails;
    static void Check(string what, bool ok)
    {
        Debug.Log((ok ? "[BAN] PASS  " : "[BAN] FAIL  ") + what);
        if (!ok) fails++;
    }

    public static void Run() { TestHarness.Exit(Execute()); }

    const float Dt = 1f / 30f;
    const float MaxDrift = .01f;
    const float Speed = .6f;               // moveBackGround.speed: ~25 s per mid-tile loop
    static readonly string[] Worlds = { "Frost", "Verdant", "Ember", "Tide", "Storm", "Desert" };

    public static int Execute()
    {
        fails = 0;
        using var sandbox = new TestHarness.Sandbox();
        float savedSpeed = moveBackGround.speed;
        try
        {
            Declared();
            foreach (string world in Worlds) Drift(world);
            foreach (string world in Worlds) Landform(world);
        }
        finally
        {
            moveBackGround.speed = savedSpeed;
            Time.timeScale = 1f;
            CoverBoulders.Dispose();
            CoverBoulders.TestAnyWorld = false;
            CoverBoulders.AnchorOrigin = Vector2.zero;
            foreach (string w in Worlds) BackdropVariants.For(w).Reset();
            if (WorldBackdrop.Instance != null) Object.DestroyImmediate(WorldBackdrop.Instance.gameObject);
        }
        Debug.Log("[BAN] failures: " + fails);
        return fails;
    }

    // Every ground-standing (Landmark role) layer of a planet world is pinned
    // to a tile layer and shares its rate exactly.
    static void Declared()
    {
        foreach (string world in Worlds)
        {
            var spec = BackdropCatalog.For(world);
            foreach (var l in spec.layers)
            {
                if (l.role != BackdropCatalog.Role.Landmark) continue;
                bool pinned = !string.IsNullOrEmpty(l.pinTo) && spec.Has(l.pinTo);
                Check(world + "/" + l.name + " (landmark layer) is pinned to a ground tile", pinned);
                if (!pinned) continue;
                var host = spec.Find(l.pinTo);
                Check(world + "/" + l.name + " rate " + l.rate + " == host " + l.pinTo + " rate " + host.rate,
                      Mathf.Approximately(l.rate, host.rate));
                Check(world + "/" + l.pinTo + " is a scrolling tile (no extra flow)", host.kind == BackdropCatalog.Kind.Tile && host.flow == 0f);
            }
        }
    }

    static WorldBackdrop Fresh(string world, int variant)
    {
        EliteSystem.Clear();
        EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
        Time.timeScale = 1f;
        moveBackGround.speed = Speed;
        var vars = BackdropVariants.For(world);
        vars.Reset();
        vars.Seed(11);
        vars.Force = variant;
        var cam = Camera.main;
        cam.orthographic = true;
        cam.aspect = 1080f / 2400f;
        cam.orthographicSize = CameraFit.ComputeSize(5f, CameraFit.GameplayHalfWidth, 1080, 2400);
        var wb = WorldBackdrop.Create(world);
        wb.Show(world, false);
        return wb;
    }

    class Track { public double y, x; public bool seen; }

    static void Drift(string world)
    {
        var spec = BackdropCatalog.For(world);
        int sets = Mathf.Max(1, spec.variantSets);
        for (int v = 1; v <= sets; v++)
        {
            if (spec.variantSets > 0 && !BackdropVariants.For(world).Installed(v)) continue;
            var wb = Fresh(world, v);
            var set = wb.Current;
            if (set == null || set.Director == null) { Check(world + " v" + v + " set builds", false); continue; }
            if (spec.variantSets > 0) Check(world + " v" + v + " is the variant under test (" + set.Variant + ")", set.Variant == v);

            // anchored pools -> their host tile
            var pools = new List<KeyValuePair<BackdropPool, BackdropTile>>();
            foreach (var pool in set.Director.Pools)
                foreach (var l in spec.layers)
                {
                    if (l.name != pool.name || string.IsNullOrEmpty(l.pinTo)) continue;
                    // Desert's sand worms are creatures crawling under the sand (they ride the ground frame on a heading of their own): left independent
                    if (set.Director is DesertDirector dd && pool == dd.Worms) continue;
                    BackdropTile host = null;
                    foreach (var t in set.Tiles) if (t.layer.name == l.pinTo) host = t;
                    if (host != null) pools.Add(new KeyValuePair<BackdropPool, BackdropTile>(pool, host));
                }
            Check(world + " v" + v + " has anchored pools to test (" + pools.Count + ")", pools.Count > 0);

            var tracks = new Dictionary<BackdropPiece, Track>();
            double worst = 0;
            int observed = 0;
            BackdropTile mid = null;
            foreach (var t in set.Tiles) if (t.layer.name == "mid") mid = t;
            double startTravel = mid != null ? mid.travel : 0;
            int selfMoving = 0;
            double phase0 = double.NaN, worstPhase = 0;
            {
                // the tile's size comes from its current sprite and the view, never from a constant
                Check(world + " v" + v + ": mid tile drawn at width " + (mid != null ? mid.TileWidth : 0f).ToString("F2") + " = the view plus overscan (" + (set.HalfWidth * 2f * 1.02f).ToString("F2") + ")",
                      mid != null && Mathf.Abs(mid.TileWidth - set.HalfWidth * 2f * 1.02f) < .01f);
            }
            double loop = mid != null && mid.TileHeight > 0 ? mid.TileHeight : 12;
            int steps = Mathf.CeilToInt((float)(loop / (30f * (WorldBackdrop.BaseSpeed + Speed) * spec.Rate("mid"))) * 1.2f / Dt) + 30;
            for (int i = 0; i < steps; i++)
            {
                wb.Step(Dt);
                if (mid != null)
                {
                    // ground phase of the first tile copy: (its bottom edge + travel) mod tile height is constant
                    var copy = mid.CopyTransform(0);
                    double h = mid.TileHeight;
                    double ph = (copy.localPosition.y - mid.TileHeight * .5f + mid.travel) % h;
                    if (ph < 0) ph += h;
                    if (double.IsNaN(phase0)) phase0 = ph;
                    double d0 = System.Math.Abs(ph - phase0);
                    d0 = System.Math.Min(d0, h - d0);
                    if (d0 > worstPhase) worstPhase = d0;
                }
                foreach (var kv in pools)
                {
                    foreach (var p in kv.Key.items)
                    {
                        Track tr;
                        if (p.active && p.parent == null && (p.vx != 0f || p.vy != 0f)) selfMoving++;
                        if (!p.active || p.parent != null || p.age <= 0f)
                        {
                            if (tracks.TryGetValue(p, out tr)) tr.seen = false;
                            continue;
                        }
                        Vector3 pos = p.root.localPosition;
                        double gy = pos.y + kv.Value.travel;
                        if (!tracks.TryGetValue(p, out tr)) tracks[p] = tr = new Track();
                        if (!tr.seen || p.age < Dt * 1.5f) { tr.seen = true; tr.y = gy; tr.x = pos.x; continue; }
                        double d = System.Math.Max(System.Math.Abs(gy - tr.y), System.Math.Abs(pos.x - tr.x));
                        observed++;
                        if (d > worst) worst = d;
                    }
                }
            }
            double travelled = mid != null ? mid.travel - startTravel : 0;
            Check(world + " v" + v + ": ground scrolled over a full loop (" + travelled.ToString("F1") + " >= " + loop.ToString("F1") + ")", travelled >= loop);
            Check(world + " v" + v + ": no anchored piece moves on its own (" + selfMoving + " frames)", selfMoving == 0);
            Check(world + " v" + v + ": mid tile ground phase constant through wraps (" + worstPhase.ToString("F5") + " < 0.001)", worstPhase < .001);
            Check(world + " v" + v + ": anchored pieces observed (" + observed + ")", observed > 0);
            Check(world + " v" + v + ": anchored overlay drift " + worst.ToString("F4") + " < " + MaxDrift, worst < MaxDrift);
            Object.DestroyImmediate(wb.gameObject);
        }
    }

    // Decorative LANDFORMS (the mountain / tree mass / spire / reef / mesa the cover-boulder system draws in every world but Space)
    // are scenery painted on the ground: for a whole crossing of the view, at jittery frame times and a changing scroll speed,
    // with the landform and the backdrop stepped in either order, a landform keeps its ground coordinate (y + the mid tile's
    // travel) and its x, never leans, bobs or tumbles, and keeps its size. (It used to fall and drift at a pace of its own and
    // sway / bob / rock about its centre, so it slid and wiggled over the ground it was meant to stand on.)
    static void Landform(string world)
    {
        int idx = -1;
        for (int i = 0; i < WorldManager.Worlds.Length; i++) if (WorldManager.Worlds[i].displayName == world) idx = i;
        var spec = BackdropCatalog.For(world);
        int sets = Mathf.Max(1, spec.variantSets);
        for (int v = 1; v <= sets; v++)
        {
            if (spec.variantSets > 0 && !BackdropVariants.For(world).Installed(v)) continue;
            var wb = Fresh(world, v);
            var set = wb.Current;
            BackdropTile mid = null;
            if (set != null) foreach (var t in set.Tiles) if (t.layer.name == "mid") mid = t;
            if (mid == null || idx < 0) { Check(world + " v" + v + " landform: ground tile found", false); continue; }
            CoverBoulders.Dispose();
            CoverBoulders.TestAnyWorld = true;
            Random.InitState(5 + v);
            var b = CoverBoulders.Spawn(idx, 1);
            if (b == null) { Check(world + " v" + v + " landform spawns", false); continue; }
            double gy0 = b.Pos.y + mid.travel;
            float x0 = b.Pos.x, size0 = b.GetComponentOnBody().transform.parent.localScale.x;
            double worst = 0; float worstX = 0, worstRot = 0, worstSize = 0;
            int frames = 0;
            var rng = new System.Random(v);
            for (int i = 0; i < 6000 && b.Alive; i++)
            {
                float dt = Dt * (.35f + 1.3f * (float)rng.NextDouble());
                moveBackGround.speed = .15f + .9f * Mathf.Abs(Mathf.Sin(i * .01f));
                if (i % 2 == 0) { wb.Step(dt); CoverBoulders.Step(dt); }
                else { CoverBoulders.Step(dt); wb.Step(dt); }
                if (!b.Alive) break;
                frames++;
                worst = System.Math.Max(worst, System.Math.Abs(b.Pos.y + mid.travel - gy0));
                worstX = Mathf.Max(worstX, Mathf.Abs(b.Pos.x - x0));
                // the drawn transform, not just the numbers
                var tr = b.GetComponentOnBody().transform.parent;
                worstRot = Mathf.Max(worstRot, Mathf.Abs(Mathf.DeltaAngle(0f, tr.eulerAngles.z)));
                worstSize = Mathf.Max(worstSize, Mathf.Abs(tr.localScale.x - size0));
                worst = System.Math.Max(worst, System.Math.Abs(tr.position.y + mid.travel - gy0));
            }
            Check(world + " v" + v + " landform crossed the view (" + frames + " frames)", frames > 100);
            Check(world + " v" + v + " landform stays on its ground: drift " + worst.ToString("F5") + " < 0.001", worst < .001);
            Check(world + " v" + v + " landform does not slide sideways (" + worstX.ToString("F5") + ")", worstX < 1e-4f);
            Check(world + " v" + v + " landform never leans or turns (" + worstRot.ToString("F3") + " deg)", worstRot < 1e-3f);
            Check(world + " v" + v + " landform never changes size (" + worstSize.ToString("F4") + ")", worstSize < 1e-3f);
            // a frozen world holds it still
            if (b.Alive)
            {
                float y = b.Pos.y;
                for (int i = 0; i < 10; i++) { wb.Step(0f); CoverBoulders.Step(0f); }
                Check(world + " v" + v + " landform is still when the world is frozen", Mathf.Approximately(y, b.Pos.y));
            }
            Object.DestroyImmediate(wb.gameObject);
        }
        CoverBoulders.Dispose();
    }
}
