using UnityEngine;

// THE SIGNATURE BULLET PATTERNS: three per boss, themed to its world, IN ADDITION to the boss's own rotation (its lasers, vines,
// flames and waves all stay). Phase 1 unlocks the first, phase 2 the second, phase 3 the third (BossSignatureTuning). The
// shapes live here; the pace (AttackRate, phase rate, the live cap) in BossSignatureTuning. Art ids reuse the boss's own
// Resources/Bosses/<Key>_attackshots.png rows (the same sprites its table uses), so a world's patterns wear its own material
// and cue colours: pink only in Space.
//
//   boss                 phase 1                    phase 2                       phase 3                  theme
//   Void Archon          neon braid (Braid)         pulsar bloom (Rose)           twin eight (Orbit)       neon weave, bloom, figure-8 release
//   Hoarfrost Leviathan  glacier halo (Halo)        blizzard vortex (Tornado)     icicle volley (AccelFan) ice ring, whiteout swirl, slow-then-fast shards
//   Bloom Queen          vine weave (Sine)          bloom rose (Rose, 5 petals)   petal whirl (Arms)      creeping vines, a flower, spinning petals
//   Cinder Drake         fire whirl (Tornado)       heat wave (WaveWall)          ember ram (AccelFan)     fire tornado, shimmering heat, slag slugs
//   Iron Kraken          tidal swell (WaveWall)     riptide braid (Braid)         whirlpool (Orbit)        swells, currents, a maelstrom that flings
//   Tempest Engine       cyclone (Tornado)          lightning zig (Sine)          storm eye (Halo)         cyclone, forked lightning, the eye
//   Dune-Eater           sand serpent (Sine)        dust devil (Tornado)          dune surge (WaveWall)    slithering sand, devil, dune swell
public static class BossSignatureTables
{
    public const int Bosses = 7;
    static PatternSlot[][] all;

    public static PatternSlot[] For(int world)
    {
        if (all == null) Build();
        return all[Mathf.Clamp(world, 0, Bosses - 1)];
    }

    static string[] E(params string[] names) => names;

    static PatternSlot S(string name, int phase, int pose, float tell, PatternStream stream, params PatternStream[] more)
    {
        var streams = new PatternStream[1 + more.Length];
        streams[0] = stream;
        for (int i = 0; i < more.Length; i++) streams[i + 1] = more[i];
        return new PatternSlot { name = name, pattern = new BossPattern { name = name, streams = streams }, minPhase = phase, pose = pose, tell = tell, cooldown = 1.3f, fireFrame = true };
    }

    static void Build()
    {
        const BossShotStyle B = BossShotStyle.Bolt, H = BossShotStyle.Shard;
        all = new PatternSlot[Bosses][];
        // ---- Space: the Void Archon
        all[0] = new[]
        {
            S("neon braid", 1, 0, 1.0f, new PatternStream { kind = StreamKind.Braid, emitters = E("Core"), duration = 4.2f, interval = .4f, count = 1, speed = 3.3f, amp = .85f, hz = .45f, phaseDeg = 90f, spin = 8f, art = 3, style = B }),
            S("pulsar bloom", 2, 1, 1.0f, new PatternStream { kind = StreamKind.Rose, emitters = E("Core"), duration = 4.4f, interval = 1.45f, count = 26, petals = 3f, gap = 58f, spread = 30f, rotStep = 18f, speed = 3.4f, art = 4, style = H }),
            S("twin eight", 3, 2, 1.0f, new PatternStream { kind = StreamKind.Orbit, emitters = E("PodL", "PodR"), duration = 4.6f, interval = 1.5f, count = 6, orbitR = 1.1f, hz = .42f, holdSeconds = 1.3f, speed = 3.4f, art = 2, style = H }),
        };
        // ---- Frost: the Hoarfrost Leviathan
        all[1] = new[]
        {
            S("glacier halo", 1, 2, 1.0f, new PatternStream { kind = StreamKind.Halo, emitters = E("Crown"), duration = 3.8f, interval = 1.1f, count = 30, gap = 60f, spread = 40f, rotStep = 80f, speed = 2.7f, art = 3, style = H }),
            S("blizzard vortex", 2, 2, 1.0f, new PatternStream { kind = StreamKind.Tornado, emitters = E("Crown"), duration = 4.2f, interval = .24f, count = 3, armDeg = 120f, spin = 70f, turnDeg = 38f, turnSeconds = 1.6f, gain = 1.25f, speed = 2.6f, art = 3, style = H }),
            S("icicle volley", 3, 0, 1.0f, new PatternStream { kind = StreamKind.AccelFan, emitters = E("Jaw"), duration = 4.4f, interval = 1.15f, count = 6, spread = 60f, speed = 5.2f, gain = 3.2f, turnSeconds = 1.5f, art = 1, style = B }),
        };
        // ---- Verdant: the Bloom Queen
        all[2] = new[]
        {
            S("vine weave", 1, 0, 1.0f, new PatternStream { kind = StreamKind.Sine, emitters = E("Stinger"), duration = 4.4f, interval = .4f, count = 1, speed = 3.0f, amp = .8f, hz = .5f, art = 1, style = B }),
            S("bloom rose", 2, 1, 1.0f, new PatternStream { kind = StreamKind.Rose, emitters = E("Stinger"), duration = 4.4f, interval = 1.4f, count = 30, petals = 5f, gap = 58f, spread = 30f, rotStep = 12f, speed = 3.0f, art = 2, style = H }),
            S("petal whirl", 3, 1, 1.0f, new PatternStream { kind = StreamKind.Arms, emitters = E("PetalUL", "PetalUR"), duration = 4.6f, interval = .17f, count = 3, spin = 38f, reverseAt = 2.2f, speed = 2.7f, art = 2, style = H }),
        };
        // ---- Ember: the Cinder Drake
        all[3] = new[]
        {
            S("fire whirl", 1, 1, 1.0f, new PatternStream { kind = StreamKind.Tornado, emitters = E("Furnace"), duration = 4.0f, interval = .24f, count = 4, armDeg = 90f, spin = 60f, turnDeg = 42f, turnSeconds = 1.5f, gain = 1.3f, speed = 2.7f, art = 2, style = H }),
            S("heat wave", 2, 0, 1.0f, new PatternStream { kind = StreamKind.WaveWall, emitters = E("Jaw"), duration = 4.4f, interval = 1.25f, pitch = .55f, gap = 1.8f, gapStep = 1.0f, amp = .38f, hz = .7f, spread = 70f, speed = 2.9f, art = 3, style = B }),
            S("ember ram", 3, 1, 1.0f, new PatternStream { kind = StreamKind.AccelFan, emitters = E("Furnace"), duration = 4.4f, interval = 1.1f, count = 6, spread = 54f, speed = 5.5f, gain = 3.5f, turnSeconds = 1.5f, art = 1, style = H }),
        };
        // ---- Tide: the Iron Kraken
        all[4] = new[]
        {
            S("tidal swell", 1, 0, 1.0f, new PatternStream { kind = StreamKind.WaveWall, emitters = E("Beak"), duration = 4.4f, interval = 1.25f, pitch = .55f, gap = 1.7f, gapStep = 1.0f, amp = .4f, hz = .6f, spread = 60f, speed = 3.0f, art = 3, style = H }),
            S("riptide braid", 2, 1, 1.0f, new PatternStream { kind = StreamKind.Braid, emitters = E("LeftA", "RightA"), alternate = true, duration = 4.4f, interval = .36f, count = 1, speed = 3.3f, amp = .85f, hz = .5f, spin = 6f, art = 1, style = B }),
            S("whirlpool", 3, 2, 1.0f, new PatternStream { kind = StreamKind.Orbit, emitters = E("Beak"), duration = 4.6f, interval = 1.6f, count = 7, orbitR = 1.2f, hz = .5f, holdSeconds = 1.4f, speed = 3.5f, art = 3, style = H }),
        };
        // ---- Storm: the Tempest Engine
        all[5] = new[]
        {
            S("cyclone", 1, 0, 1.0f, new PatternStream { kind = StreamKind.Tornado, emitters = E("Core"), duration = 4.2f, interval = .2f, count = 3, armDeg = 120f, spin = 85f, turnDeg = 45f, turnSeconds = 1.5f, gain = 1.35f, speed = 2.9f, art = 3, style = B }),
            S("lightning zig", 2, 1, 1.0f, new PatternStream { kind = StreamKind.Sine, emitters = E("Rods"), duration = 4.4f, interval = .42f, count = 2, spread = 20f, speed = 3.6f, amp = .75f, hz = .9f, art = 1, style = B }),
            S("storm eye", 3, 2, 1.0f, new PatternStream { kind = StreamKind.Halo, emitters = E("Core"), duration = 4.0f, interval = .95f, count = 34, gap = 52f, spread = 45f, rotStep = 95f, speed = 3.0f, art = 2, style = H }),
        };
        // ---- Desert: the Dune-Eater
        all[6] = new[]
        {
            S("sand serpent", 1, 0, 1.0f, new PatternStream { kind = StreamKind.Sine, emitters = E("Maw"), duration = 4.4f, interval = .3f, count = 1, speed = 3.0f, amp = .85f, hz = .42f, spin = 8f, art = 1, style = H }),
            S("dust devil", 2, 0, 1.0f, new PatternStream { kind = StreamKind.Tornado, emitters = E("Maw"), duration = 4.2f, interval = .2f, count = 4, armDeg = 90f, spin = 75f, turnDeg = 50f, turnSeconds = 1.6f, gain = 1.3f, speed = 3.0f, art = 2, style = B }),
            S("dune surge", 3, 1, 1.0f, new PatternStream { kind = StreamKind.WaveWall, emitters = E("Boom"), duration = 4.4f, interval = 1.15f, pitch = .55f, gap = 1.7f, gapStep = 1.1f, amp = .42f, hz = .75f, spread = 65f, speed = 3.2f, art = 2, style = H }),
        };
    }
}
