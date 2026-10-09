using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// Home-screen (startS4) ship traffic: depth layers, boosts, zoomers,
// formations, tricks, and cute same-layer crashes, all pooled and cosmetic.
//
// Everything is driven through TitleScreenTraffic.Step(dt) on a fixed step
// and a seeded Random, so the "long run" checks are deterministic.
public static class TitleScreenTrafficTest
{
    static int fails;
    static void Check(string what, bool ok)
    {
        Debug.Log((ok ? "[TT] PASS  " : "[TT] FAIL  ") + what);
        if (!ok) fails++;
    }

    const string LogoPath = "Assets/Art/UI/Title/pause_title_2.png";
    const string LogoSha256 = "fbc73021f06f4dd3a79d8d42e1bd22870723f49d8d16a2474df8896b1ab7d305";
    const string LogoMetaSha256 = "005e199d4579224cc74902d945901813061003e2979944897b6354f5f3a382a8";
    // startS4's authored menuTitle transform / renderer
    static readonly Vector3 LogoPos = new Vector3(-0.04f, 2.72f, 0f);
    static readonly Vector3 LogoScale = new Vector3(0.4176109f, 0.46091294f, 1f);

    const float Dt = 1f / 30f;

    public static void Run()
    {
        TestHarness.Exit(Execute());
    }

    public static int Execute()
    {
        fails = 0;
        using var sandbox = new TestHarness.Sandbox();

        // Opened once for the whole run (see git history: re-opening the
        // scene between tests churns the ship sprite caches for no benefit).
        EditorSceneManager.OpenScene("Assets/Scenes/startS4.unity", OpenSceneMode.Single);

        DepthLayersAreOrdered();
        NoDuplicateHullsInTheAir();
        BoostsUseTheShipsOwnBoostFlame();
        ZipsFlyTheDockLaunchCurve();
        SpinnersFlyTheirSpinDrift();
        SpinnerHullsSpinLikeGameplay();
        CrashesOnlyWithinALayerAndRateLimited();
        LongRunStaysBoundedAndLively();
        DensityIsFortyPercentUp();
        SafeAreaAndAspectRatios();
        NeverInterceptsUiRaycasts();
        NoPerFrameAllocations();
        LogoUntouched();
        NoBileMiteOnHomeScreen();

        Debug.Log("[TT] failures: " + fails);
        return fails;
    }

    static TitleScreenTraffic Make(string name, int seed, System.Action<TitleScreenTraffic> configure = null)
    {
        Random.InitState(seed);
        var go = new GameObject(name);
        var t = go.AddComponent<TitleScreenTraffic>();
        configure?.Invoke(t);
        t.Init();
        return t;
    }

    static void Done(TitleScreenTraffic t)
    {
        t.Shutdown();
        Object.DestroyImmediate(t.gameObject);
        TestHarness.FlushGpu();
    }

    // The elite's snipe run clears the sky on purpose; population checks
    // skip it and the few seconds the traffic takes to refill.
    static bool EliteWindow(TitleScreenTraffic t)
    {
        return t.EliteBusy || (t.LastEliteReturnAt >= 0f && t.Now - t.LastEliteReturnAt < 4f);
    }

    static SpriteRenderer Logo()
    {
        var go = GameObject.Find("menuTitle");
        return go != null ? go.GetComponent<SpriteRenderer>() : null;
    }

    // ------------------------------------------------------------------

    static void DepthLayersAreOrdered()
    {
        var d = TitleScreenTraffic.Depths;
        Check("three depth layers", d.Length == 3);
        bool scale = true, speed = true, sort = true, fx = true;
        for (int i = 1; i < d.Length; i++)
        {
            scale &= d[i].scale > d[i - 1].scale;
            speed &= d[i].speed > d[i - 1].speed;
            sort &= d[i].sortBase > d[i - 1].sortBase + (TitleScreenTraffic.PoolSize * TitleScreenTraffic.SortSlots);
            fx &= d[i].fxSort > d[i - 1].fxSort;
        }
        Check("scale grows back -> mid -> front", scale);
        Check("speed grows back -> mid -> front", speed);
        Check("sorting bands are ordered and never overlap", sort);
        Check("explosion sorting follows the layers", fx);
        Check("only the back layer is hazed", d[0].haze && !d[1].haze && !d[2].haze);
        Check("back layer's explosions are dimmed too", d[0].fxTint.r < 1f && d[0].fxTint.g < 1f);

        var logo = Logo();
        int logoOrder = logo != null ? logo.sortingOrder : 0;
        int midTop = d[1].sortBase + (TitleScreenTraffic.PoolSize * TitleScreenTraffic.SortSlots);
        Check("back and mid ships (and their fx) sort behind the PAUSE logo",
              midTop < logoOrder && d[1].fxSort + 4 < logoOrder && d[0].fxSort + 4 < logoOrder);
        Check("front ships sort in front of the logo (they keep off it instead)", d[2].sortBase > logoOrder);

        // The back layer must still draw over the menu's starfield, whatever
        // the world-backdrop work does to it: either it stays an opaque pass
        // (drawn before every sprite) or it sorts below the back band.
        var sky = GameObject.Find("menuBackground");
        var skyR = sky != null ? sky.GetComponent<Renderer>() : null;
        bool skyBehind = true;
        if (skyR != null)
        {
            var mat = skyR.sharedMaterial;
            bool opaque = mat != null && mat.renderQueue < 2500;
            skyBehind = opaque || (skyR.sortingLayerID == 0 && skyR.sortingOrder < d[0].sortBase);
        }
        Check("the menu starfield draws behind even the back layer", skyBehind);

        var t = Make("~TT_depth", 11);
        t.NextEliteAt = 1e9f;   // no elite snipe run mid-test (TitleScreenEliteTest covers it)
        float[] sum = new float[3]; int[] n = new int[3];
        bool bands = true;
        var hazeShader = Resources.Load<Shader>("TitleTraffic/TitleTrafficHaze");
        bool hazed = true;
        foreach (var f in t.Pool)
        {
            if (!f.active) continue;
            int L = (int)f.layer;
            sum[L] += f.tr.lossyScale.y / f.normScale;
            n[L]++;
            int lo = d[L].sortBase, hi = d[L].sortBase + (TitleScreenTraffic.PoolSize * TitleScreenTraffic.SortSlots);
            bands &= f.hull.sortingOrder >= lo && f.hull.sortingOrder < hi;
            if (L == 0 && hazeShader != null) hazed &= f.hull.sharedMaterial != null && f.hull.sharedMaterial.shader == hazeShader;
            if (L != 0 && hazeShader != null) hazed &= f.hull.sharedMaterial == null || f.hull.sharedMaterial.shader != hazeShader;
        }
        Check("the haze shader ships in Resources", hazeShader != null);
        Check("every layer is populated at start (" + n[0] + "/" + n[1] + "/" + n[2] + ")", n[0] > 0 && n[1] > 0 && n[2] > 0);
        Check("on-screen hull size follows depth", n[0] > 0 && n[1] > 0 && n[2] > 0 &&
              sum[0] / n[0] < sum[1] / n[1] && sum[1] / n[1] < sum[2] / n[2]);
        Check("each ship's sorting order sits in its layer's band", bands);
        Check("back-layer hulls use the haze material, nearer ones don't", hazed);

        // measured speed per layer over a few seconds of plain cruising
        float[] dist = new float[3]; int[] m = new int[3];
        var start = new Dictionary<TitleScreenTraffic.Flyer, Vector2>();
        foreach (var f in t.Pool) if (f.active && f.state == TitleScreenTraffic.State.Cruise) start[f] = f.pos;
        t.NextCrashAt = 1e9f;
        for (int i = 0; i < 30; i++) t.Step(Dt);
        foreach (var kv in start)
        {
            var f = kv.Key;
            if (!f.active || f.state != TitleScreenTraffic.State.Cruise) continue;
            dist[(int)f.layer] += (f.pos - kv.Value).magnitude; m[(int)f.layer]++;
        }
        if (m[0] > 0 && m[1] > 0 && m[2] > 0)
            Check("measured cruise speed follows depth", dist[0] / m[0] < dist[1] / m[1] && dist[1] / m[1] < dist[2] / m[2]);
        Done(t);
    }

    static void NoDuplicateHullsInTheAir()
    {
        var t = Make("~TT_dupes", 3);
        t.NextEliteAt = 1e9f;   // no elite snipe run mid-test (TitleScreenEliteTest covers it)
        // x1.4 density: the roster (15) is smaller than the busiest sky (17),
        // so the pool is one ship per roster id plus a few flagged twins that
        // only fly once every original is already in the air.
        Check("the pool holds one ship per roster id plus twins up to the cap (" + t.Pool.Length + ")",
              t.Pool.Length == TitleScreenTraffic.PoolSize && t.Pool.Length >= ShipId.Count);
        var ids = new HashSet<int>();
        bool ok = true, twinsLast = true;
        for (int i = 0; i < t.Pool.Length; i++)
        {
            var f = t.Pool[i];
            if (f.twin) { ok &= ShipId.IsValid(f.id) && f.go.name == ShipId.ObjectName(f.id) + "_twin" && !f.wind; }
            else { ok &= ids.Add(f.id) && ShipId.IsValid(f.id) && f.go.name == ShipId.ObjectName(f.id); twinsLast &= i < ShipId.Count; }
        }
        Check("originals are the roster ids, named like ships; twins come after them", ok && twinsLast && ids.Count == ShipId.Count);
        Check("sorting slots fit a layer's band (" + t.Pool.Length + " x " + TitleScreenTraffic.SortSlots + " <= 85)",
              t.Pool.Length * TitleScreenTraffic.SortSlots <= 85);
        // while any original is free, a launch never takes a twin
        foreach (var f in t.Pool) if (f.active) { f.active = false; f.go.SetActive(false); }
        bool noEarlyTwin = true;
        for (int i = 0; i < ShipId.Count; i++) { var f = t.Launch(TitleScreenTraffic.Depth.Mid, true, false, null); noEarlyTwin &= f != null && !f.twin; }
        Check("no twin flies before the originals are all up", noEarlyTwin);

        bool hullArt = true;
        foreach (var f in t.Pool)
        {
            var rest = shopingShips.SpriteFor(f.id);
            hullArt &= f.hull.sprite != null && rest != null && f.hull.sprite.texture == rest.texture;
        }
        Check("hull art is the roster's runtime art (ShipHullArt's sheet via ApplyHull)", hullArt);
        Done(t);
    }

    static void BoostsUseTheShipsOwnBoostFlame()
    {
        var t = Make("~TT_boost", 5, x => x.layerTargets = new[] { 0, 2, 0 });
        t.NextEliteAt = 1e9f;   // no elite snipe run mid-test (TitleScreenEliteTest covers it)
        t.NextCrashAt = 1e9f;
        TitleScreenTraffic.Flyer f = null;
        foreach (var c in t.Pool) if (c.active && !c.wind) { f = c; break; }
        Check("a nozzle-flamed cruiser is in the air", f != null);
        if (f == null) { Done(t); return; }

        var boost = f.boost;
        Check("the ship carries ShipExhaust's boost flame root (Boost<id>, tag boost)",
              boost != null && boost.name == "Boost" + f.id && boost.CompareTag("boost"));
        var flames = boost != null ? boost.GetComponentsInChildren<DockLaunchFlame>(true) : new DockLaunchFlame[0];
        Check("one animated plume per ShipNozzles nozzle", flames.Length == ShipNozzles.For(f.id).Length);
        // Every ship's exhaust must be exactly what gameplay gives that ShipId
        // (spawnShips.ApplyHull -> ShipExhaust.ConfigureBoost, with whatever
        // flipbook / per-ship style it attaches), not a fixed sprite.
        string why = null;
        foreach (var c in t.Pool)
        {
            why = ExhaustDiff(c);
            if (why != null) { why = ShipId.KeyOf(c.id) + ": " + why; break; }
        }
        Check("every ship's plumes are the same exhaust gameplay gives that ShipId" + (why != null ? " (" + why + ")" : ""), why == null);

        f.state = TitleScreenTraffic.State.Cruise;
        f.trick = TitleScreenTraffic.Trick.None;
        t.Step(Dt);
        float idle = boost.localScale.y, idleSpeed = f.speed;
        int before = t.Boosts;
        t.StartBoost(f);
        for (int i = 0; i < 6; i++) t.Step(TitleScreenTraffic.Tick);   // into the back-off
        float anticipation = boost.localScale.y;
        for (int i = 0; i < 9; i++) t.Step(TitleScreenTraffic.Tick);   // mid-burst
        float burst = boost.localScale.y;
        Check("StartBoost counts a boost", t.Boosts == before + 1);
        Check("anticipation pulls the flame in (" + anticipation + " < " + idle + ")", anticipation < idle);
        Check("the burst flares the flame (" + burst + " > " + idle + ")", burst > idle * 2f);
        Check("the burst accelerates (" + f.speed + " vs " + idleSpeed + ")", f.speed > idleSpeed * 2f);
        Check("the burst stretches along the flight line, within the 6% rigid-hull limit",
              f.stretchY > 1f && f.stretchY <= 1.061f && f.stretchX < 1f);
        Check("the burst draws a light streak", f.trail != null);
        for (int i = 0; i < 40; i++) t.Step(TitleScreenTraffic.Tick);
        Check("then it settles back to a cruise flame and speed",
              f.state == TitleScreenTraffic.State.Cruise && Mathf.Abs(boost.localScale.y - idle) < .05f && f.speedMul == 1f);

        // boosts happen on their own
        var t2 = Make("~TT_boost2", 6);
        for (int i = 0; i < 30 * 30; i++) t2.Step(Dt);
        Check("ships hit the boost by themselves (" + t2.Boosts + " in 30 s)", t2.Boosts >= 2);
        Done(t2);
        Done(t);
    }

    // Compares a traffic ship's boost/plume hierarchy with a ship dressed by
    // gameplay's own path for the same id. Null when they match.
    static string ExhaustDiff(TitleScreenTraffic.Flyer f)
    {
        var reference = new GameObject("~TT_ref");
        reference.AddComponent<SpriteRenderer>();
        try
        {
            spawnShips.ApplyHull(reference, f.id);
            var want = reference.transform.Find("Boost" + f.id);
            if (want == null) return f.boost == null ? null : "gameplay has no boost root";
            if (f.boost == null) return "no boost root";
            if (f.boost.childCount != want.childCount) return "plume count " + f.boost.childCount + " vs " + want.childCount;
            for (int i = 0; i < want.childCount; i++)
            {
                var w = want.GetChild(i);
                var g = f.boost.Find(w.name);
                if (g == null) return "missing " + w.name;
                if (Components(g) != Components(w)) return w.name + " components " + Components(g) + " vs " + Components(w);
                if ((g.localPosition - w.localPosition).sqrMagnitude > 1e-8f) return w.name + " mount";
                if ((g.localScale - w.localScale).sqrMagnitude > 1e-8f) return w.name + " size";
                var gs = g.GetComponent<SpriteRenderer>();
                var ws = w.GetComponent<SpriteRenderer>();
                if ((gs == null) != (ws == null)) return w.name + " renderer";
                if (gs == null) continue;
                if (gs.enabled != ws.enabled) return w.name + " enabled";
                if (gs.color != ws.color) return w.name + " tint";
                Texture gt = gs.sprite != null ? gs.sprite.texture : null, wt = ws.sprite != null ? ws.sprite.texture : null;
                if (gt != wt) return w.name + " art " + (gt != null ? gt.name : "none") + " vs " + (wt != null ? wt.name : "none");
            }
            return null;
        }
        finally { Object.DestroyImmediate(reference); }
    }

    static string Components(Transform t)
    {
        var names = new List<string>();
        foreach (var c in t.GetComponents<Component>()) if (c != null) names.Add(c.GetType().Name);
        names.Sort();
        return string.Join(",", names);
    }

    // The zip-off is SpaceDock's launch: same shared DockLaunch curves.
    static void ZipsFlyTheDockLaunchCurve()
    {
        Check("SpaceDock's launch timing is the shared DockLaunch timing",
              SpaceDock.LaunchDuration == DockLaunch.Duration && DockLaunch.FlightTime > 0f);

        var t = Make("~TT_zip", 9, x => { x.layerTargets = new[] { 0, 1, 0 }; x.zoomInterval = new Vector2(1e6f, 1e6f); });
        t.NextEliteAt = 1e9f;   // no elite snipe run mid-test (TitleScreenEliteTest covers it)
        t.NextCrashAt = 1e9f;
        TitleScreenTraffic.Flyer f = null;
        foreach (var c in t.Pool) if (c.active && !c.wind) { f = c; break; }
        Check("a ship to zip", f != null);
        if (f == null) { Done(t); return; }
        f.pos = new Vector2(t.Safe.center.x + .8f, t.Safe.yMin + 2.2f);
        f.heading = Mathf.PI * .5f;
        f.state = TitleScreenTraffic.State.Cruise;
        f.trick = TitleScreenTraffic.Trick.None;
        int zips = t.Zips;
        t.StartZip(f);
        Check("StartZip counts a zip", t.Zips == zips + 1 && f.state == TitleScreenTraffic.State.ZipOut);

        const float step = 1f / 120f;
        while (f.stateT < TitleScreenTraffic.ZipBrake + .45f) t.Step(step);
        float u = f.stateT - TitleScreenTraffic.ZipBrake;
        float lift = DockLaunch.Lift(u);
        Check("undock: lifts on DockLaunch.Lift (grow " + f.grow + ")", Mathf.Abs(f.grow - (1f + DockLaunch.LiftGrow * lift)) < 1e-4f);
        Check("undock: flame on DockLaunch.UndockFlame", Mathf.Abs(f.flame - DockLaunch.UndockFlame(lift)) < 1e-4f);
        Check("undock: it has paused and backed off", f.speedMul == 0f &&
              Vector2.Dot(f.pos - f.zipRest, new Vector2(Mathf.Cos(f.heading), Mathf.Sin(f.heading))) < 0f);
        Check("undock: it banks into the coming turn", Mathf.Abs(f.turn) > .3f);

        while (f.stateT < TitleScreenTraffic.ZipBrake + DockLaunch.UndockTime + DockLaunch.FlightTime * .5f) t.Step(step);
        float g = (f.stateT - TitleScreenTraffic.ZipBrake - DockLaunch.UndockTime) / DockLaunch.FlightTime;
        float pu = DockLaunch.Flight(g);
        Vector2 want = DockTween.Bezier(f.zipP0, f.zipP1, f.zipP2, pu);
        Check("fly-out follows the dock's launch Bezier at DockLaunch.Flight(f) (off by " + (f.pos - want).magnitude + ")",
              (f.pos - want).magnitude < 1e-4f);
        Check("first leg runs straight out ApproachDistance, like leaving the berth",
              Mathf.Abs((f.zipP1 - f.zipP0).magnitude - DockLaunch.ApproachDistance * f.scale) < 1e-3f);
        Check("exhaust flares on DockLaunch.Flare", Mathf.Abs(f.flame - DockLaunch.Flare(g)) < 1e-4f);
        Check("grows on DockLaunch.FlightGrow", Mathf.Abs(f.grow - (1f + DockLaunch.LiftGrow) * DockLaunch.FlightGrow(g)) < 1e-4f);
        Vector3 tan = DockTween.BezierTangent(f.zipP0, f.zipP1, f.zipP2, pu);
        float along = Mathf.Atan2(tan.y, tan.x) * Mathf.Rad2Deg;
        float expected = f.zipHeading * Mathf.Rad2Deg + Mathf.DeltaAngle(f.zipHeading * Mathf.Rad2Deg, along) * DockLaunch.TurnIn(g);
        Check("heading swings onto the path on DockLaunch.TurnIn",
              Mathf.Abs(Mathf.DeltaAngle(f.heading * Mathf.Rad2Deg, expected)) < .01f);
        Check("streak on the way out", f.trail != null);
        float guard = 0f;
        while (f.active && guard < 3f) { t.Step(step); guard += step; }
        Check("and it's gone off-screen, back to the pool", !f.active && !t.View.Contains(f.zipP2));

        // zoomers: the same curve backwards, braking into a spot
        var z = t.Launch(TitleScreenTraffic.Depth.Mid, false, true, null);
        Check("a zoomer enters from off-screen", z != null && z.state == TitleScreenTraffic.State.ZipIn && !t.View.Contains(z.pos));
        if (z != null)
        {
            while (z.stateT < DockLaunch.FlightTime * .4f) t.Step(step);
            float gz = z.stateT / DockLaunch.FlightTime;
            float uz = DockLaunch.Flight(1f - gz);
            Vector2 wz = DockTween.Bezier(z.zipP0, z.zipP1, z.zipP2, uz);
            Check("zoomer enters on the launch curve reversed (off by " + (z.pos - wz).magnitude + ")", (z.pos - wz).magnitude < 1e-4f);
            Vector2 motion = -(Vector2)DockTween.BezierTangent(z.zipP0, z.zipP1, z.zipP2, uz);
            Check("zoomer flies nose-first", Vector2.Dot(new Vector2(Mathf.Cos(z.heading), Mathf.Sin(z.heading)), motion) > 0f);
            while (z.state == TitleScreenTraffic.State.ZipIn) t.Step(step);
            Check("brakes to a lazy drift at its spot", z.state == TitleScreenTraffic.State.Cruise && (z.pos - z.zipP0).magnitude < .5f);
            int before = t.Zips;
            guard = 0f;
            while (z.active && t.Zips == before && guard < 3f) { t.Step(step); guard += step; }
            Check("then pauses and zips off dock-style", t.Zips == before + 1);
        }
        Done(t);
    }

    // Ninja and UFO: gameplay's ShipSpinDrift (ring + wake), cruise and boost
    // drawings, wake trailing the travel direction. Everyone else: plumes.
    static void SpinnersFlyTheirSpinDrift()
    {
        var t = Make("~TT_drift", 13, x => { x.layerTargets = new[] { 0, 0, 0 }; x.zoomInterval = new Vector2(1e6f, 1e6f); });
        t.NextEliteAt = 1e9f;   // no elite snipe run mid-test (TitleScreenEliteTest covers it)
        t.NextCrashAt = 1e9f;
        int spinners = 0;
        string why = null;
        bool plumes = true;
        foreach (var f in t.Pool)
        {
            bool spins = ShipExhaust.UsesSpinDrift(f.id);
            if (!spins)
            {
                plumes &= f.drift == null && f.go.GetComponent<ShipSpinDrift>() == null && f.nozzles.Length > 0;
                foreach (var n in f.nozzles) plumes &= n.enabled && n.sprite != null;
                continue;
            }
            spinners++;
            if (why == null) why = DriftDiff(f);
        }
        Check("both spinners are in the pool", spinners == 2);
        Check("non-spinners keep their themed nozzle plumes and get no drift", plumes);
        Check("each spinner's drift is gameplay's ShipSpinDrift for that ShipId" + (why != null ? " (" + why + ")" : ""), why == null);

        foreach (var f in t.Pool)
        {
            if (f.drift == null) continue;
            var d = f.drift;
            Check(ShipId.KeyOf(f.id) + ": drift is driven by the traffic (unscaled), not its own scaled-time Update",
                  !d.enabled && !d.spinHull && !d.spinRing && d.wakeFollowsHeading && d.headingFromOwner && !d.respondToPause);

            var at = new Vector2(t.Safe.center.x + .7f, t.Safe.yMin + 2f);
            var g = Place(t, TitleScreenTraffic.Depth.Mid, at, 0f);
            if (g != f)
            {
                // Place takes a random free hull; walk until it lands on this spinner
                int guard = 0;
                while (g != f && guard++ < 400)
                {
                    if (g != null) { g.active = false; g.go.SetActive(false); }
                    g = Place(t, TitleScreenTraffic.Depth.Mid, at, 0f);
                }
            }
            if (g != f) { Check(ShipId.KeyOf(f.id) + ": could be launched", false); continue; }
            for (int i = 0; i < 6; i++) t.Step(Dt);
            Check(ShipId.KeyOf(f.id) + ": ring and wake are showing while it cruises",
                  d.Ring.enabled && d.Wake.enabled && d.Ring.sprite != null && d.Wake.sprite != null);
            Check(ShipId.KeyOf(f.id) + ": cruise drawings while cruising", !d.ShowingBoost);

            // the wake hangs behind the direction of travel, whichever way that is
            bool behind = true;
            for (int k = 0; k < 4; k++)
            {
                f.heading = k * 1.7f + .4f;
                f.waypoint = f.pos + new Vector2(Mathf.Cos(f.heading), Mathf.Sin(f.heading)) * 3f;
                t.Step(.001f);
                Vector2 dir = new Vector2(Mathf.Cos(f.heading), Mathf.Sin(f.heading));
                Vector2 off = (Vector2)(d.Wake.transform.position - f.tr.position);
                behind &= Vector2.Dot(off, dir) < 0f;
            }
            Check(ShipId.KeyOf(f.id) + ": the wake trails behind the travel direction on any heading", behind);

            int sorted = TitleScreenTraffic.Depths[1].sortBase + f.slot * TitleScreenTraffic.SortSlots;
            Check(ShipId.KeyOf(f.id) + ": ring and wake sort just under the hull, inside the layer's band",
                  d.Ring.sortingOrder < f.hull.sortingOrder && d.Wake.sortingOrder < d.Ring.sortingOrder && d.Wake.sortingOrder >= sorted);

            t.StartBoost(f);
            for (int i = 0; i < 12; i++) t.Step(TitleScreenTraffic.Tick);
            Check(ShipId.KeyOf(f.id) + ": boost drawings while it boosts", d.ShowingBoost);
            int steps = 0;
            while (f.state == TitleScreenTraffic.State.Boost && steps++ < 200) t.Step(TitleScreenTraffic.Tick);
            t.Step(TitleScreenTraffic.Tick);
            Check(ShipId.KeyOf(f.id) + ": back to cruise drawings after", !d.ShowingBoost);

            f.pos = at; f.heading = Mathf.PI * .5f;
            t.StartZip(f);
            for (int i = 0; i < 30; i++) t.Step(TitleScreenTraffic.Tick);
            Check(ShipId.KeyOf(f.id) + ": boost drawings while it zips dock-style", f.state == TitleScreenTraffic.State.ZipOut && d.ShowingBoost);
            steps = 0;
            while (f.active && steps++ < 400) t.Step(TitleScreenTraffic.Tick);
        }

        // back-layer spinners keep the depth haze on their drift too
        var hazeShader = Resources.Load<Shader>("TitleTraffic/TitleTrafficHaze");
        bool hazed = true; int seen = 0;
        for (int tries = 0; tries < 60 && seen == 0; tries++)
        {
            var g = t.Launch(TitleScreenTraffic.Depth.Back, true, false, null);
            if (g == null) break;
            if (g.drift == null) { g.active = false; g.go.SetActive(false); continue; }
            seen++;
            hazed &= g.drift.Ring.sharedMaterial != null && g.drift.Ring.sharedMaterial.shader == hazeShader &&
                     g.drift.Wake.sharedMaterial != null && g.drift.Wake.sharedMaterial.shader == hazeShader;
        }
        Check("a back-layer spinner's ring and wake wear the depth haze", seen > 0 && hazed);
        Done(t);
    }

    // Ninja and UFO spin their hulls in traffic the way ShipSpinDrift spins
    // the player's in play: the same rate, cruising, boosting or zipping
    // (gameplay doesn't speed the spin up on a boost). The ring rides the
    // hull's spin, the wake trails the travel heading, a dizzy tumble adds
    // on top, and nothing else spins.
    static void SpinnerHullsSpinLikeGameplay()
    {
        var t = Make("~TT_spin", 21, x => { x.layerTargets = new[] { 0, 0, 0 }; x.zoomInterval = new Vector2(1e6f, 1e6f); });
        t.NextEliteAt = 1e9f;   // no elite snipe run mid-test (TitleScreenEliteTest covers it)
        t.NextCrashAt = 1e9f;
        var refGo = new GameObject("~TT_spinref");
        float gameplay = refGo.AddComponent<ShipSpinDrift>().degreesPerSecond;
        Object.DestroyImmediate(refGo);
        Check("gameplay spins a spinner hull at 300 deg/s (" + gameplay + ")", Mathf.Approximately(gameplay, 300f));

        var at = new Vector2(t.Safe.center.x + .7f, t.Safe.yMin + 2f);
        const float step = TitleScreenTraffic.Tick;
        int spinners = 0;
        foreach (var f in t.Pool)
        {
            if (f.drift == null) continue;
            spinners++;
            string who = ShipId.KeyOf(f.id) + ": ";
            var d = f.drift;
            var g = Place(t, TitleScreenTraffic.Depth.Mid, at, .3f);
            int guard = 0;
            while (g != f && guard++ < 400)
            {
                if (g != null) { g.active = false; g.go.SetActive(false); }
                g = Place(t, TitleScreenTraffic.Depth.Mid, at, .3f);
            }
            if (g != f) { Check(who + "could be launched", false); continue; }
            t.Step(step);

            Check(who + "spin rate is gameplay's", Mathf.Approximately(TitleScreenTraffic.SpinRate(f), gameplay));

            // cruising: the hull turns gameplay's rate every frame, the ring
            // turns with it, the wake hangs behind the travel heading
            float worst = 0f, ringOff = 0f, total = 0f;
            bool trails = true;
            for (int i = 0; i < 24; i++)
            {
                float z0 = f.tr.eulerAngles.z;
                t.Step(step);
                float adv = Mathf.DeltaAngle(z0, f.tr.eulerAngles.z);
                total += adv;
                worst = Mathf.Max(worst, Mathf.Abs(adv - gameplay * step));
                ringOff = Mathf.Max(ringOff, Quaternion.Angle(d.Ring.transform.rotation, f.tr.rotation));
                Vector2 dir = new Vector2(Mathf.Cos(f.heading), Mathf.Sin(f.heading));
                Vector2 off = (Vector2)(d.Wake.transform.position - f.tr.position);
                trails &= d.Wake.enabled && Vector2.Dot(off, dir) < -.5f * off.magnitude &&
                          Mathf.Abs(Mathf.DeltaAngle(d.Wake.transform.eulerAngles.z, f.heading * Mathf.Rad2Deg - 90f)) < .5f;
            }
            Check(who + "the hull spins at gameplay's rate while cruising (" + total + " deg in 1 s, off by " + worst + ")",
                  worst < .1f && Mathf.Abs(total - gameplay * 24f * step) < 1f);
            Check(who + "the ring stays in sync with the spinning hull (" + ringOff + " deg)", ringOff < .05f);
            Check(who + "the wake trails the travel heading while the hull spins", trails);

            // boosting: same rate (gameplay's spin doesn't change on a boost)
            t.StartBoost(f);
            worst = 0f; ringOff = 0f;
            int steps = 0;
            while (f.state == TitleScreenTraffic.State.Boost && steps++ < 200)
            {
                float z0 = f.tr.eulerAngles.z;
                t.Step(step);
                worst = Mathf.Max(worst, Mathf.Abs(Mathf.DeltaAngle(z0, f.tr.eulerAngles.z) - gameplay * step));
                ringOff = Mathf.Max(ringOff, Quaternion.Angle(d.Ring.transform.rotation, f.tr.rotation));
            }
            Check(who + "keeps spinning at gameplay's rate through a boost (off by " + worst + ")", steps > 10 && worst < .1f);
            Check(who + "the ring stays in sync through the boost", ringOff < .05f);

            // a dock-style zip: same rate, ring in sync, wake behind the flight line
            f.pos = at; f.heading = Mathf.PI * .5f;
            t.StartZip(f);
            worst = 0f; ringOff = 0f; trails = true;
            steps = 0;
            while (f.active && f.state == TitleScreenTraffic.State.ZipOut && steps++ < 400)
            {
                float z0 = f.tr.eulerAngles.z;
                t.Step(step);
                if (!f.active) break;
                worst = Mathf.Max(worst, Mathf.Abs(Mathf.DeltaAngle(z0, f.tr.eulerAngles.z) - gameplay * step));
                ringOff = Mathf.Max(ringOff, Quaternion.Angle(d.Ring.transform.rotation, f.tr.rotation));
                Vector2 dir = new Vector2(Mathf.Cos(f.heading), Mathf.Sin(f.heading));
                trails &= Vector2.Dot((Vector2)(d.Wake.transform.position - f.tr.position), dir) < 0f;
            }
            Check(who + "spins at gameplay's rate through a dock-style zip (off by " + worst + ")", steps > 10 && worst < .1f);
            Check(who + "ring in sync and wake trailing through the zip", ringOff < .05f && trails);
            if (f.active) { f.active = false; f.go.SetActive(false); }

            // a dizzy tumble adds to the spin, and shaking it off doesn't snap back
            g = Place(t, TitleScreenTraffic.Depth.Mid, at, .3f);
            guard = 0;
            while (g != f && guard++ < 400)
            {
                if (g != null) { g.active = false; g.go.SetActive(false); }
                g = Place(t, TitleScreenTraffic.Depth.Mid, at, .3f);
            }
            if (g != f) continue;
            t.Step(step);
            f.state = TitleScreenTraffic.State.Dizzy; f.stateT = 0f; f.spinVel = 600f; f.tumble = Vector2.zero; f.pendingPop = false;
            float zd = f.tr.eulerAngles.z;
            t.Step(step * .25f);
            float dizzyAdv = Mathf.DeltaAngle(zd, f.tr.eulerAngles.z);
            Check(who + "a dizzy tumble adds to the spin (" + dizzyAdv + " deg)", dizzyAdv > gameplay * step * .25f + 1f);
            steps = 0;
            while (f.state == TitleScreenTraffic.State.Dizzy && steps++ < 200) t.Step(step);
            float zj = f.tr.eulerAngles.z;
            t.Step(.001f);
            Check(who + "shaking it off doesn't snap the spin back", Mathf.Abs(Mathf.DeltaAngle(zj, f.tr.eulerAngles.z)) < 2f);
            f.active = false; f.go.SetActive(false);
        }
        Check("both spinners were spun", spinners == 2);

        // everyone else faces their heading (give or take a lean) and doesn't spin
        bool still = true; int seen = 0;
        foreach (var f in t.Pool)
        {
            if (f.drift != null || seen >= 3) continue;
            var g = Place(t, TitleScreenTraffic.Depth.Mid, at, .3f);
            if (g == null) break;
            if (g.drift != null) { g.active = false; g.go.SetActive(false); continue; }
            seen++;
            for (int i = 0; i < 24; i++)
            {
                t.Step(step);
                if (!g.active) break;
                still &= Mathf.Abs(Mathf.DeltaAngle(g.tr.eulerAngles.z, g.heading * Mathf.Rad2Deg - 90f)) < 13f;
            }
            g.active = false; g.go.SetActive(false);
        }
        Check("non-spinners don't spin: they face their heading (" + seen + " checked)", seen > 0 && still);
        Done(t);
    }

    // A traffic spinner's drift vs a ship dressed by gameplay for the same id
    // (ApplyHull, then ShipSpinDrift.Rebuild). Null when they match.
    static string DriftDiff(TitleScreenTraffic.Flyer f)
    {
        if (f.drift == null) return ShipId.KeyOf(f.id) + " has no drift";
        var reference = new GameObject("~TT_driftref");
        reference.AddComponent<SpriteRenderer>();
        try
        {
            spawnShips.ApplyHull(reference, f.id);
            var want = reference.AddComponent<ShipSpinDrift>();
            want.Rebuild();
            var got = f.drift;
            string k = ShipId.KeyOf(f.id) + ": ";
            if (got.ShipIndex != f.id || want.ShipIndex != f.id) return k + "ship index " + got.ShipIndex;
            if (got.Ring == null || got.Wake == null) return k + "missing ring/wake";
            if (got.Ring.color != want.Ring.color || got.Wake.color != want.Wake.color) return k + "tint";
            Texture gr = got.Ring.sprite != null ? got.Ring.sprite.texture : null, wr = want.Ring.sprite != null ? want.Ring.sprite.texture : null;
            Texture gw = got.Wake.sprite != null ? got.Wake.sprite.texture : null, ww = want.Wake.sprite != null ? want.Wake.sprite.texture : null;
            if (gr == null || gr != wr || gw != ww) return k + "art";
            if ((got.Ring.transform.localScale - want.Ring.transform.localScale).sqrMagnitude > 1e-8f) return k + "ring size";
            if ((got.Wake.transform.localScale - want.Wake.transform.localScale).sqrMagnitude > 1e-8f) return k + "wake size";
            return null;
        }
        finally { Object.DestroyImmediate(reference); }
    }

    static TitleScreenTraffic.Flyer Place(TitleScreenTraffic t, TitleScreenTraffic.Depth layer, Vector2 at, float heading)
    {
        var f = t.Launch(layer, true, false, null);
        if (f == null) return null;
        f.pos = at;
        f.heading = heading;
        f.waypoint = at + new Vector2(Mathf.Cos(heading), Mathf.Sin(heading)) * 3f;
        f.state = TitleScreenTraffic.State.Cruise;
        f.trick = TitleScreenTraffic.Trick.None;
        f.nextBoost = f.nextTrick = 1e9f;
        return f;
    }

    static void CrashesOnlyWithinALayerAndRateLimited()
    {
        var t = Make("~TT_crash", 7, x => { x.layerTargets = new[] { 0, 0, 0 }; x.zoomInterval = new Vector2(1e6f, 1e6f); });
        t.NextEliteAt = 1e9f;   // no elite snipe run mid-test (TitleScreenEliteTest covers it)
        t.NextCrashAt = 0f;
        Vector2 site = new Vector2(t.Safe.center.x + .6f, t.Safe.yMin + 1.6f);

        var a = Place(t, TitleScreenTraffic.Depth.Back, site, 0f);
        var b = Place(t, TitleScreenTraffic.Depth.Mid, site, Mathf.PI);
        t.Step(.001f);
        Check("ships on different layers pass through each other", t.Crashes == 0 && a.active && b.active &&
              a.state != TitleScreenTraffic.State.Dizzy && b.state != TitleScreenTraffic.State.Dizzy);

        var c = Place(t, TitleScreenTraffic.Depth.Front, site, 0f);
        t.Step(.001f);
        Check("nor do back/mid and front", t.Crashes == 0);
        a.pos = site + new Vector2(0f, 3f); c.pos = site + new Vector2(0f, -3f);

        int explosionsBefore = t.Explosions;
        var d = Place(t, TitleScreenTraffic.Depth.Mid, site + new Vector2(.05f, 0f), 0f);
        t.Step(.001f);
        Check("two ships on the same layer crash", t.Crashes == 1 && t.CrashLayer(0) == (int)TitleScreenTraffic.Depth.Mid);
        Check("no cross-layer crash was ever recorded", t.CrossLayerCrashes == 0);
        bool oneGone = !b.active || !d.active;
        bool otherReacted = (!b.active && !d.active) || (b.active ? b.state : d.state) == TitleScreenTraffic.State.Dizzy;
        Check("one explodes and is despawned; the other is despawned too or tumbles off dizzy", oneGone && otherReacted);

        var boom = t.LastExplosion;
        Check("the crash plays an explosion", t.Explosions > explosionsBefore && boom != null);
        if (boom != null)
        {
            Check("from the WeaponFx pool", boom.transform.parent != null && boom.transform.parent.name == "~WeaponFx");
            Check("as the cel explosion flipbook", boom.CurrentMode == FlipbookFx.Mode.Explosion && boom.Active);
            Check("using the metal variant", boom.CurrentSprite == WeaponArt.Explosion(TargetExplosion.Kind.Metal, boom.Frame));
            float want = TargetExplosion.WorldSizeFor(TargetExplosion.Size.Small, TargetExplosion.Kind.Metal) * TitleScreenTraffic.Depths[1].scale;
            Check("sized as a Small metal blast scaled to the layer (" + boom.transform.localScale.x + ")",
                  Mathf.Abs(boom.transform.localScale.x - want) < want * .12f);
            Check("sorted on the layer's fx band", boom.GetComponent<SpriteRenderer>().sortingOrder ==
                  TitleScreenTraffic.Depths[1].fxSort + 3);
            Check("driven on unscaled time by the traffic, not drifting with the game world", !boom.enabled && t.TrackedFx > 0);
            Vector3 p = boom.transform.position;
            float savedSpeed = moveBackGround.speed;
            moveBackGround.speed = .5f;
            t.Step(.1f);
            moveBackGround.speed = savedSpeed;
            Check("the explosion stays where the ships met", (boom.transform.position - p).sqrMagnitude < 1e-6f);
        }

        float gap = t.NextCrashAt - t.Now;
        Check("the next crash is 6-12 s away (" + gap.ToString("0.0") + ")", gap >= 5.9f && gap <= 12.1f);

        var e = Place(t, TitleScreenTraffic.Depth.Back, site + new Vector2(-1.2f, 0f), 0f);
        var g = Place(t, TitleScreenTraffic.Depth.Back, site + new Vector2(-1.15f, 0f), 0f);
        t.Step(.001f);
        Check("rate-limited: an overlap during the cooldown is just a near miss", t.Crashes == 1);

        // crashes never happen over the logo
        e.pos = site + new Vector2(-2f, 2.5f); g.pos = site + new Vector2(2f, -2.5f);
        t.NextCrashAt = t.Now;
        var logo = t.LogoRect;
        if (logo.width > 0f)
        {
            var h = Place(t, TitleScreenTraffic.Depth.Mid, logo.center, 0f);
            var k = Place(t, TitleScreenTraffic.Depth.Mid, logo.center, Mathf.PI);
            int crashes = t.Crashes;
            t.Step(.001f);
            Check("no crash site on the logo", t.Crashes == crashes);
        }

        // the dizzy beat resolves (no fresh crashes meanwhile)
        t.NextCrashAt = 1e9f;
        for (int i = 0; i < 90; i++) t.Step(Dt);
        bool resolved = true;
        foreach (var f in t.Pool) resolved &= !f.active || f.state != TitleScreenTraffic.State.Dizzy;
        Check("dizzy ships pop or recover within a couple of seconds", resolved);
        Done(t);
    }

    static void LongRunStaysBoundedAndLively()
    {
        var t = Make("~TT_long", 1234);
        var logo = Logo();
        Vector3 lp = logo != null ? logo.transform.position : Vector3.zero;
        bool capped = true, pools = true;
        int minPop = int.MaxValue; long popSum = 0; int steps = 0;
        int flipbookPeak = 0;
        const float Seconds = 600f;
        int frames = 0;
        for (float s = 0f; s < Seconds; s += Dt)
        {
            t.Step(Dt);
            if (++frames % TestHarness.FlushEvery == 0) TestHarness.FlushGpu();
            int n = t.ActiveCount;
            capped &= n <= t.maxShips && n <= TitleScreenTraffic.MaxCap;
            pools &= t.TrackedFx <= TitleScreenTraffic.FxTrack;
            flipbookPeak = Mathf.Max(flipbookPeak, WeaponFx.FlipbookPoolSize);
            if (s > 10f && !EliteWindow(t)) { minPop = Mathf.Min(minPop, n); popSum += n; steps++; }
        }
        Check("the elite snipe runs went off (" + t.EliteEvents + " runs, " + t.EliteSnipes + " sniped)", t.EliteEvents >= 10);
        float avgPop = steps > 0 ? popSum / (float)steps : 0f;
        Check("population never exceeds the cap (" + t.maxShips + ")", capped);
        Check("population stays steady (avg " + avgPop.ToString("0.0") + ", min " + minPop + ")", avgPop >= 12f && minPop >= 8);   // x1.4 sky (was avg >= 8, min >= 5)
        Check("ship pool is fixed (roster + twins)", t.Pool.Length == TitleScreenTraffic.PoolSize);
        Check("trail / fx tracking pools stay bounded", pools && t.Trails.Length == TitleScreenTraffic.TrailPool);
        Check("WeaponFx flipbook pool stays small (peak " + flipbookPeak + ")", flipbookPeak <= 16 && flipbookPeak <= WeaponFx.MaxFlipbooks);

        Check("crashes happened (" + t.Crashes + " in " + Seconds + " s)", t.Crashes >= 30);
        Check("never across layers", t.CrossLayerCrashes == 0);
        int logged = Mathf.Min(t.Crashes, TitleScreenTraffic.CrashLog);
        float minGap = float.MaxValue, sumGap = 0f;
        int first = t.Crashes - logged;
        for (int i = first + 1; i < t.Crashes; i++)
        {
            float g = t.CrashTime(i) - t.CrashTime(i - 1);
            minGap = Mathf.Min(minGap, g); sumGap += g;
        }
        float meanGap = logged > 1 ? sumGap / (logged - 1) : 0f;
        Check("crashes are rate-limited (min gap " + minGap.ToString("0.0") + " s >= 6)", minGap >= t.crashInterval.x - .01f);
        Check("about one every 6-12 s (mean gap " + meanGap.ToString("0.0") + " s)", meanGap >= 6f && meanGap <= 12.5f);
        Check("every layer gets crashes", CrashLayers(t, 0) > 0 && CrashLayers(t, 1) > 0);
        Check("zoomers streak by (" + t.Zooms + ")", t.Zooms >= 20);
        Check("ships zip off dock-style (" + t.Zips + ")", t.Zips >= 40);
        Check("ships boost (" + t.Boosts + ")", t.Boosts >= 40);
        Check("formation fly-bys happen (" + t.Formations + ")", t.Formations >= 10);
        Check("loops and barrel rolls happen (" + t.Loops + " / " + t.Rolls + ")", t.Loops >= 10 && t.Rolls >= 10);
        Check("dizzy beats happen (" + t.DizzyBeats + ")", t.DizzyBeats >= 5);
        for (int i = 0; i < 30 && t.LogoShaking; i++) t.Step(Dt);
        Check("logo back exactly where it was after the long run (it only shakes on a crash)", logo == null || logo.transform.position == lp);
        Done(t);
    }

    // The home sky holds DensityScale (x1.4) times the ships it used to. The
    // old defaults (cap 12, cruisers 5/4/2) are rebuilt here by hand as the
    // baseline; the same seeds fly both, so the ratio is a fair comparison.
    static void DensityIsFortyPercentUp()
    {
        var oldLayers = new[] { 5, 4, 2 };
        float baseAvg = 0f, newAvg = 0f; int basePeak = 0, newPeak = 0;
        const int Runs = 3;
        for (int run = 0; run < Runs; run++)
        {
            for (int pass = 0; pass < 2; pass++)
            {
                var t = Make("~TT_density" + pass, 500 + run, pass == 0
                    ? (System.Action<TitleScreenTraffic>)(x => { x.maxShips = 12; x.layerTargets = (int[])oldLayers.Clone(); })
                    : null);
                long sum = 0; int steps = 0, peak = 0;
                int frames = 0;
                for (float s = 0f; s < 300f; s += Dt)
                {
                    t.Step(Dt);
                    if (++frames % TestHarness.FlushEvery == 0) TestHarness.FlushGpu();
                    int n = t.ActiveCount;
                    if (s > 10f && !EliteWindow(t)) { sum += n; steps++; peak = Mathf.Max(peak, n); }
                }
                float avg = sum / (float)steps;
                if (pass == 0) { baseAvg += avg / Runs; basePeak = Mathf.Max(basePeak, peak); }
                else
                {
                    newAvg += avg / Runs; newPeak = Mathf.Max(newPeak, peak);
                    if (run == 0)
                    {
                        Check("defaults are the old ones scaled by DensityScale (cap " + t.maxShips + ", cruisers " +
                              string.Join("/", t.layerTargets) + ")",
                              t.maxShips == TitleScreenTraffic.Scaled(12) && t.layerTargets.Length == 3 &&
                              t.layerTargets[0] == TitleScreenTraffic.Scaled(5) && t.layerTargets[1] == TitleScreenTraffic.Scaled(4) &&
                              t.layerTargets[2] == TitleScreenTraffic.Scaled(2));
                        Check("the cap and the pool hold the busier sky (cap " + t.maxShips + " <= MaxCap " + TitleScreenTraffic.MaxCap +
                              " <= pool " + t.Pool.Length + ")",
                              t.maxShips <= TitleScreenTraffic.MaxCap && TitleScreenTraffic.MaxCap <= t.Pool.Length);
                    }
                }
                Done(t);
            }
        }
        float ratio = newAvg / baseAvg;
        Debug.Log("[TT] density: old avg " + baseAvg.ToString("0.00") + " peak " + basePeak + ", new avg " + newAvg.ToString("0.00") +
                  " peak " + newPeak + ", ratio " + ratio.ToString("0.000"));
        Check("average concurrent ships is ~1.4x the old sky (" + baseAvg.ToString("0.0") + " -> " + newAvg.ToString("0.0") +
              ", x" + ratio.ToString("0.00") + ", band 1.30-1.50)", ratio >= 1.30f && ratio <= 1.50f);
        Check("peak concurrent ships rose (" + basePeak + " -> " + newPeak + ")", newPeak > basePeak);
    }

    static int CrashLayers(TitleScreenTraffic t, int layer)
    {
        int n = 0, logged = Mathf.Min(t.Crashes, TitleScreenTraffic.CrashLog);
        for (int i = t.Crashes - logged; i < t.Crashes; i++) if (t.CrashLayer(i) == layer) n++;
        return n;
    }

    static void SafeAreaAndAspectRatios()
    {
        // (aspect w/h, safe-area insets as fractions: top, bottom)
        float[][] shapes =
        {
            new[] { 9f / 16f, 0f, 0f },
            new[] { 1170f / 2532f, .056f, .04f },   // notch
            new[] { 1080f / 2520f, .038f, .02f },   // tall
            new[] { 1080f / 2880f, .033f, .02f },   // 9:24
            new[] { 968f / 2376f, .038f, .02f },    // Z Fold cover
            new[] { 3f / 4f, .02f, .02f },          // tablet
        };
        bool sitesSafe = true, offLogo = true, populated = true;
        foreach (var sh in shapes)
        {
            var t = Make("~TT_shape", 99);
            float halfH = 5f, halfW = halfH * sh[0];
            if (halfW < 2.85f) { halfW = 2.85f; halfH = halfW / sh[0]; }   // CameraFit's floor
            var view = new Rect(-halfW, -halfH, 2f * halfW, 2f * halfH);
            var safe = new Rect(view.x, view.y + view.height * sh[2], view.width, view.height * (1f - sh[1] - sh[2]));
            t.PinGeometry(view, safe);
            int pop = 0, samples = 0;
            for (int i = 0; i < 30 * 120; i++)
            {
                t.Step(Dt);
                if (i % 30 == 0 && !EliteWindow(t)) { pop += t.ActiveCount; samples++; }
            }
            int logged = Mathf.Min(t.Crashes, TitleScreenTraffic.CrashLog);
            for (int i = t.Crashes - logged; i < t.Crashes; i++)
            {
                Vector2 p = t.CrashSite(i);
                sitesSafe &= safe.Contains(p);
                offLogo &= !t.LogoRect.Contains(p);
            }
            populated &= t.Crashes > 0 && pop / (float)samples >= 10f;
            Done(t);
        }
        Check("at every aspect ratio, crashes land inside the safe area", sitesSafe);
        Check("and never on the logo", offLogo);
        Check("and the sky stays populated with crashes happening", populated);
    }

    static void NeverInterceptsUiRaycasts()
    {
        var t = Make("~TT_ray", 21);
        for (int i = 0; i < 30 * 40; i++) t.Step(Dt);   // trails, debris, stars all used
        bool layer = true, noColliders = true, noUi = true;
        foreach (var tr in t.GetComponentsInChildren<Transform>(true))
        {
            layer &= tr.gameObject.layer == 2;
            noColliders &= tr.GetComponent<Collider2D>() == null && tr.GetComponent<Collider>() == null;
            noUi &= tr.GetComponent<Graphic>() == null && tr.GetComponent<Canvas>() == null &&
                    tr.GetComponent<BaseRaycaster>() == null;
        }
        Check("every traffic object is on Ignore Raycast", layer);
        Check("no colliders anywhere in the traffic (nothing for a physics raycaster to hit)", noColliders);
        Check("no UI graphics, canvases or raycasters in the traffic", noUi);
        Check("traffic is not parented under the menu canvas", t.GetComponentInParent<Canvas>() == null);

        var wfx = GameObject.Find("~WeaponFx");
        bool fxOk = true;
        if (wfx != null)
            foreach (var c in wfx.GetComponentsInChildren<Collider2D>(true)) fxOk = false;
        Check("pooled explosions carry no colliders", fxOk);

        // the menu's buttons are a screen-space overlay; they keep raycast targets
        var codex = GameObject.Find("CodexButton");
        var credits = GameObject.Find("CreditsButton");
        var play = GameObject.Find("PlayButton");
        bool buttons = true;
        foreach (var b in new[] { codex, credits, play })
        {
            if (b == null) continue;
            var canvas = b.GetComponentInParent<Canvas>();
            buttons &= canvas != null && canvas.renderMode == RenderMode.ScreenSpaceOverlay;
        }
        Check("menu buttons (Play, Credits, Codex) sit on an overlay canvas above all world sprites", buttons);
        Done(t);
    }

    static void NoPerFrameAllocations()
    {
        // a full sky (17: all 15 hulls and the twins), so at least one spinner and its
        // drift is always in the air during the measured window
        var t = Make("~TT_alloc", 77, x => { x.maxShips = TitleScreenTraffic.MaxCap; x.layerTargets = new[] { 8, 7, 4 }; });
        // warm up: every pool grown, every effect seen at least once
        for (int i = 0; i < 30 * 150; i++) t.Step(Dt);
        int spinners = 0;
        foreach (var f in t.Pool) if (f.active && f.drift != null) spinners++;
        Check("a spinner is flying during the allocation window (" + spinners + ")", spinners > 0);
        // the skin wardrobe's scheduled sheet decodes are measured apart
        // (TitleScreenCombatTest skips those frames); everything else here
        t.SkinWorkPaused = true;
        System.GC.Collect();
        long before = System.GC.GetTotalMemory(false);
        for (int i = 0; i < 900; i++) t.Step(Dt);
        long allocated = System.GC.GetTotalMemory(false) - before;
        long probe = System.GC.GetTotalMemory(false);
        var garbage = new byte[4096];
        Check("the allocation counter actually counts (" + garbage.Length + " byte probe)",
              System.GC.GetTotalMemory(false) - probe >= 4096);
        Check("Step allocates nothing per frame (" + allocated + " bytes over 900 frames, " +
              t.Crashes + " crashes so far)", allocated <= 0);
        Done(t);
    }

    static void LogoUntouched()
    {
        Check("pause_title_2.png is byte-identical", Sha256(LogoPath) == LogoSha256);
        Check("pause_title_2.png.meta is byte-identical", Sha256(LogoPath + ".meta") == LogoMetaSha256);
        var logo = Logo();
        Check("the logo is in startS4", logo != null);
        if (logo == null) return;

        var t = Make("~TT_logo", 5);
        for (int i = 0; i < 30 * 60; i++) t.Step(Dt);
        Done(t);

        var guid = AssetDatabase.AssetPathToGUID(LogoPath);
        Check("logo renderer still shows pause_title_2", logo.sprite != null &&
              AssetDatabase.GetAssetPath(logo.sprite) == LogoPath && guid == "a2e075ab7763def46a6d6d3587b47678");
        Check("logo position unchanged", (logo.transform.position - LogoPos).sqrMagnitude < 1e-10f);
        Check("logo scale unchanged", (logo.transform.localScale - LogoScale).sqrMagnitude < 1e-10f);
        Check("logo colour unchanged", logo.color == Color.white);
        Check("logo sorting unchanged", logo.sortingOrder == 0 && logo.sortingLayerID == 0 && logo.enabled);
        Check("logo rotation unchanged", logo.transform.rotation == Quaternion.identity);
    }

    // The Bile Mite (roster space_alien) used to drift up the home screen as a
    // scene object. It is gone from the home screen; gameplay is untouched.
    static void NoBileMiteOnHomeScreen()
    {
        Check("startS4 has no TitleAlien object", GameObject.Find("TitleAlien") == null);
        var scene = File.ReadAllText("Assets/Scenes/startS4.unity");
        Check("startS4 does not reference the Bile Mite (no TitleAlien, no EnemyFlipbook, no alien1)",
              !scene.Contains("m_Name: TitleAlien") && !scene.Contains("TitleScreenAlien") &&
              !System.Text.RegularExpressions.Regex.IsMatch(scene, @"m_Name: alien1\b"));
        Check("no home traffic or combat code picks the roster alien",
              !File.ReadAllText("Assets/Scripts/UI/TitleScreenTraffic.cs").Contains("space_alien") &&
              !File.ReadAllText("Assets/Scripts/UI/TitleScreenTraffic.Combat.cs").Contains("space_alien") &&
              !File.ReadAllText("Assets/Scripts/UI/TitleScreenTraffic.Elite.cs").Contains("space_alien"));
        Check("the Bile Mite still exists in the gameplay roster", EnemyRoster.Find("space_alien") != null);
        var t = Object.FindFirstObjectByType<TitleScreenTraffic>();
        bool mite = false;
        if (t != null)
            foreach (var r in Object.FindObjectsByType<SpriteRenderer>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (r.GetComponent<EnemyFlipbook>() != null) mite = true;
        Check("no roster flipbook enemy sits in the home scene", !mite);
    }

    static string Sha256(string path)
    {
        if (!File.Exists(path)) return "";
        using var sha = System.Security.Cryptography.SHA256.Create();
        var bytes = sha.ComputeHash(File.ReadAllBytes(path));
        var sb = new System.Text.StringBuilder();
        foreach (var b in bytes) sb.Append(b.ToString("x2"));
        return sb.ToString();
    }
}
