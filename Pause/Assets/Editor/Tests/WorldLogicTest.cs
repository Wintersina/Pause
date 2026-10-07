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

    // Worlds still on the six 30-second stage arrangements.
    static readonly string[] ProgressiveWorlds = { "Space", "Verdant" };

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

        Check("four worlds defined", WorldManager.Worlds.Length == 4);
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
        Check("developer flag unlocks every world", PlayerPrefs.GetInt(WorldManager.PrefsHighestWorld) == 3);
        DeveloperUnlocks.SetEnabled(false);
        Check("turning developer flag off restores locked ship state", !PlayerPrefs.HasKey("boughtship15"));
        Check("turning developer flag off restores saved world progress",
              PlayerPrefs.GetInt(WorldManager.PrefsHighestWorld) == 1);
        foreach (string world in ProgressiveWorlds)
        {
            for (int stage = 1; stage <= 6; stage++)
                Check(world + " has progressive stage " + stage,
                    AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Audio/Resources/WorldMusic/" +
                        world + "Stage" + stage.ToString("00") + ".wav") != null);
        }
        for (int w = 0; w < EnemyRoster.WorldKeys.Length; w++)
            Check("the " + EnemyRoster.WorldKeys[w] + " rail mine flipbook is present (neon atlas row)",
                  AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Art/Resources/" + RailMineArt.AtlasPath + ".png") != null &&
                  RailMineArt.Frame(w, RailMineArt.Dormant) != null);

        Check("starts at index 0", WorldManager.CurrentIndex == 0);
        Check("has a next world", WorldManager.HasNext);

        // walk the whole progression
        for (int i = 1; i < WorldManager.Worlds.Length; i++)
        {
            WorldManager.CurrentIndex = i;
            Check("index " + i + " -> " + WorldManager.Worlds[i].displayName,
                  WorldManager.CurrentIndex == i);
            Check("world " + i + " has art folder",
                  !string.IsNullOrEmpty(WorldManager.Current.resourceFolder));
            Check("world " + i + " has music",
                  !string.IsNullOrEmpty(WorldManager.Current.musicResource));
            Check("world " + i + " ramps harder than previous",
                  WorldManager.Worlds[i].speedRampPerSecond >
                  WorldManager.Worlds[i - 1].speedRampPerSecond);
        }

        Check("last world has no next", !WorldManager.HasNext);
        Check("highest recorded", PlayerPrefs.GetInt(WorldManager.PrefsHighestWorld, 0) == 3);

        // clamping
        WorldManager.CurrentIndex = 99;
        Check("clamps above range", WorldManager.CurrentIndex == WorldManager.Worlds.Length - 1);
        WorldManager.CurrentIndex = -5;
        Check("clamps below range", WorldManager.CurrentIndex == 0);
        Check("highest is not lowered by going back",
              PlayerPrefs.GetInt(WorldManager.PrefsHighestWorld, 0) == 3);

        // the assets the themes point at must actually resolve
        for (int i = 1; i < WorldManager.Worlds.Length; i++)
        {
            var t = WorldManager.Worlds[i];
            string p = "Assets/Art/Backgrounds/Resources/Worlds/" + t.displayName + "/Backdrop/sky.png";
            Check("art present for " + t.displayName + " (" + p + ")",
                  AssetDatabase.LoadAssetAtPath<Texture2D>(p) != null);
            Check("music present for " + t.displayName + " (" + t.musicResource + ")",
                  Resources.Load<AudioClip>(t.musicResource) != null);
        }

        FullSongWorlds();

        PlayerPrefs.DeleteKey(WorldManager.PrefsCurrentWorld);
        PlayerPrefs.DeleteKey(WorldManager.PrefsHighestWorld);

        Debug.Log("[WT] failures: " + failures);
        return failures;
    }

    static WorldTheme Theme(string name)
    {
        foreach (var t in WorldManager.Worlds)
            if (t.displayName == name) return t;
        return null;
    }

    static void FullSongWorlds()
    {
        foreach (var w in ProgressiveWorlds)
            Check(w + " keeps its stage arrangements", WorldMusic.UsesStages(Theme(w)));

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

        // A progressive world disarms the full-song loop.
        var verdantStage = Resources.Load<AudioClip>("WorldMusic/VerdantStage01");
        WorldMusic.Apply(Theme("Verdant"));
        var music = GameObject.Find("MovingMusic").GetComponent<AudioSource>();
        Check("Verdant goes back to its first stage", verdantStage != null && music.clip == verdantStage);
        Check("Verdant has no full-song loop", WorldMusic.LoopClip == null);
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
