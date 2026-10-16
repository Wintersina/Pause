using System;
using UnityEngine;

public interface IMusicPatternHost : IPatternHost
{
    int ActiveMusicShots { get; }
    int MusicShotCapacity { get; }
}

// A continuous musical arrangement: gap-guaranteed WAVES (BossWaves.cs) on the arrangement's rhythm, and forecast accents. The song
// owns every event and transition; the old attack durations/cooldowns are never read.
// Only projectile geometry is shared with BossPatternRunner. No allocation during Step.
public sealed class BossMusicDirector
{
    readonly IPatternHost host;
    readonly IMusicPatternHost budget;
    readonly BossWaveVoice waves;
    readonly bool accentWave;
    readonly BossPatternRunner accent = new BossPatternRunner();
    readonly PatternStream pulseShape, signatureShape, accentShape;
    IBeatSource source;
    public const float MinWaveSpacing = .15f;   // game seconds: closer waves would only stack into a wall
    const int WaveReserve = 40;
    long lastTick = long.MinValue, phrase = long.MinValue;
    float gameClock, lastWaveGame = -10f;
    public BossWaveVoice Waves => waves;
    public int WaveEmissions { get; private set; }
    double startBeat, pendingAccent = double.NaN;
    int section = -1;
    public int Emissions { get; private set; }
    public int PhraseChanges { get; private set; }
    public int AccentEmissions { get; private set; }
    public double LastScheduledBeat { get; private set; }
    public double LastLateBeats { get; private set; }
    public float Intensity { get; private set; }
    public bool Preparing { get; private set; }
    public int Pose { get; private set; }
    public PatternStream Signature => signatureShape;

    public BossMusicDirector(int world, BossDef boss, IPatternHost host)
    {
        this.host = host;
        budget = host as IMusicPatternHost;
        var shapes = BossMusicVoices.For(world, boss);
        pulseShape = shapes[0]; signatureShape = shapes[1]; accentShape = shapes[2];
        waves = new BossWaveVoice(world, host, pulseShape.parts);
        accentWave = accentShape.kind == StreamKind.Lattice;
        accent.Begin(new BossPattern { name = "accent", streams = new[] { accentShape } }, host, 1, 79, 0, prepare: false);
    }

    public void Suspend()
    {
        source = null;
        pendingAccent = double.NaN;
        Preparing = false;
    }

    bool Room(int reserve) => budget == null || budget.ActiveMusicShots <= budget.MusicShotCapacity - reserve;

    public void Step(IBeatSource music, float gameDt, float realDt, float progress)
    {
        if (music == null || !music.Active) { Suspend(); return; }
        if (gameDt <= 0) return; // music continues; missed ticks are skipped on resume
        gameClock += gameDt;
        double now = music.BeatNow;
        if (!ReferenceEquals(source, music))
        {
            source = music;
            startBeat = Math.Ceiling(now) + 2;
            lastTick = (long)Math.Floor(now * 4);
            pendingAccent = double.NaN;
            Preparing = true;
            host.Mark(host.Emitter(pulseShape.parts[0]), host.Player, .08f, (float)((startBeat - now) * music.SecondsPerBeat));
        }
        if (now < startBeat) return;
        Preparing = false;
        long tick = (long)Math.Floor(now * 4 + 1e-8);
        if (tick <= lastTick) return;
        lastTick = tick; // one current subdivision, never replay a backlog
        double scheduled = tick * .25;
        double late = now - scheduled;
        double allowedLate = Math.Max(0, realDt) / music.SecondsPerBeat + 1e-6;
        if (!double.IsNaN(pendingAccent) && now - pendingAccent > allowedLate) pendingAccent = double.NaN;
        if (late > allowedLate) return;

        var moment = music is IMusicBeatSource arranged ? arranged.MusicNow : new MusicMoment { energy = .5f, bass = .5f, accent = .5f, kind = "drive" };
        Intensity = Mathf.Clamp01(moment.energy * .75f + moment.bass * .15f + Mathf.Clamp01(progress) * .1f);
        bool peak = moment.kind == "drop" || moment.kind == "peak";
        long nextPhrase = tick / 64; // four bars, continuous even through clip wraps
        if (nextPhrase != phrase || moment.section != section)
        {
            phrase = nextPhrase; section = moment.section;
            PhraseChanges++;
            Pose = (int)((phrase % 3 + 3) % 3);
            // Phrase variants change which waves come first, never their firing clock.
            waves.ResetPhrase();
        }

        LastScheduledBeat = scheduled;
        LastLateBeats = Math.Max(0, late);
        // WAVES: each spawns on a sixteenth chosen by the arrangement's rhythm; the gap is always reachable.
        int bar = (int)(((tick - 4L * music.DownbeatBeat) % WaveRhythm.TicksPerBar + WaveRhythm.TicksPerBar) % WaveRhythm.TicksPerBar);
        if (((WaveRhythm.Mask(moment.kind, Intensity) >> bar) & 1) != 0 && gameClock - lastWaveGame >= MinWaveSpacing && Room(WaveReserve))
        {
            bool telegraph = Intensity < .75f || waves.Waves % 4 == 0;
            if (waves.Emit(Intensity, music.SecondsPerBeat, gameClock, telegraph) > 0)
            {
                lastWaveGame = gameClock;
                Emissions++; WaveEmissions++;
            }
        }

        // Warn two beats ahead while the other voices keep firing. Re-plan each accent
        // from the current ship position; pause/resume never launches an expired plan.
        var upcoming = music is IMusicBeatSource forecast ? forecast.MusicAhead(2) : moment;
        bool dropAhead = upcoming.kind == "drop" && upcoming.section != moment.section;
        bool regularAccent = tick % 32 == 24 && (Intensity > .48f || peak);
        if (tick % 16 == 8 && (regularAccent || dropAhead) && Room(56))
        {
            pendingAccent = scheduled + 2;
            accentShape.fuse = (float)(4 * music.SecondsPerBeat);
            float warning = (float)(2 * music.SecondsPerBeat);
            accent.PrepareMusical(warning);
            if (accentShape.kind != StreamKind.Bomb && accentShape.kind != StreamKind.Mines)
            {
                var muzzle = host.Emitter(accentShape.parts[0]);
                host.Mark(muzzle, muzzle, .35f, warning);
            }
        }
        if (!double.IsNaN(pendingAccent) && scheduled >= pendingAccent)
        {
            if (Room(48))
            {
                // the Tempest Engine's lattice is a heavy three-row wave: it joins the gap chain like every other wave
                if (accentWave) waves.EmitMove(WaveMove.Wall(0, false, 3), Intensity, music.SecondsPerBeat, gameClock, true);
                else Emit(accent, scheduled);
                AccentEmissions++;
            }
            pendingAccent = double.NaN;
        }
    }

    void Emit(BossPatternRunner voice, double beat)
    {
        voice.EmitMusical((float)beat);
        Emissions++;
    }
}

// Independent musical voices, not a rotation of timed attacks. Projectile speed and
// gap geometry retain each world's identity; duration/interval fields are unused.
public static class BossMusicVoices
{
    public static PatternStream[] For(int world, BossDef boss)
    {
        // Resolve the known first attack's muzzle instead of assuming every atlas names it alike.
        int part = boss.DefaultAttacks[0].parts != null && boss.DefaultAttacks[0].parts.Length > 0 ? boss.DefaultAttacks[0].parts[0] : 0;
        int[] parts = { Mathf.Max(0, part) };
        var p = new PatternStream { kind = StreamKind.Aimed, parts = parts, count = 1, speed = 4.4f + world * .2f, style = BossShotStyle.Bolt };
        var s = new PatternStream { parts = parts, count = 3, speed = 3.4f, style = BossShotStyle.Shard, spread = 35f, gap = 2.4f, gapStep = .18f, pitch = .85f, minSep = 1.7f };
        var a = new PatternStream { kind = StreamKind.Fan, parts = parts, count = 5, speed = 3.6f, spread = 90f, style = BossShotStyle.Shard };
        switch (world)
        {
            case 0: s.kind = StreamKind.Spiral; s.count = 2; s.armDeg = 180; s.spin = 70; s.speed = 3.1f; break;
            case 1: s.kind = StreamKind.Ring; s.count = 10; s.gap = 75; s.spread = 15; s.rotStep = 18; s.speed = 3.5f; break;
            case 2: s.kind = StreamKind.Seeds; s.count = 3; s.homeDeg = 45; s.homeSeconds = .8f; s.speed = 3.3f; break;
            case 3:
                s.kind = StreamKind.Fan; s.count = 5; s.spread = 85; s.amp = 8;
                a.kind = StreamKind.Bomb; a.count = 1; a.minSep = 1.8f; a.burst = 6; a.burstSpeed = 2.7f;
                break;
            case 4: s.kind = StreamKind.Curtain; s.gap = 2.4f; s.speed = 3.4f; break;
            case 5: s.kind = StreamKind.Rain; s.free = 3; s.pitch = .85f; s.speed = 3.7f; s.speedJitter = .4f;
                a.kind = StreamKind.Lattice; a.count = 3; a.pitch = 2.2f; a.angle = 14; break;
            default:
                s.kind = StreamKind.Pincer; s.count = 2; s.side = 1.5f; s.gap = 2.6f; s.spread = 14; s.speed = 5.0f;
                a.kind = StreamKind.Mines; a.count = 4; a.gap = 1.4f; a.minSep = 1.7f; a.burst = 0;
                break;
        }
        return new[] { p, s, a };
    }
}
