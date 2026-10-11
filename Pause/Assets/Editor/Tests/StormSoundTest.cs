using UnityEngine;

// Storm's sound set: all 17 Storm keys (11 roster, 5 elites, plus the boss) resolve death clips through EnemyDeathAudio.DeathAlias
// (other worlds' clips, pitched, under the authored storm_crack_ layer), the pilots and elites borrow screams, a mote / drone / whale /
// mine / rock borrows none, Storm strikes fall back to Tide's thunder, the authored attack cues (Resources/Audio/Attack/Storm) load,
// nothing leaks into the other worlds, and no authored clip is a beep (spectral check on the samples).
//
//   scripts/unity-batch.sh -executeMethod AllTests.RunSuites -suites StormSoundTest
public static class StormSoundTest
{
    static int fails;
    static void Check(string what, bool ok)
    {
        Debug.Log((ok ? "[SSD] PASS  " : "[SSD] FAIL  ") + what);
        if (!ok) fails++;
    }

    public static void Run() { TestHarness.Exit(Execute()); }

    static double clock;
    static readonly string[] Cues = { "arc", "gust", "hail" };
    static readonly string[] Roster =
    {
        "storm_rock_hail", "storm_rock_ore", "storm_rock_slag", "storm_rock_islet", "storm_mine", "storm_big",
        "storm_fighter_1", "storm_fighter_2", "storm_fighter_3", "storm_fighter_4", "storm_chaser", "storm_alien",
    };
    static readonly string[] Elites =
    {
        "storm_elite_skyhook_harrier", "storm_elite_refinery_tender", "storm_elite_rod_dancer", "storm_elite_anvil_cloud", "storm_elite_static_bastion",
    };
    static readonly string[] Screamers =
    {
        "storm_fighter_1", "storm_fighter_2", "storm_fighter_3", "storm_fighter_4",
        "storm_elite_skyhook_harrier", "storm_elite_refinery_tender", "storm_elite_rod_dancer", "storm_elite_anvil_cloud", "storm_elite_static_bastion",
    };

    public static int Execute()
    {
        fails = 0;
        bool sim = EnemyDeathAudio.Simulate, asim = AttackAudio.Simulate;
        var c1 = EnemyDeathAudio.Clock; var c2 = AttackAudio.Clock;
        try
        {
            EnemyDeathAudio.Simulate = true; EnemyDeathAudio.Clock = () => clock;
            AttackAudio.Simulate = true; AttackAudio.Clock = () => clock;
            EnemyDeathAudio.ClearCache(); AttackAudio.ClearCache();
            EnemyDeathAudio.ResetBorrows();
            Deaths();
            Screams();
            Attacks();
            NoBeeps();
        }
        finally
        {
            EnemyDeathAudio.Simulate = sim; EnemyDeathAudio.Clock = c1; AttackAudio.Simulate = asim; AttackAudio.Clock = c2;
            EnemyDeathAudio.ResetBorrows();
            EnemyDeathAudio.ClearCache(); AttackAudio.ClearCache();
        }
        Debug.Log("[SSD] failures: " + fails);
        return fails;
    }

    static void Deaths()
    {
        var bad = new System.Collections.Generic.List<string>();
        foreach (var list in new[] { Roster, Elites })
            foreach (var k in list)
            {
                string donor = EnemyDeathAudio.DonorOf(k);
                bool ok = donor != null && !donor.StartsWith("storm_") && EnemyDeathAudio.Variants(donor) > 0 && EnemyDeathAudio.DonorOf(donor) == null &&
                          EnemyDeathAudio.Variants(k) >= 3 && EnemyDeathAudio.Variants(k) == EnemyDeathAudio.Variants(donor) &&
                          EnemyDeathAudio.LayerOf(k) == EnemyDeathAudio.StormLayer;
                if (!ok) bad.Add(k);
            }
        Check("all 12 Storm roster keys and 5 elites resolve donor death clips under the storm layer (" + (bad.Count == 0 ? "ok" : string.Join(", ", bad)) + ")", bad.Count == 0);
        bool real = true;
        foreach (var r in EnemyDeathAudio.DeathAlias) real &= EnemyDeathAudio.DonorOf(r[1]) == null;
        Check("no alias points at another alias (a donor is always a real authored key)", real);
        Check("the Storm boss death cue resolves (boss_storm -> boss_space, key from artKey 'Storm')",
              EnemyDeathAudio.BossKey("Storm") == "boss_storm" && EnemyDeathAudio.DonorOf("boss_storm") == "boss_space" && EnemyDeathAudio.Variants("boss_storm") >= 2);
        // a roster / elite def, if the roster / elite agents' defs are merged, must be covered by an alias
        int defs = 0; var missing = new System.Collections.Generic.List<string>();
        foreach (var d in EnemyRoster.All) if (d.key.StartsWith("storm_")) { defs++; if (EnemyDeathAudio.Variants(d.key) < 3) missing.Add(d.key); }
        Check("every storm_ roster def present (" + defs + ") has death clips" + (missing.Count > 0 ? " (missing: " + string.Join(", ", missing) + ")" : ""), missing.Count == 0);
        EnemyDeathAudio.ResetVoices();
        clock += 5;
        bool played = EnemyDeathAudio.PlayAuthored("storm_big", .8f);
        Check("a Storm death plays its donor clip, and the crack layer takes a voice (pitch " + EnemyDeathAudio.DonorOf("storm_big") + ")",
              played && EnemyDeathAudio.Played == 1 && EnemyDeathAudio.LastClip != null && EnemyDeathAudio.ActiveScreams() >= 1);
        Check("the crack layer storm_crack_0..2 exists", Resources.Load<AudioClip>("Audio/EnemyDeath/storm_crack_0") != null &&
              Resources.Load<AudioClip>("Audio/EnemyDeath/storm_crack_1") != null && Resources.Load<AudioClip>("Audio/EnemyDeath/storm_crack_2") != null);
        Check("Tide keeps its own wet layer and other worlds are not aliased",
              EnemyDeathAudio.LayerOf("tide_big") == EnemyDeathAudio.WetLayer && EnemyDeathAudio.DonorOf("ember_big") == null && EnemyDeathAudio.DonorOf("space_mine") == null);
    }

    static void Screams()
    {
        bool all = true;
        foreach (var k in Screamers)
        {
            EnemyDeathAudio.Borrow b;
            bool ok = EnemyDeathAudio.ScreamBorrow.TryGetValue(k, out b) && EnemyDeathAudio.ScreamVariants(b.donor) > 0 && !EnemyDeathAudio.ScreamBorrow.ContainsKey(b.donor) &&
                      b.pitch >= .8f && b.pitch <= 1.6f && EnemyDeathAudio.ScreamVariants(k) == 0;
            if (!ok) Debug.Log("[SSD] bad borrow for " + k);
            all &= ok;
        }
        Check("the 4 pilots and 5 elites borrow a scream from a donor that owns one (" + Screamers.Length + ")", all);
        bool none = true;
        foreach (var k in new[] { "storm_rock_hail", "storm_rock_ore", "storm_rock_slag", "storm_rock_islet", "storm_mine", "storm_big", "storm_chaser", "storm_alien" })
            none &= !EnemyDeathAudio.ScreamBorrow.ContainsKey(k);
        Check("the mote, rotor drone, whale, mine and rocks have no scream", none);
        EnemyDeathAudio.ScreamChance = 1f;
        try
        {
            EnemyDeathAudio.ResetVoices(); clock += 5;
            EnemyDeathAudio.PlayAuthored("storm_elite_rod_dancer", .8f);
            Check("an elite death layers its borrowed scream", EnemyDeathAudio.LastScream != null && EnemyDeathAudio.LastScream.name.StartsWith("ember_elite_ash_wraith_scream_"));
        }
        finally { EnemyDeathAudio.ScreamChance = .65f; }
    }

    static void Attacks()
    {
        bool all = true;
        foreach (var c in Cues) all &= AttackAudio.Variants(5, c) == 3;
        Check("Storm has 3 variants of each authored attack cue (" + string.Join(", ", Cues) + ")", all);
        Check("a Storm strike falls back to Tide's thunder (" + AttackAudio.Variants(5, "strike") + " variants)", AttackAudio.Variants(5, "strike") == 3 && AttackAudio.Clip(5, "strike", 0) == AttackAudio.Clip(4, "strike", 0));
        bool silent = true;
        for (int w = 0; w < 4; w++) foreach (var c in Cues) silent &= AttackAudio.Variants(w, c) == 0;
        silent &= AttackAudio.Variants(4, "arc") == 0 && AttackAudio.Variants(4, "gust") == 0 && AttackAudio.Variants(4, "hail") == 0;
        silent &= AttackAudio.Variants(2, "strike") == 0 && AttackAudio.Variants(3, "strike") == 0;
        Check("the other five worlds' attacks stay as they were (no Storm cue leaks)", silent);
        Check("labels map to cues (arc, gust, hail, strike; Tide's labels unchanged)",
              AttackAudio.CueOf("arc") == "arc" && AttackAudio.CueOf("chain lightning") == "arc" && AttackAudio.CueOf("gust wave") == "gust" &&
              AttackAudio.CueOf("hail") == "hail" && AttackAudio.CueOf("strike") == "strike" && AttackAudio.CueOf("surf wave") == "wave" && AttackAudio.CueOf("vine lash") == "");
        AttackAudio.ClearCache();
        clock += 5;
        Check("a Storm cue plays and a repeat inside the interval is folded in",
              AttackAudio.Play(5, "arc") && AttackAudio.Played == 1 && AttackAudio.Play(5, "arc") && AttackAudio.Played == 1 && AttackAudio.Dropped == 1);
        clock += 5;
        Check("a Storm strike plays Tide's thunder", AttackAudio.Play(5, "strike") && AttackAudio.LastCue == "strike");
        TestHarness.Sandbox sb = null;
        try
        {
            sb = new TestHarness.Sandbox();
            AttackPools.ClearAll();
            AttackAudio.ClearCache();
            clock += 5;
            var s = AttackStrike.Arm(StrikeSpec.Standard(5), 0f, 0f, 1f, null);
            if (s != null) s.Ignite();
            Check("an AttackStrike in Storm goes live with a thunder cue (" + AttackAudio.LastCue + ")", s != null && AttackAudio.Played == 1 && AttackAudio.LastCue == "strike");
        }
        finally { AttackPools.ClearAll(); if (sb != null) sb.Dispose(); }
    }

    // Every authored clip is noise and impact: no sustained pure tone (a beep) anywhere in the samples.
    static void NoBeeps()
    {
        int bad = 0, n = 0;
        foreach (var c in Cues) for (int v = 0; v < 3; v++) { n++; if (Beepy(AttackAudio.Clip(5, c, v))) bad++; }
        for (int v = 0; v < 3; v++) { n++; if (Beepy(Resources.Load<AudioClip>("Audio/EnemyDeath/storm_crack_" + v))) bad++; }
        Check("none of the " + n + " authored Storm clips holds a pure tone over 80 ms (" + bad + " beepy)", bad == 0 && n == 12);
    }

    static bool Beepy(AudioClip clip)
    {
        if (clip == null) return true;
        var data = new float[clip.samples * clip.channels];
        if (!clip.GetData(data, 0)) return true;
        int size = 882, hop = 882;
        int run = 0, best = 0;
        double peakE = 0;
        for (int i = 0; i + size <= data.Length; i += hop)
        {
            double e = 0; for (int j = 0; j < size; j++) e += data[i + j] * data[i + j];
            if (e > peakE) peakE = e;
        }
        for (int i = 0; i + size <= data.Length; i += hop)
        {
            double e = 0; for (int j = 0; j < size; j++) e += data[i + j] * data[i + j];
            if (e < peakE * 1e-3) { run = 0; continue; }
            // goertzel-free tonality: autocorrelation peak of the window over lags 20..400 (110 Hz..2.2 kHz)
            double r0 = e, bestR = 0;
            for (int lag = 20; lag < 400; lag += 3)
            {
                double r = 0; for (int j = 0; j + lag < size; j++) r += data[i + j] * data[i + j + lag];
                if (r > bestR) bestR = r;
            }
            if (bestR / r0 > .85) { run++; best = Mathf.Max(best, run); } else run = 0;
        }
        return best * hop / 44100f > .08f;
    }
}
