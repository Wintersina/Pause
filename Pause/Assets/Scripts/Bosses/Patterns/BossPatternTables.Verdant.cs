using UnityEngine;

// Verdant: its rotation (see BossPatternTables).
public static partial class BossPatternTables
{
    // ------------------------------------------------------------ 3. Bloom Queen: seeds that home, vines
    static PatternSlot[] Verdant()
    {
        return new[]
        {
            Slot("stinger thorns", 1, 0, 0.8f, 1.1f, P("stinger thorns", new PatternStream
            { kind = StreamKind.Aimed, emitters = E("Stinger"), duration = 1.5f, interval = .24f, count = 3, spread = 14f, speed = 4.1f, art = 1, style = BossShotStyle.Bolt }), true),
            Legacy("vine lash", "lash", 1, 2.6f, 1f),
            Slot("spore seeds", 2, 1, 1.0f, 1.2f, P("spore seeds", new PatternStream
            { kind = StreamKind.Seeds, emitters = E("PetalL", "PetalR"), duration = 4.2f, interval = .65f, count = 5, spread = 80f, speed = 2.4f, homeDeg = 70f, homeSeconds = 1.7f, art = 2, style = BossShotStyle.Shard })),
            Legacy("trunk toss", "roll", 3, 3.0f, 1.2f),
            Slot("overgrowth", 3, 1, 1.0f, 1.2f, P("overgrowth",
                new PatternStream { kind = StreamKind.Seeds, emitters = E("PetalUL", "PetalUR"), duration = 4.4f, interval = .75f, count = 4, spread = 80f, speed = 2.4f, homeDeg = 80f, homeSeconds = 1.8f, art = 2, style = BossShotStyle.Shard },
                new PatternStream { kind = StreamKind.Fan, emitters = E("PetalL", "PetalR"), start = .5f, duration = 3.8f, interval = .8f, count = 5, spread = 100f, amp = 8f, speed = 2.9f, art = 1, style = BossShotStyle.Bolt, rail = BossRailMode.Bounce, bounces = 1 })),
            Slot("thorn storm", 3, 0, .9f, 1.2f, P("thorn storm",
                new PatternStream { kind = StreamKind.Aimed, emitters = E("Stinger"), duration = 4.4f, interval = .27f, count = 3, spread = 16f, speed = 5.1f, art = 1, style = BossShotStyle.Bolt },
                new PatternStream { kind = StreamKind.Seeds, emitters = E("PetalL", "PetalR"), start = .6f, duration = 3.6f, interval = 1.0f, count = 3, spread = 60f, speed = 2.4f, homeDeg = 90f, homeSeconds = 1.8f, art = 2, style = BossShotStyle.Shard },
                new PatternStream { kind = StreamKind.Fan, emitters = E("PetalUL", "PetalUR"), start = 1.0f, duration = 3.0f, interval = .8f, count = 5, spread = 110f, amp = 7f, speed = 2.7f, art = 3, style = BossShotStyle.Shard })),
        };
    }
}
