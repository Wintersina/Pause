using System.Collections.Generic;
using UnityEngine;

// Procedural, per-target destruction audio.  Each roster key deterministically
// produces its own clip, so new entries added to EnemyRoster get a distinct
// death cue without a second integration pass.  The palette is deliberately
// small and hard-edged: hostile craft whine/scream into a metal failure,
// rocks crack in their world's material, and mines alarm-pop before bursting.
public static class EnemyDeathAudio
{
    const int Rate = 22050;
    const int MaxCachedClips = 128;
    static readonly Dictionary<string, AudioClip> clips = new Dictionary<string, AudioClip>();
    static AudioSource source;

    public static void Play(GameObject target)
    {
        if (target == null || !Application.isPlaying) return;
        var def = EnemyIdentity.Of(target);
        string key = def != null ? def.key : target.name;
        if (string.IsNullOrEmpty(key)) key = "unknown_target";
        var clip = Clip(key, def != null ? def.role : GuessRole(target));
        var src = Source();
        if (src == null || clip == null) return;
        src.pitch = 1f;
        src.PlayOneShot(clip, Volume(def != null ? def.role : GuessRole(target)));
    }

    static EnemyRole GuessRole(GameObject target)
    {
        if (target.CompareTag("Astr")) return EnemyRole.Rock;
        string n = target.name.ToLowerInvariant();
        return n.Contains("mine") ? EnemyRole.Mine : EnemyRole.Fighter;
    }

    static float Volume(EnemyRole role)
    {
        return role == EnemyRole.Big ? .9f : role == EnemyRole.Mine ? .82f : .68f;
    }

    static AudioSource Source()
    {
        if (source != null) return source;
        var host = new GameObject("EnemyDeathAudio");
        Object.DontDestroyOnLoad(host);
        source = host.AddComponent<AudioSource>();
        source.playOnAwake = false;
        source.spatialBlend = 0f;
        source.volume = .8f;
        return source;
    }

    static AudioClip Clip(string key, EnemyRole role)
    {
        AudioClip found;
        if (clips.TryGetValue(key, out found)) return found;
        // There are presently far fewer than this; the guard keeps an
        // experimental content build from retaining unbounded generated clips.
        if (clips.Count >= MaxCachedClips) clips.Clear();
        uint seed = Hash(key);
        int count = Mathf.RoundToInt(Rate * Duration(role, seed));
        var data = new float[count];
        for (int i = 0; i < count; i++)
        {
            float t = (float)i / Rate;
            float x = role == EnemyRole.Rock ? Rock(t, seed) :
                      role == EnemyRole.Mine ? Mine(t, seed) : Ship(t, seed, role);
            // Short fade avoids a digital click without softening the attack.
            float fade = Mathf.Min(1f, i / 90f, (count - i) / 180f);
            data[i] = Mathf.Clamp(x * fade, -.92f, .92f);
        }
        found = AudioClip.Create("death_" + key, count, 1, Rate, false);
        found.SetData(data, 0);
        clips[key] = found;
        return found;
    }

    static float Duration(EnemyRole role, uint seed)
    {
        float variance = ((seed >> 8) & 31) / 200f;
        return role == EnemyRole.Big ? .52f + variance :
               role == EnemyRole.Rock ? .32f + variance :
               role == EnemyRole.Mine ? .38f + variance : .28f + variance;
    }

    // A compact dying servo/scream.  Each ship key changes its base note,
    // wobble and collapse rate; heavies retain a slower, lower pressure drop.
    static float Ship(float t, uint seed, EnemyRole role)
    {
        float length = Duration(role, seed);
        float p = Mathf.Clamp01(t / length);
        float baseHz = 135f + (seed & 255) * 2.1f;
        float fall = role == EnemyRole.Big ? 1f - p * .56f : 1.35f - p * .98f;
        float wobble = Mathf.Sin(t * (23f + ((seed >> 16) & 31)) + (seed & 7)) * (18f + ((seed >> 5) & 15));
        float tone = Mathf.Sin(6.2831853f * (baseHz * fall + wobble) * t);
        float grit = Noise(t, seed) * (.16f + .18f * p);
        float crack = p > .64f ? Noise(t * 4f, seed ^ 0x91u) * .34f : 0f;
        return (tone * .56f + grit + crack) * (1f - p * .74f);
    }

    // Mineral hulls: a dry, granular split. World-key bits naturally colour
    // every individual rock differently (ice brightens, magma darkens, etc.).
    static float Rock(float t, uint seed)
    {
        float length = Duration(EnemyRole.Rock, seed);
        float p = Mathf.Clamp01(t / length);
        float grit = Noise(t * (1.2f + (seed & 3)), seed);
        float split = Mathf.Sin(6.2831853f * (410f + (seed & 255) * 1.7f) * t) * Mathf.Exp(-p * 6f);
        float chunks = Noise(t * 7f, seed ^ 0x6d2bu) * Mathf.Max(0f, p - .12f) * .42f;
        return (grit * (.62f - p * .38f) + split * .36f + chunks) * (1f - p);
    }

    static float Mine(float t, uint seed)
    {
        float length = Duration(EnemyRole.Mine, seed);
        float p = Mathf.Clamp01(t / length);
        float chirp = Mathf.Sin(6.2831853f * (640f + p * 510f + (seed & 63)) * t) * Mathf.Exp(-p * 2.4f);
        float blast = Noise(t, seed ^ 0xbadu) * Mathf.Clamp01((p - .28f) * 5f) * (1f - p);
        return chirp * .4f + blast * .7f;
    }

    // Deterministic sample-and-hold noise: independent of Random state and
    // consequently identical for a target every time it is encountered.
    static float Noise(float t, uint seed)
    {
        uint n = (uint)(t * Rate / 13f) + seed;
        n ^= n << 13; n ^= n >> 17; n ^= n << 5;
        return ((n & 65535u) / 32767.5f) - 1f;
    }

    static uint Hash(string text)
    {
        uint h = 2166136261u;
        for (int i = 0; i < text.Length; i++) { h ^= text[i]; h *= 16777619u; }
        return h;
    }
}
