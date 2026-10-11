using System.Collections.Generic;
using UnityEngine;

// Per-target destruction audio.
//
// SHARED CLIPS: every non-rock creature (fighters, chasers, aliens, bigs, mines, elites, bosses of all six worlds) plays one of a few
// shared clips, chosen by a deterministic key -> category table (SharedTable) and re-pitched per key at playback (AudioSource.pitch),
// so one small file serves many creatures:
//   yelp   (2 clips)  small scouts / pilots          human (2)  male + female crew     alien (1)  alien pilot / jelly scream
//   screech (1)       insects and alien creatures    whine (1)  machines, drones, mines   roar (1)   bigs, living elites, bosses
// Files: Resources/Audio/EnemyDeath/shared_<category>_<n>.wav (mono, trimmed).
//
// ROCKS keep their own authored clips: a *_rock_* key with WAVs at Resources/Audio/EnemyDeath/<key>_0, _1, ... (Tide / Storm rocks
// alias to another world's rock clips via DeathAlias, under a wet / crack layer).
//
// Playback: a random variant (never the same one twice in a row for that key), a small pitch jitter, a small pool of voices with a
// polyphony cap, a per-key minimum interval and a light duck of rapidly repeated identical keys, so a death combo cannot stack into
// a wall of sound. There is no synthesized fallback: a key with no clips is silent (in the editor a roster / elite key without
// clips logs one warning, and EnemyDeathAudioTest fails on it).
//
// Timing: like any PlayOneShot, authored cues ignore Time.timeScale -- a kill on a frozen (paused) frame is heard at once.
// Voice bookkeeping uses unscaled time.
public static class EnemyDeathAudio
{
    // ---- tunables -----------------------------------------------------------

    // Master switch for the authored WAVs. Off: enemy deaths are silent.
    public static bool AuthoredEnabled = true;
    // Resources folder the clips live in.
    public const string ResourceFolder = "Audio/EnemyDeath/";
    // Probe stops at this many variants per key.
    public const int MaxVariants = 8;
    // Random pitch jitter of a cue: 1 +- this.
    public static float PitchJitter = .03f;
    // Overall gain of an authored cue (applied on top of Volume(role)).
    public static float AuthoredGain = .8f;
    // Base volume of an elite's death cue (elites have no EnemyRole).
    public static float EliteVolume = .8f;
    // Simultaneous death cues / under-layers; past the cap the quietest (then oldest) voice is cut -- or the new cue dropped if it is
    // quieter still. A layer with no free voice is simply skipped.
    public const int MaxVoices = 6, MaxLayerVoices = 2;
    // The wet / crack layer starts this long after its rock cue (seconds).
    public static float LayerDelay = .034f;
    // The same key re-triggering within this many seconds is dropped.
    public static float MinKeyInterval = .03f;
    // Identical keys repeating within DuckWindow seconds get quieter:
    // volume x DuckFactor per recent repeat, never below DuckFloor.
    public static float DuckWindow = .3f, DuckFactor = .8f, DuckFloor = .5f;

    // ---- shared clips ---------------------------------------------------------

    public const string SharedPrefix = "shared_";
    public const string Yelp = "yelp", Human = "human", Alien = "alien", Screech = "screech", Whine = "whine", Roar = "roar";
    public static readonly string[] Categories = { Yelp, Human, Alien, Screech, Whine, Roar };

    // key, category, playback pitch. Fighters 1-4 are the scout / two crewed gunships / the heavy; mines, drones and machine elites
    // whine; bigs, living elites and bosses roar; insects screech; jellies and alien pilots scream.
    public static readonly string[][] SharedTable =
    {
        // Space
        new[] { "space_fighter_1", Yelp, "1.15" }, new[] { "space_fighter_2", Human, "1.0" }, new[] { "space_fighter_3", Human, "1.08" }, new[] { "space_fighter_4", Human, ".88" },
        new[] { "space_chaser", Whine, "1.25" }, new[] { "space_alien", Screech, "1.05" }, new[] { "space_big", Roar, "1.0" }, new[] { "space_mine", Whine, "1.5" },
        new[] { "space_elite_eventide_bastion", Whine, ".7" }, new[] { "space_elite_orbit_reaver", Human, "1.0" },
        new[] { "space_elite_rift_lancer", Human, "1.12" }, new[] { "space_elite_singularity_hauler", Whine, ".6" },
        // Verdant (insects, plants)
        new[] { "verdant_fighter_1", Screech, "1.35" }, new[] { "verdant_fighter_2", Screech, "1.1" }, new[] { "verdant_fighter_3", Screech, "1.0" }, new[] { "verdant_fighter_4", Screech, ".85" },
        new[] { "verdant_chaser", Screech, "1.2" }, new[] { "verdant_alien", Alien, "1.15" }, new[] { "verdant_big", Roar, ".95" }, new[] { "verdant_mine", Whine, "1.4" },
        new[] { "verdant_elite_leafblade", Screech, ".8" }, new[] { "verdant_elite_resin_warden", Roar, ".85" }, new[] { "verdant_elite_sporebloom", Roar, ".9" },
        new[] { "verdant_elite_thornlash", Screech, ".75" }, new[] { "verdant_elite_timber_hauler", Whine, ".65" },
        // Ember
        new[] { "ember_fighter_1", Yelp, "1.0" }, new[] { "ember_fighter_2", Human, "1.0" }, new[] { "ember_fighter_3", Human, ".95" }, new[] { "ember_fighter_4", Alien, ".9" },
        new[] { "ember_chaser", Screech, "1.0" }, new[] { "ember_alien", Alien, "1.0" }, new[] { "ember_big", Roar, "1.1" }, new[] { "ember_mine", Whine, "1.3" },
        new[] { "ember_elite_ash_wraith", Roar, "1.05" }, new[] { "ember_elite_brass_vulture", Screech, ".8" }, new[] { "ember_elite_cauterizer", Whine, ".7" },
        new[] { "ember_elite_coalrunner", Human, ".85" }, new[] { "ember_elite_kilnback", Whine, ".55" }, new[] { "ember_elite_sunstoke", Roar, ".85" },
        // Frost (ice machines; the jelly is the one living unit)
        new[] { "frost_fighter_1", Whine, "1.45" }, new[] { "frost_fighter_2", Whine, "1.25" }, new[] { "frost_fighter_3", Whine, "1.05" }, new[] { "frost_fighter_4", Whine, ".85" },
        new[] { "frost_chaser", Whine, "1.2" }, new[] { "frost_alien", Alien, "1.25" }, new[] { "frost_big", Roar, ".8" }, new[] { "frost_mine", Whine, "1.5" },
        new[] { "frost_elite_cryo_siren", Whine, "1.0" }, new[] { "frost_elite_floe_harrower", Whine, ".7" }, new[] { "frost_elite_glacier_tender", Whine, ".6" },
        new[] { "frost_elite_rimebreaker", Whine, ".8" }, new[] { "frost_elite_whiteout_sentinel", Whine, ".65" },
        // Tide (sea creatures and submersibles)
        new[] { "tide_fighter_1", Yelp, ".95" }, new[] { "tide_fighter_2", Screech, "1.2" }, new[] { "tide_fighter_3", Alien, ".8" }, new[] { "tide_fighter_4", Roar, "1.25" },
        new[] { "tide_chaser", Screech, ".85" }, new[] { "tide_alien", Alien, "1.3" }, new[] { "tide_big", Roar, ".9" }, new[] { "tide_mine", Whine, "1.35" },
        new[] { "tide_elite_riptide", Roar, "1.2" }, new[] { "tide_elite_bathyscaphe", Whine, ".6" }, new[] { "tide_elite_trawler", Whine, ".5" },
        new[] { "tide_elite_thunderfin", Screech, ".7" }, new[] { "tide_elite_pearl_warden", Roar, ".95" },
        // Storm (gunship pilots and drones)
        new[] { "storm_fighter_1", Yelp, "1.1" }, new[] { "storm_fighter_2", Human, "1.12" }, new[] { "storm_fighter_3", Human, ".92" }, new[] { "storm_fighter_4", Human, ".8" },
        new[] { "storm_chaser", Whine, "1.35" }, new[] { "storm_alien", Whine, "1.6" }, new[] { "storm_big", Roar, ".75" }, new[] { "storm_mine", Whine, "1.45" },
        new[] { "storm_elite_skyhook_harrier", Human, "1.18" }, new[] { "storm_elite_refinery_tender", Whine, ".55" }, new[] { "storm_elite_rod_dancer", Human, "1.25" },
        new[] { "storm_elite_anvil_cloud", Roar, ".7" }, new[] { "storm_elite_static_bastion", Whine, ".75" },
        // Desert (skimmer / hawk / gunner pilots, scarab, wisp; the tank, mine and machine elites are machines)
        new[] { "desert_fighter_1", Yelp, "1.3" }, new[] { "desert_fighter_2", Human, "1.15" }, new[] { "desert_fighter_3", Human, "1.0" }, new[] { "desert_fighter_4", Whine, ".8" },
        new[] { "desert_chaser", Screech, "1.3" }, new[] { "desert_alien", Alien, "1.45" }, new[] { "desert_big", Roar, ".85" }, new[] { "desert_mine", Whine, "1.3" },
        new[] { "desert_elite_dune_stalker", Human, "1.0" }, new[] { "desert_elite_caravan_hauler", Whine, ".6" }, new[] { "desert_elite_lens_matriarch", Roar, "1.05" },
        new[] { "desert_elite_glass_cannon", Whine, ".75" }, new[] { "desert_elite_scorpion_walker", Roar, ".9" }, new[] { "desert_elite_mirror_bastion", Whine, ".7" },
        // Bosses: the big roar, pitched down by how huge each hull is
        new[] { "boss_space", Roar, ".65" }, new[] { "boss_frost", Roar, ".7" }, new[] { "boss_verdant", Roar, ".85" },
        new[] { "boss_ember", Roar, ".75" }, new[] { "boss_tide", Roar, ".78" }, new[] { "boss_storm", Roar, ".6" }, new[] { "boss_desert", Roar, ".55" },
    };
    // (not readonly on purpose: TestHarness.Sandbox restores readonly dictionaries by clearing and refilling them, which empties a generic Dictionary)
    static Dictionary<string, string[]> sharedByKey = BuildShared();
    static Dictionary<string, string[]> BuildShared()
    {
        var d = new Dictionary<string, string[]>();
        foreach (var r in SharedTable) d[r[0]] = r;
        return d;
    }
    // The shared category of a key ("" for rocks / unknown keys) and its pitch.
    public static string CategoryOf(string key) { string[] r; return key != null && sharedByKey.TryGetValue(key, out r) ? r[1] : ""; }
    public static float SharedPitchOf(string key)
    {
        string[] r;
        return key != null && sharedByKey.TryGetValue(key, out r) ? float.Parse(r[2], System.Globalization.CultureInfo.InvariantCulture) : 1f;
    }

    // ---- rocks: aliased cues ----------------------------------------------------

    // Death-cue reuse (Tide / Storm rocks): a key with no clips of its own plays a DONOR rock's clips at a pitch (playback speed
    // relative to the donor), with the world's wet / crack layer under it. A key that owns clips never aliases.
    public static readonly string[][] DeathAlias =
    {
        new[] { "tide_rock_brain",    "frost_rock_chunk",       "0.85" },   // brain coral: a heavy crunch, slower
        new[] { "tide_rock_staghorn", "frost_rock_shard",       "1.00" },   // staghorn spire: brittle blade shatter
        new[] { "tide_rock_urchin",   "ember_rock_obsidian",    "1.12" },   // spine urchin: glassy spikes snapping
        new[] { "tide_rock_islet",    "ember_rock_islet",       "0.92" },   // kelp islet: the floating slab again, softer
        new[] { "storm_rock_hail",    "frost_rock_chunk",       "1.20" },   // hailstone: a hard-grain clatter, faster and smaller
        new[] { "storm_rock_ore",     "frost_rock_shard",       "1.08" },   // crystal blade snap
        new[] { "storm_rock_slag",    "ember_rock_obsidian",    "0.82" },   // dense iron thud, slower
        new[] { "storm_rock_islet",   "ember_rock_islet",       "0.85" },   // platform break, a dull crumble
        // Desert rocks: donor rock clips, NO layer (docs/desert-wire.md)
        new[] { "desert_rock_dune",   "frost_rock_chunk",       "0.85" },   // sandstone crumble: a heavy crunch, slower
        new[] { "desert_rock_spire",  "frost_rock_shard",       "1.00" },   // fused glass: brittle snap
        new[] { "desert_rock_husk",   "ember_rock_obsidian",    "0.90" },   // fulgurite tube: glassy crack
        new[] { "desert_rock_slab",   "ember_rock_islet",       "0.88" },   // floating crust slab: a dull crumble
    };
    public const string WetLayer = "tide_wet_";       // under every aliased Tide rock
    public const string StormLayer = "storm_crack_";  // under every aliased Storm rock (thunder crack, rumble, sizzle)
    public static float WetVolume = .6f;               // relative to the death cue
    public static float StormVolume = .55f;
    // The authored layer prefix a key's aliased death wears.
    // Desert rocks wear no layer ("").
    public static string LayerOf(string key) { return IsDesert(key) ? "" : key.StartsWith("storm_") ? StormLayer : WetLayer; }
    public static bool IsDesert(string key) { return key != null && (key.StartsWith("desert_") || key == "boss_desert"); }
    static float LayerVolume(string key) { return key.StartsWith("storm_") ? StormVolume : WetVolume; }
    public static string DonorOf(string key)
    {
        for (int i = 0; i < DeathAlias.Length; i++) if (DeathAlias[i][0] == key) return DeathAlias[i][1];
        return null;
    }

    // ---- test / dev hooks -----------------------------------------------------

    // True: no AudioSource is touched -- the voice bookkeeping runs on its own
    // (edit-mode tests). Clock overrides unscaled time (tests).
    public static bool Simulate;
    public static System.Func<double> Clock;
    public static int Played { get; private set; }      // authored cues started
    public static int Dropped { get; private set; }     // refused: interval / cap
    public static int Stolen { get; private set; }      // voices cut by the cap
    public static AudioClip LastClip { get; private set; }
    public static float LastPitch { get; private set; }   // playback pitch of the last cue (key pitch x jitter)
    public static float LastVolume { get; private set; }
    public static string LastKey { get; private set; }

    // ---- authored state ---------------------------------------------------------

    sealed class KeyClips
    {
        public AudioClip[] deaths, wet;
        public float pitch = 1f, layerVolume = .6f;
        public int lastDeath = -1, repeats;
        public double lastTime = double.NegativeInfinity;
    }

    struct Voice { public AudioSource src; public double start, end; public float volume; }

    static readonly AudioClip[] None = new AudioClip[0];
    static readonly Dictionary<string, KeyClips> authored = new Dictionary<string, KeyClips>();
    static readonly Voice[] voices = new Voice[MaxVoices];
    static readonly Voice[] layerVoices = new Voice[MaxLayerVoices];
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
        PlayKey(key, role);
    }

    // The same cue without a target object (the codex's tapped enemy).
    public static void PlayKey(string key, EnemyRole role)
    {
        if (!(Application.isPlaying || Simulate) || string.IsNullOrEmpty(key)) return;
        if (!PlayAuthored(key, Volume(role))) WarnMissing(key);
    }

    // An elite going down (EliteDeath): its authored cue (its generic
    // explosion plays regardless).
    public static bool PlayElite(string eliteKey)
    {
        if (!(Application.isPlaying || Simulate)) return false;
        bool ok = PlayAuthored(eliteKey, EliteVolume);
        if (!ok) WarnMissing(eliteKey);
        return ok;
    }

    // A boss going down (BossActor.BeginOutro, the codex's tapped boss): the shared roar at the boss's own pitch (SharedTable boss_<artKey>).
    public static float BossVolume = 1f;
    public static string BossKey(string artKey) { return string.IsNullOrEmpty(artKey) ? null : "boss_" + artKey.ToLowerInvariant(); }
    public static bool PlayBossDeath(string artKey)
    {
        if (!(Application.isPlaying || Simulate)) return false;
        string key = BossKey(artKey);
        return key != null && PlayAuthored(key, BossVolume);
    }
    // A boss losing a heart (BossActor.SetHearts): an authored boss_<artKey>_hurt_<n> blow (a short hit on the boss's own material, 0.5-0.7 s, a
    // few dB under its death cue); silent without clips. Never the last heart: that is the death cue.
    public static float BossHurtVolume = .7f;
    public static string BossHurtKey(string artKey) { return string.IsNullOrEmpty(artKey) ? null : BossKey(artKey) + "_hurt"; }
    public static bool PlayBossHurt(string artKey)
    {
        if (!(Application.isPlaying || Simulate)) return false;
        string key = BossHurtKey(artKey);
        return key != null && PlayAuthored(key, BossHurtVolume);
    }

    // Editor safety net: a roster / elite key that resolves no clip is a
    // content bug (a new enemy added without a sound pass). Warn once per key.
    static readonly HashSet<string> warned = new HashSet<string>();
    // Keys whose authored death cues are still to come (the sound job): silent until then, never an error or a warning.
    // (Empty: the Verdant elites' cues were authored in 36b6b04d.)
    public static readonly string[] PendingClips =
    {
    };
    static void WarnMissing(string key)
    {
#if UNITY_EDITOR
        if (!AuthoredEnabled || Variants(key) > 0) return;
        if (EnemyRoster.Find(key) == null && EliteCatalog.Find(key) == null) return;   // hitboxes etc. are silent by design
        if (System.Array.IndexOf(PendingClips, key) >= 0) return;
        if (warned.Add(key)) Debug.LogWarning("[EnemyDeathAudio] no authored death clips for '" + key + "' (Resources/" + ResourceFolder + key + "_0)");
#endif
    }

    // The authored cue (+ the rock wet / crack layer) for a key at a base volume; false
    // when the key has no clips. A cue refused by the interval / cap still returns true (it is handled).
    public static bool PlayAuthored(string key, float baseVolume)
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
        float pitch = k.pitch * (1f + Range(-PitchJitter, PitchJitter));
        Start(ref voices[slot], clip, now, 0f, volume, pitch);
        if (k.wet.Length > 0)
        {
            int ws = FreeSlot(layerVoices, now);
            if (ws >= 0) Start(ref layerVoices[ws], k.wet[PickVariant(k.wet.Length, -1)], now, LayerDelay, volume * k.layerVolume, 1f + Range(-PitchJitter, PitchJitter));
        }
        Played++;
        LastClip = clip; LastVolume = volume; LastKey = key; LastPitch = pitch;
        return true;
    }

    // Authored variants for a key (cached; probed once).
    public static int Variants(string key) { return string.IsNullOrEmpty(key) ? 0 : Clips(key).deaths.Length; }
    public static AudioClip AuthoredClip(string key, int n) { var k = Clips(key); return n >= 0 && n < k.deaths.Length ? k.deaths[n] : null; }

    // Voices still sounding at the bookkeeping clock (tests / dev).
    public static int ActiveVoices() { return Active(voices, Now()); }
    public static int ActiveLayers() { return Active(layerVoices, Now()); }

    // Seeded variant / jitter choices (tests).
    public static void Seed(uint seed) { rng = seed != 0 ? seed : 1u; seeded = true; }

    // Forget voices, key timing and counters (not the clip cache).
    public static void ResetVoices()
    {
        for (int i = 0; i < voices.Length; i++) { if (voices[i].src != null && voices[i].src.isPlaying) voices[i].src.Stop(); voices[i].end = voices[i].start = 0; }
        for (int i = 0; i < layerVoices.Length; i++) { if (layerVoices[i].src != null && layerVoices[i].src.isPlaying) layerVoices[i].src.Stop(); layerVoices[i].end = layerVoices[i].start = 0; }
        foreach (var k in authored.Values) { k.lastTime = double.NegativeInfinity; k.repeats = 0; k.lastDeath = -1; }
        Played = Dropped = Stolen = 0;
        LastClip = null; LastKey = null;
    }

    public static void ClearCache() { ResetVoices(); authored.Clear(); warned.Clear(); }

    static KeyClips Clips(string key)
    {
        KeyClips k;
        if (authored.TryGetValue(key, out k)) return k;
        string[] row;
        if (sharedByKey.TryGetValue(key, out row))
        {
            k = new KeyClips { deaths = Load(SharedPrefix + row[1] + "_"), wet = None, pitch = SharedPitchOf(key) };
            authored[key] = k;
            return k;
        }
        // a key with a '/' is a path under Audio/ (e.g. "Shockwave/shield_release"), not under EnemyDeath/
        k = new KeyClips { deaths = Load(key + "_"), wet = None };
        if (k.deaths.Length == 0)
        {
            string donor = DonorOf(key);
            if (donor != null)
            {
                var d = Clips(donor);
                k.deaths = d.deaths;
                string layer = LayerOf(key);
                k.wet = string.IsNullOrEmpty(layer) ? None : Load(layer); k.layerVolume = LayerVolume(key);
                for (int i = 0; i < DeathAlias.Length; i++)
                    if (DeathAlias[i][0] == key) k.pitch = float.Parse(DeathAlias[i][2], System.Globalization.CultureInfo.InvariantCulture);
            }
        }
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
