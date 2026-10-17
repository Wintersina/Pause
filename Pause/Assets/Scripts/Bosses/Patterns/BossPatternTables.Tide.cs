using UnityEngine;

// Tide: its rotation (see BossPatternTables).
public static partial class BossPatternTables
{
    // ------------------------------------------------------------ 5. Iron Kraken: tidal walls
    static PatternSlot[] Tide()
    {
        return new[]
        {
            Slot("port cluster", 1, 1, 0.7f, 1.2f, P("port cluster", new PatternStream
            { kind = StreamKind.Fan, emitters = E("LeftA", "LeftB"), alternate = true, duration = 1.85f, interval = .45f, count = 7, spread = 80f, amp = 8f, speed = 3f, art = 1, style = BossShotStyle.Bolt, rail = BossRailMode.Bounce, bounces = 1 }), true),
            Legacy("beak jet", "beam", 1, 2.0f, 0.9f),
            Slot("tidal wall", 2, 0, 0.9f, 1.3f, P("tidal wall", new PatternStream
            { kind = StreamKind.Curtain, emitters = E("Beak"), duration = 4.2f, interval = .6f, gap = 2.3f, gapStep = 1.3f, pitch = .5f, speed = 3.2f, art = 3, style = BossShotStyle.Shard })),
            Slot("starboard cluster", 2, 2, .8f, 1.3f, P("starboard cluster", new PatternStream
            { kind = StreamKind.Fan, emitters = E("RightA", "RightB"), alternate = true, duration = 2.1f, interval = .6f, count = 8, spread = 100f, amp = 8f, speed = 3.2f, art = 2, style = BossShotStyle.Shard })),
            Legacy("surf wave", "wave", 3, 3.4f, 1.1f),
            Slot("riptide", 3, 0, 1.0f, 1.3f, P("riptide",
                new PatternStream { kind = StreamKind.Curtain, emitters = E("Beak"), duration = 5.0f, interval = .62f, gap = 2.1f, gapStep = 1.5f, pitch = .5f, speed = 3.5f, art = 3, style = BossShotStyle.Shard },
                new PatternStream { kind = StreamKind.Aimed, emitters = E("RightA", "LeftA"), start = .5f, duration = 3.8f, interval = .45f, count = 3, spread = 16f, speed = 5.7f, art = 1, style = BossShotStyle.Bolt })),
            Slot("maelstrom", 3, 0, 1.0f, 1.3f, P("maelstrom",
                new PatternStream { kind = StreamKind.Curtain, emitters = E("Beak"), duration = 5.4f, interval = .8f, gap = 2.0f, gapStep = 1.5f, pitch = .5f, speed = 3.2f, art = 3, style = BossShotStyle.Shard },
                new PatternStream { kind = StreamKind.Fan, emitters = E("LeftA", "RightA"), alternate = true, start = .3f, duration = 4.6f, interval = .55f, count = 4, spread = 60f, amp = 8f, speed = 3.2f, art = 1, style = BossShotStyle.Bolt },
                new PatternStream { kind = StreamKind.Aimed, emitters = E("Beak"), start = 1.0f, duration = 4.0f, interval = .7f, count = 4, spread = 20f, speed = 5.7f, art = 1, style = BossShotStyle.Bolt })),
        };
    }
}
