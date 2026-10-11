using UnityEngine;

// Desert's sound set (docs/desert-wire.md): the 4 Desert rocks alias donor rock clips (EnemyDeathAudio.DeathAlias, pitched, no authored layer:
// the plan's sand slump is a later sound job); every other Desert key (8 roster, 6 elites, the boss) plays a shared death category
// (SharedTable). Desert's strike / gust / hail attack cues fall
// back to Storm's reused clips (strike through Storm to Tide's thunder), nothing leaks into the other worlds, and nothing new is a beep, a coin
// or an ambience (no Desert file exists under Audio at all; every clip is a donor's).
//
//   scripts/unity-batch.sh -executeMethod AllTests.RunSuites -suites DesertSoundTest
public static class DesertSoundTest
{
    static int fails;
    static void Check(string what, bool ok)
    {
        Debug.Log((ok ? "[DSD] PASS  " : "[DSD] FAIL  ") + what);
        if (!ok) fails++;
    }

    public static void Run() { TestHarness.Exit(Execute()); }

    static double clock;
    static readonly string[] Roster =
    {
        "desert_rock_dune", "desert_rock_spire", "desert_rock_husk", "desert_rock_slab", "desert_mine", "desert_big",
        "desert_fighter_1", "desert_fighter_2", "desert_fighter_3", "desert_fighter_4", "desert_chaser", "desert_alien",
    };
    static readonly string[] Elites =
    {
        "desert_elite_dune_stalker", "desert_elite_caravan_hauler", "desert_elite_lens_matriarch", "desert_elite_glass_cannon",
        "desert_elite_scorpion_walker", "desert_elite_mirror_bastion",
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
        }
        finally
        {
            EnemyDeathAudio.Simulate = sim; EnemyDeathAudio.Clock = c1; AttackAudio.Simulate = asim; AttackAudio.Clock = c2;
            EnemyDeathAudio.ClearCache(); AttackAudio.ClearCache();
        }
        Debug.Log("[DSD] failures: " + fails);
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
                    ? donor != null && !donor.StartsWith("desert_") && EnemyDeathAudio.Variants(donor) > 0 && EnemyDeathAudio.DonorOf(donor) == null &&
                      EnemyDeathAudio.Variants(k) == EnemyDeathAudio.Variants(donor) && EnemyDeathAudio.LayerOf(k) == ""
                    : donor == null && EnemyDeathAudio.CategoryOf(k) != "" && EnemyDeathAudio.Variants(k) >= 1 && EnemyDeathAudio.IsDesert(k);
                if (!ok) bad.Add(k);
            }
        Check("the 4 Desert rocks alias rock donors with no layer; the other 8 roster keys and 6 elites play shared clips (" + (bad.Count == 0 ? "ok" : string.Join(", ", bad)) + ")", bad.Count == 0);
        Check("the Dune-Eater death cue resolves (boss_desert -> shared roar, key from artKey 'Desert')",
              EnemyDeathAudio.BossKey("Desert") == "boss_desert" && EnemyDeathAudio.CategoryOf("boss_desert") == EnemyDeathAudio.Roar && EnemyDeathAudio.Variants("boss_desert") >= 1);
        int defs = 0; var missing = new System.Collections.Generic.List<string>();
        foreach (var d in EnemyRoster.All) if (d.key.StartsWith("desert_")) { defs++; if (EnemyDeathAudio.Variants(d.key) < 1) missing.Add(d.key); }
        Check("every desert_ roster def present (" + defs + ") has death clips" + (missing.Count > 0 ? " (missing: " + string.Join(", ", missing) + ")" : ""), defs == 12 && missing.Count == 0);
        EnemyDeathAudio.ResetVoices();
        clock += 5;
        bool played = EnemyDeathAudio.PlayAuthored("desert_big", .8f);
        Check("a Desert big plays the shared roar with no layer voice (pitch " + EnemyDeathAudio.LastPitch.ToString("0.00") + ")",
              played && EnemyDeathAudio.Played == 1 && EnemyDeathAudio.LastClip != null && EnemyDeathAudio.LastClip.name == "shared_roar_0" && EnemyDeathAudio.ActiveLayers() == 0);
        EnemyDeathAudio.ResetVoices();
        clock += 5;
        EnemyDeathAudio.PlayAuthored("desert_rock_dune", .8f);
        Check("a Desert rock plays its donor clip with no layer voice", EnemyDeathAudio.Played == 1 && EnemyDeathAudio.LastClip != null && EnemyDeathAudio.ActiveLayers() == 0);
        Check("Storm and Tide keep their layers for their rocks, and the other worlds' creatures are not aliased",
              EnemyDeathAudio.LayerOf("storm_rock_hail") == EnemyDeathAudio.StormLayer && EnemyDeathAudio.LayerOf("tide_rock_brain") == EnemyDeathAudio.WetLayer &&
              EnemyDeathAudio.DonorOf("ember_big") == null && EnemyDeathAudio.DonorOf("storm_big") == null);
        Check("no Desert audio files were added (only the shared clips and rock donors): none under Audio/EnemyDeath/desert_* or Audio/Attack/Desert",
              Resources.Load<AudioClip>("Audio/EnemyDeath/desert_big_0") == null && Resources.Load<AudioClip>("Audio/Attack/Desert/strike_0") == null &&
              Resources.Load<AudioClip>("Audio/Attack/Desert/gust_0") == null);
    }

    static void Attacks()
    {
        Check("Desert's strike, gust and hail cues play Storm's clips (strike: Tide's thunder through Storm)",
              AttackAudio.Variants(6, "gust") == 3 && AttackAudio.Clip(6, "gust", 0) == AttackAudio.Clip(5, "gust", 0) &&
              AttackAudio.Variants(6, "hail") == 3 && AttackAudio.Clip(6, "hail", 0) == AttackAudio.Clip(5, "hail", 0) &&
              AttackAudio.Variants(6, "strike") == 3 && AttackAudio.Clip(6, "strike", 0) == AttackAudio.Clip(4, "strike", 0));
        Check("the other cues (wave, jet, lash, arc, burst, splash) stay silent in Desert", AttackAudio.Variants(6, "wave") == 0 && AttackAudio.Variants(6, "jet") == 0 &&
              AttackAudio.Variants(6, "lash") == 0 && AttackAudio.Variants(6, "arc") == 0);
        bool noLeak = true;
        for (int w = 0; w < 4; w++) foreach (var c in new[] { "gust", "hail", "strike" }) noLeak &= AttackAudio.Variants(w, c) == 0;
        noLeak &= AttackAudio.Variants(4, "gust") == 0 && AttackAudio.Variants(5, "arc") == 3;
        Check("no Desert fallback leaks into the other worlds, and Storm keeps its own cues", noLeak);
        AttackAudio.ClearCache();
        clock += 5;
        Check("a Desert gust plays", AttackAudio.Play(6, "gust") && AttackAudio.Played == 1);
    }
}
