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
//   - progressive (Space, Verdant): six 30-second arrangements,
//     WorldMusic/<World>Stage01..06, stepped up by TryEscalate on the level
//     clock.
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
    // 215.7s, Frost to ~192.0s of 197.1s. Both open with a short fade-in, so
    // the old ending and the new beginning overlap cleanly.
    public static float LoopOutSeconds(string resource)
    {
        switch (resource)
        {
            case EmberTrack: return 212.0f;
            case FrostTrack: return 192.0f;
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

        AudioClip next = UsesStages(theme) ? Resources.Load<AudioClip>(StageResource(theme, 1)) : null;
        if (next == null)
            next = string.IsNullOrEmpty(theme.musicResource)
                ? sceneDefault
                : Resources.Load<AudioClip>(theme.musicResource);

        // Only a full-song world loops at a measured point; anything else
        // (stage arrangements, the scene's own track) loops end to end.
        float measured = UsesStages(theme) ? 0f : LoopOutSeconds(theme.musicResource);
        loopClip = next != null && measured > 0f ? next : null;
        loopOut = loopClip != null ? measured : 0f;

        var boostGo = GameObject.Find("RocketsSound");
        var boost = boostGo != null ? boostGo.GetComponent<AudioSource>() : null;
        // Every world gets its own short pickup sting.  The clips are sampled
        // from the original boosting track but shaped for a quick blue-atom
        // payoff, so Ember does not fall back to its long upbeat loop.
        var boostClip = Resources.Load<AudioClip>("BoostSounds/" + theme.displayName + "Boost");
        if (boost != null && boostClip != null) boost.clip = boostClip;
        // ...and it is decoded now, not inside the blue atom's Play().
        Prewarm(boostClip);

        // A missing clip leaves the current track playing rather than dropping
        // into silence -- a half-shipped planet should still have music.
        if (next == null || next == source.clip)
        {
            if (loopClip != null) EnsureRunner();
            return;
        }
        EnsureRunner().StartCoroutine(Swap(source, next));
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

    static AudioSource FindBackgroundSource()
    {
        var go = GameObject.Find(BackgroundSourceName);
        return go != null ? go.GetComponent<AudioSource>() : null;
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
        if (loopClip == null) return;
        var main = FindBackgroundSource();
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
        EnsureRunner().StartCoroutine(Swap(source, upbeat));
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

    public static bool BossTrackPlaying => bossTrackPlaying;
    public static bool BossFallbackActive => bossPitched;

    public static void BeginBoss(WorldTheme theme)
    {
        var source = FindBackgroundSource();
        if (source == null) return;
        var next = ResolveBossTrack(source.clip, BossClip(theme));
        if (next != null && next != source.clip)
        {
            preBossClip = source.clip;
            bossTrackPlaying = true;
            EnsureRunner().StartCoroutine(Swap(source, next));
        }
        else
        {
            // No boss clip yet: keep the world track, intensified.
            bossPitched = true;
            source.pitch = BossFallbackPitch;
        }
    }

    public static void EndBoss()
    {
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

    static IEnumerator Swap(AudioSource main, AudioClip next)
    {
        float fade = runner != null ? runner.crossfadeSeconds : 1.5f;

        // hand the outgoing track to a throwaway source so it can fade freely
        var oldClip = main.clip;
        float oldVolume = main.volume;
        float oldTime = main.time;

        if (oldClip != null && main.isPlaying)
        {
            var tailGo = new GameObject("~MusicTail");
            var tail = tailGo.AddComponent<AudioSource>();
            tail.clip = oldClip;
            tail.volume = oldVolume;
            tail.loop = false;
            tail.time = Mathf.Min(oldTime, Mathf.Max(0f, oldClip.length - 0.05f));
            tail.Play();
            runner.StartCoroutine(FadeOutAndDie(tail, fade));
        }

        main.clip = next;
        main.loop = true;
        main.time = 0f;
        main.Play();
        yield return null;
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
