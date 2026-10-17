using UnityEngine;

// Frost: its rotation (see BossPatternTables).
public static partial class BossPatternTables
{
    // ------------------------------------------------------------ 2. Hoarfrost Leviathan: rings with a gap, bouncing ice
    static PatternSlot[] Frost()
    {
        return new[]
        {
            Slot("icicle spray", 1, 0, 0.85f, 1.2f, P("icicle spray", new PatternStream
            { kind = StreamKind.Fan, emitters = E("Jaw"), duration = 1.05f, interval = .5f, count = 6, spread = 84f, amp = 9f, speed = 3f, art = 1, style = BossShotStyle.Bolt, rail = BossRailMode.Bounce, bounces = 2 }), true),
            Slot("cold snap", 1, 2, 1.0f, 1.3f, P("cold snap", new PatternStream
            { kind = StreamKind.Ring, emitters = E("Crown"), duration = 2.7f, interval = .9f, count = 20, gap = 64f, spread = 26f, rotStep = 10f, speed = 2.8f, art = 3, style = BossShotStyle.Shard })),
            Legacy("glare beams", "beam", 2, 2.0f, 0.85f),
            Slot("shatter", 3, 0, .95f, 1.3f, P("shatter",
                new PatternStream { kind = StreamKind.Fan, emitters = E("Jaw"), duration = 2.2f, interval = .55f, count = 7, spread = 150f, amp = 8f, speed = 3.4f, art = 1, style = BossShotStyle.Bolt, rail = BossRailMode.Bounce, bounces = 3 },
                new PatternStream { kind = StreamKind.Aimed, emitters = E("EyeL", "EyeR"), start = .6f, duration = 2.4f, interval = .8f, count = 2, spread = 12f, speed = 4.8f, art = 2, style = BossShotStyle.Shard })),
            Slot("frozen crown", 3, 2, 1.0f, 1.3f, P("frozen crown",
                new PatternStream { kind = StreamKind.Ring, emitters = E("Crown"), duration = 4.0f, interval = .85f, count = 24, gap = 56f, spread = 28f, rotStep = 7f, speed = 2.7f, art = 3, style = BossShotStyle.Shard },
                new PatternStream { kind = StreamKind.Fan, emitters = E("Jaw"), start = 1.0f, duration = 2.6f, interval = 1.3f, count = 5, spread = 60f, amp = 6f, speed = 3.0f, art = 1, style = BossShotStyle.Bolt, rail = BossRailMode.Bounce, bounces = 2 })),
        };
    }
}
