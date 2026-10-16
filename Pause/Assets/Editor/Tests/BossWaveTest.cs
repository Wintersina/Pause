using System;
using UnityEngine;

// BOSS BULLET WAVES (docs/boss-bullet-waves.md): every wave has a gap the ship can reach, waves leave on the beat grid,
// intensity ramps, and the beat clock stays continuous across the loop. The fairness check is the omniscient flood fill
// (PatternSim.TrackStep): the set of cells a 5 u/s ship could occupy must never become empty while a whole boss song
// plays through the real BossMusicDirector.
public static class BossWaveTest
{
    static int fails;
    static readonly string[] Names = { "Space", "Frost", "Verdant", "Ember", "Tide", "Storm", "Desert" };
    static void Check(string what, bool ok) { Debug.Log("[WAVES] " + (ok ? "PASS " : "FAIL ") + what); if (!ok) fails++; }
    public static void Run() { TestHarness.Exit(Execute()); }

    public static int Execute()
    {
        fails = 0;
        var oldNow = BeatClock.Now;
        try
        {
            GapChains();
            Rhythm();
            Ramp();
            ClockLoopSnap();
            foreach (int w in new[] { 0, 1, 2, 3, 4, 5, 6 }) Song(w);
            foreach (int w in new[] { 0, 4, 6 }) Stress(w);
        }
        finally { BeatClock.Now = oldNow; }
        Debug.Log("[WAVES] failures: " + fails);
        return fails;
    }

    // ---- the generator on its own: a ship at 5 u/s can ALWAYS follow a chain of gaps
    static void GapChains()
    {
        var rng = new System.Random(5);
        bool ok = true; int links = 0;
        for (int trial = 0; trial < 400 && ok; trial++)
        {
            var g = new WaveGaps();
            float lim = 1.95f, px = (float)(rng.NextDouble() * 2 - 1) * lim;
            float lo = px, hi = px, prevArr = 0f;
            const float thick = .2f;
            for (int i = 0; i < 40; i++)
            {
                float gap = (1.7f + (float)rng.NextDouble() * 2.5f) * 2f * PatternSim.ShipRadius;
                // the first wave is 2.5 s out; later ones follow a quarter to two seconds after the last arrived
                float now = i == 0 ? 0f : prevArr;
                float arrive = i == 0 ? 2.5f : prevArr + .25f + (float)rng.NextDouble() * 1.75f;
                float c = g.Choose((lo + hi) * .5f, now, arrive, gap, thick, lim, (float)(rng.NextDouble() * 2 - 1));
                float travel = i == 0 ? arrive - now : arrive - prevArr - thick;
                lo = Mathf.Max(-lim, lo - 5f * Mathf.Max(0f, travel)); hi = Mathf.Min(lim, hi + 5f * Mathf.Max(0f, travel));
                float half = (gap - 2f * PatternSim.ShipRadius) * .5f;      // the ship's centre fits this far either side of the gap's centre
                float nl = Mathf.Max(lo, c - half), nh = Mathf.Min(hi, c + half);
                if (nl > nh + 1e-3f) { ok = false; break; }
                lo = nl; hi = nh; prevArr = arrive; links++;
            }
        }
        Check("gap generator: a 5 u/s ship can follow every gap of 400 random wave chains (" + links + " links)", ok);

        // slow motion / pause: almost no game time between two waves pins the gap in place
        var pin = new WaveGaps();
        float a = pin.Choose(0f, 0f, 1f, 1f, .2f, 1.9f, 1f);
        float b = pin.Choose(0f, .5f, 1.02f, 1f, .2f, 1.9f, 1f);
        Check("waves squeezed together by slow motion keep (almost) the same gap (" + a.ToString("F2") + " -> " + b.ToString("F2") + ")", Mathf.Abs(b - a) <= (1f - WaveGaps.ShipWidth) + .01f);
    }

    static void Rhythm()
    {
        bool ordered = true, grid = true;
        int last = 0;
        foreach (var kind in new[] { "breakdown", "drive", "build", "peak", "drop" })
        {
            int n = 0;
            for (float x = 0f; x <= 1f; x += .05f)
            {
                int m = WaveRhythm.Mask(kind, x);
                grid &= m > 0 && m < (1 << WaveRhythm.TicksPerBar);
                int c = 0; for (int i = 0; i < 16; i++) c += (m >> i) & 1;
                n = Math.Max(n, c);
            }
            last = Math.Max(last, n);
        }
        for (float x = 0; x < 1f; x += .05f)
        {
            int c1 = Bits(WaveRhythm.Mask("drive", x)), c2 = Bits(WaveRhythm.Mask("drive", x + .05f));
            ordered &= c2 >= c1;
        }
        Check("rhythm masks are never empty and only grow with intensity", grid && ordered && last >= 4);
    }

    static int Bits(int m) { int c = 0; for (int i = 0; i < 16; i++) c += (m >> i) & 1; return c; }

    static void Ramp()
    {
        bool gapsNarrow = true, floor = true;
        for (int w = 0; w < 7; w++)
        {
            float prev = 99f;
            for (float x = 0; x <= 1.001f; x += .1f)
            {
                float m = BossWaveVoice.GapMultiple(w, x);
                gapsNarrow &= m <= prev + 1e-4f;
                floor &= m >= WaveGaps.MinMultiple - 1e-4f;
                prev = m;
            }
        }
        Check("gaps tighten with intensity on every boss and never pass the fair floor", gapsNarrow && floor);
        // more waves per second, more bullets, as the song intensifies
        int quiet = Count(4, .1f, "breakdown"), mid = Count(4, .5f, "drive"), high = Count(4, .8f, "build"), peak = Count(4, 1f, "drop");
        Check("Iron Kraken bullets ramp breakdown/drive/build/drop (" + quiet + "/" + mid + "/" + high + "/" + peak + ")", quiet > 0 && mid > quiet && high > mid && peak > high);
    }

    static int Count(int world, float energy, string kind)
    {
        var sim = new PatternSim { cap = 256 };
        var music = new Manual { Moment = new MusicMoment { energy = energy, bass = energy, accent = energy, kind = kind } };
        var d = new BossMusicDirector(world, BossCatalog.ForWorld(world), sim);
        int total = 0;
        for (int f = 0; f < 60 * 30; f++)
        {
            music.BeatNow = f / 60.0 / .4;
            sim.Advance(1f / 60f);
            d.Step(music, 1f / 60f, 1f / 60f, .5f);
            if (sim.live > 200) break;
        }
        total = sim.fired;
        return total;
    }

    sealed class Manual : IMusicBeatSource
    {
        public bool Active { get; set; } = true;
        public double BeatNow { get; set; }
        public double SecondsPerBeat => .4;
        public int BeatsPerBar => 4;
        public int DownbeatBeat => 0;
        public MusicMoment Moment;
        public MusicMoment MusicNow => Moment;
        public MusicMoment MusicAhead(double beats) => Moment;
    }

    // ---- regression: a snap right at the loop point lost or doubled a whole loop of beats
    static void ClockLoopSnap()
    {
        var map = BeatMaps.For("Frost");
        double dsp = 0;
        BeatClock.Now = () => dsp;
        double worst = 0; bool monotonic = true;
        foreach (double offset in new[] { .12, -.12, .3, -.3 })
        {
            dsp = 0;
            var c = new BeatClock(map) { UseLatencyOverride = true };
            c.Sample(0, 0, true);
            double lastBeat = -1e9;
            double d = map.duration;
            for (int f = 0; dsp < d * 2.2; f++)
            {
                dsp += 1.0 / 60.0;
                double truth = dsp;
                double reported = truth;
                // the source disagrees by `offset` for a few frames round each loop point
                double toWrap = Math.Abs(truth - Math.Round(truth / d) * d);
                if (toWrap < .05 && Math.Round(truth / d) >= 1) reported = truth + offset;
                reported = (reported % d + d) % d;
                c.Sample(dsp, reported, true);
                // the unwrapped music time must follow the truth to within the disagreement; a lost or doubled loop is ~d off
                double err = Math.Abs(c.MusicTime(dsp) - truth);
                worst = Math.Max(worst, err);
                double beat = c.BeatNow;
                if (beat < lastBeat - c.map.BeatsPerLoop * .5) monotonic = false;
                lastBeat = beat;
            }
        }
        Check("beat clock keeps its loop count through a snap at the loop point (worst error " + worst.ToString("F2") + " s)", worst < .45 && monotonic);
    }

    // ---- a whole song through the director, flood-filled every frame
    sealed class Stats
    {
        public int waves, emptyFrames, gridFails, gapFails, travelFails, minReach = int.MaxValue;
        public double maxLate;
        public int peak, dropped;
        public long allocBytes;
        public double ms;
        public int steps;
    }

    static Stats Play(int world, Func<int, float> realDt, Func<double, float> gameScale, double seconds, double startClip, Func<IMusicBeatSource> make = null)
    {
        var map = BeatMaps.For(Names[world]);
        double dsp = 0;
        BeatClock.Now = () => dsp;
        var clock = new BeatClock(map) { UseLatencyOverride = true };
        clock.Sample(0, startClip, true);
        var sim = new PatternSim { cap = BossConfig.ProjectilePoolMax };
        sim.player = new Vector2(0f, -3.2f);
        var director = new BossMusicDirector(world, BossCatalog.ForWorld(world), sim);
        var st = new Stats();
        sim.TrackBegin(sim.player);
        int seenWaves = 0;
        double clipT = startClip;
        float need = WaveGaps.MinMultiple * 2f * PatternSim.ShipRadius - 1e-3f;
        var sw = new System.Diagnostics.Stopwatch();
        for (int f = 0; dsp < seconds; f++)
        {
            float rdt = realDt(f);
            dsp += rdt; clipT += rdt;
            clock.Sample(dsp, clipT % map.duration, true);
            float dt = rdt * gameScale(dsp);
            sim.Advance(dt);
            long a0 = GC.GetAllocatedBytesForCurrentThread();
            sw.Restart();
            director.Step(clock, dt, rdt, .8f);
            sw.Stop();
            st.allocBytes += GC.GetAllocatedBytesForCurrentThread() - a0;
            st.ms += sw.Elapsed.TotalMilliseconds; st.steps++;
            if (director.Waves.Waves != seenWaves && director.Waves.LastBullets > 0)
            {
                seenWaves = director.Waves.Waves;
                st.waves++;
                double g4 = director.LastScheduledBeat * 4.0;
                if (Math.Abs(g4 - Math.Round(g4)) > 1e-6) st.gridFails++;
                st.maxLate = Math.Max(st.maxLate, director.LastLateBeats * clock.SecondsPerBeat);
                if (director.Waves.LastClearWidth < need) st.gapFails++;
                double tb = (director.Waves.LastArrival - director.Waves.LastSpawnGame) / clock.SecondsPerBeat;
                if (director.Waves.LastTravelBeats < 2 || Math.Abs(tb - director.Waves.LastTravelBeats) > .25) st.travelFails++;
            }
            if (dt > 0f)
            {
                int n = sim.TrackStep(dt, 5f, .04f);
                if (dsp > 8) { st.minReach = Math.Min(st.minReach, n); if (n == 0) { st.emptyFrames++; Debug.Log("[WAVES] dead end: " + Names[world] + " t=" + dsp.ToString("F2") + " accents=" + director.AccentEmissions + " lastWave=" + director.Waves.LastKind + " arr=" + director.Waves.LastArrival.ToString("F2") + " spawn=" + director.Waves.LastSpawnGame.ToString("F2") + " gap=" + director.Waves.LastGapCenter.ToString("F2") + " w=" + director.Waves.LastClearWidth.ToString("F2") + " live=" + sim.live + " player=" + sim.player.x.ToString("F2") + " reachR=" + director.Waves.gaps.ReachLo.ToString("F2") + ".." + director.Waves.gaps.ReachHi.ToString("F2")); sim.TrackBegin(new Vector2(0f, -3.2f)); } }
                if (n > 0) sim.player.x = Mathf.Clamp(sim.ReachCentroid(), -1.6f, 1.6f);
            }
            st.peak = Math.Max(st.peak, sim.live);
        }
        st.dropped = sim.dropped;
        return st;
    }

    static void Song(int world)
    {
        // the whole real song plus a loop seam, at an uneven 30/60 fps
        var st = Play(world, f => f % 3 == 0 ? 1f / 30f : 1f / 60f, t => 1f, BeatMaps.For(Names[world]).duration + 12, 0);
        string n = Names[world];
        Check(n + ": " + st.waves + " waves, never a moment with no reachable cell (min reach " + st.minReach + " cells, empty frames " + st.emptyFrames + ")", st.waves > 30 && st.emptyFrames == 0);
        Check(n + ": every wave leaves the boss on a sixteenth (" + st.gridFails + " off-grid), within a frame of the beat (late " + (st.maxLate * 1000).ToString("F0") + " ms)", st.gridFails == 0 && st.maxLate <= 1.0 / 30 + .001);
        Check(n + ": every gap is at least " + WaveGaps.MinMultiple + " ship widths clear and every wave lands a whole number of beats later", st.gapFails == 0 && st.travelFails == 0);
        Check(n + ": peak " + st.peak + " live shots, dropped " + st.dropped + "; director " + (st.ms / Math.Max(1, st.steps) * 1000).ToString("F1") + " us/frame, " + (st.allocBytes / Math.Max(1, st.steps)) + " B/frame", st.dropped == 0 && st.peak <= BossConfig.ProjectilePoolMax);
    }

    // pauses and slow motion in the middle of a song must not squeeze a gap shut
    static void Stress(int world)
    {
        var st = Play(world, f => f % 5 == 0 ? 1f / 30f : 1f / 60f, t => { double c = t % 20; return c >= 10 && c < 12 ? 0f : c > 15 ? .25f : 1f; }, 90, 30);
        Check(Names[world] + " stays survivable through pauses and slow motion (empty frames " + st.emptyFrames + ", min reach " + st.minReach + ")", st.emptyFrames == 0);
    }
}
