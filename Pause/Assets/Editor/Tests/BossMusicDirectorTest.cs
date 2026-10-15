using System;
using UnityEngine;

public static class BossMusicDirectorTest
{
    static int fails;
    static readonly string[] Names = { "Space", "Frost", "Verdant", "Ember", "Tide", "Storm", "Desert" };
    static void Check(string what, bool ok) { Debug.Log("[MUSIC] " + (ok ? "PASS " : "FAIL ") + what); if (!ok) fails++; }
    public static void Run() { TestHarness.Exit(Execute()); }
    public static int Execute()
    {
        fails = 0;
        var oldNow = BeatClock.Now;
        try
        {
            Maps();
            foreach (int world in new[] { 0, 1, 2, 3, 4, 5, 6 }) WholeSong(world);
            Dynamics();
            Actor();
        }
        finally { BeatClock.Now = oldNow; }
        Debug.Log("[MUSIC] failures: " + fails);
        return fails;
    }

    static void Maps()
    {
        foreach (string name in Names)
        {
            var map = BeatMaps.For(name);
            bool valid = map.analysisVersion == 1 && map.energy != null && map.energy.Length > 200 &&
                map.bass.Length == map.energy.Length && map.accents.Length == map.energy.Length;
            if (valid) foreach (var values in new[] { map.energy, map.bass, map.accents })
                foreach (var value in values) if (float.IsNaN(value) || value < 0 || value > 1) valid = false;
            Check(name + " has bounded energy, bass and accent measurements", valid);
            bool sections = map.sections != null && map.sections.Length > 10 && map.sections[0].beat == 0;
            if (sections) for (int i = 1; i < map.sections.Length; i++)
                if (map.sections[i].beat <= map.sections[i-1].beat || map.sections[i].beat >= map.energy.Length) sections = false;
            Check(name + " has an ordered arrangement across the song", sections);
        }
    }

    static void WholeSong(int world)
    {
        var map = BeatMaps.For(Names[world]);
        double dsp = 0;
        BeatClock.Now = () => dsp;
        var clock = new BeatClock(map) { UseLatencyOverride = true };
        clock.Sample(0, 0, true);
        var sim = new PatternSim { cap = BossConfig.ProjectilePoolMax };
        var director = new BossMusicDirector(world, BossCatalog.ForWorld(world), sim);
        bool synced = true, pauseSilent = true;
        double lastShot = -1, maxGap = 0;
        int pauses = 0;
        // Whole song plus a wrap; variable frame rates, repeated freezes and slow motion.
        for (int f = 0; dsp < map.duration + 8; f++)
        {
            float realDt = f % 3 == 0 ? 1f / 30f : 1f / 60f;
            dsp += realDt;
            clock.Sample(dsp, dsp % map.duration, true);
            double cycle = dsp % 20;
            bool paused = cycle >= 10 && cycle < 12;
            float dt = paused ? 0 : realDt * (cycle > 15 ? .06f : 1f);
            sim.player = new Vector2(Mathf.Sin((float)dsp * .3f) * 1.8f, -3.2f);
            sim.Advance(dt);
            int before = director.Emissions;
            director.Step(clock, dt, realDt, .8f);
            if (paused) { pauseSilent &= director.Emissions == before; lastShot = -1; pauses++; }
            if (director.Emissions != before)
            {
                synced &= director.LastLateBeats * clock.SecondsPerBeat <= realDt + .0001;
                if (lastShot >= 0) maxGap = Math.Max(maxGap, dsp - lastShot);
                lastShot = dsp;
            }
        }
        Check(Names[world] + " fires continuously through phrases and the loop (max gap " + maxGap.ToString("F3") + "s)", maxGap < .65);
        Check(Names[world] + " all emissions stay within one rendered frame of the music", synced);
        Check(Names[world] + " freezes without firing and resumes without a backlog", pauseSilent && pauses > 0);
        Check(Names[world] + " peak " + sim.maxLive + ", dropped " + sim.dropped + ", total " + sim.fired + " shots", sim.dropped == 0 && sim.maxLive <= sim.cap && sim.fired > 200);
        Check(Names[world] + " evolves across phrases", director.PhraseChanges > 10);
    }

    sealed class ManualMusic : IMusicBeatSource
    {
        public bool Active { get; set; } = true;
        public double BeatNow { get; set; }
        public double SecondsPerBeat => .4;
        public int BeatsPerBar => 4;
        public int DownbeatBeat => 0;
        public MusicMoment MusicNow { get; set; }
        public bool ForecastDrop;
        public MusicMoment MusicAhead(double beats) => ForecastDrop && BeatNow + beats >= 8
            ? new MusicMoment { energy = 1, bass = 1, kind = "drop", section = 1 } : MusicNow;
    }

    static int Density(float energy, string section)
    {
        var sim = new PatternSim { cap = 144 };
        var source = new ManualMusic { MusicNow = new MusicMoment { energy = energy, bass = energy, accent = energy, kind = section } };
        var director = new BossMusicDirector(4, BossCatalog.ForWorld(4), sim);
        for (int f = 0; f < 60 * 24; f++)
        {
            source.BeatNow = f / 60.0 / .4;
            sim.Advance(1f / 60f);
            director.Step(source, 1f / 60f, 1f / 60f, .5f);
        }
        return sim.fired;
    }

    static void Dynamics()
    {
        int quiet = Density(0, "breakdown"), build = Density(.5f, "build"), drop = Density(1, "drop");
        Check("breakdown/build/drop intensify (" + quiet + "/" + build + "/" + drop + ")", quiet > 40 && build > quiet && drop > build);
        var host = new PatternSim();
        var music = new ManualMusic { MusicNow = new MusicMoment { energy = 1, bass = 1, kind = "peak" } };
        var run = new BossMusicDirector(3, BossCatalog.ForWorld(3), host);
        for (int f = 0; f < 60 * 3; f++) { music.BeatNow = f / 60.0 / .4; run.Step(music, 1f / 60f, 1f / 60f, 1); }
        int before = host.fired;
        music.BeatNow = 21.37;
        run.Step(music, 1f / 60f, 1f / 60f, 1);
        Check("resume off-grid does not replay a pending bomb accent", host.fired == before);
        music.Active = false;
        run.Step(music, 1f / 60f, 1f / 60f, 1);
        Check("inactive music emits nothing", host.fired == before);

        var forecastHost = new PatternSim();
        var forecast = new ManualMusic { ForecastDrop = true,
            MusicNow = new MusicMoment { energy = .1f, bass = .1f, kind = "breakdown" } };
        var forecastRun = new BossMusicDirector(3, BossCatalog.ForWorld(3), forecastHost);
        int warned = 0;
        for (int f = 0; f <= 8 * 24 + 1; f++)
        {
            forecast.BeatNow = f / 24.0;
            if (forecast.BeatNow >= 8) forecast.MusicNow = forecast.MusicAhead(0);
            int marks = forecastHost.marks;
            forecastRun.Step(forecast, 1f / 60f, 1f / 60f, 0);
            if (forecast.BeatNow > 5.9 && forecastHost.marks > marks) warned++;
        }
        Check("quiet-to-drop accent is warned ahead and lands on beat 8", warned == 1 && forecastRun.AccentEmissions == 1);

    }

    static void Actor()
    {
        using var sandbox = new TestHarness.Sandbox();
        var step = typeof(BossActor).GetMethod("StepAttacks", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var actor = BossActor.Spawn(BossCatalog.ForWorld(0));
        var pool = new BossProjectilePool(144, 6);
        var music = new ManualMusic { MusicNow = new MusicMoment { energy = .7f, bass = .7f, kind = "drive" } };
        try
        {
            actor.BeginFight();
            bool continuous = true;
            for (int f = 0; f < 60 * 20; f++)
            {
                music.BeatNow = f / 60.0 / .4;
                step.Invoke(actor, new object[] { 1f / 60f, 1f / 60f, new Vector3(0, -3.2f), .8f, pool, music });
                if (f > 120 && !actor.PatternPhase) continuous = false;
                pool.RecycleAll();
            }
            Check("live actor uses the musical director without timed cooldowns", continuous && actor.MusicDirector.Emissions > 50 && actor.AttacksStarted == 1);
            actor.Markers.Clear(); // the scheduler-only fixture above does not tick marker lifetimes
            actor.Markers.Add(Vector2.zero, Vector2.one, .1f, .4f);
            actor.StepFight(0, .5f, Vector3.zero, .8f, pool);
            Check("musical warning expires with audio time during a gameplay pause", actor.Markers.Live == 0);
            actor.BeginOutro(false);
            Check("outro stops the director", !actor.MusicDirector.Preparing);
        }
        finally { pool.Dispose(); UnityEngine.Object.DestroyImmediate(actor.gameObject); }
    }
}
