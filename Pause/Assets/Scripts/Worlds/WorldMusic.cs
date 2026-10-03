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
