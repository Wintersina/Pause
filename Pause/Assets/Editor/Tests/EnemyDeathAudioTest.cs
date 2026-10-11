using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Enemy death audio (EnemyDeathAudio + EnemyDeathAudioImporter): every non-rock roster enemy, elite and boss of all six worlds plays
// one of the shared death clips (shared_<category>_<n>, a deterministic key -> category + pitch table), rocks keep their own clips
// (Tide / Storm rocks alias rock donors under a wet / crack layer), there is no synthesized fallback (an unknown key is silent),
// variants never repeat back to back, the polyphony cap / per-key interval / duck hold under a death burst, zero per-play
// allocation, the import settings, and an elite's death playing its cue.
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

    // Clips per shared category (the new death sounds).
    static int Count(string cat) { return cat == EnemyDeathAudio.Yelp || cat == EnemyDeathAudio.Human ? 2 : 1; }

    // Every key a death can use: the shared table plus all rock keys.
    static string[] allKeys;
    public static string[] AllKeys
    {
        get
        {
            if (allKeys == null)
            {
                var l = new System.Collections.Generic.List<string>();
                foreach (var r in EnemyDeathAudio.SharedTable) l.Add(r[0]);
                foreach (var d in EnemyRoster.All) if (d.role == EnemyRole.Rock) l.Add(d.key);
                allKeys = l.ToArray();
            }
            return allKeys;
        }
    }

    static double clock;

    public static int Execute()
    {
        fails = 0;
        using var sandbox = new TestHarness.Sandbox();
        bool simulate = EnemyDeathAudio.Simulate;
        var savedClock = EnemyDeathAudio.Clock;
        bool enabled = EnemyDeathAudio.AuthoredEnabled;
        try
        {
            EnemyDeathAudio.Simulate = true;
            EnemyDeathAudio.AuthoredEnabled = true;
            EnemyDeathAudio.Clock = () => clock;
            EnemyDeathAudio.ClearCache();
            Resolution();
            Table();
            Pitch();
            NoRepeats();
            Burst();
            Allocations();
            Switch();
            Import();
            Elite();
            Bosses();
        }
        finally
        {
            EnemyDeathAudio.Simulate = simulate;
            EnemyDeathAudio.Clock = savedClock;
            EnemyDeathAudio.AuthoredEnabled = enabled;
            EnemyDeathAudio.ClearCache();
            EliteSystem.Clear();
            EliteSystem.PlayerOverride = null;
        }
        Debug.Log("[EDA] failures: " + fails);
        return fails;
    }

    // Every roster enemy and elite resolves clips: rocks their own (3 variants), everything else its shared category.
    static void Resolution()
    {
        var missing = new System.Collections.Generic.List<string>();
        int enemies = 0, elites = 0, rocks = 0;
        foreach (var d in EnemyRoster.All)
        {
            enemies++;
            string cat = EnemyDeathAudio.CategoryOf(d.key);
            if (d.role == EnemyRole.Rock)
            {
                rocks++;
                if (cat != "" || EnemyDeathAudio.Variants(d.key) < 3) missing.Add(d.key);
            }
            else if (cat == "" || EnemyDeathAudio.Variants(d.key) != Count(cat)) missing.Add(d.key);
        }
        foreach (var e in EliteCatalog.All)
        {
            elites++;
            string cat = EnemyDeathAudio.CategoryOf(e.key);
            if (cat == "" || EnemyDeathAudio.Variants(e.key) != Count(cat)) missing.Add(e.key);
        }
        Check("every roster enemy (" + enemies + ", " + rocks + " rocks) and elite (" + elites + ") has death clips" + (missing.Count > 0 ? " (missing: " + string.Join(", ", missing) + ")" : ""), missing.Count == 0 && enemies > 60 && elites >= 25);
        foreach (var cat in EnemyDeathAudio.Categories)
        {
            bool ok = true;
            var c0 = Resources.Load<AudioClip>(EnemyDeathAudio.ResourceFolder + EnemyDeathAudio.SharedPrefix + cat + "_0");
            ok &= c0 != null && c0.length > .3f && c0.length < 3f && c0.channels == 1;
            for (int n = 0; n < Count(cat); n++) ok &= Resources.Load<AudioClip>(EnemyDeathAudio.ResourceFolder + EnemyDeathAudio.SharedPrefix + cat + "_" + n) != null;
            ok &= Resources.Load<AudioClip>(EnemyDeathAudio.ResourceFolder + EnemyDeathAudio.SharedPrefix + cat + "_" + Count(cat)) == null;
            Check("shared category " + cat + " has exactly " + Count(cat) + " mono clip(s)", ok);
        }
        Check("the tutorial's enemy key '" + TutorialEnemy.DefKey + "' has death clips", EnemyDeathAudio.Variants(TutorialEnemy.DefKey) >= 1);
        EnemyDeathAudio.ResetVoices();
        clock += 2.0;
        Check("an unknown key is silent (no fallback)", EnemyDeathAudio.Variants("hitbox") == 0 && !EnemyDeathAudio.PlayAuthored("hitbox", 1f) && EnemyDeathAudio.Played == 0);
        foreach (var d in EnemyRoster.All)
            if (d.role != EnemyRole.Rock) { EnemyDeathAudio.ResetVoices(); clock += 2.0; if (!EnemyDeathAudio.PlayAuthored(d.key, .68f) || EnemyDeathAudio.LastKey != d.key || EnemyDeathAudio.LastClip == null) { Check(d.key + " plays", false); break; } }
    }

    // The mapping table itself: sane categories, pitches, no duplicate keys, rocks never shared, the intended categories for each creature type.
    static void Table()
    {
        var seen = new System.Collections.Generic.HashSet<string>();
        bool ok = true, rocks = true; string bad = "";
        foreach (var r in EnemyDeathAudio.SharedTable)
        {
            float p = EnemyDeathAudio.SharedPitchOf(r[0]);
            bool good = seen.Add(r[0]) && System.Array.IndexOf(EnemyDeathAudio.Categories, r[1]) >= 0 && p >= .5f && p <= 1.6f && !r[0].Contains("_rock_");
            if (!good) { ok = false; bad = r[0]; }
            if (r[0].Contains("_rock_")) rocks = false;
        }
        Check("SharedTable: unique keys, known categories, pitch .5-1.6" + (bad != "" ? " (bad: " + bad + ")" : ""), ok && rocks && seen.Count >= 80);
        Check("rocks keep their own clips (no shared category) and Tide / Storm rocks still alias a donor",
              EnemyDeathAudio.CategoryOf("space_rock_dark") == "" && EnemyDeathAudio.CategoryOf("ember_rock_magma") == "" && EnemyDeathAudio.DonorOf("tide_rock_brain") != null && EnemyDeathAudio.DonorOf("storm_rock_hail") != null);
        Check("bigs and bosses roar", EnemyDeathAudio.CategoryOf("space_big") == EnemyDeathAudio.Roar && EnemyDeathAudio.CategoryOf("ember_big") == EnemyDeathAudio.Roar &&
              EnemyDeathAudio.CategoryOf("boss_space") == EnemyDeathAudio.Roar && EnemyDeathAudio.CategoryOf("boss_ember") == EnemyDeathAudio.Roar);
        Check("mines and machine elites whine", EnemyDeathAudio.CategoryOf("space_mine") == EnemyDeathAudio.Whine && EnemyDeathAudio.CategoryOf("ember_elite_kilnback") == EnemyDeathAudio.Whine &&
              EnemyDeathAudio.CategoryOf("verdant_elite_timber_hauler") == EnemyDeathAudio.Whine && EnemyDeathAudio.CategoryOf("space_elite_singularity_hauler") == EnemyDeathAudio.Whine);
        Check("insects screech, crewed fighters are human, scouts yelp, jellies scream alien",
              EnemyDeathAudio.CategoryOf("verdant_fighter_1") == EnemyDeathAudio.Screech && EnemyDeathAudio.CategoryOf("verdant_chaser") == EnemyDeathAudio.Screech &&
              EnemyDeathAudio.CategoryOf("space_fighter_2") == EnemyDeathAudio.Human && EnemyDeathAudio.CategoryOf("space_fighter_1") == EnemyDeathAudio.Yelp &&
              EnemyDeathAudio.CategoryOf("frost_alien") == EnemyDeathAudio.Alien && EnemyDeathAudio.CategoryOf("tide_alien") == EnemyDeathAudio.Alien);
        var used = new System.Collections.Generic.HashSet<string>();
        foreach (var r in EnemyDeathAudio.SharedTable) used.Add(r[1]);
        Check("all 6 shared categories are used", used.Count == EnemyDeathAudio.Categories.Length);
    }

    // The clip is shared but the pitch is per key (AudioSource.pitch, no extra files).
    static void Pitch()
    {
        EnemyDeathAudio.ResetVoices();
        clock += 5.0;
        EnemyDeathAudio.PlayAuthored("frost_elite_glacier_tender", .8f);
        float low = EnemyDeathAudio.LastPitch; var c1 = EnemyDeathAudio.LastClip;
        clock += 5.0;
        EnemyDeathAudio.PlayAuthored("frost_fighter_1", .8f);
        float high = EnemyDeathAudio.LastPitch; var c2 = EnemyDeathAudio.LastClip;
        Check("two keys share one clip at different pitches (" + low.ToString("0.00") + " vs " + high.ToString("0.00") + ")", c1 == c2 && c1 != null && high > low * 1.5f);
        Check("pitch = table pitch within jitter", Mathf.Abs(low - EnemyDeathAudio.SharedPitchOf("frost_elite_glacier_tender")) <= EnemyDeathAudio.SharedPitchOf("frost_elite_glacier_tender") * EnemyDeathAudio.PitchJitter + 1e-4f);
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
            EnemyDeathAudio.PlayAuthored("space_fighter_2", .68f);   // human: male + female
            if (EnemyDeathAudio.LastClip == last) repeat = true;
            last = EnemyDeathAudio.LastClip;
            seen.Add(last);
        }
        Check("200 plays: never the same variant twice in a row", !repeat && EnemyDeathAudio.Played == 200);
        Check("... both human variants heard", seen.Count == 2);
        seen.Clear();
        for (int i = 0; i < 100; i++) { clock += 2.0; EnemyDeathAudio.PlayAuthored("space_fighter_1", .68f); seen.Add(EnemyDeathAudio.LastClip); }
        Check("... both yelp variants heard", seen.Count == 2);
    }

    static void Burst()
    {
        EnemyDeathAudio.ResetVoices();
        EnemyDeathAudio.Seed(99);
        clock += 5.0;
        int maxVoices = 0;
        // 60 deaths in ~0.6 s: mixed keys, repeats of the same key 10 ms apart
        var keys = AllKeys;
        for (int i = 0; i < 60; i++)
        {
            clock += .01;
            EnemyDeathAudio.PlayAuthored(keys[(i / 2) % keys.Length], .68f);
            maxVoices = Mathf.Max(maxVoices, EnemyDeathAudio.ActiveVoices());
        }
        Check("60-death burst: at most " + EnemyDeathAudio.MaxVoices + " death voices (" + maxVoices + ")", maxVoices <= EnemyDeathAudio.MaxVoices && maxVoices > 1);
        Check("... at most " + EnemyDeathAudio.MaxLayerVoices + " layers", EnemyDeathAudio.ActiveLayers() <= EnemyDeathAudio.MaxLayerVoices);
        Check("... same key 10 ms apart dropped by the min interval (" + EnemyDeathAudio.Dropped + " dropped)", EnemyDeathAudio.Dropped >= 20);

        EnemyDeathAudio.ResetVoices();
        clock += 5.0;
        EnemyDeathAudio.PlayAuthored("space_big", .9f);
        float first = EnemyDeathAudio.LastVolume;
        clock += .05;
        EnemyDeathAudio.PlayAuthored("space_big", .9f);
        float second = EnemyDeathAudio.LastVolume;
        Check("a rapidly repeated identical key is ducked (" + first + " -> " + second + ")", second < first - 1e-4f && second >= first * EnemyDeathAudio.DuckFloor - 1e-4f);
    }

    static void Allocations()
    {
        EnemyDeathAudio.ResetVoices();
        foreach (var k in AllKeys) { clock += 2.0; EnemyDeathAudio.PlayAuthored(k, .68f); }   // warm up
        long before = System.GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 300; i++)
        {
            clock += .02;
            EnemyDeathAudio.PlayAuthored(AllKeys[i % AllKeys.Length], .68f);
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
        Check("AuthoredEnabled off: authored path refuses (silent: no fallback)", !played && EnemyDeathAudio.Played == 0);
        EnemyDeathAudio.AuthoredEnabled = true;
    }

    static void Import()
    {
        var guids = AssetDatabase.FindAssets("t:AudioClip", new[] { EnemyDeathAudioImporter.Folder.TrimEnd('/') });
        bool ok = guids.Length == 8 + 42 + 6 + 10;   // 8 shared clips + the 14 rocks' 42 cues + the 6 wet / crack layer clips (storm_crack_, tide_wet_) + 10 boss hurt blows -- recount if rocks change
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
            good &= clip != null && clip.channels == 1 && clip.channels == 1;
            if (!good) { ok = false; bad = path; break; }
        }
        // Android build override: Vorbis / ADPCM, still decompress-on-load + preloaded.
        bool andOk = true; string andBad = "";
        foreach (var g in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(g);
            var imp = (AudioImporter)AssetImporter.GetAtPath(path);
            if (!imp.ContainsSampleSettingsOverride("Android")) { andOk = false; andBad = path; break; }
            var a = imp.GetOverrideSampleSettings("Android");
            if (!(a.loadType == AudioClipLoadType.DecompressOnLoad && a.preloadAudioData &&
                  (a.compressionFormat == AudioCompressionFormat.Vorbis || a.compressionFormat == AudioCompressionFormat.ADPCM)))
            { andOk = false; andBad = path; break; }
        }
        Check("EnemyDeath clips import Android override Vorbis/ADPCM, decompress-on-load, preload" + (andBad != "" ? " (bad: " + andBad + ")" : ""), andOk);
        Check("EnemyDeath clips (" + guids.Length + ") import mono / PCM / decompress-on-load / preload" + (bad != "" ? " (bad: " + bad + ")" : ""), ok);
    }

    // Boss death cues (BossActor.BeginOutro / the codex): the shared roar at each boss's own pitch.
    static void Bosses()
    {
        foreach (var b in BossCatalog.All)
        {
            string key = EnemyDeathAudio.BossKey(b.artKey);
            Check(key + " is a roar with 1 clip (" + EnemyDeathAudio.Variants(key) + ")", EnemyDeathAudio.CategoryOf(key) == EnemyDeathAudio.Roar && EnemyDeathAudio.Variants(key) == 1);
            clock += 5; EnemyDeathAudio.ResetVoices();
            bool played = EnemyDeathAudio.PlayBossDeath(b.artKey);
            Check(key + " PlayBossDeath finds and plays its clip", played && EnemyDeathAudio.Played == 1 && EnemyDeathAudio.LastKey == key && EnemyDeathAudio.LastClip != null && EnemyDeathAudio.LastPitch < 1f);

            // the hurt blow (a heart lost): its own two short clips, quieter than the death cue (not a shared death sound)
            string hurt = EnemyDeathAudio.BossHurtKey(b.artKey);
            // Storm and Desert bosses are newer than the authored hurt cues; their blows stay silent until the user authors them
            if (hurt == "boss_storm_hurt" || hurt == "boss_desert_hurt") { Check(hurt + " is pending authored audio: nothing registered, PlayBossHurt harmless", EnemyDeathAudio.Variants(hurt) == 0 && !EnemyDeathAudio.PlayBossHurt(b.artKey)); continue; }
            Check(hurt + " has 2 short variants (" + EnemyDeathAudio.Variants(hurt) + ")", EnemyDeathAudio.Variants(hurt) == 2 && EnemyDeathAudio.AuthoredClip(hurt, 0) != null && EnemyDeathAudio.AuthoredClip(hurt, 1) != null &&
                  EnemyDeathAudio.AuthoredClip(hurt, 0).length < 1f);
            clock += 5; EnemyDeathAudio.ResetVoices();
            bool hurtPlayed = EnemyDeathAudio.PlayBossHurt(b.artKey);
            Check(hurt + " PlayBossHurt plays it under the death cue's volume", hurtPlayed && EnemyDeathAudio.Played == 1 && EnemyDeathAudio.LastKey == hurt &&
                  EnemyDeathAudio.LastVolume < EnemyDeathAudio.BossVolume * EnemyDeathAudio.AuthoredGain);
        }
        Check("PlayBossHurt for an unknown boss is harmless", !EnemyDeathAudio.PlayBossHurt("Nope") && !EnemyDeathAudio.PlayBossHurt(""));
        Check("unknown boss key is harmless", !EnemyDeathAudio.PlayBossDeath("Nope") && !EnemyDeathAudio.PlayBossDeath(""));
        EnemyDeathAudio.ResetVoices();
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
              EnemyDeathAudio.Played == 1 && c != null && (c.name == "shared_human_0" || c.name == "shared_human_1") && EnemyDeathAudio.LastKey == def.key);
        RunScore.EndRun(RunScore.RunId);
    }
}
