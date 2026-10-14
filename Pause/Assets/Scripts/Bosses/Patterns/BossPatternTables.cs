using System.Collections.Generic;
using UnityEngine;

// THE BOSS ATTACK TABLES, one place (docs/boss-attack-overhaul.md): every boss's rotation as data. Tune a boss here and
// in BossLadder.Targets; nothing else holds a boss's attack numbers.
//
// Each boss has 5-7 slots, sorted by minPhase (the unlocked ones are a prefix: phase 1 = 2 slots, phase 2 = 3 or 4,
// phase 3 = all). A slot is a bullet-hell PATTERN (streams, BossPattern) or a LEGACY slot: an area hazard or laser the
// game already has (looked up by name in the boss's old tables and kept as it was -- a Beam, a vine lash, a trunk toss).
//
// Signatures: every boss owns the mechanics no other boss uses (a stream kind or hazard kind), and only the plain
// basics (aimed volleys, a fan) are shared, and only in phase 1:
//
//   Void Archon          spiral (pulsar, mirrored sweeps), ion lance
//   Hoarfrost Leviathan  ring with a gap (cold snap), bouncing ice (ricochet)
//   Bloom Queen          homing seeds, vine lash, trunk toss
//   Cinder Drake         delayed-burst bombs, flame sweep, eruption columns
//   Iron Kraken          curtains with a drifting gap (tidal wall), surf wave
//   Tempest Engine       rain from the top, lattice grids
//   Dune-Eater           mine fields, closing pincer
public sealed class PatternSlot
{
    public string name;
    public BossPattern pattern;        // a bullet-hell pattern, or
    public string legacy;              // the name of an existing attack (Beam / Jet / Strike / Lash / Roll / Wave) of the boss's old tables
    public string legacyKind;          // "beam", "jet", ... for the signature check
    public float est;                  // legacy: seconds the attack lasts after its tell (live beam, sweeps)
    public float tell = .9f;           // seconds of telegraph (the boss holds its tell pose)
    public float cooldown = 1.3f;
    public int minPhase = 1;
    public int pose;                   // which drawn tell pose (0..2)
    public bool fireFrame;
    public float Seconds => tell + (pattern != null ? pattern.Duration : est);
}

public static partial class BossPatternTables
{
    public const int Bosses = 7;
    static PatternSlot[][] all;

    // Rotation of the boss of world `world` (index-aligned with WorldManager.Worlds).
    public static PatternSlot[] For(int world)
    {
        if (all == null) Build();
        return all[Mathf.Clamp(world, 0, Bosses - 1)];
    }

    static readonly string[] None = new string[0];

    static string[] E(params string[] names) => names;

    static BossPattern P(string name, params PatternStream[] streams) => new BossPattern { name = name, streams = streams };

    static PatternSlot Slot(string name, int minPhase, int pose, float tell, float cooldown, BossPattern pattern, bool fire = false)
        => new PatternSlot { name = name, pattern = pattern, minPhase = minPhase, pose = pose, tell = tell, cooldown = cooldown, fireFrame = fire };

    static PatternSlot Legacy(string name, string kind, int minPhase, float est, float tell)
        => new PatternSlot { name = name, legacy = name, legacyKind = kind, est = est, minPhase = minPhase, tell = tell };

    static void Build()
    {
        all = new PatternSlot[Bosses][];
        all[0] = Space();
        all[1] = new PatternSlot[0];   // (filled in by the boss commits)
        all[2] = new PatternSlot[0];   // (filled in by the boss commits)
        all[3] = new PatternSlot[0];   // (filled in by the boss commits)
        all[4] = new PatternSlot[0];   // (filled in by the boss commits)
        all[5] = new PatternSlot[0];   // (filled in by the boss commits)
        all[6] = new PatternSlot[0];   // (filled in by the boss commits)
    }
}
