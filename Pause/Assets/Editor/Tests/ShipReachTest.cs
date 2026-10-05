using System;
using System.Collections.Generic;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

// "Ship can't reach parts of tall screens" and "Bosses rest mid-screen on
// tall phones" (docs/enemy-behaviours.md "Ship reach and boss height").
//
//   REACH     on every phone / tablet shape (ScreenInfo + the camera
//             CameraFit gives it): the ship's range is TopShare of the view
//             at the top and the safe area's bottom + the hull's drawing +
//             a margin at the bottom; the hull is fully visible there and
//             under the HUD band at the top; sideways stays +/-2.4; the range
//             follows the safe area continuously (a fold, a gesture bar)
//   PILOTS    every pilot against a ship parked at the top of its reach:
//             windups only start with the authored clearance (x view), no
//             body (entry, Swoop dip, station) on the hull
//   BELOW     a chaser coming up under a ship parked at the bottom of its
//             reach takes no less time to reach it than in the authored view
//   REACHABLE the portal's station, atoms (real-game and tutorial), the
//             ship's start, elites' join points
//   BOSS      at rest in the upper band on every shape: fully visible, its
//             cell under the HUD band and the cutouts, above the ship's
//             ceiling for the fight; every attack's time to the ship's row
//             within 12% of the authored view's (before / after logged);
//             beams end on a rail or past the bottom, telegraphs and muzzles
//             on screen, ricochets still ricochet, every pattern leaves a way
//             through; arrival and retreat off the top
//   COST      nothing here allocates per frame; nothing moves while paused
public static class ShipReachTest
{
    static int fails;
    static void Check(string what, bool ok)
    {
        Debug.Log((ok ? "[REACH] PASS  " : "[REACH] FAIL  ") + what);
        if (!ok) fails++;
    }

    static void Log(string what) { Debug.Log("[REACH] " + what); }

    public static void Run() { TestHarness.Exit(Execute()); }

    const float Dt = 1f / 60f;
    // HUD 35: the board's speed (moveBackGround.speed x 30 u/s, AtomWander.Descent).
    const float Hud35 = .35f * 30f;

    // 16:9, 20:9 + gesture bar, 21:9 (the user's phone), 22:9, iPhone 15, iPhone SE, a foldable's inner screen, a tablet
    static readonly string[] Shapes =
    {
        "and-1080x1920", "and-1080x2400-gesture", "flip7-1080x2520", "and-1080x2640", "iphone-15", "iphone-se",
        "fold-1812x2176", "ipad-9",
    };
    static readonly string[] Phones = { "and-1080x1920", "flip7-1080x2520", "and-1080x2640", "iphone-15" };

    static Camera cam;
    static HudStyler styler;
    static float ortho0, aspect0;
    static IDisposable screen;

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
            styler = new GameObject("~ReachHud").AddComponent<HudStyler>();
            styler.SendMessage("Start");
            Canvas.ForceUpdateCanvases();
            LayoutRebuilder.ForceRebuildLayoutImmediate(styler.HudRoot);
            CaptureHud();
            buttonClicks.playerDied = false;
            score.pauseCounter = 0;
            Time.timeScale = 1f;
            EnemyThreat.ForceShooting = true;

            ReachOnEveryShape();
            HullMeasured();
            ReachFollowsTheSafeArea();
            PilotsAgainstAShipAtTheTop();
            ChasersAgainstAShipAtTheBottom();
            EverythingReachable();
            BossScene();
            BossRestsInTheUpperBand();
            BossAttackTiming();
            BossFightOnEveryPhone();
            if (TestHarness.Slow("boss dodge simulation on phone shapes")) BossPatternsLeaveAWayThrough();
            NothingAllocatesNothingMovesWhilePaused();
        }
        finally
        {
            Use(null);
            ShipReach.FitToView = true;
            BossConfig.FitToView = true;
            EnemyThreat.ForceShooting = false;
            EnemyThreat.Reset();
            SpawnSpace.ClockOverride = null;
            EliteSystem.PlayerOverride = null;
            EliteSystem.Clear();
            PilotAirspace.Clear();
            BossEncounter.ResetRun();
            BossRails.Reset();
            PlayField.Reset();
            if (cam != null) { cam.orthographicSize = ortho0; cam.aspect = aspect0; }
        }
        Debug.Log("[REACH] failures: " + fails);
        return fails;
    }

    // ---- fixtures ------------------------------------------------------------------------

    // The scene as `d` shows it: camera fitted (CameraFit), screen, safe
    // area and cutouts (ScreenInfo), the HUD's read-out (PlayField). null: none.
    static void Use(FitDevice d)
    {
        if (screen != null) { screen.Dispose(); screen = null; }
        PlayField.Reset();
        if (d == null) return;
        cam.aspect = d.Aspect;
        cam.orthographicSize = CameraFit.ComputeSize(5f, CameraFit.GameplayHalfWidth, d.w, d.h);
        screen = ScreenInfo.Override(d.w, d.h, d.Safe, d.Cutouts);
        PlayField.UseHud(hudRect);
    }

    // The score read-out as gameS1 lays it out (HudStyler), for any screen,
    // kept as numbers so the boss's empty scene can use it too.
    static Func<Vector2, Rect, Rect> hudRect;

    static void CaptureHud()
    {
        var canvas = styler.HudRoot.GetComponentInParent<Canvas>().rootCanvas;
        var scaler = canvas.GetComponent<CanvasScaler>();
        Vector2 size = styler.HudRoot.rect.size, reference = scaler.referenceResolution;
        var mode = scaler.screenMatchMode;
        float match = scaler.matchWidthOrHeight;
        Check("the HUD read-out is gameS1's score panel on a ScaleWithScreenSize canvas (" + size + ")",
              size.x > 300f && size.y > 100f && scaler.uiScaleMode == CanvasScaler.ScaleMode.ScaleWithScreenSize);
        hudRect = (scr, safe) => HudStyler.HudScreenRect(TopBand.FrameFor(safe, scr, BossRails.InnerEdge, ScreenInfo.Cutouts), scr,
                                                         HudStyler.ScaleWithScreenSize(scr, reference, mode, match), size);
    }

    // Boss fights run in an empty scene with just a camera (BossAttackTest's
    // fixture): gameS1's rail quads are not fitted in edit mode.
    static void BossScene()
    {
        Use(null);
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        BossEncounter.ResetRun();
        BossRails.Reset();
        var camGo = new GameObject("Main Camera", typeof(Camera));
        camGo.tag = "MainCamera";
        cam = camGo.GetComponent<Camera>();
        cam.orthographic = true;
        cam.orthographicSize = 5f;
        camGo.transform.position = new Vector3(0f, 0f, -10f);
        ortho0 = 5f;
        aspect0 = cam.aspect;
        buttonClicks.playerDied = false;
        score.pauseCounter = 0;
    }

    // The authored 10 u view with no band (the editor's own view).
    static void UseAuthored()
    {
        Use(null);
        cam.aspect = 9f / 16f;
        cam.orthographicSize = 5f;
        screen = ScreenInfo.Override(640, 480, new Rect(0, 0, 640, 480));
        PlayField.Reset();
    }

    static string F(float v) { return v.ToString("F2"); }
    static string P(float v) { return (v * 100f).ToString("F0") + "%"; }

    static void Clear()
    {
        foreach (var e in Object.FindObjectsByType<EnemyIdentity>(FindObjectsSortMode.None)) Object.DestroyImmediate(e.gameObject);
        foreach (var c in Object.FindObjectsByType<ChaserEnemy>(FindObjectsSortMode.None)) Object.DestroyImmediate(c.gameObject);
        EliteSystem.Clear();
        PilotAirspace.Clear();
        EnemyThreat.Reset();
    }

    // ---- 1: the ship's reach --------------------------------------------------------------

    static void ReachOnEveryShape()
    {
        foreach (var id in Shapes)
        {
            var d = FitDevice.Find(id);
            Use(d);
            var f = PlayField.Live;
            float bottom = ShipReach.Bottom, top = ShipReach.Top;
            float oldB = Mathf.Max(ShipReach.LegacyBottom, f.bottom + ShipReach.FingerOffset), oldT = ShipReach.LegacyTop;
            float finger = f.bottom + ShipReach.FingerOffset;   // a finger on the screen's bottom edge
            Log(string.Format("{0} ({1}x{2}): view {3}..{4} ({5} u), safe {6}..{7}, HUD band from {8} ({9}) | reach {10}..{11} = {12}..{13} of the view " +
                              "(was {14}..{15} = {16}..{17}) | a finger on the bottom edge puts the ship at {18} | HUD 35: {19} s from the top edge to the hull at the ceiling (was {20} s)",
                              id, d.w, d.h, F(f.bottom), F(f.top), F(f.Height), F(f.safeBottom), F(f.safeTop), F(f.bandBottom), P(f.ShareOf(f.bandBottom)),
                              F(bottom), F(top), P(f.ShareOf(bottom)), P(f.ShareOf(top)), F(oldB), F(oldT), P(f.ShareOf(oldB)), P(f.ShareOf(oldT)),
                              F(finger), F((f.top - top - ShipReach.HullAbove) / Hud35), F(Mathf.Max(0f, f.top - oldT - ShipReach.HullAbove) / Hud35)));
            bool phone = d.h > 1.5f * d.w;
            Check(id + ": the ceiling is " + P(ShipReach.TopShare) + " of the view (" + P(f.ShareOf(top)) + ")" + (phone ? "" : " or under the HUD band"),
                  Mathf.Abs(f.ShareOf(top) - ShipReach.TopShare) < .001f || (!phone && top + ShipReach.HullAbove <= f.bandBottom - ShipReach.BottomMargin + 1e-3f));
            Check(id + ": at the floor the whole ship (hull and flame, " + F(ShipReach.HullBelow) + " u under its centre) is " + F(ShipReach.BottomMargin) +
                  " u above the safe area's bottom (" + F(bottom - ShipReach.HullBelow) + " vs " + F(f.safeBottom) + ")",
                  Mathf.Abs(bottom - ShipReach.HullBelow - ShipReach.BottomMargin - Mathf.Max(f.bottom, f.safeBottom)) < 1e-3f);
            Check(id + ": at the ceiling the hull is under the HUD band (" + F(top + ShipReach.HullAbove) + " < " + F(f.bandBottom) + ")",
                  f.hasBand && top + ShipReach.HullAbove < f.bandBottom);
            Check(id + ": movePlayer's clamp is this range", Mathf.Approximately(movePlayer.ClampPlayerY(-99f), bottom) &&
                  Mathf.Approximately(movePlayer.ClampPlayerY(99f), top) && Mathf.Approximately(movePlayer.ClampPlayerY(0f), Mathf.Clamp(0f, bottom, top)));
            Check(id + ": sideways reach unchanged (+/-2.4)", ShipReach.ClampX(-9f) == -2.4f && ShipReach.ClampX(9f) == 2.4f && ShipReach.HalfWidth == 2.4f);
            Check(id + ": it reaches the bottom of the screen (" + P(f.ShareOf(bottom)) + " of the view; was " + P(f.ShareOf(oldB)) + "), " +
                  P(f.ShareOf(top) - f.ShareOf(bottom)) + " of the view in all (was " + P(f.ShareOf(oldT) - f.ShareOf(oldB)) + ")",
                  f.ShareOf(bottom) <= .1f + 1e-3f && (!phone || bottom < oldB));
        }
        Use(FitDevice.Find("flip7-1080x2520"));
        string src = System.IO.File.ReadAllText("Assets/Scripts/Ship/movePlayer.cs") + System.IO.File.ReadAllText("Assets/Scripts/Gameplay/movePlayerInTut.cs");
        Check("movePlayer and the tutorial's mover read ShipReach, no fixed -4.15 / 4.5",
              !src.Contains("4.15f") && !src.Contains("4.5f)") && src.Contains("ShipReach.ClampY"));

        // the tutorial's mover: the same range
        var tut = new GameObject("~ReachTut");
        var hype = new GameObject("hypeText", typeof(RectTransform));
        var boost = new GameObject("boostText", typeof(RectTransform));
        var mover = tut.AddComponent<movePlayerInTut>();
        mover.SendMessage("Start");
        var move = typeof(movePlayerInTut).GetMethod("moveLeft_Right", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        move.Invoke(mover, new object[] { new Vector3(0f, -99f, 0f) });
        float low = tut.transform.position.y;
        move.Invoke(mover, new object[] { new Vector3(0f, 99f, 0f) });
        float high = tut.transform.position.y;
        Check("the tutorial's ship has the same reach (" + F(low) + ".." + F(high) + ")",
              Mathf.Approximately(low, ShipReach.Bottom) && Mathf.Approximately(high, ShipReach.Top));
        Object.DestroyImmediate(tut);
        Object.DestroyImmediate(hype);
        Object.DestroyImmediate(boost);
    }

    // The ship's drawing against HullBelow / HullAbove: every hull as gameS1
    // spawns it, its nozzle flames at their full (boost) size.
    static void HullMeasured()
    {
        Use(FitDevice.Find("iphone-15"));
        float below = 0f, above = 0f;
        string worst = "";
        foreach (int id in ShipId.All)
        {
            var spawner = new GameObject("~ReachSpawner").AddComponent<spawnShips>();
            var ship = spawner.Spawn(id);
            if (ship == null) { Object.DestroyImmediate(spawner.gameObject); continue; }
            ship.transform.position = Vector3.zero;
            foreach (var r in ship.GetComponentsInChildren<SpriteRenderer>(true))
            {
                if (r.sprite == null) continue;
                bool hull = r.gameObject == ship, flame = r.name.StartsWith("Nozzle");
                if (!hull && !flame) continue;
                if (-r.bounds.min.y > below) { below = -r.bounds.min.y; worst = ShipId.ObjectName(id) + " " + r.name; }
                above = Mathf.Max(above, r.bounds.max.y);
            }
            Object.DestroyImmediate(ship);
            Object.DestroyImmediate(spawner.gameObject);
        }
        Check("every hull and its flame fit ShipReach's numbers: " + F(below) + " u under the centre (" + worst + ") <= " + F(ShipReach.HullBelow) +
              ", " + F(above) + " above <= " + F(ShipReach.HullAbove), below <= ShipReach.HullBelow && above <= ShipReach.HullAbove && below > .5f);
    }

    static void ReachFollowsTheSafeArea()
    {
        var d = FitDevice.Find("flip7-1080x2520");
        Use(d);
        float unit = 2f * cam.orthographicSize / d.h;
        float last = float.NaN, worstJump = 0f;
        bool tracks = true;
        for (int inset = 0; inset <= 300; inset += 5)
        {
            screen.Dispose();
            screen = ScreenInfo.Override(d.w, d.h, new Rect(0, inset, d.w, d.h - inset - d.top), d.Cutouts);
            float b = movePlayer.ClampPlayerY(-99f);
            tracks &= Mathf.Abs(b - (CameraFit.ViewBottom + inset * unit + ShipReach.HullBelow + ShipReach.BottomMargin)) < 1e-3f;
            if (!float.IsNaN(last)) worstJump = Mathf.Max(worstJump, Mathf.Abs(b - last));
            last = b;
        }
        Check("the floor follows the safe area's bottom inset as it changes, pixel by pixel (largest step " + F(worstJump) + " u for 5 px)",
              tracks && worstJump <= 5f * unit + 1e-4f);

        // a foldable folding and unfolding: the camera and the screen change together
        var folded = FitDevice.Find("flip7-1080x2520");
        var open = FitDevice.Find("fold-1812x2176");
        bool same = true;
        foreach (var s in new[] { folded, open, folded, open })
        {
            Use(s);
            var f = PlayField.Live;
            same &= Mathf.Abs(f.ShareOf(ShipReach.Top) - ShipReach.TopShare) < .001f && ShipReach.Bottom > f.safeBottom;
        }
        Check("folding and unfolding: the range is recomputed for the new screen at once (the same share of each view)", same);
    }

    // ---- 2: pilots against a ship at the top of its reach --------------------------------------

    static void PilotsAgainstAShipAtTheTop()
    {
        var ship = new GameObject("~ReachShip").transform;
        int flights = 0, windups = 0, closeWindups = 0, bodyOnHull = 0;
        string firstClose = null, firstBody = null;
        foreach (var id in new[] { "and-1080x1920", "flip7-1080x2520", "iphone-15" })
        {
            Use(FitDevice.Find(id));
            float top = ShipReach.Top;
            float view = EnemyBrain.ViewScale;
            foreach (var def in EnemyRoster.All)
            {
                var b = def.Behaviour;
                if (b == null || !b.IsPilot || def.role == EnemyRole.Chaser || b.entry == PilotEntry.Descend) continue;
                foreach (float x in new[] { 0f, -1.4f })
                {
                    Clear();
                    moveBackGround.speed = .3f;
                    ship.position = new Vector3(x, top, 0f);
                    EliteSystem.PlayerOverride = ship;
                    UnityEngine.Random.InitState(def.key.GetHashCode() ^ (int)(x * 10f));
                    var go = EnemyFactory.Create(def, new Vector3(x, CameraFit.ViewTop + PilotAirspace.WaitAbove, 0f), Quaternion.identity);
                    var brain = go.GetComponent<EnemyBrain>();
                    brain.TargetOverride = ship;
                    Vector2 half = SpawnSpace.BodyHalf(def);
                    float clock = 900f;
                    var was = EnemyBrain.Phase.Idle;
                    flights++;
                    for (int i = 0; i < 60 * 14 && brain.Stage != EnemyBrain.PilotStage.Gone; i++)
                    {
                        clock += Dt;
                        SpawnSpace.ClockOverride = clock;
                        brain.Step(Dt);
                        EliteSystem.Step(Dt);
                        if (brain.Stage == EnemyBrain.PilotStage.Gone) break;
                        Vector3 p = go.transform.position;
                        if (brain.State == EnemyBrain.Phase.Windup && was != EnemyBrain.Phase.Windup && b.attack != EnemyAttack.Cross)
                        {
                            windups++;
                            float dy = p.y - ship.position.y, dist = Vector2.Distance(p, ship.position);
                            if (dy < EnemyBrain.MinFireAbove * view - 1e-3f || dist < EnemyBrain.MinFireDistance * view - 1e-3f)
                            {
                                closeWindups++;
                                if (firstClose == null) firstClose = id + " " + def.key + " dy " + F(dy) + " dist " + F(dist);
                            }
                        }
                        was = brain.State;
                        bool calm = (brain.Stage == EnemyBrain.PilotStage.Entering || brain.Stage == EnemyBrain.PilotStage.Engaging) &&
                                    brain.State == EnemyBrain.Phase.Idle;
                        if (calm && Mathf.Abs(p.x - ship.position.x) < half.x + .29f && p.y - half.y < ship.position.y + ShipReach.HullAbove)
                        {
                            bodyOnHull++;
                            if (firstBody == null) firstBody = id + " " + def.key + " " + brain.Stage + " at " + F(p.y) + " (body bottom " + F(p.y - half.y) +
                                                               ", hull top " + F(ship.position.y + ShipReach.HullAbove) + ")";
                        }
                    }
                    SpawnSpace.ClockOverride = null;
                    Object.DestroyImmediate(go);
                }
            }
        }
        Clear();
        EliteSystem.PlayerOverride = null;
        Object.DestroyImmediate(ship.gameObject);
        Log("pilots v a ship at the ceiling: " + flights + " flights, " + windups + " windups");
        Check("every pilot's windup starts at least " + EnemyBrain.MinFireAbove + " x view above and " + EnemyBrain.MinFireDistance +
              " x view from a ship at the ceiling (" + (windups - closeWindups) + "/" + windups + (firstClose != null ? ", first " + firstClose : "") + ")",
              closeWindups == 0 && flights > 50);
        Check("no pilot's body comes onto a ship parked at the ceiling while entering, dipping or holding station (" + bodyOnHull + " frames" +
              (firstBody != null ? ", first " + firstBody : "") + ")", bodyOnHull == 0);

        // every station, on every phone: its body above the hull at the ceiling
        bool clear = true;
        string worst = null;
        float least = float.MaxValue;
        foreach (var id in Shapes)
        {
            Use(FitDevice.Find(id));
            float view = EnemyBrain.ViewScale, hull = ShipReach.Top + ShipReach.HullAbove;
            foreach (var def in EnemyRoster.All)
            {
                var b = def.Behaviour;
                if (b == null || !b.IsPilot || def.role == EnemyRole.Chaser || b.entry == PilotEntry.Descend) continue;
                float station = CameraFit.ViewTop - b.stationDepth * view - SpawnSpace.BodyHalf(def).y;
                if (station - hull < least) { least = station - hull; worst = id + " " + def.key; }
                clear &= station > hull;
            }
        }
        Check("every pilot's station sits above the hull at the ceiling, on every shape (closest " + F(least) + " u: " + worst + ")", clear);
    }

    // ---- 3: chasers coming up under a ship at the bottom of its reach -------------------------

    static float ChaserContact(EnemyDef def, float shipY, out float visible)
    {
        Clear();
        var ship = new GameObject("~ReachShipLow").transform;
        ship.position = new Vector3(0f, shipY, 0f);
        // enmiesOnBoard.TrySpawnChaser: one unit under the view, here straight under the ship
        var go = EnemyFactory.Create(def, new Vector3(0f, CameraFit.ViewBottom - 1f, 0f), Quaternion.identity);
        var chaser = go.GetComponent<ChaserEnemy>();
        chaser.Target = ship;
        Vector2 half = SpawnSpace.BodyHalf(def);
        float t = 0f;
        visible = 0f;
        for (; t < 8f; t += Dt)
        {
            chaser.Step(Dt);
            Vector3 p = go.transform.position;
            if (p.y + half.y > CameraFit.ViewBottom) visible += Dt;
            if (Mathf.Abs(p.x - ship.position.x) < half.x + .29f && p.y + half.y > ship.position.y - .29f) break;
        }
        Object.DestroyImmediate(go);
        Object.DestroyImmediate(ship.gameObject);
        return t;
    }

    static void ChasersAgainstAShipAtTheBottom()
    {
        foreach (var def in EnemyRoster.All)
        {
            if (def.role != EnemyRole.Chaser) continue;
            UseAuthored();
            float authoredVis;
            float authored = ChaserContact(def, Mathf.Max(ShipReach.LegacyBottom, CameraFit.ViewBottom + ShipReach.FingerOffset), out authoredVis);
            bool ok = true;
            string line = def.key + ": a chaser from under the view reaches a ship at the floor in " + F(authored) + " s (" + F(authoredVis) + " s in view) as authored";
            foreach (var id in new[] { "and-1080x1920", "flip7-1080x2520", "iphone-15", "and-1080x1920-navbar" })
            {
                Use(FitDevice.Find(id));
                float vis;
                float t = ChaserContact(def, ShipReach.Bottom, out vis);
                line += "; " + id + " " + F(t) + " s (" + F(vis) + " s in view)";
                ok &= t >= authored - .05f && vis >= authoredVis - .05f;
            }
            Check(line + ": never sooner, never less warning", ok);
        }
        Clear();
    }

    // ---- 4: what the ship has to reach ---------------------------------------------------------

    static void EverythingReachable()
    {
        bool portal = true, atoms = true, tutorial = true, start = true, elites = true;
        string log = "";
        var defs = new List<EliteDef>();
        EliteCatalog.ForWorld(3, defs);
        var standIn = new GameObject("~ReachElitePilot").transform;
        foreach (var id in Shapes)
        {
            Use(FitDevice.Find(id));
            var f = PlayField.Live;
            float bottom = ShipReach.Bottom, top = ShipReach.Top;
            float station = Portal.StationY;
            portal &= station >= bottom && station <= top && Portal.HomeMaxX + Portal.DriftHalf <= ShipReach.HalfWidth;
            // atoms fall through the whole view; their soft ceiling is never above the ship's reach
            atoms &= AtomWander.Ceiling(CameraFit.ViewTop) <= top + AtomWander.ShipReachAbove + 1e-3f && AtomWander.Ceiling(CameraFit.ViewTop) > bottom;
            // the tutorial's atoms hover in the lower part of the screen: inside the reach, wherever the ship is
            float lowHover = float.MaxValue, highHover = float.MinValue, oldMissed = 0f;
            for (float y = bottom; y <= top; y += .25f)
            {
                float h = TutorialAtomDrift.HoverLine(f.bottom, f.top, y, true);
                lowHover = Mathf.Min(lowHover, h); highHover = Mathf.Max(highHover, h);
                if (h < ShipReach.LegacyBottom) oldMissed = Mathf.Max(oldMissed, ShipReach.LegacyBottom - h);
            }
            tutorial &= lowHover >= bottom && highHover <= top;
            start &= spawnShips.SpawnPoint.y >= bottom && spawnShips.SpawnPoint.y <= top;
            log += " " + id + ": portal " + P(f.ShareOf(station)) + ", tutorial hover " + F(lowHover) + ".." + F(highHover) +
                   (oldMissed > 0f ? " (the old floor was " + F(oldMissed) + " u above its lowest)" : "") + ", start " + P(f.ShareOf(spawnShips.SpawnPoint.y)) + ";";
            // an elite lifting off joins inside the view and away from a ship at either end of its reach
            foreach (float y in new[] { bottom, top })
            {
                standIn.position = new Vector3(0f, y, 0f);
                EliteSystem.PlayerOverride = standIn;
                foreach (var def in defs)
                {
                    var j = EliteDirector.JoinPoint(def, new Vector2(1f, 0f), new Vector3(1f, f.At(.4f), 0f));
                    elites &= j.y < f.top && j.y > f.bottom && Vector2.Distance(j, standIn.position) >= EliteShip.MinJoinDistance - 1e-3f;
                }
            }
        }
        EliteSystem.PlayerOverride = null;
        Object.DestroyImmediate(standIn.gameObject);
        Log("reachable:" + log);
        Check("the open portal's station is inside the reach on every shape (x home + drift <= 2.4)", portal);
        Check("pickup atoms' soft ceiling is inside the reach on every shape (they fall through the whole view)", atoms);
        Check("the tutorial's hovering atoms are inside the reach on every shape, wherever the ship is", tutorial);
        Check("the ship's start (y " + spawnShips.SpawnPoint.y + ") is inside the reach on every shape", start);
        Check("an elite's join point stays in view and " + EliteShip.MinJoinDistance + " u from a ship at the floor or the ceiling", elites && defs.Count > 0);
    }

    // ---- 5: the boss at rest ---------------------------------------------------------------------

    static void BossRestsInTheUpperBand()
    {
        foreach (var id in Shapes)
        {
            var d = FitDevice.Find(id);
            Use(d);
            var f = PlayField.Live;
            bool phone = d.h > 1.5f * d.w;
            float y = BossConfig.BossY, reach = BossConfig.TopReach;
            float ceiling = y - BossConfig.ShipCeilingBelowFor(f);
            BossConfig.FitToView = false;
            float oldY = BossConfig.BossY;
            BossConfig.FitToView = true;
            Log(string.Format("{0}: boss rests at {1} ({2} of the view; was {3} = {4}); its cell's top at the top of its sway {5}, HUD band from {6}; " +
                              "shots x{7}; the ship's ceiling in the fight {8} ({9}; out of a fight {10})",
                              id, F(y), P(f.ShareOf(y)), F(oldY), P(f.ShareOf(oldY)), F(y + reach), F(f.bandBottom), BossConfig.ShotScale.ToString("F2"),
                              F(ceiling), P(f.ShareOf(ceiling)), P(f.ShareOf(ShipReach.TopFor(f)))));
            Check(id + ": the boss's cell, at the top of its sway, is " + BossConfig.BossTopMargin + " u under the HUD band",
                  Mathf.Abs(y + reach + BossConfig.BossTopMargin - f.bandBottom) < 1e-3f || y <= f.At(.5f) + 1e-3f);
            Check(id + ": ... in the upper part of the screen (" + P(f.ShareOf(y)) + ") and fully visible",
                  f.ShareOf(y) > .6f && y + reach < f.safeTop && y - reach > f.bottom);
            float unit = f.Height / d.h;
            bool cut = true;
            foreach (var c in d.Cutouts)
            {
                float cx0 = (c.xMin - d.w * .5f) * unit, cx1 = (c.xMax - d.w * .5f) * unit;
                if (cx1 < -BossConfig.BossWorldSize * .5f - 1.4f || cx0 > BossConfig.BossWorldSize * .5f + 1.4f) continue;
                cut &= y + reach < f.bottom + c.yMin * unit;
            }
            Check(id + ": ... under every display cutout", cut);
            Check(id + ": it warps in from above the view", BossActor.ArrivalY - BossConfig.BossWorldSize * .5f > f.top);
            Check(id + ": the ship keeps room under it in the fight (ceiling " + P(f.ShareOf(ceiling)) + " of the view)",
                  ceiling > ShipReach.Bottom + ShipReach.MinSpan && f.ShareOf(ceiling) > (phone ? .5f : 1f / 3f));
        }
        UseAuthored();
        Check("in the editor's authored 10 u view (no HUD band) it rests where it always did (" + F(BossConfig.BossY) + ") and shots keep their speed",
              Mathf.Abs(BossConfig.BossY - BossConfig.AuthoredBossY) < 1e-3f && Mathf.Abs(BossConfig.ShotScale - 1f) < 1e-4f &&
              Mathf.Abs(BossConfig.LobTargetY + 3.2f) < 1e-3f);
        Check("no fixed rest height or 'camera top is +5' left in BossConfig",
              !System.IO.File.ReadAllText("Assets/Scripts/Bosses/BossConfig.cs").Contains("camera top is +5"));
    }

    // ---- 6: attack timing ---------------------------------------------------------------------

    // A fresh empty scene for each fight (a previous fight's pool must not
    // linger: its parked shots would clash with the new ones), the camera
    // as it was.
    static void FreshFightScene()
    {
        float ortho = cam.orthographicSize, aspect = cam.aspect;
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        BossEncounter.ResetRun();
        BossRails.Reset();
        var camGo = new GameObject("Main Camera", typeof(Camera));
        camGo.tag = "MainCamera";
        cam = camGo.GetComponent<Camera>();
        cam.orthographic = true;
        cam.orthographicSize = ortho;
        cam.aspect = aspect;
        camGo.transform.position = new Vector3(0f, 0f, -10f);
        PlayField.Reset();
        if (ScreenInfo.Overridden && ScreenInfo.Height > ScreenInfo.Width) PlayField.UseHud(hudRect);
        buttonClicks.playerDied = false;
        score.pauseCounter = 0;
    }

    static BossEncounter Fight(int world, int attack, Vector3 shipAt)
    {
        FreshFightScene();
        var ship = new GameObject("~ReachBossShip").AddComponent<movePlayer>();
        ship.transform.position = shipAt;
        moveBackGround.speed = .2f;
        PlayerPrefs.SetInt(DeveloperUnlocks.EnabledKey, 0);
        PlayerPrefs.SetInt(WorldManager.PrefsCurrentWorld, world);
        BossEncounter.Begin(world, null);
        var e = BossEncounter.Instance;
        e.Step(.1f, 1f);
        for (int i = 0; i < 400 && e.State == BossEncounter.Phase.Intro; i++) e.Step(.1f, 1f);
        e.Actor.ForcedAttack = attack;
        return e;
    }

    // Seconds from an attack's first shot (or its laser going live) to the
    // first moment it reaches the ship's row; NaN if it never does.
    static float TimeToRow(int world, int attack, out string how)
    {
        var f = PlayField.Live;
        float row = f.At(BossConfig.ShipRowShare);
        var e = Fight(world, attack, new Vector3(.6f, row, 0f));
        var a = e.Boss.attacks[attack];
        var launched = new Dictionary<BossProjectile, float>();
        float clock = 0f, liveAt = -1f;
        how = "";
        for (int i = 0; i < 60 * 8; i++)
        {
            e.Step(Dt, 1f);
            clock += Dt;
            if (a.kind == BossAttackKind.Beam)
            {
                foreach (var b in e.Pool.Beams)
                {
                    if (b == null || !b.Live) continue;
                    if (liveAt < 0f) liveAt = clock - Dt;
                    Vector3 end = b.Origin + (Vector3)(b.Direction * b.Length);
                    if (end.y <= row) { how = "beam tip"; return clock - liveAt; }
                    if (b.Length >= b.Reach - 1e-3f && b.EndsOnRail) { how = "beam on the rail at " + F(f.ShareOf(end.y)); return clock - liveAt; }
                }
                continue;
            }
            foreach (var s in e.Pool.Shots)
            {
                if (s == null || !s.Active) continue;
                if (!launched.ContainsKey(s)) launched[s] = clock - Dt;
                if (s.transform.position.y <= row) { how = a.kind.ToString().ToLowerInvariant(); return clock - launched[s]; }
            }
        }
        return float.NaN;
    }

    static void BossAttackTiming()
    {
        for (int world = 0; world < BossCatalog.All.Length; world++)
        {
            var boss = BossCatalog.ForWorld(world);
            for (int ai = 0; ai < boss.attacks.Length; ai++)
            {
                string how;
                UseAuthored();
                float tuned = TimeToRow(world, ai, out how);
                string line = boss.artKey + " " + boss.attacks[ai].name + " (" + how + "): authored view " + F(tuned) + " s to the ship's row";
                bool ok = !float.IsNaN(tuned);
                foreach (var id in Phones)
                {
                    Use(FitDevice.Find(id));
                    BossConfig.FitToView = false;
                    ShipReach.FitToView = false;
                    float before = TimeToRow(world, ai, out how);
                    BossConfig.FitToView = true;
                    ShipReach.FitToView = true;
                    float after = TimeToRow(world, ai, out how);
                    line += "; " + id + " " + F(before) + " -> " + F(after);
                    bool rail = how.StartsWith("beam on the rail");
                    ok &= !float.IsNaN(after) && (rail ? after <= tuned * 1.12f + .05f : Mathf.Abs(after - tuned) <= tuned * .12f + .05f);
                }
                Check(line + ": within 12% on every phone", ok);
                BossEncounter.ResetRun();
            }
        }
    }

    // ---- 7: the whole fight on the phones ------------------------------------------------------

    static void BossFightOnEveryPhone()
    {
        foreach (var id in Phones)
        {
            Use(FitDevice.Find(id));
            var f = PlayField.Live;
            for (int world = 0; world < BossCatalog.All.Length; world++)
            {
                var boss = BossCatalog.ForWorld(world);
                for (int ai = 0; ai < boss.attacks.Length; ai++)
                {
                    var a = boss.attacks[ai];
                    var e = Fight(world, ai, new Vector3(-.8f, f.At(.25f), 0f));
                    bool onScreen = true, beamEnds = true, rests = Mathf.Abs(e.Actor.transform.position.y - BossConfig.BossY) < .3f;
                    int bounced = 0, stuck = 0;
                    var seen = new HashSet<BossProjectile>();
                    for (int i = 0; i < 60 * 9 && e.Actor.AttacksStarted < 2; i++)
                    {
                        e.Step(Dt, 1f);
                        foreach (int part in a.parts)
                        {
                            Vector3 m = e.Actor.Emitter(part);
                            onScreen &= m.y < f.top && m.y > f.bottom && Mathf.Abs(m.x) < 3.72f;
                        }
                        Vector3 bp = e.Actor.transform.position;
                        onScreen &= bp.y + BossConfig.BossWorldSize * .5f < f.bandBottom + 1e-3f;
                        foreach (var b in e.Pool.Beams)
                        {
                            if (b == null || !b.Active) continue;
                            onScreen &= b.Origin.y < f.top && b.Origin.y > f.bottom;
                            if (b.Live && b.Length >= b.Reach - 1e-3f)
                            {
                                Vector3 end = b.Origin + (Vector3)(b.Direction * b.Length);
                                beamEnds &= b.EndsOnRail ? Mathf.Abs(Mathf.Abs(end.x) - BossRails.InnerEdge) < .01f : end.y < f.bottom;
                            }
                        }
                        foreach (var s in e.Pool.Shots) if (s != null && s.Active) { seen.Add(s); bounced = Mathf.Max(bounced, s.Bounces); }
                    }
                    for (int i = 0; i < 60 * 12 && e.Pool.ActiveShots > 0; i++) e.Pool.Step(Dt);
                    stuck = e.Pool.ActiveShots;
                    foreach (var s in e.Pool.Shots) if (s != null) bounced = Mathf.Max(bounced, s.Bounces);
                    string tag = id + " " + boss.artKey + " " + a.name + ": ";
                    Check(tag + "the boss rests at its height, its muzzles and lasers' roots on screen, under the HUD band", rests && onScreen);
                    if (a.kind == BossAttackKind.Beam) Check(tag + "its lasers end on a rail or past the bottom of the view", beamEnds);
                    else
                    {
                        Check(tag + "every shot leaves the screen (" + seen.Count + " shots, " + stuck + " left)", seen.Count > 0 && stuck == 0);
                        if (a.rail == BossRailMode.Bounce && a.bounces > 0)
                            Check(tag + "its shots still ricochet off the rails (" + bounced + " ricochets)", bounced > 0);
                    }
                    BossEncounter.ResetRun();
                }
            }

            // arrival and retreat
            var r = Fight(0, 0, new Vector3(0f, f.At(.2f), 0f));
            bool landed = Mathf.Abs(r.Actor.transform.position.y - BossConfig.BossY) < .3f;
            r.Actor.BeginOutro(false);
            float lowest = float.MaxValue;
            for (int i = 0; i < 60 * 4 && r.Actor.State != BossActor.Mode.Gone; i++)
            {
                r.Actor.StepOutro(Dt, 0);
                lowest = Mathf.Min(lowest, r.Actor.transform.position.y);
            }
            Check(id + ": the boss lands at rest after its warp-in, dips (" + F(lowest) + ") and retreats out of the top",
                  landed && r.Actor.State == BossActor.Mode.Gone && lowest > BossConfig.BossY - .5f);
            BossEncounter.ResetRun();
        }

        // the ship's ceiling follows the boss in (and back out)
        Use(FitDevice.Find("flip7-1080x2520"));
        FreshFightScene();
        float free = ShipReach.Top;
        PlayerPrefs.SetInt(WorldManager.PrefsCurrentWorld, 2);
        BossEncounter.Begin(2, null);
        var enc = BossEncounter.Instance;
        float lastTop = ShipReach.Top, worstDrop = 0f, worstBossStep = 0f, lastBoss = float.NaN;
        bool before = true;
        for (int i = 0; i < 2000 && (i < 5 || enc.State == BossEncounter.Phase.Intro || enc.State == BossEncounter.Phase.Pending); i++)
        {
            enc.Step(Dt, 1f);
            float t = ShipReach.Top;
            if (enc.Actor != null && enc.Actor.State == BossActor.Mode.Hidden) before &= Mathf.Approximately(t, free);
            if (enc.Actor != null && !float.IsNaN(lastBoss)) worstBossStep = Mathf.Max(worstBossStep, Mathf.Abs(enc.Actor.transform.position.y - lastBoss));
            worstDrop = Mathf.Max(worstDrop, lastTop - t);
            lastTop = t;
            if (enc.Actor != null) lastBoss = enc.Actor.transform.position.y;
        }
        float fight = ShipReach.Top;
        Check("the ship's ceiling comes down with the arriving boss, never faster than the boss moves (" + F(free) + " -> " + F(fight) +
              ", largest step " + F(worstDrop) + " u, boss " + F(worstBossStep) + " u)",
              before && fight < free && worstDrop <= worstBossStep + 1e-3f && Mathf.Abs(fight - (BossConfig.BossY - BossConfig.ShipCeilingBelowFor(PlayField.Live))) < 1e-3f);
        BossEncounter.ResetRun();
        Check("... and is back once the boss is gone (" + F(ShipReach.Top) + ")", Mathf.Approximately(ShipReach.Top, free));
    }

    // BossAttackTest's dodge simulation, on two phone shapes: ship lines at
    // the authored lines' shares of the view and at the fight's ceiling.
    const float ShipRadius = .28f, MaxShipSpeed = 7f;

    static void BossPatternsLeaveAWayThrough()
    {
        foreach (var id in new[] { "and-1080x1920", "flip7-1080x2520" })
        {
            Use(FitDevice.Find(id));
            var f = PlayField.Live;
            for (int world = 0; world < BossCatalog.All.Length; world++)
            {
                var boss = BossCatalog.ForWorld(world);
                for (int ai = 0; ai < boss.attacks.Length; ai++)
                {
                    float ceiling = BossConfig.BossY - BossConfig.ShipCeilingBelowFor(f);
                    foreach (float line in new[] { f.At(.15f), f.At(.3f), ceiling })
                    {
                        var e = Fight(world, ai, new Vector3(0f, line, 0f));
                        const int N = 97;
                        var reach = new bool[N];
                        var next = new bool[N];
                        for (int i = 0; i < N; i++) reach[i] = true;
                        int fewest = N;
                        float step = 4.8f / (N - 1);
                        int span = Mathf.Max(1, Mathf.FloorToInt(MaxShipSpeed * Dt / step));
                        bool alive = true;
                        for (int s = 0; s < 60 * 9 && alive && e.Actor.AttacksStarted < 3; s++)
                        {
                            e.Step(Dt, 1f);
                            int count = 0;
                            for (int i = 0; i < N; i++)
                            {
                                next[i] = false;
                                if (!Safe(e, new Vector2(-2.4f + i * step, line))) continue;
                                for (int j = Mathf.Max(0, i - span); j <= Mathf.Min(N - 1, i + span); j++)
                                    if (reach[j]) { next[i] = true; break; }
                                if (next[i]) count++;
                            }
                            fewest = Mathf.Min(fewest, count);
                            alive = count > 0;
                            var t = reach; reach = next; next = t;
                        }
                        bool through = alive && fewest * step >= .15f;
                        if (line != ceiling)
                            Check(id + " " + boss.artKey + " " + boss.attacks[ai].name + " (ship line " + P(f.ShareOf(line)) + " of the view): a way through, narrowest " +
                                  F(fewest * step) + " u", through);
                        else
                        {
                            // right under the boss a wide fan can be a wall (it always could: the old
                            // reach went into the boss). Then the ship must be able to drop to the
                            // lower line within the attack's tell.
                            float drop = (line - f.At(.3f)) / MaxShipSpeed;
                            Check(id + " " + boss.artKey + " " + boss.attacks[ai].name + " (at the fight's ceiling, " + P(f.ShareOf(line)) + "): " +
                                  (through ? "a way through, narrowest " + F(fewest * step) + " u"
                                           : "no way through this close, but the ship drops clear in " + F(drop) + " s of its " + F(boss.attacks[ai].tellSeconds) + " s tell"),
                                  through || drop <= boss.attacks[ai].tellSeconds);
                        }
                        BossEncounter.ResetRun();
                    }
                }
            }
        }
    }

    static bool Safe(BossEncounter e, Vector2 p)
    {
        foreach (var s in e.Pool.Shots)
        {
            if (s == null || !s.Active || s.Hitbox == null) continue;
            if (((Vector2)s.transform.position - p).sqrMagnitude < (s.Radius + ShipRadius) * (s.Radius + ShipRadius)) return false;
        }
        foreach (var b in e.Pool.Beams)
        {
            if (b == null || !b.Live || b.Hitbox == null) continue;
            Vector2 o = b.Origin, d = b.Direction;
            float t = Mathf.Clamp(Vector2.Dot(p - o, d), 0f, b.Length);
            float half = b.Width * BossConfig.BeamHitFraction * .5f;
            if ((o + d * t - p).sqrMagnitude < (half + ShipRadius) * (half + ShipRadius)) return false;
        }
        return true;
    }

    // ---- 8: cost and pause ---------------------------------------------------------------------

    static float sink;

    static void NothingAllocatesNothingMovesWhilePaused()
    {
        long control;
        bool meter = TestHarness.AllocMeterWorks(out control);
        Check("the allocation meter sees a deliberate allocation (" + control + " bytes)", meter);

        Use(FitDevice.Find("iphone-15"));
        var e = Fight(1, 2, new Vector3(0f, -4f, 0f));
        for (int i = 0; i < 90; i++) e.Step(Dt, 1f);
        Action work = () =>
        {
            for (int i = 0; i < 200; i++)
            {
                sink += movePlayer.ClampPlayerY(i * .1f - 10f) + ShipReach.Top + ShipReach.Bottom + ShipReach.EntryFloor;
                sink += BossConfig.BossY + BossConfig.ShotScale + BossConfig.LobTargetY + PlayField.Live.bandBottom;
                sink += AtomWander.Ceiling(8f);
            }
        };
        work();
        long bytes = TestHarness.AllocatedBytes(work);
        Check("the reach, the band, the boss's rest and shot scale read every frame allocate nothing (" + bytes + " bytes over 200 frames, mid-fight)",
              meter && bytes == 0);
        // the pool's steady steps (beams grow at the view's rate): nothing
        Action pool = () => { for (int i = 0; i < 30; i++) e.Pool.Step(Dt); };
        pool();
        long poolBytes = TestHarness.AllocatedBytes(pool);
        Check("a boss fight's steady pool steps (beams growing at the view's rate) allocate nothing (" + poolBytes + " bytes over 30 frames)", meter && poolBytes == 0);
        // whole frames, firing included: no more than the same frames with the old constants
        Action fight = () => { for (int i = 0; i < 30; i++) e.Step(Dt, 1f); };
        for (int i = 0; i < 600; i++) e.Step(Dt, 1f);
        long fightBytes = TestHarness.AllocatedBytes(fight);
        BossConfig.FitToView = false;
        ShipReach.FitToView = false;
        var old = Fight(1, 2, new Vector3(0f, -4f, 0f));
        for (int i = 0; i < 690; i++) old.Step(Dt, 1f);
        Action oldFight = () => { for (int i = 0; i < 30; i++) old.Step(Dt, 1f); };
        long oldBytes = TestHarness.AllocatedBytes(oldFight);
        BossConfig.FitToView = true;
        ShipReach.FitToView = true;
        Log("whole boss-fight frames, firing included: " + fightBytes + " bytes over 30 frames fitted to the view, " + oldBytes + " with the old constants");
        Check("fitting the fight to the view adds no allocation to a whole frame (" + fightBytes + " vs " + oldBytes + " bytes over 30 frames)",
              meter && fightBytes <= oldBytes);
        e = Fight(1, 2, new Vector3(0f, -4f, 0f));
        for (int i = 0; i < 90; i++) e.Step(Dt, 1f);

        // paused: dt 0 moves nothing, and the ceiling stays where it is
        Vector3 boss = e.Actor.transform.position;
        float top = ShipReach.Top;
        var shots = new List<Vector3>();
        foreach (var s in e.Pool.Shots) if (s != null && s.Active) shots.Add(s.transform.position);
        for (int i = 0; i < 120; i++) e.Step(0f, 0f);
        bool still = e.Actor.transform.position == boss && Mathf.Approximately(ShipReach.Top, top);
        int k = 0;
        foreach (var s in e.Pool.Shots) if (s != null && s.Active) still &= k < shots.Count && s.transform.position == shots[k++];
        Check("while the world is frozen nothing moves: the boss, its shots, the ship's ceiling", still);
        BossEncounter.ResetRun();
    }
}
