using System.Collections.Generic;
using System.Reflection;
using System.Text;
using UnityEditor.SceneManagement;
using UnityEngine;

// How long elites last against the board itself -- friendly fire, with a
// pilot who never touches them -- and whether the pilot can still kill them.
// Headless: the real spawner on every enemy's real mover and brain
// (EnemyDensityProbe.StepBoard), the real elite step, mine blasts and the
// free movers' crashes, at a pinned HUD speed in each elite's own world.
//
//   solo     one elite of a type parks on a pad over a warmed-up board,
//            lifts off and is watched for WatchSeconds in play
//   group    the director's own group of three (elite vs elite)
//   hunted   solo, but the pilot lines up under it and shoots (virtual
//            straight shots, TakeShipAttack on a hit): it must still die
//
// Run() prints each mode with the evasion off ("before": the game as it
// was), on, and -- solo -- on without the spawn shadow, plus what the board
// spawned per second while the elites were out.
//
// Caveat: the stand-in pilot has no body. A dash that would have ended on
// the ship flies on through it into whatever is behind, so the dashers'
// board deaths are overstated.
//
// Logged as "[ELITEPROBE] ..." lines; EliteEvasionTest asserts on the same
// numbers. -executeMethod EliteSurvivalProbe.Run prints the tables.
public static class EliteSurvivalProbe
{
    public const float Dt = 1f / 60f;
    public const float WarmupSeconds = 4f, WatchSeconds = 15f, EarlySeconds = 5f;
    public static readonly int[] HudPoints = { 10, 20, 30, 40 };

    public sealed class Row
    {
        public string key;
        public int flown;          // elites that were parked for the trial
        public int deaths;         // died to the board (no pilot involved)
        public int early;          // ... within EarlySeconds of joining
        public int playerKills;    // hunted mode: died to the pilot's shots
        public float deathSeconds; // summed time from joining to a board death
        public float killSeconds;  // summed time from joining to a pilot kill
        public int attacks;        // attacks begun by the survivors (its own pattern still runs)
        public int survivors;
        public readonly Dictionary<string, int> sources = new Dictionary<string, int>();

        public float DeathRate => flown > 0 ? (float)deaths / flown : 0f;
        public float EarlyRate => flown > 0 ? (float)early / flown : 0f;
        public float MeanDeathSeconds => deaths > 0 ? deathSeconds / deaths : 0f;
        public float AttacksPerSurvivor => survivors > 0 ? (float)attacks / survivors : 0f;
    }

    public sealed class Table
    {
        public readonly Dictionary<string, Row> rows = new Dictionary<string, Row>();
        public readonly List<string> order = new List<string>();

        public Row For(string key)
        {
            Row r;
            if (!rows.TryGetValue(key, out r)) { r = new Row { key = key }; rows[key] = r; order.Add(key); }
            return r;
        }

        public Row Total()
        {
            var t = new Row { key = "ALL" };
            foreach (var k in order)
            {
                var r = rows[k];
                t.flown += r.flown; t.deaths += r.deaths; t.early += r.early; t.playerKills += r.playerKills;
                t.deathSeconds += r.deathSeconds; t.killSeconds += r.killSeconds; t.attacks += r.attacks; t.survivors += r.survivors;
                foreach (var s in r.sources) { int n; t.sources.TryGetValue(s.Key, out n); t.sources[s.Key] = n + s.Value; }
            }
            return t;
        }
    }

    public static void Run() { TestHarness.Exit(Execute()); }

    // A short, talkative run for tuning: every board death with where the
    // elite was, what it was doing and what it thought was coming.
    public static bool Verbose;

    // The last second of the first watched elite, frame by frame (Verbose).
    const int TraceFrames = 48;
    static readonly string[] trace = new string[TraceFrames];
    static int traceAt, tracesLeft, traceEvery;
    static string lastThreats = "";

    static void Trace(EliteShip e)
    {
        if (!Verbose || e == null || !e.InPlay) return;
        // the hazard nearest to touching it
        ClearTarget near = null;
        float gap = 99f;
        foreach (var t in ClearTarget.Live)
        {
            if (t == null || t.Elite != null || !ClearTarget.IsHazard(t.gameObject)) continue;
            float g = ((Vector2)t.transform.position - e.Position).magnitude - (e.Def.hullRadius * .85f + t.Radius * .8f);
            if (g < gap) { gap = g; near = t; }
        }
        if (traceEvery++ % 12 == 0)
        {
            var tb = new StringBuilder("    threats:");
            for (int i = 0; i < EliteEvasion.ThreatCount; i++)
                tb.AppendFormat(" [{0} {1} v {2} r {3:F2} from {4:F1}]", EliteEvasion.ThreatKind(i), EliteEvasion.ThreatAt(i).ToString("F1"),
                                EliteEvasion.ThreatVelocity(i).ToString("F1"), EliteEvasion.ThreatRadius(i), EliteEvasion.ThreatFrom(i));
            lastThreats = tb.ToString();
        }
        trace[traceAt++ % TraceFrames] = (traceEvery % 12 == 1 ? lastThreats + "\n" : "") + string.Format("  t {0:F2} {1}{2} hearts {3} at {4} v {5} | {6} to {7} hitIn {8:F2} | nearest {9} gap {10:F2} at {11} v {12} r {13:F2}",
            e.PlaySeconds, e.State, e.Acting ? "/act" : e.Telling ? "/tell" : "", e.Hearts, e.Position.ToString("F2"), e.Velocity.ToString("F2"),
            e.Evading ? "EVADE" : "goal", e.Claim.ToString("F2"), e.HitIn,
            near != null ? near.name : "-", gap, near != null ? ((Vector2)near.transform.position).ToString("F2") : "", near != null ? near.SensedVelocity.ToString("F2") : "", near != null ? near.Radius : 0f);
    }

    static void DumpTrace()
    {
        if (tracesLeft <= 0) return;
        tracesLeft--;
        var sb = new StringBuilder("[ELITEPROBE] trace\n");
        for (int i = 0; i < TraceFrames; i += 2)
        {
            string line = trace[(traceAt + i) % TraceFrames];
            if (line != null) sb.Append(line).Append('\n');
        }
        Debug.Log(sb.ToString());
    }
    public static void RunDebug()
    {
        using var sandbox = new TestHarness.Sandbox();
        try
        {
            Open();
            Verbose = true;
            tracesLeft = 14;
            Print("solo", Solo(new[] { 20, 30 }, 3, false));
        }
        finally { Verbose = false; Close(); }
        TestHarness.Exit(0);
    }

    public static int Execute()
    {
        using var sandbox = new TestHarness.Sandbox();
        try
        {
            Open();
            float shadow = EliteEvasion.SpawnShadowSeconds;
            EliteEvasion.Enabled = false;
            Print("solo, evasion OFF (before)", Solo(HudPoints, 6, false));
            Print("group, evasion OFF (before)", Groups(HudPoints, 6));
            Print("hunted, evasion OFF (before)", Solo(new[] { 10, 20, 30 }, 4, true));
            EliteEvasion.Enabled = true;
            Print("solo", Solo(HudPoints, 6, false));
            Print("group", Groups(HudPoints, 6));
            Print("hunted", Solo(new[] { 10, 20, 30 }, 4, true));
            EliteEvasion.SpawnShadowSeconds = 0f;
            Print("solo, no spawn shadow", Solo(HudPoints, 6, false));
            EliteEvasion.SpawnShadowSeconds = shadow;
        }
        finally { EliteEvasion.Enabled = true; Close(); }
        return 0;
    }

    public static void Print(string mode, Table t)
    {
        Debug.Log("[ELITEPROBE] " + mode + ": elite, flown, board deaths (rate), of them within " + EarlySeconds +
                  " s of joining (rate), mean s from joining to death, pilot kills, attacks per survivor | killing sources");
        foreach (var k in t.order) Debug.Log(Line(mode, t.rows[k]));
        Debug.Log(Line(mode, t.Total()));
        if (BySpeed != null) foreach (var k in BySpeed.order) Debug.Log(Line(mode, BySpeed.rows[k]));
        Debug.Log(string.Format("[ELITEPROBE] {0}: the board spawned {1:F2} bodies/s while elites were parked or out ({2:F0} s)",
                                mode, watchSeconds > 0f ? watchSpawns / watchSeconds : 0f, watchSeconds));
    }

    static string Line(string mode, Row r)
    {
        var sb = new StringBuilder();
        sb.AppendFormat("[ELITEPROBE] {0} | {1,-28} flown {2,3}  deaths {3,3} ({4,4:P0})  early {5,3} ({6,4:P0})  mean {7,4:F1}s  pilot kills {8,3}{9}  attacks/survivor {10:F1} |",
                        mode, r.key, r.flown, r.deaths, r.DeathRate, r.early, r.EarlyRate, r.MeanDeathSeconds, r.playerKills,
                        r.playerKills > 0 ? " (mean " + (r.killSeconds / r.playerKills).ToString("F1") + "s)" : "", r.AttacksPerSurvivor);
        var keys = new List<string>(r.sources.Keys);
        keys.Sort((a, b) => r.sources[b].CompareTo(r.sources[a]));
        foreach (var k in keys) sb.Append(" ").Append(k).Append(" ").Append(r.sources[k]);
        return sb.ToString();
    }

    // ---- fixtures -----------------------------------------------------------

    static WorldManager madeManager;
    static readonly PropertyInfo ManagerInstance = typeof(WorldManager).GetProperty("Instance", BindingFlags.Public | BindingFlags.Static);

    public static void Open()
    {
        EnemyThreat.ForceShooting = true;
        EditorSceneLoader.Open("gameS1", OpenSceneMode.Single);
    }

    public static void Close()
    {
        EnemyThreat.ForceShooting = false;
        SpawnSpace.ClockOverride = null;
        EliteShip.Died = null;
        EliteSystem.PlayerOverride = null;
        LandingSites.Override = null;
        ManagerInstance.SetValue(null, null);
        if (madeManager != null) Object.DestroyImmediate(madeManager.gameObject);
        madeManager = null;
        FriendlyFire.ClearPending();
        EnemyDensityProbe.Clear();
        moveBackGround.speed = 0f;
    }

    // The spawner fields the world's own roster.
    static void SetWorld(int world)
    {
        if (WorldManager.Instance == null)
        {
            var wm = Object.FindFirstObjectByType<WorldManager>(FindObjectsInactive.Include);
            if (wm == null) wm = madeManager = new GameObject("~ProbeWorld").AddComponent<WorldManager>();
            ManagerInstance.SetValue(null, wm);
        }
        WorldManager.CurrentIndex = world;
    }

    static readonly List<LandingSite> pads = new List<LandingSite>();
    static readonly List<Transform> padAnchors = new List<Transform>();

    static void MakePads(int n)
    {
        foreach (var a in padAnchors) if (a != null) Object.DestroyImmediate(a.gameObject);
        padAnchors.Clear();
        pads.Clear();
        for (int i = 0; i < n; i++)
        {
            var anchor = new GameObject("~ProbePad" + i).transform;
            // background terrain: anywhere across the view's upper two thirds
            anchor.position = new Vector3(Random.Range(-.8f, .8f) * EliteSystem.RailEdge, Random.Range(-1f, 3.5f), 0f);
            padAnchors.Add(anchor);
            pads.Add(new LandingSite { anchor = anchor, local = Vector3.zero, scale = .3f, order = -420, id = 100 + i });
        }
        LandingSites.Override = list => list.AddRange(pads);
    }

    // What the spawner did while the trial's elites were parked or out.
    static float watchSpawns, watchSeconds;

    // The trial under way.
    static Table table;
    static bool hunted;
    static int trialHud;
    // The same deaths again, by HUD speed (rows "hud 10" ...).
    public static Table BySpeed { get; private set; }

    static Row SpeedRow => BySpeed.For("hud " + trialHud);

    static void OnDied(EliteShip e, EliteDamage cause, string by)
    {
        var r = table.For(e.Def.key);
        bool pilot = cause == EliteDamage.PlayerWeapon || cause == EliteDamage.Teleport || cause == EliteDamage.ShieldRam || cause == EliteDamage.PlayerContact;
        string what = cause == EliteDamage.Rail ? "rail" : string.IsNullOrEmpty(by) ? cause.ToString() : by;
        if (Verbose && !pilot)
            Debug.Log(string.Format("[ELITEPROBE] death {0} hud {1} after {2:F2}s by {3}: state {4}{5} at {6} v {7} evading {8} to {9} hitIn {10:F2} threats {11} scroll {12:F1}",
                                    e.Def.key, trialHud, e.PlaySeconds, what, e.State, e.Acting ? "/acting" : e.Telling ? "/telling" : "", e.Position, e.Velocity,
                                    e.Evading, e.EvadeTarget, e.HitIn, EliteEvasion.ThreatCount, EliteSystem.Scroll));
        if (Verbose && !pilot) DumpTrace();
        for (int pass = 0; pass < 2; pass++, r = SpeedRow)
        {
            if (pilot) { r.playerKills++; r.killSeconds += e.PlaySeconds; continue; }
            r.deaths++;
            r.deathSeconds += e.PlaySeconds;
            if (e.PlaySeconds <= EarlySeconds) r.early++;
            int n;
            r.sources.TryGetValue(what, out n);
            r.sources[what] = n + 1;
        }
    }

    // ---- the pilot's virtual shots (hunted) -----------------------------------

    const int MaxBullets = 16;
    const float BulletSpeed = 11f, FireEvery = .28f, PilotSpeed = 3.2f;
    static readonly Vector2[] bullets = new Vector2[MaxBullets];
    static readonly bool[] bulletLive = new bool[MaxBullets];
    static float fireClock;
    static Vector3 pilotAt;

    // The pilot slides under the nearest elite in play and fires straight up.
    static Vector3 Hunt(float t)
    {
        EliteShip mark = null;
        var live = EliteShip.Live;
        for (int i = 0; i < live.Count; i++) if (live[i] != null && live[i].InPlay) { mark = live[i]; break; }
        float want = mark != null ? mark.Position.x : Mathf.Sin(t * .7f) * 1.8f;
        float lane = EliteSystem.RailEdge - .4f;
        pilotAt.x = Mathf.MoveTowards(pilotAt.x, Mathf.Clamp(want, -lane, lane), PilotSpeed * Dt);
        pilotAt.y = -3f;
        fireClock -= Dt;
        if (mark != null && fireClock <= 0f && mark.Position.y > pilotAt.y && Mathf.Abs(mark.Position.x - pilotAt.x) < 1.2f)
        {
            fireClock = FireEvery;
            for (int i = 0; i < MaxBullets; i++)
                if (!bulletLive[i]) { bulletLive[i] = true; bullets[i] = new Vector2(pilotAt.x, pilotAt.y + .4f); break; }
        }
        return pilotAt;
    }

    static void StepBullets()
    {
        var live = EliteShip.Live;
        for (int i = 0; i < MaxBullets; i++)
        {
            if (!bulletLive[i]) continue;
            bullets[i].y += BulletSpeed * Dt;
            if (bullets[i].y > CameraFit.ViewTop + .5f) { bulletLive[i] = false; continue; }
            for (int k = 0; k < live.Count; k++)
            {
                var e = live[k];
                if (e == null || !e.InPlay) continue;
                float R = e.Def.hullRadius + .08f;
                if ((e.Position - bullets[i]).sqrMagnitude > R * R) continue;
                bulletLive[i] = false;
                e.TakeShipAttack(0, 1f, bullets[i]);
                break;
            }
        }
    }

    // ---- trials -----------------------------------------------------------------

    static readonly List<EliteShip> watch = new List<EliteShip>();

    // One trial: the board warmed up at `hud`, `spawn` parks the elites, then
    // everything runs until they are all dead or have had their watch.
    static void Trial(int world, int hud, int seed, System.Action<EliteDirector> spawn)
    {
        EnemyDensityProbe.Clear();
        EnemyDensityProbe.chasers.Clear();
        FriendlyFire.ClearPending();
        buttonClicks.playerDied = false;
        score.pauseCounter = 0;
        LoopDifficulty.Reset();
        SetWorld(world);
        trialHud = hud;
        Random.InitState(7300 + world * 1009 + hud * 31 + seed);
        float v = hud * .3f;
        moveBackGround.speed = hud / 100f;
        var board = EnemyDensityProbe.NewBoard();
        var ship = new GameObject("~ProbeShip").transform;
        pilotAt = new Vector3(0f, -3f, 0f);
        ship.position = pilotAt;
        EliteSystem.PlayerOverride = ship;
        for (int i = 0; i < MaxBullets; i++) bulletLive[i] = false;
        fireClock = 0f;
        foreach (string timer in EnemyDensityProbe.Timers)
            typeof(enmiesOnBoard).GetField(timer, EnemyDensityProbe.Inst).SetValue(board, Random.Range(0f, 1.5f));
        float level = EnemyDensityProbe.LevelSecondFor(hud);
        var dir = new GameObject("~ProbeDirector").AddComponent<EliteDirector>();
        var view = new Rect(-3f, CameraFit.ViewBottom, 6f, CameraFit.ViewTop - CameraFit.ViewBottom);
        System.Func<float, Vector3> fly = hunted ? Hunt : (System.Func<float, Vector3>)null;

        float clock = 0f;
        bool spawned = false;
        int spawnsAtStart = 0;
        float startedAt = 0f;
        watch.Clear();
        traceAt = 0;
        System.Array.Clear(trace, 0, TraceFrames);
        for (int guard = 0; guard < 60 * 60; guard++)
        {
            clock += Dt;
            EnemyDensityProbe.Elapsed.SetValue(board, level);
            if (!spawned && clock >= WarmupSeconds)
            {
                spawned = true;
                spawnsAtStart = board.SpawnedCount;
                startedAt = clock;
                MakePads(6);
                spawn(dir);
                watch.AddRange(EliteShip.Live);
            }
            // the pads ride the background (a slow parallax)
            foreach (var a in padAnchors) if (a != null) a.position += Vector3.down * v * .04f * Dt;
            EnemyDensityProbe.StepBoard(board, ship, clock, v, fly);
            FriendlyFire.StepBlasts(Dt);
            FriendlyFire.StepCrashes(view);
            if (hunted) StepBullets();
            if (!spawned) continue;
            if (Verbose && watch.Count > 0) Trace(watch[0]);
            bool busy = false;
            for (int i = 0; i < watch.Count; i++)
            {
                var e = watch[i];
                if (e == null || e.State == EliteState.Dead) continue;
                if (e.PlaySeconds < WatchSeconds) busy = true;
            }
            if (!busy) break;
        }
        watchSpawns += board.SpawnedCount - spawnsAtStart;
        watchSeconds += clock - startedAt;
        for (int i = 0; i < watch.Count; i++)
        {
            var e = watch[i];
            if (e == null || e.State == EliteState.Dead) continue;
            var r = table.For(e.Def.key);
            for (int pass = 0; pass < 2; pass++, r = SpeedRow)
            {
                r.attacks += e.Attacks;
                r.survivors++;
            }
        }
        Object.DestroyImmediate(dir.gameObject);
        Object.DestroyImmediate(ship.gameObject);
        EliteSystem.PlayerOverride = null;
        EnemyDensityProbe.Clear();
    }

    // Every elite type alone, `seeds` times at each HUD speed.
    public static Table Solo(int[] huds, int seeds, bool pilotShoots)
    {
        table = new Table();
        BySpeed = new Table();
        watchSpawns = watchSeconds = 0f;
        hunted = pilotShoots;
        EliteShip.Died = OnDied;
        foreach (var def in EliteCatalog.All)
        {
            var row = table.For(def.key);
            foreach (int hud in huds)
                for (int seed = 0; seed < seeds; seed++)
                {
                    Trial(def.WorldIndex, hud, seed, dir =>
                    {
                        dir.Spawn(def, pads[Random.Range(0, pads.Count)], EliteDirector.ParkSeconds.x);
                    });
                    row.flown++;
                    SpeedRow.flown++;
                }
        }
        EliteShip.Died = null;
        hunted = false;
        return table;
    }

    // The director's own groups of three in each world that has elites.
    public static Table Groups(int[] huds, int seeds)
    {
        table = new Table();
        BySpeed = new Table();
        watchSpawns = watchSeconds = 0f;
        hunted = false;
        EliteShip.Died = OnDied;
        for (int world = 0; world < EnemyRoster.WorldKeys.Length; world++)
        {
            if (!EliteCatalog.WorldHasElites(world)) continue;
            foreach (int hud in huds)
                for (int seed = 0; seed < seeds; seed++)
                {
                    Trial(world, hud, seed, dir =>
                    {
                        dir.SpawnGroup(world, 3);
                        var live = EliteShip.Live;
                        for (int i = 0; i < live.Count; i++) { table.For(live[i].Def.key).flown++; SpeedRow.flown++; }
                    });
                }
        }
        EliteShip.Died = null;
        return table;
    }
}
