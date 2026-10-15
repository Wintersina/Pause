using UnityEngine;

// Whoever tells the boss patterns where the music is. Active is false whenever the music is not really playing the
// boss track (no map, not started, paused, a different clip): the patterns then keep their old cooldown timers.
public interface IBeatSource
{
    bool Active { get; }
    double BeatNow { get; }          // the beat position of the music right now (continuous, grows across loops)
    double SecondsPerBeat { get; }   // the local tempo
    int BeatsPerBar { get; }
    int DownbeatBeat { get; }        // beat index mod BeatsPerBar that is the bar line
}

// THE LATENCY KNOB (per device): how long after the audio source reports a position you actually hear it. Positive =
// the sound reaches the ear late, so the game treats the music as that much earlier. Default 0; stored in PlayerPrefs
// so a calibration screen (or a developer) can set it per device.
public static class BeatCalibration
{
    public const string Key = "beat_latency_ms";
    static bool read;
    static float seconds;

    public static float LatencySeconds
    {
        get
        {
            if (!read)
            {
                read = true;
                try { seconds = PlayerPrefs.GetFloat(Key, 0f) / 1000f; } catch { seconds = 0f; }
            }
            return seconds;
        }
        set
        {
            read = true;
            seconds = Mathf.Clamp(value, -.25f, .25f);
            try { PlayerPrefs.SetFloat(Key, seconds * 1000f); } catch { }
        }
    }
}

// The beat clock: follows a looping music track's play position and turns it into a beat position.
//
// The audio source's own position (timeSamples) only moves in buffer-sized steps, and frame time is not the audio
// clock, so the clock is ANCHORED: at every sample it predicts where the track should be from AudioSettings.dspTime
// (the audio thread's clock, which does not stutter with frames) and nudges the anchor 10 % of the way to what the
// source reports; a bigger disagreement (a seek, the loop wrapping under it) re-anchors at once. Between samples
// BeatNow extrapolates from dspTime, so a beat is exact to the frame it is read in.
//
// The beat counter is monotonic across loop wraps (each wrap adds a whole number of bars, BeatMap.BeatsPerLoop) so
// "next beat" bookkeeping never runs backwards. The class is plain C# apart from reading AudioSettings.dspTime
// through `Now`, which the tests replace.
public sealed class BeatClock : IBeatSource
{
    public const double MaxStale = .35;      // dsp seconds without a sample before the clock calls itself inactive
    public const double SnapSeconds = .08;   // a disagreement bigger than this re-anchors at once
    public const double Smoothing = .1;

    public static System.Func<double> Now = () => AudioSettings.dspTime;

    public readonly BeatMap map;
    readonly double duration, beatsPerLoop;
    bool anchored, playing;
    double anchorDsp, anchorClip, lastSample;
    long loops;
    double pitch = 1.0;

    public bool UseLatencyOverride;
    public double LatencyOverride;

    public BeatClock(BeatMap m, double clipDuration = 0)
    {
        map = m;
        // Imported MP3 clips contain padding beyond the offline decoded duration.
        // Wrap when the AudioSource actually wraps, not at the analyzer's loop point.
        duration = clipDuration > 0 ? clipDuration : m.duration;
        beatsPerLoop = m.BeatsInLoop(duration);
    }

    public int BeatsPerBar => map.BarBeats;
    public int DownbeatBeat => map.downbeatBeat;
    public double Pitch { get => pitch; set => pitch = value <= 0.0 ? 1.0 : value; }
    public double Latency => UseLatencyOverride ? LatencyOverride : BeatCalibration.LatencySeconds;

    public bool Active => anchored && playing && Now() - lastSample <= MaxStale;

    // Music not playing (not started, paused, another clip): the clock goes inactive and re-anchors on the next sample.
    public void Halt()
    {
        playing = false;
        anchored = false;
    }

    // Start over (a new track): the beat counter restarts too.
    public void Reset()
    {
        Halt();
        loops = 0;
    }

    // One reading of the source: its clip position (seconds) while it is playing.
    public void Sample(double dsp, double clipTime, bool isPlaying)
    {
        if (!isPlaying) { Halt(); return; }
        playing = true;
        lastSample = dsp;
        double d = duration;
        if (!anchored)
        {
            anchored = true;
            anchorDsp = dsp;
            anchorClip = clipTime;
            return;
        }
        double predicted = anchorClip + (dsp - anchorDsp) * pitch;
        int wraps = 0;
        if (d > 0.0)
        {
            while (predicted >= d) { predicted -= d; wraps++; }
        }
        double diff = clipTime - predicted;
        if (d > 0.0)
        {
            // the source may already have wrapped (or not yet) when the prediction has not: compare round the loop
            if (diff > d * .5) { diff -= d; }
            else if (diff < -d * .5) { diff += d; }
        }
        loops += wraps;
        anchorDsp = dsp;
        if (System.Math.Abs(diff) > SnapSeconds)
        {
            anchorClip = clipTime;
        }
        else
        {
            anchorClip = predicted + diff * Smoothing;
        }
    }

    // The clip time the music is at, in the unwrapped timeline (clip time + whole loops * duration), less latency.
    public double MusicTime(double dsp)
    {
        return anchorClip + (dsp - anchorDsp) * pitch - Latency + loops * duration;
    }

    public double BeatAtDsp(double dsp)
    {
        double t = anchorClip + (dsp - anchorDsp) * pitch - Latency;
        double d = duration;
        long w = 0;
        if (d > 0.0)
        {
            w = (long)System.Math.Floor(t / d);
            t -= w * d;
        }
        return (loops + w) * beatsPerLoop + map.BeatAt(t);
    }

    public double BeatNow => BeatAtDsp(Now());

    public double SecondsPerBeat
    {
        get
        {
            double t = anchorClip + (Now() - anchorDsp) * pitch - Latency;
            double d = duration;
            if (d > 0.0) t -= System.Math.Floor(t / d) * d;
            return map.SecondsPerBeatAt(t) / pitch;
        }
    }
}

// The boss track's clock in the running game: WorldMusic attaches it when the boss song takes over the music source
// and detaches it when the fight ends; BossActor ticks it every frame and hands it to the pattern runner.
public static class BossBeat
{
    static BeatClock clock;
    static AudioSource source;
    static AudioClip clip;

    // Master switch (dev toggle / test): false = every boss keeps its cooldown timers.
    public static bool Enabled = true;

    // The clock, or null when the patterns should use their old timers.
    public static IBeatSource Source => Enabled && clock != null && clock.Active ? clock : null;

    public static bool Attached => clock != null;

    public static void Attach(AudioSource src, string worldName)
    {
        Detach();
        var map = BeatMaps.For(worldName);
        if (src == null || map == null || src.clip == null) return;
        source = src;
        clip = src.clip;
        clock = new BeatClock(map, clip.length);
        Tick();
    }

    public static void Detach()
    {
        clock = null;
        source = null;
        clip = null;
    }

    // Once per frame while the fight runs.
    public static void Tick()
    {
        if (clock == null) return;
        if (source == null || source.clip != clip) { Detach(); return; }
        double hz = clip.frequency;
        clock.Pitch = source.pitch;
        clock.Sample(AudioSettings.dspTime, hz > 0.0 ? source.timeSamples / hz : source.time, source.isPlaying);
    }
}
