using System.Collections.Generic;
using UnityEngine;

// What a hostile attack SOUNDS like in its own world (docs/world-attacks-design.md 0.3): material, never a beep. A cue is a few WAV
// variants at Resources/Audio/Attack/<World>/<cue>_0, _1, _2 (generator: Audio/EnemyDeathSrc~/build_tide_sounds.py). A (world, cue)
// with no clips is SILENT -- that is every world but Tide today -- so nothing here changes how the other worlds sound.
//
// Cues, by what fires them:
//   wave    AttackWave goes live (the surf wave)         jet     AttackJet goes live (water gun, limpet-mine jet)
//   strike  AttackStrike goes live (thunder)             lash    AttackLash goes live (Tide's tentacle)
//   burst   a fused orb bursts (depth charge)            splash  a glob lands as a pool (the Trawler's net)
// AttackHazard.Ignite and EliteShot call Play; the world's backdrop / ambience sounds are not here.
public static class AttackAudio
{
    public static readonly string[] WorldFolders = { "Space", "Frost", "Verdant", "Ember", "Tide" };
    public const string ResourceFolder = "Audio/Attack/";
    public const int MaxVariants = 6, MaxVoices = 4;
    public static float Volume = .7f;            // base gain of a cue
    public static float PitchJitter = .04f;
    public static float MinCueInterval = .07f;   // the same cue re-triggering inside this is dropped (a volley is one sound)

    // tests / tools
    public static bool Simulate;
    public static System.Func<double> Clock;
    public static int Played { get; private set; }
    public static int Dropped { get; private set; }
    public static string LastCue { get; private set; }
    public static AudioClip LastClip { get; private set; }

    sealed class Cue { public AudioClip[] clips; public int last = -1; public double lastTime = double.NegativeInfinity; }
    static readonly Dictionary<string, Cue> cues = new Dictionary<string, Cue>();
    static readonly AudioClip[] None = new AudioClip[0];
    static AudioSource[] voices;
    static int nextVoice;
    static uint rng = 0x2545f491u;

    // The cue an attack hazard's label asks for ("" none).
    public static string CueOf(string label)
    {
        switch (label)
        {
            case "surf wave": return "wave";
            case "jet": return "jet";
            case "strike": return "strike";
            case "tentacle lash": return "lash";
            default: return "";
        }
    }

    public static int Variants(int world, string cue) { return string.IsNullOrEmpty(cue) ? 0 : Get(world, cue).clips.Length; }
    public static AudioClip Clip(int world, string cue, int n) { var c = Get(world, cue); return n >= 0 && n < c.clips.Length ? c.clips[n] : null; }

    public static bool Play(int world, string cue, float gain = 1f)
    {
        if (string.IsNullOrEmpty(cue) || !(Application.isPlaying || Simulate)) return false;
        var c = Get(world, cue);
        if (c.clips.Length == 0) return false;
        double now = Now();
        if (now - c.lastTime < MinCueInterval) { Dropped++; return true; }
        c.lastTime = now;
        int v = c.clips.Length == 1 ? 0 : Pick(c.clips.Length, c.last);
        c.last = v;
        var clip = c.clips[v];
        Played++;
        LastCue = cue; LastClip = clip;
        if (Simulate || !Application.isPlaying) return true;
        var src = Voice();
        if (src == null) return true;
        src.Stop();
        src.clip = clip;
        src.volume = Mathf.Clamp01(Volume * gain);
        src.pitch = 1f + (Next01() * 2f - 1f) * PitchJitter;
        src.Play();
        return true;
    }

    public static void ClearCache() { cues.Clear(); Played = Dropped = 0; LastCue = null; LastClip = null; }

    static Cue Get(int world, string cue)
    {
        world = Mathf.Clamp(world, 0, WorldFolders.Length - 1);
        string key = world + "/" + cue;
        Cue c;
        if (cues.TryGetValue(key, out c)) return c;
        List<AudioClip> found = null;
        for (int n = 0; n < MaxVariants; n++)
        {
            var clip = Resources.Load<AudioClip>(ResourceFolder + WorldFolders[world] + "/" + cue + "_" + n);
            if (clip == null) break;
            if (found == null) found = new List<AudioClip>(3);
            found.Add(clip);
        }
        c = new Cue { clips = found != null ? found.ToArray() : None };
        cues[key] = c;
        return c;
    }

    static AudioSource Voice()
    {
        if (voices == null || voices[0] == null)
        {
            var host = new GameObject("AttackAudioVoices");
            Object.DontDestroyOnLoad(host);
            voices = new AudioSource[MaxVoices];
            for (int i = 0; i < MaxVoices; i++)
            {
                var s = host.AddComponent<AudioSource>();
                s.playOnAwake = false; s.spatialBlend = 0f; s.loop = false;
                voices[i] = s;
            }
        }
        // a free voice, else the oldest round-robin
        for (int i = 0; i < voices.Length; i++) if (!voices[i].isPlaying) return voices[i];
        nextVoice = (nextVoice + 1) % voices.Length;
        return voices[nextVoice];
    }

    static double Now()
    {
        if (Clock != null) return Clock();
        return Application.isPlaying ? Time.unscaledTimeAsDouble : Time.realtimeSinceStartupAsDouble;
    }

    static int Pick(int count, int last)
    {
        int v = (int)(NextU() % (uint)(count - 1));
        return last < 0 ? (int)(NextU() % (uint)count) : (v >= last ? v + 1 : v);
    }
    static uint NextU() { rng ^= rng << 13; rng ^= rng >> 17; rng ^= rng << 5; return rng; }
    static float Next01() { return (NextU() & 0xffffff) / 16777216f; }
}
