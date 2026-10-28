using UnityEngine;

// Verdant: its rotation (see BossPatternTables).

// THE BLOOM QUEEN'S SHOT WIGGLE, tuned down (user feedback: "wiggles the attacks too much"). Where it came from and what each knob was:
//   homing seeds (spore seeds / overgrowth / thorn storm): turned 70 / 80 / 90 deg/s toward the ship for ~1.8 s with no easing, so a seed
//     that reached the ship whipped from side to side (and its sprite spun with it)            -> now 25 / 28 / 32 deg/s (~36 %), eased, stops near the ship
//   fan tilt (overgrowth thorns amp 8, thorn storm gourds amp 7): the fan rocks +-amp every volley, a zigzag of tilted fans -> now 2.8 / 2.4 (~35 %)
//   art flip: the two drawings swapped every 2 ticks (12 swaps a second, a shimmer); frame b is now the same body with only the core
//     brightening, swapped every 6 ticks (4 a second)
// Damage, rates, counts, speeds and the roster are untouched. Wave bullets (art 1 walls / fans) fly straight; their gap sweep is the
// song's, and is left alone (BossWaves).
public static class VerdantShotTuning
{
    public const float OldHomeSpore = 70f, OldHomeOvergrowth = 80f, OldHomeStorm = 90f, OldFanAmpOvergrowth = 8f, OldFanAmpStorm = 7f;
    public const int OldFlipTicks = 2;
    public const float HomeSpore = 25f, HomeOvergrowth = 28f, HomeStorm = 32f;
    public const float FanAmpOvergrowth = 2.8f, FanAmpStorm = 2.4f;
    public const float HomeEaseDeg = 12f;        // the turn eases out over the last 12 degrees to the ship
    public const float HomeStopDist = .9f;       // a seed this close to the ship stops steering
    public static readonly int[] FlipTicks = { 6, 6 };
}
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
            { kind = StreamKind.Seeds, emitters = E("PetalL", "PetalR"), duration = 4.2f, interval = .65f, count = 5, spread = 80f, speed = 2.4f, homeDeg = VerdantShotTuning.HomeSpore, homeSeconds = 1.7f, art = 2, style = BossShotStyle.Shard })),
            Legacy("trunk toss", "roll", 3, 3.0f, 1.2f),
            Slot("overgrowth", 3, 1, 1.0f, 1.2f, P("overgrowth",
                new PatternStream { kind = StreamKind.Seeds, emitters = E("PetalUL", "PetalUR"), duration = 4.4f, interval = .75f, count = 4, spread = 80f, speed = 2.4f, homeDeg = VerdantShotTuning.HomeOvergrowth, homeSeconds = 1.8f, art = 2, style = BossShotStyle.Shard },
                new PatternStream { kind = StreamKind.Fan, emitters = E("PetalL", "PetalR"), start = .5f, duration = 3.8f, interval = .8f, count = 5, spread = 100f, amp = VerdantShotTuning.FanAmpOvergrowth, speed = 2.9f, art = 1, style = BossShotStyle.Bolt, rail = BossRailMode.Bounce, bounces = 1 })),
            Slot("thorn storm", 3, 0, .9f, 1.2f, P("thorn storm",
                new PatternStream { kind = StreamKind.Aimed, emitters = E("Stinger"), duration = 4.4f, interval = .27f, count = 3, spread = 16f, speed = 5.1f, art = 1, style = BossShotStyle.Bolt },
                new PatternStream { kind = StreamKind.Seeds, emitters = E("PetalL", "PetalR"), start = .6f, duration = 3.6f, interval = 1.0f, count = 3, spread = 60f, speed = 2.4f, homeDeg = VerdantShotTuning.HomeStorm, homeSeconds = 1.8f, art = 2, style = BossShotStyle.Shard },
                new PatternStream { kind = StreamKind.Fan, emitters = E("PetalUL", "PetalUR"), start = 1.0f, duration = 3.0f, interval = .8f, count = 5, spread = 110f, amp = VerdantShotTuning.FanAmpStorm, speed = 2.7f, art = 3, style = BossShotStyle.Shard })),
        };
    }
}
