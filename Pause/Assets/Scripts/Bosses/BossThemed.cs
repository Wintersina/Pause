using UnityEngine;

// THE THEMED BOSS TABLES (docs/world-attacks-design.md section 7; plan phase 1g).
//
// Each boss goes from 3 to 5 attacks: today's three plus two new ones in the world's material, sorted by
// minPhase so the unlocked attacks are a prefix:
//
//   phase 1  attack 0 + the new signature              (2)
//   phase 2  + attack 1                                (3)
//   phase 3  + attack 2 + the new final attack         (5, rotating)
//
//   Void Archon          chin cannon, ion arcs  | core burst | pod lasers, scan line
//   Hoarfrost Leviathan  icicle spray, icicle drop | glare beams | blowhole hail, cold blast
//   Bloom Queen          stinger thorns, vine lash | spore bloom | acid cannons, trunk toss     (needs Lash + Roll: no table yet)
//   Cinder Drake         fire breath, flame sweep | furnace slugs | brow laser, eruption columns
//   Iron Kraken          port cluster, thunder strike | beak jet | starboard cluster, surf wave
//
// A table only fights when BossDef.themedAttacks is on (default off); the default tables are untouched. The
// new attacks share the existing attacks' tell poses: each lights the part it fires from (the Archon's pods, the
// Leviathan's jaw / crown, the Drake's jaw / furnace, the Kraken's beak), and the expanded atlases (Space, Tide)
// show that pose's own per-part tell stages (BossArt.TellFrame).
public static class BossThemed
{
    // The themed table for `boss`, or null when a primitive it needs is not built.
    public static BossAttack[] TableFor(BossDef boss)
    {
        var d = boss.DefaultAttacks;
        int w = BossEmitters.World(boss);
        switch (boss.artKey)
        {
            case "Space":   // rail slugs (Streak) are the chin cannon unchanged for now
                return new[] { d[0], IonArcs(w), d[1], d[2], ScanLine(w) };
            case "Frost":
                return new[] { d[0], IcicleDrop(w), d[1], d[2], ColdBlast(w) };
            case "Ember":
                return new[] { d[0], FlameSweep(w), d[1], d[2], Eruption(w) };
            case "Tide":    // the thunder arcs / ink skins are art; the beak spit, ink and tentacle attacks stay the placeholders
                return new[] { d[0], ThunderStrike(w), d[1], d[2], SurfWave(w) };
            default:        // Verdant: vine lash (Lash) and trunk toss (Roll) are not built
                return null;
        }
    }

    // ---- Space: Jet Lance pulses from both pods; a neon Wave scan line ----------------------------------------

    static BossAttack IonArcs(int w)
    {
        var jet = JetSpec.Lance(w);
        jet.length = 4.2f; jet.maxLength = 7.5f; jet.baseHalf = .14f; jet.tipHalf = .14f; jet.liveSeconds = .5f;
        return new BossAttack
        {
            name = "ion arcs", kind = BossAttackKind.Jet, tell = 2, tellSeconds = .9f, emitters = new[] { "PodL", "PodR" },
            jet = jet, aimSpreadX = 1f, volleys = 2, volleyGap = .9f, cooldown = 1.3f, minPhase = 1,
        };
    }

    static BossAttack ScanLine(int w)
    {
        var wave = WaveSpec.Scan(w);
        wave.gapOffset = 1f;
        return new BossAttack
        {
            name = "scan line", kind = BossAttackKind.Wave, tell = 2, tellSeconds = 1f, emitters = new[] { "PodL" },
            wave = wave, volleys = 1, cooldown = 1.4f, minPhase = 3,
        };
    }

    // ---- Frost: icicle drop (Strike), cold blast (Blast) -------------------------------------------------------

    static BossAttack IcicleDrop(int w)
    {
        return new BossAttack
        {
            name = "icicle drop", kind = BossAttackKind.Strike, tell = 0, tellSeconds = 1f, emitters = new[] { "Jaw" },
            strike = StrikeSpec.Standard(w), count = 3, spacing = 1.9f, cooldown = 1.2f, minPhase = 1,
        };
    }

    static BossAttack ColdBlast(int w)
    {
        // from the crown, two rings 1.1 s apart, the crack of the second on the other side. The Wide ring (32 bars) closes the wall
        // up to 7.5 u; the Leviathan's crown is 6-7 u above the ship's row.
        var blast = BlastSpec.Wide(w);
        blast.startRadius = 1.6f; blast.reach = 7.5f; blast.gapOffsetDeg = 40f;
        return new BossAttack
        {
            name = "cold blast", kind = BossAttackKind.Blast, tell = 2, tellSeconds = .9f, emitters = new[] { "Crown" },
            blast = blast, volleys = 2, volleyGap = 1.1f, cooldown = 1.4f, minPhase = 3,
        };
    }

    // ---- Ember: flame sweep (Jet), eruption columns (Strike) ---------------------------------------------------

    static BossAttack FlameSweep(int w)
    {
        // a cone from the jaw, 5 u long and reaching the ship's row (to 7.5), swept; the far end never moves faster than AttackJet.MaxTipSpeed
        var jet = JetSpec.Flame(w);
        jet.length = 5f; jet.maxLength = 7.5f; jet.baseHalf = .1f; jet.tipHalf = .5f; jet.sweepDeg = 30f; jet.centered = true;
        return new BossAttack
        {
            name = "flame sweep", kind = BossAttackKind.Jet, tell = 0, tellSeconds = 1f, emitters = new[] { "Jaw" },
            jet = jet, volleys = 1, cooldown = 1.3f, minPhase = 1,
        };
    }

    static BossAttack Eruption(int w)
    {
        return new BossAttack
        {
            name = "eruption columns", kind = BossAttackKind.Strike, tell = 1, tellSeconds = 1.1f, emitters = new[] { "Furnace" },
            strike = StrikeSpec.Standard(w), count = 3, spacing = 1.9f, cooldown = 1.3f, minPhase = 3,
        };
    }

    // ---- Tide: thunder strike (Strike), surf wave (Wave) -------------------------------------------------------

    static BossAttack ThunderStrike(int w)
    {
        return new BossAttack
        {
            name = "thunder strike", kind = BossAttackKind.Strike, tell = 0, tellSeconds = 1f, emitters = new[] { "Beak" },
            strike = StrikeSpec.Standard(w), count = 3, spacing = 1.9f, cooldown = 1.2f, minPhase = 1,
        };
    }

    static BossAttack SurfWave(int w)
    {
        var wave = WaveSpec.Surf(w);
        wave.gapOffset = .8f;
        return new BossAttack
        {
            name = "surf wave", kind = BossAttackKind.Wave, tell = 0, tellSeconds = 1.1f, emitters = new[] { "Beak" },
            wave = wave, volleys = 2, volleyGap = 1.4f, cooldown = 1.4f, minPhase = 3,
        };
    }
}
