using UnityEngine;

// Storm: its rotation (see BossPatternTables).
public static partial class BossPatternTables
{
    // ------------------------------------------------------------ 6. Tempest Engine: rain and lattices
    static PatternSlot[] Storm()
    {
        return new[]
        {
            Slot("core arc fan", 1, 0, 0.7f, 1.2f, P("core arc fan", new PatternStream
            { kind = StreamKind.Fan, emitters = E("Core"), duration = 1.3f, interval = .65f, count = 5, spread = 84f, amp = 10f, speed = 2.6f, art = 1, style = BossShotStyle.Bolt, rail = BossRailMode.Bounce, bounces = 1 }), true),
            Slot("crown fan", 1, 1, .9f, 1.3f, P("crown fan", new PatternStream
            { kind = StreamKind.Fan, emitters = E("Rods"), duration = 1.2f, interval = .6f, count = 6, spread = 100f, amp = 6f, speed = 2.9f, art = 2, style = BossShotStyle.Shard })),
            Slot("storm rain", 2, 1, 1.0f, 1.3f, P("storm rain", new PatternStream
            { kind = StreamKind.Rain, emitters = E("Rods"), duration = 4.6f, interval = .27f, count = 4, pitch = .75f, free = 3, speed = 3.7f, speedJitter = 1.8f, art = 2, style = BossShotStyle.Shard })),
            Legacy("coil beams", "beam", 2, 2.3f, 1f),
            Slot("lightning lattice", 3, 2, 1.0f, 1.3f, P("lightning lattice", new PatternStream
            { kind = StreamKind.Lattice, emitters = E("Core"), duration = 5.2f, interval = .3f, count = 4, pitch = 2.5f, angle = 10f, speed = 3.4f, art = 3, style = BossShotStyle.Bolt })),
            Slot("thunderhead", 3, 1, 1.0f, 1.3f, P("thunderhead",
                new PatternStream { kind = StreamKind.Rain, emitters = E("Rods"), duration = 5.0f, interval = .26f, count = 4, pitch = .75f, free = 3, speed = 3.7f, speedJitter = 1.8f, art = 2, style = BossShotStyle.Shard },
                new PatternStream { kind = StreamKind.Aimed, emitters = E("Core"), start = .8f, duration = 3.8f, interval = .42f, count = 4, spread = 24f, speed = 6.0f, art = 1, style = BossShotStyle.Bolt },
                new PatternStream { kind = StreamKind.Fan, emitters = E("CoilL", "CoilR"), start = 1.2f, duration = 3.4f, interval = .85f, count = 4, spread = 60f, speed = 3.0f, art = 3, style = BossShotStyle.Bolt })),
            Slot("cage storm", 3, 2, 1.0f, 1.3f, P("cage storm",
                new PatternStream { kind = StreamKind.Lattice, emitters = E("Core"), duration = 5.4f, interval = .42f, count = 5, pitch = 2.5f, angle = 0f, speed = 3.6f, art = 3, style = BossShotStyle.Bolt },
                new PatternStream { kind = StreamKind.Rain, emitters = E("Rods"), start = 1.0f, duration = 4.0f, interval = .3f, count = 4, pitch = .75f, free = 3, speed = 3.9f, speedJitter = 1.4f, art = 2, style = BossShotStyle.Shard },
                new PatternStream { kind = StreamKind.Aimed, emitters = E("CoilL", "CoilR"), start = 1.5f, duration = 3.6f, interval = .6f, count = 3, spread = 16f, speed = 6.0f, art = 1, style = BossShotStyle.Bolt, rail = BossRailMode.Bounce, bounces = 1 })),
        };
    }
}
