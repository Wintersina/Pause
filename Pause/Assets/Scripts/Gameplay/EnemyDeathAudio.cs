using System.Collections.Generic;
using UnityEngine;

// Per-target destruction audio.
//
// AUTHORED CLIPS FIRST: a roster key (or an elite def key) with WAVs at
// Resources/Audio/EnemyDeath/<key>_0, <key>_1, ... plays one of them (a
// random variant, never the same one twice in a row for that key, a small
// pitch jitter) on a small pool of voices with a polyphony cap, a per-key
// minimum interval and a light duck of rapidly repeated identical keys, so
// a death combo cannot stack into a wall of sound. A key that also has
// <key>_scream_0, ... sometimes (ScreamChance) layers a quiet radio cry /
// creature screech a few tens of milliseconds after the death cue.
// Today only the Space world has authored clips; Frost needs nothing but
// <frost key>_N.wav files dropped into the same folder.
//
// PROCEDURAL FALLBACK: a key with no authored clip (Frost, Verdant, Ember
// today) -- or every key when AuthoredEnabled is off -- deterministically
// synthesizes its own clip exactly as before: hostile craft whine into a
// metal failure, rocks crack, mines alarm-pop.
//
// Timing: like the procedural cue (PlayOneShot), authored cues ignore
// Time.timeScale -- a kill on a frozen (paused) frame is heard at once --
// and the scream's delay is AudioSource.PlayDelayed (audio clock), so it
// follows the very same rule. Voice bookkeeping uses unscaled time.
public static class EnemyDeathAudio
{
    const int Rate = 22050;
    const int MaxCachedClips = 128;
    static readonly Dictionary<string, AudioClip> clips = new Dictionary<string, AudioClip>();
    static AudioSource source;

    // ---- tunables -----------------------------------------------------------

    // Master switch for the authored WAVs. Off: every key uses the
    // procedural clip, and elites play no extra death cue (as before).
    public static bool AuthoredEnabled = true;
    // Resources folder the authored clips live in (<key>_<n>, <key>_scream_<n>).
    public const string ResourceFolder = "Audio/EnemyDeath/";
    // Probe stops at this many variants per key.
    public const int MaxVariants = 8;
    // Random pitch jitter of an authored cue / scream: 1 +- this.
    public static float PitchJitter = .03f;
    // Overall gain of an authored cue (the procedural source ran at .8 x
    // Volume(role); authored cues keep that scale).
    public static float AuthoredGain = .8f;
    // Base volume of an elite's death cue (elites have no EnemyRole).
    public static float EliteVolume = .8f;
    // Chance a death cue with scream clips layers one.
    public static float ScreamChance = .65f;
    // The scream starts this long after the death cue (seconds, random in range).
    public static float ScreamDelayMin = .034f, ScreamDelayMax = .055f;
    // Scream volume relative to its death cue's (0.5 ~ peak 12 dB under it).
    public static float ScreamVolume = .5f;
    // Simultaneous authored death cues / screams; past the cap the quietest
    // (then oldest) voice is cut -- or the new cue dropped if it is quieter
    // still. A scream with no free voice is simply skipped.
    public const int MaxVoices = 6, MaxScreamVoices = 2;
    // The same key re-triggering within this many seconds is dropped.
    public static float MinKeyInterval = .03f;
    // Identical keys repeating within DuckWindow seconds get quieter:
    // volume x DuckFactor per recent repeat, never below DuckFloor.
    public static float DuckWindow = .3f, DuckFactor = .8f, DuckFloor = .5f;

    // ---- test / dev hooks -----------------------------------------------------

    // True: no AudioSource is touched -- the voice bookkeeping runs on its own
    // (edit-mode tests). Clock overrides unscaled time (tests).
    public static bool Simulate;
    public static System.Func<double> Clock;
    public static int Played { get; private set; }      // authored cues started
    public static int Dropped { get; private set; }     // refused: interval / cap
    public static int Stolen { get; private set; }      // voices cut by the cap
    public static int Screams { get; private set; }     // screams started
    public static AudioClip LastClip { get; private set; }
    public static AudioClip LastScream { get; private set; }
    public static float LastScreamDelay { get; private set; }
    public static float LastVolume { get; private set; }
    public static string LastKey { get; private set; }

    // ---- authored state ---------------------------------------------------------

    sealed class KeyClips
    {
        public AudioClip[] deaths, screams;
        public int lastDeath = -1, lastScream = -1, repeats;
        public double lastTime = double.NegativeInfinity;
    }

    struct Voice { public AudioSource src; public double start, end; public float volume; }

    static readonly AudioClip[] None = new AudioClip[0];
    static readonly Dictionary<string, KeyClips> authored = new Dictionary<string, KeyClips>();
    static readonly Voice[] voices = new Voice[MaxVoices];
    static readonly Voice[] screamVoices = new Voice[MaxScreamVoices];
    static GameObject voiceHost;
    static uint rng = 0x9e3779b9u;
    static bool seeded;

    public static void Play(GameObject target)
    {
        if (target == null || !(Application.isPlaying || Simulate)) return;
        var def = EnemyIdentity.Of(target);
        string key = def != null ? def.key : target.name;
        if (string.IsNullOrEmpty(key)) key = "unknown_target";
        EnemyRole role = def != null ? def.role : GuessRole(target);
        if (PlayAuthored(key, Volume(role))) return;
        if (!Application.isPlaying) return;
        var clip = Clip(key, role);
        var src = Source();
        if (src == null || clip == null) return;
        src.pitch = 1f;
        src.PlayOneShot(clip, Volume(role));
    }

    // An elite going down (EliteDeath): its authored cue when it has one;
    // otherwise nothing extra -- its generic explosion covers it, as before.
    public static bool PlayElite(string eliteKey)
    {
        if (!(Application.isPlaying || Simulate)) return false;
        return PlayAuthored(eliteKey, EliteVolume);
    }

    // The authored cue (+ maybe a scream) for a key at a base volume; false
    // when the key has no authored clips (the caller falls back). A cue
    // refused by the interval / cap still returns true (it is handled).
    public static bool PlayAuthored(string key, float baseVolume, bool forceScream = false)
    {
        if (!AuthoredEnabled || string.IsNullOrEmpty(key)) return false;
        var k = Clips(key);
        if (k.deaths.Length == 0) return false;
        double now = Now();
        if (now - k.lastTime < MinKeyInterval) { Dropped++; return true; }
        k.repeats = now - k.lastTime < DuckWindow ? k.repeats + 1 : 0;
        k.lastTime = now;
        float volume = baseVolume * AuthoredGain * Mathf.Max(DuckFloor, Mathf.Pow(DuckFactor, k.repeats));

        int slot = PickSlot(voices, now, volume);
        if (slot < 0) { Dropped++; return true; }
        int v = PickVariant(k.deaths.Length, k.lastDeath);
        k.lastDeath = v;
        var clip = k.deaths[v];
        float pitch = 1f + Range(-PitchJitter, PitchJitter);
        Start(ref voices[slot], clip, now, 0f, volume, pitch);
        Played++;
        LastClip = clip; LastVolume = volume; LastKey = key;

        if (k.screams.Length > 0 && (forceScream || Next01() < ScreamChance))
        {
            int s = FreeSlot(screamVoices, now);
            if (s >= 0)
            {
                int sv = PickVariant(k.screams.Length, k.lastScream);
                k.lastScream = sv;
                float delay = Range(ScreamDelayMin, ScreamDelayMax);
                Start(ref screamVoices[s], k.screams[sv], now, delay, volume * ScreamVolume, 1f + Range(-PitchJitter, PitchJitter));
                Screams++;
                LastScream = k.screams[sv]; LastScreamDelay = delay;
            }
        }
        return true;
    }

    // Authored variants for a key (cached; probed once).
    public static int Variants(string key) { return string.IsNullOrEmpty(key) ? 0 : Clips(key).deaths.Length; }
    public static int ScreamVariants(string key) { return string.IsNullOrEmpty(key) ? 0 : Clips(key).screams.Length; }
    public static AudioClip AuthoredClip(string key, int n) { var k = Clips(key); return n >= 0 && n < k.deaths.Length ? k.deaths[n] : null; }
    public static AudioClip ScreamClip(string key, int n) { var k = Clips(key); return n >= 0 && n < k.screams.Length ? k.screams[n] : null; }
    // The fallback clip a key would synthesize (tests).
    public static AudioClip ProceduralClip(string key, EnemyRole role) { return Clip(key, role); }

    // Voices still sounding at the bookkeeping clock (tests / dev).
    public static int ActiveVoices() { return Active(voices, Now()); }
    public static int ActiveScreams() { return Active(screamVoices, Now()); }

    // Seeded variant / chance / delay choices (tests).
    public static void Seed(uint seed) { rng = seed != 0 ? seed : 1u; seeded = true; }

    // Forget voices, key timing and counters (not the clip cache).
    public static void ResetVoices()
    {
        for (int i = 0; i < voices.Length; i++) { if (voices[i].src != null && voices[i].src.isPlaying) voices[i].src.Stop(); voices[i].end = voices[i].start = 0; }
        for (int i = 0; i < screamVoices.Length; i++) { if (screamVoices[i].src != null && screamVoices[i].src.isPlaying) screamVoices[i].src.Stop(); screamVoices[i].end = screamVoices[i].start = 0; }
        foreach (var k in authored.Values) { k.lastTime = double.NegativeInfinity; k.repeats = 0; k.lastDeath = k.lastScream = -1; }
        Played = Dropped = Stolen = Screams = 0;
        LastClip = LastScream = null; LastKey = null;
    }

    public static void ClearCache() { ResetVoices(); authored.Clear(); }

    static KeyClips Clips(string key)
    {
        KeyClips k;
        if (authored.TryGetValue(key, out k)) return k;
        k = new KeyClips { deaths = Load(key + "_"), screams = Load(key + "_scream_") };
        authored[key] = k;
        return k;
    }

    static AudioClip[] Load(string prefix)
    {
        List<AudioClip> found = null;
        for (int n = 0; n < MaxVariants; n++)
        {
            var c = Resources.Load<AudioClip>(ResourceFolder + prefix + n);
            if (c == null) break;
            if (found == null) found = new List<AudioClip>(3);
            found.Add(c);
        }
        return found != null ? found.ToArray() : None;
    }

    static double Now()
    {
        if (Clock != null) return Clock();
        return Application.isPlaying ? Time.unscaledTimeAsDouble : Time.realtimeSinceStartupAsDouble;
    }

    static int Active(Voice[] pool, double now)
    {
        int n = 0;
        for (int i = 0; i < pool.Length; i++) if (pool[i].end > now) n++;
        return n;
    }

    static int FreeSlot(Voice[] pool, double now)
    {
        for (int i = 0; i < pool.Length; i++) if (pool[i].end <= now) return i;
        return -1;
    }

    // A free voice, else the quietest (then oldest) one if it is no louder
    // than the newcomer; -1 drops the newcomer.
    static int PickSlot(Voice[] pool, double now, float volume)
    {
        int free = FreeSlot(pool, now);
        if (free >= 0) return free;
        int pick = 0;
        for (int i = 1; i < pool.Length; i++)
            if (pool[i].volume < pool[pick].volume - 1e-4f ||
                (Mathf.Abs(pool[i].volume - pool[pick].volume) <= 1e-4f && pool[i].start < pool[pick].start)) pick = i;
        if (pool[pick].volume > volume + 1e-4f) return -1;
        Stolen++;
        return pick;
    }

    static int PickVariant(int count, int last)
    {
        if (count <= 1) return 0;
        if (last < 0 || last >= count) return (int)(NextU() % (uint)count);
        // one of the others, uniformly
        int v = (int)(NextU() % (uint)(count - 1));
        return v >= last ? v + 1 : v;
    }

    static void Start(ref Voice voice, AudioClip clip, double now, float delay, float volume, float pitch)
    {
        voice.start = now;
        voice.end = now + delay + (clip != null ? clip.length / Mathf.Max(.01f, pitch) : 0f);
        voice.volume = volume;
        if (Simulate || !Application.isPlaying || clip == null) return;
        if (voice.src == null) voice.src = NewVoice();
        if (voice.src == null) return;
        voice.src.Stop();
        voice.src.clip = clip;
        voice.src.volume = volume;
        voice.src.pitch = pitch;
        if (delay > 0f) voice.src.PlayDelayed(delay); else voice.src.Play();
    }

    static AudioSource NewVoice()
    {
        if (voiceHost == null)
        {
            voiceHost = new GameObject("EnemyDeathVoices");
            Object.DontDestroyOnLoad(voiceHost);
        }
        var s = voiceHost.AddComponent<AudioSource>();
        s.playOnAwake = false;
        s.spatialBlend = 0f;
        s.loop = false;
        return s;
    }

    static uint NextU()
    {
        if (!seeded) { Seed((uint)System.Environment.TickCount ^ 0x5bd1e995u); }
        rng ^= rng << 13; rng ^= rng >> 17; rng ^= rng << 5;
        return rng;
    }

    static float Next01() { return (NextU() & 0xffffff) / 16777216f; }
    static float Range(float a, float b) { return a + (b - a) * Next01(); }

    static EnemyRole GuessRole(GameObject target)
    {
        if (target.CompareTag("Astr")) return EnemyRole.Rock;
        string n = target.name.ToLowerInvariant();
        return n.Contains("mine") ? EnemyRole.Mine : EnemyRole.Fighter;
    }

    public static float Volume(EnemyRole role)
    {
        return role == EnemyRole.Big ? .9f : role == EnemyRole.Mine ? .82f : .68f;
    }

    static AudioSource Source()
    {
        if (source != null) return source;
        var host = new GameObject("EnemyDeathAudio");
        Object.DontDestroyOnLoad(host);
        source = host.AddComponent<AudioSource>();
        source.playOnAwake = false;
        source.spatialBlend = 0f;
        source.volume = .8f;
        return source;
    }

    static AudioClip Clip(string key, EnemyRole role)
    {
        AudioClip found;
        if (clips.TryGetValue(key, out found)) return found;
        // There are presently far fewer than this; the guard keeps an
        // experimental content build from retaining unbounded generated clips.
        if (clips.Count >= MaxCachedClips) clips.Clear();
        uint seed = Hash(key);
        int count = Mathf.RoundToInt(Rate * Duration(role, seed));
        var data = new float[count];
        for (int i = 0; i < count; i++)
        {
            float t = (float)i / Rate;
            float x = role == EnemyRole.Rock ? Rock(t, seed) :
                      role == EnemyRole.Mine ? Mine(t, seed) : Ship(t, seed, role);
            // Short fade avoids a digital click without softening the attack.
            float fade = Mathf.Min(1f, i / 90f, (count - i) / 180f);
            data[i] = Mathf.Clamp(x * fade, -.92f, .92f);
        }
        found = AudioClip.Create("death_" + key, count, 1, Rate, false);
        found.SetData(data, 0);
        clips[key] = found;
        return found;
    }

    static float Duration(EnemyRole role, uint seed)
    {
        float variance = ((seed >> 8) & 31) / 200f;
        return role == EnemyRole.Big ? .52f + variance :
               role == EnemyRole.Rock ? .32f + variance :
               role == EnemyRole.Mine ? .38f + variance : .28f + variance;
    }

    // A compact dying servo/scream.  Each ship key changes its base note,
    // wobble and collapse rate; heavies retain a slower, lower pressure drop.
    static float Ship(float t, uint seed, EnemyRole role)
    {
        float length = Duration(role, seed);
        float p = Mathf.Clamp01(t / length);
        float baseHz = 135f + (seed & 255) * 2.1f;
        float fall = role == EnemyRole.Big ? 1f - p * .56f : 1.35f - p * .98f;
        float wobble = Mathf.Sin(t * (23f + ((seed >> 16) & 31)) + (seed & 7)) * (18f + ((seed >> 5) & 15));
        float tone = Mathf.Sin(6.2831853f * (baseHz * fall + wobble) * t);
        float grit = Noise(t, seed) * (.16f + .18f * p);
        float crack = p > .64f ? Noise(t * 4f, seed ^ 0x91u) * .34f : 0f;
        return (tone * .56f + grit + crack) * (1f - p * .74f);
    }

    // Mineral hulls: a dry, granular split. World-key bits naturally colour
    // every individual rock differently (ice brightens, magma darkens, etc.).
    static float Rock(float t, uint seed)
    {
        float length = Duration(EnemyRole.Rock, seed);
        float p = Mathf.Clamp01(t / length);
        float grit = Noise(t * (1.2f + (seed & 3)), seed);
        float split = Mathf.Sin(6.2831853f * (410f + (seed & 255) * 1.7f) * t) * Mathf.Exp(-p * 6f);
        float chunks = Noise(t * 7f, seed ^ 0x6d2bu) * Mathf.Max(0f, p - .12f) * .42f;
        return (grit * (.62f - p * .38f) + split * .36f + chunks) * (1f - p);
    }

    static float Mine(float t, uint seed)
    {
        float length = Duration(EnemyRole.Mine, seed);
        float p = Mathf.Clamp01(t / length);
        float chirp = Mathf.Sin(6.2831853f * (640f + p * 510f + (seed & 63)) * t) * Mathf.Exp(-p * 2.4f);
        float blast = Noise(t, seed ^ 0xbadu) * Mathf.Clamp01((p - .28f) * 5f) * (1f - p);
        return chirp * .4f + blast * .7f;
    }

    // Deterministic sample-and-hold noise: independent of Random state and
    // consequently identical for a target every time it is encountered.
    static float Noise(float t, uint seed)
    {
        uint n = (uint)(t * Rate / 13f) + seed;
        n ^= n << 13; n ^= n >> 17; n ^= n << 5;
        return ((n & 65535u) / 32767.5f) - 1f;
    }

    static uint Hash(string text)
    {
        uint h = 2166136261u;
        for (int i = 0; i < text.Length; i++) { h ^= text[i]; h *= 16777619u; }
        return h;
    }
}
