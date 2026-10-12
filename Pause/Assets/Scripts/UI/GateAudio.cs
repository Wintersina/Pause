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

    // The user's own gate-opening clip (5.6 s, Audio/Resources/Audio/GateOpen/gate_open.wav,
    // Vorbis q0.65, stereo). It REPLACES the natural-run rattle and the seven step shrieks
    // (they would pile into mush under it); the tap cracks and the smash stay. Its first big
    // hit (broadband sweep ends, latch lets go) is 1.11 s into the file, and splashScene
    // starts it OpenClipStart after the card begins so that hit lands on the door's biggest
    // forced step (1.22 s). A smash stops it in OpenSmashFade; leaving naturally fades it
    // over OpenLeaveFade (its second swell and thump at 4.5-5.2 s never reach the title).
    public const string OpenResource = "Audio/GateOpen/gate_open";
    public const float OpenGain = .9f;
    public const float OpenSmashFade = .1f;
    public const float OpenLeaveFade = .8f;
    public const float OpenImpactSeconds = 1.11f;   // the hit's offset inside the clip

    // ---- test / dev hooks ---------------------------------------------------
    public static bool Simulate;               // no AudioSource touched (edit-mode tests)
    public static System.Func<double> Clock;
    public static System.Func<bool> MutedSource = () => SoundSettings.SfxGain <= 0f;
    public static int Played { get; private set; }
    public static int Skipped { get; private set; }
    public static int Suppressed { get; private set; }     // rattle / step cues replaced by the open clip
    public static int OpenStarts { get; private set; }
    public static int OpenStops { get; private set; }
    public static float OpenLastFade { get; private set; }
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
    static Voice open;
    static AudioClip openClip;
    static bool openTried;
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
    public static AudioClip OpenClip()
    {
        if (openClip == null && !openTried) { openTried = true; openClip = Resources.Load<AudioClip>(OpenResource); }
        return openClip;
    }
    // splashScene asks before it plays the natural-run rattle / step cues: the
    // opening clip stands in for them when it is installed.
    public static bool OpenReplaces(string cue)
    {
        if (cue != "gate_rattle" && cue != "gate_step") return false;
        if (OpenClip() == null) return false;
        Suppressed++;
        return true;
    }

    public static bool OpenActive { get { return open.end > Now(); } }

    // The user's gate-opening clip, once per card; muted devices skip it.
    public static void PlayOpen()
    {
        if (!(Application.isPlaying || Simulate)) return;
        if (MutedSource != null && MutedSource()) { Skipped++; return; }
        var clip = OpenClip();
        if (clip == null) { Skipped++; return; }
        StartVoice(ref open, clip, Now(), OpenGain, 1f);
        OpenStarts++; Played++;
        LastCue = "gate_open"; LastClip = clip; LastVolume = OpenGain; LastPitch = 1f;
    }

    // Fade the opening clip out over `fade` seconds (a smash: 0.1 s) and stop it.
    public static void StopOpen(float fade)
    {
        double now = Now();
        if (open.end <= now) return;
        fade = Mathf.Max(0.01f, fade);
        OpenStops++; OpenLastFade = fade;
        if (!Simulate && Application.isPlaying && open.src != null && open.src.isPlaying)
        {
            var f = open.src.GetComponent<GateOpenFader>() ?? open.src.gameObject.AddComponent<GateOpenFader>();
            f.Begin(open.src, fade);
        }
        open.end = Mathf.Min((float)(open.end - now), fade) + now;
    }

    public static int ActiveVoices() { double now = Now(); int n = (rattle.end > now ? 1 : 0) + (open.end > now ? 1 : 0); for (int i = 0; i < voices.Length; i++) if (voices[i].end > now) n++; return n; }

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

        if (ci == 3) StopOpen(OpenSmashFade);
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
        if (open.src != null) open.src.Stop();
        for (int i = 0; i < voices.Length; i++) if (voices[i].src != null) voices[i].src.Stop();
        rattle.end = 0; open.end = 0;
        Suppressed = OpenStarts = OpenStops = 0; OpenLastFade = 0f;
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

// Ramps one source to silence and stops it (the opening clip when a smash or the
// scene change cuts it). Unscaled time: the splash may run at timeScale 0.
public sealed class GateOpenFader : MonoBehaviour
{
    AudioSource src; float from, fade, t;
    public void Begin(AudioSource source, float seconds) { src = source; from = source.volume; fade = Mathf.Max(0.01f, seconds); t = 0f; enabled = true; }
    void Update()
    {
        if (src == null) { enabled = false; return; }
        t += Time.unscaledDeltaTime;
        if (t >= fade) { src.Stop(); src.volume = from; enabled = false; return; }
        src.volume = from * (1f - t / fade);
    }
}
