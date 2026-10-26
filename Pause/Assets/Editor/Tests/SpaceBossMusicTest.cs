using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// SPACE BOSS MUSIC (Resources/WorldMusic/Boss_Space, the user's "Neon Armageddon"):
//   the Space boss resolves to its own clip (~137.2 s), which takes over the
//   MovingMusic source when the encounter starts; defeat, escape and the
//   player's death each hand the world's track back; other worlds' bosses do
//   not pick it up; the Android import is Vorbis, streamed, not preloaded.
//
//   Unity -batchmode -quit -projectPath Pause -executeMethod SpaceBossMusicTest.Run
public static class SpaceBossMusicTest
{
    static int fails;
    static void Check(string what, bool ok)
    {
        Debug.Log((ok ? "[SBM] PASS  " : "[SBM] FAIL  ") + what);
        if (!ok) fails++;
    }

    public static void Run()
    {
        TestHarness.Exit(Execute());
    }

    // Per-world boss tracks: a row per world (WorldMusic.BossResource = WorldMusic/Boss_<World>).
    // A world with no row keeps the fallback (its own track, nudged up in pitch).
    static readonly (string world, float seconds)[] Tracks =
    {
        // (Verdant: "jungle boss 3", 207.476 s decoded + the importer's one-frame mp3 padding)
        ("Space", 137.232f), ("Frost", 159.744f), ("Verdant", 207.5f), ("Ember", 92.664f), ("Tide", 165.024f), ("Storm", 119.904f), ("Desert", 184.824f),
    };
    static string PathOf(string world) { return "Assets/Audio/Resources/WorldMusic/Boss_" + world + ".mp3"; }
    // Desert has no world on this branch yet: its clip and import are checked by name only.
    static int IndexOf(string world)
    {
        for (int i = 0; i < WorldManager.Worlds.Length; i++) if (WorldManager.Worlds[i].displayName == world) return i;
        return -1;
    }

    public static int Execute()
    {
        fails = 0;
        using var sandbox = new TestHarness.Sandbox();
        try
        {
            Resolves();
            Import();
            foreach (var t in Tracks)
                if (IndexOf(t.world) >= 0)
                    foreach (var how in new[] { "defeat", "escape", "death" }) Lifecycle(IndexOf(t.world), how);
        }
        finally
        {
            Time.timeScale = 1f;
            buttonClicks.playerDied = false;
            BossEncounter.ResetRun();
        }
        Debug.Log("[SBM] " + (fails == 0 ? "ALL PASS" : fails + " FAILED"));
        return fails;
    }

    static void Resolves()
    {
        var seen = new HashSet<AudioClip>();
        foreach (var t in Tracks)
        {
            var clip = Resources.Load<AudioClip>("WorldMusic/Boss_" + t.world);
            Check(t.world + " boss clip loads and is distinct", clip != null && seen.Add(clip));
            Check(t.world + " clip is ~" + t.seconds + " s (mp3 padding allowed)", clip != null && Mathf.Abs(clip.length - t.seconds) < 0.15f);
            int w = IndexOf(t.world);
            if (w >= 0) Check(t.world + " boss resolves through WorldMusic.BossClip", WorldMusic.BossClip(WorldManager.Worlds[w]) == clip);
        }
        foreach (var theme in WorldManager.Worlds)
        {
            bool has = false;
            foreach (var t in Tracks) if (t.world == theme.displayName) has = true;
            if (!has) Check(theme.displayName + " boss unaffected (fallback)", WorldMusic.BossClip(theme) == null);
        }
    }

    static void Import()
    {
        foreach (var t in Tracks)
        {
            var imp = AssetImporter.GetAtPath(PathOf(t.world)) as AudioImporter;
            Check(t.world + " boss is imported", imp != null);
            if (imp == null) continue;
            var a = imp.GetOverrideSampleSettings("Android");
            Check(t.world + " Android: override, Vorbis, streaming, q 0.35",
                  imp.ContainsSampleSettingsOverride("Android") && a.compressionFormat == AudioCompressionFormat.Vorbis &&
                  a.loadType == AudioClipLoadType.Streaming && Mathf.Abs(a.quality - 0.35f) < 0.011f);
            Check(t.world + " preload off, stereo", !a.preloadAudioData && !imp.forceToMono);
        }
    }

    // The encounter starts the fight music at its intro and every way out
    // gives the world's own track back.
    static void Lifecycle(int world, string how)
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        BossEncounter.ResetRun();
        buttonClicks.playerDied = false;
        score.pauseCounter = 0;
        Time.timeScale = 1f;
        moveBackGround.speed = .37f;
        PlayerPrefs.SetInt(WorldManager.PrefsCurrentWorld, world);
        how = WorldManager.Worlds[world].displayName + " " + how;

        var src = new GameObject("MovingMusic").AddComponent<AudioSource>();
        var worldTrack = AudioClip.Create("world", 4410, 1, 44100, false);
        src.clip = worldTrack;
        var bossClip = WorldMusic.BossClip(WorldManager.Worlds[world]);

        Check(how + ": world track before the encounter", src.clip == worldTrack && !WorldMusic.BossTrackPlaying);
        BossEncounter.Begin(world, null);
        var enc = BossEncounter.Instance;
        enc.Step(.1f, 1f);
        Check(how + ": boss track starts with the encounter", src.clip == bossClip && src.loop && WorldMusic.BossTrackPlaying);

        enc.BeginFight();
        enc.Step(.1f, 1f);
        Check(how + ": still the boss track through the fight", src.clip == bossClip && WorldMusic.BossTrackPlaying);

        if (how.EndsWith("defeat")) enc.OnShipAttackHit(1000f);
        if (how.EndsWith("death")) buttonClicks.playerDied = true;
        for (int i = 0; i < 2000 && enc.State != BossEncounter.Phase.Done && enc.State != BossEncounter.Phase.Aborted; i++)
            enc.Step(.1f, 1f);
        buttonClicks.playerDied = false;
        Check(how + ": encounter over", enc.State == BossEncounter.Phase.Done || enc.State == BossEncounter.Phase.Aborted);
        // (Edit mode never swaps the source back -- that needs Application.isPlaying --
        // so the hand-back is the fight flag clearing.)
        Check(how + ": the world's music is handed back", !WorldMusic.BossTrackPlaying && !WorldMusic.BossFallbackActive);
    }
}
