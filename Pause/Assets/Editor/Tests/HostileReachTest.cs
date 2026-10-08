using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

// "Ships that have intelligence and shoot weapons should come low enough
// where we can kill them with a pause jump or run into them when we have a
// shield, they stick too high" + "when pause jumping on an elite they can
// never dodge that" + "when pause teleport on them it does 2 heart damage".
//
// HostileReach decides how high an armed pilot (EnemyBrain) or an elite
// (EliteShip) holds: inside the player's reach (ShipReach.Top, the hull,
// TeleportFx.BlastRadius), climbing back to its old firing height only for
// an attack while the ship is too close under it to fire fairly.
//
// On 9:16, 9:19.5, 9:21 and 3:4 (gameS1, the 1.35x ship, the HUD band):
//   GEOMETRY  every pilot's reach hold (ship at rest) is in reach: a pause
//             jump from the ship's rest lands on it, a ship flown up to it
//             touches it; the standoff above the ship is kept; its body is
//             clear of the hull at rest; with the ship parked at the top
//             the body stays off the hull and its pattern still comes into
//             jump range. Before / after heights logged.
//   PILOTS    every pilot flown against a ship at rest and at the top, old
//             heights vs new: the share of its engagement in jump / ram
//             range, windups (still threatening, never inside the fire
//             clearance), no body on the hull while calm.
//   ELITES    every elite (the four Space ones included) the same, with
//             its attacks on; before / after heights logged.
//   JUMP      a pause jump aimed at an elite -- the landing blast
//             overlapping its collider -- always kills it: from two hearts
//             and from one, in its grace window, mid-evasion, mid-attack,
//             short jumps included, with evasion on and off.
//
//   Unity -batchmode -quit -projectPath <abs>/Pause -executeMethod HostileReachTest.Run
public static class HostileReachTest
{
    static int fails;
    static void Check(string what, bool ok)
    {
        Debug.Log((ok ? "[HREACH] PASS  " : "[HREACH] FAIL  ") + what);
        if (!ok) fails++;
    }

    static void Log(string what) { Debug.Log("[HREACH] " + what); }

    public static void Run() { TestHarness.Exit(Execute()); }

    // the four aspect ratios asked for
    static readonly (string name, string id)[] Shapes =
    {
        ("9:16", "and-1080x1920"), ("9:19.5", "iphone-13"), ("9:21", "flip7-1080x2520"), ("3:4", "ipad-9"),
    };

    // Targets (shares of the time in play, after it has arrived).
    // Ship at rest (the typical spot): in pause-jump AND shield-ram range.
    const float RestShare = .95f;
    // Ship parked at the very top of its reach, the worst case: a hostile
    // that can fire at it from there climbs for each attack and drops back
    // into jump range between attacks (each at least the floor, the roster
    // on average at least the mean); one that cannot never climbs.
    const float TopPilotFloor = .08f, TopPilotMean = .35f;
    const float TopEliteFloor = .25f, TopEliteMean = .5f;

    static Camera cam;
    static float ortho0, aspect0;
    static IDisposable screen;
    static Func<Vector2, Rect, Rect> hudRect;

    public static int Execute()
    {
        fails = 0;
        using var sandbox = new TestHarness.Sandbox();
        try
        {
            EditorSceneLoader.Open("gameS1");
            cam = Camera.main;
            ortho0 = cam.orthographicSize;
            aspect0 = cam.aspect;
            var styler = new GameObject("~HReachHud").AddComponent<HudStyler>();
            styler.SendMessage("Start");
            Canvas.ForceUpdateCanvases();
            LayoutRebuilder.ForceRebuildLayoutImmediate(styler.HudRoot);
            CaptureHud(styler);
            buttonClicks.playerDied = false;
            score.pauseCounter = 0;
            Time.timeScale = 1f;
            EnemyThreat.ForceShooting = true;
            EliteCatalog.Reload();
            Check("the main game's ship size (gameS1: x" + ShipScale.Live + ")", Mathf.Approximately(ShipScale.Live, ShipScale.Main));

            PilotGeometry();
            PilotFlights();
            Elites();
            PauseJumpAlwaysKills();
        }
        finally
        {
            Use(null);
            HostileReach.Enabled = true;
            EliteEvasion.Enabled = true;
            EnemyThreat.ForceShooting = false;
            EnemyThreat.Reset();
            SpawnSpace.ClockOverride = null;
            EliteSystem.PlayerOverride = null;
            EliteSystem.Clear();
            PilotAirspace.Clear();
            PlayField.Reset();
            RunScore.EndRun(RunScore.RunId);
            if (cam != null) { cam.orthographicSize = ortho0; cam.aspect = aspect0; }
        }
        Debug.Log("[HREACH] failures: " + fails);
        return fails;
    }

    // ---- fixtures (as ShipReachTest's) --------------------------------------------------------

    static void CaptureHud(HudStyler styler)
    {
        var canvas = styler.HudRoot.GetComponentInParent<Canvas>().rootCanvas;
        var scaler = canvas.GetComponent<CanvasScaler>();
        Vector2 size = styler.HudRoot.rect.size, reference = scaler.referenceResolution;
        var mode = scaler.screenMatchMode;
        float match = scaler.matchWidthOrHeight;
        hudRect = (scr, safe) => HudStyler.HudScreenRect(TopBand.FrameFor(safe, scr, BossRails.InnerEdge, ScreenInfo.Cutouts), scr,
                                                         HudStyler.ScaleWithScreenSize(scr, reference, mode, match), size);
    }

    static void Use(FitDevice d)
    {
        if (screen != null) { screen.Dispose(); screen = null; }
        PlayField.Reset();
        if (d == null) return;
        cam.aspect = d.Aspect;
        cam.orthographicSize = CameraFit.ComputeSize(5f, CameraFit.GameplayHalfWidth, d.w, d.h);
        screen = ScreenInfo.Override(d.w, d.h, d.Safe, d.Cutouts, d.ReportedDpi, d.ios);
        PlayField.UseHud(hudRect);
    }

    static void Clear()
    {
        foreach (var e in Object.FindObjectsByType<EnemyIdentity>(FindObjectsSortMode.None)) Object.DestroyImmediate(e.gameObject);
        EliteSystem.Clear();
        PilotAirspace.Clear();
        EnemyThreat.Reset();
    }

    static string F(float v) => v.ToString("F2");
    static string P(float v) => (v * 100f).ToString("F0") + "%";

    static IEnumerable<EnemyDef> Pilots()
    {
        foreach (var def in EnemyRoster.All)
        {
            var b = def.Behaviour;
            if (b == null || !b.IsPilot || def.role == EnemyRole.Chaser || b.entry == PilotEntry.Descend) continue;
            yield return def;
        }
    }

    static bool InJump(Vector2 p, float below, float halfX) => HostileReach.JumpReaches(p.x, p.y - below, halfX, ShipReach.Top, ShipReach.HalfWidth);
    static bool InRam(Vector2 p, float below) => HostileReach.RamReaches(p.y - below, ShipReach.Top);

    // ---- 1: geometry --------------------------------------------------------------------------

    static void PilotGeometry()
    {
        foreach (var (name, id) in Shapes)
        {
            Use(FitDevice.Find(id));
            var f = PlayField.Live;
            float top = ShipReach.Top, rest = ShipReach.StartY, view = EnemyBrain.ViewScale, halfW = ShipReach.HalfWidth;
            float lane = SpawnLane.LaneHalf;
            Log(name + " (" + id + "): view " + F(f.bottom) + ".." + F(f.top) + " (" + F(f.Height) + " u), ship rest " + F(rest) + " (" + P(f.ShareOf(rest)) +
                "), reach top " + F(top) + " (" + P(f.ShareOf(top)) + "), hull above " + F(ShipReach.HullAbove) + ", jump blast " + F(TeleportFx.BlastRadius) +
                " u anywhere in x +/-" + F(halfW) + " y " + F(ShipReach.Bottom) + ".." + F(top));
            bool inReach = true, jumps = true, standoff = true, clear = true, pinnedClear = true, pinnedJump = true;
            string bad = "", table = "";
            foreach (var def in Pilots())
            {
                var b = def.Behaviour;
                float col = def.ColliderSize.y * .5f, drawn = SpawnSpace.BodyHalf(def).y, halfX = def.ColliderSize.x * .5f;
                float up = EnemyBrain.PatternUp(b), down = EnemyBrain.PatternDown(b);
                float station = CameraFit.ViewTop - b.stationDepth * view;
                float before = EnemyBrain.HoldY(station, rest, view, CameraFit.ViewTop, f.bandBottom);
                float after = EnemyBrain.ReachHoldY(station, rest, top, col, drawn, up, down);
                table += " " + def.key + " " + F(before) + " (" + P(f.ShareOf(before)) + ") -> " + F(after) + " (" + P(f.ShareOf(after)) + ");";
                // in reach: the top of its pattern, anywhere across the lane it may hold
                float x = Mathf.Max(0f, lane - halfX - b.bandX);
                bool r = HostileReach.JumpReaches(0f, after + up - col, halfX, top, halfW) && HostileReach.JumpReaches(x, after + up - col, halfX, top, halfW) &&
                         HostileReach.RamReaches(after + up - col, top);
                inReach &= r;
                // a pause jump from the ship's rest lands on it (the landing clamped into the reach)
                Vector2 from = new Vector2(0f, rest), land = new Vector2(0f, Mathf.Min(after, top));
                bool j = Vector2.Distance(from, land) >= TeleportFx.MinimumJump && HostileReach.JumpReaches(0f, after + up - col, halfX, land.y, halfW);
                jumps &= j;
                bool s = after - down - drawn >= rest + ShipReach.HullAbove + HostileReach.StandoffGap - 1e-3f;
                standoff &= s;
                bool c = after - down - col > rest + ShipReach.HullAbove;
                clear &= c;
                // the ship parked at the top of its reach
                float pinned = EnemyBrain.ReachHoldY(station, top, top, col, drawn, up, down);
                bool pc = pinned - down - drawn > top + ShipReach.HullAbove;
                bool pj = HostileReach.JumpReaches(0f, pinned - down - col, halfX, top, halfW);
                pinnedClear &= pc;
                pinnedJump &= pj;
                if (!(r && j && s && c && pc && pj)) bad += " " + def.key + (r ? "" : " reach") + (j ? "" : " jump") + (s ? "" : " standoff") + (c ? "" : " overlap") +
                                                       (pc ? "" : " pinned-on-hull") + (pj ? "" : " pinned-out-of-jump") + ";";
            }
            Log(name + " pilot holds with the ship at rest, before (HoldY) -> after (ReachHoldY), world y (share of the view):" + table);
            Check(name + ": every pilot's hold (ship at rest) is in pause-jump and shield-ram range, across its lane" + (bad.Length > 0 ? " (" + bad + ")" : ""), inReach);
            Check(name + ": a pause jump from the ship's rest lands on every pilot at its hold", jumps);
            Check(name + ": every pilot keeps the standoff above a ship at rest (hull + " + HostileReach.StandoffGap + " u)", standoff);
            Check(name + ": no pilot's collider overlaps a ship at rest", clear);
            Check(name + ": with the ship parked at the top no pilot holds on its hull", pinnedClear);
            Check(name + ": ... and every pilot's pattern still comes into pause-jump range there", pinnedJump);
        }
    }

    // ---- 2: pilots flown -----------------------------------------------------------------------

    struct Flight
    {
        public int frames, jump, ram, onHull, windups, close;
        public string firstClose;
        public float sumY, maxY;
    }

    const float PilotDt = 1f / 60f;

    static Flight FlyPilot(EnemyDef def, float shipY, bool enabled)
    {
        HostileReach.Enabled = enabled;
        Clear();
        moveBackGround.speed = .3f;
        var ship = new GameObject("~HReachShip").transform;
        ship.position = new Vector3(0f, shipY, 0f);
        EliteSystem.PlayerOverride = ship;
        UnityEngine.Random.InitState(def.key.GetHashCode());
        var go = EnemyFactory.Create(def, new Vector3(0f, CameraFit.ViewTop + PilotAirspace.WaitAbove, 0f), Quaternion.identity);
        var brain = go.GetComponent<EnemyBrain>();
        brain.TargetOverride = ship;
        var b = def.Behaviour;
        float col = def.ColliderSize.y * .5f, halfX = def.ColliderSize.x * .5f, view = EnemyBrain.ViewScale;
        var fl = new Flight { maxY = float.MinValue };
        float clock = 900f;
        var was = EnemyBrain.Phase.Idle;
        float hullHalf = ShipScale.HullHalfWidth;
        for (int i = 0; i < 60 * 14 && brain.Stage != EnemyBrain.PilotStage.Gone; i++)
        {
            clock += PilotDt;
            SpawnSpace.ClockOverride = clock;
            Vector2 at = go.transform.position;   // where the windup is judged (MayAttack: the frame's start)
            brain.Step(PilotDt);
            EliteSystem.Step(PilotDt);
            if (brain.Stage == EnemyBrain.PilotStage.Gone) break;
            Vector2 p = go.transform.position;
            if (brain.State == EnemyBrain.Phase.Windup && was != EnemyBrain.Phase.Windup)
            {
                fl.windups++;
                float dy = at.y - shipY, dist = Vector2.Distance(at, ship.position);
                if (dy < EnemyBrain.MinFireAbove * view - 1e-3f || dist < EnemyBrain.MinFireDistance * view - 1e-3f)
                {
                    fl.close++;
                    if (fl.firstClose == null) fl.firstClose = def.key + " ship " + F(shipY) + " dy " + F(dy) + " dist " + F(dist) + " (" + (enabled ? "new" : "old") + ")";
                }
            }
            was = brain.State;
            if (brain.Stage != EnemyBrain.PilotStage.Engaging) continue;
            fl.frames++;
            fl.sumY += p.y;
            fl.maxY = Mathf.Max(fl.maxY, p.y);
            if (InJump(p, col, halfX)) fl.jump++;
            if (InRam(p, col)) fl.ram++;
            if (brain.State == EnemyBrain.Phase.Idle && Mathf.Abs(p.x) < halfX + hullHalf && p.y - col < shipY + ShipReach.HullAbove && p.y + col > shipY - ShipReach.HullAbove)
                fl.onHull++;
        }
        SpawnSpace.ClockOverride = null;
        Object.DestroyImmediate(go);
        Object.DestroyImmediate(ship.gameObject);
        HostileReach.Enabled = true;
        return fl;
    }

    static float Share(int n, int of) => of > 0 ? (float)n / of : 1f;

    static void PilotFlights()
    {
        foreach (var (name, id) in Shapes)
        {
            Use(FitDevice.Find(id));
            var f = PlayField.Live;
            float rest = ShipReach.StartY, top = ShipReach.Top;
            bool restOk = true, topOk = true, threat = true, fair = true, hull = true;
            string table = "", bad = "", close = null;
            float worstTop = 1f, topSum = 0f;
            int topN = 0;
            foreach (var def in Pilots())
            {
                var b0 = FlyPilot(def, rest, false);
                var a0 = FlyPilot(def, rest, true);
                var b1 = FlyPilot(def, top, false);
                var a1 = FlyPilot(def, top, true);
                float restJump = Share(a0.jump, a0.frames), restRam = Share(a0.ram, a0.frames), topJump = Share(a1.jump, a1.frames);
                table += " " + def.key + ": rest y " + P(f.ShareOf(b0.sumY / Mathf.Max(1, b0.frames))) + " -> " + P(f.ShareOf(a0.sumY / Mathf.Max(1, a0.frames))) +
                         " (jump " + P(Share(b0.jump, b0.frames)) + " -> " + P(restJump) + ", ram " + P(Share(b0.ram, b0.frames)) + " -> " + P(restRam) +
                         ", windups " + b0.windups + " -> " + a0.windups + "); top y " + P(f.ShareOf(b1.sumY / Mathf.Max(1, b1.frames))) + " -> " +
                         P(f.ShareOf(a1.sumY / Mathf.Max(1, a1.frames))) + " (jump " + P(Share(b1.jump, b1.frames)) + " -> " + P(topJump) +
                         ", windups " + b1.windups + " -> " + a1.windups + ");";
                bool armed = def.Behaviour.Attacks && def.Behaviour.maxVolleys > 0;
                bool r = a0.frames == 0 || (restJump >= RestShare && restRam >= RestShare);
                bool t = a1.frames == 0 || topJump >= TopPilotFloor;
                topSum += topJump; topN++;
                restOk &= r;
                topOk &= t;
                worstTop = Mathf.Min(worstTop, topJump);
                // still threatening: what wound up before still does, at rest and with the ship high
                bool th = !armed || ((b0.windups == 0 || a0.windups > 0) && (b1.windups == 0 || a1.windups > 0));
                threat &= th;
                fair &= a0.close == 0 && a1.close == 0;
                if (close == null) close = a0.firstClose ?? a1.firstClose ?? b0.firstClose ?? b1.firstClose;
                hull &= a0.onHull == 0 && a1.onHull == 0;
                if (!(r && t && th)) bad += " " + def.key + (r ? "" : " rest") + (t ? "" : " top") + (th ? "" : " threat") + ";";
            }
            Log(name + " pilots flown, old heights -> new (share of view, mean while engaged):" + table);
            Check(name + ": with the ship at rest every pilot spends >= " + P(RestShare) + " of its engagement in pause-jump and shield-ram range" +
                  (bad.Length > 0 ? " (" + bad + ")" : ""), restOk);
            Check(name + ": with the ship parked at the top every pilot spends >= " + P(TopPilotFloor) + " in pause-jump range (worst " + P(worstTop) +
                  "), the roster >= " + P(TopPilotMean) + " on average (" + P(topSum / Mathf.Max(1, topN)) + ")", topOk && topSum / Mathf.Max(1, topN) >= TopPilotMean);
            Check(name + ": every armed pilot that wound up before still winds up, ship at rest and at the top", threat);
            Check(name + ": no windup starts inside the fire clearance (" + EnemyBrain.MinFireAbove + " / " + EnemyBrain.MinFireDistance + " x view)" +
                  (close != null ? " first: " + close : ""), fair);
            Check(name + ": no calm pilot's body on the hull", hull);
        }
    }

    // ---- 3: elites ------------------------------------------------------------------------------

    const float EliteDt = 1f / 30f;

    static Flight FlyElite(EliteDef def, float shipY, bool enabled, out int attacks)
    {
        HostileReach.Enabled = enabled;
        Clear();
        moveBackGround.speed = .25f;
        var ship = new GameObject("~HReachPilot").transform;
        ship.position = new Vector3(0f, shipY, 0f);
        EliteSystem.PlayerOverride = ship;
        UnityEngine.Random.InitState(def.key.GetHashCode());
        var e = EliteShip.CreateInPlay(def, new Vector2(1.2f, Mathf.Min(shipY + 2.5f, CameraFit.ViewTop - 1f)));
        e.AttackCooldown = 1.5f;
        var fl = new Flight { maxY = float.MinValue };
        float r = def.hullRadius, hullHalf = ShipScale.HullHalfWidth;
        for (float t = 0f; t < 16f && e != null && e.State != EliteState.Dead; t += EliteDt)
        {
            EliteSystem.Step(EliteDt);
            if (t < 2.5f || e == null || e.State == EliteState.Dead) continue;
            Vector2 p = e.Position;
            fl.frames++;
            fl.sumY += p.y;
            fl.maxY = Mathf.Max(fl.maxY, p.y);
            if (InJump(p, r, r)) fl.jump++;
            if (InRam(p, r)) fl.ram++;
            if (e.State == EliteState.Follow && Mathf.Abs(p.x) < r + hullHalf && p.y - r < shipY + ShipReach.HullAbove && p.y + r > shipY - ShipReach.HullAbove)
                fl.onHull++;
        }
        attacks = e != null ? e.Attacks : 0;
        EliteSystem.Clear();
        Object.DestroyImmediate(ship.gameObject);
        HostileReach.Enabled = true;
        return fl;
    }

    static void Elites()
    {
        int space = 0;
        foreach (var def in EliteCatalog.All) if (def.world == "space") space++;
        Check("the four Space elites are in the catalog (" + space + ")", space >= 4);
        foreach (var (name, id) in Shapes)
        {
            Use(FitDevice.Find(id));
            var f = PlayField.Live;
            float rest = ShipReach.StartY, top = ShipReach.Top;
            bool restOk = true, topOk = true, threat = true, hull = true, capOk = true;
            string table = "", bad = "";
            float worstTop = 1f, topSum = 0f;
            int topN = 0;
            foreach (var def in EliteCatalog.All)
            {
                // the cap itself: never below the standoff, never above the ceiling unless the standoff is
                float r = def.hullRadius;
                for (float y = ShipReach.Bottom; y <= top; y += .25f)
                {
                    float cap = HostileReach.Cap(y, r, r);
                    capOk &= cap >= y + HostileReach.StandoffFor(r) - 1e-4f &&
                             (cap <= HostileReach.Ceiling(r) + 1e-4f || Mathf.Abs(cap - (y + HostileReach.StandoffFor(r))) < 1e-4f) &&
                             HostileReach.JumpReaches(0f, cap - r, r, top, ShipReach.HalfWidth);
                }
                var b0 = FlyElite(def, rest, false, out int ab0);
                var a0 = FlyElite(def, rest, true, out int aa0);
                var b1 = FlyElite(def, top, false, out int ab1);
                var a1 = FlyElite(def, top, true, out int aa1);
                float restJump = Share(a0.jump, a0.frames), restRam = Share(a0.ram, a0.frames), topJump = Share(a1.jump, a1.frames);
                table += " " + def.displayName + " (" + def.brain + "): rest y " + F(b0.sumY / Mathf.Max(1, b0.frames)) + " (" + P(f.ShareOf(b0.sumY / Mathf.Max(1, b0.frames))) + ", max " +
                         P(f.ShareOf(b0.maxY)) + ") -> " + F(a0.sumY / Mathf.Max(1, a0.frames)) + " (" + P(f.ShareOf(a0.sumY / Mathf.Max(1, a0.frames))) + ", max " + P(f.ShareOf(a0.maxY)) +
                         "), jump " + P(Share(b0.jump, b0.frames)) + " -> " + P(restJump) + ", ram " + P(Share(b0.ram, b0.frames)) + " -> " + P(restRam) +
                         ", attacks " + ab0 + " -> " + aa0 + "; top y " + P(f.ShareOf(b1.sumY / Mathf.Max(1, b1.frames))) + " -> " + P(f.ShareOf(a1.sumY / Mathf.Max(1, a1.frames))) +
                         ", jump " + P(Share(b1.jump, b1.frames)) + " -> " + P(topJump) + ", attacks " + ab1 + " -> " + aa1 + ";";
                bool rr = restJump >= RestShare && restRam >= RestShare;
                bool tt = topJump >= TopEliteFloor;
                topSum += topJump; topN++;
                // (one attack before can be a fluke on the way in: two or more must survive)
                bool th = (ab0 <= 1 || aa0 > 0) && (ab1 <= 1 || aa1 > 0);
                restOk &= rr;
                topOk &= tt;
                threat &= th;
                hull &= a0.onHull <= b0.onHull;
                worstTop = Mathf.Min(worstTop, topJump);
                if (!(rr && tt && th && a0.onHull <= b0.onHull)) bad += " " + def.key + (rr ? "" : " rest") + (tt ? "" : " top") + (th ? "" : " threat") + (a0.onHull <= b0.onHull ? "" : " hull " + b0.onHull + "->" + a0.onHull) + ";";
            }
            Log(name + " elites flown (pilot at rest " + F(rest) + " = " + P(f.ShareOf(rest)) + ", reach top " + F(top) + " = " + P(f.ShareOf(top)) + "), old -> new:" + table);
            Check(name + ": the elites' cap keeps the standoff and stays in jump range for every ship height", capOk);
            Check(name + ": with the pilot at rest every elite spends >= " + P(RestShare) + " of its flight in pause-jump and shield-ram range" +
                  (bad.Length > 0 ? " (" + bad + ")" : ""), restOk);
            Check(name + ": with the pilot parked at the top every elite spends >= " + P(TopEliteFloor) + " in pause-jump range (worst " + P(worstTop) +
                  "), the elites >= " + P(TopEliteMean) + " on average (" + P(topSum / Mathf.Max(1, topN)) + ")", topOk && topSum / Mathf.Max(1, topN) >= TopEliteMean);
            Check(name + ": every elite that attacked before still attacks (pilot at rest and at the top)", threat);
            Check(name + ": no following elite sits on the hull of a pilot at rest more than it did (its own attack's return aside)" + (bad.Length > 0 ? " (" + bad + ")" : ""), hull);
        }
    }

    // ---- 4: a pause jump on an elite always kills it ----------------------------------------------

    const int Trials = 24;

    static void PauseJumpAlwaysKills()
    {
        Use(FitDevice.Find("flip7-1080x2520"));
        RunScore.BeginRun(true, true);
        foreach (bool evasion in new[] { true, false })
        {
            EliteEvasion.Enabled = evasion;
            int evaded = 0;
            foreach (var def in EliteCatalog.All)
            {
                int connected = 0, attempted = 0, fromTwo = 0, fromOne = 0, inGrace = 0, midEvade = 0, midAttack = 0, shortJumps = 0;
                string first = null;
                for (int k = 0; k < Trials; k++)
                {
                    Clear();
                    UnityEngine.Random.InitState(1000 + k * 37 + def.key.Length);
                    moveBackGround.speed = .3f;
                    var ship = new GameObject("~HReachJumper").transform;
                    ship.position = new Vector3(UnityEngine.Random.Range(-1.5f, 1.5f), Mathf.Lerp(ShipReach.Bottom, ShipReach.Top, UnityEngine.Random.value), 0f);
                    EliteSystem.PlayerOverride = ship;
                    var e = EliteShip.CreateInPlay(def, new Vector2(UnityEngine.Random.Range(-1.2f, 1.2f), Mathf.Lerp(ShipReach.StartY, ShipReach.Top, UnityEngine.Random.value)));
                    e.AttackCooldown = UnityEngine.Random.Range(0f, 2f);
                    float run = UnityEngine.Random.Range(.2f, 2.5f);
                    for (float t = 0f; t < run && e != null && e.State != EliteState.Dead; t += EliteDt) EliteSystem.Step(EliteDt);
                    // something to dodge in half the runs: the jump lands the moment it swerves
                    GameObject rock = null;
                    bool wasEvading = false;
                    if (k % 2 == 0 && e != null && e.State != EliteState.Dead)
                    {
                        rock = EnemyFactory.Create(EnemyRoster.One(3, EnemyRole.Rock), new Vector3(e.Position.x, e.Position.y + 4f, 0f), Quaternion.identity);
                        ClearTarget.Ensure(rock);
                        for (float t = 0f; t < 1.5f && e != null && e.State != EliteState.Dead && !wasEvading; t += EliteDt)
                        {
                            if (rock != null) rock.transform.position += Vector3.down * SpawnSpace.ScrollSpeed * EliteDt;
                            EliteSystem.Step(EliteDt);
                            wasEvading = e != null && (e.Evading || e.Velocity.sqrMagnitude > 4f);
                        }
                    }
                    if (e == null || e.State == EliteState.Dead || !e.InPlay) { Cleanup(ship, rock); continue; }   // crashed before the jump
                    if (k % 3 == 1) { e.TakeHit(EliteDamage.PlayerWeapon, (Vector3)e.Position + Vector3.left); }
                    if (e.State == EliteState.Dead) { Cleanup(ship, rock); continue; }
                    attempted++;
                    if (e.Hearts >= 2) fromTwo++; else fromOne++;
                    if (e.Grace > 0f) inGrace++;
                    if (e.Evading) midEvade++;
                    if (e.State == EliteState.Attack) midAttack++;
                    // THE PAUSE: the world is frozen; the finger lands on the elite (the
                    // landing blast overlapping its collider) and the ship blinks there
                    Vector2 off = UnityEngine.Random.insideUnitCircle * (TeleportFx.BlastRadius + def.hullRadius) * .95f;
                    if (k % 5 == 4) off = Vector2.zero;
                    Vector3 to = (Vector3)(e.Position + off);
                    Vector3 from = k % 7 == 3 ? to + Vector3.right * .4f : ship.position;   // a short hop now and then
                    if (Vector3.Distance(from, to) < TeleportFx.MinimumJump) shortJumps++;
                    int kills = EliteShip.Kills;
                    ship.position = to;
                    TeleportFx.Play(from, to);
                    bool dead = (e == null || e.State == EliteState.Dead) && EliteShip.Kills == kills + 1 && EliteShip.LastKillCause == EliteDamage.Teleport;
                    if (dead) connected++;
                    else Trial(ref first, def, k, "survived (hearts " + (e != null ? e.Hearts : -1) + ", grace " + (e != null ? F(e.Grace) : "-") + ", offset " + F(off.magnitude) + ")");
                    Cleanup(ship, rock);
                }
                Check((evasion ? "" : "evasion off: ") + def.displayName + ": a pause jump aimed at it kills it in " + connected + "/" + attempted + " seeded runs (from 2 hearts " + fromTwo +
                      ", from 1 " + fromOne + ", in grace " + inGrace + ", mid-evasion " + midEvade + ", mid-attack " + midAttack + ", short hops " + shortJumps + ")" +
                      (first != null ? " first miss: " + first : ""), connected == attempted && attempted >= Trials * 2 / 3 && inGrace > 0 && fromTwo > 0);
                evaded += midEvade;
            }
            if (evasion) Check("... jumps landed mid-evasion across the elites (" + evaded + ")", evaded > 0);
        }
        EliteEvasion.Enabled = true;
        Clear();
    }

    static void Trial(ref string first, EliteDef def, int k, string what)
    {
        if (first == null && what.StartsWith("survived")) first = def.key + " #" + k + " " + what;
    }

    static void Cleanup(Transform ship, GameObject rock)
    {
        if (rock != null) Object.DestroyImmediate(rock);
        if (ship != null) Object.DestroyImmediate(ship.gameObject);
        EliteSystem.Clear();
    }
}
