using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Authored enemy death cues (EnemyDeathAudio + EnemyDeathAudioImporter):
// every roster enemy and elite of ALL worlds (Space, Frost, Verdant, Ember)
// resolves three variants, the living-occupant keys resolve screams (mines,
// chasers, bigs, rocks and a few elites none), there is no synthesized
// fallback (an unknown key is silent), variants never repeat back to back, the scream chance and
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

    public static readonly string[] VerdantKeys =
    {
        "verdant_fighter_1", "verdant_fighter_2", "verdant_fighter_3", "verdant_fighter_4", "verdant_chaser", "verdant_alien",
        "verdant_big", "verdant_mine", "verdant_rock_pod", "verdant_rock_spore", "verdant_rock_knot", "verdant_rock_vine",
        "verdant_elite_resin_warden",
    };
    public static readonly string[] EmberKeys =
    {
        "ember_fighter_1", "ember_fighter_2", "ember_fighter_3", "ember_fighter_4", "ember_chaser", "ember_alien",
        "ember_big", "ember_mine", "ember_rock_magma", "ember_rock_cinder", "ember_rock_obsidian", "ember_rock_islet",
        "ember_elite_ash_wraith", "ember_elite_brass_vulture", "ember_elite_cauterizer", "ember_elite_coalrunner",
        "ember_elite_kilnback", "ember_elite_sunstoke",
    };
    public static readonly string[] FrostKeys =
    {
        "frost_fighter_1", "frost_fighter_2", "frost_fighter_3", "frost_fighter_4", "frost_chaser", "frost_alien",
        "frost_big", "frost_mine", "frost_rock_shard", "frost_rock_chunk", "frost_rock_rime",
        "frost_elite_cryo_siren", "frost_elite_floe_harrower", "frost_elite_glacier_tender", "frost_elite_rimebreaker",
        "frost_elite_whiteout_sentinel",
    };
    // Authored keys that must have screams; every other authored key must not.
    public static readonly string[] ScreamingNew =
    {
        "frost_alien",
        "verdant_fighter_1", "verdant_fighter_2", "verdant_fighter_3", "verdant_fighter_4", "verdant_alien", "verdant_elite_resin_warden",
        "ember_fighter_1", "ember_fighter_2", "ember_fighter_3", "ember_fighter_4", "ember_alien",
        "ember_elite_ash_wraith", "ember_elite_brass_vulture", "ember_elite_coalrunner", "ember_elite_sunstoke",
    };
    static string[] allKeys;
    public static string[] AllKeys
    {
        get
        {
            if (allKeys == null)
            {
                var l = new System.Collections.Generic.List<string>(SpaceKeys);
                l.AddRange(VerdantKeys); l.AddRange(EmberKeys); l.AddRange(FrostKeys);
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
        float chance = EnemyDeathAudio.ScreamChance;
        bool enabled = EnemyDeathAudio.AuthoredEnabled;
        try
        {
            EnemyDeathAudio.Simulate = true;
            EnemyDeathAudio.AuthoredEnabled = true;
            EnemyDeathAudio.Clock = () => clock;
            EnemyDeathAudio.ClearCache();
            Resolution();
            NewWorlds();
            Borrow();
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

    // Every key the game can pass is authored: all roster enemies and all
    // elite defs, whatever the world. No synthesized fallback remains.
    static void OtherWorlds()
    {
        int enemies = 0, elites = 0;
        var missing = new System.Collections.Generic.List<string>();
        int tideRoster = 0, tideSilent = 0;
        foreach (var d in EnemyRoster.All)
        {
            // TODO(sounds): Tide's death cues do not exist yet (Codex sound job pending, add-world phase 8/15 --
            // checklist section F). Its keys may lack clips until then; they play silently, never throw.
            if (d.key.StartsWith("tide_")) { tideRoster++; if (EnemyDeathAudio.Variants(d.key) < 3) tideSilent++; continue; }
            enemies++;
            if (EnemyDeathAudio.Variants(d.key) < 3) missing.Add(d.key);
        }
        foreach (var e in EliteCatalog.All) { elites++; if (EnemyDeathAudio.Variants(e.key) < 3) missing.Add(e.key); }
        Check("every roster enemy (" + enemies + ") and elite (" + elites + ") of every live-art world has authored clips" +
              (missing.Count > 0 ? " (missing: " + string.Join(", ", missing) + ")" : ""), missing.Count == 0 && enemies == 46 && elites == 16);
        Check("Tide's 12 roster keys are in the roster; their death sounds are still a TODO (" + tideSilent + " without clips) and playing one is silent, not an error",
              tideRoster == 12 && (tideSilent == 0 || (EnemyDeathAudio.PlayAuthored("tide_big", .5f) == false && EnemyDeathAudio.Variants("tide_big") == 0)));
        // the tutorial's enemy is a roster alien; the rail mines are roster mines
        Check("tutorial enemy key '" + TutorialEnemy.DefKey + "' is authored", EnemyDeathAudio.Variants(TutorialEnemy.DefKey) == 3);
        foreach (var d in EnemyRoster.All)
            if (d.role == EnemyRole.Mine && !d.key.StartsWith("tide_")) Check("rail mine " + d.key + " is authored", EnemyDeathAudio.Variants(d.key) == 3);
        // no synthesized fallback: an unknown key resolves nothing and plays nothing
        EnemyDeathAudio.ResetVoices();
        clock += 2.0;
        Check("an unknown key is silent (no fallback)", EnemyDeathAudio.Variants("hitbox") == 0 && !EnemyDeathAudio.PlayAuthored("hitbox", 1f) && EnemyDeathAudio.Played == 0);
        // the key the game passes for each roster body (EnemyIdentity.Set -> def.key) and for each elite (def.key)
        foreach (var e in EliteCatalog.All)
        {
            EnemyDeathAudio.ResetVoices();
            clock += 2.0;
            Check("elite " + e.key + " PlayElite plays an authored clip", EnemyDeathAudio.PlayElite(e.key) && EnemyDeathAudio.LastKey == e.key);
        }
    }

    static void NewWorlds()
    {
        var all = new System.Collections.Generic.List<string>(VerdantKeys);
        all.AddRange(EmberKeys); all.AddRange(FrostKeys);
        foreach (var key in all)
        {
            bool clips = EnemyDeathAudio.Variants(key) == 3;
            for (int n = 0; n < 3 && clips; n++) clips &= EnemyDeathAudio.AuthoredClip(key, n) != null && EnemyDeathAudio.AuthoredClip(key, n).length > .05f;
            Check(key + " resolves 3 authored variants (" + EnemyDeathAudio.Variants(key) + ")", clips);
            bool wantScream = System.Array.IndexOf(ScreamingNew, key) >= 0;
            int want = wantScream ? 3 : 0;
            Check(key + " has " + want + " screams (" + EnemyDeathAudio.ScreamVariants(key) + ")", EnemyDeathAudio.ScreamVariants(key) == want);
            bool unit = key.EndsWith("_mine") || key.EndsWith("_chaser") || key.EndsWith("_big") || key.Contains("_rock_");
            if (unit) Check(key + " (mine/chaser/big/rock) has no screams", EnemyDeathAudio.ScreamVariants(key) == 0);
        }
        int rosterChecked = 0;
        foreach (var d in EnemyRoster.All)
            if (d.key.StartsWith("verdant_") || d.key.StartsWith("ember_") || d.key.StartsWith("frost_"))
            {
                rosterChecked++;
                Check("roster key " + d.key + " is authored", EnemyDeathAudio.Variants(d.key) == 3);
            }
        Check("all 35 Frost+Verdant+Ember roster enemies covered (" + rosterChecked + ")", rosterChecked == 35);
        int elites = 0;
        foreach (var e in EliteCatalog.All)
            if (e.key.StartsWith("verdant_") || e.key.StartsWith("ember_") || e.key.StartsWith("frost_"))
            {
                elites++;
                EnemyDeathAudio.ResetVoices();
                clock += 2.0;
                bool ok = EnemyDeathAudio.PlayElite(e.key);
                Check("elite " + e.key + " PlayElite resolves its clip (" + (EnemyDeathAudio.LastClip != null ? EnemyDeathAudio.LastClip.name : "none") + ")",
                      ok && EnemyDeathAudio.LastKey == e.key && EnemyDeathAudio.LastClip != null && EnemyDeathAudio.LastClip.name.StartsWith(e.key + "_"));
            }
        Check("12 Frost+Verdant+Ember elites in the catalog (" + elites + ")", elites == 12);

        // Clip-level sanity: no sample-clipping, voice caps hold with the new keys.
        bool clipped = false;
        string clippedName = "";
        foreach (var key in all)
        {
            for (int pass = 0; pass < 2; pass++)
                for (int n = 0; n < 3; n++)
                {
                    var c = pass == 0 ? EnemyDeathAudio.AuthoredClip(key, n) : EnemyDeathAudio.ScreamClip(key, n);
                    if (c == null) continue;
                    var data = new float[c.samples * c.channels];
                    c.GetData(data, 0);
                    int hot = 0;
                    for (int i = 0; i < data.Length; i++) if (Mathf.Abs(data[i]) >= .9995f) hot++;
                    if (hot > 2) { clipped = true; clippedName = c.name; }
                }
        }
        Check("no new clip hard-clips (>2 samples at full scale)" + (clipped ? " (" + clippedName + ")" : ""), !clipped);

        EnemyDeathAudio.ResetVoices();
        EnemyDeathAudio.ScreamChance = 1f;
        clock += 5.0;
        int maxV = 0, maxS = 0;
        var keys = all.ToArray();
        for (int i = 0; i < 60; i++)
        {
            clock += .01;
            EnemyDeathAudio.PlayAuthored(keys[(i / 2) % keys.Length], .68f);
            maxV = Mathf.Max(maxV, EnemyDeathAudio.ActiveVoices());
            maxS = Mathf.Max(maxS, EnemyDeathAudio.ActiveScreams());
        }
        Check("Frost/Verdant/Ember burst: voices <= " + EnemyDeathAudio.MaxVoices + " (" + maxV + "), screams <= " + EnemyDeathAudio.MaxScreamVoices + " (" + maxS + ")",
              maxV <= EnemyDeathAudio.MaxVoices && maxS <= EnemyDeathAudio.MaxScreamVoices && maxV > 1);
        EnemyDeathAudio.ScreamChance = .65f;
    }

    static void Borrow()
    {
        EnemyDeathAudio.ScreamBorrow.Clear();
        EnemyDeathAudio.ResetVoices();
        EnemyDeathAudio.ScreamChance = 1f;
        clock += 5.0;
        Check("ScreamBorrow is empty by default", EnemyDeathAudio.ScreamBorrow.Count == 0);
        EnemyDeathAudio.PlayAuthored("ember_mine", .68f);
        Check("unborrowed mine: no scream", EnemyDeathAudio.Screams == 0);
        try
        {
            EnemyDeathAudio.ScreamBorrow["ember_mine"] = new EnemyDeathAudio.Borrow("ember_fighter_1", 1.4f);
            clock += 2.0;
            EnemyDeathAudio.PlayAuthored("ember_mine", .68f);
            var s = EnemyDeathAudio.LastScream;
            Check("borrowing mine plays the donor's scream (" + (s != null ? s.name : "none") + ")",
                  EnemyDeathAudio.Screams == 1 && s != null && s.name.StartsWith("ember_fighter_1_scream_"));
            Check("... at the borrowed pitch (" + EnemyDeathAudio.LastScreamPitch + ")", Mathf.Abs(EnemyDeathAudio.LastScreamPitch - 1.4f) <= 1.4f * EnemyDeathAudio.PitchJitter + 1e-4f);
            float expected = EnemyDeathAudio.LastVolume * EnemyDeathAudio.ScreamVolume * EnemyDeathAudio.BorrowVolume;
            Check("... at lower volume than a native scream (" + EnemyDeathAudio.LastScreamVolume + ")",
                  Mathf.Abs(EnemyDeathAudio.LastScreamVolume - expected) < 1e-4f && EnemyDeathAudio.LastScreamVolume < EnemyDeathAudio.LastVolume * EnemyDeathAudio.ScreamVolume);
            // a native screamer never borrows
            EnemyDeathAudio.ScreamBorrow["ember_fighter_2"] = new EnemyDeathAudio.Borrow("ember_fighter_1", 2f);
            clock += 2.0;
            EnemyDeathAudio.PlayAuthored("ember_fighter_2", .68f);
            Check("a key with its own screams ignores its borrow entry", EnemyDeathAudio.LastScream.name.StartsWith("ember_fighter_2_scream_"));
            // donor without screams: silent
            EnemyDeathAudio.ScreamBorrow["ember_mine"] = new EnemyDeathAudio.Borrow("ember_big", 1.4f);
            int s0 = EnemyDeathAudio.Screams;
            clock += 2.0;
            EnemyDeathAudio.PlayAuthored("ember_mine", .68f);
            Check("a donor with no screams yields none", EnemyDeathAudio.Screams == s0);
        }
        finally { EnemyDeathAudio.ScreamBorrow.Clear(); EnemyDeathAudio.ScreamChance = .65f; }
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
        bool ok = guids.Length >= 255;
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
