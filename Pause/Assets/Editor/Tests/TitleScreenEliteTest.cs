using System.Collections.Generic;
using System.IO;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

// Home screen: the elite sniper (every ~40 s an enemy elite flies in, snipes
// every ship on the screen and zooms away; the traffic comes back about a
// second later) and the PAUSE logo's shake when a ship crashes into it.
//
// Driven through TitleScreenTraffic.Step(dt) on a fixed step and a seeded
// Random, like TitleScreenTrafficTest / TitleScreenCombatTest.
public static class TitleScreenEliteTest
{
    static int fails;
    static void Check(string what, bool ok)
    {
        Debug.Log((ok ? "[TE] PASS  " : "[TE] FAIL  ") + what);
        if (!ok) fails++;
    }

    const float Dt = 1f / 30f;
    static readonly Vector3 LogoPos = new Vector3(-0.04f, 2.72f, 0f);
    static readonly Vector3 LogoScale = new Vector3(0.4176109f, 0.46091294f, 1f);

    public static void Run() { TestHarness.Exit(Execute()); }

    public static int Execute()
    {
        fails = 0;
        using var sandbox = new TestHarness.Sandbox();
        EditorSceneManager.OpenScene("Assets/Scenes/startS4.unity", OpenSceneMode.Single);
        var panel = GameObject.Find("UIPanel");
        var scaler = panel != null ? panel.GetComponentInParent<CanvasScaler>() : null;
        if (scaler != null)
            typeof(CanvasScaler).GetMethod("Handle", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                ?.Invoke(scaler, null);
        Canvas.ForceUpdateCanvases();
        var panelRt = panel != null ? (RectTransform)panel.transform : null;
        if (panelRt != null && panelRt.rect.height <= 0f) panelRt.sizeDelta = new Vector2(panelRt.sizeDelta.x, -300f);
        if (panel != null) LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)panel.transform);
        Canvas.ForceUpdateCanvases();

        RunsOnScheduleWithRandomElites();
        SnipesEveryShipThenLeavesAndTrafficReturns();
        ShipsArrivingMidRunAreSnipedToo();
        EmptySkyIsAFlyby();
        NeverOverlapsOtherShows();
        EveryAspectAndARotationMidRun();
        IgnoresFrozenTimeScale();
        LeavingMidRunLeavesNothingBehind();
        Deterministic();
        NoGameplaySideEffects();
        NoAllocationsDuringARun();
        LogoShakesOnACrashAndSettlesExactly();
        LogoShakeRetriggerIsCapped();
        SnipedShipsDoNotShakeTheLogo();

        Debug.Log("[TE] failures: " + fails);
        return fails;
    }

    // ------------------------------------------------------------------ helpers

    static TitleScreenTraffic Make(string name, int seed, System.Action<TitleScreenTraffic> configure = null)
    {
        Random.InitState(seed);
        var go = new GameObject(name);
        var t = go.AddComponent<TitleScreenTraffic>();
        configure?.Invoke(t);
        t.Init();
        return t;
    }

    static void Done(TitleScreenTraffic t) { t.Shutdown(); Object.DestroyImmediate(t.gameObject); TestHarness.FlushGpu(); }

    static int frames;
    static void Step(TitleScreenTraffic t)
    {
        t.Step(Dt);
        if (++frames % TestHarness.FlushEvery == 0) TestHarness.FlushGpu();
    }

    static void Run(TitleScreenTraffic t, float seconds) { for (float s = 0f; s < seconds; s += Dt) Step(t); }

    // Steps until cond or the guard runs out; false on timeout.
    static bool RunUntil(TitleScreenTraffic t, System.Func<bool> cond, float guard)
    {
        for (float s = 0f; s < guard; s += Dt)
        {
            if (cond()) return true;
            Step(t);
        }
        return cond();
    }

    static TitleScreenTraffic Quiet(string name, int seed)
    {
        var t = Make(name, seed, x =>
        {
            x.layerTargets = new[] { 0, 0, 0 };
            x.zoomInterval = new Vector2(1e6f, 1e6f);
            x.formationInterval = new Vector2(1e6f, 1e6f);
            x.plungeInterval = new Vector2(1e6f, 1e6f);
        });
        t.NextCrashAt = 1e9f;
        t.NextPlungeAt = 1e9f;
        t.NextZoomAt = 1e9f;
        t.NextEliteAt = 1e9f;
        foreach (var f in t.Pool) if (f.active) { f.active = false; f.go.SetActive(false); }
        return t;
    }

    static TitleScreenTraffic.Flyer LaunchId(TitleScreenTraffic t, int id, TitleScreenTraffic.Depth layer, Vector2 at, float heading)
    {
        TitleScreenTraffic.Flyer target = null;
        foreach (var f in t.Pool) if (f.id == id && !f.twin) target = f;
        if (target == null) return null;
        if (target.active) { target.active = false; target.go.SetActive(false); }
        TitleScreenTraffic.Flyer g = null;
        for (int guard = 0; guard < 400 && g != target; guard++)
        {
            g = t.Launch(layer, true, false, null);
            if (g != null && g != target) { g.active = false; g.go.SetActive(false); }
        }
        if (g != target) return null;
        g.pos = at;
        g.heading = heading;
        g.waypoint = at + new Vector2(Mathf.Cos(heading), Mathf.Sin(heading)) * 3f;
        g.waypointsLeft = 0;
        g.state = TitleScreenTraffic.State.Cruise;
        g.trick = TitleScreenTraffic.Trick.None;
        g.nextBoost = g.nextTrick = 1e9f;
        g.ultAt = float.MaxValue;
        g.wobA = 0f;
        return g;
    }

    static SpriteRenderer Logo() { var go = GameObject.Find("menuTitle"); return go != null ? go.GetComponent<SpriteRenderer>() : null; }

    static bool LogoAtRest(SpriteRenderer logo)
    {
        return logo == null || (logo.transform.position == LogoPos && logo.transform.rotation == Quaternion.identity &&
                                (logo.transform.localScale - LogoScale).sqrMagnitude < 1e-10f);
    }

    static int EliteObjects()
    {
        int n = 0;
        foreach (var tr in Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (tr.name.StartsWith("~elite")) n++;
        return n;
    }

    // ------------------------------------------------------------------ cases

    static void RunsOnScheduleWithRandomElites()
    {
        var t = Make("~TE_sched", 4040);
        float first = t.NextEliteAt;
        Check("the first run is after a calm start (" + first.ToString("0.0") + " s, " + t.eliteFirst + ")",
              first >= t.eliteFirst.x && first <= t.eliteFirst.y && first >= 20f);
        Check("the elite defs are all there (" + t.EliteDefCount + ")", t.EliteDefCount == EliteCatalog.All.Length && t.EliteDefCount >= 4);
        Check("the first run's elite is picked and its strip loaded ahead of time",
              t.NextEliteDef != null && EliteArt.Frames(t.NextEliteDef) != null);
        var starts = new List<float>();
        var keys = new HashSet<string>();
        int events = 0;
        bool calmBefore = true;
        for (float s = 0f; s < 420f; s += Dt)
        {
            Step(t);
            if (t.EliteEvents != events)
            {
                events = t.EliteEvents;
                starts.Add(t.LastEliteStartAt);
                if (t.EliteDefNow != null) keys.Add(t.EliteDefNow.key);
            }
            if (events == 0 && t.Now < t.eliteFirst.x) calmBefore &= !t.EliteBusy;
        }
        float minGap = float.MaxValue, maxGap = 0f;
        for (int i = 1; i < starts.Count; i++) { float g = starts[i] - starts[i - 1]; minGap = Mathf.Min(minGap, g); maxGap = Mathf.Max(maxGap, g); }
        Debug.Log("[TE] runs at " + string.Join(", ", starts.ConvertAll(x => x.ToString("0.0"))) + "; elites " + string.Join(", ", keys));
        Check("no run during the calm start", calmBefore && starts.Count > 0 && starts[0] >= t.eliteFirst.x - .01f);
        Check("the first run goes off on its schedule (" + (starts.Count > 0 ? starts[0].ToString("0.0") : "-") + " vs " + first.ToString("0.0") + ")",
              starts.Count > 0 && starts[0] >= first - .01f && starts[0] <= first + 3f);
        Check("runs come every ~40 s (" + starts.Count + " in 420 s, gaps " + minGap.ToString("0.0") + "-" + maxGap.ToString("0.0") + ")",
              starts.Count >= 8 && starts.Count <= 11 && minGap >= t.eliteInterval.x - .01f && maxGap <= t.eliteInterval.y + 3f);
        Check("a random elite each run (" + keys.Count + " different)", keys.Count >= 3);
        Check("it snipes ships (" + t.EliteSnipes + ")", t.EliteSnipes >= starts.Count * 4);
        Done(t);
    }

    static void SnipesEveryShipThenLeavesAndTrafficReturns()
    {
        var t = Make("~TE_snipe", 77);
        t.NextEliteAt = 1e9f;
        Run(t, 12f);
        var logo = Logo();
        Check("a full sky before the run (" + t.ShipsOnScreen + " on screen)", t.ShipsOnScreen >= 8);
        while (!t.TryStartElite()) Step(t);
        var def = t.EliteDefNow;
        var frameSet = new HashSet<Sprite>(EliteArt.Frames(def));
        bool drawsOwn = true, sight = false, visible = false;
        var hull = t.transform.Find("~elite/~elite_hull").GetComponent<SpriteRenderer>();
        RunUntil(t, () =>
        {
            visible |= t.EliteVisible;
            drawsOwn &= !hull.enabled || frameSet.Contains(hull.sprite);
            return t.ElitePhaseNow == TitleScreenTraffic.ElitePhase.Snipe;
        }, 3f);
        Check("the elite flies in from off-screen to its perch", t.ElitePhaseNow == TitleScreenTraffic.ElitePhase.Snipe && t.Safe.Contains(t.ElitePosition));
        var marked = new List<TitleScreenTraffic.Flyer>();
        foreach (var f in t.Pool) if (f.active && t.View.Contains(f.pos)) marked.Add(f);
        int snipes0 = t.EliteSnipes, booms0 = t.Explosions + t.Wrecks;
        int crashes0 = t.Crashes, plunges0 = t.Plunges, ults0 = t.UltsFired, flights0 = t.Flights;
        bool ok = RunUntil(t, () =>
        {
            sight |= t.EliteSightShown;
            drawsOwn &= !hull.enabled || frameSet.Contains(hull.sprite);
            return t.ElitePhaseNow != TitleScreenTraffic.ElitePhase.Snipe;
        }, TitleScreenTraffic.EliteSnipeMax + 1f);
        int left = t.ShipsOnScreen;
        bool allGone = true;
        foreach (var f in marked) allGone &= !f.active;
        int sniped = t.EliteSnipes - snipes0;
        Debug.Log("[TE] " + def.key + ": " + marked.Count + " ships on screen, " + sniped + " sniped, " + t.EliteShotsFired + " shots");
        Check(def.key + " draws only its own strip (EliteArt)", drawsOwn && visible);
        Check("it paints each mark with a sight line first", sight);
        Check("it snipes every ship on the screen (" + sniped + " of " + marked.Count + ", " + left + " left)",
              ok && left == 0 && allGone && sniped >= marked.Count - 2 && sniped > 0);
        Check("each snipe blows the ship apart (explosions / wrecks " + (t.Explosions + t.Wrecks - booms0) + ")", t.Explosions + t.Wrecks - booms0 >= sniped);
        Check("nothing else happens meanwhile (no crashes, dives, ultimates or spawns)",
              t.Crashes == crashes0 && t.Plunges == plunges0 && t.UltsFired == ults0 && t.Flights == flights0);

        ok = RunUntil(t, () => t.ElitePhaseNow == TitleScreenTraffic.ElitePhase.Return, TitleScreenTraffic.EliteExitMax + 1f);
        var v = t.View;
        Vector2 p = t.ElitePosition;
        Check("then it zooms away off the screen", ok && !t.EliteVisible && !v.Contains(p));
        Check("leaving the sky empty", t.ActiveCount == 0 && t.ActiveEliteShots == 0);
        Check("and the logo never moved", LogoAtRest(logo));
        float gone = t.LastEliteGoneAt;
        ok = RunUntil(t, () => !t.EliteBusy, 3f);
        float back = t.LastEliteReturnAt - gone;
        Check("about a second later the traffic starts back (" + back.ToString("0.00") + " s)", ok && back >= .9f && back <= 1.2f);
        bool returning = RunUntil(t, () => t.ActiveCount > 0, .5f);
        Check("ships come back (" + t.ActiveCount + " within .5 s)", returning);
        RunUntil(t, () => Dense(t), 2.5f);
        float dense = t.Now - t.LastEliteReturnAt;
        Check("every layer's density target is met again within ~2 s (" + dense.ToString("0.00") + " s: " +
              t.CountIn(TitleScreenTraffic.Depth.Back) + "/" + t.CountIn(TitleScreenTraffic.Depth.Mid) + "/" + t.CountIn(TitleScreenTraffic.Depth.Front) + ")",
              Dense(t) && dense <= 2.2f);
        Run(t, 8f);
        Check("and they fly onto the screen (" + t.ShipsOnScreen + ")", t.ShipsOnScreen >= 8);
        Check("the next run is scheduled ~40 s after this one", t.NextEliteAt - t.LastEliteStartAt >= t.eliteInterval.x - .01f &&
              t.NextEliteAt - t.LastEliteStartAt <= t.eliteInterval.y + .01f);
        Done(t);
    }

    static bool Dense(TitleScreenTraffic t)
    {
        for (int d = 0; d < 3; d++) if (t.CountIn((TitleScreenTraffic.Depth)d) < t.layerTargets[d]) return false;
        return true;
    }

    static void ShipsArrivingMidRunAreSnipedToo()
    {
        var t = Quiet("~TE_late", 12);
        var c = t.Safe.center;
        LaunchId(t, 1, TitleScreenTraffic.Depth.Mid, c + new Vector2(-1f, -2f), 0f);
        LaunchId(t, 2, TitleScreenTraffic.Depth.Back, c + new Vector2(1f, -1f), 1f);
        Check("a run starts on a sky of two", t.TryStartElite());
        RunUntil(t, () => t.ElitePhaseNow == TitleScreenTraffic.ElitePhase.Snipe, 3f);
        Step(t);
        var late = LaunchId(t, 3, TitleScreenTraffic.Depth.Front, c + new Vector2(0f, -2.5f), 2f);
        Check("a ship flies in mid-run", late != null && late.active);
        RunUntil(t, () => t.ElitePhaseNow != TitleScreenTraffic.ElitePhase.Snipe, TitleScreenTraffic.EliteSnipeMax + 1f);
        Check("it is sniped before the elite leaves (" + t.EliteSnipes + ")", late != null && !late.active && t.EliteSnipes == 3);
        Done(t);
    }

    static void EmptySkyIsAFlyby()
    {
        var t = Quiet("~TE_empty", 13);
        Check("a run starts on an empty sky", t.TryStartElite());
        bool ok = RunUntil(t, () => !t.EliteBusy, 8f);
        Check("it just flies by and leaves (" + t.EliteFlybys + " flyby, " + t.EliteShotsFired + " shots)",
              ok && t.EliteFlybys == 1 && t.EliteShotsFired == 0 && t.EliteSnipes == 0);
        Done(t);
    }

    static void NeverOverlapsOtherShows()
    {
        // a dive in progress holds the run back
        var t = Quiet("~TE_overlap", 14);
        var f = LaunchId(t, 6, TitleScreenTraffic.Depth.Front, t.LogoRect.center + new Vector2(-1.6f, -1.6f), .8f);
        bool diving = f != null && t.LogoRect.width > 0f && t.StartPlunge(f, false, -1);
        Check("a ship is diving into the logo", diving);
        Check("no elite run while it dives", !t.TryStartElite() && !t.EliteBusy);
        RunUntil(t, () => t.Plunger == null, 6f);
        Check("it runs once the dive is over", t.TryStartElite());
        Done(t);

        // and while one is on, nothing else starts
        var u = Make("~TE_overlap2", 15, x => { x.plungeInterval = new Vector2(1f, 1f); x.crashInterval = new Vector2(.5f, .5f); });
        u.NextEliteAt = 1e9f;
        Run(u, 6f);
        while (!u.TryStartElite()) Step(u);
        foreach (var g in u.Pool) if (g.active) g.ultAt = u.Now;   // everyone wants to go off
        int crashes = u.Crashes, plunges = u.Plunges, ults = u.UltsFired, zooms = u.Zooms, flights = u.Flights;
        bool busyOnly = true;
        RunUntil(u, () => { busyOnly &= u.ActiveUlts == 0 && u.Plunger == null; return !u.EliteBusy; }, 16f);
        Check("during a run: no crashes, dives, ultimates, zoomers or new ships",
              busyOnly && u.Crashes == crashes && u.Plunges == plunges && u.UltsFired == ults && u.Zooms == zooms && u.Flights == flights);
        Done(u);
    }

    static void EveryAspectAndARotationMidRun()
    {
        // (aspect w/h, safe insets top, bottom)
        float[][] shapes =
        {
            new[] { 9f / 16f, 0f, 0f },
            new[] { 9f / 21f, .045f, .03f },
            new[] { 1170f / 2532f, .056f, .04f },
            new[] { 3f / 4f, .02f, .02f },
            new[] { 21f / 9f, .0f, .0f },
        };
        bool perched = true, cleared = true, left = true, back = true;
        string bad = null;
        for (int k = 0; k < shapes.Length; k++)
        {
            var sh = shapes[k];
            Rect view, safe;
            Shape(sh, out view, out safe);
            var t = Make("~TE_shape", 200 + k);
            t.NextEliteAt = 1e9f;
            t.PinGeometry(view, safe);
            Run(t, 10f);
            while (!t.TryStartElite()) Step(t);
            RunUntil(t, () => t.ElitePhaseNow == TitleScreenTraffic.ElitePhase.Snipe, 3f);
            bool p = safe.Contains(t.ElitePosition);
            bool c = RunUntil(t, () => t.ElitePhaseNow != TitleScreenTraffic.ElitePhase.Snipe, TitleScreenTraffic.EliteSnipeMax + 1f) && t.ShipsOnScreen == 0;
            bool l = RunUntil(t, () => t.ElitePhaseNow == TitleScreenTraffic.ElitePhase.Return, 3f) && !view.Contains(t.ElitePosition);
            bool b = RunUntil(t, () => !t.EliteBusy, 2f) && RunUntil(t, () => Dense(t), 2.5f);
            if (!(p && c && l && b) && bad == null) bad = "aspect " + sh[0].ToString("0.00") + (p ? "" : " perch") + (c ? "" : " clear") + (l ? "" : " leave") + (b ? "" : " return");
            perched &= p; cleared &= c; left &= l; back &= b;
            Done(t);
        }
        Check("9:16 to 21:9, notches, 3:4: the perch is inside the safe area" + (bad != null ? " (" + bad + ")" : ""), perched);
        Check("every shape: all ships sniped, the elite leaves, the traffic returns", cleared && left && back);

        // the phone turns mid-run: the perch moves with the screen
        Rect v0, s0, v1, s1;
        Shape(shapes[0], out v0, out s0);
        Shape(new[] { 16f / 9f, 0f, 0f }, out v1, out s1);
        var r = Make("~TE_rotate", 210);
        r.NextEliteAt = 1e9f;
        r.PinGeometry(v0, s0);
        Run(r, 10f);
        while (!r.TryStartElite()) Step(r);
        RunUntil(r, () => r.ElitePhaseNow == TitleScreenTraffic.ElitePhase.Snipe, 3f);
        Step(r);
        r.PinGeometry(v1, s1);
        Run(r, .5f);
        bool inside = s1.Contains(r.ElitePosition) || r.ElitePhaseNow != TitleScreenTraffic.ElitePhase.Snipe;
        bool done = RunUntil(r, () => !r.EliteBusy, TitleScreenTraffic.EliteSnipeMax + 6f);
        Check("rotated mid-run: the elite follows its perch onto the new screen and the run completes", inside && done);
        Done(r);
    }

    static void Shape(float[] sh, out Rect view, out Rect safe)
    {
        float halfH = 5f, halfW = halfH * sh[0];
        if (halfW < 2.85f) { halfW = 2.85f; halfH = halfW / sh[0]; }
        view = new Rect(-halfW, -halfH, 2f * halfW, 2f * halfH);
        safe = new Rect(view.x, view.y + view.height * sh[2], view.width, view.height * (1f - sh[1] - sh[2]));
    }

    static void IgnoresFrozenTimeScale()
    {
        float ts = Time.timeScale;
        Time.timeScale = 0f;   // the menu's moveBackGround may freeze it
        var t = Make("~TE_frozen", 16);
        t.NextEliteAt = 1e9f;
        Run(t, 6f);
        bool started = t.TryStartElite();
        bool ok = RunUntil(t, () => !t.EliteBusy, 16f);
        Time.timeScale = ts;
        Check("with timeScale frozen the run still plays out on the traffic's own clock", started && ok && t.EliteSnipes > 0);
        Done(t);
    }

    static void LeavingMidRunLeavesNothingBehind()
    {
        var logo = Logo();
        int before = EliteObjects();
        var t = Make("~TE_leave", 17);
        t.NextEliteAt = 1e9f;
        Run(t, 8f);
        while (!t.TryStartElite()) Step(t);
        RunUntil(t, () => t.ElitePhaseNow == TitleScreenTraffic.ElitePhase.Snipe && t.ActiveEliteShots > 0, 6f);
        Check("caught mid-run, a shot in the air", t.EliteBusy && EliteObjects() > before);
        t.ShakeLogo(Vector2.down, 1f);   // and the logo mid-shake
        Step(t);
        Done(t);
        Check("leaving the home screen mid-run leaves no elite object behind (" + EliteObjects() + ")", EliteObjects() == before);
        Check("and the logo back exactly at rest", LogoAtRest(logo));
    }

    static void Deterministic()
    {
        string a = Trace(4242), b = Trace(4242), c = Trace(4243);
        Debug.Log("[TE] trace " + a);
        Check("same seed, same runs (times, elites, snipes)", a == b && a.Length > 0);
        Check("another seed, other runs", a != c);
    }

    static string Trace(int seed)
    {
        var t = Make("~TE_det", seed);
        var sb = new System.Text.StringBuilder();
        int events = 0;
        for (float s = 0f; s < 130f; s += Dt)
        {
            Step(t);
            if (t.EliteEvents != events) { events = t.EliteEvents; sb.Append(t.LastEliteStartAt.ToString("0.000")).Append(':').Append(t.EliteDefNow.key).Append(' '); }
        }
        sb.Append("snipes ").Append(t.EliteSnipes).Append(" shots ").Append(t.EliteShotsFired);
        Done(t);
        return sb.ToString();
    }

    static void NoGameplaySideEffects()
    {
        float currency = score.totalCurrency, saved = StarDustLedger.Saved;
        int dust = score.dustPickups, pauses = score.pauseCounter, codex = Codex.DiscoveredCount;
        float ts = Time.timeScale;
        var t = Make("~TE_side", 31);
        t.NextEliteAt = 1e9f;
        Run(t, 8f);
        while (!t.TryStartElite()) Step(t);
        int elites = 0;
        RunUntil(t, () => { elites = Mathf.Max(elites, Object.FindObjectsByType<EliteShip>(FindObjectsSortMode.None).Length); return !t.EliteBusy; }, 16f);
        Check("a run went off (" + t.EliteSnipes + " sniped)", t.EliteSnipes > 0);
        Check("no gameplay elite object, director or hearts", elites == 0 && EliteShip.Live.Count == 0);
        Check("no currency, star dust, pauses or codex discoveries",
              score.totalCurrency == currency && StarDustLedger.Saved == saved && score.dustPickups == dust &&
              score.pauseCounter == pauses && Codex.DiscoveredCount == codex);
        Check("time scale untouched", Time.timeScale == ts && !WorldTimeFx.HitStopping);
        Done(t);

        string[] files = { "Assets/Scripts/UI/TitleScreenTraffic.Elite.cs", "Assets/Scripts/UI/TitleScreenTraffic.Logo.cs" };
        string[] banned =
        {
            "ShipAttackHits", "AttackPool.", "ShipTargets.", "Codex.", "score.", "StarDustLedger", "Achievement", "Handheld", "Vibrat",
            "Haptic", "timeScale =", "WorldTimeFx", "CameraKick", "DeathCrash.Begin", "PlayerPrefs.Set", "SocialBridge",
            "AddComponent<EliteShip>", "EliteDirector", "EliteSystem", "EliteBrains", "EliteAttacks", "RunScore", "ScoreRules",
        };
        string hit = null;
        foreach (var file in files)
        {
            string src = File.ReadAllText(file);
            foreach (var b in banned) if (src.Contains(b)) hit = file + ": " + b;
        }
        Check("the elite / logo code calls no gameplay elite, scoring, unlock or haptic API" + (hit != null ? " (" + hit + ")" : ""), hit == null);
    }

    static void NoAllocationsDuringARun()
    {
        var t = Make("~TE_alloc", 78, x => { x.maxShips = TitleScreenTraffic.MaxCap; x.layerTargets = new[] { 8, 7, 4 }; });
        // warm-up: wardrobe, pools, a few runs (their strips loaded)
        Run(t, 200f);
        t.SkinWorkPaused = true;
        t.NextEliteAt = 1e9f;
        RunUntil(t, () => !t.EliteBusy, 12f);
        Run(t, 4f);
        System.GC.Collect();
        long counted = 0;
        int n = 0;
        bool started = false;
        for (int i = 0; i < 900 && (!started || t.EliteBusy); i++)
        {
            long before = System.GC.GetTotalMemory(false);
            if (!started) started = t.TryStartElite();
            t.Step(Dt);
            long after = System.GC.GetTotalMemory(false);
            if (after > before) counted += after - before;
            n++;
        }
        Check("a whole run played (" + n + " frames, " + t.EliteSnipes + " snipes so far)", started && !t.EliteBusy);
        Check("a run allocates nothing per frame (" + counted + " bytes)", counted <= 0);
        Done(t);
    }

    // ---------------------------------------------------------------- the logo

    static TitleScreenTraffic.Flyer DiveIntoLogo(TitleScreenTraffic t)
    {
        var f = LaunchId(t, 6, TitleScreenTraffic.Depth.Front, t.LogoRect.center + new Vector2(-1.6f, -1.6f), .8f);
        if (f == null || !t.StartPlunge(f, false, -1)) return null;
        return f;
    }

    static void LogoShakesOnACrashAndSettlesExactly()
    {
        var logo = Logo();
        Sprite sprite = logo.sprite;
        Color color = logo.color;
        int order = logo.sortingOrder;
        var t = Quiet("~TE_shake", 300);
        Check("the logo is on the home screen", t.LogoRect.width > 0f);
        var f = DiveIntoLogo(t);
        Check("a ship dives into the logo", f != null);
        if (f == null) { Done(t); return; }
        bool still = true;
        RunUntil(t, () => { still &= LogoAtRest(logo); return t.LogoCrashes > 0; }, 6f);
        Check("the logo doesn't move before the crash", still && t.LogoCrashes == 1);
        Check("the crash shakes it", t.LogoShaking && t.LogoShakes == 1);
        float maxShift = 0f, maxTilt = 0f;
        bool moved = false, look = true;
        float hit = t.Now, settledAt = -1f;
        for (int i = 0; i < 40; i++)
        {
            Vector3 d = logo.transform.position - LogoPos;
            float tilt = Mathf.Abs(Mathf.DeltaAngle(0f, logo.transform.eulerAngles.z));
            maxShift = Mathf.Max(maxShift, ((Vector2)d).magnitude);
            maxTilt = Mathf.Max(maxTilt, tilt);
            moved |= d.sqrMagnitude > 1e-6f || tilt > .1f;
            look &= logo.sprite == sprite && logo.color == color && logo.sortingOrder == order && logo.enabled &&
                    (logo.transform.localScale - LogoScale).sqrMagnitude < 1e-10f && Mathf.Abs(d.z) < 1e-6f;
            if (settledAt < 0f && !t.LogoShaking) settledAt = t.Now - hit;
            Step(t);
        }
        Debug.Log("[TE] logo shake: shift " + maxShift.ToString("0.000") + " u, tilt " + maxTilt.ToString("0.00") + " deg, settled after " + settledAt.ToString("0.00") + " s");
        Check("it visibly moves (a few px, a couple of degrees)", moved && maxShift >= .01f && maxTilt >= .5f);
        Check("small: <= " + (TitleScreenTraffic.LogoShakeShift * 1.01f).ToString("0.000") + " u and <= " + TitleScreenTraffic.LogoShakeTilt + " deg for one hit",
              maxShift <= TitleScreenTraffic.LogoShakeShift * 1.01f && maxTilt <= TitleScreenTraffic.LogoShakeTilt * 1.01f);
        Check("short: settled within " + TitleScreenTraffic.LogoShakeTime + " s (" + settledAt.ToString("0.00") + ")",
              settledAt > 0f && settledAt <= TitleScreenTraffic.LogoShakeTime + Dt * 1.5f);
        Check("and back EXACTLY at rest (position, rotation, scale)", LogoAtRest(logo) && !t.LogoShaking);
        Check("its sprite, colour and sorting never change", look);
        Done(t);
        Check("still at rest after leaving", LogoAtRest(logo));
    }

    static void LogoShakeRetriggerIsCapped()
    {
        var logo = Logo();
        var t = Quiet("~TE_shake2", 301);
        float shiftCap = TitleScreenTraffic.LogoShakeShift * TitleScreenTraffic.LogoShakeCap * 1.01f;
        float tiltCap = TitleScreenTraffic.LogoShakeTilt * TitleScreenTraffic.LogoShakeCap * 1.01f;
        float maxShift = 0f, maxTilt = 0f, maxK = 0f;
        for (int i = 0; i < 30; i++)
        {
            t.ShakeLogo(new Vector2(Mathf.Cos(i), Mathf.Sin(i)), i % 2 == 0 ? -1f : 1f);   // a crash every frame
            maxK = Mathf.Max(maxK, t.LogoShakeEnergy);
            Step(t);
            maxShift = Mathf.Max(maxShift, ((Vector2)(logo.transform.position - LogoPos)).magnitude);
            maxTilt = Mathf.Max(maxTilt, Mathf.Abs(Mathf.DeltaAngle(0f, logo.transform.eulerAngles.z)));
        }
        Check("repeated crashes refresh the shake but cap its energy (" + maxK.ToString("0.00") + " <= " + TitleScreenTraffic.LogoShakeCap + ")",
              maxK <= TitleScreenTraffic.LogoShakeCap + 1e-4f && maxK > 1.2f);
        Check("so it never grows past the cap (" + maxShift.ToString("0.000") + " u, " + maxTilt.ToString("0.00") + " deg)",
              maxShift <= shiftCap && maxTilt <= tiltCap);
        RunUntil(t, () => !t.LogoShaking, TitleScreenTraffic.LogoShakeTime + .2f);
        Check("and it still ends exactly at rest", !t.LogoShaking && LogoAtRest(logo));
        // a second crash just after one: refreshed, not doubled, then rest
        t.ShakeLogo(Vector2.down, 1f);
        Run(t, .1f);
        t.ShakeLogo(Vector2.down, 1f);
        float k = t.LogoShakeEnergy;
        RunUntil(t, () => !t.LogoShaking, TitleScreenTraffic.LogoShakeTime + .2f);
        Check("a hit mid-shake refreshes it (energy " + k.ToString("0.00") + ") and still settles exactly", k > 1f && k < 2f && LogoAtRest(logo));
        Done(t);
    }

    static void SnipedShipsDoNotShakeTheLogo()
    {
        var logo = Logo();
        var t = Make("~TE_noshake", 302, x => x.plungeInterval = new Vector2(1e6f, 1e6f));
        t.NextEliteAt = 1e9f;
        t.NextPlungeAt = 1e9f;
        Run(t, 8f);
        // put a ship right over the logo so it's sniped there
        TitleScreenTraffic.Flyer over = null;
        foreach (var f in t.Pool) if (f.active && f.layer != TitleScreenTraffic.Depth.Back) { over = f; break; }
        if (over != null) { over.pos = t.LogoRect.center; over.baseSpeed = 0f; }
        while (!t.TryStartElite()) Step(t);
        bool still = true;
        RunUntil(t, () => { still &= LogoAtRest(logo) && !t.LogoShaking; return !t.EliteBusy; }, 16f);
        Check("ships sniped (even over the logo) never shake it (" + t.EliteSnipes + " sniped, " + t.LogoShakes + " shakes)",
              t.EliteSnipes > 0 && t.LogoShakes == 0 && still);
        Done(t);
    }
}
