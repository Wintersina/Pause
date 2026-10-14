using UnityEngine;

// Ember: its rotation (see BossPatternTables).
public static partial class BossPatternTables
{
    // ------------------------------------------------------------ 4. Cinder Drake: magma bombs, fire
    static PatternSlot[] Ember()
    {
        return new[]
        {
            Slot("fire breath", 1, 0, 0.75f, 1.1f, P("fire breath", new PatternStream
            { kind = StreamKind.Fan, emitters = E("Jaw"), duration = 1.6f, interval = .5f, count = 7, spread = 110f, amp = 6f, speed = 3.3f, style = BossShotStyle.Bolt, rail = BossRailMode.Bounce, bounces = 1 }), true),
            Slot("cinder spit", 1, 1, 1.0f, 1.2f, P("cinder spit", new PatternStream
            { kind = StreamKind.Aimed, emitters = E("Furnace"), duration = 1.1f, interval = .36f, count = 3, spread = 24f, speed = 4.9f, style = BossShotStyle.Shard })),
            Slot("magma bombs", 2, 1, .9f, 1.3f, P("magma bombs", new PatternStream
            { kind = StreamKind.Bomb, emitters = E("Furnace"), duration = 4.6f, interval = 1.6f, count = 2, minSep = 1.8f, fuse = 1.9f, burst = 8, burstSpeed = 2.3f, rotStep = 22f, style = BossShotStyle.Shard })),
            Legacy("brow laser", "beam", 3, 2.2f, 0.8f),
            Legacy("eruption columns", "strike", 3, 2.4f, 1.1f),
            Slot("slag storm", 3, 1, 1.0f, 1.3f, P("slag storm",
                new PatternStream { kind = StreamKind.Bomb, emitters = E("Furnace"), duration = 4.8f, interval = 1.7f, count = 2, minSep = 1.8f, fuse = 1.8f, burst = 8, burstSpeed = 2.4f, rotStep = 18f, style = BossShotStyle.Shard },
                new PatternStream { kind = StreamKind.Fan, emitters = E("Jaw"), start = .4f, duration = 3.8f, interval = .7f, count = 6, spread = 100f, amp = 6f, speed = 3.4f, style = BossShotStyle.Bolt, rail = BossRailMode.Bounce, bounces = 1 },
                new PatternStream { kind = StreamKind.Aimed, emitters = E("Furnace"), start = 1.0f, duration = 3.0f, interval = 1.1f, count = 2, spread = 16f, speed = 5.4f, style = BossShotStyle.Shard })),
        };
    }
}
