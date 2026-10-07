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
//
// The rail mine's art went the other way: its original neon atlas (the old
// Vfx/rail_bomb_themes_atlas.png) is back, on purpose, under
// Enemies/Mines/rail_mines_neon.png, and is the one rail-mine texture allowed
// under Resources. Its old copies (and the two Ember beat frames), the old
// RailBombSprites slicer and the flat-cartoon <world>_mine strips that
// replaced it stay deleted.
//
// The original invader alien went too: alien1.prefab (the title screen's
// drifting alien and the spawner's fallback), its invader32x32x4 sheet and
// its alieanShip clip / invader32x32x4_0 controller. The title screen flies
// the Space roster alien (TitleScreenAlien) and the spawner only ever spawns
// roster aliens, so none of those files, guids or names may come back.
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
        RailMineArtIsTheRestoredAtlasOnly();
        NothingReferencesARetiredPrefab();
        DefaultCursorResolves();
        OldInvaderAlienStaysDeleted();
        ReplacedAssetsStayDeleted();
        EveryAssetIsReachable();

        Debug.Log("[UAG] failures: " + fails);
        return fails;
    }

    // The 2026-10 cleanup: the Unity-5 explosion_0 prefab and its
    // controller / clip / sheet (TargetExplosion draws every explosion now),
    // the old xenon2 default app icon (the icon is Art/AppIcon/GoldWarden),
    // and the scrollingText behaviour nothing attached.
    public static readonly string[] ReplacedAssets =
    {
        "Assets/Resources/prefabs/explosion_0.prefab",
        "Assets/Art/Animation/explosion_0.controller",
        "Assets/Art/Animation/explotion2.anim",
        "Assets/Art/Animation/explosion.png",
        "Assets/Art/Retro80s/Ships/SourceStrips/xenon2_ship.png",
        "Assets/Scripts/UI/scrollingText.cs",
        // The 2026-10 Art reorganisation: the legacy round shield bubble (the
        // inactive "Shield" child's sprite; ShipShield draws the contour
        // shield) and the flat planet walls the reinforced rails replaced.
        "Assets/Art/transparent-bubble.png",
        "Assets/Art/Resources/Worlds/Frost/wallLeft.png",
        "Assets/Art/Resources/Worlds/Frost/wallRight.png",
        "Assets/Art/Resources/Worlds/Verdant/wallLeft.png",
        "Assets/Art/Resources/Worlds/Verdant/wallRight.png",
        "Assets/Art/Resources/Worlds/Ember/wallLeft.png",
        "Assets/Art/Resources/Worlds/Ember/wallRight.png",
        // The 2026-10 Assets vet: the 2016 ship prefabs nothing loads (only
        // inGameShips/shipN are Resources-loaded, by spawnShips.PrefabPathFor),
        // their parked sprites (ship1-7 no longer carry a 2016 rest sprite:
        // spawnShips.ApplyHull dresses every hull from the roster), the dead
        // OriginalShipArt / roate scripts, and Verdant's old mid tile (its mid
        // layer draws forest_industrial_center_v1).
        "Assets/Resources/prefabs/Ships/Ninja_0.prefab",
        "Assets/Resources/prefabs/Ships/Saboteur_0.prefab",
        "Assets/Resources/prefabs/Ships/UFO_0.prefab",
        "Assets/Resources/prefabs/Ships/Turtle.prefab",
        "Assets/Resources/prefabs/Ships/player.prefab",
        "Assets/Resources/prefabs/Ships/Ligher_0_2_1.prefab",
        "Assets/Resources/prefabs/Ships/Lightning_0.prefab",
        "Assets/Resources/prefabs/Ships/Paranoid_0.prefab",
        "Assets/Art/Ships/Legacy/Ninja.png",
        "Assets/Art/Ships/Legacy/Saboteur.png",
        "Assets/Art/Ships/Legacy/UFO.png",
        "Assets/Art/Ships/Legacy/Ligher.png",
        "Assets/Art/Ships/Legacy/Lightning.png",
        "Assets/Art/Ships/Legacy/Paranoid.png",
        "Assets/Art/Ships/Legacy/player.png",
        "Assets/Scripts/Ship/OriginalShipArt.cs",
        "Assets/Scripts/Gameplay/roate.cs",
        "Assets/Art/Backgrounds/Resources/Worlds/Verdant/Backdrop/mid.png",
    };

    static void ReplacedAssetsStayDeleted()
    {
        foreach (string path in ReplacedAssets)
            Check(path + " stays deleted", !File.Exists(path) && !File.Exists(path + ".meta"));
    }

    // Every imported asset must be pulled in by something that ships: a
    // build scene, a Resources folder (anything there can be Resources.Load-ed,
    // so it counts as a root; keeping those folders lean is up to the loaders
    // and the per-system tests), or the project settings (icons, cursor,
    // splash). An asset nothing reaches is dead weight in the repo; delete it
    // or wire it up. Exempt: code, editor-only folders, third-party SDK
    // folders, docs/licences, the app-icon candidates and the art staging
    // areas (Codex's working files, promoted into Resources by hand or by
    // EliteArtSync).
    static readonly string[] ExemptPrefixes =
    {
        "Assets/Plugins/", "Assets/GooglePlayGames/", "Assets/ExternalDependencyManager/",
        "Assets/Art/AppIcon/", "Assets/Art/Enemies/Elite/",
    };

    static bool Exempt(string path)
    {
        if (path.EndsWith(".cs") || path.EndsWith(".md") || path.EndsWith(".txt") ||
            path.EndsWith(".asmdef") || path.Contains("/Editor/"))
            return true;
        if (path.Contains("/Staging/") || path.Contains("/staging/")) return true;
        foreach (string prefix in ExemptPrefixes)
            if (path.StartsWith(prefix)) return true;
        return false;
    }

    static void EveryAssetIsReachable()
    {
        var roots = new List<string>();
        foreach (var scene in EditorBuildSettings.scenes)
            if (scene.enabled) roots.Add(scene.path);
        var all = new List<string>();
        foreach (string path in AssetDatabase.GetAllAssetPaths())
        {
            if (!path.StartsWith("Assets/") || AssetDatabase.IsValidFolder(path)) continue;
            all.Add(path);
            if (path.Contains("/Resources/")) roots.Add(path);
        }
        foreach (string file in Directory.GetFiles("ProjectSettings", "*.asset"))
            foreach (Match m in Regex.Matches(File.ReadAllText(file), "guid: ([0-9a-f]{32})"))
            {
                string path = AssetDatabase.GUIDToAssetPath(m.Groups[1].Value);
                if (!string.IsNullOrEmpty(path) && path.StartsWith("Assets/")) roots.Add(path);
            }
        var reached = new HashSet<string>(AssetDatabase.GetDependencies(roots.ToArray(), true));
        var orphans = new List<string>();
        foreach (string path in all)
            if (!reached.Contains(path) && !Exempt(path)) orphans.Add(path);
        foreach (string path in orphans) Debug.Log("[UAG] unreferenced asset: " + path);
        Check("every asset is reached by a build scene, a Resources folder or the project settings (" +
              orphans.Count + " orphans)", orphans.Count == 0);
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

    // Allowed: the restored atlas at its new home. Retired: everything else
    // that ever drew the rail mine.
    public const string RestoredMineAtlas = "Assets/Art/Resources/Enemies/Mines/rail_mines_neon.png";

    static readonly string[] RetiredMineArt =
    {
        "Assets/Art/Resources/Vfx/rail_bomb_themes_atlas.png",
        "Assets/Art/Resources/Vfx/rail_mine_ember_1.png",
        "Assets/Art/Resources/Vfx/rail_mine_ember_2.png",
        "Assets/Scripts/Worlds/RailBombSprites.cs",
        "Assets/Art/Resources/Enemies/space_mine.png",
        "Assets/Art/Resources/Enemies/frost_mine.png",
        "Assets/Art/Resources/Enemies/verdant_mine.png",
        "Assets/Art/Resources/Enemies/ember_mine.png",
    };

    static readonly Regex MineArtName = new Regex(@"(rail_?bomb|rail_?mine|^(space|frost|verdant|ember)_mine$)", RegexOptions.IgnoreCase);

    static void RailMineArtIsTheRestoredAtlasOnly()
    {
        Check("the restored neon rail-mine atlas is allowed and present (" + RestoredMineAtlas + ")",
              File.Exists(RestoredMineAtlas) && AssetDatabase.LoadAssetAtPath<Texture2D>(RestoredMineAtlas) != null);
        Check("RailMineArt loads it from Resources", RailMineArt.Atlas != null &&
              AssetDatabase.GetAssetPath(RailMineArt.Atlas) == RestoredMineAtlas);
        foreach (string path in RetiredMineArt)
            Check(path + " stays deleted", !File.Exists(path) && !File.Exists(path + ".meta"));

        var found = new List<string>();
        foreach (string file in Directory.GetFiles("Assets", "*.png", SearchOption.AllDirectories))
        {
            string path = file.Replace('\\', '/');
            if (!path.Contains("/Resources/") || path == RestoredMineAtlas) continue;
            if (MineArtName.IsMatch(Path.GetFileNameWithoutExtension(path))) found.Add(path);
        }
        foreach (string path in found) Debug.Log("[UAG] stray rail-mine art under Resources: " + path);
        Check("the neon atlas is the only rail-mine texture under Resources (" + found.Count + " others)", found.Count == 0);
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

    public static readonly string[] RetiredAlienAssets =
    {
        "Assets/Resources/prefabs/alien1.prefab",
        "Assets/Art/invader32x32x4.png",
        "Assets/Art/Animation/invader32x32x4_0.controller",
        "Assets/Art/Animation/alieanShip.anim",
    };

    // alien1.prefab, invader32x32x4.png, invader32x32x4_0.controller, alieanShip.anim
    public static readonly string[] RetiredAlienGuids =
    {
        "1716f248879a3d9409936e4fc65c75a4", "fb448a50c99cf7645932abe0605d52ec",
        "7acff94875db60441ad09b1a59f3e2e1", "4bcc69e05df0c7a4fa6e539c4550f904",
    };

    static readonly Regex RetiredAlienName = new Regex(@"^(alien1|invader32x32x4(_\d+)?|alieanShip)$", RegexOptions.IgnoreCase);

    static void OldInvaderAlienStaysDeleted()
    {
        foreach (string path in RetiredAlienAssets)
            Check(path + " stays deleted", !File.Exists(path) && !File.Exists(path + ".meta"));
        foreach (string guid in RetiredAlienGuids)
            Check("retired alien guid " + guid + " resolves to nothing",
                  string.IsNullOrEmpty(AssetDatabase.GUIDToAssetPath(guid)) ||
                  AssetDatabase.LoadMainAssetAtPath(AssetDatabase.GUIDToAssetPath(guid)) == null);

        // No file of that name anywhere in the project (a re-import under a
        // new guid would dodge the guid checks).
        var named = new List<string>();
        foreach (string file in Directory.GetFiles("Assets", "*", SearchOption.AllDirectories))
        {
            string path = file.Replace('\\', '/');
            if (path.EndsWith(".meta") || path.EndsWith(".cs")) continue;
            if (RetiredAlienName.IsMatch(Path.GetFileNameWithoutExtension(path))) named.Add(path);
        }
        foreach (string path in named) Debug.Log("[UAG] old alien asset is back: " + path);
        Check("no alien1 / invader32x32x4 / alieanShip asset anywhere under Assets (" + named.Count + ")", named.Count == 0);
        Check("Resources can't load the old alien1 prefab", Resources.Load<GameObject>("prefabs/alien1") == null);

        // Nothing serialized points at them: scenes, prefabs, assets,
        // animator controllers, clips, materials and the project settings.
        var guids = new HashSet<string>(RetiredAlienGuids);
        int dangling = 0;
        var files = new List<string>();
        foreach (string ext in new[] { "*.unity", "*.prefab", "*.asset", "*.controller", "*.anim", "*.mat", "*.overrideController" })
            files.AddRange(Directory.GetFiles("Assets", ext, SearchOption.AllDirectories));
        files.AddRange(Directory.GetFiles("ProjectSettings", "*.asset", SearchOption.TopDirectoryOnly));
        foreach (string path in files)
        {
            string text = File.ReadAllText(path);
            foreach (Match m in Regex.Matches(text, "guid: ([0-9a-f]{32})"))
                if (guids.Contains(m.Groups[1].Value))
                {
                    dangling++;
                    Debug.Log("[UAG] " + path + " references retired alien asset " + m.Groups[1].Value);
                    break;
                }
        }
        Check("no scene, prefab, asset or setting references the old alien (" + dangling + " files)", dangling == 0);

        // ...and no code path brings back a prefab fallback for it.
        Check("enmiesOnBoard has no alien1 prefab slot", typeof(enmiesOnBoard).GetField("alien1") == null);
        var title = File.ReadAllText("Assets/Scenes/startS4.unity");
        Check("startS4's drifting alien is the roster alien (TitleScreenAlien), not a prefab",
              title.Contains("m_Name: TitleAlien") && !Regex.IsMatch(title, @"m_Name: alien1\b"));
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
