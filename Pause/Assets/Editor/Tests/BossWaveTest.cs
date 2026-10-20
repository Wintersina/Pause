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
            Sources();
            foreach (int w in new[] { 0, 1, 2, 3, 4, 5, 6 }) Song(w, false);
            foreach (int w in new[] { 0, 1, 2, 3, 4, 5, 6 }) Song(w, true);
            foreach (int w in new[] { 0, 4, 6 }) Stress(w);
            // RICOCHETS (BossBounce): the real rails of each screen, the boss-fight weather drift, 60 and 120 fps
            Ricochet.Run(Check);
            foreach (int w in new[] { 0, 1, 2, 3, 4, 5, 6 })
            {
                RicochetSong(w, false, true, 60);
                RicochetSong(w, true, true, 120);
                RicochetSong(w, false, false, 120);
                RicochetSong(w, true, false, 60);
            }
            RicochetStress();
            foreach (int w in new[] { 0, 1, 2, 3, 4, 5, 6 }) Live(w);
            // a phone's audio clock: 120 Hz frames, beat position in 42 ms buffer steps (the "no attack comes" report)
            foreach (int w in new[] { 0, 1, 2, 3, 4, 5, 6 }) Live(w, 120, .042);
            DrakePoses();
            TimedOrigins();
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
        public int fires, offView, strayFlash;
        public float worstEmitter;
        public BossMusicDirector director;
        // ricochets
        public int eligible, bouncy, vetoed, ricochets, maxBouncyLive, peakShadows, bigWaves, allOrNone, simBouncy;
        public float railEdge, shipHalf;
    }

    // How a song is played: the view, the rails (the authored test lane or the real drawn rails of that screen), the
    // boss-fight weather drift (a triangle wave at WeatherWind's cap and slew) and the frame rate.
    sealed class Rig
    {
        public bool phone, realRails, drift;
    }

    // ---- the stage: the boss's REAL muzzles (BossEmitterTable, its idle drawing) on a boss resting at `bossY` and
    // swaying like the live one, in the authored 10 u view or a tall 21:9 phone view (boss high under the HUD band,
    // the ship's ceiling right under the boss's lowest muzzle, shots scaled to the longer drop)
    sealed class Stage
    {
        public BossDef boss;
        public Vector2[] local;
        public float bossY;
        public float playerY;
    }

    static Stage MakeStage(PatternSim sim, int world, bool phone)
    {
        var boss = BossCatalog.ForWorld(world);
        int w = BossEmitters.World(boss);
        int n = BossEmitterTable.Parts[w].Length;
        var st = new Stage { boss = boss, local = new Vector2[n], bossY = phone ? 4.7f : 3.15f, playerY = phone ? -4.3f : -3.2f };
        int idle = BossArt.IdleStart(boss);
        for (int p = 0; p < n; p++) st.local[p] = BossEmitters.Local(boss, p, idle);
        sim.emitters = new Vector2[n];
        if (phone)
        {
            sim.viewTop = st.bossY + BossConfig.TopReach + BossConfig.BossTopMargin + .7f;   // the band: the HUD's top strip
            sim.spawnTop = sim.viewTop + .15f;
            sim.zoneTop = st.bossY - BossConfig.Underside(boss) - .45f - .35f;
            sim.zoneBottom = -5.2f;
            sim.floorY = -8f;
            sim.shotScale = (st.bossY - st.playerY) / BossConfig.AuthoredShotGap;
        }
        else sim.viewTop = 5f;
        Sway(sim, st, 0f);
        return st;
    }

    // the boss's drift and bob (BossActor.StepFight: it all but holds still while a pattern plays, x.35)
    static bool NoSway;
    static void Sway(PatternSim sim, Stage st, float t)
    {
        float k = NoSway ? 0f : t * .35f;
        var b = st.boss;
        var at = new Vector2(b.swayX * Mathf.Sin(k * b.freqX * Mathf.PI * 2f), st.bossY + b.swayY * Mathf.Sin(k * b.freqY * Mathf.PI * 2f));
        for (int p = 0; p < st.local.Length; p++) sim.emitters[p] = at + st.local[p];
    }

    static Stats Play(int world, Func<int, float> realDt, Func<double, float> gameScale, double seconds, double startClip, bool phone = false, Rig rig = null)
    {
        if (rig != null) phone = rig.phone;
        var map = BeatMaps.For(Names[world]);
        double dsp = 0;
        BeatClock.Now = () => dsp;
        var clock = new BeatClock(map) { UseLatencyOverride = true };
        clock.Sample(0, startClip, true);
        var sim = new PatternSim { cap = BossConfig.ProjectilePoolMax };
        var stage = MakeStage(sim, world, phone);
        if (rig != null && rig.realRails)
        {
            // the drawn rails' inner face on this screen (every world lays its rails out on Frost's footprint, Desert flies
            // between Storm's: BossRails.ReinforcedInnerEdge + RailInset), and the ship's reach inside them
            sim.railEdge = RealRail(phone);
            sim.shipHalf = ShipReach.HalfWidthFor(sim.railEdge, ShipScale.HullHalfWidth);
        }
        sim.player = new Vector2(0f, stage.playerY);
        var director = new BossMusicDirector(world, BossCatalog.ForWorld(world), sim);
        var st = new Stats { railEdge = sim.railEdge, shipHalf = sim.shipHalf };
        float windTarget = WeatherWind.BossShotWindCap;
        sim.TrackBegin(sim.player);
        st.director = director;
        float gameT = 0f;
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
            gameT += dt;
            Sway(sim, stage, gameT);
            if (rig != null && rig.drift && dt > 0f)
            {
                // the worst sustained weather: full cap one way, then the other, at the slew limit
                if (Mathf.Abs(sim.driftVx - windTarget) < 1e-4f) windTarget = -windTarget;
                sim.driftVx = Mathf.MoveTowards(sim.driftVx, windTarget, WeatherWind.BossShotWindSlew * dt);
            }
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
                int we = director.Waves.WaveEligible, wb = director.Waves.WaveBouncy;
                if (we > 6) { st.bigWaves++; if (wb == 0 || wb == we) st.allOrNone++; }
            }
            if (dt > 0f)
            {
                int n = sim.TrackStep(dt, 5f, .04f);
                if (dsp > 8) { st.minReach = Math.Min(st.minReach, n); if (n == 0) { st.emptyFrames++; if (st.emptyFrames <= 3) Debug.Log("[WAVES] dead end: " + Names[world] + (phone ? " (phone)" : "") + " t=" + dsp.ToString("F2") + " accents=" + director.AccentEmissions + " lastWave=" + director.Waves.LastKind + " arr=" + director.Waves.LastArrival.ToString("F2") + " spawn=" + director.Waves.LastSpawnGame.ToString("F2") + " gap=" + director.Waves.LastGapCenter.ToString("F2") + " w=" + director.Waves.LastClearWidth.ToString("F2") + " live=" + sim.live + " player=" + sim.player.x.ToString("F2") + " reachR=" + director.Waves.gaps.ReachLo.ToString("F2") + ".." + director.Waves.gaps.ReachHi.ToString("F2")); if (st.emptyFrames == 1) DumpZone(sim); sim.TrackBegin(new Vector2(0f, stage.playerY)); } }
                if (n > 0) sim.player.x = Mathf.Clamp(sim.ReachCentroid(), -1.6f, 1.6f);
            }
            st.peak = Math.Max(st.peak, sim.live);
        }
        st.dropped = sim.dropped;
        var sel = director.Waves.bounce;
        st.eligible = sel.eligible; st.bouncy = sel.picked; st.vetoed = sel.vetoed;
        st.ricochets = sim.ricochets; st.maxBouncyLive = sim.maxBouncyLive; st.simBouncy = sim.bouncyFired;
        st.peakShadows = director.Waves.gaps.PeakShadows;
        st.fires = sim.fires; st.offView = sim.offView; st.worstEmitter = sim.worstEmitterDist;
        for (int p = 0; p < sim.emittedParts.Length; p++)
            if (sim.emittedParts[p] > 0 && Array.IndexOf(director.Parts, p) < 0) st.strayFlash++;
        return st;
    }

    static void DumpZone(PatternSim sim)
    {
        var sb = new System.Text.StringBuilder("[WAVES]   zone " + sim.zoneBottom.ToString("F2") + ".." + sim.zoneTop.ToString("F2") + " shots:");
        for (int i = 0; i < sim.cap; i++)
        {
            var q = sim.shots[i];
            if (!q.alive || q.p.y < sim.zoneBottom - .5f || q.p.y > sim.zoneTop + .5f) continue;
            sb.Append(" (" + q.p.x.ToString("F2") + "," + q.p.y.ToString("F2") + " v" + q.v.x.ToString("F1") + "," + q.v.y.ToString("F1") + (q.turnLeft > 0 ? " deploying" : "") + (q.fuse > 0 ? " fuse" : "") + ")");
        }
        Debug.Log(sb.ToString());
    }

    static void Song(int world, bool phone)
    {
        // the whole real song plus a loop seam, at an uneven 30/60 fps
        var st = Play(world, f => f % 3 == 0 ? 1f / 30f : 1f / 60f, t => 1f, BeatMaps.For(Names[world]).duration + 12, 0, phone);
        string n = Names[world] + (phone ? " (21:9 phone)" : "");
        Check(n + ": all " + st.fires + " shots start AT a muzzle of the boss (worst " + st.worstEmitter.ToString("F3") + " u off), none outside the view (" + st.offView + "), flashes only on its wave parts (" + st.strayFlash + " stray)",
              st.fires > 100 && st.worstEmitter <= .01f && st.offView == 0 && st.strayFlash == 0);
        Check(n + ": " + st.waves + " waves, never a moment with no reachable cell (min reach " + st.minReach + " cells, empty frames " + st.emptyFrames + ")", st.waves > 30 && st.emptyFrames == 0);
        Check(n + ": every wave leaves the boss on a sixteenth (" + st.gridFails + " off-grid), within a frame of the beat (late " + (st.maxLate * 1000).ToString("F0") + " ms)", st.gridFails == 0 && st.maxLate <= 1.0 / 30 + .001);
        Check(n + ": every gap is at least " + WaveGaps.MinMultiple + " ship widths clear and every wave lands a whole number of beats later", st.gapFails == 0 && st.travelFails == 0);
        Check(n + ": peak " + st.peak + " live shots, dropped " + st.dropped + "; director " + (st.ms / Math.Max(1, st.steps) * 1000).ToString("F1") + " us/frame, " + (st.allocBytes / Math.Max(1, st.steps)) + " B/frame", st.dropped == 0 && st.peak <= BossConfig.ProjectilePoolMax);
    }

    // The pause / slow-motion pattern repeats every 20 s, so 60 s from the 30 s mark still sees three pauses and three slow-motion
    // stretches (it was 90 s: the extra cycle only repeated the first three).
    const double StressSeconds = 60;

    // pauses and slow motion in the middle of a song must not squeeze a gap shut
    static void Stress(int world)
    {
        var st = Play(world, f => f % 5 == 0 ? 1f / 30f : 1f / 60f, t => { double c = t % 20; return c >= 10 && c < 12 ? 0f : c > 15 ? .25f : 1f; }, StressSeconds, 30);
        Check(Names[world] + " stays survivable through pauses and slow motion (empty frames " + st.emptyFrames + ", min reach " + st.minReach + ")", st.emptyFrames == 0);
    }

    // ---- each boss's wave sources are real muzzles on the art, told in the pose of the attack that already uses them
    static void Sources()
    {
        bool real = true, drake = true;
        for (int w = 0; w < 7; w++)
        {
            var boss = BossCatalog.ForWorld(w);
            var s = BossWaveSources.For(w, boss, 0);
            foreach (var src in new[] { s.wall, s.wallAlt, s.ring, s.fan, s.accent })
                foreach (int p in src.parts) real &= p >= 0 && p < BossEmitterTable.Parts[BossEmitters.World(boss)].Length;
            real &= s.all.Length <= BossActor.MaxParts;
            if (w == 3)
            {
                drake &= Array.IndexOf(s.wall.parts, BossEmitters.Part(boss, "CannonL")) >= 0 && Array.IndexOf(s.wall.parts, BossEmitters.Part(boss, "CannonR")) >= 0;
                drake &= s.wall.pose == 1 && s.fan.pose == 0 && s.fan.parts[0] == BossEmitters.Part(boss, "Jaw") && s.fan.holdBeats >= 1f;
                drake &= s.accent.parts[0] == BossEmitters.Part(boss, "Furnace");
            }
            Debug.Log("[WAVES] " + Names[w] + " sources: wall " + src(s.wall) + (s.hasAlt ? " / " + src(s.wallAlt) : "") + ", ring " + src(s.ring) + ", fan " + src(s.fan) + ", accent " + src(s.accent));
        }
        Check("every wave source is a measured muzzle of its boss (and fits the boss's flash slots)", real);
        Check("Cinder Drake: slug walls from its twin brass cannons + furnace (pose 1), flame fans from its maw (pose 0, a held breath), bombs from the furnace", drake);
    }

    static string src(WaveSource s) => s.label + " (pose " + s.pose + ")";

    // ---- the LIVE boss: a real BossActor and BossProjectilePool playing a beat clock. Every tell is answered by bullets
    // within two beats, every bullet starts on a muzzle of the part the wave came from (in the drawing on screen), none
    // outside the view, none culled straight away, and the drawings follow: the source's tell on the beat before, its
    // fire drawing the frame the bullets leave (held for a breath), idle in between.
    static void Live(int world) => Live(world, 60, 0.0);

    // `fps` frames a second; `audioStep` > 0: the beat clock moves in steps of that many seconds, like dspTime on a
    // phone (its audio buffer: 20..45 ms) -- longer than a 120 Hz frame.
    static void Live(int world, int fps, double audioStep)
    {
        using var sandbox = new TestHarness.Sandbox();
        var boss = BossCatalog.ForWorld(world);
        var actor = BossActor.Spawn(boss);
        var pool = new BossProjectilePool(BossConfig.ProjectilePoolMax, BossConfig.BeamPoolMax);
        var music = new Manual { Moment = new MusicMoment { energy = .7f, bass = .7f, accent = .7f, kind = "drive" } };
        int tells = 0, missed = 0, launched = 0, offMuzzle = 0, offView = 0, culled = 0, bursts = 0;
        int firePose = 0, tellPose = 0, idlePose = 0, frames = 0, poseFrames = 0;
        int fanWaves = 0, shortBreath = 0;
        BossActor.OffscreenSpawns = 0;
        string firstMiss = null;
        try
        {
            actor.BeginFight();
            actor.BeatOverride = music;
            var player = new Vector3(0f, -3.2f, 0f);
            float dt = 1f / fps;
            double told = double.NaN;      // the wave beat the boss is telling
            bool answered = true;
            int waves = 0, accents = 0, breath = -1, breathTarget = 0;
            var seen = new System.Collections.Generic.HashSet<BossProjectile>();
            var watch = new System.Collections.Generic.List<(BossProjectile s, int f, Vector3 at)>();
            for (int f = 0; f < fps * 24; f++)
            {
                double t = f * (double)dt;
                if (audioStep > 0) t = Math.Floor(t / audioStep) * audioStep;
                music.BeatNow = t / .4;
                int before = actor.BodyFrame;
                actor.StepFight(dt, dt, player, .8f, pool);
                var d = actor.MusicDirector;
                bool waveNow = d.Waves.Waves != waves && d.Waves.LastBullets > 0, accentNow = d.AccentEmissions != accents;
                waves = d.Waves.Waves; accents = d.AccentEmissions;
                var from = new System.Collections.Generic.List<int>();
                if (waveNow) from.AddRange(d.Waves.LastSource.parts);
                if (accentNow) from.AddRange(d.Sources.accent.parts);
                int fresh = 0;
                foreach (var s in pool.Shots)
                {
                    if (s == null || !s.Active || seen.Contains(s)) continue;
                    fresh++; launched++;
                    float best = 99f;
                    foreach (int part in from) foreach (int fr in new[] { before, actor.BodyFrame })
                        best = Mathf.Min(best, Vector2.Distance(s.LaunchedAt, actor.Emitter(part, fr)));
                    if (best > .02f) { offMuzzle++; if (firstMiss == null) firstMiss = "shot at " + s.LaunchedAt + " is " + best.ToString("F2") + " u from " + string.Join(",", from); }
                    if (s.LaunchedAt.y > CameraFit.ViewTop || Mathf.Abs(s.LaunchedAt.x) > CameraFit.GameplayHalfWidth) offView++;
                    watch.Add((s, f, s.LaunchedAt));
                }
                // a telegraph (the boss telling the wave on beat `told`) must be answered by bullets by two beats after it
                if (fresh > 0 && !double.IsNaN(told) && music.BeatNow >= told - .3) answered = true;
                if (d.Telling && d.NextWaveBeat != told)
                {
                    if (!answered && !double.IsNaN(told)) missed++;
                    told = d.NextWaveBeat; answered = false; tells++;
                }
                if (fresh > 0 && !double.IsNaN(told) && music.BeatNow >= told - .3) answered = true;
                if (!answered && music.BeatNow > told + 2) { missed++; answered = true; }
                // the drawings (after the song's lead-in; a special in progress shows its own tell: BossSpecialsTest)
                if (f > fps * 2 && !actor.SpecialActive)
                {
                    frames++;
                    int frame = actor.BodyFrame;
                    if (breath >= 0)
                    {
                        if (waveNow || accentNow) breath = -1;   // the next wave takes over
                        else if (frame == BossArt.FireFrame(boss, d.Sources.fan.pose)) breath++;
                        else { if (breath < breathTarget) shortBreath++; breath = -1; }
                    }
                    if (waveNow)
                    {
                        poseFrames++;
                        if (frame != BossArt.FireFrame(boss, d.Waves.LastSource.pose)) firePose++;
                        if (d.Waves.LastKind == WaveKind.Fan && d.Waves.LastSource.holdBeats > 0f)
                        { fanWaves++; breath = 0; breathTarget = Mathf.FloorToInt(d.Waves.LastHold * fps) - 2; }
                    }
                    else if (actor.FireLeft <= 0f && d.Telling)
                    {
                        if (frame != BossArt.TellFrame(boss, d.TellPose, d.TellProgress * .66f)) tellPose++;
                    }
                    else if (actor.FireLeft <= 0f && !BossArt.IsIdleFrame(frame)) idlePose++;
                }
                pool.PlayerPos = player;
                pool.Step(dt);
                AttackPools.StepAll(dt);   // a special's hazards run their tells (nobody else steps them here)
                seen.Clear();
                foreach (var s in pool.Shots) if (s != null && s.Active) seen.Add(s);   // burst shards included
                // half a second on: still flying (never culled at once as off screen)
                for (int i = watch.Count - 1; i >= 0; i--)
                {
                    if (f - watch[i].f < fps / 2) continue;
                    var w = watch[i];
                    if (!w.s.Active && w.s.LaunchedAt == w.at && w.s.EndReason == 1) culled++;   // gone off screen within half a second
                    watch.RemoveAt(i);
                }
            }
        }
        finally { AttackPools.ClearAll(); pool.Dispose(); UnityEngine.Object.DestroyImmediate(actor.gameObject); }
        string n = Names[world] + " (live, " + fps + " fps" + (audioStep > 0 ? ", audio clock in " + (audioStep * 1000).ToString("F0") + " ms steps" : "") + ")";
        Check(n + ": every telegraph is followed by bullets within two beats (" + tells + " tells, " + missed + " unanswered)", tells >= 20 && missed == 0);
        if (audioStep > 0) return;
        Check(n + ": " + launched + " shots, all from the firing part's muzzle in the drawing on screen (" + offMuzzle + " off" + (firstMiss != null ? ": " + firstMiss : "") + "), none outside the view (" + offView + ", guard " + BossActor.OffscreenSpawns + "), none culled at once (" + culled + ")",
              launched > 100 && offMuzzle == 0 && offView == 0 && BossActor.OffscreenSpawns == 0 && culled == 0);
        Check(n + ": the source's fire drawing as the bullets leave (" + firePose + " of " + poseFrames + " wrong), its tell before (" + tellPose + " wrong), idle between (" + idlePose + " of " + frames + " frames wrong)" + (fanWaves > 0 ? ", " + fanWaves + " breaths held (" + shortBreath + " short)" : ""),
              poseFrames > 10 && firePose == 0 && tellPose == 0 && idlePose == 0 && shortBreath == 0);
    }

    // The drawn rails' inner face (world x) on the authored 16:9 screen or the 21:9 phone (1080 x 2520).
    static float RealRail(bool phone) => BossRails.ReinforcedInnerEdge + (phone ? RailInset.ShiftFor(1080f, 2520f) : RailInset.ShiftFor(1080f, 1920f));

    // ---- RICOCHETS: a whole song with ~30 % of the rail-meeting bullets bouncing, flood-filled every frame
    static void RicochetSong(int world, bool phone, bool drift, int fps)
    {
        var rig = new Rig { phone = phone, realRails = true, drift = drift };
        float dt = 1f / fps;
        var st = Play(world, f => dt, t => 1f, BeatMaps.For(Names[world]).duration + 12, 0, phone, rig);
        string n = Names[world] + " ricochets (" + (phone ? "21:9 phone" : "authored") + ", rails at " + st.railEdge.ToString("F2") + ", ship reach " + st.shipHalf.ToString("F2") + (drift ? ", weather drift" : "") + ", " + fps + " fps)";
        float frac = st.eligible > 0 ? st.bouncy / (float)st.eligible : 0f;
        Check(n + ": " + st.waves + " waves, never a moment with no reachable cell (min reach " + st.minReach + ", empty frames " + st.emptyFrames + "), every gap " + WaveGaps.MinMultiple + " ship widths clear",
              st.waves > 30 && st.emptyFrames == 0 && st.gapFails == 0 && st.travelFails == 0);
        Check(n + ": " + st.bouncy + " of " + st.eligible + " rail-meeting bullets bounce (" + (frac * 100f).ToString("F1") + " %, " + st.vetoed + " vetoed as unfair), " + st.ricochets + " ricochets, " + st.allOrNone + " of " + st.bigWaves + " waves of 7+ such bullets all or none bouncy",
              st.eligible > 20 && st.bouncy == st.simBouncy && Mathf.Abs(frac - BossBounce.FractionFor(world)) <= .03f && st.ricochets > 0 && st.allOrNone == 0);
        Check(n + ": peak " + st.peak + " live shots (" + st.maxBouncyLive + " bouncy, " + st.peakShadows + " shadows), dropped " + st.dropped + "; director " + (st.ms / Math.Max(1, st.steps) * 1000).ToString("F1") + " us/frame, " + (st.allocBytes / Math.Max(1, st.steps)) + " B/frame",
              st.dropped == 0 && st.peak <= BossConfig.ProjectilePoolMax - 1 && st.peakShadows < WaveGaps.MaxShadows && st.allocBytes == 0);
    }

    // pauses and slow motion with ricochets in flight (game time: the ricochets freeze and slow with everything else)
    static void RicochetStress()
    {
        foreach (int w in new[] { 0, 1, 4, 6 })
        {
            var st = Play(w, f => f % 5 == 0 ? 1f / 30f : 1f / 60f, t => { double c = t % 20; return c >= 10 && c < 12 ? 0f : c > 15 ? .25f : 1f; }, StressSeconds, 30, true,
                          new Rig { phone = true, realRails = true, drift = true });
            Check(Names[w] + " ricochets stay survivable through pauses and slow motion (21:9, drift; empty frames " + st.emptyFrames + ", min reach " + st.minReach + ", " + st.bouncy + " bouncy)",
                  st.emptyFrames == 0 && st.bouncy > 0);
        }
    }

    // ---- the Cinder Drake's own cells: its muzzles sit on the cannons / maw / furnace / brow in every drawing it
    // tells or fires from, at every damage stage the attack poses are painted for (the damaged poses share the
    // pristine drawing's geometry), and its fire cells are the ones the art paints the shots in.
    static void DrakePoses()
    {
        var boss = BossCatalog.ForWorld(3);
        bool cells = BossArt.FireFrame(boss, 0) == 28 && BossArt.FireFrame(boss, 1) == 31 && BossArt.FireFrame(boss, 2) == 34;
        int cl = BossEmitters.Part(boss, "CannonL"), cr = BossEmitters.Part(boss, "CannonR"), jaw = BossEmitters.Part(boss, "Jaw");
        bool sides = true;
        for (int f = 0; f < 35; f++)
        {
            if (f >= 12 && f < 20) continue;   // death / retreat / portrait: nothing fires
            var l = BossEmitters.Local(boss, cl, f); var r = BossEmitters.Local(boss, cr, f); var j = BossEmitters.Local(boss, jaw, f);
            // the cannons are the shoulder guns either side of the head, level with each other, above the maw
            sides &= l.x < -.4f && r.x > .4f && Mathf.Abs(l.y - r.y) < .25f && l.y > j.y;
        }
        Check("Cinder Drake: fire cells 28 / 31 / 34 (breath / slugs / brow) and the twin cannons measured either side of the head in every tell, fire and idle cell", cells && sides);
        // by pixel (384 reference px): the brow on the gem in the breath cell (the matcher had it ~20 px above), the
        // maw on the flame's root, and the cannon bores where they are drawn in the breath and slug cells
        int brow = BossEmitters.Part(boss, "Brow");
        var b28 = BossEmitters.Pixel(boss, brow, 28); var j28 = BossEmitters.Pixel(boss, jaw, 28);
        var l20 = BossEmitters.Pixel(boss, cl, 20); var r20 = BossEmitters.Pixel(boss, cr, 20);
        bool pixels = Mathf.Abs(b28.x - 191) <= 3 && Mathf.Abs(b28.y - 129) <= 3 && Mathf.Abs(j28.x - 192) <= 4 && j28.y > b28.y + 15;
        foreach (int f in new[] { 28, 31 })
        {
            var l = BossEmitters.Pixel(boss, cl, f); var r = BossEmitters.Pixel(boss, cr, f);
            pixels &= (l - l20).magnitude <= 12f && (r - r20).magnitude <= 12f;
        }
        pixels &= Mathf.Abs(l20.x - 108) <= 3 && Mathf.Abs(r20.x - 270) <= 3 && Mathf.Abs(l20.y - 141) <= 3;
        Check("Cinder Drake: brow on its gem in the breath cell 28 (" + b28 + "), maw at the flame root (" + j28 + "), cannon bores at " + l20 + " / " + r20 + " and still on them in cells 28 and 31", pixels);
    }

    // ---- the timed rotation (no song: the fallback and the developer previews): every projectile of every attack of
    // every boss leaves one of that attack's own muzzles in the drawing on screen, and the drawing is that attack's
    // tell / fire pose.
    static void TimedOrigins()
    {
        int bad = 0, total = 0, wrongPose = 0;
        string first = null;
        for (int w = 0; w < 7; w++)
        {
            var boss = BossCatalog.ForWorld(w);
            for (int a = 0; a < boss.attacks.Length; a++)
            {
                var atk = boss.attacks[a];
                if (atk.kind != BossAttackKind.Aimed && atk.kind != BossAttackKind.Fan && atk.kind != BossAttackKind.Lob && atk.kind != BossAttackKind.Pattern) continue;
                var parts = new System.Collections.Generic.List<int>();
                if (atk.parts != null) foreach (int p in atk.parts) if (p >= 0) parts.Add(p);
                if (atk.pattern != null) foreach (var st in atk.pattern.streams) if (st.parts != null) foreach (int p in st.parts) if (p >= 0 && !parts.Contains(p)) parts.Add(p);
                if (parts.Count == 0) continue;
                using var sandbox = new TestHarness.Sandbox();
                var actor = BossActor.Spawn(boss);
                var pool = new BossProjectilePool(BossConfig.ProjectilePoolMax, BossConfig.BeamPoolMax);
                try
                {
                    actor.BeginFight();
                    actor.ForcedAttack = a;
                    var player = new Vector3(.4f, -3.2f, 0f);
                    const float dt = 1f / 60f;
                    var seen = new System.Collections.Generic.HashSet<BossProjectile>();
                    for (int f = 0; f < 60 * 7; f++)
                    {
                        int before = actor.BodyFrame;
                        actor.StepFight(dt, dt, player, 1f, pool);
                        foreach (var s in pool.Shots)
                        {
                            if (s == null || !s.Active || seen.Contains(s)) continue;
                            total++;
                            float best = 99f;
                            foreach (int part in parts) foreach (int fr in new[] { before, actor.BodyFrame })
                                best = Mathf.Min(best, Vector2.Distance(s.LaunchedAt, actor.Emitter(part, fr)));
                            if (best > .02f) { bad++; if (first == null) first = boss.name + " " + atk.name + ": " + best.ToString("F2") + " u off its muzzles"; }
                            int frame = actor.BodyFrame;
                            bool pose = frame == BossArt.FireFrame(boss, atk.tell) || frame == BossArt.Hit;
                            for (int k = 0; k < 3; k++) pose |= frame == BossArt.TellFrame(boss, atk.tell, k * .4f);
                            if (!pose) { wrongPose++; if (first == null) first = boss.name + " " + atk.name + ": drawing " + frame + " is not its pose " + atk.tell; }
                        }
                        pool.PlayerPos = player;
                        pool.Step(dt);
                        seen.Clear();
                        foreach (var s in pool.Shots) if (s != null && s.Active) seen.Add(s);   // a bomb's burst shards start where it burst
                    }
                }
                finally { pool.Dispose(); UnityEngine.Object.DestroyImmediate(actor.gameObject); }
            }
        }
        Check("timed rotation: all " + total + " shots of every boss's projectile attacks leave that attack's own muzzles (" + bad + " off) in its own tell / fire drawing (" + wrongPose + " wrong)" + (first != null ? " -- first: " + first : ""),
              total > 200 && bad == 0 && wrongPose == 0);
    }
}
