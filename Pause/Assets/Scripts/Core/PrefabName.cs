using System;
using UnityEngine;

// Name-based identification of spawned objects, tolerant of cosmetic renames.
//
// A lot of gameplay logic identifies what it just hit by comparing against
// literal strings like "smStar 1(Clone)". That silently breaks the moment an
// asset is renamed -- which is exactly what happened when "smStar 1.prefab"
// became "smStar_1.prefab" and stars stopped being collectable.
//
// Matching now ignores the "(Clone)" suffix, case, spaces and underscores, so
// "smStar 1", "smStar_1" and "smstar1" are all the same object.
//
// Collision handlers ask this several times per contact, so the comparison is
// done in place (no normalised copies), and in play mode the object's name is
// fetched from the engine once per object per frame (GameObject.name
// allocates a fresh string on every read).
public static class PrefabName
{
    public static bool Is(GameObject go, string key)
    {
        return go != null && Same(NameOf(go), key);
    }

    public static bool Is(Component c, string key)
    {
        return c != null && Is(c.gameObject, key);
    }

    static GameObject lastObject;
    static string lastName;
    static int lastFrame = -1;

    // Edit-mode tests that simulate play frames can opt in to the cache (in
    // edit mode the frame count never advances, so it is off by default).
    public static bool CacheInEditMode;

    // go.name, read at most once per object per frame while playing.
    public static string NameOf(GameObject go)
    {
        if (go == null) return null;
        if (!Application.isPlaying && !CacheInEditMode) return go.name;
        int frame = Time.frameCount;
        if (frame != lastFrame || !ReferenceEquals(go, lastObject))
        {
            lastObject = go;
            lastName = go.name;
            lastFrame = frame;
        }
        return lastName;
    }

    // Normalised equality without building the normalised strings.
    public static bool Same(string a, string b)
    {
        int ea = End(a), eb = End(b), i = 0, j = 0;
        while (true)
        {
            while (i < ea && Skip(a[i])) i++;
            while (j < eb && Skip(b[j])) j++;
            if (i >= ea || j >= eb) return i >= ea && j >= eb;
            if (char.ToLowerInvariant(a[i]) != char.ToLowerInvariant(b[j])) return false;
            i++;
            j++;
        }
    }

    static bool Skip(char ch) { return ch == ' ' || ch == '_' || ch == '-'; }

    static int End(string s)
    {
        if (string.IsNullOrEmpty(s)) return 0;
        int clone = s.IndexOf("(Clone)", StringComparison.Ordinal);
        return clone >= 0 ? clone : s.Length;
    }
}
