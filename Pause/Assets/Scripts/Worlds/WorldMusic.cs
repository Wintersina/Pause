using System.Collections;
using UnityEngine;

// Swaps the gameplay music when the player changes planet.
//
// musicControl drives the main source's volume every frame, so fading the
// outgoing track on that same source would just be overwritten. Instead the old
// clip is handed to a temporary source that fades out on its own while the main
// source starts the new one.
//
// Two kinds of world music:
//   - progressive (no world uses it today; the Space/Verdant stage clips were
//     deleted): six arrangements WorldMusic/<World>Stage01..06, stepped up by
//     TryEscalate on the level clock. Kept for future use; a theme only opts
//     in with progressiveMusic = true. Space and Verdant play the scene's own
//     default track.
//   - a full song (Frost, Ember): the theme's musicResource plays for the whole
//     level. The songs end in a fade-out and a second or two of silence, so a
//     plain AudioSource.loop would leave a gap. LoopOutSeconds gives each song a
//     loop-out point just before its fade; there the main source jumps back to
//     the top while a tail source plays the song's own fade-out over the
//     restart (see Update / LoopNow).
public class WorldMusic : MonoBehaviour
{
    const string BackgroundSourceName = "MovingMusic";

    [Tooltip("Seconds to fade the previous planet's track out.")]
    public float crossfadeSeconds = 1.5f;

    static WorldMusic runner;
    static AudioClip sceneDefault;
    static bool captured;
    // Six progressive 30-second arrangements, with stage 0 playing at launch.
    static int musicStage;

    // ---- full-song worlds ---------------------------------------------------

    public const string EmberTrack = "WorldMusic/Ember_Main";
    public const string FrostTrack = "WorldMusic/Frost_Main";
    public const string VerdantTrack = "WorldMusic/Verdant_Main";
    public const string TideTrack = "WorldMusic/Tide_Main";
    public const string StormTrack = "WorldMusic/Storm_Main";
    // Dune Circuit (199.15 s): the Desert world's song. The Desert branch only has to set
    // musicResource = WorldMusic.DesertTrack on its WorldTheme row.
    public const string DesertTrack = "WorldMusic/Desert_Main";
    // Space's own song (Neon Solitude, 212.8 s): plays in the Space sky, the
    // space leg between worlds and the loop's return. It fades in from silence
    // and ends on a 50 ms fade, so a plain end-to-end loop is clean.
    public const string SpaceTrack = "WorldMusic/Space_Main";


    // The full song on the main source, and where it loops back.
    static AudioClip loopClip;
    static float loopOut;

    public static bool UsesStages(WorldTheme theme)
    {
        return theme != null && theme.progressiveMusic;
    }

    public static string StageResource(WorldTheme theme, int stage)
    {
        return "WorldMusic/" + theme.displayName + "Stage" + stage.ToString("00");
    }

    // Loop-out time for a full-song resource: where the song starts its
    // closing fade, or 0 when it has none (it then just loops end to end).
    // Measured from the source MP3s: Ember holds full level to ~212.0s of
    // 215.7s, Frost (Frozen Pulse WAV) to ~203.8s of 207.6s. Both open with a short fade-in, so
    // the old ending and the new beginning overlap cleanly.
    public static float LoopOutSeconds(string resource)
    {
        switch (resource)
        {
            case EmberTrack: return 225.5f;   // Anvil Arpeggio: full level to ~225.5 s of 229.9 s
            case FrostTrack: return 203.8f;
            case DesertTrack: return 197.8f;   // Dune Circuit: full level to ~197.8 s of 199.15 s
            case StormTrack: return 118.9f;   // Static Storm (mp3): full level to ~119.1 s of 120.0 s, then a short near-silent tail
            case TideTrack: return 205.9f;   // Deep Sonar Ping: full level to ~205.9 s of 208.4 s
            case VerdantTrack: return 197.0f;   // Jungle Night Pulse: full level to ~197.0 s of 200.0 s
            default: return 0f;
        }
    }

    // True once a playing full song has reached its loop-out point.
    public static bool PastLoopPoint(float time, float loopOutSeconds, float clipLength)
    {
        return loopOutSeconds > 0f && loopOutSeconds < clipLength && time >= loopOutSeconds;
    }

    public static AudioClip LoopClip => loopClip;

    public static void Apply(WorldTheme theme)
    {
        if (theme == null) return;
        musicStage = 0;

        var source = FindBackgroundSource();
        if (source == null) return;

        if (!captured)
        {
            captured = true;
            sceneDefault = source.clip;
        }

        // The world's own song counts as heard for the codex MUSIC tab (the tutorial never counts).
        CodexMusic.HeardWorld(theme);

        AudioClip next = UsesStages(theme) ? LoadClip(StageResource(theme, 1)) : null;
        if (next == null)
            next = string.IsNullOrEmpty(theme.musicResource)
                ? sceneDefault
                : LoadClip(theme.musicResource);

        // Only a full-song world loops at a measured point; anything else
        // (stage arrangements, the scene's own track) loops end to end.
        float measured = UsesStages(theme) ? 0f : LoopOutSeconds(theme.musicResource);
        loopClip = next != null && measured > 0f ? next : null;
        loopOut = loopClip != null ? measured : 0f;

        // The per-world BoostSounds stings were deleted: the RocketsSound
        // source keeps the scene's own clip for every world. Whatever it is,
        // decode it now rather than inside the blue atom's Play().
        var boostGo = GameObject.Find("RocketsSound");
        var boost = boostGo != null ? boostGo.GetComponent<AudioSource>() : null;
        if (boost != null) Prewarm(boost.clip);

        // A missing clip leaves the current track playing rather than dropping
        // into silence -- a half-shipped planet should still have music.
        if (next == null || next == source.clip)
        {
            if (loopClip != null) EnsureRunner();
            return;
        }
        EnsureRunner().StartCoroutine(Swap(source, next, 0f, true));
    }

    // ---- preloading the next world's song ------------------------------------
    //
    // The world songs are Streaming imports (loadType 2): Resources.Load only makes the clip object, but the first
    // Play() still opens the file and decodes its first buffers on the frame of the swap. PreloadNext asks for the
    // song with Resources.LoadAsync while the boss fight / lift-off runs (WorldManager.PreloadNextWorld) and
    // LoadAudioData()s it on the frame it arrives; Apply then takes the held clip (LoadClip), so the swap's Load is a cache hit.
    static readonly System.Collections.Generic.Dictionary<string, AudioClip> heldClips = new System.Collections.Generic.Dictionary<string, AudioClip>();
    static readonly System.Collections.Generic.Dictionary<string, ResourceRequest> asking = new System.Collections.Generic.Dictionary<string, ResourceRequest>();
    static readonly System.Collections.Generic.List<string> askDone = new System.Collections.Generic.List<string>(2);
    public static int PreloadRequests { get; private set; }

    // Asks for `theme`'s song (each path once). Several themes are fine (the destination's and the lift-off interlude's).
    public static void PreloadNext(WorldTheme theme)
    {
        if (theme == null || !Application.isPlaying || string.IsNullOrEmpty(theme.musicResource) || UsesStages(theme)) return;
        string path = theme.musicResource;
        if (heldClips.ContainsKey(path) || asking.ContainsKey(path)) return;
        PreloadRequests++;
        asking[path] = Resources.LoadAsync<AudioClip>(path);
        EnsureRunner().enabled = true;
    }

    // The swap's loader: the preloaded clip when there is one (released to the caller), else the synchronous load.
    static AudioClip LoadClip(string path)
    {
        AudioClip c;
        if (heldClips.TryGetValue(path, out c) && c != null) { heldClips.Remove(path); return c; }
        return Resources.Load<AudioClip>(path);
    }

    // One frame of PreloadNext: finished requests become held clips, decoded now rather than inside the swap's Play().
    static void PumpPreload()
    {
        if (asking.Count == 0) return;
        askDone.Clear();
        foreach (var kv in asking)
        {
            if (!kv.Value.isDone) continue;
            askDone.Add(kv.Key);
            var clip = kv.Value.asset as AudioClip;
            if (clip == null) continue;
            heldClips[kv.Key] = clip;
            Prewarm(clip);
        }
        for (int i = 0; i < askDone.Count; i++) asking.Remove(askDone[i]);
    }

    // Brings a one-shot's audio into memory ahead of its first Play(). A clip
    // imported without "Preload Audio Data" is only a header after
    // Resources.Load: the first Play() then reads and decompresses it on the
    // main thread, on the very frame the sound is wanted (the blue atom's
    // sting did exactly that, once per world per run). The stings are
    // imported preloaded + load-in-background; this is the safety net for a
    // clip that isn't (blocking then, but at world-apply / ship-spawn time).
    public static void Prewarm(AudioClip clip)
    {
        if (clip != null && clip.loadState == AudioDataLoadState.Unloaded) clip.LoadAudioData();
    }

    // Cached: Update asks for it every frame. A destroyed source (scene change)
    // is looked up again, at most once a second while none exists.
    static AudioSource cachedBackground;
    static float nextBackgroundLookupAt;

    static AudioSource FindBackgroundSource(bool everyFrame = false)
    {
        if (cachedBackground != null) return cachedBackground;
        if (everyFrame)
        {
            if (Time.unscaledTime < nextBackgroundLookupAt) return null;
            nextBackgroundLookupAt = Time.unscaledTime + 1f;
        }
        var go = GameObject.Find(BackgroundSourceName);
        cachedBackground = go != null ? go.GetComponent<AudioSource>() : null;
        return cachedBackground;
    }

    static WorldMusic EnsureRunner()
    {
        if (runner == null)
            runner = new GameObject("~WorldMusic").AddComponent<WorldMusic>();
        return runner;
    }

    // Seamless loop for full songs. Update runs during the freeze (timeScale 0)
    // too, and only reads the source, so musicControl's pause dimming carries
    // on exactly as before. A boss track (or anything else) on the source
    // leaves it alone; the boss fallback keeps the world song and so keeps
    // looping.
    void Update()
    {
        PumpPreload();
        if (loopClip == null) return;
        var main = FindBackgroundSource(everyFrame: true);
        if (main == null || main.clip != loopClip || !main.isPlaying) return;
        if (PastLoopPoint(main.time, loopOut, loopClip.length)) LoopNow(main);
    }

    static void LoopNow(AudioSource main)
    {
        var clip = main.clip;
        float at = main.time;

        // The tail plays the song's own closing fade on a separate source...
        var tail = new GameObject("~MusicLoopTail").AddComponent<AudioSource>();
        tail.clip = clip;
        tail.loop = false;
        tail.outputAudioMixerGroup = main.outputAudioMixerGroup;
        tail.spatialBlend = main.spatialBlend;
        tail.priority = main.priority;
        tail.pitch = main.pitch;
        tail.volume = main.volume;
        SoundSettings.MarkMusic(tail);
        tail.time = at;
        tail.Play();

        // ...while the main source (the one musicControl drives) starts over.
        main.time = 0f;
        if (!main.isPlaying) main.Play();

        float remaining = Mathf.Max(0.1f, clip.length - at);
        EnsureRunner().StartCoroutine(FollowAndFade(tail, main, remaining));
    }

    // The tail follows the main source's volume and pitch (pause dimming, the
    // boss pitch nudge) and is faded out by the time the song would end.
    static IEnumerator FollowAndFade(AudioSource tail, AudioSource main, float seconds)
    {
        for (float t = 0f; t < seconds; t += Time.unscaledDeltaTime)
        {
            if (tail == null) yield break;
            if (main != null)
            {
                tail.volume = main.volume * (1f - t / seconds);
                tail.pitch = main.pitch;
            }
            yield return null;
        }
        if (tail != null) Destroy(tail.gameObject);
    }

    // Called by WorldManager's guaranteed level clock rather than depending
    // on a temporary crossfade object existing in the scene. Full-song worlds
    // (Frost, Ember) have no stages and never escalate.
    public static void TryEscalate(WorldManager world)
    {
        // Count only active flight, using WorldManager's level clock. Each
        // stage is a new arrangement of the same original song, with one more
        // instrument joining every sixth of the world. Stage 06 owns the last.
        if (WorldManager.Instance == null || !UsesStages(WorldManager.Current))
        {
            return;
        }
        // Worlds are a distance now (faster flight ends them sooner), so the
        // stages follow the share of it flown: six equal steps.
        int nextStage = Mathf.Clamp(Mathf.FloorToInt(world.Progress01 * 6f), 0, 5);
        if (nextStage <= musicStage) return;

        var upbeat = Resources.Load<AudioClip>(StageResource(WorldManager.Current, nextStage + 1));
        var source = FindBackgroundSource();
        if (upbeat == null || source == null || source.clip == upbeat) return;
        musicStage = nextStage;
        EnsureRunner().StartCoroutine(Swap(source, upbeat, 0f, true));
    }

    // ---- boss music ---------------------------------------------------------
    //
    // TODO(boss-music): the user will supply a custom track per world boss.
    // Drop each one at Pause/Assets/Audio/Resources/WorldMusic/Boss_<World>
    // (.wav or .ogg): Boss_Space, Boss_Frost, Boss_Verdant, Boss_Ember.
    // BeginBoss picks it up automatically (Resources.Load by BossResource),
    // crossfades into it for the encounter and EndBoss swaps the world's own
    // track back. Until a clip exists the world's current track simply keeps
    // playing, nudged up in pitch (BossFallbackPitch) so the fight still
    // sounds different. See docs/TODO.md.
    public const string BossResourcePrefix = "WorldMusic/Boss_";
    public const float BossFallbackPitch = 1.06f;

    static AudioClip preBossClip;
    static bool bossTrackPlaying, bossPitched;

    public static string BossResource(WorldTheme theme)
    {
        return theme == null ? null : BossResourcePrefix + theme.displayName;
    }

    public static AudioClip BossClip(WorldTheme theme)
    {
        string path = BossResource(theme);
        return path == null ? null : Resources.Load<AudioClip>(path);
    }

    // What plays during the fight: the boss's own track if it exists,
    // otherwise whatever the world is already playing.
    public static AudioClip ResolveBossTrack(AudioClip worldTrack, AudioClip bossTrack)
    {
        return bossTrack != null ? bossTrack : worldTrack;
    }

    // The BOSS INCOMING warning (BossWarningHud, ~30 s ahead): load the boss
    // song's audio now, so BeginBoss -- the entrance's first frame -- starts
    // it on that frame rather than decoding it there.
    public static void PrewarmBoss(WorldTheme theme)
    {
        Prewarm(BossClip(theme));
    }

    public static bool BossTrackPlaying => bossTrackPlaying;
    public static bool BossFallbackActive => bossPitched;

    public static void BeginBoss(WorldTheme theme)
    {
        var source = FindBackgroundSource();
        if (source == null) return;
        // A fight that never reached EndBoss (a scene left mid-fight) must not
        // leak its flags or its remembered world clip into this one.
        bossTrackPlaying = bossPitched = false;
        preBossClip = null;
        var next = ResolveBossTrack(source.clip, BossClip(theme));
        if (next != null && next != source.clip)
        {
            preBossClip = source.clip;
            bossTrackPlaying = true;
            if (BossClip(theme) == next) CodexMusic.HeardBoss(theme);
            EnsureRunner().StartCoroutine(Swap(source, next, BossClip(theme) == next ? BossStartSeconds(theme) : 0f));
            // the boss song's beat clock (Scripts/Audio/BeatClock.cs): the fight's volleys follow it
            if (BossClip(theme) == next) BossBeat.Attach(source, theme.displayName);
        }
        else
        {
            BossBeat.Detach();
            // No boss clip yet: keep the world track, intensified.
            bossPitched = true;
            source.pitch = BossFallbackPitch;
        }
    }

    public static void EndBoss()
    {
        BossBeat.Detach();
        var source = FindBackgroundSource();
        if (bossPitched)
        {
            bossPitched = false;
            if (source != null) source.pitch = 1f;
        }
        if (bossTrackPlaying)
        {
            bossTrackPlaying = false;
            if (source != null && preBossClip != null && Application.isPlaying)
                EnsureRunner().StartCoroutine(Swap(source, preBossClip));
            preBossClip = null;
        }
    }

    // Where the boss song starts playing: a map with a long quiet intro (Verdant) names a bar to start at instead.
    public static float BossStartSeconds(WorldTheme theme)
    {
        var map = theme == null ? null : BeatMaps.For(theme.displayName);
        return map == null ? 0f : map.StartSeconds;
    }

    // Multiplies the source's volume (musicControl): the new song rises over the old one's fade-out, an equal-power-ish
    // crossfade instead of a hard cut in. 1 whenever no swap is fading in.
    public static float FadeGain { get; private set; } = 1f;
    static int swapId;
    // A new scene's source starts at full gain whatever a swap that died with the old scene left behind.
    public static void ResetFade() { swapId++; FadeGain = 1f; }

    static IEnumerator Swap(AudioSource main, AudioClip next, float startAt = 0f, bool fadeIn = false)
    {
        int id = ++swapId;
        FadeGain = 1f;
        float fade = runner != null ? runner.crossfadeSeconds : 1.5f;

        // hand the outgoing track to a throwaway source so it can fade freely
        var oldClip = main.clip;
        float oldVolume = main.volume;
        float oldTime = main.time;

        // A source that has only just started (the scene's own track at the
        // run's first Apply) has nothing worth fading out.
        if (oldClip != null && main.isPlaying && oldTime > 0.3f)
        {
            var tailGo = new GameObject("~MusicTail");
            var tail = tailGo.AddComponent<AudioSource>();
            tail.clip = oldClip;
            tail.volume = oldVolume;
            SoundSettings.MarkMusic(tail);
            tail.loop = false;
            tail.time = Mathf.Min(oldTime, Mathf.Max(0f, oldClip.length - 0.05f));
            tail.Play();
            runner.StartCoroutine(FadeOutAndDie(tail, fade));
        }
        else fadeIn = false;   // nothing was playing (the run's first track): no crossfade to make
        if (fadeIn) { FadeGain = 0f; main.volume = 0f; }

        main.clip = next;
        main.loop = true;
        main.time = startAt > 0f && startAt < next.length ? startAt : 0f;
        main.Play();
        if (!fadeIn) { yield return null; yield break; }
        float rise = runner != null ? runner.crossfadeSeconds : 1.5f;
        for (float t = 0f; t < rise; t += Time.unscaledDeltaTime)
        {
            if (id != swapId) yield break;   // a newer swap owns the gain
            float k = t / rise;
            FadeGain = k * k * (3f - 2f * k);
            yield return null;
        }
        if (id == swapId) FadeGain = 1f;
    }

    static IEnumerator FadeOutAndDie(AudioSource src, float seconds)
    {
        float start = src.volume;
        for (float t = 0f; t < seconds; t += Time.unscaledDeltaTime)
        {
            if (src == null) yield break;
            src.volume = Mathf.Lerp(start, 0f, t / seconds);
            yield return null;
        }
        if (src != null) Destroy(src.gameObject);
    }
}
