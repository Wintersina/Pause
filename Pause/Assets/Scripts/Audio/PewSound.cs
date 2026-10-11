using UnityEngine;

// The quiet shot sound every hostile PROJECTILE makes when it is fired (roster shots, elite shots, boss shots, rail-mine lasers), so the
// player hears that something was shot. Never played for beams, jets, waves (their own AttackAudio cues) or the player's own shots.
//
// Two cues, the user's own recordings (Resources/Audio/Attack/EnemyShot/laser.wav and gunshot.wav, trimmed from the originals by
// Audio/EnemyShotSrc~/build_enemy_shot.py):
//   Laser  a single / slow shot (the default)
//   Gun    rapid fire: an attacker (one source) that fires BurstShots shots with each less than BurstGap after the previous one is in a
//          burst, and its shots play the gun clip, chained -- retriggered at most every GunInterval so it reads as continuous fire
//          rather than a voice per bullet.
// With no clip files the Laser falls back to a tiny synthesized downward-sweep blip (and the Gun to the same blip), so a missing file is
// never silent-by-accident or a crash.
//
// Plays through a few shared AudioSources, so it follows the Effects bus (AudioListener.volume, SoundSettings); it is skipped outright
// while Effects is off. Barrages are capped: shots closer than the cue's interval to the last played one are dropped, and at most
// MaxPerWindow play per WindowSeconds. Each play gets a little pitch and volume variation.
public static class PewSound
{
    public enum Kind { Laser, Gun }

    public const string LaserPath = "Audio/Attack/EnemyShot/laser", GunPath = "Audio/Attack/EnemyShot/gunshot";
    public const int Rate = 22050;
    public static float LaserVolume = .3f, GunVolume = .22f;     // quiet base gains
    public static float PitchJitter = .06f, VolumeJitter = .15f;
    public static float LaserInterval = .05f;                      // shots closer than this to the last played one are dropped
    public static float GunInterval = .09f;                        // the gun chain's retrigger period (~11 per second)
    public static float BurstGap = .25f;                           // shots of one source this close together are one burst
    public const int BurstShots = 3;                               // the 3rd shot of a burst switches to the gun
    public const float WindowSeconds = 1f;
    public const int MaxPerWindow = 12;
    public const int MaxVoices = 4;
    const int Sources = 16;                                        // distinct shooters tracked at once

    // tests / tools
    public static bool Simulate;
    public static System.Func<double> Clock;
    public static int Played { get; private set; }
    public static int Dropped { get; private set; }
    public static Kind LastKind { get; private set; }
    public static float LastGain { get; private set; }
    public static bool UsedSynthFallback { get; private set; }

    static AudioClip laser, gun;
    static bool loaded;
    static AudioSource[] voices;
    static int nextVoice;
    static readonly double[] recent = new double[MaxPerWindow];
    static int recentCount, recentHead;
    static double lastLaser = double.NegativeInfinity, lastGun = double.NegativeInfinity;
    static readonly int[] srcKey = new int[Sources];
    static readonly double[] srcLast = new double[Sources];
    static readonly int[] srcRun = new int[Sources];
    static uint rng = 0x9e3779b9u;

    public static void Reset()
    {
        Played = Dropped = 0;
        recentCount = recentHead = 0;
        lastLaser = lastGun = double.NegativeInfinity;
        for (int i = 0; i < Sources; i++) { srcKey[i] = 0; srcLast[i] = double.NegativeInfinity; srcRun[i] = 0; }
    }

    // `source` identifies who fired (any stable id; 0 = anonymous): it decides single shot vs burst. A forced kind skips that.
    public static bool Play(int source = 0) { return Play(null, source); }
    public static bool Play(Kind? forced, int source = 0)
    {
        if (!(Application.isPlaying || Simulate)) return false;
        if (SoundSettings.SfxGain <= 0f) return false;            // Effects off: silent, nothing counted
        double now = Now();
        Kind kind = forced ?? (Run(source, now) >= BurstShots ? Kind.Gun : Kind.Laser);
        double since = now - (kind == Kind.Gun ? lastGun : lastLaser);
        if (since < (kind == Kind.Gun ? GunInterval : LaserInterval) || now - System.Math.Max(lastGun, lastLaser) < LaserInterval) { Dropped++; return false; }   // (either cue's last play counts: one barrage is one sound)
        // sliding window: drop the one that would be the (MaxPerWindow + 1)th inside WindowSeconds
        while (recentCount > 0 && now - recent[recentHead] > WindowSeconds) { recentHead = (recentHead + 1) % MaxPerWindow; recentCount--; }
        if (recentCount >= MaxPerWindow) { Dropped++; return false; }
        recent[(recentHead + recentCount) % MaxPerWindow] = now;
        recentCount++;
        if (kind == Kind.Gun) lastGun = now; else lastLaser = now;
        Played++;
        LastKind = kind;
        float gain = (kind == Kind.Gun ? GunVolume : LaserVolume);
        LastGain = gain;
        if (Simulate || !Application.isPlaying) return true;

        EnsureClips();
        var clip = kind == Kind.Gun ? gun : laser;
        if (clip == null) return true;
        var src = Voice();
        src.Stop();
        src.clip = clip;
        src.volume = Mathf.Clamp01(gain * (1f + (Next01() * 2f - 1f) * VolumeJitter));
        src.pitch = 1f + (Next01() * 2f - 1f) * PitchJitter;
        src.Play();
        return true;
    }

    // The length of `source`'s current run of closely spaced shots, this shot included.
    static int Run(int source, double now)
    {
        int slot = -1, oldest = 0;
        for (int i = 0; i < Sources; i++)
        {
            if (srcKey[i] == source && srcLast[i] > double.NegativeInfinity) { slot = i; break; }
            if (srcLast[i] < srcLast[oldest]) oldest = i;
        }
        if (slot < 0) { slot = oldest; srcKey[slot] = source; srcRun[slot] = 0; srcLast[slot] = double.NegativeInfinity; }
        srcRun[slot] = now - srcLast[slot] < BurstGap ? srcRun[slot] + 1 : 1;
        srcLast[slot] = now;
        return srcRun[slot];
    }

    static void EnsureClips()
    {
        if (loaded && (laser != null || gun != null)) return;
        loaded = true;
        laser = Resources.Load<AudioClip>(LaserPath);
        gun = Resources.Load<AudioClip>(GunPath);
        UsedSynthFallback = laser == null || gun == null;
        if (laser == null) laser = Synth("pew_fallback");
        if (gun == null) gun = laser;
    }

    // The fallback when the files are missing: a short downward-sweep blip.
    static AudioClip Synth(string name)
    {
        int n = Mathf.RoundToInt(.09f * Rate);
        var d = new float[n];
        double phase = 0;
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)Rate, u = t / .09f;
            phase += 1400f * Mathf.Pow(480f / 1400f, Mathf.Min(1f, u * 1.25f)) / Rate;
            d[i] = Mathf.Sin((float)(phase * 2 * System.Math.PI)) * Mathf.Clamp01(t / .006f) * Mathf.Exp(-t * 4.2f / .09f) * Mathf.Clamp01((.09f - t) / .012f) * .8f;
        }
        var clip = AudioClip.Create(name, n, 1, Rate, false);
        clip.SetData(d, 0);
        return clip;
    }

    static AudioSource Voice()
    {
        if (voices == null || voices[0] == null)
        {
            var host = new GameObject("PewVoices");
            Object.DontDestroyOnLoad(host);
            voices = new AudioSource[MaxVoices];
            for (int i = 0; i < MaxVoices; i++)
            {
                var s = host.AddComponent<AudioSource>();
                s.playOnAwake = false; s.spatialBlend = 0f; s.loop = false;
                voices[i] = s;
            }
        }
        for (int i = 0; i < voices.Length; i++) if (!voices[i].isPlaying) return voices[i];
        nextVoice = (nextVoice + 1) % voices.Length;
        return voices[nextVoice];
    }

    static double Now()
    {
        if (Clock != null) return Clock();
        return Application.isPlaying ? Time.unscaledTimeAsDouble : Time.realtimeSinceStartupAsDouble;
    }

    static float Next01()
    {
        rng ^= rng << 13; rng ^= rng >> 17; rng ^= rng << 5;
        return (rng & 0xffffff) / 16777216f;
    }
}
