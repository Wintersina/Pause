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
public static class PrefabName
{
    public static bool Is(GameObject go, string key)
    {
        return go != null && Normalise(go.name) == Normalise(key);
    }

    public static bool Is(Component c, string key)
    {
        return c != null && Is(c.gameObject, key);
    }

    static string Normalise(string raw)
    {
        if (string.IsNullOrEmpty(raw)) return string.Empty;

        int clone = raw.IndexOf("(Clone)");
        if (clone >= 0) raw = raw.Substring(0, clone);

        var sb = new System.Text.StringBuilder(raw.Length);
        foreach (char ch in raw)
        {
            if (ch == ' ' || ch == '_' || ch == '-') continue;
            sb.Append(char.ToLowerInvariant(ch));
        }
        return sb.ToString();
    }
}
