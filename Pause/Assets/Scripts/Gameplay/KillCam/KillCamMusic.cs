using UnityEngine;

// The song through the kill cam's rewind. The run's music is a STREAMING
// clip (Audio/Resources/WorldMusic/*.mp3, loadType Streaming), which can be
// neither read with GetData nor played with a negative pitch (and mobile
// does not reverse streams), so the reverse is made from what was actually
// heard: KillCamMusicTap, a filter on the music source, keeps the last
// couple of seconds of its output in a ring. At the start of the rewind the
// song is paused where it is, the last second of that ring is reversed into
// a small clip and played at double pitch (a tape winding back over the
// rewind's half second); when the slow-motion replay begins the song is
// un-paused and carries on forward from the exact point it stopped. The
// replay does not detune the music (the kill cam never touches timeScale or
// the source's pitch).
//
// Restore() undoes everything on every exit (finish, skip, the 8 s cap, a
// revive, scene teardown) and is safe to call any number of times.
public static class KillCamMusic
{
    public const float ReverseSeconds = 1f;   // of song, played back at ReversePitch
    public const float ReversePitch = 2f;

    static AudioSource music;      // the run's music source
    static AudioSource voice;      // plays the reversed buffer
    static AudioClip reversed;
    static bool pausedMusic;
    static float[] scratch;

    public static bool Reversing { get; private set; }
    public static bool MusicPaused { get { return pausedMusic; } }
    public static KillCamMusicTap Tap { get; private set; }

    // musicControl.Start: the run's music source gets its tap.
    public static void Attach(AudioSource source)
    {
        if (source == null) return;
        music = source;
        Tap = source.GetComponent<KillCamMusicTap>();
        if (Tap == null) Tap = source.gameObject.AddComponent<KillCamMusicTap>();
    }

    // The rewind starts: pause the song, play its last moments backwards.
    public static void BeginReverse()
    {
        if (Reversing) return;
        if (!Application.isPlaying || music == null || Tap == null) return;
        int channels, rate;
        int frames = Tap.Snapshot(ReverseSeconds, ref scratch, out channels, out rate);
        if (frames <= 0 || channels <= 0) return;
        if (music.isPlaying) { music.Pause(); pausedMusic = true; }
        reversed = AudioClip.Create("KillCamReverse", frames, channels, rate, false);
        reversed.SetData(scratch, 0);
        if (voice == null)
        {
            var host = new GameObject("KillCamMusicVoice");
            Object.DontDestroyOnLoad(host);
            voice = host.AddComponent<AudioSource>();
            voice.playOnAwake = false;
            voice.spatialBlend = 0f;
            voice.loop = false;
            voice.ignoreListenerPause = true;
            voice.ignoreListenerVolume = true;   // the music bus, like the song itself
        }
        voice.clip = reversed;
        voice.pitch = ReversePitch;
        voice.volume = 1f;   // the tap heard the song after its source volume (music gain, pause dimming)
        voice.time = 0f;
        voice.Play();
        Reversing = true;
    }

    // The song is back to normal: stop the reverse, let the song run on from where it paused.
    public static void Restore()
    {
        if (voice != null) { voice.Stop(); voice.clip = null; voice.pitch = 1f; }
        if (reversed != null)
        {
            if (Application.isPlaying) Object.Destroy(reversed); else Object.DestroyImmediate(reversed);
            reversed = null;
        }
        if (pausedMusic && music != null) music.UnPause();
        pausedMusic = false;
        Reversing = false;
    }
}
