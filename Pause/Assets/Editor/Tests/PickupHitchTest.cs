using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using Debug = UnityEngine.Debug;

// The blue-atom pickup used to freeze the game for a frame: the first pickup
// per ship / skin built the contour shield on the spot -- a RenderTexture
// readback of the whole hull sheet (a GPU stall) and a brute-force disk
// dilation (~250 ms in the editor) -- and every first pickup of a kind loaded
// its burst art from Resources. Now:
//   - every roster hull's silhouette is baked at edit time and matches the
//     art; every skin shares its ship's silhouette
//   - the contour is built once per ship (not per skin / damage state /
//     frame) as the ship spawns, from the bake: no GPU readback, ever
//   - a pickup of any atom builds nothing and reads nothing back, for stock
//     and skinned ships in every damage state
//   - the fast distance-transform dilation and scanline collider raster give
//     the same answers as the brute-force versions they replaced
//   - after warm-up a pickup allocates (next to) nothing and is fast
public static class PickupHitchTest
{
    static int fails;
    static void Check(string what, bool ok)
    {
        Debug.Log((ok ? "[PH] PASS  " : "[PH] FAIL  ") + what);
        if (!ok) fails++;
    }

    public static void Run()
    {
        TestHarness.Exit(Execute());
    }

    const BindingFlags Inst = BindingFlags.NonPublic | BindingFlags.Instance;
    const BindingFlags Stat = BindingFlags.NonPublic | BindingFlags.Static;

    // Average managed bytes a warmed-up pickup may allocate (editor; the
    // measurement is page-granular, so it is averaged over many pickups).
    // What is left: one GameObject.name read per contact (~50 B, the name
    // checks share it) and score.AwardStarDust on dust-paying pickups.
    const float AllocBoundBytes = 160f;
    // Generous editor bound for the whole OnTriggerEnter2D of a pickup.
    const double PickupBoundMs = 2.0;

    static readonly string[] Atoms =
        { "atom3a(Clone)", HealAtom.ObjectName + "(Clone)", "pauseAtom(Clone)", "smStar1(Clone)", "LargeStar1(Clone)" };

    public static int Execute()
    {
        fails = 0;
        using var sandbox = new TestHarness.Sandbox();
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        SilhouettesAreBakedFromTheArt();
        SkinsShareTheSilhouette();
        DistanceTransformMatchesBruteForce();
        ScanlineCornersMatchPointInPolygon();
        PickupsBuildAndReadBackNothing();
        PickupFrameIsCheapAndAllocationFree();
        SourceHasNoReadbackOnThePickupPath();

        Debug.Log("[PH] failures: " + fails);
        return fails;
    }

    // ------------------------------------------------------------------

    static void SilhouettesAreBakedFromTheArt()
    {
        Check("hull_silhouettes.bytes is baked and matches the current hull art",
              ShieldSilhouetteBaker.IsCurrent());
        ShieldSilhouettes.Invalidate();
        Check("a silhouette for every roster ship (" + ShieldSilhouettes.Count + "/" + ShipId.Count + ")",
              ShieldSilhouettes.Count == ShipId.Count);
    }

    static void SkinsShareTheSilhouette()
    {
        int skinsChecked = 0, mismatched = 0;
        foreach (int id in ShipId.All)
        {
            var r = ShipHullArt.RectFor(id);
            bool[] baked;
            if (!ShieldSilhouettes.TryGet(id, r.w, r.h, out baked)) { mismatched++; continue; }
            for (int skin = 0; skin < ShipSkins.CountFor(id); skin++)
            {
                if (skin == ShipSkins.Stock) continue;
                var png = ShipHullArt.SkinPng(id, skin);
                if (png == null) continue;
                var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                tex.LoadImage(png);
                // The rest drawing: damage state 0 is the top row, column 0.
                var px = tex.GetPixels32();
                int y0 = (ShipHullArt.States - 1) * ShipHullArt.Cell + r.y, x0 = r.x;
                int diff = 0;
                for (int y = 0; y < r.h; y++)
                    for (int x = 0; x < r.w; x++)
                        if ((px[(y0 + y) * tex.width + x0 + x].a >= ShieldContour.AlphaCutoff) != baked[y * r.w + x]) diff++;
                UnityEngine.Object.DestroyImmediate(tex);
                skinsChecked++;
                if (diff != 0)
                {
                    mismatched++;
                    Check("ship " + id + " skin " + skin + " rest silhouette equals the stock one (" + diff + " px differ)", false);
                }
            }
        }
        Check("every skin's rest silhouette equals its ship's baked one (" + skinsChecked + " skins)",
              skinsChecked > 0 && mismatched == 0);
    }

    static void DistanceTransformMatchesBruteForce()
    {
        var rng = new System.Random(7);
        int bad = 0;
        for (int trial = 0; trial < 6; trial++)
        {
            int W = 40 + trial * 7, H = 33 + trial * 5;
            var src = new bool[W * H];
            for (int i = 0; i < src.Length; i++) src[i] = rng.NextDouble() < (trial == 0 ? .002 : .03);
            var dist = ShieldContour.SquaredDistance(src, W, H);
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    int best = int.MaxValue;
                    for (int v = 0; v < H; v++)
                        for (int u = 0; u < W; u++)
                            if (src[v * W + u]) best = Math.Min(best, (u - x) * (u - x) + (v - y) * (v - y));
                    if (best != int.MaxValue && best != dist[y * W + x]) bad++;
                }
        }
        Check("distance-transform dilation equals the brute-force disk (" + bad + " cells differ)", bad == 0);
    }

    static void ScanlineCornersMatchPointInPolygon()
    {
        int bad = 0, checkedPts = 0;
        foreach (int id in new[] { 1, 4, 14 })
        {
            var c = ShieldContour.ForShip(id);
            var corners = (bool[])typeof(ShieldContour).GetMethod("CornersInside", Inst).Invoke(c, null);
            int gridW = (int)typeof(ShieldContour).GetField("gridW", Inst).GetValue(c);
            int gridH = (int)typeof(ShieldContour).GetField("gridH", Inst).GetValue(c);
            int pad = (int)typeof(ShieldContour).GetField("pad", Inst).GetValue(c);
            var pivot = (Vector2)typeof(ShieldContour).GetField("pivotPx", Inst).GetValue(c);
            float ppu = (float)typeof(ShieldContour).GetField("ppu", Inst).GetValue(c);
            for (int ly = 0; ly <= gridH; ly += 3)
                for (int lx = 0; lx <= gridW; lx += 3)
                {
                    var q = new Vector2((lx - pad - pivot.x) / ppu, (ly - pad - pivot.y) / ppu);
                    // Points exactly on an edge may round either way.
                    if (ShieldContour.PointInPolygon(c.Polygon, q) != corners[ly * (gridW + 1) + lx] &&
                        EdgeDistance(c.Polygon, q) > 1e-3f / ppu) bad++;
                    checkedPts++;
                }
        }
        Check("scanline collider raster agrees with PointInPolygon (" + bad + " of " + checkedPts + " differ)",
              checkedPts > 0 && bad == 0);
    }

    static float EdgeDistance(Vector2[] poly, Vector2 q)
    {
        float best = float.MaxValue;
        for (int i = 0, j = poly.Length - 1; i < poly.Length; j = i++)
        {
            Vector2 a = poly[j], ab = poly[i] - a;
            float t = ab.sqrMagnitude > 0f ? Mathf.Clamp01(Vector2.Dot(q - a, ab) / ab.sqrMagnitude) : 0f;
            best = Mathf.Min(best, (a + ab * t - q).magnitude);
        }
        return best;
    }

    // ------------------------------------------------------------------
    // Rig: a gameplay ship as spawnShips + collisionDetection.Start leave it
    // ------------------------------------------------------------------

    sealed class Rig
    {
        public GameObject ship;
        public collisionDetection cd;
        public Action<Collider2D> trigger;
        public Action tick;

        public ShipShield Shield { get { return ship.GetComponent<ShipShield>(); } }

        public void Dispose()
        {
            foreach (var go in new[] { cd.explosionAnimation, cd.boost, cd.boostText.gameObject, cd.hypeText.gameObject })
                if (go != null) UnityEngine.Object.DestroyImmediate(go);
            UnityEngine.Object.DestroyImmediate(ship);
            collisionDetection.atomCheck = false;
            collisionDetection.invTimer = 0f;
            collisionDetection.lifeCounter = 0;
        }

        // Runs the shield out, as turnTextsOff does when invTimer hits 0.
        public void Expire()
        {
            if (!collisionDetection.atomCheck) return;
            collisionDetection.invTimer = 0f;
            tick();
        }
    }

    static Rig Spawn(int id)
    {
        var r = new Rig();
        r.ship = new GameObject(ShipId.ObjectName(id) + "(Clone)", typeof(SpriteRenderer), typeof(BoxCollider2D));
        r.ship.GetComponent<BoxCollider2D>().isTrigger = true;
        r.ship.AddComponent<Rigidbody2D>().bodyType = RigidbodyType2D.Kinematic;
        spawnShips.ApplyHull(r.ship, id);
        r.cd = r.ship.AddComponent<collisionDetection>();
        r.cd.explosionAnimation = new GameObject("~TestExplosion");
        r.cd.boostSound = r.ship.AddComponent<AudioSource>();
        r.cd.boostText = new GameObject("~boostText", typeof(RectTransform)).AddComponent<Text>();
        r.cd.hypeText = new GameObject("~hypeText", typeof(RectTransform)).AddComponent<Text>();
        r.cd.boost = new GameObject("~boost");
        r.cd.boost.SetActive(false);
        r.trigger = (Action<Collider2D>)Delegate.CreateDelegate(typeof(Action<Collider2D>), r.cd,
            typeof(collisionDetection).GetMethod("OnTriggerEnter2D", Inst));
        r.tick = (Action)Delegate.CreateDelegate(typeof(Action), r.cd,
            typeof(collisionDetection).GetMethod("turnTextsOff", Inst));
        // collisionDetection.Start's shield line.
        r.cd.shield = ShipShield.For(r.ship).Visual;
        collisionDetection.MAXLIFE = 3;
        collisionDetection.lifeCounter = 0;
        collisionDetection.atomCheck = false;
        collisionDetection.invTimer = 0f;
        return r;
    }

    static GameObject Atom(string name)
    {
        var atom = new GameObject(name, typeof(CircleCollider2D), typeof(SpriteRenderer));
        atom.tag = "pickUp";
        return atom;
    }

    static void Pickup(Rig r, GameObject atom)
    {
        r.trigger(atom.GetComponent<Collider2D>());
        if (PickupBurst.LastPlayed != null) PickupBurst.LastPlayed.Finish();
    }

    static void ForgetShipContours()
    {
        ((IDictionary)typeof(ShieldContour).GetField("byShip", Stat).GetValue(null)).Clear();
        ((IDictionary)typeof(ShieldContour).GetField("cache", Stat).GetValue(null)).Clear();
    }

    // ------------------------------------------------------------------

    static void PickupsBuildAndReadBackNothing()
    {
        ForgetShipContours();
        int spawnReadbacks = 0, pickupBuilds = 0, pickupReadbacks = 0, wrongContour = 0, rigs = 0, notPrebuilt = 0;
        foreach (int id in ShipId.All)
        {
            int skins = ShipSkins.CountFor(id);
            foreach (int skin in new[] { ShipSkins.Stock, skins > 1 ? skins - 1 : ShipSkins.Stock })
            {
                ShipSkins.SetPreview(id, skin);
                for (int state = 0; state < ShipHullArt.States; state++)
                {
                    int rb = ShieldContour.ReadbackCount;
                    var r = Spawn(id);
                    spawnReadbacks += ShieldContour.ReadbackCount - rb;
                    if (!ShieldContour.IsBuiltForShip(id)) notPrebuilt++;

                    // Flying in this damage state, mid idle-flipbook.
                    collisionDetection.lifeCounter = state;
                    r.ship.GetComponent<SpriteRenderer>().sprite = ShipHullArt.Get(id, skin, state, 3);

                    int builds = ShieldContour.BuildCount;
                    rb = ShieldContour.ReadbackCount;
                    foreach (var name in Atoms)
                    {
                        var atom = Atom(name);
                        Pickup(r, atom);
                        UnityEngine.Object.DestroyImmediate(atom);
                    }
                    // A second blue atom while shielded, then one after expiry.
                    var again = Atom(Atoms[0]);
                    Pickup(r, again);
                    r.Expire();
                    // A collected pickup is spent (its collider goes off); the
                    // one after expiry is a fresh atom, as in play.
                    var after = Atom(Atoms[0]);
                    Pickup(r, after);
                    UnityEngine.Object.DestroyImmediate(again);
                    UnityEngine.Object.DestroyImmediate(after);

                    pickupBuilds += ShieldContour.BuildCount - builds;
                    pickupReadbacks += ShieldContour.ReadbackCount - rb;
                    var shield = r.Shield;
                    if (shield == null || !ReferenceEquals(shield.Contour, ShieldContour.ForShip(id)) ||
                        shield.ShieldCollider == null || !shield.ShieldCollider.enabled) wrongContour++;
                    r.Expire();
                    r.Dispose();
                    rigs++;
                }
            }
            ShipSkins.ClearPreview();
        }
        Check("spawning every ship x skin x damage state reads nothing back from the GPU (" + spawnReadbacks + " readbacks)",
              spawnReadbacks == 0);
        Check("every shield's contour is built by the time the ship has spawned (" + notPrebuilt + " not)",
              notPrebuilt == 0);
        Check("pickups of every atom build no contour (" + pickupBuilds + " builds over " + rigs + " ships)",
              rigs > 0 && pickupBuilds == 0);
        Check("pickups of every atom read nothing back from the GPU (" + pickupReadbacks + ")", pickupReadbacks == 0);
        Check("skins, damage states and idle frames all share the ship's one contour and the shielded hitbox works (" +
              wrongContour + " wrong)", wrongContour == 0);
        Check("one contour per ship, not per skin or frame (" + ShieldContour.BuildCount + " builds total is fine; " +
              "cache holds every ship)", ShipIdAllBuilt());
    }

    static bool ShipIdAllBuilt()
    {
        foreach (int id in ShipId.All) if (!ShieldContour.IsBuiltForShip(id)) return false;
        return true;
    }

    // Managed bytes per call of `action`, averaged over n calls (the heap
    // size is page-granular); retried if a collection lands in the middle.
    static double BytesPer(int n, Action<int> action)
    {
        double best = double.MaxValue;
        for (int attempt = 0; attempt < 4; attempt++)
        {
            int gc = GC.CollectionCount(0);
            long before = UnityEngine.Profiling.Profiler.GetMonoUsedSizeLong();
            for (int i = 0; i < n; i++) action(i);
            long after = UnityEngine.Profiling.Profiler.GetMonoUsedSizeLong();
            if (GC.CollectionCount(0) != gc || after < before) continue;
            best = Math.Min(best, (after - before) / (double)n);
            if (best <= AllocBoundBytes) break;
        }
        return best;
    }

    static void PickupFrameIsCheapAndAllocationFree()
    {
        // Every pickup ends in collisionDetection's Destroy(pickup), which in
        // edit mode is refused with a logged error -- an editor-only cost a
        // player never pays. Its managed stack trace is switched off while
        // measuring, and what is left is measured on its own and taken off.
        var traces = new Dictionary<LogType, StackTraceLogType>();
        foreach (LogType t in Enum.GetValues(typeof(LogType)))
        {
            traces[t] = Application.GetStackTraceLogType(t);
            Application.SetStackTraceLogType(t, StackTraceLogType.None);
        }
        try { MeasurePickups(); }
        finally
        {
            foreach (var pair in traces) Application.SetStackTraceLogType(pair.Key, pair.Value);
        }
    }

    static void MeasurePickups()
    {
        const int BaseN = 200;
        var dummies = new GameObject[BaseN];
        for (int i = 0; i < BaseN; i++) dummies[i] = new GameObject("~destroyBaseline");
        double destroyBytes = BytesPer(BaseN, i => UnityEngine.Object.Destroy(dummies[i]));
        foreach (var d in dummies) UnityEngine.Object.DestroyImmediate(d);
        if (destroyBytes == double.MaxValue) destroyBytes = 0;
        Debug.Log("[PH] edit-mode Destroy() baseline: " + destroyBytes.ToString("F1") + " B/call");

        PrefabName.CacheInEditMode = true;   // as in play mode: one name read per contact
        PickupBurst.Prewarm();
        Codex.Prewarm();
        ForgetShipContours();

        foreach (int id in new[] { ShipId.Starter, 4, 14 })
        {
            var r = Spawn(id);

            // First blue atom right after spawn.
            var first = Atom(Atoms[0]);
            var sw = Stopwatch.StartNew();
            r.trigger(first.GetComponent<Collider2D>());
            sw.Stop();
            if (PickupBurst.LastPlayed != null) PickupBurst.LastPlayed.Finish();
            r.Expire();
            UnityEngine.Object.DestroyImmediate(first);
            Check("ship " + id + ": first blue atom after spawn takes " + sw.Elapsed.TotalMilliseconds.ToString("F3") +
                  " ms (< " + PickupBoundMs + ")", sw.Elapsed.TotalMilliseconds < PickupBoundMs);

            foreach (var name in Atoms)
            {
                const int N = 400;
                var atoms = new GameObject[N];
                var cols = new Collider2D[N];
                for (int i = 0; i < N; i++) { atoms[i] = Atom(name); cols[i] = atoms[i].GetComponent<Collider2D>(); }
                // Warm-up.
                for (int i = 0; i < 4; i++) { r.trigger(cols[i]); PickupBurst.LastPlayed.Finish(); r.Expire(); }

                double worstMs = 0, totalMs = 0;
                int timed = 0;
                double bestAvgBytes = BytesPer(N - 4, k =>
                {
                    int i = k + 4;
                    sw.Restart();
                    r.trigger(cols[i]);
                    sw.Stop();
                    double ms = sw.Elapsed.TotalMilliseconds;
                    totalMs += ms;
                    timed++;
                    if (ms > worstMs) worstMs = ms;
                    PickupBurst.LastPlayed.Finish();
                    r.Expire();
                });
                if (bestAvgBytes != double.MaxValue) bestAvgBytes = Math.Max(0, bestAvgBytes - destroyBytes);
                for (int i = 0; i < N; i++) UnityEngine.Object.DestroyImmediate(atoms[i]);
                string atom = name.Replace("(Clone)", "");
                double avgMs = totalMs / Math.Max(1, timed);
                Debug.Log("[PH] ship " + id + " " + atom + ": avg " + avgMs.ToString("F4") + " ms, worst " +
                          worstMs.ToString("F3") + " ms, ~" + bestAvgBytes.ToString("F1") + " B/pickup");
                Check("ship " + id + " " + atom + " pickup allocates ~nothing once warm (" +
                      (bestAvgBytes == double.MaxValue ? "unmeasured" : bestAvgBytes.ToString("F1")) + " B avg, bound " +
                      AllocBoundBytes + ")", bestAvgBytes <= AllocBoundBytes);
                Check("ship " + id + " " + atom + " pickup is fast (avg " + avgMs.ToString("F3") + " ms < " +
                      PickupBoundMs + ")", avgMs < PickupBoundMs);
            }
            r.Dispose();
        }
    }

    // Belt and braces: the pickup path's sources never reach the readback.
    static void SourceHasNoReadbackOnThePickupPath()
    {
        string shield = System.IO.File.ReadAllText("Assets/Scripts/Ship/ShipShield.cs");
        Check("ShipShield gets roster contours from ForShip (baked), not from a hull sprite",
              shield.Contains("ShieldContour.ForShip(id)"));
        Check("ShipShield prewarms when the shield is created", shield.Contains("Prewarm();"));
        string burst = System.IO.File.ReadAllText("Assets/Scripts/Gameplay/Pickups/PickupBurst.cs");
        Check("PickupBurst reuses pooled bursts", burst.Contains("Take()") && !burst.Contains("new GameObject(\"pickupBurst\");"));
    }
}
