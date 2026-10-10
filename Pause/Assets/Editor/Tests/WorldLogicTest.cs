using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Headless check of world progression invariants.
public static class WorldLogicTest
{
    static int failures;

    static void Check(string what, bool ok)
    {
        Debug.Log((ok ? "[WT] PASS  " : "[WT] FAIL  ") + what);
        if (!ok) failures++;
    }

    // Worlds that play the scene's own default track: the Space/Verdant stage
    // arrangements and Verdant.wav were deleted on purpose, so these worlds
    // request no clips and never escalate.
    static readonly string[] SceneDefaultWorlds = { "Space", "Verdant" };

    // Worlds that play one of the user's songs for the whole level.
    static readonly (string world, string resource, string asset)[] FullSongs =
    {
        ("Frost", "WorldMusic/Frost_Main", "Assets/Audio/Resources/WorldMusic/Frost_Main.mp3"),
        ("Ember", "WorldMusic/Ember_Main", "Assets/Audio/Resources/WorldMusic/Ember_Main.mp3"),
    };

    // The audio these songs replaced: the Frost/Ember stage arrangements and
    // their old whole-level tracks. Paths and GUIDs, so a scene or prefab
    // still pointing at one would be caught.
    static readonly (string path, string guid)[] RemovedMusic =
    {
        ("Assets/Audio/Resources/WorldMusic/Ember.wav", "d32cff474930e41449a407d026dfbdee"),
        ("Assets/Audio/Resources/WorldMusic/EmberStage01.wav", "9617b330c3e748b0b3bd7f7a95c8eef6"),
        ("Assets/Audio/Resources/WorldMusic/EmberStage02.wav", "ef05261e973f4adab1214daee1a65331"),
        ("Assets/Audio/Resources/WorldMusic/EmberStage03.wav", "7560ab8411284bb0932b65dfdd92c896"),
        ("Assets/Audio/Resources/WorldMusic/EmberStage04.wav", "486883434ae748979bf22a72d3c10b59"),
        ("Assets/Audio/Resources/WorldMusic/EmberStage05.wav", "9cf49c10beac40218984b1afc2bd4ef3"),
        ("Assets/Audio/Resources/WorldMusic/EmberStage06.wav", "5ff03c82ca584be587ed360be168d57e"),
        ("Assets/Audio/Resources/WorldMusic/Frost.wav", "640e41f45cb6a43a2917707f3a7a976d"),
        ("Assets/Audio/Resources/WorldMusic/FrostStage01.wav", "f7f360ef196d4e8c8e5fedfd9e79977e"),
        ("Assets/Audio/Resources/WorldMusic/FrostStage02.wav", "3ecf7869e1404f15befcd10e0dea91e3"),
        ("Assets/Audio/Resources/WorldMusic/FrostStage03.wav", "66f6b09b63374958a894426ecb612bd9"),
        ("Assets/Audio/Resources/WorldMusic/FrostStage04.wav", "bfede4a1fe974562af30f52f3caac3a6"),
        ("Assets/Audio/Resources/WorldMusic/FrostStage05.wav", "5b466f4ade134d9d9e23233a6c8311f3"),
        ("Assets/Audio/Resources/WorldMusic/FrostStage06.wav", "18e93dfb06e744b68bf34d43bfb038a6"),
        // Deleted on purpose: Space/Verdant stage arrangements and Verdant.wav.
        ("Assets/Audio/Resources/WorldMusic/SpaceStage01.wav", "340bb36436b143158774bcbc2a8afa9b"),
        ("Assets/Audio/Resources/WorldMusic/SpaceStage02.wav", "bd5335518e7b427cb2d09f3a9a3017f1"),
        ("Assets/Audio/Resources/WorldMusic/SpaceStage03.wav", "cbe86c9346104ed999cd75c0c1c05426"),
        ("Assets/Audio/Resources/WorldMusic/SpaceStage04.wav", "f25fc21f69714529b9219a922ff04233"),
        ("Assets/Audio/Resources/WorldMusic/SpaceStage05.wav", "9df7d72f2e6045b2ae57a902d9dc9f3d"),
        ("Assets/Audio/Resources/WorldMusic/SpaceStage06.wav", "27c26cda79974728b8d29198f36a7719"),
        ("Assets/Audio/Resources/WorldMusic/Verdant.wav", "3655f9aef49504a9691c05f45c58a811"),
        ("Assets/Audio/Resources/WorldMusic/VerdantStage01.wav", "b37a2697a4524d458127e1382dc3420a"),
        ("Assets/Audio/Resources/WorldMusic/VerdantStage02.wav", "12c31113a19e48a280d86507721bd3e9"),
        ("Assets/Audio/Resources/WorldMusic/VerdantStage03.wav", "d2b346d0bafa4f18a475f9b9a7995155"),
        ("Assets/Audio/Resources/WorldMusic/VerdantStage04.wav", "e76ebd81200942cf8722a836d347ed70"),
        ("Assets/Audio/Resources/WorldMusic/VerdantStage05.wav", "8325f40ebdfa4839ab7eb7a3bf796ba7"),
        ("Assets/Audio/Resources/WorldMusic/VerdantStage06.wav", "7748f7f11f0b41c8b121de9952e6279b"),
    };

    public static void Run()
    {
        TestHarness.Exit(Execute());
    }

    public static int Execute()
    {
        failures = 0;
        using var sandbox = new TestHarness.Sandbox();

        // start clean
        PlayerPrefs.DeleteKey(WorldManager.PrefsCurrentWorld);
        PlayerPrefs.DeleteKey(WorldManager.PrefsHighestWorld);

        Check("five worlds defined (Space, Frost, Verdant, Ember, Tide)", WorldManager.Worlds.Length == 5);
        Check("the release switch is off in the shipped code: Ember is the last live world, Tide is gated (WorldManager.TideEnabled)",
              !WorldManager.TideEnabled && WorldManager.LastLiveWorld == 3 && WorldManager.LiveWorldCount == 4);
        Check("world 0 is Space", WorldManager.Worlds[0].displayName == "Space");
        Check("space keeps authored art", WorldManager.Worlds[0].resourceFolder == "");
        Check("space keeps authored music", WorldManager.Worlds[0].musicResource == "");
        Check("teleport portal atlas is present",
              AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Art/Resources/Vfx/teleport_portal_atlas.png") != null);
        Check("teleport portal has sixteen animation frames", TeleportPortalSprites.FrameCount == 16);
        Check("teleport warp sound is present",
              AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Audio/Resources/Audio/teleport_warp.wav") != null);
        Check("world music exposes level-clock escalation", typeof(WorldMusic).GetMethod("TryEscalate") != null);
        PlayerPrefs.SetInt(WorldManager.PrefsHighestWorld, 1);
        PlayerPrefs.DeleteKey("boughtship15");
        // The editor shares PlayerPrefs with the Mac build; developer mode may
        // already be on there. Start from off so turning it on snapshots.
        PlayerPrefs.DeleteKey(DeveloperUnlocks.EnabledKey);
        DeveloperUnlocks.SetEnabled(true);
        Check("developer flag unlocks every ship", PlayerPrefs.GetString("boughtship15") == "True");
        Check("developer flag unlocks every world (Tide included, for the picker)", PlayerPrefs.GetInt(WorldManager.PrefsHighestWorld) == WorldManager.Worlds.Length - 1);
        DeveloperUnlocks.SetEnabled(false);
        Check("turning developer flag off restores locked ship state", !PlayerPrefs.HasKey("boughtship15"));
        Check("turning developer flag off restores saved world progress",
              PlayerPrefs.GetInt(WorldManager.PrefsHighestWorld) == 1);
        for (int w = 0; w < EnemyRoster.WorldKeys.Length; w++)
            Check("the " + EnemyRoster.WorldKeys[w] + " rail mine flipbook is present (neon atlas row)",
                  AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Art/Resources/" + RailMineArt.AtlasPathFor(w) + ".png") != null &&
                  RailMineArt.Frame(w, RailMineArt.Dormant) != null);

        Check("starts at index 0", WorldManager.CurrentIndex == 0);
        Check("has a next world", WorldManager.HasNext);

        // walk the whole progression (the live worlds: Tide joins when its switch flips)
        for (int i = 1; i < WorldManager.LiveWorldCount; i++)
        {
            WorldManager.CurrentIndex = i;
            Check("index " + i + " -> " + WorldManager.Worlds[i].displayName,
                  WorldManager.CurrentIndex == i);
            Check("world " + i + " has art folder",
                  !string.IsNullOrEmpty(WorldManager.Current.resourceFolder));
            Check("world " + i + " has music (own song, or the scene track by design)",
                  !string.IsNullOrEmpty(WorldManager.Current.musicResource) ||
                  System.Array.IndexOf(SceneDefaultWorlds, WorldManager.Current.displayName) >= 0);
            Check("world " + i + " ramps harder than previous",
                  WorldManager.Worlds[i].speedRampPerSecond >
                  WorldManager.Worlds[i - 1].speedRampPerSecond);
        }

        Check("last live world has no next", !WorldManager.HasNext);
        Check("highest recorded", PlayerPrefs.GetInt(WorldManager.PrefsHighestWorld, 0) == WorldManager.LastLiveWorld);
        TideWorld();

        // clamping
        WorldManager.CurrentIndex = 99;
        Check("clamps above range", WorldManager.CurrentIndex == WorldManager.Worlds.Length - 1);
        WorldManager.CurrentIndex = -5;
        Check("clamps below range", WorldManager.CurrentIndex == 0);
        Check("highest is not lowered by going back",
              PlayerPrefs.GetInt(WorldManager.PrefsHighestWorld, 0) == WorldManager.Worlds.Length - 1);

        // the assets the themes point at must actually resolve (live worlds)
        for (int i = 1; i < WorldManager.LiveWorldCount; i++)
        {
            var t = WorldManager.Worlds[i];
            string skyName = t.displayName == "Space" ? SpaceSkySelection.Texture : "sky";
            // Worlds with variant ground sets (Frost, Verdant, Ember) keep their sky in <folder>/v1/; others in <folder>/ (BackdropCatalog.TileFolder).
            string p = "Assets/Art/Backgrounds/Resources/" + BackdropCatalog.TileFolder(t.displayName, 1) + skyName + ".png";
            Check("art present for " + t.displayName + " (" + p + ")",
                  AssetDatabase.LoadAssetAtPath<Texture2D>(p) != null);
            if (string.IsNullOrEmpty(t.musicResource)) continue; // scene-default world
            Check("music present for " + t.displayName + " (" + t.musicResource + ")",
                  Resources.Load<AudioClip>(t.musicResource) != null);
        }

        FullSongWorlds();

        PlayerPrefs.DeleteKey(WorldManager.PrefsCurrentWorld);
        PlayerPrefs.DeleteKey(WorldManager.PrefsHighestWorld);

        Debug.Log("[WT] failures: " + failures);
        return failures;
    }

    // World 5, Tide: in the list, behind the release switch, with its planet art,
    // its own backdrop, rails, roster and boss (elites, sounds, themed attacks: still stand-ins).
    static void TideWorld()
    {
        const int Ember = 3, Tide = 4;
        var t = WorldManager.Worlds[Tide];
        Check("Tide is world 4 (the fifth), named Tide, ramping harder than Ember",
              t.displayName == "Tide" && t.speedRampPerSecond > WorldManager.Worlds[Ember].speedRampPerSecond &&
              t.enemyRampScale > WorldManager.Worlds[Ember].enemyRampScale);
        Check("... its portal wears the bioluminescent mint, not the player's red nor a pickup's cyan",
              t.portalColor.g > .85f && t.portalColor.r < .6f && t.portalColor.b > .6f && t.portalColor.b < .85f);
        Check("... its own rails folder, texture and backdrop (no Ember stand-ins left)",
              t.resourceFolder == "Tide" && WorldPainter.RailTextureName("Tide") == "rail_tide_wide_v1" &&
              AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Art/Resources/Worlds/Tide/rail_tide_wide_v1.png") != null &&
              BackdropCatalog.For("Tide").world == "Tide" && BackdropCatalog.For("Tide") != BackdropCatalog.For("Ember"));
        // its own backdrop art, whatever the release switch says (the live-world loop above skips it until the switch flips)
        foreach (string layer in new[] { "sky", "far", "mid", "flow" })
        {
            string tile = "Assets/Art/Backgrounds/Resources/" + BackdropCatalog.TileFolder("Tide", 1) + layer + ".png";
            Check("art present for Tide (" + tile + ")", AssetDatabase.LoadAssetAtPath<Texture2D>(tile) != null || AssetDatabase.LoadAssetAtPath<Sprite>(tile) != null);
        }
        Check("... its codex backdrop tile resolves through BackdropCatalog.TileFolder (" + BackdropCatalog.TileFolder("Tide", 1) + ")",
              BackdropCatalog.TileFolder("Tide", 1) == "Worlds/Tide/Backdrop3/v1/");
        DeveloperUnlocks.SelectWorld(99);
        Check("... a developer run may start on it: the picker clamps to the full list", DeveloperUnlocks.SelectedWorld == Tide);
        PlayerPrefs.DeleteKey(DeveloperUnlocks.SelectedWorldKey);
        bool was = WorldManager.TideEnabled;
        try
        {
            WorldManager.TideEnabled = true;
            Check("switch ON: Tide is the last live world (5 live)", WorldManager.LastLiveWorld == Tide && WorldManager.LiveWorldCount == 5);
            WorldManager.CurrentIndex = Ember;
            Check("switch ON: Ember has a next, and it is Tide", WorldManager.HasNext && WorldManager.PortalDestination == Tide);
            WorldManager.CurrentIndex = Tide;
            Check("switch ON: Tide has no next", !WorldManager.HasNext);
            WorldManager.TideEnabled = false;
            Check("switch OFF: Tide (a developer run) has no next either, and Ember is last",
                  !WorldManager.HasNext && WorldManager.LastLiveWorld == Ember);
            WorldManager.CurrentIndex = Ember;
            Check("switch OFF: Ember has no next (the loop)", !WorldManager.HasNext);
        }
        finally { WorldManager.TideEnabled = was; }
    }

    static WorldTheme Theme(string name)
    {
        foreach (var t in WorldManager.Worlds)
            if (t.displayName == name) return t;
        return null;
    }

    static void FullSongWorlds()
    {
        foreach (var w in SceneDefaultWorlds)
        {
            Check(w + " requests no stage clips", !WorldMusic.UsesStages(Theme(w)));
            Check(w + " has no music resource", Theme(w).musicResource == "");
        }

        foreach (var song in FullSongs)
        {
            var theme = Theme(song.world);
            Check(song.world + " plays " + song.resource, theme != null && theme.musicResource == song.resource);
            Check(song.world + " has no stage escalation", theme != null && !WorldMusic.UsesStages(theme));

            var clip = Resources.Load<AudioClip>(song.resource);
            Check(song.resource + " loads", clip != null);
            Check(song.resource + " is the full song (over three minutes)", clip != null && clip.length > 180f);
            Check(song.resource + " is imported from " + song.asset,
                  clip != null && AssetDatabase.LoadAssetAtPath<AudioClip>(song.asset) == clip);

            var importer = AssetImporter.GetAtPath(song.asset) as AudioImporter;
            Check(song.asset + " has an audio importer", importer != null);
            if (importer != null)
            {
                foreach (var platform in new[] { "Android", "iOS" })
                {
                    var s = importer.GetOverrideSampleSettings(platform);
                    Check(song.world + " overrides " + platform, importer.ContainsSampleSettingsOverride(platform));
                    Check(song.world + " streams on " + platform, s.loadType == AudioClipLoadType.Streaming);
                    Check(song.world + " is Vorbis on " + platform, s.compressionFormat == AudioCompressionFormat.Vorbis);
                    Check(song.world + " is ~70% quality on " + platform, Mathf.Abs(s.quality - 0.7f) < 0.011f);
                    Check(song.world + " does not preload on " + platform, !s.preloadAudioData);
                }
            }

            // Loops: a measured loop-out point just before the song's closing
            // fade, and the music source loops once the world applies it.
            float length = clip != null ? clip.length : 0f;
            float loopOut = WorldMusic.LoopOutSeconds(song.resource);
            Check(song.world + " has a loop point before its fade", loopOut > length - 6f && loopOut < length - 1f);
            Check(song.world + " plays on before the loop point", !WorldMusic.PastLoopPoint(loopOut - 0.02f, loopOut, length));
            Check(song.world + " loops back at the loop point", WorldMusic.PastLoopPoint(loopOut, loopOut, length));

            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var src = new GameObject("MovingMusic").AddComponent<AudioSource>();
            WorldMusic.Apply(theme);
            Check(song.world + " starts its song on the music source", clip != null && src.clip == clip);
            Check(song.world + " music source loops", src.loop);
            Check(song.world + " loop point is armed", clip != null && WorldMusic.LoopClip == clip);

            // Boss fallback (no Boss_<World> clip yet): the song keeps playing.
            if (WorldMusic.BossClip(theme) == null)
            {
                WorldMusic.BeginBoss(theme);
                Check(song.world + " boss fallback keeps the song", src.clip == clip && WorldMusic.BossFallbackActive);
                WorldMusic.EndBoss();
                Check(song.world + " song still looping after the boss",
                      src.clip == clip && src.loop && WorldMusic.LoopClip == clip);
            }
        }

        // A scene-default world keeps the scene's track and disarms the full-song loop.
        var sceneTrack = AudioClip.Create("sceneTrack", 44100, 1, 44100, false);
        var music = GameObject.Find("MovingMusic").GetComponent<AudioSource>();
        music.clip = sceneTrack;
        foreach (var w in SceneDefaultWorlds)
        {
            WorldMusic.Apply(Theme(w));
            Check(w + " keeps the scene track", music.clip == sceneTrack);
            Check(w + " has no full-song loop", WorldMusic.LoopClip == null);
        }
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        // The replaced audio is gone and nothing points at it.
        var texts = new List<string>();
        foreach (var ext in new[] { "*.unity", "*.prefab", "*.asset", "*.controller", "*.mixer", "*.anim" })
            foreach (var f in Directory.GetFiles("Assets", ext, SearchOption.AllDirectories))
                texts.Add(File.ReadAllText(f));
        var code = new List<string>();
        foreach (var f in Directory.GetFiles("Assets/Scripts", "*.cs", SearchOption.AllDirectories))
            code.Add(File.ReadAllText(f));

        foreach (var removed in RemovedMusic)
        {
            string name = Path.GetFileNameWithoutExtension(removed.path);
            Check(name + " is deleted", !File.Exists(removed.path) && !File.Exists(removed.path + ".meta"));
            Check(name + " no longer imports", AssetDatabase.LoadAssetAtPath<AudioClip>(removed.path) == null);
            Check(name + " no longer loads from Resources", Resources.Load<AudioClip>("WorldMusic/" + name) == null);
            bool referenced = false;
            foreach (var t in texts) if (t.Contains(removed.guid)) { referenced = true; break; }
            Check(name + " GUID is unreferenced", !referenced);
            bool named = false;
            foreach (var c in code) if (c.Contains("\"WorldMusic/" + name + "\"")) { named = true; break; }
            Check(name + " is not named in code", !named);
        }
        foreach (var song in FullSongs)
            for (int stage = 1; stage <= 6; stage++)
                Check("no " + song.world + " stage " + stage + " left",
                      Resources.Load<AudioClip>(WorldMusic.StageResource(Theme(song.world), stage)) == null);
    }
}
