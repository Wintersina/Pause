using UnityEngine;

// The kill cam's optional sound hooks. Four cues, each one an optional clip
// loaded from Resources/Audio/KillCam/<cue>: drop a file named exactly
// killed / rewind / replay_start / replay_end (.wav, .ogg or .mp3) into
// Pause/Assets/Resources/Audio/KillCam/ and it plays. A cue with no clip is
// silent: there are no placeholder beeps or coins. (The music itself plays
// backwards through the rewind: see KillCamMusic.)
//
//   killed         the KILLED banner slams in (the frozen fatal frame)
//   rewind         the tape scrubs backwards (over the reversed music)
//   replay_start   the replay begins playing forward in slow motion
//   replay_end     the replay played out on its own (not on a skip)
public static class KillCamAudio
{
    public enum Cue { Killed, Rewind, ReplayStart, ReplayEnd }
    public static readonly string[] Names = { "killed", "rewind", "replay_start", "replay_end" };
    public const string ResourceFolder = "Audio/KillCam/";
    public static float Volume = .9f;

    // tests: requests are counted even though no clip exists
    public static int Requested { get; private set; }
    public static Cue LastRequested { get; private set; }
    public static int Played { get; private set; }

    static readonly AudioClip[] clips = new AudioClip[4];
    static readonly bool[] loaded = new bool[4];
    static AudioSource voice;

    public static string PathOf(Cue cue) { return ResourceFolder + Names[(int)cue]; }

    public static AudioClip Clip(Cue cue)
    {
        int i = (int)cue;
        if (!loaded[i])
        {
            loaded[i] = true;
            clips[i] = Resources.Load<AudioClip>(PathOf(cue));
        }
        return clips[i];
    }

    public static void ClearCache()
    {
        for (int i = 0; i < clips.Length; i++) { clips[i] = null; loaded[i] = false; }
        Requested = Played = 0;
    }

    // True when a clip played. A missing clip, or no play mode, does nothing.
    public static bool Play(Cue cue)
    {
        Requested++;
        LastRequested = cue;
        if (!Application.isPlaying) return false;
        var clip = Clip(cue);
        if (clip == null) return false;
        if (voice == null)
        {
            var host = new GameObject("KillCamAudioVoice");
            Object.DontDestroyOnLoad(host);
            voice = host.AddComponent<AudioSource>();
            voice.playOnAwake = false;
            voice.spatialBlend = 0f;
            voice.ignoreListenerPause = true;
        }
        voice.PlayOneShot(clip, Mathf.Clamp01(Volume));
        Played++;
        return true;
    }
}
