using System;
using System.Collections.Generic;
using UnityEngine;

// BOSS BEAT SYNC (BeatMap / BeatClock / BossPatternRunner beat mode):
//   1  maps       Resources/BossBeatMaps has all seven bosses, sane tempos, lengths matching the clips, listed maps rising
//   2  map math   time <-> beat both ways, steady and listed (drifting) maps
//   3  clock      follows a jittery audio position: dsp-extrapolated, loop wrap monotonic and phase-true, calibration
//                 offset, not started / paused / stale = inactive
//   4  waves      the real pattern runner fires on the beat grid (every beat, every other beat, off-beats), starts on
//                 bar lines, within one frame of the grid line; knobs scale pace and speed; no allocation per step
//   5  fallback   no clock / inactive clock / music stopping mid-pattern = the old cooldown timers; a stall never bursts
//
//   Unity -batchmode -quit -projectPath Pause -executeMethod BeatSyncTest.Run
public static class BeatSyncTest
{
    static int fails;
    static void Check(string what, bool ok)
    {
        Debug.Log((ok ? "[BEAT] PASS  " : "[BEAT] FAIL  ") + what);
        if (!ok) fails++;
    }
    static bool Near(double a, double b, double tol) { return Math.Abs(a - b) <= tol; }

    public static void Run() { TestHarness.Exit(Execute()); }

    static readonly string[] Names = { "Space", "Frost", "Verdant", "Ember", "Tide", "Storm", "Desert" };

    public static int Execute()
    {
        fails = 0;
        var oldNow = BeatClock.Now;
        float oldLatency = BeatCalibration.LatencySeconds;
        try
        {
            Maps();
            MapMath();
            Clock();
            Waves();
            Fallback();
            ResumeTiming();
            ActorTiming();
            SongDensity();
        }
        finally
        {
            BeatClock.Now = oldNow;
            BeatCalibration.LatencySeconds = oldLatency;
            BossBeat.Enabled = true;
            BossBeat.Detach();
        }
        Debug.Log("[BEAT] failures: " + fails);
        return fails;
    }

    // ---- 1 ----
    static void Maps()
    {
        Check("seven boss maps loaded", BeatMaps.Count == 7);
        foreach (var n in Names)
        {
            var m = BeatMaps.For(n);
            Check(n + ": has a map", m != null);
            if (m == null) continue;
            var clip = Resources.Load<AudioClip>("WorldMusic/Boss_" + n);
            Check(n + ": clip " + m.clip + " exists", clip != null && m.clip == "WorldMusic/Boss_" + n);
            Check(n + ": tempo " + m.bpm + " in 100..200", m.bpm > 100f && m.bpm < 200f);
            Check(n + ": first beat inside the first beat", m.firstBeat >= 0f && m.firstBeat < 60f / m.bpm + .01f);
            if (clip != null) Check(n + ": duration matches the clip (" + m.duration + " vs " + clip.length + ")", Near(m.duration, clip.length, .1));
            Check(n + ": 4 beats a bar", m.beatsPerBar == 4);
            // the fight starts the track on a bar line and reaches its first drop / peak soon after that start
            Check(n + ": boss start beat " + m.bossStartBeat + " is a bar line", (m.bossStartBeat - m.downbeatBeat) % m.beatsPerBar == 0);
            double startT = m.StartSeconds, dropT = -1;
            if (m.sections != null)
                foreach (var sec in m.sections)
                    if ((sec.kind == "drop" || sec.kind == "peak") && sec.beat >= m.bossStartBeat) { dropT = m.TimeOfBeat(sec.beat) - startT; break; }
            Check(n + ": first drop/peak " + dropT.ToString("0.0") + " s after the fight's music starts (<= 15 s Verdant, <= 30 s all)",
                dropT >= 0 && dropT <= (n == "Verdant" ? 15.0 : 30.0));
            if (!m.steady)
            {
                bool rising = m.beats != null && m.beats.Length > 100;
                for (int i = 1; rising && i < m.beats.Length; i++)
                {
                    float gap = m.beats[i] - m.beats[i - 1];
                    if (gap < .3f || gap > .5f) rising = false;
                }
                Check(n + ": listed beats rise at 120..200 bpm", rising);
            }
        }
    }

    static void ResumeTiming()
    {
        var map = Steady(150f, 0f, 100f);
        // Resume between beats after less than one beat: never fire an overdue volley.
        var r = new Rig(map, 4.0);
        r.Tick(0);
        r.runner.Begin(Single(Aimed(.4f, 1f, 6f)), r.host, 1f, 1, .9f, r.clock);
        r.runner.Step(1f / 60f);
        int before = r.host.volleys;
        r.Tick(.6);
        r.runner.Step(1f / 60f);
        Check("resume between beats: no overdue volley", r.host.volleys == before);
        r.Tick(.20001);
        r.runner.Step(1f / 60f);
        Check("resume: next volley retains the original beat phase", r.host.volleys == before + 1 && Near(r.runner.LastScheduledBeat, 12, 1e-6));

        // A delayed stream has not emitted yet when the player lifts their finger.
        var d = new Rig(map, 4.0);
        d.Tick(0);
        d.runner.Begin(Single(Aimed(.1f, .25f, 6f, start: .4f)), d.host, 1f, 1, .9f, d.clock);
        d.runner.Step(1f / 60f);
        d.Tick(2.05);
        d.runner.Step(1f / 60f);
        Check("resume before a delayed stream's first volley: no catch-up burst", d.host.volleys == 0);

        foreach (var name in Names)
        {
            var m = BeatMaps.For(name);
            var c = new BeatClock(m) { UseLatencyOverride = true };
            c.Sample(0, 0, true);
            Check(name + ": beat position does not go backwards at the actual loop seam",
                c.BeatAtDsp(m.duration + .00001) >= c.BeatAtDsp(m.duration - .00001));
        }
    }

    sealed class ManualBeat : IBeatSource
    {
        public bool Active => true;
        public double BeatNow { get; set; }
        public double SecondsPerBeat => .4;
        public int BeatsPerBar => 4;
        public int DownbeatBeat => 0;
    }

    static void ActorTiming()
    {
        using var sandbox = new TestHarness.Sandbox();
        var step = typeof(BossActor).GetMethod("StepAttacks", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var targetField = typeof(BossActor).GetField("alignBeat", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        foreach (float hz in new[] { 30f, 60f, 120f })
        foreach (bool pauseInTell in new[] { false, true })
        {
            var boss = BossCatalog.ForWorld(0);
            var actor = BossActor.Spawn(boss);
            var pool = new BossProjectilePool(144, 12);
            var music = new ManualBeat { BeatNow = 10.13 };
            float dt = 1f / hz;
            try
            {
                actor.BeginFight();
                // Enter the real actor's first tell, then advance the audio independently.
                step.Invoke(actor, new object[] { 10f, dt, Vector3.zero, 0f, pool, music });
                double target = (double)targetField.GetValue(actor);
                double chosen = target;
                if (pauseInTell) music.BeatNow += 9.37;
                bool sameFrame = false;
                for (int f = 0; f < hz * 5; f++)
                {
                    music.BeatNow += dt / music.SecondsPerBeat;
                    step.Invoke(actor, new object[] { dt, dt, Vector3.zero, 0f, pool, music });
                    if (actor.PatternPhase)
                    {
                        sameFrame = actor.Runner.Emissions > 0;
                        break;
                    }
                    double pending = (double)targetField.GetValue(actor);
                    if (pending >= 0) chosen = pending;
                }
                string tag = hz + " Hz actor, pause in tell=" + pauseInTell + ": ";
                Check(tag + "first volley fires on the transition frame", sameFrame);
                Check(tag + "first volley uses the chosen beat", Near(actor.Runner.LastScheduledBeat, chosen, 1e-6));
                Check(tag + "first volley is at most one frame late",
                    actor.Runner.LastEmitLateBeats >= 0 && actor.Runner.LastEmitLateBeats * .4 <= dt + 1e-6);
                if (pauseInTell) Check(tag + "expired target replaced by a current beat", chosen > target);
            }
            finally
            {
                pool.Dispose();
                UnityEngine.Object.DestroyImmediate(actor.gameObject);
            }
        }
        // A tell ending just beyond a beat cannot select the already-passed beat.
        var b = new ManualBeat { BeatNow = 10.01 };
        Check("align: never selects a beat before the tell ends", BeatAlign.Target(b, .8, 0) >= 12.01);
    }

    static void SongDensity()
    {
        for (int world = 0; world < Names.Length; world++)
        {
            var before = BeatRotation(world, new BeatTuning());
            var after = BeatRotation(world, BossBeatTuning.For(world));
            Check(Names[world] + ": song-synced density increases (" + before.fired + " -> " + after.fired + " shots)", after.fired > before.fired);
            foreach (float x in new[] { -1.8f, 1.8f })
            {
                var edge = BeatRotation(world, BossBeatTuning.For(world), x);
                after.maxLive = Math.Max(after.maxLive, edge.maxLive);
                after.dropped += edge.dropped;
            }
            Check(Names[world] + ": song-synced pool is prewarmed", BossLadder.WarmShots(world) >= after.maxLive);
            Check(Names[world] + ": song-synced peak " + after.maxLive + " fits pool " + BossConfig.ProjectilePoolMax,
                after.maxLive <= BossConfig.ProjectilePoolMax - 8 && after.dropped == 0);
        }
    }

    static PatternSim BeatRotation(int world, BeatTuning tuning, float playerX = 0f)
    {
        var map = BeatMaps.For(Names[world]);
        // Measure unconstrained demand, so exceeding the real pool is visible.
        var sim = new PatternSim { cap = 1024, shots = new PatternSim.Shot[1024], player = new Vector2(playerX, -3.2f) };
        var run = new BossPatternRunner();
        var clock = new BeatClock(map) { UseLatencyOverride = true };
        double dsp = 0;
        BeatClock.Now = () => dsp;
        const float dt = 1f / 60f;
        Action advance = () =>
        {
            dsp += dt;
            clock.Sample(dsp, dsp % map.duration, true);
            sim.Advance(dt);
        };
        clock.Sample(0, 0, true);
        var slots = BossPatternTables.For(world);
        for (int round = 0; round < 2; round++)
        foreach (var slot in slots)
        {
            if (slot.pattern != null)
            {
                double target = BeatAlign.Target(clock, slot.tell, tuning.barWaitMax);
                while (clock.BeatNow < target) advance();
                run.Begin(slot.pattern, sim, BossLadder.RateAt(world, 1f), 7, slot.tell, clock, tuning);
                run.StartOnBeat(target);
                while (!run.Step(dt)) advance();
                advance();
            }
            else for (float t = 0; t < slot.tell + slot.est; t += dt) advance();
            for (float t = 0; t < slot.cooldown * BossLadder.CooldownFor(world) * BossLadder.FinalCooldownScale; t += dt) advance();
        }
        return sim;
    }

    // ---- 2 ----
    static BeatMap Steady(float bpm, float first, float duration)
    {
        return new BeatMap { name = "T", bpm = bpm, firstBeat = first, beatsPerBar = 4, duration = duration, steady = true };
    }

    static void MapMath()
    {
        var m = Steady(120f, .25f, 20f);   // beat every .5 s
        Check("steady: first beat is beat 0", Near(m.BeatAt(.25), 0, 1e-9));
        Check("steady: 1 s later is 2 beats on", Near(m.BeatAt(1.25), 2, 1e-9));
        Check("steady: off-beat is x.5", Near(m.BeatAt(.5), .5, 1e-9));
        Check("steady: time of beat 8 is 4.25 s", Near(m.TimeOfBeat(8), 4.25, 1e-9));
        Check("steady: before the first beat is negative", m.BeatAt(0) < 0);
        Check("steady: loop is whole bars (" + m.BeatsPerLoop + ")", m.BeatsPerLoop % 4 == 0 && m.BeatsPerLoop >= m.BeatAt(m.duration));

        // a drifting map: beats .5 s apart, then .4 s apart
        var lst = new BeatMap { name = "L", steady = false, beatsPerBar = 4, duration = 10f, bpm = 120f, beats = new float[] { 0f, .5f, 1f, 1.5f, 1.9f, 2.3f, 2.7f } };
        Check("listed: beat 2 at 1.0", Near(lst.BeatAt(1.0), 2, 1e-6));
        Check("listed: mid-span 1.7 is beat 3.5", Near(lst.BeatAt(1.7), 3.5, 1e-6));
        Check("listed: time of beat 3.5 is 1.7", Near(lst.TimeOfBeat(3.5), 1.7, 1e-6));
        Check("listed: local tempo follows the span", Near(lst.SecondsPerBeatAt(.7), .5, 1e-6) && Near(lst.SecondsPerBeatAt(2.0), .4, 1e-6));
        bool inv = true;
        for (double t = .05; t < 2.6; t += .137) if (!Near(lst.TimeOfBeat(lst.BeatAt(t)), t, 1e-6)) inv = false;
        Check("listed: TimeOfBeat inverts BeatAt", inv);
    }

    // ---- 3 ----
    // A fake audio source: the clip position it reports lags the true one by up to a buffer (23 ms steps).
    static double Reported(double dsp, double dsp0, double clip0, double dur, double buffer)
    {
        double t = clip0 + (dsp - dsp0);
        t = Math.Floor(t / buffer) * buffer;
        t %= dur;
        return t;
    }

    static void Clock()
    {
        double dsp = 1000.0;
        BeatClock.Now = () => dsp;
        BeatCalibration.LatencySeconds = 0f;

        var m = Steady(150f, .2f, 10f);   // beat every .4 s; the loop is 24.5 beats -> 28 beats counted (7 bars)
        var c = new BeatClock(m);
        Check("clock: inactive before any sample (music not started)", !c.Active);
        c.Sample(dsp, 0, false);
        Check("clock: inactive while the source is not playing", !c.Active);

        double dsp0 = dsp, clip0 = 3.0;
        double worst = 0;
        double lastBeat = double.MinValue;
        bool monotonic = true;
        int wraps = 0;
        double lastClip = -1;
        for (int f = 0; f < 60 * 45; f++)   // 45 s of 60 fps: four loops
        {
            dsp = dsp0 + f / 60.0;
            double rep = Reported(dsp, dsp0, clip0, m.duration, .0232);
            c.Sample(dsp, rep, true);
            double trueClip = (clip0 + (dsp - dsp0)) % m.duration;
            double beat = c.BeatNow;
            // (a frame within a hair of the loop point: the lagging report and the truth disagree about the wrap itself)
            if (f > 30 && trueClip > .06 && trueClip < m.duration - .06)
            {
                // truth: beat position of the true clip time, plus the whole bars of the loops done so far
                long loop = (long)Math.Floor((clip0 + (dsp - dsp0)) / m.duration);
                double truth = loop * m.BeatsPerLoop + m.BeatAt(trueClip);
                worst = Math.Max(worst, Math.Abs(beat - truth) * .4);
            }
            if (beat < lastBeat) monotonic = false;
            lastBeat = beat;
            if (trueClip < lastClip) wraps++;
            lastClip = trueClip;
        }
        Check("clock: " + wraps + " loop wraps crossed", wraps == 4);
        Check("clock: stays within one 23 ms audio buffer of the true position (a source that reports late by up to a buffer is the offset BeatCalibration is for) (worst " + (worst * 1000).ToString("0.0") + " ms)", worst < .0232);
        Check("clock: beat counter never runs backwards across loop wraps", monotonic);
        Check("clock: bar phase carries over the loop (28 beats a loop = whole bars)", m.BeatsPerLoop % 4 == 0);
        Check("clock: active while sampled", c.Active);
        dsp += 1.0;
        Check("clock: stale (no samples for a second) = inactive", !c.Active);

        // Unity's imported MP3 has padding beyond ffmpeg's decoded length.
        var padded = new BeatClock(m, m.duration + .06) { UseLatencyOverride = true };
        padded.Sample(0, 0, true);
        Check("clock: imported clip padding does not wrap at the baked duration",
            Near(padded.BeatAtDsp(m.duration + .03), m.BeatAt(m.duration + .03), 1e-6));
        double paddedLoop = m.duration + .06;
        Check("clock: imported clip wraps at the source's actual length",
            Near(padded.BeatAtDsp(paddedLoop + .01), m.BeatsInLoop(paddedLoop) + m.BeatAt(.01), 1e-6));

        // calibration: latency L makes the game treat the music as L earlier
        var c2 = new BeatClock(m);
        dsp = 2000.0;
        c2.Sample(dsp, 2.0, true);
        BeatCalibration.LatencySeconds = 0f;
        double b0 = c2.BeatNow;
        BeatCalibration.LatencySeconds = .08f;
        double b1 = c2.BeatNow;
        Check("calibration: +80 ms latency = .2 beats earlier at 150 bpm (" + (b0 - b1).ToString("0.0000") + ")", Near(b0 - b1, .2, 1e-4));
        BeatCalibration.LatencySeconds = 0f;
        Check("calibration: default is 0", Near(BeatCalibration.LatencySeconds, 0, 1e-9));
        Check("calibration: clip 2.0 s with first beat .2 s = beat 4.5", Near(b0, 4.5, 1e-6));

        // pause and resume: inactive while paused, re-anchors on resume at the right place
        var c3 = new BeatClock(m);
        dsp = 3000.0;
        c3.Sample(dsp, 1.0, true);
        dsp += .5; c3.Sample(dsp, 1.5, true);
        dsp += .1; c3.Sample(dsp, 1.5, false);
        Check("pause: inactive", !c3.Active);
        dsp += 30.0; c3.Sample(dsp, 1.5, true);
        Check("resume: active again, at the paused spot (beat " + c3.BeatNow.ToString("0.00") + ")", c3.Active && Near(c3.BeatNow, m.BeatAt(1.5), 1e-6));

        // pitch
        var c4 = new BeatClock(m) { Pitch = 1.5 };
        dsp = 4000.0;
        c4.Sample(dsp, 0, true);
        dsp += 1.0;
        Check("pitch 1.5: a second plays 1.5 s of music", Near(c4.BeatAtDsp(dsp), m.BeatAt(1.5), 1e-6));
    }

    // ---- 4 ----
    sealed class Host : IPatternHost
    {
        public Vector2 Player => new Vector2(0f, -3.2f);
        public float RailEdge => 2.6f;
        public float ShipHalfWidth => 2.25f;
        public float SpawnTop => 5.4f;
        public float ZoneBottom => -3.9f;
        public float ZoneTop => -.7f;
        public Vector2 Emitter(int part) => new Vector2(part * .3f, 3.4f);
        public void Fire(Vector2 at, Vector2 velocity, ref PShot shot) { fires++; lastSpeed = velocity.magnitude; }
        public void Emitted() { volleys++; if (onVolley != null) onVolley(); }
        public void Mark(Vector2 a, Vector2 b, float halfWidth, float seconds) { }
        public int fires, volleys;
        public float lastSpeed;
        public Action onVolley;
    }

    static BossPattern Single(PatternStream s) { return new BossPattern { name = "t", streams = new[] { s } }; }

    static PatternStream Aimed(float interval, float beats, float duration, float speed = 4f, float start = 0f)
    {
        return new PatternStream { kind = StreamKind.Aimed, emitters = new[] { "Chin" }, parts = new[] { 0 }, interval = interval, beats = beats, duration = duration, speed = speed, start = start, count = 1 };
    }

    sealed class Rig
    {
        public BeatMap map;
        public BeatClock clock;
        public double dsp = 5000.0;
        public double dsp0, clip0;
        public readonly Host host = new Host();
        public readonly BossPatternRunner runner = new BossPatternRunner();
        public readonly List<double> beats = new List<double>();     // the beat position each volley was fired at (clock reading)
        public readonly List<double> scheduled = new List<double>();
        public readonly List<double> lateMs = new List<double>();
        public double startBeatPos;

        public Rig(BeatMap m, double clip0Seconds)
        {
            map = m;
            clock = new BeatClock(m);
            clip0 = clip0Seconds;
            dsp0 = dsp;
            BeatClock.Now = () => dsp;
            host.onVolley = () =>
            {
                beats.Add(clock.BeatNow);
                scheduled.Add(runner.LastScheduledBeat);
                lateMs.Add(runner.LastEmitLateBeats * clock.SecondsPerBeat * 1000.0);
            };
        }

        public void Tick(double dt)
        {
            dsp += dt;
            clock.Sample(dsp, (clip0 + dsp - dsp0) % map.duration, true);
        }
    }

    static void Waves()
    {
        var map = Steady(150f, .13f, 100f);   // .4 s a beat, 1.6 s a bar
        var tune = new BeatTuning();

        foreach (double hz in new[] { 60.0, 240.0 })
        {
            double dt = 1.0 / hz;
            // every beat (interval .32 s = .8 beats rounds to 1), every other beat, off-beats
            foreach (var spec in new[] { (interval: .32f, beats: 0f, step: 1.0), (interval: .5f, beats: 2f, step: 2.0), (interval: .3f, beats: .5f, step: .5) })
            {
                var rig = new Rig(map, 7.3);
                rig.Tick(0);
                rig.runner.Begin(Single(Aimed(spec.interval, spec.beats, 6f)), rig.host, 1f, 1, .9f, rig.clock, tune);
                for (int f = 0; f < hz * 7 && !rig.runner.Step((float)dt); f++) rig.Tick(dt);
                string tag = hz + " Hz, " + spec.step + " beat(s) apart: ";
                Check(tag + "fired " + rig.beats.Count + " volleys", rig.beats.Count >= 6 / (spec.step * .4) - 2);
                bool spaced = true, onGrid = true, late = true;
                double worstLate = 0;
                for (int i = 0; i < rig.scheduled.Count; i++)
                {
                    if (i > 0 && !Near(rig.scheduled[i] - rig.scheduled[i - 1], spec.step, 1e-9)) spaced = false;
                    if (!Near(rig.scheduled[i] / .25, Math.Round(rig.scheduled[i] / .25), 1e-6)) onGrid = false;
                    worstLate = Math.Max(worstLate, rig.lateMs[i]);
                    if (rig.lateMs[i] < -.5 || rig.lateMs[i] > dt * 1000 + 1.0) late = false;
                }
                Check(tag + "volleys exactly " + spec.step + " beat(s) apart on the 16th grid", spaced && onGrid && rig.scheduled.Count > 3);
                Check(tag + "each lands within one frame after the grid line, never before (worst " + worstLate.ToString("0.0") + " ms, frame " + (dt * 1000).ToString("0.0") + " ms)", late);
            }
        }

        // a pattern that begins on a bar line: first volley on that line
        {
            var rig = new Rig(map, 7.3);
            rig.Tick(0);
            var src = rig.clock;
            double target = BeatAlign.Target(src, .9, 1.2f);
            Check("align: target is a bar line (beat " + target + ")", Near(((target - map.downbeatBeat) % 4 + 4) % 4, 0, 1e-9) || Near(target, Math.Ceiling(src.BeatNow + .9 / .4 - .08), 1e-9));
            Check("align: never earlier than the telegraph", target >= src.BeatNow + .9 / .4 - .09);
            double wait = (target - (src.BeatNow + .9 / .4)) * .4;
            Check("align: waits at most " + 1.2 + " s past the telegraph for a bar (" + wait.ToString("0.00") + ")", wait <= 1.2 + 1e-9);
            Check("align: tiny max wait falls back to the next beat", Near(BeatAlign.Target(src, .9, 0f) % 1, 0, 1e-9));
        }

        // knobs
        {
            double dt = 1.0 / 60;
            int plain = Volleys(map, new BeatTuning(), 6f, dt, out float speedPlain);
            int slow = Volleys(map, new BeatTuning { beatsScale = 2f }, 6f, dt, out float _);
            Check("knob: beatsScale 2 halves the waves (" + plain + " -> " + slow + ")", slow >= plain / 2 - 1 && slow <= plain / 2 + 1 && slow < plain);
            Volleys(map, new BeatTuning { speedScale = 1.25f }, 6f, dt, out float speedFast);
            Check("knob: speedScale 1.25 scales bullet speed (" + speedPlain + " -> " + speedFast + ")", Near(speedPlain, 4f, 1e-4) && Near(speedFast, 5f, 1e-4));
        }

        // a real boss pattern: Space "chin cannon", 150 bpm
        {
            var slot = BossPatternTables.For(0)[0];
            var rig = new Rig(map, 7.3);
            rig.Tick(0);
            rig.runner.Begin(slot.pattern, rig.host, 1f, 3, slot.tell, rig.clock, new BeatTuning());
            for (int f = 0; f < 60 * 4 && !rig.runner.Step(1f / 60f); f++) rig.Tick(1.0 / 60);
            Check("Space chin cannon: .32 s interval becomes one volley a beat on 150 bpm (" + rig.beats.Count + " volleys)", rig.beats.Count >= 2 && Near(rig.scheduled[1] - rig.scheduled[0], 1.0, 1e-9));
        }

        // no allocation per step
        {
            var rig = new Rig(map, 7.3);
            rig.Tick(0);
            rig.host.onVolley = null;
            rig.runner.Begin(Single(Aimed(.3f, 0f, 30f)), rig.host, 1f, 1, .9f, rig.clock, tune);
            for (int f = 0; f < 120; f++) { rig.Tick(1.0 / 60); rig.runner.Step(1f / 60f); }
            long used = TestHarness.AllocatedBytes(() => { for (int f = 0; f < 600; f++) { rig.Tick(1.0 / 60); rig.runner.Step(1f / 60f); } });
            Check("beat-synced steps allocate nothing (" + used + " bytes over 600 steps)", used == 0 || used == -1);
        }
    }

    static int Volleys(BeatMap map, BeatTuning tune, float seconds, double dt, out float speed)
    {
        var rig = new Rig(map, 7.3);
        rig.Tick(0);
        rig.runner.Begin(Single(Aimed(.32f, 0f, seconds)), rig.host, 1f, 1, .9f, rig.clock, tune);
        for (int f = 0; f < 60 * 8 && !rig.runner.Step((float)dt); f++) rig.Tick(dt);
        speed = rig.host.lastSpeed;
        return rig.beats.Count;
    }

    // ---- 5 ----
    static void Fallback()
    {
        var map = Steady(150f, .13f, 100f);
        double dt = 1.0 / 60;

        // no clock: the old timers (interval .32 s over 4 s: 13 volleys)
        var host = new Host();
        var run = new BossPatternRunner();
        run.Begin(Single(Aimed(.32f, 0f, 4f)), host, 1f, 1, .9f);
        for (int f = 0; f < 60 * 6 && !run.Step((float)dt); f++) { }
        Check("fallback: no beat source = the cooldown timers (" + host.volleys + " volleys, not synced)", host.volleys == 13 && !run.BeatSynced);

        // a clock that is not active (music not started)
        var rig = new Rig(map, 0);   // never sampled
        var host2 = new Host();
        var run2 = new BossPatternRunner();
        run2.Begin(Single(Aimed(.32f, 0f, 4f)), host2, 1f, 1, .9f, rig.clock, new BeatTuning());
        for (int f = 0; f < 60 * 6 && !run2.Step((float)dt); f++) { }
        Check("fallback: inactive clock = identical timer run (" + host2.volleys + " volleys)", host2.volleys == 13 && !run2.BeatSynced);

        // BossBeat: nothing attached / disabled
        BossBeat.Detach();
        Check("fallback: no boss track attached = no beat source", BossBeat.Source == null);
        BossBeat.Enabled = false;
        Check("fallback: master switch off = no beat source", BossBeat.Source == null);
        BossBeat.Enabled = true;

        // the music stops mid-pattern: the stream goes back to its timer and keeps firing
        var r3 = new Rig(map, 7.3);
        r3.Tick(0);
        r3.runner.Begin(Single(Aimed(.32f, 0f, 6f)), r3.host, 1f, 1, .9f, r3.clock, new BeatTuning());
        int f3 = 0;
        for (; f3 < 120; f3++) { r3.Tick(dt); r3.runner.Step((float)dt); }
        int before = r3.host.volleys;
        r3.clock.Halt();
        r3.dsp += dt;
        for (int f = 0; f < 120; f++) { r3.dsp += dt; r3.runner.Step((float)dt); }
        Check("fallback: music stops mid-pattern, volleys carry on by timer (" + before + " -> " + r3.host.volleys + ")", r3.host.volleys >= before + 5);

        // a stall (game frozen while the music runs 3 beats): no burst of catch-up volleys
        var r4 = new Rig(map, 7.3);
        r4.Tick(0);
        r4.runner.Begin(Single(Aimed(.2f, 0f, 20f)), r4.host, 1f, 1, .9f, r4.clock, new BeatTuning());
        for (int f = 0; f < 60; f++) { r4.Tick(dt); r4.runner.Step((float)dt); }
        int pre = r4.host.volleys;
        r4.Tick(2.4);   // 6 beats of music, no game frames
        r4.runner.Step((float)dt);
        Check("stall: 6 beats lost = at most one volley on return (" + (r4.host.volleys - pre) + ")", r4.host.volleys - pre <= 1);
    }
}
