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
// and that each anchored layer shares its host's exact rate. Pieces that
// move on their own by design (ships under way: vx / vy) and free-floating
// ambient layers (snow, mist, wisps, aurora, gusts) are not anchored.
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
    static readonly string[] Worlds = { "Frost", "Verdant", "Ember", "Tide", "Desert" };

    public static int Execute()
    {
        fails = 0;
        using var sandbox = new TestHarness.Sandbox();
        float savedSpeed = moveBackGround.speed;
        try
        {
            Declared();
            foreach (string world in Worlds) Drift(world);
        }
        finally
        {
            moveBackGround.speed = savedSpeed;
            Time.timeScale = 1f;
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
            double loop = mid != null && mid.TileHeight > 0 ? mid.TileHeight : 12;
            int steps = Mathf.CeilToInt((float)(loop / (30f * (WorldBackdrop.BaseSpeed + Speed) * spec.Rate("mid"))) * 1.2f / Dt) + 30;
            for (int i = 0; i < steps; i++)
            {
                wb.Step(Dt);
                foreach (var kv in pools)
                {
                    foreach (var p in kv.Key.items)
                    {
                        Track tr;
                        if (!p.active || p.parent != null || p.vx != 0f || p.vy != 0f || p.age <= 0f)
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
            Check(world + " v" + v + ": anchored pieces observed (" + observed + ")", observed > 0);
            Check(world + " v" + v + ": anchored overlay drift " + worst.ToString("F4") + " < " + MaxDrift, worst < MaxDrift);
            Object.DestroyImmediate(wb.gameObject);
        }
    }
}
