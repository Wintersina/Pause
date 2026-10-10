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
// Every roster enemy and elite of all four worlds (Space, Frost, Verdant,
// Ember) has authored clips. There is no synthesized fallback any more: a key
// with no clips is silent (in the editor a roster / elite key without clips
// logs one warning, and EnemyDeathAudioTest fails on it).
//
// Timing: like any PlayOneShot, authored cues ignore
// Time.timeScale -- a kill on a frozen (paused) frame is heard at once --
// and the scream's delay is AudioSource.PlayDelayed (audio clock), so it
// follows the very same rule. Voice bookkeeping uses unscaled time.
public static class EnemyDeathAudio
{
    // ---- tunables -----------------------------------------------------------

    // Master switch for the authored WAVs. Off: enemy deaths are silent.
    public static bool AuthoredEnabled = true;
    // Resources folder the authored clips live in (<key>_<n>, <key>_scream_<n>).
    public const string ResourceFolder = "Audio/EnemyDeath/";
    // Probe stops at this many variants per key.
    public const int MaxVariants = 8;
    // Random pitch jitter of an authored cue / scream: 1 +- this.
    public static float PitchJitter = .03f;
    // Overall gain of an authored cue (applied on top of Volume(role)).
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

    // Scream reuse: a key without scream clips of its own may borrow a donor
    // key's screams at a pitch (e.g. a small unit voiced by a bigger one's
    // cry, pitched up). Empty by default. A borrowed scream plays at
    // BorrowVolume x the normal scream volume and is subject to the same
    // ScreamChance / voice cap. Keys that own screams never borrow.
    public struct Borrow { public string donor; public float pitch; public Borrow(string donor, float pitch) { this.donor = donor; this.pitch = pitch; } }
    public static readonly Dictionary<string, Borrow> ScreamBorrow = new Dictionary<string, Borrow>();
    public static float BorrowVolume = .7f;
    public static float LastScreamPitch { get; private set; }
    public static float LastScreamVolume { get; private set; }

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
        PlayKey(key, role, false);
    }

    // The same cue without a target object (the codex's tapped enemy).
    public static void PlayKey(string key, EnemyRole role, bool forceScream)
    {
        if (!(Application.isPlaying || Simulate) || string.IsNullOrEmpty(key)) return;
        if (!PlayAuthored(key, Volume(role), forceScream)) WarnMissing(key);
    }

    // An elite going down (EliteDeath): its authored cue (its generic
    // explosion plays regardless).
    public static bool PlayElite(string eliteKey, bool forceScream = false)
    {
        if (!(Application.isPlaying || Simulate)) return false;
        bool ok = PlayAuthored(eliteKey, EliteVolume, forceScream);
        if (!ok) WarnMissing(eliteKey);
        return ok;
    }

    // Editor safety net: a roster / elite key that resolves no clip is a
    // content bug (a new enemy added without a sound pass). Warn once per key.
    static readonly HashSet<string> warned = new HashSet<string>();
    static void WarnMissing(string key)
    {
#if UNITY_EDITOR
        if (!AuthoredEnabled || Variants(key) > 0) return;
        if (EnemyRoster.Find(key) == null && EliteCatalog.Find(key) == null) return;   // hitboxes etc. are silent by design
        if (key.StartsWith("tide_")) return;   // TODO(sounds): Tide's authored death cues are still to come (checklist section F)
        if (warned.Add(key)) Debug.LogWarning("[EnemyDeathAudio] no authored death clips for '" + key + "' (Resources/" + ResourceFolder + key + "_0)");
#endif
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

        var sk = k;
        float screamPitch = 1f, screamGain = 1f;
        if (k.screams.Length == 0 && ScreamBorrow.Count > 0)
        {
            Borrow b;
            if (ScreamBorrow.TryGetValue(key, out b) && b.donor != key)
            {
                var d = Clips(b.donor);
                if (d.screams.Length > 0) { sk = d; screamPitch = b.pitch; screamGain = BorrowVolume; }
            }
        }
        if (sk.screams.Length > 0 && (forceScream || Next01() < ScreamChance))
        {
            int s = FreeSlot(screamVoices, now);
            if (s >= 0)
            {
                int sv = PickVariant(sk.screams.Length, sk.lastScream);
                sk.lastScream = sv;
                float delay = Range(ScreamDelayMin, ScreamDelayMax);
                float sVol = volume * ScreamVolume * screamGain;
                float sPitch = screamPitch * (1f + Range(-PitchJitter, PitchJitter));
                Start(ref screamVoices[s], sk.screams[sv], now, delay, sVol, sPitch);
                Screams++;
                LastScream = sk.screams[sv]; LastScreamDelay = delay;
                LastScreamPitch = sPitch; LastScreamVolume = sVol;
            }
        }
        return true;
    }

    // Authored variants for a key (cached; probed once).
    public static int Variants(string key) { return string.IsNullOrEmpty(key) ? 0 : Clips(key).deaths.Length; }
    public static int ScreamVariants(string key) { return string.IsNullOrEmpty(key) ? 0 : Clips(key).screams.Length; }
    public static AudioClip AuthoredClip(string key, int n) { var k = Clips(key); return n >= 0 && n < k.deaths.Length ? k.deaths[n] : null; }
    public static AudioClip ScreamClip(string key, int n) { var k = Clips(key); return n >= 0 && n < k.screams.Length ? k.screams[n] : null; }

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

    public static void ClearCache() { ResetVoices(); authored.Clear(); warned.Clear(); }

    static KeyClips Clips(string key)
    {
        KeyClips k;
        if (authored.TryGetValue(key, out k)) return k;
        // a key with a '/' is a path under Audio/ (e.g. "Shockwave/shield_release"), not under EnemyDeath/
        k = new KeyClips { deaths = Load(key + "_"), screams = Load(key + "_scream_") };
        authored[key] = k;
        return k;
    }

    static AudioClip[] Load(string prefix)
    {
        List<AudioClip> found = null;
        for (int n = 0; n < MaxVariants; n++)
        {
            var c = Resources.Load<AudioClip>((prefix.IndexOf('/') >= 0 ? "Audio/" : ResourceFolder) + prefix + n);
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
}
