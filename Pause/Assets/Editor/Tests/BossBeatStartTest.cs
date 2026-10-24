using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor.SceneManagement;
using UnityEngine;

// THE FIRST BOSS ON THE BEAT FROM ITS FIRST WAVE (the Void Archon; docs/boss-beat-timing.md, "Clip lead and output
// latency"). The director schedules on the grid exactly; what put the first ~40 s (the song's sparse intro) audibly
// off was the clock's idea of when the music is HEARD, by a constant the intro's lone bass hits expose:
//   1 clip lead  every boss MP3 declares its codec priming in its LAME header (encoder delay + the decoder's 529
//                samples = 1105); the analyzer's decoder (ffmpeg) drops it and skips the header's Info frame, Unity's
//                import decodes everything, the Info frame too (every clip is (frames + 1) * 1152 samples long), so the
//                map carries clipLead = (1152 + 1105) / 48000 = 47 ms and BeatMap places every beat that much later in
//                AudioSource time
//   2 latency    BeatCalibration with nothing saved is the mixer's own output latency (the DSP ring buffer), not 0; a
//                saved calibration replaces it; the clock reads it
//   3 the fight  the real BossActor on a simulated BeatClock (the song starts with the entrance, then 60 s of fight at
//                60 fps, dspTime and timeSamples stepping per 21 ms buffer, 85 ms of output latency): every wave leaves
//                and every tell starts within a frame plus a buffer AFTER a sixteenth of the HEARD grid, from the first
//                wave on -- and the same run on the old clock (no lead, latency 0) is the complaint: every wave about
//                a tenth of a second early
public static class BossBeatStartTest
{
    static int fails;
    static void Check(string what, bool ok) { Debug.Log("[BEATSTART] " + (ok ? "PASS " : "FAIL ") + what); if (!ok) fails++; }
    static bool Near(double a, double b, double tol) => Math.Abs(a - b) <= tol;
    public static void Run() { TestHarness.Exit(Execute()); }

    static readonly string[] Names = { "Space", "Frost", "Verdant", "Ember", "Tide", "Storm", "Desert" };
    const int DecoderDelay = 529;   // samples every MP3 decoder adds (the synthesis filterbank's), on top of the encoder's declared delay
    const int FrameSamples = 1152;  // one MPEG-1 layer III frame: the Xing / Info header frame decodes to this much silence

    public static int Execute()
    {
        fails = 0;
        using var sandbox = new TestHarness.Sandbox();
        var oldNow = BeatClock.Now;
        var oldProbe = BeatCalibration.OutputLatencyProbe;
        bool hadLatency = PlayerPrefs.HasKey(BeatCalibration.Key);
        float savedLatency = hadLatency ? PlayerPrefs.GetFloat(BeatCalibration.Key) : 0f;
        try
        {
            ClipLead();
            OutputLatency();
            FightStart();
        }
        finally
        {
            BeatClock.Now = oldNow;
            BeatCalibration.OutputLatencyProbe = oldProbe;
            if (hadLatency) PlayerPrefs.SetFloat(BeatCalibration.Key, savedLatency); else PlayerPrefs.DeleteKey(BeatCalibration.Key);
            BeatCalibration.Forget();
        }
        Debug.Log("[BEATSTART] failures: " + fails);
        return fails;
    }

    // ---- 1 ----
    static void ClipLead()
    {
        foreach (string name in Names)
        {
            var map = BeatMaps.For(name);
            string path = Path.Combine(Application.dataPath, "Audio/Resources/WorldMusic/Boss_" + name + ".mp3");
            if (map == null || !File.Exists(path)) { Check(name + ": map and mp3 present", false); continue; }
            bool parsed = LameHeader(File.ReadAllBytes(path), out int hz, out int frames, out int delay, out _);
            var clip = Resources.Load<AudioClip>(map.clip);
            // the header frame Unity decodes as silence, then the encoder's padding, then the decoder's own delay
            double lead = parsed ? (FrameSamples + delay + DecoderDelay) / (double)hz : 0;
            Check(name + ": the mp3 declares its priming (" + FrameSamples + " + " + delay + " + " + DecoderDelay + " samples at " + hz + " Hz = " + (lead * 1000).ToString("0.0") +
                  " ms) and the map carries it as clipLead (" + (map.clipLead * 1000).ToString("0.0") + " ms)", parsed && delay > 0 && Near(map.clipLead, lead, 1e-4));
            // (the clip is one frame longer than the header counts: the Info frame itself is decoded, not skipped)
            Check(name + ": Unity's import decodes every frame, the header's too (clip " + (clip != null ? clip.length.ToString("0.000") : "-") + " s = (" + frames +
                  " + 1) x 1152 samples), so the priming is in the clip the game plays", parsed && clip != null && Near(clip.length, (frames + 1) * (double)FrameSamples / hz, .002));
            var bare = JsonUtility.FromJson<BeatMap>(JsonUtility.ToJson(map));
            bare.clipLead = 0f;
            bool applied = Near(map.BeatAt(5.0 + map.clipLead), bare.BeatAt(5.0), 1e-6) && Near(map.TimeOfBeat(7.5), bare.TimeOfBeat(7.5) + map.clipLead, 1e-6)
                        && Near(map.BeatAt(map.TimeOfBeat(7.5)), 7.5, 1e-6) && Near(map.SecondsPerBeatAt(5.0 + map.clipLead), bare.SecondsPerBeatAt(5.0), 1e-9)
                        && (!map.steady || Near(map.BeatAt(map.clipLead + map.firstBeat), 0, 1e-6));
            Check(name + ": BeatAt / TimeOfBeat / SecondsPerBeatAt speak AudioSource time (the lead applied once)", applied);
        }
    }

    // The Xing / Info frame of an MPEG-1 layer III file and its LAME extension: sample rate, frame count, encoder delay and padding.
    static bool LameHeader(byte[] d, out int hz, out int frames, out int delay, out int padding)
    {
        hz = frames = delay = padding = 0;
        int i = 0;
        if (d.Length > 10 && d[0] == 'I' && d[1] == 'D' && d[2] == '3') i = 10 + ((d[6] << 21) | (d[7] << 14) | (d[8] << 7) | d[9]);
        while (i < d.Length - 4 && !(d[i] == 0xFF && (d[i + 1] & 0xE0) == 0xE0)) i++;
        if (i >= d.Length - 4) return false;
        int version = (d[i + 1] >> 3) & 3, rateIndex = (d[i + 2] >> 2) & 3, mode = (d[i + 3] >> 6) & 3;
        if (version != 3 || rateIndex > 2) return false;
        hz = rateIndex == 0 ? 44100 : rateIndex == 1 ? 48000 : 32000;
        int off = i + 4 + (mode == 3 ? 17 : 32);
        if (off + 8 > d.Length) return false;
        string tag = System.Text.Encoding.ASCII.GetString(d, off, 4);
        if (tag != "Xing" && tag != "Info") return false;
        int flags = (d[off + 4] << 24) | (d[off + 5] << 16) | (d[off + 6] << 8) | d[off + 7];
        int q = off + 8;
        if ((flags & 1) != 0) { frames = (d[q] << 24) | (d[q + 1] << 16) | (d[q + 2] << 8) | d[q + 3]; q += 4; }
        if ((flags & 2) != 0) q += 4;
        if ((flags & 4) != 0) q += 100;
        if ((flags & 8) != 0) q += 4;
        // the LAME extension: 9 bytes of encoder name, 12 of settings, then delay and padding as two 12-bit fields
        int p = q + 21;
        if (p + 3 > d.Length) return false;
        delay = (d[p] << 4) | (d[p + 1] >> 4);
        padding = ((d[p + 1] & 0xF) << 8) | d[p + 2];
        return frames > 0;
    }

    // ---- 2 ----
    static void OutputLatency()
    {
        PlayerPrefs.DeleteKey(BeatCalibration.Key);
        BeatCalibration.OutputLatencyProbe = BeatCalibration.DspBufferLatency;
        BeatCalibration.Forget();
        float dsp = BeatCalibration.DspBufferLatency();
        AudioSettings.GetDSPBufferSize(out int length, out int buffers);
        Check("the mixer's ring buffer is the estimate (" + length + " x " + buffers + " at " + AudioSettings.outputSampleRate + " Hz = " + (dsp * 1000f).ToString("0.0") + " ms, within the knob's range)",
              dsp >= 0f && dsp <= .25f);
        Check("nothing saved: the knob reads the mixer's latency, not 0 by assumption", Near(BeatCalibration.LatencySeconds, dsp, 1e-6));
        BeatCalibration.OutputLatencyProbe = () => .0625f;
        BeatCalibration.Forget();
        Check("...through the probe (62.5 ms)", Near(BeatCalibration.LatencySeconds, .0625, 1e-6));
        var clock = new BeatClock(new BeatMap { name = "T", bpm = 150f, firstBeat = .2f, duration = 10f, steady = true });
        double now = 500.0;
        BeatClock.Now = () => now;
        clock.Sample(now, 2.0, true);
        Check("the clock reads the knob: a source at 2.0 s heard 62.5 ms late is beat " + clock.BeatNow.ToString("0.0000") + " (4.5 - .156)",
              Near(clock.Latency, .0625, 1e-6) && Near(clock.BeatNow, (2.0 - .0625 - .2) * 2.5, 1e-6));
        BeatCalibration.LatencySeconds = .03f;
        BeatCalibration.Forget();
        Check("a saved calibration replaces the estimate (30 ms)", Near(BeatCalibration.LatencySeconds, .03, 1e-6));
        PlayerPrefs.DeleteKey(BeatCalibration.Key);
        BeatCalibration.OutputLatencyProbe = BeatCalibration.DspBufferLatency;
        BeatCalibration.Forget();
    }

    // ---- 3 ----
    struct Event
    {
        public string what;
        public double heardBeat, offsetMs;   // the ear's beat position on the frame it happened; how far past the sixteenth it was meant for
        public int travel;                   // a wave's flight to the ship's row, beats
    }

    const int Fps = 60;
    const double Buffer = 1024.0 / 48000.0;   // one DSP buffer: dspTime and timeSamples both move in these steps
    const double Output = .085;                // simulated output latency: four such buffers (Unity's phone default)
    const float Intro = 4.8f;                  // BossConfig.IntroSeconds: the song starts with the entrance
    const float Fight = 60f;

    static void FightStart()
    {
        var map = BeatMaps.For("Space");
        var bare = JsonUtility.FromJson<BeatMap>(JsonUtility.ToJson(map));
        bare.clipLead = 0f;
        double spb = 60.0 / map.bpm;
        // seen on the first frame after its sixteenth, read from a clock that steps per audio buffer: never earlier, at most this late
        double lateAllowed = (1.0 / Fps + Buffer) * 1000 + 2;

        var now = Simulate(map, Output, map, out double startBeat);
        var old = Simulate(bare, 0, map, out _);

        int waves = 0, tells = 0, wavesOff = 0, tellsOff = 0;
        double worstEarly = 0, worstLate = 0, firstWave = double.NaN;
        string offenders = "";
        foreach (var e in now)
        {
            bool wave = e.what != "tell";
            if (wave) { waves++; if (double.IsNaN(firstWave)) firstWave = e.heardBeat; } else tells++;
            long tick = (long)Math.Round(e.heardBeat * 4);
            Debug.Log("[BEATSTART]   " + e.what.PadRight(11) + " heard beat " + e.heardBeat.ToString("0.000").PadLeft(8) + "  (bar " + (tick / 16).ToString().PadLeft(2) + " sixteenth " + (tick % 16).ToString().PadLeft(2) + ")  " +
                      (e.offsetMs >= 0 ? "+" : "") + e.offsetMs.ToString("0.0") + " ms" + (wave ? "  reaches the ship " + e.travel + " beats on" : ""));
            worstEarly = Math.Min(worstEarly, e.offsetMs);
            worstLate = Math.Max(worstLate, e.offsetMs);
            if (e.offsetMs < -1 || e.offsetMs > lateAllowed)
            {
                if (wave) wavesOff++; else tellsOff++;
                if (offenders.Length < 400) offenders += " " + e.what + "@" + e.heardBeat.ToString("0.00") + "(" + e.offsetMs.ToString("+0;-0") + "ms)";
            }
        }
        Check("the Void Archon fires through the first minute of its fight: " + waves + " waves and " + tells + " tells, the first wave at heard beat " + firstWave.ToString("0.00") +
              " (the director starts at " + startBeat.ToString("0.00") + ")", waves >= 20 && tells >= 20 && !double.IsNaN(firstWave) && firstWave <= startBeat + 4.25);
        Check("every wave leaves on the HEARD grid's sixteenth it was scheduled for, from the first wave on (" + wavesOff + " off; offsets " + worstEarly.ToString("+0.0;-0.0") + " .. " + worstLate.ToString("+0.0;-0.0") +
              " ms, allowed -1 .. " + lateAllowed.ToString("0.0") + ")" + (wavesOff > 0 ? ":" + offenders : ""), wavesOff == 0 && waves > 0);
        Check("every tell starts on a sixteenth of the HEARD grid (" + tellsOff + " off)" + (tellsOff > 0 ? ":" + offenders : ""), tellsOff == 0 && tells > 0);

        // the regression: the old clock (the map without its lead, the knob at 0) runs ahead of the ear by lead + latency, so
        // every wave leaves that long before the sixteenth it was scheduled for (a whole sixteenth and more at 150 bpm)
        var offsets = new List<double>();
        foreach (var e in old) if (e.what != "tell") offsets.Add(e.offsetMs);
        offsets.Sort();
        double median = offsets.Count > 0 ? offsets[offsets.Count / 2] : 0;
        double expected = -(Output + map.clipLead) * 1000;
        Check("the old clock (no clip lead, latency 0) is the complaint: its " + offsets.Count + " waves leave early, median " + median.ToString("+0.0;-0.0") + " ms against the heard grid (lead + latency = " +
              expected.ToString("0.0") + " ms" + (offsets.Count > 0 ? "; range " + offsets[0].ToString("+0.0;-0.0") + " .. " + offsets[offsets.Count - 1].ToString("+0.0;-0.0") : "") + ")",
              offsets.Count > 0 && median < -60 && median <= expected + lateAllowed && offsets[0] >= expected - 2);
        Debug.Log("[BEATSTART] seconds per beat " + spb.ToString("0.0000") + "; a sixteenth is " + (spb * 250).ToString("0.0") + " ms");
    }

    // The entrance and `Fight` seconds of the Void Archon's fight on a simulated song: the clock under test follows a
    // source started on the entrance's first frame (positions reported per mixer buffer, like timeSamples), the ear is
    // one output latency behind the mixer. Returns every wave emission and tell start with its heard beat position.
    static List<Event> Simulate(BeatMap clockMap, double clockLatency, BeatMap heardMap, out double directorStart)
    {
        var events = new List<Event>();
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        EliteSystem.Clear();
        AttackPools.Forget();
        AttackHazard.ForgetAll();
        AttackHazardArt.Forget();
        BossRails.Reset();
        var boss = BossCatalog.ForWorld(0);
        var actor = BossActor.Spawn(boss);
        var pool = new BossProjectilePool(BossConfig.ProjectilePoolMax, BossConfig.BeamPoolMax);
        var clipAsset = Resources.Load<AudioClip>(heardMap.clip);
        double clipLength = clipAsset != null ? clipAsset.length : heardMap.duration;
        var clock = new BeatClock(clockMap, clipLength) { UseLatencyOverride = true, LatencyOverride = clockLatency };
        double spb = 60.0 / heardMap.bpm;
        double dsp = 1000.0137;                                   // Play() is called mid-buffer...
        double start = Math.Ceiling(dsp / Buffer) * Buffer;      // ...and the source starts at the next mixer callback
        double Quantised(double t) => Math.Floor(t / Buffer) * Buffer;
        double dspQ = Quantised(dsp);
        BeatClock.Now = () => dspQ;
        double Mixer() => Math.Max(0, dspQ - start);              // timeSamples: the position at the last callback
        double Heard() => heardMap.BeatAt(dsp - start - Output);  // the ear: continuous, one output latency behind
        directorStart = double.NaN;
        try
        {
            BossExecutors.WarmPools(boss);
            float dt = 1f / Fps;
            clock.Sample(dspQ, Mixer(), true);                     // BossBeat.Attach on the entrance's first frame
            for (int f = 0; f < Fps * Intro; f++) { dsp += dt; dspQ = Quantised(dsp); clock.Sample(dspQ, Mixer(), true); }   // StepArrival ticks it
            actor.BeginFight();
            actor.BeatOverride = clock;
            var player = new Vector3(0f, -3.2f, 0f);
            var d = actor.MusicDirector;
            int emissions = 0;
            bool telling = false;
            float t = 0f;
            for (int f = 0; f < Fps * Fight; f++)
            {
                dsp += dt;
                dspQ = Quantised(dsp);
                clock.Sample(dspQ, Mixer(), true);
                player.x = Mathf.Sin(t * .3f) * 1.8f;
                actor.StepFight(dt, dt, player, t / BossConfig.FightSeconds, pool);
                pool.PlayerPos = player;
                pool.Step(dt);
                AttackPools.StepAll(dt);
                t += dt;
                if (double.IsNaN(directorStart)) directorStart = d.StartBeat;
                double heard = Heard();
                if (d.Emissions != emissions)
                {
                    // against the sixteenth the director scheduled it for (beat numbering is the map's on both clocks), not the
                    // nearest one: a clock a whole sixteenth ahead would otherwise look only a few ms off
                    emissions = d.Emissions;
                    double offset = (heard - d.LastScheduledBeat) * spb * 1000;
                    events.Add(new Event { what = d.Waves.LastKind.ToString().ToLowerInvariant() + " wave", heardBeat = heard, offsetMs = offset, travel = d.Waves.LastTravelBeats });
                }
                if (d.Telling != telling)
                {
                    telling = d.Telling;
                    if (telling) events.Add(new Event { what = "tell", heardBeat = heard, offsetMs = (heard - Math.Round(heard * 4) / 4) * spb * 1000 });
                }
            }
        }
        finally
        {
            pool.Dispose();
            UnityEngine.Object.DestroyImmediate(actor.gameObject);
        }
        return events;
    }
}
