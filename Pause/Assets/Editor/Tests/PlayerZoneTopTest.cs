using System;
using System.Reflection;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

// "The player should be able to go all the way up to the score board, the
// ship's top right under it, and ram bosses and enemies in that zone with the
// shield" (ShipReach, BossRam, TopGuard).
//
//   ZONE      on every screen shape the ship's top edge sits TopGap under the
//             HUD band's bottom (the score read-out / home + replay icons,
//             measured, not a constant); nothing goes above it; the touch
//             mapping (finger + FingerOffset, movePlayer.ClampPlayerY) and a
//             pause-jump land on it and never past it; the old 70% limit is
//             logged beside the new one
//   HUD       the HUD stays on top: an overlay canvas, the quick actions keep
//             the press (movePlayer's guard), and the finger that puts the
//             ship at the top is under the band, not on it
//   RAM       a shielded ship rams an enemy at the top and kills it (paid); an
//             unshielded one loses a heart; a shielded ram on a boss is a hit
//             of BossRam.RamWeight, costs the shield, has a cooldown and
//             leaves the body; an unshielded touch hurts the pilot and the
//             boss not at all; the boss ceiling opens for a shield and closes
//             before it ends
//   HOSTILES  pilots still hold above the hull and inside a pause jump's blast
//             with the ship at the new top
//   FAIR      nothing scrolling is born over a ship parked at the top with
//             less than TopGuard.ReactSeconds to see it
//   COST      none of it allocates per frame
public static class PlayerZoneTopTest
{
    static int fails;
    const BindingFlags Inst = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    static readonly MethodInfo Trigger = typeof(collisionDetection).GetMethod("OnTriggerEnter2D", Inst);

    static void Check(string what, bool ok)
    {
        Debug.Log((ok ? "[ZONE] PASS  " : "[ZONE] FAIL  ") + what);
        if (!ok) fails++;
    }
    static void Log(string what) { Debug.Log("[ZONE] " + what); }
    static string F(float v) { return v.ToString("F2"); }
    static string P(float v) { return (v * 100f).ToString("F1") + "%"; }

    // 9:16, 9:19.5, 9:20 + gesture bar, 9:21 (the user's phone), 9:22, small phones, a foldable, tablets (3:4)
    static readonly string[] Shapes =
    {
        "and-480x854", "and-1080x1920", "and-1080x1920-navbar", "and-1080x2160", "and-1080x2340-notch", "and-1080x2400-corner",
        "and-1080x2400-gesture", "flip7-1080x2520", "and-1080x2640", "iphone-se", "iphone-13", "iphone-15", "iphone-16pm",
        "fold-1812x2176", "tab-1600x2560", "ipad-9", "ipad-pro",
    };

    static Camera cam;
    static HudStyler styler;
    static IDisposable screen;
    static Func<Vector2, Rect, Rect> hudRect;
    static float ortho0, aspect0;

    public static void Run() { TestHarness.Exit(Execute()); }

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
            styler = new GameObject("~ZoneHud").AddComponent<HudStyler>();
            styler.SendMessage("Start");
            Canvas.ForceUpdateCanvases();
            LayoutRebuilder.ForceRebuildLayoutImmediate(styler.HudRoot);
            CaptureHud();
            buttonClicks.playerDied = false;
            score.pauseCounter = 0;
            Time.timeScale = 1f;

            ZoneOnEveryShape();
            JumpAndTouch();
            HudStaysOnTop();
            ShieldedRamAtTheTop();
            HostilesStillReachable();
            FairSpawns();
            BossRam_();
            Allocations();
        }
        finally
        {
            Use(null);
            ShipReach.FitToView = true;
            collisionDetection.atomCheck = false;
            collisionDetection.cloakTimer = 0f;
            collisionDetection.invTimer = 0f;
            BossRam.Reset();
            BossEncounter.ResetRun();
            BossRails.Reset();
            EliteSystem.PlayerOverride = null;
            EliteSystem.Clear();
            PlayField.Reset();
            buttonClicks.playerDied = false;
            if (cam != null) { cam.orthographicSize = ortho0; cam.aspect = aspect0; }
        }
        Debug.Log("[ZONE] failures: " + fails);
        return fails;
    }

    // ---- fixtures ------------------------------------------------------------

    static void CaptureHud()
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

    // the old reach: 70% of the view (never into the band)
    static float OldTop(PlayField.Frame f)
    {
        return Mathf.Min(f.At(.7f), f.bandBottom - ShipReach.HullAbove - ShipReach.BottomMargin);
    }

    // ---- 1. the zone ---------------------------------------------------------

    static void ZoneOnEveryShape()
    {
        string table = "";
        foreach (var id in Shapes)
        {
            var d = FitDevice.Find(id);
            Use(d);
            var f = PlayField.Live;
            float top = ShipReach.Top, old = OldTop(f);
            table += string.Format("\n  {0} ({1}x{2}, 9:{3}): view {4} u, band from {5} ({6} of the view) | old top {7} ({8}) -> new top {9} ({10}), +{11} u (+{12} of the view)",
                                   id, d.w, d.h, F(9f * d.h / d.w), F(f.Height), F(f.bandBottom), P(f.ShareOf(f.bandBottom)),
                                   F(old), P(f.ShareOf(old)), F(top), P(f.ShareOf(top)), F(top - old), P(f.ShareOf(top) - f.ShareOf(old)));
            Check(id + ": the ship's top edge is " + F(ShipReach.TopGap) + " u under the HUD band's bottom (" + F(top + ShipReach.HullAbove) + " vs " + F(f.bandBottom) + ")",
                  f.hasBand && Mathf.Abs(top + ShipReach.HullAbove + ShipReach.TopGap - f.bandBottom) < .002f);
            Check(id + ": it reaches higher than the old limit (" + F(old) + " -> " + F(top) + ")", top > old - 1e-3f && top > ShipReach.Bottom + 1f);
            Check(id + ": nothing goes above it (ClampY, movePlayer)",
                  Mathf.Approximately(ShipReach.ClampY(99f), top) && Mathf.Approximately(movePlayer.ClampPlayerY(99f), top) &&
                  Mathf.Approximately(ShipReach.ClampY(top + .001f), top));
            Check(id + ": its range is still floor < top by a real span", top - ShipReach.Bottom >= ShipReach.MinSpan - 1e-3f);
            // the measured hull of the equipped ship at the top: its top pixel is under the band
            foreach (int ship in ShipId.All)
            {
                var hull = ShipHullArt.Rest(ship, 2);
                if (hull == null) continue;
                float s = shopingShips.NormalizedHullScale(hull) * ShipScale.Live;
                float topPx = top + hull.bounds.max.y * s;
                if (topPx > f.bandBottom + 1e-3f) { Check(id + ": ship " + ship + "'s hull stays under the band (" + F(topPx) + " vs " + F(f.bandBottom) + ")", false); break; }
            }
        }
        Log("ship top limit before -> after, per screen:" + table);
        Use(null);
    }

    // ---- 2. touch mapping and the pause jump ------------------------------------

    static void JumpAndTouch()
    {
        var go = new GameObject("~MoveZone", typeof(RectTransform));
        var mover = go.AddComponent<movePlayer>();
        var rt = new GameObject("~hype", typeof(RectTransform)).GetComponent<RectTransform>();
        typeof(movePlayer).GetField("hypeText", Inst).SetValue(mover, rt);
        typeof(movePlayer).GetField("boostText", Inst).SetValue(mover, rt);
        var move = typeof(movePlayer).GetMethod("moveLeft_Right", Inst);
        try
        {
            foreach (var id in Shapes)
            {
                var d = FitDevice.Find(id);
                Use(d);
                var f = PlayField.Live;
                float top = ShipReach.Top, hw = ShipReach.HalfWidth;
                // a finger at the very top of the screen, and one just far enough under the band
                move.Invoke(mover, new object[] { new Vector3(0f, f.top + 3f, 0f) });
                Check(id + ": a finger at the top edge puts the ship at the top limit (" + F(go.transform.position.y) + " vs " + F(top) + ")",
                      Mathf.Abs(go.transform.position.y - top) < 1e-3f);
                float fy = top - ShipReach.FingerOffset;
                move.Invoke(mover, new object[] { new Vector3(hw + 5f, fy, 0f) });
                Check(id + ": the finger " + F(ShipReach.FingerOffset) + " u under the top reaches it exactly, the side clamps (" + F(go.transform.position.y) + ", x " + F(go.transform.position.x) + ")",
                      Mathf.Abs(go.transform.position.y - top) < 1e-3f && Mathf.Abs(go.transform.position.x - hw) < 1e-3f);
                // pause-jump: wherever the finger lands, in or beyond the zone
                bool inside = true;
                for (int i = 0; i < 40; i++)
                {
                    var p = new Vector3(Mathf.Lerp(-9f, 9f, i / 39f), Mathf.Lerp(f.bottom - 3f, f.top + 3f, (i * 7 % 40) / 39f), 0f);
                    move.Invoke(mover, new object[] { p });
                    var q = go.transform.position;
                    inside &= q.y >= ShipReach.Bottom - 1e-3f && q.y <= top + 1e-3f && Mathf.Abs(q.x) <= hw + 1e-3f;
                }
                Check(id + ": a pause-jump lands inside the zone for any touch", inside);
                // the finger that parks the ship at the top is under the band (not on the HUD)
                float fingerPx = (fy - f.bottom) / f.Height * d.h;
                float bandPx = (f.bandBottom - f.bottom) / f.Height * d.h;
                Check(id + ": that finger is under the HUD band (" + fingerPx.ToString("F0") + " px < " + bandPx.ToString("F0") + " px)", fingerPx < bandPx);
            }
        }
        finally { Use(null); Object.DestroyImmediate(go); Object.DestroyImmediate(rt.gameObject); }
    }

    // ---- 3. the HUD stays on top and keeps its taps -------------------------------

    static void HudStaysOnTop()
    {
        var canvas = styler.HudRoot.GetComponentInParent<Canvas>().rootCanvas;
        Check("the HUD canvas is a screen overlay: it draws over the world, and the ship under it", canvas.renderMode == RenderMode.ScreenSpaceOverlay);
        string src = System.IO.File.ReadAllText("Assets/Scripts/Ship/movePlayer.cs");
        Check("a press on the home / replay icons still belongs to the UI (movePlayer's guard runs before steering)",
              src.IndexOf("IsScreenPointOnAction", StringComparison.Ordinal) > 0 &&
              src.IndexOf("IsScreenPointOnAction", StringComparison.Ordinal) < src.IndexOf("moveLeft_Right(fingerPos)", StringComparison.Ordinal));
        foreach (var id in Shapes)
        {
            var d = FitDevice.Find(id);
            Use(d);
            var f = PlayField.Live;
            var screenSize = new Vector2(d.w, d.h);
            var band = TopBand.FrameFor(d.Safe, screenSize, BossRails.InnerEdge, ScreenInfo.Cutouts);
            var actions = PauseQuickActions.ScreenRectFor(band, screenSize);
            float actionsBottomWorld = f.bottom + actions.yMin / d.h * f.Height;
            Check(id + ": the icons sit in the band the ship stops under (" + F(actionsBottomWorld) + " >= " + F(f.bandBottom) + ")",
                  actionsBottomWorld >= f.bandBottom - 1e-3f);
            // the ship's top edge never reaches the icons' rect
            Check(id + ": the ship at the top does not cover the icons (" + F(ShipReach.Top + ShipReach.HullAbove) + " <= " + F(actionsBottomWorld) + ")",
                  ShipReach.Top + ShipReach.HullAbove <= actionsBottomWorld + 1e-3f);
        }
        Use(null);
    }

    // ---- 4. rams in the top zone -----------------------------------------------

    static void Ram(DeathCrashTest.Rig r, GameObject go)
    {
        var col = go.GetComponent<Collider2D>();
        if (col == null) col = go.AddComponent<CircleCollider2D>();
        Trigger.Invoke(r.cd, new object[] { col });
    }

    static void ShieldedRamAtTheTop()
    {
        Use(FitDevice.Find("flip7-1080x2520"));
        int firstShip = 0;
        foreach (int s in ShipId.All) { firstShip = s; break; }
        var r = new DeathCrashTest.Rig(firstShip);
        int savedMax = collisionDetection.MAXLIFE;
        try
        {
            collisionDetection.MAXLIFE = 3;
            RunScore.EndRun(RunScore.RunId);
            RunScore.BeginRun(true, true);
            EnemySplit.ForceInEditor = true;
            EnemySplit.ChanceOverride = 0f;
            float top = ShipReach.Top;
            r.ship.transform.position = new Vector3(0f, top, 0f);
            collisionDetection.lifeCounter = 0;
            PlayerInvuln.Reset();

            // shielded: the enemy dies, paid, no heart
            collisionDetection.atomCheck = true;
            collisionDetection.invTimer = 5.8f;
            var rock = EnemyFactory.Create(EnemyRoster.One(0, EnemyRole.Fighter), r.ship.transform.position + Vector3.up * .3f, Quaternion.identity);
            ClearTarget.Ensure(rock);
            long before = RunScore.Total;
            Ram(r, rock);
            Check("shielded ram at the top limit: the enemy is destroyed, paid, no heart lost (" + before + " -> " + RunScore.Total + ")",
                  RunScore.Total > before && collisionDetection.lifeCounter == 0);
            collisionDetection.atomCheck = false;

            // unshielded: a heart
            var rock2 = EnemyFactory.Create(EnemyRoster.One(0, EnemyRole.Fighter), r.ship.transform.position + Vector3.up * .3f, Quaternion.identity);
            ClearTarget.Ensure(rock2);
            Ram(r, rock2);
            Check("unshielded contact at the top limit costs a heart", collisionDetection.lifeCounter == 1);
            if (rock2 != null) Object.DestroyImmediate(rock2);
        }
        finally
        {
            EnemySplit.ForceInEditor = false;
            EnemySplit.ChanceOverride = -1f;
            collisionDetection.MAXLIFE = savedMax;
            collisionDetection.lifeCounter = 0;
            collisionDetection.atomCheck = false;
            PlayerInvuln.Reset();
            r.Dispose();
            Use(null);
        }
    }

    // ---- 5. hostiles hold inside the new reach --------------------------------------

    static void HostilesStillReachable()
    {
        foreach (var id in new[] { "and-1080x1920", "flip7-1080x2520", "iphone-15", "ipad-9" })
        {
            Use(FitDevice.Find(id));
            float top = ShipReach.Top, hw = ShipReach.HalfWidth;
            bool body = true, jump = true, ram = true;
            string worst = null, jumpWorst = "";
            foreach (var def in EnemyRoster.All)
            {
                var b = def.Behaviour;
                if (b == null || !b.IsPilot || def.role == EnemyRole.Chaser || b.entry == PilotEntry.Descend) continue;
                float view = EnemyBrain.ViewScale;
                float station = CameraFit.ViewTop - b.stationDepth * view;
                float below = def.ColliderSize.y * .5f, drawn = SpawnSpace.BodyHalf(def).y;
                float hold = EnemyBrain.ReachHoldY(station, top, top, below, drawn, EnemyBrain.PatternUp(b), EnemyBrain.PatternDown(b));
                if (hold - drawn <= top + ShipReach.HullAbove) { body = false; worst = id + " " + def.key; }
                float bottomY = hold - EnemyBrain.PatternDown(b) - below;
                ram &= HostileReach.RamReaches(bottomY, top) || hold > station;
                bool j = HostileReach.JumpReaches(0f, bottomY, def.ColliderSize.x * .5f, top, hw);
                if (!j) jumpWorst += " " + def.key + "(bottom " + F(bottomY) + " top " + F(top) + " blast " + F(TeleportFx.BlastRadius) + ")";
                jump &= j;
            }
            Check(id + ": pilots hold above a ship at the new top" + (worst != null ? " (" + worst + ")" : ""), body);
            Check(id + ": ... and a pause-jump at the new top still reaches them" + jumpWorst, jump);
            Check(id + ": ... and a hull at the new top touches their collider", ram);
        }
        Use(null);
    }

    // ---- 6. fair spawns ---------------------------------------------------------------

    static void FairSpawns()
    {
        Use(FitDevice.Find("flip7-1080x2520"));
        var f = PlayField.Live;
        float spawn = f.top + .5f, hud35 = .35f * 30f, top = ShipReach.Top;
        var ship = new Vector3(0f, top, 0f);
        float hh = ShipScale.HullHalfWidth, ha = ShipReach.HullAbove;
        float t = (spawn - .5f - (top + ha)) / hud35;
        Log("a hazard born at the spawn line takes " + F(t) + " s at HUD 35 to the top of a ship parked at the top limit");
        Check("a scrolling hazard born over a ship parked at the top is not placed with less than " + F(TopGuard.ReactSeconds) + " s to see it (HUD 35)",
              TopGuard.Blocks(-.5f, .5f, spawn, .5f, ship, hud35, hh, ha));
        Check("... another column is free", !TopGuard.Blocks(2f, 3f, spawn, .5f, ship, hud35, hh, ha));
        Check("... at a slow scroll there is time, so it is placed", !TopGuard.Blocks(-.5f, .5f, spawn, .5f, ship, 2f, hh, ha));
        Check("... a ship low in its zone is not protected: a hazard has all the view to cross",
              !TopGuard.Blocks(-.5f, .5f, spawn, .5f, new Vector3(0f, ShipReach.Bottom + 1f, 0f), hud35, hh, ha));
        Use(null);
    }

    // ---- 7. rams on the boss -----------------------------------------------------------

    static void BossRam_()
    {
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
        moveBackGround.speed = .37f;
        Time.timeScale = 1f;
        PlayerPrefs.SetInt(DeveloperUnlocks.EnabledKey, 0);
        PlayerPrefs.SetInt(WorldManager.PrefsCurrentWorld, 0);
        foreach (var id in new[] { "and-1080x1920", "flip7-1080x2520", "iphone-15" })
        {
            Use(FitDevice.Find(id));
            BossEncounter.ResetRun();
            BossRam.Reset();
            BossRam.TestClock = 100f;
            BossEncounter.Begin(0, null);
            var e = BossEncounter.Instance;
            e.Step(.1f, 1f);
            for (int i = 0; i < 2000 && e.State == BossEncounter.Phase.Intro; i++) e.Step(.1f, 1f);
            e.Step(.5f, 1f);
            var f = PlayField.Live;
            float zone = ShipReach.TopFor(f);
            collisionDetection.atomCheck = false;
            collisionDetection.invTimer = 0f;
            float closed = ShipReach.Top;
            Check(id + ": unshielded, the boss keeps the ship under its muzzles (" + F(closed) + " < zone top " + F(zone) + ")", closed < zone - .3f);
            collisionDetection.atomCheck = true;
            collisionDetection.invTimer = 5.8f;
            Check(id + ": shielded, the whole zone is open (" + F(ShipReach.Top) + " = " + F(zone) + ")", Mathf.Abs(ShipReach.Top - zone) < 1e-3f);
            collisionDetection.invTimer = ShipReach.BossOpenLead * .5f;
            Check(id + ": with the shield nearly spent the ceiling is back (" + F(ShipReach.Top) + ")", Mathf.Abs(ShipReach.Top - closed) < 1e-3f);
            collisionDetection.atomCheck = false;

            // the rams
            var r = new DeathCrashTest.Rig(0);
            int savedMax = collisionDetection.MAXLIFE;
            try
            {
                collisionDetection.MAXLIFE = 3;
                collisionDetection.lifeCounter = 0;
                PlayerInvuln.Reset();
                var body = e.Actor.BodyHitbox;
                r.ship.transform.position = body.transform.position;
                collisionDetection.atomCheck = true;
                collisionDetection.invTimer = 5.8f;
                int hits = e.Hits; float dmg = e.Damage;
                Ram(r, body);
                Check(id + ": a shielded ram on the boss is one hit of " + BossRam.RamWeight + " (" + (e.Damage - dmg).ToString("F2") + "), no heart lost",
                      e.Hits == hits + 1 && Mathf.Abs(e.Damage - dmg - BossRam.RamWeight) < 1e-4f && collisionDetection.lifeCounter == 0);
                Check(id + ": ... it costs the shield " + BossRam.RamShieldCost + " s (" + F(collisionDetection.invTimer) + " left) and leaves the body standing",
                      Mathf.Abs(collisionDetection.invTimer - (5.8f - BossRam.RamShieldCost)) < 1e-3f && e.Actor.BodyHitbox == body);
                Ram(r, body);
                Check(id + ": ... a second ram inside the " + BossRam.RamCooldown + " s cooldown is shrugged off", e.Hits == hits + 1);
                BossRam.TestClock += BossRam.RamCooldown + .01f;
                Ram(r, body);
                Check(id + ": ... after the cooldown it hits again", e.Hits == hits + 2);
                Check(id + ": ... it can never melt the boss: a full blue atom's worth of rams (" + (int)(5.8f / BossRam.RamShieldCost) + " at most) leaves it standing",
                      BossRam.RamWeight * Mathf.Floor(5.8f / BossRam.RamShieldCost) < BossConfig.HitPoints);
                // unshielded: it hurts the pilot, not the boss
                collisionDetection.atomCheck = false;
                collisionDetection.invTimer = 0f;
                BossRam.TestClock += 5f;
                hits = e.Hits;
                Ram(r, body);
                Check(id + ": an unshielded touch costs a heart and does not hurt the boss", collisionDetection.lifeCounter == 1 && e.Hits == hits);
            }
            finally
            {
                collisionDetection.MAXLIFE = savedMax;
                collisionDetection.lifeCounter = 0;
                collisionDetection.atomCheck = false;
                collisionDetection.invTimer = 0f;
                PlayerInvuln.Reset();
                r.Dispose();
                BossEncounter.ResetRun();
            }
        }
        Use(null);
    }

    // ---- 8. cost ----------------------------------------------------------------------------

    static void Allocations()
    {
        Use(FitDevice.Find("flip7-1080x2520"));
        var ship = new Vector3(0f, ShipReach.Top, 0f);
        Action work = () =>
        {
            for (int i = 0; i < 200; i++)
            {
                float t = ShipReach.Top + ShipReach.Bottom + ShipReach.ClampY(i) + ShipReach.HalfWidth;
                TopGuard.Blocks(-.5f, .5f, 9f, .5f, ship, 10f, .3f, .4f);
                if (t == float.NaN) Debug.Log("");
            }
        };
        work();
        long bytes = TestHarness.AllocatedBytes(work);
        Check("reading the zone and the spawn guard allocates nothing (" + bytes + " B)", bytes == 0);
        Use(null);
    }
}
