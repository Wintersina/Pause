using UnityEngine;

// The boss warning's sound: three tiny procedural clips in the same idiom
// as UltimateShotSound (built once at load, one cached AudioSource, no audio
// files). An authored sting / riser would replace Build*() one for one.
//
//   Announce  a two-note rising klaxon
//   Second    a low heartbeat thump, T-10 .. T-4, a little louder each second
//   Final     the charge-tick blip, pitched up on 3, 2, 1
//
// Nothing plays on Arrive: the boss intro and WorldMusic.BeginBoss own that.
public static class BossWarningAudio
{
    static AudioClip alarm, thump, blip;
    static AudioSource source;

    // Built at scene load so the first warning doesn't hitch.
    public static void Prewarm()
    {
        if (alarm == null) alarm = BuildAlarm();
        if (thump == null) thump = BuildThump();
        if (blip == null) blip = BuildBlip();
    }

    public static void Play(BossWarningBeat beat, int seconds)
    {
        if (!BossWarningConfig.AudioEnabled || !Application.isPlaying) return;
        float volume = BossWarningConfig.AudioVolume;
        switch (beat)
        {
            case BossWarningBeat.Announce:
                OneShot(alarm, 1f, volume);
                break;
            case BossWarningBeat.Second:
                if (seconds > BossWarningConfig.FinalAt)
                    OneShot(thump, 1f, volume * Mathf.Lerp(1.3f, .7f, Mathf.InverseLerp(BossWarningConfig.FinalAt, BossWarningConfig.CloseAt, seconds)));
                else
                    OneShot(blip, 1f + (BossWarningConfig.FinalAt - seconds) * .19f, volume * 1.1f);
                break;
        }
    }

    static void OneShot(AudioClip clip, float pitch, float volume)
    {
        if (clip == null) return;
        if (source == null)
        {
            var host = new GameObject("~BossWarningAudio");
            source = host.AddComponent<AudioSource>();
            source.spatialBlend = 0f;
            source.playOnAwake = false;
        }
        source.pitch = pitch;
        source.PlayOneShot(clip, Mathf.Clamp01(volume));
    }

    const int Rate = 22050;

    static AudioClip Make(string name, float seconds, System.Func<float, float> wave)
    {
        int count = Mathf.RoundToInt(seconds * Rate);
        var clip = AudioClip.Create(name, count, 1, Rate, false);
        var data = new float[count];
        for (int i = 0; i < count; i++) data[i] = wave(i / (float)Rate);
        clip.SetData(data, 0);
        return clip;
    }

    // Two square-ish notes, the second a fifth up, each with a short decay.
    static AudioClip BuildAlarm()
    {
        return Make("boss_warning_alarm", .62f, t =>
        {
            bool second = t >= .26f;
            float local = second ? t - .26f : t;
            float hz = second ? 660f : 440f;
            float phase = local * hz;
            float square = phase % 1f < .5f ? 1f : -1f;
            float sine = Mathf.Sin(phase * Mathf.PI * 2f);
            float attack = Mathf.Clamp01(local / .008f);
            return (square * .35f + sine * .65f) * attack * Mathf.Exp(-local * (second ? 7f : 11f)) * .34f;
        });
    }

    // Lub-dub: two low sine thumps.
    static AudioClip BuildThump()
    {
        return Make("boss_warning_thump", .34f, t =>
        {
            float a = Mathf.Sin(t * 78f * Mathf.PI * 2f) * Mathf.Exp(-t * 26f);
            float u = t - .15f;
            float b = u > 0f ? Mathf.Sin(u * 64f * Mathf.PI * 2f) * Mathf.Exp(-u * 30f) * .7f : 0f;
            return (a + b) * Mathf.Clamp01(t / .004f) * .6f;
        });
    }

    static AudioClip BuildBlip()
    {
        return Make("boss_warning_blip", .11f, t =>
        {
            float square = Mathf.Sin(t * 880f * Mathf.PI * 2f) > 0f ? 1f : -1f;
            return square * Mathf.Exp(-t * 38f) * .3f;
        });
    }
}
