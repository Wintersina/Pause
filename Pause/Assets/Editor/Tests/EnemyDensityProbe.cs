using System.Collections.Generic;
using System.Reflection;
using UnityEditor.SceneManagement;
using UnityEngine;

// Measures what the spawner actually puts on the board: spawns per second
// and the mean number of enemies inside the view, headless, with every enemy
// moved by its real mover. Two read-outs:
//
//   pinned   the level clock and the scroll speed held at one moment of a
//            stock Space run (HUD speed s, the level second that run reaches
//            it), sampled for WindowSeconds after a warm-up
//   run      a whole 120 s level flown on a speed curve
//
// Logged as "[DENSITY] ..." lines (EnemyDensityTest asserts on the same
// numbers). -executeMethod EnemyDensityProbe.Run prints the table.
public static class EnemyDensityProbe
{
    internal const BindingFlags Inst = BindingFlags.NonPublic | BindingFlags.Instance;
    static readonly MethodInfo SpawnStep = typeof(enmiesOnBoard).GetMethod("spawn", Inst, null, new[] { typeof(float) }, null);
    static readonly MethodInfo Select = typeof(enmiesOnBoard).GetMethod("SelectPhase", Inst);
    internal static readonly FieldInfo Elapsed = typeof(enmiesOnBoard).GetField("elapsedFlightSeconds", Inst);
    static readonly FieldInfo Rails = typeof(enmiesOnBoard).GetField("liveRails", Inst);
    internal static readonly string[] Timers =
    {
        "railDelayTimer", "smEnmDelayTimer", "bigEnmDelayTimer", "smallAstroidDelayTimer", "midAstroidDelayTimer",
        "bigAstroidDelayTimer", "spawnAnimatedEnimeOneDelayTimer", "extraEnemyDelayTimer", "mineDelayTimer", "chaserDelayTimer",
    };

    public const float Dt = 1f / 60f;
    public const float WarmupSeconds = 12f, WindowSeconds = 36f;
    // The view the probe measures in: the scene camera's (10 u tall as
    // authored; SetView makes it a phone's, as CameraFit would at run time).
    public static float ViewHalfHeight => (CameraFit.ViewTop - CameraFit.ViewBottom) * .5f;
    public static float SpawnY => CameraFit.ViewTop + 2f;
    public const float AuthoredHalfHeight = 5f;

    static float savedOrtho = -1f, savedAspect;

    // The camera a `width` x `height` phone gets (CameraFit.GameplayHalfWidth).
    public static void SetView(int width, int height)
    {
        var cam = Camera.main;
        if (cam == null) return;
        if (savedOrtho < 0f) { savedOrtho = cam.orthographicSize; savedAspect = cam.aspect; }
        cam.aspect = width / (float)height;
        cam.orthographicSize = CameraFit.ComputeSize(AuthoredHalfHeight, CameraFit.GameplayHalfWidth, width, height);
    }

    // The authored 10 u view the recorded baseline was measured in.
    public static void SetAuthoredView()
    {
        var cam = Camera.main;
        if (cam == null) return;
        if (savedOrtho < 0f) { savedOrtho = cam.orthographicSize; savedAspect = cam.aspect; }
        cam.orthographicSize = AuthoredHalfHeight;
    }

    public static void RestoreView()
    {
        var cam = Camera.main;
        if (cam != null && savedOrtho > 0f) { cam.orthographicSize = savedOrtho; cam.aspect = savedAspect; }
        savedOrtho = -1f;
    }

    public struct Sample
    {
        public float spawnsPerSecond, onScreen, shots, peakOnScreen;
        // pilots in view (mean), and how long the ones that left had stayed
        public float pilots, engageSeconds, inViewSeconds, pilotsDeparted;
        public float Threats => onScreen + shots * ShotWeight;
    }

    // A projectile is a smaller, simpler threat than a body.
    public const float ShotWeight = .5f;

    // The stock Space ramp the pinned points are taken along (the curve the
    // game shipped with before the 2026-10 retune): HUD speed per second.
    public const float ReferenceHudPerSecond = .315f;
    // 35 is the natural cap (SpeedRamp.Cap). 40 and 46 stay in the table
    // because the recorded "before" numbers were taken there; they are now
    // limit-break speeds only (the blue atom's boost over the cap, at most
    // 45), where EnemyDensity fields exactly what it fields at 35.
    public static readonly int[] HudPoints = { 5, 10, 20, 30, 35, 40, 46 };

    public static float LevelSecondFor(int hud)
    {
        return Mathf.Min(119f, hud / ReferenceHudPerSecond);
    }

    public static void Run()
    {
        TestHarness.Exit(Execute());
    }

    public static int Execute()
    {
        using var sandbox = new TestHarness.Sandbox();
        try
        {
            EnemyThreat.ForceShooting = true;
            EditorSceneLoader.Open("gameS1", OpenSceneMode.Single);
            Debug.Log("[DENSITY] pinned: HUD speed, level second -> spawns/s, on screen (mean / peak), shots, threats");
            foreach (int hud in HudPoints)
            {
                var s = Pinned(hud, LevelSecondFor(hud), 3);
                Debug.Log(string.Format("[DENSITY] pinned hud {0,2} t {1,5:F1}s  spawns/s {2:F2}  onscreen {3:F2} / {4:F0}  shots {5:F2}  threats {6:F2}",
                                        hud, LevelSecondFor(hud), s.spawnsPerSecond, s.onScreen, s.peakOnScreen, s.shots, s.Threats));
            }
            var r = WholeRun(ReferenceHudPerSecond, SpeedRamp.CapHud, 3);
            Debug.Log(string.Format("[DENSITY] run reference ramp  spawns/s {0:F2}  onscreen {1:F2}  shots {2:F2}  threats {3:F2}",
                                    r.spawnsPerSecond, r.onScreen, r.shots, r.Threats));
            var theme = WorldManager.Worlds[0];
            r = WholeRun(t => 100f * SpeedRamp.SpeedAfter(0f, theme.speedRampPerSecond, SpeedRamp.Cap, t), 3);
            Debug.Log(string.Format("[DENSITY] run live Space curve  spawns/s {0:F2}  onscreen {1:F2}  shots {2:F2}  threats {3:F2}",
                                    r.spawnsPerSecond, r.onScreen, r.shots, r.Threats));
        }
        finally
        {
            EnemyThreat.ForceShooting = false;
            SpawnSpace.ClockOverride = null;
            Clear();
        }
        return 0;
    }

    // ---- fixtures ----------------------------------------------------------

    internal static enmiesOnBoard NewBoard()
    {
        var board = new GameObject("~DensityBoard").AddComponent<enmiesOnBoard>();
        board.transform.position = new Vector3(0f, SpawnY, 0f);
        board.SendMessage("Start");
        return board;
    }

    public static void Clear()
    {
        EliteSystem.Clear();
        EnemyThreat.Reset();
        PilotAirspace.Clear();
        foreach (var f in Object.FindObjectsByType<SpawnFootprint>(FindObjectsSortMode.None)) Object.DestroyImmediate(f.gameObject);
        foreach (var r in Object.FindObjectsByType<RailLaneScroller>(FindObjectsSortMode.None)) Object.DestroyImmediate(r.gameObject);
        foreach (var b in Object.FindObjectsByType<enmiesOnBoard>(FindObjectsSortMode.None)) Object.DestroyImmediate(b.gameObject);
        foreach (var e in Object.FindObjectsByType<EnemyIdentity>(FindObjectsSortMode.None)) Object.DestroyImmediate(e.gameObject);
    }

    static readonly List<SpawnFootprint> buffer = new List<SpawnFootprint>(128);
    internal static readonly HashSet<ChaserEnemy> chasers = new HashSet<ChaserEnemy>();

    // One frame of the whole board at scroll `v` (u/s), `t` on the weave clock.
    // (`pilot`: where the stand-in ship flies this frame; null: the slow weave.)
    internal static void StepBoard(enmiesOnBoard board, Transform ship, float t, float v, System.Func<float, Vector3> pilot = null)
    {
        SpawnSpace.ClockOverride = t;
        ship.position = pilot != null ? pilot(t) : new Vector3(Mathf.Sin(t * .7f) * 1.8f, -3f, 0f);
        Select.Invoke(board, null);
        SpawnStep.Invoke(board, new object[] { Dt });

        buffer.Clear();
        buffer.AddRange(SpawnSpace.Live(SpawnLayer.Enemy));
        foreach (var f in buffer)
        {
            moveEnimes weave;
            moveItemEnmInStrightLine straight;
            if (f.TryGetComponent(out weave) && weave.enabled) weave.Step(Dt, t);
            else if (f.TryGetComponent(out straight) && straight.enabled) straight.Step(Dt);
        }
        var rails = Rails.GetValue(board) as List<Transform>;
        if (rails != null)
            for (int i = rails.Count - 1; i >= 0; i--)
            {
                var r = rails[i];
                if (r == null) { rails.RemoveAt(i); continue; }
                r.position += Vector3.down * v * Dt;
                if (r.position.y < -12f) { Object.DestroyImmediate(r.gameObject); rails.RemoveAt(i); }
            }
        // LateUpdate: the brains (pattern + attack), then the mines settle on
        // their rails, then the chasers steer; then the shots
        foreach (var f in buffer)
        {
            EnemyBrain brain;
            if (f == null || !f.TryGetComponent(out brain) || !brain.enabled) continue;
            brain.TargetOverride = ship;
            brain.Step(Dt);
        }
        foreach (var f in buffer)
        {
            RailMineMount mount;
            if (!f.TryGetComponent(out mount)) continue;
            if (mount.rail == null) Object.DestroyImmediate(f.gameObject);
            else mount.SendMessage("LateUpdate");
        }
        buffer.Clear();
        buffer.AddRange(SpawnSpace.Live(SpawnLayer.Enemy));
        foreach (var f in buffer)
        {
            ChaserEnemy c;
            if (!f.TryGetComponent(out c)) continue;
            if (chasers.Add(c)) c.Target = ship;
            c.Step(Dt);
        }
        foreach (var f in buffer)
        {
            float y = f.transform.position.y;
            if (y < -ViewHalfHeight - 3f || y > ViewHalfHeight + 15f) Object.DestroyImmediate(f.gameObject);   // off the board (departed pilots park at +/-60)
        }
        EliteSystem.Step(Dt);
    }

    static int OnScreen()
    {
        int n = 0;
        var live = SpawnSpace.Live(SpawnLayer.Enemy);
        for (int i = 0; i < live.Count; i++)
        {
            float y = live[i].transform.position.y;
            if (y >= -ViewHalfHeight && y <= ViewHalfHeight) n++;
        }
        return n;
    }

    static int PilotsOnScreen()
    {
        int n = 0;
        var live = PilotAirspace.Live;
        for (int i = 0; i < live.Count; i++)
        {
            float y = live[i].transform.position.y;
            if (y >= -ViewHalfHeight && y <= ViewHalfHeight) n++;
        }
        var all = SpawnSpace.Live(SpawnLayer.Enemy);
        for (int i = 0; i < all.Count; i++)
        {
            ChaserEnemy c;
            float y = all[i].transform.position.y;
            if (y >= -ViewHalfHeight && y <= ViewHalfHeight && all[i].TryGetComponent(out c)) n++;
        }
        return n;
    }

    static void AddPilotStats(ref Sample total)
    {
        total.pilotsDeparted += PilotAirspace.Departed;
        total.engageSeconds += PilotAirspace.EngagedSecondsTotal;
        total.inViewSeconds += PilotAirspace.InViewSecondsTotal;
    }

    static void FinishPilotStats(ref Sample total, int seeds)
    {
        total.pilots /= seeds;
        if (total.pilotsDeparted > 0f) { total.engageSeconds /= total.pilotsDeparted; total.inViewSeconds /= total.pilotsDeparted; }
    }

    static int ShotsOnScreen()
    {
        return HostileShots.ActiveCount;
    }

    // The board held at one moment: level second `levelSecond`, HUD `hud`.
    public static Sample Pinned(int hud, float levelSecond, int seeds)
    {
        var total = new Sample();
        for (int seed = 0; seed < seeds; seed++)
        {
            Clear();
            chasers.Clear();
            buttonClicks.playerDied = false;
            score.pauseCounter = 0;
            LoopDifficulty.Reset();
            Random.InitState(4100 + hud * 13 + seed);
            float v = hud * .3f;
            moveBackGround.speed = hud / 100f;
            var board = NewBoard();
            var ship = new GameObject("~DensityShip").transform;
            foreach (string timer in Timers)
                typeof(enmiesOnBoard).GetField(timer, Inst).SetValue(board, Random.Range(0f, 1.5f));
            int before = 0;
            float onScreen = 0f, shots = 0f, peak = 0f, pilots = 0f;
            PilotAirspace.ResetStats();
            int frames = 0;
            float clock = 0f;
            for (float t = 0f; t < WarmupSeconds + WindowSeconds; t += Dt)
            {
                clock += Dt;
                Elapsed.SetValue(board, levelSecond);
                if (t < WarmupSeconds) before = board.SpawnedCount;
                StepBoard(board, ship, clock, v);
                if (t < WarmupSeconds) continue;
                frames++;
                int n = OnScreen();
                onScreen += n;
                peak = Mathf.Max(peak, n);
                shots += ShotsOnScreen();
                pilots += PilotsOnScreen();
            }
            total.spawnsPerSecond += (board.SpawnedCount - before) / WindowSeconds;
            total.pilots += pilots / frames;
            AddPilotStats(ref total);
            total.onScreen += onScreen / frames;
            total.shots += shots / frames;
            total.peakOnScreen = Mathf.Max(total.peakOnScreen, peak);
            Object.DestroyImmediate(ship.gameObject);
            Clear();
        }
        total.spawnsPerSecond /= seeds;
        total.onScreen /= seeds;
        total.shots /= seeds;
        FinishPilotStats(ref total, seeds);
        return total;
    }

    // A whole 120 s level on a linear ramp of `hudPerSecond` up to `capHud`.
    public static Sample WholeRun(float hudPerSecond, float capHud, int seeds)
    {
        return WholeRun(t => Mathf.Min(capHud, hudPerSecond * t), seeds);
    }

    public static Sample WholeRun(System.Func<float, float> hudAt, int seeds)
    {
        var total = new Sample();
        const float seconds = 120f;
        for (int seed = 0; seed < seeds; seed++)
        {
            Clear();
            chasers.Clear();
            buttonClicks.playerDied = false;
            score.pauseCounter = 0;
            LoopDifficulty.Reset();
            Random.InitState(9100 + seed);
            var board = NewBoard();
            var ship = new GameObject("~DensityShip").transform;
            float onScreen = 0f, shots = 0f, peak = 0f, pilots = 0f;
            PilotAirspace.ResetStats();
            int frames = 0;
            for (float t = 0f; t < seconds; t += Dt)
            {
                float hud = hudAt(t);
                moveBackGround.speed = hud / 100f;
                Elapsed.SetValue(board, t);
                StepBoard(board, ship, t, hud * .3f);
                frames++;
                int n = OnScreen();
                onScreen += n;
                peak = Mathf.Max(peak, n);
                shots += ShotsOnScreen();
                pilots += PilotsOnScreen();
            }
            total.pilots += pilots / frames;
            AddPilotStats(ref total);
            total.spawnsPerSecond += board.SpawnedCount / seconds;
            total.onScreen += onScreen / frames;
            total.shots += shots / frames;
            total.peakOnScreen = Mathf.Max(total.peakOnScreen, peak);
            Object.DestroyImmediate(ship.gameObject);
            Clear();
        }
        total.spawnsPerSecond /= seeds;
        total.onScreen /= seeds;
        total.shots /= seeds;
        FinishPilotStats(ref total, seeds);
        return total;
    }
}
