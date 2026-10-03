using UnityEngine;

// Procedural arcade stings keep every roster weapon distinct without fifteen
// scene-wired AudioSources. Pitch, wave shape and sweep derive from ship slot.
public static class UltimateShotSound
{
    public static void Play(int shipIndex)
    {
        var host = GameObject.Find("~PowerFx");
        if (host == null) host = new GameObject("~PowerFx");
        var source = host.GetComponent<AudioSource>();
        if (source == null) source = host.AddComponent<AudioSource>();
        source.spatialBlend = 0f;
        source.PlayOneShot(Build(shipIndex), .52f);
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
