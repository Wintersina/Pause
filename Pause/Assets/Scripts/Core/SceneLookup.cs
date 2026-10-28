using System.Collections.Generic;
using UnityEngine;

// GameObject.Find, remembered. Find walks every object in every loaded scene by name; a per-spawn caller (a rail
// mine's lane, the power-up trail's target) paid that on every spawn. Active answers the same as Find does:
// the remembered object is returned only while it is still alive AND active in the hierarchy (Find never returns
// an inactive one), otherwise the name is looked up again and the new answer remembered. Nothing is remembered
// for a miss (an object created later is found at once) and nothing out of play (edit-mode suites build and
// destroy scene objects by hand: they always get a real Find).
//
// A destroyed object compares equal to null (Unity's overloaded ==), so a world change or scene load that replaced
// the object re-resolves by itself; ForgetAll() is for tests and costs nothing at runtime.
public static class SceneLookup
{
    static readonly Dictionary<string, GameObject> cache = new Dictionary<string, GameObject>();

    // Real GameObject.Find calls made on behalf of callers (tests count them).
    // Tests: remember lookups out of play too.
    public static bool CacheOutOfPlay;

    public static int Finds { get; private set; }
    public static int Hits { get; private set; }
    public static void ResetCounters() { Finds = Hits = 0; }
    public static void ForgetAll() { cache.Clear(); }

    public static GameObject Active(string name)
    {
        if (!Application.isPlaying && !CacheOutOfPlay) { Finds++; return GameObject.Find(name); }
        GameObject go;
        if (cache.TryGetValue(name, out go) && go != null && go.activeInHierarchy) { Hits++; return go; }
        Finds++;
        go = GameObject.Find(name);
        if (go != null) cache[name] = go; else cache.Remove(name);
        return go;
    }
}
