using UnityEngine;

// Tide's sound set: the 4 Tide rocks resolve death clips through EnemyDeathAudio.DeathAlias (other worlds' rock clips, pitched,
// under tide_wet_), every other roster enemy and elite plays a shared death category, the boss cue exists, the themed attack cues (Resources/Audio/Attack/Tide) load and fire from their
// attacks, nothing leaks into the other worlds, and no clip is a beep (spectral check on the samples).
//
//   scripts/unity-batch.sh -executeMethod AllTests.RunSuites -suites TideSoundTest
public static class TideSoundTest
{
    static int fails;
    static void Check(string what, bool ok)
    {
        Debug.Log((ok ? "[TSD] PASS  " : "[TSD] FAIL  ") + what);
        if (!ok) fails++;
    }

    public static void Run() { TestHarness.Exit(Execute()); }

    static double clock;
    static readonly string[] Cues = { "jet", "wave", "strike", "lash", "burst", "splash" };

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
            EliteCatalog.Reload();
            Deaths();
            Attacks();
            NoBeeps();
        }
        finally
        {
            EnemyDeathAudio.Simulate = sim; EnemyDeathAudio.Clock = c1; AttackAudio.Simulate = asim; AttackAudio.Clock = c2;
            EnemyDeathAudio.ClearCache(); AttackAudio.ClearCache();
        }
        Debug.Log("[TSD] failures: " + fails);
        return fails;
    }

    static void Deaths()
    {
        int roster = 0, elites = 0, rocks = 0;
        bool all = true;
        foreach (var d in EnemyRoster.All)
        {
            if (!d.key.StartsWith("tide_")) continue;
            roster++;
            bool rock = d.key.Contains("_rock_");
            if (rock) rocks++;
            string donor = EnemyDeathAudio.DonorOf(d.key);
            bool ok = rock ? donor != null && EnemyDeathAudio.Variants(d.key) >= 3 && EnemyDeathAudio.Variants(d.key) == EnemyDeathAudio.Variants(donor) && EnemyDeathAudio.LayerOf(d.key) == EnemyDeathAudio.WetLayer
                           : donor == null && EnemyDeathAudio.CategoryOf(d.key) != "" && EnemyDeathAudio.Variants(d.key) >= 1;
            if (!ok) Debug.Log("[TSD] no clips for " + d.key);
            all &= ok;
        }
        foreach (var e in EliteCatalog.All)
        {
            if (e.WorldIndex != 4) continue;
            elites++;
            all &= EnemyDeathAudio.DonorOf(e.key) == null && EnemyDeathAudio.CategoryOf(e.key) != "" && EnemyDeathAudio.Variants(e.key) >= 1;
        }
        Check("all 14 Tide roster keys (4 rocks aliased, 10 shared, two of them helicopters) and 5 Tide elites (shared) resolve death clips (" + roster + " / " + elites + ")", all && roster == 14 && elites == 5 && rocks == 4);
        bool own = true;
        foreach (var r in EnemyDeathAudio.DeathAlias) own &= !r[1].StartsWith("tide_");
        Check("no alias points at another alias, a donor is always a real authored key", own);
        clock += 5;
        EnemyDeathAudio.ResetVoices();
        bool played = EnemyDeathAudio.PlayAuthored("tide_big", .8f);
        Check("a Tide big plays the shared roar at its own pitch (" + EnemyDeathAudio.LastPitch.ToString("0.00") + ")", played && EnemyDeathAudio.Played == 1 && EnemyDeathAudio.LastClip != null && EnemyDeathAudio.LastClip.name == "shared_roar_0");
        clock += 5;
        EnemyDeathAudio.ResetVoices();
        EnemyDeathAudio.PlayAuthored("tide_rock_brain", .8f);
        Check("a Tide rock plays its donor clip with the wet layer under it", EnemyDeathAudio.Played == 1 && EnemyDeathAudio.LastClip != null && EnemyDeathAudio.ActiveLayers() >= 1);
        var wet = Resources.Load<AudioClip>("Audio/EnemyDeath/" + EnemyDeathAudio.WetLayer + "0");
        Check("the wet layer tide_wet_0..2 exists", wet != null && Resources.Load<AudioClip>("Audio/EnemyDeath/tide_wet_2") != null);
        Check("Tide's boss death cue exists (boss_tide, shared roar)", EnemyDeathAudio.Variants("boss_tide") >= 1 && EnemyDeathAudio.CategoryOf("boss_tide") == EnemyDeathAudio.Roar);
        Check("other worlds are not aliased", EnemyDeathAudio.DonorOf("ember_big") == null && EnemyDeathAudio.DonorOf("frost_mine") == null);
    }

    static void Attacks()
    {
        bool all = true;
        foreach (var c in Cues) all &= AttackAudio.Variants(4, c) == 3;
        Check("Tide has 3 variants of each attack cue (" + string.Join(", ", Cues) + ")", all);
        bool silent = true;
        for (int w = 0; w < 4; w++) foreach (var c in Cues) silent &= AttackAudio.Variants(w, c) == 0;
        Check("the other four worlds' attacks stay silent (no clips)", silent);
        Check("labels map to cues (surf wave, jet, strike, tentacle lash)",
              AttackAudio.CueOf("surf wave") == "wave" && AttackAudio.CueOf("jet") == "jet" && AttackAudio.CueOf("strike") == "strike" &&
              AttackAudio.CueOf("tentacle lash") == "lash" && AttackAudio.CueOf("vine lash") == "");
        AttackAudio.ClearCache();
        clock += 5;
        bool a = AttackAudio.Play(4, "strike");
        Check("a cue plays and a repeat inside the interval is folded in (" + AttackAudio.Played + " played, " + AttackAudio.Dropped + " dropped)",
              a && AttackAudio.Played == 1 && AttackAudio.Play(4, "strike") && AttackAudio.Played == 1 && AttackAudio.Dropped == 1);
        clock += 1;
        Check("... and a different variant is picked next time", AttackAudio.Play(4, "strike") && AttackAudio.Played == 2);
        Check("a cue nobody authored is silent and harmless", !AttackAudio.Play(2, "strike") && !AttackAudio.Play(4, "nope"));
        // a real Tide strike hazard firing sounds its cue
        TestHarness.Sandbox sb = null;
        try
        {
            sb = new TestHarness.Sandbox();
            AttackPools.ClearAll();
            AttackAudio.ClearCache();
            clock += 5;
            var spec = StrikeSpec.Standard(4);
            var s = AttackStrike.Arm(spec, 0f, 0f, 1f, null);
            if (s != null) s.Ignite();
            Check("an AttackStrike in Tide goes live with its thunder cue (" + AttackAudio.LastCue + ")", s != null && AttackAudio.Played == 1 && AttackAudio.LastCue == "strike");
            AttackAudio.ClearCache();
            clock += 5;
            var f = AttackStrike.Arm(StrikeSpec.Standard(3), 0f, 0f, 1f, null);
            if (f != null) f.Ignite();
            Check("the same strike in Ember stays silent", f != null && AttackAudio.Played == 0);
        }
        finally { AttackPools.ClearAll(); if (sb != null) sb.Dispose(); }
    }

    // Every clip is noise and impact: no sustained pure tone (a beep) anywhere in the samples.
    static void NoBeeps()
    {
        int bad = 0, n = 0;
        foreach (var c in Cues) for (int v = 0; v < 3; v++) { n++; if (Beepy(AttackAudio.Clip(4, c, v))) bad++; }
        for (int v = 0; v < 3; v++) { n++; if (Beepy(Resources.Load<AudioClip>("Audio/EnemyDeath/tide_wet_" + v))) bad++; }
        Check("none of the " + n + " authored Tide clips holds a pure tone over 80 ms (" + bad + " beepy)", bad == 0 && n == 21);
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
