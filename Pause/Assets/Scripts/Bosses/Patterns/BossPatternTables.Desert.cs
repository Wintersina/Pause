using UnityEngine;

// Desert: its rotation (see BossPatternTables).
public static partial class BossPatternTables
{
    // ------------------------------------------------------------ 7. Dune-Eater: mines and the closing jaws
    static PatternSlot[] Desert()
    {
        return new[]
        {
            Slot("grit fan", 1, 0, 0.65f, 1.2f, P("grit fan", new PatternStream
            { kind = StreamKind.Fan, emitters = E("Maw"), duration = 1.9f, interval = .6f, count = 7, spread = 96f, amp = 10f, speed = 2.6f, art = 1, style = BossShotStyle.Shard, rail = BossRailMode.Bounce, bounces = 1 }), true),
            Slot("dune fan", 1, 1, .9f, 1.3f, P("dune fan", new PatternStream
            { kind = StreamKind.Fan, emitters = E("Boom"), duration = 1.7f, interval = .55f, count = 7, spread = 120f, amp = 5f, speed = 3.0f, art = 2, style = BossShotStyle.Bolt })),
            Slot("buried mines", 2, 1, 1.0f, 1.3f, P("buried mines",
                new PatternStream { kind = StreamKind.Mines, emitters = E("Boom"), duration = 4.6f, interval = 9f, count = 9, gap = 1.4f, minSep = 1.3f, fuse = 4.6f, art = 3, style = BossShotStyle.Shard },
                new PatternStream { kind = StreamKind.Aimed, emitters = E("Maw"), start = .8f, duration = 3.6f, interval = .45f, count = 2, spread = 12f, speed = 4.4f, art = 2, style = BossShotStyle.Bolt })),
            Legacy("lens beam", "beam", 2, 2.3f, 0.9f),
            Slot("closing jaws", 3, 0, 1.0f, 1.3f, P("closing jaws", new PatternStream
            { kind = StreamKind.Pincer, emitters = E("Maw"), duration = 4.6f, interval = .4f, count = 4, spread = 18f, gap = 2.4f, gapStep = .3f, side = 1.5f, speed = 6.4f, art = 1, style = BossShotStyle.Bolt })),
            Slot("quicksand", 3, 1, 1.0f, 1.3f, P("quicksand",
                new PatternStream { kind = StreamKind.Mines, emitters = E("Boom"), duration = 4.8f, interval = 9f, count = 9, gap = 1.4f, minSep = 1.3f, fuse = 4.8f, art = 3, style = BossShotStyle.Shard },
                new PatternStream { kind = StreamKind.Pincer, emitters = E("Maw"), start = .8f, duration = 4.0f, interval = .42f, count = 4, spread = 18f, gap = 2.2f, gapStep = .3f, side = 1.5f, speed = 6.4f, art = 1, style = BossShotStyle.Bolt })),
            Slot("dune-eater's maw", 3, 0, 1.0f, 1.3f, P("dune-eater's maw",
                new PatternStream { kind = StreamKind.Mines, emitters = E("Boom"), duration = 5.2f, interval = 9f, count = 9, gap = 1.4f, minSep = 1.3f, fuse = 5.2f, art = 3, style = BossShotStyle.Shard },
                new PatternStream { kind = StreamKind.Pincer, emitters = E("Maw"), start = .6f, duration = 4.6f, interval = .38f, count = 4, spread = 18f, gap = 2.2f, gapStep = .3f, side = 1.5f, speed = 6.4f, art = 1, style = BossShotStyle.Bolt },
                new PatternStream { kind = StreamKind.Fan, emitters = E("Maw"), start = 1.2f, duration = 3.6f, interval = .7f, count = 6, spread = 100f, amp = 8f, speed = 3.0f, art = 1, style = BossShotStyle.Shard, rail = BossRailMode.Bounce, bounces = 1 },
                new PatternStream { kind = StreamKind.Aimed, emitters = E("Lens"), start = 2.0f, duration = 3.0f, interval = .9f, count = 2, spread = 10f, speed = 6.4f, art = 2, style = BossShotStyle.Bolt })),
        };
    }
}
