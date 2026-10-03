using System.Collections;
using UnityEngine;

// Swaps the gameplay music when the player changes planet.
//
// musicControl drives the main source's volume every frame, so fading the
// outgoing track on that same source would just be overwritten. Instead the old
// clip is handed to a temporary source that fades out on its own while the main
// source starts the new one.
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

        AudioClip next = Resources.Load<AudioClip>("WorldMusic/" + theme.displayName + "Stage01");
        if (next == null)
            next = string.IsNullOrEmpty(theme.musicResource)
                ? sceneDefault
                : Resources.Load<AudioClip>(theme.musicResource);

        var boostGo = GameObject.Find("RocketsSound");
        var boost = boostGo != null ? boostGo.GetComponent<AudioSource>() : null;
        // Every world gets its own short pickup sting.  The clips are sampled
        // from the original boosting track but shaped for a quick blue-atom
        // payoff, so Ember does not fall back to its long upbeat loop.
        var boostClip = Resources.Load<AudioClip>("BoostSounds/" + theme.displayName + "Boost");
        if (boost != null && boostClip != null) boost.clip = boostClip;

        // A missing clip leaves the current track playing rather than dropping
        // into silence -- a half-shipped planet should still have music.
        if (next == null || next == source.clip) return;
        EnsureRunner().StartCoroutine(Swap(source, next));
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

    // Called by WorldManager's guaranteed level clock rather than depending
    // on a temporary crossfade object existing in the scene.
    public static void TryEscalate(WorldManager world)
    {
        // Count only active flight, using WorldManager's level clock. Each
        // stage is a new arrangement of the same original song, with one more
        // instrument joining every 30 seconds. Stage 06 owns the final 30s.
        if (WorldManager.Instance == null)
        {
            return;
        }
        float elapsed = world.WorldLength - world.SecondsLeftInWorld;
        int nextStage = Mathf.Clamp(Mathf.FloorToInt(elapsed / 30f), 0, 5);
        if (nextStage <= musicStage) return;

        var upbeat = Resources.Load<AudioClip>("WorldMusic/" + WorldManager.Current.displayName +
                                               "Stage" + (nextStage + 1).ToString("00"));
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
