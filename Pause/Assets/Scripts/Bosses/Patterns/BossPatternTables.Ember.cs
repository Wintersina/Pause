using UnityEngine;

// Ember: its rotation (see BossPatternTables).
public static partial class BossPatternTables
{
    // ------------------------------------------------------------ 4. Cinder Drake: magma bombs, fire
    // (v3 redraw: the Furnace muzzle moved from the brow to the chest and the bot's bomb-burst hits fell 25 -> 14, putting the Drake
    //  more than a hit a minute under the Bloom Queen (BossLadderBotTest). The fans 3.3 / 3.4 -> 3.6 / 3.7 and the bursts' shards
    //  2.3 / 2.4 -> 2.6 / 2.7 give it back its bite without a single extra bullet; BossLadder.Density .62 -> .64 does the rest.)
    // (Nov 28: that barely moved the bot, 6.4 -> 6.5 a minute against the Bloom Queen's 8.6 -- speed is what the greedy bot dodges
    //  best. The bombs now land closer round the ship, minSep 1.8 -> 1.5 (rings burst .9 u off it, not 1.08: tighter arcs when
    //  they reach it), the cinder spit flies 4.9 -> 5.2 (under the slag storm's 5.4, the fastest), Density .64 -> .66 (shots/s
    //  ~6.6, under the Kraken's 6.9; peak live ~47 vs its 44, inside the ladder's 10 %). Still no extra bullet per volley.
    //  Measured (CI 46110935846): 6.5 -> 7.1 a minute, magma bombs 3 -> 11 hits: the bombs' closeness is the lever that bites, but
    //  still short of 7.6 (Bloom Queen 8.6 less 1). So minSep 1.5 -> 1.3 (rings .78 u off the ship) and Density .66 -> .67,
    //  aiming at ~8 a minute, between the Bloom Queen's 8.6 - 1 and the Kraken's 8.3 + 1.)
    // (The magma bombs' burst rings fly as Ember's painted cinders, attackshots row 1 (burstArt = 1), not the shared shard.)
    static PatternSlot[] Ember()
    {
        return new[]
        {
            Slot("fire breath", 1, 0, 0.75f, 1.1f, P("fire breath", new PatternStream
            { kind = StreamKind.Fan, emitters = E("Jaw"), duration = 1.6f, interval = .5f, count = 7, spread = 110f, amp = 6f, speed = 3.6f, art = 3, style = BossShotStyle.Bolt, rail = BossRailMode.Bounce, bounces = 1 }), true),
            Slot("cinder spit", 1, 1, 1.0f, 1.2f, P("cinder spit", new PatternStream
            { kind = StreamKind.Aimed, emitters = E("Furnace"), duration = 1.1f, interval = .36f, count = 3, spread = 24f, speed = 5.2f, art = 1, style = BossShotStyle.Shard })),
            Slot("magma bombs", 2, 1, .9f, 1.3f, P("magma bombs", new PatternStream
            { kind = StreamKind.Bomb, emitters = E("Furnace"), duration = 4.6f, interval = 1.6f, count = 2, minSep = 1.3f, fuse = 1.9f, burst = 8, burstSpeed = 2.6f, burstArt = 1, rotStep = 22f, art = 2, style = BossShotStyle.Shard })),
            Legacy("jaw flamethrower", "beam", 3, 2.2f, 0.8f),
            Legacy("eruption columns", "strike", 3, 2.4f, 1.1f),
            Slot("slag storm", 3, 1, 1.0f, 1.3f, P("slag storm",
                new PatternStream { kind = StreamKind.Bomb, emitters = E("Furnace"), duration = 4.8f, interval = 1.7f, count = 2, minSep = 1.3f, fuse = 1.8f, burst = 8, burstSpeed = 2.7f, burstArt = 1, rotStep = 18f, art = 2, style = BossShotStyle.Shard },
                new PatternStream { kind = StreamKind.Fan, emitters = E("Jaw"), start = .4f, duration = 3.8f, interval = .7f, count = 6, spread = 100f, amp = 6f, speed = 3.7f, art = 3, style = BossShotStyle.Bolt, rail = BossRailMode.Bounce, bounces = 1 },
                new PatternStream { kind = StreamKind.Aimed, emitters = E("Furnace"), start = 1.0f, duration = 3.0f, interval = 1.1f, count = 2, spread = 16f, speed = 5.4f, art = 1, style = BossShotStyle.Shard })),
        };
    }
}
