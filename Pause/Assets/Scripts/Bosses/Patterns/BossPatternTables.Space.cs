using UnityEngine;

// Space: its rotation (see BossPatternTables).
public static partial class BossPatternTables
{
    // ------------------------------------------------------------ 1. Void Archon: neon pulsar, spirals
    static PatternSlot[] Space()
    {
        return new[]
        {
            Slot("chin cannon", 1, 0, 0.9f, 1.2f, P("chin cannon", new PatternStream
            { kind = StreamKind.Aimed, emitters = E("Chin"), duration = .97f, interval = .32f, count = 1, speed = 4.4f, art = 1, style = BossShotStyle.Bolt }), true),
            Slot("ion arcs", 1, 2, .9f, 1.3f, P("ion arcs", new PatternStream
            { kind = StreamKind.Aimed, emitters = E("PodL", "PodR"), duration = 1.3f, interval = .65f, count = 2, spread = 12f, speed = 3.8f, art = 2, style = BossShotStyle.Shard })),
            Slot("pulsar spiral", 2, 1, 1.0f, 1.3f, P("pulsar spiral", new PatternStream
            { kind = StreamKind.Spiral, emitters = E("Core"), duration = 3.8f, interval = .12f, count = 2, armDeg = 180f, spin = 62f, speed = 2.8f, art = 3, style = BossShotStyle.Shard })),
            Legacy("pod lasers", "beam", 3, 2.0f, 0.95f),
            Slot("mirror sweep", 3, 2, 1.0f, 1.3f, P("mirror sweep", new PatternStream
            { kind = StreamKind.Spiral, emitters = E("PodL", "PodR"), mirror = true, duration = 3.6f, interval = .145f, count = 2, armDeg = 34f, amp = 52f, period = 3.2f, speed = 3.0f, art = 4, style = BossShotStyle.Bolt })),
        };
    }
}
