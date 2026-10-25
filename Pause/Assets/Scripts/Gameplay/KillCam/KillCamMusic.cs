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
    static bool pausedMusic, wasMuted;   // pausedMusic: the song is muted under the rewind
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
        // The song is MUTED, not paused: it keeps running underneath, so nothing has to resume it
        // (Pause/UnPause on the streamed clip left the replay silent) and it simply carries on forward.
        wasMuted = music.mute;
        music.mute = true;
        pausedMusic = true;
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
        if (pausedMusic && music != null)
        {
            music.mute = wasMuted;
            // belt and braces: whatever stopped it, the song is made to play again
            if (music.clip != null && !music.isPlaying) music.Play();
        }
        pausedMusic = false;
        Reversing = false;
        EndMuffle();
    }

    // ---- the muffle: the song at full volume and normal pitch, behind a low-pass, through the slow motion ----
    // An AudioLowPassFilter on the music source (a cheap built-in effect, fine on mobile). Its cutoff
    // eases from open to MuffleCutoff over MuffleEaseIn when the replay starts, and back to open over
    // MuffleEaseOut as the replay ends. Restore()/EndMuffle() take it off on every exit.
    public const float MuffleCutoff = 1500f;   // Hz at full muffle
    public const float OpenCutoff = 22000f;    // Hz, fully open
    public const float MuffleEaseIn = .25f;    // s, open -> muffled
    public const float MuffleEaseOut = .2f;    // s, muffled -> open

    static AudioLowPassFilter lowPass;
    static bool lowPassAdded, muffling;
    static float muffle;   // 0 open .. 1 muffled

    public static bool Muffling { get { return muffling; } }
    public static float MuffleAmount { get { return muffle; } }

    // Cutoff for a muffle amount: eased in log space so the sweep sounds even.
    public static float CutoffFor(float amount)
    {
        return Mathf.Exp(Mathf.Lerp(Mathf.Log(OpenCutoff), Mathf.Log(MuffleCutoff), Mathf.Clamp01(amount)));
    }

    // KillCam.Step, in the Play phase: `releasing` once the replay is on its closing hold.
    public static void StepMuffle(float dt, bool releasing)
    {
        if (!Application.isPlaying || music == null) return;
        if (!muffling)
        {
            if (releasing) return;
            muffling = true;
            lowPass = music.GetComponent<AudioLowPassFilter>();
            lowPassAdded = lowPass == null;
            if (lowPass == null) lowPass = music.gameObject.AddComponent<AudioLowPassFilter>();
            lowPass.cutoffFrequency = OpenCutoff;
            lowPass.enabled = true;
        }
        muffle = releasing ? Mathf.Max(0f, muffle - dt / MuffleEaseOut) : Mathf.Min(1f, muffle + dt / MuffleEaseIn);
        if (lowPass != null) lowPass.cutoffFrequency = CutoffFor(muffle);
    }

    // Every exit: the filter is gone and the song is fully open.
    public static void EndMuffle()
    {
        if (!muffling) return;
        muffling = false;
        muffle = 0f;
        if (lowPass != null)
        {
            lowPass.cutoffFrequency = OpenCutoff;
            lowPass.enabled = false;
            if (lowPassAdded) { if (Application.isPlaying) Object.Destroy(lowPass); else Object.DestroyImmediate(lowPass); }
        }
        lowPass = null;
    }
}
