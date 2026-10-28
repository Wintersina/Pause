using System;
using UnityEngine;

// The boss difficulty ladder (BossDifficulty): one table, enforced as a ladder, measured on the real songs.
//
//   1 table      seven rows in world order; every knob non-decreasing in difficulty from boss 1 to boss 7 (ramp seconds
//                non-increasing, the start level, floor, wave speed and beat-sync non-decreasing);
//                boss 1 is today's fight (the song alone: Shape is the identity, speed x1, the old start);
//                the last two have no ramp and sync from the start; every floor stays under the gap-telegraph line (.75)
//   2 ramp       the ramp level at the fight's first frame is under 1 for bosses 1..5 and rises smoothly (no step over
//                a quarter second bigger than .08) to 1 at rampSeconds; it is 1 from the first frame for the last two
//   3 shape      the shaped intensity stays in 0..1, never exceeds the ramp level, never drops under the floor's share
//                of it, rises with the song's raw intensity, and is the floor at the very least for the last two at t = 0
//   4 first 10 s each boss's real song from the fight's first frame (the song started IntroSeconds earlier, at its
//                fight start), old (no ladder) and new: the first wave's time and its place on the beat grid; for the
//                last two the first wave leaves on the first beat that leaves its one-beat tell (no two-beat calm), every
//                wave of the first ten seconds is on the beat grid with at least the floor's intensity, and none of
//                them arrives at the ship's row in the first second; bosses 2..5 open capped by the ramp
//   5 fairness   the first 30 s of every boss on the new ladder: the omniscient flood fill never runs out of reachable
//                cells, every gap is at least WaveGaps.MinMultiple ship widths, every tell answered within two beats
//   6 caps       a whole 120 s fight of every boss on the new ladder: no shot dropped, the peak within the pool,
//                and the director allocates nothing per frame
//   7 speed      a wave of the last boss falls shotSpeedScale times as fast as without the ladder
//   8 rotation   the ladder has no unlock knob: each boss's phase structure stays as authored (BossThemedTest)
//   9 seeds      the same fight twice gives the same waves (deterministic)
public static class BossDifficultyTest
{
    static int fails;
    static readonly string[] Names = { "Space", "Frost", "Verdant", "Ember", "Tide", "Storm", "Desert" };
    static void Check(string what, bool ok) { Debug.Log("[DIFFICULTY] " + (ok ? "PASS " : "FAIL ") + what); if (!ok) fails++; }
    public static void Run() { TestHarness.Exit(Execute()); }

    public static int Execute()
    {
        fails = 0;
        var oldNow = BeatClock.Now;
        bool was = BossDifficulty.Enabled;
        try
        {
            BossDifficulty.Enabled = true;
            Table();
            Ramp();
            Shape();
            for (int w = 0; w < 7; w++) Opening(w);
            for (int w = 0; w < 7; w++) Fairness(w);
            for (int w = 0; w < 7; w++) Caps(w);
            Speed();
            Rotation();
            Seeds();
        }
        finally { BeatClock.Now = oldNow; BossDifficulty.Enabled = was; }
        Debug.Log("[DIFFICULTY] failures: " + fails);
        return fails;
    }

    // ---- 1 the table is a ladder
    static void Table()
    {
        var rows = BossDifficulty.Rows;
        Check("seven rows, one per boss in world order (" + rows.Length + ")", rows.Length == 7 && BossPatternTables.Bosses == 7);
        bool ramp = true, start = true, floor = true, speed = true, sync = true;
        for (int w = 1; w < rows.Length; w++)
        {
            ramp &= rows[w].rampSeconds <= rows[w - 1].rampSeconds;
            start &= rows[w].startIntensity >= rows[w - 1].startIntensity;
            floor &= rows[w].intensityFloor >= rows[w - 1].intensityFloor;
            speed &= rows[w].shotSpeedScale >= rows[w - 1].shotSpeedScale;
            sync &= !rows[w - 1].beatSyncFromStart || rows[w].beatSyncFromStart;
        }
        Check("ramp seconds never grow up the ladder (" + Join(r => r.rampSeconds) + ")", ramp);
        Check("the ramp's start level never falls up the ladder (" + Join(r => r.startIntensity) + ")", start);
        Check("the intensity floor never falls up the ladder (" + Join(r => r.intensityFloor) + ")", floor);
        Check("the wave speed never falls up the ladder (" + Join(r => r.shotSpeedScale) + ")", speed);
        Check("beat sync from the start, once on, stays on up the ladder", sync);
        bool last = true;
        for (int w = 5; w < 7; w++) last &= rows[w].rampSeconds == 0f && rows[w].startIntensity == 1f && rows[w].beatSyncFromStart && !rows[w].songRamp;
        Check("the last two bosses (" + rows[5].boss + ", " + rows[6].boss + "): no ramp, full from the start, on the beat from the first wave", last);
        bool middle = true;
        for (int w = 1; w < 5; w++) middle &= !rows[w].songRamp && rows[w].rampSeconds > 0f && rows[w].startIntensity < 1f && !rows[w].beatSyncFromStart && rows[w].intensityFloor > 0f;
        Check("bosses 2..5 have an imposed, shortening ramp and a floor", middle);
        bool telegraph = true;
        foreach (var r in rows) telegraph &= r.intensityFloor < .75f && r.intensityFloor >= 0f && r.startIntensity >= 0f && r.startIntensity <= 1f;
        Check("every floor stays under .75: a floor-driven wave always shows its gap telegraph", telegraph);
        // boss 1 is today's fight
        var a = rows[0];
        bool identity = true;
        for (float t = 0f; t <= 130f; t += 2.5f)
            for (float raw = 0f; raw <= 1.001f; raw += .05f) identity &= BossDifficulty.Shape(0, raw, t) == raw;
        Check("boss 1 (" + a.boss + ") is unchanged: the song alone (Shape is the identity), speed x1, the old start",
              a.songRamp && identity && BossDifficulty.ShotSpeed(0) == 1f && !BossDifficulty.SyncFromStart(0));
        bool onlyFirst = true;
        for (int w = 1; w < 7; w++) onlyFirst &= !rows[w].songRamp;
        Check("only boss 1 keeps the song's own ramp", onlyFirst);
    }

    static string Join(Func<BossDifficultyRow, float> f)
    {
        var s = new string[BossDifficulty.Rows.Length];
        for (int i = 0; i < s.Length; i++) s[i] = f(BossDifficulty.Rows[i]).ToString("0.##");
        return string.Join(" / ", s);
    }

    // ---- 2 the ramp level
    static void Ramp()
    {
        for (int w = 0; w < 7; w++)
        {
            var r = BossDifficulty.For(w);
            float at0 = BossDifficulty.RampLevel(w, 0f);
            if (w >= 5) { Check(Names[w] + ": full from the first frame (level " + at0 + ")", at0 == 1f && BossDifficulty.RampLevel(w, 3f) == 1f); continue; }
            bool rising = true, smooth = true;
            float prev = at0;
            for (float t = .25f; t <= r.rampSeconds + 1f; t += .25f)
            {
                float v = BossDifficulty.RampLevel(w, t);
                rising &= v >= prev - 1e-6f;
                smooth &= v - prev <= .08f;
                prev = v;
            }
            Check(Names[w] + ": the ramp starts under full (" + at0.ToString("0.00") + "), rises smoothly and is full by " + r.rampSeconds + " s",
                  at0 < 1f && rising && smooth && BossDifficulty.RampLevel(w, r.rampSeconds) == 1f && BossDifficulty.RampLevel(w, r.rampSeconds + 30f) == 1f);
        }
    }

    // ---- 3 the shaped intensity
    static void Shape()
    {
        for (int w = 1; w < 7; w++)
        {
            var r = BossDifficulty.For(w);
            bool bounded = true, capped = true, floored = true, rising = true;
            for (float t = 0f; t <= 40f; t += .5f)
            {
                float level = BossDifficulty.RampLevel(w, t), prev = -1f;
                for (float raw = 0f; raw <= 1.001f; raw += .02f)
                {
                    float s = BossDifficulty.Shape(w, raw, t);
                    bounded &= s >= 0f && s <= 1f;
                    capped &= s <= level + 1e-6f;
                    floored &= s >= r.intensityFloor * level - 1e-6f;
                    rising &= s >= prev - 1e-6f;
                    prev = s;
                }
            }
            Check(Names[w] + ": shaped intensity in 0..1, under the ramp level, over the floor's share, rising with the song", bounded && capped && floored && rising);
        }
        for (int w = 5; w < 7; w++)
            Check(Names[w] + ": a silent song at t = 0 still plays at the floor (" + BossDifficulty.Shape(w, 0f, 0f) + ")", BossDifficulty.Shape(w, 0f, 0f) >= BossDifficulty.For(w).intensityFloor);
    }

    // ---- the real song from the fight's first frame
    sealed class Fight
    {
        public int waves, offGrid, offHalf, gapFails, empty, peak, dropped, frames;
        public double startNow, startBeat, firstBeat = double.NaN, maxLate;
        public float firstGame = -1f, firstArrival = -1f, firstIntensity, minIntensity = 1f, sumIntensity, maxOverLevel;
        public int earlyArrivals, lowIntensity, tells, unanswered;
        public long alloc;
        public long hash = 17;
        public float spb;
    }

    static Fight Play(int world, double seconds, bool ladder, bool reach, int fps = 60)
    {
        bool was = BossDifficulty.Enabled;
        BossDifficulty.Enabled = ladder;
        var fi = new Fight();
        try
        {
            var map = BeatMaps.For(Names[world]);
            double dsp = 0;
            BeatClock.Now = () => dsp;
            var clock = new BeatClock(map) { UseLatencyOverride = true };
            // the song starts with the entrance (at its fight start, StartSeconds); the fight begins IntroSeconds later
            double clip0 = map.StartSeconds + BossConfig.IntroSeconds;
            clock.Sample(0, clip0, true);
            var boss = BossCatalog.ForWorld(world);
            var sim = new PatternSim { cap = BossConfig.ProjectilePoolMax };
            Stage(sim, boss);
            sim.player = new Vector2(0f, -3.2f);
            var d = new BossMusicDirector(world, boss, sim);
            if (reach) sim.TrackBegin(sim.player);
            float dt = 1f / fps;
            int seen = 0, emitted = 0;
            double pendingTell = double.NaN;
            float need = WaveGaps.MinMultiple * 2f * PatternSim.ShipRadius - 1e-3f;
            var row = BossDifficulty.For(world);
            for (int f = 0; f * dt < seconds; f++)
            {
                dsp += dt;
                clock.Sample(dsp, (clip0 + dsp) % map.duration, true);
                sim.Advance(dt);
                if (f == 0) fi.startNow = clock.BeatNow;
                long a0 = GC.GetAllocatedBytesForCurrentThread();
                d.Step(clock, dt, dt, Mathf.Clamp01((float)(dsp / BossConfig.FightSeconds)));
                if (f > 0) fi.alloc += GC.GetAllocatedBytesForCurrentThread() - a0;
                if (f == 0) { fi.startBeat = d.StartBeat; fi.spb = (float)clock.SecondsPerBeat; }
                fi.frames++;
                // every tell is answered: a wave (or the accent on its sixteenth) leaves within two beats of the told beat
                if (d.Emissions != emitted) { emitted = d.Emissions; pendingTell = double.NaN; }
                if (d.Telling && double.IsNaN(pendingTell) && !double.IsNaN(d.NextWaveBeat)) { fi.tells++; pendingTell = d.NextWaveBeat; }
                if (!double.IsNaN(pendingTell) && clock.BeatNow > pendingTell + 2) { fi.unanswered++; pendingTell = double.NaN; }
                if (d.Waves.Waves != seen && d.Waves.LastBullets > 0)
                {
                    seen = d.Waves.Waves;
                    fi.waves++;
                    double beat = d.LastScheduledBeat;
                    if (double.IsNaN(fi.firstBeat)) { fi.firstBeat = beat; fi.firstGame = d.FightSeconds; fi.firstArrival = d.Waves.LastArrival; fi.firstIntensity = d.Intensity; }
                    if (Math.Abs(beat * 4 - Math.Round(beat * 4)) > 1e-6) fi.offGrid++;
                    if (Math.Abs(beat * 2 - Math.Round(beat * 2)) > 1e-6) fi.offHalf++;
                    fi.maxLate = Math.Max(fi.maxLate, d.LastLateBeats * clock.SecondsPerBeat);
                    if (d.Waves.LastClearWidth < need) fi.gapFails++;
                    if (d.Waves.LastArrival < 1f) fi.earlyArrivals++;
                    fi.minIntensity = Mathf.Min(fi.minIntensity, d.Intensity);
                    fi.sumIntensity += d.Intensity;
                    if (ladder && d.Intensity < row.intensityFloor * BossDifficulty.RampLevel(world, d.RampClock) - 1e-4f) fi.lowIntensity++;
                    if (ladder && !row.songRamp) fi.maxOverLevel = Mathf.Max(fi.maxOverLevel, d.Intensity - BossDifficulty.RampLevel(world, d.RampClock));
                    fi.hash = fi.hash * 31 + (long)Math.Round(beat * 4) * 7 + Mathf.RoundToInt(d.Waves.LastGapCenter * 1000f);
                }
                if (reach)
                {
                    int n = sim.TrackStep(dt, 5f, .04f);
                    if (n == 0) { fi.empty++; sim.TrackBegin(new Vector2(0f, -3.2f)); }
                    else sim.player.x = Mathf.Clamp(sim.ReachCentroid(), -1.6f, 1.6f);
                }
                else sim.player.x = Mathf.Sin((float)dsp * .3f) * 1.8f;
                fi.peak = Math.Max(fi.peak, sim.live);
            }
            fi.dropped = sim.dropped;
        }
        finally { BossDifficulty.Enabled = was; }
        return fi;
    }

    // The boss's muzzles in its idle drawing, at its authored rest (BossWaveTest's stage, no sway).
    static void Stage(PatternSim sim, BossDef boss)
    {
        int w = BossEmitters.World(boss);
        int n = BossEmitterTable.Parts[w].Length;
        int idle = BossArt.IdleStart(boss);
        sim.emitters = new Vector2[n];
        for (int p = 0; p < n; p++) sim.emitters[p] = new Vector2(0f, 3.15f) + BossEmitters.Local(boss, p, idle);
        sim.viewTop = 5f;
    }

    // ---- 4 the first ten seconds, old and new
    static void Opening(int w)
    {
        var old = Play(w, 10, false, false);
        var neu = Play(w, 10, true, false);
        var row = BossDifficulty.For(w);
        string Line(Fight f) => "first wave " + f.firstGame.ToString("0.00") + " s in (beat " + f.firstBeat.ToString("0.##") + ", " +
                                (f.firstBeat - f.startNow).ToString("0.00") + " beats after the fight's first frame), " + f.waves + " waves in 10 s, mean intensity " +
                                (f.sumIntensity / Math.Max(1, f.waves)).ToString("0.00") + ", min " + f.minIntensity.ToString("0.00");
        Debug.Log("[DIFFICULTY] TABLE " + Names[w] + " old: " + Line(old));
        Debug.Log("[DIFFICULTY] TABLE " + Names[w] + " new: " + Line(neu));
        Check(Names[w] + ": the first wave arrives no sooner than a second into the fight (" + neu.firstArrival.ToString("0.00") + " s), none of the first ten seconds' waves earlier",
              neu.waves > 0 && neu.firstArrival >= 1f && neu.earlyArrivals == 0);
        Check(Names[w] + ": every wave of the first ten seconds on a sixteenth, within a frame of it (late " + (neu.maxLate * 1000).ToString("0") + " ms)",
              neu.offGrid == 0 && neu.maxLate <= 1.0 / 60 + .001);
        Check(Names[w] + ": old start: two beats of calm before the first wave (" + (old.startBeat - old.startNow).ToString("0.00") + ")", old.startBeat - old.startNow >= 2 - 1e-6);
        if (w == 0)
        {
            Check("Space: the first ten seconds are exactly today's (" + old.waves + " waves, hash " + (old.hash == neu.hash) + ")",
                  old.waves == neu.waves && old.hash == neu.hash && old.startBeat == neu.startBeat);
            return;
        }
        Check(Names[w] + ": no wave under the floor's share of the ramp level", neu.lowIntensity == 0);
        if (w < 5)
        {
            Check(Names[w] + ": the opening is capped by its ramp (intensity never over the level, worst " + neu.maxOverLevel.ToString("0.000") + "), the old two-beat start kept",
                  neu.maxOverLevel <= 1e-4f && neu.startBeat - neu.startNow >= 2 - 1e-6);
            return;
        }
        double expected = Math.Ceiling(neu.startNow + BossMusicDirector.TellBeats - 1e-6);
        Check(Names[w] + ": the director starts on the first beat that leaves its one-beat tell (" + neu.startBeat + " = " + expected + ", " + (neu.startBeat - neu.startNow).ToString("0.00") + " beats in)",
              neu.startBeat == expected && neu.startBeat - neu.startNow >= BossMusicDirector.TellBeats - 1e-6 && neu.startBeat - neu.startNow < BossMusicDirector.TellBeats + 1);
        Check(Names[w] + ": the first wave leaves ON that beat (" + neu.firstBeat + ") at full intensity (" + neu.firstIntensity.ToString("0.00") + " >= floor " + row.intensityFloor + ")",
              neu.firstBeat == neu.startBeat && neu.firstIntensity >= row.intensityFloor - 1e-4f);
        Check(Names[w] + ": every wave of the first ten seconds on the eighth-note grid and at least the floor (min " + neu.minIntensity.ToString("0.00") + ")",
              neu.offHalf == 0 && neu.minIntensity >= row.intensityFloor - 1e-4f);
        Check(Names[w] + ": harder from the start than before (" + neu.waves + " waves in the first 10 s vs " + old.waves + ", first at " + neu.firstGame.ToString("0.00") + " s vs " + old.firstGame.ToString("0.00") + " s)",
              neu.waves >= old.waves && neu.firstGame <= old.firstGame + 1e-4f);
        // (less the one-beat tell and the beats an accent takes: a mine field or a bomb is the accent's own firing)
        Check(Names[w] + ": nearly a wave a beat through the first ten seconds (" + neu.waves + " in " + (10f / neu.spb).ToString("0") + " beats)",
              neu.waves >= Mathf.FloorToInt(10f / neu.spb) - 6);
    }

    // ---- 5 the hardened openings stay dodgeable
    static void Fairness(int w)
    {
        var f = Play(w, 30, true, true);
        Check(Names[w] + ": first 30 s on the ladder, " + f.waves + " waves: never a moment with no reachable cell (empty frames " + f.empty + "), every gap >= " + WaveGaps.MinMultiple + " ship widths",
              f.waves > 10 && f.empty == 0 && f.gapFails == 0);
        Check(Names[w] + ": first 30 s on the ladder: every tell is followed by bullets within two beats (" + f.tells + " tells, " + f.unanswered + " unanswered)",
              f.tells > 10 && f.unanswered == 0);
    }

    // ---- 6 the projectile budget over a whole fight
    static void Caps(int w)
    {
        var f = Play(w, BossConfig.FightSeconds, true, false);
        Debug.Log("[DIFFICULTY] TABLE " + Names[w] + " 120 s fight: " + f.waves + " waves, mean intensity " + (f.sumIntensity / Math.Max(1, f.waves)).ToString("0.00") + ", peak " + f.peak + " live shots");
        Check(Names[w] + ": a whole fight on the ladder: peak " + f.peak + " of " + BossConfig.ProjectilePoolMax + ", dropped " + f.dropped + ", " + (f.alloc / Math.Max(1, f.frames)) + " B/frame",
              f.dropped == 0 && f.peak <= BossConfig.ProjectilePoolMax && f.alloc == 0);
    }

    // ---- 7 the wave speed knob
    static void Speed()
    {
        float Measure(bool ladder)
        {
            bool was = BossDifficulty.Enabled;
            BossDifficulty.Enabled = ladder;
            try
            {
                var sim = new PatternSim { cap = 256 };
                var voice = new BossWaveVoice(6, sim, BossWaveSources.For(6, BossCatalog.ForWorld(6), 0));
                voice.EmitMove(WaveMove.Wall(0), .6f, .4, 0f, false);
                return voice.LastSpeed;
            }
            finally { BossDifficulty.Enabled = was; }
        }
        float plain = Measure(false), fast = Measure(true);
        Check("Dune-Eater's walls fall x" + (fast / Mathf.Max(.01f, plain)).ToString("0.000") + " (" + plain.ToString("0.00") + " -> " + fast.ToString("0.00") + " u/s), the table's x" + BossDifficulty.For(6).shotSpeedScale,
              Mathf.Abs(fast / Mathf.Max(.01f, plain) - BossDifficulty.For(6).shotSpeedScale) < .005f);
    }

    // ---- 8 the rotation is the boss's own: the ladder never unlocks an attack early (BossThemedTest's phase structure)
    static void Rotation()
    {
        bool none = true;
        foreach (var f in typeof(BossDifficultyRow).GetFields())
            none &= f.Name.IndexOf("unlock", StringComparison.OrdinalIgnoreCase) < 0 && f.Name.IndexOf("HeadStart", StringComparison.OrdinalIgnoreCase) < 0;
        Check("the ladder has no unlock knob: every boss keeps its phase-1 / phase-2 / phase-3 attacks as authored", none);
    }

    // ---- 9 deterministic
    static void Seeds()
    {
        var a = Play(6, 30, true, false);
        var b = Play(6, 30, true, false);
        Check("Dune-Eater: the same 30 s twice gives the same " + a.waves + " waves", a.waves == b.waves && a.hash == b.hash && a.peak == b.peak);
    }
}
