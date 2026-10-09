using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Authored enemy death cues (EnemyDeathAudio + EnemyDeathAudioImporter):
// the 15 Space keys resolve three variants each, the nine living-occupant
// keys resolve screams (the rest none), Frost / Verdant / Ember keep their
// procedural clip, variants never repeat back to back, the scream chance and
// delay, the polyphony cap / per-key interval under a death burst, zero
// per-play allocation, the import settings, and an elite's death playing
// its authored cue.
//
//   Unity -batchmode -quit -projectPath <abs>/Pause -executeMethod EnemyDeathAudioTest.Run
public static class EnemyDeathAudioTest
{
    static int fails;
    static void Check(string what, bool ok)
    {
        Debug.Log((ok ? "[EDA] PASS  " : "[EDA] FAIL  ") + what);
        if (!ok) fails++;
    }

    public static void Run() { TestHarness.Exit(Execute()); }

    public static readonly string[] SpaceKeys =
    {
        "space_fighter_1", "space_fighter_2", "space_fighter_3", "space_fighter_4", "space_chaser", "space_alien",
        "space_big", "space_mine", "space_rock_crater", "space_rock_cluster", "space_rock_dark",
        "space_elite_eventide_bastion", "space_elite_orbit_reaver", "space_elite_rift_lancer", "space_elite_singularity_hauler",
    };
    public static readonly string[] Screaming =
    {
        "space_fighter_1", "space_fighter_2", "space_fighter_3", "space_fighter_4", "space_alien",
        "space_elite_eventide_bastion", "space_elite_orbit_reaver", "space_elite_rift_lancer", "space_elite_singularity_hauler",
    };

    static double clock;

    public static int Execute()
    {
        fails = 0;
        using var sandbox = new TestHarness.Sandbox();
        bool simulate = EnemyDeathAudio.Simulate;
        var savedClock = EnemyDeathAudio.Clock;
        float chance = EnemyDeathAudio.ScreamChance;
        bool enabled = EnemyDeathAudio.AuthoredEnabled;
        try
        {
            EnemyDeathAudio.Simulate = true;
            EnemyDeathAudio.AuthoredEnabled = true;
            EnemyDeathAudio.Clock = () => clock;
            EnemyDeathAudio.ClearCache();
            Resolution();
            OtherWorlds();
            NoRepeats();
            ScreamOdds();
            Burst();
            Allocations();
            Switch();
            Import();
            Elite();
        }
        finally
        {
            EnemyDeathAudio.Simulate = simulate;
            EnemyDeathAudio.Clock = savedClock;
            EnemyDeathAudio.ScreamChance = chance;
            EnemyDeathAudio.AuthoredEnabled = enabled;
            EnemyDeathAudio.ClearCache();
            EliteSystem.Clear();
            EliteSystem.PlayerOverride = null;
        }
        Debug.Log("[EDA] failures: " + fails);
        return fails;
    }

    static void Resolution()
    {
        foreach (var key in SpaceKeys)
        {
            bool clips = EnemyDeathAudio.Variants(key) == 3;
            for (int n = 0; n < 3 && clips; n++) clips &= EnemyDeathAudio.AuthoredClip(key, n) != null;
            Check(key + " resolves 3 authored variants (" + EnemyDeathAudio.Variants(key) + ")", clips);
            int want = System.Array.IndexOf(Screaming, key) >= 0 ? 3 : 0;
            Check(key + " has " + want + " screams (" + EnemyDeathAudio.ScreamVariants(key) + ")", EnemyDeathAudio.ScreamVariants(key) == want);
        }
        foreach (var d in EnemyRoster.All)
            if (d.key.StartsWith("space_"))
                Check("roster key " + d.key + " is authored", EnemyDeathAudio.Variants(d.key) >= 3);
    }

    static void OtherWorlds()
    {
        int checkedKeys = 0;
        bool none = true, procedural = true;
        foreach (var d in EnemyRoster.All)
        {
            if (d.key.StartsWith("space_")) continue;
            checkedKeys++;
            none &= EnemyDeathAudio.Variants(d.key) == 0 && EnemyDeathAudio.ScreamVariants(d.key) == 0;
            none &= !EnemyDeathAudio.PlayAuthored(d.key, 1f);
            procedural &= EnemyDeathAudio.ProceduralClip(d.key, d.role) != null;
        }
        Check("Frost/Verdant/Ember roster keys (" + checkedKeys + ") have no authored clips", checkedKeys >= 30 && none);
        Check("... and still synthesize their procedural clip", procedural);
        Check("frost_fighter_1 explicitly: no authored, procedural non-null",
              EnemyDeathAudio.Variants("frost_fighter_1") == 0 && EnemyDeathAudio.ProceduralClip("frost_fighter_1", EnemyRole.Fighter) != null);
        foreach (var e in EliteCatalog.All)
            if (!e.key.StartsWith("space_") && EnemyDeathAudio.Variants(e.key) != 0)
                Check("non-Space elite " + e.key + " has no authored clip", false);
    }

    static void NoRepeats()
    {
        EnemyDeathAudio.ResetVoices();
        EnemyDeathAudio.Seed(12345);
        AudioClip last = null;
        bool repeat = false;
        var seen = new System.Collections.Generic.HashSet<AudioClip>();
        for (int i = 0; i < 200; i++)
        {
            clock += 2.0;   // every voice finished, outside the duck window
            EnemyDeathAudio.PlayAuthored("space_fighter_2", .68f);
            if (EnemyDeathAudio.LastClip == last) repeat = true;
            last = EnemyDeathAudio.LastClip;
            seen.Add(last);
        }
        Check("200 plays: never the same variant twice in a row", !repeat && EnemyDeathAudio.Played == 200);
        Check("... all 3 variants heard", seen.Count == 3);
    }

    static void ScreamOdds()
    {
        EnemyDeathAudio.ResetVoices();
        EnemyDeathAudio.Seed(777);
        EnemyDeathAudio.ScreamChance = .65f;
        const int N = 1000;
        bool delayOk = true;
        for (int i = 0; i < N; i++)
        {
            clock += 2.0;
            int before = EnemyDeathAudio.Screams;
            EnemyDeathAudio.PlayAuthored("space_alien", .68f);
            if (EnemyDeathAudio.Screams > before)
            {
                float d = EnemyDeathAudio.LastScreamDelay;
                delayOk &= d >= EnemyDeathAudio.ScreamDelayMin - 1e-6f && d <= EnemyDeathAudio.ScreamDelayMax + 1e-6f;
            }
        }
        float rate = EnemyDeathAudio.Screams / (float)N;
        // binomial sd at p=.65, n=1000 is ~0.015: 4 sd bound
        Check("scream rate honours ScreamChance .65 (" + rate.ToString("0.000") + ")", Mathf.Abs(rate - .65f) < .06f);
        Check("scream delay within 34-55 ms", delayOk && EnemyDeathAudio.ScreamDelayMin >= .034f - 1e-6f && EnemyDeathAudio.ScreamDelayMax <= .055f + 1e-6f);
        clock += 2.0;
        EnemyDeathAudio.PlayAuthored("space_alien", .68f);
        float deathVol = EnemyDeathAudio.LastVolume;
        EnemyDeathAudio.ScreamChance = 0f;
        int s0 = EnemyDeathAudio.Screams;
        for (int i = 0; i < 50; i++) { clock += 2.0; EnemyDeathAudio.PlayAuthored("space_alien", .68f); }
        Check("ScreamChance 0: no screams", EnemyDeathAudio.Screams == s0);
        EnemyDeathAudio.ScreamChance = .65f;
        int c0 = EnemyDeathAudio.Screams;
        for (int i = 0; i < 50; i++) { clock += 2.0; EnemyDeathAudio.PlayAuthored("space_chaser", .68f); }
        Check("a key without screams never screams", EnemyDeathAudio.Screams == c0);
        Check("death volume sane (" + deathVol + ")", deathVol > .3f && deathVol < 1f);
    }

    static void Burst()
    {
        EnemyDeathAudio.ResetVoices();
        EnemyDeathAudio.Seed(99);
        EnemyDeathAudio.ScreamChance = 1f;
        clock += 5.0;
        int maxVoices = 0, maxScreams = 0;
        // 40 deaths in ~0.4 s: mixed keys, repeats of the same key 10 ms apart
        for (int i = 0; i < 40; i++)
        {
            clock += .01;
            string key = SpaceKeys[(i / 2) % SpaceKeys.Length];
            EnemyDeathAudio.PlayAuthored(key, .68f);
            maxVoices = Mathf.Max(maxVoices, EnemyDeathAudio.ActiveVoices());
            maxScreams = Mathf.Max(maxScreams, EnemyDeathAudio.ActiveScreams());
        }
        Check("40-death burst: at most " + EnemyDeathAudio.MaxVoices + " death voices (" + maxVoices + ")", maxVoices <= EnemyDeathAudio.MaxVoices && maxVoices > 1);
        Check("... at most " + EnemyDeathAudio.MaxScreamVoices + " screams (" + maxScreams + ")", maxScreams <= EnemyDeathAudio.MaxScreamVoices);
        Check("... same key 10 ms apart dropped by the min interval (" + EnemyDeathAudio.Dropped + " dropped)", EnemyDeathAudio.Dropped >= 20);

        EnemyDeathAudio.ResetVoices();
        clock += 5.0;
        EnemyDeathAudio.PlayAuthored("space_big", .9f);
        float first = EnemyDeathAudio.LastVolume;
        clock += .05;
        EnemyDeathAudio.PlayAuthored("space_big", .9f);
        float second = EnemyDeathAudio.LastVolume;
        Check("a rapidly repeated identical key is ducked (" + first + " -> " + second + ")", second < first - 1e-4f && second >= first * EnemyDeathAudio.DuckFloor - 1e-4f);
        EnemyDeathAudio.ScreamChance = .65f;
    }

    static void Allocations()
    {
        EnemyDeathAudio.ResetVoices();
        foreach (var k in SpaceKeys) { clock += 2.0; EnemyDeathAudio.PlayAuthored(k, .68f); }   // warm up
        long before = System.GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 300; i++)
        {
            clock += .02;
            EnemyDeathAudio.PlayAuthored(SpaceKeys[i % SpaceKeys.Length], .68f);
        }
        long allocated = System.GC.GetAllocatedBytesForCurrentThread() - before;
        Check("zero allocations over 300 authored plays after warm-up (" + allocated + " bytes)", allocated == 0);
    }

    static void Switch()
    {
        EnemyDeathAudio.ResetVoices();
        EnemyDeathAudio.AuthoredEnabled = false;
        clock += 2.0;
        bool played = EnemyDeathAudio.PlayAuthored("space_fighter_1", .68f) || EnemyDeathAudio.PlayElite("space_elite_orbit_reaver");
        Check("AuthoredEnabled off: authored path refuses (procedural fallback)", !played && EnemyDeathAudio.Played == 0);
        EnemyDeathAudio.AuthoredEnabled = true;
    }

    static void Import()
    {
        var guids = AssetDatabase.FindAssets("t:AudioClip", new[] { EnemyDeathAudioImporter.Folder.TrimEnd('/') });
        bool ok = guids.Length >= 72;
        string bad = "";
        foreach (var g in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(g);
            var imp = AssetImporter.GetAtPath(path) as AudioImporter;
            if (imp == null) { ok = false; bad = path; break; }
            var s = imp.defaultSampleSettings;
            bool good = imp.forceToMono && s.loadType == AudioClipLoadType.DecompressOnLoad &&
                        s.compressionFormat == AudioCompressionFormat.PCM && s.preloadAudioData &&
                        s.sampleRateSetting == AudioSampleRateSetting.PreserveSampleRate;
            var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
            good &= clip != null && clip.channels == 1 && clip.frequency == 44100;
            if (!good) { ok = false; bad = path; break; }
        }
        Check("EnemyDeath clips (" + guids.Length + ") import mono / PCM / decompress-on-load / preload / 44.1 kHz" + (bad != "" ? " (bad: " + bad + ")" : ""), ok);
    }

    static void Elite()
    {
        EnemyDeathAudio.ResetVoices();
        EliteSystem.Clear();
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        RunScore.EndRun(RunScore.RunId);
        PlayerPrefs.SetString("HasDoneTut", "true");
        startMenu.youAreInTutorial = false;
        buttonClicks.playerDied = false;
        RunScore.BeginRun(true, true);
        var pilot = new GameObject("~Pilot").transform;
        pilot.position = new Vector3(0f, -2.5f, 0f);
        EliteSystem.PlayerOverride = pilot;
        EliteCatalog.Reload();
        var def = EliteCatalog.Find("space_elite_orbit_reaver");
        Check("Orbit Reaver def found", def != null);
        if (def == null) return;
        clock += 2.0;
        var e = EliteShip.CreateInPlay(def, new Vector2(0f, 1.5f));
        e.TakeHit(EliteDamage.Crash, e.transform.position, e.Hearts);
        var c = EnemyDeathAudio.LastClip;
        Check("the elite died", e == null || e.State == EliteState.Dead);
        Check("an elite's death plays its authored cue (" + (c != null ? c.name : "none") + ")",
              EnemyDeathAudio.Played == 1 && c != null && c.name.StartsWith("space_elite_orbit_reaver_") && EnemyDeathAudio.LastKey == def.key);
        RunScore.EndRun(RunScore.RunId);
    }
}
