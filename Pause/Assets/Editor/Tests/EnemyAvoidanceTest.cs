using System;
using System.Collections.Generic;
using System.Text;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

// "The jittery effect is back and it seems it's because enemies are trying to
// avoid collision with each other. Let's loosen that up."
//
// Enemy-vs-enemy avoidance used to be a stack of hard rules: SpawnSpace.
// ResolveSteer (a ladder of candidate spots re-picked every frame, an on / off
// envelope test), PilotAirspace.BandLimits (a clamp that snapped as a hazard
// crossed the look window), ChaserEnemy's early sidestep (on / off). Each is a
// threshold: two bodies chattering round one choose a different spot every
// frame. It is now one soft, low-gain, damped drift (SpawnSpace.SoftSteer);
// what still runs into a neighbour crashes into it (FriendlyFire).
//
// This suite MEASURES the shake on dense boards and holds the new numbers.
// For every enemy that stays on screen it takes the per-frame path and counts
//   * flips: the sideways velocity changing sign by a visible step, the worst
//     second of the run (a bob is 2 a period; a vibration is dozens),
//   * jitter seconds: how long the flips run above MaxFlipsPerSecond,
//   * accel: the RMS of the second difference of x (u/s^2): high-frequency
//     shake is what it adds up.
// Scenes: a dense pilot / chaser / weaving rock board at 60 and 30 fps over
// three worlds; two elites with rocks coming down on them.
//
//   Unity -batchmode -quit -projectPath <abs>/Pause -executeMethod EnemyAvoidanceTest.Run
public static class EnemyAvoidanceTest
{
    static int fails;
    static void Check(string what, bool ok)
    {
        Debug.Log((ok ? "[AVOID] PASS  " : "[AVOID] FAIL  ") + what);
        if (!ok) fails++;
    }
    static void Log(string what) { Debug.Log("[AVOID] " + what); }

    public static void Run() { TestHarness.Exit(Execute()); }

    public const float VisibleStep = .004f;
    public const int MaxFlipsPerSecond = 10;
    public const float Seconds = 14f;

    // ---- measurement -----------------------------------------------------------

    public sealed class Trace
    {
        public string who;
        public float dt;
        public List<float> x = new List<float>(900), y = new List<float>(900);
        public bool died;
        public float diedAt;
    }

    public struct Score
    {
        public int worstFlips;       // worst second of sideways direction changes
        public int worstAt;          // frame the worst second ends on
        public float jitterSeconds;  // seconds above MaxFlipsPerSecond
        public float accelRms;       // RMS second difference of x, u/s^2
        public int frames;
    }

    public static Score Measure(Trace tr)
    {
        var s = new Score();
        int n = tr.x.Count;
        s.frames = n;
        if (n < 8) return s;
        var flip = new bool[n];
        int last = 0;
        for (int i = 1; i < n; i++)
        {
            float d = tr.x[i] - tr.x[i - 1];
            if (Mathf.Abs(d) < VisibleStep) continue;
            int sign = d > 0f ? 1 : -1;
            if (last != 0 && sign != last) flip[i] = true;
            last = sign;
        }
        int win = Mathf.RoundToInt(1f / tr.dt), count = 0;
        for (int i = 0; i < n; i++)
        {
            if (flip[i]) count++;
            if (i >= win && flip[i - win]) count--;
            if (count > s.worstFlips) { s.worstFlips = count; s.worstAt = i; }
            if (count > MaxFlipsPerSecond) s.jitterSeconds += tr.dt;
        }
        double e = 0;
        for (int i = 1; i < n - 1; i++)
        {
            float a = (tr.x[i + 1] - 2f * tr.x[i] + tr.x[i - 1]) / (tr.dt * tr.dt);
            e += a * a;
        }
        s.accelRms = Mathf.Sqrt((float)(e / (n - 2)));
        return s;
    }

    public sealed class Summary
    {
        public int actors, jittering;
        public int worstFlips;
        public float jitterSeconds, accelMean, accelWorst;
        public string worstWho = "";
        public Trace worstTrace;
        public int worstAt;
        public int deaths;
        public string deathLog = "";

        public void Add(Trace tr)
        {
            var sc = Measure(tr);
            if (sc.frames < 30) return;
            actors++;
            if (tr.died) deaths++;
            if (sc.jitterSeconds > .5f) jittering++;
            jitterSeconds += sc.jitterSeconds;
            accelMean += sc.accelRms;
            if (sc.accelRms > accelWorst) accelWorst = sc.accelRms;
            if (sc.worstFlips > worstFlips) { worstFlips = sc.worstFlips; worstWho = tr.who; worstTrace = tr; worstAt = sc.worstAt; }
        }

        public override string ToString()
        {
            return actors + " bodies, " + jittering + " jitter, worst " + worstFlips + " flips/s (" + worstWho + "), jitter s " + jitterSeconds.ToString("F1") +
                   ", accel rms mean " + (actors > 0 ? accelMean / actors : 0f).ToString("F1") + " worst " + accelWorst.ToString("F1") + ", died " + deaths;
        }
    }

    // ---- the dense board ----------------------------------------------------------

    sealed class Body
    {
        public EnemyJitterTest.Actor actor;
        public Trace trace;
        public bool Alive => actor.go != null;
    }

    public static int Execute()
    {
        fails = 0;
        using var sandbox = new TestHarness.Sandbox();
        SpawnSpace.RegularsAvoid = true;   // this suite exercises the avoidance rules, which are off in play (ordinary enemies overlap; elites alone keep clear)
        try
        {
            EditorSceneLoader.Open("gameS1", OpenSceneMode.Single);
            Boards();
            Fleeing();
            Stacking();
            Rules();
            Allocations();
            EliteBoards();
        }
        finally
        {
            SpawnSpace.RegularsAvoid = false;
            EnemyThreat.ForceShooting = false;
            EnemyThreat.Reset();
            EnemyBrain.PilotsEnabled = true;
            PilotAirspace.Clear();
            SpawnSpace.ClockOverride = null;
            EliteSystem.Clear();
            EliteSystem.PlayerOverride = null;
            EnemyJitterTest.Clear();
            buttonClicks.playerDied = false;
        }
        Debug.Log("[AVOID] failures: " + fails);
        return fails;
    }

    // A dense board: five pilots shoulder to shoulder across the lane, a column of weaving and plain rocks
    // coming down through their bands, two chasers hunting the ship. `jam` squeezes the pilots
    // into half the lane so every one is crowded.
    public static Summary DenseBoard(int world, float dt, int seed, bool jam, out int friendlyKills, bool chasersOnly = false, bool withElite = false, int chaserCount = 2)
    {
        EnemyJitterTest.Fresh();
        FriendlyFire.ResetCounters();
        FriendlyFire.ResetHostileCounters();
        UnityEngine.Random.InitState(seed);
        var bodies = new List<Body>();
        var fighters = EnemyRoster.For(world, EnemyRole.Fighter);
        var bigs = EnemyRoster.For(world, EnemyRole.Big);
        var aliens = EnemyRoster.For(world, EnemyRole.Alien);
        var rocks = EnemyRoster.For(world, EnemyRole.Rock);
        var chasers = EnemyRoster.For(world, EnemyRole.Chaser);
        void Add(EnemyDef def, int variant, float xShift, float yShift, bool weave)
        {
            var a = EnemyJitterTest.Actor.Make(def, variant, xShift, yShift);
            if (weave && a.weaver != null) a.weaver.SetWeave(UnityEngine.Random.Range(.7f, 1.5f));
            bodies.Add(new Body { actor = a, trace = new Trace { who = def.key + "@" + Mathf.RoundToInt(xShift * 10), dt = dt } });
        }
        // The pilots hold columns the way PilotAirspace gives them out (three across the lane; `jam`: five,
        // squeezed shoulder to shoulder -- the stress case); the hazards come down BESIDE the columns, as the
        // spawner places them (never in one), so what crashes is what the pilots and chasers really meet.
        var px = new List<float>();
        int pilotCount = chasersOnly ? 0 : jam ? 5 : 3;
        for (int k = 0; k < pilotCount; k++)
        {
            EnemyDef def = k == 1 && bigs.Count > 0 ? bigs[0] : k == 4 && aliens.Count > 0 ? aliens[0] : fighters[k % fighters.Count];
            float x = jam ? (k - 2) * .6f : (k - 1) * 1.5f;
            px.Add(x);
            Add(def, k % 2, x, k * .25f, false);
        }
        for (int k = 0; k < (chasersOnly ? 0 : 8); k++)
        {
            float x = 0f;
            for (int tries = 0; tries < 30; tries++)
            {
                x = UnityEngine.Random.Range(-1.9f, 1.9f);
                bool free = true;
                for (int q = 0; q < px.Count; q++) free &= Mathf.Abs(x - px[q]) > (jam ? .7f : 1f);
                if (free) break;
            }
            Add(rocks[k % rocks.Count], 0, x, 1.5f + k * 2.6f, k % 2 == 0);
        }
        for (int k = 0; k < chaserCount; k++)
            if (chasers.Count > 0) Add(chasers[0], k, k == 0 ? -.5f : .5f, k * 1.5f, false);
        EliteShip elite = null;
        Trace eliteTrace = null;
        if (withElite)
        {
            EliteCatalog.Reload();
            elite = EliteShip.CreateInPlay(EliteDefOf("interceptor"), new Vector2(0f, -1f));
            elite.AttackCooldown = 99f;
            eliteTrace = new Trace { who = "elite", dt = dt };
        }
        int frames = Mathf.RoundToInt(Seconds / dt);
        float lo = CameraFit.ViewBottom - .5f, hi = CameraFit.ViewTop + .5f;
        Rect view = new Rect(-5f, CameraFit.ViewBottom, 10f, CameraFit.ViewTop - CameraFit.ViewBottom);
        for (int i = 0; i < frames; i++)
        {
            float t = i * dt;
            EnemyJitterTest.clock += dt;
            SpawnSpace.ClockOverride = EnemyJitterTest.clock;
            EnemyJitterTest.ship.position = new Vector3(Mathf.Sin(t * 1.1f) * 1.8f, ShipReach.StartY, 0f);
            for (int k = 0; k < bodies.Count; k++)
            {
                var b = bodies[k];
                if (b.actor.go == null) { if (!b.trace.died) b.trace.diedAt = t; b.trace.died = true; continue; }
                b.actor.Step(dt);
            }
            EliteSystem.Step(dt);
            if (elite != null && elite.State != EliteState.Dead) { eliteTrace.x.Add(elite.Position.x); eliteTrace.y.Add(elite.Position.y); }
            FriendlyFire.TrackPlayfield(dt, view);
            FriendlyFire.StepCrashes(view);
            for (int k = 0; k < bodies.Count; k++)
            {
                var b = bodies[k];
                if (b.actor.go == null) { if (!b.trace.died) b.trace.diedAt = t; b.trace.died = true; continue; }
                if (b.actor.Gone) continue;
                Vector2 p = b.actor.go.transform.position;
                if (p.y < lo || p.y > hi) continue;
                b.trace.x.Add(p.x);
                b.trace.y.Add(p.y);
            }
        }
        var sum = new Summary();
        var sb = new StringBuilder();
        for (int k = 0; k < bodies.Count; k++)
        {
            if (bodies[k].actor.go == null) bodies[k].trace.died = true;
            if (bodies[k].trace.died) sb.Append(' ').Append(bodies[k].trace.who).Append('=').Append(bodies[k].trace.diedAt.ToString("F1"));
            sum.Add(bodies[k].trace);
            bodies[k].actor.Destroy();
        }
        if (eliteTrace != null) { eliteTrace.died = elite == null || elite.State == EliteState.Dead; sum.Add(eliteTrace); var es = Measure(eliteTrace); Log("  elite in the crowd: " + es.worstFlips + " flips/s, accel rms " + es.accelRms.ToString("F1") + (eliteTrace.died ? ", died" : "")); EliteSystem.Clear(); }
        friendlyKills = FriendlyFire.Kills;
        sum.deathLog = sb.ToString();
        return sum;
    }

    static EliteDef EliteDefOf(string brain)
    {
        foreach (var d in EliteCatalog.All) if (d.brain == brain) return d;
        return BaseEliteDefs.For(brain);   // (no world flies the base brains now: the test-only fixture defs)
    }

    static Summary Merge(Summary all, Summary one)
    {
        all.actors += one.actors; all.jittering += one.jittering; all.jitterSeconds += one.jitterSeconds; all.accelMean += one.accelMean;
        all.deaths += one.deaths; all.accelWorst = Mathf.Max(all.accelWorst, one.accelWorst);
        if (one.worstFlips > all.worstFlips) { all.worstFlips = one.worstFlips; all.worstWho = one.worstWho; all.worstTrace = one.worstTrace; all.worstAt = one.worstAt; }
        return all;
    }

    // (PAUSE_TEST_SEED moves every scene's seeds: run the suite a few times with it, the checks are seed-sensitive by nature)
    static int SeedBase
    {
        get
        {
            int n;
            return int.TryParse(Environment.GetEnvironmentVariable("PAUSE_TEST_SEED"), out n) ? n : 0;
        }
    }

    static IEnumerable<int> Worlds()
    {
        for (int w = 0; w <= 6; w++) if (TestHarness.SweepWorld(w)) yield return w;
    }

    sealed class Board
    {
        public Summary dense = new Summary(), chasers = new Summary(), single = new Summary(), crowd = new Summary();
        public int kills, runs;
    }

    static Board Fly(bool soft)
    {
        SpawnSpace.Soft = soft;
        var b = new Board();
        string tag = soft ? "SOFT" : "OLD ";
        foreach (int world in Worlds())
            foreach (float dt in new[] { 1f / 60f, 1f / 30f })
            {
                foreach (bool jam in new[] { false, true })
                {
                    var one = DenseBoard(world, dt, SeedBase + 7000 + world * 31 + (jam ? 5 : 0), jam, out int k);
                    Log(tag + " dense world " + world + " @" + Mathf.RoundToInt(1f / dt) + "fps" + (jam ? " jam" : "") + ": " + one + ", friendly kills " + k + " deaths:" + one.deathLog);
                    Merge(b.dense, one);
                    b.kills += k;
                    b.runs++;
                }
                Merge(b.chasers, DenseBoard(world, dt, SeedBase + 4100 + world, false, out int ck, chasersOnly: true));
                Merge(b.single, DenseBoard(world, dt, SeedBase + 4100 + world, false, out int ok, chasersOnly: true, chaserCount: 1));
                Merge(b.crowd, DenseBoard(world, dt, SeedBase + 5100 + world, false, out int ek, withElite: true));
            }
        Log(tag + " ALL dense: " + b.dense + ", friendly kills " + b.kills + " in " + b.runs + " boards");
        Log(tag + " TWO chasers, nothing else on the board: " + b.chasers);
        Log(tag + " ONE chaser alone (its own pattern, nothing to avoid): " + b.single);
        Log(tag + " dense boards with an elite in them (the elite counted with the rest): " + b.crowd);
        if (b.chasers.worstTrace != null && b.chasers.worstFlips > 8)
        {
            var wt = b.chasers.worstTrace;
            var sb = new StringBuilder();
            for (int i = Mathf.Max(0, b.chasers.worstAt - 24); i <= b.chasers.worstAt && i < wt.x.Count; i++) sb.Append(wt.x[i].ToString("F3")).Append(' ');
            Log(tag + " worst chaser x per frame (" + b.chasers.worstWho + "): " + sb);
        }
        return b;
    }

    // The limits are what a smooth board stays inside (see the numbers in the PR / docs: soft 0-1 jittering
    // bodies, ~1 s of jitter, accel 9; the old rules 9-13 bodies, 10-20 s, accel 30-80).
    public const float MaxJitterSecondsDense = 3f, MaxAccelDense = 14f, MaxAccelPair = 15f;
    public const int MaxJitteringDense = 2;

    static void Boards()
    {
        var old = Fly(false);
        var soft = Fly(true);
        SpawnSpace.Soft = true;
        Check("OLD rules, for the record: two chasers hunting one ship shake it (" + old.chasers.worstFlips + " flips/s worst, " + old.chasers.jitterSeconds.ToString("F1") +
              " s of jitter, accel rms " + (old.chasers.accelMean / Mathf.Max(1, old.chasers.actors)).ToString("F0") + " u/s^2; one chaser alone: " + old.single.worstFlips + " flips/s)",
              old.chasers.worstFlips >= 30 && old.chasers.jitterSeconds >= 8f && old.single.worstFlips <= 8);
        Check("OLD rules, for the record: the dense board shakes (" + old.dense.jittering + " bodies jittering, " + old.dense.jitterSeconds.ToString("F1") + " s, worst " + old.dense.worstFlips + " flips/s)",
              old.dense.jittering > MaxJitteringDense && old.dense.jitterSeconds > MaxJitterSecondsDense);
        Check("SOFT: two chasers on one ship do not shake each other (" + soft.chasers.jittering + " jittering, " + soft.chasers.jitterSeconds.ToString("F1") + " s, accel rms " +
              (soft.chasers.accelMean / Mathf.Max(1, soft.chasers.actors)).ToString("F1") + "; old " + old.chasers.jitterSeconds.ToString("F1") + " s)",
              soft.chasers.jittering <= 1 && soft.chasers.jitterSeconds <= 2f && soft.chasers.accelMean / Mathf.Max(1, soft.chasers.actors) <= MaxAccelPair);
        Check("SOFT: the dense boards stay smooth (" + soft.dense.jittering + " of " + soft.dense.actors + " bodies jittering, " + soft.dense.jitterSeconds.ToString("F1") + " s, accel rms " +
              (soft.dense.accelMean / Mathf.Max(1, soft.dense.actors)).ToString("F1") + "; old " + old.dense.jittering + " bodies, " + old.dense.jitterSeconds.ToString("F1") + " s, accel " +
              (old.dense.accelMean / Mathf.Max(1, old.dense.actors)).ToString("F1") + ")",
              soft.dense.jittering <= MaxJitteringDense && soft.dense.jitterSeconds <= MaxJitterSecondsDense && soft.dense.accelMean / Mathf.Max(1, soft.dense.actors) <= MaxAccelDense);
        Check("SOFT: the board with an elite in it stays smooth too (" + soft.crowd.jittering + " jittering, " + soft.crowd.jitterSeconds.ToString("F1") + " s; old " + old.crowd.jitterSeconds.ToString("F1") + " s)",
              soft.crowd.jittering <= 1 && soft.crowd.jitterSeconds <= 2f);
        Check("less avoiding means more friendly fire, not a massacre: " + soft.kills + " crash kills over " + soft.runs + " dense boards (old " + old.kills + ")",
              soft.kills >= old.kills && soft.kills <= soft.runs * 8);
    }

    // ---- fleeing, stacking, rules, allocations ---------------------------------------------------

    static readonly Rect Playfield = new Rect(-5f, -9f, 10f, 20f);

    static Rect View() => new Rect(-5f, CameraFit.ViewBottom, 10f, CameraFit.ViewTop - CameraFit.ViewBottom);

    // A pilot ordered out (it Climbs away) with a rock falling down its column. The old rule steered it
    // round the rock (it never touched); now a body that is flying away is not held back, so it meets
    // the rock: the crash is friendly fire (both die, unpaid, "FRIENDLY FIRE" word).
    static void Fleeing()
    {
        foreach (bool soft in new[] { false, true })
        {
            SpawnSpace.Soft = soft;
            EnemyJitterTest.Fresh();
            FriendlyFire.ResetCounters();
            FriendlyFire.ResetHostileCounters();
            UnityEngine.Random.InitState(5);
            var fighter = EnemyRoster.For(3, EnemyRole.Fighter)[0];
            var pilot = EnemyJitterTest.Actor.Make(fighter, 0);
            float dt = 1f / 60f;
            int guard = 0;
            while (pilot.brain.Stage != EnemyBrain.PilotStage.Engaging && guard++ < 60 * 12) Step(pilot, null, dt);
            bool engaged = pilot.brain.Stage == EnemyBrain.PilotStage.Engaging;
            var rock = EnemyJitterTest.Actor.Make(EnemyRoster.One(3, EnemyRole.Rock), 0);
            if (rock.weaver != null) rock.weaver.SetWeave(0f);
            Vector2 at = pilot.go.transform.position;
            // (just above it, inside the view: the crash step only counts bodies on screen)
            rock.go.transform.position = new Vector3(at.x, Mathf.Min(at.y + 1.4f, CameraFit.ViewTop - .3f), 0f);
            pilot.brain.Order();
            float worstSide = 0f;
            for (int i = 0; i < 60 * 4 && pilot.go != null; i++)
            {
                Step(pilot, rock, dt);
                if (pilot.go != null) worstSide = Mathf.Max(worstSide, Mathf.Abs(pilot.go.transform.position.x - at.x));
            }
            bool crashed = FriendlyFire.Crashes > 0;
            Log((soft ? "SOFT" : "OLD ") + " fleeing pilot: engaged " + engaged + ", crashes " + FriendlyFire.Crashes + ", kills " + FriendlyFire.Kills +
                ", popups " + FriendlyFire.Popups + ", sideways " + worstSide.ToString("F2"));
            if (soft)
            {
                Check("a pilot flying away (ordered out, Climb) is not held back by a rock in its way: it crashes into it (" + FriendlyFire.Crashes + " crash)", engaged && crashed);
                Check("... friendly fire kills both, unpaid (" + FriendlyFire.Kills + " kills, no score)", FriendlyFire.Kills >= 2 && pilot.go == null && rock.go == null);
                Check("... and says so (the FRIENDLY FIRE word, " + FriendlyFire.Popups + ")", FriendlyFire.Popups >= 1);
            }
            else Check("(the old ladder steered the fleeing pilot round the rock: " + FriendlyFire.Crashes + " crashes)", engaged && !crashed);
            pilot.Destroy();
            rock.Destroy();
        }
        SpawnSpace.Soft = true;
    }

    static void Step(EnemyJitterTest.Actor a, EnemyJitterTest.Actor b, float dt)
    {
        EnemyJitterTest.clock += dt;
        SpawnSpace.ClockOverride = EnemyJitterTest.clock;
        if (a != null && a.go != null) a.Step(dt);
        if (b != null && b.go != null) b.Step(dt);
        EliteSystem.Step(dt);
        FriendlyFire.TrackPlayfield(dt, View());
        FriendlyFire.StepCrashes(View());
    }

    // Bodies stacked on one spot are parted (the drift) -- or die to friendly fire, which is the other way out.
    static void Stacking()
    {
        SpawnSpace.Soft = true;
        float dt = 1f / 60f;
        // two chasers born on the very same point, hunting the same ship, with the crash step OFF: only the soft push can part them
        EnemyJitterTest.Fresh();
        var chaser = EnemyRoster.For(3, EnemyRole.Chaser)[0];
        var a = EnemyJitterTest.Actor.Make(chaser, 0, 0f, 6f);
        var b = EnemyJitterTest.Actor.Make(chaser, 0, 0f, 6f);
        EnemyJitterTest.ship.position = new Vector3(0f, ShipReach.StartY, 0f);
        float startGap = Vector2.Distance(a.go.transform.position, b.go.transform.position);
        float gap = 0f;
        for (int i = 0; i < 60 * 8 && a.go != null && b.go != null; i++)
        {
            EnemyJitterTest.clock += dt;
            SpawnSpace.ClockOverride = EnemyJitterTest.clock;
            a.Step(dt); b.Step(dt);
            gap = Vector2.Distance(a.go.transform.position, b.go.transform.position);
        }
        SpawnFootprint fa, fb;
        a.go.TryGetComponent(out fa); b.go.TryGetComponent(out fb);
        bool apart = !fa.Body.Overlaps(fb.Body);
        Check("two chasers stacked on one spot are parted by the soft push alone within 8 s (gap " + startGap.ToString("F2") + " -> " + gap.ToString("F2") + " u)", apart);
        a.Destroy(); b.Destroy();

        // two pilots on the very same line and station, crash step off: once both are on station, the pair parts
        EnemyJitterTest.Fresh();
        var fighter = EnemyRoster.For(3, EnemyRole.Fighter)[0];
        var p = EnemyJitterTest.Actor.Make(fighter, 0);
        var q = EnemyJitterTest.Actor.Make(fighter, 0);
        SpawnFootprint fp, fq;
        p.go.TryGetComponent(out fp); q.go.TryGetComponent(out fq);
        float together = 0f, maxOffset = 0f;
        float overlapAfter = 0f;
        for (int i = 0; i < 60 * 12 && p.go != null && q.go != null; i++)
        {
            EnemyJitterTest.clock += dt;
            SpawnSpace.ClockOverride = EnemyJitterTest.clock;
            p.Step(dt); q.Step(dt);
            if (p.brain.Stage == EnemyBrain.PilotStage.Engaging && q.brain.Stage == EnemyBrain.PilotStage.Engaging)
            {
                together += dt;
                maxOffset = Mathf.Max(maxOffset, Mathf.Abs(fp.SoftX - fq.SoftX));
                if (together > 1f && fp.Body.Overlaps(fq.Body)) overlapAfter += dt;
                if (i % 15 == 0) Log("pilots stacked, on station " + together.ToString("F2") + " s: dx " + (fp.transform.position.x - fq.transform.position.x).ToString("F2") + " dy " + (fp.transform.position.y - fq.transform.position.y).ToString("F2") + " offsets " + fp.SoftX.ToString("F2") + " / " + fq.SoftX.ToString("F2") + " half " + fp.half.x.ToString("F2") + "," + fp.half.y.ToString("F2"));
            }
        }
        Check("two pilots on one line part by their damped offsets (" + maxOffset.ToString("F2") + " u apart at most; " + together.ToString("F1") + " s together on station), " +
              "and spend under half the time after the first second overlapping (" + (together > 1f ? overlapAfter / (together - 1f) : 1f).ToString("P0") + ")",
              together > 1.8f && maxOffset > .3f && overlapAfter < (together - 1f) * .5f);
        p.Destroy(); q.Destroy();
    }

    // Elites and bosses are untouched: a chaser overlapping an elite is no crash (EliteShip.Collide owns that),
    // and the boss is immune to friendly fire as ever.
    static void Rules()
    {
        Check("friendly fire's crash step still never takes an elite (a crash needs two non-elite hazards)",
              System.IO.File.ReadAllText("Assets/Scripts/Gameplay/Enemies/FriendlyFire.cs").Contains("a.TryGetComponent(out EliteShip _)"));
        Check("... and the boss stays immune (Immune: BossTarget)", System.IO.File.ReadAllText("Assets/Scripts/Gameplay/Enemies/FriendlyFire.cs").Contains("go.TryGetComponent(out BossTarget _)"));
        Check("soft avoidance tunables are low-gain and rate-capped (drift " + SpawnSpace.SoftSpeed + " / " + SpawnSpace.SoftDeepSpeed + " u/s, accel " + SpawnSpace.SoftAccel + ", tau " + SpawnSpace.SoftTau + " s)",
              SpawnSpace.SoftSpeed <= 1.5f && SpawnSpace.SoftDeepSpeed <= 3f && SpawnSpace.SoftAccel <= 8f && SpawnSpace.SoftTau >= .2f && SpawnSpace.SoftMaxOffset <= 1.2f);
    }

    static void Allocations()
    {
        EnemyJitterTest.Fresh();
        SpawnSpace.Soft = true;
        var defs = EnemyRoster.For(3, EnemyRole.Rock);
        var list = new List<EnemyJitterTest.Actor>();
        for (int i = 0; i < 40; i++) list.Add(EnemyJitterTest.Actor.Make(defs[i % defs.Count], 0, (i % 7 - 3) * .4f, i * .5f));
        var chaser = EnemyJitterTest.Actor.Make(EnemyRoster.For(3, EnemyRole.Chaser)[0], 0);
        SpawnFootprint cf; chaser.go.TryGetComponent(out cf);
        float sink = 0;
        long control;
        bool meter = TestHarness.AllocMeterWorks(out control);
        for (int warm = 0; warm < 2; warm++)
        {
            long bytes = TestHarness.AllocatedBytes(() =>
            {
                for (int i = 0; i < 300; i++)
                {
                    sink += SpawnSpace.SoftDrift(cf, 1f / 60f);
                    sink += SpawnSpace.SoftOffset(cf, 1f / 60f);
                    sink += SpawnSpace.Steer(cf, new Vector2(0f, -6f), new Vector2(.01f, -5.9f), .1f, .07f, 1f / 60f, 1.5f, 3f).x;
                }
            });
            if (warm == 1) Check("soft steering allocates nothing per call over 40 crowded bodies (" + bytes + " bytes over 900 calls)", meter && bytes == 0);
        }
        Log("(sink " + sink + ")");
        foreach (var a in list) a.Destroy();
        chaser.Destroy();
    }

    // ---- elites -------------------------------------------------------------------

    static Transform pilot;
    static readonly List<GameObject> rocks = new List<GameObject>();

    // Three elites in a crowd: rocks and a second line of roster rocks fall through them for 14 s.
    public static Summary EliteBoard(float dt, int seed)
    {
        EliteCatalog.Reload();
        EliteSystem.Clear();
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        rocks.Clear();
        RunScore.EndRun(RunScore.RunId);
        PlayerPrefs.SetString("HasDoneTut", "true");
        startMenu.youAreInTutorial = false;
        buttonClicks.playerDied = false;
        score.pauseCounter = 0;
        moveBackGround.speed = .2f;
        collisionDetection.lifeCounter = 0;
        collisionDetection.atomCheck = false;
        collisionDetection.cloakTimer = 0f;
        PlayerInvuln.Reset();
        RunScore.BeginRun(true, true);
        pilot = new GameObject("~Pilot").transform;
        pilot.position = new Vector3(0f, -3.5f, 0f);
        EliteSystem.PlayerOverride = pilot;
        LandingSites.Override = null;
        EliteEvasion.Enabled = true;
        UnityEngine.Random.InitState(seed);
        var names = new[] { "interceptor", "interceptor", "hauler" };
        var elites = new List<EliteShip>();
        var traces = new List<Trace>();
        for (int k = 0; k < names.Length; k++)
        {
            var e = EliteShip.CreateInPlay(EliteDefOf(names[k]), new Vector2(-1.6f + k * 1.6f, -2f + k * .8f));
            e.AttackCooldown = 99f;
            elites.Add(e);
            traces.Add(new Trace { who = names[k] + k, dt = dt });
        }
        var rng = new System.Random(seed);
        int frames = Mathf.RoundToInt(Seconds / dt);
        var rockDef = EnemyRoster.One(3, EnemyRole.Rock);
        for (int i = 0; i < frames; i++)
        {
            pilot.position = new Vector3(Mathf.Sin(i * dt * 1.1f) * 1.7f, -3.5f, 0f);
            if (i % Mathf.RoundToInt(.35f / dt) == 0)
            {
                var go = EnemyFactory.Create(rockDef, new Vector2((float)(rng.NextDouble() * 4.4 - 2.2), EliteSystem.ViewTop + 1.5f), Quaternion.identity);
                ClearTarget.Ensure(go);
                rocks.Add(go);
            }
            float fall = SpawnSpace.ScrollSpeed * dt;
            for (int r = rocks.Count - 1; r >= 0; r--)
            {
                if (rocks[r] == null) { rocks.RemoveAt(r); continue; }
                rocks[r].transform.position += Vector3.down * fall;
                if (rocks[r].transform.position.y < -7f) { Object.DestroyImmediate(rocks[r]); rocks.RemoveAt(r); }
            }
            EliteSystem.Step(dt);
            for (int k = 0; k < elites.Count; k++)
            {
                if (elites[k] == null || elites[k].State == EliteState.Dead) { traces[k].died = true; continue; }
                traces[k].x.Add(elites[k].Position.x);
                traces[k].y.Add(elites[k].Position.y);
            }
        }
        var sum = new Summary();
        foreach (var t in traces) sum.Add(t);
        foreach (var r in rocks) if (r != null) Object.DestroyImmediate(r);
        rocks.Clear();
        EliteSystem.Clear();
        return sum;
    }

    static void EliteBoards()
    {
        var all = new Summary();
        foreach (float dt in new[] { 1f / 60f, 1f / 30f })
            foreach (int seed in new[] { 11, 12, 13 })
            {
                var one = EliteBoard(dt, seed);
                Log("ELITES @" + Mathf.RoundToInt(1f / dt) + "fps seed " + seed + ": " + one);
                Merge(all, one);
            }
        Log("ELITES ALL: " + all);
    }
}
