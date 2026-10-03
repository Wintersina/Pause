using UnityEngine;

// Procedural arcade stings keep every roster weapon distinct without fifteen
// scene-wired AudioSources. Pitch, wave shape and sweep derive from ship slot.
//
// Clips are built once per ship and cached (they used to be rebuilt on every
// shot), and the AudioSource is cached too.
public static class UltimateShotSound
{
    static readonly AudioClip[] shots = new AudioClip[32];
    static AudioClip tick;
    static AudioSource shotSource, tickSource;

    static AudioSource Source(ref AudioSource cached, string hostName)
    {
        if (cached != null) return cached;
        var host = GameObject.Find(hostName);
        if (host == null) host = new GameObject(hostName);
        var source = host.GetComponent<AudioSource>();
        if (source == null) source = host.AddComponent<AudioSource>();
        source.spatialBlend = 0f;
        cached = source;
        return source;
    }

    public static void Play(int shipIndex)
    {
        int slot = Mathf.Clamp(shipIndex, 0, shots.Length - 1);
        if (shots[slot] == null) shots[slot] = Build(slot);
        Source(ref shotSource, "~PowerFx").PlayOneShot(shots[slot], .52f);
    }

    // The ready tell: three short rising ticks over the last second, pitched
    // per weapon (WeaponStyle.tickPitch). step is 0, 1, 2.
    public static void Tick(int shipIndex, int step)
    {
        if (!Application.isPlaying) return;
        if (tick == null) tick = BuildTick();
        var source = Source(ref tickSource, "~ChargeTick");
        source.pitch = WeaponStyleTable.For(shipIndex).tickPitch * (1f + step * .19f);
        source.PlayOneShot(tick, .32f + step * .06f);
    }

    static AudioClip BuildTick()
    {
        const int rate = 22050, count = 1323; // 60ms
        var clip = AudioClip.Create("charge_tick", count, 1, rate, false);
        var data = new float[count];
        for (int i = 0; i < count; i++)
        {
            float t = i / (float)rate;
            float square = Mathf.Sin(t * 1320f * Mathf.PI * 2f) > 0f ? 1f : -1f;
            data[i] = square * Mathf.Exp(-t * 70f) * .3f;
        }
        clip.SetData(data, 0);
        return clip;
    }

    static AudioClip Build(int index)
    {
        const int rate = 22050, count = 6615;
        var clip = AudioClip.Create("ultimate_" + index, count, 1, rate, false);
        var data = new float[count];
        float baseHz = 260f + (index % 8) * 54f;
        for (int i = 0; i < count; i++)
        {
            float t = i / (float)rate, k = t / .3f;
            float hz = baseHz * (1f + k * (1.3f + (index % 3) * .25f));
            float phase = t * hz;
            float wave = index % 3 == 0 ? Mathf.Sin(phase * Mathf.PI * 2f)
                : index % 3 == 1 ? (phase % 1f < .5f ? 1f : -1f)
                : 2f * (phase % 1f) - 1f;
            data[i] = wave * Mathf.Exp(-t * (8f + index % 4 * 2f)) * .34f;
        }
        clip.SetData(data, 0);
        return clip;
    }
}
