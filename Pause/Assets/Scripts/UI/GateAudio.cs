using UnityEngine;

// Sounds of the HapticGate splash card. splashScene names the cues
// (gate_rattle, gate_step, gate_crack, gate_smash); this plays the authored
// clips Resources/Audio/Gate/<cue>_<n>: a random variant (never the same one
// twice in a row per cue), a small pitch jitter, on a few pooled voices.
//
// The voices live on a DontDestroyOnLoad host, so the smash tail keeps ringing
// after the card hands over to the next scene (0.45 s later). The rattle has a
// voice of its own and the smash cuts it. The SOUND setting is honoured twice:
// AudioListener.volume (SoundSettings) silences every source, and a muted
// device does not even start a clip. No beeps, no per-frame work: a cue is one
// array lookup and one AudioSource.Play.
public static class GateAudio
{
    public const string Folder = "Audio/Gate/";
    public static readonly string[] Cues = { "gate_rattle", "gate_step", "gate_crack", "gate_smash" };
    public const int MaxVariants = 4;
    // Shared voices for step / crack / smash (the rattle has its own).
    public const int MaxVoices = 4;
    public static float PitchJitter = .04f;
    // Per-cue gain (steps get an extra random dip so the seven jolts jitter).
    public static float RattleGain = .7f, StepGain = .6f, CrackGain = .9f, SmashGain = 1f;
    public static float StepJitter = .25f;     // step volume x (1 - up to this)

    // ---- test / dev hooks ---------------------------------------------------
    public static bool Simulate;               // no AudioSource touched (edit-mode tests)
    public static System.Func<double> Clock;
    public static System.Func<bool> MutedSource = () => SoundSettings.Muted;
    public static int Played { get; private set; }
    public static int Skipped { get; private set; }
    public static int RattleCut { get; private set; }
    public static string LastCue { get; private set; }
    public static AudioClip LastClip { get; private set; }
    public static float LastVolume { get; private set; }
    public static float LastPitch { get; private set; }

    sealed class CueClips { public AudioClip[] clips; public int last = -1; }
    struct Voice { public AudioSource src; public double end; public float start; }

    static readonly CueClips[] cues = new CueClips[Cues.Length];
    static readonly Voice[] voices = new Voice[MaxVoices];
    static Voice rattle;
    static GameObject host;
    static uint rng = 0x2545f491u;
    static bool seeded;

    static int Index(string cue)
    {
        for (int i = 0; i < Cues.Length; i++) if (Cues[i] == cue) return i;
        return -1;
    }

    public static int Variants(string cue) { int i = Index(cue); return i < 0 ? 0 : Get(i).clips.Length; }
    public static AudioClip Clip(string cue, int n) { int i = Index(cue); if (i < 0) return null; var c = Get(i).clips; return n >= 0 && n < c.Length ? c[n] : null; }
    public static int ActiveVoices() { double now = Now(); int n = rattle.end > now ? 1 : 0; for (int i = 0; i < voices.Length; i++) if (voices[i].end > now) n++; return n; }

    static CueClips Get(int i)
    {
        var c = cues[i];
        if (c != null) return c;
        var list = new System.Collections.Generic.List<AudioClip>(2);
        for (int n = 0; n < MaxVariants; n++)
        {
            var clip = Resources.Load<AudioClip>(Folder + Cues[i] + "_" + n);
            if (clip == null) break;
            list.Add(clip);
        }
        return cues[i] = new CueClips { clips = list.ToArray() };
    }

    // Fire one named cue. Unknown names are ignored.
    public static void Play(string cue)
    {
        if (!(Application.isPlaying || Simulate)) return;
        int ci = Index(cue);
        if (ci < 0) return;
        if (MutedSource != null && MutedSource()) { Skipped++; return; }
        var k = Get(ci);
        if (k.clips.Length == 0) { Skipped++; return; }
        double now = Now();

        if (ci == 3 && rattle.end > now)    // the smash cuts a rattle still going
        {
            if (rattle.src != null && rattle.src.isPlaying && !Simulate) rattle.src.Stop();
            rattle.end = 0; RattleCut++;
        }

        int v = Pick(k.clips.Length, k.last);
        k.last = v;
        var clip = k.clips[v];
        float pitch = 1f + Range(-PitchJitter, PitchJitter);
        float vol = ci == 0 ? RattleGain : ci == 1 ? StepGain * (1f - Range(0f, StepJitter)) : ci == 2 ? CrackGain : SmashGain;

        if (ci == 0) { StartVoice(ref rattle, clip, now, vol, pitch); }
        else
        {
            int slot = -1;
            for (int i = 0; i < voices.Length; i++) if (voices[i].end <= now) { slot = i; break; }
            if (slot < 0)   // steal the oldest voice
            {
                slot = 0;
                for (int i = 1; i < voices.Length; i++) if (voices[i].end < voices[slot].end) slot = i;
            }
            StartVoice(ref voices[slot], clip, now, vol, pitch);
        }
        Played++;
        LastCue = cue; LastClip = clip; LastVolume = vol; LastPitch = pitch;
    }

    static void StartVoice(ref Voice voice, AudioClip clip, double now, float vol, float pitch)
    {
        voice.end = now + clip.length / Mathf.Max(.01f, pitch);
        if (Simulate || !Application.isPlaying) return;
        if (voice.src == null) voice.src = NewSource();
        voice.src.Stop();
        voice.src.clip = clip;
        voice.src.volume = vol;
        voice.src.pitch = pitch;
        voice.src.Play();
    }

    static AudioSource NewSource()
    {
        if (host == null)
        {
            host = new GameObject("GateVoices");
            Object.DontDestroyOnLoad(host);
        }
        var s = host.AddComponent<AudioSource>();
        s.playOnAwake = false; s.loop = false; s.spatialBlend = 0f;
        return s;
    }

    // Forget voices and counters (not the clip cache).
    public static void Reset()
    {
        if (rattle.src != null) rattle.src.Stop();
        for (int i = 0; i < voices.Length; i++) if (voices[i].src != null) voices[i].src.Stop();
        rattle.end = 0;
        for (int i = 0; i < voices.Length; i++) voices[i].end = 0;
        Played = Skipped = RattleCut = 0; LastCue = null; LastClip = null;
        for (int i = 0; i < cues.Length; i++) if (cues[i] != null) cues[i].last = -1;
    }

    public static void Seed(uint s) { rng = s != 0 ? s : 1u; seeded = true; }

    static double Now()
    {
        if (Clock != null) return Clock();
        return Application.isPlaying ? Time.unscaledTimeAsDouble : Time.realtimeSinceStartupAsDouble;
    }

    static uint NextU()
    {
        if (!seeded) Seed((uint)System.Environment.TickCount ^ 0x9e3779b1u);
        rng ^= rng << 13; rng ^= rng >> 17; rng ^= rng << 5;
        return rng;
    }
    static float Range(float a, float b) { return a + (b - a) * ((NextU() & 0xffffff) / 16777216f); }
    static int Pick(int count, int last)
    {
        if (count <= 1) return 0;
        if (last < 0 || last >= count) return (int)(NextU() % (uint)count);
        int v = (int)(NextU() % (uint)(count - 1));
        return v >= last ? v + 1 : v;
    }
}
