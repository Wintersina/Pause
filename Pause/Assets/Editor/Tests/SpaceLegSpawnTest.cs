using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEngine;
using Object = UnityEngine.Object;
using Random = UnityEngine.Random;

// The space leg: after a planet's lift-off the pilot is back in Space's sky
// until the next planet arrives, and what spawns there must be Space's (rocks,
// space enemies) -- not the planet just left, whose index the manager still
// holds for its bookkeeping (WorldManager.SpawnWorld). Drives the real
// spawners (WorldLeakTest's fixtures) through every transition:
//
//   leg      from each live world's lift-off swap (BeginSpaceLeg) with the
//            portal's pressure on: many seeds, 30 s each, every live enemy
//            and elite audited each second against the Space roster
//   control  the same flight WITHOUT the leg flag spawns the left world (the
//            test sees the bug)
//   arrival  Advance ends the leg; the next world's roster takes over, with
//            no extra calm window / arrival key change during the leg
//   loop     the last world's way round ends the leg on the run's start world
//   clean    a Space-start run and the first level never see the leg flag
//
// Writes SpaceLegSpawnTest.log into $SPACELEG_LOG_DIR when set (the preview).
public static class SpaceLegSpawnTest
{
    static int fails;
    static void Check(string what, bool ok)
    {
        Debug.Log((ok ? "[SPACELEG] PASS  " : "[SPACELEG] FAIL  ") + what);
        if (!ok) fails++;
    }

    public static void Run() { TestHarness.Exit(Execute()); }

    const BindingFlags Inst = BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance;
    static StringBuilder log;

    static string Census()
    {
        var counts = new SortedDictionary<string, int>();
        foreach (var id in Object.FindObjectsByType<EnemyIdentity>(FindObjectsSortMode.None))
        {
            if (id == null || id.Def == null) continue;
            string k = WorldLeakTest.W(id.Def.world)[0] + ":" + id.Def.key;
            counts[k] = counts.TryGetValue(k, out int n) ? n + 1 : 1;
        }
        var live = EliteShip.Live;
        for (int i = 0; i < live.Count; i++)
            if (live[i] != null && live[i].State != EliteState.Dead) counts["ELITE:" + live[i].Def.key] = 1;
        var sb = new StringBuilder();
        foreach (var kv in counts) sb.Append(kv.Key).Append(" x").Append(kv.Value).Append("  ");
        return sb.ToString();
    }

    static void OpenWayOn(int to)
    {
        typeof(WorldManager).GetField("portalOpen", Inst).SetValue(WorldLeakTest.wm, true);
        PortalPressure.Open(to);
    }

    // `legSeconds` of the leg after `from`'s lift-off. Returns foreign count.
    static int Leg(int from, int seed, float legSeconds, bool flag, bool logIt, out int spaceSeen)
    {
        WorldLeakTest.firstForeign.Clear(); WorldLeakTest.audited = 0;
        WorldLeakTest.Prefs(from);
        Random.InitState(seed);
        WorldLeakTest.LoadRun(true);
        int to = WorldManager.HasNext ? from + 1 : WorldManager.PortalDestination;
        int foreign = WorldLeakTest.Fly(70f, from, WorldLeakTest.W(from));       // the level
        // boss over, portal stage, lift-off: the board is cleared at the start, nothing spawns, then the swap
        OpenWayOn(to);
        Planetfall.ClearBoard(null);
        int calmBefore = WorldLeakTest.board.CalmWindows;
        if (flag) WorldManager.BeginSpaceLeg(0);
        Check(WorldLeakTest.W(from) + " seed " + seed + ": leg flag " + (flag ? "on" : "off") + " -> spawn world " +
              WorldLeakTest.W(WorldManager.SpawnWorld) + ", run world still " + WorldLeakTest.W(WorldManager.CurrentIndex),
              WorldManager.SpawnWorld == (flag ? 0 : from) && WorldManager.CurrentIndex == from);
        int space = 0, other = 0;
        const float dt = 1f / 30f;
        float next = 0f;
        for (float t = 0f; t < legSeconds; t += dt)
        {
            WorldLeakTest.Step(dt);
            if (t >= next)
            {
                next += 1f;
                foreach (var id in Object.FindObjectsByType<EnemyIdentity>(FindObjectsSortMode.None))
                { if (id == null || id.Def == null) continue; if (id.Def.world == 0) space++; else other++; }
                if (flag) foreign += WorldLeakTest.Audit(0, "leg after " + WorldLeakTest.W(from) + " t+" + t.ToString("F0"));
                if (logIt) log.AppendLine("t+" + t.ToString("00") + "s  " + Census());
            }
        }
        spaceSeen = space;
        Check(WorldLeakTest.W(from) + " seed " + seed + ": the leg opens no new calm window / arrival (" + calmBefore + " -> " +
              WorldLeakTest.board.CalmWindows + ")", WorldLeakTest.board.CalmWindows == calmBefore);
        if (!flag) return other;
        return foreign;
    }

    public static int Execute()
    {
        fails = 0;
        using var sandbox = new TestHarness.Sandbox();
        bool tide = WorldManager.TideEnabled, storm = WorldManager.StormEnabled;
        string dir = System.Environment.GetEnvironmentVariable("SPACELEG_LOG_DIR");
        log = new StringBuilder();
        try
        {
            EliteCatalog.Reload();
            foreach (bool all in new[] { false, true })
            {
                WorldManager.TideEnabled = all; WorldManager.StormEnabled = all;
                int last = WorldManager.LastLiveWorld;
                log.AppendLine("=== switches " + (all ? "Tide+Storm on" : "production (Ember last)") + " ===");
                for (int from = 1; from <= last; from++)
                {
                    int totalForeign = 0, totalSpace = 0, runs = 0;
                    for (int seed = 0; seed < 6; seed++)
                    {
                        bool logIt = seed == 0 && from == 1;
                        if (logIt) log.AppendLine("--- leg after " + WorldLeakTest.W(from) + " (flag on), seed 0 ---");
                        totalForeign += Leg(from, 4200 + from * 31 + seed, 30f, true, logIt, out int sp);
                        totalSpace += sp; runs++;
                    }
                    Check(WorldLeakTest.W(from) + "'s space leg (" + runs + " seeds x 30 s): only Space's roster, " + totalSpace +
                          " Space sightings, " + totalForeign + " foreign" + WorldLeakTest.Examples(), totalForeign == 0 && totalSpace > 20);

                    // control: the flag off flies the left world's roster (proves the audit sees the bug)
                    int ctl = 0, ctlSpace;
                    for (int seed = 0; seed < 3; seed++) ctl += Leg(from, 8800 + from + seed, 30f, false, false, out ctlSpace);
                    Check(WorldLeakTest.W(from) + " control (no leg flag): the left world's roster does spawn (" + ctl + " sightings)", ctl > 0);

                    // arrival: the next world takes over and the leg ends
                    WorldLeakTest.firstForeign.Clear(); WorldLeakTest.audited = 0;
                    WorldLeakTest.Prefs(from);
                    Random.InitState(515 + from);
                    WorldLeakTest.LoadRun(true);
                    bool loop = !WorldManager.HasNext;
                    int to = loop ? WorldManager.PortalDestination : from + 1;
                    int pre = WorldLeakTest.Fly(40f, from, WorldLeakTest.W(from));
                    OpenWayOn(to);
                    Planetfall.ClearBoard(null);
                    WorldManager.BeginSpaceLeg(0);
                    int legForeign = WorldLeakTest.Fly(15f, 0, "leg");
                    Planetfall.ClearBoard(null);   // the commit
                    try { if (loop) WorldLeakTest.wm.Advance(); else WorldLeakTest.wm.Advance(false); }
                    catch (System.Exception ex) { Debug.Log("[SPACELEG] (Advance's presentation threw: " + ex.Message + ")"); }
                    Check(WorldLeakTest.W(from) + (loop ? " (loop)" : "") + ": arrival ends the leg, run world " + WorldLeakTest.W(WorldManager.CurrentIndex) +
                          " is the spawn world", !WorldManager.InSpaceLeg && WorldManager.CurrentIndex == to && WorldManager.SpawnWorld == to);
                    int now = WorldManager.CurrentIndex;
                    int atArrival = WorldLeakTest.Audit(now, "arrival");
                    int after = WorldLeakTest.Fly(120f, now, WorldLeakTest.W(now));
                    Check(WorldLeakTest.W(from) + " -> " + WorldLeakTest.W(now) + ": " + WorldLeakTest.W(now) + "'s roster takes over at arrival and holds 120 s (" +
                          pre + "/" + legForeign + "/" + atArrival + "/" + after + " foreign)" + WorldLeakTest.Examples(),
                          pre == 0 && legForeign == 0 && atArrival == 0 && after == 0 && WorldLeakTest.audited > 20);
                }
            }

            // Space first level: no leg, Space roster, ordinary rules
            WorldManager.TideEnabled = tide; WorldManager.StormEnabled = storm;
            WorldLeakTest.firstForeign.Clear(); WorldLeakTest.audited = 0;
            WorldLeakTest.Prefs(0);
            Random.InitState(77);
            WorldLeakTest.LoadRun(true);
            int sp0 = WorldLeakTest.Fly(120f, 0, "Space");
            Check("first level (Space): no leg flag, Space roster only (" + sp0 + " foreign)", !WorldManager.InSpaceLeg && sp0 == 0 && WorldLeakTest.audited > 20);
            // Space -> Frost planetfall does not use a leg (Space is the sky already)
            OpenWayOn(1);
            Check("Space's own portal stage: no leg flag, spawn world is Space", !WorldManager.InSpaceLeg && WorldManager.SpawnWorld == 0);
            Planetfall.ClearBoard(null);
            try { WorldLeakTest.wm.Advance(false); } catch (System.Exception) { }
            int fr = WorldLeakTest.Fly(60f, 1, "Frost");
            Check("Space -> Frost: Frost's roster after the planetfall (" + fr + " foreign)", fr == 0 && WorldManager.SpawnWorld == 1);

            // a new run clears a stale leg flag
            WorldManager.BeginSpaceLeg(0);
            WorldLeakTest.Prefs(2);
            WorldLeakTest.LoadRun(true);
            Check("a fresh run clears a stale leg flag", !WorldManager.InSpaceLeg && WorldManager.SpawnWorld == 2);

            // tutorial (no WorldManager): Space roster, flag irrelevant
            WorldLeakTest.TearDown();
            Check("no WorldManager (tutorial, headless): the roster's world is Space", EnemyRoster.CurrentWorld == 0 && !WorldManager.InSpaceLeg);

            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
                File.WriteAllText(Path.Combine(dir, "space_leg_after_frost.log"), log.ToString());
            }
        }
        finally
        {
            WorldManager.TideEnabled = tide; WorldManager.StormEnabled = storm;
            WorldLeakTest.TearDown();
            EliteSystem.PlayerOverride = null;
            LandingSites.Override = null;
            SpawnSpace.ClockOverride = null;
            WorldManager.ClearReplayWorld();
            RunLoop.Reset();
            BossEncounter.ResetRun();
            PortalPressure.Reset();
            LoopDifficulty.Reset();
            ShipStartSpeed.EquippedHudOverride = null;
            buttonClicks.playerDied = false;
            moveBackGround.speed = 0f;
        }
        Debug.Log("[SPACELEG] failures: " + fails);
        return fails;
    }
}
