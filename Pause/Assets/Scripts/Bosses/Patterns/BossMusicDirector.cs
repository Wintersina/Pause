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
    readonly int world;
    readonly BossWaveVoice waves;
    readonly bool accentWave, accentFan;
    readonly BossPatternRunner accent = new BossPatternRunner();
    readonly PatternStream pulseShape, signatureShape, accentShape;
    IBeatSource source;
    public const float MinWaveSpacing = .15f;   // game seconds: closer waves would only stack into a wall
    public const double AudioStepSeconds = .085; // the coarsest audio clock step a sixteenth may be seen late by
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
    public float RawIntensity { get; private set; }   // the song's own, before BossDifficulty shaped it
    public float FightSeconds => gameClock;           // game seconds the director has run
    // The ramp's clock (BossDifficulty): gameClock as it was on the last bar line the director played. The ramp level
    // only moves on a bar line, so the rhythm a wave was told on (the beat before it) is the rhythm it fires on: a
    // level creeping across a mask's threshold mid-bar would move the bar's waves off the sixteenths already told.
    public float RampClock => rampClock;
    float rampClock;
    public bool Preparing { get; private set; }
    public int Pose { get; private set; }
    public PatternStream Signature => signatureShape;
    // Where the waves leave the boss (its muzzles per shape) and every part they use: the boss's flash / charge slots.
    public BossWaveSources Sources => sources;
    public int[] Parts => sources.all;
    readonly BossWaveSources sources;

    // The TELL of the next wave: on the beat before it leaves (or since the last wave, if that is nearer) the boss shows
    // the tell drawings of the part about to fire and charges its muzzles; TellProgress runs 0..1 into the wave's beat.
    public bool Telling { get; private set; }
    public float TellProgress { get; private set; }
    public int TellPose => tellSource.parts != null ? tellSource.pose : 0;
    public WaveSource TellSource => tellSource;
    public double NextWaveBeat => nextWaveBeat;
    WaveSource tellSource;
    double nextWaveBeat = double.NaN, lastWaveBeat = double.NegativeInfinity;
    public const double TellBeats = 1.0;
    // The first beat the director's waves start on (two beats after the song was first heard; the specials count from it).
    // A boss with BossDifficulty.beatSyncFromStart starts on the first beat that leaves its one-beat tell instead.
    public double StartBeat => startBeat;

    // THE HOLD for a special attack (BossSpecials): from this beat on no wave leaves, no accent is planned and no wave is
    // told, until the owner lifts it (NaN). The clock keeps running underneath (no backlog when it lifts: the next wave
    // is the next rhythm sixteenth). Set by BossActor before every Step.
    public double HoldFromBeat = double.NaN;
    public bool HeldAt(double beat) => !double.IsNaN(HoldFromBeat) && beat >= HoldFromBeat - 1e-9;
    public int HeldWaves { get; private set; }   // waves the hold kept in (tests)
    // The beat the special lands on: every wave fired before the hold must reach the ship's row before it (a slow wave
    // that could not is skipped, BossWaveVoice.MaxTravelBeats). NaN: no limit.
    public double ArriveBeforeBeat = double.NaN;

    public BossMusicDirector(int world, BossDef boss, IPatternHost host)
    {
        this.host = host;
        this.world = world;
        budget = host as IMusicPatternHost;
        var shapes = BossMusicVoices.For(world, boss);
        pulseShape = shapes[0]; signatureShape = shapes[1]; accentShape = shapes[2];
        sources = BossWaveSources.For(world, boss, pulseShape.parts[0]);
        waves = new BossWaveVoice(world, host, sources);
        // the accent (bombs, mines, a fan) leaves its own body part: the Drake's furnace, the Dune-Eater's boom crane
        accentShape.parts = sources.accent.parts;
        accentWave = accentShape.kind == StreamKind.Lattice;
        // a plain accent fan joins the gap chain too: it is a fan wave out of the accent's muzzle with a guaranteed gap
        accentFan = accentShape.kind == StreamKind.Fan;
        accent.Begin(new BossPattern { name = "accent", streams = new[] { accentShape } }, host, 1, 79, 0, prepare: false);
        accent.AnnounceEmits = false;
        tellSource = sources.wall;
    }

    // Slots (indices into `parts`, the boss's current attack parts) of the muzzles charging for the next wave.
    public int TellMask(int[] parts)
    {
        if (!Telling || parts == null || tellSource.parts == null) return 0;
        int m = 0;
        for (int i = 0; i < parts.Length && i < 31; i++)
            for (int j = 0; j < tellSource.parts.Length; j++) if (parts[i] == tellSource.parts[j]) m |= 1 << i;
        return m;
    }

    public void Suspend()
    {
        source = null;
        pendingAccent = double.NaN;
        Preparing = false;
        Telling = false;
        nextWaveBeat = double.NaN;
        HoldFromBeat = double.NaN;
        ArriveBeforeBeat = double.NaN;
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
            // the initial musical warning: two beats of calm after the song is first heard, or (the last bosses, hard from
            // the first bar) only the one-beat tell: the first wave leaves on the first beat at least TellBeats away
            startBeat = BossDifficulty.SyncFromStart(world) ? Math.Ceiling(now + TellBeats - 1e-6) : Math.Ceiling(now) + 2;
            lastTick = (long)Math.Floor(now * 4);
            pendingAccent = double.NaN;
            Preparing = true;
            host.Mark(host.Emitter(pulseShape.parts[0]), host.Player, .08f, (float)((startBeat - now) * music.SecondsPerBeat));
        }
        if (now < startBeat) { Forecast(music, now, (long)Math.Floor(now * 4 + 1e-8)); return; }
        Preparing = false;
        long tick = (long)Math.Floor(now * 4 + 1e-8);
        if (tick <= lastTick) { UpdateTell(now); return; }
        lastTick = tick; // one current subdivision, never replay a backlog
        double scheduled = tick * .25;
        double late = now - scheduled;
        // A sixteenth is seen the first frame the clock is past it. The clock moves in AUDIO-BUFFER steps (dspTime:
        // 20..45 ms on phones), which can be longer than a frame (8 ms at 120 Hz), so "late by more than a frame" is
        // not a missed beat: a wave seen up to AudioStepSeconds late still fires, with its flight shortened by the
        // lateness so it lands on the beat. (Only one current sixteenth is ever played: no backlog after a pause.)
        double allowedLate = Math.Max(Math.Max(0, realDt), AudioStepSeconds) / music.SecondsPerBeat + 1e-6;
        if (!double.IsNaN(pendingAccent) && now - pendingAccent > allowedLate) pendingAccent = double.NaN;
        if (late > allowedLate) return;

        var moment = music is IMusicBeatSource arranged ? arranged.MusicNow : new MusicMoment { energy = .5f, bass = .5f, accent = .5f, kind = "drive" };
        // the song's intensity, shaped by the boss's place on the difficulty ladder (ramp-up, floor: BossDifficulty)
        RawIntensity = Mathf.Clamp01(moment.energy * .75f + moment.bass * .15f + Mathf.Clamp01(progress) * .1f);
        if ((((tick - 4L * music.DownbeatBeat) % WaveRhythm.TicksPerBar) + WaveRhythm.TicksPerBar) % WaveRhythm.TicksPerBar == 0) rampClock = gameClock;
        Intensity = BossDifficulty.Shape(world, RawIntensity, rampClock);
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
        // a special's hold (BossSpecials): nothing leaves from here on, and an accent planned for inside it is dropped
        bool held = HeldAt(scheduled);
        if (held && !double.IsNaN(pendingAccent)) pendingAccent = double.NaN;
        // the accent landing on this sixteenth IS its wave (one firing per sixteenth: one part, one drawing)
        bool accentHere = !double.IsNaN(pendingAccent) && scheduled >= pendingAccent && Room(48);
        bool waveHere = ((WaveRhythm.Mask(moment.kind, Intensity) >> bar) & 1) != 0;
        if (held && waveHere) HeldWaves++;
        // (a wave leaving on this sixteenth arrives k beats on: it must be down before the special lands, a quarter beat clear)
        waves.MaxTravelBeats = double.IsNaN(ArriveBeforeBeat) ? int.MaxValue : (int)Math.Floor(ArriveBeforeBeat - scheduled - .25 + 1e-9);
        if (!accentHere && !held && waveHere && gameClock - lastWaveGame >= MinWaveSpacing && Room(WaveReserve))
        {
            bool telegraph = Intensity < .75f || waves.Waves % 4 == 0;
            // a held breath never runs into the next wave's drawing
            int mask = WaveRhythm.Mask(moment.kind, Intensity), gapTicks = 1;
            while (gapTicks < WaveRhythm.TicksPerBar && ((mask >> ((bar + gapTicks) % WaveRhythm.TicksPerBar)) & 1) == 0) gapTicks++;
            float holdCap = (float)(gapTicks * .25 * music.SecondsPerBeat * .85);
            if (waves.Emit(Intensity, music.SecondsPerBeat, gameClock, telegraph, (float)(Math.Max(0, late) * music.SecondsPerBeat), holdCap) > 0)
            {
                lastWaveGame = gameClock;
                lastWaveBeat = scheduled;
                Emissions++; WaveEmissions++;
            }
        }
        Forecast(music, now, tick);

        // Warn two beats ahead while the other voices keep firing. Re-plan each accent
        // from the current ship position; pause/resume never launches an expired plan.
        var upcoming = music is IMusicBeatSource forecast ? forecast.MusicAhead(2) : moment;
        bool dropAhead = upcoming.kind == "drop" && upcoming.section != moment.section;
        bool regularAccent = tick % 32 == 24 && (Intensity > .48f || peak);
        // (not into a hold: an accent lands two beats on and its bombs burst up to four beats later, all before the hold)
        if (tick % 16 == 8 && (regularAccent || dropAhead) && Room(56) && !HeldAt(scheduled + 2 + 4))
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
                if (accentWave || accentFan)
                {
                    var heavy = accentWave ? WaveMove.Wall(0, false, 3) : WaveMove.Fan(0, 60f, 20);
                    if (waves.EmitMove(heavy, Intensity, music.SecondsPerBeat, gameClock, true, (float)(Math.Max(0, late) * music.SecondsPerBeat)) > 0)
                    {
                        lastWaveGame = gameClock; lastWaveBeat = scheduled;
                        Emissions++;
                    }
                }
                else
                {
                    Emit(accent, scheduled);
                    for (int i = 0; i < sources.accent.parts.Length; i++) host.EmittedFrom(sources.accent.parts[i], 0f);
                }
                AccentEmissions++;
            }
            pendingAccent = double.NaN;
        }
    }

    // Finds the next sixteenth (within a bar) the rhythm spawns a wave on and the body part it will leave from, so the
    // boss can tell it on the beat before.
    void Forecast(IBeatSource music, double now, long tick)
    {
        var moment = music is IMusicBeatSource arranged ? arranged.MusicNow : new MusicMoment { energy = .5f, bass = .5f, accent = .5f, kind = "drive" };
        float intensity = Preparing ? BossDifficulty.Shape(world, Mathf.Clamp01(moment.energy * .75f + moment.bass * .15f), rampClock) : Intensity;
        int mask = WaveRhythm.Mask(moment.kind, intensity);
        nextWaveBeat = double.NaN;
        long from = Preparing ? (long)Math.Ceiling(startBeat * 4 - 1e-8) : tick + 1;
        for (long t = from; t < from + WaveRhythm.TicksPerBar; t++)
        {
            if (HeldAt(t * .25)) break;   // a special's hold: no wave is coming (so none is told)
            int bar = (int)(((t - 4L * music.DownbeatBeat) % WaveRhythm.TicksPerBar + WaveRhythm.TicksPerBar) % WaveRhythm.TicksPerBar);
            if (((mask >> bar) & 1) == 0) continue;
            nextWaveBeat = t * .25;
            break;
        }
        tellSource = waves.Peek(intensity);
        UpdateTell(now);
    }

    void UpdateTell(double now)
    {
        if (double.IsNaN(nextWaveBeat) || now >= nextWaveBeat) { Telling = false; TellProgress = 0f; return; }
        double start = Math.Max(nextWaveBeat - TellBeats, Math.Max(lastWaveBeat, startBeat - 2));
        if (now < start || nextWaveBeat - start < 1e-6) { Telling = false; TellProgress = 0f; return; }
        Telling = true;
        TellProgress = Mathf.Clamp01((float)((now - start) / (nextWaveBeat - start)));
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
        // each voice draws its world's own attack variants (Bosses/<Key>_attackshots.png rows; 0 where unpainted)
        int[] arts = world >= 0 && world < VoiceArt.Length ? VoiceArt[world] : null;
        if (arts != null) { p.art = arts[0]; s.art = arts[1]; a.art = arts[2]; }
        return new[] { p, s, a };
    }

    // per world (Space, Frost, Verdant, Ember, Tide, Storm, Desert): pulse, signature, accent
    public static readonly int[][] VoiceArt =
    {
        new[] { 1, 3, 2 }, new[] { 1, 3, 2 }, new[] { 1, 2, 3 }, new[] { 3, 1, 2 }, new[] { 1, 3, 2 }, new[] { 1, 2, 3 }, new[] { 2, 1, 3 },
    };
}
