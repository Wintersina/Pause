using UnityEngine;

// Short, distinct cues with a shared rate limit: twelve drones never produce twelve overlapping hit sounds.
public static class GunImpactFeedback
{
    static AudioClip light, heavy, kill;
    static AudioSource[] voices;
    static GameObject host;
    static int next;
    static float lastSound = -10f, lastKick = -10f;
    public static int Played { get; private set; }
    public static void Prewarm()
    {
        if (light == null) light = Clip("QuarterHit", .035f, 1100f);
        if (heavy == null) heavy = Clip("HeartBreak", .065f, 480f);
        if (kill == null) kill = Clip("GunKill", .085f, 220f);
        if (Application.isPlaying && host == null)
        {
            host = new GameObject("~GunImpactAudio"); Object.DontDestroyOnLoad(host);
            voices = new AudioSource[4];
            for (int i = 0; i < voices.Length; i++) { voices[i] = host.AddComponent<AudioSource>(); voices[i].playOnAwake = false; voices[i].spatialBlend = 0f; }
        }
    }
    static AudioClip Clip(string name, float seconds, float frequency)
    {
        const int rate = 22050;
        var samples = new float[Mathf.CeilToInt(seconds * rate)];
        for (int i = 0; i < samples.Length; i++)
        {
            float t = i / (float)samples.Length, phase = i / (float)rate;
            float grit = Mathf.Sin(i * 17.13f) * Mathf.Sin(i * 7.31f);
            samples[i] = (Mathf.Sin(phase * frequency * (1f - .5f * t) * Mathf.PI * 2f) * .7f + grit * .3f) * (1f - t) * (1f - t);
        }
        var clip = AudioClip.Create(name, samples.Length, 1, rate, false);
        clip.hideFlags = HideFlags.HideAndDontSave; clip.SetData(samples, 0); return clip;
    }
    public static int Quarters(GameObject target)
    {
        if (target == null) return 0;
        if (target.TryGetComponent(out EnemyHearts enemy)) return enemy.QuartersLeft;
        if (target.TryGetComponent(out EliteShip elite)) return elite.QuartersLeft;
        if (target.TryGetComponent(out BossTarget _) && BossEncounter.Instance != null) return BossEncounter.Instance.QuartersLeft;
        return 4;
    }
    public static void Hit(bool fullHeart, bool killed)
    {
        if (!Application.isPlaying) return;
        float now = Time.unscaledTime;
        if (killed && now - lastKick >= .18f) { lastKick = now; CameraKick.Kick(.025f); }
        if (SoundSettings.SfxGain <= 0f || now - lastSound < (killed ? .08f : fullHeart ? .065f : .045f)) return;
        lastSound = now; Prewarm();
        var source = voices[next++ % voices.Length];
        source.volume = (killed ? .13f : fullHeart ? .11f : .065f);
        source.pitch = killed ? .9f : fullHeart ? 1f : 1.2f;
        source.PlayOneShot(killed ? kill : fullHeart ? heavy : light); Played++;
    }
}
