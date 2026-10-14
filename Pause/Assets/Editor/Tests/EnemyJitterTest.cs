using System;
using System.Collections.Generic;
using System.Text;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

// "Sometimes when enemies zoom towards the player or come closer they just
// jitter and vibrate up and down quickly."
//
// Every roster enemy (rocks, fighters, heavies, aliens, chasers, mines, in all
// six worlds) is flown for 20 s at 60 fps and at 30 fps against a stand-in
// ship on several paths (resting, at the top of its reach, nearly the enemy's
// own y, straight under it, sweeping up and down, sweeping sideways). The
// body's y per frame is recorded; a JITTER is the y velocity changing sign
// (by more than a visible step) more than MaxFlipsPerSecond times in a
// second, for longer than MaxJitterSeconds in all.
//
//   Unity -batchmode -quit -projectPath <abs>/Pause -executeMethod EnemyJitterTest.Run
public static class EnemyJitterTest
{
    static int fails;
    static void Check(string what, bool ok)
    {
        Debug.Log((ok ? "[JITTER] PASS  " : "[JITTER] FAIL  ") + what);
        if (!ok) fails++;
    }
    static void Log(string what) { Debug.Log("[JITTER] " + what); }

    public static void Run() { TestHarness.Exit(Execute()); }

    // A y step under this is not a "move" (float noise, a slow drift).
    public const float VisibleStep = .004f;
    // More direction changes than this in one second is a vibration (a bob is 2 a period).
    public const int MaxFlipsPerSecond = 10;
    // ... that goes on longer than this.
    public const float MaxJitterSeconds = .5f;
    public const float Seconds = 20f;

    static readonly string[] Paths = { "rest", "top", "sameY", "under", "sweepY", "sweepX" };

    internal static Transform ship;   // (EnemyAvoidanceTest flies dense scenes with the same fixtures)
    internal static float clock;

    public static int Execute()
    {
        fails = 0;
        using var sandbox = new TestHarness.Sandbox();
        try
        {
            EditorSceneLoader.Open("gameS1", OpenSceneMode.Single);
            Fresh();
            Everything();
        }
        finally
        {
            EnemyThreat.ForceShooting = false;
            EnemyThreat.Reset();
            EnemyBrain.PilotsEnabled = true;
            PilotAirspace.Clear();
            SpawnSpace.ClockOverride = null;
            EliteSystem.Clear();
            EliteSystem.PlayerOverride = null;
            Clear();
            buttonClicks.playerDied = false;
        }
        Debug.Log("[JITTER] failures: " + fails);
        return fails;
    }

    internal static void Clear()
    {
        foreach (var b in Object.FindObjectsByType<enmiesOnBoard>(FindObjectsSortMode.None)) Object.DestroyImmediate(b.gameObject);
        foreach (var e in Object.FindObjectsByType<EnemyIdentity>(FindObjectsSortMode.None)) Object.DestroyImmediate(e.gameObject);
        foreach (var r in Object.FindObjectsByType<RailLaneScroller>(FindObjectsSortMode.None)) Object.DestroyImmediate(r.gameObject);
        if (ship != null) Object.DestroyImmediate(ship.gameObject);
        ship = null;
    }

    internal static void Fresh()
    {
        Clear();
        EliteSystem.Clear();
        RailMineLasers.Clear();
        EnemyThreat.Reset();
        PilotAirspace.Clear();
        PilotAirspace.ResetStats();
        EnemyBrain.PilotsEnabled = true;
        EnemyThreat.ForceShooting = true;
        buttonClicks.playerDied = false;
        score.pauseCounter = 0;
        startMenu.youAreInTutorial = false;
        moveBackGround.speed = .2f;
        clock = 100f;
        SpawnSpace.ClockOverride = clock;
        ship = new GameObject("~JitterShip").transform;
        ship.position = new Vector3(0f, ShipReach.StartY, 0f);
        EliteSystem.PlayerOverride = ship;
    }

    // Where the stand-in ship is at time t, for an enemy at `at`.
    static Vector2 ShipAt(string path, float t, Vector2 at)
    {
        switch (path)
        {
            case "top": return new Vector2(0f, ShipReach.Top);
            case "sameY": return new Vector2(at.x + .3f, at.y);                   // nearly the enemy's own y, close in x
            case "under": return new Vector2(at.x, at.y - 2f);                    // straight under it, following
            case "sweepY": return new Vector2(.2f, Mathf.Lerp(ShipReach.StartY, ShipReach.Top, .5f + .5f * Mathf.Sin(t * 1.3f)));
            case "sweepX": return new Vector2(Mathf.Sin(t * 1.1f) * 1.8f, ShipReach.StartY);
            default: return new Vector2(0f, ShipReach.StartY);
        }
    }

    sealed class Trace
    {
        public string who, path;
        public float dt;
        public List<float> y = new List<float>(1300);
        public List<float> x = new List<float>(1300);
        public bool leftPlay;
        public float shipGap;   // closest approach to the ship
    }

    internal sealed class Actor
    {
        public GameObject go, rail;
        public EnemyBrain brain;
        public ChaserEnemy chaser;
        public moveEnimes weaver;
        public moveItemEnmInStrightLine scroller;
        public RailMineMount mount;
        public bool Gone => go == null || (chaser != null && !chaser.enabled) || (brain != null && brain.IsPilot && brain.Stage == EnemyBrain.PilotStage.Gone);

        public static Actor Make(EnemyDef def, int variant, float xShift = 0f, float yShift = 0f)
        {
            var b = EnemyBehaviours.For(def.key);
            float railX = variant == 0 ? 2.35f : -2.35f;
            Vector3 start;
            if (def.role == EnemyRole.Chaser) start = new Vector3(variant == 0 ? 0f : 1.5f, CameraFit.ViewBottom - .5f, 0f);
            else if (b != null && b.IsPilot) start = new Vector3(variant == 0 ? 0f : -1f, CameraFit.ViewTop + PilotAirspace.WaitAbove, 0f);
            else if (def.role == EnemyRole.Mine) start = new Vector3(railX, CameraFit.ViewTop + 1f, 0f);
            else start = new Vector3(variant == 0 ? 0f : .8f, CameraFit.ViewTop + 1f, 0f);
            start += new Vector3(xShift, yShift, 0f);
            var a = new Actor();
            if (def.role == EnemyRole.Mine)
            {
                a.rail = new GameObject("RailMineLane");
                a.rail.transform.position = start;
                a.rail.AddComponent<RailLaneScroller>();
            }
            a.go = EnemyFactory.Create(def, start, Quaternion.identity);
            a.brain = a.go.GetComponent<EnemyBrain>();
            a.chaser = a.go.GetComponent<ChaserEnemy>();
            a.weaver = a.go.GetComponent<moveEnimes>();
            a.scroller = a.go.GetComponent<moveItemEnmInStrightLine>();
            if (a.brain != null) a.brain.TargetOverride = ship;
            if (a.chaser != null) a.chaser.Target = ship;
            if (def.role == EnemyRole.Mine && a.brain != null)
            {
                a.mount = a.go.AddComponent<RailMineMount>();
                a.mount.MountTo(a.rail.transform);
                a.mount.brain = a.brain;
            }
            return a;
        }

        public void Step(float dt)
        {
            if (Gone) return;
            if (rail != null)
            {
                if (rail.transform.position.y > -1f) rail.transform.position += Vector3.down * .5f * dt;   // the rail scrolls; it ends at the bottom
            }
            if (weaver != null) weaver.Step(dt, clock);
            if (scroller != null) scroller.Step(dt);
            if (chaser != null && chaser.enabled) chaser.Step(dt);
            if (brain != null && brain.enabled) brain.Step(dt);
            if (mount != null) mount.SendMessage("LateUpdate");
        }

        public void Destroy()
        {
            if (go != null) Object.DestroyImmediate(go);
            if (rail != null) Object.DestroyImmediate(rail);
        }
    }

    // variant 0/1: alone, from two starting columns. variant 2: in a crowd -- its world's rocks falling down
    // its column and a second chaser hunting the same ship, so SpawnSpace.ResolveSteer has to give way.
    static Trace Fly(EnemyDef def, string path, float dt, int seed, int variant)
    {
        Fresh();
        UnityEngine.Random.InitState(seed);
        var tr = new Trace { who = def.key + (variant != 0 ? "#" + variant : ""), path = path, dt = dt, shipGap = 99f };
        var self = Actor.Make(def, variant == 2 ? 0 : variant);
        var others = new List<Actor>();
        if (variant == 2)
        {
            var rocks = EnemyRoster.For(def.world, EnemyRole.Rock);
            for (int k = 0; k < 6 && rocks.Count > 0; k++)
            {
                var r = Actor.Make(rocks[k % rocks.Count], 0, (k % 2 == 0 ? 1f : -1f) * UnityEngine.Random.Range(.4f, 1.1f), k * 4.5f);
                others.Add(r);
            }
            var cs = EnemyRoster.For(def.world, EnemyRole.Chaser);
            if (cs.Count > 0 && def.role != EnemyRole.Chaser) others.Add(Actor.Make(cs[0], 1, 0f, 1f));
            if (cs.Count > 0 && def.role == EnemyRole.Chaser) others.Add(Actor.Make(cs[0], 1, 0f, 0f));
        }
        int frames = Mathf.RoundToInt(Seconds / dt);
        for (int i = 0; i < frames; i++)
        {
            if (self.Gone) { tr.leftPlay = true; break; }
            float t = i * dt;
            clock += dt;
            SpawnSpace.ClockOverride = clock;
            ship.position = ShipAt(path, t, self.go.transform.position);
            self.Step(dt);
            for (int k = 0; k < others.Count; k++) others[k].Step(dt);
            EliteSystem.Step(dt);
            if (self.Gone) { tr.leftPlay = true; break; }
            Vector2 p = self.go.transform.position;
            tr.shipGap = Mathf.Min(tr.shipGap, Vector2.Distance(p, ship.position));
            tr.x.Add(p.x);
            tr.y.Add(p.y);
        }
        self.Destroy();
        for (int k = 0; k < others.Count; k++) others[k].Destroy();
        return tr;
    }

    // Worst direction changes in any one second, and the seconds spent above the limit.
    static void Analyse(Trace tr, out int worstFlips, out float jitterSeconds, out float at, out float amp)
    {
        int n = tr.y.Count;
        var flip = new bool[n];
        int lastSign = 0;
        amp = 0f;
        for (int i = 1; i < n; i++)
        {
            float d = tr.y[i] - tr.y[i - 1];
            if (Mathf.Abs(d) < VisibleStep) continue;
            int s = d > 0f ? 1 : -1;
            if (lastSign != 0 && s != lastSign) { flip[i] = true; amp = Mathf.Max(amp, Mathf.Abs(d)); }
            lastSign = s;
        }
        int win = Mathf.RoundToInt(1f / tr.dt);
        worstFlips = 0; jitterSeconds = 0f; at = 0f;
        int count = 0;
        for (int i = 0; i < n; i++)
        {
            if (flip[i]) count++;
            if (i >= win && flip[i - win]) count--;
            if (count > worstFlips) { worstFlips = count; at = i * tr.dt; }
            if (count > MaxFlipsPerSecond) jitterSeconds += tr.dt;
        }
    }

    static void Everything()
    {
        var offenders = new List<string>();
        int flights = 0;
        var perKind = new SortedDictionary<string, string>();
        foreach (var def in EnemyRoster.All)
        {
            int variants = TestHarness.FullLane ? (def.role == EnemyRole.Mine ? 2 : 3) : 1;   // gate lane: the first variant
            for (int variant = 0; variant < variants; variant++)
                foreach (string path in Paths)
                    foreach (float dt in new[] { 1f / 60f, 1f / 30f })
                    {
                        var tr = Fly(def, path, dt, def.key.GetHashCode() + variant * 7, variant);
                        flights++;
                        int worst; float secs, at, amp;
                        Analyse(tr, out worst, out secs, out at, out amp);
                        if (secs > MaxJitterSeconds)
                        {
                            string line = tr.who + " " + path + " @" + Mathf.RoundToInt(1f / dt) + "fps: " + worst + " flips/s for " + secs.ToString("F1") +
                                          " s (from t=" + at.ToString("F1") + ", step " + amp.ToString("F3") + ", closest " + tr.shipGap.ToString("F2") + ")";
                            offenders.Add(line);
                            Log("JITTER " + line);
                        }
                    }
        }
        Log("flew " + flights + " flights; " + offenders.Count + " jittered");
        Check("no enemy vibrates up and down (more than " + MaxFlipsPerSecond + " direction changes a second for over " + MaxJitterSeconds +
              " s) over 20 s at 60 and 30 fps on any ship path (" + offenders.Count + " offenders)", offenders.Count == 0);
    }
}
