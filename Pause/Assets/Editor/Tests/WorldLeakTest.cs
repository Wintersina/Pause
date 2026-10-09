using System.Collections.Generic;
using System.Reflection;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;
using Random = UnityEngine.Random;

// Bug report (Frost level on the Mac build): a board full of SPACE rocks
// (cluster, coal, beacon crater), a Space alien and a Space chaser, over
// Frost's backdrop and rails. The rule: whatever is alive on the board
// belongs to the world the run is in (roster entry's world, elite def's
// world), except the explicit shared list below. WorldGatingTest proves the
// pick tables are gated; this flies the REAL spawner (enmiesOnBoard: rocks,
// heavies, aliens, extras, rail mines, chasers; EliteDirector: elites) for
// several simulated minutes and audits every LIVE object:
//
//   1  steady state in each world (4 minutes, with the portal's pressure on
//      for the last stretch)
//   2  across each way on: planetfall (the board is cleared at the commit,
//      Planetfall.ClearBoard, then WorldManager.Advance(false)), portal
//      (Portal.Enter) and the loop back round (Ember -> Space)
//   3  a replay in a different world than the run reached (a fresh scene,
//      WorldManager.Start picking the pinned start world)
//   4  the run's world survives something else writing the saved world
//      mid-run (PlayerPrefs "currentWorld": a cloud pull, developer-mode
//      restore): the spawners read the world through WorldManager.CurrentIndex
public static class WorldLeakTest
{
    static int fails;
    static void Check(string what, bool ok)
    {
        Debug.Log((ok ? "[WORLDLEAK] PASS  " : "[WORLDLEAK] FAIL  ") + what);
        if (!ok) fails++;
    }

    public static void Run() { TestHarness.Exit(Execute()); }

    const BindingFlags Inst = BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance;
    const BindingFlags Stat = BindingFlags.NonPublic | BindingFlags.Static;
    static readonly MethodInfo SpawnStep = typeof(enmiesOnBoard).GetMethod("spawn", BindingFlags.NonPublic | BindingFlags.Instance, null, new[] { typeof(float) }, null);
    static readonly MethodInfo Select = typeof(enmiesOnBoard).GetMethod("SelectPhase", BindingFlags.NonPublic | BindingFlags.Instance);
    static readonly FieldInfo Elapsed = typeof(enmiesOnBoard).GetField("elapsedFlightSeconds", BindingFlags.NonPublic | BindingFlags.Instance);
    static int Worlds => WorldManager.Worlds.Length;
    static string W(int w) => WorldManager.Worlds[w].displayName;

    // The shared types: live objects that belong to no single world. No
    // roster enemy and no elite is shared (WorldGatingTest); the only shared
    // things on the board are the run's own systems, which are not enemies.
    static readonly HashSet<string> SharedEnemyKeys = new HashSet<string>();   // none

    // ---- fixtures -------------------------------------------------------------

    static Transform pilot;
    static WorldManager wm;
    static enmiesOnBoard board;
    static EliteDirector dir;
    static int audited;
    static readonly List<string> firstForeign = new List<string>();
    static float simClock;

    static void SetWorldManager(WorldManager m)
    {
        typeof(WorldManager).GetField("<Instance>k__BackingField", BindingFlags.NonPublic | BindingFlags.Static).SetValue(null, m);
    }

    static void Prefs(int highest)
    {
        WorldManager.ClearReplayWorld();
        PlayerPrefs.SetString("HasDoneTut", "true");
        PlayerPrefs.SetInt(DeveloperUnlocks.EnabledKey, 0);
        PlayerPrefs.DeleteKey(DeveloperUnlocks.SelectedWorldKey);
        PlayerPrefs.SetInt(WorldManager.PrefsHighestWorld, highest);
        PlayerPrefs.SetInt(WorldManager.PrefsCurrentWorld, highest);
    }

    // A run's scene loading, in the order Unity runs it: the old scene gone,
    // score.Awake begins the run, the spawners' Start, WorldManager.Start
    // picks the world. `wmFirst`: WorldManager.Start before the spawners'.
    static void LoadRun(bool startAtHighest)
    {
        TearDown();
        EditorSceneLoader.Open("gameS1", OpenSceneMode.Single);
        foreach (var b in Object.FindObjectsByType<enmiesOnBoard>(FindObjectsSortMode.None)) Object.DestroyImmediate(b.gameObject);
        BossEncounter.ResetRun();
        PortalPressure.Reset();
        score.pauseCounter = 0;
        buttonClicks.playerDied = false;
        startMenu.youAreInTutorial = false;
        moveBackGround.speed = .25f;
        ShipStartSpeed.EquippedHudOverride = () => ShipStartSpeed.StockHud;
        RunScore.EndRun(RunScore.RunId);
        RunScore.BeginRun(true, true);

        wm = new GameObject("~WlWorldManager").AddComponent<WorldManager>();
        wm.startAtHighestUnlocked = startAtHighest;
        wm.SendMessage("Awake");
        SetWorldManager(wm);

        board = new GameObject("~WlBoard").AddComponent<enmiesOnBoard>();
        board.transform.position = new Vector3(0f, 7f, 0f);
        board.SendMessage("Start");

        try { typeof(WorldManager).GetMethod("Start", Inst).Invoke(wm, null); }
        catch (System.Exception e) { Debug.Log("[WORLDLEAK] (Start's presentation threw in edit mode: " + (e.InnerException ?? e).Message + ")"); }
        // far from the boss
        typeof(WorldManager).GetField("distanceLeft", Inst).SetValue(wm, 1e6f);

        pilot = new GameObject("~WlPilot").transform;
        pilot.position = new Vector3(0f, -2.5f, 0f);
        EliteSystem.Clear();
        EliteSystem.PlayerOverride = pilot;
        var pads = new List<LandingSite>();
        for (int i = 0; i < 6; i++)
        {
            var anchor = new GameObject("~WlPad" + i).transform;
            anchor.position = new Vector3(-2f + i * .8f, 3f, 0f);
            pads.Add(new LandingSite { anchor = anchor, local = Vector3.zero, scale = .3f, order = -420, id = 10 + i });
        }
        LandingSites.Override = list => list.AddRange(pads);
        dir = new GameObject("~WlDirector").AddComponent<EliteDirector>();
        simClock = 0f;
    }

    static void TearDown()
    {
        EliteSystem.Clear();
        foreach (var f in Object.FindObjectsByType<SpawnFootprint>(FindObjectsSortMode.None)) if (f != null) Object.DestroyImmediate(f.gameObject);
        foreach (var r in Object.FindObjectsByType<RailLaneScroller>(FindObjectsSortMode.None)) if (r != null) Object.DestroyImmediate(r.gameObject);
        foreach (var b in Object.FindObjectsByType<enmiesOnBoard>(FindObjectsSortMode.None)) if (b != null) Object.DestroyImmediate(b.gameObject);
        foreach (var e in Object.FindObjectsByType<EnemyIdentity>(FindObjectsSortMode.None)) if (e != null) Object.DestroyImmediate(e.gameObject);
        if (dir != null) Object.DestroyImmediate(dir.gameObject);
        if (wm != null) { SetWorldManager(null); Object.DestroyImmediate(wm.gameObject); }
        wm = null; board = null; dir = null;
    }

    // One simulated frame of the run: the level clock, the spawner, the
    // elite director, the board scrolling (enemies leave below).
    static void Step(float dt)
    {
        simClock += dt;
        SpawnSpace.ClockOverride = simClock;
        pilot.position = new Vector3(Mathf.Sin(simClock * .7f) * 1.8f, -4f, 0f);
        wm.Tick(dt);
        Elapsed.SetValue(board, (float)Elapsed.GetValue(board) + dt);
        Select.Invoke(board, null);
        SpawnStep.Invoke(board, new object[] { dt });
        dir.Tick(dt);
        try { EliteSystem.Step(dt); } catch (System.Exception) { /* presentation in edit mode */ }

        float v = moveBackGround.speed * 30f;
        foreach (var id in Object.FindObjectsByType<EnemyIdentity>(FindObjectsSortMode.None))
        {
            if (id == null) continue;
            id.transform.position += Vector3.down * v * dt;
            if (id.transform.position.y < -14f) Object.DestroyImmediate(id.gameObject);
        }
        foreach (var r in Object.FindObjectsByType<RailLaneScroller>(FindObjectsSortMode.None))
        {
            if (r == null) continue;
            r.transform.position += Vector3.down * v * dt;
            if (r.transform.position.y < RailLaneScroller.EndY && r.Riders == 0) Object.DestroyImmediate(r.gameObject);
        }
    }

    // Every live object against the world the run is in.
    static int Audit(int expect, string where)
    {
        int foreign = 0;
        foreach (var id in Object.FindObjectsByType<EnemyIdentity>(FindObjectsSortMode.None))
        {
            if (id == null) continue;
            var d = id.Def;
            audited++;
            if (d == null || (d.world != expect && !SharedEnemyKeys.Contains(d.key)))
            { foreign++; Note(where + ": " + (d != null ? d.key : id.name) + " in " + W(expect)); }
        }
        var live = EliteShip.Live;
        for (int i = 0; i < live.Count; i++)
        {
            var e = live[i];
            if (e == null || e.State == EliteState.Dead) continue;
            audited++;
            if (e.Def.WorldIndex != expect) { foreign++; Note(where + ": elite " + e.Def.key + " in " + W(expect)); }
        }
        return foreign;
    }

    static void Note(string s) { if (firstForeign.Count < 6 && !firstForeign.Contains(s)) firstForeign.Add(s); }
    static string Examples() { return firstForeign.Count > 0 ? "; e.g. " + string.Join(" | ", firstForeign) : ""; }

    // Fly `seconds`, auditing each second against world `expect`.
    static int Fly(float seconds, int expect, string where)
    {
        const float dt = 1f / 30f;
        int foreign = 0;
        for (float t = 0f, next = 0f; t < seconds; t += dt)
        {
            Step(dt);
            if (t >= next) { foreign += Audit(expect, where + " t+" + t.ToString("F0")); next += 1f; }
        }
        return foreign;
    }

    // ---- the run ----------------------------------------------------------------

    public static int Execute()
    {
        fails = 0;
        using var sandbox = new TestHarness.Sandbox();
        try
        {
            EliteCatalog.Reload();
            Steady();
            Transitions();
            Replay();
            SavedWorldWrittenMidRun();
        }
        finally
        {
            TearDown();
            EliteSystem.PlayerOverride = null;
            LandingSites.Override = null;
            SpawnSpace.ClockOverride = null;
            WorldManager.ClearReplayWorld();
            RunLoop.Reset();
            BossEncounter.ResetRun();
            PortalPressure.Reset();
            LoopDifficulty.Reset();
            ShipStartSpeed.EquippedHudOverride = null;
            PlanetfallCatalog.Enabled = true;
            buttonClicks.playerDied = false;
            moveBackGround.speed = 0f;
        }
        Debug.Log("[WORLDLEAK] failures: " + fails);
        return fails;
    }

    static void Steady()
    {
        for (int w = 0; w < Worlds; w++)
        {
            firstForeign.Clear(); audited = 0;
            Prefs(w);
            Random.InitState(9100 + w);
            LoadRun(true);
            Check(W(w) + ": the run starts on " + W(w), WorldManager.CurrentIndex == w && EnemyRoster.CurrentWorld == w);
            int foreign = Fly(200f, w, W(w));
            // the last stretch with the portal's pressure on, as the game plays it
            PortalPressure.Open((w + 1) % Worlds);
            foreign += Fly(40f, w, W(w) + " pressure");
            PortalPressure.Close(false);
            Check(W(w) + ": four minutes of the real spawners field only " + W(w) + "'s own (" + audited + " live checks, " + foreign +
                  " foreign" + Examples() + ")", foreign == 0 && audited > 200);
        }
    }

    // Ways on: planetfall (cleared at the commit), portal, loop.
    static void Transitions()
    {
        for (int from = 0; from < Worlds; from++)
            for (int mode = 0; mode < 2; mode++)   // 0: the planetfall / loop portal the game flies, 1: a plain portal (no planetfall)
            {
                int to = (from + 1) % Worlds;
                string label = W(from) + " -> " + W(to) + (mode == 0 ? (from == Worlds - 1 ? " (loop portal)" : " (planetfall)") : " (portal)");
                if (mode == 1 && from == Worlds - 1) continue;   // the loop only has the portal
                firstForeign.Clear(); audited = 0;
                Prefs(from);
                Random.InitState(555 + from * 7 + mode);
                PlanetfallCatalog.Enabled = mode == 0;
                LoadRun(true);
                int foreign = Fly(90f, from, W(from));
                PortalPressure.Open(to);
                foreign += Fly(25f, from, W(from) + " pressure");
                // the way on
                typeof(WorldManager).GetField("portalOpen", Inst).SetValue(wm, true);
                if (mode == 0 && from < Worlds - 1) Planetfall.ClearBoard(null);   // the commit
                try { if (mode == 0 && from < Worlds - 1) wm.Advance(false); else wm.Advance(); }
                catch (System.Exception ex) { Debug.Log("[WORLDLEAK] (Advance's presentation threw: " + ex.Message + ")"); }
                bool loop = from == Worlds - 1;
                int now = WorldManager.CurrentIndex;
                Check(label + ": arrives on " + W(loop ? RunLoop.StartWorld : to), now == (loop ? RunLoop.StartWorld : to));
                // the board the ship arrives to holds nothing of the world it left
                int atArrival = Audit(now, label + " at arrival");
                int after = Fly(150f, now, W(now));
                Check(label + ": nothing of " + W(from) + " is on the board at arrival or in the 150 s after (" + atArrival + " at arrival, " +
                      after + " after" + Examples() + ")", atArrival == 0 && after == 0 && foreign == 0);
            }
        PlanetfallCatalog.Enabled = true;
    }

    // Death -> Replay in a different world than the run reached.
    static void Replay()
    {
        for (int start = 0; start < Worlds - 1; start++)
        {
            firstForeign.Clear(); audited = 0;
            Prefs(start);
            Random.InitState(31 + start);
            LoadRun(true);
            // the run planetfalls on to the furthest world ...
            int foreign = Fly(30f, start, W(start));
            Planetfall.ClearBoard(null);
            try { wm.Advance(false); } catch (System.Exception) { }
            int reached = WorldManager.CurrentIndex;
            foreign += Fly(30f, reached, W(reached));
            // ... the pilot dies, Replay pins the world the run began in
            WorldManager.PinReplayWorld();
            LoadRun(true);
            Check("replay from " + W(reached) + ": flies " + W(start) + " again (" + W(WorldManager.CurrentIndex) + ")", WorldManager.CurrentIndex == start);
            foreign += Audit(start, "replay start");
            foreign += Fly(120f, start, "replay " + W(start));
            Check("replay of a " + W(start) + " run that reached " + W(reached) + ": only " + W(start) + "'s own (" + audited + " live checks, " +
                  foreign + " foreign" + Examples() + ")", foreign == 0);
            WorldManager.ClearReplayWorld();
        }
    }

    // The saved world (PlayerPrefs) is written by others mid-run: the cloud
    // pull (ProgressSnapshot.Apply), developer mode's restore. The run stays
    // on its world: that is what the backdrop, rails and boss are painted for.
    static void SavedWorldWrittenMidRun()
    {
        for (int w = 1; w < Worlds; w++)
        {
            firstForeign.Clear(); audited = 0;
            Prefs(w);
            Random.InitState(7000 + w);
            LoadRun(true);
            int foreign = Fly(40f, w, W(w));
            PlayerPrefs.SetInt(WorldManager.PrefsCurrentWorld, 0);   // another writer
            foreign += Fly(120f, w, W(w) + " after the write");
            Check(W(w) + ": a write of the saved world mid-run changes nothing the run fields (" + audited + " live checks, " + foreign +
                  " foreign" + Examples() + ")", foreign == 0 && WorldManager.CurrentIndex == w);
        }
    }
}
