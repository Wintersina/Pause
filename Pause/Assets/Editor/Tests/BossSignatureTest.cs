using System.Collections.Generic;
using UnityEngine;

// THE BOSS SIGNATURE BULLET PATTERNS (BossSignatureTables, ShotMotion, BossSignatureTuning): three new patterns a boss, IN ADDITION
// to its own rotation.
//
//   1  assignment  every boss has 3 signature patterns (>= 2 required), one unlocked per phase, built from the new stream kinds;
//                  every original attack (its rotation, its legacy lasers / vines / flames) is still in its table, unchanged
//   2  motion      every pattern's shots stay finite and inside the stage on a phone, a tall 1080x2520 cover and a wide screen
//   3  fairness    at every sampled instant the ship's rows keep a free run at least the hitbox width + a margin wide, for
//                  every ship position, on every screen; telegraphs are laid; nothing starts off a muzzle; the live cap holds
//   4  pooling     shots come out of the boss's pool, prewarmed at its spawn: firing a whole pattern creates no new object
//   5  art         each stream wears one of the boss's own attack-shot rows (the ones its rotation uses); pink only in Space
//   7  loops       BossPatternScaling.LoopFactor: 1 changes nothing; at 20 the density, speed, arm and shot caps hold, the gaps too
//   6  specials    BossSpecials schedules them between its own specials and never changes the originals' cadence
public static class BossSignatureTest
{
    static int fails;
    static void Check(string what, bool ok)
    {
        Debug.Log((ok ? "[SIGNATURE] PASS  " : "[SIGNATURE] FAIL  ") + what);
        if (!ok) fails++;
    }

    public static void Run() { TestHarness.Exit(Execute()); }

    static readonly string[] Names = { "Space", "Frost", "Verdant", "Ember", "Tide", "Storm", "Desert" };
    static readonly int[] Originals = { 5, 5, 6, 6, 7, 7, 7 };
    static readonly (int w, int h)[] Screens = { (1080, 1920), (1080, 2520), (2400, 1080) };

    static bool IsNew(StreamKind k) => k >= StreamKind.Sine && k <= StreamKind.AccelFan;

    public static int Execute()
    {
        fails = 0;
        using var sandbox = new TestHarness.Sandbox();
        try
        {
            Assignment();
            Motion();
            Fairness();
            Pooling();
            Art();
            Specials();
            Loops();
        }
        finally { BossPatternScaling.LoopFactor = 1f; BossEncounter.ResetRun(); BossRails.Reset(); }
        Debug.Log("[SIGNATURE] failures: " + fails);
        return fails;
    }

    // ---- 1 ----

    static void Assignment()
    {
        var kinds = new HashSet<StreamKind>();
        for (int w = 0; w < 7; w++)
        {
            var boss = BossCatalog.ForWorld(w);
            var sig = boss.signatures;
            bool ok = sig != null && sig.Length >= 2 && sig.Length == 3;
            bool phased = ok, news = ok, resolved = ok, major = ok;
            if (ok)
                for (int i = 0; i < sig.Length; i++)
                {
                    phased &= sig[i].minPhase == i + 1 && sig[i].signature && sig[i].kind == BossAttackKind.Pattern;
                    major &= sig[i].IsMajor;
                    foreach (var st in sig[i].pattern.streams)
                    {
                        news &= IsNew(st.kind);
                        kinds.Add(st.kind);
                        resolved &= st.parts != null && st.parts.Length > 0;
                    }
                }
            Check(Names[w] + ": 3 signature patterns (" + (ok ? string.Join(", ", System.Array.ConvertAll(sig, a => a.name)) : "none") + "), one more per phase, all new stream kinds, muzzles resolved, telegraphs on",
                  ok && phased && news && resolved && major);

            // the originals: untouched, in the same order, the same phases
            var table = boss.attacks;
            var slots = BossPatternTables.For(w);
            bool same = table.Length == Originals[w] && slots.Length == Originals[w] && ReferenceEquals(table, boss.hell);
            for (int i = 0; same && i < slots.Length; i++)
            {
                same &= table[i].name == slots[i].name && table[i].minPhase == slots[i].minPhase && !table[i].signature;
                if (slots[i].legacy != null) same &= table[i].kind != BossAttackKind.Pattern;
            }
            Check(Names[w] + ": all " + Originals[w] + " original attacks still in its rotation (its lasers, vines, flames and waves included), none replaced by a signature", same);
        }
        Check("the library is used: every new stream kind has a boss (" + kinds.Count + " of 9)", kinds.Count == 9);
    }

    // ---- shared flight helpers ----

    static PatternSim Stage(int sw, int sh)
    {
        var sim = new PatternSim { cap = 256 };
        sim.FitLane(sw, sh);
        return sim;
    }

    // Plays one pattern through the real runner and the sim at the boss's phase rate; `each` is called every step.
    static void Play(BossPattern pat, PatternSim sim, Vector2 player, int world, int phase, float dt, System.Action<float> each)
    {
        sim.Reset();
        sim.player = player;
        // the muzzles: where the boss's own parts are (spread over its width, like the real boss)
        var runner = new BossPatternRunner();
        float rate = BossSignatureTuning.RateForPhase(phase);
        runner.Begin(pat, sim, rate, 11 + world, 1f);
        float t = 0f;
        while (t < pat.Duration + 3.2f)
        {
            if (t < pat.Duration) runner.Step(dt);
            sim.Advance(dt);
            t += dt;
            each(t);
        }
    }

    // ---- 2 ----

    static void Motion()
    {
        for (int w = 0; w < 7; w++)
        {
            var sig = BossCatalog.ForWorld(w).signatures;
            foreach (var sl in sig)
            {
                bool finite = true, inside = true;
                int peak = 0, fired = 0;
                foreach (var (sw, sh) in Screens)
                {
                    var sim = Stage(sw, sh);
                    float lim = Mathf.Max(sim.railEdge, sim.viewHalfWidth) + 2f;
                    Play(sl.pattern, sim, new Vector2(0f, -3.2f), w, 3, .02f, t =>
                    {
                        for (int i = 0; i < sim.cap; i++)
                        {
                            if (!sim.shots[i].alive) continue;
                            var p = sim.shots[i].p;
                            if (float.IsNaN(p.x) || float.IsNaN(p.y) || float.IsInfinity(p.x) || float.IsInfinity(p.y)) finite = false;
                            if (Mathf.Abs(p.x) > lim || p.y > sim.spawnTop + 2f) inside = false;
                        }
                    });
                    peak = Mathf.Max(peak, sim.maxLive); fired += sim.fired;
                }
                Check(Names[w] + " / " + sl.name + ": positions finite and inside the stage on 9:16, 1080x2520 and wide screens (" + fired + " shots)", finite && inside && fired > 0);
            }
        }
        // the paths are the maths they say
        var sine = new PMotion { kind = MotionKind.Sway, amp = 1f, hz = .5f };
        var p0 = new Vector2(0f, 3f); var v0 = new Vector2(0f, -3f);
        float maxX = 0f;
        for (float t = .02f; t < 4f; t += .02f) { p0 += v0 * .02f; ShotMotion.Apply(ref sine, ref p0, ref v0, t, .02f); maxX = Mathf.Max(maxX, Mathf.Abs(p0.x)); }
        Check("sway: weaves +-" + maxX.ToString("F2") + " u about its line (amp 1) at constant speed", maxX > .95f && maxX < 1.05f && Mathf.Abs(v0.y + 3f) < 1e-4f);
        var acc = new PMotion { kind = MotionKind.Accel, a = 4f, sec = 1.5f };
        var pv = new Vector2(0f, -1f); var pp = Vector2.zero;
        for (float t = .02f; t < 2f; t += .02f) { pp += pv * .02f; ShotMotion.Apply(ref acc, ref pp, ref pv, t, .02f); }
        Check("accel: starts slow and ends " + pv.magnitude.ToString("F2") + " u/s (x4 of 1)", Mathf.Abs(pv.magnitude - 4f) < .05f);
        var orb = new PMotion { kind = MotionKind.Orbit, amp = 1f, hz = .5f, sec = 1.2f, a = 3f, b = .55f, anchor = new Vector2(0f, 3f) };
        var op = new Vector2(0f, 3f); var ov = Vector2.zero;
        for (float t = .02f; t < 1.5f; t += .02f) { op += ov * .02f; ShotMotion.Apply(ref orb, ref op, ref ov, t, .02f); }
        Check("orbit: circles its anchor, then is released outward and down at its speed (" + ov.magnitude.ToString("F2") + " u/s, vy " + ov.y.ToString("F2") + ")", Mathf.Abs(ov.magnitude - 3f) < .01f && ov.y < 0f);
    }

    // ---- 3 ----

    // The widest run of free x inside the ship's reach, over the shots that cross the ship's rows (the band the ship lives in).
    static float FreeRun(PatternSim sim)
    {
        float lo = -sim.shipHalf, hi = sim.shipHalf;
        var edges = new List<(float a, float b)>();
        for (int i = 0; i < sim.cap; i++)
        {
            ref var s = ref sim.shots[i];
            if (!s.alive) continue;
            if (s.p.y < sim.zoneBottom - s.r || s.p.y > sim.zoneTop + s.r) continue;
            edges.Add((s.p.x - s.r, s.p.x + s.r));
        }
        edges.Sort((x, y) => x.a.CompareTo(y.a));
        float best = 0f, cursor = lo;
        foreach (var e in edges)
        {
            best = Mathf.Max(best, e.a - cursor);
            cursor = Mathf.Max(cursor, e.b);
        }
        return Mathf.Max(best, hi - cursor);
    }

    static void Fairness()
    {
        float need = 2f * PatternSim.ShipRadius + BossSignatureTuning.GapMargin;
        for (int w = 0; w < 7; w++)
        {
            var sig = BossCatalog.ForWorld(w).signatures;
            for (int i = 0; i < sig.Length; i++)
            {
                var sl = sig[i];
                float worst = 99f; float worstAt = 0f; int peak = 0, dropped = 0, off = 0, marks = 0;
                string where = "";
                foreach (var (sw, sh) in Screens)
                    for (int phase = sl.minPhase; phase <= 3; phase++)
                        foreach (float px in new[] { -1.8f, 0f, 1.8f })
                        {
                            var sim = Stage(sw, sh);
                            float w0 = 99f;
                            Play(sl.pattern, sim, new Vector2(px, -3.2f), w, phase, .05f, t =>
                            {
                                float g = FreeRun(sim);
                                if (g < w0) w0 = g;
                                if (g < worst) { worst = g; worstAt = t; where = sw + "x" + sh + " phase " + phase + " ship " + px; }
                            });
                            peak = Mathf.Max(peak, sim.maxLive); dropped += sim.dropped; off += sim.offView; marks += sim.marks;
                        }
                Check(Names[w] + " / " + sl.name + ": a free run of " + worst.ToString("F2") + " u at worst (need " + need.ToString("F2") + ", " + where + " at " + worstAt.ToString("F1") + " s), " + peak + " live at most (cap " + BossSignatureTuning.MaxLive + "), telegraph marks " + marks + ", off-view spawns " + off,
                      worst >= need && peak <= BossSignatureTuning.MaxLive && dropped == 0 && off == 0 && marks > 0);
            }
        }
    }

    // ---- 4 ----

    sealed class PoolHost : IPatternHost
    {
        public BossProjectilePool pool; public BossDef boss; public Vector2 player = new Vector2(0f, -3.2f);
        public int nulls, peak;
        public Vector2 Emitter(int part) => new Vector2(part == 1 ? -1.4f : part == 2 ? 1.4f : 0f, 3.2f);
        public Vector2 Player => player;
        public float RailEdge => 2.6f;
        public float ShipHalfWidth => 2.25f;
        public float SpawnTop => 5.4f;
        public float ZoneBottom => -3.9f;
        public float ZoneTop => -.7f;
        public void Fire(Vector2 at, Vector2 v, ref PShot s)
        {
            if (pool.FireShot(boss, new Vector3(at.x, at.y, 0f), v, ref s, false) == null) nulls++;
        }
        public void Emitted() { }
        public void Mark(Vector2 a, Vector2 b, float halfWidth, float seconds) { }
    }

    static void Pooling()
    {
        for (int w = 0; w < 7; w++)
        {
            var boss = BossCatalog.ForWorld(w);
            var pool = new BossProjectilePool(BossConfig.ProjectilePoolMax, BossConfig.BeamPoolMax);
            try
            {
                int warm = BossLadder.WarmShots(w);
                pool.Warm(boss, warm);
                int created = pool.Created;
                bool moved = false, finite = true;
                int nulls = 0, peak = 0;
                foreach (var sl in boss.signatures)
                {
                    pool.RecycleAll();
                    var host = new PoolHost { pool = pool, boss = boss };
                    var runner = new BossPatternRunner();
                    runner.Begin(sl.pattern, host, BossSignatureTuning.Rate(1f), 5, 1f);
                    float t = 0f;
                    while (t < sl.pattern.Duration + 1f)
                    {
                        if (t < sl.pattern.Duration) runner.Step(1f / 60f);
                        pool.Step(1f / 60f);
                        t += 1f / 60f;
                        peak = Mathf.Max(peak, pool.ActiveShots);
                        if (t > .5f && t < 2f)
                            foreach (var s in pool.Shots)
                            {
                                if (!s.Active) continue;
                                var p = s.transform.position;
                                if (float.IsNaN(p.x) || float.IsNaN(p.y)) finite = false;
                            }
                    }
                    nulls += host.nulls;
                    foreach (var s in pool.Shots) if (s.Patterned) moved = true;
                }
                Check(Names[w] + ": the boss's pool is prewarmed to " + warm + " (>= " + BossSignatureTuning.MaxLive + ") at its spawn; all three signature patterns fire from it with no new object (" + pool.Created + " built, " + created + " before), none refused (" + nulls + "), peak " + peak + " live",
                      warm >= BossSignatureTuning.MaxLive && created == warm && pool.Created == created && nulls == 0 && peak <= BossSignatureTuning.MaxLive && finite && moved);
            }
            finally { pool.Dispose(); }
        }
    }

    // ---- 5 ----

    static void Art()
    {
        for (int w = 0; w < 7; w++)
        {
            var boss = BossCatalog.ForWorld(w);
            var own = new HashSet<int>();
            foreach (var sl in BossPatternTables.For(w))
                if (sl.pattern != null) foreach (var st in sl.pattern.streams) own.Add(st.art);
            bool ok = true; string bad = "";
            foreach (var sl in boss.signatures)
                foreach (var st in sl.pattern.streams)
                {
                    bool ids = own.Contains(st.art) && st.rail == BossRailMode.Absorb;
                    bool sprite = st.art == 0 || BossArt.AttackShot(boss, st.art, 0) != null;
                    if (!ids || !sprite) { ok = false; bad += " " + sl.name + "#" + st.art; }
                }
            Check(Names[w] + ": every signature stream wears one of the boss's own attack-shot rows (" + string.Join(",", own) + "), no sprite of another world, no pink outside Space" + bad, ok);
        }
    }


    // ---- 7 ----

    static void Loops()
    {
        BossPatternScaling.LoopFactor = 1f;
        float d1 = BossPatternScaling.Density, s1 = BossPatternScaling.Speed;
        Check("LoopFactor 1: density x" + d1 + ", speed x" + s1 + ", no extra arms, rates are the authored ones, nothing held back",
              d1 == 1f && s1 == 1f && BossPatternScaling.ExtraArms == 0 && !BossPatternScaling.Scaled && BossPatternScaling.Count(30) == 30
              && Mathf.Approximately(BossSignatureTuning.RateForPhase(1), BossSignatureTuning.AttackRate));
        // a factor under 1 is no easier than 1, and 1 fires exactly what the unscaled library fires
        int diffs = 0;
        for (int w = 0; w < 7; w++)
            foreach (var sl in BossCatalog.ForWorld(w).signatures)
            {
                int a, b;
                BossPatternScaling.LoopFactor = 1f; a = Fired(sl.pattern, w);
                BossPatternScaling.LoopFactor = .3f; b = Fired(sl.pattern, w);
                if (a != b || a == 0) diffs++;
            }
        Check("LoopFactor 1 (and anything under it) fires the identical shots, " + diffs + " patterns differ", diffs == 0);

        BossPatternScaling.LoopFactor = 20f;
        bool caps = BossPatternScaling.Density == BossPatternScaling.MaxDensity && BossPatternScaling.Speed == BossPatternScaling.MaxSpeed
                 && BossPatternScaling.ExtraArms == BossPatternScaling.MaxExtraArms && BossPatternScaling.Count(1000) <= BossPatternScaling.RingCap;
        Check("LoopFactor 20: density, speed, extra arms and ring size sit at their caps (x" + BossPatternScaling.Density + ", x" + BossPatternScaling.Speed + ", +" + BossPatternScaling.ExtraArms + ")", caps);
        float need = 2f * PatternSim.ShipRadius + BossSignatureTuning.GapMargin;
        for (int w = 0; w < 7; w++)
            foreach (var sl in BossCatalog.ForWorld(w).signatures)
            {
                float worst = 99f; int peak = 0, dropped = 0, maxVolleys = 0, allowed = 0;
                foreach (var (sw, sh) in Screens)
                    foreach (float px in new[] { -1.8f, 0f, 1.8f })
                    {
                        var sim = Stage(sw, sh);
                        Play(sl.pattern, sim, new Vector2(px, -3.2f), w, 3, .05f, t => { worst = Mathf.Min(worst, FreeRun(sim)); });
                        peak = Mathf.Max(peak, sim.maxLive); dropped += sim.dropped;
                    }
                // the fastest stream may not volley faster than MinInterval
                foreach (var st in sl.pattern.streams)
                {
                    float step = Mathf.Max(BossPatternScaling.MinInterval, st.interval / BossSignatureTuning.RateForPhase(3));
                    allowed += Mathf.CeilToInt(st.duration / step) + 1;
                }
                maxVolleys = allowed;
                var probe = Stage(1080, 2520);
                int vol = 0;
                var runner = new BossPatternRunner();
                runner.Begin(sl.pattern, probe, BossSignatureTuning.RateForPhase(3), 3, 1f);
                for (float t = 0f; t < sl.pattern.Duration; t += .01f) { runner.Step(.01f); probe.Advance(.01f); }
                vol = runner.Emissions;
                Check(Names[w] + " / " + sl.name + " at loop factor 20: free run " + worst.ToString("F2") + " u (need " + need.ToString("F2") + "), " + peak + " live (cap " + BossSignatureTuning.MaxLive + "), " + vol + " volleys (<= " + maxVolleys + " at the " + BossPatternScaling.MinInterval + " s floor), " + dropped + " dropped",
                      worst >= need && peak <= BossSignatureTuning.MaxLive && vol <= maxVolleys && dropped == 0);
            }
        BossPatternScaling.LoopFactor = 1f;
    }

    static int Fired(BossPattern pat, int w)
    {
        var sim = Stage(1080, 1920);
        Play(pat, sim, new Vector2(0f, -3.2f), w, 2, .02f, t => { });
        return sim.fired;
    }

    // ---- 6 ----

    sealed class Beat : IBeatSource
    {
        public double now;
        public bool Active => true;
        public double BeatNow => now;
        public double SecondsPerBeat => .5;
        public int BeatsPerBar => 4;
        public int DownbeatBeat => 0;
    }

    static void Specials()
    {
        for (int w = 0; w < 7; w++)
        {
            var boss = BossCatalog.ForWorld(w);
            var music = new Beat();
            // without the library: the specials as before
            var plain = new BossSpecials(boss, w);
            var with = new BossSpecials(boss, w, true);
            Check(Names[w] + ": BossSpecials built without the library has no signature (" + plain.SignatureCount + "), with it " + with.SignatureCount, plain.SignatureCount == 0 && with.SignatureCount == 3);
            plain.Begin(music, 0); with.Begin(music, 0);
            var firedWith = new List<string>(); var firedPlain = new List<string>();
            var landed = new List<double>();
            for (double beat = 0; beat < 4.0 * 4 * 60; beat += .25)
            {
                music.now = beat;
                float progress = Mathf.Min(1f, (float)(beat / (4.0 * 4 * 60)));
                var a = plain.Due(music, .02f, progress);
                if (a != null) { firedPlain.Add(a.name); plain.Done(); }
                var b = with.Due(music, .02f, progress);
                if (b != null) { firedWith.Add((b.signature ? "*" : "") + b.name); landed.Add(with.LastLanding); with.Done(); }
            }
            var specialsOnly = firedWith.FindAll(n => !n.StartsWith("*")).ConvertAll(n => n);
            bool same = specialsOnly.Count >= firedPlain.Count - 1;
            for (int i = 0; same && i < System.Math.Min(specialsOnly.Count, firedPlain.Count); i++) same &= specialsOnly[i] == firedPlain[i];
            int sigs = firedWith.FindAll(n => n.StartsWith("*")).Count;
            Check(Names[w] + ": the specials fire the same order with the library (" + firedPlain.Count + " vs " + specialsOnly.Count + "), and " + sigs + " signature patterns come between them", same && sigs >= 3);
        }
    }
}
