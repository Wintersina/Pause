using UnityEngine;

// Storm's sound set: the 4 Storm rocks resolve death clips through EnemyDeathAudio.DeathAlias (other worlds' rock clips, pitched, under
// the authored storm_crack_ layer), every other Storm key (roster, 5 elites, the boss) plays a shared death category, Storm strikes fall back to Tide's thunder, the authored attack cues (Resources/Audio/Attack/Storm) load,
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
            Deaths();
            Attacks();
            NoBeeps();
        }
        finally
        {
            EnemyDeathAudio.Simulate = sim; EnemyDeathAudio.Clock = c1; AttackAudio.Simulate = asim; AttackAudio.Clock = c2;
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
                bool rock = k.Contains("_rock_");
                string donor = EnemyDeathAudio.DonorOf(k);
                bool ok = rock
                    ? donor != null && !donor.StartsWith("storm_") && EnemyDeathAudio.Variants(donor) > 0 && EnemyDeathAudio.DonorOf(donor) == null &&
                      EnemyDeathAudio.Variants(k) == EnemyDeathAudio.Variants(donor) && EnemyDeathAudio.LayerOf(k) == EnemyDeathAudio.StormLayer
                    : donor == null && EnemyDeathAudio.CategoryOf(k) != "" && EnemyDeathAudio.Variants(k) >= 1;
                if (!ok) bad.Add(k);
            }
        Check("the 4 Storm rocks alias rock donors under the storm layer; the other 8 roster keys and 5 elites play shared clips (" + (bad.Count == 0 ? "ok" : string.Join(", ", bad)) + ")", bad.Count == 0);
        Check("the Storm boss death cue resolves (boss_storm -> shared roar, key from artKey 'Storm')",
              EnemyDeathAudio.BossKey("Storm") == "boss_storm" && EnemyDeathAudio.CategoryOf("boss_storm") == EnemyDeathAudio.Roar && EnemyDeathAudio.Variants("boss_storm") >= 1);
        int defs = 0; var missing = new System.Collections.Generic.List<string>();
        foreach (var d in EnemyRoster.All) if (d.key.StartsWith("storm_")) { defs++; if (EnemyDeathAudio.Variants(d.key) < 1) missing.Add(d.key); }
        Check("every storm_ roster def present (" + defs + ") has death clips" + (missing.Count > 0 ? " (missing: " + string.Join(", ", missing) + ")" : ""), missing.Count == 0);
        EnemyDeathAudio.ResetVoices();
        clock += 5;
        bool played = EnemyDeathAudio.PlayAuthored("storm_rock_slag", .8f);
        Check("a Storm rock death plays its donor clip, and the crack layer takes a voice",
              played && EnemyDeathAudio.Played == 1 && EnemyDeathAudio.LastClip != null && EnemyDeathAudio.ActiveLayers() >= 1);
        EnemyDeathAudio.ResetVoices();
        clock += 5;
        EnemyDeathAudio.PlayAuthored("storm_big", .8f);
        Check("a Storm big plays the shared roar at its pitch, no layer (" + EnemyDeathAudio.LastPitch.ToString("0.00") + ")",
              EnemyDeathAudio.LastClip != null && EnemyDeathAudio.LastClip.name == "shared_roar_0" && EnemyDeathAudio.ActiveLayers() == 0 &&
              Mathf.Abs(EnemyDeathAudio.LastPitch - .75f) <= .75f * EnemyDeathAudio.PitchJitter + 1e-4f);
        Check("the crack layer storm_crack_0..2 exists", Resources.Load<AudioClip>("Audio/EnemyDeath/storm_crack_0") != null &&
              Resources.Load<AudioClip>("Audio/EnemyDeath/storm_crack_1") != null && Resources.Load<AudioClip>("Audio/EnemyDeath/storm_crack_2") != null);
        Check("Tide keeps its own wet layer for its rocks and the other worlds' creatures are not aliased",
              EnemyDeathAudio.LayerOf("tide_rock_brain") == EnemyDeathAudio.WetLayer && EnemyDeathAudio.DonorOf("ember_big") == null && EnemyDeathAudio.DonorOf("space_mine") == null);
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
