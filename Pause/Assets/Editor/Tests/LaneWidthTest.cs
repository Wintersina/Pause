using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;

// "The gameplay lane is dynamic to the screen width": on a wide screen
// (foldable, tablet, square or landscape window) the side rails sit at the
// screen's edges and the playfield takes the room; spawning and pattern
// density scale with the lane (LaneMetrics). Phones are exactly as before.
//
//   METRICS   LaneMetrics on 9:21, 9:16, 19.5:9, 3:4, a foldable's inner
//             screen, ~1:1, 4:3 landscape, the 948x840 capture and an
//             ultrawide window: phones get no wide extra (Density 1, the
//             rail shift RailInset always gave them); wide screens get the
//             view's extra half-width, clamped; Density is the formula
//   RAILS     at every aspect in every world: the rails are laid out at the
//             drawn edge LaneMetrics names, the rail art keeps its width (it
//             moves out, never stretches), it runs to the screen's edge with
//             no strip of backdrop outside it (unless the clamp is hit) and
//             its lamp column stays on screen; the backdrop covers the view
//   REACH     the ship's hull reaches the rails' inner edge and never crosses it
//   SPAWNS    every roster enemy's spawn span, the heavies', the pickups',
//             the atoms', the boss's lob columns and the weather all sit
//             inside the rails
//   DENSITY   the threat ceiling, the pilot load and the boss's lob columns
//             scale with Density; fans keep their pitch and never pass their
//             cap; a fold / unfold mid-run re-lays the rails
//   PATTERNS  at every wide aspect the solver finds a way through every
//             boss's static patterns on that screen's lane (none covers it)
public static class LaneWidthTest
{
    static int fails;
    static void Check(string what, bool ok)
    {
        Debug.Log((ok ? "[LANE] PASS  " : "[LANE] FAIL  ") + what);
        if (!ok) fails++;
    }

    public static void Run() { TestHarness.Exit(Execute()); }

    static string F(float v) { return v.ToString("F3"); }

    // The aspects the lane is checked on. `phone`: a tall / narrow phone, which must be exactly as before.
    struct Shape { public string id; public int w, h; public bool phone; }
    static readonly Shape[] Shapes =
    {
        new Shape { id = "9:21 1080x2520", w = 1080, h = 2520, phone = true },
        new Shape { id = "9:19.5 1170x2532", w = 1170, h = 2532, phone = true },
        new Shape { id = "9:16 1080x1920", w = 1080, h = 1920, phone = true },
        new Shape { id = "3:4 1620x2160", w = 1620, h = 2160 },
        new Shape { id = "fold 1812x2176", w = 1812, h = 2176 },
        new Shape { id = "~1:1 2000x2000", w = 2000, h = 2000 },
        new Shape { id = "4:3 landscape 2048x1536", w = 2048, h = 1536 },
        new Shape { id = "capture 948x840", w = 948, h = 840 },
    };
    static readonly Shape Ultrawide = new Shape { id = "21:9 landscape 2520x1080", w = 2520, h = 1080 };

    public static int Execute()
    {
        fails = 0;
        using var sandbox = new TestHarness.Sandbox();
        var cam0 = (Camera)null;
        float ortho0 = 5f, aspect0 = 9f / 16f;
        try
        {
            Metrics();
            FanShapes();
            EditorSceneLoader.Open("gameS1");
            cam0 = Camera.main;
            ortho0 = cam0.orthographicSize; aspect0 = cam0.aspect;
            RailsReachAndSpawns();
            DensityAndFolds();
            PatternsLeaveAWay();
            NoFixedLaneLeft();
        }
        finally
        {
            ScreenInfo.ClearOverride();
            BossRails.Reset();
            PlayField.Reset();
            if (cam0 != null) { cam0.orthographicSize = ortho0; cam0.aspect = aspect0; }
        }
        Debug.Log("[LANE] failures: " + fails);
        return fails;
    }

    static void Use(Shape s)
    {
        ScreenInfo.ClearOverride();
        ScreenInfo.Override(s.w, s.h, new Rect(0, 0, s.w, s.h));
        var cam = Camera.main;
        if (cam != null)
        {
            cam.aspect = (float)s.w / s.h;
            cam.orthographicSize = CameraFit.ComputeSize(5f, CameraFit.GameplayHalfWidth, s.w, s.h);
        }
        BossRails.Reset();
        PlayField.Reset();
    }

    // ---- metrics ------------------------------------------------------------------------

    static void Metrics()
    {
        foreach (var s in Shapes)
        {
            float extra = LaneMetrics.WideExtraFor(s.w, s.h), density = LaneMetrics.DensityFor(s.w, s.h);
            float view = CameraFit.GameplayViewHalfWidth(new Vector2(s.w, s.h));
            float shift = RailInset.ShiftFor(s.w, s.h), room = RailInset.RoomShiftFor(s.w, s.h);
            Debug.Log("[LANE] " + s.id + ": view half-width " + F(view) + ", wide extra " + F(extra) + ", rail shift " + F(shift) +
                      ", inner edge " + F(LaneMetrics.InnerEdgeFor(s.w, s.h)) + ", density x" + density.ToString("F2"));
            if (s.phone)
            {
                Check(s.id + ": a phone gets no wide extra, density x1 and the rail shift it always had (" + F(shift) + ")",
                      extra == 0f && density == 1f && shift == room && view == CameraFit.GameplayHalfWidth &&
                      (Mathf.Approximately(shift, RailInset.BaseShift + RailInset.MaxShift) || Mathf.Approximately(shift, RailInset.BaseShift)));
            }
            else
            {
                Check(s.id + ": the rails move out by the view's extra half-width (" + F(extra) + " = " + F(view) + " - 3.72) on top of the full room shift",
                      Mathf.Abs(extra - (view - CameraFit.GameplayHalfWidth)) < 1e-4f && extra > 0f &&
                      Mathf.Abs(shift - (RailInset.BaseShift + RailInset.MaxShift + extra)) < 1e-4f);
                Check(s.id + ": density = 1 + extra / " + F(LaneMetrics.ReferenceLaneHalf) + " = x" + density.ToString("F3"),
                      Mathf.Abs(density - (1f + extra / LaneMetrics.ReferenceLaneHalf)) < 1e-4f && density > 1f && density <= LaneMetrics.MaxDensity);
            }
        }
        float ux = LaneMetrics.WideExtraFor(Ultrawide.w, Ultrawide.h);
        Check(Ultrawide.id + ": the extra is clamped (" + F(ux) + " = " + F(LaneMetrics.MaxWideExtra) + ") and the density with it (x" +
              LaneMetrics.DensityFor(Ultrawide.w, Ultrawide.h).ToString("F2") + ")",
              Mathf.Approximately(ux, LaneMetrics.MaxWideExtra) && Mathf.Approximately(LaneMetrics.DensityFor(Ultrawide.w, Ultrawide.h), LaneMetrics.MaxDensity));
        Check("no screen at all: no extra, density x1", LaneMetrics.WideExtraFor(0, 0) == 0f && LaneMetrics.DensityFor(0, 0) == 1f);
        Check("counts: never fewer than authored, x density, capped",
              LaneMetrics.ScaleCount(5, 1f) == 5 && LaneMetrics.ScaleCount(5, 1.41f) == 7 && LaneMetrics.ScaleCount(5, 2f) == 10 &&
              LaneMetrics.ScaleCount(5, 2f, 8) == 8 && LaneMetrics.ScaleCount(5, .5f) == 5 && LaneMetrics.ScaleCount(0, 2f) == 0);
        if (Application.isBatchMode)
        {
            ScreenInfo.ClearOverride();
            Check("the batch editor with no screen override keeps the authored lane (shift " + F(RailInset.Shift) + ", extra " + F(LaneMetrics.WideExtra) + ")",
                  !LaneMetrics.HasDevice && RailInset.Shift == 0f && LaneMetrics.WideExtra == 0f && LaneMetrics.Density == 1f);
        }
    }

    static void FanShapes()
    {
        bool ok = true;
        string bad = "";
        int fans = 0;
        for (int w = 0; w < BossPatternTables.Bosses; w++)
            foreach (var sl in BossPatternTables.For(w))
            {
                if (sl.pattern == null) continue;
                foreach (var s in sl.pattern.streams)
                {
                    if (s.kind != StreamKind.Fan) continue;
                    fans++;
                    foreach (float d in new[] { 1f, 1.41f, 2f })
                    {
                        BossPatternRunner.FanShape(s, d, out int c, out float spread);
                        int c0 = Mathf.Max(1, s.count);
                        bool keeps = c >= c0 && spread <= Mathf.Max(s.spread, BossPatternRunner.MaxFanSpread) + 1e-3f;
                        if (c0 >= 2 && c >= 2) keeps &= Mathf.Abs(spread / (c - 1) - s.spread / (c0 - 1)) < 1e-3f;
                        if (d == 1f) keeps &= c == c0 && spread == s.spread;
                        if (!keeps) { ok = false; bad += " " + sl.name + "@" + d; }
                    }
                }
            }
        Check("every boss fan (" + fans + ") keeps its pitch on a wide lane, carries more shots, never fans past " + BossPatternRunner.MaxFanSpread + " deg, and is untouched on a phone" + bad, ok && fans > 0);
    }

    // ---- rails, reach, spawns -----------------------------------------------------------

    static void RailsReachAndSpawns()
    {
        var shipGo = new GameObject("~LaneSpawner").AddComponent<spawnShips>().Spawn(ShipId.Starter);
        var sr = shipGo.GetComponent<SpriteRenderer>();
        var railWidth = new Dictionary<string, float>();
        var all = new List<Shape>(Shapes) { Ultrawide };
        // the phone first: the rail widths every other shape must keep
        all.Sort((a, b) => (b.id.StartsWith("9:16") ? 1 : 0) - (a.id.StartsWith("9:16") ? 1 : 0));
        foreach (var s in all)
        {
            foreach (var theme in WorldManager.Worlds)
            {
                Use(s);
                WorldPainter.Apply(theme);
                var cam = Camera.main;
                float halfW = cam.orthographicSize * cam.aspect;
                var left = GameObject.Find("leftPipe");
                var right = GameObject.Find("rightPipe");
                WorldPainter.VisibleRailEdges(left, out float inL, out float outL);
                WorldPainter.VisibleRailEdges(right, out float inR, out float outR);
                float inner = BossRails.InnerEdge;
                string tag = s.id + " " + theme.displayName;
                bool clamped = s.w == Ultrawide.w && s.h == Ultrawide.h;

                Check(tag + ": the rails sit at the edge LaneMetrics names (" + F(inner) + " = " + F(LaneMetrics.InnerEdgeFor(s.w, s.h)) + "), mirrored",
                      Mathf.Abs(inner - LaneMetrics.InnerEdgeFor(s.w, s.h)) < .003f && Mathf.Abs(inL - inR) < .003f && Mathf.Abs(outL - outR) < .003f &&
                      Mathf.Abs(LaneMetrics.InnerEdge - inner) < .003f);
                // never stretched: the same drawn width on every screen
                float width = left.GetComponent<Renderer>().bounds.size.x;
                if (!railWidth.ContainsKey(theme.displayName)) railWidth[theme.displayName] = width;
                Check(tag + ": the rail art keeps its width (" + F(width) + " vs " + F(railWidth[theme.displayName]) + " on 9:16): it moves out, never stretches",
                      Mathf.Abs(width - railWidth[theme.displayName]) < 1e-3f);
                if (!clamped)
                    Check(tag + ": the rail runs to the screen's edge (art to " + F(outR) + ", view " + F(halfW) + "), no backdrop strip outside it, " +
                          "and at most the share a tall phone loses is cut off (" + ((outR - halfW) / (outR - inR) * 100f).ToString("F0") + "%)",
                          outR >= halfW - .01f && (outR - halfW) / (outR - inR) <= .45f);
                else
                    Check(tag + ": past the clamp the rails stop at the widest lane, inside the view (" + F(outR) + " < " + F(halfW) + ")",
                          outR < halfW && Mathf.Abs(inner - (BossRails.ReinforcedInnerEdge + RailInset.BaseShift + RailInset.MaxShift + LaneMetrics.MaxWideExtra)) < .003f);
                float lampOuter = inR + .45f * (outR - inR);
                Check(tag + ": the rail's lamp column stays on screen (" + F(lampOuter) + " < " + F(halfW) + ")", lampOuter < halfW - .02f);

                // the backdrop still covers the view
                CameraFit.CoverBackdrops(cam);
                var bg = GameObject.Find("starsBackground");
                var bgr = bg != null ? bg.GetComponent<Renderer>() : null;
                if (bgr != null)
                {
                    var b = bgr.bounds;
                    Check(tag + ": the backdrop covers the whole view (" + F(b.min.x) + " .. " + F(b.max.x) + " over +/-" + F(halfW) + ")",
                          b.min.x <= -halfW + 1e-3f && b.max.x >= halfW - 1e-3f && b.min.y <= CameraFit.ViewBottom + 1e-3f && b.max.y >= CameraFit.ViewTop - 1e-3f);
                }

                // the ship reaches the rails and stays off them
                shipGo.transform.position = new Vector3(ShipReach.ClampX(99f), 0f, 0f);
                float hullR = sr.bounds.max.x;
                shipGo.transform.position = new Vector3(ShipReach.ClampX(-99f), 0f, 0f);
                float hullL = sr.bounds.min.x;
                Check(tag + ": the ship's hull reaches the rails' inner edge and stays inside it (" + F(hullL) + " .. " + F(hullR) + " in +/-" + F(inner) + ")",
                      hullR <= inner + 1e-3f && hullL >= -inner - 1e-3f && Mathf.Abs(ShipReach.HalfWidth + ShipScale.HullHalfWidth - inner) < 1e-3f);

                // rail mines ride the drawn rail
                float mx = enmiesOnBoard.WorldRailX(false);
                Check(tag + ": rail mines mount on the drawn rail (" + F(mx) + ")",
                      Mathf.Abs(mx - RailMineArt.MountX(inner)) < 1e-3f && Mathf.Abs(mx + enmiesOnBoard.WorldRailX(true)) < 1e-4f);

                if (theme != WorldManager.Worlds[0]) continue;   // the spans below do not depend on the world's paint
                Spawns(tag, inner);
            }
        }
        Object.DestroyImmediate(shipGo);
        foreach (var sp in Object.FindObjectsByType<spawnShips>(FindObjectsSortMode.None)) Object.DestroyImmediate(sp.gameObject);
    }

    static void Spawns(string tag, float inner)
    {
        bool enemies = true;
        string bad = "";
        foreach (var def in EnemyRoster.All)
        {
            if (def == null || def.role == EnemyRole.Chaser) continue;
            float reach = SpawnLane.MaxX(def) + SpawnLane.HalfExtents(def).x;
            if (reach > inner + 1e-3f) { enemies = false; bad += " " + def.key + " " + F(reach); }
        }
        Check(tag + ": every roster enemy spawns wholly inside the rails (lane +/-" + F(SpawnLane.LaneHalf) + ", heavies +/-" + F(SpawnLane.HeavyMaxXLive) + ")" + bad,
              enemies && SpawnLane.LaneHalf < inner && SpawnLane.HeavyMaxXLive < SpawnLane.LaneHalf);
        Check(tag + ": the lanes move out with the rails (enemy " + F(SpawnLane.LaneHalf) + ", pickups " + F(RailInset.PickupLaneHalf) + ", atoms " + F(AtomWander.LaneHalfWidth) + ")",
              Mathf.Abs(SpawnLane.LaneHalf - (SpawnLane.AuthoredLaneHalf + RailInset.Shift)) < 1e-4f &&
              Mathf.Abs(RailInset.PickupLaneHalf - (RailInset.AuthoredPickupLane + RailInset.Shift)) < 1e-4f &&
              Mathf.Abs(AtomWander.LaneHalfWidth - (AtomWander.AuthoredLaneHalfWidth + RailInset.Shift)) < 1e-4f &&
              RailInset.PickupLaneHalf < inner && AtomWander.LaneHalfWidth < inner);
        int slots = BossConfig.LiveLaneSlots;
        float lo = BossActor.LaneX(0) - BossActor.LaneWidth * .5f, hi = BossActor.LaneX(slots - 1) + BossActor.LaneWidth * .5f;
        Check(tag + ": the boss's lob columns (" + slots + " of " + F(BossActor.LaneWidth) + " u) span the lane inside the rails (" + F(lo) + " .. " + F(hi) + ")",
              lo >= -inner && hi <= inner && Mathf.Abs(lo + hi) < 1e-3f && BossActor.LaneWidth >= 2f * BossConfig.LaneHalfWidth / BossConfig.LaneSlots * .85f);
        Check(tag + ": the weather's swirls and columns stay inside the rails (+/-" + F(LaneMetrics.Widen(2f)) + ")", LaneMetrics.Widen(2f) < inner);
    }

    // ---- density and folds --------------------------------------------------------------

    static void DensityAndFolds()
    {
        Use(Shapes[2]);   // 9:16
        float phoneCeiling = EnemyDensity.MaxThreats(20f) / EnemyDensity.ViewScale;
        float phonePilots = EnemyDensity.MaxPilotLoad(20f, 0);
        int phoneSlots = BossConfig.LiveLaneSlots;
        Check("9:16: the boss's lob columns are the authored " + BossConfig.LaneSlots + " over +/-" + BossConfig.LaneHalfWidth,
              phoneSlots == BossConfig.LaneSlots && Mathf.Approximately(BossConfig.LiveLaneHalfWidth, BossConfig.LaneHalfWidth));
        foreach (var s in Shapes)
        {
            if (s.phone) continue;
            Use(s);
            float d = LaneMetrics.Density;
            float ceiling = EnemyDensity.MaxThreats(20f) / EnemyDensity.ViewScale;
            float pilots = EnemyDensity.MaxPilotLoad(20f, 0);
            Check(s.id + ": the threat ceiling per view height scales with the lane (x" + (ceiling / phoneCeiling).ToString("F2") + " = x" + d.ToString("F2") + ")",
                  Mathf.Abs(ceiling / phoneCeiling - d) < 1e-3f);
            Check(s.id + ": the pilot load scales with the lane (" + pilots.ToString("F2") + " vs " + phonePilots.ToString("F2") + ")",
                  pilots > phonePilots && pilots <= phonePilots * LaneMetrics.MaxDensity + 1e-3f);
            Check(s.id + ": the boss lobs on more columns (" + BossConfig.LiveLaneSlots + ")", BossConfig.LiveLaneSlots >= phoneSlots && BossConfig.LiveLaneSlots <= BossConfig.MaxLaneSlots);
        }
        string src = System.IO.File.ReadAllText("Assets/Scripts/Gameplay/Spawning/enmiesOnBoard.cs");
        Check("the roster's spawn rate divides by LaneMetrics.Density", src.Contains("/ LaneMetrics.Density"));

        // a fold mid-run: the rails move out and back with the screen
        Use(Shapes[2]);
        WorldPainter.Apply(WorldManager.Worlds[0]);
        float phone = BossRails.InnerEdge;
        foreach (var s in new[] { Shapes[4], Shapes[2], Shapes[5], Shapes[2] })
        {
            ScreenInfo.ClearOverride();
            ScreenInfo.Override(s.w, s.h, new Rect(0, 0, s.w, s.h));
            WorldPainter.RefreshInset();
            Check("fold / unfold to " + s.id + ": the rails re-lay at once (" + F(BossRails.InnerEdge) + ")",
                  Mathf.Abs(BossRails.InnerEdge - LaneMetrics.InnerEdgeFor(s.w, s.h)) < .003f);
        }
        Check("... and the phone lane comes back exactly (" + F(BossRails.InnerEdge) + " = " + F(phone) + ")", Mathf.Abs(BossRails.InnerEdge - phone) < 1e-4f);
    }

    // ---- boss patterns ------------------------------------------------------------------

    static void PatternsLeaveAWay()
    {
        bool overhaul = BossCatalog.UseOverhaul;
        BossCatalog.UseOverhaul = true;
        try
        {
            var sim = new PatternSim();
            foreach (var s in Shapes)
            {
                if (s.phone && !s.id.StartsWith("9:16")) continue;
                Use(s);
                var f = PlayField.Live;
                for (int world = 0; world < BossCatalog.All.Length; world++)
                {
                    if (!TestHarness.SweepWorld(world)) continue;
                    var boss = BossCatalog.ForWorld(world);
                    var parts = BossEmitterTable.Parts[BossEmitters.World(boss)];
                    var em = new Vector2[parts.Length];
                    for (int p = 0; p < em.Length; p++) em[p] = new Vector2(0f, BossConfig.BossY) + BossEmitters.Local(boss, p, BossArt.Idle0);
                    sim = new PatternSim
                    {
                        emitters = em,
                        railEdge = LaneMetrics.InnerEdgeFor(s.w, s.h),
                        shipHalf = ShipReach.HalfWidth,
                        spawnTop = CameraFit.ViewTop + .15f,
                        zoneBottom = ShipReach.BottomFor(f),
                        zoneTop = Mathf.Min(ShipReach.TopFor(f), BossConfig.ShipCeilingFor(f, boss)),
                        shotScale = BossConfig.ShotScaleFor(f),
                        laneDensity = LaneMetrics.Density,
                        viewHalfWidth = LaneMetrics.ViewHalfWidth,
                    };
                    int solved = 0, total = 0;
                    float worst = 1f;
                    foreach (var a in boss.attacks)
                    {
                        if (a.kind != BossAttackKind.Pattern || a.pattern == null) continue;
                        var stat = BossLadderTest.StaticOnly(a.pattern);
                        if (stat == null) continue;
                        foreach (float x in new[] { -sim.shipHalf * .9f, 0f, sim.shipHalf * .9f })
                        {
                            var r = sim.Solve(stat, BossLadder.RateAt(world, 1f), 1, new Vector2(x, Mathf.Lerp(sim.zoneBottom, sim.zoneTop, .3f)), 5f, .04f, 2.5f);
                            total++;
                            if (r.survivable) solved++;
                            worst = Mathf.Min(worst, r.minReach);
                        }
                    }
                    Check(s.id + " " + boss.artKey + ": a way through every static pattern on this lane (" + solved + "/" + total +
                          ", the tightest leaves " + (worst * 100f).ToString("F0") + "% of the zone; density x" + sim.laneDensity.ToString("F2") + ")",
                          // (a phone's floor is 8% of the zone, as ShipReachTest; a tablet-shaped screen keeps the authored 10 u view
                          // height, so its ship zone under the boss is shorter whatever the lane and Space's tightest pattern leaves
                          // ~5-6% there even at density x1.01: a way through must exist, with 4% of the zone to spare)
                          total > 0 && solved == total && worst >= (s.phone || s.id.StartsWith("9:16") ? .08f : .04f));
                    Check(s.id + " " + boss.artKey + ": no shot starts off the view", sim.offView == 0);
                }
            }
        }
        finally { BossCatalog.UseOverhaul = overhaul; }
    }

    // ---- no phone-lane constant left where the lane is read -----------------------------

    static void NoFixedLaneLeft()
    {
        string actor = System.IO.File.ReadAllText("Assets/Scripts/Bosses/BossActor.cs");
        string exec = System.IO.File.ReadAllText("Assets/Scripts/Bosses/BossExecutors.cs");
        string haz = System.IO.File.ReadAllText("Assets/Scripts/Worlds/Hazards/WorldHazards.cs");
        string sim = System.IO.File.ReadAllText("Assets/Scripts/Bosses/Patterns/BossPatternSim.cs");
        Check("the boss reads the lane live (no CameraFit.GameplayHalfWidth clamp, LiveLaneSlots / LiveLaneHalfWidth for its columns)",
              !actor.Contains("CameraFit.GameplayHalfWidth") && actor.Contains("BossConfig.LiveLaneSlots") && actor.Contains("BossConfig.LiveLaneHalfWidth"));
        Check("the boss's executors and the weather have no fixed +/-2.1 / +/-2 / +/-1.4 lane", !exec.Contains("-2.1f, 2.1f") && !haz.Contains("Rand(-1.4f") && !haz.Contains(", -2f, 2f)"));
        Check("the pattern sim culls at its own lane, not a fixed 4.2", !sim.Contains("Mathf.Abs(p.x) > 4.2f") && !sim.Contains("> CameraFit.GameplayHalfWidth"));
    }
}
