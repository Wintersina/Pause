using UnityEditor;
using UnityEngine;

// Dev audition: in Play mode, "Pause/Audition Space Death Sounds" plays every
// Space death cue in turn through the game's own EnemyDeathAudio path (pool,
// jitter, volume), each with its scream layer forced on where it has one, so
// they can be judged in context over the music. Run it again to stop.
public static class EnemyDeathAudition
{
    const float Gap = 1.3f;
    static int next = -1;
    static double due;

    [MenuItem("Pause/Audition Space Death Sounds")]
    static void Audition()
    {
        if (!Application.isPlaying) { Debug.LogWarning("[Audition] enter Play mode first"); return; }
        if (next >= 0) { Stop(); return; }
        next = 0;
        due = EditorApplication.timeSinceStartup;
        EditorApplication.update += Tick;
    }

    static void Stop()
    {
        next = -1;
        EditorApplication.update -= Tick;
    }

    static void Tick()
    {
        if (!Application.isPlaying) { Stop(); return; }
        if (EditorApplication.timeSinceStartup < due) return;
        var keys = EnemyDeathAudioTest.SpaceKeys;
        if (next >= keys.Length) { Debug.Log("[Audition] done"); Stop(); return; }
        string key = keys[next++];
        float vol = key == "space_big" ? EnemyDeathAudio.Volume(EnemyRole.Big)
                  : key == "space_mine" ? EnemyDeathAudio.Volume(EnemyRole.Mine)
                  : key.StartsWith("space_elite_") ? EnemyDeathAudio.EliteVolume
                  : EnemyDeathAudio.Volume(EnemyRole.Fighter);
        bool ok = EnemyDeathAudio.PlayAuthored(key, vol, forceScream: true);
        Debug.Log("[Audition] " + key + (ok ? " -> " + (EnemyDeathAudio.LastClip != null ? EnemyDeathAudio.LastClip.name : "?") +
                  (EnemyDeathAudio.ScreamVariants(key) > 0 && EnemyDeathAudio.LastScream != null ? " + " + EnemyDeathAudio.LastScream.name : "")
                  : " (no authored clip)"));
        due = EditorApplication.timeSinceStartup + Gap;
    }
}
