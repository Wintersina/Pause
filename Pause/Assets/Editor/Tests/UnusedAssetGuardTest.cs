using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

// Guards the unused-asset cleanup. The old enemy art (the Kenney fighters
// kn_enemy*, the Kenney meteors kn_meteor*, the pixel aestroid_* rocks and
// their PNGs) was replaced by the per-world EnemyRoster. Anything left under
// a Resources folder ships in every build and can be pulled back in by a
// Resources.Load / LoadAll, so none of it may reappear there. Scenes and
// prefabs must not point at the deleted prefabs either, and the project's
// default cursor must not name a texture that no longer exists.
public static class UnusedAssetGuardTest
{
    static int fails;
    static void Check(string what, bool ok)
    {
        Debug.Log((ok ? "[UAG] PASS  " : "[UAG] FAIL  ") + what);
        if (!ok) fails++;
    }

    public static void Run()
    {
        TestHarness.Exit(Execute());
    }

    // File names (no extension) of the retired enemy art, prefab or texture.
    static readonly Regex Retired = new Regex(
        @"^(kn_enemy|kn_meteor|aestroid_|enemy(Black|Blue|Green|Red)\d|meteor(Brown|Grey)_)",
        RegexOptions.IgnoreCase);

    static readonly string[] GoneFolders =
    {
        "Assets/Resources/prefabs/Enemies",
        "Assets/Art/Resources/Prefabs/Enemies",
        "Assets/Art/Aestroids",
        "Assets/Legacy/Art/Aestroids",
    };

    // The deleted prefabs' guids (the scenes and the spawner referenced
    // these): the 8 aestroid_* rocks, 20 kn_enemy* fighters, 8 big meteors.
    static readonly string[] RetiredPrefabGuids =
    {
        "96acc15cd0038f64baddebb664a20f6f", "b77bb6c2182153b40b19a70a325df2ab", "4be5a907c1bc7124c8a51f23bc7f45de",
        "13042310b25bf2a408a3115367a2521b", "bf21d9f03fe1bcb4496e7f780aef475b", "b84c7f2b88434b445a825cd259a465b5",
        "329d7970fb77ecd46b740012209328db", "2289f349f78780a498eb0a1159382d4a",
        "569c276a798fd4b5781be92514ac2279", "e6fad44205cc44dcbb0484b679e4eb99", "373380daecc274bb4bce9af86ad27428",
        "e1498c1cb9acb4009af830678e27c64a", "08835dfaefba4479e91a566280fbcec6", "313095f23a242434cac5901fccc96b93",
        "0e26db09c8f5648c0b81bed51eb39ff2", "3f8318e1440d84af296f333449dceb56", "5eb2d9811efd94752a6f7ad94b4e3e61",
        "77ed24e74f9a2416da93907db3f8ef46", "b5540ba17e04444e69ccb0b8e01d93fb", "f60aa9332e0f640aab69c777c76274fe",
        "e792819ea75ad4c6abc22823a98d7d82", "70c6d206c1da44564831ab170e544ffe", "dee54e1f08ca64b429419f86bd612e22",
        "922af5d82e2a0430083647f21cdac407", "2ba7482a2f80f442c98a63389916d581", "f437c2605e1eb4c6594ccf8a368a3436",
        "97fa56abce9c14bda9ad910e544670cb", "82a070866aaae4dfd9fa84014e3c9593",
        "63f6ccf3b48bf443bba23d86018b5a2c", "d99c23d57351d4f039ca1d6d09fa84ca", "ffb68a3bce61046679321f09ba79d115",
        "cb98009a465c040b1b244b9593fca1fb", "5fc76b727bbac4a5a9c52eb610af7244", "b979b6497d40142e8946463bdb2dfdba",
        "ccf6cc8400f774adeabb4cf950f6d799", "b33914ee1684d409ea8bdc157cdf6957",
    };

    public static int Execute()
    {
        fails = 0;
        using var sandbox = new TestHarness.Sandbox();

        NoRetiredArtUnderResources();
        RetiredFoldersAreGone();
        NothingReferencesARetiredPrefab();
        DefaultCursorResolves();

        Debug.Log("[UAG] failures: " + fails);
        return fails;
    }

    static void NoRetiredArtUnderResources()
    {
        var found = new List<string>();
        foreach (string file in Directory.GetFiles("Assets", "*", SearchOption.AllDirectories))
        {
            string path = file.Replace('\\', '/');
            if (path.EndsWith(".meta") || !path.Contains("/Resources/")) continue;
            if (Retired.IsMatch(Path.GetFileNameWithoutExtension(path))) found.Add(path);
        }
        foreach (string path in found) Debug.Log("[UAG] retired enemy art under Resources: " + path);
        Check("no kn_enemy* / kn_meteor* / aestroid_* asset under any Resources folder (" + found.Count + ")",
              found.Count == 0);

        // The same through the loader a stray LoadAll would use.
        int loaded = 0;
        foreach (var go in Resources.LoadAll<GameObject>(""))
            if (go != null && Retired.IsMatch(go.name)) { loaded++; Debug.Log("[UAG] Resources loads " + go.name); }
        foreach (var tex in Resources.LoadAll<Texture2D>(""))
            if (tex != null && Retired.IsMatch(tex.name)) { loaded++; Debug.Log("[UAG] Resources loads " + tex.name); }
        Check("Resources.LoadAll finds no retired enemy prefab or texture (" + loaded + ")", loaded == 0);
    }

    static void RetiredFoldersAreGone()
    {
        foreach (string folder in GoneFolders)
            Check(folder + " is gone", !AssetDatabase.IsValidFolder(folder) && !Directory.Exists(folder));
    }

    static void NothingReferencesARetiredPrefab()
    {
        var guids = new HashSet<string>(RetiredPrefabGuids);
        int dangling = 0;
        foreach (string ext in new[] { "*.unity", "*.prefab", "*.asset", "*.controller", "*.anim" })
            foreach (string path in Directory.GetFiles("Assets", ext, SearchOption.AllDirectories))
            {
                string text = File.ReadAllText(path);
                foreach (Match m in Regex.Matches(text, "guid: ([0-9a-f]{32})"))
                    if (guids.Contains(m.Groups[1].Value))
                    {
                        dangling++;
                        Debug.Log("[UAG] " + path + " references retired prefab " + m.Groups[1].Value);
                        break;
                    }
            }
        Check("no scene, prefab or asset references a deleted enemy/asteroid prefab (" + dangling + " files)",
              dangling == 0);
        foreach (string guid in RetiredPrefabGuids)
            if (!string.IsNullOrEmpty(AssetDatabase.GUIDToAssetPath(guid)) &&
                AssetDatabase.LoadMainAssetAtPath(AssetDatabase.GUIDToAssetPath(guid)) != null)
                Check("retired prefab " + guid + " is deleted (" + AssetDatabase.GUIDToAssetPath(guid) + ")", false);
    }

    static void DefaultCursorResolves()
    {
        // Either no custom cursor (the OS one) or a texture that exists.
        var settings = File.ReadAllText("ProjectSettings/ProjectSettings.asset");
        var m = Regex.Match(settings, @"defaultCursor: \{fileID: (\d+)(?:, guid: ([0-9a-f]{32}))?");
        bool ok = m.Success && (m.Groups[1].Value == "0" ||
                  (m.Groups[2].Success && AssetDatabase.LoadMainAssetAtPath(AssetDatabase.GUIDToAssetPath(m.Groups[2].Value)) != null));
        Check("the default cursor is the system cursor or an existing texture", ok);
    }
}
