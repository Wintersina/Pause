using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;
using Random = UnityEngine.Random;

// Feature: "the hazards should come varying sizes, like rocks and stuff
// floating around" (HazardSize; docs/enemy-behaviours.md "Hazard sizes").
//
//   - every rock has a size range; draws stay inside it with the designed
//     shares (small / typical / large) and an area-neutral mean
//   - a seed gives the same sizes (their own stream, seeded off the
//     spawner's without consuming it), the real spawner included
//   - the size is real: transform scale, collider, sprite, footprint and
//     placement envelope, lane extents, hit radius, the brain's lane clamp
//   - the roster's nominal-size and collider checks hold at both ends of
//     every range; the art is never magnified past 1:1 on a phone
//   - a big rock moves weightier (smaller band, slower pace, slower tumble,
//     less tilt), a small one livelier; a shockwave shoves a small rock
//     further; a mine blast reaches a big rock sooner
//   - mines and pilots are never scaled; re-applying a size resets a body
//   - kill value and blast size follow the size, and average out unchanged
//   - big rocks are never routed into a pilot's reserved column
//   - long mixed-size runs: no two bodies overlap, every rock enters its row
//     with a ship-wide gap, no rock's collider crosses into the rails
//   - zero per-frame allocation (profiler GC.Alloc, positive control)
//   - the threat table (EnemyDensityProbe's stepping) with sizes off vs on
public static class HazardSizeTest
{
    static int fails;
    static void Check(string what, bool ok)
    {
        Debug.Log((ok ? "[HAZARD-SIZE] PASS  " : "[HAZARD-SIZE] FAIL  ") + what);
        if (!ok) fails++;
    }

    public static void Run() { TestHarness.Exit(Execute()); }

    const float Dt = 1f / 60f;
    // A phone's pixels per world unit (CameraFit: 1080 px across 2 x 3.72 u).
    const float DevicePixelsPerUnit = 1080f / (2f * 3.72f);
    const int RockCellPixels = 192;

    public static int Execute()
    {
        fails = 0;
        using var sandbox = new TestHarness.Sandbox();
        try
        {
            EditorSceneLoader.Open("gameS1", OpenSceneMode.Single);
            buttonClicks.playerDied = false;
            score.pauseCounter = 0;
            Table();
            Distribution();
            Determinism();
            BodiesScale();
            NominalSizesHoldAtTheEnds();
            MotionWeight();
            OnlyRocksVary();
            ReapplyResets();
            ShockwaveShovesSmallFurther();
            MineBlastReachesBigSooner();
            ScoreAndBlast();
            PilotColumnsStayClear();
            NoAllocation();
            if (TestHarness.Slow("long mixed-size spawner runs (overlap, row gap, rails)")) LongRuns();
            if (TestHarness.Slow("threat table, sizes off vs on")) ThreatTable();
        }
        finally
        {
            HazardSize.Enabled = true;
            EnemyThreat.ForceShooting = false;
            SpawnSpace.ClockOverride = null;
            SetWorldManager(null);
            LoopDifficulty.Reset();
            EnemyDensityProbe.RestoreView();
            EnemyDensityProbe.Clear();
            EnemyShove.Clear();
            moveBackGround.speed = 0f;
        }
        Debug.Log("[HAZARD-SIZE] failures: " + fails);
        return fails;
    }

    // ---- fixtures ----------------------------------------------------------

    static List<EnemyDef> Rocks()
    {
        var list = new List<EnemyDef>();
        foreach (var d in EnemyRoster.All) if (d.role == EnemyRole.Rock) list.Add(d);
        return list;
    }

    static void SetWorldManager(WorldManager wm)
    {
        typeof(WorldManager).GetField("<Instance>k__BackingField", BindingFlags.NonPublic | BindingFlags.Static).SetValue(null, wm);
    }

    static GameObject Build(EnemyDef def, Vector2 at, float size)
    {
        return EnemyFactory.Create(def, new Vector3(at.x, at.y, 0f), Quaternion.identity, size);
    }

    static void ClearAll()
    {
        EnemyDensityProbe.Clear();
        EnemyShove.Clear();
    }

    static bool Near(float a, float b, float eps = 1e-4f) { return Mathf.Abs(a - b) <= eps; }
    static bool Near(Vector2 a, Vector2 b, float eps = 1e-4f) { return Near(a.x, b.x, eps) && Near(a.y, b.y, eps); }
    static bool Near(Rect a, Rect b, float eps = 1e-4f)
    {
        return Near(a.xMin, b.xMin, eps) && Near(a.xMax, b.xMax, eps) && Near(a.yMin, b.yMin, eps) && Near(a.yMax, b.yMax, eps);
    }

    static int Tier(EnemyBehaviour b, float s)
    {
        float j = HazardSize.Jitter;
        float smallTop = (b.sizeSmall * (1f + j) + b.sizeTypical * (1f - j)) * .5f;
        float largeBottom = (b.sizeTypical * (1f + j) + b.sizeLarge * (1f - j)) * .5f;
        return s < smallTop ? 0 : s > largeBottom ? 2 : 1;
    }

    // ---- 1: the table ----------------------------------------------------------

    static void Table()
    {
        var rocks = Rocks();
        Check("all 18 rocks are in the table (" + rocks.Count + ")", rocks.Count == 18);
        Debug.Log("[HAZARD-SIZE] key | small / typical / large tier | range | mean area | reach, pace at the ends");
        foreach (var d in rocks)
        {
            var b = d.Behaviour;
            float lo = HazardSize.Min(b), hi = HazardSize.Max(b), area = HazardSize.MeanArea(b);
            Debug.Log(string.Format("[HAZARD-SIZE] TABLE {0,-20} | {1:F2} / {2:F2} / {3:F2} | {4:F2}-{5:F2} | {6:F3} | reach {7:F2}-{8:F2}, pace {9:F2}-{10:F2}",
                                    d.key, b.sizeSmall, b.sizeTypical, b.sizeLarge, lo, hi, area,
                                    HazardSize.Reach(hi), HazardSize.Reach(lo), HazardSize.Pace(hi), HazardSize.Pace(lo)));
            Check(d.key + " varies, small < typical < large", HazardSize.Varies(d) && b.sizeSmall < b.sizeTypical && b.sizeTypical < b.sizeLarge);
            Check(string.Format("{0} range {1:F2}-{2:F2} stays within 0.70x-1.60x", d.key, lo, hi), lo >= .7f - 1e-4f && hi <= 1.6f + 1e-4f);
            Check(string.Format("{0} mean area {1:F3} is today's (within 3%)", d.key, area), Mathf.Abs(area - 1f) <= .03f);
        }
        bool none = true;
        foreach (var d in EnemyRoster.All)
            if (d.role != EnemyRole.Rock) none &= !HazardSize.Varies(d) && d.Behaviour != null && d.Behaviour.sizeLarge == 0f;
        Check("no mine, fighter, heavy, alien or chaser has a size range", none);
        Check("slim rocks stay slimmer: the shard and the obsidian blade top out below the cluster and the islet",
              HazardSize.Max(EnemyRoster.Find("frost_rock_shard").Behaviour) < HazardSize.Max(EnemyRoster.Find("space_rock_cluster").Behaviour) &&
              HazardSize.Max(EnemyRoster.Find("ember_rock_obsidian").Behaviour) < HazardSize.Max(EnemyRoster.Find("ember_rock_islet").Behaviour));
        Check("weighted toward typical, small more often than large (shares " + HazardSize.SmallShare + " / " +
              (1f - HazardSize.SmallShare - HazardSize.LargeShare) + " / " + HazardSize.LargeShare + ")",
              1f - HazardSize.SmallShare - HazardSize.LargeShare > .5f && HazardSize.SmallShare > HazardSize.LargeShare && HazardSize.LargeShare > 0f);
    }

    // ---- 2: the draw -----------------------------------------------------------

    static void Distribution()
    {
        const int N = 6000;
        foreach (var d in Rocks())
        {
            var b = d.Behaviour;
            HazardSize.Seed((uint)(7000 + d.key.Length * 31 + d.world));
            float lo = HazardSize.Min(b), hi = HazardSize.Max(b);
            bool inside = true;
            int[] tiers = new int[3];
            double area = 0;
            float seenLo = 9f, seenHi = 0f;
            for (int i = 0; i < N; i++)
            {
                float s = HazardSize.Draw(d);
                inside &= s >= lo - 1e-4f && s <= hi + 1e-4f;
                tiers[Tier(b, s)]++;
                area += s * s;
                seenLo = Mathf.Min(seenLo, s); seenHi = Mathf.Max(seenHi, s);
            }
            float small = tiers[0] / (float)N, large = tiers[2] / (float)N;
            float mean = (float)(area / N);
            Check(string.Format("{0}: {1} draws inside {2:F3}-{3:F3} (seen {4:F3}-{5:F3}); small {6:P1}, large {7:P1}, mean area {8:F3}",
                                d.key, N, lo, hi, seenLo, seenHi, small, large, mean),
                  inside && Mathf.Abs(small - HazardSize.SmallShare) <= .025f && Mathf.Abs(large - HazardSize.LargeShare) <= .025f &&
                  Mathf.Abs(mean - HazardSize.MeanArea(b)) <= .02f && seenHi - seenLo > (hi - lo) * .9f);
        }
    }

    static void Determinism()
    {
        var def = EnemyRoster.Find("space_rock_cluster");
        var a = new float[64];
        var b = new float[64];
        Random.InitState(424242);
        var seeded = Random.state;
        HazardSize.Seed();
        for (int i = 0; i < a.Length; i++) a[i] = HazardSize.Draw(def);
        bool untouched = Random.state.Equals(seeded);
        Random.InitState(424242);
        HazardSize.Seed();
        for (int i = 0; i < b.Length; i++) b[i] = HazardSize.Draw(def);
        bool same = true, varied = false;
        for (int i = 0; i < a.Length; i++) { same &= a[i] == b[i]; varied |= a[i] != a[0]; }
        Random.InitState(424243);
        HazardSize.Seed();
        bool other = false;
        for (int i = 0; i < a.Length; i++) other |= HazardSize.Draw(def) != a[i];
        Check("the same seed draws the same sizes, another seed others", same && varied && other);
        Check("seeding and drawing sizes never touch the spawner's own random stream (it is consumed exactly as before sizes)", untouched);

        HazardSize.Enabled = false;
        bool ones = true;
        for (int i = 0; i < a.Length; i++) ones &= HazardSize.Draw(def) == 1f;
        HazardSize.Enabled = true;
        Check("switched off every rock is nominal", ones);

        Random.InitState(99);
        var before = Random.state;
        HazardSize.Draw(EnemyRoster.Fighter(0, 2));
        HazardSize.Draw(EnemyRoster.One(0, EnemyRole.Mine));
        Check("a pilot's or a mine's spawn draws nothing", Random.state.Equals(before));

        // the real spawner, twice from one seed
        var first = SpawnerSizes(31337, 40f);
        var second = SpawnerSizes(31337, 40f);
        bool equal = first.Count == second.Count && first.Count > 20;
        for (int i = 0; equal && i < first.Count; i++) equal &= first[i] == second[i];
        Check("the real spawner draws the same sizes from the same seed (" + first.Count + " rocks over 40 s)", equal);
    }

    // The sizes of the rocks a seeded spawner fields over `seconds` at HUD 20, in order.
    static List<float> SpawnerSizes(int seed, float seconds)
    {
        ClearAll();
        EnemyDensityProbe.chasers.Clear();
        LoopDifficulty.Reset();
        Random.InitState(seed);
        moveBackGround.speed = .2f;
        var board = EnemyDensityProbe.NewBoard();
        var ship = new GameObject("~SizeShip").transform;
        var seen = new HashSet<EnemyIdentity>();
        var sizes = new List<float>();
        for (float t = 0f; t < seconds; t += Dt)
        {
            EnemyDensityProbe.Elapsed.SetValue(board, 63f);
            EnemyDensityProbe.StepBoard(board, ship, t, 6f);
            var live = SpawnSpace.Live(SpawnLayer.Enemy);
            for (int i = 0; i < live.Count; i++)
            {
                EnemyIdentity id;
                if (live[i] != null && live[i].TryGetComponent(out id) && id.Def != null && id.Def.role == EnemyRole.Rock && seen.Add(id))
                    sizes.Add(id.Scale);
            }
        }
        Object.DestroyImmediate(ship.gameObject);
        ClearAll();
        SpawnSpace.ClockOverride = null;
        return sizes;
    }

    // ---- 3: the size is real -----------------------------------------------------

    static void BodiesScale()
    {
        ClearAll();
        bool all = true;
        string first = null;
        foreach (var d in Rocks())
        {
            var b = d.Behaviour;
            foreach (float s in new[] { HazardSize.Min(b), b.sizeTypical, HazardSize.Max(b) })
            {
                var go = Build(d, new Vector2(.3f, 2f), s);
                var id = go.GetComponent<EnemyIdentity>();
                var box = go.GetComponent<BoxCollider2D>();
                var sr = go.GetComponent<SpriteRenderer>();
                var print = go.GetComponent<SpawnFootprint>();
                var brain = go.GetComponent<EnemyBrain>();
                var target = go.GetComponent<ClearTarget>();
                Vector2 colWorld = Vector2.Scale(box.size, (Vector2)go.transform.lossyScale);
                Vector2 half = SpawnSpace.BodyHalf(d, s);
                Rect sweep = print.Sweep(0f, SpawnSpace.Lifetime);
                Rect want = EnemyBrain.Envelope(b, go.transform.position, half, 1f, HazardSize.Reach(s));
                var spans = SpawnLane.RowSpans(1.9f, 2.1f);
                bool span = spans.Count == 1 && Near(spans[0].y - spans[0].x, 2f * SpawnLane.HalfExtents(d, s).x);
                bool ok = Near(go.transform.localScale.x, s) && Near(id.Scale, s) &&
                          Near(colWorld, d.ColliderSize * s) &&
                          Near(sr.bounds.size.x, d.FrameWorldSize * s, .002f) &&
                          Near(print.half, half) && Near(half, SpawnSpace.BodyHalf(d) * s) && Near(SpawnSpace.BodyHalf(go), half) &&
                          Near(target.Radius, HazardSize.RadiusFor(d, s)) && Near(target.Radius, d.ColliderSize.x * .5f * s) &&
                          brain != null && Near(brain.Size, s) && Near(brain.Reach, HazardSize.Reach(s)) &&
                          Near(sweep, want) && span;
                if (!ok && first == null)
                    first = string.Format("{0} at {1:F2}: scale {2:F3}, collider {3}, sprite {4:F3}, half {5} vs {6}, radius {7:F3}, sweep {8} vs {9}, spans {10}",
                                          d.key, s, go.transform.localScale.x, colWorld, sr.bounds.size.x, print.half, half, target.Radius, sweep, want, spans.Count);
                all &= ok;
                Object.DestroyImmediate(go);
            }
        }
        Check("at its smallest, typical and largest every rock's scale, collider, sprite, footprint, placement envelope, lane extents and hit radius follow its size" +
              (first != null ? " (first miss: " + first + ")" : ""), all);
        ClearAll();
    }

    // EnemyRosterTest's nominal checks, at both ends of each range: the drawn
    // silhouette is within 15% of the role's size times the scale, the
    // collider covers the same share of the drawing as at nominal size, and
    // on a phone the art is never magnified (>= 1 texel per screen pixel).
    static void NominalSizesHoldAtTheEnds()
    {
        foreach (var d in Rocks())
        {
            var b = d.Behaviour;
            float edge = SilhouetteEdge(d);
            float nominalDrawn = edge * d.FrameWorldSize;
            float coverage = d.ColliderSize.x / Mathf.Max(.01f, nominalDrawn);
            bool ok = edge > 0f;
            var notes = new List<string>();
            foreach (float s in new[] { HazardSize.Min(b), HazardSize.Max(b) })
            {
                var go = Build(d, Vector2.zero, s);
                float drawn = edge * go.GetComponent<SpriteRenderer>().bounds.size.x;
                float col = go.GetComponent<BoxCollider2D>().size.x * go.transform.lossyScale.x;
                float target = EnemyRoster.TargetWidth(d.role) * s;
                float texels = RockCellPixels / (d.FrameWorldSize * s * DevicePixelsPerUnit);
                ok &= Mathf.Abs(drawn - target) <= target * .15f + 1e-4f;
                ok &= Mathf.Abs(col / drawn - coverage) <= .005f;
                ok &= texels >= 1f && texels <= 3f;
                notes.Add(string.Format("{0:F2}x: drawn {1:F2} u vs {2:F2} u, collider {3:P0} of it, {4:F2} texels/px", s, drawn, target, col / drawn, texels));
                Object.DestroyImmediate(go);
            }
            Check(d.key + " holds the nominal checks at both ends (" + string.Join("; ", notes) + ")", ok);
        }
        ClearAll();
    }

    static float SilhouetteEdge(EnemyDef d)
    {
        string path = "Assets/Art/Resources/" + d.StripPath + ".png";
        if (!File.Exists(path)) return 0f;
        var tex = new Texture2D(2, 2);
        tex.LoadImage(File.ReadAllBytes(path));
        int h = tex.height;
        var px = tex.GetPixels32();
        int minX = h, maxX = -1, minY = h, maxY = -1;
        for (int y = 0; y < h; y++)
            for (int x = 0; x < h; x++)
                if (px[y * tex.width + x].a > 128)
                {
                    if (x < minX) minX = x;
                    if (x > maxX) maxX = x;
                    if (y < minY) minY = y;
                    if (y > maxY) maxY = y;
                }
        Object.DestroyImmediate(tex);
        if (maxX < 0) return 0f;
        return Mathf.Max(maxX - minX + 1, maxY - minY + 1) / (float)h;
    }

    // ---- 4: weight -----------------------------------------------------------------

    static void MotionWeight()
    {
        bool monotone = Near(HazardSize.Reach(1f), 1f) && Near(HazardSize.Pace(1f), 1f);
        for (float s = .7f; s < 1.6f; s += .05f)
            monotone &= HazardSize.Reach(s + .05f) <= HazardSize.Reach(s) + 1e-6f && HazardSize.Pace(s + .05f) <= HazardSize.Pace(s) + 1e-6f &&
                        HazardSize.ShoveScale(s + .05f) <= HazardSize.ShoveScale(s) + 1e-6f;
        Check("reach, pace and shove fall with size (1 at nominal)", monotone);

        foreach (var key in new[] { "space_rock_cluster", "verdant_rock_knot", "ember_rock_islet", "frost_rock_chunk", "frost_rock_rime", "ember_rock_cinder" })
        {
            ClearAll();
            var d = EnemyRoster.Find(key);
            var b = d.Behaviour;
            float lo = HazardSize.Min(b), hi = HazardSize.Max(b);
            Measure(d, lo, out float bandSmall, out float paceSmall, out float riseSmall);
            Measure(d, hi, out float bandBig, out float paceBig, out float riseBig);
            var small = Build(d, new Vector2(-1f, 0f), lo);
            var big = Build(d, new Vector2(1f, 0f), hi);
            var spinS = small.GetComponent<AsteroidSpin>();
            var spinB = big.GetComponent<AsteroidSpin>();
            bool spin = spinB.speedRange.y < spinS.speedRange.y && spinB.speedRange.x <= spinS.speedRange.x;
            bool tilt = !d.floating || (spinB.swayDegrees < spinS.swayDegrees && spinS.swayDegrees <= HazardSize.MaxTilt + 1e-4f &&
                                        spinB.swayPeriod > spinS.swayPeriod);
            float lpS = small.GetComponent<EnemyBrain>().LateralPace, lpB = big.GetComponent<EnemyBrain>().LateralPace;
            bool lateral = b.lateral == EnemyLateral.None || (bandBig < bandSmall && paceBig < paceSmall && lpB < lpS);
            bool vertical = b.vertical == EnemyVertical.None || riseBig < riseSmall;
            Check(string.Format("{0}: the big one ({1:F2}x) is weightier than the small one ({2:F2}x) -- band {3:F2} vs {4:F2} u, sideways {5:F2} vs {6:F2} u/s, " +
                                "vertical {7:F2} vs {8:F2} u, tumble up to {9:F0} vs {10:F0} deg/s{11}",
                                key, hi, lo, bandBig, bandSmall, paceBig, paceSmall, riseBig, riseSmall, spinB.speedRange.y, spinS.speedRange.y,
                                d.floating ? string.Format(", tilt {0:F1} vs {1:F1} deg", spinB.swayDegrees, spinS.swayDegrees) : ""),
                  lateral && vertical && spin && tilt);
        }
        ClearAll();
    }

    // One rock of `size` flown 30 s by its brain: its widest sideways reach,
    // its mean sideways speed, its vertical travel.
    static void Measure(EnemyDef d, float size, out float band, out float speed, out float vertical)
    {
        var go = Build(d, new Vector2(0f, 0f), size);
        var brain = go.GetComponent<EnemyBrain>();
        band = 0f; speed = 0f;
        float lo = 0f, hi = 0f, prevX = 0f;
        const int frames = 60 * 30;
        for (int i = 0; i < frames; i++)
        {
            brain.Step(Dt);
            Vector2 o = brain.Offset;
            band = Mathf.Max(band, Mathf.Abs(o.x));
            speed += Mathf.Abs(o.x - prevX);
            prevX = o.x;
            lo = Mathf.Min(lo, o.y); hi = Mathf.Max(hi, o.y);
        }
        speed /= frames * Dt;
        vertical = hi - lo;
        Object.DestroyImmediate(go);
    }

    // ---- 5: only rocks; a reused body resets --------------------------------------

    static void OnlyRocksVary()
    {
        ClearAll();
        bool none = true;
        string first = null;
        foreach (var d in EnemyRoster.All)
        {
            if (d.role == EnemyRole.Rock) continue;
            var go = Build(d, new Vector2(0f, 3f), 1.5f);
            var print = go.GetComponent<SpawnFootprint>();
            bool ok = go.transform.localScale == Vector3.one && EnemyIdentity.ScaleOf(go) == 1f &&
                      (print == null || Near(print.half, SpawnSpace.BodyHalf(d)));
            var brain = go.GetComponent<EnemyBrain>();
            if (brain != null) ok &= brain.Size == 1f && brain.Reach == 1f && brain.Pace == 1f;
            if (!ok && first == null) first = d.key;
            none &= ok;
            Object.DestroyImmediate(go);
            PilotAirspace.Clear();
        }
        Check("asked for 1.5x, no mine, fighter, heavy, alien or chaser is scaled" + (first != null ? " (" + first + " was)" : ""), none);
        ClearAll();
    }

    static void ReapplyResets()
    {
        ClearAll();
        // Roster enemies are not pooled (EnemyFactory builds each one); a size
        // is still applied absolutely, so a body given a new size -- or put
        // back to 1 -- is exactly a fresh one of that size.
        bool all = true;
        foreach (var key in new[] { "space_rock_cluster", "verdant_rock_vine", "ember_rock_cinder" })
        {
            var d = EnemyRoster.Find(key);
            var b = d.Behaviour;
            float lo = HazardSize.Min(b), hi = HazardSize.Max(b);
            foreach (float to in new[] { lo, 1f })
            {
                var reused = Build(d, new Vector2(-1f, 0f), hi);
                HazardSize.Apply(reused, d, to);
                var fresh = Build(d, new Vector2(1f, 0f), to);
                var a = reused.GetComponent<AsteroidSpin>();
                var f = fresh.GetComponent<AsteroidSpin>();
                var ba = reused.GetComponent<EnemyBrain>();
                var bf = fresh.GetComponent<EnemyBrain>();
                all &= reused.transform.localScale == fresh.transform.localScale &&
                       EnemyIdentity.ScaleOf(reused) == EnemyIdentity.ScaleOf(fresh) &&
                       Near(reused.GetComponent<ClearTarget>().Radius, fresh.GetComponent<ClearTarget>().Radius) &&
                       Near(reused.GetComponent<SpawnFootprint>().half, fresh.GetComponent<SpawnFootprint>().half) &&
                       Near(a.speedRange, f.speedRange) && Near(a.swayDegrees, f.swayDegrees) && Near(a.swayPeriod, f.swayPeriod) &&
                       ba.Size == bf.Size && ba.Reach == bf.Reach && ba.Pace == bf.Pace;
                Object.DestroyImmediate(reused);
                Object.DestroyImmediate(fresh);
            }
        }
        Check("a body given a new size (or put back to nominal) is exactly a fresh one of that size: scale, radius, footprint, tumble, tilt, brain", all);
        ClearAll();
    }

    // ---- 6: what reads a radius ------------------------------------------------------

    static void ShockwaveShovesSmallFurther()
    {
        ClearAll();
        var d = EnemyRoster.Find("space_rock_cluster");
        float lo = HazardSize.Min(d.Behaviour), hi = HazardSize.Max(d.Behaviour);
        // the same gap from the ship to each body's edge, left and right
        Vector2 ship = new Vector2(0f, -2f);
        float gap = .5f;
        var small = Build(d, new Vector2(-(gap + SpawnSpace.BodyHalf(d, lo).x), ship.y - .01f), lo);
        var big = Build(d, new Vector2(gap + SpawnSpace.BodyHalf(d, hi).x, ship.y - .01f), hi);
        Vector3 s0 = small.transform.position, b0 = big.transform.position;
        ShieldShockwave.Release(ship, .3f);
        for (int i = 0; i < 40; i++) EnemyShove.Step(Dt);
        float ds = Vector3.Distance(small.transform.position, s0), db = Vector3.Distance(big.transform.position, b0);
        Check(string.Format("a shockwave shoves a small rock further than a big one at the same gap ({0:F2} u vs {1:F2} u; x{2:F2} vs x{3:F2})",
                            ds, db, HazardSize.ShoveScale(lo), HazardSize.ShoveScale(hi)),
              ds > db * 1.5f && db > .05f);
        ClearAll();
    }

    static void MineBlastReachesBigSooner()
    {
        ClearAll();
        FriendlyFire.ResetCounters();
        var d = EnemyRoster.Find("space_rock_dark");
        float lo = HazardSize.Min(d.Behaviour), hi = HazardSize.Max(d.Behaviour);
        float reachSmall = FriendlyFire.MineBlastRadius + HazardSize.RadiusFor(d, lo) * .5f;
        float reachBig = FriendlyFire.MineBlastRadius + HazardSize.RadiusFor(d, hi) * .5f;
        float at = (reachSmall + reachBig) * .5f;
        var mine = EnemyFactory.Create(EnemyRoster.One(0, EnemyRole.Mine), new Vector3(0f, 0f, 0f), Quaternion.identity);
        var small = Build(d, new Vector2(-at, 0f), lo);
        var big = Build(d, new Vector2(at, 0f), hi);
        EliteShip.FriendlyKill(mine);
        var rt = HazardRuntime.Ensure();
        for (int i = 0; i < 30; i++) rt.Step(Dt);
        Check(string.Format("a mine blast {0:F2} u away takes the big rock (reach {1:F2}) and spares the small one (reach {2:F2})", at, reachBig, reachSmall),
              big == null && small != null);
        if (small != null) Object.DestroyImmediate(small);
        if (big != null) Object.DestroyImmediate(big);
        if (HazardRuntime.Instance != null) Object.DestroyImmediate(HazardRuntime.Instance.gameObject);
        ClearAll();
    }

    static void ScoreAndBlast()
    {
        ClearAll();
        var d = EnemyRoster.Find("verdant_rock_pod");
        float lo = HazardSize.Min(d.Behaviour), hi = HazardSize.Max(d.Behaviour);
        var small = Build(d, new Vector2(-1f, 0f), lo);
        var mid = Build(d, new Vector2(0f, 0f), 1f);
        var big = Build(d, new Vector2(1f, 0f), hi);
        int ps = RunScore.BasePoints(small), pm = RunScore.BasePoints(mid), pb = RunScore.BasePoints(big);
        Check(string.Format("a rock pays by its size: {0} small, {1} nominal, {2} large (ScoreRules.Rock {3})", ps, pm, pb, ScoreRules.Rock),
              ps == ScoreRules.Rock - 1 && pm == ScoreRules.Rock && pb > ScoreRules.Rock && pb <= ScoreRules.Rock + 2);
        Check("... and blasts at its size (small / medium / large explosion, so a big one splits more often)",
              TargetExplosion.SizeFor(small) == TargetExplosion.Size.Small && TargetExplosion.SizeFor(mid) == TargetExplosion.Size.Medium &&
              TargetExplosion.SizeFor(big) == TargetExplosion.Size.Large);
        // over the distribution, a rock pays what it always did on average
        double paid = 0;
        int n = 0;
        foreach (var r in Rocks())
        {
            HazardSize.Seed((uint)(55 + n));
            for (int i = 0; i < 2000; i++, n++) paid += HazardSize.RockPoints(HazardSize.Draw(r));
        }
        float mean = (float)(paid / n);
        Check(string.Format("averaged over every rock's draws a kill pays {0:F2} (ScoreRules.Rock {1}, within 5%)", mean, ScoreRules.Rock),
              Mathf.Abs(mean - ScoreRules.Rock) <= ScoreRules.Rock * .05f);
        var mine = EnemyFactory.Create(EnemyRoster.One(0, EnemyRole.Mine), new Vector3(0f, 3f, 0f), Quaternion.identity);
        Check("a mine still pays a mine", RunScore.BasePoints(mine) == ScoreRules.Mine);
        Object.DestroyImmediate(small); Object.DestroyImmediate(mid); Object.DestroyImmediate(big); Object.DestroyImmediate(mine);
        ClearAll();
    }

    // A pilot holding the middle of the lane: rocks drawn large (the share
    // forced to 1) are placed only beside its column.
    static void PilotColumnsStayClear()
    {
        ClearAll();
        float small = HazardSize.SmallShare, large = HazardSize.LargeShare;
        var trySpawn = typeof(enmiesOnBoard).GetMethod("TrySpawnDef", BindingFlags.NonPublic | BindingFlags.Instance);
        try
        {
            HazardSize.SmallShare = 0f;
            HazardSize.LargeShare = 1f;
            moveBackGround.speed = .2f;
            var board = EnemyDensityProbe.NewBoard();
            var pilot = EnemyFactory.Create(EnemyRoster.Fighter(0, 3), new Vector3(0f, CameraFit.ViewTop - 2f, 0f), Quaternion.identity);
            var pb = pilot.GetComponent<EnemyBrain>();
            Check("a pilot holds a reserved column (" + PilotAirspace.Count + ")", pb != null && pb.IsPilot && PilotAirspace.Count == 1);
            int placed = 0, crossing = 0, big = 0;
            var rocks = EnemyRoster.For(0, EnemyRole.Rock);
            Random.InitState(808);
            for (int i = 0; i < 60; i++)
            {
                var def = rocks[i % rocks.Count];
                int before = board.SpawnedCount;
                bool done = (bool)trySpawn.Invoke(board, new object[] { def, float.NaN });
                if (!done || board.SpawnedCount == before) continue;
                // the newest rock
                EnemyIdentity newest = null;
                foreach (var id in Object.FindObjectsByType<EnemyIdentity>(FindObjectsSortMode.None))
                    if (id.Def == def && id.gameObject != pilot) newest = id;
                if (newest == null) continue;
                placed++;
                if (newest.Scale > 1.2f) big++;
                Rect r = newest.GetComponent<SpawnFootprint>().Sweep(0f, SpawnSpace.Lifetime);
                if (PilotAirspace.Blocks(r.xMin, r.xMax)) crossing++;
                Object.DestroyImmediate(newest.gameObject);
            }
            Check(string.Format("large rocks are routed round a pilot's column: {0} placed ({1} large), {2} with a sweep across it", placed, big, crossing),
                  placed >= 30 && big == placed && crossing == 0);
        }
        finally
        {
            HazardSize.SmallShare = small;
            HazardSize.LargeShare = large;
            ClearAll();
        }
    }

    // ---- 7: allocation ---------------------------------------------------------------

    static void NoAllocation()
    {
        ClearAll();
        bool meter = TestHarness.AllocMeterWorks(out long control);
        Check("the allocation meter sees a deliberate allocation (" + control + " bytes)", meter);
        var rocks = Rocks();
        var bodies = new List<GameObject>();
        var brains = new List<EnemyBrain>();
        var spins = new List<AsteroidSpin>();
        var movers = new List<moveEnimes>();
        var stand = new GameObject("~SizeAllocShip").transform;
        stand.position = new Vector3(0f, -4f, 0f);
        Random.InitState(4);
        for (int i = 0; i < 28; i++)
        {
            var d = rocks[i % rocks.Count];
            var go = Build(d, new Vector2(-2f + (i % 7) * .66f, -6f + (i / 7) * 3f), HazardSize.Draw(d));
            bodies.Add(go);
            brains.Add(go.GetComponent<EnemyBrain>());
            brains[i].TargetOverride = stand;
            spins.Add(go.GetComponent<AsteroidSpin>());
            movers.Add(go.GetComponent<moveEnimes>());
        }
        var def = rocks[0];
        float clock = 0f;
        var plan = new EnemyBrainPlan { behaviour = def.Behaviour, reach = HazardSize.Reach(1.4f) };
        Action bodiesStep = () =>
        {
            for (int k = 0; k < 60; k++)
            {
                clock += Dt;
                for (int i = 0; i < bodies.Count; i++)
                {
                    movers[i].Step(Dt, clock);
                    brains[i].Step(Dt);
                    spins[i].Advance(Dt);
                    paceSink += brains[i].LateralPace;
                }
            }
        };
        Action placement = () =>
        {
            for (int k = 0; k < 60; k++)
            {
                SpawnLane.Fits(def, .5f, 3f, 1.4f);
                SpawnSpace.Fits(new SpawnCandidate(new Vector2(.5f, 3f), SpawnSpace.BodyHalf(def, 1.4f), plan));
                HazardSize.Draw(def);
                paceSink += HazardSize.Reach(1.4f) + HazardSize.Pace(1.4f) + HazardSize.ShoveScale(1.4f);
            }
        };
        bodiesStep();
        placement();
        long bytes = TestHarness.AllocatedBytes(bodiesStep);
        Check("60 frames of 28 mixed-size rocks (mover, brain pattern, tumble) allocate nothing (" + bytes + " bytes)", meter && bytes == 0);
        bytes = TestHarness.AllocatedBytes(placement);
        bytes = TestHarness.AllocatedBytes(placement);   // (the first measured call may still JIT)
        long lane = TestHarness.AllocatedBytes(() => { for (int k = 0; k < 60; k++) SpawnLane.Fits(def, .5f, 3f, 1.4f); });
        long space = TestHarness.AllocatedBytes(() => { for (int k = 0; k < 60; k++) SpawnSpace.Fits(new SpawnCandidate(new Vector2(.5f, 3f), SpawnSpace.BodyHalf(def, 1.4f), plan)); });
        long draw = TestHarness.AllocatedBytes(() => { for (int k = 0; k < 60; k++) HazardSize.Draw(def); });
        Vector2 h14 = SpawnSpace.BodyHalf(def, 1.4f);
        long bh = TestHarness.AllocatedBytes(() => { for (int k = 0; k < 60; k++) paceSink += SpawnSpace.BodyHalf(def, 1.4f).x; });
        long noPlan = TestHarness.AllocatedBytes(() => { for (int k = 0; k < 60; k++) SpawnSpace.Fits(new SpawnCandidate(new Vector2(.5f, 3f), h14)); });
        long withPlan = TestHarness.AllocatedBytes(() => { for (int k = 0; k < 60; k++) SpawnSpace.Fits(new SpawnCandidate(new Vector2(.5f, 3f), h14, plan)); });
        long env = TestHarness.AllocatedBytes(() => { for (int k = 0; k < 60; k++) paceSink += plan.SweptBounds(new Vector2(.5f, 3f), h14, 0f, 1f).x; });
        Check("... and so do their parts: body size " + bh + ", footprint fit " + noPlan + " / with its envelope " + withPlan + ", envelope " + env + " bytes",
              meter && bh == 0 && noPlan == 0 && withPlan == 0 && env == 0);
        Check("the sized placement checks (row gap, footprint, the draw) allocate nothing (" + bytes + " bytes; alone: lane " + lane +
              ", footprint " + space + ", draw " + draw + ")", meter && bytes == 0);
        foreach (var go in bodies) Object.DestroyImmediate(go);
        Object.DestroyImmediate(stand.gameObject);
        ClearAll();
    }

    static float paceSink;

    // ---- 8: long runs ------------------------------------------------------------------

    static void LongRuns()
    {
        ClearAll();
        var wmGo = new GameObject("~SizeRunWorlds");
        SetWorldManager(wmGo.AddComponent<WorldManager>());
        float[] speeds = { 6f, 12f };
        float[] densities = { 1f, 1.3f * 1.6f };
        const float runSeconds = 90f;
        int overlapFrames = 0, frames = 0, rowsChecked = 0, rowsShort = 0, railFrames = 0;
        string firstOverlap = null, firstRow = null, firstRail = null;
        var tiers = new int[3];
        double area = 0;
        int rocks = 0;
        float biggest = 0f, smallest = 9f, worstGap = 99f;
        var target = new GameObject("~SizeRunShip").transform;
        var seen = new HashSet<SpawnFootprint>();
        var fresh = new List<SpawnFootprint>();
        try
        {
            for (int w = 0; w < WorldManager.LiveWorldCount; w++)
                for (int si = 0; si < speeds.Length; si++)
                    for (int di = 0; di < densities.Length; di++)
                    {
                        ClearAll();
                        EnemyDensityProbe.chasers.Clear();
                        seen.Clear();
                        PlayerPrefs.SetInt(WorldManager.PrefsCurrentWorld, w);
                        LoopDifficulty.DensityScale = densities[di];
                        Random.InitState(9900 + w * 31 + si * 7 + di);
                        float v = speeds[si];
                        moveBackGround.speed = v / 30f;
                        var board = EnemyDensityProbe.NewBoard();
                        for (float t = 0f; t < runSeconds; t += Dt)
                        {
                            frames++;
                            EnemyDensityProbe.Elapsed.SetValue(board, Mathf.Min(119f, 30f + t));
                            EnemyDensityProbe.StepBoard(board, target, t, v);

                            var live = SpawnSpace.Live(SpawnLayer.Enemy);
                            fresh.Clear();
                            for (int i = 0; i < live.Count; i++)
                                if (live[i] != null && seen.Add(live[i])) fresh.Add(live[i]);
                            // every rock entered its row with a ship-wide gap left
                            foreach (var f in fresh)
                            {
                                EnemyIdentity id;
                                if (!f.TryGetComponent(out id) || id.Def == null || id.Def.role != EnemyRole.Rock) continue;
                                rocks++;
                                tiers[Tier(id.Def.Behaviour, id.Scale)]++;
                                area += id.Scale * id.Scale;
                                biggest = Mathf.Max(biggest, id.Scale);
                                smallest = Mathf.Min(smallest, id.Scale);
                                Vector2 half = SpawnLane.HalfExtents(id.Def, id.Scale);
                                float y = f.transform.position.y, band = half.y + SpawnLane.ShipGap;
                                var spans = SpawnLane.RowSpans(y - band, y + band);
                                float gap = SpawnLane.WidestGap(spans);
                                rowsChecked++;
                                worstGap = Mathf.Min(worstGap, gap);
                                if (gap < SpawnLane.GuaranteedGap - .03f)
                                {
                                    rowsShort++;
                                    if (firstRow == null) firstRow = string.Format("world {0} t={1:F1}: {2} at {3:F2}x, widest gap {4:F2}", w, t, id.Def.key, id.Scale, gap);
                                }
                            }
                            // no rock's collider crosses the lane edge into the rails
                            for (int i = 0; i < live.Count; i++)
                            {
                                EnemyIdentity id;
                                if (live[i] == null || !live[i].TryGetComponent(out id) || id.Def == null || id.Def.role != EnemyRole.Rock) continue;
                                float edge = Mathf.Abs(live[i].transform.position.x) + id.Def.ColliderSize.x * .5f * id.Scale;
                                if (edge > SpawnLane.LaneHalf + .005f)
                                {
                                    railFrames++;
                                    if (firstRail == null) firstRail = string.Format("{0} at {1:F2}x reaches {2:F3}", id.Def.key, id.Scale, edge);
                                    break;
                                }
                            }
                            SpawnFootprint a, b;
                            if (SpawnSpace.AnyBodiesOverlap(out a, out b))
                            {
                                overlapFrames++;
                                if (firstOverlap == null)
                                    firstOverlap = string.Format("world {0} scroll {1} t={2:F2}: {3} ({4:F2}x) at {5} and {6} ({7:F2}x) at {8}{9}", w, v, t,
                                                                 a.name, EnemyIdentity.ScaleOf(a.gameObject), (Vector2)a.transform.position,
                                                                 b.name, EnemyIdentity.ScaleOf(b.gameObject), (Vector2)b.transform.position,
                                                                 Describe(a) + Describe(b));
                            }
                        }
                        Debug.Log(string.Format("[HAZARD-SIZE] run world {0} scroll {1} density {2:F2}: spawned {3}, deferred {4}, dropped {5}",
                                                w, v, densities[di], board.SpawnedCount, board.DeferredTotal, board.DroppedTotal));
                    }
        }
        finally
        {
            Object.DestroyImmediate(target.gameObject);
            SetWorldManager(null);
            Object.DestroyImmediate(wmGo);
            LoopDifficulty.Reset();
            SpawnSpace.ClockOverride = null;
            ClearAll();
        }
        float meanArea = rocks > 0 ? (float)(area / rocks) : 0f;
        Check(string.Format("mixed sizes flew: {0} rocks over {1} frames, {2:F2}x to {3:F2}x; small {4:P0}, typical {5:P0}, large {6:P0}; mean area {7:F3}",
                            rocks, frames, smallest, biggest, tiers[0] / (float)Mathf.Max(1, rocks), tiers[1] / (float)Mathf.Max(1, rocks),
                            tiers[2] / (float)Mathf.Max(1, rocks), meanArea),
              rocks > 1000 && biggest > 1.3f && smallest < .8f && Mathf.Abs(meanArea - 1f) <= .05f);
        Check("no two live bodies ever overlap, every frame of 16 mixed-size runs (" + overlapFrames + " frames" +
              (firstOverlap != null ? "; first: " + firstOverlap : "") + ")", overlapFrames == 0);
        Check(string.Format("every rock entered its row with a ship-wide gap ({0} rows, {1} short, narrowest widest-gap {2:F2} u vs {3:F2}{4})",
                            rowsChecked, rowsShort, worstGap, SpawnLane.GuaranteedGap, firstRow != null ? "; first: " + firstRow : ""),
              rowsChecked > 1000 && rowsShort == 0);
        Check("no rock's collider ever crosses the lane edge into the rails (" + railFrames + " frames" +
              (firstRail != null ? "; first: " + firstRail : "") + ")", railFrames == 0);
    }

    // (a rail mine's ride, for a failure line)
    static string Describe(SpawnFootprint f)
    {
        RailMineMount m;
        if (f == null || !f.TryGetComponent(out m)) return "";
        return string.Format(" [{0} ride {1} {2:F2} slide {3:F2} shove {4:F2}]", f.name, m.RideState, m.Ride, m.Slide, m.Shove);
    }

    // ---- 9: the threat table ------------------------------------------------------------

    struct Reading { public float onScreen, shots, rocks, rockArea; public float Threats => onScreen + shots * EnemyThreat.ShotWeight; }

    static readonly int[] Huds = { 5, 10, 20, 30, 35 };

    static void ThreatTable()
    {
        EnemyThreat.ForceShooting = true;
        EnemyDensityProbe.SetView(1080, 2520);
        const int seeds = 4;
        bool pointsOk = true;
        float worst = 0f;
        Debug.Log("[HAZARD-SIZE] THREATS (1080x2520, " + seeds + " seeds): HUD | sizes off: in view, shots, threats, rocks, rock area | sizes on: same | change");
        for (int i = 0; i < Huds.Length; i++)
        {
            int hud = Huds[i];
            HazardSize.Enabled = false;
            var off = Pinned(hud, EnemyDensityProbe.LevelSecondFor(hud), seeds);
            HazardSize.Enabled = true;
            var on = Pinned(hud, EnemyDensityProbe.LevelSecondFor(hud), seeds);
            float change = on.Threats / Mathf.Max(.01f, off.Threats) - 1f;
            worst = Mathf.Max(worst, Mathf.Abs(change));
            Debug.Log(string.Format("[HAZARD-SIZE] THREATS hud {0,2} | off {1,5:F2} + {2:F2} shots = {3,5:F2}, rocks {4:F2}, area {5:F2} | on {6,5:F2} + {7:F2} shots = {8,5:F2}, rocks {9:F2}, area {10:F2} | threats {11:+0.0%;-0.0%}, rock area {12:+0.0%;-0.0%}",
                                    hud, off.onScreen, off.shots, off.Threats, off.rocks, off.rockArea, on.onScreen, on.shots, on.Threats, on.rocks, on.rockArea,
                                    change, on.rockArea / Mathf.Max(.01f, off.rockArea) - 1f));
            pointsOk &= Mathf.Abs(change) <= .06f;
        }
        HazardSize.Enabled = false;
        var runOff = WholeRun(seeds);
        HazardSize.Enabled = true;
        var runOn = WholeRun(seeds);
        float runChange = runOn.Threats / Mathf.Max(.01f, runOff.Threats) - 1f;
        float areaChange = runOn.rockArea / Mathf.Max(.01f, runOff.rockArea) - 1f;
        Debug.Log(string.Format("[HAZARD-SIZE] THREATS whole run | off {0,5:F2} + {1:F2} shots = {2,5:F2}, rocks {3:F2}, area {4:F2} | on {5,5:F2} + {6:F2} shots = {7,5:F2}, rocks {8:F2}, area {9:F2} | threats {10:+0.0%;-0.0%}, rock area {11:+0.0%;-0.0%}",
                                runOff.onScreen, runOff.shots, runOff.Threats, runOff.rocks, runOff.rockArea, runOn.onScreen, runOn.shots, runOn.Threats,
                                runOn.rocks, runOn.rockArea, runChange, areaChange));
        Check(string.Format("on-screen threat at HUD 5/10/20/30/35 within 6% of the one-size board (worst {0:P1})", worst), pointsOk);
        Check(string.Format("over a whole run within 4% ({0:+0.0%;-0.0%}), and the rock area in view within 8% ({1:+0.0%;-0.0%})", runChange, areaChange),
              Mathf.Abs(runChange) <= .04f && Mathf.Abs(areaChange) <= .08f);
        EnemyDensityProbe.RestoreView();
        EnemyThreat.ForceShooting = false;
    }

    static void Tally(ref Reading r)
    {
        float half = EnemyDensityProbe.ViewHalfHeight;
        var live = SpawnSpace.Live(SpawnLayer.Enemy);
        for (int i = 0; i < live.Count; i++)
        {
            float y = live[i].transform.position.y;
            if (y < -half || y > half) continue;
            r.onScreen++;
            EnemyIdentity id;
            if (live[i].TryGetComponent(out id) && id.Def != null && id.Def.role == EnemyRole.Rock)
            {
                r.rocks++;
                r.rockArea += id.Scale * id.Scale;
            }
        }
        r.shots += HostileShots.ActiveCount;
    }

    static Reading Pinned(int hud, float levelSecond, int seeds)
    {
        var total = new Reading();
        for (int seed = 0; seed < seeds; seed++)
        {
            ClearAll();
            EnemyDensityProbe.chasers.Clear();
            buttonClicks.playerDied = false;
            score.pauseCounter = 0;
            LoopDifficulty.Reset();
            Random.InitState(4100 + hud * 13 + seed);
            float v = hud * .3f;
            moveBackGround.speed = hud / 100f;
            var board = EnemyDensityProbe.NewBoard();
            var ship = new GameObject("~SizeDensityShip").transform;
            foreach (string timer in EnemyDensityProbe.Timers)
                typeof(enmiesOnBoard).GetField(timer, EnemyDensityProbe.Inst).SetValue(board, Random.Range(0f, 1.5f));
            var r = new Reading();
            int frames = 0;
            float clock = 0f;
            for (float t = 0f; t < EnemyDensityProbe.WarmupSeconds + EnemyDensityProbe.WindowSeconds; t += Dt)
            {
                clock += Dt;
                EnemyDensityProbe.Elapsed.SetValue(board, levelSecond);
                EnemyDensityProbe.StepBoard(board, ship, clock, v);
                if (t < EnemyDensityProbe.WarmupSeconds) continue;
                frames++;
                Tally(ref r);
            }
            Add(ref total, r, frames);
            Object.DestroyImmediate(ship.gameObject);
            ClearAll();
        }
        return Scale(total, seeds);
    }

    static Reading WholeRun(int seeds)
    {
        var total = new Reading();
        for (int seed = 0; seed < seeds; seed++)
        {
            ClearAll();
            EnemyDensityProbe.chasers.Clear();
            buttonClicks.playerDied = false;
            score.pauseCounter = 0;
            LoopDifficulty.Reset();
            Random.InitState(9100 + seed);
            var board = EnemyDensityProbe.NewBoard();
            var ship = new GameObject("~SizeDensityShip").transform;
            var r = new Reading();
            int frames = 0;
            for (float t = 0f; t < 120f; t += Dt)
            {
                float hud = Mathf.Min(SpeedRamp.CapHud, EnemyDensityProbe.ReferenceHudPerSecond * t);
                moveBackGround.speed = hud / 100f;
                EnemyDensityProbe.Elapsed.SetValue(board, t);
                EnemyDensityProbe.StepBoard(board, ship, t, hud * .3f);
                frames++;
                Tally(ref r);
            }
            Add(ref total, r, frames);
            Object.DestroyImmediate(ship.gameObject);
            ClearAll();
        }
        return Scale(total, seeds);
    }

    static void Add(ref Reading total, Reading r, int frames)
    {
        total.onScreen += r.onScreen / frames;
        total.shots += r.shots / frames;
        total.rocks += r.rocks / frames;
        total.rockArea += r.rockArea / frames;
    }

    static Reading Scale(Reading r, int seeds)
    {
        r.onScreen /= seeds; r.shots /= seeds; r.rocks /= seeds; r.rockArea /= seeds;
        return r;
    }
}
